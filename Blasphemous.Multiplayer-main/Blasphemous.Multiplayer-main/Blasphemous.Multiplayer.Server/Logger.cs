using System;
#if COOP_HOST
using Blasphemous.ModdingAPI;
#endif

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// Il log del server: UNA porta sola per tutte le sue righe, con DUE destinazioni che dipendono da
/// dove gira l'engine.
///
/// · STANDALONE (`net8.0`, cartella `SERVER-COOP\`): console colorata + specchio su file
///   (`ServerLog` -> `ServerOutput.log` accanto all'eseguibile). Identico a prima.
/// · DENTRO IL CLIENT (`net35`, bottone "Host session"): **`ModLog`**, cioe' il log della mod.
///   Ed e' obbligatorio, non una preferenza: `Console.ForegroundColor` e `Console.WriteLine`
///   LANCIANO `IOException` quando il processo non ha una console - cioe' sempre, dentro il gioco -
///   quindi qui non possono nemmeno essere nominati. Stessa ragione per `ServerLog`: scriverebbe
///   `ServerOutput.log` nella cartella di gioco del PROCESSO, fuori dal log che si guarda.
///
/// Le due destinazioni condividono la stessa formattazione (una riga, `[HH:mm:ss] messaggio`) e la
/// stessa severita', che nel client diventa quella di `ModLog` (`Info`/`Warn`/`Error`): un
/// `Logger.Error` del server non deve arrivare al log della mod come se fosse un'informazione.
/// </summary>
public static class Logger
{
    public static void Info(object message)
    {
#if COOP_HOST
        Print(message, HostLevel.Info);
#else
        Console.ForegroundColor = ConsoleColor.White;
        Print(message);
#endif
    }

    public static void Warn(object message)
    {
#if COOP_HOST
        Print(message, HostLevel.Warn);
#else
        Console.ForegroundColor = ConsoleColor.Yellow;
        Print(message);
#endif
    }

    public static void Error(object message)
    {
#if COOP_HOST
        Print(message, HostLevel.Error);
#else
        Console.ForegroundColor = ConsoleColor.Red;
        Print(message);
#endif
    }

    public static void ProgressGood(object message)
    {
#if COOP_HOST
        Print(message, HostLevel.Info);
#else
        Console.ForegroundColor = ConsoleColor.Green;
        Print(message);
#endif
    }

    public static void ProgressBad(object message)
    {
#if COOP_HOST
        Print(message, HostLevel.Info);
#else
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Print(message);
#endif
    }

#if COOP_HOST

    /// <summary>
    /// Le tre severita' che questo file usa davvero. Un `enum` privato e non una `stringa` perche'
    /// una severita' scritta a mano e' una severita' che si sbaglia: qui il compilatore la controlla.
    /// </summary>
    private enum HostLevel
    {
        Info,
        Warn,
        Error,
    }

    /// <summary>
    /// La riga, sul log della mod. Stessa forma dello standalone (`[HH:mm:ss] messaggio`) e stessa
    /// severita', con una sola differenza dichiarata: nessun colore, perche' un log di file non ne ha.
    ///
    /// ⚠️ `Logger.Info` e' chiamato anche dal thread di lettura di `SimpleTCP` (connessioni,
    /// disconnessioni): `ModLog` e' thread-safe, quindi non serve nessun lock qui.
    /// </summary>
    private static void Print(object message, HostLevel level)
    {
        string line = "[Server] [" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;

        switch (level)
        {
            case HostLevel.Warn: ModLog.Warn(line); break;
            case HostLevel.Error: ModLog.Error(line); break;
            default: ModLog.Info(line); break;
        }
    }

#else

    public static void Print(object message)
    {
        // One line, in one place, on both destinations.
        //
        // The console is what the operator watches; the file is what survives the window being closed, and
        // it is the only reason the server's account of a session can be examined afterwards. They are
        // formatted together so the two can never drift apart - a mismatch between what was seen and what
        // was kept would be worse than having no file at all.
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        Console.WriteLine(line);
        ServerLog.Write(line);
    }

#endif
}
