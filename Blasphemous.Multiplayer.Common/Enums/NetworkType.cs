
namespace Blasphemous.Multiplayer.Common.Enums;

public enum NetworkType
{
    Position = 0,
    Animation = 1,
    Direction = 2,
    EnterScene = 3,
    LeaveScene = 4,
    Skin = 5,
    Team = 6,
    Connection = 7,
    Intro = 8,
    Progress = 9,
    Attack = 10,
    Effect = 11,
    Ping = 12,
    HostAssign = 13,
    EnemyUpdate = 21,
    EnemyDamageRequest = 22,
    EnemyDeath = 23,

    /// <summary>
    /// An enemy died, sent to EVERY player regardless of their current scene.
    ///
    /// EnemyDeath is relayed only to players sharing the sender's scene, so a player who was elsewhere when
    /// the kill happened never hears about it - and on re-entering the room the game spawns that enemy
    /// fresh and alive. Their copy then contradicts the machine that had it right.
    ///
    /// Carries the enemy id as a STRING, not a handle: the receiver is usually in another scene, where its
    /// handle table for that scene does not exist.
    /// </summary>
    EnemyDeathGlobal = 24,

    /// <summary>
    /// The host's ownership decision for one enemy: the token saying which machine may simulate it.
    ///
    /// Ownership used to be computed independently on both machines, from the distance each one measured
    /// locally. Those measurements cannot agree - the two players see slightly different positions, worst
    /// of all on slopes - so both machines could conclude "the closest player is mine" and simulate the
    /// same enemy, each publishing its own position. In game that appeared as one enemy standing in two
    /// places with aggro on both players at the same time.
    ///
    /// With this packet only the host decides and the peer obeys instead of recalculating, so one enemy has
    /// exactly one authority. That is a guarantee, not a hope.
    /// </summary>
    EnemyOwnership = 25,
}
