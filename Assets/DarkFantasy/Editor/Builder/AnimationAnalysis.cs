using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Analyzes the knight's animation clips by sampling them on the actual rig:
    ///  - finds the real "swing" window of every attack (frames where the blade tip moves fastest),
    ///    so hit windows, sounds and VFX are frame-accurate with the animation;
    ///  - solves the sword grip: the blade direction (in hand_R space) that makes the attacks
    ///    sweep the widest arcs (slashes) instead of poking along the blade axis (stabs).
    /// </summary>
    public static class AnimationAnalysis
    {
        public struct SwingInfo
        {
            public string clip;
            public float length;      // clip length (s)
            public float swingStart;  // blade enters the active arc
            public float swingPeak;   // fastest frame (contact moment)
            public float swingEnd;    // blade leaves the active arc
            public float windupStart; // good offset to start the clip when chaining combos
            public float settle;      // recovery done: safe to blend back to locomotion
            public override string ToString() => $"{clip}: len={length:F2} windup={windupStart:F2} start={swingStart:F2} peak={swingPeak:F2} end={swingEnd:F2} settle={settle:F2}";
        }

        public struct GripInfo { public Vector3 bladeLocal, edgeLocal; public float score, stabScore; }

        public const string KnightFbx = "Assets/DarkFantasy/Art/Knight_GS.fbx";
        const float Fps = 60f;

        public static AnimationClip FindClip(string name) => FindClipIn(KnightFbx, name);

        /// <summary>Clip by action name inside any FBX (Blender takes are named "Armature|Action").</summary>
        public static AnimationClip FindClipIn(string fbxPath, string name)
        {
            // Blender exports takes as "Armature|Action": match the action name at the end
            AnimationClip best = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                {
                    if (c.name == name) return c;
                    if (c.name.EndsWith("|" + name) || c.name.EndsWith(name)) best = c;
                }
            return best;
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        static GameObject SpawnModel(out Transform hand, out Transform hips, out float height)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(KnightFbx);
            var go = (GameObject)Object.Instantiate(fbx);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var idle = FindClip("GS_Idle");
            if (idle) idle.SampleAnimation(go, 0f);
            hand = FindDeep(go.transform, "hand_R");
            hips = FindDeep(go.transform, "Hips");
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            height = smr ? smr.bounds.size.y : 1.8f;
            return go;
        }

        /// <summary>
        /// Samples hand/blade-tip motion. bladeLocal: blade direction in hand space (null = use hand only).
        /// </summary>
        static List<(Vector3 pos, Quaternion rot, Vector3 hips)> Sample(GameObject go, Transform hand, Transform hips, AnimationClip clip)
        {
            var list = new List<(Vector3, Quaternion, Vector3)>();
            int n = Mathf.Max(2, Mathf.CeilToInt(clip.length * Fps));
            var lockBones = InPlaceBones(go.transform);
            var bind = new Vector3[lockBones.Length];
            for (int k = 0; k < lockBones.Length; k++) bind[k] = lockBones[k].localPosition;
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Min(clip.length, i / Fps);
                clip.SampleAnimation(go, t);
                // same in-place correction as CharacterAnimator (no horizontal root drift)
                for (int k = 0; k < lockBones.Length; k++) { var lp = lockBones[k].localPosition; lockBones[k].localPosition = new Vector3(bind[k].x, lp.y, bind[k].z); }
                list.Add((hand.position, hand.rotation, hips ? hips.position : Vector3.zero));
            }
            return list;
        }

        /// <summary>Bones whose horizontal drift is cancelled (armature root and hips).</summary>
        public static Transform[] InPlaceBones(Transform model)
        {
            var l = new List<Transform>();
            // the greatsword clips are authored in place (hips may step); only the armature root is locked
            foreach (var n in new[] { "Rig" }) { var t = FindDeep(model, n); if (t) l.Add(t); }
            return l.ToArray();
        }

        /// <summary>
        /// Reads the sword socket authored with the animations (hand_R/SwordSocket, SocketTip, SocketEdge):
        /// grip position, blade and edge directions in hand_R space.
        /// </summary>
        public static bool ReadSocket(out Vector3 gripLocal, out Vector3 bladeLocal, out Vector3 edgeLocal)
        {
            gripLocal = Vector3.zero; bladeLocal = Vector3.up; edgeLocal = Vector3.forward;
            var go = SpawnModel(out var hand, out _, out _);
            try
            {
                var sock = FindDeep(go.transform, "SwordSocket");
                var tip = FindDeep(go.transform, "SocketTip");
                var edge = FindDeep(go.transform, "SocketEdge");
                if (!hand || !sock || !tip || !edge) { Debug.LogWarning("[DF] SwordSocket not found in " + KnightFbx); return false; }
                gripLocal = hand.InverseTransformPoint(sock.position);
                // metres in hand-rotation space (the hand bone may carry an FBX unit scale)
                GripOriginLocal = Quaternion.Inverse(hand.rotation) * (sock.position - hand.position);
                bladeLocal = hand.InverseTransformDirection(tip.position - sock.position).normalized;
                edgeLocal = Vector3.ProjectOnPlane(hand.InverseTransformDirection(edge.position - sock.position), bladeLocal).normalized;
                return true;
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static Vector3 GripOriginLocal = Vector3.zero;

        public static SwingInfo AnalyzeSwing(string clipName, Vector3 bladeLocal, float bladeLength)
        {
            var info = new SwingInfo { clip = clipName };
            var clip = FindClip(clipName);
            if (clip == null) return info;
            var go = SpawnModel(out var hand, out var hips, out float h);
            try
            {
                var s = Sample(go, hand, hips, clip);
                info.length = clip.length;
                int n = s.Count;
                var speed = new float[n];
                for (int i = 1; i < n; i++)
                {
                    Vector3 tipA = s[i - 1].pos + s[i - 1].rot * (GripOriginLocal + bladeLocal * bladeLength);
                    Vector3 tipB = s[i].pos + s[i].rot * (GripOriginLocal + bladeLocal * bladeLength);
                    speed[i] = (tipB - tipA).magnitude * Fps;
                }
                // smoothing (3 frames)
                var sm = new float[n];
                for (int i = 0; i < n; i++) { float a = 0; int c = 0; for (int k = -2; k <= 2; k++) { int j = i + k; if (j >= 0 && j < n) { a += speed[j]; c++; } } sm[i] = a / c; }
                // ignore the first/last 5% (blend frames)
                int lo = Mathf.FloorToInt(n * 0.05f), hi = Mathf.CeilToInt(n * 0.95f);
                int peak = lo; for (int i = lo; i < hi; i++) if (sm[i] > sm[peak]) peak = i;
                float thr = sm[peak] * 0.42f;
                int st = peak; while (st > lo && sm[st - 1] > thr) st--;
                int en = peak; while (en < hi - 1 && sm[en + 1] > thr) en++;
                // windup: back from swing start until motion was low (quiet anticipation)
                float quiet = sm[peak] * 0.12f;
                int wu = st; while (wu > 0 && sm[wu - 1] > quiet) wu--;
                // settle: forward from swing end until motion is low again
                int se = en; while (se < n - 1 && sm[se + 1] > quiet) se++;
                info.swingStart = st / Fps;
                info.swingPeak = peak / Fps;
                info.swingEnd = en / Fps;
                info.windupStart = Mathf.Max(0f, wu / Fps - 0.05f);
                info.settle = Mathf.Min(clip.length, se / Fps + 0.1f);
            }
            finally { Object.DestroyImmediate(go); }
            return info;
        }

        /// <summary>Searches 600 directions in hand_R space for the most "slashing" blade direction.</summary>
        public static GripInfo SolveGrip(params string[] clipNames)
        {
            var go = SpawnModel(out var hand, out var hips, out float h);
            var best = new GripInfo { score = -1 };
            try
            {
                var samples = new List<List<(Vector3 pos, Quaternion rot, Vector3 hips)>>();
                foreach (var c in clipNames) { var clip = FindClip(c); if (clip) samples.Add(Sample(go, hand, hips, clip)); }
                float L = h * 0.55f;
                const int N = 600;
                for (int k = 0; k < N; k++)
                {
                    // Fibonacci sphere
                    float y = 1f - (k + 0.5f) / N * 2f;
                    float r = Mathf.Sqrt(1f - y * y);
                    float phi = k * 2.39996323f;
                    var a = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
                    float slash = 0f, stab = 0f, body = 0f;
                    Vector3 edgeAcc = Vector3.zero;
                    foreach (var s in samples)
                        for (int i = 1; i < s.Count; i++)
                        {
                            Vector3 b = s[i].rot * a;
                            Vector3 tipA = s[i - 1].pos + s[i - 1].rot * a * L;
                            Vector3 tipB = s[i].pos + b * L;
                            Vector3 v = (tipB - tipA) * Fps;
                            float along = Vector3.Dot(v, b);
                            Vector3 perp = v - b * along;
                            slash += perp.magnitude;
                            stab += Mathf.Abs(along);
                            // edge leads the motion: accumulate in hand space
                            edgeAcc += Quaternion.Inverse(s[i].rot) * perp;
                            // penalize blade passing through the torso
                            Vector3 mid = s[i].pos + b * L * 0.5f;
                            Vector3 toBody = mid - s[i].hips; toBody.y = 0f;
                            if (toBody.magnitude < h * 0.12f) body += 1f;
                        }
                    float score = slash - stab * 0.8f - body * L * 4f;
                    if (score > best.score)
                    {
                        Vector3 edge = Vector3.ProjectOnPlane(edgeAcc, a).normalized;
                        best = new GripInfo { bladeLocal = a, edgeLocal = edge, score = score, stabScore = stab };
                    }
                }
            }
            finally { Object.DestroyImmediate(go); }
            return best;
        }

        public static readonly string[] AttackClips = { "GS_Attack1", "GS_Attack2", "GS_Heavy", "GS_Spin", "GS_Parry", "GS_Roar" };

        [MenuItem("Dark Fantasy/Debug/Analyze Animations", priority = 100)]
        public static void LogAnalysis()
        {
            ReadSocket(out var gp, out var blade, out var edge);
            var sb = new StringBuilder();
            sb.AppendLine($"[DF] Socket: grip={gp:F3} blade={blade:F3} edge={edge:F3}");
            var go = SpawnModel(out _, out _, out float h);
            Object.DestroyImmediate(go);
            foreach (var c in AttackClips) sb.AppendLine("[DF]   " + AnalyzeSwing(c, blade, h * 0.8f));
            // orientation check: blade vs facing (toes direction) for a few clips
            var g2 = SpawnModel(out var hand2, out _, out _);
            try
            {
                var footL = FindDeep(g2.transform, "Foot_L"); var toesL = FindDeep(g2.transform, "Toes_L");
                var sock = FindDeep(g2.transform, "SwordSocket"); var tip = FindDeep(g2.transform, "SocketTip");
                foreach (var cn in new[] { "GS_Idle", "GS_Run", "GS_Heavy" })
                {
                    var clip = FindClip(cn); if (!clip) continue;
                    clip.SampleAnimation(g2, cn == "GS_Heavy" ? 0.9f : 0f);
                    Vector3 face = Vector3.ProjectOnPlane(toesL.position - footL.position, Vector3.up).normalized;
                    Vector3 bl = (tip.position - sock.position).normalized;
                    Vector3 viaHand = hand2.rotation * blade;
                    sb.AppendLine($"[DF]   {cn}: facing={face:F2} blade(socket)={bl:F2} blade(hand*local)={viaHand:F2} dot={Vector3.Dot(face, bl):F2} up={bl.y:F2} sockToHand={(sock.position - hand2.position).magnitude:F3}");
                }
            }
            finally { Object.DestroyImmediate(g2); }
            Debug.Log(sb.ToString());
        }
    }
}

