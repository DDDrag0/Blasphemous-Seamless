using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Server.Models;
using SimpleTCP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// L'ENGINE DEL SERVER, e questa classe vive in DUE posti: nell'eseguibile standalone (`net8.0`, la
/// cartella `SERVER-COOP\`) e DENTRO il client (`net35`), per la "host session" (il bottone
/// "Host session" della schermata di connessione). Il sorgente e' UNO: il client lo compila con
/// `COOP_HOST` definito (vedi `Blasphemous.Multiplayer.Client.csproj`), e le poche righe che
/// differiscono stanno dentro `#if COOP_HOST` - dichiarate una per una nel referto
/// (`scratch\REFERTO-SERVER-NEL-CLIENT.md`).
///
/// ⚠️ PORTING net8.0 -> net35: la logica e' la stessa, riga per riga. Le tre cose che net35 non ha
/// sono il RANGE di `Receive` (sostituito da una copia esplicita, stessi byte), `Console.*` (nel
/// `Logger`, che senza console LANCIEREBBE `IOException`) e `AppContext.BaseDirectory`
/// (`ServerLog`, che nel client non esiste proprio: le righe vanno nel log della mod).
///
/// ⭐ E due cose che prima NON esistevano e che l'accorpamento richiede: uno `Stop()` (prima l'unico
/// spegnimento era `Environment.Exit(0)` nella console, che nel gioco non si puo' usare) e un
/// `IsRunning` per non avviare due volte lo stesso server.
/// </summary>
public class Server
{
    private readonly int _maxPlayers;
    private readonly string _password;

    private SimpleTcpServer server;
    private Dictionary<string, PlayerInfo> connectedPlayers;
    private string _hostIp;
    private int _port;

    public Server(int maxPlayers, string password)
    {
        // Change how this works later
        _maxPlayers = maxPlayers;
        _password = password;
    }

    /// <summary>
    /// `true` se questo oggetto sta ascoltando ADESSO. Nel client e' cio' che distingue "host
    /// avviato" da "host fermo": senza, il bottone potrebbe avviare un secondo `SimpleTcpServer`
    /// sulla stessa porta (cioe' il caso "porta occupata" provocato da noi stessi).
    /// </summary>
    public bool IsRunning
    {
        get { return server != null; }
    }

    /// <summary>La porta su cui questo server sta ascoltando (0 se non e' mai partito).</summary>
    public int Port
    {
        get { return _port; }
    }

    public Dictionary<string, PlayerInfo> getPlayers()
    {
        return connectedPlayers;
    }

    public bool DelayDisabled
    {
        get => server != null && server.DelayDisabled;
        set
        {
            if (server != null)
                server.DelayDisabled = value;
        }
    }

