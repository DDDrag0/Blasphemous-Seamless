using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Enemies.Framework.IA;
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
            public bool RemoteEverClaimed;        }

        /// <summary>
        /// Minimum time an owner keeps an enemy before the role can move again.
        ///
        /// Aggro flips whenever the target enters or leaves sight, so without a hold two machines can
        /// hand the same enemy back and forth many times per second, and with ~100 ms of latency the
        /// enemy visibly jumps between two positions. Holding makes that rare instead of constant.
        /// </summary>
        private const float OWNER_HOLD_SECONDS = 0.5f;

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

            // TEMPORARY (diagnostics): record whether this call actually restarted the tree.
            //
            // The asymmetry below is the suspect: PauseEnemy always calls PauseBehaviour, while this method
            // returns early when the component is already enabled - and with the component disabled
            // EnemyBehaviour.Update does not run, so the only thing that can call StartBehaviour is this
            // method. If "earlyReturn" shows up while an enemy stands still next to a player, the pause is
            // permanent and this is why. Logged only when the outcome changes, because this runs 20 times a
            // second. Remove once the question is answered.
            if (behaviour.enabled)
            {
                LogTreeState(enemy, behaviour, "resume:earlyReturn");
                return;
            }

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
            string key = enabled + "|" + running;

            string previous;
            if (_treeStateLogged.TryGetValue(id, out previous) && previous == key)
                return;

            _treeStateLogged[id] = key;

            Vector3 p = enemy.transform.position;
            ModLog.Info($"[EnemyTree] {FullId(id)} enabled={enabled} isRunning={running} esito={esito} " +
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
            _remoteTargets.Clear();
            _lastAnim.Clear(); // animation state belongs to the scene being unloaded
            _appliedAnim.Clear();
            _appliedAnimPrev.Clear();    // ditto: keyed by id, and ids do not survive a scene change
            _lastAnimSample.Clear();     // ditto
            _lastAnimChangeAt.Clear();   // ditto: the clock belongs to the scene being unloaded
            _treeStateLogged.Clear();    // diagnostics: keyed by id, meaningless in the next scene
            _frozenGravityScale.Clear(); // keyed by instance id: meaningless once the objects are gone
            _bossIds.Clear();            // keyed by id, and ids do not survive a scene change
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

            RegisterEnemies(_scene);

            _ids.Sort(string.CompareOrdinal);
            _listFingerprint = ComputeFingerprint();
            _idsDirty = false;
            _spawnFinalized = true;

            ModLog.Info($"[EnemySync] Post-spawn: {_enemies.Count} enemies / {_spawnPoints.Count} spawn points / {_ids.Count} ids");
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
            _pendingDeaths.Clear();
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
                MarkSpawnPointConsumed(id);
                DestroyDeadEnemy(enemy);
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

                Vector2 next;
                if (distance > FOLLOW_SNAP_DISTANCE)
                    next = target; // a real jump (spawn, knockback): do not smear it out
                else
                    next = Vector2.Lerp(flat, target, FOLLOW_LERP);

                PlaceEnemy(enemy, new Vector3(next.x, next.y, current.z));
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

            ModLog.Info($"[EnemyJoin] annunciate {ids.Count} morti precedenti al nuovo giocatore " +
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
        private static bool DecideOwnership(Enemy enemy, EnemyState state, bool localIsHost)
        {
            bool wants = ShouldOwnByProximity(enemy);
            bool peerCloser = PeerIsCloser(enemy);

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
                state.OwnedByLocal = wants || (!peerCloser && localIsHost);
                state.OwnerSince = Time.time;

                // THE OTHER HALF OF VOICE 51: this first-take was missing the position adopt. It is the
                // common case - a player walks into a room and takes the enemies on first sight - and its
                // local copies are at the spawn point while the enemy was actually moved by the previous
                // owner. Without this, the newly-owning machine started simulating from spawn and broadcast
                // that back, which is the exact "slide to spawn" the other player saw.
                //
                // _remoteTargets[id] holds the last position the previous owner transmitted, and it keeps
                // being written by the receive path regardless of who owns, so it is the truth at this
                // moment. If the record does not exist yet (the enemy was never seen), the adopt is skipped
                // and the local copy stands - there is no better answer.
                if (state.OwnedByLocal)
                    AdoptLastKnownPosition(enemy, IdOf(enemy));
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
            if (PlayerDeathSync.IsLocalPlayerDead() && state.OwnedByLocal)
            {
                state.OwnedByLocal = false;
                state.OwnerSince = Time.time;
                return true;
            }

            bool changed = false;

            // Take it when this machine's player is meaningfully closer; give it up when the other player
            // is. Both use the margin, so while the two distances are comparable neither applies and the
            // owner simply stays - which is what keeps a contested enemy from flapping between the two.
            //
            // The handover used to require RemoteEverClaimed, and that is the deadlock this fixes: a peer
            // only transmits the enemies it owns, so it could never claim the enemy it was trying to take.
            if (wants && !state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS)
            {
                state.OwnedByLocal = true;
                state.OwnerSince = Time.time;
                AdoptLastKnownPosition(enemy, IdOf(enemy));
                changed = true;
            }
            else if (peerCloser && state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS)
            {
                state.OwnedByLocal = false;
                state.OwnerSince = Time.time;
                changed = true;
            }

            return changed;
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
                return;

            state.OwnedByLocal = ownedByMe;
            state.OwnerSince = Time.time;

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
                        DecideOwnership(enemy, previous, true);

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
                    }
                    else if (!IsHost && !previous.OwnershipKnown)
                    {
                        previous.OwnedByLocal = true;
                        AdoptLastKnownPosition(enemy, id);
                        ResumeEnemy(enemy);
                    }
                    else
                    {
                        PauseEnemy(enemy);
                    }

                    // The peer obeys the host, so it transmits exactly the enemies the host granted it.
                    if (!previous.OwnedByLocal)
                        continue;

                    // Position and life are transmitted only for an enemy that is actually in play.
                    //
                    // The token above is published for EVERY tracked enemy, including one that is currently
                    // deactivated, and that order is deliberate. The game never deactivates an enemy for being
                    // off camera (verified: every SetActive(false) on an enemy belongs to a death, an execution
                    // or a dispose), so this gate only catches enemies that are not in play yet - a boss waiting
                    // for its cutscene, or one spawned with SpawnEnabledEnemy false. Publishing the token first
                    // means the peer still learns who owns each enemy, instead of being left without an owner
                    // and freezing that enemy for the rest of the scene.
                    if (!enemy.gameObject.activeInHierarchy)
                        continue;

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

            // Record the snapshot as a TARGET. The position is applied smoothly in Update, once per frame,
            // instead of being snapped here 20 times a second.
            if (FindIdOfHandle(handle, out string targetId))
                _remoteTargets[targetId] = position;

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
