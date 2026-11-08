using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy.Procedural
{
    /// <summary>
    /// Gothic interior kit for the castle (corridors, halls, prisons): walls with pointed-arch
    /// doorways, pointed barrel vaults, transverse ribs with wall shafts, prison bars, water sheets,
    /// door leaves and small props. Everything is built in local space with the floor at y = 0 and
    /// box-mapped UVs in metres (same texel density as the rest of the level).
    /// </summary>
    public static class CastleKit
    {
        /// <summary>Doorway in a wall: centre along the wall, clear width, springline and apex heights.</summary>
        public struct Opening
        {
            public float u, width, spring, apex;
            public Opening(float u, float width, float spring, float apex) { this.u = u; this.width = width; this.spring = spring; this.apex = apex; }
        }

        const int ArchSegs = 10;

        static float ArchY(Opening o, float x)
        {
            // height of the arch intrados above local x (0 outside the opening)
            float hw = o.width * 0.5f;
            float dx = Mathf.Abs(x - o.u);
            if (dx >= hw) return 0f;
            // sample the pointed arch (left half mirrors the right)
            float best = o.spring;
            for (int i = 0; i <= ArchSegs; i++)
            {
                Vector2 p = ProceduralMeshes.PointedArch(i / (float)(ArchSegs * 2), o.width, o.spring, o.apex);
                if (Mathf.Abs(Mathf.Abs(p.x) - dx) < hw / ArchSegs) best = Mathf.Max(best, p.y);
            }
            return best;
        }

        /// <summary>
        /// Straight wall along local X (from -length/2 to +length/2), thickness along Z (centred),
        /// with any number of pointed-arch doorways cut from the floor. Both faces, top, ends and the
        /// reveals of every doorway; a raised voussoir frame on both faces of each arch.
        /// </summary>
        public static Mesh Wall(int seed, float length, float height, float thickness, IList<Opening> openings, string name)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            Wall(mb, seed, length, height, thickness, openings, Matrix4x4.identity);
            return mb.Build(name);
        }

        public static void Wall(MeshBuilder dst, int seed, float length, float height, float thickness, IList<Opening> openings, Matrix4x4 m)
        {
            var mb = new MeshBuilder { uvScale = dst.uvScale };
            float hl = length * 0.5f, ht = thickness * 0.5f;
            var ops = new List<Opening>(openings ?? new Opening[0]);
            ops.Sort((a, b) => a.u.CompareTo(b.u));

            foreach (float z in new[] { ht, -ht })
            {
                Vector3 n = z > 0 ? Vector3.forward : Vector3.back;
                float x = -hl;
                foreach (var o in ops)
                {
                    float x0 = o.u - o.width * 0.5f, x1 = o.u + o.width * 0.5f;
                    if (x0 > x) Panel(mb, x, x0, 0, height, z, n, seed);
                    // over the doorway: from the arch curve up to the top of the wall
                    for (int i = 0; i < ArchSegs * 2; i++)
                    {
                        Vector2 p0 = ProceduralMeshes.PointedArch(i / (float)(ArchSegs * 2), o.width, o.spring, o.apex);
                        Vector2 p1 = ProceduralMeshes.PointedArch((i + 1) / (float)(ArchSegs * 2), o.width, o.spring, o.apex);
                        p0.x = o.u - p0.x; p1.x = o.u - p1.x;
                        mb.AddQuad(new Vector3(p0.x, p0.y, z), new Vector3(p1.x, p1.y, z), new Vector3(p1.x, height, z), new Vector3(p0.x, height, z), n);
                    }
                    x = x1;
                }
                if (x < hl) Panel(mb, x, hl, 0, height, z, n, seed + 7);
            }
            // top and ends
            mb.AddQuad(new Vector3(-hl, height, ht), new Vector3(hl, height, ht), new Vector3(hl, height, -ht), new Vector3(-hl, height, -ht), Vector3.up);
            mb.AddQuad(new Vector3(-hl, 0, ht), new Vector3(-hl, height, ht), new Vector3(-hl, height, -ht), new Vector3(-hl, 0, -ht), Vector3.left);
            mb.AddQuad(new Vector3(hl, 0, ht), new Vector3(hl, height, ht), new Vector3(hl, height, -ht), new Vector3(hl, 0, -ht), Vector3.right);
            // doorway reveals + voussoirs
            foreach (var o in ops)
            {
                float x0 = o.u - o.width * 0.5f, x1 = o.u + o.width * 0.5f;
                mb.color = new Color(0.72f, 0.72f, 0.72f);
                mb.AddQuad(new Vector3(x0, 0, ht), new Vector3(x0, o.spring, ht), new Vector3(x0, o.spring, -ht), new Vector3(x0, 0, -ht), Vector3.right);
                mb.AddQuad(new Vector3(x1, 0, ht), new Vector3(x1, o.spring, ht), new Vector3(x1, o.spring, -ht), new Vector3(x1, 0, -ht), Vector3.left);
                for (int i = 0; i < ArchSegs * 2; i++)
                {
                    Vector2 p0 = ProceduralMeshes.PointedArch(i / (float)(ArchSegs * 2), o.width, o.spring, o.apex);
                    Vector2 p1 = ProceduralMeshes.PointedArch((i + 1) / (float)(ArchSegs * 2), o.width, o.spring, o.apex);
                    p0.x = o.u - p0.x; p1.x = o.u - p1.x;
                    Vector2 mid = (p0 + p1) * 0.5f;
                    mb.AddQuad(new Vector3(p0.x, p0.y, ht), new Vector3(p1.x, p1.y, ht), new Vector3(p1.x, p1.y, -ht), new Vector3(p0.x, p0.y, -ht), new Vector3(o.u - mid.x, o.spring - mid.y, 0));
                }
                mb.color = Color.white;
                float fr = 0.24f;
                foreach (float side in new[] { 1f, -1f })
                {
                    float zf = side * (ht + 0.09f), zb = side * ht;
                    Vector3 nn = side > 0 ? Vector3.forward : Vector3.back;
                    for (int i = 0; i < ArchSegs * 2; i++)
                    {
                        float t0 = i / (float)(ArchSegs * 2), t1 = (i + 1) / (float)(ArchSegs * 2);
                        Vector2 a0 = ProceduralMeshes.PointedArch(t0, o.width, o.spring, o.apex), a1 = ProceduralMeshes.PointedArch(t1, o.width, o.spring, o.apex);
                        Vector2 b0 = ProceduralMeshes.PointedArch(t0, o.width + fr * 2, o.spring, o.apex + fr), b1 = ProceduralMeshes.PointedArch(t1, o.width + fr * 2, o.spring, o.apex + fr);
                        a0.x = o.u - a0.x; a1.x = o.u - a1.x; b0.x = o.u - b0.x; b1.x = o.u - b1.x;
                        mb.AddQuad(new Vector3(a0.x, a0.y, zf), new Vector3(a1.x, a1.y, zf), new Vector3(b1.x, b1.y, zf), new Vector3(b0.x, b0.y, zf), nn);
                        mb.AddQuad(new Vector3(b0.x, b0.y, zb), new Vector3(b1.x, b1.y, zb), new Vector3(b1.x, b1.y, zf), new Vector3(b0.x, b0.y, zf), new Vector3(b0.x - o.u, b0.y - o.spring, 0));
                    }
                    // jamb shafts under the arch
                    foreach (float jx in new[] { o.u - o.width * 0.5f - fr * 0.5f, o.u + o.width * 0.5f + fr * 0.5f })
                        ProceduralMeshes.Box(mb, new Vector3(jx, o.spring * 0.5f, side * (ht + 0.05f)), new Vector3(fr, o.spring, 0.12f), seed + (int)(jx * 10), 0.03f);
                }
            }
            dst.Append(mb, m);
        }

        static void Panel(MeshBuilder mb, float x0, float x1, float y0, float y1, float z, Vector3 n, int seed)
        {
            int nu = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / 1.6f)), nv = Mathf.Max(1, Mathf.RoundToInt((y1 - y0) / 1.6f));
            Vector3 Pt(int i, int j)
            {
                float u = i / (float)nu, v = j / (float)nv;
                var p = new Vector3(Mathf.Lerp(x0, x1, u), Mathf.Lerp(y0, y1, v), z);
                bool edge = i == 0 || j == 0 || i == nu || j == nv;
                if (!edge) p += n * (Noise.Value3(p * 1.3f, seed) - 0.5f) * 0.1f;
                return p;
            }
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                    mb.AddQuad(Pt(i, j), Pt(i + 1, j), Pt(i + 1, j + 1), Pt(i, j + 1), n);
        }

        /// <summary>
        /// Pointed barrel vault spanning local X (width), running along Z (length), springing at
        /// <paramref name="spring"/> and peaking at <paramref name="apex"/>; seen from below, with a
        /// thin extrados so it also casts shadows. Transverse ribs every <paramref name="ribEvery"/> m.
        /// </summary>
        public static Mesh Vault(int seed, float width, float length, float spring, float apex, float ribEvery, string name)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            Vault(mb, seed, width, length, spring, apex, ribEvery, Matrix4x4.identity);
            return mb.Build(name);
        }

        public static void Vault(MeshBuilder dst, int seed, float width, float length, float spring, float apex, float ribEvery, Matrix4x4 m)
        {
            var mb = new MeshBuilder { uvScale = dst.uvScale };
            int segs = 12, nz = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
            float hz = length * 0.5f;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = ProceduralMeshes.PointedArch(i / (float)segs, width, spring, apex);
                Vector2 b = ProceduralMeshes.PointedArch((i + 1) / (float)segs, width, spring, apex);
                Vector2 mid = (a + b) * 0.5f;
                Vector3 inward = new Vector3(-mid.x, spring + (apex - spring) * 0.3f - mid.y, 0);
                for (int k = 0; k < nz; k++)
                {
                    float z0 = -hz + length * k / nz, z1 = -hz + length * (k + 1) / nz;
                    mb.AddQuad(new Vector3(a.x, a.y, z0), new Vector3(b.x, b.y, z0), new Vector3(b.x, b.y, z1), new Vector3(a.x, a.y, z1), inward);
                    // extrados (outer shell, 0.4 m above)
                    mb.AddQuad(new Vector3(a.x * 1.06f, a.y + 0.4f, z0), new Vector3(b.x * 1.06f, b.y + 0.4f, z0), new Vector3(b.x * 1.06f, b.y + 0.4f, z1), new Vector3(a.x * 1.06f, a.y + 0.4f, z1), -inward);
                }
            }
            // ribs: an arch of stone blocks hanging under the vault, with shafts down the walls
            if (ribEvery > 0.5f)
            {
                int nr = Mathf.Max(1, Mathf.RoundToInt(length / ribEvery));
                for (int r = 0; r <= nr; r++)
                {
                    float z = -hz + length * r / nr;
                    if (r == 0 || r == nr) z = Mathf.Clamp(z, -hz + 0.2f, hz - 0.2f);
                    for (int i = 0; i < segs; i++)
                    {
                        Vector2 a = ProceduralMeshes.PointedArch(i / (float)segs, width - 0.3f, spring, apex - 0.25f);
                        Vector2 b = ProceduralMeshes.PointedArch((i + 1) / (float)segs, width - 0.3f, spring, apex - 0.25f);
                        Vector3 c = new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, z);
                        Vector3 d = new Vector3(b.x - a.x, b.y - a.y, 0);
                        var rot = Quaternion.LookRotation(Vector3.forward, Vector3.Cross(d, Vector3.forward));
                        var blk = new MeshBuilder { uvScale = 0.5f };
                        ProceduralMeshes.Box(blk, Vector3.zero, new Vector3(d.magnitude + 0.04f, 0.28f, 0.32f), seed + r * 31 + i, 0.03f);
                        mb.Append(blk, Matrix4x4.TRS(c, rot, Vector3.one));
                    }
                    foreach (float sx in new[] { -width * 0.5f + 0.15f, width * 0.5f - 0.15f })
                        ProceduralMeshes.Box(mb, new Vector3(sx, spring * 0.5f, z), new Vector3(0.3f, spring, 0.34f), seed + r * 7 + (sx > 0 ? 1 : 0), 0.04f);
                }
            }
            dst.Append(mb, m);
        }

        /// <summary>Flat stone ceiling slab (rooms whose vault is too wide for a barrel).</summary>
        public static Mesh Slab(int seed, float w, float d, float thick, string name)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            ProceduralMeshes.Box(mb, new Vector3(0, thick * 0.5f, 0), new Vector3(w, thick, d), seed, 0.02f);
            return mb.Build(name);
        }

        /// <summary>Row of prison bars along local X, with a top and bottom rail (origin = floor centre).</summary>
        public static Mesh Bars(int seed, float length, float height, float spacing, float gapFrom = 0, float gapTo = 0)
        {
            var mb = new MeshBuilder { uvScale = 2f };
            int n = Mathf.Max(2, Mathf.RoundToInt(length / spacing));
            for (int i = 0; i <= n; i++)
            {
                float x = -length * 0.5f + length * i / n;
                if (gapTo > gapFrom && x > gapFrom && x < gapTo) continue;
                float bend = (Noise.Hash(i, seed, 2) - 0.5f) * 0.08f;
                mb.AddPrism(new Vector3(x, 0, 0), new Vector3(x + bend, height, 0), 0.028f, 0.024f, 5, false, true, seed + i, 0.1f);
            }
            float l0 = -length * 0.5f, l1 = length * 0.5f;
            bool gap = gapTo > gapFrom;
            foreach (float y in new[] { 0.12f, height - 0.15f, height * 0.5f })
            {
                if (!gap) { ProceduralMeshes.Box(mb, new Vector3(0, y, 0), new Vector3(length, 0.07f, 0.09f), seed + (int)(y * 10), 0.01f); continue; }
                // the rails stop at the open doorway: nothing across the gap
                float a0 = l0, a1 = gapFrom - 0.04f, b0 = gapTo + 0.04f, b1 = l1;
                if (a1 > a0 + 0.05f) ProceduralMeshes.Box(mb, new Vector3((a0 + a1) * 0.5f, y, 0), new Vector3(a1 - a0, 0.07f, 0.09f), seed + (int)(y * 10), 0.01f);
                if (b1 > b0 + 0.05f) ProceduralMeshes.Box(mb, new Vector3((b0 + b1) * 0.5f, y, 0), new Vector3(b1 - b0, 0.07f, 0.09f), seed + (int)(y * 10) + 1, 0.01f);
            }
            if (gap)   // thick posts on both sides of the doorway
                foreach (float x in new[] { gapFrom - 0.03f, gapTo + 0.03f })
                    ProceduralMeshes.Box(mb, new Vector3(x, height * 0.5f, 0), new Vector3(0.08f, height, 0.1f), seed + 7, 0.005f);
            return mb.Build("Bars_" + seed);
        }

        /// <summary>A hanging iron cage (gibbet): vertical bars between two rings, domed top, hook. Origin = hook (top).</summary>
        public static Mesh Cage(int seed, float radius, float height)
        {
            var mb = new MeshBuilder { uvScale = 2f };
            int n = 10;
            float top = -0.35f, bottom = top - height;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector3 r = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                mb.AddPrism(new Vector3(r.x, bottom, r.z), new Vector3(r.x, top, r.z), 0.02f, 0.02f, 4, false, false, seed + i, 0.004f);
                mb.AddPrism(new Vector3(r.x, top, r.z), new Vector3(r.x * 0.15f, -0.08f, r.z * 0.15f), 0.018f, 0.015f, 4, false, false, seed + 30 + i, 0f);
                float a2 = (i + 1) * Mathf.PI * 2f / n;
                Vector3 r2 = new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * radius;
                foreach (float y in new[] { bottom, top, (bottom + top) * 0.5f })
                    mb.AddPrism(new Vector3(r.x, y, r.z), new Vector3(r2.x, y, r2.z), 0.022f, 0.022f, 4, false, false, seed + 60 + i, 0f);
            }
            // floor plate and the hook
            ProceduralMeshes.Box(mb, new Vector3(0, bottom - 0.02f, 0), new Vector3(radius * 1.6f, 0.04f, radius * 1.6f), seed + 99, 0.004f);
            mb.AddPrism(new Vector3(0, -0.1f, 0), Vector3.zero, 0.03f, 0.03f, 4, true, true, seed + 98, 0f);
            return mb.Build("Cage_" + seed);
        }

        /// <summary>A hanging chain (alternating flattened links). Origin = the top; hangs down along -Y.</summary>
        public static Mesh Chain(int seed, float length, float link = 0.11f)
        {
            var mb = new MeshBuilder { uvScale = 3f };
            int n = Mathf.Max(2, Mathf.RoundToInt(length / (link * 0.8f)));
            for (int i = 0; i < n; i++)
            {
                float y0 = -i * link * 0.8f, y1 = y0 - link;
                float w = link * 0.32f;
                bool turn = i % 2 == 1;
                Vector3 side = turn ? new Vector3(0, 0, w) : new Vector3(w, 0, 0);
                mb.AddPrism(new Vector3(0, y0, 0) + side, new Vector3(0, y1, 0) + side, 0.011f, 0.011f, 4, false, false, seed + i, 0f);
                mb.AddPrism(new Vector3(0, y0, 0) - side, new Vector3(0, y1, 0) - side, 0.011f, 0.011f, 4, false, false, seed + i + 50, 0f);
            }
            return mb.Build("Chain_" + seed);
        }

        /// <summary>Thin water sheet (slightly rippled grid), top at y = 0.</summary>
        public static Mesh Water(int seed, float w, float d)
        {
            // flooded floor broken into puddles with SOFT edges: a fine grid whose vertex alpha comes
            // from smooth noise (shallow rims fade into the wet stone), deeper and darker in the middle
            const float cell = 0.28f;
            int nx = Mathf.Max(2, Mathf.RoundToInt(w / cell)), nz = Mathf.Max(2, Mathf.RoundToInt(d / cell));
            var verts = new List<Vector3>(); var cols = new List<Color>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float[,] a = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float x = -w * 0.5f + w * i / nx, z = -d * 0.5f + d * j / nz;
                    float n = Noise.Value2(x * 0.33f + 7f, z * 0.33f, seed) * 0.72f + Noise.Value2(x * 1.05f, z * 1.05f, seed + 9) * 0.28f;
                    float edge = Mathf.Min(Mathf.Min(x + w * 0.5f, w * 0.5f - x), Mathf.Min(z + d * 0.5f, d * 0.5f - z));
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.5f, n)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.45f, edge));
                    a[i, j] = k;
                    verts.Add(new Vector3(x, 0f, z));
                    float deep = Mathf.InverseLerp(0.45f, 0.75f, n);
                    cols.Add(new Color(1f - 0.35f * deep, 1f - 0.3f * deep, 1f - 0.22f * deep, k));
                    uvs.Add(new Vector2(x * 0.7f, z * 0.7f));
                }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (a[i, j] + a[i + 1, j] + a[i, j + 1] + a[i + 1, j + 1] < 0.01f) continue;   // dry: no triangles
                    int v0 = i * (nz + 1) + j, v1 = (i + 1) * (nz + 1) + j, v2 = (i + 1) * (nz + 1) + j + 1, v3 = i * (nz + 1) + j + 1;
                    tris.Add(v0); tris.Add(v3); tris.Add(v2);
                    tris.Add(v0); tris.Add(v2); tris.Add(v1);
                }
            var m = new Mesh { name = "Water_" + seed };
            m.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(verts); m.SetColors(cols); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            var nrm = new Vector3[verts.Count]; var tan = new Vector4[verts.Count];
            for (int i = 0; i < nrm.Length; i++) { nrm[i] = Vector3.up; tan[i] = new Vector4(1, 0, 0, -1); }
            m.normals = nrm; m.tangents = tan;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// One leaf of a heavy door: vertical planks, iron bands and studs, a ring handle. Hinge at
        /// local x = 0, the leaf extends toward +X, pointed top following the arch of a two-leaf door
        /// of total width 2*w (<paramref name="left"/> = left leaf).
        /// </summary>
        public static Mesh DoorLeaf(int seed, float w, float spring, float apex, bool left, string name)
        {
            var mb = new MeshBuilder { uvScale = 0.7f };
            int planks = Mathf.Max(3, Mathf.RoundToInt(w / 0.28f));
            float span = w * 2f;
            for (int i = 0; i < planks; i++)
            {
                float x0 = w * i / planks, x1 = w * (i + 1) / planks;
                float xc = (x0 + x1) * 0.5f;
                // height of the arch at this x (door total width 2w, hinge at the outer edge)
                float u = left ? xc : w - xc;   // distance from the outer jamb
                Vector2 top = ArchTop(span, spring, apex, u);
                float h = top.y - 0.04f;
                mb.color = Color.Lerp(new Color(0.85f, 0.85f, 0.85f), Color.white, Noise.Hash(i, seed, 1));
                ProceduralMeshes.Box(mb, new Vector3(xc, h * 0.5f, 0), new Vector3(x1 - x0 - 0.012f, h, 0.12f + Noise.Hash(i, seed, 3) * 0.02f), seed + i, 0.015f);
            }
            mb.color = new Color(0.32f, 0.3f, 0.29f);
            foreach (float y in new[] { 0.5f, spring * 0.55f, spring - 0.2f })
            {
                ProceduralMeshes.Box(mb, new Vector3(w * 0.5f, y, 0.085f), new Vector3(w - 0.08f, 0.12f, 0.03f), seed + (int)(y * 31), 0.005f);
                ProceduralMeshes.Box(mb, new Vector3(w * 0.5f, y, -0.085f), new Vector3(w - 0.08f, 0.12f, 0.03f), seed + (int)(y * 37), 0.005f);
                for (int s = 0; s < 5; s++)
                    ProceduralMeshes.Box(mb, new Vector3(0.12f + (w - 0.24f) * s / 4f, y, 0.11f), new Vector3(0.05f, 0.05f, 0.04f), seed + s, 0.01f);
            }
            // ring handle near the meeting edge
            float hx = left ? w - 0.25f : 0.25f;
            mb.AddPrism(new Vector3(hx, 1.05f, 0.1f), new Vector3(hx, 1.05f, 0.16f), 0.05f, 0.05f, 6, true, true, seed, 0f);
            for (int k = 0; k < 8; k++)
            {
                float a0 = k / 8f * Mathf.PI * 2, a1 = (k + 1) / 8f * Mathf.PI * 2;
                mb.AddPrism(new Vector3(hx + Mathf.Sin(a0) * 0.11f, 0.93f + Mathf.Cos(a0) * 0.11f, 0.17f), new Vector3(hx + Mathf.Sin(a1) * 0.11f, 0.93f + Mathf.Cos(a1) * 0.11f, 0.17f), 0.015f, 0.015f, 4, false, false, seed + k, 0f);
            }
            mb.color = Color.white;
            return mb.Build(name);
        }

        static Vector2 ArchTop(float span, float spring, float apex, float fromJamb)
        {
            // highest point of the pointed arch at a given distance from a jamb
            float best = spring;
            for (int i = 0; i <= 40; i++)
            {
                Vector2 p = ProceduralMeshes.PointedArch(i / 80f, span, spring, apex);   // left half
                float d = p.x + span * 0.5f;                                             // distance from left jamb
                if (Mathf.Abs(d - fromJamb) < span / 40f) best = Mathf.Max(best, p.y);
            }
            return new Vector2(fromJamb, best);
        }

        /// <summary>A big iron key (shaft along +X, bow ring at the origin).</summary>
        public static Mesh Key()
        {
            var mb = new MeshBuilder { uvScale = 3f };
            for (int k = 0; k < 10; k++)
            {
                float a0 = k / 10f * Mathf.PI * 2, a1 = (k + 1) / 10f * Mathf.PI * 2;
                mb.AddPrism(new Vector3(Mathf.Cos(a0) * 0.09f, Mathf.Sin(a0) * 0.09f, 0), new Vector3(Mathf.Cos(a1) * 0.09f, Mathf.Sin(a1) * 0.09f, 0), 0.022f, 0.022f, 5, false, false, k, 0f);
            }
            mb.AddPrism(new Vector3(0.09f, 0, 0), new Vector3(0.52f, 0, 0), 0.024f, 0.02f, 6, false, true, 3, 0f);
            ProceduralMeshes.Box(mb, new Vector3(0.44f, -0.055f, 0), new Vector3(0.05f, 0.1f, 0.03f), 1, 0.005f);
            ProceduralMeshes.Box(mb, new Vector3(0.5f, -0.07f, 0), new Vector3(0.04f, 0.13f, 0.03f), 2, 0.005f);
            ProceduralMeshes.Box(mb, new Vector3(0.36f, -0.04f, 0), new Vector3(0.03f, 0.07f, 0.03f), 4, 0.005f);
            return mb.Build("Key");
        }

        /// <summary>Hanging iron chandelier ring with candle stubs (origin = chain anchor at the top).</summary>
        public static Mesh Chandelier(int seed, float radius, float drop)
        {
            var mb = new MeshBuilder { uvScale = 2f };
            mb.AddPrism(new Vector3(0, 0, 0), new Vector3(0, -drop, 0), 0.03f, 0.03f, 4, false, false, seed, 0f);
            int n = 16;
            for (int k = 0; k < n; k++)
            {
                float a0 = k / (float)n * Mathf.PI * 2, a1 = (k + 1) / (float)n * Mathf.PI * 2;
                mb.AddPrism(new Vector3(Mathf.Cos(a0) * radius, -drop, Mathf.Sin(a0) * radius), new Vector3(Mathf.Cos(a1) * radius, -drop, Mathf.Sin(a1) * radius), 0.05f, 0.05f, 4, false, false, seed + k, 0f);
                if (k % 2 == 0)
                    mb.AddPrism(new Vector3(Mathf.Cos(a0) * radius, -drop, Mathf.Sin(a0) * radius), new Vector3(Mathf.Cos(a0) * radius, -drop + 0.22f, Mathf.Sin(a0) * radius), 0.035f, 0.03f, 5, false, true, seed + 40 + k, 0.1f);
            }
            for (int s = 0; s < 4; s++)
            {
                float a = s * Mathf.PI * 0.5f;
                mb.AddPrism(new Vector3(0, -drop * 0.55f, 0), new Vector3(Mathf.Cos(a) * radius, -drop, Mathf.Sin(a) * radius), 0.02f, 0.02f, 4, false, false, seed + 80 + s, 0f);
            }
            return mb.Build("Chandelier_" + seed);
        }

        /// <summary>Stone balustrade along local X (rail + squat balusters), height ~1.05 m.</summary>
        public static Mesh Balustrade(int seed, float length)
        {
            var mb = new MeshBuilder { uvScale = 0.6f };
            ProceduralMeshes.Box(mb, new Vector3(0, 0.12f, 0), new Vector3(length, 0.24f, 0.55f), seed, 0.03f);
            ProceduralMeshes.Box(mb, new Vector3(0, 1.0f, 0), new Vector3(length, 0.16f, 0.5f), seed + 1, 0.04f);
            int n = Mathf.Max(2, Mathf.RoundToInt(length / 0.42f));
            for (int i = 0; i < n; i++)
            {
                float x = -length * 0.5f + length * (i + 0.5f) / n;
                if (Noise.Hash(i, seed, 5) < 0.06f) continue;   // a few broken balusters
                mb.AddPrism(new Vector3(x, 0.24f, 0), new Vector3(x, 0.55f, 0), 0.07f, 0.12f, 6, false, false, seed + i, 0.1f);
                mb.AddPrism(new Vector3(x, 0.55f, 0), new Vector3(x, 0.92f, 0), 0.12f, 0.07f, 6, false, false, seed + i + 100, 0.1f);
            }
            for (int p = 0; p <= Mathf.FloorToInt(length / 4f); p++)
            {
                float x = -length * 0.5f + Mathf.Min(length, p * 4f);
                ProceduralMeshes.Box(mb, new Vector3(x, 0.58f, 0), new Vector3(0.5f, 1.16f, 0.6f), seed + 300 + p, 0.04f);
            }
            return mb.Build("Balustrade_" + seed);
        }

        /// <summary>Wooden cell cot / table block (simple props).</summary>
        public static Mesh Crate(int seed, Vector3 size)
        {
            var mb = new MeshBuilder { uvScale = 1f };
            ProceduralMeshes.Box(mb, new Vector3(0, size.y * 0.5f, 0), size, seed, 0.03f);
            return mb.Build("Crate_" + seed);
        }
    }
}
