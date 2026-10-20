using Blasphemous.ModdingAPI;
using System;
using System.Reflection;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// ⭐⭐ LA FACCIATA DI STEAM — l'UNICA classe che il resto della mod nomina.
///
/// QUESTA CLASSE NON NOMINA NESSUN TIPO `Steamworks`: ne' un campo, ne' un parametro, ne' una riga di
/// codice. E' una regola, non una preferenza, e nasce da un requisito vero: **la mod deve funzionare
/// anche su una copia NON-Steam** (GOG &amp; co.), dove `Assembly-CSharp-firstpass.dll` puo' non avere
/// affatto i tipi `Steamworks`. Il percorso TCP/IP e' la BASE; Steam e' un'aggiunta, e un'aggiunta non
/// puo' impedire alla base di partire.
///
/// PERCHE' BASTA ISOLARE, E PERCHE' NON BASTA IL `try/catch` DENTRO L'IMPLEMENTAZIONE.
/// Un tipo assente non produce "una funzione che non c'e'": produce una **`TypeLoadException`**, e
/// viene lanciata **quando la JIT compila il metodo che nomina quel tipo** — cioe' al **punto di
/// chiamata**, non dentro il metodo chiamato. Un `try/catch` scritto *dentro*
/// `SteamLobbyImpl.Tick()` non verrebbe mai raggiunto. Quindi:
///   · tutte le chiamate all'implementazione sono qui, dentro un `try/catch`, in **metodi di questa
///     classe** (mai direttamente nel codice dell'implementazione);
///   · prima di ogni chiamata c'e' un cancello che **chiede per riflessione** se il tipo
///     `Steamworks.SteamAPI` esiste davvero (`Type.GetType` + giro delle assembly caricate): se non
///     c'e', l'implementazione non viene **mai** toccata, quindi la sua JIT non parte, quindi niente
///     eccezione;
///   · se i tipi ci sono ma qualcosa esplode lo stesso, il `try/catch` di qui lo cattura e si scrive
///     un marker. In nessun caso un guasto di Steam esce da questa classe.
///
/// ⚠️ E' anche la ragione per cui i metodi di questa facciata hanno quella forma un po' ripetitiva:
/// ognuno e' un guscio — cancello, poi **una** chiamata, dentro un metodo suo. Non e' verbosita':
/// e' la separazione che rende la `TypeLoadException` catturabile (la JIT dell'implementazione parte
/// dalla chiamata, che sta dentro il `try`).
///
/// ⛔ NIENTE TRASPORTI QUI: la lobby porta l'IP e la porta dell'host, non i pacchetti. L'unica cosa
/// che questa facciata in piu' chiama e' la **sonda P2P** (`SteamP2PProbe`), che *misura* se
/// `SendP2PPacket` funziona e se il traffico va diretto o in relay: non trasporta niente e non tocca
/// il protocollo, `ICoopTransport`, il server o il gioco (vedi `SteamP2PProbeImpl`).
/// </summary>
internal static class SteamLobby
{
    /// <summary>
    /// ⭐ Il marker della connessione innescata da un invito (`[Steam] join: connessione a &lt;ip&gt;:&lt;porta&gt;`).
    ///
    /// Sta QUI e non nell'implementazione per una ragione meccanica che e' anche la regola di questa
    /// classe: `ConnectionDisplay` lo stampa, e `ConnectionDisplay` non deve nominare
    /// `SteamLobbyImpl`. Essendo una `const`, il compilatore la **copia** nel punto d'uso: nominarla
    /// non carica nessun'altra classe.
    /// </summary>
    internal const string MarkerJoinConnect = "[Steam] join: connessione a ";

    /// <summary>
    /// ⭐ Il marker della copia NON-Steam: Steamworks non c'e' proprio. E' una riga **informativa**,
    /// non un errore — la mod funziona lo stesso, con l'IP.
    /// </summary>
    internal const string MarkerNoSteamworks =
        "[Steam] non disponibile: i tipi Steamworks non ci sono in Assembly-CSharp-firstpass " +
        "(copia non-Steam?) - il percorso IP resta intatto";

    /// <summary>Il marker di un guasto catturato al confine.</summary>
    internal const string MarkerError = "[Steam] errore: ";

    private static bool _typesPresent;      // una risposta "si" e' definitiva
    private static bool _typesLogged;       // la riga "non ci sono" esce una volta sola
    private static bool _tickFailureLogged; // un guasto per frame non deve inondare il log
    private static long _nextProbeTicks;    // prossima sonda (riprova solo finche' il "no" dura)

