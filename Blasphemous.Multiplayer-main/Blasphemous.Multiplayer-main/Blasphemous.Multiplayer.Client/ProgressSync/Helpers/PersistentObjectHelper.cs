using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
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
            // Two id formats travel on this channel, and the separator is what tells them apart.
            //
            // Everything the hand-written table knows about goes as "scene~index". BreakableObject does not
            // travel that way and cannot: the table holds none of its GUIDs, and its indices are positional
            // and end up inside the player's save file, so completing the table risks shifting them under old
            // saves. Those objects send their GUID as the id instead.
            //
            // The split below reads sections[1] as an integer, so a bare GUID would not merely be ignored -
            // it would throw IndexOutOfRange and take down every progress update queued behind it, because
            // ProgressManager.Update drains the queue in a single loop with no try/catch. Hence the branch.
            if (progress.Id.IndexOf('~') < 0)
            {
                ApplyByGuid(progress.Id);
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
        /// Applies a change that arrived as a GUID, by finding the object that owns it.
        ///
        /// Only the breakables use this form. The object is looked up among the persistent objects of the
        /// CURRENT scene - the same restriction the table form has, and an acceptable one: an object in a room
        /// this machine is not standing in cannot be acted on, and does not need to be, because the game
        /// restores every breakable in a scene on each PrieDieu use and a scene is rebuilt when it is entered.
        /// </summary>
        private static void ApplyByGuid(string persistentId)
        {
            foreach (PersistentObject persistentObject in Object.FindObjectsOfType<PersistentObject>())
            {
                try
                {
                    if (persistentObject.GetPersistenID() != persistentId)
                        continue;

                    persistentObject.GetCurrentPersistentState("use", false);
                    return;
                }
                catch (System.NullReferenceException)
                {
                    ModLog.Error("Error: Failed to get persistent id of object");
                }
            }
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
