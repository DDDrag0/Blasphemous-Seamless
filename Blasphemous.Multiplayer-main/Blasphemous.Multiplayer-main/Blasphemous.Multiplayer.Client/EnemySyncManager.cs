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

        // Last broadcast state, used to skip enemies that did not change.
        private static readonly Dictionary<string, Vector2> _lastSentPos = new Dictionary<string, Vector2>();
        private static readonly Dictionary<string, float> _lastSentLife = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> _lastSentAnim = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> _lastFullSend = new Dictionary<string, float>();

        private static bool _isHost;
        private static float _sendTimer;
        private static float _rescanTimer;
        private static float _lastSweep;

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
        /// Maps a damaged GameObject to its index in the shared id list. The index is the wire handle:
        /// it is derived from spawn point name and position, which are authored in the scene, so both
        /// machines build the exact same list and therefore the same indices.
        /// </summary>
        public static bool TryGetEnemyIndex(Enemy enemy, out ushort index)
        {
            index = 0;
            if (enemy == null)
                return false;

            for (int i = 0; i < _ids.Count; i++)
            {
                Enemy tracked = ResolveEnemy(_ids[i]);
                if (tracked == null)
                    continue;

                if (tracked == enemy || enemy.transform.IsChildOf(tracked.transform))
                {
                    if (i > ushort.MaxValue)
                        return false;

                    index = (ushort)i;
                    return true;
                }
            }

            return false;
        }

        private static Enemy ResolveByIndex(ushort index)
        {
            if (index >= _ids.Count)
            {
                ModLog.Error($"[EnemySync] Indice nemico {index} fuori intervallo (max {_ids.Count - 1}): le due macchine non hanno la stessa lista di nemici.");
                return null;
            }

            return ResolveEnemy(_ids[index]);
        }

        /// <summary>
        /// Per-enemy listing. Run "multiplayer sync dump" on both machines: the id column is the
        /// network identity and must be identical (same order too, since it is built the same way).
        /// </summary>
        public static List<string> Dump()
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                Enemy enemy = ResolveEnemy(id);

                if (enemy == null)
                {
                    lines.Add(string.Format("[{0,3}] {1} -> non ancora spawnato", i, id));
                    continue;
                }

                lines.Add(string.Format(
                    "[{0,3}] {1} -> alive={2} dead={3} life={4:F1} pos=({5:F2},{6:F2})",
                    i, id, enemy.gameObject.activeSelf, enemy.Dead, enemy.CurrentLife,
                    enemy.transform.position.x, enemy.transform.position.y));
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

        /// <summary>Called from Multiplayer.OnLevelLoaded.</summary>
        public static void LevelLoaded(string scene)
        {
            _enemies.Clear();
            _spawnPoints.Clear();
            _ids.Clear();
            _registered.Clear();
            _lastSentPos.Clear();
            _lastSentLife.Clear();
            _lastSentAnim.Clear();
            _lastFullSend.Clear();
            _sendTimer = 0f;
            _rescanTimer = 0f;
            _lastSweep = 0f;

            _scene = scene;
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

            // At this point the game has not spawned anything yet (this runs from OnLevelLoaded, before
            // EnemySpawner.SpawnEnemiesOnLoad), so this normally only picks up spawn points that survive
            // from a previous state. The real registration happens in OnEnemiesSpawned.
            RegisterEnemies(scene);

            // Sort once here and only here for the ids known so far; later discoveries are appended by
            // OnEnemiesSpawned, which sorts and re-fingerprints again. _ids is the wire handle table, so
            // its order must stay frozen for the lifetime of the scene.
            _ids.Sort(string.CompareOrdinal);
            _listFingerprint = ComputeFingerprint();

            ModLog.Info($"[EnemySync] Scene {scene}: level loaded, {_enemies.Count} enemies / {_spawnPoints.Count} spawn points so far (IsHost: {IsHost})");
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
        /// Called right after the game spawns enemies (EnemySpawner.SpawnEnemiesOnLoad), which is the
        /// first moment enemies actually exist. Anything registered here gets sorted and fingerprinted
        /// immediately, so the id table is complete before any packet is sent.
        /// </summary>
        public static void OnEnemiesSpawned()
        {
            RegisterEnemies(_scene);

            _ids.Sort(string.CompareOrdinal);
            _listFingerprint = ComputeFingerprint();

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

        /// <summary>Called from Multiplayer.OnLateUpdate.</summary>
        public static void Update()
        {
            if (!IsHost || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            _rescanTimer += Time.deltaTime;
            if (_rescanTimer >= RESCAN_INTERVAL)
            {
                _rescanTimer = 0f;
                RegisterEnemies(Core.LevelManager.currentLevel != null ? Core.LevelManager.currentLevel.LevelName : string.Empty);
            }

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
                ushort handle = (ushort)i;
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
                // that everything is still resent regularly.
                if (!fullSweep && _lastSentPos.TryGetValue(id, out Vector2 prevPos))
                {
                    bool changed = Mathf.Abs(prevPos.x - position.x) > POSITION_EPSILON ||
                                   Mathf.Abs(prevPos.y - position.y) > POSITION_EPSILON ||
                                   Mathf.Abs(_lastSentLife[id] - life) > LIFE_EPSILON ||
                                   _lastSentAnim[id] != animState;

                    if (!changed)
                        continue;
                }

                if (_lastSentAnim[id] != animState)
                    animTime = 0f; // Only pass a playback position when the state actually changes.

                _lastSentPos[id] = position;
                _lastSentLife[id] = life;
                _lastSentAnim[id] = animState;
                _updatesSent++;
                Main.Multiplayer.SendEnemyUpdate(handle, position, life, animState, animTime, _listFingerprint);
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    _enemies.Remove(dead[i]);
            }
        }

        /// <summary>Position, health and animation state broadcast sent by the host.</summary>
        public static void ReceiveEnemyUpdate(ushort index, Vector2 position, float currentLife, int animState, float animTime, int fingerprint)
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

            Enemy enemy = ResolveByIndex(index);
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
        public static void ReceiveEnemyDamage(ushort index, float damageAmount)
        {
            Enemy enemy = ResolveByIndex(index);
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
        public static void ReceiveEnemyKill(ushort index)
        {
            Enemy enemy = ResolveByIndex(index);
            if (enemy == null)
                return;

            _enemies.Remove(index < _ids.Count ? _ids[index] : null);

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
