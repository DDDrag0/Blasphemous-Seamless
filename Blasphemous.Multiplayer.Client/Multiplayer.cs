using Blasphemous.CheatConsole;
using Blasphemous.ModdingAPI;
using Blasphemous.ModdingAPI.Helpers;
using Blasphemous.ModdingAPI.Persistence;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Client.Host;
using Blasphemous.Multiplayer.Client.Map;
using Blasphemous.Multiplayer.Client.Network;
using Blasphemous.Multiplayer.Client.Notifications;
using Blasphemous.Multiplayer.Client.Ping;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Client.PvP;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

/// <summary>
/// Handles connecting to server, syncing progress, and tracking players
/// </summary>
public class Multiplayer : BlasMod, ISlotPersistentMod<MultiplayerSlotData>, IGlobalPersistentMod<MultiplayerGlobalData>
{
    internal Multiplayer() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    // Managers
    public AttackManager AttackManager { get; private set; }
    public MainPlayerManager MainPlayerManager { get; private set; }
    public Map.MapManager MapManager { get; private set; }
    public NetworkManager NetworkManager { get; private set; }
    public NotificationManager NotificationManager { get; private set; }
    public OtherPlayerManager OtherPlayerManager { get; private set; }
    public ProgressManager ProgressManager { get; private set; }
    public DamageCalculator DamageCalculator { get; private set; }
    public PingManager PingManager { get; private set; }

    // Game status
    public bool RandomizerMode => ModHelper.IsModLoadedByName("Randomizer");
    public Config config { get; private set; }
    public bool CurrentlyInLevel { get; private set; }

    // Player status
    public string PlayerName { get; private set; }
    public byte PlayerTeam { get; private set; }

    // Cleared on receiving a stat upgrade from a same-room player (outside randomizer); reset on
    // every level load; gates natural stat upgrades and their transmission.
    public bool CanObtainStatUpgrades { get; set; }

    /// <summary>The most recent connection info, used as the default on the connection screen.</summary>
    public ConnectionInfo LastConnectionInfo { get; set; } = new();

    protected override void OnInitialize()
    {
        LocalizationHandler.RegisterDefaultLanguage("en");

        AttackManager = new AttackManager();
        MainPlayerManager = new MainPlayerManager();
        MapManager = new Map.MapManager();
        NetworkManager = new NetworkManager();
        NotificationManager = new NotificationManager();
        OtherPlayerManager = new OtherPlayerManager();
        ProgressManager = new ProgressManager();
        DamageCalculator = new DamageCalculator();
        PingManager = new PingManager();

        NetworkManager.OnConnect += OnConnect;

        config = ConfigHandler.Load<Config>();
        PersistentStates.loadPersistentObjects();
        PlayerName = string.Empty;
        PlayerTeam = 1;

        // Cinematics probe: subscribes to public events, no patch.
        CutsceneProbe.Subscribe();

        // Life marker: subscribes to the two public static delegates of PenitentDamageArea, no patch.
        PenitentLifeMarker.Arm();

        // Steam lobby: reads the command line and registers lobby callbacks; retried from Tick if Steam
        // is not ready yet.
        SteamLobby.Arm();
    }

    /// <summary>New game resets life-guilt; the counter file is per-save, not per-slot.</summary>
    protected override void OnNewGame()
    {
        LifeGuilt.OnNewGame();
    }

    protected override void OnDispose()
    {
        // Must be raised before NetworkManager.Disconnect and HostSession.Stop: from here on no
        // CloseP2PSessionWithUser runs. Calling Steam's native close during shutdown crashes in
        // steam_api64; the process closes the sessions on exit.
        P2PShutdownGate.Enter();

        CutsceneProbe.Unsubscribe();

        // Disconnect sends the BYE (a Send, not a Close) so the peer learns immediately instead of
        // waiting for the 15s timeout.
        try { NetworkManager?.Disconnect(); } catch { }

        HostSession.Stop();
        SteamLobby.LeaveLobby();

        PenitentLifeMarker.Disarm();

        DeathCorpseFollow.Reset();
    }

    protected override void OnRegisterServices(ModServiceProvider provider)
    {
        provider.RegisterCommand(new MultiplayerCommand());
    }

