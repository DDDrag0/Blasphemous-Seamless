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

    // Window framework
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
        // Drop Steam actions that no longer make sense while connected: a stale invite must not
        // reconnect at the next Disconnect, and a host lobby left open would advertise a closed
        // port. Costs one boolean check when nothing is pending.
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

        // Sanitize each field only when the user changed it, so a clean pass is not applied to
        // the user's own typing every frame.
        string server = ReadTextField("Server IP:", _connection.ServerIp, 0);
        if (_firstShowing || server != _connection.ServerIp)
        {
            server = _sanitizer.CleanServer(server);
        }

        // Room system not implemented yet.
        string room = "a";

        string player = ReadTextField("Player name:", _connection.PlayerName, 2);
        if (_firstShowing || player != _connection.PlayerName)
        {
            player = _sanitizer.CleanPlayer(player);
        }

        string password = ReadTextField("Password:", _connection.Password, 3);
        if (_firstShowing || password != _connection.Password)
        {
            password = _sanitizer.CleanPassword(password);
        }

        int team = ReadChoiceBox("Team number:", _connection.TeamNumber - 1, Enumerable.Range(1, 8).Select(x => x.ToString()).ToArray(), 4) + 1;
        if (_firstShowing || team != _connection.TeamNumber)
        {
            team = _sanitizer.CleanTeam(team);
        }

        _connection = new ConnectionInfo(server, room, player, password, (byte)team);
        _firstShowing = false;

        if (ReadButton("Connect", 5))
            ValidateAndConnect();

        // "Connect" joins an existing session (an external SERVER-COOP); "Host session" opens
        // the port here and joins locally. When hosting, the button becomes "Stop host" (a
        // second click would start a second server on the same port).
        if (HostSession.IsRunning)
        {
            if (ReadButton("Stop host", 5, 1))
            {
                HostSession.Stop();

                // Leave the Steam lobby too: a lobby that stays open advertises a port where
                // nobody listens any more. LeaveLobby is a no-op when we were not hosting via
                // Steam.
                SteamLobby.LeaveLobby();
            }

            ShowLabel("Host active: port " + HostSession.Port + " - players " + HostSession.Players, 6);
        }
        else if (ReadButton("Host session", 5, 1))
        {
            HostAndConnect(room, player, password, (byte)team);
        }

        // Steam block is additive: the buttons above keep their word, order, position and
        // behaviour. This block only adds one command and consumes the two Steam callback
        // actions (host lobby ready / invite data ready).
        DisplaySteam(room, player, password, (byte)team);
    }

    /// <summary>
    /// Steam block: ① consume callback actions (invite accepted / host lobby ready), ② draw the
    /// Steam button (disabled, not hidden, when Steam is unavailable - hiding would look like the
    /// feature does not exist), ③ show a short status line. Every SteamLobby call is guarded
    /// internally and returns a boolean; nothing here throws. Fields (room/player/password/team)
    /// come from DisplayStart unchanged.
    /// </summary>
    private void DisplaySteam(string room, string player, string password, byte team)
    {
        bool steam = SteamLobby.Available;

        // Invite accepted: lobby data (hostIp/hostPort) is ready. Fill the fields and connect -
        // the "one click instead of typing an IP" path.
        string joinIp;
        int joinPort;
        if (SteamLobby.TryConsumeJoin(out joinIp, out joinPort))
        {
            ApplySteamJoin(joinIp, joinPort, room, player, password, team);

            // Do not also read the button this frame: the click may be leftover layout and the
            // connection is already under way.
            return;
        }

        // Host lobby ready (LobbyCreated_t with OK): start the session via the same path as the
        // "Host session" button, not a copy.
        if (SteamLobby.TryConsumeHostLobbyReady())
        {
            HostAndConnect(room, player, password, team);

            if (HostSession.IsRunning)
            {
                Main.Multiplayer.NotificationManager.DisplayNotification(
                    "Steam: lobby open, invite a friend from the overlay");
            }
            else
            {
                // Host did not start (port busy or invalid input): the lobby would advertise a
                // closed port, so leave immediately.
                SteamLobby.LeaveLobby();
            }

            return;
        }

        // Steam and probe buttons share one row: Steam on the left, probe on the right. Two
        // distinct commands so pressing the probe does not open a lobby, and opening a lobby does
        // not turn on the probe.
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && steam;

        bool clicked = ReadSteamButton(HostSession.IsRunning && SteamLobby.HasHostLobby
            ? "Steam: invite"
            : "Steam: Host");

        bool probeClicked = ReadProbeButton(SteamP2PProbe.Active ? "Stop probe" : "Probe P2P");

        GUI.enabled = wasEnabled;

        if (clicked)
        {
            // If a lobby of ours already exists, StartHost reopens the invite overlay instead of
            // creating a second one.
            if (!SteamLobby.StartHost(player))
            {
                Main.Multiplayer.NotificationManager.DisplayNotification(
                    "Steam: unavailable (see log)");
            }
        }

        // P2P probe: sends 1 byte to lobby peers every second and writes one line per peer every
        // 3s (relay=0 direct, relay=1 relayed). Measures whether Steam P2P works between two
        // machines even when TCP does not arrive. Needs no game connection, only a lobby; stays
        // armed and starts on its own when a lobby appears. Shared by DisplayStart and DisplayEnd
        // via HandleProbeClick so the two paths cannot diverge.
        if (probeClicked)
            HandleProbeClick();

        // Status line, short. The lobby id is shown on purpose: it is what you paste into Steam
        // launch options to reproduce the "game off" case by hand.
        if (!steam)
            ShowLabel("Steam: unavailable (offline)", STEAM_STATUS_LINE);
        else if (SteamLobby.HasHostLobby)
            ShowLabel("Lobby " + SteamLobby.HostLobbyId, STEAM_STATUS_LINE);

        // Probe status line only while the probe is on. Same as the log line in short form (peer
        // count, direct vs relay) so the test can be read without opening the log.
        string probe = SteamP2PProbe.Status();
        if (!string.IsNullOrEmpty(probe))
            ShowLabel(probe, P2P_STATUS_LINE);
    }

    /// <summary>
    /// Probe click body, shared by DisplayStart and DisplayEnd. Present in both screens so that
    /// measuring does not require dropping the host (which would change the lobby being
    /// measured). Does not touch connection, transport, protocol, lobby or NetworkManager.
    /// </summary>
    private void HandleProbeClick()
    {
        if (SteamP2PProbe.Active)
        {
            SteamP2PProbe.Stop();
            Main.Multiplayer.NotificationManager.DisplayNotification("P2P: probe stopped (see log)");
        }
        else if (SteamP2PProbe.Start())
        {
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "P2P: probe on, check the log ([P2P] peer=...)");
        }
        else
        {
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "P2P: unavailable (see log)");
        }
    }

    /// <summary>
    /// Probe button for the connected screen (host or guest): same right-hand slot used by "Host
    /// session" on the IP row, so no extra row and no slot taken from "Disconnect". Disabled,
    /// not hidden, when Steam is unavailable. GUI.enabled is always restored. Word toggles
    /// between "Probe P2P" and "Stop probe" to make the state readable without the log.
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
    /// Turns an accepted Steam invite into a P2P connection.
    ///
    /// The field is set to the P2P request token (SteamP2PTransport.RequestToken) BEFORE connecting
    /// so NetworkManager.CreateTransport builds SteamP2PTransport. The LAN IP/port from the lobby
    /// is only logged for diagnostics; it is not the destination. If validation fails, fields stay
    /// filled and nothing connects - the invite is not lost, only a value is missing.
    /// </summary>
    private void ApplySteamJoin(string serverIp, int port, string room, string player, string password, byte team)
    {
        // _firstShowing = false immediately, or the next DisplayStart would overwrite the token
        // with LastConnectionInfo.
        _firstShowing = false;

        _connection = new ConnectionInfo(SteamP2PTransport.RequestToken, room, player, password, team);

        ModLog.Info(SteamLobby.MarkerJoinConnect + SteamP2PTransport.RequestToken +
                    " (host from lobby: " + serverIp + ":" + port + ")");

        if (!ValidateInputs())
        {
            // Fields stay filled: the invite is not lost, only a value is missing.
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "Steam: invite received, fix the fields and press Connect");
            return;
        }

        ValidateAndConnect();
    }

    /// <summary>
    /// Starts the server here AND connects locally: the server has no built-in client, so the host
    /// must also be the first player. On "port busy" it does NOT connect anyway: that port would
    /// be someone else's server and we would join a session that is not ours. TryStart catches
    /// all exceptions and returns false, so nothing escapes OnGUI.
    /// </summary>
    private void HostAndConnect(string room, string player, string password, byte team)
    {
        if (_attemptingConnection)
            return;

        // The server is us: ConnectionInfo uses the local address (this is also what the
        // "You are connected to ..." line shows - the truth, not the previous field).
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
            "Host session started on port " + Protocol.DEFAULT_PORT);

        // Wait until loopback answers, then connect. Server.Start returns true when the listener
        // is bound, but the bind may not cover loopback (per-NIC bind) or the listener may not be
        // ready yet; probing before the dial covers both. Marker: "[Host] loopback pronto via=...".
        StartCoroutine(HostDial(room, player, password, team, Protocol.DEFAULT_PORT));
    }

    /// <summary>
    /// Host connection: probe the loopback first, then dial. The probe is a per-frame poll (not a
    /// fixed sleep) with a real deadline; on timeout an explicit marker is written and the dial
    /// runs anyway, because a wrong probe must not be the reason the user stays out of their own
    /// game. `127.0.0.1` first (no name resolution), then `localhost` as an IPv6 fallback. Uses
    /// `Time.realtimeSinceStartup` because the connection screen may run on a paused game.
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
            // Timeout has a name: an explicit marker says how long we waited and for what, and
            // the player is told. Then we try anyway.
            _hostVia = HostSession.VIA_BESTEFFORT;
            target = HOST_IP;

            ModLog.Error(HostSession.MarkerWaitFail + ": no answer on " + HostSession.LOOPBACK_IPV4 +
                         ":" + port + " nor on " + HostSession.LOOPBACK_NAME + ":" + port + " within " +
                         HOST_READY_TIMEOUT_MS + " ms (waited " + waitedMs + " ms): trying " +
                         HostSession.LOOPBACK_IPV4 + " anyway");
            Main.Multiplayer.NotificationManager.DisplayNotification(
                "Host session: loopback does not answer on " + HostSession.LOOPBACK_IPV4 + ":" + port +
                " (see log)");
        }
        else
        {
            _hostVia = target == HostSession.LOOPBACK_IPV4 ? HostSession.VIA_IPV4 : HostSession.VIA_NOME;

            ModLog.Info(HostSession.MarkerReady + _hostVia + " host=" + target + " porta=" + port +
                        " attesa=" + waitedMs + " ms");
        }

        // The address actually used is the one that answered, not the one written in the field,
        // so "You are connected to ..." shows the truth even when we connected by name.
        _connection = new ConnectionInfo(target, room, player, password, team);

        Main.Multiplayer.NetworkManager.OnConnect += OnConnect;

        // Dial line with port: the previous "Attempting to connect to" line does not print the
        // port and cannot rule out a port mismatch. This line shows real host and port before
        // dialing; the outcome arrives in OnConnect.
        ModLog.Info(HostSession.MarkerDial + "host=" + target + " porta=" + port + " via=" + _hostVia);

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

        // Safety exit, independent of the rest of the fix: this screen draws no button, so
        // without this a failed connect leaves only a game restart. "Cancel" returns to
        // DisplayStart (fields still filled, invite not lost) and closes the attempt. Same row
        // as Connect/Host session so window height is unchanged.
        if (ReadButton("Cancel", 5))
            CancelConnect();
    }

    /// <summary>
    /// Cancels the in-flight attempt: lowers _attemptingConnection, unsubscribes from the
    /// outcome, and closes the transport. Does not touch the filled fields (Cancel is a return,
    /// not a reset) and does not stop HostSession (that is "Stop host" / "Disconnect").
    /// </summary>
    private void CancelConnect()
    {
        _attemptingConnection = false;

        ModLog.Info("[Net] connection attempt cancelled by the player");

        try
        {
            Main.Multiplayer.NetworkManager.OnConnect -= OnConnect;
            Main.Multiplayer.NetworkManager.Disconnect();
        }
        catch (System.Exception e)
        {
            ModLog.Error("[Net] error cancelling the attempt: " + e.GetType().Name + ": " + e.Message);
        }
    }

    private void DisplayEnd()
    {
        // Show actual connection details
        string text = $"You are connected to {_connection.ServerIp} ({_connection.RoomName}) as {_connection.PlayerName} on team {_connection.TeamNumber}";
        ShowLabel(text, 0);

        // If hosting, say so: "127.0.0.1" alone does not tell that the server is here, or that
        // Disconnect also stops it.
        if (HostSession.IsRunning)
            ShowLabel("Host active: port " + HostSession.Port + " - players " + HostSession.Players, 1);

        // Probe button also here, on the same row as Disconnect. Before this, the probe lived
        // only in DisplayStart, and using it while hosting required dropping the host - which
        // changes the very lobby being measured. Disconnect keeps slot 0, probe takes slot 1, same
        // pair used by Connect/Host session in the free screen; fits in 330 px.
        bool disconnectClicked = ReadButton("Disconnect", 2);
        bool probeClicked = ReadProbeButtonOn(2);

        if (probeClicked)
            HandleProbeClick();

        if (disconnectClicked)
            TryDisconnect();

        // Probe status line only while the probe is on - same rule as DisplayStart.
        string probe = SteamP2PProbe.Status();
        if (!string.IsNullOrEmpty(probe))
            ShowLabel(probe, 3);
    }

    private void ValidateAndConnect()
    {
        if (!ValidateInputs())
            return;

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
    /// The five input checks in one place: used by both "Connect" and "Host session" (the host
    /// connects to itself, and the rules on name/password/team are the same: the server would
    /// reject an empty name or wrong password exactly the same way).
    /// </summary>
    private bool ValidateInputs()
    {
        bool valid = true;

        if (!_validator.IsServerValid(_connection.ServerIp))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Server IP is invalid");
            valid = false;
        }

        if (!_validator.IsRoomValid(_connection.RoomName))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Room name is invalid");
            valid = false;
        }

        if (!_validator.IsPlayerValid(_connection.PlayerName))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Player name is invalid");
            valid = false;
        }

        if (!_validator.IsPasswordValid(_connection.Password))
        {
            Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Password is invalid");
            valid = false;
        }

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

        // Only for the host session (_hostVia set by HostDial; the manual Connect leaves it
        // null): closes the auto-connect question - which address worked, on which port, with
        // which outcome.
        if (_hostVia != null)
        {
            if (success)
                ModLog.Info(HostSession.MarkerConnected + _hostVia + " host=" + _connection.ServerIp +
                            " porta=" + HostSession.Port);

            string dial = HostSession.MarkerDial + "esito=" + (success ? "ok" : "rejected") +
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

        // Also stop the host: a running server holds port 27051 until the game closes. Without
        // this, after hosting once the "Host session" button would never restart (port taken by
        // ourselves), and an external SERVER-COOP could not start either. No-op if not hosting.
        HostSession.Stop();

        // Leave the Steam lobby too. Same rule: no-op if not hosting via Steam.
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
    /// Same button in the slot to the right of the first: keeps both commands on one row and the
    /// window height unchanged.
    /// </summary>
    private bool ReadButton(string label, int line, int slot)
    {
        int x = START_X + slot * (BUTTON_WIDTH + GAP);
        int width = slot == 0 ? BUTTON_WIDTH : HOST_BUTTON_WIDTH;
        return GUI.Button(new Rect(x, START_Y + (LINE_HEIGHT + GAP) * line, width, LINE_HEIGHT), label);
    }

    /// <summary>
    /// Steam button on its own line, its own width: "Steam: Host" does not fit in 90 px. No slot
    /// because it does not share a row with anything - the two IP commands stay where they are
    /// and this is an added line below them.
    /// </summary>
    private bool ReadSteamButton(string label)
    {
        return GUI.Button(new Rect(START_X, START_Y + (LINE_HEIGHT + GAP) * STEAM_LINE,
                                   STEAM_BUTTON_WIDTH, LINE_HEIGHT), label);
    }

    /// <summary>
    /// Probe button next to the Steam one, in the right-hand slot of the same row (no extra row,
    /// no slot taken from the IP buttons). Same width so the pair reads as a pair: lobby on the
    /// left, measurement on the right.
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
    /// Steam rows: STEAM_LINE (7) is the button, STEAM_STATUS_LINE (8) the status. Row 5 is the
    /// IP buttons and row 6 the host summary; Steam rows sit below both, so no existing cell moves
    /// and the 400 px window has room. P2P_STATUS_LINE (9) is the probe row, below the Steam
    /// status, and fits (y=335, two text lines reach 395 in a 420 px window).
    /// </summary>
    private const int STEAM_LINE = 7;
    private const int STEAM_STATUS_LINE = 8;
    private const int P2P_STATUS_LINE = 9;

    /// <summary>
    /// Host session address is always this: the server binds to IPAddress.Any, but the hosting
    /// client must talk to itself. `127.0.0.1` does not pass through name resolution (hosts file
    /// included), the only thing that could redirect it. HostDial tries it first and falls back
    /// to `localhost` only if it does not answer (IPv6 / ::1 case); the marker says which worked.
    /// </summary>
    private const string HOST_IP = "127.0.0.1";

    /// <summary>
    /// How long to wait for loopback to answer before giving up: 3s. The listener bind is
    /// synchronous inside Server.Start (milliseconds), so the first probe usually answers at once
    /// and this is not seen; 3s covers a slow bind (antivirus/firewall) and stays under the
    /// threshold where the player thinks the button did nothing.
    /// </summary>
    private const int HOST_READY_TIMEOUT_MS = 3000;

    /// <summary>
    /// Max time for one probe (per address). Loopback refusal is instant, so on the defect case
    /// this is never seen; it only prevents a frame hang when nobody answers at all (firewall
    /// drops instead of refusing).
    /// </summary>
    private const int READINESS_ATTEMPT_MS = 150;

    /// <summary>
    /// `ipv4` / `nome` / `best-effort`: which route reached the host, for the `[Host] connesso
    /// via=` marker. Null means the last dial was not a "Host session" (it was "Connect"), and
    /// OnConnect writes no additional line for the manual path.
    /// </summary>
    private string _hostVia;

    private const int WIDTH = 330;
    private const int HEIGHT = 400;
}