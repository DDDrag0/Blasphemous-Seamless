using Blasphemous.ModdingAPI;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// ⭐⭐ LA SONDA P2P — MISURA IL LEGACY `SteamNetworking`, NON LO USA COME TRASPORTO.
///
/// COSA FA, IN UNA RIGA: manda **1 byte** ai peer della lobby Steam ogni secondo su canale 0, li
/// riceve, e ogni 3 secondi **chiede a Steam** com'e' andata la sessione con ciascuno: attiva? in
/// relay? con che IP? È una **misura**, non un trasporto: nessun `ICoopTransport`, nessun protocollo,
/// nessun byte di gioco. I byte che questa classe manda non arrivano a `NetworkManager` e non
/// significano niente per la mod.
///
/// PERCHÉ ESISTE (e non è una curiosità): `docs\STEAM_INVITES.md` §8 lascia aperta **una** domanda, ed
/// è quella che decide se la mod può funzionare fuori da una LAN — *il relay di Steam funziona sotto
/// l'AppID del gioco (`774361`), che non è il nostro?* Le due macchine su due reti diverse, senza
/// port forwarding, sono l'unico modo di rispondere. Questa sonda produce le tre righe che servono:
///
///   `[P2P] peer=&lt;id&gt; attiva=&lt;0|1&gt; relay=&lt;0|1&gt; via=diretto|relay ip=&lt;a.b.c.d|n/d&gt; err=&lt;n&gt; rx=&lt;byte&gt;`
///
/// e il loro significato, nell'ordine in cui vanno lette:
///   · **`attiva=0`** — la sessione P2P non è mai salita: il byte non è passato (o il peer non ha
///     accettato la sessione). È l'esito "non comunicano";
///   · **`attiva=1 relay=0`** — **diretto**: i due client si parlano senza passare dai server di
///     Steam. È il caso buono, quello che non costa banda a Valve e non dipende dal relay;
///   · **`attiva=1 relay=1`** — **in relay**: i pacchetti passano dai server di Steam. Funziona, ma
///     dipende dal relay (e dalla sua disponibilità sotto l'AppID del gioco).
///
/// ─────────────────────────────────────────────────────────────────────────────────────────────
/// PERCHÉ NON DIPENDE DAL TCP (ed è il punto del test): il test vero è **il secondo PC su un hotspot**,
/// dove il TCP della mod **non può** arrivare (NAT, nessun port forwarding). Se questa sonda avesse
/// bisogno di "sono connesso al gioco" per partire, misurerebbe zero proprio nel caso in cui serve.
/// Quindi qui non si guarda **mai** `NetworkManager`: l'unico requisito è essere **dentro una lobby
/// Steam** (che passa dal back end di Steam, non dal TCP). Vedi `RequireLobby`.
///
/// ⚠️ L'ACCETTAZIONE È OBBLIGATORIA, E SI FA IN UNA CALLBACK. Il legacy P2P non ha un handshake
/// automatico: se il ricevente non risponde a **`P2PSessionRequest_t`** con
/// **`AcceptP2PSessionWithUser`**, *ogni* pacchetto di quel peer viene **scartato in silenzio** — il
/// sintomo sarebbe `rx=0` per sempre, senza un errore da nessuna parte. Quindi la callback si registra
/// appena Steam è pronto (`Arm`/`Tick`) e **non** solo quando si preme il bottone: il peer può
/// cominciare a mandare prima che l'altro abbia premuto.
///
/// ⚠️ E SI ACCETTA SOLO CHI È NELLA LOBBY (`SteamLobbyImpl.IsLobbyMember`): senza quel cancello
/// chiunque conosca il nostro SteamID potrebbe farci aprire una sessione P2P, cioè consumare banda e
/// (in un trasporto vero) farci accettare pacchetti da uno sconosciuto.
///
/// ⚠️ `SendP2PPacket` PUÒ TORNARE `false` — buffer di invio pieno — **senza eccezione e senza
/// callback**: un `false` buttato via è un byte che nessuno saprà mai di aver perso. Qui il valore di
/// ritorno si controlla **sempre** (`Send`), e il primo `false` per peer finisce nel log.
///
/// ⚠️ `GetP2PSessionState` È SINCRONA (interroga il client Steam): si chiama **ogni 3 secondi**, non
/// per frame (`Measure`).
///
/// ─────────────────────────────────────────────────────────────────────────────────────────────
/// 🔴 PERCHÉ UNA CLASSE `*Impl` SORELLA E NON UN PEZZO DI `SteamLobbyImpl`. La regola della mod è che
/// **solo una classe `*Impl` nomina `Steamworks`** (vedi `SteamLobby.cs`): su una copia non-Steam i
/// tipi mancano e la `TypeLoadException` arriva al punto di chiamata, che deve stare dentro il
/// `try/catch` della facciata. Questa classe nomina `CSteamID`, `Callback&lt;T&gt;`,
/// `P2PSessionState_t`...
/// quindi è una `*Impl`, e chi la chiama è **`SteamP2PProbe`** (facciata, zero tipi `Steamworks`),
/// un metodo per chiamata, esattamente come `SteamLobby` fa con `SteamLobbyImpl`.
///
/// ⛔ COSA NON C'È, DI PROPOSITO: nessun `ICoopTransport`, nessuna coda, nessuna ritrasmissione,
/// nessun framing, nessuna chiamata a `NetworkManager`, al server o al gioco. E nessun
/// `AllowP2PPacketRelay(false)`: il relay **deve** restare permesso, perché è esattamente uno dei due
/// esiti che stiamo misurando.
/// </summary>
internal static class SteamP2PProbeImpl
{
    // ⭐ I MARKER — tutti col prefisso `[P2P]`, e sono il prodotto della sonda: `tools\marker-check.ps1`
    //    li cerca nella DLL deployata, e il referto dice quali guardare e in che ordine.
    internal const string MarkerStart = "[P2P] sonda avviata";
    internal const string MarkerStop = "[P2P] sonda fermata";
    internal const string MarkerState = "[P2P] peer=";
    internal const string MarkerSend = "[P2P] invio: ";
    internal const string MarkerSendFail = "[P2P] invio fallito: ";
    internal const string MarkerReceive = "[P2P] ricevuto: ";
    internal const string MarkerSessionRequest = "[P2P] sessione richiesta: ";
    internal const string MarkerSessionAccept = "[P2P] sessione accettata: ";
    internal const string MarkerSessionRefuse = "[P2P] sessione rifiutata: ";
    internal const string MarkerCallback = "[P2P] callback registrata: ";
    internal const string MarkerNoLobby = "[P2P] nessuna lobby: ";
    internal const string MarkerError = "[P2P] errore: ";

