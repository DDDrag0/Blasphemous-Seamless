
namespace Blasphemous.Multiplayer.Client.PvP.Models;

public enum ConditionType
{
    None = 0,
    LowHealth = 1,
    NoFlasks = 2,
}

public enum ScalingType
{
    Sword = 0,
    Prayer = 1,
    Blood = 2,
}

public enum AttackType
{
    Slash = 0,

    ComboNormal = 10,
    ComboUp = 11,
    ComboDown = 12,

    Charged = 20,
    Lunge = 21,
    Vertical = 22,
    Ranged = 23,
    ChargedProjectile = 24,
    RangedExplosion = 25,

    Gem = 30,
    PrayerHit = 31,

    Debla = 40,
    Taranto = 50,
    Verdiales = 60,
    Lorquiana = 70,
    Tirana = 80,
    PoisonMist = 90,
    Shield = 100,
    Miriam = 110,
    MiriamSpike = 111,
    Aubade = 120,
    Cherubs = 130,
    CanteJondo = 140,

    // ================================================================================================
    // ⭐⭐ IL VERSO NUOVO: UN NEMICO HA FERITO UN GIOCATORE (passo ② del danno del boss, 2026-10-18).
    //
    // NON e' un attacco fra giocatori: e' il colpo che il BOSS (o qualunque nemico) ha messo a segno
    // sul proxy dell'altro giocatore, inoltrato alla macchina dell'altro perche' lo applichi al PROPRIO
    // `Penitent`. Prima di questo valore quel canale non esisteva affatto
    // (`scratch\DIAGNOSI-DANNO-BOSS-COOP.md`), e il danno al peer era zero per costruzione.
    //
    // PERCHE' UN VALORE DI `AttackType` E NON UN `NetworkType` NUOVO. Il canale Attack e' gia' quello
    // che il server inoltra alla stessa scena escludendo il mittente, con il nome del mittente
    // premesso: e' esattamente il viaggio che serve. Aggiungere un tipo di pacchetto avrebbe voluto
    // dire toccare l'enum condiviso, il client E il server (e il server va ripubblicato su entrambe
    // le macchine). Cosi' invece **non cambia una riga di protocollo** (resta 11) e **non cambia una
    // riga di server**: il payload del nuovo verso e' auto-descritto dal primo byte, e le due macchine
    // possono anche essere disallineate senza danni — un client vecchio non trova 200 in `attacks.json`
    // ed esce (`AttackManager.cs:31`), un server vecchio inoltra i byte senza interpretarli.
    //
    // ⚠️ IL VALORE E' SCELTO FUORI DAI BLOCCHI USATI (0..140) E NON E' `NoDamage` (255), perche' il
    // byte viaggia come `(byte)attackType`: 200 e' libero e non collide con nessuna delle famiglie
    // esistenti. Non si ricicla un valore esistente: un `Slash` in piu' sarebbe un colpo PvP applicato
    // a chi non ha chiesto il PvP.
    // ================================================================================================
    /// <summary>
    /// Un NEMICO ha messo a segno un colpo su di me: e' la variante del canale Attack che va da
    /// nemico a giocatore, e il suo payload NON e' quello PvP (vedi `EnemyHitPeer.BuildPayload`).
    /// </summary>
    EnemyHit = 200,

