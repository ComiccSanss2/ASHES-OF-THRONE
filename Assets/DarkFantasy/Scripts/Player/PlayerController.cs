using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Souls-like player controller (NGO, owner-authoritative movement).
    /// Two classes share it: the Knight (greatsword moveset) and the Archer (war-bow: quick shot,
    /// chargeable power shot, over-the-shoulder aim on the right mouse button, see "Archer" below).
    ///
    /// Combat timing is driven by the Animator's real playback time: the hit window, the slash
    /// sound, the weapon trail and the lunge all read the same clip time, so what you see is
    /// exactly when the enemy gets hit.
    ///  - Light: 3-hit chain (diagonal slash → horizontal sweep → spinning slash) that loops while
    ///    you keep pressing. Presses during an attack are buffered and the next hit starts as soon
    ///    as the current blade leaves its arc, entering the next clip at its windup point with a
    ///    short cross-fade, so the swings flow into each other.
    ///  - Heavy: hold to charge (the animation pauses at the top of the swing), release to strike.
    ///  - Block (hold) and Parry both use the greatsword; parry window 0.22 s staggers the enemy.
    ///  - Roll with i-frames; moving or rolling cancels the recovery of an attack.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour, IHurtbox, IInteractable
    {
        public static PlayerController Local { get; private set; }
        public static readonly List<PlayerController> All = new List<PlayerController>();

        enum State { Free, Attacking, Rolling, Parrying, Hurt, Drinking, Dead, Traversing, Downed }

        [Header("Class")]
        public PlayerClass playerClass = PlayerClass.Knight;

        [Header("References")]
        public CharacterAnimator anim;
        public WeaponHitbox weapon;
        public PlayerVitals vitals;
        public Transform lockPoint;

        [Header("Movement")]
        public float walkSpeed = 1.9f;
        public float runSpeed = 4.1f;
        public float sprintSpeed = 6.3f;
        public float lockedSpeed = 3.1f;
        public float blockSpeed = 1.7f;
        public float acceleration = 16f;
        public float deceleration = 20f;
        [Tooltip("Armour weight: >1 = more inertia.")]
        public float armorWeight = 1.35f;
        public float turnSpeed = 600f;
        public float gravity = 24f;
        public float sprintStaminaPerSecond = 12f;

        [Header("Roll")]
        public float rollDuration = 0.62f;
        public float rollDistance = 4.4f;
        public float iframeStart = 0.04f;
        public float iframeEnd = 0.42f;
        public float rollStamina = 18f;

        [Header("Attacks (timings filled by the builder from clip analysis)")]
        public AttackData[] lightCombo =
        {
            new AttackData { name = "Rising Slash", state = CharacterAnimator.Attack1, animSpeed = 1.3f, damage = 22, poiseDamage = 18, staminaCost = 16, lunge = 0.9f, knockback = 2.6f },
            new AttackData { name = "Sweep",        state = CharacterAnimator.Attack2, animSpeed = 1.2f, damage = 24, poiseDamage = 20, staminaCost = 18, lunge = 0.7f, knockback = 3.2f },
            new AttackData { name = "Whirlwind",    state = CharacterAnimator.Spin,    animSpeed = 1.0f, damage = 30, poiseDamage = 28, staminaCost = 24, lunge = 1.2f, knockback = 4.2f },
        };
        public AttackData heavyAttack = new AttackData { name = "Cleave", state = CharacterAnimator.Heavy, animSpeed = 1.0f, damage = 46, poiseDamage = 55, staminaCost = 32, lunge = 1.0f, hyperArmor = true, knockback = 6.5f };
        [Tooltip("Swinging with less stamina than the attack costs: slower and weaker (no free spam).")]
        public float exhaustedSpeed = 0.75f;
        public float exhaustedDamage = 0.6f;
        [Tooltip("Clip time where the heavy swing is raised and can be held (filled by the builder).")]
        public float heavyHoldTime = 0.6f;
        public float heavyMaxCharge = 1.2f;
        public float heavyChargeBonus = 0.8f;
        public float inputBuffer = 0.4f;
        [Tooltip("Cross-fade when one combo hit flows into the next.")]
        public float chainFade = 0.12f;
        [Tooltip("A press this soon after an attack ends still continues the combo.")]
        public float comboGrace = 0.45f;

        [Header("Defense")]
        public float parryWindow = 0.22f;
        public float parryDuration = 0.55f;
        public float parryStamina = 10f;
        public float blockStaminaPerDamage = 1.1f;
        [Range(0, 1)] public float blockChip = 0.1f;
        public float hurtDuration = 0.45f;
        public float knockdownDuration = 1.4f;
        public float guardBreakDuration = 1.2f;
        public float respawnDelay = 4.5f;

        [Header("Weapons (inventory)")]
        [Tooltip("Renderer of the starting weapon (greatsword mesh / the archer's skinned bow).")]
        public Renderer baseWeaponRenderer;
        [Tooltip("Relic weapon visual (Cinder Oath / Thornwraith), hidden until equipped.")]
        public GameObject relicWeapon;
        public float relicDamageMul = 1.35f;
        public float relicPoiseMul = 1.25f;
        public float relicArrowSpeedMul = 1.15f;
        public Color relicTrailColor = new Color(1f, 0.5f, 0.18f, 0.95f);
        [Tooltip("Dragon weapon visual (Wyrmfang / Wyrmwing / Vaelthrax's Lament), won from the dragon.")]
        public GameObject dragonWeapon;
        [Tooltip("Extra effects shown only while the dragon weapon is equipped (embers, glow).")]
        public GameObject dragonFx;
        public float dragonDamageMul = 1.5f;
        public float dragonPoiseMul = 1.35f;
        public float dragonArrowSpeedMul = 1.3f;
        public Color dragonTrailColor = new Color(1f, 0.22f, 0.08f, 1f);
        [Tooltip("Radiant weapon visual (Dawnbreaker / Starwoven / Seraph's Citole), found in the prison vault.")]
        public GameObject radiantWeapon;
        [Tooltip("Extra effects shown only while the radiant weapon is equipped (sparkles, light).")]
        public GameObject radiantFx;
        public float radiantDamageMul = 1.65f;
        public float radiantPoiseMul = 1.45f;
        public float radiantArrowSpeedMul = 1.4f;
        public Color radiantTrailColor = new Color(1f, 0.95f, 0.75f, 1f);

        [Header("Ultimate (key 1, all but the bard)")]
        public PlayerUltimate ult;
        bool _ultActive;
        /// <summary>The ultimate is playing: hyper armour (no flinch, no knockdown), 40% less damage taken.</summary>
        public bool UltArmor => _ultActive && _state == State.Attacking;

        [Header("Bard")]
        public BardCaster bard;
        public SmokerCaster smoker;
        public PlayerBuffs buffs;

        [Header("Archer")]
        public ArcherBow bow;
        public float arrowSpeed = 46f;
        public float chargedArrowSpeed = 70f;
        [Tooltip("Walk speed while aiming (right mouse).")]
        public float aimMoveSpeed = 1.7f;
        [Tooltip("Clip time where the bow is up with an arrow on the string: aimed shots start here (quicker).")]
        public float aimedShotStart = 0.3f;
        [Tooltip("Hip fire (not aiming): the shot leaves this many degrees above the camera's line of sight (the camera looks down a bit).")]
        public float hipPitchBias = 9f;
        [Tooltip("Degrees around the crosshair in which a shot bends toward an enemy.")]
        public float aimAssistAngle = 4.5f;
        public float maxAimDistance = 90f;

        readonly NetworkVariable<float> _netSpeed = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<sbyte> _netLocoMul = new NetworkVariable<sbyte>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> _netBlocking = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> _netAct = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> _netWeapon = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> _netCrouch = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        [Header("Crouch (Ctrl / C)")]
        public float crouchSpeed = 1.9f;
        bool _crouched;
        float _ccHeight, _ccCenterY;
        /// <summary>Crouched: slower, lower, harder for enemies to notice.</summary>
        public bool Crouched => IsOwner ? _crouched : _netCrouch.Value;

        /// <summary>Equipped weapon (Inventory.BaseWeapon / Inventory.Relic), replicated.</summary>
        public byte EquippedWeapon => _netWeapon.Value;
        bool HasRelic => _netWeapon.Value == Inventory.Relic;
        /// <summary>0 = starting weapon, 1 = relic, 2 = dragon weapon.</summary>
        public byte WeaponTier => _netWeapon.Value;
        /// <summary>
        /// The tier whose STATS apply: an unlocked weapon is always yours to wear, but its power only
        /// awakens once its source has been conquered in THIS session (the King for the relics, the
        /// wyrm for the dragon weapons, the vault's treasure for the radiant ones).
        /// </summary>
        public byte PowerTier => CastleProgress.TierEarned(WeaponTier) ? WeaponTier : Inventory.BaseWeapon;
        public bool WeaponDormant => WeaponTier != PowerTier;
        public float WeaponDamageMul => PowerTier switch { Inventory.Radiant => radiantDamageMul, Inventory.Dragon => dragonDamageMul, Inventory.Relic => relicDamageMul, _ => 1f };
        float WeaponPoiseMul => PowerTier switch { Inventory.Radiant => radiantPoiseMul, Inventory.Dragon => dragonPoiseMul, Inventory.Relic => relicPoiseMul, _ => 1f };
        float ArrowSpeedMul => PowerTier switch { Inventory.Radiant => radiantArrowSpeedMul, Inventory.Dragon => dragonArrowSpeedMul, Inventory.Relic => relicArrowSpeedMul, _ => 1f };
        Color _baseTrailColor;
        bool _trailColorRead;
        float _baseTrailLife = 0.22f;

        /// <summary>What the player is doing, replicated so the boss AI can read and punish habits.</summary>
        public enum Act : byte { Free, Attacking, Rolling, Blocking, Drinking, Parrying, Hurt, Dead }
        public Act CurrentAct => (Act)_netAct.Value;

        CharacterController _cc;
        NetworkTransform _nt;
        State _state;
        float _stateTime;
        Vector3 _velocity;
        float _vy;
        int _comboIndex;
        AttackData _attack;
        bool _isHeavy;
        float _attackStartReal;
        bool _hitboxOn, _hitLanded;
        bool _comboQueued;
        float _heavyCharge;
        bool _heavyReleased;
        bool _remoteHeavyHold;
        float _queuedLightTime = -10f, _queuedRollTime = -10f;
        bool _holdBlocked;   // a click that opened/closed a menu must not turn into a held attack
        Vector3 _rollDir;
        bool _blocking;
        float _hurtTime;
        bool _drankApplied;
        Vector3 _traverseFrom, _traverseTo;
        bool _sprinting;
        bool _needsSpawnTeleport;
        float _lastAttackEnd = -10f;
        float _hitInvulnUntil = -10f;   // short grace after being hit: no stun-lock by several enemies
        int _lastComboIndex = -1;
        bool _aiming, _fired;

        public bool IsPlayer => true;
        bool _devGod;
        bool DevGodOn => _devGod;
        public bool IsBlocking => _netBlocking.Value;
        /// <summary>Out of the fight: dead, or downed waiting for a revive (enemies ignore them, they can't act).</summary>
        public bool IsDead => vitals != null && (vitals.IsDead || vitals.IsDowned);
        public bool IsDowned => vitals != null && vitals.IsDowned;
        public bool IsTrulyDead => vitals != null && vitals.IsDead;
        public bool IsInvulnerable => (_state == State.Rolling && _stateTime >= iframeStart && _stateTime <= iframeEnd) || _state == State.Traversing || Time.time < _hitInvulnUntil || DevGodOn;
        public string DisplayName => vitals ? vitals.DisplayName : name;
        public bool CanInteract => IsOwner && _state == State.Free && !IsDead;
        /// <summary>Owner: drinking a flask right now (the Reaper punishes it from afar).</summary>
        public bool IsDrinkingNow => _state == State.Drinking;
        public bool IsArcher => playerClass == PlayerClass.Archer;
        public bool IsBard => playerClass == PlayerClass.Bard;
        public bool IsReaper => playerClass == PlayerClass.Reaper;
        public bool IsSmoker => playerClass == PlayerClass.Smoker;
        /// <summary>Bard or smoker: mana, the Arcana skill.</summary>
        public bool IsCaster => IsBard || IsSmoker;
        /// <summary>Archer or bard: no guard, the right button aims over the shoulder.</summary>
        public bool IsRanged => IsArcher || IsBard || IsSmoker;
        /// <summary>Damage multiplier from buffs (War Anthem).</summary>
        /// <summary>Anthem, and the skill tree: Strength (weapons) or Arcana (the bard's magic).</summary>
        public float DamageDealtMul => (buffs ? buffs.DamageDealtMul : 1f) * (IsCaster ? PlayerSkills.ArcanaMul : PlayerSkills.StrengthMul);
        [Header("Armour (boss sets)")]
        public PlayerArmor armor;
        System.Action _spellRelease;
        int _spellIndex = -1;
        public bool IsCastingSpell => _spellRelease != null && _state == State.Attacking;
        /// <summary>Archer holding the right mouse button (over-the-shoulder aim).</summary>
        public bool IsAiming => _aiming;
        /// <summary>Local archer: show the crosshair (not while locked on: the lock reticle is the aim).</summary>
        public bool ShowCrosshair => IsRanged && IsOwner && !IsDead && !(SoulsCamera.Instance && SoulsCamera.Instance.IsLocked);

        public static event System.Action<PlayerController> LocalDied;
        /// <summary>Player-vs-player damage is halved: fights between players last a few exchanges, not one swing.</summary>
        /// <summary>Duels: blows land almost in full (players have less health than monsters' blows assume).</summary>
        public const float PvpDamageMul = 0.95f;
        public bool PvpOn => vitals != null && vitals.PvpOn;
        /// <summary>Both players opted into PvP, both on their feet.</summary>
        public static bool CanPvp(PlayerController a, PlayerController b) => a != null && b != null && a != b && a.PvpOn && b.PvpOn && !a.IsDead && !b.IsDead;
        /// <summary>Every client: a player killed another one in PvP (killer, victim).</summary>
        public static event System.Action<PlayerController, PlayerController> PvpKill;
        public static event System.Action<PlayerController> LocalSpawned;

        // ================================================================== lifecycle
        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _nt = GetComponent<NetworkTransform>();
            if (vitals == null) vitals = GetComponent<PlayerVitals>();
            if (anim == null) anim = GetComponentInChildren<CharacterAnimator>();
            if (buffs == null) buffs = GetComponent<PlayerBuffs>();
            if (bard == null) bard = GetComponent<BardCaster>();
            if (smoker == null) smoker = GetComponent<SmokerCaster>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Layers.SetLayerRecursive(gameObject, Layers.Character);
            if (weapon != null) { weapon.ownerRoot = transform; weapon.OnHit += OnWeaponHit; }
            _netWeapon.OnValueChanged += OnWeaponChanged;
            _netCrouch.OnValueChanged += OnCrouchChanged;
            ApplyWeaponVisual(_netWeapon.Value);
            if (!IsOwner && !Interactables.All.Contains(this)) Interactables.All.Add(this);
            // PvP: opponents can be locked on (LockOnTarget.IsValid filters by the duel)
            if (!IsOwner && !TryGetComponent<LockOnTarget>(out _)) { var lot = gameObject.AddComponent<LockOnTarget>(); lot.lockPoint = lockPoint; }
            if (armor == null) armor = GetComponent<PlayerArmor>();
            if (IsOwner) StartCoroutine(SessionStartChecks());
            if (IsOwner)
            {
                BossArena.BossDefeated += ClearBan; BossArena.FightReset += ClearBan;
                DragonArena.Defeated += ClearBan; DragonArena.FightReset += ClearBan;
                MirrorArena.Defeated += ClearBan; MirrorArena.FightReset += ClearBan;
                ReaperArena.Defeated += ClearBan; ReaperArena.FightReset += ClearBan;
                _netWeapon.Value = Inventory.Equipped(playerClass);
                Inventory.Changed += OnInventoryChanged;
                Local = this;
                _needsSpawnTeleport = true;
                if (SoulsCamera.Instance) SoulsCamera.Instance.SetFollowTarget(transform);
                GameInput.SetCursorLocked(true);
                LocalSpawned?.Invoke(this);
            }
            Sfx.Play(SfxId.Respawn, transform.position + Vector3.up, 0.6f);
            name = "Player_" + OwnerClientId + (IsOwner ? " (local)" : "");
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (weapon != null) weapon.OnHit -= OnWeaponHit;
            _netWeapon.OnValueChanged -= OnWeaponChanged;
            if (IsOwner) Inventory.Changed -= OnInventoryChanged;
            Interactables.All.Remove(this);
            if (IsOwner) { BossArena.BossDefeated -= ClearBan; BossArena.FightReset -= ClearBan; DragonArena.Defeated -= ClearBan; DragonArena.FightReset -= ClearBan; MirrorArena.Defeated -= ClearBan; MirrorArena.FightReset -= ClearBan; ReaperArena.Defeated -= ClearBan; ReaperArena.FightReset -= ClearBan; }
            if (_reviveFx) Destroy(_reviveFx);
            if (Local == this)
            {
                Local = null;
                if (SoulsCamera.Instance) SoulsCamera.Instance.SetFollowTarget(null);
            }
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            Interactables.All.Remove(this);
            if (_reviveFx) Destroy(_reviveFx);
            base.OnDestroy();
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner) OwnerUpdate(Time.deltaTime);
            else
            {
                RemoteUpdate();
                // an ally lying here: E to start reviving them
                if (GameInput.InteractPressed && Local != null && LocalCanInteract(out _))
                {
                    ReviveRequestRpc(Local.OwnerClientId);
                    Sfx.Play(SfxId.SpellCast, transform.position + Vector3.up, 0.6f, 1.2f);
                }
            }
            UpdateReviveFx();
        }

        void RemoteUpdate()
        {
            if (anim == null) return;
            anim.SetLocomotion(_netSpeed.Value, _netLocoMul.Value);
            anim.SetBlocking(_netBlocking.Value);
            // mirror the heavy charge hold so the raised blade looks the same for everyone
            if (_remoteHeavyHold && anim.GetStateTime(heavyAttack.state) >= heavyHoldTime) anim.SetPlaybackSpeed(0.03f);

            // safety net: the owner's replicated state says it is no longer attacking but this
            // copy is still in an attack clip (message lost or late, looping Whirlwind…) → recover
            var act = CurrentAct;
            bool ownerAttacking = act == Act.Attacking;
            if (!ownerAttacking && act != Act.Dead && anim.IsInAttackState) _remoteStaleAttack += Time.deltaTime;
            else _remoteStaleAttack = 0f;
            if (_remoteStaleAttack > 0.35f)
            {
                _remoteStaleAttack = 0f;
                _remoteHeavyHold = false;
                anim.ReturnToLocomotion(0.2f);
            }
        }
        float _remoteStaleAttack;

        // ================================================================== owner
        void OwnerUpdate(float dt)
        {
            if (_needsSpawnTeleport)
            {
                _needsSpawnTeleport = false;
                var lv = LevelRefs.Instance;
                if (lv != null) { Vector3 sp = lv.GetPlayerSpawn(OwnerClientId, out var srot); TeleportTo(sp, srot); }
            }
            _stateTime += dt;
            if (ult) ult.OwnerTick(dt);
            // holding the button keeps attacking (re-queues every frame while free)
            if (GameInput.LightAttackPressed || (GameInput.LightAttackHeld && _state == State.Free && !_holdBlocked)) _queuedLightTime = Time.time;
            if (GameInput.GameplayBlocked) _holdBlocked = true;
            else if (!GameInput.LightAttackHeld) _holdBlocked = false;
            if (GameInput.RollPressed) _queuedRollTime = Time.time;
            bool lightQueued = Time.time - _queuedLightTime < inputBuffer;
            bool rollQueued = Time.time - _queuedRollTime < inputBuffer;

            if (vitals.IsDead && _state != State.Dead) EnterDeath();
            else if (vitals.IsDowned && _state != State.Downed && _state != State.Dead) EnterDowned();

            Vector3 desiredMove = Vector3.zero;
            float speedLimit = 0f;
            _blocking = false;
            _aiming = false;
            _sprinting = false;
            vitals.RegenMultiplier = 1f;

            switch (_state)
            {
                case State.Free:
                    ComputeMoveInput(out desiredMove, out speedLimit);
                    if (GameInput.CrouchPressed) SetCrouched(!_crouched);
                    if (_crouched)
                    {
                        if (_sprinting) SetCrouched(false);                // sprinting stands you up
                        else speedLimit = Mathf.Min(speedLimit, crouchSpeed);
                    }
                    if (IsBard) bard?.OwnerPollSpells();
                    else
                    {
                        if (ult && GameInput.SpellPressed(0)) ult.TryActivate();
                        // keys 2-4: the session's draughts (the bard keeps those keys for his songs)
                        for (int k = 0; k < Potions.KindCount; k++)
                            if (GameInput.SpellPressed(k + 1)) DrinkPotion((Potions.Kind)k);
                    }
                    if (_state != State.Free) break;   // a spell / the ultimate started
                    if (IsSmoker && smoker)
                    {
                        // the right button draws on the pipe; letting go exhales the smoke spirits
                        bool inhaling = smoker.OwnerInhale(GameInput.BlockHeld, dt);
                        if (_state != State.Free) break;   // exhaled
                        if (inhaling) { speedLimit = Mathf.Min(speedLimit, aimMoveSpeed); _sprinting = false; break; }
                    }
                    else if (IsRanged)
                    {
                        // the archer has no guard: the right button raises the bow and aims over the shoulder
                        _aiming = GameInput.BlockHeld;
                        if (_aiming) { speedLimit = Mathf.Min(speedLimit, aimMoveSpeed); _sprinting = false; SoulsCamera.RequestAim(); }
                    }
                    else
                    {
                        _blocking = GameInput.BlockHeld;
                        if (_blocking) { speedLimit = Mathf.Min(speedLimit, blockSpeed); vitals.RegenMultiplier = 0.4f; }
                    }
                    if (rollQueued && vitals.HasStamina()) { _queuedRollTime = -10; StartRoll(desiredMove); }
                    else if (lightQueued && vitals.HasStamina())
                    {
                        _queuedLightTime = -10;
                        // pressed right after the previous swing: continue the chain instead of restarting
                        bool cont = Time.time - _lastAttackEnd < comboGrace && _lastComboIndex >= 0;
                        StartAttack(cont ? (_lastComboIndex + 1) % lightCombo.Length : 0, cont);
                    }
                    else if (GameInput.HeavyPressed && vitals.HasStamina() && (!IsSmoker || smoker == null || smoker.CanGust())) StartAttack(-1, false);
                    else if (GameInput.ParryPressed && vitals.HasStamina() && !IsRanged) StartParry();
                    else if (GameInput.FlaskPressed && vitals.Flasks > 0) StartDrink();
                    break;

                case State.Attacking:
                    if (IsRanged && !IsSmoker && GameInput.BlockHeld) { _aiming = true; SoulsCamera.RequestAim(); }
                    UpdateAttack(dt, rollQueued);
                    break;

                case State.Rolling:
                {
                    float t = Mathf.Clamp01(_stateTime / rollDuration);
                    float speed = rollDistance / rollDuration * 1.6f * (1f - t * t);
                    _velocity = _rollDir * speed;
                    if (_stateTime >= rollDuration)
                    {
                        _velocity = _rollDir * runSpeed * 0.5f;
                        EnterFree();
                        if (lightQueued) { _queuedLightTime = -10; StartAttack(0, false); }
                    }
                    break;
                }
                case State.Parrying:
                    _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, deceleration * dt);
                    if (_stateTime >= parryDuration) EnterFree();
                    break;
                case State.Hurt:
                    if (_grabbed) { UpdateSoulGrab(dt); break; }
                    _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, 11f * dt);   // slide back from the impact
                    if (_stateTime >= _hurtTime) EnterFree();
                    break;
                case State.Drinking:
                    ComputeMoveInput(out desiredMove, out speedLimit);
                    speedLimit = walkSpeed * 0.6f;
                    if (!_drankApplied && _stateTime >= 0.65f) { _drankApplied = true; vitals.Heal(vitals.flaskHeal); Fx.Spawn(FxId.Embers, transform.position + Vector3.up); }
                    if (_stateTime >= 1.15f) EnterFree();
                    break;
                case State.Dead:
                    _velocity = Vector3.zero;
                    if (_giveUpRespawn) { if (_stateTime >= 1.2f) Respawn(); break; }
                    if (_spectateArena != 0)
                    {
                        if (!ArenaFighting(_spectateArena)) { _spectateArena = 0; _stateTime = Mathf.Max(_stateTime, respawnDelay - 1.5f); }
                        else { if (_stateTime > 2.5f) UpdateSpectate(); break; }
                    }
                    if (_stateTime >= respawnDelay) Respawn();
                    break;
                case State.Downed:
                {
                    _velocity = Vector3.zero;
                    if (vitals.TickDowned(dt, HelperInCircle())) { Revived(); break; }
                    _giveUpHold = GameInput.GiveUpHeld ? _giveUpHold + dt : 0f;
                    if (_giveUpHold >= 1f) { _giveUpHold = 0f; vitals.Die(); EnterDeath(true); break; }
                    if (vitals.BleedLeft <= 0f) { vitals.Die(); EnterDeath(false); }
                    break;
                }
                case State.Traversing:
                {
                    float t = Mathf.Clamp01(_stateTime / 1.2f);
                    _cc.enabled = false; transform.position = Vector3.Lerp(_traverseFrom, _traverseTo, t); _cc.enabled = true;
                    _velocity = Vector3.zero;
                    anim.SetLocomotion(1.5f);
                    if (t >= 1f) EnterFree();
                    break;
                }
            }

            if (_state == State.Free || _state == State.Drinking)
            {
                Vector3 target = desiredMove * speedLimit;
                float rate = (target.sqrMagnitude > _velocity.sqrMagnitude ? acceleration : deceleration) / Mathf.Max(0.1f, armorWeight);
                _velocity = Vector3.MoveTowards(_velocity, target, rate * dt);
                if (_sprinting) vitals.UseStamina(sprintStaminaPerSecond * dt);
            }

            UpdateFacing(dt, desiredMove);

            if (_state != State.Traversing && !(_grabbed && _state == State.Hurt))
            {
                if (_cc.isGrounded && _vy < 0f) _vy = -2f;
                _vy -= gravity * dt;
                _cc.Move((_velocity + ExternalPull() + Vector3.up * _vy) * dt);
                // fell out of the world (the Realm of the Red Moon lies far below the castle: its own floor)
                if (transform.position.y < (transform.position.z > 760f ? -190f : -60f)) Respawn();
            }

            float planarSpeed = MathUtil.Flat(_velocity).magnitude;
            sbyte locoMul = 1;
            if (SoulsCamera.Instance && SoulsCamera.Instance.IsLocked && planarSpeed > 0.2f && Vector3.Dot(_velocity.normalized, transform.forward) < -0.3f) locoMul = -1;
            bool locoState = _state == State.Free || _state == State.Drinking;
            float animSpeed = locoState ? planarSpeed : 0f;
            // locked on (not sprinting): keep the guard walk, sped up, never the shoulder-carry run
            if (locoState && SoulsCamera.Instance && SoulsCamera.Instance.IsLocked && !_sprinting) animSpeed = Mathf.Min(animSpeed, 2.55f);
            anim.SetLocomotion(animSpeed, locoMul);
            // archer: the upper-body "guard" layer holds the bow up, nocked, while aiming on the move
            bool guardPose = _blocking || (_aiming && _state == State.Free);
            anim.SetBlocking(guardPose);
            if (Mathf.Abs(_netSpeed.Value - animSpeed) > 0.1f) _netSpeed.Value = animSpeed;
            if (_netLocoMul.Value != locoMul) _netLocoMul.Value = locoMul;
            if (_netBlocking.Value != guardPose) _netBlocking.Value = guardPose;
            Act act = _state switch
            {
                State.Attacking => Act.Attacking, State.Rolling => Act.Rolling, State.Drinking => Act.Drinking,
                State.Parrying => Act.Parrying, State.Hurt => Act.Hurt, State.Dead => Act.Dead, State.Downed => Act.Dead,
                _ => _blocking ? Act.Blocking : Act.Free
            };
            if (_netAct.Value != (byte)act) _netAct.Value = (byte)act;
        }

        void ComputeMoveInput(out Vector3 dir, out float speed)
        {
            ComputeMoveInputDirOnly(out dir);
            float mag = Mathf.Clamp01(dir.magnitude);
            if (mag > 0.001f) dir /= dir.magnitude;
            var cam = SoulsCamera.Instance;
            bool locked = cam && cam.IsLocked;
            if (mag < 0.05f) { speed = 0f; return; }
            if (GameInput.SprintHeld && vitals.Stamina > 0f) { speed = sprintSpeed; _sprinting = true; }
            else if (locked) speed = lockedSpeed * mag;
            else speed = mag < 0.55f ? walkSpeed : runSpeed * mag;
            if (MirrorSlowed) speed *= 0.55f;
        }

        void ComputeMoveInputDirOnly(out Vector3 dir)
        {
            Vector2 input = GameInput.Move;
            if (MirrorInverted) input = -input;      // the Mirror's curse: left is right, forward is back
            var cam = SoulsCamera.Instance;
            Vector3 f = cam ? cam.PlanarForward : Vector3.forward;
            Vector3 r = cam ? cam.PlanarRight : Vector3.right;
            dir = f * input.y + r * input.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
        }

        void UpdateFacing(float dt, Vector3 desiredMove)
        {
            var cam = SoulsCamera.Instance;
            bool locked = cam && cam.IsLocked;
            Vector3 face = Vector3.zero;
            float speed = turnSpeed;
            switch (_state)
            {
                case State.Free:
                case State.Drinking:
                    if (_aiming && !locked) { face = cam.PlanarForward; speed = 900f; }
                    else if (locked && !_sprinting) face = cam.CurrentTarget.transform.position - transform.position;
                    else if (MathUtil.Flat(_velocity).sqrMagnitude > 0.05f) face = _velocity;
                    break;
                case State.Attacking:
                    // tracking while the blade is raised, locked once it swings
                    if (_attack != null && AttackTime() < _attack.hitStart)
                    {
                        if (IsRanged) { face = PredictAimPoint(bow ? bow.GripPosition : transform.position + Vector3.up * 1.5f) - transform.position; speed = 900f; }
                        else if (locked) face = cam.CurrentTarget.transform.position - transform.position;
                        else if (desiredMove.sqrMagnitude > 0.01f) face = desiredMove;
                        if (!IsRanged) speed = 420f;
                    }
                    break;
                case State.Rolling:
                    face = _rollDir; speed = 2000f;
                    break;
            }
            face = MathUtil.Flat(face);
            if (face.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face.normalized), speed * dt);
        }

        // ================================================================== attacks
        /// <summary>Clip time of the current attack, read from the Animator (falls back to a clock).</summary>
        float AttackTime()
        {
            if (_attack == null) return 0f;
            float t = anim.GetStateTime(_attack.state);
            if (t >= 0f) return t;
            return _attack.startTime + (Time.time - _attackStartReal) * _attack.animSpeed;
        }

        /// <param name="index">0..2 = light combo hit, -1 = heavy.</param>
        void StartAttack(int index, bool chained)
        {
            if (_crouched) SetCrouched(false);
            _isHeavy = index < 0;
            _comboIndex = Mathf.Max(0, index);
            _attack = (_isHeavy ? heavyAttack : lightCombo[_comboIndex]).Clone();
            _attack.damage *= WeaponDamageMul; _attack.poiseDamage *= WeaponPoiseMul;
            _attack.damage *= DamageDealtMul;
            ClearSpell();
            _attackStartReal = Time.time;
            _state = State.Attacking;
            _stateTime = 0f;
            _comboQueued = false;
            _heavyCharge = 0f;
            _heavyReleased = !_isHeavy || IsCaster;   // the bard's heavy (Triad) is not charged
            _fired = false;
            if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
            if (vitals.Stamina < _attack.staminaCost)
            {
                _attack.animSpeed *= exhaustedSpeed;
                _attack.damage *= exhaustedDamage;
                _attack.poiseDamage *= exhaustedDamage;
                _attack.knockback *= 0.5f;
            }
            vitals.UseStamina(_attack.staminaCost);

            // where the clip starts: chained hits skip their wind-up; an aimed shot starts bow-up, arrow nocked
            if (chained && _attack.chainStartTime >= 0f) _attack.startTime = _attack.chainStartTime;
            if (IsArcher && !_isHeavy && _aiming && !chained) _attack.startTime = Mathf.Max(_attack.startTime, aimedShotStart);

            if (!IsRanged)   // the archer turns toward the aim point while drawing (UpdateFacing)
            {
                Vector3 f = FaceTowardInputOrTarget(transform.forward);
                if (f.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(f);
            }
            anim.Play(_attack.state, chained ? chainFade : 0.1f, _attack.animSpeed, _attack.startTime);
#if UNITY_EDITOR
            Debug.Log($"[DF] attack {_attack.name} (chained={chained}) start={_attack.startTime:F2} hit={_attack.hitStart:F2}-{_attack.hitEnd:F2} peak={_attack.swingPeak:F2} combo={_attack.comboWindowStart:F2} end={_attack.endTime:F2}");
#endif
            ActionRpc((byte)(_isHeavy ? NetAction.HeavyStart : (NetAction)((int)NetAction.LightAttack1 + _comboIndex)), IsArcher || chained ? _attack.startTime + 1f : 0f);
        }

        void UpdateAttack(float dt, bool rollQueued)
        {
            float t = AttackTime();
            // any swing, shot or cast can be cancelled into a roll at any moment: the stamina of the
            // attack is already spent, the blow (or the spell) is simply lost
            if (rollQueued && vitals.HasStamina() && _state == State.Attacking && !_ultActive)
            {
                _queuedRollTime = -10;
                if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
                ClearSpell();
                if (anim) anim.SetPlaybackSpeed(1f);
                ComputeMoveInputDirOnly(out var rd);
                StartRoll(rd);
                return;
            }
            if (_spellRelease != null) { UpdateSpell(t, dt, rollQueued); return; }

            // buffer every press made during the swing: the chain continues as soon as possible
            if ((GameInput.LightAttackPressed || (GameInput.LightAttackHeld && !_holdBlocked)) && t > _attack.startTime + 0.05f) { _comboQueued = true; _queuedLightTime = -10; }

            // ---- heavy charge: pause at the top of the swing while held
            if (_isHeavy && !_heavyReleased)
            {
                if (t >= heavyHoldTime)
                {
                    if (GameInput.HeavyHeld && _heavyCharge < heavyMaxCharge)
                    {
                        _heavyCharge += dt;
                        anim.SetPlaybackSpeed(0.03f);
                        if (rollQueued && vitals.HasStamina()) { _queuedRollTime = -10; ComputeMoveInputDirOnly(out var d); StartRoll(d); return; }
                    }
                    else ReleaseHeavy();
                }
                else if (!GameInput.HeavyHeld && t > _attack.startTime + 0.1f) { /* tapped: swing normally */ }
                _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, deceleration * dt);
                UpdateHitWindow(t);
                return;
            }

            ApplyLunge(t, dt);
            UpdateHitWindow(t);

            // next combo hit as soon as the blade leaves the arc
            if (!_isHeavy && _comboQueued && t >= _attack.comboWindowStart && vitals.HasStamina())
            {
                StartAttack((_comboIndex + 1) % lightCombo.Length, true);
                return;
            }
            // recovery cancels
            if (t >= _attack.cancelTime)
            {
                if (rollQueued && vitals.HasStamina()) { _queuedRollTime = -10; ComputeMoveInputDirOnly(out var d); StartRoll(d); return; }
                ComputeMoveInputDirOnly(out var mv);
                if (mv.sqrMagnitude > 0.1f && !_comboQueued) { EnterFree(0.3f); return; }
            }
            if (t >= _attack.endTime)
            {
                if (_comboQueued && vitals.HasStamina() && !_isHeavy) { StartAttack((_comboIndex + 1) % lightCombo.Length, true); return; }
                EnterFree(0.3f);
            }
        }

        void ReleaseHeavy()
        {
            _heavyReleased = true;
            float charge01 = Mathf.Clamp01(_heavyCharge / heavyMaxCharge);
            float wm = WeaponDamageMul, pm = WeaponPoiseMul;
            _attack.damage = heavyAttack.damage * (1f + heavyChargeBonus * charge01) * wm * DamageDealtMul;
            _attack.poiseDamage = heavyAttack.poiseDamage * (1f + heavyChargeBonus * charge01) * pm;
            _attack.knockback = heavyAttack.knockback * (1f + 0.6f * charge01);
            _attack.animSpeed = heavyAttack.animSpeed * (1f + charge01 * 0.3f);
            anim.SetPlaybackSpeed(_attack.animSpeed);
            ActionRpc((byte)NetAction.HeavyRelease, charge01);
        }

        void ApplyLunge(float t, float dt)
        {
            if (_attack.lunge > 0f && t >= _attack.lungeStart && t <= _attack.lungeEnd)
            {
                float v = _attack.lunge / Mathf.Max(0.05f, _attack.lungeEnd - _attack.lungeStart) * _attack.animSpeed;
                var cam = SoulsCamera.Instance;
                if (cam && cam.IsLocked && Vector3.Distance(transform.position, cam.CurrentTarget.transform.position) < 1.6f) v = 0f;
                _velocity = transform.forward * v;
            }
            else _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, deceleration * dt);
        }

        void UpdateHitWindow(float t)
        {
            if (IsBard)
            {
                if (!_fired && t >= _attack.hitStart) { _fired = true; if (bard) bard.FireBolt(_attack, _isHeavy); }
                return;
            }
            if (IsSmoker)
            {
                if (!_fired && t >= _attack.hitStart) { _fired = true; if (smoker) { if (_isHeavy) smoker.Gust(_attack); else smoker.FireBolt(_attack); } }
                return;
            }
            if (IsArcher)
            {
                // the arrow leaves on the release frame of the clip (same frame the string snaps)
                if (!_fired && t >= _attack.hitStart) FireArrow();
                return;
            }
            bool on = t >= _attack.hitStart && t <= _attack.hitEnd;
            if (on && !_hitboxOn) { weapon.BeginSwing(); _hitboxOn = true; _hitLanded = false; }
            else if (!on && _hitboxOn)
            {
                weapon.EndSwing(); _hitboxOn = false;
                if (!_hitLanded) AssistHit();
            }
        }

        Vector3 FaceTowardInputOrTarget(Vector3 fallback)
        {
            var cam = SoulsCamera.Instance;
            if (cam && cam.IsLocked) return MathUtil.Flat(cam.CurrentTarget.transform.position - transform.position).normalized;
            ComputeMoveInputDirOnly(out Vector3 d);
            return d.sqrMagnitude > 0.01f ? d.normalized : fallback;
        }

        // ================================================================== resting at a flame
        /// <summary>Owner: kneels a moment by the flame; health, flasks and mana are restored.</summary>
        public void RestAtFlame(CheckpointFire fire)
        {
            if (!IsOwner || IsDead || _state != State.Free) return;
            vitals.RestoreAtFlame();
            _state = State.Drinking;
            _stateTime = 0.2f;
            _drankApplied = true;            // no flask heal: this is a rest
            _velocity = Vector3.zero;
            Vector3 f = MathUtil.Flat(fire.transform.position - transform.position);
            if (f.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(f.normalized);
        }

        // ================================================================== bard spells
        /// <summary>Owner: plays a spell's animation; 'release' runs on its release frame (hitStart).</summary>
        public bool StartSpell(int index, AttackData data, System.Action release)
        {
            if (!IsOwner || _state != State.Free || IsDead || data == null) return false;
            _attack = data.Clone();
            _isHeavy = false;
            _comboIndex = 0;
            _attackStartReal = Time.time;
            _state = State.Attacking;
            _stateTime = 0f;
            _comboQueued = false;
            _heavyReleased = true;
            _fired = false;
            _spellRelease = release;
            _spellIndex = index;
            var cam = SoulsCamera.Instance;
            Vector3 f = cam && cam.IsLocked ? MathUtil.Flat(cam.CurrentTarget.transform.position - transform.position)
                      : cam ? cam.PlanarForward : transform.forward;
            if (f.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(f.normalized);
            anim.Play(_attack.state, 0.12f, _attack.animSpeed, _attack.startTime);
            ActionRpc((byte)NetAction.Spell, index);
            return true;
        }

        /// <summary>Owner: plays the class ultimate; 'release' runs on its decisive frame (hitStart).</summary>
        public bool StartUltimate(AttackData data, System.Action release)
        {
            if (!IsOwner || _state != State.Free || IsDead || data == null) return false;
            if (_crouched) SetCrouched(false);
            _attack = data.Clone();
            _isHeavy = false;
            _comboIndex = 0;
            _attackStartReal = Time.time;
            _state = State.Attacking;
            _stateTime = 0f;
            _comboQueued = false;
            _heavyReleased = true;
            _fired = false;
            _spellRelease = release;
            _spellIndex = -2;
            _ultActive = true;
            var cam = SoulsCamera.Instance;
            Vector3 f = cam && cam.IsLocked ? MathUtil.Flat(cam.CurrentTarget.transform.position - transform.position)
                      : cam ? cam.PlanarForward : transform.forward;
            if (f.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(f.normalized);
            anim.Play(_attack.state, 0.1f, _attack.animSpeed, _attack.startTime);
            ActionRpc((byte)NetAction.Ultimate, 0f);
            return true;
        }

        void UpdateSpell(float t, float dt, bool rollQueued)
        {
            _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, deceleration * dt);
            if (_ultActive)
            {
                if (ult) ult.OwnerUpdateActive(t);
                if (!_fired && t >= _attack.hitStart) { _fired = true; var r0 = _spellRelease; r0?.Invoke(); }
                if (t >= _attack.endTime) EnterFree(0.3f);
                return;
            }
            if (!_fired && t >= _attack.hitStart) { _fired = true; var r = _spellRelease; r?.Invoke(); }
            if (_fired && t >= _attack.cancelTime)
            {
                if (rollQueued && vitals.HasStamina()) { _queuedRollTime = -10; ComputeMoveInputDirOnly(out var d); StartRoll(d); return; }
                ComputeMoveInputDirOnly(out var mv);
                if (mv.sqrMagnitude > 0.1f) { EnterFree(0.3f); return; }
            }
            if (t >= _attack.endTime) EnterFree(0.3f);
        }

        void ClearSpell()
        {
            if (_ultActive) { _ultActive = false; if (ult) ult.OnEnded(); }
            if (_spellRelease == null) return;
            _spellRelease = null;
            bool wasSpell = _spellIndex >= 0;
            _spellIndex = -1;
            if (bard && wasSpell) bard.OnSpellEnded();
        }

        // ================================================================== weapons
        void OnInventoryChanged()
        {
            if (!IsOwner || !IsSpawned) return;
            byte w = Inventory.Equipped(playerClass);
            if (_netWeapon.Value != w) _netWeapon.Value = w;
        }

        void OnWeaponChanged(byte prev, byte now)
        {
            ApplyWeaponVisual(now);
            if (IsSpawned && prev != now && now != Inventory.BaseWeapon)
            {
                Vector3 p = bow ? bow.GripPosition : WeaponPoint();
                Fx.Spawn(FxId.Embers, p);
                if (now == Inventory.Dragon) { BossFx.FireBurst(p, 0.8f); Sfx.Play(SfxId.FireBurst, p, 0.6f, 1.2f); }
                if (now == Inventory.Radiant) { Fx.Spawn(FxId.ParryFlash, p); SpellFx.Sparkles(p, 1.2f); }
                Sfx.Play(SfxId.RelicReveal, p, 0.5f, now == Inventory.Dragon ? 0.9f : 1.2f);
            }
        }

        /// <summary>Shows the equipped weapon (everyone runs this from the replicated value).</summary>
        void ApplyWeaponVisual(byte w)
        {
            bool dragon = w == Inventory.Dragon && dragonWeapon != null;
            bool relic = w == Inventory.Relic && relicWeapon != null;
            bool radiant = w == Inventory.Radiant && radiantWeapon != null;
            if (baseWeaponRenderer) baseWeaponRenderer.enabled = !relic && !dragon && !radiant;
            if (relicWeapon) relicWeapon.SetActive(relic);
            if (radiantWeapon) radiantWeapon.SetActive(radiant);
            if (dragonWeapon) dragonWeapon.SetActive(dragon);
            if (dragonFx) dragonFx.SetActive(dragon);
            if (radiantFx) radiantFx.SetActive(radiant);
            var trail = anim ? anim.weaponTrail : null;
            if (trail)
            {
                if (!_trailColorRead) { _baseTrailColor = trail.color; _baseTrailLife = trail.lifetime; _trailColorRead = true; }
                // each blade cuts its own light: steel-white, ember-orange, then a long blood-red fire wake
                trail.color = radiant ? radiantTrailColor : dragon ? dragonTrailColor : relic ? relicTrailColor : _baseTrailColor;
                trail.lifetime = radiant ? _baseTrailLife * 1.8f : dragon ? _baseTrailLife * 1.6f : relic ? _baseTrailLife * 1.25f : _baseTrailLife;
            }
        }

        /// <summary>Owner: equips a weapon from the inventory (saved, and replicated to the others).</summary>
        public void Equip(byte w)
        {
            if (!IsOwner) return;
            Inventory.SetEquipped(playerClass, w);   // → OnInventoryChanged → _netWeapon
        }

        // ================================================================== archer
        float ArrowGravity => bow && bow.arrowPrefab && bow.arrowPrefab.TryGetComponent(out Arrow a) ? a.gravity : 5.5f;

        /// <summary>
        /// Where a shot released now would go: the locked target, else an enemy near the crosshair
        /// (soft assist), else whatever the crosshair touches. Hip fire leans a few degrees above the
        /// camera's line of sight (the Souls camera looks slightly down); aimed shots go exactly
        /// through the centre of the screen. Also used by the HUD to place the crosshair.
        /// </summary>
        public Vector3 PredictAimPoint(Vector3 from)
        {
            var cam = SoulsCamera.Instance;
            if (cam && cam.IsLocked && cam.CurrentTarget) return cam.CurrentTarget.Point;
            if (cam == null || cam.Cam == null) return from + transform.forward * 30f;
            Ray ray = cam.AimRay;
            Vector3 dir = ray.direction;
            float aim = cam.AimAmount;
            if (aim < 1f) dir = Quaternion.AngleAxis(-hipPitchBias * (1f - aim), cam.transform.right) * dir;
            ray = new Ray(ray.origin, dir);

            LockOnTarget best = null;
            float bestA = aimAssistAngle * Mathf.Lerp(1f, 0.6f, aim);
            foreach (var t in LockOnTarget.All)
            {
                if (t == null || !t.IsValid) continue;
                Vector3 to = t.Point - ray.origin;
                if (to.magnitude > maxAimDistance || Vector3.Dot(to, dir) <= 0f) continue;
                float a = Vector3.Angle(dir, to);
                if (a >= bestA) continue;
                if (Physics.Linecast(from, t.Point, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                bestA = a; best = t;
            }
            if (best != null) return best.Point;

            Vector3 p = ray.GetPoint(maxAimDistance);
            float bestD = maxAimDistance;
            foreach (var h in Physics.RaycastAll(ray, maxAimDistance, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (h.distance < bestD) { bestD = h.distance; p = h.point; }
            }
            // something between the camera and the archer: shoot along the line of sight instead
            if (Vector3.Dot(p - from, dir) < 1.5f) p = from + dir * 25f;
            return p;
        }

        void FireArrow()
        {
            _fired = true;
            if (bow == null || bow.arrowPrefab == null) return;
            Vector3 nock = bow.NockPosition;
            Vector3 chest = transform.position + Vector3.up * 1.45f;
            if (Physics.Linecast(chest, nock, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) nock = chest;   // bow inside a wall
            float speed = (_isHeavy ? chargedArrowSpeed : arrowSpeed) * ArrowSpeedMul;
            Vector3 target = PredictAimPoint(nock);
            // lead the drop: aim a little above so the arrow lands where the crosshair is
            float tt = Vector3.Distance(nock, target) / speed;
            target += Vector3.up * 0.5f * ArrowGravity * tt * tt;
            Vector3 vel = (target - nock).normalized * speed;
            var arrow = Arrow.Fire(bow.arrowPrefab, nock, vel, this, true, _isHeavy, _attack.damage, _attack.poiseDamage, _attack.knockback);
            if (arrow) arrow.SetTier(WeaponTier, _isHeavy);
            ShootRpc(nock, vel, _isHeavy);
            SoulsCamera.Shake(_isHeavy ? 0.035f : 0.012f, 0.1f);
#if UNITY_EDITOR
            Debug.Log($"[DF] arrow {(_isHeavy ? "charged" : "quick")} dmg={_attack.damage:0} speed={speed:0} aimed={_aiming} dist={Vector3.Distance(nock, target):0.0}");
#endif
        }

        /// <summary>The other clients fly a visual copy of the same arrow.</summary>
        [Rpc(SendTo.NotMe)]
        void ShootRpc(Vector3 nock, Vector3 velocity, bool heavy)
        {
            if (bow != null && bow.arrowPrefab != null)
            {
                var a = Arrow.Fire(bow.arrowPrefab, nock, velocity, this, false, heavy, 0f, 0f, 0f);
                if (a) a.SetTier(WeaponTier, heavy);
            }
        }

        // ================================================================== other actions
        void EnterFree(float fade = 0.25f)
        {
            // the other clients must leave the action too: looping states (the Whirlwind spin) have
            // no exit transition and would otherwise keep playing on their screens
            if (_state == State.Attacking || _state == State.Parrying || _state == State.Hurt || _state == State.Drinking)
                ActionRpc((byte)NetAction.Recover, fade);
            if (_state == State.Attacking && _attack != null && _spellRelease == null)
            {
                _lastAttackEnd = Time.time;
                _lastComboIndex = _isHeavy ? -1 : _comboIndex;
            }
            ClearSpell();
            _state = State.Free;
            _stateTime = 0f;
            _attack = null;
            _comboQueued = false;
            if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
            anim.ReturnToLocomotion(fade);
        }

        /// <summary>Owner: crouch / stand (the capsule shrinks; standing up needs head room).</summary>
        void SetCrouched(bool on)
        {
            if (on == _crouched || anim == null || !anim.HasBodyLayer) return;
            if (_ccHeight <= 0f) { _ccHeight = _cc.height; _ccCenterY = _cc.center.y; }
            if (!on)
            {
                float r = _cc.radius * 0.9f;
                Vector3 from = transform.position + Vector3.up * (_cc.height - r);
                if (Physics.SphereCast(from, r, Vector3.up, out _, _ccHeight - _cc.height + 0.05f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return;
            }
            _crouched = on;
            _cc.height = on ? _ccHeight * 0.62f : _ccHeight;
            _cc.center = new Vector3(_cc.center.x, on ? _ccCenterY * 0.62f : _ccCenterY, _cc.center.z);
            anim.SetCrouch(on);
            if (_netCrouch.Value != on) _netCrouch.Value = on;
            Sfx.Play(SfxId.StepDirt, transform.position, 0.4f, on ? 0.8f : 1.1f);
        }

        /// <summary>Owner: drinks a draught (instant, no animation lock).</summary>
        public void DrinkPotion(Potions.Kind k)
        {
            if (!IsOwner || IsDead || !vitals.UsePotion(k)) return;
            PotionFxRpc((byte)k);
        }

        /// <summary>Local clock of the last flask or draught this player was seen drinking (every peer).</summary>
        [System.NonSerialized] public float LastHealTime = -99f;

        [Rpc(SendTo.Everyone)]
        void PotionFxRpc(byte k)
        {
            LastHealTime = Time.time;
            Color c = k == 0 ? new Color(0.7f, 0.72f, 0.8f) : k == 1 ? new Color(0.5f, 1f, 0.45f) : new Color(1f, 0.9f, 0.6f);
            SpellFx.GroundRing(transform.position + Vector3.up * 0.05f, 0.2f, 1.4f, c, 0.6f, 0.8f);
            SpellFx.Sparkles(transform.position + Vector3.up * 1.2f, 0.6f);
            Sfx.Play(SfxId.Drink, transform.position + Vector3.up, 0.8f, k == 1 ? 1.2f : 0.95f);
        }

        /// <summary>Owner, once per session: a lobby that starts from the beginning resets the skill tree.</summary>
        System.Collections.IEnumerator SessionStartChecks()
        {
            Potions.ResetSession();
            float t = 0f;
            while ((CastleProgress.Instance == null || !CastleProgress.Instance.IsSpawned) && t < 8f) { t += Time.deltaTime; yield return null; }
            yield return null;
            if (CastleProgress.Instance != null && CastleProgress.Instance.FreshStart) PlayerSkills.ResetAll();
        }

        void OnCrouchChanged(bool was, bool now) { if (!IsOwner && anim) anim.SetCrouch(now); }

        void StartRoll(Vector3 dir)
        {
            ClearSpell();
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            _rollDir = MathUtil.Flat(dir).normalized;
            _lastComboIndex = -1;
            _state = State.Rolling;
            _stateTime = 0f;
            _attack = null;
            _comboQueued = false;
            if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
            vitals.UseStamina(rollStamina);
            transform.rotation = Quaternion.LookRotation(_rollDir);
            anim.ReturnToLocomotion(0.05f);
            anim.StartRoll(rollDuration);
            Sfx.Play(SfxId.Roll, transform.position, 0.8f);
            ActionRpc((byte)NetAction.Roll, rollDuration);
        }

        void StartParry()
        {
            if (_crouched) SetCrouched(false);
            _state = State.Parrying;
            _stateTime = 0f;
            vitals.UseStamina(parryStamina);
            anim.Play(CharacterAnimator.Parry, 0.05f, 1.8f, 0f);
            ActionRpc((byte)NetAction.Parry, 0f);
        }

        void StartDrink()
        {
            if (_crouched) SetCrouched(false);
            if (!vitals.UseFlask()) return;
            _state = State.Drinking;
            _stateTime = 0f;
            _drankApplied = false;
            Sfx.Play(SfxId.Drink, transform.position + Vector3.up);
            LastHealTime = Time.time;
            ActionRpc((byte)NetAction.Drink, 0f);
        }

        void EnterHurt(NetAction kind, float duration)
        {
            if (_crouched) SetCrouched(false);
            ClearSpell();
            _state = State.Hurt;
            _stateTime = 0f;
            _hurtTime = duration;
            _attack = null;
            _lastComboIndex = -1;
            _comboQueued = false;
            if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
            anim.SetPlaybackSpeed(1f);
            PlayActionLocal(kind, 0f);
            ActionRpc((byte)kind, 0f);
        }

        void EnterDeath(bool giveUp = false)
        {
            if (_crouched) SetCrouched(false);
            bool wasDown = _state == State.Downed;
            ClearSpell();
            _state = State.Dead;
            _stateTime = 0f;
            _attack = null;
            if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
            if (!wasDown) { PlayActionLocal(NetAction.Death, 0f); ActionRpc((byte)NetAction.Death, 0f); }
            if (SoulsCamera.Instance) SoulsCamera.Instance.ClearLock();
            // fell inside a boss fight: gave up → back at the flame but barred from that fight;
            // bled out → watch the survivors until the battle is won or lost
            int arena = FightArenaAt(transform.position);
            _giveUpRespawn = giveUp;
            _spectateArena = 0;
            if (arena != 0) { if (giveUp) _bannedArena = arena; else _spectateArena = arena; }
            LocalDied?.Invoke(this);
        }

        // ================================================================== the Mirror's curses (owner)
        float _invertUntil, _slowUntil;
        Vector3 _pullCenter; float _pullUntil, _pullStrength;
        public bool MirrorInverted => Time.time < _invertUntil;
        public bool MirrorSlowed => Time.time < _slowUntil;
        public float MirrorCurseLeft => Mathf.Max(_invertUntil, _slowUntil) - Time.time;
        public static event System.Action<PlayerController, byte> LocalMirrorCursed;   // 1 = inverted, 2 = slowed

        /// <summary>Server → the victim's owner: "Reflection of Malice" (1 = inverted controls, 2 = slowed).</summary>
        [Rpc(SendTo.Owner)]
        public void MirrorCurseRpc(byte kind, float duration)
        {
            if (kind == 1) _invertUntil = Time.time + duration; else _slowUntil = Time.time + duration;
            LocalMirrorCursed?.Invoke(this, kind);
        }

        // ================================================================== the Reaper's Call of Souls (owner)
        bool _grabbed;
        float _grabUntil, _grabDrain, _grabSlam;
        Vector3 _grabPos, _grabThrow;
        public bool SoulGrabbed => _grabbed;
        public static event System.Action<PlayerController> LocalSoulGrabbed;

        /// <summary>Server → the victim's owner: the skeletal hand closed on you. Lifted to 'holdPos', drained, then hurled down.</summary>
        [Rpc(SendTo.Owner)]
        public void SoulGrabRpc(Vector3 holdPos, float hold, float drainPerSecond, float slamDamage, Vector3 throwDir)
        {
            if (IsDead || _state == State.Dead || _state == State.Downed || (_grabbed && _state == State.Hurt) || DevGodOn) return;
            if (_crouched) SetCrouched(false);
            _grabbed = true;
            _grabUntil = Time.time + hold;
            _grabPos = holdPos; _grabDrain = drainPerSecond; _grabSlam = slamDamage; _grabThrow = MathUtil.Flat(throwDir).normalized;
            EnterHurt(NetAction.GuardBreak, hold + 5f);
            SoulsCamera.Shake(0.08f, 0.4f);
            LocalSoulGrabbed?.Invoke(this);
        }

        void UpdateSoulGrab(float dt)
        {
            _velocity = Vector3.zero; _vy = 0f;
            _cc.enabled = false;
            transform.position = Vector3.Lerp(transform.position, _grabPos, 1f - Mathf.Exp(-5f * dt));
            _cc.enabled = true;
            vitals.Damage(_grabDrain * dt, true);
            if (vitals.IsDead || vitals.IsDowned) { _grabbed = false; return; }
            if (Time.time < _grabUntil) return;
            // hurled to the ground: heavy fall, then the curse of the grave (slowed)
            _grabbed = false;
            vitals.Damage(_grabSlam, true);
            SoulsCamera.Shake(0.2f, 0.45f);
            if (vitals.IsDead || vitals.IsDowned) return;
            EnterHurt(NetAction.Knockdown, knockdownDuration);
            _hitInvulnUntil = Time.time + knockdownDuration + 0.3f;
            _velocity = _grabThrow * 7f;
            _vy = -6f;
            _slowUntil = Time.time + 6f;
            LocalMirrorCursed?.Invoke(this, 2);
        }

        /// <summary>Everyone (each owner moves itself): the Soul Vortex drags players toward its centre.</summary>
        public static void BeginVortexPull(Vector3 center, float duration, float strength)
        {
            var me = Local;
            if (me == null) return;
            me._pullCenter = center; me._pullUntil = Time.time + duration; me._pullStrength = strength;
        }

        Vector3 ExternalPull()
        {
            if (Time.time >= _pullUntil || _state == State.Dead || _state == State.Downed || _state == State.Traversing) return Vector3.zero;
            Vector3 d = MathUtil.Flat(_pullCenter - transform.position);
            float dist = d.magnitude;
            if (dist < 0.6f || dist > 30f) return Vector3.zero;
            float k = _state == State.Rolling ? 0.35f : 1f;            // a roll fights the current
            return d / dist * _pullStrength * k * Mathf.Clamp01(1.3f - dist / 30f);
        }

        // ================================================================== downed / revive / spectate
        public const float ReviveStartDistance = 2.2f;
        int _spectateArena, _bannedArena;
        bool _giveUpRespawn;
        float _giveUpHold;
        ulong _reviverId = ulong.MaxValue;
        PlayerController _spectating;
        GameObject _reviveFx;
        Transform _ringOuter, _ringFill;
        Light _reviveLight;
        MaterialPropertyBlock _mpb;

        /// <summary>Local, dead in a boss fight: whom the camera follows.</summary>
        public PlayerController SpectateTarget => _spectating;
        public bool IsSpectating => _state == State.Dead && _spectateArena != 0;
        public bool IsGivingUp => _giveUpHold > 0.05f;
        public float GiveUp01 => Mathf.Clamp01(_giveUpHold);
        public bool IsBannedFrom(int arena) => arena != 0 && _bannedArena == arena;
        void ClearBan() => _bannedArena = 0;
        public static event System.Action<PlayerController, PlayerController> LocalRevived;   // (me, reviver)

        /// <summary>1 = inside the throne room during the King's fight, 2 = on the terrace during the dragon's, else 0.</summary>
        public static int FightArenaAt(Vector3 p)
        {
            var lv = LevelRefs.Instance;
            if (ArenaFighting(1) && (lv == null || lv.IsInsideArena(p))) return 1;
            if (ArenaFighting(2) && (lv == null || lv.IsInsideDragonArena(p))) return 2;
            if (ArenaFighting(3) && (lv == null || lv.IsInsideMirrorArena(p))) return 3;
            if (ArenaFighting(4) && (lv == null || lv.IsInsideReaperArena(p))) return 4;
            return 0;
        }

        public static bool ArenaFighting(int arena)
        {
            if (arena == 1) { var a = BossArena.Instance; return a != null && a.IsSpawned && a.State == BossArena.ArenaState.Fighting; }
            if (arena == 2) { var d = DragonArena.Instance; return d != null && d.IsSpawned && d.State == BossArena.ArenaState.Fighting; }
            if (arena == 3) { var m = MirrorArena.Instance; return m != null && m.IsSpawned && m.State == BossArena.ArenaState.Fighting; }
            if (arena == 4) { var r = ReaperArena.Instance; return r != null && r.IsSpawned && r.State == BossArena.ArenaState.Fighting; }
            return false;
        }

        void EnterDowned()
        {
            ClearSpell();
            _state = State.Downed;
            _stateTime = 0f;
            _attack = null;
            _giveUpHold = 0f;
            _reviverId = ulong.MaxValue;
            if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
            PlayActionLocal(NetAction.Death, 0f);
            ActionRpc((byte)NetAction.Death, 0f);
            if (SoulsCamera.Instance) SoulsCamera.Instance.ClearLock();
        }

        bool HelperInCircle()
        {
            float r2 = PlayerVitals.ReviveRadius * PlayerVitals.ReviveRadius;
            foreach (var o in All)
            {
                if (o == null || o == this || !o.IsSpawned || o.IsDead) continue;
                Vector3 d = o.transform.position - transform.position;
                if (Mathf.Abs(d.y) < 2f && MathUtil.Flat(d).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        void Revived()
        {
            EnterFree(0.3f);
            PlayActionLocal(NetAction.Respawn, 0f);
            ActionRpc((byte)NetAction.Respawn, 0f);
            _hitInvulnUntil = Time.time + 2f;
            PlayerController reviver = null;
            foreach (var o in All) if (o && o.OwnerClientId == _reviverId) reviver = o;
            if (reviver && reviver.vitals) reviver.vitals.AddReviveRpc();
            RevivedFxRpc();
            LocalRevived?.Invoke(this, reviver);
        }

        [Rpc(SendTo.Everyone)]
        void PvpKillRpc(NetworkObjectReference killer)
        {
            if (killer.TryGet(out NetworkObject k) && k.TryGetComponent(out PlayerController kp)) PvpKill?.Invoke(kp, this);
        }

        /// <summary>Our PvP blow was parried by another player: a short stagger, like a monster.</summary>
        [Rpc(SendTo.Owner)]
        public void PvpParriedRpc()
        {
            if (IsDead || _state == State.Dead) return;
            EnterHurt(NetAction.GuardBreak, guardBreakDuration * 0.8f);
            SoulsCamera.Shake(0.06f, 0.2f);
        }

        [Rpc(SendTo.Owner)]
        void ReviveRequestRpc(ulong reviverClientId)
        {
            if (vitals == null || !vitals.IsDowned) return;
            if (vitals.ReviveState == 0) _reviverId = reviverClientId;
            vitals.StartRevive();
        }

        [Rpc(SendTo.Everyone)]
        void RevivedFxRpc()
        {
            Vector3 p = transform.position;
            SpellFx.HealBurst(transform);
            SpellFx.GroundRing(p + Vector3.up * 0.05f, 0.4f, PlayerVitals.ReviveRadius + 0.6f, SpellFx.Heal, 0.9f);
            Sfx.Play(SfxId.SpellHeal, p + Vector3.up, 1f, 1.1f);
        }

        /// <summary>E on a downed ally (IInteractable, evaluated on the other players' copies).</summary>
        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            if (IsOwner || vitals == null || !vitals.IsDowned) return false;
            var me = Local;
            if (me == null || !me.CanInteract) return false;
            Vector3 d = transform.position - me.transform.position;
            float dist = MathUtil.Flat(d).magnitude;
            if (Mathf.Abs(d.y) > 2f) return false;
            if (vitals.ReviveState != 0)
            {
                if (dist > PlayerVitals.ReviveRadius + 1.5f) return false;
                prompt = dist <= PlayerVitals.ReviveRadius ? $"Reviving {DisplayName}… stay in the circle" : $"Step into the circle to revive {DisplayName}";
                return false;
            }
            if (dist > ReviveStartDistance) return false;
            prompt = $"Revive {DisplayName}";
            return true;
        }

        void UpdateSpectate()
        {
            var alive = new List<PlayerController>();
            foreach (var o in All) if (o && o != this && o.IsSpawned && !o.IsDead) alive.Add(o);
            if (alive.Count == 0) return;
            bool next = GameInput.LightAttackPressed || GameInput.LockOnPressed;
            if (_spectating == null || !alive.Contains(_spectating) || next)
            {
                int i = _spectating ? alive.IndexOf(_spectating) : -1;
                _spectating = alive[(i + 1 + alive.Count) % alive.Count];
                if (SoulsCamera.Instance) SoulsCamera.Instance.SetFollowTarget(_spectating.transform);
            }
        }

        /// <summary>The circle on the ground around a downed player (every client): red while bleeding,
        /// gold once a revive has started, bright green with a growing fill while an ally stands in it.</summary>
        void UpdateReviveFx()
        {
            bool down = vitals != null && vitals.IsDowned && IsSpawned;
            if (!down) { if (_reviveFx && _reviveFx.activeSelf) _reviveFx.SetActive(false); return; }
            if (_reviveFx == null) BuildReviveFx();
            if (_reviveFx == null) return;
            if (!_reviveFx.activeSelf) _reviveFx.SetActive(true);
            _reviveFx.transform.SetPositionAndRotation(transform.position + Vector3.up * 0.06f, Quaternion.identity);
            byte st = vitals.ReviveState;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (st == 0 ? 5f : 3f));
            float R = PlayerVitals.ReviveRadius;
            Color c; float radius;
            if (st == 0) { c = new Color(1f, 0.15f, 0.08f, 0.55f + 0.35f * pulse); radius = 1.1f + 0.1f * pulse; }
            else if (st == 1) { c = new Color(1f, 0.75f, 0.3f, 0.5f + 0.4f * pulse); radius = R; }
            else { c = new Color(0.45f, 1f, 0.55f, 0.95f); radius = R; }
            _ringOuter.localScale = new Vector3(radius * 2.2f, radius * 2.2f, 1f);
            SetColor(_ringOuter, c);
            float f = st == 0 ? 0f : vitals.Revive01;
            _ringFill.gameObject.SetActive(f > 0.01f);
            _ringFill.localScale = new Vector3(R * 2f * f, R * 2f * f, 1f);
            SetColor(_ringFill, new Color(0.5f, 1f, 0.6f, 0.55f));
            if (_reviveLight)
            {
                _reviveLight.color = st == 2 ? SpellFx.Heal : st == 1 ? new Color(1f, 0.75f, 0.3f) : new Color(1f, 0.2f, 0.1f);
                _reviveLight.intensity = st == 2 ? 3f + vitals.Revive01 * 4f : 1.5f + pulse;
            }
        }

        void SetColor(Transform t, Color c)
        {
            _mpb ??= new MaterialPropertyBlock();
            var r = t.GetComponent<Renderer>();
            r.GetPropertyBlock(_mpb); _mpb.SetColor("_BaseColor", c); _mpb.SetColor("_Color", c); r.SetPropertyBlock(_mpb);
        }

        void BuildReviveFx()
        {
            var mat = SpellFx.Instance ? SpellFx.Instance.ringMaterial : null;
            if (mat == null) return;
            _reviveFx = new GameObject("ReviveCircle_" + OwnerClientId);
            Transform Quad(string n, Material m)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = n; Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(_reviveFx.transform, false);
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                return q.transform;
            }
            _ringOuter = Quad("Ring", mat);
            _ringFill = Quad("Fill", SpellFx.Instance.glowMaterial ? SpellFx.Instance.glowMaterial : mat);
            _ringFill.localPosition = Vector3.up * 0.01f;
            var lg = new GameObject("Light"); lg.transform.SetParent(_reviveFx.transform, false); lg.transform.localPosition = Vector3.up * 1.2f;
            _reviveLight = lg.AddComponent<Light>(); _reviveLight.type = LightType.Point; _reviveLight.range = 6f; _reviveLight.shadows = LightShadows.None;
        }

        void Respawn()
        {
            var lv = LevelRefs.Instance;
            Vector3 p = lv ? lv.GetPlayerSpawn(OwnerClientId, out _) : Vector3.up;
            Quaternion rot = lv && lv.playerSpawn ? lv.playerSpawn.rotation : Quaternion.identity;
            TeleportTo(p, rot);
            vitals.ResetAll();
            _spectateArena = 0; _giveUpRespawn = false; _spectating = null;
            if (SoulsCamera.Instance) SoulsCamera.Instance.SetFollowTarget(transform);
            _velocity = Vector3.zero;
            EnterFree();
            Sfx.Play(SfxId.Respawn, transform.position + Vector3.up, 0.7f);
            ActionRpc((byte)NetAction.Respawn, 0f);
        }

        public void TeleportTo(Vector3 pos, Quaternion rot)
        {
            _cc.enabled = false;
            if (_nt != null && _nt.IsSpawned && _nt.CanCommitToTransform) _nt.Teleport(pos, rot, transform.localScale);
            else transform.SetPositionAndRotation(pos, rot);
            _cc.enabled = true;
        }

        public bool BeginTraverse(Vector3 from, Vector3 to)
        {
            if (!IsOwner || _state != State.Free) return false;
            _traverseFrom = from; _traverseTo = to;
            transform.rotation = Quaternion.LookRotation(MathUtil.Flat(to - from));
            anim.ReturnToLocomotion(0.1f);
            _state = State.Traversing;
            _stateTime = 0f;
            Sfx.Play2D(SfxId.FogPass, 0.9f);
            return true;
        }

        /// <summary>Development: teleport "x;y;z" (editor test tools and the F9 developer menu).</summary>
        public void DevTeleport(string xyz)
        {
            if (!IsOwner) return;
            var v = System.Array.ConvertAll(xyz.Split(';'), f => float.Parse(f, System.Globalization.CultureInfo.InvariantCulture));
            TeleportTo(new Vector3(v[0], v[1], v[2]), transform.rotation);
        }

        /// <summary>Development only: toggles invulnerability (to watch boss patterns).</summary>
        /// <summary>Development: "1" = fall downed (bleed-out HUD, revive circle).</summary>
        public void DevDown(string _) { if (IsOwner) vitals.DevGoDown(); }
        /// <summary>Development: as if the nearest ally pressed E on us (the ally must still walk into the circle).</summary>
        public void DevReviveStart(string _)
        {
            if (!IsOwner || !vitals.IsDowned) return;
            foreach (var o in All) if (o && o != this) { _reviverId = o.OwnerClientId; break; }
            vitals.StartRevive();
        }

        public void DevGod(string on) { _devGod = on == "1"; Debug.Log("[DF] god mode " + _devGod); }
        public bool DevGodEnabled => _devGod;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /// <summary>Development only: equips "0" (starting weapon) or "1" (relic).</summary>
        public void DevEquip(string w) => Equip(byte.Parse(w));
        /// <summary>Development: "1" crouches, "0" stands up; DevRoll rolls forward.</summary>
        public void DevCrouch(string on) { if (IsOwner && _state == State.Free) SetCrouched(on == "1"); }
        public void DevRoll(string _) { if (IsOwner && _state == State.Free) StartRoll(transform.forward); }
        /// <summary>Development: plays only the roll's visual, slowed to "seconds" (to look at its frames).</summary>
        public void DevRollVisual(string seconds) => anim.StartRoll(float.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture));
        /// <summary>Development: "0".."2" = that light-combo hit, "h" = the heavy (archer: the shot fires as usual).</summary>
        public void DevAttack(string i) { if (IsOwner && _state == State.Free) StartAttack(i == "h" ? -1 : int.Parse(i), false); }

        /// <summary>Development only: "State;clipSeconds" freezes the body on that frame (renders of poses, bow draw…).</summary>
        public void DevPose(string arg)
        {
            var a = arg.Split(';');
            float t = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
            float speed = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
            anim.Play(a[0], 0.02f, 1f, Mathf.Max(0f, t - 0.08f));   // lands on t when frozen
            StartCoroutine(FreezeNextFrame(speed));
            Debug.Log($"[DF] pose {a[0]} @ {t:F2}s");
        }

        System.Collections.IEnumerator FreezeNextFrame(float speed) { yield return new WaitForSeconds(0.08f); anim.SetPlaybackSpeed(speed); }
#endif

        // ================================================================== hits dealt
        void OnWeaponHit(Collider col, Vector3 point)
        {
            if (!IsOwner || _attack == null) return;
            var other = col.GetComponentInParent<PlayerController>();
            if (other != null)
            {
                if (CanPvp(this, other))
                {
                    _hitLanded = true;
                    anim.HitStop(_isHeavy ? 0.1f : 0.06f);
                    SoulsCamera.Shake(_isHeavy ? 0.09f : 0.04f, 0.18f);
                    other.ReceiveHitRpc(_attack.damage * PvpDamageMul, _attack.poiseDamage, transform.position,
                                        (byte)(_isHeavy ? HitFlags.Heavy : HitFlags.Parryable), NetworkObject);
                }
                return;
            }
            var enemy = col.GetComponentInParent<EnemyBase>();
            if (enemy == null || !enemy.IsAlive) return;
            Land(enemy, point);
        }

        /// <summary>Contact: instant local reaction (same frame as the blade), then server damage.</summary>
        void Land(EnemyBase enemy, Vector3 point)
        {
            _hitLanded = true;
            anim.HitStop(_isHeavy ? 0.12f : 0.075f);
            SoulsCamera.Shake(_isHeavy ? 0.11f : 0.045f, _isHeavy ? 0.24f : 0.14f);
            Vector3 swing = weapon ? weapon.TipVelocity : Vector3.zero;
            if (swing.sqrMagnitude < 1f) swing = transform.right;
            enemy.LocalHitReact(point, _isHeavy, transform.position, swing);
            enemy.TakeHitRpc(_attack.damage, _attack.poiseDamage, point, transform.position, _attack.knockback);
            PlayerUltimate.LocalDealt(_attack.damage);
            if (WeaponTier == Inventory.Dragon) { DragonCutFx(point); DragonCutRpc(point); }
            else if (WeaponTier == Inventory.Radiant) { RadiantCutFx(point); RadiantCutRpc(point); }
        }

        /// <summary>Wyrmfang's cut: a burst of fire and embers where the blade bites.</summary>
        static void DragonCutFx(Vector3 point)
        {
            Fx.SpawnScaled(FxId.Fireball, point, 0.55f);
            BossFx.Embers(point, 0.3f, 18, 4f);
        }
        [Rpc(SendTo.NotMe)] void DragonCutRpc(Vector3 point) => DragonCutFx(point);
        /// <summary>Dawnbreaker's cut: a white flash and a spray of golden sparks.</summary>
        static void RadiantCutFx(Vector3 point) { Fx.Spawn(FxId.ParryFlash, point); SpellFx.Sparkles(point, 0.9f); }
        [Rpc(SendTo.NotMe)] void RadiantCutRpc(Vector3 point) => RadiantCutFx(point);

        /// <summary>
        /// Fallback if the blade capsule just missed (animation grazing the target): hits the
        /// closest enemy in a small frontal cone at the end of the arc.
        /// </summary>
        void AssistHit()
        {
            EnemyBase best = null; float bestD = 2.5f;
            foreach (var e in EnemyBase.All)
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 to = e.transform.position - transform.position;
                float reach = 2.5f + (e is BossAI ? 1.4f : 0f);
                float d = MathUtil.Flat(to).magnitude;
                if (d > reach || Mathf.Abs(to.y) > 2.5f) continue;
                float cone = _attack.state == CharacterAnimator.Spin ? 180f : 60f;
                if (Vector3.Angle(transform.forward, MathUtil.Flat(to)) > cone) continue;
                if (best == null || d < bestD) { best = e; bestD = d; }
            }
            if (best != null) { Land(best, best.transform.position + Vector3.up * 1.2f); return; }
            // duel: the same small assist against an opponent
            foreach (var p in All)
            {
                if (!CanPvp(this, p)) continue;
                Vector3 to = p.transform.position - transform.position;
                if (MathUtil.Flat(to).magnitude > 2.4f || Mathf.Abs(to.y) > 2f) continue;
                float cone = _attack.state == CharacterAnimator.Spin ? 180f : 55f;
                if (Vector3.Angle(transform.forward, MathUtil.Flat(to)) > cone) continue;
                _hitLanded = true;
                anim.HitStop(_isHeavy ? 0.1f : 0.06f);
                p.ReceiveHitRpc(_attack.damage * PvpDamageMul, _attack.poiseDamage, transform.position,
                                (byte)(_isHeavy ? HitFlags.Heavy : HitFlags.Parryable), NetworkObject);
                return;
            }
        }

        // ================================================================== hits received
        [Rpc(SendTo.Owner)]
        public void ReceiveHitRpc(float damage, float poiseDamage, Vector3 attackerPos, byte flags, NetworkObjectReference attackerRef)
        {
            if (IsDead || _state == State.Dead) return;
            PlayerController pvpAttacker = null;
            if (attackerRef.TryGet(out NetworkObject atkObj)) pvpAttacker = atkObj.GetComponent<PlayerController>();
            if (pvpAttacker != null && !CanPvp(pvpAttacker, this)) return;      // checked again on the victim's side
            var f = (HitFlags)flags;
            if (IsInvulnerable) return;
            if (buffs) damage *= buffs.DamageTakenMul;
            if (UltArmor) damage *= 0.6f;
            if (armor) damage *= armor.DamageTakenMul;
            damage *= vitals.PotionDamageTakenMul;

            bool facing = CombatUtil.IsInFront(transform, attackerPos, 75f);

            // parry with the greatsword
            if (_state == State.Parrying && _stateTime <= parryWindow && facing && (f & HitFlags.Parryable) != 0)
            {
                if (attackerRef.TryGet(out NetworkObject attackerObj) && attackerObj.TryGetComponent(out EnemyBase enemy))
                    enemy.ParriedRpc();
                else if (pvpAttacker != null) pvpAttacker.PvpParriedRpc();
                anim.HitStop(0.12f);
                SoulsCamera.Shake(0.07f, 0.2f);
                FeedbackRpc((byte)HitResult.Parried, WeaponPoint());
                return;
            }

            // block with the greatsword
            if (_blocking && facing && (f & HitFlags.Unblockable) == 0)
            {
                vitals.UseStamina(damage * blockStaminaPerDamage);
                if (vitals.Stamina <= 0f)
                {
                    vitals.Damage(damage * 0.35f);
                    _velocity = MathUtil.Flat(transform.position - attackerPos).normalized * 4.5f;
                    SoulsCamera.Shake(0.1f, 0.25f);
                    FeedbackRpc((byte)HitResult.GuardBreak, WeaponPoint());
                    if (!vitals.IsDead) EnterHurt(NetAction.GuardBreak, guardBreakDuration);
                }
                else
                {
                    vitals.Damage(damage * ((f & HitFlags.Dark) != 0 ? Mathf.Max(blockChip, 0.45f) : blockChip));
                    _velocity = MathUtil.Flat(transform.position - attackerPos).normalized * ((f & HitFlags.Heavy) != 0 ? 5f : 3.2f);
                    SoulsCamera.Shake(0.04f);
                    anim.HitStop(0.06f);
                    FeedbackRpc((byte)HitResult.Blocked, WeaponPoint());
                }
                return;
            }

            vitals.Damage(damage, pvpAttacker == null);
            FeedbackRpc((byte)HitResult.Hit, transform.position + Vector3.up * 1.2f);
            if (vitals.IsDead)
            {
                if (pvpAttacker != null) { pvpAttacker.vitals.AddKillRpc(); PvpKillRpc(pvpAttacker.NetworkObject); }
                return;
            }
            bool hyper = _state == State.Attacking && _attack != null && _attack.hyperArmor;
            Vector3 away = MathUtil.Flat(transform.position - attackerPos).normalized;
            if (UltArmor) { SoulsCamera.Shake(0.08f, 0.2f); return; }
            if (pvpAttacker != null)
            {
                // duel: the blow staggers briefly, close enough for the attacker to press on
                SoulsCamera.Shake((f & HitFlags.Heavy) != 0 ? 0.14f : 0.08f, 0.2f);
                if (!hyper || (f & HitFlags.Heavy) != 0)
                {
                    bool heavyHit = (f & HitFlags.Heavy) != 0;
                    EnterHurt(NetAction.Hurt, heavyHit ? hurtDuration * 1.2f : hurtDuration * 0.75f);
                    _hitInvulnUntil = Time.time + 0.18f;
                    _velocity = away * (heavyHit ? 3.6f : 1.6f);
                }
                return;
            }
            if ((f & HitFlags.Heavy) != 0)
            {
                SoulsCamera.Shake(0.2f, 0.35f);
                EnterHurt(NetAction.Knockdown, knockdownDuration);
                _hitInvulnUntil = Time.time + knockdownDuration + 0.3f;
                _velocity = away * 6f;
            }
            else if (!hyper && poiseDamage > 5f)
            {
                SoulsCamera.Shake(0.09f, 0.2f);
                EnterHurt(NetAction.Hurt, hurtDuration);
                _hitInvulnUntil = Time.time + hurtDuration + 0.25f;
                _velocity = away * 5.5f;
            }
            else SoulsCamera.Shake(0.05f);
        }

        Vector3 WeaponPoint() => weapon && weapon.tipPoint ? Vector3.Lerp(weapon.basePoint.position, weapon.tipPoint.position, 0.4f) : transform.position + transform.forward * 0.6f + Vector3.up * 1.2f;

        public enum HitResult : byte { Hit, Blocked, GuardBreak, Parried }

        [Rpc(SendTo.Everyone)]
        void FeedbackRpc(byte result, Vector3 point)
        {
            if (IsServer) Debug.Log($"[DF] {DisplayName}: {(HitResult)result} (HP {(vitals ? vitals.Health : 0):0})");
            switch ((HitResult)result)
            {
                case HitResult.Hit: Fx.Spawn(FxId.Blood, point); Sfx.Play(SfxId.Hurt, point); if (anim) anim.HitReact(0.06f); break;
                case HitResult.Blocked: Fx.Spawn(FxId.BlockSparks, point, transform.forward); Sfx.Play(SfxId.Block, point); break;
                case HitResult.GuardBreak: Fx.Spawn(FxId.BlockSparks, point); Sfx.Play(SfxId.GuardBreak, point); break;
                case HitResult.Parried: Fx.Spawn(FxId.ParryFlash, point); Fx.Spawn(FxId.BlockSparks, point, transform.forward); Sfx.Play(SfxId.Parry, point); break;
            }
        }

        // ================================================================== animation sync
        [Rpc(SendTo.NotMe)]
        void ActionRpc(byte action, float param) => PlayActionLocal((NetAction)action, param);

        void PlayActionLocal(NetAction action, float param)
        {
            if (anim == null) return;
            switch (action)
            {
                case NetAction.LightAttack1: case NetAction.LightAttack2: case NetAction.LightAttack3:
                {
                    var a = lightCombo[(int)action - (int)NetAction.LightAttack1];
                    // param = clip start + 1 when it isn't the default one (chained hit, aimed shot)
                    bool custom = param > 0.5f;
                    float start = custom ? param - 1f : a.startTime;
                    bool chained = (int)action > (int)NetAction.LightAttack1 || (custom && Mathf.Abs(start - a.chainStartTime) < 0.001f);
                    anim.Play(a.state, chained ? chainFade : 0.1f, a.animSpeed, start);
                    _remoteHeavyHold = false;
                    break;
                }
                case NetAction.HeavyStart: anim.Play(heavyAttack.state, 0.1f, heavyAttack.animSpeed, heavyAttack.startTime); _remoteHeavyHold = true; break;
                case NetAction.HeavyRelease: _remoteHeavyHold = false; anim.SetPlaybackSpeed(heavyAttack.animSpeed * (1f + param * 0.3f)); break;
                case NetAction.Roll: _remoteHeavyHold = false; anim.ReturnToLocomotion(0.05f); anim.StartRoll(param > 0 ? param : rollDuration); if (!IsOwner) Sfx.Play(SfxId.Roll, transform.position, 0.6f); break;
                case NetAction.Parry: anim.Play(CharacterAnimator.Parry, 0.05f, 1.8f, 0f); break;
                case NetAction.Hurt: _remoteHeavyHold = false; anim.Play(CharacterAnimator.Hurt, 0.05f, 1.3f); break;
                case NetAction.GuardBreak: _remoteHeavyHold = false; anim.Play(CharacterAnimator.Hurt, 0.05f, 0.8f); break;
                case NetAction.Knockdown: _remoteHeavyHold = false; anim.Play(CharacterAnimator.Knockdown, 0.05f, 1.2f); break;
                case NetAction.Death: _remoteHeavyHold = false; anim.Play(CharacterAnimator.Death, 0.1f, 1f); Sfx.Play(SfxId.Death, transform.position, 0.9f); break;
                case NetAction.Respawn: anim.ReturnToLocomotion(0.1f); break;
                case NetAction.Recover: _remoteHeavyHold = false; anim.ReturnToLocomotion(param > 0f ? param : 0.25f); break;
                case NetAction.Drink: LastHealTime = Time.time; if (!IsOwner) Sfx.Play(SfxId.Drink, transform.position + Vector3.up, 0.6f); break;
                case NetAction.Ultimate:
                    if (ult && ult.cast != null) anim.Play(ult.cast.state, 0.1f, ult.cast.animSpeed, ult.cast.startTime);
                    _remoteHeavyHold = false;
                    break;
                case NetAction.Spell:
                {
                    int i = Mathf.RoundToInt(param);
                    if (smoker && smoker.exhaleCast != null) anim.Play(smoker.exhaleCast.state, 0.12f, smoker.exhaleCast.animSpeed, smoker.exhaleCast.startTime);
                    else if (bard && i >= 0 && i < bard.spells.Length && bard.spells[i]?.cast != null)
                    {
                        var c = bard.spells[i].cast;
                        anim.Play(c.state, 0.12f, c.animSpeed, c.startTime);
                    }
                    _remoteHeavyHold = false;
                    break;
                }
            }
        }
    }
}
