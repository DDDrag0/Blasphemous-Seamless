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
        private static int _missingSpawnedEnemy;

        /// <summary>
        /// One-line health summary, surfaced by the "multiplayer sync" command. Built to be compared
        /// between the two machines: the tracked/spawn point counts must match on both.
        /// </summary>
        public static string Describe()
        {
            string warning = string.Empty;
            if (_duplicateIds > 0)
                warning += $" ATTENZIONE: {_duplicateIds} spawn point con identita' duplicata (id non univoci!)";
            if (_missingSpawnedEnemy > 0)
                warning += $" NOTA: {_missingSpawnedEnemy} nemici non ancora creati dallo spawn point";

            return string.Format(
                "scene={0} role={1} connected={2} spawnPoints={3} tracked={4} registered={5} | sent={6} recv={7} dmgSent={8} dmgApplied={9} killsSent={10}{11}",
                _scene,
                IsHost ? (Main.Multiplayer.NetworkManager.IsConnected ? "HOST" : "solo") : "peer",
                Main.Multiplayer.NetworkManager.IsConnected,
                _spawnPoints.Count,
                _enemies.Count,
                _registrations,
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

            // Promoted (typically because the previous host disconnected): take over simulation.
            // Without this the new host would keep broadcasting frozen puppet enemies.
            if (isHost && !wasHost)
            {
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
        }

        /// <summary>Called from Multiplayer.OnLevelLoaded.</summary>
        public static void LevelLoaded(string scene)
        {
            _enemies.Clear();
            _spawnPoints.Clear();
            _ids.Clear();
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
            _missingSpawnedEnemy = 0;

            ScanSpawnPoints(scene);

            ModLog.Info($"[EnemySync] Scene {scene}: tracking {_enemies.Count} enemies / {_spawnPoints.Count} spawn points (IsHost: {IsHost})");
        }

        /// <summary>
        /// Every spawn point is authored in the scene, so the id set is identical on both machines.
        /// Spawn points whose enemy is not created yet (or already consumed) are still registered.
        /// </summary>
        private static void ScanSpawnPoints(string scene)
        {
            EnemySpawnPoint[] points = Object.FindObjectsOfType<EnemySpawnPoint>();
            if (points == null)
                return;

            for (int i = 0; i < points.Length; i++)
            {
                EnemySpawnPoint point = points[i];
                if (point == null || point.gameObject == null)
                    continue;

                string id = BuildId(scene, point);
                if (_spawnPoints.ContainsKey(id))
                {
                    // Two spawn points sharing name and position would break the network id, so make it
                    // visible in the diagnostics instead of failing silently.
                    _duplicateIds++;
                }
                else
                {
                    _spawnPoints.Add(id, point);
                    _ids.Add(id);
                }

                Enemy spawned = point.SpawnedEnemy;
                if (spawned == null)
                {
                    _missingSpawnedEnemy++;
                }
                else if (!_enemies.ContainsKey(id))
                {
                    _enemies.Add(id, spawned);
                    _registrations++;
                }
            }
        }

        private static string BuildId(string scene, EnemySpawnPoint point)
        {
            Vector3 p = point.transform.position;
            return $"{scene}|{point.gameObject.name}|{p.x:F2}|{p.y:F2}";
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
                ScanSpawnPoints(Core.LevelManager.currentLevel != null ? Core.LevelManager.currentLevel.LevelName : string.Empty);
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
                Main.Multiplayer.SendEnemyUpdate(handle, position, life, animState, animTime);
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    _enemies.Remove(dead[i]);
            }
        }

        /// <summary>Position, health and animation state broadcast sent by the host.</summary>
        public static void ReceiveEnemyUpdate(ushort index, Vector2 position, float currentLife, int animState, float animTime)
        {
            _updatesReceived++;

            Enemy enemy = ResolveByIndex(index);
            if (enemy == null)
                return;

            // Peers do not simulate: the host is the single source of truth for enemy state.
            // PauseBehaviour is the game's own way to stop an AI; disabling the component is blunter
            // and can leave the movement controller running.
            if (!IsHost)
            {
                var behaviour = enemy.EnemyBehaviour;
                if (behaviour != null && behaviour.enabled)
                {
                    behaviour.PauseBehaviour();
                    behaviour.enabled = false;
                }

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
        /// (that is the host), so this only ever runs on the peers.
        /// </summary>
        public static void ReceiveEnemyDamage(ushort index, float damageAmount)
        {
            Enemy enemy = ResolveByIndex(index);
            if (enemy == null)
                return;

            _damageApplied++;
            ApplyDamage(enemy, damageAmount);
        }

        /// <summary>Called by the damage patch when a local hit is forwarded instead of applied.</summary>
        public static void RecordDamageSent()
        {
            _damageSent++;
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
