using System;

namespace Blasphemous.Multiplayer.Common;

/// <summary>
/// Che cosa c'e' DENTRO un <see cref="PeerId"/>. Oggi solo <see cref="Ip"/>; <see cref="Steam"/> e'
/// il caso che il tipo esiste per rendere possibile, e che in C1a NON e' ancora usato da nessuno.
/// </summary>
public enum PeerKind : byte
{
    /// <summary>L'identita' del filo TCP: la stringa che il socket ci da' (oggi `ip:porta`).</summary>
    Ip = 0,

    /// <summary>Un `CSteamID` ridotto a `ulong`. Nessun tipo `Steamworks` entra qui: vedi il commento del tipo.</summary>
    Steam = 1,
}

/// <summary>
/// ⭐⭐ L'IDENTITA' DI UN PEER — un valore, non una stringa.
///
/// PERCHE' ESISTE. Il server indicizza i giocatori per identita': la chiave di `connectedPlayers`, il
/// primo parametro di `Send(peer, ...)`, `getCurrentPlayer(peer)`, l'host della sessione. Fino a C1a
/// quella chiave era `string ip` — e non era una scelta, era l'unica cosa che il trasporto sapesse
/// dire. Con Steam il peer diventa un `CSteamID`, che NON e' una stringa e NON e' un IP: due
/// giocatori dietro lo stesso NAT hanno lo stesso IP e due `CSteamID` diversi. Cambiare il tipo della
/// chiave ADESSO, mentre vale ancora l'IP, e' il passo che rende quel cambio futuro una sostituzione
/// di valore e non un rifacimento del server.
///
/// 🔴 IL VINCOLO CHE QUESTO TIPO RISPETTA — NIENTE TIPI `Steamworks` QUI.
/// `Blasphemous.Multiplayer.Common` e' multi-target `net35;net8.0` e la compilano client E server: non
/// puo' referenziare `Steamworks.NET` (romperebbe la build del server e la CI). Percio' il caso Steam
/// e' uno `ulong` nudo: la conversione da/verso `CSteamID` avverra' SOLO dentro le `*Impl`
/// (`SteamP2PProbeImpl`, `SteamLobbyImpl`), cioe' dove il tipo `Steamworks` e' gia' confinato oggi.
///
/// 🔴 IL SECONDO VINCOLO — IL FILO TCP RESTA IDENTICO, BYTE PER BYTE.
/// Per un peer IP questo tipo E' la stringa di oggi: <see cref="FromIp"/> la conserva così com'e'
/// (nessuna normalizzazione, nessun parsing, nessun `IPAddress`), e <see cref="ToString"/> la rende
/// **esattamente** com'era — perche' finisce nei log e nella chiave che `SimpleTCP` usa per scegliere
/// il socket. Se `ToString()` cambiasse una virgola, i log cambierebbero e `Send` non troverebbe piu'
/// il destinatario.
///
/// PERCHE' `GetHashCode` E' PROPRIO QUELLO DELLA STRINGA (caso IP). Un `Dictionary` enumera le chiavi
/// in ordine di bucket: se l'hash cambiasse, cambierebbe l'ORDINE dei `foreach` sul dizionario, cioe'
/// l'ordine dei pacchetti verso i peer e l'ordine delle righe di log nelle partite a 3+. Usando
/// `string.GetHashCode()` l'hash e' identico a quello che il `Dictionary` da stringa a `PlayerInfo` di oggi
/// calcolava, quindi la mappa resta la stessa mappa, con le stesse chiavi negli stessi bucket.
/// (L'uguaglianza e' `StringComparer.Ordinal`, la stessa che il `Dictionary` da stringa a ... usava di
/// default: `Equals`/`ContainsKey`/`Remove` si comportano come prima, carattere per carattere.)
///
/// Il tipo e' IMMUTABILE: campi `readonly`, nessun setter, nessun modo di cambiare un'istanza dopo
/// `FromIp`. Due `PeerId` con la stessa `Kind` e lo stesso valore sono uguali e hanno lo stesso hash.
/// </summary>
public struct PeerId : IEquatable<PeerId>
{
    private readonly PeerKind _kind;

    /// <summary>
    /// Il valore quando `Kind == Ip`: la stringa del socket, NON interpretata. `null` per gli altri
    /// casi (e per `default(PeerId)`, che non e' un peer valido).
    /// </summary>
    private readonly string? _ip;

    /// <summary>Il valore quando `Kind == Steam`: un `CSteamID.m_SteamID` nudo.</summary>
    private readonly ulong _steam;

    private PeerId(PeerKind kind, string? ip, ulong steam)
    {
        _kind = kind;
        _ip = ip;
        _steam = steam;
    }

    /// <summary>Quale delle due identita' porta questo valore.</summary>
    public PeerKind Kind
    {
        get { return _kind; }
    }

