using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Nemico minore: "Guardiano Corrotto". State machine semplice (server):
    /// Idle (sentinella che si guarda intorno) → Chase → Attack → Recover → Chase …
    /// Stagger quando la postura si azzera o dopo un parry; Return se il giocatore si allontana troppo.
    /// Attacca con la hitbox reale della spada (HitShape.Weapon) e un colpo di scudo.
    /// </summary>
    public class GuardianAI : EnemyBase
    {
        enum State { Idle, Chase, Attack, Recover, Staggered, Return, Dead }

        [Header("IA")]
        public float aggroRange = 13f;
        public float leashRange = 28f;
        public float attackRange = 2.3f;
        public Vector2 recoverTime = new Vector2(1.4f, 2.5f);
        [Tooltip("Hits taken in quick succession before the guardian answers with an unflinching counter.")]
        public float retaliateAfter = 2.6f;
        public float retaliateCooldown = 3.5f;

        [Header("Attacchi")]
        public AttackData slash = new AttackData { name = "Slash", state = CharacterAnimator.Attack1, animSpeed = 0.95f, damage = 12, poiseDamage = 20, lunge = 0.8f };
        public AttackData sweep = new AttackData { name = "Sweep", state = CharacterAnimator.Attack2, animSpeed = 0.9f, damage = 14, poiseDamage = 22, lunge = 0.6f };
        public AttackData overhead = new AttackData { name = "Overhead", state = CharacterAnimator.Heavy, animSpeed = 0.8f, damage = 21, poiseDamage = 45, lunge = 1.0f, parryable = true };

        State _state;
        float _stateTimer;
        float _idleLookTimer;
        int _comboStep;
        static readonly System.Collections.Generic.Dictionary<ulong, float> LastSwingAt = new System.Collections.Generic.Dictionary<ulong, float>();
        float _nextRetaliate;

        public override bool IsStaggered => _state == State.Staggered;
        protected override float MinWindup => 0.5f;

        protected override void ServerTick(float dt)
        {
            _stateTimer -= dt;
            switch (_state)
            {
                case State.Idle:
                    StopMoving(dt);
                    _idleLookTimer -= dt;
                    if (_idleLookTimer <= 0f)
                    {
                        _idleLookTimer = Random.Range(3f, 6f);
                        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y + Random.Range(-50f, 50f), 0);
                    }
                    target = FindNearestPlayer(aggroRange);
                    if (target) _state = State.Chase;
                    break;

                case State.Chase:
                    if (!ValidTarget()) { _state = State.Return; break; }
                    if (Vector3.Distance(transform.position, homePosition) > leashRange) { _state = State.Return; target = null; break; }
                    FaceTowards(target.transform.position, dt);
                    if (DistanceToTarget <= attackRange)
                    {
                        StopMoving(dt);
                        // take turns: if another guardian just swung at this player, circle first
                        ulong id = target.NetworkObjectId;
                        if (LastSwingAt.TryGetValue(id, out float last) && Time.time - last < 1.3f) { _state = State.Recover; _stateTimer = Random.Range(0.5f, 1f); break; }
                        LastSwingAt[id] = Time.time;
                        ChooseAttack();
                    }
                    else MoveTowards(target.transform.position - transform.position, DistanceToTarget > 6f ? chaseSpeed : walkSpeed * 1.3f, dt);
                    break;

                case State.Attack:
                    StopMoving(dt);
                    break;

                case State.Recover:
                    // si muove lateralmente guardando il bersaglio (circling)
                    if (!ValidTarget()) { _state = State.Return; break; }
                    FaceTowards(target.transform.position, dt);
                    Vector3 side = Vector3.Cross(Vector3.up, target.transform.position - transform.position).normalized * (_comboStep % 2 == 0 ? 1 : -1);
                    MoveTowards(side, walkSpeed * 0.7f, dt);
                    if (_stateTimer <= 0f) _state = State.Chase;
                    break;

                case State.Staggered:
                    StopMoving(dt);
                    if (_stateTimer <= 0f) { _state = State.Recover; _stateTimer = 0.4f; PlayAnimRpc(StateIndex(CharacterAnimator.Locomotion), 0.2f, 1f); }
                    break;

                case State.Return:
                    target = FindNearestPlayer(aggroRange * 0.7f);
                    if (target && Vector3.Distance(transform.position, homePosition) < leashRange * 0.8f) { _state = State.Chase; break; }
                    Vector3 toHome = homePosition - transform.position;
                    if (MathUtil.Flat(toHome).magnitude < 0.8f) { _state = State.Idle; StopMoving(dt); break; }
                    FaceTowards(homePosition, dt);
                    MoveTowards(toHome, walkSpeed, dt);
                    break;
            }
        }

        bool ValidTarget() => target != null && target.IsSpawned && !target.IsDead && DistanceToTarget < leashRange;

        void ChooseAttack()
        {
            _state = State.Attack;
            _comboStep++;
            float r = Random.value;
            if (r < 0.45f) StartAttack(slash, HitShape.Weapon, 2.6f, 60f);
            else if (r < 0.8f) StartAttack(sweep, HitShape.Weapon, 2.6f, 70f);
            else StartAttack(overhead, HitShape.Weapon, 2.6f, 45f, HitFlags.Heavy);
        }

        protected override void OnAttackFinished(AttackData a)
        {
            // a volte concatena un secondo colpo
            if (_state == State.Attack && a == slash && Random.value < 0.25f && ValidTarget() && DistanceToTarget < attackRange + 0.6f)
            {
                StartAttack(sweep, HitShape.Weapon, 2.6f, 70f);
                return;
            }
            if (_state == State.Attack)
            {
                _state = State.Recover;
                _stateTimer = Random.Range(recoverTime.x, recoverTime.y);
                PlayAnimRpc(StateIndex(CharacterAnimator.Locomotion), 0.25f, 1f);
            }
        }

        protected override void Stagger(float duration)
        {
            CancelAttack();
            _state = State.Staggered;
            _stateTimer = duration;
            PlayAnimRpc(StateIndex(CharacterAnimator.Hurt), 0.05f, 0.55f);
        }

        protected override void OnDamaged(Vector3 attackerPos)
        {
            // go after whoever hurt us (an archer shooting from afar, not just the closest knight)
            var shooter = LastRangedAttacker;
            bool fromAfar = HitIsRanged && !shooter.IsDead;
            if (_state == State.Idle || _state == State.Return)
            {
                target = fromAfar && Vector3.Distance(shooter.transform.position, homePosition) < leashRange ? shooter : FindNearestPlayer(leashRange, false);
                if (target) _state = State.Chase;
            }
            else if (fromAfar && target != shooter && (!target || DistanceToTarget > attackRange + 2f) && Vector3.Distance(shooter.transform.position, homePosition) < leashRange)
                target = shooter;
            // button-mashing gets punished: after a few quick hits the guardian swings back
            // through your combo with hyper armour (gold glint: parry it, or roll away).
            // Only in sword range: arrows from afar don't trigger a swing at thin air.
            if ((_state == State.Chase || _state == State.Recover) && pressure >= retaliateAfter && Time.time >= _nextRetaliate && target
                && Vector3.Distance(attackerPos, transform.position) < attackRange + 1.5f)
            {
                _nextRetaliate = Time.time + retaliateCooldown;
                pressure = 0f;
                transform.rotation = Quaternion.LookRotation(MathUtil.Flat(attackerPos - transform.position).normalized + transform.forward * 0.01f);
                var r = sweep.Clone();
                r.name = "Retaliation"; r.hyperArmor = true; r.animSpeed = 1.05f; r.damage = 22; r.poiseDamage = 40;
                _state = State.Attack;
                StartAttack(r, HitShape.Weapon, 2.6f, 70f);
                Debug.Log($"[DF] {DisplayName} retaliates");
            }
        }

        protected override void Die()
        {
            _state = State.Dead;
            base.Die();
        }
    }
}
