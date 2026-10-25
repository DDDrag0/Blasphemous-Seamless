using System;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐ L'AVVISO "IL TUO COMPAGNO E' IN PAUSA" — e **nient'altro**. Questa classe **mostra** una riga a
    /// schermo e **non agisce sul gioco**: non chiama `Core.Logic.PauseGame()` ne' `ResumeGame()`, non apre
    /// e non chiude nessun menu', non scrive `Time.timeScale`, non simula input. Legge un booleano
    /// (`Core.Logic.IsPaused`), lo manda come **flag**, e dall'altra parte accende o spegne una riga.
    ///
    /// 🔴 LA CAUSA (misurata, `scratch\DIAGNOSI-PAUSA-BUG.md`). La V2 di questa classe **condivideva** la
    /// pausa: chi andava in pausa la faceva applicare all'altro con `Core.Logic.PauseGame()`, e l'altro la
    /// toglieva al "riprendi". Ma le pause del gioco **non sono tutte una scelta**: tutorial, dialoghi e
    /// cutscene mettono in pausa da se' (`input blocker` di sistema), e quella non e' una decisione del
    /// giocatore. Trasmessa come comando, produceva questo:
    ///   · il peer applicava `PauseGame()` e restava fermo **senza nessun menu' aperto**;
    ///   · al "riprendi" la chiusura passava dal ramo ③ di `CloseAndResume`, protetto da
    ///     `Core.Input.InputBlocked` — vero per un blocker di sistema (`IntroBlock`, `CINEMATIC`) che
    ///     nessuno spegneva;
    ///   · ⇒ **123 tentativi in 2 s**, poi il tetto: "ripresa FRENATA", il flag cadeva, `RetryClose`
    ///     azzerava la coda e **nessun percorso ritentava piu'** (`G1:1050…G1:1059`);
    ///   · ⇒ ⭐ **gioco fermo per sempre** (`timeScale = 0`, nessuno riporta `IsPaused` a false).
    /// 📌 La condivisione era **strutturalmente sbagliata**: propagava una cosa che **non e' una scelta**.
    ///
    /// ✅ IL FIX — CINQUE COSE, e sono tutte qui dentro:
    ///   ① **non si propaga la pausa locale come comando**: sul filo non esce nessun "metti in pausa";
    ///   ② **non si applica nessuna pausa remota**: `Core.Logic.PauseGame()` **non si chiama piu'**, e con
    ///      lui se ne vanno l'apertura della mappa che lo camuffava (`TryOpenMapOntoPassive`) e la
    ///      chiusura (`CloseAndResume`);
    ///   ③ **con quelle spariscono le guardie**: `InputBlocked`, il retry con il tetto, il
    ///      "dichiara e dimentica" (`RetryClose` → "ripresa FRENATA"), `StepRemotePauseGuards` e il flag
    ///      `_pausedByRemote`: **rimossi**, non commentati;
    ///   ④ **l'avviso resta**, ma il "saperlo" **non passa da un comando**: nel **battito** viaggia un
    ///      **flag** `pausa locale: si'/no`. La mod **mostra**, non agisce. Quando il compagno riprende il
    ///      flag torna "no" e **l'avviso sparisce da solo** (stessa riga persistente della V2:
    ///      `DisplayStickyNotification` / `ClearStickyNotification`);
    ///   ⑤ **IL CANCELLO — solo con un server** (host o peer): tutto questo vale **solo** con una
    ///      connessione viva, letto dal predicato che usano gia' la morte trasparente e la Colpa
    ///      (`CoopGate.IsMultiplayerActive()`). ⛔ **In single player non cambia niente**: nessun flag
    ///      mandato, nessun avviso mostrato, nessuna pausa toccata — e il gioco pausa per se' come ha
    ///      sempre fatto (non c'e' nessuno da avvisare).
    ///
    /// ⭐ IL FILO **NON CAMBIA DI UNA VIRGOLA**, ed e' voluto: canale `AttackType.PauseRelay = 203`
    /// (il quinto verso del canale Attack), payload di **un byte**, battito ogni 3 s, timeout 8 s, classe
    /// di trasporto `Reliable` (la decide `NetworkManager.IsReliableAttackPayload` dal primo byte).
    /// Cambia **il significato** del byte, non la sua forma: era un *comando* ("metti/leva la pausa"),
    /// ora e' uno *stato* ("in pausa: 1 / no: 0") che il battito **rimanda** finche' dura. Zero righe di
    /// protocollo (resta 11), zero righe di server, zero righe di adapter: il server inoltra il canale
    /// Attack ai peer della stessa scena escludendo il mittente, come faceva prima.
    ///
    /// ⚠️ GLI OROLOGI SONO `Time.unscaledTime`: con `timeScale = 0` `Time.time` si ferma e `Time.deltaTime`
    /// vale 0, quindi un battito o un timeout scritti su quelli non scatterebbero mai proprio quando
    /// servono. E `OnLateUpdate` gira anche a `timeScale = 0` (e' il frame in cui questa classe deve
    /// vedere la pausa **locale**, che il gioco ha appena messo).
    ///
    /// ⚠️ TUTTO E' DENTRO `try/catch`: un avviso non deve mai rompere la partita. Nessun errore esce mai
    /// da questa classe.
    /// </summary>
    internal static class PauseNotice
    {
        /// <summary>Marker di ESITO, obbligatorio su ogni riga: si conta da solo nel log.</summary>
        private const string MARK = "[Pausa]";

        /// <summary>
        /// L'INTERRUTTORE: `false` spegne TUTTA la funzione (non manda il flag, non mostra l'avviso).
        /// `static readonly` e non `const` per la stessa ragione di `BossFightDeathWarning.cs:79`: con un
        /// `const true` il ramo `if (!X)` diventa irraggiungibile e il compilatore emette CS0162.
        /// </summary>
        private static readonly bool NOTICE_ON_TEAMMATE_PAUSED = true;

        /// <summary>Il testo della notifica: inglese, ASCII puro, una riga sola.</summary>
        private const string TEXT = "YOUR TEAMMATE IS PAUSED";

        // ---- il FLAG sul canale Attack: [AttackType.PauseRelay][pausa locale]. Un byte, e NON e' un
        //      comando: chi lo riceve accende o spegne una riga, e basta. 1 = in pausa, 0 = no.
        private const byte FLAG_PAUSED = 1;
        private const byte FLAG_RUNNING = 0;

        // ---- il battito e la scadenza, in SECONDI NON SCALATI (vedi il commento di classe).
        //      Il battito e' il canale del FLAG: finche' il compagno e' in pausa il "si'" si rimanda ogni
        //      3 s, e se smette di arrivare (gioco chiuso, rete morta) l'avviso cade dopo 8 s — due
        //      battiti persi di fila non bastano a far cadere una riga vera.
        private const float HEARTBEAT_SECONDS = 3f;
        private const float TIMEOUT_SECONDS = 8f;

        // ---- il flag LOCALE: l'ultimo `IsPaused` osservato e l'ultimo flag SPEDITO (per non ripeterlo a
        //      ogni frame: si manda al cambio, e poi a battito solo mentre il flag dice "si'").
        private static bool _wasPaused;
        private static bool _lastSentFlag;
        private static bool _lastSentKnown;
        private static float _nextTxAt;

        // ---- il flag REMOTO: l'avviso e' a schermo, da chi, e fino a quando.
        private static bool _remotePaused;
        private static float _remoteDeadline;
        private static string _remoteFrom = "?";

        // ---- diagnostica: i contatori che il referto cita, e i due "una volta sola".
        private static int _edges;
        private static int _sent;
        private static int _received;
        private static int _shown;
        private static int _cleared;
        private static string _lastNotSent = "";
        private static bool _toggleOffReported;

        // ---- il CANCELLO (⑤): 0 = mai guardato, 1 = coop (connesso), 2 = single player. Serve solo a
        //      scrivere UNA riga quando lo stato cambia, invece di una per frame.
        private static int _gateState;

        /// <summary>
        /// UN GIRO PER FRAME, da `Multiplayer.OnLateUpdate`, **dopo** `NetworkManager.ProcessQueue()`
        /// (un flag che arriva dal filo si applica prima di essere guardato) e **dopo**
        /// `NotificationManager.Update()`.
        /// L'ORDINE DEI TRE PASSI E' SIGNIFICATIVO:
        ///   ① il cancello (⑤) — senza un server non si fa **niente**, e un avviso rimasto appeso cade;
        ///   ② il flag locale (④) — cosi' la ripresa parte nello stesso frame in cui il gioco riprende;
        ///   ③ le uscite del flag remoto (timeout, disconnessione, stanza) — che possono far cadere
        ///      l'avviso **senza toccare il gioco**.
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
                        ModLog.Info(MARK + " avviso SPENTO dall'interruttore (NOTICE_ON_TEAMMATE_PAUSED=false): " +
                                    "nessun flag, nessuna riga a schermo, nessuna pausa condivisa");
                    }
                    return;
                }

                if (!CoopGate.IsMultiplayerActive())
                {
                    SinglePlayer();            // ⑤ il cancello: senza server non esiste questa funzione
                    return;
                }

                GateOpened();                  // ⑤ la riga del cancello, una per ingresso in co-op

                ObserveLocalFlag();            // ④ il flag locale + il battito
                CheckRemoteStillPaused();      // le uscite dell'avviso
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        // ============================================================================================
        // ⑤ IL CANCELLO — solo con un server (host o peer). In single player **non cambia niente**: il
        // gioco pausa per se' (e non c'e' nessun peer da avvisare), quindi la mod tace del tutto.
        // Il predicato e' lo STESSO della morte trasparente e della Colpa (`CoopGate`): una definizione
        // sola di "sto giocando in co-op", e se un giorno cambia, cambia per tutte e tre.
        // ============================================================================================
        private static void SinglePlayer()
        {
            try
            {
                // L'avviso non ha senso senza un peer: se era a schermo cade qui, senza toccare il gioco.
                if (_remotePaused)
                    Clear("nessun server (il cancello e' chiuso): l'avviso non puo' restare acceso");

                // Il flag locale si riallinea in silenzio: in single player **non si trasmette niente**, e
                // quando si entra in co-op si manda lo stato vero una volta sola (vedi `GateOpened`).
                _wasPaused = IsLocalPaused();
                _lastSentKnown = false;
                _nextTxAt = 0f;

                if (_gateState != 2)
                {
                    bool prima = _gateState == 1;
                    _gateState = 2;
                    ModLog.Info(MARK + " cancello CHIUSO (nessun server): questa funzione tace del tutto - " +
                                "nessun flag mandato, nessun avviso a schermo, NESSUNA pausa toccata (il gioco " +
                                "pausa per se', come ha sempre fatto: in single player non c'e' nessun peer " +
                                "da avvisare)" + (prima ? " · la connessione e' caduta in questo istante" : ""));
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
                _lastSentKnown = false;   // ⇒ il primo giro manda lo stato VERO (anche "sono gia' in pausa")
                _nextTxAt = 0f;

                ModLog.Info(MARK + " cancello APERTO (server connesso: host o peer): da qui vale l'avviso - " +
                            "la mod legge `Core.Logic.IsPaused` e manda il FLAG, non un comando · stato locale=" +
                            (IsLocalPaused() ? "pausa" : "in gioco"));
            }
            catch (Exception e)
            {
                Fail("GateOpened", e);
            }
        }

        /// <summary>
        /// ⭐ ④ IL FLAG LOCALE. Si legge `IsPaused` (polling, una volta per frame: le vie di pausa sono
        /// SETTE e le riprese DODICI, non c'e' una porta sola da patchare ⇒ **zero patch Harmony nuove**) e
        /// si manda il **flag** del momento:
        ///   · **al cambio** (`false→true` e `true→false`) — subito, senza aspettare il battito;
        ///   · **a battito** (ogni `HEARTBEAT_SECONDS`) mentre il flag dice "si'" — e' cio' che tiene viva
        ///     la riga sull'altra macchina e che la lascia cadere per timeout se il peer sparisce.
        /// ⛔ Qui **non si decide niente del gioco**: nessuna pausa da applicare, nessuna da togliere.
        /// </summary>
        private static void ObserveLocalFlag()
        {
            try
            {
                bool paused = IsLocalPaused();

                if (paused != _wasPaused)
                {
                    _wasPaused = paused;
                    _edges++;
                    _nextTxAt = 0f;                 // il cambio parte subito
                }

                // ⚠️ UN TENTATIVO PER BATTITO, non uno per frame: un flag che non passa (fuori livello,
                // peer in un'altra stanza) e' un **rinvio**, e un rinvio non e' una raffica.
                if (Now() < _nextTxAt)
                    return;

                bool pending = !_lastSentKnown || _lastSentFlag != paused;

                // A regime, col flag "no" gia' spedito, non si manda piu' niente: si riprende solo quando
                // lo stato cambia (o quando si torna in pausa, che e' di nuovo un cambio).
                if (!pending && !paused)
                    return;

                _nextTxAt = Now() + HEARTBEAT_SECONDS;

                if (TrySendFlag(paused))
                {
                    _lastSentFlag = paused;
                    _lastSentKnown = true;
                }
            }
            catch (Exception e)
            {
                Fail("ObserveLocalFlag", e);
            }
        }

        /// <summary>
        /// MANDA IL FLAG, se le guardie lo permettono, e scrive SEMPRE il perche'. Le guardie sono quelle
        /// di sempre, su predicati che esistono gia': `CurrentlyInLevel` (fuori livello `CurrentLevelConfig`
        /// puo' essere null), `IsConnected`, `ConnectedPlayersCount() > 0`, e — **solo per il flag "si'"** —
        /// `AnyOtherPlayerHereAndAlive()` (se il peer e' a terra o in un'altra stanza il server non gli
        /// consegnerebbe il pacchetto: mandarlo sarebbe rumore).
        /// ⚠️ Il flag "no" **non** chiede "sono vivo": spegnere un avviso e' sempre lecito.
        /// </summary>
        private static bool TrySendFlag(bool paused)
        {
            string tipo = paused ? "pausa locale: si'" : "pausa locale: no";

            string motivo = GateReason(paused);
            if (motivo != null)
            {
                if (_lastNotSent != motivo + "|" + paused)
                {
                    _lastNotSent = motivo + "|" + paused;
                    ModLog.Info(MARK + " flag=" + tipo + " NON inviato · motivo=" + motivo +
                                " · io=" + Me() + " · peerInScena=" + (EnemySyncManager.AnyOtherPlayerInMyScene() ? 1 : 0) +
                                "/" + EnemySyncManager.ConnectedPlayersCount() + " · fronti n." + _edges +
                                " · il tentativo riparte al prossimo battito (" + HEARTBEAT_SECONDS + "s): e' un rinvio, non una rinuncia");
                }
                return false;
            }

            _lastNotSent = "";
            byte[] payload = new byte[] { (byte)AttackType.PauseRelay, paused ? FLAG_PAUSED : FLAG_RUNNING };
            Main.Multiplayer.NetworkManager.SendAttackPayload(payload);
            _sent++;

            ModLog.Info(MARK + " TX flag=" + tipo + " -> canale=Attack tipo=" + (byte)AttackType.PauseRelay +
                        " verso=" + payload[1] + " · io=" + Me() +
                        " · peerInScena=" + (EnemySyncManager.AnyOtherPlayerInMyScene() ? 1 : 0) +
                        "/" + EnemySyncManager.ConnectedPlayersCount() +
                        " · inviati=" + _sent + " · fronti n." + _edges +
                        " · NON e' un comando: il peer ACCENDE o SPEGNE una riga e non ferma niente " +
                        "(nessun PauseGame, nessun menu' aperto o chiuso)" +
                        (paused ? " · il battito lo rimanda ogni " + HEARTBEAT_SECONDS + "s finche' dura" : ""));

            return true;
        }

        /// <summary>
        /// LE GUARDIE DEL FLAG, in ordine di costo: chi non passa riceve una ragione in chiaro, cosi' "non e'
        /// partito" non e' mai un silenzio. `null` = si puo' mandare.
        /// </summary>
        private static string GateReason(bool paused)
        {
            if (Main.Multiplayer.NetworkManager == null)
                return "network-manager-assente";
            if (!Main.Multiplayer.CurrentlyInLevel)
                return "fuori-livello";
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return "non-connesso";
            if (EnemySyncManager.ConnectedPlayersCount() <= 0)
                return "nessun-peer-connesso";
            if (!paused)
                return null;

            if (!EnemySyncManager.AnyOtherPlayerHereAndAlive())
                return "peer-non-qui-o-a-terra";

            return null;
        }

        /// <summary>
        /// ⭐ LA RICEZIONE, chiamata da `NetworkManager.ReceiveAttack` **prima** del ramo PvP. Il payload e'
        /// `[attackType][flag]` e il nome del mittente lo mette il server davanti, quindi `startIdx` punta
        /// gia' oltre il nome.
        ///
        /// `pausa locale: si'` ⇒ **si accende l'avviso** (`Arm`). `pausa locale: no` ⇒ **si spegne**
        /// (`Clear`). ⛔ Nessuna delle due strade tocca il gioco: qui non si mette e non si toglie una pausa,
        /// non si apre e non si chiude un menu'. Se il flag arriva mentre il cancello e' chiuso, si scarta:
        /// senza un server questa funzione non esiste.
        /// </summary>
        internal static void ApplyFromWire(string from, byte[] data, int startIdx)
        {
            try
            {
                if (!CoopGate.IsMultiplayerActive())
                {
                    ModLog.Info(MARK + " RX flag da " + Show(from) + " SCARTATO · motivo=cancello-chiuso " +
                                "(nessun server: la mod non mostra niente)");
                    return;
                }

                if (data == null || startIdx < 0 || data.Length <= startIdx + 1)
                {
                    ModLog.Info(MARK + " RX MALFORMATO da " + Show(from) + ": manca il byte del flag " +
                                "(payload di " + (data == null ? 0 : data.Length) + " byte, indice " + startIdx +
                                ") - ignorato");
                    return;
                }

                byte flag = data[startIdx + 1];
                _received++;

                if (flag == FLAG_PAUSED)
                {
                    Arm(from);
                }
                else if (flag == FLAG_RUNNING)
                {
                    Clear("flag=pausa locale: no da " + Show(from) +
                          " (il compagno ha ripreso: l'avviso sparisce subito, non si aspetta il timeout)");
                }
                else
                {
                    ModLog.Info(MARK + " RX con flag IGNOTO (" + flag + ") da " + Show(from) +
                                ": un valore che questa build non conosce - ignorato");
                }
            }
            catch (Exception e)
            {
                Fail("ApplyFromWire", e);
            }
        }

        /// <summary>
        /// ⭐ ACCENDE L'AVVISO (o lo rinnova, se e' un battito). E' l'unica azione che questa classe fa sul
        /// mondo: una riga di testo persistente. Il testo e' fisso: con due soli giocatori "il compagno" e'
        /// gia' una risposta.
        /// </summary>
        private static void Arm(string from)
        {
            try
            {
                _remoteDeadline = Now() + TIMEOUT_SECONDS;
                _remoteFrom = Show(from);

                bool gia = _remotePaused;
                _remotePaused = true;

                if (Main.Multiplayer.NotificationManager == null)
                {
                    ModLog.Error(MARK + " RX flag=pausa locale: si' da " + _remoteFrom + " ma NotificationManager " +
                                 "non e' disponibile: nessuna riga a schermo (lo stato resta acceso lo stesso)");
                    return;
                }

                // Idempotente sullo stesso testo: chiamarla a ogni battito non crea righe in piu'.
                Main.Multiplayer.NotificationManager.DisplayStickyNotification(TEXT);

                if (gia)
                    return;   // battito di conferma: si rinnova la scadenza e basta, senza sporcare il log

                _shown++;
                ModLog.Info(MARK + " RX flag=pausa locale: si' da=" + _remoteFrom + " · avviso A SCHERMO \"" +
                            TEXT + "\" · resta finche' il flag non torna \"no\" o non scade il timeout di " +
                            TIMEOUT_SECONDS + "s (battito ogni " + HEARTBEAT_SECONDS + "s) · ricevuti=" + _received +
                            " · io=" + Me() + " · la mod MOSTRA e non agisce: nessun PauseGame, nessun menu', " +
                            "nessuna pausa condivisa (il mio gioco continua a girare)");
            }
            catch (Exception e)
            {
                Fail("Arm", e);
            }
        }

        /// <summary>
        /// ⭐ SPEGNE L'AVVISO, se c'e'. Due strade ci arrivano, e sono la stessa cosa dal punto di vista del
        /// gioco: il flag del peer torna "no", oppure il flag smette di arrivare (timeout, disconnessione,
        /// peer in un'altra stanza o a terra). ⛔ In nessuno dei due casi si tocca la pausa locale: il mio
        /// menu', se e' aperto, resta aperto — un menu' aperto dal giocatore non e' affare della rete.
        /// </summary>
        private static void Clear(string reason)
        {
            try
            {
                bool cEra = _remotePaused;
                _remotePaused = false;
                _remoteDeadline = 0f;

                if (!cEra)
                {
                    ModLog.Info(MARK + " avviso gia' spento · motivo=" + reason + " · ricevuti=" + _received +
                                " (idempotente: non c'era niente da togliere)");
                    return;
                }

                try
                {
                    if (Main.Multiplayer != null && Main.Multiplayer.NotificationManager != null)
                        Main.Multiplayer.NotificationManager.ClearStickyNotification();
                }
                catch (Exception)
                {
                    // Se la UI non si toglie, il testo sparisce al prossimo `LevelLoaded()`: non si rilancia.
                }

                _cleared++;
                ModLog.Info(MARK + " NOTIFICA TOLTA · motivo=" + reason + " · da=" + _remoteFrom +
                            " · ricevuti=" + _received + " · spenta=" + _cleared +
                            " · la pausa locale NON e' stata toccata (la mod non ha mai messo in pausa niente)");
            }
            catch (Exception e)
            {
                Fail("Clear", e);
            }
        }

        /// <summary>
        /// LE USCITE DELL'AVVISO, e sono **tutte locali**: il flag del peer smette di essere vero quando
        ///  · **non arriva piu'** (timeout di 8 s: gioco chiuso, rete morta, build diversa — la seconda
        ///    lettura del flag e' l'unica cosa che il protocollo non distingue);
        ///  · **la connessione cade** (`IsConnected`, istantaneo);
        ///  · **torno al menu' principale** (`CurrentlyInLevel` falso);
        ///  · **non c'e' piu' nessun peer connesso**;
        ///  · **il peer non e' piu' in questa stanza, o e' a terra** (`AnyOtherPlayerHereAndAlive()`).
        /// Ogni uscita spegne la riga e **non tocca il gioco**: e' la differenza con la V2, dove ogni uscita
        /// portava con se' una chiusura di menu' e un `ResumeGame` che poteva essere rifiutato.
        /// </summary>
        private static void CheckRemoteStillPaused()
        {
            try
            {
                if (!_remotePaused)
                    return;

                if (Now() > _remoteDeadline)
                {
                    Clear("TIMEOUT di " + TIMEOUT_SECONDS + "s scaduto: nessun battito del flag da " + _remoteFrom +
                          " (gioco chiuso, rete morta o build diversa: il protocollo non distingue le build, " +
                          "quindi il timeout e' l'unica difesa)");
                    return;
                }

                if (Main.Multiplayer.NetworkManager == null || !Main.Multiplayer.NetworkManager.IsConnected)
                {
                    Clear("disconnesso dal server");
                    return;
                }

                if (!Main.Multiplayer.CurrentlyInLevel)
                {
                    Clear("io sono tornato al menu principale");
                    return;
                }

                if (EnemySyncManager.ConnectedPlayersCount() <= 0)
                {
                    Clear("nessun peer piu' connesso");
                    return;
                }

                if (!EnemySyncManager.AnyOtherPlayerHereAndAlive())
                {
                    Clear("il peer non e' piu' in questa stanza o e' a terra");
                }
            }
            catch (Exception e)
            {
                Fail("CheckRemoteStillPaused", e);
            }
        }

        // ============================================================================================
        // LE LETTURE. Tutte con `try/catch`: sono il punto in cui un avviso potrebbe rompere la partita,
        // e ogni predicato e' PUBBLICO e usato dal gioco per se' stesso.
        // ============================================================================================

        /// <summary>Lo stato del gioco locale, senza mai lanciare. ⚠️ Si **legge** e basta: non si scrive.</summary>
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
                             " (l'avviso di pausa non ha interrotto niente: la partita e' proseguita)");
            }
            catch (Exception)
            {
                // Se anche il log fallisce non c'e' un secondo posto dove dirlo.
            }
        }
    }
}
