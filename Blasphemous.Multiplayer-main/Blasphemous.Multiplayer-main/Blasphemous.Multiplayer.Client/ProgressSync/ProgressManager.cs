using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Client.ProgressSync.Helpers;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync
{
    public class ProgressManager
    {
        // Only enabled when processing & applying the queued progress updates
        public bool CurrentlyUpdatingProgress { get; private set; }

        // Whether or not all of this player's progress has been sent to the server
        // Reset whenever disconnecting or changing teams
        private bool _sentAllProgress = false;

        // Progress updates are queued when received from the server until you are inside of a game
        private readonly List<ProgressUpdate> progressQueue = new();

        // Save data of pers. object ids that have been interacted with
        private List<string> interactedPersistenceObjects = new ();
        public List<string> SaveInteractedObjects() => interactedPersistenceObjects;
        public void LoadInteractedObjects(List<string> objects) => interactedPersistenceObjects = objects;
        public void ClearInteractedObjects() => interactedPersistenceObjects.Clear();

        // Helper interfaces for applying / obtaining progress updates
        private readonly Dictionary<ProgressType, IProgressHelper> progressHelpers = new ()
        {
            { ProgressType.Bead, new BeadHelper() },
            { ProgressType.Prayer, new PrayerHelper() },
            { ProgressType.Relic, new RelicHelper() },
            { ProgressType.Heart, new HeartHelper() },
            { ProgressType.Collectible, new CollectibleHelper() },
            { ProgressType.QuestItem, new QuestItemHelper() },
            { ProgressType.PlayerStat, new StatHelper() },
            { ProgressType.SwordSkill, new SwordSkillHelper() },
            { ProgressType.MapCell, new MapCellHelper() },
            { ProgressType.Flag, new FlagHelper() },
            { ProgressType.PersistentObject, new PersistentObjectHelper() },
            { ProgressType.Teleport, new TeleportHelper() },
            { ProgressType.ChurchDonation, new ChurchDonationHelper() },
            { ProgressType.MiriamStatus, new MiriamHelper() },
        };

        public void ReceiveProgress(ProgressUpdate progress)
        {
            ModLog.Info("Received new game progress: " + progress.Id);
            if (progress.ShouldSyncProgress(Main.Multiplayer.config))
            {
                progressQueue.Add(progress);
            }
        }

        public void Update()
        {
            CurrentlyUpdatingProgress = true;

            // The flag MUST come down, whatever happens inside.
            //
            // It went up and down around this loop with no try and no finally, and the outer try/finally in
            // Multiplayer.cs resets the performance probe, not this. So a single exception raised by any of
            // the helpers would leave CurrentlyUpdatingProgress true for the rest of the session, and the
            // damage is silent and total: ApplyProgress would stop taking anything off the queue, every
            // sender's "if (!CurrentlyUpdatingProgress) Send..." guard would suppress the announcement, and
            // co-op would simply stop synchronising while the game kept running normally. No crash, no
            // visible error - just a session that quietly stops working.
            //
            // The project already knew the risk and wrote it down: the comment in BreakSyncMemory.IsTableId
            // says an exception there would be fatal "because ProgressManager.Update drains the queue in a
            // single pass with no try/catch". That knowledge never became a fix. This is the fix.
            try
            {
                for (int i = 0; i < progressQueue.Count; i++)
                {
                    ApplyProgress(progressQueue[i]);
                }
            }
            finally
            {
                // In the finally, not after the loop: a partial queue must still be dropped, or the same
                // throwing entry would be retried on every frame and the log would fill with it.
                progressQueue.Clear();
                CurrentlyUpdatingProgress = false;
            }
        }

        private void ApplyProgress(ProgressUpdate progress)
        {
            if (progress == null)
                return;

            if (!progressHelpers.TryGetValue(progress.Type, out IProgressHelper helper))
            {
                ModLog.Error("Error: Progress type doesn't exist: " + progress.Type);
                return;
            }

            // One bad entry must not take the rest of the queue with it.
            //
            // The loop above now guarantees the flag comes down, so this is no longer the difference between
            // a working session and a dead one - but it is still the difference between losing one update and
            // losing every update queued behind it. ApplyProgress parses an id that arrives from the network
            // (int.Parse on the part after '~', and a lookup that can run off the end of the table), so a
            // malformed entry from a peer is a real possibility, not a theoretical one.
            try
            {
                helper.ApplyProgress(progress);
            }
            catch (System.Exception e)
            {
                // Logged with the id, because that is what makes it findable and diagnosable later. A silent
                // catch would hide the one piece of evidence that explains why an update went missing.
                ModLog.Error($"[Progress] apply failed for '{progress.Id}' ({progress.Type}): " +
                             $"{e.GetType().Name} - {e.Message}");
            }
        }

        public string GetProgressNotification(ProgressUpdate progress)
        {
            if (!progressHelpers.TryGetValue(progress.Type, out IProgressHelper helper))
            {
                ModLog.Error("Error: Progress type doesn't exist: " + progress.Type);
                return null;
            }

            return helper.GetProgressNotification(progress);
        }

        // Called when interacting with pers. object - determine whether to send it or not
        public void UsePersistentObject(string persistentId)
        {
            string scene = Core.LevelManager.currentLevel.LevelName;
            int objectSceneIdx = PersistentStates.getObjectSceneIndex(scene, persistentId);
            string objectSceneId = scene + "~" + objectSceneIdx;

            // Make sure this pers. object should sync & isn't already activated
            if (objectSceneIdx < 0 || IsObjectInteracted(objectSceneId))
                return;

            // Update save game data & send this object
            AddInteractedObject(objectSceneId);
            ProgressUpdate progress = new ProgressUpdate(objectSceneId, ProgressType.PersistentObject, 0);
            Main.Multiplayer.NetworkManager.SendProgress(progress);
        }

        // Called when sending all data upon connecting to server and loading game
        public void SendAllProgress()
        {
            if (_sentAllProgress)
                return;
            _sentAllProgress = true;

            foreach (IProgressHelper helper in progressHelpers.Values)
            {
                helper.SendAllProgress();
            }
        }

        public void ResetProgressSentStatus()
        {
            _sentAllProgress = false;
            progressQueue.Clear();
        }

        public void LevelLoaded(string scene)
        {
            foreach (PersistentObject persistence in Object.FindObjectsOfType<PersistentObject>())
            {
                int objectSceneIdx = PersistentStates.getObjectSceneIndex(scene, persistence.GetPersistenID());
                string objectSceneId = scene + "~" + objectSceneIdx;

                // This object does not even sync or hasn't been interacted with yet
                if (objectSceneIdx < 0 || !IsObjectInteracted(objectSceneId)) continue;

                // Calling setPersistence() with null data means to play instant animation
                persistence.SetCurrentPersistentState(null, false, null);
            }
        }

        // Checks whether or not a persistent object has been interacted with
        public bool IsObjectInteracted(string objectSceneId)
        {
            return interactedPersistenceObjects.Contains(objectSceneId);
        }

        public void AddInteractedObject(string objectSceneId)
        {
            if (string.IsNullOrEmpty(objectSceneId))
                return;

            // Added once, not once per event.
            //
            // There was no check here, and the file on disk showed it: eight entries where four were
            // distinct. It is harmless in effect - this list is written to savegame_N_modded.save, which the
            // game never reads - but it is the kind of harmless that stops being harmless the day someone
            // starts counting entries or comparing lists for equality.
            if (interactedPersistenceObjects.Contains(objectSceneId))
                return;

            interactedPersistenceObjects.Add(objectSceneId);
        }

        public List<string> GetAllInteractedObjects()
        {
            return interactedPersistenceObjects;
        }
    }
}
