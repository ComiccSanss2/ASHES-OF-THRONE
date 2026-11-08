using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// A flame to rest at (Dark Souls style): lit once the party finds it. Resting (E) heals the
    /// player, refills the flasks and the mana, makes this flame the party's respawn point — and
    /// every fallen enemy rises again.
    /// </summary>
    public class CheckpointFire : MonoBehaviour, IInteractable
    {
        public int index;
        public GameObject litVisual;
        public Light litLight;
        public float interactDistance = 2.8f;

        float _cool;

        void OnEnable() { if (!Interactables.All.Contains(this)) Interactables.All.Add(this); CastleProgress.Rested += OnRested; }
        void OnDisable() { Interactables.All.Remove(this); CastleProgress.Rested -= OnRested; }

        bool Lit
        {
            get
            {
                var prog = CastleProgress.Instance;
                return index == 0 || (prog != null && prog.IsSpawned && prog.IsDiscovered(index));
            }
        }

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            var prog = CastleProgress.Instance;
            if (p == null || prog == null || !prog.IsSpawned || !p.CanInteract || _cool > 0f) return false;
            Vector3 d = p.transform.position - transform.position; d.y = 0f;
            if (d.magnitude > interactDistance) return false;
            prompt = Lit ? "Rest at the flame" : "Kindle the flame";
            return true;
        }

        void Update()
        {
            if (_cool > 0f) _cool -= Time.deltaTime;
            bool lit = Lit;
            if (litVisual && litVisual.activeSelf != lit) litVisual.SetActive(lit);
            if (litLight) litLight.enabled = lit;
            if (GameInput.InteractPressed && LocalCanInteract(out _))
            {
                _cool = 2.5f;
                PlayerController.Local.RestAtFlame(this);
                CastleProgress.Instance.RestRpc((byte)index);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Dev: rest as if the local player pressed E here.</summary>
        public void DevRest(string _) { if (PlayerController.Local && CastleProgress.Instance) { PlayerController.Local.RestAtFlame(this); CastleProgress.Instance.RestRpc((byte)index); } }
#endif

        void OnRested(int i)
        {
            if (i != index) return;
            Vector3 p = transform.position + Vector3.up * 1.1f;
            Fx.Spawn(FxId.Embers, p);
            Fx.SpawnScaled(FxId.Fireball, p, 0.6f);
            Sfx.Play(SfxId.Rest, p, 1f);
            SpellFx.GroundRing(transform.position, 0.5f, 5f, new Color(1f, 0.6f, 0.25f), 1.2f);
        }
    }
}
