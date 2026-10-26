using System;
using Blasphemous.ModdingAPI;
using Framework.Util;
using Gameplay.GameControllers.Bosses.EcclesiaBros.Perpetua;
using Gameplay.GameControllers.Bosses.Quirce.Attack;
using Gameplay.GameControllers.Enemies.WallEnemy.Attack;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Keeps the game's CONTACT damage away from the mod's aggro proxy.
    ///
    /// The proxy exists on the "Penitent" layer for enemy vision. Returning false skips the original 
    /// method so the proxy is not treated as a valid contact damage target. This prevents NullReferenceExceptions 
    /// (if the proxy lacks IDamageable for contact) and prevents the local player from taking phantom hits 
    /// from afar due to IsTargetOverlapped triggering on the proxy.
    /// </summary>
    [HarmonyPatch(typeof(ContactDamage), "OnTriggerEnter2D")]
    class ContactDamageProxyIgnore_Patch
    {
        public static bool Prefix(Collider2D other)
        {
            return !EnemySyncManager.IsRemoteTargetProxy(other == null ? null : other.gameObject);
        }
    }

    /// <summary>
    /// Centralized guard to determine whether an attack hitting the multiplayer proxy should be ignored or forwarded.
    /// 
    /// Handles three specific paths:
    /// 1. Identity + Forwarding: The target is the proxy and has an IDamageable (CoopRemoteTargetDamageable). 
    ///    The hit is allowed through (returns false) so the proxy can forward it to the peer.
    /// 2. Identity without Forwarding: The target is the proxy but lacks an IDamageable. The hit is skipped (returns true) 
    ///    to prevent native NullReferenceExceptions.
    /// 3. Structural: No IDamageable found among parents. Skipped to protect against other naked objects on the Penitent layer.
    /// </summary>
    internal static class ProxyDamageGuard
    {
        internal const string Marker = "[ProxyGuard]";

        internal static GameObject ObjectOf(Collider2DParam e)
        {
            if (e == null || e.Collider2DArg == null)
                return null;

            return e.Collider2DArg.gameObject;
        }

        /// <summary>
        /// Returns true if the hit should be SKIPPED (target is the proxy without forwarding, or lacks IDamageable).
        /// Returns false if the hit is valid and should land (either local Penitent, or proxy with forwarding).
        /// </summary>
        internal static bool MustIgnore(GameObject target, string site)
        {
            try
            {
                if (target == null)
                {
                    Report(site, "target is null");
                    return true;
                }

                bool isProxy = EnemySyncManager.IsRemoteTargetProxy(target);

                if (isProxy)
                {
                    if (target.GetComponentInParent<IDamageable>() != null)
                    {
                        ReportDiverted(site);
                        return false; // Hit must reach the proxy to be forwarded to the peer
                    }

                    Report(site, "target IS the proxy but lacks forwarding (IDamageable missing)");
                    return true;
                }

                if (target.GetComponentInParent<IDamageable>() == null)
                {
                    Report(site, "target lacks IDamageable among its parents");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                try
                {
                    ModLog.Error(Marker + " guard failed (" + site + "): " +
                                 e.GetType().Name + " - " + e.Message);
                }
                catch
                {
                    // Failsafe: if logging fails, continue native execution
                }

                return false;
            }
        }

        private static void Report(string site, string why)
        {
            try
            {
                ModLog.Info(Marker + " hit SKIPPED (" + site + "): " + why +
                            " - no damage, no exception");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Logs when a hit is successfully diverted to the peer instead of skipped.
        /// </summary>
        private static void ReportDiverted(string site)
        {
            try
            {
                ModLog.Info(Marker + " hit DIVERTED (" + site + "): target is the proxy, " +
                            "hit goes to peer (local Penitent untouched)");
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Protects BossJumpAttack.OnPenitentEntersArea (e.g., ElderBrother jump).
    /// If the target is the damageable proxy, allows the hit through for forwarding. 
    /// Otherwise, prevents the native game from throwing an NRE.
    /// </summary>
    [HarmonyPatch(typeof(BossJumpAttack), "OnPenitentEntersArea")]
    class BossJumpAttackProxyIgnore_Patch
    {
        public static bool Prefix(Collider2DParam e)
        {
            try
            {
                return !ProxyDamageGuard.MustIgnore(ProxyDamageGuard.ObjectOf(e),
                    "BossJumpAttack.OnPenitentEntersArea");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] BossJumpAttack: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// Protects BossDashAttack.OnPenitentEntersArea (e.g., Quirce/Perpetua dash).
    /// Safely handles the proxy collision, bypassing native code if forwarding is unavailable.
    /// </summary>
    [HarmonyPatch(typeof(BossDashAttack), "OnPenitentEntersArea")]
    class BossDashAttackProxyIgnore_Patch
    {
        public static bool Prefix(Collider2DParam e)
        {
            try
            {
                return !ProxyDamageGuard.MustIgnore(ProxyDamageGuard.ObjectOf(e),
                    "BossDashAttack.OnPenitentEntersArea");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] BossDashAttack: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// Protects WallEnemyProjectile.OnEnterAttackArea.
    /// Filters projectiles hitting the proxy since the native trigger relies strictly on layer masking.
    /// </summary>
    [HarmonyPatch(typeof(WallEnemyProjectile), "OnEnterAttackArea")]
    class WallEnemyProjectileProxyIgnore_Patch
    {
        public static bool Prefix(Collider2DParam e)
        {
            try
            {
                return !ProxyDamageGuard.MustIgnore(ProxyDamageGuard.ObjectOf(e),
                    "WallEnemyProjectile.OnEnterAttackArea");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] WallEnemyProjectile: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// Protects PerpetuaBehaviour.AttackOnStartDash when targeting the proxy explicitly.
    /// The mod's aggro system assigns the remote proxy to Perpetua.Target; this patch ensures 
    /// the dash handles it properly without throwing exceptions.
    /// </summary>
    [HarmonyPatch(typeof(PerpetuaBehaviour), "AttackOnStartDash")]
    class PerpetuaDashProxyIgnore_Patch
    {
        public static bool Prefix(PerpetuaBehaviour __instance)
        {
            try
            {
                if (__instance == null || __instance.Perpetua == null)
                    return true;

                return !ProxyDamageGuard.MustIgnore(__instance.Perpetua.Target,
                    "PerpetuaBehaviour.AttackOnStartDash");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] PerpetuaBehaviour: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }
}