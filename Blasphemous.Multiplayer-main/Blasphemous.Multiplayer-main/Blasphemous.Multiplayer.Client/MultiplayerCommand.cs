using Blasphemous.CheatConsole;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using System;
using System.Collections.Generic;

namespace Blasphemous.Multiplayer.Client
{
    public class MultiplayerCommand : ModCommand
    {
        protected override string CommandName => "multiplayer";

        protected override bool AllowUppercase => true;

        protected override Dictionary<string, Action<string[]>> AddSubCommands()
        {
            return new Dictionary<string, Action<string[]>>()
            {
                { "help", Obsolete },
                { "status", Obsolete },
                { "connect", Obsolete },
                { "disconnect", Obsolete },
                { "players", Obsolete },
                { "sync", Sync },
#if DEBUG
                { "damage", Damage },
#endif
            };
        }

        /// <summary>
        /// Enemy sync diagnostics. This feature has never been exercised in game, so these commands are
        /// the fastest way to tell whether it works:
        ///   "multiplayer sync"       one-line health summary, including the id-list fingerprint
        ///   "multiplayer sync dump"  every tracked enemy id (they must match on both machines)
        ///   "multiplayer sync world" raw scan of enemies, spawn points and game GUIDs in the scene
        /// </summary>
        private void Sync(string[] parameters)
        {
            string mode = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : string.Empty;

            if (mode == "dump")
            {
                List<string> lines = EnemySyncManager.Dump();
                Write($"Enemy sync dump ({lines.Count} entries):");
                foreach (string line in lines)
                    Write(line);
                return;
            }

            if (mode == "world")
            {
                List<string> lines = EnemyDiagnostics.DumpWorld();
                Write($"Enemy world scan ({lines.Count} lines), anche nel log:");
                foreach (string line in lines)
                    Write(line);

                EnemyDiagnostics.LogWorld();
                return;
            }

            Write("Enemy sync: " + EnemySyncManager.Describe());
            Write("Comandi: 'multiplayer sync dump' (id tracciati), 'multiplayer sync world' (scan grezzo della scena).");
        }

        private void Obsolete(string[] parameters)
        {
            Write("The multiplayer command has been removed.  Open the connection menu on the bottom of the screen.");
        }

//        private void Help(string[] parameters)
//        {
//            if (!ValidateParameterList(parameters, 0))
//                return;

//            Write("Available MULTIPLAYER commands:");
//            Write("multiplayer status: Display connection status");
//            Write("multiplayer connect SERVER NAME [PASSWORD]: Connect to SERVER with player name as NAME with optional PASSWORD");
//            Write("multiplayer disconnect: Disconnect from current server");
//            Write("multiplayer players: List all connected players in the server");
//#if DEBUG
//            Write("multiplayer damage TYPE AMOUNT: Simulates receiving a pvp attack");
//#endif
//        }

        private void Damage(string[] parameters)
        {
            if (!ValidateParameterList(parameters, 2))
                return;

            AttackType type = (AttackType)Enum.Parse(typeof(AttackType), parameters[0]);
            ValidateIntParameter(parameters[1], 0, 255, out int amount);

            ModLog.Warn($"Testing attack {type} with damage {amount}");
            Main.Multiplayer.AttackManager.DamagePlayer_Internal(type, (byte)amount);
        }
    }
}
