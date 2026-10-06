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
    /// Behaviour:
    ///   - offline            -> vanilla, untouched;
    ///   - host               -> vanilla, it is the authority;
    ///   - peer, local swing  -> block locally and ask the host, which relays the approved damage;
    ///   - peer, host relay   -> allowed through (IsApplyingRemoteDamage), otherwise it would loop.
    /// </summary>
    [HarmonyPatch(typeof(EnemyDamageArea), "TakeDamage")]
    public class EnemyDamagePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(EnemyDamageArea __instance, Hit hit)
        {
            // The host relaying approved damage, or a non-network context: let the game apply it.
            if (EnemySyncManager.IsApplyingRemoteDamage)
                return true;

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return true;

            // Only hits dealt by the local penitent are ours to route.
            if (!IsLocalPlayerHit(hit))
                return true;

            if (EnemySyncManager.IsHost)
                return true;

            Enemy enemy = FindEnemy(__instance);
            if (enemy == null)
                return true;

            string id;
            if (!EnemySyncManager.TryGetEnemyId(enemy, out id))
                return true;

            // The game truncates DamageAmount to int in TakeDamageAmount; send the same value so the
            // host and the peers end up with identical health.
            EnemySyncManager.RecordDamageSent();
            Main.Multiplayer.SendEnemyDamageRequest(id, (float)(int)hit.DamageAmount);
            return false; // The host decides; the approved damage comes back over the network.
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
            Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour behaviour =
                area.GetComponentInParent<Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour>();

            if (behaviour != null)
                return behaviour.GetComponent<Enemy>() ?? behaviour.GetComponentInParent<Enemy>();

            return area.GetComponentInParent<Enemy>();
        }
    }
}