    public void OnConnect(string ipAddress, string playerName, string password)
    {
        PlayerName = playerName;
    }

    public void OnDisconnect()
    {
        // A disconnect can arrive while this machine is still dead, and IsLocalPlayerDead would stay
        // true, so the per-frame close would never fire.
        DeathCorpseFollow.Reset();

        NotificationManager.DisplayNotification(LocalizationHandler.Localize("dcon"));
        ProgressManager.ResetProgressSentStatus();
        OtherPlayerManager.RemoveAllConnectedPlayers();
        OtherPlayerManager.RemoveAllActivePlayers();
        EnemySyncManager.SetHost(false); // The server reassigns the role on reconnect.
        PlayerName = string.Empty;
    }

    protected override void OnLevelLoaded(string oldLevel, string newLevel)
    {
        // EffectRelay reopens its state for the new scene. No Harmony target here; the relay is a
        // single postfix on PoolManager.ReuseObject.
        EffectRelay.LevelLoaded(newLevel);

        EnemySyncManager.LevelLoaded(newLevel);
        CurrentlyInLevel = newLevel != "MainMenu";
        NotificationManager.LevelLoaded();
        OtherPlayerManager.LevelLoaded(newLevel);
        ProgressManager.LevelLoaded(newLevel);
        CanObtainStatUpgrades = true;

        // [BossJoin] marker is armed here and printed one frame later: this event fires before the
        // scene is usable, so reading predicates now would measure the wrong frame.
        _bossJoinScene = newLevel;
        _bossJoinFrame = Time.frameCount;
        _bossJoinArmed = newLevel != "MainMenu";

        if (CurrentlyInLevel && NetworkManager.IsConnected)
        {
            ModLog.Info("Entering new scene: " + newLevel);

            MainPlayerManager.SendAllLocationData();
            NetworkManager.SendEnterScene(newLevel);
            ProgressManager.SendAllProgress();
        }

        if (newLevel == "D06Z01S01")
            FixElevatorLevers();
    }

    protected override void OnLevelUnloaded(string oldLevel, string newLevel)
    {
        if (CurrentlyInLevel && NetworkManager.IsConnected)
        {
            ModLog.Info("Leaving old scene");

            // Must run before SendLeaveScene: the server filters ownership by the sender's scene,
            // so a token queued after the leave is never delivered. Also needed because the moving
            // machine clears its own ownership tables in LevelLoaded while the peer keeps them.
            EnemySyncManager.ReleaseOwnedEnemiesBeforeLeaving();

            NetworkManager.SendLeaveScene();
        }

        CurrentlyInLevel = false;
    }

    protected override void OnLateUpdate()
    {
        // Runs Steam callbacks first so any arriving callback is delivered before the probes below
        // measure. Calling RunCallbacks here is redundant with the game's own SteamManager but makes
        // our callbacks independent of its lifecycle.
        SteamLobby.Tick();

        // P2P transport ticks: peer wire, then host adapter. Both gated internally and wrapped in
        // their own try/catch. Ordered after SteamLobby.Tick so P2PSessionRequest_t has been
        // delivered before the channels are read.
        SteamP2PTransport.Tick();
        P2PServerTransport.Tick();

        // [BossJoin]: one line per level load, one frame after OnLevelLoaded.
        BossJoinMarker();

        // EffectRelay arms and refreshes its name→prefab catalog once the boss exists.
        EffectRelay.Tick();

        CutsceneProbe.Tick();

        DeathScreenProbe.Tick();

        DeathScreenTransparency.Tick();

        LifeGuilt.Tick();

        BossDeathWarning.Tick();

        BossFightDeathWarning.Tick();

        DeathCorpseFollow.AttachOnce();
        DeathCorpseFollow.Tick();

        MainPlayerManager.CorpsePeerTick();

        PerformanceProbe.BeginLate();
        try
        {
            NetworkManager.ProcessQueue();

            MapManager.Update();
            NotificationManager.Update();

            // PauseNotice: information-only flag, no shared pause. Polled once per frame; runs
            // after packet processing so a freshly received flag applies this frame.
            PauseNotice.Tick();

            PingManager.OnUpdate();
            if (CurrentlyInLevel)
            {
                MainPlayerManager.Update();
                OtherPlayerManager.Update();
                ProgressManager.Update();
                EnemySyncManager.Update();
            }

            NetworkManager.SendQueue();
        }
        finally
        {
            PerformanceProbe.EndLate();
        }
    }