    /// <summary>
    /// `true` se i tipi `Steamworks` esistono in questo processo — **per riflessione**, senza mai
    /// nominarli nel codice.
    ///
    /// Le due strade, in ordine: `Type.GetType` con il nome qualificato dell'assembly (veloce, e
    /// funziona se l'assembly e' caricabile per nome), poi un giro delle assembly **gia' caricate**
    /// (funziona anche se e' stata caricata con un nome diverso o da un percorso). `throwOnError:
    /// false` + `try/catch`: qui un guasto vale "non ci sono".
    ///
    /// ⚠️ Finche' la risposta e' "no" si riprova ogni `PROBE_SECONDS` — non a ogni frame, e non mai
    /// piu': Steamworks potrebbe comparire solo dopo che il gioco ha caricato firstpass.
    /// </summary>
    private static bool SteamworksPresent()
    {
        if (_typesPresent)
            return true;

        long now = DateTime.UtcNow.Ticks;
        if (now < _nextProbeTicks)
            return false;

        _nextProbeTicks = now + TimeSpan.TicksPerSecond * PROBE_SECONDS;

        try
        {
            if (Type.GetType(STEAM_API_TYPE + ", " + FIRSTPASS_ASSEMBLY, false) != null)
            {
                _typesPresent = true;
                return true;
            }

            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in loaded)
            {
                if (assembly == null)
                    continue;

                string name;
                try
                {
                    name = assembly.GetName().Name;
                }
                catch (Exception)
                {
                    continue;
                }

                if (name != FIRSTPASS_ASSEMBLY)
                    continue;

                if (assembly.GetType(STEAM_API_TYPE, false) != null)
                {
                    _typesPresent = true;
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Un guasto della sonda vale "non ci sono": e' la risposta prudente, e non tocca il gioco.
        }

        if (!_typesLogged)
        {
            _typesLogged = true;
            ModLog.Info(MarkerNoSteamworks);
        }

        return false;
    }

    /// <summary>
    /// "Steam c'e' e si puo' usare?" — il cancello della UI: bottone abilitato o no. **Non lancia.**
    /// Se i tipi mancano (copia non-Steam) e' `false` senza nemmeno toccare l'implementazione.
    /// </summary>
    internal static bool Available
    {
        get
        {
            if (!SteamworksPresent())
                return false;

            try
            {
                return QueryAvailable();
            }
            catch (Exception e)
            {
                Report("Available", e, false);
                return false;
            }
        }
    }

    /// <summary>`true` se una lobby Steam e' nostra adesso (host).</summary>
    internal static bool HasHostLobby
    {
        get
        {
            if (!SteamworksPresent())
                return false;

            try
            {
                return QueryHasHostLobby();
            }
            catch (Exception e)
            {
                Report("HasHostLobby", e, false);
                return false;
            }
        }
    }

    /// <summary>L'id della lobby che stiamo ospitando (0 se nessuna).</summary>
    internal static ulong HostLobbyId
    {
        get
        {
            if (!SteamworksPresent())
                return 0UL;

            try
            {
                return QueryHostLobbyId();
            }
            catch (Exception e)
            {
                Report("HostLobbyId", e, false);
                return 0UL;
            }
        }
    }

    /// <summary>
    /// ⭐ Si arma una volta, all'inizializzazione della mod: legge `+connect_lobby` dalla riga di
    /// comando e registra le callback. Se Steam non e' pronto non si perde niente: `Tick` riprova.
    /// </summary>
    internal static void Arm()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallArm();
        }
        catch (Exception e)
        {
            Report("Arm", e, false);
        }

