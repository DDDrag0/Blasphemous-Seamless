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
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Host-authoritative enemy synchronization.
    ///
    /// IDENTITY. Neither <c>Enemy.SpawningId</c> nor the enemy's GameObject name can be a network id:
    /// <c>EnemySpawnPoint.Awake</c> sets <c>SpawningId = gameObject.GetHashCode()</c>, which is
    /// per-process, and <c>SetEntityName</c> renames enemies to <c>EntityName + GetHashCode()</c>. The
    /// stable, authored anchor is the <c>EnemySpawnPoint</c> itself. The network id is therefore
    /// derived from (scene, spawn point name, spawn point position), identical on both machines, and
    /// the enemy is resolved through <c>EnemySpawnPoint.SpawnedEnemy</c>.
    ///
    /// AUTHORITY. The server elects one host via the HostAssign packet. Only the host applies damage
    /// to enemies; peers block it locally, ask the host, and apply the approved damage when it comes
    /// back. <see cref="IsApplyingRemoteDamage"/> is the re-entrancy guard.
    /// </summary>
    public static class EnemySyncManager
    {
        private const float SEND_INTERVAL = 0.05f;   // ~20 state broadcasts per second
        private const float RESCAN_INTERVAL = 0.5f;  // discovers spawn points created after level load

        /// <summary>
        /// Cadence of the per-enemy diagnostic snapshot. Kept separate from <c>RESCAN_INTERVAL</c>,
        /// which must stay fast because it discovers post-load spawn points; this one only trades
        /// detail for a readable log.
        /// </summary>
        private const float DIAGNOSTIC_LOG_INTERVAL = 10f;
        private static float _lastDiagnosticLog;
        private const float FULL_RESEND_INTERVAL = 1f;
        private const float POSITION_EPSILON = 0.01f;
        private const float LIFE_EPSILON = 0.01f;

        private static readonly Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();
        private static readonly Dictionary<string, EnemySpawnPoint> _spawnPoints = new Dictionary<string, EnemySpawnPoint>();
        private static readonly List<string> _ids = new List<string>();

        /// <summary>Every enemy already given an id; a moved enemy is never mistaken for a new one.</summary>
        private static readonly HashSet<Enemy> _registered = new HashSet<Enemy>();

        /// <summary>Enemies currently tracked in this scene, keyed by network id. Read-only use.</summary>
        public static Dictionary<string, Enemy> Enemies => _enemies;

        /// <summary>
        /// Last state broadcast for an enemy. One record per id on purpose: parallel dictionaries
        /// with an indexer threw <c>KeyNotFoundException</c> for a freshly registered enemy whose entry
        /// did not exist yet, aborting the whole update.
        /// </summary>
        private class EnemyState
        {
            public Vector2 Position;
            public float Life;
            public int AnimState;
            public float LastSweep;

            /// <summary>
            /// True when this machine runs the game's own AI for the enemy - the only thing that
            /// produces real attacks and deaths, since the animation comes from the enemy's own
            /// AnimatorInyector code driven by the behaviour tree.
            /// </summary>
            public bool OwnedByLocal;
            public float OwnerSince;

            /// <summary>
            /// Facing as <c>EntityOrientation</c> (Right = 0, Left = 1). Changes go through
            /// <c>Entity.SetOrientation</c>, which also flips <c>SpriteRenderer.flipX</c>; without
            /// syncing it, the non-owner keeps facing the wrong way and its walk looks mirrored.
            /// </summary>
            public byte Orientation;

            /// <summary>True once the host has told this machine who owns the enemy.</summary>
            public bool OwnershipKnown;

            /// <summary>Last ownership value the host published, so a change is sent once.</summary>
            public bool LastPublishedOwnerIsMe;

            /// <summary>
            /// The other machine's claim on this enemy, learned from its updates. Without it the host
            /// only sees its own aggro, never learns the peer is being chased, and two machines fight
            /// over the same enemy.
            /// </summary>
            public bool OwnedByRemote;
            public bool RemoteEverClaimed;

            /// <summary>
            /// True once the enemy has been seen TRULY IN PLAY. Read from the damage collider, not
            /// from <c>activeInHierarchy</c>: a whole family (WaxCrawler and its reuses) is spawned
            /// active but hidden, and the reveal flips the sprite and collider on without ever calling
            /// <c>SetActive</c>. The collider is the signal that means "this enemy is now real".
            /// </summary>
            public bool WasReal;

            /// <summary>
            /// True once the boss's life has been aligned in the current situation (this scene entry,
            /// this owner). For normal enemies life travels in every packet and this field is unused;
            /// for a boss it is what makes "align once" true rather than hoped for.
            /// </summary>
            public bool BossLifeAligned;

            /// <summary>
            /// The scene visit in which the boss life was aligned. Used only to name the CAUSE in the
            /// marker: same visit → owner changed; different visit → scene entry.
            /// </summary>
            public int BossLifeVisit;
        }

        /// <summary>
        /// Minimum time an owner keeps an enemy before the role can move again: without a hold, aggro
        /// flips whenever a target enters or leaves sight and the enemy visibly jumps between two
        /// positions.
        /// </summary>
        private const float OWNER_HOLD_SECONDS = 0.5f;

        /// <summary>
        /// How long a closer player waits for an enemy's position before taking it anyway. The wait
        /// exists so a newly-owning machine does not resume the enemy at its stale copy; it must
        /// never STRAND the enemy, so past this grace the enemy is taken where it stands.
        /// </summary>
        private const float POSITION_WAIT_GRACE = 1.5f;

        /// <summary>
        /// How much closer the other player must be before this machine gives up an enemy. A dead
        /// zone keeps the owner stable while the two players are side by side.
        /// </summary>
        private const float OWNER_DISTANCE_MARGIN = 2.5f;

        private static readonly Dictionary<string, EnemyState> _lastSent = new Dictionary<string, EnemyState>();

        /// <summary>
        /// The id assigned to each enemy INSTANCE, decided once and never recomputed. The free-enemy
        /// id embeds the position, and recomputing later would give a different string as soon as the
        /// enemy moves; because the two machines register at different moments, they would produce
        /// different ids for the same enemy. Keyed by instance, not name, so duplicates like
        /// <c>Fool(Clone)</c> are not merged.
        /// </summary>
        private static readonly Dictionary<Enemy, string> _enemyIds = new Dictionary<Enemy, string>();

        /// <summary>
        /// When each enemy was first seen at zero health with its death sequence not yet started,
        /// keyed by id. Enemies whose death cannot be started from here (per-type wiring varies) sit
        /// at zero health with nothing playing; past the grace the mod forces the end.
        /// </summary>
        private static readonly Dictionary<string, float> _deathPendingSince = new Dictionary<string, float>();

        /// <summary>How long an enemy may sit at zero health with no death sequence before the mod forces it.</summary>
        private const float DEATH_GRACE_SECONDS = 1.5f;

        /// <summary>
        /// The enemies this machine is NOT simulating, as a set of instances. Needed by the
        /// orientation probe: the Harmony prefix on <c>Entity.SetOrientation</c> runs many times per
        /// frame and cannot afford an id-table lookup each time. Maintained exactly where ownership
        /// changes, so it cannot drift.
        /// </summary>
        private static readonly HashSet<Enemy> _puppetEnemies = new HashSet<Enemy>();

        /// <summary>True when this machine is not simulating the enemy. Used by the orientation probe.</summary>
        public static bool IsPuppet(Enemy enemy)
        {
            return enemy != null && _puppetEnemies.Contains(enemy);
        }

        /// <summary>
        /// A short label naming an enemy by POSITION, for the orientation probe. SPAWN(NN) labels
        /// permute between runs; position is the only stable handle. Cached per instance because the
        /// probe calls this on every orientation write.
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
        /// Enemy ids that belong to a BOSS, so their health is deliberately left alone. Bosses derive
        /// from Enemy and are tracked, positioned and animated the same way, but their death is a
        /// scripted sequence started only by <c>&lt;Boss&gt;::Damage</c>; writing their health to zero
        /// does not kill them, it leaves the boss standing with an empty bar and the room unable to
        /// advance.
        /// </summary>
        private static readonly Dictionary<string, bool> _bossIds = new Dictionary<string, bool>();

        /// <summary>
        /// True when the enemy is a boss, judged by namespace rather than a name list: every boss
        /// lives under <c>Gameplay.GameControllers.Bosses</c>, ordinary enemies under
        /// <c>Gameplay.GameControllers.Enemies</c>.
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
        // M1 — BOSS DEATH, APPLIED ON THE DEAD PLAYER'S MACHINE
        // ============================================================================================

        /// <summary>
        /// The boss-death prefix of the death channel: <c>bdead|</c> means "a BOSS died on the killing
        /// machine". The generic id <c>sp|...</c> is already broadcast, but the receiver applies a
        /// generic death through <c>&lt;Boss&gt;::Damage(Hit)</c>, and that begins with
        /// <c>if (Status.Dead || Core.Logic.Penitent.Stats.Life.Current &lt;= 0f) return;</c>. With the
        /// local player dead, the death is silently DISCARDED and the boss stays standing at full
        /// health. The prefix tells the receiver one thing: apply the death with <c>Kill()</c>, which
        /// does not pass through that guard.
        /// </summary>
        private const string BossDeathPrefix = "bdead|";

        /// <summary>True when this id is a boss-death announcement.</summary>
        private static bool IsBossDeathId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(BossDeathPrefix, System.StringComparison.Ordinal);
        }

        /// <summary>The boss id inside the announcement, or null for anything malformed.</summary>
        private static string BossDeathIdOf(string id)
        {
            if (!IsBossDeathId(id))
                return null;

            string enemyId = id.Substring(BossDeathPrefix.Length);
            return string.IsNullOrEmpty(enemyId) ? null : enemyId;
        }

        /// <summary>Boss ids whose death this machine has already applied from an announcement.</summary>
        private static readonly HashSet<string> _bossKillsApplied = new HashSet<string>();

        /// <summary>
        /// Boss ids whose death SEQUENCE (<c>Death()</c>) this machine has already played. Independent
        /// from <c>_bossKillsApplied</c>: that one says "the Kill() went through", this one says "the
        /// death was acted out". Same lifetime as the first.
        /// </summary>
        private static readonly HashSet<string> _bossDeathsApplied = new HashSet<string>();

        /// <summary>
        /// A) The whitelist of <c>EnemyBehaviour</c> types whose <c>Death()</c> this mod may call after
        /// <c>Kill()</c>. Criterion: <c>Death()</c> must do something only inside the boss. Everything
        /// that touches the LOCAL <c>Penitent</c> (Life, DrivePlayer, Invulnerable, SetBlocker on
        /// input), the camera, or TimeScale is excluded: those bosses stay AFK on purpose.
        ///
        /// The five whitelisted behaviours only stop their own coroutines/events/tree and trigger the
        /// natively disappearing DEATH state. Twelve others are excluded for touching Penitent,
        /// input, camera, geometry, or because their death is driven from outside this enemy.
        /// </summary>
        private static readonly HashSet<string> BossDeathSequenceWhitelist =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ElderBrotherBehaviour",
                "LesmesBehaviour",
                "QuirceBehaviour",
                "PerpetuaBehaviour",
                "EsdrasBehaviour"
            };

        /// <summary>
        /// Applies, on THIS machine, a boss death announced by the other. Uses <c>Kill()</c>, which is
        /// the native path: <c>Entity.Kill()</c> goes through <c>Damage(Life.Current + Defense.Final, "")</c>
        /// → <c>KillInstanteneously()</c> → <c>Status.Dead = true</c> → <c>OnDeath</c> →
        /// <c>BossFightManager.OnDeathBoss</c>. The dead-player guard lives on <c>Damage(Hit)</c>, not
        /// on <c>Damage(float, string)</c>, which is why this fix is small.
        ///
        /// Does NOT touch cutscenes, SetFreeCamera, input blockers, TimeScale, the Penitent, camera,
        /// or flags. Does nothing if the local player is alive: on a live machine the death already
        /// arrives through the normal path and the cutscene plays. The "both dead" case is not
        /// handled: if nobody survives, nobody announces anything.
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
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NOT applied (I was dead: " +
                                $"{(localDead ? "yes" : "no")}) motivo=boss-not-in-scene via={via}");
                    return;
                }

                // Only fires on the broken case: local player dead with a live boss copy.
                if (!localDead)
                {
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NOT applied (I was dead: no) " +
                                $"motivo=local-player-alive via={via}");
                    return;
                }

                // Status.Dead means "the death sequence has started", same question KillEnemyLocally asks.
                if (boss.Status.Dead)
                {
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NOT applied (I was dead: yes) " +
                                $"motivo=boss-already-dead via={via}");
                    return;
                }

                if (!_bossKillsApplied.Add(id))
                {
                    ModLog.Info($"[BossDeath RX] {FullId(id)} NOT applied (I was dead: yes) " +
                                $"motivo=already-applied via={via}");
                    return;
                }

                bool isBoss = IsBoss(boss, id);
                string type = boss.GetType().Name;
                float lifeBefore = boss.CurrentLife;

                // Snapshot of the tree state at Kill() time. Not touched: the tree is not the death
                // path. Measured here so the log can say whether it was off, when Kill() went through.
                EnemyBehaviour tree = FindBehaviour(boss);
                string treePrima = tree == null
                    ? "assente"
                    : ((tree.enabled ? "on" : "off") + "/isRunning=" + ReadTreeRunning(tree));

                // The tree is NOT re-enabled before Kill(). Boss death sequences are driven by
                // <Boss>.Damage(Hit) → Behaviour.Death(), and Kill() does not go through there. Re-
                // enabling the tree would arm the AI on a dead boss without triggering the death.
                boss.Kill();

                // A) THE RECITED DEATH — the game's own sequence, after Kill().
                //
                // Kill() sets Status.Dead and starts the cutscene (OnDeath → BossFightManager.OnDeathBoss)
                // but does not go through <Boss>Behaviour.Death(), which is the only thing that fires
                // the AnimatorInyector DEATH trigger. Without this the dead machine has a boss with
                // Status.Dead=true and NO death animation. Do not call it if Kill() did not kill.
                bool died = boss.Status.Dead;
                float lifeAfter = boss.CurrentLife;
                string deathEsito = died
                    ? ApplyBossDeathSequence(boss, id)
                    : "not-called:kill-failed";

                // Always logged, so "the announcement did not arrive" and "it arrived and did nothing"
                // stay distinguishable.
                ModLog.Info($"[BossDeath RX] {FullId(id)} applicato (ero morto: si) via={via} " +
                            $"boss={isBoss} tipo={type} vitaPrima={lifeBefore:F0} " +
                            $"vitaDopo={lifeAfter:F0} StatusDead={died} " +
                            $"Kill={(died ? "ok" : "no-effect")} morte={deathEsito} " +
                            $"treePrima={treePrima} treeToccata=no");

                if (!died)
                {
                    ModLog.Error($"[BossDeath RX] {FullId(id)} Kill() did NOT kill the boss: " +
                                 $"StatusDead=false vita={boss.CurrentLife:F0}");
                }

                // Warning to the dead player. Attached here because this is the live point where the
                // mod kills the boss on this machine, and the local player is dead by construction
                // (the guard above exits on local-alive).
                if (died)
                    BossDeathWarning.WarnBossDied("rx:ApplyBossDeath (Kill() applicato)");
            }
            catch (Exception e)
            {
                // An exception here would escape NetworkManager.ProcessQueue, which runs try/FINALLY
                // without catch: the queue would not be drained and the same packet would be
                // reprocessed forever. Same hazard as the 2934-errors incident.
                ModLog.Error($"[BossDeath RX] {FullId(id)} eccezione in Kill(): {e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>
        /// A) Makes the boss play its OWN death, as the game does: <c>EnemyBehaviour.Death()</c> →
        /// <c>StopBehaviour</c> + <c>AnimatorInyector.Death()</c>. The concrete type varies per boss,
        /// so it is invoked through reflection. Only whitelisted types are called, and only once per
        /// id. Returns a string for the log (<c>morte=</c>), so "called" and "skipped because
        /// excluded" are distinguishable.
        /// </summary>
        private static string ApplyBossDeathSequence(Enemy boss, string id)
        {
            try
            {
                if (!_bossDeathsApplied.Add(id))
                    return "skipped:already-played";

                EnemyBehaviour[] behaviours = boss.GetComponents<EnemyBehaviour>();

                // Fallback for bosses whose EnemyBehaviour lives on a child; same lookup used by
                // FindBehaviour elsewhere in this file.
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
                        return "excluded:" + typeName + "-no-Death";

                    death.Invoke(behaviour, null);
                    return "recitata:" + typeName;
                }

                return "excluded:" + (primo ?? "?") + "-not-in-whitelist";
            }
            catch (Exception e)
            {
                // Anything coming out of a boss Death() must not escape here: this code runs inside
                // NetworkManager.ProcessQueue.
                return "eccezione:" + e.GetType().Name;
            }
        }

        // ============================================================================================
        // C) THE BOSS CORPSE MUST NOT REMAIN VISIBLE ON RE-ENTRY
        // ============================================================================================

        /// <summary>
        /// Bosses this fix has switched off on scene entry. Two purposes, both necessary:
        /// idempotency (a second spawn pass of the same load must not re-hide them) and skip in
        /// <c>RegisterEnemy</c> (a boss we recognised as a corpse must not enter the sync table, or
        /// the mod re-enables its tree and gets an invisible fighting boss). Cleared by
        /// <c>LevelLoaded</c>.
        /// </summary>
        private static readonly HashSet<Enemy> _hiddenBossCorpses = new HashSet<Enemy>();

        /// <summary>
        /// C) ON ENTERING A BOSS ROOM: if this room's boss has ALREADY BEEN KILLED, hide it. Predicate
        /// is (Status.Dead) OR (&lt;scene&gt;_BOSSDEAD flag on, either full-scene or zone-level). The
        /// flag is the game's progress; a live boss with the flag OFF is never touched, but with the
        /// flag ON the "looks alive" veto is bypassed because the room's progress says that fight is
        /// over.
        ///
        /// Never Destroy: SetActive(false). Idempotent on an already-inactive object.
        /// Called from OnEnemiesSpawned (once per load, on both machines).
        /// </summary>
        private static void HideDefeatedBosses(string modScene, string where)
        {
            try
            {
                // The scene used for FLAGS comes from the game, not from _scene. On the first spawn
                // pass of a load, _scene still holds the PREVIOUS room (LevelLoaded runs after spawn),
                // so the flag lookup must use Core.LevelManager.currentLevel.LevelName.
                bool dalGioco;
                string sceneDaGioco = SceneForBossFlags(modScene, out dalGioco);
                LogBossCorpseScene(sceneDaGioco, modScene, where, dalGioco);

                Enemy[] enemies = FindAllEnemiesIncludingInactive();
                if (enemies == null)
                    return;

                for (int i = 0; i < enemies.Length; i++)
                {
                    Enemy enemy = enemies[i];
                    if (enemy == null || enemy.gameObject == null)
                        continue;

                    // Same criterion as IsBoss (namespace), without id and cache.
                    if (!IsBossByNamespace(enemy))
                        continue;

                    if (_hiddenBossCorpses.Contains(enemy))
                        continue;

                    bool statusDead = enemy.Status != null && enemy.Status.Dead;
                    string flag = FirstBossDeadFlagOn(sceneDaGioco);

                    string enemyId = BuildFreeEnemyId(modScene, enemy);
                    string tipo = enemy.GetType().Name;

                    // Veto "looks ALIVE" only holds while the flag is OFF. With the flag ON, the
                    // progress says the fight is over, so the object is a corpse the room recreated.
                    bool vivoScavalcato = BossLooksAlive(enemy);
                    if (vivoScavalcato && flag == null)
                    {
                        ModLog.Info($"[BossCorpse] {enemyId} NOT hidden ({where}): the boss looks ALIVE " +
                                    $"(statusDead=False flag={(flag ?? "none")} vita={enemy.CurrentLife:F0}) " +
                                    "and a live boss is left alone");
                        continue;
                    }

                    if (!statusDead && flag == null)
                        continue; // ordinary boss of an ordinary room: do not touch

                    _hiddenBossCorpses.Add(enemy);
                    bool eraAttivo = enemy.gameObject.activeSelf;
                    enemy.gameObject.SetActive(false);

                    ModLog.Info($"[BossCorpse] {enemyId} hidden ({where}): the boss of this room was " +
                                $"already killed - statusDead={statusDead} flag={flag ?? "none"} " +
                                $"tipo={tipo} activeBefore={(eraAttivo ? "yes" : "no: it was already off")} " +
                                "(SetActive(false), no Destroy)" +
                                (vivoScavalcato
                                    ? $" · veto BYPASSED: the flag is on, the boss looked alive (vita={enemy.CurrentLife:F0})"
                                    : string.Empty));
                }
            }
            catch (Exception e)
            {
                // An exception here would stop the whole scene spawn: log and continue.
                ModLog.Error($"[BossCorpse] ricerca dei cadaveri di boss fallita ({where}): " +
                             $"{e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>
        /// True when the boss is ALIVE and operational: active, not dead, with health, and with an
        /// enabled EnemyBehaviour (a brain the game is running). The definition matches the C-guard's
        /// purpose: the corpse a room recreates is the opposite - off/inert, or already Status.Dead.
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
        /// The scene used for boss flags, read from the GAME, not from <c>_scene</c>.
        ///
        /// <c>_scene</c> is written only by <c>EnemySyncManager.LevelLoaded</c>, which the game
        /// invokes AFTER spawn: on the first spawn pass of a load it still holds the previous room.
        /// <c>Core.LevelManager.currentLevel.LevelName</c> is already the new scene (LevelManager
        /// assigns it before spawning), so read it directly. If unavailable, fall back to the mod's
        /// <c>_scene</c> (previous behaviour), and the marker will say <c>fonte=ripiego</c>.
        /// </summary>
        private static string SceneForBossFlags(string modScene, out bool dalGioco)
        {
            dalGioco = false;

            try
            {
                if (Core.LevelManager != null && Core.LevelManager.currentLevel != null)
                {
                    string live = Core.LevelManager.currentLevel.LevelName;
                    if (!string.IsNullOrEmpty(live))
                    {
                        dalGioco = true;
                        return live;
                    }
                }
            }
            catch (Exception)
            {
                // Fall through: use the mod scene, which is the previous behaviour.
            }

            return modScene;
        }

        /// <summary>
        /// [BossCorpse] scena-flag= marker: one line per call, saying where the flag scene came from
        /// (game vs mod fallback) and whether the two disagree. Uses the game scene for the flag, the
        /// mod scene for the id.
        /// </summary>
        private static void LogBossCorpseScene(string scenaFlag, string modScene, string where, bool dalGioco)
        {
            try
            {
                string fonte = dalGioco
                    ? "gioco (Core.LevelManager.currentLevel.LevelName)"
                    : "fallback: game read unavailable, using the mod's scene";

                bool divergenza = !string.Equals(scenaFlag ?? string.Empty, modScene ?? string.Empty,
                                                 StringComparison.Ordinal);

                ModLog.Info($"[BossCorpse] scena-flag={(scenaFlag ?? "nessuna")} · mod={(modScene ?? "nessuna")} · " +
                            $"fonte={fonte} · divergence={(divergenza ? "yes" : "no")} · dove={where}");
            }
            catch (Exception)
            {
                // A marker that crashes silences the measurement.
            }
        }

        /// <summary>
        /// The first of the two <c>&lt;...&gt;_BOSSDEAD</c> flag names that is ON for this scene, or
        /// null. Two forms exist (whole scene and zone-level); both are tried. <c>GetFlag</c> is a
        /// pure read, so querying a flag that does not exist does not pollute the save.
        /// </summary>
        private static string FirstBossDeadFlagOn(string scene)
        {
            string scenaDaGioco = SceneForBossFlags(scene, out _);

            if (string.IsNullOrEmpty(scenaDaGioco))
                return null;

            string full = scenaDaGioco.ToUpperInvariant() + "_BOSSDEAD";
            if (Core.Events.GetFlag(full))
                return full;

            System.Text.RegularExpressions.Match zone = System.Text.RegularExpressions.Regex.Match(
                scenaDaGioco.ToUpperInvariant(), "^D\\d\\d[A-Z]?Z\\d\\d");

            if (!zone.Success)
                return null;

            string zoneFlag = zone.Value + "_BOSSDEAD";
            if (zoneFlag == full)
                return null;

            return Core.Events.GetFlag(zoneFlag) ? zoneFlag : null;
        }

        /// <summary>
        /// Resolves the boss to kill and reports where it was found (<c>via=</c>) for the marker.
        ///
        /// <c>ResolveEnemy</c> alone is not enough: <c>ReceiveEnemyKill</c> removes the id from
        /// <c>_enemies</c> BEFORE applying damage, and a boss has no spawn point to look up. So
        /// <c>_enemyIds</c> (instance → id) is used as the fallback, since it is the only map
        /// <c>ReceiveEnemyKill</c> does not touch.
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
        /// Last known position of every OTHER player, keyed by name, fed from the position packets.
        ///
        /// Reading the position off the scene object did not work: GetPosition() returned this
        /// machine's OWN coordinates on the host and nothing on the peer, so the distance rule
        /// compared an enemy against the local player twice and left two owners for one enemy.
        /// Caching what actually arrives removes that dependency.
        /// </summary>
        private static readonly Dictionary<string, Vector2> _remotePositions = new Dictionary<string, Vector2>();

        /// <summary>When each remote player's position arrived, so stale samples can be rejected.</summary>
        private static readonly Dictionary<string, float> _remotePositionsAt = new Dictionary<string, float>();

        /// <summary>
        /// Where a non-owned enemy is heading, from the last snapshot received. Snapshots arrive about
        /// 20/s; assigning them straight to transform.position makes the enemy jump 20 times a second.
        /// Following the target each frame turns those jumps back into motion, and the value is the
        /// remote machine's real simulation result, so nothing is invented.
        /// </summary>
        private static readonly Dictionary<string, Vector2> _remoteTargets = new Dictionary<string, Vector2>();

        /// <summary>
        /// The scene visit in which each <c>_remoteTargets</c> entry was written. <c>_remoteTargets</c>
        /// is deliberately not cleared on LevelLoaded, so an entry can come from a PREVIOUS visit of
        /// the same room. An entry matching the current visit means "the owner is transmitting it
        /// right now"; an older one is a leftover that must not be applied to a boss (see
        /// SkipBossFollow).
        /// </summary>
        private static readonly Dictionary<string, int> _remoteTargetsVisit = new Dictionary<string, int>();

        /// <summary>How many scenes have been loaded in this session: the current "visit".</summary>
        private static int _sceneVisit;

        /// <summary>Boss ids whose [Insegue] marker has already said the follow was skipped, once per visit.</summary>
        private static readonly HashSet<string> _bossFollowSkipLogged = new HashSet<string>();

        /// <summary>
        /// Deaths announced while this machine was in another scene, or before the enemy existed here.
        /// Applied at registration time instead, when the death has to be enforced.
        /// </summary>
        private static readonly HashSet<string> _pendingDeaths = new HashSet<string>();

        /// <summary>
        /// True while this machine is replicating a bonfire announced by someone else. Prevents the
        /// replication from being mistaken for a player sitting down if anything is ever added to the
        /// bonfire hook. Saved and restored (not cleared) because a replication can nest.
        /// </summary>
        private static bool _replicatingBonfire;

        /// <summary>True when the bonfire being processed was announced by another machine.</summary>
        public static bool IsReplicatingBonfire { get { return _replicatingBonfire; } }

        /// <summary>
        /// Every death this machine has announced, so a joiner can be told about them even when the
        /// announcer is no longer in the room. Not cleared on level load - crossing a level is exactly
        /// the case this exists for. Cleared wherever the GAME forgets its own consumed list (bonfire
        /// respawn, reset after death or save load), because at that point the enemies really are
        /// alive again on both machines. Only deaths made while connected are recorded.
        /// </summary>
        private static readonly HashSet<string> _announcedDeaths = new HashSet<string>();

        /// <summary>
        /// Enemies this machine has announced as REVEALED, so a late joiner also learns about them.
        /// Twin of <c>_announcedDeaths</c> on the same channel with the same reasoning and lifetime.
        /// </summary>
        private static readonly HashSet<string> _announcedBirths = new HashSet<string>();

        /// <summary>Enemies already reported as dead by the diagnostic snapshot, so each death is logged once.</summary>
        private static readonly HashSet<string> _killLogged = new HashSet<string>();

        /// <summary>
        /// Handles for which a resolution failure has already been reported. A peer can receive
        /// packets for an enemy it has already removed, which is normal; logging each one would bury
        /// the failures that matter.
        /// </summary>
        private static readonly HashSet<uint> _reportedResolveFailures = new HashSet<uint>();

        /// <summary>
        /// Last animation state hash seen per enemy, so a change can be logged once. The game has no
        /// single place where enemy animations are triggered (each behaviour calls Animator.Play
        /// itself); the only reliable observation point is the Animator's current state. Read every
        /// frame so a shorter-than-snapshot attack is still recorded; only changes are written.
        /// </summary>
        private static readonly Dictionary<string, int> _lastAnim = new Dictionary<string, int>();

        /// <summary>
        /// Last animation state this machine APPLIED from the owner, per enemy. Applying a state
        /// changes the Animator, and the change was then read back as news - transitions fed
        /// themselves. Remembering what was applied lets the animation log report only genuine local
        /// changes.
        /// </summary>
        private static readonly Dictionary<string, int> _appliedAnim = new Dictionary<string, int>();

        /// <summary>
        /// TEMPORARY (PerformanceProbe): the state the Animator showed at the moment we imposed a
        /// state, so the log can tell a transition we CAUSED from one the game made.
        /// <c>_appliedAnim</c> alone cannot do this because the Animator reports the old state for
        /// one more frame. Remove with the probe.
        /// </summary>
        private static readonly Dictionary<string, int> _appliedAnimPrev = new Dictionary<string, int>();

        /// <summary>
        /// TEMPORARY (PerformanceProbe): the playback position last seen per enemy, so the position
        /// the LEAVING state had reached can be reported. Remove with the probe.
        /// </summary>
        private static readonly Dictionary<string, float> _lastAnimSample = new Dictionary<string, float>();

        /// <summary>
        /// TEMPORARY (PerformanceProbe): when the current state began, on the transitions the game
        /// made. Echoes never reset it. Remove with the probe.
        /// </summary>
        private static readonly Dictionary<string, float> _lastAnimChangeAt = new Dictionary<string, float>();

        /// <summary>
        /// The gravity scale each frozen enemy had before this machine stopped its controller, so it
        /// can be given back exactly. Restoring a guessed 1 would be wrong: some enemies zero it
        /// deliberately. Instance id, not enemy id: this is a fact about the live object.
        /// </summary>
        private static readonly Dictionary<int, float> _frozenGravityScale = new Dictionary<int, float>();

        /// <summary>
        /// Snapshot of a boss's obstacle colliders the moment this machine reduces it to a puppet, so
        /// it can be restored exactly as it was. The game itself toggles these (Crisanta teleports,
        /// barrier clips), so restoring "on by default" would put back a barrier an animation had
        /// turned off. Keyed by instance id: a fact about the live object, must not outlive it.
        /// </summary>
        private struct FrozenCollision
        {
            /// <summary>The boss's physics body (<c>Enemy.Controller.SmartPlatformCollider</c>), if any.</summary>
            public SmartPlatformCollider Smart;

            /// <summary>The two fields the game reads and writes: <c>enabled</c> and <c>EnableCollision2D</c>.</summary>
            public bool SmartEnabled;
            public bool SmartCollision2D;

            /// <summary>The boss's child obstacle barrier (<c>ElderBrotherAnimatorInyector.blockCollider</c>).</summary>
            public GameObject Barrier;

            /// <summary>Whether the barrier was ON when the boss became a puppet.</summary>
            public bool BarrierActive;
        }

        /// <summary>Obstacle colliders removed from each puppet boss, to restore on wake-up.</summary>
        private static readonly Dictionary<int, FrozenCollision> _frozenCollision =
            new Dictionary<int, FrozenCollision>();

        /// <summary>Fraction of the remaining gap closed per frame while following a remote snapshot.</summary>
        private const float FOLLOW_LERP = 0.25f;

        /// <summary>Beyond this the enemy is snapped, so a real teleport is not smoothed.</summary>
        private const float FOLLOW_SNAP_DISTANCE = 6f;

        private static bool _isHost;
        private static float _sendTimer;
        private static float _rescanTimer;
        private static float _lastSweep;

        // True once the id table has been built for this scene. Guards the fallback in UpdateCore.
        private static bool _spawnFinalized;

        // Diagnostics: enough to see whether the feature is doing anything in game.
        private static string _scene = string.Empty;
        private static int _updatesSent;
        private static int _updatesReceived;
        private static int _damageSent;
        private static int _damageApplied;
        private static int _killsSent;
        private static int _registrations;

        /// <summary>
        /// Scene entities rejected at registration, by ID: created by the game's spawn pipeline but
        /// never activated, so no code has run on them. <c>IsInertScenery</c> is the decision;
        /// <c>RegisterEnemies</c> offers the same inert object every rescan, so a counter would
        /// measure ATTEMPTS rather than entities. The id makes the number a measurement.
        /// </summary>
        private static readonly List<string> _inertIds = new List<string>();

        /// <summary>
        /// How many entries of <c>_inertIds</c> have already been written to the log. The marker
        /// speaks when the measurement GROWS, not on every rescan or every "multiplayer sync".
        /// </summary>
        private static int _inertReported;

        /// <summary>
        /// Deaths learned from another machine's join announcement, i.e. kills that happened before
        /// this machine was connected. Reported so the join fix is visible rather than inferred.
        /// </summary>
        private static int _deathsAppliedFromPeers;
        private static int _duplicateIds;
        private static int _listFingerprint;
        /// <summary>When the divergence was last reported, so it is reported again after a cooldown. Zero means never.</summary>
        private static float _fingerprintMismatchReported;

        /// <summary>How often a divergence may be reported. Long enough not to flood, short enough to see.</summary>
        private const float FINGERPRINT_REPORT_COOLDOWN = 15f;

        /// <summary>Total times a divergence was reported, so a recurring one is visible at a glance.</summary>
        private static int _fingerprintMismatches;

        /// <summary>
        /// Set whenever the id list is appended to or pruned, so the fingerprint is recomputed once
        /// per pass. Without this the published fingerprint described a list that had already
        /// changed, and the divergence check reported enemies that were not there.
        /// </summary>
        private static bool _idsDirty;
        private static readonly HashSet<string> _countedDuplicates = new HashSet<string>();

        /// <summary>
        /// One-line health summary, surfaced by the "multiplayer sync" command. Built so the two
        /// machines can be compared: tracked/spawn point counts must match.
        /// </summary>
        public static string Describe()
        {
            // Computed rather than counted, so rescans cannot inflate it.
            int notSpawned = _ids.Count - _enemies.Count;

            string warning = string.Empty;
            if (_duplicateIds > 0)
                warning += $" WARNING: {_duplicateIds} spawn points with duplicate identity (ids not unique!)";
            if (_fingerprintMismatchReported != 0)
                warning += " WARNING: list fingerprint differs from the host (see log)!";
            if (notSpawned > 0)
                warning += $" NOTE: {notSpawned} enemies not yet created by the spawn point";

            // [EnemyInert] — must live BEFORE the return. Previously it sat after, CS0162 warned,
            // and the line never appeared in any log. Condition is measured, not chosen: _inertIds
            // only grows where IsInertScenery has already rejected the entity, and holds distinct ids.
            if (_inertIds.Count > _inertReported)
            {
                ModLog.Info($"[EnemyInert] {_inertIds.Count} entita' di scena ignorate " +
                            $"(created by the game but never active: no code ran, so immobile by construction) " +
                            $"· id: {string.Join(" · ", _inertIds.ToArray())}");
                _inertReported = _inertIds.Count;
            }

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
        }

        /// <summary>Maps a damaged GameObject to its network handle. Used by the damage patch.</summary>
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
        /// The network id STRING of an enemy, for the one channel that reaches a player in ANOTHER
        /// SCENE. A handle is deliberately SCENE-LOCAL (<c>ResolveByHandle</c> searches <c>_ids</c>,
        /// which holds only the loaded scene), so the execution announcement travels on the death
        /// channel as an id string. Mirrors <c>TryGetEnemyHandle</c>, read-only, safe to call from a
        /// Harmony postfix.
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
        /// The wire handle for an id: a 32-bit hash of the id string, not its position in a list.
        /// Position indices failed because the two machines can hold lists of different sizes, and
        /// every index past the shorter list gets rejected. FNV-1a is written out because
        /// <c>string.GetHashCode()</c> is not guaranteed to agree across processes or runtimes.
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
            // second dictionary that must stay in step with the id list.
            for (int i = 0; i < _ids.Count; i++)
            {
                if (HandleOf(_ids[i]) == handle)
                    return ResolveEnemy(_ids[i]);
            }

            // Silent on purpose: a peer can receive packets for an already-removed enemy.
            return null;
        }

        /// <summary>
        /// The local copy of the enemy a handle names, or null. Used by the enemy→player channel:
        /// <c>PenitentDamageArea.TakeDamage</c> reads <c>hit.AttackingEntity.transform.position</c> to
        /// orient the pain animation, so the attacker must be a real GameObject; the local copy of the
        /// boss has the same position and name and invents nothing. Silent like
        /// <c>ResolveByHandle</c>: an enemy can have already disappeared from this scene, and that is
        /// not an error (the caller has its own fallback).
        /// </summary>
        public static Enemy EnemyForHandle(uint handle)
        {
            return ResolveByHandle(handle);
        }

        /// <summary>
        /// Reports, once per handle, that a packet could not be matched to an enemy here. Two cases
        /// are kept apart: (a) the handle is not in <c>_ids</c> at all; (b) the handle is in
        /// <c>_ids</c> but the enemy resolves to null (the "already removed" case).
        /// <c>ResolveByHandle</c> conflates them, so this reads <c>FindIdOfHandle</c> and
        /// <c>ResolveEnemy</c> separately. Deduplicated by handle.
        /// </summary>
        private static void ReportResolveFailure(uint handle)
        {
            if (!_reportedResolveFailures.Add(handle))
                return;

            if (!FindIdOfHandle(handle, out string id))
            {
                ModLog.Info($"[EnemySync] t={Time.time:F1} handle={handle:X8} not in _ids " +
                            $"(ids={_ids.Count}, spawn={_spawnPoints.Count})");
                return;
            }

            ModLog.Info($"[EnemySync] t={Time.time:F1} {FullId(id)} handle={handle:X8} " +
                        $"already removed (ResolveEnemy=null)");
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
        /// Every spawn point in the scene, with the flags that decide whether it spawns. The game
        /// tracks a defeated spawn point by POSITION (<c>List&lt;Vector3&gt;</c>), and
        /// <c>SpawningId</c> is per-process. <c>CreateEnemy</c> returns early on
        /// EnemySpawnDisabled, Consumed, or HasEnemySpawned; reading these on both machines shows why
        /// one machine spawns an enemy the other considers dead.
        /// </summary>
        public static List<string> DumpSpawnPoints()
        {
            List<string> lines = new List<string>();
            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();
            lines.Add($"spawn points in the scene: {points.Length}");

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
                          $"enemy={(spawned == null ? "none" : spawned.name)} " +
                          DescribeEnemyState(spawned));
            }

            return lines;
        }

        /// <summary>
        /// The flags that separate a live enemy from the inert "ghost". The ghost is visible, idle,
        /// untargetable, with no health - Dead set but the death sequence never finished. Printing
        /// these shows which state a stuck enemy is really in.
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

            // Alpha below 0.5 is not decoration: the game switches the damage collider off when the
            // sprite is semi-transparent, one concrete way an enemy looks present yet cannot be hit.
            SpriteRenderer renderer = enemy.GetComponentInChildren<SpriteRenderer>();
            string alpha = renderer != null ? renderer.color.a.ToString("F2") : "n/d";

            // Range is the other deactivation path: some enemy types disable themselves when the
            // target is farther than ActivationRange.
            return $"vivo={(!enemy.Dead && enemy.CurrentLife > 0f)} Dead={enemy.Dead} " +
                   $"vita={enemy.CurrentLife:F1} vulnerabile={enemy.IsVulnerable} " +
                   $"intargettabile={untouchable} invulnerabile={invulnerable} " +
                   $"inCamera={onCamera} hurt={hurt} idle={idle} alpha={alpha} " +
                   $"distTarget={enemy.DistanceToTarget:F1} range={enemy.ActivationRange:F0} " +
                   $"attivo={enemy.gameObject.activeSelf} " +
                   $"behaviour={(enemy.EnemyBehaviour == null ? "assente" : enemy.EnemyBehaviour.enabled.ToString())}";
        }

        /// <summary>
        /// The spawn points the game ITSELF considers defeated, plus a per-distinct-position listing.
        /// The game identifies a defeated spawn point by POSITION (<c>List&lt;Vector3&gt;</c>); two
        /// points sharing a position are the same entry to the game, so the count of distinct
        /// positions is the honest number.
        /// </summary>
        public static List<string> DumpConsumedSpawnPoints()
        {
            List<string> lines = new List<string>();

            EnemySpawner spawner = Core.Logic != null ? Core.Logic.EnemySpawner : null;
            if (spawner == null)
            {
                lines.Add("EnemySpawner not available");
                return lines;
            }

            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();

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
                      $"(spawn points consumed by them: {consumedPoints} of {points.Length})");
            return lines;
        }

        /// <summary>
        /// Per-enemy listing. Run "multiplayer sync" on both machines: the handles must be identical.
        /// </summary>
        public static List<string> Dump()
        {
            List<string> lines = new List<string>();
            lines.Add($"enemies={_enemies.Count} spawnPoints={_spawnPoints.Count} ids={_ids.Count} " +
                      $"fingerprint={_listFingerprint} duplicati={_duplicateIds}");

            if (_duplicateIds > 0)
            {
                lines.Add("WARNING: the same enemy received more than one id. " +
                          "If the value is not 0 on both machines, the two lists diverge.");
            }

            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                Enemy enemy = ResolveEnemy(id);

                if (enemy == null)
                {
                    lines.Add($"[{i,3}] handle={HandleOf(id)} ID {id} -> enemy not resolved");
                    continue;
                }

                lines.Add($"[{i,3}] handle={HandleOf(id)} ID {id}");
            }
            return lines;
        }

        /// <summary>
        /// Snapshot needed by the two markers of the id-list check: the three counters and the id
        /// list. Copies the list, because the list travels on the wire while the mod keeps mutating
        /// it. Read-only.
        /// </summary>
        internal static void MarkerSnapshot(out int spawnPoints, out int ids, out int fingerprint, out List<string> idList)
        {
            spawnPoints = 0;
            ids = 0;
            fingerprint = 0;
            idList = new List<string>();

            try
            {
                spawnPoints = _spawnPoints.Count;
                ids = _ids.Count;
                fingerprint = _listFingerprint;

                for (int i = 0; i < _ids.Count; i++)
                    idList.Add(_ids[i]);
            }
            catch (Exception e)
            {
                ModLog.Error($"[Impronta] list snapshot failed ({e.GetType().Name}): {e.Message} - " +
                             "il marker tace, il gioco no");
            }
        }

        /// <summary>
        /// True while damage relayed by the host is being applied, so the damage patch lets it
        /// through instead of forwarding it again (which would loop).
        /// </summary>
        public static bool IsApplyingRemoteDamage { get; private set; }

        /// <summary>
        /// True when this client drives enemy simulation. Also true while offline, so single-player
        /// keeps vanilla behaviour.
        /// </summary>
        public static bool IsHost => _isHost || !Main.Multiplayer.NetworkManager.IsConnected;

        /// <summary>Set by NetworkManager when the server assigns (or revokes) the host role.</summary>
        public static void SetHost(bool isHost)
        {
            bool wasHost = _isHost;
            _isHost = isHost;
            ModLog.Info($"[EnemySync] Host role: {isHost}");

            // Note the asymmetry with IsHost: that property also reports true while offline, so a
            // connected-but-demoted client must be brought in line here.
            if (isHost && !wasHost)
            {
                // Promoted (typically because the previous host disconnected): take over simulation.
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

                // The hold timer restarts from now. Ownership is also claimed here: before this, a
                // promoted machine owned NOTHING and waited for DecideOwnership to grant it, which
                // needs a live connection and several seconds. This machine is the host now, so by
                // definition it simulates.
                for (int i = 0; i < _ids.Count; i++)
                {
                    EnemyState state;
                    if (!_lastSent.TryGetValue(_ids[i], out state))
                        continue;

                    state.OwnedByLocal = true;
                    state.OwnedByRemote = false;
                    state.OwnershipKnown = true;
                    state.OwnerSince = Time.time;
                    ForgetBossLifeAlignment(state);   // promotion: ownership changed everywhere
                }
            }
            else if (!isHost && wasHost)
            {
                // OFFLINE: the demotion is not a demotion, and pausing freezes the session.
                //
                // When the host's connection drops, Multiplayer.OnDisconnect calls SetHost(false),
                // but IsHost is `_isHost || !IsConnected`, so the machine is STILL the host from the
                // rest of the system's point of view, and there is no other player to hand enemies
                // to. Pausing them here left every enemy suspended mid-stride with no way to
                // recover: DecideOwnership is behind `if (!IsConnected) return;`.
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

                        // ResumeEnemy gives back both the behaviour tree AND the controller.
                        Enemy enemy = ResolveEnemy(_ids[i]);
                        if (enemy != null)
                            ResumeEnemy(enemy);
                    }

                    _remotePositions.Clear();
                    _remotePositionsAt.Clear();
                    return;
                }

                // Demoted: stop simulating, otherwise the AI keeps running locally and fights the
                // state the real host is broadcasting.
                for (int i = 0; i < _ids.Count; i++)
                {
                    Enemy enemy = ResolveEnemy(_ids[i]);
                    if (enemy == null)
                        continue;

                    PauseEnemy(enemy);
                }

                // And stop BELIEVING it owns them. OwnershipKnown is cleared so the next token from
                // the new host is not a no-op.
                for (int i = 0; i < _ids.Count; i++)
                {
                    EnemyState state;
                    if (!_lastSent.TryGetValue(_ids[i], out state))
                        continue;

                    state.OwnedByLocal = false;
                    state.OwnershipKnown = false;
                    state.OwnerSince = 0f;
                    state.OwnedByRemote = false;
                    ForgetBossLifeAlignment(state);   // demotion: life must resume from the new host
                }

                _remotePositions.Clear();
                _remotePositionsAt.Clear();
            }
        }

        /// <summary>
        /// Hands an enemy over to the host: freeze it completely, so the state the real owner sends
        /// is what shows. Pausing the behaviour tree alone is not enough:
        /// <c>PlatformCharacterController.Update</c> keeps running and overwrites any transform
        /// write with its own physics state, and some enemies restart their own tree every frame
        /// when the local player is within range. Disabling the controller and the behaviour
        /// component is the lever.
        ///
        /// The enemy is left active on purpose: an inactive GameObject delivers no Update, including
        /// the one that would apply the received position.
        /// </summary>
        /// <summary>
        /// The behaviour tree component of an enemy, however that type exposes it.
        ///
        /// <c>Enemy.EnemyBehaviour</c> is NOT the general answer: only 17 of 77 Enemy subclasses call
        /// the setter. The other 60 keep the component on a property of their own and never touch the
        /// base one; reading only the base property made PauseEnemy return early for 78% of enemy
        /// types. The lookup runs on ownership transitions, not per frame, so it is not cached.
        /// </summary>
        private static EnemyBehaviour FindBehaviour(Enemy enemy)
        {
            if (enemy == null)
                return null;

            EnemyBehaviour behaviour = enemy.EnemyBehaviour;
            if (behaviour != null)
                return behaviour;

            behaviour = enemy.GetComponent<EnemyBehaviour>();
            if (behaviour != null)
                return behaviour;

            return enemy.GetComponentInChildren<EnemyBehaviour>();
        }

        private static void PauseEnemy(Enemy enemy, string reason = null)
        {
            FreezePhysics(enemy);
            _puppetEnemies.Add(enemy);

            // PauseBehaviour is called UNCONDITIONALLY. The NodeCanvas tree is ticked through
            // MonoManager.add_onUpdate, not through EnemyBehaviour.Update, so disabling the component
            // does not stop the tree on its own. Several behaviours restart the tree directly, so
            // guarding on !behaviour.enabled left a real gap; PauseBehaviour is idempotent and cheap.
            var behaviour = FindBehaviour(enemy);
            if (behaviour == null)
                return;

            // One call, AFTER the action: a throw in the diagnostic must not cancel the pause, and
            // two calls (before + after) killed the log deduplication because the key varied.
            behaviour.PauseBehaviour();
            behaviour.enabled = false;

            LogTreeState(enemy, behaviour, reason ?? "?");
        }

        /// <summary>
        /// Stops the enemy's own physics from fighting the position the owner sends. Clearing
        /// velocity and gravity as well as disabling the controller matters for the moment the enemy
        /// is handed back. GravityScale is saved for the handover, not for its own effect (it has
        /// none while the controller is disabled).
        ///
        /// Runs once per frame per non-owned enemy, on a handful of field writes.
        /// </summary>
        private static void FreezePhysics(Enemy enemy)
        {
            // FIX ① first: a puppet must not be SOLID. Disabling the controller and zeroing gravity
            // leaves the colliders on, which turns a puppet boss into an invisible wall.
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
            // HSpeed and VSpeed are not separate states (set_HSpeed writes m_vel.x, set_VSpeed writes
            // m_vel.y), so Velocity covers both; the others are cheap writes for clarity.
            physics.Velocity = Vector3.zero;
            physics.Acceleration = Vector3.zero;
            physics.HSpeed = 0f;
            physics.VSpeed = 0f;
            physics.GravityScale = 0f;
        }

        /// <summary>
        /// FIX ① — Removes the blocking colliders from a puppet boss, and nothing else.
        ///
        /// The defect: FreezePhysics disables the controller and gravity but leaves the colliders on,
        /// so a boss this machine does not simulate stays STILL AND SOLID. If its sprite covers a
        /// trigger, the trigger cannot be reached.
        ///
        /// The two levers are the game's own: SmartPlatformCollider (enabled + EnableCollision2D,
        /// the same fields the game itself toggles), and blockCollider (the boss's child barrier,
        /// toggled by clips).
        ///
        /// No writes to Status, Stats.Life, flags, Kill(), Damage(), Behaviour.Death(), cutscenes,
        /// or BossFightManager. The boss stays alive with its bar and its damage; it just stops being
        /// a wall. Bosses only (<c>IsBossByNamespace</c>), because that is what the defect was
        /// measured on. The barrier is reapplied on every call because the puppet's Animator keeps
        /// running and a clip can re-enable it; SetActive(false) is idempotent.
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
                                $"barrier={(saved.Barrier == null ? "n/a" : (saved.BarrierActive ? "off" : "already off"))} " +
                                $"vita={enemy.CurrentLife:F0}) - stays ALIVE with its damage: it is no longer a wall");
                }
            }
            catch (Exception e)
            {
                // An exception here would abort PauseEnemy; log and let the boss stay solid.
                ModLog.Error($"[BossSolido] spegnimento dei collisori d'ostacolo fallito: " +
                             $"{e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>
        /// The obstacle barrier of the boss, if that type has one. Only ElderBrother has a field
        /// named <c>blockCollider</c> in the whole game assembly, so a direct cast is used rather
        /// than reflection. Other bosses have their body as the wall, covered by SmartPlatformCollider.
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
        /// Gives an enemy back to its own AI, on the machine that now owns it. PauseBehaviour only
        /// stopped the tree, not the Animator, so a non-owner keeps animating; what a paused enemy
        /// could not do is decide to attack, which is why the owner runs the real AI.
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

            // No early return on enabled: an enemy can sit with its component enabled and its tree
            // stopped, a state the game itself produces. StartBehaviour only restarts when the tree
            // is stopped, so calling it unconditionally is exactly right. The old early return left
            // the tree permanently stopped - this is the only caller of StartBehaviour in the project.
            behaviour.enabled = true;
            behaviour.StartBehaviour();

            LogTreeState(enemy, behaviour, "resume:StartBehaviour");
        }

        /// <summary>
        /// TEMPORARY (diagnostics): reports the state of an enemy's behaviour tree, but only when it
        /// changes, so a per-tick method costs a handful of lines per session instead of thousands.
        /// </summary>
        private static void LogTreeState(Enemy enemy, EnemyBehaviour behaviour, string esito)
        {
            // behaviour.BehaviourTree must NOT be read directly: its getter is not public, and Mono
            // enforces that at runtime. Reading it threw on every call, aborting UpdateCore.
            // isRunning is reached reflectively via ReadTreeRunning instead.
            string id = IdOf(enemy);
            if (string.IsNullOrEmpty(id))
                return;

            bool enabled = behaviour != null && behaviour.enabled;

            string running = ReadTreeRunning(behaviour);

            // GotParry is part of the state: a WheelCarrier has no tree, its Update returns early
            // when GotParry is true, and the enemy looks ASLEEP without anything else changing.
            // GotParry is a public property, so it is read directly.
            bool gotParry = behaviour != null && behaviour.GotParry;

            string key = enabled + "|" + running + "|" + gotParry;

            string previous;
            if (_treeStateLogged.TryGetValue(id, out previous) && previous == key)
                return;

            _treeStateLogged[id] = key;

            Vector3 p = enemy.transform.position;

            // sprite and collider are reported because they are how the "hidden but present" family
            // shows itself: the reveal flips them on, so reading them beats deducing them.
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
        /// Reads <c>BehaviourTree.isRunning</c> reflectively, returning "?" if it cannot be read.
        /// Reflection replaces a direct call that crashed the sync: the getter is not public, and
        /// Mono refuses the call at runtime even though it compiles. The resolution is cached and
        /// failure disables the diagnostic for the session.
        /// </summary>
        private static string ReadTreeRunning(EnemyBehaviour behaviour)
        {
            if (behaviour == null)
                return "none";

            if (_treeRunningUnavailable)
                return "unavailable";

            try
            {
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
                _treeRunningUnavailable = true;
                ModLog.Error("[EnemyTree] isRunning unreadable (" + e.GetType().Name + "): " + e.Message +
                             " - no further retry this session");
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
        /// Last logged decision of the dead-owner branch of DecideOwnership, per enemy, so the
        /// [Proprieta'] marker is written when the DECISION changes, not on every evaluation.
        /// Diagnostics only - the reason is not part of the key.
        /// </summary>
        private static readonly Dictionary<string, string> _deadReleaseLogged = new Dictionary<string, string>();

        /// <summary>
        /// Gives the enemy its own physics back, mirroring FreezePhysics. The gravity scale is
        /// restored from the value saved when frozen, never assumed (some enemies zero it
        /// deliberately). Must NOT skip on "controller already enabled": the game's own OnUpdate
        /// turns it on and sets GravityScale itself for several types, and skipping would leave the
        /// saved value orphaned.
        /// </summary>
        private static void ThawPhysics(Enemy enemy)
        {
            // FIX ① symmetric: the boss this machine resumes must be SOLID exactly as it was left.
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
        /// FIX ① — Reverse of <c>FreezeCollision</c>: gives the boss back the obstacle colliders,
        /// with the values READ at freeze time (see <c>FrozenCollision</c>), not "re-enabled". If no
        /// snapshot exists (boss never became a puppet, or wake-up runs twice), nothing is touched.
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

                // If the barrier was already off at freeze time, it stays off: it is owned by clips,
                // and re-enabling would override an animation's choice.
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
        /// Begins a scene. Called from <c>Multiplayer.OnLevelLoaded</c>.
        ///
        /// Must NOT register or finalise: the game calls <c>SpawnEnemiesOnLoad</c> before
        /// <c>OnLevelLoaded</c>, so registering here produced ids with an empty scene name that were
        /// then thrown away, and every rescan minted a fresh id for the same enemy (7 enemies
        /// became 56 ids). It is also what made the two machines diverge, because they differ in
        /// WHEN each learns the scene name relative to the spawn.
        /// </summary>
        /// <summary>
        /// Gives back every enemy this machine owns, because it is about to leave the scene.
        ///
        /// Ownership is scene memory: LevelLoaded wipes ids on the machine that changes scene, but
        /// the machine that STAYS keeps them. Without this, the host that walks out still holds a
        /// token for enemies it no longer remembers, and the peer stays paused forever. The leave
        /// packet has no name, so the peer cannot tell who left; the machine leaving knows what it
        /// owned, in which scene, and who is present.
        ///
        /// Must be called BEFORE <c>SendLeaveScene</c>: the server filters ownership by the SENDER's
        /// scene (which becomes empty on leave), so a token queued after that line is never
        /// delivered. Does not resolve the enemy: reading the handle from the id works even if the
        /// enemy is already gone, and a token for an unresolvable enemy is harmless.
        /// </summary>
        public static void ReleaseOwnedEnemiesBeforeLeaving()
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            // Only the arbiter publishes ownership; a peer obeys, its OwnedByLocal reflects a token
            // the host sent.
            if (!IsHost)
                return;

            int released = 0;
            foreach (KeyValuePair<string, EnemyState> entry in _lastSent)
            {
                if (!entry.Value.OwnedByLocal)
                    continue;

                // Latch the publication, same as the send loop does, so a return before LevelLoaded
                // clears the table does not publish the same hand-back twice.
                entry.Value.OwnershipKnown = true;
                entry.Value.LastPublishedOwnerIsMe = false;

                Main.Multiplayer.SendEnemyOwnership(HandleOf(entry.Key), true);
                released++;
            }

            ModLog.Info($"[Proprieta''] uscendo dalla scena ho restituito {released} nemici sui " +
                        $"{_lastSent.Count} I had in memory (scena={_scene})");
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

            // The instances of the scene being unloaded. Their ids must not be reused by the next
            // scene's enemies; Unity compares destroyed objects as null so stale keys cannot match.
            _enemyIds.Clear();
            // _remoteTargets is deliberately NOT cleared: ids are qualified by SCENE, so entries from
            // another room cannot collide, and keeping them makes a RE-ENTRY safe. Stale ids resolve
            // to null and are skipped.
            _lastAnim.Clear();
            _appliedAnim.Clear();
            _appliedAnimPrev.Clear();
            _lastAnimSample.Clear();
            _lastAnimChangeAt.Clear();
            _treeStateLogged.Clear();
            _deadReleaseLogged.Clear();
            _bossOwnLogged.Clear();
            _bossLifeZeroLogged.Clear();
            _frozenGravityScale.Clear();
            _frozenCollision.Clear();
            _bossIds.Clear();
            _bossKillsApplied.Clear();
            _bossDeathsApplied.Clear();
            // PUNTO 0: a NEW VISIT. _remoteTargets is not cleared, but its entries age:
            // _remoteTargetsVisit says which ones were refreshed in THIS visit, and a stale one is
            // not applied to a boss (see SkipBossFollow). A new visit must also re-speak: the
            // [Insegue] marker is deduplicated per id.
            _sceneVisit++;
            _bossFollowSkipLogged.Clear();
            _hiddenBossCorpses.Clear();
            _deathPendingSince.Clear();
            _puppetEnemies.Clear();
            _orientationLabels.Clear();

            // TEMPORARY (PerformanceProbe): re-read the Animator names for the new scene.
            PerformanceProbe.ResetForScene();
            _killLogged.Clear();
            _reportedResolveFailures.Clear();

            RemoveJunkDeathFlags();

            _sendTimer = 0f;
            _rescanTimer = 0f;
            _lastSweep = 0f;

            // Boss-fight state re-read for the new scene; the armed flag forces a line even when the
            // state does not change (otherwise "OFF" would never appear in the log).
            _lastBossFightStateCheck = 0f;
            _bossFightStateArmed = true;

            // First snapshot of a new scene written immediately instead of waiting out the interval.
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

            // [EnemyInert]: measurement is PER SCENE, same as _duplicateIds and _registrations above.
            // The id is qualified by scene, so old entries cannot come back; clearing makes the
            // marker describe the current room and lets a new visit re-speak.
            _inertIds.Clear();
            _inertReported = 0;

            ModLog.Info($"[EnemySync] Scene {scene}: level loaded (IsHost: {IsHost})");
        }

        /// <summary>
        /// Cheap order-sensitive hash of the id list. Both machines must produce the same value. Must
        /// be recomputed whenever the list changes: appends and prunes after the initial build left
        /// the published fingerprint describing a list that no longer existed, which produced the
        /// false "IMPRONTA DIVERSA".
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
        /// Also re-arms the divergence report, so a mismatch caught mid-change does not silence
        /// every later, real one. Re-arming is rate-limited in ReceiveEnemyUpdate rather than here.
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
        /// The single point that finalises the id table: registers everything, sorts, and
        /// fingerprints. Called right after the game spawns enemies, the first moment enemies exist
        /// and the first moment the scene name is meaningful. Because this is the only place that
        /// builds the table, both machines build it from the same state, which is what makes the
        /// indices agree.
        /// </summary>
        public static void OnEnemiesSpawned()
        {
            _registered.Clear();
            _ids.Clear();
            _enemies.Clear();
            _lastSent.Clear();
            _registrations = 0;

            // [BossView] FIRST HALF: the scene as it is NOW, same frame as spawn, before the mod
            // registers anything. It must be here: DestroyDeadEnemy (called by RegisterEnemy on the
            // _pendingDeaths branch) switches it off, and a frame later the measurement would say
            // nothing. This is what lets a log say whether the mod or the scene switched a boss off.
            LogBossViewBefore();

            // C) The boss corpse must not remain visible on re-entry. Placed between the two
            // [BossView] markers: the line above is the scene as the game leaves it, the line below
            // is the scene after the mod's actions. Before RegisterEnemies so a recognised corpse is
            // already off when registration encounters it, and RegisterEnemy skips it by name
            // (_hiddenBossCorpses) - otherwise the mod re-enables the tree and gets an invisible
            // fighting boss.
            //
            // _scene is only the FALLBACK: the scene used for flag lookup is read from the game by
            // SceneForBossFlags (Core.LevelManager.currentLevel.LevelName), because in this first
            // pass _scene still holds the PREVIOUS room. Before the fix, _scene decided, so this
            // read the death flag of another room - and with FIX ② an ON flag bypasses the "looks
            // ALIVE" veto, which could switch off a live boss.
            HideDefeatedBosses(_scene, "ingresso in scena");

            RegisterEnemies(_scene);

            // [BossView] SECOND HALF: identical measurement, same frame, right after registration.
            // The DIFFERENCE between this line and the one above is the answer to "is the mod
            // switching the boss off?": if attivi drops while pendingMorti does not change, the mod
            // did it (RegisterEnemy → DestroyDeadEnemy); otherwise the scene did.
            LogBossViewAfter();

            _ids.Sort(string.CompareOrdinal);
            _listFingerprint = ComputeFingerprint();
            _idsDirty = false;
            _spawnFinalized = true;

            ModLog.Info($"[EnemySync] Post-spawn: {_enemies.Count} enemies / {_spawnPoints.Count} spawn points / {_ids.Count} ids");
        }

        // ============================================================================================
        // [BossView] / [BossJoin] MARKERS — LOG ONLY, ZERO BEHAVIOUR, ZERO PATCH
        //
        // These methods read nothing that was not already read and write NOTHING: they count and
        // log. They only run inside OnEnemiesSpawned (once per spawn, not per frame) and one frame
        // after LevelLoaded. Every line is in try/catch: on failure they log the same marker with
        // "?" in place of the numbers.
        // ============================================================================================

        /// <summary>
        /// [BossView] line 1 — <c>scena · mod · attivo · statusDead · pendingMorti · bossVivi</c>,
        /// BEFORE registration. <c>scena=</c> is the scene the boss is ENTERING (SceneForBossView,
        /// read from the game); <c>mod=</c> is what the mod currently believes. Before this marker
        /// the two were the same field and it printed the PREVIOUS scene.
        /// </summary>
        private static void LogBossViewBefore()
        {
            try
            {
                int attivo;
                int statusDead;
                int bossVivi;
                CountSceneEnemies(out attivo, out statusDead, out bossVivi);

                ModLog.Info($"[BossView] scena={SceneForBossView()} · mod={SceneForMarker()} · attivo={attivo} · " +
                            $"statusDead={statusDead} · pendingMorti={_pendingDeaths.Count} · bossVivi={bossVivi}");
            }
            catch (Exception e)
            {
                ModLog.Info($"[BossView] scena={SceneForBossView()} · mod={SceneForMarker()} · attivo=? · " +
                            $"statusDead=? · pendingMorti=? · bossVivi=? (lettura fallita: {e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// [BossView] line 2 (same frame) — <c>after-registration · active · pendingDeaths</c>.
        /// </summary>
        private static void LogBossViewAfter()
        {
            try
            {
                int attivo;
                int statusDead;
                int bossVivi;
                CountSceneEnemies(out attivo, out statusDead, out bossVivi);

                ModLog.Info($"[BossView] after-registration · active={attivo} · pendingDeaths={_pendingDeaths.Count}");
            }
            catch (Exception e)
            {
                ModLog.Info($"[BossView] after-registration · active=? · pendingDeaths=? " +
                            $"(lettura fallita: {e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// The three numbers of the marker, in one pass.
        ///
        /// <c>attivo</c> = how many Enemy are ACTIVE in scene (activeInHierarchy), not how many
        /// exist: an inactive boss must not count here, or "off" and "alive" would give the same
        /// number. <c>statusDead</c> = how many of those have Status.Dead. <c>bossVivi</c> = how many
        /// of those are bosses (Bosses namespace) and not dead.
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
                    continue; // exists but off: counted by Post-spawn, not by this marker

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
        /// The same test as <c>IsBoss</c> (type namespace), without id and without touching the
        /// <c>_bossIds</c> cache: the marker must read state and leave no trace in any table.
        /// </summary>
        private static bool IsBossByNamespace(Enemy enemy)
        {
            string space = enemy.GetType().Namespace;
            return !string.IsNullOrEmpty(space) &&
                   space.StartsWith("Gameplay.GameControllers.Bosses", System.StringComparison.Ordinal);
        }

        /// <summary>Scene name for the markers, with a fallback on the current level if <c>_scene</c> is empty.</summary>
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
                return "?";
            }
        }

        /// <summary>
        /// The scene the [BossView] marker must print — the one the boss is ENTERING, not what the
        /// mod still holds in memory.
        ///
        /// <c>SceneForMarker()</c> prefers <c>_scene</c>, and <c>_scene</c> is written by
        /// <c>LevelLoaded</c>, which the game reaches AFTER spawn. On the first pass <c>scena=</c>
        /// therefore printed the PREVIOUS scene. The correct scene already exists at spawn time:
        /// <c>Core.LevelManager.currentLevel.LevelName</c>. <c>SceneForMarker()</c> is not changed
        /// because <c>[BossFight]</c> and <c>[Proprieta']</c> also use it, and they run after
        /// <c>LevelLoaded</c> where <c>_scene</c> is correct.
        /// </summary>
        private static string SceneForBossView()
        {
            try
            {
                if (Core.LevelManager != null && Core.LevelManager.currentLevel != null)
                {
                    string live = Core.LevelManager.currentLevel.LevelName;
                    if (!string.IsNullOrEmpty(live))
                        return live;
                }
            }
            catch (Exception)
            {
                // Fall through: the marker must print something.
            }

            return SceneForMarker();
        }

        /// <summary>[BossJoin] — how many live bosses are in the scene (marker 2, one frame after load).</summary>
        internal static int CountLiveBossesInScene()
        {
            int attivo;
            int statusDead;
            int bossVivi;
            CountSceneEnemies(out attivo, out statusDead, out bossVivi);
            return bossVivi;
        }

        /// <summary>
        /// [BossJoin] — the existing <c>OtherPlayerInMyScene</c>, exposed for the one caller outside
        /// this file (the marker in <c>Multiplayer.OnLevelLoaded</c>). One-line forward, no new logic.
        /// </summary>
        internal static bool AnyOtherPlayerInMyScene()
        {
            return OtherPlayerInMyScene();
        }

        /// <summary>
        /// B — the "the other is paused" notification predicate: scene + alive, exposed for
        /// <c>PauseNotice</c>. One-line forward to <c>OtherPlayerHereAndAlive</c>. The alive half
        /// matters because a dead player cannot be the counterpart of a pause: death FORCES
        /// ResumeGame, so their pause front would last an instant and the notification would arrive
        /// on a machine already respawning.
        /// </summary>
        internal static bool AnyOtherPlayerHereAndAlive()
        {
            return OtherPlayerHereAndAlive();
        }

        /// <summary>
        /// [BossJoin] — second number of <c>peerInScena=N/TOT</c>: how many peers are connected in
        /// all.
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
        // ② THE THIRD VALUE OF THE STATE: BOSSFIGHT|<scene> = OFF | IN_CORSO | FINITA
        // ============================================================================================
        //
        // The mod knew two facts and had no word to combine them: "a live boss is in this room" and
        // "another player is HERE". Nothing anywhere said "the fight is IN_CORSO": zero occurrences
        // of BossFightManager, IsFightStarted or onStartBossFight in the mod's code. That is why a
        // newcomer did not know it arrived mid-fight.
        //
        // LOG AND MEMORY ONLY. This latch changes no behaviour: it turns nothing on or off, and
        // sends no packet. The gate for the feature goes on the machine that ENTERS, not the one
        // that fights.
        //
        // IN_CORSO <= bossVivi > 0 AND (peerInScena OR the boss has already lost life). The second
        // half is the witness the game does not expose for free: without a patch, "the fight
        // started" is only readable from observable fact - the boss lost life. On the machine that
        // ENTERS, that witness is not there yet (its boss copy is at full life), which is why
        // peerInScena also counts. The price is declared: with another player in the room and a
        // SLEEPING boss (pre-trigger), the latch says IN_CORSO without anyone fighting. That is why
        // the line always prints bossToccato and peerInScena too.
        private enum BossFightState
        {
            Off = 0,
            InCorso = 1,
            Finita = 2,
        }

        /// <summary>
        /// State per SCENE, not per machine: the same room can be visited more than once in a
        /// session, and the value must survive the trip (re-entry is exactly when it is needed). Not
        /// cleared by <c>LevelLoaded</c>, same reasoning as <c>_remoteTargets</c>.
        /// </summary>
        private static readonly Dictionary<string, BossFightState> _bossFightByScene =
            new Dictionary<string, BossFightState>();

        /// <summary>
        /// How often the state is re-read. Not per frame: <c>CountLiveBossesInScene</c> walks the
        /// scene roots with GetComponentsInChildren, and this is a marker, not a rule. 2s matches the
        /// PerformanceProbe report cadence, so the two logs line up.
        /// </summary>
        private const float BOSSFIGHT_STATE_INTERVAL = 2f;

        private static float _lastBossFightStateCheck;

        /// <summary>Forces one line per load even when the state does not change (see UpdateBossFightState).</summary>
        private static bool _bossFightStateArmed;

        /// <summary>
        /// The current value for a scene: <c>OFF</c>, <c>IN_CORSO</c>, <c>FINITA</c> (or <c>?</c> if
        /// unreadable). Single name, single meaning: this is the only way to read the state.
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
        /// True when a live, active boss of this scene has already lost life: the witness that the
        /// fight has begun. Read from the mod's own tables (<c>_ids</c>), not from a scene scan, so
        /// it sees the same enemies the arbiter sees.
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
                        continue;

                    if (enemy.Status != null && enemy.Status.Dead)
                        continue;

                    if (!IsBossByNamespace(enemy))
                        continue;

                    float maxLife = enemy.Stats != null && enemy.Stats.Life != null
                        ? enemy.Stats.Life.Final
                        : 0f;

                    // Half a point of tolerance: SetHealth and hit arithmetic leave fractions, and a
                    // boss at 399.9999 / 400 has not been hit.
                    if (maxLife > 0f && enemy.CurrentLife < maxLife - 0.5f)
                        return true;
                }
            }
            catch (Exception)
            {
                // A witness that cannot be read is "I do not know": stay on what is known.
            }

            return false;
        }

        /// <summary>
        /// Re-reads the boss-fight state of THIS scene and latches it, logging only when it changes
        /// (plus one line per load, armed by LevelLoaded). Rule: IN_CORSO when a live boss exists AND
        /// (another player is here OR the boss has lost life); FINITA when no live boss remains and
        /// the previous state was IN_CORSO; otherwise no change. It is a latch, not a measurement:
        /// FINITA never returns to OFF on its own, and IN_CORSO never returns to OFF while the boss
        /// is alive.
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

                ModLog.Info($"[BossFight] BOSSFIGHT|{scene} = {BossFightStateName(now)}" +
                            (before == now ? string.Empty : $" (era {BossFightStateName(before)})") +
                            $" · bossVivi={bossVivi} · bossToccato={(bossToccato ? "true" : "false")} · " +
                            $"peerInScena={(peerHere ? 1 : 0)}/{ConnectedPlayersCount()} · " +
                            $"io={(IsHost ? "host" : "peer")} · log-only-no-behaviour");
            }
            catch (Exception e)
            {
                ModLog.Info($"[BossFight] BOSSFIGHT|{SceneForMarker()} = ? " +
                            $"(lettura fallita: {e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// Cleans up the game flags an earlier build of this mod wrote by mistake. The first attempt
        /// at the join fix recorded deaths as progress flags; EventManager.SetFlag CREATES a flag for
        /// an unknown name, and flags are serialised into the savegame, so each announced death left
        /// a permanent "ENEMYDEAD|..." entry. The game has no API to remove flags, so the manager's
        /// own dictionary is read once and filtered. Nothing reads those names; removing them cannot
        /// break a save.
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
        /// Prefix of the junk flags the first join fix wrote. Kept only so the cleanup recognises
        /// them; deaths travel on the EnemyDeathGlobal channel now.
        /// </summary>
        private const string DeathFlagPrefix = "ENEMYDEAD|";

        /// <summary>True once the junk-flag sweep has run, so it happens once per session.</summary>
        private static bool _junkFlagsCleaned;

        /// <summary>
        /// The game has just brought the dead enemies back (bonfire, or penitence respawn on level
        /// load). Everything this mod remembers as "announced dead" is forgotten, mirroring what the
        /// game does with its own consumed list. This is the ONLY place those memories may be
        /// dropped: <c>_pendingDeaths</c> carries a death announced from another room until the enemy
        /// appears, and clearing it on an ordinary level load would undo that fix.
        /// </summary>
        public static void OnEnemiesRespawned()
        {
            int pending = _pendingDeaths.Count;
            int announced = _announcedDeaths.Count;

            _pendingDeaths.Clear();
            _killLogged.Clear();
            _announcedDeaths.Clear();
            _announcedBirths.Clear();

            // Always logged, not only when something was forgotten: the guard that used to be here
            // made the method silent when the lists were already empty, which is why the bonfire
            // branch appeared to never run. A measurement that only speaks when it has something to
            // say cannot distinguish "nothing happened" from "it happened and there was nothing to
            // report".
            string scene = Core.LevelManager.currentLevel == null
                ? "?"
                : Core.LevelManager.currentLevel.LevelName;

            ModLog.Info($"[Falo''] memoria nemici: dimenticate {pending} morti ricevute e {announced} " +
                        $"annunciate (scena={scene})");
        }

        /// <summary>
        /// The game has dropped its own consumed list without bringing anything back through the
        /// bonfire: a player death, a save load, or a menu reset (the three callers of
        /// <c>EnemySpawner.Reset</c>, none of which is an ordinary level load). Forgetting the
        /// announced deaths only at the bonfire left a stale memory: the next joiner was told about
        /// kills that no longer existed.
        /// </summary>
        public static void OnDeathStateReset()
        {
            int announced = _announcedDeaths.Count;
            int pending = _pendingDeaths.Count;

            string scene = Core.LevelManager.currentLevel == null
                ? "?"
                : Core.LevelManager.currentLevel.LevelName;

            // A death we are suppressing must not be forgotten here either. The game drops its
            // consumed list and the mod drops its death memory in the same breath, but if the enemy
            // list is kept and this memory is wiped, the two disagree - a machine that wiped it while
            // still holding dead enemies would stop telling the next player about them. Reading the
            // same flag in both places makes the coherence by construction.
            if (PlayerDeathSync.SuppressingWorldReset)
            {
                ModLog.Info($"[Morte] death memory NOT forgotten ({announced} announced, " +
                            $"{pending} received) and the game list was not emptied: " +
                            $"a death must not respawn the world (scena={scene})");
                return;
            }

            _announcedDeaths.Clear();
            _announcedBirths.Clear();
            _pendingDeaths.Clear();
            _bossKillsApplied.Clear();
            _bossDeathsApplied.Clear();
            _hiddenBossCorpses.Clear();
            _killLogged.Clear();

            // Always logged, same reason as OnEnemiesRespawned.
            ModLog.Info($"[Falo''] consumedSpawns azzerati dal gioco: dimenticate {announced} morti " +
                        $"annunciate e {pending} ricevute (scena={scene})");
        }

        /// <summary>
        /// Replicates a bonfire used by another player, for the WORLD only.
        ///
        /// A bonfire is local: it respawns dead enemies and brings back broken scenery only on the
        /// machine that sat down. Only the three world effects are replicated; the sitter's own
        /// benefits (health, flasks, save) are deliberately left out.
        ///
        /// One call handles both cases because the game's consumed list is global: standing in the
        /// bonfire room recreates the enemies; standing elsewhere clears the list and the enemies
        /// come back by themselves on re-entry. The flag prevents our own bonfire hook from
        /// mistaking this replication for a real bonfire.
        /// </summary>
        private static void ReplicateBonfire(string scene)
        {
            string here = Core.LevelManager.currentLevel == null
                ? "?"
                : Core.LevelManager.currentLevel.LevelName;

            if (string.IsNullOrEmpty(scene))
            {
                ModLog.Error("[Falo''] announcement received with no scene: ignored");
                return;
            }

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            ModLog.Info($"[Falo''] announcement RECEIVED: bonfire scene={scene} · I am in {here} · " +
                        $"{(scene == here ? "SAME room, full respawn" : "other room, clearing the memory")}");

            bool wasReplicating = _replicatingBonfire;
            _replicatingBonfire = true;
            try
            {
                if (Core.Logic == null || Core.Logic.EnemySpawner == null)
                    return;

                Core.Logic.EnemySpawner.RespawnDeadEnemies();

                // Scenery comes back through the game's own event, and the memory of breaks clears
                // as a side effect: UsePrieDieu raises OnUsePrieDieu, BreakableObject restores
                // itself, and the Restore postfix forgets the guid. Tying the cleanup to LOCAL
                // execution of Restore keeps the two machines aligned.
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
        /// Registers everything it can see without ever renaming or shifting an id that is already
        /// in use. Called after the game spawns enemies and then periodically, because some enemies
        /// are created later by triggers and standby activations.
        /// </summary>
        private static void RegisterEnemies(string scene)
        {
            HashSet<string> seenThisScan = new HashSet<string>();

            // Enemies with a spawn point: the spawn point is the authored, stable anchor. The link
            // is SpawnedEnemy, not the hierarchy: CreateEnemy parents the enemy to the level config,
            // so GetComponentInParent<EnemySpawnPoint> never matches.
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
                        // Two spawn points in the same scan producing one id: an authored duplicate.
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

            // Everything else: triggered encounters, arena waves, and every boss. No authored anchor,
            // so the id is name + position at first registration, cached against the instance. The
            // position is captured once and never recomputed: an enemy that walks away would
            // otherwise look brand new on every scan.
            //
            // They must be found while still INACTIVE: a boss sits disabled until its cutscene
            // enables it, and FindObjectsOfType<Enemy> skips disabled objects.
            Enemy[] enemies = FindAllEnemiesIncludingInactive();
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    Enemy enemy = enemies[i];
                    if (enemy == null || enemy.gameObject == null)
                        continue;

                    if (IsRegistered(enemy))
                        continue; // already covered through its spawn point, or an earlier scan

                    // The id is built from the position the enemy has RIGHT NOW, at its first
                    // registration, and cached against the instance. Rebuilding later produces a
                    // different string once the enemy moves, which is how two machines diverged.
                    if (_enemyIds.ContainsKey(enemy))
                        continue;

                    string id = BuildFreeEnemyId(scene, enemy);
                    if (!seenThisScan.Add(id))
                        continue;

                    RegisterEnemy(id, enemy);
                }
            }
        }

        /// <summary>
        /// Assigns an id exactly once. Already-registered enemies are left untouched, which keeps ids
        /// stable while an enemy moves and stops repeated scans from inventing duplicates.
        /// </summary>
        private static void RegisterEnemy(string id, Enemy enemy)
        {
            // C) A boss corpse we switched off must not register. HideDefeatedBosses runs before
            // this, and an inactive object does not stop the registration on its own: IsInertScenery
            // only rejects "inactive AND without EnemyBehaviour", while a boss has its behaviour
            // even when off. Without this line, the corpse would enter the table, receive
            // ResumeEnemy, and become an invisible fighting boss - exactly what the fix must avoid.
            if (_hiddenBossCorpses.Contains(enemy))
                return;

            // Already has an id from an earlier scan: leave it alone. Keeps an enemy's id stable
            // while it moves, and stops one enemy from being tracked twice under two names.
            string existing;
            if (_enemyIds.TryGetValue(enemy, out existing))
            {
                if (existing != id)
                    _duplicateIds++; // same enemy offered a second id: the duplicate seen in game

                return;
            }

            if (_enemies.ContainsKey(id))
            {
                // The id is taken by a DIFFERENT instance. Keeping the first is deliberate:
                // reassigning would move an identity a peer is already following.
                _duplicateIds++;
                return;
            }

            // Scenery is not synchronised. An Enemy that has never been active has run no code at
            // all, so it neither moves nor animates - and that immobility was mistaken for a
            // synchronisation failure for several rounds.
            //
            // [EnemyInert]: the measurement is the ID, added ONCE. This method is called for every
            // rescan and a rejected object does not enter _enemyIds, so without the duplicate check
            // the same entity would be counted twice a second.
            if (IsInertScenery(enemy))
            {
                if (!_inertIds.Contains(id))
                    _inertIds.Add(id);

                return;
            }

            _enemyIds[enemy] = id;
            _enemies.Add(id, enemy);
            _ids.Add(id);
            _registered.Add(enemy);
            _registrations++;

            // The list just changed, so the published fingerprint describes the new one.
            _idsDirty = true;

            // A death announced while this machine was elsewhere, or before this enemy existed. The
            // spawn point was already marked consumed by the announcement; if the game created the
            // enemy anyway, mark it again and take this instance down.
            if (_pendingDeaths.Contains(id))
            {
                // A BOSS is exempt from this branch: on a boss MarkSpawnPointConsumed is a no-op
                // (the id is "en|...", not "sp|...") and DestroyDeadEnemy would KillEnemyLocally +
                // SetActive(false) on a boss the scene just recreated, on ONE machine. The game's
                // own boss-death path (flag <scene>_BOSSDEAD) is what handles that. The id is also
                // removed from _pendingDeaths, otherwise a boss id would stay "pending" forever and
                // the decision would be re-taken at every registration.
                //
                // For a normal enemy the branch runs as always: id stays in _pendingDeaths, spawn
                // point is re-marked consumed, and the instance is destroyed.
                if (IsBoss(enemy, id))
                {
                    _pendingDeaths.Remove(id);

                    ModLog.Info($"[BossPending] {FullId(id)} exempt from the pending-deaths branch: " +
                                "a boss is neither taken down nor switched off at registration " +
                                "(id removed from memory, the boss death stays with the scene)");
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
        /// Every Enemy in the loaded scene, active or not. FindObjectsOfType skips inactive objects,
        /// which excludes anything the scene has not enabled yet - most importantly bosses, which
        /// stay disabled until their fight starts.
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

                // GetComponentsInChildren(true) descends into disabled children too, so a boss
                // parked inactive under a level object is still found.
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
        /// <see cref="FindAllEnemiesIncludingInactive"/> for callers outside this class: the
        /// name → prefab resolution of the effect relay (<c>EffectRelay</c>) needs to scan ordinary
        /// enemies, even inactive, because <c>projectilePrefab</c> lives on an <c>EnemyAttack</c>
        /// attached to the enemy. Same function, plus a catch because the caller is inside a
        /// PoolManager Postfix and must not break Update. No new behaviour, no new Harmony targets.
        /// </summary>
        internal static Enemy[] AllEnemiesIncludingInactive()
        {
            try
            {
                return FindAllEnemiesIncludingInactive();
            }
            catch (Exception)
            {
                return new Enemy[0];
            }
        }

        /// <summary>
        /// Identity of a spawn-point enemy. The spawn point is authored scene data, so name and
        /// position are the same on both machines.
        /// </summary>
        private static string BuildSpawnPointId(string scene, EnemySpawnPoint point)
        {
            Vector3 p = point.transform.position;
            return $"sp|{scene}|{StripInstanceSuffix(point.gameObject.name)}|{p.x:F2}|{p.y:F2}";
        }

        /// <summary>
        /// Identity of an enemy with no spawn point. EnemySpawnPoint.SetEntityName appends an
        /// instance hash to the name (EntityName + GetHashCode, when EntityName is longer than 3
        /// characters), and instance hashes differ between processes, so the trailing digits are
        /// stripped. Enemies still sharing a name differ by their initial position.
        /// </summary>
        private static string BuildFreeEnemyId(string scene, Enemy enemy)
        {
            string name = StripInstanceSuffix(enemy.gameObject.name);
            Vector3 p = enemy.transform.position;
            return $"en|{scene}|{name}|{p.x:F0}|{p.y:F0}";
        }

        /// <summary>
        /// Removes the per-instance suffix a name picks up, so the same object is called the same
        /// thing on both machines.
        ///
        /// TWO DIFFERENT SUFFIXES: the game appends a hash (SetEntityName does "EntityName +
        /// GetHashCode"), and UNITY appends an instance counter in parentheses ("SPAWN (11)" vs
        /// "SPAWN (15)"). The spawn-point builder only handled the first, so the two machines
        /// produced different ids for the same spawn point, and those enemies lost both their token
        /// and their position. The parentheses form is removed first, because "SPAWN (11)" also ends
        /// in a digit once the closing bracket is off.
        /// </summary>
        private static string StripInstanceSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // Unity's instance counter: 'NAME (12)'. Stripped only when the parentheses hold digits
            // and close the name, so a genuine 'Fool (Clone)' keeps its suffix.
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

            // The game's own hash-append: trailing digits, but never a name that was all digits to
            // begin with.
            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1]))
                end--;

            if (end == name.Length || end == 0)
                return name;

            return name.Substring(0, end);
        }

        /// <summary>
        /// True when THIS machine's player is the closest to the enemy, so it should simulate it.
        /// Used only by the host as the arbitrating rule; the peer's ownership comes from the host's
        /// EnemyOwnership token, because two machines measuring distance locally cannot be relied on
        /// to agree.
        ///
        /// Proximity replaces aggro, because aggro is not available: each machine simulates its own
        /// copy of the enemy, so EnemyBehaviour.GetTarget falls back to the LOCAL player on both, and
        /// no field can express "chasing the other player" in the game's own model.
        /// </summary>
        private static bool ShouldOwnByProximity(Enemy enemy)
        {
            Penitent local = Core.Logic.Penitent;
            if (local == null)
                return true; // no local player yet: keep it, so an enemy is never left unowned

            // A CORPSE DOES NOT KEEP THE ENEMY. EnemyBehaviour.GetTarget does not check whether its
            // target is alive and falls back to the LOCAL Penitent; a dead player stops sending
            // positions, so their last one ages out of the freshness window. Keeping the enemy
            // there leaves it attacking a corpse forever. Declining here is safe both ways: if this
            // machine cannot use the enemy, somebody else should have it, and if EVERYBODY is down
            // nobody is simulating anything the players can see.
            //
            // Uses the game's own death state, not our announcement flag: the flag is raised when
            // the death is TOLD; this has to be true from the instant the player falls.
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

            // No remote position known (still connecting, or the other player is in another scene),
            // or the one we have is too old to trust: nobody can be shown to be closer, so keep the
            // enemy here rather than hand it to a player we cannot locate.
            if (nearestRemote == float.MaxValue)
                return true;

            // A remote player has to be MEANINGFULLY closer, not a hair closer. Positions arrive
            // ~20/s, so a bare comparison would flip the owner back and forth.
            return localDistance + OWNER_DISTANCE_MARGIN < nearestRemote;
        }

        /// <summary>
        /// True when the OTHER player is meaningfully closer to the enemy. Exact mirror of
        /// <c>ShouldOwnByProximity</c>, with the same margin so that while the two distances are
        /// comparable NEITHER is true - which is what stops the owner from flapping.
        ///
        /// Exists because the handover used to depend on <c>RemoteEverClaimed</c>, but a peer only
        /// transmits the enemies the host granted it, so it could never claim the very enemy it was
        /// trying to take. With distances (which travel in every update), the host can compare the
        /// two numbers itself.
        /// </summary>
        private static bool PeerIsCloser(Enemy enemy)
        {
            Penitent local = Core.Logic.Penitent;
            if (local == null)
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

            if (nearestRemote == float.MaxValue)
                return false;

            return nearestRemote + OWNER_DISTANCE_MARGIN < localDistance;
        }

        /// <summary>
        /// True while the remote position is recent enough to arbitrate on. A missing timestamp
        /// counts as stale: if we do not know when it arrived we cannot claim it is current.
        /// </summary>
        private static bool IsRemotePositionFresh(string playerName)
        {
            float at;
            if (!_remotePositionsAt.TryGetValue(playerName, out at))
                return false;

            return Time.time - at <= REMOTE_POSITION_MAX_AGE;
        }

        /// <summary>
        /// True when another player is HERE, in the same scene as this machine. Position freshness is
        /// not a substitute: positions travel on their own channel and keep arriving whatever scene
        /// the other player is in. The SCENE is what says whether waiting is meaningful.
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
        /// True when ANOTHER PLAYER IS IN THIS SCENE **AND IS NOT DOWN** — the one question the boss
        /// rule may ask before leaving a boss to somebody else.
        ///
        /// Two halves answer two defects: (a) NOT HERE means nobody else can be simulating the boss,
        /// and leaving it to a machine not in the room produces a boss owned by nobody; (b) HERE BUT
        /// DOWN means the survivor's copy of the boss discards every hit (the game's own damage
        /// entry is guarded by the LOCAL player's life), which is worse than any handover.
        /// <c>IsDead</c> is the remote player's own announcement; unknown players read as alive,
        /// which is the conservative answer.
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
                // Conservative: "I do not know" = the other is not a reliable simulator, and the host
                // reclaims the boss instead of leaving it still.
                return false;
            }
        }

        /// <summary>
        /// The remote player HERE and not down — the recipient of the enemy→player channel, or
        /// <c>false</c> when there is none. Variant of <c>OtherPlayerHereAndAlive</c> because the
        /// forwarded hit must name a recipient (the Attack channel is a scene broadcast). Same rule
        /// line by line: same scene, not dead (<c>IsDead</c> is the pdead| announcement, the only
        /// proof of life for an avatar that is not a Penitent). Unknown player reads as alive, the
        /// conservative direction: better a hit forwarded to someone than a hit lost.
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
        /// True when this machine should hold off taking an enemy because its position has not
        /// arrived yet. Three-sided, each side was a real failure: position already here → take now;
        /// NOBODY IS IN THIS ROOM → nobody to receive from, waiting would strand the enemy; wait past
        /// <c>POSITION_WAIT_GRACE</c> → take it anyway (a stationary enemy is skipped by the
        /// change-gated send).
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
        /// The stand-in for the other player that the GAME's own aggro can see.
        ///
        /// The game has exactly ONE aggro target (Enemy.Target), always set to the LOCAL Penitent.
        /// The other player is a bare GameObject with no collider and no Entity, so the game's
        /// checks cannot see them at all. This proxy closes that gap and, since the enemy→player
        /// channel, is also the interception point for a blow aimed at the other player: it carries
        /// <see cref="CoopRemoteTargetDamageable"/>, an IDamageable that never touches the LOCAL
        /// Penitent and instead forwards the blow to the peer. Without that component it is still
        /// exactly what it used to be: a collider nothing can be hurt through.
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

            // The proxy becomes DAMAGEABLE, and not for the game: for us. Before this line the
            // proxy was the only mod collider on "Penitent" layer WITHOUT an IDamageable, which
            // meant the game filtered for it and either discarded the blow or threw
            // (BossJumpAttack.OnPenitentEntersArea had no null check), and a boss blow aimed at the
            // peer had no landing place. With this component, the blow lands on an IDamageable that
            // forwards to the peer instead of the local Penitent.
            _remoteTargetProxy.AddComponent<CoopRemoteTargetDamageable>();

            return _remoteTargetProxy;
        }

        /// <summary>
        /// True when this GameObject is the mod's own aggro proxy, the stand-in placed where the
        /// other player is so the game's vision can see them.
        ///
        /// The game's CONTACT damage must ignore it: ContactDamage.OnTriggerEnter2D does
        /// GetComponentInParent&lt;IDamageable&gt; and then calls Damage with NO null check, and
        /// IsTargetOverlapped is raised before the null is reached - so the attack that reads it hits
        /// Core.Logic.Penitent without measuring distance. The proxy being damageable now does not
        /// change that: contact damage is still skipped on purpose (see AggroProxyPatches).
        /// </summary>
        public static bool IsRemoteTargetProxy(GameObject go)
        {
            if (go == null || _remoteTargetProxy == null)
                return false;

            return go == _remoteTargetProxy || go.transform.IsChildOf(_remoteTargetProxy.transform);
        }

        /// <summary>
        /// Where the proxy stands, on one log line, for the enemy→player markers. Provides the
        /// geometry the fix rests on: the blow landed there, and that is where the machine believes
        /// the other player stands. <c>?</c> if the proxy does not exist yet (a marker must not stop
        /// a blow).
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
        /// Announces the instant an enemy becomes TRULY IN PLAY, so the other machine can reveal its
        /// own copy.
        ///
        /// A family of enemies is spawned on BOTH machines and then hidden; the game's reveal is a
        /// LOCAL event (a player stepping into the mud, an arena trigger), so on the other machine
        /// the enemy stays invisible. Not a desync - an enemy only one side can see.
        ///
        /// The damage collider is the signal, not activeInHierarchy: this family is never
        /// deactivated; the reveal is SpriteRenderer.enabled going true, Animator speed going to 1,
        /// and the damage collider switching on. Gated on ownership: only the machine whose GAME
        /// performed the reveal announces it (otherwise the announcement echoes).
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

                // The collider alone is not the truth: WaxCrawlerMeltingBehaviour.OnStateEnter turns
                // the collider on for ANY entry into "Appear", including the one the MOD forces when
                // it replays the owner's animation. Reading only the collider made our own replay
                // look like a reveal, and the peer ran Rise() on an enemy its game had never
                // revealed. The sprite distinguishes them: the real reveal turns both on.
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
                    // Back to not-in-play (a respawn puts it back to sleep). Clearing the latch lets
                    // the NEXT reveal be announced again.
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
        /// Wakes this machine's copy of an enemy whose game the other machine just revealed. Uses
        /// the game's OWN entry point: the WaxCrawler family is never inactive; its reveal is
        /// <c>WaxCrawlerBehaviour.Rise()</c>, which turns the sprite on, sets Animator speed to 1,
        /// and plays "Appear". <c>SetActive(true)</c> would be a no-op on an always-active object.
        /// The generic branch is the fallback for families that really do use SetActive. The id is
        /// dropped from <c>_pendingDeaths</c>: a spawn point already marked consumed must not keep
        /// suppressing an enemy just put back in play.
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
        /// Points an OWNED enemy at whichever player is closer, so its own AI can turn and attack
        /// towards the other player. Runs only on the simulating machine; the position and animation
        /// are already synced.
        ///
        /// NOTE: it is false that "the other player takes damage through the game's local pipeline".
        /// Each machine damages its OWN Penitent; the only path across the two machines is the
        /// forwarding in <c>EnemyHitPeer</c>. This proxy alone is not enough to hurt the peer.
        ///
        /// The proxy is positioned here, in the same pass, so the vision linecast tests a collider
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

                // A DEAD PLAYER IS NOT A TARGET. A dead player stops sending positions, so their
                // last one ages out of the freshness window - and the branch below then HOLDS the
                // proxy where they fell. EnemyBehaviour.GetTarget returns whatever is in Enemy.Target
                // without checking whether it is alive, and this mod is the only writer of Target.
                // The predicate already existed and had no call site.
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
                // Their sender is change-gated, so a player standing still sends nothing, and the
                // freshness window is only 0.3 s. Falling back to the local player there is what
                // lost the remote the instant they stopped moving.
                //
                // The `Target == proxy` half is essential: this must ONLY preserve a decision that
                // was already made. Forcing the proxy on every owned enemy flipped enemies onto the
                // remote while they were fighting the LOCAL player.
                wanted = _remoteTargetProxy;
            }

            // Only write when it really changes: Target is a property, assigning it every pass would
            // be a write per enemy per pass for nothing.
            if (enemy.Target != wanted)
                enemy.Target = wanted;
        }

        /// <summary>
        /// True while at least one remote player is still worth facing - alive, that is. Gates the
        /// "hold the proxy while the position is stale" branch: a dead player's position is stale BY
        /// CONSTRUCTION (they stopped sending the moment they died), and without this the branch
        /// held the proxy on their corpse forever.
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
        /// Animation is the one thing that genuinely has to be compared, since it is what the game
        /// uses to show state. The scan runs every <c>RESCAN_INTERVAL</c> (it must, to discover
        /// post-load spawn points); the writing here is slower (<c>DIAGNOSTIC_LOG_INTERVAL</c>) so
        /// the log stays readable.
        /// </summary>
        private static void LogDiagnosticSnapshot()
        {
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

                // Reference type: TryGetValue leaves this null when the enemy has no record yet.
                EnemyState state;
                bool hasState = _lastSent.TryGetValue(id, out state);

                int animState;
                float animTime;
                ReadAnimation(enemy, out animState, out animTime);

                // Death record, independent of the connection. Reports which enemy died, whether
                // the game set Dead or the life simply reached zero, and whether it was still being
                // drawn - the three things needed to tell a normal kill from an enemy that is
                // merely deactivated. Lives here rather than in the send loop because that loop
                // returns early when there is no connection, so a single-player test produced no
                // death records at all.
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

                // Both raw positions, because the ownership rule lives or dies on them.
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

                // Reveals an internal desync: the cached instance against the one the spawn point
                // currently owns. When they differ, everything read through the cache describes an
                // enemy that no longer exists.
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
        /// Records where another player is, called for every position packet that arrives. The
        /// ownership rule needs the other player's position, and this is the one place the value is
        /// known to be correct: it is what the game actually sent.
        /// </summary>
        public static void NoteRemotePlayerPosition(string playerName, Vector2 position)
        {
            if (string.IsNullOrEmpty(playerName))
                return;

            _remotePositions[playerName] = position;

            // When it arrived, so the arbitration can refuse to decide on a stale sample. A remote
            // position that has aged past REMOTE_POSITION_MAX_AGE is not evidence about who is
            // closer right now, and using it anyway hands a contested enemy to the wrong machine.
            _remotePositionsAt[playerName] = Time.time;
        }

        /// <summary>
        /// How old a remote player's position may be before the ownership rule stops trusting it.
        ///
        /// The rule compares two distances, and only one of them is measured locally. The remote one
        /// is stored EXACTLY as it came (the lerp smooths enemies, never the other player). Drifts of
        /// 5 to 8 units were measured against OWNER_DISTANCE_MARGIN = 2.5, so the margin cannot
        /// absorb the error. Past this age the sample is discarded rather than compared: the rule
        /// falls back to "keep it here", same conservative behaviour as for a player it cannot
        /// locate.
        /// </summary>
        private const float REMOTE_POSITION_MAX_AGE = 0.3f;

        /// <summary>
        /// Eases every enemy this machine does NOT own towards its last received snapshot. Owned
        /// enemies are left alone: their AI is running, and writing to the transform would fight it.
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

                // PUNTO 0 — a boss is not followed when the position is not from now.
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
        /// PUNTO 0 — the transplant guard: a boss is not moved by the follow.
        ///
        /// TRUE when this enemy is a BOSS and the <c>_remoteTargets</c> entry we are about to apply
        /// was NOT refreshed in THIS visit: the position comes from a previous visit of the room,
        /// from a simulation that does not exist any more, and must not be applied.
        ///
        /// A boss IN FIGHT continues to be followed, which is why the guard is on FRESHNESS not on
        /// type: when the other machine simulates it, the position arrives every tick, the entry is
        /// from this visit, and it is followed as before. Without this distinction ("never follow a
        /// boss") the boss would stay at its spawn on the other machine while the owner fights it
        /// elsewhere - a new defect, not a fix.
        ///
        /// Touches nothing about the boss: no life, no flags, no death, no game API, no FSM. It skips
        /// ONE position write: the boss stays where the scene put it. For normal enemies the method
        /// returns false immediately and the follow is unchanged.
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
                    return false;   // the owner is transmitting it NOW: it is the truth, follow it

                // Marker: one line per boss per visit, so the test shows it.
                if (_bossFollowSkipLogged.Add(id))
                {
                    ModLog.Info($"[Insegue] {FullId(id)} boss: inseguimento SALTATO - la posizione remota e' " +
                                $"of visit {writtenInVisit} and we are at {_sceneVisit} (it is not a " +
                                $"simulazione in corso: la posizione del boss la decide la scena) · " +
                                $"boss=({flat.x:F1},{flat.y:F1}) target=({target.x:F1},{target.y:F1}) " +
                                $"dist={distance:F1} · the boss stays where the scene put it");
                }

                return true;
            }
            catch (Exception)
            {
                // If the "is a boss" test fails, do not stop the follow of a normal enemy: proceed
                // and follow, which is the previous behaviour.
                return false;
            }
        }

        /// <summary>
        /// Moves an enemy the only way that actually holds: through its physics state, not its
        /// transform.
        ///
        /// A Blasphemous enemy is driven by CreativeSpore's PlatformCharacterController, whose
        /// Update ends by writing the transform FROM its own physics state. Writing
        /// <c>transform.position</c> survives only until the next Update, and with velocity near zero
        /// the physics puts the enemy straight back - the "enemy stays frozen" seen in game.
        ///
        /// <c>ResetPreviousPosition()</c> sets both m_pos and m_prevPos, realigning the physics to
        /// the new position and stopping the spurious DeltaDisp a bare transform write would
        /// produce.
        /// </summary>
        private static void PlaceEnemy(Enemy enemy, Vector3 position)
        {
            // The transform moves FIRST: ResetPreviousPosition reads the transform and copies it
            // into the physics, so calling it after setting the physics would silently undo the
            // move. Order: move the transform, then ResetPreviousPosition - which now reads the
            // transform we just moved and copies that same position into both the physics and
            // m_prevPos. Aligning m_prevPos is what keeps InstantVelocity clean.
            enemy.transform.position = position;

            PlatformCharacterController controller = enemy.Controller;
            if (controller == null)
                return;

            // No fallback when physics is missing: ResetPreviousPosition dereferences
            // m_platformPhysics with no null check, so calling it would throw out of Update().
            if (controller.PlatformCharacterPhysics == null)
                return;

            controller.ResetPreviousPosition();

            // The collider is a THIRD copy of the position and must be moved too. SmartPlatformCollider
            // is not switched off by controller.enabled = false, and it writes transform.position in
            // several places including a rewind to its own m_prevPos. TeleportTo sets transform.position,
            // its own m_prevPos, and clears m_movingPlatforms - the state a teleported collider
            // should be in.
            SmartPlatformCollider collider = controller.SmartPlatformCollider;
            if (collider != null)
                collider.TeleportTo(position);
        }

        /// <summary>
        /// Snaps an enemy to the last position the network reported, right when this machine takes
        /// ownership. A machine that stops owning stops transmitting, and the machine that takes
        /// over starts from its own local copy (often the spawn point), then broadcasts the stale
        /// position. <c>_remoteTargets[id]</c> holds the last position the previous owner
        /// transmitted, and it keeps being written regardless of owner, so it is the best truth
        /// available at handover.
        /// </summary>
        private static void AdoptLastKnownPosition(Enemy enemy, string id)
        {
            if (enemy == null || enemy.gameObject == null)
                return;

            // ④ A boss is NEVER re-anchored to a remote position.
            //
            // On handover the local copy is already tracked every frame by InterpolateRemoteEnemies,
            // so adoption only adds a visible teleport: the remote position often comes from a
            // previous visit or a fight the old owner had long stopped simulating - the boss's spawn.
            // Re-anchoring a boss there was a measurable jump back to spawn mid-fight.
            try
            {
                if (!string.IsNullOrEmpty(id) && IsBoss(enemy, id))
                {
                    ModLog.Info($"[Proprieta'] {FullId(id)} boss: remote position NOT adopted " +
                                $"(resta a ({enemy.transform.position.x:F1},{enemy.transform.position.y:F1}), " +
                                "the local copy is already interpolated every frame by InterpolateRemoteEnemies)");
                    return;
                }
            }
            catch (Exception)
            {
                // If the boss test fails, do not skip adoption for a normal enemy.
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
        /// Reads the spawn points the GAME considers defeated, as positions. The game keeps them in a
        /// private <c>List&lt;Vector3&gt;</c> and compares by exact Vector3 equality, so the useful
        /// payload is the POSITION: not the enemy id, and not the position the enemy died at (the
        /// two machines kill the same enemy at slightly different spots). Read reflectively; called
        /// only when a player joins.
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
                    ModLog.Error("[EnemyJoin] field 'consumedSpawns' not found: cannot announce the deaths");
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
        /// Re-announces this machine's defeated spawn points, so a player who joins LATER learns
        /// about deaths that happened before it was connected. A death is normally a live event
        /// (SendEnemyKill/SendEnemyDeathGlobal sit inside UpdateCore, which returns while
        /// disconnected), so it is never retried; the joiner keeps a living copy. Every machine
        /// announces its OWN list; the union is the correct result, because letting one impose its
        /// list would erase the other's kills.
        /// </summary>
        public static void AnnounceDeathStateToPeers()
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            // Announced as spawn-point ids on the EnemyDeathGlobal channel, NOT as progress entries.
            // The first attempt used SetFlag, which CREATES a flag for an unknown name and is
            // serialised into the savegame - the record was permanent and echoed forever. The death
            // channel already does exactly this job and reaches everyone except the sender.
            //
            // The ids from the current scene are the precise ones; the remembered ones cover rooms
            // this machine has since walked out of. A spawn-point id is authored data, so the same
            // spawn point yields the same string on both machines and the union deduplicates.
            List<string> sceneIds = ConsumedSpawnPointIds();

            // Reported even when empty, because an empty announcement used to be silent, and that
            // silence is the only reason this defect was found by accident.
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
                ModLog.Info("[EnemyJoin] no death to announce (empty memory, scene with no consumed spawns)");
                return;
            }

            for (int i = 0; i < ids.Count; i++)
                Main.Multiplayer.SendEnemyDeathGlobal(ids[i]);

            // And the REVEALED enemies, for the same reason deaths are replayed: the birth
            // announcement has no memory on the server, so a joiner who was not there would keep
            // seeing the hidden copy of an enemy that is in play for everybody else.
            foreach (string born in _announcedBirths)
                Main.Multiplayer.SendEnemyDeathGlobal(BirthPrefix + born);

            ModLog.Info($"[EnemyJoin] annunciate {ids.Count} morti e {_announcedBirths.Count} nascite " +
                        $"previous to the new player " +
                        $"({sceneIds.Count} dalla scena, {ids.Count - sceneIds.Count} dalla memoria)");
        }

        /// <summary>
        /// The spawn-point ids whose positions the game has marked consumed. The game stores
        /// positions, so the position is looked up against the spawn points present in the scene and
        /// the matching one's OWN id is recovered.
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
                    // The game's own comparison is exact; this only has to decide WHICH point a
                    // consumed position refers to.
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
        /// True when an enemy is scenery rather than something to synchronise: it has never run any
        /// code. The game creates an Enemy for EVERY spawn point, and a point with
        /// SpawnEnabledEnemy false is switched off immediately by
        /// <c>EnemySpawnPoint.CreateEnemy</c> and only switched back on from arena triggers. Such
        /// an object is never active, has no EnemyBehaviour and no Animator state; a single position
        /// is guaranteed by construction and measures nothing.
        /// </summary>
        private static bool IsInertScenery(Enemy enemy)
        {
            if (enemy == null || enemy.gameObject == null)
                return true;

            // Active means the game is running it; that is enough to treat it as real.
            if (enemy.gameObject.activeInHierarchy)
                return false;

            // Inactive WITH a behaviour is a stand-by enemy waiting for its arena trigger: it has
            // run Awake, it has a behaviour, and the game will switch it on. Keep it.
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
        /// The id in full. ShortId collapses every enemy in a room to "scene@name"; the position is
        /// what makes each unique. <c>ShortId</c> is not changed because nine existing lines use it
        /// and are compared against sessions that were logged with it.
        /// </summary>
        private static string FullId(string id)
        {
            return id;
        }

        /// <summary>
        /// The HOST's arbitration: decides which machine simulates an enemy, returns whether the
        /// role changed. The peer receives the decision as a token instead.
        ///
        /// Rule, deliberately sticky: host takes when its own player is the closest; hands over when
        /// the peer is meaningfully closer; the role only moves after <c>OWNER_HOLD_SECONDS</c>,
        /// which stops the two machines trading the same enemy many times per second.
        /// </summary>
        private static bool DecideOwnership(Enemy enemy, string id, EnemyState state, bool localIsHost)
        {
            bool wants = ShouldOwnByProximity(enemy);
            bool peerCloser = PeerIsCloser(enemy);

            // ① A BOSS is not a generic enemy - its ownership is stable.
            //
            // Measured: treated as a generic enemy, the boss changed hands 4 times in ~60s and sat
            // STILL for 30.5s out of 58 on G1. The two machines measure the distance slightly
            // differently, so with the 2.5 margin neither has a stable answer and the decision is
            // re-asked on every tick.
            //
            // The FIRST branch was the damaging one, not a handover: the fallback
            // `!peerCloser && localIsHost` bypasses ShouldWaitForPosition and adopts the spawn. The
            // fix therefore has three properties: (a) the boss does not enter the first-branch
            // fallback; (b) it does not enter the two handover branches (with ONE exception: the
            // recovery); (c) it is NEVER re-anchored to a remote position (see AdoptLastKnownPosition).
            //
            // The form mirrors three existing exemptions (SetHealth skip, _pendingDeaths branch
            // exemption, HideDefeatedBosses). The id comes from the caller rather than being
            // recomputed via IdOf: IsBoss needs it, and IdOf is a linear scan that would run for
            // EVERY enemy EVERY tick.
            bool boss = IsBoss(enemy, id);

            if (state.OwnerSince <= 0f)
            {
                // First time this enemy is considered. Whoever's player is closer takes it, and the
                // arbiter falls back to keeping it only when NEITHER player is meaningfully closer.
                //
                // It used to be `localIsHost || wants`, which handed the enemy to the arbiter
                // whatever the distances were - and made the first player to connect feel like a
                // privilege. The distances are known on both machines, so the arbiter has no need
                // to favour itself.
                state.OwnerSince = Time.time;

                // If this machine is the closer player but the position has not arrived yet, hold
                // off taking - otherwise the enemy resumes at its spawn copy and broadcasts that
                // stale position. The current owner keeps transmitting until the handover token, so
                // by the next scan the position is here and the take lands on the correct spot.
                //
                // ShouldWaitForPosition carries two guards that keep the wait from stranding the
                // enemy: no reachable peer, or the wait has run too long. The host fallback
                // (!peerCloser && localIsHost) is exempt.
                bool take = wants || (!peerCloser && localIsHost);

                // ② STABLE OWNERSHIP, FIRST BRANCH: the `!peerCloser && localIsHost` fallback does
                // NOT apply to a boss. With dist_io=22.9 and dist_altro=21.5 the margin makes
                // wants=false and peerCloser=false: nobody is meaningfully closer, and the fallback
                // was making the host TAKE the boss - the case that must stay put.
                //
                // No loss: if the peer is not reachable, the remote position is old or missing,
                // ShouldOwnByProximity answers true (nobody can be closer than someone we cannot
                // locate), and the host takes it anyway. The only case the host passes on is when
                // the other is genuinely simulating it.
                //
                // But "leave it to the peer" only makes sense if the peer CAN take it. Positions
                // travel across scenes, so with the peer in ANOTHER room `wants` can be false for a
                // distance that means nothing, take=false, and the host publishes "you own it" - the
                // token is filtered by scene, arrives to nobody, and the boss stays PAUSED on this
                // machine with nobody simulating it. ⇒ if no other player is HERE AND ALIVE, the
                // host always takes it: same predicate as recovery, applied on the first tick.
                bool forced = false;
                if (boss)
                {
                    bool otherCanSimulate = OtherPlayerHereAndAlive();
                    forced = !otherCanSimulate;
                    take = otherCanSimulate ? wants : true;
                }

                // The position wait does NOT apply to a forced first tick: for a boss the remote
                // position is not adopted (SkipBossFollow/AdoptLastKnownPosition), and the local
                // copy is followed every frame. Leaving the wait active would reopen the ghost boss
                // via the service door: with the other dead, OtherPlayerInMyScene is true, the wait
                // would trigger, and the boss would stay on a machine that cannot kill it.
                if (!forced && wants && ShouldWaitForPosition(enemy, state))
                    take = false;

                state.OwnedByLocal = take;

                // Ownership was decided: if this is a boss and the owner is the other, the next
                // receive will realign its life.
                ForgetBossLifeAlignment(state);

                if (state.OwnedByLocal)
                    AdoptLastKnownPosition(enemy, id);

                // Decision marker: ONE line per decision, never per reason, only for bosses.
                if (boss)
                    LogBossOwnershipDecision(enemy, id, state, wants, peerCloser, "primo-tick");

                return false;
            }

            if (!localIsHost)
                return false; // only the arbiter decides; this method is never the peer's path

            // A DEAD OWNER GIVES IT UP, IMMEDIATELY.
            //
            // Refusing to TAKE an enemy while dead is not the same as letting go of one. The only
            // release branch was peerCloser, which demands the peer be MEANINGFULLY closer - so
            // dying right next to the boss slipped through. The host then kept the boss, whose own
            // Damage() discards everything while the LOCAL player's life is zero, and every hit the
            // survivor landed was thrown away. The boss was invulnerable, and the survivor could
            // not tell because the health bar is local.
            //
            // No hold time here: OWNER_HOLD_SECONDS exists to stop two LIVING players trading the
            // same enemy; a corpse has nothing to stabilise.
            //
            // ③ - THE ORPHAN: release ONLY if someone is here to take them.
            //
            // This line used to be `if (PlayerDeathSync.IsLocalPlayerDead() && state.OwnedByLocal)`,
            // and the second condition produced the only non-cosmetic defect of the round. The
            // release is UNILATERAL: this machine drops ownership and publishes
            // SendEnemyOwnership(handle, ownedByReceiver: true) from the send loop. On the peer,
            // ReceiveEnemyOwnership sets state.OwnedByLocal = true and calls ResumeEnemy. If the
            // peer is NOT in this scene, that enemy is not in its _ids: ResolveEnemy returns null,
            // ReceiveEnemyOwnership exits at `if (enemy == null) return;`, and the record stays
            // `OwnedByLocal = true` with `OwnershipKnown = true` which the self-heal cannot bypass.
            // ⇒ The enemy is simulated by nobody: I let it go, the peer does not have it. Not
            // cosmetic - it does not walk, attack, or get pushed (FreezePhysics switches the
            // controller off).
            //
            // The release is not cosmetic either: Entity.Damage() discards everything while the
            // LOCAL player's life is zero, so hits the other player brings to this enemy arrive
            // here and are thrown away. When alive, releasing is the right thing.
            //   ⇒ But when the peer is in ANOTHER SCENE there is no survivor here: no one to pick
            //   up those enemies, no one fighting in this room. Releasing there does not move the
            //   enemy to someone who can use it - it leaves it to nobody.
            //
            // ⭐ If we do NOT release, who owns it? I stay, and it keeps running here.
            // state.OwnedByLocal is not touched, the send gate `if (!previous.OwnedByLocal) continue;`
            // keeps passing, ResumeEnemy/ApplyAggroTarget run as for any owned enemy. An enemy
            // running on my machine is BETTER than an enemy with no owner.
            //   ⭐ And for the machine watching, nothing changes: when this body gets up,
            //   ShouldOwnByProximity can say "I am closer" again and ownership recomposes itself.
            //
            // The gate is OtherPlayerInMyScene() and not something finer like "the peer can really
            // simulate": that is the only question this machine can answer with certainty
            // (PlayerStatus.CurrentScene is maintained by the scene channel). Same question as
            // ShouldWaitForPosition.
            //
            // And the release is NOT gated on peerCloser, deliberately: that is a TRADE between two
            // LIVING players, and PeerIsCloser requires a fresh remote position (0.3s) and a
            // margin. Not the death, not this case, and gating it would change normal behaviour.
            if (PlayerDeathSync.IsLocalPlayerDead() && state.OwnedByLocal)
            {
                if (!OtherPlayerInMyScene())
                {
                    // Mandatory marker: without this line, "the orphan is solved" and "the enemy no
                    // longer runs" are indistinguishable. It names the enemy so the id can be
                    // followed across the session. Deduplicated (this method runs every send tick).
                    LogDeadOwnerDecision(enemy, false);
                    return false;
                }

                state.OwnedByLocal = false;
                state.OwnerSince = Time.time;

                // Ownership changed (I released it): the next receive realigns the boss life.
                ForgetBossLifeAlignment(state);

                // The other branch of the marker: here the release DOES happen, and the line says
                // why it is allowed.
                LogDeadOwnerDecision(enemy, true);

                return true;
            }

            // The marker does not apply (alive, or the enemy is not mine): its state is FORGOTTEN,
            // so the NEXT death of this player reports the decision again instead of staying mute
            // for the whole session.
            _deadReleaseLogged.Remove(IdOf(enemy));

            bool changed = false;

            // ③ STABLE OWNERSHIP, THE TWO HANDOVER BRANCHES.
            //
            // For a boss both branches stay closed: it never gives up (peerCloser) and never
            // resumes while the other is here simulating it (wants). That is what removes the ping-
            // pong: today the boss changes hands 4 times in ~60s and each change is a pause
            // (PauseEnemy on the old owner) plus a position re-anchor.
            //
            // But closing the handover without an exit swaps the ping-pong for a worse defect: the
            // GHOST BOSS. If the boss owner leaves the room, its _lastSent is wiped by LevelLoaded
            // and nothing comes back for that id: the host would keep a record saying "the peer's"
            // and hold the boss in PauseEnemy FOREVER - frozen, no attacks, no collider
            // (FreezePhysics), and no token to wake it.
            // (ReleaseOwnedEnemiesBeforeLeaving runs only on the HOST: the peer that leaves does
            // not give anything back.) ⇒ The exit is RECOVERY, condition: NO OTHER PLAYER IS HERE
            // AND ALIVE ⇒ nobody can be simulating this boss, so the host reclaims.
            //
            // And ALIVE is not a detail. If the other is here but down, their copy of the boss
            // throws every hit away (Entity.Damage exits while the LOCAL player's life is zero,
            // the guard written out in full in the dead-owner branch above): leaving it to them
            // means an invulnerable boss until they respawn. With IsDead, the boss returns to the
            // host, who can kill it.
            //
            // Recovery is IMMEDIATE (no OWNER_HOLD_SECONDS) for the same reason the dead-owner
            // release is: not a trade between two LIVING players, and half a second of waiting
            // would be half a second of frozen boss. Once reclaimed, the handover stays closed.
            bool bossRecovery = boss && !state.OwnedByLocal && !OtherPlayerHereAndAlive();

            bool takeNow = bossRecovery ||
                           (wants && !state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS);

            // Take when this machine's player is meaningfully closer; give it up when the other
            // player is. Both use the margin, so while the distances are comparable neither applies
            // and the owner simply stays.
            if (takeNow)
            {
                // Same wait as the first-time branch, with the same guards: take the enemy as soon
                // as the position is here, and never let the wait outlive POSITION_WAIT_GRACE or a
                // reachable peer.
                if (!ShouldWaitForPosition(enemy, state))
                {
                    state.OwnedByLocal = true;
                    state.OwnerSince = Time.time;

                    // Ownership changed (I became owner): if I lose it again, life realigns to
                    // whoever takes it. The latch is reopened here, not where the life is written.
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
                                         bossRecovery ? "recovery: nobody else can simulate it" : "fixed");

            return changed;
        }

        // ============================================================================================
        // [BossProp] — the boss ownership decision, one line per decision
        // ============================================================================================

        /// <summary>Last printed decision per id: <c>true</c> = owner is me, <c>false</c> = owner is the other.</summary>
        private static readonly Dictionary<string, bool> _bossOwnLogged = new Dictionary<string, bool>();

        /// <summary>
        /// Writes <c>[BossProp]</c> only when the decision on that boss CHANGES.
        ///
        /// The fix removes a defect that shows in the log as ABSENCE (no pauses, no handovers), and
        /// absence on its own does not distinguish "the fix works" from "this boss was never
        /// considered". This line says the missing thing: who is owner, at start and at each change,
        /// with the two booleans that decided.
        ///
        /// The key is the DECISION, not the reason - same trap as LogTreeState and
        /// LogDeadOwnerDecision. DecideOwnership runs for every enemy every send tick; if the reason
        /// were in the key, wants/peerCloser (which oscillate with distances) would produce a new
        /// line every tick and dedup would be dead. The reason is CONTEXT, and it travels with the
        /// decision.
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

                // The two distances, because they are the two numbers the rule decides on, and they
                // do NOT agree between the two machines. [EnemyTick] prints them every 10s; here
                // they arrive at the exact moment of the decision.
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
                // A marker must never stop the arbiter: here we lose a line, not a decision.
            }
        }

        /// <summary>
        /// Writes the [Proprieta'] marker of the dead-owner branch, ONCE PER DECISION PER ENEMY.
        ///
        /// DecideOwnership runs from the send loop for every enemy every tick, so the two lines it
        /// used to hold wrote one per enemy per frame - 1700 lines in a session with the very same
        /// text. The decision never changed; only the evaluation did. The gate
        /// (OtherPlayerInMyScene) and the decision are untouched; only WHEN the line is written
        /// changed.
        ///
        /// The key is the DECISION, not the reason. Same trap as LogTreeState: a reason in the key
        /// makes the value alternate forever, dedup dies, and the log fills up anyway. Only "release"
        /// or "non-release" is a state; the sentence that explains it travels with the change.
        /// </summary>
        private static void LogDeadOwnerDecision(Enemy enemy, bool release)
        {
            string id = IdOf(enemy);
            if (string.IsNullOrEmpty(id))
                return;

            string decision = release ? "release" : "no-release";

            string previous;
            if (_deadReleaseLogged.TryGetValue(id, out previous) && previous == decision)
                return;

            _deadReleaseLogged[id] = decision;

            if (release)
            {
                ModLog.Info($"[Proprieta'] {id}: dead, I RELEASE it - the other player is in " +
                            "this scene and can take it (the token reaches it and it simulates it)");
            }
            else
            {
                ModLog.Info($"[Proprieta'] {id}: I am dead but I do NOT release it - the other " +
                            "player is not in this scene, and releasing it would leave it with no " +
                            "owner (nobody simulates it: it is not in its list). It stays mine and keeps " +
                            "running here; I will put it back in play when it returns.");
            }
        }

        // ============================================================================================
        // [BossLife] — BOSS LIFE TRAVELS: "STATE + DRAWING TOGETHER"
        //
        // The defect: ReceiveEnemyUpdate wrote life with SetHealth for EVERY enemy except the boss.
        // The boss health bar is local (BossHealth.BarTarget = target.Stats.Life.Current / Final), so
        // two copies = two bars, and 110 HP out of 400 were constant discrepancy for a whole fight.
        //
        // Why not per packet: SetHealth calls KillInstanteneously when the value reaches 0, and
        // writing 0 leaves a boss standing with an empty bar and the room stuck. Writing twenty
        // times a second on a boss whose ownership has not been asked for either is not right.
        // ⇒ Write ONCE per situation: scene entry, and every ownership change (owner sends,
        // puppet receives). The rest is handled by the damage relay, which already works.
        //
        // Downward only: the entering machine has a fresh copy at full life, so the measured case is
        // "receiving 290 while reading 400". An upward write would resurrect the boss. If the puppet
        // were LOWER than the owner, nothing is written - not the measured case, and not a write
        // worth making.
        //
        // Phase - "the drawing": SetHealth does not go through Damage(), the only caller of
        // CheckNextPhase(), so the boss's phase realigns at the next hit. CheckNextPhase is private
        // and per-type (six methods, none in EnemyBehaviour), and at least one ChangePhase queues an
        // attack ⇒ not forced. What IS forced is the manager's declared phase, which is public, a
        // pure function of life, and starts nothing: BossFightManager.SetCurrentActivePhase().
        // ============================================================================================

        /// <summary>Floor of the aligned boss life: NEVER zero. See the block above.</summary>
        private const float BOSS_LIFE_FLOOR = 1f;

        /// <summary>Ids for which the "the owner declares life 0" line has already been written.</summary>
        private static readonly HashSet<string> _bossLifeZeroLogged = new HashSet<string>();

        /// <summary>
        /// Reopens the boss life alignment: the next receive from the owner realigns it. Must be
        /// called where ownership CHANGES (not where it is merely re-read), and it is the only way
        /// to get "on owner change" without writing life on every packet.
        /// </summary>
        private static void ForgetBossLifeAlignment(EnemyState state)
        {
            if (state != null)
                state.BossLifeAligned = false;
        }

        /// <summary>
        /// Aligns the LOCAL boss life with the owner's declared value, once per situation. Called
        /// only from the branch where the SENDER declares it owns the enemy: the sender is the
        /// owner, so its life is the reference in that moment.
        /// </summary>
        private static void AlignBossLife(Enemy enemy, string id, EnemyState state, float ownerLife)
        {
            try
            {
                if (enemy == null || state == null || string.IsNullOrEmpty(id))
                    return;

                // Already done in this situation: exits BEFORE touching anything, and that is what
                // makes the write "one-shot" rather than twenty per second.
                if (state.BossLifeAligned)
                    return;

                // Never on a boss already dead: its state is no longer a function of life.
                if (enemy.Status.Dead)
                    return;

                if (ownerLife <= 0f)
                {
                    // SetHealth(0) calls KillInstanteneously, leaving the boss standing with an
                    // empty bar and the room stuck. Death has its own channel (kill / bdead| /
                    // <scene>_BOSSDEAD) and does not go through here.
                    if (_bossLifeZeroLogged.Add(id))
                    {
                        ModLog.Info($"[BossLife] {FullId(id)} the owner declares life 0: NOT aligning " +
                                    "(never to 0 - it would stand with an empty bar and the room locked)");
                    }

                    return;
                }

                float localLife = enemy.CurrentLife;

                // Downward only, with the life epsilon. If the owner has MORE life, the latch stays
                // open and nothing is written.
                if (ownerLife >= localLife - LIFE_EPSILON)
                    return;

                float applied = Mathf.Max(ownerLife, BOSS_LIFE_FLOOR);
                if (applied >= localLife)
                    return;

                // Written with the game's own setter, never by assigning the field, and above the
                // floor - so it cannot kill.
                enemy.SetHealth(applied);

                state.BossLifeAligned = true;

                string causa = state.BossLifeVisit == _sceneVisit ? "padrone-cambiato" : "ingresso-in-scena";
                state.BossLifeVisit = _sceneVisit;

                string fase = RealignBossPhase(enemy);
                string delta = (applied - localLife).ToString("+0;-0");

                // Marker: one line when life is aligned, with FROM WHOM and TO WHAT.
                ModLog.Info($"[BossLife] {FullId(id)} vita ALIGNED to {applied:F0} (was {localLife:F0}, " +
                            $"{delta}) · da=padrone({(IsHost ? "client" : "host")}) · causa={causa} · " +
                            $"fase={fase} · pavimento={BOSS_LIFE_FLOOR:F0} · vitaRicevuta={ownerLife:F0}");
            }
            catch (Exception e)
            {
                // A failed write must not stop the receive: we lose the alignment, not the packet
                // (which also carries position, animation and orientation).
                ModLog.Error("[BossLife] alignment failed (" + e.GetType().Name + "): " + e.Message);
            }
        }

        /// <summary>
        /// Realigns the manager's declared boss phase when the boss's life was written from outside.
        ///
        /// <c>CheckNextPhase()</c> is private and per-type (six methods, none in EnemyBehaviour),
        /// and at least one ChangePhase queues an attack - forcing it would run a decision that
        /// belongs to the simulating machine at a moment nobody chose. ⇒ the boss phase realigns at
        /// the next hit, declared. <c>BossFightManager.SetCurrentActivePhase()</c> is public, raises
        /// nothing, sends nothing, and only recomputes BossPhase.IsActive and CurrentBossPhaseId from
        /// current life. Called only on the manager whose Boss IS this enemy (Amanecidas has several).
        /// </summary>
        private static string RealignBossPhase(Enemy boss)
        {
            try
            {
                BossFightManager[] managers = UnityEngine.Object.FindObjectsOfType<BossFightManager>();
                if (managers == null || managers.Length == 0)
                    return "not-forced-no-manager";

                bool found = false;
                for (int i = 0; i < managers.Length; i++)
                {
                    BossFightManager manager = managers[i];
                    if (manager == null || manager.Boss == null || manager.Boss != boss)
                        continue;

                    found = true;
                    manager.SetCurrentActivePhase();
                }

                return found ? "manager-aligned(boss-at-first-hit)" : "not-forced-manager-of-another-boss";
            }
            catch (Exception e)
            {
                // Phase is "the drawing": if it cannot be realigned, life is still written.
                return "error:" + e.GetType().Name;
            }
        }

        /// <summary>
        /// Applies the host's ownership token. This is the ONLY thing that sets ownership on a peer,
        /// so the two machines cannot disagree: one enemy, one authority.
        /// </summary>
        public static void ReceiveEnemyOwnership(uint handle, bool ownedByMe)
        {
            if (IsHost)
                return; // the host decides; it never takes orders

            if (!FindIdOfHandle(handle, out string id))
            {
                ReportResolveFailure(handle);
                return;
            }

            EnemyState state = GetOrCreateState(id);
            state.OwnershipKnown = true;

            if (state.OwnedByLocal == ownedByMe)
            {
                // Ownership did not change. But a machine that already owns the enemy can have taken
                // it through the self-heal, which resumed it while _remoteTargets was still empty
                // after a LevelLoaded - so it resumed at the spawn point and started broadcasting
                // that stale position. This token is the host's confirmation, and by now the
                // corrected position may have arrived; adopt it here so the stale resume does not
                // stick.
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

            // Ownership changed: the next receive realigns the boss life (if it is a boss). Here,
            // not in PauseEnemy/ResumeEnemy (those run every send tick for non-owned enemies and
            // would reopen the latch twenty times a second).
            ForgetBossLifeAlignment(state);

            Enemy enemy = ResolveEnemy(id);
            if (enemy == null)
                return;

            // Take effect immediately rather than at the next send tick (20 Hz): an enemy that just
            // lost ownership must stop being simulated now.
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
            // TEMPORARY (PerformanceProbe): times this method and counts the frame. The probe
            // changes no behaviour. The finally matters: this method returns from several places,
            // and a Stopwatch left running would poison the average.
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
            // TEMPORARY (PerformanceProbe): one frame accounted for, and the Animator names read on
            // the first frame of each scene. Remove with the probe.
            PerformanceProbe.Frame();
            // Building the id table is needed on BOTH machines: the host sends indices into it and
            // the peer resolves them. It used to live inside the host-only guard, so a peer never
            // finalised its table and every incoming index was rejected. Only broadcasting is
            // host-only.
            _rescanTimer += Time.deltaTime;
            if (_rescanTimer >= RESCAN_INTERVAL)
            {
                _rescanTimer = 0f;

                if (!_spawnFinalized)
                {
                    // The spawn event never produced a table for this scene. Finalise here instead.
                    OnEnemiesSpawned();
                }
                else
                {
                    // BOTH machines append later discoveries, not just the host.
                    //
                    // The old guard restricted this to the host because the wire handle USED to be
                    // an INDEX into the id list, so a peer could not invent ids. It no longer is: the
                    // handle is a hash of the id string, so a missing or extra id cannot land an
                    // update on the wrong enemy - it only makes the fingerprint differ (reported).
                    // And the bonfire proved the cost: it respawns enemies, the host re-registers them
                    // within half a second, the peer never did - and the two machines held different
                    // lists until a scene change.
                    //
                    // RegisterEnemies is deterministic for spawn points (authored positions) and
                    // caches the id of a free enemy at first registration, so scanning the same
                    // scene on both machines yields the same ids. The [EnemySync] "non in _ids"
                    // marker and IMPRONTA DIVERSA are the guard: a genuine divergence is VISIBLE
                    // instead of silently frozen.
                    RegisterEnemies(_scene);

                    // The scan above may have appended ids. Refresh the fingerprint now.
                    RefreshFingerprintIfDirty();
                }

                // Snapshot on BOTH machines, so the two logs can be lined up.
                LogDiagnosticSnapshot();
            }

            // Anything that changed the id list since the last pass must be reflected before the
            // next update goes out.
            RefreshFingerprintIfDirty();

            // Follow the remote snapshots smoothly. Runs before the send gate below, because that
            // gate returns early on most frames and the easing has to happen every frame.
            InterpolateRemoteEnemies();

            // Settle any enemy sitting at zero health whose death never started. Runs every frame so
            // the grace period is measured in real time, and does nothing while everything is
            // healthy.
            SettleStuckDeaths();

            // Every frame, so an attack shorter than the snapshot interval is still recorded. Writes
            // only when the state actually changes.
            LogAnimationChanges();

            // Every frame on BOTH machines, and before the connection gate: the reveal of an enemy
            // is a fact about the world, not about the network, and the latch must advance even
            // while nobody is connected or the first reveal after a reconnect would be
            // unannounced.
            DetectReveals();

            // ② BOSSFIGHT|<scene> = OFF | IN_CORSO | FINITA. Same place and reasoning as
            // DetectReveals: before the connection gate, because the bossfight is a fact of the
            // WORLD, not of the network, and in single-player the state must exist anyway. Read-
            // only (see the "third value" block above).
            UpdateBossFightState();

            // Both machines receive: the owner to follow the authoritative state, the non-owner to
            // apply it.
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

                // ONE ENEMY MUST NOT BE ABLE TO STOP THE WHOLE PASS. This body used to be
                // unprotected: a diagnostic threw, the exception aborted the iteration, and
                // everything after it never ran - the update for that enemy AND for every enemy
                // following it, the death sweep, and on the host the ownership decision and its
                // token. A synchronisation loop must be able to lose one enemy and carry on.
                try
                {

                    // DEATH IS CHECKED FIRST, BEFORE the activity check. The game deactivates off-
                    // camera enemies, so a dead enemy outside the view has activeInHierarchy == false.
                    // With the activity check first, the loop skipped it and the death was never
                    // announced: the other player kept a live "ghost". A dead enemy is dead
                    // regardless of whether it is currently being drawn.
                    if (enemy.Dead || enemy.CurrentLife <= 0f)
                    {
                        (dead ??= new List<string>()).Add(id);

                        // Recorded here, before the enemy is dropped from _ids. The snapshot runs
                        // only twice a second, and by the time it ran the dead one had already been
                        // removed and no death was ever logged.
                        if (_killLogged.Add(id))
                        {
                            ModLog.Info($"[EnemyKill] {ShortId(id)} MORTO Dead={enemy.Dead} " +
                                        $"vita={enemy.CurrentLife:F1} attivo={enemy.gameObject.activeInHierarchy} " +
                                        $"pos=({enemy.transform.position.x:F1},{enemy.transform.position.y:F1})");
                        }
                        _killsSent++;
                        Main.Multiplayer.SendEnemyKill(handle);
                        // Also announce it scene-independently: the other player may be in another
                        // room and would otherwise never learn about this death.
                        Main.Multiplayer.SendEnemyDeathGlobal(id);

                        // M1 — the boss death is announced so the dead can apply it.
                        //
                        // The announcement above is identical for all enemies, and on the receiver
                        // the death is applied by <Type>::Damage(Hit): for a boss that starts with
                        // `if (Status.Dead || Core.Logic.Penitent.Stats.Life.Current <= 0f) return;`,
                        // which with the LOCAL player down DISCARDS the death - the boss stays
                        // standing and its cutscene never starts. The `bdead|` prefix tells the
                        // receiver "this is a BOSS death: apply it with Kill()", which does not pass
                        // through the guard (see ApplyBossDeath). It is one MORE announcement, not
                        // one fewer: the generic id is still sent.
                        if (IsBoss(enemy, id))
                        {
                            Main.Multiplayer.SendEnemyDeathGlobal(BossDeathPrefix + id);
                            ModLog.Info($"[BossDeath TX] {FullId(id)} boss={enemy.GetType().Name} " +
                                        $"vita={enemy.CurrentLife:F1} StatusDead={enemy.Status.Dead} " +
                                        $"localeMorto={PlayerDeathSync.IsLocalPlayerDead()}");

                            // Warning to the dead player, second attachment point: THIS machine has
                            // just seen the boss die, so "the boss is dead" is true here too, and
                            // the cutscene is at the door. The predicate (is the local player dead?)
                            // is checked by BossDeathWarning itself, and the per-window dedup
                            // prevents this and ApplyBossDeath from producing two notifications for
                            // the same death.
                            BossDeathWarning.WarnBossDied("tx:boss-death-seen-on-this-machine");
                        }

                        // Remembered as well as announced: a later reconnect must not depend on this
                        // machine still being in the room. Only reached while connected.
                        _announcedDeaths.Add(id);
                        continue;
                    }

                    Vector2 position = enemy.transform.position;
                    float life = enemy.CurrentLife;
                    byte orientation = ReadOrientation(enemy);
                    int animState;
                    float animTime;
                    ReadAnimation(enemy, out animState, out animTime);

                    // Skip unchanged enemies to save bandwidth; the periodic full sweep guarantees
                    // everything is resent regularly. TryGetValue, never an indexer: a freshly
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
                    // Previously each machine decided for itself from the distance it measured
                    // locally. Those measurements cannot agree - the two players hold slightly
                    // different positions, worst on slopes - so both machines could decide the
                    // closest player was theirs, simulate the same enemy, and publish two conflicting
                    // positions. Result: one enemy standing in two places, with aggro on both players
                    // at once.
                    //
                    // Now the host arbitrates and the peer obeys: the peer's OwnedByLocal is set only
                    // by the EnemyOwnership packet, never recalculated here.
                    if (IsHost)
                    {
                        DecideOwnership(enemy, id, previous, true);

                        if (!previous.OwnershipKnown || previous.LastPublishedOwnerIsMe != previous.OwnedByLocal)
                        {
                            previous.OwnershipKnown = true;
                            previous.LastPublishedOwnerIsMe = previous.OwnedByLocal;

                            // The wire carries "the owner is YOU, the receiver", which is the
                            // opposite of OwnedByLocal. The receiving side assigns the payload
                            // straight to its own OwnedByLocal, so sending OwnedByLocal sent "you
                            // own it" precisely for the enemies the HOST owns. The negation is the
                            // whole fix.
                            Main.Multiplayer.SendEnemyOwnership(handle, !previous.OwnedByLocal);
                        }
                    }

                    // "Owned here" means this machine runs the game's own AI, which produces real
                    // attacks and deaths. The other machine freezes it and applies state.
                    //
                    // BUT A PEER MUST NOT WAIT FOR AN ANSWER THAT CANNOT COME.
                    //
                    // The host arbitrates only what it can see: _ids holds the enemies of ITS OWN
                    // scene. When the two players are in different rooms, the enemies on this side
                    // are not in the host's list at all, it never decides on them, and the token is
                    // never sent (the server would not deliver it anyway: ownership is filtered by
                    // scene). OwnershipKnown is exactly "the host has never said anything about
                    // this one", which is the right thing to test. Waiting for a token that cannot
                    // arrive left these enemies paused for the whole session.
                    //
                    // Taking it locally is safe because ownership is exclusive by construction: the
                    // moment the host sees the enemy it publishes a token, the sibling block above
                    // latches it, and the role is settled by the host from then on - with
                    // OWNER_HOLD_SECONDS absorbing the handover.
                    if (previous.OwnedByLocal)
                    {
                        ResumeEnemy(enemy);
                        ApplyAggroTarget(enemy, id);
                    }
                    else if (!IsHost && !previous.OwnershipKnown)
                    {
                        previous.OwnedByLocal = true;

                        // Self-resolution: I became owner. The boss life latch is reopened, because
                        // if this boss were taken from me the life would realign to whoever takes it.
                        ForgetBossLifeAlignment(previous);

                        AdoptLastKnownPosition(enemy, id);
                        ResumeEnemy(enemy);
                        ApplyAggroTarget(enemy, id);
                    }
                    else
                    {
                        PauseEnemy(enemy);
                    }

                    // The peer obeys the host, so it transmits exactly the enemies the host granted
                    // it.
                    if (!previous.OwnedByLocal)
                        continue;

                    // Position and life are transmitted for an enemy that is tracked and owned,
                    // whether or not it is currently ACTIVE - and that reversed a real decision.
                    //
                    // There used to be a gate `if (!enemy.gameObject.activeInHierarchy) continue;`
                    // justified by "the game never deactivates for being off camera". The logs
                    // settle it: the enemies that failed to transfer were the ones OUTSIDE the view.
                    // Whatever the reason an enemy is inactive, its TRANSFORM still holds the truth,
                    // and that truth is what the other machine needs at handover. Skipping it left
                    // the other machine with no position at all, so it fell back to its own copy -
                    // the spawn point. An enemy not in play yet has a position too; transmitting it
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

                    // The playback position is passed on as read, INCLUDING on state change. This
                    // line used to force animTime = 0f on every state change, and it is the exact
                    // opposite of correct: a transition is the only moment the receiver needs it.
                    // Sending zero there made every transition restart the clip from the beginning -
                    // on a one-shot that shows as the animation starting and being cut off after
                    // milliseconds; on a loop it restarts the walk. Measured: the same transition,
                    // same enemy, repeated 33 and 58 times in a session at ~6 restarts/s in healthy
                    // frames at 59 FPS.

                    previous.Position = position;
                    previous.Life = life;
                    previous.AnimState = animState;
                    previous.Orientation = orientation;
                    previous.LastSweep = Time.time;

                    // ONLY THE OWNER TRANSMITS. The host used to broadcast every enemy, including
                    // the ones the peer was simulating, so both machines published a position for
                    // the same enemy and the two values fought each other. One enemy now has exactly
                    // one source of position.
                    if (!previous.OwnedByLocal)
                        continue;

                    _updatesSent++;
                    Main.Multiplayer.SendEnemyUpdate(handle, position, life, animState, animTime, _listFingerprint, previous.OwnedByLocal, orientation);
                }
                catch (Exception e)
                {
                    _syncLoopExceptions++;
                    if (_syncLoopExceptions <= 20)
                        ModLog.Error($"[EnemySync] exception on enemy {ShortId(id)}: {e.GetType().Name} - " +
                                     $"{e.Message} (enemy skipped, the rest of the pass continues)");
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
        /// spawn point and hands the same dead enemy back - so the death was rebroadcast forever.
        /// Dropping the id is safe now that the wire carries a content-derived handle.
        /// </summary>
        private static void ForgetEnemy(string id)
        {
            // Look the enemy up FIRST: after the removal the lookup is gone, and then it could never
            // be dropped from the registered set, so a later respawn would be treated as new.
            Enemy enemy;
            _enemies.TryGetValue(id, out enemy);

            _enemies.Remove(id);
            _ids.Remove(id);
            _lastSent.Remove(id);
            _remoteTargets.Remove(id);

            // Pruning shifts every index after the one removed, and the index WAS the wire handle -
            // so this is not a tidy-up. The fingerprint is refreshed so the divergence is at least
            // reported.
            _idsDirty = true;

            if (enemy != null)
            {
                _registered.Remove(enemy);

                // The instance keeps its id on purpose: the object can outlive its tracking for a
                // moment while it dies, and the next scan would otherwise give it a NEW id and
                // re-register it as a fresh enemy - which is how 6 enemies became 25 registrations
                // and produced the duplicates in game.
            }
        }

        /// <summary>Position, health and animation state broadcast sent by the host.</summary>
        public static void ReceiveEnemyUpdate(uint handle, Vector2 position, float currentLife, int animState, float animTime, int fingerprint, bool ownedRemotely, byte orientation)
        {
            _updatesReceived++;

            // Detect the silent corruption: if the two machines built different id lists, every
            // index means something different and updates land on the wrong enemy. Reported again
            // after the cooldown rather than once per level load: a mismatch seen while the id list
            // was being built would be wrong AND would silence every real mismatch for the rest of
            // the scene. The cooldown keeps a genuine divergence visible.
            if (fingerprint != _listFingerprint && Time.time - _fingerprintMismatchReported > FINGERPRINT_REPORT_COOLDOWN)
            {
                _fingerprintMismatchReported = Time.time;
                _fingerprintMismatches++;
                ModLog.Error($"[EnemySync] IMPRONTA DIVERSA: host={fingerprint} locale={_listFingerprint} " +
                             $"- the two machines have different enemy lists, updates will land " +
                             $"on the wrong enemy. Compare 'multiplayer sync dump' on both PCs " +
                             $"(spawn point locali: {_spawnPoints.Count}, id locali: {_ids.Count}, " +
                             $"segnalazioni: {_fingerprintMismatches}).");

                // The two markers of the id-list check: the line above says THAT the lists diverge
                // and reports the LOCAL counts (the wire carries only the int fingerprint). This
                // request asks the peer for ITS two counts and ITS list, printed as
                // `[Impronta] confronto #n · spawn: peer=… locale=… · id: peer=… locale=… · ...`. It
                // is inside this branch, already limited to one line per 15s, so the request cannot
                // be more frequent than the report that justifies it. Zero behaviour: nobody reads
                // the response, and the line above is untouched.
                SyncIdListMarker.RequestPeerIdList();
            }

            Enemy enemy = ResolveByHandle(handle);
            if (enemy == null)
            {
                ReportResolveFailure(handle);
                return;
            }

            // Record the sender's claim. This is the input the arbitrating host was missing: without
            // it the host never learned that the peer was simulating an enemy, so it kept simulating
            // too and the two positions fought each other.
            EnemyState claimState = null;
            if (FindIdOfHandle(handle, out string claimedId))
            {
                claimState = GetOrCreateState(claimedId);
                claimState.OwnedByRemote = ownedRemotely;
                if (ownedRemotely)
                    claimState.RemoteEverClaimed = true;
            }

            // Ownership decides what happens here. If the sender owns this enemy, this machine must
            // NOT simulate it: pause the behaviour tree and follow the state being sent.
            //
            // PauseBehaviour stops only the tree, never the Animator, so idle and walking keep
            // animating locally and hit reactions still show through the synced health. What a
            // paused enemy cannot do is decide to attack - which is why the owner runs the real AI.
            //
            // THE PAUSE IS NEVER UNDONE HERE ON THE STRENGTH OF WHAT THIS MACHINE BELIEVED. The
            // record is set from what the SENDER declared, in both directions. It used to be left
            // alone, and this method read ownedRemotely from the packet while reading OwnedByLocal
            // from a record it never updated - so a machine that considered itself the owner paused
            // the enemy and resumed it again inside the same packet, twenty times a second, on both
            // machines, on the same enemy. The record and the packet disagreed permanently about who
            // was simulating it.
            if (ownedRemotely)
            {
                // The record IS written here. An update packet is asymmetric: only the owner
                // transmits, and it always declares ownedRemotely = true. So this flag carries no
                // NEW INFORMATION - and yet it matters, because it is an ACTUATOR: it decides who
                // loses the send gate, who the send loop ends up pausing, and which side the
                // ownership token starts from. With the write in place, ownership changes measured
                // 0.04/s; without it, 0.17/s (4x churn), and the windows where an enemy stood still
                // next to a player went from 22% to 59-61% of engaged time. Removing a write because
                // it "carries no information" ignores that its effect was on control flow.
                if (claimState != null)
                {
                    // If the record still said "mine", THIS packet is the moment the boss changed
                    // hands: the life arriving from here on is the new owner's, and it must be
                    // taken once (see AlignBossLife). The comparison must happen BEFORE the write,
                    // because after it OwnedByLocal is false anyway.
                    if (claimState.OwnedByLocal)
                        ForgetBossLifeAlignment(claimState);

                    claimState.OwnedByLocal = false;
                    claimState.OwnerSince = Time.time;
                }

                PauseEnemy(enemy, "remote owns");

                // The state is still applied on the non-owner. PauseBehaviour leaves the Animator
                // running, so idle/walk animate locally anyway, but anything the behaviour tree
                // would have triggered - attacks, hurt, death - has to come from here. Deliberately
                // narrow: only the state is forced, never the whole animation graph.
                ApplyAnimation(enemy, animState, animTime);

                // Facing is part of the state, not a decoration: the game only calls SetOrientation
                // from the behaviour tree, which is paused here, so without this the enemy would
                // keep facing the wrong way and its walk would look mirrored.
                ApplyOrientation(enemy, orientation);
            }
            else
            {
                // The sender says this machine owns the enemy: let its own AI drive it, so animations
                // come from the game's per-enemy code instead of being forced from outside.
                //
                // Setting OwnedByLocal is the other half of the fix, and it is not optional: without
                // it an enemy the host hands BACK would stay marked as not ours, and the send loop
                // skips anything not owned locally - so this machine would never simulate it again.
                // This is the ONLY place the packet tells this machine something it did not know.
                if (claimState != null)
                {
                    claimState.OwnedByLocal = true;
                    claimState.OwnerSince = Time.time;

                    // I became owner: the latch is reopened, so if I lose it again the life realigns
                    // to whoever takes it.
                    ForgetBossLifeAlignment(claimState);
                }

                ResumeEnemy(enemy);
            }

            // A BOSS keeps its own health: only position, animation and facing are shared. See
            // _bossIds for why writing a boss's health to zero leaves it standing with an empty bar
            // and the room stuck.
            bool isBoss = IsBoss(enemy, IdOf(enemy));

            if (!isBoss)
            {
                // The life is written with the game's own setter, never assigned to the field:
                // Entity.SetHealth writes the value and, at or below zero, calls KillInstanteneously
                // for us. Assigning the field left the enemy at zero health with Get_Dead() true
                // (Entity.Dead = !(Life.Current > 0)) but the death sequence never started.
                enemy.SetHealth(currentLife);
            }
            else if (ownedRemotely)
            {
                // AND THE BOSS? Life travels in every update, and until now it was discarded
                // (`if (!isBoss)`): two local pools, therefore two bars, and the measured gap is
                // 110 HP out of 400 constant for the whole fight.
                // ■ Channel: no new one. `currentLife` is already in the receive, and the sender is
                //   the owner - no packet type, no server patch, no protocol.
                // ■ When: NOT on every packet. One write per situation, at scene entry and every
                //   ownership change (EnemyState.BossLifeAligned, reopened by
                //   ForgetBossLifeAlignment). The rest is done by the damage relay, which works.
                // ■ Forbidden: never 0 (floor at 1 HP), never on a dead boss, never upward. All
                //   inside AlignBossLife, with the reasoning written there.
                // Only when the SENDER declares ownership: if it says "you own it", its life is not
                // a reference and nothing is touched.
                AlignBossLife(enemy, claimedId, claimState, currentLife);
            }

            // Record the snapshot as a TARGET. The position is applied smoothly in Update, once per
            // frame, instead of being snapped here 20 times a second.
            if (FindIdOfHandle(handle, out string targetId))
            {
                _remoteTargets[targetId] = position;
                // PUNTO 0: the entry also says WHEN it was written. An entry of this visit is the
                // result of a live simulation; an older one is a leftover, and for a boss it is not
                // a position to apply (see SkipBossFollow).
                _remoteTargetsVisit[targetId] = _sceneVisit;
            }

            // Death is treated as a CONSEQUENCE OF HEALTH, not only as its own packet. The death
            // packet proved unreliable in game - the peer kept showing a frozen enemy with full
            // life because the kill never landed - and health already travels in every update, so a
            // zero can be acted on immediately and cannot be missed.
            if (!isBoss && !IsHost && (currentLife <= 0f || enemy.Status.Dead))
            {
                KillEnemyLocally(enemy, handle);
            }
        }

        /// <summary>
        /// Runs the native death sequence on an enemy whose health reached zero, then stops tracking
        /// it. The native path is used rather than deactivating, so the game plays its own death
        /// animation and handles whatever the enemy drops.
        /// </summary>
        /// <summary>
        /// Settles enemies stuck at zero health with no death sequence running, after a grace period.
        ///
        /// Step one is the game's own KillInstanteneously(), which is public, sets EntityStatus.Dead,
        /// zeroes health and fires the death events the level listens to. It does not touch the
        /// Animator, so some types may still not animate - but the enemy dies properly, which is
        /// what matters for the room and the drop. Step two, only if even that did not take, removes
        /// the object outright (skips the death events, so it is the last resort).
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
                    ModLog.Info($"[EnemyDeath] {ShortId(id)} at life zero for {DEATH_GRACE_SECONDS:F1}s with no " +
                                $"death sequence: forcing it with KillInstanteneously");
                }

                enemy.KillInstanteneously();

                if (!enemy.Status.Dead)
                {
                    // Even the native kill did not take: take it out of the scene so it cannot be
                    // found, hit, or block anything. The spawn point is marked so the room does not
                    // resurrect it.
                    //
                    // This Destroy is the SAME HAZARD as the one removed from DestroyDeadEnemy, and
                    // is left here only because it has never been observed to run. If it ever
                    // fires, it can invalidate the same game references (Execution.ExecutedEntity,
                    // prop of the execution interactable) and produce the same dead session:
                    // unhandled exceptions out of StuntBehaviour, EXECUTION blocker left on with no
                    // key that responds. First time it appears in a log, change to SetActive(false).
                    ModLog.Info($"[EnemyDeath] {ShortId(id)} survives even KillInstanteneously: removing it");
                    MarkSpawnPointConsumed(id);
                    UnityEngine.Object.Destroy(enemy.gameObject);
                    ForgetEnemy(id);
                }
            }
        }

        private static void KillEnemyLocally(Enemy enemy, uint handle)
        {            if (enemy == null)
                return;

            // Ask whether the DEATH SEQUENCE has started, not whether the health is at zero.
            // Entity.Dead is computed as !(Life.Current > 0), so it turns true the instant the health
            // is written to zero - including by this mod while syncing. Guarding on it meant the
            // call below was never reached: the peer learned an enemy had died, saw Dead already
            // true, and did nothing, so no Death trigger was ever set. EntityStatus.Dead is the
            // separate bool KillInstanteneously() sets, so it is the honest answer to "has the game
            // begun killing this enemy".
            if (!enemy.Status.Dead)
            {
                // Through the game's own damage entry point, so the hurt flash, the sound and - for
                // the many types that set their DEATH trigger there - the death animation all
                // follow the normal flow.
                //
                // Normal/Normal and no flags: this is the mod settling an agreed death, not a blow
                // somebody struck.
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

                // Deactivate the corpse, and this is the other half of the fix.
                //
                // The announcement channel (DestroyDeadEnemy) deactivates via SetActive(false), but
                // it can only do so while the enemy is still in _ids - and this health channel has
                // just forgotten it. So a peer that receives the death through the synced health
                // (the normal case) removed the id here, the announcement then found nothing to
                // deactivate, and the corpse stayed visible, untargetable, with its AI off: the
                // "ghost". On the peer the death animation never plays on its own because the
                // behaviour tree is paused, so there is nothing to wait for - deactivating
                // immediately is the correct end.
                //
                // ForgetEnemy has already run, so this cannot race the announcement anymore.
                if (enemy != null && enemy.gameObject != null && enemy.gameObject.activeSelf)
                    enemy.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Logs every animation change, per enemy, exactly once.
        ///
        /// Run every frame: the game devotes a separate behaviour per enemy to driving its Animator,
        /// with no common entry point, so the Animator's current state is the only dependable signal.
        /// Its purpose is comparing machines: the same transition should appear on both sides at
        /// roughly the same moment.
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

                // Was the transition about to be reported OUR doing? The check below compares
                // _appliedAnim against the state being read now, and it is not enough on its own:
                // ApplyAnimation writes _appliedAnim during the frame that imposes the state, while
                // the Animator still reports the OLD state until the controller evaluates, one frame
                // later. So on the frame of the imposition the comparison fails, a line describes our
                // own forcing, and _lastAnim is overwritten with the wrong value. _appliedAnimPrev
                // closes that gap without waiting a frame: it records the pair (state the Animator
                // showed, state we imposed), so the echo is recognised by its ORIGIN.
                int appliedPrev;
                bool isEcho = _appliedAnimPrev.TryGetValue(id, out appliedPrev) && appliedPrev == previous;

                // A state we applied ourselves is not a local change: reporting it would describe our
                // own forcing as if the game had animated something.
                int applied;
                if ((_appliedAnim.TryGetValue(id, out applied) && applied == stateHash) || isEcho)
                {
                    _lastAnim[id] = stateHash;
                    _lastAnimSample[id] = animTime;
                    continue;
                }

                // How long the state we are leaving actually lasted, and how far into its clip it
                // got. Only transitions the game made count: a timer reset by our own echo would
                // report a long-lived state as freshly started. "uscita" - the playback position the
                // leaving state had reached - is the measurement that settles whether a one-shot was
                // cut or just ended.
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

                // TEMPORARY (PerformanceProbe): resolve the two hashes to names, once each per
                // session. The hashes cannot be reversed from a file - they are FNV-1a of names
                // inside the packed AnimatorController - so the running Animator is asked. Knowing
                // whether the state entered hundreds of times is an attack or an idle decides
                // whether this is a defect. Remove with the probe.
                NameStateIfNew(animator, stateHash, LabelOf(enemy));

                // TEMPORARY (PerformanceProbe): counts repetitions that identify a forced replay.
                // A legitimate transition happens once; the same pair repeating for the same enemy is
                // this mod restarting the clip. Keyed by POSITION: a total was not enough to act on -
                // one transition could look like one enemy transitioning 386 times when it was
                // several doing it dozens of times each. Remove with the probe.
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
        /// Applies the facing the owner transmitted.
        ///
        /// No hysteresis here: this method is the mod's write, twenty per second, while the churn is
        /// the GAME's sixty writes per second from Ghost's OnUpdate - which never comes through here.
        /// The band lives in OrientationProbePatch, on Entity.SetOrientation, which every writer goes
        /// through. SetOrientation is the game's own single entry point: it stores the value and
        /// flips the sprite. passFlip is true because the game handles the flip inside, given the
        /// previous orientation was kept in sync.
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

            // TEMPORARY (PerformanceProbe): one applied flip. Nothing else in the log measures
            // orientation, so without this the spinning-enemy fix could only be judged by eye.
            // Remove with the probe.
            PerformanceProbe.NoteOrientationFlip(IdOf(enemy));

            enemy.SetOrientation(wanted, true, true);
        }

        /// <summary>
        /// Reports the clip name behind a state hash, once per hash per session.
        ///
        /// TEMPORARY (PerformanceProbe): the hash cannot be reversed from outside the game - it is
        /// FNV-1a of the state name, but the names live in a packed AnimatorController. Asking the
        /// Animator for the clip it is playing sidesteps the problem, and the answer decides whether
        /// hundreds of entries into one state are hundreds of attacks (nothing wrong) or an idle
        /// being re-entered (something wrong). Remove with the probe.
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
        /// Written as literals on purpose: they were read from the running game and resolved to
        /// names by inverting the game's own hash; reproducing them by a second hash function could
        /// disagree with the first. The literal cannot.
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
        /// LOCOMOTION GOES THROUGH THE PARAMETER, NOT THROUGH Play. This is the fix for the enemy
        /// that "slides": its legs restart while its body advances.
        ///
        /// Play("Walk") sets the state for one frame, but the controller CHOOSES the state from the
        /// WALK bool every frame - and WALK is false here, because the only thing that raises it is
        /// FoolBehaviour.Chase inside the paused tree. So the controller pulled the enemy back to
        /// Idle on the next frame, the next packet forced Walk again from zero, and the two fought at
        /// twenty vs sixty hertz. Measured: 250 forced Walk→Idle against 2 the other way in one
        /// session, with the same pair repeated up to 31 times in two seconds.
        ///
        /// Setting the bool is what the game itself does, so the controller performs an ordinary
        /// transition - and an ordinary transition PRESERVES THE CLIP PHASE, which is what was being
        /// lost. One-shot states (attack, death, turn) keep using Play, because they are not driven
        /// by a bool and nothing will undo them.
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

            // Remembered so the animation log does not mistake our own forcing for a local change,
            // which is what turned one transition into hundreds of them in the peer's log.
            _appliedAnim[IdOf(enemy)] = stateHash;

            // The pair (state the Animator showed BEFORE this call, state we are imposing). Read
            // before the Animator can evaluate, so it describes the transition we caused - see the
            // echo check in LogAnimationChanges.
            _appliedAnimPrev[IdOf(enemy)] = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;

            if (stateHash == ANIM_STATE_WALK || stateHash == ANIM_STATE_IDLE)
            {
                // Locomotion: hand the decision to the controller and let it transition, so the
                // clip keeps its phase. This is what the game does, and it is why the legs stop
                // restarting.
                animator.SetBool(WALK_PARAM, stateHash == ANIM_STATE_WALK);
                return;
            }

            // A one-shot. The playback position matters here, so it is passed on - wrapped into
            // [0,1) first: on a looping clip normalizedTime grows past 1, and the old test "between
            // 0 and 1" sent every later frame to Play(hash, 0, 0), restarting the clip from the
            // beginning. That was the second half of the sliding.
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
        /// Damage approved by the host. The machine that swung the sword already applied it natively,
        /// so this only runs on machines that did not swing.
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

            // If the peer that swung also landed the killing blow, its local application already
            // killed the enemy here. Applying the relayed damage to that corpse would run the death
            // sequence a second time (double purge/tears credit). The host's next update resyncs
            // the health.
            //
            // The test is Status.Dead, the "death sequence already started" flag, and NOT Entity.Dead.
            // Entity.Dead is true as soon as health reads zero, so guarding on it used to swallow
            // every relayed hit on an enemy sitting at zero health whose death had not begun - the
            // exact state this path exists to fix.
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
        /// Packs the three booleans of a Hit that change how the receiving machine reacts.
        /// DontSpawnBlood suppresses the blood spray; parry/block flags decide whether the enemy
        /// treats the blow as guarded. NoInlining is deliberate: the method takes a Hit by value,
        /// and without it the JIT could inline and touch a value type of a game assembly at a moment
        /// this mod does not control.
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
        /// count reflects host-originated damage rather than relayed damage.
        /// </summary>
        public static void RecordDamageApplied()
        {
            _damageApplied++;
        }

        /// <summary>
        /// Records a death announced from another scene, and applies it here.
        ///
        /// The important part is MarkSpawnPointConsumed: it registers the spawn point in the GAME's
        /// own consumed list, which CreateEnemy consults before creating anything, so the ghost does
        /// not come back on the next scene reload without intercepting anything.
        /// </summary>
        public static void ReceiveEnemyDeathGlobal(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId))
                return;

            // A break announcement, not a death - recognised BEFORE anything below runs.
            //
            // This channel carries free-form strings: spawn-point deaths arrive as "sp|...".
            // Breakables arrive as "brk|scene|guid", and treating one as an enemy id would put a
            // GUID into the death memory, where MarkSpawnPointConsumed would ignore it and the
            // enemy loop would search for it forever.
            //
            // They travel here rather than as progress entries for a measured reason: the progress
            // channel deduplicates per id for the server session, so after a bonfire restored a lamp
            // the same player could break it again and the announcement was silently discarded. This
            // channel keeps no such memory.
            string breakScene;
            string breakGuid;
            if (BreakSyncMemory.TryParseBreakId(enemyId, out breakScene, out breakGuid))
            {
                BreakSyncMemory.ApplyOrRemember(breakScene, breakGuid);
                return;
            }

            // A bonfire announcement, recognised here - before the two lines below.
            //
            // Everything not a "brk|" falls through to _pendingDeaths.Add, the memory that makes an
            // enemy die. A "falo|" left in it is inert, but it inflates the counters and leaves the
            // fix depending on nobody ever parsing that string.
            if (BonfireSync.IsBonfireId(enemyId))
            {
                ReplicateBonfire(BonfireSync.SceneOf(enemyId));
                return;
            }

            // A player died, or came back. Same reason as the bonfire: this payload is a PLAYER NAME.
            // It would be inert in _pendingDeaths (that list is never iterated, MarkSpawnPointConsumed
            // demands "sp|", and a name cannot contain '|'), but it would linger in memory and
            // inflate the "received" counter that the bonfire logs report.
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

            // A BIRTH, recognised here - BEFORE the line below, which consumes the id as a death.
            // This channel carries free-form strings: "sp|" for deaths, "brk|" for lamps, "falo|"
            // for bonfires, "pdead|"/"palive|" for players. "nato|" is the sixth - the sender's game
            // just REVEALED an enemy, and this machine must wake its own copy. Left unrecognised,
            // the id would fall through to _pendingDeaths.Add and the enemy would be marked dead at
            // the moment it was born.
            if (IsBirthId(enemyId))
            {
                ReceiveBirth(BirthIdOf(enemyId));
                return;
            }

            // An EXECUTION, recognised here - BEFORE the line below. The prefix sits next to
            // "nato|" for the same reason: anything unrecognised falls into _pendingDeaths.Add and
            // makes the enemy die.
            //
            // The prefix is stripped, not ignored: the id that follows is a real enemy id
            // ("sp|scene|SPAWN|x|y"), and it is put back into enemyId on purpose: everything that
            // follows (death memory, MarkSpawnPointConsumed, the kill loop) runs EXACTLY as for a
            // normal death. An executed enemy IS a dead enemy, its death announcement travels
            // separately (the send loop catches it within ~50ms because Execution.OnUse calls
            // KillEntity() BEFORE animating), and doing the same bookkeeping here is IDEMPOTENT:
            // it closes the window where only one of the two announcements arrived.
            if (ExecutionSync.IsExecutionId(enemyId))
            {
                string executed = ExecutionSync.IdOf(enemyId);
                if (string.IsNullOrEmpty(executed))
                    return;

                ApplyRemoteExecution(executed);
                enemyId = executed;
            }

            // THE DEATH OF A BOSS (M1), recognised here - BEFORE the line below. Same reason as
            // the bonfire, the player death, the birth and the execution: anything unrecognised
            // falls into _pendingDeaths.Add and is treated as a normal enemy death from then on.
            //
            // AND WE EXIT HERE WITHOUT THE GENERIC BOOKKEEPING, on purpose:
            //   - _pendingDeaths is not needed: the generic id arrives on its own (the announcement
            //     above is not removed) and does its part as it always has;
            //   - MarkSpawnPointConsumed on a boss would be NEW damage: a boss is not a consumable
            //     enemy, and marking it consumed can prevent it from returning to the scene.
            // ⇒ The only thing this branch does is Kill() on the local boss, and only when needed.
            if (IsBossDeathId(enemyId))
            {
                string bossId = BossDeathIdOf(enemyId);
                if (!string.IsNullOrEmpty(bossId))
                    ApplyBossDeath(bossId);
                return;
            }

            _pendingDeaths.Add(enemyId);
            MarkSpawnPointConsumed(enemyId);

            // Already tracked here: the enemy exists (or is being created) in this scene, so it has
            // to be taken down as well. Marking the spawn point only prevents FUTURE creations.
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
        /// Reproduces on THIS machine the sound the executing machine heard the moment the enemy
        /// became executable.
        ///
        /// WHAT THIS METHOD IS NOT, and that is the important part. It does not instantiate the
        /// execution prop, hide the enemy, or touch the local penitent, the camera or the input. The
        /// CUTSCENE of the execution is driven by Tools.Level.Interactables.Execution.OnUse(), and
        /// it is welded to the LOCAL player: it calls Core.Logic.Penitent (hides the sprite and
        /// disables physics), Core.Input.SetBlocker("EXECUTION", true), moves the local penitent
        /// onto the prop and drives the global camera and TimeScale. Calling it here would hide the
        /// player who is not executing, block their keys and - if anything threw before the last
        /// line - leave the blocker on FOREVER. This method is written so the blocker is NEVER
        /// turned on here.
        ///
        /// WHY IT IS WORTH IT ANYWAY. The enemy's DEATH already travels: OnUse calls KillEntity()
        /// before animating, Entity.Kill() zeroes the life, and the send loop announces EnemyKill
        /// and EnemyDeathGlobal within ~50ms. What does NOT travel is the SOUND: it is in the
        /// Behaviour.Execution() branch, which EXITS BEFORE EnemyDamageArea.TakeDamage - and
        /// TakeDamage is the only point EnemyDamagePatch hooks. That is why the A1 relay does not
        /// cover it.
        ///
        /// FULLY GUARDED. This code runs inside NetworkManager.ProcessQueue, which
        /// Multiplayer.OnLateUpdate calls in a try/FINALLY - without catch. An exception here escapes
        /// BEFORE the queue is drained and BEFORE SendQueue: the same packet is reprocessed forever
        /// and the mod stops sending.
        /// </summary>
        private static void ApplyRemoteExecution(string id)
        {
            try
            {
                Enemy enemy = ResolveEnemy(id);
                LastExecutedEnemy = enemy;      // A6: used only by the controller fallback.
                bool here = enemy != null && enemy.gameObject != null;

                ModLog.Info($"[Exec RX] {id} nemico_in_scena={(here ? "si" : "no")}");

                // The DisplayNotification("The other player executed an enemy") that used to
                // be here has been removed. It was a debug line, and the execution ghost is now
                // visible (OtherPlayerScript.PlayExecutionAnimation): the notification added
                // nothing and left an Italian prompt in the middle of the game.
                //
                // Only that was removed. The [Exec RX] markers above and below remain: they are the
                // only observability of this path, and without them "the packet did not arrive" and
                // "it arrived and did not play" would be indistinguishable.
                //
                // And it cannot be replaced with a better notification: the sender does not travel
                // on this channel (EnemyDeathGlobal does not carry it, and ProcessQueue only passes
                // bytes), so any text with a NAME would be invented. See the note in PlayerDeathSync.
                if (Core.Logic.Penitent == null || Core.Logic.Penitent.Audio == null)
                    return;

                // The game's verbatim call, read from all sixteen overrides of Behaviour.Execution:
                // Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy(). It is a CATALOG event
                // ("PenitentSimpleEnemyHit", the public const PenitentAudio.SimpleEnemyHitEventKey),
                // so it resolves on THIS machine's catalog: no key, no bank path, no string travels.
                // It is also already used by the mod in two places, so it is not a signature to guess.
                Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy();

                if (!here)
                    ModLog.Info($"[Exec RX] {id}: the enemy is not in this scene, played it anyway");
            }
            catch (Exception e)
            {
                ModLog.Error("[Exec RX] riproduzione fallita: " + e.GetType().Name + " - " + e.Message);
            }
        }

        /// <summary>
        /// Registers the dead enemy's spawn point in the game's consumed list, keyed by position.
        ///
        /// This is the game's own bookkeeping: EnemySpawner keeps a List&lt;Vector3&gt; and clears it
        /// when the player rests at a bonfire (RespawnDeadEnemies), so the reset comes for free and
        /// stays consistent with single player. Idempotent, so repeated calls are harmless.
        ///
        /// The key is the POSITION, not the name: several spawn points can share a position, and the
        /// game treats those as the same spot - killing one consumes the other too. Base game
        /// behaviour, not something to fix.
        /// </summary>
        private static void MarkSpawnPointConsumed(string enemyId)
        {
            EnemySpawner spawner = Core.Logic != null ? Core.Logic.EnemySpawner : null;
            if (spawner == null || string.IsNullOrEmpty(enemyId) || !enemyId.StartsWith("sp|"))
                return;

            EnemySpawnPoint[] points = UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>();
            if (points.Length == 0)
                return;

            // Prefer the enemy's OWN transform: it is the exact position the game will compare
            // against, whereas the id string only carries two decimals and rebuilding a Vector3 from
            // it can miss by a fraction - and the game's list uses exact Vector3 equality.
            Vector3 position;
            Enemy tracked = ResolveEnemyById(enemyId);
            if (tracked != null)
            {
                position = tracked.transform.position;
            }
            else if (!TryParseSpawnPointPosition(enemyId, out position))
            {
                // Spawn point exists but the enemy is not here yet: fall back to the id position,
                // accurate to two decimals, enough to find the right spot.
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

            // Half a unit is far beyond any rounding error yet far below the spacing between distinct
            // spots.
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
        /// The ghost was: Dead set, no death animation, no deactivation - visible, idle, untargetable,
        /// the middle of a death sequence that never finished.
        ///
        /// IT NO LONGER DESTROYS THE GAMEOBJECT. Destroy() is the definitive end of an object and was
        /// the definitive end of the session too: the game keeps its own references to an enemy
        /// (Execution.ExecutedEntity, the prop of the execution interactable and its native state
        /// machine), and destroying the object invalidates them while the game still uses them.
        ///
        /// What that cost: StuntBehaviour.OnStateUpdate calls GetComponent&lt;EnemyBehaviour&gt;() on
        /// the executed enemy; with the object destroyed, the failure happens inside the native icall
        /// (GetComponentFastPath), and StuntBehaviour leaves its state through the LAST instruction
        /// of OnStateUpdate, a Destroy of the prop - which is skipped. The state is never left and
        /// OnStateUpdate runs every frame. Then it stops being a log problem: the execution coroutine
        /// is SetBlocker("EXECUTION", true) -> ... -> KillEntity() -> ... -> ShowPlayer -> SetBlocker
        /// ("EXECUTION", false). When KillEntity throws on the ghost, the exception leaves the
        /// coroutine and the coroutine DIES: the player is never shown again, IsOnExecution is never
        /// reset, and the blocker stays on - invisible penitent, every key dead, game closed by force.
        ///
        /// Deactivating instead keeps every reference valid while removing the enemy from the scene
        /// exactly as intended: not visible, not targetable, not colliding, not blocking. And it lets
        /// the game end its own sequence rather than having the object pulled out from under it.
        /// </summary>
        private static void DestroyDeadEnemy(Enemy enemy)
        {
            if (enemy == null)
                return;

            string id = IdOf(enemy);

            // Ask whether the death sequence started, not whether health is at zero: see
            // KillEnemyLocally for why Entity.Dead is the wrong question here. This is the half that
            // already did the right thing - the game's own damage entry point, so the flash, the
            // sound and the DEATH trigger all follow the normal flow.
            if (!enemy.Status.Dead)
                KillEnemyLocally(enemy, HandleOf(id));

            // The game may have already taken it down; either way it must not remain in the scene.
            //
            // NOT Destroy: see above. SetActive(false) is enough for everything the destroy was for,
            // and it is the difference between a session that continues and one that ends with the
            // player unable to press a key.
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

            // Drop the id so the dead enemy stops being broadcast, then run the native death sequence
            // if it has not already died locally.
            for (int i = 0; i < _ids.Count; i++)
            {
                if (HandleOf(_ids[i]) == handle)
                {
                    _enemies.Remove(_ids[i]);
                    break;
                }
            }

            // Status.Dead, not Entity.Dead: an enemy at zero health whose death has not yet started
            // must still receive this, because this is the only thing that will start it.
            if (enemy.Status.Dead)
                return;

            // Neutral damage: this is the mod settling an agreed death, not a blow somebody struck.
            ApplyDamage(enemy, enemy.CurrentLife + 1f, (byte)DamageArea.DamageType.Normal,
                        (byte)DamageArea.DamageElement.Normal, 0);
        }

        private static Enemy ResolveEnemy(string id)
        {
            // The spawn point is asked FIRST. The cache used to win, but the game can recreate an
            // enemy from its spawn point - on a scene reload, and probably when an enemy is
            // reactivated. The cache would keep pointing at the old instance, detached from the
            // scene, so everything read through it (position, life, behaviour) described an enemy
            // that no longer existed. The game's own reference is authoritative; the cache is only
            // a fallback for ids with no spawn point.
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

            // Logged so the relayed damage is MEASURABLE, which the health channel alone cannot do:
            // when a hit only WOUNDS, no trace of how much it took is left. That gap is what made
            // the worst regression invisible - an exception fired before health was subtracted, so
            // every relayed blow dealt ZERO and nothing in the log said so. One line closes it.
            ModLog.Info($"[Danno] relay {IdOf(enemy)} amount={damageAmount:F0} type={damageType} " +
                        $"elem={damageElement} flags={flags} vitaPrima={enemy.CurrentLife:F0}");

            Hit hit = new Hit
            {
                DamageAmount = damageAmount,

                // The damage type and element come from the hit that actually landed, so the
                // receiving machine picks the same reaction the attacker's machine did. They used
                // to be hard-coded to Normal, which is why every relayed blow sounded light and why
                // the enemy types that branch on the damage type always took the wrong branch.
                DamageType = (DamageArea.DamageType)damageType,
                DamageElement = (DamageArea.DamageElement)damageElement,
                DontSpawnBlood = (flags & HIT_FLAG_DONT_SPAWN_BLOOD) != 0,
                Unparriable = (flags & HIT_FLAG_UNPARRIABLE) != 0,
                Unblockable = (flags & HIT_FLAG_UNBLOCKABLE) != 0,

                // THE SOUND must be the full bank PATH, not the catalog key.
                //
                // This was tried with PenitentAudio.SimpleEnemyHitEventKey, which is
                // "PenitentSimpleEnemyHit", and it took the mod down: Entity.Damage calls
                // RuntimeManager.CreateInstance with whatever is in here, that call requires a bank
                // path, and the exception it threw fired BEFORE the health was subtracted. Every
                // relayed blow dealt ZERO, the exception escaped through OnLateUpdate - which has
                // try/finally and no catch - so ProcessQueue never reached its queue-clearing line
                // and the same packet was reprocessed forever. 2934 errors and a frozen mod, from
                // one string.
                //
                // Both paths below appear in the game's own audio log as events it plays, so they
                // are known-good bank paths. The catalog key would only resolve through
                // AudioManager.CreateCatalogEvent, a different door entirely.
                HitSoundId = ((DamageArea.DamageType)damageType) == DamageArea.DamageType.Heavy
                    ? "event:/SFX/Penitent/Damage/PenitentHeavyEnemyHit"
                    : "event:/SFX/Penitent/Damage/PenitentSimpleEnemyHit",

                AttackingEntity = Core.Logic.Penitent != null ? Core.Logic.Penitent.gameObject : null
            };

            // SPARKS AND BLOOD ARE THE ATTACKER'S, AND NOBODY REPLAYS THEM OTHERWISE.
            //
            // The hurt flash, the sound and (for many enemy types) the death trigger all live inside
            // <EnemyType>::Damage(Hit), which the call below reaches. Sparks and blood are different:
            // they are spawned by PenitentSword.Attack, which calls Weapon.GetDamageableEntities()
            // to find what the LOCAL sword overlapped, then GetSwordSparks, and only afterwards
            // applies the damage. On the machine that is not swinging, the sword never overlapped
            // anything.
            //
            // The replay happens HERE, on the relay path, and that placement is what keeps it from
            // doubling: this method only runs for damage that arrived from the network, and the
            // sender is excluded by the server.
            //
            // The position is not transmitted: the receiver holds the same enemy instance, so the
            // impact point is the same collider bounds centre the vanilla code would have used.
            ReplayHitEffects(enemy, hit);

            // Through the game's own damage entry point, not through the hurtbox.
            //
            // ((IDamageable)enemy).Damage(hit) is what Weapon.AttackDamageableEntities calls when
            // the sword connects, and it lands in <EnemyType>::Damage(Hit). That is where the hurt
            // flash (DamageEffectScript.Blink), the hurt sound (Behaviour.Damage) and - for the many
            // types that set their DEATH trigger there - the death animation are all driven from.
            //
            // EnemyDamageArea.TakeDamage, used before, is a lower layer: it applies the health change
            // and fires the damage events, but skips <EnemyType>::Damage entirely. That is why a hit
            // applied by this mod produced no visible reaction, and why some enemies could take a
            // killing hit from here and never start dying.
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
        /// Spawns the sparks and blood a hit would have produced, on a machine that did not swing.
        ///
        /// Mirrors what PenitentSword.GetSwordSparks does for each entity it overlapped: take the
        /// collider's bounds centre, then ask the ENEMY whether this kind of impact sparks and
        /// bleeds. Those two answers are per-prefab fields (sparksOnImpact / bleedOnImpact), which
        /// is exactly why "some enemies never show sparks" is correct behaviour rather than a bug.
        ///
        /// It deliberately does NOT call PenitentSword.GetSwordSparks(list, hit), which would be
        /// less code: that method needs the attacker's own entity list and would re-derive the same
        /// decisions. Going straight to SpawnSparks/SpawnBlood with the enemy checked here keeps the
        /// number of game members this mod reaches through to the minimum, and every one was
        /// verified public in IL. Everything is guarded: an exception inside the damage path is
        /// worse than a missing spark - it is exactly how the send loop lost a session earlier.
        /// </summary>
        private static void ReplayHitEffects(Enemy enemy, Hit hit)
        {
            try
            {
                Penitent penitent = Core.Logic.Penitent;
                if (penitent == null)
                    return; // dying, in a menu, or between levels

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

                // The sword owns the blood pool; without one there is nothing to reuse. Skipping is
                // the right answer: creating a second source of blood effects would be a new problem,
                // not a fix.
                if (sword != null)
                    sword.SpawnBlood(position, hit);
            }
            catch (Exception e)
            {
                _hitEffectErrors++;
                if (_hitEffectErrors <= 5)
                    ModLog.Error("[EnemyFX] effect replay failed (" + e.GetType().Name + "): " +
                                 e.Message + " - il danno e' stato applicato comunque");
            }
        }

        /// <summary>Failed effect replays. Diagnostics only: the damage itself is unaffected.</summary>
        private static int _hitEffectErrors;

        /// <summary>
        /// How many times the send loop had to skip an enemy because of an exception. Reported in the
        /// probe line, because the session that exposed this had 666 of them and the logs looked
        /// clean: the word "Exception" never appeared, and the error marker has two spaces before
        /// its colon, so "Error:" found nothing. A count that has to be zero, visible every two
        /// seconds, cannot hide like that.
        /// </summary>
        private static int _syncLoopExceptions;

        /// <summary>Exceptions skipped by the send loop, for the probe line. Diagnostics only.</summary>
        public static int SyncLoopExceptions { get { return _syncLoopExceptions; } }
    }
}