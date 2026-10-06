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

        private static readonly Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();
        private static readonly Dictionary<string, EnemySpawnPoint> _spawnPoints = new Dictionary<string, EnemySpawnPoint>();
        private static readonly List<string> _ids = new List<string>();

        private static bool _isHost;
        private static float _sendTimer;
        private static float _rescanTimer;

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
            _sendTimer = 0f;
            _rescanTimer = 0f;

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
                if (!_spawnPoints.ContainsKey(id))
                {
                    _spawnPoints.Add(id, point);
                    _ids.Add(id);
                }

                Enemy spawned = point.SpawnedEnemy;
                if (spawned != null && _enemies.ContainsKey(id) == false)
                    _enemies.Add(id, spawned);
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

            List<string> dead = null;

            for (int i = 0; i < _ids.Count; i++)
            {
                string id = _ids[i];
                Enemy enemy = ResolveEnemy(id);

                if (enemy == null || enemy.gameObject == null)
                    continue; // not spawned yet, or already gone

                if (enemy.Dead || enemy.CurrentLife <= 0f)
                {
                    (dead ??= new List<string>()).Add(id);
                    Main.Multiplayer.SendEnemyKill(id);
                    continue;
                }

                Vector2 position = enemy.transform.position;
                Main.Multiplayer.SendEnemyUpdate(id, position, enemy.CurrentLife);
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    _enemies.Remove(dead[i]);
            }
        }

        /// <summary>Position + health broadcast sent by the host.</summary>
        public static void ReceiveEnemyUpdate(string id, Vector2 position, float currentLife)
        {
            Enemy enemy = ResolveEnemy(id);
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
            }

            enemy.transform.position = position;
            enemy.CurrentLife = currentLife;
        }

        /// <summary>
        /// Damage approved by the host. The machine that swung the sword already applied it natively
        /// (that is the host), so this only ever runs on the peers.
        /// </summary>
        public static void ReceiveEnemyDamage(string id, float damageAmount)
        {
            Enemy enemy = ResolveEnemy(id);
            if (enemy == null)
                return;

            ApplyDamage(enemy, damageAmount);
        }

        /// <summary>Authoritative death, so peers always see the corpse and the drop.</summary>
        public static void ReceiveEnemyKill(string id)
        {
            Enemy enemy = ResolveEnemy(id);
            if (enemy == null)
                return;

            _enemies.Remove(id);

            if (enemy.Dead || enemy.CurrentLife <= 0f)
                return;

            ApplyDamage(enemy, enemy.CurrentLife + 1f);
        }

        /// <summary>
        /// Maps a damaged GameObject to its network id. Used by the damage patch to forward a local hit.
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
