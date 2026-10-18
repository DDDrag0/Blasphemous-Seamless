using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Enemies.WaxCrawler;
using Gameplay.GameControllers.Bosses.BossFight;
using Tools.Level.Layout;
using Framework.Managers;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;
using Gameplay.GameControllers.Enemies.Framework.Persistence;
using Framework.FrameworkCore;
using CreativeSpore.SmartColliders;
using Framework.Managers;
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Host-authoritative enemy synchronization.
    ///
    /// IDENTITY
    /// Neither Enemy.SpawningId nor the enemy GameObject name can be used as a network id:
    ///   - EnemySpawnPoint.Awake sets SpawningId = gameObject.GetHashCode(), and CreateEnemy copies it
    ///     onto the enemy. Hash codes are per-process, so they differ between the two machines.
    ///   - EnemySpawnPoint.SetEntityName renames the enemy to EntityName + GetHashCode().
    /// The stable, authored object is the EnemySpawnPoint itself: EnemySpawner.IsSpawnerConsumed(string)
    /// already looks spawn points up by gameObject.name. We therefore derive the network id from
    /// (scene, spawn point name, spawn point position), which is identical on both machines, and resolve
    /// the enemy through EnemySpawnPoint.SpawnedEnemy.
    ///
    /// AUTHORITY
    /// The server elects one host and tells it so via the HostAssign packet. The host is the only machine
    /// that applies damage to enemies; peers block it locally and ask the host, which relays the approved
    /// damage back. See <see cref="IsApplyingRemoteDamage"/> for the re-entrancy guard.
    /// </summary>
    public static class EnemySyncManager
    {
        private const float SEND_INTERVAL = 0.05f;   // ~20 state broadcasts per second
        private const float RESCAN_INTERVAL = 0.5f;  // spawn points created after the level load

        // How often the per-enemy diagnostic snapshot is written.
        //
        // Deliberately separate from RESCAN_INTERVAL, which it used to share by accident: that made the
        // writing as frequent as the scanning, and the scan has to stay fast because it is what notices
        // spawn points created after a level load. This number only trades detail for a readable log.
        // At 0.5 it was six lines a second in a three-enemy room and half the file; at ten it is a couple
        // of dozen lines in a whole session, which is few enough to actually be read.
        private const float DIAGNOSTIC_LOG_INTERVAL = 10f;
        private static float _lastDiagnosticLog;
        private const float FULL_RESEND_INTERVAL = 1f;
        private const float POSITION_EPSILON = 0.01f;
        private const float LIFE_EPSILON = 0.01f;

        private static readonly Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();
        private static readonly Dictionary<string, EnemySpawnPoint> _spawnPoints = new Dictionary<string, EnemySpawnPoint>();
        private static readonly List<string> _ids = new List<string>();

        // Every enemy already given an id. Checks this instead of recomputing an id, so an enemy that
        // moves is never mistaken for a new one.
        private static readonly HashSet<Enemy> _registered = new HashSet<Enemy>();

        /// <summary>Enemies currently tracked in this scene, keyed by network id. Read-only use.</summary>
        public static Dictionary<string, Enemy> Enemies => _enemies;

        /// <summary>
        /// Last state broadcast for an enemy, used to skip updates when nothing changed.
        ///
        /// Everything lives in ONE object per id on purpose. Reading separate parallel dictionaries with
        /// an indexer threw KeyNotFoundException for a freshly registered enemy whose entry did not
        /// exist yet, which aborted Update() before it sent anything at all. With a single record there
        /// is either a complete previous state or none, and no partially-filled case to get wrong.
        /// </summary>
        private class EnemyState
        {
            public Vector2 Position;
            public float Life;
            public int AnimState;
            public float LastSweep;

            // Who is allowed to simulate this enemy. Simulated here means "runs the game's own AI", which
            // is what makes attacks and deaths animate: the animation comes from the enemy's own
            // *AnimatorInyector code, driven by the behaviour tree. Forcing states from outside cannot
            // reproduce that, because the game has a separate inyector per enemy type.
            public bool OwnedByLocal;
            public float OwnerSince;

            /// <summary>
            /// Which way the enemy is facing, as EntityOrientation (Right = 0, Left = 1).
            ///
            /// One bit of state, but the visible consequence is large: the game changes it through
            /// Entity.SetOrientation, which also flips SpriteRenderer.flipX. On a machine where the enemy's
            /// AI is paused nothing calls SetOrientation, so the enemy keeps facing the wrong way and its
            /// walking animation looks mirrored even when the position is correct.
            /// </summary>
            public byte Orientation;

            /// <summary>True once the host has told this machine who owns the enemy.</summary>
            public bool OwnershipKnown;

            /// <summary>Last ownership value the host published, so a change can be detected and sent once.</summary>
            public bool LastPublishedOwnerIsMe;

            /// <summary>
            /// The other machine's claim on this enemy, learned from the ownership flag in its updates.
            ///
            /// Without this the arbitration is impossible: the host can only see its OWN aggro, so it never
            /// learns that the peer is being chased and never lets go. That was the real cause of "whoever
            /// grabs aggro first keeps every enemy forever" - the host kept simulating enemies the peer had
            /// already claimed, and the two machines fought over the position.
            /// </summary>
            public bool OwnedByRemote;
            public bool RemoteEverClaimed;

            /// <summary>
            /// Whether this enemy has already been seen TRULY IN PLAY, so its reveal can be announced once.
            ///
            /// "In play" is read from the damage collider, not from the GameObject being active, and that is
            /// the whole point (voice 53). A whole family of enemies - the WaxCrawler and everything that
            /// reuses it, the mud crawlers among them - is spawned at level load on BOTH machines and left
            /// ACTIVE but hidden: the behaviour does SpriteRenderer.enabled = false and parks the Animator at
            /// speed 0, and the game's own reveal (WaxCrawlerBehaviour.Rise) flips the sprite back on, sets
            /// the speed to 1 and plays "Appear". Nothing ever calls SetActive, so `activeInHierarchy` never
            /// changes and a poll on it would see nothing. The collider is what turns on with the reveal, so
            /// the collider is the signal that means "this enemy is now real".
            /// </summary>
            public bool WasReal;

            /// <summary>
            /// Se la vita di questo BOSS e' gia' stata riallineata nella situazione corrente (questo ingresso
            /// in scena, questo padrone). Vedi <see cref="AlignBossLife"/>.
            ///
            /// Per un nemico normale la vita viaggia a ogni pacchetto e questo campo non conta niente. Per un
            /// boss no: scriverla a ogni ricezione e' proprio cio' che il divieto vieta (una scrittura a 0
            /// lascia il boss in piedi con la barra vuota e la stanza bloccata), quindi la si scrive **una
            /// volta sola** per situazione - all'ingresso in scena e a ogni cambio di padrone - e questo
            /// latch e' cio' che rende "una volta sola" una cosa vera e non una speranza.
            /// </summary>
            public bool BossLifeAligned;

            /// <summary>
            /// La visita della scena in cui la vita del boss e' stata allineata l'ultima volta. Serve solo a
            /// dire la CAUSA nel marker: se `BossLifeVisit != _sceneVisit` l'allineamento nasce da un
            /// INGRESSO in scena, altrimenti da un CAMBIO DI PADRONE.
            /// </summary>
            public int BossLifeVisit;
        }

        /// <summary>
        /// Minimum time an owner keeps an enemy before the role can move again.
        ///
        /// Aggro flips whenever the target enters or leaves sight, so without a hold two machines can
        /// hand the same enemy back and forth many times per second, and with ~100 ms of latency the
        /// enemy visibly jumps between two positions. Holding makes that rare instead of constant.
        /// </summary>
        private const float OWNER_HOLD_SECONDS = 0.5f;

        /// <summary>
        /// How long a closer player waits for an enemy's position before taking it anyway, in seconds.
        ///
        /// The wait exists so a newly-owning machine does not resume the enemy at its stale copy and
        /// broadcast that. But the wait must never STRAND an enemy: if the position does not arrive - the
        /// other machine is not there to send it, or the enemy is stationary and the send is change-gated -
        /// then there is no remote position to protect, and leaving the enemy paused would freeze the room
        /// for the machine that is standing in it. Past this grace the enemy is taken where it stands.
        /// </summary>
        private const float POSITION_WAIT_GRACE = 1.5f;

        /// <summary>
        /// How much closer the other player must be before this machine gives up an enemy, in world units.
        ///
        /// Positions arrive around 20 times a second, so comparing raw distances makes the owner flip
        /// whenever the two players are roughly equidistant - and the enemy would then jump between the two
        /// machines' positions. A dead zone keeps the owner stable while they are side by side.
        /// </summary>
        private const float OWNER_DISTANCE_MARGIN = 2.5f;

        private static readonly Dictionary<string, EnemyState> _lastSent = new Dictionary<string, EnemyState>();

        /// <summary>
        /// The id assigned to each enemy INSTANCE, decided once and never recomputed.
        ///
        /// This is the fix for the identity divergence that showed up in game as a teleport towards the
        /// wrong enemy plus a duplicated enemy. The free-enemy id embeds the enemy's current position
        /// (rounded to whole units), so recomputing it later gives a DIFFERENT string as soon as the enemy
        /// has moved half a unit. The two machines register at different moments, so they produced different
        /// ids for the same enemy, each tracked an enemy the other did not have, and updates landed on the
        /// wrong target.
        ///
        /// Keyed by instance and not by name: several enemies can share a name (Fool(Clone)), and the model
        /// is the only thing that truly identifies one. It also stops the same enemy being registered twice
        /// under two different ids, which is what produced the duplicate.
        /// </summary>
        private static readonly Dictionary<Enemy, string> _enemyIds = new Dictionary<Enemy, string>();

        /// <summary>
        /// When each enemy was first seen at zero health with its death sequence not yet started, keyed by id.
        ///
        /// This is the safety net for the enemies whose death we cannot start from here. Killing a synced
        /// enemy goes through the game's damage entry point, and for most types that sets their DEATH trigger
        /// - but the wiring differs per enemy (some set it in &lt;Type&gt;::Damage, others in a behaviour, some
        /// through an OnDeath handler), so a type we have not met yet can end up at zero health with nothing
        /// playing. Left alone it would stand there for ever: Entity.Dead already true, so the death paths
        /// would all decline to act, and EnemyDamageArea switches its own hitbox off only once
        /// EntityStatus.Dead is set.
        ///
        /// After GRACE the mod forces the end itself, so a bad case costs one enemy a second and a half
        /// instead of leaving an unkillable one in the room.
        /// </summary>
        private static readonly Dictionary<string, float> _deathPendingSince = new Dictionary<string, float>();

        /// <summary>How long an enemy may sit at zero health with no death sequence before the mod forces it.</summary>
        private const float DEATH_GRACE_SECONDS = 1.5f;

        /// <summary>
        /// The enemies this machine is NOT simulating, as a set of instances.
        ///
        /// Needed by the orientation probe: the Harmony prefix on Entity.SetOrientation runs on every
        /// orientation write in the game, many times per frame, so it cannot afford to work out ownership
        /// from the id table each time. This keeps the answer one hash-set lookup away.
        ///
        /// It is maintained exactly where ownership changes - PauseEnemy and ResumeEnemy - rather than
        /// recomputed, so it cannot drift from what those two do.
        /// </summary>
        private static readonly HashSet<Enemy> _puppetEnemies = new HashSet<Enemy>();

        /// <summary>True when this machine is not the one simulating the enemy. Used by the orientation probe.</summary>
        public static bool IsPuppet(Enemy enemy)
        {
            return enemy != null && _puppetEnemies.Contains(enemy);
        }

        /// <summary>
        /// A short label naming an enemy by POSITION, for the orientation probe.
        ///
        /// The SPAWN (NN) labels permute between runs, so they cannot be compared across sessions; the
        /// position is the only stable handle. Building the string costs an allocation, and the probe calls
        /// this on every orientation write - many times per frame - so the result is cached per instance and
        /// only built once.
        /// </summary>
        public static string LabelOf(Enemy enemy)
        {
            if (enemy == null)
                return null;

            int key = enemy.GetInstanceID();

            string cached;
            if (_orientationLabels.TryGetValue(key, out cached))
                return cached;

            Vector3 p = enemy.transform.position;
            cached = $"({p.x:F1},{p.y:F1})";
            _orientationLabels[key] = cached;

            return cached;
        }

        /// <summary>Position labels, keyed by instance id. Cleared with the scene the instances belong to.</summary>
        private static readonly Dictionary<int, string> _orientationLabels = new Dictionary<int, string>();

        /// <summary>
        /// Enemy ids that belong to a BOSS, so their health is deliberately left alone.
        ///
        /// Bosses derive from Enemy like anything else, so they are tracked, positioned and animated by the
        /// same code - but their death is not a death animation. It is a scripted sequence
        /// (Crisanta_StDeath, PontiffGiantBehaviour.DeathSequence, and similar state machines per boss),
        /// and the only thing that starts it is &lt;Boss&gt;::Damage, which the sword calls. Some bosses even run
        /// that sequence from a NodeCanvas Death_EnemyAction, so it depends on their behaviour tree being
        /// alive.
        ///
        /// Writing a boss's health to zero therefore does not kill it: it leaves the boss standing with an
        /// empty health bar and the room unable to advance. Until each boss has been tested, its health
        /// stays local and only position, animation and facing are shared.
        /// </summary>
        private static readonly Dictionary<string, bool> _bossIds = new Dictionary<string, bool>();

        /// <summary>
        /// True when the enemy is a boss, judged by its namespace rather than by a list of names.
        ///
        /// Every boss lives under Gameplay.GameControllers.Bosses (24 types verified in the game assembly),
        /// while ordinary enemies live under Gameplay.GameControllers.Enemies, so the namespace separates
        /// them without having to keep a list in step with the game.
        /// </summary>
        private static bool IsBoss(Enemy enemy, string id)
        {
            bool cached;
            if (_bossIds.TryGetValue(id, out cached))
                return cached;

            bool isBoss = false;
            System.Type type = enemy.GetType();
            string space = type.Namespace;
            if (!string.IsNullOrEmpty(space))
                isBoss = space.StartsWith("Gameplay.GameControllers.Bosses", System.StringComparison.Ordinal);

            _bossIds[id] = isBoss;
            return isBoss;
        }

        // ============================================================================================
        // M1 — LA MORTE DEL BOSS, PORTATA SULLA MACCHINA DI CHI E' A TERRA
        // (referto: scratch\STUDIO-BOSSONE-MORTE-CAM.md §5.2; che spiega anche cosa NON si tocca)
        // ============================================================================================

        /// <summary>
        /// L'OTTAVO PREFISSO del canale di morte: `bdead|` = "sulla macchina di chi ha ucciso e' morto un
        /// BOSS". (Gli altri: `sp|` nemici, `brk|` lampade, `falo|` falo', `pdead|`/`palive|` giocatori,
        /// `nato|` nascite, `exec|` esecuzioni.)
        ///
        /// PERCHE' UN PREFISSO E NON BASTA L'ID GIA' SPEDITO. Un boss viene GIA' annunciato come qualunque
        /// altro nemico: il ciclo di invio vede `enemy.Dead` e manda `SendEnemyKill(handle)` +
        /// `SendEnemyDeathGlobal(id)` (misurato: R1-G1:2708 `[EnemyKill] …ElderBrother…MORTO`, R3-G2 idem).
        /// Quello che NON viaggia e' "questo e' un BOSS": sul ricevente l'id entra nel ramo generico, dove
        /// la morte si applica chiamando `&lt;Boss&gt;::Damage(Hit)`, e quella comincia con
        /// `if (Status.Dead || Core.Logic.Penitent.Stats.Life.Current &lt;= 0f) return;`
        /// (`ElderBrother.cs:76`, e la stessa riga in 16 classi di boss). ⇒ con il giocatore LOCALE a terra
        /// la morte viene SCARTATA IN SILENZIO: il boss resta in piedi a vita piena, la sua FSM di morte
        /// non parte, `BossFightManager.OnDeathBoss` non gira e la cutscene del boss non parte.
        /// Misurato in R3-G1: ~24 s di `vita=330` e il colpo letale inoltrato (`amount=331`) scartato, con
        /// il flag `D17Z01_BOSSDEAD` gia' applicato.
        ///
        /// ⇒ Il prefisso dice al ricevente UNA cosa sola: "questa morte va applicata con `Kill()`", che
        /// dalla guardia NON passa (vedi `ApplyBossDeath`). Non cambia nient'altro: l'annuncio generico
        /// resta al suo posto, quindi il resto del sistema vede esattamente cio' che vedeva prima.
        /// </summary>
        private const string BossDeathPrefix = "bdead|";

        /// <summary>True quando questo id e' l'annuncio della morte di un BOSS.</summary>
        private static bool IsBossDeathId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(BossDeathPrefix, System.StringComparison.Ordinal);
        }

        /// <summary>L'id del boss dentro l'annuncio, o null per qualunque cosa malformata.</summary>
        private static string BossDeathIdOf(string id)
        {
            if (!IsBossDeathId(id))
                return null;

            string enemyId = id.Substring(BossDeathPrefix.Length);
            return string.IsNullOrEmpty(enemyId) ? null : enemyId;
        }

        /// <summary>
        /// Boss ids la cui morte QUESTA macchina ha gia' applicato da un annuncio. Una volta per id, e
        /// azzerata con il resto della memoria di scena: gli id non sopravvivono a un cambio livello.
        /// </summary>
        private static readonly HashSet<string> _bossKillsApplied = new HashSet<string>();

        /// <summary>
        /// Boss ids la cui SEQUENZA DI MORTE (`Death()`) questa macchina ha gia' recitato. Secondo
        /// lucchetto, indipendente dal primo: `_bossKillsApplied` dice "il `Kill()` e' passato", questo dice
        /// "la morte e' stata recitata". Stessa vita del primo (azzerato in `LevelLoaded` e in
        /// `OnDeathStateReset`), cosi' un boss della scena dopo e' di nuovo recitabile.
        /// </summary>
        private static readonly HashSet<string> _bossDeathsApplied = new HashSet<string>();

        /// <summary>
        /// ⭐⭐ A) LA WHITELIST DELLA MORTE RECITATA: gli UNICI tipi di `EnemyBehaviour` su cui questa mod
        /// puo' invocare `Death()` dopo il `Kill()`.
        ///
        /// IL CRITERIO (mandato): `Death()` deve fare qualcosa SOLO dentro il boss. Vietato tutto cio' che
        /// tocca il giocatore LOCALE (`Core.Logic.Penitent`, `DrivePlayer`), l'input (`Core.Input.SetBlocker`),
        /// la camera (`CameraManager`) o il tempo. Quei boss restano AFK di proposito: meglio un AFK noto che
        /// un effetto collaterale sul giocatore.
        ///
        /// VERIFICATI TUTTI E 17 I TIPI con un `Death()` pubblico in
        /// `Gameplay.GameControllers.Bosses` (elenco completo, con la ragione di ogni esclusione, in
        /// scratch\REFERTO-A-C-MORTE-BOSS.md). DENTRO (5): la loro `Death()` ferma solo roba propria
        /// (coroutine, eventi, tree) e lancia il trigger `DEATH` sull'animator, che nella scena del gioco e'
        /// quello che fa sparire il cadavere (`&lt;Boss&gt;DeathBehaviour.OnStateExit` -> `Destroy`).
        /// FUORI (12), perche' la loro `Death()` (o cio' che chiama) fa almeno una delle cose vietate:
        ///   Amanecidas      `Penitent.Status.Invulnerable = true`
        ///   BurntFace       `Penitent.Status.Invulnerable = true` + `ProCamera2DShake`
        ///   Crisanta        `Penitent.DrivePlayer.MoveToPosition` (ramo redux / non redux)
        ///   Isidora         `Penitent.Status.Invulnerable` + `MasterShaderEffects` sul Penitent
        ///   PontiffGiant    `Penitent.Status.Invulnerable = true`
        ///   PontiffHusk     `Penitent.Status.Invulnerable` + `SetBlocker("END_BOSS_DEFEATED"/"HW_...")` +
        ///                   scroll/camera di fine scontro
        ///   PontiffOldman   `Penitent.Status.Invulnerable = true`
        ///   PontiffSword    `Invoke("ActivatePlatform")`: tocca la GEOMETRIA della stanza
        ///   Snake           `Penitent.Status.Invulnerable` + `SetBlocker("TPOReposition")` (Death_EnemyAction)
        ///   TresAngustias   `Penitent.Status.Invulnerable` + `ProCamera2DShake` + `SetBlocker("CINEMATIC")`
        ///   WickerWurm      `Penitent.Status.Invulnerable` + `Unattacable` (via `StartDeathSequence`)
        ///   SingleAnguish   animator + FSM: nessuna cosa vietata, ma non e' un boss di scena: la sua morte
        ///                   la guida `TresAngustiasMasterBehaviour` (fuori whitelist) e il suo `Kill()`
        ///                   arriva da li'; invocarla da sola lascerebbe il trio in uno stato intermedio.
        /// </summary>
        private static readonly HashSet<string> BossDeathSequenceWhitelist =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ElderBrotherBehaviour",   // StopAllCoroutines + StopMovement + StopBehaviour + SetTrigger("DEATH")
                "LesmesBehaviour",         // idem: coroutine + eventi + StopBehaviour + SetTrigger("DEATH")
                "QuirceBehaviour",         // idem + azzera il danno del proiettile in volo (roba sua)
                "PerpetuaBehaviour",       // coroutine + summon della sua arena + SetTrigger("DEATH")
                "EsdrasBehaviour"          // + `DismissPerpetua()` -> `PerpetuaBehaviour.Death()` (stessa famiglia)
            };

        /// <summary>
        /// Applica su QUESTA macchina la morte di un boss annunciata dall'altra (vedi `BossDeathPrefix`).
        ///
        /// LA STRADA E' `Kill()`, ED E' NATIVA:
        ///     Entity.Kill()                       (Entity.cs:312, public virtual, nessun override fra i boss)
        ///       -> Damage(Life.Current + Defense.Final, "")   (Entity.cs:270, NON la Damage(Hit))
        ///         -> Stats.Life.Current &lt;= 0
        ///           -> KillInstanteneously()      (Entity.cs:317, public)
        ///             -> Status.Dead = true, vita = 0
        ///             -> Entity.Death / this.OnDeath / this.OnEntityDeath / ENTITY_DEAD
        ///               -> BossFightManager.OnDeathBoss (BossFightManager.cs:181-189, sottoscritto a
        ///                  `Boss.OnDeath` in Init :67)
        ///                 -> Metrics.EndBossFight() + onFinishBossFight.Invoke()
        /// ⭐ La guardia del morto NON e' sulla strada: e' su `Damage(Hit)`, l'overload del COLPO, mentre
        /// `Kill()` usa `Damage(float, string)`. E' l'unica ragione per cui questo fix e' cosi' piccolo.
        ///
        /// COSA QUESTO METODO NON FA (i divieti del mandato, riga per riga):
        ///   - NON chiama `StartCutscene` (la cutscene e' nativa: deve partire da sola dalla catena sopra);
        ///   - NON tocca `SetFreeCamera` (e quindi non accende MAI il blocker `FREECAMERA`);
        ///   - NON tocca `Core.Input.SetBlocker` di nessuno e NON chiama `ResetManager()` (un blocker
        ///     acceso e mai spento = tastiera bloccata fino al cambio livello);
        ///   - NON tocca `Time.timeScale` (e `DeadScreenWidget` usa `WaitForSecondsRealtime` apposta);
        ///   - NON tocca il `Penitent` dell'altro giocatore, ne' questo, ne' la camera di nessuno;
        ///   - NON tocca `FlagStates`, `FlagHelper`, i suoni della parata, l'esecuzione, il fantasma,
        ///     l'orfano, ne' l'annuncio generico della morte: aggiunge un annuncio, non ne cambia uno.
        ///
        /// COSA NON RISOLVE, dichiarato: se il giocatore locale NON e' a terra non fa NIENTE, di proposito
        /// (su una macchina viva la morte del boss arriva gia' per la strada di sempre e la cutscene parte:
        /// MISURATO in R1-G2, che non aveva ucciso lei il boss e ha avuto `cutscene_block` 2 e `CTS02`
        /// completa). E il caso "entrambi a terra" resta senza risposta: se nessuno sopravvive, nessuno
        /// annuncia niente e questo metodo non viene mai chiamato - e' il buco noto del piano.
        /// </summary>
        private static void ApplyBossDeath(string id)
        {
            try
            {
                Enemy boss = ResolveEnemyForBossDeath(id, out string via);
                bool bossHere = boss != null && boss.gameObject != null;
                bool localDead = PlayerDeathSync.IsLocalPlayerDead();

                if (!bossHere)
                {
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NON applicato (ero morto: " +
                                $"{(localDead ? "si" : "no")}) motivo=boss-non-in-scena via={via}");
                    return;
                }

                // ⚠️ LA GUARDIA CHE RENDE QUESTO FIX PICCOLO: si interviene SOLO sul caso rotto.
                if (!localDead)
                {
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NON applicato (ero morto: no) " +
                                $"motivo=giocatore-locale-vivo via={via}");
                    return;
                }

                // `Status.Dead` e' "la sequenza di morte e' partita", non "la vita e' zero": e' la stessa
                // domanda che fa KillEnemyLocally, e per la stessa ragione.
                if (boss.Status.Dead)
                {
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NON applicato (ero morto: si) " +
                                $"motivo=boss-gia-morto via={via}");
                    return;
                }

                if (!_bossKillsApplied.Add(id))
                {
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NON applicato (ero morto: si) " +
                                $"motivo=gia-applicata via={via}");
                    return;
                }

                bool isBoss = IsBoss(boss, id);
                string type = boss.GetType().Name;
                float lifeBefore = boss.CurrentLife;

                // ⭐ LO STATO DELLA TREE NELL'ISTANTE DEL KILL: misurato, e NON toccato.
                //
                // E' la meta' di misura di questo fix, e la ragione per cui non c'e' una riga che la
                // riaccende (vedi il blocco qui sotto): se il boss resta AFK, il log deve dire da solo se
                // la tree era spenta quando `Kill()` e' passato, invece di far dedurre la causa.
                // `off` = spenta (PauseEnemy), `on/isRunning=False` = componente acceso ma grafo fermo,
                // `assente` = nessun EnemyBehaviour (allora nessuna tree da svegliare e nessuna morte da
                // recitare: il boss non ha una sequenza di morte in albero).
                EnemyBehaviour tree = FindBehaviour(boss);
                string treePrima = tree == null
                    ? "assente"
                    : ((tree.enabled ? "on" : "off") + "/isRunning=" + ReadTreeRunning(tree));

                // ⛔ PERCHE' QUI NON SI RIACCENDE LA TREE PRIMA DEL `Kill()` — e non e' paura, e' misurato
                // sul codice del gioco.
                //
                // La via per spegnerla e' `PauseEnemy` (`:1304-1348`): `behaviour.PauseBehaviour()` +
                // `behaviour.enabled = false`, cioe' `EnemyBehaviour.PauseBehaviour` (IL decompilato:
                // `if (BehaviourTree != null && BehaviourTree.isRunning) BehaviourTree.PauseBehaviour();`).
                // Il rovescio esatto sarebbe `behaviour.enabled = true; behaviour.StartBehaviour();`
                // (idempotente allo stesso modo: `if (BehaviourTree != null && !BehaviourTree.isRunning)
                // BehaviourTree.StartBehaviour();`). ⇒ la riga e' innocua in se'.
                //
                // ⚠️ MA NON RESTITUISCE LA MORTE AL BOSS, E ARMA L'AI SU UN CADAVERE. Per OGNI boss la
                // sequenza di morte NON e' pilotata dall'albero: e' `Boss.Damage(Hit)` a chiamare
                // `Behaviour.Death()` dopo `DamageArea.TakeDamage` (`ElderBrother.cs:74-101` ⇒
                // `ElderBrotherBehaviour.cs:319-327`: `StopAllCoroutines` + `StopBehaviour` +
                // `AnimatorInyector.Death()`, cioe' `Animator.SetTrigger("DEATH")`; la stessa forma in 16
                // classi di boss: Crisanta.cs:109-115, Isidora.cs:101-107, Snake.cs:190, ...).
                // `Kill()` passa da `Entity.Damage(float,string)` -> `KillInstanteneously` e da li'
                // `Behaviour.Death()` NON passa: nessun trigger DEATH, nessuna animazione, e il collider di
                // danno resta acceso (lo spegne solo `Damage(Hit)`). E `Behaviour.Death()` stesso chiama
                // `BehaviourTree.StopBehaviour()`: quando il boss muore, il GIOCO la tree la FERMA.
                // ⇒ riaccenderla qui (a) non fa partire la morte e (b) lascia un albero in corsa su un boss
                // con `Status.Dead = true`, che `ElderBrotherBehaviour` non controlla da nessuna parte
                // (`LaunchRandomAction`/`CanExecuteNewAction` non guardano la morte): il boss
                // combatterebbe da morto. Meglio l'AFK noto che un boss che combatte da cadavere.
                // ⇒ la strada giusta e' far eseguire alla macchina del morto la sequenza di morte DEL BOSS
                // (la sua `Death()`), non risvegliarne l'albero: referto scratch\REFERTO-FIX-AFK.md §3.
                // ⚠️ E NON PER TUTTI I BOSS: per lo Snake `Death()` tocca il `Penitent` LOCALE e accende
                // `Core.Input.SetBlocker("TPOReposition")` (SnakeBehaviour.cs:170-219), cioe' proprio le cose
                // vietate - e lo stesso vale per altri 11 tipi su 17 (tabella completa e ragioni:
                // scratch\REFERTO-A-C-MORTE-BOSS.md). Per questo la `Death()` si invoca da una WHITELIST
                // verificata tipo per tipo (`BossDeathSequenceWhitelist`, sotto), e per i tipi fuori
                // whitelist non si invoca niente: li' resta l'AFK noto, e ci pensa il fix C.
                //
                // ⚠️ IL PUNTO DEL FIX, UNA RIGA. E' anche l'unica scrittura sul gioco di tutto M1, e cade
                // su un boss che SULL'ALTRA macchina e' gia' morto: non si inventa niente.
                boss.Kill();

                // ⭐⭐ A) LA MORTE RECITATA — la sequenza DEL GIOCO, dopo il `Kill()`.
                //
                // `Kill()` porta `Status.Dead` a vero e fa partire la cutscene (`OnDeath` ->
                // `BossFightManager.OnDeathBoss`), ma NON passa da `<Boss>Behaviour.Death()`: quella la
                // chiama solo `<Boss>.Damage(Hit)`, cioe' il colpo letale, che qui non puo' arrivare (la
                // guardia `Penitent.Stats.Life.Current <= 0` lo scarta). ⇒ senza questa chiamata la
                // macchina del morto ha un boss con `Status.Dead=true` e NESSUNA animazione di morte: lo
                // sprite resta acceso, immobile, e la tree ferma. E' l'AFK da correggere.
                //
                // L'ORDINE E' QUELLO NATIVO, non una scelta: il gioco rende `Status.Dead` prima
                // (`DamageArea.TakeDamage`) e recita la morte dopo (`Behaviour.Death()`), che a sua volta
                // ferma la tree (`StopBehaviour`, idempotente qui perche' e' gia' ferma) e lancia il
                // trigger nativo (`AnimatorInyector.Death()` = `SetTrigger("DEATH")`).
                // ⚠️ NON si chiama se `Kill()` non ha ucciso: recitare la morte di un vivo che poi sparisce
                // sarebbe peggio dell'AFK.
                bool died = boss.Status.Dead;
                float lifeAfter = boss.CurrentLife;
                string deathEsito = died
                    ? ApplyBossDeathSequence(boss, id)
                    : "non-invocata:kill-fallito";

                // L'esito va scritto SEMPRE, altrimenti "l'annuncio non e' arrivato" e "e' arrivato e non
                // ha fatto nulla" restano indistinguibili nel log - ed e' esattamente la differenza che
                // questo test deve misurare.
                ModLog.Info($"[BossDeath RX] {FullId(id)} applicato (ero morto: si) via={via} " +
                            $"boss={isBoss} tipo={type} vitaPrima={lifeBefore:F0} " +
                            $"vitaDopo={lifeAfter:F0} StatusDead={died} " +
                            $"Kill={(died ? "ok" : "nessun-effetto")} morte={deathEsito} " +
                            $"treePrima={treePrima} treeToccata=no (vedi il commento sopra)");

                if (!died)
                {
                    // Non deve succedere: `Kill()` e' la strada con cui il gioco uccide qualunque entita'.
                    // Se succede, e' la prova che su QUESTO boss l'uccisione passa altrove, e la seconda
                    // strada del piano (riaprire la guardia e mandare il colpo letale) diventa necessaria.
                    ModLog.Error($"[BossDeath RX] {FullId(id)} Kill() NON ha ucciso il boss: " +
                                 $"StatusDead=false vita={boss.CurrentLife:F0}");
                }

                // ⭐⭐ L'AVVISO A SCHERMO AL GIOCATORE MORTO — ed e' QUI che e' riagganciato (19/10).
                //
                // Il punto di aggancio ORIGINALE non esiste piu': nel commit 59f34d7 l'avviso stava nel
                // `DeathRespawnWatchdog` e partiva dal suo segnale `kill()`; il rollback 13107c9 ha
                // CANCELLATO quel file, e con lui `EnemySyncManager.BossKillSignal`. Questo invece e' il
                // punto VIVO in cui la mod fa morire il boss su questa macchina, ed e' esattamente "il
                // momento in cui il boss muore": da qui parte la sua sequenza di morte e la cutscene e'
                // alle porte.
                //
                // E il giocatore locale e' morto PER COSTRUZIONE (la guardia poco sopra esce con
                // `motivo=giocatore-locale-vivo` prima del `Kill()`): quindi la schermata di morte DEL
                // GIOCO, quella che respawna al primo tasto, c'e' davvero - e la mod non puo' impedirglielo.
                // Vedi `BossDeathWarning`: una notifica, una volta per finestra, 4 s, e nient'altro.
                if (died)
                    BossDeathWarning.WarnBossDied("rx:ApplyBossDeath (Kill() applicato)");
            }
            catch (Exception e)
            {
                // Un'eccezione qui uscirebbe da NetworkManager.ProcessQueue, che gira dentro try/FINALLY
                // senza catch: la coda non verrebbe svuotata e lo stesso pacchetto verrebbe riprocessato
                // per sempre. E' il guasto da 2934 errori, e vale per QUALUNQUE cosa giri su questo canale.
                ModLog.Error($"[BossDeath RX] {FullId(id)} eccezione in Kill(): {e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>
        /// A) Fa recitare al boss la SUA morte, come fa il gioco: `EnemyBehaviour.Death()` -> `StopBehaviour`
        /// + `AnimatorInyector.Death()` (`SetTrigger("DEATH")`). Il tipo concreto cambia boss per boss, quindi
        /// si passa dalla RIFLESSIONE: `GetComponents&lt;EnemyBehaviour&gt;()` +
        /// `GetMethod("Death", Type.EmptyTypes)` + `Invoke`.
        ///
        /// PERCHE' LA RIFLESSIONE E NON UN CAST. `EnemyBehaviour` e' la base comune
        /// (`Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour`) ma `Death()` NON e' dichiarata li':
        /// e' un metodo pubblico di ogni sottoclasse, con lo stesso nome e nessun parametro (verificato in 17
        /// classi di boss). Non esiste un'interfaccia comune da usare, e un `switch` sui 17 tipi sarebbe un
        /// cast per riga, piu' fragile della reflection gia' usata altrove in questo file (`ReadTreeRunning`).
        ///
        /// ⚠️ SOLO WHITELIST (`BossDeathSequenceWhitelist`) E UNA VOLTA PER ID (`_bossDeathsApplied`). Per un
        /// tipo fuori whitelist NON si invoca niente e si scrive il motivo nel log: la' resta l'AFK, che il
        /// fix C rende invisibile al rientro.
        ///
        /// RITORNA una stringa per il log (`morte=`), perche' "invocata" e "saltata perche' vietata" devono
        /// restare distinguibili senza dedurle.
        /// </summary>
        private static string ApplyBossDeathSequence(Enemy boss, string id)
        {
            try
            {
                if (!_bossDeathsApplied.Add(id))
                    return "saltata:gia-recitata";

                EnemyBehaviour[] behaviours = boss.GetComponents<EnemyBehaviour>();

                // Ripiego per i boss il cui `EnemyBehaviour` vive su un figlio: e' il terzo gradino di
                // `FindBehaviour`, che il resto del file usa gia' per la stessa ragione.
                if (behaviours == null || behaviours.Length == 0)
                {
                    EnemyBehaviour found = FindBehaviour(boss);
                    behaviours = found == null ? null : new[] { found };
                }

                if (behaviours == null || behaviours.Length == 0)
                    return "nessuna-EnemyBehaviour";

                string primo = null;

                for (int i = 0; i < behaviours.Length; i++)
                {
                    EnemyBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                        continue;

                    string typeName = behaviour.GetType().Name;
                    if (string.IsNullOrEmpty(primo))
                        primo = typeName;

                    if (!BossDeathSequenceWhitelist.Contains(typeName))
                        continue;

                    MethodInfo death = behaviour.GetType().GetMethod("Death", Type.EmptyTypes);
                    if (death == null)
                        return "escluso:" + typeName + "-senza-Death";

                    death.Invoke(behaviour, null);
                    return "recitata:" + typeName;
                }

                return "escluso:" + (primo ?? "?") + "-non-in-whitelist";
            }
            catch (Exception e)
            {
                // Qualunque cosa esca da una `Death()` di un boss non deve uscire da qui: questo codice gira
                // dentro `NetworkManager.ProcessQueue` (vedi il catch di `ApplyBossDeath`).
                return "eccezione:" + e.GetType().Name;
            }
        }

        // ============================================================================================
        // C) IL CADAVERE DEL BOSS NON RESTA VISIBILE AL RIENTRO
        // ============================================================================================

        /// <summary>
        /// I boss che questo fix ha SPENTO all'ingresso in scena. Serve a due cose, entrambe necessarie:
        ///   - idempotenza: la seconda passata di spawn dello stesso caricamento non li ri-spegne e non
        ///     riscrive il marker;
        ///   - `RegisterEnemy` li SALTA: un boss che abbiamo riconosciuto come cadavere non deve entrare
        ///     nella tabella di sincronizzazione (altrimenti la mod gli riaccenderebbe la tree e si
        ///     ritroverebbe un boss invisibile che combatte).
        /// Azzerato in `LevelLoaded` (gli oggetti non sopravvivono al cambio scena).
        /// </summary>
        private static readonly HashSet<Enemy> _hiddenBossCorpses = new HashSet<Enemy>();

        /// <summary>
        /// C) ALL'INGRESSO IN UNA BOSSROOM: se il boss di questa stanza e' GIA' STATO UCCISO, spegnerlo.
        ///
        /// IL SINTOMO CHE CHIUDE. Dopo la morte del boss, la stanza lo rigenera quando ci si rientra, e la
        /// scena lo lascia li': il "cadavere" resta VISIBILE in mezzo all'arena, su entrambe le macchine,
        /// perche' la mod non lo simula piu' (esce da `_enemies`) e la FSM della stanza, che decide della
        /// bossroom, vive dentro l'asset di scena e non e' verificabile dal repo.
        ///
        /// IL PREDICATO (il mandato: "il boss e' morto E/O il flag `&lt;scena&gt;_BOSSDEAD` e' acceso"):
        ///   morto = `Status.Dead` (la stessa nozione usata ovunque in questo file: "la sequenza di morte e'
        ///           partita", non "la vita e' zero");
        ///   flag  = `Core.Events.GetFlag(&lt;candidato&gt;)` per i due nomi che il gioco usa davvero per una
        ///           stanza: `&lt;scena&gt;_BOSSDEAD` (es. `D01Z06S01_BOSSDEAD`) e `&lt;zona&gt;_BOSSDEAD` (es.
        ///           `D17Z01_BOSSDEAD` per la stanza `D17Z01S11`). Sono le forme presenti in
        ///           `Data\FlagStates.cs`, righe 37-48: la tabella del mod e' la prova che esistono entrambe,
        ///           e la zona e' il prefisso `D##[lettera]Z##` del nome della stanza.
        ///   ⇒ si spegne se UNA delle due e' vera.
        ///
        /// ⚠️ E NON DEVE MAI SPEGNERE UN BOSS VIVO *QUANDO IL FLAG E' SPENTO*: e' la guardia
        /// `BossLooksAlive`, e chi la passa NON viene toccato (e lo si scrive nel log). Un boss che sta
        /// combattendo, infatti, ha sempre il flag spento: il flag e' la conseguenza della morte.
        ///
        /// ⭐ FIX ② — E IL VETO NON VALE QUANDO IL FLAG E' ACCESO. La guardia contraddiceva il predicato
        /// del metodo, e il caso e' misurato: `G1:6719`
        /// `[BossCorpse] … NON nascosto (ingresso in scena): il boss sembra VIVO (statusDead=False
        /// flag=D17Z01_BOSSDEAD vita=400) e un boss vivo non si tocca` — cioe' il flag della stanza ACCESO,
        /// la vita PIENA, e il boss lasciato in piedi in mezzo all'arena. Il flag e' progresso del GIOCO, non
        /// un'opinione della mod: se e' acceso, quell'oggetto non e' la fight, e' un residuo che la stanza ha
        /// rimesso per la prima passata di spawn (la FSM legge il flag, ma non sempre prima di questo punto).
        /// Quindi: con il flag acceso si spegne comunque; con il flag spento la guardia resta intatta e un
        /// boss vivo non viene sfiorato. Nessuna delle due strade tocca la morte, la vita o il progresso.
        ///
        /// ⛔ MAI `Destroy`: la lezione di `DestroyDeadEnemy` (`:5022-5026`) - l'oggetto della scena non si
        /// distrugge, si spegne. `SetActive(false)` su un oggetto gia' spento e' idempotente.
        ///
        /// DOVE SI CHIAMA: in `OnEnemiesSpawned`, cioe' all'ingresso (una volta per caricamento, non per
        /// frame), su ENTRAMBE le macchine perche' quel metodo gira su entrambe.
        /// </summary>
        private static void HideDefeatedBosses(string scene, string where)
        {
            try
            {
                Enemy[] enemies = FindAllEnemiesIncludingInactive();
                if (enemies == null)
                    return;

                for (int i = 0; i < enemies.Length; i++)
                {
                    Enemy enemy = enemies[i];
                    if (enemy == null || enemy.gameObject == null)
                        continue;

                    // Lo stesso criterio di `IsBoss` (namespace), ma senza id e senza cache: qui non c'e'
                    // ancora un id, e questo test non deve lasciare traccia in nessuna tabella.
                    if (!IsBossByNamespace(enemy))
                        continue;

                    if (_hiddenBossCorpses.Contains(enemy))
                        continue;

                    bool statusDead = enemy.Status != null && enemy.Status.Dead;
                    string flag = FirstBossDeadFlagOn(scene);

                    string enemyId = BuildFreeEnemyId(scene, enemy);
                    string tipo = enemy.GetType().Name;

                    // ⭐ FIX ② — LA GUARDIA, RIDOTTA AL SUO VERO PERIMETRO.
                    //
                    // Il veto vale quando il flag e' SPENTO, ed e' li' che serve: senza flag l'unica prova
                    // che quel boss sia un residuo e' `Status.Dead`, e un boss vivo non si tocca.
                    // Con il flag ACCESO il veto non ha piu' niente da difendere: il progresso del gioco
                    // dice che quella fight e' finita, e l'oggetto che la stanza ha rimesso e' un residuo.
                    // (Misurato: `G1:6719`, flag `D17Z01_BOSSDEAD` acceso, vita=400, boss lasciato in piedi.)
                    //
                    // E' QUI, e non dopo il test "boss normale di una stanza normale", perche' cosi' il veto
                    // e' VERIFICABILE: prima un boss vivo con il flag spento usciva in silenzio dalla riga
                    // sotto, e l'unica riga `NON nascosto` del corpus (`G1:6719`) era quella del caso
                    // ROVESCIO - flag acceso, veto che contraddiceva il flag. Ora la riga dice entrambe le
                    // cose: "flag spento => intoccato" e "flag acceso => scavalcato".
                    bool vivoScavalcato = BossLooksAlive(enemy);
                    if (vivoScavalcato && flag == null)
                    {
                        ModLog.Info($"[BossCorpse] {enemyId} NON nascosto ({where}): il boss sembra VIVO " +
                                    $"(statusDead=False flag={(flag ?? "nessuno")} vita={enemy.CurrentLife:F0}) " +
                                    "e un boss vivo non si tocca");
                        continue;
                    }

                    if (!statusDead && flag == null)
                        continue; // boss normale di una stanza normale: non si tocca niente

                    _hiddenBossCorpses.Add(enemy);
                    bool eraAttivo = enemy.gameObject.activeSelf;
                    enemy.gameObject.SetActive(false);

                    ModLog.Info($"[BossCorpse] {enemyId} nascosto ({where}): il boss di questa stanza era " +
                                $"gia' stato ucciso - statusDead={statusDead} flag={flag ?? "nessuno"} " +
                                $"tipo={tipo} attivoPrima={(eraAttivo ? "si" : "no: era gia' spento")} " +
                                "(SetActive(false), nessun Destroy)" +
                                (vivoScavalcato
                                    ? $" · veto SCAVALCATO: il flag e' acceso, il boss sembrava vivo (vita={enemy.CurrentLife:F0})"
                                    : string.Empty));
                }
            }
            catch (Exception e)
            {
                // Un'eccezione qui fermerebbe lo spawn di tutta la scena: si scrive e si prosegue.
                ModLog.Error($"[BossCorpse] ricerca dei cadaveri di boss fallita ({where}): " +
                             $"{e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>
        /// True quando il boss e' VIVO e operativo: acceso in gerarchia, non morto di stato, con vita, e con
        /// un `EnemyBehaviour` ACCESO (cioe' un cervello che il gioco sta eseguendo). E' la definizione che
        /// serve alla guardia di C: il cadavere lasciato dalla scena e' per costruzione una delle due cose
        /// opposte - spento/inerte (misurato: al rientro la seconda passata di spawn non lo registra piu',
        /// vedi scratch\DIAGNOSI-BOSS-AFK.md §4) oppure gia' `Status.Dead`.
        /// </summary>
        private static bool BossLooksAlive(Enemy enemy)
        {
            if (enemy == null || enemy.gameObject == null)
                return false;

            if (enemy.Status == null || enemy.Status.Dead)
                return false;

            if (!enemy.gameObject.activeInHierarchy)
                return false;

            if (enemy.CurrentLife <= 0f)
                return false;

            EnemyBehaviour behaviour = FindBehaviour(enemy);
            return behaviour != null && behaviour.enabled;
        }

        /// <summary>
        /// Il primo dei due nomi di flag `&lt;...&gt;_BOSSDEAD` che risulta ACCESO per questa scena, o null.
        ///
        /// `Core.Events.GetFlag` e' una LETTURA PURA (`EventManager.cs:131-140`: `ContainsKey` + `value`,
        /// nessuna creazione): chiedere di un flag che non esiste restituisce false e non scrive niente nel
        /// salvataggio. Ecco perche' si possono provare due nomi senza paura di sporcare la partita.
        ///
        /// I DUE NOMI, e perche' due: `Data\FlagStates.cs` elenca i flag dei boss in due forme - a stanza
        /// intera (`D01Z06S01_BOSSDEAD`, `D09Z01S03_BOSSDEAD`, `D01BZ08S01_BOSSDEAD`) e a ZONA
        /// (`D17Z01_BOSSDEAD`, per la stanza `D17Z01S11`). Non c'e' una regola unica nell'asset, quindi si
        /// provano entrambe invece di indovinare: la zona si ricava dal nome stanza con `D##[A-Z]?Z##`.
        ///
        /// ⚠️ NON COPERTE: le Amanecidas, il cui flag e' `SANTOS_AMANECIDA_&lt;arma&gt;_DEFEATED` e non si ricava
        /// dal nome della scena. Per loro resta il solo `Status.Dead`. Limite dichiarato nel referto.
        /// </summary>
        private static string FirstBossDeadFlagOn(string scene)
        {
            if (string.IsNullOrEmpty(scene))
                return null;

            string full = scene.ToUpperInvariant() + "_BOSSDEAD";
            if (Core.Events.GetFlag(full))
                return full;

            System.Text.RegularExpressions.Match zone = System.Text.RegularExpressions.Regex.Match(
                scene.ToUpperInvariant(), "^D\\d\\d[A-Z]?Z\\d\\d");

            if (!zone.Success)
                return null;

            string zoneFlag = zone.Value + "_BOSSDEAD";
            if (zoneFlag == full)
                return null;

            return Core.Events.GetFlag(zoneFlag) ? zoneFlag : null;
        }

        /// <summary>
        /// Risolve il boss da uccidere, e DICE DA DOVE l'ha preso (`via=`) perche' il marker lo mostri.
        ///
        /// ⚠️ PERCHE' NON BASTA `ResolveEnemy`, e questa non e' prudenza: e' MISURA.
        /// L'annuncio arriva DOPO `ReceiveEnemyKill`, che il ciclo di invio spedisce come pacchetto
        /// PRECEDENTE, e quel metodo toglie l'id da `_enemies` PRIMA di applicare il danno. Un boss e' un
        /// oggetto di SCENA - il suo id e' `en|…`, non `sp|…` - quindi non ha spawn point da interrogare, e
        /// `ResolveEnemy` (spawn point prima, cache poi) non lo trova piu'. In R3-G1 la riga della morte
        /// annunciata stampa l'id VUOTO ("[Danno] relay  amount=331", DUE spazi): e' `IdOf` che, passando
        /// da `ResolveEnemy`, non trova piu' niente.
        /// ⇒ si scende di un gradino: `_enemyIds` (istanza -> id) e' l'unica mappa che `ReceiveEnemyKill`
        /// non tocca, e `ForgetEnemy` la lascia intatta di proposito ("the instance keeps its id on
        /// purpose"). Se anche quella tace, l'id non e' risolvibile su questa macchina e lo si DICE, invece
        /// di far sembrare il fallimento un pacchetto perso.
        /// </summary>
        private static Enemy ResolveEnemyForBossDeath(string id, out string via)
        {
            Enemy direct = ResolveEnemy(id);
            if (direct != null && direct.gameObject != null)
            {
                via = "cache";
                return direct;
            }

            foreach (KeyValuePair<Enemy, string> pair in _enemyIds)
            {
                if (pair.Value != id)
                    continue;

                Enemy candidate = pair.Key;
                if (candidate != null && candidate.gameObject != null)
                {
                    via = "istanza";
                    return candidate;
                }
            }

            via = "nessuna";
            return null;
        }

        /// <summary>
        /// Last known position of every OTHER player, keyed by name, fed straight from the position
        /// packets that arrive from the server.
        ///
        /// This exists because reading the position off the scene object does not work: GetPosition() on
        /// the "other player" returned this machine's OWN coordinates on the host and nothing at all on
        /// the peer. The distance rule was therefore comparing an enemy against the local player twice,
        /// which made every machine believe it was the closest and left two owners for one enemy.
        /// Caching what actually arrives removes the dependency on that scene object entirely.
        /// </summary>
        private static readonly Dictionary<string, Vector2> _remotePositions = new Dictionary<string, Vector2>();

        /// <summary>
        /// When each remote player's position arrived, so a sample can be judged too old to arbitrate on.
        /// Keyed by player name, exactly like _remotePositions.
        /// </summary>
        private static readonly Dictionary<string, float> _remotePositionsAt = new Dictionary<string, float>();


        /// <summary>
        /// Where a non-owned enemy is heading, from the last snapshot received.
        ///
        /// State arrives about 20 times a second, and assigning it straight to transform.position made the
        /// enemy jump 20 times a second - visible as constant stutter on whichever machine is NOT
        /// simulating it. Keeping the snapshot as a target and moving towards it every frame turns those
        /// jumps back into motion. The value is the remote machine's real simulation result, so nothing is
        /// invented: it is the same path, just followed smoothly instead of in steps.
        /// </summary>
        private static readonly Dictionary<string, Vector2> _remoteTargets = new Dictionary<string, Vector2>();

        /// <summary>
        /// ⭐⭐ PUNTO 0 — IN CHE VISITA DELLA SCENA E' STATA SCRITTA OGNI VOCE DI `_remoteTargets` (id -> visita).
        ///
        /// PERCHE' SERVE. `_remoteTargets` non viene svuotata a `LevelLoaded`, di proposito (:2326-2331), quindi
        /// una voce puo' venire da una visita PRECEDENTE della stessa stanza: e' l'ultima posizione che il
        /// vecchio padrone aveva trasmesso, spesso quella in cui il boss si trovava quando la scena e' stata
        /// scaricata. Al rientro il gioco ricrea il boss NUOVO e al suo spawn, e `InterpolateRemoteEnemies` lo
        /// trapianta su quella voce. Misurato: `G1:L2826` `(-805.9,22.1)` (spawn) -> `G1:L2827`
        /// `(-822.0,9.0)`, UNA RIGA DOPO. E `-822.0` e' 6,3 unita' PRIMA del volume della cerimonia: il corpo
        /// del boss (solido, perche' e' lui il padrone) sbarra il corridoio, il giocatore non arriva al
        /// trigger, la cerimonia non parte, nessuno chiama `IntroJump` e il boss resta `Idle` per la visita.
        ///
        /// CON QUESTA DATA UNA VOCE SI PUO' DISTINGUERE: `_remoteTargetsVisit[id] == _sceneVisit` significa
        /// "il padrone la sta trasmettendo ADESSO", ed e' la verita' - una fight in corso si insegue come
        /// sempre. Un numero piu' basso significa "questa posizione viene da un'altra visita": per un BOSS non
        /// e' una verita' da applicare (la sua posizione la decide il gioco/la scena), per un nemico normale
        /// resta la verita' di sempre - e i nemici normali NON cambiano comportamento.
        /// </summary>
        private static readonly Dictionary<string, int> _remoteTargetsVisit = new Dictionary<string, int>();

        /// <summary>Quante scene sono state caricate in questa sessione: la "visita" corrente della stanza.</summary>
        private static int _sceneVisit;

        /// <summary>
        /// Id dei boss per cui il marker `[Insegue]` ha gia' detto che l'inseguimento e' stato saltato:
        /// la guardia gira A OGNI FRAME, e una riga per frame sarebbe 60 righe al secondo. Una per visita.
        /// </summary>
        private static readonly HashSet<string> _bossFollowSkipLogged = new HashSet<string>();

        /// <summary>
        /// Deaths announced while this machine was in another scene, or before the enemy existed here.
        ///
        /// Applied at registration time instead: the enemy may spawn moments later (or after a scene
        /// reload), and that is the point at which the death has to be enforced.
        /// </summary>
        private static readonly HashSet<string> _pendingDeaths = new HashSet<string>();

        /// <summary>
        /// True while this machine is replicating a bonfire announced by someone else.
        ///
        /// The replication calls the game's own RespawnDeadEnemies, which our own hook watches - so without a
        /// marker there is no way to tell a replicated bonfire from a player sitting down. Nothing is sent
        /// back today, but the moment anything is added to that hook the two become an infinite echo, and
        /// this is the same trap the vase fix already fell into once.
        ///
        /// Saved and restored rather than set and cleared: a replication can nest, and clearing the flag in
        /// an inner frame would open a window in which the outer one is mistaken for a real bonfire.
        /// </summary>
        private static bool _replicatingBonfire;

        /// <summary>True when the bonfire being processed was announced by another machine.</summary>
        public static bool IsReplicatingBonfire { get { return _replicatingBonfire; } }

        /// <summary>
        /// Every death this machine has announced, so a joiner can be told about them even when the
        /// announcer is no longer in the room where they happened.
        ///
        /// The announcer used to derive the ids from the spawn points of its CURRENT scene, so if it had
        /// walked away before someone reconnected the announcement was empty and the joiner saw enemies it
        /// had already killed - proven in the logs by a join line with no "[EnemyJoin] annunciate" before it,
        /// while the announcer stood in a room with no spawn points. Remembering the ids removes the
        /// dependency on where the announcer happens to be standing.
        ///
        /// NOT cleared on a level load, deliberately: crossing a level is exactly the case this exists for.
        /// It is cleared wherever the GAME forgets its own consumed list - the bonfire respawn, and the
        /// reset that follows a player death or a save load - because at that point the enemies really are
        /// alive again for both machines.
        ///
        /// Only deaths that happened while connected are recorded, because the hot send path lives inside
        /// UpdateCore, which returns early when there is no connection. A kill made in pure single-player
        /// never becomes an announcement, which matches what the peer can know about.
        /// </summary>
        private static readonly HashSet<string> _announcedDeaths = new HashSet<string>();

        /// <summary>
        /// The enemies this machine has announced as REVEALED, so a player who joins later still gets them.
        ///
        /// The birth announcement reaches whoever is connected at that moment - the server relays it to
        /// everyone but the sender, with no memory of its own - so without this a late joiner would keep
        /// seeing the hidden copy of an enemy that is already in play for everybody else: exactly the defect
        /// the birth channel fixes, just moved to the joiner. It is the twin of _announcedDeaths, on the same
        /// channel with the same reasoning, and it is cleared wherever that one is.
        /// </summary>
        private static readonly HashSet<string> _announcedBirths = new HashSet<string>();


        /// <summary>
        /// Enemies already reported as dead by the diagnostic snapshot, so each death is logged once.
        ///
        /// The kill detection used to live only inside the send loop, which returns early when there is no
        /// connection - so a single-player test, exactly where the game's own behaviour is easiest to study,
        /// produced no death records at all.
        /// </summary>
        private static readonly HashSet<string> _killLogged = new HashSet<string>();

        /// <summary>
        /// Handles for which a resolution failure has already been reported.
        ///
        /// A peer keeps receiving packets for an enemy it has already removed - that is normal, not an
        /// error, and logging every single one would flood the log and bury the failures that matter. One
        /// line per handle is enough to see which enemies are being asked about and not found; the count of
        /// what is still tracked is printed next to it, which is the actual signal.
        /// </summary>
        private static readonly HashSet<uint> _reportedResolveFailures = new HashSet<uint>();

        /// <summary>
        /// Last animation state hash seen per enemy, so a change can be logged exactly once.
        ///
        /// The game has no single place where enemy animations are triggered: every behaviour class of every
        /// enemy calls Animator.Play itself (AcolyteOverthrowBehaviour, BellCarrierIdleBehaviour,
        /// FlagellantHurtBehaviour, ...), and the AnimatorInyector base class exists only for the Penitent.
        /// There is therefore nothing to hook into, and the only reliable observation point is the Animator's
        /// own current state, read and compared.
        ///
        /// Read every frame rather than once per diagnostic snapshot: an attack can last less than the
        /// 0.5 second snapshot interval and would be missed entirely. Only CHANGES are written, so an enemy
        /// standing idle produces no lines at all.
        /// </summary>
        private static readonly Dictionary<string, int> _lastAnim = new Dictionary<string, int>();

        /// <summary>
        /// Last animation state this machine APPLIED from the owner, per enemy.
        ///
        /// Applying a state also changes the Animator, and the change was then read back as if it were news -
        /// transitions fed themselves, which showed up in the logs as one enemy bouncing between the same two
        /// states 634 times on the peer against 26 on the host. Remembering what was applied lets the
        /// animation log report only genuine local changes, so the feedback loop disappears from the records
        /// and the peer stops re-forcing what the owner did not change.
        /// </summary>
        private static readonly Dictionary<string, int> _appliedAnim = new Dictionary<string, int>();

        /// <summary>
        /// TEMPORARY (PerformanceProbe): the state the Animator showed at the moment we imposed a state, so
        /// the log can tell a transition we CAUSED from one the game made. Keyed by id, like _appliedAnim.
        ///
        /// _appliedAnim alone cannot do this: it is written during the frame that imposes the state, while
        /// the Animator keeps reporting the old state until the controller evaluates - one frame later.
        /// Recording the pair instead closes that gap. Remove with the probe.
        /// </summary>
        private static readonly Dictionary<string, int> _appliedAnimPrev = new Dictionary<string, int>();

        /// <summary>
        /// TEMPORARY (PerformanceProbe): the playback position last seen for each enemy, so the position the
        /// LEAVING state had reached can be reported. That number is what says whether a one-shot was cut
        /// short or simply finished. Remove with the probe.
        /// </summary>
        private static readonly Dictionary<string, float> _lastAnimSample = new Dictionary<string, float>();

        /// <summary>
        /// TEMPORARY (PerformanceProbe): when the current state began, on the transitions the game made.
        /// Echoes never reset it, or our own forcing would report a long-lived state as freshly started.
        /// Remove with the probe.
        /// </summary>
        private static readonly Dictionary<string, float> _lastAnimChangeAt = new Dictionary<string, float>();


        /// <summary>
        /// The gravity scale each frozen enemy had before this machine stopped its controller, keyed by
        /// instance id, so it can be given back exactly what it had.
        ///
        /// Restoring a guessed 1 would be wrong: GravityScale comes from the prefab (it is a
        /// [SerializeField] on PlatformCharacterPhysics) and most enemies carry 1, but the ones that zero it
        /// deliberately - ElderBrother after landing, some SetPositionAtStart overrides - would come back
        /// with gravity they never had. Instance id rather than the enemy id on purpose: this is a fact
        /// about the live object, and it must not survive the object being replaced.
        /// </summary>
        private static readonly Dictionary<int, float> _frozenGravityScale = new Dictionary<int, float>();

        /// <summary>
        /// Lo stato dei collisori d'OSTACOLO di un boss, fotografato nel momento in cui questa macchina lo
        /// riduce a pupazzo, cosi' da ridarglielo esattamente com'era.
        ///
        /// PERCHE' UNA FOTOGRAFIA E NON UN "RIACCENDI". `SmartPlatformCollider.enabled` e
        /// `EnableCollision2D` NON sono sempre accesi: il gioco stesso li spegne e li riaccende intorno ai
        /// teletrasporti di Crisanta (`Assembly-CSharp.cs:51574-51578`, `:52146-52150`, `:52190-52194`) e
        /// altri boss li pilotano a mano. La barriera (`blockCollider`) e' ancora piu' esplicita: e' un
        /// oggetto che le CLIP accendono e spengono (`AnimEvent_ActivateBarrier`,
        /// `AnimEvent_DeactivateBarrier`, `:55595-55603`). Riaccendere "per default" al risveglio
        /// rimetterebbe una barriera che l'animazione aveva spento: si restituisce il valore letto, non
        /// un'ipotesi. Stessa ragione di `_frozenGravityScale`.
        ///
        /// Chiave: instance id, come `_frozenGravityScale` - e' un fatto sull'oggetto vivo, e non deve
        /// sopravvivergli.
        /// </summary>
        private struct FrozenCollision
        {
            /// <summary>Il corpo fisico del boss (`Enemy.Controller.SmartPlatformCollider`), se c'e'.</summary>
            public SmartPlatformCollider Smart;

            /// <summary>I due valori che il gioco legge e scrive: `enabled` e `EnableCollision2D`.</summary>
            public bool SmartEnabled;
            public bool SmartCollision2D;

            /// <summary>La barriera d'ostacolo figlia del boss (`ElderBrotherAnimatorInyector.blockCollider`).</summary>
            public GameObject Barrier;

            /// <summary>Se la barriera era ACCESA quando il boss e' diventato pupazzo.</summary>
            public bool BarrierActive;
        }

        /// <summary>I collisori d'ostacolo tolti a ogni boss pupazzo, per poterli restituire al risveglio.</summary>
        private static readonly Dictionary<int, FrozenCollision> _frozenCollision =
            new Dictionary<int, FrozenCollision>();

        /// <summary>Fraction of the remaining gap closed per frame while following a remote snapshot.</summary>
        private const float FOLLOW_LERP = 0.25f;

        /// <summary>Beyond this the enemy is snapped instead of eased, so a real teleport is not smoothed.</summary>
        private const float FOLLOW_SNAP_DISTANCE = 6f;

        private static bool _isHost;
        private static float _sendTimer;
        private static float _rescanTimer;
        private static float _lastSweep;

        // True once the id table has been built for this scene. Guards the fallback below.
        private static bool _spawnFinalized;

        // Diagnostics: enough to tell whether the feature is actually doing anything in game.
        private static string _scene = string.Empty;
        private static int _updatesSent;
        private static int _updatesReceived;
        private static int _damageSent;
        private static int _damageApplied;
        private static int _killsSent;
        private static int _registrations;

        /// <summary>
        /// Enemies skipped because they are scenery, not enemies: created by the spawn pipeline but never
        /// activated by the game, so they have run no code. See IsInertScenery.
        /// </summary>
        private static int _inertSkipped;

        /// <summary>
        /// Deaths learned from another machine's join announcement, i.e. kills that happened before this
        /// machine was connected. Reported so the join fix is visible in the log rather than inferred.
        /// </summary>
        private static int _deathsAppliedFromPeers;
        private static int _duplicateIds;
        private static int _listFingerprint;
        /// <summary>
        /// When the divergence was last reported, so it is reported again after a cooldown instead of only
        /// once. Zero means never.
        /// </summary>
        private static float _fingerprintMismatchReported;

        /// <summary>How often a divergence may be reported. Long enough not to flood, short enough to see.</summary>
        private const float FINGERPRINT_REPORT_COOLDOWN = 15f;

        /// <summary>Total times a divergence was reported, so a recurring one is visible at a glance.</summary>
        private static int _fingerprintMismatches;

        /// <summary>
        /// Set whenever the id list is appended to or pruned, so the fingerprint can be recomputed once at
        /// the end of the pass instead of on every enemy. Without this the published fingerprint described
        /// a list that had already changed, and the divergence check reported enemies that were not there.
        /// </summary>
        private static bool _idsDirty;
        private static readonly HashSet<string> _countedDuplicates = new HashSet<string>();

        /// <summary>
        /// One-line health summary, surfaced by the "multiplayer sync" command. Built to be compared
        /// between the two machines: the tracked/spawn point counts must match on both.
        /// </summary>
        public static string Describe()
        {
            // Computed rather than counted, so rescans cannot inflate it.
            int notSpawned = _ids.Count - _enemies.Count;

            string warning = string.Empty;
            if (_duplicateIds > 0)
                warning += $" ATTENZIONE: {_duplicateIds} spawn point con identita' duplicata (id non univoci!)";
            if (_fingerprintMismatchReported != 0)
                warning += " ATTENZIONE: impronta lista diversa dall'host (vedi log)!";
            if (notSpawned > 0)
                warning += $" NOTA: {notSpawned} nemici non ancora creati dallo spawn point";

            return string.Format(
                "scene={0} role={1} connected={2} spawnPoints={3} tracked={4} registered={5} ids={6} fingerprint={7} | sent={8} recv={9} dmgSent={10} dmgApplied={11} killsSent={12}{13}",
                _scene,
                IsHost ? (Main.Multiplayer.NetworkManager.IsConnected ? "HOST" : "solo") : "peer",
                Main.Multiplayer.NetworkManager.IsConnected,
                _spawnPoints.Count,
                _enemies.Count,
                _registrations,
                _ids.Count,
                _listFingerprint,
                _updatesSent,
                _updatesReceived,
                _damageSent,
                _damageApplied,
                _killsSent,
                warning);

            // Scenery skipped this scene, reported separately so it never again looks like enemies that
            // fail to move. See IsInertScenery.
            if (_inertSkipped > 0)
                ModLog.Info($"[EnemyInert] {_inertSkipped} entita' di scena ignorate " +
                            $"(create dal gioco ma mai attive: nessun codice eseguito, quindi immobili per costruzione)");
        }

        /// <summary>
        /// Maps a damaged GameObject to its network handle. Used by the damage patch to forward a hit.
        /// </summary>
        public static bool TryGetEnemyHandle(Enemy enemy, out uint handle)
        {
            handle = 0;
            if (enemy == null)
                return false;

            for (int i = 0; i < _ids.Count; i++)
            {
                Enemy tracked = ResolveEnemy(_ids[i]);
                if (tracked == null)
                    continue;

                if (tracked == enemy || enemy.transform.IsChildOf(tracked.transform))
                {
                    handle = HandleOf(_ids[i]);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The network ID STRING of an enemy, for the one channel that must reach a player in ANOTHER SCENE.
        ///
        /// WHY NOT THE HANDLE NEXT DOOR. TryGetEnemyHandle returns a 32-bit hash, and a handle is
        /// deliberately SCENE-LOCAL: ResolveByHandle searches _ids, and _ids holds the enemies of the
        /// loaded scene only. The execution announcement travels on EnemyDeathGlobal, which the server
        /// relays to every player whatever scene they are in, so it must carry the id - the same reason
        /// SendEnemyDeathGlobal takes a string and not a handle.
        ///
        /// The body mirrors TryGetEnemyHandle line for line, and for the same reason: it walks _ids
        /// through ResolveEnemy, so the SPAWN POINT wins over the cache and a stale entry cannot be
        /// announced. Read-only: it touches nothing, so it is safe to call from a Harmony postfix.
        /// </summary>
        public static bool TryGetEnemyId(Enemy enemy, out string id)
        {
            id = null;
            if (enemy == null)
                return false;

            for (int i = 0; i < _ids.Count; i++)
            {
                Enemy tracked = ResolveEnemy(_ids[i]);
                if (tracked == null)
                    continue;

                if (tracked == enemy || enemy.transform.IsChildOf(tracked.transform))
                {
                    id = _ids[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The wire handle for an id: a 32-bit hash of the id STRING, not its position in a list.
        ///
        /// Position indices proved unworkable. The host and the peer do not always hold lists of the same
        /// size - one machine can have an extra entry for an enemy that no longer exists - and then every
        /// index beyond the shorter list is rejected and all updates for those enemies are lost. A handle
        /// derived from the id text is computed independently on each machine and stays correct whatever
        /// the other list contains.
        ///
        /// FNV-1a, written out here because string.GetHashCode() is not guaranteed to agree across
        /// processes or runtimes.
        /// </summary>
        private static uint HandleOf(string id)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;

                uint hash = offset;
                for (int i = 0; i < id.Length; i++)
                {
                    hash ^= id[i];
                    hash *= prime;
                }
                return hash;
            }
        }

        private static Enemy ResolveByHandle(uint handle)
        {
            // A scene holds tens of enemies at most, so a linear scan is cheaper than maintaining a
            // second dictionary that would have to be kept in step with the id list.
            for (int i = 0; i < _ids.Count; i++)
            {
                if (HandleOf(_ids[i]) == handle)
                    return ResolveEnemy(_ids[i]);
            }

            // Silent on purpose: a peer is told about deaths through the synced health, so it can still
            // receive packets for an enemy it has already removed. That is normal, not an error.
            return null;
        }

        /// <summary>
        /// La copia LOCALE del nemico che un handle nomina, o null. Serve al nuovo verso
        /// nemico→giocatore: il pacchetto porta l'handle dell'attaccante, e il ricevente ne ha bisogno
        /// per un motivo che non e' cosmetico — `PenitentDamageArea.TakeDamage` fa
        /// `hit.AttackingEntity.transform.position` per orientare il dolore e lo spintone, quindi
        /// l'attaccante deve essere un GameObject VERO. La copia locale del boss ha la stessa posizione
        /// e lo stesso nome, ed e' la scelta che non inventa niente.
        /// Silenziosa come `ResolveByHandle`: un nemico puo' essere gia' sparito da questa scena, e non
        /// e' un errore (il chiamante ha il suo ripiego).
        /// </summary>
        public static Enemy EnemyForHandle(uint handle)
        {
            return ResolveByHandle(handle);
        }

        /// <summary>
        /// Reports, once per handle, that a packet could not be matched to an enemy here.
        ///
        /// This is OBSERVATION, not a verdict. Two cases are kept apart on purpose:
        ///   - the handle is not in _ids at all: either this machine never knew the enemy, or it already
        ///     forgot it. The counts printed next to it (how many it tracks now) are the real signal for
        ///     the divergence the voice-45 desync is about;
        ///   - the handle IS in _ids but the enemy resolves to null: the record was kept while the
        ///     instance is gone, which is the normal "already removed" case.
        ///
        /// ResolveByHandle conflates the two, which is why this reads FindIdOfHandle and ResolveEnemy
        /// separately instead of calling it.
        ///
        /// Deduplicated by handle: a peer can be told about an already-removed enemy many times, and a
        /// line per packet would bury the failures that matter. One line per handle is the whole story.
        /// </summary>
        private static void ReportResolveFailure(uint handle)
        {
            if (!_reportedResolveFailures.Add(handle))
                return;

            if (!FindIdOfHandle(handle, out string id))
            {
                ModLog.Info($"[EnemySync] t={Time.time:F1} handle={handle:X8} non in _ids " +
                            $"(ids={_ids.Count}, spawn={_spawnPoints.Count})");
                return;
            }

            ModLog.Info($"[EnemySync] t={Time.time:F1} {FullId(id)} handle={handle:X8} " +
                        $"gia' rimosso (ResolveEnemy=null)");
        }

        private static bool FindIdOfHandle(uint handle, out string id)
        {
            for (int i = 0; i < _ids.Count; i++)
            {
                if (HandleOf(_ids[i]) == handle)
                {
                    id = _ids[i];
                    return true;
                }
            }

            id = null;
            return false;
        }

        private static EnemyState GetOrCreateState(string id)
        {
            EnemyState state;
            if (!_lastSent.TryGetValue(id, out state))
            {
                state = new EnemyState();
                _lastSent[id] = state;
            }
            return state;
        }

        /// <summary>
        /// Every enemy spawn point in the scene, with the flags that decide whether it spawns.
        ///
        /// This is the diagnostic for the death problem. The game gives enemies no static id: it tracks a
        /// defeated spawn point by its POSITION (EnemySpawner keeps a List&lt;Vector3&gt;) and offers no
        /// per-spawn-point string id at all. SpawningId, which looks like one, is just
        /// gameObject.GetHashCode() - different on every machine and every run.
        ///
        /// CreateEnemy returns early when EnemySpawnDisabled is set, or when the point is already Consumed,
        /// or when HasEnemySpawned is set. Reading those three flags on both machines shows exactly why one
        /// machine spawns an enemy the other considers dead.
        /// </summary>
        public static List<string> DumpSpawnPoints()
        {
            List<string> lines = new List<string>();
            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();
            lines.Add($"spawn point nella scena: {points.Length}");

            for (int i = 0; i < points.Length; i++)
            {
                EnemySpawnPoint sp = points[i];
                if (sp == null)
                    continue;

                Vector3 p = sp.transform.position;
                Enemy spawned = sp.SpawnedEnemy;

                lines.Add($"[{i,2}] {sp.name} @({p.x:F2},{p.y:F2}) " +
                          $"entita='{sp.EntityName}' consumed={sp.Consumed} spawned={sp.HasEnemySpawned} " +
                          $"persistenza={sp.EnablePersistence} disabilitato={sp.EnemySpawnDisabled} " +
                          $"nemico={(spawned == null ? "nessuno" : spawned.name)} " +
                          DescribeEnemyState(spawned));
            }

            return lines;
        }

        /// <summary>
        /// The flags that separate a live enemy from the inert "ghost".
        ///
        /// The ghost reported in game was visible, idle and untargetable with no health, which is not a live
        /// enemy and not a corpse either: Dead was set but the death sequence never finished, leaving the
        /// entity in the middle. Printing these fields shows which of those states a stuck enemy is really
        /// in, instead of guessing from how it looks.
        /// </summary>
        private static string DescribeEnemyState(Enemy enemy)
        {
            if (enemy == null)
                return "vivo=n/d";

            EntityStatus status = enemy.Status;
            string untouchable = status != null ? status.Unattacable.ToString() : "n/d";
            string invulnerable = status != null ? status.Invulnerable.ToString() : "n/d";
            string onCamera = status != null ? status.IsVisibleOnCamera.ToString() : "n/d";
            string hurt = status != null ? status.IsHurt.ToString() : "n/d";
            string idle = status != null ? status.IsIdle.ToString() : "n/d";

            // Alpha below 0.5 is not decoration: the game switches the damage collider off when the sprite
            // is semi-transparent, which is one concrete way an enemy looks present yet cannot be hit.
            SpriteRenderer renderer = enemy.GetComponentInChildren<SpriteRenderer>();
            string alpha = renderer != null ? renderer.color.a.ToString("F2") : "n/d";

            // Range is the other deactivation path: about seven enemy types disable themselves when the
            // target is farther than ActivationRange (default 20, set only in the prefab, never at runtime).
            return $"vivo={(!enemy.Dead && enemy.CurrentLife > 0f)} Dead={enemy.Dead} " +
                   $"vita={enemy.CurrentLife:F1} vulnerabile={enemy.IsVulnerable} " +
                   $"intargettabile={untouchable} invulnerabile={invulnerable} " +
                   $"inCamera={onCamera} hurt={hurt} idle={idle} alpha={alpha} " +
                   $"distTarget={enemy.DistanceToTarget:F1} range={enemy.ActivationRange:F0} " +
                   $"attivo={enemy.gameObject.activeSelf} " +
                   $"behaviour={(enemy.EnemyBehaviour == null ? "assente" : enemy.EnemyBehaviour.enabled.ToString())}";
        }

        /// <summary>
        /// The spawn points the game ITSELF considers defeated - the list EnemySpawner keeps.
        ///
        /// This is the authoritative answer to "what does the game think is dead here", and it is cleared
        /// when the player rests at a bonfire (EnemySpawner.RespawnDeadEnemies). Comparing this on the two
        /// machines shows directly whether they agree, instead of inferring it from behaviour.
        /// </summary>
        public static List<string> DumpConsumedSpawnPoints()
        {
            List<string> lines = new List<string>();

            EnemySpawner spawner = Core.Logic != null ? Core.Logic.EnemySpawner : null;
            if (spawner == null)
            {
                lines.Add("EnemySpawner non disponibile");
                return lines;
            }

            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();

            // The game identifies a defeated spawn point by its POSITION: EnemySpawner keeps a
            // List<Vector3> and both AddConsumedSpawner and IsSpawnerConsumed compare
            // sp.transform.position with an exact Contains. Two spawn points sharing a position are
            // therefore the same entry to the game, and killing one defeats both.
            //
            // Counting spawn points would report 2 for a single kill whenever a position is shared, which
            // is misleading: the number that describes the game's state is the number of distinct consumed
            // POSITIONS. Both figures are shown so the difference is visible instead of confusing.
            List<Vector3> positions = new List<Vector3>();
            int consumedPoints = 0;

            for (int i = 0; i < points.Length; i++)
            {
                EnemySpawnPoint sp = points[i];
                if (sp == null || !spawner.IsSpawnerConsumed(sp))
                    continue;

                consumedPoints++;

                Vector3 p = sp.transform.position;
                bool alreadyListed = false;
                for (int j = 0; j < positions.Count; j++)
                {
                    if (positions[j] == p)
                    {
                        alreadyListed = true;
                        break;
                    }
                }

                if (alreadyListed)
                {
                    lines.Add($"[consumato] {sp.name} @({p.x:F2},{p.y:F2}) entita='{sp.EntityName}' " +
                              "(stessa posizione di un'altra: il gioco li considera un unico punto)");
                    continue;
                }

                positions.Add(p);
                lines.Add($"[consumato] {sp.name} @({p.x:F2},{p.y:F2}) entita='{sp.EntityName}'");
            }

            lines.Add($"posizioni consumate: {positions.Count}  " +
                      $"(spawn point che ne risultano consumati: {consumedPoints} su {points.Length})");
            return lines;
        }

        /// <summary>
        /// Per-enemy listing. Run "multiplayer sync" on both machines: the handles must be identical,
        /// because they are what travels on the wire.
        /// </summary>
        public static List<string> Dump()
        {
            List<string> lines = new List<string>();
            lines.Add($"enemies={_enemies.Count} spawnPoints={_spawnPoints.Count} ids={_ids.Count} " +
                      $"fingerprint={_listFingerprint} duplicati={_duplicateIds}");

            if (_duplicateIds > 0)
            {
                // Non-zero means the same enemy was offered a second id, which is exactly the condition
                // that produced a duplicated enemy in game: the two machines then track different enemies.
                lines.Add("ATTENZIONE: lo stesso nemico ha ricevuto piu' di un id. " +
                          "Se il valore non e' 0 su entrambe le macchine, le due liste divergono.");
            }

            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                Enemy enemy = ResolveEnemy(id);

                if (enemy == null)
                {
                    lines.Add($"[{i,3}] handle={HandleOf(id)} ID {id} -> nemico non risolto");
                    continue;
                }

                lines.Add($"[{i,3}] handle={HandleOf(id)} ID {id}");
            }
            return lines;
        }

        /// <summary>
        /// True while damage relayed by the host is being applied, so the damage patch lets it through
        /// instead of forwarding it again (which would loop).
        /// </summary>
        public static bool IsApplyingRemoteDamage { get; private set; }

        /// <summary>
        /// True when this client drives enemy simulation. Also true while offline, so single-player
        /// keeps behaving exactly like vanilla.
        /// </summary>
        public static bool IsHost => _isHost || !Main.Multiplayer.NetworkManager.IsConnected;

        /// <summary>Set by NetworkManager when the server assigns (or revokes) the host role.</summary>
        public static void SetHost(bool isHost)
        {
            bool wasHost = _isHost;
            _isHost = isHost;
            ModLog.Info($"[EnemySync] Host role: {isHost}");

            // Note the asymmetry with IsHost: that property also reports true while offline, so a client
            // that is connected but not (or no longer) the host must be brought in line here.
            if (isHost && !wasHost)
            {
                // Promoted (typically because the previous host disconnected): take over simulation.
                // Without this the new host would keep broadcasting frozen puppet enemies.
                _sendTimer = 0f;
                for (int i = 0; i < _ids.Count; i++)
                {
                    Enemy enemy = ResolveEnemy(_ids[i]);
                    if (enemy == null)
                        continue;

                    var behaviour = FindBehaviour(enemy);
                    if (behaviour == null || behaviour.enabled)
                        continue;

                    behaviour.enabled = true;
                    behaviour.StartBehaviour();
                }

                // The hold timer restarts from now: inherited from the previous host it would already be
                // expired, and OWNER_HOLD_SECONDS is what keeps a contested enemy from changing hands on the
                // first comparison.
                //
                // Ownership is claimed here too, not just the timer. Before this, a promoted machine owned
                // NOTHING: the records still said "not mine", and taking them back was left to
                // DecideOwnership - which needs a live connection, a stale remote position (0.3 s) and then
                // the 0.5 s hold. So for roughly half a second to a second after a handover NOBODY simulated
                // the enemies, and the log showed it as "four of five owned by nobody" before it healed
                // itself. This machine is the host now, so by definition it simulates: say so immediately.
                for (int i = 0; i < _ids.Count; i++)
                {
                    EnemyState state;
                    if (!_lastSent.TryGetValue(_ids[i], out state))
                        continue;

                    state.OwnedByLocal = true;
                    state.OwnedByRemote = false;
                    state.OwnershipKnown = true;
                    state.OwnerSince = Time.time;
                    ForgetBossLifeAlignment(state);   // promozione: la proprieta' e' cambiata su tutto
                }
            }
            else if (!isHost && wasHost)
            {
                // OFFLINE: the demotion is not a demotion at all, and pausing here freezes the session.
                //
                // This is the branch that produced the worst defect measured so far. When the host's
                // connection drops, Multiplayer.OnDisconnect calls SetHost(false), so this branch runs - but
                // IsHost is `_isHost || !IsConnected`, which means the machine is STILL the host as far as
                // everything else is concerned, and there is no other player left to hand the enemies to.
                // Pausing them there, and clearing their ownership, left five of five enemies suspended
                // mid-stride: the log showed 70 identical samples over 35 seconds with the walk animation
                // playing and no movement, while the player walked through them.
                //
                // Nothing could undo it either: DecideOwnership has a single caller and it sits behind
                // `if (!IsConnected) return;`, so with no connection the arbitrating loop never runs, and
                // ResumeEnemy is only reachable from paths that need a peer. Single-player is exactly the
                // state the mod already defines for IsHost while offline, so the fix is to stop simulating
                // nothing and keep simulating everything.
                if (!Main.Multiplayer.NetworkManager.IsConnected)
                {
                    for (int i = 0; i < _ids.Count; i++)
                    {
                        EnemyState state;
                        if (_lastSent.TryGetValue(_ids[i], out state))
                        {
                            state.OwnedByLocal = true;
                            state.OwnedByRemote = false;
                            state.OwnershipKnown = true;
                            state.OwnerSince = Time.time;
                        }

                        // ResumeEnemy gives back both the behaviour tree AND the PlatformCharacterController.
                        // Pausing only the tree would leave the body unable to move.
                        Enemy enemy = ResolveEnemy(_ids[i]);
                        if (enemy != null)
                            ResumeEnemy(enemy);
                    }

                    // The previous host's positions say nothing about a session that no longer exists.
                    _remotePositions.Clear();
                    _remotePositionsAt.Clear();
                    return;
                }

                // Demoted: stop simulating, otherwise the AI keeps running locally and fights the state
                // the real host is broadcasting.
                for (int i = 0; i < _ids.Count; i++)
                {
                    Enemy enemy = ResolveEnemy(_ids[i]);
                    if (enemy == null)
                        continue;

                    PauseEnemy(enemy);
                }

                // And stop BELIEVING it owns them. PauseEnemy stops the simulation but leaves the record
                // saying "mine", and a demoted machine reads that record to decide whether to send - and
                // ReceiveEnemyOwnership returns early while the belief matches, so the first token from the
                // new host would be a no-op. OwnershipKnown is therefore cleared too: this machine has to be
                // willing to be told again.
                for (int i = 0; i < _ids.Count; i++)
                {
                    EnemyState state;
                    if (!_lastSent.TryGetValue(_ids[i], out state))
                        continue;

                    state.OwnedByLocal = false;
                    state.OwnershipKnown = false;
                    state.OwnerSince = 0f;
                    state.OwnedByRemote = false;
                    ForgetBossLifeAlignment(state);   // demozione: la vita va ripresa dal nuovo host
                }

                // Positions reported by the previous host are not evidence about who is closer now.
                _remotePositions.Clear();
                _remotePositionsAt.Clear();
            }
        }

        /// <summary>
        /// Hands an enemy over to the host: freeze it completely, so the state the real owner sends is the
        /// state that shows.
        ///
        /// Pausing the behaviour tree alone is NOT enough, and this is the part that took the longest to
        /// find. Verified in the game's IL:
        ///
        ///  - PlatformCharacterController.Update() keeps running. It copies transform.position into the
        ///    physics, integrates velocity, drag and gravity, and writes the transform straight back. Any
        ///    position applied from outside is therefore undone on the next frame, and with velocity near
        ///    zero the enemy is put back exactly where it was - the "enemy stays frozen" seen in game. The
        ///    game itself switches this cycle off with Controller.enabled = false inside Enemy.EnablePhysics,
        ///    so that is the lever to use.
        ///  - The enemy can restart its own AI. BellGhost.OnUpdate and Stoners.OnUpdate call
        ///    BehaviourTree.StartBehaviour() with no guard, every frame, whenever the local player is within
        ///    ActivationRange; several other behaviours do the same behind an activation distance. Since
        ///    Graph.Pause() leaves isRunning false, such a call really does restart the tree, so a pause
        ///    imposed from outside is cancelled on the next frame. Disabling the component is what makes the
        ///    pause stick: EnemyBehaviour.Update() only runs while the component is enabled.
        ///
        /// The enemy is left active on purpose. On an inactive GameObject Unity delivers no Update at all,
        /// so the controller would stop - but so would everything else, including the ability to apply the
        /// position received: switching the GameObject off is the wrong way round.
        /// </summary>
        /// <summary>
        /// The behaviour tree component of an enemy, however that type chooses to expose it.
        ///
        /// `Enemy.EnemyBehaviour` is NOT the general answer, and assuming it was cost the sync a real
        /// behaviour: only 17 of the 77 Enemy subclasses ever call Enemy::set_EnemyBehaviour (verified in IL).
        /// The other 60 - including Fool and WheelCarrier, every enemy in the test room, and most bosses -
        /// keep the component in a property of their own (Fool.Behaviour, WheelCarrier.Behaviour; both public,
        /// assigned in OnStart/OnAwake) and never touch the base one.
        ///
        /// Reading only the base property made PauseEnemy return before PauseBehaviour and enabled = false, so
        /// for 78% of enemy types the pause simply did not happen: on the non-owner the NodeCanvas tree, the
        /// Animator and the orientation kept running while the body stood still. Measured on a single unowned
        /// Fool: 437 orientation writes and 54 animation restarts, with its position moving by 0.1.
        ///
        /// The component is on the enemy itself, since EnemySpawnPoint.CreateEnemy parents the new enemy to
        /// the level, not to the spawn point - the same reason the code elsewhere had to stop using
        /// GetComponentInParent<EnemySpawnPoint>. The lookup is not cached here on purpose: it runs on the
        /// owned/non-owned transition rather than per frame, and GetComponent on a live object is cheap
        /// compared with being wrong about whether the enemy is paused.
        /// </summary>
        private static EnemyBehaviour FindBehaviour(Enemy enemy)
        {
            if (enemy == null)
                return null;

            EnemyBehaviour behaviour = enemy.EnemyBehaviour;
            if (behaviour != null)
                return behaviour;

            // GetComponentInChildren as well, because a behaviour living on a child would otherwise still be
            // missed - the failure mode being fixed here is precisely "assumed it was somewhere it was not".
            behaviour = enemy.GetComponent<EnemyBehaviour>();
            if (behaviour != null)
                return behaviour;

            return enemy.GetComponentInChildren<EnemyBehaviour>();
        }

        private static void PauseEnemy(Enemy enemy, string reason = null)
        {
            FreezePhysics(enemy);
            _puppetEnemies.Add(enemy);

            // PauseBehaviour is called UNCONDITIONALLY, and that is deliberate.
            //
            // The tree is not ticked by EnemyBehaviour.Update: NodeCanvas ticks it through
            // MonoManager.add_onUpdate, registered by Graph.StartGraph and removed by Graph.Pause.
            // Disabling the component therefore does NOT keep the tree paused - it only stops OnUpdate
            // from running. Behaviours that restart the tree directly, without going through
            // EnemyBehaviour.Update, keep working while the component is disabled:
            // ShieldMaidenBehaviour.Update, RangedBoomerangBehaviour.Update, FirethrowerBehaviour.Update,
            // CrossCrawlerBehaviour.Update, NunBehaviour.OnUpdate, FoolBehaviour.OnUpdate,
            // ExplodingEnemyBehaviour.OnUpdate, LesmesBehaviour.Update, the boss intro coroutines,
            // BishopHurtBehaviour.OnStateExit, and - worst of all - PlayMaker's
            // Tools.Playmaker2.Action.BehaviourStart.OnEnter and Tools.PlayMaker.Action.EnableEnemiesBehaviour.
            //
            // So guarding on !behaviour.enabled left the real gap: once something outside restarted the
            // tree, the pause was never re-applied, because the component was already disabled and this
            // method returned early. Calling PauseBehaviour every time costs nothing - it is idempotent
            // (it guards on BehaviourTree.isRunning) - and it closes the gap.
            var behaviour = FindBehaviour(enemy);
            if (behaviour == null)
                return;

            // ONE call, AFTER the action, and both halves of that matter.
            //
            // BEFORE was the original defect: if the report threw, PauseBehaviour and enabled = false never
            // ran, so the diagnostic cancelled the very thing it existed to observe.
            //
            // TWICE was worse, not safer. The deduplication key includes the outcome, so alternating
            // ":prima" and ":dopo" made every comparison fail and killed the dedup outright - and this runs
            // from PauseEnemy, which the send loop calls twenty times a second per unowned enemy and the
            // packet path calls again, so the log would have grown by tens of lines per second per enemy.
            // And it protected nothing: LogTreeState runs the same statements either way, and PauseBehaviour
            // destroys nothing, so a throw would have happened in ":prima" too - before the action.
            //
            // One call afterwards is strictly better on both counts: the pause is guaranteed to have
            // happened, and the key is stable again so only real changes are reported.
            behaviour.PauseBehaviour();
            behaviour.enabled = false;

            LogTreeState(enemy, behaviour, reason ?? "?");
        }

        /// <summary>
        /// Stops the enemy's own physics from fighting the position the owner sends.
        ///
        /// Clearing velocity and gravity as well as disabling the controller matters for the moment the
        /// enemy is handed back: the controller would otherwise resume with its old momentum and gravity
        /// scale, and the enemy would slide away from where the owner had it.
        ///
        /// GravityScale is only a value held for the handover: the one consumer of m_gravityScale is
        /// UpdatePhysics, which is called only from Controller.Update, so while the controller is disabled
        /// it has no effect at all. What it must not do is destroy the value the prefab started with, which
        /// is why ThawPhysics restores the original rather than assuming 1.
        ///
        /// This runs once per frame per non-owned enemy, not on every packet: the call site is the send
        /// loop, where PauseEnemy sits above the "only the owner transmits" gate. It is a handful of field
        /// writes with no allocation, so the cost is negligible next to PlaceEnemy.
        /// </summary>
        private static void FreezePhysics(Enemy enemy)
        {
            // ⭐ FIX ① — PRIMA di ogni altra cosa: un pupazzo non deve essere SOLIDO.
            //
            // È qui e non altrove perché è qui che la mod spegne la fisica del pupazzo: `controller.enabled
            // = false` ferma il corpo, `GravityScale = 0` toglie la gravità, ma NESSUNO dei due tocca i
            // collisori. Il risultato misurato era un boss fermo e indistruttibile di fatto, in piedi in
            // mezzo all'arena, che blocca il passaggio (e quindi il trigger della bossroom).
            FreezeCollision(enemy);

            var controller = enemy.Controller;
            if (controller == null)
                return;

            controller.enabled = false;

            var physics = controller.PlatformCharacterPhysics;
            if (physics == null)
                return;

            if (!_frozenGravityScale.ContainsKey(enemy.GetInstanceID()))
                _frozenGravityScale[enemy.GetInstanceID()] = physics.GravityScale;
            // HSpeed and VSpeed are not separate states: set_HSpeed writes m_vel.x and set_VSpeed writes
            // m_vel.y (verified in IL), so zeroing Velocity already covers both. They are written anyway
            // because the properties exist and are cheap, but Velocity is the one that matters.
            physics.Velocity = Vector3.zero;
            physics.Acceleration = Vector3.zero;
            physics.HSpeed = 0f;
            physics.VSpeed = 0f;
            physics.GravityScale = 0f;
        }

        /// <summary>
        /// ⭐ FIX ① — TOGLIE AL BOSS PUPAZZO I COLLISORI CHE BLOCCANO, e nient'altro.
        ///
        /// IL DIFETTO. `FreezePhysics` spegne il `PlatformCharacterController` e la gravità ma lascia
        /// accesi i collisori: un boss che questa macchina non simula resta FERMO E SOLIDO. Se lo sprite
        /// finisce davanti a un trigger, il trigger non si raggiunge. E' l'unico oggetto della scena che
        /// nessuno puo' ne' combattere (la sua AI e' spenta qui) ne' togliere (il veto di C lo protegge).
        ///
        /// LE DUE LEVE SONO QUELLE DEL GIOCO, verificate in IL, non inventate:
        ///   · `SmartPlatformCollider.enabled = false` + `EnableCollision2D = false` — il corpo fisico del
        ///     boss (`ElderBrother.Collider`, `Assembly-CSharp.cs:55494`/`:55510`). Il gioco usa ESATTAMENTE
        ///     queste due righe, sugli stessi campi, per rendere non solida Crisanta
        ///     (`:51574-51575` e `:52146-52147`, con riaccensione simmetrica `:51577-51578` / `:52149-52150`).
        ///   · `blockCollider.SetActive(false)` — la barriera d'ostacolo figlia del boss, che le clip accendono
        ///     e spengono (`AnimEvent_DeactivateBarrier`, `:55600-55603`): spegnerla e' cio' che il gioco fa
        ///     gia' da solo nella sua stessa sequenza.
        ///
        /// COSA NON FA, ed e' la ragione per cui e' lecita: nessuna scrittura su `Status`, `Stats.Life`,
        /// flag di progresso, `Kill()`, `Damage()`, `Behaviour.Death()`, cutscene, `BossFightManager`. Il
        /// boss resta VIVO, con la sua vita, la sua barra e il suo danno: semplicemente non fa piu' da muro.
        ///
        /// SOLO I BOSS (`IsBossByNamespace`, lo stesso test usato altrove in questo file). Un nemico
        /// normale non viene toccato: il difetto misurato e' del boss, e non c'e' ragione di allargare il
        /// cambiamento a 60 classi di nemici che nessuno ha misurato.
        ///
        /// ⚠️ LA PARTE "Applica comunque" NON E' UNA RIPETIZIONE. Questo metodo gira da `PauseEnemy`, che il
        /// ciclo di invio e il ramo dei pacchetti chiamano di continuo, quindi la fotografia si fa UNA volta
        /// sola (chiave instance id) - ma lo spegnimento si RIAPPLICA a ogni chiamata, perche' l'Animator del
        /// pupazzo continua a girare (e' scritto sopra: `PauseBehaviour` non ferma l'Animator) e una clip
        /// puo' riaccendere la barriera con `AnimEvent_ActivateBarrier`. `SetActive(false)` su un oggetto
        /// gia' spento e' idempotente e non costa niente.
        /// </summary>
        private static void FreezeCollision(Enemy enemy)
        {
            try
            {
                if (enemy == null || enemy.gameObject == null)
                    return;

                if (!IsBossByNamespace(enemy))
                    return;

                int key = enemy.GetInstanceID();

                FrozenCollision saved;
                bool primaVolta = !_frozenCollision.TryGetValue(key, out saved);

                if (primaVolta)
                {
                    saved = new FrozenCollision();

                    // Il controller puo' mancare su un nemico non ancora inizializzato: non e' un motivo per
                    // saltare la barriera, che vive altrove, quindi ogni lettura ha la sua guardia.
                    SmartPlatformCollider smart = null;
                    PlatformCharacterController controller = enemy.Controller;
                    if (controller != null)
                        smart = controller.SmartPlatformCollider;

                    if (smart != null)
                    {
                        saved.Smart = smart;
                        saved.SmartEnabled = smart.enabled;
                        saved.SmartCollision2D = smart.EnableCollision2D;
                    }

                    GameObject barrier = BossBarrierOf(enemy);
                    if (barrier != null)
                    {
                        saved.Barrier = barrier;
                        saved.BarrierActive = barrier.activeSelf;
                    }

                    _frozenCollision[key] = saved;
                }

                if (saved.Smart != null)
                {
                    saved.Smart.enabled = false;
                    saved.Smart.EnableCollision2D = false;
                }

                if (saved.Barrier != null && saved.Barrier.activeSelf)
                    saved.Barrier.SetActive(false);

                if (primaVolta)
                {
                    ModLog.Info($"[BossSolido] {IdOf(enemy)} boss pupazzo: collisori d'OSTACOLO spenti " +
                                $"(corpo={(saved.Smart == null ? "n/d" : "spento")} " +
                                $"eraCorpo=enabled:{saved.SmartEnabled}/EnableCollision2D:{saved.SmartCollision2D} " +
                                $"barriera={(saved.Barrier == null ? "n/d" : (saved.BarrierActive ? "spenta" : "era gia' spenta"))} " +
                                $"vita={enemy.CurrentLife:F0}) - resta VIVO e con il suo danno: non e' piu' un muro");
                }
            }
            catch (Exception e)
            {
                // Un'eccezione qui fermerebbe PauseEnemy, cioe' la sospensione del pupazzo: si scrive e si
                // prosegue, e il boss resta solido com'era prima del fix (nessun danno nuovo).
                ModLog.Error($"[BossSolido] spegnimento dei collisori d'ostacolo fallito: " +
                             $"{e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>
        /// La barriera d'ostacolo del boss, se quel tipo ne ha una.
        ///
        /// In tutta `Assembly-CSharp` esiste UN solo campo `blockCollider` (`:55593`), ed e' di
        /// `ElderBrotherAnimatorInyector`: la barriera del Fratello Maggiore, accesa e spenta dalle clip
        /// (`:55595-55603`). Non si passa dalla riflessione perche' il tipo e' verificato e il compilatore
        /// puo' controllarlo: `ElderBrother.AnimatorInyector` e' pubblico (`:55498`, assegnato in `OnAwake`
        /// `:55513`) e `blockCollider` e' un campo pubblico `:55593`.
        ///
        /// Per tutti gli altri boss non c'e' niente da restituire: il loro muro e' il corpo, e lo copre
        /// `SmartPlatformCollider`. Se un domani un altro boss avesse un campo con quel nome, va aggiunto qui
        /// con la sua firma verificata - non indovinato con la riflessione.
        /// </summary>
        private static GameObject BossBarrierOf(Enemy enemy)
        {
            Gameplay.GameControllers.Bosses.ElderBrother.ElderBrother elder =
                enemy as Gameplay.GameControllers.Bosses.ElderBrother.ElderBrother;

            if (elder == null || elder.AnimatorInyector == null)
                return null;

            return elder.AnimatorInyector.blockCollider;
        }

        /// <summary>
        /// Gives an enemy back to its own AI, on the machine that now owns it.
        ///
        /// PauseBehaviour only stops the NodeCanvas behaviour tree, not the Animator, so a non-owner keeps
        /// animating idle and walking on its own and shows hit reactions through the synced health. What a
        /// paused enemy cannot do is decide to attack, which is exactly why the owner runs the AI: attacks
        /// and deaths then come from the game's own per-enemy *AnimatorInyector code instead of being
        /// forced from outside.
        /// </summary>
        private static void ResumeEnemy(Enemy enemy, string reason = null)
        {
            _puppetEnemies.Remove(enemy);
            ThawPhysics(enemy);

            var behaviour = FindBehaviour(enemy);
            if (behaviour == null)
            {
                LogTreeState(enemy, null, "resume:no-behaviour");
                return;
            }

            // ⚠️ NO EARLY RETURN HERE, and that is the fix rather than a matter of style.
            //
            // This used to return as soon as the component was already enabled, and the asymmetry was the
            // whole bug: PauseEnemy ALWAYS calls PauseBehaviour, while this method skipped StartBehaviour
            // whenever the component happened to be enabled - and an enemy can sit with its component ENABLED
            // and its TREE STOPPED, a state the game itself produces. EnemyBehaviour.StartBehaviour only
            // restarts `if (!BehaviourTree.isRunning)`, so calling it unconditionally is exactly right: it
            // does the work when the tree is stopped and nothing at all when the tree is running.
            //
            // What the old shape left behind was permanent. With the component enabled, EnemyBehaviour.Update
            // runs and would normally be the thing that restarts the tree - but this method is the ONLY caller
            // of StartBehaviour in the whole project, so a stopped tree had nothing left to wake it. The enemy
            // stood still for the rest of the session: the "enemies have no AI" that a player's death made
            // visible. It was there before the death, at G1:1000, long before any of this.
            //
            // Idempotent in the game's own IL, so calling it on every resume costs one comparison.
            behaviour.enabled = true;
            behaviour.StartBehaviour();

            LogTreeState(enemy, behaviour, "resume:StartBehaviour");
        }

        /// <summary>
        /// TEMPORARY (diagnostics): reports the state of an enemy's behaviour tree, but only when it changes,
        /// so a method called twenty times a second costs a handful of lines per session instead of thousands.
        ///
        /// It exists to settle one question the logs cannot answer today: whether a frozen enemy is frozen
        /// because its tree is stopped, or because the game itself is holding it still. Remove with the rest
        /// of the probe.
        /// </summary>
        private static void LogTreeState(Enemy enemy, EnemyBehaviour behaviour, string esito)
        {
            // NOTE: behaviour.BehaviourTree is NOT read here, and must not be.
            //
            // Its getter is not public - verified in IL, "prop BehaviourTree: getter pubblico = False" - and
            // Mono enforces that at RUNTIME, unlike the C# compiler, which accepted the call. So the first
            // version of this line threw on EVERY call: 479 exceptions in one log and 666 in the other, all
            // from ResumeEnemy inside UpdateCore, which is the hot path. The exception aborted UpdateCore
            // mid-way and the two machines went out of sync. A diagnostic that breaks the thing it measures.
            //
            // isRunning is what we actually wanted, so it needs reflection (the backing field
            // "<BehaviourTree>k__BackingField" is private too). Not done yet: measuring it is worth less than
            // not crashing, and what this line CAN report without touching that property - enabled, and
            // whether ResumeEnemy took the early return - is already the decisive half of the question.
            string id = IdOf(enemy);
            if (string.IsNullOrEmpty(id))
                return;

            bool enabled = behaviour != null && behaviour.enabled;

            // isRunning is read reflectively, and the failure is swallowed on purpose.
            //
            // The property cannot be touched directly: its getter is not public, which Mono enforces at
            // runtime even though the compiler accepted the call. Reflection can still reach it, but if it
            // throws again the diagnostic must NOT take the sync down with it - that is exactly what the
            // direct call did. A measurement is never worth breaking the thing being measured.
            string running = ReadTreeRunning(behaviour);

            // THE REASON IS NOT PART OF THE STATE, and putting it in the key is a defect I have now made
            // twice. Deduplication has to answer "is this enemy in a different state than when I last
            // reported it", and the answer cannot depend on WHO asked.
            //
            // Two call sites pause the same enemy - the send loop every tick and the packet path as each
            // update arrives - and they pass different reasons. With the reason in the key the value
            // alternated "?|remote owns|?|remote owns" forever, so every call looked like a change and the
            // dedup was dead: measured in a real session, 1140 lines of [EnemyTree] out of 2137 total, with
            // enabled and isRunning IDENTICAL on both halves of every pair. The state never changed at all.
            //
            // Now only the state decides, and the reason is reported as what it is: context for the change
            // that was actually observed.
            // GOTPARRY IS PART OF THE STATE, and it is the reason this field exists.
            //
            // A WheelCarrier has NO behaviour tree - its AI is its own private Update() - and that Update
            // returns at its third instruction when GotParry is true: it does not chase, does not attack, and
            // does not even call Idle(), so the animator simply stays on its last state and the enemy looks
            // ASLEEP. Nothing in the other fields says so: for a Wheel isRunning is null (there is no tree to
            // run - see ReadTreeRunning) and enabled stays true. Without this field a latched GotParry is
            // invisible in the log.
            //
            // And the latch is the whole point: GotParry is cleared only by WheelCarrier.Damage(Hit) or by
            // WheelCarrierBehaviour.Alive(), which has NO caller in C# anywhere in the assembly - it is an
            // animation event of the clip. So if the parry/execution reaction never plays to its end, the flag
            // stays true for the rest of the session, and the mod can neither read it nor clear it.
            //
            // Unlike BehaviourTree, this one IS a public property (verified in IL: getter and setter both
            // public), so it is read directly and NOT through ReadTreeRunning.
            bool gotParry = behaviour != null && behaviour.GotParry;

            string key = enabled + "|" + running + "|" + gotParry;

            string previous;
            if (_treeStateLogged.TryGetValue(id, out previous) && previous == key)
                return;

            _treeStateLogged[id] = key;

            Vector3 p = enemy.transform.position;

            // sprite and collider are reported because they are how the "hidden but present" family shows
            // itself, and reading them beats deducing them (voice 53). For the WaxCrawler - the mud crawlers
            // among them - the enemy is never inactive: the reveal turns SpriteRenderer.enabled on, sets the
            // Animator to speed 1 and switches the damage collider on. Without these two fields a log cannot
            // tell a revealed enemy from a hidden one, and the whole family has to be inferred from the
            // animation clips instead of read.
            SpriteRenderer sprite = enemy.GetComponentInChildren<SpriteRenderer>();
            Collider2D damageCollider = enemy.EntityDamageArea == null
                ? null
                : enemy.EntityDamageArea.DamageAreaCollider;

            ModLog.Info($"[EnemyTree] {FullId(id)} enabled={enabled} isRunning={running} " +
                        $"gotParry={gotParry} esito={esito} " +
                        $"sprite={(sprite == null ? "n/d" : sprite.enabled.ToString())} " +
                        $"collider={(damageCollider == null ? "n/d" : damageCollider.enabled.ToString())} " +
                        $"pos=({p.x:F1},{p.y:F1}) vita={enemy.CurrentLife:F0} " +
                        $"mia={(IsHost ? "host" : "client")}");
        }

        /// <summary>
        /// Reads BehaviourTree.isRunning reflectively, returning "?" if it cannot be read.
        ///
        /// Reflection replaces a direct call that crashed the sync: the getter is not public, and Mono
        /// refuses the call at runtime even though it compiles. Everything here is wrapped, and the answer is
        /// cached once it is known to work or to fail - a diagnostic that tries and fails twenty times a
        /// second is its own problem.
        /// </summary>
        private static string ReadTreeRunning(EnemyBehaviour behaviour)
        {
            if (behaviour == null)
                return "none";

            if (_treeRunningUnavailable)
                return "unavailable";

            try
            {
                // Resolved once and kept: GetProperty is a lookup by string and this runs from the hot path,
                // so repeating it twenty times a second per enemy would be paid for nothing. Only the
                // resolution is cached - a null result means the member genuinely does not exist and the
                // flag below stops the whole diagnostic.
                if (_behaviourTreeProperty == null)
                {
                    _behaviourTreeProperty = typeof(EnemyBehaviour).GetProperty(
                        "BehaviourTree",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (_behaviourTreeProperty == null)
                    {
                        _treeRunningUnavailable = true;
                        return "unavailable";
                    }
                }

                object tree = _behaviourTreeProperty.GetValue(behaviour, null);
                if (tree == null)
                    return "null";

                if (_isRunningProperty == null)
                {
                    _isRunningProperty = tree.GetType().GetProperty("isRunning");
                    if (_isRunningProperty == null)
                    {
                        _treeRunningUnavailable = true;
                        return "unavailable";
                    }
                }

                object value = _isRunningProperty.GetValue(tree, null);
                return value is bool && (bool)value ? "True" : "False";
            }
            catch (Exception e)
            {
                // Given up on for the rest of the process, and said out loud once. A transient failure that
                // kept retrying twenty times a second would be its own problem; one that silently switched the
                // diagnostic off would be worse, so the message stays.
                _treeRunningUnavailable = true;
                ModLog.Error("[EnemyTree] isRunning non leggibile (" + e.GetType().Name + "): " + e.Message +
                             " - non ritento piu' per questa sessione");
                return "unavailable";
            }
        }

        /// <summary>Set once reflection has failed, so it is not retried on every call.</summary>
        private static bool _treeRunningUnavailable;

        /// <summary>Resolved once and reused: GetProperty is a string lookup and this runs on the hot path.</summary>
        private static PropertyInfo _behaviourTreeProperty;
        private static PropertyInfo _isRunningProperty;

        /// <summary>Last logged tree state per enemy, so only changes are reported. Diagnostics only.</summary>
        private static readonly Dictionary<string, string> _treeStateLogged = new Dictionary<string, string>();

        /// <summary>
        /// Last logged decision of the dead-owner branch of DecideOwnership (fix ③) per enemy, so the
        /// [Proprieta'] marker is written when the DECISION changes and not on every evaluation.
        /// Diagnostics only - see LogDeadOwnerDecision for why the reason is not part of the key.
        /// </summary>
        private static readonly Dictionary<string, string> _deadReleaseLogged = new Dictionary<string, string>();

        /// <summary>
        /// Gives the enemy its own physics back, mirroring FreezePhysics.
        ///
        /// The gravity scale is restored from the value saved when the enemy was frozen, never assumed:
        /// see _frozenGravityScale for why 1 would be the wrong guess.
        ///
        /// The restore must NOT be skipped when the controller happens to be enabled already. It usually is:
        /// the game's own &lt;Enemy&gt;.OnUpdate calls EnablePhysics(true) on several enemy types, which turns the
        /// controller back on and sets GravityScale itself. Returning early on "already enabled" would then
        /// leave GravityScale at whatever the game last put there and - worse - leave the saved value in
        /// _frozenGravityScale for ever. The controller being on is not evidence that nothing needs undoing.
        /// </summary>
        private static void ThawPhysics(Enemy enemy)
        {
            // ⭐ FIX ① (il rovescio, simmetrico): il boss che questa macchina torna a simulare deve essere
            // solido ESATTAMENTE come lo aveva lasciato. Prima della fisica, perche' e' la stessa coppia di
            // FreezePhysics e perche' la barriera non dipende dal controller.
            ThawCollision(enemy);

            var controller = enemy.Controller;
            if (controller == null)
                return;

            var physics = controller.PlatformCharacterPhysics;
            if (physics != null)
            {
                float original;
                if (_frozenGravityScale.TryGetValue(enemy.GetInstanceID(), out original))
                    physics.GravityScale = original;

                _frozenGravityScale.Remove(enemy.GetInstanceID());
            }

            controller.enabled = true;
        }

        /// <summary>
        /// ⭐ FIX ① — IL ROVESCIO DI `FreezeCollision`: restituisce al boss i collisori d'ostacolo che gli
        /// erano stati tolti, con i valori LETTI allora (vedi `FrozenCollision`), non con un "riaccendi".
        ///
        /// Si restituisce solo se c'e' una fotografia: se il boss non e' mai diventato pupazzo qui, o se il
        /// risveglio arriva due volte, questo metodo non tocca niente. E se l'oggetto e' stato distrutto nel
        /// frattempo, Unity lo confronta come null e la scrittura viene saltata invece di lanciare.
        /// </summary>
        private static void ThawCollision(Enemy enemy)
        {
            try
            {
                if (enemy == null || enemy.gameObject == null)
                    return;

                int key = enemy.GetInstanceID();

                FrozenCollision saved;
                if (!_frozenCollision.TryGetValue(key, out saved))
                    return;

                _frozenCollision.Remove(key);

                if (saved.Smart != null)
                {
                    saved.Smart.enabled = saved.SmartEnabled;
                    saved.Smart.EnableCollision2D = saved.SmartCollision2D;
                }

                // Se la barriera era gia' spenta quando il boss e' diventato pupazzo, resta spenta: e' un
                // oggetto che appartiene alle clip, e riaccenderlo sarebbe una decisione presa al posto loro.
                if (saved.Barrier != null && saved.BarrierActive)
                    saved.Barrier.SetActive(true);

                ModLog.Info($"[BossSolido] {IdOf(enemy)} boss di nuovo solido " +
                            $"(corpo={(saved.Smart == null ? "n/d" : "ripristinato")} " +
                            $"enabled:{saved.SmartEnabled}/EnableCollision2D:{saved.SmartCollision2D} " +
                            $"barriera={(saved.Barrier == null ? "n/d" : (saved.BarrierActive ? "riaccesa" : "lasciata spenta"))})");
            }
            catch (Exception e)
            {
                ModLog.Error($"[BossSolido] ripristino dei collisori d'ostacolo fallito: " +
                             $"{e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>
        /// Begins a scene. Called from Multiplayer.OnLevelLoaded.
        ///
        /// This must NOT register or finalise anything. In game the ordering is:
        ///   1. the game calls EnemySpawner.SpawnEnemiesOnLoad()
        ///   2. that fires OnEnemiesSpawned(), which builds the id table
        ///   3. only afterwards does the mod's OnLevelLoaded run, with the real scene name
        /// Registering here produced ids carrying an empty scene name, which this method then threw
        /// away while resetting _scene - and every later rescan, using the real name, minted a fresh id
        /// for the same enemy. That is exactly how 7 enemies became 56 ids.
        ///
        /// It is also what made the two machines disagree: they differ in WHEN each one learns the scene
        /// name relative to the spawn, so the id sets built here diverged and the peer rejected every
        /// index as out of range.
        /// </summary>
        /// <summary>
        /// Gives back every enemy this machine owns, because it is about to leave the scene.
        ///
        /// THE BUG THIS CLOSES. Ownership is scene memory by construction: LevelLoaded wipes _ids,
        /// _lastSent and _puppetEnemies on whichever machine changes scene, with the note that ids do not
        /// survive a scene change. So the machine that MOVES forgets, and the one that STAYS does not - and
        /// that asymmetry left a hole. The host walks into a room, sees the enemies, takes the ones it is
        /// closer to and publishes a token; then it walks out. Its own list no longer contains them, so it
        /// can no longer arbitrate them and never publishes the hand-back - and the peer is left obeying a
        /// token that will never be superseded. On the peer the self-heal only fires while OwnershipKnown is
        /// false, and the token had set it true, so the enemy stayed a paused puppet for the rest of the
        /// session: it did not walk, did not attack, did not even get pushed around, because FreezePhysics
        /// switches the controller off.
        ///
        /// WHY HERE AND NOT AT THE PEER. The peer could reclaim on ReceiveLeaveScene, and that would be
        /// simpler - but that packet carries no name, so with three or more players it cannot say WHO left,
        /// and the peer would have to reclaim from everyone at once. Here the machine leaving knows exactly
        /// what it owned, in which scene, and who is present.
        ///
        /// MUST BE CALLED BEFORE SendLeaveScene. The server filters ownership by the SENDER's scene -
        /// receivePlayerLeaveScene sets it to the empty string - so a token queued after that line is never
        /// delivered, and one queued after entering the new scene is delivered to the wrong room. The order
        /// is the whole fix; sending late is indistinguishable from not sending.
        ///
        /// IT DOES NOT RESOLVE THE ENEMY, deliberately. At OnBeforeLevelLoad the old scene is still loaded, so
        /// the handle could be read from the instance - but reading it from the id means this works even for
        /// an enemy that is already gone, and a token for an enemy the receiver cannot resolve is harmless:
        /// it sets the flag, fails to find the object and returns.
        /// </summary>
        public static void ReleaseOwnedEnemiesBeforeLeaving()
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            // Only the arbiter publishes ownership; a peer obeys, and its OwnedByLocal reflects a token the
            // host sent rather than a decision of its own.
            if (!IsHost)
                return;

            int released = 0;
            foreach (KeyValuePair<string, EnemyState> entry in _lastSent)
            {
                if (!entry.Value.OwnedByLocal)
                    continue;

                // Latch the publication, exactly as the send loop does, so that if this machine comes back
                // to the scene before LevelLoaded clears the table it does not publish the same hand-back
                // twice and does not think the peer was told something it was not.
                entry.Value.OwnershipKnown = true;
                entry.Value.LastPublishedOwnerIsMe = false;

                Main.Multiplayer.SendEnemyOwnership(HandleOf(entry.Key), true);
                released++;
            }

            ModLog.Info($"[Proprieta''] uscendo dalla scena ho restituito {released} nemici sui " +
                        $"{_lastSent.Count} che avevo in memoria (scena={_scene})");
        }

        public static void LevelLoaded(string scene)
        {
            _scene = scene;
            _spawnFinalized = false;

            _enemies.Clear();
            _spawnPoints.Clear();
            _registered.Clear();
            _ids.Clear();
            _lastSent.Clear();

            // The instances of the scene being unloaded. Their ids must not be reused by the next scene's
            // enemies, and Unity compares destroyed objects as null so stale keys could never match anyway.
            _enemyIds.Clear();
            // _remoteTargets is deliberately NOT cleared. The ids are qualified by SCENE
            // ("sp|D01Z01S03|SPAWN|x|y"), so entries from another room can never collide with this one's -
            // and keeping them is what makes a RE-ENTRY safe: the next visit to the same room still has the
            // positions the other player last reported, so a take adopts those instead of falling back to
            // the enemy's spawn copy. Clearing it here was exactly what turned a re-entry into a reset.
            // Stale ids resolve to null and are skipped, so they cost a dictionary entry and nothing else.
            _lastAnim.Clear(); // animation state belongs to the scene being unloaded
            _appliedAnim.Clear();
            _appliedAnimPrev.Clear();    // ditto: keyed by id, and ids do not survive a scene change
            _lastAnimSample.Clear();     // ditto
            _lastAnimChangeAt.Clear();   // ditto: the clock belongs to the scene being unloaded
            _treeStateLogged.Clear();    // diagnostics: keyed by id, meaningless in the next scene
            _deadReleaseLogged.Clear();  // ditto: keyed by id, and ids do not survive a scene change
            _bossOwnLogged.Clear();      // ditto: [BossProp] e' per id, e l'id di un boss non sopravvive
            _bossLifeZeroLogged.Clear(); // ditto: [BossLife] "vita 0 dal padrone" e' per id
            _frozenGravityScale.Clear(); // keyed by instance id: meaningless once the objects are gone
            _frozenCollision.Clear();    // FIX ①: ditto - la fotografia dei collisori muore con l'oggetto
            _bossIds.Clear();            // keyed by id, and ids do not survive a scene change
            _bossKillsApplied.Clear();   // M1: ditto - a boss of the next scene must be killable again
            _bossDeathsApplied.Clear();  // A: ditto - the death of the next scene's boss must be playable again
            // PUNTO 0: una VISITA nuova. Non si svuota `_remoteTargets` (di proposito, :2326-2331), ma le sue
            // voci invecchiano: `_remoteTargetsVisit` dice quali sono state rinfrescate in QUESTA visita, e
            // per un boss una voce vecchia non si applica (vedi `SkipBossFollow`). Una visita nuova deve
            // anche poter riparlare: il marker `[Insegue]` e' deduplicato per id.
            _sceneVisit++;
            _bossFollowSkipLogged.Clear();
            _hiddenBossCorpses.Clear();  // C: ditto - the instances just unloaded must not be remembered
            _deathPendingSince.Clear();  // ditto: the clock belongs to enemies of the scene being unloaded
            _puppetEnemies.Clear();      // the instances of the scene being unloaded
            _orientationLabels.Clear();  // ditto: keyed by instance id

            // TEMPORARY (PerformanceProbe): re-read the Animator names for the new scene. Remove with
            // the probe.
            PerformanceProbe.ResetForScene();
            _killLogged.Clear();
            _reportedResolveFailures.Clear();

            RemoveJunkDeathFlags();

            _sendTimer = 0f;
            _rescanTimer = 0f;
            _lastSweep = 0f;

            // ② Lo stato della bossfight si rilegge SUBITO per la scena nuova, e stampa una riga anche se
            // non cambia. Senza questa riga un `OFF` non comparirebbe mai nel log e non si potrebbe
            // distinguere "in questa stanza non c'e' nessuna fight" da "il latch non sta girando" - che
            // sono due cose diverse e la seconda sarebbe invisibile.
            _lastBossFightStateCheck = 0f;
            _bossFightStateArmed = true;

            // And the diagnostic log with them, so the first snapshot of a new scene is written immediately
            // instead of waiting out the interval. The scene just changed: that is the moment worth
            // describing, and the one where a stale timer would show the least interesting frame.
            _lastDiagnosticLog = 0f;

            _updatesSent = 0;
            _updatesReceived = 0;
            _damageSent = 0;
            _damageApplied = 0;
            _killsSent = 0;
            _registrations = 0;
            _duplicateIds = 0;
            _listFingerprint = 0;
            _fingerprintMismatchReported = 0f;
            _fingerprintMismatches = 0;
            _idsDirty = false;
            _countedDuplicates.Clear();

            ModLog.Info($"[EnemySync] Scene {scene}: level loaded (IsHost: {IsHost})");
        }

        /// <summary>
        /// Cheap order-sensitive hash of the id list. Both machines must produce the same value, because
        /// the index into this list is the wire handle: if the lists differ, updates get applied to the
        /// wrong enemy. The host sends it with every update so a divergence is reported instead of
        /// silently corrupting the scene.
        ///
        /// It has to be recomputed whenever the list it describes changes, and the first version only
        /// recomputed it when the whole table was rebuilt. The list is also appended to and pruned
        /// afterwards, so the published fingerprint described a list that no longer existed - and a check
        /// comparing a stale value against an honest one reports a divergence that is not there. That is
        /// what "IMPRONTA DIVERSA" turned out to be: the two machines agreed on the enemies, while the
        /// numbers disagreed about a list that had since changed. A false alarm is worse than no alarm,
        /// because it hides the real one.
        /// </summary>
        private static int ComputeFingerprint()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < _ids.Count; i++)
                {
                    hash = hash * 31 + _ids[i].GetHashCode();
                    hash = hash * 31 + i;
                }
                return hash;
            }
        }

        /// <summary>
        /// Brings the published fingerprint back in line with the id list, if anything changed it.
        ///
        /// Also re-arms the divergence report, because the first version fired it only once per level load -
        /// so a mismatch caught while the list was mid-change would both be wrong and silence every later,
        /// real one. Re-arming is rate limited in ReceiveEnemyUpdate rather than here, so a genuine
        /// divergence keeps being reported without flooding the log on every scan.
        /// </summary>
        private static void RefreshFingerprintIfDirty()
        {
            if (!_idsDirty)
                return;

            _idsDirty = false;
            _listFingerprint = ComputeFingerprint();
            _fingerprintMismatchReported = 0f;
        }

        /// <summary>
        /// The single point that finalises the id table: registers everything, sorts, and fingerprints.
        /// Called right after the game spawns enemies (EnemySpawner.SpawnEnemiesOnLoad), which is the
        /// first moment enemies exist and the first moment the scene name is meaningful.
        ///
        /// Because this is the only place that builds the table, both machines build it from the same
        /// state, which is what makes the indices agree.
        /// </summary>
        public static void OnEnemiesSpawned()
        {
            _registered.Clear();
            _ids.Clear();
            _enemies.Clear();
            _lastSent.Clear();
            _registrations = 0;

            // ⭐ MARKER [BossView], PRIMA META': la scena com'e' ADESSO, nello stesso frame dello spawn e
            // PRIMA che la mod registri qualunque cosa. Sta qui e non altrove perche' questo e' l'unico
            // istante in cui l'oggetto del boss e' ancora come la scena l'ha messo: `DestroyDeadEnemy`
            // (chiamato da `RegisterEnemy`, nel ramo `_pendingDeaths.Contains(id)`) lo spegne, e un frame
            // dopo la misura non direbbe piu' niente.
            //
            // LA RAGIONE (studio: scratch\STUDIO-PUNTO-0-RIENTRO.md §5, marker 4): serve a sapere se al
            // RIENTRO in bossroom il boss e' SPENTO, ASSENTE o VIVO, e se e' la mod a spegnerlo. Non e'
            // deducibile dal repo: la condizione che sopprime l'ingresso e' una flag del gioco
            // (`*_BOSSDEAD`) letta da una FSM dentro l'asset della scena, che il repo non contiene. Questa
            // riga la rende decidibile in gioco.
            //
            // COME SI LEGGE, insieme alla riga gemella sotto e al `Post-spawn` che segue:
            //   attivo>0 e bossVivi=1                 => il boss e' VIVO in scena;
            //   statusDead>0 e bossVivi=0             => trovato attivo ma GIA' morto per il gioco: la mod
            //                                            lo sta per togliere (e' lei, non la scena);
            //   attivo=0 e `Post-spawn: 1 enemies`    => c'e' e SPENTO (esiste, non lo simula nessuno);
            //   attivo=0 e `Post-spawn: 0 enemies`    => ASSENTE (distrutto: non c'e' piu').
            LogBossViewBefore();

            // ⭐ C) IL CADAVERE DEL BOSS NON DEVE RESTARE VISIBILE AL RIENTRO.
            //
            // Sta ESATTAMENTE FRA I DUE MARKER, che e' il posto giusto per la semantica che i marker hanno:
            // la riga `[BossView]` sopra e' la scena come la lascia il gioco, quella sotto e' la scena dopo
            // le azioni della mod. Uno `SetActive(false)` fatto qui e' un'azione della mod come le altre, e la
            // riga `[BossCorpse]` appena sotto dice che e' stata questa e non `RegisterEnemy`.
            //
            // PRIMA DI `RegisterEnemies`, di proposito: il boss riconosciuto come cadavere deve essere gia'
            // spento quando la registrazione lo incontra, e `RegisterEnemy` lo salta per nome (`_hiddenBossCorpses`)
            // - cosi' la mod non gli riaccende la tree e non si ritrova un boss invisibile che combatte.
            HideDefeatedBosses(_scene, "ingresso in scena");

            RegisterEnemies(_scene);

            // ⭐ MARKER [BossView], SECONDA META': IDENTICA MISURA, nello stesso frame, SUBITO DOPO la
            // registrazione. La DIFFERENZA fra questa riga e quella sopra e' la risposta a "e' la mod a
            // spegnere il boss?": se `attivi` scende mentre `pendingMorti` non cambia, a spegnerlo e' stato
            // `RegisterEnemy` -> `DestroyDeadEnemy`, cioe' NOI; se non scende, a spegnerlo (o a non
            // accenderlo) e' stata la scena. Vedi LogBossViewAfter.
            LogBossViewAfter();

            _ids.Sort(string.CompareOrdinal);
            _listFingerprint = ComputeFingerprint();
            _idsDirty = false;
            _spawnFinalized = true;

            ModLog.Info($"[EnemySync] Post-spawn: {_enemies.Count} enemies / {_spawnPoints.Count} spawn points / {_ids.Count} ids");
        }

        // ============================================================================================
        // MARKER [BossView] / [BossJoin] — SOLO LOG, ZERO COMPORTAMENTO, ZERO PATCH
        // (studio che li chiede: scratch\STUDIO-PUNTO-0-RIENTRO.md §5; referto:
        //  scratch\REFERTO-MARKER-PUNTO-0.md)
        //
        // Questi metodi NON leggono niente che non fosse gia' letto e NON scrivono NIENTE: contano e
        // stampano. Se il gioco fa qualcosa di diverso dopo questo commit, e' un errore.
        //
        // PERCHE' NON SONO COSTOSI: girano solo dentro `OnEnemiesSpawned()` (una volta per spawn, non per
        // frame) e in un singolo frame dopo ogni `LevelLoaded`. Niente e' stato messo in `Update()`.
        //
        // PERCHE' NON LANCIANO MAI: un marker che esplode e zittisce la misura e' peggio di un marker
        // assente, quindi ogni riga ha un `try/catch` che, in caso di errore, STAMPA LO STESSO MARKER con
        // `?` al posto dei numeri (fallback) e prosegue. Nessuna eccezione esce da qui.
        // ============================================================================================

        /// <summary>
        /// [BossView] riga 1 — `scena · attivo · statusDead · pendingMorti · bossVivi`, PRIMA della
        /// registrazione. Vedi il commento nel punto di chiamata per come si legge.
        /// </summary>
        private static void LogBossViewBefore()
        {
            try
            {
                int attivo;
                int statusDead;
                int bossVivi;
                CountSceneEnemies(out attivo, out statusDead, out bossVivi);

                ModLog.Info($"[BossView] scena={SceneForMarker()} · attivo={attivo} · statusDead={statusDead} · " +
                            $"pendingMorti={_pendingDeaths.Count} · bossVivi={bossVivi}");
            }
            catch (Exception e)
            {
                ModLog.Info($"[BossView] scena={SceneForMarker()} · attivo=? · statusDead=? · pendingMorti=? · " +
                            $"bossVivi=? (lettura fallita: {e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// [BossView] riga 2 (gemella, stesso frame) — `dopo-registrazione · attivi · pendingMorti`.
        /// </summary>
        private static void LogBossViewAfter()
        {
            try
            {
                int attivo;
                int statusDead;
                int bossVivi;
                CountSceneEnemies(out attivo, out statusDead, out bossVivi);

                ModLog.Info($"[BossView] dopo-registrazione · attivi={attivo} · pendingMorti={_pendingDeaths.Count}");
            }
            catch (Exception e)
            {
                ModLog.Info($"[BossView] dopo-registrazione · attivi=? · pendingMorti=? " +
                            $"(lettura fallita: {e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// I tre numeri del marker, in una passata sola.
        ///
        /// `attivo`     = quanti `Enemy` sono ATTIVI in scena (`activeInHierarchy`), non quanti esistono:
        /// `FindAllEnemiesIncludingInactive` trova anche i disattivati, e un boss spento NON deve contare
        /// qui, altrimenti "spento" e "vivo" darebbero lo stesso numero. Quanti ESISTONO lo dice la riga
        /// `[EnemySync] Post-spawn: N enemies` che segue nello stesso frame.
        /// `statusDead` = quanti di quelli ATTIVI hanno `Status.Dead == true` (la sequenza di morte e'
        /// partita: e' la stessa nozione usata altrove in questo file, non "la vita e' zero").
        /// `bossVivi`   = quanti di quelli ATTIVI sono boss (namespace `Gameplay.GameControllers.Bosses`,
        /// lo stesso criterio di `IsBoss`) e NON morti.
        /// </summary>
        private static void CountSceneEnemies(out int attivo, out int statusDead, out int bossVivi)
        {
            attivo = 0;
            statusDead = 0;
            bossVivi = 0;

            Enemy[] enemies = FindAllEnemiesIncludingInactive();
            if (enemies == null)
                return;

            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null || enemy.gameObject == null)
                    continue;

                if (!enemy.gameObject.activeInHierarchy)
                    continue; // esiste ma e' spento: lo conta il Post-spawn, non questo marker

                attivo++;

                if (enemy.Status != null && enemy.Status.Dead)
                {
                    statusDead++;
                    continue;
                }

                if (IsBossByNamespace(enemy))
                    bossVivi++;
            }
        }

        /// <summary>
        /// Lo STESSO test di `IsBoss` (il namespace del tipo), ma senza id e senza toccare la cache
        /// `_bossIds`: il marker deve leggere lo stato e NON lasciare traccia in nessuna tabella della mod.
        /// </summary>
        private static bool IsBossByNamespace(Enemy enemy)
        {
            string space = enemy.GetType().Namespace;
            return !string.IsNullOrEmpty(space) &&
                   space.StartsWith("Gameplay.GameControllers.Bosses", System.StringComparison.Ordinal);
        }

        /// <summary>Nome della scena per i marker, con il ripiego sul livello corrente se `_scene` e' vuota.</summary>
        private static string SceneForMarker()
        {
            try
            {
                if (!string.IsNullOrEmpty(_scene))
                    return _scene;

                return Core.LevelManager.currentLevel == null
                    ? "?"
                    : Core.LevelManager.currentLevel.LevelName;
            }
            catch (Exception)
            {
                // Anche il nome della scena ha un ripiego: il marker deve stampare comunque.
                return "?";
            }
        }

        /// <summary>[BossJoin] — quanti boss VIVI ci sono in scena (marker 2, un frame dopo il load).</summary>
        internal static int CountLiveBossesInScene()
        {
            int attivo;
            int statusDead;
            int bossVivi;
            CountSceneEnemies(out attivo, out statusDead, out bossVivi);
            return bossVivi;
        }

        /// <summary>
        /// [BossJoin] — il PREDICATO CHE ESISTE GIA' (`OtherPlayerInMyScene`, :2493), esposto all'unico
        /// chiamante che ne ha bisogno fuori da questo file: il marker in `Multiplayer.OnLevelLoaded`.
        /// Nessuna logica nuova: e' un inoltro di una riga.
        /// </summary>
        internal static bool AnyOtherPlayerInMyScene()
        {
            return OtherPlayerInMyScene();
        }

        /// <summary>
        /// [BossJoin] — il secondo numero di `peerInScena=N/TOT`: quanti peer sono connessi in tutto.
        /// </summary>
        internal static int ConnectedPlayersCount()
        {
            try
            {
                if (Main.Multiplayer == null || Main.Multiplayer.OtherPlayerManager == null)
                    return 0;

                return Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // ============================================================================================
        // ② IL TERZO VALORE DELLO STATO:  BOSSFIGHT|<scena> = OFF | IN_CORSO | FINITA
        // (referto: scratch\REFERTO-PROPRIETA-BOSS.md; studio: scratch\STUDIO-VIA-BOSS-E-TRIGGER.md §3.3)
        // ============================================================================================
        //
        // PERCHE' MANCAVA, E PERCHE' ADESSO C'E'. La mod conosceva due fatti e nessuna parola per
        // metterli insieme: "c'e' un boss VIVO in questa stanza" (`CountLiveBossesInScene()`) e "un altro
        // giocatore e' QUI" (`AnyOtherPlayerInMyScene()`). Quello che non esisteva in NESSUN punto del
        // codice era la frase "la fight e' **IN_CORSO**": zero occorrenze di `BossFightManager`,
        // `IsFightStarted` o `onStartBossFight` nel codice della mod. Le due informazioni c'erano e non
        // venivano usate, ed e' la ragione per cui il nuovo arrivato non sapeva di essere arrivato a meta'
        // (misurato: su G1 il predicato era vero al primo frame utile, `peerInScena=1/1 · bossVivi=1`,
        // ~110 righe e ~18 s PRIMA che partisse la cerimonia).
        //
        // ⛔ SOLO LOG E MEMORIA. Questo latch non cambia un solo comportamento: non accende niente, non
        // spegne niente, non sopprime la cerimonia (che e' vietato toccare: e' una FSM dentro l'asset di
        // scena, l'unica cosa che puo' bloccare la stanza) e non manda un solo pacchetto. Serve alla voce
        // ② della feature, dove il gate va messo SULLA MACCHINA CHE ENTRA, non su quella che combatte.
        //
        // COSA SIGNIFICA IN_CORSO, DETTO PER ESTESO. La regola e':
        //     IN_CORSO  <=  bossVivi > 0  E  ( peerInScena  O  il boss ha gia' perso vita )
        // Il secondo pezzo e' il testimone che il gioco non ci da' per contratto: senza patch non
        // esiste un modo per leggere `IsFightStarted` (e' uno stato di ISTANZA di un componente di
        // scena), quindi "la fight e' cominciata" si legge dal solo fatto osservabile - il boss ha
        // perso vita. La vita e' l'unico segnale che la mod riceve GIA' (arriva in ogni update, e i
        // `[Danno] relay` la fanno scendere anche sulla macchina che non ha menato).
        // ⚠️ MA SULLA MACCHINA CHE ENTRA il testimone non c'e' ancora (la sua copia del boss e' a vita
        // piena), e la macchina che entra e' esattamente quella a cui lo stato serve: per questo vale
        // anche `peerInScena`. Il prezzo e' dichiarato e onesto: con un altro giocatore in stanza e un
        // boss DORMIENTE (pre-trigger) il latch dice IN_CORSO senza che qualcuno stia combattendo. Per
        // questo la riga di log stampa SEMPRE anche `bossToccato` e `peerInScena`: chi legge vede su
        // quale delle due meta' la decisione si fonda, e non deve fidarsi della parola.
        private enum BossFightState
        {
            Off = 0,
            InCorso = 1,
            Finita = 2,
        }

        /// <summary>
        /// Lo stato per SCENA, e non per macchina: la stessa stanza puo' essere visitata piu' volte nella
        /// stessa sessione e il valore deve sopravvivere al viaggio (e' proprio al RIENTRO che serve).
        /// Non viene svuotato da `LevelLoaded`, per la stessa ragione per cui non viene svuotata
        /// `_remoteTargets` (:2079-2084).
        /// </summary>
        private static readonly Dictionary<string, BossFightState> _bossFightByScene =
            new Dictionary<string, BossFightState>();

        /// <summary>
        /// Ogni quanto si rilegge lo stato. NON a ogni frame: `CountLiveBossesInScene()` percorre i root
        /// della scena con `GetComponentsInChildren` e questo latch e' un marker, non una regola di gioco.
        /// 2 s e' lo stesso passo con cui la sonda di performance misura il mondo (`PerformanceProbe`,
        /// `REPORT_INTERVAL = 2f`), quindi i due log si possono affiancare riga per riga.
        /// </summary>
        private const float BOSSFIGHT_STATE_INTERVAL = 2f;

        private static float _lastBossFightStateCheck;

        /// <summary>Forza UNA riga per caricamento anche quando lo stato non cambia (vedi `UpdateBossFightState`).</summary>
        private static bool _bossFightStateArmed;

        /// <summary>
        /// Il valore corrente per una scena, per il resto della feature: `OFF`, `IN_CORSO`, `FINITA`
        /// (o `?` se non e' leggibile). Non esiste nessun altro modo di leggere questo stato, ed e'
        /// voluto: un solo nome, un solo significato.
        /// </summary>
        internal static string BossFightStateOf(string scene)
        {
            try
            {
                if (string.IsNullOrEmpty(scene))
                    return "?";

                BossFightState state;
                if (!_bossFightByScene.TryGetValue(scene, out state))
                    return "OFF";

                return BossFightStateName(state);
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string BossFightStateName(BossFightState state)
        {
            switch (state)
            {
                case BossFightState.InCorso: return "IN_CORSO";
                case BossFightState.Finita: return "FINITA";
                default: return "OFF";
            }
        }

        /// <summary>
        /// `true` quando un boss VIVO e ATTIVO di questa scena ha gia' perso vita: e' il testimone che la
        /// fight e' cominciata. Letto dalle tabelle della mod (`_ids`), non da una scansione della scena:
        /// gli stessi nemici che l'arbitro considera, senza un `GetComponentsInChildren` in piu'.
        /// </summary>
        private static bool BossAlreadyEngaged()
        {
            try
            {
                for (int i = 0; i < _ids.Count; i++)
                {
                    Enemy enemy = ResolveEnemy(_ids[i]);
                    if (enemy == null || enemy.gameObject == null)
                        continue;

                    if (!enemy.gameObject.activeInHierarchy)
                        continue; // spento: non sta combattendo (vedi CountSceneEnemies, :2333)

                    if (enemy.Status != null && enemy.Status.Dead)
                        continue; // la sequenza di morte e' partita: non e' "in corso", e' finita

                    if (!IsBossByNamespace(enemy))
                        continue;

                    float maxLife = enemy.Stats != null && enemy.Stats.Life != null
                        ? enemy.Stats.Life.Final
                        : 0f;

                    // La tolleranza e' di mezzo punto: `SetHealth` e l'aritmetica dei colpi lasciano
                    // differenze di frazioni, e un boss a 399.9999 su 400 non e' stato colpito.
                    if (maxLife > 0f && enemy.CurrentLife < maxLife - 0.5f)
                        return true;
                }
            }
            catch (Exception)
            {
                // Un testimone che non si puo' leggere vale "non lo so": si resta su quello che si sa.
            }

            return false;
        }

        /// <summary>
        /// Rilegge lo stato della bossfight di QUESTA scena e lo latcha, scrivendo una riga SOLO quando
        /// cambia (piu' una riga per caricamento, armata da `LevelLoaded`).
        ///
        /// LA REGOLA, in ordine:
        ///   IN_CORSO  se c'e' almeno un boss vivo E ( c'e' un altro giocatore in scena OPPURE il boss ha
        ///             gia' perso vita ) -- vedi il commento del blocco per cosa compra e cosa non compra;
        ///   FINITA    se NON c'e' piu' nessun boss vivo e lo stato era IN_CORSO;
        ///   altrimenti lo stato NON CAMBIA. E' un latch, non una misura: un valore che oscilla non
        ///             direbbe niente a chi deve decidere se sopprimere una cerimonia. `FINITA` non torna
        ///             `OFF` da sola (la fight non e' mai stata "mai cominciata" dopo che e' cominciata) e
        ///             `IN_CORSO` non torna `OFF` mentre il boss e' vivo - al massimo diventa `FINITA`.
        /// </summary>
        private static void UpdateBossFightState()
        {
            try
            {
                if (Time.time - _lastBossFightStateCheck < BOSSFIGHT_STATE_INTERVAL)
                    return;

                _lastBossFightStateCheck = Time.time;

                string scene = SceneForMarker();
                if (string.IsNullOrEmpty(scene) || scene == "?")
                    return;

                int bossVivi = CountLiveBossesInScene();
                bool peerHere = OtherPlayerInMyScene();
                bool bossToccato = BossAlreadyEngaged();

                BossFightState before;
                if (!_bossFightByScene.TryGetValue(scene, out before))
                    before = BossFightState.Off;

                BossFightState now = before;

                if (bossVivi > 0 && (peerHere || bossToccato))
                    now = BossFightState.InCorso;
                else if (bossVivi == 0 && before == BossFightState.InCorso)
                    now = BossFightState.Finita;

                _bossFightByScene[scene] = now;

                bool first = _bossFightStateArmed;
                _bossFightStateArmed = false;

                if (!first && now == before)
                    return;

                // La riga. Il formato `BOSSFIGHT|scena = VALORE` e' il nome dello stato, e viaggia con i
                // due numeri su cui e' stato deciso, cosi' non c'e' niente da dedurre.
                ModLog.Info($"[BossFight] BOSSFIGHT|{scene} = {BossFightStateName(now)}" +
                            (before == now ? string.Empty : $" (era {BossFightStateName(before)})") +
                            $" · bossVivi={bossVivi} · bossToccato={(bossToccato ? "true" : "false")} · " +
                            $"peerInScena={(peerHere ? 1 : 0)}/{ConnectedPlayersCount()} · " +
                            $"io={(IsHost ? "host" : "peer")} · solo-log-nessun-comportamento");
            }
            catch (Exception e)
            {
                ModLog.Info($"[BossFight] BOSSFIGHT|{SceneForMarker()} = ? " +
                            $"(lettura fallita: {e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// Cleans up the game flags an earlier build of this mod wrote by mistake.
        ///
        /// The first attempt at the join fix carried a death record as a progress entry with
        /// ProgressType.Flag. The game does not tolerate unknown flag names: EventManager.SetFlag CREATES
        /// a FlagObject for a name it has never seen, and every flag is serialised into the savegame. So
        /// each announced death became a permanent flag called "ENEMYDEAD|...", which the server then kept
        /// echoing to whoever joined next.
        ///
        /// Those flags cannot be removed through the game: SetFlag only ever creates or updates, and there
        /// is no delete. GetFlag CREATES the flag if it is missing, so asking about one is worse than
        /// useless. The only way is to take them out of the manager's own dictionary, which is what this
        /// does - read once, filtered, and only touched when something actually matches.
        ///
        /// Nothing reads those names, so removing them cannot break a save; leaving them only adds junk.
        /// </summary>
        private static void RemoveJunkDeathFlags()
        {
            if (_junkFlagsCleaned)
                return;

            EventManager events = Core.Events;
            if (events == null)
                return;

            _junkFlagsCleaned = true;

            try
            {
                FieldInfo field = typeof(EventManager).GetField(
                    "flags", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (field == null)
                    return;

                IDictionary flags = field.GetValue(events) as IDictionary;
                if (flags == null || flags.Count == 0)
                    return;

                List<string> junk = null;
                foreach (object key in flags.Keys)
                {
                    string name = key as string;
                    if (name != null && name.StartsWith(DeathFlagPrefix, StringComparison.Ordinal))
                        (junk ?? (junk = new List<string>())).Add(name);
                }

                if (junk == null)
                    return;

                for (int i = 0; i < junk.Count; i++)
                    flags.Remove(junk[i]);

                ModLog.Info($"[EnemyJoin] rimossi {junk.Count} flag spazzatura scritti da una build precedente");
            }
            catch (Exception e)
            {
                ModLog.Error("[EnemyJoin] pulizia dei flag fallita: " + e.Message);
            }
        }

        /// <summary>
        /// Prefix of the junk flags the first join fix wrote. Kept only so the cleanup can recognise them;
        /// nothing writes flags any more - deaths travel on the EnemyDeathGlobal channel.
        /// </summary>
        private const string DeathFlagPrefix = "ENEMYDEAD|";

        /// <summary>True once the junk-flag sweep has run, so it happens once per session.</summary>
        private static bool _junkFlagsCleaned;

        /// <summary>
        /// The game has just brought the dead enemies back - a bonfire, or the penitence that respawns on
        /// every level load. Everything this mod remembers as "announced dead" has to be forgotten, or the
        /// enemy the game has just recreated is killed again on the same load.
        ///
        /// This mirrors what the game does with its own consumed list, and it is the ONLY place those
        /// memories may be dropped: _pendingDeaths is what carries a death announced from another room until
        /// the enemy appears, so clearing it on an ordinary level load would undo that fix.
        ///
        /// _killLogged is cleared for the same reason: a death happening after the respawn is news again,
        /// and swallowing it would lose the kill.
        /// </summary>
        public static void OnEnemiesRespawned()
        {
            int pending = _pendingDeaths.Count;
            int announced = _announcedDeaths.Count;

            _pendingDeaths.Clear();
            _killLogged.Clear();
            _announcedDeaths.Clear();
            _announcedBirths.Clear();

            // Always logged, not only when something was forgotten.
            //
            // The guard that used to be here ("if (pending > 0 || announced > 0)") made this method silent
            // whenever the lists were already empty, and that is the whole reason the TODO could say the
            // bonfire branch never appeared in any log: the line was suppressed by the condition, not absent
            // because the bonfire had not run. A measurement that only speaks when it has something to say
            // cannot tell "nothing happened" from "it happened and there was nothing to report".
            string scene = Core.LevelManager.currentLevel == null
                ? "?"
                : Core.LevelManager.currentLevel.LevelName;

            ModLog.Info($"[Falo''] memoria nemici: dimenticate {pending} morti ricevute e {announced} " +
                        $"annunciate (scena={scene})");
        }

        /// <summary>
        /// The game has dropped its own consumed list and brought nothing back through the bonfire route:
        /// a player death, a save being loaded, or a reset from the menu - the three callers of
        /// EnemySpawner.Reset, none of which is an ordinary level load (verified in IL).
        ///
        /// This hook exists because forgetting the announced deaths ONLY at the bonfire left a stale memory:
        /// after the enemies came back alive for the local player, this machine still remembered announcing
        /// them, so the next joiner was told about kills that no longer existed and killed enemies the
        /// announcer could see standing. The first version of this fix introduced that error in the opposite
        /// direction, and it is the reason the reset is hooked rather than assumed.
        ///
        /// EnemySpawner.Reset does not run on level changes, so this cannot drop the memory that the
        /// cross-room announcement depends on.
        /// </summary>
        public static void OnDeathStateReset()
        {
            int announced = _announcedDeaths.Count;
            int pending = _pendingDeaths.Count;

            string scene = Core.LevelManager.currentLevel == null
                ? "?"
                : Core.LevelManager.currentLevel.LevelName;

            // A DEATH WE ARE SUPPRESSING MUST NOT BE FORGOTTEN HERE EITHER.
            //
            // The game drops its consumed list and the mod drops its death memory in the same breath, and
            // they are the two halves of one fact: "those enemies are alive again". If the enemy list is kept
            // but this memory is wiped, the two disagree - and the damage is not symmetric, because
            // _announcedDeaths is what lets a joiner learn about kills that already happened. A machine that
            // wiped it while still holding dead enemies would simply stop telling the next player about them,
            // which is the voice-24 error in reverse.
            //
            // This is why the flag is read in both places instead of the two changes being written
            // independently: the coherence is by construction, not by remembering.
            if (PlayerDeathSync.SuppressingWorldReset)
            {
                ModLog.Info($"[Morte] memoria delle morti NON dimenticata ({announced} annunciate, " +
                            $"{pending} ricevute) e la lista del gioco non e' stata svuotata: " +
                            $"la morte non deve respawnare il mondo (scena={scene})");
                return;
            }

            _announcedDeaths.Clear();
            _announcedBirths.Clear();
            _pendingDeaths.Clear();
            _bossKillsApplied.Clear();   // M1: the world came back, so a boss death applied once must be applicable again
            _bossDeathsApplied.Clear();  // A: the same for the recited death sequence
            _hiddenBossCorpses.Clear();  // C: the enemies came back alive, so nothing is a corpse any more
            _killLogged.Clear();

            // Always logged, for the same reason as OnEnemiesRespawned: a measurement that stays silent when
            // there is nothing to forget cannot distinguish "the game did not reset anything" from "it did,
            // and the lists happened to be empty".
            ModLog.Info($"[Falo''] consumedSpawns azzerati dal gioco: dimenticate {announced} morti " +
                        $"annunciate e {pending} ricevute (scena={scene})");
        }

        /// <summary>
        /// Replicates a bonfire used by another player, for the WORLD only.
        ///
        /// A bonfire is local in this game: it respawns the dead enemies and brings back the broken scenery
        /// on the machine that sat down and nowhere else. That was measured, not assumed - after one player's
        /// bonfire the two machines held 5/7/5 and 3/7/3 enemies in the same room, and the second had entered
        /// that room after the bonfire and still saw the pre-bonfire world.
        ///
        /// WHAT IS REPLICATED, AND WHAT IS NOT. Only the three things a bonfire does to the world:
        ///   - the enemies come back,
        ///   - the broken scenery comes back,
        ///   - the game's own list of broken objects is cleared.
        /// The sitting player's own benefits are deliberately left out. They live in ShallowActivationLogic,
        /// which is private, would point ActivePrieDieu at the SENDER's bonfire and force a SaveGame on this
        /// machine - so a remote player heals nothing, refills nothing and saves nothing. That is the rule
        /// the project chose: everyone gets the respawn, only the sitter gets the comfort.
        ///
        /// HOW THE TWO CASES ARE HANDLED WITH ONE CALL. The game's enemy bookkeeping is a single GLOBAL list
        /// of consumed spawn positions, and EnemySpawner scans only the scene that is loaded. So calling
        /// RespawnDeadEnemies here does the right thing on its own in both situations:
        ///   - standing in the bonfire's room: the list is cleared AND the enemies are recreated;
        ///   - standing elsewhere: the list is cleared and nothing is recreated, because their spawn points
        ///     are not in this scene. When they walk back in, CreateEnemy finds the list clean and the
        ///     enemies return by themselves - which is exactly the requested behaviour, and needs no
        ///     teleport, no reload and no scene rebuild.
        ///
        /// WHY THE FLAG. The call above is watched by our own hook, so without a marker a replicated bonfire
        /// is indistinguishable from a player sitting down. Nothing is sent back today, and the flag is what
        /// keeps it that way if anything ever is.
        /// </summary>
        private static void ReplicateBonfire(string scene)
        {
            string here = Core.LevelManager.currentLevel == null
                ? "?"
                : Core.LevelManager.currentLevel.LevelName;

            if (string.IsNullOrEmpty(scene))
            {
                ModLog.Error("[Falo''] annuncio ricevuto senza scena: ignorato");
                return;
            }

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            ModLog.Info($"[Falo''] annuncio RICEVUTO: scena del falo''={scene} · io sono in {here} · " +
                        $"{(scene == here ? "STESSA stanza, respawn completo" : "altra stanza, azzero la memoria")}");

            bool wasReplicating = _replicatingBonfire;
            _replicatingBonfire = true;
            try
            {
                if (Core.Logic == null || Core.Logic.EnemySpawner == null)
                    return;

                Core.Logic.EnemySpawner.RespawnDeadEnemies();

                // The scenery comes back through the game's own event, and the memory of those breaks clears
                // as a side effect: UsePrieDieu raises OnUsePrieDieu, BreakableObject restores itself, and our
                // Restore postfix forgets the guid. Tying the cleanup to the LOCAL execution of Restore - which
                // is what a real bonfire does - is what keeps the two machines from diverging.
                if (Core.Logic != null)
                    Core.Logic.UsePrieDieu();

                if (Core.Logic != null && Core.Logic.BreakableManager != null)
                    Core.Logic.BreakableManager.Reset();
            }
            catch (System.Exception e)
            {
                ModLog.Error($"[Falo''] replica fallita (scena={scene}): {e.GetType().Name} - {e.Message}");
            }
            finally
            {
                _replicatingBonfire = wasReplicating;
            }
        }

        /// <summary>
        /// Registers everything it can see without ever renaming or shifting an id that is already in
        /// use. Called after the game spawns enemies and then periodically, because some enemies are
        /// created later by triggers and standby activations.
        /// </summary>
        private static void RegisterEnemies(string scene)
        {
            HashSet<string> seenThisScan = new HashSet<string>();

            // Enemies that belong to a spawn point: the spawn point is the authored, stable anchor.
            // The link is EnemySpawnPoint.SpawnedEnemy, NOT the hierarchy: CreateEnemy parents the new
            // enemy to Core.Logic.CurrentLevelConfig, so GetComponentInParent<EnemySpawnPoint>() never
            // matches (the in-game dump confirmed childOfSpawnPoint=no for every enemy).
            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();
            if (points != null)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    EnemySpawnPoint point = points[i];
                    if (point == null || point.gameObject == null)
                        continue;

                    string id = BuildSpawnPointId(scene, point);
                    if (!seenThisScan.Add(id))
                    {
                        // Two spawn points in the SAME scan producing one id: an authored duplicate.
                        if (_countedDuplicates.Add(id))
                            _duplicateIds++;
                        continue;
                    }

                    if (!_spawnPoints.ContainsKey(id))
                        _spawnPoints.Add(id, point);

                    Enemy spawned = point.SpawnedEnemy;
                    if (spawned == null || spawned.gameObject == null)
                        continue; // not created yet, or already dead

                    RegisterEnemy(id, spawned);
                }
            }

            // Everything else: triggered encounters, arena waves, and every boss. These have no authored
            // anchor, so they are keyed by name plus the position they had when FIRST registered.
            // The position is captured once and never recomputed: an enemy that walks away from where it
            // started would otherwise look like a brand new enemy on every scan, which is exactly what
            // produced 14 ids for 5 enemies in the first in-game test.
            //
            // They must be found while still INACTIVE: a boss sits disabled in the scene until its
            // cutscene enables it, and FindObjectsOfType<Enemy>() skips disabled objects entirely.
            Enemy[] enemies = FindAllEnemiesIncludingInactive();
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    Enemy enemy = enemies[i];
                    if (enemy == null || enemy.gameObject == null)
                        continue;

                    if (IsRegistered(enemy))
                        continue; // already covered through its spawn point, or by an earlier scan

                    // The id is built from the position the enemy has RIGHT NOW, at its first registration,
                    // and cached against the instance. Rebuilding it later would produce a different string
                    // once the enemy moved, which is how the two machines ended up with different lists.
                    if (_enemyIds.ContainsKey(enemy))
                        continue; // already assigned an id in an earlier scan

                    string id = BuildFreeEnemyId(scene, enemy);
                    if (!seenThisScan.Add(id))
                        continue;

                    RegisterEnemy(id, enemy);
                }
            }
        }

        /// <summary>
        /// Assigns an id exactly once. Already-registered enemies are left untouched, which is what keeps
        /// ids stable while an enemy moves and stops repeated scans from inventing duplicates.
        /// </summary>
        private static void RegisterEnemy(string id, Enemy enemy)
        {
            // ⭐ C) UN CADAVERE DI BOSS CHE ABBIAMO SPENTO NON SI REGISTRA.
            //
            // `HideDefeatedBosses` gira prima di questa registrazione, e un oggetto spento NON basta a
            // fermarla: `IsInertScenery` scarta solo "inattivo E senza `EnemyBehaviour`", mentre un boss ha
            // il suo `EnemyBehaviour` anche da spento. Senza questa riga il cadavere entrerebbe nella tabella,
            // riceverebbe `ResumeEnemy` (`behaviour.enabled = true; StartBehaviour()`) e diventerebbe un boss
            // invisibile che combatte - esattamente il danno che il fix deve evitare.
            if (_hiddenBossCorpses.Contains(enemy))
                return;

            // Already has an id from an earlier scan: leave it alone. This is what keeps an enemy's id
            // stable while it moves, and stops one enemy being tracked twice under two names.
            string existing;
            if (_enemyIds.TryGetValue(enemy, out existing))
            {
                if (existing != id)
                    _duplicateIds++; // same enemy offered a second id: the duplicate seen in game

                return;
            }

            if (_enemies.ContainsKey(id))
            {
                // The id is taken by a DIFFERENT instance. Keeping the first is deliberate: reassigning
                // would move an identity a peer is already following.
                _duplicateIds++;
                return;
            }

            // Scenery is not synchronised. See IsInertScenery: an Enemy that has never been active has
            // run no code at all, so it neither moves nor animates - and that immobility was mistaken for
            // a synchronisation failure for several rounds. Reporting it once is enough.
            if (IsInertScenery(enemy))
            {
                _inertSkipped++;
                return;
            }

            _enemyIds[enemy] = id;
            _enemies.Add(id, enemy);
            _ids.Add(id);
            _registered.Add(enemy);
            _registrations++;

            // The list just changed, so the fingerprint published from now on has to describe the new one.
            // Recomputing once per scan rather than once per enemy keeps this off the hot path.
            _idsDirty = true;

            // A death announced while this machine was elsewhere, or before this enemy existed. The spawn
            // point was already marked consumed by the announcement, so the game should not have created
            // this enemy at all; if it did - the death arrived after SpawnEnemiesOnLoad had already run -
            // mark it again for the next load and take this instance down.
            if (_pendingDeaths.Contains(id))
            {
                // ⭐ UN BOSS E' ESENTE DA QUESTO RAMO, E NON E' UNA PRUDENZA: QUI IL RAMO FA DANNO.
                //
                // Su un boss `MarkSpawnPointConsumed` e' gia' un no-op (l'id e' "en|...", non "sp|...") e
                // `DestroyDeadEnemy` e' danno NUOVO, esattamente come lo e' nel ramo `bdead|` di
                // `ReceiveEnemyDeathGlobal` (vedi il commento li'): chiama `KillEnemyLocally` e poi
                // `SetActive(false)` su un boss che la scena ha appena rimesso in scena, su UNA macchina
                // sola. Il risultato non e' "il boss e' morto anche qui": e' un boss che il gioco ha
                // rigenerato e che la mod abbatte e spegne alle spalle della FSM della stanza, che sulla
                // morte del boss ha la sua strada (la flag `<scena>_BOSSDEAD`, letta dentro l'asset di
                // scena - vedi scratch\DIAGNOSI-BOSS-AFK.md §6.2 punto 2 e §7 punto 7).
                //
                // L'ID VA ANCHE TOLTO DALLA MEMORIA, non solo ignorato: se restasse, l'id di un boss
                // rimarrebbe "in sospeso" per sempre (misurato: G1:4532 "1 ricevute", e OnDeathStateReset
                // esce prima quando la morte sopprime il reset del mondo) e la decisione verrebbe
                // ri-presa a ogni registrazione. Toglierlo e' cio' che fa `ReceiveBirth` per la stessa
                // ragione: una memoria che non deve piu' sopprimere niente non va tenuta.
                //
                // ⚠️ E IL RESTO NON CAMBIA: per un nemico normale il ramo gira come sempre - l'id resta
                // in `_pendingDeaths`, lo spawn point viene risegnato consumato e l'istanza viene
                // abbattuta. Il test dei due casi e' in scratch\REFERTO-FIX-AFK.md.
                if (IsBoss(enemy, id))
                {
                    _pendingDeaths.Remove(id);

                    ModLog.Info($"[BossPending] {FullId(id)} esente dal ramo delle morti in sospeso: " +
                                "un boss non si abbatte ne' si spegne alla registrazione " +
                                "(id tolto dalla memoria, la morte del boss resta alla scena)");
                }
                else
                {
                    MarkSpawnPointConsumed(id);
                    DestroyDeadEnemy(enemy);
                }
            }
        }

        /// <summary>Id of a tracked enemy, or empty when it is not tracked.</summary>
        private static string IdOf(Enemy enemy)
        {
            for (int i = 0; i < _ids.Count; i++)
            {
                if (ResolveEnemy(_ids[i]) == enemy)
                    return _ids[i];
            }

            return string.Empty;
        }

        private static bool IsRegistered(Enemy enemy)        {
            return _registered.Contains(enemy);
        }

        /// <summary>
        /// Every Enemy in the loaded scene, active or not.
        ///
        /// FindObjectsOfType only returns active objects, which silently excludes anything the scene has
        /// not enabled yet - most importantly bosses, which stay disabled until their fight starts and
        /// were therefore never registered.
        /// </summary>
        private static Enemy[] FindAllEnemiesIncludingInactive()
        {
            List<Enemy> found = new List<Enemy>();

            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                // GetComponentsInChildren(true) descends into disabled children too, so a boss parked
                // inactive under a level object is still found.
                Enemy[] inRoot = root.GetComponentsInChildren<Enemy>(true);
                for (int j = 0; j < inRoot.Length; j++)
                {
                    if (inRoot[j] != null)
                        found.Add(inRoot[j]);
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Identity of a spawn-point enemy. The spawn point is authored scene data, so name and position
        /// are the same on both machines.
        /// </summary>
        private static string BuildSpawnPointId(string scene, EnemySpawnPoint point)
        {
            Vector3 p = point.transform.position;
            return $"sp|{scene}|{StripInstanceSuffix(point.gameObject.name)}|{p.x:F2}|{p.y:F2}";
        }

        /// <summary>
        /// Identity of an enemy with no spawn point. EnemySpawnPoint.SetEntityName appends an instance
        /// hash to the name (EntityName + GetHashCode(), but only when EntityName is longer than 3
        /// characters), and an instance hash differs between processes, so the trailing digits are
        /// stripped: 'Tutorial757000320' becomes 'Tutorial', while 'Fool(Clone)' keeps its name.
        /// Enemies still sharing a name differ by the position they had when first registered.
        /// </summary>
        private static string BuildFreeEnemyId(string scene, Enemy enemy)
        {
            string name = StripInstanceSuffix(enemy.gameObject.name);
            Vector3 p = enemy.transform.position;
            return $"en|{scene}|{name}|{p.x:F0}|{p.y:F0}";
        }

        /// <summary>
        /// Removes the per-instance suffix a name picks up, so the same object is called the same thing on
        /// both machines.
        ///
        /// TWO DIFFERENT SUFFIXES HAVE TO BE HANDLED, and treating them as one is what broke the spawn
        /// points. They come from different places and look different:
        ///
        ///   - the GAME appends a hash: EnemySpawnPoint.SetEntityName does "EntityName + GetHashCode()",
        ///     so 'Tutorial757000320' becomes 'Tutorial'. A hash is digits, so the digits are stripped -
        ///     but only when they were APPENDED to something, never for a name that is all digits.
        ///
        ///   - UNITY appends an instance counter in parentheses: 'SPAWN (11)' on one machine,
        ///     'SPAWN (15)' on the other. The counter differs between processes exactly like the hash does.
        ///
        /// The spawn-point builder only did the first, so 'SPAWN (11)' came out unchanged and the two
        /// machines produced different ids for the same spawn point: measured at TWO ids out of five
        /// differing in one room, and the keys that differ never match a claim - no ownership token ever
        /// resolves against them, so those enemies lose BOTH their token and their position. In a full room
        /// that is a handful of enemies permanently broken, silently.
        ///
        /// The parentheses form is removed first, because a name like 'SPAWN (11)' also ends in a digit once
        /// the closing bracket is taken off - the two patterns would otherwise overlap on nothing useful.
        /// </summary>
        private static string StripInstanceSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // Unity's instance counter: 'NAME (12)'. Stripped only when the parentheses hold digits and
            // close the name, so a genuine 'Fool (Clone)' keeps its suffix.
            int open = name.LastIndexOf(" (", System.StringComparison.Ordinal);
            if (open > 0 && name.EndsWith(")", System.StringComparison.Ordinal))
            {
                string inside = name.Substring(open + 2, name.Length - open - 3);
                if (inside.Length > 0)
                {
                    bool digits = true;
                    foreach (char ch in inside)
                    {
                        if (!char.IsDigit(ch)) { digits = false; break; }
                    }

                    if (digits)
                        return name.Substring(0, open);
                }
            }

            // The game's own hash-append: trailing digits, but never a name that was all digits to begin
            // with.
            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1]))
                end--;

            if (end == name.Length || end == 0)
                return name;

            return name.Substring(0, end);
        }

        /// <summary>
        /// True when THIS machine's player is the closest to the enemy, so it should simulate it.
        ///
        /// This is now used ONLY by the host, as the rule it applies when arbitrating. The peer never calls
        /// it: its ownership comes from the EnemyOwnership token the host sends, because two machines
        /// measuring distance locally cannot be relied on to agree.
        ///
        /// Proximity replaces aggro, because aggro is simply not available. Two attempts to read it failed
        /// for the same underlying reason: each machine simulates its own copy of the enemy, and that copy
        /// chases its own player. EnemyBehaviour.GetTarget() falls back to Core.Logic.Penitent (the LOCAL
        /// player) and Enemy.Target is always set to that same local player, so every machine truthfully
        /// reports "it is chasing me". No field can express "chasing the other player", because in the
        /// game's own model the other player does not exist.
        /// </summary>
        private static bool ShouldOwnByProximity(Enemy enemy)
        {
            Penitent local = Core.Logic.Penitent;
            if (local == null)
                return true; // no local player yet: keep it, so an enemy is never left unowned

            // A CORPSE DOES NOT KEEP THE ENEMY.
            //
            // This is what put a boss in a permanent orbit around a dead player. Nothing in the game retargets
            // it: EnemyBehaviour.GetTarget does not check whether its target is alive and falls back to the
            // LOCAL Penitent, and the remote player is not a Penitent at all. So the boss keeps chasing
            // whichever corpse is on the machine that owns it - and this method was happily keeping that
            // ownership, because it only ever compared distances.
            //
            // The rest of the rule made it worse rather than better: a dead player stops sending positions
            // (the packet only goes out when the position changes), so their last known position goes stale
            // within 0.3 seconds and the "no remote position known" branch below keeps the enemy here.
            //
            // So a dead player declines ownership, full stop. That is safe in both directions:
            //   - if this machine's player cannot use it, somebody else should have it, and the host grants
            //     it to whoever is closer and alive;
            //   - if EVERYBODY is down, refusing changes nothing, because nobody is simulating anything the
            //     players can see, and the ownership is rebuilt from distances the moment someone respawns.
            //
            // It has to be the game's own death state and not our announcement flag: the flag is raised when
            // the death is TOLD, while this has to be true from the instant the player falls.
            if (PlayerDeathSync.IsLocalPlayerDead())
                return false;

            Vector2 enemyPos = enemy.transform.position;
            float localDistance = Vector2.Distance(enemyPos, local.transform.position);

            float nearestRemote = float.MaxValue;
            foreach (KeyValuePair<string, Vector2> entry in _remotePositions)
            {
                if (!IsRemotePositionFresh(entry.Key))
                    continue;

                float d = Vector2.Distance(enemyPos, entry.Value);
                if (d < nearestRemote)
                    nearestRemote = d;
            }

            // No remote position known yet (still connecting, or the other player is in another scene, in
            // which case their packet never arrives), or the only one we have is too old to trust: nobody
            // can be shown to be closer, so keep the enemy here rather than handing it to a player we
            // cannot locate.
            if (nearestRemote == float.MaxValue)
                return true;

            // A remote player has to be MEANINGFULLY closer, not a hair closer. Positions arrive about
            // 20 times a second, so a bare comparison would flip the owner back and forth while the two
            // players stand near each other.
            return localDistance + OWNER_DISTANCE_MARGIN < nearestRemote;
        }

        /// <summary>
        /// True when the OTHER player is meaningfully closer to the enemy than this machine's player, so
        /// this machine should give the enemy up.
        ///
        /// This is the exact mirror of ShouldOwnByProximity: one is strict "I am closer", the other strict
        /// "they are closer", and the margin in both means that while the two distances are comparable
        /// NEITHER is true. That is what stops the owner from flapping when the players stand together.
        ///
        /// It exists because the handover used to depend on RemoteEverClaimed - the other machine having
        /// transmitted an update with ownedRemotely set. But a peer only transmits the enemies the host
        /// granted it, so it could never claim the very enemy it was trying to take:
        ///
        ///   the peer does not claim, because it does not own;
        ///   it does not own, because it never claimed.
        ///
        /// With the distances, which travel in every update, the host can see for itself who is closer and
        /// hand the enemy over. That is what makes the two players equal: being the arbiter stops being a
        /// privilege and becomes only the job of comparing two numbers.
        /// </summary>
        private static bool PeerIsCloser(Enemy enemy)
        {
            Penitent local = Core.Logic.Penitent;
            if (local == null)
                return false; // no local player: keep it rather than hand it to nobody

            Vector2 enemyPos = enemy.transform.position;
            float localDistance = Vector2.Distance(enemyPos, local.transform.position);

            float nearestRemote = float.MaxValue;
            foreach (KeyValuePair<string, Vector2> entry in _remotePositions)
            {
                if (!IsRemotePositionFresh(entry.Key))
                    continue;

                float d = Vector2.Distance(enemyPos, entry.Value);
                if (d < nearestRemote)
                    nearestRemote = d;
            }

            // No remote position known, or the one we have is too old to trust: the other player cannot be
            // shown to be closer, so do not hand the enemy over on the strength of a stale sample.
            if (nearestRemote == float.MaxValue)
                return false;

            return nearestRemote + OWNER_DISTANCE_MARGIN < localDistance;
        }

        /// <summary>
        /// True while the remote position is recent enough to arbitrate on. A missing timestamp counts as
        /// stale: if we do not know when it arrived we cannot claim it is current.
        /// </summary>
        private static bool IsRemotePositionFresh(string playerName)
        {
            float at;
            if (!_remotePositionsAt.TryGetValue(playerName, out at))
                return false;

            return Time.time - at <= REMOTE_POSITION_MAX_AGE;
        }

        /// <summary>
        /// True when another player is HERE, in the same scene as this machine.
        ///
        /// This is the room check the position wait needs, and the freshness of a remote POSITION is not a
        /// substitute for it: positions travel on their own channel and keep arriving whatever scene the
        /// other player is in, so "the other player is reporting a position" is true even when they are two
        /// rooms away and cannot possibly own the enemies here. The SCENE is what says whether waiting is
        /// meaningful - and PlayerStatus.CurrentScene is already maintained for exactly this kind of test.
        /// </summary>
        private static bool OtherPlayerInMyScene()
        {
            string myScene = Core.LevelManager.currentLevel == null
                ? null
                : Core.LevelManager.currentLevel.LevelName;

            if (string.IsNullOrEmpty(myScene))
                return false;

            foreach (PlayerStatus player in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers)
            {
                if (player.CurrentScene == myScene)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True when ANOTHER PLAYER IS IN THIS SCENE **AND IS NOT DOWN** — the one question the boss rule
        /// may ask before leaving a boss to somebody else.
        ///
        /// It is `OtherPlayerInMyScene()` plus one field, and the two halves answer two different defects:
        ///   - NOT HERE: nobody else can be simulating the boss. Leaving it to a machine that is not in the
        ///     room is exactly how a boss ends up owned by nobody - paused, no attacks, `FreezePhysics` on,
        ///     and no token coming that could wake it up. The host recovers it instead.
        ///   - HERE BUT DOWN: the survivor's copy of the boss throws every hit away, because the game's own
        ///     entry point is guarded by the LOCAL player's life
        ///     (`if (Status.Dead || Core.Logic.Penitent.Stats.Life.Current &lt;= 0f) return;`, the guard quoted
        ///     in full in the dead-owner branch of DecideOwnership). A boss left on a corpse is invulnerable
        ///     until that player respawns, which is worse than any handover.
        ///
        /// `IsDead` is the remote player's own announcement (pdead/palive), which is the only source there
        /// is: the other player is a bare GameObject with no Penitent, so nothing local can inspect them.
        /// An unknown player reads as not-dead (`IsDead` starts false), which is the conservative answer:
        /// it keeps the boss where it is until the other machine says otherwise.
        /// </summary>
        private static bool OtherPlayerHereAndAlive()
        {
            try
            {
                string myScene = Core.LevelManager.currentLevel == null
                    ? null
                    : Core.LevelManager.currentLevel.LevelName;

                if (string.IsNullOrEmpty(myScene))
                    return false;

                if (Main.Multiplayer == null || Main.Multiplayer.OtherPlayerManager == null)
                    return false;

                foreach (PlayerStatus player in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers)
                {
                    if (player != null && player.CurrentScene == myScene && !player.IsDead)
                        return true;
                }

                return false;
            }
            catch (Exception)
            {
                // Verso prudente: "non lo so" = l'altro non e' un simulatore affidabile, e l'host riprende
                // il boss invece di lasciarlo fermo. Un boss che si muove troppo e' meglio di un boss fermo.
                return false;
            }
        }

        /// <summary>
        /// Il nome del giocatore remoto che sta QUI e non e' a terra — il destinatario del nuovo verso
        /// nemico→giocatore, oppure `false` quando non ce n'e' nessuno.
        ///
        /// PERCHE' UNA VARIANTE DI `OtherPlayerHereAndAlive()` INVECE DI USARLO. Quello risponde alla
        /// domanda giusta ("l'altro puo' simulare?") ma non dice CHI, e il colpo inoltrato deve portare
        /// un destinatario: il canale Attack e' un broadcast di scena, quindi senza un nome il danno
        /// andrebbe a chiunque sia nella stanza. La regola e' la stessa, riga per riga: stessa scena,
        /// non morto (`IsDead` e' l'annuncio `pdead|nome`, l'unica prova di vita che esiste per un
        /// avatar che non e' un `Penitent`). Un giocatore sconosciuto legge come vivo, che e' il verso
        /// prudente: meglio un colpo inoltrato a chi c'e' che un colpo perso.
        /// </summary>
        internal static bool TryGetRemotePlayerHereAndAlive(out string name)
        {
            name = null;

            try
            {
                string myScene = Core.LevelManager.currentLevel == null
                    ? null
                    : Core.LevelManager.currentLevel.LevelName;

                if (string.IsNullOrEmpty(myScene))
                    return false;

                if (Main.Multiplayer == null || Main.Multiplayer.OtherPlayerManager == null)
                    return false;

                foreach (PlayerStatus player in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers)
                {
                    if (player == null || player.CurrentScene != myScene || player.IsDead)
                        continue;

                    name = player.Name;
                    return !string.IsNullOrEmpty(name);
                }

                return false;
            }
            catch (Exception)
            {
                name = null;
                return false;
            }
        }

        /// <summary>
        /// True when this machine should hold off taking an enemy because its position has not arrived yet.
        ///
        /// The rule is deliberately three-sided, because each side of it was a real failure:
        ///   - the position is already here => nothing to wait for, take it now;
        ///   - NOBODY IS IN THIS ROOM => there is no other owner to receive the position from, so waiting
        ///     would strand the enemy paused for the whole visit - which is exactly what froze 22 enemies
        ///     when the host walked into a room alone;
        ///   - the wait has run past POSITION_WAIT_GRACE => even a player who is here may never send it (a
        ///     stationary enemy is skipped by the change-gated send), so take it where it stands.
        /// </summary>
        private static bool ShouldWaitForPosition(Enemy enemy, EnemyState state)
        {
            if (_remoteTargets.ContainsKey(IdOf(enemy)))
                return false;

            if (!OtherPlayerInMyScene())
                return false;

            return Time.time - state.OwnerSince < POSITION_WAIT_GRACE;
        }

        /// <summary>
        /// The stand-in for the other player that the GAME's own aggro can see (voice 35b).
        ///
        /// The game has exactly ONE aggro target: a single GameObject on Enemy.Target, and every site that
        /// writes it names the LOCAL Penitent. The other player is a bare GameObject with no collider and no
        /// Entity, so the game's own checks cannot see them at all - which is why an enemy owned by one
        /// machine only ever notices that machine's player and stands idle while the other player is right
        /// next to it.
        ///
        /// This proxy closes that gap, and since the step ② of the boss damage it does the one thing that
        /// was missing: it is ALSO the place where a blow aimed at the other player can be intercepted. It
        /// is a plain object on the "Penitent" layer - so the vision check finds the layer it insists on
        /// and the distance check measures to it - and it carries <see cref="CoopRemoteTargetDamageable"/>,
        /// an IDamageable that never touches the LOCAL Penitent and instead FORWARDS the blow to the peer
        /// (see EnemyHitPeerSync.cs). Without that component the proxy is still exactly what it used to be:
        /// a collider nothing can be hurt through.
        /// </summary>
        private static GameObject _remoteTargetProxy;

        private static GameObject RemoteTargetProxy()
        {
            if (_remoteTargetProxy != null)
                return _remoteTargetProxy;

            _remoteTargetProxy = new GameObject("_coop_remote_target");
            UnityEngine.Object.DontDestroyOnLoad(_remoteTargetProxy);

            int penitentLayer = LayerMask.NameToLayer("Penitent");
            if (penitentLayer >= 0)
                _remoteTargetProxy.layer = penitentLayer;

            BoxCollider2D collider = _remoteTargetProxy.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(1.2f, 2.4f);
            collider.isTrigger = true;

            // ⭐ STEP ② — IL PROXY DIVENTA DANNEGGIABILE, E NON PER IL GIOCO: PER NOI.
            //
            // Prima di questa riga il proxy era l'unico collider del mod su layer "Penitent" SENZA un
            // `IDamageable`, e questo aveva due conseguenze opposte, entrambe vere:
            //   - il gioco, che filtra SOLO per layer, lo trovava e poi buttava via il colpo (o andava in
            //     eccezione, `BossJumpAttack.OnPenitentEntersArea` senza null-check: chiuso da `c89c368`);
            //   - il colpo del boss mirato al peer non aveva NESSUN posto dove atterrare: zero danno,
            //     per costruzione (`scratch\DIAGNOSI-DANNO-BOSS-COOP.md` §2).
            //
            // Con il componente il colpo atterra, ma su un `IDamageable` che non e' il `Penitent` locale:
            // fa solo l'INOLTRO al peer, che lo applica al proprio `Penitent`. E' lo stesso punto in cui
            // il gioco decide "questo colpo e' atterrato", quindi vale per QUALUNQUE nemico e QUALUNQUE
            // boss, senza sapere quale sia (EnemyHitPeerSync.cs).
            _remoteTargetProxy.AddComponent<CoopRemoteTargetDamageable>();

            return _remoteTargetProxy;
        }

        /// <summary>
        /// True when this GameObject is the mod's own aggro proxy - the stand-in placed where the other
        /// player is, so the game's vision can see them.
        ///
        /// The game's CONTACT damage must ignore it, and that is not cosmetic. A contact attack is armed by
        /// `ContactDamage.OnTriggerEnter2D`, which does
        /// `IDamageable c = other.GetComponentInParent&lt;IDamageable&gt;(); ... EntityContactDamage(c);`
        /// and `EnemyAttack.ContactAttack` then calls `damageable.Damage(...)` with NO null check - so when
        /// the collider that entered is the proxy, that call used to throw. Worse, `IsTargetOverlapped` is
        /// raised before the null is reached, and the attack that reads it hits `Core.Logic.Penitent`
        /// without ever measuring the distance: the proxy walked into a trigger and the LOCAL player,
        /// twenty units away, took the hit. Ignoring the proxy in the CONTACT path is what stops both, and
        /// it is unchanged by step ②: the proxy is damageable now, but contact damage is still skipped
        /// there on purpose (see AggroProxyPatches.cs).
        /// </summary>
        public static bool IsRemoteTargetProxy(GameObject go)
        {
            if (go == null || _remoteTargetProxy == null)
                return false;

            return go == _remoteTargetProxy || go.transform.IsChildOf(_remoteTargetProxy.transform);
        }

        /// <summary>
        /// Dove sta il proxy, in una riga leggibile, per i marker del verso nemico→giocatore. Serve a
        /// PROVARE NELLO STESSO LOG la geometria su cui poggia tutto il fix: il colpo e' atterrato in
        /// quel punto, e quel punto e' dove la macchina crede che stia l'altro giocatore. Se il proxy
        /// non esiste ancora, `?` (nessuna eccezione: un marker non deve fermare un colpo).
        /// </summary>
        internal static string RemoteTargetProxyPosition()
        {
            try
            {
                if (_remoteTargetProxy == null)
                    return "?";

                Vector3 position = _remoteTargetProxy.transform.position;
                return "(" + position.x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                             position.y.ToString("F1", CultureInfo.InvariantCulture) + ")";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>Marks a birth announcement on the enemy-death channel - the sixth prefix of the series.</summary>
        private const string BirthPrefix = "nato|";

        /// <summary>
        /// Announces the instant an enemy becomes TRULY IN PLAY, so the other machine can reveal its own copy
        /// (voice 53).
        ///
        /// WHY THIS IS NEEDED AT ALL. A whole family of enemies is spawned at level load on BOTH machines and
        /// then hidden - the game's own reveal is what puts them in play - but that reveal is a LOCAL event:
        /// only the machine that triggered it (a player stepping into the mud, an arena trigger, a scripted
        /// beat) ever runs it. On the other machine the enemy stays invisible, and the logs show the player
        /// there runs into something they cannot see that still counts for the room. It is not a desync - the
        /// enemy is not differently alive on the two sides - it is an enemy that only one side can see.
        ///
        /// WHY THE DAMAGE COLLIDER AND NOT activeInHierarchy. For this family nothing ever calls SetActive:
        /// the object is already active, and the reveal is SpriteRenderer.enabled going true, the Animator
        /// speed going to 1 and the damage collider switching on. A poll on activeInHierarchy would therefore
        /// see no edge at all - and it would be noisy besides, because a DEAD enemy is also inactive on one
        /// machine and active on the other. The collider is the one signal that means "this enemy is real
        /// now", and it is generic enough to hold for the families that do use SetActive too.
        ///
        /// WHY IT IS GATED ON OWNERSHIP. Only the machine whose GAME performed the reveal announces it. On the
        /// other machine the collider is driven by the state we impose, so it would flip for our reasons and
        /// the announcement would echo back and forth.
        /// </summary>
        private static void DetectReveals()
        {
            if (_ids.Count == 0)
                return;

            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];

                EnemyState state;
                if (!_lastSent.TryGetValue(id, out state))
                    continue;

                if (!state.OwnedByLocal)
                    continue;

                Enemy enemy = ResolveEnemy(id);
                if (enemy == null || enemy.gameObject == null || enemy.EntityDamageArea == null)
                    continue;

                Collider2D collider = enemy.EntityDamageArea.DamageAreaCollider;
                SpriteRenderer sprite = enemy.GetComponentInChildren<SpriteRenderer>();

                // THE COLLIDER ALONE IS NOT THE TRUTH, and the reason is subtle enough to be worth writing
                // down. The damage collider of this family is switched BY THE ANIMATOR:
                // WaxCrawlerMeltingBehaviour.OnStateEnter sets
                //     DamageAreaCollider.enabled = animator.GetCurrentAnimatorStateInfo(0).IsName("Appear")
                // so the collider turns on for ANY entry into "Appear" - including the one the MOD ITSELF
                // forces when it replays the owner's animation on this machine. Reading the collider alone
                // therefore mistook our own replay for a reveal, announced a birth, and the peer ran Rise()
                // on an enemy its game had never revealed: the sprite flashed up, its own tree put it back
                // down, and the next ownership change started the whole thing again. The logs counted 45
                // announcements for 22 enemies, and crawlers flickering in a rise they never earned.
                //
                // The sprite is what tells the two apart: the real reveal is
                // WaxCrawlerAnimatorInyector.EnableSpriteRenderer, so an enemy that is genuinely in play has
                // BOTH the sprite and the collider on, while our replay leaves the sprite off.
                bool real = collider != null && collider.enabled &&
                            sprite != null && sprite.enabled;

                if (real && !state.WasReal)
                {
                    state.WasReal = true;
                    _announcedBirths.Add(id);

                    if (Main.Multiplayer.NetworkManager.IsConnected)
                        Main.Multiplayer.SendEnemyDeathGlobal(BirthPrefix + id);

                    ModLog.Info($"[Nascita] {FullId(id)} rivelato: annunciato al peer");
                }
                else if (!real)
                {
                    // Back to not-in-play - a respawn puts it back to sleep. Clearing the latch is what lets
                    // the NEXT reveal be announced again instead of being swallowed as a duplicate.
                    state.WasReal = false;
                    _announcedBirths.Remove(id);
                }
            }
        }

        /// <summary>True when this id is a birth announcement rather than a death, a break or a bonfire.</summary>
        private static bool IsBirthId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(BirthPrefix, StringComparison.Ordinal);
        }

        /// <summary>Reads the enemy id out of a birth announcement. Null for anything malformed.</summary>
        private static string BirthIdOf(string id)
        {
            if (!IsBirthId(id))
                return null;

            string enemyId = id.Substring(BirthPrefix.Length);
            return string.IsNullOrEmpty(enemyId) ? null : enemyId;
        }

        /// <summary>
        /// Wakes this machine's copy of an enemy whose game the other machine just revealed (voice 53).
        ///
        /// It uses the game's OWN entry point rather than switching the object on, and for the family that
        /// matters here that is not a detail: the WaxCrawler - and the mud crawlers reuse it - is NEVER
        /// inactive. Its behaviour parks it with the sprite off and the Animator at speed 0, and the game's
        /// reveal is `WaxCrawlerBehaviour.Rise()`, which turns the sprite back on, sets the speed to 1 and
        /// plays "Appear". Nothing else reproduces that, and `SetActive(true)` would be a no-op on an object
        /// that never stopped being active.
        ///
        /// The generic branch is the fallback for the families that really do use SetActive, and it mirrors
        /// the game's own code there: activate, refill the health, clear the dead flag.
        ///
        /// The id is dropped from _pendingDeaths because a spawn point already marked consumed must not keep
        /// suppressing an enemy that has just been put back in play.
        /// </summary>
        private static void ReceiveBirth(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            _pendingDeaths.Remove(id);

            Enemy enemy = ResolveEnemyById(id);
            if (enemy == null || enemy.gameObject == null)
                return;

            if (enemy is WaxCrawler crawler && crawler.Behaviour != null)
            {
                crawler.Behaviour.Rise();
            }
            else if (!enemy.gameObject.activeSelf)
            {
                enemy.gameObject.SetActive(true);
                enemy.Stats.Life.Current = enemy.Stats.Life.Base;
                enemy.Status.Dead = false;
            }

            EnemyState state;
            if (_lastSent.TryGetValue(id, out state))
                state.WasReal = true;

            ModLog.Info($"[Nascita] {FullId(id)} rivelato dal peer");
        }

        /// <summary>
        /// Points an OWNED enemy at whichever player is closer, so its own AI can turn and attack towards
        /// the other player.
        ///
        /// This runs only for the machine that simulates the enemy, and only that machine's decision
        /// matters: the AI then chases and attacks as it normally would, and the position and animation are
        /// already synced to the other machine. No protocol, no server, and no change to the damage path.
        ///
        /// ⚠️ CORREZIONE (18/10) — QUI C'ERA SCRITTO *"the other player takes damage through the game's
        /// local pipeline"*, ED E' **FALSO**. Ogni macchina ferisce **il PROPRIO** `Penitent`: il codice
        /// d'attacco del gioco legge `Core.Logic.Penitent`, cioe' il giocatore di QUESTA macchina, quindi un
        /// colpo che atterra qui ferisce **il giocatore di chi simula**, mai quello remoto. L'altro prende
        /// danno **solo quando e' la SUA macchina a simulare** (misurato: in coppia A il peer diventa padrone
        /// a `L1511` e muore 19 s dopo, `L1641` — `scratch\DIAGNOSI-MEMORIA-NEMICI-E-AGGIO.md` §4.5).
        /// L'unico percorso che attraversa le due macchine e' l'INOLTRO aggiunto in `1d9e4fa`: il colpo che
        /// atterra sul proxy che questa funzione riposiziona viene inoltrato al peer, che lo applica al
        /// **proprio** `Penitent` con la porta d'ingresso del gioco (`EnemyHitPeerSync.cs`). Scrivere che il
        /// danno passa "dalla pipeline locale" faceva credere che questo proxy bastasse da solo: non basta.
        ///
        /// The proxy is positioned here, in the same pass, so the vision linecast tests a collider that is
        /// where the other player actually is.
        /// </summary>
        private static void ApplyAggroTarget(Enemy enemy, string id)
        {
            if (enemy == null || enemy.gameObject == null)
                return;

            Penitent local = Core.Logic.Penitent;
            if (local == null)
                return;

            Vector2 remotePos = Vector2.zero;
            bool hasRemote = false;
            foreach (KeyValuePair<string, Vector2> entry in _remotePositions)
            {
                if (!IsRemotePositionFresh(entry.Key))
                    continue;

                // ⚠️ A DEAD PLAYER IS NOT A TARGET.
                //
                // The corpse bug, and it was ours: a dead player stops sending positions, so their last one
                // ages out of the freshness window - and the branch below then deliberately HOLDS the proxy
                // where they fell, which is precisely where the enemy keeps swinging. EnemyBehaviour.GetTarget
                // returns whatever sits in Enemy.Target without ever testing whether it is alive, so nothing in
                // the game corrects this: this mod is the only writer of Target.
                //
                // The predicate already existed and had no call site at all. The dead are dropped here, and
                // the stale branch below is gated on the same question, so the fallback is the living local
                // player - which is what the enemy faced before that branch was added.
                if (!PlayerDeathSync.IsRemotePlayerKnownAlive(entry.Key))
                    continue;

                remotePos = entry.Value;
                hasRemote = true;
                break;
            }

            GameObject wanted = local.gameObject;

            if (hasRemote)
            {
                GameObject proxy = RemoteTargetProxy();
                proxy.transform.position = new Vector3(remotePos.x, remotePos.y, local.transform.position.z);

                float localDistance = Vector2.Distance(enemy.transform.position, local.transform.position);
                float remoteDistance = Vector2.Distance(enemy.transform.position, remotePos);
                if (remoteDistance < localDistance)
                    wanted = proxy;
            }
            else if (_remoteTargetProxy != null && enemy.Target == _remoteTargetProxy && AnyRemoteTargetAlive())
            {
                // THE REMOTE IS ALREADY THIS ENEMY'S TARGET, AND THEIR POSITION HAS JUST GONE STALE.
                //
                // Their sender is gated on change (|dx| or |dy| > 0.03), so a player who is STANDING STILL
                // sends nothing at all - and the freshness window is only 0.3 s. Falling back to the local
                // player in that moment is what lost the remote the instant they stopped moving.
                //
                // The condition is `Target == proxy`, and that half is essential: this must ONLY preserve a
                // decision that was already made. Forcing the proxy on every owned enemy flipped enemies
                // onto the remote while they were fighting the LOCAL player, and the local player simply
                // stopped being seen. An enemy that was on the local player stays there, and the distance
                // comparison above takes over again as soon as a fresh position arrives.
                wanted = _remoteTargetProxy;
            }

            // Only write when it really changes: Target is a property, and assigning it every pass would
            // be a write per enemy per pass for nothing.
            if (enemy.Target != wanted)
                enemy.Target = wanted;
        }

        /// <summary>
        /// True while at least one remote player is still worth facing - alive, that is.
        ///
        /// It gates the "hold the proxy while the position is stale" branch above, and without it that branch
        /// outlives the player it was written for: a dead player's position is stale BY CONSTRUCTION, because
        /// they stopped sending the moment they died, so the branch held the proxy on their corpse and the
        /// enemy kept attacking it. Being alive is the one thing that makes a stale remote position still
        /// worth trusting, so it is the one thing asked here.
        /// </summary>
        private static bool AnyRemoteTargetAlive()
        {
            foreach (string name in _remotePositions.Keys)
            {
                if (PlayerDeathSync.IsRemotePlayerKnownAlive(name))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Periodic snapshot of what each machine sees, for comparing the two sides by hand.
        ///
        /// This exists because the remaining problems (animations, aggro, the handover) cannot be reasoned
        /// out from a single summary: they need the two machines' views lined up over time. Animation is
        /// the one thing that genuinely has to be compared, since it is what the game uses to show state.
        /// </summary>
        private static void LogDiagnosticSnapshot()
        {
            // THE LOGGING CADENCE IS NOT THE SCAN CADENCE, and conflating them was costing half the log.
            //
            // This method is called from inside the block that fires every RESCAN_INTERVAL, so it printed
            // one line per enemy every half second for the whole session - measured at 640 lines for a
            // three-enemy room, 46% of the lines and about 70% of the bytes of that machine's log.
            //
            // The obvious fix - raise RESCAN_INTERVAL - would have been wrong: the rescan is not there for
            // logging, it is what discovers spawn points created AFTER the level load, and that is precisely
            // what makes a respawned enemy visible on the machine that owns it. Slowing it to quieten the
            // log would have bought tidiness with correctness.
            //
            // So the scan stays at half a second and only the writing slows down. These snapshots were what
            // settled the movement and animation questions, and they are worth keeping at a rate where
            // reading one is still useful; they are worth nothing at six lines a second, because a log
            // nobody reads is the same as no log - and it hides the lines that matter.
            if (Time.time - _lastDiagnosticLog < DIAGNOSTIC_LOG_INTERVAL)
                return;

            _lastDiagnosticLog = Time.time;

            Penitent local = Core.Logic.Penitent;

            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                Enemy enemy = ResolveEnemy(id);
                if (enemy == null)
                    continue;

                // Reference types: TryGetValue leaves this null when the enemy has no record yet, which is
                // what produced "owner=?" before - the whole ternary never ran against the looked-up value.
                EnemyState state;
                bool hasState = _lastSent.TryGetValue(id, out state);

                int animState;
                float animTime;
                ReadAnimation(enemy, out animState, out animTime);

                // Death record, independent of the connection. Reports which enemy died, whether the game
                // set Dead or the life simply reached zero, and whether it was still being drawn - the three
                // things needed to tell a normal kill from an enemy that is merely deactivated.
                //
                // This lives here rather than in the send loop because that loop returns early when there is
                // no connection, so a single-player test produced no death records at all.
                if (enemy.Dead || enemy.CurrentLife <= 0f)
                {
                    if (_killLogged.Add(id))
                    {
                        ModLog.Info($"[EnemyKill] {ShortId(id)} MORTO Dead={enemy.Dead} " +
                                    $"vita={enemy.CurrentLife:F1} attivo={enemy.gameObject.activeInHierarchy} " +
                                    $"pos=({enemy.transform.position.x:F1},{enemy.transform.position.y:F1})");
                    }
                }
                else
                {
                    _killLogged.Remove(id); // alive again (respawn, revive): a later death is news
                }

                float localDistance = local != null
                    ? Vector2.Distance(enemy.transform.position, local.transform.position)
                    : -1f;
                float remoteDistance = NearestRemoteDistance(enemy);

                string owner = hasState ? (state.OwnedByLocal ? "io" : "altro") : "nuovo";
                string remote = hasState ? state.OwnedByRemote.ToString() : "?";

                // Both raw positions, because the ownership rule lives or dies on them. The two machines
                // reported distances to the SAME enemy that differed by ~20 units, which cannot happen if
                // they agree on where the two players are - so these are the numbers to compare directly.
                Penitent localPlayer = Core.Logic.Penitent;
                string localPos = localPlayer != null
                    ? $"({localPlayer.transform.position.x:F1},{localPlayer.transform.position.y:F1})"
                    : "n/d";
                string remotePos = "n/d";
                foreach (Vector2 p in _remotePositions.Values)
                {
                    remotePos = $"({p.x:F1},{p.y:F1})";
                    break;
                }

                ModLog.Info($"[EnemyTick] {FullId(id)} owner={owner} remoto={remote} " +
                            $"dist_io={localDistance:F1} dist_altro={(remoteDistance == float.MaxValue ? "n/d" : remoteDistance.ToString("F1"))} " +
                            $"anim={(animState == 0 ? "0" : AnimatorStateName(enemy))} " +
                            $"life={enemy.CurrentLife:F0} posNemico=({enemy.transform.position.x:F1},{enemy.transform.position.y:F1}) " +
                            $"posMia={localPos} posAltro={remotePos}");

                // Reveals a desynchronisation INSIDE this mod rather than between machines: the cached
                // instance against the one the spawn point currently owns. When these differ, every reading
                // taken through the cache describes an enemy that is no longer in the scene - which is how
                // the single-player log could report one fixed position while the enemy was walking.
                Enemy cachedInstance;
                if (_spawnPoints.TryGetValue(id, out EnemySpawnPoint sp) && sp != null && _enemies.TryGetValue(id, out cachedInstance))
                {
                    Enemy authoritative = sp.SpawnedEnemy;
                    if (authoritative != null && cachedInstance != null && authoritative != cachedInstance)
                    {
                        Vector3 c = cachedInstance.transform != null ? cachedInstance.transform.position : Vector3.zero;
                        Vector3 a = authoritative.transform.position;
                        ModLog.Info($"[EnemyInstance] {ShortId(id)} CACHE DIVERGE: " +
                                    $"cache=({c.x:F1},{c.y:F1}) vero=({a.x:F1},{a.y:F1}) " +
                                    $"istanzaCache={cachedInstance.GetInstanceID()} istanzaVera={authoritative.GetInstanceID()}");
                    }
                }
            }
        }

        /// <summary>
        /// Records where another player is, called for every position packet that arrives.
        ///
        /// The ownership rule needs the other player's position, and this is the one place the value is
        /// known to be correct: it is what the game actually sent.
        /// </summary>
        public static void NoteRemotePlayerPosition(string playerName, Vector2 position)
        {
            if (string.IsNullOrEmpty(playerName))
                return;

            _remotePositions[playerName] = position;

            // When it arrived, so the arbitration can refuse to decide on a stale sample. See
            // REMOTE_POSITION_MAX_AGE: a remote position that has aged by more than that is not evidence
            // about who is closer right now, and using it anyway is what handed a contested enemy to the
            // wrong machine.
            _remotePositionsAt[playerName] = Time.time;
        }

        /// <summary>
        /// How old a remote player's position may be before the ownership rule stops trusting it.
        ///
        /// The rule compares two distances, and only one of them is measured on this machine. The remote
        /// one arrives about twenty times a second and is stored EXACTLY as it came - the lerp in
        /// InterpolateRemoteEnemies smooths enemies, never the other player. Measured in the logs: the
        /// remote position had drifted 5 to 8 units behind, and sat unchanged for thirty consecutive
        /// snapshots, while OWNER_DISTANCE_MARGIN is 2.5. The margin cannot absorb an error twice its size,
        /// so the same rule applied to the two machines gave opposite answers and the same enemy was
        /// contested for about a second.
        ///
        /// Past this age the sample is discarded rather than compared: the rule falls back to "keep it
        /// here", which is the same conservative behaviour it already had for a player it cannot locate at
        /// all. Better to leave an enemy with the machine that has it than to hand it to one that may not
        /// be there.
        /// </summary>
        private const float REMOTE_POSITION_MAX_AGE = 0.3f;

        /// <summary>
        /// Eases every enemy this machine does NOT own towards its last received snapshot.
        ///
        /// Only non-owned enemies are moved: an owned enemy is being simulated by the game's own AI, and
        /// writing to its transform would fight the AI and undo the whole point of running it locally.
        /// </summary>
        private static void InterpolateRemoteEnemies()
        {
            foreach (KeyValuePair<string, Vector2> entry in _remoteTargets)
            {
                Enemy enemy = ResolveEnemy(entry.Key);
                if (enemy == null)
                    continue;

                EnemyState state;
                if (_lastSent.TryGetValue(entry.Key, out state) && state.OwnedByLocal)
                    continue;

                Vector3 current = enemy.transform.position;
                Vector2 target = entry.Value;
                Vector2 flat = new Vector2(current.x, current.y);
                float distance = Vector2.Distance(flat, target);

                // ⭐⭐ PUNTO 0 — UN BOSS NON SI INSEGUE (se la posizione che arriva non e' di adesso).
                if (SkipBossFollow(entry.Key, enemy, flat, target, distance))
                    continue;

                Vector2 next;
                if (distance > FOLLOW_SNAP_DISTANCE)
                    next = target; // a real jump (spawn, knockback): do not smear it out
                else
                    next = Vector2.Lerp(flat, target, FOLLOW_LERP);

                PlaceEnemy(enemy, new Vector3(next.x, next.y, current.z));
            }
        }

        /// <summary>
        /// ⭐⭐ PUNTO 0 — LA GUARDIA DEL TRAPIANTO: il boss non viene spostato dall'inseguimento.
        ///
        /// VERO quando questo enemy e' un BOSS e la voce di `_remoteTargets` che stiamo per applicargli NON e'
        /// stata rinfrescata in QUESTA visita: significa che viene da una visita precedente della stanza, cioe'
        /// da una simulazione che non esiste piu', e non e' una posizione da applicare.
        ///
        /// LA VIA, ed e' questa - NON `AdoptLastKnownPosition`. Il padre lo aveva messo in guardia ed e'
        /// misurato: `3239407` dichiara "il boss non viene mai riagganciato a una posizione remota"
        /// (`:4254-4262`), ma i log sono di `934820d`, che CONTIENE `3239407`, e il boss era a `-822.0` lo
        /// stesso. La riga che lo sposta e' `PlaceEnemy` chiamata da QUI, a ogni frame, con lo SNAP oltre
        /// `FOLLOW_SNAP_DISTANCE = 6f` (`:1088`, `:4125-4126`): 16,3 unita' fra spawn e voce ⇒ SNAP secco.
        /// E' la stessa riga che dice "posizione remota NON adottata" (`G1:L2834`) mentre il salto e' gia'
        /// avvenuto.
        ///
        /// ⚠️ E UN BOSS IN FIGHT CONTINUA A ESSERE INSEGUITO, che e' la ragione per cui la guardia e' sulla
        /// FRESCHEZZA e non sul tipo: quando e' l'altra macchina a simularlo la posizione arriva a ogni tick,
        /// la voce e' di questa visita ⇒ si insegue come sempre, e la fight resta visibile al pupazzo. Senza
        /// questa distinzione ("nessun boss, mai") il boss resterebbe fermo al suo spawn sull'altra macchina
        /// mentre il padrone lo combatte altrove: sarebbe un difetto nuovo, non un fix.
        ///
        /// ⚠️ E NON TOCCA NIENTE DEL BOSS: nessuna vita, nessun flag, nessuna morte, nessuna API del gioco,
        /// nessuna FSM. Salta UNA scrittura di posizione: il boss resta dove la scena l'ha messo - il suo
        /// spawn, che e' 10,4 unita' OLTRE il volume della cerimonia ⇒ il corridoio e' libero, il giocatore
        /// arriva al trigger, la cerimonia parte da sola e il gioco fa il resto (intro, `IntroJump`,
        /// `AVAILABLE_FOR_ACTION`, AI, barra). Per i NEMICI NORMALI il metodo risponde `false` subito e la
        /// funzione insegue esattamente come prima.
        /// </summary>
        private static bool SkipBossFollow(string id, Enemy enemy, Vector2 flat, Vector2 target, float distance)
        {
            try
            {
                if (string.IsNullOrEmpty(id) || !IsBoss(enemy, id))
                    return false;

                int writtenInVisit;
                if (!_remoteTargetsVisit.TryGetValue(id, out writtenInVisit))
                    writtenInVisit = 0;

                if (writtenInVisit >= _sceneVisit)
                    return false;   // il padrone la sta trasmettendo ADESSO: e' la verita', si insegue

                // ⭐ IL MARKER NUOVO: una riga per boss e per visita, cosi' il test lo dimostra.
                if (_bossFollowSkipLogged.Add(id))
                {
                    ModLog.Info($"[Insegue] {FullId(id)} boss: inseguimento SALTATO - la posizione remota e' " +
                                $"della visita {writtenInVisit} e siamo alla {_sceneVisit} (non e' una " +
                                $"simulazione in corso: la posizione del boss la decide la scena) · " +
                                $"boss=({flat.x:F1},{flat.y:F1}) target=({target.x:F1},{target.y:F1}) " +
                                $"dist={distance:F1} · il boss resta dove la scena l'ha messo");
                }

                return true;
            }
            catch (Exception)
            {
                // Se il test "e' un boss" fallisse non si deve fermare l'inseguimento di un nemico normale:
                // si prosegue e si insegue, che e' il comportamento di sempre.
                return false;
            }
        }

        /// <summary>
        /// Moves an enemy the only way that actually holds: through its physics state, not its transform.
        ///
        /// A Blasphemous enemy is driven by CreativeSpore's PlatformCharacterController, whose Update runs
        /// every frame and ends by writing the transform FROM its own PlatformCharacterPhysics.Position:
        ///
        ///   m_platformPhysics.Position = transform.position
        ///   m_platformPhysics.UpdatePhysics(dt)
        ///   transform.position = transform.position + (physics.Position - transform.position)
        ///
        /// Writing transform.position therefore survives only until the next Update, and with velocity near
        /// zero the physics puts the enemy straight back where it was - which is exactly the "enemy stays
        /// frozen" seen in game. Worse, whether the write lands before or after that Update depends on
        /// script execution order, so the same enemy could sit in two different places on the two machines.
        ///
        /// ResetPreviousPosition(Vector3) sets both m_pos and m_prevPos, which realigns the physics to the new
        /// position and stops the spurious DeltaDisp/InstantVelocity a bare transform write would produce.
        /// The game uses it as a library API rather than as part of its own flow (the only managed caller in
        /// Assembly-CSharp is Tools.PlayMaker.Action.PenitentOnElevator.OnEnter, on the Penitent, plus its
        /// own internal use in firstpass), so calling it on enemies disturbs nothing.
        ///
        /// Falls back to the transform when there is no controller (an enemy not yet initialised), because a
        /// wrong-but-visible position is better than a skipped update.
        /// </summary>
        private static void PlaceEnemy(Enemy enemy, Vector3 position)
        {
            // The transform has to move FIRST, and this order is the whole method.
            //
            // PlatformCharacterController.ResetPreviousPosition() reads the transform and copies it INTO
            // the physics - it does not write the transform from the physics:
            //
            //   m_prevPos = transform.position;
            //   m_platformPhysics.ResetPreviousPosition(m_prevPos);
            //
            // So calling it after setting the physics silently undoes the move: the physics gets the new
            // position and is then overwritten with the old transform position again. That is exactly what
            // happened in the first attempt at this method, and it left the peer's enemies standing still.
            //
            // Order: move the transform, then call ResetPreviousPosition - which now reads the transform we
            // just moved and copies that same position into both the physics and m_prevPos. Aligning
            // m_prevPos is what keeps InstantVelocity clean; without it the controller derives a huge
            // spurious velocity from the jump between the old and the new position and the enemy shoots off.
            //
            // Writing physics.Position by hand is NOT needed: ResetPreviousPosition reaches m_pos through
            // set_Position plus m_prevPos = get_Position() (verified in IL), so it lands the same value.
            enemy.transform.position = position;

            PlatformCharacterController controller = enemy.Controller;
            if (controller == null)
                return;

            // No fallback call when the physics is missing: ResetPreviousPosition dereferences
            // m_platformPhysics with no null check (verified in IL), so calling it here would throw a
            // NullReferenceException out of Update() instead of degrading quietly.
            if (controller.PlatformCharacterPhysics == null)
                return;

            controller.ResetPreviousPosition();

            // The collider is a THIRD copy of the position, and it has to be moved too.
            //
            // SmartPlatformCollider is a separate component with its own Update and FixedUpdate. It is not
            // switched off by controller.enabled = false, and it writes transform.position in five places,
            // including a rewind to its own m_prevPos when the enemy is squeezed between floor and ceiling.
            // Its m_prevPos is not the one the two calls above fix, so without this it wakes up every frame
            // holding a stale position: it fights this write, and with the collider's Update mode its
            // _OnPreCull hook redraws the enemy at that stale position, which is the difference between
            // where the enemy is and where it is drawn.
            //
            // TeleportTo sets transform.position, its own m_prevPos and clears m_movingPlatforms (verified
            // in IL), which is exactly the state a teleported collider should be in. One extra call.
            SmartPlatformCollider collider = controller.SmartPlatformCollider;
            if (collider != null)
                collider.TeleportTo(position);
        }

        /// <summary>
        /// Snaps an enemy to the last position the network reported, right when this machine takes ownership.
        ///
        /// THE BUG THIS CLOSES (voice 51). When a machine stops owning an enemy it stops transmitting, and
        /// the machine that takes over starts simulating from ITS OWN local copy - which has not been
        /// simulating, so it is wherever it was, often the spawn point. The new owner then broadcasts that
        /// stale position, and the other player watches the enemy slide back to where it started.
        ///
        /// The corrective data already exists: _remoteTargets[id] holds the last position the previous owner
        /// transmitted, and it keeps being written by the receive path regardless of who owns the enemy. It
        /// is the closest thing to the truth this machine has at the moment of the handover, so the enemy is
        /// placed there before the new owner's AI starts moving it. If there is no record yet - the enemy was
        /// never seen - the snap is skipped and the local copy stands, because there is no better answer.
        /// </summary>
        private static void AdoptLastKnownPosition(Enemy enemy, string id)
        {
            if (enemy == null || enemy.gameObject == null)
                return;

            // ============================================================================================
            // ⭐ ④ UN BOSS NON SI RIAGGANCIA MAI A UNA POSIZIONE REMOTA
            // (referto: scratch\REFERTO-PROPRIETA-BOSS.md; studio: scratch\STUDIO-VIA-BOSS-E-TRIGGER.md §4.6)
            //
            // E' IL SALTO CHE L'UTENTE VEDE, e la misura e' nei log: G2 `[Proprieta'] t=375.5 adotta la
            // posizione (-805.9,22.1)` - il punto di SPAWN - mentre il boss stava combattendo a
            // `(-823.0,9.0)`. Il boss viene teletrasportato indietro di ~12,6 unita', e l'altra macchina lo
            // vede saltare.
            //
            // PERCHE' QUI E NON SOLO NEI DUE RAMI DELL'ARBITRO. `_remoteTargets` e' scritta da OGNI update
            // ricevuto ma CONTIENE SOLO CIO' CHE IL PADRONE HA TRASMESSO: quando il boss passa all'altra
            // macchina, questa non riceve piu' niente per quell'id, quindi il record resta fermo all'ultima
            // posizione trasmessa dal vecchio padrone - e se il vecchio padrone non lo simulava da tempo
            // (o se il record viene da una VISITA PRECEDENTE della stanza: `LevelLoaded` non svuota
            // `_remoteTargets`, di proposito, :2079-2084) quella posizione e' lo SPAWN di quando nessuno
            // combatteva. Adottarla non e' "correggere": e' scrivere un'assenza come se fosse una verita'.
            //
            // ⚠️ E NON SI PERDE NIENTE A SALTARLA, che e' la ragione per cui questa guardia e' sicura:
            // `InterpolateRemoteEnemies` (:3601-3626) insegue GIA' `_remoteTargets` a OGNI frame su ogni
            // nemico che questa macchina NON possiede, e oltre 6 unita' fa uno SNAP secco. Al momento del
            // passaggio di mano la copia locale e' quindi gia' dove e' il boss: l'adozione aggiungeva solo
            // il salto, non la verita'. Per un nemico normale il salto puo' servire (la copia puo' essere
            // rimasta indietro di pochi frame e il lerp e' morbido); per un boss e' solo un teletrasporto
            // visibile, perche' tutti i suoi spostamenti sono grandi e tutte le sue pause sono lunghe.
            // ============================================================================================
            try
            {
                if (!string.IsNullOrEmpty(id) && IsBoss(enemy, id))
                {
                    ModLog.Info($"[Proprieta'] {FullId(id)} boss: posizione remota NON adottata " +
                                $"(resta a ({enemy.transform.position.x:F1},{enemy.transform.position.y:F1}), " +
                                "la copia locale e' gia' inseguita ogni frame da InterpolateRemoteEnemies)");
                    return;
                }
            }
            catch (Exception)
            {
                // Se il test "e' un boss" fallisse non si deve saltare l'adozione per un nemico normale:
                // si prosegue e si adotta, che e' il comportamento di sempre.
            }

            Vector2 lastKnown;
            if (!_remoteTargets.TryGetValue(id, out lastKnown))
                return;

            Vector3 target = new Vector3(lastKnown.x, lastKnown.y, enemy.transform.position.z);
            PlaceEnemy(enemy, target);

            ModLog.Info($"[Proprieta''] t={Time.time:F1} {FullId(id)} adotta la posizione " +
                        $"({lastKnown.x:F1},{lastKnown.y:F1}) al passaggio di proprieta'");
        }

        /// <summary>Distance to the nearest other player, or float.MaxValue when none is known.</summary>
        private static float NearestRemoteDistance(Enemy enemy)        {
            float nearest = float.MaxValue;
            foreach (Vector2 remote in _remotePositions.Values)
            {
                float d = Vector2.Distance(enemy.transform.position, remote);
                if (d < nearest)
                    nearest = d;
            }

            return nearest;
        }

        private static string AnimatorStateName(Enemy enemy)
        {
            Animator animator = AnimatorOf(enemy);
            if (animator == null || animator.runtimeAnimatorController == null)
                return "n/d";

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            return state.IsName("Idle") ? "Idle" : state.shortNameHash.ToString();
        }

        /// <summary>
        /// Reads the spawn points the GAME considers defeated, as positions.
        ///
        /// EnemySpawner keeps them in a private List&lt;Vector3&gt; and answers IsSpawnerConsumed() with
        /// consumedSpawns.Contains(sp.transform.position) - an exact Vector3 comparison. So the useful
        /// payload is the POSITION: not the enemy id, and not the position the enemy died at, because the
        /// two machines kill the same enemy at slightly different spots (2.2 units apart in the test that
        /// exposed this) while the spawn point is the same point for both.
        ///
        /// The list is private, so it is read reflectively. That is deliberate and cheap: one lookup per
        /// call, and the call happens only when a player joins.
        /// </summary>
        private static List<Vector3> ConsumedSpawnPositions()
        {
            List<Vector3> result = new List<Vector3>();

            EnemySpawner spawner = Core.Logic != null ? Core.Logic.EnemySpawner : null;
            if (spawner == null)
                return result;

            try
            {
                FieldInfo field = typeof(EnemySpawner).GetField(
                    "consumedSpawns",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (field == null)
                {
                    ModLog.Error("[EnemyJoin] campo 'consumedSpawns' non trovato: non posso annunciare le morti");
                    return result;
                }

                List<Vector3> consumed = field.GetValue(spawner) as List<Vector3>;
                if (consumed != null)
                    result.AddRange(consumed);
            }
            catch (Exception e)
            {
                ModLog.Error("[EnemyJoin] lettura di consumedSpawns fallita: " + e.Message);
            }

            return result;
        }

        /// <summary>
        /// Re-announces this machine's defeated spawn points, so a player who joins LATER learns about
        /// deaths that happened before it was connected.
        ///
        /// A death is normally a live event: SendEnemyKill and SendEnemyDeathGlobal sit inside UpdateCore,
        /// which returns immediately while disconnected, and the id is dropped straight after. So a death
        /// that happened while alone is never transmitted and never retried - the joiner keeps a living
        /// copy of an enemy the other player already killed. That is the desync this fixes.
        ///
        /// Every machine announces its OWN list when somebody joins, which is what makes the result a
        /// UNION. That is not a choice: the server already keeps the union per team, and the alternative -
        /// letting one machine impose its list - would erase the other's kills and make a dead enemy
        /// reappear in front of the player who killed it.
        ///
        /// Called from NetworkManager.ReceiveConnection, which runs inside the packet queue on the main
        /// thread, so queueing the messages here is safe.
        /// </summary>
        public static void AnnounceDeathStateToPeers()
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            // Announced as spawn-point ids and on the EnemyDeathGlobal channel, NOT as progress entries.
            //
            // The first attempt sent progress records with a custom id and ProgressType.Flag, and that was
            // wrong twice over: EventManager.SetFlag CREATES a game flag for an unknown name, and
            // GetCurrentPersistentState serialises every flag - so the record was written into the save file
            // permanently, and the server kept echoing it to every later joiner. The death channel already
            // does exactly this job and reaches everyone except the sender.
            //
            // The ids from the current scene are the precise ones: they carry a position the game itself has
            // marked consumed. The remembered ones cover the rooms this machine has since walked out of, and
            // a spawn-point id is built from scene, name and position - authored data - so the same spawn
            // point yields the same string on both machines and the union deduplicates instead of doubling.
            List<string> sceneIds = ConsumedSpawnPointIds();

            // Reported even when empty, because an empty announcement used to be silent, and that silence is
            // the only reason this defect was found by accident rather than by reading a log.
            if (sceneIds.Count == 0)
            {
                ModLog.Info($"[EnemyJoin] annuncio vuoto per la scena {_scene} (nessuno spawn point consumato " +
                            $"visibile): uso solo le {_announcedDeaths.Count} morti in memoria");
            }

            List<string> ids = new List<string>(sceneIds);
            HashSet<string> seen = new HashSet<string>(sceneIds);

            foreach (string remembered in _announcedDeaths)
            {
                if (seen.Add(remembered))
                    ids.Add(remembered);
            }

            if (ids.Count == 0)
            {
                ModLog.Info("[EnemyJoin] nessuna morte da annunciare (memoria vuota, scena senza consumati)");
                return;
            }

            for (int i = 0; i < ids.Count; i++)
                Main.Multiplayer.SendEnemyDeathGlobal(ids[i]);

            // And the REVEALED enemies, for the same reason deaths are replayed: the birth announcement has
            // no memory on the server, so a joiner who was not there when it went out would keep seeing the
            // hidden copy of an enemy that is in play for everybody else.
            foreach (string born in _announcedBirths)
                Main.Multiplayer.SendEnemyDeathGlobal(BirthPrefix + born);

            ModLog.Info($"[EnemyJoin] annunciate {ids.Count} morti e {_announcedBirths.Count} nascite " +
                        $"precedenti al nuovo giocatore " +
                        $"({sceneIds.Count} dalla scena, {ids.Count - sceneIds.Count} dalla memoria)");
        }

        /// <summary>
        /// The spawn-point ids whose positions the game has marked as consumed.
        ///
        /// EnemySpawner stores positions, so the position is looked up against the spawn points present in
        /// the scene and the matching one's OWN id is recovered. That is the inverse of the lookup
        /// MarkSpawnPointConsumed does, and it produces the id the death channel speaks.
        /// </summary>
        private static List<string> ConsumedSpawnPointIds()
        {
            List<string> result = new List<string>();

            List<Vector3> consumed = ConsumedSpawnPositions();
            if (consumed.Count == 0)
                return result;

            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();
            if (points.Length == 0)
                return result;

            for (int c = 0; c < consumed.Count; c++)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    // The game's own comparison is exact; this only has to decide WHICH point a consumed
                    // position refers to.
                    if (Vector3.Distance(points[i].transform.position, consumed[c]) > 0.5f)
                        continue;

                    string id = BuildSpawnPointId(_scene, points[i]);
                    if (!string.IsNullOrEmpty(id) && !result.Contains(id))
                        result.Add(id);

                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// True when an enemy is scenery rather than something to synchronise: it has never run any code.
        ///
        /// The game creates an Enemy for EVERY spawn point, whatever the scene author intended, and a
        /// spawn point with SpawnEnabledEnemy false is switched off immediately by
        /// EnemySpawnPoint.CreateEnemy. Nothing switches it back on - SetEnemyActive(true) is reachable
        /// only from SpawnEnemyOnArena, which is called by PontiffHuskiesCombatTrigger.CombatSequence and
        /// Tools.Playmaker.Action.EnableStandByEnemies - so it is never active, Awake never runs, and it
        /// has no EnemyBehaviour, no Animator state and no entry in Entity.livingEntities.
        ///
        /// The test room has two of these (at about -697.1,12.8 and -689.1,11.8). This mod sees them
        /// because it is the only thing that looks with GetComponentsInChildren<Enemy>(true) and so
        /// includes inactive objects; the game's own FindObjectsOfType does not. They sat still in every
        /// single test - anim=0, life=60, one position - and for a long time that was read as enemies
        /// failing to synchronise. It was never a symptom: an object that never runs cannot move or
        /// animate, so a single position is guaranteed by construction and measures nothing.
        ///
        /// They are still counted where the GAME counts them - the consumed spawn points the room uses to
        /// decide whether it is cleared - because that bookkeeping is the game's, not ours. They are only
        /// kept out of the tracking that drives synchronisation and the diagnostics.
        /// </summary>
        private static bool IsInertScenery(Enemy enemy)
        {
            if (enemy == null || enemy.gameObject == null)
                return true;

            // Active means the game is running it; that is enough to treat it as real.
            if (enemy.gameObject.activeInHierarchy)
                return false;

            // Inactive with a behaviour is a stand-by enemy waiting for its arena trigger: it has run
            // Awake, it has a behaviour, and the game will switch it on. Keep it.
            return enemy.EnemyBehaviour == null;
        }

        private static string ShortId(string id)
        {
            string[] parts = id.Split('|');
            if (parts.Length >= 2)
                return parts[1] + (parts.Length >= 3 ? "@" + parts[2] : string.Empty);

            return id;
        }

        /// <summary>
        /// The id in full, so one enemy can be told apart from the others by hand.
        ///
        /// ShortId collapses every enemy in a room to "scene@name", because the part that makes each one
        /// unique is the POSITION - an id is already "sp|scene|name|x|y" or "en|scene|name|x|y". For a
        /// per-enemy diagnosis that is the one thing the log must not drop, so the full string is what the
        /// markers print. It is a separate helper and not a change to ShortId: nine existing lines use
        /// ShortId and are compared against sessions that were logged with it.
        /// </summary>
        private static string FullId(string id)
        {
            return id;
        }

        /// <summary>
        /// The HOST's arbitration: decides which machine simulates an enemy, and reports whether the role
        /// changed. Called only on the host - the peer receives this decision as a token instead.
        ///
        /// The rule, deliberately sticky:
        ///   - the host takes an enemy when its own player is the closest;
        ///   - it hands the enemy over when the peer has claimed it and the host is not closer;
        ///   - the role only moves after OWNER_HOLD_SECONDS, which stops the two machines from trading the
        ///     same enemy many times per second.
        /// </summary>
        private static bool DecideOwnership(Enemy enemy, string id, EnemyState state, bool localIsHost)
        {
            bool wants = ShouldOwnByProximity(enemy);
            bool peerCloser = PeerIsCloser(enemy);

            // ============================================================================================
            // ① UN BOSS NON E' UN NEMICO QUALUNQUE — LA PROPRIETA' E' STABILE
            // (referto: scratch\REFERTO-PROPRIETA-BOSS.md; studio: scratch\STUDIO-VIA-BOSS-E-TRIGGER.md §4.6)
            //
            // MISURATO, e non e' una supposizione: trattato come un nemico qualunque, il boss cambiava di
            // mano 4 volte in ~60 s e stava FERMO 30,5 s su 58 (53%) su G1 - tutto `esito=` di proprieta',
            // niente cerimonia. Le due macchine misurano la stessa distanza in modo diverso (§4.4), quindi
            // con il margine di 2,5 nessuna delle due ha ragione in modo stabile e la decisione viene
            // rimessa in discussione a ogni tick.
            //
            // ⚠️⚠️ E LA SCOPERTA CHE CAMBIA IL FIX (§4.6): il ramo che fa il danno non e' un passaggio di
            // mano, e' IL PRIMO, dove il ripiego `!peerCloser && localIsHost` SCAVALCA `ShouldWaitForPosition`
            // e fa adottare lo spawn. Misurato: `adotta (-805.9,22.1)` mentre il boss era a `(-823.0,9.0)`,
            // e l'altra macchina se lo vedeva teletrasportare. ⇒ rendere il boss "definitivo" senza
            // proteggere il PRIMO ramo CONGELA quel difetto invece di toglierlo.
            //
            // ⇒ la regola ha TRE proprieta', e sono le tre scritte qui sotto:
            //   ② il boss non entra nel ripiego del primo ramo (questo blocco);
            //   ③ il boss non entra nei due rami di cessione (piu' sotto), con UNA eccezione: il recupero;
            //   ④ il boss non viene MAI riagganciato a una posizione remota (`AdoptLastKnownPosition`).
            //
            // La forma e' quella dei tre precedenti gia' in produzione: `SetHealth` saltato per i boss
            // (:4770-4782), esenzione dal ramo `_pendingDeaths` (:2815-2822), `HideDefeatedBosses`
            // (:692-740). L'id arriva dal chiamante (:4474) invece di essere ricalcolato con `IdOf` a ogni
            // tick: `IsBoss` ha bisogno dell'id, e `IdOf` e' una scansione lineare che qui girerebbe per
            // OGNI nemico a OGNI tick.
            // ============================================================================================
            bool boss = IsBoss(enemy, id);

            if (state.OwnerSince <= 0f)
            {
                // First time this enemy is considered. Whoever's player is closer takes it, and the arbiter
                // only falls back to keeping it when NEITHER player is meaningfully closer - an enemy out of
                // everyone's reach must not be left with no machine simulating it.
                //
                // It used to read `localIsHost || wants`, which handed the enemy to the arbiter whatever the
                // distances were. That is what made being the first player to connect feel like a privilege:
                // the arbiter kept every enemy it touched, and the peer could not take one even standing on
                // top of it. The distances are known on both machines (they travel in every update), so the
                // arbiter has no need to favour itself.
                state.OwnerSince = Time.time;

                // VOICE 51: hold off taking when this machine is the CLOSER player but the enemy's position
                // has not arrived yet - the test is `_remoteTargets.ContainsKey(IdOf(enemy))`, i.e. "do I
                // already know where THIS enemy is?". Taking then would resume the enemy at its spawn copy
                // and broadcast that stale position - before any corrective adopt could run. The current
                // owner keeps transmitting until the handover token, so by the next scan the position is
                // here and the take lands on the correct spot.
                //
                // ⚠️ CORREZIONE (18/10) — QUI C'ERA SCRITTO *"_remoteTargets is emptied by LevelLoaded, so in
                // the instant a player walks into a room it is empty"*, ED E' **FALSO DA FIX 51**:
                // `_remoteTargets` NON viene svuotata dal caricamento del livello, e la scelta e' dichiarata
                // a `LevelLoaded` (:2392-2397): gli id sono qualificati dalla SCENA ("sp|D01Z01S03|SPAWN|x|y"),
                // quindi tenere la memoria e' cio' che rende sicuro il RIENTRO. E comunque questa attesa non
                // ne dipende: la domanda e' su QUESTO id (`ShouldWaitForPosition`, :3756), non "il dizionario
                // e' vuoto". Vedi `scratch\DIAGNOSI-MEMORIA-NEMICI-E-AGGIO.md` §6, punto 9.
                //
                // ShouldWaitForPosition carries the two guards that keep the wait from stranding the enemy:
                // no reachable peer (nobody can send the position), or the wait has run too long. The host
                // fallback (!peerCloser && localIsHost) is exempt: an enemy nobody is closer to must not be
                // left unowned just because its position is not known yet.
                bool take = wants || (!peerCloser && localIsHost);

                // ⭐ ② PROPRIETA' STABILE, PRIMO RAMO: il ripiego `!peerCloser && localIsHost` NON vale per
                // un boss, e questa e' la riga che chiude il difetto misurato.
                //
                // Con `dist_io=22.9` e `dist_altro=21.5` (G1:L2421) il margine rende `wants=false` e
                // `peerCloser=false`: nessuno e' significativamente piu' vicino, quindi il ripiego faceva
                // PRENDERE il boss all'host, che e' esattamente il caso che deve restare fermo. Un boss che
                // nessuno dei due e' "significativamente" vicino a chi appartiene? A chi lo sta gia'
                // simulando, cioe' a quello che era nella stanza prima: l'host lo lascia al peer.
                //
                // ⚠️ E NON E' UNA PERDITA: se il peer NON e' raggiungibile la posizione remota e' vecchia o
                // assente, `ShouldOwnByProximity` risponde `true` (nessuno puo' essere piu' vicino di uno che
                // non si sa dove sia) e l'host se lo prende lo stesso. L'unico caso in cui l'host lo lascia
                // e' quello in cui l'altro lo sta davvero simulando.
                //
                // ⚠️⚠️ MA "LASCIARLO AL PEER" HA SENSO SOLO SE IL PEER **PUO'** AVERLO, e senza questa
                // distinzione il fix creerebbe esattamente il difetto che il mandato vieta di creare: il
                // BOSS FANTASMA. Le posizioni remote viaggiano anche FRA STANZE (dichiarato in
                // `OtherPlayerInMyScene`, :3269-3275), quindi con il peer in un'ALTRA stanza `wants` puo'
                // essere `false` per una distanza che non significa niente - e allora `take=false`, l'host
                // pubblica "il padrone sei tu", il token viene filtrato per scena e non arriva a nessuno, e
                // il boss resta PAUSATO su questa macchina senza che nessuno lo simuli. ⇒ se nessun altro
                // giocatore e' qui VIVO, l'host lo prende SEMPRE: e' lo stesso predicato del recupero
                // (`bossRecovery`, piu' sotto), applicato al primo tick invece che dopo un abbandono.
                bool forced = false;
                if (boss)
                {
                    bool otherCanSimulate = OtherPlayerHereAndAlive();
                    forced = !otherCanSimulate;
                    take = otherCanSimulate ? wants : true;
                }

                // ⚠️ E L'ATTESA DELLA POSIZIONE NON SI APPLICA A UN PRIMO TICK FORZATO. La sua ragione -
                // "non riprendere il nemico sulla copia fresca e trasmettere una posizione vecchia" - per un
                // boss non esiste piu': la posizione remota non viene adottata (:4005-4019) e la copia
                // locale e' inseguita ogni frame. Lasciarla attiva qui riaprirebbe il boss fantasma per la
                // porta di servizio: con l'altro a TERRA `OtherPlayerInMyScene()` e' vero, l'attesa
                // scatterebbe e il boss resterebbe a una macchina che non puo' ucciderlo.
                if (!forced && wants && ShouldWaitForPosition(enemy, state))
                    take = false;

                state.OwnedByLocal = take;

                // ⭐ La proprieta' e' stata DECISA: se questo e' un boss e il padrone e' l'altro, la prossima
                // ricezione gli allinea la vita.
                ForgetBossLifeAlignment(state);

                if (state.OwnedByLocal)
                    AdoptLastKnownPosition(enemy, id);

                // Il marker della decisione: UNA riga per decisione, mai una per motivo (vedi
                // LogBossOwnershipDecision), e solo per i boss.
                if (boss)
                    LogBossOwnershipDecision(enemy, id, state, wants, peerCloser, "primo-tick");

                return false;
            }

            if (!localIsHost)
                return false; // only the arbiter decides; this method is never the peer's path

            // A DEAD OWNER GIVES IT UP, IMMEDIATELY.
            //
            // Refusing to TAKE an enemy while dead is not the same as letting go of one, and only the first
            // was handled. The single branch that releases is the peerCloser one, and it demands the peer be
            // MEANINGFULLY closer - nearestRemote + OWNER_DISTANCE_MARGIN < localDistance. So the likeliest
            // case of all slipped through: dying right next to the boss, where the survivor standing beside
            // the corpse is not 2.5 units nearer. The host then kept the boss, and because the boss's own
            // Damage() discards everything while the LOCAL player's life is zero, every hit the survivor
            // landed was thrown away - on the host, which is where those hits arrive from. The boss was
            // simply invulnerable, and the survivor had no way to tell, because the health bar is local and
            // only the owner ever sees a truthful one.
            //
            // No hold time here, on purpose. OWNER_HOLD_SECONDS exists to stop two LIVING players trading
            // the same enemy many times a second; a corpse has nothing to stabilise, and waiting half a
            // second would be half a second of an unkillable boss.
            //
            // ⭐⭐ ③ - L'ORFANO: SI RILASCIA SOLO SE QUALCUNO E' QUI A PRENDERLI.
            //
            // QUESTA RIGA ERA `if (PlayerDeathSync.IsLocalPlayerDead() && state.OwnedByLocal)`, e la
            // seconda condizione e' quella che produceva l'unico difetto NON cosmetico del giro.
            //
            // IL DIFETTO, passo per passo. Il rilascio e' UNILATERALE: questa macchina si toglie la
            // proprieta' e PUBBLICA il token `SendEnemyOwnership(handle, ownedByReceiver: true)` dal ciclo
            // di invio (riga ~3515, `LastPublishedOwnerIsMe != OwnedByLocal`). Sul peer
            // `ReceiveEnemyOwnership` fa `state.OwnedByLocal = true` e chiama `ResumeEnemy`. Se il peer NON
            // e' in questa scena, pero', quel nemico NON E' NELLA SUA `_ids`: `ResolveEnemy(id)` restituisce
            // null, `ReceiveEnemyOwnership` esce alla riga `if (enemy == null) return;` e il record resta
            // `OwnedByLocal = true` con un `OwnershipKnown = true` che il self-heal (l'unico altro modo di
            // prendere un nemico: `!OwnershipKnown`) non puo' piu' scavalcare.
            //   ⇒ Il nemico NON e' simulato da nessuno: io l'ho lasciato andare e il peer non ce l'ha. Non e'
            //   cosmetico - e' un nemico che non cammina, non attacca e non si fa nemmeno spingere, perche'
            //   `PauseEnemy` spegne il `PlatformCharacterController`.
            //
            // ⚠️ E IL RILASCIO NON E' COSMETICO NEMMENO PER ME, che e' la ragione per cui non si puo'
            // semplicemente ignorare il caso: il commento qui sopra lo dice - `Entity.Damage()` scarta tutto
            // mentre la vita del giocatore LOCALE e' zero. Quindi i colpi che l'altro giocatore porta a
            // questo nemico arrivano, sulla mia macchina, e vengono BUTTATI. Da vivo il rilascio e' la cosa
            // giusta: e' il caso "muoio accanto al boss e il sopravvissuto deve poterlo uccidere".
            //   ⇒ Ma quando il peer e' in un'ALTRA SCENA non c'e' nessun sopravvissuto qui: non c'e' nessuno
            //   che possa raccogliere quei nemici e nessuno che stia combattendo in questa stanza. Il
            //   rilascio, in quel caso, non sposta il nemico verso chi puo' usarlo: lo lascia a nessuno.
            //
            // ⭐ LA DOMANDA 3 DEL REFERTO, che e' quella che decide se il fix e' giusto o se crea uno stallo
            // PEGGIORE: se NON rilasciamo, chi possiede il nemico? RISPOSTA: RESTO IO, e continua a girare
            // sulla mia macchina. `state.OwnedByLocal` non viene toccato, il gate del ciclo di invio
            // (`if (!previous.OwnedByLocal) continue;`) continua a passare, e `ResumeEnemy`/`ApplyAggroTarget`
            // girano come per qualunque nemico mio. Un nemico che gira sulla mia macchina e' MEGLIO di un
            // nemico senza padrone: e' esattamente il confronto che il fix deve vincere, e lo vince.
            //   ⚠️ E per la macchina che sta guardando NON cambia nulla: quando questo corpo si rialza
            //   `ShouldOwnByProximity` torna a poter dire "sono piu' vicino io" e la proprieta' si ricompone
            //   da sola (vedi la domanda 4).
            //
            // ⚠️ PERCHE' IL GATE E' `OtherPlayerInMyScene()` E NON UNA COSA PIU' FINE, tipo "il peer puo'
            // davvero simulare". Perche' e' l'unica domanda che questa macchina puo' RISPONDERE con certezza:
            // `PlayerStatus.CurrentScene` e' mantenuto dal canale di scena, mentre "il peer ha questo nemico
            // in tabella" non e' osservabile da qui. E' la stessa domanda che `ShouldWaitForPosition` usa gia'
            // per la stessa ragione, con lo stesso argomento scritto nella sua intestazione.
            //
            // ⚠️ E NON SI GATA IL RILASCIO PER PROSSIMITA' (`peerCloser`, riga ~3237), DI PROPOSITO: quello
            // e' un TRADE fra due giocatori vivi, e `PeerIsCloser` pretende una posizione remota FRESCA (0.3 s)
            // e un margine. Non e' la morte, non e' il caso del referto, e gatarlo cambierebbe il
            // comportamento normale del gioco.
            if (PlayerDeathSync.IsLocalPlayerDead() && state.OwnedByLocal)
            {
                if (!OtherPlayerInMyScene())
                {
                    // ⚠️ MARKER OBBLIGATORIO, ED E' IL PUNTO DELLA VOCE 5: senza questa riga "l'orfano e'
                    // risolto" e "il nemico non gira piu'" restano INDISTINGUIBILI. Dice la decisione presa e
                    // il motivo, e nomina il nemico, cosi' si puo' seguire QUELL'id per tutta la sessione.
                    //
                    // ⚠️ ED E' DEDUPLICATO (misurato: 1700 righe [Proprieta'] su una sola sessione, 84 per
                    // nemico, perche' questo metodo gira a OGNI tick di invio). Il gate NON e' cambiato e la
                    // decisione NON e' cambiata: e' cambiato solo QUANDO si scrive la riga.
                    LogDeadOwnerDecision(enemy, false);
                    return false;
                }

                state.OwnedByLocal = false;
                state.OwnerSince = Time.time;

                // ⭐ Il padrone e' cambiato (io l'ho lasciato): la prossima ricezione riallinea la vita del boss.
                ForgetBossLifeAlignment(state);

                // L'altro ramo del marker: qui il rilascio AVVIENE, e la riga dice perche' e' lecito.
                LogDeadOwnerDecision(enemy, true);

                return true;
            }

            // IL MARKER NON SI APPLICA (sono vivo, o il nemico non e' mio): il suo stato viene DIMENTICATO,
            // cosi' la PROSSIMA morte di questo giocatore riporta di nuovo la decisione invece di restare
            // muta per tutta la sessione. E' l'unica differenza voluta rispetto a LogTreeState, e non
            // riapre il buco del "motivo nella chiave": qui non si registra nulla, si cancella soltanto.
            _deadReleaseLogged.Remove(IdOf(enemy));

            bool changed = false;

            // ⭐ ③ PROPRIETA' STABILE, I DUE RAMI DI CESSIONE.
            //
            // Per un boss i due rami restano chiusi: non cede mai (il ramo `peerCloser`) e non riprende mai
            // mentre l'altro e' qui a simularlo (il ramo `wants`). E' la regola della voce (e) dello studio,
            // ed e' quella che toglie il ping-pong: oggi il boss cambia padrone 4 volte in ~60 s e ogni
            // cambio e' una pausa (`PauseEnemy` sul vecchio padrone) piu' un riaggancio di posizione.
            //
            // ⚠️⚠️ MA LA CESSIONE NON PUO' ESSERE CHIUSA SENZA UNA VIA D'USCITA, o si scambia il ping-pong
            // con un difetto peggiore: il BOSS FANTASMA. Se il padrone del boss esce dalla stanza, il suo
            // `_lastSent` viene azzerato da LevelLoaded e non torna piu' niente per quell'id: l'host
            // resterebbe con un record che dice "suo del peer" e terrebbe il boss in `PauseEnemy` PER SEMPRE
            // - fermo, senza attacchi, senza collider (`FreezePhysics`), e nessun token a svegliarlo.
            // (`ReleaseOwnedEnemiesBeforeLeaving` gira solo sull'HOST: il peer che esce non restituisce
            // niente.) ⇒ la via d'uscita e' il RECUPERO, e la sua condizione e' la sola che questa macchina
            // puo' rispondere con certezza: NESSUN ALTRO GIOCATORE E' QUI VIVO ⇒ nessuna macchina puo' stare
            // simulando questo boss, quindi lo riprende l'host.
            //
            // ⚠️ E IL "VIVO" NON E' UN DETTAGLIO. Se l'altro e' qui ma a terra, la sua copia del boss BUTTA
            // VIA ogni colpo (`Entity.Damage()` esce se la vita del giocatore LOCALE e' zero, e' la guardia
            // scritta per esteso nel commento del ramo "padrone morto" qui sopra): lasciarglielo vorrebbe
            // dire un boss invulnerabile fino al suo respawn. Con `IsDead` il boss torna all'host, che e'
            // chi puo' ucciderlo.
            //
            // Il recupero e' IMMEDIATO (niente `OWNER_HOLD_SECONDS`) per la stessa ragione del rilascio del
            // padrone morto: non e' un trade fra due giocatori vivi, e mezzo secondo di attesa sarebbe mezzo
            // secondo di boss fermo. Una volta ripreso, non torna piu' indietro (la cessione resta chiusa).
            bool bossRecovery = boss && !state.OwnedByLocal && !OtherPlayerHereAndAlive();

            bool takeNow = bossRecovery ||
                           (wants && !state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS);

            // Take it when this machine's player is meaningfully closer; give it up when the other player
            // is. Both use the margin, so while the two distances are comparable neither applies and the
            // owner simply stays - which is what keeps a contested enemy from flapping between the two.
            //
            // The handover used to require RemoteEverClaimed, and that is the deadlock this fixes: a peer
            // only transmits the enemies it owns, so it could never claim the enemy it was trying to take.
            if (takeNow)
            {
                // Same wait as the first-time branch, with the same guards: take the enemy as soon as the
                // position is here, and never let the wait outlive POSITION_WAIT_GRACE or a reachable peer,
                // because either would leave the enemy paused with nobody simulating it.
                if (!ShouldWaitForPosition(enemy, state))
                {
                    state.OwnedByLocal = true;
                    state.OwnerSince = Time.time;

                    // ⭐ Il padrone e' cambiato (sono diventato io): se un giorno lo perdo di nuovo, la vita
                    // va riallineata a chi lo prende. Il latch si riapre qui, non dove si scrive la vita.
                    ForgetBossLifeAlignment(state);

                    AdoptLastKnownPosition(enemy, id);
                    changed = true;
                }
            }
            else if (!boss && peerCloser && state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS)
            {
                state.OwnedByLocal = false;
                state.OwnerSince = Time.time;
                changed = true;
            }

            if (boss)
                LogBossOwnershipDecision(enemy, id, state, wants, peerCloser,
                                         bossRecovery ? "recupero: nessun altro puo' simularlo" : "fisso");

            return changed;
        }

        // ============================================================================================
        // ⭐ MARKER [BossProp] — LA DECISIONE DI PROPRIETA' DI UN BOSS, UNA RIGA PER DECISIONE
        // ============================================================================================

        /// <summary>Ultima decisione stampata per id: `true` = padrone io, `false` = padrone l'altro.</summary>
        private static readonly Dictionary<string, bool> _bossOwnLogged = new Dictionary<string, bool>();

        /// <summary>
        /// Scrive `[BossProp]` SOLO quando la decisione su quel boss CAMBIA.
        ///
        /// PERCHE' ESISTE. Il fix toglie un difetto che nei log si vede come ASSENZA (niente pause, niente
        /// passaggi di mano) e l'assenza, da sola, non distingue "il fix funziona" da "questo boss non e'
        /// mai stato preso in considerazione". Questa riga dice la cosa che manca: chi e' il padrone,
        /// all'inizio e a ogni cambiamento, con i due booleani che hanno deciso.
        ///
        /// ⚠️ LA CHIAVE E' LA DECISIONE, NON IL MOTIVO - la trappola e' la stessa di `LogTreeState` e di
        /// `LogDeadOwnerDecision`, ed e' scritta per esteso sopra. `DecideOwnership` gira per ogni nemico a
        /// ogni tick di invio: se il motivo entrasse nella chiave, `wants`/`peerCloser` (che oscillano con
        /// le distanze) produrrebbero una riga nuova a ogni tick e la deduplica sarebbe morta. Il motivo e'
        /// il CONTESTO della decisione, e viaggia con essa.
        /// </summary>
        private static void LogBossOwnershipDecision(Enemy enemy, string id, EnemyState state,
                                                     bool wants, bool peerCloser, string why)
        {
            try
            {
                if (state == null || string.IsNullOrEmpty(id))
                    return;

                bool was;
                if (_bossOwnLogged.TryGetValue(id, out was) && was == state.OwnedByLocal)
                    return;

                _bossOwnLogged[id] = state.OwnedByLocal;

                // Le due distanze, perche' sono i due numeri su cui la regola decide e sono quelli che nei
                // log delle due macchine NON concordano (§4.4 dello studio: ~20 unita' di differenza sulla
                // stessa distanza). `[EnemyTick]` le stampa ogni 10 s: qui arrivano nell'istante esatto
                // della decisione, che e' quando servono.
                Penitent local = Core.Logic.Penitent;
                string distLocal = local != null
                    ? Vector2.Distance(enemy.transform.position, local.transform.position).ToString("F1")
                    : "n/d";
                float remote = NearestRemoteDistance(enemy);
                string distRemote = remote == float.MaxValue ? "n/d" : remote.ToString("F1");

                ModLog.Info($"[BossProp] {FullId(id)} padrone={(state.OwnedByLocal ? "io" : "altro")} " +
                            $"motivo={why} wants={wants} peerCloser={peerCloser} " +
                            $"dist_io={distLocal} dist_altro={distRemote} " +
                            $"pos=({enemy.transform.position.x:F1},{enemy.transform.position.y:F1})");
            }
            catch (Exception)
            {
                // Un marker non deve mai fermare l'arbitro: qui si perde una riga, non una decisione.
            }
        }

        /// <summary>
        /// Writes the [Proprieta'] marker of the dead-owner branch, ONCE PER DECISION PER ENEMY.
        ///
        /// IT EXISTS BECAUSE THE MARKER WAS SPAM. DecideOwnership is called from the send loop for every
        /// enemy, every tick, so the two ModLog.Info lines it used to hold wrote one line per enemy per
        /// frame: measured 1700 lines of [Proprieta'] in a single session, 84 for EACH of the 20 enemies,
        /// with the very same text and the same decision on all 84. The decision never changed - it was
        /// only re-evaluated, which is what a per-tick arbiter does. The gate (OtherPlayerInMyScene) and
        /// the decision are untouched; what changed is WHEN the line is written.
        ///
        /// THE KEY IS THE DECISION AND NOT THE REASON, and that is the whole trap. It is the third time
        /// this project has met it: the comment on LogTreeState records the other two, where the reason
        /// ("?" vs "remote owns") was part of the key and the value alternated forever, so every call
        /// looked like a change and the deduplication was dead while the log filled up anyway. Here the
        /// reasoning is the same shape: "l'altro giocatore non e' in questa scena" is a sentence that
        /// happens to be true on many consecutive ticks, but a key holding it would be compared against a
        /// DIFFERENT sentence every time the situation is reached from another place. Only "rilascio" or
        /// "non-rilascio" is a state; the sentence that explains it is context for the change, and it is
        /// reported - unchanged, verbatim - with the change it belongs to.
        ///
        /// The two opposite decisions therefore stay distinguishable in the log, and each enemy is
        /// followed by id across the session.
        /// </summary>
        private static void LogDeadOwnerDecision(Enemy enemy, bool release)
        {
            string id = IdOf(enemy);
            if (string.IsNullOrEmpty(id))
                return;

            string decision = release ? "rilascio" : "non-rilascio";

            string previous;
            if (_deadReleaseLogged.TryGetValue(id, out previous) && previous == decision)
                return;

            _deadReleaseLogged[id] = decision;

            if (release)
            {
                ModLog.Info($"[Proprieta'] {id}: morto, lo RILascio - l'altro giocatore e' in " +
                            "questa scena e puo' prenderlo (il token gli arriva e lo simula lui)");
            }
            else
            {
                ModLog.Info($"[Proprieta'] {id}: sono morto ma NON lo rilascio - l'altro " +
                            "giocatore non e' in questa scena, e rilasciarlo lo lascerebbe senza " +
                            "padrone (nessuno lo simula: non e' nella sua lista). Resta mio e continua " +
                            "a girare qui; lo rimettero' in gioco quando rientra.");
            }
        }

        // ================================================================================================
        // ⭐ [BossLife] — LA VITA DEL BOSS VIAGGIA: "STATO + DISEGNO INSIEME"
        // Referto: scratch\REFERTO-DUE-BARRE.md · diagnosi: scratch\DIAGNOSI-PAUSA-E-DUE-BARRE.md
        // Studio: scratch\STUDIO-VIA-BOSS-E-TRIGGER.md §5
        //
        // IL DIFETTO MISURATO. `ReceiveEnemyUpdate` scriveva la vita con `SetHealth` per OGNI nemico tranne
        // il boss (`if (!isBoss)`): la vita viaggiava in ogni pacchetto e veniva BUTTATA, e la barra e'
        // locale (`BossHealth.BarTarget = target.Stats.Life.Current / Final`), quindi due copie = due barre.
        // Costo misurato in gioco: 110 HP di scarto su 400 (27,5%) costanti per tutto il fight, perche' i
        // colpi che un giocatore aveva gia' inflitto PRIMA che l'altro entrasse in stanza arrivano quando
        // l'altro non ha ancora il boss in `_ids` e vengono scartati (`non in _ids`).
        //
        // PERCHE' NON A OGNI PACCHETTO. Il divieto ha una ragione scritta e verificata: `SetHealth` chiama
        // `KillInstanteneously()` quando il valore e' <= 0 (`Entity.cs:297-304`), e una scrittura a zero
        // lascia un boss IN PIEDI, con la barra vuota e la stanza che non avanza. A ogni pacchetto, poi, si
        // scriverebbe venti volte al secondo su un oggetto di cui nessuno ha chiesto la proprieta'.
        // ⇒ si scrive UNA VOLTA per situazione: all'INGRESSO in scena e a ogni CAMBIO DI PADRONE
        //   (il padrone manda, il pupazzo riceve). Il resto lo fa il relay del danno, che gia' funziona.
        //
        // PERCHE' SOLO IN DISCESA. La macchina che entra ha la copia FRESCA (vita piena): il caso misurato
        // e' "ricevo 290 mentre leggo 400". Una SALITA vorrebbe dire scrivere PIU' vita di quella che il
        // boss ha qui, cioe' resuscitarlo: e' l'unica cosa che il divieto vieta davvero, e non e' il caso.
        // ⚠️ Residuo dichiarato: se il pupazzo fosse PIU' BASSO del padrone non si allinea (il latch resta
        // aperto, non si scrive nulla). Non e' il caso misurato, e non e' una scrittura che si vuole fare.
        //
        // E LA FASE — "IL DISEGNO". `SetHealth` non passa da `Damage()`, che e' l'unico chiamante di
        // `CheckNextPhase()`: la fase del BOSS si riallinea al primo colpo dopo, non qui. `CheckNextPhase`
        // e' **privata e per tipo** (Crisanta/BurntFace/Esdras/PontiffGiant/PontiffOldman/WickerWurm, sei
        // metodi diversi, nessuna dichiarazione nella base `EnemyBehaviour`) e almeno una `ChangePhase` ha un
        // effetto che non e' un semplice cambio di pool (`BurntFaceBehaviour.cs:225-245` infila
        // `RELEASE_SECOND_HAND` nella coda delle azioni) ⇒ **non si forza**. Quello che si forza e' la fase
        // DICHIARATA dal manager, che e' pubblica, e' una funzione pura della vita e non fa partire niente:
        // `BossFightManager.SetCurrentActivePhase()` (vedi `RealignBossPhase`).
        // ================================================================================================

        /// <summary>Pavimento della vita allineata di un boss: MAI zero. Vedi il blocco qui sopra.</summary>
        private const float BOSS_LIFE_FLOOR = 1f;

        /// <summary>Id per cui e' gia' stata scritta la riga "il padrone dichiara vita 0": non si ripete.</summary>
        private static readonly HashSet<string> _bossLifeZeroLogged = new HashSet<string>();

        /// <summary>
        /// Riapre l'allineamento della vita di un boss: la prossima ricezione dal padrone la riallinea.
        ///
        /// Va chiamata dove la proprieta' CAMBIA davvero (non dove viene solo riletta), ed e' l'unico modo
        /// per ottenere il "ai cambi di padrone" senza scrivere la vita a ogni pacchetto. Per un nemico
        /// normale e' un campo che nessuno legge.
        /// </summary>
        private static void ForgetBossLifeAlignment(EnemyState state)
        {
            if (state != null)
                state.BossLifeAligned = false;
        }

        /// <summary>
        /// Allinea la vita LOCALE di un boss a quella dichiarata dal suo padrone, una volta per situazione.
        ///
        /// Chiamata solo dal ramo in cui il MITTENTE dichiara di possedere il nemico: chi manda e' il
        /// padrone, quindi la sua vita e' l'unica che ha senso prendere come riferimento in quel momento.
        /// </summary>
        private static void AlignBossLife(Enemy enemy, string id, EnemyState state, float ownerLife)
        {
            try
            {
                if (enemy == null || state == null || string.IsNullOrEmpty(id))
                    return;

                // Gia' fatto in questa situazione (ingresso/changed padrone): si esce PRIMA di toccare
                // niente, ed e' questo che rende la scrittura "una-tantum" invece di venti al secondo.
                if (state.BossLifeAligned)
                    return;

                // Mai su un boss gia' morto: il suo stato non e' piu' una funzione della vita.
                if (enemy.Status.Dead)
                    return;

                if (ownerLife <= 0f)
                {
                    // ⛔ IL DIVIETO, rispettato nel punto in cui si potrebbe violarlo: `SetHealth(0)` chiama
                    // `KillInstanteneously()`, e da li' il boss resta in piedi con la barra vuota e la stanza
                    // bloccata. La morte ha gia' il suo canale (kill / `bdead|` / `D17Z01_BOSSDEAD`) e non
                    // passa da qui: questa riga esiste perche' un giorno qualcuno provi a farla passare.
                    if (_bossLifeZeroLogged.Add(id))
                    {
                        ModLog.Info($"[BossLife] {FullId(id)} il padrone dichiara vita 0: NON allineo " +
                                    "(mai a 0 - resterebbe in piedi con la barra vuota e la stanza bloccata)");
                    }

                    return;
                }

                float localLife = enemy.CurrentLife;

                // Solo in DISCESA, con l'epsilon della vita: sotto di esso non c'e' niente da correggere.
                // Se il padrone ha PIU' vita di questa copia il latch resta aperto e non si scrive nulla.
                if (ownerLife >= localLife - LIFE_EPSILON)
                    return;

                float applied = Mathf.Max(ownerLife, BOSS_LIFE_FLOOR);
                if (applied >= localLife)
                    return;

                // Il gioco scrive la vita con il proprio setter, MAI assegnando il campo: e' lo stesso
                // principio della scrittura dei nemici normali, e sopra il pavimento non uccide nessuno.
                enemy.SetHealth(applied);

                state.BossLifeAligned = true;

                string causa = state.BossLifeVisit == _sceneVisit ? "padrone-cambiato" : "ingresso-in-scena";
                state.BossLifeVisit = _sceneVisit;

                string fase = RealignBossPhase(enemy);
                string delta = (applied - localLife).ToString("+0;-0");

                // ⭐ IL MARKER: una riga quando la vita viene allineata, con DA CHI e A CHE VALORE.
                // Senza questa riga "le due barre ora coincidono" e "la vita non viene piu' scritta per
                // niente" sarebbero indistinguibili nel log.
                ModLog.Info($"[BossLife] {FullId(id)} vita ALLINEATA a {applied:F0} (era {localLife:F0}, " +
                            $"{delta}) · da=padrone({(IsHost ? "client" : "host")}) · causa={causa} · " +
                            $"fase={fase} · pavimento={BOSS_LIFE_FLOOR:F0} · vitaRicevuta={ownerLife:F0}");
            }
            catch (Exception e)
            {
                // Una scrittura che fallisce non deve fermare la ricezione: si perde l'allineamento, non il
                // pacchetto (che porta anche posizione, animazione e orientamento).
                ModLog.Error("[BossLife] allineamento non riuscito (" + e.GetType().Name + "): " + e.Message);
            }
        }

        /// <summary>
        /// Riallinea la fase DICHIARATA dal manager quando la vita del boss e' stata scritta dall'esterno.
        ///
        /// PERCHE' QUESTA E NON `CheckNextPhase()`. `CheckNextPhase` e' privata e distinta per ogni tipo di
        /// boss (sei metodi, nessuna dichiarazione in `EnemyBehaviour`), e almeno una `ChangePhase` mette in
        /// coda un attacco (`BurntFaceBehaviour.cs:225-245`): forzarla dall'esterno significa far partire una
        /// decisione che appartiene alla macchina che SIMULA il boss, in un momento che nessuno ha scelto.
        /// ⇒ la fase del boss si riallinea al primo colpo dopo, e questo e' dichiarato.
        ///
        /// `BossFightManager.SetCurrentActivePhase()` invece e' pubblica, non solleva eventi, non accende
        /// niente, non manda pacchetti e non tocca la metrica: ricalcola `BossPhase.IsActive` e
        /// `CurrentBossPhaseId` dalla vita corrente (la stessa cosa che il gioco fa in `Init`, in
        /// `StartBossFight` e in `OnDamageBoss`). E' l'unico pezzo di "fase" che si puo' forzare senza
        /// inventarsi una transizione.
        ///
        /// ⚠️ Solo sul manager il cui `Boss` e' ESATTAMENTE questo nemico: in una scena con piu' boss
        /// (Amanecidas) allineare la fase del manager sbagliato sarebbe peggio che non farlo.
        /// </summary>
        private static string RealignBossPhase(Enemy boss)
        {
            try
            {
                BossFightManager[] managers = UnityEngine.Object.FindObjectsOfType<BossFightManager>();
                if (managers == null || managers.Length == 0)
                    return "non-forzata-nessun-manager";

                bool found = false;
                for (int i = 0; i < managers.Length; i++)
                {
                    BossFightManager manager = managers[i];
                    if (manager == null || manager.Boss == null || manager.Boss != boss)
                        continue;

                    found = true;
                    manager.SetCurrentActivePhase();
                }

                return found ? "manager-allineata(boss-al-primo-colpo)" : "non-forzata-manager-di-un-altro-boss";
            }
            catch (Exception e)
            {
                // La fase e' il "disegno": se non si puo' riallineare, la vita e' comunque scritta.
                return "errore:" + e.GetType().Name;
            }
        }

        /// <summary>
        /// Applies the host's ownership token. This is the ONLY thing that sets ownership on a peer, so the
        /// two machines cannot disagree: one enemy, one authority.
        /// </summary>
        public static void ReceiveEnemyOwnership(uint handle, bool ownedByMe)
        {
            if (IsHost)
                return; // the host is the one that decides; it never takes orders

            if (!FindIdOfHandle(handle, out string id))
            {
                ReportResolveFailure(handle);
                return;
            }

            EnemyState state = GetOrCreateState(id);
            state.OwnershipKnown = true;

            if (state.OwnedByLocal == ownedByMe)
            {
                // Ownership did not change. But a machine that already owns the enemy can have taken it
                // through the self-heal (see UpdateCore), which resumed it while _remoteTargets was still
                // empty after a LevelLoaded - so it resumed at the spawn point and started broadcasting
                // that stale position. This token is the host's confirmation of that ownership, and by now
                // the corrected position may have arrived; adopt it here so the stale resume does not
                // stick. For ownedByMe == false this is a genuine no-op (nothing to correct).
                if (ownedByMe)
                {
                    Enemy alreadyOwned = ResolveEnemy(id);
                    if (alreadyOwned != null)
                        AdoptLastKnownPosition(alreadyOwned, id);
                }
                return;
            }

            state.OwnedByLocal = ownedByMe;
            state.OwnerSince = Time.time;

            // ⭐ IL PADRONE E' CAMBIATO: la prossima ricezione riallinea la vita del boss (se e' un boss).
            // Qui e' il punto giusto e non `PauseEnemy`/`ResumeEnemy`: quelli vengono chiamati anche a ogni
            // tick di invio per i nemici che non sono nostri, e il latch si riaprirebbe venti volte al secondo.
            ForgetBossLifeAlignment(state);

            Enemy enemy = ResolveEnemy(id);
            if (enemy == null)
                return;

            // Take effect at once rather than waiting for the next send tick, which only runs 20 times a
            // second: an enemy that just lost ownership must stop being simulated immediately.
            if (ownedByMe)
            {
                AdoptLastKnownPosition(enemy, id);
                ResumeEnemy(enemy);
            }
            else
                PauseEnemy(enemy);
        }

        /// <summary>Called from Multiplayer.OnLateUpdate, on every machine.</summary>
        public static void Update()
        {
            // TEMPORARY (PerformanceProbe): times this method and counts the frame. The probe changes
            // no behaviour. The finally matters: this method returns from several places, and a
            // Stopwatch left running would poison the average.
            PerformanceProbe.BeginSync();
            try
            {
                UpdateCore();
            }
            finally
            {
                PerformanceProbe.EndSync();
            }
        }

        private static void UpdateCore()
        {
            // TEMPORARY (PerformanceProbe): one frame accounted for, and the Animator names read on the
            // first frame of each scene. Remove with the probe.
            PerformanceProbe.Frame();
            // Building the id table is needed on BOTH machines: the host sends indices into it and the
            // peer resolves indices against it. It used to live inside the host-only guard below, so a
            // peer never finalised its table and every incoming index was rejected as out of range
            // ("max -1" means an empty list). Only the broadcasting is host-only.
            _rescanTimer += Time.deltaTime;
            if (_rescanTimer >= RESCAN_INTERVAL)
            {
                _rescanTimer = 0f;

                if (!_spawnFinalized)
                {
                    // The spawn event never produced a table for this scene (it either fired before the
                    // scene name was known, or never fired at all). Finalise here instead.
                    OnEnemiesSpawned();
                }
                else
                {
                    // BOTH machines append later discoveries, not just the host.
                    //
                    // The old guard restricted this to the host so "the peer cannot invent ids the host
                    // does not have" - a fear from when the wire handle was an INDEX into the id list. It
                    // no longer is: the handle is a hash of the id string, so a missing or extra id cannot
                    // land an update on the wrong enemy, it can only make the fingerprint differ (which is
                    // reported). And the falò proved the cost of the restriction: it respawns enemies, the
                    // host re-registers them within half a second, and the peer never did - so the two
                    // machines held different lists until a scene change, which was the desync.
                    //
                    // RegisterEnemies is deterministic for spawn points (authored positions) and caches the
                    // id of a free enemy at its first registration, so scanning the same scene on both
                    // machines yields the same ids. The [EnemySync] "non in _ids" marker and IMPRONTA
                    // DIVERSA are the guard: if a machine genuinely has an enemy the other does not, the
                    // divergence is now VISIBLE instead of silently frozen.
                    RegisterEnemies(_scene);

                    // The scan above may have appended ids. Refresh the fingerprint now, so everything sent
                    // until the next scan describes the list that actually exists.
                    RefreshFingerprintIfDirty();
                }

                // Snapshot on BOTH machines, so the two logs can be lined up: same enemy, same moment,
                // what does each side think the aggro, ownership, animation and life are.
                LogDiagnosticSnapshot();
            }

            // Anything that changed the id list since the last pass - the host's scan above, or a pruned
            // enemy on either machine - has to be reflected before the next update goes out, or the
            // fingerprint sent would describe a list that no longer exists.
            RefreshFingerprintIfDirty();

            // Follow the remote snapshots smoothly. Runs before the send gate below, because that gate
            // returns early on most frames and the easing has to happen every frame to look smooth.
            InterpolateRemoteEnemies();

            // Settle any enemy sitting at zero health whose death never started. Runs every frame so the
            // grace period is measured in real time, and does nothing at all while everything is healthy.
            SettleStuckDeaths();

            // Every frame, so an attack shorter than the snapshot interval is still recorded. Writes only
            // when the state actually changes.
            LogAnimationChanges();

            // Every frame on BOTH machines, and deliberately before the connection gate below: the reveal of
            // an enemy is a fact about the world, not about the network, and the latch has to advance even
            // while nobody is connected or the first reveal after a reconnect would go unannounced.
            DetectReveals();

            // ② BOSSFIGHT|<scena> = OFF | IN_CORSO | FINITA. Stesso posto e stessa ragione di DetectReveals:
            // prima del gate della connessione, perche' la bossfight e' un fatto del MONDO (una stanza con
            // un boss dentro), non della rete - e in single player lo stato deve comunque esistere.
            // ⛔ Solo lettura e memoria: questa chiamata non cambia un solo comportamento (vedi il blocco
            // "IL TERZO VALORE DELLO STATO" piu' sopra).
            UpdateBossFightState();

            // Both machines receive: the owner to follow the authoritative state, the non-owner to apply
            // it. Receiving is what tells a machine whether it should be simulating a given enemy, so it
            // cannot be limited to the host.
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            if (_ids.Count == 0)
                return;

            _sendTimer += Time.deltaTime;
            if (_sendTimer < SEND_INTERVAL)
                return;
            _sendTimer = 0f;

            // Once per second every enemy is resent regardless of change, so a dropped packet cannot
            // leave a peer permanently diverged.
            bool fullSweep = Time.time - _lastSweep >= FULL_RESEND_INTERVAL;
            if (fullSweep)
                _lastSweep = Time.time;

            List<string> dead = null;

            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                uint handle = HandleOf(id);
                Enemy enemy = ResolveEnemy(id);

                if (enemy == null || enemy.gameObject == null)
                    continue; // not spawned yet, or already gone

                // ONE ENEMY MUST NOT BE ABLE TO STOP THE WHOLE PASS.
                //
                // This body used to be unprotected, and that cost a whole session its synchronisation:
                // a diagnostic inside it threw, the exception aborted the iteration, and everything after
                // it never ran - the update for that enemy AND for every enemy following it, the death
                // sweep, and on the host the ownership decision and its token. The send never completed,
                // the peer kept a frozen copy of every enemy, and the symptom looked like a desync bug
                // somewhere else entirely.
                //
                // A synchronisation loop has to be able to lose one enemy and carry on with the rest.
                try
                {

                    // DEATH IS CHECKED FIRST, BEFORE the activity check, and that order is the whole point.
                    //
                    // The game deactivates enemies that are off camera (Enemy.DisableBehaviourWhenInvisible,
                    // Enemy.ActivationRange), so a dead enemy outside the view has activeInHierarchy == false.
                    // With the activity check first, the loop skipped it and the death was never announced: the
                    // other player kept a live enemy - the "ghost" - until someone walked close enough to
                    // reactivate it, which is exactly when it disappeared. A dead enemy is dead regardless of
                    // whether it is currently being drawn.
                    if (enemy.Dead || enemy.CurrentLife <= 0f)
                    {
                        (dead ??= new List<string>()).Add(id);

                        // Recorded here, before the enemy is dropped from _ids. The snapshot runs only twice a
                        // second and looked for deaths among the tracked enemies, so by the time it ran the dead
                        // one had already been removed and no death was ever logged.
                        if (_killLogged.Add(id))
                        {
                            ModLog.Info($"[EnemyKill] {ShortId(id)} MORTO Dead={enemy.Dead} " +
                                        $"vita={enemy.CurrentLife:F1} attivo={enemy.gameObject.activeInHierarchy} " +
                                        $"pos=({enemy.transform.position.x:F1},{enemy.transform.position.y:F1})");
                        }
                        _killsSent++;
                        Main.Multiplayer.SendEnemyKill(handle);
                        // Also announce it scene-independently: the other player may be in another room and
                        // would otherwise never learn about this death.
                        Main.Multiplayer.SendEnemyDeathGlobal(id);

                        // ⭐ M1 — LA MORTE DEL BOSS DETTA IN MODO CHE IL MORTO POSSA APPLICARLA.
                        //
                        // L'annuncio qui sopra e' identico per tutti i nemici, e sul ricevente la morte si
                        // applica chiamando `<Tipo>::Damage(Hit)`: per un boss quella comincia con la
                        // guardia `if (Status.Dead || Core.Logic.Penitent.Stats.Life.Current <= 0f) return;`,
                        // che con il giocatore LOCALE a terra BUTTA VIA la morte - il boss resta in piedi e
                        // la sua cutscene non parte (misurato in R3-G1; vedi BossDeathPrefix).
                        // Il prefisso `bdead|` dice al ricevente "questa e' la morte di un BOSS: applicala
                        // con `Kill()`", che dalla guardia non passa (vedi ApplyBossDeath).
                        // ⚠️ E' UN ANNUNCIO IN PIU', NON UNO DI MENO: l'id generico resta spedito come
                        // prima, quindi niente di cio' che esisteva cambia comportamento.
                        if (IsBoss(enemy, id))
                        {
                            Main.Multiplayer.SendEnemyDeathGlobal(BossDeathPrefix + id);
                            ModLog.Info($"[BossDeath TX] {FullId(id)} boss={enemy.GetType().Name} " +
                                        $"vita={enemy.CurrentLife:F1} StatusDead={enemy.Status.Dead} " +
                                        $"localeMorto={PlayerDeathSync.IsLocalPlayerDead()}");

                            // ⭐⭐ L'AVVISO A SCHERMO, secondo punto di aggancio: QUESTA macchina ha appena
                            // visto morire il boss, quindi anche qui "il boss e' morto" e la cutscene e' alle
                            // porte. Il predicato (il giocatore locale e' morto?) lo verifica
                            // `BossDeathWarning` da solo, e il dedup per finestra impedisce che questo punto
                            // e `ApplyBossDeath` producano due notifiche per la stessa morte.
                            BossDeathWarning.WarnBossDied("tx:morte-del-boss-vista-su-questa-macchina");
                        }

                        // Remembered as well as announced: if someone reconnects later, this machine may no
                        // longer be in the room where the kill happened, and the announcement must not depend
                        // on that. Only reached while connected, because UpdateCore returns early otherwise.
                        _announcedDeaths.Add(id);
                        continue;
                    }

                    Vector2 position = enemy.transform.position;
                    float life = enemy.CurrentLife;
                    byte orientation = ReadOrientation(enemy);
                    int animState;
                    float animTime;
                    ReadAnimation(enemy, out animState, out animTime);

                    // Skip unchanged enemies to save bandwidth; the periodic full sweep above guarantees
                    // that everything is still resent regularly. TryGetValue, never an indexer: a freshly
                    // registered enemy has no previous record yet.
                    EnemyState previous;
                    bool hasPrevious = _lastSent.TryGetValue(id, out previous);

                    if (!hasPrevious)
                    {
                        previous = new EnemyState();
                        _lastSent[id] = previous;
                    }

                    // OWNERSHIP IS THE HOST'S, AND ONLY THE HOST'S.
                    //
                    // Previously each machine decided for itself from the distance it measured locally. Those
                    // measurements cannot agree - the two players hold slightly different positions, worst on
                    // slopes - so both machines could decide the closest player was theirs, simulate the same
                    // enemy, and publish two conflicting positions. The result in game was one enemy standing in
                    // two places, with aggro on both players at once.
                    //
                    // Now the host arbitrates and the peer obeys: the peer's OwnedByLocal is set only by the
                    // EnemyOwnership packet, never recalculated here.
                    if (IsHost)
                    {
                        DecideOwnership(enemy, id, previous, true);

                        if (!previous.OwnershipKnown || previous.LastPublishedOwnerIsMe != previous.OwnedByLocal)
                        {
                            previous.OwnershipKnown = true;
                            previous.LastPublishedOwnerIsMe = previous.OwnedByLocal;

                            // The wire carries "the owner is YOU, the receiver", which is the opposite of
                            // OwnedByLocal, where true means "I simulate it". The receiving side assigns the
                            // payload straight to its own OwnedByLocal:
                            //
                            //   ReceiveEnemyOwnership(ownedByMe) -> state.OwnedByLocal = ownedByMe
                            //
                            // and the parameter is named ownedByReceiver in NetworkManager and Multiplayer. So
                            // sending OwnedByLocal sent "you own it" precisely for the enemies the HOST owns: the
                            // peer then resumed the AI, transmitted, and skipped applying the received position -
                            // two owners on every contested enemy, the diverging positions seen in the logs. The
                            // negation is the whole fix.
                            Main.Multiplayer.SendEnemyOwnership(handle, !previous.OwnedByLocal);
                        }
                    }

                    // "Owned here" means this machine runs the game's own AI for this enemy, which is what
                    // produces real attacks and deaths. The other machine freezes it and applies state.
                    //
                    // BUT A PEER MUST NOT WAIT FOR AN ANSWER THAT CANNOT COME.
                    //
                    // The host arbitrates, and it can only arbitrate what it can see: _ids holds the enemies
                    // of ITS OWN scene. So when the two players are in different rooms, the enemies on this
                    // side are not in the host's list at all, it never decides on them, and the ownership
                    // token for them is never sent - and the server would not deliver it anyway, because
                    // ownership travels through relayToScene and is filtered by scene.
                    //
                    // OwnershipKnown is exactly "the host has never said anything about this one", so it is
                    // the right thing to test. Waiting for a token that cannot arrive left these enemies
                    // paused for the whole session: FreezePhysics switches off the controller, so they did
                    // not even get pushed around, and the measured cost was a WheelCarrier standing still for
                    // 134 samples (~67 s). Two players in different rooms is not an edge case, it is how the
                    // game is played - and getting it wrong is invisible in every test where both players
                    // walk in together.
                    //
                    // Taking it locally is safe because ownership is exclusive by construction: the moment
                    // the host does see the enemy it publishes a token, the sibling block above latches it,
                    // and the role is settled by the host from then on - with OWNER_HOLD_SECONDS absorbing
                    // the handover. Nothing else claims this enemy in the meantime, because the host has no
                    // record of it to send.
                    if (previous.OwnedByLocal)
                    {
                        ResumeEnemy(enemy);
                        ApplyAggroTarget(enemy, id);
                    }
                    else if (!IsHost && !previous.OwnershipKnown)
                    {
                        previous.OwnedByLocal = true;

                        // ⭐ Auto-risoluzione: sono diventato padrone. Il latch della vita del boss si riapre,
                        // perche' se questo boss mi venisse tolto la vita va riallineata a chi lo prende.
                        ForgetBossLifeAlignment(previous);

                        AdoptLastKnownPosition(enemy, id);
                        ResumeEnemy(enemy);
                        ApplyAggroTarget(enemy, id);
                    }
                    else
                    {
                        PauseEnemy(enemy);
                    }

                    // The peer obeys the host, so it transmits exactly the enemies the host granted it.
                    if (!previous.OwnedByLocal)
                        continue;

                    // Position and life are transmitted for an enemy that is tracked and owned, whether or not
                    // it is currently ACTIVE - and that reversed a real decision.
                    //
                    // There used to be a gate here: `if (!enemy.gameObject.activeInHierarchy) continue;`,
                    // justified by a comment claiming the game never deactivates an enemy for being off
                    // camera. The comment above this method says the opposite, and the logs settle it: the
                    // enemies that failed to transfer were the ones OUTSIDE the view. Whatever the reason an
                    // enemy is inactive - waiting for its cutscene, or simply off camera behind the owner -
                    // its TRANSFORM still holds the truth, and that truth is exactly what the other machine
                    // needs at a handover. Skipping it there is what left the other machine with no position
                    // at all, so it fell back to its own copy, which is the spawn point.
                    //
                    // An enemy that is not in play yet has a position too - its spawn - so transmitting it
                    // costs a packet and tells the truth.

                    if (!fullSweep && hasPrevious)
                    {
                        bool changed = Mathf.Abs(previous.Position.x - position.x) > POSITION_EPSILON ||
                                       Mathf.Abs(previous.Position.y - position.y) > POSITION_EPSILON ||
                                       Mathf.Abs(previous.Life - life) > LIFE_EPSILON ||
                                       previous.AnimState != animState;

                        if (!changed)
                            continue;
                    }

                    // The playback position is passed on as it was read, INCLUDING when the state changes.
                    //
                    // This line used to force animTime = 0f on every state change, on the reasoning that a
                    // playback position only matters at a transition. It is the exact opposite: a transition
                    // is the only moment the receiver needs it. Sending zero there made ApplyAnimation call
                    // Animator.Play(hash, 0, 0) - so every transition restarted the clip from the beginning.
                    // On a one-shot (an attack, a death animation) that shows as the animation starting and
                    // being cut off after a few milliseconds; on a loop it restarts the walk.
                    //
                    // Measured before removing it: the same transition, for the same enemy, repeated 33 and
                    // 58 times in one session at about 6 restarts per second, in healthy frames at 59 FPS.
                    // That repetition - not a frame stall, which happens 0/3 and 1/11 of the time alongside a
                    // cut - is the signature of this line.

                    previous.Position = position;
                    previous.Life = life;
                    previous.AnimState = animState;
                    previous.Orientation = orientation;
                    previous.LastSweep = Time.time;

                    // ONLY THE OWNER TRANSMITS. The host used to broadcast every enemy, including the ones the
                    // peer was simulating, so both machines published a position for the same enemy and the two
                    // values fought each other - visible as a small desync whenever the non-owner interacted
                    // with it. One enemy now has exactly one source of position.
                    if (!previous.OwnedByLocal)
                        continue;

                    _updatesSent++;
                    Main.Multiplayer.SendEnemyUpdate(handle, position, life, animState, animTime, _listFingerprint, previous.OwnedByLocal, orientation);
                }
                catch (Exception e)
                {
                    _syncLoopExceptions++;
                    if (_syncLoopExceptions <= 20)
                        ModLog.Error($"[EnemySync] eccezione sul nemico {ShortId(id)}: {e.GetType().Name} - " +
                                     $"{e.Message} (nemico saltato, il resto della passata continua)");
                    goto continueEnemy;
                }

            continueEnemy:;
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    ForgetEnemy(dead[i]);
            }
        }

        /// <summary>
        /// Drops a dead enemy completely: from the lookup AND from the id list.
        ///
        /// Removing it only from the lookup left the id in _ids, where ResolveEnemy falls back to the
        /// spawn point and hands the same dead enemy back - so it was found dead again on every tick and
        /// the death was rebroadcast forever (1008 kill messages in one session).
        ///
        /// Dropping the id itself is safe now that the wire carries a content-derived handle: indices no
        /// longer mean anything, so the list may shrink or reorder without disturbing a peer.
        /// </summary>
        private static void ForgetEnemy(string id)
        {
            // Look the enemy up FIRST: after the removal the lookup is gone, and then it could never be
            // dropped from the registered set, so a later respawn would be treated as a new enemy.
            Enemy enemy;
            _enemies.TryGetValue(id, out enemy);

            _enemies.Remove(id);
            _ids.Remove(id);
            _lastSent.Remove(id);
            _remoteTargets.Remove(id);

            // Pruning shifts every index after the one removed, and the index IS the wire handle - so this
            // is not a tidy-up, it invalidates handles the peer may still be using. The fingerprint is
            // refreshed so the divergence is at least reported instead of staying hidden.
            _idsDirty = true;

            if (enemy != null)
            {
                _registered.Remove(enemy);

                // The instance keeps its id on purpose, even though it is no longer tracked: the object can
                // outlive its tracking for a moment while it dies, and the next scan would otherwise hand it
                // a NEW id and register it again as a fresh enemy. That is exactly what turned 6 enemies into
                // 25 registrations and produced the duplicates in game. The map is cleared on a scene change,
                // which is the only moment instance identity genuinely stops being meaningful.
            }
        }

        /// <summary>Position, health and animation state broadcast sent by the host.</summary>
        public static void ReceiveEnemyUpdate(uint handle, Vector2 position, float currentLife, int animState, float animTime, int fingerprint, bool ownedRemotely, byte orientation)
        {
            _updatesReceived++;

            // Detect the one failure that would otherwise corrupt things silently: if the two machines
            // built different id lists, every index means something different and updates land on the
            // wrong enemy. Report it, with enough detail to diagnose it.
            //
            // Reported again after the cooldown rather than once per level load. The once-only rule looked
            // safe but was not: a mismatch seen while the id list was still being built would be wrong AND
            // would silence every real mismatch for the rest of the scene, which is the opposite of what a
            // detector is for. The cooldown keeps a genuine divergence visible without flooding the log.
            if (fingerprint != _listFingerprint && Time.time - _fingerprintMismatchReported > FINGERPRINT_REPORT_COOLDOWN)
            {
                _fingerprintMismatchReported = Time.time;
                _fingerprintMismatches++;
                ModLog.Error($"[EnemySync] IMPRONTA DIVERSA: host={fingerprint} locale={_listFingerprint} " +
                             $"- le due macchine hanno liste di nemici diverse, gli aggiornamenti finiranno " +
                             $"sul nemico sbagliato. Confronta 'multiplayer sync dump' sui due PC " +
                             $"(spawn point locali: {_spawnPoints.Count}, id locali: {_ids.Count}, " +
                             $"segnalazioni: {_fingerprintMismatches}).");
            }

            Enemy enemy = ResolveByHandle(handle);
            if (enemy == null)
            {
                ReportResolveFailure(handle);
                return;
            }

            // Record the sender's claim. This is the input the arbitrating host was missing: without it
            // the host never learned that the peer was simulating an enemy, so it kept simulating too and
            // the two positions fought each other.
            //
            // The state is fetched out here rather than inside the branch because the ownership decision
            // below has to write to the same record the send loop reads. When the id cannot be resolved the
            // decision is skipped: an update that cannot be attributed to a known enemy must not change who
            // owns anything.
            EnemyState claimState = null;
            if (FindIdOfHandle(handle, out string claimedId))
            {
                claimState = GetOrCreateState(claimedId);
                claimState.OwnedByRemote = ownedRemotely;
                if (ownedRemotely)
                    claimState.RemoteEverClaimed = true;
            }

            // Ownership decides what happens here. If the sender owns this enemy, this machine must NOT
            // simulate it: pause the behaviour tree and follow the state being sent.
            //
            // PauseBehaviour stops only the behaviour tree, never the Animator, so idle and walking keep
            // animating locally and hit reactions still show through the synced health. What a paused enemy
            // cannot do is decide to attack - which is why the owner runs the real AI.
            //
            // THE PAUSE IS NEVER UNDONE HERE ON THE STRENGTH OF WHAT THIS MACHINE BELIEVED. The record is
            // set from what the SENDER declared, in both directions. It used to be left alone, and this
            // method reads ownedRemotely from the packet while reading OwnedByLocal from a record it never
            // updated - so a machine that considered itself the owner paused the enemy and resumed it again
            // inside the same packet, twenty times a second, on both machines, on the same enemy. The
            // record and the packet disagreed permanently about who was simulating it.
            if (ownedRemotely)
            {
                // The record IS written here, and it is not a redundancy.
                //
                // An update packet is asymmetric: only the owner transmits, and it always declares
                // ownedRemotely = true. So this flag carries no NEW INFORMATION. That is true, and it is why
                // the write was removed once, reasoning that it could not matter. It mattered. This is not an
                // information channel, it is an ACTUATOR: it decides who loses the send gate
                // ("if (!previous.OwnedByLocal) continue;"), who the send loop ends up pausing, and which
                // side the ownership token starts from.
                //
                // With the write in place, ownership changes measured 0.04 per second; without it, 0.17 -
                // four times as much churn. And the windows where an enemy stood still next to a player went
                // from 22% of the engaged time to 59-61%. Removing a write because it "carries no
                // information" ignores that its effect was on control flow.
                if (claimState != null)
                {
                    // ⭐ Se il record diceva ancora "mio", QUESTO pacchetto e' l'istante in cui il boss e'
                    // passato di mano: la vita che arriva da qui in poi e' quella del nuovo padrone, e va
                    // presa una volta (vedi AlignBossLife). Il confronto va fatto PRIMA della scrittura,
                    // perche' dopo `OwnedByLocal` e' false comunque e il cambio non sarebbe piu' visibile.
                    if (claimState.OwnedByLocal)
                        ForgetBossLifeAlignment(claimState);

                    claimState.OwnedByLocal = false;
                    claimState.OwnerSince = Time.time;
                }

                PauseEnemy(enemy, "remote owns");

                // The state is still applied on the non-owner. PauseBehaviour leaves the Animator running,
                // so idle/walk animate locally anyway, but anything the behaviour tree would have triggered
                // - attacks, hurt, death - has to come from here. This is deliberately narrow: only the
                // state is forced, never the whole animation graph.
                ApplyAnimation(enemy, animState, animTime);

                // Facing is part of the state, not a decoration: the game only calls SetOrientation from
                // the behaviour tree, which is paused here, so without this the enemy would keep facing the
                // wrong way and its walking animation would look mirrored.
                ApplyOrientation(enemy, orientation);
            }
            else
            {
                // The sender says this machine owns the enemy: let its own AI drive it, so animations come
                // from the game's per-enemy code instead of being forced from outside.
                //
                // Setting OwnedByLocal is the other half of the fix above, and it is not optional: without
                // it an enemy the host hands BACK would stay marked as not ours, and the send loop skips
                // anything that is not owned locally - so this machine would never simulate it again.
                // This is the ONLY place the packet tells this machine something it did not already know.
                if (claimState != null)
                {
                    claimState.OwnedByLocal = true;
                    claimState.OwnerSince = Time.time;

                    // ⭐ Sono diventato io il padrone: il latch si riapre, cosi' se lo perdo di nuovo la vita
                    // viene riallineata a chi lo prende.
                    ForgetBossLifeAlignment(claimState);
                }

                ResumeEnemy(enemy);
            }

            // A BOSS keeps its own health: only position, animation and facing are shared. See _bossIds for
            // why writing a boss's health to zero leaves it standing with an empty bar and the room stuck.
            bool isBoss = IsBoss(enemy, IdOf(enemy));

            if (!isBoss)
            {
                // The life is written with the game's own setter, never assigned to the field.
                //
                // Entity.CurrentLife is a property over Stats.Life.Current, and Entity.SetHealth(float) is
                // what the game itself uses: it writes the same value and, when it lands at or below zero,
                // calls KillInstanteneously() for us. Assigning the field instead left the enemy at zero
                // health with Get_Dead() already true - because Entity.Dead is COMPUTED as
                // !(Life.Current > 0) - but with the death sequence never started.
                enemy.SetHealth(currentLife);
            }
            else if (ownedRemotely)
            {
                // ⭐⭐ E IL BOSS, ALLORA? La vita viaggia in OGNI update anche per lui, e fino a qui veniva
                // BUTTATA (`if (!isBoss)`): due pool di vita locali, quindi due barre, e lo scarto misurato
                // e' di **110 HP su 400 (27,5%)** costante per tutto il fight.
                // ■ Il canale: **non ne serve uno nuovo**. `currentLife` e' gia' nella ricezione, e chi manda
                //   e' il padrone: nessun tipo di pacchetto, nessuna patch al server, nessun protocollo.
                // ■ Il quando: **non a ogni pacchetto**. Una scrittura sola per situazione, all'ingresso in
                //   scena e a ogni cambio di padrone (`EnemyState.BossLifeAligned`, riaperto da
                //   `ForgetBossLifeAlignment`). Il resto lo fa il relay del danno, che gia' funziona.
                // ■ Il divieto: **mai 0** (pavimento a 1 HP), **mai su un boss morto**, **mai in salita**.
                //   Tutto dentro `AlignBossLife`, con la ragione scritta la'.
                // ⚠️ Solo quando il MITTENTE dichiara di possedere il nemico: se dice "il padrone sei tu", la
                // sua vita non e' un riferimento e non si tocca niente.
                AlignBossLife(enemy, claimedId, claimState, currentLife);
            }

            // Record the snapshot as a TARGET. The position is applied smoothly in Update, once per frame,
            // instead of being snapped here 20 times a second.
            if (FindIdOfHandle(handle, out string targetId))
            {
                _remoteTargets[targetId] = position;
                // PUNTO 0: la voce dice anche QUANDO e' stata scritta. Una voce di questa visita e' il
                // risultato di una simulazione in corso; una voce piu' vecchia e' solo un residuo, e per un
                // boss non e' una posizione da applicare (`SkipBossFollow`).
                _remoteTargetsVisit[targetId] = _sceneVisit;
            }

            // Death is treated as a CONSEQUENCE OF HEALTH, not only as its own packet. The death packet
            // proved unreliable in game - the peer kept showing a frozen enemy with full life because the
            // kill never landed - and health already travels in every update, so a zero can be acted on
            // immediately and cannot be missed.
            if (!isBoss && !IsHost && (currentLife <= 0f || enemy.Status.Dead))
            {
                KillEnemyLocally(enemy, handle);
            }
        }

        /// <summary>
        /// Runs the native death sequence on an enemy whose health reached zero, then stops tracking it.
        ///
        /// The native path is used rather than activating the object off, so the game plays its own death
        /// animation and handles whatever the enemy drops.
        /// </summary>
        /// <summary>
        /// Settles enemies stuck at zero health with no death sequence running, after a grace period.
        ///
        /// Step one is the game's own KillInstanteneously(), which is public, sets EntityStatus.Dead, zeroes
        /// the health and fires the death events the rest of the level listens to (OnDeath, OnEntityDeath,
        /// GenericEntityDead). It does not touch the Animator, so an enemy whose type triggers its death
        /// animation from somewhere the mod cannot reach may still not animate - but it does die properly,
        /// which is what matters for the room and for the drop.
        ///
        /// Step two only runs if even that did not take: the object is removed from the scene outright, so
        /// nothing inert is left standing. Destroying skips the death events, which is why it is the last
        /// resort and not the first.
        /// </summary>
        private static void SettleStuckDeaths()
        {
            for (int i = _ids.Count - 1; i >= 0; i--)
            {
                string id = _ids[i];
                Enemy enemy = ResolveEnemy(id);
                if (enemy == null || enemy.gameObject == null)
                    continue;

                bool healthGone = enemy.CurrentLife <= 0f;
                if (!healthGone || enemy.Status.Dead)
                {
                    _deathPendingSince.Remove(id);
                    continue;
                }

                float since;
                if (!_deathPendingSince.TryGetValue(id, out since))
                {
                    _deathPendingSince[id] = Time.time;
                    continue; // start the clock; the game still has the grace period to do it itself
                }

                if (Time.time - since < DEATH_GRACE_SECONDS)
                    continue;

                _deathPendingSince.Remove(id);

                if (_killLogged.Add(id))
                {
                    ModLog.Info($"[EnemyDeath] {ShortId(id)} a vita zero da {DEATH_GRACE_SECONDS:F1}s senza " +
                                $"sequenza di morte: la forzo con KillInstanteneously");
                }

                enemy.KillInstanteneously();

                if (!enemy.Status.Dead)
                {
                    // Even the native kill did not take: take it out of the scene so it cannot be found,
                    // hit, or block anything. The spawn point is marked so the room does not resurrect it.
                    //
                    // ⚠️ THIS Destroy IS THE SAME HAZARD AS THE ONE REMOVED FROM DestroyDeadEnemy, and it is
                    // left here only because it has never been observed to run: `[EnemyDeath]` is 0 in every
                    // session measured so far. If it ever fires it can invalidate the same game references -
                    // Execution.ExecutedEntity and the prop of the execution interactable - and produce the
                    // same dead session: unhandled exceptions out of StuntBehaviour, and the EXECUTION input
                    // blocker left on with no key that responds. The first time this line appears in a log it
                    // should become SetActive(false), like the other one.
                    ModLog.Info($"[EnemyDeath] {ShortId(id)} resiste anche a KillInstanteneously: lo rimuovo");
                    MarkSpawnPointConsumed(id);
                    UnityEngine.Object.Destroy(enemy.gameObject);
                    ForgetEnemy(id);
                }
            }
        }

        private static void KillEnemyLocally(Enemy enemy, uint handle)
        {            if (enemy == null)
                return;

            // The guard asks whether the DEATH SEQUENCE has started, not whether the health is at zero.
            //
            // Entity.Dead is computed as !(Stats.Life.Current > 0), so it turns true the instant the health
            // is written to zero - including by this mod while syncing. Guarding on it meant the call below
            // was never reached: the peer learned an enemy had died, saw Dead already true, and did nothing,
            // so no Death trigger was ever set. The enemy stayed on its feet at zero health, its damage
            // collider was never switched off, and it could not be finished off either.
            //
            // EntityStatus.Dead is the separate bool that KillInstanteneously() sets, so it is the honest
            // answer to "has the game begun killing this enemy".
            if (!enemy.Status.Dead)
            {
                // Through the game's own damage entry point, so the hurt flash, the sound and - for the
                // many enemy types that set their DEATH trigger there - the death animation all follow the
                // normal flow instead of being skipped.
                //
                // Normal/Normal and no flags: this is the killing blow this mod applies to settle an enemy
                // whose death was agreed, not a blow somebody struck. Giving it a real damage type would be
                // inventing information, so it keeps the neutral one.
                ApplyDamage(enemy, enemy.CurrentLife + 1f, (byte)DamageArea.DamageType.Normal,
                            (byte)DamageArea.DamageElement.Normal, 0);
            }

            if (enemy.Dead || enemy.CurrentLife <= 0f)
            {
                for (int i = 0; i < _ids.Count; i++)
                {
                    if (HandleOf(_ids[i]) == handle)
                    {
                        ForgetEnemy(_ids[i]);
                        break;
                    }
                }

                // Deactivate the corpse, and this is the other half of the voice-54 fix.
                //
                // The announcement channel (DestroyDeadEnemy) deactivates via SetActive(false), but it can
                // only do so while the enemy is still in _ids - and this health channel has just forgotten it.
                // So a peer that receives the death through the synced health (the normal case) removed the id
                // here, the announcement then found nothing to deactivate, and the corpse stayed visible,
                // untargetable, with its AI off: the "ghost". On the peer the death animation never plays on
                // its own because the behaviour tree is paused, so there is nothing to wait for - deactivating
                // immediately is the correct end, and the host's own copy still plays its death normally.
                //
                // ForgetEnemy has already run, so this cannot race the announcement channel anymore.
                if (enemy != null && enemy.gameObject != null && enemy.gameObject.activeSelf)
                    enemy.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Logs every animation change, per enemy, exactly once.
        ///
        /// Run every frame: the game devotes a separate behaviour class per enemy to driving its Animator,
        /// with no common entry point, so the Animator's current state is the only dependable signal. A
        /// change is written only when the state hash actually differs, which keeps an idle enemy silent.
        ///
        /// Its purpose is comparing machines: the same transition should appear on both sides at
        /// approximately the same moment, and a transition on one side only is a synchronisation gap.
        /// </summary>
        private static void LogAnimationChanges()
        {
            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                Enemy enemy = ResolveEnemy(id);
                if (enemy == null)
                    continue;

                Animator animator = AnimatorOf(enemy);
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;

                int stateHash;
                float animTime;
                ReadAnimation(enemy, out stateHash, out animTime);
                if (stateHash == 0)
                    continue; // no state yet: nothing meaningful to report

                int previous;
                bool known = _lastAnim.TryGetValue(id, out previous);

                // Was the transition about to be reported OUR doing?
                //
                // The check below compares _appliedAnim - what we last imposed - against the state being read
                // now, and it is not enough on its own: ApplyAnimation writes _appliedAnim during the frame
                // that imposes the state, while the Animator still reports the OLD state until the controller
                // evaluates, one frame later. So on the frame of the imposition the comparison fails, a line
                // is written that describes our own forcing, and _lastAnim is overwritten with the wrong
                // value - which is what produced repeated "phantom" transitions in the log, including the
                // same pair printed many times in a row, which the guard below makes impossible otherwise.
                //
                // _appliedAnimPrev closes that gap without waiting a frame: it records the pair
                // (state the Animator showed, state we imposed), so the echo is recognised by its ORIGIN.
                int appliedPrev;
                bool isEcho = _appliedAnimPrev.TryGetValue(id, out appliedPrev) && appliedPrev == previous;

                // A state we applied ourselves is not a local change: reporting it would describe our own
                // forcing as if the game had animated something, and that is what produced hundreds of
                // transitions for a single event.
                int applied;
                if ((_appliedAnim.TryGetValue(id, out applied) && applied == stateHash) || isEcho)
                {
                    _lastAnim[id] = stateHash;
                    _lastAnimSample[id] = animTime;
                    continue;
                }

                // How long the state we are leaving actually lasted, and how far into its clip it got.
                //
                // Only transitions the game made count towards this: a timer reset by our own echo would
                // report a long-lived state as if it had just started. And "uscita" - the playback position
                // the leaving state had reached - is the measurement that settles the question the counts
                // could not: a one-shot interrupted near zero was cut, one that reached the end simply ended.
                // It is read before the sample is overwritten, and it is local to this machine, so the two
                // logs can be compared without aligning anything.
                float lastSample;
                _lastAnimSample.TryGetValue(id, out lastSample);

                float changedAt;
                bool hadChangedAt = _lastAnimChangeAt.TryGetValue(id, out changedAt);
                float durata = hadChangedAt ? Time.time - changedAt : 0f;

                _lastAnim[id] = stateHash;
                _lastAnimSample[id] = animTime;
                _lastAnimChangeAt[id] = Time.time;

                if (known && previous == stateHash)
                    continue;

                Vector3 p = enemy.transform.position;
                ModLog.Info($"[EnemyAnim] {ShortId(id)} {previous} -> {stateHash} " +
                            $"avvio={animTime:F2} uscita={lastSample:F2} durata={durata:F2} appl=gioco " +
                            $"vita={enemy.CurrentLife:F0} " +
                            $"pos=({p.x:F1},{p.y:F1}) " +
                            $"mia={(IsHost ? "host" : "client")}");

                // TEMPORARY (PerformanceProbe): resolve the two hashes to names, once each per session.
                // The hashes cannot be reversed from a file - they are FNV-1a of names that live inside the
                // packed AnimatorController - so the running Animator has to be asked. Knowing whether the
                // state entered hundreds of times is an attack or an idle is what decides whether this is a
                // defect at all. Remove with the probe.
                NameStateIfNew(animator, stateHash, LabelOf(enemy));

                // TEMPORARY (PerformanceProbe): counts the repetitions that identify a forced replay.
                // A legitimate transition happens once; the same pair repeating for the same enemy is
                // this mod restarting the clip again.
                //
                // Keyed by POSITION, not by ShortId: ShortId carries the position already but can gain a
                // suffix when two spawn points share a spot, and the report has to be comparable with the
                // orientation tallies. A total was not enough to act on - counting the transitions without
                // distinguishing the enemies made one transition look like one enemy transitioning 386
                // times, when it may have been several enemies doing it dozens of times each. Remove with
                // the probe.
                if (animTime <= 0.01f)
                    PerformanceProbe.NoteAnimationRestart(LabelOf(enemy), previous, stateHash);
            }
        }

        /// <summary>Which way the enemy faces, as a byte: EntityOrientation Right = 0, Left = 1.</summary>
        private static byte ReadOrientation(Enemy enemy)
        {
            EntityStatus status = enemy.Status;
            if (status == null)
                return 0;

            return (byte)status.Orientation;
        }

        /// <summary>
        /// Applies the owner's facing to a non-owned enemy.
        ///
        /// SetOrientation is the game's own single entry point for this: it stores the value and flips the
        /// sprite. Using the field directly would leave SpriteRenderer.flipX out of step, so the enemy would
        /// face one way logically and the other way on screen. passFlip is false because the game already
        /// handles the flip inside, given the previous orientation was kept in sync.
        /// </summary>
        /// <summary>
        /// Applies the facing the owner transmitted.
        ///
        /// No hysteresis here any more, and that is deliberate. A band was tried here first and did nothing:
        /// this method is the mod's own write, twenty per second, while the churn is the GAME's sixty writes
        /// per second from Ghost's OnUpdate - which never comes through here. The measurement said so
        /// outright: orientSkip stayed at zero for a whole session while orientFlip hit 34 in one two second
        /// window. The band now lives in OrientationProbePatch, on Entity.SetOrientation, which every writer
        /// goes through.
        ///
        /// SetOrientation is still the game's own single entry point: it stores the value and flips the
        /// sprite. Writing the field directly would leave SpriteRenderer.flipX out of step, so the enemy
        /// would face one way logically and the other on screen. passFlip is true because the game handles
        /// the flip inside, given the previous orientation was kept in sync.
        /// </summary>
        private static void ApplyOrientation(Enemy enemy, byte orientation)
        {
            EntityStatus status = enemy.Status;
            if (status == null)
                return;

            EntityOrientation wanted = orientation == (byte)EntityOrientation.Left
                ? EntityOrientation.Left
                : EntityOrientation.Right;

            if (status.Orientation == wanted)
                return;

            // TEMPORARY (PerformanceProbe): one applied flip. Nothing else in the log measures orientation,
            // so without this the spinning-enemy fix could only be judged by eye. Remove with the probe.
            PerformanceProbe.NoteOrientationFlip(IdOf(enemy));

            enemy.SetOrientation(wanted, true, true);
        }

        /// <summary>
        /// Reports the clip name behind a state hash, once per hash per session.
        ///
        /// TEMPORARY (PerformanceProbe): the hash cannot be reversed from outside the game. It is FNV-1a of
        /// the state name, but those names live in the AnimatorController, which is a packed Unity asset:
        /// hashing every string in Assembly-CSharp produced no match for any of the three hashes seen in the
        /// churn. Asking the Animator for the clip it is playing sidesteps the whole problem, and the answer
        /// decides whether hundreds of entries into one state are hundreds of attacks (nothing wrong) or an
        /// idle being re-entered (something wrong). Remove with the probe.
        /// </summary>
        private static void NameStateIfNew(Animator animator, int stateHash, string enemyLabel)
        {
            if (animator == null || stateHash == 0)
                return;

            AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length == 0 || clips[0].clip == null)
                return;

            PerformanceProbe.NameAnimationState(stateHash, clips[0].clip.name, enemyLabel);
        }

        private static void ReadAnimation(Enemy enemy, out int stateHash, out float normalizedTime)
        {
            stateHash = 0;
            normalizedTime = 0f;

            Animator animator = AnimatorOf(enemy);
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            stateHash = state.shortNameHash;
            normalizedTime = state.normalizedTime;
        }

        /// <summary>
        /// The two locomotion states, as the game hashes them, and the parameter that drives them.
        ///
        /// The hashes are written as literals on purpose: they were read from the running game
        /// ([AnimName]) and resolved to names by inverting the game's own hash, and reproducing them by
        /// calling a hash function here would be a second implementation that could disagree with the
        /// first. The literal cannot.
        /// </summary>
        private const int ANIM_STATE_WALK = 765711723;   // "Walk"
        private const int ANIM_STATE_IDLE = 2081823275;  // "Idle"

        /// <summary>
        /// The bool the controller actually uses to choose between walking and standing still.
        ///
        /// Walk and Idle are NOT selected by playing a state: the controller selects them from this
        /// parameter, which the enemy's own behaviour sets (FoolAnimatorInyector.Walk calls
        /// SetBool("WALK", true), and FoolBehaviour.Chase is its only caller). On this machine the
        /// behaviour tree is paused, so nothing ever sets it - which is the whole problem below.
        /// </summary>
        private static readonly int WALK_PARAM = Animator.StringToHash("WALK");

        /// <summary>
        /// Applies the owner's animation state to an enemy this machine does not simulate.
        ///
        /// LOCOMOTION GOES THROUGH THE PARAMETER, NOT THROUGH Play. This is the fix for the enemy that
        /// "slides": its legs restart while its body advances.
        ///
        /// Play("Walk") sets the state for one frame, but the controller CHOOSES the state from the WALK
        /// bool on every frame - and WALK is false here, because the only thing that raises it is
        /// FoolBehaviour.Chase inside the behaviour tree that PauseEnemy has stopped. So the controller
        /// pulled the enemy back to Idle on the very next frame, the next packet forced Walk again from
        /// zero, and the two fought at twenty against sixty hertz. Measured: 250 forced Walk->Idle against
        /// 2 the other way in one session, with the same pair repeated up to 31 times in two seconds.
        ///
        /// Setting the bool is what the game itself does, so the controller performs an ordinary
        /// transition - and an ordinary transition PRESERVES THE CLIP PHASE, which is exactly what was
        /// being lost. It is the same idea as the position handling: give the simulation a goal and let it
        /// get there, rather than imposing a state it will immediately undo.
        ///
        /// One-shot states (attack, death, turn) keep using Play, because they are not driven by a bool and
        /// nothing will undo them.
        /// </summary>
        private static void ApplyAnimation(Enemy enemy, int stateHash, float normalizedTime)
        {
            if (stateHash == 0)
                return;

            Animator animator = AnimatorOf(enemy);
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
                return; // Already in that state; replaying it would restart the clip constantly.

            // Remembered so the animation log does not mistake our own forcing for a local change, which is
            // what turned one transition into hundreds of them in the peer's log.
            _appliedAnim[IdOf(enemy)] = stateHash;

            // The pair (state the Animator showed BEFORE this call, state we are imposing). Read before the
            // Animator can evaluate, so it describes the transition we caused - see the echo check in
            // LogAnimationChanges, which cannot rely on _appliedAnim alone because the Animator reports the
            // old state for one more frame. Remove with the probe.
            _appliedAnimPrev[IdOf(enemy)] = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;

            if (stateHash == ANIM_STATE_WALK || stateHash == ANIM_STATE_IDLE)
            {
                // Locomotion: hand the decision to the controller and let it transition, so the clip keeps
                // its phase. This is what the game does, and it is why the legs stop restarting.
                animator.SetBool(WALK_PARAM, stateHash == ANIM_STATE_WALK);
                return;
            }

            // A one-shot. The playback position matters here, so it is passed on - but wrapped into [0,1)
            // first: on a looping clip normalizedTime grows past 1, and the old test for "between 0 and 1"
            // sent every later frame to Play(hash, 0, 0), restarting the clip from the beginning. That was
            // the second half of the sliding, and it hit states that were otherwise being applied
            // correctly.
            float time = normalizedTime - Mathf.Floor(normalizedTime);
            animator.Play(stateHash, 0, time);
        }

        private static Animator AnimatorOf(Enemy enemy)
        {
            if (enemy == null)
                return null;

            return enemy.Animator != null ? enemy.Animator : enemy.GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// Damage approved by the host. The machine that swung the sword already applied it natively
        /// (either it is the host, or it is the peer that is then echoed by the server to the others),
        /// so this only ever runs on machines that did not swing.
        /// </summary>
        public static void ReceiveEnemyDamage(uint handle, float damageAmount, byte damageType,
                                              byte damageElement, byte flags)
        {
            Enemy enemy = ResolveByHandle(handle);
            if (enemy == null)
            {
                ReportResolveFailure(handle);
                return;
            }

            // If the peer that swung also landed the killing blow, its local application already killed
            // the enemy here. Applying the relayed damage to that corpse would run the death sequence a
            // second time (double purge/tears credit). The host's next update resyncs the exact health,
            // so skipping is safe.
            //
            // The test is Status.Dead, the "death sequence already started" flag, and NOT Entity.Dead.
            // Entity.Dead is true as soon as the health reads zero, so this guard used to swallow every
            // relayed hit on an enemy sitting at zero health whose death had not actually begun - which is
            // precisely the state this whole path exists to fix: no damage, no hurt flash, no sound, and
            // no death animation on the machine that was not swinging.
            if (enemy.Status.Dead)
                return;

            _damageApplied++;
            ApplyDamage(enemy, damageAmount, damageType, damageElement, flags);
        }

        /// <summary>Called by the damage patch when a local hit is forwarded instead of applied.</summary>
        public static void RecordDamageSent()
        {
            _damageSent++;
        }

        /// <summary>
        /// Packs the three booleans of a Hit that change how the receiving machine reacts to it.
        ///
        /// Only the ones that actually alter the outcome travel: DontSpawnBlood suppresses the blood spray,
        /// and the parry/block flags decide whether the enemy treats the blow as guarded. Everything else in
        /// the struct is either recomputed locally or irrelevant on the machine that is not swinging.
        ///
        /// NoInlining is deliberate: the method takes a Hit by VALUE, and without it the JIT is free to
        /// inline the call and then touch a value type belonging to a game assembly at a moment this mod does
        /// not control. Keeping a real call frame is the cheap way to stay out of trouble.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static byte HitFlagsFrom(Hit hit)
        {
            byte flags = 0;

            if (hit.DontSpawnBlood)
                flags |= 1;

            if (hit.Unparriable)
                flags |= 2;

            if (hit.Unblockable)
                flags |= 4;

            return flags;
        }

        /// <summary>Bit 0 of the damage flags: the hit must not produce blood.</summary>
        private const byte HIT_FLAG_DONT_SPAWN_BLOOD = 1;

        /// <summary>Bit 1 of the damage flags: the hit cannot be parried.</summary>
        private const byte HIT_FLAG_UNPARRIABLE = 2;

        /// <summary>Bit 2 of the damage flags: the hit cannot be blocked.</summary>
        private const byte HIT_FLAG_UNBLOCKABLE = 4;

        /// <summary>
        /// Called by the damage patch on the host, once per hit it applies itself, so the diagnostic
        /// count reflects damage the host originated rather than damage relayed to it.
        /// </summary>
        public static void RecordDamageApplied()
        {
            _damageApplied++;
        }

        /// <summary>
        /// Records a death announced from another scene, and applies it here.
        ///
        /// The important part is MarkSpawnPointConsumed: it registers the spawn point in the GAME's own
        /// consumed list, which CreateEnemy already consults before creating anything. That is what stops
        /// the ghost from coming back on the next scene reload, without us having to intercept anything.
        /// </summary>
        public static void ReceiveEnemyDeathGlobal(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId))
                return;

            // A break announcement, not a death - and it has to be recognised BEFORE anything below runs.
            //
            // This channel carries free-form strings and the project already uses that: spawn-point deaths
            // arrive as "sp|...". Breakables arrive as "brk|scene|guid", and treating one as an enemy id
            // would put a GUID into the death memory, where MarkSpawnPointConsumed would ignore it and the
            // enemy loop would search for it forever.
            //
            // They travel here rather than as progress entries for one measured reason: the progress channel
            // deduplicates per id for the life of the server session, so after a bonfire restored a lamp the
            // same player could break it again and the announcement was thrown away in silence - the other
            // machine never learned about it. This channel keeps no such memory.
            string breakScene;
            string breakGuid;
            if (BreakSyncMemory.TryParseBreakId(enemyId, out breakScene, out breakGuid))
            {
                BreakSyncMemory.ApplyOrRemember(breakScene, breakGuid);
                return;
            }

            // A bonfire announcement, and it MUST be recognised here - before the two lines below.
            //
            // Everything that is not a "brk|" falls through to _pendingDeaths.Add, and _pendingDeaths is the
            // memory that makes an enemy die. A "falo|" left in it is inert (verified in IL: RegisterEnemy
            // compares exact ids, MarkSpawnPointConsumed requires "sp|", the id loop uses op_Inequality), but
            // it inflates the counters and leaves the fix depending on nobody ever parsing that string.
            if (BonfireSync.IsBonfireId(enemyId))
            {
                ReplicateBonfire(BonfireSync.SceneOf(enemyId));
                return;
            }

            // A player died, or came back. Both return here for the same reason as the bonfire: everything
            // that is not recognised falls through to _pendingDeaths.Add, and this payload is a PLAYER NAME.
            // It would be inert there - that list is never iterated, MarkSpawnPointConsumed demands "sp|",
            // and a name cannot contain '|' - but it would linger in memory and inflate the "received"
            // counter that the bonfire logs report, which is a measurement we are actively using.
            if (PlayerDeathSync.IsDeathId(enemyId) || PlayerDeathSync.IsAliveId(enemyId))
            {
                bool died = PlayerDeathSync.IsDeathId(enemyId);
                string who = PlayerDeathSync.NameOf(enemyId, died
                    ? PlayerDeathSync.DeathPrefix
                    : PlayerDeathSync.AlivePrefix);

                if (who != null)
                    PlayerDeathSync.ApplyRemoteState(who, died);

                return;
            }

            // A BIRTH, and it MUST be recognised here - BEFORE the line below, which consumes the id as a
            // death.
            //
            // This channel carries free-form strings and the project already uses it that way: "sp|" for
            // deaths, "brk|" for lamps, "falo|" for bonfires and "pdead|"/"palive|" for players. "nato|" is
            // the sixth of the series - the sender's game has just REVEALED an enemy, and this machine has to
            // wake its own copy. Left unrecognised the id would fall through to _pendingDeaths.Add and the
            // enemy would be marked dead at the very moment it was born.
            if (IsBirthId(enemyId))
            {
                ReceiveBirth(BirthIdOf(enemyId));
                return;
            }

            // UN'ESECUZIONE, e va riconosciuta QUI - PRIMA della riga qui sotto, che consuma l'id come
            // morte. Il prefisso sta accanto a "nato|" per la stessa ragione: tutto cio' che non e'
            // riconosciuto finisce in _pendingDeaths.Add e fa MORIRE il nemico.
            //
            // ⚠️ IL PREFISSO SI TOGLIE, NON SI IGNORA. L'id che segue e' un id VERO di nemico
            // ("sp|scena|SPAWN|x|y"), e viene rimesso in enemyId di proposito: cosi' TUTTO quello che
            // segue - la memoria delle morti, MarkSpawnPointConsumed e il ciclo che abbatte il nemico
            // qui presente - gira ESATTAMENTE come per una morte normale. Un nemico eseguito E' un
            // nemico morto, il suo annuncio di morte viaggia per conto suo (il ciclo di invio lo
            // intercetta entro ~50 ms, perche' Execution.OnUse chiama KillEntity() PRIMA di animare), e
            // fare qui la stessa contabilita' e' IDEMPOTENTE: chiude la finestra in cui dei due annunci
            // ne arrivasse uno solo.
            if (ExecutionSync.IsExecutionId(enemyId))
            {
                string executed = ExecutionSync.IdOf(enemyId);
                if (string.IsNullOrEmpty(executed))
                    return;

                ApplyRemoteExecution(executed);
                enemyId = executed;
            }

            // ⭐ LA MORTE DI UN BOSS (M1), e va riconosciuta QUI - PRIMA della riga qui sotto, che consuma
            // l'id come morte. Stessa ragione del bonfire, della morte dei giocatori, della nascita e
            // dell'esecuzione: qualunque cosa non riconosciuta finisce in `_pendingDeaths.Add` e da li' in
            // poi l'id viene trattato come la morte di un nemico qualunque.
            //
            // ⚠️ E QUI SI ESCE SENZA LA CONTABILITA' GENERICA, di proposito:
            //   - `_pendingDeaths` non serve: l'id generico arriva per conto suo (l'annuncio sopra non e'
            //     stato tolto) e fa la sua parte come ha sempre fatto;
            //   - `MarkSpawnPointConsumed` su un boss sarebbe un danno NUOVO: un boss non e' un nemico da
            //     consumare, e segnarlo consumato puo' impedirgli di tornare in scena per un altro tentativo.
            // ⇒ l'unica cosa che questo ramo fa e' `Kill()` sul boss locale, e solo se serve davvero.
            if (IsBossDeathId(enemyId))
            {
                string bossId = BossDeathIdOf(enemyId);
                if (!string.IsNullOrEmpty(bossId))
                    ApplyBossDeath(bossId);
                return;
            }

            _pendingDeaths.Add(enemyId);
            MarkSpawnPointConsumed(enemyId);

            // Already tracked here: the enemy exists (or is being created) in this scene, so it has to be
            // taken down as well. Marking the spawn point only prevents FUTURE creations.
            for (int i = 0; i < _ids.Count; i++)
            {
                if (_ids[i] != enemyId)
                    continue;

                DestroyDeadEnemy(ResolveEnemy(enemyId));
                return;
            }
        }

        internal static Enemy LastExecutedEnemy;

        /// <summary>
        /// Riproduce su QUESTA macchina il suono che la macchina esecutrice ha sentito nel momento in cui
        /// il nemico e' diventato eseguibile.
        ///
        /// COSA QUESTO METODO NON E', ed e' la parte importante. Non istanzia la prop di esecuzione, non
        /// nasconde il nemico, non tocca il penitent locale, la camera o l'input. La CUTSCENE
        /// dell'esecuzione e' pilotata da Tools.Level.Interactables.Execution.OnUse(), ed e' saldata al
        /// giocatore LOCALE: chiama Core.Logic.Penitent (nasconde lo sprite e disabilita la fisica),
        /// Core.Input.SetBlocker("EXECUTION", true), sposta il penitent locale sulla prop e guida la
        /// camera globale e il TimeScale. Chiamarla qui nasconderebbe IL GIOCATORE CHE NON STA ESEGUENDO,
        /// gli bloccherebbe i tasti e - se qualcosa lanciasse prima dell'ultima riga - lascerebbe il
        /// blocker acceso PER SEMPRE. Quella e' la voce 54. Questo metodo e' scritto perche' su questa
        /// macchina il blocker non venga MAI acceso.
        ///
        /// PERCHE' VALE COMUNQUE LA PENA. La MORTE del nemico viaggia gia': OnUse chiama KillEntity()
        /// prima di animare, Entity.Kill() porta la vita a zero, e il ciclo di invio annuncia sia
        /// EnemyKill sia EnemyDeathGlobal entro ~50 ms. Quello che NON viaggia e' il SUONO: sta nel ramo
        /// Behaviour.Execution(), che ESCE PRIMA di EnemyDamageArea.TakeDamage - e TakeDamage e' l'unico
        /// punto su cui e' agganciato EnemyDamagePatch. Percio' il relay di A1 NON lo copre.
        ///
        /// ⚠️ GUARDATO DA UN CAPO ALL'ALTRO, E NON E' PIGNOLERIA. Questo codice gira dentro
        /// NetworkManager.ProcessQueue, che Multiplayer.OnLateUpdate chiama dentro try/FINALLY - senza
        /// catch. Un'eccezione qui esce PRIMA che la coda venga svuotata e PRIMA di SendQueue: lo stesso
        /// pacchetto viene riprocessato per sempre e la mod smette di spedire. E' il guasto che e' costato
        /// 2934 errori e una sessione congelata (vedi il commento di ApplyDamage).
        /// </summary>
        private static void ApplyRemoteExecution(string id)
        {
            try
            {
                Enemy enemy = ResolveEnemy(id);
                LastExecutedEnemy = enemy;      // A6: serve al solo ripiego del controller.
                bool here = enemy != null && enemy.gameObject != null;

                ModLog.Info($"[Exec RX] {id} nemico_in_scena={(here ? "si" : "no")}");

                // ⚠️ QUI C'ERA UN `DisplayNotification("L'altro giocatore ha eseguito un nemico")`, ED E'
                // STATA TOLTA SU RICHIESTA ESPLICITA DELL'UTENTE. Era una riga di debug, scritta quando il
                // caso non era ancora chiaro: adesso il fantasma dell'esecuzione si vede a schermo
                // (OtherPlayerScript.PlayExecutionAnimation), quindi la notifica non aggiunge nulla e
                // resta solo un prompt in italiano in mezzo al gioco.
                //
                // ⚠️ TOLTA SOLO LEI. I marker `ModLog.Info("[Exec RX] ...")` qui sopra e qui sotto
                // RESTANO: sono l'unica osservabilita' di questa strada, e senza di loro "il pacchetto non
                // e' arrivato" e "e' arrivato e non ha suonato" tornano indistinguibili.
                //
                // ⚠️ E NON E' SOSTITUIBILE CON UNA NOTIFICA MIGLIORE: il mittente non viaggia su questo
                // canale (EnemyDeathGlobal non lo porta, e ProcessQueue passa solo i byte), quindi
                // qualunque testo con un NOME sarebbe inventato. Vedi la nota in PlayerDeathSync.
                if (Core.Logic.Penitent == null || Core.Logic.Penitent.Audio == null)
                    return;

                // LA CHIAMATA VERBATIM DEL GIOCO, letta da tutti e sedici gli override di
                // Behaviour.Execution: Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy().
                // E' un evento di CATALOGO ("PenitentSimpleEnemyHit", la public const
                // PenitentAudio.SimpleEnemyHitEventKey), quindi si risolve sul catalogo DI QUESTA macchina:
                // non viaggia alcuna chiave, alcun percorso di banca, alcuna stringa.
                // E' anche GIA' usata dalla mod in due punti, quindi non e' una firma da indovinare.
                Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy();

                if (!here)
                    ModLog.Info($"[Exec RX] {id}: il nemico non e' in questa scena, ho suonato lo stesso");
            }
            catch (Exception e)
            {
                ModLog.Error("[Exec RX] riproduzione fallita: " + e.GetType().Name + " - " + e.Message);
            }
        }

        /// <summary>
        /// Registers the dead enemy's spawn point in the game's consumed list, keyed by position.
        ///
        /// This is the game's own bookkeeping: EnemySpawner keeps a List&lt;Vector3&gt; and clears it when
        /// the player rests at a bonfire (RespawnDeadEnemies), so the reset comes for free and stays
        /// consistent with single player. It is idempotent, so calling it repeatedly is harmless.
        ///
        /// Note the key is the POSITION, not the name. Several spawn points can share a position (the dump
        /// showed SPAWN (11) and SPAWN (15) both at -697,15), and the game treats those as the same spot:
        /// killing one consumes the other too. That is the base game's behaviour, not something to fix.
        /// </summary>
        private static void MarkSpawnPointConsumed(string enemyId)
        {
            EnemySpawner spawner = Core.Logic != null ? Core.Logic.EnemySpawner : null;
            if (spawner == null || string.IsNullOrEmpty(enemyId) || !enemyId.StartsWith("sp|"))
                return;

            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();
            if (points.Length == 0)
                return;

            // Prefer the enemy's OWN transform: it is the exact position the game will compare against,
            // whereas the id string only carries two decimals and rebuilding a Vector3 from it can miss by a
            // fraction. The game's list uses exact Vector3 equality, so a fraction is enough to fail.
            Vector3 position;
            Enemy tracked = ResolveEnemyById(enemyId);
            if (tracked != null)
            {
                position = tracked.transform.position;
            }
            else if (!TryParseSpawnPointPosition(enemyId, out position))
            {
                // Spawn point exists but the enemy is not here yet: fall back to the position in the id,
                // which is accurate to two decimals and is enough to find the right spot.
                return;
            }

            EnemySpawnPoint nearest = null;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                EnemySpawnPoint sp = points[i];
                if (sp == null)
                    continue;

                float d = Vector3.Distance(sp.transform.position, position);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = sp;
                }
            }

            // Half a unit is far beyond any rounding error yet far below the spacing between distinct spots.
            if (nearest != null && nearestDistance <= 0.5f)
                spawner.AddConsumedSpawner(nearest);
        }

        /// <summary>Resolves an id to a live Enemy without creating anything.</summary>
        private static Enemy ResolveEnemyById(string id)
        {
            Enemy enemy;
            return _enemies.TryGetValue(id, out enemy) ? enemy : null;
        }

        private static bool TryParseSpawnPointPosition(string enemyId, out Vector3 position)
        {
            position = Vector3.zero;

            string[] parts = enemyId.Split('|');
            if (parts.Length < 5)
                return false;

            float x;
            float y;
            if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                return false;

            position = new Vector3(x, y, 0f);
            return true;
        }

        /// <summary>
        /// Takes a dead enemy down completely, so nothing inert is left standing in the scene.
        ///
        /// The state that produced the ghost was: Dead set, but the entity neither animated its death nor
        /// deactivated - visible, idle and untargetable, which is the middle of a death sequence that never
        /// finished.
        ///
        /// IT NO LONGER DESTROYS THE GAMEOBJECT, and that change is the whole point of this method's history.
        /// Destroy() is the definitive end of an object, and it was the definitive end of the session too:
        /// the game keeps its own references to an enemy - Execution.ExecutedEntity, the prop of the
        /// execution interactable and its native state machine - and destroying the object invalidates them
        /// while the game still uses them.
        ///
        /// What that cost, measured: `StuntBehaviour.OnStateUpdate` calls `GetComponent&lt;EnemyBehaviour&gt;()`
        /// on the executed enemy. With the object destroyed, the failure happens inside the NATIVE icall
        /// (GetComponentFastPath), not on a managed null check - which is why the stack's top frame is
        /// GetComponent rather than OnStateUpdate. And `StuntBehaviour` leaves its state through the LAST
        /// instruction of OnStateUpdate, a Destroy of the prop: the exception skips it, so the state is never
        /// left and the native state machine calls OnStateUpdate every single frame. 514 exceptions in one
        /// session, 78% of that log.
        ///
        /// Then it stops being a log problem. The execution coroutine is
        /// `SetBlocker("EXECUTION", true) -> ... -> ExecutedEntity.KillEntity() -> ... -> ShowPlayer(...) ->
        /// SetBlocker("EXECUTION", false)`. When KillEntity throws on the ghost, the exception leaves the
        /// coroutine and the coroutine DIES: the player is never shown again, IsOnExecution is never reset,
        /// and the blocker stays on - invisible penitent, every key dead, game closed by force. It happened.
        ///
        /// Deactivating instead keeps every reference valid while removing the enemy from the scene exactly
        /// as intended: not visible, not targetable, not colliding, not blocking. And it lets the game end
        /// its own sequence rather than having the object pulled out from under it.
        /// </summary>
        private static void DestroyDeadEnemy(Enemy enemy)
        {
            if (enemy == null)
                return;

            string id = IdOf(enemy);

            // Ask whether the death sequence started, not whether the health is at zero: see
            // KillEnemyLocally for why Entity.Dead is the wrong question here. This is the half that
            // already did the right thing - the game's own damage entry point, so the flash, the sound and
            // the DEATH trigger all follow the normal flow.
            if (!enemy.Status.Dead)
                KillEnemyLocally(enemy, HandleOf(id));

            // The game may have already taken it down; either way it must not remain in the scene.
            //
            // NOT Destroy: see above. SetActive(false) is enough for everything the destroy was for, and
            // it is the difference between a session that continues and one that ends with the player
            // unable to press a key.
            if (enemy != null && enemy.gameObject != null && enemy.gameObject.activeSelf)
                enemy.gameObject.SetActive(false);
        }


        /// <summary>Authoritative death, so peers always see the corpse and the drop.</summary>
        public static void ReceiveEnemyKill(uint handle)
        {
            Enemy enemy = ResolveByHandle(handle);
            if (enemy == null)
            {
                ReportResolveFailure(handle);
                return;
            }

            // Drop the id so the dead enemy stops being broadcast, then run the native death sequence if
            // it has not already died locally.
            for (int i = 0; i < _ids.Count; i++)
            {
                if (HandleOf(_ids[i]) == handle)
                {
                    _enemies.Remove(_ids[i]);
                    break;
                }
            }

            // Status.Dead, not Entity.Dead: an enemy at zero health whose death has not yet started must
            // still receive this, because this is the only thing that will start it.
            if (enemy.Status.Dead)
                return;

            // Neutral damage: this is the mod settling an agreed death, not a blow somebody struck.
            ApplyDamage(enemy, enemy.CurrentLife + 1f, (byte)DamageArea.DamageType.Normal,
                        (byte)DamageArea.DamageElement.Normal, 0);
        }

        private static Enemy ResolveEnemy(string id)
        {
            // The spawn point is asked FIRST, and that order is the fix for a real defect.
            //
            // The cache used to win, but the game can recreate an enemy from its spawn point - on a scene
            // reload, and probably when an enemy is reactivated. The cache would then keep pointing at the
            // old instance, detached from the scene, so everything read through it (position, life,
            // behaviour) described an enemy that no longer existed: in single player the log reported one
            // position for the whole test while the enemy was visibly walking. The game's own reference is
            // authoritative; the cache is only a fallback for ids with no spawn point.
            if (_spawnPoints.TryGetValue(id, out EnemySpawnPoint point) && point != null)
            {
                Enemy spawned = point.SpawnedEnemy;
                if (spawned != null && spawned.gameObject != null)
                {
                    _enemies[id] = spawned;
                    return spawned;
                }
            }

            if (_enemies.TryGetValue(id, out Enemy cached) && cached != null && cached.gameObject != null)
                return cached;

            return null;
        }

        // Applies a hit through the native damage area so hurt animations, sounds and death all run.
        private static void ApplyDamage(Enemy enemy, float damageAmount, byte damageType,
                                        byte damageElement, byte flags)
        {
            if (enemy.EntityDamageArea == null)
                return;

            // Logged so the relayed damage is MEASURABLE, which the health channel alone cannot do: when a hit
            // kills, both machines agree on the kill and the log shows it, but a hit that only WOUNDS leaves no
            // trace of how much it took. That gap is what made the worst regression of this project invisible
            // in the logs - an exception fired before the health was subtracted, so every relayed blow dealt
            // ZERO and nothing in the log said so. One line closes it.
            ModLog.Info($"[Danno] relay {IdOf(enemy)} amount={damageAmount:F0} type={damageType} " +
                        $"elem={damageElement} flags={flags} vitaPrima={enemy.CurrentLife:F0}");

            Hit hit = new Hit
            {
                DamageAmount = damageAmount,

                // The damage type and the element come from the hit that actually landed, so the receiving
                // machine picks the same reaction the attacker's machine did. They used to be hard-coded to
                // Normal, which is why every relayed blow sounded light and why the enemy types that branch on
                // the damage type always took the wrong branch.
                DamageType = (DamageArea.DamageType)damageType,
                DamageElement = (DamageArea.DamageElement)damageElement,
                DontSpawnBlood = (flags & HIT_FLAG_DONT_SPAWN_BLOOD) != 0,
                Unparriable = (flags & HIT_FLAG_UNPARRIABLE) != 0,
                Unblockable = (flags & HIT_FLAG_UNBLOCKABLE) != 0,

                // THE SOUND, and it has to be the full bank PATH - not the catalog key.
                //
                // This was tried once with PenitentAudio.SimpleEnemyHitEventKey, which is "PenitentSimpleEnemyHit",
                // and it took the mod down: Entity.Damage calls RuntimeManager.CreateInstance with whatever is in
                // here, that call requires a bank path, and the exception it threw fired BEFORE the health was
                // subtracted. Every relayed blow dealt ZERO damage, the exception escaped through OnLateUpdate -
                // which has try/finally and no catch - so ProcessQueue never reached its queue-clearing line and
                // the same packet was reprocessed forever. 2934 errors and a frozen mod, from one string.
                //
                // Those two paths below are not guessed: both appear in the game's own audio log as events it
                // plays, so they are known-good bank paths. The key would only resolve through the catalog
                // (AudioManager.CreateCatalogEvent), which is a different door entirely.
                HitSoundId = ((DamageArea.DamageType)damageType) == DamageArea.DamageType.Heavy
                    ? "event:/SFX/Penitent/Damage/PenitentHeavyEnemyHit"
                    : "event:/SFX/Penitent/Damage/PenitentSimpleEnemyHit",

                AttackingEntity = Core.Logic.Penitent != null ? Core.Logic.Penitent.gameObject : null
            };

            // SPARKS AND BLOOD ARE THE ATTACKER'S, AND NOBODY REPLAYS THEM OTHERWISE.
            //
            // The hurt flash, the sound and (for many enemy types) the death trigger all live inside
            // <EnemyType>::Damage(Hit), which the call below already reaches - that is why the sound is heard
            // on both machines today. Sparks and blood are different: they are spawned by
            // PenitentSword.Attack, which calls Weapon.GetDamageableEntities() to find what the LOCAL sword
            // overlapped, then GetSwordSparks, and only afterwards applies the damage. On the machine that is
            // not swinging, the sword never overlapped anything, so that whole path never runs and the hit
            // lands silently from the eye's point of view.
            //
            // The replay happens HERE, on the relay path, and that placement is what keeps it from doubling:
            // this method only ever runs for damage that arrived from the network, and the sender is excluded
            // by the server, so the machine that swung never reaches this code for its own blow.
            //
            // The position is not transmitted: the receiver holds the same enemy instance, so the impact point
            // is the same collider bounds centre the vanilla code would have used.
            ReplayHitEffects(enemy, hit);

            // Through the game's own damage entry point, not through the hurtbox.
            //
            // ((IDamageable)enemy).Damage(hit) is what Weapon.AttackDamageableEntities calls when the
            // player's sword connects, and it lands in <EnemyType>::Damage(Hit). That is where the hurt
            // flash (DamageEffectScript.Blink), the hurt sound (Behaviour.Damage) and - for the many enemy
            // types that set their DEATH trigger there - the death animation are all driven from.
            //
            // EnemyDamageArea.TakeDamage, used before, is a lower layer: it applies the health change and
            // fires the damage events, but skips <EnemyType>::Damage entirely. That is why a hit applied by
            // this mod produced no visible reaction at all on the machine that had not swung the sword, and
            // why some enemies could take a killing hit from here and never start dying.
            IsApplyingRemoteDamage = true;
            try
            {
                IDamageable damageable = (IDamageable)enemy;
                if (damageable != null)
                    damageable.Damage(hit);
                else if (enemy.EntityDamageArea != null)
                    enemy.EntityDamageArea.TakeDamage(hit, true);
            }
            finally
            {
                IsApplyingRemoteDamage = false;
            }
        }

        /// <summary>
        /// Spawns the sparks and the blood a hit would have produced, on a machine that did not swing.
        ///
        /// Mirrors what PenitentSword.GetSwordSparks does for each entity it overlapped: take the collider's
        /// bounds centre, then ask the ENEMY whether this kind of impact sparks and bleeds. Those two answers
        /// are per-prefab fields (sparksOnImpact / bleedOnImpact), which is exactly why "some enemies never
        /// show sparks" is correct behaviour rather than a bug.
        ///
        /// It deliberately does NOT call PenitentSword.GetSwordSparks(list, hit), which would be less code.
        /// That method needs the attacker's own entity list and would re-derive the same decisions; going
        /// straight to SpawnSparks/SpawnBlood with the enemy checked here keeps the number of game members
        /// this mod reaches through to the minimum, and every one of these was verified public in IL.
        ///
        /// Everything is guarded. A mod-visible exception inside the damage path is worse than a missing
        /// spark: it is exactly how the send loop lost a session earlier in this project.
        /// </summary>
        private static void ReplayHitEffects(Enemy enemy, Hit hit)
        {
            try
            {
                Penitent penitent = Core.Logic.Penitent;
                if (penitent == null)
                    return; // dying, in a menu, or between levels: nothing to attach the effect to

                // Same source the vanilla code uses for the impact point.
                Collider2D collider = enemy.GetComponentInChildren<Collider2D>();
                if (collider == null)
                    return;

                Vector3 position = collider.bounds.center;

                if (enemy.SparkOnImpact() && penitent.SwordSparkSpawner != null)
                    penitent.SwordSparkSpawner.GetSwordSpark(position);

                if (hit.DontSpawnBlood || !enemy.BleedOnImpact() || enemy.IsHitGuarded(hit))
                    return;

                PenitentSword sword = penitent.PenitentAttack != null
                    ? penitent.PenitentAttack.CurrentPenitentWeapon as PenitentSword
                    : null;

                // The sword owns the blood pool, so without one there is nothing to reuse. Skipping is the
                // right answer: creating a second source of blood effects would be a new problem, not a fix.
                if (sword != null)
                    sword.SpawnBlood(position, hit);
            }
            catch (Exception e)
            {
                _hitEffectErrors++;
                if (_hitEffectErrors <= 5)
                    ModLog.Error("[EnemyFX] replay degli effetti fallito (" + e.GetType().Name + "): " +
                                 e.Message + " - il danno e' stato applicato comunque");
            }
        }

        /// <summary>Failed effect replays. Diagnostics only: the damage itself is unaffected by them.</summary>
        private static int _hitEffectErrors;

        /// <summary>
        /// How many times the send loop had to skip an enemy because of an exception.
        ///
        /// Reported in the probe line, because the session that exposed this had 666 of them and the logs
        /// looked clean: the word "Exception" never appeared, and the error marker has two spaces before its
        /// colon, so a search for "Error:" found nothing. A count that has to be zero, and is visible every two
        /// seconds, cannot hide like that.
        /// </summary>
        private static int _syncLoopExceptions;

        /// <summary>Exceptions skipped by the send loop, for the probe line. Diagnostics only.</summary>
        public static int SyncLoopExceptions { get { return _syncLoopExceptions; } }
    }
}
