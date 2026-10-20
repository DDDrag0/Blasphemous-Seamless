using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Host;
using Blasphemous.Multiplayer.Common;
using Steamworks;
using System;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// ⭐⭐ FASE B — LOBBY STEAM E INVITI, ADDITIVI, SOPRA IL TRASPORTO CHE C'E'.
///
/// COSA FA, IN DUE RIGHE: trasforma l'invito di un amico su Steam in una connessione IP gia'
/// compilata; e viceversa, apre una lobby quando si ospita, cosi' l'invito si manda dall'overlay.
/// **Non e' un trasporto**: i byte continuano a viaggiare sul TCP di sempre, verso il server di
/// sempre (`HostSession` / `SERVER-COOP`). La lobby e' solo un **punto d'incontro**: porta l'IP e la
/// porta dell'host, e nient'altro. Quindi il protocollo, il seam `ICoopTransport`, `TcpTransport`,
/// il server, `PlayerDisplay`, il campo nome, le skin, la pausa e la Colpa **non sono toccati**.
///
/// ⛔ NIENTE P2P, E NON E' UNA SVOLTA A META': questa build di Steamworks (SDK 1.42) non ha
/// `SteamNetworkingSockets` (0 occorrenze in firstpass), e il P2P legacy e' un altro lavoro
/// (`docs\STEAM_INVITES.md` §8-§9). Qui non si chiama `SendP2PPacket` ne' `AcceptP2PSessionWithUser`:
/// l'unica cosa che passa da Steam e' **il nome del posto, non i pacchetti**.
///
/// ─────────────────────────────────────────────────────────────────────────────────────────────
/// LA SEQUENZA, E PERCHE' OGNI PASSO ASPETTA IL PRECEDENTE (§2.1 del piano)
///
///   HOST   CreateLobby(friendsOnly, 4)   -> LobbyCreated_t { m_eResult, m_ulSteamIDLobby }
///          SetLobbyData(hostIp / hostPort / hostName)
///          ActivateGameOverlayInviteDialog(lobby)
///          e la sessione host parte dal bottone che esiste gia' ("Host session")
///
///   GUEST  +connect_lobby &lt;id&gt;   (gioco spento)  -> JoinLobby
///          GameLobbyJoinRequested_t     (gioco acceso) -> JoinLobby
///          -> LobbyEnter_t { m_EChatRoomEnterResponse }   (== 1, altrimenti e' un rifiuto CON MOTIVO)
///          -> ⭐ GetLobbyData(hostIp / hostPort) SUBITO, dalla cache
///          -> solo se sono vuoti: RequestLobbyData -> LobbyDataUpdate_t -> GetLobbyData
///          -> i campi della schermata -> Connect
///
/// 🔴 LA REGOLA DELLA LETTURA, nella forma giusta (e la prima versione del piano sbagliava):
///   · **dopo un `SetLobbyData` dell'host non si legge "nello stesso istante"**: il valore lo replica
///     il back end di Steam, e una `GetLobbyData` emessa subito dopo puo' tornare **stringa vuota** e
///     sembrare "lobby senza dati". Per l'**host** la regola vale, e la lettura la fa l'ospite;
///   · **per l'ospite, in `LobbyEnter_t`, si legge SUBITO dalla cache.** Aspettare sempre
///     `LobbyDataUpdate_t` appenderebbe il join: **Steam non manda quell'update se i dati sono gia' in
///     cache** (`RequestLobbyData` su metadati identici salta la rete), quindi il callback non
///     arriverebbe **mai** e il join resterebbe appeso per sempre — lo stesso sintomo di un click a
///     vuoto. La strada giusta: leggere subito; **se** i dati sono vuoti, allora `RequestLobbyData` e
///     si aspetta l'update. Vedi `OnLobbyEnter`.
///   · **`m_EChatRoomEnterResponse` dice se il join e' avvenuto** (`1` = successo): dopo un ingresso
///     fallito non ci sono dati da leggere, e il fallimento va **detto col suo motivo**, non
///     trasformato in una lobby vuota che sembra funzionare.
///
/// 🔴 E L'HOST CHE SE NE VA NON SI ANNUNCIA DA SOLO: la lobby **non muore** con lui (la proprieta'
/// passa al membro successivo), quindi senza `LobbyChatUpdate_t` l'ospite resterebbe dentro una
/// sessione morta senza saperlo. Vedi `OnLobbyChatUpdate`.
///
/// ─────────────────────────────────────────────────────────────────────────────────────────────
/// ⚠️ IL PUMP DELLE CALLBACK: LO CHIAMIAMO ANCHE NOI, E NON E' UN DOPPIONE.
/// `SteamAPI.RunCallbacks()` lo chiama gia' `SteamManager.Update` del gioco. Chiamarlo qui costa
/// quanto una coda vuota (la seconda chiamata del frame non trova niente da consegnare) e in cambio
/// ci rende **indipendenti dal ciclo di vita del gioco**: se il suo `SteamManager` non gira — o e'
/// stato distrutto in una scena — le NOSTRE callback continuano ad arrivare. Vedi `Tick()`.
///
/// ⚠️ E SE LE CALLBACK NON ARRIVANO, SI DICE: due scadenze (`HOST_CALLBACK_TIMEOUT_S`,
/// `JOIN_CALLBACK_TIMEOUT_S`) scrivono un marker d'errore invece di lasciare il bottone muto. Il
/// marker `[Steam] prima callback ricevuta:` e' la prova che il pump funziona: senza, il test e' cieco.
///
/// ⚠️ TUTTO IN `try/catch`: un guasto di Steam non deve rompere il gioco. Nessun metodo di questa
/// classe lancia verso chi la chiama, e soprattutto **nessuno la chiama direttamente**: la facciata
/// e' `SteamLobby` (vedi `SteamLobby.cs`), che e' l'unica classe che il resto della mod nomina.
///
/// 🔴 PERCHE' DUE CLASSI E NON UNA — LA COPIA NON-STEAM (GOG &amp; co.).
/// Su una build non-Steam `Assembly-CSharp-firstpass.dll` puo' **non contenere affatto** i tipi
/// `Steamworks`. Questa classe li nomina dappertutto (campi `CSteamID`, `Callback&lt;T&gt;`, ...), e un
/// riferimento a un tipo assente non e' "Steam non c'e'": e' una **`TypeLoadException`** lanciata
/// quando la JIT carica questa classe. Il percorso TCP/IP e' la BASE della mod e non deve poter
/// morire per un'aggiunta, quindi:
///   · **tutto** il codice che nomina `Steamworks` sta QUI, in una classe sola;
///   · il resto della mod nomina solo `SteamLobby`, che di tipi `Steamworks` non ne ha **nessuno**
///     (nemmeno nei campi o nelle firme) e che decide "ci sono i tipi?" **per riflessione**, prima;
///   · la `TypeLoadException` viene lanciata al **punto di chiamata** (la JIT di questa classe parte
///     quando la facciata la invoca), quindi e' catturabile dalla facciata stessa, che ha il
///     `try/catch` addosso.
/// ⇒ su una copia non-Steam: `[Steam] non disponibile` nel log, il bottone Steam disabilitato, e la
///   schermata IP **identica a prima**. Vedi `SteamLobby.MarkerNoSteamworks`.
/// </summary>
internal static class SteamLobbyImpl
{
    // ⭐ I MARKER — tutti nuovi, tutti col prefisso `[Steam]`, nessuno sostituisce una riga esistente.
    //    Servono a rispondere DAL LOG alle tre domande del test: Steam c'e'? il pump gira? i dati
    //    della lobby sono arrivati?
    internal const string MarkerAvailable = "[Steam] disponibile=";
    internal const string MarkerUnavailable = "[Steam] non disponibile";
    internal const string MarkerFirstCallback = "[Steam] prima callback ricevuta: ";
    internal const string MarkerHostCreate = "[Steam] host: CreateLobby ";
    internal const string MarkerHostCreated = "[Steam] host: lobby creata id=";
    internal const string MarkerHostFailed = "[Steam] host: creazione fallita ";
    internal const string MarkerHostData = "[Steam] host: SetLobbyData ";
    internal const string MarkerOverlayState = "[Steam] overlay abilitato=";
    internal const string MarkerOverlayOpen = "[Steam] apro l'overlay inviti lobby=";
    internal const string MarkerJoinRequest = "[Steam] join: richiesta lobby=";
    internal const string MarkerJoinEnter = "[Steam] join: LobbyEnter lobby=";
    internal const string MarkerJoinRefused = "[Steam] join rifiutato: ";
    internal const string MarkerJoinRequestData = "[Steam] join: RequestLobbyData lobby=";
    internal const string MarkerJoinData = "[Steam] join: LobbyDataUpdate lobby=";
    internal const string MarkerJoinNoData = "[Steam] join: dati mancanti ";
    internal const string MarkerJoinWaiting = "[Steam] join in attesa da ";
    internal const string MarkerJoinReady = "[Steam] join: dati pronti hostIp=";
    internal const string MarkerJoinDropped = "[Steam] join: scartato ";
    internal const string MarkerLobbyMember = "[Steam] lobby: membro cambiato ";
    internal const string MarkerLobbyOwner = "[Steam] lobby: proprietario=";
    internal const string MarkerHostLeft = "[Steam] l'host ha chiuso la sessione";
    internal const string MarkerLeft = "[Steam] lobby lasciata";
    internal const string MarkerTimeout = "[Steam] timeout: ";
    internal const string MarkerError = "[Steam] errore: ";

