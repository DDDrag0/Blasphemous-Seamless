using Blasphemous.ModdingAPI;
using Com.LuisPedroFonseca.ProCamera2D;
using Framework.Managers;
using Gameplay.UI.Others.Buttons;
using System;
using System.Globalization;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Log-only probe of the death screen. Samples camera, ProCamera2D target, the penitent
    /// position, and the death-screen widget (alpha and hierarchy under GameplayUI) while the
    /// local player is dead. Runs from Multiplayer.OnLateUpdate - no Harmony patches, no
    /// MonoBehaviour. One sample on entry, then one every <c>SAMPLE_INTERVAL</c> seconds, plus a
    /// closing line.
    ///
    /// Notes: <c>FindObjectsOfType&lt;T&gt;()</c> skips disabled objects (silent no-op on a widget
    /// that is toggled off); <c>Resources.FindObjectsOfTypeAll&lt;T&gt;()</c> returns them too.
    /// </summary>
    internal static class DeathScreenProbe
    {
        /// <summary>Measurement marker; distinct from the transparency-fix marker.</summary>
        private const string MARK = "[DeathScreen] misura";

        /// <summary>Last line of defense, as in CutsceneProbe.</summary>
        private const string MARK_FAIL = "[DeathScreen ERR]";

        /// <summary>Seconds between samples while dead. Two, matching PerformanceProbe's report.</summary>
        private const float SAMPLE_INTERVAL = 2f;

        private static bool _wasDead;
        private static float _nextSample;
        private static int _samples;

        /// <summary>
        /// One sample per frame, only while dead. First sample fires immediately, next ones on the
        /// interval; leaving the window writes a closing line with the sample count.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_wasDead)
                    {
                        _wasDead = false;
                        ModLog.Info(MARK + " death window END samples=" + _samples);
                        _samples = 0;
                    }
                    return;
                }

                float now = Time.unscaledTime;
                if (_wasDead && now < _nextSample)
                    return;

                bool primo = !_wasDead;
                _wasDead = true;
                _nextSample = now + SAMPLE_INTERVAL;
                _samples++;

                ModLog.Info(Sample(primo, now));
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Builds the line. Each read is isolated: if the camera or ProCamera2D is not available,
        /// that field shows "error" but the line is emitted anyway - a missing field must not
        /// erase the measurement of the others.
        /// </summary>
        private static string Sample(bool primo, float now)
        {
            Camera main = null;
            string cam = "n/d";
            try
            {
                main = Camera.main;
                if (main != null)
                    cam = V(main.transform.position);
            }
            catch (Exception)
            {
                cam = "error";
            }

            string target = "n/d";
            string dist = "n/d";
            try
            {
                // ProCamera2D.Instance and CameraTargetPosition are both public; the latter is the
                // same the game itself reads.
                ProCamera2D pro = ProCamera2D.Instance;
                if (pro != null)
                {
                    Vector3 targetPosition = pro.CameraTargetPosition;
                    target = V(targetPosition);
                    if (main != null)
                        dist = F(Vector3.Distance(main.transform.position, targetPosition));
                }
            }
            catch (Exception)
            {
                target = "error";
            }

            string posMia = "n/d";
            try
            {
                if (Core.Logic != null && Core.Logic.Penitent != null)
                    posMia = V(Core.Logic.Penitent.transform.position);
            }
            catch (Exception)
            {
                posMia = "error";
            }

            int trovati = 0;
            DeadScreenWidget widget = null;
            string normalAlpha = "n/d";
            string figlio = "n/d";
            try
            {
                widget = FindWidget(out trovati);
                if (widget != null && widget.NormalGroup != null)
                {
                    normalAlpha = F(widget.NormalGroup.alpha);

                    // Only meaningful if the widget is under Core.UI.GameplayUI: the fork between
                    // "disappears with the boss cutscene" and "stays on top of the video".
                    if (Core.UI != null && Core.UI.GameplayUI != null)
                        figlio = widget.NormalGroup.transform.IsChildOf(Core.UI.GameplayUI.transform) ? "si" : "no";
                }
            }
            catch (Exception)
            {
                normalAlpha = "error";
            }

            return MARK + (primo ? " INIZIO" : "") + " t=" + F(now) + " campione=" + _samples +
                   " cam=" + cam + " target=" + target + " posMia=" + posMia + " distCamTarget=" + dist +
                   " normalAlpha=" + normalAlpha + " figlioDiGameplayUI=" + figlio +
                   " widget=" + trovati;
        }

        /// <summary>
        /// Finds the death-screen widget, and reports how many candidates were found.
        ///
        /// Uses Resources.FindObjectsOfTypeAll&lt;T&gt;() so disabled widgets are also returned;
        /// filters by NonGroup != null to skip non-widget matches, and prefers the one that is
        /// active in hierarchy. Count is separate from the widget so "found=0" and "found=2" stay
        /// distinguishable in the log.
        /// </summary>
        internal static DeadScreenWidget FindWidget(out int found)
        {
            found = 0;

            DeadScreenWidget chosen = null;
            DeadScreenWidget fallback = null;

            DeadScreenWidget[] all = Resources.FindObjectsOfTypeAll<DeadScreenWidget>();
            if (all == null)
                return null;

            foreach (DeadScreenWidget candidate in all)
            {
                if (candidate == null || candidate.NormalGroup == null)
                    continue;

                found++;

                if (fallback == null)
                    fallback = candidate;

                if (chosen == null && candidate.gameObject.activeInHierarchy)
                    chosen = candidate;
            }

            // `!= null` and not `??`: the Unity operator detects destroyed objects, ?? does not.
            return chosen != null ? chosen : fallback;
        }

        /// <summary>Compact vector with invariant formatting.</summary>
        internal static string V(Vector3 v)
        {
            return "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";
        }

        /// <summary>
        /// Fixed numeric format (invariant). The log's key is order and time; a locale-dependent
        /// decimal separator would make numbers ambiguous.
        /// </summary>
        internal static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Last line of defense: if ModLog.Error itself failed, the exception would escape the
        /// entry point into the game - the only damage a log-only probe can do.
        /// </summary>
        internal static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " in " + where + " (no effect on the game): " + e.Message);
            }
            catch
            {
            }
        }
    }
}