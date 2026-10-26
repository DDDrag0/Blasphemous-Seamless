using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Server.Models;
using System;
using System.Collections.Generic;
#if !COOP_HOST
using Basalt.CommandParser;
#endif

namespace Blasphemous.Multiplayer.Server;

/// <summary>
/// Server entry point (standalone build) and shared team-data helpers used by
/// <c>Server.cs</c> in both builds.
/// </summary>
internal static class Core
{
    private static readonly Dictionary<byte, TeamInfo> teamGameDatas = [];

#if !COOP_HOST

    static string ApplicationTitle
    {
        get
        {
            string text = "Blasphemous Multiplayer Server";
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            return $"{text} v{version}";
        }
    }

    static void Main(string[] args)
    {
        // Title
        Console.Title = ApplicationTitle;
        Console.WriteLine(string.Empty);

        // Read options from args
        var options = CommandParser.ProcessArguments<ServerArguments>(args);

        // Transport is chosen here; the server only knows IServerTransport.
        CompositeServerTransport transport = new CompositeServerTransport();
        transport.Add(new TcpServerTransport());

        Server server = new Server(options.MaxPlayers, options.Password, transport);
        if (!server.Start(options.Port))
        {
            Logger.Error($"Server failed to start on port {options.Port}");
            return;
        }

        // Initial messages
        Logger.Info($"Server has been started on port {options.Port}");
        Logger.Info("Press 'esc' to exit");

        // Start read loop
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Escape)
            {
                Logger.Info("Shutting down server");
                Environment.Exit(0);
            }
        }
    }

#endif

    // No Main in the client build: Main depends on Console and command-line args, which do not
    // exist in the game process. What remains in both builds is the team-data store, used by
    // Server.cs.

    public static TeamInfo getTeamData(byte team)
    {
        if (teamGameDatas.ContainsKey(team))
            return teamGameDatas[team];

        Logger.Info("Creating new game data for team " + team);
        TeamInfo newData = new TeamInfo();
        teamGameDatas.Add(team, newData);
        return newData;
    }

    /// <summary>
    /// Drops team data for teams with no remaining player. Iterates the full byte domain (0..255,
    /// not just the valid 1..10 range): a packet carrying an out-of-range <c>team</c> creates an
    /// entry that no narrower loop would ever clean up.
    /// </summary>
    public static void removeUnusedGameData(Dictionary<PeerId, PlayerInfo> allPlayers)
    {
        // int, not byte: a byte counter would wrap at 255 and never exit the loop.
        for (int i = 0; i <= byte.MaxValue; i++)
        {
            byte team = (byte)i;

            if (!teamGameDatas.ContainsKey(team))
                continue;

            // If no player is currently on this team, remove the game data
            bool teamExists = false;
            foreach (PlayerInfo player in allPlayers.Values)
            {
                if (player.team == team)
                {
                    teamExists = true;
                    break;
                }
            }
            if (!teamExists)
            {
                Logger.Info("Removing game data for team " + team);
                teamGameDatas.Remove(team);
            }
        }
    }
}