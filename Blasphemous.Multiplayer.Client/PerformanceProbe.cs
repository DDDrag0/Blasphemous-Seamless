using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
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

        // Orientation changes applied per report, and how many the hysteresis declined to apply. Nothing in
        // the log measured orientation before, so the fix for the spinning enemy would otherwise have to be
        // judged by eye: the measured before-state was 31 changes in 1.7 s on one machine and 106 in 10.4 s
        // on the other, all with the enemy standing still.
        private static int _orientFlips;
        private static int _orientFlipsSkipped;

        // Per enemy, keyed by a label carrying the position, because SPAWN (NN) labels are permuted between
        // runs and a total would not say WHICH enemy dithers - which is the thing needed to judge the band.
        private static readonly Dictionary<string, int> _orientByEnemy = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _gameOrientByEnemy = new Dictionary<string, int>();

        // Session totals, so the worst enemy is still reported after its busiest window has passed.
        private static readonly Dictionary<string, int> _orientTotalByEnemy = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _gameOrientTotalByEnemy = new Dictionary<string, int>();

        // Animation restarts per enemy, so the churn can be attributed. Sessions totals only: the point is
        // to name the worst enemy over the whole run, and per-window counts reset before it can be read.
        private static readonly Dictionary<string, int> _animRestartsByEnemy = new Dictionary<string, int>();

        // State hashes already named in the log, so each one is resolved once per session.
        private static readonly HashSet<int> _namedStates = new HashSet<int>();

        // Orientation writes made by the GAME on enemies this machine does not own. This is the number that
        // matters for the spinning enemy; _orientFlips above only sees this mod's own writes.
        private static int _gameOrientWrites;
        private static int _gameOrientWindowWrites;
        private static int _gameOrientBurst;

        /// <summary>
        /// Names an animation state hash the first time it is seen, by asking the Animator for the clip
        /// currently playing.
        ///
        /// Written because the hash cannot be reversed from the outside. Animator state hashes are FNV-1a
        /// of the state name, but those names live in the AnimatorController, which is a packed Unity asset
        /// - not in Assembly-CSharp, and not in any file small enough to read. Hashing every string in the
        /// assembly produced no match for 765711723, 2081823275 or 460696939, so the name has to come from
        /// the running game instead of from a file.
        ///
        /// Once per hash, so it costs a handful of lines in the whole session and cannot flood. The name
        /// settles the question the numbers could not: whether the state that is entered so often is an
        /// attack, in which case hundreds of entries are hundreds of attacks and nothing is wrong, or an
        /// idle, in which case something is putting the enemy back into it.
        /// </summary>
        internal static void NameAnimationState(int stateHash, string clipName, string enemyLabel)
        {
            if (stateHash == 0 || string.IsNullOrEmpty(clipName))
                return;

            if (!_namedStates.Add(stateHash))
                return;

            ModLog.Info($"[AnimName] stato={stateHash} clip='{clipName}' primo-avvistamento={enemyLabel}");

            // The parry state of an enemy has a name we have NOT verified against the repository - a
            // hardcoded hash here would silently never match. Instead the name is resolved at runtime and
            // this line fires only when the clip genuinely contains "parry", so the marker cannot be
            // wrong, it can only be absent. Once a session shows the real name, the gate can be tightened.
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
        /// Counts one orientation change the hysteresis declined to apply. Together with the counter above
        /// this says how much of the dithering the guard absorbed.
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
        ///
        /// The counter above only sees this mod's own writes, at 20 Hz - and the spinning that was reported
        /// comes from the game writing orientation every frame, which that counter cannot see at all. So
        /// this one is fed from a Harmony prefix on Entity.SetOrientation, the single setter every
        /// orientation write in the game goes through.
        ///
        /// The prefix measures and does nothing else: it never blocks a write, so behaviour is unchanged.
        /// If the spinning turns out to be real, turning this into a filter is a matter of returning false
        /// when the two orientations disagree by less than a dead band.
        /// </summary>
        internal static void NoteGameOrientationWrite(bool ownedByLocal, string enemyId)
        {
            // Only writes on enemies that this machine is NOT simulating are interesting: those are the
            // ones that fight with the owner's orientation and produce the visible oscillation.
            if (ownedByLocal)
                return;

            _gameOrientWrites++;
            Bump(_gameOrientByEnemy, enemyId);

            // Track the busiest report window, so the log says both "how many in total" and "how bad it got".
            _gameOrientWindowWrites++;
            if (_gameOrientWindowWrites > _gameOrientBurst)
                _gameOrientBurst = _gameOrientWindowWrites;
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

            // Per enemy, because a total could not be acted on: 386 repetitions of one transition looked
            // like a single enemy in a loop, but counting without separating the enemies cannot tell that
            // apart from several enemies doing it dozens of times each. The label carries the POSITION,
            // since SPAWN (NN) labels are permuted between runs.
            Bump(_animRestartsByEnemy, id);
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
        /// Names the enemies whose orientation is churning, and how it was paid for.
        ///
        /// Worth its own line because a total cannot say WHICH enemy dithers, and the SPAWN (NN) labels
        /// permute between runs - so the label carries the position instead. It says how many writes the
        /// game made and how many of ours the hysteresis declined, which is the pair that shows whether the
        /// guard is absorbing real dithering or nothing at all.
        ///
        /// The per-enemy tallies are kept for the whole session and only the worst few are printed, so the
        /// output stays readable without losing the peak.
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
        /// Prints which enemies collect the animation restarts, worst first, once per report.
        ///
        /// Written after a total misled: 386 repetitions of a single transition, counted without separating
        /// the enemies, cannot be told apart from one enemy looping and several enemies each doing it dozens
        /// of times. The label carries the position, because SPAWN (NN) labels permute between runs.
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
