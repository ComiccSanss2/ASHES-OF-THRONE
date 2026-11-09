using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The Reaper's hovering loop: the poses of its flight are blend shapes of one mesh (body and
    /// scythe), morphed continuously from one to the next and back (eased at both ends), so the
    /// motion is smooth at any frame rate.
    /// </summary>
    public class GrimFlipbook : MonoBehaviour
    {
        public SkinnedMeshRenderer[] renderers;
        [Tooltip("Poses per second along the loop.")]
        public float fps = 7f;
        [Tooltip("Speed multiplier (the boss quickens it in phase 2).")]
        public float speed = 1f;
        float _t;

        void Update()
        {
            if (renderers == null) return;
            _t += Time.deltaTime * fps * speed;
            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null) continue;
                int keys = r.sharedMesh.blendShapeCount;           // pose 0 is the basis, 1..keys the shapes
                if (keys == 0) continue;
                float cycle = keys * 2f;
                float u = (_t % cycle) / cycle;                     // 0..1 over a full there-and-back
                float p = (1f - Mathf.Cos(u * Mathf.PI * 2f)) * 0.5f * keys;   // 0 → keys → 0, eased
                int i = Mathf.FloorToInt(p); float f = p - i;
                for (int k = 0; k < keys; k++)
                {
                    int pose = k + 1;
                    float w = pose == i ? 1f - f : pose == i + 1 ? f : 0f;
                    r.SetBlendShapeWeight(k, w * 100f);
                }
            }
        }
    }
}
