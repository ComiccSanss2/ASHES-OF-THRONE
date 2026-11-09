using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Vaelthrax, the Cinder Wyrm: second boss, on the castle terrace (host-authoritative).
    /// Moves (timings in DragonMoves, shared with the visuals):
    ///  - Bite / Double Bite (P2): front of the jaws, heavy.
    ///  - Tail Sweep: punishes players standing at its flanks or behind it.
    ///  - Fire Breath: a stream that sweeps across the front cone, ticking damage (roll through or run).
    ///  - Stomp: rears up and crushes the ground in front, shock ring.
    ///  - Charge: runs at far targets.
    ///  - Phase 2 (55% HP): roar, takes off, rains fireballs (marked on the ground), lands with a slam.
    /// HP scales with the number of players (same rule as the Ashen King).
    /// </summary>
    public class DragonAI : EnemyBase
    {
        public static DragonAI Instance { get; private set; }

        public struct MoveState : INetworkSerializeByMemcpy
        {
            public byte move;
            public double start;
            public float p1, p2;
        }

        [Header("Dragon")]
        public float scale = 0.75f;
        [Tooltip("Metres from the dragon's origin to its jaws in the rest pose (measured by the builder).")]
        public float headForward = 3.4f;
        [Tooltip("Height of the jaws in the rest pose.")]
        public float headHeight = 3.6f;
        public float perPlayerScaling = 0.6f;
        [Range(0, 1)] public float phase2Threshold = 0.55f;
        public float flyHeight = 9f;
        public float flightInterval = 38f;

        [Header("Damage")]
        public float biteDamage = 34f, sweepDamage = 28f, stompDamage = 30f, chargeDamage = 32f, pounceDamage = 40f;
        Vector3 _pounceFrom, _pounceTo, _pounceMark;
        public float breathTickDamage = 9f, breathRange = 15f, breathHalfAngle = 15f;
        public float fireballDamage = 26f, fireballRadius = 2.8f, fireballFlight = 1.25f;
        public float landDamage = 30f, landRadius = 7f;

        readonly NetworkVariable<MoveState> _netMove = new NetworkVariable<MoveState>();
        readonly NetworkVariable<bool> _netActive = new NetworkVariable<bool>(false);
        readonly NetworkVariable<byte> _netPhase = new NetworkVariable<byte>(1);
        readonly NetworkVariable<ulong> _netTarget = new NetworkVariable<ulong>(ulong.MaxValue);

        // ---------------------------------------------------------------- public (all clients)
        public DragonMove CurrentMove => (DragonMove)_netMove.Value.move;
        public float MoveTime => CurrentMove == DragonMove.None ? 0f : (float)(Now - _netMove.Value.start);
        public DragonPose CurrentPose => DragonMoves.Evaluate(CurrentMove, MoveTime, _netMove.Value.p1, _netMove.Value.p2);
        public float LocoSpeed => netLocoSpeed.Value;
        public bool IsAliveVisual => IsAlive;
        public bool FightActive => _netActive.Value;
        public int Phase => _netPhase.Value;
        public PlayerController VisualTarget
        {
            get
            {
                ulong id = _netTarget.Value;
                if (id == ulong.MaxValue) return null;
                foreach (var p in PlayerController.All) if (p && p.NetworkObjectId == id) return p;
                return null;
            }
        }

        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        public override bool IsStaggered => CurrentMove == DragonMove.Stagger;
        protected override bool IsInvulnerable => !_netActive.Value || CurrentMove == DragonMove.Awaken || (_transition && CurrentMove == DragonMove.Roar);
        protected override bool IsInvulnerableVisual => !_netActive.Value;
        protected override float GruntPitch => 0.45f;
        protected override float GruntChance => 0f;

        // ---------------------------------------------------------------- server state
        readonly Queue<DragonMove> _queue = new Queue<DragonMove>();
        readonly HashSet<ulong> _hitOnce = new HashSet<ulong>();
        readonly List<(double time, Vector3 pos)> _fireballs = new List<(double, Vector3)>();
        readonly Dictionary<DragonMove, float> _cooldown = new Dictionary<DragonMove, float>();
        float _recover, _nextBreathTick, _nextFireball, _sinceFlight, _sinceTail = 99f;
        bool _resolved, _resolved2, _flying, _transition;
        int _fireballIndex;
        float _groundY;
        Vector3 _flyFrom;
        DragonVisuals _visuals;

        float S => scale;
        bool P2 => _netPhase.Value >= 2;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            knockbackFactor = 0f;
            stanceImmunity = 9f;
            anim = null;
            _visuals = GetComponent<DragonVisuals>();
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
            playerCount = Mathf.Max(1, playerCount);
            SetupStats(1f + (playerCount - 1) * perPlayerScaling);
            _netPhase.Value = 1;
            _netActive.Value = true;
            _queue.Clear();
            _sinceFlight = 0f;
            target = PickTarget();
            if (target) transform.rotation = Quaternion.LookRotation(MathUtil.Flat(target.transform.position - transform.position));
            SetMove(DragonMove.Awaken);
            Debug.Log($"[DF] Dragon awakened for {playerCount} player(s) → HP {netMaxHealth.Value:0}");
        }

        public void ResetDragon()
        {
            if (!IsServer) return;
            _queue.Clear(); _fireballs.Clear();
            _flying = false; _transition = false;
            ResetToHome(1f);
            _netActive.Value = false;
            _netPhase.Value = 1;
            _netTarget.Value = ulong.MaxValue;
            SetMove(DragonMove.None);
        }

        // ================================================================== moves
        void SetMove(DragonMove m, float p1 = 0f, float p2 = 0f)
        {
            _netMove.Value = new MoveState { move = (byte)m, start = Now, p1 = p1, p2 = p2 };
            _resolved = _resolved2 = false;
            _hitOnce.Clear();
            _nextBreathTick = DragonMoves.BreathStart + 0.1f;
            _nextFireball = 0.6f;
            if (m != DragonMove.None) netLocoSpeed.Value = 0f;
            if (m == DragonMove.Roar || m == DragonMove.Awaken) RoarRpc();
            if (m == DragonMove.Bite || m == DragonMove.DoubleBite) SnarlRpc();
            // readable tells even from under its belly: a glint on the jaws, the arc of the tail, the claw's mark
            if (m == DragonMove.Bite || m == DragonMove.DoubleBite) TellRpc(0, HeadPoint(0.8f), 0f, DragonMoves.BiteHit);
            else if (m == DragonMove.TailSweep) TellRpc(1, transform.position, 10f * S, DragonMoves.SweepSpinStart);
            else if (m == DragonMove.Stomp) TellRpc(2, transform.position + transform.forward * 2f * S, 2.8f, DragonMoves.StompHit);
        }

        void EndMove(DragonMove m)
        {
            if (m == DragonMove.Roar) _transition = false;
            if (_queue.Count > 0) { StartQueued(); return; }
            SetMove(DragonMove.None);
            _recover = m == DragonMove.Stagger ? 0.4f : P2 ? Random.Range(0.35f, 0.8f) : Random.Range(0.6f, 1.2f);
            if (m == DragonMove.Bite || m == DragonMove.DoubleBite || m == DragonMove.Stomp || m == DragonMove.TailSweep) _recover += P2 ? 0.45f : 0.8f;
        }

        void StartQueued()
        {
            var m = _queue.Dequeue();
            if (m == DragonMove.Breath) { var (a, b) = BreathSweep(); SetMove(m, a, b); }
            else SetMove(m);
        }

        (float, float) BreathSweep()
        {
            float aim = target ? Mathf.Clamp(MathUtil.SignedAngleY(transform.forward, target.transform.position - transform.position), -30f, 30f) : 0f;
            float side = Random.value < 0.5f ? -1f : 1f;
            return (aim - 38f * side, aim + 38f * side);
        }

        bool Ready(DragonMove m) => !_cooldown.TryGetValue(m, out var c) || c <= 0f;
        void Cool(DragonMove m, float s) => _cooldown[m] = s;

        // ================================================================== FSM
        protected override void ServerTick(float dt)
        {
            UpdateFireballs();
            UpdateBurns();
            if (!_netActive.Value) { StopMoving(dt); return; }

            if (_cooldown.Count > 0) foreach (var k in new List<DragonMove>(_cooldown.Keys)) _cooldown[k] -= dt;
            if (target == null || target.IsDead || !InArena(target)) target = PickTarget();
            ulong tid = target ? target.NetworkObjectId : ulong.MaxValue;
            if (_netTarget.Value != tid) _netTarget.Value = tid;
            if (P2 && !_flying) _sinceFlight += dt;
            _sinceTail += dt;

            var m = CurrentMove;
            if (m != DragonMove.None)
            {
                float t = MoveTime;
                UpdateMove(m, t, dt);
                if (CurrentMove == m && t >= DragonMoves.Duration(m)) EndMove(m);
                return;
            }

            if (_recover > 0f) { _recover -= dt; StopMoving(dt); if (target) FaceTowards(target.transform.position, dt, 0.6f); return; }
            if (target == null) { StopMoving(dt); return; }
            if (_queue.Count > 0) { StartQueued(); return; }
            Decide(dt);
        }

        void Decide(float dt)
        {
            Vector3 to = target.transform.position - transform.position;
            float d = MathUtil.Flat(to).magnitude;
            float ang = Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to));
            float headReach = headForward;

            // second phase: periodic flights
            if (P2 && _sinceFlight > flightInterval)
            {
                _sinceFlight = 0f;
                _queue.Enqueue(DragonMove.FireRain); _queue.Enqueue(DragonMove.Land);
                SetMove(DragonMove.TakeOff);
                return;
            }
            // players hiding at the flanks / behind
            int behind = 0;
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && InArena(p) && Vector3.Distance(p.transform.position, transform.position) < 9f * S + 2f
                    && Mathf.Abs(MathUtil.SignedAngleY(transform.forward, p.transform.position - transform.position)) > 95f) behind++;
            if (behind > 0 && _sinceTail > 2.5f && Ready(DragonMove.TailSweep) && Random.value < 0.4f) { Cool(DragonMove.TailSweep, 9f); _sinceTail = 0f; SetMove(DragonMove.TailSweep); return; }

            if (d < headReach + 3.2f && ang < 40f)
            {
                float r = Random.value;
                if (r < 0.2f && Ready(DragonMove.Stomp)) { Cool(DragonMove.Stomp, 10f); SetMove(DragonMove.Stomp); return; }
                if (P2 && r < 0.65f) { SetMove(DragonMove.DoubleBite, Random.value < 0.5f ? -30f : 30f); return; }
                SetMove(DragonMove.Bite); return;
            }
            if (d < 6f * S + 2f && ang >= 40f && Ready(DragonMove.Stomp) && Random.value < 0.5f) { Cool(DragonMove.Stomp, 10f); SetMove(DragonMove.Stomp); return; }
            if (d > 7f && d < breathRange && ang < 45f && Ready(DragonMove.Breath) && Random.value < 0.6f)
            {
                Cool(DragonMove.Breath, P2 ? 7f : 10f);
                var (a, b) = BreathSweep(); SetMove(DragonMove.Breath, a, b); return;
            }
            // (the straight charge is gone) a leaping slam onto distant prey
            if (d > 10f && d < 26f && ang < 35f && Ready(DragonMove.Pounce) && Random.value < 0.55f) { Cool(DragonMove.Pounce, P2 ? 8f : 11f); SetMove(DragonMove.Pounce); return; }

            // walk toward the target
            FaceTowards(target.transform.position, dt, ang > 60f ? 1.2f : 0.8f);
            if (d > headReach + 1.5f && ang < 70f) MoveTowards(to, d > 14f ? 5.2f : P2 ? 3.8f : 3.2f, dt);
            else StopMoving(dt);
        }

        void UpdateMove(DragonMove m, float t, float dt)
        {
            if (!_flying) StopMoving(dt);
            switch (m)
            {
                case DragonMove.Awaken:
                case DragonMove.Roar:
                    if (target && t < 0.6f) FaceTowards(target.transform.position, dt, 0.7f);
                    if (!_resolved && t >= DragonMoves.RoarBlast)
                    {
                        _resolved = true;
                        foreach (var p in PlayerController.All)
                            if (p && !p.IsDead && Vector3.Distance(p.transform.position, transform.position) < 8.5f)
                                DeliverHit(p, 4f, HitFlags.Unblockable | HitFlags.AreaOfEffect, true);
                    }
                    break;

                case DragonMove.Bite:
                case DragonMove.DoubleBite:
                    if (target && t < 0.62f) FaceTowards(target.transform.position, dt, 0.9f);
                    if (!_resolved && t >= DragonMoves.BiteHit) { _resolved = true; HitSphere(HeadPoint(1.4f), 2.6f, biteDamage, HitFlags.None); }
                    if (m == DragonMove.DoubleBite)
                    {
                        if (target && t > 1.0f && t < 1.4f) FaceTowards(target.transform.position, dt, 0.9f);
                        if (!_resolved2 && t >= DragonMoves.DoubleBiteHit2) { _resolved2 = true; _hitOnce.Clear(); HitSphere(HeadPoint(2f, _netMove.Value.p1 * 0.6f), 2.8f, biteDamage * 1.1f, HitFlags.Heavy); }
                    }
                    break;

                case DragonMove.TailSweep:
                    if (!_resolved && t >= (DragonMoves.SweepSpinStart + DragonMoves.SweepSpinEnd) * 0.5f)
                    {
                        _resolved = true;
                        TailRpc();
                        foreach (var p in PlayerController.All)
                        {
                            if (!p || p.IsDead) continue;
                            Vector3 to = p.transform.position - transform.position;
                            if (Mathf.Abs(to.y) > 3f || MathUtil.Flat(to).magnitude > 10f * S + 1.5f) continue;
                            if (Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to)) < 55f) continue;
                            bool tip = MathUtil.Flat(to).magnitude > 5.5f * S;
                            DeliverHit(p, tip ? sweepDamage : sweepDamage * 0.7f, tip ? HitFlags.Heavy : HitFlags.None, true);
                        }
                    }
                    break;

                case DragonMove.Breath:
                    if (t < DragonMoves.BreathStart && target) FaceTowards(target.transform.position, dt, 0.5f);
                    if (t >= _nextBreathTick && t <= DragonMoves.BreathEnd)
                    {
                        _nextBreathTick += 0.25f;
                        var pose = CurrentPose;
                        Vector3 dir = MathUtil.Flat(Quaternion.AngleAxis(pose.neckYaw, Vector3.up) * transform.forward).normalized;
                        Vector3 mouth = HeadPoint(0f, pose.neckYaw);
                        if (((int)(t * 4f)) % 2 == 0)
                        {
                            float dist = Random.Range(6f, breathRange - 2f);
                            Vector3 at = mouth + dir * dist; at.y = _groundY;
                            if (!Physics.Linecast(mouth, at + Vector3.up, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                            {
                                float life = P2 ? BurnTime + 2f : BurnTime;
                                _burns.Add((at, Now + 0.4 + life, Now + 0.4));
                                BurnRpc(at, life);
                            }
                        }
                        foreach (var p in PlayerController.All)
                        {
                            if (!p || p.IsDead) continue;
                            Vector3 to = MathUtil.Flat(p.transform.position - mouth);
                            float d = to.magnitude;
                            if (d > breathRange || Mathf.Abs(p.transform.position.y - _groundY) > 4f) continue;
                            float half = breathHalfAngle + Mathf.Lerp(18f, 0f, Mathf.Clamp01(d / 5f));   // wider right at the mouth
                            if (d > 1f && Vector3.Angle(dir, to) > half) continue;
                            if (Physics.Linecast(mouth, p.transform.position + Vector3.up, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                            DeliverHit(p, breathTickDamage, HitFlags.Unblockable, true);
                        }
                    }
                    break;

                case DragonMove.Stomp:
                    if (target && t < 0.7f) FaceTowards(target.transform.position, dt, 0.6f);
                    if (!_resolved && t >= DragonMoves.StompHit)
                    {
                        _resolved = true;
                        Vector3 c = transform.position + transform.forward * 2f * S;
                        SlamRpc(c, 5.5f);
                        HitSphere(c, 2.8f, stompDamage, HitFlags.Heavy | HitFlags.AreaOfEffect);                 // under the claw: thrown
                        HitRing(c, 2.8f, 5.5f, stompDamage * 0.55f, HitFlags.AreaOfEffect);                   // the shock: only staggered
                        if (P2)
                        {
                            // the slam cracks the floor: a line of eruptions racing toward the target
                            Vector3 dir = target ? MathUtil.Flat(target.transform.position - transform.position).normalized : transform.forward;
                            for (int i = 0; i < 4; i++)
                            {
                                Vector3 at = c + dir * (7f + i * 3.6f); at.y = _groundY;
                                float delay = 0.45f + i * 0.22f;
                                _fireballs.Add((Now + delay, at));
                                EruptRpc(at, delay);
                            }
                        }
                    }
                    break;

                case DragonMove.Pounce:
                {
                    if (t < DragonMoves.PounceLock) { if (target) FaceTowards(target.transform.position, dt, 1.2f); break; }
                    if (!_resolved2)
                    {
                        _resolved2 = true;
                        Vector3 aim = target ? target.transform.position : transform.position + transform.forward * 10f;
                        Vector3 dir = MathUtil.Flat(aim - transform.position);
                        float dist = Mathf.Clamp(dir.magnitude, 4f, 18f);
                        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
                        _pounceFrom = transform.position;
                        // the body lands so that its chest comes down on the marked spot
                        _pounceTo = transform.position + dir * Mathf.Max(0f, dist - headForward * 0.45f);
                        if (Physics.SphereCast(transform.position + Vector3.up * 1.5f, 1.4f, dir, out var wall, (_pounceTo - _pounceFrom).magnitude + 4f * S, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                            _pounceTo = _pounceFrom + dir * Mathf.Max(0f, wall.distance - 4.5f * S);
                        _pounceMark = _pounceTo + dir * headForward * 0.45f;
                        TellRpc(3, _pounceMark, 5.2f * S, DragonMoves.PounceLand - t);
                    }
                    if (t < DragonMoves.PounceLand)
                    {
                        float u = Mathf.Clamp01((t - DragonMoves.PounceLock) / (DragonMoves.PounceLand - DragonMoves.PounceLock));
                        Vector3 want = Vector3.Lerp(_pounceFrom, _pounceTo, u);
                        Vector3 delta = MathUtil.Flat(want - transform.position);
                        cc.Move(delta);
                        break;
                    }
                    if (!_resolved)
                    {
                        _resolved = true;
                        SlamRpc(_pounceMark, 7f * S);
                        HitSphere(_pounceMark, 3.6f * S, pounceDamage, HitFlags.Heavy | HitFlags.AreaOfEffect);
                        HitRing(_pounceMark, 3.6f * S, 7f * S, pounceDamage * 0.5f, HitFlags.AreaOfEffect);
                    }
                    break;
                }

                case DragonMove.Charge:
                {
                    // 1) wind-up: turns to the target, paws the ground
                    if (t < DragonMoves.ChargeLock) { if (target) FaceTowards(target.transform.position, dt, 1.4f); break; }
                    // 2) the lane is locked and painted on the ground (ChargeLaneRpc): no more tracking
                    if (!_resolved2)
                    {
                        _resolved2 = true;
                        Vector3 o = transform.position + Vector3.up * 1.5f;
                        float len = ChargeSpeed * (DragonMoves.ChargeRunEnd - DragonMoves.ChargeRun) + 2f;
                        if (Physics.SphereCast(o, 1.4f, transform.forward, out var wall, len + 5.5f * S, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                            len = Mathf.Max(3f, wall.distance - 5.5f * S - 1.2f);
                        _chargeLeft = len;
                        ChargeLaneRpc(transform.position, transform.forward, len + headForward);
#if UNITY_EDITOR
                        Debug.Log($"[DF] dragon charge lane len={len:F1} from={transform.position} fwd={transform.forward}");
#endif
                    }
                    if (t < DragonMoves.ChargeRun) break;
                    // 3) the run: straight down the lane, the whole body is the hitbox
                    float speed = t < DragonMoves.ChargeRunEnd ? ChargeSpeed : Mathf.Lerp(ChargeSpeed, 0f, (t - DragonMoves.ChargeRunEnd) / 0.45f);
                    if (_chargeLeft <= 0f) speed = Mathf.Min(speed, 2f);
                    Vector3 fwd = transform.forward;
                    if (t < DragonMoves.ChargeRunEnd && Physics.SphereCast(transform.position + Vector3.up * 1.5f, 1.4f, fwd, out _, 5.5f * S, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                    {
#if UNITY_EDITOR
                        Debug.Log("[DF] dragon charge hit a wall");
#endif
                        SlamRpc(HeadPoint(0.5f), 4f); SetMove(DragonMove.Stagger); _netMove.Value = new MoveState { move = (byte)DragonMove.Stagger, start = Now - 1.4f };
                        break;
                    }
                    cc.Move(fwd * speed * dt);
                    _chargeLeft -= speed * dt;
                    netLocoSpeed.Value = speed;
                    if (speed > 4f) HitCapsule(transform.position - fwd * 2.5f * S, HeadPoint(0.6f), ChargeHalfWidth, chargeDamage, HitFlags.Heavy | HitFlags.Unblockable);
                    if (!_resolved && t >= DragonMoves.ChargeRunEnd) { _resolved = true; SkidRpc(HeadPoint(0.4f)); }
                    break;
                }

                case DragonMove.TakeOff:
                {
                    if (!_flying && t >= 0.4f)
                    {
                        _flying = true; cc.enabled = false; _flyFrom = transform.position;
                        WingBlastRpc(transform.position);
                    }
                    if (_flying)
                    {
                        float k = Mathf.SmoothStep(0f, 1f, (t - 0.4f) / (DragonMoves.TakeOffDur - 0.4f));
                        Vector3 c = ArenaCenterGround();
                        Vector3 p = Vector3.Lerp(_flyFrom, new Vector3(Mathf.Lerp(_flyFrom.x, c.x, 0.5f), _groundY, Mathf.Lerp(_flyFrom.z, c.z, 0.5f)), k);
                        p.y = _groundY + flyHeight * k;
                        transform.position = p;
                    }
                    break;
                }

                case DragonMove.FireRain:
                {
                    if (target) FaceTowards(target.transform.position, dt, 0.8f);
                    // slow drift around the centre
                    Vector3 c = ArenaCenterGround();
                    Vector3 p = transform.position;
                    Vector3 goal = c + new Vector3(Mathf.Sin(t * 0.6f) * 6f, 0f, Mathf.Cos(t * 0.6f) * 4f - 4f);
                    p = Vector3.MoveTowards(p, new Vector3(goal.x, _groundY + flyHeight + Mathf.Sin(t * 2f) * 0.5f, goal.z), dt * 4f);
                    transform.position = p;
                    if (t >= _nextFireball && t < DragonMoves.FireRainDur - 0.8f)
                    {
                        _nextFireball += P2 && Health01 < 0.3f ? 0.5f : 0.68f;
                        LaunchFireball();
                    }
                    break;
                }

                case DragonMove.Land:
                {
                    if (!_resolved2) { _resolved2 = true; _flyFrom = transform.position; }
                    if (target && t < 0.6f) FaceTowards(target.transform.position, dt, 0.8f);
                    float k = Mathf.Clamp01(t / DragonMoves.LandHit);
                    Vector3 p = _flyFrom; p.y = Mathf.Lerp(_flyFrom.y, _groundY, k * k);
                    transform.position = p;
                    if (!_resolved && t >= DragonMoves.LandHit)
                    {
                        _resolved = true;
                        _flying = false;
                        transform.position = new Vector3(p.x, _groundY + 0.05f, p.z);
                        cc.enabled = true;
                        SlamRpc(transform.position, landRadius);
                        HitSphere(transform.position, landRadius, landDamage, HitFlags.Heavy | HitFlags.AreaOfEffect);
                    }
                    break;
                }
            }
        }

        // ================================================================== fireballs
        void LaunchFireball()
        {
            var alive = new List<PlayerController>();
            foreach (var p in PlayerController.All) if (p && !p.IsDead && InArena(p)) alive.Add(p);
            if (alive.Count == 0) return;
            var tgt = alive[_fireballIndex++ % alive.Count];
            Vector3 vel = Vector3.zero;
            var cc2 = tgt.GetComponent<CharacterController>();
            if (cc2) vel = MathUtil.Flat(cc2.velocity);
            Vector3 at = tgt.transform.position + vel * 0.7f + MathUtil.Flat(Random.insideUnitSphere) * 1.8f;
            at = BossFx.Ground(at);
            var pose = CurrentPose;
            Vector3 mouth = HeadPoint(0f, pose.neckYaw);
            _fireballs.Add((Now + fireballFlight, at));
            FireballRpc(mouth, at, fireballFlight);
        }

        void UpdateFireballs()
        {
            for (int i = _fireballs.Count - 1; i >= 0; i--)
            {
                if (Now < _fireballs[i].time) continue;
                Vector3 at = _fireballs[i].pos;
                _fireballs.RemoveAt(i);
                foreach (var p in PlayerController.All)
                    if (p && !p.IsDead && Vector3.Distance(p.transform.position, at) < fireballRadius + 0.3f)
                        DeliverProjectileHit(p, fireballDamage, 40f, HitFlags.Heavy | HitFlags.AreaOfEffect | HitFlags.Unblockable);
            }
        }

        // ================================================================== helpers
        Vector3 HeadPoint(float lunge, float yaw = 0f)
        {
            Vector3 f = Quaternion.AngleAxis(yaw, Vector3.up) * transform.forward;
            return transform.position + f * (headForward + lunge * S) + Vector3.up * 1.2f;
        }

        public const float ChargeSpeed = 15f, ChargeHalfWidth = 2.3f;
        float _chargeLeft;

        /// <summary>Hits everyone within 'radius' of the segment a→b (the dragon's body while it charges).</summary>
        void HitCapsule(Vector3 a, Vector3 b, float radius, float damage, HitFlags flags)
        {
            a.y = b.y = 0f;
            foreach (var p in PlayerController.All)
            {
                if (!p || p.IsDead || _hitOnce.Contains(p.NetworkObjectId)) continue;
                if (Mathf.Abs(p.transform.position.y - _groundY) > 3.5f) continue;
                Vector3 q = p.transform.position; q.y = 0f;
                Vector3 ab = b - a;
                float k = Mathf.Clamp01(Vector3.Dot(q - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                if (Vector3.Distance(q, a + ab * k) > radius + 0.35f) continue;
                _hitOnce.Add(p.NetworkObjectId);
                DeliverHit(p, damage, flags, true);
            }
        }

        // ---- lingering fire left by the breath (P2: the ground keeps burning)
        readonly List<(Vector3 pos, double until, double nextTick)> _burns = new List<(Vector3, double, double)>();
        const float BurnRadius = 2.2f, BurnTime = 5f;

        void UpdateBurns()
        {
            for (int i = _burns.Count - 1; i >= 0; i--)
            {
                var b = _burns[i];
                if (Now > b.until) { _burns.RemoveAt(i); continue; }
                if (Now < b.nextTick) continue;
                b.nextTick = Now + 0.5;
                _burns[i] = b;
                foreach (var p in PlayerController.All)
                    if (p && !p.IsDead && Vector3.Distance(MathUtil.Flat(p.transform.position), MathUtil.Flat(b.pos)) < BurnRadius && Mathf.Abs(p.transform.position.y - b.pos.y) < 2f)
                        DeliverProjectileHit(p, 5f, 0f, HitFlags.Unblockable);
            }
        }

        /// <summary>Players between inner and outer radius (those already hit by this move are skipped).</summary>
        void HitRing(Vector3 c, float inner, float outer, float damage, HitFlags flags) => HitSphere(c, outer, damage, flags);

        void HitSphere(Vector3 c, float radius, float damage, HitFlags flags)
        {
            foreach (var p in PlayerController.All)
            {
                if (!p || p.IsDead || _hitOnce.Contains(p.NetworkObjectId)) continue;
                Vector3 to = p.transform.position - c;
                if (Mathf.Abs(p.transform.position.y - _groundY) > 3.5f) continue;
                if (MathUtil.Flat(to).magnitude > radius + 0.4f) continue;
                _hitOnce.Add(p.NetworkObjectId);
                DeliverHit(p, damage, flags, true);
            }
        }

        Vector3 ArenaCenterGround()
        {
            var lv = LevelRefs.Instance;
            Vector3 c = lv ? lv.dragonArena.center : homePosition;
            c.y = _groundY;
            return c;
        }

        bool InArena(PlayerController p) => LevelRefs.Instance == null || LevelRefs.Instance.IsInsideDragonArena(p.transform.position);

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
                _transition = true;
                _queue.Clear();
                _queue.Enqueue(DragonMove.TakeOff); _queue.Enqueue(DragonMove.FireRain); _queue.Enqueue(DragonMove.Land);
                if (!_flying) SetMove(DragonMove.Roar);
                _sinceFlight = 0f;
                PhaseRpc();
            }
        }

        protected override void Stagger(float duration)
        {
            var m = CurrentMove;
            if (_flying || m == DragonMove.Roar || m == DragonMove.Awaken || m == DragonMove.Land) return;
            _queue.Clear();
            SetMove(DragonMove.Stagger);
        }

        protected override int SoulReward => 2000;
        protected override bool IsBossReward => true;

        protected override void Die()
        {
            _queue.Clear();
            _fireballs.Clear();
            if (_flying)
            {
                _flying = false;
                transform.position = new Vector3(transform.position.x, _groundY + 0.05f, transform.position.z);
            }
            SetMove(DragonMove.Death);
            base.Die();
            DragonDeathRpc();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DevMove(string m)
        {
            if (!IsServer) return;
            if (System.Enum.TryParse<DragonMove>(m, true, out var mv))
            {
                if (mv == DragonMove.TakeOff) { _queue.Clear(); _queue.Enqueue(DragonMove.FireRain); _queue.Enqueue(DragonMove.Land); }
                if (mv == DragonMove.Breath) { var (a, b) = BreathSweep(); SetMove(mv, a, b); }
                else SetMove(mv);
            }
        }
        public void DevHealth(string frac) { if (IsServer && float.TryParse(frac, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) netHealth.Value = netMaxHealth.Value * Mathf.Clamp01(f); }
#endif

        // ================================================================== client fx
        [Rpc(SendTo.Everyone)]
        void RoarRpc()
        {
            Vector3 p = transform.position + Vector3.up * headHeight + transform.forward * headForward;
            Sfx.Play(SfxId.DragonRoar, p, 1.4f, Random.Range(0.85f, 0.95f));
            SoulsCamera.Shake(0.05f, 1.6f);
        }

        [Rpc(SendTo.Everyone)]
        void SnarlRpc() => Sfx.Play(SfxId.DragonBite, transform.position + transform.forward * headForward + Vector3.up * headHeight, 1f, Random.Range(0.9f, 1.05f));

        [Rpc(SendTo.Everyone)]
        void TailRpc()
        {
            Sfx.Play(SfxId.DragonTail, transform.position - transform.forward * 5f * S, 1.3f, 0.75f);
            // a crescent of dust and sparks along the tail's sweep
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(-150f, 150f, i / 8f) + 180f;
                Vector3 p = transform.position + Quaternion.AngleAxis(a, Vector3.up) * transform.forward * (9f * S);
                p = BossFx.Ground(p);
                if (i % 2 == 0) Fx.Spawn(FxId.ShockDust, p); else Fx.SpawnScaled(FxId.Dust, p + Vector3.up * 0.3f, 1.8f);
            }
            BossFx.RingWave(BossFx.Ground(transform.position), 10f * S + 1f, 22f, false);
            SoulsCamera.Shake(0.05f, 0.35f);
        }

        [Rpc(SendTo.Everyone)]
        void ChargeLaneRpc(Vector3 from, Vector3 dir, float length)
        {
            dir = MathUtil.Flat(dir).normalized;
            Sfx.Play(SfxId.DragonCharge, from + Vector3.up * 3f, 1.4f, 0.8f);
            SoulsCamera.Shake(0.03f, 0.5f);
            float lead = DragonMoves.ChargeRun - DragonMoves.ChargeLock;
            // a lane of danger discs: each fills up exactly when the dragon's head gets there
            for (float d = headForward; d <= length; d += 2.2f)
            {
                Vector3 p = from + dir * d;
                float when = lead + Mathf.Max(0f, d - headForward) / ChargeSpeed;
                BossFx.GroundWarning(p, ChargeHalfWidth, Mathf.Max(0.35f, when), BossFx.DangerRed);
            }
            BossFx.ChargeLane(from + dir * headForward, dir, length - headForward, ChargeHalfWidth, lead + (length - headForward) / ChargeSpeed);
            if (_chargeTrail != null) StopCoroutine(_chargeTrail);
            _chargeTrail = StartCoroutine(ChargeTrail());
        }

        Coroutine _chargeTrail;
        System.Collections.IEnumerator ChargeTrail()
        {
            yield return new WaitForSeconds(DragonMoves.ChargeRun - DragonMoves.ChargeLock);
            float until = Time.time + (DragonMoves.ChargeRunEnd - DragonMoves.ChargeRun) + 0.3f;
            int k = 0;
            while (Time.time < until && CurrentMove == DragonMove.Charge)
            {
                Vector3 side = transform.right * ((k++ % 2 == 0) ? 1.6f : -1.6f) * S;
                Fx.SpawnScaled(FxId.Dust, BossFx.Ground(transform.position + side) + Vector3.up * 0.2f, 2.2f);
                if (k % 3 == 0) Fx.Spawn(FxId.ShockDust, BossFx.Ground(transform.position - transform.forward * 2f));
                yield return new WaitForSeconds(0.09f);
            }
        }

        /// <summary>Everyone: the tell of a melee move (0 jaws glint, 1 tail arc, 2 claw mark, 3 leap target).</summary>
        [Rpc(SendTo.Everyone)]
        void TellRpc(byte kind, Vector3 at, float radius, float until)
        {
            until = Mathf.Max(0.3f, until);
            switch (kind)
            {
                case 0:
                    BossFx.Flash(at, new Color(1f, 0.55f, 0.2f), 6f, 6f, 0.35f);
                    Sfx.Play(SfxId.EnemyGrunt, at, 1.2f, 0.5f);
                    break;
                case 1:
                    // the arc behind it, where the tail will pass
                    for (int i = 0; i < 7; i++)
                    {
                        float a = (110f + i * 23f) * Mathf.Deg2Rad;
                        Vector3 dir = transform.rotation * new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        BossFx.GroundWarning(at + dir * radius * 0.65f, radius * 0.28f, until, BossFx.DangerRed);
                    }
                    Sfx.Play(SfxId.DragonTail, at, 0.8f, 0.8f);
                    break;
                case 2:
                    BossFx.GroundWarning(at, radius, until, BossFx.DangerRed);
                    Sfx.Play(SfxId.EnemyGrunt, at + Vector3.up * 3f, 1.1f, 0.45f);
                    break;
                case 3:
                    BossFx.GroundWarning(at, radius, until, BossFx.DangerRed);
                    Sfx.Play(SfxId.Roar, at, 1.3f, 0.75f);
                    Sfx.Play(SfxId.DragonSwoop, transform.position + Vector3.up * 3f, 1.4f, 0.9f);
                    break;
            }
        }

        [Rpc(SendTo.Everyone)]
        void SkidRpc(Vector3 at)
        {
            Fx.Spawn(FxId.ShockDust, BossFx.Ground(at));
            Fx.SpawnScaled(FxId.Dust, BossFx.Ground(at) + Vector3.up * 0.3f, 3f);
            Sfx.Play(SfxId.DragonStep, at, 1.4f, 0.45f);
            SoulsCamera.Shake(0.04f, 0.3f);
        }

        [Rpc(SendTo.Everyone)]
        void BurnRpc(Vector3 at, float life)
        {
            BossFx.BurningGround(BossFx.Ground(at), BurnRadius, life);
        }

        [Rpc(SendTo.Everyone)]
        void EruptRpc(Vector3 at, float delay)
        {
            BossFx.GroundWarning(at, fireballRadius, delay, BossFx.DangerRed);
            BossFx.DelayedPillar(at, fireballRadius, delay);
        }

        [Rpc(SendTo.Everyone)]
        void SlamRpc(Vector3 c, float radius)
        {
            BossFx.Debris(BossFx.Ground(c), radius * 0.6f, 26);
            Sfx.Play(SfxId.DragonStep, c, 1.5f, 0.5f);
            Sfx.Play(SfxId.Slam, c, 1.2f, 0.7f);
            Fx.Spawn(FxId.ShockDust, c);
            BossFx.RingWave(BossFx.Ground(c), radius, radius * 2.2f, false);
            SoulsCamera.Shake(0.07f, 0.45f);
        }

        [Rpc(SendTo.Everyone)]
        void WingBlastRpc(Vector3 c)
        {
            Fx.Spawn(FxId.ShockDust, c);
            BossFx.RingWave(BossFx.Ground(c), 8f, 14f, false);
            Sfx.Play(SfxId.DragonWing, c, 1.4f, 0.4f);
            Sfx.Play(SfxId.DragonSwoop, c + Vector3.up * 4f, 1.3f, 0.8f);
            SoulsCamera.Shake(0.04f, 0.5f);
        }

        [Rpc(SendTo.Everyone)]
        void FireballRpc(Vector3 from, Vector3 to, float flight)
        {
            BossFx.GroundWarning(to, fireballRadius, flight, BossFx.DangerRed);
            Sfx.Play(SfxId.Fireball, from, 1f, Random.Range(0.8f, 1f));
            var go = new GameObject("dragon_fireball");
            go.transform.position = from;
            var fb = go.AddComponent<FireballVisual>();
            fb.Launch(from, to, flight, fireballRadius);
        }

        [Rpc(SendTo.Everyone)]
        void PhaseRpc()
        {
            Sfx.Play(SfxId.DragonRoar, transform.position + Vector3.up * 4f, 1.5f, 0.7f);
            Enraged?.Invoke();
        }

        [Rpc(SendTo.Everyone)]
        void DragonDeathRpc()
        {
            Sfx.Play(SfxId.DragonRoar, transform.position + Vector3.up * 3f, 1.4f, 0.55f);
            SoulsCamera.Shake(0.06f, 2f);
            BossFx.Embers(transform.position + Vector3.up * 2f, 6f, 80, 3f);
        }

        /// <summary>UI hook: the dragon enters its second phase (all clients).</summary>
        public static event System.Action Enraged;
    }

    /// <summary>A fireball in flight (pure visual): parabolic arc, flame trail, light, explodes at the target.</summary>
    public class FireballVisual : MonoBehaviour
    {
        Vector3 _from, _to;
        float _flight, _t, _radius;
        Light _light;

        public void Launch(Vector3 from, Vector3 to, float flight, float radius)
        {
            _from = from; _to = to; _flight = Mathf.Max(0.2f, flight); _radius = radius;
            if (Fx.Instance) Fx.CreateFlame(transform, Vector3.zero, 2.2f, Fx.Instance.additiveMaterial);
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point; _light.range = 9f; _light.intensity = 5f; _light.color = new Color(1f, 0.55f, 0.2f);
            _light.shadows = LightShadows.None;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _flight);
            Vector3 p = Vector3.Lerp(_from, _to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 2.5f;
            transform.position = p;
            if (Time.frameCount % 2 == 0) Fx.SpawnScaled(FxId.Fireball, p, 0.45f);
            if (k >= 1f)
            {
                Fx.SpawnScaled(FxId.Fireball, _to + Vector3.up * 0.3f, 1.6f);
                BossFx.FireBurst(_to, _radius);
                BossFx.Flash(_to + Vector3.up, new Color(1f, 0.5f, 0.2f), 8f, 12f, 0.4f);
                Sfx.Play(SfxId.FireBurst, _to, 1.1f, Random.Range(0.85f, 1.05f));
                var cam = SoulsCamera.Instance;
                if (cam && Vector3.Distance(cam.transform.position, _to) < 14f) SoulsCamera.Shake(0.03f, 0.25f);
                Destroy(gameObject);
            }
        }
    }
}