namespace DarkFantasy.EditorTools
{
    public static class AnimationSheet
    {
        /// <summary>Renders a contact sheet: rows = clips, columns = frames, sword drawn with a given grip.</summary>
        public static void Render(string outPath, string[] clips, Vector3 bladeLocal, Vector3 edgeLocal, int cols = 8)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(AnimationAnalysis.KnightFbx);
            var go = (GameObject)Object.Instantiate(fbx);
            go.hideFlags = HideFlags.HideAndDontSave;
            Transform FindDeep(Transform t, string n) { if (t.name == n) return t; foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; } return null; }
            var hand = FindDeep(go.transform, "hand_R");
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            float h = smr.bounds.size.y;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/DarkFantasy/Generated/Meshes/Greatsword.asset");
            var sword = new GameObject("S") { hideFlags = HideFlags.HideAndDontSave };
            sword.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = sword.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(1f, 0.3f, 0.2f) };
            var camGo = new GameObject("C") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.25f, 0.28f);
            cam.fieldOfView = 40f;
            var light = new GameObject("L") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Light>();
            light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, -30, 0); light.intensity = 1.5f;
            int cw = 220, ch = 260;
            var sheet = new Texture2D(cw * cols, ch * clips.Length, TextureFormat.RGB24, false);
            var rt = new RenderTexture(cw, ch, 24);
            cam.targetTexture = rt;
            // lato frontale-destro del personaggio
            bool flip = FindDeep(go.transform, "hand_L").position.x > 0f;
            Vector3 fwd = flip ? Vector3.back : Vector3.forward;
            for (int r = 0; r < clips.Length; r++)
            {
                var clip = AnimationAnalysis.FindClip(clips[r]);
                if (clip == null) continue;
                for (int c = 0; c < cols; c++)
                {
                    float t = clip.length * c / (cols - 1);
                    clip.SampleAnimation(go, t);
                    var rot = hand.rotation * Quaternion.LookRotation(edgeLocal, bladeLocal);
                    sword.transform.SetPositionAndRotation(hand.position, rot);
                    sword.transform.localScale = Vector3.one * (h / 1.85f) * 1.25f;
                    Vector3 center = new Vector3(0, h * 0.55f, 0);
                    cam.transform.position = center + (fwd + Vector3.right * 0.9f).normalized * h * 2.6f + Vector3.up * h * 0.3f;
                    cam.transform.LookAt(center);
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cw, ch), c * cw, (clips.Length - 1 - r) * ch);
                }
            }
            sheet.Apply();
            System.IO.File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(go); Object.DestroyImmediate(sword); Object.DestroyImmediate(camGo); Object.DestroyImmediate(light.gameObject);
        }

        [MenuItem("Dark Fantasy/Debug/Render Attack Sheets", priority = 101)]
        public static void RenderAll()
        {
            string logs = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Logs"));
            string[] clips = { "GS_Idle", "GS_Walk", "GS_Run", "GS_Attack1", "GS_Attack2", "GS_Heavy", "GS_Spin", "GS_Parry" };
            AnimationAnalysis.ReadSocket(out _, out var blade, out var edge);
            Render(System.IO.Path.Combine(logs, "sheet_greatsword.png"), clips, blade, edge);
            Debug.Log("[DF] Sheets rendered");
        }
    }
}