    // ================================================================================================
    // ⭐⭐ P1 — IL RELAY DEGLI EFFETTI GENERICO (2026-10-19): un effetto del pool e' nato su una
    // macchina e va ricostruito sull'altra.
    //
    // NON e' un attacco e non ferisce nessuno: e' il TERZO verso del canale Attack, e ne usa la stessa
    // infrastruttura per la stessa ragione del ②. Il server inoltra questo canale a tutti i peer della
    // STESSA SCENA **escludendo il mittente**, col nome del mittente premesso (`Server.cs:676-699`):
    // e' esattamente il viaggio che serve a `prefab + posizione + rotazione`, e non chiede una riga di
    // protocollo (resta 11) ne' una riga di server. Un client vecchio non trova 201 in `attacks.json`
    // ed esce da solo (`AttackManager.cs:31`); un server vecchio inoltra i byte senza interpretarli.
    //
    // ⚠️ NON e' un broadcast a chiunque: lo e' il CANALE, e il ricevente non si fida — ricostruisce
    // l'effetto solo se il nome e' un effetto del **suo** boss e solo se non ce l'ha **gia'**
    // (`EffectRelay.cs`: catalogo + dedup). Questo byte, da solo, non istanzia niente.
    //
    // 201 e non 202 perche' sta accanto al ② (200, nemico→giocatore) nella stessa famiglia: due versi
    // che si distinguono per lo stesso byte iniziale, letti nello stesso punto (`ReceiveAttack`).
    // ================================================================================================
    /// <summary>
    /// Un EFFETTO in pool e' nato su un'altra macchina: il ricevente lo ricostruisce dal proprio pool
    /// se lo riconosce come effetto del proprio boss e se non ce l'ha gia'. Vedi `EffectRelay`.
    /// </summary>
    CoopEffectRelay = 201,

    // ================================================================================================
    // ⭐ I DUE MARKER DELLA VOCE 56 (2026-10-19): i conteggi del PEER e il PRIMO id presente su un lato
    // solo. NON e' un attacco e non decide niente: e' il QUARTO verso del canale Attack, e ne usa la
    // stessa infrastruttura per la stessa ragione del ② e del ③ — il server inoltra questo canale ai
    // peer della STESSA SCENA escludendo il mittente, col nome del mittente premesso
    // (`Server.cs:676-699`) ⇒ **nessuna riga di protocollo** (resta 11), **nessuna riga di server**.
    // Un client vecchio non trova 202 in `attacks.json` ed esce da solo (`AttackManager.cs:31`); un
    // server vecchio inoltra i byte senza interpretarli.
    //
    // PERCHE' SERVE UN PACCHETTO: sul filo viaggia SOLO l'`int` dell'impronta (`SendEnemyUpdate`), e
    // l'impronta dice **che** le due liste divergono, non **come**. I due numeri del peer e la sua
    // lista non sono mai arrivati su questa macchina, e senza di loro `IMPRONTA DIVERSA` non puo'
    // stampare ne' i conteggi dell'altro (voce 56, marker 3a) ne' quale id diverge (3b).
    //
    // LA FORMA: richiesta/risposta, una coppia al massimo ogni 15 s (lo stesso periodo del referto
    // della divergenza), e solo mentre la divergenza c'e'. Il payload e' in `SyncIdListMarker`.
    // Il valore 202 sta accanto a 200 e 201 nello stesso blocco, e resta fuori dai blocchi 0..140 e da
    // `NoDamage = 255`, perche' il byte viaggia come `(byte)attackType`.
    // ================================================================================================
    /// <summary>
    /// I conteggi e la lista di id del peer: richiesta e risposta per i due marker della voce 56.
    /// Vedi <see cref="SyncIdListMarker"/> per la forma del payload e per cosa stampa.
    /// </summary>
    SyncIdList = 202,

