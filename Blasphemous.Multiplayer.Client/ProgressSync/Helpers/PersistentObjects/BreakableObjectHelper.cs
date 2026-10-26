using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client;
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
    /// Synchronises <see cref="BreakableObject"/> (lanterns, hanged skeletons, twisted symbols) -
    /// the objects that break and come BACK after a bonfire. Identity is the object's GUID, not the
    /// usual "scene~index", because the hand-written <c>PersistentStates</c> table contains zero
    /// BreakableObject GUIDs and completing it would shift positional indices in existing saves.
    /// The scene rides with the GUID on the wire because the receiver may be in another room.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "Damage")]
    class BreakableObjectUse_Patch
    {
        /// <summary>
        /// Records whether the object was ALREADY broken when Damage was entered. Read before the
        /// game can touch the field; needed so an applied break (from the wire) is not announced
        /// back as if it were a local swing - Damage returns early when already broken, leaving
        /// the same observable state as a real break.
        /// </summary>
        private static readonly Dictionary<BreakableObject, bool> alreadyBroken =
            new Dictionary<BreakableObject, bool>();

        [HarmonyPrefix]
        public static void Prefix(BreakableObject __instance, bool ___destroyed)
        {
            alreadyBroken[__instance] = ___destroyed;
        }

        /// <summary>Called on level load: the entries refer to objects that no longer exist.</summary>
        public static void ForgetTrackedObjects()
        {
            alreadyBroken.Clear();
        }

        /// <summary>
        /// Announces a genuine local break. Four guards, all load-bearing: the object must have
        /// actually broken, it must not be a relayed application (<c>CurrentlyUpdatingProgress</c>
        /// or <c>IsReapplying</c>), the machine must be connected, and the object must carry a
        /// GUID. The scene travels with the GUID on the enemy-death channel, whose "brk|" prefix
        /// keeps it clear of "sp|" spawn-point ids that share the channel.
        /// </summary>
        public static void Postfix(BreakableObject __instance, bool ___destroyed)
        {
            bool wasBroken;
            bool tracked = alreadyBroken.TryGetValue(__instance, out wasBroken);
            alreadyBroken.Remove(__instance);

            if (tracked && wasBroken)
                return; // already broken before this call: nothing to announce

            if (!___destroyed)
                return; // the game refused the hit, nothing actually broke

            if (Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
                return; // arrived from the network: do not send back

            // Same rule for the memory's own re-application, which runs outside
            // ProgressManager.Update; on the enemy-death channel nothing is discarded, so an echo
            // would be a real relay and could revive a break a bonfire had already undone.
            if (BreakSyncMemory.IsReapplying)
                return;

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            string persistentId = __instance.GetPersistenID();
            if (string.IsNullOrEmpty(persistentId))
                return; // no UniqueId on this prefab: nothing to send

            string scene = Core.LevelManager.currentLevel.LevelName;
            ModLog.Info($"Broke breakable: {persistentId} in {scene} (oggetto={__instance.gameObject.name})");

            // Announced on the enemy-death channel, not as a progress entry. The progress channel
            // deduplicates per id for the life of the server session and every persistent object
            // sends value 0, so repeats are discarded in silence; enemy-death relays every message.
            string id = BreakSyncMemory.BreakPrefix + scene + BreakSyncMemory.IdSeparator + persistentId;
            Main.Multiplayer.SendEnemyDeathGlobal(id);
        }
    }

    /// <summary>
    /// Receiver, live path (called from PersistentObjectHelper.ApplyProgress inside
    /// ProgressManager.Update). Applies the break through <c>Use()</c>, which is the game's own
    /// entry point. <c>SetCurrentPersistentState</c> cannot be used to apply a break: it reads
    /// BreakableManager.ContainsBreakable, which is only populated by Damage, so with an empty
    /// manager it would run Restore() and silently cancel the break.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "GetCurrentPersistentState")]
    class BreakableObjectReceive_Patch
    {
        /// <summary>
        /// Applies the break unless the object is already broken. The re-application runs more than
        /// once per level load, and without this guard each pass would re-break the object and
        /// re-play the animation. <c>___destroyed</c> is the same source the sender trusts.
        /// </summary>
        public static bool Prefix(string dataPath, BreakableObject __instance, bool ___destroyed)
        {
            if (dataPath != "use")
                return true;

            if (___destroyed)
            {
                // Marker fires only when the guard actually refuses: it is the evidence that the
                // double-animation fix is being reached, not just declared.
                ModLog.Info($"[BreakSync] already broken, apply skipped: {__instance.GetPersistenID()}");
                return false;
            }

            __instance.Use();
            return false;
        }
    }

    /// <summary>
    /// Receiver, load path - guards against the null data the mod's own LevelLoaded passes to
    /// every PersistentObject. SetCurrentPersistentState casts the argument with no null check, so
    /// a BreakableObject reached from that path would throw.
    /// </summary>
    [HarmonyPatch(typeof(BreakableObject), "SetCurrentPersistentState")]
    class BreakableObjectLoad_Patch
    {
        public static bool Prefix(PersistentManager.PersistentData data, BreakableObject __instance)
        {
            if (data != null)
                return true;

            __instance.Use();
            return false;
        }
    }

    /// <summary>
    /// Forgets a remembered break the moment the game restores that object (bonfire Restore or the
    /// save loader). Keeping the memory would re-break it on the next load. Cleared per object,
    /// because that is how the game does it - no global respawn announcement.
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
    /// Re-applies remembered breaks once the scene has finished loading both its objects and its
    /// save. The hook is LoadSnapShot, not OnLevelLoaded: the latter fires before the scene exists
    /// and is overwritten by the save load immediately after.
    /// </summary>
    [HarmonyPatch(typeof(PersistentManager), "LoadSnapShot")]
    class PersistentManagerLoadSnapShot_Patch
    {
        /// <summary>
        /// Suspends the erasure of remembered breaks for the duration of the load. Without this,
        /// the game's own Restore inside LoadSnapShot deletes each entry a moment before the
        /// postfix below can act on it.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            BreakableObjectUse_Patch.ForgetTrackedObjects();
            BreakSyncMemory.BeginSnapshot();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BreakSyncMemory.ReapplyCurrentScene();
            BreakSyncMemory.EndSnapshot();
        }
    }

    /// <summary>
    /// Reports, once per session, where the game keeps saves and whether Steam initialised. The
    /// save path embeds the SteamID when Steam is up, so starting the game without Steam silently
    /// uses a different folder - and with Steam Cloud enabled the two diverge.
    /// </summary>
    [HarmonyPatch(typeof(Multiplayer), "OnLateUpdate")]
    class MultiplayerSavePathReport_Patch
    {
        private static bool reported;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (reported)
                return;

            reported = true;

            try
            {
                ModLog.Info($"[Path] Steam initialised: {SteamManager.Initialized} · " +
                            $"save folder in use: {PersistentManager.GetPathAppSettings("")}");

                if (!SteamManager.Initialized)
                {
                    ModLog.Error("[Path] Steam is NOT initialised: the game will use a different " +
                                 "save folder than when Steam is up. If Steam Cloud is enabled for " +
                                 "Blasphemous the two folders diverge and the save will look lost or " +
                                 "corrupted. Start STEAM first.");
                }
            }
            catch (System.Exception e)
            {
                ModLog.Error("[Path] could not read the save path: " +
                             e.GetType().Name + " - " + e.Message);
            }
        }
    }

    /// <summary>
    /// Second chance one frame later. The re-application runs more than once per level load and
    /// duplicates are harmless now that applying a break to an already broken object is refused;
    /// this pass is kept because the two do not fire at the same moment (LoadSnapShot's can run
    /// while objects are still being built). Armed by OnLevelLoaded, executed on OnLateUpdate -
    /// the mod class is not a MonoBehaviour, so no coroutine is available.
    /// </summary>
    [HarmonyPatch(typeof(Multiplayer), "OnLateUpdate")]
    class MultiplayerLateUpdate_Patch
    {
        private static bool armed;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!armed)
                return;

            armed = false;
            try
            {
                BreakSyncMemory.ReapplyCurrentScene();
            }
            catch (System.Exception e)
            {
                // A throw here stops the sibling postfixes on OnLateUpdate too, which is how a null
                // level once silently suppressed the [Path] line. Logged, not swallowed.
                ModLog.Error($"[BreakSync] deferred pass failed: {e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>Arms the deferred pass. Called after OnLevelLoaded.</summary>
        public static void Arm()
        {
            armed = true;
        }
    }

    [HarmonyPatch(typeof(Multiplayer), "OnLevelLoaded")]
    class MultiplayerOnLevelLoaded_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            MultiplayerLateUpdate_Patch.Arm();
        }
    }

    /// <summary>
    /// Remembers breaks that arrived while their scene was not loaded - the server relays progress
    /// to the whole team, so a break from another room arrives with nothing to act on. Entries are
    /// keyed by "scene|guid", because a GUID alone cannot say which room it belongs to. Holds the
    /// re-application flag so an applied break is never announced back.
    /// </summary>
    public static class BreakSyncMemory
    {
        /// <summary>
        /// Scene/payload separator. Must not be '~', which would collide with the "scene~index"
        /// entries the older helpers put in the same field; the two formats are told apart by
        /// whether the part after the separator parses as a number.
        /// </summary>
        public const char IdSeparator = '|';

        private const char SceneSeparator = '~';

        /// <summary>Breaks announced for a scene this machine had not loaded. Survives scene changes.</summary>
        private static readonly HashSet<string> pending = new HashSet<string>();

        /// <summary>Breaks applied here, kept so re-entering the room re-applies them.</summary>
        private static readonly HashSet<string> applied = new HashSet<string>();

        /// <summary>
        /// True while PersistentManager.LoadSnapShot is running. During a load the game calls
        /// SetCurrentPersistentState on every object; for a remembered break the BreakableManager
        /// is not repopulated, so the game runs Restore() and would delete the pending entry a
        /// moment before the postfix that re-applies it.
        /// </summary>
        private static bool loadingSnapshot;

        /// <summary>Called by a prefix on LoadSnapShot.</summary>
        public static void BeginSnapshot()
        {
            loadingSnapshot = true;
        }

        /// <summary>Called by a postfix on LoadSnapShot, after the re-application.</summary>
        public static void EndSnapshot()
        {
            loadingSnapshot = false;
        }

        /// <summary>
        /// True while a break is being applied here, so the sender can tell an application from a
        /// local swing. ReapplyCurrentScene runs outside ProgressManager.Update, so
        /// CurrentlyUpdatingProgress is false while it works.
        /// </summary>
        public static bool IsReapplying { get { return reapplying; } }

        private static bool reapplying;

        /// <summary>Break prefix on the enemy-death channel: "brk|".</summary>
        public const string BreakPrefix = "brk|";

        /// <summary>True when this id is a break announcement rather than a death.</summary>
        public static bool IsBreakId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(BreakPrefix, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Splits "brk|scene|guid" into scene and guid. False for anything malformed so a rogue
        /// format is never misinterpreted as a break. The GUID never contains the separator.
        /// </summary>
        public static bool TryParseBreakId(string id, out string scene, out string guid)
        {
            scene = null;
            guid = null;

            if (!IsBreakId(id))
                return false;

            string rest = id.Substring(BreakPrefix.Length);
            int separator = rest.IndexOf(IdSeparator);
            if (separator <= 0 || separator + 1 >= rest.Length)
                return false;

            scene = rest.Substring(0, separator);
            guid = rest.Substring(separator + 1);
            return true;
        }

        /// <summary>True when the id belongs to the table format rather than to a breakable.</summary>
        public static bool IsTableId(string id)
        {
            int separator = id.IndexOf(SceneSeparator);
            if (separator < 0 || separator + 1 >= id.Length)
                return false;

            for (int i = separator + 1; i < id.Length; i++)
            {
                if (!char.IsDigit(id[i]))
                    return false;
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

        /// <summary>
        /// Routes an announced break: applied now if the scene is loaded, remembered otherwise.
        /// Sets IsReapplying around the application so the sender does not announce it back - the
        /// enemy-death channel discards nothing, so an echo here would be a real relay.
        /// </summary>
        public static void ApplyOrRemember(string scene, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            if (!string.IsNullOrEmpty(scene) && Core.LevelManager.currentLevel.LevelName != scene)
            {
                Remember(scene, guid);
                return;
            }

            // Saved/restored (not set/cleared) because applications can nest.
            bool wasReapplying = reapplying;
            reapplying = true;
            try
            {
                Apply(guid);
            }
            finally
            {
                reapplying = wasReapplying;
            }
        }

        /// <summary>Holds a break until its scene is loaded.</summary>
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

            // Not while a snapshot is loading: the game's own Restore during LoadSnapShot lands
            // here and would delete the pending entry a moment before the re-application.
            if (loadingSnapshot)
                return;

            int separator = guid.IndexOf(IdSeparator);
            string bare = separator < 0 ? guid : guid.Substring(separator + 1);

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
        /// Re-applies everything remembered for the scene that has just finished loading. Runs the
        /// applications with IsReapplying set, so the sender cannot echo them.
        /// </summary>
        public static void ReapplyCurrentScene()
        {
            // Nothing to do while an application is already in progress; re-entering would clear
            // the flag while the outer pass is still running.
            if (reapplying)
                return;

            // The level may not exist yet at this point - the deferred pass is armed by
            // OnLevelLoaded, which itself runs before the scene exists. Reading LevelName straight
            // through NREs on the main menu, and taking the sibling postfix down with it.
            if (Core.LevelManager == null || Core.LevelManager.currentLevel == null)
                return;

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

            reapplying = true;
            try
            {
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
            finally
            {
                reapplying = false;
            }
        }

        /// <summary>
        /// Breaks the object with this id if the current scene holds it. False means "not here",
        /// which is what makes the caller remember instead of forgetting.
        /// </summary>
        private static bool ApplyToLoadedObject(string guid)
        {
            foreach (PersistentObject persistentObject in Object.FindObjectsOfType<PersistentObject>())
            {
                try
                {
                    if (persistentObject.GetPersistenID() != guid)
                        continue;

                    // GetCurrentPersistentState("use") is the same route a relayed break always
                    // took; the breakable prefix turns it into Use().
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