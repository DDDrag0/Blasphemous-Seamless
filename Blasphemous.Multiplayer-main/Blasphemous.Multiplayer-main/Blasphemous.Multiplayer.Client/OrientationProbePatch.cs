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
    /// THE BUG. Entity.SetOrientation is the one setter every orientation write goes through. Ghost's
    /// OnUpdate calls it every frame, comparing its sprite x with the target's x and turning whenever the
    /// sign flips - with no dead band, no time threshold and no memory of the facing it already has. In
    /// single player the target drifts slowly and never crosses the boundary; with a second player the
    /// compared x jitters by a couple of units, so the sign flips every frame and the enemy visibly
    /// trembles. Measured: 31 flips in 1.7 s on one machine, 106 in 10.4 s on the other, with the enemy
    /// standing still.
    ///
    /// WHY HERE AND NOT IN ApplyOrientation. Hysteresis was first added to ApplyOrientation, which is this
    /// mod's own write - twenty per second. That filtered the wrong thing: the churn is the GAME's sixty
    /// writes per second, which never pass through ApplyOrientation at all. The measurement said so
    /// plainly: over a whole session orientSkip stayed at zero while orientFlip reached 34 in one two
    /// second window. The guard never saw the enemy that was spinning. Filtering the single setter is the
    /// only place that covers every writer, whoever calls it.
    ///
    /// A PREFIX, NOT A POSTFIX. A postfix that restored the owner's facing would faithfully replay a
    /// jittering owner sixty times a second - the opposite of the point. Returning false instead declines
    /// the write, so the enemy simply keeps the facing it has.
    ///
    /// ONLY ON ENEMIES THIS MACHINE DOES NOT OWN. The owner's facing is authoritative and is the one that
    /// gets transmitted, so it must never be filtered here; the answer comes from a hash set the sync
    /// manager maintains exactly where ownership changes. The Penitent is not an Enemy and falls out
    /// immediately, and the two bosses that override SetOrientation do not pass through the base method at
    /// all - so the player and the bosses are untouched by construction.
    ///
    /// THE BAND. Inside ORIENT_HYSTERESIS the game is not deciding anything real: it is reacting to a
    /// fraction of a unit. Outside it, a player has genuinely crossed to the other side of the enemy and
    /// the turn is applied as before. If the target cannot be found the write goes through: the worst this
    /// can do is decline to turn an enemy, never turn one wrongly.
    /// </summary>
    [HarmonyPatch(typeof(Entity), nameof(Entity.SetOrientation))]
    public class OrientationProbePatch
    {
        /// <summary>
        /// How close the enemy has to be, on the x axis, before a request to turn it around is treated as
        /// dithering rather than a real turn. The game's own band is one unit, and it is unreachable code.
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

            // Only a write that would turn the enemy AGAINST the direction the local target actually lies,
            // while the two are inside the band, is dithering. A write that agrees with the band is honest
            // and goes through.
            if (dx >= ORIENT_HYSTERESIS || wanted == orientation)
                return true;

            PerformanceProbe.NoteOrientationFlipSkipped();
            return false;
        }

        /// <summary>
        /// Measures the enemy against the x its own facing code uses, and says which way that implies.
        ///
        /// The x compared here is the LOCAL Penitent's, which is what Ghost's OnUpdate reads - it never
        /// consults Enemy.Target. Measuring against Target instead was an earlier mistake: that field is a
        /// GameObject written by dozens of other types, and against it the band simply never opened, so the
        /// guard would have looked present and done nothing.
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
