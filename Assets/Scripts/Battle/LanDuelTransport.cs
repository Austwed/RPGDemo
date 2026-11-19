using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace BaseConquest.Battle
{
    public sealed class LanDuelRoom
    {
        public string Name { get; internal set; }
        public string Address { get; internal set; }
        public int Port { get; internal set; }
        internal string Id;
        internal double LastSeen;
    }

    // Two peers, one ordered stream. Discovery uses a separate socket and never carries battle data.
    // All operations are pumped by the owner on the main thread, including the initial handshake.
    public sealed class LanDuelTransport : IDisposable
    {
        public const int DefaultPort = 47771;
        private const int DiscoveryPort = 47772;
        private const int MaxFrame = 512 * 1024;
        private const int MaxQueuedBytes = 2 * 1024 * 1024;
        private const string Discovery = "DEMO-LAN-DUEL-1";
        private static readonly byte[] Hello = Encoding.ASCII.GetBytes("DEMODL01");
        private static readonly byte[] Find = Encoding.ASCII.GetBytes(Discovery + "|find");
        private static readonly byte[] Pulse = new byte[4];
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<LanDuelRoom> rooms = new List<LanDuelRoom>();
        private readonly List<IPEndPoint> discoveryTargets = new List<IPEndPoint>();
        private readonly Queue<byte[]> outgoing = new Queue<byte[]>();
        private readonly Queue<string> incoming = new Queue<string>();
        private readonly byte[] discoveryBuffer = new byte[768];
        private byte[] wireBuffer = new byte[16384];
        private Socket listener, peer, discovery;
        private bool connecting, browsing;
        private int received, sentOffset, pendingBytes;
        private double lastReceived, lastWrite, nextPing;
        private byte[] advertisement;

        public bool IsHost { get; private set; }
        public bool Connected { get; private set; }
        public string Error { get; private set; } = "";
        public string LocalAddress { get; private set; } = "127.0.0.1";
        public int Port { get; private set; } = DefaultPort;
        public int RoomsRevision { get; private set; }
        public IReadOnlyList<LanDuelRoom> Rooms => rooms;
        private double Now => clock.Elapsed.TotalSeconds;

        public void Browse()
        {
            Reset();
            browsing = true;
            RefreshInterfaces();
            OpenDiscovery();
        }

        public void Host(string campName, int port = DefaultPort)
        {
            Reset();
            if (!ValidPort(port)) { Fail("房间端口无效。"); return; }
            IsHost = true;
            Port = port;
            RefreshInterfaces();
            try
            {
                listener = NewSocket(SocketType.Stream);
                listener.Bind(new IPEndPoint(IPAddress.Any, port));
                listener.Listen(2);
                string name = string.IsNullOrWhiteSpace(campName) ? "营地" : campName.Trim();
                if (name.Length > 48) name = name.Substring(0, char.IsHighSurrogate(name[47]) ? 47 : 48);
                advertisement = Utf8.GetBytes(Discovery + "|room|" + Guid.NewGuid().ToString("N") + "|" +
                    Port + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(name)));
                OpenDiscovery();
            }
            catch (SocketException ex) { Fail("无法开启房间（" + ex.SocketErrorCode + "）。"); }
        }

        public void Join(string ipv4, int port = DefaultPort)
        {
            Reset();
            string target = (ipv4 ?? "").Trim();
            int colon = target.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(target.Substring(colon + 1), out port)) port = 0;
                target = target.Substring(0, colon);
            }
            if (!ValidPort(port) || !IPAddress.TryParse(target, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            { Fail("请输入房主的 IPv4 地址和有效端口。"); return; }
            Port = port;
            try
            {
                peer = NewSocket(SocketType.Stream);
                connecting = true;
                lastReceived = Now;
                peer.Connect(new IPEndPoint(ip, port));
                StartStream();
            }
            catch (SocketException ex) when (WouldWait(ex.SocketErrorCode)) { }
            catch (SocketException ex) { Fail("连接房间失败（" + ex.SocketErrorCode + "）。"); }
        }

        public void Poll()
        {
            if (discovery != null) PollDiscovery();
            try
            {
                if (listener != null && peer == null && listener.Poll(0, SelectMode.SelectRead))
                {
                    peer = listener.Accept();
                    StartStream();
                }
                if (peer == null) return;
                if (Now - lastReceived > 12) { Fail("连接超时，已离开演武。"); return; }
                if (connecting)
                {
                    if (!peer.Poll(0, SelectMode.SelectWrite) && !peer.Poll(0, SelectMode.SelectError)) return;
                    int error = (int)peer.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                    if (error != 0) { Fail("连接房间失败（" + (SocketError)error + "）。"); return; }
                    StartStream();
                }
                if (Connected && pendingBytes == 0 && Now - lastWrite >= 1) Enqueue(Pulse);
                Flush();
                ReadStream();
            }
            catch (SocketException ex) when (WouldWait(ex.SocketErrorCode)) { }
            catch (SocketException ex) { Fail("与对方的连接已中断（" + ex.SocketErrorCode + "）。"); }
            catch (DecoderFallbackException) { Fail("收到无法识别的对局消息。"); }
        }

        public bool Send(string payload)
        {
            if (!Connected || string.IsNullOrEmpty(payload)) return false;
            int size = Utf8.GetByteCount(payload);
            if (size > MaxFrame || pendingBytes + size + 4 > MaxQueuedBytes)
            { Fail("对局数据积压，已离开演武。"); return false; }
            var data = new byte[size + 4];
            data[0] = (byte)(size >> 24); data[1] = (byte)(size >> 16);
            data[2] = (byte)(size >> 8); data[3] = (byte)size;
            Utf8.GetBytes(payload, 0, payload.Length, data, 4);
            Enqueue(data);
            try { Flush(); }
            catch (SocketException ex) when (WouldWait(ex.SocketErrorCode)) { }
            catch (SocketException ex) { Fail("发送失败（" + ex.SocketErrorCode + "）。"); return false; }
            return true;
        }

        public bool TryReceive(out string payload)
        {
            payload = incoming.Count > 0 ? incoming.Dequeue() : null;
            return payload != null;
        }

        private static bool ValidPort(int port) => port >= 1 && port <= 65535;
        private static bool WouldWait(SocketError error) =>
            error == SocketError.WouldBlock || error == SocketError.IOPending ||
            error == SocketError.InProgress || error == SocketError.AlreadyInProgress;

        private static Socket NewSocket(SocketType type) =>
            new Socket(AddressFamily.InterNetwork, type, type == SocketType.Stream ? ProtocolType.Tcp : ProtocolType.Udp) { Blocking = false };

        private void StartStream()
        {
            connecting = false;
            peer.Blocking = false;
            peer.NoDelay = true;
            lastReceived = Now;
            Enqueue(Hello);
        }

        private void Enqueue(byte[] bytes) { outgoing.Enqueue(bytes); pendingBytes += bytes.Length; }

        private void Flush()
        {
            int budget = 128 * 1024;
            while (outgoing.Count > 0 && budget > 0 && peer.Poll(0, SelectMode.SelectWrite))
            {
                var head = outgoing.Peek();
                int sent = peer.Send(head, sentOffset, Math.Min(budget, head.Length - sentOffset), SocketFlags.None);
                if (sent <= 0) break;
                sentOffset += sent;
                pendingBytes -= sent;
                budget -= sent;
                lastWrite = Now;
                if (sentOffset == head.Length) { outgoing.Dequeue(); sentOffset = 0; }
            }
        }

        private void ReadStream()
        {
            int budget = 192 * 1024;
            while (budget > 0 && peer != null && peer.Poll(0, SelectMode.SelectRead))
            {
                if (received == wireBuffer.Length)
                    Array.Resize(ref wireBuffer, Math.Min(MaxFrame + 4, 2 * received));
                int n = peer.Receive(wireBuffer, received, Math.Min(budget, wireBuffer.Length - received), SocketFlags.None);
                if (n == 0) { Fail("对方已离开演武。"); return; }
                received += n;
                budget -= n;
                lastReceived = Now;
                int offset = 0;
                if (!Connected)
                {
                    if (received < Hello.Length) continue;
                    for (int i = 0; i < Hello.Length; i++)
                        if (wireBuffer[i] != Hello[i]) { Fail("房间版本不匹配。"); return; }
                    Connected = true;
                    offset = Hello.Length;
                }
                while (received - offset >= 4)
                {
                    int len = wireBuffer[offset] << 24 | wireBuffer[offset+1] << 16 |
                        wireBuffer[offset+2] << 8 | wireBuffer[offset+3];
                    if (len < 0 || len > MaxFrame) { Fail("收到无效的对局消息。"); return; }
                    if (received - offset - 4 < len) break;
                    offset += 4;
                    if (len > 0)
                    {
                        if (incoming.Count >= 256) { Fail("对局接收队列已满。"); return; }
                        incoming.Enqueue(Utf8.GetString(wireBuffer, offset, len));
                    }
                    offset += len;
                }
                if (offset > 0)
                {
                    received -= offset;
                    if (received > 0) Buffer.BlockCopy(wireBuffer, offset, wireBuffer, 0, received);
                }
            }
        }

        private void OpenDiscovery()
        {
            try
            {
                discovery = NewSocket(SocketType.Dgram);
                discovery.EnableBroadcast = true;
                discovery.Bind(new IPEndPoint(IPAddress.Any, IsHost ? DiscoveryPort : 0));
            }
            catch (SocketException) { Close(ref discovery); } // Direct address joining remains available.
        }

        private void RefreshInterfaces()
        {
            discoveryTargets.Clear();
            discoveryTargets.Add(new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            discoveryTargets.Add(new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
            LocalAddress = "127.0.0.1";
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var properties = nic.GetIPProperties();
                    foreach (var info in properties.UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(info.Address)) continue;
                        if (LocalAddress == "127.0.0.1") LocalAddress = info.Address.ToString();
                        if (info.IPv4Mask == null) continue;
                        var address = info.Address.GetAddressBytes();
                        var mask = info.IPv4Mask.GetAddressBytes();
                        for (int i = 0; i < address.Length; i++) address[i] |= (byte)~mask[i];
                        var ep = new IPEndPoint(new IPAddress(address), DiscoveryPort);
                        if (!discoveryTargets.Contains(ep)) discoveryTargets.Add(ep);
                    }
                    // A phone acting as a hotspot, or an emulator host, can be the LAN gateway.
                    foreach (var gateway in properties.GatewayAddresses)
                    {
                        var ip = gateway.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || ip.Equals(IPAddress.Any)) continue;
                        var target = new IPEndPoint(ip, DiscoveryPort);
                        if (!discoveryTargets.Contains(target)) discoveryTargets.Add(target);
                    }
                }
            }
            catch (NetworkInformationException) { }
            catch (NotSupportedException) { }
            catch (NotImplementedException) { }
            catch (SocketException) { }