    // ⭐ LE CHIAVI DEI DATI DELLA LOBBY: `hostIp`, `hostPort`, `hostName`. Sono i tre nomi del piano
    //    (§1 e §4 del piano), e sono l'UNICA cosa che questa classe mette in comune.
    //    ⚠️ LA PASSWORD NON C'E', di proposito: il piano elenca quei tre dati e basta. Conseguenza
    //    dichiarata nel referto: se l'host mette una password, l'ospite la deve scrivere a mano.
    internal const string KEY_IP = "hostIp";
    internal const string KEY_PORT = "hostPort";
    internal const string KEY_NAME = "hostName";

    /// <summary>La lobby e' `friendsOnly` (inviti diretti, non compare nel matchmaking globale).</summary>
    private const ELobbyType LOBBY_TYPE = ELobbyType.k_ELobbyTypeFriendsOnly;

    /// <summary>Quattro posti: il piano. (Il server ne accetta 10, ma l'invito e' per un amico.)</summary>
    private const int MAX_MEMBERS = 4;

    /// <summary>`k_EChatRoomEnterResponseSuccess`: l'unico valore che significa "sono dentro".</summary>
    private const uint ENTER_SUCCESS = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;

    /// <summary>Quanti `RequestLobbyData` si ripetono se i dati non sono ancora replicati.</summary>
    private const int MAX_DATA_RETRIES = 3;

    /// <summary>Ogni quanto si riprova a chiedere a Steam se c'e' (solo finche' la risposta e' no).</summary>
    private const float RECHECK_SECONDS = 2f;

    /// <summary>Quanto si aspetta `LobbyCreated_t` prima di dire che le callback non arrivano.</summary>
    private const float HOST_CALLBACK_TIMEOUT_S = 10f;

    /// <summary>
    /// Dopo quanti secondi senza dati della lobby si scrive `[Steam] join in attesa da Ns` — cosi' un
    /// blocco **si vede** invece di sembrare un click a vuoto. Non e' un abbandono: si continua ad
    /// aspettare fino a `JOIN_CALLBACK_TIMEOUT_S`.
    /// </summary>
    private const float JOIN_DATA_WAIT_WARN_S = 5f;

    /// <summary>Quanto si aspetta `LobbyEnter_t` / `LobbyDataUpdate_t` prima di dichiarare il guasto.</summary>
    private const float JOIN_CALLBACK_TIMEOUT_S = 15f;

    // ─── I CALLBACK, COME CAMPI (⚠️ NON LOCALI: il GC li ucciderebbe) ─────────────────────────────
    //
    // `Callback<T>.Create` registra il delegato in un registro GLOBALE del processo e tiene un
    // `GCHandle` pinning sull'oggetto. Se il `Callback<T>` fosse una variabile locale di un metodo,
    // dopo il ritorno non ci sarebbe piu' nessun riferimento gestito: il GC puo' raccoglierlo, il
    // registro resta appeso a memoria liberata, e le callback **smettono di arrivare in silenzio**.
    // Quindi: cinque campi statici, vivi quanto la mod.
    private static Callback<LobbyCreated_t> _cbLobbyCreated;
    private static Callback<LobbyEnter_t> _cbLobbyEnter;
    private static Callback<LobbyDataUpdate_t> _cbLobbyDataUpdate;
    private static Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
    private static Callback<LobbyChatUpdate_t> _cbLobbyChatUpdate;

    // ─── STATO ────────────────────────────────────────────────────────────────────────────────────
    private static bool _available;          // Steam c'e' ed e' loggato (una volta vero, resta vero)
    private static bool _availabilityLogged; // una riga sola per transizione, non una per frame
    private static float _nextAvailabilityCheck;
    private static bool _callbacksReady;     // i cinque Callback<T> sono registrati
    private static bool _firstCallbackLogged;

    private static CSteamID _hostLobby = CSteamID.Nil;   // la lobby che stiamo ospitando (o Nil)
    private static bool _creatingLobby;
    private static float _creatingSince;
    private static string _hostName = string.Empty;
    private static bool _hostLobbyReady;     // ⭐ consumato dalla UI: "la lobby c'e', avvia l'host"

