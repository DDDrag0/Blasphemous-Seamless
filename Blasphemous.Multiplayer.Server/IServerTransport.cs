using Blasphemous.Multiplayer.Common;
using System;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// Abstraction of a single wire, on the server side. No TCP, no Steam, no protocol bytes here:
/// implementations move already-framed buffers (see <see cref="Send"/> and <see cref="FrameReceived"/>).
/// <c>Server</c> speaks only through this interface, so multiple wires can coexist behind a
/// <see cref="CompositeServerTransport"/>.
///
/// Contract:
/// - Identity is <see cref="PeerId"/>; the transport alone knows how a peer is named on its wire.
/// - Events are delivered on the transport's own reader thread (unchanged from the previous
///   implementation); implementations must not marshal to another thread.
/// - A transport must announce a peer via <see cref="PeerConnected"/> before its first
///   <see cref="FrameReceived"/> for that peer, so the composite can route replies.
/// - <see cref="Start"/> returns false on failure and does not throw. <see cref="Stop"/> may throw
///   (the server catches and logs it). <see cref="Send"/> may throw when the peer is gone (the
///   server catches and logs it); swallowing that exception would hide a lost peer.
/// </summary>
public interface IServerTransport
{
    /// <summary>
    /// Opens the wire on <paramref name="port"/> and begins delivering events. Returns false on
    /// failure and does not throw. After a false return the transport must remain reusable for a
    /// later Start.
    /// </summary>
    bool Start(int port);

    /// <summary>
    /// Closes the wire and stops delivering events. May throw. No-op when the transport was never
    /// started.
    /// </summary>
    void Stop();

    /// <summary>
    /// Sends <paramref name="framed"/> (already packed as <c>[ushort len][byte type][payload]</c>)
    /// to <paramref name="peer"/> without interpreting it. May throw when the peer is no longer
    /// reachable.
    /// </summary>
    void Send(PeerId peer, byte[] framed);

    /// <summary>
    /// Addresses the transport is actually listening on, for the <c>[Host] ascolto su=...</c>
    /// marker. P2P transports return a descriptive string rather than an address.
    /// </summary>
    string ListeningAddresses();

    /// <summary>Wire-level send delay flag, as before.</summary>
    bool DelayDisabled { get; set; }

    /// <summary>A peer is reachable; from this point <see cref="Send"/> can address it.</summary>
    event Action<PeerId> PeerConnected;

    /// <summary>A peer is no longer reachable. Delivered on the reader thread.</summary>
    event Action<PeerId> PeerDisconnected;

    /// <summary>
    /// Bytes arrived from the wire, exactly as they left it: no reassembly and no interpretation
    /// (framing is done by <c>Server.Receive</c>). Delivered on the transport's reader thread.
    /// </summary>
    event Action<PeerId, byte[]> FrameReceived;
}