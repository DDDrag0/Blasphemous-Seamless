using Blasphemous.Multiplayer.Common;
using System;
using System.Collections.Generic;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// ⭐⭐ IL TRASPORTO CHE NE CONTIENE ALTRI — e la ragione per cui esiste e' un fatto, non un'ipotesi.
///
/// L'architettura che arrivera' e' **asimmetrica**:
///   · **Host Client** ⇒ [ TCP loopback `127.0.0.1` ] ⇒ **Host Server**: l'host **resta un client
///     TCP**, esattamente come oggi — un percorso solo, quello che i 42 frame misurano;
///   · **Peer Client** ⇒ [ **Steam P2P** ] ⇒ un adapter che **inietta** i pacchetti nel server.
/// ⇒ mentre il server gira nell'host riceve da **due fonti contemporaneamente**: il suo TCP e il P2P
///   dei peer. (E `SendP2PPacket(proprioSteamID)` e' da evitare: loopback UDP e negoziazione con se
///   stessi. Per questo l'host continua a passare dal TCP invece di mandare a se' stesso via Steam.)
///
/// ⇒ Quindi "il trasporto del server" **non e' un oggetto singolo** e non puo' esserlo: e' un
///   **registro**. Questo file e' la forma piu' semplice che regge:
///     1. **contiene** N trasporti (`Add`/`Remove`, nessun singleton);
///     2. **sa a chi appartiene ogni peer** (`_owners`: `PeerId` -> trasporto che lo ha annunciato);
///     3. **instrada** `Send(peer, ...)` sul filo che possiede quel peer;
///     4. **riversa** gli eventi di tutti i figli su un solo flusso di eventi — cosi' `Server` vede
///        **una** interfaccia e non sa nemmeno che possono essere due.
///
/// ⚠️ IN C1b-1 IL COMPOSITO CONTIENE **UN** TRASPORTO (`TcpServerTransport`): la struttura e' pronta,
/// l'uso e' uno. Il filo TCP non cambia di un byte — l'unica cosa che cambia e' che la `Send` passa da
/// una mappa prima di arrivare a `SimpleTcpServer.Send`, e la mappa contiene **quella stessa stringa**
/// (`peer.ToString()`), quindi trova **lo stesso socket** di prima.
///
/// ⚠️ NESSUNA MAGIA SUL THREADING: il composito non sposta niente. Gli eventi dei figli vengono
/// ri-emessi **sul thread che li ha consegnati** (per il TCP: il thread di lettura di `SimpleTCP`),
/// che e' la regola di `IServerTransport`.
///
/// ⚠️ LA REGOLA DELLA MAPPA: un trasporto deve annunciare un peer (`PeerConnected`) **prima** del suo
/// primo frame, e quel peer deve essere **suo**. Due trasporti che annunciano lo stesso `PeerId`
/// sarebbero un errore di chi li crea (l'ultimo annuncio vince); un `Send` verso un peer che nessuno
/// ha annunciato **lancia** — che e' quello che faceva `SimpleTcpServer.Send` con un destinatario
/// sconosciuto, e che `Server.Send` traduce nella stessa riga di sempre
/// (`"Couldn't send data to <peer>!"`).
///
/// 📌 IL P2P DI DOMANI, in una riga: `composite.Add(new P2PServerTransport(steam))` — e i peer che
/// arrivano da Steam si instradano da soli, senza toccare `Server.cs`.
/// </summary>
public class CompositeServerTransport : IServerTransport
{
    /// <summary>I trasporti registrati, nell'ordine in cui sono stati aggiunti (che e' l'ordine di avvio).</summary>
    private readonly List<IServerTransport> transports = new List<IServerTransport>();

    /// <summary>Chi possiede un peer: la mappa che rende possibile la `Send` con un solo destinatario.</summary>
    private readonly Dictionary<PeerId, IServerTransport> owners = new Dictionary<PeerId, IServerTransport>();

    /// <summary>Un "ascoltatore" per figlio: serve a potersi staccare dagli eventi con gli STESSI delegati.</summary>
    private readonly Dictionary<IServerTransport, Watcher> watchers = new Dictionary<IServerTransport, Watcher>();

    /// <inheritdoc/>
    public event Action<PeerId> PeerConnected;

    /// <inheritdoc/>
    public event Action<PeerId> PeerDisconnected;

    /// <inheritdoc/>
    public event Action<PeerId, byte[]> FrameReceived;

    /// <summary>Un composito vuoto: i trasporti si aggiungono con <see cref="Add"/>.</summary>
    public CompositeServerTransport()
    {
    }

