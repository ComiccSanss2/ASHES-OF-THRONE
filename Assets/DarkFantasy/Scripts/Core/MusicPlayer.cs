using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Boss music: fades in when the fight starts (on every client), fades out on victory,
    /// on reset, or when the local player leaves the session.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        public AudioClip bossTheme;
        [Tooltip("The dragon's theme (Vaelthrax, on the terrace).")]
        public AudioClip dragonTheme;
        [Tooltip("The Shattered Mirror's theme: phase 1 loops the dark choir, phase 2 the orchestral crescendo.")]
        public AudioClip mirrorTheme;
        public Vector2 mirrorPhase1Loop = new Vector2(0f, 64f);
        public float mirrorPhase2Entry = 66f;
        public Vector2 mirrorPhase2Loop = new Vector2(68f, 112f);
        [Range(0, 1)] public float volume = 0.55f;
        public float fadeIn = 2.5f;
        public float fadeOut = 4f;

        AudioSource _src;
        float _target;

        void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip = bossTheme;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.volume = 0f;
            _src.priority = 0;
        }

        void OnEnable()
        {
            BossArena.FightStarted += OnFight;
            BossArena.BossDefeated += Stop;
            BossArena.FightReset += Stop;
            DragonArena.FightStarted += OnDragonFight;
            DragonArena.Defeated += Stop;
            DragonArena.FightReset += Stop;
            MirrorArena.FightStarted += OnMirrorFight;
            MirrorArena.Defeated += Stop;
            MirrorArena.FightReset += Stop;
            MirrorAI.Awakened += OnMirrorPhase2;
        }

        void OnDisable()
        {
            BossArena.FightStarted -= OnFight;
            BossArena.BossDefeated -= Stop;
            BossArena.FightReset -= Stop;
            DragonArena.FightStarted -= OnDragonFight;
            DragonArena.Defeated -= Stop;
            DragonArena.FightReset -= Stop;
            MirrorArena.FightStarted -= OnMirrorFight;
            MirrorArena.Defeated -= Stop;
            MirrorArena.FightReset -= Stop;
            MirrorAI.Awakened -= OnMirrorPhase2;
        }

        void OnFight(int players) => PlayTheme(bossTheme);
        void OnDragonFight(int players) => PlayTheme(dragonTheme ? dragonTheme : bossTheme);

        void OnMirrorFight(int players)
        {
            var m = MirrorAI.Instance;
            PlayTheme(mirrorTheme ? mirrorTheme : bossTheme);
            if (mirrorTheme && _src.clip == mirrorTheme && m != null && m.Phase >= 2 && _src.time < mirrorPhase2Entry) _src.time = mirrorPhase2Entry;
        }

        /// <summary>The glass cracks through: jump straight to the crescendo.</summary>
        void OnMirrorPhase2()
        {
            if (mirrorTheme == null || _src.clip != mirrorTheme || !_src.isPlaying) return;
            if (_src.time < mirrorPhase2Entry) _src.time = Mathf.Min(mirrorPhase2Entry, mirrorTheme.length - 1f);
        }

        void PlayTheme(AudioClip clip)
        {
            if (clip == null) return;
            if (_src.clip != clip) { _src.Stop(); _src.clip = clip; }
            if (!_src.isPlaying) { _src.time = 0f; _src.Play(); }
            _target = volume;
        }

        void Stop() => _target = 0f;

        void Update()
        {
            // the boss fight can also be joined late: follow the boss state
            var boss = BossAI.Instance;
            var dragon = DragonAI.Instance;
            bool kingFight = boss != null && boss.IsSpawned && boss.FightActive && boss.IsAlive;
            bool dragonFight = dragon != null && dragon.IsSpawned && dragon.FightActive && dragon.IsAlive;
            var mirror = MirrorAI.Instance;
            bool mirrorFight = mirror != null && mirror.IsSpawned && mirror.FightActive && mirror.IsAlive;
            bool fighting = PlayerController.Local != null && (kingFight || dragonFight || mirrorFight);
            if (fighting && _target <= 0f) { if (mirrorFight) OnMirrorFight(0); else if (dragonFight) OnDragonFight(0); else OnFight(0); }

            // the Mirror's theme loops by section (the choir in phase 1, the crescendo in phase 2)
            if (mirrorTheme && _src.clip == mirrorTheme && _src.isPlaying && _target > 0f)
            {
                bool p2 = mirror != null && mirror.Phase >= 2;
                Vector2 loop = p2 ? mirrorPhase2Loop : mirrorPhase1Loop;
                loop.y = Mathf.Min(loop.y, mirrorTheme.length - 0.5f);
                if (_src.time >= loop.y || (p2 && _src.time < loop.x - 3f)) _src.time = loop.x;
            }
            if (!fighting && _target > 0f && PlayerController.Local == null) _target = 0f;

            if (_target > 0f) _target = Mathf.Max(0.0001f, volume * GameSettings.MusicVolume);
            float rate = _target > _src.volume ? volume / Mathf.Max(0.1f, fadeIn) : volume / Mathf.Max(0.1f, fadeOut);
            _src.volume = Mathf.MoveTowards(_src.volume, _target, rate * Time.unscaledDeltaTime);
            if (_src.isPlaying && _target <= 0f && _src.volume <= 0.001f) _src.Stop();
        }
    }
}
