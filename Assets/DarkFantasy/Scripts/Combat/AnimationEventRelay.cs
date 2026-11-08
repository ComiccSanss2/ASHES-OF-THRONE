using System;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Inoltra gli Animation Event dal GameObject dell'Animator al controller del personaggio.
    /// Eventi supportati: HitboxOn, HitboxOff, Footstep.
    /// </summary>
    public class AnimationEventRelay : MonoBehaviour
    {
        public WeaponHitbox hitbox;
        public event Action Footstep;

        public void HitboxOn() { if (hitbox) hitbox.BeginSwing(); }
        public void HitboxOff() { if (hitbox) hitbox.EndSwing(); }
        public void FootstepEvent() => Footstep?.Invoke();
    }
}
