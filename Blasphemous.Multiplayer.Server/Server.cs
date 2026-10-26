using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Server.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// Server engine. Runs as standalone (net8.0) and inside the client (net35, "Host session").
/// Wired through <see cref="IServerTransport"/> so the physical wire (TCP, composite, future P2P)
/// is injected. <see cref="Stop"/> and <see cref="_listening"/> exist because the in-game use
/// cannot rely on <c>Environment.Exit</c> or a fresh process per session.
/// </summary>
public class Server
{
    private readonly int _maxPlayers;
    private readonly string _password;

    /// <summary>Wire abstraction; never null. Non-null does not imply listening: see <see cref="_listening"/>.</summary>
    private readonly IServerTransport _transport;

    /// <summary>True between a successful Start and the following Stop; a failed Start does not set it.</summary>
    private bool _listening;

    /// <summary>Connected players keyed by <see cref="PeerId"/>; FromIp conversion happens only in the transport.</summary>
    private Dictionary<PeerId, PlayerInfo> connectedPlayers;

    /// <summary>Current host or null. <c>peer == _hostPeer</c> uses lifted equality; null means "no host".</summary>
    private PeerId? _hostPeer;

    private int _port;

    /// <summary>Transport is injected by whoever starts the server (Core.Main, HostSession).</summary>
    public Server(int maxPlayers, string password, IServerTransport transport)
    {
        _maxPlayers = maxPlayers;
        _password = password;

        if (transport == null)
            throw new ArgumentNullException(nameof(transport));

        _transport = transport;

        // Subscribed once: transport lifetime equals server lifetime, so Stop/Start does not duplicate handlers.
        _transport.PeerConnected += clientConnected;
        _transport.PeerDisconnected += clientDisconnected;
        _transport.FrameReceived += Receive;
    }

    /// <summary>Port this server is listening on (0 if never started).</summary>
    public int Port
    {
        get { return _port; }
    }

#if COOP_HOST
    /// <summary>
    /// Asks the transport which sockets actually exist; <see cref="IsRunning"/> only says the
    /// server believes it is up. Used by the <c>[Host] ascolto su=...</c> marker.
    /// </summary>
    public string ListeningAddresses()
    {
        if (!_listening)
            return "(stopped)";

        try
        {
            return _transport.ListeningAddresses();
        }
        catch (Exception e)
        {
            return "(unreadable: " + e.GetType().Name + ")";
        }
    }
#endif

    /// <summary>Connected players keyed by PeerId. Caller uses .Count.</summary>
    public Dictionary<PeerId, PlayerInfo> getPlayers()
    {
        return connectedPlayers;
    }

