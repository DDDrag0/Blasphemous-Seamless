using System;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// "STO GIOCANDO IN CO-OP?" — **un predicato solo**, per le guardie che devono valere soltanto con un
    /// server: la schermata di morte (trasparente solo connessi) e la Colpa sulla vita (attiva solo
    /// connessi). Non e' una patch, non e' un `MonoBehaviour`, non tocca niente: legge un booleano.
    ///
    /// LA DOMANDA, e perche' e' questa. L'utente ha detto "quando ci si **connette a un server**", quindi
    /// il segnale e' il **SOCKET**: `Main.Multiplayer.NetworkManager.IsConnected`
    /// (`NetworkManager.cs:22`: `_connectionStatus == Connected && _client != null`). Non
    /// `EnemySyncManager.ConnectedPlayersCount()`, che risponde "quanti PEER sono in scena" — una cosa
    /// diversa e piu' stretta. ⇒ **dichiarato**: una connessione al server **senza** un peer attivo conta
    /// come co-op (la Colpa si accende); se un giorno non va bene, si cambia **qui**, una riga, e tutte e
    /// due le guardie cambiano insieme.
    ///
    /// IN DUBBIO: **single player**. Se la catena non e' ancora costruita (`Main.Multiplayer` o il
    /// `NetworkManager` nulli) o se una lettura lancia, si risponde `false` e il gioco resta quello
    /// originale. E' la direzione giusta: un guasto di questa funzione non deve mai accendere un debuff.
    /// </summary>
    internal static class CoopGate
    {
        /// <summary>
        /// `true` solo con una connessione al server viva. Non lancia mai: qualunque guasto vale "single
        /// player", che e' lo stato inerte (nessuna scrittura, nessuna notifica, schermata normale).
        /// </summary>
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
