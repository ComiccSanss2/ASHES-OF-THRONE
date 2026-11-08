using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Visual of the boss's flying sword wave: a curved wall of ash (phase 1) or fire (phase 2)
    /// that bulges forward and travels along the ground. The server moves an identical
    /// invisible hit segment with the same speed, so what you see is what hits you:
    /// roll through it or get out of its width.
    /// </summary>
    public class AshCrescent : MonoBehaviour
    {
        float _speed, _maxDist, _travelled, _width;
        Vector3 _dir;
        Mesh _mesh;
        MeshRenderer _mr;
        Color _color;
        ParticleSystem _embers;
        Light _light;

        public static void Spawn(Vector3 pos, Vector3 dir, float speed, float maxDist, float width, bool fire)
        {
            if (Fx.Instance == null || Fx.Instance.slashMaterial == null) return;
            var go = new GameObject("AshCrescent");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(MathUtil.Flat(dir).normalized);
            var c = go.AddComponent<AshCrescent>();
            c.Init(MathUtil.Flat(dir).normalized, speed, maxDist, width, fire);
        }

        void Init(Vector3 dir, float speed, float maxDist, float width, bool fire)
        {
            _dir = dir; _speed = speed; _maxDist = maxDist; _width = width;
            _color = fire ? new Color(2.6f, 1.05f, 0.32f, 1f) : new Color(1.5f, 1.42f, 1.3f, 1f);
            _mesh = BuildMesh(width, fire ? 1.9f : 1.6f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = Fx.Instance.slashMaterial;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            SetAlpha(1f);

            // trail of embers / ash left behind
            var eg = new GameObject("Embers"); eg.transform.SetParent(transform, false); eg.transform.localPosition = new Vector3(0, 0.6f, -0.3f);
            _embers = eg.AddComponent<ParticleSystem>();
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _embers.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, fire ? 0.22f : 0.12f);
            main.startColor = fire ? new ParticleSystem.MinMaxGradient(BossFx.FireCore, BossFx.FireEdge) : new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.78f, 0.7f), new Color(0.5f, 0.48f, 0.45f));
            main.gravityModifier = -0.1f;
            main.maxParticles = 300;
            var em = _embers.emission; em.rateOverTime = 0; em.rateOverDistance = fire ? 14f : 8f;
            var sh = _embers.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(width * 0.8f, 1.2f, 0.2f);
            eg.GetComponent<ParticleSystemRenderer>().material = Fx.Instance.additiveMaterial;
            _embers.Play();

            var lg = new GameObject("Light"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0, 1f, 0.3f);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point; _light.range = 7f; _light.intensity = fire ? 22f : 10f;
            _light.color = fire ? BossFx.FireEdge : new Color(0.7f, 0.75f, 0.9f);
        }

        /// <summary>Curved band: an arc in the horizontal plane bulging forward, extruded upward.</summary>
        static Mesh BuildMesh(float width, float height)
        {
            var m = new Mesh { name = "Crescent" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
            const int seg = 20;
            float halfAngle = 62f * Mathf.Deg2Rad;
            float radius = width * 0.5f / Mathf.Sin(halfAngle);
            float centerZ = -radius * Mathf.Cos(halfAngle);   // arc ends at z = 0, middle bulges forward
            for (int i = 0; i <= seg; i++)
            {
                float k = i / (float)seg;
                float a = Mathf.Lerp(-halfAngle, halfAngle, k);
                var p = new Vector3(Mathf.Sin(a) * radius, 0f, centerZ + Mathf.Cos(a) * radius);
                float end = Mathf.Sin(k * Mathf.PI);                     // fades at the tips
                float h = height * (0.55f + 0.45f * end);                // taller in the middle
                float lean = 0.35f * end;                                // leans forward like a breaking wave
                v.Add(p); uv.Add(new Vector2(k, 0)); col.Add(new Color(1, 1, 1, end));
                v.Add(p + new Vector3(0, h, lean)); uv.Add(new Vector2(k, 1)); col.Add(new Color(1, 1, 1, end));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                tri.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });      // front
                tri.AddRange(new[] { a, a + 2, a + 1, a + 2, a + 3, a + 1 });      // back
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        void SetAlpha(float a)
        {
            var cols = new Color[_mesh.vertexCount];
            var src = _mesh.colors;
            for (int i = 0; i < cols.Length; i++)
            {
                int k = i / 2; float end = Mathf.Sin(k / 20f * Mathf.PI);
                cols[i] = new Color(_color.r, _color.g, _color.b, end * a);
            }
            _mesh.colors = cols;
        }

        void Update()
        {
            float step = _speed * Time.deltaTime;
            _travelled += step;
            transform.position += _dir * step;
            float k = Mathf.Clamp01(_travelled / _maxDist);
            transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.25f, k);
            float a = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
            SetAlpha(a);
            if (_light) _light.intensity *= 0.995f;
            if (_travelled >= _maxDist)
            {
                if (_embers) { _embers.transform.SetParent(null, true); var em = _embers.emission; em.rateOverDistance = 0; Destroy(_embers.gameObject, 1.5f); }
                Destroy(gameObject);
            }
        }

        void OnDestroy() { if (_mesh) Destroy(_mesh); }
    }
}
