using System;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// ⭐ L'INVITO A GIOCO SPENTO: leggere `+connect_lobby &lt;id&gt;` dalla riga di comando.
///
/// IL CASO. Quando un amico accetta un invito e Blasphemous **non** e' in esecuzione, e' Steam ad
/// avviare il gioco, e l'unica cosa che consegna al processo nuovo e' un argomento sulla riga di
/// comando: non esiste nessun processo a cui mandare una callback, perche' prima non c'era. Quindi
/// questo caso non puo' arrivare da `GameLobbyJoinRequested_t` (quello e' il caso "gioco acceso",
/// vedi `SteamLobby.OnJoinRequested`): va letto qui.
///
/// ⚠️ NESSUNO LO LEGGE PER NOI. La stringa `connect_lobby` **non compare affatto** in
/// `Assembly-CSharp-firstpass.dll` (0 occorrenze, verificato sui byte dell'assembly): il gioco non
/// installa nessun gestore dell'argomento. Il parsing e' nostro, e questo file e' tutto il parsing
/// che c'e'.
///
/// ⭐ DUE FORME, NON UNA. Steam usa la forma a **due argomenti separati** (`+connect_lobby`,
/// `&lt;id&gt;`), ed e' quella che scrive lui; ma un utente puo' anche incollare `+connect_lobby=&lt;id&gt;`
/// in una scorciatoia o nelle opzioni di avvio, quindi si accettano entrambe. L'id e' un `ulong`
/// in decimale: si prova a leggerlo e, se non e' un numero, si **scarta in silenzio** (nessun
/// crash, nessuna eccezione: una riga di comando sbagliata non e' un guasto del gioco).
///
/// ⚠️ MAI L'INDICE 0: e' il percorso dell'eseguibile, e su una macchina dove il gioco sta in una
/// cartella che contiene la parola `+connect_lobby` (assurdo, ma gratis da evitare) non e' un
/// argomento. Si comincia da 1.
///
/// ⚠️ QUESTO FILE NON DIPENDE DA STEAMWORKS, di proposito: cosi' si puo' compilare e provare da solo,
/// senza gioco e senza Steam (vedi il test headless nel referto). Se un giorno gli servisse
/// `Steamworks`, quella proprieta' sparirebbe.
/// </summary>
internal static class SteamInviteCommandLine
{
    /// <summary>L'argomento che Steam mette sulla riga di comando: `+connect_lobby`.</summary>
    internal const string ARGUMENT = "+connect_lobby";

    /// <summary>
    /// Cerca un id di lobby negli argomenti. Ritorna `false` (e `lobbyId = 0`) se non c'e', se e'
    /// l'ultimo argomento senza valore, o se il valore non e' un numero: **non lancia mai**.
    /// </summary>
    internal static bool TryParse(string[] args, out ulong lobbyId)
    {
        lobbyId = 0UL;

        try
        {
            if (args == null)
                return false;

            // ⚠️ da 1: l'indice 0 e' il percorso dell'eseguibile.
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg))
                    continue;

                // ⭐ forma 1 (quella di Steam): "+connect_lobby" "<id>" — due argomenti separati.
                if (string.Equals(arg, ARGUMENT, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                        continue;

                    if (TryParseId(args[i + 1], out lobbyId))
                        return true;

                    // Valore illeggibile: non si interrompe la scansione, ma non si e' trovato
                    // niente nemmeno qui (si prova il resto della riga).
                    continue;
                }

                // ⭐ forma 2 (protettiva): "+connect_lobby=<id>" — un solo argomento.
                if (arg.Length > ARGUMENT.Length && arg[ARGUMENT.Length] == '=' &&
                    string.Compare(arg, 0, ARGUMENT, 0, ARGUMENT.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (TryParseId(arg.Substring(ARGUMENT.Length + 1), out lobbyId))
                        return true;
                }
            }

            lobbyId = 0UL;
            return false;
        }
        catch (Exception)
        {
            // Una riga di comando non deve poter rompere il caricamento della mod.
            lobbyId = 0UL;
            return false;
        }
    }

    /// <summary>
    /// Un id di lobby e' un `ulong` in decimale. Le virgolette (una scorciatoia le mette) e gli spazi
    /// ai bordi si tolgono; qualunque altra cosa = non e' un id. `0` non e' un id valido.
    /// </summary>
    private static bool TryParseId(string text, out ulong lobbyId)
    {
        lobbyId = 0UL;

        if (string.IsNullOrEmpty(text))
            return false;

        string clean = text.Trim().Trim('"').Trim();
        if (clean.Length == 0)
            return false;

        // `NumberStyles.None`: niente segni, niente spazi, niente esadecimali. Un id di lobby e'
        // solo cifre, e accettare altro vorrebbe dire accettare una stringa che Steam non manda.
        if (!ulong.TryParse(clean, NumberStyles.None, CultureInfo.InvariantCulture, out lobbyId))
        {
            lobbyId = 0UL;
            return false;
        }

        if (lobbyId == 0UL)
            return false;

        return true;
    }
}
