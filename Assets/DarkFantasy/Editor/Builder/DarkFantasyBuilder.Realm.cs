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
    /// Chapter 4 — the Realm of the Red Moon (far below the world, y -150):
    ///   the Glass Temple ─(the dead Mirror's frame becomes a portal)─► the Field of Graves under a
    ///   huge red moon: ash, leaning headstones, hanged lanterns of souls, a ruined chapel, dead trees
    ///   ─► the Reaper's Court (a ring of obelisks, fog gate) ─► Death, the spectral Grim Reaper.
    /// New enemies: the Gravecrawler (a six-legged ribcage with a skull, skitters and leaps) and the
    /// Lantern Warden (a hooded giant with a soul lantern: wide sweeps and a tolling ring of light).
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const float RealmY = -150f;
        static readonly Vector3 RealmArrive = new Vector3(0f, RealmY, 820f);
        static readonly Vector3 CourtC = new Vector3(0f, RealmY, 940f);
        const float CourtR = 22f;
        static readonly List<(Vector3 p, float yaw)> _shades = new List<(Vector3, float)>();
        static readonly List<(Vector3 p, float yaw)> _sentinels = new List<(Vector3, float)>();

        // ================================================================== materials
        static void BuildRealmMaterials()
        {
            string T(string n) => $"{Gen}/Textures/{n}.png";
            var ash = AssetDatabase.LoadAssetAtPath<Texture2D>(T("ash"));
            var bricks = AssetDatabase.LoadAssetAtPath<Texture2D>(T("stone_bricks"));
            var ground = AssetDatabase.LoadAssetAtPath<Texture2D>(T("ground_mud"));
            var knight = AssetDatabase.LoadAssetAtPath<Texture2D>(KnightTex);
            LitMat("RealmAsh", ground, new Color(0.2f, 0.15f, 0.16f));
            LitMat("RealmStone", bricks, new Color(0.24f, 0.21f, 0.25f), emission: new Color(0.01f, 0f, 0f));
            LitMat("RealmGrave", bricks, new Color(0.36f, 0.33f, 0.35f));
            LitMat("RealmRune", null, new Color(1f, 0.2f, 0.2f), emission: new Color(2.4f, 0.25f, 0.2f));
            var moon = LitMat("RedMoon", ash, new Color(1f, 0.3f, 0.25f), emission: new Color(7f, 1.0f, 0.7f));
            moon.SetTexture("_EmissionMap", ash);
            LitMat("ReaperCloak", ash, new Color(0.2f, 0.18f, 0.26f), emission: new Color(0.1f, 0.06f, 0.2f));
            LitMat("ReaperEcto", ash, new Color(0.3f, 0.25f, 0.55f), emission: new Color(0.7f, 0.45f, 2.1f));
            LitMat("ShadeRobe", ash, new Color(0.08f, 0.05f, 0.06f), emission: new Color(0.06f, 0.0f, 0.02f));
            LitMat("ShadeEyes", null, new Color(1f, 0.25f, 0.25f), emission: new Color(2.6f, 0.3f, 0.3f));
            LitMat("SentinelKnight", knight, new Color(0.62f, 0.58f, 0.66f), emission: new Color(0.03f, 0.01f, 0.06f));
            TransparentMat("SpectralBlade", null, new Color(0.55f, 0.7f, 1f, 0.75f), true);
            LitMat("RealmBone", bricks, new Color(0.62f, 0.57f, 0.5f));
            LitMat("WardenRobe", ash, new Color(0.07f, 0.065f, 0.08f));
            LitMat("LanternFlame", null, new Color(0.6f, 0.75f, 1f), emission: new Color(2.2f, 3.2f, 5.5f));
            TransparentMat("SoulMist", null, new Color(0.65f, 0.75f, 1f, 0.55f), true);
        }

        // ================================================================== effects
        static GameObject WispFire(Transform parent, Vector3 localPos, Vector3 box, float rate)
        {
            var go = new GameObject("WispFire");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.38f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.65f, 1f, 1f), new Color(0.7f, 0.35f, 1f, 1f));
            main.gravityModifier = -0.35f;
            var em = ps.emission; em.rateOverTime = rate; em.rateOverDistance = rate * 0.4f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.5f, 0.3f, 1f), 1) },
                      new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0f, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = M["ParticleAdd"];
            ps.Play();
            var lg = new GameObject("Light"); lg.transform.SetParent(go.transform, false);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.55f, 0.45f, 1f); l.range = 7f; l.intensity = 4f; l.shadows = LightShadows.None;
            lg.AddComponent<FlickerLight>();
            go.SetActive(false);
            return go;
        }

        static ParticleSystem Mist(Transform parent, string name, Vector3 localPos, bool trail)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 600;
            main.startLifetime = trail ? new ParticleSystem.MinMaxCurve(1.2f, 2.6f) : new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = trail ? new ParticleSystem.MinMaxCurve(0.1f, 0.5f) : new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = trail ? new ParticleSystem.MinMaxCurve(0.8f, 2.0f) : new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.03f, 0.02f, 0.04f, 0.75f), new Color(0.12f, 0.08f, 0.16f, 0.6f));
            main.gravityModifier = trail ? -0.02f : -0.06f;
            var em = ps.emission; em.rateOverTime = trail ? 10f : 0f; em.rateOverDistance = trail ? 5f : 0f;
            var sh = ps.shape; sh.shapeType = trail ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere; sh.radius = trail ? 1.0f : 1.4f;
            if (trail) sh.rotation = new Vector3(90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
            go.GetComponent<ParticleSystemRenderer>().material = M["ParticleAlpha"];
            ps.Play();
            if (trail)
            {
                // a few pale souls drift in the wake
                var sg = new GameObject("Souls"); sg.transform.SetParent(go.transform, false);
                var sp = sg.AddComponent<ParticleSystem>();
                sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var sm = sp.main; sm.loop = true; sm.simulationSpace = ParticleSystemSimulationSpace.World; sm.maxParticles = 120;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f); sm.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                sm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f); sm.gravityModifier = -0.08f;
                sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.85f, 1f), new Color(0.6f, 0.45f, 1f));
                var se = sp.emission; se.rateOverTime = 6f; se.rateOverDistance = 2f;
                var ss = sp.shape; ss.shapeType = ParticleSystemShapeType.Sphere; ss.radius = 1.2f;
                sg.GetComponent<ParticleSystemRenderer>().material = M["ParticleAdd"];
                sp.Play();
            }
            return ps;
        }

        // ================================================================== meshes
        static void BuildRealmMeshes()
        {
            // headstones: slab + rounded top, a cross, a broken one, an obelisk
            for (int i = 0; i < 4; i++)
            {
                var mb = new MeshBuilder { uvScale = 1.2f };
                switch (i)
                {
                    case 0:
                        ProceduralMeshes.Box(mb, new Vector3(0f, 0.55f, 0f), new Vector3(0.75f, 1.1f, 0.18f), 400, 0.03f);
                        Dome(mb, new Vector3(0f, 1.1f, 0f), new Vector3(0.375f, 0.3f, 0.09f), 0f, 90f, 10, 3);
                        break;
                    case 1:
                        ProceduralMeshes.Box(mb, new Vector3(0f, 0.8f, 0f), new Vector3(0.18f, 1.6f, 0.16f), 401, 0.02f);
                        ProceduralMeshes.Box(mb, new Vector3(0f, 1.15f, 0f), new Vector3(0.8f, 0.17f, 0.15f), 402, 0.02f);
                        break;
                    case 2:
                        ProceduralMeshes.Box(mb, new Vector3(0f, 0.35f, 0f), new Vector3(0.7f, 0.7f, 0.2f), 403, 0.05f);
                        mb.AddPrism(new Vector3(0.1f, 0.7f, 0f), new Vector3(-0.15f, 0.95f, 0.02f), 0.2f, 0.05f, 4, false, true, 404, 0.2f);
                        break;
                    default:
                        mb.AddPrism(Vector3.zero, new Vector3(0f, 2.6f, 0f), 0.32f, 0.2f, 4, true, false, 405, 0f);
                        mb.AddPrism(new Vector3(0f, 2.6f, 0f), new Vector3(0f, 3.0f, 0f), 0.2f, 0.01f, 4, false, false, 406, 0f);
                        break;
                }
                SaveMesh(mb.Build("Grave" + i), "Grave" + i);
            }
            // the obelisks of the court: tall, a slit of red rune light on the inner face
            {
                var mb = new MeshBuilder { uvScale = 0.6f };
                mb.AddPrism(Vector3.zero, new Vector3(0f, 0.6f, 0f), 1.1f, 1.0f, 4, true, false, 410, 0f);
                mb.AddPrism(new Vector3(0f, 0.6f, 0f), new Vector3(0f, 9f, 0f), 0.75f, 0.45f, 4, false, false, 411, 0.02f);
                mb.AddPrism(new Vector3(0f, 9f, 0f), new Vector3(0f, 10.4f, 0f), 0.45f, 0.02f, 4, false, false, 412, 0f);
                SaveMesh(mb.Build("CourtObelisk"), "CourtObelisk");
                var rn = new MeshBuilder();
                for (int k = 0; k < 6; k++) ProceduralMeshes.Box(rn, new Vector3(0f, 2f + k * 1.1f, 0.62f - k * 0.03f), new Vector3(0.12f, 0.7f, 0.05f), 413 + k, 0f);
                SaveMesh(rn.Build("CourtRune"), "CourtRune");
            }
            // skull pile and a soul lantern on a crooked post
            {
                var mb = new MeshBuilder { uvScale = 2f };
                var rnd = new System.Random(77);
                for (int k = 0; k < 9; k++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = (float)rnd.NextDouble() * 0.45f;
                    AddSkull(mb, new Vector3(Mathf.Cos(a) * r, 0.12f + (k > 5 ? 0.18f : 0f), Mathf.Sin(a) * r), 0.22f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 20f));
                }
                SaveMesh(mb.Build("SkullPile"), "SkullPile");
                var post = new MeshBuilder { uvScale = 1f };
                post.AddPrism(Vector3.zero, new Vector3(0.1f, 2.6f, 0f), 0.07f, 0.05f, 5, true, false, 420, 0.15f);
                post.AddPrism(new Vector3(0.1f, 2.5f, 0f), new Vector3(0.75f, 2.65f, 0f), 0.04f, 0.035f, 5, false, true, 421, 0.1f);
                post.AddPrism(new Vector3(0.7f, 2.6f, 0f), new Vector3(0.7f, 2.2f, 0f), 0.008f, 0.008f, 3, false, false, 422, 0f);
                ProceduralMeshes.Box(post, new Vector3(0.7f, 2.05f, 0f), new Vector3(0.2f, 0.3f, 0.2f), 423, 0.02f);
                SaveMesh(post.Build("SoulLanternPost"), "SoulLanternPost");
            }
            // the red moon (a sphere) and the dark disc of the arena floor
            {
                var mb = new MeshBuilder();
                Dome(mb, Vector3.zero, Vector3.one, -90f, 90f, 32, 16);
                SaveMesh(mb.Build("MoonSphere"), "MoonSphere");
                var fl = new MeshBuilder { uvScale = 0.3f };
                const int segs = 36;
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i / (float)segs * Mathf.PI * 2f, a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
                    Vector3 o0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)), o1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                    Vector3 C0 = Vector3.up * 0.25f;
                    fl.AddTri(C0, C0 + o0 * CourtR, C0 + o1 * CourtR, Vector3.up);
                    fl.AddQuad(C0 + o0 * CourtR, o0 * CourtR + Vector3.down * 0.3f, o1 * CourtR + Vector3.down * 0.3f, C0 + o1 * CourtR, o0 + o1);
                }
                SaveMesh(fl.Build("CourtFloor"), "CourtFloor");
                // the portal's disc (XY plane, facing -Z)
                var pd = new MeshBuilder();
                for (int i = 0; i < 32; i++)
                {
                    float a0 = i / 32f * Mathf.PI * 2f, a1 = (i + 1) / 32f * Mathf.PI * 2f;
                    pd.AddQuad(Vector3.zero, Vector3.zero, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f), new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), Vector3.back,
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f + Mathf.Cos(a1) * 0.5f, 0.5f + Mathf.Sin(a1) * 0.5f), new Vector2(0.5f + Mathf.Cos(a0) * 0.5f, 0.5f + Mathf.Sin(a0) * 0.5f));
                }
                SaveMesh(pd.Build("PortalDisc"), "PortalDisc");
            }
            // the skeletal hand of Call of Souls (palm + five clawed fingers of mist, pointing +Z)
            {
                var mb = new MeshBuilder();
                ProceduralMeshes.Box(mb, new Vector3(0f, 0f, 0.15f), new Vector3(0.55f, 0.14f, 0.5f), 430, 0.04f);
                for (int k = 0; k < 5; k++)
                {
                    bool thumb = k == 4;
                    Vector3 root = thumb ? new Vector3(-0.3f, 0f, 0.15f) : new Vector3(-0.21f + k * 0.14f, 0f, 0.4f);
                    Vector3 dir = thumb ? new Vector3(-0.7f, -0.2f, 0.7f) : new Vector3((k - 1.5f) * 0.12f, -0.15f, 1f);
                    Horn(mb, root, dir, thumb ? 0.35f : 0.55f, 0.05f, 22f, Vector3.right, 4);
                }
                mb.AddPrism(new Vector3(0f, 0f, -0.1f), new Vector3(0f, 0f, -1.4f), 0.12f, 0.4f, 6, false, false, 440, 0.25f);   // the trailing wrist of smoke
                SaveMesh(mb.Build("SoulHand"), "SoulHand");
            }
            // one spectral scythe as a single-material mesh (the Rain)
            {
                var mb = new MeshBuilder();
                mb.AddPrism(Vector3.zero, new Vector3(0f, SnathLength, 0f), 0.03f, 0.026f, 6, true, true, 450, 0f);
                ScytheBlade(mb, 0.95f, 0.17f, 0.12f, 0.32f, 0.014f, hook: 0.1f);
                SaveMesh(mb.Build("SpectralScythe"), "SpectralScythe");
            }
        }

        // ================================================================== prefabs
        static Transform Pivot(string name, Transform parent, Vector3 lp)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = lp; return t;
        }

        static (GameObject root, RealmBeastAI ai) BeastRoot(string name, string display, float radius, float height)
        {
            var root = new GameObject(name);
            var cc = root.AddComponent<CharacterController>();
            cc.radius = radius; cc.height = height; cc.center = new Vector3(0f, height * 0.5f + 0.05f, 0f);
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), false);
            var ai = root.AddComponent<RealmBeastAI>();
            ai.displayName = display;
            return (root, ai);
        }

        static void BeastFinish(GameObject root, Transform body, Vector3 hurtC, float hurtR, float hurtH, float lockY)
        {
            var hb = new GameObject("Hurtbox"); hb.transform.SetParent(body, false); hb.transform.localPosition = hurtC;
            var cap = hb.AddComponent<CapsuleCollider>(); cap.isTrigger = true; cap.radius = hurtR; cap.height = hurtH;
            if (hurtH < hurtR * 3f) cap.direction = 2;
            var lot = root.AddComponent<LockOnTarget>(); var lp = new GameObject("LockPoint").transform; lp.SetParent(body, false); lp.localPosition = new Vector3(0, lockY, 0);
            lot.lockPoint = lp;
        }

        /// <summary>Gravecrawler: a ribcage on six jointed bone legs, a horned skull for a head, soul-fire in the chest.</summary>
        static GameObject BuildShade()
        {
            var (root, ai) = BeastRoot("Gravecrawler", "Gravecrawler", 0.7f, 1.2f);
            ai.kind = RealmBeastAI.Kind.Crawler;
            ai.baseHealth = 85f; ai.basePoise = 20f;
            ai.walkSpeed = 3.2f; ai.chaseSpeed = 6.2f; ai.turnSpeed = 300f;
            ai.staggerDuration = 1f; ai.meleeDamage = 18f; ai.aggroRange = 20f;
            var body = Pivot("Body", root.transform, new Vector3(0f, 0.75f, 0f));
            // ribcage: a spine with curved ribs, a pelvis behind, all bone
            var mb = new MeshBuilder { uvScale = 2f };
            mb.AddPrism(new Vector3(0, 0.12f, -0.9f), new Vector3(0, 0.18f, 0.55f), 0.07f, 0.08f, 6, true, true, 801, 0.01f);
            for (int i = 0; i < 6; i++)
            {
                float z = -0.55f + i * 0.2f, w = 0.36f - Mathf.Abs(i - 2.5f) * 0.04f;
                foreach (int sx in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(0, 0.16f, z), b = new Vector3(sx * w, 0.02f, z + 0.03f), c = new Vector3(sx * w * 0.85f, -0.26f, z + 0.06f), d = new Vector3(sx * 0.08f, -0.36f, z + 0.08f);
                    mb.AddPrism(a, b, 0.03f, 0.028f, 5, false, false, 810 + i, 0f);
                    mb.AddPrism(b, c, 0.028f, 0.024f, 5, false, false, 820 + i, 0f);
                    mb.AddPrism(c, d, 0.024f, 0.016f, 5, false, true, 830 + i, 0f);
                }
                mb.AddPrism(new Vector3(0, 0.15f, z), new Vector3(0, 0.34f, z - 0.08f), 0.03f, 0.008f, 4, false, true, 840 + i, 0f);   // vertebra spines
            }
            mb.AddPrism(new Vector3(-0.22f, 0.05f, -0.9f), new Vector3(0.22f, 0.05f, -0.9f), 0.11f, 0.11f, 6, true, true, 850, 0.04f);   // pelvis
            mb.AddPrism(new Vector3(0, 0.1f, -0.95f), new Vector3(0.05f, 0.25f, -1.6f), 0.06f, 0.012f, 5, false, true, 851, 0.01f);      // tail
            SaveMesh(mb.Build("CrawlerRibs"), "CrawlerRibs");
            Place("Ribs", "CrawlerRibs", "RealmBone", Vector3.zero, Quaternion.identity, body, false).transform.localPosition = Vector3.zero;
            // soul-fire caged in the ribs
            var core = Place("Core", "ShardSmall", "SoulMist", Vector3.zero, Quaternion.identity, body, false, 2.2f, false);
            core.transform.localPosition = new Vector3(0f, -0.08f, -0.1f);
            // head: a long horned skull on a neck
            var head = Pivot("Head", body, new Vector3(0f, 0.18f, 0.6f));
            var hb2 = new MeshBuilder { uvScale = 2f };
            hb2.AddPrism(new Vector3(0, 0.02f, -0.05f), new Vector3(0, 0f, 0.42f), 0.17f, 0.07f, 7, true, true, 860, 0.025f);        // cranium tapering to the snout
            hb2.AddPrism(new Vector3(0, -0.1f, 0.0f), new Vector3(0, -0.12f, 0.4f), 0.09f, 0.05f, 6, true, true, 861, 0.01f);         // jaw
            foreach (int sx in new[] { -1, 1 })
            {
                hb2.AddPrism(new Vector3(sx * 0.1f, 0.12f, -0.02f), new Vector3(sx * 0.3f, 0.32f, -0.25f), 0.045f, 0.01f, 5, false, true, 862, 0f);   // horns swept back
                for (int k = 0; k < 3; k++) hb2.AddPrism(new Vector3(sx * 0.05f, -0.06f, 0.22f + k * 0.07f), new Vector3(sx * 0.055f, -0.15f, 0.24f + k * 0.07f), 0.014f, 0.003f, 4, false, true, 863 + k, 0f);  // teeth
            }
            SaveMesh(hb2.Build("CrawlerSkull"), "CrawlerSkull");
            Place("Skull", "CrawlerSkull", "RealmBone", Vector3.zero, Quaternion.identity, head, false).transform.localPosition = Vector3.zero;
            var em = new MeshBuilder();
            foreach (int sx in new[] { -1, 1 }) { Vector3 c = new Vector3(sx * 0.075f, 0.06f, 0.2f); float h = 0.025f; em.AddQuad(c + new Vector3(-h, -h, 0), c + new Vector3(h, -h, 0), c + new Vector3(h, h, 0), c + new Vector3(-h, h, 0), Vector3.forward); }
            SaveMesh(em.Build("CrawlerEyes"), "CrawlerEyes");
            Place("Eyes", "CrawlerEyes", "ShadeEyes", Vector3.zero, Quaternion.identity, head, false, 1f, false).transform.localPosition = Vector3.zero;
            // six legs: thigh up and out, shin down to a point
            var lm = new MeshBuilder { uvScale = 2f };
            lm.AddPrism(Vector3.zero, new Vector3(0.45f, 0.35f, 0f), 0.045f, 0.035f, 5, true, false, 870, 0f);
            lm.AddPrism(new Vector3(0.45f, 0.35f, 0f), new Vector3(0.75f, -0.68f, 0f), 0.035f, 0.008f, 5, true, true, 871, 0f);
            SaveMesh(lm.Build("CrawlerLeg"), "CrawlerLeg");
            var legs = new List<Transform>();
            for (int i = 0; i < 6; i++)
            {
                int sx = i % 2 == 0 ? -1 : 1; float z = 0.3f - (i / 2) * 0.42f;
                var hip = Pivot("Leg" + i, body, new Vector3(sx * 0.18f, -0.05f, z));
                hip.localRotation = Quaternion.Euler(0f, sx < 0 ? 180f : 0f, 0f) * Quaternion.Euler(0f, (i / 2 - 1) * -22f * sx, 0f);
                Place("Bone", "CrawlerLeg", "RealmBone", Vector3.zero, Quaternion.identity, hip, false).transform.localPosition = Vector3.zero;
                legs.Add(hip);
            }
            var lg = new GameObject("Glow"); lg.transform.SetParent(body, false); lg.transform.localPosition = new Vector3(0f, -0.05f, -0.1f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.55f, 0.65f, 1f); l.range = 4f; l.intensity = 1.2f; l.shadows = LightShadows.None;
            ai.body = body; ai.head = head; ai.legs = legs.ToArray();
            BeastFinish(root, body, new Vector3(0, 0f, -0.2f), 0.6f, 2.2f, 0.1f);
            return SavePrefab(root, "Gravecrawler");
        }

        /// <summary>Lantern Warden: a gaunt hooded giant in a heavy tattered mantle, bearing a soul lantern on a pole.</summary>
        static GameObject BuildSentinel(AnimatorController ctrl)
        {
            var (root, ai) = BeastRoot("LanternWarden", "Lantern Warden", 0.65f, 3.2f);
            ai.kind = RealmBeastAI.Kind.Warden;
            ai.baseHealth = 260f; ai.basePoise = 80f;
            ai.walkSpeed = 1.5f; ai.chaseSpeed = 2.6f; ai.turnSpeed = 120f;
            ai.staggerDuration = 1.2f; ai.meleeDamage = 24f; ai.specialDamage = 32f; ai.aggroRange = 16f;
            var body = Pivot("Body", root.transform, Vector3.zero);
            var mb = new MeshBuilder { uvScale = 1f };
            mb.AddPrism(new Vector3(0, 0f, 0), new Vector3(0, 1.9f, 0.05f), 0.62f, 0.38f, 11, false, false, 901, 0.09f);     // mantle
            mb.AddPrism(new Vector3(0, 1.9f, 0.05f), new Vector3(0, 2.45f, 0.12f), 0.38f, 0.5f, 11, false, false, 902, 0.06f);  // shoulders bunched under the cowl
            mb.AddPrism(new Vector3(0, 2.45f, 0.12f), new Vector3(0, 3.15f, 0.42f), 0.5f, 0.1f, 11, false, true, 903, 0.05f);  // deep hood, pitched forward
            mb.AddPrism(new Vector3(0, 2.3f, -0.1f), new Vector3(0, 0.3f, -0.55f), 0.35f, 0.55f, 9, false, false, 904, 0.12f);  // cape trailing behind
            mb.AddPrism(new Vector3(-0.36f, 2.3f, 0.1f), new Vector3(-0.55f, 1.35f, 0.3f), 0.13f, 0.09f, 6, false, false, 905, 0.02f);  // left arm hanging
            mb.AddPrism(new Vector3(-0.55f, 1.35f, 0.3f), new Vector3(-0.58f, 0.95f, 0.38f), 0.07f, 0.02f, 5, false, true, 906, 0.02f);
            SaveMesh(mb.Build("WardenBody"), "WardenBody");
            Place("Mantle", "WardenBody", "WardenRobe", Vector3.zero, Quaternion.identity, body, false).transform.localPosition = Vector3.zero;
            // the face: a bone mask deep in the hood with two cold eyes
            var mask = new MeshBuilder { uvScale = 2f };
            mask.AddPrism(new Vector3(0, 2.62f, 0.32f), new Vector3(0, 2.62f, 0.42f), 0.15f, 0.13f, 7, true, true, 907, 0.01f);
            SaveMesh(mask.Build("WardenMask"), "WardenMask");
            Place("Mask", "WardenMask", "RealmBone", Vector3.zero, Quaternion.identity, body, false, 1f, false).transform.localPosition = Vector3.zero;
            var em = new MeshBuilder();
            foreach (int sx in new[] { -1, 1 }) { Vector3 c = new Vector3(sx * 0.06f, 2.66f, 0.43f); float h = 0.022f; em.AddQuad(c + new Vector3(-h, -h, 0), c + new Vector3(h, -h, 0), c + new Vector3(h, h, 0), c + new Vector3(-h, h, 0), Vector3.forward); }
            SaveMesh(em.Build("WardenEyes"), "WardenEyes");
            Place("Eyes", "WardenEyes", "LanternFlame", Vector3.zero, Quaternion.identity, body, false, 1f, false).transform.localPosition = Vector3.zero;
            // right arm: shoulder pivot holding a long iron pole; the lantern hangs from its head
            var arm = Pivot("Arm", body, new Vector3(0.42f, 2.25f, 0.1f));
            var am = new MeshBuilder { uvScale = 1f };
            am.AddPrism(Vector3.zero, new Vector3(0.12f, -0.75f, 0.35f), 0.13f, 0.09f, 6, true, false, 910, 0.02f);
            SaveMesh(am.Build("WardenArm"), "WardenArm");
            Place("Sleeve", "WardenArm", "WardenRobe", Vector3.zero, Quaternion.identity, arm, false).transform.localPosition = Vector3.zero;
            var pole = new MeshBuilder { uvScale = 1f };
            pole.AddPrism(new Vector3(0.12f, -1.9f, 0.35f), new Vector3(0.12f, 1.15f, 0.35f), 0.035f, 0.03f, 6, true, true, 911, 0f);
            pole.AddPrism(new Vector3(0.12f, 1.15f, 0.35f), new Vector3(0.12f, 1.2f, 0.95f), 0.03f, 0.025f, 5, true, true, 912, 0f);   // the crook
            SaveMesh(pole.Build("WardenPole"), "WardenPole");
            Place("Pole", "WardenPole", "Iron", Vector3.zero, Quaternion.identity, arm, false).transform.localPosition = Vector3.zero;
            var lan = new MeshBuilder { uvScale = 2f };
            Vector3 lc = new Vector3(0.12f, 0.7f, 0.95f);
            lan.AddPrism(lc + new Vector3(0, 0.45f, 0), lc + new Vector3(0, 0.3f, 0), 0.01f, 0.01f, 4, false, false, 913, 0f);       // chain
            lan.AddPrism(lc + new Vector3(0, 0.3f, 0), lc + new Vector3(0, 0.22f, 0), 0.05f, 0.17f, 6, true, false, 914, 0f);       // cap
            for (int k = 0; k < 6; k++) { float an = k * Mathf.PI / 3f; Vector3 o = new Vector3(Mathf.Cos(an), 0, Mathf.Sin(an)) * 0.15f; lan.AddPrism(lc + o + new Vector3(0, 0.22f, 0), lc + o * 0.8f + new Vector3(0, -0.2f, 0), 0.012f, 0.012f, 4, false, false, 915, 0f); }  // cage bars
            lan.AddPrism(lc + new Vector3(0, -0.2f, 0), lc + new Vector3(0, -0.28f, 0), 0.13f, 0.03f, 6, true, true, 916, 0f);
            SaveMesh(lan.Build("WardenLantern"), "WardenLantern");
            Place("Lantern", "WardenLantern", "Iron", Vector3.zero, Quaternion.identity, arm, false).transform.localPosition = Vector3.zero;
            var flame = Place("Flame", "ShardSmall", "LanternFlame", Vector3.zero, Quaternion.identity, arm, false, 1.6f, false);
            flame.transform.localPosition = lc;
            var lg = new GameObject("LanternLight"); lg.transform.SetParent(arm, false); lg.transform.localPosition = lc;
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.5f, 0.68f, 1f); l.range = 9f; l.intensity = 3f; l.shadows = LightShadows.None;
            WispFire(arm, lc, new Vector3(0.12f, 0.12f, 0.12f), 10f);
            ai.body = body; ai.arm = arm; ai.lanternLight = l;
            BeastFinish(root, body, new Vector3(0, 1.6f, 0), 0.7f, 3.2f, 2.3f);
            return SavePrefab(root, "LanternWarden");
        }

        const string GrimFbx = "Assets/DarkFantasy/Art/GrimReaper/GrimReaper.fbx";
        const string GrimTex = "Assets/grim reaper/textures/";
        /// <summary>Yaw that turns the imported model to face +Z.</summary>
        const float GrimYaw = 180f;

        static Texture2D GrimTexture(string file, bool normal)
        {
            string path = GrimTex + file;
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != want || ti.maxTextureSize > 1024) { ti.textureType = want; ti.maxTextureSize = 1024; ti.SaveAndReimport(); }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Death, from the "grim reaper" folder: the winged reaper (Alembic) baked in Blender into 8 poses
        /// of its hovering loop (body + scythe each), played as a flipbook (GrimFlipbook); the boss turns,
        /// leans and spins the whole figure for its cuts (ReaperBossAI's procedural poses).
        /// </summary>
        static GameObject BuildReaperBoss(GameObject minion)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(GrimFbx);
            if (fbx == null) { Debug.LogError("[DF] Reaper boss: " + GrimFbx + " missing"); return null; }
            var bodyMat = LitMat("GrimBody", GrimTexture("DIFUSE.jpg", false), new Color(0.82f, 0.78f, 0.86f), emission: new Color(0.06f, 0.03f, 0.1f));
            var nrm = GrimTexture("DIFUSENORMAL.jpg", true);
            if (nrm) { bodyMat.SetTexture("_BumpMap", nrm); bodyMat.EnableKeyword("_NORMALMAP"); }
            var ectoMat = LitMat("GrimEcto", GrimTexture("DIFUSE.jpg", false), new Color(0.45f, 0.4f, 0.85f), emission: new Color(0.6f, 0.4f, 1.8f));
            ectoMat.SetTexture("_EmissionMap", GrimTexture("DIFUSE.jpg", false));
            var axeMat = LitMat("GrimScythe", GrimTexture("AXE_DIFUSE.jpg", false), Color.white, emission: new Color(0.12f, 0.05f, 0.2f));
            var anrm = GrimTexture("AXE_NORMAL.jpg", true);
            if (anrm) { axeMat.SetTexture("_BumpMap", anrm); axeMat.EnableKeyword("_NORMALMAP"); }

            const float H = 4.4f;
            var root = new GameObject("ReaperBoss");
            var cc = root.AddComponent<CharacterController>();
            cc.radius = 1.1f; cc.height = 4.4f; cc.center = new Vector3(0f, 2.25f, 0f);
            var hover = new GameObject("Hover").transform; hover.SetParent(root.transform, false);
            var pose = new GameObject("Pose").transform; pose.SetParent(hover, false); pose.localPosition = new Vector3(0f, H * 0.45f, 0f);
            var visual = new GameObject("Visual").transform; visual.SetParent(pose, false);
            var model = (GameObject)Object.Instantiate(fbx, visual);
            model.name = "GrimReaper";
            // one mesh each for body and scythe, their flight poses as blend shapes (GrimFlipbook morphs them)
            var bodies = new List<Renderer>(); var scythes = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool isBody = r.name.StartsWith("GR_Body");
                r.sharedMaterial = isBody ? bodyMat : axeMat;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.updateWhenOffscreen = true;
                (isBody ? bodies : scythes).Add(r);
            }
            // normalise: frame 0 stands H tall, centred on the root, feet (robe hem) at the ground
            model.transform.localRotation = Quaternion.Euler(0f, GrimYaw, 0f) * model.transform.localRotation;
            Bounds bb = bodies.Count > 0 ? bodies[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            float s = H / Mathf.Max(0.01f, bb.size.y);
            model.transform.localScale *= s;
            bb = bodies[0].bounds;
            model.transform.position += new Vector3(root.transform.position.x - bb.center.x, root.transform.position.y - bb.min.y, root.transform.position.z - bb.center.z);
            var flip = model.AddComponent<GrimFlipbook>(); flip.renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true); flip.fps = 6f;

            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), false);
            var ai = root.AddComponent<ReaperBossAI>();
            ai.displayName = "Death, the Last Reaper";
            ai.baseHealth = 3400f; ai.basePoise = 240f; ai.poiseRegenPerSecond = 22f; ai.poiseRegenDelay = 3f;
            ai.staggerDuration = 1.3f; ai.parryStaggerDuration = 1.3f;
            ai.walkSpeed = 3f; ai.chaseSpeed = 4f; ai.turnSpeed = 200f;
            ai.hover = hover; ai.pose = pose;
            ai.bodyRenderers = bodies.ToArray(); ai.scytheRenderers = scythes.ToArray();
            ai.cloakMaterial = bodyMat; ai.ectoMaterial = ectoMat;
            ai.minionPrefab = minion;
            ai.spectralScytheMesh = Meshes["SpectralScythe"]; ai.spectralMaterial = M["SpectralBlade"];

            // phase 2 fire along the blade (the scythe's upper half in frame 0)
            Bounds sb = scythes.Count > 0 ? scythes[0].bounds : new Bounds(root.transform.position + Vector3.up * 3f, Vector3.one);
            var fireAnchor = new GameObject("BladeFire").transform; fireAnchor.SetParent(pose, false);
            fireAnchor.position = new Vector3(sb.center.x, sb.center.y + sb.extents.y * 0.3f, sb.center.z);
            ai.wispFire = WispFire(fireAnchor, Vector3.zero, new Vector3(Mathf.Max(0.3f, sb.size.x * 0.6f), sb.size.y * 0.55f, Mathf.Max(0.3f, sb.size.z * 0.6f)), 90f);

            var eyes = new GameObject("EyeLight"); eyes.transform.SetParent(hover, false); eyes.transform.localPosition = new Vector3(0f, H * 0.82f, 0.5f);
            var el = eyes.AddComponent<Light>(); el.type = LightType.Point; el.color = new Color(0.9f, 0.12f, 0.15f); el.range = 7f; el.intensity = 3f; el.shadows = LightShadows.None;
            ai.eyeLight = el;
            // a soft key light on the figure itself, so the silhouette reads in the red gloom
            var key = new GameObject("BodyLight"); key.transform.SetParent(hover, false); key.transform.localPosition = new Vector3(0f, H * 0.6f, 2.5f);
            var kl = key.AddComponent<Light>(); kl.type = LightType.Point; kl.color = new Color(0.7f, 0.65f, 1f); kl.range = 7f; kl.intensity = 2.5f; kl.shadows = LightShadows.None;
            var chest = new GameObject("Chest").transform; chest.SetParent(hover, false); chest.localPosition = new Vector3(0f, H * 0.62f, 0.6f);
            ai.chest = chest;
            ai.mistTrail = Mist(root.transform, "MistTrail", new Vector3(0f, 0.4f, 0f), true);
            ai.mistBurst = Mist(hover, "MistBurst", new Vector3(0f, H * 0.5f, 0f), false);
            var hand = Place("SoulHand", "SoulHand", "SoulMist", Vector3.zero, Quaternion.identity, root.transform, false, 1.8f, false);
            hand.SetActive(false);
            var hl = new GameObject("Light"); hl.transform.SetParent(hand.transform, false);
            var hli = hl.AddComponent<Light>(); hli.type = LightType.Point; hli.color = new Color(0.6f, 0.75f, 1f); hli.range = 5f; hli.intensity = 3f; hli.shadows = LightShadows.None;
            ai.handVisual = hand.transform;

            var hb = new GameObject("Hurtbox"); hb.transform.SetParent(hover, false); hb.transform.localPosition = new Vector3(0f, H * 0.5f, 0f);
            var cap = hb.AddComponent<CapsuleCollider>(); cap.isTrigger = true; cap.radius = 1.1f; cap.height = H;
            var lot = root.AddComponent<LockOnTarget>();
            var lp = new GameObject("LockPoint").transform; lp.SetParent(hover, false); lp.localPosition = new Vector3(0f, H * 0.6f, 0f);
            lot.lockPoint = lp; lot.framingSize = 2.6f; lot.enabled = false;
            return SavePrefab(root, "ReaperBoss");
        }

        static GameObject BuildReaperArenaPrefab()
        {
            var root = new GameObject("ReaperArena");
            root.AddComponent<NetworkObject>();
            var arena = root.AddComponent<ReaperArena>();
            var fog = new GameObject("FogGate"); fog.transform.SetParent(root.transform, false);
            fog.AddComponent<MeshFilter>().sharedMesh = Meshes["FogSheet"];
            var mr = fog.AddComponent<MeshRenderer>(); mr.sharedMaterial = M["FogGate"]; mr.shadowCastingMode = ShadowCastingMode.Off;
            fog.AddComponent<UVScroll>().speed = new Vector2(0.03f, 0.06f);
            var colGo = new GameObject("Barrier"); colGo.transform.SetParent(root.transform, false);
            var box = colGo.AddComponent<BoxCollider>(); box.center = new Vector3(0, 3.6f, 0.1f); box.size = new Vector3(5f, 7.4f, 0.6f);
            var lightGo = new GameObject("FogLight"); lightGo.transform.SetParent(root.transform, false); lightGo.transform.localPosition = new Vector3(0, 3f, -1.2f);
            var l = lightGo.AddComponent<Light>(); l.type = LightType.Point; l.range = 8f; l.intensity = 1.5f; l.color = new Color(1f, 0.35f, 0.35f);
            arena.fogRenderer = mr; arena.fogCollider = box; arena.fogLight = l;
            return SavePrefab(root, "ReaperArena");
        }

        static void BuildRealmPrefabs(Prefabs p, AnimatorController ctrl)
        {
            BuildRealmMeshes();
            p.shade = BuildShade();
            p.sentinel = BuildSentinel(ctrl);
            p.reaperBoss = BuildReaperBoss(p.shade);
            p.reaperArena = BuildReaperArenaPrefab();
        }

        // ================================================================== the level
        static GameObject BuildPortal(Transform parent, string name, Vector3 pos, float yaw, float radius, bool requiresMirror, Vector3 dest, float destYaw, string prompt, Color glow)
        {
            var root = new GameObject(name); root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Yaw(yaw));
            var rp = root.AddComponent<RealmPortal>();
            rp.requiresMirror = requiresMirror; rp.prompt = prompt;
            var open = new GameObject("Open"); open.transform.SetParent(root.transform, false);
            var swirl = new GameObject("Swirl"); swirl.transform.SetParent(open.transform, false);
            swirl.transform.localPosition = new Vector3(0f, radius + (requiresMirror ? 1.4f : 0.4f), 0f);
            swirl.transform.localScale = Vector3.one * radius;
            swirl.AddComponent<MeshFilter>().sharedMesh = Meshes["PortalDisc"];
            var mr = swirl.AddComponent<MeshRenderer>(); mr.sharedMaterial = M["PortalSwirl"]; mr.shadowCastingMode = ShadowCastingMode.Off;
            var back = Object.Instantiate(swirl, open.transform); back.name = "SwirlBack";
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var lg = new GameObject("Light"); lg.transform.SetParent(open.transform, false); lg.transform.localPosition = new Vector3(0f, radius + 0.4f, 1.2f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = glow; l.range = radius * 5f; l.intensity = 6f; l.shadows = LightShadows.None;
            Motes(open.transform, pos + Vector3.up * (radius + 0.4f), new Vector3(radius * 2f, radius * 2f, 1.5f), 30f, new Color(glow.r, glow.g, glow.b, 0.9f), new Color(1f, 0.85f, 0.9f, 0.7f));
            var d = new GameObject(name + "_Destination").transform; d.SetParent(parent, false); d.SetPositionAndRotation(dest, Yaw(destYaw));
            rp.destination = d; rp.openVisual = open; rp.openLight = l; rp.swirl = swirl.transform;
            open.SetActive(!requiresMirror);
            return root;
        }

        static bool InOssuary(float x, float z, float margin) => x > OssuaryC.x - 6.8f - margin && x < OssuaryC.x + 7.8f + margin && z > OssuaryC.z - 7.3f - margin && z < OssuaryC.z + 7.3f + margin;

        static void BuildRealm(Transform parent)
        {
            _shades.Clear(); _sentinels.Clear();
            var root = new GameObject("RealmOfTheRedMoon").transform; root.SetParent(parent, false);
            var rnd = new System.Random(6666);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            // ---- the way in: the dead Mirror's frame (in the Glass Temple) and the way back
            Vector3 mirrorBase = TempleC + new Vector3(0f, TempleFloorY - ForestY + 0.1f, 8f);
            BuildPortal(root, "MirrorPortal", mirrorBase, 180f, 3.1f, true, RealmArrive + new Vector3(0f, 0.1f, 2f), 0f, "Step through the broken mirror", new Color(0.85f, 0.25f, 0.35f));
            BuildPortal(root, "ReturnPortal", RealmArrive + new Vector3(0f, 0f, -6f), 0f, 1.6f, false, TempleC + new Vector3(0f, TempleFloorY - ForestY + 0.1f, 0f), 180f, "Return to the Glass Temple", new Color(0.7f, 0.5f, 1f));

            // ---- the ground: an ash plain with gentle swells (flat on the path and in the court)
            const float x0 = -90f, x1 = 90f, z0 = 790f, z1 = 1010f, cell = 4f;
            float GH(float x, float z)
            {
                float hh = (Noise.Fbm2(x * 0.05f, z * 0.05f, 3, 131) - 0.5f) * 2.2f;
                hh *= Mathf.Clamp01((Mathf.Abs(x) - 5f) / 8f);
                float dc = Vector2.Distance(new Vector2(x, z), new Vector2(CourtC.x, CourtC.z));
                if (dc < CourtR + 10f) hh *= Mathf.Clamp01((dc - CourtR - 4f) / 6f);
                hh = Mathf.Lerp(hh, -60f, Mathf.Clamp01((CourtR + 16f - dc) / 3f));      // the abyss around the floating clock
                if (InOssuary(x, z, 4f)) hh = 0f;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(RealmArrive.x, RealmArrive.z)) < 14f) hh = 0f;
                return hh;
            }
            var gmb = new MeshBuilder { uvScale = 0.22f };
            for (float x = x0; x < x1; x += cell)
                for (float z = z0; z < z1; z += cell)
                {
                    Vector3 a = new Vector3(x, RealmY + GH(x, z), z), b = new Vector3(x + cell, RealmY + GH(x + cell, z), z);
                    Vector3 c = new Vector3(x + cell, RealmY + GH(x + cell, z + cell), z + cell), d = new Vector3(x, RealmY + GH(x, z + cell), z + cell);
                    gmb.AddQuad(a, d, c, b, Vector3.up);
                }
            SaveMesh(gmb.Build("RealmGround"), "RealmGround");
            Place("Ground", "RealmGround", "RealmAsh", Vector3.zero, Quaternion.identity, root, true, 1f, false);
            AddBox(root, new Vector3(0f, RealmY - 0.6f, 846f), new Vector3(200f, 1f, 112f));          // a floor under the floor (not under the abyss)
            foreach (int sx in new[] { -1, 1 }) AddBox(root, new Vector3(sx * 50f, RealmY - 0.6f, 945f), new Vector3(24f, 1f, 82f));
            AddBox(root, new Vector3(-62f, RealmY + 10f, 900f), new Vector3(1f, 30f, 240f));
            AddBox(root, new Vector3(62f, RealmY + 10f, 900f), new Vector3(1f, 30f, 240f));
            AddBox(root, new Vector3(0f, RealmY + 10f, 800f), new Vector3(130f, 30f, 1f));
            AddBox(root, new Vector3(0f, RealmY + 10f, 985f), new Vector3(130f, 30f, 1f));
            // the horizon: crags all around
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Sin(a) * R(70f, 95f), RealmY - 2f, 900f + Mathf.Cos(a) * R(100f, 115f));
                Place("Crag", "Rock" + (i % 6), "RealmStone", p, Quaternion.Euler(R(-10, 10), R(0, 360), R(-10, 10)), root, false, R(9f, 18f));
            }

            // ---- the red moon, low over the court
            var moon = Place("RedMoon", "MoonSphere", "RedMoon", new Vector3(0f, RealmY + 72f, 1085f), Quaternion.Euler(0f, 0f, 20f), root, false, 62f, false);
            moon.GetComponent<MeshRenderer>().receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(moon, 0);
            var mlev = moon.AddComponent<Levitate>(); mlev.spin = new Vector3(0f, 0.6f, 0f); mlev.bob = 0f; mlev.wobble = 0f;

            // ---- the Field of Graves (the path runs north, z 826 → 916)
            for (int i = 0; i < 160; i++)
            {
                float z = R(832f, 912f), side = rnd.NextDouble() < 0.5 ? -1f : 1f, x = side * R(4.5f, 40f);
                if (Vector2.Distance(new Vector2(x, z), new Vector2(-14f, 868f)) < 9f) continue;   // the chapel
                if (GH(x, z) < -2f || InOssuary(x, z, 3f)) continue;
                var g = Place("Grave", "Grave" + (i % 4 == 3 && rnd.NextDouble() < 0.6 ? 0 : i % 4), "RealmGrave", new Vector3(x, RealmY + GH(x, z) - 0.15f, z),
                    Quaternion.Euler(R(-12f, 12f), R(-25f, 25f) + (side > 0 ? 180f : 0f), R(-10f, 10f)), root, i % 3 == 0, R(0.9f, 1.4f));
            }
            for (int i = 0; i < 26; i++)
            {
                float z = R(830f, 915f), side = rnd.NextDouble() < 0.5 ? -1f : 1f, x = side * R(7f, 45f);
                if (GH(x, z) < -2f || InOssuary(x, z, 3f)) continue;
                Place("DeadTree", "TwistedTree_" + (700 + i % 6), "ThornBark", new Vector3(x, RealmY + GH(x, z) - 0.3f, z), Yaw(R(0, 360)), root, true, R(1.0f, 1.6f));
            }
            for (int i = 0; i < 22; i++)
            {
                float z = R(830f, 915f), x = (rnd.NextDouble() < 0.5 ? -1f : 1f) * R(2.6f, 30f);
                if (GH(x, z) < -2f || InOssuary(x, z, 3f)) continue;
                Place("Skulls", "SkullPile", "MirrorBone", new Vector3(x, RealmY + GH(x, z) - 0.05f, z), Yaw(R(0, 360)), root, false, R(0.8f, 1.5f));
            }
            // soul lanterns along the path: cold blue lights against the red
            for (int i = 0; i < 9; i++)
            {
                float z = 836f + i * 9.5f;
                foreach (int s in new[] { -1, 1 })
                {
                    if ((i + (s > 0 ? 1 : 0)) % 2 == 0) continue;
                    var post = Place("SoulLantern", "SoulLanternPost", "Iron", new Vector3(s * 3.4f, RealmY, z), Yaw(s > 0 ? 180f : 0f), root, true);
                    var lg = new GameObject("Soul"); lg.transform.SetParent(post.transform, false); lg.transform.localPosition = new Vector3(0.7f, 2.05f, 0f);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.5f, 0.7f, 1f); l.range = 13f; l.intensity = 9f; l.shadows = LightShadows.None;
                    lg.AddComponent<FlickerLight>();
                    Motes(post.transform, post.transform.position + post.transform.rotation * new Vector3(0.7f, 2.05f, 0f), new Vector3(0.3f, 0.3f, 0.3f), 6f, new Color(0.6f, 0.8f, 1f, 0.9f), new Color(0.5f, 0.5f, 1f, 0.7f));
                }
            }
            // a ruined chapel on the west of the path (pillars, an altar, a broken arch)
            {
                Vector3 c = new Vector3(-14f, RealmY, 868f);
                for (int i = 0; i < 6; i++)
                {
                    float x = (i % 2 == 0 ? -1f : 1f) * 3.2f, z = (i / 2 - 1) * 4f;
                    Place("ChapelPillar", i == 2 ? "PillarHall" : "PillarBroken" + (i % 2), "RealmStone", c + new Vector3(x, 0f, z), Yaw(R(0, 360)), root, true, i == 2 ? 0.9f : 1.3f);
                }
                var altar = new MeshBuilder { uvScale = 0.8f };
                ProceduralMeshes.Box(altar, c + new Vector3(0f, 0.55f, 5f), new Vector3(2.4f, 1.1f, 1.1f), 460, 0.04f);
                SaveMesh(altar.Build("ChapelAltar"), "ChapelAltar");
                Place("Altar", "ChapelAltar", "RealmStone", Vector3.zero, Quaternion.identity, root, true);
                Place("AltarSkulls", "SkullPile", "MirrorBone", c + new Vector3(0f, 1.1f, 5f), Yaw(20f), root, false, 0.9f);
                var cl = new GameObject("AltarCandle"); cl.transform.SetParent(root, false); cl.transform.position = c + new Vector3(0f, 2f, 4.6f);
                var cli = cl.AddComponent<Light>(); cli.type = LightType.Point; cli.color = new Color(1f, 0.35f, 0.3f); cli.range = 10f; cli.intensity = 4f; cli.shadows = LightShadows.None;
                cl.AddComponent<FlickerLight>();
                _sentinels.Add((c + new Vector3(0f, 0.1f, 2.5f), 90f));
            }
            Motes(root, new Vector3(0f, RealmY + 4f, 875f), new Vector3(90f, 8f, 100f), 90f, new Color(1f, 0.3f, 0.25f, 0.8f), new Color(0.6f, 0.75f, 1f, 0.6f));

            // enemies of the field
            foreach (var (x, z, yaw) in new[] { (6f, 846f, 200f), (-8f, 856f, 160f), (10f, 878f, 220f), (-5f, 892f, 180f), (14f, 900f, 200f), (-18f, 884f, 150f), (4f, 897f, 180f) })
                _shades.Add((new Vector3(x, RealmY + 0.1f, z), yaw));
            _sentinels.Add((new Vector3(3f, RealmY + 0.1f, 872f), 180f));
            _sentinels.Add((new Vector3(-2.5f, RealmY + 0.1f, 894f), 180f));
            _sentinels.Add((new Vector3(6f, RealmY + 0.1f, 897f), 180f));

            // ---- the Reaper's Court
            var court = new GameObject("ReapersCourt").transform; court.SetParent(root, false);
            Place("CourtFloor", "CourtFloor", "RealmStone", CourtC, Quaternion.identity, court, true, 1f, false);
            BuildClockCourt(court);
            for (int i = 0; i < 40; i++)
            {
                float a0 = i / 40f * Mathf.PI * 2f, a1 = (i + 1) / 40f * Mathf.PI * 2f, mid = (a0 + a1) * 0.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(mid * Mathf.Rad2Deg, 180f)) < 6.5f) continue;
                Vector3 p = CourtC + new Vector3(Mathf.Sin(mid) * (CourtR + 0.6f), 8f, Mathf.Cos(mid) * (CourtR + 0.6f));
                var b = new GameObject("CourtBound"); b.transform.SetParent(court, false); b.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, mid * Mathf.Rad2Deg, 0f));
                b.AddComponent<BoxCollider>().size = new Vector3(2f * Mathf.PI * (CourtR + 0.6f) / 40f + 0.3f, 40f, 0.6f);
            }
            SaveMesh(ProceduralMeshes.ArchFrame(332, 5f, 6f, 9f, 0.9f, 1.4f), "CourtGateArch");
            Place("CourtGate", "CourtGateArch", "RealmStone", CourtC + new Vector3(0f, 0.2f, -CourtR), Quaternion.identity, court, true);
            Motes(court, CourtC + Vector3.up * 5f, new Vector3(44f, 10f, 44f), 40f, new Color(1f, 0.25f, 0.25f, 0.9f), new Color(0.45f, 0.35f, 1f, 0.7f));
            foreach (var (o, col, inten, range) in new[] { (new Vector3(0f, 14f, 0f), new Color(1f, 0.35f, 0.35f), 40f, 46f), (new Vector3(-12f, 4f, 8f), new Color(0.5f, 0.55f, 1f), 12f, 20f), (new Vector3(12f, 4f, 8f), new Color(0.5f, 0.55f, 1f), 12f, 20f),
                     (new Vector3(-12f, 4f, -8f), new Color(0.7f, 0.5f, 1f), 10f, 20f), (new Vector3(12f, 4f, -8f), new Color(0.7f, 0.5f, 1f), 10f, 20f), (new Vector3(0f, 5f, -16f), new Color(1f, 0.45f, 0.4f), 10f, 18f) })
            {
                var lg = new GameObject("CourtLight"); lg.transform.SetParent(court, false); lg.transform.position = CourtC + o;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = col; l.intensity = inten; l.range = range; l.shadows = LightShadows.None;
            }

            BuildOssuary(root);
            Checkpoint(root, 7, "The Red Moon", RealmArrive + new Vector3(4.2f, 0f, 3f), RealmArrive + new Vector3(0f, 0.2f, 2f), 0f);
        }

        /// <summary>
        /// The Court of the Last Hour: the arena is a clock face of black stone floating over an abyss.
        /// A bone bridge leads in from the south; eleven hour-stones ring the rim (the twelfth is the gate);
        /// glowing hands turn in the floor; a colossal clock hangs in the sky behind, chained to the moon;
        /// souls rise out of the abyss in a slow vortex around the platform, lit red from below.
        /// </summary>
        static void BuildClockCourt(Transform court)
        {
            // ---- the rock the platform was torn from, hanging underneath
            {
                var mb = new MeshBuilder { uvScale = 0.15f };
                mb.AddPrism(new Vector3(0f, -0.04f, 0f), new Vector3(0f, -9f, 0f), CourtR + 0.3f, CourtR * 0.7f, 24, false, false, 470, 0.9f);
                mb.AddPrism(new Vector3(0f, -9f, 0f), new Vector3(1.5f, -26f, -1f), CourtR * 0.7f, 2.5f, 18, false, true, 471, 1.6f);
                for (int k = 0; k < 7; k++)
                {
                    float a = k / 7f * Mathf.PI * 2f + 0.3f, r = CourtR * (0.45f + (k % 3) * 0.12f);
                    Vector3 top = new Vector3(Mathf.Sin(a) * r, -8f, Mathf.Cos(a) * r);
                    mb.AddPrism(top, top + new Vector3(0.5f, -6f - (k % 4) * 3f, 0.3f), 2.2f, 0.15f, 7, false, true, 472 + k, 0.4f);
                }
                SaveMesh(mb.Build("CourtUnderside"), "CourtUnderside");
                Place("Underside", "CourtUnderside", "RealmStone", CourtC, Quaternion.identity, court, false, 1f, false);
            }
            // ---- the bone bridge (south), with railings
            {
                float zA = CourtC.z - CourtR - 17f, zB = CourtC.z - CourtR + 0.6f, len = zB - zA, zc = (zA + zB) * 0.5f;
                var mb = new MeshBuilder { uvScale = 0.6f };
                ProceduralMeshes.Box(mb, new Vector3(0f, -0.2f, 0f), new Vector3(4.4f, 0.9f, len), 480, 0.08f);
                for (int k = 0; k < 5; k++)   // arches underneath
                {
                    float z = -len * 0.5f + 2f + k * (len - 4f) / 4f;
                    mb.AddPrism(new Vector3(0f, -0.6f, z), new Vector3(0f, -4.5f + Mathf.Abs(k - 2) * 0.8f, z), 1.1f, 0.2f, 6, false, true, 481 + k, 0.15f);
                }
                SaveMesh(mb.Build("CourtBridge"), "CourtBridge");
                var br = Place("Bridge", "CourtBridge", "RealmStone", new Vector3(0f, RealmY + 0.25f, zc), Quaternion.identity, court, true, 1f, true);
                var rail = new MeshBuilder { uvScale = 2f };
                int posts = Mathf.RoundToInt(len / 2f);
                foreach (int sx in new[] { -1, 1 })
                {
                    for (int k = 0; k <= posts; k++)
                    {
                        float z = -len * 0.5f + k * len / posts;
                        rail.AddPrism(new Vector3(sx * 2.05f, 0.2f, z), new Vector3(sx * 2.05f, 1.25f, z), 0.07f, 0.05f, 5, false, false, 490, 0.01f);
                        AddSkull(rail, new Vector3(sx * 2.05f, 1.36f, z), 0.17f, Quaternion.Euler(0f, sx * 90f + (k % 3 - 1) * 20f, 0f));
                    }
                    rail.AddPrism(new Vector3(sx * 2.05f, 1.05f, -len * 0.5f), new Vector3(sx * 2.05f, 1.05f, len * 0.5f), 0.045f, 0.045f, 5, true, true, 491, 0f);
                    rail.AddPrism(new Vector3(sx * 2.05f, 0.6f, -len * 0.5f), new Vector3(sx * 2.05f, 0.6f, len * 0.5f), 0.035f, 0.035f, 5, true, true, 492, 0f);
                    AddBox(court, new Vector3(sx * 2.15f, RealmY + 1.2f, zc), new Vector3(0.3f, 2.4f, len));
                }
                SaveMesh(rail.Build("CourtRail"), "CourtRail");
                Place("Rails", "CourtRail", "RealmBone", new Vector3(0f, RealmY + 0.25f, zc), Quaternion.identity, court, false);
                for (int k = 0; k < 3; k++)
                {
                    var lg = new GameObject("BridgeSoul"); lg.transform.SetParent(court, false); lg.transform.position = new Vector3((k % 2 == 0 ? -1f : 1f) * 2.05f, RealmY + 2.2f, zA + 3f + k * (len - 6f) / 2f);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.5f, 0.7f, 1f); l.range = 8f; l.intensity = 4f; l.shadows = LightShadows.None;
                    lg.AddComponent<FlickerLight>();
                }
            }
            // ---- eleven hour-stones on the rim, each with its hour cut in red light
            {
                var mb = new MeshBuilder { uvScale = 0.6f };
                mb.AddPrism(Vector3.zero, new Vector3(0f, 0.5f, 0f), 1.0f, 0.9f, 4, true, false, 500, 0f);
                mb.AddPrism(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 4.2f, 0f), 0.62f, 0.5f, 4, false, false, 501, 0.03f);
                mb.AddPrism(new Vector3(0f, 4.2f, 0f), new Vector3(0f, 4.5f, 0f), 0.75f, 0.75f, 4, true, true, 502, 0f);
                for (int k = 0; k < 2; k++) mb.AddPrism(new Vector3((k * 2 - 1) * 0.35f, 4.5f, 0f), new Vector3((k * 2 - 1) * 0.7f, 5.6f, 0f), 0.14f, 0.02f, 4, false, true, 503, 0f);  // a crescent of two horns
                SaveMesh(mb.Build("HourStone"), "HourStone");
                for (int h = 1; h <= 12; h++)
                {
                    var rn = new MeshBuilder();
                    int bars = h <= 4 ? h : h <= 8 ? h - 4 : h - 8;
                    int rows = h <= 4 ? 1 : h <= 8 ? 2 : 3;
                    for (int r = 0; r < rows; r++)
                        for (int k = 0; k < (r == rows - 1 ? bars : 4); k++)
                            ProceduralMeshes.Box(rn, new Vector3((k - ((r == rows - 1 ? bars : 4) - 1) * 0.5f) * 0.16f, 3.4f - r * 0.75f, 0.53f), new Vector3(0.07f, 0.55f, 0.04f), 510 + k, 0f);
                    SaveMesh(rn.Build("HourRune" + h), "HourRune" + h);
                }
                for (int h = 1; h <= 12; h++)
                {
                    float a = h / 12f * Mathf.PI * 2f;                  // 12 o'clock is north; 6 o'clock (south) is the gate
                    if (h == 6) continue;
                    Vector3 o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    Vector3 p = CourtC + o * (CourtR - 1.4f) + Vector3.up * 0.25f;
                    var rot = Quaternion.LookRotation(-o);
                    Place("HourStone" + h, "HourStone", "RealmStone", p, rot, court, true);
                    Place("Hour", "HourRune" + h, "RealmRune", p, rot, court, false, 1f, false);
                    var rl = new GameObject("HourLight"); rl.transform.SetParent(court, false); rl.transform.position = p + rot * new Vector3(0f, 3f, 1.3f);
                    var rli = rl.AddComponent<Light>(); rli.type = LightType.Point; rli.color = new Color(1f, 0.22f, 0.18f); rli.range = 7f; rli.intensity = 2.5f; rli.shadows = LightShadows.None;
                }
            }
            // ---- the face inlaid in the floor: a ring of ticks and two turning hands of red light
            {
                var mb = new MeshBuilder();
                for (int k = 0; k < 60; k++)
                {
                    float a = k / 60f * Mathf.PI * 2f, len = k % 5 == 0 ? 1.6f : 0.6f, w = k % 5 == 0 ? 0.16f : 0.07f;
                    Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), side = Vector3.Cross(Vector3.up, d) * w;
                    Vector3 p0 = d * (CourtR - 3.4f - len), p1 = d * (CourtR - 3.4f);
                    mb.AddQuad(p0 - side, p0 + side, p1 + side, p1 - side, Vector3.up);
                }
                for (int k = 0; k < 96; k++)  // the outer circle
                {
                    float a0 = k / 96f * Mathf.PI * 2f, a1 = (k + 1) / 96f * Mathf.PI * 2f;
                    Vector3 d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                    mb.AddQuad(d0 * (CourtR - 3.25f), d1 * (CourtR - 3.25f), d1 * (CourtR - 3.05f), d0 * (CourtR - 3.05f), Vector3.up);
                }
                SaveMesh(mb.Build("ClockTicks"), "ClockTicks");
                Place("ClockTicks", "ClockTicks", "RealmRune", CourtC + Vector3.up * 0.27f, Quaternion.identity, court, false, 1f, false);
                foreach (var (len, w, spin, nm) in new[] { (CourtR - 5f, 0.5f, 6f, "MinuteHand"), (CourtR - 9f, 0.8f, 0.5f, "HourHand") })
                {
                    var hm = new MeshBuilder();
                    hm.AddTri(new Vector3(-w, 0f, -1.5f), new Vector3(0f, 0f, len), new Vector3(w, 0f, -1.5f), Vector3.up);
                    hm.AddQuad(new Vector3(-w, 0f, -1.5f), new Vector3(w, 0f, -1.5f), new Vector3(w * 0.5f, 0f, -2.6f), new Vector3(-w * 0.5f, 0f, -2.6f), Vector3.up);
                    SaveMesh(hm.Build(nm), nm);
                    var hand = Place(nm, nm, "RealmRune", CourtC + Vector3.up * (nm == "HourHand" ? 0.28f : 0.29f), Yaw(nm == "HourHand" ? 120f : 0f), court, false, 1f, false);
                    var lev = hand.AddComponent<Levitate>(); lev.bob = 0f; lev.wobble = 0f; lev.spin = new Vector3(0f, spin, 0f);
                }
            }
            // ---- the great clock in the sky behind the court, chained to the moon
            {
                Vector3 cc = CourtC + new Vector3(0f, 24f, 52f);
                const float cr = 17f;
                var ring = new MeshBuilder { uvScale = 0.4f };
                for (int k = 0; k < 48; k++)
                {
                    float a0 = k / 48f * Mathf.PI * 2f, a1 = (k + 1) / 48f * Mathf.PI * 2f;
                    Vector3 p0 = new Vector3(Mathf.Sin(a0), Mathf.Cos(a0), 0f) * cr, p1 = new Vector3(Mathf.Sin(a1), Mathf.Cos(a1), 0f) * cr;
                    ring.AddPrism(p0, p1, 1.1f, 1.1f, 6, false, false, 520, 0.08f);
                    ring.AddPrism(p0 * 0.86f, p1 * 0.86f, 0.35f, 0.35f, 5, false, false, 521, 0.03f);
                }
                for (int h = 0; h < 12; h++)
                {
                    float a = h / 12f * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                    ring.AddPrism(d * (cr * 0.8f), d * (cr * 0.98f), 0.7f, 0.45f, 4, true, true, 522 + h, 0.05f);
                }
                ring.AddPrism(new Vector3(0f, 0f, 0.6f), new Vector3(0f, 0f, -0.8f), 1.6f, 1.2f, 8, true, true, 540, 0.05f);  // the hub
                SaveMesh(ring.Build("SkyClock"), "SkyClock");
                Place("SkyClock", "SkyClock", "RealmStone", cc, Quaternion.identity, court, false, 1f, false);
                var face = new MeshBuilder();
                for (int k = 0; k < 48; k++)
                {
                    float a0 = k / 48f * Mathf.PI * 2f, a1 = (k + 1) / 48f * Mathf.PI * 2f;
                    face.AddTri(new Vector3(0f, 0f, 0.4f), new Vector3(Mathf.Sin(a1), Mathf.Cos(a1), 0f) * cr * 0.84f + Vector3.forward * 0.4f, new Vector3(Mathf.Sin(a0), Mathf.Cos(a0), 0f) * cr * 0.84f + Vector3.forward * 0.4f, Vector3.back);
                }
                SaveMesh(face.Build("SkyClockFace"), "SkyClockFace");
                Place("SkyClockFace", "SkyClockFace", "SoulMist", cc, Quaternion.identity, court, false, 1f, false);
                foreach (var (len, w, spin, nm) in new[] { (cr * 0.92f, 0.7f, -4f, "SkyMinute"), (cr * 0.62f, 1.1f, -0.35f, "SkyHour") })
                {
                    var hm = new MeshBuilder { uvScale = 0.5f };
                    hm.AddPrism(new Vector3(0f, -2.5f, -0.9f), new Vector3(0f, len, -0.9f), w, 0.05f, 4, true, true, 541, 0f);
                    SaveMesh(hm.Build(nm), nm);
                    var hand = Place(nm, nm, "Iron", cc, Quaternion.Euler(0f, 0f, nm == "SkyHour" ? -130f : -20f), court, false, 1f, false);
                    var lev = hand.AddComponent<Levitate>(); lev.bob = 0f; lev.wobble = 0f; lev.spin = new Vector3(0f, 0f, spin);
                }
                var cl = new GameObject("SkyClockLight"); cl.transform.SetParent(court, false); cl.transform.position = cc + new Vector3(0f, 0f, -6f);
                var cli = cl.AddComponent<Light>(); cli.type = LightType.Point; cli.color = new Color(0.55f, 0.6f, 1f); cli.range = 34f; cli.intensity = 14f; cli.shadows = LightShadows.None;
                // chains: from the clock and the rim of the platform, up into the moon's glare
                var ch = new MeshBuilder { uvScale = 2f };
                for (int k = 0; k < 140; k++)
                {
                    float y = k * 0.55f;
                    if (k % 2 == 0) { ch.AddPrism(new Vector3(-0.16f, y, 0f), new Vector3(-0.16f, y + 0.55f, 0f), 0.05f, 0.05f, 4, false, false, 550, 0f); ch.AddPrism(new Vector3(0.16f, y, 0f), new Vector3(0.16f, y + 0.55f, 0f), 0.05f, 0.05f, 4, false, false, 551, 0f); }
                    else { ch.AddPrism(new Vector3(0f, y, -0.16f), new Vector3(0f, y + 0.55f, -0.16f), 0.05f, 0.05f, 4, false, false, 552, 0f); ch.AddPrism(new Vector3(0f, y, 0.16f), new Vector3(0f, y + 0.55f, 0.16f), 0.05f, 0.05f, 4, false, false, 553, 0f); }
                }
                SaveMesh(ch.Build("RealmChain"), "RealmChain");
                Vector3 moonP = new Vector3(0f, RealmY + 72f, 1085f);
                foreach (var from in new[] { cc + new Vector3(-cr * 0.7f, cr * 0.7f, 0f), cc + new Vector3(cr * 0.7f, cr * 0.7f, 0f),
                                             CourtC + new Vector3(-CourtR * 0.71f, 0f, CourtR * 0.71f), CourtC + new Vector3(CourtR * 0.71f, 0f, CourtR * 0.71f),
                                             CourtC + new Vector3(-CourtR, 0f, 0f), CourtC + new Vector3(CourtR, 0f, 0f) })
                {
                    Vector3 to = Vector3.Lerp(from, moonP + new Vector3(from.x * 1.5f, 0f, 0f), 0.85f);
                    Vector3 d = to - from; float sc = d.magnitude / 77f;
                    var c = Place("Chain", "RealmChain", "Iron", from, Quaternion.FromToRotation(Vector3.up, d.normalized), court, false, 1f, false);
                    c.transform.localScale = new Vector3(1.6f, sc, 1.6f);
                }
            }
            // ---- the abyss: souls spiral up out of it, lit red from far below
            {
                var go = new GameObject("SoulVortex"); go.transform.SetParent(court, false); go.transform.position = CourtC + Vector3.down * 34f;
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.loop = true; main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 13f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.75f, 1f, 0.9f), new Color(1f, 0.35f, 0.3f, 0.8f));
                main.maxParticles = 1400; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var em = ps.emission; em.rateOverTime = 110f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = CourtR + 9f; sh.radiusThickness = 0.35f; sh.rotation = new Vector3(-90f, 0f, 0f);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
                vel.y = new ParticleSystem.MinMaxCurve(3.2f, 4.8f);
                vel.orbitalY = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
                vel.orbitalX = new ParticleSystem.MinMaxCurve(0f); vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
                vel.radial = new ParticleSystem.MinMaxCurve(0f);
                var no = ps.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.2f;
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.75f), new GradientAlphaKey(0f, 1) });
                col.color = g;
                var tr = ps.trails; tr.enabled = true; tr.lifetime = 0.12f; tr.widthOverTrail = 0.6f; tr.dieWithParticles = true;
                var pr = go.GetComponent<ParticleSystemRenderer>(); pr.material = M["ParticleAdd"]; pr.trailMaterial = M["ParticleAdd"];
                foreach (var (o, i, r) in new[] { (new Vector3(0f, -26f, 0f), 60f, 60f), (new Vector3(0f, -14f, -26f), 18f, 30f) })
                {
                    var lg = new GameObject("AbyssLight"); lg.transform.SetParent(court, false); lg.transform.position = CourtC + o;
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.18f, 0.12f); l.intensity = i; l.range = r; l.shadows = LightShadows.None;
                }
            }
        }

        // ================================================================== level refs
        static void WireRealmRefs(LevelRefs lv, Transform systems)
        {
            Transform[] Points(string name, List<(Vector3 p, float yaw)> list) => list.Select(e =>
            {
                var t = new GameObject(name).transform; t.SetParent(systems); t.position = e.p; t.rotation = Yaw(e.yaw); return t;
            }).ToArray();
            lv.mourningShades = Points("ShadeSpawn", _shades);
            lv.deathSentinels = Points("SentinelSpawn", _sentinels);
            var rs = new GameObject("ReaperSpawn").transform; rs.SetParent(systems); rs.position = CourtC + new Vector3(0f, 0.35f, 7f); rs.rotation = Yaw(180);
            lv.reaperSpawn = rs;
            var gate = new GameObject("ReaperGate").transform; gate.SetParent(systems); gate.position = CourtC + new Vector3(0f, 0.27f, -CourtR); gate.rotation = Yaw(0);
            lv.reaperGate = gate;
            lv.reaperArena = new Bounds(CourtC + new Vector3(0f, 8f, 0f), new Vector3(46f, 26f, 46f));

            var zone = new LevelRefs.AtmosphereZone
            {
                name = "The Red Moon", bounds = new Bounds(new Vector3(0f, RealmY + 20f, 900f), new Vector3(200f, 90f, 240f)),
                fog = new Color(0.46f, 0.15f, 0.15f), fogDensity = 0.0055f, ambient = new Color(1.15f, 0.72f, 0.74f), exposure = 1.75f,
                overrideSun = true, sunColor = new Color(1f, 0.45f, 0.38f), sunIntensity = 3.2f, sunEuler = new Vector3(24f, 180f, 0f),
            };
            lv.zones = new[] { zone }.Concat(lv.zones ?? new LevelRefs.AtmosphereZone[0]).ToArray();

            string vpath = $"{Gen}/Rendering/RealmVolume.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(vpath) != null) AssetDatabase.DeleteAsset(vpath);
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, vpath);
            var ca = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            ca.contrast.Override(8f); ca.saturation.Override(-4f); ca.postExposure.Override(0.7f); ca.colorFilter.Override(new Color(1f, 0.88f, 0.88f));
            var sp = prof.Add<UnityEngine.Rendering.Universal.SplitToning>(true);
            sp.shadows.Override(new Color(0.3f, 0.32f, 0.55f)); sp.highlights.Override(new Color(0.8f, 0.35f, 0.32f)); sp.balance.Override(-10f);
            var bl = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true); bl.intensity.Override(1.25f); bl.threshold.Override(0.85f); bl.tint.Override(new Color(1f, 0.55f, 0.55f));
            var vg = prof.Add<UnityEngine.Rendering.Universal.Vignette>(true); vg.intensity.Override(0.22f); vg.color.Override(new Color(0.12f, 0f, 0.02f));
            foreach (var cmp in prof.components) { cmp.name = cmp.GetType().Name; AssetDatabase.AddObjectToAsset(cmp, prof); }
            EditorUtility.SetDirty(prof);
            var vgo = new GameObject("RealmGrade"); vgo.transform.SetParent(systems, false); vgo.transform.position = new Vector3(0f, RealmY + 20f, 900f);
            var box = vgo.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(200f, 90f, 240f);
            vgo.layer = 2;
            var vol = vgo.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = 12f; vol.blendDistance = 6f; vol.sharedProfile = prof;
        }
    }
}
