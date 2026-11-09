using System.Collections.Generic;
using DarkFantasy.Procedural;
using UnityEditor;
using UnityEngine;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Side areas off the main path of each zone, Souls-style: a short tunnel through the ravine wall
    /// opens into a chamber with an item or two, a guardian and a figure with something to tell.
    ///  - Thornwood (west wall): the Rootbound Barrow, Maelis the Rootbound.
    ///  - Thornwood near the temple (east wall): the Glassblower's Grotto, Sister Calyx.
    ///  - The Red Moon (east of the field): the Ossuary of Names, the Ferryman Who Stayed.
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const float BarrowZ = 405f, GrottoZ = 472f;
        static readonly Vector3 OssuaryC = new Vector3(34f, RealmY, 862f);

        // ================================================================== loot
        static GameObject Loot(Transform parent, Vector3 pos, LootPickup.Reward reward, int amount, string itemName, Color glow)
        {
            if (!Meshes.ContainsKey("LootSatchel"))
            {
                var mb = new MeshBuilder { uvScale = 2f };
                mb.AddPrism(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.26f, 0.02f), 0.2f, 0.12f, 7, true, false, 1, 0.04f);
                mb.AddPrism(new Vector3(0f, 0.26f, 0.02f), new Vector3(0f, 0.34f, 0.03f), 0.12f, 0.05f, 7, false, true, 2, 0.02f);
                mb.AddPrism(new Vector3(-0.12f, 0.22f, 0f), new Vector3(0.14f, 0.3f, 0.05f), 0.015f, 0.015f, 4, true, true, 3, 0f);   // the cord
                SaveMesh(mb.Build("LootSatchel"), "LootSatchel");
            }
            var root = new GameObject("Loot_" + itemName.Replace(" ", "")); root.transform.SetParent(parent, false); root.transform.position = pos;
            var vis = Place("Visual", "LootSatchel", "OswinCloth", pos, Yaw(Random.Range(0f, 360f)), root.transform, false, 1.2f, false);
            var lg = new GameObject("Glint"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = glow; l.range = 3f; l.intensity = 1.4f; l.shadows = LightShadows.None;
            Fx.CreateFlame(lg.transform, Vector3.zero, 0.08f, M["ParticleAdd"]);
            var lp = root.AddComponent<LootPickup>(); lp.visual = vis.transform; lp.glow = l; lp.reward = reward; lp.amount = amount; lp.itemName = itemName;
            Dynamic(root);
            return root;
        }

        /// <summary>A corpse slumped against the wall (bones under a rotten cloak), holding what it carried.</summary>
        static void Corpse(Transform parent, Vector3 pos, float yaw)
        {
            if (!Meshes.ContainsKey("SlumpedCorpse"))
            {
                var mb = new MeshBuilder { uvScale = 1.5f };
                mb.AddPrism(new Vector3(0f, 0.05f, -0.15f), new Vector3(0f, 0.7f, -0.25f), 0.3f, 0.22f, 7, true, false, 11, 0.15f);
                mb.AddPrism(new Vector3(0f, 0.7f, -0.25f), new Vector3(0.05f, 0.95f, -0.12f), 0.2f, 0.06f, 7, false, true, 12, 0.1f);
                foreach (int s in new[] { -1, 1 }) mb.AddPrism(new Vector3(s * 0.15f, 0.1f, 0f), new Vector3(s * 0.22f, 0.08f, 0.65f), 0.09f, 0.06f, 5, true, true, 13, 0.08f);
                SaveMesh(mb.Build("SlumpedCorpse"), "SlumpedCorpse");
            }
            Place("Corpse", "SlumpedCorpse", "OswinCloth", pos, Yaw(yaw), parent, false);
            var sk = new MeshBuilder { uvScale = 2f };
            AddSkull(sk, Vector3.zero, 0.2f, Quaternion.Euler(25f, 0f, 10f));
            string n = "CorpseSkull";
            if (!Meshes.ContainsKey(n)) SaveMesh(sk.Build(n), n);
            Place("Skull", n, "MirrorBone", pos + Yaw(yaw) * new Vector3(0.05f, 0.93f, -0.06f), Yaw(yaw), parent, false, 1f, false);
        }

        static void PointLight(Transform parent, Vector3 pos, Color c, float intensity, float range, bool flicker = false)
        {
            var g = new GameObject("Light"); g.transform.SetParent(parent, false); g.transform.position = pos;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            l.intensity *= 2.2f; l.range *= 1.3f;     // side areas read too dark under the fog otherwise
            if (flicker) g.AddComponent<FlickerLight>();
        }

        // ================================================================== Thornwood
        static void BuildThornSideAreas(Transform root, System.Func<float, float, float> R)
        {
            var sec = new GameObject("ThornSideAreas").transform; sec.SetParent(root, false);
            float y = ForestY;

            // ---- WEST: the Rootbound Barrow. Roots break through a burial vault; a seer is grown into the wall.
            var tun = new Room("BarrowTunnel", -46.2f, -43.2f, BarrowZ - 1.6f, BarrowZ + 1.6f, y) { spring = 2.4f, apex = 3.3f, alongX = true, openE = true, openW = true, torchEvery = 0f, wallMat = "StoneCastle" };
            BuildRoom(sec, tun);
            var barrow = new Room("RootboundBarrow", -60f, -46.2f, BarrowZ - 6f, BarrowZ + 6f, y) { spring = 3f, apex = 4.6f, alongX = true, torchEvery = 0f, wallMat = "StoneCastle" };
            barrow.E.Add(new Op(BarrowZ, 3f, 2.4f, 3.3f));
            BuildRoom(sec, barrow);
            Place("BarrowMouth", "TempleGateArch", "ThornBark", new Vector3(-43.3f, y, BarrowZ), Yaw(90f), sec, false, 0.55f);
            // burial niches with the dead along the walls, roots everywhere
            for (int i = 0; i < 4; i++)
                foreach (int s in new[] { -1, 1 })
                {
                    float x = -57f + i * 3f;
                    Corpse(sec, new Vector3(x, y + 0.05f, BarrowZ + s * 5.4f), s > 0 ? 180f : 0f);
                }
            var roots = new MeshBuilder { uvScale = 1f };
            var rnd = new System.Random(4051);
            float Rr(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int i = 0; i < 26; i++)
            {
                Vector3 a = new Vector3(Rr(-59.5f, -47f), y + 4.4f, BarrowZ + Rr(-5.5f, 5.5f));
                Vector3 m = a + new Vector3(Rr(-1.5f, 1.5f), -Rr(1.5f, 3f), Rr(-1.5f, 1.5f));
                Vector3 b = m + new Vector3(Rr(-1f, 1f), -(m.y - y) - 0.1f, Rr(-1f, 1f));
                float r0 = Rr(0.08f, 0.22f);
                roots.AddPrism(a, m, r0, r0 * 0.7f, 5, false, false, 4100 + i, 0.05f);
                if (i % 2 == 0) roots.AddPrism(m, b, r0 * 0.7f, 0.02f, 5, false, true, 4200 + i, 0.05f);
            }
            SaveMesh(roots.Build("BarrowRoots"), "BarrowRoots");
            Place("Roots", "BarrowRoots", "ThornBark", Vector3.zero, Quaternion.identity, sec, false);
            // Maelis: a woman kneeling at the far wall, roots growing out of her back into the stone
            var seer = new GameObject("Maelis"); seer.transform.SetParent(sec, false);
            Vector3 sp = new Vector3(-58.6f, y + 0.05f, BarrowZ); seer.transform.SetPositionAndRotation(sp, Yaw(90f));
            {
                var b = new MeshBuilder { uvScale = 1.5f };
                b.AddPrism(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.55f, 0.05f), 0.42f, 0.3f, 8, true, false, 4301, 0.12f);       // skirts pooled on the floor
                b.AddPrism(new Vector3(0f, 0.55f, 0.05f), new Vector3(0f, 1.05f, 0f), 0.24f, 0.2f, 8, false, false, 4302, 0.05f);   // torso
                b.AddPrism(new Vector3(0f, 1.05f, 0f), new Vector3(0f, 1.42f, 0.06f), 0.16f, 0.05f, 8, false, true, 4303, 0.05f);  // veiled head, bowed
                foreach (int s in new[] { -1, 1 }) b.AddPrism(new Vector3(s * 0.22f, 0.98f, 0f), new Vector3(s * 0.08f, 0.62f, 0.3f), 0.06f, 0.05f, 5, true, true, 4304, 0.03f);  // hands together
                SaveMesh(b.Build("MaelisBody"), "MaelisBody");
                Place("Body", "MaelisBody", "WraithRobe", sp, seer.transform.rotation, seer.transform, false);
                var rr = new MeshBuilder { uvScale = 1f };
                for (int k = 0; k < 7; k++)
                {
                    float a = (k - 3) * 0.35f;
                    Vector3 from = new Vector3(Mathf.Sin(a) * 0.15f, 0.8f + k * 0.07f, -0.15f);
                    Vector3 to = new Vector3(Mathf.Sin(a) * 1.4f, 1.6f + (k % 3) * 0.9f, -1.3f);
                    rr.AddPrism(from, to, 0.07f, 0.03f, 5, false, true, 4310 + k, 0.04f);
                }
                SaveMesh(rr.Build("MaelisRoots"), "MaelisRoots");
                Place("Roots", "MaelisRoots", "ThornBark", sp, seer.transform.rotation, seer.transform, false);
                var eyes = new MeshBuilder();
                foreach (int s in new[] { -1, 1 }) { Vector3 c = new Vector3(s * 0.05f, 1.22f, 0.17f); float h = 0.016f; eyes.AddQuad(c + new Vector3(-h, -h, 0), c + new Vector3(h, -h, 0), c + new Vector3(h, h, 0), c + new Vector3(-h, h, 0), Vector3.forward); }
                SaveMesh(eyes.Build("MaelisEyes"), "MaelisEyes");
                Place("Eyes", "MaelisEyes", "MirrorEyes", sp, seer.transform.rotation, seer.transform, false, 1f, false);
            }
            Lore(seer, "Maelis the Rootbound", "Talk to", 3.4f, LoreNpc.Flourish.Whisper,
                "Mind the roots, little candle. They are listening. They have listened to me for a very long time.",
                "I was the Queen's gardener. When the Mirror first cracked, it wept glass into the ravine, and where the shards fell, my trees drank them. Now they grow crooked, and they remember faces that are not theirs.",
                "I came down here to cut the poisoned roots at their source. They took me instead. It does not hurt. Nothing hurts, once the wood has your name.",
                "The Mirror in the temple is not the Queen, whatever it tells you. It wears her voice the way a crow wears a ring.",
                "Beyond the Mirror there is a red moon, and under it a clock that has stopped at the last hour. Do not let it start again.",
                "Take what the dead left in the niches. They will not need it, and the roots would only grow around it.");
            Loot(sec, new Vector3(-56.4f, y + 0.08f, BarrowZ - 4.3f), LootPickup.Reward.Souls, 650, "Root-Wrapped Souls", new Color(0.7f, 0.55f, 1f));
            Loot(sec, new Vector3(-50f, y + 0.08f, BarrowZ + 4.2f), LootPickup.Reward.VigorTonic, 1, "Vigor Tonic", new Color(0.55f, 1f, 0.5f));
            Flask(sec, new Vector3(-59f, y + 0.1f, BarrowZ - 2.4f));
            PointLight(sec, new Vector3(-57.5f, y + 2.2f, BarrowZ), new Color(0.55f, 0.85f, 0.5f), 4f, 9f, true);
            PointLight(sec, new Vector3(-50f, y + 3f, BarrowZ), new Color(0.6f, 0.45f, 1f), 3.5f, 10f, true);
            PointLight(sec, new Vector3(-44.6f, y + 2.3f, BarrowZ), new Color(0.62f, 0.4f, 1f), 3f, 6f, true);
            Motes(sec, new Vector3(-53f, y + 2f, BarrowZ), new Vector3(12f, 3.5f, 10f), 18f, new Color(0.7f, 1f, 0.6f, 0.8f), new Color(0.6f, 0.5f, 1f, 0.6f));
            _wraiths.Add((new Vector3(-52.5f, y + 0.2f, BarrowZ + 1.5f), 90f));

            // ---- EAST: the Glassblower's Grotto. A furnace that still burns violet; failed mirrors stacked everywhere.
            var tun2 = new Room("GrottoTunnel", 43.2f, 46.2f, GrottoZ - 1.6f, GrottoZ + 1.6f, y) { spring = 2.4f, apex = 3.3f, alongX = true, openE = true, openW = true, torchEvery = 0f, wallMat = "TempleStone" };
            BuildRoom(sec, tun2);
            var grotto = new Room("GlassblowersGrotto", 46.2f, 59f, GrottoZ - 6.5f, GrottoZ + 6.5f, y) { spring = 3.4f, apex = 5.4f, torchEvery = 0f, wallMat = "TempleStone" };
            grotto.W.Add(new Op(GrottoZ, 3f, 2.4f, 3.3f));
            BuildRoom(sec, grotto);
            Place("GrottoMouth", "TempleGateArch", "TempleStone", new Vector3(43.3f, y, GrottoZ), Yaw(-90f), sec, false, 0.55f);
            {   // the furnace: a squat dome of brick with a mouth of violet fire
                var fu = new MeshBuilder { uvScale = 0.8f };
                fu.AddPrism(new Vector3(0f, 0f, 0f), new Vector3(0f, 1.6f, 0f), 1.5f, 1.3f, 10, false, false, 4401, 0.05f);
                fu.AddPrism(new Vector3(0f, 1.6f, 0f), new Vector3(0f, 2.6f, 0f), 1.3f, 0.4f, 10, false, false, 4402, 0.05f);
                fu.AddPrism(new Vector3(0f, 2.6f, 0f), new Vector3(0f, 5.4f, 0f), 0.4f, 0.32f, 8, false, false, 4403, 0.02f);   // flue
                SaveMesh(fu.Build("GlassFurnace"), "GlassFurnace");
                Vector3 fp = new Vector3(56.8f, y, GrottoZ + 3.6f);
                Place("Furnace", "GlassFurnace", "StoneCastle", fp, Yaw(R(0, 360)), sec, true);
                var fire = new GameObject("FurnaceFire"); fire.transform.SetParent(sec, false); fire.transform.position = fp + new Vector3(-1.3f, 0.7f, -0.3f);
                foreach (var ps in Fx.CreateFlame(fire.transform, Vector3.zero, 0.7f, M["ParticleAdd"]).GetComponentsInChildren<ParticleSystem>())
                { var m = ps.main; m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.5f, 1f), new Color(0.45f, 0.25f, 1f)); }
                PointLight(sec, fire.transform.position + Vector3.left * 0.6f, new Color(0.7f, 0.45f, 1f), 7f, 12f, true);
            }
            // racks of failed mirrors: dull panes leaning on the walls, a few cracked, one that still shows something
            for (int i = 0; i < 9; i++)
            {
                bool north = i % 2 == 0; float x = 48f + i * 1.25f;
                Place("FailedMirror", "TemplePane", i % 3 == 0 ? "MirrorSilver" : "TempleGlass", new Vector3(x, y + 0.05f, north ? GrottoZ + 6.2f : GrottoZ - 6.2f),
                    Quaternion.Euler(north ? -12f : 12f, north ? 180f : 0f, R(-4f, 4f)), sec, false, R(0.28f, 0.42f), false);
            }
            for (int i = 0; i < 18; i++)
                Place("GlassShard", "GroundShard" + (i % 4), i % 4 == 0 ? "GlassShardGlow" : "GlassShard", new Vector3(R(47f, 58f), y - 0.1f, GrottoZ + R(-6f, 6f)), Quaternion.Euler(R(-40, 40), R(0, 360), R(-40, 40)), sec, false, R(0.25f, 0.6f), false);
            // Sister Calyx: a tall nun whose hands and face have turned to glass
            var nun = new GameObject("Calyx"); nun.transform.SetParent(sec, false);
            Vector3 np = new Vector3(55.2f, y + 0.05f, GrottoZ - 1.8f); nun.transform.SetPositionAndRotation(np, Yaw(-110f));
            {
                var b = new MeshBuilder { uvScale = 1.2f };
                b.AddPrism(new Vector3(0f, 0f, 0f), new Vector3(0f, 1.45f, 0f), 0.42f, 0.24f, 9, true, false, 4501, 0.04f);
                b.AddPrism(new Vector3(0f, 1.45f, 0f), new Vector3(0f, 1.7f, 0f), 0.26f, 0.2f, 9, false, false, 4502, 0.02f);
                b.AddPrism(new Vector3(0f, 1.62f, -0.06f), new Vector3(0f, 2.12f, -0.12f), 0.24f, 0.1f, 9, false, true, 4503, 0.02f);    // wimple
                b.AddPrism(new Vector3(0f, 2.0f, -0.12f), new Vector3(0f, 0.6f, -0.42f), 0.12f, 0.36f, 8, false, false, 4504, 0.06f);    // veil down the back
                SaveMesh(b.Build("CalyxRobe"), "CalyxRobe");
                Place("Robe", "CalyxRobe", "WraithRobe", np, nun.transform.rotation, nun.transform, false);
                var g = new MeshBuilder();
                g.AddPrism(new Vector3(0f, 1.74f, 0.03f), new Vector3(0f, 1.98f, 0.06f), 0.1f, 0.09f, 7, true, true, 4505, 0.01f);       // the glass face
                foreach (int s in new[] { -1, 1 })
                {
                    g.AddPrism(new Vector3(s * 0.24f, 1.5f, 0f), new Vector3(s * 0.12f, 1.12f, 0.3f), 0.06f, 0.05f, 5, true, false, 4506, 0f);
                    g.AddPrism(new Vector3(s * 0.12f, 1.12f, 0.3f), new Vector3(s * 0.04f, 1.18f, 0.42f), 0.05f, 0.012f, 5, false, true, 4507, 0f);
                }
                g.AddPrism(new Vector3(-0.05f, 1.2f, 0.45f), new Vector3(0.05f, 1.45f, 0.5f), 0.07f, 0.02f, 6, true, true, 4508, 0f);     // a little glass bird in her hands
                SaveMesh(g.Build("CalyxGlass"), "CalyxGlass");
                Place("Glass", "CalyxGlass", "GlassShardGlow", np, nun.transform.rotation, nun.transform, false, 1f, false);
                PointLight(nun.transform, np + nun.transform.rotation * new Vector3(0f, 1.6f, 0.5f), new Color(0.75f, 0.65f, 1f), 1.8f, 4f);
            }
            Lore(nun, "Sister Calyx, the Glassblower", "Talk to", 3.4f, LoreNpc.Flourish.Whisper,
                "Careful. The floor is mostly me, these days. I mean the shards. I blew every one of them.",
                "The temple taught that a perfect mirror shows the soul. Forty years at this furnace and I made a thousand mirrors that showed only faces. Then I made one that showed what you lack.",
                "Queen Ysolde took it as a wedding gift. I warned her. Glass that knows what you lack will always offer to sell it to you.",
                "Its heart is a single shard, colder than the rest. When the Mirror kneels to gather itself, the heart is open. Strike it then, not the frame.",
                "My hands turned first, then my face. Not a curse. A signature. Every glassblower ends up in her work.",
                "There is a draught on the shelf. I distilled it for the pain. You will need it more than I do now.");
            Loot(sec, new Vector3(58.2f, y + 0.08f, GrottoZ - 4.6f), LootPickup.Reward.SoulSalve, 2, "Soul Salve", new Color(0.55f, 0.75f, 1f));
            Loot(sec, new Vector3(48.3f, y + 0.08f, GrottoZ + 4.8f), LootPickup.Reward.Souls, 900, "Glassblower's Wages", new Color(0.9f, 0.8f, 1f));
            PointLight(sec, new Vector3(50f, y + 3.2f, GrottoZ), new Color(0.5f, 0.75f, 1f), 3.5f, 10f);
            PointLight(sec, new Vector3(44.6f, y + 2.3f, GrottoZ), new Color(0.62f, 0.4f, 1f), 3f, 6f, true);
            Motes(sec, new Vector3(52.5f, y + 2.5f, GrottoZ), new Vector3(11f, 4f, 11f), 22f, new Color(0.85f, 0.7f, 1f, 0.9f), new Color(0.5f, 0.9f, 1f, 0.7f));
            _wraiths.Add((new Vector3(50.5f, y + 0.2f, GrottoZ - 2f), -90f));
        }

        // ================================================================== the Red Moon
        /// <summary>
        /// The Ossuary of Names: a crypt half sunk in the ash east of the field, walls stacked with skulls.
        /// The Ferryman sits by a black pool with his oar across his knees; his boat was never needed here.
        /// </summary>
        static void BuildOssuary(Transform root)
        {
            var sec = new GameObject("OssuaryOfNames").transform; sec.SetParent(root, false);
            Vector3 c = OssuaryC; float y = c.y + 0.05f;
            var r = new Room("OssuaryOfNames", c.x - 6f, c.x + 7f, c.z - 6.5f, c.z + 6.5f, y) { spring = 3.6f, apex = 5.6f, torchEvery = 0f, wallMat = "RealmStone", floor = true, floorMat = "RealmGrave" };
            r.W.Add(new Op(c.z, 3.2f, 2.6f, 3.6f));
            BuildRoom(sec, r);
            Place("OssuaryGate", "CourtGateArch", "RealmStone", new Vector3(c.x - 6.4f, y, c.z), Yaw(90f), sec, false, 0.6f);
            for (int k = 0; k < 2; k++)
                Place("GateSkulls", "SkullPile", "MirrorBone", new Vector3(c.x - 7.6f, y, c.z + (k * 2 - 1) * 2.4f), Yaw(k * 90f), sec, false, 1.3f);
            // the walls of names: shelves of skulls, floor to vault
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        float x = c.x - 4f + col * 2.4f;
                        var sk = Place("NameShelf", "SkullPile", "MirrorBone", new Vector3(x, y + 0.1f + row * 1.05f, c.z + side * 6.1f), Yaw(side > 0 ? 180f : 0f), sec, false, 0.85f, false);
                        sk.transform.localScale = new Vector3(1.4f, 0.75f, 0.6f);
                    }
            // a black pool with the souls of the unnamed drifting over it; the ferryman beside it
            {
                var pool = new MeshBuilder();
                for (int k = 0; k < 24; k++)
                {
                    float a0 = k / 24f * Mathf.PI * 2f, a1 = (k + 1) / 24f * Mathf.PI * 2f;
                    pool.AddTri(Vector3.zero, new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * 2.6f, new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * 2.6f, Vector3.up);
                }
                SaveMesh(pool.Build("OssuaryPool"), "OssuaryPool");
                Place("Pool", "OssuaryPool", "MirrorSilver", new Vector3(c.x + 2.5f, y + 0.12f, c.z), Quaternion.identity, sec, false, 1f, false);
                Motes(sec, new Vector3(c.x + 2.5f, y + 1.2f, c.z), new Vector3(4.5f, 1.8f, 4.5f), 26f, new Color(0.6f, 0.75f, 1f, 1f), new Color(1f, 0.4f, 0.35f, 0.8f));
                PointLight(sec, new Vector3(c.x + 2.5f, y + 1.4f, c.z), new Color(0.5f, 0.65f, 1f), 5f, 10f, true);
            }
            var fm = new GameObject("Ferryman"); fm.transform.SetParent(sec, false);
            Vector3 fp = new Vector3(c.x + 5.6f, y, c.z - 1.6f); fm.transform.SetPositionAndRotation(fp, Yaw(-100f));
            {
                var b = new MeshBuilder { uvScale = 1.3f };
                b.AddPrism(new Vector3(0f, 0f, -0.1f), new Vector3(0f, 0.55f, -0.15f), 0.4f, 0.3f, 8, true, false, 4601, 0.15f);    // seated, robe pooled on the step
                b.AddPrism(new Vector3(0f, 0.55f, -0.15f), new Vector3(0f, 1.15f, -0.05f), 0.3f, 0.22f, 8, false, false, 4602, 0.06f);
                b.AddPrism(new Vector3(0f, 1.1f, -0.05f), new Vector3(0f, 1.6f, 0.12f), 0.24f, 0.06f, 8, false, true, 4603, 0.05f);   // hood, bowed
                foreach (int s in new[] { -1, 1 }) b.AddPrism(new Vector3(s * 0.18f, 0.3f, 0.05f), new Vector3(s * 0.2f, 0.45f, 0.55f), 0.11f, 0.09f, 6, true, true, 4604, 0.05f);
                SaveMesh(b.Build("FerrymanBody"), "FerrymanBody");
                Place("Body", "FerrymanBody", "WardenRobe", fp, fm.transform.rotation, fm.transform, false);
                var oar = new MeshBuilder { uvScale = 1f };
                oar.AddPrism(new Vector3(-1.1f, 0.62f, 0.45f), new Vector3(1.2f, 0.58f, 0.5f), 0.035f, 0.035f, 6, true, true, 4605, 0f);
                ProceduralMeshes.Box(oar, new Vector3(1.45f, 0.57f, 0.5f), new Vector3(0.6f, 0.04f, 0.18f), 4606, 0.01f);
                SaveMesh(oar.Build("FerrymanOar"), "FerrymanOar");
                Place("Oar", "FerrymanOar", "ThornBark", fp, fm.transform.rotation, fm.transform, false);
                var mask = new MeshBuilder { uvScale = 2f };
                AddSkull(mask, new Vector3(0f, 1.3f, 0.12f), 0.2f, Quaternion.Euler(20f, 0f, 0f));
                SaveMesh(mask.Build("FerrymanFace"), "FerrymanFace");
                Place("Face", "FerrymanFace", "MirrorBone", fp, fm.transform.rotation, fm.transform, false, 1f, false);
                var lan = new GameObject("Lantern"); lan.transform.SetParent(fm.transform, false); lan.transform.localPosition = new Vector3(-0.6f, 0.3f, 0.3f);
                Fx.CreateFlame(lan.transform, Vector3.zero, 0.15f, M["ParticleAdd"]);
                var ll = lan.AddComponent<Light>(); ll.type = LightType.Point; ll.color = new Color(0.55f, 0.7f, 1f); ll.intensity = 3f; ll.range = 6f; ll.shadows = LightShadows.None;
                lan.AddComponent<FlickerLight>();
            }
            Lore(fm, "The Ferryman Who Stayed", "Talk to", 3.4f, LoreNpc.Flourish.Whisper,
                "You have a pulse. How strange it sounds in here. Sit, if you like. Nobody hurries in the Ossuary.",
                "I rowed the dead across the black water for a thousand years. Then Death himself stopped the clock in the court, and the dead stopped coming. No hour, no ending. My river dried to this.",
                "These skulls are the names he refused. He keeps them so that no one is ever finished. He calls it mercy. The crawlers that pick through the graves out there were people waiting for a boat that never came.",
                "The Wardens carry the last lights of the old crossings. When one lifts its lantern high, step away. The bell it rings is the sound of a soul being turned back.",
                "Death fights like the hand of a clock: wide, patient cuts, then all at once. When he rises into the air and the ground darkens beneath you, run for the edge of the shadow.",
                "Strike him down and the clock moves again. Then I can row once more. Take my coins, I have not needed them in a long while.");
            Loot(sec, new Vector3(c.x + 6.2f, y + 0.05f, c.z + 4.6f), LootPickup.Reward.Souls, 1400, "Ferryman's Coins", new Color(1f, 0.85f, 0.5f));
            Loot(sec, new Vector3(c.x - 3.8f, y + 0.05f, c.z + 4.8f), LootPickup.Reward.IronDraught, 2, "Iron Draught", new Color(1f, 0.55f, 0.4f));
            Flask(sec, new Vector3(c.x - 4.2f, y + 0.1f, c.z - 4.8f));
            PointLight(sec, new Vector3(c.x - 3f, y + 3.5f, c.z), new Color(1f, 0.3f, 0.25f), 3f, 9f, true);
            _shades.Add((new Vector3(c.x - 1f, y + 0.1f, c.z + 2.5f), -90f));
            _shades.Add((new Vector3(c.x - 10f, y + 0.1f, c.z - 3f), -90f));
        }
    }
}