    private static CSteamID _joinLobby = CSteamID.Nil;   // la lobby che stiamo entrando (o Nil)
    private static bool _joinEntered;        // LobbyEnter_t con successo: siamo DENTRO
    private static float _joinSince;         // quando e' partito il JoinLobby
    private static float _joinDataSince;     // quando abbiamo cominciato ad aspettare i dati
    private static bool _joinWaitingLogged;  // il marker "in attesa da Ns" esce una volta sola
    private static int _joinDataRetries;
    private static bool _joinRetryPending;
    private static bool _joinDataDone;       // i dati sono arrivati (dalla cache o dall'update)
    private static bool _hostLeftReported;   // "l'host ha chiuso": una volta sola

    private static bool _joinReady;          // ⭐ consumato dalla UI: hostIp/hostPort pronti
    private static string _joinIp = string.Empty;
    private static int _joinPort;

    private static ulong _cmdLineLobbyId;    // 0 = nessun `+connect_lobby`
    private static bool _cmdLineRead;        // la riga di comando si legge UNA volta
    private static bool _cmdLineJoinStarted;

    /// <summary>
    /// `true` se Steam c'e' e si puo' usare: e' il cancello della UI (bottone abilitato o no).
    /// Comodo e **non** lancia: internamente e' tutto in `try/catch`.
    /// </summary>
    internal static bool Available
    {
        get
        {
            CheckAvailability();
            return _available;
        }
    }

    /// <summary>`true` se una lobby Steam e' nostra ADESSO (host).</summary>
    internal static bool HasHostLobby
    {
        get { return _hostLobby.m_SteamID != 0UL; }
    }

    /// <summary>L'id della lobby che stiamo ospitando, per il log/lo schermo. 0 se nessuna.</summary>
    internal static ulong HostLobbyId
    {
        get { return _hostLobby.m_SteamID; }
    }

    /// <summary>
    /// ⭐ LA LOBBY IN CUI SIAMO ADESSO — quella che ospitiamo se ne abbiamo una, altrimenti quella in cui
    /// siamo entrati. `CSteamID.Nil` se non siamo in nessuna lobby.
    ///
    /// Serve alla sonda P2P (`SteamP2PProbeImpl`), che deve mandare il suo byte ai **peer della lobby**:
    /// le due liste (`GetNumLobbyMembers`/`GetLobbyMemberByIndex`) sono le stesse che usa la lobby per
    /// l'invito, e non c'è un secondo elenco da tenere allineato.
    ///
    /// ⚠️ NON GUARDA SE UNA SESSIONE TCP È APERTA: è il punto della sonda — deve funzionare anche
    /// quando il TCP non arriva (secondo PC su un hotspot).
    /// </summary>
    internal static CSteamID CurrentLobby
    {
        get
        {
            if (_hostLobby.m_SteamID != 0UL)
                return _hostLobby;

            return _joinLobby;
        }
    }

