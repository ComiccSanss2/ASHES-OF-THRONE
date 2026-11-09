using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The Field of the Red Moon: fog gate at the threshold of the Reaper's court (same rules as the
    /// other boss rooms). First player through wakes Death; nobody alive inside for 3 s → it resets.
    /// </summary>
    public class ReaperArena : NetworkBehaviour
    {
        public static ReaperArena Instance { get; private set; }

        public Renderer fogRenderer;
        public Collider fogCollider;
        public Light fogLight;
        public float interactDistance = 2.6f;
        public float traverseDepth = 2.4f;
        public float halfWidth = 2.8f;

        readonly NetworkVariable<BossArena.ArenaState> _state = new NetworkVariable<BossArena.ArenaState>(BossArena.ArenaState.Idle);
        public BossArena.ArenaState State => _state.Value;

        public static event System.Action<int> FightStarted;
        public static event System.Action Defeated;
        public static event System.Action FightReset;

        float _noPlayersTimer, _fogAlpha = 1f;
        MaterialPropertyBlock _mpb;

        void Awake() { Instance = this; _mpb = new MaterialPropertyBlock(); }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _state.OnValueChanged += OnStateChanged;
            Apply(_state.Value, true);
            if (IsServer && CastleProgress.Instance != null && CastleProgress.Instance.ReaperSlain) ServerSkipDefeated();
        }

        /// <summary>Server: Death already fell in this lobby.</summary>
        public void ServerSkipDefeated()
        {
            if (!IsServer || !IsSpawned) return;
            _state.Value = BossArena.ArenaState.Defeated;
            var d = ReaperBossAI.Instance;
            if (d != null && d.IsSpawned) d.NetworkObject.Despawn(true);
        }

        public override void OnNetworkDespawn() => _state.OnValueChanged -= OnStateChanged;
        void OnStateChanged(BossArena.ArenaState a, BossArena.ArenaState b) => Apply(b, false);

        void Apply(BossArena.ArenaState s, bool instant)
        {
            if (fogCollider) fogCollider.enabled = s != BossArena.ArenaState.Defeated;
            if (instant) _fogAlpha = s == BossArena.ArenaState.Defeated ? 0f : 1f;
        }

        public bool IsOutside(Vector3 p) => Vector3.Dot(p - transform.position, transform.forward) < 0f;

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            if (p == null || !p.CanInteract || _state.Value == BossArena.ArenaState.Defeated) return false;
            Vector3 local = transform.InverseTransformPoint(p.transform.position);
            if (Mathf.Abs(local.x) > halfWidth || Mathf.Abs(local.z) > interactDistance) return false;
            bool outside = local.z < 0f;
            if (outside && _state.Value == BossArena.ArenaState.Fighting && p.IsBannedFrom(4)) { prompt = "You fell in this battle · the fog rejects you"; return false; }
            if (!outside && _state.Value == BossArena.ArenaState.Fighting) { prompt = "The fog holds you back…"; return false; }
            prompt = outside ? "Traverse the fog" : "Leave through the fog";
            return true;
        }

        void Update()
        {
            float target = _state.Value == BossArena.ArenaState.Defeated ? 0f : 1f;
            _fogAlpha = Mathf.MoveTowards(_fogAlpha, target, Time.deltaTime * 0.5f);
            if (fogRenderer)
            {
                fogRenderer.enabled = _fogAlpha > 0.01f;
                fogRenderer.GetPropertyBlock(_mpb);
                float pulse = _state.Value == BossArena.ArenaState.Fighting ? 0.85f + Mathf.Sin(Time.time * 3f) * 0.1f : 0.7f;
                _mpb.SetColor("_BaseColor", new Color(0.85f, 0.25f, 0.3f, _fogAlpha * pulse));
                fogRenderer.SetPropertyBlock(_mpb);
            }
            if (fogLight) fogLight.intensity = 1.2f * _fogAlpha;

            if (IsSpawned && GameInput.InteractPressed && LocalCanInteract(out _))
            {
                var p = PlayerController.Local;
                bool outside = IsOutside(p.transform.position);
                Vector3 from = transform.position + transform.forward * (outside ? -1.1f : 1.1f);
                Vector3 to = transform.position + transform.forward * (outside ? traverseDepth : -traverseDepth);
                from.y = to.y = p.transform.position.y;
                if (p.BeginTraverse(from, to) && outside) EnteredRpc();
            }

            if (!IsServer || !IsSpawned || _state.Value != BossArena.ArenaState.Fighting) return;
            var d = ReaperBossAI.Instance;
            if (d == null) return;
            if (!d.IsAlive)
            {
                _state.Value = BossArena.ArenaState.Defeated;
                VictoryRpc();
                CastleProgress.Instance?.ServerReaperSlain();
                return;
            }
            bool any = false;
            var lv = LevelRefs.Instance;
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && (lv == null || lv.IsInsideReaperArena(p.transform.position))) { any = true; break; }
            _noPlayersTimer = any ? 0f : _noPlayersTimer + Time.deltaTime;
            if (_noPlayersTimer > 3f)
            {
                d.ResetReaper();
                _state.Value = BossArena.ArenaState.Idle;
                ResetRpc();
            }
        }

        /// <summary>Development (editor tools, F9 menu): starts the fight as if a player crossed the fog.</summary>
        public void DevStartFight(string _) => EnteredRpc();

        [Rpc(SendTo.Server)]
        void EnteredRpc()
        {
            if (_state.Value != BossArena.ArenaState.Idle) return;
            var d = ReaperBossAI.Instance;
            if (d == null || !d.IsAlive) return;
            int players = NetworkManager.Singleton.ConnectedClientsIds.Count;
            d.Activate(players);
            _state.Value = BossArena.ArenaState.Fighting;
            _noPlayersTimer = 0f;
            StartedRpc(players);
        }

        [Rpc(SendTo.Everyone)] void StartedRpc(int players) => FightStarted?.Invoke(players);
        [Rpc(SendTo.Everyone)] void VictoryRpc() { Sfx.Play2D(SfxId.Victory); Defeated?.Invoke(); }
        [Rpc(SendTo.Everyone)] void ResetRpc() => FightReset?.Invoke();
    }
}
