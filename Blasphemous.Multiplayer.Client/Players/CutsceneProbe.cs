using Blasphemous.ModdingAPI;
using Framework.Managers;
using System;
using System.Globalization;
using Tools.DataContainer;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Log-only probes for cinematics. Two probes remain, both zero-cost (no Harmony patches).
    ///
    /// Probe #0 ([Cutscene BLK]) logs CINEMATIC blocker transitions, once per change. CINEMATIC
    /// has two owners (Cinematics and TresAngustiasMasterBehaviour) and is not refcounted; its
    /// enable is also the first sign that the boss-death FSM has started, so this probe detects
    /// death-chain activity even before any cutscene is named.
    ///
    /// Probe #2 ([Cutscene EV]) subscribes to public Cinematics events. CinematicStarted proves
    /// the call completed, but fires even when PlayVideo dies inside its catch - so it does not
    /// prove the content started. VideoStarted is the real witness: PlayVideo failure leaves
    /// cinematicRunning false and EndCutscene a no-op, so CINEMATIC stays on.
    ///
    /// To remove: delete this file and the three TEMPORARY (CutsceneProbe) lines in
    /// Multiplayer.cs (OnInitialize, OnLateUpdate, OnDispose).
    /// </summary>
    internal static class CutsceneProbe
    {
        /// <summary>Fallback marker for probe failures; distinct so faults are separable from measurements.</summary>
        private const string MARK_FAIL = "[Cutscene ERR]";

        /// <summary>Probe #0 - CINEMATIC blocker cycle, one line per value change.</summary>
        private const string MARK_BLOCKER = "[Cutscene BLK]";

        /// <summary>Probe #2 - public Cinematics events.</summary>
        private const string MARK_EVENT = "[Cutscene EV]";

        // Probe #0: last read value. _blockerRead suppresses the first line (initial value is not
        // a transition).
        private static bool _blockerRead;
        private static bool _blockerLast;

        // Probe #2: subscription state. _subscribed blocks double-subscription;
        // _subscribePending covers Core.Cinematics not being ready yet (retried from Tick).
        private static bool _subscribed;
        private static bool _subscribePending;
        private static bool _subscribeFailureReported;

        /// <summary>
        /// Probe #0 plus retry for probe #2. Called once per frame from Multiplayer.OnLateUpdate.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                if (_subscribePending && !_subscribed)
                    Subscribe();

                InputManager input = Core.Input;
                if (input == null)
                    return;

                bool now = input.HasBlocker("CINEMATIC");
                if (_blockerRead && now == _blockerLast)
                    return;

                _blockerRead = true;
                _blockerLast = now;

                // InputBlocked is the aggregate of all blockers, updated only inside SetBlocker.
                ModLog.Info(MARK_BLOCKER + " t=" + F(Time.time) + " CINEMATIC=" + now +
                            " InputBlocked=" + input.InputBlocked);
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Probe #2: subscribes to Cinematics public events. Called from Multiplayer.OnInitialize;
        /// retried from Tick if Core.Cinematics is not ready yet.
        /// </summary>
        internal static void Subscribe()
        {
            try
            {
                Cinematics cinematics = Core.Cinematics;
                if (cinematics == null)
                {
                    _subscribePending = true;
                    if (!_subscribeFailureReported)
                    {
                        _subscribeFailureReported = true;
                        ModLog.Info(MARK_EVENT + " Core.Cinematics not ready yet: retrying every frame");
                    }
                    return;
                }

                if (_subscribed)
                    return;

                cinematics.CinematicStarted += OnCinematicStarted;
                cinematics.CinematicEnded += OnCinematicEnded;
                cinematics.VideoStarted += OnVideoStarted;
                cinematics.VideoEnded += OnVideoEnded;

                _subscribed = true;
                _subscribePending = false;

                ModLog.Info(MARK_EVENT + " probe armed: subscribed to CinematicStarted/CinematicEnded/" +
                            "VideoStarted/VideoEnded (no patches)");
            }
            catch (Exception e)
            {
                Fail("Subscribe", e);
            }
        }

        /// <summary>Probe #2: removes the subscription. Called from Multiplayer.OnDispose.</summary>
        internal static void Unsubscribe()
        {
            try
            {
                Cinematics cinematics = Core.Cinematics;
                if (cinematics == null || !_subscribed)
                    return;

                cinematics.CinematicStarted -= OnCinematicStarted;
                cinematics.CinematicEnded -= OnCinematicEnded;
                cinematics.VideoStarted -= OnVideoStarted;
                cinematics.VideoEnded -= OnVideoEnded;

                _subscribed = false;
                _subscribePending = false;

                ModLog.Info(MARK_EVENT + " probe disarmed: subscriptions removed");
            }
            catch (Exception e)
            {
                Fail("Unsubscribe", e);
            }
        }

        // --- event handlers. Each is guarded: these are entry points invoked by the game, so an
        //     escaping exception would travel up the game's call path.

        private static void OnCinematicStarted()
        {
            try
            {
                // Fired at the end of StartCutscene, after the blocker.
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " CinematicStarted");
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
                // cancelled=true means the player cancelled with key 51 (a distinct close path).
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " CinematicEnded cancelled=" + cancelled);
            }
            catch (Exception e)
            {
                Fail("OnCinematicEnded", e);
            }
        }

        private static void OnVideoStarted(CutsceneData cutscene)
        {
            try
            {
                // Real witness that the content started: Video via player.started, Images/Animation
                // via OnVideoStart(null). Without this, cinematicRunning never becomes true and
                // EndCutscene is a no-op.
                string nome = Name(cutscene);
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " VideoStarted nome=\"" + nome + "\"");
            }
            catch (Exception e)
            {
                Fail("OnVideoStarted", e);
            }
        }

        private static void OnVideoEnded()
        {
            try
            {
                // Fired from EndCutscene before the closing fade; CinematicEnded may arrive later.
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " VideoEnded");
            }
            catch (Exception e)
            {
                Fail("OnVideoEnded", e);
            }
        }

        // --- reads and formatting

        /// <summary>Name of the CutsceneData; null returns "&lt;null&gt;" (destructed ScriptableObject).</summary>
        internal static string Name(CutsceneData cutscene)
        {
            if (cutscene == null)
                return "<null>";

            return cutscene.name;
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
        /// Last line of defense. The empty catch is deliberate: if ModLog.Error itself failed, the
        /// exception would escape the entry point into the game, the only damage a log-only probe
        /// can cause.
        /// </summary>
        internal static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " probe failed in " + where + " (no effect on the game): " + e.Message);
            }
            catch
            {
            }
        }
    }
}