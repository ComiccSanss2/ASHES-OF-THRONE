using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DarkFantasy
{
    /// <summary>
    /// "Vibe PS2": la camera di gioco renderizza in una RenderTexture a bassa risoluzione
    /// (filtro Point, niente MSAA) che viene poi mostrata a schermo intero tramite una RawImage
    /// con lo shader VHS_PS2_Screen. Questo approccio non richiede Renderer Feature di URP,
    /// funziona con qualsiasi Renderer (Forward, Forward+, Deferred) e lascia l'HUD nitido
    /// perché viene disegnato da un Canvas separato sopra.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ScreenPresenter : MonoBehaviour
    {
        public static ScreenPresenter Instance { get; private set; }

        [Tooltip("Righe verticali della risoluzione interna (PS2 ~ 448-480).")]
        public int internalHeight = 448;
        public bool lowResEnabled = true;
        public bool vhsEnabled = true;
        public Material vhsMaterial;

        Camera _cam;
        RenderTexture _rt;
        RawImage _image;
        Camera _outputCam;
        int _lastW, _lastH;
        bool _lastLowRes;

        void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            if (vhsMaterial != null) vhsMaterial = new Material(vhsMaterial); // istanza: non sporcare l'asset
            BuildOutput();
        }

        void BuildOutput()
        {
            // Camera "vuota" che pulisce il backbuffer (in URP serve almeno una camera sullo schermo)
            var outGo = new GameObject("ScreenOutputCamera");
            outGo.transform.SetParent(transform.parent, false);
            _outputCam = outGo.AddComponent<Camera>();
            _outputCam.cullingMask = 0;
            _outputCam.clearFlags = CameraClearFlags.SolidColor;
            _outputCam.backgroundColor = Color.black;
            _outputCam.depth = _cam.depth + 10;
            _outputCam.useOcclusionCulling = false;

            var canvasGo = new GameObject("ScreenCanvas");
            canvasGo.transform.SetParent(transform.parent, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1000;
            var imgGo = new GameObject("GameView");
            imgGo.transform.SetParent(canvasGo.transform, false);
            _image = imgGo.AddComponent<RawImage>();
            _image.raycastTarget = false;
            var rt = _image.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            ApplyMaterial();
        }

        // ------------------------------------------------------------------ brightness option
        public static float Brightness { get; private set; } = 1f;
        ColorAdjustments _colorAdj;
        float _baseExposure;
        bool _exposureReady;

        /// <summary>Extra exposure (EV) driven by the level, e.g. inside the throne room.</summary>
        public static float ExtraExposure { get; private set; }
        /// <summary>The post exposure (EV) actually applied: the sky dome divides it back out so the painted sunset is not blown to white.</summary>
        public static float TotalExposure => Instance != null && Instance._colorAdj != null ? Instance._colorAdj.postExposure.value : 0f;
        public static void SetExtraExposure(float ev) { ExtraExposure = ev; if (Instance) Instance.ApplyBrightness(); }

        public static void SetBrightness(float v)
        {
            Brightness = Mathf.Clamp(v, 0.5f, 2f);
            PlayerPrefs.SetFloat("df_bright", Brightness);
            if (Instance) Instance.ApplyBrightness();
        }

        void Start()
        {
            Brightness = PlayerPrefs.GetFloat("df_bright", 1f);
            ApplyBrightness();
        }

        void ApplyBrightness()
        {
            if (!_exposureReady)
            {
                foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                    if (v.isGlobal && v.profile != null && v.profile.TryGet(out _colorAdj)) break;
                if (_colorAdj == null) return;
                _baseExposure = _colorAdj.postExposure.value;
                _exposureReady = true;
            }
            _colorAdj.postExposure.Override(_baseExposure + ExtraExposure + Mathf.Log(Brightness, 2f) * 1.6f);
        }

        public void SetVhs(bool on) { vhsEnabled = on; ApplyMaterial(); }
        /// <summary>Development: "0"/"1" toggles the VHS screen filter (bridge tests).</summary>
        public void DevVhs(string on) => SetVhs(on == "1");
        public void SetLowRes(bool on) { lowResEnabled = on; }

        void ApplyMaterial()
        {
            if (_image == null) return;
            _image.material = vhsEnabled && vhsMaterial != null ? vhsMaterial : null;
        }

        void Update()
        {
            int sw = Mathf.Max(Screen.width, 16), sh = Mathf.Max(Screen.height, 16);
            if (sw == _lastW && sh == _lastH && _rt != null && _lastLowRes == lowResEnabled) return;
            _lastW = sw; _lastH = sh; _lastLowRes = lowResEnabled;

            int h = lowResEnabled ? Mathf.Min(internalHeight, sh) : sh;
            int w = Mathf.RoundToInt(h * (sw / (float)sh));

            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default)
            {
                name = "PS2_InternalRT",
                filterMode = FilterMode.Point,
                antiAliasing = 1,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
            _cam.targetTexture = _rt;
            _image.texture = _rt;
            if (vhsMaterial != null) vhsMaterial.SetFloat("_ScanlineCount", h * 0.5f);
        }

        void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
