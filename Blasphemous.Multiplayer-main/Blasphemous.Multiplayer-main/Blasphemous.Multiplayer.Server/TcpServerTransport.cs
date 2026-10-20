using Blasphemous.Multiplayer.Common;
using SimpleTCP;
using System;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// ⭐⭐ L'UNICA IMPLEMENTAZIONE DI C1b-1: IL FILO TCP, AVVOLTO. Questo file contiene **le righe che
/// stavano in `Server.cs`**, spostate qui una per una: la creazione di `SimpleTcpServer`, i tre
/// eventi, la `Start`, la `Send` e la lettura degli indirizzi in ascolto. Non e' stata riscritta
/// nessuna di quelle righe e non e' stato aggiunto nessun byte al filo.
///
/// COSA RESTA FUORI, DI PROPOSITO. Il framing (`[ushort len][byte type][payload]`), la scelta della
/// funzione `Receive*` per `NetworkType`, l'autenticazione, l'host, le scene, il relay, il replay:
/// tutto **nel server**, dov'era. Qui c'e' solo "aprire un socket, dare i byte a chi li legge, e
/// scrivere i byte che mi danno" — cioe' quello che domani fara' anche un trasporto P2P.
///
/// ⚠️ L'IDENTITA' NASCE QUI, IN UN PUNTO SOLO. `SimpleTCP` chiama i suoi eventi con `e.ip`, cioe'
/// `IPEndPoint.ToString()` (`ip:porta`, misurato: `"127.0.0.1:54470"`); questo file lo converte in
/// `PeerId.FromIp(e.ip)` **senza toccare la stringa** e non lo fa mai piu' vedere a nessuno. E'
/// esattamente il punto di contatto che in C1a stava in `Server.cs`: da qui in poi il server parla
/// solo di `PeerId`, e `peer.ToString()` rende la stessa stringa, quindi la chiave del dizionario dei
/// giocatori e il testo dei log restano quelli di prima.
///
/// ⚠️ IL BIND — E' ANCORA PER-NIC NELLO STANDALONE E WILDCARD NEL CLIENT, e non e' una preferenza:
/// `SimpleTcpServer.Start(int port)` NON lega `IPAddress.Any`: enumera le NIC con
/// `OperationalStatus == Up` (`SimpleTcpServer.GetIPAddresses()`) e apre **un listener per OGNI
/// indirizzo unicast** di ognuna (`ServerListener` ctor -> `TcpListenerEx.Start()`). Quindi il
/// loopback c'e' **solo se il runtime lo elenca fra le NIC**. E' esattamente il difetto misurato nella
/// "host session": `[Host] server avviato` + rifiuto istantaneo di `127.0.0.1` (0 righe `[Server]` per
/// quel dial) mentre l'IP LAN funziona. Il dettaglio, con le prove, sta in
/// `scratch\REFERTO-AUTOCONNECT.md`.
/// ⇒ Nel client si lega ESPLICITAMENTE la wildcard IPv4: un solo listener, che copre `127.0.0.1`
///   **e** la LAN, senza dipendere da cosa il runtime elenca. Ed e' l'unica riga che cambia, perche'
///   lo standalone (`SERVER-COOP\`, `net8.0`) continua a chiamare `Start(port)` con la sua strada
///   per-NIC: **il bind del server esterno non si tocca**.
/// ⚠️ Con la wildcard il firewall di Windows mostra la richiesta di autorizzazione al primo avvio
///   (gia' documentato in `HostSession`): se la si nega, la LAN non passa ma il loopback continua a
///   funzionare, quindi l'host entra in partita lo stesso.
///
/// ⚠️ PORTE OCCUPATE — DUE ECCEZIONI, ENTRAMBE GIA' MISURATE:
///   · `SocketException` da `Start(IPAddress, int)` (il `ServerListener` lancia dentro il
///     costruttore);
///   · `InvalidOperationException("Port was already occupied for all network interfaces")` da
///     `Start(int port)`: e' il caso MISURATO, non supposto. Fino a prima del fix uno standalone
///     avviato su una porta gia' occupata moriva con un'eccezione non gestita invece di stampare
///     "Server failed to start on port", che era esattamente cio' che `Main` si aspettava.
/// Entrambe valgono `false`, e il `catch` che le legge sta qui perche' qui si apre la porta: da questo
/// punto il resto del server non vede piu' un socket.
/// </summary>
public class TcpServerTransport : IServerTransport
{
    /// <summary>
    /// L'ascoltatore vero. `null` significa "non sto ascoltando": mai avviato, oppure fermato, oppure
    /// un `Start` fallito (che lo azzera, cosi' un secondo tentativo riparte da zero invece di toccare
    /// un oggetto mai inizializzato).
    /// </summary>
    private SimpleTcpServer _server;

    /// <summary>
    /// Il valore di `DelayDisabled` da applicare al prossimo ascoltatore: `true` come oggi, cioe' il
    /// ritardo interno di SimpleTCP spento. Vive qui e non nel server perche' e' una proprieta' del
    /// filo (Nagle), non dell'engine.
    /// </summary>
    private bool _delayDisabled = true;

    /// <inheritdoc/>
    public event Action<PeerId> PeerConnected;

    /// <inheritdoc/>
    public event Action<PeerId> PeerDisconnected;

    /// <inheritdoc/>
    public event Action<PeerId, byte[]> FrameReceived;

    /// <summary>`true` se c'e' un ascoltatore vivo adesso.</summary>
    public bool IsListening
    {
        get { return _server != null; }
    }

