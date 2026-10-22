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

        // Create server
        // ⭐⭐ C1b-1 — IL TRASPORTO SI SCEGLIE **QUI**, NON DENTRO IL SERVER. Il `Server` parla solo con
        // `IServerTransport`; l'unica implementazione di questo giro e' il TCP, che contiene le righe
        // del socket che prima stavano in `Server.Start`. Il composito c'e' perche' domani i fili
        // possono essere due (TCP per l'host locale + P2P per i peer): oggi ne contiene **uno**.
        CompositeServerTransport transport = new CompositeServerTransport();
        transport.Add(new TcpServerTransport());

        // ⭐ IL SERVER E' UNA VARIABILE LOCALE, non un campo statico: prima era `private static Server
        // server` (`#if !COOP_HOST`) assegnato qui e letto SOLO dalla riga qui sotto, nello stesso `Main`.
        // Come campo era write-only: nessun altro file lo nominava (e non poteva, essendo `private`).
        // Il tipo e' locale, la vita e' quella di `Main`, e il comportamento non cambia.
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
        // ⭐⭐ TUTTI i valori possibili della chiave, non 1..10. Prima questo ciclo era
        // `for (byte i = 1; i <= 10; i++)`, e le squadre 0 e 11..255 non venivano MAI guardate: `team`
        // arriva dal filo e `getTeamData` crea una voce per qualunque valore, quindi bastava UN
        // pacchetto con `team = 200` per lasciare nel dizionario una squadra che nessuna pulizia
        // avrebbe piu' rimosso - e nel client questo processo e' il gioco, che vive per ore.
        // ⇒ 256 valori, l'intero dominio di un `byte`: nessuna squadra puo' restare immortale, nemmeno
        // se un domani l'intervallo valido cambia (`Server.MIN_TEAM`/`MAX_TEAM`, che oggi e' 1..10).
        // Il conto e' `int` e non `byte` di proposito: con `byte` la condizione `i <= 255` non
        // finirebbe mai (al 255 l'incremento avvolge a 0).
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