    /// <summary>Il canale della misura: **0**, lo stesso che il piano (§9) assegna al gameplay.</summary>
    private const int CHANNEL = 0;

    /// <summary>Un byte, sempre lo stesso. Non è un messaggio: è un battito.</summary>
    private static readonly byte[] _payload = new byte[1] { 0x01 };

    /// <summary>Ogni quanto si manda il byte (§9: un battito, non un flusso).</summary>
    private const float SEND_INTERVAL_S = 1f;

    /// <summary>Ogni quanto si chiede lo stato a Steam. NON per frame: la chiamata è sincrona.</summary>
    private const float MEASURE_INTERVAL_S = 3f;

    /// <summary>
    /// Ogni quanto si ritenta la registrazione della callback se è fallita. E' il freno che impedisce
    /// a `Tick` (per frame) di trasformare un guasto di Steam in un log inondato.
    /// </summary>
    private const float REGISTER_RETRY_S = 2f;

    /// <summary>
    /// Quanti pacchetti si leggono al massimo per frame. Il ciclo di lettura è `while` (ci può essere
    /// più di un pacchetto pronto), e un tetto lo rende **limitato**: nessun frame che si allunga per
    /// una coda anomala. Con un byte al secondo da un peer non ci si arriva mai; con 64 ci si ferma e
    /// si riprende al frame dopo, senza perdere niente.
    /// </summary>
    private const int MAX_PACKETS_PER_FRAME = 64;

