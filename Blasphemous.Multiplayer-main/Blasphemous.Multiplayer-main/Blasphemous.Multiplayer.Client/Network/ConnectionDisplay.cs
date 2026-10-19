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
        if (Main.Multiplayer.NetworkManager.IsConnected)
        {
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
                HostSession.Stop();

            ShowLabel("Host attivo: porta " + HostSession.Port + " - giocatori " + HostSession.Players, 6);
        }
        else if (ReadButton("Host session", 5, 1))
        {
            HostAndConnect(room, player, password, (byte)team);
        }
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

        if (ReadButton("Disconnect", 2))
            TryDisconnect();
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

    private const int START_X = 10;
    private const int START_Y = 20;
    private const int GAP = 10;
    private const int LINE_HEIGHT = 25;
    private const int LABEL_WIDTH = 100;
    private const int TEXT_WIDTH = 200;
    private const int BUTTON_WIDTH = 90;
    private const int HOST_BUTTON_WIDTH = 130;

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
