using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Network;
using Blasphemous.Multiplayer.Common;
using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Host
{
    /// <summary>
    /// P2P adapter for the server: converts Steam P2P packets into <c>Server.Receive</c> frames
    /// through the same <c>IServerTransport</c> events used by TCP. This is the only class (with
    /// its peer <c>SteamP2PTransportImpl</c>) that touches <c>SteamNetworking</c> on the server
    /// side, and the only one that knows how to turn a Steam packet into a frame.
    ///
    /// The relay context is critical: a frame forwarded from peer A must reach B with A's id and
    /// sequence, not the adapter's. Otherwise B sees every player as "the host", channel 0 becomes
    /// a single global sequence, and packets from different senders discard each other as stale.
    /// <see cref="Relay"/> is <c>[ThreadStatic]</c> so the TCP thread never sees the main thread's
    /// context.
    ///
    /// Frames the server generates on its own (Intro, HostAssign, Connection, replay, re-encoded
    /// Position/Animation/Direction) have no relay context; their class is decided by
    /// <c>TransportClassTable.ForFrame</c>. The server itself is unchanged.
    /// </summary>
    internal static class P2PServerTransportImpl
    {
        /// <summary>
        /// 15s without any packet means the peer is gone (5 heartbeats at 3s each). Must match the
        /// peer side's <c>KEEPALIVE_TIMEOUT_SECONDS</c>: a shorter side closes the session first
        /// and the other sees the drop as "the host closed".
        /// </summary>
        private const float TIMEOUT_SECONDS = 15f;

        /// <summary>Max packets read per channel per frame.</summary>
        private const int MAX_PACKETS_PER_FRAME = 64;

        /// <summary>Max read buffer (a PNG skin fits; beyond this it is truncated).</summary>
        private const uint MAX_PACKET_BYTES = 256u * 1024u;

        /// <summary>Max outbound queue size (flushed every frame).</summary>
        private const int MAX_OUTBOUND = 1024;

        private const string MarkerStart = "[Net] p2p listening: ";
        private const string MarkerStop = "[Net] p2p: adapter stopped";
        private const string MarkerHello = "[Net] p2p peer connected: ";
        private const string MarkerGone = "[Net] p2p peer disconnected: ";
        private const string MarkerTimeout = "[Net] p2p timeout peer=";
        private const string MarkerHostSolo = "[Net] p2p host: no peer connected ";
        private const string MarkerSendFail = "[Net] p2p send failed: ";

        // === STATE ===
        private static readonly object _gate = new object();
        private static readonly Dictionary<ulong, PeerState> _peers = new Dictionary<ulong, PeerState>();
        private static readonly Queue<Outbound> _outbound = new Queue<Outbound>();
        private static readonly HashSet<ulong> _acceptLogged = new HashSet<ulong>();
        private static readonly HashSet<ulong> _foreignLogged = new HashSet<ulong>();

        /// <summary>Peers for which the "first packet" line has already been written.</summary>
        private static readonly HashSet<ulong> _firstPacketLogged = new HashSet<ulong>();

        /// <summary>How many RX trace lines have already been written per channel.</summary>
        private static readonly int[] _rxLogged = new int[3];

        /// <summary>
        /// RX trace lines per channel: TRACE, not counter. Answers "do packets arrive, and on
        /// which channel?". Traffic counting is done by the probe every 3s.
        /// </summary>
        private const int RX_LOG_FIRST = 3;

        /// <summary>When the adapter was armed: used by the "first packet after Xs" line.</summary>
        private static float _startedAt;

        private static bool _running;
        private static bool _registered;
        private static bool _registerFailed;
        private static uint _ownSeq;
        private static ulong _myId;
        private static byte[] _rxBuffer = new byte[1024];
        private static int _outboundDropped;
        private static int _outboundFailLogged;

        private static Action<PeerId> _onConnected;
        private static Action<PeerId> _onDisconnected;
        private static Action<PeerId, byte[]> _onFrame;

        // Relay context per thread: [ThreadStatic] so the TCP thread never sees it.
        [ThreadStatic] private static Relay _relay;

        /// <summary>Sink of the facade: events are delivered on the Tick thread (main thread).</summary>
        internal static void SetSink(Action<PeerId> onConnected, Action<PeerId> onDisconnected,
                                     Action<PeerId, byte[]> onFrame)
        {
            _onConnected = onConnected;
            _onDisconnected = onDisconnected;
            _onFrame = onFrame;
        }

        /// <summary>"Is it listening now?" (for the log and the report).</summary>
        internal static bool Listening
        {
            get { return _running && _registered; }
        }

        /// <summary>Composite line for <c>[Host] ascolto su=...</c>.</summary>
        internal static string ListeningAddresses()
        {
            int peers;
            lock (_gate)
                peers = _peers.Count;

            return "p2p (" + peers + " peer)";
        }

        /// <summary>
        /// <c>Start(port)</c>: the port is ignored, P2P has no listener. What is done here is
        /// registering the callback that accepts sessions; without it every peer packet is
        /// silently discarded. Returns true even without Steam: an unavailable addition must not
        /// prevent TCP from listening (the composite stops everything if a child returns false).
        /// </summary>
        internal static bool Start(int port)
        {
            try
            {
                if (!SteamAPI.IsSteamRunning() || !SteamUser.BLoggedOn())
                {
                    ModLog.Warn(MarkerStart + "Steam is not running or no account: adapter stays " +
                                "inert (TCP still listening)");
                    return true;
                }

                if (!EnsureRegistered())
                {
                    ModLog.Warn(MarkerStart + "P2PSessionRequest_t callback not registered: no P2P peer " +
                                "will reach the server (TCP still does)");
                    return true;
                }

                lock (_gate)
                {
                    _peers.Clear();
                    _outbound.Clear();
                    _ownSeq = 0u;
                    _outboundDropped = 0;
                    _outboundFailLogged = 0;
                }

                _acceptLogged.Clear();
                _foreignLogged.Clear();
                _firstPacketLogged.Clear();
                _relay = null;

                for (int i = 0; i < 3; i++)
                    _rxLogged[i] = 0;

                _startedAt = Time.realtimeSinceStartup;

                // The SteamID is read here (main thread) and cached: Send can arrive from the TCP
                // thread, and a native Steam call from there is a failure that hides until it does
                // not.
                _myId = SteamUser.GetSteamID().m_SteamID;

                _running = true;

                ModLog.Info(MarkerStart + "porta=" + port + " (ignored: P2P has no listener) " +
                            "io=" + _myId + " canali=0,1,2");

                // Marker 0 — host armed. First line to look for when a peer reports "I sent hello
                // and nothing happened": if it is absent, the host is deaf and packets fall into
                // the void.
                ulong lobbyId = 0UL;
                try
                {
                    lobbyId = SteamLobbyImpl.CurrentLobby.m_SteamID;
                }
                catch (Exception)
                {
                    // An unreadable lobby counts as "none": the line below says so.
                }

                ModLog.Info(SteamP2PTransport.MarkerAdapterActive + lobbyId + ") - ready to accept " +
                            "sessions from lobby members (io=" + _myId + ")");
                return true;
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerStart + "error: " + e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        /// <summary>Forgets peers and stops delivering events. Idempotent, never throws.</summary>
        internal static void Stop()
        {
            List<ulong> peers;
            lock (_gate)
            {
                peers = new List<ulong>(_peers.Keys);
                _peers.Clear();
                _outbound.Clear();
                _running = false;
            }

            // No CloseP2PSessionWithUser here. Stop is called during process shutdown
            // (Core.OnDestroy -> ... -> HostSession.Stop -> Server.Stop -> ...); at that point
            // Steam is already tearing down, and Close there crashes natively in steam_api64.
            // The process closes the sessions when it dies, which is the limit already accepted.
            // Game-path Close calls (Drop, LinkLost, HandshakeExpired) are unchanged.
            if (peers.Count > 0 || _registered)
                ModLog.Info(MarkerStop + " peer=" + peers.Count +
                            " - no P2P Close from here (" +
                            (P2PShutdownGate.Active
                                ? "process shutdown: Steam closes the sessions"
                                : "session left to the process") + ")");
        }

        /// <summary>
        /// Forwards a frame to a peer. Called by the server, possibly from the TCP thread: the
        /// packet is built and queued, the native Steam call happens in <see cref="Tick"/> on the
        /// main thread. Returns false if the peer is no longer announced (the facade translates
        /// that into an exception, matching the TCP contract).
        /// </summary>
        internal static bool Send(ulong steamId, byte[] framed)
        {
            if (steamId == 0UL)
                return false;

            Relay relay = _relay;
            TransportClass cls;
            int channel;
            ulong origin;
            uint seq;

            lock (_gate)
            {
                if (!_running)
                    return false;

                PeerState peer;
                if (!_peers.TryGetValue(steamId, out peer) || !peer.Announced)
                    return false;

                if (relay != null && relay.Active)
                {
                    // A forwarded frame keeps its origin (see class comment): id is the real sender,
                    // sequence is its stream advanced by one per forwarded frame so the receiver
                    // does not discard it as stale.
                    cls = relay.Class;
                    origin = relay.Origin;

                    if (cls == TransportClass.Unreliable)
                    {
                        PeerState from = Origin(origin);
                        from.RelaySeq++;
                        seq = from.RelaySeq;
                    }
                    else
                    {
                        seq = 0u;
                    }
                }
                else
                {
                    // A frame nobody sent as a relay: class comes from the table.
                    cls = TransportClassTable.ForFrame(framed);
                    origin = _myId;
                    seq = cls == TransportClass.Unreliable ? NextOwnSeq() : 0u;
                }

                byte type = framed.Length > 2 ? framed[2] : (byte)0;
                channel = P2PWire.ChannelFor(cls, type);

                byte[] packet = cls == TransportClass.Unreliable
                    ? P2PWire.BuildData(framed, cls, origin, seq)
                    : P2PWire.BuildData(framed, cls, origin, 0u);

                if (_outbound.Count >= MAX_OUTBOUND)
                {
                    _outbound.Dequeue();
                    _outboundDropped++;

                    if (_outboundDropped <= 3)
                        ModLog.Warn(MarkerSendFail + "queue full (" + MAX_OUTBOUND + " packets): the oldest " +
                                    "was dropped");
                }

                _outbound.Enqueue(new Outbound(steamId, packet, channel, cls == TransportClass.Reliable));
                return true;
            }
        }

        /// <summary>
        /// Per-frame cycle: ① drain the three channels (deliver to server), ② flush what the server
        /// wrote, ③ time out peers. Order matters: receive first (a packet that arrived this frame
        /// must feed the server before replies leave), then send, then check who stopped talking.
        /// </summary>
        internal static void Tick()
        {
            if (!_running)
                return;

            try
            {
                DrainChannel(P2PWire.ChannelUnreliable);
                DrainChannel(P2PWire.ChannelReliable);
                DrainChannel(P2PWire.ChannelHeavy);

                Flush();

                CheckTimeouts();
            }
            catch (Exception e)
            {
                ModLog.Error("[Net] p2p error in Tick: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // === RECEIVE ===

        private static void DrainChannel(int channel)
        {
            int guard = MAX_PACKETS_PER_FRAME;

            while (guard-- > 0)
            {
                uint available;
                if (!SteamNetworking.IsP2PPacketAvailable(out available, channel))
                    break;

                uint capacity = available;
                if (capacity == 0u)
                    capacity = 1u;
                if (capacity > MAX_PACKET_BYTES)
                    capacity = MAX_PACKET_BYTES;

                if ((uint)_rxBuffer.Length < capacity)
                    _rxBuffer = new byte[capacity];

                uint read;
                CSteamID remote;
                if (!SteamNetworking.ReadP2PPacket(_rxBuffer, capacity, out read, out remote, channel))
                    break;

                if (read == 0u || remote.m_SteamID == 0UL)
                    continue;

                Handle(channel, remote, _rxBuffer, (int)read);
            }
        }

        /// <summary>
        /// One packet: HELLO = announce, DATA = inject, BYE = goodbye. A foreign packet (the
        /// <c>SteamP2PProbe</c> byte on channel 0) is counted and dropped.
        /// </summary>
        private static void Handle(int channel, CSteamID remote, byte[] packet, int length)
        {
            byte kind;
            int ignoredOffset;
            uint ignoredSeq;
            ulong ignoredSender;

            // RX trace, twin of the peer-side one: if the tunnel is healthy but the game receives
            // nothing, this line says on which channel and from whom. First RX_LOG_FIRST per
            // channel: per frame would be a river.
            if (_rxLogged[channel] < RX_LOG_FIRST)
            {
                _rxLogged[channel]++;
                ModLog.Info("[Net] p2p: rx canale=" + channel + " da=" + remote.m_SteamID +
                            " byte=" + length);
            }

            if (!P2PWire.TryParse(packet, length, out kind, out ignoredOffset, out ignoredSender, out ignoredSeq))
            {
                ulong id = remote.m_SteamID;
                bool first;
                lock (_gate)
                    first = _foreignLogged.Add(id);

                if (first)
                    ModLog.Warn("[Net] p2p foreign packet from " + id + " canale=" + channel +
                                " (" + length + " byte): dropped (P2P probe enabled?)");
                return;
            }

            ulong steamId = remote.m_SteamID;

            // Marker ④ — the first valid packet from this peer: the fact that the tunnel carries
            // bytes (not just that Accept went through). From here the peer is reachable.
            bool firstPacket;
            lock (_gate)
                firstPacket = _firstPacketLogged.Add(steamId);

            if (firstPacket)
            {
                float waited = Time.realtimeSinceStartup - _startedAt;
                ModLog.Info(SteamP2PTransport.MarkerFirstPacket + channel + ") after " +
                            waited.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                            "s - da=" + steamId + " (HOST/adapter side)");
            }

            // Anyone sending a valid packet is alive: timeout is measured on this.
            Touch(steamId);

            switch (kind)
            {
                case P2PWire.KindHello:
                    Announce(steamId);
                    return;

                case P2PWire.KindKeepAlive:
                    return;

                case P2PWire.KindBye:
                    Drop(steamId, "sent BYE");
                    return;

                case P2PWire.KindData:
                {
                    ulong origin;
                    uint seq;
                    int payloadOffset;

                    if (!P2PWire.TryParseData(packet, length, channel, out origin, out seq, out payloadOffset))
                        return;

                    if (!IsAnnounced(steamId))
                    {
                        // An unannounced peer is unrouteable and without hello it is not even known
                        // to be talking to us. Said once per peer.
                        bool first;
                        lock (_gate)
                            first = _acceptLogged.Add(steamId);

                        if (first)
                            ModLog.Warn("[Net] p2p frame from " + steamId + " BEFORE the hello: dropped " +
                                        "(the peer must send hello first)");
                        return;
                    }

                    int count = length - payloadOffset;
                    if (count <= 0)
                        return;

                    byte[] framed = new byte[count];
                    Buffer.BlockCopy(packet, payloadOffset, framed, 0, count);

                    // Relay context: from here to the end of the call, every Send from the server is
                    // a forward of THIS packet.
                    Relay previous = _relay;
                    _relay = new Relay
                    {
                        Active = true,
                        Class = channel == P2PWire.ChannelUnreliable ? TransportClass.Unreliable : TransportClass.Reliable,
                        Origin = channel == P2PWire.ChannelUnreliable ? origin : steamId,
                    };

                    try
                    {
                        Action<PeerId, byte[]> handler = _onFrame;
                        if (handler != null)
                            handler(PeerId.FromSteam(steamId), framed);
                    }
                    finally
                    {
                        _relay = previous;
                    }

                    return;
                }
            }
        }

        /// <summary>Announces a peer and (once) tells the server about it.</summary>
        private static void Announce(ulong steamId)
        {
            PeerState state;
            bool isNew;

            lock (_gate)
            {
                if (!_peers.TryGetValue(steamId, out state))
                {
                    state = new PeerState();
                    _peers[steamId] = state;
                }

                isNew = !state.Announced;
                state.Announced = true;
                state.LastRx = Time.realtimeSinceStartup;
            }

            if (isNew)
            {
                ModLog.Info(MarkerHello + steamId + " (hello received)");
                Action<PeerId> handler = _onConnected;
                if (handler != null)
                    handler(PeerId.FromSteam(steamId));
            }
        }

        /// <summary>
        /// Removes a peer BEFORE announcing it to the server: so a server write towards a peer who
        /// just left fails the way <c>SimpleTcpServer.Send</c> would fail.
        /// </summary>
        private static void Drop(ulong steamId, string reason)
        {
            bool existed;
            bool wasPeer;    // an ANNOUNCED peer: a real session, not a leftover
            bool aloneNow;   // after this drop no announced peer remains

            lock (_gate)
            {
                PeerState state;
                wasPeer = _peers.TryGetValue(steamId, out state) && state.Announced;

                existed = _peers.Remove(steamId);

                // The count is done UNDER the gate: between this drop and the next frame another
                // peer could disappear, and an "I am alone" read outside would be stale.
                aloneNow = existed;
                if (aloneNow)
                {
                    foreach (KeyValuePair<ulong, PeerState> pair in _peers)
                    {
                        if (pair.Value.Announced)
                        {
                            aloneNow = false;
                            break;
                        }
                    }
                }
            }

            if (!existed)
                return;

            ModLog.Info(MarkerGone + steamId + " (" + reason + ")");

            Action<PeerId> handler = _onDisconnected;
            if (handler != null)
                handler(PeerId.FromSteam(steamId));

            try
            {
                SteamNetworking.CloseP2PSessionWithUser(new CSteamID(steamId));
            }
            catch (Exception)
            {
                // A session that will not close must not prevent forgetting the peer.
            }

            // Link is dead, and this concerns the PEER: after removing it and telling the server,
            // and ONLY if no other announced peer remains, the host line is written (see
            // ResetHostPlayerState). The host's own connection is NOT touched: Drop only clears
            // the peer's state.
            if (wasPeer && aloneNow)
                ResetHostPlayerState(reason);
        }

        /// <summary>
        /// A peer drop is NOT about the host's own connection.
        ///
        /// The host's NetworkManager is a TCP client on 127.0.0.1:porta to its OWN server - not a
        /// link to the peer. Calling Disconnect here would close the loopback, the host would no
        /// longer be connected to its own server, and it could not rejoin (the still-listening
        /// server would answer "port occupied"). Drop already cleaned the peer's state; the host
        /// connection stays. The line below only measures that with <c>connesso=</c>.
        /// </summary>
        private static void ResetHostPlayerState(string reason)
        {
            try
            {
                if (Main.Multiplayer == null || Main.Multiplayer.NetworkManager == null)
                    return;

                bool connected = Main.Multiplayer.NetworkManager.IsConnected;

                ModLog.Warn(MarkerHostSolo + "(" + reason + "): the drop is the peer's, the host stays " +
                            "connected to its own server (no Disconnect on loopback) " +
                            "connesso=" + (connected ? "true" : "false") +
                            " - the server stays listening and the adapter armed");
            }
            catch (Exception e)
            {
                ModLog.Error("[Net] p2p host: error in the player-state line: " +
                             e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Touch(ulong steamId)
        {
            lock (_gate)
            {
                PeerState state;
                if (_peers.TryGetValue(steamId, out state))
                    state.LastRx = Time.realtimeSinceStartup;
            }
        }

        private static bool IsAnnounced(ulong steamId)
        {
            lock (_gate)
            {
                PeerState state;
                return _peers.TryGetValue(steamId, out state) && state.Announced;
            }
        }

        private static void CheckTimeouts()
        {
            if (_peers.Count == 0)
                return;

            List<ulong> gone = null;
            float now = Time.realtimeSinceStartup;

            lock (_gate)
            {
                foreach (KeyValuePair<ulong, PeerState> pair in _peers)
                {
                    if (now - pair.Value.LastRx < TIMEOUT_SECONDS)
                        continue;

                    if (gone == null)
                        gone = new List<ulong>();

                    gone.Add(pair.Key);
                }
            }

            if (gone == null)
                return;

            for (int i = 0; i < gone.Count; i++)
            {
                ModLog.Error(MarkerTimeout + gone[i] + " no packets for " + (int)TIMEOUT_SECONDS +
                             " s (P2P session lost)");
                Drop(gone[i], "timeout");
            }
        }

        // === SEND ===

        /// <summary>
        /// Drains the queue. This is the only place this adapter calls Steam: it runs on the main
        /// thread (from Tick), so a Send that arrived from the TCP thread is never a native call
        /// from the wrong thread.
        /// </summary>
        private static void Flush()
        {
            int guard = MAX_OUTBOUND;
            Outbound item = null;

            while (guard-- > 0)
            {
                lock (_gate)
                {
                    if (_outbound.Count == 0)
                        break;

                    item = _outbound.Dequeue();
                }

                if (item == null)
                    continue;

                EP2PSend mode = item.Reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;

                bool sent;
                try
                {
                    sent = SteamNetworking.SendP2PPacket(new CSteamID(item.SteamId), item.Packet,
                                                        (uint)item.Packet.Length, mode, item.Channel);
                }
                catch (Exception e)
                {
                    ModLog.Error(MarkerSendFail + "exception: " + e.GetType().Name + ": " + e.Message);
                    continue;
                }

                if (!sent)
                {
                    _outboundFailLogged++;
                    if (_outboundFailLogged <= 3)
                        ModLog.Warn(MarkerSendFail + "peer=" + item.SteamId + " canale=" + item.Channel +
                                    ": SendP2PPacket returned false (send buffer full), dropped");
                }
            }
        }

        // === SESSION-REQUEST CALLBACK ===

        /// <summary>
        /// <c>P2PSessionRequest_t</c> -> <c>AcceptP2PSessionWithUser</c>, ONLY for lobby members.
        /// Without this callback, every peer packet is silently discarded and the symptom is
        /// "Client connected" that never arrives, with no line to explain it.
        /// </summary>
        private static void OnSessionRequest(P2PSessionRequest_t request)
        {
            try
            {
                CSteamID remote = request.m_steamIDRemote;
                ulong id = remote.m_SteamID;

                if (id == 0UL)
                    return;

                // Marker ② — the request, before any gate: it is the fact that the tunnel was
                // requested from us. And if the adapter is not armed, say it: "request arrived
                // but nobody listening" is our fault, not the network's.
                ModLog.Info(SteamP2PTransport.MarkerSessionRequest + id + " (HOST/adapter side)");

                if (!_running)
                {
                    ModLog.Warn(SteamP2PTransport.MarkerNotArmed + id + " but adapter NOT armed " +
                                "(no active host session): session accepted anyway, but " +
                                "no frame will be delivered to the server");
                }

                if (!SteamLobbyImpl.IsLobbyMember(remote))
                {
                    ModLog.Warn(SteamP2PTransport.MarkerAcceptFail + id + " (not a lobby member)");
                    return;
                }

                bool accepted = SteamNetworking.AcceptP2PSessionWithUser(remote);

                bool first;
                lock (_gate)
                    first = _acceptLogged.Add(id);

                if (!first)
                    return;

                // Marker ③ — the outcome, with the true/false of AcceptP2PSessionWithUser:
                // true = session exists; false = Steam refused locally before the network
                // (usually: already present, or the id is no longer valid).
                if (accepted)
                    ModLog.Info(SteamP2PTransport.MarkerAcceptOk + id + " ACCEPTED (lobby member)");
                else
                    ModLog.Warn(SteamP2PTransport.MarkerAcceptFail + id + " (Steam returned false: " +
                                "session already present or id no longer valid)");
            }
            catch (Exception e)
            {
                ModLog.Error("[Net] p2p error in P2PSessionRequest_t: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// Black box on the host side: <c>P2PSessionConnectFail_t</c>. Arrives 10-15s late and is
        /// NOT symmetric (only the side with queued packets sees it), so having it on both sides is
        /// the only way to know which machine stopped sending. Log only: the handshake and
        /// heartbeat timeouts make the actual decision.
        /// </summary>
        private static void OnSessionConnectFail(P2PSessionConnectFail_t failure)
        {
            try
            {
                ulong id = failure.m_steamIDRemote.m_SteamID;
                int error = failure.m_eP2PSessionError;

                ModLog.Error(SteamP2PTransport.MarkerSessionFail + id + " err=" + error + " " +
                             ErrorName(error) + " (HOST/adapter side: tunnel not open)");
            }
            catch (Exception e)
            {
                ModLog.Error("[Net] p2p error in P2PSessionConnectFail_t: " + e.GetType().Name + ": " +
                             e.Message);
            }
        }

        /// <summary>Steam error name: a bare number is not readable.</summary>
        private static string ErrorName(int error)
        {
            switch (error)
            {
                case 0: return "NONE";
                case 1: return "NOT_RUNNING_SAME_APP";
                case 2: return "NO_RIGHTS_TO_APP";
                case 3: return "DESTINATION_NOT_LOGGED_IN";
                case 4: return "TIMEOUT";
                default: return "UNKNOWN";
            }
        }

        private static bool EnsureRegistered()
        {
            if (_registered)
                return true;

            if (_registerFailed)
                return false;

            try
            {
                _cbSessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
                _registered = true;

                // Black box: registered at the same point, and its failure does not prevent the
                // transport from working (one log line is missing, not a session).
                try
                {
                    _cbSessionFail = Callback<P2PSessionConnectFail_t>.Create(OnSessionConnectFail);
                }
                catch (Exception e)
                {
                    ModLog.Warn("[Net] p2p Callback<P2PSessionConnectFail_t>.Create: " + e.GetType().Name +
                                ": " + e.Message + " (only the error line is missing)");
                }

                return true;
            }
            catch (Exception e)
            {
                _registerFailed = true;
                ModLog.Error("[Net] p2p Callback<P2PSessionRequest_t>.Create: " + e.GetType().Name + ": " +
                             e.Message);
                return false;
            }
        }

        /// <summary>
        /// FIELD, not local: <c>Callback&lt;T&gt;.Create</c> keeps a <c>GCHandle</c> on the delegate,
        /// and a local would disappear after return (callbacks would silently stop).
        /// </summary>
        private static Callback<P2PSessionRequest_t> _cbSessionRequest;

        /// <summary>Session black box, host side: same reason (field, not local).</summary>
        private static Callback<P2PSessionConnectFail_t> _cbSessionFail;

        /// <summary>
        /// Next sequence number for frames the server generates (its own stream): a single counter
        /// under <c>_gate</c>, so two frames generated from different threads cannot share a
        /// sequence. The receiver drops the OLDER sequence, not the equal one: monotonicity here is
        /// a guarantee, not a hope.
        /// </summary>
        private static uint NextOwnSeq()
        {
            _ownSeq++;
            return _ownSeq;
        }

        /// <summary>Peer state for the relay: its outgoing sequence and last heartbeat.</summary>
        private static PeerState Origin(ulong steamId)
        {
            PeerState state;
            if (!_peers.TryGetValue(steamId, out state))
            {
                state = new PeerState();
                _peers[steamId] = state;
            }

            return state;
        }

        private sealed class PeerState
        {
            internal bool Announced;
            internal float LastRx;

            /// <summary>Last sequence used to forward this peer's frames (channel 0).</summary>
            internal uint RelaySeq;
        }

        private sealed class Relay
        {
            internal bool Active;
            internal TransportClass Class;
            internal ulong Origin;
        }

        private sealed class Outbound
        {
            internal readonly ulong SteamId;
            internal readonly byte[] Packet;
            internal readonly int Channel;
            internal readonly bool Reliable;

            internal Outbound(ulong steamId, byte[] packet, int channel, bool reliable)
            {
                SteamId = steamId;
                Packet = packet;
                Channel = channel;
                Reliable = reliable;
            }
        }
    }
}