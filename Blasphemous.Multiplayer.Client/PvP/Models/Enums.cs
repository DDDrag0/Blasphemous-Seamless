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

    /// <summary>
    /// Enemy hit a player: enemy-to-player direction on the Attack channel. Payload is
    /// EnemyHitPeer.BuildPayload, not a PvP hit. Reusing the Attack channel avoids a new
    /// NetworkType, a protocol bump, and a server redeploy; mismatched peers ignore 200 and
    /// exit cleanly in AttackManager.
    /// </summary>
    EnemyHit = 200,

    /// <summary>
    /// A pooled effect was spawned on another machine and should be rebuilt locally if
    /// recognized and not already present (see EffectRelay). Same channel/version rationale as
    /// EnemyHit.
    /// </summary>
    CoopEffectRelay = 201,

    /// <summary>
    /// Peer's spawn/id counters and id list for the id-list divergence markers (see
    /// SyncIdListMarker). Same channel/version rationale as EnemyHit; up to one request/reply
    /// pair per 15s.
    /// </summary>
    SyncIdList = 202,

    /// <summary>
    /// Local pause state flag (1 = paused, 0 = running) for the "teammate is paused"
    /// notification (see PauseNotice). Information, never a pause command: the receiver only
    /// toggles a sticky line. Reliable, one byte, heartbeated.
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

    /// <summary>Ghost trail start.</summary>
    TrailOn = 70,

    /// <summary>Ghost trail stop.</summary>
    TrailOff = 71,

    /// <summary>Slide dust at the sender's slide position (payload carries position).</summary>
    SlideDust = 80,

    /// <summary>
    /// Ground impact variants: the receiver cannot derive which one applies, because
    /// VerticalAttack.InstantiateExplosion branches on the last unlocked skill and on a private
    /// beam-height flag that are per-machine. LandingImpactBase has no landing sound (the
    /// game's own get_GetLandingFxEventKey returns empty in that case).
    /// </summary>
    LandingImpactBase = 81,
    LandingImpact1 = 82,
    LandingImpact2 = 83,
    LandingImpact3 = 84,
    LandingImpact3Beam = 85,

    /// <summary>
    /// Dash dust start. Two members because the game has two distinct prefabs (start/stop) with
    /// different serialized fields and positions; a single byte cannot disambiguate them.
    /// </summary>
    DashDustStart = 86,

    /// <summary>Dash dust stop.</summary>
    DashDustStop = 87,

    /// <summary>
    /// Footstep sound. One-shot, resolved via PenitentAudio.FootStepEventKey on the receiver;
    /// sent through the motion path to preserve the 14 floor-material parameters.
    /// </summary>
    FootStep = 88,

    /// <summary>Basic attack 1 sound ("PenitentAttack1").</summary>
    Attack1 = 89,

    /// <summary>Basic attack 2 sound ("PenitentAttack2").</summary>
    Attack2 = 90,

    /// <summary>Air attack 1 sound ("PenitentAirAttack1").</summary>
    AirAttack1 = 91,

    /// <summary>
    /// Air attack 2 sound. Resolves to PenitentAttack2, not PenitentAirAttack2: the game's
    /// PlayAirAttack2 calls PlayBasicAttack2, and BasicAirAttack2EventKey has no consumer in
    /// the assembly. The byte stays distinct because the clip event is distinct.
    /// </summary>
    AirAttack2 = 92,

    /// <summary>Heavy attack sound ("PenitentHeavyAttack").</summary>
    HeavyAttack = 93,

    /// <summary>Charged attack release sound ("PenitentReleaseChargedAttack").</summary>
    ChargedRelease = 94,

    /// <summary>
    /// Dash sound start ("PenitentDash"). Latched event: pairs with DashSoundStop to avoid
    /// leaving the sound on forever. Uses PlayEventWithCatalog, not PlayOneShotFromCatalog,
    /// because the latter would release the instance and make it unstoppable.
    /// </summary>
    DashSoundStart = 95,

    /// <summary>Dash sound stop.</summary>
    DashSoundStop = 96,

    /// <summary>Combo hit sound; confirmed audible by the probes.</summary>
    ComboHit = 97,

    /// <summary>Idle sword sound; confirmed audible (rare).</summary>
    IdleSword = 100,

    /// <summary>Vertical attack start sound.</summary>
    VerticalStart = 103,

    /// <summary>Vertical attack falling sound.</summary>
    VerticalFalling = 104,

    /// <summary>Execution animation start on the avatar.</summary>
    ExecutionStart = 105,

    /// <summary>
    /// Execution animation end. The game's own INTERACTION_END lives on the prop's ghost
    /// AnimatorEvent, not reachable here; the sender's InteractionEnd is the natural source.
    /// Paired with a receiver-side fuse because a lost packet cannot be excluded.
    /// </summary>
    ExecutionEnd = 106,

    /// <summary>Parry clang (latched catalog event via PlayEvent); needs a matching stop.</summary>
    ParryHit = 107,

    /// <summary>Parry clang stop; missing it would leave the sound on forever.</summary>
    ParryHitStop = 108,

    /// <summary>
    /// Parry guard sound ("PenitentStartParry"): fires on every guard attempt, not only on
    /// successful parries. The path is read from the local Parry component; the value lives in
    /// the asset.
    /// </summary>
    ParryStart = 109,

    /// <summary>
    /// Parry counter-attack sound (PenitentParry), one-shot. Sent alongside ParryHit and
    /// ParryStartCatalog so the correct clang can be identified by ear.
    /// </summary>
    ParryAttack = 110,

    /// <summary>
    /// StartParryEventKey on the local catalog. PlayStartParry has no C# callers in the
    /// assembly, so this byte is sent alongside ParryStart to avoid missing the actual clang.
    /// </summary>
    ParryStartCatalog = 111,
}