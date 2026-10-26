using System;
using Blasphemous.ModdingAPI;
using Gameplay.UI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Closes open menus (inventory, map, pause) at the exact moment of death, before the death
    /// screen, so its <c>while (!Input.anyKey)</c> is not skipped by held keys. Runs from the
    /// <c>Penitent.OnUpdate</c> prefix, before <c>OnDead</c> fires, so the close happens before
    /// either <c>DeadScreenWidget</c> or <c>UIController.OnDead</c> reacts. Uses the game's own
    /// public hide calls (which resume from pause themselves) and relies on the game's
    /// <c>DeathEventLaunched</c> guard for once-per-death semantics; the hide calls are
    /// idempotent. Coop-gated: in single player the game pauses with timeScale 0, so death with
    /// an open menu cannot happen.
    /// </summary>
    internal static class DeathMenuCloser
    {
        /// <summary>Outcome marker.</summary>
        private const string MARK = "[Morte]";

        /// <summary>
        /// Closes open menus immediately, at the death instant and before the death screen.
        /// Called once per death from the <c>Penitent.OnUpdate</c> prefix.
        /// </summary>
        internal static void CloseBeforeDeathScreen()
        {
            try
            {
                if (!CoopGate.IsMultiplayerActive())
                {
                    ModLog.Info(MARK + " menus NOT closed: gate closed (no server) · in single player " +
                                "the pause freezes the world (timeScale 0) and death with an open menu is impossible");
                    return;
                }

                UIController ui = UIController.instance;
                if (ui == null)
                {
                    ModLog.Info(MARK + " menus NOT closed: UIController unavailable (the death screen " +
                                "starts anyway: it does not depend on this class)");
                    return;
                }

                bool inventarioPrima = IsShowingInventory(ui);
                bool menuPrima = IsShowingMenu(ui);

                if (!inventarioPrima && !menuPrima)
                {
                    ModLog.Info(MARK + " death with NO menu open: nothing to close, the death screen " +
                                "starts normally");
                    return;
                }

                // Close first; OnUpdate fires the death a moment later.
                string modo = "";

                if (inventarioPrima)
                {
                    ui.HideInventory();
                    modo += (modo.Length == 0 ? "" : "+") + "HideInventory";
                }

                if (menuPrima)
                {
                    ui.HidePauseMenu();
                    modo += (modo.Length == 0 ? "" : "+") + "HidePauseMenu";
                }

                bool inventarioDopo = IsShowingInventory(ui);
                bool menuDopo = IsShowingMenu(ui);
                bool pausaDopo = IsPaused(ui);

                ModLog.Info(MARK + " menus closed BEFORE the death screen · modo=" + modo +
                            " · before: inventory=" + Si(inventarioPrima) + " pause/map=" + Si(menuPrima) +
                            " · after: inventory=" + Si(inventarioDopo) + " pause/map=" + Si(menuDopo) +
                            " · pausedAfter=" + Si(pausaDopo) +
                            " · menu keys are no longer \"held\": the death screen is visible " +
                            "(its `while (!Input.anyKey)` has ~6.3s of unscaled waits to get there)");
            }
            catch (Exception e)
            {
                Fail("CloseBeforeDeathScreen", e);
            }
        }

        // Read predicates: the game's public state (IsShowingInventory, IsShowingMenu, Paused).
        // Each guarded so a failure in one does not stop the others or the death screen.

        private static bool IsShowingInventory(UIController ui)
        {
            try
            {
                return ui.IsShowingInventory;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsShowingMenu(UIController ui)
        {
            try
            {
                return ui.IsShowingMenu;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsPaused(UIController ui)
        {
            try
            {
                return ui.Paused;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Si(bool valore)
        {
            return valore ? "si" : "no";
        }

        /// <summary>
        /// Last catch: never rethrows. A failure here means menus stay open, not that death does
        /// not fire - but even that should not happen.
        /// </summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (death continues: this class is not on the critical path)");
            }
            catch (Exception)
            {
                // Nothing to do: no second place to report it.
            }
        }
    }
}