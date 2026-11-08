using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Luce tremolante per torce e bracieri.</summary>
    public class FlickerLight : MonoBehaviour
    {
        public float baseIntensity = 2.2f;
        public float amount = 0.35f;
        public float speed = 7f;
        Light _l; float _seed;
        void Awake() { _l = GetComponent<Light>(); _seed = Random.value * 100f; if (_l) baseIntensity = _l.intensity; }
        void Update()
        {
            if (!_l) return;
            float n = Mathf.PerlinNoise(_seed, Time.time * speed) - 0.5f;
            _l.intensity = baseIntensity * (1f + n * amount * 2f);
        }
    }
}
