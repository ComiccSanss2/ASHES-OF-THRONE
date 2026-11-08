using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy.Procedural
{
    /// <summary>Rumore deterministico (stesso risultato su host e client e ad ogni rigenerazione).</summary>
    public static class Noise
    {
        public static float Hash(int n)
        {
            unchecked
            {
                n = (n << 13) ^ n;
                int m = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
                return m / 2147483647f;
            }
        }

        public static float Hash(int x, int y, int seed = 0) => Hash(x * 73856093 ^ y * 19349663 ^ seed * 83492791);
        public static float Hash(int x, int y, int z, int seed) => Hash(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed * 2654435);

        static float S(float t) => t * t * (3f - 2f * t);

        public static float Value2(float x, float y, int seed = 0)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = S(x - xi), yf = S(y - yi);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, xf), Mathf.Lerp(c, d, xf), yf);
        }

        public static float Value3(Vector3 p, int seed = 0)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float xf = S(p.x - xi), yf = S(p.y - yi), zf = S(p.z - zi);
            float c000 = Hash(xi, yi, zi, seed), c100 = Hash(xi + 1, yi, zi, seed);
            float c010 = Hash(xi, yi + 1, zi, seed), c110 = Hash(xi + 1, yi + 1, zi, seed);
            float c001 = Hash(xi, yi, zi + 1, seed), c101 = Hash(xi + 1, yi, zi + 1, seed);
            float c011 = Hash(xi, yi + 1, zi + 1, seed), c111 = Hash(xi + 1, yi + 1, zi + 1, seed);
            float x00 = Mathf.Lerp(c000, c100, xf), x10 = Mathf.Lerp(c010, c110, xf);
            float x01 = Mathf.Lerp(c001, c101, xf), x11 = Mathf.Lerp(c011, c111, xf);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, yf), Mathf.Lerp(x01, x11, yf), zf);
        }

        public static float Fbm2(float x, float y, int octaves, int seed = 0)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value2(x, y, seed + i * 17) * amp;
                norm += amp; amp *= 0.5f; x *= 2.03f; y *= 2.03f;
            }
            return sum / norm;
        }

        public static float Fbm3(Vector3 p, int octaves, int seed = 0)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value3(p, seed + i * 31) * amp;
                norm += amp; amp *= 0.5f; p *= 2.03f;
            }
            return sum / norm;
        }
    }

    /// <summary>
    /// Piccolo costruttore di mesh. Ogni quad/triangolo ha vertici propri (shading faccettato,
    /// tipico del low-poly PS2) e UV proiettate "a scatola" in metri, così le texture tile
    /// hanno la stessa densità di texel su tutte le superfici.
    /// L'orientamento (winding) viene corretto automaticamente rispetto alla normale indicata.
    /// </summary>
    public class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();
        public float uvScale = 0.5f;      // 1/metri per ripetizione texture (0.5 = una ripetizione ogni 2 m)
        public Color color = Color.white;  // colore vertice (AO "baked" semplice)

        public Vector2 BoxUV(Vector3 p, Vector3 n)
        {
            Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z) * uvScale;
            if (a.x >= a.z) return new Vector2(p.z, p.y) * uvScale;
            return new Vector2(p.x, p.y) * uvScale;
        }

        public void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0) { var t = b; b = c; c = t; n = -n; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            UV.Add(BoxUV(a, n)); UV.Add(BoxUV(b, n)); UV.Add(BoxUV(c, n));
            C.Add(color); C.Add(color); C.Add(color);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        public void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0) { var t = b; b = c; c = t; var tu = ub; ub = uc; uc = tu; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            UV.Add(ua); UV.Add(ub); UV.Add(uc);
            C.Add(color); C.Add(color); C.Add(color);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        /// <summary>Quad a,b,c,d in ordine perimetrale (qualsiasi verso).</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            AddTri(a, b, c, outward);
            AddTri(a, c, d, outward);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            AddTri(a, b, c, outward, ua, ub, uc);
            AddTri(a, c, d, outward, ua, uc, ud);
        }

        /// <summary>Quad con normale calcolata automaticamente (usa l'ordine dato).</summary>
        public void AddQuadAuto(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            AddQuad(a, b, c, d, n);
        }

        public void Append(MeshBuilder other, Matrix4x4 m)
        {
            int baseIndex = V.Count;
            for (int i = 0; i < other.V.Count; i++)
            {
                V.Add(m.MultiplyPoint3x4(other.V[i]));
                UV.Add(other.UV[i]);
                C.Add(other.C[i]);
            }
            bool flip = m.determinant < 0;
            for (int i = 0; i < other.T.Count; i += 3)
            {
                if (flip) { T.Add(baseIndex + other.T[i]); T.Add(baseIndex + other.T[i + 2]); T.Add(baseIndex + other.T[i + 1]); }
                else { T.Add(baseIndex + other.T[i]); T.Add(baseIndex + other.T[i + 1]); T.Add(baseIndex + other.T[i + 2]); }
            }
        }

        /// <summary>Prisma (cilindro low-poly) tra due punti, con raggi diversi e rotazione irregolare.</summary>
        public void AddPrism(Vector3 from, Vector3 to, float r0, float r1, int sides, bool capBottom, bool capTop, int seed = 0, float jitter = 0f)
        {
            Vector3 axis = (to - from).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 fwd = Vector3.Cross(side, axis);
            var ring0 = new Vector3[sides];
            var ring1 = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f + seed * 0.37f;
                float j0 = 1f + (Noise.Hash(i, seed, 1) - 0.5f) * jitter;
                float j1 = 1f + (Noise.Hash(i, seed, 2) - 0.5f) * jitter;
                Vector3 dir = side * Mathf.Cos(a) + fwd * Mathf.Sin(a);
                ring0[i] = from + dir * r0 * j0;
                ring1[i] = to + dir * r1 * j1;
            }
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                Vector3 mid = (ring0[i] + ring0[n] + ring1[i] + ring1[n]) * 0.25f;
                Vector3 centre = Vector3.Lerp(from, to, 0.5f);
                AddQuad(ring0[i], ring0[n], ring1[n], ring1[i], mid - centre);
            }
            if (capBottom)
                for (int i = 0; i < sides; i++) AddTri(from, ring0[i], ring0[(i + 1) % sides], -axis);
            if (capTop)
                for (int i = 0; i < sides; i++) AddTri(to, ring1[i], ring1[(i + 1) % sides], axis);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(V);
            mesh.SetUVs(0, UV);
            mesh.SetColors(C);
            mesh.SetTriangles(T, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