    /// <summary>
    /// ⭐ "QUESTO UTENTE È UN MEMBRO DI UNA NOSTRA LOBBY?" — il cancello d'igiene delle sessioni P2P:
    /// si accetta una sessione solo da chi è nella lobby (`docs\STEAM_INVITES.md` §9), altrimenti
    /// chiunque conosca il nostro SteamID potrebbe farci aprire una sessione.
    ///
    /// Si guardano **entrambe** le lobby (host e join) e **non** solo `CurrentLobby`: se un giorno un
    /// processo fosse in due lobby insieme, il peer della seconda non deve sembrare uno sconosciuto.
    /// Non lancia: un guasto qui vale "non è un membro", che è la risposta prudente.
    /// </summary>
    internal static bool IsLobbyMember(CSteamID user)
    {
        try
        {
            if (user.m_SteamID == 0UL)
                return false;

            if (IsMemberOf(_hostLobby, user))
                return true;

            return IsMemberOf(_joinLobby, user);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Il giro dei membri di UNA lobby. Separato perché serve per due lobby (host e join).</summary>
    private static bool IsMemberOf(CSteamID lobby, CSteamID user)
    {
        if (lobby.m_SteamID == 0UL)
            return false;

        int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < members; i++)
        {
            CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (member.m_SteamID == user.m_SteamID)
                return true;
        }

        return false;
    }

    /// <summary>
    /// ⭐ SI ARMA UNA VOLTA, all'inizializzazione della mod: legge la riga di comando (il caso "gioco
    /// spento") e registra le callback se Steam e' gia' pronto. Idempotente: si puo' chiamare due volte.
    ///
    /// ⚠️ Se Steam non e' ancora pronto QUI (l'init del gioco puo' arrivare dopo la mod), non si perde
    /// niente: `Tick()` riprova ogni frame finche' non riesce. E' il motivo per cui questa funzione non
    /// ritorna un esito: non c'e' un "fallito", c'e' un "non ancora".
    /// </summary>
    internal static void Arm()
    {
        try
        {
            // ⭐ LA RIGA DI COMANDO — UNA VOLTA SOLA, prima di tutto il resto: e' l'unica cosa che
            // esiste quando il gioco e' stato avviato da un invito e nessuna callback puo' arrivare.
            if (!_cmdLineRead)
            {
                _cmdLineRead = true;

                ulong lobbyId;
                if (SteamInviteCommandLine.TryParse(Environment.GetCommandLineArgs(), out lobbyId))
                {
                    _cmdLineLobbyId = lobbyId;
                    ModLog.Info(MarkerJoinRequest + lobbyId + " origine=cmdline (" +
                                SteamInviteCommandLine.ARGUMENT + ")");
                }
            }

            CheckAvailability();

            if (_available)
                EnsureCallbacks();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// ⭐⭐ IL CICLO PER FRAME: il pump delle callback, e le azioni che aspettano una callback.
    ///
    /// Si aggancia al ciclo per frame che la mod ha gia' (`Multiplayer.OnLateUpdate`), quindi **zero
    /// bersagli Harmony nuovi e zero MonoBehaviour nuovi**.
    ///
    /// PERCHE' SI CHIAMA `RunCallbacks` ANCHE SE IL GIOCO LO FA GIA': vedi il commento in testa alla
    /// classe. In breve — chiamarlo due volte per frame non e' un problema (la seconda trova la coda
    /// vuota), e ci rende immuni al ciclo di vita dello `SteamManager` del gioco: se lui non gira,
    /// le nostre callback arrivano lo stesso. E' un costo nullo per un guasto che altrimenti sarebbe
    /// invisibile ("ho premuto e non e' successo niente").
    ///
    /// ⚠️ NON LANCIA MAI: e' dentro il `OnLateUpdate` della mod, e un'eccezione qui fermerebbe il
    /// resto del ciclo (pacchetti, giocatori, nemici) per un guasto di Steam.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            CheckAvailability();

            if (!_available)
                return;

            if (!_callbacksReady && !EnsureCallbacks())
                return;

            // ⭐ IL PUMP, CON IL SUO CANCELLO E IL SUO `try/catch` (vedi mandato): se il client Steam
            // e' chiuso non si pompa niente, e se la chiamata esplode si scrive un marker e si va
            // avanti. E' l'unica riga di questa classe che tocca il motore di callback.
            if (SteamAPI.IsSteamRunning())
                SteamAPI.RunCallbacks();

            // ① il join dal `+connect_lobby` aspetta solo che le callback siano pronte.
            if (_cmdLineLobbyId != 0UL && !_cmdLineJoinStarted)
            {
                _cmdLineJoinStarted = true;
                StartJoin(new CSteamID(_cmdLineLobbyId), "cmdline");
            }

            // ② il secondo `RequestLobbyData`, se il primo e' arrivato prima dei dati.
            if (_joinRetryPending)
            {
                _joinRetryPending = false;
                RequestJoinData();
            }

            // ③ le scadenze: se una callback non arriva, lo si dice (invece di restare muti).
            CheckTimeouts();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// ⭐ "STEAM: HOST" — crea la lobby, e **non** avvia niente: l'avvio della sessione host lo fa la
    /// UI riusando il percorso del bottone che esiste gia' ("Host session"), quando `LobbyCreated_t`
    /// e' arrivata (`TryConsumeHostLobbyReady`). Due cose distinte e in due momenti distinti, perche'
    /// la lobby senza host annuncerebbe una porta dove non ascolta nessuno.
    ///
    /// Ritorna `false` solo se Steam non c'e' o se la chiamata esplode: in quel caso la UI lo dice.
    /// </summary>
    internal static bool StartHost(string hostName)
    {
        try
        {
            CheckAvailability();

            if (!_available)
            {
                ModLog.Warn(MarkerUnavailable + ": StartHost richiesto ma Steam non e' disponibile");
                return false;
            }

            if (!EnsureCallbacks())
                return false;

            _hostName = hostName ?? string.Empty;

            // ⭐ Una lobby nostra c'e' gia': NON se ne crea una seconda (sarebbe la seconda lobby
            // orfana della stessa sessione). Si riapre l'overlay degli inviti, che e' quello che
            // l'utente vuole fare ripremendo il bottone.
            if (HasHostLobby)
            {
                OpenInviteOverlay("riapertura");
                return true;
            }

            // Doppio clic mentre la creazione e' in volo: si aspetta quella.
            if (_creatingLobby)
                return true;

            _creatingLobby = true;
            _creatingSince = Time.realtimeSinceStartup;
            _hostLobbyReady = false;

            SteamAPICall_t call = SteamMatchmaking.CreateLobby(LOBBY_TYPE, MAX_MEMBERS);
            ModLog.Info(MarkerHostCreate + "tipo=friendsOnly posti=" + MAX_MEMBERS +
                        " chiamata=" + call.m_SteamAPICall);
            return true;
        }
        catch (Exception e)
        {
            _creatingLobby = false;
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (CreateLobby)");
            return false;
        }
    }

    /// <summary>
    /// ⭐ Esce dalla lobby (host o join) e libera lo stato. Idempotente: se non c'e' nessuna lobby non
    /// fa niente e **non stampa niente** (cosi' "Disconnect" di chi non usa Steam non aggiunge righe).
    ///
    /// Serve perche' una lobby che resta aperta e' peggio di un errore: annuncia una sessione che non
    /// c'e' piu', e chi la vede dall'overlay entra in un vicolo cieco.
    /// </summary>
    internal static void LeaveLobby()
    {
        try
        {
            CSteamID host = _hostLobby;
            CSteamID join = _joinLobby;

            ResetHostState();
            ResetJoinState();

            if (host.m_SteamID != 0UL)
            {
                SteamMatchmaking.LeaveLobby(host);
                ModLog.Info(MarkerLeft + " id=" + host.m_SteamID + " (host)");
            }

            if (join.m_SteamID != 0UL && join.m_SteamID != host.m_SteamID)
            {
                SteamMatchmaking.LeaveLobby(join);
                ModLog.Info(MarkerLeft + " id=" + join.m_SteamID + " (join)");
            }
        }
        catch (Exception e)
        {
            // Lo stato e' gia' stato azzerato: la lobby puo' restare appesa sul back end, ma il gioco
            // non ne sa piu' niente e non ci si appende mai piu'.
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LeaveLobby)");
        }
    }

    /// <summary>
    /// ⭐ "LA LOBBY E' PRONTA": consuma una volta sola il segnale di `LobbyCreated_t`. La UI lo chiama
    /// prima dei bottoni, e se ritorna `true` avvia la sessione host col percorso che c'e' gia'.
    /// </summary>
    internal static bool TryConsumeHostLobbyReady()
    {
        if (!_hostLobbyReady)
            return false;

        _hostLobbyReady = false;
        return true;
    }

    /// <summary>
    /// ⭐ "I DATI SONO ARRIVATI": consuma una volta sola l'indirizzo letto dalla lobby (subito in
    /// `LobbyEnter_t` dalla cache, oppure dopo `LobbyDataUpdate_t` — vedi `TryReadJoinData`). La UI
    /// compila i campi e connette.
    /// </summary>
    internal static bool TryConsumeJoin(out string serverIp, out int port)
    {
        serverIp = _joinIp;
        port = _joinPort;

        if (!_joinReady)
            return false;

        _joinReady = false;
        return true;
    }

    /// <summary>
    /// ⭐ "SEI GIA' CONNESSO, L'INVITO NON SI APPLICA QUI": la schermata chiama questo quando mostra
    /// lo stato connesso. Butta via un join in attesa (con una riga, non in silenzio) e una lobby
    /// d'host creata ma mai avviata (che altrimenti partirebbe al prossimo `Disconnect`), perche'
    /// **un invito non deve riconnettersi da solo mezz'ora dopo**.
    ///
    /// Non fa niente (e non stampa niente) se non c'era niente in attesa: il percorso normale non
    /// paga nemmeno un confronto.
    /// </summary>
    internal static void DropPendingWhileConnected()
    {
        try
        {
            // ⚠️ SOLO IL DATO NON ANCORA CONSUMATO (`_joinReady`), non "qualunque lobby in cui sono":
            //    dopo un join riuscito si RESTA nella lobby per tutta la partita (serve al
            //    `LobbyChatUpdate_t` per accorgersi che l'host e' uscito), quindi `_joinLobby != Nil`
            //    e' lo stato NORMALE di chi sta giocando — trattarlo come "in attesa" vorrebbe dire una
            //    notifica a ogni frame.
            if (_joinReady)
            {
                ModLog.Warn(MarkerJoinDropped + "gia' connessi a una sessione: invito non applicato");
                _joinReady = false;
                Notify("Steam: sei gia' connesso, invito ignorato");
            }

            // Una lobby d'host senza sessione host avviata annuncerebbe una porta chiusa.
            if (_hostLobbyReady && !HostSession.IsRunning)
            {
                _hostLobbyReady = false;
                LeaveLobby();
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (DropPending)");
        }
    }

    // ─── CALLBACK ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// `LobbyCreated_t` — la risposta a `CreateLobby`. ⭐ E' QUI che l'host pubblica i dati e apre
    /// l'overlay degli inviti: prima non c'e' nessuna lobby in cui invitare.
    ///
    /// ⚠️ `ActivateGameOverlayInviteDialog` SOLO con `k_EResultOK`: su un errore l'overlay si aprirebbe
    /// con una lista amici vuota (o con un errore), cioe' un dialogo che non porta a niente.
    /// </summary>
    private static void OnLobbyCreated(LobbyCreated_t result)
    {
        NoteCallback("LobbyCreated_t");

        try
        {
            if (result.m_eResult != EResult.k_EResultOK)
            {
                _creatingLobby = false;
                _hostLobbyReady = false;
                ModLog.Error(MarkerHostFailed + "risultato=" + result.m_eResult);
                Notify("Steam: creazione lobby fallita (" + result.m_eResult + ")");
                return;
            }

            _creatingLobby = false;
            _hostLobby = new CSteamID(result.m_ulSteamIDLobby);

            ModLog.Info(MarkerHostCreated + _hostLobby.m_SteamID);

            // ⭐ I TRE DATI DELL'HOST — l'IP LAN e' quello che il peer scriverebbe a mano nel campo
            // "Server IP", la porta e' quella della sessione host (`Protocol.DEFAULT_PORT`, 27051).
            // `HostSession.LanAddress` e' lo stesso indirizzo che il marker `[Host] indirizzo locale=`
            // gia' stampa: non se ne calcola un secondo, cosi' i due non possono divergere.
            string ip = HostSession.LanAddress;
            int port = Protocol.DEFAULT_PORT;

            bool okIp = SteamMatchmaking.SetLobbyData(_hostLobby, KEY_IP, ip);
            bool okPort = SteamMatchmaking.SetLobbyData(_hostLobby, KEY_PORT, port.ToString());
            bool okName = SteamMatchmaking.SetLobbyData(_hostLobby, KEY_NAME, _hostName ?? string.Empty);

            ModLog.Info(MarkerHostData + KEY_IP + "=" + ip + " " + KEY_PORT + "=" + port +
                        " " + KEY_NAME + "=" + (_hostName ?? string.Empty) +
                        " esiti=" + (okIp ? "1" : "0") + (okPort ? "1" : "0") + (okName ? "1" : "0"));

            if (string.IsNullOrEmpty(ip) || ip == "sconosciuto")
                ModLog.Warn(MarkerHostData + "attenzione: l'IP LAN non e' stato risolto (" +
                            (ip ?? "(vuoto)") + "): la lobby non porta un indirizzo utile");

            // 🔴 QUI NON SI LEGGE NIENTE: `GetLobbyData` adesso tornerebbe vuota. La lettura la fa
            // l'ospite dentro `LobbyDataUpdate_t` (vedi `OnLobbyDataUpdate`).

            OpenInviteOverlay("creazione");

            // ⭐ E ADESSO LA UI PUO' AVVIARE L'HOST (percorso del bottone "Host session").
            _hostLobbyReady = true;
        }
        catch (Exception e)
        {
            _creatingLobby = false;
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyCreated_t)");
        }
    }

    /// <summary>
    /// `GameLobbyJoinRequested_t` — il caso **"gioco acceso"**: un amico ha accettato l'invito mentre
    /// il gioco era aperto, e Steam lo dice a QUESTO processo. La callback dice che l'invito e' stato
    /// accettato: non e' un ingresso (quello e' `LobbyEnter_t`), quindi qui si chiama `JoinLobby` e si
    /// aspetta, come per il caso "gioco spento".
    /// </summary>
    private static void OnJoinRequested(GameLobbyJoinRequested_t request)
    {
        NoteCallback("GameLobbyJoinRequested_t");

        try
        {
            CSteamID lobby = request.m_steamIDLobby;
            ModLog.Info(MarkerJoinRequest + lobby.m_SteamID + " origine=callback (amico=" +
                        request.m_steamIDFriend.m_SteamID + ")");
            StartJoin(lobby, "callback");
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (GameLobbyJoinRequested_t)");
        }
    }

    /// <summary>
    /// `LobbyEnter_t` — "sono dentro o no". 🔴 `m_EChatRoomEnterResponse`: `1` (`Success`) e' l'unico
    /// valore che significa lobby nostra; ogni altro va detto **col suo motivo** (sessione sparita,
    /// non amici, piena, bannati...), non con un "errore" generico, perche' il motivo e' esattamente
    /// quello che l'utente deve correggere.
    ///
    /// ⭐⭐ POI SI LEGGE **SUBITO**, DALLA CACHE. Questa e' la correzione che ha sostituito la regola
    /// "aspetta `LobbyDataUpdate_t`", e la ragione e' concreta: **Steam non manda `LobbyDataUpdate_t`
    /// se i dati sono gia' in cache** — `RequestLobbyData` su una lobby con metadati identici salta la
    /// rete, e il callback non arriva mai. Aspettarlo sempre vorrebbe dire appendere il join per
    /// sempre, col sintomo identico a un click a vuoto. Quindi:
    ///
    ///   · `GetLobbyData` **qui**, adesso: se `hostIp`/`hostPort` ci sono, si connette subito;
    ///   · **solo se sono vuoti** (lobby appena creata, dati non ancora replicati) si chiede
    ///     `RequestLobbyData` e **allora** si aspetta `LobbyDataUpdate_t`.
    ///
    /// ⚠️ E resta vera l'altra regola: dopo un **`SetLobbyData` dell'host** non si legge "nello stesso
    /// istante" (li' il valore non e' ancora replicato). Le due cose non si contraddicono: qui il join
    /// e' un `GetLobbyData` su una lobby di cui Steam ha appena confermato l'ingresso, e la cache o ha
    /// i dati o e' vuota — e se e' vuota c'e' la strada del `RequestLobbyData`.
    /// </summary>
    private static void OnLobbyEnter(LobbyEnter_t enter)
    {
        NoteCallback("LobbyEnter_t");

        try
        {
            CSteamID lobby = new CSteamID(enter.m_ulSteamIDLobby);
            uint response = enter.m_EChatRoomEnterResponse;
            string reason = EnterReason(response);

            ModLog.Info(MarkerJoinEnter + lobby.m_SteamID + " risposta=" + response +
                        (response == ENTER_SUCCESS ? " (successo)" : " (" + reason + ")"));

            // ⚠️ La NOSTRA lobby: se Steam manda un `LobbyEnter_t` anche all'host (varia per SDK),
            // non e' un join e non si deve leggere niente: si ignora.
            if (lobby.m_SteamID == _hostLobby.m_SteamID && _hostLobby.m_SteamID != 0UL)
            {
                ModLog.Info(MarkerJoinEnter + "e' la lobby che ospitiamo noi: nessun join da fare");
                return;
            }

            if (response != ENTER_SUCCESS)
            {
                ModLog.Error(MarkerJoinRefused + reason + " (risposta=" + response + ", lobby=" + lobby.m_SteamID + ")");
                Notify("Steam: invito non accettato - " + reason);
                ResetJoinState();
                return;
            }

            _joinLobby = lobby;
            _joinEntered = true;
            _joinDataSince = Time.realtimeSinceStartup;
            _joinWaitingLogged = false;
            _joinDataRetries = 0;
            _joinDataDone = false;
            _hostLeftReported = false;

            // ⭐ PRIMO TENTATIVO: la cache, subito, senza nessuna attesa.
            if (TryReadJoinData("cache"))
                return;

            // ⭐ RIPIEGO (caso raro: dati non ancora replicati): si chiede e si aspetta l'update.
            RequestJoinData();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyEnter_t)");
        }
    }

    /// <summary>
    /// `LobbyDataUpdate_t` — il ripiego: arriva quando i dati sono stati replicati dopo un
    /// `RequestLobbyData`. Il primo tentativo (la cache, dentro `LobbyEnter_t`) e' gia' stato fatto.
    ///
    /// `m_bSuccess` a zero = la richiesta non e' andata a buon fine (lobby sparita, dati non
    /// disponibili): non si legge niente, si dice.
    ///
    /// Se i dati sono ancora vuoti si ritenta `RequestLobbyData` (al massimo `MAX_DATA_RETRIES` volte,
    /// una per frame): l'update puo' riferirsi a un altro dato della lobby. **Bounded**: nessun ciclo
    /// infinito, e dopo il limite si scrive un marker d'errore invece di aspettare per sempre.
    /// </summary>
    private static void OnLobbyDataUpdate(LobbyDataUpdate_t update)
    {
        NoteCallback("LobbyDataUpdate_t");

        try
        {
            CSteamID lobby = new CSteamID(update.m_ulSteamIDLobby);

            // Non e' la lobby che stiamo entrando (puo' essere la nostra, da host): non e' affar nostro.
            if (_joinLobby.m_SteamID == 0UL || lobby.m_SteamID != _joinLobby.m_SteamID)
                return;

            string ip = SteamMatchmaking.GetLobbyData(lobby, KEY_IP);
            string portText = SteamMatchmaking.GetLobbyData(lobby, KEY_PORT);
            string hostName = SteamMatchmaking.GetLobbyData(lobby, KEY_NAME);

            ModLog.Info(MarkerJoinData + lobby.m_SteamID + " successo=" + update.m_bSuccess +
                        " " + KEY_IP + "=" + (ip ?? string.Empty) +
                        " " + KEY_PORT + "=" + (portText ?? string.Empty) +
                        " " + KEY_NAME + "=" + (hostName ?? string.Empty));

            if (update.m_bSuccess == 0)
            {
                ModLog.Error(MarkerJoinNoData + "LobbyDataUpdate_t con successo=0 lobby=" + lobby.m_SteamID);
                RetryJoinData();
                return;
            }

            if (TryReadJoinData("update"))
                return;

            ModLog.Warn(MarkerJoinNoData + "hostIp/hostPort non leggibili ancora (ip='" +
                        (ip ?? string.Empty) + "' porta='" + (portText ?? string.Empty) + "')");
            RetryJoinData();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyDataUpdate_t)");
        }
    }

