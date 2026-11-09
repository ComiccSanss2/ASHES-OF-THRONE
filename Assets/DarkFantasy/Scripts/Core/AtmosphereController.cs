using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Fog / ambient / exposure follow the local camera: outside (thick grey-green fog), the throne
    /// room (warm, readable), and the zones of LevelRefs (dark castle corridors, the night terrace).
    /// Changes blend smoothly.
    /// </summary>
    public class AtmosphereController : MonoBehaviour
    {
        public float blendSpeed = 1.2f;
        Color _outAmbient, _fog, _amb;
        float _density, _exposure;
        bool _init;
        float _appliedExposure = -99f;
        float _sky, _sunW;
        Color _sunCol0; float _sunInt0; Quaternion _sunRot0; bool _sunInit;
        Color _zSunCol = Color.white; float _zSunInt; Quaternion _zSunRot = Quaternion.identity;

        /// <summary>Extra fog density laid over the zone (the Reaper's Veil of Darkness).</summary>
        public static float VeilFog;

        void Start() { _outAmbient = RenderSettings.ambientLight; }

        void Update()
        {
            var lv = LevelRefs.Instance;
            var cam = SoulsCamera.Instance;
            if (lv == null || cam == null) return;
            // zones follow the player, not the camera: on the terrace the camera often sits behind
            // the player, back over the top of the stair (an interior zone), which hid the sunset
            bool active = cam.followTarget != null;
            Vector3 p = active ? cam.followTarget.position + Vector3.up * 1.5f : cam.transform.position;

            Color fog = lv.outdoorFog, amb = _outAmbient;
            float dens = lv.outdoorFogDensity, exp = 0f;
            bool sky = false, sun = false;
            if (active && lv.IsInsideArena(p)) { fog = lv.indoorFog; dens = lv.indoorFogDensity; amb = lv.indoorAmbient; exp = lv.indoorExposure; }
            else if (active && lv.zones != null)
                foreach (var z in lv.zones)
                    if (z.bounds.Contains(p))
                    {
                        fog = z.fog; dens = z.fogDensity; amb = z.ambient; exp = z.exposure; sky = z.sky;
                        if (z.overrideSun) { sun = true; _zSunCol = z.sunColor; _zSunInt = z.sunIntensity; _zSunRot = Quaternion.Euler(z.sunEuler); }
                        break;
                    }

            if (VeilFog > 0.001f) { dens += VeilFog; fog = Color.Lerp(fog, new Color(0.02f, 0.0f, 0.01f), Mathf.Clamp01(VeilFog * 12f)); }
            if (!_init) { _fog = fog; _amb = amb; _density = dens; _exposure = exp; _init = true; }
            float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime * 2f);
            _fog = Color.Lerp(_fog, fog, k);
            _amb = Color.Lerp(_amb, amb, k);
            _density = Mathf.Lerp(_density, dens, k);
            _exposure = Mathf.Lerp(_exposure, exp, k);

            RenderSettings.fogColor = _fog;
            RenderSettings.fogDensity = _density;
            Color cur = RenderSettings.ambientLight;
            if (Mathf.Abs(cur.r - _amb.r) + Mathf.Abs(cur.g - _amb.g) + Mathf.Abs(cur.b - _amb.b) > 0.006f)
            { RenderSettings.ambientLight = _amb; DynamicGI.UpdateEnvironment(); }
            if (cam.Cam) cam.Cam.backgroundColor = _fog;
            if (Mathf.Abs(_exposure - _appliedExposure) > 0.005f) { _appliedExposure = _exposure; ScreenPresenter.SetExtraExposure(_exposure); }

            // sunset sky dome
            _sky = Mathf.MoveTowards(_sky, sky ? 1f : 0f, Time.deltaTime * 0.8f);
            if (lv.skyDome) lv.skyDome.SetAlpha(_sky);
            // the sun: blends from the scene's light to the zone's
            var l = lv.mainLight;
            if (l)
            {
                if (!_sunInit) { _sunCol0 = l.color; _sunInt0 = l.intensity; _sunRot0 = l.transform.rotation; _sunInit = true; }
                _sunW = Mathf.MoveTowards(_sunW, sun ? 1f : 0f, Time.deltaTime * 0.6f);
                l.color = Color.Lerp(_sunCol0, _zSunCol, _sunW);
                l.intensity = Mathf.Lerp(_sunInt0, _zSunInt, _sunW);
                l.transform.rotation = Quaternion.Slerp(_sunRot0, _zSunRot, _sunW);
            }
        }
    }
}
