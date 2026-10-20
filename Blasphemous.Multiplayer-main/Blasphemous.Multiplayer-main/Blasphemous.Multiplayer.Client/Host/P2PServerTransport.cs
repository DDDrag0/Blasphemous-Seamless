using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Network;
using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Server;
using System;

namespace Blasphemous.Multiplayer.Client.Host
{
    /// <summary>
    /// ⭐⭐ L'ADAPTER P2P DEL SERVER — il secondo filo del composito, accanto al TCP.
    ///
    /// COSA FA: riceve i pacchetti P2P dei peer e li **inietta** nel server con gli **stessi tre eventi**
    /// del `IServerTransport` (`PeerConnected`, `FrameReceived`, `PeerDisconnected`), e manda le risposte
    /// con `SendP2PPacket`. Il server **non cambia una riga**: non sa che esiste un secondo filo, non sa
    /// che esiste Steam, e `Server.Receive` legge i byte esattamente come li legge dal TCP.
    ///
    /// ⚠️ E L'HOST RESTA UN CLIENT **TCP** SUL LOOPBACK. Questo adapter non tocca il percorso dell'host:
    /// il suo client continua a connettersi a `127.0.0.1:27051` con `TcpTransport`, che e' l'unico dei 42
    /// frame. `SendP2PPacket(proprioSteamID)` sarebbe loopback UDP e negoziazione con se stessi: non si fa.
    ///
    /// 🔴 PERCHE' DUE CLASSI (`P2PServerTransport` + `P2PServerTransportImpl`) E NON UNA — la stessa
    /// regola di `SteamP2PTransport`: **solo le `*Impl` nominano `Steamworks`**, perche' su una copia
    /// non-Steam quei tipi possono mancare e la `TypeLoadException` arriva **al punto di chiamata**.
    /// Questa classe non nomina nessun tipo `Steamworks`; ogni chiamata all'implementazione sta in un
    /// metodo suo, dentro un `try/catch`, dopo il cancello `SteamLobby.Available`. Se Steam non c'e',
    /// <see cref="Prepare"/> risponde `false` e il composito resta con **il solo TCP** — cioe' esattamente
    /// com'era in C1b-1.
    ///
    /// ⚠️ IL THREAD: i packet P2P si leggono in un ciclo per frame (`Tick`, sul main thread), e le
    /// `Send` possono arrivare **anche dal thread di lettura del TCP** (una posizione dell'host inoltrata
    /// al peer). Per questo `Send` **non chiama Steam**: costruisce il pacchetto e lo accoda, e il flush
    /// avviene in `Tick`. Una chiamata nativa di Steam dal thread sbagliato e' il tipo di guasto che non
    /// si vede finche' non si vede.
    /// </summary>
    internal sealed class P2PServerTransport : IServerTransport
    {
        /// <summary>Per il log: il marker dell'ascolto P2P nel composito.</summary>
        internal const string MarkerListen = "[Net] p2p ascolto: ";

        /// <summary>Il marker dell'annuncio di un peer (l'equivalente di "Client connected at").</summary>
        internal const string MarkerPeerConnected = "[Net] p2p peer connesso: ";

        /// <summary>Il marker della disconnessione.</summary>
        internal const string MarkerPeerDisconnected = "[Net] p2p peer disconnesso: ";

        /// <summary>Il marker dell'iniezione di un frame nel server.</summary>
        internal const string MarkerFrame = "[Net] p2p frame: ";

        /// <summary>Il marker di un guasto catturato al confine.</summary>
        internal const string MarkerError = "[Net] p2p errore: ";

        private static bool _failureLogged;

