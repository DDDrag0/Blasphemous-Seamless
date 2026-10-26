using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Damage;
using System;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Log-only marker of the damage applied to a Penitent by its hurtbox. Uses the two public
    /// static delegates on <c>PenitentDamageArea</c> (<c>OnHitGlobal</c> before the gates,
    /// <c>OnDamagedGlobal</c> after applying the damage) - no Harmony patch, targets stay 269.
    ///
    /// Closes two gaps: the enemy channel only logs life for enemy attackers (an effect attacker
    /// is discarded by <c>EnemyHitPeer.AttackerOf</c> and applied locally with no marker), and the
    /// audio event that plays on damage does not tell which of the two Penitents lost life. The
    /// marker is attached to the hurtbox that actually applies the damage, so it can name which
    /// side.
    /// </summary>
    internal static class PenitentLifeMarker
    {
        /// <summary>Distinct from [DannoPeer] and [Danno]: three separate measurements.</summary>
        internal const string Marker = "[DannoVita]";

        /// <summary>Subscription is per-process: two subscriptions would log twice per hit.</summary>
        private static bool _armed;

        /// <summary>Life read in <c>OnHitGlobal</c>, i.e. before the hit is applied.</summary>
        private static float _before;

        /// <summary>
        /// True when <c>_before</c> belongs to the current hit. <c>OnHitGlobal</c> also fires for
        /// hits stopped by a gate; without this flag the next line would print a stale "before".
        /// </summary>
        private static bool _beforeValid;

        /// <summary>
        /// Subscribes to the Penitent hurtbox delegates. Called from Multiplayer.OnInitialize.
        /// Never throws: on failure the marker stays off and the game is unaffected.
        /// </summary>
        internal static void Arm()
        {
            if (_armed)
                return;

            try
            {
                PenitentDamageArea.OnHitGlobal += OnHitGlobal;
                PenitentDamageArea.OnDamagedGlobal += OnDamagedGlobal;
                _armed = true;
            }
            catch (Exception e)
            {
                _armed = false;
                Fail("iscrizione", e);
            }
        }

        /// <summary>Unsubscribes. Called from Multiplayer.OnDispose.</summary>
        internal static void Disarm()
        {
            if (!_armed)
                return;

            try
            {
                PenitentDamageArea.OnHitGlobal -= OnHitGlobal;
                PenitentDamageArea.OnDamagedGlobal -= OnDamagedGlobal;
            }
            catch (Exception e)
            {
                Fail("disiscrizione", e);
            }

            _armed = false;
            _beforeValid = false;
        }

        /// <summary>
        /// The hit reached the hurtbox: read life BEFORE. This is the only point where "before" is
        /// still "before" - RaiseDamageEvent subtracts life right after.
        /// </summary>
        private static void OnHitGlobal(Penitent penitent, Hit hit)
        {
            try
            {
                _beforeValid = false;
                if (penitent == null || penitent.Stats == null || penitent.Stats.Life == null)
                    return;

                _before = penitent.Stats.Life.Current;
                _beforeValid = true;
            }
            catch (Exception e)
            {
                Fail("reading life before", e);
            }
        }

        /// <summary>
        /// The hit was APPLIED: log the trio and the rest. One line, never throws. OnDamagedGlobal
        /// means "life was subtracted", not "the hit passed a gate".
        /// </summary>
        private static void OnDamagedGlobal(Penitent damaged, Hit hit)
        {
            try
            {
                if (damaged == null || damaged.Stats == null || damaged.Stats.Life == null)
                    return;

                float after = damaged.Stats.Life.Current;
                if (!_beforeValid)
                {
                    // No "before" for this hit: prefer a "?" to an invented value.
                    ModLog.Info($"{Marker} applicato · vitaPrima=? · vitaDopo={F0(after)} · perso=? · " +
                                $"da={Name(hit)} · classe={ClassOf(hit)} · danno={F1(hit)} · " +
                                $"tipo={TypeOf(hit)} · elem={ElementOf(hit)} · su={Side(damaged)}");
                    return;
                }

                _beforeValid = false;

                ModLog.Info($"{Marker} applicato · vitaPrima={F0(_before)} · vitaDopo={F0(after)} · " +
                            $"perso={F0(Math.Max(0f, _before - after))} · da={Name(hit)} · " +
                            $"classe={ClassOf(hit)} · danno={F1(hit)} · tipo={TypeOf(hit)} · " +
                            $"elem={ElementOf(hit)} · su={Side(damaged)}");
            }
            catch (Exception e)
            {
                Fail("riga", e);
            }
        }

        /// <summary>
        /// Attacker name without Unity's instance suffix: <c>CorpsesShockwave(Clone)</c> and
        /// <c>CorpsesShockwave(Clone) (1)</c> are the same prefab.
        /// </summary>
        private static string Name(Hit hit)
        {
            try
            {
                if (hit.AttackingEntity == null)
                    return "(none)";

                string name = hit.AttackingEntity.name;
                if (string.IsNullOrEmpty(name))
                    return "(no name)";

                return name.Replace("(Clone)", string.Empty).Trim();
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// <c>enemy</c> / <c>effect</c> / <c>unknown</c> - the same test as
        /// EnemyHitPeer.AttackerOf: if it says "effect", the enemy channel discarded this hit and
        /// this line is the only measure of its damage.
        /// </summary>
        private static string ClassOf(Hit hit)
        {
            try
            {
                if (hit.AttackingEntity == null)
                    return "unknown";

                return hit.AttackingEntity.GetComponentInParent<Enemy>() != null ? "enemy" : "effect";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// <c>locale</c> if the hurtbox belongs to this machine's Penitent, otherwise
        /// <c>altro(nome)</c>. Read from the object, not inferred from distance.
        /// </summary>
        private static string Side(Penitent damaged)
        {
            try
            {
                Penitent local = Core.Logic == null ? null : Core.Logic.Penitent;
                if (local == null)
                    return damaged != null && damaged.name != null ? "altro(" + damaged.name + ")" : "?";

                return ReferenceEquals(damaged, local) ? "locale" : "altro(" + damaged.name + ")";
            }
            catch
            {
                return "?";
            }
        }

        private static string TypeOf(Hit hit)
        {
            try
            {
                return hit.DamageType.ToString();
            }
            catch
            {
                return "?";
            }
        }

        private static string ElementOf(Hit hit)
        {
            try
            {
                return hit.DamageElement.ToString();
            }
            catch
            {
                return "?";
            }
        }

        private static string F1(Hit hit)
        {
            try
            {
                return hit.DamageAmount.ToString("F1", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// Numbers on one log line with invariant decimal separator: two machines' logs must render
        /// the same numbers the same way.
        /// </summary>
        private static string F0(float value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        private static void Fail(string what, Exception e)
        {
            try
            {
                ModLog.Error($"{Marker} {what} fallita ({e.GetType().Name}): {e.Message} - " +
                             "the marker stays silent, the game does not");
            }
            catch
            {
                // A marker that throws while reporting that it threw must not break anything.
            }
        }
    }
}