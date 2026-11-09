using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The class ultimate (key 1 / D-pad up), for every class but the bard (he has his songs).
    /// The meter fills slowly by itself and quickly by dealing damage; when full, the icon blazes.
    ///  - Knight  "Cinder Judgement": an overhead blow that splits the ground, a wall of fire races forward.
    ///  - Archer  "Storm of Arrows":  one arrow loosed at the sky, a rain of arrows on the aimed spot.
    ///  - Reaper  "Harvest of Souls": two whirling turns that rend everything around, a leaping
    ///    reap that crushes the ground; every soul taken heals the reaper.
    ///  - Smoker  "The Great Exhale": a colossus of smoke rises behind him and pounds the field.
    /// The owner plays the animation (PlayerController.StartUltimate: hyper armour, no flinch),
    /// deals the damage (same RPCs as arrows) and sends the effects to everyone.
    /// </summary>
    public class PlayerUltimate : NetworkBehaviour
    {
        public enum Kind : byte { None, CinderJudgement, ArrowStorm, Harvest, SmokeColossus }

        public static PlayerUltimate Local { get; private set; }

        public Kind kind;
        public string ultName = "Ultimate";
        [TextArea(2, 4)] public string blurb;
        [Tooltip("Animation of the ultimate: state Ultimate. hitStart = the decisive frame.")]
        public AttackData cast;
        [Tooltip("Meter (0-100) gained per second by itself and per point of damage dealt.")]
        public float passivePerSecond = 1.1f, perDamage = 0.5f;

        [Header("Cinder Judgement")]
        public float laneLength = 10f, laneHalfWidth = 1.5f, laneDamage = 95f, lanePoise = 120f;
        [Header("Storm of Arrows")]
        public float stormRange = 28f, stormRadius = 4.6f, stormTick = 11f, stormDelay = 0.45f;
        public int stormTicks = 8;
        [Header("Harvest of Souls")]
        public float harvestRadius = 3.7f, harvestTick = 13f, harvestCrush = 125f, harvestCrushPoise = 150f;
        public float harvestSpinStart = 0.3f, harvestSpinEnd = 1.2f, harvestTickEvery = 0.2f, harvestCrushTime = 1.66f;
        [Range(0, 0.2f)] public float healPerSoul = 0.04f;

        public PlayerController pc;

        float _charge;
        bool _active;
        float _nextTick;
        bool _crushed;
        float _healed;
        Vector3 _stormPoint;

        public float Charge01 => Mathf.Clamp01(_charge / 100f);
        public bool Ready => _charge >= 100f;
        public bool Active => _active;

        public override void OnNetworkSpawn() { if (IsOwner) Local = this; }
        public override void OnNetworkDespawn() { if (Local == this) Local = null; }

        /// <summary>Owner: damage dealt by the local player charges the meter (not by the ultimate itself).</summary>
        public static void LocalDealt(float damage)
        {
            var u = Local;
            if (u == null || u._active) return;
            u._charge = Mathf.Min(100f, u._charge + damage * u.perDamage * PlayerSkills.UltChargeMul);
        }

        public void DevCharge(string _) { if (IsOwner) _charge = 100f; }
        /// <summary>Development (bridge): fills the meter and casts the ultimate at once.</summary>
        public void DevUlt(string _) { if (!IsOwner) return; _charge = 100f; Debug.Log("[DF] ultimate " + kind + " → " + TryActivate()); }

        // ------------------------------------------------------------------ owner
        public void OwnerTick(float dt)
        {
            if (!_active && !pc.IsDead) _charge = Mathf.Min(100f, _charge + passivePerSecond * PlayerSkills.UltChargeMul * dt);
        }

        public bool TryActivate()
        {
            if (!IsOwner || !Ready || cast == null) return false;
            if (!pc.StartUltimate(cast, OnRelease)) return false;
            _charge = 0f;
            _active = true;
            _nextTick = harvestSpinStart;
            _crushed = false;
            _healed = 0f;
            if (kind == Kind.ArrowStorm)
            {
                Vector3 from = transform.position + Vector3.up * 1.5f;
                Vector3 p = pc.PredictAimPoint(from);
                Vector3 flat = MathUtil.Flat(p - transform.position);
                if (flat.magnitude > stormRange) p = transform.position + flat.normalized * stormRange;
                if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 30f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) p = hit.point;
                _stormPoint = p;
            }
            StartFxRpc((byte)kind, transform.position, kind == Kind.ArrowStorm ? _stormPoint : transform.forward);
            return true;
        }

        /// <summary>Owner, every frame of the ultimate animation (clip seconds).</summary>
        public void OwnerUpdateActive(float t)
        {
            if (!_active || kind != Kind.Harvest) return;
            while (t >= _nextTick && _nextTick <= harvestSpinEnd)
            {
                _nextTick += harvestTickEvery;
                int n = HitAround(transform.position, harvestRadius, 360f, harvestTick * Mul, 14f, 2f, false);
                Harvested(n);
                TickFxRpc(transform.position);
            }
            if (!_crushed && t >= harvestCrushTime)
            {
                _crushed = true;
                Vector3 c = transform.position + transform.forward * 1.6f;
                int n = HitAround(c, harvestRadius, 360f, harvestCrush * Mul, harvestCrushPoise, 7f, true);
                Harvested(n * 2);
                SoulsCamera.Shake(0.12f, 0.4f);
                CrushFxRpc(c);
            }
        }

        public void OnEnded() { _active = false; }

        float Mul => pc.WeaponDamageMul * pc.DamageDealtMul * PlayerSkills.ArcanaMul;

        void OnRelease()
        {
            switch (kind)
            {
                case Kind.CinderJudgement: StartCoroutine(Lane(transform.position, MathUtil.Flat(transform.forward).normalized)); break;
                case Kind.ArrowStorm: StartCoroutine(Storm(_stormPoint)); break;
                case Kind.SmokeColossus:
                    var sm = pc.smoker;
                    SmokeColossus.Spawn(transform.position, transform.forward, pc, true, Mul,
                        sm ? sm.Smoke : SmokerCaster.SmokeColor(0), sm ? sm.Ember : SmokerCaster.EmberColor(0));
                    break;
            }
        }

        IEnumerator Lane(Vector3 origin, Vector3 dir)
        {
            SoulsCamera.Shake(0.13f, 0.45f);
            var hit = new HashSet<EnemyBase>();
            var hitP = new HashSet<PlayerController>();
            int steps = 5;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = origin + dir * (1.4f + laneLength * i / steps);
                foreach (var e in EnemyBase.All)
                {
                    if (e == null || !e.IsAlive || hit.Contains(e)) continue;
                    Vector3 to = e.transform.position - origin;
                    float along = Vector3.Dot(MathUtil.Flat(to), dir);
                    if (along < 0f || along > 1.4f + laneLength * i / steps + 1f) continue;
                    if (Vector3.Distance(MathUtil.Flat(e.transform.position), MathUtil.Flat(origin + dir * along)) > laneHalfWidth + 0.6f) continue;
                    if (Mathf.Abs(to.y) > 3f) continue;
                    hit.Add(e);
                    Strike(e, laneDamage * Mul, lanePoise, 8f);
                }
                foreach (var pl in Foes())
                {
                    if (hitP.Contains(pl)) continue;
                    Vector3 to = pl.transform.position - origin;
                    float along = Vector3.Dot(MathUtil.Flat(to), dir);
                    if (along < 0f || along > 1.4f + laneLength * i / steps + 1f || Mathf.Abs(to.y) > 3f) continue;
                    if (Vector3.Distance(MathUtil.Flat(pl.transform.position), MathUtil.Flat(origin + dir * along)) > laneHalfWidth + 0.6f) continue;
                    hitP.Add(pl);
                    StrikePlayer(pl, laneDamage * Mul, lanePoise, true);
                }
                yield return new WaitForSeconds(0.06f);
            }
        }

        IEnumerator Storm(Vector3 at)
        {
            yield return new WaitForSeconds(stormDelay);
            for (int k = 0; k < stormTicks; k++)
            {
                foreach (var e in EnemyBase.All)
                {
                    if (e == null || !e.IsAlive) continue;
                    if (MathUtil.Flat(e.transform.position - at).magnitude > stormRadius + 0.4f || Mathf.Abs(e.transform.position.y - at.y) > 4f) continue;
                    Strike(e, stormTick * Mul, 8f, 0.5f);
                }
                foreach (var pl in Foes())
                    if (MathUtil.Flat(pl.transform.position - at).magnitude <= stormRadius + 0.4f && Mathf.Abs(pl.transform.position.y - at.y) <= 4f)
                        StrikePlayer(pl, stormTick * Mul, 8f, false);
                yield return new WaitForSeconds(0.25f);
            }
        }

        int HitAround(Vector3 c, float r, float cone, float dmg, float poise, float kb, bool heavy)
        {
            int n = 0;
            foreach (var e in EnemyBase.All)
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 to = e.transform.position - c;
                if (MathUtil.Flat(to).magnitude > r + 0.5f || Mathf.Abs(to.y) > 2.5f) continue;
                Strike(e, dmg, poise, kb);
                SoulRpc(e.transform.position + Vector3.up * 1.2f);
                n++;
            }
            foreach (var pl in Foes())
            {
                Vector3 to = pl.transform.position - c;
                if (MathUtil.Flat(to).magnitude > r + 0.5f || Mathf.Abs(to.y) > 2.5f) continue;
                StrikePlayer(pl, dmg, poise, heavy);
                SoulRpc(pl.transform.position + Vector3.up * 1.2f);
                n++;
            }
            return n;
        }

        // ---- PvP: the ultimates also strike hostile players (duels), at a reduced rate
        const float PvpUltMul = 0.6f;

        IEnumerable<PlayerController> Foes()
        {
            foreach (var pl in PlayerController.All)
                if (pl && pl != pc && !pl.IsDead && PlayerController.CanPvp(pc, pl)) yield return pl;
        }

        void StrikePlayer(PlayerController pl, float dmg, float poise, bool heavy)
        {
            pl.ReceiveHitRpc(dmg * PlayerController.PvpDamageMul * PvpUltMul, poise, transform.position,
                (byte)(heavy ? HitFlags.Heavy : HitFlags.None), pc.NetworkObject);
        }

        void Strike(EnemyBase e, float dmg, float poise, float kb)
        {
            Vector3 point = e.transform.position + Vector3.up * 1.1f;
            e.LocalHitReact(point, kb > 4f, transform.position, transform.right);
            e.TakeRangedHitRpc(dmg, poise, point, transform.position, kb);
        }

        void Harvested(int souls)
        {
            if (souls <= 0 || pc.vitals == null) return;
            float add = Mathf.Min(healPerSoul * souls, 0.25f - _healed);
            if (add <= 0f) return;
            _healed += add;
            pc.vitals.Heal(add);
        }

        // ------------------------------------------------------------------ effects (everyone)
        static readonly Color Fire = new Color(1f, 0.5f, 0.15f), Sky = new Color(0.55f, 1f, 0.85f), Soul = new Color(0.72f, 0.45f, 1f);

        [Rpc(SendTo.Everyone)]
        void StartFxRpc(byte k, Vector3 pos, Vector3 param)
        {
            switch ((Kind)k)
            {
                case Kind.CinderJudgement:
                    Sfx.Play(SfxId.Roar, pos + Vector3.up * 1.6f, 0.8f, 1.25f);
                    BossFx.Embers(pos + Vector3.up, 0.6f, 30, 3f);
                    break;
                case Kind.ArrowStorm:
                    Sfx.Play(SfxId.BowDraw, pos + Vector3.up * 1.5f, 1f, 0.8f);
                    StartCoroutine(StormFx(param));
                    break;
                case Kind.SmokeColossus:
                    Sfx.Play(SfxId.FogPass, pos + Vector3.up * 1.6f, 1f, 0.5f);
                    if (pc && pc.smoker) SmokeFx.Cloud(pc.smoker.MouthPosition, Vector3.up, 1.2f, pc.smoker.Smoke, 30);
                    if (!IsOwner) StartCoroutine(ColossusFx(pos, param));
                    break;
                case Kind.Harvest:
                    Sfx.Play(SfxId.MirrorCurse, pos + Vector3.up, 1f, 0.8f);
                    SpellFx.GroundRing(pos + Vector3.up * 0.05f, 0.4f, harvestRadius, Soul, 1.4f, 0.9f);
                    break;
            }
            if (kind == Kind.CinderJudgement) StartCoroutine(LaneFx(pos, MathUtil.Flat(param).normalized));
        }

        IEnumerator LaneFx(Vector3 origin, Vector3 dir)
        {
            // wait for the blow to land (same release frame as the owner)
            float wait = cast != null ? Mathf.Max(0f, (cast.hitStart - cast.startTime) / Mathf.Max(0.1f, cast.animSpeed)) : 0.6f;
            yield return new WaitForSeconds(wait);
            Sfx.Play(SfxId.Eruption, origin + dir * 2f, 1.3f, 0.8f);
            Sfx.Play(SfxId.Quake, origin, 1.1f, 0.9f);
            BossFx.RingWave(origin + dir * 1.4f, 4f, 16f, true);
            if (PlayerController.Local && Vector3.Distance(PlayerController.Local.transform.position, origin) < 20f) SoulsCamera.Shake(0.1f, 0.4f);
            int steps = 5;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = BossFx.Ground(origin + dir * (1.4f + laneLength * i / steps));
                BossFx.FirePillar(p, laneHalfWidth, 3.2f + i * 0.4f);
                BossFx.Debris(p, 0.8f, 8);
                yield return new WaitForSeconds(0.06f);
            }
        }

        IEnumerator ColossusFx(Vector3 pos, Vector3 fwd)
        {
            float wait = cast != null ? Mathf.Max(0f, (cast.hitStart - cast.startTime) / Mathf.Max(0.1f, cast.animSpeed)) : 0.6f;
            yield return new WaitForSeconds(wait);
            var sm = pc ? pc.smoker : null;
            SmokeColossus.Spawn(pos, fwd, pc, false, 1f, sm ? sm.Smoke : SmokerCaster.SmokeColor(0), sm ? sm.Ember : SmokerCaster.EmberColor(0));
        }

        IEnumerator StormFx(Vector3 at)
        {
            SpellFx.GroundRing(at + Vector3.up * 0.06f, stormRadius - 0.2f, stormRadius, Sky, stormDelay + stormTicks * 0.25f + 0.4f, 0.8f);
            BossFx.GroundWarning(at, stormRadius, stormDelay, Sky);
            float wait = cast != null ? Mathf.Max(0f, (cast.hitStart - cast.startTime) / Mathf.Max(0.1f, cast.animSpeed)) : 0.5f;
            yield return new WaitForSeconds(wait);
            Sfx.Play(SfxId.BowRelease, transform.position + Vector3.up * 1.5f, 1f, 0.7f);
            Mesh mesh = null; Material mat = null;
            if (pc && pc.bow && pc.bow.arrowPrefab)
            {
                var mf = pc.bow.arrowPrefab.GetComponentInChildren<MeshFilter>(); var mr = pc.bow.arrowPrefab.GetComponentInChildren<MeshRenderer>();
                if (mf) mesh = mf.sharedMesh; if (mr) mat = mr.sharedMaterial;
            }
            int total = stormTicks * 5;
            float span = stormTicks * 0.25f;
            float t0 = Time.time;
            for (int i = 0; i < total; i++)
            {
                float due = t0 + stormDelay - 0.2f + span * i / total;
                while (Time.time < due) yield return null;
                Vector2 r = Random.insideUnitCircle * stormRadius;
                Vector3 land = BossFx.Ground(at + new Vector3(r.x, 0f, r.y));
                FallingArrow.Spawn(mesh, mat, land, Sky);
            }
        }

        [Rpc(SendTo.Everyone)]
        void TickFxRpc(Vector3 pos)
        {
            Sfx.Play(SfxId.DragonTail, pos + Vector3.up, 0.5f, 1.7f);
            SpellFx.GroundRing(pos + Vector3.up * 0.05f, harvestRadius * 0.5f, harvestRadius, Soul, 0.35f, 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        void CrushFxRpc(Vector3 pos)
        {
            Vector3 g = BossFx.Ground(pos);
            Sfx.Play(SfxId.Slam, g, 1.3f, 0.75f);
            Sfx.Play(SfxId.MirrorShatter, g, 0.8f, 0.6f);
            MirrorFx.DarkShock(g, harvestRadius);
            BossFx.Debris(g, 1.2f, 22);
            BossFx.RingWave(g, harvestRadius + 1f, 14f, false);
        }

        [Rpc(SendTo.Everyone)]
        void SoulRpc(Vector3 from) => SoulWisp.Spawn(from, transform, Soul);
    }

    /// <summary>An arrow falling from the sky (Storm of Arrows), purely visual.</summary>
    public class FallingArrow : MonoBehaviour
    {
        Vector3 _land, _vel;
        public static void Spawn(Mesh mesh, Material mat, Vector3 land, Color c)
        {
            var go = new GameObject("FallingArrow");
            Vector3 dir = new Vector3(Random.Range(-0.15f, 0.15f), -1f, Random.Range(-0.15f, 0.15f)).normalized;
            go.transform.position = land - dir * 22f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            if (mesh)
            {
                var vis = new GameObject("mesh"); vis.transform.SetParent(go.transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = vis.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.12f; tr.widthMultiplier = 0.08f; tr.sharedMaterial = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            tr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 1) }, alphaKeys = new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0f, 1) } };
            var a = go.AddComponent<FallingArrow>(); a._land = land; a._vel = dir * 48f;
        }

        void Update()
        {
            Vector3 next = transform.position + _vel * Time.deltaTime;
            if (next.y <= _land.y + 0.3f)
            {
                transform.position = _land + Vector3.up * 0.3f;
                Fx.SpawnScaled(FxId.Dust, _land + Vector3.up * 0.1f, 0.5f);
                if (Random.value < 0.25f) Sfx.Play(SfxId.ArrowImpact, _land, 0.5f, Random.Range(0.9f, 1.2f));
                enabled = false;
                Destroy(gameObject, 1.2f);
                return;
            }
            transform.position = next;
        }
    }

    /// <summary>A reaped soul flying into the reaper (Harvest of Souls), purely visual.</summary>
    public class SoulWisp : MonoBehaviour
    {
        Transform _to; float _t;
        Vector3 _from, _bend;
        public static void Spawn(Vector3 from, Transform to, Color c)
        {
            if (to == null) return;
            var go = new GameObject("SoulWisp");
            go.transform.position = from;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.35f; tr.widthMultiplier = 0.22f; tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            tr.sharedMaterial = Fx.Instance ? Fx.Instance.additiveMaterial : null;
            tr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 1) }, alphaKeys = new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) } };
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = 2.5f; l.intensity = 2f; l.shadows = LightShadows.None;
            var w = go.AddComponent<SoulWisp>(); w._to = to; w._from = from; w._bend = Vector3.up * Random.Range(1.2f, 2.2f) + Random.insideUnitSphere;
        }

        void Update()
        {
            if (_to == null) { Destroy(gameObject); return; }
            _t += Time.deltaTime / 0.55f;
            Vector3 end = _to.position + Vector3.up * 1.2f;
            Vector3 mid = (_from + end) * 0.5f + _bend;
            float t = Mathf.Clamp01(_t);
            transform.position = Vector3.Lerp(Vector3.Lerp(_from, mid, t), Vector3.Lerp(mid, end, t), t);
            if (_t >= 1f) { SpellFx.Sparkles(end, 0.5f); Destroy(gameObject, 0.3f); enabled = false; }
        }
    }
}
