using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkFantasy.Procedural;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Boss armour sets (helmet, armour, legs, boots) as rigid shells fitted on the bones of every class
    /// (same Solus rig). The inventory's item pictures are rendered in game (UI/ItemIcons).
    ///   Ashen King  — blackened plate edged in gold, an ember crown;
    ///   Vaelthrax   — crimson wyrm scale, bone horns and spikes, smouldering seams;
    ///   The Mirror  — moon-silver and gold, glass crystals, a floating halo and light (the finest);
    ///   The Reaper  — shadow cloth and bone, a skull cowl with violet eyes, chains.
    /// Pieces are authored in a "bone frame": origin on the joint, +Y along the bone (toward the child
    /// joint), +Z the character's forward, +X outward (mirrored for the left side).
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        struct Part
        {
            public string bone, toward;   // frame: from bone to 'toward' (null = world up)
            public Mesh mesh;
            public Vector3 pos, euler;
            public bool mirror;
            public float scale;
        }

        static readonly Dictionary<(byte set, int slot), List<Part>> _armorParts = new Dictionary<(byte, int), List<Part>>();

        // ================================================================== mesh helpers
        /// <summary>Ellipsoid cap: latitude from lat0 to lat1 (degrees, 90 = top), radii rx/ry/rz.</summary>
        static void Dome(MeshBuilder mb, Vector3 c, Vector3 r, float lat0, float lat1, int sides = 12, int rings = 6, float lon0 = 0f, float lon1 = 360f)
        {
            for (int j = 0; j < rings; j++)
            {
                float a0 = Mathf.Lerp(lat0, lat1, j / (float)rings) * Mathf.Deg2Rad, a1 = Mathf.Lerp(lat0, lat1, (j + 1) / (float)rings) * Mathf.Deg2Rad;
                for (int i = 0; i < sides; i++)
                {
                    float b0 = Mathf.Lerp(lon0, lon1, i / (float)sides) * Mathf.Deg2Rad, b1 = Mathf.Lerp(lon0, lon1, (i + 1) / (float)sides) * Mathf.Deg2Rad;
                    Vector3 P(float a, float b) => c + new Vector3(Mathf.Cos(a) * Mathf.Sin(b) * r.x, Mathf.Sin(a) * r.y, Mathf.Cos(a) * Mathf.Cos(b) * r.z);
                    Vector3 p00 = P(a0, b0), p01 = P(a0, b1), p10 = P(a1, b0), p11 = P(a1, b1);
                    Vector3 n = (p00 + p11) * 0.5f - c;
                    mb.AddQuad(p00, p01, p11, p10, n);
                }
            }
        }

        /// <summary>A curved horn / spike: a chain of tapering prisms bending by 'bend' (degrees about X) per segment.</summary>
        static void Horn(MeshBuilder mb, Vector3 root, Vector3 dir, float len, float r, float bend, Vector3 bendAxis, int segs = 5)
        {
            Vector3 p = root, d = dir.normalized;
            for (int i = 0; i < segs; i++)
            {
                float r0 = r * (1f - i / (float)segs), r1 = r * (1f - (i + 1) / (float)segs) + 0.002f;
                Vector3 q = p + d * (len / segs);
                mb.AddPrism(p, q, r0, r1, 5, i == 0, false, 900 + i, 0f);
                d = Quaternion.AngleAxis(bend, bendAxis) * d;
                p = q;
            }
        }

        static void Ring(MeshBuilder mb, Vector3 c, float radius, float thick, float height, int sides = 24, Vector3? axisUp = null)
        {
            // flat ring band (halo): inner/outer radius, small height
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 o0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)), o1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                Vector3 h = Vector3.up * height * 0.5f;
                Vector3 A = c + o0 * radius, B = c + o1 * radius, C = c + o1 * (radius - thick), D = c + o0 * (radius - thick);
                mb.AddQuad(A + h, B + h, C + h, D + h, Vector3.up);
                mb.AddQuad(D - h, C - h, B - h, A - h, Vector3.down);
                mb.AddQuad(A - h, B - h, B + h, A + h, o0 + o1);
                mb.AddQuad(D + h, C + h, C - h, D - h, -(o0 + o1));
            }
        }

        static Mesh ArmorMesh(string name, params (string mat, System.Action<MeshBuilder> build)[] parts)
        {
            var list = new List<(string, MeshBuilder)>();
            foreach (var (mat, build) in parts) { var mb = new MeshBuilder { uvScale = 2.5f }; build(mb); list.Add((mat, mb)); }
            return CombineParts(name, list);
        }

        static void AddPart(byte set, Inventory.Slot slot, string bone, string toward, Mesh mesh, Vector3 pos, Vector3 euler = default, bool both = false, float scale = 1f)
        {
            if (!_armorParts.TryGetValue((set, (int)slot), out var l)) _armorParts[(set, (int)slot)] = l = new List<Part>();
            if (both)
            {
                l.Add(new Part { bone = bone + "_R", toward = toward != null ? toward + "_R" : null, mesh = mesh, pos = pos, euler = euler, mirror = false, scale = scale });
                l.Add(new Part { bone = bone + "_L", toward = toward != null ? toward + "_L" : null, mesh = mesh, pos = pos, euler = euler, mirror = true, scale = scale });
            }
            else l.Add(new Part { bone = bone, toward = toward, mesh = mesh, pos = pos, euler = euler, scale = scale });
        }

        // ================================================================== materials and the four sets
        static void BuildArmorMaterials()
        {
            string T(string n) => $"{Gen}/Textures/{n}.png";
            var steel = AssetDatabase.LoadAssetAtPath<Texture2D>(T("steel"));
            var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(T("rust_metal"));
            var ash = AssetDatabase.LoadAssetAtPath<Texture2D>(T("ash"));
            var bs = LitMat("ArmorBlackSteel", steel, new Color(0.22f, 0.21f, 0.23f), emission: new Color(0.01f, 0.005f, 0f)); bs.SetFloat("_Smoothness", 0.55f);
            LitMat("ArmorWyrmScale", metal, new Color(0.55f, 0.12f, 0.08f), emission: new Color(0.12f, 0.02f, 0f));
            LitMat("ArmorWyrmDark", metal, new Color(0.16f, 0.07f, 0.06f));
            var halo = LitMat("ArmorHalo", null, new Color(1f, 0.95f, 0.8f), emission: new Color(2.6f, 2.3f, 1.6f));
            LitMat("ArmorGlass", null, new Color(0.75f, 0.85f, 1f), emission: new Color(0.7f, 0.9f, 1.6f));
            LitMat("ArmorShadow", ash, new Color(0.2f, 0.17f, 0.24f), emission: new Color(0.04f, 0.01f, 0.08f));
            LitMat("ArmorSoul", null, new Color(0.75f, 0.45f, 1f), emission: new Color(1.0f, 0.45f, 1.8f));
            // muted, not cartoonish: the halo and glass only glow softly
            halo.SetColor("_EmissionColor", new Color(1.1f, 0.95f, 0.65f));
            M["ArmorGlass"].SetColor("_EmissionColor", new Color(0.3f, 0.4f, 0.65f));

            // ---- the plates: generated textures with normal maps (engraved steel, wyrm scales, filigree silver, shroud)
            Material Plate(string name, string tex, Color tint, float smooth, float spec)
            {
                var (alb, nrm) = PlateTextures(tex);
                var m = LitMat(name, alb, tint);
                m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Smoothness", smooth);
                if (spec > 0f)
                {
                    m.SetFloat("_SpecularHighlights", 1f); m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    m.SetColor("_SpecColor", new Color(spec, spec, spec));
                }
                EditorUtility.SetDirty(m);
                return m;
            }
            EditorUtility.SetDirty(halo); EditorUtility.SetDirty(M["ArmorGlass"]);
            Plate("PlateAshen", "plate_engraved", new Color(0.42f, 0.4f, 0.4f), 0.45f, 0.35f);
            Plate("PlateWyrm", "wyrm_scales", new Color(0.62f, 0.3f, 0.24f), 0.35f, 0.25f);
            Plate("PlateMirror", "silver_filigree", new Color(0.78f, 0.78f, 0.8f), 0.6f, 0.55f);
            Plate("PlateDeath", "shroud_cloth", new Color(0.42f, 0.39f, 0.45f), 0.05f, 0f);
        }

        /// <summary>Albedo (grey, tinted by the material) + normal map of a plate pattern, 256 px, tileable.</summary>
        static (Texture2D albedo, Texture2D normal) PlateTextures(string kind)
        {
            const int N = 256;
            var h = new float[N * N]; var a = new float[N * N];
            float F(float u, float v, int f, int o, int seed) => TextureGen.TFbm(u, v, f, o, seed);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N, ht = 0f, al = 0.5f;
                    switch (kind)
                    {
                        case "plate_engraved":
                        {
                            // overlapping lames (bands), a bevel at each edge, rivets along it, engraved scrolls
                            float bv = (v * 4f) % 1f;
                            float bevel = Mathf.SmoothStep(0f, 0.08f, bv) * (1f - Mathf.SmoothStep(0.9f, 1f, bv));
                            float rx = (u * 16f) % 1f - 0.5f, ry = (bv - 0.86f) * 4f;
                            float rivet = Mathf.Clamp01(1f - (rx * rx + ry * ry) * 16f);
                            float scroll = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 12f + Mathf.Sin(v * Mathf.PI * 8f) * 1.6f));
                            float band = Mathf.SmoothStep(0.25f, 0.3f, bv) * (1f - Mathf.SmoothStep(0.62f, 0.67f, bv));
                            float groove = band * Mathf.SmoothStep(0.06f, 0f, scroll);
                            float brushed = F(u * 8f, v, 32, 2, 3) * 0.15f;
                            ht = bevel * 0.7f + rivet * 0.6f - groove * 0.35f + brushed * 0.3f;
                            al = 0.42f + brushed + rivet * 0.25f - groove * 0.25f + (1f - bevel) * 0.12f - F(u, v, 6, 4, 7) * 0.18f;
                            break;
                        }
                        case "wyrm_scales":
                        {
                            float sy = v * 10f; int row = Mathf.FloorToInt(sy);
                            float sx = u * 8f + (row % 2) * 0.5f;
                            float fx = sx % 1f - 0.5f, fy = sy % 1f;
                            // a scale: rounded at the bottom, overlapped by the row above
                            float d = Mathf.Sqrt(fx * fx * 1.3f + (fy - 0.15f) * (fy - 0.15f));
                            float scale = Mathf.Clamp01(1f - d * 1.6f);
                            float ridge = Mathf.Clamp01(1f - Mathf.Abs(fx) * 9f) * scale * 0.3f;
                            float var1 = TextureGen.TNoise(Mathf.Floor(sx) * 3.1f, row * 1.7f, 64, 11);
                            ht = scale * 0.8f + ridge;
                            al = 0.25f + scale * 0.45f + var1 * 0.15f - (scale < 0.05f ? 0.15f : 0f) + F(u, v, 16, 3, 5) * 0.1f;
                            break;
                        }
                        case "silver_filigree":
                        {
                            float pv = (v * 3f) % 1f, pu = (u * 3f) % 1f;
                            float panel = Mathf.SmoothStep(0f, 0.04f, pv) * (1f - Mathf.SmoothStep(0.96f, 1f, pv)) * Mathf.SmoothStep(0f, 0.04f, pu) * (1f - Mathf.SmoothStep(0.96f, 1f, pu));
                            float cx = pu - 0.5f, cy = pv - 0.5f, r = Mathf.Sqrt(cx * cx + cy * cy), ang = Mathf.Atan2(cy, cx);
                            float swirl = Mathf.Abs(Mathf.Sin(r * 40f - ang * 2f));
                            float fil = Mathf.SmoothStep(0.12f, 0f, swirl) * Mathf.SmoothStep(0.42f, 0.3f, r);
                            float tarnish = F(u, v, 5, 4, 21);
                            ht = panel * 0.6f + fil * 0.4f;
                            al = 0.6f + fil * 0.25f + panel * 0.1f - tarnish * 0.25f - (1f - panel) * 0.2f;
                            break;
                        }
                        default: // shroud_cloth
                        {
                            float wu = Mathf.Sin(u * Mathf.PI * 2f * 64f), wv = Mathf.Sin(v * Mathf.PI * 2f * 64f);
                            float weave = (wu * 0.5f + 0.5f) * ((Mathf.FloorToInt(v * 64f) % 2 == 0) ? 1f : 0.6f) * 0.5f + (wv * 0.5f + 0.5f) * 0.5f;
                            float folds = Mathf.Sin(u * Mathf.PI * 2f * 5f + F(u, v, 3, 2, 31) * 4f) * 0.5f + 0.5f;
                            float stain = F(u, v, 4, 4, 33);
                            ht = weave * 0.25f + folds * 0.6f;
                            al = 0.3f + weave * 0.12f + folds * 0.15f - stain * 0.2f;
                            break;
                        }
                    }
                    h[y * N + x] = ht; a[y * N + x] = Mathf.Clamp01(al);
                }
            var albedo = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var normal = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var pa = new Color[N * N]; var pn = new Color[N * N];
            float strength = kind == "shroud_cloth" ? 2.5f : 4f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float hl = h[y * N + (x + N - 1) % N], hr = h[y * N + (x + 1) % N], hd = h[((y + N - 1) % N) * N + x], hu = h[((y + 1) % N) * N + x];
                    var n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                    pn[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    float g = a[y * N + x];
                    pa[y * N + x] = new Color(g, g, g, 1f);
                }
            albedo.SetPixels(pa); albedo.Apply(); normal.SetPixels(pn); normal.Apply();
            string dir = $"{Gen}/Textures/Armor";
            Directory.CreateDirectory(dir);
            Texture2D Write(Texture2D t, string file, bool isNormal)
            {
                string path = $"{dir}/{file}.png";
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.anisoLevel = 2;
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return (Write(albedo, kind, false), Write(normal, kind + "_n", true));
        }

        /// <summary>
        /// The ornaments only (crowns, horns, halo, pauldrons): the plates themselves are skin shells
        /// grown from each character's own body (see SkinShell), so they fit and move like the skin.
        /// </summary>
        static void BuildArmorSets()
        {
            _armorParts.Clear();
            var H = Inventory.Slot.Helmet; var C = Inventory.Slot.Chest;
            void Pauldron(MeshBuilder mb, float r = 0.1f)
            {
                Dome(mb, new Vector3(0.02f, -0.02f, 0f), new Vector3(r, r * 0.8f, r * 1.05f), 0f, 90f, 10, 4);
                Dome(mb, new Vector3(0.03f, 0.01f, 0f), new Vector3(r * 1.1f, r * 0.45f, r * 1.1f), -10f, 25f, 10, 2);
            }

            // ---------------- ASHEN KING: a gold crown with ember jewels, gilded pauldrons
            {
                byte S = Inventory.ArmorAshen;
                AddPart(S, H, "Head", null, ArmorMesh("Orn_Ashen_Crown",
                    ("RelicGold", mb =>
                    {
                        mb.AddPrism(new Vector3(0, 0.17f, -0.01f), new Vector3(0, 0.2f, -0.01f), 0.122f, 0.116f, 14, false, false, 6, 0f);
                        for (int i = 0; i < 7; i++) { float a = (i / 7f) * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Sin(a) * 0.116f, 0.2f, Mathf.Cos(a) * 0.12f); mb.AddPrism(o, o + Vector3.up * (i % 2 == 0 ? 0.06f : 0.035f), 0.014f, 0.002f, 4, false, false, 7 + i, 0f); }
                    }),
                    ("RelicEmber", mb => { foreach (int s in new[] { -1, 0, 1 }) Gem(mb, new Vector3(s * 0.07f, 0.185f, 0.1f - Mathf.Abs(s) * 0.03f), new Vector3(0.014f, 0.014f, 0.01f)); })), Vector3.zero);
                AddPart(S, C, "Upper_Arm", "Lower_Arm", ArmorMesh("Orn_Ashen_Pauldron", ("PlateAshen", mb => Pauldron(mb)), ("RelicGold", mb => Dome(mb, new Vector3(0.03f, 0.01f, 0f), new Vector3(0.115f, 0.05f, 0.115f), -12f, -2f, 10, 1))), new Vector3(0.015f, 0.03f, 0f), default, true);
            }
            // ---------------- VAELTHRAX: swept bone horns and a crest, spiked pauldrons
            {
                byte S = Inventory.ArmorWyrm;
                AddPart(S, H, "Head", null, ArmorMesh("Orn_Wyrm_Horns",
                    ("RelicBone", mb =>
                    {
                        foreach (int s in new[] { -1, 1 }) Horn(mb, new Vector3(s * 0.09f, 0.16f, -0.02f), new Vector3(s * 0.5f, 0.45f, -0.75f), 0.17f, 0.026f, -16f * s, Vector3.forward, 5);
                        for (int i = 0; i < 4; i++) mb.AddPrism(new Vector3(0, 0.22f - i * 0.045f, -0.06f - i * 0.035f), new Vector3(0, 0.28f - i * 0.05f, -0.1f - i * 0.04f), 0.015f, 0.002f, 4, false, false, 21 + i, 0f);
                    })), Vector3.zero);
                AddPart(S, C, "Upper_Arm", "Lower_Arm", ArmorMesh("Orn_Wyrm_Pauldron", ("PlateWyrm", mb => Pauldron(mb, 0.105f)),
                    ("RelicBone", mb => { for (int i = 0; i < 3; i++) Horn(mb, new Vector3(0.05f, -0.05f + i * 0.035f, -0.03f + i * 0.035f), new Vector3(1f, -0.5f, 0f), 0.1f - i * 0.015f, 0.018f, 12f, Vector3.forward, 3); })), new Vector3(0.015f, 0.03f, 0f), default, true);
            }
            // ---------------- THE MIRROR: a floating halo, a glass crest, silver wings at the temples, sparkles
            {
                byte S = Inventory.ArmorMirror;
                AddPart(S, H, "Head", null, ArmorMesh("Orn_Mirror_Halo",
                    ("RadiantSilver", mb => { foreach (int s in new[] { -1, 1 }) for (int i = 0; i < 4; i++) mb.AddPrism(new Vector3(s * 0.12f, 0.08f + i * 0.025f, -0.02f), new Vector3(s * (0.17f + i * 0.012f), 0.13f + i * 0.03f, -0.09f - i * 0.018f), 0.013f, 0.002f, 4, false, false, 30 + i, 0f); }),
                    ("ArmorGlass", mb => { Horn(mb, new Vector3(0, 0.21f, 0.05f), new Vector3(0, 1f, 0.4f), 0.06f, 0.018f, 0f, Vector3.right, 2); }),
                    ("ArmorHalo", mb => Ring(mb, new Vector3(0, 0.31f, -0.04f), 0.1f, 0.012f, 0.009f, 28))), Vector3.zero);
                AddPart(S, C, "Upper_Arm", "Lower_Arm", ArmorMesh("Orn_Mirror_Pauldron", ("PlateMirror", mb => Pauldron(mb)),
                    ("RadiantGold", mb => Dome(mb, new Vector3(0.03f, 0.01f, 0f), new Vector3(0.115f, 0.05f, 0.115f), -12f, -2f, 12, 1)),
                    ("ArmorGlass", mb => { for (int i = 0; i < 3; i++) Horn(mb, new Vector3(0.06f, -0.06f + i * 0.02f, -0.04f + i * 0.04f), new Vector3(0.7f, -1f, 0f), 0.07f + i * 0.015f, 0.013f, 0f, Vector3.forward, 2); })), new Vector3(0.015f, 0.03f, 0f), default, true);
            }
            // ---------------- THE REAPER: the hood's falling tip, skull pauldrons
            {
                byte S = Inventory.ArmorDeath;
                AddPart(S, H, "Head", null, ArmorMesh("Orn_Death_HoodTip", ("ArmorShadow", mb => Horn(mb, new Vector3(0, 0.2f, -0.07f), new Vector3(0, 0.5f, -1f), 0.12f, 0.04f, 28f, Vector3.right, 4))), Vector3.zero);
                AddPart(S, C, "Upper_Arm", "Lower_Arm", ArmorMesh("Orn_Death_Pauldron", ("PlateDeath", mb => Pauldron(mb, 0.105f)),
                    ("MirrorBone", mb => AddSkull(mb, new Vector3(0.075f, -0.02f, 0f), 0.09f, Quaternion.Euler(0, 90, 0)))), new Vector3(0.015f, 0.03f, 0f), default, true);
            }
        }

        /// <summary>Main plate material of each set (the skin shells).</summary>
        static string ShellMaterial(byte set) => set switch
        {
            Inventory.ArmorAshen => "PlateAshen", Inventory.ArmorWyrm => "PlateWyrm",
            Inventory.ArmorMirror => "PlateMirror", _ => "PlateDeath",
        };

        static readonly (Inventory.Slot slot, string[] bones, float thick)[] ShellBones =
        {
            (Inventory.Slot.Helmet, new[] { "Head" }, 0.014f),
            (Inventory.Slot.Chest, new[] { "Hips", "Spine", "Spine 1", "Chest", "Neck", "Shoulder_L", "Shoulder_R", "Upper_Arm_L", "Upper_Arm_R" }, 0.016f),
            (Inventory.Slot.Legs, new[] { "Upper_Leg_L", "Upper_Leg_R", "Lower_Leg_L", "Lower_Leg_R" }, 0.013f),
            (Inventory.Slot.Boots, new[] { "Foot_L", "Foot_R", "Toes_L", "Toes_R" }, 0.015f),
        };

        /// <summary>
        /// A plate grown from the character's own skin: the triangles bound to the given bones, pushed
        /// out along their normals by 'thick' metres, keeping the skin's bone weights (so the plate
        /// bends with every animation exactly like the body) and given wrap-around UVs for the metal.
        /// </summary>
        static Mesh SkinShell(SkinnedMeshRenderer skin, string[] boneNames, float thick, string name)
        {
            var src = skin.sharedMesh;
            var bones = skin.bones;
            var want = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++) if (bones[b] && System.Array.IndexOf(boneNames, bones[b].name) >= 0) want.Add(b);
            if (want.Count == 0) return null;
            var v = src.vertices; var n = src.normals; var w = src.boneWeights;
            if (n.Length != v.Length || w.Length != v.Length) return null;
            float scale = Mathf.Max(1e-4f, skin.transform.lossyScale.x);
            float t = thick / scale;
            var map = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nw = new List<BoneWeight>(); var uv = new List<Vector2>(); var tris = new List<int>();
            int Map(int i)
            {
                if (map.TryGetValue(i, out int j)) return j;
                j = nv.Count; map[i] = j;
                Vector3 p = v[i] + n[i].normalized * t;
                nv.Add(p); nn.Add(n[i]); nw.Add(w[i]);
                uv.Add(new Vector2((Mathf.Atan2(p.x, p.z) / Mathf.PI + 1f) * 2f, p.y * 4.5f * scale));
                return j;
            }
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var it = src.GetTriangles(sm);
                for (int k = 0; k < it.Length; k += 3)
                {
                    int inside = 0;
                    for (int q = 0; q < 3; q++) if (want.Contains(w[it[k + q]].boneIndex0)) inside++;
                    if (inside < 2) continue;
                    tris.Add(Map(it[k])); tris.Add(Map(it[k + 1])); tris.Add(Map(it[k + 2]));
                }
            }
            if (tris.Count == 0) return null;
            var m = new Mesh { name = name, indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.boneWeights = nw.ToArray();
            m.bindposes = src.bindposes;
            m.RecalculateBounds();
            m.RecalculateTangents();
            string dir = Gen + "/Meshes/Armor";
            System.IO.Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ================================================================== fitting on a class model
        /// <summary>Frame of a bone in the current (idle) pose: Y toward the child joint, Z forward, X outward.</summary>
        static Quaternion BoneFrame(Transform model, Transform bone, Transform toward, bool left)
        {
            Vector3 up = toward ? (toward.position - bone.position).normalized : Vector3.up;
            if (bone.name.StartsWith("Foot")) up = Vector3.up;
            Vector3 fwd = model.forward;
            if (bone.name.StartsWith("Foot"))
            {
                var toes = FindDeep(bone, "Toes_" + (left ? "L" : "R"));
                if (toes) fwd = Vector3.ProjectOnPlane(toes.position - bone.position, Vector3.up).normalized;
            }
            fwd = Vector3.ProjectOnPlane(fwd, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(model.forward, up);
            return Quaternion.LookRotation(fwd.normalized, up);
        }

        /// <summary>Adds the armour pieces (inactive) and a PlayerArmor to a saved player prefab.</summary>
        static void FitArmor(GameObject prefab, string[] hideWithHelmet = null)
        {
            if (prefab == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            var model = root.GetComponentInChildren<Animator>().transform;
            var pa = root.GetComponent<PlayerArmor>() ?? root.AddComponent<PlayerArmor>();
            pa.pieces.Clear();
            // the skin, and how thick the body is around each bone (pieces are sized to it)
            var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh).OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
            pa.body = skin;
            var girth = BoneGirth(skin);
            // the plates: skin shells, one per set and slot, siblings of the skin with its bones
            if (skin != null)
                for (byte set = 1; set < Inventory.ArmorSetCount; set++)
                    foreach (var (slot, names, thick) in ShellBones)
                    {
                        var mesh = SkinShell(skin, names, thick, $"Shell_{prefab.name}_{set}_{(int)slot}");
                        if (mesh == null) continue;
                        var go = new GameObject($"Plate_{set}_{slot}");
                        go.transform.SetParent(skin.transform.parent, false);
                        go.transform.localPosition = skin.transform.localPosition; go.transform.localRotation = skin.transform.localRotation; go.transform.localScale = skin.transform.localScale;
                        var smr = go.AddComponent<SkinnedMeshRenderer>();
                        smr.sharedMesh = mesh; smr.bones = skin.bones; smr.rootBone = skin.rootBone;
                        smr.sharedMaterial = M[ShellMaterial(set)];
                        smr.updateWhenOffscreen = false; smr.localBounds = skin.localBounds;
                        go.SetActive(false);
                        pa.pieces.Add(new PlayerArmor.Piece { set = set, slot = slot, go = go });
                    }
            var pc = root.GetComponent<PlayerController>();
            if (pc) pc.armor = pa;
            var headB = FindDeep(model, "Head");
            float k = headB ? Mathf.Clamp((headB.position.y - root.transform.position.y) / 1.58f, 0.7f, 1.5f) : 1f;
            Debug.Log($"[DF] armour fit on {prefab.name}: scale {k:0.00}");
            foreach (var kv in _armorParts)
            {
                foreach (var part in kv.Value)
                {
                    var bone = FindDeep(model, part.bone);
                    if (!bone) { Debug.LogWarning("[DF] armour: bone missing " + part.bone); continue; }
                    var toward = part.toward != null ? FindDeep(model, part.toward) : null;
                    if (part.bone == "Chest") toward = FindDeep(model, "Neck");
                    if (part.bone == "Hips") toward = FindDeep(model, "Spine");
                    bool left = part.mirror;
                    var rot = BoneFrame(root.transform, bone, toward, left);
                    var go = RelicObject(part.mesh.name, part.mesh.name, null);
                    go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
                    Vector3 lp = part.pos; if (left) lp.x = -lp.x;
                    go.transform.SetPositionAndRotation(bone.position + rot * (lp * k), rot * Quaternion.Euler(part.euler));
                    // lateral size from the body's girth around this bone (fallback: the height ratio)
                    float f = k;
                    if (girth.TryGetValue(part.bone, out float g) && DesignGirth.TryGetValue(BoneKind(part.bone), out float d))
                        f = Mathf.Clamp(g * 1.04f / d, k * 0.75f, k * 1.7f);
                    if (part.bone.StartsWith("Head")) f = Mathf.Clamp(f * 0.9f, k * 0.7f, k * 0.95f);   // helmets: snug, never oversized
                    Vector3 sc = part.bone.StartsWith("Head") ? new Vector3(f, f, f) : part.bone.StartsWith("Foot") ? new Vector3(f, f, k) : new Vector3(f, k, f);
                    if (left) sc.x = -sc.x;
                    go.transform.localScale = sc * (part.scale <= 0f ? 1f : part.scale);
                    go.transform.SetParent(bone, true);
                    go.SetActive(false);
                    pa.pieces.Add(new PlayerArmor.Piece { set = kv.Key.set, slot = (Inventory.Slot)kv.Key.slot, go = go });
                    // the Mirror's crown: a soft light and drifting motes of light
                    if (kv.Key.set == Inventory.ArmorMirror && kv.Key.slot == (int)Inventory.Slot.Helmet)
                    {
                        var fx = RadiantSparkles(go.transform, new Vector3(0, 0.33f, -0.05f), new Vector3(0.2f, 0.04f, 0.2f), 10f, 2f);
                        fx.SetActive(true);
                    }
                    if (kv.Key.set == Inventory.ArmorWyrm && kv.Key.slot == (int)Inventory.Slot.Chest && part.bone == "Chest")
                        DragonEmbers(go.transform, new Vector3(0, 0.1f, 0.15f), new Vector3(0.3f, 0.3f, 0.1f), 10f, 2f).SetActive(true);
                }
            }
            if (hideWithHelmet != null)
                pa.hideWithHelmet = hideWithHelmet.Select(n => FindDeep(model, n)).Where(t => t).Select(t => t.gameObject).ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>The radius each piece was drawn for (metres, for a 1.85 m knight).</summary>
        static readonly Dictionary<string, float> DesignGirth = new Dictionary<string, float>
        {
            { "Head", 0.155f }, { "Chest", 0.22f }, { "Hips", 0.2f }, { "Upper_Arm", 0.1f },
            { "Upper_Leg", 0.095f }, { "Lower_Leg", 0.075f }, { "Foot", 0.058f },
        };

        static string BoneKind(string bone) => bone.EndsWith("_L") || bone.EndsWith("_R") ? bone.Substring(0, bone.Length - 2) : bone;

        /// <summary>90th-percentile distance of the skin from each bone (world metres), by bone name.</summary>
        static Dictionary<string, float> BoneGirth(SkinnedMeshRenderer skin)
        {
            var res = new Dictionary<string, float>();
            if (skin == null) return res;
            var mesh = skin.sharedMesh; var bones = skin.bones; var bind = mesh.bindposes;
            var verts = mesh.vertices; var w = mesh.boneWeights;
            if (w.Length != verts.Length) return res;
            float scale = skin.transform.lossyScale.x;
            var dists = new List<float>[bones.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int b = w[i].boneIndex0;
                if (b < 0 || b >= bones.Length || bones[b] == null) continue;
                Vector3 a = bind[b].inverse.GetColumn(3), e = a;
                for (int c = 0; c < bones.Length; c++) if (bones[c] != null && bones[c].parent == bones[b]) { e = bind[c].inverse.GetColumn(3); break; }
                Vector3 ab = e - a;
                float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(verts[i] - a, ab) / ab.sqrMagnitude);
                (dists[b] ??= new List<float>()).Add(Vector3.Distance(verts[i], a + ab * t) * scale);
            }
            for (int b = 0; b < bones.Length; b++)
            {
                if (dists[b] == null || dists[b].Count < 8 || bones[b] == null) continue;
                dists[b].Sort();
                res[bones[b].name] = dists[b][(int)(dists[b].Count * 0.9f)];
            }
            return res;
        }

        static void BuildArmor(Prefabs p)
        {
            // the skins must be readable: worn pieces draw the body in toward the bones at runtime (PlayerArmor)
            foreach (var fbx in new[] { KnightFbx, ArcherFbx, BardFbx, ReaperFbx })
                if (AssetImporter.GetAtPath(fbx) is ModelImporter mi && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); }
            BuildArmorSets();
            FitArmor(p.player);
            FitArmor(p.archer);
            FitArmor(p.bard);
            FitArmor(p.reaper);
            FitArmor(p.smoker, new[] { "Hat" });
        }
    }
}
