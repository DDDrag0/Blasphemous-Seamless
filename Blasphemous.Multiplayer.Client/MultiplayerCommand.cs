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
        /// Enemy sync diagnostics. Every line is also written via ModLog because console output does
        /// not reach LogOutput.log. Modes: "multiplayer sync" (all), "brief" (summary), "dump" (ids),
        /// "world" (scene scan).
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

        private void Sync(string[] parameters)
        {
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
                Write($"Enemy sync dump ({lines.Count} lines):");
                foreach (string line in lines)
                {
                    Write(line);
                    ModLog.Info("[EnemyDump] " + line);
                }
            }

            if (mode == "world" || mode == "all")
            {
                List<string> lines = EnemyDiagnostics.DumpWorld();
                Write($"Enemy world scan ({lines.Count} lines):");
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