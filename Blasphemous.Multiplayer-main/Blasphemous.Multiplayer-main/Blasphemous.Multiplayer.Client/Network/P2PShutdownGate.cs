using Blasphemous.ModdingAPI;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// 🔴 IL CANCELLO DELLA CHIUSURA — "sto chiudendo: `CloseP2PSessionWithUser` **non** si chiama".
    ///
    /// PERCHE' ESISTE. Il crash misurato in uscita (`scratch\DIAGNOSI-CRASH-E-AI.md` §A) e' un
    /// **access violation NATIVO in `steam_api64`**, e lo stack e' questo:
    /// `Core.OnDestroy` → `ModLoader.Dispose` → `Multiplayer.OnDispose` → `HostSession.Stop` →
    /// `Server.Stop` → `CompositeServerTransport.stopAll` → `P2PServerTransportImpl.Stop` →
    /// `CloseP2PSessionWithUser` → `steam_api64`. Cioe': la `Close` aggiunta **per igiene** gira
    /// **mentre Steam sta gia' chiudendo**, e una chiamata nativa su un modulo in teardown e' il modo
    /// piu' diretto di far saltare il processo. `G2` (l'ospite) chiude pulito, `G1` (l'host) no: la
    /// differenza e' **quale** delle due `Close` di chiusura viene raggiunta.
    ///
    /// ⭐ LA REGOLA, e vale per tutte e due: *la `Close` non deve **mai** girare durante lo shutdown.*
    /// In chiusura **ci pensa il processo**: Steam chiude le sessioni da se' quando il processo muore,
    /// ed e' il limite che avevamo gia' dichiarato (`scratch\REFERTO-IGIENE-FIX.md`, "cosa NON e'
    /// coperto"). Le `Close` dei percorsi **di gioco** restano tutte, e sono quelle che contano:
    /// `Drop` (peer perso), `LinkLost`, `HandshakeExpired`, il bottone "Disconnect".
    ///
    /// ⚠️ CHI LO ALZA — **una riga sola**, la prima di `Multiplayer.OnDispose`, cioe' **prima** di
    /// `HostSession.Stop()`: da li' in poi nessun percorso di chiusura puo' piu' chiamare Steam per
    /// chiudere una sessione. Chi lo legge:
    ///   · `SteamP2PTransportImpl.End()` — il `CloseP2PSessionWithUser` del lato peer (il `Disconnect`
    ///     del `NetworkManager`, che in chiusura passa da li');
    ///   · `P2PServerTransportImpl.Stop()` — la `Close` **non c'e' piu'**: tolta, non messa dietro un
    ///     cancello (vedi il commento la' dentro). Il cancello resta qui perche' il lato peer un
    ///     percorso di chiusura ce l'ha ancora, e chiudere una sessione **non** e' un'operazione che si
    ///     possa fare "a meta'".
    ///
    /// ⚠️ E' `volatile` perche' `Stop` e' raggiungibile anche dal thread del TCP: una bandierina letta
    /// da un altro thread senza `volatile` e' esattamente il tipo di guasto che non si vede finche' non
    /// si vede.
    /// </summary>
    internal static class P2PShutdownGate
    {
        /// <summary>
        /// La riga che dichiara il passaggio. E' un marker **di proposito**: se il gioco chiude e questa
        /// riga **c'e'**, la chiusura e' passata dal cancello; se **non c'e'**, la chiusura non e'
        /// arrivata (crash, kill, morte del processo) e il crash va cercato altrove.
        /// </summary>
        internal const string MarkerEnter =
            "[Net] chiusura in corso: da qui in poi nessuna CloseP2PSessionWithUser";

        private static volatile bool _shuttingDown;

        /// <summary>
        /// Alza il cancello. Idempotente, non lancia, e si chiama **prima** di `HostSession.Stop()`:
        /// tutto cio' che viene dopo e' chiusura.
        /// </summary>
        internal static void Enter()
        {
            _shuttingDown = true;

            try
            {
                ModLog.Info(MarkerEnter +
                            " (la sessione P2P la chiude la morte del processo: e' il limite dichiarato)");
            }
            catch (System.Exception)
            {
                // Il cancello e' alzato lo stesso: un log che non si scrive non deve riaprirlo.
            }
        }

        /// <summary>"Sto chiudendo?" — letto dalle `Close` che girano in chiusura.</summary>
        internal static bool Active
        {
            get { return _shuttingDown; }
        }
    }
}