        // ⭐ E LA SONDA P2P SI ARMA CON LA LOBBY: registra `P2PSessionRequest_t`, cioe' la callback che
        // **accetta** le sessioni. E' l'unica cosa della sonda che deve esistere anche a bottone non
        // premuto — il peer puo' cominciare a mandare quando vuole, e una richiesta scartata perche'
        // "non avevo ancora premuto" sarebbe un `rx=0` senza nessun errore da nessuna parte.
        // ⚠️ `SteamP2PProbe` e' una facciata come questa: ha il suo cancello e il suo `try/catch`, quindi
        // non lancia e non tocca niente se Steam (o i tipi Steamworks) non ci sono.
        SteamP2PProbe.Arm();
    }

    /// <summary>
    /// ⭐ Il ciclo per frame: il pump delle callback e le scadenze. Chiamato da
    /// `Multiplayer.OnLateUpdate` (zero bersagli Harmony nuovi).
    ///
    /// ⚠️ Il guasto di `Tick` si logga **una volta sola**: e' per frame, e un log inondato non aiuta
    /// nessuno. Se il guasto sparisce e torna, la riga non torna: e' un compromesso dichiarato, e il
    /// marker che conta (`prima callback ricevuta`) parla comunque.
    /// </summary>
    internal static void Tick()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallTick();
        }
        catch (Exception e)
        {
            Report("Tick", e, true);
        }

        // ⭐ E LA SONDA P2P AVANZA NELLO STESSO CICLO PER FRAME (zero bersagli Harmony nuovi, zero
        // MonoBehaviour nuovi). Sta DOPO il pump di proposito: l'accettazione di una sessione arriva da
        // una callback, e in questo frame deve essere gia' stata consegnata prima di leggere il canale.
        // ⚠️ Facciata separata, cancello e `try/catch` suoi: un guasto della sonda non ferma la lobby, e
        // a sonda spenta questa riga costa un `if` (nessun byte, nessuna chiamata nativa).
        SteamP2PProbe.Tick();
    }

    /// <summary>
    /// ⭐ "Steam: Host" — crea la lobby. L'avvio della sessione host resta al percorso che c'e' gia'
    /// ("Host session"), quando arriva `LobbyCreated_t`. `false` = non si e' potuto nemmeno chiedere.
    /// </summary>
    internal static bool StartHost(string hostName)
    {
        if (!SteamworksPresent())
            return false;

        try
        {
            return CallStartHost(hostName ?? string.Empty);
        }
        catch (Exception e)
        {
            Report("StartHost", e, false);
            return false;
        }
    }

    /// <summary>Esce dalla lobby (host o join). Idempotente, e non fa niente se non c'era nessuna lobby.</summary>
    internal static void LeaveLobby()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallLeaveLobby();
        }
        catch (Exception e)
        {
            Report("LeaveLobby", e, false);
        }
    }

    /// <summary>Consuma una volta sola "la lobby e' pronta" (`LobbyCreated_t`).</summary>
    internal static bool TryConsumeHostLobbyReady()
    {
        if (!SteamworksPresent())
            return false;

        try
        {
            return CallTryConsumeHostLobbyReady();
        }
        catch (Exception e)
        {
            Report("TryConsumeHostLobbyReady", e, false);
            return false;
        }
    }

    /// <summary>Consuma una volta sola l'indirizzo arrivato dalla lobby (invito accettato).</summary>
    internal static bool TryConsumeJoin(out string serverIp, out int port)
    {
        serverIp = string.Empty;
        port = 0;

        if (!SteamworksPresent())
            return false;

        try
        {
            return CallTryConsumeJoin(out serverIp, out port);
        }
        catch (Exception e)
        {
            Report("TryConsumeJoin", e, false);
            serverIp = string.Empty;
            port = 0;
            return false;
        }
    }

    /// <summary>Butta via le azioni Steam in attesa quando si e' gia' dentro una sessione.</summary>
    internal static void DropPendingWhileConnected()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallDropPendingWhileConnected();
        }
        catch (Exception e)
        {
            Report("DropPendingWhileConnected", e, false);
        }
    }

    // ─── I GUSCI VERSO L'IMPLEMENTAZIONE ─────────────────────────────────────────────────────────
    //
    // ⚠️ UN METODO PER CHIAMATA, e non e' verbosita' inutile: la JIT compila il corpo al primo uso,
    // quindi la `TypeLoadException` dell'implementazione (se i tipi `Steamworks` mancassero davvero)
    // nasce **qui dentro**, dentro il `try` del metodo che li ha invocati. Se queste righe stessero
    // direttamente nei metodi pubblici qui sopra, la JIT caricherebbe l'implementazione compilando
    // QUEI metodi, cioe' prima del `try`: un `catch` che non cattura mai niente.

    private static bool QueryAvailable()
    {
        return SteamLobbyImpl.Available;
    }

    private static bool QueryHasHostLobby()
    {
        return SteamLobbyImpl.HasHostLobby;
    }

    private static ulong QueryHostLobbyId()
    {
        return SteamLobbyImpl.HostLobbyId;
    }

    private static void CallArm()
    {
        SteamLobbyImpl.Arm();
    }

    private static void CallTick()
    {
        SteamLobbyImpl.Tick();
    }

    private static bool CallStartHost(string hostName)
    {
        return SteamLobbyImpl.StartHost(hostName);
    }

    private static void CallLeaveLobby()
    {
        SteamLobbyImpl.LeaveLobby();
    }

    private static bool CallTryConsumeHostLobbyReady()
    {
        return SteamLobbyImpl.TryConsumeHostLobbyReady();
    }

    private static bool CallTryConsumeJoin(out string serverIp, out int port)
    {
        return SteamLobbyImpl.TryConsumeJoin(out serverIp, out port);
    }

    private static void CallDropPendingWhileConnected()
    {
        SteamLobbyImpl.DropPendingWhileConnected();
    }

    /// <summary>
    /// Una riga di log per un guasto catturato al confine. `once` e' per i metodi per frame: un
    /// guasto che si ripete 60 volte al secondo non deve diventare 60 righe al secondo.
    /// </summary>
    private static void Report(string operation, Exception error, bool once)
    {
        try
        {
            if (once)
            {
                if (_tickFailureLogged)
                    return;

                _tickFailureLogged = true;
            }

            ModLog.Error(MarkerError + operation + ": " + error.GetType().Name + ": " + error.Message);
        }
        catch (Exception)
        {
            // Niente: se anche il log esplode, il gioco deve continuare.
        }
    }

    /// <summary>Il tipo che deve esistere, e l'assembly che lo porta (i due nomi delle cose).</summary>
    private const string STEAM_API_TYPE = "Steamworks.SteamAPI";
    private const string FIRSTPASS_ASSEMBLY = "Assembly-CSharp-firstpass";

    /// <summary>Ogni quanto si riprova la sonda finche' i tipi non ci sono.</summary>
    private const int PROBE_SECONDS = 2;
}