    // [BossJoin] state: OnLevelLoaded arms, next-frame OnLateUpdate prints.
    private bool _bossJoinArmed;
    private int _bossJoinFrame;
    private string _bossJoinScene = string.Empty;

    /// <summary>
    /// [BossJoin] marker: <c>scena · io · vivo · peerInScena · bossVivi</c>, one line per level
    /// load, one frame after <c>OnLevelLoaded</c>. Distinguishes "peer already in room" from
    /// "peer elsewhere" from "no peer connected". Never throws; prints the same marker with
    /// <c>?</c> on any read failure.
    /// </summary>
    private void BossJoinMarker()
    {
        if (!_bossJoinArmed || Time.frameCount <= _bossJoinFrame)
            return;

        _bossJoinArmed = false;

        try
        {
            bool vivo = !PlayerDeathSync.IsLocalPlayerDead();
            int peer = EnemySyncManager.AnyOtherPlayerInMyScene() ? 1 : 0;
            int tot = EnemySyncManager.ConnectedPlayersCount();
            int bossVivi = EnemySyncManager.CountLiveBossesInScene();

            ModLog.Info($"[BossJoin] scena={_bossJoinScene} · " +
                        $"io={(string.IsNullOrEmpty(PlayerName) ? "(no-name)" : PlayerName)} · " +
                        $"vivo={(vivo ? "true" : "false")} · peerInScena={peer}/{tot} · bossVivi={bossVivi}");
        }
        catch (System.Exception e)
        {
            ModLog.Info($"[BossJoin] scena={_bossJoinScene} · io=? · vivo=? · peerInScena=?/? · bossVivi=? " +
                        $"(lettura fallita: {e.GetType().Name}: {e.Message})");
        }
    }

    // Enemy sync network helpers
    public void SendEnemyUpdate(uint enemyHandle, Vector2 position, float currentLife, int animState, float animTime, int fingerprint, bool ownedBySender, byte orientation) => NetworkManager.SendEnemyUpdate(enemyHandle, position, currentLife, animState, animTime, fingerprint, ownedBySender, orientation);

    // Used for both directions (peer→host request, host→peers broadcast). The three extra bytes
    // carry damage type, element and flags so the receiver rebuilds the same Hit and the correct
    // reaction.
    public void SendEnemyDamageRequest(uint enemyHandle, float damageAmount, byte damageType,
                                       byte damageElement, byte flags)
        => NetworkManager.SendEnemyDamageRequest(enemyHandle, damageAmount, damageType, damageElement, flags);

    public void SendEnemyKill(uint enemyHandle) => NetworkManager.SendEnemyKill(enemyHandle);

    // Ownership token: only the host sends these, and the peer obeys them.
    public void SendEnemyOwnership(uint enemyHandle, bool ownedByReceiver) => NetworkManager.SendEnemyOwnership(enemyHandle, ownedByReceiver);

    // Scene-independent death record: reaches a player who was elsewhere when the kill happened.
    public void SendEnemyDeathGlobal(string enemyId) => NetworkManager.SendEnemyDeathGlobal(enemyId);

    // Called by NetworkManager to store name and team.
    public void SetPlayerData(string player, byte team)
    {
        PlayerName = player;
        PlayerTeam = team;
    }

    // Refresh players' nametags and map icons when someone changed teams.
    public void RefreshPlayerColors()
    {
        OtherPlayerManager.RefreshNametagColors();
        MapManager.QueueMapUpdate();
    }

