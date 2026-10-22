using Blasphemous.ModdingAPI;
using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// ⭐⭐ IL P2P VERO — l'unica classe (con `P2PServerTransportImpl`) che tocca `SteamNetworking`.
    ///
    /// PERCHE' UNA CLASSE `*Impl` SORELLA E NON UN PEZZO DI `SteamP2PTransport`: la regola della mod e'
    /// che **solo le `*Impl` nominano `Steamworks`** (vedi il commento in testa a `SteamLobby.cs`). Su
    /// una copia non-Steam i tipi possono mancare, e la `TypeLoadException` arriva **al punto di
    /// chiamata**: la facciata la cattura perche' chiama da metodi suoi, dentro un `try/catch`, dopo il
    /// cancello `SteamLobby.Available`.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────────────────
    /// LA SEQUENZA DI APERTURA (il "③" del mandato: **prima l'hello, poi lo stato**)
    ///
    ///   PEER                                     HOST (adapter)
    ///   Begin   lobby = CurrentLobby (l'invito)
    ///   Tick    host  = GetLobbyOwner(lobby)
    ///           ── HELLO (reliable, canale 1) ──▶  riceve, annuncia `PeerConnected`
    ///                                             il server risponde Intro/HostAssign/replay
    ///           ◀── DATA (Intro(0), HostAssign, replay) ──
    ///           Intro di gioco (reliable) ───────▶  il server accetta il giocatore
    ///
    /// ⚠️ L'HELLO E' UN PACCHETTO **DEL TRASPORTO**, non del protocollo: `P2PWire.KindHello`, 3 byte di
    /// prefisso + 8 di SteamID. Non entra mai nel server (l'adapter lo consuma e basta), quindi il
    /// protocollo resta 11 e il server non sa nemmeno che esiste.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────────────────
    /// IL CANALE 0 E LA SEQUENZA (§10.4)
    ///
    /// `k_EP2PSendUnreliableNoDelay` non ordina: senza un contatore vince l'**ultimo arrivato**, non
    /// l'ultimo **campionato**, e un pacchetto scavalcato trascina indietro l'avatar. Quindi ogni
    /// pacchetto del canale 0 antepone `[origin 8][seq 4]`: il ricevente tiene l'ultima sequenza
    /// accettata **per mittente** e scarta le piu' vecchie. L'header e' del **trasporto**: il protocollo
    /// non lo vede, e chi riceve lo toglie prima di consegnare i frame a `NetworkManager`.
    ///
    /// ⚠️ E L'HOST CHE INOLTRA CONSERVA L'HEADER (vedi `P2PServerTransportImpl`): se l'adapter mettesse
    /// il **proprio** id, tutti i giocatori collasserebbero in un flusso solo e la sequenza di A
    /// scarterebbe i pacchetti di B.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────────────────
    /// ⭐ IL BATTITO E I DUE TIMEOUT (⭐⭐ questo e' il fix del "si disconnette dopo 5 secondi")
    ///
    /// 🔴 IL DIFETTO, misurato: c'era **una sola** soglia, 5 s, e valeva **anche per la prima
    /// negoziazione**. Ma la prima sessione P2P legacy di Steam richiede **3-7 s**, con picchi
    /// **10-15 s** quando il punch-through non passa e serve il **relay**. ⇒ la mod **abortiva** mentre
    /// Steam stava ancora aprendo il tunnel, e il sintomo era esattamente "si disconnette dopo 5
    /// secondi". Peggio: l'hello **non si perde** — Steam lo tiene in un buffer di transizione
    /// (~15-20 s) e lo consegna all'`Accept` — quindi abortire a 5 s **distruggeva** proprio il
    /// meccanismo che stava per funzionare.
    ///
    /// ✅ IL FIX, con **uno stato esplicito** (`Phase`): `Connecting` → `Connected` → `Disconnected`.
    ///
    ///   · `Connecting`  — l'hello e' partito (o sta partendo) e **nessun pacchetto e' ancora arrivato**.
    ///     La soglia e' `HANDSHAKE_TIMEOUT_SECONDS` = **25 s** misurata da `Begin` (il momento in cui la
    ///     mod arma il tentativo; l'hello parte nel primo frame). Copre il relay peggiore (10-15 s) con
    ///     un margine che non si mangia l'attesa del giocatore. Se scade senza **mai** aver ricevuto un
    ///     pacchetto: `[Net] p2p: handshake scaduto (25 s)` + `Disconnected`.
    ///
    ///   · `Connected`   — il passaggio avviene sul **primo pacchetto valido ricevuto**, ed e' **loggato**:
    ///     `[Net] p2p: primo pacchetto (canale N) dopo Xs` e subito dopo
    ///     `[Net] p2p: connesso (handshake Xs)`. ⭐ E' **il** marker che dice se il tunnel si e' aperto —
    ///     e il valore di X dice **quanto** ci mette la rete vera. Da questo istante (e **solo** da
    ///     questo) vale la soglia del battito: `KEEPALIVE_TIMEOUT_SECONDS` = **5 s** senza nessun
    ///     pacchetto valido ⇒ link perso.
    ///
    /// ⚠️ E' UNA DISTINZIONE, NON UN NUMERO PIU' GRANDE: il 5 s non e' stato "allargato" (un link che si
    /// e' aperto e tace per 5 s e' davvero morto) — e' stato **spostato dove ha senso**, cioe' dopo il
    /// primo pacchetto. Un timeout unico a 25 s avrebbe reso la caduta di un link reale 5 volte piu'
    /// lenta da vedere.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────────────────
    /// ③ LA RICONNESSIONE (⭐ questo e' il fix del "dopo una disconnessione non torna e non avvisa")
    ///
    /// 🔴 IL DIFETTO, misurato: `_linkLost = true` (e `_opened`/`_helloed` **ancora** `true`) faceva da
    /// muro in testa a `Tick`. Da li' in poi non si svuotavano i canali, non partiva l'hello, non si
    /// mandava il battito — e i **due controlli di fase stavano sotto quel return**, quindi non scattava
    /// piu' nessun timeout: silenzio totale. E l'`Accept` di Steam **non** riportava in vita niente:
    /// `OnSessionRequest` chiamava **solo** `AcceptP2PSessionWithUser`, quindi la sessione tornava a
    /// esistere e il trasporto restava morto. La firma nei log era: `peer ha accettato la sessione di
    /// ritorno dall host=…` **senza** `hello inviato` — la sessione si accetta, il trasporto e' gia' morto.
    ///
    /// ✅ IL FIX, in due parti:
    ///   · ① `ReviveLink` — su una `P2PSessionRequest_t` **accettata e dal nostro host**, il link torna
    ///     vivo con un reset **mirato** (solo lo stato del link: `_linkLost = false`, hello da rimandare,
    ///     battito riavviato, fase di nuovo `Connecting`). **Mai `End()`**, che chiuderebbe la sessione
    ///     appena accettata e azzererebbe anche `_host`/`_lobby`.
    ///   · ② `CheckPhase` — i controlli di fase stanno **prima di ogni return** di `Tick`: una scadenza
    ///     non puo' piu' essere saltata da un ritorno anticipato, quindi un fallimento **avvisa**
    ///     (marker + notifica) invece di sparire.
    ///
    /// ⭐ LA CATENA CHE L'UTENTE DEVE VEDERE, dopo una disconnessione e una riconnessione: `link
    /// RITROVATO …` → `hello inviato a …` → `primo pacchetto (canale N) dopo Xs` → `connesso (handshake
    /// Xs)`. Se non si riconnette, al posto degli ultimi due: `handshake scaduto (25 s)` + notifica.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────────────────
    /// LA SCATOLA NERA: `P2PSessionConnectFail_t`
    ///
    /// Arriva **dopo** ~10-15 s (non subito) e **non e' simmetrica**: scatta solo dal lato che ha
    /// pacchetti in coda. E' pero' **l'unica** riga che dice *perche'* Steam non lascia passare
    /// (`err=4 TIMEOUT` = nessun Accept dall'altra parte, o firewall/relay). Per questo e' registrata e
    /// loggata: senza, un fallimento del tunnel resta "non funziona".
    ///
    /// ⚠️ TUTTO IN `try/catch`: nessun metodo di questa classe lancia verso il gioco se non dove il
    /// contratto lo prevede (`Send` → `IOException`, e solo per il link dichiarato perso).
    /// </summary>
    internal static class SteamP2PTransportImpl
    {
        /// <summary>
        /// ⏱️ LA SOGLIA DELL'HANDSHAKE: **25 s** senza **mai** un pacchetto ricevuto (prima
        /// negoziazione P2P: 3-7 s tipici, 10-15 s con il relay). Vedi il commento di classe.
        /// </summary>
        private const float HANDSHAKE_TIMEOUT_SECONDS = 25f;

        /// <summary>⏱️ LA SOGLIA DEL BATTITO: un link **gia' aperto** che tace per 5 s e' perso.</summary>
        private const float KEEPALIVE_TIMEOUT_SECONDS = 5f;

        /// <summary>Il battito: uno ogni 3 s (§9: "1 byte ogni 2-3 s").</summary>
        private const float KEEPALIVE_SECONDS = 3f;

        /// <summary>Quanti pacchetti si leggono al massimo per canale per frame (nessun frame lungo).</summary>
        private const int MAX_PACKETS_PER_FRAME = 64;

        /// <summary>Il tetto del buffer di lettura (un PNG di skin ci sta; oltre si tronca e si scarta).</summary>
        private const uint MAX_PACKET_BYTES = 256u * 1024u;

        /// <summary>Quanti pacchetti in attesa dell'hello si tengono (oltre, si scarta il piu' vecchio).</summary>
        private const int MAX_OUTBOX = 512;

        /// <summary>
        /// Quanto si aspetta, dopo l'apertura, che i dati della lobby arrivino: la `LobbyDataUpdate_t` puo'
        /// arrivare un istante dopo l'ingresso. Passata la tolleranza, un `hostSteamId` che manca e' un
        /// FATTO, non un ritardo.
        /// </summary>
        private const float HOST_DATA_GRACE_SECONDS = 3f;

        private const string MarkerOpen = "[Net] p2p: ";
        private const string MarkerTimeout = "[Net] p2p timeout: ";
        private const string MarkerLinkLost = "[Net] p2p: link perso";

        /// <summary>"La sessione non e' piu' disponibile": un fatto, non un guasto tecnico.</summary>
        private const string MarkerUnavailable = "[Net] p2p: la sessione non e' piu' disponibile";

        // ─── STATO ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ UNO STATO SOLO, ESPLICITO: `Connecting` → `Connected` → `Disconnected`.
        ///
        /// ⚠️ PERCHE' UN ENUM E NON DUE BOOLEANI: il difetto era che **una** soglia di 5 s valeva in
        /// **due** fasi diverse ("sto negoziando" e "sono connesso"). Con lo stato, la domanda "quale
        /// timeout vale adesso?" ha una risposta sola, e il passaggio all'altra fase e' un **evento**
        /// (il primo pacchetto) invece di un'inferenza.
        /// </summary>
        private enum Phase
        {
            /// <summary>Nessun tentativo in corso (o fallito): `Send` non deve partire.</summary>
            Disconnected,

            /// <summary>Hello partito (o in partenza), nessun pacchetto ricevuto: vale il timeout di 25 s.</summary>
            Connecting,

            /// <summary>Almeno un pacchetto valido ricevuto: vale il timeout del battito, 5 s.</summary>
            Connected,
        }

        private static Phase _phase = Phase.Disconnected;
        private static bool _opened;
        private static bool _helloed;
        private static bool _linkLost;

        /// <summary>
        /// "QUESTO LINK E' GIA' STATO DATO PER PERSO E POI RIANIMATO" (`ReviveLink`). Serve **solo** a
        /// decidere chi da' la notifica del fallimento successivo: dopo una rianimazione il percorso
        /// normale tace (vedi `NotifyIfOutOfSession`), quindi la deve dare il trasporto. Si azzera in
        /// `End()`, cioe' a ogni chiusura voluta e all'inizio di ogni `Begin`.
        /// </summary>
        private static bool _revived;
        private static CSteamID _lobby = CSteamID.Nil;
        private static CSteamID _host = CSteamID.Nil;
        private static ulong _myId;
        private static uint _txSeq;
        private static float _lastRx;
        private static float _nextKeepAlive;
        private static bool _registered;
        private static bool _registerFailed;
        private static byte[] _rxBuffer = new byte[1024];
        private static float _openedAt;

        private static readonly Dictionary<ulong, uint> _lastSeqByOrigin = new Dictionary<ulong, uint>();
        private static readonly List<Pending> _outbox = new List<Pending>();
        private static readonly int[] _foreign = new int[3];
        private static readonly int[] _foreignLogged = new int[3];
        private static readonly HashSet<ulong> _peerLogged = new HashSet<ulong>();

        /// <summary>I peer per cui e' gia' stata detta la riga "sessione di ritorno accettata".</summary>
        private static readonly HashSet<ulong> _returnLogged = new HashSet<ulong>();

        /// <summary>Quanti pacchetti si sono gia' tracciati per canale (vedi `RX_LOG_FIRST`).</summary>
        private static readonly int[] _rxLogged = new int[3];

        /// <summary>
        /// Quante righe `rx canale=... da=...` per canale. **Tre**, non per frame: la traccia serve a
        /// rispondere a "i pacchetti arrivano, e su quale canale?", non a contare il traffico (per quello
        /// c'e' la sonda, che stampa ogni 3 s).
        /// </summary>
        private const int RX_LOG_FIRST = 3;
        private static int _droppedStale;
        private static int _droppedStaleLogged;
        private static int _sendFailLogged;
        private static int _outboxDropped;
        private static bool _timeoutLogged;

        private static Action<byte[]> _onData;
        private static Action _onConnected;
        private static Action _onDisconnected;

        /// <summary>Il filo e' aperto: hello partito e link non scaduto.</summary>
        internal static bool Open
        {
            get { return _opened && _helloed && !_linkLost; }
        }

        /// <summary>Il sink della facciata: eventi consegnati sul thread di `Tick` (il main thread).</summary>
        internal static void SetSink(Action<byte[]> onData, Action onConnected, Action onDisconnected)
        {
            _onData = onData;
            _onConnected = onConnected;
            _onDisconnected = onDisconnected;
        }

        /// <summary>
        /// "SI APRE ADESSO?" — l'unico requisito e' **essere in una lobby Steam**: e' la lobby
        /// dell'invito, e il proprietario e' l'host. Nessuna lobby = `false` (la facciata lo traduce in
        /// `SocketException`, cioe' "connessione rifiutata").
        ///
        /// ⚠️ NON SI GUARDA IL TCP: il punto del P2P e' funzionare dove il TCP non arriva (le due
        /// macchine su reti diverse, senza port forwarding).
        /// </summary>
        internal static bool Begin(string host, int port)
        {
            try
            {
                End();

                if (!SteamAPI.IsSteamRunning() || !SteamUser.BLoggedOn())
                {
                    ModLog.Warn(MarkerOpen + "Steam non sta girando o nessun account loggato: nessun P2P");
                    return false;
                }

                CSteamID lobby = SteamLobbyImpl.CurrentLobby;
                if (lobby.m_SteamID == 0UL)
                {
                    ModLog.Warn(MarkerOpen + "nessuna lobby Steam. Il P2P non ha un host da raggiungere: " +
                                "accetta un invito (o apri tu una lobby) e riprova");
                    return false;
                }

                if (!EnsureRegistered())
                {
                    ModLog.Warn(MarkerOpen + "callback P2PSessionRequest_t non registrata: nessun P2P");
                    return false;
                }

                _lobby = lobby;
                _host = CSteamID.Nil;
                _myId = SteamUser.GetSteamID().m_SteamID;
                _opened = true;
                _helloed = false;
                _linkLost = false;

                // ⭐ ENTRA NELLA FASE DI NEGOZIAZIONE: da qui e fino al **primo pacchetto ricevuto** vale
                // `HANDSHAKE_TIMEOUT_SECONDS` (25 s), non i 5 s del battito. Vedi `Tick`.
                _phase = Phase.Connecting;

                _txSeq = 0u;
                _lastRx = Time.realtimeSinceStartup;
                _openedAt = _lastRx;
                _nextKeepAlive = _lastRx;
                _timeoutLogged = false;
                _sendFailLogged = 0;
                _outboxDropped = 0;
                _droppedStale = 0;
                _droppedStaleLogged = 0;
                _outbox.Clear();
                _lastSeqByOrigin.Clear();
                _peerLogged.Clear();
                _returnLogged.Clear();
                for (int i = 0; i < 3; i++)
                {
                    _foreign[i] = 0;
                    _foreignLogged[i] = 0;
                    _rxLogged[i] = 0;
                }

                // ⚠️ DUE NUMERI DIVERSI, E VANNO DETTI ENTRAMBI: `hostSteamId` e' il DATO che l'host ha
                // scritto (ed e' quello che si usa), `proprietario` e' il RUOLO di Steam (che cambia se
                // l'host esce). Se divergono, questo e' il posto in cui si vede.
                ModLog.Info(MarkerOpen + "in attesa: lobby=" + lobby.m_SteamID + " io=" + _myId +
                            " hostDichiarato=" + DeclaredHost() +
                            " proprietario=" + SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID +
                            " (host nel campo Server IP='" + SteamP2PTransport.RequestToken + "')");
                return true;
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + "errore in apertura: " + e.GetType().Name + ": " + e.Message);
                _opened = false;
                return false;
            }
        }

        /// <summary>
        /// Chiude: un BYE (reliable, best effort: cosi' l'host lo sa **subito** invece di aspettare il
        /// timeout), poi `CloseP2PSessionWithUser` — una sessione lasciata aperta e' uno stato che
        /// nessuno vede e che resta fino alla chiusura del gioco. Idempotente, non lancia.
        /// </summary>
        internal static void End()
        {
            try
            {
                if (_helloed && !_linkLost && _host.m_SteamID != 0UL)
                    SendControl(P2PWire.KindBye);
            }
            catch (Exception)
            {
                // Un addio perso non deve impedire la chiusura.
            }

            try
            {
                if (_host.m_SteamID != 0UL)
                    SteamNetworking.CloseP2PSessionWithUser(_host);
            }
            catch (Exception)
            {
                // Idem.
            }

            _opened = false;
            _helloed = false;
            _linkLost = false;
            _revived = false;
            _phase = Phase.Disconnected;
            _host = CSteamID.Nil;
            _lobby = CSteamID.Nil;
            _outbox.Clear();
            _lastSeqByOrigin.Clear();
        }

        /// <summary>
        /// ⭐⭐ IL CICLO PER FRAME: aprire (hello), ricevere i tre canali, controllare **il timeout della
        /// fase in cui siamo**, mandare il battito, svuotare la coda che aspettava l'hello.
        ///
        /// ⚠️ L'ORDINE NON E' UN CAPRICCIO: prima si **riceve** (il buffer va svuotato in ogni frame fin
        /// dal `Begin`, altrimenti si satura e blocca l'avanzamento — vedi il commento dentro `Tick`),
        /// poi si apre (l'hello deve partire prima dei frame di gioco, vedi ③), poi si controlla il
        /// timeout della fase (dopo la ricezione: un pacchetto arrivato in questo frame deve poter dire
        /// "sono vivo"), poi si manda il battito.
        ///
        /// ⭐⭐ E IL TIMEOUT CHE SI CONTROLLA DIPENDE DALLO STATO — questa e' la correzione:
        ///   · `Connecting` ⇒ **25 s** dal `Begin`. Scaduti **senza aver mai ricevuto un pacchetto**,
        ///     l'handshake e' fallito: `[Net] p2p: handshake scaduto (25 s)` + `Disconnected`. Prima di
        ///     questo fix qui c'erano i 5 s del battito, cioe' la mod abbandonava mentre Steam stava
        ///     ancora aprendo il tunnel (3-7 s tipici, 10-15 s col relay).
        ///   · `Connected`  ⇒ **5 s** dall'ultimo pacchetto valido. Qui i 5 s sono **giusti**: il link
        ///     e' aperto, il peer manda un battito ogni 3 s, e 5 s di silenzio sono un fatto.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                // ⚠️ IL CANCELLO D'INGRESSO E' **SOLO** `_opened` = "c'e' un tentativo armato nella mod".
                // Lo azzera `End()` (una chiusura VOLUTA non si rianima) e nient'altro lo tocca.
                //
                // 🔴 PRIMA QUI C'ERA `if (!_opened || _linkLost) return;` E `_linkLost` ERA UN MURO:
                // da quel `return` in poi non si svuotavano piu' i canali, non partiva piu' l'hello, non
                // si mandava piu' il battito — e i due controlli di fase stavano **sotto** quel return,
                // quindi non scattava piu' **nessun** timeout: dopo una disconnessione il log diventava
                // muto e la schermata non sapeva piu' niente. E' il difetto ③ (vedi `CheckPhase` per i
                // controlli, ora **prima** del cancello, e `ReviveLink` per l'accept che riporta in vita).
                if (!_opened)
                    return;

                // ⭐⭐ SI LEGGE **PRIMA** DI TUTTO, e **non** solo da "connesso": il ciclo di lettura parte
                // dal `Begin`, in ogni frame, qualunque sia lo stato. Se si aspettasse di essere
                // `Connected` per iniziare a leggere, Steam spingerebbe i pacchetti in un buffer che
                // nessuno svuota, il buffer si saturerebbe e il passaggio a `Connected` — che dipende dal
                // **leggere** il primo pacchetto — non arriverebbe **mai**: un deadlock. (E il buffer
                // consegna retroattivamente: un pacchetto letto 3 s dopo l'`Accept` e' ancora li'.)
                //
                // ⚠️ E' anche il motivo per cui questa riga sta **prima** dell'hello: fra il `Begin` e
                // l'hello c'e' l'attesa dei dati della lobby (fino a 3 s), e in quella finestra il buffer
                // va svuotato lo stesso.
                //
                // ⭐ E SI LEGGE ANCHE A **LINK PERSO**: il buffer di Steam va svuotato comunque (un
                // residuo della sessione morta lo riempirebbe fino a bloccare l'avanzamento della
                // sessione **successiva**), ma quei pacchetti **non si consegnano** — sono avanzi di un
                // tunnel chiuso (vedi il primo controllo in `Handle`).
                Drain();

                if (!_opened)
                    return;

                float now = Time.realtimeSinceStartup;

                // ⭐⭐ ② I CONTROLLI DI FASE **PRIMA** DI OGNI ALTRO RETURN — e questa posizione E' il fix.
                //
                // Un timeout che non si controlla non avvisa, e un fallimento che non avvisa non si
                // distingue da "non e' successo niente". Prima di questo spostamento c'erano **tre** vie
                // per saltarli: il cancello su `_linkLost` in testa, il `return` quando l'hello non parte
                // (sotto), e il `return` per `!_opened` in mezzo. Da qui in poi la domanda "e' scaduta la
                // fase in cui sono?" si fa **sempre**, qualunque sia lo stato del link.
                if (CheckPhase(now))
                    return;

                if (_linkLost)
                    return;

                if (!_helloed && !TrySendHello())
                    return;

                if (!_opened || _linkLost)
                    return;

                // ⭐ IL BATTITO SI MANDA IN **ENTRAMBE** LE FASI, di proposito: durante l'handshake tiene
                // viva la sessione (e il NAT/relay aperto) e provoca una risposta dall'host.
                if (now >= _nextKeepAlive)
                {
                    _nextKeepAlive = now + KEEPALIVE_SECONDS;
                    SendControl(P2PWire.KindKeepAlive);
                }

                FlushOutbox();
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + "errore in Tick: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// ⭐⭐ ② IL SORVEGLIANTE DELLA FASE — **dove** sta e' il fix, non cosa fa.
        ///
        /// Risponde a **una** domanda: "la fase in cui sono ha superato la sua scadenza?". Le due fasi
        /// hanno due soglie diverse e nessuna terza possibilita' (vedi il commento di classe):
        ///
        ///   · `Connecting` ⇒ **25 s** da `_openedAt` senza **mai** un pacchetto ⇒ `HandshakeExpired`;
        ///   · `Connected`  ⇒ **5 s** dall'ultimo pacchetto valido ⇒ `LinkLost`.
        ///
        /// ⚠️ `Disconnected` NON si sorveglia, e non e' una dimenticanza: e' la fase in cui il fatto e'
        /// **gia' stato detto** (marker + evento + notifica, una volta sola). Ripeterlo sarebbe un
        /// secondo avviso per lo stesso fatto — cioe' il difetto opposto.
        ///
        /// ⚠️ E' **idempotente per frame**: dopo una dichiarazione la fase diventa `Disconnected` (lo
        /// fanno `HandshakeExpired` e `LinkLost`), quindi al frame dopo questa funzione ritorna `false` e
        /// il ciclo prosegue. Non c'e' nessun contatore da azzerare a mano.
        /// </summary>
        private static bool CheckPhase(float now)
        {
            if (_phase == Phase.Connecting)
            {
                float waited = now - _openedAt;

                if (waited < HANDSHAKE_TIMEOUT_SECONDS)
                    return false;

                // ⚠️ `_phase` e' ancora `Connecting` SOLO se nessun pacchetto valido e' mai arrivato:
                // il primo pacchetto porta a `Connected` dentro `Handle` (vedi `MarkConnected`). E
                // `_openedAt` e' il momento in cui il tentativo e' stato **armato**: `Begin` per una
                // connessione nuova, `ReviveLink` per una **riconnessione** — cosi' i 25 s si contano
                // dalla rianimazione, non dal `Begin` di mezz'ora prima.
                HandshakeExpired(waited);
                return true;
            }

            if (_phase == Phase.Connected)
            {
                float silent = now - _lastRx;

                if (silent < KEEPALIVE_TIMEOUT_SECONDS)
                    return false;

                LinkLost("nessun pacchetto da " + (int)silent + " s (soglia " +
                         (int)KEEPALIVE_TIMEOUT_SECONDS + " s)");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 🔴 L'HANDSHAKE E' SCADUTO: **25 s** e nessun pacchetto e' mai arrivato. Si dice con la sua
        /// riga (e' il marker che il referto manda a cercare), si passa a `Disconnected` e si alza
        /// l'evento: da li' in poi `Send` lancia `IOException`, che e' la via con cui
        /// `NetworkManager.SendQueue` si disconnette.
        ///
        /// ⚠️ NON e' `LinkLost("nessun pacchetto...")`: quella riga parla di un **link aperto** che
        /// tace, e qui il link **non si e' mai aperto**. Due fatti diversi, due righe diverse — e il
        /// ragazzo che legge il log deve poter capire quale dei due e'.
        /// </summary>
        private static void HandshakeExpired(float waited)
        {
            _phase = Phase.Disconnected;

            // ⚠️ E IL LINK VA DICHIARATO PERSO, non solo lo stato: `_linkLost` e' cio' che ferma `Tick`
            // (senza, il ciclo continuerebbe a mandare il battito a una sessione che non esiste) e cio'
            // che fa lanciare `IOException` al `Send` successivo — la via che `NetworkManager.SendQueue`
            // usa gia' per disconnettersi.
            _linkLost = true;

            // 🔴 E LA SESSIONE P2P VA CHIUSA, o resta aperta fino al `Begin` successivo o alla morte del
            // processo: qui il link non si e' mai aperto, quindi nessuno la chiudera' piu'.
            //
            // ⚠️ NON si chiama `End()`: azzera `_linkLost` (`:315`) e romperebbe il contratto "`Send`
            // lancia `IOException`", cioe' la via con cui la UI si sblocca. Si chiude e basta.
            if (_host.m_SteamID != 0UL) { try { SteamNetworking.CloseP2PSessionWithUser(_host); } catch { } }

            if (!_timeoutLogged)
            {
                _timeoutLogged = true;

                ModLog.Error(SteamP2PTransport.MarkerHandshakeTimeout +
                             (int)HANDSHAKE_TIMEOUT_SECONDS + " s): nessun pacchetto ricevuto dall'host=" +
                             _host.m_SteamID + " (attesa " + (int)waited + " s, hello=" +
                             (_helloed ? "partito" : "MAI PARTITO") + "): la negoziazione P2P non si e' " +
                             "aperta (relay lento, firewall, o host che non risponde)");
            }

            // ⭐ ② LA NOTIFICA, e **solo quando serve**: se il gioco e' gia' fuori sessione (il caso
            // della **riconnessione** fallita) il percorso normale non la da', perche' `OnTransportEnded`
            // esce subito a stato gia' "sconnesso" — e il giocatore vedrebbe solo la schermata ferma.
            // Cosi' invece "non si e' riconnesso" **si vede**, oltre che leggersi nel log.
            NotifyIfOutOfSession("P2P: riconnessione non riuscita (l'host non risponde)");

            RaiseDisconnected();

            // ⭐ E SI DICE COME SI RIENTRA (stessa riga di `LinkLost`): senza, dopo lo scaduto il log si
            // ferma e non si distingue "sto aspettando" da "ho rinunciato".
            ModLog.Info(MarkerOpen + "link non aperto: si rientra con un Accept dell'host " +
                        "(`ReviveLink`) o con un Connect nuovo (`Begin`)");
        }

        /// <summary>
        /// Alza l'evento di fine. **Una via sola** per i tre modi in cui il link finisce (handshake
        /// scaduto, battito perso, sessione non disponibile): se un giorno cambia il modo in cui si
        /// avvisa, cambia in un punto. Il `try/catch` c'e' perche' qui siamo dentro `Tick`, cioe' nel
        /// late update della mod: un gestore che lancia fermerebbe il resto del ciclo.
        /// </summary>
        private static void RaiseDisconnected()
        {
            try
            {
                Action handler = _onDisconnected;
                if (handler != null)
                    handler();
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + "errore in Disconnected: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>La riga di stato della lobby (per il log e per il referto).</summary>
        internal static string LobbySummary()
        {
            try
            {
                CSteamID lobby = _lobby.m_SteamID != 0UL ? _lobby : SteamLobbyImpl.CurrentLobby;
                if (lobby.m_SteamID == 0UL)
                    return "nessuna lobby";

                return "lobby=" + lobby.m_SteamID +
                       " hostDichiarato=" + DeclaredHost() +
                       " proprietario=" + SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID +
                       " hello=" + (_helloed ? "si" : "no") + " link=" + (_linkLost ? "perso" : "ok");
            }
            catch (Exception)
            {
                return "illeggibile";
            }
        }

        /// <summary>
        /// ⭐ L'INVIO. `reliable` viene dalla classe del messaggio (secondo §10, scelta dove il payload
        /// nasce). Prima dell'hello i pacchetti si **accodano** (l'`Intro` di gioco parte nello stesso
        /// stack della `Connect`): senza coda la sessione non comincerebbe mai.
        ///
        /// ⚠️ Se il link e' perso lancia `System.IO.IOException`: e' la via di
        /// `NetworkManager.SendQueue` → `OnDisconnect()`, la stessa del socket caduto. Non e' un guasto
        /// nuovo: e' lo stesso contratto del seam.
        /// </summary>
        internal static void Send(byte[] framed, bool reliable)
        {
            if (framed == null || framed.Length == 0)
                return;

            if (_linkLost)
                throw new System.IO.IOException("[Net] p2p: link perso");

            if (!_helloed)
            {
                if (_outbox.Count >= MAX_OUTBOX)
                {
                    _outbox.RemoveAt(0);
                    _outboxDropped++;
                    if (_outboxDropped <= 3)
                        ModLog.Warn(MarkerOpen + "coda dell'hello piena (" + MAX_OUTBOX + " pacchetti): " +
                                    "il piu' vecchio e' stato scartato (hello non ancora partito?)");
                }

                _outbox.Add(new Pending(framed, reliable));
                return;
            }

            SendFrame(framed, reliable);
        }

        // ─── APERTURA ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Manda l'hello all'host **dichiarato nella lobby**. Fino a quando non parte, nessun frame di
        /// gioco parte (restano in coda): e' l'ordine ③ del mandato.
        ///
        /// 🔴 L'HOST NON E' `GetLobbyOwner`. Quando il proprietario esce, Steam **passa la proprieta' al
        /// membro successivo**: dopo quell'istante `GetLobbyOwner` risponde un ALTRO giocatore, e un hello
        /// mandato a lui non trova nessun server. Il destinatario giusto e' il dato che l'host ha scritto
        /// alla creazione (`SteamLobbyImpl.KEY_STEAM` = `hostSteamId`), che non cambia se cambia la
        /// proprieta'. Se quel dato manca o non e' un numero valido, la risposta non e' un errore tecnico:
        /// e' **"la sessione non e' piu' disponibile"** — vedi `SessionUnavailable`.
        ///
        /// ⚠️ I PRIMI `HOST_DATA_GRACE_SECONDS` sono una tolleranza: i dati della lobby possono arrivare
        /// con una `LobbyDataUpdate_t` un istante dopo l'ingresso. Passata la tolleranza, il dato mancante
        /// e' un fatto e non un ritardo.
        /// </summary>
        private static bool TrySendHello()
        {
            ulong declared = DeclaredHost();

            if (declared == 0UL)
            {
                float waited = Time.realtimeSinceStartup - _openedAt;

                if (waited < HOST_DATA_GRACE_SECONDS)
                {
                    LogOnce("la lobby " + _lobby.m_SteamID + " non porta ancora " +
                            SteamLobbyImpl.KEY_STEAM + ": aspetto (e' la tolleranza di " +
                            (int)HOST_DATA_GRACE_SECONDS + " s)");
                    return false;
                }

                SessionUnavailable("l'host non ha dichiarato " + SteamLobbyImpl.KEY_STEAM + " nella lobby " +
                                   _lobby.m_SteamID + " (lobby vecchia, o host che non pubblica i dati)");
                return false;
            }

            if (declared == _myId)
            {
                // L'host di se stesso non ha senso: `SendP2PPacket(proprioSteamID)` e' loopback UDP e
                // negoziazione con se stessi (vedi `CompositeServerTransport`). L'host resta TCP.
                SessionUnavailable("il giocatore che ospita sono io (" + _myId + "): il P2P serve al " +
                                   "PEER, l'host resta sul TCP del loopback");
                return false;
            }

            // ⭐ E SE L'HOST DICHIARATO NON E' (PIU') NELLA LOBBY, LA SESSIONE NON C'E'. E' il caso che
            // `GetLobbyOwner` avrebbe MASCHERATO: l'host esce, la proprieta' passa a un altro, e un
            // "hello" al nuovo proprietario non troverebbe nessun server. Qui invece il fatto si dice.
            if (!SteamLobbyImpl.IsLobbyMember(new CSteamID(declared)))
            {
                SessionUnavailable("l'host dichiarato (" + declared + ") non e' piu' nella lobby " +
                                   _lobby.m_SteamID);
                return false;
            }

            _host = new CSteamID(declared);

            if (!SendControl(P2PWire.KindHello))
            {
                LogOnce("hello non partito verso " + declared + " (si riprova al frame dopo)");
                return false;
            }

            _helloed = true;
            _lastRx = Time.realtimeSinceStartup;
            _nextKeepAlive = _lastRx + KEEPALIVE_SECONDS;

            // ⭐ MARKER ① — **DOPO** l'invio, non prima (cosi' dice che il pacchetto e' **partito**, non
            // che si e' provato a mandarlo) e con la soglia dell'handshake dichiarata: chi legge il log
            // sa **quanto** si e' disposti ad aspettare, e non deve indovinarlo dal codice.
            ModLog.Info(SteamP2PTransport.MarkerHelloSent + _host.m_SteamID + " (timeout handshake " +
                        (int)HANDSHAKE_TIMEOUT_SECONDS + "s) - host DICHIARATO nella lobby=" +
                        _lobby.m_SteamID + " (proprietario attuale=" +
                        SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID + ") canale=" +
                        P2PWire.ChannelReliable);

            FlushOutbox();

            // ⚠️ E QUI **NON** SI ALZA `Connected`: il tunnel **non e' aperto**, e' solo stato chiesto.
            // L'evento parte dal primo pacchetto ricevuto (`MarkConnected`), che e' l'unico fatto che
            // dica "l'altra parte mi sente". Prima di questo fix l'evento partiva qui, e la schermata
            // diceva "connesso" mentre nessun byte era ancora arrivato.
            return true;
        }

        /// <summary>
        /// ⭐ L'HOST DICHIARATO NEI DATI DELLA LOBBY — `0` se il dato non c'e' o non e' un id valido.
        ///
        /// ⚠️ `GetLobbyOwner` NON si usa qui, ed e' il punto della correzione: la proprieta' della lobby
        /// **passa al membro successivo** quando l'host esce, quindi non e' un modo di sapere chi ospita.
        /// </summary>
        private static ulong DeclaredHost()
        {
            try
            {
                string text = SteamMatchmaking.GetLobbyData(_lobby, SteamLobbyImpl.KEY_STEAM);
                if (string.IsNullOrEmpty(text))
                    return 0UL;

                ulong id;
                if (!ulong.TryParse(text.Trim(), out id))
                    return 0UL;

                return id == 0UL ? 0UL : id;
            }
            catch (Exception)
            {
                // Un dato illeggibile vale "non dichiarato": la risposta prudente e' "non disponibile".
                return 0UL;
            }
        }

        /// <summary>
        /// "LA SESSIONE NON E' PIU' DISPONIBILE" — e non un errore tecnico: e' un fatto che il giocatore
        /// capisce ("l'host non c'e' piu' / questa lobby non ha un host"), non un guasto da leggere nel log.
        /// Si dice **una volta**, si alza `Disconnected` e basta: nessun indovinello su chi chiamare.
        /// </summary>
        private static void SessionUnavailable(string why)
        {
            if (_linkLost)
                return;

            ModLog.Warn(MarkerUnavailable + " (" + why + ")");
            Notify("P2P: la sessione non e' piu' disponibile");

            _linkLost = true;
            _phase = Phase.Disconnected;

            RaiseDisconnected();
        }

        /// <summary>Una notifica a schermo, con il `try/catch` addosso (la UI puo' non esistere ancora).</summary>
        private static void Notify(string message)
        {
            try
            {
                if (Main.Multiplayer != null && Main.Multiplayer.NotificationManager != null)
                    Main.Multiplayer.NotificationManager.DisplayNotification(message);
            }
            catch (Exception)
            {
                // Niente: la riga di log c'e' gia', e la notifica e' un di piu'.
            }
        }

        /// <summary>Manda i pacchetti che aspettavano l'hello, nell'ordine in cui sono stati accodati.</summary>
        private static void FlushOutbox()
        {
            if (_outbox.Count == 0)
                return;

            List<Pending> copy = new List<Pending>(_outbox);
            _outbox.Clear();

            for (int i = 0; i < copy.Count; i++)
            {
                try
                {
                    SendFrame(copy[i].Framed, copy[i].Reliable);
                }
                catch (Exception e)
                {
                    ModLog.Error(MarkerOpen + "errore in coda: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // ─── INVIO ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Un frame sul canale giusto: `Reliable` -> canale 1 (o 2 se e' una skin), `Unreliable` ->
        /// canale 0 **con** l'intestazione di sequenza.
        /// </summary>
        private static void SendFrame(byte[] framed, bool reliable)
        {
            if (_host.m_SteamID == 0UL)
                return;

            if (reliable)
            {
                TransportClass cls = TransportClass.Reliable;
                byte type = framed.Length > 2 ? framed[2] : (byte)0;
                int channel = P2PWire.ChannelFor(cls, type);
                byte[] packet = P2PWire.BuildData(framed, cls, _myId, 0u);
                SendPacket(_host, packet, channel, true);
                return;
            }

            _txSeq++;
            byte[] sequenced = P2PWire.BuildData(framed, TransportClass.Unreliable, _myId, _txSeq);
            SendPacket(_host, sequenced, P2PWire.ChannelUnreliable, false);
        }

        /// <summary>
        /// Manda un pacchetto di controllo (hello/keepalive/bye) sul canale 1, affidabile.
        /// </summary>
        private static bool SendControl(byte kind)
        {
            if (_host.m_SteamID == 0UL)
                return false;

            byte[] packet = P2PWire.BuildControl(kind, _myId);
            return SendPacket(_host, packet, P2PWire.ChannelReliable, true);
        }

        /// <summary>
        /// ⚠️ `SendP2PPacket` PUO' TORNARE `false` (buffer di invio pieno) **senza eccezione e senza
        /// callback**: il valore di ritorno si controlla SEMPRE, e il primo `false` per canale finisce
        /// nel log. Un `false` buttato via e' un pacchetto che nessuno sapra' mai di aver perso.
        /// </summary>
        private static bool SendPacket(CSteamID peer, byte[] packet, int channel, bool reliable)
        {
            EP2PSend mode = reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;

            bool sent;
            try
            {
                sent = SteamNetworking.SendP2PPacket(peer, packet, (uint)packet.Length, mode, channel);
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + "errore in SendP2PPacket: " + e.GetType().Name + ": " + e.Message);
                return false;
            }

            if (!sent)
            {
                _sendFailLogged++;
                if (_sendFailLogged <= 3)
                    ModLog.Warn(MarkerOpen + "SendP2PPacket ha risposto false (canale=" + channel +
                                " byte=" + packet.Length + "): buffer di invio pieno, il pacchetto e' perso");
            }

            return sent;
        }

        // ─── RICEZIONE ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Svuota i **tre** canali. `IsP2PPacketAvailable` + `ReadP2PPacket` per ciascuno, con un tetto
        /// per canale per frame: un arretrato anomalo non deve allungare il frame.
        /// </summary>
        private static void Drain()
        {
            DrainChannel(P2PWire.ChannelUnreliable);
            DrainChannel(P2PWire.ChannelReliable);
            DrainChannel(P2PWire.ChannelHeavy);
        }

        private static void DrainChannel(int channel)
        {
            int guard = MAX_PACKETS_PER_FRAME;

            while (guard-- > 0)
            {
                uint available;
                if (!SteamNetworking.IsP2PPacketAvailable(out available, channel))
                    break;

                uint capacity = available;
                if (capacity == 0u)
                    capacity = 1u;
                if (capacity > MAX_PACKET_BYTES)
                    capacity = MAX_PACKET_BYTES;

                if ((uint)_rxBuffer.Length < capacity)
                    _rxBuffer = new byte[capacity];

                uint read;
                CSteamID remote;
                if (!SteamNetworking.ReadP2PPacket(_rxBuffer, capacity, out read, out remote, channel))
                    break;

                if (read == 0u)
                    continue;

                Handle(channel, remote, _rxBuffer, (int)read);
            }
        }

        /// <summary>
        /// Un pacchetto: si riconosce, si toglie l'intestazione e si consegna **solo i frame**.
        /// I pacchetti estranei (per esempio il byte della sonda `SteamP2PProbe`, canale 0) si contano e
        /// si scartano: non si interpretano.
        /// </summary>
        private static void Handle(int channel, CSteamID remote, byte[] packet, int length)
        {
            byte kind;
            int ignoredOffset;
            uint ignoredSeq;
            ulong controlSender;

            // ⚠️ PRIMO CONTROLLO: A LINK PERSO NON SI CONSEGNA NIENTE.
            // Da quando `Tick` svuota i canali **anche** dopo una disconnessione (vedi li': il buffer di
            // Steam va comunque drenato), qui possono arrivare gli **avanzi** della sessione morta. Il
            // gioco in quel momento non e' piu' in sessione (`NetworkManager` ha azzerato il trasporto e
            // mostra la schermata di connessione): consegnarli significherebbe mutare uno stato che
            // nessuno sta piu' seguendo. Si scartano — e la rianimazione **non** passa da qui, passa
            // dall'`Accept` (`ReviveLink`), che e' l'evento che dice "l'host e' tornato".
            if (_linkLost)
                return;

            // ⭐ LA TRACCIA DELLA RICEZIONE, e dice **canale** e **mittente**: se il tunnel e' sano
            // (`active=1` nella sonda) ma il gioco non si connette, questo e' il punto in cui si vede se i
            // pacchetti si leggono su un canale diverso da quello su cui l'altro manda. Le prime
            // `RX_LOG_FIRST` per canale: per frame sarebbero 30 righe al secondo, cioe' un log illeggibile.
            if (_rxLogged[channel] < RX_LOG_FIRST)
            {
                _rxLogged[channel]++;
                ModLog.Info(MarkerOpen + "rx canale=" + channel + " da=" + remote.m_SteamID +
                            " byte=" + length);
            }

            if (!P2PWire.TryParse(packet, length, out kind, out ignoredOffset, out controlSender, out ignoredSeq))
            {
                _foreign[channel]++;
                if (_foreignLogged[channel]++ == 0)
                    ModLog.Warn(MarkerOpen + "pacchetto estraneo sul canale " + channel + " (da " +
                                remote.m_SteamID + ", " + length + " byte): scartato (sonda P2P accesa?)");
                return;
            }

            // ⭐⭐ IL PRIMO PACCHETTO VALIDO E' IL FATTO CHE APRE IL TUNNEL: `Connecting` → `Connected`.
            // E' **qui** che si alza l'evento `Connected` (non quando l'hello parte: quello dice solo che
            // abbiamo chiesto), ed e' qui che parte il marker `connesso (handshake Ns)`.
            if (_phase == Phase.Connecting)
                MarkConnected(channel);

            // QUALUNQUE pacchetto valido dice "sono vivo": e' il battito che tiene il timeout.
            _lastRx = Time.realtimeSinceStartup;

            switch (kind)
            {
                case P2PWire.KindData:
                {
                    ulong origin;
                    uint seq;
                    int payloadOffset;

                    if (!P2PWire.TryParseData(packet, length, channel, out origin, out seq, out payloadOffset))
                        return;

                    if (channel == P2PWire.ChannelUnreliable)
                    {
                        uint last;
                        if (_lastSeqByOrigin.TryGetValue(origin, out last) && !P2PWire.IsNewer(seq, last))
                        {
                            _droppedStale++;
                            if (_droppedStaleLogged++ == 0)
                                ModLog.Info(MarkerOpen + "scarto i pacchetti VECCHI del canale 0 (per " +
                                            "mittente): primo scarto origin=" + origin + " seq=" + seq +
                                            " ultima=" + last);
                            return;
                        }

                        _lastSeqByOrigin[origin] = seq;
                    }

                    Deliver(packet, payloadOffset, length - payloadOffset);
                    return;
                }

                case P2PWire.KindKeepAlive:
                    return;

                case P2PWire.KindBye:
                    LinkLost("l'host (" + remote.m_SteamID + ") ha mandato BYE");
                    return;

                case P2PWire.KindHello:
                    // Il peer non deve ricevere hello in questo verso; lo si nota una volta e si ignora.
                    LogOnce("hello ricevuto da " + remote.m_SteamID + " (verso inatteso, ignorato)");
                    return;
            }
        }

        /// <summary>
        /// ⭐⭐ `Connecting` → `Connected`: il **primo pacchetto valido** e' l'unico fatto che dice che il
        /// tunnel si e' aperto (prima di questo istante si sa solo che abbiamo *chiesto* la sessione).
        ///
        /// Scrive **due** righe, di proposito:
        ///   · `[Net] p2p: primo pacchetto (canale N) dopo Xs` — **dove** e' arrivato (il canale sbagliato
        ///     e' un'ipotesi da escludere, non da indovinare);
        ///   · `[Net] p2p: connesso (handshake Xs)` — ⭐ **il** marker del fix: se non compare entro 25 s,
        ///     al suo posto c'e' `handshake scaduto`. E il valore di X dice **quanto** ci mette la rete
        ///     vera (3-7 s tipici, 10-15 s col relay).
        ///
        /// E da qui in poi vale la soglia del **battito** (5 s), non piu' quella dell'handshake (25 s).
        /// </summary>
        private static void MarkConnected(int channel)
        {
            float now = Time.realtimeSinceStartup;
            float handshake = now - _openedAt;
            string seconds = handshake.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            _phase = Phase.Connected;

            ModLog.Info(SteamP2PTransport.MarkerFirstPacket + channel + ") dopo " + seconds + "s");
            ModLog.Info(SteamP2PTransport.MarkerConnected + seconds + "s) - host=" + _host.m_SteamID +
                        ": da adesso il timeout e' quello del battito (" +
                        (int)KEEPALIVE_TIMEOUT_SECONDS + " s)");

            try
            {
                Action connected = _onConnected;
                if (connected != null)
                    connected();
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + "errore in Connected: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Consegna il payload (i frame, senza intestazione) al seam. Copiato: il buffer si riusa.</summary>
        private static void Deliver(byte[] packet, int offset, int count)
        {
            if (count <= 0)
                return;

            byte[] framed = new byte[count];
            Buffer.BlockCopy(packet, offset, framed, 0, count);

            Action<byte[]> handler = _onData;
            if (handler != null)
                handler(framed);
        }

        // ─── IL LINK PERSO ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Il link e' perso: si scrive il marker (una volta sola), si alza `Disconnected` e da quel
        /// momento `Send` lancia `IOException`. Lo stato resta "perso" finche' non si riapre: non si
        /// finge che sia tornato.
        /// </summary>
        private static void LinkLost(string reason)
        {
            if (_linkLost)
                return;

            _linkLost = true;
            _phase = Phase.Disconnected;

            // 🔴 E LA SESSIONE P2P VA CHIUSA: il link e' perso, `_host` resta valorizzato e senza questa
            // riga la sessione vive fino al `Begin` successivo o alla morte del processo.
            //
            // ⚠️ NON si chiama `End()`: azzera `_linkLost` e romperebbe il contratto "`Send` lancia
            // `IOException`", cioe' la via con cui la UI si sblocca.
            if (_host.m_SteamID != 0UL) { try { SteamNetworking.CloseP2PSessionWithUser(_host); } catch { } }

            if (!_timeoutLogged)
            {
                _timeoutLogged = true;

                if (reason != null && reason.StartsWith("nessun pacchetto", StringComparison.Ordinal))
                    ModLog.Error(MarkerTimeout + reason + " (l'host non manda piu' niente: sessione persa)");
                else
                    ModLog.Error(MarkerLinkLost + ": " + reason);
            }

            // ⭐ ② LA SECONDA VOLTA (dopo una riconnessione) LA NOTIFICA LA DA' QUESTO, non
            // `NetworkManager`: quello esce subito a stato gia' "sconnesso" (`OnTransportEnded`). Vedi
            // `NotifyIfOutOfSession`. La **prima** volta non fa niente, perche' il gioco e' ancora
            // "connesso" e la notifica arriva dalla via normale (una sola, non due).
            NotifyIfOutOfSession("P2P: link perso di nuovo (l'host non manda piu' niente)");

            RaiseDisconnected();

            // ⭐ E SI DICE **COSA SUCCEDE ADESSO**: senza questa riga, chi legge il log dopo il timeout
            // trova il silenzio e non sa se la mod sta aspettando, se ha rinunciato, o se e' viva. Il
            // link resta dichiarato perso (non si finge che sia tornato, vedi sopra) e si riapre in
            // **due** modi soltanto, entrambi scritti qui.
            ModLog.Info(MarkerOpen + "in attesa di una nuova sessione P2P: la riapre un Accept dell'host " +
                        "(`ReviveLink`) o un Connect nuovo (`Begin`). Fino ad allora nessun pacchetto " +
                        "parte e nessuno arriva");
        }

        /// <summary>
        /// ⭐⭐ ① L'ACCEPT **RIPORTA IL TRASPORTO IN VITA** — il difetto era che l'accettazione era **solo
        /// Steam** (`AcceptP2PSessionWithUser`, in `OnSessionRequest`): la sessione tornava a esistere,
        /// ma `_linkLost` restava `true`, `Tick` esce in testa e da li' non partiva piu' **niente** — ne'
        /// l'hello, ne' il battito, ne' un timeout. La firma nei log era esattamente questa:
        /// `peer ha accettato la sessione di ritorno` e poi il vuoto, senza `hello inviato`.
        ///
        /// ✅ Qui il reset e' **MIRATO**: si tocca **solo lo stato del link**. Le tre cose da rimettere in
        /// riga, e la ragione di ciascuna:
        ///   · `_linkLost = false` — il muro che ferma `Tick` (e che fa lanciare `IOException` a `Send`);
        ///   · `_helloed = false` — ⭐ l'hello si **rimanda**: la sessione e' nuova, e l'host deve
        ///     ri-annunciare questo peer. E' anche il marker che l'utente cerca (`hello inviato a …`);
        ///   · `_phase = Connecting` + `_openedAt = adesso` — si rientra in **negoziazione**, quindi vale
        ///     il timeout dell'handshake (25 s) contato da **questo** istante, non i 5 s del battito e non
        ///     il `Begin` di mezz'ora prima. Cosi' se la riconnessione non riesce lo dice
        ///     `handshake scaduto (25 s)` — con la notifica — invece di sparire.
        ///
        /// ⛔ **NON SI CHIAMA `End()` E NON SI TOCCA**: `_host`, `_lobby`, `_myId`, `_txSeq`, `_outbox`.
        ///   · `End()` chiuderebbe la sessione che **Steam ci ha appena riaccettato** e azzererebbe anche
        ///     `_host`/`_lobby`/`_linkLost`, cioe' proprio lo stato che qui serve: e' l'opposto di un
        ///     reset mirato (ed e' la ragione per cui il mandato dice di non usarlo);
        ///   · `_txSeq` **non** si azzera: il ricevente tiene l'ultima sequenza accettata **per mittente**
        ///     e la scarta se non e' piu' nuova (`P2PWire.IsNewer`). Ripartire da 0 farebbe scartare
        ///     *tutti* i pacchetti del canale 0 come "vecchi" — un blocco silenzioso, il difetto opposto;
        ///   · `_outbox` **non** si svuota: sono i frame di gioco che aspettavano l'hello, e adesso
        ///     l'hello c'e'.
        ///
        /// ⚠️ L'UNICA memoria che si butta e' quella del **ricevente** (`_lastSeqByOrigin`): dopo una
        /// sessione nuova il mittente puo' ripartire da una sequenza piu' bassa, e tenere la memoria
        /// vecchia farebbe scartare il suo primo pacchetto utile. I contatori di log si azzerano perche'
        /// la riconnessione e' un evento nuovo e va tracciata da capo (poche righe, non un fiume).
        /// </summary>
        private static void ReviveLink(string why)
        {
            // Senza un tentativo armato non c'e' niente da rianimare: chi ha chiuso lo ha fatto con
            // `End()` (una scelta), e rianimare da soli riaprirebbe un filo che il gioco ha lasciato.
            if (!_opened)
                return;

            // Gia' vivo: niente da fare (e nessuna riga in piu' a ogni accept).
            if (!_linkLost)
                return;

            float now = Time.realtimeSinceStartup;

            _linkLost = false;
            _helloed = false;
            _revived = true;
            _phase = Phase.Connecting;
            _openedAt = now;
            _lastRx = now;
            _nextKeepAlive = now;
            _timeoutLogged = false;
            _sendFailLogged = 0;
            _outboxDropped = 0;
            _droppedStale = 0;
            _droppedStaleLogged = 0;
            _lastSeqByOrigin.Clear();
            _peerLogged.Clear();
            _returnLogged.Clear();

            for (int i = 0; i < 3; i++)
            {
                _foreign[i] = 0;
                _foreignLogged[i] = 0;
                _rxLogged[i] = 0;
            }

            ModLog.Info(MarkerOpen + "link RITROVATO (" + why + "): l'hello si rimanda e il battito " +
                        "riparte - hello=no, link=ok, fase=negoziazione (timeout " +
                        (int)HANDSHAKE_TIMEOUT_SECONDS + " s)");

            Notify("P2P: link ritrovato, riconnessione in corso");

            // ⚠️ NIENTE `_onConnected` QUI: l'evento parte dal **primo pacchetto valido**
            // (`MarkConnected`), che e' l'unico fatto che dice "l'altra parte mi sente". Lo stesso
            // contratto di `Begin` (vedi il commento in `TrySendHello`).
        }

        /// <summary>
        /// "E' L'HOST CHE ASPETTO?" — il cancello della rianimazione. Non basta che un pacchetto o una
        /// richiesta arrivino: se arrivano da un altro membro della lobby, il link **nostro** con l'host
        /// non e' tornato, e riarmare l'hello verso `_host` sulla base di un estraneo sarebbe un falso
        /// positivo (il difetto del marker `:596`, che stampa solo perche' `_host` e' valorizzato).
        ///
        /// `_host` e' il valore che l'hello ha gia' usato; se non c'e' ancora (accept arrivato prima del
        /// primo hello), vale l'host **dichiarato** nei dati della lobby — la stessa fonte di
        /// `TrySendHello`, non `GetLobbyOwner` (vedi li' perche').
        /// </summary>
        private static bool IsExpectedHost(ulong id)
        {
            if (id == 0UL)
                return false;

            if (id == _myId)
                return false;

            if (_host.m_SteamID != 0UL)
                return id == _host.m_SteamID;

            ulong declared = DeclaredHost();
            return declared != 0UL && declared == id;
        }

        /// <summary>
        /// Una notifica a schermo **solo quando nessun altro la dara'** — cioe' dopo una
        /// **riconnessione** (`ReviveLink`), dove il percorso normale non arriva: `RaiseDisconnected`
        /// passa da `NetworkManager.OnTransportEnded`, che esce subito a stato gia' "sconnesso"
        /// (`if (_connectionStatus == Disconnected) return`), e il gioco non e' piu' in sessione.
        ///
        /// ⚠️ IL CANCELLO `_revived` NON E' UN DETTAGLIO: sulla **prima** connessione la notifica la da'
        /// gia' il percorso normale (`OnConnect(false, RefusalType.Connection)`, o `Multiplayer.OnDisconnect`
        /// con la stringa localizzata), e aggiungerne una seconda qui sarebbe due avvisi per lo stesso
        /// fatto. Dopo una rianimazione invece quel percorso tace, e senza questa riga il giocatore
        /// vedrebbe solo una schermata ferma: il "non avvisa" del difetto ③.
        /// </summary>
        private static void NotifyIfOutOfSession(string message)
        {
            try
            {
                if (!_revived)
                    return;

                if (Main.Multiplayer == null || Main.Multiplayer.NetworkManager == null)
                    return;

                if (Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                Notify(message);
            }
            catch (Exception)
            {
                // Niente: la riga di log c'e' gia', e la notifica e' un di piu'.
            }
        }

        // ─── LA CALLBACK CHE ACCETTA LE SESSIONI ───────────────────────────────────────────────────

        /// <summary>
        /// `P2PSessionRequest_t` => `AcceptP2PSessionWithUser`, **solo** per i membri della lobby (§9).
        ///
        /// ⚠️ Senza questa callback OGNI pacchetto del peer viene scartato **in silenzio**. Si registra
        /// anche se sul canale non e' ancora arrivato niente: la richiesta puo' precedere il primo
        /// frame, e una richiesta persa sarebbe un timeout senza una riga che lo spiega.
        ///
        /// ⚠️ E' registrata anche da `SteamP2PProbe` (che e' una misura, non un trasporto): Steamworks
        /// ammette piu' callback dello stesso tipo e ciascuna accetta solo i membri della lobby, quindi
        /// il doppione e' innocuo. Qui non c'e' nessun effetto sul filo derivante dall'ordine.
        /// </summary>
        private static void OnSessionRequest(P2PSessionRequest_t request)
        {
            try
            {
                CSteamID remote = request.m_steamIDRemote;
                ulong id = remote.m_SteamID;

                if (id == 0UL)
                    return;

                // ⭐ MARKER ② — la richiesta si dice **sempre**, prima di ogni cancello: e' il fatto che
                // dice se il tunnel e' stato chiesto **a noi**. Senza questa riga, "l'altro dice che ha
                // mandato" e "io non ho visto niente" sono indistinguibili.
                ModLog.Info(SteamP2PTransport.MarkerSessionRequest + id + " (lato PEER)");

                if (!SteamLobbyImpl.IsLobbyMember(remote))
                {
                    ModLog.Warn(SteamP2PTransport.MarkerAcceptFail + id + " (non e' un membro della lobby: " +
                                "sessione NON accettata)");
                    return;
                }

                bool accepted;
                try
                {
                    accepted = SteamNetworking.AcceptP2PSessionWithUser(remote);
                }
                catch (Exception e)
                {
                    ModLog.Error(SteamP2PTransport.MarkerAcceptFail + id + " eccezione: " +
                                 e.GetType().Name + ": " + e.Message);
                    return;
                }

                // ⭐ MARKER ③ — L'ESITO, sempre: `true` = la sessione ora esiste; `false` = Steam l'ha
                // rifiutata **in locale** (quasi sempre: esisteva gia', oppure l'id non e' piu' valido).
                if (accepted)
                {
                    if (_peerLogged.Add(id))
                        ModLog.Info(SteamP2PTransport.MarkerAcceptOk + id + " ACCETTATA (membro della lobby)");

                    // ⭐⭐ LA SESSIONE **DI RITORNO**: quando l'host risponde, sul PC del peer arriva una
                    // `P2PSessionRequest_t` **dall'host**, e senza un `Accept` anche di quella il pacchetto
                    // di risposta resta nel buffer di Valve e scade (il tunnel resterebbe aperto in
                    // **un verso solo**). Questa riga dice che il verso di ritorno e' stato accettato.
                    if (_host.m_SteamID != 0UL && id == _host.m_SteamID && _returnLogged.Add(id))
                        ModLog.Info(SteamP2PTransport.MarkerReturnAccepted + "=" + id);
                }
                else if (_peerLogged.Add(id))
                {
                    ModLog.Info(SteamP2PTransport.MarkerAcceptFail + id + " (Steam ha risposto false: " +
                                "sessione gia' presente o id non piu' valido)");
                }

                // ⭐⭐ ① E L'ACCETTAZIONE **NON E' SOLO DI STEAM**: se la sessione che torna e' quella
                // con il **nostro host**, il link torna vivo (`ReviveLink`).
                //
                // 🔴 Il difetto, misurato: qui c'era **solo** `AcceptP2PSessionWithUser`. La sessione
                // tornava a esistere, `_linkLost` restava `true` e `Tick` usciva in testa ⇒ nessun hello
                // rimandato, nessun battito, nessun timeout che avvisasse. Nel log: `peer ha accettato la
                // sessione di ritorno dall host=…` e poi il nulla, fino a `SESSIONE FALLITA err=4`.
                //
                // ⚠️ VALE ANCHE SE STEAM HA RISPOSTO `false`: quel `false` significa "la sessione c'e'
                // gia' / l'id non e' piu' valido" (vedi la riga sopra), non "l'host non c'e'": la
                // richiesta **e'** una prova che l'host sta parlando con noi. Se poi il tunnel non regge,
                // lo dicono i 25 s dell'handshake — cioe' il log lo dice, invece di tacere.
                if (IsExpectedHost(id))
                    ReviveLink("P2PSessionRequest_t da " + id + " accettata (lato PEER)");
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + "errore in P2PSessionRequest_t: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// ⭐⭐ LA SCATOLA NERA: `P2PSessionConnectFail_t`.
        ///
        /// Arriva **dopo** ~10-15 s dal tentativo (non subito) e **non e' simmetrica**: scatta solo dal lato
        /// che ha pacchetti in coda. E' pero' **l'unica** riga che dice *perche'* Steam non lascia passare:
        ///
        ///   **0** `NONE` · **1** `NOT_RUNNING_SAME_APP` (l'altro ha chiuso, o AppID diverso) ·
        ///   **2** `NO_RIGHTS_TO_APP` (licenza) · **3** `DESTINATION_NOT_LOGGED_IN` (offline) ·
        ///   **4** `TIMEOUT` ⇒ il codice che conta: **nessun `Accept` dall'altra parte**, o firewall/relay.
        ///
        /// ⚠️ NON disconnette: e' una **misura**, e la decisione la prende il timeout (25 s di handshake,
        /// 5 s di battito). Chiamare qui una disconnessione darebbe due fini per lo stesso fatto.
        /// </summary>
        private static void OnSessionConnectFail(P2PSessionConnectFail_t failure)
        {
            try
            {
                ulong id = failure.m_steamIDRemote.m_SteamID;
                int error = failure.m_eP2PSessionError;

                ModLog.Error(SteamP2PTransport.MarkerSessionFail + id + " err=" + error + " " +
                             ErrorName(error) + " (lato PEER: tunnel non aperto)");
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerOpen + "errore in P2PSessionConnectFail_t: " + e.GetType().Name + ": " +
                             e.Message);
            }
        }

        /// <summary>Il nome dell'errore di Steam: un numero da solo non si legge.</summary>
        private static string ErrorName(int error)
        {
            switch (error)
            {
                case 0: return "NONE";
                case 1: return "NOT_RUNNING_SAME_APP";
                case 2: return "NO_RIGHTS_TO_APP";
                case 3: return "DESTINATION_NOT_LOGGED_IN";
                case 4: return "TIMEOUT";
                default: return "SCONOSCIUTO";
            }
        }

        /// <summary>
        /// Registra `Callback&lt;P2PSessionRequest_t&gt;` (e la `P2PSessionConnectFail_t`) una volta sola, e
        /// solo se Steam e' pronto (registrare prima dell'`Init` parlerebbe con l'API nativa in un punto
        /// che non controlliamo).
        /// </summary>
        private static bool EnsureRegistered()
        {
            if (_registered)
                return true;

            if (_registerFailed)
                return false;

            try
            {
                _cbSessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
                _registered = true;

                // ⭐ L'ERRORE DI SESSIONE, registrato nello stesso punto: e' l'unica riga che dice *perche'*
                // Steam non lascia passare, e arriva quando nessun altro sta dicendo niente.
                try
                {
                    _cbSessionFail = Callback<P2PSessionConnectFail_t>.Create(OnSessionConnectFail);
                }
                catch (Exception e)
                {
                    ModLog.Warn(MarkerOpen + "Callback<P2PSessionConnectFail_t>.Create: " +
                                e.GetType().Name + ": " + e.Message + " (la sessione funziona lo stesso, " +
                                "manca solo la riga dell'errore)");
                }

                return true;
            }
            catch (Exception e)
            {
                _registerFailed = true;
                ModLog.Error(MarkerOpen + "Callback<P2PSessionRequest_t>.Create: " + e.GetType().Name +
                             ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// ⚠️ CAMPO E NON LOCALE: `Callback&lt;T&gt;.Create` registra il delegato in un registro GLOBALE e
        /// tiene un `GCHandle` sull'oggetto; una variabile locale sparirebbe dopo il ritorno e il GC
        /// potrebbe raccoglierla, e le callback smetterebbero di arrivare **in silenzio**.
        /// </summary>
        private static Callback<P2PSessionRequest_t> _cbSessionRequest;

        /// <summary>La scatola nera della sessione: stessa ragione (campo, non locale).</summary>
        private static Callback<P2PSessionConnectFail_t> _cbSessionFail;

        /// <summary>Una riga sola per motivo: `Tick` e' per frame, il log non deve diventare un fiume.</summary>
        private static readonly HashSet<string> _loggedReasons = new HashSet<string>();

        private static void LogOnce(string message)
        {
            if (!_loggedReasons.Add(message))
                return;

            ModLog.Warn(MarkerOpen + message);
        }

        /// <summary>Un frame che aspetta l'hello.</summary>
        private sealed class Pending
        {
            internal readonly byte[] Framed;
            internal readonly bool Reliable;

            internal Pending(byte[] framed, bool reliable)
            {
                byte[] copy = new byte[framed.Length];
                Buffer.BlockCopy(framed, 0, copy, 0, framed.Length);

                Framed = copy;
                Reliable = reliable;
            }
        }
    }
}
