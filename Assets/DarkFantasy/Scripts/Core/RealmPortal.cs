using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The way into the Realm of the Red Moon: once the Mirror is shattered, its empty frame becomes a
    /// door, a slow vortex of dark light where the glass used to be. Stepping through (E) carries the
    /// local player to the other side. The same component, with <see cref="requiresMirror"/> off, is
    /// the way back from the realm. Owner-authoritative movement: each player moves itself.
    /// </summary>
    public class RealmPortal : MonoBehaviour, IInteractable
    {
        [Tooltip("Visible / usable only after the Mirror falls.")]
        public bool requiresMirror = true;
        public Transform destination;
        public string prompt = "Step through the broken mirror";
        public float interactDistance = 4.2f;
        [Tooltip("The swirl, its light and its motes (shown when the portal is open).")]
        public GameObject openVisual;
        public Light openLight;
        public Transform swirl;

        float _cool, _open;

        void OnEnable() { if (!Interactables.All.Contains(this)) Interactables.All.Add(this); }
        void OnDisable() => Interactables.All.Remove(this);

        bool Open
        {
            get
            {
                if (!requiresMirror) return true;
                var prog = CastleProgress.Instance;
                if (prog == null || !prog.IsSpawned || !prog.MirrorSlain) return false;
                // while the dead Mirror still collapses, wait for its fall
                var m = MirrorAI.Instance;
                return m == null || !m.IsSpawned || !m.IsAlive;
            }
        }

        public bool LocalCanInteract(out string text)
        {
            text = null;
            var p = PlayerController.Local;
            if (p == null || !p.CanInteract || _cool > 0f || !Open || _open < 0.9f || destination == null) return false;
            Vector3 d = p.transform.position - transform.position; d.y = 0f;
            if (d.magnitude > interactDistance || Mathf.Abs(p.transform.position.y - transform.position.y) > 3f) return false;
            text = prompt;
            return true;
        }

        void Update()
        {
            if (_cool > 0f) _cool -= Time.deltaTime;
            bool open = Open;
            _open = Mathf.MoveTowards(_open, open ? 1f : 0f, Time.deltaTime * 0.35f);
            if (openVisual && openVisual.activeSelf != _open > 0.01f) openVisual.SetActive(_open > 0.01f);
            if (openVisual) openVisual.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, _open);
            if (openLight) openLight.intensity = 6f * _open * (0.85f + Mathf.Sin(Time.time * 2.1f) * 0.15f);
            if (swirl) swirl.Rotate(0f, 0f, -40f * Time.deltaTime, Space.Self);

            if (GameInput.InteractPressed && LocalCanInteract(out _))
            {
                _cool = 3f;
                var p = PlayerController.Local;
                Vector3 at = destination.position + destination.right * (((int)p.OwnerClientId % 4) - 1.5f) * 1.2f;
                Sfx.Play2D(SfxId.MirrorVortex, 0.9f, 0.8f);
                Sfx.Play2D(SfxId.FogPass, 1f);
                MirrorFx.ShardBurst(p.transform.position + Vector3.up, 16, 4f, 0.7f);
                p.TeleportTo(at, destination.rotation);
                SoulsCamera.Shake(0.05f, 0.5f);
            }
        }
    }
}
