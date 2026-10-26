using Blasphemous.ModdingAPI;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// P2P probe: sends 1 byte per second on channel 0 to lobby peers, receives them, and asks Steam
/// every 3 seconds whether the session is active, direct or relayed. A measurement, not a
/// transport: no <c>ICoopTransport</c>, no game byte, no <c>NetworkManager</c> involvement.
/// Independently of the TCP session - it must work exactly when TCP fails. Accepts sessions only
/// for lobby members (<c>P2PSessionRequest_t</c>); without that, every peer packet is silently
/// dropped. Named *Impl because it names Steamworks types; the only caller is the
/// reflection-gated <c>SteamP2PProbe</c> facade.
/// </summary>
internal static class SteamP2PProbeImpl
{
    internal const string MarkerStart = "[P2P] sonda avviata";
    internal const string MarkerStop = "[P2P] sonda fermata";
    internal const string MarkerState = "[P2P] peer=";

    /// <summary>
    /// Session line distinguishing "handshake did not complete" (active=0 connecting=1) from
    /// "tunnel is healthy, block is in our code" (active=1 connecting=0). <c>tx</c> is bytes
    /// queued for send, the only number telling "sent and lost" from "sent and still in the
    /// buffer".
    /// </summary>
    internal const string MarkerSession = "[Sonda P2P] ";
    internal const string MarkerSend = "[P2P] invio: ";
    internal const string MarkerSendFail = "[P2P] invio fallito: ";
    internal const string MarkerReceive = "[P2P] ricevuto: ";
    internal const string MarkerSessionRequest = "[P2P] sessione richiesta: ";
    internal const string MarkerSessionAccept = "[P2P] sessione accettata: ";
    internal const string MarkerSessionRefuse = "[P2P] sessione rifiutata: ";
    internal const string MarkerCallback = "[P2P] callback registrata: ";
    internal const string MarkerNoLobby = "[P2P] nessuna lobby: ";
    internal const string MarkerError = "[P2P] error: ";

    /// <summary>Channel of the measurement: 0, the same channel the plan assigns to gameplay.</summary>
    private const int CHANNEL = 0;

    /// <summary>A single byte. Not a message: a heartbeat.</summary>
    private static readonly byte[] _payload = new byte[1] { 0x01 };

    /// <summary>How often the byte is sent.</summary>
    private const float SEND_INTERVAL_S = 1f;

    /// <summary>How often state is queried. Not per frame - GetP2PSessionState is synchronous.</summary>
    private const float MEASURE_INTERVAL_S = 3f;

    /// <summary>Retry interval for the P2PSessionRequest_t registration when it failed.</summary>
    private const float REGISTER_RETRY_S = 2f;

    /// <summary>Max packets read per frame; bounds a possible backlog to a single frame.</summary>
    private const int MAX_PACKETS_PER_FRAME = 64;

    /// <summary>Max read buffer. Our packets are 1 byte; the cap guards against a corrupt length.</summary>
    private const uint MAX_PACKET_BYTES = 64u * 1024u;

    // Callback<T>.Create pins the delegate in a global registry via GCHandle; a local would be
    // collected and callbacks would silently stop. Field keeps it alive.
    private static Callback<P2PSessionRequest_t> _cbSessionRequest;

    private static bool _registered;
    private static bool _registerLogged;
    private static bool _registerFailed;
    private static float _nextRegisterTry;
    private static bool _active;
    private static float _nextSend;
    private static float _nextMeasure;
    private static byte[] _rxBuffer = new byte[1024];

    private static long _rxTotal;
    private static readonly Dictionary<ulong, long> _rxByPeer = new Dictionary<ulong, long>();
    private static readonly HashSet<ulong> _peersSeen = new HashSet<ulong>();
    private static readonly HashSet<ulong> _sendOkLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _sendFailLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _rxLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _acceptLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _stateFailLogged = new HashSet<ulong>();

    private static bool _noLobbyLogged;
    private static bool _measured;
    private static int _lastPeers;
    private static int _lastActive;
    private static int _lastDirect;
    private static int _lastRelay;
    private static string _summary = string.Empty;

    /// <summary>True while the probe is measuring. Read by the UI for the button label.</summary>
    internal static bool Active
    {
        get { return _active; }
    }

