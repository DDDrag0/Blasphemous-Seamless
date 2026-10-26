using HarmonyLib;
using UnityEngine;
using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Keeps the game simulating when it is not the focused window. Unity pauses when losing focus
    /// unless <c>Application.runInBackground</c> is true, and the game explicitly disables it on the
    /// main menu. For single player that is fine; for this mod the host would stop simulating
    /// enemies and stop broadcasting the moment its window loses focus, freezing the other player.
    /// </summary>
    public static class RunInBackgroundFix
    {
        public static void Apply()
        {
            if (Application.runInBackground)
                return;

            Application.runInBackground = true;
            ModLog.Info("[EnemySync] runInBackground enabled: game keeps running in background (required for the host)");
        }
    }

    [HarmonyPatch(typeof(Gameplay.UI.Others.MenuLogic.NewMainMenu), "Awake")]
    internal static class NewMainMenu_Awake_RunInBackground_Patch
    {
        private static void Postfix() => RunInBackgroundFix.Apply();
    }

    [HarmonyPatch(typeof(Gameplay.UI.Others.MenuLogic.Landing), "Awake")]
    internal static class Landing_Awake_RunInBackground_Patch
    {
        private static void Postfix() => RunInBackgroundFix.Apply();
    }

    /// <summary>
    /// Re-asserted whenever a level is configured as a safety net; LogicManager has no
    /// OnLevelLoaded and SetCurrentLevelConfig is called per level.
    /// </summary>
    [HarmonyPatch(typeof(Framework.Managers.LogicManager), "SetCurrentLevelConfig")]
    internal static class LogicManager_SetCurrentLevelConfig_RunInBackground_Patch
    {
        private static void Postfix() => RunInBackgroundFix.Apply();
    }
}