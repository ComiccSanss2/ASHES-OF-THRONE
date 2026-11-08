using System;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Attack definition. ALL TIMES ARE IN CLIP SECONDS (animation time at speed 1) and are
    /// read against the Animator's real playback time, so hit windows, sounds, trails and
    /// reactions stay frame-locked to the animation even with hit-stop or speed changes.
    /// The builder fills swing timings automatically by analysing the clips (AnimationAnalysis).
    /// </summary>
    [Serializable]
    public class AttackData
    {
        public string name = "Attack";
        [Tooltip("Animator state name.")]
        public string state = CharacterAnimator.Attack1;
        public float animSpeed = 1f;
        [Tooltip("Clip time the attack starts from (skips idle anticipation so combos flow).")]
        public float startTime = 0f;
        [Tooltip("Active hit window (clip seconds) = the frames where the blade really sweeps.")]
        public float hitStart = 0.35f;
        public float hitEnd = 0.55f;
        [Tooltip("Contact frame (fastest blade frame): sounds and VFX peak here.")]
        public float swingPeak = 0.45f;
        [Tooltip("From here the next combo hit can start (clip seconds).")]
        public float comboWindowStart = 0.55f;
        [Tooltip("From here moving/rolling cancels the recovery (clip seconds).")]
        public float cancelTime = 0.7f;
        [Tooltip("End of the attack: blend back to locomotion (clip seconds).")]
        public float endTime = 1.0f;
        public float damage = 20f;
        public float poiseDamage = 15f;
        public float staminaCost = 14f;
        [Tooltip("Initial push speed given to the victim (m/s).")]
        public float knockback = 3f;
        [Tooltip("Forward step during the attack (m), applied between lungeStart and lungeEnd (clip seconds).")]
        public float lunge = 0.8f;
        public float lungeStart = 0.15f;
        public float lungeEnd = 0.45f;
        public bool parryable = true;
        public bool unblockable;
        [Tooltip("Not interrupted by incoming hits.")]
        public bool hyperArmor;
        [Tooltip("Clip time used when this attack is chained from the previous one (-1 = startTime).")]
        public float chainStartTime = -1f;

        public AttackData Clone() => (AttackData)MemberwiseClone();

        /// <summary>Real seconds from the attack start until the contact frame.</summary>
        public float RealTimeToPeak => Mathf.Max(0f, swingPeak - startTime) / Mathf.Max(0.05f, animSpeed);
    }

    /// <summary>Playable classes (chosen in the menu / lobby).</summary>
    public enum PlayerClass : byte { Knight, Archer, Bard }

    [Flags]
    public enum HitFlags : byte
    {
        None = 0,
        Parryable = 1,
        Unblockable = 2,
        Heavy = 4,     // knockdown instead of a flinch
        AreaOfEffect = 8,
    }

    /// <summary>Actions replicated to other clients.</summary>
    public enum NetAction : byte
    {
        None,
        LightAttack1, LightAttack2, LightAttack3,
        HeavyStart, HeavyRelease,
        Roll,
        Parry,
        Hurt, Knockdown, Stagger, BlockHit, GuardBreak,
        Death, Respawn,
        Drink,
        EnemyAttack,
        BossPhase2,
        Recover,        // back to locomotion (an attack/parry/flinch ended or was cancelled on the owner)
        Spell,          // bard: param = spell index
    }

    public static class CombatUtil
    {
        public static bool IsInFront(Transform defender, Vector3 attackerPos, float halfAngle = 70f)
        {
            Vector3 to = MathUtil.Flat(attackerPos - defender.position);
            if (to.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(MathUtil.Flat(defender.forward), to) <= halfAngle;
        }
    }
}
