using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// A glinting item left in a side area (a corpse's satchel, an offering bowl): souls, a draught or an
    /// Ember Flask. Local and per player, like the hidden flasks: each player takes it once per session.
    /// </summary>
    public class LootPickup : MonoBehaviour, IInteractable
    {
        public enum Reward : byte { Souls, IronDraught, VigorTonic, SoulSalve, Flask }
        public Reward reward;
        public int amount = 1;
        public string itemName = "Sack of Souls";
        public Transform visual;
        public Light glow;
        public float interactDistance = 1.9f;
        bool _taken;
        float _t;

        public static event System.Action<string, string> Looted;   // title, description

        void OnEnable() { if (!Interactables.All.Contains(this)) Interactables.All.Add(this); }
        void OnDisable() => Interactables.All.Remove(this);

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            if (_taken || p == null || !p.CanInteract) return false;
            if (Vector3.Distance(p.transform.position, transform.position) > interactDistance) return false;
            prompt = "Take " + itemName;
            return true;
        }

        void Update()
        {
            _t += Time.deltaTime;
            if (glow) glow.intensity = _taken ? Mathf.MoveTowards(glow.intensity, 0f, Time.deltaTime * 3f) : 1.2f + Mathf.Sin(_t * 2.7f) * 0.35f;
            if (!_taken && GameInput.InteractPressed && LocalCanInteract(out _)) Take();
        }

        void Take()
        {
            var p = PlayerController.Local;
            if (p == null) return;
            _taken = true;
            string desc;
            switch (reward)
            {
                case Reward.Souls: PlayerSkills.AddSouls(amount); desc = $"+{amount} souls"; break;
                case Reward.Flask: if (p.vitals) p.vitals.AddFlaskCharge(); desc = "+1 Ember Flask"; break;
                default:
                    var k = reward == Reward.IronDraught ? Potions.Kind.IronDraught : reward == Reward.VigorTonic ? Potions.Kind.VigorTonic : Potions.Kind.SoulSalve;
                    Potions.Add(k, amount); desc = $"{Potions.Name(k)} ×{amount}"; break;
            }
            if (visual) visual.gameObject.SetActive(false);
            Sfx.Play(SfxId.RelicReveal, transform.position + Vector3.up, 0.6f, 1.6f);
            Fx.Spawn(FxId.Embers, transform.position + Vector3.up * 0.4f);
            Looted?.Invoke(itemName.ToUpperInvariant(), desc);
        }
    }
}
