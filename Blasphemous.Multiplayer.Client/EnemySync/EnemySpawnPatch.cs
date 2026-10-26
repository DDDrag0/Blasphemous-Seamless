using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Blasphemous.ModdingAPI;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Hooks <c>EnemySpawner.SpawnEnemiesOnLoad</c> so enemy registration runs right after the game
    /// actually spawns enemies. <c>Multiplayer.OnLevelLoaded</c> fires before spawns happen, so a
    /// scan there finds nothing.
    ///
    /// Enemy identity is derived from the enemy's own name plus its spawn position, both from
    /// scene data and therefore identical on every machine. The game's <c>Enemy.SpawningId</c>
    /// cannot be used because <c>EnemySpawnPoint.Awake</c> sets it to <c>gameObject.GetHashCode()</c>,
    /// a per-process instance id.
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.SpawnEnemiesOnLoad))]
    public class EnemySpawnPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            EnemySyncManager.OnEnemiesSpawned();
        }
    }

    /// <summary>
    /// Hooks <c>EnemySpawner.RespawnDeadEnemies</c> (the bonfire's respawn) to drop the mod's memory
    /// of announced deaths exactly when the game itself decides those enemies are alive again.
    /// RespawnDeadEnemies clears the game's consumed list and recreates every enemy; without this
    /// hook the mod's pending list survives and <c>RegisterEnemy</c> re-kills the freshly respawned
    /// enemies on the same load. The list must not be dropped on every level load because it lets
    /// a death announced from another room survive until the enemy appears; hooking this method
    /// covers the bonfire's first activation, its re-activation and the penitence level load.
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.RespawnDeadEnemies))]
    public class EnemyRespawnPatch
    {
        /// <summary>
        /// Diagnostic line written on every respawn so the hook is observable even when there is
        /// nothing to forget: the handler only logs when its lists are non-empty, so a bonfire with
        /// no remembered deaths would otherwise produce no line at all. The scene travels with it
        /// because the bonfire only respawns what belongs to its own room.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            string scene = Core.LevelManager.currentLevel == null
                ? "?"
                : Core.LevelManager.currentLevel.LevelName;

            ModLog.Info($"[Falo''] RespawnDeadEnemies: scena={scene}");

            EnemySyncManager.OnEnemiesRespawned();
        }
    }

    /// <summary>
    /// Hooks <c>EnemySpawner.Reset</c>, the other place the game forgets its consumed-spawn list
    /// (callers: player death, save load, menu reset; none is an ordinary level load). Without
    /// this hook the mod remembered having announced deaths the game no longer considered dead,
    /// so the next joiner was told to kill enemies that were visibly standing.
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.Reset))]
    public class EnemySpawnResetPatch
    {
        /// <summary>
        /// Suppresses the consumed-list wipe while a player death is being processed: on a death
        /// the game clears the list locally, with no network line, so one player respawning at a
        /// bonfire would silently resurrect every enemy they had killed while the other machine
        /// kept them dead. Skipping the spawn pass in <c>SpawnEnemiesOnLoad</c> would not work,
        /// because Reset has already emptied the list by then. The
        /// <c>PersistentManager.RestoreStored</c> refill is left alone so the player still recovers
        /// health, flasks and fervour. Only the death caller is affected; save load and menu reset
        /// must keep working because the consumed list is not serialised. The postfix still runs
        /// when this returns false (Harmony always executes postfixes), which is why the mod's own
        /// memory wipe is guarded by the same flag.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!PlayerDeathSync.SuppressingWorldReset)
                return true;

            ModLog.Info("[Morte] EnemySpawner.Reset does NOT wipe the list: enemies stay dead " +
                        "for the dying player, and the others' world does not change");
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            EnemySyncManager.OnDeathStateReset();
        }
    }

    public static class EnemyDiagnostics
    {
        /// <summary>
        /// Full dump of everything the mod can see in the current scene. Reports raw values rather
        /// than the mod's derived ids, so it can be used as ground truth to decide how enemies are
        /// identified across machines.
        /// </summary>
        public static List<string> DumpWorld()
        {
            List<string> lines = new List<string>();

            Enemy[] enemies = Object.FindObjectsOfType<Enemy>();
            lines.Add($"Active enemies in scene: {(enemies == null ? 0 : enemies.Length)}");

            // Tracked includes enemies that exist but are not enabled yet (a boss waiting for its
            // cutscene), so the two numbers legitimately differ. Saying so avoids a false alarm.
            lines.Add($"Enemies tracked by mod: {EnemySyncManager.Enemies.Count} " +
                      $"(the difference is enemies registered but not yet active, e.g. a boss before its cutscene)");

            if (enemies != null)
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    Enemy e = enemies[i];
                    if (e == null)
                        continue;

                    Vector3 p = e.transform.position;
                    lines.Add($"  [{i}] name='{e.gameObject.name}' spawningId={e.SpawningId} " +
                              $"pos=({p.x:F2},{p.y:F2},{p.z:F2}) life={e.CurrentLife:F1} dead={e.Dead} " +
                              $"behaviour={(e.EnemyBehaviour == null ? "null" : e.EnemyBehaviour.enabled.ToString())} " +
                              $"childOfSpawnPoint={DescribeSpawnParent(e)}");
                }
            }

            Tools.Level.Layout.EnemySpawnPoint[] points = Object.FindObjectsOfType<Tools.Level.Layout.EnemySpawnPoint>();
            lines.Add($"Active EnemySpawnPoint in scene: {(points == null ? 0 : points.Length)}");

            if (points != null)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    Tools.Level.Layout.EnemySpawnPoint sp = points[i];
                    if (sp == null)
                        continue;

                    Vector3 p = sp.transform.position;
                    Enemy spawned = sp.SpawnedEnemy;
                    lines.Add($"  [{i}] name='{sp.gameObject.name}' entityName='{sp.EntityName}' " +
                              $"spawningId={sp.SpawningId} pos=({p.x:F2},{p.y:F2}) " +
                              $"consumed={sp.Consumed} hasSpawned={sp.HasEnemySpawned} " +
                              $"spawnedEnemy={(spawned == null ? "null" : spawned.gameObject.name)} " +
                              $"enablePersistence={sp.EnablePersistence}");
                }
            }

            // Anything that carries a stable game GUID, which would be the ideal identity if present.
            var persistent = Object.FindObjectsOfType<Framework.FrameworkCore.PersistentObject>();
            int withGuid = 0;
            StringBuilder sb = new StringBuilder();
            if (persistent != null)
            {
                for (int i = 0; i < persistent.Length; i++)
                {
                    var po = persistent[i];
                    if (po == null)
                        continue;

                    string guid;
                    try { guid = po.GetPersistenID(); }
                    catch { continue; }

                    if (string.IsNullOrEmpty(guid))
                        continue;

                    withGuid++;
                    // Only report the ones attached to an enemy, which is what we could actually use.
                    Enemy owner = po.GetComponentInParent<Enemy>();
                    if (owner != null)
                        sb.AppendLine($"  -> persistent object on enemy '{owner.gameObject.name}': guid={guid}");
                }
            }
            lines.Add($"PersistentObject with GUID in scene: {withGuid}");
            if (sb.Length > 0)
                lines.Add(sb.ToString().TrimEnd());

            return lines;
        }

        private static string DescribeSpawnParent(Enemy enemy)
        {
            var sp = enemy.GetComponentInParent<Tools.Level.Layout.EnemySpawnPoint>();
            return sp == null ? "no" : $"si ('{sp.gameObject.name}')";
        }

        /// <summary>Logs the dump immediately, so a bug report contains it without extra steps.</summary>
        public static void LogWorld()
        {
            foreach (string line in DumpWorld())
                ModLog.Info("[EnemyDiag] " + line);
        }
    }
}