    /// <summary>
    /// Registers the accept-session callback. Idempotent, never throws. Called from Arm (mod init)
    /// and from Tick (per frame until it succeeds), because a mod that starts before Steam would
    /// otherwise never register it - and without this callback every peer packet is silently
    /// dropped.
    /// </summary>
    internal static void Arm()
    {
        try
        {
            EnsureRegistered();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Arm)");
        }
    }

    /// <summary>
    /// Probe P2P: starts measuring. Returns false when Steam is not available. With Steam present
    /// returns true even without a lobby: the probe waits and starts on its own once inside one.
    /// </summary>
    internal static bool Start()
    {
        try
        {
            if (!SteamLobbyImpl.Available)
            {
                ModLog.Warn(MarkerNoLobby + "Steam is unavailable: the probe does not start");
                return false;
            }

            if (!EnsureRegistered())
                return false;

            if (_active)
                return true;

            _active = true;
            _measured = false;
            _noLobbyLogged = false;
            _rxTotal = 0L;
            _lastPeers = 0;
            _lastActive = 0;
            _lastDirect = 0;
            _lastRelay = 0;
            _rxByPeer.Clear();
            _peersSeen.Clear();
            _sendOkLogged.Clear();
            _sendFailLogged.Clear();
            _rxLogged.Clear();
            _acceptLogged.Clear();
            _stateFailLogged.Clear();

            float now = Time.realtimeSinceStartup;
            _nextSend = now;
            _nextMeasure = now + MEASURE_INTERVAL_S;

            _summary = "P2P: in ascolto...";

            CSteamID lobby = SteamLobbyImpl.CurrentLobby;
            ModLog.Info(MarkerStart + " canale=" + CHANNEL + " byte=1 ogni " + SEND_INTERVAL_S +
                        " s, measures every " + MEASURE_INTERVAL_S + " s, lobby=" +
                        (lobby.m_SteamID == 0UL ? "nessuna (si aspetta)" : lobby.m_SteamID.ToString()));
            return true;
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Start)");
            return false;
        }
    }

    /// <summary>
    /// Stops the probe and closes the P2P sessions it opened (an open session is invisible and
    /// survives until process exit). Idempotent, never throws, silent if not measuring.
    /// </summary>
    internal static void Stop()
    {
        try
        {
            if (!_active)
                return;

            _active = false;
            _summary = string.Empty;

            int closed = 0;
            foreach (ulong peer in _peersSeen)
            {
                try
                {
                    if (SteamNetworking.CloseP2PSessionWithUser(new CSteamID(peer)))
                        closed++;
                }
                catch (Exception)
                {
                    // A session that will not close must not prevent stopping the probe.
                }
            }

            ModLog.Info(MarkerStop + " peer=" + _peersSeen.Count + " sessioni chiuse=" + closed +
                        " rxTotale=" + _rxTotal);
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Stop)");
        }
    }

    /// <summary>
    /// Per-frame cycle: drains channel 0 (must be drained or the buffer fills), sends a byte every
    /// SEND_INTERVAL_S, measures every MEASURE_INTERVAL_S. When stopped, nothing but an if runs;
    /// the callback stays registered.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            if (!_registered && !EnsureRegistered())
                return;

            if (!_active)
                return;

            DrainChannel();

            float now = Time.realtimeSinceStartup;

            if (now >= _nextSend)
            {
                _nextSend = now + SEND_INTERVAL_S;
                Send();
            }

            if (now >= _nextMeasure)
            {
                _nextMeasure = now + MEASURE_INTERVAL_S;
                Measure();
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Tick)");
        }
    }

    /// <summary>
    /// Short line for the connection screen, built on the last measurement. Empty while stopped:
    /// users who do not press the button see no extra line.
    /// </summary>
    internal static string Status()
    {
        return _active ? _summary : string.Empty;
    }

    // === Actions ===

    /// <summary>
    /// Heartbeat: one byte to every lobby peer except self, channel 0, k_EP2PSendReliable. Return
    /// value is checked: false means send buffer full and the byte is silently lost. Peer list is
    /// re-read every time so friends joining or leaving appear without restarting the probe.
    /// </summary>
    private static void Send()
    {
        CSteamID lobby = SteamLobbyImpl.CurrentLobby;
        if (lobby.m_SteamID == 0UL)
        {
            RequireLobby("invio");
            return;
        }

        _noLobbyLogged = false;

        int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
        CSteamID me = SteamUser.GetSteamID();

        for (int i = 0; i < members; i++)
        {
            CSteamID peer = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (peer.m_SteamID == 0UL || peer.m_SteamID == me.m_SteamID)
                continue;

            ulong id = peer.m_SteamID;
            _peersSeen.Add(id);

            bool sent;
            try
            {
                sent = SteamNetworking.SendP2PPacket(peer, _payload, (uint)_payload.Length,
                                                     EP2PSend.k_EP2PSendReliable, CHANNEL);
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (SendP2PPacket)");
                continue;
            }

            if (sent)
            {
                if (_sendOkLogged.Add(id))
                    ModLog.Info(MarkerSend + "peer=" + id + " byte=" + _payload.Length +
                                " canale=" + CHANNEL + " esito=ok");
                _sendFailLogged.Remove(id);
            }
            else if (_sendFailLogged.Add(id))
            {
                // false is not an exception and no callback fires: without this line the byte is
                // lost and nobody names it. One line per peer, not per second.
                ModLog.Warn(MarkerSendFail + "peer=" + id + " (SendP2PPacket ha risposto false: buffer di " +
                            "invio pieno) - il byte si e' perso, la sonda continua");
            }
        }
    }

    /// <summary>
    /// Receive: drains channel 0. Reads sender and length, counts bytes per peer (the number in
    /// <c>rx=</c>). ReadP2PPacket returning false exits the loop instead of retrying within the
    /// same frame; the per-frame cap prevents a backlog from stretching the frame.
    /// </summary>
    private static void DrainChannel()
    {
        int guard = MAX_PACKETS_PER_FRAME;

        while (guard-- > 0)
        {
            uint available;
            if (!SteamNetworking.IsP2PPacketAvailable(out available, CHANNEL))
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
            if (!SteamNetworking.ReadP2PPacket(_rxBuffer, capacity, out read, out remote, CHANNEL))
                break;

            if (read == 0u || remote.m_SteamID == 0UL)
                continue;

            ulong id = remote.m_SteamID;

            long previous;
            _rxByPeer.TryGetValue(id, out previous);
            _rxByPeer[id] = previous + read;
            _rxTotal += read;

            if (_rxLogged.Add(id))
                ModLog.Info(MarkerReceive + "peer=" + id + " byte=" + read + " canale=" + CHANNEL);
        }
    }

    /// <summary>
    /// Measurement, one line per lobby peer:
    ///   [P2P] peer=&lt;id&gt; attiva=&lt;0|1&gt; relay=&lt;0|1&gt; via=diretto|relay ip=&lt;a.b.c.d|n/d&gt; err=&lt;n&gt; rx=&lt;byte&gt;
    ///
    /// Read <c>attiva</c> first: with attiva=0 the relay/via pair has no meaning. GetP2PSessionState
    /// is synchronous (queries the Steam client) and only runs every 3 seconds. If it returns false
    /// the row is still printed with attiva=0 (no session) plus a one-shot per-peer note that the
    /// call failed, so a failed query is not confused with a peer that never accepted.
    /// </summary>
    private static void Measure()
    {
        CSteamID lobby = SteamLobbyImpl.CurrentLobby;
        if (lobby.m_SteamID == 0UL)
        {
            RequireLobby("misura");
            return;
        }

        _noLobbyLogged = false;

        int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
        CSteamID me = SteamUser.GetSteamID();

        int peers = 0;
        int active = 0;
        int direct = 0;
        int relay = 0;

        for (int i = 0; i < members; i++)
        {
            CSteamID peer = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (peer.m_SteamID == 0UL || peer.m_SteamID == me.m_SteamID)
                continue;

            ulong id = peer.m_SteamID;
            peers++;
            _peersSeen.Add(id);

            P2PSessionState_t state;
            bool ok;
            try
            {
                ok = SteamNetworking.GetP2PSessionState(peer, out state);
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (GetP2PSessionState)");
                continue;
            }

            if (!ok && _stateFailLogged.Add(id))
                ModLog.Warn(MarkerError + "GetP2PSessionState ha risposto false per peer=" + id +
                            " (nessuna sessione con quel peer): la riga seguente vale attiva=0");

            int isActive = state.m_bConnectionActive != 0 ? 1 : 0;
            int isRelay = state.m_bUsingRelay != 0 ? 1 : 0;

            string via = isRelay == 0 ? "diretto" : "relay";

            // m_nRemoteIP is 0 before the session is up: "n/d" is the truth, not an error.
            string ip = state.m_nRemoteIP == 0u ? "n/d" : FormatIp(state.m_nRemoteIP);

            long rx;
            _rxByPeer.TryGetValue(id, out rx);

            if (isActive != 0)
            {
                active++;
                if (isRelay == 0)
                    direct++;
                else
                    relay++;
            }

            ModLog.Info(MarkerState + id + " attiva=" + isActive + " relay=" + isRelay +
                        " via=" + via + " ip=" + ip + " err=" + state.m_eP2PSessionError +
                        " rx=" + rx);

            // active=0 connecting=1  -> low-level handshake did not complete.
            // active=1 connecting=0  -> tunnel is healthy, block is in our code.
            // active=0 connecting=0  -> no session, Accept never happened.
            // tx = m_nBytesQueuedForSend: if it grows and never drains, the packet is stuck in
            // Valve's send buffer.
            int connecting = state.m_bConnecting != 0 ? 1 : 0;

            ModLog.Info(MarkerSession + "peer=" + id + " active=" + isActive +
                        " connecting=" + connecting + " error=" + state.m_eP2PSessionError +
                        " relay=" + isRelay + " ip=" + ip + " tx=" + state.m_nBytesQueuedForSend);
        }

        _lastPeers = peers;
        _lastActive = active;
        _lastDirect = direct;
        _lastRelay = relay;
        _measured = true;

        _summary = BuildSummary();
    }

    /// <summary>Short screen line built from the last measurement.</summary>
    private static string BuildSummary()
    {
        if (!_measured)
            return "P2P: in ascolto...";

        if (_lastPeers == 0)
            return "P2P: no peer in the lobby";

        return "P2P: peer=" + _lastPeers + " attive=" + _lastActive +
               " diretto=" + _lastDirect + " relay=" + _lastRelay + " rx=" + _rxTotal + "B";
    }

    /// <summary>One-shot "no lobby" line; the probe stays active and starts on its own later.</summary>
    private static void RequireLobby(string phase)
    {
        if (_noLobbyLogged)
            return;

        _noLobbyLogged = true;
        _summary = "P2P: nessuna lobby (ospita o accetta un invito)";
        ModLog.Warn(MarkerNoLobby + "nessuna lobby Steam (" + phase + "): la sonda resta accesa e " +
                    "comincia da sola appena si entra in una lobby");
    }

    /// <summary>
    /// P2PSessionRequest_t -> AcceptP2PSessionWithUser. Only for lobby members: without that,
    /// anyone knowing the SteamID could open a session. Acceptance is refused (and reported once
    /// per peer) for non-members - a silent refusal is indistinguishable from a probe that
    /// receives nothing.
    /// </summary>
    private static void OnSessionRequest(P2PSessionRequest_t request)
    {
        try
        {
            CSteamID remote = request.m_steamIDRemote;
            ulong id = remote.m_SteamID;

            ModLog.Info(MarkerSessionRequest + "peer=" + id);

            if (id == 0UL || !SteamLobbyImpl.IsLobbyMember(remote))
            {
                if (_acceptLogged.Add(id))
                    ModLog.Warn(MarkerSessionRefuse + "peer=" + id + " (not a lobby member: " +
                                "session NOT accepted)");
                return;
            }

            bool accepted = SteamNetworking.AcceptP2PSessionWithUser(remote);

            if (accepted)
            {
                if (_acceptLogged.Add(id))
                    ModLog.Info(MarkerSessionAccept + "peer=" + id);
            }
            else if (_acceptLogged.Add(id))
            {
                ModLog.Warn(MarkerSessionRefuse + "peer=" + id +
                            " (AcceptP2PSessionWithUser ha risposto false)");
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (P2PSessionRequest_t)");
        }
    }

    /// <summary>
    /// Registers Callback&lt;P2PSessionRequest_t&gt; once, only when Steam is ready. Tick retries
    /// per frame, gated by REGISTER_RETRY_S and a one-shot failure log so a persistent failure
    /// does not become sixty lines per second.
    /// </summary>
    private static bool EnsureRegistered()
    {
        if (_registered)
            return true;

        if (!SteamLobbyImpl.Available)
            return false;

        float now = Time.realtimeSinceStartup;
        if (now < _nextRegisterTry)
            return false;

        try
        {
            if (!SteamAPI.IsSteamRunning())
                return false;

            _cbSessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            _registered = true;

            if (!_registerLogged)
            {
                _registerLogged = true;
                ModLog.Info(MarkerCallback + "P2PSessionRequest_t (P2P session acceptance)");
            }

            return true;
        }
        catch (Exception e)
        {
            _registered = false;
            _nextRegisterTry = now + REGISTER_RETRY_S;

            if (!_registerFailed)
            {
                _registerFailed = true;
                ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message +
                             " (Callback<P2PSessionRequest_t>.Create) - retried every " +
                             REGISTER_RETRY_S + " s, the log does not repeat");
            }

            return false;
        }
    }

    /// <summary>
    /// Remote session IP as a.b.c.d. m_nRemoteIP is documented in host byte order, so octets are
    /// read from the high bits; if that yields 0.0.0.x, the reverse is tried so an address read
    /// the other way round does not look broken.
    /// </summary>
    private static string FormatIp(uint ip)
    {
        byte a = (byte)(ip >> 24);
        byte b = (byte)(ip >> 16);
        byte c = (byte)(ip >> 8);
        byte d = (byte)ip;

        if (a == 0 && b == 0 && c == 0)
        {
            byte a2 = (byte)ip;
            byte b2 = (byte)(ip >> 8);
            byte c2 = (byte)(ip >> 16);
            byte d2 = (byte)(ip >> 24);
            if (a2 != 0)
                return a2 + "." + b2 + "." + c2 + "." + d2;
        }

        return a + "." + b + "." + c + "." + d;
    }
}