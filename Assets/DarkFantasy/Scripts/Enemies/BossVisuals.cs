using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The Ashen King's presence (client side, added at runtime by BossAI):
    ///  - phase 1: ash constantly flaking off his armour, cold glint in the visor;
    ///  - phase 2 ("Ignite"): the greatsword bursts into flame (fire along the blade + light),
    ///    embers swirl around him, the armour smoulders from within, eyes burn red, the blade
    ///    trail turns to fire.
    /// </summary>
    public class BossVisuals : MonoBehaviour
    {
        CharacterAnimator _anim;
        WeaponHitbox _weapon;
        Transform _head;
        readonly List<GameObject> _phase2 = new List<GameObject>();
        GameObject _ash, _eyes;
        bool _ignited;
        Color _trailColor; float _trailIntensity;

        public void Setup(CharacterAnimator anim, WeaponHitbox weapon, Transform head)
        {
            _anim = anim; _weapon = weapon; _head = head;
            if (_anim && _anim.weaponTrail) { _trailColor = _anim.weaponTrail.color; _trailIntensity = _anim.weaponTrail.intensity; }
            BuildAsh();
            BuildEyes(false);
        }

        float Scale => transform.lossyScale.y;

        void BuildAsh()
        {
            if (_ash || Fx.Instance == null) return;
            _ash = new GameObject("AshFlakes");
            _ash.transform.SetParent(transform, false);
            _ash.transform.localPosition = new Vector3(0, 2.2f, 0);
            var ps = _ash.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.52f, 0.48f, 0.8f), new Color(0.3f, 0.28f, 0.26f, 0.6f));
            main.gravityModifier = 0.03f;
            main.maxParticles = 200;
            var em = ps.emission; em.rateOverTime = 14f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.9f;
            var no = ps.noise; no.enabled = true; no.strength = 0.4f; no.frequency = 0.5f;
            _ash.GetComponent<ParticleSystemRenderer>().material = Fx.Instance.alphaMaterial;
            ps.Play();
        }

        void BuildEyes(bool fire)
        {
            if (_eyes) Destroy(_eyes);
            if (_head == null) return;
            _eyes = new GameObject("EyeGlow");
            _eyes.transform.SetParent(_head, false);
            _eyes.transform.localPosition = Vector3.zero;
            var l = _eyes.AddComponent<Light>();
            l.type = LightType.Point; l.shadows = LightShadows.None;
            l.color = fire ? new Color(1f, 0.2f, 0.05f) : new Color(0.65f, 0.7f, 0.85f);
            l.intensity = fire ? 9f : 3f; l.range = fire ? 4f : 2.5f;
        }

        /// <summary>Phase 2 transformation. Instant = late joiner / already enraged.</summary>
        public void Ignite(bool instant)
        {
            if (_ignited) return;
            _ignited = true;
            if (Fx.Instance == null) return;

            // flames along the blade
            if (_weapon && _weapon.basePoint && _weapon.tipPoint)
            {
                var sword = _weapon.tipPoint.parent;
                Vector3 a = _weapon.basePoint.localPosition, b = _weapon.tipPoint.localPosition;
                var fgo = new GameObject("BladeFire");
                fgo.transform.SetParent(sword, false);
                fgo.transform.localPosition = (a + b) * 0.5f;
                fgo.transform.localRotation = Quaternion.identity;
                var ps = fgo.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Shape;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.5f);
                main.startColor = new ParticleSystem.MinMaxGradient(BossFx.FireCore, BossFx.FireEdge);
                main.gravityModifier = -0.6f;
                main.maxParticles = 400;
                var em = ps.emission; em.rateOverTime = 110f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.08f, Mathf.Abs(b.y - a.y), 0.08f);
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.5f, 0.12f, 0.04f), 1) },
                          new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0, 1) });
                col.color = g;
                var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
                fgo.GetComponent<ParticleSystemRenderer>().material = Fx.Instance.additiveMaterial;
                ps.Play();
                _phase2.Add(fgo);

                var lg = new GameObject("BladeLight");
                lg.transform.SetParent(sword, false);
                lg.transform.localPosition = Vector3.Lerp(a, b, 0.65f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.color = BossFx.FireEdge; l.intensity = 30f; l.range = 9f; l.shadows = LightShadows.None;
                lg.AddComponent<FlickerLight>();
                _phase2.Add(lg);
            }

            // embers swirling around the body
            var ego = new GameObject("EmberAura");
            ego.transform.SetParent(transform, false);
            ego.transform.localPosition = new Vector3(0, 1.6f, 0);
            var eps = ego.AddComponent<ParticleSystem>();
            eps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var em2 = eps.main;
            em2.loop = true; em2.simulationSpace = ParticleSystemSimulationSpace.World;
            em2.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.4f);
            em2.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            em2.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.13f);
            em2.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.25f, 0.05f));
            em2.gravityModifier = -0.12f;
            em2.maxParticles = 300;
            var erate = eps.emission; erate.rateOverTime = 45f;
            var esh = eps.shape; esh.shapeType = ParticleSystemShapeType.Sphere; esh.radius = 1.1f;
            var vel = eps.velocityOverLifetime; vel.enabled = true; vel.orbitalY = 1.2f; vel.space = ParticleSystemSimulationSpace.Local;
            var noise = eps.noise; noise.enabled = true; noise.strength = 0.6f;
            ego.GetComponent<ParticleSystemRenderer>().material = Fx.Instance.additiveMaterial;
            eps.Play();
            _phase2.Add(ego);

            // smouldering armour, burning eyes, fire trail
            if (_anim)
            {
                _anim.SetBaseEmission(new Color(0.16f, 0.04f, 0.008f));
                if (_anim.weaponTrail) { _anim.weaponTrail.color = new Color(1f, 0.5f, 0.12f, 1f); _anim.weaponTrail.intensity = 3.2f; _anim.weaponTrail.lifetime = 0.3f; }
            }
            BuildEyes(true);
            if (_ash) { var m = _ash.GetComponent<ParticleSystem>().main; m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.25f, 0.22f, 0.8f), new Color(0.15f, 0.12f, 0.1f, 0.6f)); }

            if (!instant)
            {
                BossFx.FireBurst(transform.position + Vector3.up * 2f, 3.5f);
                Sfx.Play(SfxId.FireBurst, transform.position + Vector3.up * 2f, 1.2f);
            }
        }

        public void Extinguish()
        {
            _ignited = false;
            foreach (var g in _phase2) if (g) Destroy(g);
            _phase2.Clear();
            if (_anim)
            {
                _anim.SetBaseEmission(Color.black);
                if (_anim.weaponTrail) { _anim.weaponTrail.color = _trailColor; _anim.weaponTrail.intensity = _trailIntensity; _anim.weaponTrail.lifetime = 0.22f; }
            }
            BuildEyes(false);
        }
    }
}
