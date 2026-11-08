using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>The Warden's key, lying on his desk deep in the prisons. Whoever takes it opens the way for all.</summary>
    public class KeyPickup : MonoBehaviour, IInteractable
    {
        public static KeyPickup Instance { get; private set; }
        public Transform visual;
        public Light glow;
        public float interactDistance = 2.2f;
        Vector3 _base;

        void Awake() { Instance = this; if (visual) _base = visual.localPosition; }
        void OnEnable() => Interactables.All.Add(this);
        void OnDisable() => Interactables.All.Remove(this);

        bool Taken => CastleProgress.Instance != null && CastleProgress.Instance.IsSpawned && CastleProgress.Instance.KeyFound;

        void Update()
        {
            bool taken = Taken;
            if (visual)
            {
                if (visual.gameObject.activeSelf == taken) visual.gameObject.SetActive(!taken);
                visual.localPosition = _base + Vector3.up * (0.06f + Mathf.Sin(Time.time * 2f) * 0.04f);
                visual.localRotation = Quaternion.Euler(0f, Time.time * 50f, 70f);
            }
            if (glow) glow.intensity = taken ? 0f : 1.4f + Mathf.Sin(Time.time * 3f) * 0.3f;
            if (!taken && GameInput.InteractPressed && LocalCanInteract(out _)) CastleProgress.Instance.TakeKeyRpc();
        }

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            if (p == null || !p.CanInteract || Taken || CastleProgress.Instance == null) return false;
            if (Vector3.Distance(p.transform.position, transform.position) > interactDistance) return false;
            prompt = "Take the Warden's key";
            return true;
        }
    }
}
