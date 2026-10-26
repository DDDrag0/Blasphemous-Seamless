using Blasphemous.ModdingAPI;
using System;
using System.Net.Sockets;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// Peer-side P2P transport behind the same <c>ICoopTransport</c> seam as TCP. Enters the
    /// current Steam lobby, targets its owner, sends a reliable hello, then routes frames on
    /// three channels (0 unreliable with sequence, 1 reliable, 2 reliable-isolated for skins).
    /// Reads per frame, drops stale packets per sender, detects link loss via a heartbeat.
    ///
    /// Names no Steamworks type: on a non-Steam copy those may be missing and TypeLoadException
    /// surfaces at the call site. <see cref="SteamLobby.Available"/> gates every call into the
    /// Impl so a missing Steam never touches it. Failures surface as <c>SocketException</c>
    /// (Connect) or <c>IOException</c> (Send), matching the TCP contract. Chosen only via the
    /// "p2p" token in the Server IP field; the host stays TCP on loopback.
    /// </summary>
    internal sealed class SteamP2PTransport : ICoopTransport
    {
        /// <summary>Open marker, twin of TcpTransport.Marker: which wire is running.</summary>
        internal const string Marker = "[Net] trasporto=p2p";

        /// <summary>Token that selects this wire in the Server IP field.</summary>
        internal const string RequestToken = "p2p";

        internal const string MarkerAdapterActive = "[Net] p2p: adapter active (lobby=";
        internal const string MarkerHelloSent = "[Net] p2p: hello sent to ";
        internal const string MarkerSessionRequest = "[Net] p2p: session request from ";
        internal const string MarkerNotArmed = "[Net] p2p: request from ";
        internal const string MarkerAcceptOk = "[Net] p2p: session with ";
        internal const string MarkerAcceptFail = "[Net] p2p: accept FAILED ";
        internal const string MarkerReturnAccepted = "[Net] p2p: peer accepted the return session from the host";
        internal const string MarkerFirstPacket = "[Net] p2p: first packet (channel ";
        internal const string MarkerConnected = "[Net] p2p: connected (handshake ";
        internal const string MarkerHandshakeTimeout = "[Net] p2p: handshake expired (";
        internal const string MarkerSessionFail = "[Net] p2p: SESSION FAILED peer=";

        private static bool _failureLogged;

        /// <summary>
        /// True when the Server IP field requests P2P (exact "p2p", case-insensitive). Anything
        /// else - IPs, ip:port, hostnames - stays TCP.
        /// </summary>
        internal static bool IsRequested(string server)
        {
            if (server == null)
                return false;

            return string.Equals(server.Trim(), RequestToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Connects the Impl sink to this instance's events. One instance at a time, created by
        /// <c>NetworkManager.Connect</c>; a new connection replaces the previous one, already
        /// closed.
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

        /// <summary>True when the hello has been sent and the link has not expired.</summary>
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
        /// Opens the wire. Throws <c>SocketException</c> when P2P is not usable (Steam unavailable,
        /// or no lobby joined) so the caller answers with <c>RefusalType.Connection</c>, the same
        /// refusal as a rejected TCP socket. Late failure (no packet for 5s) is not an exception
        /// here: it is the <c>Disconnected</c> event plus an <c>IOException</c> on the next Send.
        /// </summary>
        public void Connect(string host, int port)
        {
            if (!SteamLobby.Available)
            {
                ModLog.Warn(Marker + ": Steam unavailable (Steamworks types missing, or no " +
                            "account): P2P not opened, TCP path unaffected");
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
                ModLog.Error(Marker + ": no Steam lobby to join. Accept an invite (or host " +
                             "yourself), then press Connect with '" + RequestToken + "' in the Server IP field");
                throw new SocketException((int)SocketError.NotConnected);
            }

            ReportOpen(host, port);
        }

        /// <summary>
        /// Open marker for P2P. Without it, a log with an open tunnel could not say "P2P is going
        /// out": the previous code only logged Marker on error paths. Log only, no state, no
        /// decision. The try/catch guards LobbySummary because it calls Steam.
        /// </summary>
        private static void ReportOpen(string host, int port)
        {
            try
            {
                // Concatenation (not string.Format) merges Marker and " host=" into a single
                // literal, which is what the marker-check tool finds in the DLL.
                ModLog.Info(Marker + " host=" + host + " porta=" +
                            port.ToString(System.Globalization.CultureInfo.InvariantCulture) + " - " +
                            LobbySummary());
            }
            catch (Exception e)
            {
                Report("LogOpen", e);
            }
        }

        /// <summary>Closes the wire and notifies. Must not throw; caller still reaches OnDisconnect.</summary>
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
        /// Sends frames on the correct channel. Throws <c>IOException</c> when the link has been
        /// declared lost (timeout), the way the caller already expects.
        /// </summary>
        public void Send(byte[] framed, TransportClass cls)
        {
            if (!SteamLobby.Available)
                throw new System.IO.IOException("[Net] trasporto=p2p: Steam unavailable");

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
                throw new System.IO.IOException("[Net] trasporto=p2p: send failed (" + e.GetType().Name + ")");
            }
        }

        /// <summary>
        /// Per-frame receive and heartbeat. Called from <c>Multiplayer.OnLateUpdate</c> after
        /// <c>SteamLobby.Tick()</c> so an accepted <c>P2PSessionRequest_t</c> is delivered before
        /// the channels are read. Never escapes: a Steam failure here would stop the mod's late
        /// update for everyone. Logged once.
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

        /// <summary>Read-only lobby summary for the report. Empty when no lobby is joined.</summary>
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

        // Handlers fired from within Tick (main thread). Each is guarded: a throwing handler would
        // stop the per-frame channel read, and with it the game. The TCP side does not need this
        // guard because the exception rises on SimpleTCP's read thread, where there is no one else
        // to save.

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

        // Wrappers to the implementation.
        //
        // One method per call: the JIT compiles the body when it is invoked, so a TypeLoadException
        // from the Impl surfaces inside the caller's try, not before it.

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
        /// One log line for a failure caught at the boundary. <c>once</c> is for per-frame methods:
        /// a repeating failure must not become a repeating line.
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

                ModLog.Error(Marker + " error: " + operation + ": " + error.GetType().Name + ": " +
                             error.Message);
            }
            catch (Exception)
            {
                // Nothing: if the log itself fails, the game must continue.
            }
        }
    }
}