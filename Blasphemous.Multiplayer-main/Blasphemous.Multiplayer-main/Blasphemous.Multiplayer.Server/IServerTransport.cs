using Blasphemous.Multiplayer.Common;
using System;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// ⭐⭐ IL SEAM DEL TRASPORTO, LATO SERVER (C1b-1) — la superficie minima con cui <see cref="Server"/>
/// parla a "un filo", senza sapere quale.
///
/// PERCHE' ESISTE. Fino a C1a il server era **cucito** a `SimpleTcpServer`: il costruttore, i tre
/// eventi (`ClientConnected`/`DataReceived`/`ClientDisconnected`), la `Start` e **l'unica**
/// `Send(ip, byte[])` stavano dentro `Server.cs`. Quel modello e' quello di un **listener**: qualcuno
/// bussa, il listener accetta un socket e consegna eventi con l'indirizzo del chiamante (`e.ip`).
/// Un trasporto P2P **non ha un listener**: non c'e' nessuno che bussa. Le sessioni si accettano per
/// peer (`AcceptP2PSessionWithUser`) e i pacchetti arrivano gia' attribuiti a un `CSteamID`. Se il
/// server non parlasse a un'interfaccia, il P2P non ci si potrebbe attaccare senza riscriverlo.
///
/// ⇒ Questo file e' **solo l'astrazione**: qui non c'e' TCP, non c'e' Steam, non c'e' un byte di
///   protocollo. L'unica implementazione di C1b-1 e' <see cref="TcpServerTransport"/>, che contiene
///   le righe che prima stavano in `Server.cs` — le stesse, spostate.
///
/// ⭐ L'IDENTITA' E' <see cref="PeerId"/>, NON UNA STRINGA. E' il tipo di C1a e non se ne crea un
/// doppione: il trasporto e' l'unico che sa **come** un peer si chiama sul suo filo (per il TCP la
/// stringa `ip:porta` che `SimpleTCP` ci da', per il P2P un `CSteamID`), e la fabbrica la usa **solo**
/// lui (`TcpServerTransport` fa `PeerId.FromIp(e.ip)`). Da li' in poi il server parla di `PeerId` e
/// basta — `peer.ToString()` e' la stessa stringa di prima, quindi `Send` trova lo stesso socket e i
/// log hanno lo stesso testo.
///
/// ⚠️ IL CONTRATTO E' A **BYTE**, NON A MESSAGGI. `Send(peer, framed)` riceve il buffer **gia'
/// impacchettato** (`[ushort len][byte type][payload]`), testa compresa: cioe' **esattamente** i byte
/// che oggi finiscono in `SimpleTcpServer.Send`. Il trasporto non lo interpreta, non lo spezza e non
/// lo ri-impacchetta. Il framing (`ushort` di lunghezza + `NetworkType`) resta **nel server**, dov'e'
/// sempre stato: e' protocollo, non filo.
///
/// ⚠️ E SIMMETRICO: `FrameReceived(peer, data)` consegna i byte **come sono usciti dal filo**, senza
/// riassemblaggi ne' interpretazioni — per il TCP e' il buffer che `SimpleTcpServer` passa nel suo
/// `DataReceived` (uno o piu' frame interi, come li da' il socket). Per un trasporto a **messaggi**
/// (P2P) sara' **un pacchetto per evento**, cioe' esattamente un frame: il ciclo di lettura del server
/// lo legge uguale, perche' un frame solo e' il caso piu' semplice di quello che riceve oggi.
///
/// ⚠️ IL THREADING FA PARTE DEL CONTRATTO: gli eventi sono consegnati **sul thread che il trasporto
/// usa per leggere**. Per il TCP e' il thread di lettura di `SimpleTCP`, esattamente come oggi.
/// Nessuna implementazione deve fare marshal su un altro thread: cambiare il thread di consegna
/// cambierebbe il comportamento di tutta la ricezione del server.
///
/// ⚠️ LE ECCEZIONI SONO QUELLE DI OGGI, e sono load-bearing:
///   · `Start` **non lancia**: ritorna `false` quando non riesce ad aprire la porta (oggi e' il
///     `catch` di `SocketException` e `InvalidOperationException` in `Server.Start`); il chiamante
///     (`Server`) risponde `false` al suo chiamante, senza eccezioni;
///   · `Stop` puo' lanciare: `Server.Stop` lo cattura e scrive `"Couldn't stop the server: ..."`
///     (testo invariato), perche' un guasto in chiusura non deve lasciare la porta occupata;
///   · `Send` puo' lanciare quando il peer non c'e' piu': `Server.Send` cattura e scrive
///     `"Couldn't send data to <peer>!"` (testo invariato). Un'implementazione che **assorbe**
///     quest'eccezione spegnerebbe l'unico modo che il server ha di accorgersi di un peer caduto.
///
/// ⚠️ DUE PROPRIETA' CHE SEMBRANO FUORI POSTO E NON LO SONO:
///   · `ListeningAddresses()` — il marker `[Host] ascolto su=...` deve continuare a **chiedere** quali
///     socket esistono davvero (`SimpleTcpServer.GetListeningIPs()`), non a indovinarli: con il bind
///     per-NIC il loopback poteva mancare, e la riga lo diceva. E' una domanda al filo, quindi il suo
///     posto e' qui. Un trasporto P2P rispondera' qualcosa come `"p2p (3 peer)"`: nessun indirizzo.
///   · `DelayDisabled` — e' il ritardo di invio **interno di SimpleTCP** (Nagle): una proprieta' del
///     filo, non del server. Un trasporto a messaggi non ce l'ha e risponde il suo valore di default.
///
/// ⭐⭐ PIU' DI UN TRASPORTO INSIEME — QUESTA INTERFACCIA E' **UN FILO**, NON "IL FILO".
/// L'architettura che arrivera' e' asimmetrica: l'host resta un client **TCP** (loopback verso il suo
/// server) mentre i peer remoti arrivano **P2P**; quindi il server, mentre gira nell'host, riceve da
/// **due fonti contemporaneamente**. Per questo **nessun singleton** e nessuna istanza globale: chi ne
/// ha bisogno ne crea due e le mette insieme con <see cref="CompositeServerTransport"/>, che tiene la
/// mappa peer -> trasporto e instrada `Send` sul filo giusto. `Server` vede **una** interfaccia e non
/// sa nemmeno che possono essere due: oggi il composito ne contiene **una** (`TcpServerTransport`),
/// domani due. Vedi il referto `scratch\REFERTO-C1B1-SEAM-SERVER.md`.
///
/// ⚠️ UN TRASPORTO DEVE ANNUNCIARE UN PEER **PRIMA** DI CONSEGNARE IL SUO PRIMO FRAME
/// (`PeerConnected`, poi i `FrameReceived`): e' l'ordine che il TCP ha gratis (il socket e' accettato
/// prima che arrivi qualunque byte) ed e' cio' che permette al composito di sapere a chi mandare la
/// risposta. Una connessione mai annunciata non e' instradabile.
/// </summary>
public interface IServerTransport
{
    /// <summary>
    /// Apre il filo sulla porta `port` e comincia a consegnare eventi.
    ///
    /// Ritorna `false` se non riesce (porta occupata, socket rifiutato) e **non lancia**: e' il
    /// contratto di `Server.Start`, che i suoi chiamanti (`Core.Main`, `HostSession.TryStart`)
    /// leggono come `bool`. Su `false` il trasporto deve restare **riutilizzabile**: un secondo
    /// `Start` deve poter riprovare da zero.
    /// </summary>
    bool Start(int port);

