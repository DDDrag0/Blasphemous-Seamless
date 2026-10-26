using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// Shutdown gate: once raised, <c>CloseP2PSessionWithUser</c> must not be called. Calling
    /// Steam's native close during process teardown crashes natively in steam_api64; the process
    /// closes the sessions when it dies. Game-path Close calls (Drop, LinkLost, HandshakeExpired,
    /// Disconnect button) are unaffected.
    /// </summary>
    internal static class P2PShutdownGate
    {
        /// <summary>Log marker. Presence in the log proves shutdown passed through the gate.</summary>
        internal const string MarkerEnter =
            "[Net] chiusura in corso: da qui in poi nessuna CloseP2PSessionWithUser";

        private static volatile bool _shuttingDown;

        /// <summary>
        /// Raises the gate. Idempotent, never throws. Must be called before
        /// <c>HostSession.Stop()</c>.
        /// </summary>
        internal static void Enter()
        {
            _shuttingDown = true;

            try
            {
                ModLog.Info(MarkerEnter +
                            " (the P2P session is closed by process death: that is the declared limit)");
            }
            catch (System.Exception)
            {
                // The gate is raised anyway: a log that does not write must not reopen it.
            }
        }

        /// <summary>True while shutting down; read by Close paths that run at exit.</summary>
        internal static bool Active
        {
            get { return _shuttingDown; }
        }
    }
}