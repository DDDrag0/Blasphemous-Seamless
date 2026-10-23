using Blasphemous.ModdingAPI;
using System;
using System.Net.Sockets;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// ⭐⭐ IL TRASPORTO P2P, LATO PEER — il filo di Steam dietro lo **stesso** seam del TCP.
    ///
    /// COSA FA, IN TRE RIGHE: entra nella lobby Steam in cui siamo gia' (quella dell'invito), prende il
    /// **proprietario** della lobby (l'host), gli manda un **hello** affidabile, e da quel momento manda
    /// i frame di gioco su **tre canali** (`P2PWire`): 0 unreliable con numero di sequenza, 1 reliable,
    /// 2 reliable isolato per le skin. Riceve in un ciclo per frame, scarta i pacchetti **vecchi** per
    /// mittente, e tiene la sessione viva con un battito: se il battito manca per 5 s il link e' perso.
    ///
    /// 🔴 PERCHE' DUE CLASSI (`SteamP2PTransport` + `SteamP2PTransportImpl`) E NON UNA. La regola della
    /// mod e' che **solo una `*Impl` nomina `Steamworks`** (vedi `SteamLobby.cs`), e la ragione e'
    /// concreta: su una copia **non-Steam** (GOG &amp; co.) i tipi `Steamworks` possono mancare, e un tipo
    /// assente non e' "una funzione che non c'e'": e' una **`TypeLoadException`** lanciata **quando la JIT
    /// compila il metodo che lo nomina**, cioe' al **punto di chiamata**, non dentro il metodo chiamato.
    /// Quindi:
    ///   · questa classe **non nomina nessun tipo `Steamworks`** — ne' un campo, ne' un parametro;
    ///   · ogni chiamata all'implementazione sta in **un metodo suo**, dentro un `try/catch`, e prima
    ///     c'e' il cancello `SteamLobby.Available` (che e' gia' "Steam c'e' ed e' loggato", chiesto per
    ///     riflessione da `SteamLobby`): se Steam non c'e', l'implementazione non viene **mai** toccata,
    ///     la sua JIT non parte, e non c'e' nessuna eccezione da catturare;
    ///   · un guasto esce da una porta sola e con la forma che il chiamante conosce gia':
    ///     `Connect` lancia `SocketException` (la stessa che lancia `TcpTransport.Connect` e che
    ///     `NetworkManager.Connect` traduce in `RefusalType.Connection`) e `Send` lancia
    ///     `System.IO.IOException` (la stessa che lancia il socket caduto, e che `NetworkManager.SendQueue`
    ///     usa per disconnettersi). Nessuna eccezione nuova nel contratto.
    ///
    /// ⚠️ QUANDO SI USA: **mai da solo**. Il filo lo sceglie il campo "Server IP" quando vale `p2p`
    /// (<see cref="IsRequested"/>). La ragione per cui non si sceglie "sono in una lobby" e' che l'host
    /// e' in lobby **anche lui**, e il suo client deve restare un client **TCP** sul loopback
    /// (`scratch\REFERTO-C1B1-SEAM-SERVER.md` §4): cambiarglielo romperebbe l'unico percorso che i 42
    /// frame misurano.
    /// </summary>
    internal sealed class SteamP2PTransport : ICoopTransport
    {
        /// <summary>
        /// Il marker di apertura, gemello di `TcpTransport.Marker` (`[Net] trasporto=tcp`): da questa
        /// riga si sa **quale** filo sta girando senza leggere il codice.
        /// </summary>
        internal const string Marker = "[Net] trasporto=p2p";

        /// <summary>Il token che sceglie questo filo nel campo "Server IP".</summary>
        internal const string RequestToken = "p2p";

        // ─── I MARKER DELLA DIAGNOSI: UNA DEFINIZIONE SOLA, PER I DUE LATI ────────────────────────
        //
        // ⚠️ PERCHE' QUI E NON DENTRO LE `*Impl`: sono **stringhe**, non tipi `Steamworks`, e questa
        // classe e' il punto neutro che entrambe le implementazioni (peer e host) possono nominare senza
        // aggiungere superficie al motore. Scritte una volta sola, le due macchine producono le
        // **stesse** righe, e i due log si possono mettere in colonna.
        //
        // ⚠️ E SONO CINQUE PIU' DUE, SCELTI DOVE SI PRENDE LA DECISIONE (non dove si vede il dettaglio):
        //   ① l'adapter si arma (senza questa riga l'host e' sordo e nessuno lo sa);
        //   ② il peer manda l'hello e arma il timeout dell'handshake;
        //   ③ l'host riceve la richiesta di sessione — e se l'adapter **non** e' armato lo dice;
        //   ④ l'host accetta (o Steam rifiuta) — `AcceptP2PSessionWithUser` vero/falso;
        //   ⑤ il primo pacchetto esce dalla ricezione, da entrambi i lati, con `connesso (handshake)`
        //      subito dopo;
        //   ·  la `P2PSessionConnectFail_t`, che e' **l'unica** riga che dice *perche'* Steam non
        //      lascia passare (arriva 10-15 s dopo il tentativo, non subito).
        //
        // La tabella di lettura (cosa significa quale riga manca) sta in
        // `scratch\REFERTO-TIMEOUT-E-UI.md`.
        internal const string MarkerAdapterActive = "[Net] p2p: adapter attivo (lobby=";
        internal const string MarkerHelloSent = "[Net] p2p: hello inviato a ";
        internal const string MarkerSessionRequest = "[Net] p2p: richiesta sessione da ";
        internal const string MarkerNotArmed = "[Net] p2p: richiesta da ";
        internal const string MarkerAcceptOk = "[Net] p2p: sessione con ";
        internal const string MarkerAcceptFail = "[Net] p2p: accept FALLITO ";
        internal const string MarkerReturnAccepted = "[Net] p2p: peer ha accettato la sessione di ritorno dall host";
        internal const string MarkerFirstPacket = "[Net] p2p: primo pacchetto (canale ";
        internal const string MarkerConnected = "[Net] p2p: connesso (handshake ";
        internal const string MarkerHandshakeTimeout = "[Net] p2p: handshake scaduto (";
        internal const string MarkerSessionFail = "[Net] p2p: SESSIONE FALLITA peer=";

        private static bool _failureLogged;

        /// <summary>
        /// Ipotizza il P2P quando il campo "Server IP" vale esattamente `p2p` (spazi e maiuscole
        /// ignorati). Tutto il resto — indirizzi IP, `ip:porta`, nomi — resta **TCP**, byte per byte
        /// come prima.
        /// </summary>
        internal static bool IsRequested(string server)
        {
            if (server == null)
                return false;

            return string.Equals(server.Trim(), RequestToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Collega il sink dell'implementazione agli eventi di **questa** istanza. Ce n'e' una sola per
        /// volta (la crea `NetworkManager.Connect`), quindi il registro statico non si sovrascrive mai
        /// "a meta'": una nuova connessione sostituisce la vecchia, che e' gia' stata chiusa.
        /// </summary>
        internal SteamP2PTransport()
        {
            if (!SteamLobby.Available)
                return;

            try
            {
                CallSetSink(OnData, OnConnected, OnDisconnected);
            }
            catch (Exception e)
            {
                Report("SetSink", e);
            }
        }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> DataReceived;

        /// <summary>
        /// `true` quando l'hello e' partito e il link non e' scaduto. `NetworkManager` **non** la usa
        /// (la sua `IsConnected` e' rimasta quella di sempre): la usa la facciata per non toccare
        /// l'implementazione inutilmente.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (!SteamLobby.Available)
                    return false;

                try
                {
                    return QueryOpen();
                }
                catch (Exception e)
                {
                    Report("IsConnected", e);
                    return false;
                }
            }
        }

        /// <summary>
        /// Apre il filo. Lancia `SocketException` se il P2P non e' utilizzabile **adesso** (Steam non
        /// disponibile, o nessuna lobby Steam: l'invito non e' stato accettato) — cosi' il chiamante
        /// risponde al giocatore con lo stesso `RefusalType.Connection` di un socket rifiutato.
        ///
        /// ⚠️ E IL FALLO CHE ARRIVA DOPO (nessun pacchetto per 5 s) NON e' un'eccezione qui: e'
        /// l'evento `Disconnected` piu' un `IOException` dal `Send` successivo, che e' la via di
        /// `NetworkManager.SendQueue` → `OnDisconnect()`. Vedi `SteamP2PTransportImpl.Tick`.
        /// </summary>
        public void Connect(string host, int port)
        {
            if (!SteamLobby.Available)
            {
                ModLog.Warn(Marker + ": Steam non disponibile (i tipi Steamworks non ci sono, o nessun " +
                            "account loggato): il P2P non si apre, il percorso TCP resta intatto");
                throw new SocketException((int)SocketError.NotConnected);
            }

            bool started;
            try
            {
                started = CallBegin(host, port);
            }
            catch (Exception e)
            {
                Report("Begin", e);
                throw new SocketException((int)SocketError.NotConnected);
            }

            if (!started)
            {
                ModLog.Error(Marker + ": nessuna lobby Steam in cui entrare. Accetta un invito (o ospita " +
                             "tu), poi premi Connect con '" + RequestToken + "' nel campo Server IP");
                throw new SocketException((int)SocketError.NotConnected);
            }

            ReportOpen(host, port);
        }

        /// <summary>
        /// ⭐ IL MARKER DI APERTURA DEL P2P — gemello di `[Net] trasporto=tcp host=... porta=...`
        /// (`TcpTransport.Connect`).
        ///
        /// 🔴 PERCHE' E' STATO AGGIUNTO (e non e' cosmesi): la costante <see cref="Marker"/> compariva
        /// **solo** nei rami di errore di questa classe, quindi un log con il tunnel **aperto** non diceva
        /// da nessuna parte "sto andando in P2P". Nella diagnosi del fallimento
        /// (`scratch\DIAGNOSI-P2P-FALLITO.md`) la riga `[Net] trasporto=p2p` era **assente**: non si
        /// poteva distinguere "il P2P non e' partito" da "e' partito e non ha risposto nessuno". Dopo
        /// questa riga la distinzione e' nel log, e il campo "Server IP" dice `p2p` solo se qualcuno l'ha
        /// scritto davvero.
        ///
        /// ⚠️ SOLO LOG: nessuno stato, nessuna decisione, nessun byte sul filo. Il `try/catch` c'e' perche'
        /// `LobbySummary` chiama Steam (e un log non deve mai essere la ragione per cui una connessione
        /// non si apre).
        /// </summary>
        private static void ReportOpen(string host, int port)
        {
            try
            {
                // La concatenazione (non `string.Format`) fonde `Marker` e `" host="` in **un solo
                // letterale**, che e' quello che `tools\marker-check.ps1` trova nella DLL — la stessa
                // ragione gia' scritta in `TcpTransport.Connect`.
                ModLog.Info(Marker + " host=" + host + " porta=" +
                            port.ToString(System.Globalization.CultureInfo.InvariantCulture) + " - " +
                            LobbySummary());
            }
            catch (Exception e)
            {
                Report("LogOpen", e);
            }
        }

        /// <summary>Chiude il filo e avvisa. Non lancia: un guasto in chiusura non deve impedire al
        /// chiamante di riportare il suo stato a "disconnesso".</summary>
        public void Disconnect()
        {
            if (SteamLobby.Available)
            {
                try
                {
                    CallEnd();
                }
                catch (Exception e)
                {
                    Report("End", e);
                }
            }

            RaiseDisconnected();
        }

        /// <summary>
        /// Manda i frame sul canale giusto. Lancia `System.IO.IOException` se il link e' stato dichiarato
        /// perso (timeout): e' il contratto del seam, e la via che il chiamante ha gia'.
        /// </summary>
        public void Send(byte[] framed, TransportClass cls)
        {
            if (!SteamLobby.Available)
                throw new System.IO.IOException("[Net] trasporto=p2p: Steam non disponibile");

            try
            {
                CallSend(framed, cls == TransportClass.Reliable);
            }
            catch (System.IO.IOException)
            {
                throw;
            }
            catch (Exception e)
            {
                Report("Send", e);
                throw new System.IO.IOException("[Net] trasporto=p2p: invio fallito (" + e.GetType().Name + ")");
            }
        }

        /// <summary>
        /// ⭐ IL CICLO PER FRAME della ricezione (e del battito). Chiamato da `Multiplayer.OnLateUpdate`
        /// **dopo** `SteamLobby.Tick()` — che e' il pump delle callback: un `P2PSessionRequest_t`
        /// accettata in questo frame deve essere gia' stata consegnata prima di leggere i canali.
        ///
        /// ⚠️ `try/catch` **qui**, non dentro i gestori: questa funzione gira nel late update della mod,
        /// e un'eccezione che sfugge fermerebbe il resto del ciclo (pacchetti, giocatori, nemici) per un
        /// guasto di Steam. Un guasto si logga **una volta sola** (e' per frame).
        /// </summary>
        internal static void Tick()
        {
            if (!SteamLobby.Available)
                return;

            try
            {
                CallTick();
            }
            catch (Exception e)
            {
                Report("Tick", e, true);
            }
        }

        /// <summary>
        /// ⭐ QUALE LOBBY — una riga di sola lettura per il referto e per il test: senza lobby non c'e'
        /// P2P, e "p2p" da solo sulla schermata non basta a capirlo. Vuota se non c'e' nessuna lobby.
        /// </summary>
        internal static string LobbySummary()
        {
            if (!SteamLobby.Available)
                return string.Empty;

            try
            {
                return QueryLobbySummary() ?? string.Empty;
            }
            catch (Exception e)
            {
                Report("LobbySummary", e);
                return string.Empty;
            }
        }

        // ─── GLI EVENTI, SOLLEVATI DALL'IMPLEMENTAZIONE (che gira sul thread del Tick) ──────────────
        //
        // ⚠️ UN `try/catch` attorno a ciascun gestore: qui siamo dentro `Tick`, cioe' dentro il late
        // update della mod. Un gestore che lancia (per esempio `NetworkManager.ProcessQueue`) fermerebbe
        // la lettura dei canali per sempre, e con essa il gioco. Il TCP non ha questo `catch` perche' li'
        // l'eccezione risale al thread di lettura di `SimpleTCP`, dove non c'e' nessun altro da salvare.
        private void OnData(byte[] framed)
        {
            Action<byte[]> handler = DataReceived;
            if (handler == null)
                return;

            try
            {
                handler(framed);
            }
            catch (Exception e)
            {
                Report("DataReceived", e, true);
            }
        }

        private void OnConnected()
        {
            Action handler = Connected;
            if (handler == null)
                return;

            try
            {
                handler();
            }
            catch (Exception e)
            {
                Report("Connected", e, true);
            }
        }

        private void OnDisconnected()
        {
            RaiseDisconnected();
        }

        private void RaiseDisconnected()
        {
            Action handler = Disconnected;
            if (handler == null)
                return;

            try
            {
                handler();
            }
            catch (Exception e)
            {
                Report("Disconnected", e, true);
            }
        }

        // ─── I GUSCI VERSO L'IMPLEMENTAZIONE ─────────────────────────────────────────────────────────
        //
        // ⚠️ UN METODO PER CHIAMATA, come in `SteamLobby`: la JIT compila il corpo al primo uso, quindi
        // la `TypeLoadException` dell'implementazione (se i tipi mancassero davvero) nasce **qui
        // dentro**, dentro il `try` del metodo che li ha invocati. Se queste righe stessero direttamente
        // nei metodi qui sopra, la JIT caricherebbe l'implementazione compilando QUEI metodi, cioe'
        // prima del `try`: un `catch` che non cattura mai niente.

        private static void CallSetSink(Action<byte[]> onData, Action onConnected, Action onDisconnected)
        {
            SteamP2PTransportImpl.SetSink(onData, onConnected, onDisconnected);
        }

        private static bool QueryOpen()
        {
            return SteamP2PTransportImpl.Open;
        }

        private static bool CallBegin(string host, int port)
        {
            return SteamP2PTransportImpl.Begin(host, port);
        }

        private static void CallEnd()
        {
            SteamP2PTransportImpl.End();
        }

        private static void CallSend(byte[] framed, bool reliable)
        {
            SteamP2PTransportImpl.Send(framed, reliable);
        }

        private static void CallTick()
        {
            SteamP2PTransportImpl.Tick();
        }

        private static string QueryLobbySummary()
        {
            return SteamP2PTransportImpl.LobbySummary();
        }

        /// <summary>
        /// Una riga di log per un guasto catturato al confine. `once` e' per i metodi per frame: un
        /// guasto che si ripete 60 volte al secondo non deve diventare 60 righe al secondo.
        /// </summary>
        private static void Report(string operation, Exception error, bool once = false)
        {
            try
            {
                if (once)
                {
                    if (_failureLogged)
                        return;

                    _failureLogged = true;
                }

                ModLog.Error(Marker + " errore: " + operation + ": " + error.GetType().Name + ": " +
                             error.Message);
            }
            catch (Exception)
            {
                // Niente: se anche il log esplode, il gioco deve continuare.
            }
        }
    }
}
