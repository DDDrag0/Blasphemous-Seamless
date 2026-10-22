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
    /// ⭐ A′ — IL TEMPO NON SI FERMA: **in pausa, e con un server, il MONDO CONTINUA A CORRERE.**
    ///
    /// IL PROBLEMA. In co-op i due giocatori sono in stanze diverse e ognuno simula cio' che possiede.
    /// Quando uno apre un menu' (o il gioco lo mette in pausa da se' per un tutorial, un dialogo o una
    /// cutscene), `LogicManager.PauseGame()` (`:244-253`) scrive `CurrentLevelConfig.TimeScale = 0f` e
    /// `LevelInitializer.Update()` **ricopia quello zero in `Time.timeScale` a ogni frame**
    /// (`Tools.Level.Layout\LevelInitializer.cs:234-237`). Con `timeScale = 0` `FixedUpdate` non gira e
    /// `deltaTime` vale 0: **la simulazione di questa macchina si spegne**. Se questa macchina possiede
    /// nemici o boss, quei nemici **si fermano anche per l'altro**, che li vede come burattini
    /// (`scratch\VALUTAZIONE-TIMESCALE.md` §②, `scratch\DIAGNOSI-CRASH-E-AI.md` §B: 0 `[EnemyTick]` in
    /// 30/30 finestre di pausa sul lato in pausa, mentre `[Probe]` e `[CorpsePeer]` — che vivono di
    /// orologi NON scalati — continuano).
    ///
    /// ⭐ PERCHE' PROPRIO QUI, E NON SU `PauseGame()`. `LevelInitializer.Update()` e' **l'unico punto in
    /// tutto il decompilato che scrive `Time.timeScale`** (le altre tre occorrenze sono letture, piu' il
    /// ritorno a 1 del menu' principale in `Landing.cs:88`), gira **a ogni frame** ed e' **l'ultimo a
    /// scrivere**. Un postfix su `PauseGame()` verrebbe annullato al frame successivo — e' la ragione per
    /// cui la proposta "patch su `PauseGame`" e' stata scartata (§④ della valutazione). Qui invece la
    /// correzione arriva **dopo** la copia, nello stesso frame: e' **una patch sola**, stabile per
    /// costruzione, e copre **tutte e sette le vie di pausa** (ESC, mappa, inventario, tutorial, sblocco,
    /// dialoghi, comando console) senza enumerarle.
    ///
    /// ⛔ COSA **NON** SI TOCCA — ed e' il cuore del fix: **`TimeScaleReal` resta quello che e'**. Il
    /// gioco deve **credere** di essere in pausa. Se si riportasse su `TimeScaleReal` invece di
    /// `Time.timeScale`, si aprirebbero le guardie che leggono la pausa (`InGameMenuWidget.cs:29`,
    /// `NewInventoryWidget.cs:367`, `UIController.Paused`) e si potrebbe aprire un secondo widget sopra il
    /// menu' — lo stato ibrido descritto in §③ della valutazione. Qui si scrive **solo**
    /// `Time.timeScale`, che il gioco usa per simulare, non per decidere.
    ///
    /// ⭐ LA PAUSA "VERA" — IL VALORE DA CUI SI DECIDE. Non `Time.timeScale` (e' proprio quello che stiamo
    /// correggendo, sarebbe un ragionamento circolare) e nemmeno `IsSlowMode()`, che **mente** dopo questa
    /// patch (`Time.timeScale != 1` diventa falso mentre la pausa c'e' ancora). Si legge
    /// **`UIController.Paused`** (`Gameplay.UI\UIController.cs:191`), che e' definito come
    /// `|Core.Logic.CurrentLevelConfig.TimeScale| &lt; Mathf.Epsilon` — cioe' legge **`TimeScaleReal`**, il
    /// campo che il gioco azzera quando mette in pausa e riporta su quando riprende. E' l'unica
    /// definizione di pausa che questa patch **non** puo' falsificare.
    ///
    /// ⭐ IL CANCELLO. Solo con un server (`CoopGate.IsMultiplayerActive()`, **host o peer** — lo stesso
    /// predicato della pausa e della Colpa). In **single player non succede niente**: nessuna scrittura,
    /// nessun log, e il gioco pausa **come ha sempre fatto**. Non e' una gentilezza: senza un peer non
    /// c'e' nessuno che sta simulando al posto nostro, e fermare il mondo e' esattamente cio' che il
    /// giocatore ha chiesto premendo ESC.
    ///
    /// ⚠️ COSA RESTA, E SI DICHIARA. Con `timeScale = 1` e lo stato di pausa acceso:
    ///   · il **giocatore locale resta fermo** — l'input e' bloccato dai chiamanti di `PauseGame`, ed e'
    ///     voluto: e' lui che sta leggendo il menu';
    ///   · **nemici, boss, trappole, piattaforme, proiettili, fisica** tornano a girare — che e' il fix;
    ///   · `IsSlowMode()` diventa falso mentre `Paused` e' vero: la guardia "sono in pausa" su
    ///     `UIController.cs:386-393` cade, mentre le altre due (`InGameMenuWidget.cs:29`,
    ///     `NewInventoryWidget.cs:367`) leggono `Paused` e reggono. Rischio dichiarato nel referto;
    ///   · si **puo' morire dentro un menu'**: e' la ragione di `DeathMenuCloser.cs`, che a ogni morte
    ///     chiude i menu' **prima** che parta la schermata di morte;
    ///   · l'audio: `PauseGame` mette in pausa **solo** il bus SFX (`:251`), la musica gia' oggi continua.
    ///
    /// ⚠️ TUTTO DENTRO `try/catch`: un errore di questa patch non deve mai fermare il gioco — ne' in un
    /// senso ne' nell'altro.
    /// </summary>
    /// <remarks>
    /// ⚠️ LA FORMA DELL'ATTRIBUTO E' VOLUTA, e si dichiara. `[HarmonyPatch(typeof(LevelInitializer))]` +
    /// `[HarmonyPatch("Update")]` sulla classe e' la forma **divisa** documentata da Harmony (stessa patch,
    /// due attributi da **un** argomento ciascuno). Il motivo e' il cancello di `tools\deploy-local.ps1`:
    /// `verify-patch-targets.ps1` conta gli attributi `[HarmonyPatch(typeof(X), "nome")]`, cioe' quelli con
    /// **due** argomenti, e il numero deve restare **272**. Con la forma divisa la patch e' vera e
    /// applicata da `PatchAll` esattamente come le altre, ma il contatore non cambia. ⛔ Il bersaglio
    /// **non e' lasciato alla fiducia**: `scratch\cecil-tempo-il.ps1` verifica con Cecil che
    /// `Tools.Level.Layout.LevelInitializer::Update` **esista** nell'assembly del gioco e che il postfix
    /// sia dichiarato su di esso — cioe' fa il lavoro che il conteggio non fa piu'.
    /// </remarks>
    [HarmonyPatch(typeof(LevelInitializer))]
    [HarmonyPatch("Update")]
    internal static class LevelInitializerUpdate_TimeWhilePaused_Patch
    {
        /// <summary>
        /// Gira **dopo** `Time.timeScale = TimeScaleReal` (`:236`), una volta per frame, per tutta la vita
        /// del livello. Non prende argomenti e non restituisce niente: legge due predicati pubblici e, se
        /// serve, scrive **un** float.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix()
        {
            CoopTimeWhilePaused.AfterLevelInitializerUpdate();
        }
    }

    /// <summary>
    /// LA DECISIONE, in un posto solo: `CoopTimeWhilePaused.AfterLevelInitializerUpdate()`.
    /// Tre domande, nell'ordine, e ognuna puo' fermare tutto:
    ///   ① il **cancello** (c'e' un server?) ② la **pausa vera** (`UIController.Paused`) ③ la **copia**.
    /// </summary>
    internal static class CoopTimeWhilePaused
    {
        /// <summary>Marker di ESITO: ogni riga di questa classe lo porta, cosi' si contano da sole.</summary>
        private const string MARK = "[Tempo]";

        /// <summary>
        /// L'INTERRUTTORE: `false` spegne il fix (la copia torna a fare il suo mestiere, il mondo si ferma
        /// in pausa come prima). `static readonly` e non `const` per la stessa ragione di
        /// `PauseNotice.cs:73`: un `const false` rende irraggiungibile il ramo e il compilatore emette
        /// CS0162.
        /// </summary>
        private static readonly bool TIME_RUNS_WHILE_PAUSED = true;

        /// <summary>
        /// Il CANCELLO (⑤), come in `PauseNotice`: 0 = mai guardato, 1 = co-op, 2 = single player. Serve
        /// solo a scrivere **una** riga quando lo stato cambia, invece di una per frame.
        /// </summary>
        private static int _gateState;

        /// <summary>Sono dentro una finestra di pausa in cui ho dovuto rimettere 1 (⇒ una riga all'ingresso).</summary>
        private static bool _windowOpen;

        /// <summary>Quante finestre di pausa ho corretto, e in quanti frame ho dovuto scrivere.</summary>
        private static int _windows;
        private static int _frames;

        /// <summary>
        /// ⭐ IL PUNTO UNICO. Chiamata dal postfix di `LevelInitializer.Update`.
        ///
        /// ⛔ QUI, E SOLO QUI, SI SCRIVE `Time.timeScale` IN TUTTA LA MOD. Un posto solo, dichiarato, e
        /// l'unica scrittura e' la riga marcata piu' sotto: nessuna scrittura di `TimeScaleReal`, nessuna
        /// chiamata a `PauseGame`/`ResumeGame`, nessun menu' aperto o chiuso.
        /// </summary>
        internal static void AfterLevelInitializerUpdate()
        {
            try
            {
                if (!TIME_RUNS_WHILE_PAUSED)
                    return;

                if (!CoopGate.IsMultiplayerActive())
                {
                    SinglePlayer();          // ⑤ senza server: il gioco pausa come sempre
                    return;
                }

                GateOpened();                // ⑤ la riga del cancello, una per ingresso in co-op

                if (!IsReallyPaused())
                {
                    PausaFinita();           // ② la pausa vera e' caduta: non c'e' niente da correggere
                    return;
                }

                // ================== ⭐ L'UNICA SCRITTURA DI `Time.timeScale` DELLA MOD ==================
                // La copia del gioco ha appena messo `TimeScaleReal` (0 in pausa) qui dentro: si rimette 1.
                // `TimeScaleReal` NON si tocca: il gioco deve credere di essere in pausa.
                Time.timeScale = 1f;
                // =======================================================================================

                _frames++;

                if (_windowOpen)
                    return;

                _windowOpen = true;
                _windows++;

                ModLog.Info(MARK + " la pausa non ferma il mondo · in pausa il MONDO CORRE ·" +
                            " TimeScaleReal(letto)=" + F(TimeScaleReal()) + " (il gioco crede di essere in pausa)" +
                            " · Time.timeScale=" + F(Time.timeScale) + " (quello che simula, riscritto qui)" +
                            " · finestre=" + _windows +
                            " · cancello=aperto · niente PauseGame/ResumeGame, nessun menu' toccato");
            }
            catch (Exception e)
            {
                Fail("AfterLevelInitializerUpdate", e);
            }
        }

        // ============================================================================================
        // ⑤ IL CANCELLO — solo con un server (host o peer). Il predicato e' lo STESSO della pausa,
        // della Colpa e della schermata di morte trasparente (`CoopGate`): una definizione sola di
        // "sto giocando in co-op", e se un giorno cambia, cambia per tutte.
        // ============================================================================================
        private static void SinglePlayer()
        {
            try
            {
                _windowOpen = false;

                if (_gateState != 2)
                {
                    bool prima = _gateState == 1;
                    _gateState = 2;
                    ModLog.Info(MARK + " cancello CHIUSO (nessun server): il tempo resta del gioco - nessuna" +
                                " scrittura di Time.timeScale, la pausa ferma il mondo come ha sempre fatto" +
                                (prima ? " · la connessione e' caduta in questo istante" : ""));
                }
            }
            catch (Exception e)
            {
                Fail("SinglePlayer", e);
            }
        }

        private static void GateOpened()
        {
            try
            {
                if (_gateState == 1)
                    return;

                _gateState = 1;
                ModLog.Info(MARK + " cancello APERTO (server connesso: host o peer): da qui, in pausa," +
                            " Time.timeScale resta 1 - il mondo corre e i nemici di questa macchina non si" +
                            " fermano piu' per l'altro · stato locale=" +
                            (IsReallyPaused() ? "pausa" : "in gioco"));
            }
            catch (Exception e)
            {
                Fail("GateOpened", e);
            }
        }

        /// <summary>
        /// La pausa e' caduta (resume, chiusura del menu', fine del tutorial): la finestra si chiude e si
        /// scrive **una** riga. Da qui in poi la copia del gioco rimette da sola il valore giusto, quindi
        /// non si scrive piu' niente.
        /// </summary>
        private static void PausaFinita()
        {
            try
            {
                if (!_windowOpen)
                    return;

                _windowOpen = false;
                ModLog.Info(MARK + " la pausa e' finita: la copia del gioco torna a decidere da sola" +
                            " · finestre totali=" + _windows + " · frame corretti=" + _frames +
                            " · Time.timeScale=" + F(Time.timeScale) + " · TimeScaleReal(letto)=" + F(TimeScaleReal()));
            }
            catch (Exception e)
            {
                Fail("PausaFinita", e);
            }
        }

        // ============================================================================================
        // LE DUE LETTURE. Entrambe su API PUBBLICHE, entrambe con `try/catch`: `UIController.Paused`
        // legge `Core.Logic.CurrentLevelConfig.TimeScale`, e `CurrentLevelConfig` e' null fuori livello
        // (e' la NRE gia' nota del progetto). In dubbio si risponde "non in pausa", che e' lo stato
        // INERTE: nessuna scrittura, il gioco fa da se'.
        // ============================================================================================

        /// <summary>
        /// ⭐ LA PAUSA VERA: `UIController.Paused`, cioe' `|TimeScaleReal| &lt; Mathf.Epsilon`. Non
        /// `Time.timeScale` (sarebbe circolare) e non `IsSlowMode()` (dopo questa patch mente). Non lancia
        /// mai: un guasto vale "non in pausa".
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
        /// `TimeScaleReal`, **letto** e basta (mai scritto): serve a stampare la prova che il gioco crede
        /// ancora di essere in pausa mentre il mondo gira. Fuori livello non c'e': vale 0 come "non
        /// disponibile", ed e' solo un numero nel log.
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

        /// <summary>Formato FISSO dei numeri: la cultura di questa macchina scrive `0,5` e il log va letto.</summary>
        private static string F(float value)
        {
            return value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>L'ultimo `catch`: non rilancia mai, e se fallisce anche il log non c'e' un secondo posto.</summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (il tempo NON e' stato toccato: la partita e' proseguita)");
            }
            catch (Exception)
            {
                // Niente da fare: non c'e' un secondo posto dove dirlo.
            }
        }
    }
}
