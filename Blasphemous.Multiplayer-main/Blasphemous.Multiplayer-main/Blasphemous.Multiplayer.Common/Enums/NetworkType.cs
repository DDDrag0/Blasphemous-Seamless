
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
    /// EnemyDeath is relayed only to players sharing the sender's scene, which loses the information
    /// exactly when it matters most: a player who was elsewhere when the kill happened never hears about
    /// it, and on re-entering the room the game respawns that enemy alive at full health. Their copy then
    /// overwrites the death on the machine that had it right - the "ghost" that never goes away.
    ///
    /// This type carries the enemy id as a STRING rather than a handle, because the receiving machine is
    /// likely in another scene and its handle table for that scene does not exist.
    /// </summary>
    EnemyDeathGlobal = 24,
}
