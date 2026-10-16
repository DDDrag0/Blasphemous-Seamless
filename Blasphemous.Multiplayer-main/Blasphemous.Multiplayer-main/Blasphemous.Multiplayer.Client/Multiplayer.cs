using Blasphemous.CheatConsole;
using Blasphemous.ModdingAPI;
using Blasphemous.ModdingAPI.Helpers;
using Blasphemous.ModdingAPI.Persistence;
using Blasphemous.Multiplayer.Client.Data;
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

    // Set to false when receiving a stat upgrade from someone in the same room & not in randomizer
    // Set to true upon loading a new scene
    // Must be true to naturally obtain stat upgrades and send them
    public bool CanObtainStatUpgrades { get; set; }

    /// <summary>
    /// The most recent connectioninfo that should be used as the default
    /// </summary>
    public ConnectionInfo LastConnectionInfo { get; set; } = new();

    protected override void OnInitialize()
    {
        LocalizationHandler.RegisterDefaultLanguage("en");

        // Create managers
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

        // Initialize data
        config = ConfigHandler.Load<Config>();
        PersistentStates.loadPersistentObjects();
        PlayerName = string.Empty;
        PlayerTeam = 1;

        // TEMPORARY (CutsceneProbe): PROBE #2 - subscription to the PUBLIC Cinematics events. No patch,
        // no behaviour: see CutsceneProbe.cs. If Core.Cinematics is not up yet it retries from LateUpdate.
        CutsceneProbe.Subscribe();
    }

    // TEMPORARY (CutsceneProbe): the probe disarms itself on shutdown, so the events are never left
    // subscribed on a dying object. Log only.
    protected override void OnDispose()
    {
        CutsceneProbe.Unsubscribe();

        // TEMPORARY (DeathCorpseFollow): the death window can be open when the mod shuts down, and the
        // copy is a GameObject of its own - nothing else would ever take it away.
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
        // TEMPORARY (DeathCorpseFollow): a disconnect can arrive while this machine is STILL DEAD, and in
        // that moment IsLocalPlayerDead() stays true - the game knows nothing about the network - so the
        // per-frame close in Tick() would never fire. Without this line a copy and a hidden renderer would
        // stay in the scene with no living player left to follow. See DeathCorpseFollow.Reset().
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
        EnemySyncManager.LevelLoaded(newLevel);
        CurrentlyInLevel = newLevel != "MainMenu";
        NotificationManager.LevelLoaded();
        OtherPlayerManager.LevelLoaded(newLevel);
        ProgressManager.LevelLoaded(newLevel);
        CanObtainStatUpgrades = true;

        if (CurrentlyInLevel && NetworkManager.IsConnected)
        {
            // Entered a new scene
            ModLog.Info("Entering new scene: " + newLevel);

            // Send location, scene, and progress
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
            // Left a scene
            ModLog.Info("Leaving old scene");

            // GIVE THE ENEMIES BACK BEFORE ANNOUNCING THAT WE ARE GONE, and the order is the whole point.
            //
            // Ownership is scene memory on both machines, but only the machine that MOVES clears its own:
            // EnemySyncManager.LevelLoaded wipes _ids and _lastSent here, on this machine, a moment from now.
            // The peer keeps everything. So a host that walks into a room, takes the enemies it is closer to
            // and then walks out would never hand them back - its list no longer has them, so it cannot
            // arbitrate them - and the peer, whose self-heal only fires while OwnershipKnown is false, would
            // hold a paused puppet for the rest of the session.
            //
            // This has to come before SendLeaveScene because the server filters ownership by the SENDER's
            // scene: receivePlayerLeaveScene sets that to the empty string, so a token queued afterwards is
            // never delivered at all, and one queued after the next scene loads arrives in the wrong room.
            // Both messages share one queue, so the enqueue order is the delivery order.
            EnemySyncManager.ReleaseOwnedEnemiesBeforeLeaving();

            NetworkManager.SendLeaveScene();
        }

        CurrentlyInLevel = false;
    }

    protected override void OnLateUpdate()
    {
        // TEMPORARY (CutsceneProbe): PROBE #0 - one log line ONLY when the CINEMATIC input blocker
        // changes, read from two PUBLIC values (HasBlocker / InputBlocked). It reuses this per-frame
        // loop on purpose: a probe does not deserve a MonoBehaviour of its own. It sits BEFORE the
        // timer below so its own cost never lands inside the late-update measurement.
        CutsceneProbe.Tick();

        // TEMPORARY (DeathScreenProbe): PASSO 0 - the death screen measurement. One line while the local
        // player is down, plus a line entering and leaving that window. It reuses this per-frame loop for
        // the same reason as the probe above: no new Harmony target (263 stays 263) and no new
        // MonoBehaviour. Log only: it reads the camera, ProCamera2D's target and the death screen's own
        // alpha, and writes nothing. See DeathScreenProbe.cs.
        DeathScreenProbe.Tick();

        // TEMPORARY (DeathScreenTransparency): PASSO 1 - the death screen's background goes transparent
        // while the local player is down, so the corpse can see the boss room, the fight and the boss's
        // cutscene. It zeroes the alpha of the GRAPHIC CHILDREN of NormalGroup - never CanvasGroup.alpha,
        // which DOFade rewrites at DeadScreenWidget.cs:82 - so it does not fight the tween. The prompt
        // lives on ContinueGroup and is not touched. Same loop, still no new Harmony target (263).
        DeathScreenTransparency.Tick();

        // ⭐⭐ BossDeathWarning (19/10): L'AVVISO A SCHERMO AL GIOCATORE MORTO QUANDO IL BOSS MUORE, e la
        // sua finestra di morte. E' la rimessa in funzione di un avviso che ERA NOSTRO - stava nel
        // `DeathRespawnWatchdog` del commit 59f34d7, e il rollback 13107c9 ha cancellato quel file.
        //
        // PERCHE' SERVE, in una riga: la schermata di morte DEL GIOCO legge `UnityEngine.Input`
        // (`DeadScreenWidget.OnDeadAction:87-96`: `while (!Input.anyKey)` -> FADE 1.5 s ->
        // `Penitent.Respawn()`), quindi premere un tasto mentre il boss muore RICARICA il livello e
        // distrugge la cutscene; la mod non puo' impedirlo (bloccare l'input, patchare quella schermata e
        // "consumare" il tasto sono tutti fuori portata, i primi due vietati) ⇒ l'unica difesa e'
        // avvisare. La notifica e' quella DELLA MOD (`NotificationManager`), non un prompt modale, una
        // volta per finestra, ~4 s.
        //
        // QUESTO `Tick` NON DISEGNA NIENTE: tiene solo la finestra di morte e riarma l'avviso quando il
        // giocatore e' di nuovo vivo. Stesso ciclo per frame delle sonde qui sopra, quindi **zero patch
        // Harmony nuove** (263 bersagli, invariati).
        BossDeathWarning.Tick();

        // TEMPORARY (DeathCorpseFollow): LA CAMERA DEL MORTO SEGUE IL COMPAGNO VIVO. Via (c): un
        // cadavere-copia resta sul posto e il Penitent locale (invisibile) si sposta sull'avatar del
        // compagno VIVO, che la mod ha gia' in scena - quindi la camera, legata al Penitent, lo segue da
        // sola e questo file NON tocca la camera. E' anche il punto in cui si congela l'invio della
        // posizione da morti. Same loop, still no new Harmony target (263). See DeathCorpseFollow.cs.
        // ⭐ 19/10: `AttachOnce` sottoscrive `Door.OnDoorEnter`/`OnDoorExit` (eventi PUBBLICI, zero patch)
        // per CONGELARE l'inseguimento durante un cambio stanza: muovere il `Collider2D` del Penitent in un
        // trigger di scena e' la stessa cosa che fa il fusibile, cioe' un load che respawna il giocatore.
        DeathCorpseFollow.AttachOnce();
        DeathCorpseFollow.Tick();

        // TEMPORARY (CorpsePeer): ⭐⭐ LA MORTE VIAGGIA UNA VOLTA, COSI' IL PEER VEDE IL CADAVERE A TERRA.
        // Dal remoto il giocatore morto si vedeva IN PIEDI E BLOCCATO, e la causa non e' la posa: da morti
        // l'invio e' congelato (righe sopra) e quindi al peer non arrivava nemmeno l'animazione di morte -
        // restava sull'ultima posa viva. Qui si manda QUEL byte una volta, prima del congelamento. Va DOPO
        // `DeathCorpseFollow.Tick()` (che e' il punto in cui il congelamento si accende) e puo' stare dopo
        // il watchdog: nessuno dei due spedisce animazioni. Stesso ciclo, quindi **zero patch Harmony nuove**.
        // Vedi `MainPlayerManager.CorpsePeerTick` per il come e `OtherPlayerScript.CurrentAnimation` per
        // l'esclusione della morte dalla prova di vita, che e' l'altra meta' della stessa modifica.
        MainPlayerManager.CorpsePeerTick();

        // TEMPORARY (PerformanceProbe): the whole late-update pass is timed, because it holds the
        // packet processing, the player managers and the enemy sync together - that total is what the
        // frame actually pays. Remove with the probe.
        PerformanceProbe.BeginLate();
        try
        {
            NetworkManager.ProcessQueue();

            MapManager.Update();
            NotificationManager.Update();
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

    // Enemy sync network helpers
    public void SendEnemyUpdate(uint enemyHandle, Vector2 position, float currentLife, int animState, float animTime, int fingerprint, bool ownedBySender, byte orientation) => NetworkManager.SendEnemyUpdate(enemyHandle, position, currentLife, animState, animTime, fingerprint, ownedBySender, orientation);
    // Used for both directions: a peer asks the host to apply a hit, and the host broadcasts one it
    // already applied. The server routes by sender, so a single packet type covers both.
    //
    // The three extra bytes carry the damage type, the element and the boolean flags: without them the
    // receiving machine rebuilds the Hit as "Normal" and the reaction is wrong there - the hurt sound picks
    // the light variant for a heavy blow, and the enemy types that read the damage type choose the wrong
    // response. See NetworkManager.SendEnemyDamageRequest for why this needs no protocol change.
    public void SendEnemyDamageRequest(uint enemyHandle, float damageAmount, byte damageType,
                                       byte damageElement, byte flags)
        => NetworkManager.SendEnemyDamageRequest(enemyHandle, damageAmount, damageType, damageElement, flags);
    public void SendEnemyKill(uint enemyHandle) => NetworkManager.SendEnemyKill(enemyHandle);
    // Ownership token: only the host sends these, and the peer obeys them.
    public void SendEnemyOwnership(uint enemyHandle, bool ownedByReceiver) => NetworkManager.SendEnemyOwnership(enemyHandle, ownedByReceiver);
    // Scene-independent death record: reaches a player who was elsewhere when the kill happened.
    public void SendEnemyDeathGlobal(string enemyId) => NetworkManager.SendEnemyDeathGlobal(enemyId);

    // TEMP: only called by Network manager to store name/team right now
    public void SetPlayerData(string player, byte team)
    {
        PlayerName = player;
        PlayerTeam = team;
    }

    // Refresh players' nametags & map icons when someone changed teams
    public void RefreshPlayerColors()
    {
        OtherPlayerManager.RefreshNametagColors();
        MapManager.QueueMapUpdate();
    }

    // Send more player info after successful connection or display notification
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

        // Send all initial data
        NetworkManager.SendSkin(Core.ColorPaletteManager.GetCurrentColorPaletteId());
        NetworkManager.SendTeam(PlayerTeam);

        // If already in game, send enter scene data & game progress
        if (CurrentlyInLevel)
        {
            MainPlayerManager.SendAllLocationData();
            NetworkManager.SendEnterScene(Core.LevelManager.currentLevel.LevelName);
            OtherPlayerManager.AddNametag(PlayerName, true);
            ProgressManager.SendAllProgress();
        }

        NotificationManager.DisplayNotification(LocalizationHandler.Localize("con"));
    }

    // Whenever you receive a stat upgrade, it needs to check if you are in the same room as the player who sent it.
    // If so, you can no longer obtain stat upgrades in the same room
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

    // If loading the rooftops elevator scene, set levers if they have been unlocked by someone else
    private void FixElevatorLevers()
    {
        Lever[] levers = Object.FindObjectsOfType<Lever>();
        Lever downLever = null, UpLever = null;

        // Find the levers in the scene
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

        // Check where the elevator is & what positions are unlocked, and maybe set levers up
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

    /// <summary>
    /// Save list of interacted persistent objects
    /// </summary>
    public MultiplayerSlotData SaveSlot()
    {
        return new MultiplayerSlotData()
        {
            InteractedPersistenceObjects = ProgressManager.SaveInteractedObjects()
        };
    }

    /// <summary>
    /// Load list of interacted persistent objects
    /// </summary>
    public void LoadSlot(MultiplayerSlotData data)
    {
        ProgressManager.LoadInteractedObjects(data.InteractedPersistenceObjects);
    }

    /// <summary>
    /// Reset list of interacted persistent objects
    /// </summary>
    public void ResetSlot()
    {
        ProgressManager.ClearInteractedObjects();
    }

    /// <summary>
    /// Save last connection info
    /// </summary>
    public MultiplayerGlobalData SaveGlobal()
    {
        return new MultiplayerGlobalData()
        {
            LastConnection = LastConnectionInfo
        };
    }

    /// <summary>
    /// Load last connection info
    /// </summary>
    public void LoadGlobal(MultiplayerGlobalData data)
    {
        LastConnectionInfo = data.LastConnection;
    }
}
