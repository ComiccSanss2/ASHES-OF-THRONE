using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Shard Wraith (the Thornwood): a hooded spectre of smoke and broken glass that drifts above
    /// the ground. Keeps its distance and flings fans of glass splinters (the glass glows before the
    /// volley: dodge sideways); rakes with its claw when someone gets close. Fragile: two or three
    /// good blows break it apart. Host-authoritative, no animator: the body is posed in code.
    /// </summary>
    public class ShardWraithAI : EnemyBase
    {
        public enum Act : byte { None, Volley, Claw, Hurt }

        [Header("Wraith")]
        public float aggroRange = 17f;
        public float leashRange = 28f;
        public float preferredMin = 6.5f, preferredMax = 11f;
        public float splinterDamage = 13f, splinterSpeed = 19f;
        public float clawDamage = 17f, clawRange = 2.8f;
        public float volleyCooldown = 2.8f;
        [Tooltip("Visual body (bobbing, leaning) and the shards that circle it.")]
        public Transform body;
        public Transform[] shards;
        public Renderer glowRenderer;
        public Light glowLight;

        readonly NetworkVariable<byte> _act = new NetworkVariable<byte>(0);
        readonly NetworkVariable<double> _actStart = new NetworkVariable<double>(0);

        const float VolleyWind = 0.75f, VolleyDur = 1.25f, ClawWind = 0.5f, ClawHit = 0.62f, ClawDur = 1.05f, HurtDur = 1.3f;

        float _cool, _strafe = 1f, _strafeTimer;
        bool _fired;
        MaterialPropertyBlock _mpb;
        Vector3[] _shardSeed;

        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        Act CurrentAct => (Act)_act.Value;
        float ActTime => (float)(Now - _actStart.Value);

        public override bool IsStaggered => CurrentAct == Act.Hurt;
        protected override float GruntPitch => 1.6f;
        protected override float GruntChance => 0.15f;

        protected override void Awake()
        {
            base.Awake();
            anim = null;
            _mpb = new MaterialPropertyBlock();
            if (shards != null)
            {
                _shardSeed = new Vector3[shards.Length];
                for (int i = 0; i < shards.Length; i++) _shardSeed[i] = new Vector3(Random.Range(0.7f, 1.3f), Random.Range(0.6f, 2.2f), Random.Range(1.2f, 2.4f) * (i % 2 == 0 ? 1 : -1));
            }
        }

        void SetAct(Act a)
        {
            _act.Value = (byte)a;
            _actStart.Value = Now;
            _fired = false;
        }

        // ================================================================== server
        protected override void ServerTick(float dt)
        {
            _cool -= dt;
            var a = CurrentAct;
            if (a != Act.None)
            {
                StopMoving(dt);
                float t = ActTime;
                if (target) FaceTowards(target.transform.position, dt, a == Act.Volley && t < VolleyWind ? 1.4f : 0.3f);
                if (a == Act.Volley && !_fired && t >= VolleyWind)
                {
                    _fired = true;
                    if (target) FireVolley();
                }
                if (a == Act.Claw && !_fired && t >= ClawHit)
                {
                    _fired = true;
                    foreach (var p in PlayerController.All)
                    {
                        if (!p || p.IsDead) continue;
                        Vector3 to = p.transform.position - transform.position;
                        if (MathUtil.Flat(to).magnitude < clawRange && Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to)) < 70f && Mathf.Abs(to.y) < 2.2f)
                            DeliverHit(p, clawDamage, HitFlags.Parryable, true);
                    }
                }
                float dur = a == Act.Volley ? VolleyDur : a == Act.Claw ? ClawDur : HurtDur;
                if (t >= dur) SetAct(Act.None);
                return;
            }

            if (target == null || target.IsDead || Vector3.Distance(transform.position, homePosition) > leashRange)
            {
                target = Vector3.Distance(transform.position, homePosition) > leashRange ? null : FindNearestPlayer(aggroRange);
                if (target == null)
                {
                    Vector3 home = homePosition - transform.position;
                    if (MathUtil.Flat(home).magnitude > 1f) { MoveTowards(home, chaseSpeed, dt); FaceTowards(homePosition, dt); }
                    else StopMoving(dt);
                    return;
                }
            }

            Vector3 toT = target.transform.position - transform.position;
            float d = MathUtil.Flat(toT).magnitude;
            FaceTowards(target.transform.position, dt);
            if (d < clawRange * 0.9f && _cool < 1.5f) { SetAct(Act.Claw); TelegraphRpc(); return; }
            if (_cool <= 0f && d < aggroRange && !Physics.Linecast(transform.position + Vector3.up * 1.6f, target.transform.position + Vector3.up * 1.2f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                _cool = volleyCooldown + Random.Range(-0.4f, 0.6f);
                SetAct(Act.Volley);
                return;
            }
            // keep the distance, drift sideways
            _strafeTimer -= dt;
            if (_strafeTimer <= 0f) { _strafeTimer = Random.Range(1.2f, 2.6f); _strafe = Random.value < 0.5f ? -1f : 1f; }
            Vector3 dir = Vector3.Cross(Vector3.up, MathUtil.Flat(toT).normalized) * _strafe * 0.7f;
            if (d > preferredMax) dir += MathUtil.Flat(toT).normalized;
            else if (d < preferredMin) dir -= MathUtil.Flat(toT).normalized;
            MoveTowards(dir, d > preferredMax ? chaseSpeed : walkSpeed, dt);
        }

        void FireVolley()
        {
            Vector3 from = transform.position + Vector3.up * 2.1f + transform.forward * 0.6f;
            Vector3 aim = target.transform.position + Vector3.up * 1.1f;
            var tcc = target.GetComponent<CharacterController>();
            if (tcc) aim += MathUtil.Flat(tcc.velocity) * 0.25f;   // a little lead
            Vector3 dir = (aim - from).normalized;
            for (int k = -1; k <= 1; k++)
            {
                Vector3 v = Quaternion.AngleAxis(k * 9f, Vector3.up) * dir * splinterSpeed;
                ShardRpc(from, v);
            }
        }

        protected override void Stagger(float duration)
        {
            SetAct(Act.Hurt);
        }

        protected override void OnDamaged(Vector3 attackerPos)
        {
            if (target == null) target = FindNearestPlayer(aggroRange * 1.5f, false);
        }

        [Rpc(SendTo.Everyone)]
        void ShardRpc(Vector3 from, Vector3 vel)
        {
            GlassShard.Fire(GlassShard.Kind.Splinter, from, vel, this, IsServer, splinterDamage, 12f);
            Sfx.Play(SfxId.ArrowImpact, from, 0.6f, Random.Range(1.6f, 1.9f));
        }

        [Rpc(SendTo.Everyone)]
        void TelegraphRpc() => Sfx.Play(SfxId.MirrorCurse, transform.position + Vector3.up * 2f, 0.6f, 1.6f);

        // ================================================================== visuals (everyone)
        protected override void Update()
        {
            base.Update();
            if (!body) return;
            float t = Time.time;
            bool alive = IsAlive;
            var a = CurrentAct;
            float at = ActTime;
            // drifting above the ground, swaying; leans in for the claw, rears for the volley
            float lean = 0f, lift = 0f, glow = 0.35f + Mathf.Sin(t * 3f) * 0.08f;
            if (a == Act.Volley) { float k = Mathf.Clamp01(at / VolleyWind); lift = 0.4f * k; lean = -14f * k; glow = 0.4f + k * 2.2f; if (at > VolleyWind) glow = Mathf.Lerp(2.6f, 0.4f, (at - VolleyWind) / 0.4f); }
            if (a == Act.Claw) { float k = at < ClawHit ? at / ClawHit : 1f - (at - ClawHit) / (ClawDur - ClawHit); lean = 28f * Mathf.Clamp01(k); glow = 0.9f; }
            if (a == Act.Hurt) { lean = -22f * Mathf.Clamp01(1f - at / HurtDur); glow = 0.1f; }
            float deathK = alive ? 0f : 1f;
            body.localPosition = new Vector3(0f, (1.1f + Mathf.Sin(t * 1.7f + NetworkObjectId) * 0.18f + lift) * (1f - deathK * 0.85f), 0f);
            body.localRotation = Quaternion.Euler(lean + deathK * 60f, 0f, Mathf.Sin(t * 1.1f) * 4f);
            if (shards != null)
                for (int i = 0; i < shards.Length; i++)
                {
                    var s = shards[i];
                    if (!s) continue;
                    s.gameObject.SetActive(alive);
                    var sd = _shardSeed[i];
                    float ang = t * sd.z + i * 2.1f;
                    float r = sd.x * (a == Act.Volley ? 0.6f : 1f);
                    s.localPosition = new Vector3(Mathf.Cos(ang) * r, sd.y + Mathf.Sin(t * 2f + i) * 0.2f, Mathf.Sin(ang) * r);
                    s.localRotation = Quaternion.Euler(t * 90f + i * 40f, t * 60f, 0f);
                }
            if (glowRenderer)
            {
                glowRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", MirrorFx.Void * glow * (alive ? 1f : 0f));
                glowRenderer.SetPropertyBlock(_mpb);
            }
            if (glowLight) glowLight.intensity = alive ? 1.2f + glow * 2f : 0f;
        }
    }
}
