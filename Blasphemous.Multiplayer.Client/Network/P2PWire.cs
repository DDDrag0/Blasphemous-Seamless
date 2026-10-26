using System;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// Packet format of the P2P transport (transport header, not protocol header). Adds two
    /// things TCP gives for free and legacy SteamNetworking does not: the origin SteamID (needed
    /// when the host forwards a peer's frame) and a sequence on channel 0 (unreliable delivery
    /// does not order packets). Layout:
    ///   [magic 0xCB][version 0x01][kind] [origin 8][seq 4 if kind=DATA and channel 0] [frames...]
    /// Magic + version + kind (1..4) distinguish a P2P packet from a plain frame (a frame's first
    /// byte is the low half of a ushort length, its third byte is a NetworkType 0..25). A foreign
    /// packet (e.g. the probe's single byte on channel 0) is dropped, never interpreted.
    /// </summary>
    internal static class P2PWire
    {
        /// <summary>First byte of every packet of this transport.</summary>
        internal const byte Magic = 0xCB;

        /// <summary>Format version. A packet with a different version is dropped.</summary>
        internal const byte Version = 0x01;

        /// <summary>"I am here, accept me" (reliable, channel 1).</summary>
        internal const byte KindHello = 0x01;

        /// <summary>Game frames: one or more consecutive [ushort len][byte type][payload].</summary>
        internal const byte KindData = 0x02;

        /// <summary>"I am alive" (reliable, channel 1). Drives the timeout.</summary>
        internal const byte KindKeepAlive = 0x03;

        /// <summary>"I am leaving" (reliable, channel 1). Closes immediately, no timeout wait.</summary>
        internal const byte KindBye = 0x04;

        /// <summary>The three bytes in front of everything.</summary>
        internal const int PrefixBytes = 3;

        /// <summary>Length of [origin 8][seq 4] (channel 0 only).</summary>
        internal const int SequenceBytes = 12;

        /// <summary>Channel 0: k_EP2PSendUnreliableNoDelay, with header (sequence).</summary>
        internal const int ChannelUnreliable = 0;

        /// <summary>Channel 1: k_EP2PSendReliable, atomic events and durable facts.</summary>
        internal const int ChannelReliable = 1;

        /// <summary>Channel 2: k_EP2PSendReliable, ISOLATED: skins and heavy payloads.</summary>
        internal const int ChannelHeavy = 2;

        /// <summary>A packet shorter than the prefix is not ours.</summary>
        internal const int MinimumPacketBytes = PrefixBytes;

        /// <summary>Header-only DATA on channel 0 (no useful frame).</summary>
        internal const int MinimumDataOnChannel0Bytes = PrefixBytes + SequenceBytes;

        /// <summary>Channel of a class: Unreliable -> 0, Reliable -> 1. Channel 2 is content-based.</summary>
        internal static int ChannelFor(TransportClass cls)
        {
            return cls == TransportClass.Reliable ? ChannelReliable : ChannelUnreliable;
        }

        /// <summary>
        /// Channel of a frame: class decides 0/1, but a Skin goes to 2 even when reliable - a PNG
        /// must not delay a death and a death must not delay a PNG. <paramref name="networkType"/>
        /// is the third byte of the frame (framed[2]).
        /// </summary>
        internal static int ChannelFor(TransportClass cls, byte networkType)
        {
            if (cls == TransportClass.Reliable && networkType == SKIN_TYPE)
                return ChannelHeavy;

            return ChannelFor(cls);
        }

        /// <summary>NetworkType.Skin as a byte.</summary>
        private const byte SKIN_TYPE = 5;

        /// <summary>
        /// Builds a HELLO/KEEPALIVE/BYE packet: [magic][version][kind][steamId 8]. The sender is
        /// included so a control packet is readable on its own.
        /// </summary>
        internal static byte[] BuildControl(byte kind, ulong senderSteamId)
        {
            byte[] packet = new byte[PrefixBytes + 8];
            packet[0] = Magic;
            packet[1] = Version;
            packet[2] = kind;
            WriteUInt64(packet, PrefixBytes, senderSteamId);
            return packet;
        }

        /// <summary>
        /// Builds a DATA packet. On channel 0 prepends [origin][seq]; on other channels no header.
        /// The frames buffer is copied; the caller keeps ownership of its array.
        /// </summary>
        internal static byte[] BuildData(byte[] framed, TransportClass cls, ulong originSteamId, uint sequence)
        {
            if (framed == null)
                throw new ArgumentNullException(nameof(framed));

            bool sequenced = cls != TransportClass.Reliable;
            int header = PrefixBytes + (sequenced ? SequenceBytes : 0);

            byte[] packet = new byte[header + framed.Length];
            packet[0] = Magic;
            packet[1] = Version;
            packet[2] = KindData;

            if (sequenced)
            {
                WriteUInt64(packet, PrefixBytes, originSteamId);
                WriteUInt32(packet, PrefixBytes + 8, sequence);
            }

            Buffer.BlockCopy(framed, 0, packet, header, framed.Length);
            return packet;
        }

        /// <summary>
        /// Recognizes a packet of this transport. Never throws on arbitrary data. For DATA packets
        /// the sequence header cannot be resolved without the channel - see <see cref="TryParseData"/>.
        /// </summary>
        internal static bool TryParse(byte[] packet, int length, out byte kind, out int payloadOffset, out ulong originSteamId, out uint sequence)
        {
            kind = 0;
            payloadOffset = 0;
            originSteamId = 0UL;
            sequence = 0u;

            if (packet == null || length < MinimumPacketBytes + 1)
                return false;

            if (packet[0] != Magic || packet[1] != Version)
                return false;

            kind = packet[2];
            if (kind < KindHello || kind > KindBye)
                return false;

            // Control packets carry 8 bytes of sender; DATA does not (Steam reports the sender).
            int offset = PrefixBytes;

            if (kind != KindData)
            {
                if (length < PrefixBytes + 8)
                    return false;

                originSteamId = ReadUInt64(packet, PrefixBytes);
                payloadOffset = PrefixBytes + 8;
                return true;
            }

            payloadOffset = offset;
            return true;
        }

        /// <summary>
        /// Recognizes a DATA packet with its channel. Only channel 0 has the sequence header.
        /// Returns false on truncated bytes so a reader never interprets a partial packet.
        /// </summary>
        internal static bool TryParseData(byte[] packet, int length, int channel,
                                          out ulong originSteamId, out uint sequence, out int payloadOffset)
        {
            originSteamId = 0UL;
            sequence = 0u;
            payloadOffset = PrefixBytes;

            if (packet == null || length < MinimumPacketBytes)
                return false;

            if (packet[0] != Magic || packet[1] != Version || packet[2] != KindData)
                return false;

            if (channel != ChannelUnreliable)
                return true;

            if (length < MinimumDataOnChannel0Bytes)
                return false;

            originSteamId = ReadUInt64(packet, PrefixBytes);
            sequence = ReadUInt32(packet, PrefixBytes + 8);
            payloadOffset = PrefixBytes + SequenceBytes;
            return true;
        }

        /// <summary>
        /// "Is this sequence number newer than the one already accepted?" Strictly less = stale;
        /// equal = accepted. The equal case matters: the host splits a received packet into one
        /// per-frame packet, all carrying the same origin sequence - a "strictly newer" comparison
        /// would discard frames 2..N.
        /// </summary>
        internal static bool IsNewer(uint incoming, uint lastAccepted)
        {
            return incoming >= lastAccepted;
        }

        internal static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }

        internal static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        internal static ulong ReadUInt64(byte[] buffer, int offset)
        {
            return buffer[offset]
                 | ((ulong)buffer[offset + 1] << 8)
                 | ((ulong)buffer[offset + 2] << 16)
                 | ((ulong)buffer[offset + 3] << 24)
                 | ((ulong)buffer[offset + 4] << 32)
                 | ((ulong)buffer[offset + 5] << 40)
                 | ((ulong)buffer[offset + 6] << 48)
                 | ((ulong)buffer[offset + 7] << 56);
        }

        internal static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                 | (buffer[offset + 1] << 8)
                 | (buffer[offset + 2] << 16)
                 | (buffer[offset + 3] << 24));
        }
    }
}