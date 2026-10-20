using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Network;
using Blasphemous.Multiplayer.Common;
using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Host
{
    /// <summary>
    /// ⭐⭐ L'ADAPTER P2P VERO — l'unica classe (con `SteamP2PTransportImpl`) che tocca `SteamNetworking`
    /// dal lato **server**, e l'unica che sa trasformare un pacchetto di Steam in un frame per
    /// `Server.Receive`.
    ///
    /// COSA FA, IN QUATTRO RIGHE:
    ///   ① `Start` — nessun listener: si registra `P2PSessionRequest_t` e si accetta **solo** chi e' nella
    ///     lobby (`SteamLobbyImpl.IsLobbyMember`, §9);
    ///   ② `Tick` — svuota i tre canali; l'HELLO di un peer lo **annuncia** (`PeerConnected`), un DATA lo
    ///     **inietta** nel server (`FrameReceived`), il BYE o il timeout lo **toglie** (`PeerDisconnected`);
    ///   ③ `Send(peer, framed)` — costruisce il pacchetto e lo **accoda** (la `Send` puo' arrivare dal
    ///     thread del TCP: Steam si chiama solo nel `Tick`, sul main thread);
    ///   ④ e **conserva l'header** dei pacchetti inoltrati (vedi sotto).
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────────────────
    /// ⭐⭐ L'HEADER CONSERVATO — il punto delicato di tutto l'adapter
    ///
    /// Un frame che arriva dal peer A non e' "un frame dell'host": e' un frame di **A inoltrato**. Se
    /// l'adapter lo rimandasse a B con il **proprio** id e il **proprio** contatore, due cose andrebbero
    /// storte insieme:
    ///   · B vedrebbe tutti i giocatori come "l'host", e la sequenza del canale 0 diventerebbe **una
    ///     sola** per tutti: un pacchetto di A appena arrivato farebbe scartare come "vecchio" il
    ///     pacchetto di C arrivato dopo;
    ///   · il mittente VERO andrebbe perso, e con lui la possibilita' di distinguere i flussi.
    ///
    /// ⇒ quando il server inoltra un frame **dentro** `FrameReceived`, la classe, l'origine e il
    /// contatore sono quelli del pacchetto ricevuto (`Relay` qui sotto, `[ThreadStatic]` perche' il
    /// thread del TCP non deve mai vedere il contesto del thread principale). Il contatore d'origine
    /// viene **avanzato** dall'adapter (uno per frame inoltrato, a partire dalla sequenza ricevuta):
    /// cosi' B vede una sequenza strettamente crescente per A, e nessun frame inoltrato viene scartato.
    ///
    /// ⚠️ E I FRAME CHE IL SERVER GENERA DA SOLO (Intro, HostAssign, Connection, replay, e
    /// Position/Animation/Direction ri-codificati dal suo `PlayerInfo`) non hanno nessun contesto: per
    /// loro la classe la dice `TransportClassTable` — la tabella di `§10`, con le tre ambiguita'
    /// risolte leggendo il byte che le distingue.
    ///
    /// ⚠️ E IL SERVER NON LO SA: `Server.cs` chiama `_transport.Send(peer, framed)` e basta, come per il
    /// TCP. Nessuna riga del server e' cambiata per questo file.
    /// </summary>
    internal static class P2PServerTransportImpl
    {
        /// <summary>Un peer che non manda piu' niente da 5 s non c'e' piu' (stessa soglia del lato peer).</summary>
        private const float TIMEOUT_SECONDS = 5f;

        /// <summary>Quanti pacchetti si leggono al massimo per canale per frame.</summary>
        private const int MAX_PACKETS_PER_FRAME = 64;

        /// <summary>Il tetto del buffer di lettura (una skin PNG ci sta; oltre si tronca).</summary>
        private const uint MAX_PACKET_BYTES = 256u * 1024u;

        /// <summary>Quanti pacchetti in uscita si tengono in coda (il flush e' ogni frame).</summary>
        private const int MAX_OUTBOUND = 1024;

        private const string MarkerStart = "[Net] p2p ascolto: ";
        private const string MarkerStop = "[Net] p2p: adapter fermo";
        private const string MarkerHello = "[Net] p2p peer connesso: ";
        private const string MarkerGone = "[Net] p2p peer disconnesso: ";
        private const string MarkerTimeout = "[Net] p2p timeout peer=";
        private const string MarkerSendFail = "[Net] p2p invio fallito: ";

        // ─── STATO ──────────────────────────────────────────────────────────────────────────────────
        private static readonly object _gate = new object();
        private static readonly Dictionary<ulong, PeerState> _peers = new Dictionary<ulong, PeerState>();
        private static readonly Queue<Outbound> _outbound = new Queue<Outbound>();
        private static readonly HashSet<ulong> _acceptLogged = new HashSet<ulong>();
        private static readonly HashSet<ulong> _foreignLogged = new HashSet<ulong>();

        private static bool _running;
        private static bool _registered;
        private static bool _registerFailed;
        private static uint _ownSeq;
        private static ulong _myId;
        private static byte[] _rxBuffer = new byte[1024];
        private static int _outboundDropped;
        private static int _outboundFailLogged;

        private static Action<PeerId> _onConnected;
        private static Action<PeerId> _onDisconnected;
        private static Action<PeerId, byte[]> _onFrame;

        // ─── IL CONTESTO DI INOLTRO, PER THREAD (`[ThreadStatic]`: il TCP non lo vede mai) ──────────
        [ThreadStatic] private static Relay _relay;

        /// <summary>Il sink della facciata: eventi consegnati sul thread di `Tick` (il main thread).</summary>
        internal static void SetSink(Action<PeerId> onConnected, Action<PeerId> onDisconnected,
                                     Action<PeerId, byte[]> onFrame)
        {
            _onConnected = onConnected;
            _onDisconnected = onDisconnected;
            _onFrame = onFrame;
        }

        /// <summary>"Sta ascoltando adesso?" (per il log e per il referto).</summary>
        internal static bool Listening
        {
            get { return _running && _registered; }
        }

        /// <summary>La riga del composito per il marker `[Host] ascolto su=...`.</summary>
        internal static string ListeningAddresses()
        {
            int peers;
            lock (_gate)
                peers = _peers.Count;

            return "p2p (" + peers + " peer)";
        }

        /// <summary>
        /// ⭐ `Start(port)` — **la porta si ignora**, e si dichiara: il P2P non ha un listener, non c'e'
        /// nessuno che bussa. Cio' che si fa e' registrare la callback che **accetta** le sessioni; senza
        /// di lei ogni pacchetto del peer viene scartato in silenzio.
        ///
        /// Ritorna `true` anche senza Steam: un'aggiunta non disponibile non deve impedire al TCP di
        /// ascoltare (e il composito, se un figlio dice `false`, ferma TUTTO).
        /// </summary>
        internal static bool Start(int port)
        {
            try
            {
                if (!SteamAPI.IsSteamRunning() || !SteamUser.BLoggedOn())
                {
                    ModLog.Warn(MarkerStart + "Steam non sta girando o nessun account: l'adapter resta " +
                                "inerte (il TCP ascolta lo stesso)");
                    return true;
                }

                if (!EnsureRegistered())
                {
                    ModLog.Warn(MarkerStart + "callback P2PSessionRequest_t non registrata: nessun peer " +
                                "P2P raggiungera' il server (il TCP lo raggiunge lo stesso)");
                    return true;
                }

                lock (_gate)
                {
                    _peers.Clear();
                    _outbound.Clear();
                    _ownSeq = 0u;
                    _outboundDropped = 0;
                    _outboundFailLogged = 0;
                }

                _acceptLogged.Clear();
                _foreignLogged.Clear();
                _relay = null;

                // ⚠️ L'ID SI LEGGE QUI (main thread) E SI TIENE: `Send` puo' arrivare dal thread del TCP
                // (il server inoltra una posizione dell'host mentre quel thread legge), e una chiamata
                // nativa di Steam da li' e' il tipo di guasto che non si vede finche' non si vede.
                _myId = SteamUser.GetSteamID().m_SteamID;

                _running = true;

                ModLog.Info(MarkerStart + "porta=" + port + " (ignorata: il P2P non ha un listener) " +
                            "io=" + _myId + " canali=0,1,2");
                return true;
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerStart + "errore: " + e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        /// <summary>Chiude le sessioni dei peer e smette di consegnare eventi. Idempotente, non lancia.</summary>
        internal static void Stop()
        {
            List<ulong> peers;
            lock (_gate)
            {
                peers = new List<ulong>(_peers.Keys);
                _peers.Clear();
                _outbound.Clear();
                _running = false;
            }

            for (int i = 0; i < peers.Count; i++)
            {
                try
                {
                    SteamNetworking.CloseP2PSessionWithUser(new CSteamID(peers[i]));
                }
                catch (Exception)
                {
                    // Una sessione che non si chiude non deve impedire di fermare l'adapter.
                }
            }

            if (peers.Count > 0 || _registered)
                ModLog.Info(MarkerStop + " peer=" + peers.Count);
        }

        /// <summary>
        /// ⭐ INOLTRA UN FRAME A UN PEER — chiamata dal server (anche dal thread del TCP). Costruisce il
        /// pacchetto e lo accoda: la chiamata nativa la fa il `Tick`, sul main thread.
        ///
        /// Ritorna `false` se il peer non e' (piu') annunciato: la facciata lo traduce in un'eccezione,
        /// che e' il comportamento del TCP quando il destinatario non c'e' piu'.
        /// </summary>
        internal static bool Send(ulong steamId, byte[] framed)
        {
            if (steamId == 0UL)
                return false;

            Relay relay = _relay;
            TransportClass cls;
            int channel;
            ulong origin;
            uint seq;

            lock (_gate)
            {
                if (!_running)
                    return false;

                PeerState peer;
                if (!_peers.TryGetValue(steamId, out peer) || !peer.Announced)
                    return false;

                if (relay != null && relay.Active)
                {
                    // ⭐ IL FRAME INOLTRATO CONSERVA LA SUA ORIGINE (vedi il commento di classe): l'id e'
                    // quello del mittente vero, il contatore e' quello del suo flusso, avanzato di uno per
                    // ogni frame inoltrato cosi' il ricevente non lo scarta come "vecchio".
                    cls = relay.Class;
                    origin = relay.Origin;

                    if (cls == TransportClass.Unreliable)
                    {
                        PeerState from = Origin(origin);
                        from.RelaySeq++;
                        seq = from.RelaySeq;
                    }
                    else
                    {
                        seq = 0u;
                    }
                }
                else
                {
                    // Un frame che nessun peer ha mandato: la classe la dice la tabella di §10.
                    cls = TransportClassTable.ForFrame(framed);
                    origin = _myId;
                    seq = cls == TransportClass.Unreliable ? NextOwnSeq() : 0u;
                }

                byte type = framed.Length > 2 ? framed[2] : (byte)0;
                channel = P2PWire.ChannelFor(cls, type);

                byte[] packet = cls == TransportClass.Unreliable
                    ? P2PWire.BuildData(framed, cls, origin, seq)
                    : P2PWire.BuildData(framed, cls, origin, 0u);

                if (_outbound.Count >= MAX_OUTBOUND)
                {
                    _outbound.Dequeue();
                    _outboundDropped++;

                    if (_outboundDropped <= 3)
                        ModLog.Warn(MarkerSendFail + "coda piena (" + MAX_OUTBOUND + " pacchetti): il piu' " +
                                    "vecchio e' stato scartato");
                }

                _outbound.Enqueue(new Outbound(steamId, packet, channel, cls == TransportClass.Reliable));
                return true;
            }
        }

        /// <summary>
        /// ⭐⭐ IL CICLO PER FRAME: ① i tre canali (consegna al server), ② il flush di quello che il server
        /// ha scritto, ③ i timeout dei peer.
        ///
        /// L'ordine conta: prima si **riceve** (un pacchetto arrivato in questo frame deve poter
        /// alimentare il server prima che le risposte partano), poi si **manda**, poi si guarda chi non
        /// manda piu' niente.
        /// </summary>
        internal static void Tick()
        {
            if (!_running)
                return;

            try
            {
                DrainChannel(P2PWire.ChannelUnreliable);
                DrainChannel(P2PWire.ChannelReliable);
                DrainChannel(P2PWire.ChannelHeavy);

                Flush();

                CheckTimeouts();
            }
            catch (Exception e)
            {
                ModLog.Error("[Net] p2p errore in Tick: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ─── RICEZIONE ─────────────────────────────────────────────────────────────────────────────

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

                if (read == 0u || remote.m_SteamID == 0UL)
                    continue;

                Handle(channel, remote, _rxBuffer, (int)read);
            }
        }

        /// <summary>
        /// Un pacchetto: HELLO = annuncio, DATA = iniezione, BYE = addio. Un pacchetto **estraneo** (il byte
        /// della sonda `SteamP2PProbe`, che vive sullo stesso canale 0) si conta e si scarta.
        /// </summary>
        private static void Handle(int channel, CSteamID remote, byte[] packet, int length)
        {
            byte kind;
            int ignoredOffset;
            uint ignoredSeq;
            ulong ignoredSender;

            if (!P2PWire.TryParse(packet, length, out kind, out ignoredOffset, out ignoredSender, out ignoredSeq))
            {
                ulong id = remote.m_SteamID;
                bool first;
                lock (_gate)
                    first = _foreignLogged.Add(id);

                if (first)
                    ModLog.Warn("[Net] p2p pacchetto estraneo da " + id + " canale=" + channel +
                                " (" + length + " byte): scartato (sonda P2P accesa?)");
                return;
            }

            ulong steamId = remote.m_SteamID;

            // Chiunque mandi un pacchetto valido e' vivo: il timeout si misura su questo.
            Touch(steamId);

            switch (kind)
            {
                case P2PWire.KindHello:
                    Announce(steamId);
                    return;

                case P2PWire.KindKeepAlive:
                    return;

                case P2PWire.KindBye:
                    Drop(steamId, "ha mandato BYE");
                    return;

                case P2PWire.KindData:
                {
                    ulong origin;
                    uint seq;
                    int payloadOffset;

                    if (!P2PWire.TryParseData(packet, length, channel, out origin, out seq, out payloadOffset))
                        return;

                    if (!IsAnnounced(steamId))
                    {
                        // ⚠️ La regola del contratto: un peer non annunciato non e' instradabile — e senza
                        // hello non si sa nemmeno che stia parlando con noi. Si dice una volta per peer.
                        bool first;
                        lock (_gate)
                            first = _acceptLogged.Add(steamId);

                        if (first)
                            ModLog.Warn("[Net] p2p frame da " + steamId + " PRIMA dell'hello: scartato " +
                                        "(il peer deve mandare l'hello per primo)");
                        return;
                    }

                    int count = length - payloadOffset;
                    if (count <= 0)
                        return;

                    byte[] framed = new byte[count];
                    Buffer.BlockCopy(packet, payloadOffset, framed, 0, count);

                    // ⭐ IL CONTESTO: da qui fino alla fine della chiamata, ogni `Send` del server e' un
                    // inoltro di QUESTO pacchetto. Vedi il commento di classe.
                    Relay previous = _relay;
                    _relay = new Relay
                    {
                        Active = true,
                        Class = channel == P2PWire.ChannelUnreliable ? TransportClass.Unreliable : TransportClass.Reliable,
                        Origin = channel == P2PWire.ChannelUnreliable ? origin : steamId,
                    };

                    try
                    {
                        Action<PeerId, byte[]> handler = _onFrame;
                        if (handler != null)
                            handler(PeerId.FromSteam(steamId), framed);
                    }
                    finally
                    {
                        _relay = previous;
                    }

                    return;
                }
            }
        }

        /// <summary>Annulla un peer e (una volta sola) lo dice al server.</summary>
        private static void Announce(ulong steamId)
        {
            PeerState state;
            bool isNew;

            lock (_gate)
            {
                if (!_peers.TryGetValue(steamId, out state))
                {
                    state = new PeerState();
                    _peers[steamId] = state;
                }

                isNew = !state.Announced;
                state.Announced = true;
                state.LastRx = Time.realtimeSinceStartup;
            }

            if (isNew)
            {
                ModLog.Info(MarkerHello + steamId + " (hello ricevuto)");
                Action<PeerId> handler = _onConnected;
                if (handler != null)
                    handler(PeerId.FromSteam(steamId));
            }
        }

        /// <summary>Toglie un peer, **prima** di annunciarlo: cosi' una scritta del server verso chi se n'e'
        /// appena andato fallisce come falliva `SimpleTcpServer.Send`.</summary>
        private static void Drop(ulong steamId, string reason)
        {
            bool existed;
            lock (_gate)
                existed = _peers.Remove(steamId);

            if (!existed)
                return;

            ModLog.Info(MarkerGone + steamId + " (" + reason + ")");

            Action<PeerId> handler = _onDisconnected;
            if (handler != null)
                handler(PeerId.FromSteam(steamId));

            try
            {
                SteamNetworking.CloseP2PSessionWithUser(new CSteamID(steamId));
            }
            catch (Exception)
            {
                // Una sessione che non si chiude non deve impedire di dimenticare il peer.
            }
        }

        private static void Touch(ulong steamId)
        {
            lock (_gate)
            {
                PeerState state;
                if (_peers.TryGetValue(steamId, out state))
                    state.LastRx = Time.realtimeSinceStartup;
            }
        }

        private static bool IsAnnounced(ulong steamId)
        {
            lock (_gate)
            {
                PeerState state;
                return _peers.TryGetValue(steamId, out state) && state.Announced;
            }
        }

        private static void CheckTimeouts()
        {
            if (_peers.Count == 0)
                return;

            List<ulong> gone = null;
            float now = Time.realtimeSinceStartup;

            lock (_gate)
            {
                foreach (KeyValuePair<ulong, PeerState> pair in _peers)
                {
                    if (now - pair.Value.LastRx < TIMEOUT_SECONDS)
                        continue;

                    if (gone == null)
                        gone = new List<ulong>();

                    gone.Add(pair.Key);
                }
            }

            if (gone == null)
                return;

            for (int i = 0; i < gone.Count; i++)
            {
                ModLog.Error(MarkerTimeout + gone[i] + " nessun pacchetto da " + (int)TIMEOUT_SECONDS +
                             " s (sessione P2P persa)");
                Drop(gone[i], "timeout");
            }
        }

        // ─── INVIO ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Svuota la coda. E' **l'unico punto** in cui questo adapter chiama Steam: gira sul main thread
        /// (dal `Tick`), quindi una `Send` arrivata dal thread del TCP non e' mai una chiamata nativa da
        /// un thread sbagliato.
        /// </summary>
        private static void Flush()
        {
            int guard = MAX_OUTBOUND;
            Outbound item = null;

            while (guard-- > 0)
            {
                lock (_gate)
                {
                    if (_outbound.Count == 0)
                        break;

                    item = _outbound.Dequeue();
                }

                if (item == null)
                    continue;

                EP2PSend mode = item.Reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;

                bool sent;
                try
                {
                    sent = SteamNetworking.SendP2PPacket(new CSteamID(item.SteamId), item.Packet,
                                                        (uint)item.Packet.Length, mode, item.Channel);
                }
                catch (Exception e)
                {
                    ModLog.Error(MarkerSendFail + "eccezione: " + e.GetType().Name + ": " + e.Message);
                    continue;
                }

                if (!sent)
                {
                    _outboundFailLogged++;
                    if (_outboundFailLogged <= 3)
                        ModLog.Warn(MarkerSendFail + "peer=" + item.SteamId + " canale=" + item.Channel +
                                    ": SendP2PPacket ha risposto false (buffer di invio pieno), perso");
                }
            }
        }

        // ─── LA CALLBACK CHE ACCETTA LE SESSIONI ───────────────────────────────────────────────────

        /// <summary>
        /// `P2PSessionRequest_t` => `AcceptP2PSessionWithUser`, **solo** per i membri della lobby (§9).
        /// Senza questa callback ogni pacchetto del peer viene scartato in silenzio e il sintomo sarebbe
        /// `"Client connected"` che non arriva mai, senza una riga che lo spieghi.
        /// </summary>
        private static void OnSessionRequest(P2PSessionRequest_t request)
        {
            try
            {
                CSteamID remote = request.m_steamIDRemote;
                ulong id = remote.m_SteamID;

                if (id == 0UL)
                    return;

                if (!SteamLobbyImpl.IsLobbyMember(remote))
                {
                    ModLog.Warn("[Net] p2p sessione rifiutata: " + id + " non e' un membro della lobby");
                    return;
                }

                bool accepted = SteamNetworking.AcceptP2PSessionWithUser(remote);

                bool first;
                lock (_gate)
                    first = _acceptLogged.Add(id);

                if (!first)
                    return;

                if (accepted)
                    ModLog.Info("[Net] p2p sessione accettata: " + id + " (membro della lobby)");
                else
                    ModLog.Info("[Net] p2p sessione con " + id + " gia' presente (Accept ha risposto false)");
            }
            catch (Exception e)
            {
                ModLog.Error("[Net] p2p errore in P2PSessionRequest_t: " + e.GetType().Name + ": " + e.Message);
            }
        }

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
                return true;
            }
            catch (Exception e)
            {
                _registerFailed = true;
                ModLog.Error("[Net] p2p Callback<P2PSessionRequest_t>.Create: " + e.GetType().Name + ": " +
                             e.Message);
                return false;
            }
        }

        /// <summary>⚠️ CAMPO E NON LOCALE: `Callback&lt;T&gt;.Create` tiene un `GCHandle` sul delegato, e una
        /// variabile locale sparirebbe dopo il ritorno (le callback smetterebbero di arrivare in silenzio).</summary>
        private static Callback<P2PSessionRequest_t> _cbSessionRequest;

        /// <summary>
        /// Il prossimo numero di sequenza dei frame che il server genera (il proprio flusso): un contatore
        /// solo, sotto `_gate`, cosi' due frame generati da thread diversi non possono avere la stessa
        /// sequenza. Il ricevente scarta la sequenza **piu' vecchia**, non quella uguale: la monotonia qui
        /// e' una garanzia, non una speranza.
        /// </summary>
        private static uint NextOwnSeq()
        {
            _ownSeq++;
            return _ownSeq;
        }

        /// <summary>Lo stato di un peer per il relay: la sua sequenza d'uscita e l'ultimo battito.</summary>
        private static PeerState Origin(ulong steamId)
        {
            PeerState state;
            if (!_peers.TryGetValue(steamId, out state))
            {
                state = new PeerState();
                _peers[steamId] = state;
            }

            return state;
        }

        private sealed class PeerState
        {
            internal bool Announced;
            internal float LastRx;

            /// <summary>L'ultima sequenza usata per inoltrare i frame di questo peer (canale 0).</summary>
            internal uint RelaySeq;
        }

        private sealed class Relay
        {
            internal bool Active;
            internal TransportClass Class;
            internal ulong Origin;
        }

        private sealed class Outbound
        {
            internal readonly ulong SteamId;
            internal readonly byte[] Packet;
            internal readonly int Channel;
            internal readonly bool Reliable;

            internal Outbound(ulong steamId, byte[] packet, int channel, bool reliable)
            {
                SteamId = steamId;
                Packet = packet;
                Channel = channel;
                Reliable = reliable;
            }
        }
    }
}
