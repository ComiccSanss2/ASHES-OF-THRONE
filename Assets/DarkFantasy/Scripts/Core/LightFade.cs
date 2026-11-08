using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Short-lived light that fades out and destroys itself (explosions, eruptions).</summary>
    public class LightFade : MonoBehaviour
    {
        public float duration = 0.4f;
        Light _l; float _start, _t;

        void Awake() { _l = GetComponent<Light>(); }
        public void Begin(float d) { duration = Mathf.Max(0.05f, d); _start = _l ? _l.intensity : 0f; _t = 0f; }

        void Update()
        {
            _t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_t / duration);
            if (_l) _l.intensity = _start * k * k;
            if (_t >= duration) Destroy(gameObject);
        }
    }
}
