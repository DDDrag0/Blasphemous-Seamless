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
                { "spawn", Spawn },
                { "dead", Dead },
#if DEBUG
                { "damage", Damage },
#endif
            };
        }

        /// <summary>
        /// Enemy sync diagnostics. Everything is also written to the log file via ModLog, because console
        /// output does not reach LogOutput.log - which previously meant a remote bug report contained none
        /// of the data needed to diagnose it.
        ///
        ///   "multiplayer sync"        summary + tracked ids + raw scene scan (the usual one)
        ///   "multiplayer sync brief"  summary only
        ///   "multiplayer sync dump"   tracked ids only
        ///   "multiplayer sync world"  raw scene scan only
        /// </summary>
        /// <summary>
        ///   "multiplayer spawn"  every spawn point with the flags that decide whether it spawns
        ///   "multiplayer dead"   the spawn points the game itself considers defeated
        ///
        /// Kept as two commands rather than more modes of "sync" because they answer a different question:
        /// not what the sync thinks, but what the GAME thinks. Diagnosing the death problem needs the
        /// game's own view, and the game tracks defeated enemies by spawn point position, not by id.
        /// </summary>
        private void Spawn(string[] parameters)
        {
            ModLog.Info("[EnemyCmd] multiplayer spawn invoked");

            List<string> lines = EnemySyncManager.DumpSpawnPoints();
            foreach (string line in lines)
            {
                Write(line);
                ModLog.Info("[EnemySpawn] " + line);
            }
        }

        private void Dead(string[] parameters)
        {
            ModLog.Info("[EnemyCmd] multiplayer dead invoked");

            List<string> lines = EnemySyncManager.DumpConsumedSpawnPoints();
            foreach (string line in lines)
            {
                Write(line);
                ModLog.Info("[EnemyDead] " + line);
            }
        }

        private void Sync(string[] parameters)        {
            string mode = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "all";

            if (mode != "brief" && mode != "dump" && mode != "world")
                mode = "all";

            // Logged first, before anything can throw, so a log always shows whether the command ran.
            ModLog.Info($"[EnemyCmd] multiplayer sync invoked, mode='{mode}'");

            if (mode == "brief" || mode == "all")
            {
                string summary = EnemySyncManager.Describe();
                Write("Enemy sync: " + summary);
                ModLog.Info("[EnemySummary] " + summary);
            }

            if (mode == "dump" || mode == "all")
            {
                List<string> lines = EnemySyncManager.Dump();
                Write($"Enemy sync dump ({lines.Count} righe):");
                foreach (string line in lines)
                {
                    Write(line);
                    ModLog.Info("[EnemyDump] " + line);
                }
            }

            if (mode == "world" || mode == "all")
            {
                List<string> lines = EnemyDiagnostics.DumpWorld();
                Write($"Enemy world scan ({lines.Count} righe):");
                foreach (string line in lines)
                {
                    Write(line);
                    ModLog.Info("[EnemyWorld] " + line);
                }
            }
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