    /// <summary>
    /// Il tetto del buffer di lettura. I nostri pacchetti sono da 1 byte; il tetto serve solo a non
    /// allocare una dimensione assurda se un domani un peer mandasse qualcosa di grosso (o se la
    /// lettura restituisse una dimensione corrotta). Oltre, il pacchetto si legge troncato: non è un
    /// dato che ci interessa, ci interessa **che sia arrivato**.
    /// </summary>
    private const uint MAX_PACKET_BYTES = 64u * 1024u;

    // ─── I CALLBACK COME CAMPI (⚠️ NON LOCALI: il GC li ucciderebbe) ─────────────────────────────
    // Stessa ragione di `SteamLobbyImpl`: `Callback<T>.Create` registra il delegato in un registro
    // GLOBALE del processo e tiene un `GCHandle` sull'oggetto. Una variabile locale sparirebbe dopo il
    // ritorno, il GC potrebbe raccoglierla, e le callback smetterebbero di arrivare **in silenzio**.
    private static Callback<P2PSessionRequest_t> _cbSessionRequest;

    // ─── STATO ────────────────────────────────────────────────────────────────────────────────────
    private static bool _registered;        // la callback P2PSessionRequest_t è registrata
    private static bool _registerLogged;    // il marker "callback registrata" esce una volta sola
    private static bool _registerFailed;    // il guasto della registrazione esce una volta sola
    private static float _nextRegisterTry;  // e non si ritenta a ogni frame (sarebbe un log inondato)
    private static bool _active;            // la sonda sta misurando (bottone premuto)
    private static float _nextSend;         // prossimo battito
    private static float _nextMeasure;      // prossima misura
    private static byte[] _rxBuffer = new byte[1024];

    private static long _rxTotal;                                  // byte ricevuti in totale
    private static readonly Dictionary<ulong, long> _rxByPeer = new Dictionary<ulong, long>();
    private static readonly HashSet<ulong> _peersSeen = new HashSet<ulong>();
    private static readonly HashSet<ulong> _sendOkLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _sendFailLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _rxLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _acceptLogged = new HashSet<ulong>();
    private static readonly HashSet<ulong> _stateFailLogged = new HashSet<ulong>();

    private static bool _noLobbyLogged;     // "nessuna lobby" una volta per silenzio, non per frame
    private static bool _measured;          // almeno una misura fatta (per la riga a schermo)
    private static int _lastPeers;
    private static int _lastActive;
    private static int _lastDirect;
    private static int _lastRelay;
    private static string _summary = string.Empty;  // la riga breve per la schermata (vedi `Status`)

    /// <summary>
    /// `true` se la sonda sta misurando adesso. Letto dalla UI per la parola del bottone
    /// ("Probe P2P" / "Stop probe").
    /// </summary>
    internal static bool Active
    {
        get { return _active; }
    }

