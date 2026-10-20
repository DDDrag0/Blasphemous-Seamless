using Blasphemous.ModdingAPI;
using Blasphemous.ModdingAPI.Helpers;
using Blasphemous.Multiplayer.Client.Host;
using Blasphemous.Multiplayer.Client.InputValidation;
using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// Controls all GUI windows on screen
/// </summary>
public class ConnectionDisplay : MonoBehaviour
{
    private readonly IValidator _validator;
    private readonly ISanitizer _sanitizer;

    private bool _attemptingConnection = false;
    private bool _firstShowing = true;

    private ConnectionInfo _connection = new();

    // New variables (Window framework)
    private Rect _window;
    private bool _open = false;

    public ConnectionDisplay()
    {
        var sv = new StandardValidator();
        _validator = sv;
        _sanitizer = sv;
    }

    private void SetOpenStatus(bool open)
    {
        if (_open == open)
            return;

        _open = open;
        Core.Input.SetBlocker("MULTIPLAYER", _open);
    }

    private void OnGUI()
    {
        if (!SceneHelper.GameSceneLoaded)
        {
            Cursor.visible = false;
            SetOpenStatus(false);
            return;
        }

        // Temp since debug mod hides the cursor
        Cursor.visible = true;

        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
            SetOpenStatus(_window.Contains(e.mousePosition));

        int ypos = Screen.height - (_open ? HEIGHT : 17);
        _window = GUI.Window(99, new Rect(240, ypos, WIDTH, HEIGHT + 20), MultiplayerWindow, "Multiplayer connection info");
    }

    private void MultiplayerWindow(int windowID)
    {
        // ⭐ STEAM — L'INVITO NON SI APPLICA MENTRE SI E' GIA' DENTRO UNA SESSIONE.
        //
        // Qui siamo nel punto che vede TUTTI gli stati della schermata (connesso / in connessione /
        // libero), quindi e' qui che si buttano via le azioni Steam che non hanno piu' senso: un
        // invito arrivato mentre si giocava gia' non deve riconnettersi da solo al prossimo
        // "Disconnect" (sarebbe una connessione a sorpresa, mezz'ora dopo), e una lobby d'host creata
        // ma mai avviata non deve restare appesa annunciando una porta chiusa.
        //
        // Quando non c'e' niente in attesa questa riga costa un confronto booleano e non stampa
        // niente: il percorso di chi non usa Steam resta identico.
        if (Main.Multiplayer.NetworkManager.IsConnected)
        {
            SteamLobby.DropPendingWhileConnected();
            DisplayEnd();
        }
        else if (_attemptingConnection)
        {
            DisplayMiddle();
        }
        else
        {
            DisplayStart();
        }
    }

    private void DisplayStart()
    {
        if (_firstShowing)
            _connection = Main.Multiplayer.LastConnectionInfo;

        // Clean server ip
        string server = ReadTextField("Server IP:", _connection.ServerIp, 0);
        if (_firstShowing || server != _connection.ServerIp)
        {
            server = _sanitizer.CleanServer(server);
        }

        // Clean room name
        //string room = ReadTextField("Room name:", _connection.RoomName, 1);
        //if (_firstShowing || room != _connection.RoomName)
        //{
        //    room = _sanitizer.CleanRoom(room);
        //}

        // Not until room system is implemented
        string room = "a";

        // Clean player name
        string player = ReadTextField("Player name:", _connection.PlayerName, 2);
        if (_firstShowing || player != _connection.PlayerName)
        {
            player = _sanitizer.CleanPlayer(player);
        }

        // Clean password
        string password = ReadTextField("Password:", _connection.Password, 3);
        if (_firstShowing || password != _connection.Password)
        {
            password = _sanitizer.CleanPassword(password);
        }

        // Clean team number
        int team = ReadChoiceBox("Team number:", _connection.TeamNumber - 1, Enumerable.Range(1, 8).Select(x => x.ToString()).ToArray(), 4) + 1;
        if (_firstShowing || team != _connection.TeamNumber)
        {
            team = _sanitizer.CleanTeam(team);
        }

        _connection = new ConnectionInfo(server, room, player, password, (byte)team);
        _firstShowing = false;

        if (ReadButton("Connect", 5))
            ValidateAndConnect();

        // ⭐⭐ "HOST SESSION" — IL SERVER DENTRO IL CLIENT.
        //
        // E' un bottone di QUESTA schermata, che e' UI DELLA MOD (un `OnGUI` in questo file), non del
        // gioco: nessun menu di Blasphemous e' stato toccato, nessun bersaglio Harmony e' cambiato.
        //
        // Sta di fianco a "Connect" e non al posto suo: le due strade sono diverse e restano tutte e
        // due aperte. "Connect" entra in una sessione che esiste gia' (un `SERVER-COOP` esterno su
        // questa macchina o su un'altra); "Host session" apre la porta qui e poi entra in quella.
        //
        // Quando l'host e' acceso il bottone diventa "Stop host": premere due volte "Host session"
        // avvierebbe un secondo server sulla stessa porta, cioe' il caso "porta occupata" creato da
        // noi stessi.
        if (HostSession.IsRunning)
        {
            if (ReadButton("Stop host", 5, 1))
            {
                HostSession.Stop();

                // ⭐ E LA LOBBY SE NE VA CON L'HOST. Una lobby che resta aperta e' peggio di un
                // errore: annuncia una porta dove non ascolta piu' nessuno, e chi la vede
                // dall'overlay di Steam entra in un vicolo cieco. (`LeaveLobby` non fa niente e non
                // stampa niente se non stavamo ospitando via Steam.)
                SteamLobby.LeaveLobby();
            }

            ShowLabel("Host attivo: porta " + HostSession.Port + " - giocatori " + HostSession.Players, 6);
        }
        else if (ReadButton("Host session", 5, 1))
        {
            HostAndConnect(room, player, password, (byte)team);
        }

        // ⭐⭐ STEAM — L'INVITO, ADDITIVO, SOTTO I BOTTONI CHE C'ERANO GIA'.
        // I bottoni qui sopra ("Connect", "Host session" / "Stop host") NON cambiano parola, ordine,
        // posizione ne' comportamento: chi non usa Steam non vede nessuna differenza. Questo blocco
        // aggiunge solo un comando in piu' e consuma le due cose che arrivano dalle callback di Steam
        // (lobby pronta / dati dell'invito pronti).
        DisplaySteam(room, player, password, (byte)team);
    }

