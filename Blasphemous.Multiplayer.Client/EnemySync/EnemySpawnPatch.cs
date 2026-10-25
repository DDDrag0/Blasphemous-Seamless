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

    /// <summary>
    /// Forgets the remembered deaths when the game brings the dead enemies back.
    ///
    /// RespawnDeadEnemies is what a bonfire runs: it clears the spawn points the game considers defeated
    /// and recreates every enemy. But the mod keeps its own memory of announced deaths, and that memory was
    /// never cleared - so the enemy was recreated by the game and killed again by RegisterEnemy on the very
    /// same load. The player who sat at the bonfire saw the enemies come back; the other one saw them stay
    /// dead for ever, and a second bonfire did not help either.
    ///
    ///   the bonfire clears the GAME's consumed list (local, and local it stays)
    ///   nothing cleared the MOD's pending list
    ///
    /// That list is what lets a death announced from another room survive until the enemy appears, so it
    /// cannot be dropped on every level load: it has to be dropped exactly when the game itself decides
    /// those enemies are alive again. That is this method, and only this method.
    ///
    /// The three callers are the bonfire's first activation, its re-activation, and one penitence's level
    /// load (verified in IL), so hooking the method covers all of them without hooking any of them.
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.RespawnDeadEnemies))]
    public class EnemyRespawnPatch
    {
        /// <summary>
        /// TEMPORARY (measurement). Reports the bonfire's respawn the moment it happens.
        ///
        /// Without this the hook was invisible whenever there was nothing to forget: the handler only logged
        /// when its lists were non-empty, so a bonfire with no remembered deaths produced no line at all.
        /// That is why the TODO recorded "the bonfire branch never appears in any log" - the log was silent
        /// by construction, not empty because the branch was never taken. This line is always written, so
        /// the next test says whether the bonfire reaches this method and in which room.
        ///
        /// The scene travels with it because the bonfire only respawns what belongs to the room it is in,
        /// and that is the piece the propagation will need. Remove with the rest of the bonfire probe.
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
    /// The game has dropped its consumed-spawn list without bringing the enemies back through the bonfire.
    ///
    /// EnemySpawner.Reset is the second place the game forgets which spawn points it has consumed -
    /// RespawnDeadEnemies is the first. Its three callers are a player death, a save being loaded, and a
    /// reset from the menu; none of them is an ordinary level load (verified in IL), which is what makes
    /// hooking it safe: it cannot drop the memory the cross-room announcement depends on.
    ///
    /// Without this hook the mod remembered having announced deaths the game no longer considered dead, so
    /// the next joiner was told to kill enemies that were visibly standing. That is the mirror image of the
    /// bug this fix addresses, and it appeared the moment the memory was introduced.
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.Reset))]
    public class EnemySpawnResetPatch
    {
        /// <summary>
        /// On a death we are suppressing, the game's consumed list must NOT be dropped.
        ///
        /// The list is what makes a room come back with its dead enemies still dead: EnemySpawner scans the
        /// loaded scene and CreateEnemy skips every spawn point marked consumed. Clearing it is precisely
        /// what brings them back - and on a death the game does that locally, on the machine that died, with
        /// no network line anywhere in the chain. So one player respawning at a bonfire silently resurrected
        /// every enemy they had killed, while the other machine kept them dead, kept killing them again, and
        /// the two only agreed if something else forced them together.
        ///
        /// SUPPRESSING HERE RATHER THAN AT SpawnEnemiesOnLoad. Skipping the spawn pass instead would be
        /// wrong, not merely later: Reset has already emptied the list, so the respawn would only be
        /// POSTPONED to the next room change - which is the case we are trying to avoid. Keeping the list
        /// full acts at the right moment by construction.
        ///
        /// THE REFILL IS NOT TOUCHED. Health, flasks and fervour are restored by
        /// PersistentManager.RestoreStored, a separate call inside Penitent.Respawn. Suppressing this leaves
        /// it alone, and that is deliberate: without it the player comes back to the bonfire with nothing.
        /// The boundary is exactly the one the design asks for - the world does not reset, the player does
        /// recover.
        ///
        /// ONLY THE DEATH. Reset has three callers and the other two - loading a save and resetting from the
        /// menu - must keep working: the consumed list is not serialised, so it has to be dropped when a save
        /// is loaded. The flag is raised around Penitent.Respawn alone, which is why this reads it rather
        /// than testing anything about the caller.
        ///
        /// THE POSTFIX BELOW STILL RUNS even when this returns false: Harmony always executes postfixes.
        /// That is why the mod's own memory wipe is guarded by the same flag - see OnDeathStateReset.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!PlayerDeathSync.SuppressingWorldReset)
                return true;

            ModLog.Info("[Morte] EnemySpawner.Reset NON svuota la lista: i nemici restano morti " +
                        "per chi muore, e il mondo degli altri non cambia");
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
