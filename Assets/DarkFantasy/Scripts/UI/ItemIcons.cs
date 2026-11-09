using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DarkFantasy.UI
{
    /// <summary>
    /// The inventory's item pictures, rendered in the game itself (no baked sprites): a copy of the
    /// weapon / armour piece the local player really carries — or a little bottle for the draughts —
    /// is posed in a dark studio far below the world and filmed, one per frame, into its own small
    /// RenderTexture shown by a RawImage. The copies are plain meshes (skinned parts are baked), so
    /// no script, particle or light of the original comes along.
    /// </summary>
    public class ItemIcons : MonoBehaviour
    {
        public const int IconLayer = 29;
        const int Size = 128;
        static readonly Vector3 Studio = new Vector3(0f, -860f, 0f);
        static ItemIcons _instance;

        Camera _cam;
        readonly Dictionary<string, RenderTexture> _cache = new Dictionary<string, RenderTexture>();
        readonly Queue<(string key, System.Func<GameObject> make, Vector3 view)> _queue = new Queue<(string, System.Func<GameObject>, Vector3)>();
        bool _busy;

        static ItemIcons Instance
        {
            get
            {
                if (_instance) return _instance;
                var go = new GameObject("ItemIcons");
                DontDestroyOnLoad(go);
                go.transform.position = Studio;
                _instance = go.AddComponent<ItemIcons>();
                _instance.Setup();
                return _instance;
            }
        }

        void Setup()
        {
            var camGo = new GameObject("IconCamera"); camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask = 1 << IconLayer;
            _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 30f;
            _cam.allowHDR = false; _cam.allowMSAA = true;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.renderShadows = false;
            _cam.enabled = false;
            void L(Vector3 pos, Color c, float i)
            {
                var lg = new GameObject("IconLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = pos;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = 16f; l.shadows = LightShadows.None;
            }
            L(new Vector3(2.5f, 3f, 4f), new Color(1f, 0.9f, 0.78f), 9f);
            L(new Vector3(-3.5f, 1f, 3f), new Color(0.55f, 0.62f, 1f), 5f);
            L(new Vector3(0f, 2f, -4f), new Color(1f, 0.6f, 0.35f), 4f);
        }

        // ================================================================== public API
        /// <summary>The picture of one of the local player's weapons (null for another class).</summary>
        public static Texture Weapon(PlayerController p, byte tier)
        {
            if (p == null) return null;
            return Instance.Request($"w{(int)p.playerClass}_{tier}", () => Snapshot(p.transform, WeaponRenderers(p, tier)), Vector3.zero);
        }

        /// <summary>The picture of an armour piece as fitted on the local player's body.</summary>
        public static Texture Armor(PlayerController p, byte set, int slot)
        {
            if (p == null || p.armor == null) return null;
            var list = new List<Renderer>();
            foreach (var piece in p.armor.pieces)
                if (piece.set == set && (int)piece.slot == slot && piece.go)
                    list.AddRange(piece.go.GetComponentsInChildren<Renderer>(true));
            if (list.Count == 0) return null;
            return Instance.Request($"a{(int)p.playerClass}_{set}_{slot}", () => Snapshot(p.transform, list), new Vector3(0.35f, 0.18f, 1f));
        }

        static readonly Color[] PotionColors = { new Color(1f, 0.5f, 0.15f), new Color(0.62f, 0.66f, 0.78f), new Color(0.4f, 1f, 0.4f), new Color(1f, 0.88f, 0.55f) };

        /// <summary>0 = Ember Flask, 1..3 = the draughts.</summary>
        public static Texture Potion(int i) => Instance.Request("p" + i, () => Bottle(PotionColors[Mathf.Clamp(i, 0, 3)], i == 0), new Vector3(0.25f, 0.2f, 1f));

        // ================================================================== rendering
        Texture Request(string key, System.Func<GameObject> make, Vector3 view)
        {
            if (_cache.TryGetValue(key, out var rt) && rt) return rt;
            rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Icon_" + key, antiAliasing = 4 };
            rt.Create();
            _cache[key] = rt;
            _queue.Enqueue((key, make, view));
            if (!_busy) StartCoroutine(Process());
            return rt;
        }

        IEnumerator Process()
        {
            _busy = true;
            while (_queue.Count > 0)
            {
                var (key, make, view) = _queue.Dequeue();
                GameObject go = null;
                try { go = make(); } catch (System.Exception e) { Debug.LogWarning("[DF] icon " + key + ": " + e.Message); }
                if (go == null) continue;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                var rends = go.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { Destroy(go); continue; }
                Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
                // weapons are seen flat on (along their thinnest side), armour from the front three-quarter
                Vector3 dir = view;
                if (dir == Vector3.zero)
                {
                    Vector3 s = b.size;
                    dir = s.x <= s.y && s.x <= s.z ? Vector3.right : s.z <= s.y ? Vector3.forward : Vector3.up;
                    dir = (dir + new Vector3(0.12f, 0.1f, 0.12f)).normalized;
                }
                dir.Normalize();
                float ext = b.extents.magnitude;
                _cam.transform.position = b.center + dir * (ext + 4f);
                _cam.transform.rotation = Quaternion.LookRotation(-dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up);
                float half = 0.01f;
                foreach (var r in rends)
                {
                    var bb = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 l = _cam.transform.InverseTransformPoint(c) - _cam.transform.InverseTransformPoint(b.center);
                        half = Mathf.Max(half, Mathf.Abs(l.x), Mathf.Abs(l.y));
                    }
                }
                _cam.orthographicSize = half * 1.1f;
                _cam.farClipPlane = ext * 2f + 8f;
                _cam.targetTexture = _cache[key];
                _cam.enabled = true;
                yield return null;
                yield return new WaitForEndOfFrame();
                _cam.enabled = false;
                _cam.targetTexture = null;
                Destroy(go);
            }
            _busy = false;
        }

        // ================================================================== copies
        static List<Renderer> WeaponRenderers(PlayerController p, byte tier)
        {
            var list = new List<Renderer>();
            if (tier == 0) { if (p.baseWeaponRenderer) list.Add(p.baseWeaponRenderer); return list; }
            var go = tier == 1 ? p.relicWeapon : tier == 2 ? p.dragonWeapon : p.radiantWeapon;
            if (go) list.AddRange(go.GetComponentsInChildren<Renderer>(true));
            return list;
        }

        /// <summary>Plain-mesh copies of the given renderers, kept in their arrangement relative to 'space'.</summary>
        static GameObject Snapshot(Transform space, List<Renderer> renderers)
        {
            var root = new GameObject("IconSubject");
            int n = 0;
            foreach (var r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Mesh mesh = null; bool baked = false;
                if (r is SkinnedMeshRenderer sk)
                {
                    mesh = new Mesh(); sk.BakeMesh(mesh, true); baked = true;
                }
                else if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf) mesh = mf.sharedMesh; }
                if (mesh == null) continue;
                var c = new GameObject("Part");
                c.transform.SetParent(root.transform, false);
                Matrix4x4 m = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Vector3 cx = m.GetColumn(0), cy = m.GetColumn(1), cz = m.GetColumn(2);
                c.transform.localPosition = m.GetColumn(3);
                c.transform.localRotation = Quaternion.LookRotation(cz, cy);
                // a baked mesh already carries the renderer's scale; a mirrored piece keeps its flip
                c.transform.localScale = baked ? Vector3.one : new Vector3(cx.magnitude * (m.determinant < 0f ? -1f : 1f), cy.magnitude, cz.magnitude);
                c.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = c.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.layer = IconLayer;
                n++;
            }
            if (n == 0) { Destroy(root); return null; }
            root.layer = IconLayer;
            return root;
        }

        static Material _glass, _cork;

        /// <summary>A little corked bottle (a round flask for the Ember Flask) of glowing liquid.</summary>
        static GameObject Bottle(Color liquid, bool round)
        {
            var sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (_cork == null && sh) { _cork = new Material(sh); _cork.SetColor("_BaseColor", new Color(0.36f, 0.24f, 0.16f)); }
            var root = new GameObject("IconSubject");
            var mat = new Material(sh);
            mat.SetColor("_BaseColor", liquid);
            mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", liquid * 0.8f);
            GameObject Part(PrimitiveType t, Vector3 pos, Vector3 scale, Material m)
            {
                var g = GameObject.CreatePrimitive(t);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = pos; g.transform.localScale = scale;
                g.GetComponent<MeshRenderer>().sharedMaterial = m;
                g.layer = IconLayer;
                return g;
            }
            if (round) Part(PrimitiveType.Sphere, new Vector3(0f, 0.22f, 0f), new Vector3(0.42f, 0.4f, 0.42f), mat);
            else
            {
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0f), new Vector3(0.3f, 0.17f, 0.3f), mat);
                Part(PrimitiveType.Sphere, new Vector3(0f, 0.36f, 0f), new Vector3(0.3f, 0.16f, 0.3f), mat);
            }
            Part(PrimitiveType.Cylinder, new Vector3(0f, round ? 0.48f : 0.5f, 0f), new Vector3(0.1f, 0.08f, 0.1f), mat);
            Part(PrimitiveType.Cylinder, new Vector3(0f, round ? 0.58f : 0.6f, 0f), new Vector3(0.12f, 0.035f, 0.12f), _cork);
            root.layer = IconLayer;
            return root;
        }
    }
}
