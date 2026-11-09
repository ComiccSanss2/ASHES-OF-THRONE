using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Moves shared by every class (Tools/moves_anim.py → Art/Moves.fbx, same Solus rig):
    /// MV_Roll (the dodge roll), MV_CrouchIdle / MV_CrouchWalk (Ctrl / C). They play on a "Body"
    /// layer whose mask has the hips, spine, neck, head and legs but not the arms, so each class
    /// keeps holding its own weapon the way its own clips do.
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        const string MovesFbx = "Assets/DarkFantasy/Art/Moves.fbx";

        static void ConfigureMovesImport()
        {
            if (!(AssetImporter.GetAtPath(MovesFbx) is ModelImporter mi)) { Debug.LogError("[DF] Missing " + MovesFbx + " (Tools/moves_anim.py)"); return; }
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = c.name.Contains("Crouch");
                c.loopPose = false;
                c.lockRootRotation = false; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();

            // sanity: the clip's curves must address the class models' bones (same paths)
            var roll = AnimationAnalysis.FindClipIn(MovesFbx, "MV_Roll");
            var knight = AssetDatabase.LoadAssetAtPath<GameObject>(KnightFbx);
            if (roll && knight)
            {
                var paths = new HashSet<string>(knight.GetComponentsInChildren<Transform>(true).Select(t => AnimationUtility.CalculateTransformPath(t, knight.transform)));
                var b = AnimationUtility.GetCurveBindings(roll);
                int hit = b.Count(x => paths.Contains(x.path));
                Debug.Log($"[DF] Moves.fbx: MV_Roll {b.Length} curves, {hit} match the knight's bones · clip e.g. '{(b.Length > 0 ? b[b.Length / 2].path : "")}' · knight e.g. '{paths.FirstOrDefault(x => x.EndsWith("Spine"))}'");
            }
        }

        /// <summary>
        /// The move clip retargeted onto this model's hierarchy: every curve is re-addressed by bone
        /// name (the last path segment), so the Blender armature's root naming does not matter.
        /// Saved as Generated/Animation/{name}_{model}.anim.
        /// </summary>
        static AnimationClip MoveClip(string name, string fbx)
        {
            var src = AnimationAnalysis.FindClipIn(MovesFbx, name);
            if (src == null) { Debug.LogWarning("[DF] Moves clip not found: " + name); return null; }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null) return src;
            var byBone = new Dictionary<string, string>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                string p = AnimationUtility.CalculateTransformPath(t, model.transform);
                if (!byBone.ContainsKey(t.name)) byBone[t.name] = p;
            }
            var dst = new AnimationClip { name = name, frameRate = src.frameRate, legacy = false };
            int mapped = 0, total = 0;
            var bindings = AnimationUtility.GetCurveBindings(src);
            string Bone(string path) => path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
            // units: the clip's spine offset vs the model's (Blender metres vs the FBX's own units)
            float unit = 1f;
            var spineT = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Spine");
            float clipSpine = 0f;
            foreach (var axis in new[] { "x", "y", "z" })
            {
                var sb = bindings.FirstOrDefault(x => Bone(x.path) == "Spine" && x.path.Contains("/") && x.propertyName == "m_LocalPosition." + axis);
                if (sb.path != null) { float v = AnimationUtility.GetEditorCurve(src, sb).Evaluate(0f); clipSpine += v * v; }
            }
            clipSpine = Mathf.Sqrt(clipSpine);
            if (spineT && clipSpine > 1e-5f) unit = spineT.localPosition.magnitude / clipSpine;
            foreach (var b in bindings)
            {
                total++;
                if (!b.path.Contains("/")) continue;                         // the armature object itself
                string bone = Bone(b.path);
                if (!byBone.TryGetValue(bone, out var path)) continue;
                bool rot = b.propertyName.StartsWith("m_LocalRotation");
                bool hipsPos = bone == "Hips" && b.propertyName.StartsWith("m_LocalPosition");
                if (!rot && !hipsPos) continue;                               // bone lengths stay the model's own
                var curve = AnimationUtility.GetEditorCurve(src, b);
                if (hipsPos && Mathf.Abs(unit - 1f) > 1e-3f)
                {
                    var keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++) { keys[k].value *= unit; keys[k].inTangent *= unit; keys[k].outTangent *= unit; }
                    curve.keys = keys;
                }
                var nb = b; nb.path = path;
                AnimationUtility.SetEditorCurve(dst, nb, curve);
                mapped++;
            }
            Debug.Log($"[DF] {name}: unit scale {unit:0.###}");
            var settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.loopTime = name.Contains("Crouch");
            AnimationUtility.SetAnimationClipSettings(dst, settings);
            string outPath = $"{Gen}/Animation/{name}_{System.IO.Path.GetFileNameWithoutExtension(fbx)}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath) != null) AssetDatabase.DeleteAsset(outPath);
            AssetDatabase.CreateAsset(dst, outPath);
            Debug.Log($"[DF] {name} → {System.IO.Path.GetFileName(fbx)}: {mapped}/{total} curves mapped");
            return dst;
        }

        static AvatarMask BuildBodyMask(string fbx, string name)
        {
            string path = $"{Gen}/Animation/{name}.mask";
            if (AssetDatabase.LoadAssetAtPath<AvatarMask>(path) != null) AssetDatabase.DeleteAsset(path);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var mask = new AvatarMask();
            var all = model.GetComponentsInChildren<Transform>(true);
            mask.transformCount = all.Length;
            string[] arms = { "Shoulder_", "Arm_", "hand_", "Index", "Middle", "Ring", "Pinky", "Thumb", "Socket", "Bow", "Nock", "Staff", "Scythe", "String" };
            for (int i = 0; i < all.Length; i++)
            {
                string p = AnimationUtility.CalculateTransformPath(all[i], model.transform);
                bool on = p.Contains("Hips") && !arms.Any(a => p.Contains(a));
                mask.SetTransformPath(i, p);
                mask.SetTransformActive(i, on);
            }
            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }

        /// <summary>Adds the "Body" layer (crouch locomotion + roll) to a class controller.</summary>
        static void AddBodyLayer(AnimatorController ctrl, string fbx, string maskName)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MovesFbx) == null) return;
            if (!ctrl.parameters.Any(p => p.name == "RollMul"))
            {
                ctrl.AddParameter("RollMul", AnimatorControllerParameterType.Float);
                var ps = ctrl.parameters; ps[ps.Length - 1].defaultFloat = 1f; ctrl.parameters = ps;
            }
            ctrl.AddLayer("Body");
            var layers = ctrl.layers;
            int li = layers.Length - 1;
            layers[li].avatarMask = BuildBodyMask(fbx, maskName);
            layers[li].defaultWeight = 0f;
            layers[li].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var sm = ctrl.layers[li].stateMachine;
            sm.defaultState = sm.AddState(CharacterAnimator.BodyEmpty);
            var crouch = ctrl.CreateBlendTreeInController(CharacterAnimator.CrouchLoco, out BlendTree tree, li);
            tree.blendParameter = "Speed";
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.AddChild(MoveClip("MV_CrouchIdle", fbx), 0f);
            tree.AddChild(MoveClip("MV_CrouchWalk", fbx), 1.9f);
            var ch = tree.children; ch[1].timeScale = 1.1f; tree.children = ch;
            crouch.speedParameterActive = true;
            crouch.speedParameter = "LocoMul";
            var roll = sm.AddState(CharacterAnimator.RollBody);
            roll.motion = MoveClip("MV_Roll", fbx);
            roll.speedParameterActive = true;
            roll.speedParameter = "RollMul";
            EditorUtility.SetDirty(ctrl);
        }
    }
}