    public bool Start(int port)
    {
        try
        {
            if (!_transport.Start(port))
                return false;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Belt: IServerTransport.Start must not throw, but the server behaves as if it did.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        _listening = true;
        _port = port;
        connectedPlayers = new Dictionary<PeerId, PlayerInfo>();
        _hostPeer = null;

        // New session starts from scratch: without this, Stop/Start in-game would carry over team progress.
        Core.removeUnusedGameData(connectedPlayers);
        return true;
    }

    /// <summary>
    /// Stops the server and returns the object to the "never started" state so it can start
    /// again. Never throws: called from UI and dispose.
    /// </summary>
    public void Stop()
    {
        // Detach "listening" before stopping the wire, so no Send goes out during teardown.
        bool wasListening = _listening;
        _listening = false;
        _port = 0;

        if (wasListening)
        {
            try
            {
                _transport.Stop();
            }
            catch (Exception e)
            {
                Logger.Error("Couldn't stop the server: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Not null: disconnect callbacks may still fire while sockets are closing.
        connectedPlayers = new Dictionary<PeerId, PlayerInfo>();
        _hostPeer = null;
    }

    /// <summary>Packs <c>[ushort len][byte type][payload]</c> and hands it to the transport unchanged.</summary>
    private void Send(PeerId peer, byte[] data, NetworkType dataType)
    {
        // !_listening means "stopped" - same semantics as the previous null-server check.
        if (!_listening)
            return;

        if (data != null && data.Length > 0)
        {
            List<byte> list = new List<byte>(BitConverter.GetBytes((ushort)data.Length));
            list.Add((byte)dataType);
            list.AddRange(data);

            try
            {
                _transport.Send(peer, list.ToArray());
            }
            catch (Exception)
            {
                Logger.Error("Couldn't send data to " + peer.ToString() + "!");
            }
        }
    }

    private void Receive(PeerId peer, byte[] received)
    {
        int startIdx = 0;
        while (startIdx < received.Length - 3)
        {
            ushort length = BitConverter.ToUInt16(received, startIdx);
            NetworkType type = (NetworkType)received[startIdx + 2];

            // net35 has no System.Range: explicit copy of the same bytes. Also guards a truncated
            // frame, which would otherwise throw inside the reader thread; no protocol change.
            if (startIdx + 3 + length > received.Length)
            {
                Logger.Error("Received data was formatted incorrectly");
                return;
            }

            byte[] messageData = new byte[length];
            Array.Copy(received, startIdx + 3, messageData, 0, length);

            processDataReceived(type, messageData);
            startIdx += 3 + length;
        }
        if (startIdx != received.Length)
            Logger.Error("Received data was formatted incorrectly");

        void processDataReceived(NetworkType type, byte[] data)
        {
            // Payload-length guard: reads below would throw on short payloads inside the reader
            // thread. Short packets are dropped and reported; other frames keep flowing.
            int required = MinimumPayloadLength(type);
            if (required > 0 && data.Length < required)
            {
                reportShortPayload(type, required, data.Length);
                return;
            }

            switch (type)
            {
                case NetworkType.Position:      ReceivePosition(peer, data); break;
                case NetworkType.Animation:     ReceiveAnimation(peer, data); break;
                case NetworkType.Direction:     ReceiveDirection(peer, data); break;
                case NetworkType.EnterScene:    receivePlayerEnterScene(peer, data); break;
                case NetworkType.LeaveScene:    receivePlayerLeaveScene(peer, data); break;
                case NetworkType.Skin:          receivePlayerSkin(peer, data); break;
                case NetworkType.Team:          receivePlayerTeam(peer, data); break;
                case NetworkType.Intro:         receivePlayerIntro(peer, data); break;
                case NetworkType.Progress:      receivePlayerProgress(peer, data); break;
                case NetworkType.Attack:             receivePlayerAttack(peer, data); break;
                case NetworkType.Effect:             receivePlayerEffect(peer, data); break;
                case NetworkType.Ping:               ReceivePing(peer, data); break;
                case NetworkType.EnemyUpdate:        receiveEnemyUpdate(peer, data); break;
                case NetworkType.EnemyDamageRequest: receiveEnemyDamageRequest(peer, data); break;
                case NetworkType.EnemyDeath:         receiveEnemyDeath(peer, data); break;
                case NetworkType.EnemyDeathGlobal:   receiveEnemyDeathGlobal(peer, data); break;
                case NetworkType.EnemyOwnership:     receiveEnemyOwnership(peer, data); break;
                default:
                    Logger.Error($"Data type '{type}' is not valid"); break;
            }
        }
    }

    // Minimum payload bytes each Receive* actually reads. 0 for opaque relays (forwarded verbatim);
    // Intro has a variable shape and uses HasBytes/TryReadString instead.
    private static int MinimumPayloadLength(NetworkType type)
    {
        switch (type)
        {
            case NetworkType.Position:  return 8;
            case NetworkType.Animation: return 1;
            case NetworkType.Direction: return 1;
            case NetworkType.Team:      return 1;
            case NetworkType.Progress:  return 2;
            case NetworkType.Ping:      return 6;
            default:                    return 0;
        }
    }

    // Rate limit for short-payload reports: first per type, then when the shape changes (bounded),
    // then every N. Arrays indexed by type because Receive may run on multiple reader threads.
    private readonly int[] _shortPayloads = new int[256];

    private readonly int[] _shortPayloadLastExpected = new int[256];

    private int _shortPayloadsTotal;

    private const int SHORT_PAYLOAD_LOG_EVERY = 100;

    private const int SHORT_PAYLOAD_NEW_SHAPES = 8;

    private void reportShortPayload(NetworkType type, int expected, int have)
    {
        // NetworkType is a byte enum: 0..255, same as the array slots.
        int slot = (byte)type;
        int count = ++_shortPayloads[slot];
        int total = ++_shortPayloadsTotal;

        // 0 = never written; expected is always >= 1 when we reach here.
        bool newShape = _shortPayloadLastExpected[slot] != expected && count <= SHORT_PAYLOAD_NEW_SHAPES;
        if (!newShape && count % SHORT_PAYLOAD_LOG_EVERY != 0)
            return;

        _shortPayloadLastExpected[slot] = expected;

        // The marker is assembled at the START of the literal so marker-check finds it byte-for-byte
        // (tools\marker-check.ps1 looks for "[Server] payload corto"): do not translate this word.
        Logger.Warn("[Server] payload corto: tipo=" + type
            + " attesi=" + expected
            + " ho=" + have
            + (count == 1
                ? ""
                : " (drops of this type: " + count + ", total: " + total + ")"));
    }

    // Returns false and reports when there are not enough bytes; caller returns without throwing.
    private bool HasBytes(byte[] data, int start, int count, NetworkType type)
    {
        if (data != null && start >= 0 && count >= 0 && start + count <= data.Length)
            return true;

        reportShortPayload(type, start + count, data == null ? 0 : data.Length);
        return false;
    }

    // Reads [byte length][length bytes] after checking bounds; declared length never exceeds payload.
    private bool TryReadString(byte[] data, ref int idx, NetworkType type, out string value)
    {
        value = string.Empty;

        if (!HasBytes(data, idx, 1, type))
            return false;

        int length = data[idx];
        if (!HasBytes(data, idx + 1, length, type))
            return false;

        value = Encoding.UTF8.GetString(data, idx + 1, length);
        idx += length + 1;
        return true;
    }

    // Progress guards: type indexes TeamInfo.progressSets (NUMBER_OF_PROGRESS_TYPES), and id
    // length is bounded in wire bytes so the peer cannot grow progressSets without limit.
    private const byte MAX_PROGRESS_TYPE = (byte)(TeamInfo.NUMBER_OF_PROGRESS_TYPES - 1);

    private const int MAX_PROGRESS_ID_LENGTH = 256;

    // Rate limit for rejected progress reports, same reasoning as short payloads.
    private readonly int[] _rejectedProgress = new int[2];

    private int _rejectedProgressTotal;

    private const int REJECTED_PROGRESS_LOG_EVERY = 100;

    private const int PROGRESS_REJECT_TYPE = 0;

    private const int PROGRESS_REJECT_ID = 1;

    private void reportRejectedProgress(int reason, string detail)
    {
        int count = ++_rejectedProgress[reason];
        int total = ++_rejectedProgressTotal;

        if (count != 1 && count % REJECTED_PROGRESS_LOG_EVERY != 0)
            return;

        Logger.Warn("[Server] progress rejected: " + detail
            + (count == 1
                ? ""
                : " (drops of this reason: " + count + ", total: " + total + ")"));
    }

    /// <summary>Peer connected; <c>peer</c> is already a PeerId from the transport.</summary>
    private void clientConnected(PeerId peer)
    {
        Logger.Info("Client connected at " + peer.ToString());
    }

    /// <summary>Peer disconnected: notify others, drop it, and reassign the host if needed.</summary>
    private void clientDisconnected(PeerId peer)
    {
        Logger.Info("Client disconnected at " + peer.ToString());

        // Ignore peers that were rejected before entering the player table.
        if (!connectedPlayers.ContainsKey(peer))
            return;

        sendPlayerConnection(peer, false);
        connectedPlayers.Remove(peer);
        Core.removeUnusedGameData(connectedPlayers);

        if (peer == _hostPeer)
        {
            // Next player in dictionary order becomes host; null if none remain.
            PeerId? next = null;
            foreach (PeerId remaining in connectedPlayers.Keys)
            {
                next = remaining;
                break;
            }
            _hostPeer = next;

            if (_hostPeer.HasValue)
            {
                Logger.Info($"Host reassigned to: {connectedPlayers[_hostPeer.Value].name} ({_hostPeer.Value})");

                // Broadcast, not one-shot: a lost assign used to leave a third player permanently
                // believing the old host. sendHostAssign already decides per recipient.
                foreach (PeerId other in connectedPlayers.Keys)
                {
                    sendHostAssign(other);
                }
            }
            else
                Logger.Info("Host disconnected, no other players connected.");
        }
    }

    private PlayerInfo getCurrentPlayer(PeerId peer)
    {
        if (connectedPlayers.ContainsKey(peer))
            return connectedPlayers[peer];

        Logger.Warn("Data for " + peer.ToString() + " has not been created yet!");
        return new PlayerInfo(string.Empty, 1);
    }

    private List<byte> addPlayerNameToData(string name)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        List<byte> data = new List<byte>();
        data.Add((byte)nameBytes.Length);
        data.AddRange(nameBytes);
        return data;
    }

    // Position

    private void SendPosition(PeerId peer)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other && current.isInSameScene(connectedPlayers[other]))
            {
                Send(other, GetPositionPacket(current), NetworkType.Position);
            }
        }
    }

