using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Server.Models;
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
///
/// ⭐⭐ C1b-1 — IL FILO E' DIETRO UN'INTERFACCIA, E QUESTA CLASSE NON SA PIU' QUALE SIA.
/// Fino a C1a questa classe era **cucita** a `SimpleTcpServer`: un campo, tre eventi, una `Start` e
/// una `Send`. Quel modello e' quello di un **listener**; un trasporto P2P non ne ha uno (le sessioni
/// si accettano per peer, e i pacchetti arrivano gia' attribuiti a un `CSteamID`). Adesso il filo e'
/// <see cref="IServerTransport"/> e l'unica riga di questa classe che nomina il TCP e' **zero**:
/// il trasporto arriva dal costruttore, lo crea chi avvia il server (`Core.Main` nello standalone,
/// `HostSession` nel client) — e puo' essere <see cref="TcpServerTransport"/> da solo oppure un
/// <see cref="CompositeServerTransport"/> che ne contiene piu' d'uno (domani TCP + P2P).
/// ⚠️ CIO' CHE QUESTO FILE **NON** HA PERSO: la logica (autenticazione, host, scene, relay, replay),
/// il framing `[ushort len][byte type][payload]`, i testi dei log e i byte sul filo. Sono le stesse
/// righe di prima; le sole righe che se ne sono andate sono quelle del socket fisico.
/// </summary>
public class Server
{
    private readonly int _maxPlayers;
    private readonly string _password;

    /// <summary>
    /// ⭐⭐ IL FILO — UN'INTERFACCIA, NON UN SOCKET. Contiene le righe che prima stavano qui dentro
    /// (`new SimpleTcpServer()`, i tre eventi, `Start`, `Send`, `GetListeningIPs`) e nient'altro.
    ///
    /// ⚠️ Non e' mai `null`: arriva dal costruttore e vive quanto il server. Cio' che distingue
    /// "ascolto" da "fermo" non e' piu' "il campo e' `null`" ma <see cref="_listening"/>, perche' un
    /// trasporto iniettato non si puo' "azzerare" senza toglierlo a chi l'ha creato.
    /// </summary>
    private readonly IServerTransport _transport;

    /// <summary>
    /// `true` fra una `Start` riuscita e la `Stop` successiva. Prima questa riga era
    /// `server != null`, e la semantica e' la stessa: un `Start` fallito NON lascia il server "acceso"
    /// (prima azzerava il campo, cosi' `IsRunning` non mentiva e `DelayDisabled` non toccava un
    /// oggetto mai avviato).
    /// </summary>
    private bool _listening;

    /// <summary>
    /// ⭐⭐ LE CHIAVI SONO `PeerId`, NON `string ip` — ed e' TUTTO il senso di C1a.
    ///
    /// Prima era `Dictionary` da stringa a `PlayerInfo`: l'identita' di un giocatore era la stringa che il
    /// socket ci dava (`e.ip`, cioe' `ip:porta`). Il tipo ora e' neutro (`Common\PeerId.cs`): oggi
    /// contiene quella stessa stringa, domani un `CSteamID` — e il server non cambia di una riga.
    /// ⚠️ Nessuna conversione qui: i valori entrano da `PeerId.FromIp` **solo** nel punto di contatto
    /// col socket (`Receive`, `clientConnected`, `clientDisconnected`) e da li' in poi sono `PeerId`.
    /// </summary>
    private Dictionary<PeerId, PlayerInfo> connectedPlayers;

    /// <summary>
    /// Chi e' l'host ADESSO, oppure `null` se non c'e' nessun host. Prima era `string _hostIp` e valeva
    /// `null` quando l'host non c'era: `PeerId?` conserva esattamente quella semantica (e `peer == _hostPeer`
    /// si legge come prima, compreso il caso "nessun host"). Mai `default(PeerId)` come "nessun host":
    /// un valore vuoto sarebbe indistinguibile da un peer vero.
    /// </summary>
    private PeerId? _hostPeer;

    private int _port;

    /// <summary>
    /// ⭐ IL SERVER SI COSTRUISCE **CON** IL SUO FILO. Il trasporto non si crea qui dentro: lo crea
    /// chi avvia il server, e questa e' l'unica differenza visibile del seam in C1b-1.
    ///   · standalone (`Core.Main`): `new Server(maxPlayers, password, trasportoTcp)`;
    ///   · client (`HostSession.TryStart`): idem — **oggi** TCP, esattamente come prima.
    /// Una `Start` fallita non e' un errore del costruttore: il server resta costruito e non acceso.
    /// </summary>
    public Server(int maxPlayers, string password, IServerTransport transport)
    {
        // Change how this works later
        _maxPlayers = maxPlayers;
        _password = password;

        if (transport == null)
            throw new ArgumentNullException(nameof(transport));

        _transport = transport;

        // Gli eventi si sottoscrivono UNA volta, qui: il trasporto vive quanto il server, mentre prima
        // i tre `+=` stavano in `Start` perche' ogni `Start` creava un `SimpleTcpServer` nuovo. Fermare
        // e riavviare lo stesso server non duplica quindi nessun handler.
        _transport.PeerConnected += clientConnected;
        _transport.PeerDisconnected += clientDisconnected;
        _transport.FrameReceived += Receive;
    }

