using System;
using System.IO;
using System.Text;

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// Mirrors the console output into a rotating-free text file, so the server's own account of a session
/// survives the window being closed.
///
/// WHY THIS EXISTS. The server logged to the console and nowhere else, which cost two investigations in a
/// row: the ownership deduplication that silently discards repeated progress, and the lamp that stopped
/// being synchronised after a bonfire, were both diagnosed from client logs because the server's log had
/// simply never been kept. The lines that would have proved them in one look are
/// "Received new pers. object from X: Y" and "Received duplicated or inferior progress from X: Y", both of
/// which are written here.
///
/// The file is written synchronously, under a lock, and flushed after every line. That is slower than
/// queueing to a background thread, and deliberately so: a log that loses its last lines when the process
/// is stopped - and this server is stopped with a keypress - is worse than useless, because it is trusted.
/// The server emits a handful of lines per event, not thousands per second, so the cost is not measurable.
///
/// Nothing here may take the server down. Every failure is swallowed: an unwritable folder, a locked file
/// or a full disk costs us the mirror and nothing more, and the caller cannot tell the difference.
/// </summary>
public static class ServerLog
{
    private static readonly object gate = new object();

    private static string path;
    private static bool initialized;

    /// <summary>
    /// Creates the log file beside the server binary and opens the mirror.
    ///
    /// Safe to call more than once, and safe to call before anything is logged: the first write initialises
    /// it anyway, so a future ordering change cannot silently lose the beginning of a session.
    /// </summary>
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

                // The header carries the version and the time, because a log without a timestamp - or one
                // whose file was overwritten by a later run - is exactly the ambiguity this file removes.
                TryWriteAllText(
                    path,
                    $"# Blasphemous COOP server log - started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
            }
            catch
            {
                // No file. The console output is unaffected.
                path = null;
            }
        }
    }

    /// <summary>Writes one already-formatted line to the mirror, if there is one.</summary>
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
