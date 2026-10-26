using System;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// Parses the lobby id from "+connect_lobby &lt;id&gt;" on the command line.
///
/// When a friend accepts an invite while Blasphemous is not running, Steam starts the game and
/// delivers only a command-line argument - there is no process to receive GameLobbyJoinRequested_t.
/// The game itself does not handle the argument (connect_lobby does not appear in
/// Assembly-CSharp-firstpass.dll), so the parsing is done here.
///
/// Accepts both "+connect_lobby" "&lt;id&gt;" (Steam's form) and "+connect_lobby=&lt;id&gt;" (a user
/// pasting it into a shortcut). The id is a ulong in decimal; anything unparsable is discarded
/// silently. Index 0 (the executable path) is never inspected.
///
/// Does not depend on Steamworks on purpose, so it can be built and tested standalone.
/// </summary>
internal static class SteamInviteCommandLine
{
    /// <summary>The command-line argument Steam uses: "+connect_lobby".</summary>
    internal const string ARGUMENT = "+connect_lobby";

    /// <summary>
    /// Searches for a lobby id. Returns false (and lobbyId = 0) when absent, when the value is
    /// missing, or when it is not a number. Never throws.
    /// </summary>
    internal static bool TryParse(string[] args, out ulong lobbyId)
    {
        lobbyId = 0UL;

        try
        {
            if (args == null)
                return false;

            // Start at 1: index 0 is the executable path.
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg))
                    continue;

                // Form 1 (Steam's): "+connect_lobby" "<id>" as two arguments.
                if (string.Equals(arg, ARGUMENT, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                        continue;

                    if (TryParseId(args[i + 1], out lobbyId))
                        return true;

                    continue;
                }

                // Form 2 (protective): "+connect_lobby=<id>" as one argument.
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
            // A malformed command line must not break mod loading.
            lobbyId = 0UL;
            return false;
        }
    }

    /// <summary>
    /// Parses a lobby id: a ulong in decimal. Strips surrounding quotes and whitespace; anything
    /// else is not an id. Zero is rejected.
    /// </summary>
    private static bool TryParseId(string text, out ulong lobbyId)
    {
        lobbyId = 0UL;

        if (string.IsNullOrEmpty(text))
            return false;

        string clean = text.Trim().Trim('"').Trim();
        if (clean.Length == 0)
            return false;

        // NumberStyles.None: no signs, no whitespace, no hex. Steam sends only digits.
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