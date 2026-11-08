using System;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Single interface to the knight's Animator (players, guardians and the boss share the rig).
    ///
    /// Frame-accurate combat presentation, computed from the Animator's REAL playback time on
    /// every client (so it is identical for the attacker, the victim and spectators):
    ///  - swing windows per state (baked by the builder from clip analysis) drive the weapon
    ///    trail and the sword-slash sound, timed so the sound's peak lands on the contact frame;
    ///  - pitch/speed of the slash follows the swing speed (+ random variation);
    ///  - upper-body hit reaction (flinch) that does not interrupt locomotion or attacks;
    ///  - hit-stop freezes the animation for a few frames on impact;
    ///  - root drift of the clips is cancelled (attacks/spins play in place; lunges are physical).
    /// The procedural roll rotates VisualRoot around the hips (the pack has no roll clip).
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        // State names (must match DarkFantasyBuilder.BuildAnimator)
        public const string Locomotion = "Locomotion";
        public const string Attack1 = "Attack1";      // diagonal overhead slash
        public const string Attack2 = "Attack2";      // horizontal sweep
        public const string Heavy = "Heavy";          // big overhead (chargeable)
        public const string Spin = "Spin";            // spinning slash
        public const string Parry = "Parry";
        public const string Hurt = "Hurt";
        public const string Knockdown = "Knockdown";
        public const string Death = "Death";
        public const string Taunt = "Taunt";
        public const string BlockUpper = "Block";
        public const string HitReactUpper = "HitReact";
        public const string EmptyUpper = "Empty";

        [Serializable]
        public struct SwingWindow
        {
            public string state;
            public float start, peak, end;   // clip seconds
            public float loopLength;         // >0 for looping states (one swing per loop)
        }

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int LocoMulHash = Animator.StringToHash("LocoMul");

        public Animator animator;
        [Tooltip("Pivot at hip height: rotated for the procedural roll.")]
        public Transform visualRoot;
        public float pivotHeight = 0.9f;
        [Tooltip("Body size relative to a 1.85 m knight: locomotion speeds are read per body size (the boss strides).")]
        public float locomotionScale = 1f;

        [Header("Combat presentation")]
        public SwingWindow[] swings;
        public BladeTrail weaponTrail;
        [Tooltip("Base pitch of this character's sword (big boss blade = low).")]
        public float swingPitch = 1f;
        public float swingVolume = 1f;
        [Tooltip("Bones whose horizontal animation drift is cancelled (armature root, hips).")]
        public Transform[] inPlaceBones;

        float _upperWeight, _blockTarget, _reactTimer;
        float _rollTimer = -1f, _rollDuration;
        float _flashTimer;
        bool _flashActive;
        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        float _baseSpeed = 1f;
        float _hitStopTimer;
        Vector3[] _bindLocal;

        public bool IsRollingVisual => _rollTimer >= 0f;
        /// <summary>The upper-body guard pose is up (knight: blade across; archer: bow raised, arrow nocked).</summary>
        public bool GuardRaised => _blockTarget > 0.5f && _upperWeight > 0.6f && _reactTimer <= 0f;
        public float PlaybackSpeed => _baseSpeed;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _renderers = GetComponentsInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            if (inPlaceBones != null)
            {
                _bindLocal = new Vector3[inPlaceBones.Length];
                for (int i = 0; i < inPlaceBones.Length; i++) if (inPlaceBones[i]) _bindLocal[i] = inPlaceBones[i].localPosition;
            }
            if (weaponTrail) weaponTrail.emitting = false;
        }

        // ------------------------------------------------------------------ playback
        // blend-tree thresholds (DarkFantasyBuilder.BuildAnimator): walk 1.9 (sword in guard), run 4.1 (sword on the shoulder)
        const float WalkRef = 1.9f, RunRef = 4.1f;

        /// <summary>
        /// Walk and run carry the greatsword in two different ways (guard / shoulder): a half-way blend
        /// would leave the arms in between, so the speed is snapped to one gait or the other and the
        /// walk cycle is sped up instead. Speeds are divided by the body size (a big boss walks).
        /// </summary>
        public void SetLocomotion(float speed, float playbackMul = 1f)
        {
            if (animator == null) return;
            float s = Mathf.Abs(speed) / Mathf.Max(0.1f, locomotionScale);
            float param, mul = 1f;
            if (s < 0.05f) param = 0f;
            else if (s <= 2.6f) { param = Mathf.Min(s, WalkRef); mul = Mathf.Max(1f, s / WalkRef); }
            else { param = RunRef; mul = Mathf.Max(1f, s / RunRef); }
            animator.SetFloat(SpeedHash, param, 0.06f, Time.deltaTime);
            playbackMul *= mul;
            animator.SetFloat(LocoMulHash, playbackMul);
        }

        /// <summary>Cross-fades into a state starting at a given clip time (seconds).</summary>
        public void Play(string state, float fade = 0.1f, float speed = 1f, float startTimeSeconds = 0f)
        {
            if (animator == null) return;
            _baseSpeed = speed;
            if (_hitStopTimer <= 0f) animator.speed = speed;
            animator.CrossFadeInFixedTime(state, fade, 0, startTimeSeconds);
        }

        public void ReturnToLocomotion(float fade = 0.25f)
        {
            if (animator == null) return;
            _baseSpeed = 1f;
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(Locomotion, fade, 0);
        }

        public void SetPlaybackSpeed(float s) { _baseSpeed = s; if (animator && _hitStopTimer <= 0f) animator.speed = s; }

        /// <summary>
        /// Current clip time (seconds) of a state on the base layer, or -1 if it isn't playing.
        /// During a cross-fade the incoming state is considered. Looping states return total time.
        /// </summary>
        public float GetStateTime(string state)
        {
            if (animator == null) return -1f;
            int hash = Animator.StringToHash(state);
            if (animator.IsInTransition(0))
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                if (next.shortNameHash == hash) return StateSeconds(next, true);
            }
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            if (cur.shortNameHash == hash) return StateSeconds(cur, false);
            return -1f;
        }

        static readonly System.Collections.Generic.List<AnimatorClipInfo> _clipBuf = new System.Collections.Generic.List<AnimatorClipInfo>(4);

        /// <summary>
        /// Clip seconds of a state. AnimatorStateInfo.length is divided by the playback speed, so
        /// while an animation is slowed (charge hold at 0.03, hit-stop at 0.02, frozen at 0) the
        /// naive normalizedTime × length jumps 30-50× ahead (or to infinity). The clip's own length
        /// keeps the time exact whatever the speed.
        /// </summary>
        float StateSeconds(AnimatorStateInfo st, bool next)
        {
            _clipBuf.Clear();
            if (next) animator.GetNextAnimatorClipInfo(0, _clipBuf); else animator.GetCurrentAnimatorClipInfo(0, _clipBuf);
            float len = _clipBuf.Count > 0 && _clipBuf[0].clip ? _clipBuf[0].clip.length : 0f;
            if (len <= 0f)
            {
                float sp = Mathf.Abs(animator.speed * st.speed * st.speedMultiplier);
                len = sp > 1e-4f && !float.IsInfinity(st.length) ? st.length * sp : 0f;
            }
            return st.normalizedTime * len;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Development: the naive (speed-dependent) state time, for comparison.</summary>
        public float DevRawStateTime(string state)
        {
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            return cur.shortNameHash == Animator.StringToHash(state) ? cur.normalizedTime * cur.length : -1f;
        }
#endif

        public bool IsInState(string state) => GetStateTime(state) >= 0f;

        public bool IsInAttackState => IsInState(Attack1) || IsInState(Attack2) || IsInState(Heavy) || IsInState(Spin);

        public void SetBlocking(bool on) => _blockTarget = on ? 1f : 0f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Development: logs the animator state (used by the editor test bridge).</summary>
        public void DevAnimState(string _)
        {
            if (!animator) { Debug.Log("[DF] anim: no animator"); return; }
            var st = animator.GetCurrentAnimatorStateInfo(0);
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            string cn = clips.Length > 0 ? clips[0].clip.name : "-";
            Debug.Log($"[DF] anim {name}: enabled={animator.enabled} speed={animator.speed:F2} ctrl={(animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name : "null")} " +
                      $"state={(st.IsName(Locomotion) ? "Locomotion" : st.shortNameHash.ToString())} t={st.normalizedTime:F2} clip={cn} layers={animator.layerCount} w1={(animator.layerCount > 1 ? animator.GetLayerWeight(1) : 0):F2} " +
                      $"cull={animator.cullingMode} avatar={(animator.avatar ? animator.avatar.name : "null")} hasRoot={animator.hasRootMotion} model={animator.gameObject.name} scale={animator.transform.lossyScale.y:F2}");
        }
#endif

        public void StartRoll(float duration) { _rollTimer = 0f; _rollDuration = duration; }

        /// <summary>Hit reaction: brief red flash + upper-body flinch + hit-stop.</summary>
        public void HitReact(float hitStop = 0.07f)
        {
            _flashTimer = 0.12f;
            _reactTimer = 0.38f;
            if (animator && animator.layerCount > 1) animator.CrossFadeInFixedTime(HitReactUpper, 0.03f, 1, 0.05f);
            HitStop(hitStop);
        }

        public void HitFlash() => _flashTimer = 0.12f;

        Color _baseEmission = Color.black;

        /// <summary>Resting emission of the character (e.g. the boss smouldering in phase 2).</summary>
        public void SetBaseEmission(Color c)
        {
            _baseEmission = c;
            bool on = _flashActive;
            _flashActive = !on;   // force ApplyFlash to re-apply the current state
            ApplyFlash(on);
        }

        /// <summary>Freezes the animation for a few frames to give weight to the impact.</summary>
        public void HitStop(float t = 0.06f) => _hitStopTimer = Mathf.Max(_hitStopTimer, t);

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (animator == null) return;
            float dt = Time.deltaTime;

            // hit stop
            if (_hitStopTimer > 0f)
            {
                _hitStopTimer -= dt;
                animator.speed = _hitStopTimer > 0f ? 0.02f : _baseSpeed;
            }

            UpdateUpperLayer(dt);
            UpdateSwingPresentation();
            UpdateRoll(dt);

            if (_flashTimer > 0f || _flashActive)
            {
                _flashTimer -= dt;
                ApplyFlash(_flashTimer > 0f);
            }
        }

        void LateUpdate()
        {
            // cancel horizontal drift baked into the clips (the character moves physically)
            if (inPlaceBones == null || _bindLocal == null) return;
            for (int i = 0; i < inPlaceBones.Length; i++)
            {
                var b = inPlaceBones[i];
                if (!b) continue;
                var p = b.localPosition;
                b.localPosition = new Vector3(_bindLocal[i].x, p.y, _bindLocal[i].z);
            }
        }

        void UpdateUpperLayer(float dt)
        {
            if (animator.layerCount < 2) return;
            bool reacting = _reactTimer > 0f;
            if (reacting) _reactTimer -= dt;
            float target = reacting ? Mathf.Clamp01(_reactTimer / 0.12f) : _blockTarget;
            _upperWeight = Mathf.MoveTowards(_upperWeight, target, dt * (reacting ? 14f : 7f));
            animator.SetLayerWeight(1, _upperWeight);
            if (reacting) return;
            var st = animator.GetCurrentAnimatorStateInfo(1);
            if (_blockTarget > 0.5f && !st.IsName(BlockUpper) && !animator.IsInTransition(1)) animator.CrossFadeInFixedTime(BlockUpper, 0.12f, 1);
            if (_blockTarget < 0.5f && _upperWeight <= 0.01f && !st.IsName(EmptyUpper) && !animator.IsInTransition(1)) animator.CrossFadeInFixedTime(EmptyUpper, 0.05f, 1);
        }

        float[] _prevT;
        bool[] _armed;

        /// <summary>Weapon trail + timed slash sound, from the real animation time.</summary>
        void UpdateSwingPresentation()
        {
            if (swings == null || swings.Length == 0) return;
            if (_prevT == null || _prevT.Length != swings.Length)
            {
                _prevT = new float[swings.Length];
                _armed = new bool[swings.Length];
                for (int i = 0; i < _prevT.Length; i++) _prevT[i] = -1f;
            }
            bool active = false;
            float speed = Mathf.Max(0.05f, _baseSpeed);
            for (int i = 0; i < swings.Length; i++)
            {
                var w = swings[i];
                float t = GetStateTime(w.state);
                if (t < 0f) { _prevT[i] = -1f; _armed[i] = false; continue; }
                if (w.loopLength > 0.01f) t = Mathf.Repeat(t, w.loopLength);

                // state just entered, restarted, or looped → arm the swing sound again
                if (_prevT[i] < 0f || t < _prevT[i] - 0.02f) _armed[i] = t < w.peak;
                _prevT[i] = t;

                if (_armed[i])
                {
                    float timeToPeak = (w.peak - t) / speed;
                    float pitch = Mathf.Max(0.3f, swingPitch);
                    float lead = (Sfx.Instance ? Sfx.Instance.slashPeakTime : 0.23f) / pitch;
                    if (timeToPeak <= lead)
                    {
                        _armed[i] = false;
                        Sfx.PlaySwing(WeaponPosition, timeToPeak, speed, swingPitch, swingVolume);
                    }
                }
                if (t >= w.start - 0.03f && t <= w.end + 0.05f) active = true;
            }
            if (weaponTrail && weaponTrail.emitting != active) weaponTrail.emitting = active;
        }

        Vector3 WeaponPosition => weaponTrail && weaponTrail.tipPoint ? weaponTrail.tipPoint.position : transform.position + Vector3.up;

        void UpdateRoll(float dt)
        {
            if (visualRoot == null) return;
            if (_rollTimer < 0f) return;
            _rollTimer += dt;
            float t = Mathf.Clamp01(_rollTimer / _rollDuration);
            float eased = t * t * (3f - 2f * t);
            float crouch = Mathf.Sin(t * Mathf.PI) * 0.35f;
            visualRoot.localRotation = Quaternion.Euler(eased * 360f, 0, 0);
            visualRoot.localPosition = new Vector3(0, pivotHeight - crouch, 0);
            if (t >= 1f) { _rollTimer = -1f; visualRoot.localRotation = Quaternion.identity; visualRoot.localPosition = new Vector3(0, pivotHeight, 0); }
        }

        void ApplyFlash(bool on)
        {
            if (on == _flashActive) return;
            _flashActive = on;
            foreach (var r in _renderers)
            {
                if (r == null || r.gameObject.name == "BladeTrailMesh") continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", on ? new Color(0.4f, 0.06f, 0.04f) + _baseEmission : _baseEmission);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
