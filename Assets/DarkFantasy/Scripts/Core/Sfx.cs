using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DarkFantasy
{
    public enum SfxId
    {
        Swing, HitFlesh, HitArmor, Block, Parry, GuardBreak, Roll, Hurt, Death,
        EnemyDeath, EnemyGrunt, Roar, Slam, Quake, FogPass, Drink, Victory, Respawn,
        UiClick, UiConfirm,
        FireWhoosh, FireBurst, Eruption, Warning, Leap,
        BowDraw, BowRelease, ArrowImpact,
        ChestOpen, RelicReveal,
        DoorOpen, WaterStep, WaterDrip, DragonRoar, DragonBite, DragonFire, DragonWing, DragonStep, Fireball,
        SpellCast, SpellBolt, SpellImpact, SpellHeal, SpellAnthem, SpellCurse, SpellCrescendo, SpellFail, Rest,
        StepStone, StepDirt, StepGravel, StepWater, DragonTail, DragonCharge, DragonSwoop,
        MirrorShatter, MirrorHum, MirrorVortex, MirrorSlam, MirrorCurse
    }

    /// <summary>
    /// Sound effects from the project's sound packs (assigned by the builder).
    /// Every play randomises pitch (which in Unity also changes playback speed) inside the
    /// entry's range, so repeated hits never sound identical. Entries can layer several clips
    /// (e.g. flesh stab + generic impact) and skip leading silence with "offset".
    /// No procedural/synth audio and no global ambient bed.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx Instance { get; private set; }

        [Serializable]
        public class Layer
        {
            public AudioClip clip;
            [Tooltip("Seconds of leading silence to skip.")] public float offset;
            public float volume = 1f;
            [Tooltip("Pitch multiplier for this layer (on top of the random pitch).")] public float pitch = 1f;
        }

        [Serializable]
        public class Entry
        {
            public SfxId id;
            public Layer[] layers;
            public Vector2 pitchRange = new Vector2(0.92f, 1.08f);
            public float volume = 1f;
            [Tooltip("0 = 2D, 1 = fully 3D.")] public float spatial = 0.85f;
            [Tooltip("Plays ONE random layer (variations, e.g. footsteps) instead of all of them.")] public bool pickOne;
        }

        public Entry[] entries;
        [Range(0, 1)] public float masterVolume = 0.9f;

        /// <summary>Seconds from the start of the sword-slash clip to its loudest point.</summary>
        public float slashPeakTime = 0.23f;

        readonly Dictionary<SfxId, Entry> _map = new Dictionary<SfxId, Entry>();
        readonly Queue<AudioSource> _pool = new Queue<AudioSource>();
        readonly List<AudioSource> _busy = new List<AudioSource>();

        void Awake()
        {
            Instance = this;
            if (entries != null) foreach (var e in entries) _map[e.id] = e;
        }

        void Update()
        {
            for (int i = _busy.Count - 1; i >= 0; i--)
            {
                var s = _busy[i];
                if (s == null) { _busy.RemoveAt(i); continue; }
                if (!s.isPlaying && s.time <= 0f && !s.GetComponent<DelayedFlag>().pending) { _busy.RemoveAt(i); _pool.Enqueue(s); }
            }
        }

        class DelayedFlag : MonoBehaviour { public bool pending; public float until; void Update() { if (pending && Time.time >= until) pending = false; } }

        AudioSource Get()
        {
            AudioSource s = _pool.Count > 0 ? _pool.Dequeue() : null;
            if (s == null)
            {
                var go = new GameObject("sfx");
                go.transform.SetParent(transform, false);
                s = go.AddComponent<AudioSource>();
                go.AddComponent<DelayedFlag>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 4f; s.maxDistance = 45f;
                s.dopplerLevel = 0f;
            }
            _busy.Add(s);
            return s;
        }

        void PlayLayer(Layer l, Vector3 pos, float vol, float pitch, float spatial, float delay)
        {
            if (l == null || l.clip == null) return;
            var s = Get();
            s.transform.position = pos;
            s.clip = l.clip;
            s.volume = Mathf.Clamp01(vol * l.volume * masterVolume * GameSettings.SfxVolume);
            s.pitch = Mathf.Clamp(pitch * l.pitch, 0.1f, 3f);
            s.spatialBlend = spatial;
            float off = l.offset;
            if (delay < 0f) { off += -delay * s.pitch; delay = 0f; }   // late: skip into the clip
            s.time = Mathf.Clamp(off, 0f, l.clip.length - 0.01f);
            var flag = s.GetComponent<DelayedFlag>();
            flag.pending = delay > 0f; flag.until = Time.time + delay + 0.05f;
            if (delay > 0f) s.PlayDelayed(delay); else s.Play();
        }

        public static void Play(SfxId id, Vector3 pos, float vol = 1f, float pitch = 1f, float delay = 0f)
        {
            if (Instance == null || !Instance._map.TryGetValue(id, out var e)) return;
            float p = pitch * Random.Range(e.pitchRange.x, e.pitchRange.y);
            if (e.pickOne && e.layers.Length > 0) { Instance.PlayLayer(e.layers[Random.Range(0, e.layers.Length)], pos, vol * e.volume, p, e.spatial, delay); return; }
            foreach (var l in e.layers) Instance.PlayLayer(l, pos, vol * e.volume, p, e.spatial, delay);
        }

        public static void Play2D(SfxId id, float vol = 1f, float pitch = 1f)
        {
            if (Instance == null || !Instance._map.TryGetValue(id, out var e)) return;
            float p = pitch * Random.Range(e.pitchRange.x, e.pitchRange.y);
            foreach (var l in e.layers) Instance.PlayLayer(l, Vector3.zero, vol * e.volume, p, 0f, 0f);
        }

        /// <summary>
        /// Sword slash timed on the animation: the clip's loudest point lands exactly on the
        /// contact frame. timeToPeak = real seconds until the blade's fastest frame.
        /// The pitch (and therefore speed) follows the swing speed, plus random variation.
        /// </summary>
        public static void PlaySwing(Vector3 pos, float timeToPeak, float swingSpeed, float basePitch, float vol = 1f)
        {
            if (Instance == null || !Instance._map.TryGetValue(SfxId.Swing, out var e)) return;
            float p = basePitch * Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(0.6f, 1.6f, swingSpeed)) * Random.Range(e.pitchRange.x, e.pitchRange.y);
            float lead = Instance.slashPeakTime / p;
            foreach (var l in e.layers)
                Instance.PlayLayer(l, pos, vol * e.volume, p, e.spatial, timeToPeak - lead);
        }
    }
}
