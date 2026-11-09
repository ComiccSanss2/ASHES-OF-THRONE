using UnityEngine;

namespace DarkFantasy
{
    /// <summary>One spectral scythe of the Rain: falls spinning onto its mark, bites the ground, fades.</summary>
    public class FallingScythe : MonoBehaviour
    {
        Vector3 _to, _from; float _flight, _t, _yaw; Light _light;

        public void Init(Vector3 to, float flight, float yaw, Color c)
        {
            _to = to; _flight = Mathf.Max(0.2f, flight); _yaw = yaw;
            _from = to + Vector3.up * 14f + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 2f;
            transform.position = _from;
            transform.localScale = Vector3.one * 1.6f;
            var lg = new GameObject("Light"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0f, 1.3f, 0.5f);
            _light = lg.AddComponent<Light>(); _light.type = LightType.Point; _light.color = c; _light.range = 4f; _light.intensity = 3f; _light.shadows = LightShadows.None;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _flight);
            if (k < 1f)
            {
                transform.position = Vector3.Lerp(_from, _to, k * k);
                // blade spinning end over end, landing tip-first in the ground
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(Mathf.Lerp(-540f, -200f, k), 0f, 0f);
                return;
            }
            if (_t - _flight < Time.deltaTime * 1.5f)
            {
                Sfx.Play(SfxId.MirrorSlam, _to, 0.7f, Random.Range(1.3f, 1.6f));
                MirrorFx.DarkShock(_to, 1.4f);
            }
            float fade = Mathf.Clamp01((_t - _flight) / 1.4f);
            transform.localScale = Vector3.one * 1.6f * (1f - fade);
            if (_light) _light.intensity = 3f * (1f - fade);
            if (fade >= 1f) Destroy(gameObject);
        }
    }
}
