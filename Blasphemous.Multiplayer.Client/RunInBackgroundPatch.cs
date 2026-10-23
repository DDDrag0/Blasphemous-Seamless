using HarmonyLib;
using UnityEngine;
using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Keeps the game simulating while it is not the focused window.
    ///
    /// Unity pauses when the game loses focus unless Application.runInBackground is true, and Blasphemous
    /// switches it off explicitly on the main menu:
    ///
    ///   Gameplay.UI.Others.MenuLogic.NewMainMenu.Awake:
    ///     IL_0000: call get_runInBackground()
    ///     IL_0014: ldc.i4.0
    ///     IL_0015: call set_runInBackground(false)
    ///
    /// For single-player that is right. For this mod it is fatal: the host is the machine simulating the
    /// enemies, so the moment its window loses focus the simulation stops and it stops broadcasting, and
    /// the other player watches everything freeze. That is a host losing the window, not a bug in the
    /// sync itself - and it hits badly when one person runs two clients on one machine.
    ///
    /// A postfix on those two Awake methods overrides the setting right after the game applies it, which
    /// is more reliable than setting it once and hoping nothing resets it.
    /// </summary>
    public static class RunInBackgroundFix
    {
        public static void Apply()
        {
            if (Application.runInBackground)
                return;

            Application.runInBackground = true;
            ModLog.Info("[EnemySync] runInBackground abilitato: il gioco continua a girare in background (necessario per l'host)");
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
    /// Also re-asserted whenever a level is configured, as a safety net. LogicManager has no
    /// OnLevelLoaded; SetCurrentLevelConfig is the method the game actually calls per level.
    /// </summary>
    [HarmonyPatch(typeof(Framework.Managers.LogicManager), "SetCurrentLevelConfig")]
    internal static class LogicManager_SetCurrentLevelConfig_RunInBackground_Patch
    {
        private static void Postfix() => RunInBackgroundFix.Apply();
    }
}
