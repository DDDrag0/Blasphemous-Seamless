using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Blasphemous.ModdingAPI;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Temporary measurement rig for the animation investigation: reads numbers only and writes one
    /// line per report interval, no behaviour changes. Answers three questions the logs cannot: the
    /// penitent's Animator name (decides whether the ~130 player patches guarded by
    /// <c>animator.name == "Body"</c> skip the game's own walk/attack speed code), the real frame
    /// rate before and after connecting, and per-frame cost with GC counts. Remove this file and its
    /// call sites once the investigation is settled.
    /// </summary>
    internal static class PerformanceProbe
    {
        /// <summary>Seconds between reports. Frequent enough to catch a stall, rare enough not to flood.</summary>
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

        // Stall and animation-restart counters. A cut animation that never lands in a >100ms frame
        // exonerates the stall and points at the replay mechanism; repeats are the signature of a
        // forced replay, since a legitimate transition happens once.
        private static int _stalls;
        private static int _restarts;
        private static int _repeats;
        private static string _lastRestartId;
        private static int _lastRestartFrom;
        private static int _lastRestartTo;

        // Orientation changes applied per report, and how many the hysteresis declined to apply.
        private static int _orientFlips;
        private static int _orientFlipsSkipped;

        // Per-enemy tallies; labels carry the position because SPAWN labels permute between runs.
        private static readonly Dictionary<string, int> _orientByEnemy = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _gameOrientByEnemy = new Dictionary<string, int>();

        // Session totals, so the worst enemy is still reported after its busiest window has passed.
        private static readonly Dictionary<string, int> _orientTotalByEnemy = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _gameOrientTotalByEnemy = new Dictionary<string, int>();

        // Animation restarts per enemy, session totals only.
        private static readonly Dictionary<string, int> _animRestartsByEnemy = new Dictionary<string, int>();

        // State hashes already named in the log, so each is resolved once per session.
        private static readonly HashSet<int> _namedStates = new HashSet<int>();

        // Orientation writes made by the GAME on enemies this machine does not own - the number that
        // matters for the spinning enemy, since _orientFlips only sees this mod's own writes.
        private static int _gameOrientWrites;
        private static int _gameOrientWindowWrites;
        private static int _gameOrientBurst;

        /// <summary>
        /// Names an animation state hash the first time it is seen by asking the Animator for the
        /// clip currently playing: hashes are FNV-1a of names inside a packed AnimatorController and
        /// cannot be reversed from outside. Once per hash, so it costs a handful of lines per session.
        /// </summary>
        internal static void NameAnimationState(int stateHash, string clipName, string enemyLabel)
        {
            if (stateHash == 0 || string.IsNullOrEmpty(clipName))
                return;

            if (!_namedStates.Add(stateHash))
                return;

            ModLog.Info($"[AnimName] stato={stateHash} clip='{clipName}' primo-avvistamento={enemyLabel}");

            // The parry state name is not verified against the repository; the check fires only when
            // the clip contains "parry", so the marker cannot be wrong - only absent.
            if (clipName.IndexOf("parry", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ModLog.Info($"[Parry] stato={stateHash} clip='{clipName}' primo-avvistamento={enemyLabel}");
        }

        /// <summary>Counts one orientation change applied to an enemy, per enemy.</summary>
        internal static void NoteOrientationFlip(string enemyId)
        {
            _orientFlips++;
            Bump(_orientByEnemy, enemyId);
        }

        /// <summary>
        /// Counts one orientation change the hysteresis declined to apply. Together with the applied
        /// counter this shows how much of the dithering the guard absorbed.
        /// </summary>
        internal static void NoteOrientationFlipSkipped()
        {
            _orientFlipsSkipped++;
        }

        /// <summary>Increments a per-enemy counter, always, so the worst case is never lost by sampling.</summary>
        private static void Bump(Dictionary<string, int> counts, string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId))
                return;

            int current;
            counts.TryGetValue(enemyId, out current);
            counts[enemyId] = current + 1;
        }

        /// <summary>
        /// Counts one orientation write made by the GAME while this machine does not own the enemy.
        /// Fed from a Harmony prefix on <c>Entity.SetOrientation</c>, which only measures and never
        /// blocks a write, so behaviour is unchanged.
        /// </summary>
        internal static void NoteGameOrientationWrite(bool ownedByLocal, string enemyId)
        {
            // Only non-owned enemies are interesting: those are the ones that fight with the owner's
            // orientation and produce the visible oscillation.
            if (ownedByLocal)
                return;

            _gameOrientWrites++;
            Bump(_gameOrientByEnemy, enemyId);

            _gameOrientWindowWrites++;
            if (_gameOrientWindowWrites > _gameOrientBurst)
                _gameOrientBurst = _gameOrientWindowWrites;
        }

        /// <summary>
        /// Called when a state change is about to be reported with a playback position of zero, i.e.
        /// when this machine is about to restart a clip.
        /// </summary>
        internal static void NoteAnimationRestart(string id, int from, int to)
        {
            _restarts++;

            if (id == _lastRestartId && from == _lastRestartFrom && to == _lastRestartTo)
                _repeats++;

            _lastRestartId = id;
            _lastRestartFrom = from;
            _lastRestartTo = to;

            // Per enemy: totals alone cannot distinguish one enemy looping from several enemies each
            // doing it dozens of times. Labels carry the position, since SPAWN labels permute.
            Bump(_animRestartsByEnemy, id);
        }

        /// <summary>Starts timing EnemySyncManager.Update.</summary>
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

        /// <summary>Starts timing Multiplayer.OnLateUpdate.</summary>
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

            // unscaledDeltaTime: a pause or hit-stop must not report a fake frame rate.
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
                $"orientSkip={_orientFlipsSkipped} gameOrient={_gameOrientWrites} " +
                $"gameOrientBurst={_gameOrientBurst} " +
                $"exceptions={EnemySyncManager.SyncLoopExceptions} " +
                $"connected={Main.Multiplayer.NetworkManager.IsConnected}");

            ReportOrientationByEnemy();
            ReportAnimationsByEnemy();

            _frames = 0;
            _worstFrameMs = 0f;
            _worstSyncMs = 0f;
            _worstLateMs = 0f;
            _syncTicks = 0;
            _stalls = 0;
            _restarts = 0;
            _repeats = 0;
            _orientFlips = 0;
            _orientFlipsSkipped = 0;
            _gameOrientWrites = 0;
            _gameOrientWindowWrites = 0;
            _gameOrientBurst = 0;
        }

        /// <summary>
        /// Names the enemies whose orientation is churning, and how it was paid for. A total cannot
        /// say WHICH enemy dithers; labels carry the position, since SPAWN labels permute between
        /// runs. Kept for the whole session; only the worst entries are printed.
        /// </summary>
        private static void ReportOrientationByEnemy()
        {
            if (_orientByEnemy.Count == 0 && _gameOrientByEnemy.Count == 0)
                return;

            foreach (KeyValuePair<string, int> entry in _orientByEnemy)
            {
                int current;
                _orientTotalByEnemy.TryGetValue(entry.Key, out current);
                _orientTotalByEnemy[entry.Key] = current + entry.Value;
            }

            foreach (KeyValuePair<string, int> entry in _gameOrientByEnemy)
            {
                int current;
                _gameOrientTotalByEnemy.TryGetValue(entry.Key, out current);
                _gameOrientTotalByEnemy[entry.Key] = current + entry.Value;
            }

            _orientByEnemy.Clear();
            _gameOrientByEnemy.Clear();

            LogTopEnemies("[OrientByEnemy] nostre=", _orientTotalByEnemy);
            LogTopEnemies("[GameOrientByEnemy] gioco=", _gameOrientTotalByEnemy);
        }

        /// <summary>
        /// Prints which enemies collect animation restarts, worst first, once per report. A total
        /// misled once before: repetitions of one transition cannot be separated from several
        /// enemies doing it dozens of times each without naming the enemy.
        /// </summary>
        private static void ReportAnimationsByEnemy()
        {
            LogTopEnemies("[AnimByEnemy] avvii=", _animRestartsByEnemy);
        }

        /// <summary>Prints the busiest entries of a per-enemy tally, worst first.</summary>
        private static void LogTopEnemies(string prefix, Dictionary<string, int> totals)
        {
            if (totals.Count == 0)
                return;

            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(totals);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

            StringBuilder line = new StringBuilder(prefix);
            int shown = sorted.Count < TOP_ENEMIES_REPORTED ? sorted.Count : TOP_ENEMIES_REPORTED;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                    line.Append("  ");
                line.Append(sorted[i].Key).Append('=').Append(sorted[i].Value);
            }

            if (sorted.Count > shown)
                line.Append("  (+").Append(sorted.Count - shown).Append(" altri)");

            ModLog.Info(line.ToString());
        }

        /// <summary>How many enemies a per-enemy tally line names before summarising the rest.</summary>
        private const int TOP_ENEMIES_REPORTED = 4;

        /// <summary>
        /// Writes the Animator names once per scene. Decisive measurement: the "Body" patches on the
        /// player's StateMachineBehaviour callbacks skip the original code if the name does not match,
        /// and the string lives in scene data, not in Assembly-CSharp - so it can only be read at run
        /// time.
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
                    ModLog.Error("[Probe] Penitent Animator not available after 60 frames");
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
                            ? "MATCH: they are not skipping anything, the cause is elsewhere"
                            : "DO NOT MATCH: they are skipping the original bodies - this is the cause"));
        }

        /// <summary>Called from EnemySyncManager.LevelLoaded so the names are re-read on a scene change.</summary>
        internal static void ResetForScene()
        {
            _namesReported = false;
            _animatorMissing = 0;
        }
    }
}