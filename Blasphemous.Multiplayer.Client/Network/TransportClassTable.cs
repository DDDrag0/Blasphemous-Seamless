using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Blasphemous.Multiplayer.Common.Enums;
using System;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// Class of a frame that the server generates itself (reply, HostAssign, Connection, replay,
    /// and re-encoded Position/Animation/Direction). Peer-originated frames carry their class in
    /// the packet and are preserved by the host adapter, so they never come from this table. The
    /// three ambiguities are resolved by reading the distinguishing byte, using the same predicate
    /// as the sender (PlayerStatus.IsDeathAnimation, the three latched stops, the AttackType
    /// byte). Unreadable frames default to Reliable: costs bandwidth, not state.
    /// </summary>
    internal static class TransportClassTable
    {
        /// <summary>
        /// Class of an already-framed packet. Never throws; an unreadable frame is Reliable.
        /// </summary>
        internal static TransportClass ForFrame(byte[] framed)
        {
            byte type;
            int nameOffset;

            if (!TryReadFrameHeader(framed, out type, out nameOffset))
                return TransportClass.Reliable;

            return ForFrame((NetworkType)type, framed, nameOffset);
        }

        /// <summary>
        /// Same decision from type and payload offset: for callers that already read the header
        /// (offline test bench).
        /// </summary>
        internal static TransportClass ForFrame(NetworkType type, byte[] framed, int payloadOffset)
        {
            switch (type)
            {
                // Unreliable: the next copy overwrites.
                case NetworkType.Position:
                case NetworkType.Direction:
                case NetworkType.EnemyUpdate:
                case NetworkType.Ping:
                    return TransportClass.Unreliable;

                // Animation: unreliable byte on a reliable message; death is the exception, same
                // predicate as the sender.
                case NetworkType.Animation:
                {
                    byte animation;
                    if (!TryReadByteAfterName(framed, payloadOffset, out animation))
                        return TransportClass.Reliable;

                    return PlayerStatus.IsDeathAnimation(animation)
                        ? TransportClass.Reliable
                        : TransportClass.Unreliable;
                }

                // Effect: 29 cosmetic ones are unreliable; the three latched stops are not.
                case NetworkType.Effect:
                {
                    byte effect;
                    if (!TryReadByteAfterName(framed, payloadOffset, out effect))
                        return TransportClass.Reliable;

                    return IsLatchedStop((EffectType)effect)
                        ? TransportClass.Reliable
                        : TransportClass.Unreliable;
                }

                // Attack: a demultiplexer, the class is the AttackType byte.
                case NetworkType.Attack:
                {
                    byte attack;
                    if (!TryReadByteAfterName(framed, payloadOffset, out attack))
                        return TransportClass.Reliable;

                    switch ((AttackType)attack)
                    {
                        case AttackType.CoopEffectRelay:
                        case AttackType.SyncIdList:
                            return TransportClass.Unreliable;

                        default:
                            // EnemyHit, PauseRelay, and every PvP hit: losing them leaves state wrong.
                            return TransportClass.Reliable;
                    }
                }

                // Everything else is reliable: durable facts, atomic events, handshake.
                default:
                    return TransportClass.Reliable;
            }
        }

        /// <summary>
        /// The three latched stops: ghost trail, dash sound, parry CLANG. Without a timeout the
        /// receiver cannot expire them, so a lost STOP leaves the state wrong.
        /// </summary>
        internal static bool IsLatchedStop(EffectType effect)
        {
            return effect == EffectType.TrailOff
                || effect == EffectType.DashSoundStop
                || effect == EffectType.ParryHitStop;
        }

        /// <summary>Same, from a raw byte.</summary>
        internal static bool IsLatchedStop(byte effect)
        {
            return IsLatchedStop((EffectType)effect);
        }

        /// <summary>
        /// Reads [ushort len][byte type] and returns the payload offset. False on a truncated
        /// frame.
        /// </summary>
        internal static bool TryReadFrameHeader(byte[] framed, out byte type, out int payloadOffset)
        {
            type = 0;
            payloadOffset = 0;

            if (framed == null || framed.Length < 3)
                return false;

            int length = framed[0] | (framed[1] << 8);
            if (3 + length > framed.Length)
                return false;

            type = framed[2];
            payloadOffset = 3;
            return true;
        }

        /// <summary>
        /// The first useful byte after the sender name ([byte nameLen][name...][value]). False
        /// on a truncated frame; caller defaults to Reliable.
        /// </summary>
        internal static bool TryReadByteAfterName(byte[] framed, int payloadOffset, out byte value)
        {
            value = 0;

            if (framed == null || payloadOffset < 0 || payloadOffset >= framed.Length)
                return false;

            int nameLength = framed[payloadOffset];
            int valueOffset = payloadOffset + 1 + nameLength;

            if (valueOffset >= framed.Length)
                return false;

            value = framed[valueOffset];
            return true;
        }
    }
}