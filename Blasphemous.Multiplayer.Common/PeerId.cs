using System;

namespace Blasphemous.Multiplayer.Common;

/// <summary>
/// Kind of identity carried by <see cref="PeerId"/>. <see cref="Ip"/> is the only one in use; <see cref="Steam"/>
/// exists so the type can already represent the future case without requiring any Steamworks type here.
/// </summary>
public enum PeerKind : byte
{
    /// <summary>TCP identity: the exact string the socket gave us (today `ip:port`).</summary>
    Ip = 0,

    /// <summary>A `CSteamID` reduced to `ulong`. No Steamworks type enters here - see <see cref="PeerId"/>.</summary>
    Steam = 1,
}

/// <summary>
/// Immutable peer identity: a value, not a string.
///
/// The server keys players by this identity (dictionary key, `Send(peer, ...)` first argument,
/// `getCurrentPlayer(peer)`, host election). Until now the key was `string ip`, which was the only
/// thing the transport could supply; with Steam the peer becomes a `CSteamID`, which is not a string
/// and not an IP (two players behind the same NAT share the IP but have different SteamIDs). Moving
/// the key type now, while IP is still the only case, makes the future change a substitution of
/// values instead of a server rewrite.
///
/// No Steamworks types are allowed here: <c>Blasphemous.Multiplayer.Common</c> is multi-target
/// (<c>net35;net8.0</c>) and is compiled by both client and server, so it cannot reference
/// <c>Steamworks.NET</c>. The Steam case is a bare <c>ulong</c>; the conversion to/from
/// <c>CSteamID</c> happens only inside the <c>*Impl</c> classes.
///
/// The TCP wire stays byte-for-byte identical: <see cref="FromIp"/> preserves the string as-is and
/// <see cref="ToString"/> returns exactly the same string, because it is used both in logs and as the
/// key <c>SimpleTCP</c> uses to select the socket.
///
/// <see cref="GetHashCode"/> for the IP case is the string's own hash, so <see cref="Equals"/> and
/// hashing match the previous <c>Dictionary&lt;string, ...&gt;</c> behaviour. Enumeration order is
/// <c>Dictionary</c>'s insertion order, not a hash order; this type does not affect it.
/// </summary>
public struct PeerId : IEquatable<PeerId>
{
    private readonly PeerKind _kind;

    /// <summary>
    /// The IP string when <c>Kind == Ip</c>, never interpreted. <c>null</c> for other kinds and for
    /// <c>default(PeerId)</c>, which is not a valid peer.
    /// </summary>
    private readonly string? _ip;

    /// <summary>The bare <c>CSteamID.m_SteamID</c> when <c>Kind == Steam</c>.</summary>
    private readonly ulong _steam;

    private PeerId(PeerKind kind, string? ip, ulong steam)
    {
        _kind = kind;
        _ip = ip;
        _steam = steam;
    }

    /// <summary>Which identity this value carries.</summary>
    public PeerKind Kind
    {
        get { return _kind; }
    }

    /// <summary>
    /// The peer string when <c>Kind == Ip</c> - exactly what the transport supplied. Empty for
    /// <see cref="PeerKind.Steam"/> and for <c>default(PeerId)</c>.
    /// </summary>
    public string? Ip
    {
        get { return _ip; }
    }

    /// <summary>The <c>CSteamID</c> (as <c>ulong</c>) when <c>Kind == Steam</c>; <c>0</c> otherwise.</summary>
    public ulong SteamId
    {
        get { return _steam; }
    }

    /// <summary>
    /// Builds a peer from a TCP socket string. The value is stored byte-for-byte (spaces, port,
    /// casing included) because it is the same string <c>SimpleTCP</c> uses to locate the socket.
    /// </summary>
    public static PeerId FromIp(string ip)
    {
        // A null is not a peer: throw instead of fabricating an identity nobody can address.
        // SimpleTCP's `e.ip` is never null.
        if (ip == null)
            throw new ArgumentNullException(nameof(ip));

        return new PeerId(PeerKind.Ip, ip, 0UL);
    }

    /// <summary>
    /// Builds a peer from a bare <c>CSteamID.m_SteamID</c>. Not used yet (no P2P in this build);
    /// it exists so the Steam case is a value the type can represent, compare and print.
    /// </summary>
    public static PeerId FromSteam(ulong steamId)
    {
        return new PeerId(PeerKind.Steam, null, steamId);
    }

    /// <summary>
    /// Equal only when same <c>Kind</c> and same value, compared ordinally (the previous
    /// <c>Dictionary&lt;string, ...&gt;</c> default). No trimming, no lowercasing, no name resolution.
    /// </summary>
    public bool Equals(PeerId other)
    {
        if (_kind != other._kind)
            return false;

        if (_kind == PeerKind.Steam)
            return _steam == other._steam;

        return string.Equals(_ip, other._ip, StringComparison.Ordinal);
    }

    /// <summary><see cref="Equals(PeerId)"/> via <c>object</c>, so lists/dictionaries behave as expected.</summary>
    public override bool Equals(object? obj)
    {
        return obj is PeerId other && Equals(other);
    }

    /// <summary>
    /// Hash for the IP case is the string's own hash so equal values hash equally. For Steam the kind
    /// is mixed in so different kinds do not accidentally collide.
    /// </summary>
    public override int GetHashCode()
    {
        if (_kind == PeerKind.Steam)
            return _steam.GetHashCode() ^ (int)_kind;

        return _ip == null ? 0 : _ip.GetHashCode();
    }

    /// <summary>
    /// Representation used by logs and (for Steam) as the only printable form of the id. For the IP
    /// case it is exactly the previous IP string - no prefix, no brackets - because both log lines
    /// and the socket lookup depend on it.
    /// </summary>
    public override string ToString()
    {
        if (_kind == PeerKind.Steam)
            return _steam.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return _ip ?? string.Empty;
    }

    /// <summary>Same as <see cref="Equals(PeerId)"/>.</summary>
    public static bool operator ==(PeerId left, PeerId right)
    {
        return left.Equals(right);
    }

    /// <summary>Negation of <c>==</c>.</summary>
    public static bool operator !=(PeerId left, PeerId right)
    {
        return !left.Equals(right);
    }
}