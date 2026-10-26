using System;
#if COOP_HOST
using Blasphemous.ModdingAPI;
#endif

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// Single entry point for server log lines, with two destinations depending on the build.
///
/// Standalone (<c>net8.0</c>): coloured console plus mirror to <c>ServerOutput.log</c>. In the
/// client (<c>net35</c>): <c>ModLog</c>, because <c>Console.*</c> throws <c>IOException</c> when no
/// console exists (i.e. always inside the game). Same line format and severity on both
/// destinations, so <c>Logger.Error</c> is not logged as plain info by <c>ModLog</c>.
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

    /// <summary>The three severities this file actually uses. Enum so the compiler checks them.</summary>
    private enum HostLevel
    {
        Info,
        Warn,
        Error,
    }

    /// <summary>
    /// Formats the line for ModLog, same shape as standalone (<c>[HH:mm:ss]</c> + message) and
    /// same severity, minus colour. ModLog is thread-safe, so no lock is needed even when this is
    /// called from SimpleTCP's reader thread.
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
        // Console is what the operator watches; the file survives the window being closed and is
        // what can be examined afterwards. Formatted together so the two cannot drift apart.
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        Console.WriteLine(line);
        ServerLog.Write(line);
    }

#endif
}