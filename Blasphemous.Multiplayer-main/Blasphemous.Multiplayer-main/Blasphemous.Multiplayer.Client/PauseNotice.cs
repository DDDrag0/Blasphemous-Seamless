using System;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐⭐ B — LA NOTIFICA "L'ALTRO E' IN PAUSA". **Solo informazione: questa classe NON mette in pausa
    /// nessuno.** Non chiama `PauseGame`/`ResumeGame`, non scrive `Time.timeScale`, non tocca la UI del
    /// gioco, non aggiunge una patch Harmony: legge un booleano e, quando cambia, scrive un byte in coda.
    ///
    /// IL PROBLEMA CHE CHIUDE, in una riga. Quando uno dei due apre il menu, **il mondo dell'altro resta
    /// fermo lo stesso** (i nemici che il pausante possiede non ricevono update: `Time.deltaTime = 0` ⇒
    /// i suoi timer di invio sono fermi) e **l'altro non sa perche'**: vede le statue e aspetta. La pausa
    /// **condivisa** e' la strada piu' larga (`scratch\STUDIO-PAUSA-CONDIVISA.md` §6), ma tocca il
    /// `timeScale` del gioco: qui si fa **la terza strada**, quella che non lo tocca (§6.2), e che **non
    /// toglie niente** a una pausa condivisa futura — il canale e il pacchetto sarebbero gia' questi.
    ///
    /// ⭐ IL FRONTE, NON LO STATO — ed e' tutta la correttezza del meccanismo. In `Tick()` si legge
    /// `Core.Logic.IsPaused` **una volta per frame** (polling: le vie di pausa sono SETTE e le riprese
    /// DODICI, non c'e' una porta sola da patchare) e si trasmette **il cambio**, non il valore. Il
    /// rimbalzo di RETE non esiste (il server esclude il mittente, `Server.cs:676-687`) e quello di
    /// LOGICA non puo' esistere qui **perche' nessun pacchetto cambia `IsPaused`**: chi riceve mostra una
    /// riga a schermo e basta, quindi non produce nessun fronte da rimandare. Non serve nessun flag
    /// d'origine: **il ciclo e' di un solo hop per costruzione.**
    ///
    /// ⭐ IL BATTITO, E PERCHE' C'E'. La notifica deve restare accesa **finche'** l'altro e' in pausa, e
    /// una pausa puo' durare minuti: una scadenza breve la spegnerebbe mentre e' ancora vera, una lunga
    /// lascerebbe la frase a schermo per sempre se l'altro sparisce (disconnessione, altra camera, build
    /// diversa). La forma che soddisfa entrambe: **chi e' in pausa rimanda lo stesso fronte ogni
    /// `HEARTBEAT_SECONDS`**, e chi riceve rinnova la scadenza a ogni arrivo. Cosi' la notifica cade da
    /// sola entro `TIMEOUT_SECONDS` dall'ultimo battito — che e' **l'unica difesa possibile**: nel
    /// protocollo non esiste nessun predicato di versione, e due build diverse sono indistinguibili
    /// sulla rete (protocollo 11 uguale per tutte).
    ///
    /// ⚠️ GLI OROLOGI SONO TUTTI `Time.unscaledTime`: con `timeScale = 0` `Time.time` **si ferma** e
    /// `Time.deltaTime` vale 0, quindi un timeout scritto su quelli **non scatterebbe mai** proprio nel
    /// caso in cui serve. Il precedente in casa e' dichiarato: `BossDeathWarning.cs:61-65`,
    /// `PerformanceProbe.cs:253-255`. E `OnLateUpdate` gira anche a `timeScale = 0` (provato:
    /// `scratch\afk\tail-G1.txt` righe 706-708 ⇒ log 4731-4732 dentro la finestra di pausa).
    ///
    /// IL CANALE: `AttackType.PauseRelay = 203`, **un byte di verso** nel payload (1 = in pausa,
    /// 0 = ripresa). Zero righe di protocollo, zero righe di server: il server inoltra i byte senza
    /// interpretarli e **il filtro per stanza e' gratis** (canale Attack, `Server.cs:676-687`).
    ///
    /// IL TESTO, in inglese e ASCII puro (una riga, tutti i caratteri sotto il 128):
    ///     "YOUR TEAMMATE IS PAUSED"
    /// E' una **proposta**, dichiarata nel referto `scratch\REFERTO-NRE-E-PAUSA.md`.
    ///
    /// ⛔ LE DUE REGOLE, e sono regole non implementazione:
    ///   ① **non si arma se uno dei due e' a terra**: la morte **FORZA** `ResumeGame()` (`UIController.OnDead`)
    ///      ⇒ un fronte di pausa di un morto durerebbe un istante. Il predicato del peer e' quello che la
    ///      casa usa gia' per la stessa domanda: `EnemySyncManager.AnyOtherPlayerHereAndAlive()` (stanza
    ///      **e** vivo); il locale e' `PlayerDeathSync.IsLocalPlayerDead()`.
    ///   ② **se il peer si disconnette, la notifica DEVE cadere**: non arrivera' mai una ripresa, quindi
    ///      la difesa e' il timeout — **piu'** le tre uscite rapide del §"Check": disconnesso, nessun peer
    ///      connesso, peer non piu' qui/vivo.
    ///
    /// TUTTO E' DENTRO `try/catch`: gira nel ciclo per frame della mod, e **una notifica non deve mai
    /// rompere la partita**. Nessun errore esce mai da questa classe.
    /// </summary>
    internal static class PauseNotice
    {
        /// <summary>Marker di ESITO, obbligatorio su ogni riga: si conta da solo nel log.</summary>
        private const string MARK = "[Pausa]";

        /// <summary>
        /// L'INTERRUTTORE: `false` spegne QUESTA notifica e nient'altro (non manda, non mostra, non
        /// tocca il timer). `static readonly` e non `const` per la stessa ragione di
        /// `BossFightDeathWarning.cs:79`: con un `const true` il ramo `if (!X)` diventa irraggiungibile e
        /// il compilatore emette CS0162 a ogni build.
        /// </summary>
        private static readonly bool NOTICE_ON_TEAMMATE_PAUSED = true;

        /// <summary>Il testo: inglese, ASCII puro, una riga sola. La PROPOSTA, dichiarata nel referto.</summary>
        private const string TEXT = "YOUR TEAMMATE IS PAUSED";

        // ---- il payload sul canale Attack: [AttackType.PauseRelay][verso]. Un byte, e non e' uno stato:
        //      e' la direzione del cambiamento. Vedi `Enums.cs` accanto a 200/201/202.
        private const byte WIRE_PAUSED = 1;
        private const byte WIRE_RESUMED = 0;

        // ---- il battito e la scadenza, in SECONDI NON SCALATI (vedi il commento di classe).
        //      3 s di battito e 8 s di scadenza: due battiti persi di fila non fanno cadere la notifica
        //      mentre l'altro e' ancora in pausa, e una macchina sparita la fa cadere in 8 secondi.
        private const float HEARTBEAT_SECONDS = 3f;
        private const float TIMEOUT_SECONDS = 8f;

        // ---- il fronte LOCALE: l'ultimo `IsPaused` osservato, e "sto annunciando una pausa".
        private static bool _wasPaused;
        private static bool _pauseTxAlive;
        private static float _nextHeartbeatAt;

        // ---- il fronte REMOTO: la notifica e' a schermo, da chi, e fino a quando.
        private static bool _remotePaused;
        private static float _remoteDeadline;
        private static string _remoteFrom = "?";

        // ---- diagnostica: i contatori che il referto cita, e l'interruttore spento detto UNA volta.
        private static int _fronts;
        private static int _sent;
        private static int _received;
        private static bool _toggleOffReported;

        /// <summary>
        /// UN GIRO PER FRAME, da `Multiplayer.OnLateUpdate`, **dopo** `NetworkManager.ProcessQueue()`
        /// (un fronte che arriva dal filo si applica prima di essere guardato) e **dopo**
        /// `NotificationManager.Update()`. Il `try/catch` non e' decorativo: un'eccezione qui uscirebbe
        /// dentro il ciclo per frame e salterebbe, come le 63 NRE, tutto quello che viene dopo.
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
                                    "nessun pacchetto, nessuna riga a schermo");
                    }
                    return;
                }

                ObserveLocalFront();
                CheckRemoteStillPaused();
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// ⭐ IL FRONTE LOCALE. Si legge `IsPaused` (polling, una volta per frame) e si reagisce **solo al
        /// cambio**: `false→true` = il mio giocatore ha aperto una delle sette vie di pausa; `true→false` =
        /// ne e' uscito (o e' morto: la morte forza `ResumeGame()`). Finche' resta in pausa, ogni
        /// `HEARTBEAT_SECONDS` si rimanda lo STESSO fronte, che e' cio' che tiene viva la notifica di la'.
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
                    if (TrySendFront(true))
                    {
                        _pauseTxAlive = true;
                        _nextHeartbeatAt = Now() + HEARTBEAT_SECONDS;
                    }
                }
                else
                {
                    TrySendFront(false);
                    _pauseTxAlive = false;
                }

                return;
            }

            // ---- il battito: solo mentre sto annunciando una pausa.
            if (paused && _pauseTxAlive && Now() >= _nextHeartbeatAt)
            {
                _nextHeartbeatAt = Now() + HEARTBEAT_SECONDS;
                TrySendFront(true);
            }
        }

        /// <summary>
        /// MANDA UN FRONTE, se le guardie lo permettono, e scrive SEMPRE il perche' (mandato oppure
        /// frenato, con il motivo). Le guardie sono quelle del §7 del referto di studio, tutte su
        /// predicati che esistono gia':
        ///   `CurrentlyInLevel` (fuori livello `CurrentLevelConfig` puo' essere null: e' la NRE n.2 dello
        ///   studio), `IsConnected`, `ConnectedPlayersCount() > 0`, e poi - **solo per la pausa** -
        ///   `IsLocalPlayerDead()` e `AnyOtherPlayerHereAndAlive()`.
        /// ⚠️ La RIPRESA non chiede "sono vivo": togliere una notifica e' sempre lecito, e se sono morto
        ///    e' proprio la morte ad aver prodotto il fronte (§regola ①). Se la pausa non e' mai stata
        ///    annunciata, la ripresa non si manda affatto (`_pauseTxAlive`): niente pacchetti inutili.
        /// </summary>
        private static bool TrySendFront(bool isPause)
        {
            string tipo = isPause ? "pausa" : "ripresa";

            if (!isPause && !_pauseTxAlive)
            {
                ModLog.Info(MARK + " fronte=ripresa NON inviato · motivo=nessuna-pausa-annunciata " +
                            "(non e' mai partito un annuncio di pausa: non c'e' niente da ritirare)");
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
                        " · il pacchetto esce da SendQueue() nello stesso frame (per questo la NRE di " +
                        "MapManager doveva essere corretta prima)");

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
        /// e' `[attackType][verso]` e il nome del mittente lo mette il server davanti
        /// (`Server.cs:694-699`), quindi `startIdx` punta gia' oltre il nome.
        ///
        /// `pausa` ⇒ si ARMA (o si RINNOVA, se e' un battito): la notifica va a schermo e la scadenza si
        /// sposta avanti. `ripresa` ⇒ si TOGLIE subito, senza aspettare il timeout.
        /// ⚠️ NON si manda niente indietro, e non si chiama `PauseGame`: chi riceve **non e' in pausa**,
        ///    sta solo leggendo una riga.
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
                    Disarm("ricevuta ripresa da " + Show(from));
                    ModLog.Info(MARK + " RX fronte=ripresa da=" + Show(from) + " · ricevuti=" + _received +
                                (cEra
                                    ? " · la notifica e' stata tolta SUBITO (non si aspetta il timeout)"
                                    : " · nessuna notifica era a schermo (idempotente: non c'era niente da togliere)"));
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
        /// ARMA la notifica (o la rinnova). Il testo e' fisso: non serve il nome, e non cambia con
        /// l'identita' di chi e' in pausa — con due soli giocatori "il compagno" e' gia' una risposta.
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
                return;
            }

            // Idempotente sullo stesso testo: chiamarla a ogni battito non crea righe in piu'.
            Main.Multiplayer.NotificationManager.DisplayStickyNotification(TEXT);

            if (gia)
            {
                // Battito di conferma: si rinnova la scadenza e basta, senza sporcare il log.
                return;
            }

            _remotePaused = true;

            ModLog.Info(MARK + " RX fronte=pausa da=" + _remoteFrom + " · notifica A SCHERMO \"" + TEXT + "\" " +
                        "· resta finche' non arriva la ripresa o non scade il timeout di " +
                        TIMEOUT_SECONDS + "s (battito ogni " + HEARTBEAT_SECONDS + "s) · ricevuti=" + _received +
                        " · io=" + Me() + " · il giocatore locale NON e' stato messo in pausa: " +
                        "nessuna chiamata a PauseGame, nessun Time.timeScale toccato");
        }

        /// <summary>
        /// ⭐⭐ LA NOTIFICA CADRE DA SOLA — e questo e' l'unico punto in cui puo' cadere. Quattro uscite,
        /// tutte con la ragione scritta:
        ///   · **timeout** (la piu' importante: copre disconnessione, altra stanza e **build diversa**);
        ///   · non piu' connesso; · nessun peer connesso; · il peer non e' piu' qui (o e' a terra).
        /// Le ultime tre sono piu' rapide del timeout e non costano niente: esistono perche' il caso
        /// normale (l'altro chiude il gioco) non deve aspettare 8 secondi per essere detto.
        /// </summary>
        private static void CheckRemoteStillPaused()
        {
            if (!_remotePaused)
                return;

            if (Now() > _remoteDeadline)
            {
                Disarm("TIMEOUT di " + TIMEOUT_SECONDS + "s scaduto: nessun battito da " + _remoteFrom +
                       " (disconnesso, oppure in un'altra stanza, oppure build diversa - il protocollo non " +
                       "distingue le build, quindi il timeout e' l'unica difesa)");
                return;
            }

            if (Main.Multiplayer.NetworkManager == null || !Main.Multiplayer.NetworkManager.IsConnected)
            {
                Disarm("disconnesso dal server");
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

        /// <summary>Toglie la notifica, se c'e', e dice perche'. Non fa niente se non c'e' (caso normale).</summary>
        private static void Disarm(string reason)
        {
            if (!_remotePaused)
                return;

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

        /// <summary>Lo stato del gioco locale, senza mai lanciare (durante un load `Core.Logic` c'e' sempre, ma non si scommette).</summary>
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

        /// <summary>L'ultimo `catch` prima del ciclo per frame: non rilancia mai.</summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (la notifica non ha interrotto niente: la partita e' proseguita)");
            }
            catch (Exception)
            {
                // Se anche il log fallisce non c'e' un secondo posto dove dirlo.
            }
        }
    }
}