    public bool Start(int port)
    {
        try
        {
            server = new SimpleTcpServer();
            server.ClientConnected += clientConnected;
            server.ClientDisconnected += clientDisconnected;
            server.DataReceived += Receive;

            // ⭐⭐ IL BIND: WILDCARD NEL CLIENT, INVARIATO NELLO STANDALONE — E NON E' UNA PREFERENZA.
            //
            // `SimpleTcpServer.Start(int port)` NON lega `IPAddress.Any`: enumera le NIC con
            // `OperationalStatus == Up` (`SimpleTcpServer.GetIPAddresses()`) e apre **un listener per
            // OGNI indirizzo unicast** di ognuna (`ServerListener` ctor -> `TcpListenerEx.Start()`).
            // Quindi il loopback c'e' **solo se il runtime lo elenca fra le NIC**. E' esattamente il
            // difetto misurato nella "host session": `[Host] server avviato` + rifiuto istantaneo di
            // `127.0.0.1` (0 righe `[Server]` per quel dial) mentre l'IP LAN funziona — cioe' il
            // listener era sui NIC e NON sul loopback. Il dettaglio, con le prove, sta in
            // `scratch\REFERTO-AUTOCONNECT.md`.
            //
            // ⇒ Nel client si lega ESPLICITAMENTE la wildcard IPv4: un solo listener, che copre
            //   `127.0.0.1` **e** la LAN, senza dipendere da cosa il runtime elenca. Ed e' l'unica
            //   riga che cambia, perche' lo standalone (`SERVER-COOP\`, `net8.0`) continua a chiamare
            //   `Start(port)` con la sua strada per-NIC: **il bind del server esterno non si tocca**.
            //
            // ⚠️ La porta occupata resta un `SocketException` da `Start(IPAddress, int)` (il
            //   `ServerListener` lancia dentro il costruttore): stesso `catch`, stesso `false`, stessa
            //   riga `[Host] porta occupata` di prima. Non si perde nessun caso d'errore.
            //
            // ⚠️ Con la wildcard il firewall di Windows mostra la richiesta di autorizzazione al primo
            //   avvio (gia' documentato in `HostSession`): se la si nega, la LAN non passa ma il
            //   loopback continua a funzionare, quindi l'host entra in partita lo stesso.
#if COOP_HOST
            server.Start(System.Net.IPAddress.Any, port);
#else
            server.Start(port);
#endif
            server.DelayDisabled = true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // ⚠️ Qui il campo viene azzerato, e prima no: un `Start` fallito lasciava un
            // `SimpleTcpServer` mai avviato nel campo, quindi `DelayDisabled` toccava un oggetto non
            // inizializzato e `IsRunning` avrebbe detto "sto ascoltando" su una porta che non e' mai
            // stata aperta. Nessun cambio di comportamento per lo standalone (che su `false` esce),
            // e nel client e' la differenza fra "riprovo" e "credo di essere acceso".
            server = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            // ⭐ MISURATO, NON SUPPOSTO: con la porta occupata `SimpleTcpServer.Start` NON lancia
            // `SocketException`, lancia `InvalidOperationException("Port was already occupied for all
            // network interfaces")`. Il `catch` qui sopra quindi non e' MAI scattato su questo caso:
            // fino a oggi uno standalone avviato su una porta gia' occupata moriva con un'eccezione
            // non gestita (`Unhandled exception ... at Server.Start`) invece di stampare
            // "Server failed to start on port", che era esattamente cio' che `Main` si aspettava.
            //
            // ⇒ Questa riga e' l'unica differenza di COMPORTAMENTO per lo standalone, e va nella
            //   direzione voluta: da "crash" a "messaggio chiaro e uscita pulita". Sul percorso
            //   buono non cambia niente.
            // ⇒ Nel client e' cio' che rende `[Host] porta occupata` vero anche quando la porta viene
            //   presa fra la prova di liberta' e il `bind` vero (vedi `HostSession.IsPortFree`).
            server = null;
            return false;
        }

        _port = port;
        connectedPlayers = new Dictionary<string, PlayerInfo>();
        _hostIp = null;

        // ⭐ UNA SESSIONE CHE PARTE E' UNA SESSIONE DA ZERO. Nello standalone lo garantiva il
        // processo (che muore e riparte); qui il processo e' il GIOCO e vive per ore: senza questa
        // riga, spegnere e riavviare l'host si porterebbe dietro il progresso delle squadre della
        // sessione precedente e lo rigiocherebbe addosso ai nuovi arrivati. Riusa la funzione che
        // gia' esiste per la pulizia a fine partita (`Core.removeUnusedGameData`): qui non c'e'
        // nessun giocatore, quindi non resta nessuna squadra.
        Core.removeUnusedGameData(connectedPlayers);
        return true;
    }