    // Send initial data after successful connection, or display the refusal notification.
    private void OnConnect(bool success, RefusalType refusal)
    {
        if (!success)
        {
            string reason = refusal switch
            {
                RefusalType.Connection => "refcon",
                RefusalType.Protocol => "refpro",
                RefusalType.DuplicateIp => "refipa",
                RefusalType.DuplicateName => "refnam",
                RefusalType.Password => "refpas",
                RefusalType.PlayerLimit => "refmax",
                RefusalType.Banned => "refban",
                _ => "refunk",
            };

            NotificationManager.DisplayNotification($"{LocalizationHandler.Localize("refuse")}: {LocalizationHandler.Localize(reason)}");
            return;
        }

        NetworkManager.SendSkin(Core.ColorPaletteManager.GetCurrentColorPaletteId());
        NetworkManager.SendTeam(PlayerTeam);

        if (CurrentlyInLevel)
        {
            MainPlayerManager.SendAllLocationData();
            NetworkManager.SendEnterScene(Core.LevelManager.currentLevel.LevelName);
            OtherPlayerManager.AddNametag(PlayerName, true);
            ProgressManager.SendAllProgress();
        }

        NotificationManager.DisplayNotification(LocalizationHandler.Localize("con"));
    }

    // Disables natural stat upgrades when the sender is in the same room, outside randomizer.
    public void ProcessRecievedStat(string playerName, ProgressUpdate progress)
    {
        PlayerStatus player = OtherPlayerManager.FindConnectedPlayer(playerName);
        if (player == null)
            return;

        if (!CurrentlyInLevel || RandomizerMode || progress.Type != ProgressType.PlayerStat || Core.LevelManager.currentLevel.LevelName != player.CurrentScene)
            return;

        if (progress.Id == "LIFE" || progress.Id == "FERVOUR" || progress.Id == "STRENGTH" || progress.Id == "MEACULPA")
        {
            CanObtainStatUpgrades = false;
            ModLog.Warn("Received stat upgrade from player in the same room!");
        }
    }

    // Rooftops elevator: align the two levers with the flags the elevator has unlocked.
    private void FixElevatorLevers()
    {
        Lever[] levers = Object.FindObjectsOfType<Lever>();
        Lever downLever = null, UpLever = null;

        foreach (Lever lever in levers)
        {
            if (lever.GetPersistenID() == "fc76ec10-a6e2-4465-ade0-642520e84efc")
                downLever = lever;
            else if (lever.GetPersistenID() == "14b5e15b-2178-4677-8a54-468d5496037d")
                UpLever = lever;
        }
        if (UpLever == null || downLever == null)
        {
            ModLog.Warn("Could not find elevator levers!");
            return;
        }

        if (Core.Events.GetFlag("ELEVATOR_POSITION_1"))
        {
            downLever.SetLeverDownInstantly();
            if (Core.Events.GetFlag("ELEVATOR_POSITION_2_UNLOCKED"))
                UpLever.SetLeverUpInstantly();
        }
        else if (Core.Events.GetFlag("ELEVATOR_POSITION_2"))
        {
            downLever.SetLeverUpInstantly();
            if (Core.Events.GetFlag("ELEVATOR_POSITION_3_UNLOCKED"))
                UpLever.SetLeverUpInstantly();
        }
        else if (Core.Events.GetFlag("ELEVATOR_POSITION_3"))
        {
            downLever.SetLeverUpInstantly();
            if (Core.Events.GetFlag("ELEVATOR_FULL_UNLOCKED"))
                UpLever.SetLeverUpInstantly();
        }
    }

    /// <summary>Save list of interacted persistent objects</summary>
    public MultiplayerSlotData SaveSlot()
    {
        return new MultiplayerSlotData()
        {
            InteractedPersistenceObjects = ProgressManager.SaveInteractedObjects()
        };
    }

    /// <summary>Load list of interacted persistent objects</summary>
    public void LoadSlot(MultiplayerSlotData data)
    {
        ProgressManager.LoadInteractedObjects(data.InteractedPersistenceObjects);
    }

    /// <summary>Reset list of interacted persistent objects</summary>
    public void ResetSlot()
    {
        ProgressManager.ClearInteractedObjects();
    }

    /// <summary>Save last connection info</summary>
    public MultiplayerGlobalData SaveGlobal()
    {
        return new MultiplayerGlobalData()
        {
            LastConnection = LastConnectionInfo
        };
    }

    /// <summary>Load last connection info</summary>
    public void LoadGlobal(MultiplayerGlobalData data)
    {
        LastConnectionInfo = data.LastConnection;
    }
}