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

        // ⭐ MARKER DELL'IMPORTO DEL DANNO (`[DannoVita]`). Si iscrive ai due delegate STATICI che la
        // `PenitentDamageArea` espone gia' (`OnHitGlobal` prima dei cancelli, `OnDamagedGlobal` dopo
        // l'applicazione): **zero patch Harmony**, i bersagli restano 269. La lacuna che chiude e'
        // `scratch\CONFERMA-RELAY.md` §2.1 — il danno degli effetti ARRIVA, ma **di quanto** non lo
        // diceva nessun marker, e **quale dei due Penitenti** ha perso vita nemmeno. Vedi
        // `PenitentLifeMarker.cs`. Solo log: nessuno stato, nessuna vita, nessuna decisione.
        PenitentLifeMarker.Arm();
    }

    // ⭐ LA COLPA SULLA VITA — una partita NUOVA riparte senza colpa. Il file del contatore e' UNO solo
    // (`Modding\data\Multiplayer\life-guilt.txt`), non uno per slot: senza questa riga una nuova partita
    // erediterebbe le morti della precedente. Il ModdingAPI invoca questo evento quando il menu
    // principale avvia una partita nuova o converte in NG+ (`ModPatches.cs:20-21`), cioe' esattamente i
    // casi in cui il gioco azzera anche la propria colpa. Vedi `LifeGuilt.cs`.
    /// <summary>
    /// Una partita nuova riparte senza la colpa sulla vita (vedi `LifeGuilt.cs`).
    /// </summary>
    protected override void OnNewGame()
    {
        LifeGuilt.OnNewGame();
    }

    // TEMPORARY (CutsceneProbe): the probe disarms itself on shutdown, so the events are never left
    // subscribed on a dying object. Log only.
    protected override void OnDispose()
    {
        CutsceneProbe.Unsubscribe();

        // ⭐ LA SESSIONE-HOST SI CHIUDE CON LA MOD. Il server ascolta su una porta del PROCESSO del
        // gioco: se la mod sparisce (o il gioco si chiude) senza `Stop()`, il socket resta aperto
        // finche' il processo non muore, e in quel tempo la porta 27051 risulta occupata a chiunque
        // la voglia - compreso un `SERVER-COOP` esterno lanciato subito dopo. `HostSession.Stop()`
        // non fa niente (e non stampa niente) se non stavamo ospitando.
        HostSession.Stop();

        // ⭐ Marker dell'importo del danno: si toglie l'iscrizione, cosi' nessun delegate resta appeso a
        // una mod che sta per sparire (stessa ragione della probe qui sopra).
        PenitentLifeMarker.Disarm();

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
        // ⭐ P1 - IL RELAY DEGLI EFFETTI (EffectRelay.cs). Riapre lo stato per la scena nuova e chiude
        // il riepilogo di quella lasciata. Zero bersagli Harmony qui: il relay e' UN Postfix su
        // `PoolManager.ReuseObject`, che vive in quel file.
        EffectRelay.LevelLoaded(newLevel);

        EnemySyncManager.LevelLoaded(newLevel);
        CurrentlyInLevel = newLevel != "MainMenu";
        NotificationManager.LevelLoaded();
        OtherPlayerManager.LevelLoaded(newLevel);
        ProgressManager.LevelLoaded(newLevel);
        CanObtainStatUpgrades = true;

        // ⭐ MARKER [BossJoin] — SI ARMA QUI, SI STAMPA UN FRAME DOPO (`BossJoinMarker`, in OnLateUpdate).
        // "Un frame dopo" non e' pigrizia: questo evento scatta PRIMA che la scena sia utilizzabile
        // (`EnemySpawnPatch.cs:15-17`), quindi leggere qui il predicato di scena o i nemici misurerebbe il
        // frame sbagliato. Vedi `BossJoinMarker` per cosa stampa e perche'.
        _bossJoinScene = newLevel;
        _bossJoinFrame = Time.frameCount;
        _bossJoinArmed = newLevel != "MainMenu";

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
        // ⭐ MARKER [BossJoin]: consuma l'armato di OnLevelLoaded, UNA riga sola, nel primo frame utile.
        // Sta PRIMA di tutto il resto perche' non deve misurare lo stato che questo stesso ciclo modifica,
        // e perche' quando non c'e' niente armato costa un solo confronto booleano. Stesso ciclo per frame
        // delle sonde qui sotto, quindi **zero patch Harmony nuove** (263 bersagli, invariati).
        BossJoinMarker();

        // ⭐ P1 - IL RELAY DEGLI EFFETTI. Nel ciclo per frame fa UNA cosa sola: arma il catalogo
        // nome → prefab appena il boss esiste davvero (nasce dopo la cutscene, quindi non c'e' al
        // caricamento del livello) e lo rinfresca ogni tanto. Non manda, non istanzia, non tocca
        // niente; riusa QUESTO ciclo di proposito, quindi **zero bersagli Harmony nuovi**.
        EffectRelay.Tick();

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

        // ⭐⭐ LA COLPA SULLA VITA SI ACCENDE COL SERVER E SI SPEGNE SENZA (19/10, richiesta dell'utente:
        // "deve attivarsi solo quando ci si connette a un server; in single player il debuff deve essere
        // disattivato e puo' curarsi"). Nel ciclo per frame fa UNA cosa sola: legge il cancello del co-op
        // (`CoopGate.IsMultiplayerActive()` = il socket, host o peer) e **quando cambia** riapplica o
        // spegne, cosi' la vita torna piena senza aspettare una morte o un falo'. Se il fronte non c'e'
        // costa un booleano e un confronto. Stesso ciclo delle sonde qui sopra: **zero patch Harmony
        // nuove, i 272 bersagli restano 272**. Vedi `LifeGuilt.cs` (`Tick`, `SuppressionReason`).
        LifeGuilt.Tick();

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

        // ⭐⭐ BossFightDeathWarning (18/10): L'AVVISO "WAIT FOR YOUR TEAMMATES" - la richiesta dell'utente,
        // cioe' l'avviso che esce **quando il giocatore locale muore in una bossfight** e consiglia di NON
        // respawnare e di aspettare gli altri. E' un avviso DIVERSO da quello qui sopra, in un momento
        // diverso (quello e' la morte del BOSS e la sua cutscene, questo e' la morte del GIOCATORE).
        //
        // Il segnale e' la transizione vivo->morto (`PlayerDeathSync.IsLocalPlayerDead()`), letta in questo
        // stesso ciclo per frame: **una per finestra di morte**. Il predicato "in bossfight" e' quello che
        // la mod ha gia' (`EnemySyncManager.CountLiveBossesInScene()` > 0, costruito su `IsBoss`): senza un
        // boss vivo in scena l'avviso NON esce. Tace anche se la finestra della cutscene di
        // `BossDeathWarning` e' aperta, cosi' i due non si sovrappongono mai.
        // Stesso `NotificationManager` della mod, stesso ciclo: **zero patch Harmony nuove (269)**.
        BossFightDeathWarning.Tick();

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

            // ⭐⭐ B — LA NOTIFICA "L'ALTRO E' IN PAUSA" (solo informazione: NON condivide la pausa).
            // Sta QUI, e non prima, per due ragioni d'ordine:
            //   - **dopo** `NetworkManager.ProcessQueue()` (`:290`): un fronte arrivato dal filo in questo
            //     frame e' gia' stato applicato quando lo si guarda;
            //   - **dopo** `NotificationManager.Update()` (`:293`): la riga persistente viene disegnata
            //     nello stesso frame in cui la si accoda, senza un frame di ritardo.
            // Il polling di `Core.Logic.IsPaused` si fa la' dentro, una volta per frame: le vie di pausa
            // sono SETTE e le riprese DODICI (`scratch\STUDIO-PAUSA-CONDIVISA.md` §1.2) e non c'e' una
            // porta sola da patchare ⇒ **zero patch Harmony nuove** (269 bersagli, invariati), e `Tick()`
            // gira anche a `timeScale = 0` perche' un `LateUpdate` non e' scalato dal tempo di gioco.
            // ⛔ Questa classe NON chiama `PauseGame`/`ResumeGame` e non tocca `Time.timeScale`: riceve e
            // manda un fronte, e accende una riga di testo. Vedi `PauseNotice.cs`.
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

    // ⭐ MARKER [BossJoin] (studio: scratch\STUDIO-PUNTO-0-RIENTRO.md §5, marker 5 — SOLO LOG):
    // ONLEVELLOADED ARMA, IL LATEUPDATE DEL FRAME DOPO STAMPA. Tre campi, nessuno stato di gioco.
    private bool _bossJoinArmed;
    private int _bossJoinFrame;
    private string _bossJoinScene = string.Empty;

    /// <summary>
    /// ⭐ MARKER [BossJoin] — `scena · io · vivo · peerInScena · bossVivi`, UNA riga per caricamento di
    /// livello, un frame dopo `OnLevelLoaded`.
    ///
    /// LA RAGIONE: e' il marker che dice CHI ARRIVA, QUANDO, e se gli altri sono GIA' DENTRO. E' la base
    /// della via 2 dello studio (comunicare lo stato e lasciare che l'ingresso avvenga) e del gate della via
    /// 1: senza sapere se il peer e' in questa scena nel momento esatto del load, "chi entra a meta'" resta
    /// un'ipotesi. Va confrontato con il `[BossView]` dello stesso frame: quello dice com'e' il boss, questo
    /// dice chi c'e' nella stanza.
    ///
    /// COME SI LEGGE: `peerInScena=1/1` = l'altro e' qui ADESSO, quindi la fight e' potenzialmente
    /// condivisa; `0/1` = l'altro e' connesso ma in un'ALTRA stanza (e' il caso "arrivo dopo io");
    /// `0/0` = nessun peer connesso, misure non confrontabili con una run a due.
    /// ⚠️ `peerInScena` e' il predicato `OtherPlayerInMyScene()` ridotto a 0/1 (e' un booleano), mentre il
    /// secondo numero e' il TOTALE dei peer connessi: con 2 macchine i due numeri esauriscono il quadro, con
    /// 3+ `1/2` significa "almeno uno dei due e' qui".
    ///
    /// NON LANCIA MAI: in caso di errore stampa lo stesso marker con `?` (fallback) e prosegue.
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
                        $"io={(string.IsNullOrEmpty(PlayerName) ? "(senza-nome)" : PlayerName)} · " +
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