    /// <inheritdoc/>
    public bool DelayDisabled
    {
        get
        {
            SimpleTcpServer listening = _server;
            return listening != null ? listening.DelayDisabled : _delayDisabled;
        }
        set
        {
            _delayDisabled = value;

            SimpleTcpServer listening = _server;
            if (listening != null)
                listening.DelayDisabled = value;
        }
    }

    /// <inheritdoc/>
    public bool Start(int port)
    {
        try
        {
            SimpleTcpServer listening = new SimpleTcpServer();

            // Gli stessi tre eventi di prima, sulla stessa classe: qui c'e' solo l'involucro che
            // traduce `e.ip` in un `PeerId` e lo ripete ai suoi sottoscrittori.
            listening.ClientConnected += onClientConnected;
            listening.ClientDisconnected += onClientDisconnected;
            listening.DataReceived += onDataReceived;

            // ⭐ IL BIND: wildcard nel client, invariato nello standalone (vedi il commento del tipo).
#if COOP_HOST
            listening.Start(System.Net.IPAddress.Any, port);
#else
            listening.Start(port);
#endif

            // ⚠️ `DelayDisabled = true` era la riga dopo la `Start` in `Server.Start`: stesso punto,
            // stesso effetto. Il valore viene dalla proprieta', che di default vale `true` come prima.
            listening.DelayDisabled = _delayDisabled;

            _server = listening;
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // La porta non si e' aperta: nessun ascoltatore, nessuno stato a meta'. Il chiamante
            // risponde `false` (nello standalone: "Server failed to start on port").
            _server = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            // "Port was already occupied for all network interfaces": il caso misurato, vedi sopra.
            _server = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        // Prima si stacca il campo, poi si ferma l'ascoltatore: da questo istante nessuna `Send` di
        // questo trasporto puo' partire su una connessione in smontaggio. L'ordine e' quello che
        // `Server.Stop` aveva gia' (li' il campo era il `SimpleTcpServer` stesso).
        SimpleTcpServer running = _server;
        _server = null;

        if (running == null)
            return;

        try
        {
            // ⚠️ Durante questa chiamata SimpleTCP consegna le ultime `ClientDisconnected`: gli
            // handler sono ancora attaccati di proposito, perche' le righe `Client disconnected at ...`
            // e la riassegnazione dell'host devono vedere la stessa sequenza di prima.
            running.Stop();
        }
        finally
        {
            // ...e si staccano DOPO, quando l'ascoltatore ha finito di parlare.
            running.ClientConnected -= onClientConnected;
            running.ClientDisconnected -= onClientDisconnected;
            running.DataReceived -= onDataReceived;
        }
    }

    /// <inheritdoc/>
    public void Send(PeerId peer, byte[] framed)
    {
        SimpleTcpServer sending = _server;
        if (sending == null)
            throw new InvalidOperationException("il trasporto TCP non e' avviato");

        // ⭐ IL PUNTO IN CUI L'IDENTITA' TORNA AD ESSERE IL FILO: `ToString()` -> stringa IP. E'
        // esattamente la chiamata di prima (`server.Send(peer.ToString(), list.ToArray())`), con lo
        // stesso destinatario e gli stessi byte.
        sending.Send(peer.ToString(), framed);
    }

    /// <inheritdoc/>
    public string ListeningAddresses()
    {
        try
        {
            SimpleTcpServer listening = _server;
            if (listening == null)
                return "(fermo)";

            System.Collections.Generic.List<System.Net.IPAddress> addresses = listening.GetListeningIPs();
            if (addresses == null || addresses.Count == 0)
                return "(nessun ascoltatore)";

            string[] text = new string[addresses.Count];
            for (int i = 0; i < addresses.Count; i++)
                text[i] = addresses[i].ToString();

            // Concatenazione e non `string.Join(",", addresses)`: net35 vuole l'array di stringhe, e
            // qui l'array si costruisce a mano senza LINQ (stessa resa).
            return string.Join(",", text);
        }
        catch (Exception e)
        {
            // Un guasto di lettura non deve diventare un guasto dell'host: si dice che non si sa.
            return "(illeggibile: " + e.GetType().Name + ")";
        }
    }

    /// <summary>
    /// Un client si e' connesso: `e.ip` diventa un <see cref="PeerId"/> e la stringa non viene toccata
    /// (e' la stessa che `SimpleTCP` usa per trovare il socket a cui scrivere).
    /// </summary>
    private void onClientConnected(object sender, ClientConnectionEventArgs e)
    {
        Action<PeerId> handler = PeerConnected;
        if (handler != null)
            handler(PeerId.FromIp(e.ip));
    }

    /// <summary>Un client se n'e' andato. Stesso trattamento: `e.ip` -> <see cref="PeerId"/>.</summary>
    private void onClientDisconnected(object sender, ClientConnectionEventArgs e)
    {
        Action<PeerId> handler = PeerDisconnected;
        if (handler != null)
            handler(PeerId.FromIp(e.ip));
    }

    /// <summary>
    /// Sono arrivati dei byte: si consegnano **tali e quali** (nessun riassemblaggio, nessuna
    /// interpretazione: il framing lo fa `Server.Receive`, dov'e' sempre stato).
    /// </summary>
    private void onDataReceived(object sender, DataReceivedEventArgs e)
    {
        Action<PeerId, byte[]> handler = FrameReceived;
        if (handler != null)
            handler(PeerId.FromIp(e.ip), e.data);
    }
}
