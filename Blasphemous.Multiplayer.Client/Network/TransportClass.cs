namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// Transport class of a message: what <c>ICoopTransport.Send</c> needs beyond the network
    /// type. The type alone cannot decide the class - <c>Attack</c> is a demultiplexer (two
    /// classes), <c>Effect</c> has two classes (three latched stops are reliable), and
    /// <c>Animation</c> carries an unreliable byte except for the death, which arrives once and
    /// must not be lost. The class is decided where the payload is produced. TCP ignores it (one
    /// ordered, reliable stream); P2P uses it to pick the channel. The byte never travels on the
    /// wire protocol (version stays 11) - only in the P2P header or channel selection.
    /// </summary>
    internal enum TransportClass : byte
    {
        /// <summary>May be lost; the next copy of the same kind overwrites it.</summary>
        Unreliable = 0,

        /// <summary>Must not be lost; no later message re-establishes it.</summary>
        Reliable = 1,
    }
}