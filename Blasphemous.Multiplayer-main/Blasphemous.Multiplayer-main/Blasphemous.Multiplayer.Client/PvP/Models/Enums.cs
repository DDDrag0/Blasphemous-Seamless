
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
}
