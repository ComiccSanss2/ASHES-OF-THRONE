using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Scorrimento texture (nebbia del cancello, nuvole).</summary>
    public class UVScroll : MonoBehaviour
    {
        public Vector2 speed = new Vector2(0.03f, 0.06f);
        public string property = "_BaseMap";
        Renderer _r; MaterialPropertyBlock _mpb; Vector4 _st = new Vector4(1, 1, 0, 0);
        void Awake() { _r = GetComponent<Renderer>(); _mpb = new MaterialPropertyBlock(); if (_r && _r.sharedMaterial && _r.sharedMaterial.HasProperty(property + "_ST")) _st = _r.sharedMaterial.GetVector(property + "_ST"); }
        void Update()
        {
            if (!_r) return;
            _r.GetPropertyBlock(_mpb);
            _mpb.SetVector(property + "_ST", new Vector4(_st.x, _st.y, _st.z + Time.time * speed.x, _st.w + Time.time * speed.y));
            _r.SetPropertyBlock(_mpb);
        }
    }
}
