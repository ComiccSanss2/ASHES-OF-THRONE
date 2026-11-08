using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Finds lobbies without typing an IP: the host broadcasts a small UDP beacon every second on
    /// the local network (and on VPN adapters such as Radmin / ZeroTier that carry broadcasts);
    /// the lobby browser listens and lists them. Internet games still use "Join by IP".
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public const int Port = 47777;
        const string Magic = "DFLOBBY1";

        public class Entry
        {
            public string name, address, host;
            public ushort port;
            public int players, max;
            public bool started;
            public float lastSeen;
            public string Key => address + ":" + port;
        }

        public static LanDiscovery Instance { get; private set; }
        public readonly List<Entry> Found = new List<Entry>();

        UdpClient _listen, _send;
        bool _hosting;
        float _beaconTimer;
        Func<string> _beacon;

        void Awake() => Instance = this;
        void OnDestroy() { StopListening(); StopBroadcast(); }

        // ------------------------------------------------------------------ host
        public void StartBroadcast(Func<string> beacon)
        {
            _beacon = beacon;
            _hosting = true;
            try { _send ??= new UdpClient { EnableBroadcast = true }; }
            catch (Exception e) { Debug.LogWarning("[DF] LAN broadcast unavailable: " + e.Message); _hosting = false; }
        }

        public void StopBroadcast()
        {
            _hosting = false;
            _send?.Close(); _send = null;
        }

        public static string MakeBeacon(string lobby, string host, ushort port, int players, int max, bool started)
            => string.Join("|", Magic, Clean(lobby), Clean(host), port, players, max, started ? 1 : 0);

        static string Clean(string s) => (s ?? "").Replace("|", "/");

        // ------------------------------------------------------------------ browser
        public void StartListening()
        {
            if (_listen != null) return;
            try
            {
                _listen = new UdpClient();
                _listen.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listen.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                _listen.EnableBroadcast = true;
            }
            catch (Exception e) { Debug.LogWarning("[DF] LAN discovery unavailable: " + e.Message); _listen = null; }
        }

        public void StopListening()
        {
            _listen?.Close(); _listen = null;
        }

        public bool IsListening => _listen != null;

        void Update()
        {
            if (_hosting && _send != null)
            {
                _beaconTimer -= Time.unscaledDeltaTime;
                if (_beaconTimer <= 0f)
                {
                    _beaconTimer = 1f;
                    var data = Encoding.UTF8.GetBytes(_beacon != null ? _beacon() : Magic);
                    foreach (var ep in BroadcastTargets())
                        try { _send.Send(data, data.Length, ep); } catch { /* adapter down */ }
                }
            }
            if (_listen != null)
            {
                try
                {
                    while (_listen.Available > 0)
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        var bytes = _listen.Receive(ref from);
                        Parse(Encoding.UTF8.GetString(bytes), from.Address.ToString());
                    }
                }
                catch { /* socket closed */ }
                for (int i = Found.Count - 1; i >= 0; i--)
                    if (Time.unscaledTime - Found[i].lastSeen > 4f) Found.RemoveAt(i);
            }
        }

        void Parse(string msg, string address)
        {
            var p = msg.Split('|');
            if (p.Length < 7 || p[0] != Magic) return;
            if (!ushort.TryParse(p[3], out ushort port)) return;
            bool loop = address == "127.0.0.1";
            // the same lobby heard twice (loopback + LAN on the host's own PC): keep the LAN address
            var twin = Found.Find(x => x.port == port && x.name == p[1] && x.address != address);
            if (twin != null)
            {
                if (loop) { twin.lastSeen = Time.unscaledTime; return; }
                if (twin.address == "127.0.0.1") Found.Remove(twin);
            }
            var e = Found.Find(x => x.address == address && x.port == port);
            if (e == null) { e = new Entry { address = address, port = port }; Found.Add(e); }
            e.name = p[1]; e.host = p[2];
            int.TryParse(p[4], out e.players); int.TryParse(p[5], out e.max);
            e.started = p[6] == "1";
            e.lastSeen = Time.unscaledTime;
        }

        /// <summary>255.255.255.255 plus the directed broadcast of every IPv4 adapter (VPNs included).</summary>
        static List<IPEndPoint> BroadcastTargets()
        {
            var list = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, Port) };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        var ip = ua.Address.GetAddressBytes(); var mask = ua.IPv4Mask.GetAddressBytes();
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        list.Add(new IPEndPoint(new IPAddress(b), Port));
                    }
                }
            }
            catch { /* best effort */ }
            list.Add(new IPEndPoint(IPAddress.Loopback, Port));   // same-PC test instances
            return list;
        }
    }
}