    /// <summary>
    /// `true` se questo oggetto sta ascoltando ADESSO. Nel client e' cio' che distingue "host
    /// avviato" da "host fermo": senza, il bottone potrebbe avviare un secondo trasporto
    /// sulla stessa porta (cioe' il caso "porta occupata" provocato da noi stessi).
    /// </summary>
    public bool IsRunning
    {
        get { return _listening; }
    }

    /// <summary>La porta su cui questo server sta ascoltando (0 se non e' mai partito).</summary>
    public int Port
    {
        get { return _port; }
    }

#if COOP_HOST
    /// <summary>
    /// ⭐⭐ SU QUALI INDIRIZZI STA ASCOLTANDO **DAVVERO** — e la parola "davvero" e' il punto.
    ///
    /// `_port != 0` e `IsRunning` dicono che **noi crediamo** di essere accesi: sono lo stato di
    /// questo oggetto, non quello del sistema operativo. Questa funzione invece chiede all'ascoltatore
    /// (`SimpleTcpServer.GetListeningIPs()`, che enumera i `ServerListener` vivi — dietro
    /// `IServerTransport.ListeningAddresses`, perche' e' una domanda al filo) **quali socket
    /// esistono adesso**, e restituisce la lista. E' la differenza fra "avviato" e "raggiungibile su
    /// quell'indirizzo": con il bind per-NIC del client, `IsRunning` era `true` mentre il loopback non
    /// era fra i listener - cioe' il marker diceva il vero sullo stato e il falso sull'ascolto.
    ///
    /// Serve al marker `[Host] ascolto su=...`: e' la riga che, dal log del gioco, dice se il loopback
    /// e' coperto dal bind. `0.0.0.0` = si'; un elenco di IP = solo quelli (e `127.0.0.1` non c'e').
    ///
    /// Solo nel client (`COOP_HOST`): lo standalone non ha bisogno di questa lettura e la sua
    /// superficie pubblica resta identica.
    /// </summary>
    public string ListeningAddresses()
    {
        if (!_listening)
            return "(fermo)";

        try
        {
            return _transport.ListeningAddresses();
        }
        catch (Exception e)
        {
            // Un guasto di lettura non deve diventare un guasto dell'host: si dice che non si sa.
            return "(illeggibile: " + e.GetType().Name + ")";
        }
    }
#endif

    /// <summary>
    /// I giocatori connessi, indicizzati per <see cref="PeerId"/>. La firma cambia (prima era
    /// `Dictionary` da stringa a `PlayerInfo`) e il chiamante nel client (`HostSession.Players`) ne usa solo
    /// `.Count`, quindi non cambia una riga: il tipo della chiave e' un dettaglio suo.
    /// </summary>
    public Dictionary<PeerId, PlayerInfo> getPlayers()
    {
        return connectedPlayers;
    }

    /// <summary>
    /// ⭐ IL RITARDO DI INVIO DEL FILO (Nagle, dentro `SimpleTCP`): `true` = spento, come oggi.
    /// E' una proprieta' del **trasporto**, non dell'engine: questa resta solo perche' era gia'
    /// pubblica. Con il server fermo il getter rispondeva `false` (il campo era `null`), un dettaglio
    /// che nessuno legge; adesso risponde il valore vero del trasporto.
    /// </summary>
    public bool DelayDisabled
    {
        get { return _transport != null && _transport.DelayDisabled; }
        set
        {
            if (_transport != null)
                _transport.DelayDisabled = value;
        }
    }

