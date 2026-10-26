using Blasphemous.ModdingAPI;
using SimpleTCP;
using System;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// The only <see cref="ICoopTransport"/> implementation: today's TCP socket, wrapped not
    /// changed - <c>SimpleTcpClient</c>. Holds the same calls that used to live in
    /// <c>NetworkManager</c>, same order and arguments: no byte on the wire changes.
    ///
    /// <c>NoDelay</c> lives here because it is a socket-level concern that should not cross the
    /// abstraction. <c>Connect</c> does not catch <c>SocketException</c> and <c>Send</c> does not
    /// catch <c>IOException</c> on purpose: the caller relies on both (RefusalType.Connection and
    /// OnDisconnect). Event handler throws still propagate; only the marker, the event
    /// invocations and Disconnect's own close are guarded, so nothing new can prevent a
    /// connection or leave the caller in a "connected" state after a failed close.
    /// </summary>
    internal sealed class TcpTransport : ICoopTransport
    {
        /// <summary>Open marker: <c>[Net] trasporto=tcp host=... porta=...</c>.</summary>
        internal const string Marker = "[Net] trasporto=tcp";

        /// <summary>The socket; the only place in this assembly that names SimpleTCP.</summary>
        private SimpleTcpClient _client;

        /// <summary>
        /// True when the socket exists and is connected. <c>NetworkManager</c> does not consume
        /// this: its IsConnected is unchanged for behavior parity.
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
                    // A closing socket must not turn a state query into an exception.
                    return false;
                }
            }
        }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> DataReceived;

        /// <summary>
        /// Opens the socket (same three calls as before), subscribes to SimpleTCP, and logs the
        /// open marker. <c>NoDelay = true</c> is set right after Connect and before any write,
        /// same as before. The subscription happens slightly earlier than in the previous code,
        /// but the "bytes arriving before a handler exists are lost" window is the same on both
        /// sides; no byte on the wire is affected.
        /// </summary>
        public void Connect(string host, int port)
        {
            _client = new SimpleTcpClient();
            _client.Connect(host, port);
            _client.TcpClient.NoDelay = true;
            _client.DataReceived += HandleDataReceived;

            // The host is what the player typed: it is already in the connect log
            // (Attempting to connect to ...) and the disconnect log, so this line introduces no
            // new exposure. Concatenation (not string.Format) merges Marker and " host=" into a
            // single literal that the marker-check tool can find in the DLL.
            try
            {
                ModLog.Info(Marker + " host=" + host + " porta=" + port.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                // A log must never be the reason a connection does not open.
            }

            if (Connected != null)
            {
                try { Connected(); }
                catch (Exception e) { ModLog.Error("[Net] trasporto=tcp: a Connected handler threw: " + e.GetType().Name); }
            }
        }

        /// <summary>
        /// Closes the socket and notifies. Close is guarded so a failure cannot prevent the
        /// caller from reaching its own OnDisconnect; the event handlers are guarded for the same
        /// reason.
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
                ModLog.Error("[Net] trasporto=tcp: error while closing: " + e.GetType().Name);
            }

            if (Disconnected != null)
            {
                try { Disconnected(); }
                catch (Exception e) { ModLog.Error("[Net] trasporto=tcp: a Disconnected handler threw: " + e.GetType().Name); }
            }
        }

        /// <summary>
        /// Writes the bytes verbatim. <c>IOException</c> propagates on purpose: the caller uses it
        /// to detect the socket going down. <paramref name="cls"/> is ignored - TCP is a single
        /// ordered, reliable stream, so Unreliable/Reliable has no effect here.
        /// </summary>
        public void Send(byte[] framed, TransportClass cls)
        {
            _client.Write(framed);
        }

        /// <summary>
        /// SimpleTCP callback: rebroadcasts the payload to <see cref="DataReceived"/> on the same
        /// thread, without copying, splitting or interpreting it.
        /// </summary>
        private void HandleDataReceived(object sender, DataReceivedEventArgs message)
        {
            if (DataReceived != null)
                DataReceived(message.data);
        }
    }
}