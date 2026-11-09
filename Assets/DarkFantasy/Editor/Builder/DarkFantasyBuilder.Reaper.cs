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
    /// The Reaper class (4th): the Solus rig in blackened plate under a hood and a bone mask
    /// (Tools/reaper_anim.py → Art/Reaper.fbx, RP_* clips), the scythes of every tier (built here),
    /// and the class ultimates of the knight, the archer and the reaper (key 1, PlayerUltimate).
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const string ReaperFbx = "Assets/DarkFantasy/Art/Reaper.fbx";
        const string ReaperTex = "Assets/DarkFantasy/Art/Reaper_Base_Color.png";
        const string ReaperEmission = "Assets/DarkFantasy/Art/Reaper_Emission.png";
        const string UltIconDir = "Assets/DarkFantasy/Art/Ult";
        const float F30 = 1f / 30f;

        static Sprite[] _ultSprites;

        static AnimationClip ReaperClip(string name)
        {
            var c = AnimationAnalysis.FindClipIn(ReaperFbx, name);
            if (c == null) Debug.LogWarning("[DF] Reaper clip not found: " + name);
            return c;
        }

        static void ConfigureReaperImport()
        {
            if (!(AssetImporter.GetAtPath(ReaperFbx) is ModelImporter mi)) { Debug.LogError("[DF] Missing " + ReaperFbx + " (run Tools/reaper_anim.py in Blender)"); return; }
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = c.name.Contains("RP_Idle") || c.name.Contains("RP_Walk") || c.name.Contains("RP_Run") || c.name.Contains("RP_Block");
                c.loopPose = false;
                c.lockRootRotation = false; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        static void BuildReaperMaterials()
        {
            foreach (var path in new[] { ReaperTex, ReaperEmission })
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.maxTextureSize = 512; ti.filterMode = FilterMode.Point;
                    ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ReaperTex);
            var em = AssetDatabase.LoadAssetAtPath<Texture2D>(ReaperEmission);
            if (tex == null) Debug.LogWarning("[DF] Missing " + ReaperTex + " (Tools/reaper_texture.py)");
            var m = LitMat("ReaperPlayer", tex, new Color(0.92f, 0.9f, 0.95f));
            if (em != null)
            {
                m.SetTexture("_EmissionMap", em);
                m.SetColor("_EmissionColor", new Color(2f, 2f, 2f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(m);
            }
            // scythe parts
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Gen}/Textures/rotten_wood.png");
            var steel = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Gen}/Textures/steel.png");
            LitMat("ScytheSnath", wood, new Color(0.42f, 0.34f, 0.28f));
            var bl = LitMat("ScytheBlade", steel, new Color(0.78f, 0.8f, 0.85f)); bl.SetFloat("_Smoothness", 0.6f);
            LitMat("ScytheIron", steel, new Color(0.24f, 0.23f, 0.25f));
            LitMat("ScytheSoul", null, new Color(0.7f, 0.45f, 1f), emission: new Color(1.4f, 0.7f, 2.4f));

            // ultimate icons (sprites)
            _ultSprites = new Sprite[3];
            string[] names = { "ult_knight", "ult_archer", "ult_reaper" };
            for (int i = 0; i < 3; i++)
            {
                string p = $"{UltIconDir}/{names[i]}.png";
                if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                {
                    ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                    ti.mipmapEnabled = false; ti.filterMode = FilterMode.Bilinear;
                    ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true;
                    ti.SaveAndReimport();
                }
                _ultSprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (_ultSprites[i] == null) Debug.LogWarning("[DF] Missing ultimate icon " + p);
            }
        }

        // ================================================================== animator
        // Blender timeline (Tools/reaper_anim.py, 30 fps)
        static readonly CharacterAnimator.SwingWindow[] ReaperSwings =
        {
            new CharacterAnimator.SwingWindow { state = CharacterAnimator.Attack1, start = 6 * F30, peak = 11 * F30, end = 16 * F30 },
            new CharacterAnimator.SwingWindow { state = CharacterAnimator.Attack2, start = 7 * F30, peak = 11 * F30, end = 15 * F30 },
            new CharacterAnimator.SwingWindow { state = CharacterAnimator.Spin, start = 6 * F30, peak = 14 * F30, end = 22 * F30 },
            new CharacterAnimator.SwingWindow { state = CharacterAnimator.Heavy, start = 16 * F30, peak = 20 * F30, end = 24 * F30 },
            new CharacterAnimator.SwingWindow { state = CharacterAnimator.Ultimate, start = 8 * F30, peak = 48 * F30, end = 52 * F30 },
        };

        static AnimatorController BuildReaperAnimator()
        {
            string path = $"{Gen}/Animation/Reaper.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("LocoMul", AnimatorControllerParameterType.Float);
            var ps = ctrl.parameters; ps[1].defaultFloat = 1f; ctrl.parameters = ps;

            var sm = ctrl.layers[0].stateMachine;
            var loco = ctrl.CreateBlendTreeInController(CharacterAnimator.Locomotion, out BlendTree tree, 0);
            tree.blendParameter = "Speed";
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.AddChild(ReaperClip("RP_Idle"), 0f);
            tree.AddChild(ReaperClip("RP_Walk"), 1.9f);
            tree.AddChild(ReaperClip("RP_Run"), 4.4f);
            tree.AddChild(ReaperClip("RP_Run"), 7f);
            var ch = tree.children; ch[1].timeScale = 1.15f; ch[3].timeScale = 1.45f; tree.children = ch;
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoMul";
            sm.defaultState = loco;

            void OneShot(string state, string clip, bool returnToLoco = true, float exitTime = 0.98f)
            {
                var st = sm.AddState(state);
                st.motion = ReaperClip(clip);
                st.writeDefaultValues = true;
                if (!returnToLoco) return;
                var t = st.AddTransition(loco);
                t.hasExitTime = true; t.exitTime = exitTime; t.duration = 0.2f;
            }
            OneShot(CharacterAnimator.Attack1, "RP_Attack1", true, 0.99f);
            OneShot(CharacterAnimator.Attack2, "RP_Attack2", true, 0.99f);
            OneShot(CharacterAnimator.Spin, "RP_Attack3", true, 0.99f);
            OneShot(CharacterAnimator.Heavy, "RP_Heavy", true, 0.99f);
            OneShot(CharacterAnimator.Ultimate, "RP_Ult", true, 0.99f);
            OneShot(CharacterAnimator.Parry, "RP_Parry", true, 0.9f);
            OneShot(CharacterAnimator.Hurt, "knight_hurt_left");
            OneShot(CharacterAnimator.Knockdown, "knight_front_attacked_fall", true, 0.98f);
            OneShot(CharacterAnimator.Death, "knight_front_attacked_fall", false);
            OneShot(CharacterAnimator.Taunt, "RP_Roar", true, 0.95f);

            var mask = BuildUpperBodyMask(ReaperFbx, "ReaperUpperBody");
            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            layers[1].avatarMask = mask; layers[1].defaultWeight = 0f; layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var upper = ctrl.layers[1].stateMachine;
            upper.defaultState = upper.AddState(CharacterAnimator.EmptyUpper);
            upper.AddState(CharacterAnimator.BlockUpper).motion = ReaperClip("RP_Block");
            var react = upper.AddState(CharacterAnimator.HitReactUpper);
            react.motion = ReaperClip("knight_hurt_left");
            react.speed = 1.4f;
            AddBodyLayer(ctrl, ReaperFbx, "ReaperBody");
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>An attack on the reaper's Blender timeline (frames at 30 fps).</summary>
        static AttackData ReaperAttack(string name, string state, float speed, int swingStart, int swingEnd, int peak, int end,
                                       float dmg, float poise, float stamina, float kb, float lunge, int startFrame = 0)
        {
            float s0 = swingStart * F30, s1 = swingEnd * F30;
            return new AttackData
            {
                name = name, state = state, animSpeed = speed, startTime = startFrame * F30, chainStartTime = -1f,
                hitStart = s0 - 0.02f, hitEnd = s1 + 0.03f, swingPeak = peak * F30,
                comboWindowStart = s1 + 0.02f, cancelTime = s1 + 0.12f, endTime = end * F30,
                damage = dmg, poiseDamage = poise, staminaCost = stamina, knockback = kb,
                lunge = lunge, lungeStart = Mathf.Max(0f, s0 - 0.12f), lungeEnd = s1
            };
        }

        // ================================================================== the scythes
        // Mesh frame: origin at the butt, +Y up the snath to the head, the blade reaches +Z, curving down.
        const float SnathLength = 1.52f;

        /// <summary>A curved blade: back curve and cutting edge in the YZ plane, a thick back, a sharp edge.</summary>
        static void ScytheBlade(MeshBuilder mb, float len, float width, float rise, float droop, float thick, bool serrated = false, float hook = 0f)
        {
            int n = 18;
            Vector3 Back(float u) => new Vector3(0f, SnathLength - 0.02f + rise * Mathf.Sin(Mathf.PI * u * 0.8f) - droop * u * u - hook * Mathf.Pow(u, 6f), 0.04f + len * u);
            var backs = new Vector3[n + 1]; var edges = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                Vector3 b = Back(u);
                Vector3 t = (Back(Mathf.Min(1f, u + 0.01f)) - Back(Mathf.Max(0f, u - 0.01f))).normalized;
                Vector3 down = new Vector3(0f, -t.z, t.y);            // inner side (toward the reaper)
                float w = width * Mathf.Pow(1f - u, 0.75f) + 0.004f;
                if (serrated && i % 3 == 1 && u < 0.85f) w *= 1.35f;
                backs[i] = b; edges[i] = b + down * w;
            }
            for (int i = 0; i < n; i++)
            {
                float th0 = thick * (1f - i / (float)n * 0.8f), th1 = thick * (1f - (i + 1) / (float)n * 0.8f);
                Vector3 bl0 = backs[i] + Vector3.left * th0, br0 = backs[i] + Vector3.right * th0;
                Vector3 bl1 = backs[i + 1] + Vector3.left * th1, br1 = backs[i + 1] + Vector3.right * th1;
                Vector3 e0 = edges[i], e1 = edges[i + 1];
                mb.AddQuad(bl0, bl1, e1, e0, Vector3.left);
                mb.AddQuad(br0, e0, e1, br1, Vector3.right);
                mb.AddQuad(bl0, br0, br1, bl1, (backs[i] - edges[i]).normalized);
                if (serrated && i % 3 == 0 && i < n - 3)
                {
                    // teeth on the back (the wyrm's fang)
                    Vector3 up = (backs[i] - edges[i]).normalized;
                    Vector3 tip = backs[i + 1] + up * 0.07f + new Vector3(0f, 0f, 0.03f);
                    mb.AddTri(bl0, tip, br0, up); mb.AddTri(br0, tip, br1, up); mb.AddTri(br1, tip, bl1, up); mb.AddTri(bl1, tip, bl0, up);
                }
            }
            // the blade's root, sitting on the head of the snath
            Vector3 r = backs[0];
            mb.AddQuad(r + Vector3.left * thick, edges[0], edges[0], r + Vector3.right * thick, Vector3.back);
        }

        static Mesh CombineParts(string name, List<(string mat, MeshBuilder mb)> parts)
        {
            var byMat = new Dictionary<string, List<Mesh>>();
            var order = new List<string>();
            foreach (var (mat, mb) in parts)
            {
                if (!byMat.ContainsKey(mat)) { byMat[mat] = new List<Mesh>(); order.Add(mat); }
                byMat[mat].Add(mb.Build(name + "_" + mat));
            }
            var subs = new List<Mesh>();
            foreach (var m in order)
            {
                var c = new Mesh { indexFormat = IndexFormat.UInt32 };
                c.CombineMeshes(byMat[m].Select(x => new CombineInstance { mesh = x, transform = Matrix4x4.identity }).ToArray(), true, false);
                subs.Add(c);
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(subs.Select(x => new CombineInstance { mesh = x, transform = Matrix4x4.identity }).ToArray(), false, false);
            mesh.RecalculateBounds();
            SaveMesh(mesh, name);
            RelicMaterials[name] = order.Select(m => M[m]).ToArray();
            return mesh;
        }

        static void BuildScytheMeshes()
        {
            float L = SnathLength;
            // ---- Gravewarden's Scythe: worn ash-wood snath, iron fittings, a plain long blade
            {
                var snath = new MeshBuilder { uvScale = 2f }; var iron = new MeshBuilder { uvScale = 3f }; var blade = new MeshBuilder { uvScale = 2f };
                snath.AddPrism(Vector3.zero, new Vector3(0f, L, 0f), 0.028f, 0.024f, 7, true, true, 1, 0.02f);
                // the nib (second handle) where the left hand rides
                snath.AddPrism(new Vector3(0f, 0.62f, 0f), new Vector3(0f, 0.66f, 0.13f), 0.016f, 0.014f, 5, true, true, 2, 0f);
                iron.AddPrism(new Vector3(0f, L - 0.08f, 0f), new Vector3(0f, L + 0.04f, 0f), 0.036f, 0.034f, 7, true, true, 3, 0f);
                iron.AddPrism(new Vector3(0f, -0.01f, 0f), new Vector3(0f, 0.05f, 0f), 0.032f, 0.032f, 7, true, true, 4, 0f);
                ScytheBlade(blade, 0.86f, 0.12f, 0.05f, 0.22f, 0.012f);
                CombineParts("ScytheBase", new List<(string, MeshBuilder)> { ("ScytheSnath", snath), ("ScytheIron", iron), ("ScytheBlade", blade) });
            }
            // ---- Widow of the Pyre (relic): blackened snath with gold bands, a skull at the head, an ember edge
            {
                var dark = new MeshBuilder { uvScale = 2f }; var gold = new MeshBuilder { uvScale = 3f }; var blade = new MeshBuilder { uvScale = 2f };
                var bone = new MeshBuilder { uvScale = 3f }; var ember = new MeshBuilder();
                dark.AddPrism(Vector3.zero, new Vector3(0f, L, 0f), 0.03f, 0.026f, 8, true, true, 11, 0.01f);
                for (int i = 0; i < 5; i++) gold.AddPrism(new Vector3(0f, 0.12f + i * 0.3f, 0f), new Vector3(0f, 0.16f + i * 0.3f, 0f), 0.036f, 0.036f, 8, true, true, 12 + i, 0f);
                gold.AddPrism(new Vector3(0f, -0.12f, 0f), new Vector3(0f, 0.02f, 0f), 0.005f, 0.04f, 6, false, true, 18, 0f);   // spike on the butt
                AddSkull(bone, new Vector3(0f, L + 0.02f, -0.02f), 0.13f, Quaternion.Euler(0f, 180f, 0f));
                ScytheBlade(blade, 0.92f, 0.15f, 0.08f, 0.26f, 0.014f);
                ScytheBlade(ember, 0.9f, 0.05f, 0.08f, 0.26f, 0.004f);   // a glowing seam along the edge
                CombineParts("ScytheRelic", new List<(string, MeshBuilder)> { ("RelicDark", dark), ("RelicGold", gold), ("RelicEdge", blade), ("RelicBone", bone), ("RelicEmber", ember) });
            }
            // ---- Wyrmreaver (dragon): a curved fang lashed to a spine-snath, vertebrae and crimson wraps
            {
                var bone = new MeshBuilder { uvScale = 2f }; var crimson = new MeshBuilder { uvScale = 3f }; var fang = new MeshBuilder { uvScale = 2f };
                bone.AddPrism(Vector3.zero, new Vector3(0f, L, 0f), 0.026f, 0.03f, 6, true, true, 21, 0.03f);
                for (int i = 0; i < 9; i++)
                {
                    float y = 0.1f + i * 0.16f;
                    bone.AddPrism(new Vector3(0f, y, 0f), new Vector3(0f, y + 0.05f, 0f), 0.045f, 0.04f, 6, true, true, 22 + i, 0.15f);
                    bone.AddPrism(new Vector3(0f, y + 0.03f, 0f), new Vector3(0f, y + 0.03f, -0.07f), 0.012f, 0.003f, 4, false, false, 40 + i, 0f);   // spinous process
                }
                crimson.AddPrism(new Vector3(0f, 0.55f, 0f), new Vector3(0f, 0.75f, 0f), 0.034f, 0.034f, 7, true, true, 50, 0.08f);
                crimson.AddPrism(new Vector3(0f, L - 0.12f, 0f), new Vector3(0f, L + 0.02f, 0f), 0.04f, 0.036f, 7, true, true, 51, 0.08f);
                ScytheBlade(fang, 0.98f, 0.17f, 0.12f, 0.3f, 0.02f, serrated: true, hook: 0.12f);
                CombineParts("ScytheDragon", new List<(string, MeshBuilder)> { ("RelicBone", bone), ("RelicCrimson", crimson), ("RelicBone", fang) });
            }
            BuildRadiantScythe();
        }

        // ================================================================== ultimates (knight, archer, reaper)
        static PlayerUltimate AddUltimate(GameObject root, PlayerController pc, PlayerUltimate.Kind kind, string name, string blurb, AttackData cast)
        {
            var u = root.AddComponent<PlayerUltimate>();
            u.kind = kind; u.ultName = name; u.blurb = blurb; u.cast = cast; u.pc = pc;
            cast.hyperArmor = true; cast.damage = 0f; cast.staminaCost = 0f; cast.lunge = 0f;
            pc.ult = u;
            return u;
        }

        /// <summary>Knight and archer get their ultimate after their prefabs are saved (prefab contents edit).</summary>
        static void AddClassUltimates(Prefabs p)
        {
            void Edit(GameObject prefab, System.Action<GameObject, PlayerController> edit)
            {
                if (prefab == null) return;
                string path = AssetDatabase.GetAssetPath(prefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                edit(root, root.GetComponent<PlayerController>());
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            Edit(p.player, (root, pc) =>
            {
                var cast = pc.heavyAttack.Clone();
                cast.name = "Cinder Judgement";
                cast.animSpeed = 0.85f;
                ApplySwing(cast, 0f, true);
                AddUltimate(root, pc, PlayerUltimate.Kind.CinderJudgement, "Cinder Judgement",
                    "The greatsword splits the ground: a wall of fire races forward, burning and staggering everything in its path.", cast);
            });
            Edit(p.archer, (root, pc) =>
            {
                var cast = pc.heavyAttack.Clone();
                cast.name = "Storm of Arrows";
                cast.animSpeed = 1.15f; cast.startTime = 0f;
                cast.hitStart = cast.hitEnd = cast.swingPeak = PowerRelease;
                cast.endTime = 1.3f; cast.cancelTime = 1.25f; cast.comboWindowStart = 1.25f;
                AddUltimate(root, pc, PlayerUltimate.Kind.ArrowStorm, "Storm of Arrows",
                    "One arrow loosed at the sky: a rain of arrows falls on the aimed spot for two seconds.", cast);
            });
        }

        // ================================================================== the reaper
        static void BuildReaperPrefab(Prefabs p, AnimatorController ctrl)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ReaperFbx) == null) { Debug.LogError("[DF] Reaper.fbx missing: the Reaper class is not built"); return; }
            BuildScytheMeshes();
            var root = BuildCharacter("PlayerReaper", M["ReaperPlayer"], 1.85f, ctrl, out var ca, out _, out var lockPoint, ReaperFbx, "RP_Idle", false);
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), true);
            var vit = root.AddComponent<PlayerVitals>();
            vit.maxHealth = 112f;                    // lighter than the knight (130)
            var pc = root.AddComponent<PlayerController>();
            pc.buffs = root.AddComponent<PlayerBuffs>();
            pc.playerClass = PlayerClass.Reaper;
            pc.anim = ca; pc.vitals = vit; pc.lockPoint = lockPoint;
            // quick on its feet: faster than the knight, lighter roll
            pc.armorWeight = 0.95f;
            pc.runSpeed = 4.6f;
            pc.sprintSpeed = 6.9f;
            pc.rollDistance = 5.0f;
            pc.rollStamina = 13f;
            // graceful and fast, each cut weaker than the knight's (22/24/30 · heavy 46)
            pc.lightCombo = new[]
            {
                ReaperAttack("Reap",          CharacterAnimator.Attack1, 1.35f, 6, 16, 11, 26, 14f, 11f, 11f, 1.6f, 0.7f),
                ReaperAttack("Rising Crescent", CharacterAnimator.Attack2, 1.4f, 7, 15, 11, 26, 15f, 12f, 12f, 1.8f, 0.6f),
                ReaperAttack("Pirouette",     CharacterAnimator.Spin,    1.25f, 6, 22, 14, 32, 19f, 16f, 15f, 2.6f, 1.0f),
            };
            pc.heavyAttack = ReaperAttack("Gallows Reap", CharacterAnimator.Heavy, 1.15f, 16, 24, 20, 40, 34f, 40f, 24f, 4.5f, 0.9f);
            pc.heavyAttack.hyperArmor = true;
            pc.heavyHoldTime = 13 * F30;
            pc.heavyMaxCharge = 1.0f;
            pc.relicTrailColor = new Color(1f, 0.55f, 0.25f);

            var model = root.GetComponentInChildren<Animator>().transform;
            var socket = FindDeep(model, "ScytheSocket");
            var head = FindDeep(model, "ScytheHead"); var butt = FindDeep(model, "ScytheButt"); var edge = FindDeep(model, "ScytheEdge");
            if (!socket || !head || !butt || !edge) { Debug.LogError("[DF] Reaper.fbx: scythe socket bones missing (ScytheSocket/ScytheHead/ScytheButt/ScytheEdge)"); p.reaper = SavePrefab(root, "PlayerReaper"); return; }
            // the scythe's frame from the sockets (idle pose sampled by BuildCharacter)
            Vector3 up = (head.position - butt.position);
            float len = up.magnitude; up /= len;
            Vector3 fwd = Vector3.ProjectOnPlane(edge.position - socket.position, up).normalized;
            float scale = len / SnathLength;
            var rig = new GameObject("Scythe").transform;
            rig.SetPositionAndRotation(butt.position, Quaternion.LookRotation(fwd, up));
            rig.localScale = Vector3.one * scale;
            rig.SetParent(socket, true);
            GameObject Visual(string mesh, string name)
            {
                var go = RelicObject(mesh, name, rig);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
                return go;
            }
            var baseVis = Visual("ScytheBase", "GravewardenScythe");
            pc.baseWeaponRenderer = baseVis.GetComponent<MeshRenderer>();
            var relic = Visual("ScytheRelic", "WidowOfThePyre"); relic.SetActive(false); pc.relicWeapon = relic;
            var drg = Visual("ScytheDragon", "Wyrmreaver"); drg.SetActive(false); pc.dragonWeapon = drg;
            var rad = Visual("ScytheRadiant", "Dawnsickle"); rad.SetActive(false); pc.radiantWeapon = rad;
            Vector3 bladeMid = new Vector3(0f, SnathLength - 0.12f, 0.45f);
            pc.dragonFx = DragonEmbers(rig, bladeMid, new Vector3(0.05f, 0.25f, 0.8f), 26f, 3.2f);
            pc.radiantFx = RadiantSparkles(rig, bladeMid, new Vector3(0.05f, 0.25f, 0.9f), 24f, 3.2f);

            // hitbox along the blade (head of the snath → blade tip), slash trail between the head and the tip
            var basePt = new GameObject("HitboxBase").transform; basePt.SetParent(rig, false); basePt.localPosition = new Vector3(0f, SnathLength - 0.1f, 0f);
            var tipPt = new GameObject("HitboxTip").transform; tipPt.SetParent(rig, false); tipPt.localPosition = new Vector3(0f, SnathLength - 0.22f, 0.9f);
            var hb = rig.gameObject.AddComponent<WeaponHitbox>();
            hb.basePoint = basePt; hb.tipPoint = tipPt; hb.radius = 0.2f;
            pc.weapon = hb;
            model.gameObject.AddComponent<AnimationEventRelay>().hitbox = hb;
            var trail = rig.gameObject.AddComponent<BladeTrail>();
            var tb = new GameObject("TrailBase").transform; tb.SetParent(rig, false); tb.localPosition = new Vector3(0f, SnathLength - 0.05f, 0.12f);
            trail.basePoint = tb; trail.tipPoint = tipPt;
            trail.material = M["SlashTrail"];
            trail.lifetime = 0.26f;
            trail.color = new Color(0.78f, 0.62f, 1f, 0.8f);
            ca.weaponTrail = trail;
            ca.swings = ReaperSwings;
            ca.swingPitch = 1.25f;

            // ultimate: Harvest of Souls
            var cast = new AttackData
            {
                name = "Harvest of Souls", state = CharacterAnimator.Ultimate, animSpeed = 1f, startTime = 0f,
                hitStart = 48 * F30, hitEnd = 52 * F30, swingPeak = 48 * F30,
                comboWindowStart = 66 * F30, cancelTime = 66 * F30, endTime = 68 * F30,
            };
            var u = AddUltimate(root, pc, PlayerUltimate.Kind.Harvest, "Harvest of Souls",
                "Two whirling turns rend everything around, then a leaping reap crushes the ground. Every soul taken heals the reaper.", cast);
            u.harvestSpinStart = 9 * F30; u.harvestSpinEnd = 36 * F30; u.harvestCrushTime = 49 * F30;

            p.reaper = SavePrefab(root, "PlayerReaper");
        }
    }
}
