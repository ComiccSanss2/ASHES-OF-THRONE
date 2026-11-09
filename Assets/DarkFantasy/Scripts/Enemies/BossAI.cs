using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DarkFantasy
{
    /// <summary>
    /// THE ASHEN KING — server-authoritative boss built around readable, punishing patterns.
    ///
    /// Moves are SEQUENCES of steps (combo strings). Each step is one swing with its own
    /// tracking, optional HOLD at the top of the swing (the classic delayed attack: the blade
    /// freezes, glints, then comes down fast) and an IMPACT event on the contact frame
    /// (dust, shockwaves, flying sword waves, fire bursts, eruptions…).
    ///
    /// Phase 1 – Ashen Knight (heavy, deliberate):
    ///   Ashen String (cleave → sweep → maybe a delayed overhead), Delayed Verdict,
    ///   Lunging Cleave (gap closer), Leaping Slam (jumps onto you, red circle shows where),
    ///   Ash Wave (flying crescent from a sweep), Turning Sweep (punishes standing behind him).
    /// Transition at 55% HP: he staggers, roars, the greatsword IGNITES and a fire nova
    ///   expands from him (roll through the ring).
    /// Phase 2 – Inferno (faster, every heavy impact explodes):
    ///   Inferno String (4 hits, last one delayed and explosive), Triple Fire Wave,
    ///   Double Leap, Eruption (fire pillars rise under every player, 3+ waves),
    ///   Inferno Whirlwind (chasing fire tornado + final burst), Earthquake (3 rings).
    /// Desperation (below 25%): shorter breathers, more eruptions.
    ///
    /// HP/poise scaling: base × (1 + (players − 1) × 0.6) when the fight starts.
    /// </summary>
    public class BossAI : EnemyBase
    {
        public static BossAI Instance { get; private set; }
        public static event Action Ignited;

        enum State { Dormant, Intro, Combat, Recover, Staggered, PhaseTransition, Dead, Flinch, Evade, ChargePrep, Charging }
        enum Pace { Approach, Circle, Retreat, Watch }
        enum Impact : byte { None, Dust, Burst, Shockwave, TripleShockwave, Crescent, TripleCrescent, Eruption, LeapLand }

        class Step
        {
            public AttackData data;
            public HitShape shape = HitShape.FrontArc;
            public float range = 4.8f, angle = 60f;
            public HitFlags flags;
            public float hold;                 // seconds frozen at the top of the swing (0 = none)
            public float tracking = 0.6f;
            public Impact impact;
            public bool leap, spin;
            public bool charged;              // follows a run-up: short wind-up
            public float lungeOverride = -1f;
        }

        [Header("Boss")]
        [Tooltip("Increase for every player beyond the first (0.6 = +60%).")]
        public float perPlayerScaling = 0.6f;
        [Range(0, 1)] public float phase2Threshold = 0.55f;
        [Range(0, 1)] public float desperationThreshold = 0.25f;
        public float phase2SpeedMultiplier = 1.15f;
        public float engageRange = 40f;

        [Header("Moveset (swing timings filled by the builder from clip analysis)")]
        public AttackData cleave = new AttackData { name = "Ashen Cleave", state = CharacterAnimator.Attack1, animSpeed = 0.72f, damage = 30, poiseDamage = 45, lunge = 1.2f };
        public AttackData sweep = new AttackData { name = "King's Sweep", state = CharacterAnimator.Attack2, animSpeed = 0.68f, damage = 32, poiseDamage = 45, lunge = 1.0f };
        public AttackData overhead = new AttackData { name = "Verdict", state = CharacterAnimator.Heavy, animSpeed = 0.6f, damage = 46, poiseDamage = 90, lunge = 0.8f, parryable = false };
        public AttackData lungeCleave = new AttackData { name = "Lunging Cleave", state = CharacterAnimator.Attack1, animSpeed = 0.75f, damage = 34, poiseDamage = 60, lunge = 7f };
        public AttackData leapSlam = new AttackData { name = "Leaping Slam", state = CharacterAnimator.Heavy, animSpeed = 0.8f, damage = 44, poiseDamage = 90, lunge = 0f, parryable = false };
        public AttackData waveSlash = new AttackData { name = "Ash Wave", state = CharacterAnimator.Attack2, animSpeed = 0.8f, damage = 24, poiseDamage = 40, lunge = 0.4f, parryable = false };
        public AttackData turnSweep = new AttackData { name = "Turning Sweep", state = CharacterAnimator.Attack2, animSpeed = 1.05f, damage = 26, poiseDamage = 40, lunge = 0.3f };
        public AttackData plunge = new AttackData { name = "Eruption", state = CharacterAnimator.Heavy, animSpeed = 0.7f, damage = 30, poiseDamage = 70, lunge = 0.2f, parryable = false };
        public AttackData whirlwind = new AttackData { name = "Inferno Whirlwind", state = CharacterAnimator.Spin, animSpeed = 1f, damage = 12, poiseDamage = 20, parryable = false };

        [Header("Area attacks")]
        public float shockwaveSpeed = 9f;
        public float shockwaveDamage = 26f;
        public float waveProjectileSpeed = 16f;
        public float waveProjectileDamage = 26f;
        public float pillarRadius = 2.1f;
        public float pillarDelay = 1.0f;
        public float pillarDamage = 32f;
        public float burstDamage = 22f;
        public float tornadoRadius = 3.8f;
        public float tornadoTick = 0.4f;
        public float tornadoChaseSpeed = 2.9f;

        [Header("VFX")]
        public GameObject shockwavePrefab;

        readonly NetworkVariable<bool> _netActive = new NetworkVariable<bool>(false);
        readonly NetworkVariable<byte> _netPhase = new NetworkVariable<byte>(1);
        readonly NetworkVariable<int> _netScaledFor = new NetworkVariable<int>(1);

        State _state;
        float _stateTimer;
        readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();
        readonly Queue<Step> _seq = new Queue<Step>();
        Step _step;
        string _lastMove;
        int _strafeSign = 1;

        // ---- reading the player / pacing
        Pace _pace;
        int _movesInRow;
        int _fatigueAfter = 3;
        float _rollMemory, _farTime;
        PlayerController.Act _lastAct;
        // ---- flinch (knockback) and evasive hop
        float _flinchMeter, _flinchCooldown;
        float _chargeDustTimer;

        [Header("Flinch / pacing")]
        [Tooltip("Share of max HP dealt in quick succession that makes the king recoil (with knockback).")]
        public float flinchDamageShare = 0.045f;
        public float flinchCooldown = 6f;
        public float chargeSpeed = 8f;

        // delayed swing
        bool _held, _holdDone; float _holdTimer, _holdAt;
        // leap
        bool _airborne; Vector3 _leapFrom, _leapTo; float _leapT0, _leapT1;
        // whirlwind
        float _tornadoTickTimer;
        // phase transition
        float _ptTime; int _ptStage;

        struct Wave { public Vector3 center; public float radius, max, damage; public HashSet<ulong> hit; }
        readonly List<Wave> _waves = new List<Wave>();
        struct Proj { public Vector3 pos, dir; public float travelled, max, width, damage; public HashSet<ulong> hit; }
        readonly List<Proj> _projectiles = new List<Proj>();
        readonly List<(float time, Action action)> _scheduled = new List<(float, Action)>();

        BossVisuals _visuals;

        public bool FightActive => _netActive.Value;
        public int Phase => _netPhase.Value;
        public int ScaledForPlayers => _netScaledFor.Value;
        public override bool IsStaggered => _state == State.Staggered;
        protected override bool IsInvulnerableVisual => !_netActive.Value;
        protected override float GruntPitch => 0.62f;
        protected override float GruntChance => 0.4f;
        protected override float DamageScale => 0.8f;
        protected override float MinWindup => _step != null && _step.charged ? 0.18f : _step != null && _step.spin ? 0.5f : Desperate ? 0.38f : P2 ? 0.45f : 0.6f;
        protected override Color TrailColor => new Color(0.9f, 0.88f, 0.82f, 0.95f);
        protected override bool IsInvulnerable => _state == State.PhaseTransition || _state == State.Dormant || _state == State.Intro;

        bool P2 => _netPhase.Value >= 2;
        bool Desperate => P2 && Health01 <= desperationThreshold;
        float SpeedMul => Desperate ? phase2SpeedMultiplier * 1.08f : P2 ? phase2SpeedMultiplier : 1f;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            knockbackFactor = 0.12f;
            stanceImmunity = 6f;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            var lot = GetComponent<LockOnTarget>();
            if (lot) lot.enabled = _netActive.Value && IsAlive;
            _netActive.OnValueChanged += (a, b) => { var l = GetComponent<LockOnTarget>(); if (l) l.enabled = b && IsAlive; };

            _visuals = gameObject.AddComponent<BossVisuals>();
            _visuals.Setup(anim, weapon, FindHead());
            if (_netActive.Value && _netPhase.Value >= 2 && IsAlive) _visuals.Ignite(true);
        }

        Transform FindHead()
        {
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                if (n == "head" || n.EndsWith("_head") || n.EndsWith(".head")) return t;
            }
            var lot = GetComponent<LockOnTarget>();
            return lot ? lot.lockPoint : null;
        }

        // ================================================================== arena control (server)
        /// <summary>Called by BossArena when the first player enters.</summary>
        public void Activate(int playerCount)
        {
            if (!IsServer) return;
            playerCount = Mathf.Max(1, playerCount);
            float mul = 1f + (playerCount - 1) * perPlayerScaling;   // HP_Final = HP_Base * (1 + (N-1)*0.6)
            SetupStats(mul);
            _netScaledFor.Value = playerCount;
            _netPhase.Value = 1;
            _netActive.Value = true;
            _state = State.Intro;
            _stateTimer = 2.6f;
            target = FindNearestPlayer(engageRange, false);
            if (target) transform.rotation = Quaternion.LookRotation(MathUtil.Flat(target.transform.position - transform.position));
            PlayAnimRpc(StateIndex(CharacterAnimator.Taunt), 0.2f, 0.8f);
            RoarRpc(false);
            Debug.Log($"[DF] Boss awakened for {playerCount} player(s) → HP {netMaxHealth.Value:0} (base {baseHealth}), poise {maxPoise:0}");
        }

        /// <summary>Everyone died / left: back to the throne at full health.</summary>
        public void ResetBoss()
        {
            if (!IsServer) return;
            ClearCombat();
            ResetToHome(1f);
            _netActive.Value = false;
            _netPhase.Value = 1;
            _state = State.Dormant;
            ExtinguishRpc();
        }

        void ClearCombat()
        {
            EndLeap();
            _waves.Clear(); _projectiles.Clear(); _scheduled.Clear(); _seq.Clear();
            _held = false; _step = null;
        }

        // ================================================================== FSM
        protected override void ServerTick(float dt)
        {
            UpdateWaves(dt);
            UpdateProjectiles(dt);
            UpdateScheduled();
            _stateTimer -= dt;
            if (_cooldowns.Count > 0)
            {
                var keys = new List<string>(_cooldowns.Keys);
                foreach (var k in keys) _cooldowns[k] -= dt;
            }

            if (_state != State.Dormant && (target == null || target.IsDead || !InArena(target)))
                target = PickTarget();
            UpdateMemory(dt);

            switch (_state)
            {
                case State.Dormant:
                    StopMoving(dt);
                    break;

                case State.Intro:
                    StopMoving(dt);
                    if (target) FaceTowards(target.transform.position, dt, 0.5f);
                    if (_stateTimer <= 0f) EnterRecover(0.2f);
                    break;

                case State.Combat:
                    if (IsAttacking)
                    {
                        if (_step != null && _step.leap) UpdateLeap();
                        else if (_step != null && _step.spin) UpdateSpin(dt);
                        else StopMoving(dt);
                        UpdateHold(dt);
                    }
                    else if (target == null) { StopMoving(dt); EnterRecover(0.5f); }
                    else
                    {
                        float d = DistanceToTarget;
                        FaceTowards(target.transform.position, dt, 0.9f);
                        if (!TryChooseMove(d)) MoveTowards(target.transform.position - transform.position, d > 9f ? chaseSpeed : walkSpeed * 1.4f, dt);
                        else StopMoving(dt);
                    }
                    break;

                case State.Recover:
                    if (target)
                    {
                        FaceTowards(target.transform.position, dt, 0.8f);
                        float d = DistanceToTarget;
                        Vector3 to = (target.transform.position - transform.position).normalized;
                        Vector3 side = Vector3.Cross(Vector3.up, to) * _strafeSign;
                        switch (_pace)
                        {
                            case Pace.Approach:
                                if (d > 4.5f) MoveTowards(to + side * 0.15f, walkSpeed * 1.25f, dt);
                                else MoveTowards(side, walkSpeed * 0.6f, dt);
                                break;
                            case Pace.Retreat:
                                // backs off to a safe distance, facing you
                                if (d < 8f) MoveTowards(-to + side * 0.35f, walkSpeed * 0.95f, dt);
                                else MoveTowards(side, walkSpeed * 0.6f, dt);
                                break;
                            case Pace.Watch:
                                StopMoving(dt);
                                break;
                            default: // circle: sizing you up, sword low
                                if (d > 9f) MoveTowards(to, walkSpeed, dt);
                                else MoveTowards(side + (d < 3.5f ? -to * 0.6f : Vector3.zero), walkSpeed * 0.7f, dt);
                                break;
                        }
                    }
                    else StopMoving(dt);
                    if (_stateTimer <= 0f) _state = State.Combat;
                    break;

                case State.Staggered:
                    StopMoving(dt);
                    if (_stateTimer <= 0f) EnterRecover(0.35f);
                    break;

                case State.Flinch:
                    StopMoving(dt);
                    if (_stateTimer <= 0f)
                    {
                        // after being driven back he either re-engages carefully or puts distance between you
                        if (Random.value < 0.45f && Ready("evade")) BeginEvade();
                        else EnterRecover(Random.Range(0.4f, 0.9f), Random.value < 0.5f ? Pace.Watch : Pace.Circle);
                    }
                    break;

                case State.Evade:
                    StopMoving(dt);
                    if (target) FaceTowards(target.transform.position, dt, 1.5f);
                    if (_stateTimer <= 0f) AfterEvade();
                    break;

                case State.ChargePrep:
                    StopMoving(dt);
                    if (target) FaceTowards(target.transform.position, dt, 2.5f);
                    if (_stateTimer <= 0f)
                    {
                        _state = State.Charging;
                        _stateTimer = 1.7f;
                        PlayAnimRpc(StateIndex(CharacterAnimator.Locomotion), 0.15f, 1f);
                    }
                    break;

                case State.Charging:
                    if (target == null) { EnterRecover(0.5f); break; }
                    FaceTowards(target.transform.position, dt, 2.2f);
                    MoveTowards(target.transform.position - transform.position, chargeSpeed * SpeedMul, dt);
                    _chargeDustTimer -= dt;
                    if (_chargeDustTimer <= 0f) { _chargeDustTimer = 0.22f; DustRpc(transform.position, false); }
                    if (DistanceToTarget < 5.2f) ChargeStrike();
                    else if (_stateTimer <= 0f) EnterRecover(0.5f, Pace.Watch);   // you got away
                    break;

                case State.PhaseTransition:
                    StopMoving(dt);
                    UpdatePhaseTransition(dt);
                    break;
            }

            if (_netPhase.Value == 1 && Health01 <= phase2Threshold && _state != State.PhaseTransition && _state != State.Dead && _state != State.Dormant)
                BeginPhaseTransition();
        }

        bool InArena(PlayerController p) => LevelRefs.Instance == null || LevelRefs.Instance.IsInsideArena(p.transform.position);

        PlayerController PickTarget()
        {
            PlayerController best = null; float bestD = float.MaxValue;
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead || !InArena(p)) continue;
                float d = Vector3.Distance(p.transform.position, transform.position) * Random.Range(0.8f, 1.2f);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        bool Ready(string key) => !_cooldowns.TryGetValue(key, out float t) || t <= 0f;

        void EnterRecover(float t) => EnterRecover(t, ChoosePace());

        void EnterRecover(float t, Pace pace)
        {
            // fatigue: after a few attacks in a row he takes a real breather
            if (_movesInRow >= _fatigueAfter && _state == State.Combat)
            {
                t = t * 1.6f + 1.2f;
                pace = Random.value < 0.4f ? Pace.Retreat : Random.value < 0.5f ? Pace.Circle : Pace.Watch;
                _movesInRow = 0;
                _fatigueAfter = Random.Range(2, 4);
            }
            // a retreat or a stare-down only reads as intent if it lasts long enough to see it
            if (pace == Pace.Retreat) t = Mathf.Max(t, Random.Range(2.2f, 3.2f));
            else if (pace == Pace.Watch) t = Mathf.Max(t, Random.Range(1.2f, 2f));
            _state = State.Recover;
            _stateTimer = t;
            _pace = pace;
            _strafeSign = Random.value < 0.5f ? 1 : -1;
            PlayAnimRpc(StateIndex(CharacterAnimator.Locomotion), 0.3f, 1f);
        }

        Pace ChoosePace()
        {
            float approach = 0.35f, circle = 0.35f, retreat = 0.15f, watch = 0.15f;
            if (target && target.CurrentAct == PlayerController.Act.Drinking) { approach += 2f; }
            if (pressure >= 1.5f) retreat += 0.45f;
            if (target && target.CurrentAct == PlayerController.Act.Blocking) { retreat += 0.2f; watch += 0.2f; }
            if (Desperate) approach += 0.3f;
            if (target && DistanceToTarget > 9f) { approach += 0.4f; retreat = 0f; }
            float r = Random.value * (approach + circle + retreat + watch);
            if ((r -= approach) < 0f) return Pace.Approach;
            if ((r -= circle) < 0f) return Pace.Circle;
            if ((r -= retreat) < 0f) return Pace.Retreat;
            return Pace.Watch;
        }

        /// <summary>Reads what the player is doing (replicated state) and remembers habits.</summary>
        void UpdateMemory(float dt)
        {
            _flinchCooldown -= dt;
            _flinchMeter = Mathf.Max(0f, _flinchMeter - dt * 0.1f);
            _rollMemory = Mathf.Max(0f, _rollMemory - dt * 0.12f);
            if (target == null) return;
            var act = target.CurrentAct;
            if (act == PlayerController.Act.Rolling && _lastAct != PlayerController.Act.Rolling) _rollMemory += 1f;
            _lastAct = act;
            _farTime = DistanceToTarget > 8f ? _farTime + dt : 0f;
        }

        float RecoverTime() => Desperate ? Random.Range(0.4f, 1.0f) : P2 ? Random.Range(0.8f, 1.7f) : Random.Range(1.3f, 2.5f);

        // ================================================================== move selection
        bool TryChooseMove(float d)
        {
            float angle = target ? Vector3.Angle(transform.forward, MathUtil.Flat(target.transform.position - transform.position)) : 0f;
            const float reach = 5.0f;
            var opts = new List<(string key, float w, float cd, Action run)>();
            void Add(string key, float w, float cd, Func<List<Step>> build) => opts.Add((key, w, cd, () => Run(build())));

            // ---- reading the player
            var act = target ? target.CurrentAct : PlayerController.Act.Free;
            bool drinking = act == PlayerController.Act.Drinking;
            bool blocking = act == PlayerController.Act.Blocking;
            bool rollHappy = _rollMemory >= 1.5f;                       // dodges a lot → delayed swings
            float heavyBias = (blocking ? 2.5f : 1f) * (rollHappy ? 2f : 1f);
            float comboBias = (blocking ? 0.6f : 1f) * (rollHappy ? 0.8f : 1f);

            // drinking a flask in front of him is punished
            if (drinking && Ready("punish"))
            {
                if (d > 5.5f) opts.Add(("punish", 12f, 6f, BeginCharge));
                else Add("punish", 12f, 6f, P2 ? (Func<List<Step>>)InfernoString : AshenString);
            }
            // being pressed hard: he disengages (hop back), then decides from range
            if (pressure >= 2f && d < 6f && Ready("evade")) opts.Add(("evade", 3.5f, 8f, BeginEvade));

            if (angle > 105f && d < 5.5f && Ready("turn")) Add("turn", 8f, 4f, TurnSweep);
            if (d <= reach)
            {
                if (Ready("string")) Add("string", 2.4f * comboBias, 4f, P2 ? (Func<List<Step>>)InfernoString : AshenString);
                else Add("poke", 1f, 0f, () => new List<Step> { Random.value < 0.5f ? S(cleave, 4.8f, 45f, tracking: 1f) : S(sweep, 5.2f, 85f, tracking: 1f) });
                if (Ready("verdict")) Add("verdict", 2f * heavyBias, 5f, DelayedVerdict);
                if (P2 && Ready("whirl")) Add("whirl", 1.4f, 14f, Whirlwind);
                // sometimes he gives ground on purpose, then punishes the gap from range (charge, wave, leap)
                if (Ready("backoff")) opts.Add(("backoff", 1.1f + (blocking ? 0.8f : 0f), 9f, () => EnterRecover(Random.Range(2.4f, 3.2f), Pace.Retreat)));
                if (P2 && Ready("quake")) Add("quake", 1.3f * (blocking ? 2f : 1f), 10f, Earthquake);
                if (P2 && Ready("erupt")) Add("erupt", (Desperate ? 3f : 1.2f) * (rollHappy ? 1.5f : 1f), Desperate ? 8f : 13f, Eruption);
            }
            else
            {
                float far = _farTime > 3f ? 1.8f : 1f;                   // keeps his distance? → closes it
                if (d > 6f && d < 18f && Ready("charge")) opts.Add(("charge", 2.2f * far, 6f, BeginCharge));
                if (d > 7f && d < 22f && Ready("leap")) Add("leap", 2.4f * far, P2 ? 7f : 9f, P2 ? (Func<List<Step>>)DoubleLeap : Leap);
                if (d > 5.5f && d < 20f && Ready("wave")) Add("wave", 2f, 6f, P2 ? (Func<List<Step>>)TripleWave : AshWave);
                if (P2 && d > 6f && Ready("erupt")) Add("erupt", Desperate ? 3f : 1.6f, Desperate ? 8f : 13f, Eruption);
                if (P2 && d < 10f && Ready("whirl")) Add("whirl", 1f, 14f, Whirlwind);
            }
            if (opts.Count > 1) opts.RemoveAll(o => o.key == _lastMove);
            if (opts.Count == 0) return false;

            float total = 0f; foreach (var o in opts) total += o.w;
            float r = Random.value * total;
            foreach (var o in opts)
            {
                r -= o.w;
                if (r > 0f) continue;
                if (o.cd > 0f) _cooldowns[o.key] = o.cd;
                _lastMove = o.key;
                Debug.Log($"[DF] Boss move: {o.key} (d {d:0.0}, phase {_netPhase.Value}, player {act}, rolls {_rollMemory:0.0}, pressure {pressure:0.0})");
                _movesInRow++;
                o.run();
                return true;
            }
            return false;
        }

        // ---- step factory
        Step S(AttackData a, float range, float angle, HitFlags flags = HitFlags.None, float hold = 0f, float tracking = 0.6f, Impact impact = Impact.None)
            => new Step { data = a, range = range, angle = angle, flags = flags, hold = hold, tracking = tracking, impact = impact };

        float HoldTime(float min, float max) => Random.Range(min, max) / (Desperate ? 1.3f : 1f);

        // ---- phase 1
        List<Step> AshenString()
        {
            var l = new List<Step> { S(cleave, 4.8f, 45f, tracking: 0.9f), S(sweep, 5.2f, 85f, tracking: 0.8f) };
            if (Random.value < 0.45f) l.Add(S(overhead, 5f, 32f, HitFlags.Heavy, hold: Random.value < 0.6f ? HoldTime(0.35f, 0.9f) : 0f, tracking: 1f, impact: Impact.Dust));
            return l;
        }

        List<Step> DelayedVerdict() => new List<Step>
        {
            S(overhead, 5f, 32f, HitFlags.Heavy, hold: HoldTime(0.55f, 1.25f), tracking: 1.1f, impact: P2 ? Impact.Burst : Impact.Dust)
        };

        List<Step> LungeCleave()
        {
            var l = new List<Step> { S(lungeCleave, 4.6f, 45f, HitFlags.Heavy, tracking: 0.9f, impact: Impact.Dust) };
            if (Random.value < 0.4f) l.Add(S(sweep, 5.2f, 85f, tracking: 0.9f));
            return l;
        }

        List<Step> Leap()
        {
            var st = S(leapSlam, 4.4f, 180f, HitFlags.Heavy, tracking: 0f, impact: Impact.LeapLand);
            st.shape = HitShape.Radial; st.leap = true;
            return new List<Step> { st };
        }

        List<Step> DoubleLeap()
        {
            var l = Leap();
            if (Random.value < 0.6f) l.AddRange(Leap());
            return l;
        }

        List<Step> AshWave() => new List<Step> { S(waveSlash, 5.2f, 85f, tracking: 1f, impact: Impact.Crescent) };

        List<Step> TurnSweep() => new List<Step> { S(turnSweep, 5.2f, 110f, tracking: 3.2f) };

        // ---- phase 2
        List<Step> InfernoString() => new List<Step>
        {
            S(cleave, 4.8f, 45f, tracking: 0.9f),
            S(sweep, 5.2f, 85f, tracking: 0.9f),
            S(cleave, 4.8f, 45f, tracking: 1f, hold: Random.value < 0.5f ? HoldTime(0.3f, 0.7f) : 0f),
            S(overhead, 5f, 32f, HitFlags.Heavy, hold: Random.value < 0.5f ? HoldTime(0.4f, 1f) : 0f, tracking: 1.1f, impact: Impact.Burst),
        };

        List<Step> TripleWave() => new List<Step>
        {
            S(waveSlash, 5.2f, 85f, tracking: 1f, impact: Impact.Crescent),
            S(waveSlash, 5.2f, 85f, tracking: 1.2f, impact: Impact.TripleCrescent),
        };

        List<Step> Eruption() => new List<Step> { S(plunge, 3.6f, 50f, HitFlags.Heavy, tracking: 0.7f, impact: Impact.Eruption) };

        List<Step> Earthquake() => new List<Step> { S(overhead, 5f, 32f, HitFlags.Heavy, hold: HoldTime(0.2f, 0.5f), tracking: 1f, impact: Impact.TripleShockwave) };

        List<Step> Whirlwind()
        {
            var st = new Step { data = whirlwind, shape = HitShape.Radial, range = 0f, angle = 0f, flags = HitFlags.AreaOfEffect, spin = true, tracking = 0f };
            return new List<Step> { st };
        }

        // ================================================================== sequence runner
        void Run(List<Step> seq)
        {
            _seq.Clear();
            foreach (var s in seq) _seq.Enqueue(s);
            _state = State.Combat;
            BeginNext();
        }

        void BeginNext()
        {
            if (_seq.Count == 0) return;
            _step = _seq.Dequeue();
            var data = _step.data.Clone();
            data.animSpeed *= SpeedMul;
            if (_step.lungeOverride >= 0f) data.lunge = _step.lungeOverride;
            if (_seq.Count > 0 && !_step.leap) data.endTime = Mathf.Min(data.endTime, data.comboWindowStart + 0.06f);   // flow into the next swing
            trackingMul = _step.tracking * (P2 ? 1.2f : 1f);
            _held = false;
            _holdDone = _step.hold <= 0f;
            _holdAt = data.hitStart - 0.12f;

            if (_step.spin)
            {
                float loop = Mathf.Max(0.3f, data.endTime);
                data.endTime = loop * 3f; data.cancelTime = data.endTime; data.comboWindowStart = data.endTime;
                data.hitStart = 0.25f; data.hitEnd = data.endTime - 0.2f; data.swingPeak = 0.3f;
                data.lunge = 0f;
                _tornadoTickTimer = 0.3f;
                FlameRingRpc(data.endTime / Mathf.Max(0.1f, data.animSpeed));
            }
            if (_step.leap) PrepareLeap(data);

            StartAttack(data, _step.shape, _step.range, _step.angle, _step.flags);
        }

        protected override void OnAttackFinished(AttackData a)
        {
            if (_state == State.Dead || _state == State.Staggered || _state == State.PhaseTransition || _state == State.Flinch) return;
            if (_step != null && _step.spin)
            {
                Vector3 c = transform.position;
                BurstRpc(c + Vector3.up * 1.5f, 5f);
                AreaDamage(c, 5f, burstDamage + 6f, HitFlags.AreaOfEffect | HitFlags.Heavy);
                SpawnWave(c, 10f, true);
            }
            if (_step != null && _step.leap) EndLeap();
            if (_seq.Count > 0 && target != null && !target.IsDead) { BeginNext(); return; }
            _step = null;
            EnterRecover(RecoverTime());
        }

        // ---- delayed swing: freeze at the top, glint, then a fast release
        void UpdateHold(float dt)
        {
            if (_step == null || _holdDone || currentAttack == null || InPreHold) return;
            if (!_held && attackClock >= _holdAt)
            {
                _held = true;
                _holdTimer = _step.hold;
                SetAnimSpeedRpc(0.035f);
                TelegraphNow(currentAttack.parryable);
            }
            else if (_held)
            {
                _holdTimer -= dt;
                if (_holdTimer <= 0f)
                {
                    _held = false; _holdDone = true;
                    SetAnimSpeedRpc(currentAttack.animSpeed * 1.45f);
                }
            }
        }

        // ---- leap
        void PrepareLeap(AttackData data)
        {
            _leapFrom = transform.position;
            Vector3 tp = target ? target.transform.position : transform.position + transform.forward * 8f;
            Vector3 flat = MathUtil.Flat(tp - _leapFrom);
            float dist = flat.magnitude;
            _leapTo = dist > 1.5f ? tp - flat.normalized * 1.3f : _leapFrom;
            if (LevelRefs.Instance && !LevelRefs.Instance.IsInsideArena(_leapTo)) _leapTo = Vector3.Lerp(_leapFrom, _leapTo, 0.6f);
            _leapTo.y = GroundY(_leapTo);
            float span = Mathf.Max(0.2f, data.swingPeak - data.startTime);
            data.animSpeed = Mathf.Clamp(span / 1.45f, 0.3f, 1.4f);
            data.lunge = 0f;
            _leapT0 = data.startTime + span * 0.2f;
            _leapT1 = data.swingPeak - 0.02f;
            if (dist > 0.5f) transform.rotation = Quaternion.LookRotation(flat);
            float flight = (_leapT1 - data.startTime) / data.animSpeed;
            WarningRpc(_leapTo, 4.6f, flight, false);
        }

        void UpdateLeap()
        {
            float u = Mathf.InverseLerp(_leapT0, _leapT1, attackClock);
            if (!_airborne && u > 0f && u < 1f)
            {
                _airborne = true;
                cc.enabled = false;
                LeapFxRpc(transform.position);
            }
            if (_airborne)
            {
                Vector3 p = Vector3.Lerp(_leapFrom, _leapTo, u);
                p.y += 4.2f * 4f * u * (1f - u);
                transform.position = p;
                if (u >= 1f) EndLeap();
            }
        }

        void EndLeap()
        {
            if (!_airborne) return;
            _airborne = false;
            transform.position = _leapTo;
            cc.enabled = true;
        }

        float GroundY(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return hit.point.y + 0.05f;
            return p.y;
        }

        // ---- whirlwind
        void UpdateSpin(float dt)
        {
            if (target) MoveTowards(target.transform.position - transform.position, tornadoChaseSpeed * SpeedMul, dt);
            _tornadoTickTimer -= dt;
            if (_tornadoTickTimer <= 0f && attackClock > currentAttack.hitStart && attackClock < currentAttack.hitEnd)
            {
                _tornadoTickTimer = tornadoTick;
                AreaDamage(transform.position, tornadoRadius, whirlwind.damage, HitFlags.AreaOfEffect, true);
            }
        }

        // ================================================================== impacts (contact frame)
        Vector3 BladePoint(float range) => transform.position + transform.forward * range * 0.8f;

        protected override void OnAttackImpact(AttackData a)
        {
            if (_step == null) return;
            Vector3 bp = BladePoint(_step.range);
            switch (_step.impact)
            {
                case Impact.Dust:
                    DustRpc(bp, true);
                    break;
                case Impact.Burst:
                    DustRpc(bp, true);
                    BurstRpc(bp + Vector3.up * 0.6f, 3.4f);
                    AreaDamage(bp, 3.4f, burstDamage, HitFlags.AreaOfEffect);
                    SpawnWave(bp, 7f, true);
                    break;
                case Impact.Shockwave:
                    DustRpc(bp, true);
                    SpawnWave(bp, 12f, P2);
                    break;
                case Impact.TripleShockwave:
                    DustRpc(bp, true);
                    SpawnWave(bp, 14f, P2);
                    Schedule(0.6f, () => SpawnWave(bp, 14f, P2));
                    Schedule(1.2f, () => SpawnWave(bp, 14f, P2));
                    break;
                case Impact.Crescent:
                    LaunchWave(0f);
                    break;
                case Impact.TripleCrescent:
                    LaunchWave(-24f); LaunchWave(0f); LaunchWave(24f);
                    break;
                case Impact.Eruption:
                    DustRpc(bp, true);
                    BurstRpc(bp, 2.5f);
                    int waves = Desperate ? 4 : 3;
                    for (int w = 0; w < waves; w++) Schedule(0.35f + w * 0.85f, EruptionWave);
                    break;
                case Impact.LeapLand:
                    EndLeap();
                    DustRpc(transform.position, true);
                    SpawnWave(transform.position, P2 ? 11f : 7f, P2);
                    if (P2) BurstRpc(transform.position + Vector3.up, 4f);
                    break;
            }
        }

        void EruptionWave()
        {
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead || !InArena(p)) continue;
                Vector3 pos = p.transform.position;
                WarningRpc(pos, pillarRadius, pillarDelay, true);
                Schedule(pillarDelay, () => PillarStrike(pos));
            }
            if (Desperate)
            {
                // extra pillars scattered around the king
                for (int i = 0; i < 2; i++)
                {
                    Vector3 pos = transform.position + Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward * Random.Range(4f, 9f);
                    WarningRpc(pos, pillarRadius, pillarDelay, true);
                    Schedule(pillarDelay, () => PillarStrike(pos));
                }
            }
        }

        void PillarStrike(Vector3 pos)
        {
            PillarRpc(pos);
            AreaDamage(pos, pillarRadius, pillarDamage, HitFlags.AreaOfEffect | HitFlags.Heavy, true);
        }

        void AreaDamage(Vector3 center, float radius, float dmg, HitFlags flags, bool ignoreOnce = false)
        {
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead) continue;
                Vector3 to = p.transform.position - center;
                if (Mathf.Abs(to.y) > 3f) continue;
                if (MathUtil.Flat(to).magnitude <= radius + 0.3f) DeliverHit(p, dmg, flags, ignoreOnce);
            }
        }

        // ---- flying sword waves (server hit segment = client visual)
        void LaunchWave(float yawOffset)
        {
            Vector3 dir = target ? MathUtil.Flat(target.transform.position - transform.position).normalized : transform.forward;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir = Quaternion.Euler(0, yawOffset, 0) * dir;
            Vector3 start = transform.position + dir * 1.6f;
            start.y = GroundY(start);
            float max = 24f;
            if (Physics.Raycast(start + Vector3.up, dir, out var hit, max, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) max = Mathf.Max(2f, hit.distance);
            float width = P2 ? 5f : 4.2f;
            _projectiles.Add(new Proj { pos = start, dir = dir, max = max, width = width, damage = waveProjectileDamage * (P2 ? 1.15f : 1f), hit = new HashSet<ulong>() });
            CrescentRpc(start, dir, waveProjectileSpeed, max, width, P2);
        }

        void UpdateProjectiles(float dt)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var pr = _projectiles[i];
                float step = waveProjectileSpeed * dt;
                pr.pos += pr.dir * step;
                pr.travelled += step;
                Vector3 right = Vector3.Cross(Vector3.up, pr.dir);
                foreach (var p in PlayerController.All)
                {
                    if (p == null || p.IsDead || pr.hit.Contains(p.NetworkObjectId)) continue;
                    Vector3 rel = p.transform.position - pr.pos;
                    float fwd = Vector3.Dot(rel, pr.dir);
                    if (fwd > 0.7f || fwd < -0.7f - step) continue;
                    if (Mathf.Abs(Vector3.Dot(rel, right)) > pr.width * 0.5f * Mathf.Lerp(0.85f, 1.25f, pr.travelled / pr.max)) continue;
                    if (Mathf.Abs(rel.y) > 2.4f) continue;
                    pr.hit.Add(p.NetworkObjectId);
                    DeliverHit(p, pr.damage, HitFlags.AreaOfEffect, true);
                }
                if (pr.travelled >= pr.max) _projectiles.RemoveAt(i);
                else _projectiles[i] = pr;
            }
        }

        // ---- expanding rings
        void SpawnWave(Vector3 center, float maxRadius, bool fire)
        {
            center.y = GroundY(center);
            _waves.Add(new Wave { center = center, radius = 0.5f, max = maxRadius, damage = shockwaveDamage, hit = new HashSet<ulong>() });
            WaveFxRpc(center, maxRadius, shockwaveSpeed, fire);
        }

        void UpdateWaves(float dt)
        {
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i];
                float prev = w.radius;
                w.radius += shockwaveSpeed * dt;
                foreach (var p in PlayerController.All)
                {
                    if (p == null || p.IsDead || w.hit.Contains(p.NetworkObjectId)) continue;
                    Vector3 to = p.transform.position - w.center;
                    if (Mathf.Abs(to.y) > 1.2f) continue;          // airborne / on a step: dodged
                    float d = MathUtil.Flat(to).magnitude;
                    if (d >= prev - 0.6f && d <= w.radius + 0.3f)
                    {
                        w.hit.Add(p.NetworkObjectId);
                        DeliverHit(p, w.damage, HitFlags.AreaOfEffect | HitFlags.Heavy, true);
                    }
                }
                _waves[i] = w;
                if (w.radius >= w.max) _waves.RemoveAt(i);
            }
        }

        // ---- scheduler (server)
        void Schedule(float delay, Action a) => _scheduled.Add((Time.time + delay, a));

        void UpdateScheduled()
        {
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                if (i >= _scheduled.Count || Time.time < _scheduled[i].time) continue;
                var a = _scheduled[i].action;
                _scheduled.RemoveAt(i);
                if (_state != State.Dead && _state != State.Dormant) a?.Invoke();
            }
        }

        // ================================================================== phase 2 transformation
        void BeginPhaseTransition()
        {
            CancelAttack();
            EndLeap();
            _seq.Clear(); _step = null; _held = false;
            _state = State.PhaseTransition;
            _ptTime = 0f; _ptStage = 0;
            PlayAnimRpc(StateIndex(CharacterAnimator.Hurt), 0.1f, 0.35f);
            EmbersRpc(transform.position + Vector3.up * 2f, 60);
            Debug.Log("[DF] The Ashen King enters phase 2");
        }

        void UpdatePhaseTransition(float dt)
        {
            _ptTime += dt;
            if (_ptStage == 0 && _ptTime >= 1.4f)
            {
                _ptStage = 1;
                _netPhase.Value = 2;
                PlayAnimRpc(StateIndex(CharacterAnimator.Taunt), 0.25f, 0.75f);
                RoarRpc(true);
                IgniteRpc();
            }
            else if (_ptStage == 1 && _ptTime >= 2.8f)
            {
                _ptStage = 2;
                Vector3 c = transform.position;
                BurstRpc(c + Vector3.up * 1.5f, 6.5f);
                AreaDamage(c, 5.5f, burstDamage + 10f, HitFlags.AreaOfEffect | HitFlags.Heavy, true);
                SpawnWave(c, 17f, true);
            }
            else if (_ptStage == 2 && _ptTime >= 3.9f)
            {
                EnterRecover(0.2f);
            }
        }

        // ================================================================== stagger / death
        protected override void Stagger(float duration)
        {
            if (_state == State.PhaseTransition || _state == State.Intro || _state == State.Dormant) return;
            EndLeap();
            _seq.Clear(); _step = null; _held = false;
            CancelAttack();
            _state = State.Staggered;
            _stateTimer = duration;
            PlayAnimRpc(StateIndex(CharacterAnimator.Hurt), 0.05f, 0.4f);
        }

        protected override int SoulReward => 1200;
        protected override bool IsBossReward => true;

        protected override void Die()
        {
            _state = State.Dead;
            ClearCombat();
            base.Die();
            DeathFxRpc();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Development only: sets the boss health fraction ("0.5" → straight to phase 2).</summary>
        public void DevHealth(string frac)
        {
            if (!IsServer) return;
            netHealth.Value = netMaxHealth.Value * float.Parse(frac, System.Globalization.CultureInfo.InvariantCulture);
        }
#endif

        // ================================================================== charge (readable gap closer)
        /// <summary>
        /// Raises the sword and roars (red glint) → runs at you kicking up dust → strikes when close.
        /// Replaces the old instant lunge that looked like a teleport.
        /// </summary>
        void BeginCharge()
        {
            _state = State.ChargePrep;
            _stateTimer = Desperate ? 0.4f : P2 ? 0.5f : 0.7f;
            _chargeDustTimer = 0f;
            PlayAnimRpc(StateIndex(CharacterAnimator.Taunt), 0.15f, 1.7f);
            TelegraphNow(false);
            ChargeCueRpc(transform.position);
        }

        void ChargeStrike()
        {
            var strike = S(lungeCleave, 4.8f, 50f, HitFlags.Heavy, tracking: 1.2f, impact: Impact.Dust);
            strike.charged = true; strike.lungeOverride = 2.2f;
            var l = new List<Step> { strike };
            if (Random.value < (P2 ? 0.6f : 0.35f)) l.Add(S(sweep, 5.2f, 85f, tracking: 1f));
            Run(l);
        }

        // ================================================================== evasive hop
        void BeginEvade()
        {
            _state = State.Evade;
            _stateTimer = 0.65f;
            pressure = 0f;
            _cooldowns["evade"] = 8f;
            Vector3 back = -transform.forward * 9f + transform.right * Random.Range(-3f, 3f);
            Knock(back);
            PlayAnimRpc(StateIndex(CharacterAnimator.Parry), 0.08f, 1.2f);
            EvadeFxRpc(transform.position);
        }

        void AfterEvade()
        {
            float d = target ? DistanceToTarget : 0f;
            // from range he answers with a ranged move, a leap, a charge... or just watches you
            if (target && d > 5.5f && Random.value < 0.7f)
            {
                float r = Random.value;
                if (r < 0.45f && Ready("wave")) { _cooldowns["wave"] = 6f; Run(P2 ? TripleWave() : AshWave()); return; }
                if (r < 0.75f && Ready("leap") && d > 7f) { _cooldowns["leap"] = 8f; Run(P2 ? DoubleLeap() : Leap()); return; }
                if (Ready("charge")) { _cooldowns["charge"] = 6f; BeginCharge(); return; }
            }
            EnterRecover(Random.Range(0.6f, 1.2f), Random.value < 0.5f ? Pace.Watch : Pace.Circle);
        }

        // ================================================================== flinch (knockback)
        /// <summary>
        /// Enough damage in quick succession (or a charged heavy) makes the king recoil and get pushed
        /// back, interrupting a swing that hasn't started yet. Then he can't recoil again for a while,
        /// so he can't be stun-locked either.
        /// </summary>
        protected override void OnHitReceived(float damage, float knockback, Vector3 away)
        {
            if (_state == State.Dormant || _state == State.Intro || _state == State.PhaseTransition || _state == State.Dead ||
                _state == State.Staggered || _state == State.Flinch || _airborne) return;
            _flinchMeter += damage / Mathf.Max(1f, netMaxHealth.Value * flinchDamageShare) + (knockback >= 5f ? 0.45f : 0f);
            bool heavyHit = knockback >= 6f;
            bool swinging = IsAttacking && !_held && !InPreHold && attackClock >= currentAttack.hitStart - 0.08f;
            if (_flinchCooldown > 0f || (_flinchMeter < 1f && !heavyHit) || (swinging && !heavyHit)) return;

            _flinchMeter = 0f;
            _flinchCooldown = P2 ? flinchCooldown * 1.3f : flinchCooldown;
            EndLeap();
            _seq.Clear(); _step = null; _held = false;
            CancelAttack();
            _state = State.Flinch;
            _stateTimer = heavyHit ? 1.1f : 0.75f;
            Knock(away * (heavyHit ? 7f : 5f));
            PlayAnimRpc(StateIndex(CharacterAnimator.Hurt), 0.05f, heavyHit ? 0.7f : 0.95f);
            FlinchFxRpc(transform.position, heavyHit);
            Debug.Log($"[DF] Boss recoils{(heavyHit ? " (heavy hit)" : "")}");
        }

        [Rpc(SendTo.Everyone)]
        void ChargeCueRpc(Vector3 pos)
        {
            Sfx.Play(SfxId.EnemyGrunt, pos + Vector3.up * 3f, 1.4f, 0.5f);
            Fx.Spawn(FxId.ShockDust, pos);
            BossFx.Embers(pos + Vector3.up * 2f, 1.5f, 30, 3f);
            ShakeNear(pos, 0.1f, 18f, 0.5f);
        }

        [Rpc(SendTo.Everyone)]
        void EvadeFxRpc(Vector3 pos)
        {
            Fx.Spawn(FxId.Dust, pos);
            Sfx.Play(SfxId.Roll, pos, 1.2f, 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        void FlinchFxRpc(Vector3 pos, bool heavy)
        {
            Fx.Spawn(heavy ? FxId.ShockDust : FxId.Dust, pos);
            Sfx.Play(SfxId.EnemyGrunt, pos + Vector3.up * 3f, 1.2f, 0.55f);
            Sfx.Play(SfxId.HitArmor, pos + Vector3.up * 2f, 1f, 0.7f);
            ShakeNear(pos, heavy ? 0.15f : 0.08f, 15f);
        }

        // ================================================================== client FX RPCs
        static void ShakeNear(Vector3 pos, float amp, float range, float duration = 0.4f)
        {
            var me = PlayerController.Local;
            if (me == null) return;
            float k = 1f - Mathf.Clamp01(Vector3.Distance(me.transform.position, pos) / range);
            if (k > 0f) SoulsCamera.Shake(amp * k, duration);
        }

        [Rpc(SendTo.Everyone)]
        void WaveFxRpc(Vector3 center, float maxRadius, float speed, bool fire)
        {
            Sfx.Play(fire ? SfxId.FireBurst : SfxId.Quake, center, fire ? 0.9f : 1.2f);
            Fx.Spawn(FxId.ShockDust, center);
            ShakeNear(center, 0.25f, 20f);
            if (shockwavePrefab)
            {
                var go = Instantiate(shockwavePrefab, center + Vector3.up * 0.08f, Quaternion.identity);
                var sw = go.GetComponent<ShockwaveVisual>();
                if (sw) sw.Init(maxRadius, speed, fire ? new Color(1f, 0.45f, 0.12f) : new Color(0.62f, 0.55f, 0.45f));
            }
            BossFx.RingWave(center, maxRadius, speed, fire);
            if (fire) BossFx.Flash(center + Vector3.up, BossFx.FireEdge, 40f, maxRadius, 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        void DustRpc(Vector3 pos, bool big)
        {
            Fx.Spawn(big ? FxId.ShockDust : FxId.Dust, pos);
            if (!big) return;
            Sfx.Play(SfxId.Slam, pos);
            ShakeNear(pos, 0.22f, 14f);
            BossFx.Embers(pos, 1.5f, 25, 4f);
        }

        [Rpc(SendTo.Everyone)]
        void BurstRpc(Vector3 pos, float radius)
        {
            BossFx.FireBurst(pos, radius);
            Sfx.Play(SfxId.FireBurst, pos, 1.1f);
            ShakeNear(pos, 0.3f, 18f, 0.5f);
        }

        [Rpc(SendTo.Everyone)]
        void WarningRpc(Vector3 pos, float radius, float duration, bool fire)
        {
            BossFx.GroundWarning(pos, radius, duration, fire ? BossFx.FireEdge : BossFx.DangerRed);
            Sfx.Play(SfxId.Warning, pos, 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        void PillarRpc(Vector3 pos)
        {
            BossFx.FirePillar(pos, pillarRadius, 6f);
            Sfx.Play(SfxId.Eruption, pos, 1.1f);
            ShakeNear(pos, 0.2f, 10f);
        }

        [Rpc(SendTo.Everyone)]
        void CrescentRpc(Vector3 pos, Vector3 dir, float speed, float max, float width, bool fire)
        {
            AshCrescent.Spawn(pos, dir, speed, max, width, fire);
            Sfx.Play(fire ? SfxId.FireWhoosh : SfxId.Leap, pos + Vector3.up, 1.1f, fire ? 1f : 1.2f);
        }

        [Rpc(SendTo.Everyone)]
        void LeapFxRpc(Vector3 pos)
        {
            Fx.Spawn(FxId.ShockDust, pos);
            Sfx.Play(SfxId.Leap, pos + Vector3.up, 1.2f);
            ShakeNear(pos, 0.1f, 12f);
        }

        [Rpc(SendTo.Everyone)]
        void FlameRingRpc(float duration)
        {
            BossFx.FlameRing(transform, tornadoRadius * 0.9f, duration);
            Sfx.Play(SfxId.FireWhoosh, transform.position + Vector3.up, 1.2f, 0.8f);
        }

        [Rpc(SendTo.Everyone)]
        void EmbersRpc(Vector3 pos, int count) => BossFx.Embers(pos, 1.5f, count, 3f);

        [Rpc(SendTo.Everyone)]
        void RoarRpc(bool fire)
        {
            Sfx.Play(SfxId.Roar, transform.position + Vector3.up * 3f, 1.3f);
            BossFx.Embers(transform.position + Vector3.up * 2f, 2f, fire ? 120 : 40, fire ? 7f : 3f);
            ShakeNear(transform.position, fire ? 0.2f : 0.12f, 25f, 0.9f);
        }

        [Rpc(SendTo.Everyone)]
        void IgniteRpc()
        {
            if (_visuals) _visuals.Ignite(false);
            Ignited?.Invoke();
        }

        [Rpc(SendTo.Everyone)]
        void ExtinguishRpc() { if (_visuals) _visuals.Extinguish(); }

        [Rpc(SendTo.Everyone)]
        void DeathFxRpc()
        {
            Vector3 c = transform.position + Vector3.up * 2f;
            BossFx.Embers(c, 2.5f, 220, 5f);
            BossFx.Flash(c, new Color(1f, 0.8f, 0.6f), 60f, 25f, 2.5f);
            Sfx.Play(SfxId.FireBurst, c, 1f, 0.6f);
            ShakeNear(c, 0.3f, 30f, 1.2f);
            if (_visuals) StartCoroutine(ExtinguishLater());
        }

        System.Collections.IEnumerator ExtinguishLater()
        {
            yield return new WaitForSeconds(2.5f);
            if (_visuals) _visuals.Extinguish();
        }
    }
}