    // ================================================================================================
    // ⭐⭐ B — LA NOTIFICA "L'ALTRO E' IN PAUSA" (2026-10-19): un fronte di pausa del giocatore locale,
    // che arriva all'altro solo per ESSERE LETTO. Non mette in pausa nessuno: **e' un'informazione**.
    //
    // NON e' un attacco e non ferisce nessuno: e' il QUINTO verso del canale Attack, e ne usa la stessa
    // infrastruttura per la stessa ragione del ②, del ③ e del ④ — **e' il canale che fa il viaggio che
    // serve**: il server lo inoltra ai peer della STESSA SCENA **escludendo il mittente**, col nome del
    // mittente premesso (`Server.cs:676-699`, `PlayerInfo.isInSameScene`) ⇒ **il filtro "solo chi e' nella
    // mia stanza" e' gratis e sta gia' dall'altra parte**, e non serve una riga di protocollo (resta 11)
    // ne' una riga di server (un server vecchio inoltra i byte senza interpretarli; un client vecchio non
    // trova 203 in `attacks.json` ed esce da solo, `AttackManager.cs:31`).
    //
    // ⚠️ IL PAYLOAD E' DI UN BYTE, ed e' un VERSO (1 = in pausa, 0 = ripresa), non uno STATO: e' la
    // differenza che rende impossibile un rimbalzo. Vedi `PauseNotice` per il ciclo completo, per il
    // battito che tiene viva la notifica e per il timeout che la fa cadere se il peer sparisce.
    //
    // 203 sta accanto a 200/201/202 nello stesso blocco, e resta fuori dai blocchi 0..140 e da
    // `NoDamage = 255`, perche' il byte viaggia come `(byte)attackType`.
    // ================================================================================================
    /// <summary>
    /// Il fronte di pausa del giocatore locale ("sono in pausa" / "non lo sono piu'"), per la notifica
    /// a schermo sull'altra macchina. Vedi <see cref="Blasphemous.Multiplayer.Client.PauseNotice"/>:
    /// informazione, mai pausa condivisa.
    /// </summary>
    PauseRelay = 203,

    NoDamage = 255
}

public enum EffectType
{
    SidewaysGrounded = 0,
    UpwardsGrounded = 1,
    SidewaysAir = 10,
    UpwardsAir = 11,
    Crouch = 20,
    Ranged = 30,

    Debla = 40,
    Verdiales = 60,

    // A3 - the ghost trail. Two members instead of a payload: the values above leave 61..255 free, and
    // EffectType travels as a single byte, so nothing about the protocol has to change.
    TrailOn = 70,
    TrailOff = 71,

    // A4 - the effects that HAPPEN SOMEWHERE. These travel with a position in the payload, and that position
    // is optional by construction: the receiver reads the effect byte and stops, so a longer payload is
    // invisible to the older shapes and no protocol version has to move.
    SlideDust = 80,

    // A4 - the ground impact. FIVE members instead of a payload, for the same reason the position is optional:
    // the effect byte is free up to 255, and the KIND of impact is what the receiver needs. The two things
    // VerticalAttack.InstantiateExplosion branches on are the last unlocked skill (VERTICAL_1/2/3) and its
    // private BeamRequiredHeightReached, and BOTH are decided on the SENDER: the receiver has its own skill
    // and its own stale height flag, so it cannot work either of them out for itself.
    //
    //   Base       no VERTICAL_* skill was selected: the plain dust and NO landing sound, which is what the
    //              game's own get_GetLandingFxEventKey does (it returns an empty string in that case)
    //   1 / 2 / 3  the plain dust and that level's landing sound
    //   3Beam      VERTICAL_3 from a fall high enough for the beam: the UPGRADED dust, the beam, level 3 sound
    LandingImpactBase = 81,
    LandingImpact1 = 82,
    LandingImpact2 = 83,
    LandingImpact3 = 84,
    LandingImpact3Beam = 85,

    // A5 - la polvere della SCIVOLATA DI MOVIMENTO (Dash).
    //
    // DUE membri, non uno, perche' il gioco ha DUE prefab distinti con due campi serializzati diversi -
    // `startDashDustPrefab` e `stopDashDustPrefab`, entrambi private [SerializeField] - e due posizioni
    // diverse: la polvere di PARTENZA nasce sulla trasform del penitent, quella di ARRESTO a terra,
    // fuori dal DamageArea di 0.2 secondo l'orientamento. Un solo byte non puo' dire quale delle due.
    //
    // Il byte arriva a 255 e l'ultimo valore usato e' 85, quindi 86 e 87 sono liberi: nessun bump di
    // protocollo e nessuna modifica al server, che inoltra il payload INTERO e PREFISSATO col nome.
    DashDustStart = 86,
    DashDustStop = 87,