    /// <summary>
    /// ⭐ LO SPEGNIMENTO CHE PRIMA NON ESISTEVA. Nello standalone l'unico modo di fermare il server
    /// era `Environment.Exit(0)` dalla console (`Core.Main`), che dentro il gioco non si puo' usare:
    /// chiude il processo del gioco. Questo chiude l'ascolto, butta giu' i client e riporta l'oggetto
    /// allo stato di "mai avviato", cosi' si puo' riaccendere (o lasciare la porta a un server
    /// esterno).
    ///
    /// Non lancia MAI: e' chiamato dal pulsante "Stop host" e alla chiusura della mod, cioe' in punti
    /// dove un'eccezione arriverebbe al gioco. Ogni guasto vale "il server e' fermo lo stesso",
    /// perche' e' quello che conta per la porta.
    /// </summary>
    public void Stop()
    {
        // L'ordine e' deliberato: prima si stacca il campo, poi si ferma l'ascoltatore. Da questo
        // istante `Send` non manda piu' niente (vedi la guardia li' sotto) mentre `Stop()` sta
        // chiudendo i socket, quindi nessuna scrittura parte su una connessione in smontaggio.
        SimpleTcpServer running = server;
        server = null;
        _port = 0;

        if (running != null)
        {
            try
            {
                running.Stop();
            }
            catch (Exception e)
            {
                Logger.Error("Couldn't stop the server: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // La memoria della sessione se ne va con la sessione: `connectedPlayers` resta un dizionario
        // VUOTO e non null, perche' i callback di disconnessione possono ancora arrivare mentre i
        // socket si chiudono e devono trovarlo utilizzabile.
        connectedPlayers = new Dictionary<string, PlayerInfo>();
        _hostIp = null;
    }

    private void Send(string ip, byte[] data, NetworkType dataType)
    {
        // ⚠️ `server == null` significa "fermo" (mai avviato, o `Stop()` appena chiamato). Prima
        // questa guardia non serviva: il campo era sempre valorizzato dal processo. Nel gioco il
        // server puo' essere spento mentre i manager del client stanno ancora girando, e un
        // `NullReferenceException` qui arriverebbe al thread di update della mod.
        if (server == null)
            return;

        if (data != null && data.Length > 0)
        {
            List<byte> list = new List<byte>(BitConverter.GetBytes((ushort)data.Length));
            list.Add((byte)dataType);
            list.AddRange(data);

            try
            {
                server.Send(ip, list.ToArray());
            }
            catch (Exception)
            {
                Logger.Error("Couldn't send data to " + ip + "!");
            }
        }
    }

    // Data should be formatted as length length type data
    private void Receive(object sender, DataReceivedEventArgs e)
    {
        int startIdx = 0;
        while (startIdx < e.data.Length - 3)
        {
            ushort length = BitConverter.ToUInt16(e.data, startIdx);
            NetworkType type = (NetworkType)e.data[startIdx + 2];

            // ⚠️ PORTING net35 — QUESTA E' LA SOLA RIGA DEL SERVER che non compilava sul client.
            // Prima era il range: `e.data[(startIdx + 3)..(startIdx + 3 + length)]`, che richiede
            // System.Range/System.Index (.NET Core 3+) e che `net35` non ha. I byte sono GLI STESSI,
            // copiati a mano: `length` byte a partire da `startIdx + 3`. Nessuna interpretazione,
            // nessun riordino, nessun byte in piu' o in meno.
            //
            // ⭐ E con la copia esplicita arriva gratis una guardia che col range non c'era: se il
            // frame dichiara piu' byte di quanti ne sono arrivati, prima si lanciava
            // `ArgumentOutOfRangeException` DENTRO il thread di lettura di SimpleTCP (nel gioco:
            // thread che muore, connessione che resta appesa). Qui il frame troncato viene scartato
            // con la riga d'errore che questo metodo usava gia' per i dati malformati, e il ciclo si
            // chiude. Nessun cambio di protocollo: un frame integro prende la stessa strada di prima.
            if (startIdx + 3 + length > e.data.Length)
            {
                Logger.Error("Received data was formatted incorrectly");
                return;
            }

            byte[] messageData = new byte[length];
            Array.Copy(e.data, startIdx + 3, messageData, 0, length);

            //Logger.Info($"Received {type} packet: {length} bytes");
            processDataReceived(type, messageData);
            startIdx += 3 + length;
        }
        if (startIdx != e.data.Length)
            Logger.Error("Received data was formatted incorrectly");

        // Determines which received function to call based on the type
        void processDataReceived(NetworkType type, byte[] data)
        {
            switch (type)
            {
                case NetworkType.Position:      ReceivePosition(e.ip, data); break;
                case NetworkType.Animation:     ReceiveAnimation(e.ip, data); break;
                case NetworkType.Direction:     ReceiveDirection(e.ip, data); break;
                case NetworkType.EnterScene:    receivePlayerEnterScene(e.ip, data); break;
                case NetworkType.LeaveScene:    receivePlayerLeaveScene(e.ip, data); break;
                case NetworkType.Skin:          receivePlayerSkin(e.ip, data); break;
                case NetworkType.Team:          receivePlayerTeam(e.ip, data); break;
                case NetworkType.Intro:         receivePlayerIntro(e.ip, data); break;
                case NetworkType.Progress:      receivePlayerProgress(e.ip, data); break;
                case NetworkType.Attack:             receivePlayerAttack(e.ip, data); break;
                case NetworkType.Effect:             receivePlayerEffect(e.ip, data); break;
                case NetworkType.Ping:               ReceivePing(e.ip, data); break;
                case NetworkType.EnemyUpdate:        receiveEnemyUpdate(e.ip, data); break;
                case NetworkType.EnemyDamageRequest: receiveEnemyDamageRequest(e.ip, data); break;
                case NetworkType.EnemyDeath:         receiveEnemyDeath(e.ip, data); break;
        case NetworkType.EnemyDeathGlobal:   receiveEnemyDeathGlobal(e.ip, data); break;
        case NetworkType.EnemyOwnership:     receiveEnemyOwnership(e.ip, data); break;
                default:
                    Logger.Error($"Data type '{type}' is not valid"); break;
            }
        }
    }

    private void clientConnected(object sender, ClientConnectionEventArgs e)
    {
        Logger.Info("Client connected at " + e.ip);
    }

    private void clientDisconnected(object sender, ClientConnectionEventArgs e)
    {
        Logger.Info("Client disconnected at " + e.ip);

        // Make sure this client was actually connected, not just rejected from server
        if (!connectedPlayers.ContainsKey(e.ip))
            return;

        // Send that this player has disconnected & remove them
        sendPlayerConnection(e.ip, false);
        connectedPlayers.Remove(e.ip);
        Core.removeUnusedGameData(connectedPlayers);

        if (e.ip == _hostIp)
        {
            _hostIp = connectedPlayers.Keys.FirstOrDefault();
            if (_hostIp != null)
            {
                Logger.Info($"Host reassigned to: {connectedPlayers[_hostIp].name} ({_hostIp})");

                // Sent to EVERY remaining client, not only to the new host.
                //
                // The single send was one-shot and never retried, so a lost HostAssign was permanent: with
                // three players, when the host left, the promotion of the next player was announced to that
                // player alone - and the third one was told nothing at all. Its log shows no "Host role: True"
                // and not even the departure of the player before it, so it kept believing the enemies belonged
                // to somebody who had already gone, and its AI stayed frozen for the rest of the session.
                //
                // Broadcasting makes the assignment idempotent, which is what it should have been: every
                // client is told the current truth each time it changes. sendHostAssign already decides per
                // recipient (isHost = playerIp == _hostIp), and a client that is not the host and never was
                // receives SetHost(false), which changes nothing at all.
                foreach (string ip in connectedPlayers.Keys)
                    sendHostAssign(ip);
            }
            else
                Logger.Info("Host disconnected, no other players connected.");
        }
    }

    private PlayerInfo getCurrentPlayer(string ip)
    {
        if (connectedPlayers.ContainsKey(ip))
            return connectedPlayers[ip];

        Logger.Warn("Data for " + ip + " has not been created yet!");
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

    private void SendPosition(string playerIp)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip && current.isInSameScene(connectedPlayers[ip]))
            {
                // Send this player's updated position
                Send(ip, GetPositionPacket(current), NetworkType.Position);
            }
        }
    }

    private void ReceivePosition(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        current.xPos = BitConverter.ToSingle(data, 0);
        current.yPos = BitConverter.ToSingle(data, 4);

        SendPosition(playerIp);
    }

    private byte[] GetPositionPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(BitConverter.GetBytes(player.xPos));
        bytes.AddRange(BitConverter.GetBytes(player.yPos));
        return bytes.ToArray();
    }

