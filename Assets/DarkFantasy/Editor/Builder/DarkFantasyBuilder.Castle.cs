using System.Collections.Generic;
using System.Linq;
using DarkFantasy.Procedural;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Chapter 2 — the castle interior, the prisons and the dragon's terrace.
    ///
    ///   throne room ──door (opens when the King falls)──► Flooded Gallery (C1, z 66→104)
    ///        C1 ─ z 82 ─► West Passage ─► Ossuary ─► West Stair-hall ─► Gaolers' Hall ─► west cells ─► Hall of Chains
    ///        C1 ─ z 82 ─► East Passage ─► Barracks ─► East Passage  ─► Warden's Office (KEY) ─► east cells ─► Hall of Chains
    ///        passages also lead to the Torture Chamber (west) and the Drowned Chapel (east)
    ///   Hall of Chains (z 104→136) ─GREAT DOOR (key)─► Grand Stair (y 0 → 7.5) ─fog─► Terrace: the dragon.
    /// Every room is a rectangle with thick walls, pointed-arch doorways and a pointed vault (CastleKit),
    /// lit only by torches; shallow water lies in the passages.
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const string DragonFbx = "Assets/DarkFantasy/Art/Dragon/Dragon.fbx";
        const string DragonTex = "Assets/DarkFantasy/Art/Dragon/Dragon_Color.png";
        const float TerraceY = 7.5f;

        static readonly Color TorchColor = new Color(1f, 0.52f, 0.24f);

        // filled while building the level, wired into LevelRefs afterwards
        static readonly List<(Vector3 p, float yaw)> _gaolers = new List<(Vector3, float)>();
        static readonly List<(Vector3 p, float yaw)> _bowmen = new List<(Vector3, float)>();
        static readonly List<(Vector3 p, float yaw)> _wardens = new List<(Vector3, float)>();
        static readonly List<(string name, Vector3 p, float yaw)> _checkpoints = new List<(string, Vector3, float)>();
        static int _seed = 900;

        // ================================================================== materials / sounds
        static void BuildCastleMaterials()
        {
            string T(string n) => $"{Gen}/Textures/{n}.png";
            var ring = AssetDatabase.LoadAssetAtPath<Texture2D>(T("shock_ring"));
            var fog = AssetDatabase.LoadAssetAtPath<Texture2D>(T("fog"));
            var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(T("rust_metal"));
            var bricks = AssetDatabase.LoadAssetAtPath<Texture2D>(T("stone_bricks_castle"));

            TransparentMat("Ripple", ring, new Color(0.72f, 0.78f, 0.82f, 0.55f), false, "Universal Render Pipeline/Particles/Unlit");

            // dark, glossy, slightly transparent water: the torches glint on it
            const string WaterNormal = "Assets/DarkFantasy/Art/Env/water_normal.png";
            if (AssetImporter.GetAtPath(WaterNormal) is TextureImporter wni)
            {
                wni.textureType = TextureImporterType.NormalMap; wni.wrapMode = TextureWrapMode.Repeat; wni.mipmapEnabled = true;
                wni.SaveAndReimport();
            }
            var wn = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterNormal);
            // Particles/Lit: lit + glossy like Lit, but it multiplies the VERTEX colour, so the puddle mesh
            // can fade its edges smoothly into the dry stone (alpha painted per vertex)
            var w = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            w.SetTexture("_BaseMap", fog);
            w.SetFloat("_ColorMode", 0f);
            // pale, glossy and a little emissive: the shallow water reads from far away in the dark
            // corridors (torch highlights dance on the moving normal map)
            // cold blue-grey against the warm torch-lit stone: the puddles read by colour from far away
            w.SetColor("_BaseColor", new Color(0.22f, 0.32f, 0.4f, 0.86f));
            w.SetFloat("_Smoothness", 0.97f);
            if (wn != null) { w.SetTexture("_BumpMap", wn); w.SetFloat("_BumpScale", 0.55f); w.EnableKeyword("_NORMALMAP"); }
            w.SetColor("_EmissionColor", new Color(0.05f, 0.085f, 0.11f));
            w.EnableKeyword("_EMISSION");
            w.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;   // else URP treats the emission as black and strips it
            w.SetFloat("_SpecularHighlights", 1f);
            w.SetFloat("_Metallic", 0f);
            w.SetFloat("_Surface", 1f);
            w.SetFloat("_Blend", 0f);
            w.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            w.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            w.SetFloat("_ZWrite", 0f);
            w.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            w.SetOverrideTag("RenderType", "Transparent");
            w.renderQueue = (int)RenderQueue.Transparent - 10;
            w.SetFloat("_EnvironmentReflections", 1f);      // mirrors the torches via the corridor reflection probes
            SaveMat("Water", w);
            QualitySettings.realtimeReflectionProbes = true;

            LitMat("Iron", metal, new Color(0.42f, 0.4f, 0.39f));
            LitMat("PrisonStone", bricks, new Color(0.6f, 0.58f, 0.55f));
            LitMat("KeyGold", metal, new Color(1f, 0.78f, 0.36f), emission: new Color(0.45f, 0.28f, 0.06f));
            LitMat("CandleWax", null, new Color(0.85f, 0.8f, 0.68f), emission: new Color(0.08f, 0.06f, 0.03f));
            LitMat("RoofSlate", bricks, new Color(0.27f, 0.29f, 0.36f));
            var wg = new Material(Shader.Find("Universal Render Pipeline/Unlit")); wg.SetColor("_BaseColor", new Color(1f, 0.6f, 0.28f)); SaveMat("WindowGlow", wg);
            var wd = new Material(Shader.Find("Universal Render Pipeline/Unlit")); wd.SetColor("_BaseColor", new Color(0.04f, 0.035f, 0.04f)); SaveMat("WindowDark", wd);
            // sunset sky dome (terrace)
            const string SkyTex = "Assets/DarkFantasy/Art/Env/sky_sunset.png";
            if (AssetImporter.GetAtPath(SkyTex) is TextureImporter sti)
            { sti.wrapModeU = TextureWrapMode.Repeat; sti.wrapModeV = TextureWrapMode.Clamp; sti.mipmapEnabled = false; sti.textureCompression = TextureImporterCompression.Uncompressed; sti.maxTextureSize = 1024; sti.SaveAndReimport(); }
            var skyMat = new Material(Shader.Find("DarkFantasy/SkyDome"));
            skyMat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTex));
            SaveMat("SkyDome", skyMat);

            var knight = AssetDatabase.LoadAssetAtPath<Texture2D>(KnightTex);
            LitMat("KnightGaoler", knight, new Color(0.3f, 0.29f, 0.26f), emission: new Color(0.015f, 0.01f, 0.005f));
            LitMat("KnightWarden", knight, new Color(0.42f, 0.24f, 0.18f), emission: new Color(0.04f, 0.015f, 0.008f));
            var archer = AssetDatabase.LoadAssetAtPath<Texture2D>(ArcherTex);
            LitMat("ArcherHollow", archer, new Color(0.42f, 0.4f, 0.37f));

            if (AssetImporter.GetAtPath(DragonTex) is TextureImporter ti)
            {
                ti.maxTextureSize = 1024;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            var dtex = AssetDatabase.LoadAssetAtPath<Texture2D>(DragonTex);
            if (dtex == null) Debug.LogWarning("[DF] Missing " + DragonTex);
            LitMat("DragonSkin", dtex, new Color(0.92f, 0.88f, 0.84f), emission: new Color(0.02f, 0.008f, 0.004f));
        }

        static Sfx.Entry[] CastleSfx()
        {
            return new[]
            {
                // heavy door: low creak + stone grinding
                E(SfxId.DoorOpen,  new Vector2(0.95f, 1.02f), 1.2f, 0.6f, L("Door Open 4-1.wav", 0f, 1f, 0.6f), L("Rock Large Debris 1-3.wav", 0.24f, 0.45f, 0.65f), L("Door Close 4-1.wav", 0f, 0.5f, 0.5f)),
                E(SfxId.WaterStep, new Vector2(0.85f, 1.15f), 0.7f, 0.9f, L("Water Splash 5-2.wav", 0f, 1f, 1.1f)),
                E(SfxId.WaterDrip, new Vector2(0.9f, 1.2f), 0.35f, 0.95f, L("Water Splash 5-2.wav", 0f, 0.6f, 2.2f)),
                E(SfxId.DragonRoar, new Vector2(0.95f, 1.03f), 1.4f, 0.4f, L("Creature 1-21.wav", 0.12f, 1f, 0.42f), L("Creature 1-21.wav", 0.12f, 0.6f, 0.3f), L("Explosion Large 1-1.wav", 0.035f, 0.3f, 0.45f)),
                E(SfxId.DragonBite, new Vector2(0.9f, 1.05f), 1.1f, 0.7f, L("Creature 1-21.wav", 0.12f, 0.8f, 0.75f), L("Punch 2-1.wav", 0.008f, 0.7f, 0.6f)),
                E(SfxId.DragonFire, new Vector2(0.85f, 1.0f), 1.1f, 0.5f, L("Fire Whoosh 2-15.wav", 0f, 1f, 0.6f), L("Fire Whoosh 2-15.wav", 0f, 0.6f, 0.45f)),
                E(SfxId.DragonWing, new Vector2(0.9f, 1.05f), 1f, 0.6f, L("Whoosh 4-1.wav", 0f, 1f, 0.45f), L("Whoosh 1-1.wav", 0.05f, 0.5f, 0.35f)),
                E(SfxId.DragonStep, new Vector2(0.85f, 1.0f), 1f, 0.75f, L("Rock Impact 11.wav", 0f, 0.9f, 0.5f), L("Punch 2-1.wav", 0.008f, 0.7f, 0.45f)),
                E(SfxId.Fireball,  new Vector2(0.9f, 1.1f), 0.9f, 0.7f, L("Fire Whoosh 2-15.wav", 0f, 1f, 1.05f)),
                E(SfxId.DragonTail, new Vector2(0.9f, 1.0f), 1.2f, 0.7f, L("Whoosh 4-1.wav", 0f, 1f, 0.38f), L("Whoosh 1-1.wav", 0.02f, 0.7f, 0.32f)),
                E(SfxId.DragonCharge, new Vector2(0.95f, 1.0f), 1.3f, 0.6f, L("Creature 1-21.wav", 0.12f, 0.9f, 0.55f), L("Rock Large Debris 1-3.wav", 0.24f, 0.6f, 0.6f)),
                E(SfxId.DragonSwoop, new Vector2(0.9f, 1.0f), 1.2f, 0.6f, L("Whoosh 4-1.wav", 0f, 1f, 0.3f), L("Ambient Wind Loop 1.wav", 0.5f, 0.4f, 0.6f)),
                // ---- the bard
                E(SfxId.SpellCast, new Vector2(0.97f, 1.03f), 0.8f, 0.7f, L("Assets/DarkFantasy/Audio/Bard/mando_pluck.wav", 0f, 0.55f, 0.9f), L("Magical Interface 5-1.wav", 0f, 0.35f, 0.8f)),
                E(SfxId.SpellBolt, new Vector2(0.92f, 1.12f), 0.85f, 0.75f, L("Assets/DarkFantasy/Audio/Bard/mando_pluck.wav", 0f, 1f, 1.35f), L("Magical Interface 8-1.wav", 0f, 0.25f, 1.35f)),
                E(SfxId.SpellImpact, new Vector2(0.9f, 1.1f), 0.8f, 0.85f, L("Magical Interface 8-1.wav", 0f, 0.6f, 0.85f), L("Hit Generic 2-1.wav", 0f, 0.5f, 0.9f)),
                E(SfxId.SpellHeal, new Vector2(0.98f, 1.02f), 0.95f, 0.6f, L("Assets/DarkFantasy/Audio/Bard/mando_strum.wav", 0f, 1f, 1.2f), L("Special Collectible 26-1.wav", 0.05f, 0.45f, 0.9f)),
                E(SfxId.SpellAnthem, new Vector2(0.98f, 1.02f), 1f, 0.6f, L("Assets/DarkFantasy/Audio/Bard/mando_long.wav", 0f, 1f, 1f), L("Assets/DarkFantasy/Audio/Bard/mando_strum.wav", 0.18f, 0.6f, 1.5f), L("Draw Weapon Metal 1-1.wav", 0f, 0.4f, 0.9f)),
                E(SfxId.SpellCurse, new Vector2(0.95f, 1.02f), 0.9f, 0.75f, L("Assets/DarkFantasy/Audio/Bard/mando_strum.wav", 0f, 1f, 0.6f), L("Zombie 1 - Short 1-01.wav", 0.05f, 0.25f, 1.4f)),
                E(SfxId.SpellCrescendo, new Vector2(0.97f, 1.03f), 1.2f, 0.7f, L("Assets/DarkFantasy/Audio/Bard/mando_strum.wav", 0f, 1f, 0.8f), L("Explosion Medium 2-1.wav", 0f, 0.7f, 1.15f), L("Rock Impact 37.wav", 0f, 0.5f, 0.8f)),
                E(SfxId.SpellFail, new Vector2(0.95f, 1.0f), 0.5f, 0f, L("Interface 1-1.wav", 0f, 1f, 0.7f)),
                E(SfxId.Rest, new Vector2(0.98f, 1.02f), 1f, 0.5f, L("Fire Whoosh 2-15.wav", 0f, 0.8f, 0.6f), L("Special Collectible 26-1.wav", 0f, 0.6f, 0.6f)),
                // ---- footsteps (one random variation per step; Tools: sliced from the footstep packs)
                Steps(SfxId.StepStone, new Vector2(0.9f, 1.1f), 0.55f, "stone_1", "stone_2"),
                Steps(SfxId.StepDirt, new Vector2(0.9f, 1.1f), 0.6f, "dirt_1", "dirt_2", "dirt_3", "dirt_4"),
                Steps(SfxId.StepGravel, new Vector2(0.9f, 1.1f), 0.55f, "gravel_1", "gravel_2", "gravel_3"),
                Steps(SfxId.StepWater, new Vector2(0.9f, 1.12f), 0.75f, "water_2", "water_3", "water_4", "water_5", "water_6", "water_7", "water_9"),
            };
        }

        static Sfx.Entry Steps(SfxId id, Vector2 pitch, float vol, params string[] files)
        {
            var e = E(id, pitch, vol, 0.95f, files.Select(f => L("Assets/DarkFantasy/Audio/Steps/" + f + ".wav")).ToArray());
            e.pickOne = true;
            return e;
        }

        static void AddLoop(Transform parent, string file, float volume, float maxDistance, float spatial = 1f)
        {
            var clip = Snd(file, true);
            if (clip == null) return;
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.playOnAwake = true;
            src.volume = volume; src.spatialBlend = spatial;
            src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 2f; src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
        }

        // ================================================================== geometry helpers
        struct Op
        {
            public float at, width, spring, apex;
            public Op(float at, float width, float spring, float apex) { this.at = at; this.width = width; this.spring = spring; this.apex = apex; }
        }

        class Room
        {
            public string name;
            public float x0, x1, z0, z1, y;
            public float spring = 3.4f, apex = 5f, rib = 4f;
            public bool alongX;                 // the vault runs along X (the room is long in X)
            public bool openS, openN, openW, openE;
            public readonly List<Op> S = new List<Op>(), N = new List<Op>(), W = new List<Op>(), E = new List<Op>();
            public bool water, floor, vault = true;   // floor: the continuous castle under-floor is used by default
            public float torchEvery = 7f, torchY = 2.5f;
            public string wallMat = "StoneCastle", floorMat = "Flagstones";
            public float wallT = 0.8f;
            public float CX => (x0 + x1) * 0.5f;
            public float CZ => (z0 + z1) * 0.5f;
            public Room(string n, float x0, float x1, float z0, float z1, float y = 0f) { name = n; this.x0 = x0; this.x1 = x1; this.z0 = z0; this.z1 = z1; this.y = y; }
        }

        /// <summary>A straight wall from a to b (horizontal), cut into short pieces (better light culling).</summary>
        static void WallRun(Transform parent, string name, Vector3 a, Vector3 b, float height, float thick, List<Op> ops, string mat)
        {
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.05f) return;
            Vector3 dir = d / len;
            var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
            var cuts = new List<float> { 0f };
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 11f));
            for (int k = 1; k < n; k++)
            {
                float c = len * k / n;
                foreach (var o in ops)
                    if (c > o.at - o.width * 0.5f - 0.5f && c < o.at + o.width * 0.5f + 0.5f) c = o.at + o.width * 0.5f + 0.6f;
                if (c > cuts[cuts.Count - 1] + 1f && c < len - 1f) cuts.Add(c);
            }
            cuts.Add(len);
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                float s0 = cuts[i], s1 = cuts[i + 1], mid = (s0 + s1) * 0.5f;
                var local = new List<CastleKit.Opening>();
                foreach (var o in ops)
                    if (o.at > s0 && o.at < s1) local.Add(new CastleKit.Opening(o.at - mid, o.width, o.spring, o.apex));
                string mn = $"{name}_{i}";
                SaveMesh(CastleKit.Wall(_seed++, s1 - s0, height, thick, local, mn), mn);
                Place(mn, mn, mat, a + dir * mid, rot, parent);
            }
        }

        static void BuildRoom(Transform parent, Room r)
        {
            var root = new GameObject(r.name).transform;
            root.SetParent(parent, false);
            float w = r.x1 - r.x0, d = r.z1 - r.z0, t = r.wallT, y = r.y;
            float H = r.apex + 0.7f;

            if (r.floor)
            {
                string fm = "F_" + r.name;
                SaveMesh(ProceduralMeshes.FlagstoneFloor(_seed++, w, d, 1.25f), fm);
                Place("Floor", fm, r.floorMat, new Vector3(r.CX, y + 0.09f, r.z0), Quaternion.identity, root, true, 1f, false);
            }
            // walls: S/N span the corners, W/E sit between them; 'at' measured from the wall start
            List<Op> Shift(List<Op> ops, float origin) => ops.Select(o => new Op(o.at - origin, o.width, o.spring, o.apex)).ToList();
            if (!r.openS) WallRun(root, "W_" + r.name + "_S", new Vector3(r.x0 - t, y, r.z0 - t * 0.5f), new Vector3(r.x1 + t, y, r.z0 - t * 0.5f), H, t, Shift(r.S, r.x0 - t), r.wallMat);
            if (!r.openN) WallRun(root, "W_" + r.name + "_N", new Vector3(r.x0 - t, y, r.z1 + t * 0.5f), new Vector3(r.x1 + t, y, r.z1 + t * 0.5f), H, t, Shift(r.N, r.x0 - t), r.wallMat);
            if (!r.openW) WallRun(root, "W_" + r.name + "_W", new Vector3(r.x0 - t * 0.5f, y, r.z0), new Vector3(r.x0 - t * 0.5f, y, r.z1), H, t, Shift(r.W, r.z0), r.wallMat);
            if (!r.openE) WallRun(root, "W_" + r.name + "_E", new Vector3(r.x1 + t * 0.5f, y, r.z0), new Vector3(r.x1 + t * 0.5f, y, r.z1), H, t, Shift(r.E, r.z0), r.wallMat);

            if (r.vault)
            {
                string vm = "V_" + r.name;
                if (!r.alongX) SaveMesh(CastleKit.Vault(_seed++, w, d, r.spring, r.apex, r.rib, vm), vm);
                else SaveMesh(CastleKit.Vault(_seed++, d, w, r.spring, r.apex, r.rib, vm), vm);
                Place("Vault", vm, r.wallMat, new Vector3(r.CX, y, r.CZ), r.alongX ? Yaw(90) : Quaternion.identity, root, false);
            }

            // torches along the two long walls, alternating, away from the doorways
            if (r.torchEvery > 0.5f)
            {
                bool longX = r.alongX || (w > d && !r.alongX && w > 6f);
                float len = longX ? w : d;
                int n = Mathf.Max(1, Mathf.FloorToInt(len / r.torchEvery));
                for (int i = 0; i < n; i++)
                {
                    float u = (i + 0.5f) / n * len;
                    bool first = i % 2 == 0;
                    if (longX)
                    {
                        float x = r.x0 + u;
                        bool south = first;
                        if ((south && r.openS) || (!south && r.openN)) south = !south;
                        var ops = south ? r.S : r.N;
                        if (ops.Any(o => Mathf.Abs(o.at - x) < o.width * 0.5f + 0.9f)) continue;
                        float z = south ? r.z0 : r.z1;
                        Torch(root, new Vector3(x, y + r.torchY, z), south ? Vector3.forward : Vector3.back);
                    }
                    else
                    {
                        float z = r.z0 + u;
                        bool west = first;
                        if ((west && r.openW) || (!west && r.openE)) west = !west;
                        var ops = west ? r.W : r.E;
                        if (ops.Any(o => Mathf.Abs(o.at - z) < o.width * 0.5f + 0.9f)) continue;
                        float x = west ? r.x0 : r.x1;
                        Torch(root, new Vector3(x, y + r.torchY, z), west ? Vector3.right : Vector3.left);
                    }
                }
            }

            if (r.water) Water(root, r.name, r.x0 + 0.05f, r.x1 - 0.05f, r.z0 + 0.05f, r.z1 - 0.05f, y, r.apex);
        }

        static void Water(Transform parent, string name, float x0, float x1, float z0, float z1, float y, float ceiling)
        {
            float w = x1 - x0, d = z1 - z0;
            string wm = "Water_" + name;
            SaveMesh(CastleKit.Water(_seed++, w, d), wm);
            var go = Place("Water", wm, "Water", new Vector3((x0 + x1) * 0.5f, y + 0.12f, (z0 + z1) * 0.5f), Quaternion.identity, parent, false, 1f, false);
            go.GetComponent<MeshRenderer>().receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            var sc = go.AddComponent<UVScroll>(); sc.speed = new Vector2(0.012f, 0.03f);
            var wa = go.AddComponent<WaterArea>(); wa.size = new Vector3(w, 0.4f, d);
            // a reflection probe per flooded stretch: the black water mirrors torches and arches
            for (float t = 0f; t < Mathf.Max(w, d); t += 14f)
            {
                var pg = new GameObject("WaterReflection"); pg.transform.SetParent(parent, false);
                Vector3 c = new Vector3((x0 + x1) * 0.5f, y + 1.6f, (z0 + z1) * 0.5f);
                if (d >= w) c.z = z0 + Mathf.Min(d, t + 7f); else c.x = x0 + Mathf.Min(w, t + 7f);
                pg.transform.position = c;
                var rp = pg.AddComponent<ReflectionProbe>();
                rp.mode = ReflectionProbeMode.Realtime;
                rp.refreshMode = ReflectionProbeRefreshMode.OnAwake;
                rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                rp.resolution = 64; rp.hdr = true; rp.boxProjection = true;
                rp.size = new Vector3(Mathf.Min(w, 14f) + 1f, ceiling + 1f, Mathf.Min(d, 14f) + 1f);
                if (d >= w) rp.size = new Vector3(w + 1f, ceiling + 1f, 15f); else rp.size = new Vector3(15f, ceiling + 1f, d + 1f);
                rp.center = new Vector3(0f, ceiling * 0.5f - 1.6f, 0f);
                rp.nearClipPlane = 0.1f; rp.farClipPlane = 30f;
                rp.clearFlags = ReflectionProbeClearFlags.SolidColor; rp.backgroundColor = new Color(0.02f, 0.02f, 0.02f);
                rp.cullingMask = ~0;
                rp.importance = 2;
            }
            // drips from the vault
            int drips = Mathf.Clamp(Mathf.RoundToInt(w * d / 40f), 1, 6);
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < drips; i++)
            {
                var dg = new GameObject("Drip").transform; dg.SetParent(parent, false);
                dg.position = new Vector3(x0 + (float)rng.NextDouble() * w, y + ceiling - 0.6f, z0 + (float)rng.NextDouble() * d);
                var dr = dg.gameObject.AddComponent<WaterDrip>(); dr.floorY = y + 0.12f;
            }
            if (w * d > 30f)
            {
                var a = new GameObject("WaterAmbience").transform; a.SetParent(parent, false);
                a.position = new Vector3((x0 + x1) * 0.5f, y + 1f, (z0 + z1) * 0.5f);
                AddLoop(a, "Ambient Water Stream Calm 3.wav", 0.16f, Mathf.Max(w, d) * 0.6f + 6f);
            }
        }

        static void Torch(Transform parent, Vector3 wallPoint, Vector3 inward, float intensity = 16f, float range = 11.5f)
        {
            var t = Place("Torch", "WallTorch", "RottenWood", wallPoint, Quaternion.LookRotation(inward), parent, false);
            GameObjectUtility.SetStaticEditorFlags(t, 0);
            Fx.CreateFlame(t.transform, new Vector3(0, 0.45f, 0.38f), 0.85f, M["ParticleAdd"]);
            AddLight(t.transform, new Vector3(0, 0.6f, 0.7f), TorchColor, intensity, range, false);
        }

        static void Brazier(Transform parent, Vector3 pos, float intensity, float range, bool shadows = false)
        {
            var br = Place("Brazier", "Brazier", "RustMetal", pos, Quaternion.identity, parent);
            Fx.CreateFlame(br.transform, new Vector3(0, 1.05f, 0), 2.0f, M["ParticleAdd"]);
            AddFireLoop(br.transform, 0.4f, 14f);
            AddLight(br.transform, new Vector3(0, 1.8f, 0), new Color(1f, 0.5f, 0.22f), intensity, range, shadows);
        }

        static void Checkpoint(Transform parent, int index, string name, Vector3 brazier, Vector3 spawn, float yaw)
        {
            var br = Place("Checkpoint_" + index, "Brazier", "RustMetal", brazier, Quaternion.identity, parent);
            var lit = new GameObject("Lit"); lit.transform.SetParent(br.transform, false);
            Fx.CreateFlame(lit.transform, new Vector3(0, 1.05f, 0), 2.2f, M["ParticleAdd"]);
            AddFireLoop(lit.transform, 0.4f, 12f);
            var lg = new GameObject("Light"); lg.transform.SetParent(br.transform, false); lg.transform.localPosition = new Vector3(0, 1.8f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.3f); l.intensity = 14f; l.range = 11f; l.shadows = LightShadows.None;
            lg.AddComponent<FlickerLight>();
            var cf = br.AddComponent<CheckpointFire>(); cf.index = index; cf.litVisual = lit; cf.litLight = l;
            GameObjectUtility.SetStaticEditorFlags(br, 0);
            _checkpoints.Add((name, spawn, yaw));
        }

        static GameObject Prop(Transform parent, string name, Mesh mesh, string mat, Vector3 pos, Quaternion rot, bool collider = true)
        {
            string mn = "P_" + name + "_" + _seed++;
            SaveMesh(mesh, mn);
            return Place(name, mn, mat, pos, rot, parent, collider);
        }

        static void Lantern(Transform parent, Vector3 anchor, float drop, float radius)
        {
            string mn = "Chandelier_" + _seed++;
            SaveMesh(CastleKit.Chandelier(_seed++, radius, drop), mn);
            var ch = Place("Chandelier", mn, "Iron", anchor, Quaternion.identity, parent, false);
            for (int k = 0; k < 16; k += 4)
            {
                float a = k / 16f * Mathf.PI * 2;
                Fx.CreateFlame(ch.transform, new Vector3(Mathf.Cos(a) * radius, -drop + 0.3f, Mathf.Sin(a) * radius), 0.35f, M["ParticleAdd"]);
            }
            var lg = new GameObject("Light"); lg.transform.SetParent(ch.transform, false); lg.transform.localPosition = new Vector3(0, -drop, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.62f, 0.32f); l.intensity = 30f; l.range = 16f; l.shadows = LightShadows.None;
            lg.AddComponent<FlickerLight>();
        }

        static void Bars(Transform parent, string name, Vector3 center, bool alongX, float length, float height, bool open, int cellSide = 1)
        {
            string mn = "Bars_" + name;
            var mesh = open ? CastleKit.Bars(_seed++, length, height, 0.16f, -0.2f, 0.95f) : CastleKit.Bars(_seed++, length, height, 0.16f);
            SaveMesh(mesh, mn);
            var go = Place(name, mn, "Iron", center, alongX ? Quaternion.identity : Yaw(90), parent, false, 1f, true);
            // blocking box (the open cells keep a 1.1 m gap)
            if (!open) { var b = go.AddComponent<BoxCollider>(); b.center = new Vector3(0, height * 0.5f, 0); b.size = new Vector3(length, height, 0.2f); }
            else
            {
                // the cell door, swung wide open against the bars
                string dm = "CellDoor_" + name;
                SaveMesh(CastleKit.Bars(_seed++, 1.12f, height - 0.08f, 0.17f), dm);
                var hinge = new GameObject("CellDoorHinge").transform; hinge.SetParent(go.transform, false);
                hinge.localPosition = new Vector3(0.97f, 0f, 0f);
                hinge.localRotation = Quaternion.Euler(0f, -155f * cellSide, 0f);
                var d = Place("CellDoor", dm, "Iron", Vector3.zero, Quaternion.identity, hinge, false);
                d.transform.localPosition = new Vector3(0.56f, 0.02f, 0f);
                d.transform.localRotation = Quaternion.identity;
                float l0 = -length * 0.5f, g0 = -0.2f, g1 = 0.95f, l1 = length * 0.5f;
                var b1 = go.AddComponent<BoxCollider>(); b1.center = new Vector3((l0 + g0) * 0.5f, height * 0.5f, 0); b1.size = new Vector3(g0 - l0, height, 0.2f);
                var b2 = go.AddComponent<BoxCollider>(); b2.center = new Vector3((g1 + l1) * 0.5f, height * 0.5f, 0); b2.size = new Vector3(l1 - g1, height, 0.2f);
            }
        }

        static void Doorway(Transform parent, string name, CastleProgress.Door kind, Vector3 pos, float halfW, float spring, float apex, float openAngle, bool interactive)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            root.position = pos;
            string lm = "DoorLeaf_" + name;
            SaveMesh(CastleKit.DoorLeaf(_seed++, halfW, spring, apex, true, lm), lm);
            var pl = new GameObject("LeafL").transform; pl.SetParent(root, false); pl.localPosition = new Vector3(-halfW, 0, 0);
            Place("Leaf", lm, "RottenWood", pl.position, root.rotation, pl, false);
            var pr = new GameObject("LeafR").transform; pr.SetParent(root, false); pr.localPosition = new Vector3(halfW, 0, 0);
            Place("Leaf", lm, "RottenWood", pr.position, root.rotation * Yaw(180), pr, false);
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>()) GameObjectUtility.SetStaticEditorFlags(mr.gameObject, 0);
            var cd = root.gameObject.AddComponent<CastleDoor>();
            cd.door = kind; cd.leafLeft = pl; cd.leafRight = pr; cd.openAngle = openAngle; cd.interactive = interactive;
            cd.halfWidth = halfW + 0.8f;
            var bl = root.gameObject.AddComponent<BoxCollider>(); bl.center = new Vector3(0, apex * 0.5f, 0.05f); bl.size = new Vector3(halfW * 2f, apex, 0.35f);
            cd.blocker = bl;
            if (interactive)
            {
                var gl = new GameObject("Glow"); gl.transform.SetParent(root, false); gl.transform.localPosition = new Vector3(0, 2.2f, -1.4f);
                var l = gl.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.7f, 0.75f, 0.95f); l.range = 6f; l.intensity = 0.9f; l.shadows = LightShadows.None;
                cd.glow = l;
            }
        }

        // ================================================================== throne room back wall (with the door)
        static void BuildThroneDoorWall(Transform castle)
        {
            // side panels (solid), central lower panel with the doorway, upper panel with the rose window
            SaveMesh(ProceduralMeshes.GothicWall(81, 11.5f, 17f, 2.2f, 1f, 1f, 2f, 3f, false), "BackWallSide");
            SaveMesh(CastleKit.Wall(82, 9f, 5.5f, 2.2f, new[] { new CastleKit.Opening(0f, 3.9f, 3.2f, 4.7f) }, "BackWallDoor"), "BackWallDoor");
            SaveMesh(ProceduralMeshes.GothicWall(83, 9f, 11f, 2.2f, 6f, 0.5f, 5f, 9.5f), "BackWallRose");
            SaveMesh(ProceduralMeshes.ArchPane(6f, 5f, 9.5f, 0.5f), "RosePaneSmall");
            Place("BackWall_L", "BackWallSide", "StoneCastle", new Vector3(-10.25f, -0.5f, 67.2f), Quaternion.identity, castle);
            Place("BackWall_R", "BackWallSide", "StoneCastle", new Vector3(10.25f, -0.5f, 67.2f), Quaternion.identity, castle);
            Place("BackWall_Door", "BackWallDoor", "StoneCastle", new Vector3(0f, 0f, 67.2f), Quaternion.identity, castle);
            Place("BackWall_Rose", "BackWallRose", "StoneCastle", new Vector3(0f, 5.5f, 67.2f), Quaternion.identity, castle);
            Place("RoseWindow", "RosePaneSmall", "StainedGlass", new Vector3(0f, 5.5f, 67.2f), Quaternion.identity, castle, false, 1f, false);
            Doorway(castle, "ThroneDoor", CastleProgress.Door.Throne, new Vector3(0, 0.09f, 66.45f), 1.95f, 3.2f, 4.7f, 92f, false);
        }

        // ================================================================== the castle interior
        static void BuildCastleInterior(Transform parent)
        {
            _gaolers.Clear(); _bowmen.Clear(); _wardens.Clear(); _checkpoints.Clear();
            var root = new GameObject("Castle_Interior").transform; root.SetParent(parent, false);
            // one continuous floor under every room: fills the doorway sills between double walls
            SaveMesh(ProceduralMeshes.FlagstoneFloor(_seed++, 120f, 92f, 1.25f), "CastleUnderFloor");
            Place("UnderFloor", "CastleUnderFloor", "Flagstones", new Vector3(0f, 0.07f, 66.2f), Quaternion.identity, root, true, 1f, false);

            // ---- Flooded Gallery (C1): from the throne door north to the Hall of Chains
            var c1 = new Room("FloodedGallery", -2.2f, 2.2f, 66.2f, 104f) { spring = 3.4f, apex = 5.4f, openS = true, openN = true, water = true, torchEvery = 5.5f };
            c1.W.Add(new Op(82f, 3f, 2.9f, 4.3f)); c1.E.Add(new Op(82f, 3f, 2.9f, 4.3f));
            BuildRoom(root, c1);
            _gaolers.Add((new Vector3(0.6f, 0.1f, 90f), 180f));
            _gaolers.Add((new Vector3(-0.8f, 0.1f, 99f), 180f));

            foreach (int side in new[] { -1, 1 })
            {
                string sd = side < 0 ? "W" : "E";
                float S(float x) => x * side;   // mirror helper
                // passage from the gallery to the side wing
                var p1 = new Room("Passage" + sd, side < 0 ? -35.2f : 3f, side < 0 ? -3f : 35.2f, 80.4f, 83.6f) { spring = 3f, apex = 4.6f, alongX = true, water = true, torchEvery = 6.5f };
                if (side < 0) { p1.openE = true; p1.openW = true; } else { p1.openW = true; p1.openE = true; }
                p1.S.Add(new Op(S(20f), 2.6f, 2.6f, 3.9f));
                BuildRoom(root, p1);

                // south room off the passage: Torture Chamber (W) / Drowned Chapel (E)
                var sr = new Room(side < 0 ? "TortureChamber" : "DrownedChapel", Mathf.Min(S(14f), S(26f)), Mathf.Max(S(14f), S(26f)), 69.2f, 78.8f) { spring = 4f, apex = 6.5f, torchEvery = 5f, water = side > 0 };
                sr.N.Add(new Op(S(20f), 2.6f, 2.6f, 3.9f));
                BuildRoom(root, sr);

                // side hall: Ossuary (W) / Barracks (E)
                var oss = new Room(side < 0 ? "Ossuary" : "Barracks", Mathf.Min(S(36f), S(48f)), Mathf.Max(S(36f), S(48f)), 74f, 90f) { spring = 4.5f, apex = 7f, torchEvery = 5.5f };
                (side < 0 ? oss.E : oss.W).Add(new Op(82f, 3.2f, 3f, 4.6f));
                (side < 0 ? oss.W : oss.E).Add(new Op(82f, 2.2f, 2.4f, 3.3f));   // the secret doorway (hidden by an illusory wall)
                oss.N.Add(new Op(S(42f), 3.2f, 3f, 4.6f));
                BuildRoom(root, oss);

                // passage north to the gaol wing
                var p2 = new Room("NorthPassage" + sd, Mathf.Min(S(40.4f), S(43.6f)), Mathf.Max(S(40.4f), S(43.6f)), 90.8f, 109.2f) { spring = 3f, apex = 4.6f, openS = true, openN = true, water = true, torchEvery = 6f };
                BuildRoom(root, p2);

                // Gaolers' Hall (W) / Warden's Office (E)
                var gr = new Room(side < 0 ? "GaolersHall" : "WardensOffice", Mathf.Min(S(34f), S(50f)), Mathf.Max(S(34f), S(50f)), 110f, 130f) { spring = 5f, apex = 8f, torchEvery = 6f };
                gr.S.Add(new Op(S(42f), 3.2f, 3f, 4.6f));
                (side < 0 ? gr.E : gr.W).Add(new Op(120f, 3.2f, 3f, 4.4f));
                BuildRoom(root, gr);

                // cell corridor towards the Hall of Chains, three cells on each side
                var cc = new Room("CellBlock" + sd, side < 0 ? -33.2f : 13.8f, side < 0 ? -13.8f : 33.2f, 118.4f, 121.6f) { spring = 3f, apex = 4.4f, alongX = true, openW = true, openE = true, torchEvery = 6.4f, wallMat = "PrisonStone" };
                float module = 19.4f / 3f;
                for (int i = 0; i < 3; i++)
                {
                    float cx0 = cc.x0 + i * module + 0.8f, cx1 = cc.x0 + (i + 1) * module - 0.8f, cxc = (cx0 + cx1) * 0.5f;
                    cc.N.Add(new Op(cxc, 3.4f, 2.7f, 3.3f));
                    cc.S.Add(new Op(cxc, 3.4f, 2.7f, 3.3f));
                    foreach (int ns in new[] { 1, -1 })
                    {
                        bool open = (i + (ns > 0 ? 0 : 1) + (side > 0 ? 1 : 0)) % 2 == 0;
                        var cell = new Room($"Cell{sd}{i}{(ns > 0 ? "N" : "S")}", cx0, cx1, ns > 0 ? 123.2f : 111.8f, ns > 0 ? 128.2f : 116.8f) { spring = 2.6f, apex = 3.6f, torchEvery = 0f, wallMat = "PrisonStone" };
                        (ns > 0 ? cell.S : cell.N).Add(new Op(cxc, 3.4f, 2.7f, 3.3f));
                        BuildRoom(root, cell);
                        Bars(root, $"{sd}{i}{ns}", new Vector3(cxc, 0.05f, ns > 0 ? 122.0f : 118.0f), true, 3.35f, 2.65f, open, ns);
                        // a cot and a dim candle in each cell
                        float cz = ns > 0 ? 126.8f : 113.2f;
                        Prop(root, "Cot", CastleKit.Crate(_seed++, new Vector3(0.9f, 0.42f, 2f)), "RottenWood", new Vector3(cx0 + 0.7f, 0.05f, (ns > 0 ? 125.5f : 114.5f)), Quaternion.identity);
                        if (open && i != 1) _gaolers.Add((new Vector3(cxc + 0.8f, 0.1f, cz), ns > 0 ? 180f : 0f));
                        var cand = new GameObject("CellCandle").transform; cand.SetParent(root, false); cand.position = new Vector3(cx1 - 0.5f, 0.5f, cz);
                        Fx.CreateFlame(cand, Vector3.zero, 0.25f, M["ParticleAdd"]);
                        var cl = cand.gameObject.AddComponent<Light>(); cl.type = LightType.Point; cl.color = TorchColor; cl.intensity = 2.2f; cl.range = 4.5f; cl.shadows = LightShadows.None;
                        cand.gameObject.AddComponent<FlickerLight>();
                    }
                }
                BuildRoom(root, cc);
                // the cell walls leave no room for torches: iron lanterns hang along the corridor
                foreach (float f in new[] { 0.25f, 0.75f })
                    Lantern(root, new Vector3(Mathf.Lerp(cc.x0, cc.x1, f), 4.3f, 120f), 1.3f, 0.7f);
            }

            // ---- room dressing and inhabitants
            // Torture Chamber: racks and tables
            Prop(root, "Rack", CastleKit.Crate(_seed++, new Vector3(1.1f, 0.9f, 2.6f)), "RottenWood", new Vector3(-20f, 0.05f, 73f), Yaw(8));
            Prop(root, "Table", CastleKit.Crate(_seed++, new Vector3(2f, 0.85f, 0.9f)), "RottenWood", new Vector3(-24.6f, 0.05f, 71f), Yaw(90));
            Lantern(root, new Vector3(-20f, 6.2f, 74f), 2.2f, 1f);
            _gaolers.Add((new Vector3(-17f, 0.1f, 75f), 30f));
            // Drowned Chapel: altar on a dais, kneeling statues of rubble
            Prop(root, "Altar", CastleKit.Crate(_seed++, new Vector3(2.4f, 1.1f, 1.1f)), "StoneCastle", new Vector3(20f, 0.05f, 70.6f), Quaternion.identity);
            for (int i = 0; i < 4; i++) Place("Rubble", "Rock" + (i % 6), "StoneCastle", new Vector3(16f + i * 2.4f, 0f, 74f + (i % 2)), Yaw(i * 70), root, true, 0.55f);
            Brazier(root, new Vector3(17.2f, 0.05f, 70.4f), 16f, 11f);
            Brazier(root, new Vector3(22.8f, 0.05f, 70.4f), 16f, 11f);
            _gaolers.Add((new Vector3(21f, 0.1f, 75.5f), 0f));
            // Ossuary: sarcophagi and bone heaps
            for (int i = 0; i < 3; i++)
            {
                Prop(root, "Sarcophagus", CastleKit.Crate(_seed++, new Vector3(1.1f, 0.85f, 2.4f)), "StoneCastle", new Vector3(-46f, 0.05f, 77f + i * 4.5f), Quaternion.identity);
                Place("Bones", "AshMound", "Ash", new Vector3(-38f, 0f, 77f + i * 5f), Yaw(i * 50), root, false, 1.2f);
            }
            _gaolers.Add((new Vector3(-42f, 0.1f, 79f), 90f));
            _gaolers.Add((new Vector3(-39f, 0.1f, 87f), 90f));
            _bowmen.Add((new Vector3(-46f, 0.1f, 88.5f), 135f));
            // Barracks: bunks and a weapon rack
            for (int i = 0; i < 3; i++)
                Prop(root, "Bunk", CastleKit.Crate(_seed++, new Vector3(1f, 0.7f, 2.2f)), "RottenWood", new Vector3(46.6f, 0.05f, 76.5f + i * 4.6f), Quaternion.identity);
            Prop(root, "Table", CastleKit.Crate(_seed++, new Vector3(2.2f, 0.85f, 1f)), "RottenWood", new Vector3(40f, 0.05f, 86f), Yaw(90));
            _gaolers.Add((new Vector3(42f, 0.1f, 79f), -90f));
            _gaolers.Add((new Vector3(39f, 0.1f, 87f), -90f));
            _bowmen.Add((new Vector3(46f, 0.1f, 88.5f), -135f));
            // passages
            _bowmen.Add((new Vector3(-31f, 0.1f, 82f), 90f));
            _bowmen.Add((new Vector3(31f, 0.1f, 82f), -90f));
            _gaolers.Add((new Vector3(-42f, 0.1f, 101f), 180f));
            _gaolers.Add((new Vector3(42f, 0.1f, 101f), 180f));
            // Gaolers' Hall: long table, a chandelier
            Prop(root, "LongTable", CastleKit.Crate(_seed++, new Vector3(1.4f, 0.85f, 6f)), "RottenWood", new Vector3(-42f, 0.05f, 121f), Quaternion.identity);
            Lantern(root, new Vector3(-42f, 7.6f, 121f), 3f, 1.6f);
            _gaolers.Add((new Vector3(-45f, 0.1f, 117f), 0f));
            _gaolers.Add((new Vector3(-38f, 0.1f, 125f), -90f));
            _bowmen.Add((new Vector3(-48.5f, 0.1f, 128.5f), 135f));
            // Warden's Office: the desk with the key, shelves, the Warden
            Prop(root, "Desk", CastleKit.Crate(_seed++, new Vector3(2.4f, 0.9f, 1.2f)), "RottenWood", new Vector3(45f, 0.05f, 127f), Quaternion.identity);
            for (int i = 0; i < 3; i++) Prop(root, "Shelf", CastleKit.Crate(_seed++, new Vector3(0.6f, 2.4f, 2f)), "RottenWood", new Vector3(49.5f, 0.05f, 113f + i * 3f), Quaternion.identity);
            Lantern(root, new Vector3(42f, 7.6f, 120f), 3f, 1.6f);
            {
                var keyRoot = new GameObject("WardensKey"); keyRoot.transform.SetParent(root, false);
                keyRoot.transform.position = new Vector3(45f, 1.02f, 127f);
                var vis = new GameObject("KeyVisual"); vis.transform.SetParent(keyRoot.transform, false);
                SaveMesh(CastleKit.Key(), "Key");
                vis.AddComponent<MeshFilter>().sharedMesh = Meshes["Key"];
                vis.AddComponent<MeshRenderer>().sharedMaterial = M["KeyGold"];
                vis.transform.localScale = Vector3.one * 1.3f;
                var gl = new GameObject("KeyGlow"); gl.transform.SetParent(keyRoot.transform, false); gl.transform.localPosition = new Vector3(0, 0.5f, 0);
                var l = gl.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.78f, 0.4f); l.range = 4f; l.intensity = 1.5f; l.shadows = LightShadows.None;
                var kp = keyRoot.AddComponent<KeyPickup>(); kp.visual = vis.transform; kp.glow = l;
            }
            _wardens.Add((new Vector3(42f, 0.1f, 119f), 180f));
            _gaolers.Add((new Vector3(38f, 0.1f, 126f), -90f));
            _bowmen.Add((new Vector3(48.5f, 0.1f, 111.5f), -45f));

            // ---- Hall of Chains: the prison's great hall
            var hall = new Room("HallOfChains", -13f, 13f, 104.8f, 136f) { spring = 9f, apex = 13f, rib = 6f, torchEvery = 0f, wallMat = "PrisonStone" };
            hall.S.Add(new Op(0f, 4.4f, 3.4f, 5.4f));
            hall.W.Add(new Op(120f, 3.2f, 3f, 4.4f));
            hall.E.Add(new Op(120f, 3.2f, 3f, 4.4f));
            hall.N.Add(new Op(0f, 5f, 4.4f, 6.6f));
            BuildRoom(root, hall);
            for (int i = 0; i < 4; i++)
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 p = new Vector3(side * 7f, 0f, 110.5f + i * 6.5f);
                    Place("HallPillar", "PillarHall", "PrisonStone", p, Yaw(22.5f), root);
                    if (i % 2 == 1) continue;
                    Torch(root, p + new Vector3(-side * 0.85f, 3f, 0f), new Vector3(-side, 0, 0), 20f, 13f);
                    Torch(root, p + new Vector3(side * 0.85f, 3f, 0f), new Vector3(side, 0, 0), 14f, 10f);
                }
            foreach (float z in new[] { 108f, 132f })
                foreach (int side in new[] { -1, 1 })
                    Torch(root, new Vector3(side * 13f, 2.8f, z), new Vector3(-side, 0, 0), 16f, 11f);
            Lantern(root, new Vector3(0f, 12.5f, 114f), 4.5f, 2.2f);
            Lantern(root, new Vector3(0f, 12.5f, 127f), 4.5f, 2.2f);
            // the Great Door, sealed until the Warden's key is brought
            Doorway(root, "GreatDoor", CastleProgress.Door.Great, new Vector3(0f, 0.09f, 136.25f), 2.5f, 4.4f, 6.6f, 92f, true);
            Torch(root, new Vector3(-3.6f, 3.2f, 136f), Vector3.back, 18f, 12f);
            Torch(root, new Vector3(3.6f, 3.2f, 136f), Vector3.back, 18f, 12f);
            Checkpoint(root, 2, "Hall of Chains", new Vector3(-4.5f, 0.05f, 107.2f), new Vector3(0f, 0.2f, 108.5f), 0f);
            _gaolers.Add((new Vector3(-4f, 0.1f, 125f), 180f));
            _gaolers.Add((new Vector3(4.5f, 0.1f, 129f), 180f));
            _bowmen.Add((new Vector3(-10f, 0.1f, 133f), 160f));
            _bowmen.Add((new Vector3(10f, 0.1f, 133f), -160f));
            _bowmen.Add((new Vector3(28f, 0.1f, 120f), -90f));

            DressPrisons(root);
            BuildPrisonSecrets(root);

            // ---- Grand Stair up to the terrace
            BuildGrandStair(root);
            Checkpoint(root, 3, "The Grand Stair", new Vector3(2.2f, TerraceY + 0.05f, 154.6f), new Vector3(-0.8f, TerraceY + 0.2f, 155.4f), 0f);

            // checkpoint 1: the throne room, beside the door (unlocked when the King falls)
            Checkpoint(parent, 1, "The Throne", new Vector3(8.5f, 0.05f, 64.5f), new Vector3(6.5f, 0.2f, 63.5f), 180f);
        }

        /// <summary>Cages and chains hanging from the vaults, banners, puddles, candles, echoing halls.</summary>
        static void DressPrisons(Transform root)
        {
            var dress = new GameObject("PrisonDressing").transform; dress.SetParent(root, false);
            void Hang(Mesh m, string name, Vector3 top, float yaw, string mat = "Iron")
            {
                string mn = name + "_" + _seed++;
                SaveMesh(m, mn);
                var go = Place(name, mn, mat, top, Yaw(yaw), dress, false);
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                go.AddComponent<Sway>().amplitude = 2.2f;
            }
            // Hall of Chains: three gibbet cages and chains between the pillars
            foreach (var (x, z, len) in new[] { (-3.5f, 112f, 5.0f), (3.2f, 121f, 4.2f), (-2.6f, 129f, 5.6f) })
            {
                Hang(CastleKit.Chain(_seed++, len), "CageChain", new Vector3(x, 12.4f, z), 0f);
                Hang(CastleKit.Cage(_seed++, 0.55f, 1.9f), "Cage", new Vector3(x, 12.4f - len, z), x * 17f);
                Place("CageBones", "AshMound", "Ash", new Vector3(x, 12.4f - len - 2.2f, z), Yaw(x * 40f), dress, false, 0.35f);
            }
            for (int i = 0; i < 10; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (9.5f + (i % 3) * 0.6f), z = 108f + i * 2.7f;
                Hang(CastleKit.Chain(_seed++, 2.5f + (i % 4) * 1.3f), "Chain", new Vector3(x, 10.5f, z), i * 30f);
            }
            for (int i = 0; i < 3; i++)
                foreach (int side in new[] { -1, 1 })
                    Place("HallBanner", "Banner", "Banner", new Vector3(side * 12.9f, 8.6f, 112f + i * 9f), Yaw(side > 0 ? -90 : 90), dress, false, 0.85f, false);
            // puddles in the hall and the wings
            Water(dress, "HallPuddleA", -12.5f, -8.5f, 105.3f, 109.5f, 0f, 13f);
            Water(dress, "HallPuddleB", 7.8f, 12.5f, 130f, 135.5f, 0f, 13f);
            Water(dress, "GaolPuddle", -49.5f, -44f, 110.5f, 114f, 0f, 8f);
            Water(dress, "OfficePuddle", 44f, 49.5f, 110.5f, 113f, 0f, 8f);
            // candle clusters on the floor (warm pools of light in the dark)
            foreach (var c in new[] { new Vector3(-11.8f, 0f, 118f), new Vector3(11.8f, 0f, 124f), new Vector3(-40f, 0f, 89f), new Vector3(46.5f, 0f, 129f), new Vector3(-20f, 0f, 70f), new Vector3(24f, 0f, 70.2f) })
            {
                for (int k = 0; k < 4; k++)
                {
                    Vector3 o = c + new Vector3((k % 2) * 0.22f - 0.1f, 0f, (k / 2) * 0.2f - 0.1f);
                    float h = 0.12f + k * 0.05f;
                    var mb = new MeshBuilder();
                    mb.AddPrism(Vector3.zero, Vector3.up * h, 0.035f, 0.032f, 6, true, true, k, 0f);
                    string mn = "Candle_" + _seed++;
                    SaveMesh(mb.Build(mn), mn);
                    var cg = Place("Candle", mn, "CandleWax", o, Quaternion.identity, dress, false);
                    Fx.CreateFlame(cg.transform, Vector3.up * (h + 0.04f), 0.18f, M["ParticleAdd"]);
                }
                var lg = new GameObject("CandleLight"); lg.transform.SetParent(dress, false); lg.transform.position = c + Vector3.up * 0.5f;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.62f, 0.3f); l.intensity = 3.5f; l.range = 5f; l.shadows = LightShadows.None;
                lg.AddComponent<FlickerLight>();
            }
            // lanterns hanging along the flooded gallery
            foreach (float z in new[] { 74f, 88f, 100f })
                Lantern(dress, new Vector3(0f, 5.2f, z), 1.0f, 0.5f);
            // barrels and crates in the passages
            foreach (var (x, z) in new[] { (-6f, 81.2f), (-14f, 83f), (8f, 81.4f), (26f, 83f), (-42.5f, 96f), (41.5f, 104f) })
                Prop(dress, "Crate", CastleKit.Crate(_seed++, new Vector3(0.9f, 0.85f, 0.9f)), "RottenWood", new Vector3(x, 0.05f, z), Yaw(x * 13f));

            // ---- reverb: stone halls and corridors echo
            void Reverb(Vector3 c, float min, float max, AudioReverbPreset preset)
            {
                var g = new GameObject("Reverb_" + preset); g.transform.SetParent(dress, false); g.transform.position = c;
                var z = g.AddComponent<AudioReverbZone>(); z.minDistance = min; z.maxDistance = max; z.reverbPreset = preset;
            }
            Reverb(new Vector3(0f, 6f, 46f), 16f, 26f, AudioReverbPreset.Concerthall);
            Reverb(new Vector3(0f, 2f, 78f), 8f, 14f, AudioReverbPreset.StoneCorridor);
            Reverb(new Vector3(0f, 2f, 96f), 6f, 12f, AudioReverbPreset.StoneCorridor);
            Reverb(new Vector3(0f, 5f, 120f), 14f, 22f, AudioReverbPreset.Cave);
            foreach (int side in new[] { -1, 1 })
            {
                Reverb(new Vector3(side * 20f, 2f, 82f), 10f, 18f, AudioReverbPreset.StoneCorridor);
                Reverb(new Vector3(side * 42f, 3f, 82f), 8f, 14f, AudioReverbPreset.Stoneroom);
                Reverb(new Vector3(side * 42f, 2f, 100f), 6f, 12f, AudioReverbPreset.StoneCorridor);
                Reverb(new Vector3(side * 42f, 3f, 120f), 10f, 16f, AudioReverbPreset.Stoneroom);
                Reverb(new Vector3(side * 23f, 2f, 120f), 8f, 14f, AudioReverbPreset.Hallway);
                Reverb(new Vector3(side * 20f, 3f, 74f), 6f, 11f, AudioReverbPreset.Stoneroom);
            }
            Reverb(new Vector3(0f, 5f, 146f), 8f, 14f, AudioReverbPreset.StoneCorridor);
        }

        static void BuildGrandStair(Transform parent)
        {
            var root = new GameObject("GrandStair").transform; root.SetParent(parent, false);
            var st = new Room("GrandStair", -3f, 3f, 136.8f, 157.2f) { spring = 11.2f, apex = 13.6f, rib = 3f, openS = true, openN = true, floor = false, torchEvery = 0f };
            BuildRoom(root, st);
            // steps: y 0 → 7.5 between z 139 and 154 (30 × 0.25 m), landing up to the terrace
            var mb = new MeshBuilder { uvScale = 0.5f };
            ProceduralMeshes.Box(mb, new Vector3(0f, -0.15f, 137.45f), new Vector3(6f, 0.3f, 3.1f), 1, 0.01f);
            int n = 30;
            for (int i = 0; i < n; i++)
            {
                float h = (i + 1) * (TerraceY / n);
                float z0 = 139f + i * 0.5f;
                mb.color = Color.Lerp(new Color(0.8f, 0.8f, 0.8f), Color.white, (i % 3) / 2f);
                ProceduralMeshes.Box(mb, new Vector3(0f, h * 0.5f, z0 + 0.25f), new Vector3(6f, h, 0.5f), 10 + i, 0.015f);
            }
            mb.color = Color.white;
            ProceduralMeshes.Box(mb, new Vector3(0f, TerraceY * 0.5f, 156.15f), new Vector3(6f, TerraceY, 4.3f), 77, 0.01f);
            SaveMesh(mb.Build("GrandStairSteps"), "GrandStairSteps");
            Place("Steps", "GrandStairSteps", "StoneCastle", Vector3.zero, Quaternion.identity, root, true, 1f, true);
            // a smooth ramp for the character controllers (invisible), following the nosing
            var ramp = new GameObject("StairRamp"); ramp.transform.SetParent(root, false);
            float run = 15f, rise = TerraceY;
            ramp.transform.position = new Vector3(0f, rise * 0.5f - 0.12f, 139f + run * 0.5f);
            ramp.transform.rotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f);
            var rb = ramp.AddComponent<BoxCollider>(); rb.size = new Vector3(5.8f, 0.2f, Mathf.Sqrt(run * run + rise * rise));
            // torches climbing with the steps
            for (int i = 0; i < 4; i++)
            {
                float z = 140.5f + i * 4f;
                float h = Mathf.Clamp((z - 139f) / 15f, 0f, 1f) * TerraceY;
                bool west = i % 2 == 0;
                Torch(root, new Vector3(west ? -3f : 3f, h + 2.7f, z), west ? Vector3.right : Vector3.left, 18f, 12f);
            }
            Torch(root, new Vector3(-3f, TerraceY + 2.7f, 155.5f), Vector3.right, 16f, 11f);
            Torch(root, new Vector3(3f, 2.7f, 138f), Vector3.left, 16f, 11f);
        }

        // ================================================================== the terrace
        static void BuildTerrace(Transform parent, System.Func<float, float, float> R)
        {
            var root = new GameObject("Terrace").transform; root.SetParent(parent, false);
            float x0 = -24f, x1 = 24f, z0 = 158f, z1 = 206f, y = TerraceY;
            SaveMesh(ProceduralMeshes.FlagstoneFloor(_seed++, x1 - x0, z1 - z0, 1.6f), "TerraceFloor");
            Place("TerraceFloor", "TerraceFloor", "Flagstones", new Vector3(0f, y + 0.09f, z0 - 0.2f), Quaternion.identity, root, true, 1f, false);
            var slab = new MeshBuilder { uvScale = 0.5f };
            ProceduralMeshes.Box(slab, new Vector3(0f, y - 10f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 1.4f, 20f, z1 - z0 + 1f), 5, 0.05f);
            SaveMesh(slab.Build("TerraceSlab"), "TerraceSlab");
            Place("TerraceSlab", "TerraceSlab", "StoneCastle", Vector3.zero, Quaternion.identity, root, false);

            // the keep's back wall, with the arch of the stair
            WallRun(root, "W_TerraceKeep", new Vector3(x0 - 0.8f, y, z0 - 0.4f), new Vector3(x1 + 0.8f, y, z0 - 0.4f), 14f, 0.8f,
                    new List<Op> { new Op(24.8f, 4.4f, 3.6f, 5.4f) }, "StoneCastle");
            Battlements(root, "TerraceKeepCrenels", new Vector3(x0 - 0.8f, y + 14f, z0 - 0.4f), new Vector3(x1 + 0.8f, y + 14f, z0 - 0.4f), 1.2f);
            // balustrades + invisible guard walls
            SaveMesh(CastleKit.Balustrade(_seed++, z1 - z0), "BalustradeSide");
            SaveMesh(CastleKit.Balustrade(_seed++, x1 - x0 + 1f), "BalustradeEnd");
            Place("Balustrade_W", "BalustradeSide", "StoneCastle", new Vector3(x0 - 0.25f, y, (z0 + z1) * 0.5f), Yaw(90), root);
            Place("Balustrade_E", "BalustradeSide", "StoneCastle", new Vector3(x1 + 0.25f, y, (z0 + z1) * 0.5f), Yaw(90), root);
            // the north balustrade leaves a gap for the bridge gate (opens when the wyrm dies)
            SaveMesh(CastleKit.Balustrade(_seed++, x1 + 0.5f - 3.5f), "BalustradeEndHalf");
            Place("Balustrade_NW", "BalustradeEndHalf", "StoneCastle", new Vector3((x0 - 0.5f - 3.5f) * 0.5f, y, z1 + 0.25f), Quaternion.identity, root);
            Place("Balustrade_NE", "BalustradeEndHalf", "StoneCastle", new Vector3((x1 + 0.5f + 3.5f) * 0.5f, y, z1 + 0.25f), Quaternion.identity, root);
            AddBox(root, new Vector3(x0 - 0.6f, y + 4f, (z0 + z1) * 0.5f), new Vector3(0.6f, 8f, z1 - z0 + 2f));
            AddBox(root, new Vector3(x1 + 0.6f, y + 4f, (z0 + z1) * 0.5f), new Vector3(0.6f, 8f, z1 - z0 + 2f));
            AddBox(root, new Vector3((x0 - 1f - 3.5f) * 0.5f, y + 4f, z1 + 0.6f), new Vector3(x1 + 1f - 3.5f, 8f, 0.6f));
            AddBox(root, new Vector3((x1 + 1f + 3.5f) * 0.5f, y + 4f, z1 + 0.6f), new Vector3(x1 + 1f - 3.5f, 8f, 0.6f));
            // courtyard far below
            var gmb = new MeshBuilder { uvScale = 0.05f };
            gmb.AddQuad(new Vector3(-220f, 0f, 60f), new Vector3(220f, 0f, 60f), new Vector3(220f, 0f, 328f), new Vector3(-220f, 0f, 328f), Vector3.up);
            SaveMesh(gmb.Build("Courtyard"), "Courtyard");
            Place("Courtyard", "Courtyard", "Ground", new Vector3(0f, -12f, 0f), Quaternion.identity, root, false, 1f, false);

            // ---- the castle all around: corner towers, the great keep ahead, a palace hall, a belfry, curtain walls and bridges
            var city = new GameObject("CastleSkyline").transform; city.SetParent(root, false);
            const float G0 = -12f;
            foreach (var p in new[] { new Vector2(x0 - 2f, z0 - 1f), new Vector2(x1 + 2f, z0 - 1f), new Vector2(x0 - 2f, z1 + 2f), new Vector2(x1 + 2f, z1 + 2f) })
                RoundTower(city, new Vector3(p.x, G0, p.y), 3.4f, 34f, 9f, 0.35f);
            // north: two great towers frame the sunset over a low gatehouse; the great keep rises to the north-east
            RoundTower(city, new Vector3(-25f, G0, 238f), 5f, 58f, 16f, 0.5f);
            RoundTower(city, new Vector3(25f, G0, 238f), 5f, 58f, 16f, 0.5f);
            // a slender gate of two towers at the end of the bridge: the sun sets right between them
            RoundTower(city, new Vector3(-6.5f, G0, 242f), 3f, 26f, 8f, 0.7f);
            RoundTower(city, new Vector3(6.5f, G0, 242f), 3f, 26f, 8f, 0.7f);
            Building(city, "GreatKeep", new Vector3(78f, G0, 268f), 34f, 30f, 7, 7.5f, 0.45f, 14f);
            RoundTower(city, new Vector3(60f, G0, 252f), 5f, 66f, 18f, 0.5f);
            RoundTower(city, new Vector3(96f, G0, 252f), 5f, 70f, 18f, 0.5f);
            RoundTower(city, new Vector3(60f, G0, 284f), 5f, 64f, 18f, 0.5f);
            RoundTower(city, new Vector3(78f, G0 + 52.5f, 268f), 4f, 22f, 20f, 0.6f);    // central spire on the keep roof
            // palace hall to the west, belfry to the east
            Building(city, "PalaceHall", new Vector3(-60f, G0, 200f), 22f, 64f, 3, 9f, 0.55f, 11f);
            RoundTower(city, new Vector3(-60f, G0, 162f), 5.5f, 44f, 14f, 0.4f);
            RoundTower(city, new Vector3(-60f, G0, 238f), 5.5f, 48f, 14f, 0.4f);
            RoundTower(city, new Vector3(56f, G0, 212f), 6.5f, 62f, 22f, 0.55f);       // belfry
            Building(city, "EastWing", new Vector3(58f, G0, 172f), 18f, 30f, 4, 7f, 0.4f, 9f);
            RoundTower(city, new Vector3(-48f, G0, 280f), 6f, 56f, 16f, 0.4f);
            RoundTower(city, new Vector3(-86f, G0, 226f), 5f, 46f, 15f, 0.4f);
            // curtain walls with battlements
            Curtain(city, new Vector3(x1 + 2f, G0, z1 + 2f), new Vector3(25f, G0, 238f), 24f);
            Curtain(city, new Vector3(x0 - 2f, G0, z1 + 2f), new Vector3(-25f, G0, 238f), 24f);
            Curtain(city, new Vector3(x0 - 2f, G0, z0 - 1f), new Vector3(-60f, G0, 162f), 22f);
            Curtain(city, new Vector3(-60f, G0, 238f), new Vector3(-25f, G0, 238f), 26f);
            Curtain(city, new Vector3(x1 + 2f, G0, z0 - 1f), new Vector3(49f, G0, 172f), 22f);
            Curtain(city, new Vector3(56f, G0, 212f), new Vector3(60f, G0, 252f), 24f);
            Curtain(city, new Vector3(25f, G0, 238f), new Vector3(60f, G0, 252f), 22f);
            // bridges: the way on toward the keep, and high walkways between the towers
            Viaduct(city, "KeepBridge", new Vector3(0f, G0, z1 + 0.6f), new Vector3(0f, G0, 239f), y - G0, 7f, true);
            Viaduct(city, "SkyBridge", new Vector3(31f, G0, 241f), new Vector3(55f, G0, 250f), 40f, 3.5f, false);
            Viaduct(city, "WestBridge", new Vector3(-49f, G0, 215f), new Vector3(-30f, G0, 238f), 34f, 4f, false);
            Viaduct(city, "EastBridge", new Vector3(x1 + 5f, G0, 192f), new Vector3(50f, G0, 208f), 28f, 4f, false);

            // ---- terrace dressing: braziers, banners, rubble, embers in the warm air
            foreach (var p in new[] { new Vector2(-18f, 164f), new Vector2(18f, 164f), new Vector2(-18f, 200f), new Vector2(18f, 200f) })
                Brazier(root, new Vector3(p.x, y + 0.05f, p.y), 24f, 18f, false);
            for (int i = 0; i < 4; i++)
                foreach (int side in new[] { -1, 1 })
                {
                    var bn = Place("TerraceBanner", "Banner", "Banner", new Vector3(side * 24.6f, y + 1.2f, 166f + i * 11f), Yaw(side > 0 ? -90 : 90), root, false, 0.9f, false);
                    var sw = bn.AddComponent<Sway>(); sw.amplitude = 5f; sw.period = 2.6f;
                }
            for (int i = 0; i < 9; i++)
                Place("Rubble", "Rock" + (i % 6), "StoneCastle", new Vector3(R(-21f, 21f), y, R(162f, 203f)), Yaw(R(0, 360)), root, true, R(0.35f, 0.9f));
            var embers = new GameObject("SunsetEmbers"); embers.transform.SetParent(root, false); embers.transform.position = new Vector3(0f, y + 6f, 182f);
            {
                var ps = embers.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.loop = true; main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.25f, 0.9f), new Color(1f, 0.35f, 0.12f, 0.8f));
                main.maxParticles = 400; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = -0.02f;
                var em = ps.emission; em.rateOverTime = 45f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60f, 14f, 60f);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.4f); vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.4f); vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.4f; noise.frequency = 0.3f;
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1) });
                col.color = g;
                embers.GetComponent<ParticleSystemRenderer>().material = M["ParticleAdd"];
                ps.Play();
            }
            var wind = new GameObject("WindAmbience").transform; wind.SetParent(root, false); wind.position = new Vector3(0f, y + 2f, 182f);
            AddLoop(wind, "Ambient Wind Loop 1.wav", 0.35f, 45f, 0.6f);
        }

        // ---------------------------------------------------------------- skyline pieces
        static readonly Dictionary<string, string> _facadeCache = new Dictionary<string, string>();

        static string Facade(float w, float h, int variant, bool window)
        {
            string key = $"Facade_{w:0.0}_{h:0.0}_{variant}_{window}";
            if (_facadeCache.TryGetValue(key, out var mn) && Meshes.ContainsKey(mn)) return mn;
            SaveMesh(ProceduralMeshes.GothicWall(500 + variant, w, h, 1.2f, Mathf.Min(2.2f, w * 0.35f), h * 0.3f, h * 0.68f, h * 0.86f, window), key);
            _facadeCache[key] = key;
            return key;
        }

        static string Pane(float w, float h)
        {
            string key = $"Pane_{w:0.0}_{h:0.0}";
            if (!Meshes.ContainsKey(key)) SaveMesh(ProceduralMeshes.ArchPane(Mathf.Min(2.2f, w * 0.35f), h * 0.68f, h * 0.86f, h * 0.3f), key);
            return key;
        }

        /// <summary>A gothic building: storeys of windowed walls (some lit), battlements, a hip roof.</summary>
        static void Building(Transform parent, string name, Vector3 c, float w, float d, int floors, float floorH, float litChance, float roofH)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            var rng = new System.Random(name.GetHashCode());
            void Side(Vector3 a, Vector3 b)
            {
                Vector3 dir = b - a; float len = dir.magnitude; dir /= len;
                var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 7f));
                float tw = len / n;
                for (int f = 0; f < floors; f++)
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = a + dir * (tw * (i + 0.5f)) + Vector3.up * (f * floorH);
                        bool window = f > 0 || (i % 2 == 1);
                        var go = Place("Facade", Facade(tw, floorH, (i + f) % 3, window), "StoneCastle", p, rot, root, false);
                        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                        if (window)
                        {
                            bool lit = rng.NextDouble() < litChance;
                            Place("Window", Pane(tw, floorH), lit ? "WindowGlow" : "WindowDark", p, rot, root, false, 1f, false);
                        }
                    }
            }
            float hx = w * 0.5f, hz = d * 0.5f;
            Side(c + new Vector3(-hx, 0, -hz), c + new Vector3(hx, 0, -hz));
            Side(c + new Vector3(hx, 0, hz), c + new Vector3(-hx, 0, hz));
            Side(c + new Vector3(-hx, 0, hz), c + new Vector3(-hx, 0, -hz));
            Side(c + new Vector3(hx, 0, -hz), c + new Vector3(hx, 0, hz));
            float top = c.y + floors * floorH;
            Battlements(root, name + "_CrenS", new Vector3(c.x - hx, top, c.z - hz), new Vector3(c.x + hx, top, c.z - hz), 1f);
            Battlements(root, name + "_CrenN", new Vector3(c.x - hx, top, c.z + hz), new Vector3(c.x + hx, top, c.z + hz), 1f);
            Battlements(root, name + "_CrenW", new Vector3(c.x - hx, top, c.z - hz), new Vector3(c.x - hx, top, c.z + hz), 1f);
            Battlements(root, name + "_CrenE", new Vector3(c.x + hx, top, c.z - hz), new Vector3(c.x + hx, top, c.z + hz), 1f);
            // hip roof
            var mb = new MeshBuilder { uvScale = 0.25f };
            float ix = hx - 0.6f, iz = hz - 0.6f, rh = roofH;
            Vector3 A = new Vector3(-ix, 0, -iz), B = new Vector3(ix, 0, -iz), C = new Vector3(ix, 0, iz), D = new Vector3(-ix, 0, iz);
            float ridge = Mathf.Max(0f, (w > d ? ix - iz : iz - ix));
            Vector3 R1 = w > d ? new Vector3(-ridge, rh, 0) : new Vector3(0, rh, -ridge), R2 = w > d ? new Vector3(ridge, rh, 0) : new Vector3(0, rh, ridge);
            if (w > d)
            {
                mb.AddQuad(A, B, R2, R1, Vector3.back + Vector3.up); mb.AddQuad(C, D, R1, R2, Vector3.forward + Vector3.up);
                mb.AddTri(D, A, R1, Vector3.left + Vector3.up); mb.AddTri(B, C, R2, Vector3.right + Vector3.up);
            }
            else
            {
                mb.AddQuad(D, A, R1, R2, Vector3.left + Vector3.up); mb.AddQuad(B, C, R2, R1, Vector3.right + Vector3.up);
                mb.AddTri(A, B, R1, Vector3.back + Vector3.up); mb.AddTri(C, D, R2, Vector3.forward + Vector3.up);
            }
            string rn = name + "_Roof";
            SaveMesh(mb.Build(rn), rn);
            Place("Roof", rn, "RoofSlate", new Vector3(c.x, top, c.z), Quaternion.identity, root, false);
        }

        /// <summary>A round tower: stone shaft with lit arrow-slits, a ring of merlons and a slate spire with a pennant.</summary>
        static void RoundTower(Transform parent, Vector3 basePos, float radius, float height, float spire, float litChance)
        {
            var root = new GameObject("Tower").transform; root.SetParent(parent, false);
            var mb = new MeshBuilder { uvScale = 0.35f };
            mb.AddPrism(Vector3.zero, Vector3.up * height, radius, radius * 0.95f, 14, false, true, _seed++, 0.05f);
            // corbel ring and merlons
            mb.AddPrism(Vector3.up * (height - 0.6f), Vector3.up * height, radius * 1.12f, radius * 1.12f, 14, true, true, _seed++, 0f);
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI * 2f / 12f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * 1.06f + Vector3.up * (height + 0.6f);
                ProceduralMeshes.Box(mb, p, new Vector3(0.9f, 1.2f, 0.9f), _seed + k, 0.02f);
            }
            string mn = "TowerShaft_" + _seed++;
            SaveMesh(mb.Build(mn), mn);
            Place("Shaft", mn, "StoneCastle", basePos, Yaw(basePos.x * 7f), root, false);
            var roof = new MeshBuilder { uvScale = 0.3f };
            roof.AddPrism(Vector3.zero, Vector3.up * spire, radius * 1.05f, 0.05f, 12, true, false, _seed++, 0f);
            string rn = "Spire_" + _seed++;
            SaveMesh(roof.Build(rn), rn);
            Place("Spire", rn, "RoofSlate", basePos + Vector3.up * (height + 0.2f), Quaternion.identity, root, false);
            // pennant on the tip
            var flag = Place("Pennant", "Banner", "Banner", basePos + Vector3.up * (height + spire + 0.3f), Yaw(basePos.z * 3f), root, false, 0.35f, false);
            var sw = flag.AddComponent<Sway>(); sw.amplitude = 9f; sw.period = 1.8f;
            // lit arrow slits
            var rng = new System.Random((int)(basePos.x * 31 + basePos.z * 17));
            var slits = new MeshBuilder();
            var dark = new MeshBuilder();
            for (float h = 6f; h < height - 3f; h += 5.5f)
                for (int k = 0; k < 4; k++)
                {
                    float a = (k + (h * 0.13f)) * Mathf.PI * 0.5f;
                    Vector3 n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 c = n * (radius + 0.03f) + Vector3.up * h;
                    Vector3 t = Vector3.Cross(Vector3.up, n) * 0.28f;
                    var target = rng.NextDouble() < litChance ? slits : dark;
                    target.AddQuad(c - t, c + t, c + t + Vector3.up * 1.6f, c - t + Vector3.up * 1.6f, n);
                }
            string sn = "Slits_" + _seed++, dn = "SlitsDark_" + _seed++;
            SaveMesh(slits.Build(sn), sn); SaveMesh(dark.Build(dn), dn);
            Place("SlitsLit", sn, "WindowGlow", basePos, Yaw(basePos.x * 7f), root, false, 1f, false);
            Place("SlitsDark", dn, "WindowDark", basePos, Yaw(basePos.x * 7f), root, false, 1f, false);
        }

        /// <summary>A crenellated parapet along a line (merlons on a low wall).</summary>
        static void Battlements(Transform parent, string name, Vector3 a, Vector3 b, float h)
        {
            var mb = new MeshBuilder { uvScale = 0.5f };
            Vector3 d = b - a; float len = d.magnitude; Vector3 dir = d / len;
            var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
            int n = Mathf.Max(2, Mathf.RoundToInt(len / 1.6f));
            for (int i = 0; i < n; i++)
            {
                if (i % 2 == 1) continue;
                Vector3 p = dir * (len * (i + 0.5f) / n) + Vector3.up * (h * 0.5f);
                ProceduralMeshes.Box(mb, Quaternion.Inverse(rot) * p, new Vector3(len / n * 0.95f, h, 1.0f), _seed + i, 0.02f);
            }
            string mn = name + "_" + _seed++;
            SaveMesh(mb.Build(mn), mn);
            Place(name, mn, "StoneCastle", a, rot, parent, false);
        }

        /// <summary>A curtain wall between two towers, crowned with battlements.</summary>
        static void Curtain(Transform parent, Vector3 a, Vector3 b, float height)
        {
            WallRun(parent, "Curtain_" + _seed++, a, b, height, 2.4f, new List<Op>(), "StoneCastle");
            Battlements(parent, "CurtainCrenels", a + Vector3.up * height, b + Vector3.up * height, 1.3f);
        }

        /// <summary>An arched stone viaduct (deck on top, parapets). walkTop: deck height above 'a'.</summary>
        static void Viaduct(Transform parent, string name, Vector3 a, Vector3 b, float deckH, float width, bool parapets)
        {
            Vector3 d = b - a; float len = MathUtil.Flat(d).magnitude;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 9f));
            var ops = new List<Op>();
            for (int i = 0; i < n; i++) ops.Add(new Op(len * (i + 0.5f) / n, len / n * 0.62f, deckH * 0.55f, deckH * 0.78f));
            WallRun(parent, name, a, b, deckH, width, ops, "StoneCastle");
            Vector3 dir = MathUtil.Flat(d).normalized;
            var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
            var deck = new MeshBuilder { uvScale = 0.5f };
            ProceduralMeshes.Box(deck, new Vector3(len * 0.5f, deckH + 0.2f, 0f), new Vector3(len, 0.4f, width + 0.4f), _seed++, 0.02f);
            string dn = name + "_Deck";
            SaveMesh(deck.Build(dn), dn);
            Place(name + "Deck", dn, "Flagstones", a, Quaternion.LookRotation(dir) * Quaternion.Euler(0, -90, 0), parent, false);
            Battlements(parent, name + "_ParL", a + Vector3.up * (deckH + 0.4f) + Vector3.Cross(Vector3.up, dir) * (width * 0.5f), b + Vector3.up * (deckH + 0.4f) + Vector3.Cross(Vector3.up, dir) * (width * 0.5f), parapets ? 1.0f : 0.8f);
            Battlements(parent, name + "_ParR", a + Vector3.up * (deckH + 0.4f) - Vector3.Cross(Vector3.up, dir) * (width * 0.5f), b + Vector3.up * (deckH + 0.4f) - Vector3.Cross(Vector3.up, dir) * (width * 0.5f), parapets ? 1.0f : 0.8f);
        }

        // ================================================================== prefabs
        static void BuildCastlePrefabs(Prefabs p, AnimatorController ctrl, AnimatorController archerCtrl)
        {
            // ---- progress (network state of the chapter)
            {
                var go = new GameObject("CastleProgress");
                go.AddComponent<NetworkObject>();
                go.AddComponent<CastleProgress>();
                p.progress = SavePrefab(go, "CastleProgress");
            }
            // ---- Rotting Gaoler / Gaol Warden (knights)
            p.gaoler = BuildKnightEnemy("Gaoler", "Rotting Gaoler", M["KnightGaoler"], 1.9f, ctrl, 130f, 45f, 1f);
            p.warden = BuildKnightEnemy("Warden", "The Gaol Warden", M["KnightWarden"], 1.9f * 1.35f, ctrl, 460f, 95f, 1.25f);
            // ---- Hollow Bowman (archer)
            {
                var root = BuildCharacter("HollowBowman", M["ArcherHollow"], 1.85f, archerCtrl, out var ca, out _, out var lockPoint, ArcherFbx, "BW_Idle", false);
                root.AddComponent<NetworkObject>();
                ConfigureNT(root.AddComponent<NetworkTransform>(), false);
                var ai = root.AddComponent<ArcherEnemyAI>();
                ai.anim = ca;
                ai.displayName = "Hollow Bowman";
                ai.baseHealth = 80f;
                ai.basePoise = 25f;
                ai.walkSpeed = 2f; ai.chaseSpeed = 3.6f;
                ai.bow = AttachBow(root, ca, p.arrow);
                ai.shot.hitStart = ai.shot.hitEnd = ai.shot.swingPeak = ShotRelease;
                var lot = root.AddComponent<LockOnTarget>(); lot.lockPoint = lockPoint;
                p.bowman = SavePrefab(root, "HollowBowman");
            }
            p.dragon = BuildDragonPrefab();
            // ---- dragon arena (fog gate at the top of the stair)
            {
                var root = new GameObject("DragonArena");
                root.AddComponent<NetworkObject>();
                var arena = root.AddComponent<DragonArena>();
                var fog = new GameObject("FogGate"); fog.transform.SetParent(root.transform, false);
                fog.AddComponent<MeshFilter>().sharedMesh = Meshes["FogSheet"];
                var mr = fog.AddComponent<MeshRenderer>(); mr.sharedMaterial = M["FogGate"]; mr.shadowCastingMode = ShadowCastingMode.Off;
                fog.AddComponent<UVScroll>().speed = new Vector2(0.02f, 0.05f);
                var colGo = new GameObject("Barrier"); colGo.transform.SetParent(root.transform, false);
                var box = colGo.AddComponent<BoxCollider>(); box.center = new Vector3(0, 3.6f, 0.1f); box.size = new Vector3(5f, 7.4f, 0.6f);
                var lightGo = new GameObject("FogLight"); lightGo.transform.SetParent(root.transform, false); lightGo.transform.localPosition = new Vector3(0, 3f, -1.2f);
                var l = lightGo.AddComponent<Light>(); l.type = LightType.Point; l.range = 7f; l.intensity = 1.2f; l.color = new Color(0.75f, 0.75f, 0.7f);
                arena.fogRenderer = mr; arena.fogCollider = box; arena.fogLight = l;
                p.dragonArena = SavePrefab(root, "DragonArena");
            }
        }

        static GameObject BuildKnightEnemy(string prefabName, string display, Material mat, float height, AnimatorController ctrl, float hp, float poise, float dmgScale)
        {
            var root = BuildCharacter(prefabName, mat, height, ctrl, out var ca, out var hitbox, out var lockPoint);
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), false);
            var ai = root.AddComponent<GuardianAI>();
            ai.anim = ca; ai.weapon = hitbox;
            ai.displayName = display;
            ai.baseHealth = hp;
            ai.basePoise = poise;
            ApplySwing(ai.slash, 0.45f);
            ApplySwing(ai.sweep, 0.35f);
            ApplySwing(ai.overhead, 0f, true);
            if (dmgScale != 1f)
            {
                foreach (var a in new[] { ai.slash, ai.sweep, ai.overhead }) { a.damage *= dmgScale; a.poiseDamage *= dmgScale; a.animSpeed *= 0.92f; }
                ai.aggroRange = 11f;
                ai.leashRange = 18f;
                ai.knockbackFactor = 0.4f;
            }
            ca.swingPitch = 0.9f / Mathf.Sqrt(height / 1.9f);
            var lot = root.AddComponent<LockOnTarget>(); lot.lockPoint = lockPoint; lot.framingSize = height / 1.9f;
            return SavePrefab(root, prefabName);
        }

        static ArcherBow AttachBow(GameObject root, CharacterAnimator ca, GameObject arrowPrefab)
        {
            var model = root.GetComponentInChildren<Animator>().transform;
            var bowGo = new GameObject("Bow"); bowGo.transform.SetParent(root.transform, false);
            var bow = bowGo.AddComponent<ArcherBow>();
            bow.anim = ca;
            bow.grip = FindDeep(model, "BowSocket"); bow.bowTop = FindDeep(model, "BowTop"); bow.bowBottom = FindDeep(model, "BowBottom"); bow.nock = FindDeep(model, "NockSocket");
            var lr = bowGo.AddComponent<LineRenderer>();
            lr.positionCount = 3; lr.useWorldSpace = true; lr.widthMultiplier = 0.011f; lr.numCapVertices = 0;
            lr.sharedMaterial = M["BowString"]; lr.shadowCastingMode = ShadowCastingMode.Off;
            bow.stringLine = lr;
            var nocked = new GameObject("NockedArrow"); nocked.transform.SetParent(root.transform, false);
            nocked.AddComponent<MeshFilter>().sharedMesh = Meshes["Arrow"];
            nocked.AddComponent<MeshRenderer>().sharedMaterial = M["ArcherPlayer"];
            bow.nockedArrow = nocked.transform;
            bow.arrowPrefab = arrowPrefab;
            bow.draws = new[]
            {
                new ArcherBow.DrawWindow { state = CharacterAnimator.Attack1, nock = ShotNock, release = ShotRelease },
                new ArcherBow.DrawWindow { state = CharacterAnimator.Heavy, nock = PowerNock, release = PowerRelease },
            };
            return bow;
        }

        // ================================================================== the dragon
        static void ConfigureDragonImport()
        {
            if (!(AssetImporter.GetAtPath(DragonFbx) is ModelImporter mi)) { Debug.LogWarning("[DF] Missing " + DragonFbx + " (Tools/dragon_export.py)"); return; }
            bool dirty = mi.animationType != ModelImporterAnimationType.Generic || mi.materialImportMode != ModelImporterMaterialImportMode.None
                         || mi.clipAnimations == null || mi.clipAnimations.Length == 0 || mi.clipAnimations.Any(c => !c.loopTime);
            if (!dirty) return;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false; mi.importLights = false;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations.ToList();
            foreach (var c in clips)
            {
                c.loopTime = true; c.loopPose = false;
                c.lockRootRotation = false; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();
        }

        static AnimatorController BuildDragonAnimator()
        {
            AnimationClip C(string n) => AnimationAnalysis.FindClipIn(DragonFbx, n);
            var idle = C("D_Idle"); var walk = C("D_Walk"); var run = C("D_Run"); var hover = C("D_Hover"); var fly = C("D_Fly"); var glide = C("D_Glide");
            if (idle == null) { Debug.LogError("[DF] Dragon.fbx: clip D_Idle not found"); return null; }
            string path = $"{Gen}/Animation/Dragon.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var loco = ctrl.CreateBlendTreeInController(DragonVisuals.Locomotion, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            if (walk) tree.AddChild(walk, 2.6f);
            if (run) tree.AddChild(run, 7.5f);
            var sm = ctrl.layers[0].stateMachine;
            sm.AddState(DragonVisuals.Hover).motion = hover ? hover : idle;
            sm.AddState(DragonVisuals.Fly).motion = fly ? fly : (hover ? hover : idle);
            sm.AddState("Glide").motion = glide ? glide : idle;
            sm.defaultState = loco;
            return ctrl;
        }

        static GameObject BuildDragonPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(DragonFbx);
            if (fbx == null) { Debug.LogError("[DF] Missing " + DragonFbx); return null; }
            var ctrl = BuildDragonAnimator();
            if (ctrl == null) return null;
            var idle = AnimationAnalysis.FindClipIn(DragonFbx, "D_Idle");

            const float targetLength = 13f;   // nose to tail tip, metres
            var root = new GameObject("Dragon");
            var visual = new GameObject("VisualRoot").transform; visual.SetParent(root.transform, false);
            var offset = new GameObject("ModelOffset").transform; offset.SetParent(visual, false);
            var model = (GameObject)Object.Instantiate(fbx, offset, false);
            model.name = "DragonModel";
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            if (idle) idle.SampleAnimation(model, 0f);

            Transform B(string n) { var t = FindDeep(model.transform, n); if (!t) Debug.LogWarning("[DF] Dragon bone missing: " + n); return t; }
            Transform head = B("head"), breast = B("breast"), pelvis = B("pelvic_2"), paunch = B("paunch");
            Transform tailTip = FindDeep(model.transform, "ik_ACT_tail_5") ?? B("tail_3");
            if (!head || !tailTip) { Object.DestroyImmediate(root); return null; }

            // face +Z, scale to the target length
            Vector3 fwd = MathUtil.Flat(head.position - tailTip.position);
            float yaw = Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up);
            offset.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float len = MathUtil.Flat(head.position - tailTip.position).magnitude;
            float s = targetLength / Mathf.Max(0.01f, len) * 0.92f;   // the head bone sits behind the snout
            offset.localScale = Vector3.one * s;
            if (idle) idle.SampleAnimation(model, 0f);
            // origin: under the belly; feet on the ground
            Vector3 org = paunch ? paunch.position : (breast.position + pelvis.position) * 0.5f;
            float minY = float.MaxValue;
            foreach (var t in model.GetComponentsInChildren<Transform>()) minY = Mathf.Min(minY, t.position.y);
            offset.localPosition = new Vector3(-org.x, -minY + 0.05f, -org.z);
            if (idle) idle.SampleAnimation(model, 0f);

            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = M["DragonSkin"];
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer sk) { sk.updateWhenOffscreen = true; sk.quality = SkinQuality.Bone4; }
            }
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            float headZ = head.position.z, headY = head.position.y;
            float bodyZ = breast && pelvis ? (breast.position.z + pelvis.position.z) * 0.5f : 0f;

            var cc = root.AddComponent<CharacterController>();
            cc.radius = 1.7f; cc.height = 4.2f; cc.center = new Vector3(0f, 2.15f, bodyZ);
            cc.slopeLimit = 40f; cc.stepOffset = 0.6f; cc.skinWidth = 0.08f;
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), false);

            var ai = root.AddComponent<DragonAI>();
            ai.displayName = "Vaelthrax, the Cinder Wyrm";
            ai.baseHealth = 2400f; ai.basePoise = 300f; ai.poiseRegenPerSecond = 22f; ai.poiseRegenDelay = 3.5f;
            ai.staggerDuration = DragonMoves.StaggerDur; ai.parryStaggerDuration = DragonMoves.StaggerDur;
            ai.walkSpeed = 3.2f; ai.chaseSpeed = 5f; ai.turnSpeed = 70f;
            ai.scale = targetLength / 17f;
            ai.headForward = headZ + 0.9f * ai.scale;
            ai.headHeight = headY;

            var vis = root.AddComponent<DragonVisuals>();
            vis.ai = ai; vis.animator = animator; vis.modelRoot = visual; vis.scale = ai.scale;
            vis.rootBone = pelvis; vis.frontBody = breast; vis.head = head;
            vis.neck = new[] { B("ACT_neck_4"), B("ACT_neck_3"), B("ACT_neck_2"), B("ACT_neck_1") };
            vis.tail = new[] { B("tail_1"), B("tail_2"), B("tail_3") };
            vis.feet = new[] { FindDeep(model.transform, "Oberschenkel_L"), FindDeep(model.transform, "Oberschenkel_R"), FindDeep(model.transform, "upper_arm_L"), FindDeep(model.transform, "upper_arm_R") }.Where(t => t).ToArray();
            var ml = new GameObject("MouthLight"); ml.transform.SetParent(root.transform, false); ml.transform.localPosition = new Vector3(0, headY, headZ);
            var mouth = ml.AddComponent<Light>(); mouth.type = LightType.Point; mouth.color = new Color(1f, 0.5f, 0.18f); mouth.range = 14f; mouth.intensity = 0f; mouth.shadows = LightShadows.None;
            vis.mouthLight = mouth;

            // hurtboxes along the body, neck and tail (triggers: weapons and arrows land on the visible body)
            void Hurt(Transform bone, Vector3 a, Vector3 b, float radius)
            {
                if (!bone) return;
                var go = new GameObject("Hurtbox"); go.transform.SetParent(bone, true);
                go.transform.position = (a + b) * 0.5f;
                Vector3 dir = b - a;
                go.transform.rotation = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : Quaternion.identity;
                SetWorldScale(go.transform, 1f);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.isTrigger = true; cap.direction = 2; cap.radius = radius; cap.height = dir.magnitude + radius * 2f;
            }
            float k = ai.scale;
            var neckChain = new List<Transform> { breast };
            neckChain.AddRange(vis.neck.Where(t => t));
            neckChain.Add(head);
            for (int i = 0; i < neckChain.Count - 1; i++)
                if (neckChain[i] && neckChain[i + 1]) Hurt(neckChain[i], neckChain[i].position, neckChain[i + 1].position, (i == 0 ? 1.5f : 0.75f) * k / 0.765f);
            Hurt(head, head.position, head.position + Vector3.forward * 1.1f * k, 0.75f * k / 0.765f);
            if (pelvis && breast) Hurt(pelvis, pelvis.position, breast.position, 1.7f * k / 0.765f);
            var tailChain = new List<Transform> { pelvis };
            tailChain.AddRange(vis.tail.Where(t => t));
            tailChain.Add(tailTip);
            for (int i = 0; i < tailChain.Count - 1; i++)
                if (tailChain[i] && tailChain[i + 1]) Hurt(tailChain[i], tailChain[i].position, tailChain[i + 1].position, Mathf.Lerp(1.1f, 0.35f, i / 3f) * k / 0.765f);

            var lot = root.AddComponent<LockOnTarget>();
            var lp = new GameObject("LockPoint").transform; lp.SetParent(breast ? breast : root.transform, true);
            lp.position = breast ? breast.position + Vector3.up * 0.4f : root.transform.position + Vector3.up * 3f;
            lot.lockPoint = lp; lot.framingSize = 3f; lot.enabled = false;

            Debug.Log($"[DF] Dragon prefab: scale {s:0.000}, yaw {yaw:0}, head z {headZ:0.0} y {headY:0.0}, body z {bodyZ:0.0}");
            return SavePrefab(root, "Dragon");
        }

        // ================================================================== level refs
        static void WireCastleRefs(LevelRefs lv, Transform systems, Transform playerSpawn)
        {
            Transform[] Points(string name, List<(Vector3 p, float yaw)> list) => list.Select(e =>
            {
                var t = new GameObject(name).transform; t.SetParent(systems); t.position = e.p; t.rotation = Yaw(e.yaw); return t;
            }).ToArray();
            lv.castleKnights = Points("GaolerSpawn", _gaolers);
            lv.castleArchers = Points("BowmanSpawn", _bowmen);
            lv.wardens = Points("WardenSpawn", _wardens);

            var cps = new List<LevelRefs.CheckpointDef> { new LevelRefs.CheckpointDef { name = "The Bonfire", point = playerSpawn, reachRadius = 0f } };
            string[] order = { "The Throne", "Hall of Chains", "The Grand Stair", "The Long Bridge", "The Thornwood", "The Glass Temple" };
            foreach (var c in _checkpoints.OrderBy(c => System.Array.IndexOf(order, c.name)))
            {
                var t = new GameObject("Checkpoint_" + c.name).transform; t.SetParent(systems); t.position = c.p; t.rotation = Yaw(c.yaw);
                cps.Add(new LevelRefs.CheckpointDef { name = c.name, point = t, reachRadius = 6f });
            }
            lv.checkpoints = cps.ToArray();

            var ds = new GameObject("DragonSpawn").transform; ds.SetParent(systems); ds.position = new Vector3(0f, TerraceY + 0.15f, 188f); ds.rotation = Yaw(180);
            lv.dragonSpawn = ds;
            var gate = new GameObject("DragonGate").transform; gate.SetParent(systems); gate.position = new Vector3(0f, TerraceY + 0.05f, 157.6f); gate.rotation = Yaw(0);
            lv.dragonGate = gate;
            lv.dragonArena = new Bounds(new Vector3(0f, TerraceY + 10f, 182f), new Vector3(50f, 28f, 48.4f));

            lv.zones = new[]
            {
                new LevelRefs.AtmosphereZone { name = "Castle interior", bounds = new Bounds(new Vector3(0f, 6f, 112f), new Vector3(112f, 24f, 92f)),
                    fog = new Color(0.07f, 0.06f, 0.055f), fogDensity = 0.034f, ambient = new Color(0.34f, 0.3f, 0.27f), exposure = 1.1f },
                new LevelRefs.AtmosphereZone { name = "Terrace", bounds = new Bounds(new Vector3(0f, 20f, 190f), new Vector3(90f, 50f, 64f)),
                    fog = new Color(0.42f, 0.27f, 0.25f), fogDensity = 0.006f, ambient = new Color(1.0f, 0.78f, 0.7f), exposure = 0.95f,
                    sky = true, overrideSun = true, sunColor = new Color(1f, 0.6f, 0.34f), sunIntensity = 2.4f, sunEuler = new Vector3(8f, 180f, 0f) },
            };

            // the sunset dome (only visible in the terrace zone) and the main light it re-tints
            var domeGo = new GameObject("SkyDome");
            domeGo.transform.SetParent(systems, false);
            var domeChild = new GameObject("Dome"); domeChild.transform.SetParent(domeGo.transform, false);
            domeChild.transform.localScale = Vector3.one * 140f;   // the built-in sphere has radius 1: 140 m, inside the 180 m far plane
            domeChild.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var dmr = domeChild.AddComponent<MeshRenderer>();
            dmr.sharedMaterial = M["SkyDome"]; dmr.shadowCastingMode = ShadowCastingMode.Off; dmr.receiveShadows = false;
            dmr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; dmr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var sd = domeGo.AddComponent<SkyDome>(); sd.dome = dmr;
            lv.skyDome = sd;
            lv.mainLight = RenderSettings.sun;

            // the terrace gets its own colour grade: the castle's green-grey look would turn the sunset
            // into a flat salmon wash, so out here the colours are let through (warm, saturated)
            {
                string vpath = $"{Gen}/Rendering/TerraceVolume.asset";
                if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(vpath) != null) AssetDatabase.DeleteAsset(vpath);
                var prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, vpath);
                var tca = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                tca.contrast.Override(10f); tca.saturation.Override(-4f); tca.colorFilter.Override(new Color(1f, 0.97f, 0.95f));
                var tsp = prof.Add<UnityEngine.Rendering.Universal.SplitToning>(true);
                tsp.shadows.Override(new Color(0.45f, 0.4f, 0.5f)); tsp.highlights.Override(new Color(0.62f, 0.5f, 0.4f)); tsp.balance.Override(10f);
                var tbl = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true); tbl.intensity.Override(0.7f); tbl.threshold.Override(0.85f); tbl.tint.Override(new Color(1f, 0.75f, 0.5f));
                foreach (var c in prof.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, prof); }
                EditorUtility.SetDirty(prof);
                var vg = new GameObject("TerraceGrade");
                vg.transform.SetParent(systems, false);
                vg.transform.position = new Vector3(0f, 20f, 190f);
                var box = vg.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(90f, 50f, 64f);
                vg.layer = 2;   // Ignore Raycast
                var vol = vg.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = 10f; vol.blendDistance = 10f; vol.sharedProfile = prof;
            }
        }
    }
}
