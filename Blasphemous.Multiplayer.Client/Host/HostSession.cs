using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Server;
using CoopServer = Blasphemous.Multiplayer.Server.Server;
using System;
using System.Net;
using System.Net.Sockets;

namespace Blasphemous.Multiplayer.Client.Host
{
    /// <summary>
    /// Host session: runs the multiplayer server inside the game process.
    ///
    /// Opens the port with the same engine used by the standalone server and lets the local client
    /// connect to <c>127.0.0.1</c>. Never calls <c>Environment.Exit</c> (would close the game) or
    /// <c>Console.*</c> (throws <c>IOException</c> when no console exists): server output goes
    /// through ModLog. The port is checked in two steps (exclusive bind probe plus a real TCP
    /// connect) to distinguish a port that is genuinely occupied from TIME_WAIT or a busy NIC that
    /// would not prevent the real bind.
    /// </summary>
    internal static class HostSession
    {
        /// <summary>Max players; same default as the standalone server.</summary>
        internal const int MAX_PLAYERS = 10;

        // Loopback markers. The existing lines ("server avviato", "indirizzo locale", etc.) are
        // unchanged; these add the bind address and the answering loopback (ipv4 vs name), so the
        // log alone can say what the server is bound to and which address actually answered.
        internal const string MarkerBind = "[Host] ascolto su=";
        internal const string MarkerReady = "[Host] loopback pronto via=";
        internal const string MarkerConnected = "[Host] connesso via=";
        internal const string MarkerWaitFail = "[Host] error: loopback";

        /// <summary>
        /// Local dial line with real host and port, plus the outcome in <c>OnConnect</c>. The
        /// connect message does not print the port, so this line makes the dial port visible and
        /// comparable with the listening port.
        /// </summary>
        internal const string MarkerDial = "[Host] dial locale ";

        /// <summary><c>127.0.0.1</c>: bypasses name resolution.</summary>
        internal const string LOOPBACK_IPV4 = "127.0.0.1";

        /// <summary><c>localhost</c>: the resolver may route it to <c>::1</c> (IPv6).</summary>
        internal const string LOOPBACK_NAME = "localhost";

        internal const string VIA_IPV4 = "ipv4";
        internal const string VIA_NOME = "nome";

        /// <summary>
        /// <c>via=</c> value when the probe timed out and the dial is attempted anyway (best
        /// effort): nobody answered in time.
        /// </summary>
        internal const string VIA_BESTEFFORT = "best-effort";

        private static CoopServer _server;

        /// <summary>Live instances in this process. The marker prints 1 when hosting, 0 otherwise.</summary>
        private static int _instances;

        /// <summary>
        /// Last start failure, one line, shown to the player by the connection screen. Empty when
        /// the last start succeeded. The <c>[Host]</c> marker remains the log trail.
        /// </summary>
        internal static string LastError { get; private set; }

        /// <summary>True while this machine's host is listening.</summary>
        internal static bool IsRunning
        {
            get { return _server != null; }
        }

        /// <summary>Host port, or 0 when no host is running.</summary>
        internal static int Port
        {
            get { return _server != null ? _server.Port : 0; }
        }

        /// <summary>
        /// LAN IPv4 of this machine, the value the peer writes in the "Server IP" field. Read from
        /// the same function that prints <c>[Host] indirizzo locale=</c> on start, so the two
        /// cannot disagree. Read-only. Returns <c>"unknown"</c> when resolution fails (no
        /// network): the host still runs.
        /// </summary>
        internal static string LanAddress
        {
            get { return LocalAddress(); }
        }

