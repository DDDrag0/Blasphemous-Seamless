using Blasphemous.ModdingAPI;
using SimpleTCP;
using System;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// ⭐ L'UNICA IMPLEMENTAZIONE DI <see cref="ICoopTransport"/>: il socket TCP di oggi, avvolto e non
    /// cambiato — `SimpleTcpClient` da `resources\data\SimpleTCP.dll`.
    ///
    /// CONTIENE LE 8 RIGHE CHE PRIMA STAVANO IN `NetworkManager`, identiche (a sinistra i numeri di riga
    /// di HEAD `4b85289`, cioe' prima del seam; a destra i numeri di questo file):
    ///   `NetworkManager.cs:38`  `_client = new SimpleTcpClient();`        → `:103` qui
    ///   `NetworkManager.cs:39`  `_client.Connect(server, port);`          → `:104` qui
    ///   `NetworkManager.cs:41`  `_client.TcpClient.NoDelay = true;`       → `:105` qui
    ///   `NetworkManager.cs:40`  `_client.DataReceived += ReceiveMessage;` → `:106` qui (poi rimbalzato)
    ///   `NetworkManager.cs:56`  `_client.Disconnect();`                   → `:152` qui
    ///   `NetworkManager.cs:103` `_client.Write(sendingQueue.ToArray());`  → `:172` qui
    ///   `NetworkManager.cs:19`  campo `SimpleTcpClient _client`            → `:65` qui
    ///   `NetworkManager.cs:75`  `_client = null`                          → resta in `NetworkManager` (e' lo stato del seam)
    /// Nomi, ordine e argomenti delle chiamate sono quelli di prima: **il filo non cambia di un byte**.
    ///
    /// ⚠️ `NoDelay` STA QUI, ed era l'eccezione che bucava l'astrazione: in `NetworkManager` era
    /// `_client.TcpClient.NoDelay = true`, cioe' una riga che attraversava il wrapper per arrivare al
    /// `TcpClient` sotto. Qui e' al suo posto, e un trasporto che non ha un `TcpClient` semplicemente non
    /// ce l'ha. **Perche' il valore e' `true` e resta `true`**: con `NoDelay = false` (default di .NET)
    /// il Nagle accumula i pacchetti piccoli, e questa mod manda posizioni/animazioni a 30 Hz — il seam
    /// non tocca ne' il valore ne' il momento in cui viene impostato (subito dopo la `Connect`, prima di
    /// qualunque scrittura: nessun byte puo' essere partito prima).
    ///
    /// ⚠️ NESSUN PARAMETRO NUOVO. Timeout, retry, keep-alive: `SimpleTcpClient` non ne espone e il codice
    /// di oggi non ne imposta (l'unica opzione toccata era `NoDelay`); qui non ne compare nessuno.
    ///
    /// ⚠️ IL THREAD NON CAMBIA: `HandleDataReceived` e' il callback di `SimpleTCP`, quindi gira sul suo
    /// thread di lettura, e da li' rimbalza a <see cref="DataReceived"/> **senza** marshal. Non c'e'
    /// nessun `MainThreadDispatcher`, nessuna coda nuova, nessun `lock`: la coda e i suoi lock restano
    /// dove sono (`NetworkManager.receivingQueue` / `datalock`).
    ///
    /// ⚠️ DOVE LE ECCEZIONI SONO DI PROPOSITO NON CATTURATE, e perche' (e' l'unico modo di rispettare
    /// "zero cambi di comportamento"):
    ///   · <see cref="Connect"/> **non** cattura `SocketException`: `NetworkManager.Connect` ha lo stesso
    ///     `catch` di oggi e deve continuare a vedere la stessa eccezione per rispondere
    ///     `RefusalType.Connection` e restituire `false`;
    ///   · <see cref="Send"/> **non** cattura `IOException`: `NetworkManager.SendQueue` la cattura per
    ///     chiamare `OnDisconnect()`. Assorbirla qui vorrebbe dire non accorgersi piu' che il server e'
    ///     caduto;
    ///   · <see cref="DataReceived"/> **non** ha un `try/catch` attorno all'invocazione: un'eccezione di
    ///     un gestore deve continuare a risalire nel thread di `SimpleTCP` come oggi.
    /// Catturato c'e' invece cio' che **nuovo** e che non deve mai essere la ragione per cui una
    /// connessione non si apre: la riga del marker, le due invocazioni degli eventi `Connected` e
    /// `Disconnected`, e la chiusura in <see cref="Disconnect"/> (dove oggi un guasto usciva dal metodo e
    /// saltava `NetworkManager.OnDisconnect`, lasciando il client in stato "connesso": assorbirlo e'
    /// l'unico delta dichiarato di questo file — vedi `scratch\REFERTO-SEAM-TRASPORTO.md`).
    /// </summary>
    internal sealed class TcpTransport : ICoopTransport
    {
        /// <summary>
        /// Il prefisso del marker di apertura. `[Net]` e' lo stesso prefisso di `[Net] skin:`
        /// (`SkinSendMarker`), quindi `Select-String '\[Net\] trasporto'` prende questa riga e nient'altro.
        /// </summary>
        internal const string Marker = "[Net] trasporto=tcp";

        /// <summary>Il socket vero. E' l'unico punto di questo assembly che tocca `SimpleTCP`.</summary>
        private SimpleTcpClient _client;

        /// <summary>
        /// `true` se il socket esiste ed e' connesso. Vedi il commento su
        /// <see cref="ICoopTransport.IsConnected"/>: `NetworkManager` **non** la usa, perche' la sua
        /// `IsConnected` e' rimasta quella di oggi.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                try
                {
                    return _client != null && _client.TcpClient != null && _client.TcpClient.Connected;
                }
                catch
                {
                    // Un socket in chiusura non deve trasformare una domanda di stato in un'eccezione.
                    return false;
                }
            }
        }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> DataReceived;

        /// <summary>
        /// Apre il socket. Le tre righe di oggi, nello stesso ordine (`:38`, `:39`, `:41`), piu' la
        /// sottoscrizione (`:40`) e il marker.
        ///
        /// ⚠️ La sottoscrizione a `SimpleTCP` avviene **dentro** questa `Connect`, quindi un po' prima di
        /// oggi (dove `NetworkManager` si sottoscriveva dopo il ritorno della `Connect`): la finestra in
        /// cui i byte in arrivo non hanno un gestore c'e' in entrambi i casi, e in entrambi i casi quei
        /// byte si perdono. Nessuna differenza sul filo.
        /// </summary>
        public void Connect(string host, int port)
        {
            _client = new SimpleTcpClient();
            _client.Connect(host, port);
            _client.TcpClient.NoDelay = true;
            _client.DataReceived += HandleDataReceived;

            // ⭐ IL MARKER DI APERTURA — una riga, solo log, nessun effetto sul filo.
            //
            // ⚠️ L'HOST E' QUELLO CHE IL GIOCATORE HA SCRITTO, e si puo' scrivere: **l'IP e' gia' nel
            // log di oggi**, senza nessuna aggiunta di questo seam — `ConnectionDisplay.TryConnect`
            // (`ConnectionDisplay.cs:199`) stampa `Attempting to connect to {ServerIp}` un frame prima,
            // e `TryDisconnect` (`:234`) stampa `Disconnecting from {ServerIp}`. Quindi il marker non
            // introduce nessuna esposizione nuova: se un giorno quelle righe sparissero, questa deve
            // diventare mascherata (o sparire), non restare.
            //
            // Gli altri due campi: `trasporto=tcp` e' la classe di filo (con un trasporto diverso la
            // parola cambia, ed e' il modo di sapere QUALE implementazione sta girando senza leggere il
            // codice) e `porta=` e' quella effettiva, che puo' non essere la 27051 di default se
            // l'utente ha scritto `ip:porta` (`ConnectionDisplay.cs:204-214`).
            try
            {
                // La riga si costruisce per concatenazione (non con `string.Format`) perche' `Marker` e'
                // una costante: il compilatore fonde `Marker + " host="` in **un solo letterale**
                // `[Net] trasporto=tcp host=`, che e' esattamente cio' che `tools\marker-check.ps1` cerca
                // nella DLL. Con un `{0}` in mezzo la riga sarebbe la stessa ma il letterale no, e la
                // verifica di contenuto non avrebbe niente da trovare.
                ModLog.Info(Marker + " host=" + host + " porta=" + port.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                // Un log non deve essere la ragione per cui una connessione non si apre.
            }

            if (Connected != null)
            {
                try { Connected(); }
                catch (Exception e) { ModLog.Error("[Net] trasporto=tcp: un gestore di Connected ha lanciato: " + e.GetType().Name); }
            }
        }

        /// <summary>
        /// Chiude il socket (prima era `NetworkManager.cs:56`) e avvisa. Vedi il commento di classe sul
        /// perche' questa e' l'unica eccezione assorbita.
        /// </summary>
        public void Disconnect()
        {
            SimpleTcpClient client = _client;
            try
            {
                if (client != null)
                    client.Disconnect();
            }
            catch (Exception e)
            {
                ModLog.Error("[Net] trasporto=tcp: errore in chiusura: " + e.GetType().Name);
            }

            if (Disconnected != null)
            {
                try { Disconnected(); }
                catch (Exception e) { ModLog.Error("[Net] trasporto=tcp: un gestore di Disconnected ha lanciato: " + e.GetType().Name); }
            }
        }

        /// <summary>
        /// Scrive i byte che riceve, senza toccarli (prima era `NetworkManager.cs:103`). Vedi il commento
        /// di classe: `IOException` risale di proposito.
        /// </summary>
        public void Send(byte[] framed)
        {
            _client.Write(framed);
        }

        /// <summary>
        /// Il callback di `SimpleTCP`: rimbalza il payload a <see cref="DataReceived"/> **sullo stesso
        /// thread** (niente marshal), senza copiare, spezzare o interpretare i byte.
        /// </summary>
        private void HandleDataReceived(object sender, DataReceivedEventArgs message)
        {
            if (DataReceived != null)
                DataReceived(message.data);
        }
    }
}
