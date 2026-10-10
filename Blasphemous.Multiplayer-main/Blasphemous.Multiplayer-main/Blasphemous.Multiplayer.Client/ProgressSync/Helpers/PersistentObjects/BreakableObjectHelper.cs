using Blasphemous.ModdingAPI;
using HarmonyLib;
using Tools.Level.Actionables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects
{
    /// <summary>
    /// INSTRUMENTATION ONLY - this patch reports, it does not synchronise anything yet.
    ///
    /// The breakable objects a player smashes are BreakableObject, and none of them is synchronised today.
    /// Before writing that - which needs a sender AND a receiver, because the persistent-object table does
    /// not contain a single one of their GUIDs - there are two facts that must be established in game rather
    /// than assumed, and both of them are answered by this one line:
    ///
    ///   1. does GetPersistenID() return a real GUID on these prefabs? The evidence so far is indirect (a
    ///      GUID appears in the save file under sceneElements), and a GUID is authored scene data, which is
    ///      what would make it identical on both machines. If it came back empty, the whole approach would
    ///      need a different identity.
    ///
    ///   2. does a postfix on Damage actually fire? BreakableObject.Damage is public and it is the method the
    ///      game runs when the object is struck, but the object could be reached through another path.
    ///
    /// The log is emitted BEFORE the guard that skips an already-destroyed object, deliberately: the guard
    /// inside the game (if (destroyed) return;) already makes repeat hits harmless, and seeing a second line
    /// for a second swing is information, not noise - it says the object was hit twice while already broken.
    ///
    /// Note this is not the same as the "Broke wall" line already in the project: that one covers
    /// BreakableWall, which IS in the persistent table and IS synchronised. A count of zero there means the
    /// wall was never hit in that session, not that the hook is broken.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "Damage")]
    class BreakableObjectUse_Patch
    {
        /// <summary>
        /// Note the odd parameter name: "___destroyed" is how Harmony injects a PRIVATE field, and the field
        /// cannot be read from here by its plain name. It is captured because the value tells the two cases
        /// apart - the first swing sets it true, a later one finds it already true.
        /// </summary>
        public static void Postfix(BreakableObject __instance, bool ___destroyed)
        {
            string persistentId = __instance.GetPersistenID();

            ModLog.Info($"Broke breakable: {persistentId} " +
                        $"(distrutto={___destroyed} oggetto={__instance.gameObject.name})");
        }
    }
}
