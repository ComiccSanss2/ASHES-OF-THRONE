using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Pieces held up by dark magic (the Glass Temple): a slow bob and drift, a lazy spin.
    /// Purely visual, no colliders are moved.
    /// </summary>
    public class Levitate : MonoBehaviour
    {
        public float bob = 0.35f, period = 5f;
        public Vector3 spin = new Vector3(0f, 6f, 0f);
        public float wobble = 3f;
        Vector3 _p0;
        Quaternion _r0;
        float _phase;

        void Start()
        {
            _p0 = transform.localPosition;
            _r0 = transform.localRotation;
            _phase = (transform.position.x * 0.37f + transform.position.z * 0.21f) % 6.28f;
        }

        void Update()
        {
            float t = Time.time + _phase * period;
            float k = Mathf.Sin(t / period * Mathf.PI * 2f);
            transform.localPosition = _p0 + Vector3.up * k * bob;
            transform.localRotation = _r0 * Quaternion.Euler(spin * Time.time) * Quaternion.Euler(Mathf.Sin(t * 0.7f) * wobble, 0f, Mathf.Cos(t * 0.5f) * wobble);
        }
    }
}
