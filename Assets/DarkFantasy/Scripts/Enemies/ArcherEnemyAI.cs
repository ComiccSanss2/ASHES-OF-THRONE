using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// "Hollow Bowman": an enemy archer of the castle prisons (same rig, bow and clips as the
    /// player archer). Server-side state machine:
    ///   Idle (sentry) → Engage: keeps the target between preferMin and preferMax metres, with a
    ///   clear line of sight, strafing a little; shoots on a cadence (red glint ~0.4 s before the
    ///   release: roll through it or block it) → Retreat when a knight closes in (backs off, then
    ///   shoots point-blank).
    /// The arrow leaves on the clip's release frame (HitShape.Projectile → OnAttackImpact); every
    /// client flies the same arrow, the server's copy deals the damage (EnemyBase.DeliverProjectileHit).
    /// Shots lead a moving target a little.
    /// </summary>
    public class ArcherEnemyAI : EnemyBase
    {
        enum State { Idle, Engage, Shoot, Retreat, Staggered, Return, Dead }

        [Header("Arciere")]
        public ArcherBow bow;
        public float aggroRange = 20f;
        public float leashRange = 32f;
        public float preferMin = 7f;
        public float preferMax = 15f;
        public float fleeRange = 4f;
        public float arrowSpeed = 32f;
        public float arrowDamage = 15f;
        public float arrowPoise = 18f;
        public Vector2 shotInterval = new Vector2(1.7f, 3.2f);
        [Tooltip("Fraction of the target's velocity the shot leads.")]
        public float leadFactor = 0.6f;

        public AttackData shot = new AttackData
        {
            name = "Bow Shot", state = CharacterAnimator.Attack1, animSpeed = 0.78f, startTime = 0f,
            hitStart = 0.5f, hitEnd = 0.5f, swingPeak = 0.5f, endTime = 0.86f, damage = 15, poiseDamage = 18
        };

        State _state;
        float _stateTimer, _nextShot, _idleLook, _strafeSign = 1f, _retreatCooldown;
        readonly Dictionary<PlayerController, (Vector3 pos, Vector3 vel)> _track = new Dictionary<PlayerController, (Vector3, Vector3)>();

        public override bool IsStaggered => _state == State.Staggered;
        protected override float MinWindup => 0.55f;
        protected override Color TrailColor => new Color(1f, 0.32f, 0.18f, 0.9f);

        protected override void ServerTick(float dt)
        {
            _stateTimer -= dt;
            TrackTarget(dt);
            switch (_state)
            {
                case State.Idle:
                    StopMoving(dt);
                    _idleLook -= dt;
                    if (_idleLook <= 0f) { _idleLook = Random.Range(3f, 6f); transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y + Random.Range(-60f, 60f), 0); }
                    target = FindNearestPlayer(aggroRange);
                    if (target) Engage(Random.Range(0.5f, 1.2f));
                    break;

                case State.Engage:
                {
                    if (!ValidTarget()) { _state = State.Return; break; }
                    FaceTowards(target.transform.position, dt, 1.3f);
                    float d = DistanceToTarget;
                    bool los = HasLineOfSight();
                    Vector3 to = MathUtil.Flat(target.transform.position - transform.position).normalized;
                    if (d < fleeRange && Time.time > _retreatCooldown)
                    {
                        _state = State.Retreat; _stateTimer = Random.Range(1.1f, 1.6f);
                        _retreatCooldown = Time.time + 4f;
                        break;
                    }
                    if (!los || d > preferMax) MoveTowards(to, d > preferMax + 6f ? chaseSpeed : walkSpeed * 1.3f, dt);
                    else if (d < preferMin) MoveTowards(-to, walkSpeed * 0.9f, dt);
                    else
                    {
                        if (Random.value < dt * 0.4f) _strafeSign = -_strafeSign;
                        MoveTowards(Vector3.Cross(Vector3.up, to) * _strafeSign, walkSpeed * 0.45f, dt);
                    }
                    if (Time.time >= _nextShot && los && d < preferMax + 5f) BeginShot();
                    break;
                }

                case State.Shoot:
                    StopMoving(dt);
                    if (target && attackClock < shot.hitStart - 0.05f) FaceTowards(target.transform.position, dt, 1.6f);
                    break;

                case State.Retreat:
                {
                    if (!ValidTarget()) { _state = State.Return; break; }
                    FaceTowards(target.transform.position, dt);
                    Vector3 away = MathUtil.Flat(transform.position - target.transform.position).normalized;
                    MoveTowards(away, walkSpeed * 1.5f, dt);
                    if (_stateTimer <= 0f)
                    {
                        // still cornered: loose an arrow point-blank, otherwise back to the normal dance
                        if (DistanceToTarget < fleeRange + 1.5f && HasLineOfSight()) BeginShot();
                        else Engage(Random.Range(0.3f, 0.8f));
                    }
                    break;
                }

                case State.Staggered:
                    StopMoving(dt);
                    if (_stateTimer <= 0f) { PlayAnimRpc(StateIndex(CharacterAnimator.Locomotion), 0.2f, 1f); Engage(Random.Range(0.6f, 1.2f)); }
                    break;

                case State.Return:
                {
                    target = FindNearestPlayer(aggroRange * 0.7f);
                    if (target && Vector3.Distance(transform.position, homePosition) < leashRange * 0.8f) { Engage(0.6f); break; }
                    Vector3 toHome = homePosition - transform.position;
                    if (MathUtil.Flat(toHome).magnitude < 0.8f) { _state = State.Idle; StopMoving(dt); break; }
                    FaceTowards(homePosition, dt);
                    MoveTowards(toHome, walkSpeed, dt);
                    break;
                }
            }
        }

        void Engage(float firstShotDelay)
        {
            _state = State.Engage;
            _nextShot = Mathf.Max(_nextShot, Time.time + firstShotDelay);
        }

        bool ValidTarget() => target != null && target.IsSpawned && !target.IsDead && DistanceToTarget < leashRange
                              && Vector3.Distance(transform.position, homePosition) < leashRange;

        bool HasLineOfSight() => target && !Physics.Linecast(transform.position + Vector3.up * 1.5f, target.transform.position + Vector3.up * 1.2f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);

        void BeginShot()
        {
            _state = State.Shoot;
            StartAttack(shot, HitShape.Projectile, 0f, 0f);
        }

        void TrackTarget(float dt)
        {
            if (!target || dt <= 0f) return;
            Vector3 p = target.transform.position;
            if (_track.TryGetValue(target, out var tr)) _track[target] = (p, Vector3.Lerp(tr.vel, (p - tr.pos) / dt, 0.2f));
            else _track[target] = (p, Vector3.zero);
        }

        protected override void OnAttackImpact(AttackData a)
        {
            if (!target) return;
            Vector3 nock = bow ? bow.NockPosition : transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
            Vector3 aim = target.transform.position + Vector3.up * 1.1f;
            float tt = Vector3.Distance(nock, aim) / arrowSpeed;
            if (_track.TryGetValue(target, out var tr)) aim += MathUtil.Flat(tr.vel) * tt * leadFactor;
            float g = bow && bow.arrowPrefab && bow.arrowPrefab.TryGetComponent(out Arrow ar) ? ar.gravity : 5.5f;
            aim += Vector3.up * 0.5f * g * tt * tt;
            // a little human error
            Vector3 dir = Quaternion.Euler(Random.Range(-1.2f, 1.2f), Random.Range(-1.8f, 1.8f), 0f) * (aim - nock).normalized;
            ShootRpc(nock, dir * arrowSpeed);
        }

        [Rpc(SendTo.Everyone)]
        void ShootRpc(Vector3 nock, Vector3 velocity)
        {
            if (bow == null || bow.arrowPrefab == null) return;
            Arrow.FireEnemy(bow.arrowPrefab, nock, velocity, this, IsServer, arrowDamage, arrowPoise, HitFlags.None);
        }

        protected override void OnAttackFinished(AttackData a)
        {
            if (_state != State.Shoot) return;
            PlayAnimRpc(StateIndex(CharacterAnimator.Locomotion), 0.25f, 1f);
            _nextShot = Time.time + Random.Range(shotInterval.x, shotInterval.y);
            _state = State.Engage;
        }

        protected override void Stagger(float duration)
        {
            CancelAttack();
            _state = State.Staggered;
            _stateTimer = duration;
            PlayAnimRpc(StateIndex(CharacterAnimator.Hurt), 0.05f, 0.6f);
        }

        protected override void OnDamaged(Vector3 attackerPos)
        {
            if (_state == State.Idle || _state == State.Return)
            {
                target = HitIsRanged && !LastRangedAttacker.IsDead ? LastRangedAttacker : FindNearestPlayer(leashRange, false);
                if (target) Engage(0.4f);
            }
            // a knight hacking at it: break away instead of standing there
            if (_state == State.Engage && Vector3.Distance(attackerPos, transform.position) < fleeRange && Time.time > _retreatCooldown)
            {
                _state = State.Retreat; _stateTimer = 1.2f; _retreatCooldown = Time.time + 3.5f;
            }
        }

        protected override void Die()
        {
            _state = State.Dead;
            base.Die();
        }
    }
}
