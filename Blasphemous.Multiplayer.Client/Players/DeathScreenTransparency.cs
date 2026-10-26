using Blasphemous.ModdingAPI;
using Gameplay.UI.Others.Buttons;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Makes the death screen transparent during co-op only, by zeroing the alpha of the Graphic
    /// children of NormalGroup. The prompt lives on ContinueGroup and is untouched. Works because
    /// DOFade animates CanvasGroup.alpha while Graphic.color.a is a separate multiply, so the fix
    /// does not fight DOTween. Registers the original alphas and restores them when the coop gate
    /// closes (single player or disconnect), so the death screen is the game's again after a
    /// co-op session.
    /// </summary>
    internal static class DeathScreenTransparency
    {
        /// <summary>Marker for every line of this class.</summary>
        private const string MARK = "[DeathScreen] trasparenza";

        /// <summary>
        /// Reapply interval while dead. Not per frame: GetComponentsInChildren allocates, and a
        /// 1s cadence is enough to repair a runtime-added Graphic on a ~24s death window.
        /// </summary>
        private const float REAPPLY_INTERVAL = 1f;

        private static bool _dead;
        private static float _nextApply;

        /// <summary>One error line per death window when the widget is not found.</summary>
        private static bool _missingReported;

        // Original alphas of every Graphic we zeroed, to restore them when the coop gate closes.
        // Two parallel lists for simplicity; entries are few and the order is irrelevant.
        private static readonly List<Graphic> _zeroedGraphics = new List<Graphic>();
        private static readonly List<float> _zeroedAlphas = new List<float>();

        /// <summary>
        /// Per frame, from Multiplayer.OnLateUpdate. Applies transparency while dead and
        /// connected; restores the original alphas when the coop gate closes. Guarded against
        /// exceptions that would escape the mod's late-update cycle.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                // Coop gate before any read: single player does not write, and restores any
                // alpha zeroed by a previous co-op session.
                if (!CoopGate.IsMultiplayerActive())
                {
                    if (_dead)
                    {
                        _dead = false;
                        _missingReported = false;
                    }

                    Restore("single player: no server");
                    return;
                }

                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_dead)
                    {
                        _dead = false;
                        _missingReported = false;
                        ModLog.Info(MARK + ": end of death window, no restore " +
                                    "(the background stays transparent: nothing to restore)");
                    }
                    return;
                }

                float now = Time.unscaledTime;
                bool first = !_dead;
                _dead = true;

                if (!first && now < _nextApply)
                    return;

                _nextApply = now + REAPPLY_INTERVAL;
                Apply(first);
            }
            catch (Exception e)
            {
                DeathScreenProbe.Fail("DeathScreenTransparency.Tick", e);
            }
        }

        /// <summary>
        /// Restores the original alphas of every Graphic this class zeroed, and clears the
        /// registry. Idempotent. Needed because the zeroing is permanent on the scene instance:
        /// without it, a player who was in co-op then lost the server would see a faded screen in
        /// single player too.
        /// </summary>
        private static void Restore(string why)
        {
            try
            {
                if (_zeroedGraphics.Count == 0)
                    return;

                int restored = 0;

                for (int i = 0; i < _zeroedGraphics.Count; i++)
                {
                    Graphic graphic = _zeroedGraphics[i];
                    if (graphic == null)
                        continue;

                    Color color = graphic.color;
                    graphic.color = new Color(color.r, color.g, color.b, _zeroedAlphas[i]);
                    restored++;
                }

                _zeroedGraphics.Clear();
                _zeroedAlphas.Clear();

                if (restored > 0)
                {
                    ModLog.Info(MARK + " RESTORED (" + why + ") alphaRimessi=" +
                                restored.ToString() + " · the death screen is the game's again");
                }
            }
            catch (Exception e)
            {
                // Clear the registry anyway: retrying every frame on a broken object would just
                // be spam. The screen stays as it is (invisible outside of death).
                _zeroedGraphics.Clear();
                _zeroedAlphas.Clear();
                ModLog.Error(MARK + " restore FAILED (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· the screen stays as is, nothing broken");
            }
        }

        /// <summary>
        /// Zeroes the alpha of every Graphic child of NormalGroup. Idempotent. The widget is
        /// looked up at each call rather than cached, so a scene reload does not leave a stale
        /// reference that turns the whole fix into a silent no-op after a respawn.
        /// </summary>
        private static void Apply(bool first)
        {
            int found;
            DeadScreenWidget widget = DeathScreenProbe.FindWidget(out found);

            if (found > 1 && first)
            {
                // Declared: using "the first active in hierarchy" can be wrong if that one is
                // not the live widget, so the choice is stated instead of hidden.
                ModLog.Info(MARK + ": trovato=" + found + " widget with NormalGroup - using the first " +
                            "active in hierarchy (declared, not silent)");
            }

            if (widget == null || widget.NormalGroup == null)
            {
                if (!_missingReported)
                {
                    _missingReported = true;
                    ModLog.Error(MARK + " NOT applied motivo=widget-not-found found=" + found +
                                 " (Resources.FindObjectsOfTypeAll<DeadScreenWidget>() with NormalGroup != null)");
                }
                return;
            }

            _missingReported = false;

            Graphic[] graphics = widget.NormalGroup.GetComponentsInChildren<Graphic>(true);
            int children = graphics != null ? graphics.Length : 0;

            int zeroed = 0;
            for (int i = 0; i < children; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                    continue;

                Color color = graphic.color;
                if (color.a <= 0f)
                    continue;

                // Only alpha. RGB is preserved and the ORIGINAL alpha is registered before
                // zeroing, since that is what Restore() puts back. A Graphic already in the list
                // is not overwritten: the first value recorded is the true one.
                if (!_zeroedGraphics.Contains(graphic))
                {
                    _zeroedGraphics.Add(graphic);
                    _zeroedAlphas.Add(color.a);
                }

                graphic.color = new Color(color.r, color.g, color.b, 0f);
                zeroed++;
            }

            // Group alpha is read but never written: it proves the DOFade still runs while the
            // children stay invisible.
            float groupAlpha = widget.NormalGroup.alpha;

            if (first)
            {
                ModLog.Info(MARK + " applied: trovato=" + found + " figli=" + children +
                            " azzerati=" + zeroed + " alphaGruppo=" + DeathScreenProbe.F(groupAlpha) +
                            " applicato=" + (children > 0 ? "si" : "no") +
                            (zeroed == 0 && children > 0 ? " (the children alphas were already all 0)" : "") +
                            (children == 0 ? " motivo=no-graphic-child" : "") +
                            " · the prompt is on ContinueGroup and was NOT touched");

                if (children == 0)
                {
                    ModLog.Error(MARK + " NOT applied motivo=NormalGroup-without-Graphic children=0 " +
                                 "applicato=no (the background is not a Graphic child of NormalGroup)");
                }
            }
            else if (zeroed > 0)
            {
                // Only when something changed: an identical log every second would be spam.
                ModLog.Info(MARK + " reapplied (auto-repair) azzerati=" + zeroed +
                            " figli=" + children + " alphaGruppo=" + DeathScreenProbe.F(groupAlpha));
            }
        }
    }
}