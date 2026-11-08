using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// A two-leaf castle door that swings open when the shared progress says so (CastleProgress).
    /// The great door of the prison hall is opened by a player with Interact once the Warden's key
    /// has been found; the throne door opens by itself when The Ashen King falls.
    /// Plain scene object: every client animates it from the replicated state.
    /// </summary>
    public class CastleDoor : MonoBehaviour, IInteractable
    {
        public CastleProgress.Door door;
        public Transform leafLeft, leafRight;
        [Tooltip("Degrees each leaf swings (away from the player: +Z side of the door).")]
        public float openAngle = 105f;
        public float openTime = 3.2f;
        public Collider blocker;
        public bool interactive;
        public float interactDistance = 3.2f;
        public float halfWidth = 3f;
        public Light glow;

        float _t;
        bool _wasOpen;

        void OnEnable() => Interactables.All.Add(this);
        void OnDisable() => Interactables.All.Remove(this);

        void Update()
        {
            bool open = CastleProgress.IsOpen(door);
            if (open && !_wasOpen && _t < 0.01f)
            {
                Vector3 c = transform.position + Vector3.up * 2f;
                Sfx.Play(SfxId.DoorOpen, c, 1f, door == CastleProgress.Door.Great ? 0.7f : 0.85f);
                Fx.Spawn(FxId.ShockDust, transform.position + Vector3.up * 0.2f);
                SoulsCamera.Shake(0.03f, 0.5f);
            }
            _wasOpen = open;
            _t = Mathf.MoveTowards(_t, open ? 1f : 0f, Time.deltaTime / Mathf.Max(0.1f, openTime));
            float e = _t * _t * (3f - 2f * _t);
            if (leafLeft) leafLeft.localRotation = Quaternion.Euler(0f, -openAngle * e, 0f);
            if (leafRight) leafRight.localRotation = Quaternion.Euler(0f, openAngle * e, 0f);
            if (blocker) blocker.enabled = e < 0.55f;
            if (glow) glow.intensity = Mathf.Lerp(glow.intensity, open ? 0f : 0.9f, Time.deltaTime * 2f);

            if (interactive && GameInput.InteractPressed && LocalCanInteract(out _))
                CastleProgress.Instance?.OpenGreatDoorRpc();
        }

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            var prog = CastleProgress.Instance;
            if (p == null || !p.CanInteract || prog == null || !prog.IsSpawned || CastleProgress.IsOpen(door)) return false;
            Vector3 local = transform.InverseTransformPoint(p.transform.position);
            if (local.z > 0.5f || local.z < -interactDistance || Mathf.Abs(local.x) > halfWidth) return false;
            if (door == CastleProgress.Door.Throne) { prompt = "Sealed while the king lives"; return false; }
            if (door == CastleProgress.Door.Bridge) { prompt = "Barred while the wyrm draws breath"; return false; }
            if (!interactive) return false;
            if (!prog.KeyFound) { prompt = "Locked. A great key is needed"; return false; }
            prompt = "Unlock with the Warden's key";
            return true;
        }
    }
}
