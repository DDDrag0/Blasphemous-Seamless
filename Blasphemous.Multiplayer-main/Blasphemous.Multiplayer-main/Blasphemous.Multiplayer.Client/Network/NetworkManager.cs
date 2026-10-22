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
        /// ⭐⭐ "C'E' UN TENTATIVO IN CORSO, E NESSUNO HA ANCORA DATO UN ESITO" — dall'avvio del dial fino
        /// a *qualunque* esito (Intro accettato, rifiuto, timeout, eccezione).
        ///
        /// 🔴 PERCHE' ESISTE (⭐ questo e' il fix della **schermata bloccata su "Connecting..."**):
        /// `ConnectionDisplay` alza il proprio `_attemptingConnection` prima del dial e lo abbassa **solo**
        /// dentro `OnConnect`. Ma `OnConnect` veniva invocato in **due** punti soli: la `SocketException`
        /// del TCP e la risposta `Intro` del server. Il **timeout P2P** non passa da nessuno dei due: il
        /// trasporto alza `Disconnected`, si arriva a questa `OnDisconnect`, e `OnConnect` **non veniva
        /// chiamato** ⇒ il flag della schermata restava `true` **per sempre** ⇒ `DisplayMiddle` (che non
        /// disegna nessun bottone) ⇒ **solo il riavvio del gioco**.
        ///
        /// ✅ Da qui in poi `OnDisconnect` sa se stava leggendo un tentativo, e in quel caso chiude l'esito
        /// come **rifiuto di connessione**. Vale per **entrambi** i trasporti: e' il trasporto che dice
        /// "e' finita", non il protocollo.
        /// </summary>
        private bool _attempting;

        /// <summary>
        /// `true` mentre siamo **dentro** `Disconnect()`: il trasporto P2P emette `Disconnected` anche
        /// li', e senza questa bandierina `OnTransportEnded` chiamerebbe `OnDisconnect` due volte (due
        /// notifiche, due rimozioni). Il TCP non ne ha bisogno: non e' sottoscritto.
        /// </summary>
        private bool _ending;

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

        /// <summary>
        /// ⭐ LA CLASSE DI OGNI MESSAGGIO IN CODA, una voce per messaggio (non per byte): la decide
        /// `QueueMesssage`, secondo la tabella di `docs\STEAM_INVITES.md` §10. Serve a `SendQueue` per
        /// raggruppare i messaggi **consecutivi della stessa classe** in un solo invio: cosi' il filo
        /// P2P riceve un pacchetto unreliable per la posizione e uno reliable per la morte, e il TCP
        /// riceve gli stessi byte nello stesso ordine (la classe la ignora).
        /// </summary>
        private readonly List<TransportClass> sendingClasses = new ();

        /// <summary>Quanti byte occupa ogni messaggio in coda (testa `[ushort len][byte type]` compresa).</summary>
        private readonly List<int> sendingSizes = new ();

        // Connection

        public bool Connect(string server, int port, string room, string player, string password, byte team)
        {
            if (_connectionStatus != ConnectionStatus.Disconnected)
                return false;

            // ⭐ DA QUI IN POI C'E' UN TENTATIVO IN CORSO: l'esito **deve** arrivare a chi ha premuto
            // "Connect", anche quando arriva sotto forma di "il trasporto e' finito" (vedi `_attempting`).
            _attempting = true;

            try
            {
                // ⭐ IL SEAM — la prova che il filo non cambia sta nel confronto riga per riga:
                //
                //   PRIMA                                          DOPO
                //   _client = new SimpleTcpClient();               _transport = CreateTransport(server);
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
                //
                // ⭐⭐ C1b-2 — QUALE FILO. `CreateTransport` risponde `TcpTransport` per **ogni**
                // indirizzo, tranne quando il campo "Server IP" chiede esplicitamente il P2P (`p2p`):
                // vedi `SteamP2PTransport.IsRequested`. Il percorso TCP — cioe' tutto quello che c'era
                // prima, loopback dell'host compreso — resta la strada di default, identica.
                _transport = CreateTransport(server);
                _transport.Connect(server, port);
                _transport.DataReceived += ReceiveMessage;

                // ⭐⭐ C1b-2 — E SOLO PER IL P2P, IL SUO EVENTO DI FINE. Un socket TCP lo si scopre
                // caduto perche' la scrittura dopo fallisce (`IOException` -> `OnDisconnect`), ed e'
                // esattamente quello che fa gia'. Il link P2P invece puo' morire **in silenzio** (un
                // timeout: nessun pacchetto per 5 s), e senza ascoltare il suo `Disconnected` il client
                // resterebbe in "Attempting" per sempre. La sottoscrizione e' **condizionata al token
                // `p2p`**: sul percorso TCP non si sottoscrive niente, quindi il comportamento di oggi
                // (dove `Disconnected` lo emette solo `TcpTransport.Disconnect`) resta identico —
                // sottoscriverlo li' sarebbe un `OnDisconnect` doppio.
                if (SteamP2PTransport.IsRequested(server))
                    _transport.Disconnected += OnTransportEnded;
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Lo stesso `catch` di oggi, sulla stessa eccezione: il trasporto NON la cattura
                // (`TcpTransport.Connect`), altrimenti questo ramo non verrebbe mai raggiunto.
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
            // Prima: `_client.Disconnect();` — stessa chiamata, un livello sotto (TcpTransport.Disconnect).
            //
            // ⚠️ `_ending` esiste per UNA ragione: il trasporto P2P emette `Disconnected` anche dentro
            // questa chiamata, e `OnTransportEnded` non deve chiamare `OnDisconnect` una seconda volta.
            //
            // ⭐ E IL CASO "NON C'E' NIENTE DA CHIUDERE" NON DEVE ESPLODERE: `_transport` e' `null` dopo
            // una connessione fallita, e questa funzione la chiama anche il bottone "Annulla" della
            // schermata di connessione (`ConnectionDisplay.CancelConnect`), che e' proprio la rete di
            // sicurezza per quando il tentativo e' ancora in corso. Prima un `null` qui era una
            // `NullReferenceException` dentro l'`OnGUI`.
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
        /// Il trasporto ha dichiarato la fine del link **da solo** (timeout P2P, o "la sessione non e'
        /// piu' disponibile"). Si passa dalla stessa porta di sempre: `OnDisconnect`.
        ///
        /// ⚠️ Sottoscritto **solo** per il trasporto P2P (vedi `Connect`); sul TCP questo gestore non e'
        /// mai in ascolto, quindi il percorso di oggi non cambia.
        /// </summary>
        private void OnTransportEnded()
        {
            if (_ending)
                return;

            if (_connectionStatus == ConnectionStatus.Disconnected)
                return;

            ModLog.Info("[Net] p2p: il trasporto ha dichiarato finito il link: disconnessione");
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
            // ⭐⭐ (B) L'ESITO NEGATIVO ARRIVA ANCHE A CHI HA PREMUTO "CONNECT".
            //
            // 🔴 Il difetto: quando il P2P falliva (timeout d'handshake, timeout del battito, sessione non
            // piu' disponibile) o quando un TCP cadeva, si arrivava **solo** qui. `OnConnect` non veniva
            // invocato, quindi `ConnectionDisplay._attemptingConnection` restava `true` e la schermata
            // restava su "Connecting..." **senza nessun bottone**: l'unica uscita era riavviare il gioco.
            //
            // ⚠️`wasAttempting` va letto **prima** di azzerare il campo, e il campo si azzera **sempre**:
            // il flag non deve sopravvivere a un fallimento. E l'invocazione e' **dopo** aver riportato lo
            // stato a "sconnesso", cosi' chi riceve l'esito trova gia' `IsConnected == false` (e la
            // schermata torna a `DisplayStart` con i bottoni attivi).
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

            // "Rifiuto di connessione" e non un tipo nuovo: e' lo **stesso** esito che il client riceve
            // quando il socket non si apre (`RefusalType.Connection`), quindi la notifica a schermo e' una
            // di quelle gia' localizzate (`refcon`) e non serve nessuna stringa nuova.
            ModLog.Info("[Net] connessione fallita senza risposta: la schermata torna disponibile");
            OnConnect?.Invoke(false, RefusalType.Connection);
        }

        // Server

        /// <summary>
        /// ⭐ QUALE FILO — e il default e' quello di sempre. `TcpTransport` per ogni indirizzo; il P2P
        /// solo se il campo "Server IP" lo chiede **esplicitamente** (`p2p`), perche' sceglierlo da soli
        /// ("sono in una lobby Steam") romperebbe il loopback dell'host, che e' in lobby anche lui e deve
        /// restare un client TCP (`scratch\REFERTO-C1B1-SEAM-SERVER.md` §4).
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

            // ⭐ Una voce per MESSAGGIO, non per byte: e' cio' che permette a `SendQueue` di mandare in
            // un pacchetto solo i messaggi consecutivi della stessa classe, senza mai spezzare un frame.
            sendingClasses.Add(cls);
            sendingSizes.Add(bytes.Count);
        }

        public void SendQueue()
        {
            if (sendingQueue.Count == 0 || _connectionStatus == ConnectionStatus.Disconnected) return;

            try
            {
                // ⭐ LA SOLA SCRITTURA DEL CLIENT, e l'argomento e' LETTERALMENTE lo stesso di prima
                // quando la coda ha una classe sola (il caso normale):
                //   prima: `_client.Write(sendingQueue.ToArray());`
                //   dopo:  `_transport.Send(sendingQueue.ToArray(), cls);`
                // `sendingQueue.ToArray()` e' la stessa espressione, valutata nello stesso punto, e
                // `Send` non tocca il buffer (`TcpTransport.Send` fa `_client.Write(framed)`): il
                // numero di byte, il loro contenuto e il loro ordine sono quelli di prima. `IOException`
                // risale qui di proposito, perche' il `catch` sotto e' la via di `OnDisconnect()`.
                //
                // ⚠️ QUANDO LE CLASSI SONO MISTE (es. una posizione e una morte nello stesso frame) la
                // coda si spezza in **run consecutivi**: sul TCP sono piu' `Write` una dopo l'altra con
                // gli STESSI byte nello STESSO ordine — un flusso non ha confini di messaggio, quindi il
                // ricevente legge esattamente quello che leggeva prima — mentre sul P2P ogni run prende
                // il suo canale, che e' il motivo per cui la classe esiste.
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
        /// Manda la coda in **run di classe**: tutti i messaggi consecutivi con la stessa classe in un
        /// solo invio. Non riordina niente e non unisce classi diverse: l'ordine dei byte resta quello
        /// di accodamento, sempre.
        /// </summary>
        private void SendRuns()
        {
            // Il caso normale (una classe sola in tutto il flush): UNA chiamata, esattamente come prima.
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
            // 🔵 §10.2: il peer tiene il punto precedente per <=0.2 s e il campione dopo lo sostituisce.
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
            // ⭐ LA TERZA AMBIGUITA' DI §10: **un byte reliable su un messaggio unreliable**. Le clip
            // cambiano a ogni cambio e la successiva sovrascrive la precedente (🔵), ma il byte di MORTE
            // arriva **una volta sola per morte** e senza di lui il cadavere del peer resta **in piedi**
            // per tutta la morte — e il record affidabile della morte (`PlayerDeathSync.ApplyRemoteState`)
            // non ripara la posa (§10.2). Il predicato e' lo STESSO del mandante (`CorpsePeerTick`), non
            // una tabella riscritta qui: "questo byte e' una delle tre morti?".
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
            // 🔵 §10.2: il peer guarda dalla parte sbagliata fino al flip successivo — sbagliato per un
            // po', non per sempre; e il flip successivo lo sovrascrive.
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
            // 🟢 §10.2: se si perde, ogni flusso filtrato per scena resta diretto nella scena vecchia
            // fino alla stanza successiva — e niente lo ri-stabilisce.
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
            // 🟢 §10.2: il peer terrebbe un avatar nella stanza che ho lasciato, e il ramo che lo
            // toglie non verrebbe mai preso.
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

            // 🟢 §10.2 — E IL CANALE E' IL 2: la skin e' affidabile **e** l'unico payload che puo'
            // superare un datagramma unreliable (~1200 B, un PNG e' KB). Il canale 2 e' affidabile ma
            // ISOLATO: un PNG non deve far aspettare una morte, una morte non deve aspettare un PNG.
            QueueMesssage(bytes.ToArray(), NetworkType.Skin, TransportClass.Reliable);

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
            // 🟢 §10.2: mandata UNA volta, alla connessione. Se si perde, il nome e il colore sulla
            // mappa restano sbagliati per tutta la sessione.
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
            // ⭐ LA VERSIONE E' UNA SOLA, e non vive piu' in fondo a questo file: sta in
            // `Common\Protocol.cs`, cioe' nella DLL che client e server condividono (e che il deploy
            // copia in `Modding\data`). Due definizioni separate dello stesso numero potevano
            // divergere in silenzio; una sola non puo'. Il valore resta 11.
            bytes.Add(Protocol.PROTOCOL_VERSION);

            bytes.Add((byte)room.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(room));

            bytes.Add((byte)player.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(player));

            bytes.Add((byte)password.Length);
            if (password.Length > 0)
                bytes.AddRange(Encoding.UTF8.GetBytes(password));

            bytes.Add(team);

            // 🟢 §10.2: l'handshake, prima di tutto il resto. Senza, nessuna sessione — e niente lo
            // ritenta.
            QueueMesssage(bytes.ToArray(), NetworkType.Intro, TransportClass.Reliable);
        }

        private void ReceiveIntro(byte[] data)
        {
            RefusalType response = (RefusalType)data[0];
            bool success = response == RefusalType.Accepted;

            ModLog.Info($"Established full connection (Success: {success})");

            // ⭐ L'ESITO VERO E' QUESTO (c'e' una risposta del server, anche quando e' un rifiuto): il
            // tentativo e' concluso, e l'`OnConnect` in fondo a questa funzione lo dira' con il motivo
            // esatto (`RefusalType`). Va azzerato **prima** di `Disconnect()`: altrimenti `OnDisconnect`
            // vedrebbe ancora un tentativo in corso e aggiungerebbe il suo esito generico (`Connection`),
            // cioe' due esiti per lo stesso tentativo — e il secondo cancellerebbe il motivo vero.
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

            // 🟢 §10.2: il progresso e' un fatto durevole, e `_sentAllProgress` e' one-shot — perso,
            // non si ri-manda.
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

            // 🟢 §10.3 — il colpo PvP: se si perde, quel danno non avviene MAI sull'altra macchina, e
            // la vita non e' un valore replicato, quindi niente lo ri-stabilisce.
            QueueMesssage(bytes.ToArray(), NetworkType.Attack, TransportClass.Reliable);
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

            // ⭐ LA PRIMA AMBIGUITA' DI §10.3: `NetworkType.Attack` e' un **demultiplexer** — cinque
            // messaggi su un tipo solo, e **due classi**. La classe la decide il primo byte del payload
            // (l'`AttackType`), che e' esattamente l'informazione che il ricevente usa per smistare:
            //   🟢 `EnemyHit` (200) — il colpo di un nemico sull'altro giocatore: la sua vita non e'
            //      replicata, perso il colpo il danno non c'e' e nessuno lo ripete;
            //   🟢 `PauseRelay` (203) — un fronte edge-triggered, mandato una volta per pausa/ripresa:
            //      perso, il peer continua a giocare e il fronte non torna finche' lo stato non cambia
            //      due volte;
            //   🔵 `CoopEffectRelay` (201) — una particella: cosmetico, e il prossimo fronte la rimpiazza;
            //   🔵 `SyncIdList` (202) — la diagnostica degli id: al massimo una ogni 15 s.
            TransportClass cls = IsReliableAttackPayload(payload)
                ? TransportClass.Reliable
                : TransportClass.Unreliable;

            QueueMesssage(payload, NetworkType.Attack, cls);
        }

        /// <summary>
        /// La classe di un payload del canale Attack, dal suo primo byte (`AttackType`). Il default e'
        /// **reliable**: un `AttackType` che non conosciamo e' un colpo PvP, e un colpo perso non si
        /// recupera. Vedi §10.3 per la tabella completa.
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
            // ⭐ LA SECONDA AMBIGUITA' DI §10: un `NetworkType`, **due classi**. I 29 siti di chiamata
            // mandano quasi tutti un effetto cosmetico — se si perde manca un suono o una particella,
            // e nessuno stato resta sbagliato (🔵). Ma **tre** sono i latch che il ricevente NON sa
            // scadere da solo: `TrailOff` (la scia fantasma resta accesa), `DashSoundStop` (il suono
            // del dash resta in loop) e `ParryHitStop` (il CLANG resta acceso) — il `PARRY_HIT_TIMEOUT`
            // che li scadeva e' stato rimosso, quindi si mandano affidabili (🟢). La tabella e' in
            // `TransportClassTable.IsLatchedStop`, in un posto solo.
            TransportClass cls = TransportClassTable.IsLatchedStop(effectType)
                ? TransportClass.Reliable
                : TransportClass.Unreliable;

            QueueMesssage(new byte[] { (byte)effectType }, NetworkType.Effect, cls);
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

            // 🔵 Come l'overload a un byte: gli effetti CON posizione sono polvere/impatti, nessuno dei
            // tre latch. La classe si chiede alla stessa tabella, cosi' un domani che ne arrivasse uno
            // non si sbaglia per distrazione.
            TransportClass cls = TransportClassTable.IsLatchedStop(effectType)
                ? TransportClass.Reliable
                : TransportClass.Unreliable;

            QueueMesssage(payload, NetworkType.Effect, cls);
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

            // 🔵 §10.2: un campione di RTT; il secondo dopo lo rimpiazza.
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
            // 🔵 §10.2: e' IL messaggio del congelamento — 30 B x 20 Hz x N nemici. Sulla classe
            // reliable riempirebbe il buffer di invio (~1 MB) in meno di un secondo (`SendP2PPacket`
            // torna `false` in silenzio): la prossima copia e' a <=50 ms, e la passata completa entro 1 s.
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
            // 🟢 §10.2: senza, l'host non applica MAI il colpo e il fendente non conta — per sempre,
            // mentre lo schermo di chi ha mandato ha gia' mostrato il colpo.
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDamageRequest, TransportClass.Reliable);
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
            // 🟢 §10.2: il "fantasma" — la copia del peer resta viva finche' qualcosa la ri-stabilisce.
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDeath, TransportClass.Reliable);
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
            // 🟢 §10.2: il token di proprieta' e' latchato — mandato una volta per cambio, mai ripetuto.
            // Perso, il peer tiene il proprietario sbagliato: un nemico congelato per tutta la sessione.
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyOwnership, TransportClass.Reliable);
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
            // 🟢 §10.2: e' IL record durevole (l'id come stringa, non un handle): un giocatore che
            // entra dopo troverebbe il nemico vivo. Nessun altro messaggio lo ri-stabilisce.
            QueueMesssage(Encoding.UTF8.GetBytes(enemyId), NetworkType.EnemyDeathGlobal, TransportClass.Reliable);
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
    }
}
