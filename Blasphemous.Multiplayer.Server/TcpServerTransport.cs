using Blasphemous.Multiplayer.Common;
using SimpleTCP;
using System;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// TCP implementation of <see cref="IServerTransport"/>: opens a <c>SimpleTcpServer</c>,
/// converts <c>e.ip</c> to <see cref="PeerId"/> at the boundary, and forwards raw frames.
/// Framing, protocol dispatch, authentication and relay stay in <c>Server</c>; this class only
/// moves bytes. Binds wildcard IPv4 in the client build so loopback is guaranteed; standalone
/// keeps <c>SimpleTcpServer</c>'s per-NIC bind.
/// </summary>
public class TcpServerTransport : IServerTransport
{
    /// <summary>Listener; null means "not listening" (never started, stopped, or Start failed).</summary>
    private SimpleTcpServer _server;

    /// <summary>
    /// DelayDisabled applied to the next listener. Lives here because it is a wire property
    /// (Nagle), not a server one.
    /// </summary>
    private bool _delayDisabled = true;

    /// <inheritdoc/>
    public event Action<PeerId> PeerConnected;

    /// <inheritdoc/>
    public event Action<PeerId> PeerDisconnected;

    /// <inheritdoc/>
    public event Action<PeerId, byte[]> FrameReceived;

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

            listening.ClientConnected += onClientConnected;
            listening.ClientDisconnected += onClientDisconnected;
            listening.DataReceived += onDataReceived;

            // Wildcard in the client (SimpleTcpServer's per-NIC bind does not guarantee loopback);
            // standalone keeps the historical per-NIC bind.
#if COOP_HOST
            listening.Start(System.Net.IPAddress.Any, port);
#else
            listening.Start(port);
#endif

            listening.DelayDisabled = _delayDisabled;

            _server = listening;
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Port could not be opened: no listener, no half state.
            _server = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            // "Port was already occupied for all network interfaces".
            _server = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        // Detach the field first so no Send can leave during teardown.
        SimpleTcpServer running = _server;
        _server = null;

        if (running == null)
            return;

        try
        {
            // Handlers stay attached during Stop so the last ClientDisconnected events reach the
            // server, as before.
            running.Stop();
        }
        finally
        {
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
            throw new InvalidOperationException("TCP transport is not started");

        sending.Send(peer.ToString(), framed);
    }

    /// <inheritdoc/>
    public string ListeningAddresses()
    {
        try
        {
            SimpleTcpServer listening = _server;
            if (listening == null)
                return "(stopped)";

            System.Collections.Generic.List<System.Net.IPAddress> addresses = listening.GetListeningIPs();
            if (addresses == null || addresses.Count == 0)
                return "(no listener)";

            string[] text = new string[addresses.Count];
            for (int i = 0; i < addresses.Count; i++)
                text[i] = addresses[i].ToString();

            // string.Join with a string[] for net35; same rendering.
            return string.Join(",", text);
        }
        catch (Exception e)
        {
            return "(unreadable: " + e.GetType().Name + ")";
        }
    }

    /// <summary>Peer connected: <c>e.ip</c> becomes a <see cref="PeerId"/>; the string is kept intact.</summary>
    private void onClientConnected(object sender, ClientConnectionEventArgs e)
    {
        Action<PeerId> handler = PeerConnected;
        if (handler != null)
            handler(PeerId.FromIp(e.ip));
    }

    /// <summary>Peer disconnected: same conversion as connect.</summary>
    private void onClientDisconnected(object sender, ClientConnectionEventArgs e)
    {
        Action<PeerId> handler = PeerDisconnected;
        if (handler != null)
            handler(PeerId.FromIp(e.ip));
    }

    /// <summary>Raw bytes from the wire: forwarded unchanged; framing is done by <c>Server.Receive</c>.</summary>
    private void onDataReceived(object sender, DataReceivedEventArgs e)
    {
        Action<PeerId, byte[]> handler = FrameReceived;
        if (handler != null)
            handler(PeerId.FromIp(e.ip), e.data);
    }
}