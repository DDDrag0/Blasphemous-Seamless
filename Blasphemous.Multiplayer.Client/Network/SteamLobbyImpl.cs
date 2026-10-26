using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Host;
using Blasphemous.Multiplayer.Common;
using Steamworks;
using System;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// Steam lobby and invites, additive on top of the existing transport. The lobby is a meeting
/// point, not a transport: it carries hostIp / hostPort / hostName / hostSteamId only, bytes
/// travel on the TCP transport as before. Names Steamworks types directly, so on a non-Steam
/// copy a TypeLoadException would surface at the call site; the reflection-gated
/// <see cref="SteamLobby"/> facade is the only caller and keeps the IP path working.
/// </summary>
internal static class SteamLobbyImpl
{
    internal const string MarkerAvailable = "[Steam] disponibile=";
    internal const string MarkerUnavailable = "[Steam] unavailable";
    internal const string MarkerFirstCallback = "[Steam] first callback received: ";
    internal const string MarkerHostCreate = "[Steam] host: CreateLobby ";
    internal const string MarkerHostCreated = "[Steam] host: lobby creata id=";
    internal const string MarkerHostFailed = "[Steam] host: creazione fallita ";
    internal const string MarkerHostData = "[Steam] host: SetLobbyData ";
    internal const string MarkerOverlayState = "[Steam] overlay abilitato=";
    internal const string MarkerOverlayOpen = "[Steam] apro l'overlay inviti lobby=";
    internal const string MarkerJoinRequest = "[Steam] join: richiesta lobby=";
    internal const string MarkerJoinEnter = "[Steam] join: LobbyEnter lobby=";
    internal const string MarkerJoinRefused = "[Steam] join rifiutato: ";
    internal const string MarkerJoinRequestData = "[Steam] join: RequestLobbyData lobby=";
    internal const string MarkerJoinData = "[Steam] join: LobbyDataUpdate lobby=";
    internal const string MarkerJoinNoData = "[Steam] join: dati mancanti ";
    internal const string MarkerJoinWaiting = "[Steam] join in attesa da ";
    internal const string MarkerJoinReady = "[Steam] join: dati pronti hostIp=";
    internal const string MarkerJoinDropped = "[Steam] join: scartato ";
    internal const string MarkerJoinReset = "[Steam] join: ingresso azzerato (link perso) lobby=";
    internal const string MarkerLobbyMember = "[Steam] lobby: membro cambiato ";
    internal const string MarkerLobbyOwner = "[Steam] lobby: proprietario=";
    internal const string MarkerHostLeft = "[Steam] l'host ha chiuso la sessione";
    internal const string MarkerLeft = "[Steam] lobby lasciata";
    internal const string MarkerTimeout = "[Steam] timeout: ";
    internal const string MarkerError = "[Steam] error: ";

    // Lobby data keys. No password here on purpose: the plan carries these three plus hostSteamId.
    internal const string KEY_IP = "hostIp";
    internal const string KEY_PORT = "hostPort";
    internal const string KEY_NAME = "hostName";

    /// <summary>
    /// Hosting SteamID. Needed because <c>GetLobbyOwner</c> transfers to the next member when the
    /// host leaves - a P2P transport asking Steam "who is host?" would then address the wrong peer.
    /// </summary>
    internal const string KEY_STEAM = "hostSteamId";

    /// <summary>Friends-only: direct invites, does not appear in global matchmaking.</summary>
    private const ELobbyType LOBBY_TYPE = ELobbyType.k_ELobbyTypeFriendsOnly;

    /// <summary>Four slots.</summary>
    private const int MAX_MEMBERS = 4;

    /// <summary>The only <c>EChatRoomEnterResponse</c> meaning "joined".</summary>
    private const uint ENTER_SUCCESS = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;

    /// <summary>Retry cap for RequestLobbyData when data has not replicated yet.</summary>
    private const int MAX_DATA_RETRIES = 3;

    /// <summary>Availability re-probe interval while the answer is "no".</summary>
    private const float RECHECK_SECONDS = 2f;

    /// <summary>Wait before reporting that LobbyCreated_t never arrived.</summary>
    private const float HOST_CALLBACK_TIMEOUT_S = 10f;

    /// <summary>Warn threshold for "joined but data still empty" - visible block instead of a silent stall.</summary>
    private const float JOIN_DATA_WAIT_WARN_S = 5f;

    /// <summary>Wait for LobbyEnter_t / LobbyDataUpdate_t before declaring the callback dead.</summary>
    private const float JOIN_CALLBACK_TIMEOUT_S = 15f;

