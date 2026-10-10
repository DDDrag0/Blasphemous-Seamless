using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using HarmonyLib;
using System.Collections.Generic;
using Tools.Level.Actionables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects
{
    /// <summary>
    /// Synchronises BreakableObject: the scenery a player smashes - lanterns, hanged skeletons, twisted
    /// symbols. They are the objects that break and come BACK, and that is why they are worth synchronising:
    /// everything that stays broken for good is already handled by the older helpers.
    ///
    /// WHY THE IDENTITY IS THE GUID, AND NOT THE USUAL SCENE~INDEX
    ///
    /// Every other persistent object travels as "scene~index", where the index is a position in a
    /// hand-written table (PersistentStates, 371 entries). That table contains ZERO BreakableObject GUIDs -
    /// measured against a real save file, not deduced - and ProgressManager.UsePersistentObject returns
    /// silently when the lookup fails, so with the table as it is the sender would never send anything.
    ///
    /// Completing the table was rejected: the indices are positional and end up inside the player's save as
    /// "scene~index", so inserting entries can shift everything after them and make old saves point at the
    /// wrong object. The GUID travels as the id instead - ProgressUpdate.Id is already a free-form string, so
    /// the packet, the server and the protocol are untouched.
    ///
    /// WHY THERE IS NO BONFIRE HOOK TO FIGHT
    ///
    /// BreakableObject.Start subscribes to Core.Logic.OnUsePrieDieu, and on every PrieDieu use the game runs
    /// Restore() on every breakable in the scene: destroyed = false, collider and renderer back on, idle
    /// animation playing. The broken state is transient BY DESIGN and the game clears it by itself, on every
    /// machine. So this sync only has to cover the window between two bonfires, and when the bonfire itself
    /// is synchronised later the vases will line up for free.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "Damage")]
    class BreakableObjectUse_Patch
    {
        /// <summary>
        /// The sender. The guards are load-bearing.
        ///
        /// ___destroyed is Harmony's way of reading a PRIVATE field, and a POSTFIX is the only place it is
        /// meaningful: Damage returns early when the object is already broken, so by the time this runs the
        /// flag is true - but it is ALSO true when the damage arrived from the network and we are the ones
        /// applying it. CurrentlyUpdatingProgress covers that: it is set while ProgressManager drains its
        /// queue, which is exactly the window in which a relayed break is applied here. Sending it back would
        /// be an echo, and the other machine would apply the break it just told us about.
        ///
        /// The scene travels WITH the guid, and it has to: the receiver may be standing in another room, and
        /// a bare GUID does not say which room it belongs to. There is no table to consult - that is the whole
        /// point of this format.
        /// </summary>
        public static void Postfix(BreakableObject __instance, bool ___destroyed)
        {
            if (!___destroyed)
                return; // the game refused the hit, so nothing actually broke

            if (Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
                return; // this break arrived from the network: do not send it back

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return; // single player: nobody to tell

            string persistentId = __instance.GetPersistenID();
            if (string.IsNullOrEmpty(persistentId))
                return; // no UniqueId on this prefab: without an identity there is nothing to send

            string scene = Core.LevelManager.currentLevel.LevelName;
            ModLog.Info($"Broke breakable: {persistentId} in {scene} (oggetto={__instance.gameObject.name})");

            // Formatted like the table entries so the wire format stays uniform: "scene<sep>payload".
            // See BreakSyncMemory.IdSeparator for why the payload must not read as an integer.
            string id = scene + BreakSyncMemory.IdSeparator + persistentId;
            ProgressUpdate progress = new ProgressUpdate(id, ProgressType.PersistentObject, 0);
            Main.Multiplayer.NetworkManager.SendProgress(progress);
        }
    }

    /// <summary>
    /// The receiver, live path: this is the call PersistentObjectHelper.ApplyProgress makes inside
    /// ProgressManager.Update, and therefore the one that runs while CurrentlyUpdatingProgress is set.
    ///
    /// It calls Use(), which is the game's own "this object just broke" entry point: Use() builds a default
    /// Hit and calls Damage with it. That is not a stylistic choice.
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
    /// persistence.SetCurrentPersistentState(null, false, null) to mean "play the instant state".
    /// The day a BreakableObject becomes reachable from that path, it throws.
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

    /// <summary>
    /// Forgets a remembered break the moment the game brings that object back.
    ///
    /// Restore() is what a PrieDieu use runs on every breakable in the scene, and it is also what
    /// SetCurrentPersistentState runs when the loaded snapshot says the object is intact. Both are "the
    /// object is whole again", and in both cases any memory of it being broken is stale: keeping it would
    /// break the object again the next time its scene loads, which is exactly the fight with the game that
    /// this design is trying to avoid.
    ///
    /// This is the ONLY place the memory is cleared, and it is cleared per object rather than per scene
    /// because that is how the game does it: Restore runs per breakable, on every machine, at every PrieDieu.
    /// No global "respawn" announcement is needed or wanted.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "Restore")]
    class BreakableObjectRestore_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BreakableObject __instance)
        {
            BreakSyncMemory.Forget(__instance.GetPersistenID());
        }
    }

    /// <summary>
    /// Re-applies the remembered breaks once a scene has finished loading both its objects and its save.
    ///
    /// The hook point is the whole trick, and the project had already paid to learn it: Multiplayer's
    /// OnLevelLoaded fires BEFORE the scene exists, so applying there would find nothing - and the game
    /// loads its snapshot immediately afterwards, which would overwrite anything written too early anyway.
    /// PersistentManager.LoadSnapShot is the first moment both conditions hold: it works on
    /// FindObjectsOfType, so the objects are in the scene, and it is the game's own restore, so once its
    /// postfix runs the state it wrote is final.
    /// </summary>
    [HarmonyPatch(typeof(PersistentManager), "LoadSnapShot")]
    class PersistentManagerLoadSnapShot_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BreakSyncMemory.ReapplyCurrentScene();
        }
    }

    /// <summary>
    /// Remembers the breaks that could not be applied because their room was not loaded yet.
    ///
    /// This is the piece the first version was missing, and the reason a lamp stayed intact for the player
    /// who walked in afterwards. The packet arrives fine - the server relays progress to the whole team, not
    /// per scene - but the object it names lives in a room this machine has not loaded, so there is nothing
    /// to act on. Applying would be impossible; forgetting is wrong; so it is held until the room arrives.
    ///
    /// The same shape as _pendingDeaths in EnemySyncManager, and for the same reason: a state change that
    /// happened in a room the player was not standing in still has to be there when they walk in.
    ///
    /// Entries are keyed by "scene|guid" because a GUID alone cannot say which room it belongs to - the
    /// BreakableObject table does not exist, by design.
    /// </summary>
    public static class BreakSyncMemory
    {
        /// <summary>
        /// The separator between scene and payload. A '~' would collide with the "scene~index" entries the
        /// older helpers put in the same field, and that collision is not cosmetic: the two formats are told
        /// apart by whether the part after the separator parses as a number, so a separator that appears in
        /// both would make a GUID look like a broken index.
        /// </summary>
        public const char IdSeparator = '|';

        private const char SceneSeparator = '~';

        /// <summary>Breaks announced for a scene this machine had not loaded. Survives scene changes.</summary>
        private static readonly HashSet<string> pending = new HashSet<string>();

        /// <summary>Breaks applied here, kept so that leaving and re-entering the room re-applies them.</summary>
        private static readonly HashSet<string> applied = new HashSet<string>();

        /// <summary>True when the id belongs to the table format rather than to a breakable.</summary>
        public static bool IsTableId(string id)
        {
            int separator = id.IndexOf(SceneSeparator);
            if (separator < 0 || separator + 1 >= id.Length)
                return false; // no separator, or nothing after it: never a valid table id

            for (int i = separator + 1; i < id.Length; i++)
            {
                if (!char.IsDigit(id[i]))
                    return false; // a GUID is not an index
            }

            return true;
        }

        /// <summary>Applies a break here, and remembers it so re-entering the room re-applies it.</summary>
        public static void Apply(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            if (ApplyToLoadedObject(guid))
            {
                applied.Add(guid);
            }
        }

        /// <summary>Holds a break until its scene is loaded, because there is nothing to act on yet.</summary>
        public static void Remember(string scene, string guid)
        {
            if (string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(guid))
                return;

            pending.Add(scene + IdSeparator + guid);
            ModLog.Info($"[BreakSync] rottura rimandata: {guid} appartiene a {scene}");
        }

        /// <summary>The game brought an object back: any memory of it being broken is stale.</summary>
        public static void Forget(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            applied.Remove(guid);

            int separator = guid.IndexOf(IdSeparator);
            string bare = separator < 0 ? guid : guid.Substring(separator + 1);

            // Rebuild the pending keys rather than remove while enumerating.
            List<string> stale = null;
            foreach (string entry in pending)
            {
                int split = entry.IndexOf(IdSeparator);
                if (split >= 0 && entry.Substring(split + 1) == bare)
                    (stale ??= new List<string>()).Add(entry);
            }

            if (stale == null)
                return;

            for (int i = 0; i < stale.Count; i++)
                pending.Remove(stale[i]);
        }

        /// <summary>
        /// Re-applies everything remembered for the scene that has just finished loading.
        ///
        /// Called from a postfix on PersistentManager.LoadSnapShot, and the hook point is the whole trick:
        /// Multiplayer.OnLevelLoaded fires BEFORE the scene exists - the project already learned that with
        /// the enemies - and applying there would find no objects and be overwritten by the save that is
        /// loaded immediately afterwards. LoadSnapShot is the first moment both are true: the objects are
        /// in the scene AND the game has finished writing its own state onto them.
        /// </summary>
        public static void ReapplyCurrentScene()
        {
            string scene = Core.LevelManager.currentLevel.LevelName;
            if (string.IsNullOrEmpty(scene))
                return;

            List<string> toApply = null;
            foreach (string guid in applied)
            {
                (toApply ??= new List<string>()).Add(guid);
            }

            foreach (string entry in pending)
            {
                int split = entry.IndexOf(IdSeparator);
                if (split < 0 || entry.Substring(0, split) != scene)
                    continue;

                (toApply ??= new List<string>()).Add(entry.Substring(split + 1));
            }

            if (toApply == null)
                return;

            for (int i = 0; i < toApply.Count; i++)
            {
                string guid = toApply[i];
                if (ApplyToLoadedObject(guid))
                {
                    applied.Add(guid);
                    pending.Remove(scene + IdSeparator + guid);
                }
            }
        }

        /// <summary>
        /// Breaks the object with this id if the current scene holds it. False means "not here", which is a
        /// normal answer, not a failure: it is what makes the caller remember instead of forgetting.
        /// </summary>
        private static bool ApplyToLoadedObject(string guid)
        {
            foreach (PersistentObject persistentObject in Object.FindObjectsOfType<PersistentObject>())
            {
                try
                {
                    if (persistentObject.GetPersistenID() != guid)
                        continue;

                    // GetCurrentPersistentState("use") is what the older helpers call, and the breakable
                    // prefix turns it into Use(). Going through that path keeps the echo guard working:
                    // it also means this call happens on the same route a relayed break always took.
                    persistentObject.GetCurrentPersistentState("use", false);
                    ModLog.Info($"[BreakSync] rottura applicata: {guid}");
                    return true;
                }
                catch (System.NullReferenceException)
                {
                    ModLog.Error("Error: Failed to get persistent id of object");
                }
            }

            return false;
        }
    }
}
