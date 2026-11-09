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
    /// Chapter 3 — beyond the wyrm:
    ///   terrace ─BRIDGE GATE (opens when Vaelthrax dies)─► Keep Bridge ─► gate towers ─► the Long Bridge (z 242→336, sunset)
    ///   ─► the Stair Tower: a spiral stair down 32 m into the ravine ─► the Thornwood (twisted trees, glass shards, wraiths)
    ///   ─► the Glass Temple (z ~505, y -24): a dream of floating pillars and broken panes; the Shattered Mirror of Malice.
    /// Also the prison secrets (vault behind an illusory wall, the minstrel's room, Oswin, the duck, Ember Flasks),
    /// the radiant weapons and their chest.
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const string MirrorArt = "Assets/DarkFantasy/Art/Mirror";
        const float ForestY = -24f;
        const float BridgeDeckY = 7.9f;
        static readonly Vector3 TowerC = new Vector3(0f, 0f, 344f);
        static readonly Vector3 TempleC = new Vector3(0f, ForestY, 505f);
        const float TempleFloorY = ForestY + 0.6f;

        static readonly List<(Vector3 p, float yaw)> _wraiths = new List<(Vector3, float)>();
        static readonly List<(Vector3 p, float yaw)> _revenants = new List<(Vector3, float)>();
        static (Vector3 p, float yaw) _vaultChest;

        // ================================================================== materials / sounds
        static Texture2D MirrorTex(string file, bool normal = false)
        {
            string path = $"{MirrorArt}/{file}";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.maxTextureSize = 512; ti.mipmapEnabled = true; ti.wrapMode = file.StartsWith("arm") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.alphaIsTransparency = file == "vortex.png";
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) Debug.LogWarning("[DF] Missing " + path);
            return t;
        }

        static void BuildMirrorMaterials()
        {
            string T(string n) => $"{Gen}/Textures/{n}.png";
            var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(T("rust_metal"));
            var steel = AssetDatabase.LoadAssetAtPath<Texture2D>(T("steel"));
            var ash = AssetDatabase.LoadAssetAtPath<Texture2D>(T("ash"));
            var bark = AssetDatabase.LoadAssetAtPath<Texture2D>(T("bark"));
            var ground = AssetDatabase.LoadAssetAtPath<Texture2D>(T("ground_mud"));
            var flag = AssetDatabase.LoadAssetAtPath<Texture2D>(T("flagstones"));
            var bricks = AssetDatabase.LoadAssetAtPath<Texture2D>(T("stone_bricks"));
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>(T("rotten_wood"));
            var knight = AssetDatabase.LoadAssetAtPath<Texture2D>(KnightTex);

            var gi = MirrorTex("glass_intact.png");
            MirrorTex("glass_shattered.png");
            var arm = MirrorTex("arm_lava.png");
            var armE = MirrorTex("arm_lava_emission.png");
            var vortex = MirrorTex("vortex.png");

            var g = LitMat("MirrorGlass", gi, Color.white, emission: new Color(0.5f, 0.3f, 0.9f));
            g.SetTexture("_EmissionMap", gi); g.SetFloat("_Smoothness", 0.9f);
            var a = LitMat("DemonArm", arm, new Color(0.9f, 0.85f, 0.85f), emission: new Color(1.6f, 0.75f, 0.35f));
            a.SetTexture("_EmissionMap", armE); a.SetTextureScale("_BaseMap", new Vector2(1f, 2f));
            LitMat("DemonClaw", ash, new Color(0.14f, 0.12f, 0.12f), emission: new Color(0.06f, 0.01f, 0f));
            LitMat("MirrorFrame", metal, new Color(0.2f, 0.18f, 0.22f), emission: new Color(0.02f, 0.0f, 0.04f));
            LitMat("MirrorBone", ash, new Color(0.78f, 0.72f, 0.62f), emission: new Color(0.02f, 0.02f, 0.02f));
            LitMat("MirrorEyes", null, new Color(0.7f, 0.3f, 1f), emission: new Color(2.2f, 0.7f, 3f));
            TransparentMat("PortalSwirl", vortex, new Color(1f, 1f, 1f, 1f), true, "Universal Render Pipeline/Particles/Unlit");
            TransparentMat("GlassShard", null, new Color(0.72f, 0.78f, 1f, 0.5f), false);
            TransparentMat("GlassShardGlow", null, new Color(0.75f, 0.55f, 1f, 0.55f), true);
            TransparentMat("Ghost", null, new Color(0.45f, 0.75f, 1f, 0.42f), true);
            TransparentMat("TempleGlass", null, new Color(0.6f, 0.55f, 0.95f, 0.28f), false);

            LitMat("ThornGround", ground, new Color(0.34f, 0.3f, 0.38f));
            LitMat("ThornBark", bark, new Color(0.22f, 0.18f, 0.25f), emission: new Color(0.01f, 0f, 0.02f));
            LitMat("TempleStone", bricks, new Color(0.5f, 0.48f, 0.58f), emission: new Color(0.015f, 0.01f, 0.03f));
            // the temple floor: black polished glass that mirrors the motes and the pillars
            var fl = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fl.SetTexture("_BaseMap", flag);
            fl.SetColor("_BaseColor", new Color(0.16f, 0.13f, 0.22f));
            fl.SetFloat("_Smoothness", 0.94f); fl.SetFloat("_Metallic", 0.35f);
            fl.EnableKeyword("_EMISSION"); fl.SetColor("_EmissionColor", new Color(0.02f, 0.01f, 0.04f));
            fl.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            SaveMat("TempleFloor", fl);
            var silver = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            silver.SetColor("_BaseColor", new Color(0.62f, 0.62f, 0.7f));
            silver.SetFloat("_Smoothness", 0.97f); silver.SetFloat("_Metallic", 1f);
            silver.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            SaveMat("MirrorSilver", silver);
            TransparentMat("LightBeam", null, new Color(0.8f, 0.72f, 1f, 0.09f), true);
            TransparentMat("LightBeamPrism", null, new Color(0.55f, 0.85f, 1f, 0.12f), true);

            LitMat("WraithRobe", ash, new Color(0.09f, 0.08f, 0.12f), emission: new Color(0.05f, 0.02f, 0.1f));
            LitMat("RevenantGlass", knight, new Color(0.5f, 0.55f, 0.72f), emission: new Color(0.06f, 0.04f, 0.14f));
            var rs = LitMat("RadiantSilver", steel, new Color(1f, 1f, 1.05f), emission: new Color(0.22f, 0.24f, 0.3f));
            rs.SetFloat("_Smoothness", 0.75f);
            LitMat("RadiantGold", metal, new Color(1f, 0.9f, 0.6f), emission: new Color(1.3f, 1.0f, 0.45f));
            LitMat("EmberFlask", null, new Color(1f, 0.5f, 0.2f), emission: new Color(2.4f, 0.8f, 0.18f));
            LitMat("FlaskGlass", null, new Color(0.25f, 0.18f, 0.12f));
            LitMat("Duck", wood, new Color(1f, 0.84f, 0.32f));
            LitMat("DuckBeak", null, new Color(1f, 0.48f, 0.1f));
            LitMat("OswinCloth", wood, new Color(0.34f, 0.3f, 0.27f));
            LitMat("OswinSkin", null, new Color(0.55f, 0.5f, 0.46f));
        }

        static Sfx.Entry[] MirrorSfx() => new[]
        {
            E(SfxId.MirrorShatter, new Vector2(0.9f, 1.1f), 1.2f, 0.5f, L("Coins 2-1.wav", 0f, 0.9f, 1.9f), L("Shield Metal Impact 1-3.wav", 0f, 0.7f, 1.7f), L("Rock Large Debris 1-3.wav", 0.24f, 0.6f, 1.2f)),
            E(SfxId.MirrorHum, new Vector2(0.95f, 1.05f), 0.8f, 0.6f, L("Underwater 01.wav", 0f, 0.7f, 0.6f), L("Magical Interface 5-1.wav", 0f, 0.35f, 0.45f)),
            E(SfxId.MirrorVortex, new Vector2(0.95f, 1.05f), 1.3f, 0.4f, L("Ambient Wind Loop 1.wav", 0.5f, 1f, 0.5f), L("Underwater 01.wav", 0f, 0.8f, 0.4f), L("Magical Interface 8-1.wav", 0f, 0.4f, 0.4f)),
            E(SfxId.MirrorSlam, new Vector2(0.9f, 1.05f), 1.3f, 0.6f, L("Rock Impact 37.wav", 0f, 1f, 0.6f), L("Explosion Medium 2-1.wav", 0f, 0.6f, 0.8f), L("Coins 2-1.wav", 0f, 0.4f, 1.6f)),
            E(SfxId.MirrorCurse, new Vector2(0.9f, 1.1f), 0.9f, 0.7f, L("Magical Interface 8-1.wav", 0f, 0.8f, 0.55f), L("Zombie 1 - Short 1-01.wav", 0f, 0.35f, 1.5f)),
        };

        // ================================================================== mesh helpers
        static Mesh ShardMesh(string name, float length, float radius, int sides = 3, int seed = 1)
        {
            var mb = new MeshBuilder { uvScale = 1f };
            mb.AddPrism(new Vector3(0, 0, -length * 0.22f), new Vector3(0, 0, length * 0.78f), radius, 0.002f, sides, true, false, seed, 0.4f);
            return SaveMesh(mb.Build(name), name);
        }

        /// <summary>A twisted, thorned tree: the trunk corkscrews, branches claw upward and end in spikes.</summary>
        static Mesh TwistedTree(int seed, float height)
        {
            var mb = new MeshBuilder { uvScale = 1.2f };
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            Vector3 p = Vector3.zero, d = Vector3.up;
            float r = height * 0.06f;
            int segs = 7;
            float twist = (R() < 0.5f ? -1f : 1f) * (40f + R() * 30f);
            for (int s = 0; s < segs; s++)
            {
                d = Quaternion.AngleAxis(twist, Vector3.up) * (d + new Vector3(R() - 0.5f, 0.15f, R() - 0.5f) * 0.6f);
                d = new Vector3(d.x, Mathf.Max(0.45f, d.y), d.z).normalized;
                Vector3 q = p + d * (height / segs);
                float r2 = r * 0.82f;
                mb.color = s == 0 ? new Color(0.55f, 0.55f, 0.55f) : Color.white;
                mb.AddPrism(p, q, r, r2, 6, false, s == segs - 1, seed + s, 0.3f);
                if (s >= 2)
                {
                    int kids = 1 + (R() < 0.5f ? 1 : 0);
                    for (int k = 0; k < kids; k++)
                    {
                        Vector3 side = Quaternion.AngleAxis(R() * 360f, Vector3.up) * Vector3.right;
                        Vector3 bd = (side * 0.9f + Vector3.up * (0.3f + R() * 0.6f)).normalized;
                        Vector3 b0 = q, bl = b0;
                        float br = r2 * 0.5f, blen = height * (0.18f + R() * 0.16f);
                        for (int j = 0; j < 3; j++)
                        {
                            bd = (Quaternion.AngleAxis(twist * 0.6f, Vector3.up) * bd + Vector3.up * 0.25f).normalized;
                            Vector3 b1 = bl + bd * blen / 3f;
                            mb.AddPrism(bl, b1, br, br * 0.6f, 4, false, j == 2, seed + 50 + s * 7 + k * 3 + j, 0.2f);
                            // thorns
                            Vector3 tn = Vector3.Cross(bd, Vector3.up).normalized * (R() < 0.5f ? -1 : 1);
                            mb.AddPrism(b1, b1 + (tn + bd * 0.3f).normalized * br * 4f, br * 0.4f, 0.005f, 3, false, false, seed + 90 + j, 0f);
                            bl = b1; br *= 0.6f;
                        }
                    }
                }
                p = q; r = r2;
            }
            mb.AddPrism(p, p + d * height * 0.12f, r, 0.01f, 5, false, false, seed + 200, 0.1f);
            // roots clawing the ground
            for (int k = 0; k < 5; k++)
            {
                Vector3 rd = Quaternion.Euler(0, k * 72f + R() * 30f, 0) * new Vector3(1f, -0.3f, 0f);
                mb.AddPrism(Vector3.up * 0.3f, rd.normalized * height * 0.16f, height * 0.035f, 0.01f, 4, false, false, seed + 300 + k, 0.15f);
            }
            string n = "TwistedTree_" + seed;
            return SaveMesh(mb.Build(n), n);
        }

        /// <summary>Little skull: cranium, jaw, two dark sockets (sockets are separate glowing quads).</summary>
        static void AddSkull(MeshBuilder mb, Vector3 c, float s, Quaternion rot)
        {
            Matrix4x4 m = Matrix4x4.TRS(c, rot, Vector3.one * s);
            var sk = new MeshBuilder { uvScale = 2f };
            sk.AddPrism(new Vector3(0, -0.15f, 0), new Vector3(0, 0.35f, 0), 0.42f, 0.36f, 7, true, false, 3, 0.1f);
            sk.AddPrism(new Vector3(0, 0.35f, 0), new Vector3(0, 0.62f, 0), 0.36f, 0.15f, 7, false, true, 4, 0.05f);
            sk.AddPrism(new Vector3(0, -0.45f, 0.08f), new Vector3(0, -0.15f, 0.05f), 0.24f, 0.33f, 6, true, false, 5, 0.1f);
            mb.Append(sk, m);
        }

        // ================================================================== the boss
        static GameObject BuildMirrorBoss()
        {
            var root = new GameObject("ShatteredMirror");
            var cc = root.AddComponent<CharacterController>();
            cc.radius = 2.4f; cc.height = 7f; cc.center = new Vector3(0f, 3.6f, 0f);
            cc.slopeLimit = 40f; cc.stepOffset = 0.6f; cc.skinWidth = 0.08f;
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), false);
            var ai = root.AddComponent<MirrorAI>();
            ai.displayName = "Shattered Mirror of Malice";
            ai.baseHealth = 2600f; ai.basePoise = 210f; ai.poiseRegenPerSecond = 20f; ai.poiseRegenDelay = 3.5f;
            ai.staggerDuration = MirrorMoves.StaggerDur; ai.parryStaggerDuration = MirrorMoves.StaggerDur;
            ai.walkSpeed = 2.2f; ai.chaseSpeed = 3f; ai.turnSpeed = 80f;
            ai.glassHeight = 4.2f;

            var body = new GameObject("Body").transform; body.SetParent(root.transform, false);
            // ---- the frame: a gothic arch of black iron, skulls and spikes
            const float span = 4.2f, spring = 4.3f, apex = 6.6f, bottom = 0.9f;
            SaveMesh(ProceduralMeshes.ArchFrame(301, span, spring - bottom, apex - bottom, 0.6f, 0.7f), "MirrorFrameArch");
            var fr = Place("Frame", "MirrorFrameArch", "MirrorFrame", Vector3.zero, Quaternion.identity, body, false);
            fr.transform.localPosition = new Vector3(0f, bottom, 0f);
            // base bar under the glass
            var deco = new MeshBuilder { uvScale = 0.8f };
            ProceduralMeshes.Box(deco, new Vector3(0f, bottom - 0.2f, 0f), new Vector3(span + 1.6f, 0.5f, 0.8f), 7, 0.06f);
            // spikes radiating from the outline
            for (int i = 0; i <= 18; i++)
            {
                float t = i / 18f;
                Vector2 a = ProceduralMeshes.PointedArch(t, span + 1.2f, spring - bottom, apex - bottom + 0.6f);
                Vector3 o = new Vector3(a.x, a.y + bottom, 0f);
                Vector3 outward = new Vector3(a.x, a.y - (spring - bottom) * 0.6f, 0f).normalized;
                if (i < 3 || i > 15) outward = new Vector3(Mathf.Sign(a.x), 0.15f, 0f).normalized;
                float len = (i % 3 == 0 ? 1.5f : 0.8f) + Noise.Hash(i, 7) * 0.5f;
                deco.AddPrism(o, o + outward * len, 0.14f, 0.01f, 4, false, false, 400 + i, 0.1f);
            }
            // spikes on the sides, low (claws of the frame)
            foreach (int sd in new[] { -1, 1 })
                for (int k = 0; k < 3; k++)
                {
                    Vector3 o = new Vector3(sd * (span * 0.5f + 0.5f), bottom + 0.6f + k * 1.1f, 0f);
                    deco.AddPrism(o, o + new Vector3(sd * 1.1f, -0.3f + k * 0.3f, 0.2f), 0.12f, 0.01f, 4, false, false, 450 + k, 0.1f);
                }
            // pendant under the frame (it hovers: a cluster of spikes hangs below)
            for (int k = 0; k < 7; k++)
            {
                float ang = k / 7f * Mathf.PI * 2f;
                Vector3 o = new Vector3(Mathf.Cos(ang) * 0.9f, bottom - 0.4f, Mathf.Sin(ang) * 0.25f);
                deco.AddPrism(o, o + new Vector3(Mathf.Cos(ang) * 0.4f, -0.9f - Noise.Hash(k, 3) * 0.7f, 0f), 0.13f, 0.01f, 4, false, false, 480 + k, 0.1f);
            }
            SaveMesh(deco.Build("MirrorFrameDeco"), "MirrorFrameDeco");
            Place("FrameDeco", "MirrorFrameDeco", "MirrorFrame", body.position, Quaternion.identity, body, false);
            // skulls: a crowning skull at the apex, a row along the base, more down the jambs
            var skulls = new MeshBuilder { uvScale = 1f };
            var eyes = new MeshBuilder();
            void Skull(Vector3 c, float s, float yaw)
            {
                AddSkull(skulls, c, s, Quaternion.Euler(0, yaw, 0));
                foreach (int e in new[] { -1, 1 })
                {
                    Vector3 ec = c + Quaternion.Euler(0, yaw, 0) * new Vector3(e * 0.15f * s, 0.12f * s, 0.4f * s);
                    float h = 0.09f * s;
                    eyes.AddQuad(ec + new Vector3(-h, -h, 0), ec + new Vector3(h, -h, 0), ec + new Vector3(h, h, 0), ec + new Vector3(-h, h, 0), Vector3.forward);
                }
            }
            Skull(new Vector3(0f, apex + 0.55f, 0.15f), 0.9f, 0f);
            for (int i = 0; i < 5; i++) Skull(new Vector3(-1.9f + i * 0.95f, bottom - 0.35f, 0.42f), 0.5f, 0f);
            foreach (int sd in new[] { -1, 1 })
                for (int k = 0; k < 3; k++) Skull(new Vector3(sd * (span * 0.5f + 0.3f), bottom + 0.9f + k * 1.35f, 0.42f), 0.45f, 0f);
            SaveMesh(skulls.Build("MirrorSkulls"), "MirrorSkulls");
            SaveMesh(eyes.Build("MirrorSkullEyes"), "MirrorSkullEyes");
            Place("Skulls", "MirrorSkulls", "MirrorBone", body.position, Quaternion.identity, body, false);
            var eg = Place("SkullEyes", "MirrorSkullEyes", "MirrorEyes", body.position, Quaternion.identity, body, false, 1f, false);
            // the glass
            SaveMesh(ProceduralMeshes.ArchPane(span, spring, apex, bottom), "MirrorPane");
            var glass = Place("Glass", "MirrorPane", "MirrorGlass", body.position, Quaternion.identity, body, false, 1f, false);
            // chains hanging from the frame
            foreach (var (x, y, len) in new[] { (-2.6f, 5.2f, 2.8f), (2.7f, 4.6f, 3.4f), (-1.2f, 6.9f, 2.2f), (1.6f, 6.4f, 3.0f) })
            {
                string cm = "MirrorChain_" + _seed++;
                SaveMesh(CastleKit.Chain(_seed++, len), cm);
                var ch = Place("Chain", cm, "Iron", body.position + new Vector3(x, y, -0.3f), Quaternion.identity, body, false);
                ch.transform.localPosition = new Vector3(x, y, -0.3f);
                ch.AddComponent<Sway>().amplitude = 6f;
            }
            // portal swirl, just in front of the glass
            SaveMesh(ProceduralMeshes.Quad(1f, 1f, "PortalQuad"), "PortalQuad");
            var portal = new GameObject("Portal").transform; portal.SetParent(body, false); portal.localPosition = new Vector3(0f, ai.glassHeight, 0.25f);
            var pq = Place("Swirl", "PortalQuad", "PortalSwirl", Vector3.zero, Quaternion.identity, portal, false, 1f, false);
            pq.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            var glg = new GameObject("GlassLight"); glg.transform.SetParent(body, false); glg.transform.localPosition = new Vector3(0f, ai.glassHeight, 1.6f);
            var gl = glg.AddComponent<Light>(); gl.type = LightType.Point; gl.color = MirrorFx.Void; gl.range = 16f; gl.intensity = 3f; gl.shadows = LightShadows.None;

            // ---- the arms: upper / fore segments (pointing +Z from their joint) and a clawed hand
            var vis = root.AddComponent<MirrorVisuals>();
            vis.ai = ai; vis.body = body; vis.glass = glass.GetComponent<MeshRenderer>();
            vis.glassIntact = AssetDatabase.LoadAssetAtPath<Texture2D>($"{MirrorArt}/glass_intact.png");
            vis.glassShattered = AssetDatabase.LoadAssetAtPath<Texture2D>($"{MirrorArt}/glass_shattered.png");
            vis.portal = portal; vis.glassLight = gl;
            vis.portalRenderer = pq.GetComponent<MeshRenderer>();

            const float upperL = 3.7f, foreL = 3.6f;
            {
                var mb = new MeshBuilder { uvScale = 0.6f };
                mb.AddPrism(Vector3.zero, new Vector3(0, 0, upperL * 0.55f), 0.62f, 0.7f, 7, true, false, 11, 0.25f);
                mb.AddPrism(new Vector3(0, 0, upperL * 0.55f), new Vector3(0, 0, upperL), 0.7f, 0.5f, 7, false, true, 12, 0.25f);
                // bony spurs along the arm
                for (int k = 0; k < 3; k++) mb.AddPrism(new Vector3(0, 0.5f, 0.8f + k * 0.9f), new Vector3(0, 1.05f, 0.5f + k * 0.9f), 0.12f, 0.01f, 4, false, false, 20 + k, 0f);
                SaveMesh(mb.Build("DemonUpperArm"), "DemonUpperArm");
                var f = new MeshBuilder { uvScale = 0.6f };
                f.AddPrism(Vector3.zero, new Vector3(0, 0, foreL * 0.5f), 0.55f, 0.6f, 7, true, false, 13, 0.25f);
                f.AddPrism(new Vector3(0, 0, foreL * 0.5f), new Vector3(0, 0, foreL), 0.6f, 0.45f, 7, false, true, 14, 0.25f);
                for (int k = 0; k < 2; k++) f.AddPrism(new Vector3(0, 0.45f, 0.9f + k * 1.2f), new Vector3(0, 0.95f, 0.6f + k * 1.2f), 0.1f, 0.01f, 4, false, false, 30 + k, 0f);
                SaveMesh(f.Build("DemonForearm"), "DemonForearm");
                // shackle at the wrist
                var sh = new MeshBuilder { uvScale = 1f };
                sh.AddPrism(new Vector3(0, 0, foreL - 0.55f), new Vector3(0, 0, foreL - 0.15f), 0.66f, 0.66f, 8, false, false, 15, 0.05f);
                SaveMesh(sh.Build("DemonShackle"), "DemonShackle");
                // palm: flat in XZ, palm down (-Y), fingers toward +Z
                var palm = new MeshBuilder { uvScale = 0.8f };
                ProceduralMeshes.Box(palm, new Vector3(0f, 0.05f, 0.25f), new Vector3(1.45f, 0.55f, 1.3f), 16, 0.12f);
                SaveMesh(palm.Build("DemonPalm"), "DemonPalm");
                var seg = new MeshBuilder { uvScale = 1f };
                seg.AddPrism(Vector3.zero, new Vector3(0, 0, 0.62f), 0.17f, 0.15f, 6, true, true, 17, 0.1f);
                SaveMesh(seg.Build("DemonFingerSeg"), "DemonFingerSeg");
                var claw = new MeshBuilder { uvScale = 1f };
                claw.AddPrism(Vector3.zero, new Vector3(0, -0.18f, 0.72f), 0.14f, 0.01f, 5, true, false, 18, 0f);
                SaveMesh(claw.Build("DemonClaw"), "DemonClawMesh");
            }
            MirrorVisuals.Arm BuildArm(string side, float sx)
            {
                var arm = new MirrorVisuals.Arm { upperLength = upperL, foreLength = foreL, shoulder = new Vector3(2.75f, 3.8f, 0.2f) };
                var up = new GameObject("Upper" + side).transform; up.SetParent(root.transform, false);
                Place("Mesh", "DemonUpperArm", "DemonArm", Vector3.zero, Quaternion.identity, up, false).transform.localPosition = Vector3.zero;
                var fo = new GameObject("Fore" + side).transform; fo.SetParent(root.transform, false);
                Place("Mesh", "DemonForearm", "DemonArm", Vector3.zero, Quaternion.identity, fo, false).transform.localPosition = Vector3.zero;
                Place("Shackle", "DemonShackle", "Iron", Vector3.zero, Quaternion.identity, fo, false).transform.localPosition = Vector3.zero;
                // a broken chain dangling from the shackle
                string cm = "ShackleChain_" + side;
                SaveMesh(CastleKit.Chain(_seed++, 1.8f), cm);
                var ch = Place("Chain", cm, "Iron", Vector3.zero, Quaternion.identity, fo, false);
                ch.transform.localPosition = new Vector3(0f, -0.5f, foreL - 0.35f);
                var hand = new GameObject("Hand" + side).transform; hand.SetParent(root.transform, false);
                Place("Palm", "DemonPalm", "DemonArm", Vector3.zero, Quaternion.identity, hand, false).transform.localPosition = Vector3.zero;
                var fingers = new List<Transform>();
                for (int k = 0; k < 5; k++)
                {
                    bool thumb = k == 4;
                    var fr0 = new GameObject(thumb ? "Thumb" : "Finger" + k).transform; fr0.SetParent(hand, false);
                    // four fingers across the front edge, the thumb on the inner side angled forward
                    fr0.localPosition = thumb ? new Vector3(-sx * 0.75f, 0f, 0.2f) : new Vector3((k - 1.5f) * 0.36f, 0f, 0.85f);
                    fr0.localRotation = thumb ? Quaternion.Euler(10f, -sx * 50f, 0f) : Quaternion.Euler(8f, (k - 1.5f) * 6f, 0f);
                    var sg = Place("Seg", "DemonFingerSeg", "DemonArm", Vector3.zero, Quaternion.identity, fr0, false);
                    sg.transform.localPosition = Vector3.zero; sg.transform.localRotation = Quaternion.identity;
                    var j1 = new GameObject("Claw").transform; j1.SetParent(fr0, false); j1.localPosition = new Vector3(0, 0, 0.6f);
                    var cl = Place("ClawMesh", "DemonClawMesh", "DemonClaw", Vector3.zero, Quaternion.identity, j1, false);
                    cl.transform.localPosition = Vector3.zero; cl.transform.localRotation = Quaternion.identity;
                    fingers.Add(fr0);
                }
                arm.upper = up; arm.fore = fo; arm.hand = hand; arm.fingers = fingers.ToArray();
                // hurtboxes: the arms and hands can be struck
                void Hurt(Transform t, Vector3 c, float r, float len)
                {
                    var go = new GameObject("Hurtbox"); go.transform.SetParent(t, false); go.transform.localPosition = c;
                    var cap = go.AddComponent<CapsuleCollider>(); cap.isTrigger = true; cap.direction = 2; cap.radius = r; cap.height = len;
                }
                Hurt(up, new Vector3(0, 0, upperL * 0.5f), 0.75f, upperL + 0.4f);
                Hurt(fo, new Vector3(0, 0, foreL * 0.5f), 0.65f, foreL + 0.4f);
                Hurt(hand, new Vector3(0, 0, 0.5f), 0.9f, 2f);
                foreach (var r in new[] { up, fo, hand }) foreach (var mr in r.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.On;
                return arm;
            }
            vis.left = BuildArm("L", -1f);
            vis.right = BuildArm("R", 1f);

            // the frame itself is struck through a box hurtbox following the body
            var fh = new GameObject("FrameHurtbox"); fh.transform.SetParent(body, false); fh.transform.localPosition = new Vector3(0f, 3.8f, 0f);
            var fb = fh.AddComponent<BoxCollider>(); fb.isTrigger = true; fb.size = new Vector3(5.6f, 6.6f, 1.6f);

            var lot = root.AddComponent<LockOnTarget>();
            var lp = new GameObject("LockPoint").transform; lp.SetParent(body, false); lp.localPosition = new Vector3(0f, ai.glassHeight, 0.4f);
            lot.lockPoint = lp; lot.framingSize = 3.2f; lot.enabled = false;
            return SavePrefab(root, "ShatteredMirror");
        }

        // ================================================================== wraith / revenant / arena / radiant
        static GameObject BuildWraith()
        {
            var root = new GameObject("ShardWraith");
            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.5f; cc.height = 2.2f; cc.center = new Vector3(0f, 1.15f, 0f);
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), false);
            var ai = root.AddComponent<ShardWraithAI>();
            ai.displayName = "Shard Wraith";
            ai.baseHealth = 75f; ai.basePoise = 22f;
            ai.walkSpeed = 2.4f; ai.chaseSpeed = 4.2f; ai.turnSpeed = 260f;
            ai.staggerDuration = 1.2f;
            var body = new GameObject("Body").transform; body.SetParent(root.transform, false);
            // hooded robe that frays into nothing: a hollow cone, a hood, two long arms ending in glass claws
            var mb = new MeshBuilder { uvScale = 1f };
            mb.AddPrism(new Vector3(0, -0.4f, 0), new Vector3(0, 1.3f, 0), 0.08f, 0.42f, 8, false, false, 61, 0.35f);   // tattered skirt (narrow at the bottom)
            mb.AddPrism(new Vector3(0, 1.3f, 0), new Vector3(0, 1.75f, 0), 0.42f, 0.34f, 8, false, false, 62, 0.15f);
            mb.AddPrism(new Vector3(0, 1.75f, -0.05f), new Vector3(0, 2.3f, -0.12f), 0.36f, 0.06f, 8, false, true, 63, 0.1f);  // hood
            foreach (int s in new[] { -1, 1 })
            {
                mb.AddPrism(new Vector3(s * 0.32f, 1.6f, 0), new Vector3(s * 0.6f, 1.0f, 0.45f), 0.1f, 0.07f, 5, true, false, 64, 0.1f);
                mb.AddPrism(new Vector3(s * 0.6f, 1.0f, 0.45f), new Vector3(s * 0.55f, 0.7f, 1.0f), 0.07f, 0.05f, 5, false, true, 65, 0.1f);
            }
            SaveMesh(mb.Build("WraithBody"), "WraithBody");
            var bgo = Place("Robe", "WraithBody", "WraithRobe", Vector3.zero, Quaternion.identity, body, false);
            bgo.transform.localPosition = Vector3.zero;
            // face: two cold eyes in the hood's darkness
            var em = new MeshBuilder();
            foreach (int s in new[] { -1, 1 }) { Vector3 c = new Vector3(s * 0.09f, 1.95f, 0.3f); float h = 0.04f; em.AddQuad(c + new Vector3(-h, -h, 0), c + new Vector3(h, -h, 0), c + new Vector3(h, h, 0), c + new Vector3(-h, h, 0), Vector3.forward); }
            SaveMesh(em.Build("WraithEyes"), "WraithEyes");
            Place("Eyes", "WraithEyes", "MirrorEyes", Vector3.zero, Quaternion.identity, body, false, 1f, false).transform.localPosition = Vector3.zero;
            // claws of glass
            foreach (int s in new[] { -1, 1 })
                for (int k = 0; k < 3; k++)
                {
                    var c = Place("Claw", "ShardSmall", "GlassShard", Vector3.zero, Quaternion.identity, body, false, 1f, false);
                    c.transform.localPosition = new Vector3(s * 0.55f + (k - 1) * 0.06f, 0.7f, 1.0f);
                    c.transform.localRotation = Quaternion.Euler(30f + k * 10f, s * 10f, 0f);
                    c.transform.localScale = Vector3.one * 1.3f;
                }
            var shards = new List<Transform>();
            for (int i = 0; i < 6; i++)
            {
                var s = Place("OrbitShard", "ShardSmall", "GlassShardGlow", Vector3.zero, Quaternion.identity, body, false, 1f, false);
                s.transform.localScale = Vector3.one * (0.9f + (i % 3) * 0.3f);
                shards.Add(s.transform);
            }
            var lg = new GameObject("Glow"); lg.transform.SetParent(body, false); lg.transform.localPosition = new Vector3(0f, 1.8f, 0.4f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = MirrorFx.Void; l.range = 6f; l.intensity = 1.5f; l.shadows = LightShadows.None;
            ai.body = body; ai.shards = shards.ToArray(); ai.glowRenderer = bgo.GetComponent<MeshRenderer>(); ai.glowLight = l;
            var hb = new GameObject("Hurtbox"); hb.transform.SetParent(body, false); hb.transform.localPosition = new Vector3(0, 1.3f, 0);
            var cap = hb.AddComponent<CapsuleCollider>(); cap.isTrigger = true; cap.radius = 0.55f; cap.height = 2.2f;
            var lot = root.AddComponent<LockOnTarget>(); var lp = new GameObject("LockPoint").transform; lp.SetParent(body, false); lp.localPosition = new Vector3(0, 1.6f, 0);
            lot.lockPoint = lp;
            return SavePrefab(root, "ShardWraith");
        }

        static GameObject BuildRevenant(AnimatorController ctrl)
        {
            var prefab = BuildKnightEnemy("GlassRevenant", "Glass Revenant", M["RevenantGlass"], 2.05f, ctrl, 190f, 60f, 1.1f);
            // shards circling the knight (opened as prefab contents, then saved back)
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            var orbit = new GameObject("GlassOrbit"); orbit.transform.SetParent(root.transform, false);
            var os = orbit.AddComponent<OrbitShards>();
            var list = new List<Transform>();
            for (int i = 0; i < 7; i++)
            {
                var s = Place("Shard", "ShardSmall", i % 2 == 0 ? "GlassShardGlow" : "GlassShard", Vector3.zero, Quaternion.identity, orbit.transform, false, 1f, false);
                s.transform.localScale = Vector3.one * (1.1f + (i % 3) * 0.35f);
                list.Add(s.transform);
            }
            os.shards = list.ToArray(); os.radius = 1.0f; os.height = 1.0f; os.speed = 1.3f;
            var lg = new GameObject("Glow"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 1.6f, 0.3f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.6f, 0.45f, 1f); l.range = 5f; l.intensity = 1.2f; l.shadows = LightShadows.None;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static GameObject BuildMirrorArena()
        {
            var root = new GameObject("MirrorArena");
            root.AddComponent<NetworkObject>();
            var arena = root.AddComponent<MirrorArena>();
            var fog = new GameObject("FogGate"); fog.transform.SetParent(root.transform, false);
            fog.AddComponent<MeshFilter>().sharedMesh = Meshes["FogSheet"];
            var mr = fog.AddComponent<MeshRenderer>(); mr.sharedMaterial = M["FogGate"]; mr.shadowCastingMode = ShadowCastingMode.Off;
            fog.AddComponent<UVScroll>().speed = new Vector2(0.03f, 0.06f);
            var colGo = new GameObject("Barrier"); colGo.transform.SetParent(root.transform, false);
            var box = colGo.AddComponent<BoxCollider>(); box.center = new Vector3(0, 3.6f, 0.1f); box.size = new Vector3(5f, 7.4f, 0.6f);
            var lightGo = new GameObject("FogLight"); lightGo.transform.SetParent(root.transform, false); lightGo.transform.localPosition = new Vector3(0, 3f, -1.2f);
            var l = lightGo.AddComponent<Light>(); l.type = LightType.Point; l.range = 8f; l.intensity = 1.5f; l.color = new Color(0.65f, 0.5f, 1f);
            arena.fogRenderer = mr; arena.fogCollider = box; arena.fogLight = l;
            return SavePrefab(root, "MirrorArena");
        }

        /// <summary>White-gold sparkles that ride a radiant weapon, plus a soft light.</summary>
        static GameObject RadiantSparkles(Transform parent, Vector3 localPos, Vector3 box, float rate, float lightRange)
        {
            var go = new GameObject("RadiantFx");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 200;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(1f, 0.85f, 0.5f, 1f));
            main.gravityModifier = -0.03f;
            var em = ps.emission; em.rateOverTime = rate; em.rateOverDistance = rate * 0.5f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0f), new Keyframe(0.2f, 1.4f), new Keyframe(0.35f, 0.5f), new Keyframe(0.55f, 1.1f), new Keyframe(1, 0f)));
            go.GetComponent<ParticleSystemRenderer>().material = M["ParticleAdd"];
            ps.Play();
            var lg = new GameObject("Light"); lg.transform.SetParent(go.transform, false);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.92f, 0.75f); l.range = lightRange; l.intensity = 1.6f; l.shadows = LightShadows.None;
            go.SetActive(false);
            return go;
        }

        static void Radiantize(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh && (mf.sharedMesh.name == "Dawnbreaker" || mf.sharedMesh.name == "Starwoven" || mf.sharedMesh.name == "ScytheRadiant")) return;   // already radiant
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] ? mats[i].name : "";
                bool gold = n.Contains("Gold") || n.Contains("Crimson") || n.Contains("Ember") || n.Contains("Glow") || n.Contains("Rune") || n.Contains("Bone");
                mats[i] = gold ? M["RadiantGold"] : M["RadiantSilver"];
            }
            r.sharedMaterials = mats;
        }

        /// <summary>Adds the radiant weapon (a polished silver-and-gold twin of the relic) to each class prefab.</summary>
        static void AddRadiantWeapons(Prefabs p)
        {
            void Edit(GameObject prefab, System.Action<GameObject, PlayerController> edit)
            {
                if (prefab == null) return;
                string path = AssetDatabase.GetAssetPath(prefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                var pc = root.GetComponent<PlayerController>();
                if (pc && pc.relicWeapon) edit(root, pc);
                else Debug.LogWarning("[DF] Radiant weapon not fitted on " + prefab.name);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            GameObject Twin(PlayerController pc, string name)
            {
                var src = pc.relicWeapon;
                var go = Object.Instantiate(src, src.transform.parent, false);
                go.name = name;
                go.transform.SetLocalPositionAndRotation(src.transform.localPosition, src.transform.localRotation);
                go.transform.localScale = src.transform.localScale;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Radiantize(r);
                go.SetActive(false);
                pc.radiantWeapon = go;
                return go;
            }
            GameObject Fresh(PlayerController pc, string mesh)
            {
                var src = pc.relicWeapon;
                var go = RelicObject(mesh, mesh, src.transform.parent);
                go.transform.SetLocalPositionAndRotation(src.transform.localPosition, src.transform.localRotation);
                go.transform.localScale = src.transform.localScale;
                go.SetActive(false);
                pc.radiantWeapon = go;
                return go;
            }
            Edit(p.player, (root, pc) =>
            {
                var w = Fresh(pc, "Dawnbreaker");
                pc.radiantFx = RadiantSparkles(w.transform.parent, new Vector3(0f, 0.95f, 0f), new Vector3(0.03f, 1.4f, 0.16f), 26f, 3.5f);
            });
            Edit(p.archer, (root, pc) =>
            {
                var w = Meshes.ContainsKey("Starwoven") ? Fresh(pc, "Starwoven") : Twin(pc, "Starwoven");
                var fx = RadiantSparkles(w.transform, Vector3.zero, new Vector3(0.04f, 1.4f, 0.25f), 20f, 3f);
                fx.transform.SetParent(w.transform.parent, true);
                pc.radiantFx = fx;
            });
            Edit(p.smoker, (root, pc) =>
            {
                var tw = Twin(pc, "PipeRadiant");
                var bowl = FindDeep(root.transform, "Bowl");
                if (bowl) pc.radiantFx = RadiantSparkles(bowl, Vector3.zero, new Vector3(0.12f, 0.12f, 0.12f), 14f, 2.6f);
            });
            Edit(p.bard, (root, pc) =>
            {
                var tw = Twin(pc, "BardStaffRadiant");
                var tip = FindDeep(root.transform, "StaffTip");
                if (tip && Meshes.ContainsKey("BardRadiantCrown"))
                {
                    // a crown of light on the head of the staff, aligned with the staff's axis
                    var rends = tw.GetComponentsInChildren<Renderer>(true);
                    Vector3 c = rends.Length > 0 ? rends[0].bounds.center : tw.transform.position;
                    Vector3 axis = (tip.position - c).sqrMagnitude > 1e-4f ? (tip.position - c).normalized : Vector3.up;
                    var crown = RelicObject("BardRadiantCrown", "RadiantCrown", null);
                    crown.transform.SetPositionAndRotation(tip.position, Quaternion.FromToRotation(Vector3.up, axis));
                    crown.transform.SetParent(tw.transform, true);
                }
                pc.radiantFx = RadiantSparkles(tip ? tip : root.transform, Vector3.zero, new Vector3(0.14f, 0.14f, 0.22f), 20f, 2.8f);
            });
        }

        static GameObject BuildRadiantDisplay(string mesh, string prefabName)
        {
            var root = new GameObject(prefabName);
            var vis = RelicObject(mesh, "Mesh", root.transform);
            var r = vis.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            Radiantize(r);
            var b = Meshes[mesh].bounds;
            vis.transform.localPosition = -b.center;
            var lg = new GameObject("Halo"); lg.transform.SetParent(root.transform, false);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.92f, 0.72f); l.range = 4.5f; l.intensity = 0f; l.shadows = LightShadows.None;
            var d = root.AddComponent<RelicDisplay>(); d.halo = l;
            RadiantSparkles(root.transform, Vector3.zero, new Vector3(0.3f, 1.2f, 0.3f), 18f, 2f).SetActive(true);
            return SavePrefab(root, prefabName);
        }

        static GameObject BuildRadiantChest()
        {
            if (!Meshes.ContainsKey("RewardChest")) return null;
            var root = new GameObject("RadiantChest");
            root.AddComponent<NetworkObject>();
            var rc = root.AddComponent<RewardChest>();
            rc.tier = Inventory.Radiant;
            var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
            RelicObject("RewardChest", "Body", visual);
            var pivot = new GameObject("LidPivot").transform; pivot.SetParent(visual, false);
            var h = RelicData["RewardChestLid"].hinge;
            pivot.localPosition = new Vector3(h[0], h[1], h[2]);
            RelicObject("RewardChestLid", "Lid", pivot);
            foreach (var r in visual.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = mats[i] == M["RelicGold"] ? M["RadiantGold"] : mats[i] == M["RelicDark"] || mats[i] == M["Steel"] ? M["RadiantSilver"] : mats[i];
                r.sharedMaterials = mats;
            }
            var lg = new GameObject("Glow"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0.75f, 0.1f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 9f; l.intensity = 0f; l.shadows = LightShadows.None; l.color = new Color(1f, 0.92f, 0.72f);
            var box = root.AddComponent<BoxCollider>(); box.center = new Vector3(0, 0.3f, 0); box.size = new Vector3(1.12f, 0.62f, 0.72f);
            rc.lidPivot = pivot; rc.glowLight = l;
            RadiantSparkles(root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.1f, 0.4f, 0.7f), 26f, 5f).SetActive(true);
            rc.swordDisplay = BuildRadiantDisplay("Dawnbreaker", "RadiantDisplaySword");
            rc.bowDisplay = BuildRadiantDisplay(Meshes.ContainsKey("Starwoven") ? "Starwoven" : "Thornwraith", "RadiantDisplayBow");
            if (Meshes.ContainsKey("StaffRelicDisplay")) rc.staffDisplay = BuildRadiantDisplay("StaffRelicDisplay", "RadiantDisplayStaff");
            if (Meshes.ContainsKey("ScytheRadiant")) rc.scytheDisplay = BuildRadiantDisplay("ScytheRadiant", "RadiantDisplayScythe");
            return SavePrefab(root, "RadiantChest");
        }

        static void BuildMirrorPrefabs(Prefabs p, AnimatorController ctrl)
        {
            ShardMesh("ShardSmall", 0.55f, 0.07f);
            ShardMesh("ShardFx", 0.5f, 0.06f);
            p.mirror = BuildMirrorBoss();
            p.mirrorArena = BuildMirrorArena();
            p.wraith = BuildWraith();
            p.revenant = BuildRevenant(ctrl);
            p.radiantChest = BuildRadiantChest();
            AddRadiantWeapons(p);
        }

        // ================================================================== small props
        static GameObject Flask(Transform parent, Vector3 pos)
        {
            if (!Meshes.ContainsKey("EmberFlaskMesh"))
            {
                var mb = new MeshBuilder { uvScale = 2f };
                mb.AddPrism(Vector3.zero, new Vector3(0, 0.18f, 0), 0.09f, 0.1f, 8, true, false, 1, 0f);
                mb.AddPrism(new Vector3(0, 0.18f, 0), new Vector3(0, 0.26f, 0), 0.1f, 0.035f, 8, false, false, 2, 0f);
                mb.AddPrism(new Vector3(0, 0.26f, 0), new Vector3(0, 0.33f, 0), 0.035f, 0.035f, 6, false, true, 3, 0f);
                SaveMesh(mb.Build("EmberFlaskMesh"), "EmberFlaskMesh");
            }
            var root = new GameObject("EmberFlask"); root.transform.SetParent(parent, false); root.transform.position = pos;
            var vis = Place("Visual", "EmberFlaskMesh", "EmberFlask", pos, Quaternion.identity, root.transform, false, 1.4f, false);
            GameObjectUtility.SetStaticEditorFlags(vis, 0);
            var lg = new GameObject("Glow"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0.35f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.2f); l.range = 3.5f; l.intensity = 2.2f; l.shadows = LightShadows.None;
            var fp = root.AddComponent<EmberFlaskPickup>(); fp.visual = vis.transform; fp.glow = l;
            Dynamic(root);
            return root;
        }

        /// <summary>Clears the static flags (the object moves: sway, levitation).</summary>
        static void Dynamic(GameObject go) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); }

        static LoreNpc Lore(GameObject go, string speaker, string verb, float dist, LoreNpc.Flourish fl, params string[] pages)
        {
            var n = go.AddComponent<LoreNpc>();
            n.speaker = speaker; n.promptVerb = verb; n.interactDistance = dist; n.flourish = fl; n.pages = pages;
            return n;
        }

        /// <summary>A hunched, hooded figure sitting against the wall, knees drawn up (Oswin).</summary>
        static GameObject Prisoner(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("Oswin"); root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Yaw(yaw));
            var cloth = new MeshBuilder { uvScale = 1.5f };
            cloth.AddPrism(new Vector3(0, 0.05f, -0.2f), new Vector3(0, 0.75f, -0.15f), 0.36f, 0.3f, 8, true, false, 1, 0.2f);   // torso, leaning back on the wall
            cloth.AddPrism(new Vector3(0, 0.75f, -0.15f), new Vector3(0, 1.0f, -0.08f), 0.3f, 0.2f, 8, false, false, 2, 0.1f);
            cloth.AddPrism(new Vector3(0, 0.95f, -0.12f), new Vector3(0, 1.38f, -0.05f), 0.25f, 0.05f, 8, false, true, 3, 0.1f);   // hood
            foreach (int s in new[] { -1, 1 })
            {
                cloth.AddPrism(new Vector3(s * 0.16f, 0.15f, 0f), new Vector3(s * 0.16f, 0.55f, 0.35f), 0.12f, 0.1f, 6, true, false, 4, 0.1f);    // thighs up
                cloth.AddPrism(new Vector3(s * 0.16f, 0.55f, 0.35f), new Vector3(s * 0.15f, 0.08f, 0.5f), 0.1f, 0.08f, 6, false, true, 5, 0.1f);  // shins down
                cloth.AddPrism(new Vector3(s * 0.28f, 0.85f, -0.05f), new Vector3(s * 0.2f, 0.55f, 0.32f), 0.08f, 0.07f, 6, true, false, 6, 0.1f); // arms around the knees
            }
            SaveMesh(cloth.Build("OswinBody"), "OswinBody");
            Place("Body", "OswinBody", "OswinCloth", pos, root.transform.rotation, root.transform, false);
            var face = new MeshBuilder { uvScale = 2f };
            face.AddPrism(new Vector3(0, 1.0f, -0.02f), new Vector3(0, 1.2f, 0.0f), 0.12f, 0.11f, 7, true, true, 7, 0.1f);
            foreach (int s in new[] { -1, 1 }) face.AddPrism(new Vector3(s * 0.12f, 0.55f, 0.36f), new Vector3(s * 0.1f, 0.55f, 0.46f), 0.05f, 0.05f, 6, true, true, 8, 0.1f);
            SaveMesh(face.Build("OswinSkin"), "OswinSkinMesh");
            Place("Skin", "OswinSkinMesh", "OswinSkin", pos, root.transform.rotation, root.transform, false);
            var cand = new GameObject("Candle"); cand.transform.SetParent(root.transform, false); cand.transform.localPosition = new Vector3(0.6f, 0.15f, 0.55f);
            Fx.CreateFlame(cand.transform, Vector3.zero, 0.22f, M["ParticleAdd"]);
            var cl = cand.AddComponent<Light>(); cl.type = LightType.Point; cl.color = TorchColor; cl.intensity = 2.6f; cl.range = 4.5f; cl.shadows = LightShadows.None;
            cand.AddComponent<FlickerLight>();
            return root;
        }

        static GameObject Duck(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("WoodenDuck"); root.transform.SetParent(parent, false); root.transform.SetPositionAndRotation(pos, Yaw(yaw));
            var b = new MeshBuilder { uvScale = 3f };
            b.AddPrism(new Vector3(0, 0.08f, -0.16f), new Vector3(0, 0.1f, 0.14f), 0.11f, 0.09f, 8, true, true, 1, 0.05f);
            b.AddPrism(new Vector3(0, 0.12f, -0.16f), new Vector3(0, 0.2f, -0.22f), 0.05f, 0.01f, 5, false, true, 2, 0f);   // tail
            b.AddPrism(new Vector3(0, 0.14f, 0.1f), new Vector3(0, 0.27f, 0.13f), 0.05f, 0.06f, 7, false, true, 3, 0f);     // neck + head
            SaveMesh(b.Build("DuckBody"), "DuckBody");
            var k = new MeshBuilder();
            k.AddPrism(new Vector3(0, 0.25f, 0.17f), new Vector3(0, 0.24f, 0.25f), 0.025f, 0.01f, 5, true, false, 4, 0f);
            SaveMesh(k.Build("DuckBeakMesh"), "DuckBeakMesh");
            Place("Body", "DuckBody", "Duck", pos, root.transform.rotation, root.transform, false, 1.4f);
            Place("Beak", "DuckBeakMesh", "DuckBeak", pos, root.transform.rotation, root.transform, false, 1.4f);
            root.AddComponent<Sway>().amplitude = 6f;
            Dynamic(root);
            return root;
        }

        static GameObject Ghost(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("LastMinstrel"); root.transform.SetParent(parent, false); root.transform.SetPositionAndRotation(pos, Yaw(yaw));
            var mb = new MeshBuilder { uvScale = 1f };
            mb.AddPrism(new Vector3(0, 0.0f, 0), new Vector3(0, 1.35f, 0), 0.05f, 0.36f, 8, false, false, 1, 0.3f);
            mb.AddPrism(new Vector3(0, 1.35f, 0), new Vector3(0, 1.62f, 0), 0.36f, 0.24f, 8, false, false, 2, 0.1f);
            mb.AddPrism(new Vector3(0, 1.6f, -0.04f), new Vector3(0, 2.0f, -0.1f), 0.24f, 0.05f, 8, false, true, 3, 0.1f);
            // a lute held across the chest
            mb.AddPrism(new Vector3(-0.25f, 1.05f, 0.3f), new Vector3(0.05f, 1.15f, 0.36f), 0.2f, 0.2f, 8, true, true, 4, 0f);
            mb.AddPrism(new Vector3(0.05f, 1.15f, 0.36f), new Vector3(0.55f, 1.45f, 0.32f), 0.04f, 0.03f, 5, false, true, 5, 0f);
            SaveMesh(mb.Build("MinstrelGhost"), "MinstrelGhost");
            Place("Ghost", "MinstrelGhost", "Ghost", pos, root.transform.rotation, root.transform, false, 1f, false);
            var lg = new GameObject("Glow"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 1.4f, 0.3f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.5f, 0.75f, 1f); l.range = 6f; l.intensity = 2.4f; l.shadows = LightShadows.None;
            lg.AddComponent<FlickerLight>();
            var lev = root.AddComponent<Levitate>(); lev.bob = 0.12f; lev.period = 3.5f; lev.spin = Vector3.zero; lev.wobble = 2f;
            Dynamic(root);
            return root;
        }

        static GameObject Tablet(Transform parent, Vector3 pos, float yaw, Color glow)
        {
            var go = Prop(parent, "Inscription", CastleKit.Crate(_seed++, new Vector3(1.1f, 1.5f, 0.22f)), "StoneCastle", pos, Yaw(yaw));
            var lg = new GameObject("RuneGlow"); lg.transform.SetParent(go.transform, false); lg.transform.localPosition = new Vector3(0, 1.0f, 0.6f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = glow; l.range = 3.5f; l.intensity = 1.6f; l.shadows = LightShadows.None;
            return go;
        }

        // ================================================================== prison secrets
        static void BuildPrisonSecrets(Transform root)
        {
            var sec = new GameObject("PrisonSecrets").transform; sec.SetParent(root, false);
            // ---- WEST: the forgotten vault, behind an illusory wall of the Ossuary (x -58..-49.6, z 78..86)
            var vault = new Room("ForgottenVault", -58f, -49.6f, 78f, 86f) { spring = 3.2f, apex = 4.6f, torchEvery = 0f, wallMat = "PrisonStone" };
            vault.E.Add(new Op(82f, 2.2f, 2.4f, 3.3f));
            BuildRoom(sec, vault);
            IllusionPanel(sec, new Vector3(-48.05f, 0.07f, 82f), 90f);
            _vaultChest = (new Vector3(-55.5f, 0.1f, 82f), 90f);
            Lore(Tablet(sec, new Vector3(-57.3f, 0.05f, 84.8f), 90f, new Color(1f, 0.9f, 0.65f)), "An inscription", "Read", 2.6f, LoreNpc.Flourish.Whisper,
                "HERE THE SILVERSMITHS OF CAER VAELMOOR HID THE LAST LIGHT OF THE OLD SUN.",
                "Three arms were forged from it, that no mirror might hold them: a blade that wakes the dawn, a bow strung with starlight, a citole that sings the seraphs' hymn.",
                "The light does not stay. Some nights the vault is empty; some nights it waits. Whoever finds it carries it out for all who walk beside them.");
            Flask(sec, new Vector3(-57f, 0.1f, 79.2f));
            var vc = new GameObject("VaultCandle"); vc.transform.SetParent(sec, false); vc.transform.position = new Vector3(-50.6f, 0.45f, 85.2f);
            Fx.CreateFlame(vc.transform, Vector3.zero, 0.25f, M["ParticleAdd"]);
            var vl = vc.AddComponent<Light>(); vl.type = LightType.Point; vl.color = new Color(1f, 0.85f, 0.6f); vl.intensity = 3f; vl.range = 7f; vl.shadows = LightShadows.None;
            vc.AddComponent<FlickerLight>();

            // ---- EAST: the minstrel's room behind the Barracks (x 49.6..58, z 78..86)
            var mins = new Room("MinstrelsRoom", 49.6f, 58f, 78f, 86f) { spring = 3.2f, apex = 4.6f, torchEvery = 0f, wallMat = "PrisonStone" };
            mins.W.Add(new Op(82f, 2.2f, 2.4f, 3.3f));
            BuildRoom(sec, mins);
            IllusionPanel(sec, new Vector3(48.05f, 0.07f, 82f), -90f);
            var ghost = Ghost(sec, new Vector3(55.5f, 0.1f, 82.4f), -90f);
            Lore(ghost, "The Last Minstrel", "Listen to", 3.2f, LoreNpc.Flourish.Tune,
                "Oh! A living ear. Forgive me, I have played to these stones for ninety winters and they never once applauded.",
                "I was the Queen's own minstrel. When Ysolde died, Aldric hung her mirror in the throne hall and asked it to show her to him. It showed him only what he lacked.",
                "So he fed it. A reflection here, a reflection there. Prisoners first: who would miss the face of a thief? Then the guards. Then the court. Then me.",
                "Without a reflection a man forgets his own face, then his name, then why he should stay dead. You have met the gaolers.",
                "If you find the Mirror, friend, strike when its hands rest on the stone. Glass is proud. It never guards its own heart.",
                "And bards: the Mirror hates music. It cannot reflect a song.");
            Flask(sec, new Vector3(56.6f, 0.1f, 79.2f));

            // ---- Oswin the Unreflected, in the closed cell W1N
            var os = Prisoner(sec, new Vector3(-23.5f, 0.07f, 126.6f), 180f);
            Lore(os, "Oswin the Unreflected", "Talk to", 5.6f, LoreNpc.Flourish.Whisper,
                "Don't look for my reflection in the puddle, stranger. It's not there. It hasn't been for forty years.",
                "This was Caer Vaelmoor once. Aldric was a good king, before the Ashen crown. His queen Ysolde brought a mirror from the glass-temples beyond the ravine. A wedding gift, they said.",
                "It shows you what you lack. Ysolde lacked nothing, so it showed her only herself, and she loved it. When the fever took her, the King stood before it day and night, and it showed him an empty chair.",
                "The Mirror offered a bargain: give it reflections, and it would give her back. Piece by piece. The prisoners went first. I was the last one before the gaolers stopped bringing anyone down.",
                "The dead don't rest here because the Mirror keeps their echoes. Break it and every stolen reflection goes home. Mine too, maybe.",
                "The wyrm on the terrace? Vaelthrax was Ysolde's guardian. It still waits for her. Kill it and the bridge behind it lowers: that way lies the Thornwood, where the trees grew from the shards the Mirror spat out.",
                "One more thing. The Ossuary's western wall. Walk into it. Some walls in this castle are only remembering that they were walls.");

            // ---- the duck (Drowned Chapel)
            var duck = Duck(sec, new Vector3(24.6f, 0.13f, 77.6f), 200f);
            Lore(duck, "A wooden duck", "Squeeze", 2.2f, LoreNpc.Flourish.Squeak,
                "Squeak.",
                "It has a reflection. A perfect one. The Mirror apparently had standards.");

            // ---- Ember Flasks scattered in the side rooms
            Flask(sec, new Vector3(-46f, 0.93f, 81.5f));    // on a sarcophagus (Ossuary)
            Flask(sec, new Vector3(20.6f, 1.18f, 70.7f));   // on the drowned altar
            Flask(sec, new Vector3(46.6f, 0.78f, 85.8f));   // on a bunk (Barracks)
            Flask(sec, new Vector3(-24.4f, 0.98f, 71f));    // torture table
        }

        /// <summary>Stones that are not there (IllusoryWall): covers the arch of a secret doorway.</summary>
        static void IllusionPanel(Transform parent, Vector3 pos, float yaw)
        {
            string mn = "Illusion_" + _seed++;
            SaveMesh(CastleKit.Wall(_seed++, 2.9f, 3.7f, 0.3f, new List<CastleKit.Opening>(), mn), mn);
            var go = Place("IllusoryWall", mn, "StoneCastle", pos, Yaw(yaw), parent, false);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            var iw = go.AddComponent<IllusoryWall>(); iw.wall = go.GetComponent<MeshRenderer>();
        }

        // ================================================================== beyond the bridge
        static void BuildBeyond(Transform parent)
        {
            _wraiths.Clear(); _revenants.Clear();
            var root = new GameObject("Beyond").transform; root.SetParent(parent, false);
            var rnd = new System.Random(4242);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            // ---- the bridge gate at the north end of the terrace (opens when the wyrm dies)
            SaveMesh(ProceduralMeshes.ArchFrame(330, 5.6f, 3.8f, 5.6f, 0.7f, 1f), "BridgeGateArch");
            Place("BridgeGateArch", "BridgeGateArch", "StoneCastle", new Vector3(0f, TerraceY, 206.3f), Quaternion.identity, root);
            Doorway(root, "BridgeGate", CastleProgress.Door.Bridge, new Vector3(0f, TerraceY + 0.09f, 206.1f), 2.7f, 3.8f, 5.6f, 95f, false);

            // a gentle ramp from the terrace floor up onto the bridge deck (0.3 m higher)
            var kr = new GameObject("KeepBridgeRamp"); kr.transform.SetParent(root, false);
            kr.transform.position = new Vector3(0f, (TerraceY + 0.09f + BridgeDeckY) * 0.5f - 0.1f, 206.2f);
            kr.transform.rotation = Quaternion.Euler(-Mathf.Atan2(BridgeDeckY - TerraceY - 0.09f, 3f) * Mathf.Rad2Deg, 0f, 0f);
            kr.AddComponent<BoxCollider>().size = new Vector3(6f, 0.2f, 3.1f);

            // ---- the Long Bridge (gate towers → stair tower), towers of the castle all around in the sunset
            const float G0 = -12f;
            Viaduct(root, "LongBridge", new Vector3(0f, G0, 239f), new Vector3(0f, G0, 337f), BridgeDeckY - 0.4f - G0, 6f, true);
            // invisible rails (the parapets are low)
            AddBox(root, new Vector3(-3.4f, BridgeDeckY + 1.5f, 288f), new Vector3(0.4f, 3f, 100f));
            AddBox(root, new Vector3(3.4f, BridgeDeckY + 1.5f, 288f), new Vector3(0.4f, 3f, 100f));
            AddBox(root, new Vector3(-3.4f, BridgeDeckY + 1.5f, 222.8f), new Vector3(0.4f, 3f, 32f));
            AddBox(root, new Vector3(3.4f, BridgeDeckY + 1.5f, 222.8f), new Vector3(0.4f, 3f, 32f));
            var city = new GameObject("BridgeSkyline").transform; city.SetParent(root, false);
            foreach (var (x, z, r, h) in new[] { (-34f, 262f, 4.5f, 48f), (38f, 280f, 5f, 56f), (-46f, 300f, 5.5f, 60f), (30f, 312f, 4f, 40f), (-26f, 326f, 3.5f, 36f), (52f, 330f, 6f, 70f), (-70f, 280f, 6f, 64f) })
                RoundTower(city, new Vector3(x, G0, z), r, h, h * 0.3f, 0.5f);
            Viaduct(city, "SkyBridgeW", new Vector3(-34f, G0, 262f), new Vector3(-46f, G0, 300f), 36f, 3f, false);
            Viaduct(city, "SkyBridgeE", new Vector3(38f, G0, 280f), new Vector3(52f, G0, 330f), 40f, 3f, false);
            for (int i = 0; i < 5; i++)
                foreach (int s in new[] { -1, 1 })
                {
                    var bn = Place("BridgeBanner", "Banner", "Banner", new Vector3(s * 3.3f, BridgeDeckY + 0.6f, 250f + i * 19f), Yaw(s > 0 ? -90 : 90), root, false, 0.8f, false);
                    var sw = bn.AddComponent<Sway>(); sw.amplitude = 7f; sw.period = 2.2f;
                }
            foreach (float z in new[] { 258f, 296f, 330f })
                foreach (int s in new[] { -1, 1 }) Brazier(root, new Vector3(s * 2.3f, BridgeDeckY, z), 14f, 10f);
            // cliff face where the courtyard ends and the ravine falls away
            var cliff = new MeshBuilder { uvScale = 0.15f };
            ProceduralMeshes.Box(cliff, new Vector3(0f, (G0 + ForestY) * 0.5f, 328f), new Vector3(440f, G0 - ForestY, 4f), 3, 0.2f);
            SaveMesh(cliff.Build("RavineCliff"), "RavineCliff");
            Place("RavineCliff", "RavineCliff", "StoneCastle", Vector3.zero, Quaternion.identity, root, true);

            BuildStairTower(root);
            BuildThornwood(root, R);
            BuildGlassTemple(root, R);
            BuildThornSideAreas(root, R);
        }

        static void BuildStairTower(Transform root)
        {
            var t = new GameObject("StairTower").transform; t.SetParent(root, false);
            Vector3 c = TowerC;
            const int sides = 12;
            const float rad = 6.5f, thick = 0.9f, top = 20f;
            float y0 = ForestY, yd = BridgeDeckY;
            // walls: a lower stack (forest floor → deck) and an upper stack (deck → top), 12 panels each
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                // angle 0 = north (+Z), clockwise seen from above
                Vector3 P(float a, float y) => c + new Vector3(Mathf.Sin(a) * (rad + thick * 0.5f), y, Mathf.Cos(a) * (rad + thick * 0.5f));
                float mid = (a0 + a1) * 0.5f;
                bool north = Mathf.Abs(Mathf.DeltaAngle(mid * Mathf.Rad2Deg, 0f)) < 16f;
                bool south = Mathf.Abs(Mathf.DeltaAngle(mid * Mathf.Rad2Deg, 180f)) < 16f;
                float chord = Vector3.Distance(P(a0, 0), P(a1, 0));
                var lowOps = north ? new List<Op> { new Op(chord * 0.5f, 2.4f, 2.6f, 3.6f) } : new List<Op>();
                var upOps = south ? new List<Op> { new Op(chord * 0.5f, 2.6f, 2.8f, 3.9f) } : new List<Op>();
                // WallRun's start point is 'a': walls face outward from the centre when walking clockwise
                WallRun(t, $"TowerLow{i}", P(a1, y0), P(a0, y0), yd - y0, thick, lowOps, "StoneCastle");
                WallRun(t, $"TowerUp{i}", P(a1, yd), P(a0, yd), top - yd, thick, upOps, "StoneCastle");
            }
            // narrow windows of light
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 90f + 45f) * Mathf.Deg2Rad;
                var lg = new GameObject("Slit"); lg.transform.SetParent(t, false); lg.transform.position = c + new Vector3(Mathf.Sin(a) * (rad - 0.5f), -8f + k * 8f, Mathf.Cos(a) * (rad - 0.5f));
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.35f); l.intensity = 3f; l.range = 7f; l.shadows = LightShadows.None;
            }
            // roof cone and crenels
            var roof = new MeshBuilder { uvScale = 0.4f };
            roof.AddPrism(c + Vector3.up * top, c + Vector3.up * (top + 9f), rad + 1.2f, 0.05f, 12, true, false, 9, 0.02f);
            SaveMesh(roof.Build("StairTowerRoof"), "StairTowerRoof");
            Place("Roof", "StairTowerRoof", "RoofSlate", Vector3.zero, Quaternion.identity, t, false);
            // central column
            var col = new MeshBuilder { uvScale = 0.5f };
            col.AddPrism(c + Vector3.up * y0, c + Vector3.up * top, 1.1f, 1.1f, 10, false, false, 11, 0.02f);
            SaveMesh(col.Build("StairColumn"), "StairColumn");
            Place("Column", "StairColumn", "StoneCastle", Vector3.zero, Quaternion.identity, t, true);
            // floors: top landing (south half) and the bottom
            var floor = new MeshBuilder { uvScale = 0.5f };
            ProceduralMeshes.Box(floor, c + new Vector3(0f, y0 - 0.2f, 0f), new Vector3(rad * 2f, 0.4f, rad * 2f), 12, 0.02f);
            SaveMesh(floor.Build("StairTowerFloor"), "StairTowerFloor");
            Place("Floor", "StairTowerFloor", "Flagstones", Vector3.zero, Quaternion.identity, t, true);

            // ---- the spiral: from the south door at the deck (angle 180°) down 3.5 turns to the north door
            const float turns = 3.5f, r0 = 1.15f, r1 = rad - 0.05f;
            float startA = 180f, sweep = turns * 360f;
            float drop = yd - y0;
            int steps = Mathf.RoundToInt(sweep / 10f);
            var stepMb = new MeshBuilder { uvScale = 0.5f };
            var ramp = new MeshBuilder();
            float Y(float k) => yd - drop * Mathf.Clamp01(k);
            // landing at the top: a flat sector in front of the door (180° ± 25°) before the descent
            const float landing = 30f;
            for (int i = 0; i < steps; i++)
            {
                float k0 = i / (float)steps, k1 = (i + 1) / (float)steps;
                float a0 = (startA + landing + sweep * k0) * Mathf.Deg2Rad, a1 = (startA + landing + sweep * k1) * Mathf.Deg2Rad;
                float yTop = Y(k0) - 0.05f;
                Vector3 In0 = c + new Vector3(Mathf.Sin(a0) * r0, 0, Mathf.Cos(a0) * r0), Out0 = c + new Vector3(Mathf.Sin(a0) * r1, 0, Mathf.Cos(a0) * r1);
                Vector3 In1 = c + new Vector3(Mathf.Sin(a1) * r0, 0, Mathf.Cos(a1) * r0), Out1 = c + new Vector3(Mathf.Sin(a1) * r1, 0, Mathf.Cos(a1) * r1);
                // a wedge step (top + front + a thick underside)
                float yb = yTop - 0.45f;
                stepMb.color = Color.Lerp(new Color(0.78f, 0.78f, 0.78f), Color.white, (i % 3) / 2f);
                stepMb.AddQuad(In0 + Vector3.up * yTop, Out0 + Vector3.up * yTop, Out1 + Vector3.up * yTop, In1 + Vector3.up * yTop, Vector3.up);
                stepMb.AddQuad(In1 + Vector3.up * yb, Out1 + Vector3.up * yb, Out0 + Vector3.up * yb, In0 + Vector3.up * yb, Vector3.down);
                stepMb.AddQuad(In0 + Vector3.up * yb, Out0 + Vector3.up * yb, Out0 + Vector3.up * yTop, In0 + Vector3.up * yTop, Vector3.Cross(Vector3.up, Out0 - In0));
                stepMb.AddQuad(In1 + Vector3.up * yTop, Out1 + Vector3.up * yTop, Out1 + Vector3.up * yb, In1 + Vector3.up * yb, -Vector3.Cross(Vector3.up, Out1 - In1));
                stepMb.AddQuad(Out0 + Vector3.up * yb, Out1 + Vector3.up * yb, Out1 + Vector3.up * yTop, Out0 + Vector3.up * yTop, (Out0 + Out1) * 0.5f - c);
                // smooth helicoid for the character controllers
                float ry0 = Y(k0) - 0.12f, ry1 = Y(k1) - 0.12f;
                ramp.AddQuad(In0 + Vector3.up * ry0, Out0 + Vector3.up * ry0, Out1 + Vector3.up * ry1, In1 + Vector3.up * ry1, Vector3.up);
            }
            // top landing
            for (int i = 0; i < 6; i++)
            {
                float a0 = (startA - landing + i * (landing * 2f / 6f)) * Mathf.Deg2Rad, a1 = (startA - landing + (i + 1) * (landing * 2f / 6f)) * Mathf.Deg2Rad;
                Vector3 In0 = c + new Vector3(Mathf.Sin(a0) * r0, yd - 0.05f, Mathf.Cos(a0) * r0), Out0 = c + new Vector3(Mathf.Sin(a0) * r1, yd - 0.05f, Mathf.Cos(a0) * r1);
                Vector3 In1 = c + new Vector3(Mathf.Sin(a1) * r0, yd - 0.05f, Mathf.Cos(a1) * r0), Out1 = c + new Vector3(Mathf.Sin(a1) * r1, yd - 0.05f, Mathf.Cos(a1) * r1);
                stepMb.AddQuad(In0, Out0, Out1, In1, Vector3.up);
                stepMb.AddQuad(In1 + Vector3.down * 0.5f, Out1 + Vector3.down * 0.5f, Out0 + Vector3.down * 0.5f, In0 + Vector3.down * 0.5f, Vector3.down);
                ramp.AddQuad(In0 + Vector3.down * 0.07f, Out0 + Vector3.down * 0.07f, Out1 + Vector3.down * 0.07f, In1 + Vector3.down * 0.07f, Vector3.up);
            }
            SaveMesh(stepMb.Build("SpiralSteps"), "SpiralSteps");
            Place("SpiralSteps", "SpiralSteps", "StoneCastle", Vector3.zero, Quaternion.identity, t, false);
            SaveMesh(ramp.Build("SpiralRamp"), "SpiralRamp");
            var rg = new GameObject("SpiralRamp"); rg.transform.SetParent(t, false);
            rg.AddComponent<MeshCollider>().sharedMesh = Meshes["SpiralRamp"];
            // torches climbing down the outer wall
            for (int i = 0; i < 12; i++)
            {
                float k = (i + 0.5f) / 12f;
                float a = (startA + landing + sweep * k) * Mathf.Deg2Rad;
                Vector3 wall = c + new Vector3(Mathf.Sin(a) * (rad - 0.05f), Y(k) + 2.4f, Mathf.Cos(a) * (rad - 0.05f));
                Vector3 inw = c - wall; inw.y = 0f;
                Torch(t, wall, inw.normalized, 20f, 11f);
            }
            // checkpoint at the foot of the tower, outside the north door
            Checkpoint(t, 5, "The Thornwood", c + new Vector3(3f, y0 + 0.05f, rad + 4.5f), c + new Vector3(0f, y0 + 0.2f, rad + 4f), 0f);
            Checkpoint(t, 4, "The Long Bridge", new Vector3(2.1f, BridgeDeckY, 244.5f), new Vector3(0f, BridgeDeckY + 0.2f, 245f), 0f);
        }

        static void BuildThornwood(Transform root, System.Func<float, float, float> R)
        {
            var f = new GameObject("Thornwood").transform; f.SetParent(root, false);
            // ground: gently uneven, x -60..60, z 326..542
            float x0 = -62f, x1 = 62f, z0 = 326f, z1 = 544f, cell = 2f;
            float GH(float x, float z)
            {
                float h = (Noise.Fbm2(x * 0.06f, z * 0.06f, 3, 77) - 0.5f) * 1.4f;
                float path = Mathf.Abs(x - PathX(z));
                h *= Mathf.Clamp01(path / 6f);                                  // the path is level
                float dt = Vector2.Distance(new Vector2(x, z), new Vector2(TempleC.x, TempleC.z));
                if (dt < 30f) h *= Mathf.Clamp01((dt - 25f) / 5f);               // around the temple, flat
                return h;
            }
            var gmb = new MeshBuilder { uvScale = 0.25f };
            for (float x = x0; x < x1; x += cell)
                for (float z = z0; z < z1; z += cell)
                {
                    Vector3 a = new Vector3(x, ForestY + GH(x, z), z), b = new Vector3(x + cell, ForestY + GH(x + cell, z), z);
                    Vector3 cc = new Vector3(x + cell, ForestY + GH(x + cell, z + cell), z + cell), d = new Vector3(x, ForestY + GH(x, z + cell), z + cell);
                    gmb.AddQuad(a, d, cc, b, Vector3.up);
                }
            SaveMesh(gmb.Build("ThornGround"), "ThornGround");
            Place("Ground", "ThornGround", "ThornGround", Vector3.zero, Quaternion.identity, f, true, 1f, false);
            // bounds of the ravine
            // (two gaps: the barrow in the west wall, the glassblower's grotto in the east)
            void Bound(float x, float za, float zb) => AddBox(f, new Vector3(x, ForestY + 10f, (za + zb) * 0.5f), new Vector3(1f, 30f, zb - za));
            Bound(-44f, 325f, BarrowZ - 2.6f); Bound(-44f, BarrowZ + 2.6f, 545f);
            Bound(44f, 325f, GrottoZ - 2.6f); Bound(44f, GrottoZ + 2.6f, 545f);
            AddBox(f, new Vector3(0f, ForestY + 10f, 541f), new Vector3(90f, 30f, 1f));
            // ravine walls: tall dark rock faces just beyond the bounds
            for (float z = 330f; z < 545f; z += 9f)
                foreach (int s in new[] { -1, 1 })
                {
                    float rx = s * R(48f, 54f), rz = z + R(-2f, 2f);
                    if (Mathf.Abs(rz - (s < 0 ? BarrowZ : GrottoZ)) < 12f) continue;    // the side areas cut through here
                    Place("RavineRock", "Rock" + Mathf.Abs((int)z % 6), "StoneCastle", new Vector3(rx, ForestY - 2f, rz), Yaw(R(0, 360)), f, false, R(6f, 10f));
                }
            for (float x = -40f; x <= 40f; x += 9f)
                Place("RavineRock", "Rock" + Mathf.Abs((int)x % 6), "StoneCastle", new Vector3(x, ForestY - 2f, R(548f, 553f)), Yaw(R(0, 360)), f, false, R(6f, 10f));

            // twisted trees, away from the path and the temple
            var treeMeshes = new List<string>();
            for (int i = 0; i < 6; i++) treeMeshes.Add(TwistedTree(700 + i, 7f + i * 1.3f).name);
            int placed = 0;
            for (int tries = 0; tries < 400 && placed < 70; tries++)
            {
                float x = R(-42f, 42f), z = R(352f, 540f);
                if (Mathf.Abs(x - PathX(z)) < 5f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(TempleC.x, TempleC.z)) < 30f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(TowerC.x, TowerC.z)) < 13f) continue;
                if (Mathf.Abs(x) > 34f && Mathf.Abs(z - (x < 0f ? BarrowZ : GrottoZ)) < 6f) continue;   // keep the side-area mouths clear
                var tr = Place("TwistedTree", treeMeshes[placed % treeMeshes.Count], "ThornBark", new Vector3(x, ForestY + GH(x, z) - 0.2f, z), Yaw(R(0, 360)), f, false, R(0.8f, 1.35f));
                var cap = tr.AddComponent<CapsuleCollider>(); cap.radius = 0.5f; cap.height = 6f; cap.center = new Vector3(0, 3f, 0);
                placed++;
            }
            // glass shards stuck in the ground, some glowing faintly
            for (int i = 0; i < 4; i++) ShardMesh("GroundShard" + i, 1.6f + i * 0.7f, 0.22f + i * 0.06f, 3 + (i % 2), 810 + i);
            for (int i = 0; i < 90; i++)
            {
                float x = R(-41f, 41f), z = R(352f, 538f);
                if (Mathf.Abs(x - PathX(z)) < 1.6f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(TempleC.x, TempleC.z)) < 24f) continue;
                var rot = Quaternion.LookRotation(Vector3.up + new Vector3(R(-0.5f, 0.5f), 0f, R(-0.5f, 0.5f))) * Quaternion.Euler(0, 0, R(0, 360));
                var sh = Place("GlassShard", "GroundShard" + (i % 4), i % 5 == 0 ? "GlassShardGlow" : "GlassShard", new Vector3(x, ForestY + GH(x, z) - 0.15f, z), rot, f, false, R(0.7f, 1.6f), false);
                if (i % 9 == 0)
                {
                    var l = new GameObject("ShardLight"); l.transform.SetParent(f, false); l.transform.position = sh.transform.position + Vector3.up * 1.2f;
                    var li = l.AddComponent<Light>(); li.type = LightType.Point; li.color = new Color(0.6f, 0.45f, 1f); li.range = 7f; li.intensity = 2f; li.shadows = LightShadows.None;
                }
            }
            // the path: old flagstones, broken; lanterns of violet witch-fire along it
            for (float z = 352f; z < 482f; z += 2.3f)
            {
                if (Noise.Hash((int)z, 5) < 0.25f) continue;
                float x = PathX(z) + R(-0.8f, 0.8f);
                var fs = new MeshBuilder { uvScale = 0.6f };
                ProceduralMeshes.Box(fs, Vector3.zero, new Vector3(R(1.2f, 2f), 0.16f, R(1f, 1.6f)), (int)z, 0.04f);
                string mn = "PathStone_" + _seed++;
                SaveMesh(fs.Build(mn), mn);
                Place("PathStone", mn, "Flagstones", new Vector3(x, ForestY + 0.04f, z), Yaw(R(-20, 20)), f, false, 1f, false);
            }
            for (int i = 0; i < 8; i++)
            {
                float z = 365f + i * 15f;
                float x = PathX(z) + (i % 2 == 0 ? -2.6f : 2.6f);
                var lp = Prop(f, "WitchPost", CastleKit.Crate(_seed++, new Vector3(0.18f, 2.4f, 0.18f)), "ThornBark", new Vector3(x, ForestY, z), Yaw(R(0, 40)));
                var lg = new GameObject("WitchFire"); lg.transform.SetParent(lp.transform, false); lg.transform.localPosition = new Vector3(0, 2.5f, 0);
                var ps = Fx.CreateFlame(lg.transform, Vector3.zero, 0.5f, M["ParticleAdd"]);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.62f, 0.4f, 1f); l.range = 10f; l.intensity = 6f; l.shadows = LightShadows.None;
                lg.AddComponent<FlickerLight>();
                foreach (var psr in lg.GetComponentsInChildren<ParticleSystem>()) { var m = psr.main; m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.5f, 1f), new Color(0.4f, 0.2f, 0.9f)); }
            }
            // violet motes drifting in the dark
            Motes(f, new Vector3(0f, ForestY + 4f, 430f), new Vector3(80f, 8f, 180f), 70f, new Color(0.7f, 0.5f, 1f, 0.8f), new Color(0.4f, 0.8f, 1f, 0.6f));
            // fog banks
            for (int i = 0; i < 10; i++)
            {
                var fg = new GameObject("FogBank"); fg.transform.SetParent(f, false); fg.transform.position = new Vector3(R(-30f, 30f), ForestY + 0.6f, R(360f, 470f));
                var ps = fg.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var m = ps.main; m.loop = true; m.prewarm = true; m.startLifetime = 9f; m.startSpeed = 0.15f; m.startSize = new ParticleSystem.MinMaxCurve(6f, 10f);
                m.startColor = new Color(0.32f, 0.26f, 0.42f, 0.16f); m.maxParticles = 40; m.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = 2.5f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(14f, 0.5f, 14f);
                fg.GetComponent<ParticleSystemRenderer>().material = M["ParticleAlpha"];
            }

            // ---- inhabitants
            foreach (var (x, z) in new[] { (-6f, 372f), (8f, 392f), (-9f, 412f), (5f, 428f), (-4f, 446f), (10f, 458f), (-12f, 468f), (14f, 405f) })
                _wraiths.Add((new Vector3(x, ForestY + 0.2f, z), 180f));
            foreach (var (x, z) in new[] { (1.5f, 384f), (-2f, 420f), (2.5f, 452f), (-3f, 474f), (18f, 438f) })
                _revenants.Add((new Vector3(PathX(z) + x * 0.3f, ForestY + 0.2f, z), 180f));

            // ---- secrets of the wood: a hollow tree with a flask, and a shard that remembers
            Flask(f, new Vector3(-31f, ForestY + 0.3f, 398f));
            Place("HollowTree", treeMeshes[3], "ThornBark", new Vector3(-31.6f, ForestY - 0.3f, 399f), Yaw(40f), f, false, 1.6f);
            var mem = Place("SingingShard", "GroundShard3", "GlassShardGlow", new Vector3(27f, ForestY - 0.1f, 486f), Quaternion.Euler(-80f, 30f, 0f), f, false, 1.8f, false);
            var ml = new GameObject("Glow"); ml.transform.SetParent(mem.transform, false); ml.transform.position = mem.transform.position + Vector3.up * 2f;
            var mli = ml.AddComponent<Light>(); mli.type = LightType.Point; mli.color = new Color(0.55f, 0.85f, 1f); mli.range = 6f; mli.intensity = 3f; mli.shadows = LightShadows.None;
            Lore(mem, "A shard that remembers", "Look into", 3f, LoreNpc.Flourish.Whisper,
                "In the glass a woman in a silver crown is laughing at something you cannot see.",
                "Her lips move. \"Don't let it keep me, Aldric. Let me go.\"",
                "The reflection turns, looks straight at you, and is gone.");
            Flask(f, new Vector3(26f, ForestY + 0.2f, 484.5f));
        }

        static float PathX(float z) => Mathf.Sin((z - 350f) * 0.045f) * 9f * Mathf.Clamp01((480f - z) / 30f) * Mathf.Clamp01((z - 350f) / 8f);

        static void Motes(Transform parent, Vector3 c, Vector3 box, float rate, Color a, Color b)
        {
            var go = new GameObject("Motes"); go.transform.SetParent(parent, false); go.transform.position = c;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.maxParticles = 900; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.01f;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var no = ps.noise; no.enabled = true; no.strength = 0.3f; no.frequency = 0.25f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = M["ParticleAdd"];
        }

        static void BuildGlassTemple(Transform root, System.Func<float, float, float> R)
        {
            var t = new GameObject("GlassTemple").transform; t.SetParent(root, false);
            Vector3 c = TempleC;
            const float radius = 22f;
            // ---- the round floor of black glass on a low dais, steps down to the wood on the south
            var fl = new MeshBuilder { uvScale = 0.35f };
            const int segs = 32;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i / (float)segs * Mathf.PI * 2f, a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
                Vector3 o0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)), o1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                Vector3 C0 = c + Vector3.up * (TempleFloorY - ForestY);
                fl.AddTri(C0, C0 + o0 * radius, C0 + o1 * radius, Vector3.up);
                fl.AddQuad(C0 + o0 * radius, c + o0 * radius + Vector3.down * 0.4f, c + o1 * radius + Vector3.down * 0.4f, C0 + o1 * radius, o0 + o1);
            }
            SaveMesh(fl.Build("TempleFloor"), "TempleFloor");
            Place("Floor", "TempleFloor", "TempleFloor", Vector3.zero, Quaternion.identity, t, true, 1f, false);
            var stp = new MeshBuilder { uvScale = 0.5f };
            ProceduralMeshes.Box(stp, c + new Vector3(0f, (TempleFloorY - ForestY) * 0.5f - 0.3f, -radius - 1.2f), new Vector3(7f, TempleFloorY - ForestY + 0.6f, 3f), 5, 0.03f);
            SaveMesh(stp.Build("TempleSteps"), "TempleSteps");
            Place("Steps", "TempleSteps", "TempleStone", Vector3.zero, Quaternion.identity, t, false, 1f, true);
            var sr = new GameObject("StepRamp"); sr.transform.SetParent(t, false);
            sr.transform.position = c + new Vector3(0f, (TempleFloorY - ForestY) * 0.5f - 0.05f, -radius - 1.4f);
            sr.transform.rotation = Quaternion.Euler(-Mathf.Atan2(TempleFloorY - ForestY, 3.2f) * Mathf.Rad2Deg, 0f, 0f);
            sr.AddComponent<BoxCollider>().size = new Vector3(6.6f, 0.2f, 3.4f);

            // ---- the Hall of Reflections: a ring of tall arched mirrors facing in (a gap at the south for the
            //      fog gate), the same ring hanging upside down above (the reflection of the hall), light
            //      falling from an oculus and split by prisms into beams that cross between the mirrors
            float fy = TempleFloorY - ForestY;
            SaveMesh(ProceduralMeshes.ArchFrame(333, 3.4f, 5f, 9.2f, 0.42f, 0.8f), "MirrorFrame");
            SaveMesh(ProceduralMeshes.ArchPane(3.4f, 5f, 9.2f, 0f), "MirrorPane");
            {
                var plinth = new MeshBuilder { uvScale = 0.6f };
                ProceduralMeshes.Box(plinth, new Vector3(0f, 0.25f, 0f), new Vector3(4.6f, 0.5f, 1.4f), 334, 0.05f);
                plinth.AddPrism(new Vector3(-2.15f, 0.5f, 0f), new Vector3(-2.15f, 9.6f, 0f), 0.32f, 0.26f, 6, false, true, 335, 0f);
                plinth.AddPrism(new Vector3(2.15f, 0.5f, 0f), new Vector3(2.15f, 9.6f, 0f), 0.32f, 0.26f, 6, false, true, 336, 0f);
                SaveMesh(plinth.Build("MirrorPlinth"), "MirrorPlinth");
            }
            const int mirrors = 13;
            for (int i = 0; i < mirrors; i++)
            {
                float a = (i + 0.5f) / mirrors * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 180f)) < 15f) continue;
                Vector3 o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 p = c + o * (radius - 0.2f) + Vector3.up * fy;
                var rot = Quaternion.LookRotation(-o);
                bool cracked = i % 4 == 1;
                Place("MirrorPlinth", "MirrorPlinth", "TempleStone", p, rot, t, true);
                Place("MirrorFrame", "MirrorFrame", "TempleStone", p + Vector3.up * 0.5f, rot, t, false);
                if (!cracked) Place("Mirror", "MirrorPane", "MirrorSilver", p + Vector3.up * 0.5f + o * 0.05f, rot, t, false, 1f, false);
                else
                    for (int k = 0; k < 5; k++)   // a shattered mirror: a few shards still in the frame
                        Place("MirrorShard", "GroundShard" + (k % 4), "MirrorSilver", p + Vector3.up * (1.5f + k * 1.4f) + rot * new Vector3(R(-1f, 1f), 0f, 0f), Quaternion.Euler(R(-20, 20), R(0, 360), R(-30, 30)), t, false, R(0.6f, 1.1f), false);
                // the reflection: the same mirror hanging upside down in the dark above, slowly turning
                var up = Place("MirrorAbove", "MirrorFrame", "TempleStone", p + Vector3.up * 30f, Quaternion.Euler(180f, 0f, 0f) * Quaternion.LookRotation(o), t, false, 1f, false);
                GameObjectUtility.SetStaticEditorFlags(up, 0);
                var lev = up.AddComponent<Levitate>(); lev.bob = 0.5f; lev.period = R(7f, 11f); lev.spin = Vector3.zero; lev.wobble = 1.5f;
                var upPane = Place("MirrorAbovePane", "MirrorPane", "MirrorSilver", Vector3.zero, Quaternion.identity, up.transform, false, 1f, false);
                upPane.transform.localPosition = new Vector3(0f, 0f, 0.05f); upPane.transform.localRotation = Quaternion.identity;
                var ml = new GameObject("MirrorGlint"); ml.transform.SetParent(t, false); ml.transform.position = p + Vector3.up * 5f - o * 1.2f;
                var mli = ml.AddComponent<Light>(); mli.type = LightType.Point; mli.color = i % 2 == 0 ? new Color(0.65f, 0.55f, 1f) : new Color(0.5f, 0.85f, 1f); mli.range = 7f; mli.intensity = 2f; mli.shadows = LightShadows.None;
            }
            // the inlay: concentric rings of pale light in the black glass
            {
                var inl = new MeshBuilder();
                foreach (float rr in new[] { 6f, 12.5f, 18.5f })
                    for (int k = 0; k < 72; k++)
                    {
                        float a0 = k / 72f * Mathf.PI * 2f, a1 = (k + 1) / 72f * Mathf.PI * 2f;
                        Vector3 d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                        inl.AddQuad(d0 * (rr - 0.07f), d1 * (rr - 0.07f), d1 * (rr + 0.07f), d0 * (rr + 0.07f), Vector3.up);
                    }
                for (int k = 0; k < 13; k++)
                {
                    float a = (k + 0.5f) / 13f * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), sd = Vector3.Cross(Vector3.up, d) * 0.05f;
                    inl.AddQuad(d * 6f - sd, d * 6f + sd, d * 18.5f + sd, d * 18.5f - sd, Vector3.up);
                }
                SaveMesh(inl.Build("TempleInlay"), "TempleInlay");
                Place("Inlay", "TempleInlay", "GlassShardGlow", c + Vector3.up * (fy + 0.02f), Quaternion.identity, t, false, 1f, false);
            }
            // the oculus: a broken stone ring high above, and the shaft of pale light that falls through it
            {
                var oc = new MeshBuilder { uvScale = 0.4f };
                for (int k = 0; k < 24; k++)
                {
                    if (k == 7 || k == 8 || k == 17) continue;   // broken
                    float a0 = k / 24f * Mathf.PI * 2f, a1 = (k + 1) / 24f * Mathf.PI * 2f;
                    oc.AddPrism(new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * 7f, new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * 7f, 0.9f, 0.9f, 6, true, true, 337 + k, 0.12f);
                }
                SaveMesh(oc.Build("TempleOculus"), "TempleOculus");
                var ocg = Place("Oculus", "TempleOculus", "TempleStone", c + Vector3.up * 34f, Quaternion.identity, t, false, 1f, false);
                GameObjectUtility.SetStaticEditorFlags(ocg, 0);
                var olev = ocg.AddComponent<Levitate>(); olev.bob = 0.4f; olev.period = 12f; olev.spin = new Vector3(0f, 2f, 0f); olev.wobble = 1f;
                var shaft = new MeshBuilder();
                shaft.AddPrism(new Vector3(0f, 34f, 0f), new Vector3(0f, fy, 0f), 6.2f, 4.2f, 24, false, false, 360, 0f);
                SaveMesh(shaft.Build("TempleShaft"), "TempleShaft");
                Place("LightShaft", "TempleShaft", "LightBeam", c, Quaternion.identity, t, false, 1f, false);
                var sl = new GameObject("OculusLight"); sl.transform.SetParent(t, false); sl.transform.position = c + Vector3.up * 33f; sl.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var sli = sl.AddComponent<Light>(); sli.type = LightType.Spot; sli.spotAngle = 26f; sli.innerSpotAngle = 14f; sli.range = 50f; sli.intensity = 260f; sli.color = new Color(0.86f, 0.8f, 1f); sli.shadows = LightShadows.None;
                Motes(t, c + Vector3.up * 14f, new Vector3(9f, 26f, 9f), 60f, new Color(1f, 0.95f, 1f, 1f), new Color(0.7f, 0.85f, 1f, 0.8f));
            }
            // prisms: three crystals drifting high in the shaft, each splitting the light into beams to the mirrors
            {
                var beam = new MeshBuilder();
                beam.AddPrism(Vector3.zero, Vector3.forward, 0.12f, 0.12f, 6, false, false, 361, 0f);
                SaveMesh(beam.Build("BeamUnit"), "BeamUnit");
                for (int k = 0; k < 3; k++)
                {
                    float a = k / 3f * Mathf.PI * 2f;
                    Vector3 pp = c + new Vector3(Mathf.Sin(a) * 2.2f, 17f + k * 2.5f, Mathf.Cos(a) * 2.2f);
                    var pr = Place("Prism", "ShardSmall", "GlassShardGlow", pp, Quaternion.Euler(R(0, 360), R(0, 360), 0f), t, false, 4.5f, false);
                    GameObjectUtility.SetStaticEditorFlags(pr, 0);
                    var plev = pr.AddComponent<Levitate>(); plev.bob = 0.5f; plev.period = 6f + k; plev.spin = new Vector3(10f, 25f, 0f); plev.wobble = 5f;
                    for (int j = 0; j < 4; j++)
                    {
                        float b = a + (j - 1.5f) * 0.55f + 0.3f;
                        Vector3 target = c + new Vector3(Mathf.Sin(b) * (radius - 0.6f), fy + 3.5f + j * 1.3f, Mathf.Cos(b) * (radius - 0.6f));
                        Vector3 d = target - pp;
                        var bm = Place("PrismBeam", "BeamUnit", j % 2 == 0 ? "LightBeamPrism" : "LightBeam", pp, Quaternion.LookRotation(d), t, false, 1f, false);
                        bm.transform.localScale = new Vector3(1f + j * 0.3f, 1f + j * 0.3f, d.magnitude);
                        var bl = new GameObject("BeamHit"); bl.transform.SetParent(t, false); bl.transform.position = target - d.normalized * 0.8f;
                        var bli = bl.AddComponent<Light>(); bli.type = LightType.Point; bli.range = 4f; bli.intensity = 3f; bli.shadows = LightShadows.None;
                        bli.color = Color.HSVToRGB((k * 4 + j) / 12f * 0.5f + 0.5f, 0.45f, 1f);
                    }
                }
            }
            for (int i = 0; i < 40; i++)
            {
                float a0 = i / 40f * Mathf.PI * 2f, a1 = (i + 1) / 40f * Mathf.PI * 2f;
                float mid = (a0 + a1) * 0.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(mid * Mathf.Rad2Deg, 180f)) < 6.5f) continue;   // the gate's gap (fog gate collider fills it)
                Vector3 p = c + new Vector3(Mathf.Sin(mid) * (radius + 0.6f), 8f, Mathf.Cos(mid) * (radius + 0.6f));
                var b = new GameObject("TempleBound"); b.transform.SetParent(t, false); b.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, mid * Mathf.Rad2Deg, 0f));
                b.AddComponent<BoxCollider>().size = new Vector3(2f * Mathf.PI * (radius + 0.6f) / 40f + 0.3f, 40f, 0.6f);
            }
            // the gate: two tall broken panes framing the fog
            SaveMesh(ProceduralMeshes.ArchFrame(331, 5f, 6f, 9f, 0.9f, 1.4f), "TempleGateArch");
            Place("TempleGate", "TempleGateArch", "TempleStone", c + new Vector3(0f, TempleFloorY - ForestY, -radius), Quaternion.identity, t, true);

            // ---- floating panes of shattered glass, drifting rocks, the dream
            SaveMesh(ProceduralMeshes.ArchPane(3f, 4f, 6f, 0f), "TemplePane");
            for (int i = 0; i < 16; i++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = R(12f, 30f);
                Vector3 p = c + new Vector3(Mathf.Sin(a) * rr, R(7f, 20f), Mathf.Cos(a) * rr);
                var pane = Place("FloatingPane", "TemplePane", "TempleGlass", p, Quaternion.Euler(R(-30, 30), R(0, 360), R(-40, 40)), t, false, R(0.7f, 1.6f), false);
                GameObjectUtility.SetStaticEditorFlags(pane, 0);
                var lev = pane.AddComponent<Levitate>(); lev.bob = R(0.3f, 0.9f); lev.period = R(5f, 10f); lev.spin = new Vector3(0f, R(-8f, 8f), 0f); lev.wobble = 6f;
            }
            for (int i = 0; i < 46; i++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = R(5f, 34f);
                Vector3 p = c + new Vector3(Mathf.Sin(a) * rr, R(6f, 24f), Mathf.Cos(a) * rr);
                var sh = Place("FloatingShard", "GroundShard" + (i % 4), i % 3 == 0 ? "GlassShardGlow" : "GlassShard", p, Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)), t, false, R(0.6f, 2.2f), false);
                GameObjectUtility.SetStaticEditorFlags(sh, 0);
                var lev = sh.AddComponent<Levitate>(); lev.bob = R(0.2f, 0.7f); lev.period = R(3f, 7f); lev.spin = new Vector3(R(-20, 20), R(-30, 30), 0f); lev.wobble = 10f;
            }
            for (int i = 0; i < 10; i++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = R(26f, 42f);
                var rk = Place("FloatingRock", "Rock" + (i % 6), "TempleStone", c + new Vector3(Mathf.Sin(a) * rr, R(10f, 26f), Mathf.Cos(a) * rr), Yaw(R(0, 360)), t, false, R(2f, 5f));
                GameObjectUtility.SetStaticEditorFlags(rk, 0);
                var lev = rk.AddComponent<Levitate>(); lev.bob = 1f; lev.period = R(8f, 14f); lev.spin = new Vector3(0f, R(-3f, 3f), 0f); lev.wobble = 3f;
            }
            Motes(t, c + Vector3.up * 8f, new Vector3(50f, 16f, 50f), 110f, new Color(0.85f, 0.7f, 1f, 0.9f), new Color(0.5f, 0.9f, 1f, 0.7f));
            // the light of the dream: a pale core above the arena, violet rims
            foreach (var (o, col, inten, range) in new[] { (new Vector3(0f, 16f, 0f), new Color(0.75f, 0.65f, 1f), 30f, 40f), (new Vector3(-14f, 4f, 8f), new Color(0.5f, 0.3f, 1f), 10f, 18f), (new Vector3(14f, 4f, 8f), new Color(0.35f, 0.75f, 1f), 10f, 18f), (new Vector3(0f, 4f, -16f), new Color(0.6f, 0.4f, 1f), 8f, 16f) })
            {
                var lg = new GameObject("TempleLight"); lg.transform.SetParent(t, false); lg.transform.position = c + o;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = col; l.intensity = inten; l.range = range; l.shadows = LightShadows.None;
            }
            var probe = new GameObject("TempleReflection"); probe.transform.SetParent(t, false); probe.transform.position = c + Vector3.up * 3f;
            var rp = probe.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Realtime; rp.refreshMode = ReflectionProbeRefreshMode.EveryFrame; rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            rp.resolution = 128; rp.hdr = true; rp.size = new Vector3(48f, 30f, 48f); rp.importance = 3;
            rp.clearFlags = ReflectionProbeClearFlags.SolidColor; rp.backgroundColor = new Color(0.08f, 0.05f, 0.14f);
            rp.farClipPlane = 60f;

            Checkpoint(t, 6, "The Glass Temple", c + new Vector3(4.6f, 0.05f, -radius - 6f), c + new Vector3(0f, 0.2f, -radius - 7f), 0f);
        }

        // ================================================================== level refs
        static void WireMirrorRefs(LevelRefs lv, Transform systems)
        {
            Transform[] Points(string name, List<(Vector3 p, float yaw)> list) => list.Select(e =>
            {
                var t = new GameObject(name).transform; t.SetParent(systems); t.position = e.p; t.rotation = Yaw(e.yaw); return t;
            }).ToArray();
            lv.shardWraiths = Points("WraithSpawn", _wraiths);
            lv.glassRevenants = Points("RevenantSpawn", _revenants);
            var ms = new GameObject("MirrorSpawn").transform; ms.SetParent(systems); ms.position = TempleC + new Vector3(0f, TempleFloorY - ForestY + 0.1f, 8f); ms.rotation = Yaw(180);
            lv.mirrorSpawn = ms;
            var gate = new GameObject("MirrorGate").transform; gate.SetParent(systems); gate.position = TempleC + new Vector3(0f, TempleFloorY - ForestY + 0.02f, -22f); gate.rotation = Yaw(0);
            lv.mirrorGate = gate;
            lv.mirrorArena = new Bounds(TempleC + new Vector3(0f, 8f, 0f), new Vector3(46f, 26f, 46f));
            var vc = new GameObject("VaultChest").transform; vc.SetParent(systems); vc.position = _vaultChest.p; vc.rotation = Yaw(_vaultChest.yaw);
            lv.vaultChest = vc;

            // atmosphere zones (first match wins): temple, tower, wood, bridge
            var zones = new List<LevelRefs.AtmosphereZone>
            {
                new LevelRefs.AtmosphereZone { name = "Glass Temple", bounds = new Bounds(TempleC + new Vector3(0f, 10f, 0f), new Vector3(60f, 40f, 60f)),
                    fog = new Color(0.14f, 0.08f, 0.24f), fogDensity = 0.016f, ambient = new Color(0.5f, 0.42f, 0.7f), exposure = 0.85f,
                    overrideSun = true, sunColor = new Color(0.7f, 0.6f, 1f), sunIntensity = 1.1f, sunEuler = new Vector3(70f, 20f, 0f) },
                new LevelRefs.AtmosphereZone { name = "Stair Tower", bounds = new Bounds(TowerC + new Vector3(0f, -6f, 0f), new Vector3(13.5f, 40f, 13.5f)),
                    fog = new Color(0.07f, 0.06f, 0.055f), fogDensity = 0.025f, ambient = new Color(0.42f, 0.37f, 0.33f), exposure = 1.3f },
                new LevelRefs.AtmosphereZone { name = "Thornwood", bounds = new Bounds(new Vector3(0f, ForestY + 8f, 435f), new Vector3(130f, 30f, 220f)),
                    fog = new Color(0.11f, 0.08f, 0.16f), fogDensity = 0.03f, ambient = new Color(0.36f, 0.32f, 0.48f), exposure = 1.0f,
                    overrideSun = true, sunColor = new Color(0.55f, 0.48f, 0.85f), sunIntensity = 0.7f, sunEuler = new Vector3(40f, 160f, 0f) },
                new LevelRefs.AtmosphereZone { name = "The Long Bridge", bounds = new Bounds(new Vector3(0f, 20f, 284f), new Vector3(110f, 50f, 128f)),
                    fog = new Color(0.42f, 0.27f, 0.25f), fogDensity = 0.005f, ambient = new Color(1.0f, 0.78f, 0.7f), exposure = 0.95f,
                    sky = true, overrideSun = true, sunColor = new Color(1f, 0.6f, 0.34f), sunIntensity = 2.4f, sunEuler = new Vector3(8f, 180f, 0f) },
            };
            lv.zones = zones.Concat(lv.zones ?? new LevelRefs.AtmosphereZone[0]).ToArray();

            // the bridge shares the terrace's grade; the wood and the temple get the dream's
            var terraceProf = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{Gen}/Rendering/TerraceVolume.asset");
            void LocalVolume(string name, Vector3 c, Vector3 size, VolumeProfile prof, float priority)
            {
                if (prof == null) return;
                var vg = new GameObject(name); vg.transform.SetParent(systems, false); vg.transform.position = c;
                var box = vg.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = size;
                vg.layer = 2;
                var vol = vg.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = priority; vol.blendDistance = 8f; vol.sharedProfile = prof;
            }
            LocalVolume("BridgeGrade", new Vector3(0f, 20f, 284f), new Vector3(110f, 50f, 100f), terraceProf, 10f);

            VolumeProfile Dream(string file, bool temple)
            {
                string vpath = $"{Gen}/Rendering/{file}.asset";
                if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(vpath) != null) AssetDatabase.DeleteAsset(vpath);
                var prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, vpath);
                var ca = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                ca.contrast.Override(14f); ca.saturation.Override(temple ? 6f : -12f); ca.colorFilter.Override(new Color(0.93f, 0.9f, 1f));
                var sp = prof.Add<UnityEngine.Rendering.Universal.SplitToning>(true);
                sp.shadows.Override(new Color(0.42f, 0.32f, 0.6f)); sp.highlights.Override(new Color(0.5f, 0.62f, 0.72f)); sp.balance.Override(-5f);
                var bl = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true); bl.intensity.Override(temple ? 1.1f : 0.7f); bl.threshold.Override(0.8f); bl.tint.Override(new Color(0.8f, 0.65f, 1f));
                if (temple)
                {
                    var ld = prof.Add<UnityEngine.Rendering.Universal.LensDistortion>(true); ld.intensity.Override(-0.14f); ld.scale.Override(0.98f);
                    var chr = prof.Add<UnityEngine.Rendering.Universal.ChromaticAberration>(true); chr.intensity.Override(0.3f);
                }
                foreach (var cmp in prof.components) { cmp.name = cmp.GetType().Name; AssetDatabase.AddObjectToAsset(cmp, prof); }
                EditorUtility.SetDirty(prof);
                return prof;
            }
            LocalVolume("ThornGrade", new Vector3(0f, ForestY + 8f, 420f), new Vector3(110f, 30f, 190f), Dream("ThornVolume", false), 10f);
            LocalVolume("TempleGrade", TempleC + new Vector3(0f, 10f, 0f), new Vector3(52f, 40f, 52f), Dream("TempleVolume", true), 11f);
        }
    }
}
