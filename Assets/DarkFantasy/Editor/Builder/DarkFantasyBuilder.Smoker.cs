using DarkFantasy.Procedural;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// The Smoker class: the bard's rig and clips (Art/Bard.fbx), re-dressed: a weathered coat, a wide
    /// hat, and instead of the mandolin a churchwarden pipe as long as a staff (procedural, on the
    /// right hand, its bowl where the staff's tip was). SmokerCaster does the rest.
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        static void BuildSmokerMaterials()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(BardTex);
            var bark = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Gen}/Textures/bark.png");
            LitMat("SmokerPlayer", tex, new Color(0.66f, 0.6f, 0.52f));
            LitMat("SmokerHat", null, new Color(0.13f, 0.11f, 0.1f));
            LitMat("SmokerHatBand", null, new Color(0.38f, 0.26f, 0.16f));
            LitMat("PipeWood", bark, new Color(0.36f, 0.22f, 0.13f));
            LitMat("PipeBrass", null, new Color(0.62f, 0.48f, 0.24f));
            LitMat("PipeRelicBone", null, new Color(0.82f, 0.76f, 0.64f));
            LitMat("PipeRelicGold", null, new Color(0.75f, 0.55f, 0.2f), emission: new Color(0.08f, 0.05f, 0.01f));
            LitMat("PipeDragon", null, new Color(0.08f, 0.06f, 0.06f), emission: new Color(0.25f, 0.04f, 0f));
            LitMat("PipeDragonEmber", null, new Color(1f, 0.35f, 0.1f), emission: new Color(3.2f, 0.7f, 0.1f));
            LitMat("PipeEmber", null, new Color(1f, 0.45f, 0.15f), emission: new Color(2.6f, 0.9f, 0.25f));
        }

        static Transform FindBoneLoose(Transform root, params string[] names)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                foreach (var n in names)
                    if (string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>The pipe along +Y: mouthpiece at y = -0.5, a brass-banded stem, the bowl at the top.</summary>
        static void PipeMeshes(float len)
        {
            var wood = new MeshBuilder { uvScale = 2f };
            wood.AddPrism(new Vector3(0f, -0.4f, 0f), new Vector3(0f, -0.24f, 0f), 0.018f, 0.026f, 8, true, false, 7001, 0f);      // mouthpiece
            wood.AddPrism(new Vector3(0f, -0.24f, 0f), new Vector3(0f, len - 0.32f, 0.02f), 0.03f, 0.036f, 8, false, false, 7002, 0f);   // stem
            wood.AddPrism(new Vector3(0f, len - 0.32f, 0.02f), new Vector3(0f, len - 0.2f, 0.1f), 0.036f, 0.045f, 8, false, false, 7003, 0f);  // the bend
            wood.AddPrism(new Vector3(0f, len - 0.26f, 0.2f), new Vector3(0f, len + 0.12f, 0.22f), 0.13f, 0.17f, 12, true, false, 7004, 0.004f);   // bowl
            wood.AddPrism(new Vector3(0f, len - 0.24f, 0.08f), new Vector3(0f, len - 0.2f, 0.2f), 0.06f, 0.07f, 8, true, true, 7005, 0f);      // shank into the bowl
            SaveMesh(wood.Build("PipeBody"), "PipeBody");
            var band = new MeshBuilder { uvScale = 2f };
            foreach (float y in new[] { -0.24f, 0.15f, len * 0.55f, len - 0.36f })
                band.AddPrism(new Vector3(0f, y - 0.025f, 0f), new Vector3(0f, y + 0.025f, 0f), 0.042f, 0.042f, 8, true, true, 7010, 0f);
            band.AddPrism(new Vector3(0f, len + 0.09f, 0.22f), new Vector3(0f, len + 0.14f, 0.22f), 0.178f, 0.178f, 12, false, false, 7011, 0f);   // the bowl's rim
            SaveMesh(band.Build("PipeBands"), "PipeBands");
            var comb = new Mesh();
            comb.CombineMeshes(new[] { new CombineInstance { mesh = Meshes["PipeBody"], transform = Matrix4x4.identity },
                                       new CombineInstance { mesh = Meshes["PipeBands"], transform = Matrix4x4.identity } }, false, true);
            SaveMesh(comb, "PipeCombined");
            var ember = new MeshBuilder();
            for (int k = 0; k < 12; k++)
            {
                float a0 = k / 12f * Mathf.PI * 2f, a1 = (k + 1) / 12f * Mathf.PI * 2f;
                ember.AddTri(new Vector3(0f, 0.02f, 0f), new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.14f, new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.14f, Vector3.up);
            }
            SaveMesh(ember.Build("PipeEmberDisc"), "PipeEmberDisc");
            var hat = new MeshBuilder { uvScale = 1.5f };
            hat.AddPrism(new Vector3(0f, -0.01f, 0f), new Vector3(0f, 0.02f, 0f), 0.42f, 0.4f, 16, true, true, 7020, 0.012f);       // brim, a little crooked
            hat.AddPrism(new Vector3(0f, 0.02f, 0f), new Vector3(0.01f, 0.2f, -0.02f), 0.17f, 0.15f, 12, false, false, 7021, 0.01f);   // crown
            hat.AddPrism(new Vector3(0.01f, 0.2f, -0.02f), new Vector3(0.03f, 0.27f, -0.05f), 0.15f, 0.05f, 12, false, true, 7022, 0.01f);
            SaveMesh(hat.Build("SmokerHat"), "SmokerHat");
            var hb = new MeshBuilder();
            hb.AddPrism(new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0.07f, 0f), 0.172f, 0.165f, 12, false, false, 7023, 0f);
            SaveMesh(hb.Build("SmokerHatBand"), "SmokerHatBand");
        }

        static GameObject PipeVariant(string name, Transform parent, string wood, string band)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Meshes["PipeCombined"];
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { M[wood], M[band] };
            return go;
        }

        static void BuildSmokerPrefab(Prefabs p, AnimatorController bardCtrl)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BardFbx) == null) { Debug.LogError("[DF] Bard.fbx missing: the Smoker class is not built"); return; }
            BuildSmokerMaterials();
            var root = BuildCharacter("PlayerSmoker", M["SmokerPlayer"], 1.85f, bardCtrl, out var ca, out _, out var lockPoint, BardFbx, "BD_Idle", false);
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), true);
            var vit = root.AddComponent<PlayerVitals>();
            vit.maxHealth = 110f;
            vit.maxMana = 100f;
            vit.manaRegen = 4f;
            var pc = root.AddComponent<PlayerController>();
            pc.buffs = root.AddComponent<PlayerBuffs>();
            pc.playerClass = PlayerClass.Smoker;
            pc.anim = ca; pc.weapon = null; pc.vitals = vit; pc.lockPoint = lockPoint;
            pc.armorWeight = 1.05f;
            pc.runSpeed = 4.1f;
            pc.sprintSpeed = 6.2f;
            pc.rollDistance = 4.6f;
            pc.rollStamina = 15f;
            pc.lightCombo = new[] { BardAttack("Puff", CharacterAnimator.Attack1, 0.85f, BardCastRelease, 1.1f, 0.75f, 9f, 6f, 10f, 1f) };
            pc.heavyAttack = BardAttack("Choking Gust", CharacterAnimator.Spin, 1.0f, BardHexRelease, 1.02f, 0.75f, 0f, 0f, 25f, 0f);
            pc.heavyHoldTime = 99f;
            pc.aimedShotStart = 0f;

            var model = root.GetComponentInChildren<Animator>().transform;
            // the mandolins go: the pipe takes their place
            foreach (var n in new[] { "BardStaff", "BardStaffRelic", "BardStaffDragon" })
            {
                var t = FindDeep(model, n);
                if (t) Object.DestroyImmediate(t.gameObject);
            }
            var hand = FindBoneLoose(model, "hand_R", "Hand_R", "hand.R", "RightHand");
            var tip = FindDeep(model, "StaffTip");
            var head = FindBoneLoose(model, "Head", "head");
            if (!hand || !tip) { Debug.LogError("[DF] Smoker: hand_R / StaffTip missing in Bard.fbx"); Object.DestroyImmediate(root); return; }

            Vector3 a = hand.position, b = tip.position;
            float len = 1.45f;     // longer than the staff: a churchwarden as tall as a man
            Vector3 axis = (b - a).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(root.transform.forward, axis);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, axis);
            PipeMeshes(len);
            var pivot = new GameObject("Pipe").transform;
            pivot.SetPositionAndRotation(a, Quaternion.LookRotation(fwd.normalized, axis));
            pivot.SetParent(hand, true);
            var basePipe = PipeVariant("PipeBase", pivot, "PipeWood", "PipeBrass");
            var relic = PipeVariant("PipeRelic", pivot, "PipeRelicBone", "PipeRelicGold"); relic.SetActive(false);
            var dragon = PipeVariant("PipeDragon", pivot, "PipeDragon", "PipeDragonEmber"); dragon.SetActive(false);
            pc.baseWeaponRenderer = basePipe.GetComponent<Renderer>();
            pc.relicWeapon = relic;
            pc.dragonWeapon = dragon;
            pc.relicTrailColor = new Color(1f, 0.6f, 0.3f);
            // the coal in the bowl (shared by every pipe), its light and the smoke source
            var bowl = new GameObject("Bowl").transform; bowl.SetParent(pivot, false); bowl.localPosition = new Vector3(0f, len + 0.11f, 0.22f);
            var ember = new GameObject("Coal"); ember.transform.SetParent(bowl, false);
            ember.AddComponent<MeshFilter>().sharedMesh = Meshes["PipeEmberDisc"];
            var er = ember.AddComponent<MeshRenderer>(); er.sharedMaterial = M["PipeEmber"]; er.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lg = new GameObject("EmberLight"); lg.transform.SetParent(bowl, false); lg.transform.localPosition = Vector3.up * 0.1f;
            SetWorldScale(lg.transform, 1f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.5f, 0.2f); l.range = 3.2f; l.intensity = 1.1f; l.shadows = LightShadows.None;
            pc.dragonFx = DragonEmbers(bowl, Vector3.zero, new Vector3(0.12f, 0.1f, 0.12f), 16f, 2.6f);

            // a wide, battered hat
            if (head)
            {
                var hat = new GameObject("Hat").transform;
                Vector3 hp = head.position + Vector3.up * 0.17f + root.transform.forward * 0.01f;
                hat.SetPositionAndRotation(hp, Quaternion.Euler(-6f, 0f, 4f));
                hat.SetParent(head, true);
                SetWorldScale(hat, 1f);
                var h1 = new GameObject("Felt"); h1.transform.SetParent(hat, false);
                h1.AddComponent<MeshFilter>().sharedMesh = Meshes["SmokerHat"]; h1.AddComponent<MeshRenderer>().sharedMaterial = M["SmokerHat"];
                var h2 = new GameObject("Band"); h2.transform.SetParent(hat, false);
                h2.AddComponent<MeshFilter>().sharedMesh = Meshes["SmokerHatBand"]; h2.AddComponent<MeshRenderer>().sharedMaterial = M["SmokerHatBand"];
            }

            var sc = root.AddComponent<SmokerCaster>();
            pc.smoker = sc;
            sc.pc = pc; sc.vitals = vit; sc.bowl = bowl; sc.mouth = head; sc.emberLight = l;
            sc.exhaleCast = BardAttack("Exhale", CharacterAnimator.Attack1, 0.75f, BardCastRelease, 1.2f, 0.95f);

            var pose = root.AddComponent<SmokerPose>();
            pose.smoker = sc; pose.animator = model.GetComponent<Animator>(); pose.pipe = pivot; pose.hand = hand; pose.head = head;
            pose.upperArm = FindBoneLoose(model, "Upper_Arm_R"); pose.foreArm = FindBoneLoose(model, "Lower_Arm_R");
            pose.mouthpiece = 0.4f;
            if (!pose.upperArm || !pose.foreArm) Debug.LogError("[DF] Smoker: arm bones not found, no pipe pose");

            var cast = BardAttack("The Great Exhale", CharacterAnimator.Heavy, 0.9f, BardSpellRelease, 1.35f, 1.3f);
            AddUltimate(root, pc, PlayerUltimate.Kind.SmokeColossus, "The Great Exhale",
                "He empties his lungs: a colossus of smoke rises behind him, pounds the nearest foes with its fists for nine seconds and ends with a crash of both.", cast);
            p.smoker = SavePrefab(root, "PlayerSmoker");
        }
    }
}
