
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
}
