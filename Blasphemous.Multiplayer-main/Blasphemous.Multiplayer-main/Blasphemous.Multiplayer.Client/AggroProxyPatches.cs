using Gameplay.GameControllers.Entities;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Keeps the game's CONTACT damage away from the mod's aggro proxy.
    ///
    /// The proxy is a plain collider on the "Penitent" layer that exists only so the enemies' vision can
    /// find the other player. It has no Entity and no IDamageable on purpose, so nothing can be hurt
    /// through it. But a collider is a collider to the physics engine, and the game arms its contact
    /// attacks from OnTriggerEnter2D:
    ///
    ///     IDamageable c = other.GetComponentInParent&lt;IDamageable&gt;();
    ///     ...
    ///     EntityContactDamage(c);          ->  EnemyAttack.ContactAttack(c)  ->  c.Damage(ContactHit)
    ///
    /// Two things go wrong when that `other` is the proxy, and this prefix closes both:
    ///   - `c` is null (the proxy has no IDamageable) and the call is made anyway, throwing a
    ///     NullReferenceException out of a Unity trigger callback;
    ///   - even before that, IsTargetOverlapped has already been raised, and the attacks that read it
    ///     strike Core.Logic.Penitent without measuring any distance - so the LOCAL player, standing
    ///     twenty units away, took a hit from an enemy that only ever "felt" the proxy.
    ///
    /// Returning false skips the original method, so the proxy is simply not a damageable contact.
    /// </summary>
    [HarmonyPatch(typeof(ContactDamage), "OnTriggerEnter2D")]
    class ContactDamageProxyIgnore_Patch
    {
        public static bool Prefix(Collider2D other)
        {
            return !EnemySyncManager.IsRemoteTargetProxy(other == null ? null : other.gameObject);
        }
    }
}
