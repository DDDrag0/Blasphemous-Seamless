using System;
using System.Globalization;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐⭐ L'AVVISO A SCHERMO AL GIOCATORE MORTO DURANTE LA CUTSCENE DI MORTE DEL BOSS.
    ///
    /// LA RAGIONE, ED E' TUTTA DEL GIOCO. Quando il boss muore parte la sua cutscene, ma il giocatore che
    /// e' a terra ha davanti la schermata di morte DEL GIOCO, e quella schermata legge
    /// **`UnityEngine.Input`** (l'input del dispositivo, non `Core.Input`):
    ///
    ///     DeadScreenWidget.OnDeadAction   (`DeadScreenWidget.cs:87-96`)
    ///       while (!Input.anyKey) { yield return null; }        // :87-90  · il tasto, letto diretto
    ///       Core.UI.Fade.Fade(toBlack: true, 1.5f, 0f, callback) // :91
    ///         callback: Core.Logic.Penitent.Respawn()            // :95     · il livello si RICARICA
    ///
    /// ⇒ **premendo un tasto qualsiasi DURANTE la cutscene, il livello si ricarica e la cutscene e'
    /// distrutta** - e la diagnosi misurata (`scratch\DIAGNOSI-REGRESSIONE-RIENTRO.md` §2, §7) dice ancora
    /// di piu': il fade del respawn e il fade di chiusura della cutscene si contendono l'UNICO
    /// `currentTween` di `FadeWidget`, quindi il `Respawn()` puo' **non partire mai** e il giocatore resta
    /// a terra con il blocker `FADE` acceso. E' il difetto che l'utente ha chiamato "rientro rotto".
    ///
    /// E LA MOD NON PUO' IMPEDIRLO, e non e' un'opinione: non si puo' frapporre un blocker a
    /// `UnityEngine.Input` (il blocco dell'input e' un DIVIETO di questa consegna, e comunque non
    /// fermerebbe una lettura diretta del dispositivo), non esiste nessuna API per "consumare" il tasto
    /// (`Input.anyKey` non ha una coda da svuotare) e patchare quella schermata e' ZONA VIETATA.
    /// ⇒ **l'unica difesa che resta e' AVVISARE**: non si impedisce il respawn, si toglie la causa — il
    /// tasto non voluto. Questo file e' quell'avviso.
    ///
    /// ⭐⭐ LA FINESTRA: **NON LA MORTE DEL BOSS, MA LA CUTSCENE** (la correzione del 18/10). Fino alla
    /// consegna precedente l'avviso usciva dal `Kill()` del boss e durava 4 s
    /// (`config.notificationDisplaySeconds`). **Misurato**: la cutscene comincia **~9-21 s DOPO** il
    /// `Kill()` (in `G2` della diagnosi: avviso a `t≈400,1`, scaduto a `≈404,1`; `CinematicStarted` a
    /// `≈409,5`; il tasto fatale a `≈425`) ⇒ **l'avviso era finito a schermo ~5 s PRIMA che il pericolo
    /// cominciasse**, e non c'era nessuno a schermo quando l'utente ha premuto. Quindi la finestra adesso
    /// e' quella vera del pericolo, presa dagli **eventi PUBBLICI** di `Cinematics` (`:85-91`, gli stessi
    /// che la probe legge gia' senza patcharmi niente):
    ///
    ///     CinematicStarted  →  si mostra l'avviso, e si RINFRESCA finche' la cutscene dura
    ///     CinematicEnded    →  la finestra si chiude (e se `cancelled=True`, l'annullo e' successo)
    ///
    /// ⚠️ **E IL CASO "BOSS SENZA CUTSCENE": in quel caso l'avviso NON DEVE USCIRE.** La morte del boss
    /// **arma** la finestra (`WarnBossDied`, chiamata dai due punti vivi di `EnemySyncManager`), ma non
    /// mostra piu' niente: se nessun `CinematicStarted` arriva entro `CINEMATIC_ARM_TIMEOUT` secondi,
    /// l'armamento scade con una riga che lo dichiara. Senza cutscene non c'e' **niente da distruggere**,
    /// quindi non c'e' niente da dire.
    ///
    /// PERCHE' SI RINFRESCA, e non si mostra una volta sola. La cutscene misurata dura **~15 s** (da
    /// `t≈409,5` a `t≈425`), mentre una notifica della mod ne vive **4**: mostrarla una volta sola
    /// ripeterebbe lo stesso difetto in piccolo (avviso sparito, pericolo ancora in corso). Si rinfresca
    /// quindi **una volta ogni `notificationDisplaySeconds * 0.8`** (≈3,2 s: la nuova riga e' in coda prima
    /// che la precedente scada, cosi' non c'e' un buco), e la finestra ha un **tetto** (`MAX_SHOWS`) e un
    /// **timeout di sicurezza** (`CINEMATIC_WINDOW_MAX`): se `CinematicEnded` non arrivasse mai - e'
    /// documentato che `EndCutscene` diventa un no-op se `PlayVideo` e' morto dentro il suo `catch`, vedi
    /// `CutsceneProbe.cs:39-46` - la finestra **non resta aperta per sempre**.
    ///
    /// ⚠️ L'OROLOGIO DI QUESTA FINESTRA E' `Time.unscaledTime`, NON `Time.time`, e non e' un vezzo: una
    /// cutscene puo' girare con `Time.timeScale` a zero, e in quel caso `Time.time` si ferma - la finestra
    /// non si aprirebbe mai davvero, il rinfresco non scatterebbe e il timeout non arriverebbe. La scelta e'
    /// la stessa che fa `NotificationManager`, che misura la vita delle righe in
    /// `Time.unscaledDeltaTime` (`NotificationManager.cs:71`): l'avviso e la sua scadenza devono usare lo
    /// stesso orologio, o si contraddicono.
    ///
    /// COME, E SENZA TOCCARE NIENTE DEL GIOCO. Si usa `NotificationManager` **DELLA MOD**
    /// (`Main.Multiplayer.NotificationManager`, lo stesso di "connesso"/"disconnesso"/errori): non e' la UI
    /// del gioco, non la si patcha, non si accende ne' si spegne nessun `SetActive`, non si mette nessun
    /// blocker, non si tocca `Time.timeScale`, la camera, nessun `Penitent` e nessun flag. Si aggiunge una
    /// riga al suo elenco, e il suo `Update` la disegna e la fa sfumare da sola: **e' un avviso, NON un
    /// prompt modale** (appare e sfuma, non aspetta niente da nessuno).
    /// ⇒ **zero patch Harmony: i bersagli restano 269.**
    ///
    /// DOVE E' AGGANCIATA, E PERCHE' LI'. Nel commit originale (`59f34d7`, "LA PORTA DEI TASTI E' CHIUSA")
    /// l'avviso stava nel `DeathRespawnWatchdog` e partiva dal suo segnale `kill()`. Quel file e' stato
    /// **cancellato dal rollback `13107c9`**, e con lui `EnemySyncManager.BossKillSignal`: il punto di
    /// aggancio originale **non esiste piu'**. I punti VIVI che restano sono gli stessi due che fanno
    /// morire il boss su questa macchina: `EnemySyncManager.ApplyBossDeath` (M1) subito dopo `boss.Kill()`
    /// (li' il giocatore locale e' morto **per costruzione**: la guardia che precede il `Kill()` esce se e'
    /// vivo) e l'annuncio `bdead|` dentro `EnemySyncManager.Update`, quando e' QUESTA macchina a vedere il
    /// boss morto. Entrambi chiamano `WarnBossDied`, che adesso **arma** soltanto.
    /// ⚠️ **IL NOME `WarnBossDied` NON E' PIU' ESATTO** ed e' una scelta dichiarata: rinominarla
    /// significherebbe toccare le due chiamate dentro `EnemySyncManager`, cioe' M1, che questa consegna
    /// vuole **intatta**. Il metodo arma; a mostrare e' `CinematicStarted`.
    ///
    /// IL TESTO E' IN INGLESE (richiesta dell'utente) e **senza caratteri accentati**: passa da
    /// `NotificationManager.DisplayNotification(string)`, che vuole una STRINGA e non una chiave di
    /// localizzazione (`resources\localization\Multiplayer.txt` serve solo alle chiavi di
    /// `LocalizationHandler`), quindi resta in inglese qualunque sia la lingua del gioco.
    /// </summary>
    internal static class BossDeathWarning
    {
        /// <summary>
        /// Marker di ESITO, obbligatorio su ogni riga: senza di lui "non ha avvisato perche' il giocatore
        /// era vivo" e "non ha avvisato perche' era gia' armato" hanno lo stesso aspetto in un log.
        /// </summary>
        private const string MARK = "[BossWarn]";

        /// <summary>
        /// L'INTERRUTTORE. `false` spegne l'avviso a schermo e nient'altro: nessun altro pezzo di questa
        /// classe resta acceso (il `Tick` continua a leggere la finestra di morte, ed e' solo un log).
        ///
        /// ⚠️ E' `static readonly` E NON `const`, di proposito: con `const bool X = true` la riga
        /// `if (!X) return;` fa emettere al compilatore **CS0162 "codice non raggiungibile"**, e un warning
        /// che grida sempre nasconde quelli veri (la build di questo repo ne ha centinaia: uno in piu' non
        /// aggiunge informazione, uno in piu' FALSO la toglie). Con `readonly` la riga resta raggiungibile
        /// per il compilatore e la scelta resta una riga sola.
        /// </summary>
        private static readonly bool WARN_PLAYER_ON_BOSS_DEATH = true;

        /// <summary>
        /// IL TESTO, in inglese e senza accenti, come richiesto: caratteri tutti ASCII.
        /// </summary>
        private const string TEXT = "DO NOT PRESS: the boss is dying - the cutscene is about to start";

        // ============================================================================================
        // LA FINESTRA DELLA CUTSCENE (⭐ la correzione di questa consegna)
        // ============================================================================================

        /// <summary>
        /// Quanto si aspetta una cutscene dopo la morte del boss prima di considerare che **non ce n'e'**
        /// (il caso "boss senza cutscene": l'avviso non serve). Misurato: 9-21 s; 60 s e' un margine largo
        /// che non lascia un armamento appeso per il resto della sessione.
        /// </summary>
        private const float CINEMATIC_ARM_TIMEOUT = 60f;

        /// <summary>
        /// Il tetto di sicurezza della finestra: se `CinematicEnded` non arrivasse (e' il caso documentato
        /// in cui `EndCutscene` e' un no-op), la finestra si chiude d'ufficio dopo questo tempo.
        /// </summary>
        private const float CINEMATIC_WINDOW_MAX = 45f;

        /// <summary>
        /// Il tetto di avvisi in UNA finestra. Con `REFRESH_FRACTION = 0.8` e 4 s di durata sono ~12
        /// notifiche in ~38 s: piu' di cosi' non e' un rinfresco, e' spam.
        /// </summary>
        private const int MAX_SHOWS_PER_WINDOW = 12;

        /// <summary>
        /// Quando rinfrescare, in frazione della durata della notifica: 0.8 ⇒ la riga nuova entra in coda
        /// prima che la vecchia scada, e sullo schermo ce n'e' sempre una accesa senza buchi.
        /// </summary>
        private const float REFRESH_FRACTION = 0.8f;

        /// <summary>La durata di ripiego se la config non e' leggibile (`Config.cs:18` dice `4f`).</summary>
        private const float DEFAULT_NOTIFICATION_SECONDS = 4f;

        // ---- la finestra di morte ("il giocatore locale e' a terra"), invariata: e' il fronte su cui si
        //      contano le finestre e si riarma tutto quando il giocatore torna vivo.
        private static bool _dead;
        private static int _windows;
        private static int _warnings;

        // ---- "spento dall'interruttore", detto UNA volta; e il dedup dell'armamento doppio.
        private static bool _toggleOffReported;
        private static bool _dedupReported;

        // ---- l'ARMAMENTO: una morte del boss mentre siamo morti, in attesa della sua cutscene.
        private static bool _armed;
        private static float _armedAt;
        private static string _armedWhere = string.Empty;

        // ---- la FINESTRA aperta: da `CinematicStarted` a `CinematicEnded`.
        private static bool _cinematicOpen;
        private static float _cinematicStartedAt;

        // ---- cosa e' gia' uscito in QUESTA finestra di cutscene.
        private static bool _shownThisWindow;
        private static float _lastShownAt;
        private static int _showsThisWindow;
        private static bool _capReported;
        private static bool _aliveReported;
        private static bool _noManagerReported;

        // ---- l'iscrizione agli eventi pubblici di `Cinematics` (zero patch): `Tick` la tenta a ogni
        //      frame finche' non riesce, perche' `Core.Cinematics` puo' non essere pronto al primo giro
        //      (stessa strada di `CutsceneProbe`, che ritenta da `Tick` per la stessa ragione).
        private static bool _subscribed;
        private static bool _subscribeFailureReported;

        /// <summary>
        /// `true` mentre la finestra della cutscene e' aperta. Letto da UN SOLO posto,
        /// `BossFightDeathWarning` (l'avviso della morte del giocatore): se la finestra della cutscene e'
        /// aperta, il suo avviso tace, cosi' i due non si sovrappongono mai.
        /// </summary>
        internal static bool CutsceneWindowOpen
        {
            get { return _cinematicOpen; }
        }

        /// <summary>
        /// Un giro per frame, da `Multiplayer.OnLateUpdate`, accanto agli altri `Tick()` delle sonde.
        /// Fa tre cose, tutte dentro il `try/catch`: **tiene la finestra di morte** (la apre, la chiude, e
        /// riarma tutto quando il giocatore torna vivo), **tiene l'armamento** (lo fa scadere se la
        /// cutscene non arriva) e **tiene la finestra della cutscene** (mostra, rinfresca, chiude).
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
                    ModLog.Info(MARK + " finestra di morte n." + _windows + " APERTA " +
                                "(PlayerDeathSync.IsLocalPlayerDead()=True): il boss che muore adesso ha " +
                                "diritto al suo avviso, e l'avviso uscira' alla SUA cutscene");
                }
                else if (!dead && _dead)
                {
                    ModLog.Info(MARK + " finestra di morte n." + _windows + " CHIUSA (il giocatore locale e' " +
                                "di nuovo vivo): l'avviso torna disponibile per la prossima morte" +
                                (_shownThisWindow
                                    ? " (in questa finestra l'avviso e' uscito " + _showsThisWindow + " volta/e)"
                                    : " (in questa finestra l'avviso NON e' uscito)"));
                }

                _dead = dead;

                if (!dead)
                {
                    // Il giocatore e' tornato vivo: se l'armamento era ancora in attesa, la sua cutscene
                    // non protegge piu' niente (e il respawn l'avrebbe comunque distrutta).
                    if (_armed && !_shownThisWindow)
                    {
                        ModLog.Info(MARK + " finestra della cutscene DISARMATA: il giocatore e' tornato " +
                                    "vivo prima che la cutscene partisse ⇒ nessuna notifica · armata-da=" +
                                    _armedWhere);
                    }

                    _armed = false;
                    _shownThisWindow = false;
                    _dedupReported = false;
                }

                // (a) ARMATA MA NESSUNA CUTSCENE: e' il caso "boss senza cutscene", e li' l'avviso NON serve
                //     (senza cutscene un tasto non distrugge niente).
                if (_armed && !_cinematicOpen && Time.unscaledTime - _armedAt > CINEMATIC_ARM_TIMEOUT)
                {
                    _armed = false;
                    ModLog.Info(MARK + " finestra della cutscene CHIUSA SENZA CUTSCENE (boss senza cutscene) " +
                                "· t=" + F(Time.unscaledTime) + " · armata-da=" + _armedWhere + " a t=" + F(_armedAt) +
                                " · nessun CinematicStarted in " + CINEMATIC_ARM_TIMEOUT.ToString("F0") +
                                "s ⇒ nessun avviso a schermo: senza cutscene il tasto non ha niente da " +
                                "distruggere, quindi l'avviso non serve");
                }

                // (b) LA FINESTRA E' APERTA: si mostra, e si rinfresca finche' la cutscene dura.
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
                            ModLog.Info(MARK + " tetto di " + MAX_SHOWS_PER_WINDOW + " avvisi in questa " +
                                        "finestra raggiunto: non si rinfresca piu' (la finestra si chiude " +
                                        "comunque a CinematicEnded)");
                        }
                    }
                    else if (Time.unscaledTime - _lastShownAt >= NotificationSeconds() * REFRESH_FRACTION)
                    {
                        Show("cutscene:rinfresco (la cutscene e' ancora in corso)");
                    }

                    // (c) `CinematicEnded` mai arrivato: la finestra non resta aperta per sempre.
                    if (Time.unscaledTime - _cinematicStartedAt > CINEMATIC_WINDOW_MAX)
                    {
                        ModLog.Info(MARK + " finestra della cutscene CHIUSA D'UFFICIO: CinematicEnded non e' " +
                                    "arrivato entro " + CINEMATIC_WINDOW_MAX.ToString("F0") + "s (t=" +
                                    F(Time.unscaledTime) + ", aperta a t=" + F(_cinematicStartedAt) + "): " +
                                    "l'avviso smette di rinfrescarsi");
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
        /// ⭐ ARMA LA FINESTRA — da chiamare nei punti in cui QUESTA macchina fa morire il boss (vedi il
        /// commento di classe). **Non mostra piu' niente**: una morte del boss non e' il momento
        /// pericoloso, la sua **cutscene** lo e'. Non ritorna niente e non lancia mai: **una notifica non
        /// deve rompere la morte del boss**, quindi tutto e' dentro il `try/catch` e ogni uscita anticipata
        /// scrive il perche'.
        /// </summary>
        internal static void WarnBossDied(string where)
        {
            try
            {
                // L'INTERRUTTORE, e si dice: spento non deve avere lo stesso aspetto di "gia' armato".
                if (!WARN_PLAYER_ON_BOSS_DEATH)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " avviso SPENTO dall'interruttore " +
                                    "(WARN_PLAYER_ON_BOSS_DEATH=false): nessuna notifica a schermo · dove=" +
                                    where);
                    }
                    return;
                }
                // ⭐ IL PREDICATO, ed e' lo stesso delle altre due funzioni della morte: se il giocatore
                // locale e' VIVO l'avviso e' rumore, e la schermata di morte che lo rende necessario non
                // c'e' nemmeno.
                if (!PlayerDeathSync.IsLocalPlayerDead())
                {
                    ModLog.Info(MARK + " avviso NON mostrato: il giocatore locale e' VIVO " +
                                "(PlayerDeathSync.IsLocalPlayerDead()=False, quindi nessuna schermata di " +
                                "morte da cui respawnare) · dove=" + where);
                    return;
                }

                if (_armed || _shownThisWindow)
                {
                    if (!_dedupReported)
                    {
                        _dedupReported = true;
                        ModLog.Info(MARK + " avviso GIA' armato per questa finestra di morte (uno per " +
                                    "finestra): nessun secondo armamento · dove=" + where +
                                    " · gia-armato-da=" + _armedWhere);
                    }
                    return;
                }

                _armed = true;
                _armedAt = Time.unscaledTime;
                _armedWhere = where;
                _dedupReported = false;

                ModLog.Info(MARK + " finestra della cutscene ARMATA (finestra di morte n." + _windows + ") " +
                            "· t=" + F(Time.unscaledTime) + " · armata-da=" + where +
                            " · predicato=PlayerDeathSync.IsLocalPlayerDead()=True · " +
                            "⚠️ NESSUNA NOTIFICA ADESSO: l'avviso esce a CinematicStarted, cioe' quando il " +
                            "pericolo comincia davvero (misurato: la cutscene parte 9-21 s dopo il Kill). " +
                            "Se la cutscene non arriva entro " + CINEMATIC_ARM_TIMEOUT.ToString("F0") +
                            "s, la finestra si chiude senza avviso (boss senza cutscene)");

                // La cutscene potrebbe essere GIA' in corso (l'ordine fra il pacchetto di morte e la FSM
                // della stanza non e' garantito): in quel caso si mostra subito, senza aspettare un
                // `CinematicStarted` che e' gia' passato.
                if (_cinematicOpen && !_shownThisWindow)
                {
                    _shownThisWindow = true;
                    Show("bossmorto:cutscene-gia-in-corso");
                }
            }
            catch (Exception e)
            {
                Fail("WarnBossDied", e);
            }
        }

        /// <summary>
        /// ⭐ L'UNICO POSTO CHE MOSTRA LA NOTIFICA. Si mostra solo se il giocatore locale e' ancora a terra
        /// (se e' risorto, la schermata di morte non c'e' e non c'e' niente da proteggere), e ogni uscita
        /// anticipata scrive il perche'.
        /// </summary>
        private static void Show(string trigger)
        {
            try
            {
                // ⚠️ IL TIMBRO SI METTE PRIMA DI OGNI CONTROLLO, e non e' un dettaglio: il rinfresco si
                // misura su `_lastShownAt`, quindi se lo si aggiornasse solo quando la notifica esce, un
                // tentativo SCARTATO (giocatore risorto, config assente) verrebbe ritentato a OGNI frame -
                // cioe' un log per frame invece di uno ogni ~3 s. Il timbro e' "ho provato", non "e' uscito".
                _lastShownAt = Time.unscaledTime;

                if (!WARN_PLAYER_ON_BOSS_DEATH)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " avviso SPENTO dall'interruttore " +
                                    "(WARN_PLAYER_ON_BOSS_DEATH=false): nessuna notifica a schermo · dove=" +
                                    trigger);
                    }
                    return;
                }

                if (!PlayerDeathSync.IsLocalPlayerDead())
                {
                    if (!_aliveReported)
                    {
                        _aliveReported = true;
                        ModLog.Info(MARK + " avviso NON mostrato: il giocatore locale e' VIVO " +
                                    "(PlayerDeathSync.IsLocalPlayerDead()=False): non c'e' nessuna " +
                                    "schermata di morte da cui respawnare · dove=" + trigger);
                    }
                    return;
                }

                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                {
                    if (!_noManagerReported)
                    {
                        _noManagerReported = true;
                        ModLog.Error(MARK + " avviso NON mostrato: NotificationManager non disponibile · " +
                                     "dove=" + trigger);
                    }
                    return;
                }

                _showsThisWindow++;
                _warnings++;

                Main.Multiplayer.NotificationManager.DisplayNotification(TEXT);

                ModLog.Info(MARK + " AVVISO A SCHERMO n." + _warnings + " (finestra di morte n." + _windows +
                            ", avviso " + _showsThisWindow + "/" + MAX_SHOWS_PER_WINDOW + " di questa " +
                            "cutscene) · \"" + TEXT + "\" · dove=" + trigger + " · armato-da=" + _armedWhere +
                            " · predicato=PlayerDeathSync.IsLocalPlayerDead()=True " +
                            "· durata=" + DurationSeconds() + "s (config.notificationDisplaySeconds) " +
                            "· NotificationManager DELLA MOD: zero patch Harmony, nessuna UI del gioco " +
                            "toccata, nessun blocker, nessun SetActive, nessun TimeScale, nessuna camera · " +
                            "e' l'unica difesa contro la schermata di morte DEL GIOCO " +
                            "(DeadScreenWidget.OnDeadAction:87-96: `while (!Input.anyKey)` -> FADE 1.5 s -> " +
                            "`Penitent.Respawn()`), che la mod NON puo' chiudere: non impedisce il respawn, " +
                            "toglie la causa (il tasto non voluto)");
            }
            catch (Exception e)
            {
                Fail("Show", e);
            }
        }

        /// <summary>
        /// L'iscrizione agli **eventi pubblici** di `Cinematics` (`:85-91`), zero patch: e' la stessa strada
        /// di `CutsceneProbe`, che e' gia' iscritta (due iscrizioni allo stesso evento convivono, e questa
        /// non tocca quella). Si ritenta da `Tick` se `Core.Cinematics` non e' ancora pronto.
        /// </summary>
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
                        ModLog.Info(MARK + " Core.Cinematics non ancora pronto: riprovo a ogni frame " +
                                    "(la finestra della cutscene non e' ancora agganciata)");
                    }
                    return;
                }

                if (_subscribed)
                    return;

                cinematics.CinematicStarted += OnCinematicStarted;
                cinematics.CinematicEnded += OnCinematicEnded;

                _subscribed = true;

                ModLog.Info(MARK + " finestra della cutscene AGGANCIATA: sottoscritti " +
                            "CinematicStarted/CinematicEnded (eventi PUBBLICI di Cinematics, zero patch " +
                            "Harmony). L'avviso non esce piu' alla morte del boss: esce qui");
            }
            catch (Exception e)
            {
                Fail("Subscribe", e);
            }
        }

        /// <summary>
        /// `CinematicStarted`: **si apre la finestra**. Se la morte del boss non ha armato niente (una
        /// cutscene qualunque, o il giocatore vivo), non succede niente e la riga lo dice.
        /// </summary>
        private static void OnCinematicStarted()
        {
            try
            {
                _cinematicOpen = true;
                _cinematicStartedAt = Time.unscaledTime;

                // Ogni cutscene e' una finestra nuova: i contatori dell'avviso ripartono.
                _shownThisWindow = false;
                _showsThisWindow = 0;
                _capReported = false;
                _aliveReported = false;
                _noManagerReported = false;

                ModLog.Info(MARK + " cutscene INIZIATA (CinematicStarted) t=" + F(Time.unscaledTime) +
                            ": la finestra dell'avviso si apre ADESSO · armata=" + (_armed ? "SI" : "NO") +
                            (_armed ? " (armata-da=" + _armedWhere + " a t=" + F(_armedAt) + ")" : string.Empty) +
                            (_armed
                                ? " ⇒ l'avviso esce nei prossimi frame"
                                : " ⇒ nessun avviso (nessuna morte del boss da proteggere)"));
            }
            catch (Exception e)
            {
                Fail("OnCinematicStarted", e);
            }
        }

        /// <summary>
        /// `CinematicEnded`: **si chiude la finestra**. `cancelled=True` significa che l'utente ha
        /// annullato col tasto 51: e' esattamente il caso che l'avviso doveva evitare, e la riga lo dice
        /// con il numero di avvisi che erano a schermo.
        /// </summary>
        private static void OnCinematicEnded(bool cancelled)
        {
            try
            {
                ModLog.Info(MARK + " cutscene FINITA (CinematicEnded cancelled=" + cancelled + ") t=" +
                            F(Time.unscaledTime) + ": la finestra dell'avviso si chiude · avvisi usciti in questa " +
                            "finestra=" + _showsThisWindow + (_showsThisWindow > 0
                                ? string.Empty
                                : " (nessuno: o il giocatore era vivo, o la morte del boss non era armata)") +
                            (cancelled
                                ? " · ⚠️ ANNULLATA: un tasto durante la cutscene l'ha interrotta (e' il caso " +
                                  "in cui il fade del respawn si perde: vedi DIAGNOSI-REGRESSIONE-RIENTRO.md §2)"
                                : string.Empty));

                CloseWindow("CinematicEnded");
            }
            catch (Exception e)
            {
                Fail("OnCinematicEnded", e);
            }
        }

        /// <summary>Chiude la finestra della cutscene e disarma: la morte successiva riparte da zero.</summary>
        private static void CloseWindow(string reason)
        {
            _cinematicOpen = false;
            _armed = false;
            _shownThisWindow = false;
            _showsThisWindow = 0;
            _capReported = false;
            _aliveReported = false;
            _noManagerReported = false;

            ModLog.Info(MARK + " finestra della cutscene CHIUSA (" + reason + "): la prossima morte del " +
                        "boss arma una finestra nuova");
        }

        /// <summary>
        /// La durata di una notifica, letta dalla config (`config.notificationDisplaySeconds`): e' il
        /// numero su cui si decide il rinfresco, quindi va letto **dalla stessa fonte** che usa
        /// `NotificationManager` (`NotificationManager.cs:28`), non duplicato.
        /// </summary>
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
            catch (Exception)
            {
                // Solo diagnostica: senza config si usa il ripiego, e la notifica non dipende da questo.
            }

            return DEFAULT_NOTIFICATION_SECONDS;
        }

        /// <summary>
        /// La durata dell'avviso in una stringa: e' il numero che rende verificabile "4 s, e si rinfresca
        /// finche' la cutscene dura".
        /// </summary>
        private static string DurationSeconds()
        {
            return NotificationSeconds().ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Formato dei numeri FISSO (invariante): la cultura di questa macchina e' italiana.</summary>
        private static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// L'errore di questa classe non esce mai: e' l'ultimo `catch` prima del ciclo per frame della mod
        /// (o della morte del boss), e un throw li' dentro e' il guasto che una volta ha prodotto 2934
        /// errori in un log solo.
        /// </summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (l'avviso non ha interrotto niente: la morte del boss e' proseguita)");
            }
            catch (Exception)
            {
                // Niente da fare: se anche il log fallisce, non c'e' un secondo posto dove dirlo.
            }
        }
    }
}
