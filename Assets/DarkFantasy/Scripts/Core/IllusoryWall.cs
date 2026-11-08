using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// A wall that is not there: no collider, it looks like the stones around it until someone
    /// walks into it; then it shimmers and fades for good (for that player).
    /// </summary>
    public class IllusoryWall : MonoBehaviour
    {
        public Renderer wall;
        public float revealDistance = 1.1f;
        public static event System.Action Revealed;
        float _alpha = 1f;
        bool _revealed;
        MaterialPropertyBlock _mpb;

        void Update()
        {
            var p = PlayerController.Local;
            if (!_revealed && p != null && wall != null)
            {
                Vector3 c = wall.bounds.ClosestPoint(p.transform.position + Vector3.up);
                if (Vector3.Distance(c, p.transform.position + Vector3.up) < revealDistance)
                {
                    _revealed = true;
                    Sfx.Play(SfxId.FogPass, wall.bounds.center, 0.9f, 0.8f);
                    Fx.Spawn(FxId.ShockDust, wall.bounds.center);
                    Revealed?.Invoke();
                }
            }
            if (!_revealed || wall == null) return;
            _alpha = Mathf.MoveTowards(_alpha, 0f, Time.deltaTime * 0.8f);
            if (_alpha <= 0f) { wall.enabled = false; enabled = false; return; }
            // flicker out
            wall.enabled = Random.value < _alpha * 1.2f;
        }
    }
}