    public bool Start(int port)
    {
        try
        {
            // ⭐ UNA RIGA SOLA PER IL FILO. `Start` apre la porta e ritorna `false` quando non si apre
            // (porta occupata). I due casi che questa classe catturava — `SocketException` e
            // `InvalidOperationException("Port was already occupied for all network interfaces")` —
            // ora stanno in `TcpServerTransport.Start`, dove la porta si apre davvero, insieme alle
            // due cose che sono del filo e non del server: il bind **wildcard nel client / per-NIC
            // nello standalone** e lo spegnimento del ritardo interno di `SimpleTCP`.
            // ⚠️ La garanzia per chi chiama non cambia: su porta occupata si ritorna `false`, mai
            // un'eccezione; nello standalone la riga `[Host] porta occupata` e la `Server failed to
            // start on port` della console continuano a essere vere.
            if (!_transport.Start(port))
                return false;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Cintura: il contratto di `IServerTransport.Start` e' di non lanciare, e il server si
            // comporta come se fosse rispettato — `false`, che e' cio' che `Core.Main` e `HostSession`
            // sanno leggere. Un `Start` fallito NON lascia il server acceso.
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
        // L'ordine e' deliberato: prima si stacca "sto ascoltando", poi si ferma il filo. Da questo
        // istante `Send` non manda piu' niente (vedi la guardia li' sotto) mentre `Stop()` sta
        // chiudendo i socket, quindi nessuna scrittura parte su una connessione in smontaggio.
        // Prima questa riga era `server = null`: la semantica e' identica (il campo non poteva essere
        // "azzerato" davvero, perche' il trasporto e' di chi ce l'ha dato).
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

        // La memoria della sessione se ne va con la sessione: `connectedPlayers` resta un dizionario
        // VUOTO e non null, perche' i callback di disconnessione possono ancora arrivare mentre i
        // socket si chiudono e devono trovarlo utilizzabile.
        connectedPlayers = new Dictionary<PeerId, PlayerInfo>();
        _hostPeer = null;
    }

    /// <summary>
    /// ⭐ IL PACCHETTO VERSO UN PEER: `peer` e' la chiave del dizionario, e il filo non sa niente di
    /// tutto questo. Il frame qui sotto e' INVARIATO: `[ushort len][byte type][payload]`, nessun byte
    /// nuovo — ed e' **tutto** cio' che questo metodo fa al messaggio: il trasporto riceve questo
    /// array e lo scrive tale e quale.
    /// </summary>
    private void Send(PeerId peer, byte[] data, NetworkType dataType)
    {
        // ⚠️ `!_listening` significa "fermo" (mai avviato, o `Stop()` appena chiamato). Prima questa
        // guardia era `server == null`; la semantica e' la stessa e serve per la stessa ragione: nel
        // gioco il server puo' essere spento mentre i manager del client stanno ancora girando.
        if (!_listening)
            return;

        if (data != null && data.Length > 0)
        {
            List<byte> list = new List<byte>(BitConverter.GetBytes((ushort)data.Length));
            list.Add((byte)dataType);
            list.AddRange(data);

            try
            {
                // ⭐ IL PUNTO IN CUI L'IDENTITA' TORNA AD ESSERE IL FILO: il trasporto sa che per il
                // TCP `peer.ToString()` e' la stringa IP di prima, quindi trova lo stesso socket.
                _transport.Send(peer, list.ToArray());
            }
            catch (Exception)
            {
                // Testo invariato: `peer.ToString()` e' la stessa stringa che finiva qui prima.
                Logger.Error("Couldn't send data to " + peer.ToString() + "!");
            }
        }
    }

    // Data should be formatted as length length type data
    private void Receive(PeerId peer, byte[] received)
    {
        // ⭐⭐ IL PUNTO DI CONTATTO COL FILO, 1 di 3 — ma `e.ip` qui NON esiste piu': e' il trasporto a
        // consegnare gia' il `PeerId` (`TcpServerTransport` fa `PeerId.FromIp(e.ip)`), e non lo
        // normalizza di proposito perche' quella stringa e' anche la chiave con cui `SimpleTCP` trova
        // il socket a cui scrivere. Da questa riga in poi tutto il server parla di `peer`, mai di
        // stringhe, e `peer` e' esattamente l'argomento che prima si ricavava da `e.ip`.
        int startIdx = 0;
        while (startIdx < received.Length - 3)
        {
            ushort length = BitConverter.ToUInt16(received, startIdx);
            NetworkType type = (NetworkType)received[startIdx + 2];

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
            if (startIdx + 3 + length > received.Length)
            {
                Logger.Error("Received data was formatted incorrectly");
                return;
            }

            byte[] messageData = new byte[length];
            Array.Copy(received, startIdx + 3, messageData, 0, length);

            //Logger.Info($"Received {type} packet: {length} bytes");
            processDataReceived(type, messageData);
            startIdx += 3 + length;
        }
        if (startIdx != received.Length)
            Logger.Error("Received data was formatted incorrectly");

        // Determines which received function to call based on the type
        void processDataReceived(NetworkType type, byte[] data)
        {
            // ⭐⭐ DIFESA 1 — LA LUNGHEZZA DEL PAYLOAD SI CONTROLLA PRIMA DI LEGGERLO. La guardia qui
            // sopra difende l'HEADER del frame; questa difende il PAYLOAD, che era la superficie
            // rimasta aperta: `BitConverter.ToSingle(data, 4)`, `data[0]` e
            // `Encoding.UTF8.GetString(data, 2, ...)` leggono OLTRE la fine se il pacchetto e' piu'
            // corto di cosi', e l'eccezione nascerebbe QUI DENTRO, cioe' nel thread di lettura del
            // trasporto: la stessa classe di guasto che la guardia del framing esiste per evitare
            // (thread che muore, connessione che resta appesa).
            // ⚠️ Un pacchetto malformato NON si lancia: si SCARTA, si dichiara (una volta sola, vedi
            // `reportShortPayload`), e il ciclo prosegue, perche' gli altri frame dello stesso buffer
            // possono essere integri.
            // ⚠️ Un pacchetto VALIDO non passa di qui invano: la tabella dice il MINIMO per tipo, e
            // per i tipi che non hanno un minimo (i relay opachi) vale zero.
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

    // ⭐⭐ LE DUE DIFESE DEL PAYLOAD — 1 di 3: QUANTI BYTE SERVONO, tipo per tipo.
    //
    // Questa tabella e' l'UNICO posto in cui vive il minimo di un pacchetto: ogni `Receive*` legge il
    // suo payload sapendo che il controllo e' gia' passato, e `receivePlayerIntro` (l'unica `Receive*`
    // con una forma variabile) si difende da sola con `TryReadString`.
    // ⚠️ Il valore e' il minimo che il codice legge DAVVERO, non la lunghezza "giusta":
    //   · Position: 2 float (`ToSingle(data, 0)` e `(data, 4)`) = 8 byte;
    //   · Animation / Direction / Team: 1 byte (`data[0]`);
    //   · Progress: 1 byte tipo + 1 byte valore (`data[0]`, `data[1]`); l'id e' la coda e puo' essere
    //     vuoto;
    //   · Ping: 1 float + 1 ushort (`ToSingle(data, 0)`, `ToUInt16(data, 4)`) = 6 byte;
    //   · 0 per i relay opachi (EnterScene, LeaveScene, Skin, Attack, Effect, Enemy*): il server li
    //     inoltra e non li interpreta, quindi non c'e' niente da leggere - e un minimo inventato
    //     scarterebbe pacchetti che oggi passano;
    //   · 0 anche per i tipi che questo server non conosce: li rifiuta lo `switch`, non questa tabella.
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

    // ⭐⭐ 2 di 3: LO SCARTO DICHIARATO, UNA VOLTA E NON A RAFFICA.
    //
    // Un peer ostile (o un pacchetto corrotto) puo' mandare mille payload corti: una riga per pacchetto
    // sarebbe un log che cresce senza limite, cioe' il difetto della crescita illimitata spostato dal
    // dizionario al file. Quindi si scrive quando la notizia e' NUOVA:
    //   · la PRIMA volta per ogni tipo (cosi' il log nomina ogni superficie che qualcuno sta provando);
    //   · quando cambia la FORMA dello scarto (`attesi` diverso = un'altra lettura oltre la fine, e non
    //     e' una ripetizione) - ma solo per i primi `SHORT_PAYLOAD_NEW_SHAPES` scarti del tipo, perche'
    //     una forma che cambia a ogni pacchetto (per esempio `attesi` che cresce con la lunghezza
    //     dichiarata nell'Intro) sarebbe di nuovo una raffica travestita;
    //   · poi solo ogni `SHORT_PAYLOAD_LOG_EVERY` scarti DI QUEL TIPO, con i due conteggi.
    // ⚠️ I contatori sono ARRAY indicizzati dal tipo e non un `Dictionary`: `Receive` puo' girare su
    // piu' thread di lettura (uno per client) e un dizionario mutato da due thread non e' sicuro. Due
    // incrementi persi su un array non fanno danno: spostano solo la riga di log successiva.
    // ⚠️ Il marker si costruisce per CONCATENAZIONE, cosi' il suo letterale resta contiguo e una
    // ricerca a byte (`tools\marker-check.ps1`) lo trova. Prefisso: `[Server] payload corto: `.
    private readonly int[] _shortPayloads = new int[256];

    private readonly int[] _shortPayloadLastExpected = new int[256];

    private int _shortPayloadsTotal;

    private const int SHORT_PAYLOAD_LOG_EVERY = 100;

    private const int SHORT_PAYLOAD_NEW_SHAPES = 8;

    private void reportShortPayload(NetworkType type, int expected, int have)
    {
        // NetworkType e' un enum `byte`: 0..255, cioe' esattamente gli slot degli array.
        int slot = (byte)type;
        int count = ++_shortPayloads[slot];
        int total = ++_shortPayloadsTotal;

        // `attesi` e' sempre >= 1 (si scarta solo quello che serviva), quindi 0 = "mai scritto".
        bool newShape = _shortPayloadLastExpected[slot] != expected && count <= SHORT_PAYLOAD_NEW_SHAPES;
        if (!newShape && count % SHORT_PAYLOAD_LOG_EVERY != 0)
            return;

        _shortPayloadLastExpected[slot] = expected;

        Logger.Warn("[Server] payload corto: tipo=" + type
            + " attesi=" + expected
            + " ho=" + have
            + (count == 1
                ? ""
                : " (scarti di questo tipo: " + count + ", totali: " + total + ")"));
    }

    // ⭐⭐ 3 di 3: I BYTE CI SONO? Le due funzioni con cui un `Receive*` non legge oltre la fine.
    //
    // `HasBytes` e' il controllo secco: `data` ha almeno `start + count` byte? Se no, il pacchetto e'
    // corto: si scarta e lo si dichiara, con `attesi` = i byte che servivano fino a quel punto e
    // `ho` = quelli arrivati. `false` = chi chiama si ferma, senza lanciare niente.
    private bool HasBytes(byte[] data, int start, int count, NetworkType type)
    {
        if (data != null && start >= 0 && count >= 0 && start + count <= data.Length)
            return true;

        reportShortPayload(type, start + count, data == null ? 0 : data.Length);
        return false;
    }

    // Il campo `[byte lunghezza][lunghezza byte di testo]`, letto DOPO aver controllato che ci sia:
    // la lunghezza dichiarata dal filo non supera MAI la fine del payload.
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

    // ⭐⭐ LA TERZA SUPERFICIE DEL PROGRESSO — 1 di 3: I DUE TETTI, DICHIARATI IN UN POSTO SOLO.
    //
    // La lunghezza del payload non era l'unico modo di far nascere un'eccezione DENTRO il thread di
    // lettura: il VALORE del pacchetto `Progress` ne era un altro, e nessuna delle due difese di sopra
    // lo copriva (una guarda i byte, l'altra il framing).
    //
    // · IL TIPO. `Core.getTeamData(...).AddTeamProgress` fa `progressSets[progressType]`, e `progressSets`
    //   e' un array di `TeamInfo.NUMBER_OF_PROGRESS_TYPES` slot indicizzato dal `byte` del filo: un tipo
    //   >= 14 lanciava `IndexOutOfRangeException` dentro `receivePlayerProgress`, cioe' nello stesso
    //   punto — e con lo stesso esito — che il framing e il payload corto sono stati scritti per evitare:
    //   thread che muore, connessione che resta appesa. Il tetto e' `NUMBER_OF_PROGRESS_TYPES`, la STESSA
    //   costante che dimensiona l'array, cosi' non sono due numeri che possono divergere; e il dominio
    //   che ne esce (0..13) e' esattamente quello di `ProgressType` (`Common\Enums\ProgressType.cs`),
    //   cioe' quello che il client scrive davvero sul filo (`(byte)progress.Type`).
    // · L'ID. La coda del payload non ha prefisso di lunghezza e non aveva tetto, mentre OGNI id nuovo
    //   crea una voce in `progressSets[type]`: durante una sessione un peer poteva far crescere il
    //   dizionario quanto voleva. Il tetto e' in BYTE del filo (non in caratteri: `progressId.Length`
    //   sarebbe la lunghezza UTF-16, che per un id non ASCII non e' quella spedita), ed e' 256:
    //   ~5 volte l'id piu' lungo che il client puo' mandare oggi (misurato: 49 caratteri in
    //   `Data\FlagStates.cs`), quindi nessun pacchetto valido si avvicina al tetto.
    // ⚠️ Come per il payload corto, oltre il tetto NON si lancia: si SCARTA il pacchetto e lo si dichiara
    // (una volta sola, vedi `reportRejectedProgress`). E come per il corto, un pacchetto valido non passa
    // di qui invano: nessun byte sul filo cambia.
    private const byte MAX_PROGRESS_TYPE = (byte)(TeamInfo.NUMBER_OF_PROGRESS_TYPES - 1);

    private const int MAX_PROGRESS_ID_LENGTH = 256;

    // ⭐⭐ 2 di 3: LO STESSO RATE LIMIT DEL PAYLOAD CORTO, per la stessa ragione — un peer ostile puo'
    // mandare mille progressi invalidi, e una riga per pacchetto sposterebbe la crescita illimitata dal
    // dizionario al file di log (nel client: il log della mod). Prima riga per ogni motivo, poi una ogni
    // `REJECTED_PROGRESS_LOG_EVERY` scarti DI QUEL MOTIVO, con i due conteggi.
    // ⚠️ I contatori sono campi e non variabili locali perche' `Receive` puo' girare su piu' thread di
    // lettura (uno per client); due incrementi persi fanno solo spostare la riga successiva.
    // ⚠️ Il marker si costruisce per CONCATENAZIONE, cosi' il letterale resta contiguo e
    // `tools\marker-check.ps1` lo trova. Prefisso: `[Server] progresso scartato: `.
    private readonly int[] _rejectedProgress = new int[2];

    private int _rejectedProgressTotal;

    private const int REJECTED_PROGRESS_LOG_EVERY = 100;

    // Gli indici dei due motivi: sono gli slot di `_rejectedProgress`.
    private const int PROGRESS_REJECT_TYPE = 0;

    private const int PROGRESS_REJECT_ID = 1;

    private void reportRejectedProgress(int reason, string detail)
    {
        int count = ++_rejectedProgress[reason];
        int total = ++_rejectedProgressTotal;

        if (count != 1 && count % REJECTED_PROGRESS_LOG_EVERY != 0)
            return;

        Logger.Warn("[Server] progresso scartato: " + detail
            + (count == 1
                ? ""
                : " (scarti di questo motivo: " + count + ", totali: " + total + ")"));
    }

    /// <summary>
    /// ⭐ PUNTO DI CONTATTO COL FILO, 2 di 3. Il `peer` arriva **gia'** come <see cref="PeerId"/> dal
    /// trasporto (che e' l'unico a sapere che per il TCP e' `ip:porta`), e il testo e' byte per byte
    /// quello di prima: `peer.ToString()` rende la stessa stringa che c'era in `e.ip`.
    /// </summary>
    private void clientConnected(PeerId peer)
    {
        Logger.Info("Client connected at " + peer.ToString());
    }

    /// <summary>
    /// ⭐ PUNTO DI CONTATTO COL FILO, 3 di 3. Stessa cosa: `e.ip` non esiste piu' qui, il `PeerId` e'
    /// gia' l'identita' con cui il server indicizza tutto — e `peer.ToString()` restituisce la stessa
    /// stringa di prima.
    /// </summary>
    private void clientDisconnected(PeerId peer)
    {
        Logger.Info("Client disconnected at " + peer.ToString());

        // Make sure this client was actually connected, not just rejected from server
        if (!connectedPlayers.ContainsKey(peer))
            return;

        // Send that this player has disconnected & remove them
        sendPlayerConnection(peer, false);
        connectedPlayers.Remove(peer);
        Core.removeUnusedGameData(connectedPlayers);

        // ⭐ `peer == _hostPeer` e' il confronto di prima (`e.ip == _hostIp`), con `_hostPeer` che vale
        // `null` quando l'host non c'e': l'operatore sollevato su `PeerId?` tratta il null come "diverso"
        // esattamente come faceva `==` fra stringhe con un lato null.
        if (peer == _hostPeer)
        {
            // ⭐ IL PRIMO RIMASTO E' IL NUOVO HOST — con la STESSA scelta di prima (`FirstOrDefault` su
            // `Keys`, cioe' il primo in ordine di enumerazione del dizionario). Il `foreach` esplicito
            // esiste perche' `FirstOrDefault` su `PeerId` renderebbe `default(PeerId)`, che NON e' "nessuno":
            // un peer vuoto sarebbe indistinguibile da un peer vero.
            PeerId? next = null;
            foreach (PeerId remaining in connectedPlayers.Keys)
            {
                next = remaining;
                break;
            }
            _hostPeer = next;

            if (_hostPeer.HasValue)
            {
                // Testo invariato: `{_hostPeer.Value}` chiama `ToString()`, cioe' la stessa stringa IP.
                Logger.Info($"Host reassigned to: {connectedPlayers[_hostPeer.Value].name} ({_hostPeer.Value})");

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
                // recipient (isHost = peer == _hostPeer), and a client that is not the host and never was
                // receives SetHost(false), which changes nothing at all.
                foreach (PeerId other in connectedPlayers.Keys)
                    sendHostAssign(other);
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
                // Send this player's updated position
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
                // Send this player's updated animation
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
                // Send this player's updated direction
                Send(other, GetDirectionPacket(current), NetworkType.Direction);
            }
        }
    }

    private void ReceiveDirection(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);

        // ⚠️ PORTING net35 — LA SECONDA RIGA CHE NON COMPILAVA, e non era nell'elenco dello studio.
        // Prima era `BitConverter.ToBoolean(data)`, cioe' l'overload a UN argomento: quello esiste
        // solo da .NET Core in poi. `net35` ha solo `ToBoolean(byte[], int)`, che vuole l'indice.
        // Il significato e' identico - su un solo byte "diverso da zero" vuol dire `true` - quindi si
        // legge il byte e lo si confronta: stesso valore, stessa direzione, stesso pacchetto.
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

    private void sendPlayerEnterScene(PeerId peer) // Optimize these send functions to combine them together
    {
        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                // Send that this player has entered a new scene
                Send(other, getScenePacket(current), NetworkType.EnterScene);

                if (current.isInSameScene(connectedPlayers[other]))
                {
                    // If in same scene, also send position data to each one
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
                // Send that this player has left their old scene
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
                // Send this player's updated skin
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

    /// <summary>
    /// ⭐⭐ DIFESA 2 — L'INTERVALLO VALIDO DI `team`, e vive in UN SOLO POSTO.
    ///
    /// `team` e' un `byte` che arriva DAL FILO (`receivePlayerIntro` e `receivePlayerTeam`), e prima
    /// d'ora nessuno lo validava: `Core.getTeamData` crea una voce per QUALUNQUE valore, mentre la
    /// pulizia (`Core.removeUnusedGameData`) ne guardava solo 1..10 — cioe' le squadre 0 e 11..255 non
    /// venivano MAI rimosse, e il dizionario cresceva per tutta la vita del processo (nel client: il
    /// processo del gioco, che vive per ore).
    /// ⇒ 1..10 e' l'intervallo vero (10 = i posti/squadre del menu, `HostSession.MAX_PLAYERS`), e i due
    /// ingressi passano da `sanitizeTeam`. La pulizia, dal canto suo, ora scansiona TUTTI i valori
    /// possibili della chiave (0..255), cosi' nessuna squadra puo' restare immortale nemmeno se un
    /// giorno l'intervallo cambia.
    /// </summary>
    private const byte MIN_TEAM = 1;
    private const byte MAX_TEAM = 10;

    /// <summary>
    /// ⭐⭐ LA SCELTA DICHIARATA: **NORMALIZZARE**, non rifiutare.
    ///
    /// Un `team` fuori da 1..10 viene portato a <see cref="MIN_TEAM"/> e DICHIARATO con un marker.
    /// Rifiutare avrebbe voluto dire o un nuovo valore di `RefusalType` (cioe' un cambio del protocollo,
    /// che questo giro non fa) o riusare un rifiuto che parla d'altro (`Protocol`, `DuplicateName`): un
    /// log che dice una cosa falsa e' peggio di un log che dice cosa e' successo. Normalizzando, il
    /// giocatore entra (come oggi, quando `team` era quasi sempre 1) e il server non impara nessuna
    /// chiave nuova: nessuna squadra immortale.
    /// </summary>
    private byte sanitizeTeam(byte team)
    {
        if (team >= MIN_TEAM && team <= MAX_TEAM)
            return team;

        Logger.Warn("[Server] team fuori range: valore=" + team
            + " (validi " + MIN_TEAM + ".." + MAX_TEAM + ") -> normalizzato a " + MIN_TEAM);
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

        // Send all of the server data to this player for them to merge
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
        // ⭐ DIFESA 2 sull'ingresso del pacchetto `Team`: la squadra che entra nello stato e' SOLO
        // quella valida. Un valore fuori da 1..10 verrebbe altrimenti memorizzato e usato come chiave
        // di `Core.getTeamData` - cioe' una squadra che la pulizia non sapeva nemmeno di dover cercare.
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
        // Send message to all other connected ips
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

        // Only send rest of data if successful connection
        if (response > 0)
            return;

        PlayerInfo current = getCurrentPlayer(peer);
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
            {
                // Send all other connected players and their important data
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

        // Ensure the server doesn't have max number of players
        if (connectedPlayers.Count >= _maxPlayers)
        {
            Logger.Warn("Player connection rejected: Player limit reached");
            sendPlayerIntro(peer, RefusalType.PlayerLimit);
            return;
        }

        // Ensure there are no duplicate ips
        if (connectedPlayers.ContainsKey(peer))
        {
            Logger.Warn("Player connection rejected: Duplicate ip address");
            sendPlayerIntro(peer, RefusalType.DuplicateIp);
            return;
        }

        // ⭐⭐ DIFESA 1 sull'Intro: e' l'unica `Receive*` con una forma VARIABILE (il byte di protocollo,
        // TRE stringhe `[len][byte]` e il byte della squadra), quindi non la copre la tabella
        // `MinimumPayloadLength`: ogni lettura passa da `HasBytes`/`TryReadString`, che controllano i
        // byte PRIMA di leggerli. Un payload corto si ferma qui, dichiarato, e il thread di lettura
        // resta vivo. Nessun byte cambia per un pacchetto valido: gli stessi byte, letti nell'ordine di
        // prima (protocollo, stanza, nome, password, squadra).
        if (!HasBytes(data, idx, 1, NetworkType.Intro))
            return;

        byte protocol = data[idx++];

        // Ensure the protocol version matches
        // ⭐ LA COSTANTE E' UNA SOLA, e non vive piu' in fondo a questo file: sta in
        // `Common\Protocol.cs`, cioe' nella DLL che client e server condividono. Due definizioni
        // separate dello stesso numero potevano divergere; una sola non puo'.
        if (protocol != Protocol.PROTOCOL_VERSION)
        {
            Logger.Warn("Player connection rejected: Protocol version doesn't match");
            sendPlayerIntro(peer, RefusalType.Protocol);
            return;
        }

        // La stanza non si usa (e' informativa): si legge per farla scorrere, ma si legge DOPO il
        // controllo dei byte. `out _` perche' il valore non serve, come prima.
        if (!TryReadString(data, ref idx, NetworkType.Intro, out _))
            return;

        if (!TryReadString(data, ref idx, NetworkType.Intro, out string name))
            return;

        // Ensure there are no duplicate names
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

        // La password vuota resta `null` come prima: `string.IsNullOrEmpty(_password)` distingue il caso
        // "nessuna password impostata" da "password sbagliata", e la stringa vuota non lo farebbe.
        string password = passwordRaw.Length == 0 ? null : passwordRaw;

        // Ensure the password is correct
        if (!string.IsNullOrEmpty(_password) && (password == null || password != _password))
        {
            Logger.Warn("Player connection rejected: Incorrect password");
            sendPlayerIntro(peer, RefusalType.Password);
            return;
        }

        if (!HasBytes(data, idx, 1, NetworkType.Intro))
            return;

        // ⭐ DIFESA 2: la squadra che entra nello stato e' solo quella valida (vedi `sanitizeTeam`).
        // Prima questo byte andava DENTRO `PlayerInfo` cosi' com'era - fino a 255, e la pulizia ne
        // cercava solo 10.
        byte team = sanitizeTeam(data[idx]);

        // Add new connected player
        Logger.Info("Player connection accepted");
        PlayerInfo newPlayer = new PlayerInfo(name, team);
        connectedPlayers.Add(peer, newPlayer);
        if (!_hostPeer.HasValue)
        {
            _hostPeer = peer;
            Logger.Info($"Designated host: {newPlayer.name} ({peer})");
        }
        // Tell the client whether it is the one that must drive enemy simulation.
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

        // ⭐⭐ LE DUE DIFESE DEL PROGRESSO — 3 di 3: il TIPO si controlla PRIMA di indicizzare l'array.
        // `AddTeamProgress` fa `progressSets[progressType]` su `NUMBER_OF_PROGRESS_TYPES` slot: un tipo
        // piu' grande lanciava `IndexOutOfRangeException` DENTRO questo thread. Qui si scarta, e il
        // pacchetto non viene ne' memorizzato ne' inoltrato.
        if (progressType > MAX_PROGRESS_TYPE)
        {
            reportRejectedProgress(PROGRESS_REJECT_TYPE,
                "tipo=" + progressType + " (validi 0.." + MAX_PROGRESS_TYPE + ")");
            return;
        }

        // ...e l'ID si misura in BYTE del filo, prima di decodificarlo: e' la lunghezza che il peer ha
        // spedito, non quella UTF-16 della stringa che ne esce.
        int progressIdLength = data.Length - 2;
        if (progressIdLength > MAX_PROGRESS_ID_LENGTH)
        {
            reportRejectedProgress(PROGRESS_REJECT_ID,
                "id lungo " + progressIdLength + " byte (massimo " + MAX_PROGRESS_ID_LENGTH
                + ") tipo=" + progressType);
            return;
        }

        string progressId = Encoding.UTF8.GetString(data, 2, progressIdLength);

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
                // Send this player's attack
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
                // Send this player's effect, name included
                Send(other, getEffectPacket(current, effectData), NetworkType.Effect);
            }
        }
    }

    private void receivePlayerEffect(PeerId peer, byte[] data)
    {
        // The NAME goes in front, exactly as it does for every other relayed payload, and everything the client
        // sent rides behind it. The receiver's first act is to read that name, so a payload relayed without it
        // is not a payload missing a detail - it is an unreadable packet.

        // This began as a rebuild from a single byte - the name plus the effect - which quietly discarded the
        // rest, and adding a position is what made that visible. The first attempt at a fix relayed the raw
        // client bytes instead, which dropped the NAME: the receiver then read the effect byte as the name's
        // LENGTH and threw an out-of-range exception on every effect, freezing the peer's animation mid-stride
        // and detaching its nameplate. Both halves matter - the payload is forwarded WHOLE and PREFIXED.
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

    // Enemy ids are the authored spawn point identity (scene|name|x|y) and travel as a string, so the
    // server only has to relay the payload to the peers sharing the sender's scene.
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
        // NO host-only filter here, and that is deliberate. Enemy state is per-enemy: each machine owns
        // some enemies and broadcasts only those, so a packet may legitimately come from the peer. An
        // earlier version dropped everything that did not come from the host, which silently discarded
        // every update the peer sent for the enemies it was simulating - so the host stayed the only
        // source of positions and ownership could never take effect.
        //
        // The server deliberately does not interpret the ownership flag: it relays bytes and lets each
        // client decide for itself. Keeping the server ignorant of game rules means the authority scheme
        // can change without touching it.
        relayToScene(peer, data, NetworkType.EnemyUpdate);
    }

    private void receiveEnemyDeath(PeerId peer, byte[] data)
    {
        // Same reasoning as EnemyUpdate: whoever simulated the enemy is the one that knows it died.
        relayToScene(peer, data, NetworkType.EnemyDeath);
    }

    private void receiveEnemyDeathGlobal(PeerId peer, byte[] data)
    {
        // Sent to EVERY player, deliberately ignoring the scene filter that relayToScene applies.
        //
        // The player who needs this most is precisely the one who was NOT in the scene when the enemy died:
        // on returning, the game spawns that enemy alive, and without this record their copy contradicts
        // the machine that had it right.
        foreach (PeerId other in connectedPlayers.Keys)
        {
            if (peer != other)
                Send(other, data, NetworkType.EnemyDeathGlobal);
        }
    }

    private void receiveEnemyOwnership(PeerId peer, byte[] data)
    {
        // The host's ownership token, and only the host may issue one: a peer must not be able to grant
        // itself an enemy. Relayed to the scene because ownership only matters to players sharing it.
        if (peer != _hostPeer)
            return;

        relayToScene(peer, data, NetworkType.EnemyOwnership);
    }
    private void receiveEnemyDamageRequest(PeerId peer, byte[] data)
    {
        PlayerInfo current = getCurrentPlayer(peer);

        // Se il pacchetto arriva dall'Host, diffondi il danno confermato a tutti gli altri client nella stessa scena
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
            // Se arriva da un peer, inoltralo esclusivamente all'Host se connesso per l'approvazione
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
