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
            // Two id formats travel on this channel, both "scene<sep>payload": an index (a number)
            // for table objects, and a GUID for breakables. BreakSyncMemory tells them apart from
            // the payload, because a GUID parsed as int.Parse would throw IndexOutOfRange/FormatException
            // and take the rest of the queue down with it.
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

            // The player just received a pers. object in the same scene: apply it immediately.
            foreach (PersistentObject persistentObject in Object.FindObjectsOfType<PersistentObject>())
            {
                try
                {
                    if (persistentObject.GetPersistenID() == objectPersistentId)
                    {
                        // Passing "use" to GetCurrentPersistentState plays the used animation.
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
        /// Routes an announced break: applied now if the room is loaded, remembered otherwise.
        /// Remembering matters because the server relays progress to the whole team, not per
        /// scene - a break can arrive while the receiver is elsewhere, and dropping it would
        /// leave a lamp intact for the player who walks in afterwards.
        /// </summary>
        private static void ApplyBreak(string id)
        {
            int separator = id.IndexOf(BreakSyncMemory.IdSeparator);

            // No separator: an older sender sent the bare GUID. The object is looked for in the
            // current scene, so this path stays backward compatible.
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