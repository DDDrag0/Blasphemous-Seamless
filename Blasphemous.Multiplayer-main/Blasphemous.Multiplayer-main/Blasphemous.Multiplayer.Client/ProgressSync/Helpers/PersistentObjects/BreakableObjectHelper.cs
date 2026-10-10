using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using HarmonyLib;
using Tools.Level.Actionables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects
{
    /// <summary>
    /// Synchronises BreakableObject: the scenery a player smashes - lanterns, hanged skeletons, twisted
    /// symbols. They are the objects that break and come BACK, and that is the whole reason they are worth
    /// synchronising: everything that stays broken for good is already handled by the older helpers.
    ///
    /// WHY THE IDENTITY IS THE GUID, AND NOT THE USUAL SCENE~INDEX
    ///
    /// Every other persistent object in this project travels as "scene~index", where the index is a position
    /// in a hand-written table (PersistentStates, 371 entries). That table contains ZERO BreakableObject
    /// GUIDs - measured against a real save file, not deduced - and ProgressManager.UsePersistentObject
    /// returns silently when the lookup fails, so with the table as it is the sender would never send
    /// anything at all.
    ///
    /// Completing the table was rejected on purpose. The indices are positional and end up inside the
    /// player's save file as "scene~index", so inserting entries can shift the index of everything after
    /// them and make old saves point at the wrong object. Instead the GUID travels as the id itself:
    /// ProgressUpdate.Id is already a free-form string, so nothing about the packet, the server or the
    /// protocol changes, no table has to be completed, and no save has to be migrated.
    ///
    /// WHY THERE IS NO MEMORY AND NO BONFIRE HOOK
    ///
    /// BreakableObject.Start subscribes to Core.Logic.OnUsePrieDieu, and on every PrieDieu use the game runs
    /// Restore() on every breakable in the scene: destroyed = false, collider and renderer back on, idle
    /// animation playing. The broken state is transient BY DESIGN, and it is the game that clears it, on
    /// every machine, by itself. So this sync only has to cover the window between two bonfires - and when
    /// the bonfire itself is synchronised later, the vases will line up for free.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "Damage")]
    class BreakableObjectUse_Patch
    {
        /// <summary>
        /// The sender. Two guards, and both are load-bearing.
        ///
        /// ___destroyed is Harmony's way of reading a PRIVATE field, and reading it in a POSTFIX is the only
        /// place it is meaningful: Damage returns early when the object is already broken, so by the time
        /// this runs the flag is true - but it is ALSO true when the damage came from the network and we are
        /// the one applying it. That second case is exactly what
        /// CurrentlyUpdatingProgress is for: it is set while ProgressManager drains its queue, which is
        /// precisely the window in which a relayed break is applied here. Relaying it back would be an echo,
        /// and the other machine would apply the break it just told us about.
        /// </summary>
        public static void Postfix(BreakableObject __instance, bool ___destroyed)
        {
            if (!___destroyed)
                return; // the game refused the hit, so nothing actually broke

            if (Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
                return; // this break arrived from the network: do not send it back

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return; // single player: there is nobody to tell, and the log below stays quiet

            string persistentId = __instance.GetPersistenID();
            if (string.IsNullOrEmpty(persistentId))
                return; // no UniqueId on this prefab: without an identity there is nothing to send

            ModLog.Info($"Broke breakable: {persistentId} (oggetto={__instance.gameObject.name})");

            ProgressUpdate progress = new ProgressUpdate(persistentId, ProgressType.PersistentObject, 0);
            Main.Multiplayer.NetworkManager.SendProgress(progress);
        }
    }

    /// <summary>
    /// The receiver, live path: this is the call PersistentObjectHelper.ApplyProgress makes inside
    /// ProgressManager.Update, and therefore the one that runs while CurrentlyUpdatingProgress is set.
    ///
    /// It calls Use(), which is the game's own "this object just broke" entry point: Use() builds a default
    /// Hit and calls Damage with it. That matters, and it is not a stylistic choice.
    /// BreakableObject.SetCurrentPersistentState CANNOT be used to apply a break: it reads the break from
    /// BreakableManager.ContainsBreakable, and AddBreakable is called from exactly ONE place in the whole
    /// assembly - BreakableObject.Damage. With an empty manager it concludes the object is not broken and
    /// runs Restore() instead, so the break would be cancelled in silence, with no exception to warn anyone.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "GetCurrentPersistentState")]
    class BreakableObjectReceive_Patch
    {
        public static bool Prefix(string dataPath, BreakableObject __instance)
        {
            if (dataPath != "use")
                return true;

            // No guard on the already-broken case, and the reason is that Use() is idempotent where it
            // matters: Damage returns early when destroyed is true, so a repeated application does not double
            // the break, does not re-run OnBreakBreak and does not re-add the object to BreakableManager.
            //
            // It is NOT fully idempotent, and that is worth knowing rather than guarding: Use() invokes the
            // instance OnDead event OUTSIDE the destroyed guard inside Damage, so applying a break twice
            // fires that event twice. Its only consumer is the DestructibleDead PlayMaker action, and the
            // sender cannot produce that case: a break is announced once, and the packet queue is drained
            // once. A guard would need the private destroyed field through Harmony on a path that already
            // runs inside a try/catch in the caller, which is more risk than the case is worth.
            __instance.Use();
            return false;
        }
    }

    /// <summary>
    /// The receiver, load path - and this one is not optional, it is a crash waiting to happen.
    ///
    /// BreakableObject.SetCurrentPersistentState casts the argument and immediately reads a field off it,
    /// with no null check: ((BreakablePersistenceData)data).broken. The game never passes null - its own
    /// loader checks ContainsKey first and skips - but this mod does: ProgressManager.LevelLoaded calls
    /// persistence.SetCurrentPersistentState(null, false, null) to mean "play the instant animation".
    /// The day a BreakableObject becomes reachable from that path, it throws.
    ///
    /// So the two cases are split explicitly: real data means the game is loading a save and is left alone;
    /// null means the mod is asking for the instant state, and gets Use().
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "SetCurrentPersistentState")]
    class BreakableObjectLoad_Patch
    {
        public static bool Prefix(PersistentManager.PersistentData data, BreakableObject __instance)
        {
            if (data != null)
                return true; // the game's own data: let the original method run

            __instance.Use();
            return false;
        }
    }
}
