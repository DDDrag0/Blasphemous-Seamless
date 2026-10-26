using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Watches the local player's death and decides whether this machine should suppress the
    /// game's world reset (enemy respawn) at that moment. The game does the reset LOCALLY with no
    /// network call, so without this the two machines disagree about which enemies are alive. Only
    /// the world effects are suppressed; the player's health/flask refill (a separate call inside
    /// Penitent.Respawn) is kept, so the player still benefits from the bonfire. The flag is read
    /// by EnemySpawner.Reset and by the mod's own death-memory reset - reading one flag guarantees
    /// those two cannot diverge.
    /// </summary>
    public static class PlayerDeathSync
    {
        /// <summary>
        /// True while Penitent.Respawn is running on a machine that is suppressing the world reset.
        /// Set in a Prefix and cleared in a Finalizer (not a Postfix): Respawn calls enemy reset,
        /// restore, and level load in a row, and if any throws a Postfix would never run - the
        /// flag would stay raised for the session. A Finalizer runs on every exit path.
        /// </summary>
        public static bool SuppressingWorldReset { get; internal set; }

        /// <summary>
        /// Whether this machine has told others that its own player is down. Prevents "I am alive
        /// again" from being sent on every level load: CreatePlayer runs per level, and would
        /// otherwise flood the channel with messages that cancel nothing.
        /// </summary>
        private static bool localDeathAnnounced;

        /// <summary>
        /// Death marker on the enemy-death channel. The server relays that channel to every peer
        /// but the sender with no scene filter, so a new prefix costs nothing and needs no
        /// protocol bump.
        /// </summary>
        public const string DeathPrefix = "pdead|";

        /// <summary>
        /// Alive counterpart. Without it the set of dead players only grows and the spectator state
        /// could never end.
        /// </summary>
        public const string AlivePrefix = "palive|";

        /// <summary>True when this id is a death announcement rather than an enemy death.</summary>
        public static bool IsDeathId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(DeathPrefix, System.StringComparison.Ordinal);
        }

        /// <summary>True when this id is the alive counterpart.</summary>
        public static bool IsAliveId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(AlivePrefix, System.StringComparison.Ordinal);
        }

        /// <summary>The player name carried by one of the two announcements, or null.</summary>
        public static string NameOf(string id, string prefix)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(prefix, System.StringComparison.Ordinal))
                return null;

            string name = id.Substring(prefix.Length);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// True while this machine's own player is down. Read from the game's Status.Dead, not from
        /// our announcement flag: the flag is raised when the death is TOLD, while Status.Dead is
        /// true from the fall until respawn finishes - and consumers that need to stop treating a
        /// corpse as a participant (the ownership rule) need the second window.
        /// </summary>
        public static bool IsLocalPlayerDead()
        {
            try
            {
                return Core.Logic != null
                    && Core.Logic.Penitent != null
                    && Core.Logic.Penitent.Status != null
                    && Core.Logic.Penitent.Status.Dead;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// True when this player is alive AND we know it. A remote player we have never heard from
        /// is not evidence of life.
        /// </summary>
        public static bool IsRemotePlayerKnownAlive(string name)
        {
            var player = Main.Multiplayer.OtherPlayerManager.FindConnectedPlayerQuietly(name);
            return player != null && !player.IsDead;
        }

        /// <summary>
        /// Tells other machines this player died. The name must travel: the receiver discards the
        /// sender and ProcessQueue passes only the bytes, so without a name the receiver cannot
        /// tell who died. The validator forbids '|' in names, so the payload cannot collide with a
        /// spawn-point id. Returns whether the announcement went out; only that earns a matching
        /// "alive again".
        /// </summary>
        public static bool AnnounceDeath()
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return false;

            string name = Main.Multiplayer.PlayerName;
            if (string.IsNullOrEmpty(name))
                return false;

            Main.Multiplayer.SendEnemyDeathGlobal(DeathPrefix + name);
            localDeathAnnounced = true;
            ModLog.Info($"[Morte] announcement sent: {name} is dead");
            return true;
        }

        /// <summary>
        /// Tells others this player is back up, but only if a death was announced. CreatePlayer runs
        /// on every level load, so announcing unconditionally floods the channel and - now that
        /// dead state is tracked - would actively mislead. The pairing also means a machine that
        /// never heard the death hears nothing now: correct, there is no state to clear.
        /// </summary>
        public static void AnnounceAlive()
        {
            if (!localDeathAnnounced)
                return;

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;

            string name = Main.Multiplayer.PlayerName;
            if (string.IsNullOrEmpty(name))
                return;

            Main.Multiplayer.SendEnemyDeathGlobal(AlivePrefix + name);
            localDeathAnnounced = false;
            ModLog.Info($"[Morte] announcement sent: {name} is alive again");
        }

        /// <summary>
        /// Applies a death or resurrection heard from another machine. The per-player flag lives on
        /// PlayerStatus so it disappears with the player entry - a player who leaves while down
        /// cannot leave a permanent "somebody is dead" behind.
        /// </summary>
        public static void ApplyRemoteState(string name, bool dead)
        {
            Main.Multiplayer.OtherPlayerManager.SetPlayerDead(name, dead);

            ModLog.Info($"[Morte] state updated: {name} {(dead ? "dead" : "alive")} · " +
                        $"currently dead: {Main.Multiplayer.OtherPlayerManager.CountDeadPlayers()}" +
                        $"/{Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Count}");
        }

        /// <summary>
        /// True when nobody is left standing, this player included. The game cannot answer this
        /// (one Penitent, remotes are not Penitents) and position packets are not a liveness signal,
        /// so the announcements are the only trustworthy source.
        /// </summary>
        public static bool IsEveryoneDead(bool localPlayerDead)
        {
            return Main.Multiplayer.OtherPlayerManager.AreAllPlayersDead(localPlayerDead);
        }

        /// <summary>
        /// True when this machine should suppress the world reset on death. Two conditions: a
        /// connection, and at least one other player on it. Alone, the game's own behaviour is
        /// correct and better left alone. Uses IsConnected rather than EnemySyncManager.IsHost,
        /// because IsHost returns true when nobody is connected at all - the exact opposite of what
        /// is needed here.
        /// </summary>
        public static bool ShouldSuppressWorldReset()
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return false;

            return Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Count > 0;
        }

        /// <summary>
        /// Stats line for the logs. Reports against Final (what the game's own bars divide by:
        /// PlayerHealth.BarTarget = Current / Final) rather than CurrentMax, which is Final clamped
        /// and scaled by MaxFactor and can move mid-transition. The clamped fervour value and the
        /// two MaxFactors are printed alongside so the source of any movement is visible.
        /// </summary>
        public static string StatsLine()
        {
            try
            {
                if (Core.Logic == null || Core.Logic.Penitent == null || Core.Logic.Penitent.Stats == null)
                    return "stats=unavailable";

                var stats = Core.Logic.Penitent.Stats;
                return $"vita={stats.Life.Current:F0}/{stats.Life.Final:F0} " +
                       $"fiale={stats.Flask.Current:F0}/{stats.Flask.Final:F0} " +
                       $"fervour={stats.Fervour.Current:F0}/{stats.Fervour.Final:F0} " +
                       $"[fervour with guilt penalty: max={stats.Fervour.CurrentMax:F0}] " +
                       $"(fattori: vita={stats.Life.MaxFactor:F2} fervour={stats.Fervour.MaxFactor:F2})";
            }
            catch (System.Exception e)
            {
                return $"stats=error ({e.GetType().Name})";
            }
        }

        /// <summary>
        /// Measurement line for the guilt-on-life study. Read-only: nothing here writes MaxFactor,
        /// counts guilt, or touches the bonfire. Reports CurrentMax (with the factor applied), the
        /// factors themselves, Final and Current for both Life and Fervour, and the current guilt
        /// drop count. Called at spawn (post native guilt application) and at death (before the
        /// new drop is added); the two lines show the step.
        /// </summary>
        public static string ColpaLine()
        {
            string colpe = "?";

            try
            {
                if (Core.GuiltManager != null)
                    colpe = Core.GuiltManager.GetDropsCount().ToString();
            }
            catch (System.Exception)
            {
                colpe = "error";
            }

            try
            {
                if (Core.Logic == null || Core.Logic.Penitent == null || Core.Logic.Penitent.Stats == null)
                    return $"[Colpa] stats=unavailable · colpe={colpe}";

                var stats = Core.Logic.Penitent.Stats;
                return $"[Colpa] vita: max={stats.Life.CurrentMax:F1} fattore={stats.Life.MaxFactor:F2} " +
                       $"final={stats.Life.Final:F0} current={stats.Life.Current:F0} · " +
                       $"fervour: max={stats.Fervour.CurrentMax:F1} fattore={stats.Fervour.MaxFactor:F2} · " +
                       $"colpe={colpe}";
            }
            catch (System.Exception e)
            {
                return $"[Colpa] stats=error ({e.GetType().Name}) · colpe={colpe}";
            }
        }
    }

    /// <summary>
    /// Detects the death at the exact instant, not after the player leaves the death screen.
    /// Penitent.Respawn fires only when the player presses a key on the death screen, so announcing
    /// there would tell others a player is down only once they chose to come back - while the boss
    /// would keep chasing a corpse. Hooks Penitent.OnUpdate with a Prefix rather than subscribing
    /// to OnDead: the Penitent is rebuilt on every level load, an exception in a subscriber breaks
    /// the multicast chain (other subscribers are GuiltManager and DeadScreenWidget), and the
    /// invocation order is not deterministic. Uses the game's own DeathEventLaunched for once-per-
    /// death semantics.
    /// </summary>
    [HarmonyPatch(typeof(Penitent), "OnUpdate")]
    public static class PenitentOnUpdateDeath_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Penitent __instance)
        {
            if (__instance == null || __instance.Status == null)
                return;

            if (!__instance.Status.Dead || __instance.DeathEventLaunched)
                return;

            // Close open menus before the death screen, first line so it runs before OnDead. With
            // the world running during pause it is possible to die with a menu open, and those
            // keys would skip the death screen's while(!Input.anyKey).
            DeathMenuCloser.CloseBeforeDeathScreen();

            // Measurement only. Runs before the native GuiltManager receives the death, so the
            // guilt count printed here is the one before this death.
            ModLog.Info($"{PlayerDeathSync.ColpaLine()} · dove=morte");

            // Guilt-on-life counter: the death guard above is also its once-per-death condition.
            // Counts on this machine only - Guilt is not a network feature, it must count in
            // single player too.
            LifeGuilt.RegisterDeath();

            PlayerDeathSync.AnnounceDeath();
        }
    }

    /// <summary>
    /// Announces "alive again" after each spawn. Without it the dead set only grows, so "is
    /// everybody down?" could never be answered. A lost "pdead" heals itself eventually (a later
    /// "palive" arrives); a "pdead" with no "palive" never heals.
    ///
    /// Hooks CreatePlayer, not OnPlayerSpawn: OnPlayerSpawn is an event FIELD, not a method, and a
    /// HarmonyPatch on a field name compiles but throws at PatchAll time - which aborts every
    /// subsequent patch in the assembly. CreatePlayer is the method that actually runs per spawn.
    /// </summary>
    [HarmonyPatch(typeof(SpawnManager), "CreatePlayer")]
    public static class SpawnManagerCreatePlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            PlayerDeathSync.AnnounceAlive();

            // Reapplies Life.MaxFactor to the freshly instantiated Penitent: the attribute is
            // rebuilt per Instantiate with MaxFactor = 1. Applied before the Colpa line so that
            // line's fattore= and max= values verify the write.
            LifeGuilt.ApplyOnSpawn();

            // Measurement only, one line per spawn: the native guilt is applied during OnPlayerSpawn
            // inside CreatePlayer, so this is where the curve becomes readable - one step per death.
            ModLog.Info($"{PlayerDeathSync.ColpaLine()} · dove=spawn");
        }
    }

    /// <summary>
    /// Detects the death at the moment the player chooses to come back. Respawn has one caller
    /// (DeadScreenWidget), so it fires once per death. Piece 1 of the death work: measures and
    /// reports, changes nothing - the suppression itself is a separate step so that "flag never
    /// raised" and "suppression failed" would not look the same in the log.
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

            ModLog.Info($"[Morte] player died: connesso={connected} altri giocatori={peers} " +
                        $"=> {(suppress ? "SUPPRESSING world reset" : "leaving it to the game")} · " +
                        $"{PlayerDeathSync.StatsLine()}");
        }

        /// <summary>
        /// Clears the flag even if Respawn throws. A Postfix would not run on an exception, leaving
        /// the flag raised for the session.
        /// </summary>
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            PlayerDeathSync.SuppressingWorldReset = false;
        }
    }

    /// <summary>
    /// Reports the player's stats right after the bonfire's effects. Hooks RespawnDeadEnemies (not
    /// PrieDieu.OnUse) because the postfix on OnUse runs before the coroutine body, so it would
    /// read values before the benefits. This is the measurement point where the "world yes,
    /// comfort no" boundary is verified for the replicated bonfire: sit on one machine and compare
    /// both logs.
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawner), "RespawnDeadEnemies")]
    public static class RespawnDeadEnemiesStats_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Reports the true source of this call: SuppressingWorldReset is the death flag,
            // IsReplicatingBonfire marks the replication. Using the wrong one makes the label
            // always false - and a label that is always false reads as evidence.
            string origin = EnemySyncManager.IsReplicatingBonfire
                ? " (replicating a bonfire)"
                : PlayerDeathSync.SuppressingWorldReset
                    ? " (suppressing a death's world reset)"
                    : " (local bonfire)";

            ModLog.Info($"[Falo''] stats after respawn: {PlayerDeathSync.StatsLine()}{origin}");
        }
    }
}