    /// <summary>
    /// La stringa del peer quando `Kind == Ip` — quella che il trasporto ci ha dato, tale e quale.
    /// Vuota per un <see cref="PeerKind.Steam"/> e per `default(PeerId)`: e' una lettura, non una
    /// conversione, quindi non inventa niente.
    /// </summary>
    public string? Ip
    {
        get { return _ip; }
    }

    /// <summary>Il `CSteamID` (gia' ridotto a `ulong`) quando `Kind == Steam`; `0` negli altri casi.</summary>
    public ulong SteamId
    {
        get { return _steam; }
    }

    /// <summary>
    /// ⭐ LA FABBRICA DEL FILO TCP: da una stringa del socket a un'identita'. Nessuna trasformazione:
    /// il valore e' conservato BYTE PER BYTE (spazi, porta, maiuscole comprese), perche' e' la stessa
    /// stringa che `SimpleTCP` usa per trovare il socket a cui scrivere.
    /// </summary>
    public static PeerId FromIp(string ip)
    {
        // ⚠️ `null` non e' un peer: oggi `Dictionary` da stringa a ... lanciava su una chiave null, e
        // questo tipo fa lo stesso invece di fabbricare un'identita' vuota che nessuno saprebbe
        // indirizzare. `e.ip` di SimpleTCP non e' mai null (misurato in `scratch\REFERTO-C1A-IDENTITA.md`).
        if (ip == null)
            throw new ArgumentNullException(nameof(ip));

        return new PeerId(PeerKind.Ip, ip, 0UL);
    }

    /// <summary>
    /// La fabbrica del caso Steam: un `CSteamID.m_SteamID` nudo, nessun tipo `Steamworks` in vista.
    /// ⚠️ In C1a NON e' chiamata da nessuno (niente P2P in questo commit): esiste perche' il caso
    /// `PeerKind.Steam` sia gia' un valore che il tipo sa rappresentare, confrontare e stampare.
    /// </summary>
    public static PeerId FromSteam(ulong steamId)
    {
        return new PeerId(PeerKind.Steam, null, steamId);
    }

    /// <summary>
    /// Uguali se e solo se stessa `Kind` e stesso valore — e il confronto e' **ordinale**, la stessa
    /// regola che il `Dictionary` da stringa a `PlayerInfo` usava di default. Niente `ToLower`, niente
    /// trim, niente risoluzione di nomi: `"127.0.0.1:1"` e `"127.0.0.1:01"` sono peer DIVERSI, oggi
    /// come prima di questo tipo.
    /// </summary>
    public bool Equals(PeerId other)
    {
        if (_kind != other._kind)
            return false;

        if (_kind == PeerKind.Steam)
            return _steam == other._steam;

        // ⭐ `StringComparison.Ordinal`: e' esattamente `EqualityComparer<string>.Default`, cioe' il
        // comparatore che il `Dictionary` da stringa a ... di oggi usava senza dirlo.
        return string.Equals(_ip, other._ip, StringComparison.Ordinal);
    }

    /// <summary>`Equals` anche per la via di `object`: un `PeerId` in una `List`/`Dictionary` si comporta come ci si aspetta.</summary>
    public override bool Equals(object? obj)
    {
        return obj is PeerId other && Equals(other);
    }

    /// <summary>
    /// ⭐ L'HASH DEL CASO IP E' QUELLO DELLA STRINGA, di proposito: e' cio' che tiene identico
    /// l'ordine di enumerazione del `Dictionary` (vedi il commento del tipo). Per il caso Steam si
    /// mescola il `Kind`, cosi' due valori di tipo diverso non finiscono per caso nello stesso bucket
    /// con lo stesso hash.
    /// </summary>
    public override int GetHashCode()
    {
        if (_kind == PeerKind.Steam)
            return _steam.GetHashCode() ^ (int)_kind;

        return _ip == null ? 0 : _ip.GetHashCode();
    }

    /// <summary>
    /// ⭐⭐ LA RAPPRESENTAZIONE — e per il filo TCP e' **ESATTAMENTE** la stringa IP di oggi.
    ///
    /// Questo non e' un dettaglio di stile: `ToString()` finisce nelle righe di log che devono restare
    /// le stesse (`"Client connected at " + ip`, `"Designated host: {name} ({ip})"`) e, per il caso
    /// Steam, sara' l'unica forma stampabile di un id che non e' un indirizzo. Nessun prefisso, nessuna
    /// parentesi, nessun `"ip="`: solo il valore.
    /// </summary>
    public override string ToString()
    {
        if (_kind == PeerKind.Steam)
            return _steam.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return _ip ?? string.Empty;
    }

    /// <summary>Uguale a <see cref="Equals(PeerId)"/>, per poter scrivere `peer == _hostPeer` come si scriveva `ip == _hostIp`.</summary>
    public static bool operator ==(PeerId left, PeerId right)
    {
        return left.Equals(right);
    }

    /// <summary>Il contrario di `==`.</summary>
    public static bool operator !=(PeerId left, PeerId right)
    {
        return !left.Equals(right);
    }
}
