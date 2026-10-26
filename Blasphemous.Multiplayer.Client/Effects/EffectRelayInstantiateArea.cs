using System;
using Blasphemous.ModdingAPI;
using Gameplay.GameControllers.Bosses.Quirce.Attack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Hooks BossJumpAttack.InstantiateArea(GameObject) to relay the jump impact area.
    /// This catches effect instantiations that bypass the standard PoolManager (e.g., ElderBrother, Amanecidas).
    /// The Postfix runs after the local game has already instantiated the area.
    /// </summary>
    [HarmonyPatch(typeof(BossJumpAttack), "InstantiateArea")]
    internal static class EffectRelayJumpAreaHook
    {
        /// <summary>
        /// The parameter name 'toInstantiate' must exactly match the game's method signature for Harmony injection.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(BossJumpAttack __instance, GameObject toInstantiate)
        {
            EffectRelayInstantiateArea.TryRelay(EffectRelayInstantiateArea.SiteJump, __instance, toInstantiate);
        }
    }

    /// <summary>
    /// Hooks BossTeleportAttack.InstantiateArea(GameObject) to relay the teleport impact area.
    /// Captures direct instantiations from both OnTeleportOut and OnTeleportIn (e.g., Lesmes, Quirce).
    /// </summary>
    [HarmonyPatch(typeof(BossTeleportAttack), "InstantiateArea")]
    internal static class EffectRelayTeleportAreaHook
    {
        [HarmonyPostfix]
        private static void Postfix(BossTeleportAttack __instance, GameObject toInstantiate)
        {
            EffectRelayInstantiateArea.TryRelay(EffectRelayInstantiateArea.SiteTeleport, __instance, toInstantiate);
        }
    }

    /// <summary>
    /// Shared relay logic for direct InstantiateArea calls.
    /// Routes the instantiated prefab to the main EffectRelay for filtering, deduplication, and network transmission.
    /// </summary>
    internal static class EffectRelayInstantiateArea
    {
        internal const string SiteJump = "BossJump";

        internal const string SiteTeleport = "BossTeleport";

        /// <summary>
        /// Base marker concatenated as a single string literal for binary grep compatibility.
        /// </summary>
        internal const string Marker = "[FxRelay] hook InstantiateArea: prefab=";

        /// <summary>
        /// Evaluates and forwards the prefab to the main effect relay.
        /// Wrapped in try/catch to guarantee that hook failures never interrupt boss attack logic.
        /// Logs unconditionally before filtering to allow distinguishing transmitted effects from dropped cosmetics.
        /// </summary>
        internal static void TryRelay(string site, Component owner, GameObject prefab)
        {
            try
            {
                if (prefab == null || owner == null)
                    return;

                // Position is derived from the component's transform since it is absent from the method signature.
                Vector3 position = owner.transform.position;

                // Isolated try/catch ensures logging exceptions do not prevent network transmission.
                try
                {
                    ModLog.Info(Marker + "\"" + prefab.name + "\" da=" + site);
                }
                catch (Exception)
                {
                }

                // Forward to the main relay logic (filter, throttle, dedup, payload, send).
                EffectRelay.OnPoolSpawn(prefab, position, Quaternion.identity);
            }
            catch (Exception exception)
            {
                try
                {
                    ModLog.Error("[FxRelay] hook InstantiateArea/" + site + ": " +
                                 exception.GetType().Name + " - " + exception.Message);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}