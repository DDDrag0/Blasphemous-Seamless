using HarmonyLib;
using UnityEngine;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Framework.Managers;
using Framework.FrameworkCore;
using Blasphemous.Multiplayer.Client;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Stops an enemy this machine does not own from dithering its facing.
    ///
    /// <c>Entity.SetOrientation</c> is the single setter every orientation write goes through.
    /// <c>Ghost.OnUpdate</c> calls it every frame, comparing its sprite x with the target's x and
    /// turning whenever the sign flips - with no dead band, no time threshold and no memory of the
    /// current facing. With a second player the compared x jitters by a couple of units, so the
    /// sign flips every frame and the enemy visibly trembles.
    ///
    /// Filtering on <c>ApplyOrientation</c> was the wrong place: that method is this mod's own
    /// write (about 20/s), while the churn is the GAME's 60/s writes, which never pass through it.
    /// Returning false from a PREFIX on the single setter is the only place that covers every
    /// writer: a postfix would faithfully replay a jittering owner sixty times a second. Only
    /// puppets are filtered - the owner's facing is authoritative and transmitted. The Penitent is
    /// not an Enemy and falls out immediately.
    ///
    /// The band: inside <c>ORIENT_HYSTERESIS</c> the game is reacting to a fraction of a unit;
    /// outside it, a player has genuinely crossed sides and the turn is applied as before. If the
    /// target cannot be found the write goes through - worst case it declines a turn, never turns
    /// one wrongly.
    /// </summary>
    [HarmonyPatch(typeof(Entity), nameof(Entity.SetOrientation))]
    public class OrientationProbePatch
    {
        /// <summary>
        /// Distance below which a request to turn the enemy is treated as dithering rather than a
        /// real turn. The game's own band is one unit, and it is unreachable code.
        /// </summary>
        private const float ORIENT_HYSTERESIS = 1.5f;

        [HarmonyPrefix]
        public static bool Prefix(Entity __instance, EntityOrientation orientation)
        {
            // The Penitent is not an enemy and is never a puppet.
            Enemy enemy = __instance as Enemy;
            if (enemy == null)
                return true;

            bool owned = !EnemySyncManager.IsPuppet(enemy);

            // Measurement, kept because it is what proved the band was in the wrong place.
            PerformanceProbe.NoteGameOrientationWrite(
                owned,
                EnemySyncManager.LabelOf(enemy));

            if (owned)
                return true;

            EntityStatus status = enemy.Status;
            if (status == null)
                return true;

            // A write asking for the facing the enemy already has is not a turn and is let through.
            if (status.Orientation == orientation)
                return true;

            float dx;
            EntityOrientation wanted;
            if (!TryMeasureTruth(enemy, out dx, out wanted))
                return true;

            // Only a write that would turn the enemy AGAINST the direction the local target
            // actually lies, while the two are inside the band, is dithering. A write that agrees
            // with the band is honest and goes through.
            if (dx >= ORIENT_HYSTERESIS || wanted == orientation)
                return true;

            PerformanceProbe.NoteOrientationFlipSkipped();
            return false;
        }

        /// <summary>
        /// Measures the enemy against the x its own facing code uses (the LOCAL Penitent's), and
        /// says which way that implies. Measuring against <c>Enemy.Target</c> instead was wrong:
        /// that field is written by dozens of other types, and against it the band never opened.
        /// </summary>
        private static bool TryMeasureTruth(Enemy enemy, out float dx, out EntityOrientation wanted)
        {
            dx = 0f;
            wanted = EntityOrientation.Right;

            Penitent local = Core.Logic != null ? Core.Logic.Penitent : null;
            if (local == null)
                return false;

            float raw = enemy.transform.position.x - local.transform.position.x;
            dx = raw < 0f ? -raw : raw;

            // The game turns the enemy to face the target: to its Left when the enemy sits to its left.
            wanted = raw < 0f ? EntityOrientation.Left : EntityOrientation.Right;
            return true;
        }
    }
}