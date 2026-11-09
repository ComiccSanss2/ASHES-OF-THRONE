using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DarkFantasy.UI
{
    /// <summary>
    /// The inventory's character preview: a copy of the local player's model (with the weapon and
    /// armour they wear right now) standing in a small lit studio far below the world, filmed by its
    /// own camera into a RenderTexture. Drag to turn it, scroll to come closer.
    /// </summary>
    public class InventoryPreview : MonoBehaviour
    {
        public const int PreviewLayer = 30;
        static readonly Vector3 Studio = new Vector3(0f, -800f, 0f);

        public RenderTexture Texture { get; private set; }
        Camera _cam;
        Transform _pivot;
        GameObject _clone;
        PlayerController _source;
        string _signature;
        float _yaw = 200f, _zoom = 1f, _spin;
        bool _active;

        public static InventoryPreview Create(int width, int height)
        {
            var go = new GameObject("InventoryPreview");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<InventoryPreview>();
            p.Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "InventoryPreviewRT", antiAliasing = 2 };
            go.transform.position = Studio;

            var camGo = new GameObject("PreviewCamera"); camGo.transform.SetParent(go.transform, false);
            p._cam = camGo.AddComponent<Camera>();
            p._cam.targetTexture = p.Texture;
            p._cam.clearFlags = CameraClearFlags.SolidColor;
            p._cam.backgroundColor = new Color(0.07f, 0.05f, 0.06f, 1f);
            p._cam.cullingMask = 1 << PreviewLayer;
            p._cam.fieldOfView = 30f;
            p._cam.nearClipPlane = 0.1f; p._cam.farClipPlane = 20f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.renderShadows = false;
            p._cam.enabled = false;

            p._pivot = new GameObject("Pivot").transform; p._pivot.SetParent(go.transform, false);
            void L(Vector3 pos, Color c, float i, float range)
            {
                var lg = new GameObject("Light"); lg.transform.SetParent(go.transform, false); lg.transform.localPosition = pos;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None;
            }
            L(new Vector3(1.4f, 2.2f, 2.0f), new Color(1f, 0.86f, 0.7f), 6f, 8f);     // warm key
            L(new Vector3(-1.8f, 1.6f, 1.2f), new Color(0.55f, 0.6f, 0.9f), 3.5f, 7f); // cool fill
            L(new Vector3(0f, 2.4f, -1.8f), new Color(1f, 0.55f, 0.3f), 5f, 7f);      // ember rim
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(floor.GetComponent<Collider>());
            floor.name = "Plinth"; floor.layer = PreviewLayer;
            floor.transform.SetParent(go.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.05f, 0f); floor.transform.localScale = new Vector3(1.4f, 0.05f, 1.4f);
            var fr = floor.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh) { fr.sharedMaterial = new Material(sh); fr.sharedMaterial.SetColor("_BaseColor", new Color(0.16f, 0.13f, 0.12f)); }
            return p;
        }

        /// <summary>Shows (or refreshes) the preview of this player. Called while the inventory is open.</summary>
        public void Show(PlayerController p)
        {
            _active = p != null;
            _cam.enabled = _active;
            if (!_active) { if (_clone) Destroy(_clone); _clone = null; _source = null; return; }
            string sig = Signature(p);
            if (_clone == null || _source != p || sig != _signature) Rebuild(p, sig);
        }

        public void Hide() { _active = false; _cam.enabled = false; }

        /// <summary>What the model wears: when it changes, the copy is made again.</summary>
        static string Signature(PlayerController p) => p.EquippedWeapon + ":" + (p.armor ? p.armor.Packed : 0);

        void Rebuild(PlayerController p, string sig)
        {
            if (_clone) Destroy(_clone);
            _source = p; _signature = sig;
            var vr = p.anim ? p.anim.visualRoot : null;
            if (vr == null) return;
            _clone = Instantiate(vr.gameObject, _pivot, false);
            _clone.name = "PreviewModel";
            _clone.transform.localPosition = new Vector3(0f, vr.localPosition.y, 0f);
            _clone.transform.localRotation = Quaternion.identity;
            foreach (var c in _clone.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var mb in _clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is FlickerLight)) mb.enabled = false;
            foreach (var t in _clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
            foreach (var tr in _clone.GetComponentsInChildren<TrailRenderer>(true)) tr.emitting = false;
            var an = _clone.GetComponentInChildren<Animator>();
            if (an) { an.enabled = true; an.speed = 1f; an.cullingMode = AnimatorCullingMode.AlwaysAnimate; an.Play(CharacterAnimator.Locomotion, 0, 0f); an.SetFloat("Speed", 0f); }
        }

        public void Drag(float dx) { _yaw -= dx * 0.4f; _spin = 0f; }
        public void Zoom(float d) { _zoom = Mathf.Clamp(_zoom - d * 0.1f, 0.55f, 1.25f); }

        void LateUpdate()
        {
            if (!_active) return;
            if (_source == null) { Show(null); return; }
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            float dist = 4.6f * _zoom;
            float lookY = Mathf.Lerp(1.45f, 1.0f, (_zoom - 0.55f) / 0.7f);
            _cam.transform.localPosition = new Vector3(0f, lookY + 0.15f, dist);
            _cam.transform.LookAt(transform.position + Vector3.up * lookY);
        }

        void OnDestroy() { if (Texture) Texture.Release(); }
    }
}
