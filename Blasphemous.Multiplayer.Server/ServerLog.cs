using System;
using System.IO;
using System.Text;

namespace Blasphemous.Multiplayer.Server;

public static class ServerLog
{
    private static readonly object gate = new object();

    private static string path;
    private static bool initialized;

    public static void Initialize()
    {
        lock (gate)
        {
            if (initialized)
                return;

            initialized = true;

            try
            {
                string directory = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(directory))
                    directory = Directory.GetCurrentDirectory();

                path = Path.Combine(directory, "ServerOutput.log");

                TryWriteAllText(
                    path,
                    $"# Blasphemous COOP server log - started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
            }
            catch
            {
                path = null;
            }
        }
    }

    public static void Write(string line)
    {
        if (!initialized)
            Initialize();

        if (path == null)
            return;

        lock (gate)
        {
            try
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Keep the console working even when the file cannot be written, and do not retry in a
                // loop: one failed disk is not worth a stalled server.
            }
        }
    }

    private static void TryWriteAllText(string file, string contents)
    {
        try
        {
            File.WriteAllText(file, contents, Encoding.UTF8);
        }
        catch
        {
            path = null;
        }
    }
}