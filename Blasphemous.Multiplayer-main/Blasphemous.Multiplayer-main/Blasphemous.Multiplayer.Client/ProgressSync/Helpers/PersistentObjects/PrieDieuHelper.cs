using Blasphemous.ModdingAPI;
using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects
{
    /// <summary>
    /// Announces a bonfire, so the other machines can replicate what it does to the WORLD.
    ///
    /// A bonfire is LOCAL in this game and stays local: it respawns the dead enemies and brings back the
    /// broken scenery on the machine that sat down, and on that one only. Measured, not assumed: every
    /// "[Falo''] RespawnDeadEnemies" line in a test session was preceded - on the same machine - by that
    /// machine's own "Used PrieDieu", with a constant 24-25 line gap, and a fourth bonfire on one side had
    /// no counterpart on the other and left no trace there at all.
    ///
    /// The player's own benefits are a separate matter and are deliberately NOT propagated: the flask, the
    /// fervour and the save belong to whoever sat down.
    ///
    /// WHY THE ANNOUNCEMENT IS SENT FROM HERE. PrieDieu.OnUse only builds the coroutine and returns - its
    /// caller starts it afterwards - so a postfix on it runs BEFORE the body does anything. For a signal of
    /// intent that is exactly right, and it is what this needs: we want to say "a bonfire is being used
    /// here", not "the respawn has already happened".
    ///
    /// WHY NOT FROM THE RESPAWN HOOK. EnemySpawner.RespawnDeadEnemies has THREE callers, not two, and the
    /// third is Framework.Penitences.PenitencePE02.OnLevelLoaded: while that penitence is active, ANY level
    /// load calls it. A broadcast placed in that hook would fire on every room change for a player using
    /// that penitence. This entry point cannot, because it is the bonfire and nothing else.
    /// </summary>
    public static class BonfireSync
    {
        /// <summary>
        /// Marks a bonfire announcement. It travels on the enemy-death channel, whose only other
        /// conventions are "sp|" for spawn points and "brk|" for a broken vase, and which the server relays
        /// to everyone but the sender with no dictionary and no deduplication - so a third prefix costs
        /// nothing and needs no protocol bump.
        /// </summary>
        public const string Prefix = "falo|";

        /// <summary>True when this id is a bonfire announcement rather than a death or a break.</summary>
        public static bool IsBonfireId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(Prefix, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads the scene out of a bonfire id. Returns null for anything malformed, so a rogue id cannot
        /// be mistaken for a bonfire.
        /// </summary>
        public static string SceneOf(string id)
        {
            if (!IsBonfireId(id))
                return null;

            string scene = id.Substring(Prefix.Length);
            return string.IsNullOrEmpty(scene) ? null : scene;
        }

        /// <summary>Tells the other machines that a bonfire was used in this room.</summary>
        public static void Broadcast(string scene)
        {
            if (string.IsNullOrEmpty(scene))
                return;

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return; // single player: nobody to tell

            Main.Multiplayer.SendEnemyDeathGlobal(Prefix + scene);
            ModLog.Info($"[Falo''] annuncio inviato: scena={scene}");
        }
    }

    [HarmonyPatch(typeof(PrieDieu), "OnUse")]
    class PrieDieuUse_Patch
    {
        public static void Postfix(PrieDieu __instance)
        {
            if (Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress) return;

            string persistentId = __instance.GetPersistenID();
            ModLog.Info($"Used PrieDieu: {persistentId}");
            Main.Multiplayer.ProgressManager.UsePersistentObject(persistentId);

            // The bonfire's effect on the world travels separately from its record in the progress list.
            //
            // This is the local player sitting down, and the guard above keeps a relayed interaction from
            // being announced back - the same rule the rest of the progress helpers use.
            BonfireSync.Broadcast(Core.LevelManager.currentLevel == null
                ? null
                : Core.LevelManager.currentLevel.LevelName);
        }
    }

    [HarmonyPatch(typeof(PrieDieu), "GetCurrentPersistentState")]
    class PrieDieuReceive_Patch
    {
        public static bool Prefix(string dataPath, PrieDieu __instance)
        {
            if (dataPath != "use") return true;

            // Maybe play activation animation
            __instance.Ligthed = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PrieDieu), "SetCurrentPersistentState")]
    class PrieDieuLoad_Patch
    {
        public static bool Prefix(PersistentManager.PersistentData data, PrieDieu __instance)
        {
            if (data != null)
            {
                // This method is being called normally - only execute if object hasn't been interacted with
                return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
            }

            __instance.Ligthed = true;
            return false;
        }
    }
}
