using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Water dripping from the vault now and then: a drop sound and a ripple below.</summary>
    public class WaterDrip : MonoBehaviour
    {
        public Vector2 interval = new Vector2(2f, 6f);
        public float floorY;
        float _next;
        void Start() => _next = Time.time + Random.Range(0f, interval.y);
        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(interval.x, interval.y);
            var cam = SoulsCamera.Instance;
            if (cam && (cam.transform.position - transform.position).sqrMagnitude > 30f * 30f) return;
            Vector3 p = new Vector3(transform.position.x, floorY + 0.02f, transform.position.z);
            Fx.Spawn(FxId.Ripple, p, Vector3.up * 0.6f);
            Sfx.Play(SfxId.WaterDrip, p, 0.4f, Random.Range(1.3f, 1.8f));
        }
    }
}
