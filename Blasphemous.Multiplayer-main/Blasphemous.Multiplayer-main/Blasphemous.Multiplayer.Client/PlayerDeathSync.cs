using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Watches a player's death, and decides whether this machine should suppress what the game does to the
    /// WORLD when it happens.
    ///
    /// WHY THIS EXISTS. When a player dies the game respawns every enemy it had consumed, and it does so
    /// LOCALLY: the whole chain - Penitent.Respawn, EnemySpawner.Reset, SpawnManager.Respawn, ChangeLevel,
    /// SpawnEnemiesOnLoad - contains no network call at all, and the mod has no hook on death. Measured, not
    /// assumed: in one session the dying machine came back to a room with five enemies where the other
    /// machine still had three, and the two only agreed again because a bonfire announcement happened to
    /// arrive later - for a DIFFERENT room.
    ///
    /// So the effect is not "one player's death is unfair to the others". It is that the two machines
    /// DISAGREE about which enemies are alive, and stay that way until something else forces them together.
    /// In a Metroidvania that is worse than unfair: if one player is halfway through a punishing platform
    /// section and the other dies at the start of the map, the survivor should not have the world reset in
    /// their face - and the dying player should not silently invalidate the survivor's progress.
    ///
    /// WHAT IS SUPPRESSED, AND WHAT IS NOT. Only the part that touches the world. The health and flask
    /// refill lives in PersistentManager.RestoreStored, a SEPARATE call inside Penitent.Respawn, so
    /// suppressing the enemy reset leaves the refill intact - and it must stay: without it the player comes
    /// back to the bonfire with nothing.
    ///
    /// THE FLAG IS READ BY EVERYTHING THAT NEEDS IT. EnemySpawner.Reset must not clear the consumed list,
    /// and the mod's own death memory must not be cleared either - because those are the two halves of the
    /// same fact, "those enemies are alive again". If only one of them were suppressed the game and the mod
    /// would disagree, and a machine holding dead enemies would stop announcing them to a joiner. Reading
    /// one flag is what makes that impossible rather than merely remembered.
    /// </summary>
    public static class PlayerDeathSync
    {
        /// <summary>
        /// True while Penitent.Respawn is running on a machine that is suppressing the world reset.
        ///
        /// Read by the EnemySpawner.Reset prefix and by the death-memory reset, both of which are waiting
        /// for it. Set in a prefix on Respawn and cleared in a FINALIZER, not a postfix: Respawn calls the
        /// enemy reset, the restore and the level load in a row, and if any of them throws, a postfix would
        /// never run and the flag would stay raised for the rest of the session - the same trap the progress
        /// queue had, where one exception could freeze synchronisation permanently.
        /// </summary>
        public static bool SuppressingWorldReset { get; internal set; }

        /// <summary>
        /// True when this machine should suppress the world reset on death.
        ///
        /// Two conditions, both required: a connection, and at least one other player on it. Alone - single
        /// player, or the host of an empty server - the game's own behaviour is correct and better left
        /// alone.
        ///
        /// IsConnected is used rather than EnemySyncManager.IsHost, and that is deliberate: IsHost is
        /// "_isHost || !IsConnected", so it answers true when nobody is connected at all. Here the question
        /// is literally "is anybody else here", so it is asked directly.
        ///
        /// KNOWN LIMITATION: AllConnectedPlayers is filled when the server's connection packet arrives, so
        /// there is a short window at startup where the socket is up but the list is still empty. For an
        /// event like a death, and not a per-frame test, that is acceptable - and saying so is better than
        /// pretending the check is instantaneous.
        /// </summary>
        public static bool ShouldSuppressWorldReset()
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return false;

            return Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Count > 0;
        }

        /// <summary>
        /// Reads the values back, for logs. Null-safe: the player may not exist yet.
        ///
        /// WHICH MAXIMUM, AND WHY IT MATTERS. Everything is reported against **Final**, which is what the
        /// game's own bars divide by: `PlayerHealth.BarTarget = Life.Current / Life.Final`, and PlayerFlask
        /// builds its total from Flask.Final. CurrentMax is a different number - it is Final clamped to
        /// MaxValue and then scaled by MaxFactor, so it moves when the game's guilt penalty moves it.
        ///
        /// The first version printed CurrentMax and produced a reading that looked impossible: fervour=37/6,
        /// a current larger than its own maximum. It was not a bug in the game and not a bug in the player -
        /// it was this line reading two numbers from two different moments. The prefix on Respawn runs while
        /// the player is dead and the Penitent is being rebuilt, and the guilt factor is re-applied DURING
        /// the respawn, so CurrentMax was caught mid-transition while Current had not been clamped yet.
        ///
        /// Final does not take part in that clamp, so it is the stable number and the one the bar uses. For
        /// fervour the clamped value is reported too, because PlayerFervour draws its segments from
        /// CurrentMax and it is worth seeing both when they disagree.
        ///
        /// MaxFactor is printed as well, because when a maximum moves it is worth knowing whether the cause
        /// was ours or the game's native guilt system - which, as it turns out, is the only thing that ever
        /// moves it: `set_MaxFactor` has two callers in the whole assembly, both GuiltManager, both on
        /// Fervour.
        /// </summary>
        public static string StatsLine()
        {
            try
            {
                if (Core.Logic == null || Core.Logic.Penitent == null || Core.Logic.Penitent.Stats == null)
                    return "stats=non disponibili";

                var stats = Core.Logic.Penitent.Stats;
                return $"vita={stats.Life.Current:F0}/{stats.Life.Final:F0} " +
                       $"fiale={stats.Flask.Current:F0}/{stats.Flask.Final:F0} " +
                       $"fervour={stats.Fervour.Current:F0}/{stats.Fervour.Final:F0} " +
                       $"[fervour con la penalita' di colpa: max={stats.Fervour.CurrentMax:F0}] " +
                       $"(fattori: vita={stats.Life.MaxFactor:F2} fervour={stats.Fervour.MaxFactor:F2})";
            }
            catch (System.Exception e)
            {
                return $"stats=errore ({e.GetType().Name})";
            }
        }
    }

    /// <summary>
    /// The death, as this machine sees it.
    ///
    /// Respawn has exactly one caller in the whole assembly - DeadScreenWidget, after the player pressed a
    /// key - which is what makes it a reliable place to detect a death: it runs once, when the player has
    /// actually chosen to come back, and not while the death screen is merely sitting there.
    ///
    /// PIECE 1 OF THE DEATH WORK, AND IT ONLY MEASURES. It raises the flag, reports the decision and the
    /// stats, and changes nothing. The suppression itself is the next step, deliberately separate: with the
    /// detection and the suppression in the same commit, "the flag was never raised" and "the suppression
    /// did not work" would look identical in the log.
    /// </summary>
    [HarmonyPatch(typeof(Penitent), "Respawn")]
    public static class PenitentRespawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            bool connected = Main.Multiplayer.NetworkManager.IsConnected;
            int peers = 0;
            try
            {
                peers = Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Count;
            }
            catch (System.Exception)
            {
                // The list may not exist yet during startup; a missing count is reported as zero.
            }

            bool suppress = PlayerDeathSync.ShouldSuppressWorldReset();
            PlayerDeathSync.SuppressingWorldReset = suppress;

            ModLog.Info($"[Morte] il giocatore e' morto: connesso={connected} altri giocatori={peers} " +
                        $"=> {(suppress ? "SOPPRIMO il respawn del mondo" : "lascio fare al gioco")} · " +
                        $"{PlayerDeathSync.StatsLine()}");
        }

        /// <summary>
        /// The flag comes down even if Respawn throws.
        ///
        /// A finalizer runs on every exit path, including the exceptional one. A postfix would not - which
        /// is how a flag with no guaranteed way down becomes a permanent freeze, and the project has
        /// already paid for that lesson once.
        /// </summary>
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            PlayerDeathSync.SuppressingWorldReset = false;
        }
    }

    /// <summary>
    /// Reports the player's stats right after the bonfire has applied its benefits.
    ///
    /// Resting at a bonfire is where the "world yes, comfort no" boundary would break if it ever broke: the
    /// benefits live in PrieDieu.ShallowActivationLogic, which the replication deliberately never calls -
    /// so the receiving machine should finish this method with the same health and flasks it had before.
    /// That was guaranteed by construction but never MEASURED, because nothing in the logs reported the
    /// player's own values. This line is the measurement: sit at a bonfire on one machine and compare the
    /// two logs.
    ///
    /// It hooks RespawnDeadEnemies rather than PrieDieu.OnUse on purpose. The postfix on OnUse runs BEFORE
    /// the coroutine body does anything, so it would read the values before the benefits; this hook runs
    /// after ShallowActivationLogic in the first-activation path, which is where "after" means something.
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawner), "RespawnDeadEnemies")]
    public static class RespawnDeadEnemiesStats_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // The suffix reports whether THIS call came from a replicated bonfire, and it used to read the
            // wrong flag: PlayerDeathSync.SuppressingWorldReset is the DEATH flag, while a replication is
            // marked by EnemySyncManager.IsReplicatingBonfire. The two are unrelated, so the suffix never
            // appeared - not even on the line that genuinely was a replication, which is how the mistake was
            // caught. A label that is always false is worse than no label: it reads as evidence.
            string origin = EnemySyncManager.IsReplicatingBonfire
                ? " (stiamo replicando un falo'')"
                : PlayerDeathSync.SuppressingWorldReset
                    ? " (stiamo sopprimendo il respawn di una morte)"
                    : " (falo' locale)";

            ModLog.Info($"[Falo''] stats dopo il respawn: {PlayerDeathSync.StatsLine()}{origin}");
        }
    }
}
