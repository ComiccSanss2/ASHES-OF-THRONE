using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// The Bard class: hooded robe + staff on the Solus rig (Tools/bard_anim.py → Art/Bard.fbx),
    /// BD_* clips, spells (BardCaster), spell icons and particle materials.
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const string BardFbx = "Assets/DarkFantasy/Art/Bard.fbx";
        const string BardTex = "Assets/DarkFantasy/Art/Bard_Base_Color.png";
        const string BardEmission = "Assets/DarkFantasy/Art/Bard_Emission.png";
        const string BardIcons = "Assets/DarkFantasy/Art/Bard";

        // Blender timeline (Tools/bard_anim.py, 30 fps)
        const float BardCastRelease = 13f / 30f, BardCastEnd = 30f / 30f;
        const float BardSpellRelease = 20f / 30f, BardSpellEnd = 40f / 30f;
        const float BardHexRelease = 15f / 30f, BardHexEnd = 34f / 30f;

        static Sprite[] _spellSprites;

        static AnimationClip BardClip(string name)
        {
            var c = AnimationAnalysis.FindClipIn(BardFbx, name);
            if (c == null) Debug.LogWarning("[DF] Bard clip not found: " + name);
            return c;
        }

        static void ConfigureBardImport()
        {
            if (!(AssetImporter.GetAtPath(BardFbx) is ModelImporter mi)) { Debug.LogError("[DF] Missing " + BardFbx + " (run Tools/bard_anim.py in Blender)"); return; }
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations.ToList();
            ModelImporterClipAnimation cast = null;
            foreach (var c in clips)
            {
                c.loopTime = c.name.Contains("BD_Idle") || c.name.Contains("BD_Walk") || c.name.Contains("BD_Run");
                c.loopPose = false;
                c.lockRootRotation = false; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
                if (c.name.EndsWith("BD_Cast")) cast = c;
            }
            if (cast != null)
                clips.Add(new ModelImporterClipAnimation
                {
                    name = "BD_Ready", takeName = cast.takeName, firstFrame = 14f, lastFrame = 15f, loopTime = true,
                    lockRootRotation = false, lockRootHeightY = false, lockRootPositionXZ = false
                });
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();
        }

        static void BuildBardMaterials()
        {
            foreach (var path in new[] { BardTex, BardEmission })
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.maxTextureSize = 512;
                    ti.filterMode = FilterMode.Point;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(BardTex);
            var em = AssetDatabase.LoadAssetAtPath<Texture2D>(BardEmission);
            if (tex == null) Debug.LogWarning("[DF] Missing " + BardTex + " (Tools/bard_texture.py)");
            var m = LitMat("BardPlayer", tex, new Color(0.9f, 0.87f, 0.84f));
            if (em != null)
            {
                m.SetTexture("_EmissionMap", em);
                m.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(m);
            }

            // spell icons (sprites) and particle textures
            _spellSprites = new Sprite[4];
            string[] names = { "spell_hymn", "spell_anthem", "spell_dirge", "spell_crescendo" };
            for (int i = 0; i < names.Length; i++)
            {
                string p = $"{BardIcons}/{names[i]}.png";
                if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.spriteImportMode = SpriteImportMode.Single;
                    ti.mipmapEnabled = false;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.alphaIsTransparency = true;
                    ti.SaveAndReimport();
                }
                _spellSprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (_spellSprites[i] == null) Debug.LogWarning("[DF] Missing spell icon " + p);
            }
            string notePath = $"{BardIcons}/fx_note.png";
            if (AssetImporter.GetAtPath(notePath) is TextureImporter nti)
            {
                nti.alphaIsTransparency = true; nti.mipmapEnabled = true; nti.textureCompression = TextureImporterCompression.Uncompressed;
                nti.wrapMode = TextureWrapMode.Clamp;
                nti.SaveAndReimport();
            }
            var note = AssetDatabase.LoadAssetAtPath<Texture2D>(notePath);
            string ringPath = $"{BardIcons}/fx_ring.png";
            if (AssetImporter.GetAtPath(ringPath) is TextureImporter rti)
            {
                rti.alphaIsTransparency = true; rti.mipmapEnabled = true; rti.textureCompression = TextureImporterCompression.Uncompressed;
                rti.wrapMode = TextureWrapMode.Clamp;
                rti.SaveAndReimport();
            }
            var ring = AssetDatabase.LoadAssetAtPath<Texture2D>(ringPath);
            var dot = M.ContainsKey("ParticleAdd") ? M["ParticleAdd"].GetTexture("_BaseMap") : null;
            TransparentMat("SpellNote", note, Color.white, true, "Universal Render Pipeline/Particles/Unlit");
            TransparentMat("SpellRing", ring, Color.white, true, "Universal Render Pipeline/Particles/Unlit");
            TransparentMat("SpellGlow", dot, Color.white, true, "Universal Render Pipeline/Particles/Unlit");
        }

        static AnimatorController BuildBardAnimator()
        {
            string path = $"{Gen}/Animation/Bard.controller";
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
            tree.AddChild(BardClip("BD_Idle"), 0f);
            tree.AddChild(BardClip("BD_Walk"), 1.9f);
            tree.AddChild(BardClip("BD_Run"), 4.1f);
            tree.AddChild(BardClip("BD_Run"), 6.5f);
            var ch = tree.children; ch[1].timeScale = 1.1f; ch[3].timeScale = 1.45f; tree.children = ch;
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoMul";
            sm.defaultState = loco;

            void OneShot(string state, string clip, bool returnToLoco = true, float exitTime = 0.98f)
            {
                var st = sm.AddState(state);
                st.motion = BardClip(clip);
                st.writeDefaultValues = true;
                if (!returnToLoco) return;
                var t = st.AddTransition(loco);
                t.hasExitTime = true; t.exitTime = exitTime; t.duration = 0.2f;
            }
            OneShot(CharacterAnimator.Attack1, "BD_Cast");        // arcane note
            OneShot(CharacterAnimator.Attack2, "BD_Hex");         // dirge of ruin
            OneShot(CharacterAnimator.Heavy, "BD_Spell");         // hymn / anthem / crescendo
            OneShot(CharacterAnimator.Spin, "BD_Hex");            // triad (heavy)
            OneShot(CharacterAnimator.Parry, "BD_Idle", true, 0.5f);
            OneShot(CharacterAnimator.Hurt, "knight_hurt_left", true, 0.92f);
            OneShot(CharacterAnimator.Knockdown, "knight_front_attacked_fall", true, 0.98f);
            OneShot(CharacterAnimator.Death, "knight_front_attacked_fall", false);
            OneShot(CharacterAnimator.Taunt, "BD_Idle", true, 0.5f);

            var mask = BuildUpperBodyMask(BardFbx, "BardUpperBody");
            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            layers[1].avatarMask = mask; layers[1].defaultWeight = 0f; layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var upper = ctrl.layers[1].stateMachine;
            upper.defaultState = upper.AddState(CharacterAnimator.EmptyUpper);
            upper.AddState(CharacterAnimator.BlockUpper).motion = BardClip("BD_Ready");
            var react = upper.AddState(CharacterAnimator.HitReactUpper);
            react.motion = BardClip("knight_hurt_left");
            react.speed = 1.4f;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static AttackData BardAttack(string name, string state, float speed, float release, float end, float cancel, float dmg = 0f, float poise = 0f, float stamina = 0f, float kb = 0f)
            => new AttackData
            {
                name = name, state = state, animSpeed = speed, startTime = 0f, chainStartTime = -1f,
                hitStart = release, hitEnd = release, swingPeak = release,
                comboWindowStart = end, cancelTime = cancel, endTime = end,
                damage = dmg, poiseDamage = poise, staminaCost = stamina, knockback = kb, lunge = 0f
            };

        static void BuildBardPrefab(Prefabs p, AnimatorController bardCtrl)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BardFbx) == null) { Debug.LogError("[DF] Bard.fbx missing: the Bard class is not built"); return; }
            var root = BuildCharacter("PlayerBard", M["BardPlayer"], 1.85f, bardCtrl, out var ca, out _, out var lockPoint, BardFbx, "BD_Idle", false);
            root.AddComponent<NetworkObject>();
            ConfigureNT(root.AddComponent<NetworkTransform>(), true);
            var vit = root.AddComponent<PlayerVitals>();
            vit.maxHealth = 105f;
            vit.maxMana = 100f;
            vit.manaRegen = 4.5f;
            var pc = root.AddComponent<PlayerController>();
            pc.buffs = root.AddComponent<PlayerBuffs>();
            pc.playerClass = PlayerClass.Bard;
            pc.anim = ca; pc.weapon = null; pc.vitals = vit; pc.lockPoint = lockPoint;
            pc.armorWeight = 1.05f;
            pc.runSpeed = 4.2f;
            pc.sprintSpeed = 6.3f;
            pc.rollDistance = 4.6f;
            pc.rollStamina = 15f;
            // slower cadence than the archer's quick shot (1.12 s against 0.86 s)
            pc.lightCombo = new[] { BardAttack("Arcane Note", CharacterAnimator.Attack1, 0.85f, BardCastRelease, 0.95f, 0.62f, 13f, 8f, 12f, 1.2f) };
            pc.heavyAttack = BardAttack("Triad", CharacterAnimator.Spin, 0.9f, BardHexRelease, 1.02f, 0.75f, 9f, 8f, 30f, 1.4f);
            pc.heavyHoldTime = 99f;
            pc.aimedShotStart = 0f;

            var model = root.GetComponentInChildren<Animator>().transform;
            var bc = root.AddComponent<BardCaster>();
            pc.bard = bc;
            bc.pc = pc; bc.vitals = vit;
            bc.staffTip = FindDeep(model, "StaffTip");
            if (!bc.staffTip) Debug.LogError("[DF] Bard.fbx: StaffTip bone missing");
            else
            {
                var lg = new GameObject("CrystalLight"); lg.transform.SetParent(bc.staffTip, false);
                SetWorldScale(lg.transform, 1f);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.5f, 0.82f, 1f); l.range = 3.5f; l.intensity = 1.3f; l.shadows = LightShadows.None;
                bc.crystalLight = l;
            }
            // balance (see README): the bard is the support, his own damage stays below the archer's
            bc.hymnHeal = 0.3f; bc.crescendoDamage = 46f;
            bc.spells = new[]
            {
                new BardCaster.Spell { name = "Mending Hymn", blurb = "Heals the bard and nearby allies (30% of their health).", mana = 35f, cooldown = 14f,
                    cast = BardAttack("Mending Hymn", CharacterAnimator.Heavy, 1.1f, BardSpellRelease, 1.25f, 0.95f) },
                new BardCaster.Spell { name = "War Anthem", blurb = "Allies nearby deal +25% damage, take -18% damage and recover stamina faster for 15 s.", mana = 40f, cooldown = 24f,
                    cast = BardAttack("War Anthem", CharacterAnimator.Heavy, 1.0f, BardSpellRelease, 1.25f, 0.95f) },
                new BardCaster.Spell { name = "Dirge of Ruin", blurb = "A seeking curse: the foe takes +30% damage and moves 30% slower for 12 s.", mana = 25f, cooldown = 14f,
                    cast = BardAttack("Dirge of Ruin", CharacterAnimator.Attack2, 1.1f, BardHexRelease, 1.05f, 0.8f) },
                new BardCaster.Spell { name = "Crescendo", blurb = "A crashing chord: a shockwave at the aimed spot (heavy damage, staggers).", mana = 45f, cooldown = 16f,
                    cast = BardAttack("Crescendo", CharacterAnimator.Heavy, 0.95f, BardSpellRelease, 1.3f, 1.0f) },
            };

            // starting staff ↔ relic staff (both skinned meshes on hand_R)
            var baseStaff = FindDeep(model, "BardStaff");
            var relicStaff = FindDeep(model, "BardStaffRelic");
            if (baseStaff) pc.baseWeaponRenderer = baseStaff.GetComponent<Renderer>();
            if (relicStaff) { pc.relicWeapon = relicStaff.gameObject; relicStaff.gameObject.SetActive(false); }
            pc.relicTrailColor = new Color(1f, 0.6f, 0.25f);

            // the wyrm's mandolin (third skinned mesh) + its embers around the soundhole
            var dragonStaff = FindDeep(model, "BardStaffDragon");
            if (dragonStaff) { pc.dragonWeapon = dragonStaff.gameObject; dragonStaff.gameObject.SetActive(false); }
            if (bc.staffTip) pc.dragonFx = DragonEmbers(bc.staffTip, Vector3.zero, new Vector3(0.12f, 0.12f, 0.2f), 20f, 2.8f);

            // relic / dragon mandolins as static meshes for the chest displays
            void BakeDisplay(Transform t, string meshName)
            {
                if (!t || !t.TryGetComponent(out SkinnedMeshRenderer smr)) return;
                var baked = new Mesh { name = meshName };
                t.gameObject.SetActive(true);
                smr.BakeMesh(baked);
                t.gameObject.SetActive(false);
                var v = baked.vertices;
                var c = baked.bounds.center;
                for (int i = 0; i < v.Length; i++) v[i] = smr.transform.TransformPoint(v[i]) - smr.transform.TransformPoint(c);
                baked.vertices = v; baked.RecalculateBounds(); baked.RecalculateNormals();
                SaveMesh(baked, meshName);
                RelicMaterials[meshName] = new[] { M["BardPlayer"] };
            }
            BakeDisplay(relicStaff, "StaffRelicDisplay");
            BakeDisplay(dragonStaff, "StaffDragonDisplay");
            p.bard = SavePrefab(root, "PlayerBard");
        }
    }
}
