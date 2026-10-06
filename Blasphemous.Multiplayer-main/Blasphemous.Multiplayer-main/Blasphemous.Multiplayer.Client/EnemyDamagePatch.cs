using HarmonyLib;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Makes player-dealt enemy damage host-authoritative.
    ///
    /// This patches <see cref="EnemyDamageArea.TakeDamage"/>, the hurtbox that actually applies the
    /// damage, NOT PenitentSword.OnEnemyDamaged. That method only grants fervour and notifies the
    /// inventory (it is subscribed to the static EnemyDamageArea.OnDamagedGlobal, which fires *after*
    /// the damage has already been applied), so patching it would neither block nor duplicate damage.
    ///
    /// It is a postfix, which is what makes the authority model work out:
    ///   - the HOST lets the vanilla damage happen and then broadcasts it, so peers can show it;
    ///   - a PEER never sees its own hit applied (the host will), and asks the host to apply it;
    ///   - damage the host relays back is marked by <see cref="EnemySyncManager.IsApplyingRemoteDamage"/>
    ///     and is ignored here, which is what stops the loop and any double application.
    ///
    /// A prefix could not express this: on the host it would have to both let the damage through and
    /// emit a packet, and a prefix that returns true cannot know whether the damage actually landed
    /// (EnemyDamageArea drops a second hit on the same frame).
    /// </summary>
    [HarmonyPatch(typeof(EnemyDamageArea), "TakeDamage")]
    public class EnemyDamagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(EnemyDamageArea __instance, Hit hit)
        {
            // Damage relayed by the host: already authorised, and re-broadcasting it would loop.
            if (EnemySyncManager.IsApplyingRemoteDamage)
                return;

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            // Only hits dealt by the local penitent are ours to route.
            if (!IsLocalPlayerHit(hit))
                return;

            Enemy enemy = FindEnemy(__instance);
            if (enemy == null)
                return;

            ushort index;
            if (!EnemySyncManager.TryGetEnemyIndex(enemy, out index))
                return;

            // The game truncates DamageAmount to int in TakeDamageAmount; send the same value so every
            // machine ends up with the same health.
            float amount = (float)(int)hit.DamageAmount;

            if (EnemySyncManager.IsHost)
            {
                // The damage already landed here; the server relays this to the peers in the scene so
                // they can show it too.
                EnemySyncManager.RecordDamageApplied();
                Main.Multiplayer.SendEnemyDamageRequest(index, amount);
            }
            else
            {
                EnemySyncManager.RecordDamageSent();
                Main.Multiplayer.SendEnemyDamageRequest(index, amount);
            }
        }

        private static bool IsLocalPlayerHit(Hit hit)
        {
            if (hit.AttackingEntity == null || Core.Logic.Penitent == null)
                return false;

            return hit.AttackingEntity == Core.Logic.Penitent.gameObject ||
                   hit.AttackingEntity.transform.IsChildOf(Core.Logic.Penitent.transform);
        }

        // The damaged enemy is the one owning the damage area being patched.
        private static Enemy FindEnemy(EnemyDamageArea area)
        {
            if (area == null)
                return null;

            // Every Enemy needs an EnemyBehaviour, so looking the behaviour up is a reliable fallback.
            var behaviour = area.GetComponentInParent<Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour>();
            if (behaviour != null)
                return behaviour.GetComponent<Enemy>() ?? behaviour.GetComponentInParent<Enemy>();

            return area.GetComponentInParent<Enemy>();
        }
    }
}
