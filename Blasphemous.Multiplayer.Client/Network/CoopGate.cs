using System;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Co-op predicate for guards that must fire only when connected to a server. Uses
    /// <c>NetworkManager.IsConnected</c> (the socket), not the peer count: a live server
    /// connection without an active peer still counts as co-op. Returns false on any failure -
    /// a bug here must never enable a debuff.
    /// </summary>
    internal static class CoopGate
    {
        /// <summary>True only with a live server connection. Never throws.</summary>
        internal static bool IsMultiplayerActive()
        {
            try
            {
                if (Main.Multiplayer == null)
                    return false;

                if (Main.Multiplayer.NetworkManager == null)
                    return false;

                return Main.Multiplayer.NetworkManager.IsConnected;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}