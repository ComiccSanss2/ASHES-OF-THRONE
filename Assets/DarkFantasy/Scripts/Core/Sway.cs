using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Slow pendulum swing (hanging chains, cages, lanterns, banners in the wind).</summary>
    public class Sway : MonoBehaviour
    {
        public float amplitude = 2f;      // degrees
        public float period = 4.5f;       // seconds
        Quaternion _rest;
        float _phase;

        void Awake()
        {
            _rest = transform.localRotation;
            _phase = (transform.position.x * 1.7f + transform.position.z * 0.9f) % 6.28f;
        }

        void Update()
        {
            float t = Time.time * Mathf.PI * 2f / period + _phase;
            transform.localRotation = _rest * Quaternion.Euler(Mathf.Sin(t) * amplitude, 0f, Mathf.Sin(t * 0.73f + 1.3f) * amplitude * 0.7f);
        }
    }
}
