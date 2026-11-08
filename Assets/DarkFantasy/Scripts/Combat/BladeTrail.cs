using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Sword swing trail: a fading strip swept between the blade's base and tip over the last
    /// few hundredths of a second (the classic action-game "slash arc"). Emits only while
    /// CharacterAnimator reports the real swing frames, so it matches the hit window exactly.
    /// Rendered in world space by a hidden child mesh.
    /// </summary>
    public class BladeTrail : MonoBehaviour
    {
        public Transform basePoint;
        public Transform tipPoint;
        public Material material;
        public float lifetime = 0.22f;
        public Color color = new Color(1f, 0.9f, 0.72f, 0.9f);
        [Tooltip("HDR boost so the arc reads through the dark grading.")]
        public float intensity = 1.9f;
        [Tooltip("Subdivisions between stored samples (smooth curves at low framerates).")]
        public int smoothing = 3;

        public bool emitting { get; set; }

        struct Sample { public Vector3 a, b; public float time; }
        readonly List<Sample> _samples = new List<Sample>();
        Mesh _mesh;
        GameObject _renderGo;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _t = new List<int>();

        void Awake()
        {
            _mesh = new Mesh { name = "BladeTrail" };
            _mesh.MarkDynamic();
            _renderGo = new GameObject("BladeTrailMesh");
            _renderGo.hideFlags = HideFlags.DontSave;
            _renderGo.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _renderGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void OnDestroy() { if (_renderGo) Destroy(_renderGo); }
        void OnDisable() { _samples.Clear(); if (_mesh) _mesh.Clear(); }

        void LateUpdate()
        {
            if (basePoint == null || tipPoint == null) return;
            float now = Time.time;
            if (emitting) _samples.Add(new Sample { a = basePoint.position, b = tipPoint.position, time = now });
            while (_samples.Count > 0 && now - _samples[0].time > lifetime) _samples.RemoveAt(0);
            Rebuild(now);
        }

        void Rebuild(float now)
        {
            _mesh.Clear();
            if (_samples.Count < 2) return;
            _v.Clear(); _c.Clear(); _uv.Clear(); _t.Clear();
            int count = _samples.Count;
            for (int i = 0; i < count - 1; i++)
            {
                var s0 = _samples[Mathf.Max(0, i - 1)]; var s1 = _samples[i]; var s2 = _samples[i + 1]; var s3 = _samples[Mathf.Min(count - 1, i + 2)];
                int steps = i == count - 2 ? smoothing + 1 : smoothing;
                for (int k = 0; k < steps; k++)
                {
                    float u = k / (float)smoothing;
                    Vector3 a = CatmullRom(s0.a, s1.a, s2.a, s3.a, u);
                    Vector3 b = CatmullRom(s0.b, s1.b, s2.b, s3.b, u);
                    float time = Mathf.Lerp(s1.time, s2.time, u);
                    float age = Mathf.Clamp01((now - time) / lifetime);
                    float alpha = (1f - age) * (1f - age);
                    // the strip is brighter near the tip; the freshest part is near-white (hot edge)
                    float hot = Mathf.Clamp01(1f - age * 3f);
                    Color c = Color.Lerp(color, Color.white, hot * 0.6f) * intensity;
                    _v.Add(a); _c.Add(new Color(c.r, c.g, c.b, color.a * alpha * 0.12f)); _uv.Add(new Vector2(age, 0));
                    _v.Add(b); _c.Add(new Color(c.r, c.g, c.b, color.a * alpha)); _uv.Add(new Vector2(age, 1));
                }
            }
            for (int i = 0; i < _v.Count / 2 - 1; i++)
            {
                int i0 = i * 2;
                _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2);
                _t.Add(i0 + 2); _t.Add(i0 + 1); _t.Add(i0 + 3);
            }
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetUVs(0, _uv);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
