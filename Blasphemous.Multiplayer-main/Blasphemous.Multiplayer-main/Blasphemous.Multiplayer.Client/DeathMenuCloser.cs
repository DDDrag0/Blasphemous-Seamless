using System;
using Blasphemous.ModdingAPI;
using Gameplay.UI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⑤ — **LA MORTE CHIUDE I MENU' PRIMA DELLA SCHERMATA DI MORTE.**
    ///
    /// IL PROBLEMA, e perche' esiste solo dopo A′. Con il tempo che corre in pausa (`TimeWhilePaused.cs`)
    /// **si puo' morire con un menu' aperto**: il giocatore locale e' fermo, ma il mondo no. E quando si
    /// muore, `DeadScreenWidget.OnDeadAction()` (`Gameplay.UI.Others.Buttons\DeadScreenWidget.cs:67-97`)
    /// aspetta il tasto con `while (!Input.anyKey)` (`:87`) — e `Input.anyKey` in Unity e' "un tasto e'
    /// **tenuto** giu' in questo momento", non "e' stato premuto ora". Chi sta **premendo i tasti del
    /// menu'** nel momento in cui il ciclo viene raggiunto **salta la schermata di morte**: respawn
    /// immediato, senza leggere "SEI MORTO". E' un difetto **gia' latente** nel gioco, che il tempo
    /// corrente rende raggiungibile (`scratch\VALUTAZIONE-TIMESCALE.md` §⑤).
    ///
    /// ⭐ LA SOLUZIONE E' DELL'UTENTE, ed e' un fatto di **ordine**: alla morte si **chiudono prima i
    /// menu'** (inventario, mappa, pausa) e **poi** si lascia partire il fade di morte. I tasti non sono
    /// piu' "premuti" — il menu' non c'e' piu', e davanti c'e' la schermata di morte — e la schermata **si
    /// vede**. Non c'e' nessuna scommessa sul tempismo: fra la morte e il `while (!Input.anyKey)` passano
    /// i **~6,3 s** di attese NON scalate della schermata stessa (`DelayToAnimImage` 1 s + `FadeImageTime`
    /// 2,15 s + `DelayToAnimText` 1 s + `FadeImageTime` 2,15 s, tutte `WaitForSecondsRealtime`/DOTween non
    /// scalato). Chiudere i menu' **all'istante della morte** lascia quei 6,3 s per staccare le dita.
    ///
    /// ⭐ PERCHE' LE PRIMITIVE SONO QUELLE DEL GIOCO, e non roba nuova. `UIController.HideInventory()`
    /// (`:592-598`) e `UIController.HidePauseMenu()` (`:887-894`) sono **API pubbliche che il gioco usa per
    /// se' stesso**, e fanno la ripresa da sole:
    ///   · `HideInventory()` → `newInventoryMenu.Show(false)` → `SetState(OFF)` + `ResumeGame()`
    ///     (`NewInventoryWidget.cs:215-218`) e via il blocker `INVENTORY` (`:166`). Sincrono, nessun tween;
    ///   · `HidePauseMenu()` → `pauseWidget.FadeHide()` → `ResumeGame()` + `SetState(Playing)`
    ///     (`BasicUIBlockingWidget.cs:59-63`) → `EndFadeHide` toglie `UIBLOCKING` (`:130-139`). E' **il
    ///     "riprendi" del gioco** (`OptionsWidget.Option_ResumeSecure` fa la stessa chiamata). Copre anche
    ///     la **mappa**, che vive dentro il widget di pausa (`PauseWidget` + scheda `MAP`).
    /// ⚠️ Sono **le stesse due chiamate** che `PauseNotice` faceva nella V2 della pausa condivisa (ramo ②
    /// di `CloseAndResume`, rimosse dal commit `0246f0d`). Qui si **riusano per la morte**, non per la
    /// pausa: la pausa non si condivide piu', e questi due menu' non c'entrano nulla con la rete.
    ///
    /// ⭐ L'ORDINE NON E' UNA CONVENZIONE, E' UNA GARANZIA. Questo metodo e' chiamato dalla **prefix** di
    /// `Penitent.OnUpdate` (`PlayerDeathSync.cs`, `PenitentOnUpdateDeath_Patch`), che gira **prima** che il
    /// corpo di `OnUpdate` invochi `OnDead` (`Penitent.cs:490-498`). `OnDead` e' l'evento a cui sono
    /// iscritti **sia** `DeadScreenWidget` **sia** `UIController.OnDead` (`UIController.cs:462-469`), in
    /// ordine di iscrizione **non deterministico**: aspettare che sia uno di loro a chiudere i menu'
    /// significa **scommettere sull'ordine**. Da qui invece si chiude **prima di tutti e due**, sempre.
    ///
    /// ⭐ "UNA VOLTA SOLA" E' LA GUARDIA DEL GIOCO, non una nostra. La prefix esce se
    /// `!Status.Dead || DeathEventLaunched`, e `DeathEventLaunched` e' messo dal gioco **immediatamente
    /// prima** di invocare `OnDead`: e' vero **una volta per morte**, per costruzione. Un nostro flag
    /// sarebbe un secondo "una volta" che puo' divergere dal primo; qui non ce n'e'. In piu' le due
    /// chiamate sono **idempotenti**: `newInventoryMenu.Show(false)` lo chiama gia' anche
    /// `UIController.OnDead`, e `BasicUIBlockingWidget.FadeHide` esce subito se il widget non e' attivo o
    /// sta gia' sfumando (`:52-54`) — quindi la doppia chiusura non fa danni.
    ///
    /// ⛔ IL CANCELLO — solo con un server (`CoopGate.IsMultiplayerActive()`), e qui **non costa niente**:
    /// in single player il gioco pausa con `timeScale = 0`, quindi **non si puo' morire con un menu'
    /// aperto**. La funzione e' inerte per costruzione, non per fortuna.
    ///
    /// ⚠️ TUTTO DENTRO `try/catch`: una chiusura di menu' non deve mai impedire la schermata di morte.
    /// </summary>
    internal static class DeathMenuCloser
    {
        /// <summary>Marker di ESITO: la riga della morte si conta da sola nel log.</summary>
        private const string MARK = "[Morte]";

        /// <summary>
        /// Chiude i menu' aperti, **adesso**, all'istante della morte e **prima** della schermata di morte.
        /// Chiamata una volta per morte dalla prefix di `Penitent.OnUpdate` (vedi il commento di classe).
        /// </summary>
        internal static void CloseBeforeDeathScreen()
        {
            try
            {
                if (!CoopGate.IsMultiplayerActive())
                {
                    ModLog.Info(MARK + " menu' NON chiusi: cancello chiuso (nessun server) · in single player" +
                                " la pausa ferma il mondo (timeScale 0) e non si puo' morire con un menu' aperto");
                    return;
                }

                UIController ui = UIController.instance;
                if (ui == null)
                {
                    ModLog.Info(MARK + " menu' NON chiusi: UIController non disponibile (la schermata di" +
                                " morte parte comunque: non dipende da questa classe)");
                    return;
                }

                bool inventarioPrima = IsShowingInventory(ui);
                bool menuPrima = IsShowingMenu(ui);

                if (!inventarioPrima && !menuPrima)
                {
                    ModLog.Info(MARK + " morte con NESSUN menu' aperto: niente da chiudere, la schermata di" +
                                " morte parte normalmente");
                    return;
                }

                // ⭐ L'ORDINE: prima si chiude, poi (nel corpo di OnUpdate, un attimo dopo) parte la morte.
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

                ModLog.Info(MARK + " menu' chiusi PRIMA della schermata di morte · modo=" + modo +
                            " · prima: inventario=" + Si(inventarioPrima) + " pausa/mappa=" + Si(menuPrima) +
                            " · dopo: inventario=" + Si(inventarioDopo) + " pausa/mappa=" + Si(menuDopo) +
                            " · pausaDopo=" + Si(pausaDopo) +
                            " · i tasti del menu' non sono piu' \"premuti\": la schermata di morte si vede" +
                            " (il suo `while (!Input.anyKey)` ha ~6,3 s di attese non scalate per arrivarci)");
            }
            catch (Exception e)
            {
                Fail("CloseBeforeDeathScreen", e);
            }
        }

        // ============================================================================================
        // LE LETTURE — predicati PUBBLICI del gioco (`UIController.IsShowingInventory`, `IsShowingMenu`,
        // `Paused`), ognuno con il suo `try/catch`: un guasto non deve impedire la chiusura degli altri
        // ne', tanto meno, la schermata di morte.
        // ============================================================================================
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
        /// L'ultimo `catch`: non rilancia mai. Se questa classe sbagliasse, il danno peggiore sarebbe
        /// "i menu' restano aperti", non "la morte non parte" — ma nemmeno quello deve succedere.
        /// </summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (la morte prosegue: questa classe non e' nel percorso critico)");
            }
            catch (Exception)
            {
                // Niente da fare: non c'e' un secondo posto dove dirlo.
            }
        }
    }
}