    // Callback<T>.Create pins the delegate in a global registry via GCHandle. A local Callback
    // would be collected and the callbacks would silently stop; fields keep them alive.
    private static Callback<LobbyCreated_t> _cbLobbyCreated;
    private static Callback<LobbyEnter_t> _cbLobbyEnter;
    private static Callback<LobbyDataUpdate_t> _cbLobbyDataUpdate;
    private static Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
    private static Callback<LobbyChatUpdate_t> _cbLobbyChatUpdate;

    private static bool _available;          // Steam present and logged in (sticky once true)
    private static bool _availabilityLogged; // one line per transition, not per frame
    private static float _nextAvailabilityCheck;
    private static bool _callbacksReady;
    private static bool _firstCallbackLogged;

    private static CSteamID _hostLobby = CSteamID.Nil;
    private static bool _creatingLobby;
    private static float _creatingSince;
    private static string _hostName = string.Empty;
    private static bool _hostLobbyReady;

    private static CSteamID _joinLobby = CSteamID.Nil;

    /// <summary>
    /// Last lobby we actually joined, surviving <c>ResetJoinAfterLinkLost</c>. After a link loss
    /// <c>_joinLobby</c> is reset to prevent silently swallowing the next invite, but the host's
    /// lobby is still alive and a manual P2P Connect and <c>IsLobbyMember</c> need to name it.
    /// </summary>
    private static CSteamID _lastJoinLobby = CSteamID.Nil;

    private static bool _joinEntered;
    private static float _joinSince;
    private static float _joinDataSince;
    private static bool _joinWaitingLogged;
    private static int _joinDataRetries;
    private static bool _joinRetryPending;
    private static bool _joinDataDone;
    private static bool _hostLeftReported;

    private static bool _joinReady;
    private static string _joinIp = string.Empty;
    private static int _joinPort;

    private static ulong _cmdLineLobbyId;
    private static bool _cmdLineRead;
    private static bool _cmdLineJoinStarted;

    /// <summary>True when Steam is present and usable - the UI gate.</summary>
    internal static bool Available
    {
        get
        {
            CheckAvailability();
            return _available;
        }
    }

    /// <summary>True while a Steam lobby of ours is active (host).</summary>
    internal static bool HasHostLobby
    {
        get { return _hostLobby.m_SteamID != 0UL; }
    }

    /// <summary>Lobby id we are hosting (0 if none).</summary>
    internal static ulong HostLobbyId
    {
        get { return _hostLobby.m_SteamID; }
    }

    /// <summary>
    /// The lobby we are in: hosted lobby if any, else the joined one, else the last joined one.
    /// The last one survives a link-loss reset so a manual P2P Connect can still name it.
    /// </summary>
    internal static CSteamID CurrentLobby
    {
        get
        {
            if (_hostLobby.m_SteamID != 0UL)
                return _hostLobby;

            if (_joinLobby.m_SteamID != 0UL)
                return _joinLobby;

            return _lastJoinLobby;
        }
    }

