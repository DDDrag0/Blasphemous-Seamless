using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using Gameplay.UI.Others.Buttons;
using System;
using System.Globalization;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// LA VIA D'USCITA GARANTITA PER IL GIOCATORE MORTO — la lezione del guasto "il peer morto non
    /// respawna piu': i tasti non funzionano".
    ///
    /// ============================================================================================
    /// IL GUASTO, e la riga di log che lo prova (sessione G1, `output_log G1.txt`).
    ///
    ///   riga 2510  `[TRACE][INPUT] The input blocker <color=green>DEAD_SCREEN</color> has been enabled.`
    ///   ...        e NON esiste, in tutto il log, la riga corrispondente `... DEAD_SCREEN ... disabled`.
    ///
    /// Lo `stack` dei blocker ricostruito riga per riga su tutto il log dice, alla fine della sessione:
    ///
    ///   `FADE`        -> +@3159  (mai spento)
    ///   `DEAD_SCREEN` -> +@2510  (mai spento)
    ///
    /// e il giocatore e' rimasto morto **fino alla chiusura del gioco** (`[CorpseFollow] finestra chiusa:
    /// disconnessione / fine sessione` alla riga 3345, con la finestra di morte ancora aperta).
    /// ⭐ LA PROVA CHE IL RESPAWN NON E' MAI PARTITO, e non e' una deduzione: `Penitent.Respawn()` ha un
    /// `Prefix` di Harmony che logga **sempre** la sua prima riga
    /// (`PlayerDeathSync.cs`, `PenitentRespawn_Patch`: `[Morte] il giocatore e' morto: connesso=...`).
    /// **Quella riga non compare: `Respawn()` non e' stato invocato nemmeno una volta.**
    /// E non compare nemmeno `[TRACE][FADE] Called fade function. Active: False`, cioe' la riga che
    /// `FadeWidget.FadeAfterDelay` scrive SEMPRE alla fine di un fade (FadeWidget.cs:133): l'undicesima e
    /// ultima occorrenza e' `Active: True` (@3163, il fade della cutscene di morte del boss), e il suo
    /// completamento non e' mai arrivato.
    ///
    /// ⇒ **Il respawn NON e' stato rifiutato: non e' mai stato chiesto.** `DeadScreenWidget.OnDeadAction`
    /// (:87-96) esce da `while (!Input.anyKey)` e SOLO ALLORA fa `Core.UI.Fade.Fade(...)` e SOLO nel suo
    /// callback chiama `Core.Logic.Penitent.Respawn()`. Nessun fade, nessun `Respawn`: la lettura dei tasti
    /// di quella schermata non e' arrivata a destinazione.
    ///
    /// ============================================================================================
    /// PERCHE' QUESTA RETE E NON UNA TOCCATA AI BLOCKER.
    ///
    /// ⛔ `Core.Input.ResetManager()` azzera **TUTTI** i blocker (`InputManager.cs:181-185` →
    ///    `RemoveBlockers()`), quindi usarlo come riflesso riaprirebbe l'input **durante un'altra sequenza**
    ///    (una cutscene, un dialogo, un'esecuzione). E' il divieto esplicito del mandato.
    /// ⛔ `DEAD_SCREEN` e' **del gioco** e lo spegne `OnPenitentReady` (`DeadScreenWidget.cs:101`):
    ///    non lo si tocca.
    /// ✅ E non serve toccarli, perche' **l'uscita buona esiste gia' ed e' quella del gioco**:
    ///    `Core.Logic.Penitent.Respawn()` (`Penitent.cs:310-315`) → `SpawnManager.Respawn()` →
    ///    `SpawnPlayer(..., forceLoad: true)`, cioe' **un vero load di livello**, e il load e' l'unico
    ///    punto in cui il gioco rimette a posto tutto da solo:
    ///      - `LevelManager` : `ResetManager()` (:355) ⇒ blocker azzerati dal GIOCO;
    ///      - `FadeWidget.instance.StartEasyFade(..., toBlack: false)` (:354) ⇒ il fade riaperto dal GIOCO;
    ///      - `CreatePlayer` → `SpawnManager.OnPlayerSpawn` (:505-508) ⇒ `DeadScreenWidget.OnPenitentReady`
    ///        ⇒ `SetBlocker("DEAD_SCREEN", false)` e `HideAll()`.
    ///    **Il watchdog non ripara i blocker: chiede al gioco di fare la cosa che li ripara.**
    ///
    /// LE DUE RETI, e sono indipendenti di proposito.
    ///
    ///   RETE 1 — "il tasto premuto dal morto". Il ciclo per frame della mod gira **anche quando il gioco
    ///   non legge piu' i tasti** (e' il ciclo che ha continuato a misurare `[DeathScreen] misura` fino
    ///   all'ultima riga del log). Qui si guarda `UnityEngine.Input` **in proprio** e, alla prima pressione
    ///   vera, si chiama `Respawn()`. E' la stessa cosa che la schermata di morte farebbe: se lei ci
    ///   arriva, questa rete e' un **no-op** (il giocatore e' gia' risorto quando il tasto arriva); se non
    ///   ci arriva, questa e' l'unica strada.
    ///
    ///   RETE 2 — "il fusibile". Se il giocatore resta morto oltre `ESCAPE_SECONDS` con la schermata di
    ///   morte aperta e **nessuna** sequenza in corso (niente `CINEMATIC`, niente `DIALOG`, niente
    ///   cutscene di `Cinematics`, niente dialogo), il respawn viene invocato **anche senza tasti**. Un
    ///   giocatore bloccato non puo' piu' giocare: e' il guasto peggiore possibile, e a quel punto il
    ///   respawn e' **esattamente cio' che il gioco avrebbe fatto** al primo tasto.
    ///
    /// ⚠️ LE DUE CONDIZIONI CHE ARMANO LA RETE 1, e non sono pignoleria.
    ///   - `KEY_ARM_SECONDS = 2.5 s`: il tasto che ha **ucciso** il giocatore (o quello premuto mentre
    ///     moriva) non deve essere riletto come "voglio respawnare". La schermata di morte del gioco non ha
    ///     questo problema perche' guarda `Input.anyKey` — che e' vero **mentre** il tasto e' tenuto giu',
    ///     mentre qui si guarda la **pressione** (`GetKeyDown`), che e' un fronte.
    ///   - `CINEMATIC` spento e nessun dialogo: muoversi mentre una cutscene gira e' il secondo guasto, non
    ///     una riparazione.
    ///
    /// ⚠️ LA SCELTA DEL MOMENTO DI "SPEGNIMENTO" DEL CADAVERE-SEGUACE. Si chiude la finestra di
    /// `DeathCorpseFollow` **prima** di invocare `Respawn()`, e non per estetica: quell'inseguimento ha
    /// spostato il Penitent addosso al vivo (e' invisibile), quindi lo si riporta **al punto di morte**
    /// (`Teleport`) mentre la scena e' ancora quella giusta, invece di lasciare che sia il load a scoprirlo.
    /// Se `Respawn()` lanciasse, il cadavere e' comunque gia' ricomposto e il giocatore si ritrova morto
    /// **dov'e' caduto** — il difetto noto, non uno nuovo.
    ///
    /// ⚠️ LA PROBE, e perche' sta qui. Il mandato lo chiede: *"quali blocker sono accesi al momento in cui i
    /// tasti non rispondono?"*. La probe #0 (`[Cutscene BLK]`) logga solo `CINEMATIC` + `InputBlocked`, e
    /// nella sessione del guasto **non e' bastata**: `DEAD_SCREEN` e `FADE` erano accesi e nessuna riga lo
    /// diceva. Qui, **una riga al secondo mentre si e' morti**, si legge TUTTO quello che serve a chiudere
    /// la diagnosi alla prossima sessione:
    ///   - `unityAnyKey` — `UnityEngine.Input.anyKey` letto **in proprio** (quello che legge la schermata
    ///     di morte): distingue "il gioco non legge i tasti" da "i tasti non arrivano a Unity";
    ///   - `blockers=` — l'elenco VERO, ricostruito per differenza sulle transizioni di `SetBlocker`
    ///     (l'API pubblica non espone la lista: `HasBlocker` e' pubblico, `inputBlockers` no). Serve la
    ///     **transizione**, non lo stato, perche' `SetBlocker` logga solo al cambio;
    ///   - `gameplayUIActive` / `fadeUIActive` — ⭐ la gerarchia: se una di queste due e' `False`, i
    ///     **coroutine** della schermata di morte e del fade sono in pausa (Unity li ferma con il
    ///     `MonoBehaviour`), e quella e' la spiegazione meccanica di "i tasti non fanno niente";
    ///   - `deadScreenAttivo` — ⭐ `activeInHierarchy` del widget, che `DeathScreenProbe` **non** leggeva:
    ///     `FindWidget` usa `Resources.FindObjectsOfTypeAll`, che restituisce **anche gli inattivi**, quindi
    ///     "widget=1" nel log precedente **non** voleva dire "la schermata di morte e' viva";
    ///   - `respawnVisti` — quante volte il `Prefix` di `Respawn()` ha visto una richiesta in questa
    ///     finestra (letta da `PlayerDeathSync`), cosi' "il gioco ha provato e non e' riuscito" e "nessuno
    ///     ha mai provato" restano separabili.
    ///
    /// I DIVIETI, riga per riga.
    ///   - ⛔ `Core.Input.SetBlocker`: **zero occorrenze**. `CINEMATIC` si legge e basta (come in
    ///     `DeathCorpseFollow`).
    ///   - ⛔ `Core.Input.ResetManager()`: **zero occorrenze**.
    ///   - ⛔ `DEAD_SCREEN`: **zero occorrenze**, nemmeno in lettura come scrittura. E' del gioco.
    ///   - ⛔ `Time.timeScale` / `TimeScale`: zero. Si usa `Time.unscaledTime`, perche' un respawn deve
    ///     poter scattare anche a tempo fermo.
    ///   - ⛔ `SetActive`: zero.
    ///   - ⛔ nessuna patch Harmony nuova: si aggancia al ciclo che gira gia' (`Multiplayer.OnLateUpdate`).
    ///   - ✅ `try/catch` in ogni punto di ingresso: `Tick()` e' l'unico, e ha il suo.
    /// </summary>
    internal static class DeathRespawnWatchdog
    {
        /// <summary>Marker di ESITO. Chi legge deve poter dire chi ha parlato senza indovinare.</summary>
        private const string MARK = "[Respawn]";

        /// <summary>L'ultima riga di difesa, come in `CutsceneProbe`/`DeathCorpseFollow`.</summary>
        private const string MARK_FAIL = "[Respawn ERR]";

        /// <summary>
        /// Da quando la schermata di morte e' stabile si ascoltano i tasti. Vedi il commento di classe:
        /// e' la distanza dal tasto che ha ucciso.
        /// </summary>
        private const float KEY_ARM_SECONDS = 2.5f;

        /// <summary>
        /// IL FUSIBILE. Oltre questo tempo da morto con la schermata aperta e nessuna sequenza in corso, il
        /// respawn si invoca anche senza tasti: un giocatore che non respawna e' il guasto peggiore.
        /// </summary>
        private const float ESCAPE_SECONDS = 45f;

        /// <summary>
        /// La distanza fra due tentativi del fusibile. Non e' pignoleria: `Respawn()` fa un **load di
        /// livello**, che non e' istantaneo, e ritentarlo a ogni frame sarebbe una richiesta di load per
        /// frame. Dieci secondi stanno larghi su un load vero e restano un fusibile vero.
        /// </summary>
        private const float ESCAPE_RETRY_SECONDS = 10f;

        /// <summary>Cadenza della riga di stato. Un secondo, non un frame: e' una misura, non uno spam.</summary>
        private const float SAMPLE_INTERVAL = 1f;

        /// <summary>
        /// I tasti che interessano, in una tabella fissa: `Enum.GetValues` allocherebbe un array a ogni
        /// chiamata, e questo codice gira per frame.
        /// </summary>
        private static readonly KeyCode[] Keys = new KeyCode[]
        {
            KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape,
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.E, KeyCode.Q, KeyCode.R, KeyCode.F,
            KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.Tab, KeyCode.Backspace,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4
        };

        // ---- stato della finestra. Nessuno di questi campi sopravvive a una finestra chiusa.
        private static bool _dead;
        private static float _deadSince;
        private static float _nextSample;
        private static int _samples;
        private static bool _keyArmed;
        private static bool _fuseUsed;
        private static int _attempts;
        private static float _nextAttempt;

        // ---- lo stack dei blocker, ricostruito per differenza dalle transizioni di `SetBlocker`.
        private static readonly System.Collections.Generic.List<string> Blockers =
            new System.Collections.Generic.List<string>();

        // ---- una riga sola per finestra quando il Penitent non e' disponibile.
        private static bool _goneReported;

        /// <summary>
        /// Un giro per frame, **solo da morti**. Chiamata da `Multiplayer.OnLateUpdate`, subito dopo
        /// `DeathCorpseFollow.Tick()` (che e' il pezzo che ha spostato il Penitent e va chiuso per primo).
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_dead)
                        CloseWindow("il giocatore e' tornato (respawn)");
                    return;
                }

                float now = Time.unscaledTime;

                if (!_dead)
                {
                    _dead = true;
                    _deadSince = now;
                    _nextSample = now;
                    _samples = 0;
                    _keyArmed = false;
                    _fuseUsed = false;
                    _attempts = 0;
                    _nextAttempt = 0f;
                    _goneReported = false;

                    // ⭐ IL PRIMO GIRO NON SI FIDA DELLA DIFFERENZA: le sottoscrizioni possono essere
                    // arrivate DOPO un blocker gia' acceso (la mod si aggancia al primo ciclo per frame, e il
                    // gioco accende `InitialBlocker` nel suo `Start`). Quindi all'apertura della finestra si
                    // fa una **fotografia** con `HasBlocker` su tutti i nomi noti: cosi' `DEAD_SCREEN` —
                    // che e' acceso **prima** che la finestra si apra, perche' la schermata di morte lo
                    // accende nel suo `OnDeadAction` — compare nella riga di stato invece di restare
                    // invisibile. E' esattamente il blocker che nel log del guasto non si vedeva.
                    SyncBlockers();
                }

                // ---- LA PROBE: si legge lo stato VERO una volta al secondo. E' il pezzo che il mandato
                // chiede ("quali blocker sono accesi QUANDO i tasti non rispondono?") e che nella sessione
                // del guasto non c'era: `DEAD_SCREEN` e `FADE` erano accesi e nessuna riga lo diceva.
                if (now >= _nextSample)
                {
                    _nextSample = now + SAMPLE_INTERVAL;
                    _samples++;
                    ModLog.Info(State(now));
                }

                // ---- RETE 1 + RETE 2. Le sequenze in corso si LEGGONO, mai si scrivono.
                if (Core.Input == null || Core.Logic == null)
                    return;

                bool sequence = Core.Input.HasBlocker("CINEMATIC") || Core.Input.HasBlocker("DIALOG");

                if (now - _deadSince < KEY_ARM_SECONDS)
                    return;

                if (!sequence && !_keyArmed)
                {
                    _keyArmed = true;
                    ModLog.Info(MARK + " ascolto dei tasti ARMATO (nessuna sequenza in corso): " +
                                "se il gioco non legge piu' i tasti della schermata di morte, questo e' " +
                                "il punto in cui il respawn viene chiesto lo stesso");
                }

                if (!_keyArmed)
                    return;

                if (sequence)
                    return;

                KeyCode pressed = FirstKeyPressed();
                if (pressed != KeyCode.None)
                {
                    Respawn("tasto premuto dal morto (" + pressed + "): la schermata di morte non ha " +
                            "reagito, il respawn viene chiesto qui");
                    return;
                }

                if (now - _deadSince >= ESCAPE_SECONDS && (!_fuseUsed || now >= _nextAttempt))
                {
                    bool primo = !_fuseUsed;
                    _fuseUsed = true;
                    _nextAttempt = now + ESCAPE_RETRY_SECONDS;
                    Respawn((primo
                                ? "FUSIBILE scattato: " + F(now - _deadSince) + "s da morto senza nessuna " +
                                  "sequenza in corso e senza che il gioco abbia risposto ai tasti"
                                : "FUSIBILE, nuovo tentativo a " + F(now - _deadSince) + "s da morto") +
                            " · un giocatore bloccato non puo' piu' giocare, quindi si respawna comunque " +
                            "(e' esattamente cio' che il gioco avrebbe fatto al primo tasto)");
                }
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Chiude la finestra e **riporta lo stato della sessione a zero**. Esiste anche per la
        /// disconnessione (`Reset`), perche' la disconnessione puo' arrivare **mentre si e' ancora morti** e
        /// in quel momento `IsLocalPlayerDead()` resta vero (il gioco non sa nulla della rete).
        /// </summary>
        internal static void Reset()
        {
            try
            {
                if (_dead)
                    CloseWindow("disconnessione / fine sessione");
            }
            catch (Exception e)
            {
                Fail("Reset", e);
            }
        }

        private static void CloseWindow(string why)
        {
            ModLog.Info(MARK + " finestra chiusa: " + why + " · campioni=" + _samples +
                        " fusibile=" + (_fuseUsed ? "usato" : "non usato") +
                        " · " + BlockersLine());
            _dead = false;
            _deadSince = 0f;
            _samples = 0;
            _keyArmed = false;
            _fuseUsed = false;
            _attempts = 0;
            _nextAttempt = 0f;
            _goneReported = false;
        }

        // ============================================================================================
        // L'USCITA
        // ============================================================================================

        /// <summary>
        /// **LA VIA D'USCITA E' QUELLA DEL GIOCO**: `Penitent.Respawn()` — l'unico chiamante in tutto
        /// l'assembly e' `DeadScreenWidget` (`PlayerDeathSync.cs` lo documenta), quindi chiamarla qui e'
        /// fare la stessa cosa che il gioco farebbe al tasto, non un rimedio nuovo.
        ///
        /// E L'ORDINE CONTA: prima si ricompone il cadavere (il `Penitent` torna **al punto di morte** e il
        /// renderer torna visibile), poi si respawna. Se il respawn lanciasse, il giocatore si ritrova morto
        /// **dov'e' caduto**, cioe' il difetto noto, non un giocatore invisibile a meta' stanza.
        /// </summary>
        private static void Respawn(string why)
        {
            Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
            if (penitent == null)
            {
                // Il Penitent puo' essere assente per un istante durante un cambio scena: non e' un guasto,
                // ma va DETTO una volta (senza, "non ho fatto niente" e "ho provato e non c'era" si
                // assomigliano).
                if (!_goneReported)
                {
                    _goneReported = true;
                    ModLog.Warn(MARK + " respawn NON chiesto (" + why + "): Core.Logic.Penitent non e' " +
                                "disponibile in questo momento (cambio scena in corso?) · si riprova al " +
                                "prossimo frame");
                }
                return;
            }

            try
            {
                DeathCorpseFollow.Reset();
            }
            catch (Exception e)
            {
                // Mai fatale: la copia e' cosmetica, il respawn no. Si prosegue comunque.
                Fail("Respawn/DeathCorpseFollow.Reset", e);
            }

            ModLog.Info(MARK + " RESPAWN CHIESTO: " + why + " · via=" + BlockersLine() +
                        " · tentativi=" + (_attempts + 1) + " (le stesse cose che farebbe la schermata di " +
                        "morte: il load del livello rimette a posto blocker, fade e renderer DAL GIOCO)");

            try
            {
                _attempts++;
                // ⚠️ `SpawnManager.AutomaticRespawn`: se il gioco e' in una modalita' che respawna da solo
                // (`DeadScreenWidget.OnDead` lo legge per primo, :126), il `Prefix` di `PenitentRespawn` lo
                // rispetta. Qui si vuole **la stessa cosa che farebbe la schermata di morte**, quindi si
                // lascia la decisione al gioco.
                penitent.Respawn();
            }
            catch (Exception e)
            {
                Fail("Respawn/penitent.Respawn", e);
            }
        }

        // ============================================================================================
        // LE LETTURE
        // ============================================================================================

        /// <summary>
        /// Il primo tasto premuto **in questo frame**, `None` se nessuno. `GetKeyDown` e' un fronte: il
        /// tasto tenuto giu' dalla morte non viene riletto.
        /// </summary>
        private static KeyCode FirstKeyPressed()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                if (Input.GetKeyDown(Keys[i]))
                    return Keys[i];
            }

            return KeyCode.None;
        }

        /// <summary>
        /// LA RIGA DELLA PROBE. Ogni campo e' isolato: se una lettura fallisce il campo lo dice e **la riga
        /// esce lo stesso**, perche' un campo mancante non deve poter cancellare la misura degli altri.
        /// </summary>
        private static string State(float now)
        {
            bool unityAnyKey = false;
            try
            {
                unityAnyKey = Input.anyKey;
            }
            catch (Exception)
            {
            }

            string deadScreen = "widget-non-trovato";
            try
            {
                int found;
                DeadScreenWidget widget = DeathScreenProbe.FindWidget(out found);
                if (widget != null)
                {
                    deadScreen = "trovato=" + found +
                                 " attivo=" + (widget.gameObject != null && widget.gameObject.activeInHierarchy
                                     ? "True" : "False") +
                                 " enabled=" + (widget.enabled ? "True" : "False") +
                                 " alphaNorm=" + (widget.NormalGroup != null
                                     ? DeathScreenProbe.F(widget.NormalGroup.alpha) : "n/d");
                }
            }
            catch (Exception)
            {
                deadScreen = "errore";
            }

            string gameplayUI = "n/d";
            string fadeUI = "n/d";
            try
            {
                if (Core.UI != null)
                {
                    if (Core.UI.GameplayUI != null)
                        gameplayUI = Core.UI.GameplayUI.gameObject.activeInHierarchy ? "True" : "False";

                    if (Core.UI.Fade != null)
                        fadeUI = Core.UI.Fade.gameObject.activeInHierarchy ? "True" : "False";
                }
            }
            catch (Exception)
            {
                gameplayUI = "errore";
            }

            return MARK + " stato t=" + F(now) + " daMorte=" + F(now - _deadSince) +
                   " campione=" + _samples +
                   " unityAnyKey=" + (unityAnyKey ? "True" : "False") +
                   " tentativiMiei=" + _attempts +
                   " gameplayUIActive=" + gameplayUI + " fadeUIActive=" + fadeUI +
                   " schermataMorte=[" + deadScreen + "] · " + BlockersLine();
        }

        /// <summary>
        /// L'elenco dei blocker accesi, **tenuto per differenza**: `InputManager` logga `SetBlocker` a ogni
        /// transizione ma non espone la lista (`inputBlockers` e' privato; `HasBlocker` e' pubblico ma
        /// chiede un nome). Quindi si ascolta `OnInputLocked`/`OnInputUnlocked` — due eventi **pubblici** —
        /// e si ricostruisce lo stack dai log di `SetBlocker`. E' l'unico modo di rispondere alla domanda
        /// operativa del mandato senza toccare niente.
        /// </summary>
        private static string BlockersLine()
        {
            if (Blockers.Count == 0)
                return "blockers=(nessuno)";

            return "blockers=(" + string.Join(",", Blockers.ToArray()) + ")";
        }

        /// <summary>
        /// Le due sottoscrizioni pubbliche, fatte **una volta sola** e con la guardia: `Core.Input` puo'
        /// esistere prima di `Awake`. Gli eventi non si toccano mai in scrittura.
        /// </summary>
        internal static void AttachOnce()
        {
            try
            {
                if (_attached || Core.Input == null)
                    return;

                Core.Input.OnInputLocked += OnLocked;
                Core.Input.OnInputUnlocked += OnUnlocked;
                _attached = true;
            }
            catch (Exception e)
            {
                Fail("AttachOnce", e);
            }
        }

        private static bool _attached;

        /// <summary>
        /// Un blocker e' stato ACCESO. Il nome esatto non arriva dall'evento (e' `Core.SimpleEvent`, senza
        /// argomenti), quindi la lista si ricostruisce **leggendo `HasBlocker` sui nomi noti del gioco**:
        /// sono 20 e sono tutti in `_il` (l'elenco completo dei `SetBlocker` del gioco). Un nome che non
        /// fosse in questa tabella non verrebbe mostrato: il campo `blockers=` lo dichiara con un `+?`.
        /// </summary>
        private static void OnLocked()
        {
            try
            {
                SyncBlockers();
            }
            catch (Exception e)
            {
                Fail("OnLocked", e);
            }
        }

        /// <summary>
        /// Riallinea l'elenco con la verita' di `HasBlocker` (pubblico). Si usa sia sulle transizioni sia
        /// all'apertura della finestra: vedi il commento li'.
        /// </summary>
        private static void SyncBlockers()
        {
            for (int i = 0; i < KnownBlockers.Length; i++)
            {
                string name = KnownBlockers[i];
                bool on = Core.Input.HasBlocker(name);
                bool known = Blockers.Contains(name);

                if (on && !known)
                    Blockers.Add(name);
                else if (!on && known)
                    Blockers.Remove(name);
            }
        }

        private static void OnUnlocked()
        {
            try
            {
                SyncBlockers();
            }
            catch (Exception e)
            {
                Fail("OnUnlocked", e);
            }
        }

        /// <summary>
        /// ⭐ I NOMI DEI BLOCKER DEL GIOCO, presi da `_il` (tutti i `Core.Input.SetBlocker` dell'assembly):
        /// `InitialBlocker` (InputManager:142), `CINEMATIC`/`FREECAMERA` (Cinematics:134,219,253),
        /// `DIALOG` (DialogManager:209,471), `BLOCK_UNTIL_FPS_STABLE` (LevelManager:329,336),
        /// `TUTORIAL` (TutorialManager:109,156), `PLAYER_LOGIC` (12 behaviour di animazione),
        /// `HW_DIALOG_TIME`/`END_BOSS_DEFEATED`/`HW_FINAL_PLATFORM_RISING` (PontiffHuskBossBehaviour),
        /// `cutscene_block`/`BossBlock`/`popup_block`/`POP_UP`/`CUTSCENE`/`MULTIPLAYER`/`FADE`/`DEAD_SCREEN`/
        /// `INTERACTABLE`/`DOOR`/`EXECUTION` (dai nomi misurati nei log di sessione).
        /// </summary>
        private static readonly string[] KnownBlockers = new string[]
        {
            "InitialBlocker", "CINEMATIC", "FREECAMERA", "DIALOG", "BLOCK_UNTIL_FPS_STABLE", "TUTORIAL",
            "PLAYER_LOGIC", "HW_DIALOG_TIME", "END_BOSS_DEFEATED", "HW_FINAL_PLATFORM_RISING",
            "BossBlock", "cutscene_block", "popup_block", "POP_UP", "CUTSCENE", "MULTIPLAYER", "FADE",
            "DEAD_SCREEN", "INTERACTABLE", "DOOR", "EXECUTION", "IntroBlock"
        };

        /// <summary>Formato FISSO dei numeri (invariante): la cultura di questa macchina puo' essere italiana.</summary>
        private static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// L'ultima riga di difesa: se fallisse anche `ModLog.Error`, l'eccezione uscirebbe dal punto di
        /// ingresso e arriverebbe dentro `OnLateUpdate` del gioco (il guasto da 2934 errori).
        /// </summary>
        internal static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " in " + where + " (nessun effetto sul gioco): " + e.Message);
            }
            catch
            {
            }
        }
    }
}
