using System;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.UI;
using HarmonyLib;
using Tools.Level.Layout;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Keeps the world running while the game is paused, but only when connected to a server.
    /// The game's own pause sets <c>TimeScaleReal</c> to 0 and <c>LevelInitializer.Update</c>
    /// copies that into <c>Time.timeScale</c> every frame. With <c>timeScale = 0</c> this machine
    /// stops simulating, so any enemies/bosses it owns stop for the other player too. Hooking the
    /// single write point is enough: no changes to <c>TimeScaleReal</c> (the game must believe it
    /// is paused), no PauseGame/ResumeGame, no menu touched. Pause state is read from
    /// <c>UIController.Paused</c>, which is the only predicate this patch cannot falsify.
    /// Single player: nothing runs, the game pauses as usual.
    /// </summary>
    /// <remarks>
    /// The patch is declared with two attribute usages (type and method), the form Harmony
    /// documents; <c>PatchAll</c> applies it the same as the single-line form.
    /// </remarks>
    [HarmonyPatch(typeof(LevelInitializer))]
    [HarmonyPatch("Update")]
    internal static class LevelInitializerUpdate_TimeWhilePaused_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            CoopTimeWhilePaused.AfterLevelInitializerUpdate();
        }
    }

    /// <summary>
    /// Pause-time decision in one place. Three questions, in order, any of which can stop the
    /// work: gate (is there a server), real pause (UIController.Paused), and the copy.
    /// </summary>
    internal static class CoopTimeWhilePaused
    {
        /// <summary>Outcome marker.</summary>
        private const string MARK = "[Tempo]";

        /// <summary>
        /// Function toggle. static readonly (not const) so the early-return branch stays reachable
        /// and the compiler does not emit CS0162.
        /// </summary>
        private static readonly bool TIME_RUNS_WHILE_PAUSED = true;

        /// <summary>
        /// Gate state: 0 = never observed, 1 = co-op, 2 = single player. Used to write one line per
        /// transition instead of one per frame.
        /// </summary>
        private static int _gateState;

        /// <summary>True while inside a pause window whose timescale we had to correct.</summary>
        private static bool _windowOpen;

        /// <summary>Pause windows corrected, and frames corrected.</summary>
        private static int _windows;
        private static int _frames;

        /// <summary>
        /// Single decision point, called from the LevelInitializer.Update postfix. This is the only
        /// place in the mod that writes <c>Time.timeScale</c>.
        /// </summary>
        internal static void AfterLevelInitializerUpdate()
        {
            try
            {
                if (!TIME_RUNS_WHILE_PAUSED)
                    return;

                if (!CoopGate.IsMultiplayerActive())
                {
                    SinglePlayer();
                    return;
                }

                GateOpened();

                if (!IsReallyPaused())
                {
                    PausaFinita();
                    return;
                }

                // The game just copied TimeScaleReal (0 while paused) into Time.timeScale.
                // TimeScaleReal is NOT touched: the game must still believe it is paused.
                Time.timeScale = 1f;

                _frames++;

                if (_windowOpen)
                    return;

                _windowOpen = true;
                _windows++;

                ModLog.Info(MARK + " pause does not stop the world · world keeps running while paused ·" +
                            " TimeScaleReal(letto)=" + F(TimeScaleReal()) + " (the game believes it is paused)" +
                            " · Time.timeScale=" + F(Time.timeScale) + " (what actually simulates, rewritten here)" +
                            " · finestre=" + _windows +
                            " · cancello=aperto · no PauseGame/ResumeGame, no menu touched");
            }
            catch (Exception e)
            {
                Fail("AfterLevelInitializerUpdate", e);
            }
        }

        /// <summary>
        /// Coop gate closed: the game keeps control of time. One line per transition into single
        /// player, so the reason for the change is visible.
        /// </summary>
        private static void SinglePlayer()
        {
            try
            {
                _windowOpen = false;

                if (_gateState != 2)
                {
                    bool prima = _gateState == 1;
                    _gateState = 2;
                    ModLog.Info(MARK + " cancello CHIUSO (no server): time belongs to the game - no write" +
                                " to Time.timeScale, pause stops the world as usual" +
                                (prima ? " · connection dropped at this instant" : ""));
                }
            }
            catch (Exception e)
            {
                Fail("SinglePlayer", e);
            }
        }

        /// <summary>Coop gate opened: one line to report the switch and current local pause state.</summary>
        private static void GateOpened()
        {
            try
            {
                if (_gateState == 1)
                    return;

                _gateState = 1;
                ModLog.Info(MARK + " cancello APERTO (server connected: host or peer): from here, in pause," +
                            " Time.timeScale stays 1 - the world runs and this machine's enemies no longer" +
                            " stop for the other player · stato locale=" +
                            (IsReallyPaused() ? "pausa" : "in gioco"));
            }
            catch (Exception e)
            {
                Fail("GateOpened", e);
            }
        }

        /// <summary>
        /// The pause ended: close the window and write one line. The game's own copy is authoritative
        /// again, so nothing else is written.
        /// </summary>
        private static void PausaFinita()
        {
            try
            {
                if (!_windowOpen)
                    return;

                _windowOpen = false;
                ModLog.Info(MARK + " pause ended: the game's copy takes over again" +
                            " · finestre totali=" + _windows + " · frame corretti=" + _frames +
                            " · Time.timeScale=" + F(Time.timeScale) + " · TimeScaleReal(letto)=" + F(TimeScaleReal()));
            }
            catch (Exception e)
            {
                Fail("PausaFinita", e);
            }
        }

        /// <summary>
        /// Real pause state via <c>UIController.Paused</c> (which reads TimeScaleReal). Not
        /// <c>Time.timeScale</c> (circular: this patch rewrites it) and not <c>IsSlowMode()</c> (it
        /// lies after this patch). Never throws; on failure answers "not paused" - the inert state.
        /// </summary>
        private static bool IsReallyPaused()
        {
            try
            {
                if (Core.Logic == null || Core.Logic.CurrentLevelConfig == null)
                    return false;

                UIController ui = UIController.instance;
                if (ui == null)
                    return false;

                return ui.Paused;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// <c>TimeScaleReal</c> read-only, printed to prove the game still believes it is paused.
        /// Out of level it is unavailable and reported as 0.
        /// </summary>
        private static float TimeScaleReal()
        {
            try
            {
                if (Core.Logic == null || Core.Logic.CurrentLevelConfig == null)
                    return 0f;

                return Core.Logic.CurrentLevelConfig.TimeScale;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        /// <summary>Fixed numeric format (invariant) so logs compare across machines.</summary>
        private static string F(float value)
        {
            return value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Last catch before the mod's per-frame cycle: never rethrows.</summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " EXCEPTION in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (time was NOT touched: the game continued)");
            }
            catch (Exception)
            {
                // No second place to report if the log itself fails.
            }
        }
    }
}