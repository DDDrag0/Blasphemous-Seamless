using System.Diagnostics;
using UnityEngine;
using Blasphemous.ModdingAPI;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Temporary measurement rig for the animation investigation.
    ///
    /// Nothing here changes behaviour: it only reads numbers and writes one line every couple of
    /// seconds. It exists to answer three questions that the logs could not, before any code is
    /// changed on the strength of a guess:
    ///
    ///  1. What is Core.Logic.Penitent.Animator.name?
    ///     This is the one that matters most. AnimationFixes.cs installs around 130 Harmony prefixes
    ///     on the player's StateMachineBehaviour callbacks, each guarded by `animator.name == "Body"`.
    ///     Those callbacks are void methods, and a bool prefix returning false SKIPS the original - so
    ///     if the name is not exactly "Body" the patch skips the very code that sets the walk and
    ///     attack speeds (MoveAnimationBehaviour sets speed to 1, GroundAttackSubStateBehaviour sets
    ///     the attack speed). That would explain chopped walking and combat on an actor the enemy
    ///     sync never touches. The string "Body" does not exist in Assembly-CSharp.dll: it is a scene
    ///     or prefab name, so this can only be read at run time.
    ///
    ///  2. What is the real frame rate, and did it change when the session connected?
    ///     No log line has ever recorded it.
    ///
    ///  3. How much does the mod cost per frame, and is it allocating?
    ///     EnemySyncManager.Update and Multiplayer.OnLateUpdate are timed with a Stopwatch, and the
    ///     GC counters say whether the per-message allocations in ProcessQueue are being collected
    ///     during play.
    ///
    /// Remove this file (and its two call sites) once the question is settled.
    /// </summary>
    internal static class PerformanceProbe
    {
        /// <summary>Seconds between reports. Two is frequent enough to catch a stall, rare enough not to flood.</summary>
        private const float REPORT_INTERVAL = 2f;

        private static readonly Stopwatch _syncTimer = new Stopwatch();
        private static readonly Stopwatch _lateTimer = new Stopwatch();

        private static float _timer;
        private static int _frames;

        // Peaks since the last report, so a single bad frame is not averaged away.
        private static float _worstFrameMs;
        private static float _worstSyncMs;
        private static float _worstLateMs;
        private static long _syncTicks;

        private static int _lastCollectionCount;
        private static bool _namesReported;

        private static int _animatorMissing;

        // Stall and animation-restart counters, so the next test separates the two candidate causes in
        // its own log instead of needing another round.
        //
        // stalls: frames slower than a tenth of a second. If a cut animation never lands in one of these
        // frames, the stall is innocent and the cause is the replay mechanism.
        // restarts: reports where the playback position arrived as 0, i.e. the receiver was told to start
        // the clip over. repeats: the same enemy repeating the same hash pair back to back, which is the
        // real signature of a forced replay - a legitimate transition happens once.
        private static int _stalls;
        private static int _restarts;
        private static int _repeats;
        private static string _lastRestartId;
        private static int _lastRestartFrom;
        private static int _lastRestartTo;

        // Orientation changes applied per report. Nothing in the log measures orientation today, so the
        // fix for the spinning enemy would otherwise have to be judged by eye: the measured before-state
        // was 31 changes in 1.7 s on one machine and 106 in 10.4 s on the other, all with the enemy
        // standing still.
        private static int _orientFlips;

        /// <summary>Counts one orientation change applied to an enemy. Called from ApplyOrientation.</summary>
        internal static void NoteOrientationFlip()
        {
            _orientFlips++;
        }

        /// <summary>
        /// Called by LogAnimationChanges when a state change is about to be reported with a playback
        /// position of zero, i.e. when this machine is about to restart a clip.
        /// </summary>
        internal static void NoteAnimationRestart(string id, int from, int to)
        {
            _restarts++;

            if (id == _lastRestartId && from == _lastRestartFrom && to == _lastRestartTo)
                _repeats++;

            _lastRestartId = id;
            _lastRestartFrom = from;
            _lastRestartTo = to;
        }

        /// <summary>Starts timing EnemySyncManager.Update. Call at the top of that method.</summary>
        internal static void BeginSync()
        {
            _syncTimer.Reset(); _syncTimer.Start();
        }

        /// <summary>Stops timing EnemySyncManager.Update. Call at every exit of that method.</summary>
        internal static void EndSync()
        {
            _syncTimer.Stop();
            double ms = _syncTimer.Elapsed.TotalMilliseconds;
            _syncTicks += _syncTimer.ElapsedTicks;

            if (ms > _worstSyncMs)
                _worstSyncMs = (float)ms;
        }

        /// <summary>Starts timing Multiplayer.OnLateUpdate. Call at the top of that method.</summary>
        internal static void BeginLate()
        {
            _lateTimer.Reset(); _lateTimer.Start();
        }

        /// <summary>Stops timing Multiplayer.OnLateUpdate. Call at every exit of that method.</summary>
        internal static void EndLate()
        {
            _lateTimer.Stop();
            double ms = _lateTimer.Elapsed.TotalMilliseconds;
            if (ms > _worstLateMs)
                _worstLateMs = (float)ms;
        }

        /// <summary>
        /// Counts one frame and, every REPORT_INTERVAL, writes the report. Call once per frame from
        /// EnemySyncManager.Update, which already runs on both machines whether connected or not.
        /// </summary>
        internal static void Frame()
        {
            _frames++;

            // unscaledDeltaTime, so a pause or a hit-stop does not report a fake frame rate. It is
            // still what the game's own animation stepping follows, so it is the honest number here.
            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (frameMs > _worstFrameMs)
                _worstFrameMs = frameMs;
            if (frameMs > 100f)
                _stalls++;

            ReportNamesOnce();

            _timer += Time.unscaledDeltaTime;
            if (_timer < REPORT_INTERVAL)
                return;

            float seconds = _timer;
            _timer = 0f;

            float fps = _frames / seconds;
            int collections = System.GC.CollectionCount(0);
            int collectionsSince = collections - _lastCollectionCount;
            _lastCollectionCount = collections;

            long managedBytes = System.GC.GetTotalMemory(false);

            ModLog.Info(
                $"[Probe] fps={fps:F1} frameMs_max={_worstFrameMs:F1} stalls100={_stalls} " +
                $"syncMs_avg={_syncTicks / 10000.0 / _frames:F2} syncMs_max={_worstSyncMs:F1} " +
                $"lateMs_max={_worstLateMs:F1} gc0={collectionsSince} managedMB={managedBytes / 1048576.0:F1} " +
                $"animRestart={_restarts} animRepeat={_repeats} orientFlip={_orientFlips} " +
                $"connected={Main.Multiplayer.NetworkManager.IsConnected}");

            _frames = 0;
            _worstFrameMs = 0f;
            _worstSyncMs = 0f;
            _worstLateMs = 0f;
            _syncTicks = 0;
            _stalls = 0;
            _restarts = 0;
            _repeats = 0;
            _orientFlips = 0;
        }

        /// <summary>
        /// Writes the Animator names once per scene. This is the decisive measurement: see the class
        /// comment for why the name decides whether the 130 player patches are skipping the game's own
        /// walk and attack speed code.
        /// </summary>
        private static void ReportNamesOnce()
        {
            if (_namesReported)
                return;

            var penitent = Core.Logic.Penitent;
            if (penitent == null || penitent.Animator == null)
            {
                // Only complain once the level is really up, otherwise this fires during the menu.
                if (Main.Multiplayer.CurrentlyInLevel && ++_animatorMissing == 60)
                    ModLog.Error("[Probe] Animator del Penitente non disponibile dopo 60 frame");
                return;
            }

            _namesReported = true;

            string penitentName = penitent.Animator.name;
            string bodyName = penitent.Animator.gameObject.name;
            string rootName = penitent.gameObject.name;

            ModLog.Info($"[Probe] PENITENT animator.name='{penitentName}' " +
                        $"animatorGameObject='{bodyName}' root='{rootName}'");

            // The patches compare against "Body", so say outright whether they can be firing.
            bool matches = penitentName == "Body";
            ModLog.Info($"[Probe] Le 130 patch su animator.name == \"Body\" " +
                        (matches
                            ? "CORRISPONDONO: non stanno saltando nulla, la causa e' altrove"
                            : "NON CORRISPONDONO: stanno saltando i corpi originali - e' questa la causa"));
        }

        /// <summary>Called from EnemySyncManager.LevelLoaded so the names are re-read on a scene change.</summary>
        internal static void ResetForScene()
        {
            _namesReported = false;
            _animatorMissing = 0;
        }
    }
}