#if UNITY_ANDROID && !UNITY_EDITOR
            AddAndroidGateway();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android keeps the default route in a per-network table which Mono may not enumerate.
        private void AddAndroidGateway()
        {
            try
            {
                using var player = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity");
                using var manager = activity.Call<UnityEngine.AndroidJavaObject>("getSystemService", "connectivity");
                using var network = manager.Call<UnityEngine.AndroidJavaObject>("getActiveNetwork");
                if (network == null) return;
                using var caps = manager.Call<UnityEngine.AndroidJavaObject>("getNetworkCapabilities", network);
                if (caps == null || !(caps.Call<bool>("hasTransport", 1) || caps.Call<bool>("hasTransport", 3))) return; // Wi-Fi or Ethernet.
                using var props = manager.Call<UnityEngine.AndroidJavaObject>("getLinkProperties", network);
                using var routes = props?.Call<UnityEngine.AndroidJavaObject>("getRoutes");
                if (routes == null) return;
                int count = routes.Call<int>("size");
                for (int i = 0; i < count; i++)
                {
                    using var route = routes.Call<UnityEngine.AndroidJavaObject>("get", i);
                    if (!route.Call<bool>("isDefaultRoute")) continue;
                    using var gate = route.Call<UnityEngine.AndroidJavaObject>("getGateway");
                    if (gate == null || !IPAddress.TryParse(gate.Call<string>("getHostAddress"), out var ip) ||
                        ip.AddressFamily != AddressFamily.InterNetwork || ip.Equals(IPAddress.Any)) continue;
                    var target = new IPEndPoint(ip, DiscoveryPort);
                    if (!discoveryTargets.Contains(target)) discoveryTargets.Add(target);
                }
            }
            catch (UnityEngine.AndroidJavaException) { } // Broadcast and direct joining still work.
        }
