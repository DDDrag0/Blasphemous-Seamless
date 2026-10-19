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
        /// Whether this machine has told the others that its own player is down.
        ///
        /// It exists so that "I am alive again" can be sent only when it has something to cancel. CreatePlayer
        /// runs on every level load, so without this the alive message goes out constantly - measured at four
        /// for a single death - and on a channel whose purpose is to be believed, a message that is usually
        /// meaningless is worse than a rare one.
        /// </summary>
        private static bool localDeathAnnounced;

        /// <summary>
        /// Marks a death announcement. Fourth convention on the enemy-death channel, whose only others are
        /// "sp|" for spawn points, "brk|" for a broken vase and "falo|" for a bonfire. The server relays
        /// that channel to everyone but the sender with no dictionary and no scene filter, so a new prefix
        /// costs nothing and needs no protocol bump.
        /// </summary>
        public const string DeathPrefix = "pdead|";

        /// <summary>
        /// The counterpart, and it is not decoration: without it the set of dead players only grows, so
        /// "is everybody down?" could never be answered and the spectator state would have no end.
        /// </summary>
        public const string AlivePrefix = "palive|";

        /// <summary>True when this id is a death announcement rather than a death of an enemy.</summary>
        public static bool IsDeathId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(DeathPrefix, System.StringComparison.Ordinal);
        }

        /// <summary>True when this id is the "I am alive again" counterpart.</summary>
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
        /// True while this machine's own player is down.
        ///
        /// Read straight from the game rather than from our own announcement flag, because the two are not
        /// the same window: the flag is raised when the death is TOLD, while Status.Dead is true from the
        /// instant the player falls until the respawn finishes. Anything that has to stop treating a corpse
        /// as a participant needs the second one - most of all the ownership rule, which decided which
        /// machine simulates each enemy.
        ///
        /// The game exposes this as a public FIELD on Entity, not a property, which is why it is reached
        /// through Status.Dead.
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
        /// Whether this player is alive AND we know it - used where "not dead" and "known to be alive" are
        /// different things. A remote player we have never heard from is not evidence of life.
        /// </summary>
        public static bool IsRemotePlayerKnownAlive(string name)
        {
            var player = Main.Multiplayer.OtherPlayerManager.FindConnectedPlayerQuietly(name);
            return player != null && !player.IsDead;
        }

        /// <summary>
        /// Tells the other machines that this player has just died.
        ///
        /// The name must travel. ReceiveMessage discards the sender and ProcessQueue passes only the bytes,
        /// so without a name in the payload the receiver cannot tell who died - and "who" is the whole
        /// question, both for the boss that has to hand over and for counting how many are still standing.
        /// The name cannot contain '|' (the validator allows "_-.' " up to sixteen characters), so it cannot
        /// be mistaken for a spawn-point id on a channel where that would matter.
        ///
        /// Returns whether the announcement actually went out, because "I died" and "I told them I died"
        /// are different facts and only the second one earns a matching "I am alive again".
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
            ModLog.Info($"[Morte] annuncio inviato: {name} e' morto");
            return true;
        }

        /// <summary>
        /// Tells the other machines that this player is back on their feet - but only if we ever said they
        /// went down.
        ///
        /// CreatePlayer runs again every time the level loads rebuilds the Penitent, so this is reached far
        /// more often than a death is. Announcing every time produced four "alive" messages for one death in
        /// the measured session. Harmless while nothing kept state, but it is noise on a channel whose whole
        /// job is to be believed, and once the dead set is tracked it would be actively misleading.
        ///
        /// The pairing also means a machine that never heard the death hears nothing now - which is correct:
        /// there is no state to clear.
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
            ModLog.Info($"[Morte] annuncio inviato: {name} e' di nuovo vivo");
        }

        /// <summary>
        /// Applies a death or resurrection heard from another machine, and reports the resulting state.
        ///
        /// The per-player flag lives on PlayerStatus because that is where the identity already is, and
        /// because it has to disappear with the player: RemoveConnectedPlayer drops the whole entry, so a
        /// player who leaves while down cannot leave a permanent "somebody is dead" behind.
        /// </summary>
        public static void ApplyRemoteState(string name, bool dead)
        {
            Main.Multiplayer.OtherPlayerManager.SetPlayerDead(name, dead);

            ModLog.Info($"[Morte] stato aggiornato: {name} {(dead ? "morto" : "vivo")} · " +
                        $"morti in questo momento: {Main.Multiplayer.OtherPlayerManager.CountDeadPlayers()}" +
                        $"/{Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Count}");
        }

        /// <summary>
        /// True when nobody is left standing, this player included.
        ///
        /// This is what the spectator state will end on, and the game cannot answer it: there is one
        /// Penitent, the remote players are not Penitents, and position packets are not a liveness signal -
        /// they are only sent when the position changes, so a player standing still looks exactly like a
        /// player who is not there. The only trustworthy source is the announcements themselves.
        /// </summary>
        public static bool IsEveryoneDead(bool localPlayerDead)
        {
            return Main.Multiplayer.OtherPlayerManager.AreAllPlayersDead(localPlayerDead);
        }

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

        /// <summary>
        /// The measurement the guilt-on-life study is missing, and it MEASURES ONLY.
        ///
        /// WHY IT EXISTS. The native guilt lives on the Fervour, not on the life: the curve that moves it
        /// is an AnimationCurve inside the asset `Resources/GuiltConfig`, which is not on disk, so its
        /// numbers exist nowhere in the code - the only known datum is one measured session (1 death:
        /// Fervour max 60 -> 50). And `Life.MaxFactor` exists but the game has never exercised it, so
        /// clamp, refills and bar are deduced from the code, not observed. This line is where they get
        /// observed: it prints the maximum WITH the factor, the factor itself, Final and Current (which
        /// are what PlayerHealth divides and what it uses as the bar WIDTH - Final ignores MaxFactor), the
        /// same pair for the Fervour, and the number of guilt drops the game is holding.
        ///
        /// READ-ONLY, ON PURPOSE. Nothing here writes MaxFactor, counts guilt, or touches the bonfire: the
        /// values are read and formatted. `Current` and `CurrentMax` do run VariableAttribute.CalculateValue,
        /// which clamps the current value down to the maximum - but that is precisely what every other
        /// reader in the game does, it is idempotent, and it does not raise OnChanged (only the setter
        /// does), so nothing downstream can tell the difference.
        ///
        /// EVERY FIELD IS PUBLIC, checked before writing: MaxFactor and CurrentMax on VariableAttribute
        /// (:28, :60), Current (:41), Final on Attribute (:23), GetDropsCount on GuiltManager (:197), and
        /// Core.GuiltManager itself (Core.cs:76). No reflection and no substitutions: no field had to be
        /// dropped.
        ///
        /// The string starts with the marker so a log grep finds it, and `dove=` is appended by the two
        /// call sites, because "at a spawn" and "at a death" are different moments and must not be
        /// confused: at the death instant the game has not yet added the new drop (GuiltManager.AddDrop
        /// runs later, in OnDead), so `colpe` there is the count BEFORE this death. That is the point -
        /// the two lines show the step.
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
                colpe = "errore";
            }

            try
            {
                if (Core.Logic == null || Core.Logic.Penitent == null || Core.Logic.Penitent.Stats == null)
                    return $"[Colpa] stats=non disponibili · colpe={colpe}";

                var stats = Core.Logic.Penitent.Stats;
                return $"[Colpa] vita: max={stats.Life.CurrentMax:F1} fattore={stats.Life.MaxFactor:F2} " +
                       $"final={stats.Life.Final:F0} current={stats.Life.Current:F0} · " +
                       $"fervour: max={stats.Fervour.CurrentMax:F1} fattore={stats.Fervour.MaxFactor:F2} · " +
                       $"colpe={colpe}";
            }
            catch (System.Exception e)
            {
                return $"[Colpa] stats=errore ({e.GetType().Name}) · colpe={colpe}";
            }
        }
    }

    /// <summary>
    /// The death, the instant it happens - which is NOT where the first version watched.
    ///
    /// Respawn has exactly one caller, DeadScreenWidget, and it runs only after the player has pressed a
    /// key on the death screen. In one measured session the death state was set 151 lines before the
    /// Respawn prefix fired: that gap is however long the player leaves the screen up. Announcing a death
    /// there would tell the other machines that a player is down only once that player decided to come
    /// back - so a boss would keep chasing a corpse, and the spectator camera would have nothing to follow.
    ///
    /// So the announcement has to happen at the death itself: Penitent.OnUpdate, right after the game
    /// raises OnDead.
    ///
    /// WHY A PREFIX RATHER THAN SUBSCRIBING TO OnDead. Subscribing looks natural and is a trap:
    ///   - the Penitent is REBUILT on every level load (InitializeLevel -> SpawnPlayerOnLevelLoad ->
    ///     CreatePlayer -> Instantiate), so a subscription would last exactly one death;
    ///   - an exception in a subscriber BREAKS THE MULTICAST CHAIN, and two of the other subscribers are
    ///     GuiltManager (which drops the guilt) and DeadScreenWidget (which shows the death screen) - so a
    ///     mistake in our handler could cost the player the death screen entirely;
    ///   - the invocation order is not deterministic.
    /// A prefix has none of those problems, and the original method still runs afterwards, so the other
    /// seventeen subscribers are untouched.
    ///
    /// THE GUARD IS THE GAME'S OWN. OnUpdate runs every frame, so the handler needs to fire once per death.
    /// Doing it with a flag of ours would work until two of our features needed the same event; the game
    /// already has one - DeathEventLaunched, public, set immediately before OnDead is invoked - and using
    /// it means our "once" and the game's "once" cannot drift apart. The cost is one boolean per frame.
    ///
    /// PIECE 3a, AND IT ONLY MEASURES AND ANNOUNCES. It reports the death, and the receive side logs the
    /// announcement. Keeping the state - who is dead, whether everyone is dead - comes next, deliberately
    /// separate: with both at once, "the announcement never left" and "the state was wrong" would look
    /// identical in the log.
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
                return; // alive, or this death has already been announced by the game itself

            // MEASUREMENT ONLY - the death instant, once per death. It runs BEFORE the game's own
            // GuiltManager.OnPenitentDead (which is reached through OnDead, later in the same OnUpdate),
            // so the guilt count printed here is the one from the deaths BEFORE this one: the spawn line
            // that follows is what shows the step.
            ModLog.Info($"{PlayerDeathSync.ColpaLine()} · dove=morte");

            PlayerDeathSync.AnnounceDeath();
        }
    }

    /// <summary>
    /// The alive counterpart, and it is not optional.
    ///
    /// Without it the set of dead players only ever grows: a machine that hears "X died" and never hears
    /// anything else will believe X dead for the rest of the session, so "is everybody down?" can never be
    /// answered - and the spectator state has no way to know when to end.
    ///
    /// The asymmetry is what settles it. A LOST "pdead" heals itself, because the player eventually comes
    /// back and "palive" arrives. A "pdead" with no "palive" NEVER heals.
    ///
    /// WHY THIS PATCHES CreatePlayer AND NOT OnPlayerSpawn. The first attempt used
    /// `[HarmonyPatch(typeof(SpawnManager), "OnPlayerSpawn")]` and it was wrong in a way that cost a whole
    /// session: OnPlayerSpawn is an EVENT FIELD, not a method. Harmony looked for a method by that name,
    /// found none, threw, and PatchAll ABORTED - so every patch after this one in the assembly was never
    /// applied. The mod then produced no log lines at all and OnLateUpdate threw on every frame. A wrong
    /// name in a HarmonyPatch attribute is a string: it compiles, and it fails at runtime.
    ///
    /// CreatePlayer is the method that actually exists and actually runs on every spawn - private, in
    /// SpawnManager, called from SpawnPlayerOnLevelLoad, and it is where the new Penitent is instantiated
    /// and assigned. It is also the right moment to re-subscribe anything that lives on the instance.
    /// </summary>
    [HarmonyPatch(typeof(SpawnManager), "CreatePlayer")]
    public static class SpawnManagerCreatePlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            PlayerDeathSync.AnnounceAlive();

            // MEASUREMENT ONLY - every spawn, including the respawn after a death and every level load.
            // This is the moment the native guilt is applied (GuiltManager.OnPenitentReady sets
            // Fervour.MaxFactor during OnPlayerSpawn, inside CreatePlayer), so it is where the curve the
            // code does not contain becomes readable: one line per spawn, one step per death.
            ModLog.Info($"{PlayerDeathSync.ColpaLine()} · dove=spawn");
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
