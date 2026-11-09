using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The Shattered Mirror of Malice: third boss, in the Glass Temple beyond the Thornwood
    /// (host-authoritative, same scheme as the dragon: the server picks moves, every client poses
    /// the frame and the giant hands from the replicated move + start time, see MirrorMoves).
    ///
    /// Locomotion: it hovers and drags itself on its hands; spins on its magic axis to strike behind.
    /// Phase 1 "the Glass Guardian": Dark Slam (L/R hand, shock + debris), Crushing Embrace (the
    /// hands clap together in front of the glass), Circular Sweep (the frame spins, a hand razes the
    /// ground all around), Reflection of Malice (phantoms that slow or invert controls), Shard Hail.
    /// Phase 2 "the Awakening" (50%): the glass cracks through, smoke and floating shards, faster
    /// hands with black-fire wakes, and the Soul Vortex (pulls everyone toward the glass).
    /// After heavy blows it rests its hands on the ground: the moment to strike the base.
    /// </summary>
    public class MirrorAI : EnemyBase
    {
        public static MirrorAI Instance { get; private set; }
        public static event System.Action Awakened;   // phase 2 (all clients)

        public struct MoveState : INetworkSerializeByMemcpy
        {
            public byte move;
            public double start;
            public float p1, p2, rate;
        }

        [Header("Mirror")]
        public float perPlayerScaling = 0.6f;
        [Range(0, 1)] public float phase2Threshold = 0.5f;
        public float glassHeight = 4.2f;

        [Header("Damage")]
        public float slamDamage = 36f, slamRadius = 3.6f;
        public float embraceDamage = 40f;
        public float sweepDamage = 28f;
        public float shardDamage = 16f, shardRadius = 1.7f;
        public float phantomDamage = 10f;
        public float vortexTick = 12f, vortexRadius = 3.4f, implodeDamage = 30f, implodeRadius = 6.5f, vortexPull = 4.6f;
        public float glideDamage = 18f;

        readonly NetworkVariable<MoveState> _netMove = new NetworkVariable<MoveState>();
        readonly NetworkVariable<bool> _netActive = new NetworkVariable<bool>(false);
        readonly NetworkVariable<byte> _netPhase = new NetworkVariable<byte>(1);

        public MirrorMove CurrentMove => (MirrorMove)_netMove.Value.move;
        public float MoveRate => _netMove.Value.rate > 0f ? _netMove.Value.rate : 1f;
        /// <summary>Move time already scaled by the move's rate (what MirrorMoves expects).</summary>
        public float MoveTime => CurrentMove == MirrorMove.None ? 0f : (float)(Now - _netMove.Value.start) * MoveRate;
        public MirrorPose CurrentPose => MirrorMoves.Evaluate(CurrentMove, MoveTime, _netMove.Value.p1, _netMove.Value.p2);
        public bool FightActive => _netActive.Value;
        public int Phase => _netPhase.Value;
        public float LocoSpeed => netLocoSpeed.Value;
        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        bool P2 => _netPhase.Value >= 2;

        public override bool IsStaggered => CurrentMove == MirrorMove.Stagger;
        protected override bool IsInvulnerable => !_netActive.Value || CurrentMove == MirrorMove.Awaken || CurrentMove == MirrorMove.Awakening;
        protected override bool IsInvulnerableVisual => !_netActive.Value;
        protected override float GruntChance => 0f;
        protected override float CurseHeight => 7.5f;

        readonly Queue<MirrorMove> _queue = new Queue<MirrorMove>();
        readonly HashSet<ulong> _hitOnce = new HashSet<ulong>();
        readonly Dictionary<MirrorMove, float> _cooldown = new Dictionary<MirrorMove, float>();
        readonly List<(double time, Vector3 pos)> _shards = new List<(double, Vector3)>();
        float _recover, _nextTick;
        bool _resolved, _resolved2;
        int _hailIndex;
        float _groundY;
        Vector3 _lastSweepHand;
        int _lastSlamSide;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            knockbackFactor = 0f;
            stanceImmunity = 10f;
            anim = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            _groundY = homePosition.y;
            var lot = GetComponent<LockOnTarget>();
            if (lot) lot.enabled = _netActive.Value && IsAlive;
            _netActive.OnValueChanged += OnActiveChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _netActive.OnValueChanged -= OnActiveChanged;
            if (Instance == this) Instance = null;
        }

        void OnActiveChanged(bool a, bool b) { var l = GetComponent<LockOnTarget>(); if (l) l.enabled = b && IsAlive; }

        // ================================================================== arena control (server)
        public void Activate(int playerCount)
        {
            if (!IsServer) return;
            SetupStats(1f + (Mathf.Max(1, playerCount) - 1) * perPlayerScaling);
            _netPhase.Value = 1;
            _netActive.Value = true;
            _queue.Clear(); _shards.Clear(); _cooldown.Clear();
            target = PickTarget();
            if (target) transform.rotation = Quaternion.LookRotation(MathUtil.Flat(target.transform.position - transform.position));
            SetMove(MirrorMove.Awaken);
            Debug.Log($"[DF] The Mirror wakes for {playerCount} player(s) → HP {netMaxHealth.Value:0}");
        }

        public void ResetMirror()
        {
            if (!IsServer) return;
            _queue.Clear(); _shards.Clear();
            ResetToHome(1f);
            _netActive.Value = false;
            _netPhase.Value = 1;
            SetMove(MirrorMove.None);
        }

        // ================================================================== moves
        void SetMove(MirrorMove m, float p1 = 0f, float p2 = 0f)
        {
            float rate = P2 && m != MirrorMove.Awakening && m != MirrorMove.Death && m != MirrorMove.Stagger ? 1.22f : 1f;
            _netMove.Value = new MoveState { move = (byte)m, start = Now, p1 = p1, p2 = p2, rate = rate };
            _resolved = _resolved2 = false;
            _hitOnce.Clear();
            _hailIndex = 0;
            _nextTick = 0f;
            if (m != MirrorMove.None) netLocoSpeed.Value = 0f;
            if (m != MirrorMove.None && m != MirrorMove.Death) MoveStartRpc((byte)m, p1, p2);
        }

        void EndMove(MirrorMove m)
        {
            if (_queue.Count > 0) { StartMove(_queue.Dequeue()); return; }
            SetMove(MirrorMove.None);
            // after the heavy blows the hands rest on the ground for a moment: the opening
            bool heavy = m == MirrorMove.SlamL || m == MirrorMove.SlamR || m == MirrorMove.Embrace || m == MirrorMove.Vortex;
            _recover = m == MirrorMove.Stagger ? 0.4f : heavy ? (P2 ? 0.9f : 1.5f) : (P2 ? 0.45f : 0.9f);
        }

        void StartMove(MirrorMove m)
        {
            if (m == MirrorMove.SlamL || m == MirrorMove.SlamR) { var (x, z) = SlamTarget(m == MirrorMove.SlamL); SetMove(m, x, z); }
            else if (m == MirrorMove.Glide) SetMove(m, Random.value < 0.5f ? -1f : 1f);
            else SetMove(m);
        }

        /// <summary>The slam lands where the target stands (body space), within reach of that hand.</summary>
        (float, float) SlamTarget(bool left)
        {
            Vector3 local = target ? transform.InverseTransformPoint(target.transform.position) : new Vector3(left ? -3f : 3f, 0f, 4f);
            Vector3 shoulder = new Vector3(left ? -2.9f : 2.9f, 0f, 0.3f);
            Vector3 d = new Vector3(local.x, 0f, local.z) - shoulder;
            if (d.magnitude > 6.6f) d = d.normalized * 6.6f;
            if (d.magnitude < 1.8f) d = d.normalized * 1.8f;
            Vector3 p = shoulder + d;
            p.z = Mathf.Max(p.z, 0.8f);
            return (p.x, p.z);
        }

        bool Ready(MirrorMove m) => !_cooldown.TryGetValue(m, out var c) || c <= 0f;
        void Cool(MirrorMove m, float s) => _cooldown[m] = s;

        // ================================================================== FSM
        protected override void ServerTick(float dt)
        {
            UpdateShards();
            if (!_netActive.Value) { StopMoving(dt); return; }
            if (_cooldown.Count > 0) foreach (var k in new List<MirrorMove>(_cooldown.Keys)) _cooldown[k] -= dt;
            if (target == null || target.IsDead || !InArena(target)) target = PickTarget();

            var m = CurrentMove;
            if (m != MirrorMove.None)
            {
                float t = MoveTime;
                UpdateMove(m, t, dt);
                if (CurrentMove == m && t >= MirrorMoves.Duration(m)) EndMove(m);
                return;
            }
            if (_recover > 0f) { _recover -= dt; StopMoving(dt); if (target) FaceTowards(target.transform.position, dt, 0.5f); return; }
            if (target == null) { StopMoving(dt); return; }
            if (_queue.Count > 0) { StartMove(_queue.Dequeue()); return; }
            Decide(dt);
        }

        void Decide(float dt)
        {
            Vector3 to = target.transform.position - transform.position;
            float d = MathUtil.Flat(to).magnitude;
            float ang = Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to));

            // circling players get the sweep (or the frame just spins to face them)
            int around = 0;
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && InArena(p) && Vector3.Distance(p.transform.position, transform.position) < MirrorMoves.SweepRadius + 1.5f
                    && Mathf.Abs(MathUtil.SignedAngleY(transform.forward, p.transform.position - transform.position)) > 80f) around++;
            if (around > 0 && Ready(MirrorMove.Sweep) && Random.value < 0.7f) { Cool(MirrorMove.Sweep, 6f); StartMove(MirrorMove.Sweep); return; }

            if (P2 && Ready(MirrorMove.Vortex) && Random.value < 0.35f && d < 16f) { Cool(MirrorMove.Vortex, 22f); StartMove(MirrorMove.Vortex); return; }

            if (d < 8.2f && ang < 60f)
            {
                float r = Random.value;
                if (d < 6.5f && r < 0.25f && Ready(MirrorMove.Embrace)) { Cool(MirrorMove.Embrace, 7f); StartMove(MirrorMove.Embrace); return; }
                if (r < 0.4f && Ready(MirrorMove.Sweep)) { Cool(MirrorMove.Sweep, 6f); StartMove(MirrorMove.Sweep); return; }
                // slam with the hand on the target's side (alternating when centred)
                float side = MathUtil.SignedAngleY(transform.forward, to);
                bool left = Mathf.Abs(side) < 12f ? _lastSlamSide > 0 : side < 0f;
                _lastSlamSide = left ? -1 : 1;
                StartMove(left ? MirrorMove.SlamL : MirrorMove.SlamR);
                if (P2 && Random.value < 0.5f) _queue.Enqueue(left ? MirrorMove.SlamR : MirrorMove.SlamL);   // the other hand follows
                return;
            }
            if (d >= 8.2f && ang < 50f)
            {
                float r = Random.value;
                if (r < 0.35f && Ready(MirrorMove.ShardHail)) { Cool(MirrorMove.ShardHail, P2 ? 9f : 12f); StartMove(MirrorMove.ShardHail); return; }
                if (r < 0.6f && Ready(MirrorMove.Reflection)) { Cool(MirrorMove.Reflection, P2 ? 10f : 13f); StartMove(MirrorMove.Reflection); return; }
                if (d > 13f && Ready(MirrorMove.Glide)) { Cool(MirrorMove.Glide, 5f); StartMove(MirrorMove.Glide); return; }
            }
            // drag itself toward the target, spinning on its axis to face it
            FaceTowards(target.transform.position, dt, ang > 50f ? 1.6f : 0.9f);
            if (d > 6f && ang < 70f) MoveTowards(to, P2 ? 2.8f : 2.2f, dt);
            else StopMoving(dt);
        }

        // ================================================================== world positions of the hands
        Vector3 BodyToWorld(Vector3 local, MirrorPose pose)
        {
            Vector3 l = Quaternion.Euler(0f, pose.bodyYaw, 0f) * local;
            return transform.TransformPoint(l + Vector3.up * pose.hover);
        }

        public Vector3 GlassCenter => transform.position + Vector3.up * (glassHeight + CurrentPose.hover) + transform.forward * 0.4f;
        Vector3 VortexCenter => new Vector3(transform.position.x, _groundY, transform.position.z) + transform.forward * 3.2f;

        void UpdateMove(MirrorMove m, float t, float dt)
        {
            var pose = CurrentPose;
            switch (m)
            {
                case MirrorMove.SlamL:
                case MirrorMove.SlamR:
                {
                    bool left = m == MirrorMove.SlamL;
                    if (!_resolved2 && t >= 0.4f)
                    {
                        _resolved2 = true;
                        var hit = MirrorMoves.Evaluate(m, MirrorMoves.SlamImpact, _netMove.Value.p1, _netMove.Value.p2);
                        Vector3 at = BodyToWorld(left ? hit.handL : hit.handR, hit); at.y = _groundY;
                        WarnRpc(at, slamRadius, (MirrorMoves.SlamImpact - t) / MoveRate);
                    }
                    if (!_resolved && t >= MirrorMoves.SlamImpact)
                    {
                        _resolved = true;
                        Vector3 at = BodyToWorld(left ? pose.handL : pose.handR, pose); at.y = _groundY;
                        HitCircle(at, slamRadius, slamDamage, HitFlags.Heavy | HitFlags.AreaOfEffect);
                        SlamRpc(at, slamRadius, P2);
                    }
                    break;
                }
                case MirrorMove.Embrace:
                    if (!_resolved2 && t >= 0.35f) { _resolved2 = true; EmbraceWarnRpc((MirrorMoves.EmbraceClap - t) / MoveRate); }
                    if (!_resolved && t >= MirrorMoves.EmbraceClap)
                    {
                        _resolved = true;
                        foreach (var p in PlayerController.All)
                        {
                            if (!p || p.IsDead) continue;
                            Vector3 l = transform.InverseTransformPoint(p.transform.position);
                            if (Mathf.Abs(l.x) < 6.2f && l.z > 1.2f && l.z < 7.8f && Mathf.Abs(l.y) < 4f) DeliverHit(p, embraceDamage, HitFlags.Heavy | HitFlags.Unblockable, true);
                        }
                        ClapRpc(BodyToWorld(new Vector3(0f, 2.4f, 4.8f), pose));
                    }
                    break;
                case MirrorMove.Sweep:
                {
                    Vector3 hand = BodyToWorld(pose.handR, pose);
                    if (!_resolved2) { _resolved2 = true; SweepWarnRpc(new Vector3(transform.position.x, _groundY, transform.position.z), (MirrorMoves.SweepStart - t) / MoveRate); _lastSweepHand = hand; }
                    if (t >= MirrorMoves.SweepStart && t <= MirrorMoves.SweepEnd + 0.05f)
                    {
                        Vector3 a = _lastSweepHand, b = hand;
                        foreach (var p in PlayerController.All)
                        {
                            if (!p || p.IsDead || _hitOnce.Contains(p.NetworkObjectId)) continue;
                            if (Mathf.Abs(p.transform.position.y - _groundY) > 2.2f) continue;
                            if (DistToSegmentFlat(p.transform.position, a, b) < 2.1f) { _hitOnce.Add(p.NetworkObjectId); DeliverHit(p, sweepDamage, HitFlags.Heavy, true); }
                        }
                    }
                    _lastSweepHand = hand;
                    break;
                }
                case MirrorMove.Reflection:
                    if (!_resolved && t >= MirrorMoves.ReflectFire)
                    {
                        _resolved = true;
                        int n = 0;
                        foreach (var p in PlayerController.All)
                        {
                            if (!p || p.IsDead || !InArena(p) || n >= 4) continue;
                            byte kind = (byte)(Random.value < 0.5f ? 1 : 2);
                            for (int k = 0; k < (P2 ? 2 : 1); k++)
                            {
                                Vector3 from = GlassCenter + transform.right * Random.Range(-1.6f, 1.6f) + Vector3.up * Random.Range(-1f, 1.4f);
                                Vector3 dir = (p.transform.position + Vector3.up - from).normalized;
                                dir = Quaternion.Euler(Random.Range(-20f, 5f), Random.Range(-35f, 35f), 0f) * dir;
                                PhantomRpc(from, dir * 7.5f, p.NetworkObject, kind);
                            }
                            n++;
                        }
                    }
                    break;
                case MirrorMove.ShardHail:
                {
                    int total = P2 ? 22 : 15;
                    float span = MirrorMoves.HailEnd - MirrorMoves.HailStart;
                    while (_hailIndex < total && t >= MirrorMoves.HailStart + span * _hailIndex / total)
                    {
                        _hailIndex++;
                        Vector3 to = HailPoint();
                        Vector3 from = GlassCenter + new Vector3(Random.Range(-2f, 2f), Random.Range(0f, 3f), 0f);
                        float flight = 0.95f;
                        _shards.Add((Now + flight, to));
                        HailRpc(from, to, flight);
                    }
                    break;
                }
                case MirrorMove.Vortex:
                {
                    Vector3 c = VortexCenter;
                    if (!_resolved2 && t >= MirrorMoves.VortexOpen)
                    {
                        _resolved2 = true;
                        VortexRpc(c, (MirrorMoves.VortexImplode - t) / MoveRate);
                    }
                    if (t >= MirrorMoves.VortexOpen && t < MirrorMoves.VortexImplode && t >= _nextTick)
                    {
                        _nextTick = t + 0.5f;
                        foreach (var p in PlayerController.All)
                            if (p && !p.IsDead && MathUtil.Flat(p.transform.position - c).magnitude < vortexRadius && Mathf.Abs(p.transform.position.y - _groundY) < 3f)
                                DeliverHit(p, vortexTick, HitFlags.Unblockable, true);
                    }
                    if (!_resolved && t >= MirrorMoves.VortexImplode)
                    {
                        _resolved = true;
                        foreach (var p in PlayerController.All)
                            if (p && !p.IsDead && MathUtil.Flat(p.transform.position - c).magnitude < implodeRadius && Mathf.Abs(p.transform.position.y - _groundY) < 3f)
                                DeliverHit(p, implodeDamage, HitFlags.Heavy | HitFlags.AreaOfEffect, true);
                        ImplodeRpc(c, implodeRadius);
                    }
                    break;
                }
                case MirrorMove.Glide:
                {
                    if (t < 0.15f || t > 1.0f) break;
                    Vector3 dir = target ? MathUtil.Flat(target.transform.position - transform.position).normalized : transform.forward;
                    dir = (dir + transform.right * _netMove.Value.p1 * 0.6f).normalized;
                    FaceTowards(transform.position + dir, dt, 2f);
                    cc.Move(dir * 11f * dt);
                    foreach (var p in PlayerController.All)
                        if (p && !p.IsDead && !_hitOnce.Contains(p.NetworkObjectId) && MathUtil.Flat(p.transform.position - transform.position).magnitude < 3f)
                        { _hitOnce.Add(p.NetworkObjectId); DeliverHit(p, glideDamage, HitFlags.None, true); }
                    break;
                }
                case MirrorMove.Awaken:
                    if (!_resolved && t >= 2.45f) { _resolved = true; Vector3 c = transform.position + transform.forward * 3.4f; c.y = _groundY; HitCircle(c, 6f, 14f, HitFlags.AreaOfEffect); SlamRpc(c, 6f, false); }
                    break;
                case MirrorMove.Awakening:
                    if (!_resolved && t >= 1.2f) { _resolved = true; AwakeningRpc(); }
                    break;
            }
        }

        Vector3 HailPoint()
        {
            var alive = new List<PlayerController>();
            foreach (var p in PlayerController.All) if (p && !p.IsDead && InArena(p)) alive.Add(p);
            Vector3 c;
            if (alive.Count > 0 && Random.value < 0.6f)
            {
                var p = alive[Random.Range(0, alive.Count)];
                c = p.transform.position + Random.insideUnitSphere * 3.5f;
            }
            else
            {
                var lv = LevelRefs.Instance;
                Vector3 ac = lv ? lv.mirrorArena.center : transform.position;
                c = ac + Random.insideUnitSphere * 15f;
            }
            c.y = _groundY;
            return c;
        }

        void UpdateShards()
        {
            for (int i = _shards.Count - 1; i >= 0; i--)
            {
                if (Now < _shards[i].time) continue;
                Vector3 at = _shards[i].pos;
                _shards.RemoveAt(i);
                foreach (var p in PlayerController.All)
                    if (p && !p.IsDead && MathUtil.Flat(p.transform.position - at).magnitude < shardRadius && Mathf.Abs(p.transform.position.y - at.y) < 2.5f)
                        DeliverProjectileHit(p, shardDamage, 18f, HitFlags.AreaOfEffect);
            }
        }

        void HitCircle(Vector3 c, float r, float damage, HitFlags flags)
        {
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && MathUtil.Flat(p.transform.position - c).magnitude < r + 0.4f && Mathf.Abs(p.transform.position.y - c.y) < 2.5f)
                    DeliverHit(p, damage, flags, true);
        }

        static float DistToSegmentFlat(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            Vector3 ab = b - a;
            float k = ab.sqrMagnitude < 1e-5f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * k);
        }

        bool InArena(PlayerController p) => LevelRefs.Instance == null || LevelRefs.Instance.IsInsideMirrorArena(p.transform.position);

        PlayerController PickTarget()
        {
            PlayerController best = null; float bd = float.MaxValue;
            foreach (var p in PlayerController.All)
            {
                if (!p || p.IsDead || !InArena(p)) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        // ================================================================== damage hooks
        protected override void OnDamaged(Vector3 attackerPos)
        {
            if (!P2 && Health01 <= phase2Threshold && IsAlive)
            {
                _netPhase.Value = 2;
                _queue.Clear();
                SetMove(MirrorMove.Awakening);
                _queue.Enqueue(MirrorMove.Vortex);
                Cool(MirrorMove.Vortex, 22f);
            }
        }

        protected override void Stagger(float duration)
        {
            var m = CurrentMove;
            if (m == MirrorMove.Awaken || m == MirrorMove.Awakening || m == MirrorMove.Vortex) return;
            _queue.Clear();
            SetMove(MirrorMove.Stagger);
        }

        protected override int SoulReward => 2600;
        protected override bool IsBossReward => true;

        protected override void Die()
        {
            _queue.Clear(); _shards.Clear();
            SetMove(MirrorMove.Death);
            base.Die();
            DeathRpc();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DevMove(string m) { if (IsServer && System.Enum.TryParse<MirrorMove>(m, true, out var mv)) { if (target == null) target = PickTarget(); StartMove(mv); } }
        public void DevHealth(string frac) { if (IsServer && float.TryParse(frac, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) { netHealth.Value = netMaxHealth.Value * Mathf.Clamp01(f); OnDamaged(transform.position); } }
#endif

        // ================================================================== client fx
        [Rpc(SendTo.Everyone)]
        void MoveStartRpc(byte move, float p1, float p2)
        {
            var mv = (MirrorMove)move;
            Vector3 g = transform.position + Vector3.up * glassHeight;
            switch (mv)
            {
                case MirrorMove.Awaken: Sfx.Play(SfxId.Roar, g, 1.6f, 0.5f); Sfx.Play(SfxId.MirrorShatter, g, 1f, 0.6f); break;
                case MirrorMove.Reflection: Sfx.Play(SfxId.MirrorHum, g, 1.3f, 1.2f); break;
                case MirrorMove.ShardHail: Sfx.Play(SfxId.MirrorHum, g, 1.4f, 0.8f); break;
                case MirrorMove.Vortex: Sfx.Play(SfxId.MirrorVortex, g, 1.6f, 0.7f); break;
                case MirrorMove.Glide: Sfx.Play(SfxId.DragonSwoop, g, 1.2f, 0.7f); MirrorFx.ShardBurst(g, 10, 4f, 0.6f); break;
                case MirrorMove.Embrace: Sfx.Play(SfxId.DragonWing, g, 1.2f, 0.6f); break;
                case MirrorMove.SlamL: case MirrorMove.SlamR: Sfx.Play(SfxId.Leap, g, 1.2f, 0.6f); break;
                case MirrorMove.Sweep: Sfx.Play(SfxId.DragonTail, g, 1.3f, 0.6f); break;
            }
        }

        [Rpc(SendTo.Everyone)] void WarnRpc(Vector3 at, float r, float t) => BossFx.GroundWarning(at, r, Mathf.Max(0.2f, t), MirrorFx.Void);

        [Rpc(SendTo.Everyone)]
        void SlamRpc(Vector3 at, float r, bool darkFire)
        {
            Sfx.Play(SfxId.MirrorSlam, at, 1.6f, Random.Range(0.75f, 0.9f));
            Sfx.Play(SfxId.Quake, at, 1f, 0.7f);
            MirrorFx.DarkShock(at, r);
            BossFx.Debris(at, r * 0.5f, 30);
            BossFx.RingWave(at, r + 1.5f, 18f, false);
            SoulsCamera.Shake(0.07f, 0.45f);
            if (darkFire) MirrorFx.DarkFire(at, r * 0.6f, 4f);
        }

        [Rpc(SendTo.Everyone)]
        void EmbraceWarnRpc(float t)
        {
            for (int i = -2; i <= 2; i++)
                BossFx.GroundWarning(transform.TransformPoint(new Vector3(i * 2.4f, 0f, 4.5f)), 2f, Mathf.Max(0.3f, t), MirrorFx.Void);
        }

        [Rpc(SendTo.Everyone)]
        void ClapRpc(Vector3 at)
        {
            Sfx.Play(SfxId.MirrorSlam, at, 1.8f, 1.1f);
            Sfx.Play(SfxId.Parry, at, 1.2f, 0.5f);
            MirrorFx.DarkShock(at - Vector3.up * 2f, 4f);
            SoulsCamera.Shake(0.08f, 0.4f);
        }

        [Rpc(SendTo.Everyone)]
        void SweepWarnRpc(Vector3 center, float t)
        {
            SpellFx.GroundRing(center + Vector3.up * 0.05f, MirrorMoves.SweepRadius + 1.5f, MirrorMoves.SweepRadius + 1.6f, MirrorFx.Void, Mathf.Max(0.4f, t) + 1f, 0.7f);
            BossFx.GroundWarning(center, MirrorMoves.SweepRadius + 1.2f, Mathf.Max(0.3f, t), MirrorFx.Void);
        }

        [Rpc(SendTo.Everyone)]
        void PhantomRpc(Vector3 from, Vector3 vel, NetworkObjectReference victim, byte kind)
        {
            Transform t = victim.TryGet(out NetworkObject no) ? no.transform : null;
            Sfx.Play(SfxId.MirrorCurse, from, 0.8f, Random.Range(1.1f, 1.4f));
            GlassShard.Fire(GlassShard.Kind.Phantom, from, vel, this, IsServer, phantomDamage, 6f, t, kind, kind == 1 ? 4f : 5f);
        }

        [Rpc(SendTo.Everyone)]
        void HailRpc(Vector3 from, Vector3 to, float flight)
        {
            BossFx.GroundWarning(to, shardRadius, flight, MirrorFx.Pale);
            MirrorFx.ArcShard(from, to, flight, 1.4f);
            if (Random.value < 0.4f) Sfx.Play(SfxId.ArrowImpact, from, 0.6f, 1.8f);
        }

        [Rpc(SendTo.Everyone)]
        void VortexRpc(Vector3 center, float duration)
        {
            MirrorFx.VortexSwirl(center + Vector3.up * 0.3f, 9f, duration);
            BossFx.GroundWarning(center, vortexRadius, duration, MirrorFx.Void);
            PlayerController.BeginVortexPull(center, duration, vortexPull);
            SoulsCamera.Shake(0.03f, duration);
        }

        [Rpc(SendTo.Everyone)]
        void ImplodeRpc(Vector3 c, float r)
        {
            Sfx.Play(SfxId.MirrorShatter, c, 1.8f, 0.7f);
            Sfx.Play(SfxId.Quake, c, 1.2f, 0.6f);
            MirrorFx.DarkShock(c, r);
            MirrorFx.ShardBurst(c + Vector3.up, 30, 9f, 1.2f);
            SoulsCamera.Shake(0.1f, 0.7f);
        }

        [Rpc(SendTo.Everyone)]
        void AwakeningRpc()
        {
            Vector3 g = transform.position + Vector3.up * glassHeight;
            Sfx.Play(SfxId.MirrorShatter, g, 2f, 0.5f);
            Sfx.Play(SfxId.Roar, g, 1.6f, 0.4f);
            MirrorFx.ShardBurst(g, 40, 7f, 1.2f);
            MirrorFx.DarkShock(transform.position, 8f);
            SoulsCamera.Shake(0.09f, 1.6f);
            Awakened?.Invoke();
        }

        [Rpc(SendTo.Everyone)]
        void DeathRpc()
        {
            Vector3 g = transform.position + Vector3.up * glassHeight;
            Sfx.Play(SfxId.MirrorShatter, g, 2f, 0.45f);
            Sfx.Play(SfxId.Roar, g, 1.4f, 0.35f);
            MirrorFx.ShardBurst(g, 80, 10f, 1.5f);
            MirrorFx.DarkShock(transform.position, 10f);
            SoulsCamera.Shake(0.08f, 2.5f);
        }
    }
}
