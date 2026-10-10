using System;

namespace Blasphemous.Multiplayer.Server;

public static class Logger
{
    public static void Info(object message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Print(message);
    }

    public static void Warn(object message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Print(message);
    }

    public static void Error(object message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Print(message);
    }

    public static void ProgressGood(object message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Print(message);
    }

    public static void ProgressBad(object message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Print(message);
    }

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
}