        /// <summary>
        /// "QUESTO FILO SI PUO' AGGIUNGERE?" — chiesto da `HostSession` **prima** di `Add`, cosi' su una
        /// copia non-Steam il composito resta a **un** trasporto (identico a C1b-1) invece di avere un
        /// figlio che non fa niente. Non lancia.
        /// </summary>
        internal static bool Prepare()
        {
            if (!SteamLobby.Available)
            {
                ModLog.Info(MarkerListen + "non aggiunto: Steam non disponibile (solo TCP)");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Collega gli eventi dell'implementazione a quelli di **questa** istanza (il composito si
        /// sottoscrive all'istanza). Una sola istanza per sessione host: una nuova `HostSession` la
        /// sostituisce, e la precedente e' gia' stata fermata.
        /// </summary>
        internal P2PServerTransport()
        {
            if (!SteamLobby.Available)
                return;

            try
            {
                CallSetSink(RaisePeerConnected, RaisePeerDisconnected, RaiseFrameReceived);
            }
            catch (Exception e)
            {
                Report("SetSink", e);
            }
        }

        public event Action<PeerId> PeerConnected;
        public event Action<PeerId> PeerDisconnected;
        public event Action<PeerId, byte[]> FrameReceived;

        /// <summary>
        /// La porta **non si usa**: il P2P non ha un listener, non c'e' nessuno che bussa. Le sessioni si
        /// accettano per peer (`AcceptP2PSessionWithUser` nella callback). Ritorna `true` anche quando
        /// Steam non c'e': fermare l'host perche' un'aggiunta non e' disponibile sarebbe la coda che
        /// scodinzola il cane — il TCP e' la BASE, e deve partire lo stesso.
        /// </summary>
        public bool Start(int port)
        {
            if (!SteamLobby.Available)
            {
                ModLog.Info(MarkerListen + "Steam non disponibile: l'adapter resta inerte, il TCP ascolta");
                return true;
            }

            try
            {
                return CallStart(port);
            }
            catch (Exception e)
            {
                Report("Start", e);
                return true;
            }
        }

        public void Stop()
        {
            if (!SteamLobby.Available)
                return;

            try
            {
                CallStop();
            }
            catch (Exception e)
            {
                Report("Stop", e);
            }
        }

        /// <summary>
        /// Invia un frame **gia' impacchettato** a un peer Steam. Lancia se il peer non e' Steam o non e'
        /// piu' annunciato: e' lo stesso contratto del TCP, dove `SimpleTcpServer.Send` lancia su un
        /// destinatario che non c'e' e `Server.Send` lo traduce in `"Couldn't send data to X!"`.
        /// </summary>
        public void Send(PeerId peer, byte[] framed)
        {
            if (peer.Kind != PeerKind.Steam)
                throw new InvalidOperationException("P2PServerTransport non conosce il peer " + peer.ToString());

            if (framed == null || framed.Length == 0)
                return;

            if (!SteamLobby.Available)
                throw new InvalidOperationException("Steam non disponibile");

            bool accepted;
            try
            {
                accepted = CallSend(peer.SteamId, framed);
            }
            catch (Exception e)
            {
                Report("Send", e);
                throw new InvalidOperationException(MarkerError + e.GetType().Name);
            }

            if (!accepted)
                throw new InvalidOperationException("peer " + peer.ToString() + " non annunciato");
        }

        /// <summary>Quali indirizzi si stanno ascoltando: il P2P non ne ha, dice quanti peer ha.</summary>
        public string ListeningAddresses()
        {
            if (!SteamLobby.Available)
                return "p2p (Steam non disponibile)";

            try
            {
                return QueryListening() ?? "p2p (?)";
            }
            catch (Exception e)
            {
                return "p2p (illeggibile: " + e.GetType().Name + ")";
            }
        }

        /// <summary>Il delay di invio non esiste in un trasporto a messaggi: la proprieta' c'e' e basta.</summary>
        public bool DelayDisabled
        {
            get { return true; }
            set { }
        }

        /// <summary>
        /// ⭐ IL CICLO PER FRAME: svuota i tre canali e inietta i frame nel server, poi manda quello che e'
        /// stato accodato dalle `Send`. Chiamato da `Multiplayer.OnLateUpdate`.
        ///
        /// ⚠️ `try/catch` **qui** (e' per frame, e gira nel late update della mod): un guasto di Steam non
        /// deve fermare il resto del ciclo. Si logga una volta sola.
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
        /// "L'adapter sta ascoltando?" — per il log e per il referto: senza callback registrata il P2P
        /// scarta tutto in silenzio, e questa riga lo direbbe.
        /// </summary>
        internal static bool Listening()
        {
            if (!SteamLobby.Available)
                return false;

            try
            {
                return QueryListening2();
            }
            catch (Exception e)
            {
                Report("Listening", e);
                return false;
            }
        }

        // ─── GLI EVENTI VERSO IL COMPOSITO (sul thread di `Tick` = main thread) ────────────────────

        private void RaisePeerConnected(PeerId peer)
        {
            Action<PeerId> handler = PeerConnected;
            if (handler == null) return;

            try { handler(peer); }
            catch (Exception e) { Report("PeerConnected", e, true); }
        }

        private void RaisePeerDisconnected(PeerId peer)
        {
            Action<PeerId> handler = PeerDisconnected;
            if (handler == null) return;

            try { handler(peer); }
            catch (Exception e) { Report("PeerDisconnected", e, true); }
        }

        private void RaiseFrameReceived(PeerId peer, byte[] data)
        {
            Action<PeerId, byte[]> handler = FrameReceived;
            if (handler == null) return;

            try { handler(peer, data); }
            catch (Exception e) { Report("FrameReceived", e, true); }
        }

        // ─── I GUSCI VERSO L'IMPLEMENTAZIONE ─────────────────────────────────────────────────────────
        //
        // ⚠️ UN METODO PER CHIAMATA, come in `SteamLobby`: e' **qui** che nasce la `TypeLoadException`
        // (la JIT compila questo corpo quando viene invocato), quindi e' qui che il `try/catch` del
        // chiamante la cattura.

        private static void CallSetSink(Action<PeerId> onConnected, Action<PeerId> onDisconnected,
                                        Action<PeerId, byte[]> onFrame)
        {
            P2PServerTransportImpl.SetSink(onConnected, onDisconnected, onFrame);
        }

        private static bool CallStart(int port)
        {
            return P2PServerTransportImpl.Start(port);
        }

        private static void CallStop()
        {
            P2PServerTransportImpl.Stop();
        }

        private static bool CallSend(ulong steamId, byte[] framed)
        {
            return P2PServerTransportImpl.Send(steamId, framed);
        }

        private static void CallTick()
        {
            P2PServerTransportImpl.Tick();
        }

        private static string QueryListening()
        {
            return P2PServerTransportImpl.ListeningAddresses();
        }

        private static bool QueryListening2()
        {
            return P2PServerTransportImpl.Listening;
        }

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

                ModLog.Error(MarkerError + operation + ": " + error.GetType().Name + ": " + error.Message);
            }
            catch (Exception)
            {
                // Niente: se anche il log esplode, il gioco deve continuare.
            }
        }
    }
}