    /// <summary>
    /// ⭐⭐ IL BLOCCO STEAM DELLA SCHERMATA DI CONNESSIONE. Tre cose, in quest'ordine:
    ///
    ///   ① **le azioni che arrivano dalle callback**, consumate qui (una volta sola, vedi
    ///     `SteamLobby.TryConsumeHostLobbyReady` / `TryConsumeJoin`);
    ///   ② **il bottone** "Steam: Host" (o "Steam: invita" se una lobby c'e' gia');
    ///   ③ **lo stato**, in una riga.
    ///
    /// ⚠️ SE STEAM NON C'E' (client spento, offline, nessun account) IL BOTTONE E' **DISABILITATO**,
    /// non nascosto: nasconderlo farebbe credere che la funzione non esista, disabilitarlo dice la
    /// verita' - c'e', e adesso non si puo' usare. In nessun caso si lancia un'eccezione: tutte le
    /// chiamate a `SteamLobby` sono in `try/catch` al suo interno e ritornano un booleano.
    ///
    /// ⚠️ E IL CAMPO DEL NOME NON SI TOCCA: il nome che l'host pubblica e quello che l'ospite usa sono
    /// quelli scritti qui dall'utente, letti e sanitizzati esattamente come prima (`room`, `player`,
    /// `password`, `team` arrivano da `DisplayStart`).
    /// </summary>
    private void DisplaySteam(string room, string player, string password, byte team)
    {
        bool steam = SteamLobby.Available;

        // ① L'INVITO ACCETTATO DA UN AMICO: i dati della lobby (hostIp/hostPort) sono pronti — letti
        //    subito in `LobbyEnter_t` dalla cache, oppure dopo `LobbyDataUpdate_t` quando la cache era
        //    vuota (vedi `SteamLobbyImpl.TryReadJoinData`). Adesso si possono applicare: si compilano
        //    i campi e si connette, che e' il "un clic invece di digitare un IP".
        string joinIp;
        int joinPort;
        if (SteamLobby.TryConsumeJoin(out joinIp, out joinPort))
        {
            ApplySteamJoin(joinIp, joinPort, room, player, password, team);

            // ⚠️ Dopo aver applicato un invito NON si legge il bottone in questo frame: il click
            // potrebbe essere un residuo di layout, e la connessione e' gia' partita.
            return;
        }

        // ①bis LA LOBBY E' PRONTA (`LobbyCreated_t` con esito OK): si avvia la sessione host con lo
        //    STESSO percorso del bottone "Host session", non con una copia.
        if (SteamLobby.TryConsumeHostLobbyReady())
        {
            HostAndConnect(room, player, password, team);

            if (HostSession.IsRunning)
            {
                Main.Multiplayer.NotificationManager.DisplayNotification(
                    "Steam: lobby aperta, invita un amico dall'overlay");
            }
            else
            {
                // L'host NON e' partito (porta occupata o input non valido): la lobby annuncerebbe
                // una porta dove non ascolta nessuno, quindi si chiude subito. L'errore l'ha gia'
                // detto `HostAndConnect` con la sua notifica di sempre.
                SteamLobby.LeaveLobby();
            }

            return;
        }

        // ② I BOTTONI, SULLA STESSA RIGA. A sinistra quello di Steam (lobby/invito), a destra la
        //    sonda P2P: sono due cose diverse e restano due comandi distinti, cosi' premere la sonda
        //    non apre una lobby e aprire una lobby non accende la sonda.
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && steam;

        bool clicked = ReadSteamButton(HostSession.IsRunning && SteamLobby.HasHostLobby
            ? "Steam: invita"
            : "Steam: Host");

        bool probeClicked = ReadProbeButton(SteamP2PProbe.Active ? "Stop probe" : "Probe P2P");

        GUI.enabled = wasEnabled;

        if (clicked)
        {
            // Se una lobby e' gia' nostra, `StartHost` non ne crea una seconda: riapre l'overlay
            // degli inviti (e lo dice nel log).
            if (!SteamLobby.StartHost(player))
            {
                Main.Multiplayer.NotificationManager.DisplayNotification(
                    "Steam: non disponibile (vedi log)");
            }
        }

        // ②bis ⭐⭐ "PROBE P2P" — LA SONDA. Non connette niente e non manda niente al gioco: manda
        //     **1 byte** ai peer della lobby ogni secondo e ogni 3 secondi scrive nel log una riga per
        //     peer (`relay=0` = diretto, `relay=1` = in relay). Serve a misurare se il P2P di Steam
        //     funziona tra due macchine **anche quando il TCP non arriva** (secondo PC su un hotspot),
        //     che e' il caso in cui questa misura serve davvero.
        //
        //     ⚠️ La sonda NON ha bisogno di essere connessi al gioco: l'unico requisito e' una lobby
        //     Steam (ospitata o accettata). Se non c'e', resta accesa e comincia da sola quando arriva.
        //
        //     ⚠️ E IL CORPO DEL CLIC E' UNO SOLO (`HandleProbeClick`): lo chiama anche `DisplayEnd`,
        //     cioe' la schermata di chi e' hostato/connesso. Una sonda e' uno STRUMENTO DI MISURA:
        //     deve restare a portata anche mentre si e' dentro una sessione, ed e' proprio quello il
        //     momento in cui c'e' qualcosa da misurare. Due copie del corpo divergerebbero.
        if (probeClicked)
            HandleProbeClick();

        // ③ LO STATO, una riga sola e corta (la larghezza del pannello e' 330 px).
        //
        // ⭐ L'ID DELLA LOBBY VA A SCHERMO, e non e' cosmesi: e' il numero che serve per provare a
        // mano il caso "gioco spento" (opzioni di avvio di Steam: `+connect_lobby <id>`). Senza,
        // bisognerebbe andarlo a cercare nel log per poter fare il test.
        if (!steam)
            ShowLabel("Steam: non disponibile (offline)", STEAM_STATUS_LINE);
        else if (SteamLobby.HasHostLobby)
            ShowLabel("Lobby " + SteamLobby.HostLobbyId, STEAM_STATUS_LINE);

        // ④ ⭐ LA RIGA DELLA SONDA, e c'e' SOLO mentre la sonda e' accesa: chi non la usa non vede
        //    nessuna riga in piu' e la schermata resta identica a prima.
        //
        //    E' la stessa risposta del log in forma breve (quanti peer, quanti in relay), perche' chi
        //    fa il test puo' non avere il log davanti: `relay=0`/`diretto` = i due client si parlano
        //    senza server di Steam, `relay=1` = passano dal relay, `attive=0` = non si sono connessi.
        string probe = SteamP2PProbe.Status();
        if (!string.IsNullOrEmpty(probe))
            ShowLabel(probe, P2P_STATUS_LINE);
    }