    /// <summary>
    /// ⭐⭐ "L'HOST HA CHIUSO LA SESSIONE" — `LobbyChatUpdate_t`.
    ///
    /// LA LOBBY NON MUORE CON L'HOST: quando il proprietario esce, la proprieta' **passa al membro
    /// successivo**, quindi la lobby resta viva e — senza questa intercettazione — l'ospite
    /// resterebbe dentro una sessione morta senza nessun avviso (ne' una disconnessione, perche' il
    /// TCP l'ha gia' persa in silenzio o la perdera' al prossimo invio).
    ///
    /// IL SEGNALE: qualcuno se n'e' andato (`Left`/`Disconnected`) **e** il proprietario ora sono io.
    /// Le due condizioni insieme dicono "il padrone di prima non c'e' piu'" senza doverlo sapere per
    /// nome. Una volta sola (`_hostLeftReported`), e solo se ero davvero un ospite (`_joinEntered`):
    /// all'host che vede uscire un amico non si dice che "l'host ha chiuso".
    /// </summary>
    private static void OnLobbyChatUpdate(LobbyChatUpdate_t update)
    {
        NoteCallback("LobbyChatUpdate_t");

        try
        {
            CSteamID lobby = new CSteamID(update.m_ulSteamIDLobby);

            if (_joinLobby.m_SteamID == 0UL || lobby.m_SteamID != _joinLobby.m_SteamID)
                return;

            uint change = update.m_rgfChatMemberStateChange;
            bool leaving = (change & (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft) != 0 ||
                           (change & (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected) != 0 ||
                           (change & (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked) != 0;

            ModLog.Info(MarkerLobbyMember + "lobby=" + lobby.m_SteamID +
                        " membro=" + update.m_ulSteamIDUserChanged + " cambiamento=" + change);

            if (!leaving || !_joinEntered || _hostLeftReported)
                return;

            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            CSteamID me = SteamUser.GetSteamID();
            bool iAmOwnerNow = owner.m_SteamID == me.m_SteamID;

            ModLog.Info(MarkerLobbyOwner + owner.m_SteamID + " io=" + me.m_SteamID +
                        " (ioProprietario=" + (iAmOwnerNow ? "si" : "no") + ")");

            if (!iAmOwnerNow)
                return;

            _hostLeftReported = true;

            ModLog.Warn(MarkerHostLeft + " lobby=" + lobby.m_SteamID +
                        ": il proprietario della lobby ora sono io, quindi il vecchio host e' uscito");

            Notify("Steam: l'host ha chiuso la sessione");

            // ⚠️ La disconnessione si chiama SOLO se c'e' davvero una connessione: `NetworkManager.
            // Disconnect` non controlla che il trasporto esista, e da qui (dentro una callback) non
            // si deve poter far esplodere il gioco per una lobby lasciata a meta'.
            if (Main.Multiplayer != null && Main.Multiplayer.NetworkManager != null &&
                Main.Multiplayer.NetworkManager.IsConnected)
            {
                Main.Multiplayer.NetworkManager.Disconnect();
            }

            // E la lobby si lascia: non e' piu' la sessione di nessuno.
            LeaveLobby();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (LobbyChatUpdate_t)");
        }
    }

    /// <summary>
    /// ⭐ LA LETTURA DEI DATI, in un posto solo (la usano la cache di `LobbyEnter_t` e
    /// `LobbyDataUpdate_t`). Ritorna `true` se `hostIp`/`hostPort` sono leggibili e validi; in quel
    /// caso i campi sono pronti per la UI.
    ///
    /// `where` finisce nel log e dice **da dove** sono arrivati (`cache` = subito dopo l'ingresso,
    /// `update` = dopo `LobbyDataUpdate_t`): e' la riga che permette di distinguere i due percorsi
    /// nel test.
    /// </summary>
    private static bool TryReadJoinData(string where)
    {
        try
        {
            if (_joinLobby.m_SteamID == 0UL)
                return false;

            string ip = SteamMatchmaking.GetLobbyData(_joinLobby, KEY_IP);
            string portText = SteamMatchmaking.GetLobbyData(_joinLobby, KEY_PORT);
            string hostName = SteamMatchmaking.GetLobbyData(_joinLobby, KEY_NAME);

            int port;
            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(portText) ||
                !int.TryParse(portText, out port) || port <= 0 || port > 65535)
                return false;

            _joinIp = ip;
            _joinPort = port;
            _joinReady = true;
            _joinDataDone = true;

            ModLog.Info(MarkerJoinReady + ip + " porta=" + port +
                        " host=" + (hostName ?? string.Empty) + " da=" + where);
            Notify("Steam: invito accettato, connessione a " + ip + ":" + port);
            return true;
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (GetLobbyData)");
            return false;
        }
    }

    /// <summary>
    /// Il motivo di un `EChatRoomEnterResponse`, in una frase leggibile. I valori sono quelli
    /// dell'enum di questa build (`EChatRoomEnterResponse`); ogni motivo dice **cosa fare**, perche'
    /// "errore" non e' un'informazione.
    /// </summary>
    private static string EnterReason(uint response)
    {
        switch ((EChatRoomEnterResponse)response)
        {
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess:
                return "successo";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                return "la sessione non esiste piu' (invito vecchio)";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                return "non hai i permessi: controlla di essere amico su Steam";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                return "la sessione e' piena";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                return "sei stato bannato da questa lobby";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited:
                return "lobby in modalita' limitata";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou:
                return "l'altro giocatore ti ha bloccato su Steam";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember:
                return "hai bloccato l'altro giocatore su Steam";

            case EChatRoomEnterResponse.k_EChatRoomEnterResponseError:
                return "errore generico della lobby";

            default:
                return "risposta sconosciuta (" + response + ")";
        }
    }

    // ─── AIUTANTI ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Entra in una lobby. Un solo punto, per entrambi i casi (cmdline e callback).</summary>
    private static void StartJoin(CSteamID lobby, string origin)
    {
        try
        {
            if (lobby.m_SteamID == 0UL)
                return;

            // ⚠️ Gia' dentro una sessione TCP: l'invito non si applica, e lo si dice. Senza questo
            // controllo l'invito verrebbe "lavorato" e poi buttato via dalla UI, lasciando nel log una
            // sequenza di join che sembra andata a buon fine.
            if (IsConnectedNow())
            {
                ModLog.Warn(MarkerJoinDropped + "gia' connessi a una sessione: lobby=" + lobby.m_SteamID +
                            " origine=" + origin);
                Notify("Steam: sei gia' connesso, invito ignorato");
                return;
            }

            // Un ingresso gia' in corso non si duplica.
            if (_joinLobby.m_SteamID != 0UL)
            {
                ModLog.Info(MarkerJoinRequest + lobby.m_SteamID + " ignorata: c'e' gia' un ingresso in corso (id=" +
                            _joinLobby.m_SteamID + ")");
                return;
            }

            _joinLobby = lobby;
            _joinEntered = false;
            _joinSince = Time.realtimeSinceStartup;
            _joinDataSince = Time.realtimeSinceStartup;
            _joinWaitingLogged = false;
            _joinDataRetries = 0;
            _joinDataDone = false;
            _joinReady = false;
            _hostLeftReported = false;
            _joinIp = string.Empty;
            _joinPort = 0;

            SteamAPICall_t call = SteamMatchmaking.JoinLobby(lobby);
            ModLog.Info(MarkerJoinRequest + lobby.m_SteamID + " -> JoinLobby origine=" + origin +
                        " chiamata=" + call.m_SteamAPICall);
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (JoinLobby)");
        }
    }

    /// <summary>
    /// "C'e' una sessione aperta adesso?" — letto dal `NetworkManager`, senza toccarlo. Serve a non
    /// applicare un invito a chi sta gia' giocando (e a non far partire un dial sopra una connessione).
    /// </summary>
    private static bool IsConnectedNow()
    {
        try
        {
            return Main.Multiplayer != null &&
                   Main.Multiplayer.NetworkManager != null &&
                   Main.Multiplayer.NetworkManager.IsConnected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void RequestJoinData()
    {
        try
        {
            if (_joinLobby.m_SteamID == 0UL)
                return;

            bool requested = SteamMatchmaking.RequestLobbyData(_joinLobby);
            ModLog.Info(MarkerJoinRequestData + _joinLobby.m_SteamID + " inviata=" + (requested ? "si" : "no"));
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (RequestLobbyData)");
        }
    }

    /// <summary>
    /// Un altro `RequestLobbyData`, se ne restano. Non si chiama dentro la callback (sarebbe una
    /// raffica nella stessa pila): si alza una bandierina e la spende `Tick()`, una volta per frame.
    /// </summary>
    private static void RetryJoinData()
    {
        if (_joinDataRetries >= MAX_DATA_RETRIES)
        {
            ModLog.Error(MarkerJoinNoData + "dopo " + MAX_DATA_RETRIES + " tentativi: la lobby " +
                        _joinLobby.m_SteamID + " non porta " + KEY_IP + "/" + KEY_PORT +
                        " (host che non ha pubblicato i dati, o dati non replicati)");
            return;
        }

        _joinDataRetries++;
        _joinRetryPending = true;
    }

    /// <summary>
    /// ⭐ L'OVERLAY DEGLI INVITI, aperto con due righe di log PRIMA della chiamata.
    ///
    /// PERCHE' DUE RIGHE: se l'overlay e' **disattivato** nelle impostazioni di Steam, la chiamata
    /// **fallisce in silenzio** — nessuna eccezione, nessun codice di ritorno. Il sintomo e' "clicco e
    /// non succede niente", e senza queste due righe nel log quel sintomo non si distingue da "il
    /// codice non ci e' mai arrivato". Con `[Steam] overlay abilitato=no` il test ha la risposta.
    /// </summary>
    private static void OpenInviteOverlay(string reason)
    {
        try
        {
            bool enabled = SteamUtils.IsOverlayEnabled();
            ModLog.Info(MarkerOverlayState + (enabled ? "si" : "no") + " (motivo=" + reason + ")");
            ModLog.Info(MarkerOverlayOpen + _hostLobby.m_SteamID + " motivo=" + reason);

            SteamFriends.ActivateGameOverlayInviteDialog(_hostLobby);

            if (!enabled)
            {
                ModLog.Warn(MarkerOverlayState + "no: gli inviti non si apriranno. Impostazioni di Steam -> " +
                            "In gioco -> 'Abilita l'overlay di Steam'");
                Notify("Steam: l'overlay e' disattivato, l'invito non si apre (vedi log)");
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (ActivateGameOverlayInviteDialog)");
        }
    }

    /// <summary>
    /// Le scadenze. Servono a una cosa sola: **dire** che qualcosa non e' arrivato. Senza, un
    /// `CreateLobby` senza risposta o un `JoinLobby` senza `LobbyEnter_t` sarebbero un bottone che non
    /// fa niente e nessuna riga che lo spiega.
    ///
    /// ⭐ E c'e' la terza: **"join in attesa da Ns: dati lobby vuoti"** (dopo `JOIN_DATA_WAIT_WARN_S`).
    /// E' la riga che rende visibile un blocco invece di lasciarlo sembrare un click a vuoto — che e'
    /// esattamente il sintomo con cui un join appeso si presenta.
    /// </summary>
    private static void CheckTimeouts()
    {
        float now = Time.realtimeSinceStartup;

        if (_creatingLobby && now - _creatingSince >= HOST_CALLBACK_TIMEOUT_S)
        {
            _creatingLobby = false;
            ModLog.Error(MarkerTimeout + "nessuna LobbyCreated_t entro " + HOST_CALLBACK_TIMEOUT_S +
                         " s da CreateLobby: le callback non stanno arrivando (pump fermo?)");
            Notify("Steam: nessuna risposta da Steam per la lobby (vedi log)");
        }

        if (_joinLobby.m_SteamID == 0UL || _joinDataDone)
            return;

        float waited = now - (_joinEntered ? _joinDataSince : _joinSince);

        // "join in attesa": una riga sola, con i secondi veri.
        if (_joinEntered && !_joinWaitingLogged && waited >= JOIN_DATA_WAIT_WARN_S)
        {
            _joinWaitingLogged = true;
            ModLog.Warn(MarkerJoinWaiting + (int)waited + " s: dati lobby vuoti (lobby=" +
                        _joinLobby.m_SteamID + "): l'host non ha ancora pubblicato " + KEY_IP + "/" +
                        KEY_PORT + ", oppure la replica non e' arrivata");
        }

        if (waited >= JOIN_CALLBACK_TIMEOUT_S)
        {
            ModLog.Error(MarkerTimeout + (_joinEntered ? "nessuna LobbyDataUpdate_t" : "nessun LobbyEnter_t") +
                         " entro " + JOIN_CALLBACK_TIMEOUT_S + " s per la lobby " + _joinLobby.m_SteamID +
                         ": le callback non stanno arrivando (pump fermo?)");
            Notify("Steam: nessuna risposta da Steam per l'invito (vedi log)");
            ResetJoinState();
        }
    }

    /// <summary>
    /// Registra i quattro `Callback&lt;T&gt;` (una volta sola) e scrive il marker di disponibilita'.
    ///
    /// ⚠️ Si registra SOLO se Steam e' pronto: `Callback&lt;T&gt;.Create` parla con l'API nativa, e
    /// registrare prima dell'`Init` del gioco sarebbe un guasto in un punto che non controlliamo.
    /// </summary>
    private static bool EnsureCallbacks()
    {
        if (_callbacksReady)
            return true;

        CheckAvailability();
        if (!_available)
            return false;

        try
        {
            _cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _cbLobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _cbLobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            _cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            _cbLobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);

            _callbacksReady = true;

            ModLog.Info(MarkerAvailable + "si steamId=" + SteamUser.GetSteamID().m_SteamID +
                        " callbacks=5 (LobbyCreated_t, LobbyEnter_t, LobbyDataUpdate_t, " +
                        "GameLobbyJoinRequested_t, LobbyChatUpdate_t)");
            return true;
        }
        catch (Exception e)
        {
            _callbacksReady = false;
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Callback<T>.Create)");
            return false;
        }
    }

    /// <summary>
    /// "Steam c'e' e si puo' usare?" — `IsSteamRunning` (il client e' acceso), `BLoggedOn` (c'e' un
    /// account) e uno SteamID valido. Una volta vero **resta vero** (non si spegne il bottone a metà
    /// partita se il client fa i capricci); finche' e' falso si riprova ogni `RECHECK_SECONDS`.
    ///
    /// ⚠️ Il costo e' una chiamata nativa ogni due secondi SOLO finche' la risposta e' no: quando la
    /// mod parte col gioco, Steam e' gia' inizializzato e la prima risposta e' quasi sempre si.
    /// </summary>
    private static void CheckAvailability()
    {
        if (_available)
            return;

        float now = Time.realtimeSinceStartup;
        if (now < _nextAvailabilityCheck)
            return;

        _nextAvailabilityCheck = now + RECHECK_SECONDS;

        try
        {
            _available = SteamAPI.IsSteamRunning() && SteamUser.BLoggedOn() && SteamUser.GetSteamID().IsValid();
        }
        catch (Exception e)
        {
            _available = false;
            if (!_availabilityLogged)
            {
                _availabilityLogged = true;
                ModLog.Info(MarkerUnavailable + ": " + e.GetType().Name + ": " + e.Message +
                            " (bottone Steam disabilitato, i bottoni IP restano identici)");
            }
            return;
        }

        if (!_availabilityLogged)
        {
            _availabilityLogged = true;

            if (!_available)
                ModLog.Info(MarkerUnavailable + ": SteamAPI.IsSteamRunning()=no oppure nessun account loggato " +
                            "(bottone Steam disabilitato, i bottoni IP restano identici)");
        }
    }

    /// <summary>La prima callback che arriva lo dice: e' la prova che il pump funziona.</summary>
    private static void NoteCallback(string type)
    {
        if (_firstCallbackLogged)
            return;

        _firstCallbackLogged = true;
        ModLog.Info(MarkerFirstCallback + type);
    }

    private static void ResetHostState()
    {
        _hostLobby = CSteamID.Nil;
        _creatingLobby = false;
        _hostLobbyReady = false;
    }

    private static void ResetJoinState()
    {
        _joinLobby = CSteamID.Nil;
        _joinEntered = false;
        _joinReady = false;
        _joinRetryPending = false;
        _joinDataRetries = 0;
        _joinWaitingLogged = false;
        _joinDataSince = 0f;
        _joinDataDone = false;
        _hostLeftReported = false;
        _joinIp = string.Empty;
        _joinPort = 0;
    }

    /// <summary>
    /// Una notifica a schermo, con il `try/catch` addosso: la UI della mod puo' non esistere ancora
    /// (prima che `Multiplayer` sia inizializzato), e una notifica persa non e' un guasto.
    /// </summary>
    private static void Notify(string message)
    {
        try
        {
            if (Main.Multiplayer != null && Main.Multiplayer.NotificationManager != null)
                Main.Multiplayer.NotificationManager.DisplayNotification(message);
        }
        catch (Exception)
        {
            // Niente: la riga di log c'e' gia', e la notifica e' un di piu'.
        }
    }
}
