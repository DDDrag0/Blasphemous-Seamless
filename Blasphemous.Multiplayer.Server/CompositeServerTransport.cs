using Blasphemous.Multiplayer.Common;
using System;
using System.Collections.Generic;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// Transport that holds other transports. The server can receive from two sources at once
/// (the host's TCP loopback plus Steam P2P from peers), so its transport cannot be a single
/// object: it is a registry. It contains N transports, owns a peer-to-transport map for routing,
/// and funnels every child's events onto one stream so <c>Server</c> sees a single interface.
///
/// Threading: the composite does not move anything. Child events are re-emitted on the thread
/// that delivered them (for TCP, SimpleTCP's reader thread), per the <c>IServerTransport</c>
/// contract.
///
/// Rule: a transport must announce a peer before its first frame, and that peer is its own.
/// A <c>Send</c> to a peer no transport has announced throws, matching <c>SimpleTcpServer.Send</c>
/// with an unknown recipient.
/// </summary>
public class CompositeServerTransport : IServerTransport
{
    /// <summary>Registered transports, in add order (which is the start order).</summary>
    private readonly List<IServerTransport> transports = new List<IServerTransport>();

    /// <summary>Which transport owns each peer, so Send knows where to route.</summary>
    private readonly Dictionary<PeerId, IServerTransport> owners = new Dictionary<PeerId, IServerTransport>();

    /// <summary>One watcher per child, so event attach/detach pairs use the same delegates.</summary>
    private readonly Dictionary<IServerTransport, Watcher> watchers = new Dictionary<IServerTransport, Watcher>();

    /// <inheritdoc/>
    public event Action<PeerId> PeerConnected;

    /// <inheritdoc/>
    public event Action<PeerId> PeerDisconnected;

    /// <inheritdoc/>
    public event Action<PeerId, byte[]> FrameReceived;

    /// <summary>Empty composite: transports are added with <see cref="Add"/>.</summary>
    public CompositeServerTransport()
    {
    }

    /// <summary>
    /// Registers a transport and subscribes to its events. Does not start it: <see cref="Start"/>
    /// starts all of them together on the same port. Adding the same object twice is a no-op.
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

    /// <inheritdoc/>
    /// <remarks>
    /// Starts every child on the same port. If one fails, those already started are stopped
    /// (in reverse order) and false is returned: a half-started composite would be an open port
    /// nobody asked for. A child that throws from Start counts as not-started, because the
    /// <c>IServerTransport.Start</c> contract forbids throwing.
    /// </remarks>
    public bool Start(int port)
    {
        if (transports.Count == 0)
        {
            // No wire, no listen: returning true would mean "listening on nothing".
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
    /// Stops children in reverse start order. A child that throws does not prevent stopping the
    /// others; all are stopped, then the first failure is rethrown (only one, so Server.Stop logs
    /// a single line as before).
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
            throw new InvalidOperationException("no transport knows peer " + peer.ToString());

        owner.Send(peer, framed);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// With one transport the string is that transport's, so the existing
    /// <c>[Host] ascolto su=0.0.0.0</c> line is unchanged. With several, they are joined with
    /// commas so the line stays informative.
    /// </remarks>
    public string ListeningAddresses()
    {
        if (transports.Count == 0)
            return "(no transport)";

        string[] parts = new string[transports.Count];
        for (int i = 0; i < transports.Count; i++)
        {
            try
            {
                parts[i] = transports[i].ListeningAddresses();
            }
            catch (Exception e)
            {
                parts[i] = "(unreadable: " + e.GetType().Name + ")";
            }
        }

        if (parts.Length == 1)
            return parts[0];

        return string.Join(",", parts);
    }

    /// <summary>
    /// Send delay is set on every wire; on read, true only if all wires have it disabled
    /// (no wires = true, the historical default).
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

    /// <summary>Stops transports in reverse order and returns the first failure (or null).</summary>
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

    /// <summary>The peer announced by <paramref name="transport"/> now belongs to it.</summary>
    private void registerOwner(PeerId peer, IServerTransport transport)
    {
        owners[peer] = transport;
    }

    /// <summary>The peer left the wire: stop routing to it.</summary>
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
    /// Bridge between a child and the composite. Delegates are created once here so Add/Remove
    /// pair correctly.
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
        /// Order: register the owner FIRST, then announce. A handler that replies to the new
        /// arrival immediately (writing a frame) must find the route already open.
        /// </summary>
        public void OnPeerConnected(PeerId peer)
        {
            _parent.registerOwner(peer, _transport);
            _parent.raisePeerConnected(peer);
        }

        /// <summary>
        /// Reverse order on purpose: announce FIRST, forget AFTER. The server's disconnect
        /// handler may try to write to the peer that just left; with the map still populated
        /// that write reaches the real transport, which fails exactly the way
        /// <c>SimpleTcpServer.Send</c> did. Clearing the map first would change where the error
        /// comes from, not just when.
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