    // A5 - I SUONI locali che il peer non sente: i PASSI, i FENDENTI A VUOTO e la SCIVOLATA.
    //
    // PERCHE' NON UN PAYLOAD. Questi byte NON portano musica: portano il NOME del suono da suonare, e i
    // nomi sono GIA' su entrambe le macchine, perche' sono le `public const string` di PenitentAudio
    // (stessa build, stesso assembly). Trasmettere una stringa sarebbe stato il modo di sbagliare - la
    // lezione di A1 e' che una CHIAVE DI CATALOGO passata dove serve un PERCORSO DI BANCA fa LANCiARE
    // FMOD, e l'eccezione scatta PRIMA della sottrazione della vita.
    //
    // PERCHE' VALORI ALTI E NON 88..97 COMPATTI. 88 e' libero (86/87 sono di A5-polvere), ma lo spazio
    // NON e' infinito e A6 arrivera': il blocco 200.. e' riservato a chi vuole distinguere la VARIANTE
    // di un suono (es. i due passi della falcata), 88..96 e' il blocco dei SUONI.
    //
    // ⚠️ UN SOLO byte per tipo: e' la stessa proprieta' su cui poggia tutto il canale Effect
    // (SendEffect(EffectType) scrive UN byte) e per cui NON serve alcun bump di protocollo.
    //
    //   Valore  Suono che si sente             Chiave che lo suona DAVVERO
    //   ------  -----------------------------  --------------------------
    //   88      i passi a terra                 "PenitentFootStep"
    //   89      fendente base 1                 "PenitentAttack1"
    //   90      fendente base 2                 "PenitentAttack2"
    //   91      fendente in aria 1              "PenitentAirAttack1"
    //   92      fendente in aria 2              "PenitentAttack2"   <-- NON "PenitentAirAttack2"!
    //   93      colpo pesante                   "PenitentHeavyAttack"
    //   94      rilascio della carica           "PenitentReleaseChargedAttack"
    //   95/96   la SCIVOLATA: e' un evento CATALOG SOSTENUTO, quindi ha DUE byte
    //           (uno per ACCENDERE, uno per SPEGNERE) invece di uno: "PenitentDash"
    //
    // ⚠️ LA RIGA 92 NON E' UN REFUSO, ed e' l'unica cosa di A5 che si legge male. In `PenitentAudio` NON
    // esiste un `PlayAirAttack2`, e `PenitentAttackAnimations.PlayAirAttack2()` - l'evento del colpo in
    // aria 2 - chiama `PlayBasicAttack2()`. Verificato in IL: e' l'unico `call` del suo corpo. E la
    // costante `BasicAirAttack2EventKey` vale "PenitentAirAttack2" ma NON E' LETTA DA NESSUN METODO
    // dell'assembly: e' morta. ⇒ La chiave da risolvere e' "PenitentAttack2". Il BYTE resta distinto (92
    // != 90) perche' l'evento e' distinto, ma il SUONO riprodotto e' lo stesso.
    //
    // ⚠️ COSA NON E' IN QUESTO ELENCO, E PERCHE'.
    //   - Il caricamento della carica ("PenitentLoadingChargedAttack") e' un evento SOSTENUTO con altri
    //     QUATTRO punti di spegnimento nel gioco: fuori portata, e il fendente del rilascio - che e' quello
    //     che si sente - c'e' (94).
    //   - Il dash IN ARIA: il gioco NON lo suona (AirDashBehaviour non chiama PlayDash e PlayDashSound ha
    //     UN SOLO chiamante). Non si inventa un suono che il gioco non ha.
    FootStep = 88,
    Attack1 = 89,
    Attack2 = 90,
    AirAttack1 = 91,
    AirAttack2 = 92,
    HeavyAttack = 93,
    ChargedRelease = 94,
    DashSoundStart = 95,
    DashSoundStop = 96,

