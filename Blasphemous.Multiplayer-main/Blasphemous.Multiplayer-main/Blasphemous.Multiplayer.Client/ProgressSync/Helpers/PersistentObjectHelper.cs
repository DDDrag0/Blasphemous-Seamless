using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers
{
    public class PersistentObjectHelper : IProgressHelper
    {
        public void ApplyProgress(ProgressUpdate progress)
        {
            // Two id formats travel on this channel, and which one it is cannot be guessed from the
            // separator alone: both are "scene<separator>payload". They are told apart by the payload -
            // an index is a number, a breakable's GUID is not - so the test lives in BreakSyncMemory.
            //
            // The distinction matters because this method used to read sections[1] as an integer
            // unconditionally, and a bare GUID would not merely have been ignored: it would have thrown
            // IndexOutOfRange, and ProgressManager.Update drains the queue in a single loop with no
            // try/catch, so every progress update queued behind it would have been lost.
            if (!BreakSyncMemory.IsTableId(progress.Id))
            {
                ApplyBreak(progress.Id);
                return;
            }

            Main.Multiplayer.ProgressManager.AddInteractedObject(progress.Id);
            string[] sections = progress.Id.Split('~');
            string objectScene = sections[0];
            int objectSceneIdx = int.Parse(sections[1]);
            string objectPersistentId = PersistentStates.getObjectPersistentId(objectScene, objectSceneIdx);

            if (Core.LevelManager.currentLevel.LevelName != objectScene || objectPersistentId == null)
                return;

            // Player just received a pers. object in the same scene - find it and set value immediately
            foreach (PersistentObject persistentObject in Object.FindObjectsOfType<PersistentObject>())
            {
                try
                {
                    if (persistentObject.GetPersistenID() == objectPersistentId)
                    {
                        // Calling getPersistence() with "use" means to play used animation
                        persistentObject.GetCurrentPersistentState("use", false);
                        break;
                    }
                }
                catch (System.NullReferenceException)
                {
                    ModLog.Error("Error: Failed to get persistent id of object");
                }
            }
        }

        /// <summary>
        /// Routes an announced break: applied now if its room is loaded, remembered if it is not.
        ///
        /// The remembered case is the one the first version got wrong, and it is not an edge case: the
        /// server relays progress to the whole team rather than per scene, so a break announced from a room
        /// the receiver has not entered arrives perfectly well - and finds nothing to act on. Dropping it
        /// there is what left a lamp intact for the player who walked in afterwards.
        /// </summary>
        private static void ApplyBreak(string id)
        {
            int separator = id.IndexOf(BreakSyncMemory.IdSeparator);

            // No separator means an older sender that sent the bare GUID. It still works - the object is
            // simply looked for in the current scene - so the format change is backward compatible.
            if (separator < 0)
            {
                BreakSyncMemory.Apply(id);
                return;
            }

            string scene = id.Substring(0, separator);
            string guid = id.Substring(separator + 1);

            if (Core.LevelManager.currentLevel.LevelName != scene)
            {
                BreakSyncMemory.Remember(scene, guid);
                return;
            }

            BreakSyncMemory.Apply(guid);
        }

        public string GetProgressNotification(ProgressUpdate progress)
        {
            return null;
        }

        public void SendAllProgress()
        {
            List<string> objects = Main.Multiplayer.ProgressManager.GetAllInteractedObjects();
            for (int i = 0; i < objects.Count; i++)
            {
                ProgressUpdate progress = new ProgressUpdate(objects[i], ProgressType.PersistentObject, 0);
                Main.Multiplayer.NetworkManager.SendProgress(progress);
            }
        }
    }
}
