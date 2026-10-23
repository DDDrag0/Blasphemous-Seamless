namespace Blasphemous.Multiplayer.Common;

public static class Protocol
{
    /// <summary>
    /// ⭐⭐ LA VERSIONE DEL PROTOCOLLO — UNA SOLA DEFINIZIONE, E QUI.
    ///
    /// Prima questo numero stava in DUE posti: `Server\Server.cs` e
    /// `Client\Network\NetworkManager.cs`, ognuno con la sua `const byte PROTOCOL_VERSION = 11`. Due
    /// copie di un numero che deve essere identico su due macchine diverse sono una divergenza che
    /// aspetta di succedere, e il costo di sbagliarla non e' un errore di compilazione: e' il rifiuto
    /// dell'handshake (`RefusalType.Protocol`) a runtime, cioe' un client che non entra.
    ///
    /// E questo file aveva `public const int VERSION = 0` con ZERO occorrenze in tutto il repo: il
    /// posto giusto c'era, ed era vuoto. Il numero e' quindi un `byte`, che e' il tipo del byte di
    /// `Intro` sul filo: nessun cast da nessuna delle due parti.
    ///
    /// ⚠️ IL VALORE RESTA **11**. Le difese di questo giro (payload corto, `team`, `progressType`, id)
    /// hanno cambiato SOLO la validazione: nessun `NetworkType` nuovo, nessun campo nuovo, nessun byte
    /// diverso sul filo. La prova sono i 42 frame del banco headless: `sha256 9ecdfa42…`, identico
    /// prima e dopo ogni passo. Quindi non c'e' niente da alzare a 12: alzarlo qui sarebbe stata una
    /// discontinuita' voluta senza un byte cambiato.
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
