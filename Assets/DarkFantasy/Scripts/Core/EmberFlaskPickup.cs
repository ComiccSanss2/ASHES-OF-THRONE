using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// An Ember Flask hidden in the prison: +1 flask for the rest of the session. Every player can
    /// take each one once (local: it stays visible for the others until they take theirs).
    /// </summary>
    public class EmberFlaskPickup : MonoBehaviour, IInteractable
    {
        public Transform visual;
        public Light glow;
        public float interactDistance = 1.9f;
        bool _taken;
        float _t;

        public static event System.Action<int> Taken;   // new flask count

        void OnEnable() { if (!Interactables.All.Contains(this)) Interactables.All.Add(this); }
        void OnDisable() => Interactables.All.Remove(this);

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            if (_taken || p == null || !p.CanInteract) return false;
            if (Vector3.Distance(p.transform.position, transform.position) > interactDistance) return false;
            prompt = "Take the Ember Flask";
            return true;
        }

        void Update()
        {
            _t += Time.deltaTime;
            if (visual && !_taken) { visual.localPosition = Vector3.up * (0.12f + Mathf.Sin(_t * 2f) * 0.04f); visual.localRotation = Quaternion.Euler(0f, _t * 40f, 0f); }
            if (glow) glow.intensity = _taken ? Mathf.MoveTowards(glow.intensity, 0f, Time.deltaTime * 3f) : 1.6f + Mathf.Sin(_t * 3.1f) * 0.3f;
            if (!_taken && GameInput.InteractPressed && LocalCanInteract(out _)) Take();
        }

        void Take()
        {
            var p = PlayerController.Local;
            if (p == null || p.vitals == null) return;
            _taken = true;
            p.vitals.AddFlaskCharge();
            if (visual) visual.gameObject.SetActive(false);
            Sfx.Play(SfxId.RelicReveal, transform.position + Vector3.up, 0.7f, 1.4f);
            Fx.Spawn(FxId.Embers, transform.position + Vector3.up * 0.5f);
            Taken?.Invoke(p.vitals.maxFlasks);
        }
    }
}
