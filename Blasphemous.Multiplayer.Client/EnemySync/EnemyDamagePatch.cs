using HarmonyLib;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Implements host-authoritative enemy damage by patching <see cref="EnemyDamageArea.TakeDamage"/>.
    /// A Postfix guarantees the damage is broadcast only after the base game has successfully applied it.
    /// The host applies and broadcasts the damage; peers send damage requests to the host.
    /// Incoming remote damage is flagged via <see cref="EnemySyncManager.IsApplyingRemoteDamage"/> to prevent infinite loops.
    /// </summary>
    [HarmonyPatch(typeof(EnemyDamageArea), "TakeDamage")]
    public class EnemyDamagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(EnemyDamageArea __instance, Hit hit)
        {
            // Prevent infinite echo loops from incoming remote damage.
            if (EnemySyncManager.IsApplyingRemoteDamage)
                return;

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            if (!IsLocalPlayerHit(hit))
                return;

            Enemy enemy = FindEnemy(__instance);
            if (enemy == null)
                return;

            uint handle;
            if (!EnemySyncManager.TryGetEnemyHandle(enemy, out handle))
                return;

            // Match the base game's truncation to int to maintain exact health sync across all clients.
            float amount = (float)(int)hit.DamageAmount;

            // Extract Hit metadata to ensure accurate remote recreation, preserving impact audio, 
            // specific enemy reactions, and damage flags.
            byte damageType = (byte)hit.DamageType;
            byte damageElement = (byte)hit.DamageElement;
            byte flags = EnemySyncManager.HitFlagsFrom(hit);

            if (EnemySyncManager.IsHost)
            {
                EnemySyncManager.RecordDamageApplied();
                Main.Multiplayer.SendEnemyDamageRequest(handle, amount, damageType, damageElement, flags);
            }
            else
            {
                EnemySyncManager.RecordDamageSent();
                Main.Multiplayer.SendEnemyDamageRequest(handle, amount, damageType, damageElement, flags);
            }
        }

        private static bool IsLocalPlayerHit(Hit hit)
        {
            if (hit.AttackingEntity == null || Core.Logic.Penitent == null)
                return false;

            return hit.AttackingEntity == Core.Logic.Penitent.gameObject ||
                   hit.AttackingEntity.transform.IsChildOf(Core.Logic.Penitent.transform);
        }

        private static Enemy FindEnemy(EnemyDamageArea area)
        {
            if (area == null)
                return null;

            // Fallback lookup via EnemyBehaviour.
            var behaviour = area.GetComponentInParent<Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour>();
            if (behaviour != null)
                return behaviour.GetComponent<Enemy>() ?? behaviour.GetComponentInParent<Enemy>();

            return area.GetComponentInParent<Enemy>();
        }
    }
}