    // A5.2 - I SUONI CONFERMATI DALLE PROBE. Le probe di solo log hanno detto QUALE di questi il gioco
    // suona davvero, invece di scommettere su tre fix:
    //   · `ComboHit`      COMPARE (2 su G1, 12 su G2)  -> il QUARTO colpo di una combo e' lui
    //   · `ComboHitUp`/`Down` NON compaiono mai        -> non si coprono
    //   · `IdleSword`     COMPARE (3 e 4) ma RARO      -> non e' il rumore di fondo dell'idle: copribile
    //   · `LoadingCharged` COMPARE (2 e 2)             -> e' il caricamento, SOSTENUTO: rimandato
    //
    // I numeri seguono la numerazione del piano (97..104) e non sono compatti: 98/99 restano liberi per
    // `ComboHitUp`/`ComboHitDown` e 101/102 per il caricamento, cosi' un passo futuro non deve rinumerare.
    ComboHit = 97,
    IdleSword = 100,
    VerticalStart = 103,
    VerticalFalling = 104,

    // A6 - L'ESECUZIONE SULL'AVATAR. Due membri, e NON un canale nuovo: `SendEffect(EffectType)` scrive UN
    // byte e i valori sono liberi fino a 255. Nessun bump, nessuna modifica al server.
    //
    // PERCHE' DUE E NON UNO, UNO PER INIZIO E UNO PER FINE. Il ricevente deve sapere QUANDO rimettere
    // l'avatar come stava. `Animator.Play` non offre un "quando finisce" ascoltabile da fuori: l'evento di
    // fine esiste nel gioco - e' l'`AnimatorEvent` con id "INTERACTION_END" che `Interactable.OnEventLaunched`
    // riceve - ma vive su un componente del FANTASMA DENTRO LA PROP, che qui non esiste. Il mittente invece
    // lo sa, perche' la sua prop ce l'ha: `Execution.InteractionEnd()` e' il punto in cui la clip finisce.
    //
    // ⚠️ E IL BYTE DI FINE E' UNA COMODITA', NON UNA GARANZIA. Un pacchetto perso non si puo' escludere: la
    // garanzia vera e' il FUSIBILE del ricevente (E1.4), come per la scivolata di A5. Il byte di fine serve a
    // non dover ASPETTARE il fusibile nel caso normale - cioe' a rendere l'animazione lunga quanto deve.
    ExecutionStart = 105,
    ExecutionEnd = 106,

    // A7 - I SUONI DELLA PARATA DEL PENITENT.
    //
    // ⚠️ "IL SUONO FINALE" (il contrattacco) NON e' fra questi, e la ragione e' importante: quello che
    // l'utente sente e' l'impatto del NEMICO (`Entity.Damage(float, string impactAudioId) ->
    // EventOneShotPanned`), che suona SUL NEMICO - condiviso, col danno gia' relayato - e attraversa quindi
    // la rete GIA' OGGI. `PenitentParry` non e' sincronizzato da nessuno, quindi non puo' essere quello.
    //
    //   ParryHit      107  il CLANG della parata riuscita: chiave di catalogo SOSTENUTA (`PlayEvent`)
    //   ParryHitStop  108  il suo STOP: un evento sostenuto va spento, o resta acceso per sempre
    //   ParryStart    109  la GUARDIA che sale: chiave di catalogo PURA (`PlaySfxOnCatalog`), one-shot
    //
    // ⚠️ `ParryStart` NON e' "la parata fallita": si sente a OGNI tentativo. L'unico colpo davvero
    // ASSORBITO e' la GuardSlide, gia' coperta da A4 - non si duplica.
    ParryHit = 107,
    ParryHitStop = 108,
    ParryStart = 109,

    // A7-bis - GLI ALTRI DUE SUONI DELLA CATENA, per togliere il dubbio su QUALE sia il clang.
    // Il dubbio e' dichiarato dal piano e non risolvibile in IL (dipende da quale clip porta quale
    // AnimationEvent): si suonano TUTTI, e l'orecchio decide.
    //   ParryAttack       110  `PenitentParry`       - il contrattacco (one-shot puro, `PlayParryAttack`)
    //   ParryStartCatalog 111  `PenitentStartParry` - la chiave del metodo SENZA chiamanti C# (`PlayStartParry`)
    ParryAttack = 110,
    ParryStartCatalog = 111,
}
