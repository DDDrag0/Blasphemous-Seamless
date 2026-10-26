using System;
using System.Globalization;
using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Displays an on-screen warning to the local player if they die during an active boss fight,
    /// advising them to wait for teammates rather than respawning immediately (which would trap the
    /// remaining players in an unclosable boss room).
    /// 
    /// Evaluates the death transition, the presence of live bosses, and the connected player count
    /// on the fly without requiring Harmony patches. Suppresses itself if BossDeathWarning's cutscene
    /// window is currently active.
    /// </summary>
    internal static class BossFightDeathWarning
    {
        /// <summary>
        /// Log marker, deliberately distinct from BossDeathWarning to allow separate tracking.
        /// </summary>
        private const string MARK = "[DeathWarn]";

        /// <summary>
        /// Toggle to enable/disable this specific warning. Uses static readonly instead of const 
        /// to prevent CS0162 "unreachable code" compiler warnings.
        /// </summary>
        private static readonly bool WARN_ON_DEATH_IN_BOSSFIGHT = true;

        private const string TEXT =
            "WAIT FOR YOUR TEAMMATES: do not respawn - the boss fight is still in progress";

        // ---- Death window front: true if local player is dead
        private static bool _dead;

        // ---- Prevents multiple warnings/logs in a single death window (e.g. during load transitions)
        private static bool _warnedThisWindow;

        // ---- Diagnostics
        private static bool _toggleOffReported;
        private static int _windows;
        private static int _warnings;

        /// <summary>
        /// Executed every frame. Checks for the local player's alive-to-dead transition.
        /// Wrapped in try/catch to ensure exceptions never break the frame cycle.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (dead && !_dead)
                {
                    _windows++;
                    Warn();
                }

                _dead = dead;

                if (!dead)
                    _warnedThisWindow = false;
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Evaluates all conditions and displays the warning if met. 
        /// Logs every early exit to maintain full observability.
        /// </summary>
        private static void Warn()
        {
            try
            {
                if (_warnedThisWindow)
                {
                    ModLog.Info(MARK + " warning ALREADY decided for death window #" + _windows +
                                " (one per window): no second warning");
                    return;
                }

                _warnedThisWindow = true;

                if (!WARN_ON_DEATH_IN_BOSSFIGHT)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " warning DISABLED by toggle " +
                                    "(WARN_ON_DEATH_IN_BOSSFIGHT=false): no on-screen notification · " +
                                    "death window #" + _windows);
                    }
                    return;
                }

                // Check for live bosses in the current scene
                int bossVivi = EnemySyncManager.CountLiveBossesInScene();
                if (bossVivi <= 0)
                {
                    ModLog.Info(MARK + " warning NOT shown: OUTSIDE bossfight " +
                                "(EnemySyncManager.CountLiveBossesInScene()=0, no live bosses) · " +
                                "death window #" + _windows);
                    return;
                }

                // Ensure there is at least one connected teammate, otherwise the text makes no sense
                int compagni = EnemySyncManager.ConnectedPlayersCount();
                if (compagni <= 0)
                {
                    ModLog.Info(MARK + " warning NOT shown: no teammates connected " +
                                "(EnemySyncManager.ConnectedPlayersCount()=0) · bossVivi=" + bossVivi +
                                " · death window #" + _windows);
                    return;
                }

                // Prevent overlap with BossDeathWarning
                if (BossDeathWarning.CutsceneWindowOpen)
                {
                    ModLog.Info(MARK + " warning NOT shown: BossDeathWarning cutscene window is OPEN " +
                                "(preventing overlap) · bossVivi=" + bossVivi +
                                " · death window #" + _windows);
                    return;
                }

                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                {
                    ModLog.Error(MARK + " warning NOT shown: NotificationManager unavailable · " +
                                 "bossVivi=" + bossVivi + " · death window #" + _windows);
                    return;
                }

                _warnings++;

                Main.Multiplayer.NotificationManager.DisplayNotification(TEXT);

                ModLog.Info(MARK + " ON-SCREEN WARNING #" + _warnings + " (death window #" + _windows +
                            ") · \"" + TEXT + "\" · dove=local player transition alive->dead " +
                            "(PlayerDeathSync.IsLocalPlayerDead()=True) · " +
                            "predicato in-bossfight=EnemySyncManager.CountLiveBossesInScene()=" + bossVivi +
                            " · compagniConnessi=" + compagni + " · durata=" + DurationSeconds() + "s " +
                            "(config.notificationDisplaySeconds)");
            }
            catch (Exception e)
            {
                Fail("Warn", e);
            }
        }

        /// <summary>
        /// Reads the display duration directly from the configuration.
        /// </summary>
        private static string DurationSeconds()
        {
            try
            {
                if (Main.Multiplayer != null && Main.Multiplayer.config != null)
                {
                    return Main.Multiplayer.config.notificationDisplaySeconds
                        .ToString("0.0", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception) { }

            return "?";
        }

        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " EXCEPTION in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (warning bypassed, player death proceeds safely)");
            }
            catch (Exception) { }
        }
    }
}