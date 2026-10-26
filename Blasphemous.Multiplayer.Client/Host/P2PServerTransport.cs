using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Network;
using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Server;
using System;

namespace Blasphemous.Multiplayer.Client.Host
{
    /// <summary>
    /// P2P adapter for the server: receives peer P2P packets and injects them into the server
    /// through the same <c>IServerTransport</c> events used by TCP, so the server does not change.
    /// The host remains a TCP client on loopback; this adapter only adds the remote-peer wire.
    ///
    /// Two classes (<c>P2PServerTransport</c> + <c>P2PServerTransportImpl</c>): only the Impl names
    /// <c>Steamworks</c>, because on a non-Steam copy those types may be missing and
    /// <c>TypeLoadException</c> surfaces at the call site. <see cref="Prepare"/> is the gate.
    ///
    /// <c>Send</c> does not call Steam directly: the TCP reader thread may call it, and a native
    /// Steam call from the wrong thread is a failure that stays hidden until it does not. Packets
    /// are queued and flushed in <see cref="Tick"/> on the main thread.
    /// </summary>
    internal sealed class P2PServerTransport : IServerTransport
    {
        /// <summary>Log marker: P2P listen line in the composite.</summary>
        internal const string MarkerListen = "[Net] p2p ascolto: ";

        /// <summary>Log marker: peer announcement.</summary>
        internal const string MarkerPeerConnected = "[Net] p2p peer connesso: ";

        /// <summary>Log marker: peer disconnection.</summary>
        internal const string MarkerPeerDisconnected = "[Net] p2p peer disconnesso: ";

        /// <summary>Log marker: frame injection into the server.</summary>
        internal const string MarkerFrame = "[Net] p2p frame: ";

        /// <summary>Log marker: failure caught at the boundary.</summary>
        internal const string MarkerError = "[Net] p2p error: ";

        private static bool _failureLogged;

        /// <summary>
        /// Asked before <c>Add</c>: on a non-Steam copy the composite keeps a single transport.
        /// Does not throw.
        /// </summary>
        internal static bool Prepare()
        {
            if (!SteamLobby.Available)
            {
                ModLog.Info(MarkerListen + "not added: Steam unavailable (TCP only)");
                return false;
            }

            return true;
        }

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
        /// The port is unused: P2P has no listener, sessions are accepted per peer. Returns true
        /// even without Steam, so the TCP base still starts.
        /// </summary>
        public bool Start(int port)
        {
            if (!SteamLobby.Available)
            {
                ModLog.Info(MarkerListen + "Steam unavailable: adapter inert, TCP listening");
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
        /// Sends an already-framed message to a Steam peer. Throws if the peer is not a Steam peer
        /// or is no longer announced.
        /// </summary>
        public void Send(PeerId peer, byte[] framed)
        {
            if (peer.Kind != PeerKind.Steam)
                throw new InvalidOperationException("P2PServerTransport does not know peer " + peer.ToString());

            if (framed == null || framed.Length == 0)
                return;

            if (!SteamLobby.Available)
                throw new InvalidOperationException("Steam unavailable");

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
                throw new InvalidOperationException("peer " + peer.ToString() + " not announced");
        }

        /// <summary>Listening addresses string: P2P has no listener.</summary>
        public string ListeningAddresses()
        {
            if (!SteamLobby.Available)
                return "p2p (Steam unavailable)";

            try
            {
                return QueryListening() ?? "p2p (?)";
            }
            catch (Exception e)
            {
                return "p2p (unreadable: " + e.GetType().Name + ")";
            }
        }

        /// <summary>Message transports have no send delay; the property exists for the interface.</summary>
        public bool DelayDisabled
        {
            get { return true; }
            set { }
        }

        /// <summary>
        /// Per-frame cycle: drains the three channels, injects frames into the server, flushes
        /// queued sends. Called from <c>Multiplayer.OnLateUpdate</c>. Logged once on failure so a
        /// per-frame Steam error cannot flood.
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
        /// True when the adapter is listening. Without a registered callback, P2P discards
        /// silently.
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

        // === Events toward the composite (main thread) ===

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

        // === Wrappers toward the implementation ===
        //
        // One method per call, as in SteamLobby: TypeLoadException surfaces here (the JIT compiles
        // this body when it is invoked), so this is where the caller's try/catch catches it.

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
                // Nothing: if the log itself fails, the game must continue.
            }
        }
    }
}