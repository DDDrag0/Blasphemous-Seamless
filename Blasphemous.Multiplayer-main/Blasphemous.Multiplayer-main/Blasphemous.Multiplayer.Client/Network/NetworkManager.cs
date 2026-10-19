using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Client.PvP.Models;
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

        /// <summary>
        /// ⭐ IL FILO, DIETRO IL SEAM: da qui in giu' questo file non conosce piu' `SimpleTCP` e non
        /// nomina nessun socket. Prima era `private SimpleTcpClient _client;` e questo era l'unico
        /// punto dell'assembly in cui il trasporto compariva per nome; oggi il nome del tipo vero
        /// (`TcpTransport`, che avvolge `SimpleTcpClient`) sta in **un** posto solo
        /// (`TcpTransport.Connect`) e questo campo e' il seam.
        /// </summary>
        private ICoopTransport _transport;
        private string _serverIp = string.Empty;

        /// <summary>
        /// ⚠️ L'ESPRESSIONE E' QUELLA DI OGGI, con `_transport` al posto di `_client`:
        /// `_connectionStatus == Connected &amp;&amp; trasporto != null`. Non si usa
        /// `_transport.IsConnected` di proposito: aggiungerebbe un controllo sul socket (il `Connected`
        /// del `TcpClient`) che oggi non c'e', e la caduta della connessione verrebbe osservata prima —
        /// un cambio di comportamento, non un refactor. Vedi `CoopGate.cs:11-19`.
        /// </summary>
        public bool IsConnected => _connectionStatus == ConnectionStatus.Connected && _transport != null;
        public string ServerIP => _serverIp;

        private readonly List<byte> sendingQueue = new ();
        private readonly List<byte> receivingQueue = new ();
        private static readonly object datalock = new object();

        // Connection

        public bool Connect(string server, int port, string room, string player, string password, byte team)
        {
            if (_connectionStatus != ConnectionStatus.Disconnected)
                return false;

            try
            {
                // ⭐ IL SEAM — la prova che il filo non cambia sta nel confronto riga per riga:
                //
                //   PRIMA                                          DOPO
                //   _client = new SimpleTcpClient();               _transport = new TcpTransport();
                //   _client.Connect(server, port);                 _transport.Connect(server, port);
                //   _client.DataReceived += ReceiveMessage;        _transport.DataReceived += ReceiveMessage;
                //   _client.TcpClient.NoDelay = true;              (dentro TcpTransport.Connect)
                //
                // Stessi argomenti, stesso ordine utile: la `Connect` prima della sottoscrizione (come
                // oggi: la sottoscrizione avviene DOPO che il socket e' aperto) e `NoDelay` prima di
                // qualunque scrittura. La creazione del socket e `NoDelay` sono finite dentro il
                // trasporto, insieme; l'unica riga che si sposta e' `NoDelay`, che oggi era impostata
                // dopo la sottoscrizione ma prima di ogni `Write` (le scritture partono da `SendQueue`,
                // sul main thread, molto dopo) ⇒ nessun byte sul filo puo' accorgersene.
                _transport = new TcpTransport();
                _transport.Connect(server, port);
                _transport.DataReceived += ReceiveMessage;
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Lo stesso `catch` di oggi, sulla stessa eccezione: il trasporto NON la cattura
                // (`TcpTransport.Connect`), altrimenti questo ramo non verrebbe mai raggiunto.
                ModLog.Info("Failed to establish connection");
                OnConnect?.Invoke(false, RefusalType.Connection);
                return false;
            }

            OnConnectOld(server, room, player, password, team);
            return true;
        }

        public void Disconnect()
        {
            // Prima: `_client.Disconnect();` — stessa chiamata, un livello sotto (TcpTransport.Disconnect).
            _transport.Disconnect();
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
            _connectionStatus = ConnectionStatus.Disconnected;
            _serverIp = string.Empty;
            _transport = null;
            sendingQueue.Clear();
            receivingQueue.Clear();

            ModLog.Info("Disconnected from server");
            Main.Multiplayer.OnDisconnect();
        }

        // Server

        private void QueueMesssage(byte[] data, NetworkType type)
        {
            if (data == null || data.Length == 0 || _connectionStatus == ConnectionStatus.Disconnected) return;

            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes((ushort)data.Length));
            bytes.Add((byte)type);
            bytes.AddRange(data);

            sendingQueue.AddRange(bytes);
        }

        public void SendQueue()
        {
            if (sendingQueue.Count == 0 || _connectionStatus == ConnectionStatus.Disconnected) return;

            try
            {
                // ⭐ LA SOLA SCRITTURA DEL CLIENT, e l'argomento e' LETTERALMENTE lo stesso di prima:
                //   prima: `_client.Write(sendingQueue.ToArray());`
                //   dopo:  `_transport.Send(sendingQueue.ToArray());`
                // `sendingQueue.ToArray()` e' la stessa espressione, valutata nello stesso punto, e
                // `Send` non tocca il buffer (`TcpTransport.Send` fa `_client.Write(framed)`): il
                // numero di byte, il loro contenuto e il loro ordine sono quelli di prima. `IOException`
                // risale qui di proposito, perche' il `catch` sotto e' la via di `OnDisconnect()`.
                _transport.Send(sendingQueue.ToArray());
            }
            catch (System.IO.IOException)
            {
                OnDisconnect();
            }
            finally
            {
                sendingQueue.Clear();
            }
        }

        // Data should be formatted as length length type data
        //
        // ⚠️ ADATTATORE DI FORMA, NON DI CONTENUTO. Prima la firma era quella di `SimpleTCP`
        // (`object sender, DataReceivedEventArgs message` + `message.data`); ora e' `byte[]`, che e' la
        // forma del seam (`ICoopTransport.DataReceived`). **I byte sono gli stessi**: `message.data` era
        // gia' il `byte[]` che `SimpleTCP` consegna, e `TcpTransport.HandleDataReceived` lo rimbalza
        // senza copiarlo, spezzarlo o interpretarlo. Anche il THREAD e' lo stesso: `HandleDataReceived`
        // e' il callback di `SimpleTCP`, quindi questa riga gira sul suo thread di lettura, e da li'
        // prende `datalock` come ha sempre fatto.
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
            QueueMesssage(bytes.ToArray(), NetworkType.Position);
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
            QueueMesssage(new byte[] { animation }, NetworkType.Animation);
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
            QueueMesssage(BitConverter.GetBytes(direction), NetworkType.Direction);
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
            QueueMesssage(Encoding.UTF8.GetBytes(sceneName), NetworkType.EnterScene);
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
            QueueMesssage(new byte[] { 0 }, NetworkType.LeaveScene);
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

            // ⭐ MARKER `[Net] skin:` — la misura comincia QUI, prima del ramo, perche' il costo di una
            // skin non originale e' quasi tutto `EncodeToPNG`. NON tocca ne' il ramo ne' il payload:
            // e' un cronometro e basta (vedi `SkinSendMarker.cs`).
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

            QueueMesssage(bytes.ToArray(), NetworkType.Skin);

            // ⭐ La riga della misura, DOPO l'invio e sui byte che sono gia' stati accodati: legge
            // `bytes.Count` e lo stato della connessione, non decide niente. (`bytes` non viene
            // consumato da `QueueMesssage`, che ne copia il contenuto in una lista nuova.)
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
            QueueMesssage(new byte[] { team }, NetworkType.Team);
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

                // A death is normally a live event, so a player who joins later cannot learn about kills
                // that already happened: the enemy it never saw die stays alive on its screen. Re-announcing
                // our defeated spawn points here closes that gap. This runs on the main thread (the packet
                // queue is drained from OnLateUpdate), so queueing messages from here is safe.
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
            bytes.Add(PROTOCOL_VERSION);

            bytes.Add((byte)room.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(room));

            bytes.Add((byte)player.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(player));

            bytes.Add((byte)password.Length);
            if (password.Length > 0)
                bytes.AddRange(Encoding.UTF8.GetBytes(password));

            bytes.Add(team);

            QueueMesssage(bytes.ToArray(), NetworkType.Intro);
        }

        private void ReceiveIntro(byte[] data)
        {
            RefusalType response = (RefusalType)data[0];
            bool success = response == RefusalType.Accepted;

            ModLog.Info($"Established full connection (Success: {success})");

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

            QueueMesssage(bytes.ToArray(), NetworkType.Progress);
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

            QueueMesssage(bytes.ToArray(), NetworkType.Attack);
        }

        /// <summary>
        /// Manda un payload già pronto sul canale Attack, per il verso NUOVO nemico→giocatore.
        ///
        /// Perché un metodo separato e non un `NetworkType` nuovo: questo canale fa esattamente il
        /// viaggio che serve — il server lo inoltra a tutti i peer della STESSA SCENA tranne il mittente
        /// e ci PREMETTE il nome del mittente (`Server.cs:676-699`) — quindi non c'è niente da
        /// aggiungere né al protocollo (resta 11) né al server. Il verso si distingue per il primo byte
        /// del payload (`AttackType.EnemyHit`), che è un byte libero dell'enum.
        ///
        /// La forma del payload è in `EnemyHitPeer.BuildPayload` (EnemyHitPeerSync.cs), insieme alla
        /// sua spiegazione: qui non si interpreta niente, si consegna.
        /// </summary>
        public void SendAttackPayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                return;

            QueueMesssage(payload, NetworkType.Attack);
        }

        private void ReceiveAttack(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string attackerName);
            AttackType attackType = (AttackType)data[startIdx];

            // ⭐ IL VERSO NUOVO, prima di ogni altra cosa: un colpo di NEMICO su un giocatore.
            //
            // Va riconosciuto e dirottato PRIMA della lettura che segue, non dopo, per due ragioni:
            //   - il payload di questo verso ha una forma sua (danno float, bandiere, handle, nome del
            //     destinatario, id del suono) e i suoi byte non sono un nome di giocatore;
            //   - il ramo PvP di `AttackManager.ReceiveAttack` scarta tutto ciò che non è in
            //     `attacks.json` e si ferma su `InputManager.HasBlocker("ANY")`: un colpo di boss
            //     arrivato durante un fade NON deve essere buttato via come un colpo fra giocatori.
            if (attackType == AttackType.EnemyHit)
            {
                EnemyHitPeer.ApplyFromWire(attackerName, data, startIdx);
                return;
            }

            // ⭐ P1 - IL TERZO VERSO: un EFFETTO in pool nato su un'altra macchina. Stessa ragione del
            // ② per stare qui e non dopo: il payload ha una forma sua (nome del prefab + posizione +
            // rotazione) e i suoi byte non sono un nome di giocatore, quindi il ramo PvP lo
            // interpreterebbe come spazzatura. Il ricevente decide da solo se ricostruirlo
            // (`EffectRelay.ApplyFromWire`: catalogo del boss locale + dedup).
            if (attackType == AttackType.CoopEffectRelay)
            {
                EffectRelay.ApplyFromWire(attackerName, data, startIdx);
                return;
            }

            // ⭐ I DUE MARKER DELLA VOCE 56: la coppia richiesta/risposta che porta i conteggi e la
            // lista di id dell'altro. Stessa ragione delle due sopra per stare qui e non dopo: il suo
            // payload ha una forma sua e i suoi byte non sono un nome di giocatore, quindi il ramo PvP
            // li interpreterebbe come spazzatura. Non decide niente: stampa due righe e basta
            // (`SyncIdListMarker`).
            if (attackType == AttackType.SyncIdList)
            {
                SyncIdListMarker.ApplyFromWire(attackerName, data, startIdx);
                return;
            }

            // ⭐⭐ B — LA NOTIFICA "L'ALTRO E' IN PAUSA" (il quinto verso del canale Attack).
            // Stessa ragione dei tre qui sopra per stare QUI e non dopo: il payload ha una forma sua
            // (un byte di verso) e il ramo PvP lo interpreterebbe come `damageAmount` + nome del
            // bersaglio. E una ragione in piu': un fronte di pausa deve arrivare **anche durante un fade
            // o un blocker** (`AttackManager.ReceiveAttack` si ferma su `HasBlocker("ANY")`), perche' non
            // e' un colpo e non c'e' niente da filtrare.
            // Il filtro per stanza NON e' qui: lo fa il server (`Server.cs:676-687`). Vedi `PauseNotice`:
            // qui si consegna e basta, l'interpretazione (e il timeout) stanno di la'.
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
            QueueMesssage(new byte[] { (byte)effectType }, NetworkType.Effect);
        }

        /// <summary>
        /// Sends an effect that needs a PLACE as well as a kind - the ground impact and the slide's dust (A4).
        ///
        /// This is an overload rather than a new packet type on purpose: the receiver reads the effect byte and
        /// stops, so anything past it is ignored by every existing build, and a payload that grows is not a
        /// protocol change. That keeps the version at 11 and the server untouched.
        /// </summary>
        public void SendEffect(EffectType effectType, Vector2 position)
        {
            byte[] payload = new byte[9];
            payload[0] = (byte)effectType;
            Buffer.BlockCopy(BitConverter.GetBytes(position.x), 0, payload, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(position.y), 0, payload, 5, 4);

            QueueMesssage(payload, NetworkType.Effect);
        }

        private void ReceiveEffect(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            EffectType effectType = (EffectType)data[startIdx];

            // The position is OPTIONAL, and the length check is what lets both shapes share one channel:
            // the effects that stand on their own carry no coordinates, the ones that happen somewhere do.
            //
            // ⚠️ The comparison is GREATER-OR-EQUAL, and the difference is the whole feature. The payload is
            // exactly nine bytes - one for the effect, four for x, four for y - so the length here is
            // startIdx + 9, and a strict `>` asked whether nine is greater than nine. It never was, so
            // hasPosition was permanently false and the position was never read: the sender wrote it, the
            // server relayed it, and this line threw it away without a word.
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

            QueueMesssage(bytes.ToArray(), NetworkType.Ping);
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

        // The wire handle is a 32-bit hash of the enemy id string, NOT a position index. The full
        // identity (scene|name|x|y) is about 40 bytes and would dwarf the state it accompanies, but a
        // position index broke as soon as the two machines held lists of different sizes: every index
        // past the shorter list was rejected and those enemies never updated. A content-derived handle
        // is computed independently on each machine and survives any difference in list contents.

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
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyUpdate);
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
        /// Forwards one sword hit to the machine that is not swinging.
        ///
        /// The payload carries THREE bytes more than the handle and the amount, and the reason is that the
        /// hit itself cannot be reconstructed without them. The original 8 bytes produced a Hit with
        /// DamageType fixed at Normal, so on the machine that was only watching every blow sounded like a
        /// light one, and the enemy types that read the damage type to pick their reaction always picked the
        /// wrong one.
        ///
        /// DamageType and DamageElement are one byte each; the third byte is a flag field for the booleans
        /// the receiving side needs (see EnemySyncManager.HitFlagsFrom). The position is deliberately NOT
        /// sent: the receiver holds the same enemy instance, so it derives the impact point from the same
        /// collider the game would have used.
        ///
        /// This does NOT require a protocol change. The server relays the payload verbatim without parsing
        /// it, and a receiver built before this change reads the first eight bytes and ignores the rest.
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
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDamageRequest);
        }

        private void ReceiveEnemyDamageRequest(byte[] data)
        {
            uint handle = BitConverter.ToUInt32(data, 0);
            float damageAmount = BitConverter.ToSingle(data, 4);

            // Tolerated by design: an older sender stops at eight bytes, and the hit is then applied with the
            // neutral values the mod used before. Never read past the end.
            byte damageType = data.Length > 8 ? data[8] : (byte)DamageArea.DamageType.Normal;
            byte damageElement = data.Length > 9 ? data[9] : (byte)DamageArea.DamageElement.Normal;
            byte flags = data.Length > 10 ? data[10] : (byte)0;

            EnemySyncManager.ReceiveEnemyDamage(handle, damageAmount, damageType, damageElement, flags);
        }

        public void SendEnemyKill(uint handle)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(handle));
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDeath);
        }

        private void ReceiveEnemyDeath(byte[] data)
        {
            uint handle = BitConverter.ToUInt32(data, 0);
            EnemySyncManager.ReceiveEnemyKill(handle);
        }

        // The host's ownership token: which machine may simulate the enemy. The peer applies this instead
        // of deciding for itself, which is what keeps a single authority per enemy.
        public void SendEnemyOwnership(uint handle, bool ownedByReceiver)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(handle));
            bytes.Add((byte)(ownedByReceiver ? 1 : 0));
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyOwnership);
        }

        private void ReceiveEnemyOwnership(byte[] data)
        {
            uint handle = BitConverter.ToUInt32(data, 0);
            bool ownedByMe = data[4] == 1;
            EnemySyncManager.ReceiveEnemyOwnership(handle, ownedByMe);
        }
        // Scene-independent death record. Payload is the enemy ID STRING, not a handle: the receiver is
        // usually in another scene, where its handle table for that scene does not exist yet.
        public void SendEnemyDeathGlobal(string enemyId)
        {
            QueueMesssage(Encoding.UTF8.GetBytes(enemyId), NetworkType.EnemyDeathGlobal);
        }

        private void ReceiveEnemyDeathGlobal(byte[] data)
        {
            EnemySyncManager.ReceiveEnemyDeathGlobal(Encoding.UTF8.GetString(data));
        }

        // Authority

        /// <summary>
        /// The server elects the host; this tells the client whether it is the one driving enemies.
        /// Without this packet the client has no way of knowing, and enemy sync silently does nothing.
        /// </summary>
        private void ReceiveHostAssign(byte[] data)
        {
            EnemySyncManager.SetHost(data.Length > 0 && data[0] == 1);
        }

        // Maybe replace name with entire ConnectInfo
        public delegate void ConnectDelegate(bool success, RefusalType refusal);
        public event ConnectDelegate OnConnect;

        private const byte PROTOCOL_VERSION = 11;
    }
}