    /// <summary>
    /// True when the user is a member of one of our lobbies. Gate for accepting P2P sessions: only
    /// lobby members are accepted, so a stranger knowing the SteamID cannot open one. Checks both
    /// the hosted and joined lobbies plus the last-joined memory (post-link-loss re-entry).
    /// </summary>
    internal static bool IsLobbyMember(CSteamID user)
    {
        try
        {
            if (user.m_SteamID == 0UL)
                return false;

            if (IsMemberOf(_hostLobby, user))
                return true;

            if (IsMemberOf(_joinLobby, user))
                return true;

            return IsMemberOf(_lastJoinLobby, user);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsMemberOf(CSteamID lobby, CSteamID user)
    {
        if (lobby.m_SteamID == 0UL)
            return false;

        int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < members; i++)
        {
            CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (member.m_SteamID == user.m_SteamID)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Called once at mod init: reads +connect_lobby (game-was-off case) and registers callbacks
    /// if Steam is ready. Idempotent; if Steam is not ready yet, Tick retries.
    /// </summary>
    internal static void Arm()
    {
        try
        {
            if (!_cmdLineRead)
            {
                _cmdLineRead = true;

                ulong lobbyId;
                if (SteamInviteCommandLine.TryParse(Environment.GetCommandLineArgs(), out lobbyId))
                {
                    _cmdLineLobbyId = lobbyId;
                    ModLog.Info(MarkerJoinRequest + lobbyId + " origine=cmdline (" +
                                SteamInviteCommandLine.ARGUMENT + ")");
                }
            }

            CheckAvailability();

            if (_available)
                EnsureCallbacks();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Per-frame: pumps Steam callbacks and drains pending actions. Runs twice per frame (the
    /// game's SteamManager also calls RunCallbacks) - the second call sees an empty queue, and
    /// running the pump here makes us independent from the game's SteamManager lifecycle.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            CheckAvailability();

            if (!_available)
                return;

            if (!_callbacksReady && !EnsureCallbacks())
                return;

            if (SteamAPI.IsSteamRunning())
                SteamAPI.RunCallbacks();

            if (_cmdLineLobbyId != 0UL && !_cmdLineJoinStarted)
            {
                _cmdLineJoinStarted = true;
                StartJoin(new CSteamID(_cmdLineLobbyId), "cmdline");
            }

            if (_joinRetryPending)
            {
                _joinRetryPending = false;
                RequestJoinData();
            }

            CheckTimeouts();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Steam: Host - creates the lobby but does NOT start the session. The host session starts
    /// later via the existing "Host session" path once LobbyCreated_t fires and the ready flag is
    /// consumed, so a lobby never advertises a port where nothing listens.
    /// </summary>
    internal static bool StartHost(string hostName)
    {
        try
        {
            CheckAvailability();

            if (!_available)
            {
                ModLog.Warn(MarkerUnavailable + ": StartHost requested but Steam is unavailable");
                return false;
            }

            if (!EnsureCallbacks())
                return false;

            _hostName = hostName ?? string.Empty;

            // A lobby of ours already exists: do not create a second one; reopen the invite overlay
            // instead (that is what the second click wants).
            if (HasHostLobby)
            {
                OpenInviteOverlay("riapertura");
                return true;
            }

            // Double-click while creation is in flight: let that one finish.
            if (_creatingLobby)
                return true;

            _creatingLobby = true;
            _creatingSince = Time.realtimeSinceStartup;
            _hostLobbyReady = false;

            SteamAPICall_t call = SteamMatchmaking.CreateLobby(LOBBY_TYPE, MAX_MEMBERS);
            ModLog.Info(MarkerHostCreate + "tipo=friendsOnly posti=" + MAX_MEMBERS +
                        " chiamata=" + call.m_SteamAPICall);
            return true;
        }
        catch (Exception e)
        {
            _creatingLobby = false;
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (CreateLobby)");
            return false;
        }
    }

    /// <summary>
    /// Leaves the current lobby (host or guest) and clears state. Idempotent: no-op and no log
    /// line when there is no lobby, so Disconnect for a non-Steam user adds nothing.
    /// </summary>
    internal static void LeaveLobby()
    {
        try
        {
            CSteamID host = _hostLobby;
            CSteamID join = _joinLobby;

            ResetHostState();
            ResetJoinState();

            // A voluntary leave clears the last-joined memory; a link-loss reset keeps it.
            _lastJoinLobby = CSteamID.Nil;

            if (host.m_SteamID != 0UL)
            {
                SteamMatchmaking.LeaveLobby(host);
                ModLog.Info(MarkerLeft + " id=" + host.m_SteamID + " (host)");
            }

            if (join.m_SteamID != 0UL && join.m_SteamID != host.m_SteamID)
            {
                SteamMatchmaking.LeaveLobby(join);
                ModLog.Info(MarkerLeft + " id=" + join.m_SteamID + " (join)");
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LeaveLobby)");
        }
    }

    /// <summary>Consumes the "lobby ready" flag once (set by LobbyCreated_t); the UI starts the host.</summary>
    internal static bool TryConsumeHostLobbyReady()
    {
        if (!_hostLobbyReady)
            return false;

        _hostLobbyReady = false;
        return true;
    }

    /// <summary>Consumes the join address once (cache read in LobbyEnter_t, or LobbyDataUpdate_t).</summary>
    internal static bool TryConsumeJoin(out string serverIp, out int port)
    {
        serverIp = _joinIp;
        port = _joinPort;

        if (!_joinReady)
            return false;

        _joinReady = false;
        return true;
    }

    /// <summary>
    /// Discards pending actions while connected: an invite must not reconnect by itself later, and
    /// an untriggered host lobby would announce a closed port. No-op and no log when there is
    /// nothing pending.
    /// </summary>
    internal static void DropPendingWhileConnected()
    {
        try
        {
            // Only the unconsumed flag is a "pending" action - being in a lobby is the normal state
            // of a player already in a session, and must not trigger a notification per frame.
            if (_joinReady)
            {
                ModLog.Warn(MarkerJoinDropped + "already connected to a session: invite not applied");
                _joinReady = false;
                Notify("Steam: already connected, invite ignored");
            }

            if (_hostLobbyReady && !HostSession.IsRunning)
            {
                _hostLobbyReady = false;
                LeaveLobby();
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (DropPending)");
        }
    }

    // === Callbacks ===

    /// <summary>
    /// LobbyCreated_t: publishes the four lobby data keys and opens the invite overlay. Does NOT
    /// read data (a GetLobbyData right after SetLobbyData returns empty until the back end
    /// replicates). The overlay is opened only on OK - a failed create with the overlay would show
    /// an empty friend list.
    /// </summary>
    private static void OnLobbyCreated(LobbyCreated_t result)
    {
        NoteCallback("LobbyCreated_t");

        try
        {
            if (result.m_eResult != EResult.k_EResultOK)
            {
                _creatingLobby = false;
                _hostLobbyReady = false;
                ModLog.Error(MarkerHostFailed + "risultato=" + result.m_eResult);
                Notify("Steam: creazione lobby fallita (" + result.m_eResult + ")");
                return;
            }

            _creatingLobby = false;
            _hostLobby = new CSteamID(result.m_ulSteamIDLobby);

            ModLog.Info(MarkerHostCreated + _hostLobby.m_SteamID);

            string ip = HostSession.LanAddress;
            int port = Protocol.DEFAULT_PORT;
            ulong hostSteamId = SteamUser.GetSteamID().m_SteamID;

            bool okIp = SteamMatchmaking.SetLobbyData(_hostLobby, KEY_IP, ip);
            bool okPort = SteamMatchmaking.SetLobbyData(_hostLobby, KEY_PORT, port.ToString());
            bool okName = SteamMatchmaking.SetLobbyData(_hostLobby, KEY_NAME, _hostName ?? string.Empty);
            bool okSteam = SteamMatchmaking.SetLobbyData(_hostLobby, KEY_STEAM, hostSteamId.ToString());

            ModLog.Info(MarkerHostData + KEY_IP + "=" + ip + " " + KEY_PORT + "=" + port +
                        " " + KEY_NAME + "=" + (_hostName ?? string.Empty) +
                        " " + KEY_STEAM + "=" + hostSteamId +
                        " esiti=" + (okIp ? "1" : "0") + (okPort ? "1" : "0") + (okName ? "1" : "0") +
                        (okSteam ? "1" : "0"));

            if (string.IsNullOrEmpty(ip) || ip == "unknown")
                ModLog.Warn(MarkerHostData + "warning: LAN IP was not resolved (" +
                            (ip ?? "(vuoto)") + "): the lobby carries no useful address");

            OpenInviteOverlay("creazione");

            _hostLobbyReady = true;
        }
        catch (Exception e)
        {
            _creatingLobby = false;
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyCreated_t)");
        }
    }

    /// <summary>
    /// GameLobbyJoinRequested_t: the game-is-already-open case. The callback only says the invite
    /// was accepted; JoinLobby is issued here and the actual entry arrives in LobbyEnter_t.
    /// </summary>
    private static void OnJoinRequested(GameLobbyJoinRequested_t request)
    {
        NoteCallback("GameLobbyJoinRequested_t");

        try
        {
            CSteamID lobby = request.m_steamIDLobby;
            ModLog.Info(MarkerJoinRequest + lobby.m_SteamID + " origine=callback (amico=" +
                        request.m_steamIDFriend.m_SteamID + ")");
            StartJoin(lobby, "callback");
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (GameLobbyJoinRequested_t)");
        }
    }

    /// <summary>
    /// LobbyEnter_t: "did we enter?". Only <c>m_EChatRoomEnterResponse == 1</c> means success;
    /// every other code is reported with its reason. Data is read IMMEDIATELY from the cache -
    /// Steam does not emit LobbyDataUpdate_t when the data is already cached, so waiting always
    /// would hang the join; only when the cache is empty is RequestLobbyData issued and the update
    /// awaited.
    /// </summary>
    private static void OnLobbyEnter(LobbyEnter_t enter)
    {
        NoteCallback("LobbyEnter_t");

        try
        {
            CSteamID lobby = new CSteamID(enter.m_ulSteamIDLobby);
            uint response = enter.m_EChatRoomEnterResponse;
            string reason = EnterReason(response);

            ModLog.Info(MarkerJoinEnter + lobby.m_SteamID + " risposta=" + response +
                        (response == ENTER_SUCCESS ? " (success)" : " (" + reason + ")"));

            // Our own lobby (SDK-dependent): no join, do not read.
            if (lobby.m_SteamID == _hostLobby.m_SteamID && _hostLobby.m_SteamID != 0UL)
            {
                ModLog.Info(MarkerJoinEnter + "is our own lobby: no join needed");
                return;
            }

            if (response != ENTER_SUCCESS)
            {
                ModLog.Error(MarkerJoinRefused + reason + " (risposta=" + response + ", lobby=" + lobby.m_SteamID + ")");
                Notify("Steam: invite not accepted - " + reason);
                ResetJoinState();
                return;
            }

            _joinLobby = lobby;
            _joinEntered = true;
            _joinDataSince = Time.realtimeSinceStartup;
            _joinWaitingLogged = false;
            _joinDataRetries = 0;
            _joinDataDone = false;
            _hostLeftReported = false;

            _lastJoinLobby = lobby;

            if (TryReadJoinData("cache"))
                return;

            RequestJoinData();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyEnter_t)");
        }
    }

    /// <summary>
    /// LobbyDataUpdate_t: the fallback after RequestLobbyData. Bounded retries (MAX_DATA_RETRIES);
    /// m_bSuccess == 0 is reported instead of being treated as empty data.
    /// </summary>
    private static void OnLobbyDataUpdate(LobbyDataUpdate_t update)
    {
        NoteCallback("LobbyDataUpdate_t");

        try
        {
            CSteamID lobby = new CSteamID(update.m_ulSteamIDLobby);

            if (_joinLobby.m_SteamID == 0UL || lobby.m_SteamID != _joinLobby.m_SteamID)
                return;

            string ip = SteamMatchmaking.GetLobbyData(lobby, KEY_IP);
            string portText = SteamMatchmaking.GetLobbyData(lobby, KEY_PORT);
            string hostName = SteamMatchmaking.GetLobbyData(lobby, KEY_NAME);

            ModLog.Info(MarkerJoinData + lobby.m_SteamID + " successo=" + update.m_bSuccess +
                        " " + KEY_IP + "=" + (ip ?? string.Empty) +
                        " " + KEY_PORT + "=" + (portText ?? string.Empty) +
                        " " + KEY_NAME + "=" + (hostName ?? string.Empty));

            if (update.m_bSuccess == 0)
            {
                ModLog.Error(MarkerJoinNoData + "LobbyDataUpdate_t con successo=0 lobby=" + lobby.m_SteamID);
                RetryJoinData();
                return;
            }

            if (TryReadJoinData("update"))
                return;

            ModLog.Warn(MarkerJoinNoData + "hostIp/hostPort not readable yet (ip='" +
                        (ip ?? string.Empty) + "' porta='" + (portText ?? string.Empty) + "')");
            RetryJoinData();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyDataUpdate_t)");
        }
    }

    /// <summary>
    /// LobbyChatUpdate_t: detects the host leaving. When the owner leaves, ownership transfers to
    /// the next member - so "someone left AND I am owner now" means the previous host is gone.
    /// Fires once (<c>_hostLeftReported</c>) and only for a guest, not for the host watching a
    /// friend leave.
    /// </summary>
    private static void OnLobbyChatUpdate(LobbyChatUpdate_t update)
    {
        NoteCallback("LobbyChatUpdate_t");

        try
        {
            CSteamID lobby = new CSteamID(update.m_ulSteamIDLobby);

            if (_joinLobby.m_SteamID == 0UL || lobby.m_SteamID != _joinLobby.m_SteamID)
                return;

            uint change = update.m_rgfChatMemberStateChange;
            bool leaving = (change & (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft) != 0 ||
                           (change & (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected) != 0 ||
                           (change & (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked) != 0;

            ModLog.Info(MarkerLobbyMember + "lobby=" + lobby.m_SteamID +
                        " membro=" + update.m_ulSteamIDUserChanged + " cambiamento=" + change);

            if (!leaving || !_joinEntered || _hostLeftReported)
                return;

            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            CSteamID me = SteamUser.GetSteamID();
            bool iAmOwnerNow = owner.m_SteamID == me.m_SteamID;

            ModLog.Info(MarkerLobbyOwner + owner.m_SteamID + " io=" + me.m_SteamID +
                        " (ioProprietario=" + (iAmOwnerNow ? "si" : "no") + ")");

            if (!iAmOwnerNow)
                return;

            _hostLeftReported = true;

            ModLog.Warn(MarkerHostLeft + " lobby=" + lobby.m_SteamID +
                        ": the lobby owner is now me, so the previous host left");

            Notify("Steam: l'host ha chiuso la sessione");

            // Disconnect only if a connection actually exists: NetworkManager.Disconnect does not
            // guard against a null transport, and this runs from a Steam callback.
            if (Main.Multiplayer != null && Main.Multiplayer.NetworkManager != null &&
                Main.Multiplayer.NetworkManager.IsConnected)
            {
                Main.Multiplayer.NetworkManager.Disconnect();
            }

            LeaveLobby();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyChatUpdate_t)");
        }
    }

    /// <summary>
    /// Reads hostIp/hostPort from the joined lobby - the single path used by both LobbyEnter_t
    /// (cache) and LobbyDataUpdate_t (fallback). Returns false if the fields are absent or invalid.
    /// <paramref name="where"/> labels the source in the log. The join notification fires only on
    /// the first read (<c>_joinDataDone</c>) because both paths may find the data.
    /// </summary>
    private static bool TryReadJoinData(string where)
    {
        try
        {
            if (_joinLobby.m_SteamID == 0UL)
                return false;

            string ip = SteamMatchmaking.GetLobbyData(_joinLobby, KEY_IP);
            string portText = SteamMatchmaking.GetLobbyData(_joinLobby, KEY_PORT);
            string hostName = SteamMatchmaking.GetLobbyData(_joinLobby, KEY_NAME);

            int port;
            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(portText) ||
                !int.TryParse(portText, out port) || port <= 0 || port > 65535)
                return false;

            bool firstRead = !_joinDataDone;

            _joinIp = ip;
            _joinPort = port;
            _joinReady = true;
            _joinDataDone = true;

            ModLog.Info(MarkerJoinReady + ip + " porta=" + port +
                        " host=" + (hostName ?? string.Empty) + " da=" + where);

            // The notification does not name an IP: invites travel P2P (ConnectionDisplay writes
            // the "p2p" token into the Server IP field), so printing an address here would
            // announce a connection that is not the one being made. The IP stays in the log above.
            if (firstRead)
                Notify("Steam: invito accettato, connessione P2P");

            return true;
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (GetLobbyData)");
            return false;
        }
    }

    /// <summary>Human-readable reason for an EChatRoomEnterResponse; each reason says what to fix.</summary>
    private static string EnterReason(uint response)
    {
        switch ((EChatRoomEnterResponse)response)
        {
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess:
                return "success";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                return "the session no longer exists (stale invite)";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                return "no permission: check that you are friends on Steam";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                return "the session is full";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                return "you have been banned from this lobby";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited:
                return "lobby in limited mode";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou:
                return "the other player has blocked you on Steam";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember:
                return "you have blocked the other player on Steam";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseError:
                return "generic lobby error";

            default:
                return "unknown response (" + response + ")";
        }
    }

    // === Helpers ===

    /// <summary>Enters a lobby - single entry point for both the cmdline and the callback case.</summary>
    private static void StartJoin(CSteamID lobby, string origin)
    {
        try
        {
            if (lobby.m_SteamID == 0UL)
                return;

            // Already connected to a TCP session: discard the invite and say so.
            if (IsConnectedNow())
            {
                ModLog.Warn(MarkerJoinDropped + "already connected to a session: lobby=" + lobby.m_SteamID +
                            " origine=" + origin);
                Notify("Steam: already connected, invite ignored");
                return;
            }

            // A join in progress does not start a second one; the rejection is visible on screen,
            // not only in the log.
            if (_joinLobby.m_SteamID != 0UL)
            {
                ModLog.Info(MarkerJoinRequest + lobby.m_SteamID + " ignored: a join is already in progress (id=" +
                            _joinLobby.m_SteamID + ")");
                Notify("Steam: invite ignored, a join is already in progress");
                return;
            }

            _joinLobby = lobby;
            _joinEntered = false;
            _joinSince = Time.realtimeSinceStartup;
            _joinDataSince = Time.realtimeSinceStartup;
            _joinWaitingLogged = false;
            _joinDataRetries = 0;
            _joinDataDone = false;
            _joinReady = false;
            _hostLeftReported = false;
            _joinIp = string.Empty;
            _joinPort = 0;

            SteamAPICall_t call = SteamMatchmaking.JoinLobby(lobby);
            ModLog.Info(MarkerJoinRequest + lobby.m_SteamID + " -> JoinLobby origine=" + origin +
                        " chiamata=" + call.m_SteamAPICall);
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (JoinLobby)");
        }
    }

    /// <summary>Is a session open right now? Reads NetworkManager without touching it.</summary>
    private static bool IsConnectedNow()
    {
        try
        {
            return Main.Multiplayer != null &&
                   Main.Multiplayer.NetworkManager != null &&
                   Main.Multiplayer.NetworkManager.IsConnected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void RequestJoinData()
    {
        try
        {
            if (_joinLobby.m_SteamID == 0UL)
                return;

            bool requested = SteamMatchmaking.RequestLobbyData(_joinLobby);
            ModLog.Info(MarkerJoinRequestData + _joinLobby.m_SteamID + " inviata=" + (requested ? "si" : "no"));
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (RequestLobbyData)");
        }
    }

    /// <summary>
    /// Schedules another RequestLobbyData via Tick (not from inside the callback, to avoid a
    /// re-entrant burst). Bounded by MAX_DATA_RETRIES.
    /// </summary>
    private static void RetryJoinData()
    {
        if (_joinDataRetries >= MAX_DATA_RETRIES)
        {
            ModLog.Error(MarkerJoinNoData + "after " + MAX_DATA_RETRIES + " attempts: lobby " +
                        _joinLobby.m_SteamID + " does not carry " + KEY_IP + "/" + KEY_PORT +
                        " (host did not publish, or data did not replicate)");
            return;
        }

        _joinDataRetries++;
        _joinRetryPending = true;
    }

    /// <summary>
    /// Opens the invite overlay. The overlay-enabled state is logged BEFORE the call: if the
    /// overlay is disabled in Steam settings, the call fails silently (no exception, no return
    /// code) and would otherwise look like a dead button.
    /// </summary>
    private static void OpenInviteOverlay(string reason)
    {
        try
        {
            bool enabled = SteamUtils.IsOverlayEnabled();
            ModLog.Info(MarkerOverlayState + (enabled ? "yes" : "no") + " (motivo=" + reason + ")");
            ModLog.Info(MarkerOverlayOpen + _hostLobby.m_SteamID + " motivo=" + reason);

            SteamFriends.ActivateGameOverlayInviteDialog(_hostLobby);

            if (!enabled)
            {
                ModLog.Warn(MarkerOverlayState + "no: invites will not open. Steam settings -> " +
                            "In-Game -> 'Enable the Steam Overlay'");
                Notify("Steam: the overlay is off, the invite cannot open (see log)");
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (ActivateGameOverlayInviteDialog)");
        }
    }

    /// <summary>
    /// Detects missing callbacks: LobbyCreated_t after CreateLobby, LobbyEnter_t/LobbyDataUpdate_t
    /// after JoinLobby, and a "join in attesa" warning after JOIN_DATA_WAIT_WARN_S. Without these,
    /// a stalled callback is a silent dead button.
    /// </summary>
    private static void CheckTimeouts()
    {
        float now = Time.realtimeSinceStartup;

        if (_creatingLobby && now - _creatingSince >= HOST_CALLBACK_TIMEOUT_S)
        {
            _creatingLobby = false;
            ModLog.Error(MarkerTimeout + "no LobbyCreated_t within " + HOST_CALLBACK_TIMEOUT_S +
                         " s from CreateLobby: callbacks are not arriving (pump stalled?)");
            Notify("Steam: nessuna risposta da Steam per la lobby (vedi log)");
        }

        if (_joinLobby.m_SteamID == 0UL || _joinDataDone)
            return;

        float waited = now - (_joinEntered ? _joinDataSince : _joinSince);

        if (_joinEntered && !_joinWaitingLogged && waited >= JOIN_DATA_WAIT_WARN_S)
        {
            _joinWaitingLogged = true;
            ModLog.Warn(MarkerJoinWaiting + (int)waited + " s: lobby data empty (lobby=" +
                        _joinLobby.m_SteamID + "): host has not published " + KEY_IP + "/" +
                        KEY_PORT + " yet, or replication did not arrive");
        }

        if (waited >= JOIN_CALLBACK_TIMEOUT_S)
        {
            ModLog.Error(MarkerTimeout + (_joinEntered ? "no LobbyDataUpdate_t" : "no LobbyEnter_t") +
                         " within " + JOIN_CALLBACK_TIMEOUT_S + " s for lobby " + _joinLobby.m_SteamID +
                         ": callbacks are not arriving (pump stalled?)");
            Notify("Steam: nessuna risposta da Steam per l'invito (vedi log)");
            ResetJoinState();
        }
    }

    /// <summary>
    /// Registers the five Callback&lt;T&gt; once, and logs availability. Only registers when Steam
    /// is ready - Callback.Create talks to native API and registering before the game's Init would
    /// fail where we do not control.
    /// </summary>
    private static bool EnsureCallbacks()
    {
        if (_callbacksReady)
            return true;

        CheckAvailability();
        if (!_available)
            return false;

        try
        {
            _cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _cbLobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _cbLobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            _cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            _cbLobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);

            _callbacksReady = true;

            ModLog.Info(MarkerAvailable + "si steamId=" + SteamUser.GetSteamID().m_SteamID +
                        " callbacks=5 (LobbyCreated_t, LobbyEnter_t, LobbyDataUpdate_t, " +
                        "GameLobbyJoinRequested_t, LobbyChatUpdate_t)");
            return true;
        }
        catch (Exception e)
        {
            _callbacksReady = false;
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Callback<T>.Create)");
            return false;
        }
    }

    /// <summary>
    /// Steam-present probe: IsSteamRunning + BLoggedOn + valid SteamID. Sticky once true; while
    /// false it re-probes every RECHECK_SECONDS. Cost is one native call per interval, only while
    /// the answer is "no".
    /// </summary>
    private static void CheckAvailability()
    {
        if (_available)
            return;

        float now = Time.realtimeSinceStartup;
        if (now < _nextAvailabilityCheck)
            return;

        _nextAvailabilityCheck = now + RECHECK_SECONDS;

        try
        {
            _available = SteamAPI.IsSteamRunning() && SteamUser.BLoggedOn() && SteamUser.GetSteamID().IsValid();
        }
        catch (Exception e)
        {
            _available = false;
            if (!_availabilityLogged)
            {
                _availabilityLogged = true;
                ModLog.Info(MarkerUnavailable + ": " + e.GetType().Name + ": " + e.Message +
                            " (Steam button disabled, IP buttons unchanged)");
            }
            return;
        }

        if (!_availabilityLogged)
        {
            _availabilityLogged = true;

            if (!_available)
                ModLog.Info(MarkerUnavailable + ": SteamAPI.IsSteamRunning()=no or no account logged in " +
                            "(Steam button disabled, IP buttons unchanged)");
        }
    }

    /// <summary>Logs the first callback received - proof the callback pump is running.</summary>
    private static void NoteCallback(string type)
    {
        if (_firstCallbackLogged)
            return;

        _firstCallbackLogged = true;
        ModLog.Info(MarkerFirstCallback + type);
    }

    private static void ResetHostState()
    {
        _hostLobby = CSteamID.Nil;
        _creatingLobby = false;
        _hostLobbyReady = false;
    }

    /// <summary>
    /// Link-loss reset: clears the pending join so the next invite is not rejected by
    /// <c>StartJoin</c> as "a join is already in progress". Does NOT call LeaveLobby - the host's
    /// lobby is still alive, and leaving would make this player a non-member that the host would
    /// reject on P2P accept. <c>_lastJoinLobby</c> keeps the lobby nameable for a manual Connect
    /// and for <c>IsLobbyMember</c>. No-op (and no log) when no join is in progress.
    /// </summary>
    internal static void ResetJoinAfterLinkLost(string reason)
    {
        try
        {
            CSteamID join = _joinLobby;

            if (join.m_SteamID == 0UL)
                return;

            // The lobby we host is not a "join": keep it.
            if (_hostLobby.m_SteamID != 0UL && join.m_SteamID == _hostLobby.m_SteamID)
                return;

            _lastJoinLobby = join;

            ModLog.Warn(MarkerJoinReset + join.m_SteamID + " motivo=" + (reason ?? "(not stated)") +
                        ": the next invite will not find the gate closed (the lobby stays: the " +
                        "memory is used by the manual Connect)");

            ResetJoinState();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (ResetJoinAfterLinkLost)");
        }
    }

    /// <summary>
    /// Clears the join state. Does NOT clear <c>_lastJoinLobby</c>: that is a memory that only a
    /// voluntary <c>LeaveLobby</c> drops.
    /// </summary>
    private static void ResetJoinState()
    {
        _joinLobby = CSteamID.Nil;
        _joinEntered = false;
        _joinReady = false;
        _joinRetryPending = false;
        _joinDataRetries = 0;
        _joinWaitingLogged = false;
        _joinDataSince = 0f;
        _joinDataDone = false;
        _hostLeftReported = false;
        _joinIp = string.Empty;
        _joinPort = 0;
    }

    /// <summary>Notifies the player, guarded because the mod's UI may not exist yet.</summary>
    private static void Notify(string message)
    {
        try
        {
            if (Main.Multiplayer != null && Main.Multiplayer.NotificationManager != null)
                Main.Multiplayer.NotificationManager.DisplayNotification(message);
        }
        catch (Exception)
        {
            // The log line already exists; the notification is a bonus.
        }
    }
}