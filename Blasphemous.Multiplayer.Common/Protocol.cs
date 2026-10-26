namespace Blasphemous.Multiplayer.Common;

public static class Protocol
{
    /// <summary>
    /// Wire protocol version. Must be identical on client and server: a mismatch causes a
    /// <c>RefusalType.Protocol</c> handshake refusal at runtime. <c>byte</c> matches the Intro
    /// byte on the wire, so no cast is needed on either side.
    /// </summary>
    public const byte PROTOCOL_VERSION = 11;

    /// <summary>
    /// The default ip used by client and server
    /// </summary>
    public const string DEFAULT_IP = "localhost";

    /// <summary>
    /// The default port used by client and server
    /// </summary>
    public const int DEFAULT_PORT = 27051;
}