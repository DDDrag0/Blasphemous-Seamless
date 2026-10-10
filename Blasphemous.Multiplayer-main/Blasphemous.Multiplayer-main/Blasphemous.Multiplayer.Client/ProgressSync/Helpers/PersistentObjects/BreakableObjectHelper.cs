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
        /// <summary>
        /// Suspends the erasure of remembered breaks for the duration of the load.
        ///
        /// Without this, the game's own restore inside LoadSnapShot deletes each remembered break just
        /// before the postfix below can act on it - which is exactly why the hook ran, counted something to
        /// apply, and painted nothing.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
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
    /// Reports, once per session, WHERE the game is keeping its saves and whether Steam came up.
    ///
    /// This is a one-minute instrument that would have saved days of investigation. The save path is built
    /// as persistentDataPath + "/Savegames/" + (SteamManager.Initialized ? SteamID : "") + "/", so starting
    /// the game without Steam silently moves every save into a different folder - and with Steam Cloud
    /// enabled the two locations then disagree, so a session started without Steam looks, from the player's
    /// side, like the saves were corrupted or lost. They were never corrupted: they were somewhere else.
    ///
    /// That is not something the mod can fix. It is something it can make visible, once, in the log.
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
                ModLog.Info($"[Path] Steam inizializzato: {SteamManager.Initialized} · " +
                            $"cartella salvataggi in uso: {PersistentManager.GetPathAppSettings("")}");

                if (!SteamManager.Initialized)
                {
                    ModLog.Error("[Path] Steam NON e' inizializzato: il gioco scrivera' i salvataggi in una " +
                                 "cartella DIVERSA da quella usata quando Steam e' attivo. Se Steam Cloud e' " +
                                 "abilitato per Blasphemous, le due cartelle divergono e la partita sembrera' " +
                                 "persa o corrotta. Avvia STEAM PRIMA del gioco.");
                }
            }
            catch (System.Exception e)
            {
                ModLog.Error("[Path] non ho potuto leggere il percorso dei salvataggi: " +
                             e.GetType().Name + " - " + e.Message);
            }
        }
    }

    /// <summary>
    /// A SECOND chance, one frame later - and for now it is also a measurement.
    ///
    /// The LoadSnapShot hook never painted the break it was written for, and the logs could not say whether
    /// it ran before the scene had its objects, ran before the announcement had even arrived, or ran and was
    /// then overwritten. The three cases look identical from outside, which is why guessing at the answer
    /// would have been the wrong move: this asks instead.
    ///
    /// The frame after the level load is a good place to ask because by then the scene is built and the
    /// game's own state has been written - the two conditions the LoadSnapShot hook was meant to satisfy.
    /// It is deliberately additive: the existing hook is untouched, so whichever of the two paints the
    /// object, the log says which one it was.
    ///
    /// The hook is OnLateUpdate rather than OnLevelLoaded: the mod class is not a MonoBehaviour, so there is
    /// no coroutine to start and no frame to wait in - the frame boundary is taken from the update that
    /// already runs every frame, and armed by the level load.
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
            BreakSyncMemory.ReapplyCurrentScene();
        }

        /// <summary>Arms the deferred pass. Called after the level has been announced as loaded.</summary>
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

        /// <summary>
        /// True while PersistentManager.LoadSnapShot is running.
        ///
        /// This exists to stop Forget from destroying the very thing it was keeping. While a snapshot is
        /// loaded, the game calls SetCurrentPersistentState on every object it has data for; for a breakable
        /// whose break the mod remembers but which this machine never broke, IsBroken() is false (the
        /// BreakableManager is not repopulated on load), so it runs Restore() - and the memory of that break
        /// was cleared a moment before the hook that exists to re-apply it could look.
        ///
        /// The result was measured: the hook ran, the counter said there was something to apply, and nothing
        /// was painted. So the erasure is simply suspended until the snapshot has finished, and the
        /// re-application decides what to do with the entry afterwards.
        /// </summary>
        private static bool loadingSnapshot;

        /// <summary>Called by a prefix on LoadSnapShot. Diagnostics and correctness, both.</summary>
        public static void BeginSnapshot()
        {
            loadingSnapshot = true;
        }

        /// <summary>Called by a postfix on LoadSnapShot, after the re-application has had its turn.</summary>
        public static void EndSnapshot()
        {
            loadingSnapshot = false;
        }

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

            // NOT while a snapshot is loading, and this is the fix for the case that never painted.
            //
            // LoadSnapShot runs SetCurrentPersistentState on every object it holds data for, and for a
            // breakable that means Restore() - because the BreakableManager is not repopulated on load, so
            // IsBroken() answers false even for an object the snapshot says was broken. That Restore lands
            // here, and it used to delete the pending entry a moment before the hook that exists to re-apply
            // it could read it. The hook then ran, reported there was something to apply, and applied nothing.
            //
            // The entry stays. If the object really is whole again and is not in the room being loaded, the
            // re-application simply will not find it and will leave it alone - which is the correct outcome.
            if (loadingSnapshot)
                return;

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

            // TEMPORARY (diagnostics). The first version of this hook never painted a break, and the logs
            // could not say why: there was no line inside it. This one answers the three questions the log
            // could not - is the hook running at all, is it running before the scene has its objects, and was
            // the entry already in memory by then - and it prints the object count so "ran too early" can be
            // told apart from "ran but found nothing". Remove with the rest of the probe.
            ModLog.Info($"[BreakSync] hook LoadSnapShot: scena={scene} " +
                        $"oggetti={CountPersistentObjects()} daApplicare={CountFor(scene)} " +
                        $"pending={pending.Count} applied={applied.Count}");

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

        /// <summary>How many persistent objects exist right now. Diagnostics only.</summary>
        private static int CountPersistentObjects()
        {
            return Object.FindObjectsOfType<PersistentObject>().Length;
        }

        /// <summary>How many remembered breaks belong to this scene. Diagnostics only.</summary>
        private static int CountFor(string scene)
        {
            int count = applied.Count;
            foreach (string entry in pending)
            {
                int split = entry.IndexOf(IdSeparator);
                if (split >= 0 && entry.Substring(0, split) == scene)
                    count++;
            }

            return count;
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
