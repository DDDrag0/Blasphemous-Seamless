using Blasphemous.Multiplayer.Common;
using Blasphemous.Multiplayer.Server.Models;
using System;
using System.Collections.Generic;
#if !COOP_HOST
using Basalt.CommandParser;
#endif

namespace Blasphemous.Multiplayer.Server;

internal static class Core
{
#if !COOP_HOST
    // Sta solo nel processo della console: e' il server che `Main` crea. Nel client il server lo
    // crea `HostSession` e vive la' (`CoopServer _server`), quindi qui non servirebbe a niente.
    private static Server server;
#endif

    private static readonly Dictionary<byte, TeamInfo> teamGameDatas = [];

#if !COOP_HOST

    static string ApplicationTitle
    {
        get
        {
            string text = "Blasphemous Multiplayer Server";
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) ?? "Unknown";
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

        // Create server
        // ⭐⭐ C1b-1 — IL TRASPORTO SI SCEGLIE **QUI**, NON DENTRO IL SERVER. Il `Server` parla solo con
        // `IServerTransport`; l'unica implementazione di questo giro e' il TCP, che contiene le righe
        // del socket che prima stavano in `Server.Start`. Il composito c'e' perche' domani i fili
        // possono essere due (TCP per l'host locale + P2P per i peer): oggi ne contiene **uno**.
        CompositeServerTransport transport = new CompositeServerTransport();
        transport.Add(new TcpServerTransport());

        server = new Server(options.MaxPlayers, options.Password, transport);
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

    // ⚠️ NEL CLIENT QUESTA CLASSE NON HA UN `Main`, E NON E' UNA SVISTA.
    //
    // Qui sopra `#if !COOP_HOST` chiude fuori il `Main` dello standalone: e' l'unico posto di tutto il
    // server che dipende dalla CONSOLE (`Console.Title`, `Console.ReadKey`) e dagli ARGOMENTI della
    // riga di comando (`Basalt.CommandParser` + `ServerArguments`), cioe' due cose che nel processo
    // del gioco non esistono. Le altre due cose che `Main` faceva - `Environment.Exit(0)` per uscire
    // e il ciclo di lettura dei tasti - le sostituisce il bottone "Host session", che chiama
    // `Server.Start(port)` e `Server.Stop()`.
    //
    // Quello che RESTA di questa classe in entrambe le build e' il pezzo che serve al server vero: i
    // dati di squadra (`teamGameDatas`), che `Server.cs` legge e scrive in ~10 punti. Toglierli di
    // qui avrebbe voluto dire toccare `Server.cs` in quei punti, cioe' riscriverne la logica.
    //
    // ⇒ LA PORTA DELLO STANDALONE E' INVARIATA: stesso exe, stessa console, stessi argomenti
    //   `-p/--port`, `-mp/--max-players`, `-ps/--password`, stesso `ServerOutput.log`.

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
    /// Butta via i dati delle squadre in cui non c'e' piu' nessuno. La chiave del dizionario e' un
    /// <see cref="PeerId"/> (prima era `string ip`): a questo metodo la chiave NON serve — guarda solo
    /// i valori (`player.team`) — quindi cambia solo il tipo del parametro, non una riga di logica.
    /// Questo file e' compilato anche nel CLIENT (`COOP_HOST`): e' uno dei due punti in cui il tipo
    /// nuovo compare nel client, oltre al server.
    /// </summary>
    public static void removeUnusedGameData(Dictionary<PeerId, PlayerInfo> allPlayers)
    {
        for (byte i = 1; i <= 10; i++)
        {
            if (!teamGameDatas.ContainsKey(i))
                continue;

            // If no player is currently on this team, remove the game data
            bool teamExists = false;
            foreach (PlayerInfo player in allPlayers.Values)
            {
                if (player.team == i)
                {
                    teamExists = true;
                    break;
                }
            }
            if (!teamExists)
            {
                Logger.Info("Removing game data for team " + i);
                teamGameDatas.Remove(i);
            }
        }
    }
}