#endif

        private void PollDiscovery()
        {
            if (browsing && Now >= nextPing)
            {
                nextPing = Now + 1;
                foreach (var target in discoveryTargets)
                    try { discovery.SendTo(Find, target); } catch (SocketException) { }
                if (rooms.RemoveAll(room => Now - room.LastSeen > 4) > 0) RoomsRevision++;
            }
            try
            {
                for (int i = 0; i < 24 && discovery.Poll(0, SelectMode.SelectRead); i++)
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    int count = discovery.ReceiveFrom(discoveryBuffer, ref remote);
                    var endpoint = (IPEndPoint)remote;
                    string message = Encoding.ASCII.GetString(discoveryBuffer, 0, count);
                    if (IsHost && peer == null && message == Discovery + "|find")
                        discovery.SendTo(advertisement, endpoint);
                    else if (browsing) AcceptAdvertisement(message, endpoint.Address);
                }
            }
            catch (SocketException) { } // A blocked broadcast must not interrupt the TCP match.
        }

        private void AcceptAdvertisement(string message, IPAddress ip)
        {
            var parts = message.Split('|');
            if (parts.Length != 5 || parts[0] != Discovery || parts[1] != "room" ||
                !Guid.TryParseExact(parts[2], "N", out _) || !int.TryParse(parts[3], out int port) || !ValidPort(port)) return;
            string name;
            try { name = Utf8.GetString(Convert.FromBase64String(parts[4])); }
            catch (FormatException) { return; }
            catch (DecoderFallbackException) { return; }
            if (name.Length == 0 || name.Length > 48) return;
            var room = rooms.Find(item => item.Id == parts[2]);
            if (room == null)
            {
                if (rooms.Count >= 64) return;
                room = new LanDuelRoom { Id = parts[2], Address = ip.ToString(), Port = port, Name = name };
                rooms.Add(room);
                RoomsRevision++;
            }
            else if (room.Address == "127.0.0.1" && !IPAddress.IsLoopback(ip))
            { room.Address = ip.ToString(); RoomsRevision++; }
            room.LastSeen = Now;
        }

        private static void Close(ref Socket socket)
        {
            if (socket != null) { socket.Dispose(); socket = null; }
        }
        private void Fail(string message)
        {
            // Keep any decoded final messages available after a peer closes the stream.
            Close(ref peer); Close(ref listener); Close(ref discovery);
            Connected = false; connecting = false; Error = message;
            outgoing.Clear(); sentOffset = pendingBytes = 0;
        }
        private void Reset()
        {
            Fail("");
            incoming.Clear();
            IsHost = browsing = false;
            received = 0; nextPing = 0;
            if (rooms.Count > 0) { rooms.Clear(); RoomsRevision++; }
        }
        public void Dispose() => Reset();
    }

}
