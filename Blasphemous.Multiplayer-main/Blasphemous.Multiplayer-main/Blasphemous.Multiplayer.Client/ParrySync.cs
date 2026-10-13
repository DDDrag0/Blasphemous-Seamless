using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent.Attack;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Synchronises the player's parry, so an enemy reacts on BOTH machines regardless of which player
    /// parried it (voice 35a).
    ///
    /// The parry reaction is LOCAL in this game: PenitentSword.SuccessParryChance(Hit) calls
    /// Enemy.Parry() on the machine where the player parried. When that machine is also the enemy's
    /// OWNER, the reaction plays and its stateHash is already synced down to the puppet, so both screens
    /// agree. When it is NOT the owner (the owner is the other player), the authoritative copy never
    /// learns about it, so the enemy does not visibly stun on the other screen.
    ///
    /// The fix is one-way, like the bonfire: broadcast "parry|<enemyId>" on the enemy-death channel, and
    /// on the receiving side resolve the enemy and call Enemy.Parry() on its authoritative copy. The
    /// sender is skipped by the server (verbatim relay to everyone but the sender), so a player's own
    /// local reaction is never echoed back.
    /// </summary>
    public static class ParrySync
    {
        /// <summary>Marks a parry announcement on the enemy-death channel.</summary>
        public const string Prefix = "parry|";

        /// <summary>True when this id is a parry announcement rather than a death, a break or a bonfire.</summary>
        public static bool IsParryId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(Prefix, System.StringComparison.Ordinal);
        }

        /// <summary>Reads the enemy id out of a parry announcement. Null for anything malformed.</summary>
        public static string EnemyIdOf(string id)
        {
            if (!IsParryId(id))
                return null;

            string enemyId = id.Substring(Prefix.Length);
            return string.IsNullOrEmpty(enemyId) ? null : enemyId;
        }
    }

    /// <summary>
    /// Broadcasts the parry the moment it succeeds. SuccessParryChance is the single call-site of the
    /// sword that reaches Enemy.Parry() (verified in IL), so a postfix here cannot fire on a projectile
    /// or on a parry that failed; the __result guard and the null checks make it robust to both.
    /// </summary>
    [HarmonyPatch(typeof(PenitentSword), "SuccessParryChance")]
    class PenitentSwordParry_Patch
    {
        public static void Postfix(Hit hit, bool __result)
        {
            if (!__result)
                return;

            if (hit.AttackingEntity == null)
                return;

            Enemy enemy = hit.AttackingEntity.GetComponent<Enemy>();
            if (enemy == null)
                return;

            EnemySyncManager.BroadcastParry(enemy);
        }
    }
}
