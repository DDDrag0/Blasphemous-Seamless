using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network
{
    public class NetworkManager
    {
        private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;

        /// <summary>The wire, behind the transport seam. This file never names a socket.</summary>
        private ICoopTransport _transport;
        private string _serverIp = string.Empty;

        /// <summary>
        /// True from dial start until ANY outcome (Intro accepted, refusal, timeout, exception).
        /// OnDisconnect uses it to close a pending attempt with RefusalType.Connection - without
        /// it, a P2P timeout leaves the connection screen on "Connecting..." forever.
        /// </summary>
        private bool _attempting;

        /// <summary>
        /// True while inside Disconnect(). The P2P transport emits Disconnected from its own
        /// Disconnect call; without this flag OnTransportEnded would call OnDisconnect twice.
        /// </summary>
        private bool _ending;

        /// <summary>
        /// Same expression as before the seam: status Connected AND transport not null.
        /// Deliberately not _transport.IsConnected - that would observe socket loss earlier than
        /// the first failed write, a behavior change.
        /// </summary>
        public bool IsConnected => _connectionStatus == ConnectionStatus.Connected && _transport != null;
        public string ServerIP => _serverIp;

        private readonly List<byte> sendingQueue = new ();
        private readonly List<byte> receivingQueue = new ();
        private static readonly object datalock = new object();

        /// <summary>
        /// TransportClass of each queued message (one entry per message). Used by SendQueue to
        /// group consecutive messages of the same class into one send: P2P gets one unreliable
        /// packet per position and one reliable per death; TCP gets the same bytes in the same
        /// order (ignores the class).
        /// </summary>
        private readonly List<TransportClass> sendingClasses = new ();

        /// <summary>Byte length of each queued message, length header included.</summary>
        private readonly List<int> sendingSizes = new ();

        // Connection

        public bool Connect(string server, int port, string room, string player, string password, byte team)
        {
            if (_connectionStatus != ConnectionStatus.Disconnected)
                return false;

            _attempting = true;

            try
            {
                // Same order as before: Connect, then subscribe. NoDelay moved inside
                // TcpTransport.Connect; no byte on the wire can notice, since writes start later
                // from SendQueue.
                _transport = CreateTransport(server);
                _transport.Connect(server, port);
                _transport.DataReceived += ReceiveMessage;

                // Only P2P needs the wire's Disconnected event: TCP loss is discovered on the first
                // failed write; a P2P link can die silently on timeout. Subscribed only for the
                // p2p token - on TCP, Disconnect also emits it, and subscribing would double
                // OnDisconnect.
                if (SteamP2PTransport.IsRequested(server))
                    _transport.Disconnected += OnTransportEnded;
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Same catch: the transport does not swallow SocketException, so this is reachable.
                ModLog.Info("Failed to establish connection");
                _attempting = false;
                OnConnect?.Invoke(false, RefusalType.Connection);
                return false;
            }

            OnConnectOld(server, room, player, password, team);
            return true;
        }

        public void Disconnect()
        {
            // _ending prevents OnTransportEnded from calling OnDisconnect a second time (the P2P
            // transport emits Disconnected from inside this call).
            //
            // Null transport is a valid case: CancelConnect on the connection screen calls this as
            // its safety net after a failed connect.
            ICoopTransport transport = _transport;

            if (transport == null && _connectionStatus == ConnectionStatus.Disconnected)
                return;

            _ending = true;
            try
            {
                if (transport != null)
                    transport.Disconnect();
            }
            finally
            {
                _ending = false;
            }

            OnDisconnect();
        }

        /// <summary>
        /// The transport declared the link ended on its own (P2P timeout). Goes through OnDisconnect.
        /// Subscribed only for P2P; on TCP this is never armed.
        /// </summary>
        private void OnTransportEnded()
        {
            if (_ending)
                return;

            if (_connectionStatus == ConnectionStatus.Disconnected)
                return;

            ModLog.Info("[Net] p2p: transport declared link ended: disconnecting");
            OnDisconnect();
        }

        private void OnConnectOld(string ipAddress, string room, string player, string password, byte team)
        {
            ModLog.Info("Established basic connection");

            _connectionStatus = ConnectionStatus.Attempting;
            _serverIp = ipAddress;
            SendIntro(room, player, password, team);

            Main.Multiplayer.SetPlayerData(player, team);
        }

        private void OnDisconnect()
        {
            // The negative outcome must reach whoever pressed Connect: on P2P failure or TCP drop,
            // OnConnect was never called and the connection screen stayed on "Connecting..." with
            // no buttons. wasAttempting is read BEFORE clearing the field (which is cleared
            // always); the invocation is after restoring the state, so the listener finds
            // IsConnected == false and the screen returns to DisplayStart.
            bool wasAttempting = _attempting;
            _attempting = false;

            _connectionStatus = ConnectionStatus.Disconnected;
            _serverIp = string.Empty;
            _transport = null;
            sendingQueue.Clear();
            sendingClasses.Clear();
            sendingSizes.Clear();
            receivingQueue.Clear();

            ModLog.Info("Disconnected from server");
            Main.Multiplayer.OnDisconnect();

            if (!wasAttempting)
                return;

            // Same outcome as "socket did not open": RefusalType.Connection, so the notification is
            // the one already localized as refcon. No new string needed.
            ModLog.Info("[Net] connection failed with no answer: the screen is available again");
            OnConnect?.Invoke(false, RefusalType.Connection);
        }

        // Server

        /// <summary>
        /// Picks the wire. TcpTransport for every address; P2P only when the "Server IP" field
        /// explicitly asks for it (p2p). Choosing P2P automatically would break the host's
        /// loopback, since the host is in a lobby too and must remain a TCP client.
        /// </summary>
        private static ICoopTransport CreateTransport(string server)
        {
            if (SteamP2PTransport.IsRequested(server))
                return new SteamP2PTransport();

            return new TcpTransport();
        }

        private void QueueMesssage(byte[] data, NetworkType type, TransportClass cls)
        {
            if (data == null || data.Length == 0 || _connectionStatus == ConnectionStatus.Disconnected) return;

            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes((ushort)data.Length));
            bytes.Add((byte)type);
            bytes.AddRange(data);

            sendingQueue.AddRange(bytes);

            // One entry per MESSAGE, not per byte: lets SendQueue group consecutive messages of
            // the same class without ever splitting a frame.
            sendingClasses.Add(cls);
            sendingSizes.Add(bytes.Count);
        }

        public void SendQueue()
        {
            if (sendingQueue.Count == 0 || _connectionStatus == ConnectionStatus.Disconnected) return;

            try
            {
                // Single class (the normal case): ONE Send, same bytes as before, same order.
                // IOException propagates on purpose - the catch below is the way to OnDisconnect.
                // Mixed classes: the queue splits into consecutive runs; on TCP that is multiple
                // Writes with the same bytes in the same order, on P2P each run takes its channel.
                SendRuns();
            }
            catch (System.IO.IOException)
            {
                OnDisconnect();
            }
            finally
            {
                sendingQueue.Clear();
                sendingClasses.Clear();
                sendingSizes.Clear();
            }
        }

        /// <summary>
        /// Sends the queue in class runs. Never reorders and never merges different classes:
        /// byte order remains enqueue order.
        /// </summary>
        private void SendRuns()
        {
            // Single class: ONE call, exactly as before.
            if (sendingClasses.Count <= 1)
            {
                TransportClass single = sendingClasses.Count == 0 ? TransportClass.Unreliable : sendingClasses[0];
                _transport.Send(sendingQueue.ToArray(), single);
                return;
            }

            int start = 0;
            int index = 0;

            while (index < sendingClasses.Count)
            {
                TransportClass cls = sendingClasses[index];
                int end = start + sendingSizes[index];
                index++;

                while (index < sendingClasses.Count && sendingClasses[index] == cls)
                {
                    end += sendingSizes[index];
                    index++;
                }

                _transport.Send(sendingQueue.GetRange(start, end - start).ToArray(), cls);
                start = end;
            }
        }

        // Data should be formatted as length length type data
        //
        // Shape adapter for ICoopTransport.DataReceived: bytes only, no interpretation or framing.
        // Same bytes and same read thread as the previous SimpleTCP callback, so taking datalock
        // here is unchanged.
        private void ReceiveMessage(byte[] message)
        {
            lock (datalock)
            {
                receivingQueue.AddRange(message);
            }
        }

        public void ProcessQueue()
        {
            lock (datalock)
            {
                if (receivingQueue.Count == 0) return;

                int startIdx = 0;
                while (startIdx < receivingQueue.Count - 3)
                {
                    ushort length = BitConverter.ToUInt16(receivingQueue.GetRange(startIdx, 2).ToArray(), 0);
                    byte type = receivingQueue[startIdx + 2];
                    byte[] messageData = new byte[length];
                    for (int i = 0; i < length; i++)
                        messageData[i] = receivingQueue[startIdx + 3 + i];

                    switch ((NetworkType)type)
                    {
                        case NetworkType.Position: ReceivePosition(messageData); break;
                        case NetworkType.Animation: ReceiveAnimation(messageData); break;
                        case NetworkType.Direction: ReceiveDirection(messageData); break;
                        case NetworkType.EnterScene: ReceiveEnterScene(messageData); break;
                        case NetworkType.LeaveScene: ReceiveLeaveScene(messageData); break;
                        case NetworkType.Skin: ReceiveSkin(messageData); break;
                        case NetworkType.Team: ReceiveTeam(messageData); break;
                        case NetworkType.Connection: ReceiveConnection(messageData); break;
                        case NetworkType.Intro: ReceiveIntro(messageData); break;
                        case NetworkType.Progress: ReceiveProgress(messageData); break;
                        case NetworkType.Attack: ReceiveAttack(messageData); break;
                        case NetworkType.Effect: ReceiveEffect(messageData); break;
                        case NetworkType.Ping: ReceivePing(messageData); break;
                        case NetworkType.HostAssign: ReceiveHostAssign(messageData); break;
                        case NetworkType.EnemyUpdate: ReceiveEnemyUpdate(messageData); break;
                        case NetworkType.EnemyDamageRequest: ReceiveEnemyDamageRequest(messageData); break;
                        case NetworkType.EnemyDeath: ReceiveEnemyDeath(messageData); break;
                        case NetworkType.EnemyDeathGlobal: ReceiveEnemyDeathGlobal(messageData); break;
                        case NetworkType.EnemyOwnership: ReceiveEnemyOwnership(messageData); break;
                    }
                    startIdx += 3 + length;
                }
                if (startIdx != receivingQueue.Count)
                    ModLog.Error("Received data was formatted incorrectly");

                receivingQueue.Clear();
            }
        }

        private int ExtractNameFromData(byte[] data, out string name)
        {
            byte nameLength = data[0];
            name = Encoding.UTF8.GetString(data, 1, nameLength);
            return nameLength + 1;
        }

        // Position

        public void SendPosition(Vector2 position)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(position.x));
            bytes.AddRange(BitConverter.GetBytes(position.y));
            // Unreliable: the next sample replaces the previous one.
            QueueMesssage(bytes.ToArray(), NetworkType.Position, TransportClass.Unreliable);
        }

        private void ReceivePosition(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            float xpos = BitConverter.ToSingle(data, startIdx);
            float ypos = BitConverter.ToSingle(data, startIdx + 4);

            Main.Multiplayer.OtherPlayerManager.ReceivePosition(playerName, new Vector2(xpos, ypos));
        }
        
        // Animation

        public void SendAnimation(byte animation)
        {
            // Reliable only for the death byte: clips overwrite each other (unreliable is fine),
            // but the death byte arrives ONCE per death and without it the peer's corpse stays
            // standing. The predicate is the sender's own (PlayerStatus.IsDeathAnimation), not a
            // rewritten table here.
            TransportClass cls = PlayerStatus.IsDeathAnimation(animation)
                ? TransportClass.Reliable
                : TransportClass.Unreliable;

            QueueMesssage(new byte[] { animation }, NetworkType.Animation, cls);
        }

        private void ReceiveAnimation(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            byte animation = data[startIdx];

            Main.Multiplayer.OtherPlayerManager.ReceiveAnimation(playerName, animation);
        }

        // Direction

        public void SendDirection(bool direction)
        {
            // Unreliable: the next flip overwrites the previous one.
            QueueMesssage(BitConverter.GetBytes(direction), NetworkType.Direction, TransportClass.Unreliable);
        }

        private void ReceiveDirection(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            bool directon = BitConverter.ToBoolean(data, startIdx);

            Main.Multiplayer.OtherPlayerManager.ReceiveDirection(playerName, directon);
        }

        // Enter scene

        public void SendEnterScene(string sceneName)
        {
            // Reliable: lost, every scene-filtered stream stays on the old scene until the next room.
            QueueMesssage(Encoding.UTF8.GetBytes(sceneName), NetworkType.EnterScene, TransportClass.Reliable);
        }

        private void ReceiveEnterScene(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            string scene = Encoding.UTF8.GetString(data, startIdx, data.Length - startIdx);

            Main.Multiplayer.OtherPlayerManager.ReceiveEnterScene(playerName, scene);
        }

        // Leave scene

        public void SendLeaveScene()
        {
            // Reliable: lost, the peer keeps an avatar in the room I left.
            QueueMesssage(new byte[] { 0 }, NetworkType.LeaveScene, TransportClass.Reliable);
        }

        private void ReceiveLeaveScene(byte[] data)
        {
            string playerName = Encoding.UTF8.GetString(data);

            Main.Multiplayer.OtherPlayerManager.ReceiveLeaveScene(playerName);
        }

        // Skin

        public void SendSkin(string skinName)
        {
            if (string.IsNullOrEmpty(skinName))
            {
                ModLog.Error("Skin name is invalid");
                skinName = "PENITENT_DEFAULT";
            }

            bool originalSkin = PlayerStatus.IsOriginalSkin(skinName);
            List<byte> bytes = new ();

            // Measurement starts before the branch: a non-original skin costs almost entirely
            // EncodeToPNG. Does not touch the branch or the payload - it is a stopwatch only.
            System.Diagnostics.Stopwatch skinTimer = SkinSendMarker.Begin();

            if (originalSkin)
            {
                bytes.Add(0);
                bytes.AddRange(Encoding.UTF8.GetBytes(skinName));
            }
            else
            {
                bytes.Add(1);
                bytes.AddRange(Core.ColorPaletteManager.GetColorPaletteById(skinName).texture.EncodeToPNG());
            }

            // Reliable and channel 2: a PNG can exceed an unreliable datagram (~1200 B); channel 2
            // is isolated so a PNG does not delay a death and vice versa.
            QueueMesssage(bytes.ToArray(), NetworkType.Skin, TransportClass.Reliable);

            // Measurement line, after the send, on the bytes already queued: reads bytes.Count and
            // IsConnected, decides nothing.
            SkinSendMarker.Report(skinTimer, skinName, originalSkin, bytes.Count, IsConnected);
        }

        private void ReceiveSkin(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);

            PlayerStatus player = Main.Multiplayer.OtherPlayerManager.FindConnectedPlayer(playerName);
            if (player == null) return;

            bool originalSkin = data[startIdx] == 0;
            if (originalSkin)
            {
                string skinName = Encoding.UTF8.GetString(data, startIdx + 1, data.Length - startIdx - 1);
                player.SetSkinTexture(skinName);
            }
            else
            {
                byte[] skinData = new byte[data.Length - startIdx - 1];
                for (int i = 0; i < skinData.Length; i++)
                {
                    skinData[i] = data[startIdx + 1 + i];
                }
                player.SetSkinTexture(skinData);
            }
        }

        // Team

        public void SendTeam(byte team)
        {
            // Reliable: sent once at connect. Lost, name/color on the map stay wrong for the session.
            QueueMesssage(new byte[] { team }, NetworkType.Team, TransportClass.Reliable);
        }

        private void ReceiveTeam(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            byte team = data[startIdx];

            Main.Multiplayer.OtherPlayerManager.ReceiveTeam(playerName, team);
        }

        // Connection

        public void SendConnection()
        {
            // A client will never send a player connection, this is only sent by the server
        }

        private void ReceiveConnection(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            bool connected = BitConverter.ToBoolean(data, startIdx);

            if (connected)
            {
                // Add this player to the list of connected players
                Main.Multiplayer.OtherPlayerManager.AddConnectedPlayer(playerName);

                // Re-announce defeated spawn points so a late joiner learns about kills that
                // already happened. Runs on the main thread (packet queue drained from OnLateUpdate).
                EnemySyncManager.AnnounceDeathStateToPeers();
            }
            else
            {
                // Remove this player from the list of connected players
                Main.Multiplayer.OtherPlayerManager.ReceiveLeaveScene(playerName);
                Main.Multiplayer.OtherPlayerManager.RemoveConnectedPlayer(playerName);
            }
            Main.Multiplayer.NotificationManager.DisplayNotification(
                $"{playerName} {Main.Multiplayer.LocalizationHandler.Localize(connected ? "join" : "leave")}");
        }

        // Intro

        public void SendIntro(string room, string player, string password, byte team)
        {
            var bytes = new List<byte>();
            // Single PROTOCOL_VERSION, in Common\Protocol.cs (DLL shared with the server).
            bytes.Add(Protocol.PROTOCOL_VERSION);

            bytes.Add((byte)room.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(room));

            bytes.Add((byte)player.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(player));

            bytes.Add((byte)password.Length);
            if (password.Length > 0)
                bytes.AddRange(Encoding.UTF8.GetBytes(password));

            bytes.Add(team);

            // Reliable: the handshake precedes everything; without it, no session and nothing
            // retries.
            QueueMesssage(bytes.ToArray(), NetworkType.Intro, TransportClass.Reliable);
        }

        private void ReceiveIntro(byte[] data)
        {
            RefusalType response = (RefusalType)data[0];
            bool success = response == RefusalType.Accepted;

            ModLog.Info($"Established full connection (Success: {success})");

            // Cleared BEFORE Disconnect(): otherwise OnDisconnect still sees the attempt in
            // progress and adds its generic Connection outcome, which would overwrite the real
            // RefusalType.
            _attempting = false;

            if (success)
            {
                // Successfully connected and can sync data now
                _connectionStatus = ConnectionStatus.Connected;
            }
            else
            {
                // Rejected from server
                Disconnect();
            }

            OnConnect?.Invoke(success, response);
        }

        // Progress

        public void SendProgress(ProgressUpdate progress)
        {
            if (!progress.ShouldSyncProgress(Main.Multiplayer.config))
                return;

            List<byte> bytes = new ();
            bytes.Add((byte)progress.Type);
            bytes.Add(progress.Value);
            bytes.AddRange(Encoding.UTF8.GetBytes(progress.Id));

            // Reliable: progress is durable and _sentAllProgress is one-shot - lost, it is not resent.
            QueueMesssage(bytes.ToArray(), NetworkType.Progress, TransportClass.Reliable);
        }

        private void ReceiveProgress(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            ProgressType progressType = (ProgressType)data[startIdx];
            byte progressValue = data[startIdx + 1];
            string progressId = Encoding.UTF8.GetString(data, startIdx + 2, data.Length - startIdx - 2);

            ProgressUpdate progress = new ProgressUpdate(progressId, progressType, progressValue);

            Main.Multiplayer.ProgressManager.ReceiveProgress(progress);
            Main.Multiplayer.ProcessRecievedStat(playerName, progress);

            if (playerName != "*")
                Main.Multiplayer.NotificationManager.DisplayProgressNotification(playerName, progress);
        }

        // Attack

        public void SendAttack(string hitPlayerName, AttackType attackType, byte damageAmount)
        {
            List<byte> bytes = new ();
            bytes.Add((byte)attackType);
            bytes.Add(damageAmount);
            bytes.AddRange(Encoding.UTF8.GetBytes(hitPlayerName));

            // Reliable: a lost PvP hit means the damage never happens on the other machine, and
            // life is not a replicated value.
            QueueMesssage(bytes.ToArray(), NetworkType.Attack, TransportClass.Reliable);
        }

        /// <summary>
        /// Sends a ready-made payload on the Attack channel for the enemy-to-player direction.
        /// Reuses this channel rather than adding a NetworkType: the server already relays it to
        /// every peer in the SAME SCENE except the sender, with the sender name prefixed. Direction
        /// is distinguished by the first payload byte (AttackType.EnemyHit). Layout documented in
        /// EnemyHitPeer.BuildPayload; nothing here interprets it.
        /// </summary>
        public void SendAttackPayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                return;

            // NetworkType.Attack is a demultiplexer: five messages on one type, two classes. The
            // class is decided by the first payload byte (the AttackType), exactly what the
            // receiver uses to route.
            TransportClass cls = IsReliableAttackPayload(payload)
                ? TransportClass.Reliable
                : TransportClass.Unreliable;

            QueueMesssage(payload, NetworkType.Attack, cls);
        }

        /// <summary>
        /// Class of an Attack channel payload, from its first byte. Default Reliable: an unknown
        /// AttackType is a PvP hit, and a lost hit is not recovered.
        /// </summary>
        private static bool IsReliableAttackPayload(byte[] payload)
        {
            switch ((AttackType)payload[0])
            {
                case AttackType.CoopEffectRelay:
                case AttackType.SyncIdList:
                    return false;

                default:
                    return true;
            }
        }

        private void ReceiveAttack(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string attackerName);
            AttackType attackType = (AttackType)data[startIdx];

            // Enemy-to-player hit: own payload shape and the PvP branch would drop it (unknown
            // AttackType or HasBlocker("ANY") during a fade). Recognized BEFORE the following read.
            if (attackType == AttackType.EnemyHit)
            {
                EnemyHitPeer.ApplyFromWire(attackerName, data, startIdx);
                return;
            }

            // Pooled effect from another machine: own payload shape; PvP branch would misinterpret.
            if (attackType == AttackType.CoopEffectRelay)
            {
                EffectRelay.ApplyFromWire(attackerName, data, startIdx);
                return;
            }

            // Id-list diagnostics: own payload shape, decides nothing.
            if (attackType == AttackType.SyncIdList)
            {
                SyncIdListMarker.ApplyFromWire(attackerName, data, startIdx);
                return;
            }

            // "Other is paused" notification: own payload shape (a direction byte) and must
            // arrive even during a fade or blocker (not a hit). Room filter is server-side.
            if (attackType == AttackType.PauseRelay)
            {
                PauseNotice.ApplyFromWire(attackerName, data, startIdx);
                return;
            }

            byte damageAmount = data[startIdx + 1];
            string receiverName = Encoding.UTF8.GetString(data, startIdx + 2, data.Length - startIdx - 2);

            Main.Multiplayer.AttackManager.ReceiveAttack(attackerName, receiverName, attackType, damageAmount);
        }

        // Effect

        public void SendEffect(EffectType effectType)
        {
            // Reliable only for the three latches the receiver cannot expire on its own: TrailOff,
            // DashSoundStop, ParryHitStop (PARRY_HIT_TIMEOUT was removed). Table in
            // TransportClassTable.IsLatchedStop, one place only.
            TransportClass cls = TransportClassTable.IsLatchedStop(effectType)
                ? TransportClass.Reliable
                : TransportClass.Unreliable;

            QueueMesssage(new byte[] { (byte)effectType }, NetworkType.Effect, cls);
        }

        /// <summary>
        /// Sends an effect that also needs a POSITION (ground impact and slide dust). An overload
        /// rather than a new packet type: the receiver reads the effect byte and stops, so
        /// existing builds ignore the extra bytes. No protocol change (version stays 11).
        /// </summary>
        public void SendEffect(EffectType effectType, Vector2 position)
        {
            byte[] payload = new byte[9];
            payload[0] = (byte)effectType;
            Buffer.BlockCopy(BitConverter.GetBytes(position.x), 0, payload, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(position.y), 0, payload, 5, 4);

            // Same table as the single-byte overload.
            TransportClass cls = TransportClassTable.IsLatchedStop(effectType)
                ? TransportClass.Reliable
                : TransportClass.Unreliable;

            QueueMesssage(payload, NetworkType.Effect, cls);
        }

        private void ReceiveEffect(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            EffectType effectType = (EffectType)data[startIdx];

            // Position is OPTIONAL; the length check lets both shapes share one channel. The
            // comparison is GREATER-OR-EQUAL: the payload is exactly nine bytes (effect + 4 + 4),
            // so length == startIdx + 9, and a strict > would ask nine > nine - never true.
            bool hasPosition = data.Length >= startIdx + 9;
            Vector2 position = Vector2.zero;

            if (hasPosition)
            {
                position.x = BitConverter.ToSingle(data, startIdx + 1);
                position.y = BitConverter.ToSingle(data, startIdx + 5);
            }

            Main.Multiplayer.AttackManager.ReceiveEffect(playerName, effectType, hasPosition, position);
        }

        // Ping

        public void SendPing(float time, ushort ping)
        {
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(time));
            bytes.AddRange(BitConverter.GetBytes(ping));

            // An RTT sample; the next replaces it.
            QueueMesssage(bytes.ToArray(), NetworkType.Ping, TransportClass.Unreliable);
        }

        private void ReceivePing(byte[] data)
        {
            float time = BitConverter.ToSingle(data, 0);

            int idx = 4;
            while (idx < data.Length)
            {
                byte length = data[idx];
                string name = Encoding.UTF8.GetString(data, idx + 1, length);
                ushort ping = BitConverter.ToUInt16(data, idx + 1 + length);
                idx += 1 + length + 2;

                Main.Multiplayer.OtherPlayerManager.ReceivePing(name, ping);
            }

            Main.Multiplayer.PingManager.ReceivePing(time);
        }

        // Enemies
        //
        // The wire handle is a 32-bit hash of the enemy id string, NOT a position index. A position
        // index breaks as soon as the two machines hold lists of different sizes; a content-derived
        // handle is computed independently on each machine.

        public void SendEnemyUpdate(uint handle, Vector2 position, float currentLife, int animState, float animTime, int fingerprint, bool ownedBySender, byte orientation)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(handle));
            bytes.AddRange(BitConverter.GetBytes(position.x));
            bytes.AddRange(BitConverter.GetBytes(position.y));
            bytes.AddRange(BitConverter.GetBytes(currentLife));
            bytes.AddRange(BitConverter.GetBytes(animState));
            bytes.AddRange(BitConverter.GetBytes(animTime));
            bytes.AddRange(BitConverter.GetBytes(fingerprint));
            bytes.Add((byte)(ownedBySender ? 1 : 0));
            bytes.Add(orientation);
            // Unreliable: the freeze message - 30 B x 20 Hz x N enemies. On the reliable class it
            // would fill the send buffer in under a second; the next copy is within 50 ms.
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyUpdate, TransportClass.Unreliable);
        }

        private void ReceiveEnemyUpdate(byte[] data)
        {
            uint handle = BitConverter.ToUInt32(data, 0);
            float xpos = BitConverter.ToSingle(data, 4);
            float ypos = BitConverter.ToSingle(data, 8);
            float currentLife = BitConverter.ToSingle(data, 12);
            int animState = BitConverter.ToInt32(data, 16);
            float animTime = BitConverter.ToSingle(data, 20);
            int fingerprint = BitConverter.ToInt32(data, 24);
            bool ownedBySender = data[28] == 1;
            byte orientation = data[29];

            EnemySyncManager.ReceiveEnemyUpdate(handle, new Vector2(xpos, ypos), currentLife, animState, animTime, fingerprint, ownedBySender, orientation);
        }

        /// <summary>
        /// Forwards one sword hit to the machine that is not swinging. Three extra bytes vs the
        /// original 8: DamageType, DamageElement and a flag byte (EnemySyncManager.HitFlagsFrom) -
        /// without them the reconstructed Hit was forced to Normal. Position is not sent: the
        /// receiver holds the same enemy instance. No protocol change: the server relays payload
        /// verbatim, and an older receiver reads the first 8 bytes and ignores the rest.
        /// </summary>
        public void SendEnemyDamageRequest(uint handle, float damageAmount, byte damageType,
                                           byte damageElement, byte flags)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(handle));
            bytes.AddRange(BitConverter.GetBytes(damageAmount));
            bytes.Add(damageType);
            bytes.Add(damageElement);
            bytes.Add(flags);
            // Reliable: lost, the host never applies the hit and the swing does not count - ever.
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDamageRequest, TransportClass.Reliable);
        }

        private void ReceiveEnemyDamageRequest(byte[] data)
        {
            uint handle = BitConverter.ToUInt32(data, 0);
            float damageAmount = BitConverter.ToSingle(data, 4);

            // An older sender stops at 8 bytes; the hit is then applied with the neutral values
            // the mod used before. Never read past the end.
            byte damageType = data.Length > 8 ? data[8] : (byte)DamageArea.DamageType.Normal;
            byte damageElement = data.Length > 9 ? data[9] : (byte)DamageArea.DamageElement.Normal;
            byte flags = data.Length > 10 ? data[10] : (byte)0;

            EnemySyncManager.ReceiveEnemyDamage(handle, damageAmount, damageType, damageElement, flags);
        }

        public void SendEnemyKill(uint handle)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(handle));
            // Reliable: the "ghost" - the peer's copy stays alive until something re-establishes
            // it, and nothing else does.
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDeath, TransportClass.Reliable);
        }

        private void ReceiveEnemyDeath(byte[] data)
        {
            uint handle = BitConverter.ToUInt32(data, 0);
            EnemySyncManager.ReceiveEnemyKill(handle);
        }

        // The host's ownership token: which machine may simulate the enemy. The peer applies this
        // instead of deciding for itself, keeping a single authority per enemy.
        public void SendEnemyOwnership(uint handle, bool ownedByReceiver)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(handle));
            bytes.Add((byte)(ownedByReceiver ? 1 : 0));
            // Reliable: a latch, sent once per change and never repeated. Lost, the peer keeps the
            // wrong owner and the enemy stays frozen for the session.
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyOwnership, TransportClass.Reliable);
        }

        private void ReceiveEnemyOwnership(byte[] data)
        {
            uint handle = BitConverter.ToUInt32(data, 0);
            bool ownedByMe = data[4] == 1;
            EnemySyncManager.ReceiveEnemyOwnership(handle, ownedByMe);
        }

        // Scene-independent death record. Payload is the enemy ID STRING, not a handle: the
        // receiver is usually in another scene where its handle table for that scene does not
        // exist yet.
        public void SendEnemyDeathGlobal(string enemyId)
        {
            // Reliable: the durable record (id as string, not a handle) - a later joiner would
            // find the enemy alive. No other message re-establishes it.
            QueueMesssage(Encoding.UTF8.GetBytes(enemyId), NetworkType.EnemyDeathGlobal, TransportClass.Reliable);
        }

        private void ReceiveEnemyDeathGlobal(byte[] data)
        {
            EnemySyncManager.ReceiveEnemyDeathGlobal(Encoding.UTF8.GetString(data));
        }

        // Authority

        /// <summary>
        /// The server elects the host; this tells the client whether it is the one driving
        /// enemies. Without this packet enemy sync silently does nothing.
        /// </summary>
        private void ReceiveHostAssign(byte[] data)
        {
            EnemySyncManager.SetHost(data.Length > 0 && data[0] == 1);
        }

        // Maybe replace name with entire ConnectInfo
        public delegate void ConnectDelegate(bool success, RefusalType refusal);
        public event ConnectDelegate OnConnect;
    }
}