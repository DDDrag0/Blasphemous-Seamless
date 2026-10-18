using System;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.UI;
using Gameplay.UI.Others.MenuLogic;
using Gameplay.UI.Widgets;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐⭐ V2 — LA PAUSA CONDIVISA. La pausa diventa **una regola**: quando uno dei due apre il menu,
    /// **si ferma il mondo di tutti e due**, e quando uno dei due esce **escono tutti e due**. L'altro
    /// non gioca piu' "fra le statue".
    ///
    /// COSA ERA (B, commit `bc834f3`): una notifica a schermo, e nient'altro. Il passivo **continuava a
    /// giocare** in un mondo fermo - un nemico congelato che lo inseguiva, un boss che non si muoveva.
    /// La notifica toglieva l'ansia, non il guasto.
    ///
    /// ⛔ COSA QUESTA CLASSE **NON** FA, e sono le regole del progetto, non dettagli:
    ///   · **non scrive MAI `Time.timeScale`**: la pausa passa dal **canale ufficiale del gioco**,
    ///     `Core.Logic.PauseGame()` / `ResumeGame()` (`LogicManager.cs:244-264`), che e' la funzione che
    ///     il gioco chiama per se' stesso. E' la distinzione che il mandato chiede, e va dichiarata:
    ///     l'effetto sul `timeScale` c'e' (e' il gioco a produrlo, `LevelInitializer.cs:236`), ma la
    ///     **scrittura** non e' nostra;
    ///   · **non apre un menu' che il gioco non aprirebbe**: apre `MAP` + `SHOW`, cioe' **la stessa
    ///     identica configurazione del tasto pausa** (`UIController.cs:440-442`), con `FadeShow`;
    ///   · **non chiude mai con `ResumeGame()` mentre un menu' e' aperto**: quello e' lo stato
    ///     "mondo che gira + menu' aperto" da cui si raggiunge "esci al menu principale";
    ///   · **zero reflection, zero patch Harmony (269 bersagli, invariati), zero protocollo, zero
    ///     server, zero simulazione di input**: tutti i pezzi sono API **pubbliche** che il gioco usa
    ///     per se' stesso (`UIController.HidePauseMenu` / `HideInventory`, `PauseWidget.FadeShow`).
    ///
    /// ⭐ IL FRONTE, NON LO STATO. In `Tick()` si legge `Core.Logic.IsPaused` una volta per frame (le vie
    /// di pausa sono SETTE e le riprese DODICI: non c'e' una porta sola da patchare) e si trasmette
    /// **il cambio**. Il rimbalzo di rete non esiste (il server esclude il mittente) e quello di logica
    /// e' chiuso dal flag `_pausedByRemote`: la pausa che **subisco** non la rilancio.
    ///
    /// Il canale resta `AttackType.PauseRelay = 203`, un byte di verso (1 = pausa, 0 = ripresa):
    /// **il trasporto non cambia di una virgola** rispetto a B.
    ///
    /// ==================== I SEI PEZZI (l'ordine del mandato) ====================
    ///   R1 — LA PAUSA: ricevuto "in pausa" ⇒ `Core.Logic.PauseGame()` + `_pausedByRemote = true`.
    ///        Le guardie (R5) e il perche' di ognuna: vedi `ApplyRemotePause`.
    ///   R2 — CHIUDERE E RIPRENDERE: ricevuto "riprendi" ⇒ dispatch a **tre rami** con i predicati
    ///        pubblici `IsShowingInventory` / `IsShowingMenu` / niente. Il ramo `ResumeGame()` nudo
    ///        esiste **solo** quando un menu' non c'e' ⇒ lo stato proibito non si produce mai.
    ///        ⚠️ Il **retry**: `FadeHide` **rifiuta** mentre `IsFading` (`BasicUIBlockingWidget.cs:54`)
    ///        ⇒ un "riprendi" che arriva nei 0,4 s del fade **si perderebbe**. Si ritenta a ogni frame
    ///        con un tetto (`CLOSE_CAP_SECONDS`), poi si dichiara "frenata" e si lascia la pausa.
    ///   R3 — UNA RIGA in `TrySendFront`: `|| _pausedByRemote`. **Senza, il passivo esce e il pausante
    ///        resta in menu' per sempre** (il passivo non ha mai "annunciato" una pausa, quindi la sua
    ///        ripresa non partirebbe).
    ///   R4 — LE USCITE, **senza timeout aggiuntivo**: la pausa cade se il peer si disconnette
    ///        (istantaneo: lo dice il predicato della connessione), se e' in un'altra stanza, se e' a
    ///        terra, se si torna al menu principale. Il timeout di 8 s resta, ma **non e' piu' l'unica
    ///        difesa**: l'unico caso che copriva - la disconnessione - e' coperto **istantaneamente**
    ///        dalle uscite rapide di `CheckRemoteStillPaused`.
    ///   R5 — LE GUARDIE: niente pausa se uno e' **a terra**, durante un **cambio di scena**, durante un
    ///        **fade** o in **slow-motion**; e il caso **passivo morto** (a terra + pausa remota ⇒
    ///        chiudi e manda RESUME: a `timeScale = 0` il respawn passa da un tween e puo' non
    ///        completarsi).
    ///   R0 — APRIRE LA MAPPA (per ultima, e si puo' togliere): `FindObjectOfType di PauseWidget()` →
    ///        `InitialWidget = MAP` · `InitialMapMode = SHOW` → `FadeShow(checkInput: true)`. Il widget
    ///        esiste, il giocatore **vede** di essere in pausa e chiude con **una** pressione.
    ///        ⚠️ Ordine obbligatorio: **prima `PauseGame()`, poi il tentativo** - se `FadeShow` fallisce
    ///        una delle sue cinque guardie, **la pausa c'e' lo stesso**.
    ///
    /// ⚠️ GLI OROLOGI SONO TUTTI `Time.unscaledTime`: con `timeScale = 0` `Time.time` si ferma e
    /// `Time.deltaTime` vale 0, quindi un timeout o un retry scritti su quelli non scatterebbero mai
    /// proprio nel caso in cui servono. Precedenti in casa: `BossDeathWarning.cs:61-65`.
    /// E `OnLateUpdate` gira anche a `timeScale = 0` (provato in `scratch\afk\tail-G1.txt`).
    ///
    /// TUTTO E' DENTRO `try/catch`: una pausa non deve mai rompere la partita. Nessun errore esce mai
    /// da questa classe.
    /// </summary>
    internal static class PauseNotice
    {
        /// <summary>Marker di ESITO, obbligatorio su ogni riga: si conta da solo nel log.</summary>
        private const string MARK = "[Pausa]";

        /// <summary>
        /// L'INTERRUTTORE: `false` spegne TUTTA la funzione (non manda, non mostra, non mette in pausa).
        /// `static readonly` e non `const` per la stessa ragione di `BossFightDeathWarning.cs:79`: con un
        /// `const true` il ramo `if (!X)` diventa irraggiungibile e il compilatore emette CS0162.
        /// </summary>
        private static readonly bool NOTICE_ON_TEAMMATE_PAUSED = true;

        /// <summary>Il testo della notifica: inglese, ASCII puro, una riga sola.</summary>
        private const string TEXT = "YOUR TEAMMATE IS PAUSED";

        // ---- il payload sul canale Attack: [AttackType.PauseRelay][verso]. Un byte, e non e' uno stato:
        //      e' la direzione del cambiamento. Vedi `Enums.cs` accanto a 200/201/202.
        private const byte WIRE_PAUSED = 1;
        private const byte WIRE_RESUMED = 0;

        // ---- il battito e la scadenza, in SECONDI NON SCALATI (vedi il commento di classe).
        //      3 s di battito e 8 s di scadenza: due battiti persi di fila non fanno cadere la notifica
        //      mentre l'altro e' ancora in pausa. E' anche il canale del RINVIO: se la pausa non si e'
        //      potuta applicare (slow-motion, fade, cambio livello), il battito la **ritenta**.
        private const float HEARTBEAT_SECONDS = 3f;
        private const float TIMEOUT_SECONDS = 8f;

        /// <summary>
        /// R2 — IL TETTO DEL RETRY. `FadeHide` rifiuta per `alphaDurantion = 0.4f`
        /// (`BasicUIBlockingWidget.cs:16`, `:54`): 2 s sono cinque volte il fade, e sono un tetto vero -
        /// oltre, si **dichiara** la frenata e si tiene la pausa (mai `ResumeGame()` con un menu' aperto).
        /// </summary>
        private const float CLOSE_CAP_SECONDS = 2f;

        // ---- il fronte LOCALE: l'ultimo `IsPaused` osservato, e "sto annunciando una pausa".
        private static bool _wasPaused;
        private static bool _pauseTxAlive;
        private static float _nextHeartbeatAt;

        // ---- il fronte REMOTO: la notifica e' a schermo, da chi, e fino a quando.
        private static bool _remotePaused;
        private static float _remoteDeadline;
        private static string _remoteFrom = "?";

        // ---- ⭐ V2 — LA PAUSA REMOTA APPLICATA. `_remotePaused` dice "la notifica e' a schermo";
        //      `_pausedByRemote` dice "**la pausa in cui sono non e' mia**". Sono cose diverse nel caso
        //      che conta, e il flag ha due usi: (i) non rilanciare il fronte che ho subito (R3/rimbalzo),
        //      (ii) sapere **se** posso togliere la pausa (R4). La scena serve alla guardia "cambio di
        //      livello": una pausa in un'altra scena non ha piu' senso.
        private static bool _pausedByRemote;
        private static string _pausedScene = "";

        // ---- R2 — il retry della chiusura, per frame, con il tetto sopra.
        private static bool _closePending;
        private static int _closeAttempts;
        private static float _closeDeadline;
        private static string _closeCausa = "";

        // ---- R0 — il menu' l'ha aperto la MOD (non il giocatore): serve solo ai marker.
        private static bool _mapOpenedByUs;

        // ---- il caso "passivo morto" si dichiara UNA volta per pausa (vedi `StepRemotePauseGuards`).
        private static bool _deathLiftLogged;

        // ---- le due ricerche di scena, FATTE UNA VOLTA e tenute da parte: `FindObjectOfType` e' una
        //      SCANSIONE di scena e non va fatta a ogni frame (la regola e' gia' scritta in
        //      `Map\MapManager.cs:25`). `PauseWidget.Deactivate()` **non** disattiva il GameObject
        //      (`PauseWidget.cs:215-219`), quindi il riferimento resta valido anche a menu' chiuso.
        private static PauseWidget _pauseWidget;
        private static NewMapMenuWidget _mapWidget;

        // ---- diagnostica: i contatori che il referto cita, e i due "una volta sola".
        private static int _fronts;
        private static int _sent;
        private static int _received;
        private static int _applied;
        private static string _lastNotApplied = "";
        private static bool _toggleOffReported;

        /// <summary>
        /// UN GIRO PER FRAME, da `Multiplayer.OnLateUpdate`, **dopo** `NetworkManager.ProcessQueue()`
        /// (un fronte che arriva dal filo si applica prima di essere guardato) e **dopo**
        /// `NotificationManager.Update()`. L'ORDINE DEI QUATTRO PASSI E' SIGNIFICATIVO:
        ///   ① le guardie (R5) e il retry (R2) — possono far cadere la pausa **in questo frame**;
        ///   ② il fronte locale (R3) — cosi' la ripresa che ① ha appena prodotto **parte nello stesso
        ///      frame**, che e' esattamente il caso "passivo morto ⇒ manda RESUME";
        ///   ③ il congedo del flag — **dopo** ②, altrimenti il fronte di ② non avrebbe piu' il permesso
        ///      di R3 di mandare il pacchetto;
        ///   ④ le uscite del R4 — che possono, anche loro, far cadere la pausa.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                if (Main.Multiplayer == null)
                    return;

                if (!NOTICE_ON_TEAMMATE_PAUSED)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " notifica SPENTA dall'interruttore (NOTICE_ON_TEAMMATE_PAUSED=false): " +
                                    "nessun pacchetto, nessuna riga a schermo, nessuna pausa condivisa");
                    }
                    return;
                }

                StepRemotePauseGuards();   // R5 + il retry di R2
                ObserveLocalFront();       // il fronte locale + la riga di R3
                SettlePausedByRemote();    // il flag si congeda DOPO il fronte
                CheckRemoteStillPaused();  // R4
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        // ============================================================================================
        // R5 — LE GUARDIE DELLA PAUSA REMOTA, e il retry della chiusura.
        //
        // Tre domande, in quest'ordine, e ognuna ha una ragione misurata:
        //   ① **sto chiudendo?** ⇒ si ritenta (una cosa per frame: il retry ha la precedenza, perche' e'
        //      l'unico che ha una scadenza);
        //   ② **sono a terra con addosso una pausa remota?** ⇒ si chiude e si manda RESUME. Non e'
        //      cosmetica: la morte e' immune al `timeScale` (`DeadScreenWidget` usa `WaitForSecondsRealtime`
        //      e `Input.anyKey`), ma il **respawn** passa da `Core.UI.Fade.Fade(...)`, cioe' un tween:
        //      a `timeScale = 0` con `DOTween.defaultTimeScaleIndependent = false` il fade puo' non
        //      completarsi e il respawn non avvenire **mai**. E' la difesa contro il blocco;
        //   ③ **la scena e' cambiata mentre ero in pausa?** ⇒ il flag cade e **non si manda niente**:
        //      `IsPaused` non viene rimesso a `false` da un cambio di livello (solo `ResumeGame()` e
        //      `Initialize()` lo fanno), quindi una pausa "vecchia" in una scena nuova sarebbe uno stato
        //      senza senso in un altro mondo. Il pacchetto non servirebbe comunque: il server filtra per
        //      la scena del MITTENTE, che a quel punto e' gia' quella nuova.
        // ============================================================================================
        private static void StepRemotePauseGuards()
        {
            try
            {
                if (_closePending)
                {
                    RetryClose();
                    return;
                }

                if (!_pausedByRemote)
                    return;

                if (IsLocalDead())
                {
                    // ⭐ IL CASO "PASSIVO MORTO". Il flag RESTA in piedi per un frame: e' il fronte di ②
                    // a mandare il RESUME (una riga, R3), e `SettlePausedByRemote()` lo congeda dopo.
                    // La riga si stampa UNA volta per pausa: se la chiusura viene rinviata dal fade, il
                    // retry non deve diventare una raffica.
                    if (!_deathLiftLogged)
                    {
                        _deathLiftLogged = true;
                        ModLog.Info(MARK + " sciolta dalla MORTE locale · io=" + Me() + " · da=" + _remoteFrom +
                                    " · pausa caduta, motivo=morte-locale · la pausa era REMOTA: chiudo con il " +
                                    "dispatch a 3 rami e mando RESUME al pausante (il respawn passa da un tween: " +
                                    "a timeScale 0 puo' non completarsi, questa e' la difesa)");
                    }
                    CloseAndResume("morte-locale");
                    return;
                }

                string scena = SceneName();
                if (scena.Length > 0 && _pausedScene.Length > 0 && !string.Equals(scena, _pausedScene, StringComparison.Ordinal))
                {
                    // ⛔ niente pacchetto: si esce dal mondo vecchio, il RESUME userebbe la scena sbagliata.
                    _pausedByRemote = false;
                    ModLog.Info(MARK + " pausa caduta, motivo=cambio-scena · io=" + Me() + " · da=" + _remoteFrom +
                                " · scenaPrima=" + _pausedScene + " · scenaDopo=" + scena +
                                " · nessun pacchetto (il server filtra per la scena del mittente: arriverebbe nella stanza sbagliata)");
                    CloseAndResume("cambio-scena");
                }
            }
            catch (Exception e)
            {
                Fail("StepRemotePauseGuards", e);
            }
        }

        /// <summary>
        /// R2 — IL RETRY, con il tetto. Un "riprendi" che arriva nei **0,4 s** del fade-in non si perde:
        /// si ritenta a ogni frame finche' `IsPaused` resta vero. Al tetto **non** si ripiega su
        /// `ResumeGame()` (sarebbe il rischio "esci al menu principale"): si **tiene** la pausa e si
        /// scrive il motivo. Se poi e' il giocatore a chiudere, il fronte di ripresa manda il RESUME.
        /// </summary>
        private static void RetryClose()
        {
            try
            {
                if (Now() > _closeDeadline)
                {
                    _closePending = false;
                    ModLog.Info(MARK + " ripresa FRENATA · motivo=menu' in fade per piu' di " + CLOSE_CAP_SECONDS +
                                "s: la ripresa NON e' stata applicata · tentativi=" + _closeAttempts +
                                " · IsPaused=true · IsFading=" + PauseIsFading() +
                                " · la pausa RESTA (non si chiama ResumeGame con un menu' aperto) · causa=" + _closeCausa);
                    return;
                }

                CloseAndResume(_closeCausa);
            }
            catch (Exception e)
            {
                Fail("RetryClose", e);
            }
        }

        /// <summary>
        /// ⭐ IL FRONTE LOCALE. Si legge `IsPaused` (polling, una volta per frame) e si reagisce **solo al
        /// cambio**: `false→true` = qualcuno ha messo in pausa questa macchina; `true→false` = ne e' uscita.
        /// Finche' resta in pausa, ogni `HEARTBEAT_SECONDS` si rimanda lo STESSO fronte.
        ///
        /// ⭐ V2 — DUE CAMBIAMENTI, uno per verso:
        ///   · `false→true` **non** si rimanda se la pausa e' **remota** (`_pausedByRemote`): l'ho subita,
        ///     non decisa. Rimandarla sarebbe un hop in piu' e farebbe comparire **al pausante** la
        ///     notifica "il tuo compagno e' in pausa" mentre e' lui in pausa;
        ///   · `true→false` **deve** partire anche se non ho mai annunciato niente, **quando la pausa era
        ///     remota**: e' la riga R3 (`|| _pausedByRemote`). Senza, il passivo esce e il pausante
        ///     **resta in menu' per sempre**.
        /// </summary>
        private static void ObserveLocalFront()
        {
            bool paused = IsLocalPaused();

            if (paused != _wasPaused)
            {
                _wasPaused = paused;
                _fronts++;

                if (paused)
                {
                    if (_pausedByRemote)
                    {
                        ModLog.Info(MARK + " fronte=pausa NON rinviato · motivo=pausa-remota " +
                                    "(l'ho applicata io perche' l'ha chiesta il peer: rimandarla sarebbe un hop in piu', " +
                                    "e al pausante comparirebbe la notifica \"il compagno e' in pausa\" mentre e' lui in pausa) " +
                                    "· fronte n." + _fronts);
                    }
                    else if (TrySendFront(true))
                    {
                        _pauseTxAlive = true;
                        _nextHeartbeatAt = Now() + HEARTBEAT_SECONDS;
                    }
                }
                else
                {
                    // ⭐ R3 vive qui dentro: `TrySendFront(false)` passa anche quando la pausa era remota.
                    TrySendFront(false);
                    _pauseTxAlive = false;
                }

                return;
            }

            // ---- il battito: solo mentre sto annunciando una pausa MIA.
            if (paused && _pauseTxAlive && Now() >= _nextHeartbeatAt)
            {
                _nextHeartbeatAt = Now() + HEARTBEAT_SECONDS;
                TrySendFront(true);
            }
        }

        /// <summary>
        /// ⭐ V2 — IL CONGEDO DEL FLAG, e sta QUI per una ragione d'ordine precisa: **dopo**
        /// `ObserveLocalFront()`. Finche' `_pausedByRemote` e' vero, il fronte di ripresa ha il permesso di
        /// R3 di mandare il RESUME. Se lo si azzerasse prima (come sarebbe naturale), il caso "passivo
        /// morto" produrrebbe un RESUME che non parte - e il pausante resterebbe in ostaggio.
        /// </summary>
        private static void SettlePausedByRemote()
        {
            try
            {
                if (!_pausedByRemote)
                    return;
                if (IsLocalPaused())
                    return;

                _pausedByRemote = false;
                _pausedScene = "";
                _closePending = false;
                _closeAttempts = 0;
                _lastNotApplied = "";
                _deathLiftLogged = false;
            }
            catch (Exception e)
            {
                Fail("SettlePausedByRemote", e);
            }
        }

        // ============================================================================================
        // R1 — APPLICARE LA PAUSA. Chiamata da `Arm()`: sia al PRIMO fronte sia a ogni battito, perche'
        // il battito e' cio' che rende la guardia un **RINVIO e non una rinuncia** (slow-motion, fade,
        // cambio livello: la pausa arriva appena la condizione cade, al massimo 3 s dopo).
        //
        // LE GUARDIE, e ognuna ha una prova:
        //   `CurrentlyInLevel` / `CurrentLevelConfig` — `PauseGame()` dereferenzia `CurrentLevelConfig`
        //      senza guardia (`LogicManager.cs:250`) ⇒ NRE fuori livello;
        //   `InsideChangeLevel` — il livello si sta caricando: la pausa si applicherebbe a un mondo che
        //      sta sparendo;
        //   scene name vuoto — il pacchetto non arriverebbe comunque (il server filtra per scena) e
        //      `FadeShow` non avrebbe una scena valida;
        //   `IsLocalPlayerDead()` — a terra la pausa e' dannosa (il respawn e' un tween): si RINVIA;
        //   gia' in pausa **mia** — non la tocco e **non me ne attribuisco la proprieta'**: il mondo e' gia'
        //      fermo, ma alla ripresa non devo chiudere il suo menu';
        //   gia' in pausa **remota** — idempotente, silenzioso (e' il battito);
        //   `IsSlowMode()` — `Time.timeScale != 1` significa hit-stop, esecuzione, cutscene lenta:
        //      congelare ora cancellerebbe il rallentamento. ⇒ RINVIO (il battito ritenta);
        //   `FadeWidget.instance.Fading` — un fade di schermo in corso: la scheda mappa non si aprirebbe
        //      e il "riprendi" cadrebbe in una finestra di animazione. ⇒ RINVIO.
        // ============================================================================================
        private static void ApplyRemotePause(string from)
        {
            try
            {
                if (Main.Multiplayer == null)
                    return;

                // ⭐ IL BATTITO NON DEVE MAI RIMETTERE UNA PAUSA CHE E' GIA' STATA TOLTA. I due casi sono
                // distinguibili per costruzione: se `_pausedByRemote` e' vero, la pausa **l'ho applicata** -
                // quindi se ora `IsPaused` e' falso e' stata tolta (morte, chiusura del giocatore, cambio
                // scena) e il battito NON la rimette, altrimenti chi chiude il menu' verrebbe ricongelato
                // al battito successivo. Un rinvio vero (slow-motion, fade, cambio livello) ha il flag
                // **falso**, perche' in quel caso la pausa non e' mai stata applicata.
                if (_pausedByRemote)
                    return;

                if (!Main.Multiplayer.CurrentlyInLevel)
                {
                    NotApplied(from, "fuori-livello");
                    return;
                }

                if (SceneName().Length == 0)
                {
                    NotApplied(from, "scena-non-pronta (sceneName vuoto: il pacchetto non arriverebbe)");
                    return;
                }

                if (InsideChangeLevel())
                {
                    NotApplied(from, "in-cambio-livello");
                    return;
                }

                if (IsLocalDead())
                {
                    NotApplied(from, "io-a-terra (il respawn passa da un tween: a timeScale 0 puo' non completarsi)");
                    return;
                }

                if (IsLocalPaused())
                {
                    if (_lastNotApplied != "gia-in-pausa-mia")
                    {
                        _lastNotApplied = "gia-in-pausa-mia";
                        ModLog.Info(MARK + " pausa NON applicata · motivo=gia-in-pausa-mia · io=" + Me() +
                                    " · da=" + Show(from) + " · il mondo e' GIA' fermo per me (menu' mio): non tocco " +
                                    "niente e non mi attribuisco la proprieta' della pausa (`_pausedByRemote=false`)");
                    }
                    return;
                }

                if (SlowMode())
                {
                    NotApplied(from, "slow-motion (Time.timeScale != 1: hit-stop, esecuzione o cutscene lenta)");
                    return;
                }

                if (ScreenFading())
                {
                    NotApplied(from, "fade-a-schermo");
                    return;
                }

                if (LevelConfigMissing())
                {
                    NotApplied(from, "currentlevelconfig-assente (PauseGame dereferenzia senza guardia)");
                    return;
                }

                // ⭐ IL CANALE UFFICIALE DEL GIOCO. Una chiamata, nessuna scrittura di `Time.timeScale`.
                Core.Logic.PauseGame();

                _pausedByRemote = true;
                _pausedScene = SceneName();
                _closePending = false;
                _closeAttempts = 0;
                _lastNotApplied = "";
                _deathLiftLogged = false;
                _applied++;

                ModLog.Info(MARK + " RX pausa -> ho messo in pausa il passivo (io) · APPLICATA localmente con " +
                            "Core.Logic.PauseGame() (il canale UFFICIALE del gioco, non Time.timeScale) · io=" + Me() +
                            " · da=" + Show(from) + " · scena=" + _pausedScene +
                            " · IsShowingMenu=false · timeScale=0 dal prossimo frame · pausaRemota=true · applicate=" + _applied);

                // ⭐ R0 va DOPO R1, mai prima: se l'apertura fallisce, la pausa c'e' lo stesso.
                TryOpenMapOntoPassive(from);
            }
            catch (Exception e)
            {
                Fail("ApplyRemotePause", e);
            }
        }

        /// <summary>
        /// R0 — APRIRE LA MAPPA SUL PASSIVO (IDEA 1). E' **la chiamata identica** a quella del tasto pausa
        /// (`UIController.cs:440-442`), fatta con pezzi **tutti pubblici**: `FindObjectOfType di PauseWidget()`
        /// (la classe e' pubblica e il GameObject resta attivo anche a menu' chiuso, `PauseWidget.cs:215-219`),
        /// `InitialWidget` / `InitialMapMode` (setter pubblici) e `FadeShow(checkInput:true)`.
        ///
        /// PERCHE' `MAP` + `SHOW` e non altro:
        ///   · `ShowOptions()` apre la scheda **OPTIONS**, dove vivono `ExitToMainMenuOption` e il reset ⇒ scartata;
        ///   · `ShowMapTeleport()` apre **MAP + TELEPORT**, dove il submit e' un **teletrasporto** ⇒ scartata;
        ///   · `MAP + SHOW` e' **la scheda del tasto pausa**, e da li' l'ESC chiude in **una** pressione
        ///     (`NewMapMenuWidget.GoBack()` restituisce `false` sempre ⇒ `PauseWidget.GoBack` e' falso ⇒
        ///     `FadeHide`).
        ///
        /// ⚠️ Se una delle cinque guardie di `FadeShow` fallisce **non si apre niente** e va bene cosi': la
        /// pausa e' gia' in piedi (R1), il passivo puo' aprire il menu' da se' con il tasto pausa. Non si ritenta
        /// a ogni frame: `FindObjectOfType` e' una scansione di scena, e un menu' che non si apre non
        /// diventa piu' apribile ritentando.
        /// </summary>
        private static void TryOpenMapOntoPassive(string from)
        {
            try
            {
                UIController ui = SafeUI();
                if (ui != null && SafeIsShowingMenu(ui))
                {
                    OpenFailed(from, "gia-un-menu-aperto (si lascia il suo: FadeShow ha la guardia !IsActive())");
                    return;
                }

                PauseWidget widget = PauseWidgetCached();
                if (widget == null)
                {
                    OpenFailed(from, "widget-non-trovato (FindObjectOfType<PauseWidget>() = null)");
                    return;
                }

                widget.InitialWidget = PauseWidget.ChildWidgets.MAP;
                widget.InitialMapMode = PauseWidget.MapModes.SHOW;
                widget.FadeShow(true);

                if (!widget.IsActive())
                {
                    OpenFailed(from, "FadeShow rifiutata da una delle sue 5 guardie (la pausa c'e' lo stesso)");
                    return;
                }

                _mapOpenedByUs = true;
                ModLog.Info(MARK + " RX pausa -> ho aperto la MAPPA sul passivo · scheda=MAP/SHOW (la stessa del " +
                            "tasto pausa, UIController.cs:440-442) · widget=TROVATO · InitialWidget=MAP · mode=SHOW · " +
                            "io=" + Me() + " · da=" + Show(from) + " · FadeShow(checkInput:true) ACCETTATA · " +
                            "il giocatore VEDE di essere in pausa e chiude con UNA pressione · " +
                            "(FadeShow mette anche UIBLOCKING + Unresponsive: lo stato diventa quello che il gioco produce da se')");
            }
            catch (Exception e)
            {
                Fail("TryOpenMapOntoPassive", e);
            }
        }

        private static void OpenFailed(string from, string motivo)
        {
            ModLog.Info(MARK + " RX pausa -> scheda mappa NON aperta · motivo=" + motivo + " · io=" + Me() +
                        " · da=" + Show(from) + " · la pausa REMOTA c'e' comunque (R1 e' passata prima di R0): " +
                        "il passivo la chiude con il tasto pausa");
        }

        /// <summary>
        /// R2 — CHIUDERE E RIPRENDERE. **Un dispatch a tre rami con predicati pubblici**, e ognuno dei
        /// primi due **fa la ripresa da solo**:
        ///   ① `IsShowingInventory` ⇒ `HideInventory()` → `Show(false)` → `ResumeGame()` (`NewInventoryWidget.cs:218`),
        ///      e toglie il blocker `INVENTORY` (`:166`). Sincrono, nessun tween.
        ///   ② `IsShowingMenu` ⇒ `HidePauseMenu()` → `pauseWidget.FadeHide()` → `ResumeGame()` +
        ///      `SetState(Playing)` + `EndFadeHide` toglie `UIBLOCKING` (`BasicUIBlockingWidget.cs:59-63`,
        ///      `:130-139`). **E' il "riprendi" del gioco**: `OptionsWidget.Option_ResumeSecure()` fa
        ///      esattamente questa chiamata (`:707-712`).
        ///   ③ nessun menu' ⇒ `ResumeGame()` **se `IsPaused`**, altrimenti niente. E' l'unico posto in cui
        ///      si riprende "a mani nude", ed e' lecito **perche' non c'e' nessun menu' da lasciare orfano**:
        ///      lo stato proibito (`IsPaused=false` con un menu' aperto) **non si produce mai**.
        /// ⛔ Nel ramo ③ **non si riprende se `Core.Input.InputBlocked`**: un blocker acceso significa
        ///    "qualcosa e' aperto, e non e' uno dei due che so chiudere" (`extrasMenu`, tutorial, sblocco...):
        ///    meglio restare in pausa che produrre uno stato ibrido. Sul passivo il blocker e' **falso per
        ///    costruzione** (`PauseGame` non ne mette), quindi questa condizione non blocca il caso normale.
        ///
        /// ⚠️ IL RETRY. Se dopo la chiamata `IsPaused` e' ancora vero, la chiusura e' stata **rifiutata**
        /// (`FadeHide` esce se `IsFading`) ⇒ si riprova a ogni frame, con il tetto `CLOSE_CAP_SECONDS`.
        /// </summary>
        private static void CloseAndResume(string causa)
        {
            try
            {
                UIController ui = SafeUI();
                bool menuPrima = SafeIsShowingMenu(ui);
                bool invPrima = SafeIsShowingInventory(ui);
                bool pausaPrima = IsLocalPaused();
                string modo;

                if (ui != null && invPrima)
                {
                    ui.HideInventory();
                    modo = "HideInventory";
                }
                else if (ui != null && menuPrima)
                {
                    ui.HidePauseMenu();
                    modo = "HidePauseMenu";
                }
                else if (InputBlocked())
                {
                    modo = "NESSUNO: ramo 3 NON eseguito (input bloccato da un widget che IsShowingMenu non copre)";
                }
                else if (pausaPrima)
                {
                    Core.Logic.ResumeGame();
                    modo = "ResumeGame";
                }
                else
                {
                    modo = "niente-da-fare (non ero in pausa)";
                }

                bool pausaDopo = IsLocalPaused();
                bool menuDopo = SafeIsShowingMenu(ui);

                if (pausaDopo)
                {
                    // ⭐ la chiusura non ha ripreso il gioco: si ritenta, non si ripiega.
                    if (!_closePending)
                    {
                        _closePending = true;
                        _closeAttempts = 0;
                        _closeDeadline = Now() + CLOSE_CAP_SECONDS;
                        _closeCausa = causa;
                    }
                    _closeAttempts++;

                    if (_closeAttempts <= 3 || _closeAttempts % 30 == 0)
                    {
                        ModLog.Info(MARK + " riprendi rimandato · motivo=la chiusura non ha ripreso il gioco " +
                                    "(FadeHide rifiuta mentre IsFading, 0,4 s) · modo=" + modo +
                                    " · tentativo=" + _closeAttempts + "/" + (int)(CLOSE_CAP_SECONDS * 60f) +
                                    " · IsShowingMenu dopo=" + menuDopo + " · IsFading=" + PauseIsFading() +
                                    " · causa=" + causa + " · si ritenta al prossimo frame (un \"riprendi\" nei 0,4 s " +
                                    "del fade NON si perde)");
                    }
                    return;
                }

                bool ritentata = _closePending;
                int tentativi = _closeAttempts + 1;
                _closePending = false;
                _closeAttempts = 0;

                ModLog.Info(MARK + " ho chiuso e ripreso · modo=" + modo + " · causa=" + causa +
                            " · IsShowingMenu prima=" + menuPrima + " dopo=" + menuDopo +
                            " · IsPaused prima=" + pausaPrima + " dopo=" + pausaDopo +
                            " · riprendeIlGioco=" + (pausaPrima && !pausaDopo) +
                            " · tentativi=" + tentativi + (ritentata ? " (dopo un rinvio)" : "") +
                            " · pausaRemota=" + _pausedByRemote +
                            " · schedaApertaDallaMod=" + _mapOpenedByUs +
                            " · mappaAschermoDopo=" + MapOnScreenAfterMarker() +
                            " · il RESUME non e' mai chiamato con un menu' aperto (dispatch a 3 rami)");
            }
            catch (Exception e)
            {
                Fail("CloseAndResume", e);
            }
        }

        /// <summary>
        /// MANDA UN FRONTE, se le guardie lo permettono, e scrive SEMPRE il perche'. Le guardie sono quelle
        /// del §7 del referto di studio, tutte su predicati che esistono gia':
        ///   `CurrentlyInLevel` (fuori livello `CurrentLevelConfig` puo' essere null: e' la NRE n.2),
        ///   `IsConnected`, `ConnectedPlayersCount() > 0`, e poi - **solo per la pausa** -
        ///   `IsLocalPlayerDead()` e `AnyOtherPlayerHereAndAlive()`.
        /// ⚠️ La RIPRESA non chiede "sono vivo": togliere una pausa e' sempre lecito, e se sono morto e'
        ///    proprio la morte ad aver prodotto il fronte.
        /// ⭐ R3 (V2) — UNA CONDIZIONE IN PIU', ed e' obbligatoria: `|| _pausedByRemote`. Senza, il
        ///    **passivo** non potrebbe mai mandare la sua ripresa (non ha mai "annunciato" una pausa:
        ///    `_pauseTxAlive` e' falso) e il **pausante resterebbe in menu' per sempre**.
        /// </summary>
        private static bool TrySendFront(bool isPause)
        {
            string tipo = isPause ? "pausa" : "ripresa";

            if (!isPause && !_pauseTxAlive && !_pausedByRemote)
            {
                ModLog.Info(MARK + " fronte=ripresa NON inviato · motivo=nessuna-pausa-annunciata " +
                            "(non e' mai partito un annuncio di pausa, e la pausa non era remota: non c'e' niente da ritirare)");
                return false;
            }

            string motivo = GateReason(isPause);
            if (motivo != null)
            {
                ModLog.Info(MARK + " fronte=" + tipo + " NON inviato · motivo=" + motivo +
                            " · io=" + Me() + " · peerInScena=" + (EnemySyncManager.AnyOtherPlayerInMyScene() ? 1 : 0) +
                            "/" + EnemySyncManager.ConnectedPlayersCount() + " · fronte n." + _fronts);
                return false;
            }

            byte[] payload = new byte[] { (byte)AttackType.PauseRelay, isPause ? WIRE_PAUSED : WIRE_RESUMED };
            Main.Multiplayer.NetworkManager.SendAttackPayload(payload);
            _sent++;

            ModLog.Info(MARK + " TX fronte=" + tipo + " -> canale=Attack tipo=" + (byte)AttackType.PauseRelay +
                        " verso=" + payload[1] + " · io=" + Me() +
                        " · peerInScena=" + (EnemySyncManager.AnyOtherPlayerInMyScene() ? 1 : 0) +
                        "/" + EnemySyncManager.ConnectedPlayersCount() +
                        " · inviati=" + _sent + " · fronte n." + _fronts +
                        (isPause ? "" : " · la ripresa parte anche se la pausa era REMOTA (R3: senza questa riga il " +
                                        "pausante resterebbe in menu' per sempre)") +
                        " · il pacchetto esce da SendQueue() nello stesso frame");

            return true;
        }

        /// <summary>
        /// LE GUARDIE, in ordine di costo: chi non passa riceve una ragione in chiaro, cosi' "non e'
        /// partito" non e' mai un silenzio. `null` = si puo' mandare.
        /// </summary>
        private static string GateReason(bool isPause)
        {
            if (Main.Multiplayer.NetworkManager == null)
                return "network-manager-assente";
            if (!Main.Multiplayer.CurrentlyInLevel)
                return "fuori-livello";
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return "non-connesso";
            if (EnemySyncManager.ConnectedPlayersCount() <= 0)
                return "nessun-peer-connesso";
            if (!isPause)
                return null;

            if (PlayerDeathSync.IsLocalPlayerDead())
                return "io-a-terra";
            if (!EnemySyncManager.AnyOtherPlayerHereAndAlive())
                return "peer-non-qui-o-a-terra";

            return null;
        }

        /// <summary>
        /// ⭐ LA RICEZIONE, chiamata da `NetworkManager.ReceiveAttack` **prima** del ramo PvP. Il payload
        /// e' `[attackType][verso]` e il nome del mittente lo mette il server davanti, quindi `startIdx`
        /// punta gia' oltre il nome.
        ///
        /// `pausa` ⇒ si ARMA (o si RINNOVA, se e' un battito) **e si applica** (R1 + R0).
        /// `ripresa` ⇒ si TOGLIE la notifica **e si chiude** (R2) — `Disarm` porta con se' il dispatch.
        /// ⚠️ Non si manda niente indietro da qui: il fronte locale di `ObserveLocalFront` e' l'unico posto
        ///    da cui parte un pacchetto, e per la pausa remota non parte (R3 la lascia passare solo in
        ///    uscita).
        /// </summary>
        internal static void ApplyFromWire(string from, byte[] data, int startIdx)
        {
            try
            {
                if (data == null || startIdx < 0 || data.Length <= startIdx + 1)
                {
                    ModLog.Info(MARK + " RX MALFORMATO da " + Show(from) + ": manca il byte di verso " +
                                "(payload di " + (data == null ? 0 : data.Length) + " byte, indice " + startIdx +
                                ") - ignorato");
                    return;
                }

                byte verso = data[startIdx + 1];
                _received++;

                if (verso == WIRE_PAUSED)
                {
                    Arm(from);
                }
                else if (verso == WIRE_RESUMED)
                {
                    bool cEra = _remotePaused;
                    ModLog.Info(MARK + " RX fronte=ripresa da=" + Show(from) + " · ricevuti=" + _received +
                                (cEra
                                    ? " · la notifica e' stata tolta SUBITO (non si aspetta il timeout)"
                                    : " · nessuna notifica era a schermo (idempotente: non c'era niente da togliere)"));
                    // ⚠️ `Disarm` fa anche la CHIUSURA (R2) e il flag cade PRIMA, di proposito: la ripresa
                    // l'ha chiesta il peer, quindi non c'e' niente da rimandargli (il fronte locale non
                    // deve produrre un secondo pacchetto).
                    // `chiudiSempre: true` = la REGOLA dell'utente ("chi esce per primo
                    // porta fuori tutti"): la ripresa richiesta chiude anche un menu' che il giocatore aveva
                    // aperto per conto suo. Le uscite di R4, invece, non toccano un menu' che non e' il nostro.
                    Disarm("ricevuta ripresa da " + Show(from), true);
                }
                else
                {
                    ModLog.Info(MARK + " RX con verso IGNOTO (" + verso + ") da " + Show(from) +
                                ": un verso che questa build non conosce - ignorato");
                }
            }
            catch (Exception e)
            {
                Fail("ApplyFromWire", e);
            }
        }

        /// <summary>
        /// ARMA la notifica (o la rinnova) **e applica la pausa** (R1 + R0). Anche sul battito: e' li' che
        /// una pausa rinviata (slow-motion, fade, cambio livello) viene **ritentata**.
        /// Il testo e' fisso: con due soli giocatori "il compagno" e' gia' una risposta.
        /// </summary>
        private static void Arm(string from)
        {
            _remoteDeadline = Now() + TIMEOUT_SECONDS;

            bool gia = _remotePaused;
            _remoteFrom = Show(from);

            if (Main.Multiplayer.NotificationManager == null)
            {
                ModLog.Error(MARK + " RX fronte=pausa da " + _remoteFrom + " ma NotificationManager non e' " +
                             "disponibile: nessuna riga a schermo (lo stato resta armato lo stesso)");
                _remotePaused = true;
                ApplyRemotePause(from);
                return;
            }

            // Idempotente sullo stesso testo: chiamarla a ogni battito non crea righe in piu'.
            Main.Multiplayer.NotificationManager.DisplayStickyNotification(TEXT);

            if (gia)
            {
                // Battito di conferma: si rinnova la scadenza e basta, senza sporcare il log — ma la
                // pausa si RITENTA (e' il rinvio, non la rinuncia).
                ApplyRemotePause(from);
                return;
            }

            _remotePaused = true;
            _lastNotApplied = "";

            ModLog.Info(MARK + " RX fronte=pausa da=" + _remoteFrom + " · notifica A SCHERMO \"" + TEXT + "\" " +
                        "· resta finche' non arriva la ripresa o non scade il timeout di " +
                        TIMEOUT_SECONDS + "s (battito ogni " + HEARTBEAT_SECONDS + "s) · ricevuti=" + _received +
                        " · io=" + Me() + " · V2: a questa riga segue l'APPLICAZIONE della pausa " +
                        "(Core.Logic.PauseGame, canale ufficiale del gioco) e il tentativo di aprire la mappa");

            ApplyRemotePause(from);
        }

        /// <summary>
        /// ⭐⭐ R4 — LA PAUSA CADE DA SOLA, e questo e' l'unico punto in cui puo' cadere. **Senza timeout
        /// aggiuntivo**: le uscite sono i predicati, e sono istantanee.
        ///   · **disconnesso** — la connessione cade, `IsConnected` lo dice subito;
        ///   · **nessun peer connesso**;
        ///   · **il peer non e' piu' in questa stanza, o e' a terra** (`AnyOtherPlayerHereAndAlive()`);
        ///   · **tornato al menu principale** (`CurrentlyInLevel` falso).
        /// E, per ultima, il **timeout di 8 s**: resta per la build diversa, che sulla rete e'
        /// indistinguibile (protocollo 11 uguale per tutte) — ed e' dichiarato che l'unico caso che il
        /// timeout copriva, la **disconnessione**, e' coperto **istantaneamente** dalle prime tre uscite.
        ///
        /// Ogni uscita porta la pausa con se' (`Disarm` ⇒ `pausa caduta, motivo=...` ⇒ dispatch a 3 rami).
        /// </summary>
        private static void CheckRemoteStillPaused()
        {
            try
            {
                if (!_remotePaused)
                    return;

                if (Now() > _remoteDeadline)
                {
                    Disarm("TIMEOUT di " + TIMEOUT_SECONDS + "s scaduto: nessun battito da " + _remoteFrom +
                           " (build diversa: il protocollo non distingue le build, quindi il timeout e' l'unica difesa)");
                    return;
                }

                if (Main.Multiplayer.NetworkManager == null || !Main.Multiplayer.NetworkManager.IsConnected)
                {
                    Disarm("disconnesso dal server");
                    return;
                }

                if (!Main.Multiplayer.CurrentlyInLevel)
                {
                    Disarm("io sono tornato al menu principale");
                    return;
                }

                if (EnemySyncManager.ConnectedPlayersCount() <= 0)
                {
                    Disarm("nessun peer piu' connesso");
                    return;
                }

                if (!EnemySyncManager.AnyOtherPlayerHereAndAlive())
                {
                    Disarm("il peer non e' piu' in questa stanza o e' a terra");
                }
            }
            catch (Exception e)
            {
                Fail("CheckRemoteStillPaused", e);
            }
        }

        /// <summary>
        /// Toglie la notifica, se c'e', dice perche' **e porta via la pausa** (R4 + R2), con lo stesso
        /// dispatch a tre rami.
        ///
        /// DUE CAUSE DIVERSE, e vanno distinte:
        ///   · **`chiudiSempre = true`** — arriva dalla **richiesta del peer** ("riprendi").
        ///     Qui la regola e' quella chiesta dall'utente: **chi esce per primo porta fuori tutti**. Se la
        ///     pausa in cui sono era **mia** (ero gia' in menu' quando e' arrivata la sua), il dispatch a 3
        ///     rami chiude il **mio** menu' con le API del gioco - lecito, e senza mai produrre lo stato
        ///     "mondo che gira + menu' aperto";
        ///   · **`false`** — le uscite di R4 (timeout, disconnessione, stanza, menu' principale): la pausa
        ///     si porta via **solo se era remota**, perche' un menu' aperto dal giocatore non e' affare della
        ///     rete.
        /// Il flag cade **prima** del dispatch, di proposito: da qui non si manda nessun pacchetto.
        /// </summary>
        private static void Disarm(string reason, bool chiudiSempre = false)
        {
            try
            {
                bool cEra = _remotePaused;
                bool pausaRemota = _pausedByRemote;

                if (cEra)
                {
                    _remotePaused = false;

                    try
                    {
                        if (Main.Multiplayer != null && Main.Multiplayer.NotificationManager != null)
                            Main.Multiplayer.NotificationManager.ClearStickyNotification();
                    }
                    catch (Exception)
                    {
                        // Se la UI non si toglie, il testo sparisce al prossimo `LevelLoaded()`: non si rilancia.
                    }

                    ModLog.Info(MARK + " NOTIFICA TOLTA · motivo=" + reason + " · da=" + _remoteFrom +
                                " · ricevuti=" + _received);
                }

                // ⭐ QUANDO SI CHIUDE, e sono DUE cause diverse:
                //   · la pausa in cui sono e' **REMOTA** ⇒ si chiude sempre (R4 e R2 coincidono);
                //   · **`chiudiSempre`** (la ripresa l'ha chiesta il peer) ⇒ si chiude **se c'e' qualcosa da
                //     chiudere**: `IsPaused` o un menu' aperto. E' la meta' che manca senza questa riga:
                //     il passivo esce, manda il RESUME (R3) e il **pausante**, che non ha mai ricevuto un
                //     PAUSE e quindi non ha nessun flag alzato, resterebbe in menu' **per sempre**. Il
                //     dispatch a 3 rami chiude il SUO menu' con le API del gioco - lecito, e senza mai
                //     produrre lo stato "mondo che gira + menu' aperto".
                //   · `false` (le uscite di R4) ⇒ la pausa si porta via **solo se era remota**: un menu'
                //     aperto dal giocatore non e' affare della rete.
                UIController uiOra = SafeUI();
                bool occupato = IsLocalPaused() || SafeIsShowingMenu(uiOra);

                bool daChiudere = pausaRemota || (chiudiSempre && occupato);
                if (!daChiudere)
                    return;

                _pausedByRemote = false;
                _pausedScene = "";
                _lastNotApplied = "";
                _deathLiftLogged = false;

                if (pausaRemota)
                {
                    ModLog.Info(MARK + " pausa caduta, motivo=" + reason + " · io=" + Me() + " · da=" + _remoteFrom +
                                " · la pausa era REMOTA: la toglie il gioco con il dispatch a 3 rami " +
                                "(mai ResumeGame con un menu' aperto) · nessun pacchetto (nessuna uscita di R4 ha " +
                                "un peer da avvisare: la disconnessione, la stanza, il menu' principale e il timeout " +
                                "sono tutte cause LOCALI o gia' note al peer)");
                }
                else
                {
                    ModLog.Info(MARK + " pausa caduta, motivo=" + reason + " · io=" + Me() + " · da=" + _remoteFrom +
                                " · la pausa NON era remota (era il MIO menu') ma la ripresa l'ha chiesta il peer: " +
                                "la regola e' \"chi esce per primo porta fuori tutti\" ⇒ chiudo con il dispatch a 3 " +
                                "rami (Mai ResumeGame con un menu' aperto)");
                }

                CloseAndResume("pausa-caduta: " + reason);
            }
            catch (Exception e)
            {
                Fail("Disarm", e);
            }
        }

        // ============================================================================================
        // LE LETTURE. Tutte con `try/catch`: sono il punto in cui una pausa potrebbe rompere la partita,
        // e ogni predicato e' PUBBLICO e usato dal gioco per se' stesso.
        // ============================================================================================

        /// <summary>Lo stato del gioco locale, senza mai lanciare.</summary>
        private static bool IsLocalPaused()
        {
            try
            {
                return Core.Logic != null && Core.Logic.IsPaused;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>`PauseGame()` dereferenzia `CurrentLevelConfig` senza guardia: se e' null, e' NRE.</summary>
        private static bool LevelConfigMissing()
        {
            try
            {
                return Core.Logic == null || Core.Logic.CurrentLevelConfig == null;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>`Time.timeScale != 1` — vero in pausa **e** in slow-motion: qui si usa solo per il verso
        /// giusto (non congelare durante un hit-stop), **mai** per riprendere (`ResumeGame` non scrive
        /// niente se non e' in pausa).</summary>
        private static bool SlowMode()
        {
            try
            {
                return Core.Logic != null && Core.Logic.IsSlowMode();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool InsideChangeLevel()
        {
            try
            {
                return Core.LevelManager != null && Core.LevelManager.InsideChangeLevel;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Il nome della scena, in una forma che non lancia mai; `""` = non pronta.</summary>
        private static string SceneName()
        {
            try
            {
                if (Core.LevelManager == null || Core.LevelManager.currentLevel == null)
                    return string.Empty;
                string nome = Core.LevelManager.currentLevel.LevelName;
                return nome ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsLocalDead()
        {
            try
            {
                return PlayerDeathSync.IsLocalPlayerDead();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Il fade di SCHERMO (non quello di un widget): `FadeWidget.Fading` e' pubblico.</summary>
        private static bool ScreenFading()
        {
            try
            {
                return FadeWidget.instance != null && FadeWidget.instance.Fading;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool InputBlocked()
        {
            try
            {
                return Core.Input.InputBlocked;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static UIController SafeUI()
        {
            try
            {
                return UIController.instance;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool SafeIsShowingMenu(UIController ui)
        {
            try
            {
                return ui != null && ui.IsShowingMenu;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SafeIsShowingInventory(UIController ui)
        {
            try
            {
                return ui != null && ui.IsShowingInventory;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Il widget del menu' di pausa, cercato **una volta** e tenuto: `FindObjectOfType` e' una scansione
        /// di scena (regola gia' scritta in `Map\MapManager.cs:25`). `PauseWidget.Deactivate()` non
        /// disattiva il GameObject, quindi il riferimento sopravvive alla chiusura.
        /// </summary>
        private static PauseWidget PauseWidgetCached()
        {
            try
            {
                if (_pauseWidget != null)
                    return _pauseWidget;
                _pauseWidget = UnityEngine.Object.FindObjectOfType<PauseWidget>();
                return _pauseWidget;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string PauseIsFading()
        {
            try
            {
                PauseWidget w = PauseWidgetCached();
                return w == null ? "widget-assente" : w.IsFading.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>
        /// ⚠️ LA TRAPPOLA ③ — `PauseWidget.Deactivate()` **non** fa `SetActive(false)` sul GameObject
        /// (`PauseWidget.cs:215-219`: sposta il widget di `Vector3.up * 1000f` e azzera `isActive`), e i
        /// figli (mappa/opzioni) restano attivi finche' `OnWidgetShow` non li rimette. Quindi la domanda
        /// "la scheda mappa e' ancora a schermo DOPO la chiusura?" va MISURATA, non dedotta: questa sonda
        /// fa **la stessa domanda di `MapManager.IsMapOnScreen()`** (riferimento tenuto da parte +
        /// `gameObject.activeInHierarchy`) con la stessa API, una volta per chiusura.
        /// </summary>
        private static string MapOnScreenAfterMarker()
        {
            try
            {
                if (_mapWidget == null)
                    _mapWidget = UnityEngine.Object.FindObjectOfType<NewMapMenuWidget>();
                if (_mapWidget == null)
                    return "nessun-widget-mappa-attivo (FindObjectOfType non lo trova)";
                return _mapWidget.gameObject.activeInHierarchy
                    ? "true (il GameObject della mappa resta ATTIVO: PauseWidget.Deactivate non fa SetActive(false))"
                    : "false";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>L'orologio di TUTTA questa classe: non scalato, quindi vivo a `timeScale = 0`.</summary>
        private static float Now()
        {
            return Time.unscaledTime;
        }

        private static string Me()
        {
            try
            {
                string name = Main.Multiplayer == null ? null : Main.Multiplayer.PlayerName;
                return string.IsNullOrEmpty(name) ? "(senza-nome)" : name;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string Show(string name)
        {
            return string.IsNullOrEmpty(name) ? "?" : name;
        }

        /// <summary>
        /// La pausa NON e' stata applicata. Si scrive **una volta per motivo** (non a ogni battito: il
        /// battito la ritenta ogni 3 s, e 20 righe identiche non aggiungono niente).
        /// </summary>
        private static void NotApplied(string from, string motivo)
        {
            if (_lastNotApplied == motivo)
                return;
            _lastNotApplied = motivo;

            ModLog.Info(MARK + " pausa NON applicata · motivo=" + motivo + " · io=" + Me() + " · da=" + Show(from) +
                        " · il battito ogni " + HEARTBEAT_SECONDS + "s la RITENTA (e' un rinvio, non una rinuncia)");
        }

        /// <summary>L'ultimo `catch` prima del ciclo per frame: non rilancia mai.</summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (la pausa condivisa non ha interrotto niente: la partita e' proseguita)");
            }
            catch (Exception)
            {
                // Se anche il log fallisce non c'e' un secondo posto dove dirlo.
            }
        }
    }
}
