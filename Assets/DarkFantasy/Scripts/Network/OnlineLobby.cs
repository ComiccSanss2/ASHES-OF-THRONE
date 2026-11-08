using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Online lobbies through Unity Gaming Services (Multiplayer Services: Lobby + Relay).
    /// Anyone running the game sees public lobbies in "Find Lobbies" and joins with one click;
    /// traffic goes through Unity Relay, so nobody has to open ports on the router.
    /// The SDK starts Netcode (host / client) itself once the session is created or joined.
    /// </summary>
    public static class OnlineLobby
    {
        public struct Entry
        {
            public string id, name, host;
            public int players, max;
            public bool started;
        }

        public static ISession Current { get; private set; }
        public static bool IsOnline => Current != null;
        public static string LastError { get; private set; } = "";
        public static string State { get; private set; } = "";
        public static readonly List<Entry> Found = new List<Entry>();
        public static bool Querying { get; private set; }
        public static bool HasQueried { get; private set; }

        static Task _init;
        static float _nextQuery;

        // ------------------------------------------------------------------ sign-in
        public static Task EnsureReady()
        {
            if (_init == null || _init.IsFaulted || _init.IsCanceled) _init = InitAsync();
            return _init;
        }

        static async Task InitAsync()
        {
            State = "Connecting to the online services…";
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var opts = new InitializationOptions();
                // a fresh anonymous identity per running copy: two copies on the same PC are two players
                opts.SetProfile("k" + Guid.NewGuid().ToString("N").Substring(0, 16));
                await UnityServices.InitializeAsync(opts);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            State = "";
            Debug.Log("[DF] Online services ready (player " + AuthenticationService.Instance.PlayerId + ")");
        }

        // ------------------------------------------------------------------ host
        public static async Task<bool> CreateAsync(string lobbyName, int maxPlayers, string hostName)
        {
            try
            {
                await EnsureReady();
                State = "Opening the online lobby…";
                var options = new SessionOptions
                {
                    Name = string.IsNullOrWhiteSpace(lobbyName) ? "Lobby" : lobbyName.Trim(),
                    MaxPlayers = Mathf.Clamp(maxPlayers, 1, 4),
                    IsPrivate = false,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        { "host", new SessionProperty(Trim(hostName, 24), VisibilityPropertyOptions.Public) },
                        { "started", new SessionProperty("0", VisibilityPropertyOptions.Public) },
                    },
                }.WithRelayNetwork();
                Current = await MultiplayerService.Instance.CreateSessionAsync(options);
                State = "";
                Debug.Log($"[DF] Online lobby '{Current.Name}' created (code {Current.Code})");
                return true;
            }
            catch (Exception e) { return Fail("Could not open the online lobby", e); }
        }

        /// <summary>Host: lists the lobby as "in game" (late joiners can still enter).</summary>
        public static async void MarkStarted()
        {
            try
            {
                if (Current == null || !Current.IsHost) return;
                var h = Current.AsHost();
                h.SetProperty("started", new SessionProperty("1", VisibilityPropertyOptions.Public));
                await h.SavePropertiesAsync();
            }
            catch (Exception e) { Debug.LogWarning("[DF] Online lobby update failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ browse / join
        /// <summary>Refreshes the public lobby list every few seconds while the browser is open.</summary>
        public static void PollIfDue()
        {
            if (Querying || Time.unscaledTime < _nextQuery) return;
            _nextQuery = Time.unscaledTime + 4f;
            _ = QueryAsync();
        }

        public static void ForceRefresh() => _nextQuery = 0f;

        static async Task QueryAsync()
        {
            Querying = true;
            try
            {
                await EnsureReady();
                var res = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
                Found.Clear();
                foreach (var s in res.Sessions)
                {
                    var e = new Entry { id = s.Id, name = s.Name, max = s.MaxPlayers, players = s.MaxPlayers - s.AvailableSlots, host = "?" };
                    if (s.Properties != null)
                    {
                        if (s.Properties.TryGetValue("host", out var h) && h != null) e.host = h.Value;
                        if (s.Properties.TryGetValue("started", out var st) && st != null) e.started = st.Value == "1";
                    }
                    Found.Add(e);
                }
                LastError = "";
                HasQueried = true;
            }
            catch (Exception e) { Fail("Online search failed", e); }
            finally { Querying = false; }
        }

        public static async Task<bool> JoinAsync(string sessionId)
        {
            try
            {
                await EnsureReady();
                State = "Joining the lobby…";
                Current = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
                State = "";
                Debug.Log($"[DF] Joined online lobby '{Current.Name}'");
                return true;
            }
            catch (Exception e) { return Fail("Could not join", e); }
        }

        public static async void Leave()
        {
            var s = Current;
            Current = null;
            if (s == null) return;
            try { await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("[DF] Leaving the online lobby: " + e.Message); }
        }

        // ------------------------------------------------------------------ helpers
        static bool Fail(string what, Exception e)
        {
            State = "";
            string msg = e.Message;
            if (e is SessionException se) msg = se.Error + ": " + se.Message;
            LastError = what + ": " + Trim(msg, 120);
            Debug.LogWarning("[DF] " + what + ": " + e);
            return false;
        }

        static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length > n ? s.Substring(0, n) : s;
    }
}
