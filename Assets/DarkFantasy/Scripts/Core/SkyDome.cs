using UnityEngine;

namespace DarkFantasy
{
    /// <summary>The sunset dome: follows the camera, fades in/out with the open-air zones.</summary>
    public class SkyDome : MonoBehaviour
    {
        public Renderer dome;
        [Tooltip("Brightness of the painted sky after the exposure compensation.")]
        public float skyGain = 0.9f;
        [Tooltip("Warms the painted sky against the global green-grey grade.")]
        public Color skyWarmth = new Color(1f, 0.95f, 0.92f, 1f);
        public float saturation = 1.15f;
        MaterialPropertyBlock _mpb;
        float _alpha = -1f;

        void Awake() { _mpb = new MaterialPropertyBlock(); SetAlpha(0f); }

        public void SetAlpha(float a)
        {
            if (Mathf.Abs(a - _alpha) < 0.002f) return;
            _alpha = a;
            if (!dome) return;
            dome.enabled = a > 0.001f;
            dome.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Alpha", a);
            dome.SetPropertyBlock(_mpb);
        }

        /// <summary>Development: "q" = log the state, a number = force that render queue.</summary>
        public void DevSky(string a)
        {
            if (a != "q" && dome) dome.material.renderQueue = int.Parse(a);
            Debug.Log($"[DF] sky alpha={_alpha:0.00} enabled={(dome ? dome.enabled : false)} queue={(dome ? dome.sharedMaterial.renderQueue : -1)} tint={Mathf.Pow(2f, -ScreenPresenter.TotalExposure) * skyGain:0.00} exp={ScreenPresenter.TotalExposure:0.00}");
        }

        void LateUpdate()
        {
            var cam = SoulsCamera.Instance;
            if (cam && cam.Cam) transform.position = cam.Cam.transform.position;
            // undo the scene's post exposure on the painted sky (else it saturates to a cream blur and the sun vanishes)
            if (dome && _alpha > 0.001f)
            {
                float k = Mathf.Pow(2f, -ScreenPresenter.TotalExposure) * skyGain;
                dome.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_Saturation", saturation);
                _mpb.SetColor("_Tint", new Color(k * skyWarmth.r, k * skyWarmth.g, k * skyWarmth.b, 1f));
                dome.SetPropertyBlock(_mpb);
            }
        }
    }
}
