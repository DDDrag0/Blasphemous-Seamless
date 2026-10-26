using System;
using System.Globalization;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Displays an on-screen warning to dead players during a boss death cutscene.
    /// Prevents pressing any key, which would otherwise trigger the game's DeadScreenWidget
    /// and break the respawn fade sequence. Relies on public Cinematics events instead of Harmony patches.
    /// </summary>
    internal static class BossDeathWarning
    {
        private const string MARK = "[BossWarn]";

        /// <summary>
        /// Toggle to enable/disable the warning. Uses static readonly instead of const 
        /// to prevent CS0162 "unreachable code" compiler warnings.
        /// </summary>
        private static readonly bool WARN_PLAYER_ON_BOSS_DEATH = true;

        private const string TEXT = "DO NOT PRESS: the boss is dying - the cutscene is about to start";

        // ============================================================================================
        // CUTSCENE WINDOW SETTINGS
        // ============================================================================================

        private const float CINEMATIC_ARM_TIMEOUT = 60f;
        private const float CINEMATIC_WINDOW_MAX = 45f;
        private const int MAX_SHOWS_PER_WINDOW = 12;
        private const float REFRESH_FRACTION = 0.8f;
        private const float DEFAULT_NOTIFICATION_SECONDS = 4f;

        private static bool _dead;
        private static int _windows;
        private static int _warnings;

        private static bool _toggleOffReported;
        private static bool _dedupReported;

        private static bool _armed;
        private static float _armedAt;
        private static string _armedWhere = string.Empty;

        private static bool _cinematicOpen;
        private static float _cinematicStartedAt;

        private static bool _shownThisWindow;
        private static float _lastShownAt;
        private static int _showsThisWindow;
        private static bool _capReported;
        private static bool _aliveReported;
        private static bool _noManagerReported;

        private static bool _subscribed;
        private static bool _subscribeFailureReported;

        /// <summary>
        /// True while the cutscene window is open. Checked by BossFightDeathWarning 
        /// to prevent overlapping notifications.
        /// </summary>
        internal static bool CutsceneWindowOpen
        {
            get { return _cinematicOpen; }
        }

        /// <summary>
        /// Executed every frame. Manages the death window, cinematic window, and notification refreshing.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                if (!_subscribed)
                    Subscribe();

                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (dead && !_dead)
                {
                    _windows++;
                    ModLog.Info(MARK + " death window #" + _windows + " OPENED " +
                                "(PlayerDeathSync.IsLocalPlayerDead()=True): warning armed for the next cutscene");
                }
                else if (!dead && _dead)
                {
                    ModLog.Info(MARK + " death window #" + _windows + " CLOSED (local player is alive): " +
                                "warning available for next death" +
                                (_shownThisWindow
                                    ? " (warnings shown this window: " + _showsThisWindow + ")"
                                    : " (no warnings shown this window)"));
                }

                _dead = dead;

                if (!dead)
                {
                    if (_armed && !_shownThisWindow)
                    {
                        ModLog.Info(MARK + " cutscene window DISARMED: player respawned before cutscene ⇒ no notification · armata-da=" +
                                    _armedWhere);
                    }

                    _armed = false;
                    _shownThisWindow = false;
                    _dedupReported = false;
                }

                if (_armed && !_cinematicOpen && Time.unscaledTime - _armedAt > CINEMATIC_ARM_TIMEOUT)
                {
                    _armed = false;
                    ModLog.Info(MARK + " cutscene window CLOSED WITHOUT CUTSCENE (no cutscene boss) " +
                                "· t=" + F(Time.unscaledTime) + " · armata-da=" + _armedWhere + " a t=" + F(_armedAt) +
                                " · no CinematicStarted in " + CINEMATIC_ARM_TIMEOUT.ToString("F0") +
                                "s ⇒ no notification needed");
                }

                if (_cinematicOpen)
                {
                    if (!_shownThisWindow)
                    {
                        _shownThisWindow = true;
                        Show("cutscene:CinematicStarted");
                    }
                    else if (_showsThisWindow >= MAX_SHOWS_PER_WINDOW)
                    {
                        if (!_capReported)
                        {
                            _capReported = true;
                            ModLog.Info(MARK + " cap of " + MAX_SHOWS_PER_WINDOW + " notifications reached: " +
                                        "stopping refresh (window will close at CinematicEnded)");
                        }
                    }
                    else if (Time.unscaledTime - _lastShownAt >= NotificationSeconds() * REFRESH_FRACTION)
                    {
                        Show("cutscene:refresh");
                    }

                    if (Time.unscaledTime - _cinematicStartedAt > CINEMATIC_WINDOW_MAX)
                    {
                        ModLog.Info(MARK + " cutscene window FORCE CLOSED: CinematicEnded did not arrive within " +
                                    CINEMATIC_WINDOW_MAX.ToString("F0") + "s (t=" +
                                    F(Time.unscaledTime) + ", aperta a t=" + F(_cinematicStartedAt) + "): " +
                                    "stopping refresh");
                        CloseWindow("timeout");
                    }
                }
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Arms the warning system. Called when the local instance registers a boss death.
        /// The notification is delayed until the actual CinematicStarted event.
        /// </summary>
        internal static void WarnBossDied(string where)
        {
            try
            {
                if (!WARN_PLAYER_ON_BOSS_DEATH)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " warning DISABLED by toggle " +
                                    "(WARN_PLAYER_ON_BOSS_DEATH=false): no on-screen notification · dove=" +
                                    where);
                    }
                    return;
                }
                
                if (!PlayerDeathSync.IsLocalPlayerDead())
                {
                    ModLog.Info(MARK + " warning NOT shown: local player is ALIVE " +
                                "(PlayerDeathSync.IsLocalPlayerDead()=False, no dead screen to protect) · dove=" + where);
                    return;
                }

                if (_armed || _shownThisWindow)
                {
                    if (!_dedupReported)
                    {
                        _dedupReported = true;
                        ModLog.Info(MARK + " warning ALREADY ARMED for this death window " +
                                    "· dove=" + where +
                                    " · gia-armato-da=" + _armedWhere);
                    }
                    return;
                }

                _armed = true;
                _armedAt = Time.unscaledTime;
                _armedWhere = where;
                _dedupReported = false;

                ModLog.Info(MARK + " cutscene window ARMED (death window #" + _windows + ") " +
                            "· t=" + F(Time.unscaledTime) + " · armata-da=" + where +
                            " · predicato=PlayerDeathSync.IsLocalPlayerDead()=True · " +
                            "⚠️ NO NOTIFICATION YET: will show at CinematicStarted. " +
                            "If no cutscene within " + CINEMATIC_ARM_TIMEOUT.ToString("F0") +
                            "s, window closes without warning");

                if (_cinematicOpen && !_shownThisWindow)
                {
                    _shownThisWindow = true;
                    Show("bossdied:cutscene-already-active");
                }
            }
            catch (Exception e)
            {
                Fail("WarnBossDied", e);
            }
        }

        /// <summary>
        /// Displays the notification if the local player is currently dead.
        /// Uses unscaled time for timestamps since cutscenes might run with Time.timeScale = 0.
        /// </summary>
        private static void Show(string trigger)
        {
            try
            {
                _lastShownAt = Time.unscaledTime;

                if (!WARN_PLAYER_ON_BOSS_DEATH)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " warning DISABLED by toggle " +
                                    "(WARN_PLAYER_ON_BOSS_DEATH=false): no on-screen notification · dove=" +
                                    trigger);
                    }
                    return;
                }

                if (!PlayerDeathSync.IsLocalPlayerDead())
                {
                    if (!_aliveReported)
                    {
                        _aliveReported = true;
                        ModLog.Info(MARK + " warning NOT shown: local player is ALIVE " +
                                    "(PlayerDeathSync.IsLocalPlayerDead()=False): no dead screen to protect · dove=" + trigger);
                    }
                    return;
                }

                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                {
                    if (!_noManagerReported)
                    {
                        _noManagerReported = true;
                        ModLog.Error(MARK + " warning NOT shown: NotificationManager unavailable · " +
                                     "dove=" + trigger);
                    }
                    return;
                }

                _showsThisWindow++;
                _warnings++;

                Main.Multiplayer.NotificationManager.DisplayNotification(TEXT);

                ModLog.Info(MARK + " ON-SCREEN WARNING #" + _warnings + " (death window #" + _windows +
                            ", show " + _showsThisWindow + "/" + MAX_SHOWS_PER_WINDOW + " of this " +
                            "cutscene) · \"" + TEXT + "\" · dove=" + trigger + " · armato-da=" + _armedWhere +
                            " · predicato=PlayerDeathSync.IsLocalPlayerDead()=True " +
                            "· durata=" + DurationSeconds() + "s (config.notificationDisplaySeconds)");
            }
            catch (Exception e)
            {
                Fail("Show", e);
            }
        }

        private static void Subscribe()
        {
            try
            {
                Cinematics cinematics = Core.Cinematics;
                if (cinematics == null)
                {
                    if (!_subscribeFailureReported)
                    {
                        _subscribeFailureReported = true;
                        ModLog.Info(MARK + " Core.Cinematics not ready: retrying every frame");
                    }
                    return;
                }

                if (_subscribed)
                    return;

                cinematics.CinematicStarted += OnCinematicStarted;
                cinematics.CinematicEnded += OnCinematicEnded;

                _subscribed = true;
                ModLog.Info(MARK + " cutscene window HOOKED: subscribed to CinematicStarted/CinematicEnded");
            }
            catch (Exception e)
            {
                Fail("Subscribe", e);
            }
        }

        private static void OnCinematicStarted()
        {
            try
            {
                _cinematicOpen = true;
                _cinematicStartedAt = Time.unscaledTime;

                _shownThisWindow = false;
                _showsThisWindow = 0;
                _capReported = false;
                _aliveReported = false;
                _noManagerReported = false;

                ModLog.Info(MARK + " cutscene STARTED (CinematicStarted) t=" + F(Time.unscaledTime) +
                            ": warning window OPEN · armata=" + (_armed ? "YES" : "NO") +
                            (_armed ? " (armata-da=" + _armedWhere + " a t=" + F(_armedAt) + ")" : string.Empty) +
                            (_armed ? " ⇒ warning will show" : " ⇒ no warning needed"));
            }
            catch (Exception e)
            {
                Fail("OnCinematicStarted", e);
            }
        }

        private static void OnCinematicEnded(bool cancelled)
        {
            try
            {
                ModLog.Info(MARK + " cutscene ENDED (CinematicEnded cancelled=" + cancelled + ") t=" +
                            F(Time.unscaledTime) + ": warning window CLOSED · showsThisWindow=" + _showsThisWindow +
                            (cancelled ? " · ⚠️ CANCELLED: a key press interrupted the cutscene" : string.Empty));
                CloseWindow("CinematicEnded");
            }
            catch (Exception e)
            {
                Fail("OnCinematicEnded", e);
            }
        }

        private static void CloseWindow(string reason)
        {
            _cinematicOpen = false;
            _armed = false;
            _shownThisWindow = false;
            _showsThisWindow = 0;
            _capReported = false;
            _aliveReported = false;
            _noManagerReported = false;

            ModLog.Info(MARK + " cutscene window CLOSED (" + reason + "): next boss death will arm a new window");
        }

        private static float NotificationSeconds()
        {
            try
            {
                if (Main.Multiplayer != null && Main.Multiplayer.config != null)
                {
                    float seconds = Main.Multiplayer.config.notificationDisplaySeconds;
                    if (seconds > 0.2f)
                        return seconds;
                }
            }
            catch (Exception) { }

            return DEFAULT_NOTIFICATION_SECONDS;
        }

        private static string DurationSeconds()
        {
            return NotificationSeconds().ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " EXCEPTION in " + where + ": " + e.GetType().Name + " - " + e.Message);
            }
            catch (Exception) { }
        }
    }
}