    /// <summary>
    /// Chiude il filo e smette di consegnare eventi. Puo' lanciare: chi chiama (il `Server`) ha il
    /// `catch` che serve per dire "il server e' fermo lo stesso". Chiamabile quando non e' partito:
    /// in quel caso non fa niente.
    /// </summary>
    void Stop();

    /// <summary>
    /// Manda a `peer` un buffer **gia' impacchettato** (`[ushort len][byte type][payload]`), senza
    /// interpretarlo: i byte che arrivano sono i byte che partono. Puo' lanciare se il peer non e'
    /// (piu') raggiungibile — vedi il contratto sulle eccezioni.
    /// </summary>
    void Send(PeerId peer, byte[] framed);

    /// <summary>
    /// Su quali indirizzi si sta ascoltando **davvero** (`"0.0.0.0"`, un elenco di IP, oppure il
    /// testo che il trasporto ritiene vero per se'). Serve al marker `[Host] ascolto su=...`.
    /// </summary>
    string ListeningAddresses();

    /// <summary>Il ritardo di invio interno del filo: spento (`true`) come oggi.</summary>
    bool DelayDisabled { get; set; }

    /// <summary>Un peer e' raggiungibile: da questo istante `Send` puo' indirizzarlo.</summary>
    event Action<PeerId> PeerConnected;

    /// <summary>Un peer non e' piu' raggiungibile. Consegnato sul thread di lettura, come oggi.</summary>
    event Action<PeerId> PeerDisconnected;

    /// <summary>
    /// Sono arrivati dei byte dal filo, **cosi' come sono usciti**: nessun riassemblaggio e nessuna
    /// interpretazione (il framing lo fa `Server.Receive`). Consegnato sul thread di lettura del
    /// trasporto, che per il TCP e' quello di `SimpleTCP`.
    /// </summary>
    event Action<PeerId, byte[]> FrameReceived;
}
