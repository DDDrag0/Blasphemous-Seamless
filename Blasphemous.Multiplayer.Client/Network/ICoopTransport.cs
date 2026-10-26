using System;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// Transport seam: the minimum surface NetworkManager uses to talk to any wire, without
    /// knowing which. Byte-based - Send receives the buffer already framed by QueueMesssage
    /// ([ushort len][byte type][payload]). DataReceived fires on the transport's read thread;
    /// no implementation marshals to the main thread. Connect may throw SocketException, Send
    /// may throw IOException - callers rely on both.
    /// </summary>
    internal interface ICoopTransport
    {
        /// <summary>Opens the wire. May throw SocketException; caller handles it. Do not catch here.</summary>
        void Connect(string host, int port);

        /// <summary>Closes the wire. Must not throw; caller must still reach OnDisconnect.</summary>
        void Disconnect();

        /// <summary>
        /// True if the wire is open. NetworkManager does not consume it - it uses status + non-null
        /// transport, so socket loss is only observed on a failed write.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Writes a pre-framed buffer verbatim. TransportClass is decided where the payload is
        /// produced; TCP ignores it and writes the same bytes. May throw IOException when the wire
        /// is down (the caller uses it to disconnect).
        /// </summary>
        void Send(byte[] framed, TransportClass cls);

        /// <summary>The wire opened (after a successful Connect).</summary>
        event Action Connected;

        /// <summary>The wire closed (from Disconnect).</summary>
        event Action Disconnected;

        /// <summary>
        /// Bytes arrived, as they came off the socket: no reassembly, no interpretation (framing
        /// is ProcessQueue's job). Delivered on the transport's read thread.
        /// </summary>
        event Action<byte[]> DataReceived;
    }
}