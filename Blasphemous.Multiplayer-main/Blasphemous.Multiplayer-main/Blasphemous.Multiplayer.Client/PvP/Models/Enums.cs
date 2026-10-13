
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
}
