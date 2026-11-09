using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Forma della zona di danno di un attacco nemico.</summary>
    public enum HitShape : byte
    {
        Weapon,     // sweep della capsula sull'arma (hitbox reale)
        FrontArc,   // cono frontale (fendente ampio)
        Radial,     // cerchio attorno al nemico
        Projectile, // nessuna zona: OnAttackImpact lancia un proiettile (arcieri, palle di fuoco)
    }

    /// <summary>
    /// Base comune di guardiani e boss. Il SERVER è autoritativo: IA, salute, postura e danni.
    /// I client ricevono posizione (NetworkTransform), salute (NetworkVariable) e animazioni (RPC).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public abstract class EnemyBase : NetworkBehaviour, IHealthView, IHurtbox
    {
        public static readonly List<EnemyBase> All = new List<EnemyBase>();

        [Header("Identità")]
        public string displayName = "Corrupted Guardian";

        [Header("Statistiche")]
        public float baseHealth = 110f;
        public float basePoise = 40f;
        public float poiseRegenPerSecond = 12f;
        public float poiseRegenDelay = 2.5f;
        public float staggerDuration = 1.6f;
        public float parryStaggerDuration = 2.4f;
        [Tooltip("Damage multiplier of the first hit after a PARRY (riposte).")]
        public float staggeredDamageMultiplier = 2f;
        [Tooltip("After a poise break the enemy can't be staggered again for this long (no stun-lock).")]
        public float stanceImmunity = 3f;
        [Tooltip("1 = full knockback, 0 = immovable (boss).")]
        public float knockbackFactor = 1f;

        [Header("Movimento")]
        public float walkSpeed = 1.8f;
        public float chaseSpeed = 3.4f;
        public float turnSpeed = 300f;
        public float gravity = 24f;

        [Header("Riferimenti")]
        public CharacterAnimator anim;
        public WeaponHitbox weapon;

        // ---- stato di rete (scritto dal server)
        protected readonly NetworkVariable<float> netHealth = new NetworkVariable<float>(100f);
        protected readonly NetworkVariable<float> netMaxHealth = new NetworkVariable<float>(100f);
        protected readonly NetworkVariable<bool> netDead = new NetworkVariable<bool>(false);
        protected readonly NetworkVariable<float> netLocoSpeed = new NetworkVariable<float>(0f);
        /// <summary>-1 = walking backwards (retreat), 1 = forward.</summary>
        protected readonly NetworkVariable<sbyte> netLocoMul = new NetworkVariable<sbyte>(1);
        protected readonly NetworkVariable<FixedString32Bytes> netName = new NetworkVariable<FixedString32Bytes>();
        /// <summary>Bard's Dirge of Ruin: server time when the curse ends.</summary>
        readonly NetworkVariable<double> _netCurseEnd = new NetworkVariable<double>(0);
        public const float CurseDamageMul = 1.3f, CurseSlowMul = 0.7f;
        double NetNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        public bool IsCursed => IsAlive && NetNow < _netCurseEnd.Value;
        public float CurseRemaining => Mathf.Max(0f, (float)(_netCurseEnd.Value - NetNow));
        GameObject _curseFx;

        public bool IsAlive => !netDead.Value;
        public float Health01 => netMaxHealth.Value > 0 ? Mathf.Clamp01(netHealth.Value / netMaxHealth.Value) : 0f;
        public string DisplayName => netName.Value.Length > 0 ? netName.Value.ToString() : displayName;
        public bool IsPlayer => false;
        public float LastDamagedTime { get; private set; } = -100f;

        protected CharacterController cc;
        protected float poise;
        protected float maxPoise;
        protected float poiseRegenTimer;
        protected PlayerController target;
        protected Vector3 homePosition;
        protected Quaternion homeRotation;
        float _vy;

        // ---- attacco in corso (server)
        protected AttackData currentAttack;
        protected HitShape currentShape;
        protected float attackClock;
        protected float shapeRange, shapeAngle;
        protected HitFlags currentFlags;
        bool _shapeResolved;
        Vector3 _knockVel;
        /// <summary>Pushes the enemy (server): used for flinches and evasive hops.</summary>
        protected void Knock(Vector3 velocity) => _knockVel = velocity;
        protected bool IsBeingKnocked => _knockVel.sqrMagnitude > 0.25f;
        float _stanceUntil = -10f, _riposteUntil = -10f;
        /// <summary>Recent hits taken (decays): AIs use it to punish button mashing.</summary>
        protected float pressure;
        bool _hitboxOn;
        readonly HashSet<ulong> _hitThisAttack = new HashSet<ulong>();

        protected virtual void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (anim == null) anim = GetComponentInChildren<CharacterAnimator>();
            // enemy blades leave a red arc: danger is readable at a glance
            if (anim && anim.weaponTrail) anim.weaponTrail.color = TrailColor;
        }

        protected virtual Color TrailColor => new Color(1f, 0.32f, 0.18f, 0.9f);

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Layers.SetLayerRecursive(gameObject, Layers.Character);
            homePosition = transform.position;
            homeRotation = transform.rotation;
            if (weapon != null) { weapon.ownerRoot = transform; weapon.OnHit += OnWeaponHitServer; }
            if (IsServer)
            {
                netName.Value = new FixedString32Bytes(displayName);
                SetupStats(1);
            }
            netDead.OnValueChanged += OnDeadChanged;
            if (netDead.Value) anim?.Play(CharacterAnimator.Death, 0f, 5f);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            netDead.OnValueChanged -= OnDeadChanged;
            if (weapon != null) weapon.OnHit -= OnWeaponHitServer;
        }

        void OnDeadChanged(bool prev, bool now)
        {
            var lot = GetComponent<LockOnTarget>();
            if (lot) lot.enabled = !now;
            if (cc) cc.enabled = !now;
        }

        /// <summary>Imposta vita e postura massime (il boss le scala col numero di giocatori).</summary>
        protected void SetupStats(float multiplier)
        {
            netMaxHealth.Value = baseHealth * multiplier;
            netHealth.Value = netMaxHealth.Value;
            maxPoise = basePoise * multiplier;
            poise = maxPoise;
        }

        protected virtual void Update()
        {
            if (!IsSpawned) return;
            if (anim) anim.SetLocomotion(netLocoSpeed.Value, netLocoMul.Value);
            // curse visual (everyone): purple wisps and falling notes around the victim
            bool cursed = IsCursed;
            if (cursed && _curseFx == null) _curseFx = SpellFx.CurseAura(transform, CurseHeight);
            else if (!cursed && _curseFx != null) { SpellFx.Release(_curseFx); _curseFx = null; }
            if (!IsServer) return;
            if (netDead.Value) return;

            float dt = Time.deltaTime;
            if (poiseRegenTimer > 0f) poiseRegenTimer -= dt;
            else poise = Mathf.Min(maxPoise, poise + poiseRegenPerSecond * dt);

            pressure = Mathf.Max(0f, pressure - dt * 0.55f);
            if (_knockVel.sqrMagnitude > 0.0004f && cc.enabled)
            {
                cc.Move(_knockVel * dt);
                _knockVel = Vector3.MoveTowards(_knockVel, Vector3.zero, 16f * dt);
            }
            ServerTick(dt);
            if (currentAttack != null) UpdateAttackServer(dt);
            ApplyGravity(dt);
        }

        protected abstract void ServerTick(float dt);

        // ================================================================== movimento (server)
        protected Vector3 _moveVelocity;

        protected void MoveTowards(Vector3 worldDir, float speed, float dt)
        {
            Vector3 d = MathUtil.Flat(worldDir);
            if (d.sqrMagnitude > 0.0001f) d.Normalize();
            // evitamento semplice degli ostacoli: se davanti c'è un muro devia a lato
            if (d.sqrMagnitude > 0.0001f && Physics.SphereCast(transform.position + Vector3.up * 0.8f, cc.radius * 0.8f, d, out var hit, 1.2f + cc.radius, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                d = Vector3.Slerp(d, Vector3.Cross(Vector3.up, hit.normal) * Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, hit.normal), d) + 0.01f), 0.7f).normalized;
            if (IsCursed) speed *= CurseSlowMul;
            _moveVelocity = Vector3.MoveTowards(_moveVelocity, d * speed, 12f * dt);
            cc.Move(_moveVelocity * dt);
            netLocoSpeed.Value = MathUtil.Flat(_moveVelocity).magnitude;
            sbyte mul = (sbyte)(Vector3.Dot(MathUtil.Flat(_moveVelocity), transform.forward) < -0.3f * MathUtil.Flat(_moveVelocity).magnitude ? -1 : 1);
            if (netLocoMul.Value != mul) netLocoMul.Value = mul;
        }

        protected void StopMoving(float dt)
        {
            _moveVelocity = Vector3.MoveTowards(_moveVelocity, Vector3.zero, 14f * dt);
            if (_moveVelocity.sqrMagnitude > 0.001f) cc.Move(_moveVelocity * dt);
            if (netLocoSpeed.Value != 0f) netLocoSpeed.Value = currentAttack == null ? MathUtil.Flat(_moveVelocity).magnitude : 0f;
        }

        protected void FaceTowards(Vector3 worldPos, float dt, float speedMul = 1f)
        {
            Vector3 d = MathUtil.Flat(worldPos - transform.position);
            if (d.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), turnSpeed * speedMul * dt);
        }

        void ApplyGravity(float dt)
        {
            if (!cc.enabled) return;
            if (cc.isGrounded && _vy < 0) _vy = -2f;
            _vy -= gravity * dt;
            cc.Move(Vector3.up * _vy * dt);
        }

        // ================================================================== bersagli
        protected PlayerController FindNearestPlayer(float range, bool requireLos = true)
        {
            PlayerController best = null;
            float bestD = range;
            foreach (var p in PlayerController.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead) continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                // sneaking (crouched) players are noticed only from much closer, unless already fighting us
                if (p.Crouched && target != p) d /= 0.5f;
                if (d > bestD) continue;
                if (requireLos && Physics.Linecast(transform.position + Vector3.up * 1.5f, p.transform.position + Vector3.up * 1.2f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                bestD = d; best = p;
            }
            return best;
        }

        protected float DistanceToTarget => target ? Vector3.Distance(transform.position, target.transform.position) : float.MaxValue;

        // ================================================================== attacchi (server)
        protected void StartAttack(AttackData data, HitShape shape, float range = 2.5f, float halfAngle = 70f, HitFlags extraFlags = HitFlags.None)
        {
            currentAttack = data;
            currentShape = shape;
            shapeRange = range;
            shapeAngle = halfAngle;
            attackClock = 0f;
            _shapeResolved = false;
            _hitThisAttack.Clear();
            currentFlags = extraFlags;
            if (data.parryable) currentFlags |= HitFlags.Parryable;
            if (data.unblockable) currentFlags |= HitFlags.Unblockable;
            netLocoSpeed.Value = 0f;
            _attackStartReal = Time.time;
            // guaranteed readable wind-up: clips whose blade moves almost immediately are held in
            // their ready pose first (frozen, tracking the player), then released at full speed
            float lead = (data.hitStart - data.startTime) / Mathf.Max(0.05f, data.animSpeed);
            _preHold = Mathf.Max(0f, MinWindup - lead);
            PlayAnimRpc(StateIndex(data.state), 0.14f, _preHold > 0f ? 0.035f : data.animSpeed, data.startTime);
            if (Random.value < GruntChance) GruntRpc();
            // wind-up glint on the blade ~0.38 s before it connects (gold = parryable, red = not)
            TelegraphRpc((byte)(data.parryable && (extraFlags & HitFlags.Unblockable) == 0 ? 0 : 1), Mathf.Max(0f, lead + _preHold - 0.38f));
        }

        float _attackStartReal;
        float _preHold;
        /// <summary>Minimum real seconds between the start of an attack and its contact.</summary>
        protected virtual float MinWindup => 0.4f;
        protected bool InPreHold => _preHold > 0f;
        protected virtual float GruntChance => 0.3f;

        /// <summary>Attack clip time from the Animator (the same clock that drives what players see).</summary>
        protected float AttackClipTime()
        {
            float t = anim ? anim.GetStateTime(currentAttack.state) : -1f;
            if (t >= 0f) return t;
            return currentAttack.startTime + (Time.time - _attackStartReal) * currentAttack.animSpeed;
        }

        protected bool IsAttacking => currentAttack != null;
        /// <summary>How hard the enemy turns toward its target during the wind-up (per attack).</summary>
        protected float trackingMul = 0.6f;

        /// <summary>Changes the animation speed on every client (delayed swings: hold, then release).</summary>
        [Rpc(SendTo.Everyone)]
        protected void SetAnimSpeedRpc(float speed) { if (anim) anim.SetPlaybackSpeed(speed); }

        /// <summary>Blade glint right now (used when a swing is held at the top).</summary>
        protected void TelegraphNow(bool parryable) => TelegraphRpc((byte)(parryable ? 0 : 1), 0f);

        void UpdateAttackServer(float dt)
        {
            if (_preHold > 0f)
            {
                _preHold -= dt;
                if (_preHold <= 0f) SetAnimSpeedRpc(currentAttack.animSpeed);
            }
            attackClock = AttackClipTime();
            float t = attackClock;

            // tracking toward the target until the blade starts its arc
            if (target && t < currentAttack.hitStart - 0.05f) FaceTowards(target.transform.position, dt, trackingMul);

            // affondo
            if (t >= currentAttack.lungeStart && t <= currentAttack.lungeEnd && currentAttack.lunge > 0f)
            {
                // real playback speed: a held (delayed) swing must not keep sliding forward
                float playSpeed = anim ? anim.PlaybackSpeed : currentAttack.animSpeed;
                float v = currentAttack.lunge / Mathf.Max(0.01f, currentAttack.lungeEnd - currentAttack.lungeStart) * playSpeed;
                if (!target || DistanceToTarget > cc.radius + 1.0f) cc.Move(transform.forward * v * dt);
            }

            bool active = t >= currentAttack.hitStart && t <= currentAttack.hitEnd;
            if (currentShape == HitShape.Weapon && weapon != null)
            {
                if (active && !_hitboxOn) { weapon.BeginSwing(); _hitboxOn = true; }
                if (!active && _hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
                // ripiego: se l'arma non ha toccato nessuno, controlla un piccolo cono frontale a fine finestra
                if (!active && t > currentAttack.hitEnd && !_shapeResolved) { _shapeResolved = true; if (_hitThisAttack.Count == 0) ResolveShape(HitShape.FrontArc, shapeRange * 0.8f, 45f); }
            }
            else if (!_shapeResolved && t >= currentAttack.swingPeak && t <= currentAttack.hitEnd + 0.1f)
            {
                _shapeResolved = true;
                if (currentShape != HitShape.Projectile) ResolveShape(currentShape, shapeRange, shapeAngle);
                OnAttackImpact(currentAttack);
            }

            if (t >= currentAttack.endTime)
            {
                if (_hitboxOn) { weapon.EndSwing(); _hitboxOn = false; }
                var finished = currentAttack;
                currentAttack = null;
                OnAttackFinished(finished);
            }
        }

        /// <summary>Hook: momento d'impatto di un attacco a forma (per VFX, onde d'urto…).</summary>
        protected virtual void OnAttackImpact(AttackData a) { }
        protected virtual void OnAttackFinished(AttackData a) { }

        protected void CancelAttack()
        {
            _preHold = 0f;
            if (_hitboxOn && weapon) { weapon.EndSwing(); _hitboxOn = false; }
            currentAttack = null;
        }

        protected void ResolveShape(HitShape shape, float range, float halfAngle)
        {
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead) continue;
                Vector3 to = p.transform.position - transform.position;
                if (Mathf.Abs(to.y) > 3f) continue;
                float d = MathUtil.Flat(to).magnitude;
                if (d > range + 0.4f) continue;
                if (shape == HitShape.FrontArc && d > 0.6f && Vector3.Angle(MathUtil.Flat(transform.forward), MathUtil.Flat(to)) > halfAngle) continue;
                DeliverHit(p);
            }
        }

        void OnWeaponHitServer(Collider col, Vector3 point)
        {
            if (!IsServer || currentAttack == null) return;
            var p = col.GetComponentInParent<PlayerController>();
            if (p != null) DeliverHit(p);
        }

        protected void DeliverHit(PlayerController p, float damageOverride = -1f, HitFlags flagsOverride = HitFlags.None, bool ignoreOncePerAttack = false)
        {
            if (p == null || p.IsDead) return;
            if (!ignoreOncePerAttack && !_hitThisAttack.Add(p.NetworkObjectId)) return;
            float dmg = (damageOverride >= 0 ? damageOverride : currentAttack != null ? currentAttack.damage : 10f) * DamageScale;
            float poiseDmg = currentAttack != null ? currentAttack.poiseDamage : 10f;
            HitFlags f = flagsOverride != HitFlags.None ? flagsOverride : currentFlags;
            p.ReceiveHitRpc(dmg, poiseDmg, transform.position, (byte)f, new NetworkObjectReference(NetworkObject));
        }

        /// <summary>Server: a projectile of this enemy (arrow, fireball) reached a player.</summary>
        /// <summary>Height of the curse aura (bosses override).</summary>
        protected virtual float CurseHeight => cc ? cc.height : 2f;

        /// <summary>Bard's Dirge of Ruin: takes more damage and moves slower for a while.</summary>
        [Rpc(SendTo.Server)]
        public void CurseRpc(float duration)
        {
            if (!IsAlive) return;
            _netCurseEnd.Value = NetNow + duration;
            Debug.Log($"[DF] {DisplayName} cursed for {duration:0}s");
        }

        public void DeliverProjectileHit(PlayerController p, float damage, float poiseDamage, HitFlags flags)
        {
            if (!IsServer || p == null || p.IsDead) return;
            p.ReceiveHitRpc(damage * DamageScale, poiseDamage, transform.position, (byte)flags, new NetworkObjectReference(NetworkObject));
        }

        // ================================================================== danni ricevuti
        /// <summary>
        /// Called by the attacking client on the exact frame its blade connects: plays the
        /// reaction locally (no network delay). The server sends the same FX to everyone else.
        /// </summary>
        public void LocalHitReact(Vector3 point, bool heavy, Vector3 attackerPos, Vector3 swingDir)
        {
            if (IsInvulnerableVisual) { Fx.Spawn(FxId.BlockSparks, point); Sfx.Play(SfxId.HitArmor, point); return; }
            Fx.Spawn(heavy ? FxId.SlashImpactHeavy : FxId.SlashImpact, point, swingDir);
            PlayHitFx(point, heavy ? (byte)2 : (byte)0);
        }

        protected virtual bool IsInvulnerableVisual => false;

        [Rpc(SendTo.Server)]
        public void TakeHitRpc(float damage, float poiseDamage, Vector3 hitPoint, Vector3 attackerPos, float knockback, RpcParams rpcParams = default)
        {
            if (Vector3.Distance(attackerPos, transform.position) > 9f + cc.radius * 2f) return; // basic anti-cheat
            ServerTakeHit(damage, poiseDamage, hitPoint, attackerPos, knockback, rpcParams.Receive.SenderClientId);
        }

        /// <summary>Arrows: same as a melee hit, but the shooter may be far away.</summary>
        [Rpc(SendTo.Server)]
        public void TakeRangedHitRpc(float damage, float poiseDamage, Vector3 hitPoint, Vector3 attackerPos, float knockback, RpcParams rpcParams = default)
        {
            if (Vector3.Distance(hitPoint, transform.position) > 2.5f + cc.height) return; // basic anti-cheat: the arrow must touch us
            ulong sender = rpcParams.Receive.SenderClientId;
            if (NetworkManager.ConnectedClients.TryGetValue(sender, out var cl) && cl.PlayerObject)
            { LastRangedAttacker = cl.PlayerObject.GetComponent<PlayerController>(); LastRangedFrame = Time.frameCount; }
            ServerTakeHit(damage, poiseDamage, hitPoint, attackerPos, knockback, sender);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Development: kills this enemy through the normal damage path (server).</summary>
        public void DevKill(string _) { if (IsServer) ServerTakeHit(99999f, 0f, transform.position + Vector3.up, transform.position + transform.forward * 3f, 0f, NetworkManager.ServerClientId); }
#endif

        /// <summary>Server: the player who last hit this enemy with an arrow (guardians go after them).</summary>
        protected PlayerController LastRangedAttacker { get; private set; }
        protected int LastRangedFrame { get; private set; } = -1;
        /// <summary>True inside OnDamaged when the current hit is an arrow.</summary>
        protected bool HitIsRanged => LastRangedFrame == Time.frameCount && LastRangedAttacker != null;

        /// <summary>Server: the scoreboard kill goes to the player who landed the last blow.</summary>
        void CreditKill(ulong sender)
        {
            foreach (var p in PlayerController.All)
                if (p && p.IsSpawned && p.OwnerClientId == sender && p.vitals) { p.vitals.ServerAddKill(); return; }
        }

        void ServerTakeHit(float damage, float poiseDamage, Vector3 hitPoint, Vector3 attackerPos, float knockback, ulong sender)
        {
            if (netDead.Value) return;
            var others = RpcTarget.Not(sender, RpcTargetUse.Temp);
            if (IsInvulnerable) { HitFxRpc(hitPoint, 1, others); return; }
            bool staggered = IsStaggered;
            bool riposte = Time.time < _riposteUntil;
            if (riposte) _riposteUntil = -10f;   // one critical hit per parry
            float dmg = damage * (riposte ? staggeredDamageMultiplier : 1f) * DamageTakenMultiplier * (IsCursed ? CurseDamageMul : 1f);
            netHealth.Value = Mathf.Max(0f, netHealth.Value - dmg);
            bool armored = currentAttack != null && (currentAttack.hyperArmor || attackClock >= currentAttack.hitStart - 0.2f);
            bool stance = Time.time < _stanceUntil;
            if (!stance && !staggered) poise -= poiseDamage * (currentAttack != null && currentAttack.hyperArmor ? 0f : armored ? 0.5f : 1f);
            poiseRegenTimer = poiseRegenDelay;
            pressure += 1f;

            // knockback: pushed away from the attacker (less while swinging, more when stunned)
            Vector3 away = MathUtil.Flat(transform.position - attackerPos);
            away = away.sqrMagnitude > 0.001f ? away.normalized : -transform.forward;
            float kb = knockback * knockbackFactor * (staggered ? 1.3f : armored ? 0.35f : 1f);
            _knockVel = away * kb;

            HitFxRpc(hitPoint, riposte ? (byte)3 : knockback > 4f ? (byte)2 : (byte)0, others);
            Debug.Log($"[DF] {DisplayName} hit: -{dmg:0} HP ({netHealth.Value:0}/{netMaxHealth.Value:0} left){(riposte ? " [RIPOSTE]" : "")} poise {poise:0}/{maxPoise:0}{(stance ? " [stance]" : "")}");
            OnDamaged(attackerPos);
            OnHitReceived(dmg, knockback, away);
            if (netHealth.Value <= 0f) { _knockVel *= 1.6f; CreditKill(sender); Die(); return; }
            if (poise <= 0f && !staggered)
            {
                poise = maxPoise;
                _stanceUntil = Time.time + staggerDuration + stanceImmunity;
                _knockVel = away * Mathf.Max(kb, 5f * knockbackFactor);
                Stagger(staggerDuration);
                StaggerFxRpc();
                Debug.Log($"[DF] {DisplayName} staggered (poise broken)");
            }
        }

        [Rpc(SendTo.Server)]
        public void ParriedRpc()
        {
            if (netDead.Value) return;
            Debug.Log($"[DF] {DisplayName} was PARRIED");
            poise = maxPoise;
            _riposteUntil = Time.time + parryStaggerDuration;
            _stanceUntil = -10f;
            Stagger(parryStaggerDuration);
        }

        [Rpc(SendTo.Everyone)]
        void StaggerFxRpc()
        {
            Vector3 p = transform.position + Vector3.up * 1.4f * transform.localScale.y;
            Fx.Spawn(FxId.ParryFlash, p);
            Sfx.Play(SfxId.GuardBreak, p, 0.9f, GruntPitch);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void HitFxRpc(Vector3 point, byte kind, RpcParams rpcParams = default)
        {
            if (kind == 1) { Fx.Spawn(FxId.BlockSparks, point); Sfx.Play(SfxId.HitArmor, point); return; }
            Fx.Spawn(kind >= 2 ? FxId.SlashImpactHeavy : FxId.SlashImpact, point, Vector3.Cross(Vector3.up, point - transform.position));
            PlayHitFx(point, kind);
        }

        /// <summary>Gets a hit-react hitstop short enough not to stall the enemy's own swing.</summary>
        float ReactStop(bool heavy) => anim != null && anim.IsInAttackState ? 0.025f : heavy ? 0.1f : 0.07f;

        void PlayHitFx(Vector3 point, byte kind)
        {
            LastDamagedTime = Time.time;
            bool heavy = kind >= 2;
            Fx.Spawn(FxId.Blood, point);
            Fx.Spawn(FxId.HitSparks, point);
            if (kind == 3) { Fx.Spawn(FxId.ParryFlash, point); Sfx.Play(SfxId.Parry, point, 0.8f, 0.8f); }
            Sfx.Play(SfxId.HitFlesh, point, heavy ? 1.25f : 1f, heavy ? 0.82f : 1f);
            if (anim) anim.HitReact(ReactStop(heavy));
        }

        [Rpc(SendTo.Everyone)]
        void TelegraphRpc(byte kind, float delay) => StartCoroutine(Telegraph(kind, delay));

        System.Collections.IEnumerator Telegraph(byte kind, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (!IsAlive) yield break;
            Transform tip = weapon && weapon.tipPoint ? weapon.tipPoint : anim && anim.weaponTrail ? anim.weaponTrail.tipPoint : null;
            Vector3 pos = tip ? Vector3.Lerp(weapon ? weapon.basePoint.position : tip.position, tip.position, 0.7f) : transform.position + Vector3.up * 2f * transform.localScale.y;
            Fx.SpawnAttached(kind == 0 ? FxId.GlintParry : FxId.GlintDanger, tip, pos, transform.localScale.y);
        }

        [Rpc(SendTo.Everyone)]
        void GruntRpc() => Sfx.Play(SfxId.EnemyGrunt, transform.position + Vector3.up * 1.6f * transform.localScale.y, 0.7f, GruntPitch);

        protected virtual float GruntPitch => 1f;
        /// <summary>Global damage multiplier of this enemy (difficulty tuning).</summary>
        protected virtual float DamageScale => 1f;

        protected virtual bool IsInvulnerable => false;
        protected virtual float DamageTakenMultiplier => 1f;
        public abstract bool IsStaggered { get; }
        protected abstract void Stagger(float duration);
        protected virtual void OnDamaged(Vector3 attackerPos) { }
        /// <summary>Server: every landed hit (damage dealt, knockback strength of the attack, push direction).</summary>
        protected virtual void OnHitReceived(float damage, float knockback, Vector3 away) { }

        protected virtual void Die()
        {
            Debug.Log($"[DF] {DisplayName} defeated");
            CancelAttack();
            netDead.Value = true;
            netLocoSpeed.Value = 0f;
            PlayAnimRpc(StateIndex(CharacterAnimator.Death), 0.1f, 0.9f);
            DeathFxRpc();
            SoulsRpc(SoulReward, IsBossReward);
        }

        /// <summary>Souls given to every player of the party when this enemy dies.</summary>
        protected virtual int SoulReward => Mathf.Clamp(Mathf.RoundToInt(netMaxHealth.Value * 0.3f), 12, 300);
        protected virtual bool IsBossReward => false;

        [Rpc(SendTo.Everyone)]
        void SoulsRpc(int amount, bool boss)
        {
            var me = PlayerController.Local;
            if (me == null) return;
            PlayerSkills.AddSouls(amount, boss);
            SoulWisp.Spawn(transform.position + Vector3.up * 1.2f, me.transform, new Color(0.85f, 0.75f, 1f));
            // a draught now and then (each player rolls their own loot)
            float chance = boss ? 1f : 0.12f;
            int n = boss ? 2 : 1;
            for (int i = 0; i < n; i++)
                if (Random.value < chance) Potions.Add((Potions.Kind)Random.Range(0, Potions.KindCount));
        }

        [Rpc(SendTo.Everyone)]
        void DeathFxRpc()
        {
            Sfx.Play(SfxId.EnemyDeath, transform.position + Vector3.up, 1f, GruntPitch * 0.85f);
            Sfx.Play(SfxId.Death, transform.position, 0.9f, GruntPitch);
            Fx.Spawn(FxId.Dust, transform.position);
            if (SoulsCamera.Instance && SoulsCamera.Instance.CurrentTarget && SoulsCamera.Instance.CurrentTarget.gameObject == gameObject)
                SoulsCamera.Instance.ClearLock();
        }

        /// <summary>Riporta il nemico allo stato iniziale (usato dal reset del boss).</summary>
        protected void ResetToHome(float statMultiplier)
        {
            CancelAttack();
            cc.enabled = false;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            cc.enabled = true;
            netDead.Value = false;
            SetupStats(statMultiplier);
            netLocoSpeed.Value = 0f;
            target = null;
            PlayAnimRpc(StateIndex(CharacterAnimator.Locomotion), 0.1f, 1f);
        }

        // ================================================================== animazioni in rete
        static readonly string[] States =
        {
            CharacterAnimator.Locomotion, CharacterAnimator.Attack1, CharacterAnimator.Attack2,
            CharacterAnimator.Heavy, CharacterAnimator.Spin, CharacterAnimator.Parry, CharacterAnimator.Hurt,
            CharacterAnimator.Knockdown, CharacterAnimator.Death, CharacterAnimator.Taunt
        };

        protected static byte StateIndex(string s)
        {
            for (int i = 0; i < States.Length; i++) if (States[i] == s) return (byte)i;
            return 0;
        }

        [Rpc(SendTo.Everyone)]
        protected void PlayAnimRpc(byte stateIndex, float fade, float speed, float startTime = 0f)
        {
            if (anim == null) return;
            string s = States[Mathf.Clamp(stateIndex, 0, States.Length - 1)];
            if (s == CharacterAnimator.Locomotion) anim.ReturnToLocomotion(fade);
            else anim.Play(s, fade, speed, startTime);
        }
    }
}
