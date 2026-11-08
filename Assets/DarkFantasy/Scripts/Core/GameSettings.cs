using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Player options (saved in PlayerPrefs): master / music / effects volume, fullscreen,
    /// screen shake. Applied immediately; other systems read the static values.
    /// </summary>
    public static class GameSettings
    {
        const string KMaster = "df_vol_master", KMusic = "df_vol_music", KSfx = "df_vol_sfx", KFull = "df_fullscreen", KShake = "df_shake";

        public static float MasterVolume { get; private set; } = 1f;
        public static float MusicVolume { get; private set; } = 0.8f;
        public static float SfxVolume { get; private set; } = 1f;
        public static float ScreenShake { get; private set; } = 1f;
        public static bool Fullscreen => Screen.fullScreen;

        public static event System.Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            MasterVolume = PlayerPrefs.GetFloat(KMaster, 1f);
            MusicVolume = PlayerPrefs.GetFloat(KMusic, 0.8f);
            SfxVolume = PlayerPrefs.GetFloat(KSfx, 1f);
            ScreenShake = PlayerPrefs.GetFloat(KShake, 1f);
            AudioListener.volume = MasterVolume;
            if (!Application.isEditor && PlayerPrefs.HasKey(KFull)) ApplyFullscreen(PlayerPrefs.GetInt(KFull) == 1);
        }

        public static void SetMaster(float v) { MasterVolume = Mathf.Clamp01(v); AudioListener.volume = MasterVolume; Save(KMaster, MasterVolume); }
        public static void SetMusic(float v) { MusicVolume = Mathf.Clamp01(v); Save(KMusic, MusicVolume); }
        public static void SetSfx(float v) { SfxVolume = Mathf.Clamp01(v); Save(KSfx, SfxVolume); }
        public static void SetShake(float v) { ScreenShake = Mathf.Clamp01(v); Save(KShake, ScreenShake); }

        public static void SetFullscreen(bool on)
        {
            ApplyFullscreen(on);
            PlayerPrefs.SetInt(KFull, on ? 1 : 0); PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static void ApplyFullscreen(bool on)
        {
            if (on)
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else
            {
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            }
        }

        static void Save(string key, float v) { PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); Changed?.Invoke(); }
    }
}
