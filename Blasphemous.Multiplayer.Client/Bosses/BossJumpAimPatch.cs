using System;
using Blasphemous.ModdingAPI;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Modifies the Elder Brother boss's jump prediction to aim at the multiplayer proxy.
    /// By default, the boss's jump attack hardcodes the local Penitent's position, causing 
    /// the boss to always jump towards the host even if aggroed by the client.
    /// 
    /// This Postfix overrides the return value of GetTargetPredictedPos to use the proxy's 
    /// position if Entity.Target is a multiplayer proxy, seamlessly fixing the trajectory.
    /// </summary>
    [HarmonyPatch(typeof(Gameplay.GameControllers.Bosses.ElderBrother.ElderBrotherBehaviour),
                  "GetTargetPredictedPos")]
    class ElderBrotherJumpAimProxy_Patch
    {
        internal const string Marker = "[SaltoProxy]";

        public static void Postfix(
            Gameplay.GameControllers.Bosses.ElderBrother.ElderBrotherBehaviour __instance,
            ref Vector3 __result)
        {
            try
            {
                Gameplay.GameControllers.Bosses.ElderBrother.ElderBrother boss =
                    __instance == null ? null : __instance.ElderBrother;

                GameObject target = boss == null ? null : boss.Target;

                if (target == null)
                {
                    // No target assigned yet: original aim is correct. No changes applied.
                    Log(__instance, false, "boss has no Target", __result);
                    return;
                }

                if (!EnemySyncManager.IsRemoteTargetProxy(target))
                {
                    // Normal state (single player or aggro on local player): keep original __result.
                    Log(__instance, false, "Target is not the proxy", __result);
                    return;
                }

                // Target is the proxy: aim at the other player's position. 
                // The original Z axis is kept (0) to preserve the game's linecast logic.
                Vector3 proxyPos = target.transform.position;
                __result = new Vector3(proxyPos.x, proxyPos.y, __result.z);

                Log(__instance, true, "Target is the other player's proxy", __result);
            }
            catch (Exception e)
            {
                // Fallback to original game behavior to prevent breaking the jump completely.
                try
                {
                    ModLog.Error(Marker + " aim left to original: " +
                                 e.GetType().Name + " - " + e.Message);
                }
                catch
                {
                }
            }
        }

        private static void Log(
            Gameplay.GameControllers.Bosses.ElderBrother.ElderBrotherBehaviour instance,
            bool usesProxy,
            string why,
            Vector3 aim)
        {
            try
            {
                int id = instance == null ? 0 : instance.GetInstanceID();
                string who = instance == null ? "ElderBrother n/d" : instance.name;

                // Keeps all structured keys for grep intact
                string where = "punto=(" + aim.x.ToString("F1") + ", " + aim.y.ToString("F1") + ")";

                Gameplay.GameControllers.Penitent.Penitent local = Framework.Managers.Core.Logic.Penitent;
                if (local != null && local.transform != null)
                {
                    Vector3 p = local.transform.position;
                    where += " penitenteLocale=(" + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ")" +
                             " distanza=" +
                             Vector2.Distance(new Vector2(aim.x, aim.y), new Vector2(p.x, p.y)).ToString("F1");
                }

                if (usesProxy)
                {
                    ModLog.Info(Marker + " jump aiming at other player's PROXY: " +
                                who + " istanza=" + id + " " + where);
                }
                else
                {
                    ModLog.Info(Marker + " jump aiming at local PENITENT like base game (" +
                                why + "): " + who + " istanza=" + id + " " + where);
                }
            }
            catch
            {
                // Must never interrupt the jump attack.
            }
        }
    }
}