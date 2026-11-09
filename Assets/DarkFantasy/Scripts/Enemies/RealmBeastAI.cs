using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The creatures of the Red Moon (server-authoritative, procedurally animated on every client):
    ///  - Gravecrawler: a ribcage on six spindly legs with a skull for a head. Skitters fast, rears
    ///    up (tell) and leaps to bite, or snaps at close range.
    ///  - Lantern Warden: a gaunt hooded giant carrying a soul lantern on a pole. Slow; winds the
    ///    lantern back and sweeps it in a wide arc, or lifts it high and tolls an expanding ring of
    ///    soul-light (the lantern brightens and the ground is marked before it rings).
    /// </summary>
    public class RealmBeastAI : EnemyBase
    {
        public enum Kind : byte { Crawler, Warden }
        public enum Act : byte { None, Leap, Snap, Swing, Toll, Hurt }

        [Header("Beast")]
        public Kind kind;
        public float aggroRange = 18f, leashRange = 30f;
        public float meleeDamage = 20f, specialDamage = 26f;

        [Header("Visual rig (procedural)")]
        public Transform body;           // the torso (bob, pitch, rear)
        public Transform head;
        public Transform[] legs;         // crawler: hip pivots, alternate tripods
        public Transform arm;            // warden: shoulder pivot holding the pole
        public Light lanternLight;
        public Renderer glowRenderer;

        readonly NetworkVariable<byte> _act = new NetworkVariable<byte>(0);
        readonly NetworkVariable<double> _actStart = new NetworkVariable<double>(0);

        // timings
        const float LeapWind = 0.55f, LeapGo = 0.75f, LeapDur = 1.45f;
        const float SnapHit = 0.38f, SnapDur = 0.9f;
        const float SwingHit = 0.95f, SwingDur = 1.9f;
        const float TollRing = 1.3f, TollDur = 2.4f;
        const float HurtDur = 1.1f;

        float _cool, _gait;
        bool _fired;
        Vector3 _leapDir;
        Quaternion[] _legRest;
        Vector3 _bodyRest;
        Quaternion _armRest;

        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        Act CurrentAct => (Act)_act.Value;
        float ActTime => (float)(Now - _actStart.Value);
        public override bool IsStaggered => CurrentAct == Act.Hurt;
        protected override float GruntPitch => kind == Kind.Crawler ? 1.5f : 0.55f;
        protected override float GruntChance => 0.2f;

        protected override void Awake()
        {
            base.Awake();
            anim = null;
            if (legs != null) { _legRest = new Quaternion[legs.Length]; for (int i = 0; i < legs.Length; i++) if (legs[i]) _legRest[i] = legs[i].localRotation; }
            if (body) _bodyRest = body.localPosition;
            if (arm) _armRest = arm.localRotation;
        }

        void SetAct(Act a) { _act.Value = (byte)a; _actStart.Value = Now; _fired = false; }

        // ================================================================== server
        protected override void ServerTick(float dt)
        {
            _cool -= dt;
            var a = CurrentAct;
            if (a != Act.None) { UpdateAct(a, ActTime, dt); return; }

            if (target == null || target.IsDead || Vector3.Distance(transform.position, homePosition) > leashRange)
            {
                target = Vector3.Distance(transform.position, homePosition) > leashRange ? null : FindNearestPlayer(aggroRange);
                if (target == null)
                {
                    Vector3 home = homePosition - transform.position;
                    if (MathUtil.Flat(home).magnitude > 1f) { MoveTowards(home, walkSpeed, dt); FaceTowards(homePosition, dt); }
                    else StopMoving(dt);
                    return;
                }
            }
            Vector3 to = target.transform.position - transform.position;
            float d = MathUtil.Flat(to).magnitude;
            float ang = Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to));
            FaceTowards(target.transform.position, dt);

            if (kind == Kind.Crawler)
            {
                if (_cool <= 0f && d < 2.4f && ang < 40f) { _cool = 1.2f; SetAct(Act.Snap); TellRpc((byte)Act.Snap); return; }
                if (_cool <= 0f && d > 4f && d < 9f && ang < 25f) { _cool = 2.6f; _leapDir = MathUtil.Flat(to).normalized; SetAct(Act.Leap); TellRpc((byte)Act.Leap); return; }
                MoveTowards(to, d > 4f ? chaseSpeed : walkSpeed, dt);
            }
            else
            {
                if (_cool <= 0f && d < 4.2f && ang < 50f) { _cool = 2.2f; SetAct(Random.value < 0.3f ? Act.Toll : Act.Swing); TellRpc(_act.Value); return; }
                if (_cool <= 0f && d < 9f && Random.value < dt * 0.4f) { _cool = 3f; SetAct(Act.Toll); TellRpc((byte)Act.Toll); return; }
                if (d > 3f) MoveTowards(to, d > 8f ? chaseSpeed : walkSpeed, dt); else StopMoving(dt);
            }
        }

        void UpdateAct(Act a, float t, float dt)
        {
            switch (a)
            {
                case Act.Leap:
                    if (t < LeapWind) { StopMoving(dt); if (target) { FaceTowards(target.transform.position, dt, 1.5f); _leapDir = MathUtil.Flat(target.transform.position - transform.position).normalized; } }
                    else if (t < LeapGo + 0.25f) { cc.Move(_leapDir * 13f * dt); if (!_fired && t >= LeapGo) { _fired = true; Strike(2.2f, 60f, meleeDamage * 1.2f, HitFlags.Heavy); } }
                    else StopMoving(dt);
                    if (t >= LeapDur) SetAct(Act.None);
                    break;
                case Act.Snap:
                    StopMoving(dt);
                    if (t < SnapHit * 0.8f && target) FaceTowards(target.transform.position, dt, 1.2f);
                    if (!_fired && t >= SnapHit) { _fired = true; Strike(2.4f, 55f, meleeDamage, HitFlags.Parryable); }
                    if (t >= SnapDur) SetAct(Act.None);
                    break;
                case Act.Swing:
                    StopMoving(dt);
                    if (t < SwingHit * 0.7f && target) FaceTowards(target.transform.position, dt, 0.8f);
                    if (!_fired && t >= SwingHit) { _fired = true; Strike(4.2f, 80f, specialDamage, HitFlags.Heavy); SwingFxRpc(); }
                    if (t >= SwingDur) SetAct(Act.None);
                    break;
                case Act.Toll:
                    StopMoving(dt);
                    if (!_fired && t >= TollRing)
                    {
                        _fired = true;
                        foreach (var p in PlayerController.All)
                            if (p && !p.IsDead && MathUtil.Flat(p.transform.position - transform.position).magnitude < 5.4f && Mathf.Abs(p.transform.position.y - transform.position.y) < 2.5f)
                                DeliverHit(p, specialDamage * 0.85f, HitFlags.AreaOfEffect | HitFlags.Dark, true);
                        TollFxRpc(transform.position);
                    }
                    if (t >= TollDur) SetAct(Act.None);
                    break;
                case Act.Hurt:
                    StopMoving(dt);
                    if (t >= HurtDur) SetAct(Act.None);
                    break;
            }
        }

        void Strike(float range, float halfAngle, float dmg, HitFlags flags)
        {
            foreach (var p in PlayerController.All)
            {
                if (!p || p.IsDead) continue;
                Vector3 to = p.transform.position - transform.position;
                if (MathUtil.Flat(to).magnitude < range && Mathf.Abs(MathUtil.SignedAngleY(transform.forward, to)) < halfAngle && Mathf.Abs(to.y) < 2.4f)
                    DeliverHit(p, dmg, flags, true);
            }
        }

        protected override void Stagger(float duration) { if (CurrentAct != Act.Toll || ActTime < 0.6f) SetAct(Act.Hurt); }

        // ================================================================== tells and effects (everyone)
        [Rpc(SendTo.Everyone)]
        void TellRpc(byte act)
        {
            Vector3 c = transform.position;
            switch ((Act)act)
            {
                case Act.Leap: Sfx.Play(SfxId.EnemyGrunt, c, 1f, 1.8f); break;
                case Act.Snap: Sfx.Play(SfxId.EnemyGrunt, c, 0.8f, 2f); break;
                case Act.Swing: Sfx.Play(SfxId.Swing, c + Vector3.up * 2f, 1f, 0.55f); break;
                case Act.Toll:
                    BossFx.GroundWarning(c, 5.4f, TollRing, new Color(0.45f, 0.6f, 1f));
                    Sfx.Play(SfxId.MirrorHum, c + Vector3.up * 3f, 1f, 0.7f);
                    break;
            }
        }

        [Rpc(SendTo.Everyone)]
        void SwingFxRpc() => Sfx.Play(SfxId.Slam, transform.position + transform.forward * 2.5f, 0.9f, 0.9f);

        [Rpc(SendTo.Everyone)]
        void TollFxRpc(Vector3 c)
        {
            Sfx.Play(SfxId.MirrorShatter, c + Vector3.up * 2.5f, 0.9f, 0.5f);
            SpellFx.GroundRing(c + Vector3.up * 0.05f, 0.8f, 5.6f, new Color(0.5f, 0.7f, 1f), 0.5f, 0.8f);
            BossFx.Flash(c + Vector3.up * 3f, new Color(0.55f, 0.75f, 1f), 10f, 12f, 0.4f);
        }

        // ================================================================== procedural animation (everyone)
        protected override void Update()
        {
            base.Update();
            float dt = Time.deltaTime, t = ActTime;
            var a = CurrentAct;
            bool alive = IsAlive;
            float speed = netLocoSpeed.Value;
            _gait += dt * (kind == Kind.Crawler ? 2.2f : 1.1f) * Mathf.Max(0.6f, speed);

            if (kind == Kind.Crawler)
            {
                // alternate tripods; the body rears for the leap tell, lunges for the bite
                float rear = 0f, lunge = 0f;
                if (a == Act.Leap) { rear = t < LeapWind ? Mathf.SmoothStep(0f, 1f, t / LeapWind) : t < LeapGo + 0.25f ? -0.6f : Mathf.Lerp(-0.6f, 0f, (t - LeapGo - 0.25f) / 0.4f); }
                if (a == Act.Snap) { rear = t < SnapHit * 0.8f ? 0.5f * (t / (SnapHit * 0.8f)) : -0.5f; lunge = t > SnapHit * 0.8f && t < SnapDur * 0.8f ? 0.35f : 0f; }
                if (a == Act.Hurt) rear = -0.4f * Mathf.Sin(Mathf.Clamp01(t / HurtDur) * Mathf.PI);
                if (!alive) rear = -1.2f;
                if (body)
                {
                    float bob = alive ? Mathf.Abs(Mathf.Sin(_gait * Mathf.PI)) * 0.05f * Mathf.Clamp01(speed) : 0f;
                    body.localPosition = Vector3.Lerp(body.localPosition, _bodyRest + new Vector3(0f, bob + rear * 0.25f + (alive ? 0f : -0.3f), lunge), 1f - Mathf.Exp(-14f * dt));
                    body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(-rear * 35f, 0f, alive ? Mathf.Sin(_gait * Mathf.PI) * 3f * Mathf.Clamp01(speed) : 25f), 1f - Mathf.Exp(-12f * dt));
                }
                if (legs != null)
                    for (int i = 0; i < legs.Length; i++)
                    {
                        if (!legs[i]) continue;
                        float ph = _gait * Mathf.PI + (i % 2 == 0 ? 0f : Mathf.PI) + (i / 2) * 0.4f;
                        float k = alive ? Mathf.Clamp01(speed / 2f) : 0f;
                        float swing = Mathf.Sin(ph) * 28f * k, lift = Mathf.Max(0f, Mathf.Cos(ph)) * 22f * k;
                        if (!alive) lift = -30f;
                        legs[i].localRotation = _legRest[i] * Quaternion.Euler(-lift, swing, 0f);
                    }
                if (head) head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(a == Act.Snap && t > SnapHit * 0.8f ? 25f : a == Act.Leap && t < LeapWind ? -25f : 0f, 0f, 0f), 1f - Mathf.Exp(-14f * dt));
            }
            else
            {
                // the lantern: drawn back over the shoulder, then swept across; raised high to toll
                Quaternion want = Quaternion.identity;
                if (a == Act.Swing)
                {
                    if (t < SwingHit - 0.2f) want = Quaternion.Euler(-30f * Mathf.SmoothStep(0f, 1f, t / (SwingHit - 0.2f)), -80f * Mathf.SmoothStep(0f, 1f, t / (SwingHit - 0.2f)), 0f);
                    else if (t < SwingHit + 0.25f) want = Quaternion.Euler(10f, Mathf.Lerp(-80f, 90f, Mathf.SmoothStep(0f, 1f, (t - SwingHit + 0.2f) / 0.45f)), 0f);
                    else want = Quaternion.Euler(0f, Mathf.Lerp(90f, 0f, (t - SwingHit - 0.25f) / 0.7f), 0f);
                }
                else if (a == Act.Toll) want = Quaternion.Euler(t < TollRing ? -120f * Mathf.SmoothStep(0f, 1f, t / 0.8f) : Mathf.Lerp(-120f, 0f, (t - TollRing) / 0.8f), 0f, 0f);
                if (arm) arm.localRotation = Quaternion.Slerp(arm.localRotation, _armRest * want, 1f - Mathf.Exp(-10f * dt));
                if (body)
                {
                    float sway = alive ? Mathf.Sin(_gait * Mathf.PI) * 4f * Mathf.Clamp01(speed) : 0f;
                    float lean = a == Act.Swing && t > SwingHit - 0.2f && t < SwingHit + 0.3f ? 12f : a == Act.Hurt ? -14f : !alive ? -70f : 0f;
                    body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(lean, 0f, sway), 1f - Mathf.Exp(-8f * dt));
                }
                if (lanternLight)
                {
                    float glow = a == Act.Toll && t < TollRing ? Mathf.Lerp(3f, 10f, t / TollRing) : 3f + Mathf.Sin(Time.time * 3f) * 0.5f;
                    lanternLight.intensity = alive ? glow : Mathf.MoveTowards(lanternLight.intensity, 0f, dt * 2f);
                }
            }
        }
    }
}