    /// <summary>
    /// ⭐ REGISTRA LA CALLBACK DI ACCETTAZIONE. Idempotente, e non lancia.
    ///
    /// Sta in una funzione sua perché ha **due** occasioni d'ingresso: `Arm` (l'avvio della mod, se
    /// Steam è già pronto) e `Tick` (ogni frame, finché non riesce). Se il gioco inizializza Steam dopo
    /// la mod, la prima occasione non basta — e senza questa callback il P2P non funziona **affatto**,
    /// quindi non è una cosa che si può lasciare al caso.
    ///
    /// ⚠️ Si registra **anche a sonda ferma**: il peer può cominciare a mandare quando vuole, e una
    /// richiesta di sessione scartata perché "non avevo ancora premuto" sarebbe un `rx=0` inspiegabile.
    /// </summary>
    internal static void Arm()
    {
        try
        {
            EnsureRegistered();
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Arm)");
        }
    }

    /// <summary>
    /// ⭐ "PROBE P2P" — comincia a misurare. `false` = non si può (Steam non disponibile), e la UI lo
    /// dice. Con Steam disponibile ritorna `true` anche **senza lobby**: la sonda resta in attesa e
    /// comincia da sola appena si entra in una lobby (`RequireLobby`), che è quello che serve per poter
    /// premere il bottone *prima* di accettare l'invito.
    /// </summary>
    internal static bool Start()
    {
        try
        {
            if (!SteamLobbyImpl.Available)
            {
                ModLog.Warn(MarkerNoLobby + "Steam non e' disponibile: la sonda non parte");
                return false;
            }

            if (!EnsureRegistered())
                return false;

            if (_active)
                return true;

            _active = true;
            _measured = false;
            _noLobbyLogged = false;
            _rxTotal = 0L;
            _lastPeers = 0;
            _lastActive = 0;
            _lastDirect = 0;
            _lastRelay = 0;
            _rxByPeer.Clear();
            _peersSeen.Clear();
            _sendOkLogged.Clear();
            _sendFailLogged.Clear();
            _rxLogged.Clear();
            _acceptLogged.Clear();
            _stateFailLogged.Clear();

            float now = Time.realtimeSinceStartup;
            _nextSend = now;                                  // il primo battito subito
            _nextMeasure = now + MEASURE_INTERVAL_S;          // la prima misura fra 3 s (c'e' qualcosa da misurare)

            _summary = "P2P: in ascolto...";

            CSteamID lobby = SteamLobbyImpl.CurrentLobby;
            ModLog.Info(MarkerStart + " canale=" + CHANNEL + " byte=1 ogni " + SEND_INTERVAL_S +
                        " s, misura ogni " + MEASURE_INTERVAL_S + " s, lobby=" +
                        (lobby.m_SteamID == 0UL ? "nessuna (si aspetta)" : lobby.m_SteamID.ToString()));
            return true;
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Start)");
            return false;
        }
    }

    /// <summary>
    /// Ferma la sonda e **chiude le sessioni** che aveva aperto (`CloseP2PSessionWithUser`): una
    /// sessione P2P lasciata aperta è uno stato che nessuno vede e che resta fino alla chiusura del
    /// gioco. Idempotente, non lancia, e non stampa niente se non stava misurando.
    /// </summary>
    internal static void Stop()
    {
        try
        {
            if (!_active)
                return;

            _active = false;
            _summary = string.Empty;

            int closed = 0;
            foreach (ulong peer in _peersSeen)
            {
                try
                {
                    if (SteamNetworking.CloseP2PSessionWithUser(new CSteamID(peer)))
                        closed++;
                }
                catch (Exception)
                {
                    // Una sessione che non si chiude non deve impedire di fermare la sonda.
                }
            }

            ModLog.Info(MarkerStop + " peer=" + _peersSeen.Count + " sessioni chiuse=" + closed +
                        " rxTotale=" + _rxTotal);
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Stop)");
        }
    }

    /// <summary>
    /// ⭐⭐ IL CICLO PER FRAME. Chiamato da `SteamLobby.Tick()` (che a sua volta è dentro il
    /// `OnLateUpdate` della mod): **zero bersagli Harmony nuovi, zero MonoBehaviour nuovi**.
    ///
    /// Fa tre cose, e la prima è quella che rende possibile tutto il resto:
    ///   ① **riceve** (`DrainChannel`): finché `IsP2PPacketAvailable(canale 0)` è vero, legge e **conta
    ///      i byte** per peer. È per frame perché il buffer va svuotato, altrimenti si riempie;
    ///   ② **manda** un byte ogni `SEND_INTERVAL_S`;
    ///   ③ **misura** ogni `MEASURE_INTERVAL_S` (`Measure`).
    ///
    /// ⚠️ A sonda FERMA non fa niente oltre a un `if`: la callback resta registrata (vedi `Arm`), ma
    /// nessun byte parte, nessun byte si legge e nessuno stato si interroga.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            if (!_registered && !EnsureRegistered())
                return;

            if (!_active)
                return;

            DrainChannel();

            float now = Time.realtimeSinceStartup;

            if (now >= _nextSend)
            {
                _nextSend = now + SEND_INTERVAL_S;
                Send();
            }

            if (now >= _nextMeasure)
            {
                _nextMeasure = now + MEASURE_INTERVAL_S;
                Measure();
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (Tick)");
        }
    }

    /// <summary>
    /// ⭐ LA RIGA BREVE PER LA SCHERMATA (`ConnectionDisplay`), costruita nell'ultima misura. Vuota a
    /// sonda ferma: chi non preme il bottone non vede **nessuna riga in piu'**, e la schermata resta
    /// identica a prima.
    ///
    /// PERCHÉ ESISTE, visto che la misura vera è nel log: chi fa il test può non avere il log aperto
    /// davanti, ma la schermata di connessione ce l'ha sempre. Questa riga dice **quanti** peer e
    /// **quanti in relay**, cioè la stessa risposta in forma breve. Il dettaglio per peer (id, ip, err,
    /// rx) resta nel log, che è dove va cercato se questa riga non basta.
    /// </summary>
    internal static string Status()
    {
        return _active ? _summary : string.Empty;
    }

    // ─── LE TRE AZIONI ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ③ IL BATTITO: un byte a **ogni peer della lobby escluso me stesso**, canale 0,
    /// `k_EP2PSendReliable`. ⚠️ Il valore di ritorno si controlla **sempre**: `false` = buffer di
    /// invio pieno, e il pacchetto è perso in silenzio.
    ///
    /// La lista dei peer si rilegge **ogni volta** (`GetNumLobbyMembers`/`GetLobbyMemberByIndex`) e non
    /// si tiene in cache: un amico che entra o esce dalla lobby deve comparire/sparire senza dover
    /// fermare e riavviare la sonda.
    /// </summary>
    private static void Send()
    {
        CSteamID lobby = SteamLobbyImpl.CurrentLobby;
        if (lobby.m_SteamID == 0UL)
        {
            RequireLobby("invio");
            return;
        }

        _noLobbyLogged = false;

        int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
        CSteamID me = SteamUser.GetSteamID();

        for (int i = 0; i < members; i++)
        {
            CSteamID peer = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (peer.m_SteamID == 0UL || peer.m_SteamID == me.m_SteamID)
                continue;   // me stesso non e' un peer: il P2P non e' un loopback

            ulong id = peer.m_SteamID;
            _peersSeen.Add(id);

            bool sent;
            try
            {
                sent = SteamNetworking.SendP2PPacket(peer, _payload, (uint)_payload.Length,
                                                     EP2PSend.k_EP2PSendReliable, CHANNEL);
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (SendP2PPacket)");
                continue;
            }

            if (sent)
            {
                // Il primo invio riuscito per peer lo si dice: e' la prova che la sonda sta provando
                // davvero, e distingue "non ho mandato niente" da "ho mandato e non e' tornato niente".
                if (_sendOkLogged.Add(id))
                    ModLog.Info(MarkerSend + "peer=" + id + " byte=" + _payload.Length +
                                " canale=" + CHANNEL + " esito=ok");
                _sendFailLogged.Remove(id);
            }
            else if (_sendFailLogged.Add(id))
            {
                // ⚠️ `false` NON e' un'eccezione e non arriva nessuna callback: senza questa riga sarebbe
                // un byte perso che nessuno nomina mai. Una riga per peer (non una al secondo).
                ModLog.Warn(MarkerSendFail + "peer=" + id + " (SendP2PPacket ha risposto false: buffer di " +
                            "invio pieno) - il byte si e' perso, la sonda continua");
            }
        }
    }

    /// <summary>
    /// ④ LA RICEZIONE: svuota il canale 0. `ReadP2PPacket` restituisce chi ha mandato e quanti byte,
    /// e il conteggio è **per peer** (il numero che finisce in `rx=` nella misura).
    ///
    /// ⚠️ Se `ReadP2PPacket` risponde `false` si **esce dal ciclo** invece di insistere: al frame dopo
    /// si riprova. Insistere dentro lo stesso frame sarebbe un ciclo che non consuma niente.
    ///
    /// ⚠️ E il ciclo è **limitato** (`MAX_PACKETS_PER_FRAME`): un eventuale arretrato non deve poter
    /// allungare il frame.
    /// </summary>
    private static void DrainChannel()
    {
        int guard = MAX_PACKETS_PER_FRAME;

        while (guard-- > 0)
        {
            uint available;
            if (!SteamNetworking.IsP2PPacketAvailable(out available, CHANNEL))
                break;

            uint capacity = available;
            if (capacity == 0u)
                capacity = 1u;              // ci sono byte "disponibili" ma dimensione 0: si prova lo stesso, senza allocare zero
            if (capacity > MAX_PACKET_BYTES)
                capacity = MAX_PACKET_BYTES;

            if ((uint)_rxBuffer.Length < capacity)
                _rxBuffer = new byte[capacity];

            uint read;
            CSteamID remote;
            if (!SteamNetworking.ReadP2PPacket(_rxBuffer, capacity, out read, out remote, CHANNEL))
                break;

            if (read == 0u || remote.m_SteamID == 0UL)
                continue;

            ulong id = remote.m_SteamID;

            long previous;
            _rxByPeer.TryGetValue(id, out previous);
            _rxByPeer[id] = previous + read;
            _rxTotal += read;

            // Il primo pacchetto da un peer lo si dice: e' la prova che l'accettazione ha funzionato e
            // che la strada (diretta o relay) porta davvero i byte. Una riga per peer, non una per byte.
            if (_rxLogged.Add(id))
                ModLog.Info(MarkerReceive + "peer=" + id + " byte=" + read + " canale=" + CHANNEL);
        }
    }

    /// <summary>
    /// ⑤ LA MISURA — **è lo scopo della sonda**. Per ogni peer della lobby, una riga:
    ///
    ///   `[P2P] peer=&lt;id&gt; attiva=&lt;0|1&gt; relay=&lt;0|1&gt; via=diretto|relay ip=&lt;a.b.c.d|n/d&gt; err=&lt;n&gt; rx=&lt;byte&gt;`
    ///
    /// ⚠️ `GetP2PSessionState` è **sincrona** (interroga il client Steam): si chiama qui, ogni 3
    /// secondi, e **mai** per frame.
    ///
    /// ⚠️ La lettura della riga, nell'ordine giusto: **`attiva` prima di tutto**. `attiva=0` significa
    /// "non c'è sessione" — e in quel caso `relay=0`/`via=diretto` **non** vogliono dire "diretto":
    /// vogliono dire che non c'è niente da misurare. Solo con `attiva=1` la coppia `relay`/`via` ha un
    /// significato (`0` = diretto, `1` = in relay). Il referto lo dice anche a parole.
    ///
    /// ⚠️ Se `GetP2PSessionState` risponde `false` la struttura resta a zero: si stampa lo stesso la
    /// riga, che quindi dice `attiva=0` — cioè "con quel peer non c'è una sessione", che è la verità.
    /// Una riga in piu' (una per peer) dice che è stata la **chiamata** a fallire, così l'esito non si
    /// confonde con "il peer non ha mai accettato".
    /// </summary>
    private static void Measure()
    {
        CSteamID lobby = SteamLobbyImpl.CurrentLobby;
        if (lobby.m_SteamID == 0UL)
        {
            RequireLobby("misura");
            return;
        }

        _noLobbyLogged = false;

        int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
        CSteamID me = SteamUser.GetSteamID();

        int peers = 0;
        int active = 0;
        int direct = 0;
        int relay = 0;

        for (int i = 0; i < members; i++)
        {
            CSteamID peer = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (peer.m_SteamID == 0UL || peer.m_SteamID == me.m_SteamID)
                continue;

            ulong id = peer.m_SteamID;
            peers++;
            _peersSeen.Add(id);

            P2PSessionState_t state;
            bool ok;
            try
            {
                ok = SteamNetworking.GetP2PSessionState(peer, out state);
            }
            catch (Exception e)
            {
                ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (GetP2PSessionState)");
                continue;
            }

            if (!ok && _stateFailLogged.Add(id))
                ModLog.Warn(MarkerError + "GetP2PSessionState ha risposto false per peer=" + id +
                            " (nessuna sessione con quel peer): la riga seguente vale attiva=0");

            int isActive = state.m_bConnectionActive != 0 ? 1 : 0;
            int isRelay = state.m_bUsingRelay != 0 ? 1 : 0;

            // ⚠️ LA REGOLA, in una riga: `m_bUsingRelay == 0` => diretto, `== 1` => relay.
            string via = isRelay == 0 ? "diretto" : "relay";

            // ⚠️ `m_nRemoteIP` vale 0 quando la sessione non e' ancora salita: "n/d" e' la verita',
            // non un errore.
            string ip = state.m_nRemoteIP == 0u ? "n/d" : FormatIp(state.m_nRemoteIP);

            long rx;
            _rxByPeer.TryGetValue(id, out rx);

            if (isActive != 0)
            {
                active++;
                if (isRelay == 0)
                    direct++;
                else
                    relay++;
            }

            ModLog.Info(MarkerState + id + " attiva=" + isActive + " relay=" + isRelay +
                        " via=" + via + " ip=" + ip + " err=" + state.m_eP2PSessionError +
                        " rx=" + rx);
        }

        _lastPeers = peers;
        _lastActive = active;
        _lastDirect = direct;
        _lastRelay = relay;
        _measured = true;

        _summary = BuildSummary();
    }

    /// <summary>
    /// La riga breve della schermata, costruita dai numeri dell'ultima misura. È l'unica cosa che la UI
    /// legge della sonda (una stringa: la facciata non ha nessun tipo `Steamworks` da maneggiare).
    /// </summary>
    private static string BuildSummary()
    {
        if (!_measured)
            return "P2P: in ascolto...";

        if (_lastPeers == 0)
            return "P2P: nessun peer in lobby";

        return "P2P: peer=" + _lastPeers + " attive=" + _lastActive +
               " diretto=" + _lastDirect + " relay=" + _lastRelay + " rx=" + _rxTotal + "B";
    }

    /// <summary>
    /// "Non c'è una lobby adesso" — una riga sola per volta, non una ogni 3 secondi. È il caso di chi
    /// preme il bottone **prima** di ospitare o di accettare l'invito: la sonda resta accesa e comincia
    /// da sola quando la lobby c'è (la riga lo dice, così non sembra che non funzioni).
    /// </summary>
    private static void RequireLobby(string phase)
    {
        if (_noLobbyLogged)
            return;

        _noLobbyLogged = true;
        _summary = "P2P: nessuna lobby (ospita o accetta un invito)";
        ModLog.Warn(MarkerNoLobby + "nessuna lobby Steam (" + phase + "): la sonda resta accesa e " +
                    "comincia da sola appena si entra in una lobby");
    }

    /// <summary>
    /// ⭐ LA CALLBACK CHE RENDE POSSIBILE TUTTO: `P2PSessionRequest_t` => `AcceptP2PSessionWithUser`.
    ///
    /// Senza questa, **ogni** pacchetto del peer viene scartato in silenzio (nessuna eccezione, nessun
    /// callback d'errore): il sintomo sarebbe `rx=0` per sempre. Vedi il commento in testa alla classe.
    ///
    /// 🔴 E SI ACCETTA SOLO CHI È NELLA LOBBY (`IsLobbyMember`): è la regola d'igiene del piano (§9).
    /// Chi non è nella lobby viene rifiutato **e lo si dice**, perché un rifiuto silenzioso è
    /// indistinguibile da una sonda che non riceve.
    /// </summary>
    private static void OnSessionRequest(P2PSessionRequest_t request)
    {
        try
        {
            CSteamID remote = request.m_steamIDRemote;
            ulong id = remote.m_SteamID;

            ModLog.Info(MarkerSessionRequest + "peer=" + id);

            if (id == 0UL || !SteamLobbyImpl.IsLobbyMember(remote))
            {
                if (_acceptLogged.Add(id))
                    ModLog.Warn(MarkerSessionRefuse + "peer=" + id + " (non e' un membro della lobby: " +
                                "sessione NON accettata)");
                return;
            }

            bool accepted = SteamNetworking.AcceptP2PSessionWithUser(remote);

            if (accepted)
            {
                if (_acceptLogged.Add(id))
                    ModLog.Info(MarkerSessionAccept + "peer=" + id);
            }
            else if (_acceptLogged.Add(id))
            {
                ModLog.Warn(MarkerSessionRefuse + "peer=" + id +
                            " (AcceptP2PSessionWithUser ha risposto false)");
            }
        }
        catch (Exception e)
        {
            ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message + " (P2PSessionRequest_t)");
        }
    }

    /// <summary>
    /// Registra `Callback&lt;P2PSessionRequest_t&gt;` una volta sola, e solo quando Steam è pronto
    /// (registrare prima dell'`Init` del gioco parlerebbe con l'API nativa in un punto che non
    /// controlliamo). Ritorna `true` se è registrata.
    /// </summary>
    private static bool EnsureRegistered()
    {
        if (_registered)
            return true;

        if (!SteamLobbyImpl.Available)
            return false;

        // ⚠️ `Tick` chiama questa funzione ogni frame finché non riesce: se la registrazione fallisse
        // di continuo, senza questo cancello e senza il log "una volta sola" il log diventerebbe
        // 60 righe al secondo, che è il modo peggiore di segnalare un guasto.
        float now = Time.realtimeSinceStartup;
        if (now < _nextRegisterTry)
            return false;

        try
        {
            if (!SteamAPI.IsSteamRunning())
                return false;

            _cbSessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            _registered = true;

            if (!_registerLogged)
            {
                _registerLogged = true;
                ModLog.Info(MarkerCallback + "P2PSessionRequest_t (accettazione delle sessioni P2P)");
            }

            return true;
        }
        catch (Exception e)
        {
            _registered = false;
            _nextRegisterTry = now + REGISTER_RETRY_S;

            if (!_registerFailed)
            {
                _registerFailed = true;
                ModLog.Error(MarkerError + e.GetType().Name + ": " + e.Message +
                             " (Callback<P2PSessionRequest_t>.Create) - si ritenta ogni " +
                             REGISTER_RETRY_S + " s, il log non si ripete");
            }

            return false;
        }
    }

    /// <summary>
    /// L'IP remoto della sessione, in forma `a.b.c.d`.
    ///
    /// `m_nRemoteIP` è documentato in **host byte order** (`192.168.1.5` = `0xC0A80105`), quindi gli
    /// ottetti si leggono dai bit alti. ⚠️ C'è un ripiego: se l'interpretazione diretta darebbe
    /// `0.0.0.x` (mai un indirizzo remoto valido) si prova quella inversa, così un valore letto
    /// "al contrario" non diventa un IP che sembra rotto. La sonda stampa il numero che usa, quindi non
    /// c'è niente da indovinare: se l'IP sembra strano, la riga dice **cosa** ha visto.
    /// </summary>
    private static string FormatIp(uint ip)
    {
        byte a = (byte)(ip >> 24);
        byte b = (byte)(ip >> 16);
        byte c = (byte)(ip >> 8);
        byte d = (byte)ip;

        if (a == 0 && b == 0 && c == 0)
        {
            byte a2 = (byte)ip;
            byte b2 = (byte)(ip >> 8);
            byte c2 = (byte)(ip >> 16);
            byte d2 = (byte)(ip >> 24);
            if (a2 != 0)
                return a2 + "." + b2 + "." + c2 + "." + d2;
        }

        return a + "." + b + "." + c + "." + d;
    }
}