    /// <summary>
    /// ⭐⭐ IL CLIC DELLA SONDA, IN UN POSTO SOLO — lo chiamano **due** schermate: `DisplayStart`
    /// (sconnesso: riga dei bottoni Steam) e `DisplayEnd` (hostato/connesso: riga di "Disconnect").
    ///
    /// PERCHE' DUE SCHERMATE E UNA SOLA FUNZIONE: la sonda e' una **misura**, non un comando di
    /// connessione. Se vivesse solo nella schermata di chi non e' connesso, per misurarla bisognerebbe
    /// **togliere l'host** - cioe' distruggere la cosa che si sta misurando. Quindi il bottone c'e' in
    /// entrambi gli stati, e il corpo del clic e' questo, identico per tutti e due.
    ///
    /// Cosa fa, in ordine (ed e' quello che faceva prima, parola per parola):
    ///   · sonda ACCESA  => `Stop` e notifica "sonda fermata";
    ///   · sonda SPENTA  => `Start`; `true` = accesa (anche senza lobby: aspetta e comincia da sola),
    ///     `false` = Steam non c'e' (o la registrazione della callback e' fallita) e la UI lo dice.
    ///
    /// ⚠️ NON tocca la connessione, il trasporto, il protocollo, la lobby ne' `NetworkManager`: manda
    /// un byte ai peer della lobby e scrive `[P2P] ...` nel log. Premuto da hostato o da sconnesso,
    /// l'effetto e' lo stesso.
    /// </summary>
    private void HandleProbeClick()
    {
        if (SteamP2PProbe.Active)
        {
            SteamP2PProbe.Stop();
            Main.Multiplayer.NotificationManager.DisplayNotification("P2P: sonda fermata (vedi log)");
        }
        else if (SteamP2PProbe.Start())
        {
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "P2P: sonda accesa, guarda il log ([P2P] peer=...)");
        }
        else
        {
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "P2P: non disponibile (vedi log)");
        }
    }

    /// <summary>
    /// ⭐ IL BOTTONE DELLA SONDA NELLA SCHERMATA DI CHI E' GIA' DENTRO (host o ospite): la stessa
    /// casella a destra gia' usata da "Host session" sulla riga dei bottoni IP (slot 1), quindi **una
    /// riga sola, nessuna riga in piu'** e nessuna casella rubata a "Disconnect", che resta a sinistra.
    ///
    /// ⚠️ DISABILITATO, NON NASCOSTO, SE STEAM NON C'E' (`SteamLobby.Available`): e' la regola gia'
    /// scritta per il bottone di Steam - nascondere fa credere che la funzione non esista, disabilitare
    /// dice la verita' (c'e', e adesso non si puo' usare). Il `GUI.enabled` si ripristina **sempre**,
    /// cosi' il resto della schermata non eredita lo stato disabilitato.
    ///
    /// ⚠️ E LA PAROLA CAMBIA COME PRIMA: "Probe P2P" da spenta, "Stop probe" da accesa. Non e' cosmesi:
    /// e' il modo di sapere se la misura sta andando senza guardare il log.
    /// </summary>
    private bool ReadProbeButtonOn(int line)
    {
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && SteamLobby.Available;

        bool clicked = ReadButton(SteamP2PProbe.Active ? "Stop probe" : "Probe P2P", line, 1);

        GUI.enabled = wasEnabled;
        return clicked;
    }

    /// <summary>
    /// ⭐ L'INVITO DIVENTA UNA CONNESSIONE **P2P**. L'IP e la porta arrivano dalla lobby (letti in
    /// `LobbyEnter_t` dalla cache, o dopo `LobbyDataUpdate_t`: vedi `SteamLobbyImpl`) e servono **solo**
    /// come diagnostica nel log: la connessione non passa da li'. Tutto il resto viene dai campi della
    /// schermata: nome, password, squadra.
    ///
    /// 🔴 PERCHE' IL P2P E NON IL TCP - misurato, non ipotizzato. Prima questa funzione scriveva
    /// `serverIp:porta` nel campo "Server IP", cioe' l'IP **LAN** che l'host pubblica nella lobby.
    /// Ma `NetworkManager.CreateTransport` sceglie il filo **solo** dal campo
    /// (`SteamP2PTransport.IsRequested`, vero se e solo se vale `p2p`): quindi un invito faceva partire
    /// un `TcpTransport` verso un indirizzo privato, e fuori dalla LAN quel dial finisce in
    /// `Connection refused` / `Failed to establish connection`
    /// (`scratch\DIAGNOSI-P2P-HOTSPOT.md`), con **zero** righe `[Net] trasporto=p2p` nei log. Il P2P
    /// invece era gia' disponibile e diretto (`[P2P] ... relay=0 via=diretto`): moriva solo perche'
    /// nessuno glielo aveva chiesto.
    ///
    /// ⭐ IL MECCANISMO E' UNO SOLO, e non c'e' nessun flag nuovo: si scrive nel campo il token che il
    /// trasporto chiede gia' (`SteamP2PTransport.RequestToken`), **prima** della connessione. Da li' in
    /// poi la strada e' quella che esiste: `ValidateAndConnect` legge il campo, `CreateTransport` vede
    /// il token e costruisce `SteamP2PTransport`, che entra nella lobby dell'invito e parla con il
    /// **proprietario** (`SteamP2PTransportImpl.Begin`: `host`/`port` non vengono nemmeno letti). Il
    /// TCP resta intatto: chi scrive un indirizzo nel campo continua a fare TCP, byte per byte.
    ///
    /// ⚠️ E IL CAMPO RESTA `p2p` ANCHE DOPO: `OnConnect` mette questa `ConnectionInfo` in
    /// `LastConnectionInfo`, quindi alla riapertura il campo propone `p2p` e **mai piu'** l'IP locale
    /// dell'host. Vuoto sarebbe peggio: cancellerebbe l'unica cosa che l'invito ha insegnato.
    ///
    /// ⚠️ SE L'INPUT NON E' VALIDO (tipicamente: nome del giocatore vuoto al primo avvio) **non si
    /// connette niente**, ma i campi restano compilati: l'utente scrive il nome, preme Connect, e
    /// l'invito non e' andato perso. E' il motivo per cui il prefill e il tentativo sono due passi.
    /// </summary>
    private void ApplySteamJoin(string serverIp, int port, string room, string player, string password, byte team)
    {
        // ⚠️ `_firstShowing = false` SUBITO: altrimenti il prossimo giro di `DisplayStart`
        // rimetterebbe `LastConnectionInfo` al posto del token dell'invito.
        _firstShowing = false;

        // ⭐ IL CAMPO PRIMA DELLA CONNESSIONE: e' il token che il trasporto legge (`IsRequested`). Da
        // qui in avanti questa connessione e' P2P e non nomina nessun indirizzo.
        _connection = new ConnectionInfo(SteamP2PTransport.RequestToken, room, player, password, team);

        // L'IP della lobby resta nel log come DIAGNOSTICA (dice **quale** host ha invitato), non come
        // destinazione: non e' l'indirizzo su cui si viaggia.
        ModLog.Info(SteamLobby.MarkerJoinConnect + SteamP2PTransport.RequestToken +
                    " (host dalla lobby: " + serverIp + ":" + port + ")");

        if (!ValidateInputs())
        {
            // I campi restano compilati: l'invito NON e' perso, manca solo un dato.
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "Steam: invito ricevuto, correggi i campi e premi Connect");
            return;
        }

        ValidateAndConnect();
    }

    /// <summary>
    /// ⭐ LA SESSIONE-HOST IN UN CLIC: avvia il server su questa macchina E si connette in locale.
    ///
    /// PERCHE' DUE COSE E NON UNA: il server non ha un client dentro di se' - e' lo stesso engine di
    /// `SERVER-COOP`, che aspetta connessioni. Se il bottone avviasse solo il server, il giocatore
    /// resterebbe su una schermata di connessione con un server acceso e nessun giocatore: chi
    /// ospita deve essere anche il PRIMO giocatore. Quindi: porta su, poi `Connect` su `127.0.0.1`
    /// (non su `localhost`: e' lo stesso, ma `127.0.0.1` non puo' finire in una risoluzione di nome
    /// che dipende dal file hosts della macchina).
    ///
    /// ⚠️ SE LA PORTA E' OCCUPATA NON SI CONNETTE A NIENTE. Sarebbe la tentazione facile -
    /// "connettiti lo stesso a 127.0.0.1" - ma su quella porta ci sarebbe il server di QUALCUN
    /// ALTRO, e si entrerebbe in una sessione che non e' la nostra. Si torna indietro e lo si dice.
    ///
    /// ⚠️ SE L'HOST NON PARTE, IL GIOCO NON SE NE ACCORGE: nessuna eccezione esce dal `OnGUI`,
    /// perche' `HostSession.TryStart` le cattura tutte e ritorna `false`.
    /// </summary>
    private void HostAndConnect(string room, string player, string password, byte team)
    {
        if (_attemptingConnection)
            return;

        // L'IP scritto nel campo e' il server che si vuole RAGGIUNGERE: qui il server siamo noi,
        // quindi `ConnectionInfo` prende l'indirizzo locale (ed e' anche cio' che si legge poi nella
        // riga "You are connected to ..." - cioe' la verita', non quello che c'era scritto prima).
        _connection = new ConnectionInfo(HOST_IP, room, player, password, team);

        if (!ValidateInputs())
            return;

        if (!HostSession.TryStart(Protocol.DEFAULT_PORT, password))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Host session: " + HostSession.LastError);
            return;
        }

        _firstShowing = false;
        Main.Multiplayer.NotificationManager.DisplayNotification(
            "Host session avviata sulla porta " + Protocol.DEFAULT_PORT);

        // ⭐⭐ E ADESSO SI ASPETTA CHE IL LOOPBACK RISPONDA, POI SI ENTRA — VEDI `HostDial`.
        //
        // Prima qui c'era `StartCoroutine(TryConnect(HOST_IP, DEFAULT_PORT))`: il dial partiva al
        // frame dopo, senza verificare che il listener rispondesse. Il log misurato dice che quella
        // connessione finiva in `Connection refused` con **zero** righe `[Server]`: il tentativo non
        // arrivava a nessun listener. Le due cause possibili (bind per-NIC senza loopback, oppure
        // listener non ancora pronto) sono distinte e documentate in
        // `scratch\REFERTO-AUTOCONNECT.md`; qui si coprono ENTRAMBE, e la riga che lo dice e' il
        // marker `[Host] loopback pronto via=...`.
        StartCoroutine(HostDial(room, player, password, team, Protocol.DEFAULT_PORT));
    }

    /// <summary>
    /// ⭐ LA CONNESSIONE-HOST: PRIMA LA PROVA CHE LA PORTA RISPONDA, POI IL DIAL.
    ///
    /// PERCHE' ASPETTARE E NON CONNETTERSI SUBITO: `Server.Start` ritorna `true` quando il listener
    /// e' legato, ma fra quel ritorno e il dial c'e' comunque un frame di mezzo - e soprattutto, prima
    /// del fix, il "legato" non voleva dire "legato anche sul loopback" (`SimpleTcpServer.Start(int)`
    /// lega per NIC, una per indirizzo). Un solo modo per non indovinare: **chiedere** a
    /// `127.0.0.1:porta` se risponde, e ripetere la domanda finche' non risponde o finche' non scade il
    /// tempo.
    ///
    /// ⚠️ NON E' UNA `Sleep` FISSA: e' un POLL, un tentativo per frame (`yield return null`), con una
    /// SCADENZA vera (`HOST_READY_TIMEOUT_MS`). Se la scadenza arriva, si scrive un marker d'errore
    /// esplicito (`[Host] errore: loopback ...`) e **si prova lo stesso** a connettersi, perche' un
    /// poll che sbaglia non deve essere la ragione per cui l'utente resta fuori dalla sua partita.
    ///
    /// ⚠️ E IL NOME, COME RIPIEGO: prima `127.0.0.1` (nessuna risoluzione di nome, nessun file hosts
    /// di mezzo), poi `localhost` - che il resolver puo' mandare su `::1`, cioe' IPv6. Se risponde il
    /// nome, il marker lo dice (`via=nome`), e quella domanda non si riapre piu'.
    ///
    /// ⚠️ `Time.realtimeSinceStartup` e non `Time.time`: la schermata di connessione puo' stare su un
    /// gioco in pausa (`timeScale = 0`), dove `Time.time` non avanza e la scadenza non arriverebbe mai.
    /// </summary>
    private IEnumerator HostDial(string room, string player, string password, byte team, int port)
    {
        _attemptingConnection = true;

        string target = null;
        float started = Time.realtimeSinceStartup;

        while (target == null)
        {
            target = HostSession.ProbeLoopback(port, READINESS_ATTEMPT_MS);

            if (target != null)
                break;

            if ((Time.realtimeSinceStartup - started) * 1000f >= HOST_READY_TIMEOUT_MS)
                break;

            yield return null;
        }

        int waitedMs = (int)((Time.realtimeSinceStartup - started) * 1000f);

        if (target == null)
        {
            // ⭐ LA SCADENZA HA UN NOME. Non si finge che sia andata bene: il marker dice quanto si e'
            // aspettato e su cosa, e la notifica lo dice al giocatore. Poi si prova lo stesso.
            _hostVia = HostSession.VIA_BESTEFFORT;
            target = HOST_IP;

            ModLog.Error(HostSession.MarkerWaitFail + ": nessuna risposta su " + HostSession.LOOPBACK_IPV4 +
                         ":" + port + " ne' su " + HostSession.LOOPBACK_NAME + ":" + port + " entro " +
                         HOST_READY_TIMEOUT_MS + " ms (attesa " + waitedMs + " ms): provo lo stesso " +
                         HostSession.LOOPBACK_IPV4);
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "Host session: il loopback non risponde su " + HostSession.LOOPBACK_IPV4 + ":" + port +
                " (vedi log)");
        }
        else
        {
            _hostVia = target == HostSession.LOOPBACK_IPV4 ? HostSession.VIA_IPV4 : HostSession.VIA_NOME;

            ModLog.Info(HostSession.MarkerReady + _hostVia + " host=" + target + " porta=" + port +
                        " attesa=" + waitedMs + " ms");
        }

        // L'indirizzo effettivo e' quello che ha RISPOSTO, non quello scritto nel campo: cosi' la riga
        // "You are connected to ..." dice la verita' anche quando si e' entrati col nome.
        _connection = new ConnectionInfo(target, room, player, password, team);

        Main.Multiplayer.NetworkManager.OnConnect += OnConnect;

        // ⭐ LA RIGA DEL DIAL, CON LA PORTA: `Attempting to connect to {indirizzo}` (la riga di sempre)
        // NON stampa la porta, quindi da sola non permetteva di escludere che il dial andasse su una
        // porta diversa da quella dove il server ascolta - ed era uno dei due sospetti sul `refused`.
        // Qui host e porta sono quelli VERI, scritti prima di provare, e l'esito arriva in `OnConnect`
        // (`[Host] dial locale esito=...`).
        ModLog.Info(HostSession.MarkerDial + "host=" + target + " porta=" + port + " via=" + _hostVia);

        // La stessa riga di sempre, parola per parola (`Attempting to connect to {server}`).
        ModLog.Info($"Attempting to connect to {target}");

        yield return null;

        Main.Multiplayer.NetworkManager.Connect(target, port, room, player, password, team);
    }

    private void DisplayMiddle()
    {
        var bigStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 20,
        };

        GUI.Label(new Rect(0, 20, 330, 380), "Connnecting...", bigStyle);

        // ⭐⭐ LA VIA D'USCITA — RETE DI SICUREZZA, INDIPENDENTE DAL RESTO DEL FIX.
        //
        // 🔴 Il difetto, osservato dall'utente: quando il P2P falliva la schermata restava **qui**, e
        // questa schermata **non disegna nessun bottone**: l'unica uscita era riavviare il gioco. Il
        // fix vero e' che l'esito negativo arrivi a `OnConnect` (`NetworkManager.OnDisconnect`), ma una
        // UI che puo' restare senza uscita se qualcosa *ancora* non arriva e' una UI sbagliata lo stesso.
        // Quindi il "si puo' sempre tornare indietro" non dipende dal fatto che il fix funzioni.
        //
        // ⚠️ "Annulla" e' **la stessa parola di ripiego** che l'utente ha chiesto: riporta a
        // `DisplayStart` (i campi sono ancora compilati, l'invito non e' perso) e chiude il tentativo.
        // Sta sulla riga 5, la stessa di "Connect"/"Host session": cosi' il bottone cade esattamente dove
        // l'utente l'ha lasciato e la finestra non cambia altezza.
        if (ReadButton("Annulla", 5))
            CancelConnect();
    }

    /// <summary>
    /// ⭐ ANNULLA IL TENTATIVO IN CORSO — la rete di sicurezza della schermata di connessione.
    ///
    /// Fa tre cose, in quest'ordine: abbassa `_attemptingConnection` (cosi' il frame successivo disegna
    /// `DisplayStart` e **non** torna qui), toglie la sottoscrizione all'esito (un esito che arrivasse
    /// dopo non deve riportare la schermata da un'altra parte) e chiude il trasporto se c'e'.
    ///
    /// ⚠️ NON tocca i campi compilati: server/nome/password/squadra restano, quindi "Annulla" e' un
    /// ritorno al punto di prima e non una perdita di quello che si e' scritto.
    ///
    /// ⚠️ NON spegne la sessione-host (`HostSession`): e' un'altra cosa (una porta aperta, non un
    /// tentativo), e chiudere quella e' il mestiere di "Stop host" e di "Disconnect" (vedi `TryDisconnect`).
    ///
    /// ⚠️ Tutto in `try/catch`: questo corpo gira dentro `OnGUI`, e un guasto qui non deve poter fermare
    /// il resto della schermata.
    /// </summary>
    private void CancelConnect()
    {
        _attemptingConnection = false;

        ModLog.Info("[Net] tentativo di connessione annullato dal giocatore");

        try
        {
            Main.Multiplayer.NetworkManager.OnConnect -= OnConnect;
            Main.Multiplayer.NetworkManager.Disconnect();
        }
        catch (System.Exception e)
        {
            ModLog.Error("[Net] errore annullando il tentativo: " + e.GetType().Name + ": " + e.Message);
        }
    }

    private void DisplayEnd()
    {
        // Show actual connection details
        string text = $"You are connected to {_connection.ServerIp} ({_connection.RoomName}) as {_connection.PlayerName} on team {_connection.TeamNumber}";
        ShowLabel(text, 0);

        // Se stiamo ospitando, lo si dice: "127.0.0.1" da solo non fa capire che il server e' qui,
        // ne' che spegnendo la connessione si spegne anche l'host.
        if (HostSession.IsRunning)
            ShowLabel("Host attivo: porta " + HostSession.Port + " - giocatori " + HostSession.Players, 1);

        // ⭐⭐ LA SONDA P2P C'E' ANCHE QUI, accanto a "Disconnect" e sulla STESSA riga.
        //
        // PERCHE' QUI (ed era il guasto): da hostati/connessi questa schermata e' l'UNICA che si vede
        // (`MultiplayerWindow` sceglie `DisplayEnd` quando `IsConnected`), e prima conteneva solo
        // "Disconnect". La sonda invece viveva solo in `DisplayStart`, quindi per misurarla bisognava
        // **togliere l'host** - e togliere l'host cambia la lobby, cioe' proprio la cosa da misurare.
        // Un misuratore che si puo' usare solo dopo aver smontato l'esperimento non misura niente.
        //
        // ⚠️ LAYOUT: NESSUNA RIGA NUOVA. "Disconnect" e' nella casella 0 (slot 0, x=10, 90 px), la
        // sonda nella casella 1 (x=110, 130 px): la stessa coppia che "Connect" + "Host session" usa
        // nella schermata libera, e ci sta dentro la finestra larga 330 px (110+130 = 240). Le righe
        // 0 e 1 (connessione e stato dell'host) non si spostano di un pixel.
        bool disconnectClicked = ReadButton("Disconnect", 2);
        bool probeClicked = ReadProbeButtonOn(2);

        if (probeClicked)
            HandleProbeClick();

        if (disconnectClicked)
            TryDisconnect();

        // ⭐ LA RIGA DELLA MISURA, e c'e' SOLO mentre la sonda e' accesa - stessa regola di
        // `DisplayStart`: chi non la preme non vede una riga in piu' e la schermata resta com'era.
        // Sta sotto i bottoni (riga 3, y=125) e dice quello che serve a chi sta testando: quanti peer,
        // quante sessioni attive, quante dirette e quante in relay (il dettaglio per peer e' nel log).
        string probe = SteamP2PProbe.Status();
        if (!string.IsNullOrEmpty(probe))
            ShowLabel(probe, 3);
    }

    private void ValidateAndConnect()
    {
        if (!ValidateInputs())
            return;

        // Lo stesso server/porta di prima, letti dallo stesso campo ("ip" oppure "ip:porta").
        string server;
        int port;

        if (_connection.ServerIp.Contains(':'))
        {
            var parts = _connection.ServerIp.Split(':');
            server = parts[0];
            port = int.Parse(parts[1]);
        }
        else
        {
            server = _connection.ServerIp;
            port = Protocol.DEFAULT_PORT;
        }

        StartCoroutine(TryConnect(server, port));
    }

    /// <summary>
    /// I cinque controlli dell'input, in un posto solo: li usano sia "Connect" sia "Host session"
    /// (l'host si connette a se stesso, e le regole su nome/password/squadra sono le stesse: il
    /// server rifiuterebbe un nome vuoto o una password sbagliata esattamente come per un peer).
    /// Stesse notifiche, stesso testo, stesso ordine di prima.
    /// </summary>
    private bool ValidateInputs()
    {
        bool valid = true;

        // Validate server ip
        if (!_validator.IsServerValid(_connection.ServerIp))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Server IP is invalid");
            valid = false;
        }

        // Validate room name
        if (!_validator.IsRoomValid(_connection.RoomName))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Room name is invalid");
            valid = false;
        }

        // Validate player name
        if (!_validator.IsPlayerValid(_connection.PlayerName))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Player name is invalid");
            valid = false;
        }

        // Validate password
        if (!_validator.IsPasswordValid(_connection.Password))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Password is invalid");
            valid = false;
        }

        // Validate team number
        if (!_validator.IsTeamValid(_connection.TeamNumber))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Team number is invalid");
            valid = false;
        }

        return valid;
    }

    private IEnumerator TryConnect(string server, int port)
    {
        ModLog.Info($"Attempting to connect to {server}");

        _attemptingConnection = true;
        Main.Multiplayer.NetworkManager.OnConnect += OnConnect;

        yield return null;

        bool result = Main.Multiplayer.NetworkManager.Connect(server, port, _connection.RoomName, _connection.PlayerName, _connection.Password, (byte)_connection.TeamNumber);
    }

    private void OnConnect(bool success, RefusalType refusal)
    {
        _attemptingConnection = false;

        // ⭐ SOLO PER LA SESSIONE-HOST (`_hostVia` la valorizza `HostDial`, il "Connect" manuale no):
        // la riga che chiude la domanda dell'auto-connessione, cioe' QUALE indirizzo ha funzionato,
        // su QUALE porta, e con QUALE esito. Se il dial dell'host fallisce lo si dice qui, con la
        // stessa parola chiave del log.
        if (_hostVia != null)
        {
            if (success)
                ModLog.Info(HostSession.MarkerConnected + _hostVia + " host=" + _connection.ServerIp +
                            " porta=" + HostSession.Port);

            string dial = HostSession.MarkerDial + "esito=" + (success ? "ok" : "rifiutato") +
                          " via=" + _hostVia + " host=" + _connection.ServerIp +
                          " porta=" + HostSession.Port +
                          (success ? string.Empty : " rifiuto=" + refusal);

            if (success)
                ModLog.Info(dial);
            else
                ModLog.Error(dial);

            _hostVia = null;
        }

        Main.Multiplayer.LastConnectionInfo = _connection;
        Main.Multiplayer.NetworkManager.OnConnect -= OnConnect;
    }

    private void TryDisconnect()
    {
        ModLog.Info($"Disconnecting from {_connection.ServerIp}");
        Main.Multiplayer.NetworkManager.Disconnect();

        // ⭐⭐ CHI SMETTE DI GIOCARE CHIUDE ANCHE L'HOST, e non e' un dettaglio: un server acceso
        // tiene occupata la porta 27051 FINO ALLA CHIUSURA DEL GIOCO. Senza questa riga, dopo aver
        // ospitato una volta il bottone "Host session" non ripartirebbe piu' (porta occupata da noi
        // stessi) e un `SERVER-COOP` esterno su questa macchina non potrebbe piu' partire.
        //
        // Se non stavamo ospitando non fa niente e non stampa niente (`HostSession.Stop` esce subito
        // quando non c'e' nessun host), quindi il percorso "peer di un server esterno" resta
        // identico a prima.
        HostSession.Stop();

        // ⭐ E LA LOBBY STEAM SE NE VA CON LA SESSIONE (vedi `SteamLobby.LeaveLobby`). Stessa regola
        // di `HostSession.Stop`: se non c'era nessuna lobby non fa niente e non stampa niente, quindi
        // chi non usa Steam non vede una riga in piu'.
        SteamLobby.LeaveLobby();
    }

    private void ShowLabel(string label, int line)
    {
        GUI.Label(new Rect(START_X, START_Y + (LINE_HEIGHT + GAP) * line, 310, LINE_HEIGHT * 2 + GAP), label);
    }

    private string ReadTextField(string label, string value, int line)
    {
        GUI.Label(new Rect(START_X, START_Y + (LINE_HEIGHT + GAP) * line, LABEL_WIDTH, LINE_HEIGHT), label);
        return GUI.TextField(new Rect(START_X + LABEL_WIDTH + GAP, START_Y + (LINE_HEIGHT + GAP) * line, TEXT_WIDTH, LINE_HEIGHT), value);
    }

    private int ReadChoiceBox(string label, int value, string[] choices, int line)
    {
        GUI.Label(new Rect(START_X, START_Y + (LINE_HEIGHT + GAP) * line, LABEL_WIDTH, LINE_HEIGHT), label);
        return GUI.Toolbar(new Rect(START_X + LABEL_WIDTH + GAP, START_Y + (LINE_HEIGHT + GAP) * line, TEXT_WIDTH, LINE_HEIGHT), value, choices);
    }

    private bool ReadButton(string label, int line)
    {
        return GUI.Button(new Rect(START_X, START_Y + (LINE_HEIGHT + GAP) * line, BUTTON_WIDTH, LINE_HEIGHT), label);
    }

    /// <summary>
    /// Lo stesso bottone, nella casella a destra della prima (`slot` 0 = "Connect", 1 = l'host):
    /// cosi' i due comandi stanno sulla STESSA riga e la schermata resta alta com'era.
    /// </summary>
    private bool ReadButton(string label, int line, int slot)
    {
        int x = START_X + slot * (BUTTON_WIDTH + GAP);
        int width = slot == 0 ? BUTTON_WIDTH : HOST_BUTTON_WIDTH;
        return GUI.Button(new Rect(x, START_Y + (LINE_HEIGHT + GAP) * line, width, LINE_HEIGHT), label);
    }

    /// <summary>
    /// Il bottone di Steam: riga sua, larghezza sua (`STEAM_BUTTON_WIDTH`), perche' "Steam: Host" non
    /// sta in 90 px. Non ha uno `slot` perche' non condivide la riga con nessuno: i due comandi IP
    /// restano dove sono e questa e' una riga AGGIUNTA sotto, non una casella rubata a loro.
    /// </summary>
    private bool ReadSteamButton(string label)
    {
        return GUI.Button(new Rect(START_X, START_Y + (LINE_HEIGHT + GAP) * STEAM_LINE,
                                   STEAM_BUTTON_WIDTH, LINE_HEIGHT), label);
    }

    /// <summary>
    /// ⭐ IL BOTTONE DELLA SONDA P2P: **accanto** a quello di Steam, nella casella a destra della stessa
    /// riga (non una riga in piu', non una casella rubata ai bottoni IP). La larghezza e' la stessa,
    /// cosi' i due comandi si leggono come una coppia: a sinistra la lobby, a destra la misura.
    /// </summary>
    private bool ReadProbeButton(string label)
    {
        int x = START_X + STEAM_BUTTON_WIDTH + GAP;
        return GUI.Button(new Rect(x, START_Y + (LINE_HEIGHT + GAP) * STEAM_LINE,
                                   STEAM_BUTTON_WIDTH, LINE_HEIGHT), label);
    }

    private const int START_X = 10;
    private const int START_Y = 20;
    private const int GAP = 10;
    private const int LINE_HEIGHT = 25;
    private const int LABEL_WIDTH = 100;
    private const int TEXT_WIDTH = 200;
    private const int BUTTON_WIDTH = 90;
    private const int HOST_BUTTON_WIDTH = 130;
    private const int STEAM_BUTTON_WIDTH = 130;

    /// <summary>
    /// Le due righe di Steam: `STEAM_LINE` (7) e' il bottone, `STEAM_STATUS_LINE` (8) lo stato.
    ///
    /// PERCHE' 7 E 8, E NON 5 O 6: la 5 e' la riga dei bottoni ("Connect" + "Host session") e la 6 e'
    /// la riga del riepilogo dell'host. Le due righe di Steam stanno **sotto** entrambe, cosi'
    /// nessuna casella esistente si sposta - e la finestra e' alta 400 px, quindi la 8 (y=300) ci
    /// sta comoda.
    ///
    /// ⭐ E LA 9 E' LA RIGA DELLA SONDA P2P: sta **sotto** lo stato di Steam perche' e' un'altra cosa
    /// (una misura, non un comando), e ci sta: y=335, due righe di testo arrivano a 395 dentro una
    /// finestra alta 420.
    /// </summary>
    private const int STEAM_LINE = 7;
    private const int STEAM_STATUS_LINE = 8;
    private const int P2P_STATUS_LINE = 9;

    /// <summary>
    /// ⚠️ L'INDIRIZZO DELLA SESSIONE-HOST E' SEMPRE QUESTO, e non e' una preferenza: il server
    /// ascolta su `IPAddress.Any`, ma il client che ospita deve parlare con SE STESSO. `localhost`
    /// andrebbe ugualmente bene; `127.0.0.1` in piu' non passa da nessuna risoluzione di nome (file
    /// hosts compreso), che e' l'unica cosa che potrebbe farlo sbagliare.
    ///
    /// ⭐ E RESTA IL PRIMO DELLA LISTA: `HostDial` lo prova per primo e passa a `localhost` solo se
    /// lui non risponde (ripiego per un eventuale listener IPv6/`::1`). Il marker dice quale dei due
    /// ha funzionato, quindi la scelta non e' piu' una supposizione.
    /// </summary>
    private const string HOST_IP = "127.0.0.1";

    /// <summary>
    /// ⏱️ QUANTO SI ASPETTA CHE IL LOOPBACK RISPONDA PRIMA DI ARRENDERSI: 3 secondi, e non e' un
    /// numero a caso. Il bind del listener e' sincrono dentro `Server.Start` (millisecondi), quindi
    /// nel caso normale la prima sonda risponde subito e questo tempo non si vede nemmeno; 3 s
    /// coprono un `bind` rallentato da antivirus/firewall, e restano sotto la soglia in cui il
    /// giocatore crede che il bottone non abbia funzionato.
    /// </summary>
    private const int HOST_READY_TIMEOUT_MS = 3000;

    /// <summary>
    /// Il tempo massimo di UNA sonda (per indirizzo). Il rifiuto su loopback e' istantaneo, quindi nel
    /// caso del difetto questo valore non si vede; serve solo a non piantare il frame quando nessuno
    /// risponde affatto (firewall che scarta invece di rifiutare).
    /// </summary>
    private const int READINESS_ATTEMPT_MS = 150;

    /// <summary>
    /// `ipv4` / `nome` / `best-effort`: **quale** strada ha portato all'host, per il marker
    /// `[Host] connesso via=`. `null` = l'ultimo dial non era una "Host session" (era "Connect"), e in
    /// quel caso `OnConnect` non aggiunge NESSUNA riga: il percorso manuale resta identico a prima.
    /// </summary>
    private string _hostVia;

    private const int WIDTH = 330;
    private const int HEIGHT = 400;
}
