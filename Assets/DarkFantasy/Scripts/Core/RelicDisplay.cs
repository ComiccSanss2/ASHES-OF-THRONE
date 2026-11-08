using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// A relic shown above the opened chest: it rises out of it, turns slowly in a halo of light,
    /// then vanishes into embers (it is now in the players' inventories). Visual only.
    /// </summary>
    public class RelicDisplay : MonoBehaviour
    {
        public float riseHeight = 1.5f;
        public float lifetime = 9f;
        public Light halo;

        Transform _chest;
        float _side, _delay, _t;
        Vector3 _baseScale;

        public void Begin(Transform chest, float sideOffset, float delay)
        {
            _chest = chest; _side = sideOffset; _delay = delay; _t = 0f;
            _baseScale = transform.localScale;
            transform.localScale = Vector3.zero;
        }

        void Update()
        {
            if (_chest == null) { Destroy(gameObject); return; }
            _t += Time.deltaTime;
            float t = Mathf.Max(0f, _t - _delay);
            float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 1.6f), 3f);
            float fade = Mathf.Clamp01((lifetime - t) / 0.8f);
            Vector3 p = _chest.position + _chest.right * _side * rise + Vector3.up * (0.35f + riseHeight * rise + Mathf.Sin(t * 2f) * 0.05f);
            transform.position = p;
            transform.rotation = Quaternion.Euler(0f, t * 55f + _side * 30f, 0f) * Quaternion.Euler(0, 0, 8f);
            float s = Mathf.Clamp01(t * 3f) * fade;
            transform.localScale = _baseScale * s;
            if (halo) halo.intensity = 1.6f * s * (0.85f + 0.15f * Mathf.Sin(t * 5f));
            if (t >= lifetime) { Fx.Spawn(FxId.Embers, p); Destroy(gameObject); }
        }
    }
}