    /// <summary>
    /// Un composito gia' pieno: `new CompositeServerTransport(new TcpServerTransport())` e' la forma
    /// che usano oggi lo standalone (`Core.Main`) e la host session (`HostSession`).
    /// </summary>
    public CompositeServerTransport(params IServerTransport[] children)
    {
        if (children == null)
            return;

        for (int i = 0; i < children.Length; i++)
            Add(children[i]);
    }

    /// <summary>Quanti fili sono registrati adesso (in C1b-1: 1).</summary>
    public int Count
    {
        get { return transports.Count; }
    }

    /// <summary>
    /// Registra un trasporto e si mette in ascolto dei suoi eventi. Non lo avvia: lo avvia
    /// <see cref="Start"/>, tutti insieme, sulla stessa porta.
    /// ⚠️ Aggiungere due volte lo stesso oggetto non fa niente (non si duplicano gli eventi).
    /// </summary>
    public void Add(IServerTransport transport)
    {
        if (transport == null)
            throw new ArgumentNullException(nameof(transport));

        if (watchers.ContainsKey(transport))
            return;

        Watcher watcher = new Watcher(this, transport);
        watchers.Add(transport, watcher);
        transports.Add(transport);

        transport.PeerConnected += watcher.OnPeerConnected;
        transport.PeerDisconnected += watcher.OnPeerDisconnected;
        transport.FrameReceived += watcher.OnFrameReceived;
    }

