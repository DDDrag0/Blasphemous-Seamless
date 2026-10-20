using System;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// ⭐⭐ IL FORMATO DEL PACCHETTO P2P — l'intestazione **del trasporto**, non del protocollo.
    ///
    /// PERCHE' UN'INTESTAZIONE, E PERCHE' NON E' UN CAMBIO DI PROTOCOLLO. `ICoopTransport` consegna
    /// buffer gia' impacchettati (`[ushort len][byte type][payload]`, protocollo 11, INVARIATO). Il
    /// trasporto P2P ha bisogno di due cose che il filo TCP gli dava gratis e che il legacy
    /// `SteamNetworking` non da':
    ///   · **chi ha mandato** questo pacchetto, quando e' l'host a inoltrarlo (da soli si vedrebbe solo
    ///     "host" e tutti i giocatori collasserebbero in un flusso solo);
    ///   · **un numero di sequenza** sul canale 0, perche' `k_EP2PSendUnreliableNoDelay` non ordina:
    ///     senza contatore vince l'ULTIMO ARRIVATO, non l'ultimo CAMPIONATO, e un pacchetto scavalcato
    ///     trascina indietro l'avatar (`docs\STEAM_INVITES.md` §10.4).
    /// Quindi ogni pacchetto di questo trasporto comincia con un prefisso di 3 byte, e solo il canale 0
    /// (unreliable) aggiunge l'intestazione `[origin 8][seq 4]`. Le classi affidabili non hanno bisogno
    /// di niente: Steam le consegna ordinate e complete.
    ///
    ///   pacchetto = [magic 0xCB][versione 0x01][tipo] [intestazione se tipo=DATA e canale 0] [frame...]
    ///
    /// 🔴 PERCHE' UN MAGIC, E PERCHE' NON SI CONFONDE CON UN FRAME. Il primo byte di un frame e' il
    /// byte basso della lunghezza `ushort` (0..255). Un frame con lunghezza 0xCB esiste in teoria, e
    /// quindi la sola `0xCB` non discrimina: si guardano **tre** byte, e il terzo (il tipo) vale 1..4,
    /// mentre il terzo byte di un frame e' un `NetworkType` (0..25). Inoltre il prefisso viene messo da
    /// NOI su ogni pacchetto: chi legge sa gia' che un pacchetto di questo trasporto comincia cosi'. Un
    /// pacchetto **estraneo** (per esempio il byte della sonda `SteamP2PProbe`, che vive sullo stesso
    /// canale 0) non ha il magic e viene **contato e scartato**, mai interpretato.
    ///
    /// ⚠️ QUESTO FILE NON NOMINA `Steamworks`: e' aritmetica sui byte, e la usano **entrambe** le
    /// estremita' (il trasporto del peer e l'adapter dell'host) piu' un banco di prova offline.
    /// </summary>
    internal static class P2PWire
    {
        /// <summary>Il primo byte di OGNI pacchetto di questo trasporto.</summary>
        internal const byte Magic = 0xCB;

        /// <summary>La versione del formato. Un pacchetto con un'altra versione si scarta e lo si dice.</summary>
        internal const byte Version = 0x01;

        /// <summary>"Sono qui, accettami" (reliable, canale 1) — vedi `SteamP2PTransport.Connect`.</summary>
        internal const byte KindHello = 0x01;

        /// <summary>Frame di gioco: uno o piu' `[ushort len][byte type][payload]` consecutivi.</summary>
        internal const byte KindData = 0x02;

        /// <summary>"Sono ancora vivo" (reliable, canale 1): e' cio' che tiene in piedi il timeout.</summary>
        internal const byte KindKeepAlive = 0x03;

        /// <summary>"Me ne vado" (reliable, canale 1): chiude subito, senza aspettare il timeout.</summary>
        internal const byte KindBye = 0x04;

        /// <summary>I tre byte davanti a tutto.</summary>
        internal const int PrefixBytes = 3;

        /// <summary>Il numero di byte dell'intestazione `[origin 8][seq 4]` (solo canale 0).</summary>
        internal const int SequenceBytes = 12;

        /// <summary>Canale 0: `k_EP2PSendUnreliableNoDelay`, con intestazione (sequenza).</summary>
        internal const int ChannelUnreliable = 0;

        /// <summary>Canale 1: `k_EP2PSendReliable`, eventi atomici e fatti durevoli.</summary>
        internal const int ChannelReliable = 1;

        /// <summary>Canale 2: `k_EP2PSendReliable`, ma ISOLATO: skin e payload grassi.</summary>
        internal const int ChannelHeavy = 2;

        /// <summary>Un pacchetto piu' corto del prefisso non e' nostro.</summary>
        internal const int MinimumPacketBytes = PrefixBytes;

        /// <summary>Il pacchetto di sola intestazione (DATA su canale 0 senza frame utili).</summary>
        internal const int MinimumDataOnChannel0Bytes = PrefixBytes + SequenceBytes;

        /// <summary>
        /// Il canale di una classe: `Unreliable` -> 0, `Reliable` -> 1. Il canale 2 non si sceglie per
        /// classe ma per **contenuto** (una skin), ed e' una decisione del chiamante che sa cos'e'
        /// (vedi <see cref="ChannelFor(TransportClass, byte)"/>).
        /// </summary>
        internal static int ChannelFor(TransportClass cls)
        {
            return cls == TransportClass.Reliable ? ChannelReliable : ChannelUnreliable;
        }

        /// <summary>
        /// Il canale di un frame: la classe decide 0/1, ma una SKIN va sul 2 anche se e' affidabile —
        /// e' il "class + isolation" di §10.1: un PNG non deve far aspettare una morte, e una morte non
        /// deve aspettare un PNG. `networkType` e' il terzo byte del frame (`framed[2]`).
        /// </summary>
        internal static int ChannelFor(TransportClass cls, byte networkType)
        {
            if (cls == TransportClass.Reliable && networkType == SKIN_TYPE)
                return ChannelHeavy;

            return ChannelFor(cls);
        }

        /// <summary>`NetworkType.Skin` come numero: 5 (vedi `Common\Enums\NetworkType.cs`).</summary>
        private const byte SKIN_TYPE = 5;

        /// <summary>
        /// Costruisce un pacchetto HELLO/KEEPALIVE/BYE: `[magic][versione][tipo][steamId 8]`.
        /// Il mittente ci mette il proprio id anche se `ReadP2PPacket` lo direbbe: e' il campo che
        /// l'adapter logga, e un pacchetto di controllo deve essere leggibile da solo.
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
        /// Costruisce un pacchetto DATA. Su canale 0 antepone `[origin][seq]`; sugli altri no.
        /// Il buffer dei frame e' copiato: il chiamante resta padrone del suo.
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
        /// Il riconoscimento di un pacchetto: `true` se e' di questo trasporto, con la sua versione, e
        /// con abbastanza byte per il tipo dichiarato. **Non lancia mai** su dati arbitrari.
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

            // I pacchetti di controllo portano 8 byte di mittente; i DATA no (il mittente lo da' Steam).
            int offset = PrefixBytes;

            if (kind != KindData)
            {
                if (length < PrefixBytes + 8)
                    return false;

                originSteamId = ReadUInt64(packet, PrefixBytes);
                payloadOffset = PrefixBytes + 8;
                return true;
            }

            // ⚠️ `TryParse` non sa su che CANALE e' arrivato il pacchetto: glielo dice il chiamante
            //    con `hasSequence`. Qui si prova la forma sequenziata se c'e' spazio, e si lascia al
            //    chiamante la parola finale (vedi `TryParseData`).
            payloadOffset = offset;
            return true;
        }

        /// <summary>
        /// Il riconoscimento di un pacchetto DATA, con il canale. `sequenced` viene da
        /// <paramref name="channel"/>: solo il canale 0 ha l'intestazione. Ritorna `false` se i byte non
        /// bastano, cosi' chi legge non interpreta mai un pacchetto troncato.
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
        /// "Questo numero di sequenza e' piu' NUOVO di quello che ho gia' accettato?"
        ///
        /// ⚠️ STRETTAMENTE minore = vecchio; UGUALE = accettato. Non e' una svista: l'host che inoltra
        /// spezza un pacchetto ricevuto in un pacchetto per frame (il server inoltra frame per frame) e
        /// ognuno porta la **stessa** sequenza d'origine ⇒ con un confronto "minore-uguale" si
        /// butterebbero via i frame 2..N dello stesso pacchetto. Il prezzo e' che un duplicato della rete
        /// unreliable (raro) passa: per posizioni e effetti e' la copia di un campione, non un errore di
        /// stato.
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