    // Animation

    private void SendAnimation(string playerIp)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip && current.isInSameScene(connectedPlayers[ip]))
            {
                // Send this player's updated animation
                Send(ip, GetAnimationPacket(current), NetworkType.Animation);
            }
        }
    }

    private void ReceiveAnimation(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        current.animation = data[0];

        SendAnimation(playerIp);
    }

    private byte[] GetAnimationPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.Add(player.animation);
        return bytes.ToArray();
    }

    // Direction

    private void SendDirection(string playerIp)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip && current.isInSameScene(connectedPlayers[ip]))
            {
                // Send this player's updated direction
                Send(ip, GetDirectionPacket(current), NetworkType.Direction);
            }
        }
    }

    private void ReceiveDirection(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);

        // ⚠️ PORTING net35 — LA SECONDA RIGA CHE NON COMPILAVA, e non era nell'elenco dello studio.
        // Prima era `BitConverter.ToBoolean(data)`, cioe' l'overload a UN argomento: quello esiste
        // solo da .NET Core in poi. `net35` ha solo `ToBoolean(byte[], int)`, che vuole l'indice.
        // Il significato e' identico - su un solo byte "diverso da zero" vuol dire `true` - quindi si
        // legge il byte e lo si confronta: stesso valore, stessa direzione, stesso pacchetto.
        current.facingDirection = data[0] != 0;

        SendDirection(playerIp);
    }

    private byte[] GetDirectionPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(BitConverter.GetBytes(player.facingDirection));
        return bytes.ToArray();
    }

    // Enter scene

    private void sendPlayerEnterScene(string playerIp) // Optimize these send functions to combine them together
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip)
            {
                // Send that this player has entered a new scene
                Send(ip, getScenePacket(current), NetworkType.EnterScene);

                if (current.isInSameScene(connectedPlayers[ip]))
                {
                    // If in same scene, also send position data to each one
                    Send(ip, GetPositionPacket(current), NetworkType.Position);
                    Send(ip, GetAnimationPacket(current), NetworkType.Animation);
                    Send(ip, GetDirectionPacket(current), NetworkType.Direction);

                    Send(playerIp, GetPositionPacket(connectedPlayers[ip]), NetworkType.Position);
                    Send(playerIp, GetAnimationPacket(connectedPlayers[ip]), NetworkType.Animation);
                    Send(playerIp, GetDirectionPacket(connectedPlayers[ip]), NetworkType.Direction);
                }
            }
        }
    }

    private void receivePlayerEnterScene(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        current.sceneName = Encoding.UTF8.GetString(data);

        sendPlayerEnterScene(playerIp);
    }

    private byte[] getScenePacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(Encoding.UTF8.GetBytes(player.sceneName));
        return bytes.ToArray();
    }

    // Leave scene

    private void sendPlayerLeaveScene(string playerIp)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        current.xPos = 0;
        current.yPos = 0;
        current.animation = 0;
        current.facingDirection = false;

        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip)
            {
                // Send that this player has left their old scene
                Send(ip, Encoding.UTF8.GetBytes(current.name), NetworkType.LeaveScene);
            }
        }
    }

    private void receivePlayerLeaveScene(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);

        sendPlayerLeaveScene(playerIp);
        current.sceneName = "";
    }

    // Skin

    private void sendPlayerSkin(string playerIp)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip)
            {
                // Send this player's updated skin
                Send(ip, getSkinPacket(current), NetworkType.Skin);
            }
        }
    }

    private void receivePlayerSkin(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        current.skin = data;

        sendPlayerSkin(playerIp);
    }

    private byte[] getSkinPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(player.skin);
        return bytes.ToArray();
    }

    // Team

    private void sendPlayerTeam(string playerIp)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip)
            {
                Send(ip, getTeamPacket(current), NetworkType.Team);
            }
        }

        // Send all of the server data to this player for them to merge
        Logger.Info("Sending all server data to " + playerIp);
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
                Send(playerIp, getProgressPacket("*", i, value, id), NetworkType.Progress);
            }
        }
    }

    private void receivePlayerTeam(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        current.team = data[0];

        sendPlayerTeam(playerIp);
        Core.removeUnusedGameData(connectedPlayers);
    }

    private byte[] getTeamPacket(PlayerInfo player)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.Add(player.team);
        return bytes.ToArray();
    }

    // Connection

    private void sendPlayerConnection(string playerIp, bool connected)
    {
        // Send message to all other connected ips
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip)
            {
                Send(ip, getConnectionPacket(current, connected), NetworkType.Connection);
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

    private void sendPlayerIntro(string playerIp, RefusalType response)
    {
        Send(playerIp, getIntroPacket((byte)response), NetworkType.Intro);

        // Only send rest of data if successful connection
        if (response > 0)
            return;

        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip)
            {
                // Send all other connected players and their important data
                Send(playerIp, getConnectionPacket(connectedPlayers[ip], true), NetworkType.Connection);
                Send(playerIp, getSkinPacket(connectedPlayers[ip]), NetworkType.Skin);
                Send(playerIp, getTeamPacket(connectedPlayers[ip]), NetworkType.Team);
                if (connectedPlayers[ip].sceneName != "")
                {
                    Send(playerIp, getScenePacket(connectedPlayers[ip]), NetworkType.EnterScene);
                    if (current.isInSameScene(connectedPlayers[ip]))
                    {
                        Send(playerIp, GetPositionPacket(connectedPlayers[ip]), NetworkType.Position);
                        Send(playerIp, GetAnimationPacket(connectedPlayers[ip]), NetworkType.Animation);
                        Send(playerIp, GetDirectionPacket(connectedPlayers[ip]), NetworkType.Direction);
                    }
                }
            }
        }
    }

    private void receivePlayerIntro(string playerIp, byte[] data)
    {
        int idx = 0;

        // Ensure the server doesn't have max number of players
        if (connectedPlayers.Count >= _maxPlayers)
        {
            Logger.Warn("Player connection rejected: Player limit reached");
            sendPlayerIntro(playerIp, RefusalType.PlayerLimit);
            return;
        }

        // Ensure there are no duplicate ips
        if (connectedPlayers.ContainsKey(playerIp))
        {
            Logger.Warn("Player connection rejected: Duplicate ip address");
            sendPlayerIntro(playerIp, RefusalType.DuplicateIp);
            return;
        }

        byte protocol = data[idx++];

        // Ensure the protocol version matches
        if (protocol != PROTOCOL_VERSION)
        {
            Logger.Warn("Player connection rejected: Protocol version doesn't match");
            sendPlayerIntro(playerIp, RefusalType.Protocol);
            return;
        }

        byte roomLength = data[idx];
        string room = Encoding.UTF8.GetString(data, idx + 1, roomLength);
        idx += roomLength + 1;

        byte nameLength = data[idx];
        string name = Encoding.UTF8.GetString(data, idx + 1, nameLength);
        idx += nameLength + 1;

        // Ensure there are no duplicate names
        foreach (PlayerInfo player in connectedPlayers.Values)
        {
            if (player.name == name)
            {
                Logger.Warn("Player connection rejected: Duplicate name");
                sendPlayerIntro(playerIp, RefusalType.DuplicateName);
                return;
            }
        }

        byte passwordLength = data[idx];
        string password = passwordLength == 0 ? null : Encoding.UTF8.GetString(data, idx + 1, passwordLength);
        idx += passwordLength + 1;

        // Ensure the password is correct
        if (!string.IsNullOrEmpty(_password) && (password == null || password != _password))
        {
            Logger.Warn("Player connection rejected: Incorrect password");
            sendPlayerIntro(playerIp, RefusalType.Password);
            return;
        }

        byte team = data[idx];

        // Add new connected player
        Logger.Info("Player connection accepted");
        PlayerInfo newPlayer = new PlayerInfo(name, team);
        connectedPlayers.Add(playerIp, newPlayer);
        if (string.IsNullOrEmpty(_hostIp))
        {
            _hostIp = playerIp;
            Logger.Info($"Designated host: {newPlayer.name} ({playerIp})");
        }
        // Tell the client whether it is the one that must drive enemy simulation.
        sendHostAssign(playerIp);
        sendPlayerConnection(playerIp, true);
        sendPlayerIntro(playerIp, RefusalType.Accepted);
    }

    private byte[] getIntroPacket(byte response)
    {
        return new byte[] { response };
    }

    // Progress

    private void sendPlayerProgress(string playerIp, byte type, byte value, string id)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip && current.team == getCurrentPlayer(ip).team)
            {
                Send(ip, getProgressPacket(current.name, type, value, id), NetworkType.Progress);
            }
        }
    }

    private void receivePlayerProgress(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        byte progressType = data[0];
        byte progressValue = data[1];
        string progressId = Encoding.UTF8.GetString(data, 2, data.Length - 2);

        // Add the progress to the server data, and if it's new send it to the rest of the players
        if (!Core.getTeamData(current.team).AddTeamProgress(progressId, progressType, progressValue))
        {
            Logger.ProgressBad($"Received duplicated or inferior progress from {current.name}: {progressId}, Type {progressType}, Value {progressValue}");
            return;
        }

        if (progressType >= 0 && progressType <= 5)
        {
            // Item
            Logger.ProgressGood($"{(progressValue == 0 ? "Received new" : "Lost an")} item from {current.name}: {progressId}");
        }
        else if (progressType == 6)
        {
            // Stat
            Logger.ProgressGood($"Received new stat upgrade from {current.name}: {progressId} level {progressValue + 1}");
        }
        else if (progressType == 7)
        {
            // Skill
            Logger.ProgressGood($"Received new skill from {current.name}: {progressId}");
        }
        else if (progressType == 8)
        {
            // Map cell
            Logger.ProgressGood($"Received new map cell from {current.name}: {progressId}");
        }
        else if (progressType == 9)
        {
            // Flag
            Logger.ProgressGood($"Received new flag from {current.name}: {progressId}");
        }
        else if (progressType == 10)
        {
            // Pers. object
            Logger.ProgressGood($"Received new pers. object from {current.name}: {progressId}");
        }
        else if (progressType == 11)
        {
            // Teleport
            Logger.ProgressGood($"Received new teleport location from {current.name}: {progressId}");
        }
        else if (progressType == 12)
        {
            // Church donation
            Logger.ProgressGood($"Received new tear donation from {current.name}: {progressValue}");
        }
        else if (progressType == 13)
        {
            // Miriam status
            Logger.ProgressGood($"Received new miriam status from {current.name}: {progressId}");
        }

        // If this is a stat upgrade, might have to do something extra with flask/flaskhealth
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
                sendPlayerProgress(playerIp, 6, (byte)(flaskUpgrades - progressValue), "FLASK");
            }
        }

        sendPlayerProgress(playerIp, progressType, progressValue, progressId);
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

    private void sendPlayerAttack(string playerIp, byte[] attackData)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip && current.isInSameScene(connectedPlayers[ip]))
            {
                // Send this player's attack
                Send(ip, getAttackPacket(current, attackData), NetworkType.Attack);
            }
        }
    }

    private void receivePlayerAttack(string playerIp, byte[] data)
    {
        sendPlayerAttack(playerIp, data);
    }

    private byte[] getAttackPacket(PlayerInfo player, byte[] attackData)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(attackData);
        return bytes.ToArray();
    }

    // Effect

    private void sendPlayerEffect(string playerIp, byte[] effectData)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip && current.isInSameScene(connectedPlayers[ip]))
            {
                // Send this player's effect, name included
                Send(ip, getEffectPacket(current, effectData), NetworkType.Effect);
            }
        }
    }

    private void receivePlayerEffect(string playerIp, byte[] data)
    {
        // The NAME goes in front, exactly as it does for every other relayed payload, and everything the client
        // sent rides behind it. The receiver's first act is to read that name, so a payload relayed without it
        // is not a payload missing a detail - it is an unreadable packet.

        // This began as a rebuild from a single byte - the name plus the effect - which quietly discarded the
        // rest, and adding a position is what made that visible. The first attempt at a fix relayed the raw
        // client bytes instead, which dropped the NAME: the receiver then read the effect byte as the name's
        // LENGTH and threw an out-of-range exception on every effect, freezing the peer's animation mid-stride
        // and detaching its nameplate. Both halves matter - the payload is forwarded WHOLE and PREFIXED.
        sendPlayerEffect(playerIp, data);
    }

    private byte[] getEffectPacket(PlayerInfo player, byte[] effectData)
    {
        List<byte> bytes = addPlayerNameToData(player.name);
        bytes.AddRange(effectData);
        return bytes.ToArray();
    }

    // Ping

    private void SendPing(string playerIp, float time)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(time));

        foreach (var player in getPlayers())
        {
            if (player.Key == playerIp)
                continue;

            string name = player.Value.name;
            ushort ping = player.Value.ping;

            bytes.Add((byte)name.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(name));
            bytes.AddRange(BitConverter.GetBytes(ping));
        }

        Send(playerIp, bytes.ToArray(), NetworkType.Ping);
    }

    private void ReceivePing(string playerIp, byte[] data)
    {
        float time = BitConverter.ToSingle(data, 0);
        ushort ping = BitConverter.ToUInt16(data, 4);

        PlayerInfo current = getCurrentPlayer(playerIp);
        current.ping = ping;

        SendPing(playerIp, time);
    }

    // Enemies

    // Enemy ids are the authored spawn point identity (scene|name|x|y) and travel as a string, so the
    // server only has to relay the payload to the peers sharing the sender's scene.
    private void relayToScene(string playerIp, byte[] data, NetworkType type)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip && current.isInSameScene(connectedPlayers[ip]))
            {
                Send(ip, data, type);
            }
        }
    }

    private void receiveEnemyUpdate(string playerIp, byte[] data)
    {
        // NO host-only filter here, and that is deliberate. Enemy state is per-enemy: each machine owns
        // some enemies and broadcasts only those, so a packet may legitimately come from the peer. An
        // earlier version dropped everything that did not come from the host, which silently discarded
        // every update the peer sent for the enemies it was simulating - so the host stayed the only
        // source of positions and ownership could never take effect.
        //
        // The server deliberately does not interpret the ownership flag: it relays bytes and lets each
        // client decide for itself. Keeping the server ignorant of game rules means the authority scheme
        // can change without touching it.
        relayToScene(playerIp, data, NetworkType.EnemyUpdate);
    }

    private void receiveEnemyDeath(string playerIp, byte[] data)
    {
        // Same reasoning as EnemyUpdate: whoever simulated the enemy is the one that knows it died.
        relayToScene(playerIp, data, NetworkType.EnemyDeath);
    }

    private void receiveEnemyDeathGlobal(string playerIp, byte[] data)
    {
        // Sent to EVERY player, deliberately ignoring the scene filter that relayToScene applies.
        //
        // The player who needs this most is precisely the one who was NOT in the scene when the enemy died:
        // on returning, the game spawns that enemy alive, and without this record their copy contradicts
        // the machine that had it right.
        foreach (string ip in connectedPlayers.Keys)
        {
            if (playerIp != ip)
                Send(ip, data, NetworkType.EnemyDeathGlobal);
        }
    }

    private void receiveEnemyOwnership(string playerIp, byte[] data)
    {
        // The host's ownership token, and only the host may issue one: a peer must not be able to grant
        // itself an enemy. Relayed to the scene because ownership only matters to players sharing it.
        if (playerIp != _hostIp)
            return;

        relayToScene(playerIp, data, NetworkType.EnemyOwnership);
    }
    private void receiveEnemyDamageRequest(string playerIp, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(playerIp);

        // Se il pacchetto arriva dall'Host, diffondi il danno confermato a tutti gli altri client nella stessa scena
        if (playerIp == _hostIp)
        {
            foreach (string ip in connectedPlayers.Keys)
            {
                if (playerIp != ip && current.isInSameScene(connectedPlayers[ip]))
                {
                    Send(ip, data, NetworkType.EnemyDamageRequest);
                }
            }
        }
        else
        {
            // Se arriva da un peer, inoltralo esclusivamente all'Host se connesso per l'approvazione
            if (!string.IsNullOrEmpty(_hostIp) && connectedPlayers.ContainsKey(_hostIp))
            {
                Send(_hostIp, data, NetworkType.EnemyDamageRequest);
            }
        }
    }

    // Authority

    private void sendHostAssign(string playerIp)
    {
        bool isHost = playerIp == _hostIp;
        Send(playerIp, new byte[] { (byte)(isHost ? 1 : 0) }, NetworkType.HostAssign);
    }

    private const byte PROTOCOL_VERSION = 11;
}
