using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    public enum ReaperMove : byte
    {
        None, Awaken, Reap, Slash, Crescent, Lunge, Blink, Grab, Rain, Wave, Summon, Ascend, Stagger, Death
    }

    /// <summary>
    /// Death, the spectral Grim Reaper: last boss, in the court of the Red Moon (host-authoritative:
    /// the server picks the moves and resolves the hits in code, every client poses the body from the
    /// replicated move + start time, like the Mirror).
    ///
    /// Locomotion: it never touches the ground (a hovering glide leaving a wake of mist and souls),
    /// dissolves into black smoke to cross the arena and reappears behind its prey, and rises into
    /// the air for its great spells.
    /// Scythe: Reaper's Slash (slow, devastating, a spectral blade lengthens the reach; dark damage
    /// pierces a raised guard), Infernal Crescent (a spin that sends an expanding ring of dark: roll
    /// through it), Eclipse Lunge (vanishes, tears straight through the target, reappears beyond).
    /// Magic: Call of Souls (a skeletal hand of mist: if it closes, the victim is lifted, drained and
    /// hurled down, cursed), Rain of Spectral Scythes (timed patterns from above), Veil of Darkness
    /// (it fades into the fog: only the drag of the blade and a ticking clock betray it).
    /// Phase 1 "the Silent Reaper": measured, wide cuts, short blinks, punishes healing from afar.
    /// Phase 2 "the Final Judgement" (below 40%): the cloak burns away into ectoplasm, the scythe
    /// takes blue-violet will-o'-wisp fire, longer combos, faster, minor spectres answer its call.
    /// </summary>
    public class ReaperBossAI : EnemyBase
    {
        public static ReaperBossAI Instance { get; private set; }
        public static event System.Action Judgement;   // phase 2 (all clients)

        public struct MoveState : INetworkSerializeByMemcpy
        {
            public byte move;
            public double start;
            public float p1, p2, rate;
            public Vector3 pos;
        }

        [Header("Reaper")]
        public float perPlayerScaling = 0.6f;
        [Range(0, 1)] public float phase2Threshold = 0.4f;
        public float hoverHeight = 0.55f;

        [Header("Damage")]
        public float reapDamage = 26f, slashDamage = 44f, crescentDamage = 30f, lungeDamage = 34f;
        public float rainDamage = 20f, waveDamage = 22f, grabDrain = 8f, grabSlam = 24f;

        [Header("Visuals")]
        [Tooltip("Parent of the visual root: lifted by the hover / elevation.")]
        public Transform hover;
        [Tooltip("Pivot at the body's middle: turned, leaned and spun for the attacks.")]
        public Transform pose;
        public Renderer[] bodyRenderers;
        public Material cloakMaterial, ectoMaterial;
        public Renderer[] scytheRenderers;
        public GameObject wispFire;            // phase 2: will-o'-wisp flames along the blade
        public ParticleSystem mistTrail;       // the wake of mist and souls
        public ParticleSystem mistBurst;       // a puff of black smoke (dissolve / reappear)
        public Light eyeLight;
        public Transform handVisual;           // Call of Souls: the skeletal hand
        public Transform chest;                // where the hand leaves from
        public Mesh spectralScytheMesh;
        public Material spectralMaterial;
        public GameObject minionPrefab;

        readonly NetworkVariable<MoveState> _netMove = new NetworkVariable<MoveState>();
        readonly NetworkVariable<bool> _netActive = new NetworkVariable<bool>(false);
        readonly NetworkVariable<byte> _netPhase = new NetworkVariable<byte>(1);
        readonly NetworkVariable<bool> _netVeil = new NetworkVariable<bool>(false);

        public ReaperMove CurrentMove => (ReaperMove)_netMove.Value.move;
        float Rate => _netMove.Value.rate > 0f ? _netMove.Value.rate : 1f;
        float MoveTime => CurrentMove == ReaperMove.None ? 0f : (float)(Now - _netMove.Value.start) * Rate;
        public bool FightActive => _netActive.Value;
        public int Phase => _netPhase.Value;
        public bool Veiled => _netVeil.Value;
        bool P2 => _netPhase.Value >= 2;
        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        public override bool IsStaggered => CurrentMove == ReaperMove.Stagger;
        protected override bool IsInvulnerable => !_netActive.Value || CurrentMove == ReaperMove.Awaken || CurrentMove == ReaperMove.Ascend || Dissolved;
        protected override bool IsInvulnerableVisual => !_netActive.Value;
        protected override float GruntChance => 0f;
        protected override float CurseHeight => 5.2f;
        protected override int SoulReward => 3500;
        protected override bool IsBossReward => true;

        // ---- move timing (seconds of move time, i.e. already scaled by the rate)
        const float AwakenDur = 3.0f, AwakenPulse = 2.1f;
        const float ReapHit = 0.36f, ReapDur = 0.95f;
        const float SlashHitH = 1.25f, SlashDurH = 2.55f, SlashHitV = 0.8f, SlashDurV = 1.9f;
        const float CrescentRelease = 0.52f, CrescentDur = 1.35f, RingSpeed = 9f, RingMax = 17f;
        const float LungeVanish = 0.4f, LungeDashEnd = 0.68f, LungeDur = 1.55f, LungeLength = 15f;
        const float BlinkOut = 0.3f, BlinkIn = 0.75f, BlinkDur = 1.0f;
        const float GrabCast = 0.75f, HandSpeed = 12f, HandRange = 15f, GrabMissDur = 2.6f, GrabHold = 2.3f;
        const float RainStart = 1.0f, RainEnd = 4.2f, RainDur = 5.4f, RainWarn = 1.0f;
        const float WaveRelease = 0.45f, WaveDur = 1.15f, WaveSpeed = 15f, WaveRange = 26f, WaveWidth = 5f;
        const float SummonCall = 1.0f, SummonDur = 2.2f;
        const float AscendBurn = 1.5f, AscendBlast = 2.6f, AscendDur = 3.4f;
        const float StaggerDur = 1.3f, DeathDur = 6f;

        readonly Queue<ReaperMove> _queue = new Queue<ReaperMove>();
        readonly HashSet<ulong> _hitOnce = new HashSet<ulong>();
        readonly HashSet<ulong> _hitOnce2 = new HashSet<ulong>();
        readonly Dictionary<ReaperMove, float> _cooldown = new Dictionary<ReaperMove, float>();
        readonly List<(double time, Vector3 pos)> _scythes = new List<(double, Vector3)>();
        readonly List<(double start, Vector3 origin, Vector3 dir, HashSet<ulong> hit)> _waves = new List<(double, Vector3, Vector3, HashSet<ulong>)>();
        readonly List<NetworkObject> _minions = new List<NetworkObject>();
        float _recover, _veilTimer, _veilLeft, _orbit = 1f;
        bool _resolved, _resolved2, _resolved3;
        int _rainIndex;
        float _groundY;
        Vector3 _handPos, _handDir;
        bool _handLive;
        PlayerController _grabbed;
        double _grabEnd;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            knockbackFactor = 0f;
            stanceImmunity = 10f;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            _groundY = homePosition.y;
            _netActive.OnValueChanged += OnActiveChanged;
            _netPhase.OnValueChanged += OnPhaseChanged;
            ApplyPhaseLook(_netPhase.Value >= 2);
            var lot = GetComponent<LockOnTarget>();
            if (lot) lot.enabled = _netActive.Value && IsAlive;
            if (handVisual) handVisual.gameObject.SetActive(false);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _netActive.OnValueChanged -= OnActiveChanged;
            _netPhase.OnValueChanged -= OnPhaseChanged;
            if (Instance == this) Instance = null;
            AtmosphereController.VeilFog = 0f;
        }

        public override void OnDestroy()
        {
            if (handVisual && handVisual.parent == null) Destroy(handVisual.gameObject);
            base.OnDestroy();
        }

        void OnActiveChanged(bool a, bool b) { var l = GetComponent<LockOnTarget>(); if (l) l.enabled = b && IsAlive; }
        void OnPhaseChanged(byte a, byte b) { if (b < 2) ApplyPhaseLook(false); }

        // ================================================================== arena control (server)
        public void Activate(int playerCount)
        {
            if (!IsServer) return;
            SetupStats(1f + (Mathf.Max(1, playerCount) - 1) * perPlayerScaling);
            _netPhase.Value = 1;
            _netActive.Value = true;
            _netVeil.Value = false;
            _queue.Clear(); _scythes.Clear(); _waves.Clear(); _cooldown.Clear();
            _veilTimer = 24f;
            Cool(ReaperMove.Grab, 10f); Cool(ReaperMove.Rain, 16f);
            target = PickTarget();
            if (target) transform.rotation = Quaternion.LookRotation(MathUtil.Flat(target.transform.position - transform.position));
            SetMove(ReaperMove.Awaken);
            Debug.Log($"[DF] Death wakes for {playerCount} player(s) → HP {netMaxHealth.Value:0}");
        }

        public void ResetReaper()
        {
            if (!IsServer) return;
            _queue.Clear(); _scythes.Clear(); _waves.Clear();
            ReleaseGrab(false);
            DespawnMinions();
            cc.enabled = true;
            ResetToHome(1f);
            _netActive.Value = false;
            _netPhase.Value = 1;
            _netVeil.Value = false;
            SetMove(ReaperMove.None);
        }

        void DespawnMinions()
        {
            foreach (var m in _minions) if (m != null && m.IsSpawned) m.Despawn(true);
            _minions.Clear();
        }

        // ================================================================== moves
        void SetMove(ReaperMove m, float p1 = 0f, float p2 = 0f, Vector3 pos = default)
        {
            bool fixedRate = m == ReaperMove.Ascend || m == ReaperMove.Death || m == ReaperMove.Stagger || m == ReaperMove.Awaken;
            float rate = P2 && !fixedRate ? 1.22f : 1f;
            _netMove.Value = new MoveState { move = (byte)m, start = Now, p1 = p1, p2 = p2, rate = rate, pos = pos };
            _resolved = _resolved2 = _resolved3 = false;
            _hitOnce.Clear(); _hitOnce2.Clear();
            _rainIndex = 0;
            _handLive = false;
            if (m == ReaperMove.Crescent) { _ringStart = -1; _ringLive = 0; }
            netLocoSpeed.Value = 0f;
            byte anim = AnimFor(m, p1, out float speed);
            if (anim != 255) PlayAnimRpc(anim, 0.12f, speed);
            if (m != ReaperMove.None && m != ReaperMove.Death) MoveStartRpc((byte)m, p1);
        }

        static byte AnimFor(ReaperMove m, float p1, out float speed)
        {
            speed = 1f;
            switch (m)
            {
                case ReaperMove.Reap: speed = 1.1f; return StateIndex(CharacterAnimator.Attack2);
                case ReaperMove.Slash: speed = p1 > 0.5f ? 0.5f : 0.55f; return StateIndex(p1 > 0.5f ? CharacterAnimator.Attack1 : CharacterAnimator.Heavy);
                case ReaperMove.Crescent: speed = 1f; return StateIndex(CharacterAnimator.Spin);
                case ReaperMove.Lunge: speed = 0.9f; return StateIndex(CharacterAnimator.Attack1);
                case ReaperMove.Blink: return StateIndex(CharacterAnimator.Locomotion);
                case ReaperMove.Wave: speed = 0.9f; return StateIndex(CharacterAnimator.Attack2);
                case ReaperMove.Grab: case ReaperMove.Summon: case ReaperMove.Awaken: speed = 0.8f; return StateIndex(CharacterAnimator.Taunt);
                case ReaperMove.Rain: speed = 0.55f; return StateIndex(CharacterAnimator.Taunt);
                case ReaperMove.Ascend: speed = 0.7f; return StateIndex(CharacterAnimator.Taunt);
                case ReaperMove.Stagger: return StateIndex(CharacterAnimator.Hurt);
                case ReaperMove.Death: return 255;
                default: return StateIndex(CharacterAnimator.Locomotion);
            }
        }

        static float Duration(ReaperMove m, float p1) => m switch
        {
            ReaperMove.Awaken => AwakenDur,
            ReaperMove.Reap => ReapDur,
            ReaperMove.Slash => p1 > 0.5f ? SlashDurV : SlashDurH,
            ReaperMove.Crescent => CrescentDur,
            ReaperMove.Lunge => LungeDur,
            ReaperMove.Blink => BlinkDur,
            ReaperMove.Grab => GrabMissDur,
            ReaperMove.Rain => RainDur,
            ReaperMove.Wave => WaveDur,
            ReaperMove.Summon => SummonDur,
            ReaperMove.Ascend => AscendDur,
            ReaperMove.Stagger => StaggerDur,
            ReaperMove.Death => DeathDur,
            _ => 0f,
        };

        /// <summary>The ghost has dissolved into smoke (cannot be hit while it crosses the arena).</summary>
        bool Dissolved
        {
            get
            {
                float t = MoveTime;
                var m = CurrentMove;
                if (m == ReaperMove.Blink) return t > BlinkOut && t < BlinkIn;
                if (m == ReaperMove.Lunge) return t > LungeVanish - 0.1f && t < LungeDashEnd;
                return false;
            }
        }

        void EndMove(ReaperMove m)
        {
            if (_queue.Count > 0) { StartMove(_queue.Dequeue()); return; }
            SetMove(ReaperMove.None);
            bool heavy = m == ReaperMove.Slash || m == ReaperMove.Rain || m == ReaperMove.Lunge;
            _recover = m == ReaperMove.Stagger ? 0.3f : heavy ? (P2 ? 0.75f : 1.3f) : (P2 ? 0.35f : 0.8f);
        }

        void StartMove(ReaperMove m)
        {
            if (target == null) target = PickTarget();
            switch (m)
            {
                case ReaperMove.Slash: SetMove(m, Random.value < 0.4f ? 1f : 0f); break;
                case ReaperMove.Lunge:
                {
                    Vector3 dir = target ? MathUtil.Flat(target.transform.position - transform.position).normalized : transform.forward;
                    if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
                    float d = target ? MathUtil.Flat(target.transform.position - transform.position).magnitude : 8f;
                    Vector3 end = ClampToArena(transform.position + dir * Mathf.Max(LungeLength, d + 4f));
                    SetMove(m, 0f, 0f, end);
                    break;
                }
                case ReaperMove.Blink:
                {
                    Vector3 at = transform.position;
                    if (target)
                    {
                        Vector3 back = -MathUtil.Flat(target.transform.forward).normalized;
                        if (back.sqrMagnitude < 0.01f) back = -transform.forward;
                        at = ClampToArena(target.transform.position + back * 2.8f);
                    }
                    SetMove(m, 0f, 0f, at);
                    break;
                }
                case ReaperMove.Wave:
                {
                    Vector3 dir = target ? MathUtil.Flat(target.transform.position - transform.position).normalized : transform.forward;
                    SetMove(m, 0f, 0f, dir);
                    break;
                }
                default: SetMove(m); break;
            }
        }

        bool Ready(ReaperMove m) => !_cooldown.TryGetValue(m, out var c) || c <= 0f;
        void Cool(ReaperMove m, float s) => _cooldown[m] = s;

        // ================================================================== FSM
        protected override void ServerTick(float dt)
        {
            UpdateScythes();
            UpdateWaves();
            if (!_netActive.Value) { StopMoving(dt); return; }
            if (_cooldown.Count > 0) foreach (var k in new List<ReaperMove>(_cooldown.Keys)) _cooldown[k] -= dt;
            _minions.RemoveAll(n => n == null || !n.IsSpawned || !n.GetComponent<EnemyBase>().IsAlive);
            if (target == null || target.IsDead || !InArena(target)) target = PickTarget();

            var m = CurrentMove;
            if (m != ReaperMove.None)
            {
                float t = MoveTime;
                UpdateMove(m, t, dt);
                float dur = Duration(m, _netMove.Value.p1);
                if (m == ReaperMove.Grab && _grabbed != null) dur = Mathf.Max(dur, (float)(_grabEnd - _netMove.Value.start) * Rate + 0.6f);
                if (CurrentMove == m && t >= dur) EndMove(m);
                return;
            }

            // Veil of Darkness: it fades into the fog and circles, betrayed only by sound
            if (_netVeil.Value)
            {
                _veilLeft -= dt;
                Glide(dt, true);
                if (_veilLeft <= 0f || target == null)
                {
                    _netVeil.Value = false;
                    _veilTimer = Random.Range(22f, 30f) * (P2 ? 0.75f : 1f);
                    // out of the fog behind its prey, blade first
                    StartMove(ReaperMove.Blink);
                    _queue.Enqueue(P2 ? ReaperMove.Slash : ReaperMove.Reap);
                }
                return;
            }
            _veilTimer -= dt;

            if (_recover > 0f) { _recover -= dt; Glide(dt, false, 0.35f); return; }
            if (target == null) { StopMoving(dt); return; }
            if (_queue.Count > 0) { StartMove(_queue.Dequeue()); return; }
            if (_veilTimer <= 0f)
            {
                _netVeil.Value = true; _veilLeft = P2 ? 4.5f : 6f;
                VeilRpc(true);
                return;
            }
            Decide(dt);
        }

        void Decide(float dt)
        {
            Vector3 to = target.transform.position - transform.position;
            float d = MathUtil.Flat(to).magnitude;
            float ang = Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to));

            // punish the healer: a crescent of soul-light from afar
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && InArena(p) && Time.time - p.LastHealTime < 1.2f && MathUtil.Flat(p.transform.position - transform.position).magnitude > 6f && Ready(ReaperMove.Wave))
                {
                    target = p; Cool(ReaperMove.Wave, 3f);
                    StartMove(ReaperMove.Wave);
                    if (P2) _queue.Enqueue(ReaperMove.Wave);
                    return;
                }

            int near = 0;
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && InArena(p) && MathUtil.Flat(p.transform.position - transform.position).magnitude < 7f) near++;

            if (P2 && Ready(ReaperMove.Summon) && _minions.Count < 2 && Random.value < 0.5f) { Cool(ReaperMove.Summon, 26f); StartMove(ReaperMove.Summon); return; }
            if (Ready(ReaperMove.Rain) && Random.value < 0.35f) { Cool(ReaperMove.Rain, P2 ? 15f : 21f); StartMove(ReaperMove.Rain); return; }
            if (Ready(ReaperMove.Grab) && d > 4f && d < HandRange - 2f && ang < 50f && Random.value < 0.35f) { Cool(ReaperMove.Grab, P2 ? 11f : 15f); StartMove(ReaperMove.Grab); return; }

            if (d < 5f)
            {
                float r = Random.value;
                if ((near >= 2 || r < 0.22f) && Ready(ReaperMove.Crescent)) { Cool(ReaperMove.Crescent, P2 ? 4f : 7f); StartMove(ReaperMove.Crescent); if (P2) _queue.Enqueue(ReaperMove.Reap); return; }
                if (r < 0.5f) { StartMove(ReaperMove.Reap); if (P2) { _queue.Enqueue(ReaperMove.Reap); if (Random.value < 0.5f) _queue.Enqueue(ReaperMove.Slash); } return; }
                if (r < 0.72f) { StartMove(ReaperMove.Slash); if (P2 && Random.value < 0.5f) _queue.Enqueue(ReaperMove.Crescent); return; }
                if (Ready(ReaperMove.Blink)) { Cool(ReaperMove.Blink, P2 ? 3f : 6f); StartMove(ReaperMove.Blink); _queue.Enqueue(ReaperMove.Reap); return; }
            }
            else if (d < 10f)
            {
                float r = Random.value;
                if (r < 0.3f && ang < 40f) { StartMove(ReaperMove.Slash); return; }
                if (r < 0.55f && Ready(ReaperMove.Lunge) && ang < 35f) { Cool(ReaperMove.Lunge, P2 ? 4f : 7f); StartMove(ReaperMove.Lunge); if (P2) { _queue.Enqueue(ReaperMove.Reap); _queue.Enqueue(ReaperMove.Crescent); } return; }
                if (r < 0.75f && Ready(ReaperMove.Blink)) { Cool(ReaperMove.Blink, P2 ? 3f : 6f); StartMove(ReaperMove.Blink); _queue.Enqueue(ReaperMove.Reap); if (P2) _queue.Enqueue(ReaperMove.Slash); return; }
            }
            else
            {
                float r = Random.value;
                if (r < 0.35f && Ready(ReaperMove.Lunge) && ang < 35f) { Cool(ReaperMove.Lunge, P2 ? 4f : 7f); StartMove(ReaperMove.Lunge); return; }
                if (r < 0.6f && Ready(ReaperMove.Wave) && ang < 45f) { Cool(ReaperMove.Wave, P2 ? 4f : 6f); StartMove(ReaperMove.Wave); return; }
                if (r < 0.8f && Ready(ReaperMove.Blink)) { Cool(ReaperMove.Blink, P2 ? 3f : 6f); StartMove(ReaperMove.Blink); _queue.Enqueue(ReaperMove.Reap); return; }
            }
            Glide(dt, false);
        }

        /// <summary>The spectral glide: drifts toward its prey (or circles it, veiled), always facing it.</summary>
        void Glide(float dt, bool circle, float speedMul = 1f)
        {
            if (target == null) { StopMoving(dt); return; }
            Vector3 to = MathUtil.Flat(target.transform.position - transform.position);
            float d = to.magnitude;
            FaceTowards(target.transform.position, dt, 1.2f);
            Vector3 dir;
            if (circle)
            {
                Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * _orbit;
                dir = side + to.normalized * (d > 9f ? 0.8f : d < 6f ? -0.6f : 0f);
                if (Random.value < dt * 0.2f) _orbit = -_orbit;
            }
            else dir = d > 3.2f ? to.normalized : Vector3.zero;
            float speed = (P2 ? 4.6f : 3.4f) * speedMul * (circle ? 1.3f : 1f);
            if (dir.sqrMagnitude > 0.01f) cc.Move(dir.normalized * speed * dt);
            netLocoSpeed.Value = 0f;   // it does not walk: the body drifts in its idle float
        }

        Vector3 ClampToArena(Vector3 p)
        {
            var lv = LevelRefs.Instance;
            if (lv == null) return p;
            Vector3 c = lv.reaperArena.center;
            Vector3 d = MathUtil.Flat(p - c);
            float max = Mathf.Min(lv.reaperArena.extents.x, lv.reaperArena.extents.z) - 3f;
            if (d.magnitude > max) d = d.normalized * max;
            return new Vector3(c.x + d.x, _groundY, c.z + d.z);
        }

        // ================================================================== moves (server)
        void UpdateMove(ReaperMove m, float t, float dt)
        {
            var ms = _netMove.Value;
            switch (m)
            {
                case ReaperMove.Awaken:
                    if (!_resolved && t >= AwakenPulse) { _resolved = true; HitCircle(transform.position, 6f, 12f, HitFlags.AreaOfEffect); PulseRpc(transform.position, 6f); }
                    break;

                case ReaperMove.Reap:
                    if (t < ReapHit && target) FaceTowards(target.transform.position, dt, 1.3f);
                    if (t < ReapHit * 0.7f && target && MathUtil.Flat(target.transform.position - transform.position).magnitude > 2.6f) cc.Move(transform.forward * 5f * dt);
                    if (!_resolved && t >= ReapHit) { _resolved = true; HitCone(4.4f, 75f, reapDamage, HitFlags.Parryable); SwingRpc(0); }
                    break;

                case ReaperMove.Slash:
                {
                    bool vertical = ms.p1 > 0.5f;
                    float hit = vertical ? SlashHitV : SlashHitH;
                    if (t < hit - 0.35f && target) FaceTowards(target.transform.position, dt, 0.7f);
                    if (!_resolved2 && t >= 0.25f) { _resolved2 = true; SlashWarnRpc(vertical, (hit - t) / Rate); }
                    if (!_resolved && t >= hit)
                    {
                        _resolved = true;
                        if (vertical) HitLine(transform.position, transform.forward, 11f, 1.3f, slashDamage, HitFlags.Heavy | HitFlags.Dark);
                        else HitCone(8.5f, 80f, slashDamage, HitFlags.Heavy | HitFlags.Dark);
                        SlashRpc(vertical);
                    }
                    break;
                }

                case ReaperMove.Crescent:
                {
                    if (!_resolved2 && t >= 0.05f) { _resolved2 = true; CrescentWarnRpc(); }
                    if (!_resolved && t >= CrescentRelease) { _resolved = true; RingRpc(transform.position, P2); }
                    // the ring is resolved by time (it keeps travelling after the move ends: UpdateWaves)
                    break;
                }

                case ReaperMove.Lunge:
                {
                    Vector3 end = ms.pos;
                    if (t < LungeVanish) { FaceTowards(end, dt, 2f); break; }
                    if (t < LungeDashEnd)
                    {
                        if (!_resolved2) { _resolved2 = true; _lungeFrom = transform.position; cc.enabled = false; }
                        float k = Mathf.InverseLerp(LungeVanish, LungeDashEnd, t);
                        Vector3 p = Vector3.Lerp(_lungeFrom, end, k);
                        transform.position = new Vector3(p.x, _groundY, p.z);
                        foreach (var pl in PlayerController.All)
                            if (pl && !pl.IsDead && !_hitOnce.Contains(pl.NetworkObjectId) && DistToSegmentFlat(pl.transform.position, _lungeFrom, transform.position) < 1.6f && Mathf.Abs(pl.transform.position.y - _groundY) < 2.5f)
                            { _hitOnce.Add(pl.NetworkObjectId); DeliverHit(pl, lungeDamage, HitFlags.Heavy | HitFlags.Dark, true); }
                        break;
                    }
                    if (!_resolved) { _resolved = true; cc.enabled = true; if (target) transform.rotation = Quaternion.LookRotation(MathUtil.Flat(target.transform.position - transform.position).normalized + transform.forward * 0.01f); }
                    break;
                }

                case ReaperMove.Blink:
                    if (!_resolved && t >= (BlinkOut + BlinkIn) * 0.5f)
                    {
                        _resolved = true;
                        cc.enabled = false; transform.position = ms.pos; cc.enabled = true;
                        if (target) transform.rotation = Quaternion.LookRotation(MathUtil.Flat(target.transform.position - transform.position).normalized + transform.forward * 0.01f);
                    }
                    if (_resolved && target) FaceTowards(target.transform.position, dt, 2f);
                    break;

                case ReaperMove.Grab:
                    UpdateGrab(t, dt);
                    break;

                case ReaperMove.Rain:
                {
                    if (!_resolved2) { _resolved2 = true; ShadowRpc(transform.position, (RainEnd + RainWarn + 0.4f) / Rate); }
                    int total = P2 ? 36 : 26;
                    float span = RainEnd - RainStart;
                    while (_rainIndex < total && t >= RainStart + span * _rainIndex / total)
                    {
                        Vector3 at = RainPoint(_rainIndex, total);
                        _rainIndex++;
                        _scythes.Add((Now + RainWarn / Rate, at));
                        ScytheFallRpc(at, RainWarn / Rate);
                    }
                    break;
                }

                case ReaperMove.Wave:
                    if (t < WaveRelease && target) FaceTowards(target.transform.position, dt, 1.4f);
                    if (!_resolved && t >= WaveRelease)
                    {
                        _resolved = true;
                        Vector3 dir = target ? MathUtil.Flat(target.transform.position - transform.position).normalized : transform.forward;
                        Vector3 from = transform.position + dir * 1.2f;
                        _waves.Add((Now, from, dir, new HashSet<ulong>()));
                        WaveRpc(from, dir);
                    }
                    break;

                case ReaperMove.Summon:
                    if (!_resolved && t >= SummonCall) { _resolved = true; SummonMinions(); }
                    break;

                case ReaperMove.Ascend:
                    if (!_resolved2 && t >= AscendBurn) { _resolved2 = true; JudgementRpc(); }
                    if (!_resolved && t >= AscendBlast) { _resolved = true; HitCircle(transform.position, 7.5f, 14f, HitFlags.Heavy | HitFlags.AreaOfEffect); PulseRpc(transform.position, 7.5f); }
                    break;
            }
        }

        Vector3 _lungeFrom;

        // ---- Call of Souls
        void UpdateGrab(float t, float dt)
        {
            if (t < GrabCast && target) FaceTowards(target.transform.position, dt, 1.5f);
            if (!_resolved2 && t >= GrabCast)
            {
                _resolved2 = true;
                _handPos = (chest ? chest.position : transform.position + Vector3.up * 3f);
                Vector3 aim = target ? target.transform.position + Vector3.up * 1f : _handPos + transform.forward * 8f;
                _handDir = (aim - _handPos).normalized;
                _handLive = true;
                HandRpc(_handPos, _handDir * HandSpeed);
            }
            if (_handLive)
            {
                Vector3 step = _handDir * HandSpeed * dt * Rate;
                _handPos += step;
                foreach (var p in PlayerController.All)
                {
                    if (!p || p.IsDead || p.IsInvulnerable) continue;
                    if (Vector3.Distance(p.transform.position + Vector3.up, _handPos) < 1.35f)
                    {
                        _handLive = false;
                        _grabbed = p;
                        _grabEnd = Now + GrabHold;
                        Vector3 hold = transform.position + transform.forward * 2.4f + Vector3.up * 3.3f;
                        p.SoulGrabRpc(hold, GrabHold, grabDrain, grabSlam, transform.forward);
                        GrabbedRpc(p.NetworkObject, hold, GrabHold);
                        break;
                    }
                }
                if (_handLive && (_handPos - (chest ? chest.position : transform.position)).magnitude > HandRange) { _handLive = false; HandMissRpc(); }
            }
            if (_grabbed != null && Now >= _grabEnd) ReleaseGrab(true);
        }

        void ReleaseGrab(bool slam)
        {
            if (_grabbed == null) return;
            var p = _grabbed;
            _grabbed = null;
            if (slam) HandSlamRpc(p.transform.position);
        }

        Vector3 RainPoint(int i, int total)
        {
            var lv = LevelRefs.Instance;
            Vector3 c = lv ? lv.reaperArena.center : transform.position;
            c.y = _groundY;
            int pattern = (i * 3 / total);   // three waves: rings around Death, lines at the players, then a hunt
            if (pattern == 0)
            {
                int ringN = total / 3;
                float a = (i % ringN) / (float)ringN * Mathf.PI * 2f + (i % 2) * 0.3f;
                float r = 4f + (i % 3) * 4f;
                Vector3 o = transform.position + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                return ClampToArena(o);
            }
            var alive = new List<PlayerController>();
            foreach (var p in PlayerController.All) if (p && !p.IsDead && InArena(p)) alive.Add(p);
            if (alive.Count == 0) return ClampToArena(c + Random.insideUnitSphere * 14f);
            var v = alive[i % alive.Count];
            if (pattern == 1)
            {
                Vector3 dir = MathUtil.Flat(v.transform.position - transform.position).normalized;
                float k = (i % 4) / 3f;
                return ClampToArena(Vector3.Lerp(transform.position, v.transform.position + dir * 3f, k) + Vector3.Cross(Vector3.up, dir) * Random.Range(-1f, 1f));
            }
            var tcc = v.GetComponent<CharacterController>();
            Vector3 lead = tcc ? MathUtil.Flat(tcc.velocity) * 0.7f : Vector3.zero;
            return ClampToArena(v.transform.position + lead + Random.insideUnitSphere * 2.2f);
        }

        void UpdateScythes()
        {
            for (int i = _scythes.Count - 1; i >= 0; i--)
            {
                if (Now < _scythes[i].time) continue;
                Vector3 at = _scythes[i].pos;
                _scythes.RemoveAt(i);
                foreach (var p in PlayerController.All)
                    if (p && !p.IsDead && MathUtil.Flat(p.transform.position - at).magnitude < 1.7f && Mathf.Abs(p.transform.position.y - at.y) < 2.5f)
                        DeliverProjectileHit(p, rainDamage, 20f, HitFlags.AreaOfEffect);
            }
        }

        /// <summary>The soul crescents (travel on their own) and the expanding rings of the Infernal Crescent.</summary>
        void UpdateWaves()
        {
            // rings: hit once when the edge passes over a player that is not rolling
            if (CurrentMove == ReaperMove.Crescent || _ringLive > 0)
            {
                if (CurrentMove == ReaperMove.Crescent && _resolved && _ringStart < 0) { _ringStart = Now; _ringCenter = transform.position; _ringHit.Clear(); _ringLive = P2 ? 2 : 1; }
                if (_ringStart >= 0)
                {
                    for (int k = 0; k < _ringLive; k++)
                    {
                        float r = 1.5f + (float)(Now - _ringStart - k * 0.6f) * RingSpeed;
                        if (r < 1.5f || r > RingMax) continue;
                        foreach (var p in PlayerController.All)
                        {
                            if (!p || p.IsDead || _ringHit.Contains(p.NetworkObjectId * 4 + (ulong)k)) continue;
                            float d = MathUtil.Flat(p.transform.position - _ringCenter).magnitude;
                            if (Mathf.Abs(d - r) < 0.75f && Mathf.Abs(p.transform.position.y - _groundY) < 1.8f)
                            { _ringHit.Add(p.NetworkObjectId * 4 + (ulong)k); DeliverHit(p, crescentDamage, HitFlags.Heavy | HitFlags.AreaOfEffect | HitFlags.Unblockable, true); }
                        }
                    }
                    if ((float)(Now - _ringStart) * RingSpeed > RingMax + 8f) { _ringStart = -1; _ringLive = 0; }
                }
            }
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i];
                float travelled = (float)(Now - w.start) * WaveSpeed;
                if (travelled > WaveRange) { _waves.RemoveAt(i); continue; }
                Vector3 front = w.origin + w.dir * travelled;
                Vector3 side = Vector3.Cross(Vector3.up, w.dir);
                foreach (var p in PlayerController.All)
                {
                    if (!p || p.IsDead || w.hit.Contains(p.NetworkObjectId)) continue;
                    Vector3 rel = p.transform.position - front;
                    if (Mathf.Abs(Vector3.Dot(rel, w.dir)) < 0.8f && Mathf.Abs(Vector3.Dot(rel, side)) < WaveWidth * 0.5f && Mathf.Abs(rel.y) < 2f)
                    { w.hit.Add(p.NetworkObjectId); DeliverHit(p, waveDamage, HitFlags.Dark, true); }
                }
            }
        }

        double _ringStart = -1;
        Vector3 _ringCenter;
        int _ringLive;
        readonly HashSet<ulong> _ringHit = new HashSet<ulong>();

        void SummonMinions()
        {
            if (minionPrefab == null) return;
            int players = NetworkManager.Singleton ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
            int n = players > 2 ? 3 : 2;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f + Random.Range(0f, 1f);
                Vector3 p = ClampToArena(transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 5f);
                var go = Instantiate(minionPrefab, p, Quaternion.LookRotation(MathUtil.Flat(p - transform.position).normalized + Vector3.forward * 0.01f));
                var no = go.GetComponent<NetworkObject>();
                no.Spawn(true);
                _minions.Add(no);
                SpectreRiseRpc(p);
            }
        }

        // ---- hit helpers
        void HitCircle(Vector3 c, float r, float damage, HitFlags flags)
        {
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && MathUtil.Flat(p.transform.position - c).magnitude < r + 0.4f && Mathf.Abs(p.transform.position.y - _groundY) < 2.5f)
                    DeliverHit(p, damage, flags, true);
        }

        void HitCone(float range, float halfAngle, float damage, HitFlags flags)
        {
            foreach (var p in PlayerController.All)
            {
                if (!p || p.IsDead) continue;
                Vector3 to = p.transform.position - transform.position;
                if (MathUtil.Flat(to).magnitude < range && Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to)) < halfAngle && Mathf.Abs(to.y) < 3f)
                    DeliverHit(p, damage, flags, true);
            }
        }

        void HitLine(Vector3 from, Vector3 dir, float length, float halfWidth, float damage, HitFlags flags)
        {
            dir = MathUtil.Flat(dir).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            foreach (var p in PlayerController.All)
            {
                if (!p || p.IsDead) continue;
                Vector3 rel = p.transform.position - from;
                float along = Vector3.Dot(rel, dir);
                if (along > -0.5f && along < length && Mathf.Abs(Vector3.Dot(rel, side)) < halfWidth && Mathf.Abs(rel.y) < 3f)
                    DeliverHit(p, damage, flags, true);
            }
        }

        static float DistToSegmentFlat(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            Vector3 ab = b - a;
            float k = ab.sqrMagnitude < 1e-5f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * k);
        }

        bool InArena(PlayerController p) => LevelRefs.Instance == null || LevelRefs.Instance.IsInsideReaperArena(p.transform.position);

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
                _netVeil.Value = false;
                _queue.Clear();
                ReleaseGrab(false);
                cc.enabled = true;
                SetMove(ReaperMove.Ascend);
                _queue.Enqueue(ReaperMove.Summon);
                Cool(ReaperMove.Summon, 26f);
            }
        }

        protected override void Stagger(float duration)
        {
            var m = CurrentMove;
            if (m == ReaperMove.Awaken || m == ReaperMove.Ascend || m == ReaperMove.Rain || m == ReaperMove.Lunge || m == ReaperMove.Blink || _netVeil.Value) return;
            _queue.Clear();
            ReleaseGrab(false);
            SetMove(ReaperMove.Stagger);
        }

        protected override void Die()
        {
            _queue.Clear(); _scythes.Clear(); _waves.Clear();
            ReleaseGrab(false);
            DespawnMinions();
            cc.enabled = true;
            _netVeil.Value = false;
            SetMove(ReaperMove.Death);
            base.Die();
            DeathRpc();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DevMove(string m) { if (IsServer && System.Enum.TryParse<ReaperMove>(m, true, out var mv)) { if (target == null) target = PickTarget(); StartMove(mv); } }
        public void DevHealth(string frac) { if (IsServer && float.TryParse(frac, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) { netHealth.Value = netMaxHealth.Value * Mathf.Clamp01(f); OnDamaged(transform.position); } }
        public void DevVeil(string _) { if (IsServer && _netActive.Value) { _veilTimer = 0f; } }
#endif

        // ================================================================== client fx
        static readonly Color Soul = new Color(0.55f, 0.75f, 1f);
        static readonly Color Wisp = new Color(0.62f, 0.35f, 1f);
        static readonly Color Blood = new Color(0.9f, 0.12f, 0.15f);

        [Rpc(SendTo.Everyone)]
        void MoveStartRpc(byte move, float p1)
        {
            Vector3 c = transform.position + Vector3.up * 3f;
            switch ((ReaperMove)move)
            {
                case ReaperMove.Awaken: Sfx.Play(SfxId.Roar, c, 1.4f, 0.45f); Sfx.Play(SfxId.MirrorHum, c, 1.4f, 0.5f); Burst(60); break;
                case ReaperMove.Slash: Sfx.Play(SfxId.Swing, c, 1.3f, 0.45f); Sfx.Play(SfxId.MirrorCurse, c, 0.9f, 0.6f); break;
                case ReaperMove.Crescent: Sfx.Play(SfxId.DragonTail, c, 1.2f, 0.75f); break;
                case ReaperMove.Lunge: Sfx.Play(SfxId.DragonSwoop, c, 1.3f, 0.8f); Burst(40); break;
                case ReaperMove.Blink: Sfx.Play(SfxId.FogPass, c, 1.2f, 0.7f); Burst(45); break;
                case ReaperMove.Grab: Sfx.Play(SfxId.MirrorCurse, c, 1.4f, 0.45f); Sfx.Play(SfxId.Roar, c, 0.8f, 0.7f); break;
                case ReaperMove.Rain: Sfx.Play(SfxId.MirrorHum, c, 1.6f, 0.45f); Sfx.Play(SfxId.Roar, c, 1f, 0.55f); break;
                case ReaperMove.Wave: Sfx.Play(SfxId.Swing, c, 1.2f, 0.6f); break;
                case ReaperMove.Summon: Sfx.Play(SfxId.MirrorCurse, c, 1.5f, 0.4f); Sfx.Play(SfxId.Roar, c, 1f, 0.5f); break;
                case ReaperMove.Ascend: Sfx.Play(SfxId.Roar, c, 1.6f, 0.38f); Sfx.Play(SfxId.MirrorVortex, c, 1.4f, 0.6f); break;
            }
        }

        void Burst(int n) { if (mistBurst) mistBurst.Emit(n); }

        [Rpc(SendTo.Everyone)]
        void SwingRpc(byte kind) { Sfx.Play(SfxId.Swing, transform.position + Vector3.up * 2.5f, 1.2f, 0.65f); }

        [Rpc(SendTo.Everyone)]
        void SlashWarnRpc(bool vertical, float t)
        {
            t = Mathf.Max(0.3f, t);
            if (vertical)
                for (int i = 1; i <= 5; i++) BossFx.GroundWarning(transform.position + transform.forward * (i * 2.1f), 1.3f, t, Wisp);
            else
                for (int i = -2; i <= 2; i++) BossFx.GroundWarning(transform.position + Quaternion.Euler(0f, i * 32f, 0f) * transform.forward * 5.5f, 2.4f, t, Wisp);
        }

        [Rpc(SendTo.Everyone)]
        void SlashRpc(bool vertical)
        {
            Vector3 c = transform.position + transform.forward * (vertical ? 5f : 3.5f);
            Sfx.Play(SfxId.MirrorSlam, c, 1.5f, 0.75f);
            Sfx.Play(SfxId.Swing, c, 1.4f, 0.5f);
            AshCrescent.Spawn(transform.position + transform.forward * 0.8f, transform.forward, vertical ? 26f : 14f, vertical ? 10f : 7f, vertical ? 1.6f : 10f, false, P2 ? new Color(1.2f, 0.9f, 3f, 1f) : new Color(0.9f, 1.1f, 1.6f, 1f));
            MirrorFx.DarkShock(c, vertical ? 2.5f : 4f);
            SoulsCamera.Shake(0.09f, 0.4f);
        }

        [Rpc(SendTo.Everyone)]
        void CrescentWarnRpc()
        {
            SpellFx.GroundRing(transform.position + Vector3.up * 0.05f, 1.2f, 2.2f, Wisp, 0.7f, 0.8f);
        }

        [Rpc(SendTo.Everyone)]
        void RingRpc(Vector3 c, bool twice)
        {
            Sfx.Play(SfxId.MirrorVortex, c, 1.3f, 1.1f);
            BossFx.RingWave(c, RingMax, RingSpeed, false);
            SpellFx.GroundRing(c + Vector3.up * 0.06f, 1.5f, RingMax, Wisp, RingMax / RingSpeed, 0.6f);
            if (twice) StartCoroutine(SecondRing(c));
        }

        System.Collections.IEnumerator SecondRing(Vector3 c)
        {
            yield return new WaitForSeconds(0.6f);
            Sfx.Play(SfxId.MirrorVortex, c, 1.2f, 1.3f);
            BossFx.RingWave(c, RingMax, RingSpeed, false);
            SpellFx.GroundRing(c + Vector3.up * 0.06f, 1.5f, RingMax, Soul, RingMax / RingSpeed, 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        void PulseRpc(Vector3 c, float r)
        {
            Sfx.Play(SfxId.Quake, c, 1.2f, 0.6f);
            MirrorFx.DarkShock(c, r);
            BossFx.RingWave(c, r + 2f, 16f, false);
            SoulsCamera.Shake(0.08f, 0.5f);
        }

        [Rpc(SendTo.Everyone)]
        void ShadowRpc(Vector3 c, float duration)
        {
            BossFx.GroundWarning(c, 9f, duration, new Color(0.15f, 0.02f, 0.08f));
            SoulsCamera.Shake(0.02f, duration);
        }

        [Rpc(SendTo.Everyone)]
        void ScytheFallRpc(Vector3 at, float flight)
        {
            BossFx.GroundWarning(at, 1.7f, flight, Wisp);
            if (spectralScytheMesh == null || spectralMaterial == null) return;
            var go = new GameObject("SpectralScythe");
            go.AddComponent<MeshFilter>().sharedMesh = spectralScytheMesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = spectralMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var f = go.AddComponent<FallingScythe>();
            f.Init(at, flight, Random.Range(0f, 360f), P2 ? Wisp : Soul);
        }

        [Rpc(SendTo.Everyone)]
        void WaveRpc(Vector3 from, Vector3 dir)
        {
            Sfx.Play(SfxId.Swing, from, 1.3f, 0.55f);
            AshCrescent.Spawn(from, dir, WaveSpeed, WaveRange, WaveWidth, false, P2 ? new Color(1.3f, 0.8f, 3.2f, 1f) : new Color(0.8f, 1.2f, 2.4f, 1f));
        }

        // ---- the hand (every client simulates it from the cast)
        Vector3 _hVel; bool _hFly; NetworkObject _hVictim; float _hHoldUntil; Vector3 _hHold;

        [Rpc(SendTo.Everyone)]
        void HandRpc(Vector3 from, Vector3 vel)
        {
            if (!handVisual) return;
            handVisual.gameObject.SetActive(true);
            handVisual.SetParent(null, true);
            handVisual.position = from;
            handVisual.rotation = Quaternion.LookRotation(vel);
            _hVel = vel; _hFly = true; _hVictim = null;
            Sfx.Play(SfxId.DragonSwoop, from, 1f, 1.3f);
        }

        [Rpc(SendTo.Everyone)]
        void GrabbedRpc(NetworkObjectReference victim, Vector3 hold, float duration)
        {
            _hFly = false;
            _hVictim = victim.TryGet(out NetworkObject no) ? no : null;
            _hHold = hold; _hHoldUntil = Time.time + duration;
            Sfx.Play(SfxId.MirrorCurse, hold, 1.4f, 0.5f);
            Sfx.Play(SfxId.Hurt, hold, 1f, 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        void HandMissRpc() { _hFly = false; _hVictim = null; _hHoldUntil = Time.time + 0.4f; }

        [Rpc(SendTo.Everyone)]
        void HandSlamRpc(Vector3 at)
        {
            Sfx.Play(SfxId.Slam, at, 1.4f, 0.7f);
            MirrorFx.DarkShock(at, 2.5f);
            SoulsCamera.Shake(0.1f, 0.4f);
            _hVictim = null; _hHoldUntil = Time.time + 0.3f;
        }

        void UpdateHand(float dt)
        {
            if (!handVisual || !handVisual.gameObject.activeSelf) return;
            if (_hFly)
            {
                handVisual.position += _hVel * dt;
                if (chest && (handVisual.position - chest.position).magnitude > HandRange + 1f) { _hFly = false; _hHoldUntil = Time.time + 0.3f; }
                return;
            }
            if (_hVictim != null && Time.time < _hHoldUntil)
            {
                Vector3 to = _hVictim.transform.position + Vector3.up * 1.1f;
                handVisual.position = Vector3.Lerp(handVisual.position, to, 1f - Mathf.Exp(-10f * dt));
                handVisual.rotation = Quaternion.Slerp(handVisual.rotation, Quaternion.LookRotation(Vector3.down + transform.forward * 0.3f), dt * 6f);
                return;
            }
            if (Time.time >= _hHoldUntil)
            {
                // the mist hand withers back into the cloak
                handVisual.localScale = Vector3.MoveTowards(handVisual.localScale, Vector3.zero, dt * 4f);
                if (handVisual.localScale.x <= 0.01f) { handVisual.gameObject.SetActive(false); handVisual.localScale = Vector3.one; }
            }
        }

        [Rpc(SendTo.Everyone)]
        void SpectreRiseRpc(Vector3 at)
        {
            MirrorFx.DarkShock(at, 2f);
            Sfx.Play(SfxId.MirrorCurse, at, 1f, 0.7f);
        }

        [Rpc(SendTo.Everyone)]
        void VeilRpc(bool on)
        {
            Burst(70);
            Sfx.Play(SfxId.FogPass, transform.position + Vector3.up * 3f, 1.3f, 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        void JudgementRpc()
        {
            Vector3 c = transform.position + Vector3.up * 3f;
            ApplyPhaseLook(true);
            Burst(120);
            Sfx.Play(SfxId.MirrorShatter, c, 1.6f, 0.45f);
            Sfx.Play(SfxId.FireBurst, c, 1.4f, 0.6f);
            BossFx.Flash(c, Wisp, 30f, 30f, 0.8f);
            SoulsCamera.Shake(0.1f, 1.5f);
            Judgement?.Invoke();
        }

        void ApplyPhaseLook(bool ecto)
        {
            if (bodyRenderers != null)
                foreach (var r in bodyRenderers)
                {
                    if (!r) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = ecto && ectoMaterial ? ectoMaterial : cloakMaterial ? cloakMaterial : mats[i];
                    r.sharedMaterials = mats;
                }
            if (wispFire) wispFire.SetActive(ecto);
            if (eyeLight) eyeLight.color = ecto ? Wisp : Blood;
        }

        [Rpc(SendTo.Everyone)]
        void DeathRpc()
        {
            Vector3 c = transform.position + Vector3.up * 3f;
            Burst(150);
            Sfx.Play(SfxId.Roar, c, 1.5f, 0.32f);
            Sfx.Play(SfxId.MirrorShatter, c, 1.6f, 0.4f);
            BossFx.Flash(c, Soul, 40f, 40f, 1.5f);
            SoulsCamera.Shake(0.08f, 2.5f);
        }

        /// <summary>Euler (pitch, yaw, roll) of the figure for a move at move time t: wind-ups and follow-throughs.</summary>
        static Vector3 PoseFor(ReaperMove m, float t, bool alive, float p1)
        {
            float S(float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));
            if (!alive) return new Vector3(-75f * S(0f, 2.2f), 0f, 12f * S(0f, 2.2f));
            switch (m)
            {
                case ReaperMove.Reap: return new Vector3(8f, Mathf.Lerp(-55f * S(0f, 0.22f), 70f, S(0.26f, 0.42f)) * (1f - S(0.6f, 0.95f)), 0f);
                case ReaperMove.Slash:
                    if (p1 > 0.5f)   // vertical: rears back, then brings the blade down in front
                        return new Vector3(Mathf.Lerp(-32f * S(0f, 0.65f), 42f, S(0.68f, 0.86f)) * (1f - S(1.2f, 1.8f)), 0f, 0f);
                    return new Vector3(Mathf.Lerp(-12f * S(0f, 0.9f), 14f, S(1.0f, 1.3f)) * (1f - S(1.8f, 2.5f)),
                                       Mathf.Lerp(-85f * S(0f, 0.95f), 105f, S(1.0f, 1.32f)) * (1f - S(1.8f, 2.5f)), 0f);
                case ReaperMove.Crescent: return new Vector3(0f, -40f * S(0f, 0.5f) + 400f * S(0.5f, 0.95f), 0f);
                case ReaperMove.Lunge: return new Vector3(38f * S(0.2f, 0.4f) * (1f - S(0.75f, 1.2f)), 0f, 0f);
                case ReaperMove.Wave: return new Vector3(0f, Mathf.Lerp(-50f * S(0f, 0.35f), 65f, S(0.38f, 0.55f)) * (1f - S(0.7f, 1.1f)), 0f);
                case ReaperMove.Grab: return new Vector3(22f * S(0.2f, 0.75f), 0f, 0f);
                case ReaperMove.Rain: return new Vector3(-10f * S(0f, 0.6f), 30f * Mathf.Sin(t * 1.4f), 0f);
                case ReaperMove.Awaken: case ReaperMove.Summon: case ReaperMove.Ascend: return new Vector3(-22f * S(0f, 0.8f), 0f, 0f);
                case ReaperMove.Stagger: return new Vector3(-24f, 0f, 9f);
                default: return Vector3.zero;
            }
        }

        // ================================================================== per-frame visuals (everyone)
        float _elev, _fade = 1f, _tick, _drag;
        Vector3 _lastPos, _glide;

        protected override void Update()
        {
            base.Update();
            float dt = Time.deltaTime;
            UpdateHand(dt);
            var m = CurrentMove;
            float t = MoveTime;
            bool alive = IsAlive;

            // mystic elevation: rises for the great spells, sinks when it dies
            float elev = 0f;
            if (m == ReaperMove.Rain) elev = t < RainEnd + 0.5f ? 4.2f : 0f;
            else if (m == ReaperMove.Grab) elev = 1.3f;
            else if (m == ReaperMove.Ascend) elev = 2.2f;
            else if (m == ReaperMove.Summon) elev = 1.5f;
            else if (m == ReaperMove.Awaken) elev = Mathf.Clamp01(t / 1.8f) * 0.6f;
            if (!alive) elev = -hoverHeight;
            _elev = Mathf.Lerp(_elev, elev, 1f - Mathf.Exp(-2.4f * dt));
            if (hover)
            {
                float bob = alive ? Mathf.Sin(Time.time * 1.3f) * 0.14f : 0f;
                hover.localPosition = new Vector3(0f, hoverHeight + _elev + bob, 0f);
                hover.localRotation = Quaternion.Euler(alive ? Mathf.Sin(Time.time * 0.7f) * 2f : 0f, 0f, alive ? Mathf.Sin(Time.time * 0.9f) * 2.5f : 0f);
            }

            // the cut itself: the whole figure turns, leans and spins with the scythe
            if (pose)
            {
                Vector3 e = PoseFor(m, t, alive, _netMove.Value.p1);
                // always alive: a slow breathing sway, and a lean into the glide
                Vector3 vel = (transform.position - _lastPos) / Mathf.Max(1e-4f, dt); _lastPos = transform.position;
                _glide = Vector3.Lerp(_glide, transform.InverseTransformDirection(MathUtil.Flat(vel)), 1f - Mathf.Exp(-4f * dt));
                if (alive)
                {
                    e.x += Mathf.Sin(Time.time * 0.9f) * 3f + Mathf.Clamp(_glide.z * 2.5f, -8f, 14f);
                    e.y += Mathf.Sin(Time.time * 0.55f) * 4f;
                    e.z += Mathf.Sin(Time.time * 0.7f + 1f) * 2.5f - Mathf.Clamp(_glide.x * 2.5f, -10f, 10f);
                }
                var target = Quaternion.Euler(e);
                pose.localRotation = Quaternion.Slerp(pose.localRotation, target, 1f - Mathf.Exp(-20f * dt));
                // dissolving: the figure shrinks into its smoke instead of popping out
                float sc = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, _fade));
                pose.localScale = new Vector3(sc, Mathf.Lerp(1.15f, 1f, _fade) * sc, sc);
                var fb = pose.GetComponentInChildren<GrimFlipbook>();
                if (fb) fb.speed = !alive ? 0.2f : _netPhase.Value >= 2 ? 1.7f : 1f;
            }

            // dissolve: smoke out, smoke in (and the Veil)
            bool hidden = Dissolved || _netVeil.Value || (!alive && t > 3.5f);
            float fadeTarget = hidden ? 0f : 1f;
            bool wasVisible = _fade > 0.15f;
            _fade = Mathf.MoveTowards(_fade, fadeTarget, dt * (hidden ? 4.5f : 3f));
            bool visible = _fade > 0.15f;
            if (visible != wasVisible) { Burst(35); }
            if (bodyRenderers != null) foreach (var r in bodyRenderers) if (r) r.enabled = visible;
            if (scytheRenderers != null) foreach (var r in scytheRenderers) if (r) r.enabled = visible;
            if (wispFire) wispFire.SetActive(visible && _netPhase.Value >= 2 && alive);
            if (eyeLight) eyeLight.intensity = visible && alive ? 3f + Mathf.Sin(Time.time * 4f) * 0.5f : 0f;
            if (mistTrail) { var em = mistTrail.emission; em.enabled = alive && _netActive.Value; }
            var lot = GetComponent<LockOnTarget>();
            if (lot) lot.enabled = _netActive.Value && alive && !_netVeil.Value;

            // the Veil of Darkness: the fog thickens, the clock ticks, the blade drags
            bool localInArena = PlayerController.Local && LevelRefs.Instance && LevelRefs.Instance.IsInsideReaperArena(PlayerController.Local.transform.position);
            float veil = _netVeil.Value && localInArena ? 0.07f : _netActive.Value && localInArena && alive ? 0.006f : 0f;
            AtmosphereController.VeilFog = Mathf.MoveTowards(AtmosphereController.VeilFog, veil, dt * 0.08f);
            if (_netVeil.Value && alive)
            {
                _tick -= dt; _drag -= dt;
                Vector3 at = transform.position + Vector3.up * 1.5f;
                if (_tick <= 0f) { _tick = 0.6f; Sfx.Play(SfxId.UiClick, at, 1.4f, 0.42f); }
                if (_drag <= 0f) { _drag = Random.Range(1.6f, 2.6f); Sfx.Play(SfxId.StepGravel, at, 1.3f, 0.5f); Sfx.Play(SfxId.Swing, at, 0.5f, 0.35f); }
            }
        }
    }
}
