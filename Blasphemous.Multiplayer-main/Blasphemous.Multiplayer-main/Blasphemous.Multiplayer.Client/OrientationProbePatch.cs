using HarmonyLib;
using Gameplay.GameControllers.Entities;
using Blasphemous.Multiplayer.Client;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Measures orientation writes made by the GAME on enemies this machine does not own.
    ///
    /// This exists because the counter we had was measuring the wrong thing. That one sits inside
    /// ApplyOrientation, so it only sees this mod's own writes - about twenty per second - while the
    /// spinning that was reported comes from the game writing orientation every frame from an OnUpdate.
    /// The counter read zero over a whole session with both machines in the same spot, which proves it was
    /// blind rather than that the problem was gone.
    ///
    /// Entity.SetOrientation is the single setter every orientation write in the game goes through, so a
    /// prefix here sees all of them, whoever calls them. For now the prefix MEASURES ONLY: it never
    /// returns false, so nothing is blocked and behaviour is unchanged. If the spinning turns out to be
    /// real, turning it into a filter means returning false when the requested orientation disagrees with
    /// the current one by less than a dead band - which is why this is written as a prefix and not a
    /// postfix: a postfix would faithfully copy a jittering owner at sixty hertz.
    ///
    /// Only enemies this machine does NOT simulate are counted: those are the ones whose orientation is
    /// being fought over. The answer comes from a hash set the sync manager maintains where ownership
    /// changes, so this stays a cheap lookup on a path called many times per frame.
    /// </summary>
    [HarmonyPatch(typeof(Entity), nameof(Entity.SetOrientation))]
    public class OrientationProbePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Entity __instance)
        {
            // The Penitent is not an enemy and is never a puppet, so it falls out here.
            Enemy enemy = __instance as Enemy;
            if (enemy == null)
                return;

            PerformanceProbe.NoteGameOrientationWrite(
                EnemySyncManager.IsPuppet(enemy),
                EnemySyncManager.LabelOf(enemy));
        }
    }
}
