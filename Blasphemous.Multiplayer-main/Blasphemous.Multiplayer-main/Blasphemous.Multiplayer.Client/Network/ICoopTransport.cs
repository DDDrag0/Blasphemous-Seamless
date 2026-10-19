using System;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// ⭐ IL SEAM DEL TRASPORTO, LATO CLIENT — la superficie minima con cui `NetworkManager` parla a
    /// "un filo", senza sapere quale.
    ///
    /// PERCHE' ESISTE, e quanto e' piccolo: oggi l'unico filo e' un socket TCP (`SimpleTcpClient`, dalla
    /// DLL di terzi `resources\data\SimpleTCP.dll`) e prima di questo file erano **8 righe** in
    /// `NetworkManager` a toccarlo — la creazione (`:38`), la `Connect` (`:39`), la sottoscrizione agli
    /// eventi (`:40`), `TcpClient.NoDelay` (`:41`), la `Disconnect` (`:56`), l'annullamento del campo
    /// (`:75`) e **una sola** scrittura (`:103`, dentro `SendQueue`). Questo seam non sposta il filo:
    /// mette quelle 8 righe in **una** implementazione (`TcpTransport`), che resta l'unica, e lascia
    /// `NetworkManager` a parlare solo con questa interfaccia. `NoDelay` compreso: era l'eccezione che
    /// bucava l'astrazione (`_client.TcpClient.NoDelay` arrivava al `TcpClient` sotto) e ora sta dentro
    /// il trasporto, dove un socket ce l'ha e un trasporto diverso non ce l'ha.
    ///
    /// ⚠️ IL CONTRATTO E' A **BYTE**, NON A MESSAGGI — e non e' un dettaglio, e' la ragione per cui
    /// questo refactor e' verificabile:
    ///   `Send(byte[] framed)` riceve il buffer **gia' impacchettato** da `QueueMesssage`, testa
    ///   `[ushort len][byte type]` compresa: cioe' **esattamente i byte che oggi finiscono in
    ///   `SimpleTcpClient.Write`** (`NetworkManager.QueueMesssage`, `:85-95` di HEAD, costruisce la lista;
    ///   `SendQueue`, `:103` di HEAD, la passa a `Write`). Il trasporto a flusso li scrive **verbatim**, senza
    ///   toccarli. Domani un trasporto a **messaggi** (Steam) ricevera' **lo stesso array**: il confine
    ///   del messaggio lo da' lui, quindi potra' anche smettere di aver bisogno della testa di
    ///   lunghezza — ma quella decisione resta **dentro `QueueMesssage`**, che il seam non tocca. In
    ///   nessuno dei due casi il chiamante cambia: se il filo cambia di un byte, e' un errore del seam,
    ///   non una scelta di questo file.
    ///
    /// ⚠️ IL THREADING FA PARTE DEL CONTRATTO: `DataReceived` e' consegnato **sul thread che il
    /// trasporto usa per leggere**. Per il TCP e' il thread di lettura di `SimpleTCP`, esattamente come
    /// oggi: nessuna implementazione deve fare marshal sul main thread. Spostare la consegna cambierebbe
    /// il comportamento di tutta la ricezione (`ProcessQueue` gira da `OnLateUpdate` e prende
    /// `datalock`), quindi non si fa — nemmeno "per pulizia".
    ///
    /// ⚠️ LE ECCEZIONI SONO QUELLE DI OGGI, e fanno parte del contratto:
    ///   · `Connect` puo' lanciare `System.Net.Sockets.SocketException` quando il socket non si apre:
    ///     `NetworkManager.Connect` la cattura (`:43-48`) per rispondere `RefusalType.Connection`;
    ///   · `Send` puo' lanciare `System.IO.IOException` quando il socket e' caduto: `NetworkManager.SendQueue`
    ///     la cattura (`:105-108`) per chiamare `OnDisconnect()`.
    /// Un'implementazione che **assorbe** queste due eccezioni non e' conforme: spegnerebbe le due sole
    /// vie con cui il client si accorge che la connessione e' caduta.
    ///
    /// ⚠️ COSA NON C'E' DI PROPOSITO: nessun `event` di "errore", nessun `byte[] Receive()`, nessuno
    /// stato di riconnessione, nessun `int` di byte scritti. Sono tutte cose che oggi il client non usa;
    /// aggiungerle qui sarebbe superficie da mantenere senza un chiamante.
    ///
    /// 📌 STEP 1b (NON fatto qui): il **server** ha lo stesso socket in `Server.cs` (`:16` campo,
    /// `:46-51` creazione + 3 eventi + `Start`, `:73` una sola `Send(ip, byte[])`), ma il suo seam e' un
    /// altro lavoro perche' la sua identita' di peer e' la **stringa IP** (~115 righe su 864). In questo
    /// giro il server non e' toccato.
    /// </summary>
    internal interface ICoopTransport
    {
        /// <summary>
        /// Apre il filo verso `host`:`port`.
        ///
        /// Puo' lanciare `System.Net.Sockets.SocketException` se il socket non si apre: il chiamante ha
        /// gia' il `catch` che serve (`NetworkManager.Connect`, `:70`) e la risposta al giocatore e'
        /// `RefusalType.Connection`. Non catturarla qui.
        /// </summary>
        void Connect(string host, int port);

        /// <summary>
        /// Chiude il filo. Non lancia: un guasto in chiusura non deve impedire al chiamante di
        /// riportare il suo stato a "disconnesso" (`NetworkManager.OnDisconnect`).
        /// </summary>
        void Disconnect();

        /// <summary>
        /// `true` se il filo e' aperto.
        ///
        /// ⚠️ `NetworkManager` **non** consuma questa proprieta': la sua `IsConnected` e' rimasta
        /// `_connectionStatus == Connected &amp;&amp; trasporto != null`, che e' l'espressione di oggi
        /// (`NetworkManager.cs:36`). Usare il `Connected` del socket sarebbe un cambio di comportamento,
        /// perche' oggi la caduta del socket non viene osservata finche' una scrittura non fallisce.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Scrive sul filo un buffer **gia' impacchettato** (`[ushort len][byte type][payload]`), senza
        /// interpretarlo, spezzarlo o ri-impacchettarlo: i byte che arrivano sono i byte che partono.
        ///
        /// Puo' lanciare `System.IO.IOException` se il socket e' caduto: e' load-bearing, il chiamante
        /// la usa per disconnettersi (`NetworkManager.SendQueue`, `:142`). Non catturarla qui.
        /// </summary>
        void Send(byte[] framed);

        /// <summary>Il filo si e' aperto (dopo un `Connect` riuscito).</summary>
        event Action Connected;

        /// <summary>Il filo si e' chiuso (da `Disconnect`).</summary>
        event Action Disconnected;

        /// <summary>
        /// Sono arrivati dei byte, cosi' come sono usciti dal socket: nessun riassemblaggio e nessuna
        /// interpretazione (il framing lo fa `ProcessQueue`). Consegnato sul thread di lettura del
        /// trasporto, che per il TCP e' quello di `SimpleTCP`.
        /// </summary>
        event Action<byte[]> DataReceived;
    }
}