    /// <summary>
    /// Toglie un trasporto: si stacca dai suoi eventi e dimentica i peer che aveva annunciato.
    /// ⚠️ NON lo ferma: la vita dell'oggetto e' di chi l'ha creato (qui non si chiudono porte che non
    /// abbiamo aperto in questo momento). `Stop()` ferma quello che `Start()` ha avviato.
    /// </summary>
    public bool Remove(IServerTransport transport)
    {
        if (transport == null)
            return false;

        Watcher watcher;
        if (!watchers.TryGetValue(transport, out watcher))
            return false;

        transport.PeerConnected -= watcher.OnPeerConnected;
        transport.PeerDisconnected -= watcher.OnPeerDisconnected;
        transport.FrameReceived -= watcher.OnFrameReceived;

        watchers.Remove(transport);
        transports.Remove(transport);

        // I peer di quel filo non sono piu' raggiungibili da qui.
        List<PeerId> orphaned = new List<PeerId>();
        foreach (KeyValuePair<PeerId, IServerTransport> pair in owners)
        {
            if (pair.Value == transport)
                orphaned.Add(pair.Key);
        }
        for (int i = 0; i < orphaned.Count; i++)
            owners.Remove(orphaned[i]);

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Avvia **tutti** i figli sulla stessa porta. Se uno non parte, quelli gia' partiti vengono
    /// fermati (in ordine inverso) e si ritorna `false`: un composito mezzo acceso sarebbe una porta
    /// aperta che nessuno ha chiesto. Un figlio che **lancia** da `Start` conta come "non partito",
    /// perche' il contratto di `IServerTransport.Start` e' di non lanciare e questa e' la cintura.
    /// </remarks>
    public bool Start(int port)
    {
        if (transports.Count == 0)
        {
            // Nessun filo, nessun ascolto: dire `true` qui vorrebbe dire "sto ascoltando" su niente.
            return false;
        }

        List<IServerTransport> started = new List<IServerTransport>();

        for (int i = 0; i < transports.Count; i++)
        {
            IServerTransport transport = transports[i];

            bool ok;
            try
            {
                ok = transport.Start(port);
            }
            catch (Exception)
            {
                ok = false;
            }

            if (!ok)
            {
                stopAll(started);
                return false;
            }

            started.Add(transport);
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Ferma i figli in ordine **inverso** all'avvio. Un figlio che lancia non impedisce di fermare gli
    /// altri: si fermano tutti, poi si rilancia il primo guasto (uno solo: `Server.Stop` ne stampa una
    /// riga sola, come prima).
    /// </remarks>
    public void Stop()
    {
        Exception first = stopAll(transports);

        owners.Clear();

        if (first != null)
            throw first;
    }

    /// <inheritdoc/>
    public void Send(PeerId peer, byte[] framed)
    {
        IServerTransport owner;
        if (!owners.TryGetValue(peer, out owner) || owner == null)
            throw new InvalidOperationException("nessun trasporto conosce il peer " + peer.ToString());

        owner.Send(peer, framed);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Con **un** trasporto (C1b-1) la stringa e' quella di quel trasporto, cioe' il marker
    /// `[Host] ascolto su=0.0.0.0` di oggi, identico. Con piu' trasporti si mettono in fila, separati
    /// da virgola, cosi' la riga dice tutto quello che c'e' da sapere.
    /// </remarks>
    public string ListeningAddresses()
    {
        if (transports.Count == 0)
            return "(nessun trasporto)";

        string[] parts = new string[transports.Count];
        for (int i = 0; i < transports.Count; i++)
        {
            try
            {
                parts[i] = transports[i].ListeningAddresses();
            }
            catch (Exception e)
            {
                parts[i] = "(illeggibile: " + e.GetType().Name + ")";
            }
        }

        if (parts.Length == 1)
            return parts[0];

        return string.Join(",", parts);
    }

    /// <summary>
    /// Il ritardo di invio si spegne su **tutti** i fili; in lettura vale `true` solo se ce l'hanno
    /// spento tutti (nessun filo = `true`, che e' il default storico).
    /// </summary>
    public bool DelayDisabled
    {
        get
        {
            bool all = true;
            for (int i = 0; i < transports.Count; i++)
                all = all && transports[i].DelayDisabled;
            return all;
        }
        set
        {
            for (int i = 0; i < transports.Count; i++)
                transports[i].DelayDisabled = value;
        }
    }

    /// <summary>Ferma i trasporti in ordine inverso e ritorna il primo guasto (o `null`).</summary>
    private static Exception stopAll(List<IServerTransport> list)
    {
        Exception first = null;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            try
            {
                list[i].Stop();
            }
            catch (Exception e)
            {
                if (first == null)
                    first = e;
            }
        }

        return first;
    }

    /// <summary>Il peer che ha annunciato `transport` ora e' suo.</summary>
    private void registerOwner(PeerId peer, IServerTransport transport)
    {
        owners[peer] = transport;
    }

    /// <summary>Il peer ha lasciato il filo: si smette di instradarlo.</summary>
    private void forgetOwner(PeerId peer)
    {
        owners.Remove(peer);
    }

    private void raisePeerConnected(PeerId peer)
    {
        Action<PeerId> handler = PeerConnected;
        if (handler != null)
            handler(peer);
    }

    private void raisePeerDisconnected(PeerId peer)
    {
        Action<PeerId> handler = PeerDisconnected;
        if (handler != null)
            handler(peer);
    }

    private void raiseFrameReceived(PeerId peer, byte[] data)
    {
        Action<PeerId, byte[]> handler = FrameReceived;
        if (handler != null)
            handler(peer, data);
    }

    /// <summary>
    /// Il ponte fra un figlio e il composito: tiene il figlio e sa dire da chi arriva ogni evento.
    /// I delegati si creano **una volta** qui, cosi' `Add`/`Remove` si accoppiano davvero.
    /// </summary>
    private sealed class Watcher
    {
        private readonly CompositeServerTransport _parent;
        private readonly IServerTransport _transport;

        public Watcher(CompositeServerTransport parent, IServerTransport transport)
        {
            _parent = parent;
            _transport = transport;
        }

        /// <summary>
        /// ⚠️ L'ORDINE: prima si registra il proprietario, poi si annuncia. Un handler che risponde
        /// subito al nuovo arrivato (`Server.clientConnected` oggi scrive solo una riga, ma domani
        /// potrebbe mandare un frame) deve trovare la strada gia' aperta.
        /// </summary>
        public void OnPeerConnected(PeerId peer)
        {
            _parent.registerOwner(peer, _transport);
            _parent.raisePeerConnected(peer);
        }

        /// <summary>
        /// ⚠️ E QUI L'ORDINE E' L'INVERSO, DI PROPOSITO: si annuncia PRIMA e si dimentica DOPO.
        /// Il gestore della disconnessione del server risponde ai *rimanenti* ma puo' anche provare a
        /// scrivere a chi se n'e' appena andato (lo fa, dentro `sendPlayerConnection`); con la mappa
        /// ancora popolata quella scrittura arriva al trasporto vero, che si comporta — e fallisce —
        /// esattamente come faceva `SimpleTcpServer.Send` un istante prima di questa modifica. Togliere
        /// la mappa prima cambierebbe *da dove* arriva l'errore, non solo quando.
        /// </summary>
        public void OnPeerDisconnected(PeerId peer)
        {
            _parent.raisePeerDisconnected(peer);
            _parent.forgetOwner(peer);
        }

        public void OnFrameReceived(PeerId peer, byte[] data)
        {
            _parent.raiseFrameReceived(peer, data);
        }
    }
}
