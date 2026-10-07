using System.Collections.Generic;
using UnityEngine;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Tools.Level.Layout;
using Framework.Managers;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Gameplay.GameControllers.Enemies.Framework.Persistence;
using Framework.FrameworkCore;
using CreativeSpore.SmartColliders;
using Framework.Managers;
using System;
using System.Globalization;

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
        /// Enemies already reported as dead by the diagnostic snapshot, so each death is logged once.
        ///
        /// The kill detection used to live only inside the send loop, which returns early when there is no
        /// connection - so a single-player test, exactly where the game's own behaviour is easiest to study,
        /// produced no death records at all.
        /// </summary>
        private static readonly HashSet<string> _killLogged = new HashSet<string>();

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
        private static int _duplicateIds;
        private static int _listFingerprint;
        private static int _fingerprintMismatchReported;
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

                    var behaviour = enemy.EnemyBehaviour;
                    if (behaviour == null || behaviour.enabled)
                        continue;

                    behaviour.enabled = true;
                    behaviour.StartBehaviour();
                }
            }
            else if (!isHost && wasHost)
            {
                // Demoted: stop simulating, otherwise the AI keeps running locally and fights the state
                // the real host is broadcasting.
                for (int i = 0; i < _ids.Count; i++)
                {
                    Enemy enemy = ResolveEnemy(_ids[i]);
                    if (enemy == null)
                        continue;

                    PauseEnemy(enemy);
                }
            }
        }

        /// <summary>
        /// Hands an enemy over to the host: pause the behaviour tree and disable the component, which is
        /// the same treatment a peer applies to enemies it receives updates for.
        /// </summary>
        private static void PauseEnemy(Enemy enemy)
        {
            var behaviour = enemy.EnemyBehaviour;
            if (behaviour == null || !behaviour.enabled)
                return;

            behaviour.PauseBehaviour();
            behaviour.enabled = false;
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
        private static void ResumeEnemy(Enemy enemy)
        {
            var behaviour = enemy.EnemyBehaviour;
            if (behaviour == null || behaviour.enabled)
                return;

            behaviour.enabled = true;
            behaviour.StartBehaviour();
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
            _killLogged.Clear();

            _sendTimer = 0f;
            _rescanTimer = 0f;
            _lastSweep = 0f;

            _updatesSent = 0;
            _updatesReceived = 0;
            _damageSent = 0;
            _damageApplied = 0;
            _killsSent = 0;
            _registrations = 0;
            _duplicateIds = 0;
            _listFingerprint = 0;
            _fingerprintMismatchReported = 0;
            _countedDuplicates.Clear();

            ModLog.Info($"[EnemySync] Scene {scene}: level loaded (IsHost: {IsHost})");
        }

        /// <summary>
        /// Cheap order-sensitive hash of the id list. Both machines must produce the same value, because
        /// the index into this list is the wire handle: if the lists differ, updates get applied to the
        /// wrong enemy. The host sends it with every update so a divergence is reported instead of
        /// silently corrupting the scene.
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
            _spawnFinalized = true;

            ModLog.Info($"[EnemySync] Post-spawn: {_enemies.Count} enemies / {_spawnPoints.Count} spawn points / {_ids.Count} ids");
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

            _enemyIds[enemy] = id;
            _enemies.Add(id, enemy);
            _ids.Add(id);
            _registered.Add(enemy);
            _registrations++;

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
            return $"sp|{scene}|{point.gameObject.name}|{p.x:F2}|{p.y:F2}";
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
            string name = StripInstanceHash(enemy.gameObject.name);
            Vector3 p = enemy.transform.position;
            return $"en|{scene}|{name}|{p.x:F0}|{p.y:F0}";
        }

        private static string StripInstanceHash(string name)
        {
            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1]))
                end--;

            // Only strip when digits were appended to something, never when the name is all digits.
            if (end == name.Length || end == 0)
                return name;

            return name.Substring(0, end);
        }

        /// <summary>
        /// True when THIS machine's player is the closest to the enemy, so it should simulate it.
        ///
        /// Proximity replaces aggro, because aggro simply is not available. Two attempts to read it both
        /// failed for the same underlying reason: each machine simulates its own copy of the enemy, and
        /// that copy chases its own player. EnemyBehaviour.GetTarget() falls back to Core.Logic.Penitent
        /// (the LOCAL player) and Enemy.Target is always set to that same local player, so every machine
        /// truthfully reports "it is chasing me". There is no field that can express "chasing the other
        /// player", because in the game's own model the other player does not exist.
        ///
        /// Distance is the workable substitute: every machine can compute it from data it already has (its
        /// own position plus the synced position of the other player), and both reach the same conclusion
        /// without exchanging anything. It also matches the gameplay - whoever is next to an enemy is
        /// whoever is fighting it.
        /// </summary>
        private static bool ShouldOwnByProximity(Enemy enemy)
        {
            Penitent local = Core.Logic.Penitent;
            if (local == null)
                return true; // no local player yet: keep it, so an enemy is never left unowned

            Vector2 enemyPos = enemy.transform.position;
            float localDistance = Vector2.Distance(enemyPos, local.transform.position);

            float nearestRemote = float.MaxValue;
            foreach (Vector2 remote in _remotePositions.Values)
            {
                float d = Vector2.Distance(enemyPos, remote);
                if (d < nearestRemote)
                    nearestRemote = d;
            }

            // No remote position known yet (still connecting, or the other player is in another scene, in
            // which case their packet never arrives): nobody can be closer, so keep the enemy here rather
            // than handing it to a player we cannot even locate.
            if (nearestRemote == float.MaxValue)
                return true;

            // A remote player has to be MEANINGFULLY closer, not a hair closer. Positions arrive about
            // 20 times a second, so a bare comparison would flip the owner back and forth while the two
            // players stand near each other.
            return localDistance <= nearestRemote + OWNER_DISTANCE_MARGIN;
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

                ModLog.Info($"[EnemyTick] {ShortId(id)} owner={owner} remoto={remote} " +
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
        }

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
        /// The game itself never calls it (verified: zero callers in both assemblies), so it is a public
        /// library API meant for exactly this and it disturbs nothing.
        ///
        /// Falls back to the transform when there is no controller (an enemy not yet initialised), because a
        /// wrong-but-visible position is better than a skipped update.
        /// </summary>
        private static void PlaceEnemy(Enemy enemy, Vector3 position)
        {
            PlatformCharacterController controller = enemy.Controller;
            if (controller == null)
            {
                enemy.transform.position = position;
                return;
            }

            PlatformCharacterPhysics physics = controller.PlatformCharacterPhysics;
            if (physics == null)
            {
                enemy.transform.position = position;
                controller.ResetPreviousPosition();
                return;
            }

            physics.ResetPreviousPosition(position);
            controller.ResetPreviousPosition();
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

        /// <summary>Short, readable form of an id for logs: the spawn point name and position.</summary>
        private static string ShortId(string id)
        {
            string[] parts = id.Split('|');
            if (parts.Length >= 2)
                return parts[1] + (parts.Length >= 3 ? "@" + parts[2] : string.Empty);

            return id;
        }

        /// <summary>
        /// Decides whether this machine should be the one simulating an enemy, and reports whether the
        /// role changed.
        ///
        /// The rule, deliberately sticky:
        ///   - a machine whose player is being chased wants the enemy, because running the AI there is
        ///     what produces real attacks and deaths;
        ///   - otherwise the enemy stays where it is, and if it has no owner yet the host takes it, so
        ///     an idle enemy is never left with nobody simulating it;
        ///   - the role only moves after OWNER_HOLD_SECONDS, which is what stops the two machines from
        ///     trading the same enemy many times per second.
        ///
        /// The host is the arbiter: it applies this decision and tells the peer, rather than both sides
        /// deciding independently and possibly disagreeing.
        /// </summary>
        private static bool DecideOwnership(Enemy enemy, EnemyState state, bool localIsHost)
        {
            bool wants = ShouldOwnByProximity(enemy);
            bool changed = false;

            if (state.OwnerSince <= 0f)
            {
                // First time this enemy is considered: the host takes it, so an enemy that nobody is
                // chasing is never left with no machine simulating it. A peer only claims it if the
                // enemy is already chasing the peer's player.
                state.OwnedByLocal = localIsHost || wants;
                state.OwnerSince = Time.time;
                return false;
            }

            // The host arbitrates. It knows its own aggro directly, and the peer's claim from the
            // ownership flag in the peer's updates - without that second input the host would never
            // release an enemy the peer is already simulating.
            if (localIsHost)
            {
                bool peerWants = state.RemoteEverClaimed && !wants;

                // Take it when this machine is being chased; give it up when the peer is the one being
                // chased. Neither case applies for an enemy nobody is chasing, which stays here.
                if (wants && !state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS)
                {
                    state.OwnedByLocal = true;
                    state.OwnerSince = Time.time;
                    changed = true;
                }
                else if (peerWants && state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS)
                {
                    state.OwnedByLocal = false;
                    state.OwnerSince = Time.time;
                    changed = true;
                }
            }
            else if (wants && !state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS)
            {
                // The peer asks for an enemy that is chasing it.
                state.OwnedByLocal = true;
                state.OwnerSince = Time.time;
                changed = true;
            }

            return changed;
        }

        /// <summary>Called from Multiplayer.OnLateUpdate, on every machine.</summary>
        public static void Update()
        {
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
                else if (IsHost)
                {
                    // Only append later discoveries, and only on the host: the peer must not invent ids
                    // the host does not have, or the indices would stop meaning the same thing.
                    RegisterEnemies(_scene);
                }

                // Snapshot on BOTH machines, so the two logs can be lined up: same enemy, same moment,
                // what does each side think the aggro, ownership, animation and life are.
                LogDiagnosticSnapshot();
            }

            // Follow the remote snapshots smoothly. Runs before the send gate below, because that gate
            // returns early on most frames and the easing has to happen every frame to look smooth.
            InterpolateRemoteEnemies();

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
                    continue;
                }

                // Registered but not yet in play (a boss waiting for its cutscene), or simply out of view.
                // Sending no state is correct: nothing is moving, and the peer applies everything as soon as
                // it appears.
                if (!enemy.gameObject.activeInHierarchy)
                    continue;

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

                // Decide who simulates this enemy. Only the host arbitrates; the peer's request is folded
                // into the same rule inside DecideOwnership.
                bool ownershipChanged = DecideOwnership(enemy, previous, IsHost);

                // "Owned here" means this machine runs the game's own AI for this enemy, which is what
                // produces real attacks and deaths. The other machine pauses its AI and applies state.
                if (previous.OwnedByLocal)
                    ResumeEnemy(enemy);
                else
                    PauseEnemy(enemy);

                if (!fullSweep && hasPrevious && !ownershipChanged)
                {
                    bool changed = Mathf.Abs(previous.Position.x - position.x) > POSITION_EPSILON ||
                                   Mathf.Abs(previous.Position.y - position.y) > POSITION_EPSILON ||
                                   Mathf.Abs(previous.Life - life) > LIFE_EPSILON ||
                                   previous.AnimState != animState;

                    if (!changed)
                        continue;
                }

                if (!hasPrevious || previous.AnimState != animState)
                    animTime = 0f; // Only pass a playback position when the state actually changes.

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
            // wrong enemy. Report it once, with enough detail to diagnose it.
            if (fingerprint != _listFingerprint && _fingerprintMismatchReported == 0)
            {
                _fingerprintMismatchReported = 1;
                ModLog.Error($"[EnemySync] IMPRONTA DIVERSA: host={fingerprint} locale={_listFingerprint} " +
                             $"- le due macchine hanno liste di nemici diverse, gli aggiornamenti finiranno " +
                             $"sul nemico sbagliato. Confronta 'multiplayer sync dump' sui due PC " +
                             $"(spawn point locali: {_spawnPoints.Count}, id locali: {_ids.Count}).");
            }

            Enemy enemy = ResolveByHandle(handle);
            if (enemy == null)
                return;

            // Record the sender's claim. This is the input the arbitrating host was missing: without it
            // the host never learned that the peer was simulating an enemy, so it kept simulating too and
            // the two positions fought each other.
            if (FindIdOfHandle(handle, out string claimedId))
            {
                EnemyState claimState = GetOrCreateState(claimedId);
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
            if (ownedRemotely)
            {
                PauseEnemy(enemy);

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
                ResumeEnemy(enemy);
            }

            enemy.CurrentLife = currentLife;

            // Record the snapshot as a TARGET. The position is applied smoothly in Update, once per frame,
            // instead of being snapped here 20 times a second.
            if (FindIdOfHandle(handle, out string targetId))
                _remoteTargets[targetId] = position;

            // Death is treated as a CONSEQUENCE OF HEALTH, not only as its own packet. The death packet
            // proved unreliable in game - the peer kept showing a frozen enemy with full life because the
            // kill never landed - and health already travels in every update, so a zero (or negative,
            // which is what "life + 1" produces) can be acted on immediately and cannot be missed.
            if (!IsHost && (enemy.Dead || currentLife <= 0f))
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
        private static void KillEnemyLocally(Enemy enemy, uint handle)
        {
            if (enemy == null)
                return;

            if (!enemy.Dead)
            {
                // DamageThroughNativePath so the hurt/death animation and the drop follow the normal flow.
                ApplyDamage(enemy, enemy.CurrentLife + 1f);
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

                // A state we applied ourselves is not a local change: reporting it would describe our own
                // forcing as if the game had animated something, and that is what produced hundreds of
                // transitions for a single event.
                int applied;
                if (_appliedAnim.TryGetValue(id, out applied) && applied == stateHash)
                {
                    _lastAnim[id] = stateHash;
                    continue;
                }

                _lastAnim[id] = stateHash;
                if (known && previous == stateHash)
                    continue;

                Vector3 p = enemy.transform.position;
                ModLog.Info($"[EnemyAnim] {ShortId(id)} {previous} -> {stateHash} " +
                            $"avvio={animTime:F2} vita={enemy.CurrentLife:F0} " +
                            $"pos=({p.x:F1},{p.y:F1}) " +
                            $"mia={(IsHost ? "host" : "client")}");
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

            enemy.SetOrientation(wanted, true, true);
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

            if (normalizedTime > 0f && normalizedTime < 1f)
                animator.Play(stateHash, 0, normalizedTime);
            else
                animator.Play(stateHash, 0, 0f);
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
        public static void ReceiveEnemyDamage(uint handle, float damageAmount)
        {
            Enemy enemy = ResolveByHandle(handle);
            if (enemy == null)
                return;

            // If the peer that swung also landed the killing blow, its local application already killed
            // the enemy here. Applying the relayed damage to that corpse would run the death sequence a
            // second time (double purge/tears credit). The host's next update resyncs the exact health,
            // so skipping is safe.
            if (enemy.Dead || enemy.CurrentLife <= 0f)
                return;

            _damageApplied++;
            ApplyDamage(enemy, damageAmount);
        }

        /// <summary>Called by the damage patch when a local hit is forwarded instead of applied.</summary>
        public static void RecordDamageSent()
        {
            _damageSent++;
        }

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
        /// finished. Destroying the GameObject is the definitive end of it.
        /// </summary>
        private static void DestroyDeadEnemy(Enemy enemy)
        {
            if (enemy == null)
                return;

            if (!enemy.Dead)
                KillEnemyLocally(enemy, HandleOf(IdOf(enemy)));

            // The game may have already deactivated it; either way it must not remain in the scene.
            if (enemy != null && enemy.gameObject != null)
                UnityEngine.Object.Destroy(enemy.gameObject);
        }


        /// <summary>Authoritative death, so peers always see the corpse and the drop.</summary>
        public static void ReceiveEnemyKill(uint handle)
        {
            Enemy enemy = ResolveByHandle(handle);
            if (enemy == null)
                return;

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

            if (enemy.Dead || enemy.CurrentLife <= 0f)
                return;

            ApplyDamage(enemy, enemy.CurrentLife + 1f);
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
        private static void ApplyDamage(Enemy enemy, float damageAmount)
        {
            if (enemy.EntityDamageArea == null)
                return;

            Hit hit = new Hit
            {
                DamageAmount = damageAmount,
                DamageType = DamageArea.DamageType.Normal,
                DamageElement = DamageArea.DamageElement.Normal,
                AttackingEntity = Core.Logic.Penitent != null ? Core.Logic.Penitent.gameObject : null
            };

            // The damage patch sees this call; the flag tells it the hit is already authorised.
            IsApplyingRemoteDamage = true;
            try
            {
                enemy.EntityDamageArea.TakeDamage(hit, true);
            }
            finally
            {
                IsApplyingRemoteDamage = false;
            }
        }
    }
}
