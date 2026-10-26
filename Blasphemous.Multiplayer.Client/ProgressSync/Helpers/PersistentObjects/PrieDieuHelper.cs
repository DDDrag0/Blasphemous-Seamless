using Blasphemous.ModdingAPI;
using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects
{
    /// <summary>
    /// Announces a bonfire so other machines can replicate what it does to the WORLD.
    ///
    /// A bonfire is local: it respawns dead enemies and brings back broken scenery only on the
    /// machine that sat down. The player's own benefits (flask, fervour, save) are deliberately
    /// NOT propagated. Sent from <c>PrieDieu.OnUse</c> because <c>EnemySpawner.RespawnDeadEnemies</c>
    /// has a third caller (<c>PenitencePE02.OnLevelLoaded</c>) that fires on every level load. The
    /// prefix rides the enemy-death channel, which the server relays to every peer but the sender
    /// with no deduplication, so no protocol bump is needed.
    /// </summary>
    public static class BonfireSync
    {
        /// <summary>Marker on the enemy-death channel; a third prefix there is free.</summary>
        public const string Prefix = "falo|";

        /// <summary>True when this id is a bonfire announcement rather than a death or a break.</summary>
        public static bool IsBonfireId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(Prefix, System.StringComparison.Ordinal);
        }

        /// <summary>Scene embedded in a bonfire id, or null for anything malformed.</summary>
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
                return;

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

            // Bonfire's world effect travels separately from its progress record. The guard above
            // prevents a relayed interaction from being announced back.
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
                return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
            }

            __instance.Ligthed = true;
            return false;
        }
    }
}