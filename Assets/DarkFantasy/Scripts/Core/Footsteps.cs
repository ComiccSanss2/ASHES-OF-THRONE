using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Footsteps synced to the animation: a step plays exactly when a foot bone comes down to its
    /// resting height (works for any clip, speed or character size). The surface under the foot
    /// picks the sound: stone floors, earth and grass outside, gravel, or shallow water (splash,
    /// ripples and a wading sound). Pitch follows the character's size.
    /// </summary>
    public class Footsteps : MonoBehaviour
    {
        public enum Surface : byte { Stone, Dirt, Gravel, Water }

        public Transform footL, footR;
        public float scale = 1f;
        public float volume = 1f;
        [Tooltip("Pitch multiplier (heavier characters lower).")]
        public float pitch = 1f;

        float _restL, _restR;
        bool _upL = true, _upR = true;
        Vector3 _last;
        float _speed;
        bool _wasInWater;
        PlayerController _pc;
        bool IsLocal => _pc != null && _pc.IsOwner;
        static readonly Dictionary<Collider, Surface> SurfaceCache = new Dictionary<Collider, Surface>();

        void Start()
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim)
            {
                if (!footL) footL = FindDeep(anim.transform, "Foot_L");
                if (!footR) footR = FindDeep(anim.transform, "Foot_R");
            }
            _restL = footL ? footL.position.y - transform.position.y : 0.1f;
            _restR = footR ? footR.position.y - transform.position.y : 0.1f;
            _restL = Mathf.Clamp(_restL, 0.02f * scale, 0.25f * scale);
            _restR = Mathf.Clamp(_restR, 0.02f * scale, 0.25f * scale);
            _last = transform.position;
            _pc = GetComponent<PlayerController>();
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        void LateUpdate()
        {
            Vector3 p = transform.position;
            Vector3 d = p - _last; d.y = 0f;
            _last = p;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float v = d.magnitude / dt;
            if (v > 30f) { _speed = 0f; return; }   // teleport / respawn
            _speed = Mathf.Lerp(_speed, v, 1f - Mathf.Exp(-dt * 12f));

            // walking into water: one big splash
            bool inWater = WaterArea.Find(p + Vector3.up * 0.1f, out float surf);
            if (inWater && !_wasInWater && _speed > 1f) Splash(new Vector3(p.x, surf + 0.02f, p.z), 1.3f, true);
            _wasInWater = inWater;

            if (_speed < 0.35f) { _upL = _upR = true; return; }
            Foot(footL, _restL, ref _upL);
            Foot(footR, _restR, ref _upR);
        }

        void Foot(Transform f, float rest, ref bool up)
        {
            if (!f) return;
            float h = f.position.y - transform.position.y;
            if (up && h < rest + 0.035f * scale) { up = false; Step(f.position); }
            else if (!up && h > rest + 0.08f * scale) up = true;
        }

        void Step(Vector3 at)
        {
            float run = Mathf.InverseLerp(2f, 6f, _speed);
            float vol = volume * (IsLocal ? 0.55f : 0.42f) * Mathf.Lerp(0.8f, 1.15f, run);
            float pit = pitch / Mathf.Sqrt(Mathf.Max(0.5f, scale));
            if (WaterArea.Find(at + Vector3.up * 0.1f, out float surf))
            {
                Splash(new Vector3(at.x, surf + 0.02f, at.z), Mathf.Lerp(0.6f, 1.4f, run), false);
                Sfx.Play(SfxId.StepWater, at, vol * 1.15f, pit * Random.Range(0.95f, 1.08f));
                return;
            }
            var s = SurfaceAt(at);
            var id = s == Surface.Dirt ? SfxId.StepDirt : s == Surface.Gravel ? SfxId.StepGravel : SfxId.StepStone;
            Sfx.Play(id, at, vol, pit);
        }

        void Splash(Vector3 at, float strength, bool entry)
        {
            Fx.Spawn(FxId.Splash, at, Vector3.up * strength * scale);
            Fx.Spawn(FxId.Ripple, at, Vector3.up * scale);
            if (entry) Sfx.Play(SfxId.WaterStep, at, 0.7f * volume, Random.Range(0.85f, 1f) / Mathf.Sqrt(scale));
        }

        public static Surface SurfaceAt(Vector3 at)
        {
            if (!Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var hit, 1.6f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return Surface.Dirt;
            if (SurfaceCache.TryGetValue(hit.collider, out var s)) return s;
            s = Surface.Stone;
            var r = hit.collider.GetComponent<Renderer>();
            string m = r && r.sharedMaterial ? r.sharedMaterial.name : "";
            if (m.Contains("Ground") || m.Contains("Grass") || m.Contains("Moss") || m.Contains("Ash")) s = Surface.Dirt;
            else if (m.Contains("Gravel") || m.Contains("Path") || m.Contains("Cobble") || m.Contains("Rubble")) s = Surface.Gravel;
            SurfaceCache[hit.collider] = s;
            return s;
        }
    }
}
