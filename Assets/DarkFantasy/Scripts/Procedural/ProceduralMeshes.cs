using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy.Procedural
{
    /// <summary>
    /// Generatori di mesh low-poly "organiche" per il dark fantasy: nessun cubo puro, ogni
    /// superficie è suddivisa e leggermente deformata (pietra consumata, legno storto).
    /// Usati dal builder dell'editor per creare gli asset in Assets/DarkFantasy/Generated/Meshes.
    /// </summary>
    public static class ProceduralMeshes
    {
        // ================================================================== TERRENO
        /// <summary>Altezza del terreno del livello (usata anche per piazzare gli oggetti).</summary>
        public static float LevelHeight(float x, float z)
        {
            float h = (Noise.Fbm2(x * 0.045f, z * 0.045f, 4, 11) - 0.5f) * 3.2f;
            h += (Noise.Fbm2(x * 0.2f, z * 0.2f, 2, 5) - 0.5f) * 0.45f;
            // sentiero pianeggiante dal falò al castello
            float path = Mathf.Abs(x - Mathf.Sin(z * 0.06f) * 5f);
            float pathFlat = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(2.5f, 9f, path));
            h = Mathf.Lerp(h, -0.15f, pathFlat * 0.8f);
            // piazzale davanti al castello pianeggiante
            float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 18f, z));
            h = Mathf.Lerp(h, 0f, approach);
            // colline ai bordi (catino che contiene il giocatore)
            float ex = x / 46f, ez = (z + 25f) / 68f;
            float edge = Mathf.Sqrt(ex * ex + ez * ez);
            h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 1.08f, edge)) * (14f + Noise.Fbm2(x * 0.08f, z * 0.08f, 3, 3) * 10f);
            // impronta del castello (sala del trono x ±17, z 14..70): terreno sotto il pavimento
            float inX = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17.5f, 24f, Mathf.Abs(x)));
            float inZ = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 16f, z)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(69f, 76f, z)));
            float castleMask = inX * inZ;
            // castle interior beyond the throne room (corridors, prisons): flat ground under the floors
            float inX2 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(58f, 66f, Mathf.Abs(x)));
            float inZ2 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 67f, z));
            castleMask = Mathf.Max(castleMask, inX2 * inZ2);
            h = Mathf.Lerp(h, Mathf.Min(h, 0f), castleMask);
            if (castleMask > 0.99f) h = Mathf.Min(h, 0f);
            return h;
        }

        public static Mesh Terrain(float minX, float maxX, float minZ, float maxZ, float cell)
        {
            var mb = new MeshBuilder { uvScale = 0.22f };
            int nx = Mathf.CeilToInt((maxX - minX) / cell), nz = Mathf.CeilToInt((maxZ - minZ) / cell);
            var mesh = new Mesh { name = "Terrain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            for (int iz = 0; iz <= nz; iz++)
                for (int ix = 0; ix <= nx; ix++)
                {
                    float x = minX + ix * cell, z = minZ + iz * cell;
                    // jitter per evitare la griglia regolare
                    x += (Noise.Hash(ix, iz, 7) - 0.5f) * cell * 0.35f;
                    z += (Noise.Hash(ix, iz, 9) - 0.5f) * cell * 0.35f;
                    float y = LevelHeight(x, z);
                    verts.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(x, z) * mb.uvScale);
                    // AO dipinta: valli più scure
                    float ao = Mathf.Lerp(0.72f, 1f, Noise.Fbm2(x * 0.15f, z * 0.15f, 2, 21));
                    cols.Add(new Color(ao, ao, ao, 1));
                }
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int a = iz * (nx + 1) + ix, b = a + 1, c = a + nx + 1, d = c + 1;
                    if (((ix + iz) & 1) == 0) { tris.Add(a); tris.Add(c); tris.Add(d); tris.Add(a); tris.Add(d); tris.Add(b); }
                    else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                }
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // ================================================================== ROCCE
        public static Mesh Rock(int seed, float radius, float flatten = 0.65f)
        {
            // icosfera suddivisa una volta + displacement → sassi faccettati
            var (verts, faces) = Icosphere(1);
            var mb = new MeshBuilder { uvScale = 0.8f };
            var displaced = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                float n = Noise.Fbm3(v * 1.7f + Vector3.one * seed * 3.1f, 3, seed);
                Vector3 p = v * radius * (0.7f + n * 0.6f);
                p.y *= flatten;
                if (p.y < -radius * 0.25f) p.y = -radius * 0.25f; // base piatta, appoggiata a terra
                displaced[i] = p;
            }
            foreach (var f in faces)
            {
                Vector3 a = displaced[f.x], b = displaced[f.y], c = displaced[f.z];
                Vector3 centre = (a + b + c) / 3f;
                float ao = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(-radius * 0.25f, radius * 0.5f, centre.y));
                mb.color = new Color(ao, ao, ao, 1);
                mb.AddTri(a, b, c, centre);
            }
            return mb.Build("Rock_" + seed);
        }

        static (List<Vector3>, List<Vector3Int>) Icosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<Vector3Int>
            {
                new Vector3Int(0,11,5), new Vector3Int(0,5,1), new Vector3Int(0,1,7), new Vector3Int(0,7,10), new Vector3Int(0,10,11),
                new Vector3Int(1,5,9), new Vector3Int(5,11,4), new Vector3Int(11,10,2), new Vector3Int(10,7,6), new Vector3Int(7,1,8),
                new Vector3Int(3,9,4), new Vector3Int(3,4,2), new Vector3Int(3,2,6), new Vector3Int(3,6,8), new Vector3Int(3,8,9),
                new Vector3Int(4,9,5), new Vector3Int(2,4,11), new Vector3Int(6,2,10), new Vector3Int(8,6,7), new Vector3Int(9,8,1)
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    cache[key] = v.Count - 1;
                    return v.Count - 1;
                }
                var nf = new List<Vector3Int>();
                foreach (var tri in f)
                {
                    int a = Mid(tri.x, tri.y), b = Mid(tri.y, tri.z), c = Mid(tri.z, tri.x);
                    nf.Add(new Vector3Int(tri.x, a, c)); nf.Add(new Vector3Int(tri.y, b, a));
                    nf.Add(new Vector3Int(tri.z, c, b)); nf.Add(new Vector3Int(a, b, c));
                }
                f = nf;
            }
            return (v, f);
        }

        // ================================================================== MURA IN ROVINA
        /// <summary>Muro di pietra con la cima spezzata in modo irregolare e superfici deformate.</summary>
        public static Mesh RuinedWall(int seed, float length, float height, float thickness, float breakAmount = 0.6f)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            int cols = Mathf.Max(2, Mathf.CeilToInt(length / 0.5f));
            int rows = Mathf.Max(2, Mathf.CeilToInt(height / 0.6f));
            float half = thickness * 0.5f;
            // profilo della cima: crolli profondi e denti
            var top = new float[cols + 1];
            for (int i = 0; i <= cols; i++)
            {
                float u = i / (float)cols;
                float n = Noise.Fbm2(u * 4f + seed, seed * 0.3f, 3, seed);
                float bite = Mathf.Pow(Noise.Value2(u * 2.2f + seed * 1.3f, 0.5f, seed + 3), 2f);
                float end = Mathf.Min(u, 1 - u) < 0.12f ? 0.35f : 0f; // estremità più basse
                top[i] = height * (1f - breakAmount * Mathf.Clamp01(n * 0.6f + bite * 0.9f + end));
                top[i] = Mathf.Max(top[i], height * 0.18f);
                if (Noise.Hash(i, seed, 77) > 0.8f) top[i] -= 0.15f; // dente
            }
            Vector3 P(int i, float y, float side)
            {
                float x = -length * 0.5f + length * i / cols;
                float j = (Noise.Value2(i * 0.7f, y * 1.3f, seed + (side > 0 ? 1 : 2)) - 0.5f) * 0.09f;
                float bulge = Mathf.Sin(i / (float)cols * Mathf.PI) * 0.04f;
                return new Vector3(x + j * 0.5f, y, side * (half + j + bulge));
            }
            for (int i = 0; i < cols; i++)
            {
                for (int r = 0; r < rows; r++)
                {
                    float y0a = top[i] * r / rows, y1a = top[i] * (r + 1) / rows;
                    float y0b = top[i + 1] * r / rows, y1b = top[i + 1] * (r + 1) / rows;
                    float ao0 = r == 0 ? 0.6f : 0.85f + 0.15f * r / rows;
                    mb.color = new Color(ao0, ao0, ao0, 1);
                    // faccia frontale (+Z) e posteriore (-Z)
                    mb.AddQuad(Pr(i, y0a, 1), Pr(i + 1, y0b, 1), Pr(i + 1, y1b, 1), Pr(i, y1a, 1), Vector3.forward);
                    mb.AddQuad(Pr(i, y0a, -1), Pr(i + 1, y0b, -1), Pr(i + 1, y1b, -1), Pr(i, y1a, -1), Vector3.back);
                }
                mb.color = Color.white;
                // cima
                mb.AddQuad(Pr(i, top[i], 1), Pr(i + 1, top[i + 1], 1), Pr(i + 1, top[i + 1], -1), Pr(i, top[i], -1), Vector3.up);
            }
            // estremità
            for (int r = 0; r < rows; r++)
            {
                float y0 = top[0] * r / rows, y1 = top[0] * (r + 1) / rows;
                mb.AddQuad(Pr(0, y0, 1), Pr(0, y0, -1), Pr(0, y1, -1), Pr(0, y1, 1), Vector3.left);
                y0 = top[cols] * r / rows; y1 = top[cols] * (r + 1) / rows;
                mb.AddQuad(Pr(cols, y0, 1), Pr(cols, y0, -1), Pr(cols, y1, -1), Pr(cols, y1, 1), Vector3.right);
            }
            return mb.Build("RuinedWall_" + seed);

            Vector3 Pr(int i, float y, float side) => P(i, y, side);
        }

        // ================================================================== COLONNE
        public static Mesh Pillar(int seed, float radius, float height, bool broken, int sides = 8)
        {
            var mb = new MeshBuilder { uvScale = 0.6f };
            float baseH = 0.45f, capH = 0.5f;
            // plinto (ottagono più largo, bordi smussati)
            mb.color = new Color(0.65f, 0.65f, 0.65f);
            mb.AddPrism(Vector3.zero, Vector3.up * baseH * 0.6f, radius * 1.45f, radius * 1.45f, sides, false, true, seed, 0.05f);
            mb.AddPrism(Vector3.up * baseH * 0.6f, Vector3.up * baseH, radius * 1.45f, radius * 1.15f, sides, false, true, seed, 0.05f);
            float shaftTop = broken ? height * (0.45f + Noise.Hash(seed) * 0.35f) : height - capH;
            int segs = Mathf.Max(2, Mathf.CeilToInt((shaftTop - baseH) / 0.8f));
            for (int s = 0; s < segs; s++)
            {
                float y0 = Mathf.Lerp(baseH, shaftTop, s / (float)segs);
                float y1 = Mathf.Lerp(baseH, shaftTop, (s + 1) / (float)segs);
                float r0 = radius * (1f - 0.04f * s / segs), r1 = radius * (1f - 0.04f * (s + 1) / segs);
                mb.color = Color.Lerp(new Color(0.8f, 0.8f, 0.8f), Color.white, s / (float)segs);
                mb.AddPrism(Vector3.up * y0, Vector3.up * y1, r0, r1, sides, false, false, seed + s, 0.06f);
            }
            mb.color = Color.white;
            if (broken)
            {
                // cima spezzata: ventaglio di triangoli a quote diverse
                Vector3 c = Vector3.up * (shaftTop + 0.15f);
                Vector3 side = Vector3.right, fwd = Vector3.forward;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i / (float)sides * Mathf.PI * 2f + (seed + segs - 1) * 0.37f;
                    float a1 = (i + 1) / (float)sides * Mathf.PI * 2f + (seed + segs - 1) * 0.37f;
                    float h0 = (Noise.Hash(i, seed, 5) - 0.5f) * 0.5f, h1 = (Noise.Hash((i + 1) % sides, seed, 5) - 0.5f) * 0.5f;
                    Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius * 0.96f + Vector3.up * shaftTop;
                    Vector3 p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius * 0.96f + Vector3.up * shaftTop;
                    Vector3 q0 = p0 + Vector3.up * (h0 + 0.25f), q1 = p1 + Vector3.up * (h1 + 0.25f);
                    mb.AddQuad(p0, p1, q1, q0, (p0 + p1) * 0.5f - Vector3.up * shaftTop);
                    mb.AddTri(q0, q1, c + Vector3.up * (h0 + h1), Vector3.up);
                }
            }
            else
            {
                // capitello
                mb.AddPrism(Vector3.up * shaftTop, Vector3.up * (shaftTop + capH * 0.5f), radius, radius * 1.4f, sides, false, false, seed, 0.05f);
                mb.AddPrism(Vector3.up * (shaftTop + capH * 0.5f), Vector3.up * height, radius * 1.5f, radius * 1.5f, sides, true, true, seed, 0.04f);
            }
            return mb.Build("Pillar_" + seed + (broken ? "_broken" : ""));
        }

        // ================================================================== ARCO GOTICO
        /// <summary>Punto su un arco acuto (gotico) di luce "span", imposta a y=spring, chiave a y=apex.</summary>
        public static Vector2 PointedArch(float t, float span, float spring, float apex)
        {
            // t: 0 = imposta sinistra, 0.5 = chiave, 1 = imposta destra
            bool right = t > 0.5f;
            float u = right ? (1f - t) * 2f : t * 2f;  // 0..1 dall'imposta verso la chiave
            float a = u * Mathf.PI / 3f;               // arco a sesto acuto equilatero (60°)
            float x = span * 0.5f - span * Mathf.Cos(a);
            float y = span * Mathf.Sin(a);
            float scaleY = (apex - spring) / (span * 0.8660254f);
            x = right ? -x : x;
            return new Vector2(x, spring + y * scaleY);
        }

        /// <summary>
        /// Muro spesso con finestra (o porta) ad arco acuto. Larghezza W centrata in X, spessore lungo Z.
        /// winBottom = 0 → porta.
        /// </summary>
        public static Mesh GothicWall(int seed, float width, float height, float thickness, float winWidth, float winBottom, float winSpring, float winApex, bool withWindow = true)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            float hw = width * 0.5f, ht = thickness * 0.5f, ww = winWidth * 0.5f;
            int archSegs = 10;

            void Faces(float z, Vector3 n)
            {
                if (!withWindow)
                {
                    GridQuad(mb, new Vector3(-hw, 0, z), new Vector3(hw, 0, z), new Vector3(hw, height, z), new Vector3(-hw, height, z), n, 6, 5, seed);
                    return;
                }
                GridQuad(mb, new Vector3(-hw, 0, z), new Vector3(-ww, 0, z), new Vector3(-ww, height, z), new Vector3(-hw, height, z), n, 2, 5, seed);
                GridQuad(mb, new Vector3(ww, 0, z), new Vector3(hw, 0, z), new Vector3(hw, height, z), new Vector3(ww, height, z), n, 2, 5, seed + 1);
                if (winBottom > 0.01f)
                    GridQuad(mb, new Vector3(-ww, 0, z), new Vector3(ww, 0, z), new Vector3(ww, winBottom, z), new Vector3(-ww, winBottom, z), n, 2, 2, seed + 2);
                for (int i = 0; i < archSegs; i++)
                {
                    Vector2 p0 = PointedArch(i / (float)archSegs, winWidth, winSpring, winApex);
                    Vector2 p1 = PointedArch((i + 1) / (float)archSegs, winWidth, winSpring, winApex);
                    mb.AddQuad(new Vector3(p0.x, p0.y, z), new Vector3(p1.x, p1.y, z), new Vector3(p1.x, height, z), new Vector3(p0.x, height, z), n);
                }
            }
            Faces(ht, Vector3.forward);
            Faces(-ht, Vector3.back);

            // contorno superiore, laterale
            mb.AddQuad(new Vector3(-hw, height, ht), new Vector3(hw, height, ht), new Vector3(hw, height, -ht), new Vector3(-hw, height, -ht), Vector3.up);
            mb.AddQuad(new Vector3(-hw, 0, ht), new Vector3(-hw, height, ht), new Vector3(-hw, height, -ht), new Vector3(-hw, 0, -ht), Vector3.left);
            mb.AddQuad(new Vector3(hw, 0, ht), new Vector3(hw, height, ht), new Vector3(hw, height, -ht), new Vector3(hw, 0, -ht), Vector3.right);

            if (withWindow)
            {
                // sguinci (spessore dell'apertura)
                mb.color = new Color(0.7f, 0.7f, 0.7f);
                mb.AddQuad(new Vector3(-ww, winBottom, ht), new Vector3(-ww, winSpring, ht), new Vector3(-ww, winSpring, -ht), new Vector3(-ww, winBottom, -ht), Vector3.right);
                mb.AddQuad(new Vector3(ww, winBottom, ht), new Vector3(ww, winSpring, ht), new Vector3(ww, winSpring, -ht), new Vector3(ww, winBottom, -ht), Vector3.left);
                if (winBottom > 0.01f)
                    mb.AddQuad(new Vector3(-ww, winBottom, ht), new Vector3(ww, winBottom, ht), new Vector3(ww, winBottom, -ht), new Vector3(-ww, winBottom, -ht), Vector3.up);
                for (int i = 0; i < archSegs; i++)
                {
                    Vector2 p0 = PointedArch(i / (float)archSegs, winWidth, winSpring, winApex);
                    Vector2 p1 = PointedArch((i + 1) / (float)archSegs, winWidth, winSpring, winApex);
                    Vector2 mid = (p0 + p1) * 0.5f;
                    Vector3 inward = new Vector3(-mid.x, winSpring - mid.y, 0);
                    mb.AddQuad(new Vector3(p0.x, p0.y, ht), new Vector3(p1.x, p1.y, ht), new Vector3(p1.x, p1.y, -ht), new Vector3(p0.x, p0.y, -ht), inward);
                }
                mb.color = Color.white;
                // cornice dell'arco in rilievo (conci) sul lato +Z
                float fr = 0.22f;
                for (int i = 0; i < archSegs; i++)
                {
                    Vector2 a0 = PointedArch(i / (float)archSegs, winWidth, winSpring, winApex);
                    Vector2 a1 = PointedArch((i + 1) / (float)archSegs, winWidth, winSpring, winApex);
                    Vector2 b0 = PointedArch(i / (float)archSegs, winWidth + fr * 2, winSpring, winApex + fr);
                    Vector2 b1 = PointedArch((i + 1) / (float)archSegs, winWidth + fr * 2, winSpring, winApex + fr);
                    float z = ht + 0.08f + (Noise.Hash(i, seed, 3) - 0.5f) * 0.03f;
                    mb.AddQuad(new Vector3(a0.x, a0.y, z), new Vector3(a1.x, a1.y, z), new Vector3(b1.x, b1.y, z), new Vector3(b0.x, b0.y, z), Vector3.forward);
                    mb.AddQuad(new Vector3(b0.x, b0.y, ht), new Vector3(b1.x, b1.y, ht), new Vector3(b1.x, b1.y, z), new Vector3(b0.x, b0.y, z), new Vector3(b0.x, b0.y - winSpring, 0));
                }
            }
            return mb.Build("GothicWall_" + seed);
        }

        /// <summary>Quad suddiviso a griglia con lieve deformazione (pietra irregolare).</summary>
        static void GridQuad(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, int nu, int nv, int seed)
        {
            Vector3 Pt(int i, int j)
            {
                float u = i / (float)nu, v = j / (float)nv;
                Vector3 p = Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);
                bool edge = i == 0 || j == 0 || i == nu || j == nv;
                if (!edge) p += n.normalized * (Noise.Value3(p * 1.3f, seed) - 0.5f) * 0.12f;
                return p;
            }
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                    mb.AddQuad(Pt(i, j), Pt(i + 1, j), Pt(i + 1, j + 1), Pt(i, j + 1), n);
        }

        /// <summary>Portale ad arco (ghiera spessa) usato per l'ingresso del castello.</summary>
        public static Mesh ArchFrame(int seed, float span, float spring, float apex, float frame, float depth)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            int segs = 14;
            float hd = depth * 0.5f;
            // piedritti
            mb.AddPrism(new Vector3(-span * 0.5f - frame * 0.5f, 0, 0), new Vector3(-span * 0.5f - frame * 0.5f, spring, 0), frame * 0.72f, frame * 0.68f, 6, false, false, seed, 0.06f);
            mb.AddPrism(new Vector3(span * 0.5f + frame * 0.5f, 0, 0), new Vector3(span * 0.5f + frame * 0.5f, spring, 0), frame * 0.72f, frame * 0.68f, 6, false, false, seed + 1, 0.06f);
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                Vector2 a0 = PointedArch(t0, span, spring, apex), a1 = PointedArch(t1, span, spring, apex);
                Vector2 b0 = PointedArch(t0, span + frame * 2, spring, apex + frame), b1 = PointedArch(t1, span + frame * 2, spring, apex + frame);
                float j = (Noise.Hash(i, seed, 9) - 0.5f) * 0.06f;
                Vector3 A0 = new Vector3(a0.x, a0.y, hd + j), A1 = new Vector3(a1.x, a1.y, hd + j), B0 = new Vector3(b0.x, b0.y, hd + j), B1 = new Vector3(b1.x, b1.y, hd + j);
                Vector3 A0b = new Vector3(a0.x, a0.y, -hd), A1b = new Vector3(a1.x, a1.y, -hd), B0b = new Vector3(b0.x, b0.y, -hd), B1b = new Vector3(b1.x, b1.y, -hd);
                mb.AddQuad(A0, A1, B1, B0, Vector3.forward);
                mb.AddQuad(A0b, A1b, B1b, B0b, Vector3.back);
                Vector2 mid = (a0 + a1) * 0.5f;
                mb.AddQuad(A0, A1, A1b, A0b, new Vector3(-mid.x, spring - mid.y, 0));
                Vector2 midb = (b0 + b1) * 0.5f;
                mb.AddQuad(B0, B1, B1b, B0b, new Vector3(midb.x, midb.y - spring, 0));
            }
            return mb.Build("ArchFrame_" + seed);
        }

        // ================================================================== PAVIMENTO
        public static Mesh FlagstoneFloor(int seed, float width, float depth, float cell)
        {
            var mb = new MeshBuilder { uvScale = 0.25f };
            int nx = Mathf.CeilToInt(width / cell), nz = Mathf.CeilToInt(depth / cell);
            float H(int i, int j) => (Noise.Hash(i, j, seed) - 0.5f) * 0.05f;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float x0 = -width * 0.5f + i * cell, x1 = x0 + cell, z0 = j * cell, z1 = z0 + cell;
                    float ao = Mathf.Lerp(0.75f, 1f, Noise.Hash(i, j, seed + 4));
                    mb.color = new Color(ao, ao, ao);
                    mb.AddQuad(new Vector3(x0, H(i, j), z0), new Vector3(x1, H(i + 1, j), z0), new Vector3(x1, H(i + 1, j + 1), z1), new Vector3(x0, H(i, j + 1), z1), Vector3.up);
                }
            return mb.Build("Floor_" + seed);
        }

        // ================================================================== GRADINI / DAIS
        public static Mesh Dais(int seed, float radius, int steps, float stepH, float stepDepth, int sides = 8)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            for (int s = 0; s < steps; s++)
            {
                float r = radius - s * stepDepth;
                mb.color = Color.Lerp(new Color(0.7f, 0.7f, 0.7f), Color.white, s / (float)steps);
                mb.AddPrism(Vector3.up * (s * stepH), Vector3.up * ((s + 1) * stepH), r, r * 0.99f, sides, false, true, seed + s, 0.03f);
            }
            return mb.Build("Dais_" + seed);
        }

        // ================================================================== TRONO
        public static Mesh Throne(int seed)
        {
            var mb = new MeshBuilder { uvScale = 0.9f };
            // seduta
            Box(mb, new Vector3(0, 0.25f, 0), new Vector3(1.6f, 0.5f, 1.2f), seed);
            Box(mb, new Vector3(0, 0.6f, 0.05f), new Vector3(1.4f, 0.2f, 1.0f), seed + 1);
            // braccioli
            Box(mb, new Vector3(-0.75f, 0.95f, 0.05f), new Vector3(0.22f, 0.5f, 1.1f), seed + 2);
            Box(mb, new Vector3(0.75f, 0.95f, 0.05f), new Vector3(0.22f, 0.5f, 1.1f), seed + 3);
            // schienale a punta gotica
            float bw = 1.5f, bz = -0.55f, t = 0.25f;
            int segs = 8;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = PointedArch(i / (float)segs, bw, 2.2f, 4.2f), p1 = PointedArch((i + 1) / (float)segs, bw, 2.2f, 4.2f);
                Vector3 a = new Vector3(p0.x, 0.7f, bz), b = new Vector3(p1.x, 0.7f, bz);
                Vector3 c = new Vector3(p1.x, p1.y, bz), d = new Vector3(p0.x, p0.y, bz);
                mb.AddQuad(a, b, c, d, Vector3.forward);
                mb.AddQuad(a + Vector3.back * t, b + Vector3.back * t, c + Vector3.back * t, d + Vector3.back * t, Vector3.back);
                mb.AddQuad(d, c, c + Vector3.back * t, d + Vector3.back * t, new Vector3((p0.x + p1.x) * 0.5f, (p0.y + p1.y) * 0.5f - 2.2f, 0));
            }
            mb.AddQuad(new Vector3(-bw * 0.5f, 0.7f, bz), new Vector3(-bw * 0.5f, 2.2f, bz), new Vector3(-bw * 0.5f, 2.2f, bz - t), new Vector3(-bw * 0.5f, 0.7f, bz - t), Vector3.left);
            mb.AddQuad(new Vector3(bw * 0.5f, 0.7f, bz), new Vector3(bw * 0.5f, 2.2f, bz), new Vector3(bw * 0.5f, 2.2f, bz - t), new Vector3(bw * 0.5f, 0.7f, bz - t), Vector3.right);
            // pinnacoli laterali
            mb.AddPrism(new Vector3(-0.9f, 0.5f, -0.62f), new Vector3(-0.9f, 3.0f, -0.62f), 0.12f, 0.07f, 4, false, false, seed, 0f);
            mb.AddPrism(new Vector3(-0.9f, 3.0f, -0.62f), new Vector3(-0.9f, 3.6f, -0.62f), 0.1f, 0.0f, 4, false, false, seed, 0f);
            mb.AddPrism(new Vector3(0.9f, 0.5f, -0.62f), new Vector3(0.9f, 3.0f, -0.62f), 0.12f, 0.07f, 4, false, false, seed, 0f);
            mb.AddPrism(new Vector3(0.9f, 3.0f, -0.62f), new Vector3(0.9f, 3.6f, -0.62f), 0.1f, 0.0f, 4, false, false, seed, 0f);
            return mb.Build("Throne");
        }

        /// <summary>Blocco smussato (8 facce laterali effettive grazie agli spigoli tagliati).</summary>
        public static void Box(MeshBuilder mb, Vector3 c, Vector3 size, int seed, float bevel = 0.06f)
        {
            Vector3 h = size * 0.5f;
            float b = Mathf.Min(bevel, Mathf.Min(h.x, h.z) * 0.5f);
            // anello ottagonale (spigoli verticali smussati)
            Vector3[] ring =
            {
                new Vector3(-h.x + b, 0, -h.z), new Vector3(h.x - b, 0, -h.z), new Vector3(h.x, 0, -h.z + b), new Vector3(h.x, 0, h.z - b),
                new Vector3(h.x - b, 0, h.z), new Vector3(-h.x + b, 0, h.z), new Vector3(-h.x, 0, h.z - b), new Vector3(-h.x, 0, -h.z + b)
            };
            for (int i = 0; i < 8; i++)
            {
                Vector3 p0 = ring[i], p1 = ring[(i + 1) % 8];
                float j0 = (Noise.Hash(i, seed, 1) - 0.5f) * 0.02f;
                Vector3 a = c + p0 + Vector3.down * h.y, bb = c + p1 + Vector3.down * h.y;
                Vector3 cc = c + p1 + Vector3.up * (h.y + j0), d = c + p0 + Vector3.up * (h.y + j0);
                mb.AddQuad(a, bb, cc, d, (p0 + p1) * 0.5f);
            }
            for (int i = 1; i < 7; i++)
            {
                mb.AddTri(c + ring[0] + Vector3.up * h.y, c + ring[i] + Vector3.up * h.y, c + ring[i + 1] + Vector3.up * h.y, Vector3.up);
                mb.AddTri(c + ring[0] + Vector3.down * h.y, c + ring[i] + Vector3.down * h.y, c + ring[i + 1] + Vector3.down * h.y, Vector3.down);
            }
        }

        // ================================================================== ALBERO MORTO
        public static Mesh DeadTree(int seed, float height)
        {
            var mb = new MeshBuilder { uvScale = 1.2f };
            Branch(mb, Vector3.zero, Vector3.up, height * 0.55f, height * 0.045f, 0, seed);
            return mb.Build("DeadTree_" + seed);
        }

        static void Branch(MeshBuilder mb, Vector3 start, Vector3 dir, float length, float radius, int depth, int seed)
        {
            int segs = depth == 0 ? 4 : 2;
            Vector3 p = start;
            Vector3 d = dir;
            float r = radius;
            for (int s = 0; s < segs; s++)
            {
                // il ramo si piega in modo irregolare
                d = (d + new Vector3(Noise.Hash(s, depth, seed) - 0.5f, 0.1f, Noise.Hash(s, depth, seed + 9) - 0.5f) * 0.55f).normalized;
                Vector3 q = p + d * (length / segs);
                float r2 = r * 0.78f;
                mb.color = depth == 0 && s == 0 ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
                mb.AddPrism(p, q, r, r2, depth == 0 ? 6 : 4, false, depth >= 2 && s == segs - 1, seed + s, 0.15f);
                if (depth < 2 && s >= (depth == 0 ? 1 : 0))
                {
                    int kids = depth == 0 ? 2 : 1;
                    for (int k = 0; k < kids; k++)
                    {
                        float ang = (Noise.Hash(s, k + depth * 7, seed) * 360f);
                        Vector3 side = Quaternion.AngleAxis(ang, d) * Vector3.Cross(d, Vector3.right).normalized;
                        Vector3 nd = (d * 0.6f + side * 0.9f + Vector3.up * 0.3f).normalized;
                        Branch(mb, q, nd, length * 0.55f, r2 * 0.6f, depth + 1, seed * 3 + s * 11 + k);
                    }
                }
                p = q; r = r2;
            }
            // radici
            if (depth == 0)
                for (int k = 0; k < 4; k++)
                {
                    float a = k * 90f + Noise.Hash(k, seed) * 40f;
                    Vector3 rd = Quaternion.Euler(0, a, 0) * new Vector3(1, -0.35f, 0);
                    mb.AddPrism(start + Vector3.up * 0.25f, start + rd.normalized * radius * 5f, radius * 0.6f, radius * 0.15f, 4, false, true, seed + k, 0.1f);
                }
        }

        // ================================================================== LAPIDE
        public static Mesh Gravestone(int seed)
        {
            var mb = new MeshBuilder { uvScale = 1.0f };
            float w = 0.55f + Noise.Hash(seed) * 0.25f, h = 0.8f + Noise.Hash(seed + 1) * 0.5f, t = 0.14f;
            int segs = 6;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = PointedArch(i / (float)segs, w, h * 0.7f, h), p1 = PointedArch((i + 1) / (float)segs, w, h * 0.7f, h);
                if (seed % 2 == 0) // variante a sommità tonda
                {
                    float a0 = Mathf.PI * (1 - i / (float)segs), a1 = Mathf.PI * (1 - (i + 1) / (float)segs);
                    p0 = new Vector2(Mathf.Cos(a0) * w * 0.5f, h * 0.75f + Mathf.Sin(a0) * w * 0.4f);
                    p1 = new Vector2(Mathf.Cos(a1) * w * 0.5f, h * 0.75f + Mathf.Sin(a1) * w * 0.4f);
                }
                mb.AddQuad(new Vector3(p0.x, 0, t), new Vector3(p1.x, 0, t), new Vector3(p1.x, p1.y, t), new Vector3(p0.x, p0.y, t), Vector3.forward);
                mb.AddQuad(new Vector3(p0.x, 0, -t), new Vector3(p1.x, 0, -t), new Vector3(p1.x, p1.y, -t), new Vector3(p0.x, p0.y, -t), Vector3.back);
                mb.AddQuad(new Vector3(p0.x, p0.y, t), new Vector3(p1.x, p1.y, t), new Vector3(p1.x, p1.y, -t), new Vector3(p0.x, p0.y, -t), new Vector3((p0.x + p1.x), (p0.y + p1.y) - h, 0));
            }
            Vector2 l = seed % 2 == 0 ? new Vector2(-w * 0.5f, h * 0.75f) : PointedArch(0, w, h * 0.7f, h);
            Vector2 r = seed % 2 == 0 ? new Vector2(w * 0.5f, h * 0.75f) : PointedArch(1, w, h * 0.7f, h);
            mb.AddQuad(new Vector3(l.x, -0.3f, t), new Vector3(l.x, l.y, t), new Vector3(l.x, l.y, -t), new Vector3(l.x, -0.3f, -t), Vector3.left);
            mb.AddQuad(new Vector3(r.x, -0.3f, t), new Vector3(r.x, r.y, t), new Vector3(r.x, r.y, -t), new Vector3(r.x, -0.3f, -t), Vector3.right);
            return mb.Build("Gravestone_" + seed);
        }

        // ================================================================== ERBA SECCA
        public static Mesh GrassTuft(int seed)
        {
            var mb = new MeshBuilder();
            for (int k = 0; k < 3; k++)
            {
                float a = k * 60f + Noise.Hash(k, seed) * 20f;
                Quaternion q = Quaternion.Euler(0, a, 0);
                float w = 0.45f, h = 0.5f + Noise.Hash(k, seed, 2) * 0.3f;
                Vector3 p0 = q * new Vector3(-w, 0, 0), p1 = q * new Vector3(w, 0, 0);
                Vector3 lean = q * new Vector3(0, 0, 0.12f);
                Vector3 n = q * Vector3.forward;
                mb.color = new Color(0.8f, 0.8f, 0.8f);
                mb.AddQuad(p0, p1, p1 + Vector3.up * h + lean, p0 + Vector3.up * h + lean, n, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                mb.AddQuad(p0, p1, p1 + Vector3.up * h + lean, p0 + Vector3.up * h + lean, -n, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            }
            return mb.Build("GrassTuft_" + seed);
        }

        // ================================================================== ARMI
        /// <summary>Spada lunga: impugnatura all'origine, lama lungo +Y, filo nel piano YZ.</summary>
        public static Mesh Sword()
        {
            var mb = new MeshBuilder { uvScale = 2f };
            // pomolo
            mb.AddPrism(new Vector3(0, -0.24f, 0), new Vector3(0, -0.17f, 0), 0.035f, 0.04f, 6, true, false, 1, 0f);
            // impugnatura
            mb.AddPrism(new Vector3(0, -0.17f, 0), new Vector3(0, 0.08f, 0), 0.022f, 0.02f, 6, false, false, 2, 0f);
            // elsa (crossguard) leggermente curva
            mb.AddPrism(new Vector3(0, 0.09f, -0.2f), new Vector3(0, 0.1f, 0f), 0.022f, 0.03f, 4, true, false, 3, 0f);
            mb.AddPrism(new Vector3(0, 0.1f, 0f), new Vector3(0, 0.09f, 0.2f), 0.03f, 0.022f, 4, false, true, 4, 0f);
            // lama: sezione a rombo, rastremata, con punta
            float len = 0.95f, w0 = 0.045f, t0 = 0.012f;
            Vector3 b = new Vector3(0, 0.11f, 0), tip = new Vector3(0, 0.11f + len + 0.12f, 0), e = new Vector3(0, 0.11f + len, 0);
            Vector3[] r0 = { b + Vector3.forward * w0, b + Vector3.right * t0, b - Vector3.forward * w0, b - Vector3.right * t0 };
            Vector3[] r1 = { e + Vector3.forward * w0 * 0.75f, e + Vector3.right * t0 * 0.8f, e - Vector3.forward * w0 * 0.75f, e - Vector3.right * t0 * 0.8f };
            for (int i = 0; i < 4; i++)
            {
                int n = (i + 1) % 4;
                Vector3 mid = (r0[i] + r0[n]) * 0.5f - b;
                mb.color = Color.white;
                mb.AddQuad(r0[i], r0[n], r1[n], r1[i], mid);
                mb.AddTri(r1[i], r1[n], tip, mid + Vector3.up * 0.1f);
            }
            return mb.Build("Sword");
        }

        /// <summary>
        /// Greatsword (two-handed "spadone"): grip at the origin, blade along +Y, edge in the YZ plane.
        /// Long handle with leather wrap and pommel, wide crossguard, fullered tapering blade.
        /// </summary>
        public static Mesh Greatsword()
        {
            var mb = new MeshBuilder { uvScale = 2f };
            // pommel + long grip (the hand sits around the origin)
            mb.AddPrism(new Vector3(0, -0.36f, 0), new Vector3(0, -0.29f, 0), 0.03f, 0.045f, 6, true, false, 1, 0f);
            mb.AddPrism(new Vector3(0, -0.29f, 0), new Vector3(0, -0.26f, 0), 0.045f, 0.028f, 6, false, false, 2, 0f);
            for (int i = 0; i < 4; i++) // leather wrap rings
            {
                float y0 = -0.26f + i * 0.085f, y1 = y0 + 0.085f;
                mb.AddPrism(new Vector3(0, y0, 0), new Vector3(0, y1, 0), 0.026f + (i % 2) * 0.003f, 0.026f + ((i + 1) % 2) * 0.003f, 6, false, false, 3 + i, 0f);
            }
            // crossguard, slightly drooping arms
            mb.AddPrism(new Vector3(0, 0.1f, 0), new Vector3(0, 0.08f, -0.24f), 0.035f, 0.022f, 4, false, true, 9, 0f);
            mb.AddPrism(new Vector3(0, 0.1f, 0), new Vector3(0, 0.08f, 0.24f), 0.035f, 0.022f, 4, false, true, 10, 0f);
            mb.AddPrism(new Vector3(0, 0.08f, 0), new Vector3(0, 0.14f, 0), 0.05f, 0.045f, 4, true, false, 11, 0f);
            // ricasso + blade (diamond section, fuller groove), tapering to the point
            float len = 1.32f, w0 = 0.062f, t0 = 0.016f;
            Vector3 b = new Vector3(0, 0.14f, 0), e = new Vector3(0, 0.14f + len, 0), tip = e + new Vector3(0, 0.16f, 0);
            Vector3[] r0 = { b + Vector3.forward * w0, b + Vector3.right * t0, b - Vector3.forward * w0, b - Vector3.right * t0 };
            Vector3[] r1 = { e + Vector3.forward * w0 * 0.68f, e + Vector3.right * t0 * 0.8f, e - Vector3.forward * w0 * 0.68f, e - Vector3.right * t0 * 0.8f };
            for (int i = 0; i < 4; i++)
            {
                int n = (i + 1) % 4;
                Vector3 mid = (r0[i] + r0[n]) * 0.5f - b;
                mb.AddQuad(r0[i], r0[n], r1[n], r1[i], mid);
                mb.AddTri(r1[i], r1[n], tip, mid + Vector3.up * 0.1f);
                mb.AddTri(r0[i], r0[n], b, -Vector3.up);
            }
            return mb.Build("Greatsword");
        }

        /// <summary>Scudo a goccia (kite) bombato: faccia verso +Z, asse lungo +Y, centro all'origine.</summary>
        public static Mesh KiteShield()
        {
            var mb = new MeshBuilder { uvScale = 1.5f };
            int n = 14;
            var outline = new List<Vector2>();
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float a = Mathf.Lerp(Mathf.PI, 0, t);
                outline.Add(new Vector2(Mathf.Cos(a) * 0.32f, 0.2f + Mathf.Sin(a) * 0.18f));
            }
            outline.Add(new Vector2(0.27f, -0.05f));
            outline.Add(new Vector2(0.16f, -0.3f));
            outline.Add(new Vector2(0f, -0.48f));
            outline.Add(new Vector2(-0.16f, -0.3f));
            outline.Add(new Vector2(-0.27f, -0.05f));
            float Bulge(Vector2 p) => 0.07f * (1f - Mathf.Clamp01((p.x * p.x) / 0.1f));
            Vector3 c = new Vector3(0, 0.02f, Bulge(Vector2.zero) + 0.02f);
            float th = 0.03f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 p0 = outline[i], p1 = outline[(i + 1) % outline.Count];
                Vector3 a = new Vector3(p0.x, p0.y, Bulge(p0)), b = new Vector3(p1.x, p1.y, Bulge(p1));
                mb.AddTri(c, a, b, Vector3.forward);
                mb.AddTri(c + Vector3.back * (th + 0.04f), a + Vector3.back * th, b + Vector3.back * th, Vector3.back);
                mb.AddQuad(a, b, b + Vector3.back * th, a + Vector3.back * th, new Vector3((p0.x + p1.x), (p0.y + p1.y), 0));
            }
            // umbone
            mb.AddPrism(c, c + Vector3.forward * 0.05f, 0.06f, 0.025f, 6, false, true, 5, 0f);
            return mb.Build("KiteShield");
        }

        // ================================================================== TORCIA / BRACIERE
        public static Mesh WallTorch()
        {
            var mb = new MeshBuilder { uvScale = 3f };
            // staffa di ferro
            mb.AddPrism(new Vector3(0, 0, 0), new Vector3(0, 0, 0.32f), 0.025f, 0.025f, 4, true, false, 1, 0f);
            mb.AddPrism(new Vector3(0, -0.1f, 0.0f), new Vector3(0, -0.05f, 0.3f), 0.02f, 0.02f, 4, true, false, 2, 0f);
            // bastone di legno inclinato
            mb.AddPrism(new Vector3(0, -0.25f, 0.24f), new Vector3(0, 0.3f, 0.36f), 0.03f, 0.04f, 6, true, false, 3, 0.1f);
            // testa avvolta di stracci
            mb.AddPrism(new Vector3(0, 0.25f, 0.35f), new Vector3(0, 0.42f, 0.38f), 0.06f, 0.05f, 6, false, true, 4, 0.25f);
            return mb.Build("WallTorch");
        }

        public static Mesh Brazier()
        {
            var mb = new MeshBuilder { uvScale = 2f };
            for (int k = 0; k < 3; k++)
            {
                float a = k * 120f * Mathf.Deg2Rad;
                Vector3 foot = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.38f;
                mb.AddPrism(foot, Vector3.up * 0.75f + foot * 0.4f, 0.03f, 0.03f, 4, false, false, k, 0f);
            }
            mb.AddPrism(Vector3.up * 0.75f, Vector3.up * 1.05f, 0.25f, 0.42f, 8, true, false, 7, 0.08f);
            // carbone
            mb.color = new Color(0.4f, 0.4f, 0.4f);
            mb.AddPrism(Vector3.up * 0.95f, Vector3.up * 1.02f, 0.38f, 0.3f, 8, false, true, 8, 0.2f);
            return mb.Build("Brazier");
        }

        // ================================================================== STENDARDO LACERO
        public static Mesh Banner(int seed, float width, float height)
        {
            var mb = new MeshBuilder();
            int cols = 6, rows = 8;
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    float u0 = i / (float)cols, u1 = (i + 1) / (float)cols;
                    float v0 = j / (float)rows, v1 = (j + 1) / (float)rows;
                    // bordo inferiore strappato: le celle in basso mancano a caso
                    if (j == 0 && Noise.Hash(i, seed) > 0.45f) continue;
                    if (j == 1 && Noise.Hash(i, seed, 3) > 0.8f) continue;
                    Vector3 P(float u, float v) => new Vector3((u - 0.5f) * width, -(1 - v) * height, Mathf.Sin(u * 9f + seed) * 0.05f * (1 - v));
                    Vector2 U(float u, float v) => new Vector2(u, v);
                    mb.AddQuad(P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1), Vector3.forward, U(u0, v0), U(u1, v0), U(u1, v1), U(u0, v1));
                    mb.AddQuad(P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1), Vector3.back, U(u0, v0), U(u1, v0), U(u1, v1), U(u0, v1));
                }
            // asta
            mb.AddPrism(new Vector3(-width * 0.6f, 0.02f, 0), new Vector3(width * 0.6f, 0.02f, 0), 0.03f, 0.03f, 5, true, true, seed, 0f);
            return mb.Build("Banner_" + seed);
        }

        // ================================================================== VFX
        /// <summary>Superficie del cancello di nebbia: griglia curva con UV 0..1.</summary>
        public static Mesh FogSheet(float width, float height, float bend = 0.4f)
        {
            var mb = new MeshBuilder();
            int cols = 10, rows = 8;
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    Vector3 P(int a, int b) { float u = a / (float)cols, v = b / (float)rows; return new Vector3((u - 0.5f) * width, v * height, Mathf.Sin(u * Mathf.PI) * bend); }
                    Vector2 U(int a, int b) => new Vector2(a / (float)cols, b / (float)rows);
                    mb.AddQuad(P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1), Vector3.back, U(i, j), U(i + 1, j), U(i + 1, j + 1), U(i, j + 1));
                    mb.AddQuad(P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1), Vector3.forward, U(i, j), U(i + 1, j), U(i + 1, j + 1), U(i, j + 1));
                }
            return mb.Build("FogSheet");
        }

        /// <summary>Anello piatto (onda d'urto del boss): raggio interno 0.85, esterno 1.</summary>
        public static Mesh Ring(int segments = 40)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2, a1 = (i + 1) / (float)segments * Mathf.PI * 2;
                Vector3 o0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), o1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                mb.AddQuad(o0 * 0.85f, o1 * 0.85f, o1, o0, Vector3.up, new Vector2(i / (float)segments, 0), new Vector2((i + 1) / (float)segments, 0), new Vector2((i + 1) / (float)segments, 1), new Vector2(i / (float)segments, 1));
            }
            return mb.Build("ShockRing");
        }

        /// <summary>Quad semplice con UV 0..1 (vetrate, sprite nel mondo).</summary>
        public static Mesh Quad(float w, float h, string name)
        {
            var mb = new MeshBuilder();
            mb.AddQuad(new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, h, 0), new Vector3(-w / 2, h, 0), Vector3.back,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            mb.AddQuad(new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, h, 0), new Vector3(-w / 2, h, 0), Vector3.forward,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            return mb.Build(name);
        }

        /// <summary>Finestra ad arco acuto (vetro): stessa forma dell'apertura di GothicWall, UV 0..1.</summary>
        public static Mesh ArchPane(float span, float spring, float apex, float bottom)
        {
            var mb = new MeshBuilder();
            int segs = 10;
            float H = apex - bottom;
            Vector2 U(Vector2 p) => new Vector2(p.x / span + 0.5f, (p.y - bottom) / H);
            Vector2 c = new Vector2(0, bottom);
            Vector2 bl = new Vector2(-span / 2, bottom), br = new Vector2(span / 2, bottom);
            Vector2 sl = new Vector2(-span / 2, spring), sr = new Vector2(span / 2, spring);
            void Q(Vector2 a, Vector2 b, Vector2 cc, Vector2 d)
            {
                mb.AddQuad(a, b, cc, d, Vector3.forward, U(a), U(b), U(cc), U(d));
                mb.AddQuad(a, b, cc, d, Vector3.back, U(a), U(b), U(cc), U(d));
            }
            Q(bl, br, sr, sl);
            Vector2 mid = new Vector2(0, spring);
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = PointedArch(i / (float)segs, span, spring, apex), p1 = PointedArch((i + 1) / (float)segs, span, spring, apex);
                mb.AddTri(mid, p0, p1, Vector3.forward, U(mid), U(p0), U(p1));
                mb.AddTri(mid, p0, p1, Vector3.back, U(mid), U(p0), U(p1));
            }
            return mb.Build("ArchPane");
        }
    }
}
