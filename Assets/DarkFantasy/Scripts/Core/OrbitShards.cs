using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Glass splinters circling an enemy (Glass Revenants). Visual only.</summary>
    public class OrbitShards : MonoBehaviour
    {
        public Transform[] shards;
        public float radius = 0.9f, height = 1.2f, speed = 1.6f;
        EnemyBase _owner;

        void Awake() => _owner = GetComponentInParent<EnemyBase>();

        void LateUpdate()
        {
            if (shards == null) return;
            bool alive = _owner == null || _owner.IsAlive;
            float t = Time.time;
            for (int i = 0; i < shards.Length; i++)
            {
                var s = shards[i];
                if (!s) continue;
                if (s.gameObject.activeSelf != alive) s.gameObject.SetActive(alive);
                if (!alive) continue;
                float a = t * speed * (i % 2 == 0 ? 1f : -0.8f) + i * (Mathf.PI * 2f / shards.Length);
                float r = radius * (0.85f + 0.15f * Mathf.Sin(t * 1.3f + i));
                s.position = transform.position + new Vector3(Mathf.Cos(a) * r, height + Mathf.Sin(t * 2.1f + i * 1.3f) * 0.35f + (i % 3) * 0.3f, Mathf.Sin(a) * r);
                s.rotation = Quaternion.Euler(t * 120f + i * 30f, t * 70f + i * 50f, 0f);
            }
        }
    }
}
