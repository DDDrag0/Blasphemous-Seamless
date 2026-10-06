using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Enemy registration and diagnostics.
    ///
    /// TIMING is the reason this exists. Multiplayer.OnLevelLoaded fires before the game spawns anything,
    /// so a scan there finds nothing: the log showed "tracking 0 enemies / 0 spawn points" even in rooms
    /// full of enemies. The game spawns them in EnemySpawner.SpawnEnemiesOnLoad(), which is hooked here,
    /// so registration runs right after the enemies actually exist.
    ///
    /// IDENTITY. The game's own Enemy.SpawningId cannot be used: EnemySpawnPoint.Awake sets it to
    /// gameObject.GetHashCode(), which is a per-process instance id. Instead the id is derived from the
    /// enemy's own name plus its spawn position, both of which come from scene data and are therefore
    /// identical on every machine.
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

    public static class EnemyDiagnostics
    {
        /// <summary>
        /// Full dump of everything the mod can see in the current scene. This is the ground truth used to
        /// decide how enemies can be identified across machines, so it deliberately reports raw values
        /// rather than the mod's derived ids.
        /// </summary>
        public static List<string> DumpWorld()
        {
            List<string> lines = new List<string>();

            Enemy[] enemies = Object.FindObjectsOfType<Enemy>();
            lines.Add($"Enemy attivi nella scena: {(enemies == null ? 0 : enemies.Length)}");

            // Tracked includes enemies that exist but are not enabled yet (a boss waiting for its
            // cutscene), so the two numbers legitimately differ. Saying so avoids a false alarm.
            lines.Add($"Nemici tracciati dalla mod: {EnemySyncManager.Enemies.Count} " +
                      $"(la differenza sono nemici registrati ma non ancora attivi, es. un boss prima della cutscene)");

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
            lines.Add($"EnemySpawnPoint attivi nella scena: {(points == null ? 0 : points.Length)}");

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
                        sb.AppendLine($"  -> oggetto persistente su nemico '{owner.gameObject.name}': guid={guid}");
                }
            }
            lines.Add($"PersistentObject con GUID nella scena: {withGuid}");
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
