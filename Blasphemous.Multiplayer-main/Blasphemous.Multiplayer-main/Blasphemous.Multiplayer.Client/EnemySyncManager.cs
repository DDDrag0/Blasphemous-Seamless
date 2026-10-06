using System.Collections.Generic;
using UnityEngine;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Tools.Level.Layout;
using Framework.Managers;
using Blasphemous.ModdingAPI;

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
        }

        private static readonly Dictionary<string, EnemyState> _lastSent = new Dictionary<string, EnemyState>();

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

            ModLog.Error($"[EnemySync] Handle {handle} non trovato fra i {_ids.Count} id locali: le due macchine non hanno la stessa lista di nemici.");
            return null;
        }

        /// <summary>
        /// Per-enemy listing. Run "multiplayer sync" on both machines: the handles must be identical,
        /// because they are what travels on the wire.
        /// </summary>
        public static List<string> Dump()
        {
            List<string> lines = new List<string>();
            lines.Add($"enemies={_enemies.Count} spawnPoints={_spawnPoints.Count} ids={_ids.Count} fingerprint={_listFingerprint}");

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
            EnemySpawnPoint[] points = Object.FindObjectsOfType<EnemySpawnPoint>();
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
            if (_enemies.ContainsKey(id))
                return;

            _enemies.Add(id, enemy);
            _ids.Add(id);
            _registered.Add(enemy);
            _registrations++;
        }

        private static bool IsRegistered(Enemy enemy)
        {
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
            }

            // Broadcasting is the host's job only.
            if (!IsHost || !Main.Multiplayer.NetworkManager.IsConnected)
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

            // The wire handle is the numeric index into _ids, not the id string: a short name is ~40
            // bytes, which dwarfs the position and health it accompanies. The index is stable because
            // both machines build the same list from the same authored spawn points.
            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                uint handle = HandleOf(id);
                Enemy enemy = ResolveEnemy(id);

                if (enemy == null || enemy.gameObject == null)
                    continue; // not spawned yet, or already gone

                // Registered but not yet in play (a boss waiting for its cutscene). Sending no state is
                // correct: nothing is moving, and the peer applies everything as soon as it appears.
                if (!enemy.gameObject.activeInHierarchy)
                    continue;

                if (enemy.Dead || enemy.CurrentLife <= 0f)
                {
                    (dead ??= new List<string>()).Add(id);
                    _killsSent++;
                    Main.Multiplayer.SendEnemyKill(handle);
                    continue;
                }

                Vector2 position = enemy.transform.position;
                float life = enemy.CurrentLife;
                int animState;
                float animTime;
                ReadAnimation(enemy, out animState, out animTime);

                // Skip unchanged enemies to save bandwidth; the periodic full sweep above guarantees
                // that everything is still resent regularly. TryGetValue, never an indexer: a freshly
                // registered enemy has no previous record yet.
                EnemyState previous;
                bool hasPrevious = _lastSent.TryGetValue(id, out previous);

                if (!fullSweep && hasPrevious)
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

                _lastSent[id] = new EnemyState
                {
                    Position = position,
                    Life = life,
                    AnimState = animState,
                    LastSweep = Time.time
                };
                _updatesSent++;
                Main.Multiplayer.SendEnemyUpdate(handle, position, life, animState, animTime, _listFingerprint);
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

            if (enemy != null)
                _registered.Remove(enemy);
        }

        /// <summary>Position, health and animation state broadcast sent by the host.</summary>
        public static void ReceiveEnemyUpdate(uint handle, Vector2 position, float currentLife, int animState, float animTime, int fingerprint)
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

            // Peers do not simulate: the host is the single source of truth for enemy state.
            // PauseBehaviour is the game's own way to stop an AI; disabling the component is blunter
            // and can leave the movement controller running.
            if (!IsHost)
            {
                PauseEnemy(enemy);

                // PauseBehaviour only stops the NodeCanvas behaviour tree, not the Animator, so a peer
                // still animates idle/walk on its own but would never show an attack. Drive the state
                // from the host instead.
                ApplyAnimation(enemy, animState, animTime);
            }

            enemy.transform.position = position;
            enemy.CurrentLife = currentLife;
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
            if (_enemies.TryGetValue(id, out Enemy cached) && cached != null && cached.gameObject != null)
                return cached;

            // The enemy may have been (re)created by its spawn point since the last scan.
            if (!_spawnPoints.TryGetValue(id, out EnemySpawnPoint point) || point == null)
                return null;

            Enemy spawned = point.SpawnedEnemy;
            if (spawned == null)
                return null;

            _enemies[id] = spawned;
            return spawned;
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