    private void ReceivePosition(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        current.xPos = BitConverter.ToSingle(data, 0);
        current.yPos = BitConverter.ToSingle(data, 4);

        SendPosition(peer);
    }

    private byte[] GetPositionPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(BitConverter.GetBytes(player.xPos));
        bytes.AddRange(BitConverter.GetBytes(player.yPos));
        return bytes.ToArray();
    }

    // Animation

    private void SendAnimation(PeerId peer)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other && current.isInSameScene(connectedPlayers[other]))
            {
                Send(other, GetAnimationPacket(current), NetworkType.Animation);
            }
        }
    }

    private void ReceiveAnimation(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        current.animation = data[0];

        SendAnimation(peer);
    }

    private byte[] GetAnimationPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.Add(player.animation);
        return bytes.ToArray();
    }

    // Direction

    private void SendDirection(PeerId peer)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other && current.isInSameScene(connectedPlayers[other]))
            {
                Send(other, GetDirectionPacket(current), NetworkType.Direction);
            }
        }
    }

    private void ReceiveDirection(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);

        // net35 has no BitConverter.ToBoolean(byte[]); equivalent: single byte, non-zero means true.
        current.facingDirection = data[0] != 0;

        SendDirection(peer);
    }

    private byte[] GetDirectionPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(BitConverter.GetBytes(player.facingDirection));
        return bytes.ToArray();
    }

    // Enter scene

    private void sendPlayerEnterScene(PeerId peer)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                Send(other, getScenePacket(current), NetworkType.EnterScene);

                // Same scene: cross-seed position/animation/direction in both directions.
                if (current.isInSameScene(connectedPlayers[other]))
                {
                    Send(other, GetPositionPacket(current), NetworkType.Position);
                    Send(other, GetAnimationPacket(current), NetworkType.Animation);
                    Send(other, GetDirectionPacket(current), NetworkType.Direction);

                    Send(peer, GetPositionPacket(connectedPlayers[other]), NetworkType.Position);
                    Send(peer, GetAnimationPacket(connectedPlayers[other]), NetworkType.Animation);
                    Send(peer, GetDirectionPacket(connectedPlayers[other]), NetworkType.Direction);
                }
            }
        }
    }

    private void receivePlayerEnterScene(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        current.sceneName = Encoding.UTF8.GetString(data);

        sendPlayerEnterScene(peer);
    }

    private byte[] getScenePacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(Encoding.UTF8.GetBytes(player.sceneName));
        return bytes.ToArray();
    }

    // Leave scene

    private void sendPlayerLeaveScene(PeerId peer)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        current.xPos = 0;
        current.yPos = 0;
        current.animation = 0;
        current.facingDirection = false;

        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                Send(other, Encoding.UTF8.GetBytes(current.name), NetworkType.LeaveScene);
            }
        }
    }

    private void receivePlayerLeaveScene(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);

        sendPlayerLeaveScene(peer);
        current.sceneName = "";
    }

    // Skin

    private void sendPlayerSkin(PeerId peer)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                Send(other, getSkinPacket(current), NetworkType.Skin);
            }
        }
    }

    private void receivePlayerSkin(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        current.skin = data;

        sendPlayerSkin(peer);
    }

    private byte[] getSkinPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(player.skin);
        return bytes.ToArray();
    }

    // Team

    // Valid team range. Values outside are normalized (see sanitizeTeam) rather than rejected,
    // so no new RefusalType is introduced and no peer can create uncounted team data.
    private const byte MIN_TEAM = 1;
    private const byte MAX_TEAM = 10;

    /// <summary>Clamps out-of-range teams to MIN_TEAM and logs; rejecting would require a protocol change.</summary>
    private byte sanitizeTeam(byte team)
    {
        if (team >= MIN_TEAM && team <= MAX_TEAM)
            return team;

        Logger.Warn("[Server] team out of range: valore=" + team
            + " (valid " + MIN_TEAM + ".." + MAX_TEAM + ") -> normalized to " + MIN_TEAM);
        return MIN_TEAM;
    }

    private void sendPlayerTeam(PeerId peer)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                Send(other, getTeamPacket(current), NetworkType.Team);
            }
        }

        Logger.Info("Sending all server data to " + peer.ToString());
        for (byte i = 0; i < TeamInfo.NUMBER_OF_PROGRESS_TYPES; i++)
        {
            Dictionary<string, byte> progressSet = Core.getTeamData(current.team).GetTeamProgressSet(i);
            foreach (string id in progressSet.Keys)
            {
                byte value = progressSet[id];
                if (i == 6 && id == "FLASK")
                {
                    Logger.Info("Send all server flask: " + value);
                    value -= Core.getTeamData(current.team).GetTeamProgressValue(6, "FLASKHEALTH");
                    Logger.Info("Send all send flask: " + value);
                }
                Send(peer, getProgressPacket("*", i, value, id), NetworkType.Progress);
            }
        }
    }

    private void receivePlayerTeam(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        // Team validated here as well: the stored value becomes a key of Core.getTeamData.
        current.team = sanitizeTeam(data[0]);

        sendPlayerTeam(peer);
        Core.removeUnusedGameData(connectedPlayers);
    }

    private byte[] getTeamPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.Add(player.team);
        return bytes.ToArray();
    }

    // Connection

    private void sendPlayerConnection(PeerId peer, bool connected)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                Send(other, getConnectionPacket(current, connected), NetworkType.Connection);
            }
        }
    }

    private byte[] getConnectionPacket(PlayerInfo player, bool connected)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(BitConverter.GetBytes(connected));
        return bytes.ToArray();
    }

    // Intro

    private void sendPlayerIntro(PeerId peer, RefusalType response)
    {
        Send(peer, getIntroPacket((byte)response), NetworkType.Intro);

        // Only accepted connections receive the initial state dump.
        if (response > 0)
            return;

        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                Send(peer, getConnectionPacket(connectedPlayers[other], true), NetworkType.Connection);
                Send(peer, getSkinPacket(connectedPlayers[other]), NetworkType.Skin);
                Send(peer, getTeamPacket(connectedPlayers[other]), NetworkType.Team);
                if (connectedPlayers[other].sceneName != "")
                {
                    Send(peer, getScenePacket(connectedPlayers[other]), NetworkType.EnterScene);
                    if (current.isInSameScene(connectedPlayers[other]))
                    {
                        Send(peer, GetPositionPacket(connectedPlayers[other]), NetworkType.Position);
                        Send(peer, GetAnimationPacket(connectedPlayers[other]), NetworkType.Animation);
                        Send(peer, GetDirectionPacket(connectedPlayers[other]), NetworkType.Direction);
                    }
                }
            }
        }
    }

    private void receivePlayerIntro(PeerId peer, byte[] data)
    {
        int idx = 0;

        if (connectedPlayers.Count >= _maxPlayers)
        {
            Logger.Warn("Player connection rejected: Player limit reached");
            sendPlayerIntro(peer, RefusalType.PlayerLimit);
            return;
        }

        if (connectedPlayers.ContainsKey(peer))
        {
            Logger.Warn("Player connection rejected: Duplicate ip address");
            sendPlayerIntro(peer, RefusalType.DuplicateIp);
            return;
        }

        // Variable-length payload: guarded read-by-read via HasBytes/TryReadString.
        if (!HasBytes(data, idx, 1, NetworkType.Intro))
            return;

        byte protocol = data[idx++];

        if (protocol != Protocol.PROTOCOL_VERSION)
        {
            Logger.Warn("Player connection rejected: Protocol version doesn't match");
            sendPlayerIntro(peer, RefusalType.Protocol);
            return;
        }

        // Scene string is informational: read to advance, not stored.
        if (!TryReadString(data, ref idx, NetworkType.Intro, out _))
            return;

        if (!TryReadString(data, ref idx, NetworkType.Intro, out string name))
            return;

        foreach (PlayerInfo player in connectedPlayers.Values)
        {
            if (player.name == name)
            {
                Logger.Warn("Player connection rejected: Duplicate name");
                sendPlayerIntro(peer, RefusalType.DuplicateName);
                return;
            }
        }

        if (!TryReadString(data, ref idx, NetworkType.Intro, out string passwordRaw))
            return;

        // Empty password becomes null so IsNullOrEmpty distinguishes "no password configured"
        // from "wrong password".
        string password = passwordRaw.Length == 0 ? null : passwordRaw;

        if (!string.IsNullOrEmpty(_password) && (password == null || password != _password))
        {
            Logger.Warn("Player connection rejected: Incorrect password");
            sendPlayerIntro(peer, RefusalType.Password);
            return;
        }

        if (!HasBytes(data, idx, 1, NetworkType.Intro))
            return;

        // Team validated before entering state (see sanitizeTeam).
        byte team = sanitizeTeam(data[idx]);

        Logger.Info("Player connection accepted");
        PlayerInfo newPlayer = new PlayerInfo(name, team);
        connectedPlayers.Add(peer, newPlayer);
        if (!_hostPeer.HasValue)
        {
            _hostPeer = peer;
            Logger.Info($"Designated host: {newPlayer.name} ({peer})");
        }
        sendHostAssign(peer);
        sendPlayerConnection(peer, true);
        sendPlayerIntro(peer, RefusalType.Accepted);
    }

    private byte[] getIntroPacket(byte response)
    {
        return new byte[] { response };
    }

    // Progress

    private void sendPlayerProgress(PeerId peer, byte type, byte value, string id)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other && current.team == getCurrentPlayer(other).team)
            {
                Send(other, getProgressPacket(current.name, type, value, id), NetworkType.Progress);
            }
        }
    }

    private void receivePlayerProgress(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        byte progressType = data[0];
        byte progressValue = data[1];

        // Type indexes TeamInfo.progressSets: must be checked before indexing.
        if (progressType > MAX_PROGRESS_TYPE)
        {
            reportRejectedProgress(PROGRESS_REJECT_TYPE,
                "tipo=" + progressType + " (valid 0.." + MAX_PROGRESS_TYPE + ")");
            return;
        }

        // Bounded by wire bytes (not UTF-16 length) before decoding.
        int progressIdLength = data.Length - 2;
        if (progressIdLength > MAX_PROGRESS_ID_LENGTH)
        {
            reportRejectedProgress(PROGRESS_REJECT_ID,
                "id length " + progressIdLength + " bytes (max " + MAX_PROGRESS_ID_LENGTH
                + ") tipo=" + progressType);
            return;
        }

        string progressId = Encoding.UTF8.GetString(data, 2, progressIdLength);

        if (!Core.getTeamData(current.team).AddTeamProgress(progressId, progressType, progressValue))
        {
            Logger.ProgressBad($"Received duplicated or inferior progress from {current.name}: {progressId}, Type {progressType}, Value {progressValue}");
            return;
        }

        if (progressType >= 0 && progressType <= 5)
        {
            Logger.ProgressGood($"{(progressValue == 0 ? "Received new" : "Lost an")} item from {current.name}: {progressId}");
        }
        else if (progressType == 6)
        {
            Logger.ProgressGood($"Received new stat upgrade from {current.name}: {progressId} level {progressValue + 1}");
        }
        else if (progressType == 7)
        {
            Logger.ProgressGood($"Received new skill from {current.name}: {progressId}");
        }
        else if (progressType == 8)
        {
            Logger.ProgressGood($"Received new map cell from {current.name}: {progressId}");
        }
        else if (progressType == 9)
        {
            Logger.ProgressGood($"Received new flag from {current.name}: {progressId}");
        }
        else if (progressType == 10)
        {
            Logger.ProgressGood($"Received new pers. object from {current.name}: {progressId}");
        }
        else if (progressType == 11)
        {
            Logger.ProgressGood($"Received new teleport location from {current.name}: {progressId}");
        }
        else if (progressType == 12)
        {
            Logger.ProgressGood($"Received new tear donation from {current.name}: {progressValue}");
        }
        else if (progressType == 13)
        {
            Logger.ProgressGood($"Received new miriam status from {current.name}: {progressId}");
        }

        // Stat upgrades interact with flask/flaskhealth; the value sent to peers is the corrected one.
        if (progressType == 6)
        {
            if (progressId == "FLASK")
            {
                Logger.Info("Received flask level: " + progressValue);
                byte flaskHealthUpgrades = Core.getTeamData(current.team).GetTeamProgressValue(6, "FLASKHEALTH");
                progressValue -= flaskHealthUpgrades;
                Logger.Info("Flask level sent out: " + progressValue);
            }
            else if (progressId == "FLASKHEALTH")
            {
                byte flaskUpgrades = Core.getTeamData(current.team).GetTeamProgressValue(6, "FLASK");
                Logger.Info("Flask level stored: " + flaskUpgrades);
                Logger.Info("Flask level sent: " + (byte)(flaskUpgrades - progressValue));
                sendPlayerProgress(peer, 6, (byte)(flaskUpgrades - progressValue), "FLASK");
            }
        }

        sendPlayerProgress(peer, progressType, progressValue, progressId);
    }

    private byte[] getProgressPacket(string nameOrStar, byte type, byte value, string id)
    {
        List<byte> bytes = addPlayerNameToData(nameOrStar);
        bytes.Add(type);
        bytes.Add(value);
        bytes.AddRange(Encoding.UTF8.GetBytes(id));
        return bytes.ToArray();
    }

    // Attack

    private void sendPlayerAttack(PeerId peer, byte[] attackData)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other && current.isInSameScene(connectedPlayers[other]))
            {
                Send(other, getAttackPacket(current, attackData), NetworkType.Attack);
            }
        }
    }

    private void receivePlayerAttack(PeerId peer, byte[] data)
    {
        sendPlayerAttack(peer, data);
    }

    private byte[] getAttackPacket(PlayerInfo player, byte[] attackData)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(attackData);
        return bytes.ToArray();
    }

    // Effect

    private void sendPlayerEffect(PeerId peer, byte[] effectData)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other && current.isInSameScene(connectedPlayers[other]))
            {
                Send(other, getEffectPacket(current, effectData), NetworkType.Effect);
            }
        }
    }

    private void receivePlayerEffect(PeerId peer, byte[] data)
    {
        // Relay the client payload WHOLE and PREFIXED with the name: the receiver reads the name
        // first, so an unprefixed relay would be unreadable.
        sendPlayerEffect(peer, data);
    }

    private byte[] getEffectPacket(PlayerInfo player, byte[] effectData)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(effectData);
        return bytes.ToArray();
    }

    // Ping

    private void SendPing(PeerId peer, float time)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(time));

        foreach (var player in getPlayers())
        {
            if (player.Key == peer)
                continue;

            string name = player.Value.name;
            ushort ping = player.Value.ping;

            bytes.Add((byte)name.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(name));
            bytes.AddRange(BitConverter.GetBytes(ping));
        }

        Send(peer, bytes.ToArray(), NetworkType.Ping);
    }

    private void ReceivePing(PeerId peer, byte[] data)
    {
        float time = BitConverter.ToSingle(data, 0);
        ushort ping = BitConverter.ToUInt16(data, 4);

        PlayerInfo current = getCurrentPlayer(peer);
        current.ping = ping;

        SendPing(peer, time);
    }

    // Enemies

    // Relays payload to peers sharing the sender's scene; enemy ids are strings so the server
    // does not interpret them.
    private void relayToScene(PeerId peer, byte[] data, NetworkType type)
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other && current.isInSameScene(connectedPlayers[other]))
            {
                Send(other, data, type);
            }
        }
    }

    private void receiveEnemyUpdate(PeerId peer, byte[] data)
    {
        // No host-only filter: state is per-enemy, and each machine owns some enemies; the server
        // does not interpret the ownership flag, letting each client decide.
        relayToScene(peer, data, NetworkType.EnemyUpdate);
    }

    private void receiveEnemyDeath(PeerId peer, byte[] data)
    {
        // Same reasoning as EnemyUpdate: whoever simulated the enemy knows it died.
        relayToScene(peer, data, NetworkType.EnemyDeath);
    }

    private void receiveEnemyDeathGlobal(PeerId peer, byte[] data)
    {
        // No scene filter: the player who needs this most was NOT in the scene when the enemy
        // died, and their copy would otherwise re-spawn it alive.
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                Send(other, data, NetworkType.EnemyDeathGlobal);
            }
        }
    }

    private void receiveEnemyOwnership(PeerId peer, byte[] data)
    {
        // Only the host issues ownership tokens; a peer must not grant itself an enemy. Relayed
        // to the scene because ownership only matters to players sharing it.
        if (peer != _hostPeer)
            return;

        relayToScene(peer, data, NetworkType.EnemyOwnership);
    }

    private void receiveEnemyDamageRequest(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);

        // From the host: relay to peers sharing the scene. From a peer: forward to the host only.
        if (peer == _hostPeer)
        {
            foreach (PeerId other in connectedPlayers.Keys)
            {
                if (peer != other && current.isInSameScene(connectedPlayers[other]))
                {
                    Send(other, data, NetworkType.EnemyDamageRequest);
                }
            }
        }
        else
        {
            if (_hostPeer.HasValue && connectedPlayers.ContainsKey(_hostPeer.Value))
            {
                Send(_hostPeer.Value, data, NetworkType.EnemyDamageRequest);
            }
        }
    }

    // Authority

    private void sendHostAssign(PeerId peer)
    {
        bool isHost = peer == _hostPeer;
        Send(peer, new byte[] { (byte)(isHost ? 1 : 0) }, NetworkType.HostAssign);
    }
}