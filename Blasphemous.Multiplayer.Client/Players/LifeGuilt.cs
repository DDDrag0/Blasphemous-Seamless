using System;
using System.Globalization;
using System.IO;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Life Guilt: a MaxFactor penalty on Life, tracked separately from the game's own Guilt
    /// (which is on Fervour). Three fixed steps (0->1.0, 1->0.8, 2->0.6, 3+->0.4), reset only at
    /// a PrieDieu, counter persisted to disk. Two suppressions via <see cref="SuppressionReason"/>:
    /// single player (CoopGate) and PE02/Demake (stock-based life). Applied from three hooks: an
    /// existing death prefix, an existing spawn postfix, and a new prefix on
    /// <c>PrieDieu.ShallowActivationLogic</c>. Everything is try/catch guarded; no Kill, Damage,
    /// SetHealth, TimeScale, or network calls.
    /// </summary>
    internal static class LifeGuilt
    {
        /// <summary>Marker on every line of this class.</summary>
        internal const string MARK = "[ColpaVita]";

        // The three fixed steps.
        private const float FACTOR_NO_GUILT = 1f;
        private const float FACTOR_ONE_DEATH = 0.8f;
        private const float FACTOR_TWO_DEATHS = 0.6f;
        private const float FACTOR_LAST_STEP = 0.4f;

        /// <summary>Last step: beyond this the factor does not drop further.</summary>
        private const int LAST_STEP = 3;

        /// <summary>
        /// Cap on the counter. Not the penalty cap (that is LAST_STEP): caps the stored integer so
        /// the file stays bounded.
        /// </summary>
        private const int MAX_DEATHS = 999;

        /// <summary>Persistence. Text format because it is one integer and stays readable if opened; a corrupted file cannot throw a deserializer.</summary>
        private const string FILE_NAME = "life-guilt.txt";
        private const string FILE_KEY = "deaths=";

        private static int _deaths;
        private static int _announcedStep;
        private static bool _loaded;
        private static string _path;

        // Coop gate edge detection: last seen state, so Tick can react when it changes.
        private static bool _gateKnown;
        private static bool _gateMultiplayer;

        /// <summary>
        /// Why the Guilt must NOT apply right now, or null if it should. Single source of truth so
        /// Factor, ApplyOnSpawn, RegisterDeath, Notify and Tick cannot disagree.
        ///
        /// Single player: the socket predicate <c>CoopGate.IsMultiplayerActive</c>.
        /// PE02/Demake: life is made of stock items derived from Life.CurrentMax, so reducing the
        /// max would change the number of orbs. Detection uses the same conditions the game uses
        /// for its own Guilt (<c>IsCurrentMode(DEMAKE)</c>, and the current penitence id "PE02").
        /// On any read failure returns null (apply the Guilt): a guard that disables it in normal
        /// co-op would be worse than the defect.
        /// </summary>
        private static string SuppressionReason()
        {
            try
            {
                if (!CoopGate.IsMultiplayerActive())
                    return "single player (no server)";

                if (Core.GameModeManager != null &&
                    Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.DEMAKE))
                {
                    return "Demake mode";
                }

                if (Core.PenitenceManager != null)
                {
                    var penitence = Core.PenitenceManager.GetCurrentPenitence();
                    if (penitence != null && string.Equals(penitence.Id, "PE02", StringComparison.Ordinal))
                        return "PE02 mode";
                }
            }
            catch (Exception)
            {
                // Doubt = apply, per the summary.
            }

            return null;
        }

        /// <summary>
        /// Max life factor from the three steps. Reads the counter (loads the file on first call).
        /// Suppressions are checked here, at the single source of the factor, so no future branch
        /// can bypass them. Floor at 0.4 is enforced here because set_MaxFactor calls
        /// CalculateValue which clamps current life to the new max, and life &lt;= 0 kills.
        /// </summary>
        internal static float Factor()
        {
            if (SuppressionReason() != null)
                return FACTOR_NO_GUILT;

            int deaths = Deaths;

            if (deaths <= 0)
                return FACTOR_NO_GUILT;

            if (deaths == 1)
                return FACTOR_ONE_DEATH;

            if (deaths == 2)
                return FACTOR_TWO_DEATHS;

            return FACTOR_LAST_STEP;
        }

        /// <summary>Deaths counted between bonfires.</summary>
        internal static int Deaths
        {
            get
            {
                EnsureLoaded();
                return _deaths;
            }
        }

        /// <summary>
        /// One more death. Called from the existing Penitent.OnUpdate prefix, which already fires
        /// once per death (Status.Dead and !DeathEventLaunched). Saves immediately so closing the
        /// game is not a way out. Suppressions are checked first: where the Guilt does not apply,
        /// a death is not a death for the Guilt, so neither the counter nor the factor move.
        /// </summary>
        internal static void RegisterDeath()
        {
            try
            {
                string reason = SuppressionReason();
                if (reason != null)
                {
                    ModLog.Info(MARK + " not applied: " + reason +
                                " · death NOT counted, life untouched, no notification · " +
                                "the counter resumes when Guilt is active again " +
                                "(a penalty does not accumulate where it does not apply)");
                    return;
                }

                EnsureLoaded();

                if (_deaths < MAX_DEATHS)
                    _deaths++;

                float factor = Factor();

                // Concatenation keeps the marker as a single literal in the DLL (string interpolation
                // would compile to string.Format and lose it to marker-check).
                ModLog.Info(MARK + " deaths=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                            " factor=" + Text(factor) +
                            " · steps are fixed (1 => 0.8, 2 => 0.6, 3+ => 0.4, the last one capped in code)");

                ApplyNow("death");
                Save("death");

                // Notify once per step, not per death.
                int step = StepOf(_deaths);
                if (step > _announcedStep)
                {
                    _announcedStep = step;
                    Notify(factor);
                }
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " deaths: counter NOT updated (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· the game continues, the Guilt stays as before");
            }
        }

        /// <summary>
        /// Reapplies Life.MaxFactor on the Penitent just created. Required, not an optimization:
        /// MaxFactor lives on the attribute instance, which is rebuilt on every Instantiate with a
        /// fresh 1.0. Also idempotent, so writing the same value twice is harmless. When the
        /// suppression is active the value written is 1.0 (no reduction).
        /// </summary>
        internal static void ApplyOnSpawn()
        {
            try
            {
                EnsureLoaded();

                string reason = SuppressionReason();
                float factor = Factor();
                var stats = PlayerStats();

                if (stats == null)
                {
                    ModLog.Warn(MARK + " applied on each spawn · stats=unavailable (no Penitent): " +
                                "NO write, counter=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                                " factor=" + Text(factor) +
                                (reason == null ? string.Empty : " · BUT not applied: " + reason));
                    return;
                }

                stats.Life.MaxFactor = factor;

                if (reason != null)
                {
                    ModLog.Info(MARK + " not applied: " + reason + " · wrote Life.MaxFactor=" +
                                Text(factor) + " (no reduction, no notification) · life: max=" +
                                F1(stats.Life.CurrentMax) + " final=" + F0(stats.Life.Final) +
                                " current=" + F0(stats.Life.Current) +
                                " · counter=" + _deaths.ToString(CultureInfo.InvariantCulture));
                    return;
                }

                ModLog.Info(MARK + " applied on each spawn · contatore=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                            " factor=" + Text(factor) +
                            " · life: max=" + F1(stats.Life.CurrentMax) +
                            " final=" + F0(stats.Life.Final) +
                            " current=" + F0(stats.Life.Current) +
                            " (final ignores the factor: it is the bar width)");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " applied on each spawn NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· the game continues without the penalty on this spawn");
            }
        }

        /// <summary>
        /// Reset counter and factor to 1. Called from the new prefix on
        /// <c>PrieDieu.ShallowActivationLogic</c>. Prefix order matters: the game's
        /// <c>Life.SetToCurrentMax()</c> runs after this method, so the fill lands at the full max;
        /// a postfix would leave the player half-empty. Not replicated by the bonfire replication,
        /// which calls UsePrieDieu instead, so the reset is local to the sitter.
        /// </summary>
        internal static void ResetAtBonfire()
        {
            try
            {
                EnsureLoaded();

                int before = _deaths;
                _deaths = 0;
                _announcedStep = 0;

                var stats = PlayerStats();
                if (stats != null)
                    stats.Life.MaxFactor = 1f;

                Save("bonfire");

                ModLog.Info(MARK + " reset at bonfire · counter 0 · before=" + before.ToString(CultureInfo.InvariantCulture) +
                            " · factor=1.0 " +
                            "· the game's Life.SetToCurrentMax() now fills to the full max " +
                            "· native Guilt (Fervour) is untouched: it stays as it was");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " reset at bonfire NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· the player stands up with the previous Guilt, nothing broken");
            }
        }

        /// <summary>
        /// Per-frame coop edge detection. Reads the coop gate and, on change, re-applies or disables.
        /// Needed because a player can disconnect without respawning (debuff must turn off
        /// immediately) or connect mid-session (Guilt must return without waiting for a spawn).
        /// Cost: one boolean read and one comparison per frame; no writes unless the edge fires.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool multiplayer = CoopGate.IsMultiplayerActive();

                if (_gateKnown && multiplayer == _gateMultiplayer)
                    return;

                bool first = !_gateKnown;
                _gateKnown = true;
                _gateMultiplayer = multiplayer;

                string reason = SuppressionReason();

                if (reason != null)
                {
                    var stats = PlayerStats();
                    if (stats != null)
                        stats.Life.MaxFactor = 1f;

                    ModLog.Info(MARK + " not applied: " + reason +
                                " · debuff OFF, Life.MaxFactor=1.0 · the player can heal" +
                                (stats == null ? " · (no Penitent: no write)" : string.Empty) +
                                (first ? " · first tick" : " · edge: connection lost"));
                    return;
                }

                ApplyNow(first ? "first tick with server" : "edge: connection to server");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " coop edge NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· the Guilt stays as it was, nothing broken");
            }
        }

        /// <summary>
        /// New game resets the counter. The file is one (not per slot), so without this a new game
        /// would inherit the previous game's deaths. Called from Multiplayer.OnNewGame.
        /// </summary>
        internal static void OnNewGame()
        {
            try
            {
                EnsureLoaded();

                int before = _deaths;
                _deaths = 0;
                _announcedStep = 0;
                Save("new game");

                ModLog.Info(MARK + " new game · counter 0 · before=" + before.ToString(CultureInfo.InvariantCulture) + " · factor=1.0");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " new game: counter NOT reset (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        /// <summary>
        /// Notification text is English ASCII, like the other mod notifications: a plain string,
        /// not a localization key. The percentage is the actual factor, not a hard-coded number.
        /// </summary>
        private static void Notify(float factor)
        {
            try
            {
                string reason = SuppressionReason();
                if (reason != null)
                {
                    ModLog.Info(MARK + " not applied: " + reason + " · notification NOT shown " +
                                "(life was not reduced: the text would lie)");
                    return;
                }

                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                {
                    ModLog.Info(MARK + " notification NOT shown (NotificationManager missing): the penalty " +
                                "is still applied, factor=" + Text(factor));
                    return;
                }

                int percent = (int)Math.Round(factor * 100f);
                string text = "YOUR MAX LIFE IS REDUCED TO " + percent.ToString(CultureInfo.InvariantCulture) +
                              "% - REST AT A PRIE-DIEU";

                Main.Multiplayer.NotificationManager.DisplayNotification(text);

                ModLog.Info(MARK + " notification \"" + text + "\" · ONCE per step " +
                            "(not per spawn, not per death)");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " notification NOT shown (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· the penalty stays applied anyway");
            }
        }

        /// <summary>
        /// First read, once. Missing, unreadable, empty or corrupted file = counter 0, no
        /// exception. Negative values clamp to 0, oversized values to MAX_DEATHS.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;

            int value = 0;
            string note = "file missing: counter 0";

            try
            {
                string path = PathOfFile();

                if (File.Exists(path))
                {
                    note = "file present but no readable number: counter 0";

                    foreach (string raw in File.ReadAllLines(path))
                    {
                        if (raw == null)
                            continue;

                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#')
                            continue;

                        if (line.StartsWith(FILE_KEY, StringComparison.Ordinal))
                            line = line.Substring(FILE_KEY.Length).Trim();

                        int parsed;
                        if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            continue;

                        if (parsed < 0)
                            parsed = 0;
                        if (parsed > MAX_DEATHS)
                            parsed = MAX_DEATHS;

                        value = parsed;
                        note = "read";
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                value = 0;
                note = "read FAILED (" + e.GetType().Name + "): counter 0";
            }

            _deaths = value;
            _announcedStep = StepOf(_deaths);

            ModLog.Info(MARK + " loaded · morti=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                        " factor=" + Text(FactorOfCount(_deaths)) + " · " + note +
                        " · " + SafePath());
        }

        /// <summary>
        /// Writes the counter. Small file, written only when the number changes. Text format with
        /// three comment lines and one data line so a human opening it understands what is there.
        /// </summary>
        private static void Save(string why)
        {
            try
            {
                string path = PathOfFile();
                string dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string text =
                    "# Blasphemous COOP - life guilt" + Environment.NewLine +
                    "# Deaths made between one Prie-Dieu and the next. Reset only at a Prie-Dieu." + Environment.NewLine +
                    "# Missing or unreadable number = 0. Deleting this file forgives the guilt." + Environment.NewLine +
                    FILE_KEY + _deaths.ToString(CultureInfo.InvariantCulture) + Environment.NewLine;

                File.WriteAllText(path, text);

                ModLog.Info(MARK + " saved · contatore=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                            " fattore=" + Text(FactorOfCount(_deaths)) +
                            " · " + path + " · (" + why + ")");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " save NO (" + e.GetType().Name + ": " + e.Message + ") · the counter " +
                             "(" + _deaths.ToString(CultureInfo.InvariantCulture) + ") stays in memory: " +
                             "the penalty works but does not survive a close");
            }
        }

        /// <summary>File path: Modding/data/Multiplayer/life-guilt.txt, the same folder as other mod data files.</summary>
        private static string PathOfFile()
        {
            if (_path != null)
                return _path;

            string root = null;

            try
            {
                if (Main.Multiplayer != null && Main.Multiplayer.FileHandler != null)
                    root = Main.Multiplayer.FileHandler.ModdingFolder;
            }
            catch (Exception)
            {
            }

            if (string.IsNullOrEmpty(root))
                root = Path.GetFullPath("Modding" + Path.DirectorySeparatorChar);

            _path = Path.Combine(Path.Combine(Path.Combine(root, "data"), "Multiplayer"), FILE_NAME);
            return _path;
        }

        /// <summary>Path for logs; never throws.</summary>
        private static string SafePath()
        {
            try
            {
                return PathOfFile();
            }
            catch (Exception)
            {
                return "(path unavailable)";
            }
        }

        /// <summary>Reapplies the factor to the in-scene Penitent when the counter changes. Same write as ApplyOnSpawn; separate for logging.</summary>
        private static void ApplyNow(string why)
        {
            try
            {
                var stats = PlayerStats();
                if (stats == null)
                    return;

                stats.Life.MaxFactor = Factor();

                ModLog.Info(MARK + " factor reapplied immediately (" + why + ") · life max=" +
                            F1(stats.Life.CurrentMax) + " final=" + F0(stats.Life.Final) +
                            " current=" + F0(stats.Life.Current));
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " reapply (" + why + ") NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· it will be redone on the next spawn");
            }
        }

        /// <summary>Penitent stats, or null. Never throws.</summary>
        private static Gameplay.GameControllers.Entities.EntityStats PlayerStats()
        {
            try
            {
                if (Core.Logic == null || Core.Logic.Penitent == null)
                    return null;

                return Core.Logic.Penitent.Stats;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Step of a death count: 0..3.</summary>
        private static int StepOf(int deaths)
        {
            if (deaths <= 0)
                return 0;
            if (deaths >= LAST_STEP)
                return LAST_STEP;
            return deaths;
        }

        /// <summary>Factor of a count without touching state (used by load logs).</summary>
        private static float FactorOfCount(int deaths)
        {
            if (deaths <= 0)
                return FACTOR_NO_GUILT;
            if (deaths == 1)
                return FACTOR_ONE_DEATH;
            if (deaths == 2)
                return FACTOR_TWO_DEATHS;
            return FACTOR_LAST_STEP;
        }

        /// <summary>Factor in a stable, searchable form (0.8, not 0,8).</summary>
        private static string Text(float factor)
        {
            return factor.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>One decimal, dot not comma (log is a measurement, not locale text).</summary>
        private static string F1(float value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>Integer form (88, not 88.0).</summary>
        private static string F0(float value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Prefix on PrieDieu.ShallowActivationLogic to reset the counter before the game's heal. A
    /// prefix because the game's Life.SetToCurrentMax() runs after this method: a postfix would
    /// leave the player half-empty. Method is private but Harmony patches by name; the replica
    /// bonfire uses UsePrieDieu instead, so the reset is local to the sitter.
    /// </summary>
    [HarmonyPatch(typeof(PrieDieu), "ShallowActivationLogic")]
    public static class PrieDieuShallowActivationLogic_LifeGuilt_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            LifeGuilt.ResetAtBonfire();
        }
    }
}