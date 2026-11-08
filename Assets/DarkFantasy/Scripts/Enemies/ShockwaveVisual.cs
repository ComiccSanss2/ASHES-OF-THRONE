using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Anello che si espande (visuale lato client dell'onda sismica).</summary>
    public class ShockwaveVisual : MonoBehaviour
    {
        float _max = 15f, _speed = 9f, _r = 0.5f;
        Renderer _rend; MaterialPropertyBlock _mpb;

        Color _color = new Color(0.55f, 0.45f, 0.35f, 1f);
        public void Init(float maxRadius, float speed) { _max = maxRadius; _speed = speed; }
        public void Init(float maxRadius, float speed, Color color) { _max = maxRadius; _speed = speed; _color = color; }

        void Awake() { _rend = GetComponentInChildren<Renderer>(); _mpb = new MaterialPropertyBlock(); }

        void Update()
        {
            _r += _speed * Time.deltaTime;
            transform.localScale = new Vector3(_r, 1f, _r);
            if (_rend)
            {
                float a = Mathf.Clamp01(1f - _r / _max);
                _rend.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", new Color(_color.r, _color.g, _color.b, a * 0.35f));
                _rend.SetPropertyBlock(_mpb);
            }
            if (_r >= _max) Destroy(gameObject);
        }
    }
}
