using System;
using System.Globalization;
using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐⭐ L'AVVISO A SCHERMO AL GIOCATORE MORTO NEL MOMENTO IN CUI IL BOSS MUORE.
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
    /// ⇒ **premendo un tasto qualsiasi mentre il boss sta morendo, il livello si ricarica e la cutscene e'
    /// distrutta.**
    ///
    /// E LA MOD NON PUO' IMPEDIRLO, e non e' un'opinione: non si puo' frapporre un blocker a
    /// `UnityEngine.Input` (il blocco dell'input e' un DIVIETO di questa consegna, e comunque non
    /// fermerebbe una lettura diretta del dispositivo), non esiste nessuna API per "consumare" il tasto
    /// (`Input.anyKey` non ha una coda da svuotare) e patchare quella schermata e' ZONA VIETATA.
    /// ⇒ **l'unica difesa che resta e' AVVISARE**: non si impedisce il respawn, si toglie la causa — il
    /// tasto non voluto. Questo file e' quell'avviso.
    ///
    /// COME, E SENZA TOCCARE NIENTE DEL GIOCO. Si usa `NotificationManager` **DELLA MOD**
    /// (`Main.Multiplayer.NotificationManager`, lo stesso di "connesso"/"disconnesso"/errori): non e' la UI
    /// del gioco, non la si patcha, non si accende ne' si spegne nessun `SetActive`, non si mette nessun
    /// blocker, non si tocca `Time.timeScale`, la camera, nessun `Penitent` e nessun flag. Si aggiunge una
    /// riga al suo elenco, e il suo `Update` la disegna e la fa sfumare da sola: **e' un avviso, NON un
    /// prompt modale** (appare e sfuma, non aspetta niente da nessuno).
    /// ⇒ **zero patch Harmony: i bersagli restano 263**.
    ///
    /// QUANDO. **Una volta per finestra di morte** (`_warnedThisWindow`: la finestra e' "il giocatore
    /// locale e' a terra", da `PlayerDeathSync.IsLocalPlayerDead()`), **solo se il giocatore locale e'
    /// morto** — se e' vivo l'avviso e' rumore, e la sua schermata di morte non c'e' — e per
    /// `config.notificationDisplaySeconds`, **4 s di default**: si spegne intorno al momento in cui la
    /// scena parte e **non copre la cutscene**. Si spegne tutto, senza toccare altro, mettendo
    /// <see cref="WARN_PLAYER_ON_BOSS_DEATH"/> a `false`.
    ///
    /// DOVE E' RIAGGANCIATO, E PERCHE' LI'. Nel commit originale (`59f34d7`, "LA PORTA DEI TASTI E'
    /// CHIUSA") l'avviso stava nel `DeathRespawnWatchdog` e partiva dal suo segnale `kill()`. Quel file e'
    /// stato **cancellato dal rollback `13107c9`**, e con lui `EnemySyncManager.BossKillSignal`: il punto di
    /// aggancio originale **non esiste piu'**. Il punto VIVO che resta e' **dove la mod applica davvero la
    /// morte del boss su questa macchina**, cioe' `EnemySyncManager.ApplyBossDeath` (M1) subito dopo
    /// `boss.Kill()`: li' il giocatore locale e' morto **per costruzione** (la guardia che precede il
    /// `Kill()` esce se e' vivo) ed e' l'istante esatto in cui comincia la sequenza di morte del boss.
    /// Il secondo punto e' l'annuncio `bdead|` dentro `EnemySyncManager.Update`, cioe' quando e' QUESTA
    /// macchina a vedere il boss morto: stessa condizione, e il dedup per finestra impedisce che i due
    /// punti producano due notifiche.
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
        /// era vivo" e "non ha avvisato perche' era gia' stato avvisato" hanno lo stesso aspetto in un log.
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

        // ---- `_warnedThisWindow` e' "l'avviso e' gia' uscito in QUESTA finestra di morte" (uno per
        //      finestra); `_dedupReported` fa scrivere UNA volta il perche' del secondo tentativo (senza,
        //      il dedup sarebbe invisibile nel log); i due contatori danno i numeri al referto.
        private static bool _warnedThisWindow;
        private static bool _dedupReported;
        private static bool _toggleOffReported;
        private static int _windows;
        private static int _warnings;

        // ---- il fronte della finestra di morte, per la riga di apertura e quella di chiusura.
        private static bool _dead;

        /// <summary>
        /// Un giro per frame, da `Multiplayer.OnLateUpdate`, accanto agli altri `Tick()` delle sonde.
        /// Non fa altro che **tenere la finestra di morte**: la apre, la chiude, e soprattutto azzera
        /// `_warnedThisWindow` quando il giocatore locale e' di nuovo vivo, cosi' la morte successiva ha
        /// diritto al suo avviso. Il `try/catch` non e' decorativo: un'eccezione qui uscirebbe da un punto
        /// di ingresso del ciclo per frame della mod.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (dead && !_dead)
                {
                    _windows++;
                    ModLog.Info(MARK + " finestra di morte n." + _windows + " APERTA " +
                                "(PlayerDeathSync.IsLocalPlayerDead()=True): il boss che muore adesso avra' " +
                                "il suo avviso a schermo");
                }
                else if (!dead && _dead)
                {
                    ModLog.Info(MARK + " finestra di morte n." + _windows + " CHIUSA (il giocatore locale e' " +
                                "di nuovo vivo): l'avviso torna disponibile per la prossima morte" +
                                (_warnedThisWindow
                                    ? " (in questa finestra l'avviso e' uscito " + _warnings + " volta/e)"
                                    : " (in questa finestra l'avviso NON e' uscito)"));
                }

                _dead = dead;

                // Una finestra nuova = un avviso nuovo. Fuori dalla morte l'avviso e' sempre disponibile.
                if (!dead)
                {
                    _warnedThisWindow = false;
                    _dedupReported = false;
                }
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// ⭐⭐ L'AVVISO — da chiamare nei punti in cui QUESTA macchina fa morire il boss (vedi il commento
        /// di classe). Non ritorna niente e non lancia mai: **una notifica non deve rompere la morte del
        /// boss**, quindi tutto e' dentro il `try/catch` e ogni uscita anticipata scrive il perche'.
        /// </summary>
        internal static void WarnBossDied(string where)
        {
            try
            {
                // L'INTERRUTTORE, e si dice: spento non deve avere lo stesso aspetto di "gia' uscito".
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

                if (_warnedThisWindow)
                {
                    if (!_dedupReported)
                    {
                        _dedupReported = true;
                        ModLog.Info(MARK + " avviso GIA' uscito in questa finestra di morte (uno per " +
                                    "finestra): nessuna seconda notifica · dove=" + where);
                    }
                    return;
                }

                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                {
                    ModLog.Error(MARK + " avviso NON mostrato: NotificationManager non disponibile · dove=" +
                                 where);
                    return;
                }

                // Prima di mostrarlo: se la notifica esplodesse, ritentare a ogni boss sarebbe spam.
                _warnedThisWindow = true;
                _warnings++;

                Main.Multiplayer.NotificationManager.DisplayNotification(TEXT);

                ModLog.Info(MARK + " AVVISO A SCHERMO n." + _warnings + " (finestra di morte n." + _windows +
                            ") · \"" + TEXT + "\" · dove=" + where +
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
                Fail("WarnBossDied", e);
            }
        }

        /// <summary>
        /// La durata dell'avviso, letta dalla config al momento della notifica: e' il numero che rende
        /// verificabile "4 s, e si spegne prima che la scena parta".
        /// </summary>
        private static string DurationSeconds()
        {
            try
            {
                if (Main.Multiplayer != null && Main.Multiplayer.config != null)
                {
                    return Main.Multiplayer.config.notificationDisplaySeconds
                        .ToString("0.0", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                // Solo diagnostica: se la config non si legge, la notifica e' gia' partita lo stesso.
            }

            return "?";
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
