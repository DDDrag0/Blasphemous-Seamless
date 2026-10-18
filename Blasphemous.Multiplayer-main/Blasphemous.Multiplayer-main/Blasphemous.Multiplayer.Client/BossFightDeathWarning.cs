using System;
using System.Globalization;
using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐⭐ L'AVVISO A SCHERMO AL GIOCATORE CHE MUORE **IN BOSSFIGHT**: "aspetta i compagni, non respawnare".
    ///
    /// LA RICHIESTA DELL'UTENTE, testuale: *"Appena si muore in bossfight, viene fatto un avviso dove viene
    /// consigliato di non respawnare e attendere gli altri giocatori."* L'avviso esistente
    /// (<see cref="BossDeathWarning"/>) parla di un'altra cosa e in un altro momento: quello e' il
    /// "DO NOT PRESS" della **morte del boss**, e vale solo per la cutscene. Qui la morte e' quella del
    /// **giocatore**, e la ragione e' un'altra: chi e' a terra in una bossfight e respawna al falò
    /// **lascia la stanza** - la fight perde un giocatore, il boss resta a meta' e la stanza non si chiude
    /// finche' qualcuno non la finisce. Il consiglio giusto, in quel momento, e' **aspettare**.
    ///
    /// QUANDO, ED E' TUTTA LA DIFFERENZA CON L'ALTRO AVVISO. Il segnale e' la **transizione** del
    /// giocatore locale da vivo a morto (`PlayerDeathSync.IsLocalPlayerDead()`, lo stesso predicato di
    /// `DeathScreenTransparency`, `DeathCorpseFollow` e `BossDeathWarning`) letta in `Tick()`, cioe' nello
    /// stesso ciclo per frame di tutte le sonde ⇒ **zero patch Harmony** (i bersagli restano quelli di
    /// HEAD: nessuna patch nuova). Una transizione = una finestra di morte = **un avviso**: e' la regola
    /// "una volta per finestra di morte" scritta come flag (`_warnedThisWindow`), azzerata quando il
    /// giocatore e' di nuovo vivo.
    ///
    /// ⭐ IL PREDICATO "IN BOSSFIGHT", E PERCHE' E' QUELLO. Non esiste nel gioco un "sono in una
    /// bossfight" leggibile senza patch; quello che esiste **nella mod** e' la domanda che la fight la
    /// descrive per davvero: **c'e' un boss VIVO in questa scena?**
    /// (`EnemySyncManager.CountLiveBossesInScene()`, lo stesso predicato di `[BossJoin]`/`[BossFight]`,
    /// costruito su `IsBoss` = il namespace `Gameplay.GameControllers.Bosses`) ⇒ se **non** c'e' nessun
    /// boss vivo, questo avviso **non esce** (e lo dice nel log): morire in un corridoio non e' una
    /// bossfight, e "aspetta i compagni" li' non e' un consiglio, e' rumore.
    /// ⚠️ IL PREDICATO SI LEGGE **UNA VOLTA PER MORTE**, non a ogni frame: `FindAllEnemiesIncludingInactive`
    /// percorre i root della scena con `GetComponentsInChildren(true)`, ed e' la ragione per cui
    /// `[BossFight]` lo legge ogni 2 s e non ogni frame (vedi `EnemySyncManager`, commento del latch).
    ///
    /// ⭐ E UNA SECONDA CONDIZIONE, DICHIARATA: che ci sia **almeno un altro giocatore connesso**
    /// (`EnemySyncManager.ConnectedPlayersCount()`). La frase dice "i compagni": se non ce n'e' nessuno
    /// (partita in solitaria con la mod installata) l'avviso sarebbe **falso**, non solo inutile. Non
    /// impedisce niente e non tocca niente: e' un cancello sul TESTO, ed e' scritto nel log con il numero,
    /// cosi' "non e' uscito" ha sempre un perche' e non e' mai un silenzio.
    ///
    /// NON SI SOVRAPPONE A "DO NOT PRESS": sono due momenti diversi (la **mia** morte adesso, la morte del
    /// **boss** ~9-21 s dopo, misurato in `scratch\DIAGNOSI-REGRESSIONE-RIENTRO.md` §4.2), e in piu' c'e'
    /// un cancello esplicito: se la finestra della cutscene di `BossDeathWarning` e' **aperta** in questo
    /// istante, questo avviso **tace** e lo dice (`BossDeathWarning.CutsceneWindowOpen`). Le due
    /// notifiche, comunque, non potrebbero nemmeno accavallarsi sul testo: `NotificationManager` disegna
    /// **una riga per notifica** (`textLines[i].anchoredPosition`, `NotificationManager.cs:150`) e ne
    /// tiene al massimo 4 (`MAX_LINES`, `:12`).
    ///
    /// IL MECCANISMO E' LO STESSO DELL'ALTRO AVVISO: `NotificationManager` **DELLA MOD**
    /// (`Main.Multiplayer.NotificationManager`, quello di "connesso"/"disconnesso"), non la UI del gioco.
    /// Nessuna patch, nessun `SetActive`, nessun blocker, nessun `TimeScale`, nessuna camera, nessun
    /// `Penitent` toccato: si accoda una riga, il suo `Update` la disegna e la fa sfumare da sola
    /// (`config.notificationDisplaySeconds`, 4 s di default). **E' un avviso, non un prompt modale.**
    ///
    /// IL TESTO, in inglese e senza un solo carattere accentato (tutti ASCII), e' una **proposta**, ed e'
    /// dichiarata nel referto `scratch\REFERTO-AVVISI-MORTE.md`:
    ///     "WAIT FOR YOUR TEAMMATES: do not respawn - the boss fight is still in progress"
    ///
    /// TUTTO E' DENTRO `try/catch`: questo `Tick` gira nel ciclo per frame della mod e viene invocato
    /// nel frame in cui il giocatore muore. **Una notifica non deve mai rompere una morte**, e l'errore
    /// non esce mai da questa classe.
    /// </summary>
    internal static class BossFightDeathWarning
    {
        /// <summary>
        /// Marker di ESITO, obbligatorio su ogni riga. E' **distinto** da `[BossWarn]` di proposito: sono
        /// due avvisi diversi, in due momenti diversi, e nel log si devono poter contare separatamente.
        /// </summary>
        private const string MARK = "[DeathWarn]";

        /// <summary>
        /// L'INTERRUTTORE: `false` spegne QUESTO avviso e nient'altro (l'altro, `BossDeathWarning`, ha il
        /// suo). `static readonly` e non `const` per la ragione scritta in `BossDeathWarning.cs`: con un
        /// `const true` la riga `if (!X) return;` diventa codice irraggiungibile e il compilatore emette
        /// CS0162 a ogni build.
        /// </summary>
        private static readonly bool WARN_ON_DEATH_IN_BOSSFIGHT = true;

        /// <summary>Il testo: inglese, ASCII puro, una riga sola.</summary>
        private const string TEXT =
            "WAIT FOR YOUR TEAMMATES: do not respawn - the boss fight is still in progress";

        // ---- il fronte della finestra di morte: "il giocatore locale e' a terra".
        private static bool _dead;

        // ---- "in QUESTA finestra di morte l'avviso e' gia' stato deciso" (mostrato OPPURE scartato con
        //      una ragione): senza, un tremolio di `IsLocalPlayerDead()` (che risponde `false` anche
        //      quando il `Penitent` non esiste, cioe' durante un load) produrrebbe due avvisi in una
        //      morte sola. Si azzera quando il giocatore e' di nuovo vivo.
        private static bool _warnedThisWindow;

        // ---- diagnostica: una riga sola per "spento dall'interruttore", i contatori per il referto.
        private static bool _toggleOffReported;
        private static int _windows;
        private static int _warnings;

        /// <summary>
        /// Un giro per frame, da `Multiplayer.OnLateUpdate`, subito dopo `BossDeathWarning.Tick()`: cosi'
        /// la finestra della cutscene e' gia' aggiornata quando questo avviso decide se tacere.
        /// Il `try/catch` non e' decorativo: un'eccezione qui uscirebbe dentro il ciclo per frame.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (dead && !_dead)
                {
                    _windows++;
                    Warn();
                }

                _dead = dead;

                if (!dead)
                    _warnedThisWindow = false;
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// L'AVVISO. Decide, e scrive SEMPRE il perche': mostrato, spento, fuori bossfight, nessun
        /// compagno connesso, gia' deciso in questa finestra, NotificationManager assente. Ogni uscita
        /// anticipata e' una riga di log, perche' "non ha avvisato" senza motivo non si puo' verificare.
        /// </summary>
        private static void Warn()
        {
            try
            {
                if (_warnedThisWindow)
                {
                    ModLog.Info(MARK + " avviso GIA' deciso in questa finestra di morte n." + _windows +
                                " (uno per finestra): nessun secondo avviso");
                    return;
                }

                _warnedThisWindow = true;

                if (!WARN_ON_DEATH_IN_BOSSFIGHT)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " avviso SPENTO dall'interruttore " +
                                    "(WARN_ON_DEATH_IN_BOSSFIGHT=false): nessuna notifica a schermo · " +
                                    "finestra di morte n." + _windows);
                    }
                    return;
                }

                // ⭐ IL PREDICATO "IN BOSSFIGHT": c'e' un boss VIVO in questa scena?
                int bossVivi = EnemySyncManager.CountLiveBossesInScene();
                if (bossVivi <= 0)
                {
                    ModLog.Info(MARK + " avviso NON mostrato: FUORI bossfight " +
                                "(EnemySyncManager.CountLiveBossesInScene()=0, nessun boss vivo in scena) · " +
                                "finestra di morte n." + _windows);
                    return;
                }

                // ⭐ E la seconda condizione, dichiarata: senza compagni la frase sarebbe falsa.
                int compagni = EnemySyncManager.ConnectedPlayersCount();
                if (compagni <= 0)
                {
                    ModLog.Info(MARK + " avviso NON mostrato: nessun altro giocatore connesso " +
                                "(EnemySyncManager.ConnectedPlayersCount()=0, quindi non c'e' nessun " +
                                "'compagno' da aspettare: il testo sarebbe falso) · bossVivi=" + bossVivi +
                                " · finestra di morte n." + _windows);
                    return;
                }

                // ⭐ IL CANCELLO CHE IMPEDISCE LA SOVRAPPOSIZIONE con "DO NOT PRESS".
                if (BossDeathWarning.CutsceneWindowOpen)
                {
                    ModLog.Info(MARK + " avviso NON mostrato: la finestra della cutscene di " +
                                "BossDeathWarning e' APERTA in questo istante (sarebbero due avvisi " +
                                "sovrapposti: vince quello della cutscene) · bossVivi=" + bossVivi +
                                " · finestra di morte n." + _windows);
                    return;
                }

                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                {
                    ModLog.Error(MARK + " avviso NON mostrato: NotificationManager non disponibile · " +
                                 "bossVivi=" + bossVivi + " · finestra di morte n." + _windows);
                    return;
                }

                _warnings++;

                Main.Multiplayer.NotificationManager.DisplayNotification(TEXT);

                ModLog.Info(MARK + " AVVISO A SCHERMO n." + _warnings + " (finestra di morte n." + _windows +
                            ") · \"" + TEXT + "\" · dove=transizione vivo->morto del giocatore locale " +
                            "(PlayerDeathSync.IsLocalPlayerDead()=True) · " +
                            "predicato in-bossfight=EnemySyncManager.CountLiveBossesInScene()=" + bossVivi +
                            " (boss VIVO in scena, IsBoss = namespace Gameplay.GameControllers.Bosses) · " +
                            "compagniConnessi=" + compagni + " · durata=" + DurationSeconds() + "s " +
                            "(config.notificationDisplaySeconds) · NotificationManager DELLA MOD: zero " +
                            "patch Harmony, nessuna UI del gioco toccata, nessun blocker, nessun SetActive, " +
                            "nessun TimeScale, nessuna camera, nessun Penitent toccato · uno per finestra di " +
                            "morte, e tace se la finestra della cutscene di BossDeathWarning e' aperta");
            }
            catch (Exception e)
            {
                Fail("Warn", e);
            }
        }

        /// <summary>
        /// La durata dell'avviso, letta dalla config al momento della notifica (la stessa sorgente di
        /// `NotificationManager`: `config.notificationDisplaySeconds`).
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

        /// <summary>L'ultimo `catch` prima del ciclo per frame: non rilancia mai.</summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (l'avviso non ha interrotto niente: la morte del giocatore e' proseguita)");
            }
            catch (Exception)
            {
                // Se anche il log fallisce non c'e' un secondo posto dove dirlo.
            }
        }
    }
}