        /// <summary>Connected players on the host (0 when no host).</summary>
        internal static int Players
        {
            get
            {
                try
                {
                    if (_server == null)
                        return 0;

                    var players = _server.getPlayers();
                    return players == null ? 0 : players.Count;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Starts the host on the given port with the session password (the one peers must use).
        /// Returns false when it did not start, and in that case there is no server to stop -
        /// callers must not try to connect locally, since nothing is listening on our behalf.
        /// Never throws: every failure becomes a <c>[Host] ...</c> marker and a <c>false</c>.
        /// </summary>
        internal static bool TryStart(int port, string password)
        {
            try
            {
                LastError = string.Empty;

                if (port <= 0 || port > 65535)
                {
                    LastError = "invalid port " + port;
                    ModLog.Error("[Host] error: " + LastError);
                    return false;
                }

                if (_server != null)
                {
                    // Double click on the button: do not start a second instance on the same port.
                    ModLog.Info("[Host] server already started porta=" + _server.Port + " istanze=" + _instances);
                    return true;
                }

                if (Main.Multiplayer != null && Main.Multiplayer.NetworkManager != null &&
                    Main.Multiplayer.NetworkManager.IsConnected)
                {
                    // Already connected to a server (external or ours): hosting now would mean two
                    // sessions on the same machine, neither consistent.
                    LastError = "already connected to a server: disconnect before hosting";
                    ModLog.Warn("[Host] error: " + LastError);
                    return false;
                }

                // Two-step port check. The probe below is an exclusive bind: it fails if anything
                // holds the port (needed for coexisting with the standalone SERVER-COOP). On its
                // own it would also report "occupied" in two real cases where the real start
                // WOULD succeed: a TIME_WAIT connection on the port, and a busy NIC while others
                // are free. Second opinion: does anyone ANSWER on 127.0.0.1:porta? If not, there
                // is no server to respect, and we try the real start. If yes, we do not touch it -
                // that is the external-server case.
                if (!IsPortFree(port))
                {
                    if (SomeoneListens(port))
                    {
                        LastError = "port " + port + " is already occupied (external server or another host session)";
                        ModLog.Error("[Host] port occupied");
                        ModLog.Error("[Host] detail: someone answers on 127.0.0.1:" + port +
                                     " (external SERVER-COOP or another host session): not starting");
                        return false;
                    }

                    ModLog.Warn("[Host] detail: probe says occupied but nobody answers on 127.0.0.1:" +
                                port + " (TIME_WAIT or busy NIC): trying the real start");
                }

                // The server's transport is a composite: TCP (the same socket as before, wildcard
                // IPv4 bind) plus, when available, the P2P transport for remote peers. Prepare()
                // is the gate - on a non-Steam copy (or Steam offline) the composite keeps only
                // TCP, so a missing optional transport never prevents the base from starting.
                CompositeServerTransport transport = new CompositeServerTransport();
                transport.Add(new TcpServerTransport());

                if (P2PServerTransport.Prepare())
                    transport.Add(new P2PServerTransport());

                CoopServer started = new CoopServer(MAX_PLAYERS, password ?? string.Empty, transport);
                if (!started.Start(port))
                {
                    // Server.Start returns false on SocketException and on InvalidOperationException
                    // ("Port was already occupied"): the real start has the last word.
                    LastError = "port " + port + " is already occupied (external server or another host session)";
                    ModLog.Error("[Host] port occupied");
                    return false;
                }

                _server = started;
                _instances = 1;

                ModLog.Info("[Host] server started porta=" + port + " istanze=" + _instances);

                // Address to give the other machine: the only value a peer needs to join.
                ModLog.Info("[Host] indirizzo locale=" + LocalAddress());

                // Bind marker: ask the listener which sockets it actually has
                // (Server.ListeningAddresses() -> SimpleTcpServer.GetListeningIPs()), so the log
                // says whether loopback is covered. With the fix that is "0.0.0.0" (loopback +
                // LAN); without it, only the NIC list.
                ModLog.Info(MarkerBind + started.ListeningAddresses() + " porta=" + port +
                            " (read from the listener)");
                return true;
            }
            catch (Exception e)
            {
                // A failing server must not break the game: this runs in OnGUI, and an exception
                // escaping OnGUI is a Unity error every frame, forever. Return false and move on.
                _server = null;
                _instances = 0;
                LastError = "error: " + e.GetType().Name + ": " + e.Message;
                ModLog.Error("[Host] error: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Stops the host and releases the port. Safe to call at any time: when no host exists it
        /// does and logs nothing, so "Disconnect" does not print <c>[Host] server fermo</c> when
        /// there was no host. Never throws.
        /// </summary>
        internal static void Stop()
        {
            CoopServer running = _server;
            _server = null;

            if (running == null)
                return;

            _instances = 0;

            try
            {
                running.Stop();
            }
            catch (Exception e)
            {
                // Server.Stop already does not throw; this is the belt for an unforeseen failure.
                // The port is treated as free regardless: the object is detached.
                ModLog.Error("[Host] error: " + e.GetType().Name + ": " + e.Message);
            }

            ModLog.Info("[Host] server stopped");
        }

        /// <summary>
        /// "Does loopback answer right now?" - one attempt. Returns <see cref="LOOPBACK_IPV4"/>
        /// when <c>127.0.0.1:porta</c> accepts a connection, <see cref="LOOPBACK_NAME"/> when only
        /// <c>localhost</c> does (the name resolves to <c>::1</c>), otherwise <c>null</c>.
        ///
        /// It is a measurement, not a sleep: the caller repeats it once per frame until it answers
        /// or times out, and the real connection starts only after that. Order is deliberate:
        /// <c>127.0.0.1</c> first (bypasses the resolver, cannot be hijacked by the hosts file),
        /// the name only as an IPv6 fallback. The probe is a real TCP connect, opened and closed
        /// immediately, so the server sees it (two log lines) - that is the price for measuring
        /// "the port answers" instead of guessing it. Never throws.
        /// </summary>
        internal static string ProbeLoopback(int port, int attemptTimeoutMs)
        {
            try
            {
                if (Answers(LOOPBACK_IPV4, port, attemptTimeoutMs))
                    return LOOPBACK_IPV4;

                if (Answers(LOOPBACK_NAME, port, attemptTimeoutMs))
                    return LOOPBACK_NAME;

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// "Does anyone ACCEPT a connection on host:port within <paramref name="timeoutMs"/>?"
        /// Returns false on immediate refusal (nobody listens) and on timeout. Unlike
        /// <see cref="SomeoneListens"/> - where on doubt we return true because the cost is
        /// starting a second server - here doubt means false: the caller retries next frame and
        /// will try to connect anyway.
        /// </summary>
        private static bool Answers(string host, int port, int timeoutMs)
        {
            TcpClient probe = null;

            try
            {
                probe = new TcpClient();
                IAsyncResult pending = probe.BeginConnect(host, port, null, null);

                if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
                    return false;

                probe.EndConnect(pending);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (probe != null)
                {
                    try
                    {
                        probe.Close();
                    }
                    catch (Exception)
                    {
                        // The probe already did its job.
                    }
                }
            }
        }

        /// <summary>
        /// "Does anyone ANSWER on 127.0.0.1:porta?" - the second opinion on the port. Returns true
        /// when a socket is listening (or something that does not refuse): the port belongs to
        /// someone else and we start nothing. Returns false on immediate Connection refused:
        /// nobody listens on loopback, so the probe that said "occupied" meant TIME_WAIT or another
        /// interface, not a server to respect. On doubt we answer true (1 s timeout): starting a
        /// second server over someone's is far worse than telling the user to retry.
        /// </summary>
        private static bool SomeoneListens(int port)
        {
            TcpClient probe = null;

            try
            {
                probe = new TcpClient();
                IAsyncResult pending = probe.BeginConnect("127.0.0.1", port, null, null);

                if (!pending.AsyncWaitHandle.WaitOne(1000))
                    return true;

                probe.EndConnect(pending);
                return true;
            }
            catch (SocketException)
            {
                // Connection refused: nobody listening.
                return false;
            }
            catch (Exception)
            {
                return true;
            }
            finally
            {
                if (probe != null)
                {
                    try
                    {
                        probe.Close();
                    }
                    catch (Exception)
                    {
                        // Nothing.
                    }
                }
            }
        }

        /// <summary>
        /// "Port is free" probe: binds the port and releases it immediately. Gives a clear message
        /// only - the real start is <c>Server.Start</c>, which has its own failure path.
        ///
        /// <c>ExclusiveAddressUse = true</c> before Start is what makes the probe fail even when
        /// the other socket requested address reuse; without it an occupied port could look free
        /// and the failure would surface only at connect time. There is a millisecond window
        /// between probe close and engine bind; if someone grabs the port right there, Server.Start
        /// reports it with the same marker.
        /// </summary>
        private static bool IsPortFree(int port)
        {
            TcpListener probe = null;

            try
            {
                probe = new TcpListener(IPAddress.Any, port);

                try
                {
                    probe.ExclusiveAddressUse = true;
                }
                catch (Exception)
                {
                    // On some runtimes the property cannot be set before Start: proceed anyway,
                    // the probe remains useful in most cases.
                }

                probe.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                if (probe != null)
                {
                    try
                    {
                        probe.Stop();
                    }
                    catch (Exception)
                    {
                        // The probe already did its job.
                    }
                }
            }
        }

        /// <summary>
        /// LAN IPv4 of this machine, the value the peer writes in "Server IP" to join. Informational
        /// only: decides nothing and opens nothing. On failure returns <c>"unknown"</c>, and
        /// the host still runs. Result is cached: resolution can cost tens of milliseconds and the
        /// caller runs in OnGUI.
        /// </summary>
        private static string LocalAddress()
        {
            if (_localAddress != null)
                return _localAddress;

            try
            {
                IPAddress[] addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(address))
                        continue;

                    _localAddress = address.ToString();
                    return _localAddress;
                }
            }
            catch (Exception)
            {
                // No network, or hostname not resolvable: not an error of the host.
            }

            _localAddress = "unknown";
            return _localAddress;
        }

        private static string _localAddress;
    }
}