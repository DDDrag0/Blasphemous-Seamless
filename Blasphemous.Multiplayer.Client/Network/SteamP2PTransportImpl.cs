using Blasphemous.ModdingAPI;
using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// Peer-side P2P transport (Steam). Wraps <c>SteamNetworking</c> in a class that names
    /// Steamworks types directly; the reflection-gated <c>SteamP2PTransport</c> facade is the
    /// only caller, keeping the TCP path usable on non-Steam builds where those types may be
    /// missing (TypeLoadException would surface at the call site).
    ///
    /// Opens into the current lobby, sends a reliable HELLO to the host declared in
    /// <c>hostSteamId</c>, then routes game frames on three channels: 0 unreliable with per-
    /// sender sequence, 1 reliable, 2 reliable-isolated for skins. Heartbeat keepalive; handshake
    /// timeout (25s) is distinct from keepalive timeout (15s) - one timeout for two very different
    /// phases was the source of false disconnects.
    /// </summary>
    internal static class SteamP2PTransportImpl
    {
        /// <summary>
        /// Handshake timeout: 25s from Begin until the first valid packet is received. Covers
        /// typical 3-7s P2P plus 10-15s relay case with margin; a single keepalive-sized window
        /// here aborted while Steam was still opening the tunnel.
        /// </summary>
        private const float HANDSHAKE_TIMEOUT_SECONDS = 25f;

        /// <summary>
        /// Keepalive timeout: 15s of silence on an OPEN link. 5s against a 3s heartbeat meant one
        /// lost beat (3+3 > 5) declared a live link dead; 15s tolerates four consecutive losses,
        /// matching the 4-5s game hitches that were killing sessions.
        /// </summary>
        private const float KEEPALIVE_TIMEOUT_SECONDS = 15f;

        /// <summary>Heartbeat interval.</summary>
        private const float KEEPALIVE_SECONDS = 3f;

        /// <summary>Max packets read per channel per frame; bounds a possible backlog to one frame.</summary>
        private const int MAX_PACKETS_PER_FRAME = 64;

        /// <summary>Max read buffer size (a skin PNG fits; beyond this the packet is truncated).</summary>
        private const uint MAX_PACKET_BYTES = 256u * 1024u;

        /// <summary>Max frames queued while waiting for the hello; oldest dropped past this.</summary>
        private const int MAX_OUTBOX = 512;

        /// <summary>
        /// Grace for lobby data replication after entry: the hostSteamId may arrive in a
        /// LobbyDataUpdate_t a beat after entry. Past this, a missing value is a fact, not a delay.
        /// </summary>
        private const float HOST_DATA_GRACE_SECONDS = 3f;

        private const string MarkerOpen = "[Net] p2p: ";
        private const string MarkerTimeout = "[Net] p2p timeout: ";
        private const string MarkerLinkLost = "[Net] p2p: link perso";

        /// <summary>Session no longer available: a fact, not a technical failure.</summary>
        private const string MarkerUnavailable = "[Net] p2p: the session is no longer available";

        // === State ===

        /// <summary>
        /// Explicit phase: Connecting → Connected → Disconnected. One enum rather than two booleans
        /// because the previous single 5s timeout was valid in two very different phases; with a
        /// phase, "which timeout applies now?" has a single answer and the switch is the first
        /// valid packet.
        /// </summary>
        private enum Phase
        {
            /// <summary>No attempt in progress (or failed): Send must not go out.</summary>
            Disconnected,

            /// <summary>Hello sent (or pending), no packet yet: 25s handshake timeout applies.</summary>
            Connecting,

            /// <summary>At least one valid packet received: 15s keepalive timeout applies.</summary>
            Connected,
        }

        private static Phase _phase = Phase.Disconnected;
        private static bool _opened;
        private static bool _helloed;
        private static bool _linkLost;

        /// <summary>
        /// True after a link was declared lost and later revived. Used only to decide who emits the
        /// failure notification: after a revive the normal path stays silent (see
        /// <c>NotifyIfOutOfSession</c>), so the transport must speak. Reset by End and Begin.
        /// </summary>
        private static bool _revived;

        private static CSteamID _lobby = CSteamID.Nil;
        private static CSteamID _host = CSteamID.Nil;
        private static ulong _myId;
        private static uint _txSeq;
        private static float _lastRx;
        private static float _nextKeepAlive;
        private static bool _registered;
        private static bool _registerFailed;
        private static byte[] _rxBuffer = new byte[1024];
        private static float _openedAt;

        private static readonly Dictionary<ulong, uint> _lastSeqByOrigin = new Dictionary<ulong, uint>();
        private static readonly List<Pending> _outbox = new List<Pending>();
        private static readonly int[] _foreign = new int[3];
        private static readonly int[] _foreignLogged = new int[3];
        private static readonly HashSet<ulong> _peerLogged = new HashSet<ulong>();

        /// <summary>Peers for which the "return session accepted" line has already been written.</summary>
        private static readonly HashSet<ulong> _returnLogged = new HashSet<ulong>();

        /// <summary>RX trace lines already written per channel.</summary>
        private static readonly int[] _rxLogged = new int[3];

        /// <summary>RX trace cap per channel; three lines are enough to know which channel arrives.</summary>
        private const int RX_LOG_FIRST = 3;
        private static int _droppedStale;
        private static int _droppedStaleLogged;
        private static int _sendFailLogged;
        private static int _outboxDropped;
        private static bool _timeoutLogged;

        private static Action<byte[]> _onData;
        private static Action _onConnected;
        private static Action _onDisconnected;

        /// <summary>The wire is open: hello sent and link not expired.</summary>
        internal static bool Open
        {
            get { return _opened && _helloed && !_linkLost; }
        }

        /// <summary>Sink of the facade; events are delivered on the Tick thread (main).</summary>
        internal static void SetSink(Action<byte[]> onData, Action onConnected, Action onDisconnected)
        {
            _onData = onData;
            _onConnected = onConnected;
            _onDisconnected = onDisconnected;
        }

        /// <summary>
        /// Opens the wire. The only requirement is being in a Steam lobby (the invite lobby);
        /// the host declared there is the target. No TCP check - P2P must work where TCP does not.
        /// </summary>
        internal static bool Begin(string host, int port)
        {
            try
            {
                End();

                if (!SteamAPI.IsSteamRunning() || !SteamUser.BLoggedOn())
                {
                    ModLog.Warn(MarkerOpen + "Steam is not running or no account is logged in: no P2P");
                    return false;
                }

                CSteamID lobby = SteamLobbyImpl.CurrentLobby;
                if (lobby.m_SteamID == 0UL)
                {
                    ModLog.Warn(MarkerOpen + "no Steam lobby. P2P has no host to reach: " +
                                "accetta un invito (o apri tu una lobby) e riprova");
                    return false;
                }

                if (!EnsureRegistered())
                {
                    ModLog.Warn(MarkerOpen + "P2PSessionRequest_t callback not registered: no P2P");
                    return false;
                }

                _lobby = lobby;
                _host = CSteamID.Nil;
                _myId = SteamUser.GetSteamID().m_SteamID;
                _opened = true;
                _helloed = false;
                _linkLost = false;

                // Enter negotiation: until the first valid packet, HANDSHAKE_TIMEOUT_SECONDS applies.
                _phase = Phase.Connecting;

                _txSeq = 0u;
                _lastRx = Time.realtimeSinceStartup;
                _openedAt = _lastRx;
                _nextKeepAlive = _lastRx;
                _timeoutLogged = false;
                _sendFailLogged = 0;
                _outboxDropped = 0;
                _droppedStale = 0;
                _droppedStaleLogged = 0;
                _outbox.Clear();
                _lastSeqByOrigin.Clear();
                _peerLogged.Clear();
                _returnLogged.Clear();
                for (int i = 0; i < 3; i++)
                {
                    _foreign[i] = 0;
                    _foreignLogged[i] = 0;
                    _rxLogged[i] = 0;
                }

                // Two different values worth printing: hostSteamId is the DATA the host wrote (and
                // the one used); GetLobbyOwner is Steam's role (which transfers on host leave).
                ModLog.Info(MarkerOpen + "in attesa: lobby=" + lobby.m_SteamID + " io=" + _myId +
                            " hostDichiarato=" + DeclaredHost() +
                            " proprietario=" + SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID +
                            " (host nel campo Server IP='" + SteamP2PTransport.RequestToken + "')");
                return true;
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + " error opening: " + e.GetType().Name + ": " + e.Message);
                _opened = false;
                return false;
            }
        }

        /// <summary>
        /// Closes the wire: BYE (best effort), then CloseP2PSessionWithUser - unless the shutdown
        /// gate is raised, in which case the process closes the session. The BYE is kept because
        /// it is a Send, not a native Close: the peer learns immediately instead of waiting for
        /// the 15s timeout.
        /// </summary>
        internal static void End()
        {
            try
            {
                if (_helloed && !_linkLost && _host.m_SteamID != 0UL)
                    SendControl(P2PWire.KindBye);
            }
            catch (Exception)
            {
                // A lost goodbye must not prevent closing.
            }

            // Native Close skipped during shutdown: Steam tears sessions down on process exit
            // (see P2PShutdownGate).
            if (!P2PShutdownGate.Active)
            {
                try
                {
                    if (_host.m_SteamID != 0UL)
                        SteamNetworking.CloseP2PSessionWithUser(_host);
                }
                catch (Exception)
                {
                    // Idem.
                }
            }

            _opened = false;
            _helloed = false;
            _linkLost = false;
            _revived = false;
            _phase = Phase.Disconnected;
            _host = CSteamID.Nil;
            _lobby = CSteamID.Nil;
            _outbox.Clear();
            _lastSeqByOrigin.Clear();
        }

        /// <summary>
        /// Per-frame: open (hello), drain the three channels, check the timeout of the current
        /// phase, send keepalive, flush the queue. Read happens first and from Begin - not only
        /// once "connected" - or the packet buffer saturates and the switch to Connected never
        /// fires. Phase checks run before every return so a timeout is never skipped.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                // Only _opened gates entry; _linkLost must NOT, or after a drop no timeout would
                // ever fire again (see CheckPhase for the fix).
                if (!_opened)
                    return;

                // Read from Begin, in every frame, regardless of phase. Reading only once connected
                // would saturate the buffer and deadlock the switch to Connected. Reading even when
                // the link is lost drains residuals so the next session is not blocked.
                Drain();

                if (!_opened)
                    return;

                float now = Time.realtimeSinceStartup;

                // Phase checks come BEFORE every other return - this placement is the fix: a
                // timeout that is never checked cannot notify, and a silent failure is not
                // distinguishable from "nothing happened".
                if (CheckPhase(now))
                    return;

                if (_linkLost)
                    return;

                if (!_helloed && !TrySendHello())
                    return;

                if (!_opened || _linkLost)
                    return;

                // Heartbeat runs in both phases: it keeps the session alive during the handshake
                // and triggers a response from the host.
                if (now >= _nextKeepAlive)
                {
                    _nextKeepAlive = now + KEEPALIVE_SECONDS;
                    SendControl(P2PWire.KindKeepAlive);
                }

                FlushOutbox();
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + " error in Tick: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// Phase sentinel: "has the current phase exceeded its deadline?". Two phases, two
        /// thresholds. Disconnected is not watched - the fact has already been reported.
        /// </summary>
        private static bool CheckPhase(float now)
        {
            if (_phase == Phase.Connecting)
            {
                float waited = now - _openedAt;

                if (waited < HANDSHAKE_TIMEOUT_SECONDS)
                    return false;

                HandshakeExpired(waited);
                return true;
            }

            if (_phase == Phase.Connected)
            {
                float silent = now - _lastRx;

                if (silent < KEEPALIVE_TIMEOUT_SECONDS)
                    return false;

                LinkLost("no packet for " + (int)silent + " s (threshold " +
                         (int)KEEPALIVE_TIMEOUT_SECONDS + " s)");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handshake expired: 25s and no packet ever arrived. Different from a lost link - this is
        /// "never opened", reported with its own marker. Closes the session directly (End() would
        /// clear _linkLost and break the IOException contract on Send).
        /// </summary>
        private static void HandshakeExpired(float waited)
        {
            _phase = Phase.Disconnected;
            _linkLost = true;

            if (_host.m_SteamID != 0UL) { try { SteamNetworking.CloseP2PSessionWithUser(_host); } catch { } }

            if (!_timeoutLogged)
            {
                _timeoutLogged = true;

                ModLog.Error(SteamP2PTransport.MarkerHandshakeTimeout +
                             (int)HANDSHAKE_TIMEOUT_SECONDS + " s): no packet received from host=" +
                             _host.m_SteamID + " (attesa " + (int)waited + " s, hello=" +
                             (_helloed ? "sent" : "NEVER SENT") + "): the P2P handshake did not " +
                             "open (slow relay, firewall, or an unresponsive host)");
            }

            // Notify only when nobody else will (revival case).
            NotifyIfOutOfSession("P2P: reconnect failed (the host does not answer)");

            RaiseDisconnected();

            // Entry gate is now invalid: the next invite must not be rejected as "in progress".
            SteamLobbyImpl.ResetJoinAfterLinkLost("handshake scaduto (" + (int)HANDSHAKE_TIMEOUT_SECONDS +
                                                  " s)");

            ModLog.Info(MarkerOpen + " link not open: re-entering with an Accept from the host " +
                        "(`ReviveLink`) o con un Connect nuovo (`Begin`)");
        }

        /// <summary>
        /// Fires the end event. Single path for all three ways a link ends, so a future change
        /// touches one place.
        /// </summary>
        private static void RaiseDisconnected()
        {
            try
            {
                Action handler = _onDisconnected;
                if (handler != null)
                    handler();
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + " error in Disconnected: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>State line for log and report.</summary>
        internal static string LobbySummary()
        {
            try
            {
                CSteamID lobby = _lobby.m_SteamID != 0UL ? _lobby : SteamLobbyImpl.CurrentLobby;
                if (lobby.m_SteamID == 0UL)
                    return "nessuna lobby";

                return "lobby=" + lobby.m_SteamID +
                       " hostDichiarato=" + DeclaredHost() +
                       " proprietario=" + SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID +
                       " hello=" + (_helloed ? "si" : "no") + " link=" + (_linkLost ? "perso" : "ok");
            }
            catch (Exception)
            {
                return "illeggibile";
            }
        }

        /// <summary>
        /// Sends a frame. Before the hello, frames queue (the game's Intro goes out on the same
        /// stack as Connect); without queueing the session would never start. IOException when the
        /// link is lost - the path NetworkManager.SendQueue already uses to disconnect.
        /// </summary>
        internal static void Send(byte[] framed, bool reliable)
        {
            if (framed == null || framed.Length == 0)
                return;

            if (_linkLost)
                throw new System.IO.IOException("[Net] p2p: link perso");

            if (!_helloed)
            {
                if (_outbox.Count >= MAX_OUTBOX)
                {
                    _outbox.RemoveAt(0);
                    _outboxDropped++;
                    if (_outboxDropped <= 3)
                        ModLog.Warn(MarkerOpen + "coda dell'hello piena (" + MAX_OUTBOX + " pacchetti): " +
                                    "the oldest was dropped (hello not sent yet?)");
                }

                _outbox.Add(new Pending(framed, reliable));
                return;
            }

            SendFrame(framed, reliable);
        }

        // === Opening ===

        /// <summary>
        /// Sends the hello to the host declared in the lobby. Game frames stay queued until it is
        /// sent.
        ///
        /// The target is <c>hostSteamId</c>, NOT <c>GetLobbyOwner</c>: lobby ownership transfers
        /// to the next member when the host leaves, so GetLobbyOwner would answer a different
        /// player. A missing hostSteamId is not a technical error - it is "session no longer
        /// available" (see SessionUnavailable). The first HOST_DATA_GRACE_SECONDS are tolerance
        /// for the LobbyDataUpdate_t; after that, the missing value is a fact.
        /// </summary>
        private static bool TrySendHello()
        {
            ulong declared = DeclaredHost();

            if (declared == 0UL)
            {
                float waited = Time.realtimeSinceStartup - _openedAt;

                if (waited < HOST_DATA_GRACE_SECONDS)
                {
                    LogOnce("the lobby " + _lobby.m_SteamID + " does not carry yet " +
                            SteamLobbyImpl.KEY_STEAM + ": aspetto (e' la tolleranza di " +
                            (int)HOST_DATA_GRACE_SECONDS + " s)");
                    return false;
                }

                SessionUnavailable("the host did not declare " + SteamLobbyImpl.KEY_STEAM + " in the lobby " +
                                   _lobby.m_SteamID + " (old lobby, or a host that does not publish its data)");
                return false;
            }

            if (declared == _myId)
            {
                // Host talking to itself is not P2P; the host stays on TCP loopback.
                SessionUnavailable("I am the hosting player (" + _myId + "): P2P is for the " +
                                   "PEER, l'host resta sul TCP del loopback");
                return false;
            }

            // Declared host no longer in lobby: session does not exist. GetLobbyOwner would have
            // masked this by answering the new owner.
            if (!SteamLobbyImpl.IsLobbyMember(new CSteamID(declared)))
            {
                SessionUnavailable("the declared host (" + declared + ") is no longer in the lobby " +
                                   _lobby.m_SteamID);
                return false;
            }

            _host = new CSteamID(declared);

            if (!SendControl(P2PWire.KindHello))
            {
                LogOnce("hello not sent to " + declared + " (retried next frame)");
                return false;
            }

            _helloed = true;
            _lastRx = Time.realtimeSinceStartup;
            _nextKeepAlive = _lastRx + KEEPALIVE_SECONDS;

            // Written AFTER send: says the packet left, not that we tried to send it.
            ModLog.Info(SteamP2PTransport.MarkerHelloSent + _host.m_SteamID + " (timeout handshake " +
                        (int)HANDSHAKE_TIMEOUT_SECONDS + "s) - host DECLARED in the lobby=" +
                        _lobby.m_SteamID + " (proprietario attuale=" +
                        SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID + ") canale=" +
                        P2PWire.ChannelReliable);

            FlushOutbox();

            // Connected fires from the first packet received, not here - the tunnel is requested,
            // not open.
            return true;
        }

        /// <summary>
        /// The host declared in lobby data (hostSteamId). 0 if absent or invalid. GetLobbyOwner is
        /// deliberately not used: ownership transfers on host leave.
        /// </summary>
        private static ulong DeclaredHost()
        {
            try
            {
                string text = SteamMatchmaking.GetLobbyData(_lobby, SteamLobbyImpl.KEY_STEAM);
                if (string.IsNullOrEmpty(text))
                    return 0UL;

                ulong id;
                if (!ulong.TryParse(text.Trim(), out id))
                    return 0UL;

                return id == 0UL ? 0UL : id;
            }
            catch (Exception)
            {
                // Unreadable data means "not declared": session unavailable.
                return 0UL;
            }
        }

        /// <summary>
        /// "Session no longer available": a fact, not an error. Reported once, event raised, no
        /// guessing about who to call.
        /// </summary>
        private static void SessionUnavailable(string why)
        {
            if (_linkLost)
                return;

            ModLog.Warn(MarkerUnavailable + " (" + why + ")");
            Notify("P2P: the session is no longer available");

            _linkLost = true;
            _phase = Phase.Disconnected;

            RaiseDisconnected();
        }

        /// <summary>Screen notification with try/catch: the UI may not exist yet.</summary>
        private static void Notify(string message)
        {
            try
            {
                if (Main.Multiplayer != null && Main.Multiplayer.NotificationManager != null)
                    Main.Multiplayer.NotificationManager.DisplayNotification(message);
            }
            catch (Exception)
            {
                // Log line already emitted; notification is a bonus.
            }
        }

        /// <summary>Flushes queued frames that waited for the hello, in order.</summary>
        private static void FlushOutbox()
        {
            if (_outbox.Count == 0)
                return;

            List<Pending> copy = new List<Pending>(_outbox);
            _outbox.Clear();

            for (int i = 0; i < copy.Count; i++)
            {
                try
                {
                    SendFrame(copy[i].Framed, copy[i].Reliable);
                }
                catch (Exception e)
                {
                    ModLog.Error(MarkerOpen + " error in queue: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // === Sending ===

        /// <summary>
        /// Sends a frame on the right channel: Reliable -> 1 (or 2 for skins), Unreliable -> 0
        /// with the sequence header.
        /// </summary>
        private static void SendFrame(byte[] framed, bool reliable)
        {
            if (_host.m_SteamID == 0UL)
                return;

            if (reliable)
            {
                TransportClass cls = TransportClass.Reliable;
                byte type = framed.Length > 2 ? framed[2] : (byte)0;
                int channel = P2PWire.ChannelFor(cls, type);
                byte[] packet = P2PWire.BuildData(framed, cls, _myId, 0u);
                SendPacket(_host, packet, channel, true);
                return;
            }

            _txSeq++;
            byte[] sequenced = P2PWire.BuildData(framed, TransportClass.Unreliable, _myId, _txSeq);
            SendPacket(_host, sequenced, P2PWire.ChannelUnreliable, false);
        }

        /// <summary>Sends a control packet (hello/keepalive/bye) on channel 1, reliable.</summary>
        private static bool SendControl(byte kind)
        {
            if (_host.m_SteamID == 0UL)
                return false;

            byte[] packet = P2PWire.BuildControl(kind, _myId);
            return SendPacket(_host, packet, P2PWire.ChannelReliable, true);
        }

        /// <summary>
        /// SendP2PPacket can return false (send buffer full) with no exception and no callback:
        /// the return value is checked every time, first failure per channel logged once.
        /// </summary>
        private static bool SendPacket(CSteamID peer, byte[] packet, int channel, bool reliable)
        {
            EP2PSend mode = reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;

            bool sent;
            try
            {
                sent = SteamNetworking.SendP2PPacket(peer, packet, (uint)packet.Length, mode, channel);
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + " error in SendP2PPacket: " + e.GetType().Name + ": " + e.Message);
                return false;
            }

            if (!sent)
            {
                _sendFailLogged++;
                if (_sendFailLogged <= 3)
                    ModLog.Warn(MarkerOpen + "SendP2PPacket ha risposto false (canale=" + channel +
                                " byte=" + packet.Length + "): buffer di invio pieno, il pacchetto e' perso");
            }

            return sent;
        }

        // === Receiving ===

        /// <summary>Drains all three channels, one read cap per channel per frame.</summary>
        private static void Drain()
        {
            DrainChannel(P2PWire.ChannelUnreliable);
            DrainChannel(P2PWire.ChannelReliable);
            DrainChannel(P2PWire.ChannelHeavy);
        }

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

                if (read == 0u)
                    continue;

                Handle(channel, remote, _rxBuffer, (int)read);
            }
        }

        /// <summary>
        /// One packet: recognized, header stripped, only frames delivered. Foreign packets (e.g.
        /// the probe's byte on channel 0) are counted and dropped, not interpreted.
        /// </summary>
        private static void Handle(int channel, CSteamID remote, byte[] packet, int length)
        {
            byte kind;
            int ignoredOffset;
            uint ignoredSeq;
            ulong controlSender;

            // Not delivering anything when the link is lost: Tick still drains residuals (the
            // buffer must be emptied), but the game is no longer in session.
            if (_linkLost)
                return;

            // RX trace with channel and sender: if the tunnel is healthy but the game does not
            // connect, this says on which channel packets are read. First RX_LOG_FIRST per
            // channel.
            if (_rxLogged[channel] < RX_LOG_FIRST)
            {
                _rxLogged[channel]++;
                ModLog.Info(MarkerOpen + "rx canale=" + channel + " da=" + remote.m_SteamID +
                            " byte=" + length);
            }

            if (!P2PWire.TryParse(packet, length, out kind, out ignoredOffset, out controlSender, out ignoredSeq))
            {
                _foreign[channel]++;
                if (_foreignLogged[channel]++ == 0)
                    ModLog.Warn(MarkerOpen + "pacchetto estraneo sul canale " + channel + " (da " +
                                remote.m_SteamID + ", " + length + " byte): scartato (sonda P2P accesa?)");
                return;
            }

            // First valid packet opens the tunnel: Connecting -> Connected.
            if (_phase == Phase.Connecting)
                MarkConnected(channel);

            _lastRx = Time.realtimeSinceStartup;

            switch (kind)
            {
                case P2PWire.KindData:
                {
                    ulong origin;
                    uint seq;
                    int payloadOffset;

                    if (!P2PWire.TryParseData(packet, length, channel, out origin, out seq, out payloadOffset))
                        return;

                    if (channel == P2PWire.ChannelUnreliable)
                    {
                        uint last;
                        if (_lastSeqByOrigin.TryGetValue(origin, out last) && !P2PWire.IsNewer(seq, last))
                        {
                            _droppedStale++;
                            if (_droppedStaleLogged++ == 0)
                                ModLog.Info(MarkerOpen + "scarto i pacchetti VECCHI del canale 0 (per " +
                                            "mittente): primo scarto origin=" + origin + " seq=" + seq +
                                            " ultima=" + last);
                            return;
                        }

                        _lastSeqByOrigin[origin] = seq;
                    }

                    Deliver(packet, payloadOffset, length - payloadOffset);
                    return;
                }

                case P2PWire.KindKeepAlive:
                    return;

                case P2PWire.KindBye:
                    LinkLost("l'host (" + remote.m_SteamID + ") ha mandato BYE");
                    return;

                case P2PWire.KindHello:
                    // The peer should not receive hello; noted once and ignored.
                    LogOnce("hello received from " + remote.m_SteamID + " (unexpected direction, ignored)");
                    return;
            }
        }

        /// <summary>
        /// Connecting -> Connected: first valid packet is the only fact that says the tunnel is
        /// open. Two lines on purpose - which channel and how long the handshake took. From here
        /// on the keepalive timeout applies.
        /// </summary>
        private static void MarkConnected(int channel)
        {
            float now = Time.realtimeSinceStartup;
            float handshake = now - _openedAt;
            string seconds = handshake.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            _phase = Phase.Connected;

            ModLog.Info(SteamP2PTransport.MarkerFirstPacket + channel + ") after " + seconds + "s");
            ModLog.Info(SteamP2PTransport.MarkerConnected + seconds + "s) - host=" + _host.m_SteamID +
                        ": da adesso il timeout e' quello del battito (" +
                        (int)KEEPALIVE_TIMEOUT_SECONDS + " s)");

            try
            {
                Action connected = _onConnected;
                if (connected != null)
                    connected();
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + " error in Connected: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Delivers the payload (frames, header stripped) to the seam. Copied because the buffer is reused.</summary>
        private static void Deliver(byte[] packet, int offset, int count)
        {
            if (count <= 0)
                return;

            byte[] framed = new byte[count];
            Buffer.BlockCopy(packet, offset, framed, 0, count);

            Action<byte[]> handler = _onData;
            if (handler != null)
                handler(framed);
        }

        // === Link loss ===

        /// <summary>
        /// Link lost: write marker once, raise Disconnected, Send throws IOException from now on.
        /// State stays lost until explicitly reopened - do not pretend it came back.
        /// </summary>
        private static void LinkLost(string reason)
        {
            if (_linkLost)
                return;

            _linkLost = true;
            _phase = Phase.Disconnected;

            // Close the native session now, otherwise it lingers until Begin or process exit.
            // End() would zero _linkLost and break the IOException contract.
            if (_host.m_SteamID != 0UL) { try { SteamNetworking.CloseP2PSessionWithUser(_host); } catch { } }

            if (!_timeoutLogged)
            {
                _timeoutLogged = true;

                if (reason != null && reason.StartsWith("no packet", StringComparison.Ordinal))
                    ModLog.Error(MarkerTimeout + reason + " (the host sends nothing any more: session lost)");
                else
                    ModLog.Error(MarkerLinkLost + ": " + reason);
            }

            // Second occurrence after a revival: NetworkManager exits early on "already
            // disconnected", so only the transport can notify (see NotifyIfOutOfSession).
            NotifyIfOutOfSession("P2P: link lost again (the host sends nothing any more)");

            RaiseDisconnected();

            // The lobby entry that opened this session is no longer valid: leaving it set makes
            // the next invite rejected as "in progress". Do NOT LeaveLobby - the host's lobby is
            // still alive and leaving would make us a non-member.
            SteamLobbyImpl.ResetJoinAfterLinkLost(reason);

            ModLog.Info(MarkerOpen + "in attesa di una nuova sessione P2P: la riapre un Accept dell'host " +
                        "(`ReviveLink`) or a new Connect (`Begin`). Until then no packet " +
                        "parte e nessuno arriva");
        }

        /// <summary>
        /// Accepting a session brings the transport back. Previously the accept was Steam-only:
        /// the session existed but _linkLost stayed true, so Tick exited in the head and nothing
        /// fired. Reset is targeted - only link state; _host/_lobby/_myId/_txSeq/_outbox untouched
        /// (End() would close the just-accepted session; _txSeq=0 would make the receiver discard
        /// everything as stale; _outbox holds frames waiting for the hello). Receiver-side
        /// _lastSeqByOrigin is cleared because the new session may restart from a lower sequence.
        /// </summary>
        private static void ReviveLink(string why)
        {
            if (!_opened)
                return;

            if (!_linkLost)
                return;

            float now = Time.realtimeSinceStartup;

            _linkLost = false;
            _helloed = false;
            _revived = true;
            _phase = Phase.Connecting;
            _openedAt = now;
            _lastRx = now;
            _nextKeepAlive = now;
            _timeoutLogged = false;
            _sendFailLogged = 0;
            _outboxDropped = 0;
            _droppedStale = 0;
            _droppedStaleLogged = 0;
            _lastSeqByOrigin.Clear();
            _peerLogged.Clear();
            _returnLogged.Clear();

            for (int i = 0; i < 3; i++)
            {
                _foreign[i] = 0;
                _foreignLogged[i] = 0;
                _rxLogged[i] = 0;
            }

            ModLog.Info(MarkerOpen + "link RITROVATO (" + why + "): l'hello si rimanda e il battito " +
                        "riparte - hello=no, link=ok, fase=negoziazione (timeout " +
                        (int)HANDSHAKE_TIMEOUT_SECONDS + " s)");

            Notify("P2P: link ritrovato, riconnessione in corso");

            // Connected is not fired here: the event comes from the first valid packet (see
            // MarkConnected).
        }

        /// <summary>
        /// "Is this the host I am waiting for?" - gate for revival. A packet from another lobby
        /// member does not mean our link to the host came back. Uses _host when set, otherwise
        /// the declared host (same source as TrySendHello, not GetLobbyOwner).
        /// </summary>
        private static bool IsExpectedHost(ulong id)
        {
            if (id == 0UL)
                return false;

            if (id == _myId)
                return false;

            if (_host.m_SteamID != 0UL)
                return id == _host.m_SteamID;

            ulong declared = DeclaredHost();
            return declared != 0UL && declared == id;
        }

        /// <summary>
        /// Screen notification only when nobody else will - i.e. after a revive, where
        /// NetworkManager.OnTransportEnded exits early on "already disconnected". The _revived
        /// gate matters: on first connect the normal path notifies, and a second notification
        /// here would be two warnings for the same fact.
        /// </summary>
        private static void NotifyIfOutOfSession(string message)
        {
            try
            {
                if (!_revived)
                    return;

                if (Main.Multiplayer == null || Main.Multiplayer.NetworkManager == null)
                    return;

                if (Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                Notify(message);
            }
            catch (Exception)
            {
                // Log line already emitted; notification is a bonus.
            }
        }

        // === Accept-session callback ===

        /// <summary>
        /// P2PSessionRequest_t -> AcceptP2PSessionWithUser, only for lobby members. Without this
        /// callback every peer packet is silently discarded; without the member gate, anyone
        /// knowing the SteamID could open a session.
        /// </summary>
        private static void OnSessionRequest(P2PSessionRequest_t request)
        {
            try
            {
                CSteamID remote = request.m_steamIDRemote;
                ulong id = remote.m_SteamID;

                if (id == 0UL)
                    return;

                // Say "request received" before any gate: the fact that the tunnel was requested
                // from us.
                ModLog.Info(SteamP2PTransport.MarkerSessionRequest + id + " (lato PEER)");

                if (!SteamLobbyImpl.IsLobbyMember(remote))
                {
                    ModLog.Warn(SteamP2PTransport.MarkerAcceptFail + id + " (not a lobby member: " +
                                "session NOT accepted)");
                    return;
                }

                bool accepted;
                try
                {
                    accepted = SteamNetworking.AcceptP2PSessionWithUser(remote);
                }
                catch (Exception e)
                {
                    ModLog.Error(SteamP2PTransport.MarkerAcceptFail + id + " eccezione: " +
                                 e.GetType().Name + ": " + e.Message);
                    return;
                }

                // Report the outcome always: true = session exists; false = Steam refused locally
                // (usually already present, or id no longer valid).
                if (accepted)
                {
                    if (_peerLogged.Add(id))
                        ModLog.Info(SteamP2PTransport.MarkerAcceptOk + id + " ACCEPTED (lobby member)");

                    // Return session: the host's own request to us. Accepting it makes the tunnel
                    // bidirectional; without this, the host's reply sits in Valve's buffer.
                    if (_host.m_SteamID != 0UL && id == _host.m_SteamID && _returnLogged.Add(id))
                        ModLog.Info(SteamP2PTransport.MarkerReturnAccepted + "=" + id);
                }
                else if (_peerLogged.Add(id))
                {
                    ModLog.Info(SteamP2PTransport.MarkerAcceptFail + id + " (Steam ha risposto false: " +
                                "session already present or id no longer valid)");
                }

                // Accepting is not enough: if the session is the one with OUR host, the link is
                // back - ReviveLink re-arms hello and timeout. Fires on accepted AND on refused
                // ("already present"): the request itself is proof the host is talking to us.
                if (IsExpectedHost(id))
                    ReviveLink("P2PSessionRequest_t da " + id + " accettata (lato PEER)");
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + " error in P2PSessionRequest_t: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// Black box: P2PSessionConnectFail_t. Arrives 10-15s after the attempt and is not
        /// symmetric (only the side with queued packets sees it). It is the only line that says
        /// WHY Steam refused. Does not disconnect: the timeout makes the decision.
        /// </summary>
        private static void OnSessionConnectFail(P2PSessionConnectFail_t failure)
        {
            try
            {
                ulong id = failure.m_steamIDRemote.m_SteamID;
                int error = failure.m_eP2PSessionError;

                ModLog.Error(SteamP2PTransport.MarkerSessionFail + id + " err=" + error + " " +
                             ErrorName(error) + " (PEER side: tunnel not open)");
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + " error in P2PSessionConnectFail_t: " + e.GetType().Name + ": " +
                             e.Message);
            }
        }

        /// <summary>Steam error name; a bare number is not readable.</summary>
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

        /// <summary>
        /// Registers P2PSessionRequest_t (and P2PSessionConnectFail_t) once, only when Steam is
        /// ready: registering before the game's Init would talk to native API at a point we do
        /// not control.
        /// </summary>
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

                // Error callback registered here: it is the only line that says WHY Steam refused.
                try
                {
                    _cbSessionFail = Callback<P2PSessionConnectFail_t>.Create(OnSessionConnectFail);
                }
                catch (Exception e)
                {
                    ModLog.Warn(MarkerOpen + "Callback<P2PSessionConnectFail_t>.Create: " +
                                e.GetType().Name + ": " + e.Message + " (la sessione funziona lo stesso, " +
                                "only the error line is missing)");
                }

                return true;
            }
            catch (Exception e)
            {
                _registerFailed = true;
                ModLog.Error(MarkerOpen + "Callback<P2PSessionRequest_t>.Create: " + e.GetType().Name +
                             ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// FIELD, not local: Callback&lt;T&gt;.Create registers the delegate in a global registry
        /// via GCHandle; a local would be collected and the callbacks would silently stop.
        /// </summary>
        private static Callback<P2PSessionRequest_t> _cbSessionRequest;

        /// <summary>Session black box: same reason (field, not local).</summary>
        private static Callback<P2PSessionConnectFail_t> _cbSessionFail;

        /// <summary>One line per reason: Tick is per frame, the log must not flood.</summary>
        private static readonly HashSet<string> _loggedReasons = new HashSet<string>();

        private static void LogOnce(string message)
        {
            if (!_loggedReasons.Add(message))
                return;

            ModLog.Warn(MarkerOpen + message);
        }

        /// <summary>A frame waiting for the hello.</summary>
        private sealed class Pending
        {
            internal readonly byte[] Framed;
            internal readonly bool Reliable;

            internal Pending(byte[] framed, bool reliable)
            {
                byte[] copy = new byte[framed.Length];
                Buffer.BlockCopy(framed, 0, copy, 0, framed.Length);

                Framed = copy;
                Reliable = reliable;
            }
        }
    }
}