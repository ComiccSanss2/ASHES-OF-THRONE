using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Arena del boss con Cancello di Nebbia (Fog Gate).
    ///  - Il cancello è un muro di nebbia solido: si attraversa con "Interagisci" (E / A).
    ///  - Quando il PRIMO giocatore entra, il server avvia lo scontro: conta i giocatori connessi in
    ///    quel momento e scala HP/postura del boss (HP_Finale = HP_Base × (1 + (N − 1) × 0.6)).
    ///  - Durante lo scontro non si può uscire (gli altri possono ancora entrare ad aiutare).
    ///  - Se nessun giocatore vivo resta nell'arena per 3 s, il boss si resetta.
    ///  - Alla morte del boss la nebbia si dissolve.
    /// Lo stato è una NetworkVariable: chi si connette dopo vede la situazione corretta.
    /// </summary>
    public class BossArena : NetworkBehaviour
    {
        public enum ArenaState : byte { Idle, Fighting, Defeated }

        public static BossArena Instance { get; private set; }

        [Header("Cancello")]
        public Renderer fogRenderer;
        public Collider fogCollider;
        public Light fogLight;
        public float interactDistance = 2.6f;
        public float traverseDepth = 2.4f;

        readonly NetworkVariable<ArenaState> _state = new NetworkVariable<ArenaState>(ArenaState.Idle);

        public ArenaState State => _state.Value;
        float _noPlayersTimer;
        float _fogAlpha = 1f;
        MaterialPropertyBlock _mpb;

        /// <summary>Eventi per la UI (scatenati su tutti i client).</summary>
        public static event System.Action<int> FightStarted;
        public static event System.Action BossDefeated;
        public static event System.Action FightReset;

        void Awake() { Instance = this; _mpb = new MaterialPropertyBlock(); }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _state.OnValueChanged += OnStateChanged;
            ApplyVisuals(_state.Value, true);
        }

        public override void OnNetworkDespawn() => _state.OnValueChanged -= OnStateChanged;

        void OnStateChanged(ArenaState prev, ArenaState now) => ApplyVisuals(now, false);

        void ApplyVisuals(ArenaState s, bool instant)
        {
            if (fogCollider) fogCollider.enabled = s != ArenaState.Defeated;
            if (instant) _fogAlpha = s == ArenaState.Defeated ? 0f : 1f;
        }

        /// <summary>Vero se il punto è dal lato esterno del cancello (forward = verso l'interno).</summary>
        public bool IsOutside(Vector3 p) => Vector3.Dot(p - transform.position, transform.forward) < 0f;

        public bool LocalCanInteract(out string prompt)
        {
            prompt = null;
            var p = PlayerController.Local;
            if (p == null || !p.CanInteract || _state.Value == ArenaState.Defeated) return false;
            Vector3 local = transform.InverseTransformPoint(p.transform.position);
            if (Mathf.Abs(local.x) > 2.6f || Mathf.Abs(local.z) > interactDistance) return false;
            bool outside = local.z < 0f;
            if (outside && _state.Value == ArenaState.Fighting && p.IsBannedFrom(1)) { prompt = "You fell in this battle · the fog rejects you"; return false; }
            if (!outside && _state.Value == ArenaState.Fighting) { prompt = "The fog holds you back…"; return false; }
            prompt = outside ? "Traverse the fog" : "Leave through the fog";
            return true;
        }

        void Update()
        {
            // ---- visuale della nebbia (tutti)
            float targetAlpha = _state.Value == ArenaState.Defeated ? 0f : 1f;
            _fogAlpha = Mathf.MoveTowards(_fogAlpha, targetAlpha, Time.deltaTime * 0.5f);
            if (fogRenderer)
            {
                fogRenderer.enabled = _fogAlpha > 0.01f;
                fogRenderer.GetPropertyBlock(_mpb);
                float pulse = _state.Value == ArenaState.Fighting ? 0.85f + Mathf.Sin(Time.time * 3f) * 0.1f : 0.7f;
                _mpb.SetColor("_BaseColor", new Color(0.78f, 0.76f, 0.7f, _fogAlpha * pulse));
                fogRenderer.SetPropertyBlock(_mpb);
            }
            if (fogLight) fogLight.intensity = 1.2f * _fogAlpha;

            // ---- interazione del giocatore locale
            if (IsSpawned && GameInput.InteractPressed && LocalCanInteract(out _))
            {
                var p = PlayerController.Local;
                bool outside = IsOutside(p.transform.position);
                Vector3 from = transform.position + transform.forward * (outside ? -1.1f : 1.1f);
                Vector3 to = transform.position + transform.forward * (outside ? traverseDepth : -traverseDepth);
                from.y = to.y = p.transform.position.y;
                if (p.BeginTraverse(from, to) && outside) EnteredArenaRpc();
            }

            if (!IsServer || !IsSpawned) return;
            ServerUpdate(Time.deltaTime);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Development: starts the boss fight as if a player crossed the fog.</summary>
        public void DevStartFight(string _) => EnteredArenaRpc();
#endif

        [Rpc(SendTo.Server)]
        void EnteredArenaRpc()
        {
            if (_state.Value != ArenaState.Idle) return;
            var boss = BossAI.Instance;
            if (boss == null || !boss.IsAlive) return;
            int players = NetworkManager.Singleton.ConnectedClientsIds.Count;
            boss.Activate(players);
            _state.Value = ArenaState.Fighting;
            _noPlayersTimer = 0f;
            FightStartedRpc(players);
        }

        void ServerUpdate(float dt)
        {
            if (_state.Value != ArenaState.Fighting) return;
            var boss = BossAI.Instance;
            if (boss == null) return;
            if (!boss.IsAlive)
            {
                _state.Value = ArenaState.Defeated;
                VictoryRpc();
                NetworkBootstrap.Instance?.ServerSpawnRewardChest();
                CastleProgress.Instance?.ServerOpenThroneDoor();
                return;
            }
            bool anyAliveInside = false;
            var lv = LevelRefs.Instance;
            foreach (var p in PlayerController.All)
                if (p && !p.IsDead && (lv == null || lv.IsInsideArena(p.transform.position))) { anyAliveInside = true; break; }
            _noPlayersTimer = anyAliveInside ? 0f : _noPlayersTimer + dt;
            if (_noPlayersTimer > 3f)
            {
                boss.ResetBoss();
                _state.Value = ArenaState.Idle;
                ResetRpc();
            }
        }

        [Rpc(SendTo.Everyone)] void FightStartedRpc(int players) => FightStarted?.Invoke(players);
        [Rpc(SendTo.Everyone)] void VictoryRpc() { Sfx.Play2D(SfxId.Victory); BossDefeated?.Invoke(); }
        [Rpc(SendTo.Everyone)] void ResetRpc() => FightReset?.Invoke();

        /// <summary>Server: the session starts past the throne room (the King lies dead, the fog is gone).</summary>
        public void ServerSkipDefeated()
        {
            if (!IsServer) return;
            _state.Value = ArenaState.Defeated;
            var boss = BossAI.Instance;
            if (boss != null && boss.IsSpawned) boss.NetworkObject.Despawn(true);
        }
    }
}
