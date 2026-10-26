using Blasphemous.Multiplayer.Client.PvP.Models;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbLadder;
using Gameplay.GameControllers.AnimationBehaviours.Player.Crouch;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dash;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using Gameplay.GameControllers.AnimationBehaviours.Player.Hurt;
using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using Gameplay.GameControllers.AnimationBehaviours.Player.RangeAttack;
using Gameplay.GameControllers.AnimationBehaviours.Player.Run;
using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using Gameplay.GameControllers.Penitent.Animator;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    // Default attack
    [HarmonyPatch(typeof(DefaultSwordSlashBehaviour), "OnStateEnter")]
    class DefaultSwordSlashBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Slash";
    }
    [HarmonyPatch(typeof(DefaultSwordSlashBehaviour), "OnStateExit")]
    class DefaultSwordSlashBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Slash";
    }
    // Air attack
    [HarmonyPatch(typeof(AirAttackBehaviour), "OnStateEnter")]
    class AirAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.SidewaysAir);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(AirAttackBehaviour), "OnStateUpdate")]
    class AirAttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(AirAttackBehaviour), "OnStateExit")]
    class AirAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Air upward
    [HarmonyPatch(typeof(AirUpwardAttackBehaviour), "OnStateEnter")]
    class AirUpwardAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.UpwardsAir);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(AirUpwardAttackBehaviour), "OnStateUpdate")]
    class AirUpwardAttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(AirUpwardAttackBehaviour), "OnStateExit")]
    class AirUpwardAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Main attack
    [HarmonyPatch(typeof(AttackBehaviour), "OnStateEnter")]
    class AttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.SidewaysGrounded);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(AttackBehaviour), "OnStateUpdate")]
    class AttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Cancel combo
    [HarmonyPatch(typeof(CancelComboBehaviour), "OnStateEnter")]
    class CancelComboBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(CancelComboBehaviour), "OnStateUpdate")]
    class CancelComboBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Charged attack
    [HarmonyPatch(typeof(ChargedAttackBehaviour), "OnStateEnter")]
    class ChargedAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Blasphemous.ModdingAPI.ModLog.Info("[Invia] Carica");
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.SidewaysGrounded);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(ChargedAttackBehaviour), "OnStateUpdate")]
    class ChargedAttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ChargedAttackBehaviour), "OnStateExit")]
    class ChargedAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Charged attack effect
    [HarmonyPatch(typeof(ChargedAttackEffectBehaviour), "OnStateEnter")]
    class ChargedAttackEffectBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Blasphemous.ModdingAPI.ModLog.Info("[Invia] Carica (effetto)");
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.UpwardsGrounded);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(ChargedAttackEffectBehaviour), "OnStateUpdate")]
    class ChargedAttackEffectBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Charging attack
    [HarmonyPatch(typeof(ChargingAttackBehaviour), "OnStateEnter")]
    class ChargingAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Combo finisher starter
    [HarmonyPatch(typeof(FinishingComboStarterBehaviour), "OnStateEnter")]
    class FinishingComboStarterBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FinishingComboStarterBehaviour), "OnStateUpdate")]
    class FinishingComboStarterBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FinishingComboStarterBehaviour), "OnStateExit")]
    class FinishingComboStarterBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Combo finisher up
    [HarmonyPatch(typeof(FinishingComboUpBehaviour), "OnStateEnter")]
    class FinishingComboUpBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FinishingComboUpBehaviour), "OnStateUpdate")]
    class FinishingComboUpBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FinishingComboUpBehaviour), "OnStateExit")]
    class FinishingComboUpBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Combo finisher down
    [HarmonyPatch(typeof(FinishingComboDownBehaviour), "OnStateEnter")]
    class FinishingComboDownBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FinishingComboDownBehaviour), "OnStateExit")]
    class FinishingComboDownBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ground upward
    [HarmonyPatch(typeof(GroundUpwardAttackBehaviour), "OnStateEnter")]
    class GroundUpwardAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.UpwardsGrounded);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(GroundUpwardAttackBehaviour), "OnStateUpdate")]
    class GroundUpwardAttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundUpwardAttackBehaviour), "OnStateExit")]
    class GroundUpwardAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Guard to idle
    [HarmonyPatch(typeof(GuardToIdleBehaviour), "OnStateEnter")]
    class GuardToIdleBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GuardToIdleBehaviour), "OnStateExit")]
    class GuardToIdleBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Lunge attack
    [HarmonyPatch(typeof(LungeAttackBehaviour), "OnStateEnter")]
    class LungeAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Blasphemous.ModdingAPI.ModLog.Info("[Invia] Lunge");
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.SidewaysGrounded);

                // Lunge also drives the ghost trail; sends two bytes rather than widening the payload.
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.TrailOn);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(LungeAttackBehaviour), "OnStateExit")]
    class LungeAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.TrailOff);
                return true;
            }

            // Return false for non-body: this is a one-liner whose return VALUE matters, and returning
            // true unconditionally would let the sword animator run the game's real OnStateExit and NRE.
            return false;
        }
    }

    // Trail on/off from the game's own decisions, at the three places the penitent changes it.
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.Dash), "OnCastStart")]
    class DashGhostTrailEnter_Patch
    {
        public static void Postfix()
        {
            Main.Multiplayer.NetworkManager.SendEffect(EffectType.TrailOn);
        }
    }
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.Dash), "OnCastEnd")]
    class DashGhostTrailExit_Patch
    {
        public static void Postfix()
        {
            Main.Multiplayer.NetworkManager.SendEffect(EffectType.TrailOff);
        }
    }
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.VerticalAttack), "ShowGhostTrail")]
    class VerticalAttackGhostTrail_Patch
    {
        public static void Postfix(bool show)
        {
            Main.Multiplayer.NetworkManager.SendEffect(show ? EffectType.TrailOn : EffectType.TrailOff);
        }
    }
    [HarmonyPatch(typeof(Framework.Inventory.IncreaseSpeedSwordHeartEffect), "ApplyGhostingEffect")]
    class IncreaseSpeedSwordHeartEffectGhosting_Patch
    {
        // Parameter name MUST match the game's (`apply`): Harmony binds postfix args by name, and a
        // mismatch throws during patch application, taking the whole mod down.
        public static void Postfix(bool apply)
        {
            Main.Multiplayer.NetworkManager.SendEffect(apply ? EffectType.TrailOn : EffectType.TrailOff);
        }
    }

    /// <summary>
    /// Shared sender for the six one-shot slash sounds. Two sounds of the eight do not go through
    /// here: footsteps (rate fuse) and dash (start/stop pair). These six are pure one-shots.
    /// </summary>
    static class LocalSoundSender
    {
        private static int sentCount;

        /// <summary>
        /// Sends the just-played slash sound with the penitent position. <paramref name="what"/> is
        /// for the log only - the payload is a byte.
        /// </summary>
        public static void SendLocalSound(EffectType effect, string what)
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            // Sender-side proof: the receiver's line is written on the OTHER machine, so without this
            // "postfix never ran" and "packet never arrived" are indistinguishable.
            sentCount++;
            if (sentCount == 1 || sentCount % 10 == 0)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[Sound TX] {what} n.{sentCount} ({at.x:F1},{at.y:F1})");
            }

            Main.Multiplayer.NetworkManager.SendEffect(effect, at);
        }
    }
    // Parry riposte
    [HarmonyPatch(typeof(ParryRepostBehaviour), "OnStateEnter")]
    class ParryRepostBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ParryRepostBehaviour), "OnStateExit")]
    class ParryRepostBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Parry success
    [HarmonyPatch(typeof(ParrySuccessBehaviour), "OnStateEnter")]
    class ParrySuccessBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ParrySuccessBehaviour), "OnStateUpdate")]
    class ParrySuccessBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ParrySuccessBehaviour), "OnStateExit")]
    class ParrySuccessBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Start charging attack
    [HarmonyPatch(typeof(StartChargingAttackBehaviour), "OnStateEnter")]
    class StartChargingAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Footsteps: hooked on PenitentMoveAnimations.PlayFootStep, the only call site, so 1:1 with the
    // sound the game plays. Zero-arg signature means no parameter name to mismatch. Sends only when
    // grounded (ladder/idle clips can carry the same event). Rate fuse: cap on frequency, not tuning.
    [HarmonyPatch(typeof(PenitentMoveAnimations), "PlayFootStep")]
    class PenitentFootStep_Patch
    {
        /// <summary>Frequency cap toward the peer. Fuse, not tuning.</summary>
        private const float MIN_INTERVAL = 0.08f;

        private static float lastSent = float.NegativeInfinity;
        private static int sentCount;

        public static void Postfix()
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            // Grounded only: ladder and idle clips can carry the same event.
            if (Framework.Managers.Core.Logic.Penitent.Status == null ||
                !Framework.Managers.Core.Logic.Penitent.Status.IsGrounded)
                return;

            // unscaledTime: FMOD sound does not stop on pause, use the same clock.
            if (UnityEngine.Time.unscaledTime - lastSent < MIN_INTERVAL)
                return;

            lastSent = UnityEngine.Time.unscaledTime;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            // Sender-side proof; footsteps fire fast, so log the first and every tenth.
            sentCount++;
            if (sentCount == 1 || sentCount % 10 == 0)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[Sound TX] FootStep n.{sentCount} ({at.x:F1},{at.y:F1})");
            }

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.FootStep, at);
        }
    }

    // Attack swing sounds from PenitentAttackAnimations.Play*: no parameter, so no name to mismatch.
    // Six catalog keys, one-shot; only the byte travels, the receiver resolves the key locally.
    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayBasicAttack1")]
    class AttackSound1_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.Attack1, "Attack1");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayBasicAttack2")]
    class AttackSound2_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.Attack2, "Attack2");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayAirAttack1")]
    class AirAttackSound1_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.AirAttack1, "AirAttack1");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayAirAttack2")]
    class AirAttackSound2_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.AirAttack2, "AirAttack2");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayHeavyAttack")]
    class HeavyAttackSound_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.HeavyAttack, "HeavyAttack");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayReleaseChargeAttack")]
    class ChargedReleaseSound_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.ChargedRelease, "ChargedRelease");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayComboHit")]
    class ComboHitSound_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.ComboHit, "ComboHit");
        }
    }

    [HarmonyPatch(typeof(PenitentMoveAnimations), "PlayIdleSword")]
    class IdleSwordSound_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.IdleSword, "IdleSword");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayVerticalAttackStart")]
    class VerticalStartSound_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.VerticalStart, "VerticalStart");
        }
    }

    [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayVerticalAttackFalling")]
    class VerticalFallingSound_Patch
    {
        public static void Postfix()
        {
            LocalSoundSender.SendLocalSound(EffectType.VerticalFalling, "VerticalFalling");
        }
    }

    // Dash sound: a latched catalog event (start/stop pair). Hooked on Dash.OnCastStart/OnCastEnd,
    // not DashBehaviour.OnStateEnter/Exit - those already carry Prefixes and a Postfix there runs
    // even for non-body animators. Zero-arg postfix, no parameter name to mismatch. Air dash has no
    // sound in the game (verified in IL), so it is not invented here.
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.Dash), "OnCastStart")]
    class DashSoundStart_Patch
    {
        public static void Postfix()
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            Blasphemous.ModdingAPI.ModLog.Info($"[Sound TX] DashSoundStart ({at.x:F1},{at.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.DashSoundStart, at);
        }
    }

    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.Dash), "OnCastEnd")]
    class DashSoundStop_Patch
    {
        // No null check on Penitent on purpose: the stop must be delivered even after death or a
        // scene change; the receiver handles "nothing to stop" idempotently.
        public static void Postfix()
        {
            Blasphemous.ModdingAPI.ModLog.Info("[Sound TX] DashSoundStop");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.DashSoundStop);
        }
    }

    /// <summary>
    /// Shared sender for the parry sounds. `PenitentParryHit` is a latched catalog event; one stop
    /// must be sent per start, so a guard prevents orphan stops when the parry fails
    /// (`StopParryFx` is called even on failure).
    /// </summary>
    static class ParrySoundSender
    {
        private static bool armed;

        /// <summary>Sends the clang with the penitent position.</summary>
        public static void SendParryHit()
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            Blasphemous.ModdingAPI.ModLog.Info($"[Sound TX] ParryHit ({at.x:F1},{at.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.ParryHit, at);
            armed = true;
        }

        /// <summary>Sends the clang stop only when a start was sent.</summary>
        public static void SendParryHitStop()
        {
            if (!armed)
                return;

            armed = false;
            Blasphemous.ModdingAPI.ModLog.Info("[Sound TX] ParryHitStop");
            Main.Multiplayer.NetworkManager.SendEffect(EffectType.ParryHitStop);
        }
    }

    /// <summary>Parry clang start; postfix, zero args.</summary>
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Audio.PenitentAudio), "PlayParryHit")]
    class PenitentParryHitSound_Patch
    {
        public static void Postfix()
        {
            ParrySoundSender.SendParryHit();
        }
    }

    /// <summary>
    /// Parry clang stop. Called both at the end of the parry and as the first line of PlayParryHit;
    /// the armed guard discards stops with no active start.
    /// </summary>
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Audio.PenitentAudio), "StopParryFx")]
    class PenitentParryHitStop_Patch
    {
        public static void Postfix()
        {
            ParrySoundSender.SendParryHitStop();
        }
    }

    /// <summary>
    /// Guard sound. Hooked on Parry.OnCastStart (which always fires on a guard attempt, hit or
    /// not), not on PlayStartParry - that method has no C# callers. The path comes from the local
    /// `ParryGuardFx` field, which is what the game just played; the byte sent carries the effect,
    /// the path is logged only. Both `ParryStart` and `ParryStartCatalog` are sent so the listener
    /// resolves which key actually plays on the receiver.
    /// </summary>
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.Parry), "OnCastStart")]
    class ParryGuardSound_Patch
    {
        public static void Postfix(Gameplay.GameControllers.Penitent.Abilities.Parry __instance)
        {
            if (__instance == null || Framework.Managers.Core.Logic.Penitent == null)
                return;

            string path = __instance.ParryGuardFx;
            if (string.IsNullOrEmpty(path))
            {
                Blasphemous.ModdingAPI.ModLog.Warn("[Sound TX] ParryStart: `Parry.ParryGuardFx` is empty " +
                                                   "on this machine - the peer's guard will not sound");
                return;
            }

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            Blasphemous.ModdingAPI.ModLog.Info($"[Sound TX] ParryStart ({at.x:F1},{at.y:F1}) path={path}");
            Main.Multiplayer.NetworkManager.SendEffect(EffectType.ParryStart, at);

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.ParryStartCatalog, at);
        }
    }

    // Slide dust: hooked at the ability's OnCastStart (where the game performs the slide), postfix,
    // so the ability is untouched. Receiver must not call OnCastStart (would cast the ability).
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.GuardSlide), "OnCastStart")]
    class GuardSlideEffect_Patch
    {
        public static void Postfix()
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            // Sender-side proof: the [Effect] line is written on the receiver only.
            Blasphemous.ModdingAPI.ModLog.Info($"[Effect TX] SlideDust ({at.x:F1},{at.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.SlideDust, at);
        }
    }

    // Ground impact at plunge completion (TriggerVerticalAttack). PrayerUseAttack calls this with
    // false - the argument check skips that. Receiver must not call this (would re-deal damage).
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.VerticalAttack), "TriggerVerticalAttack")]
    class VerticalAttackLandingEffect_Patch
    {
        private const string Vertical1 = "VERTICAL_1";
        private const string Vertical2 = "VERTICAL_2";
        private const string Vertical3 = "VERTICAL_3";

        // BeamRequiredHeightReached is private; reflection with caching, degrades to plain impact on miss.
        private static readonly System.Reflection.PropertyInfo BeamReached =
            typeof(Gameplay.GameControllers.Penitent.Abilities.VerticalAttack).GetProperty(
                "BeamRequiredHeightReached",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public static void Postfix(
            Gameplay.GameControllers.Penitent.Abilities.VerticalAttack __instance,
            bool instantiateExplosion)
        {
            // PrayerUseAttack calls this with false; sending here would invent an impact.
            if (!instantiateExplosion)
                return;

            if (__instance == null || Framework.Managers.Core.Logic.Penitent == null)
                return;

            string skill = __instance.LastUnlockedSkillId;
            EffectType effect;

            if (skill == Vertical1)
                effect = EffectType.LandingImpact1;
            else if (skill == Vertical2)
                effect = EffectType.LandingImpact2;
            else if (skill == Vertical3)
                effect = BeamWasReached(__instance) ? EffectType.LandingImpact3Beam : EffectType.LandingImpact3;
            else
                effect = EffectType.LandingImpactBase;

            Blasphemous.ModdingAPI.ModLog.Info($"[Effect TX] {effect} " +
                $"({Framework.Managers.Core.Logic.Penitent.transform.position.x:F1}," +
                $"{Framework.Managers.Core.Logic.Penitent.transform.position.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(
                effect,
                Framework.Managers.Core.Logic.Penitent.transform.position);
        }

        private static bool BeamWasReached(Gameplay.GameControllers.Penitent.Abilities.VerticalAttack instance)
        {
            if (BeamReached == null)
                return false;

            object value = BeamReached.GetValue(instance, null);
            if (value == null)
                return false;

            return (bool)value;
        }
    }

    // Dash dust: hooked on the generator's two factories, which are the game's own points of dust
    // creation (start: OnCastStart only; stop: every stop path through GetDashDustPosition).
    // GetStopDashDust is overloaded and is deliberately not patched; GetDashDustPosition is not.
    [HarmonyPatch(typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator), "GetStartDashDust")]
    class DashDustStart_Patch
    {
        public static void Postfix(UnityEngine.GameObject __result)
        {
            if (__result == null)
                return;

            UnityEngine.Vector3 at = __result.transform.position;

            Blasphemous.ModdingAPI.ModLog.Info($"[Effect TX] DashDustStart ({at.x:F1},{at.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.DashDustStart, at);
        }
    }

    [HarmonyPatch(typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator), "GetDashDustPosition")]
    class DashDustStop_Patch
    {
        public static void Postfix(UnityEngine.Vector3 __result)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[Effect TX] DashDustStop ({__result.x:F1},{__result.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.DashDustStop, __result);
        }
    }

    // Vertical attack landing
    [HarmonyPatch(typeof(VerticalAttackLandingBehaviour), "OnStateEnter")]
    class VerticalAttackLandingBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(VerticalAttackLandingBehaviour), "OnStateUpdate")]
    class VerticalAttackLandingBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(VerticalAttackLandingBehaviour), "OnStateExit")]
    class VerticalAttackLandingBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    
    // Climb cliff
    [HarmonyPatch(typeof(ClimbCliffLedeBehaviour), "OnStateEnter")]
    class ClimbCliffLedeBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ClimbCliffLedeBehaviour), "OnStateUpdate")]
    class ClimbCliffLedeBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ClimbCliffLedeBehaviour), "OnStateExit")]
    class ClimbCliffLedeBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Hang on cliff
    [HarmonyPatch(typeof(HangOnCliffLedeBehaviour), "OnStateEnter")]
    class HangOnCliffLedeBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HangOnCliffLedeBehaviour), "OnStateUpdate")]
    class HangOnCliffLedeBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HangOnCliffLedeBehaviour), "OnStateExit")]
    class HangOnCliffLedeBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Grab ladder up
    [HarmonyPatch(typeof(GrabLadderBehaviour), "OnStateEnter")]
    class GrabLadderBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GrabLadderBehaviour), "OnStateUpdate")]
    class GrabLadderBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GrabLadderBehaviour), "OnStateExit")]
    class GrabLadderBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Grab ladder down
    [HarmonyPatch(typeof(GrabLadderDownBehaviour), "OnStateEnter")]
    class GrabLadderDownBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GrabLadderDownBehaviour), "OnStateUpdate")]
    class GrabLadderDownBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GrabLadderDownBehaviour), "OnStateExit")]
    class GrabLadderDownBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ladder go up
    [HarmonyPatch(typeof(LadderGoingUpBehaviour), "OnStateEnter")]
    class LadderGoingUpBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderGoingUpBehaviour), "OnStateUpdate")]
    class LadderGoingUpBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderGoingUpBehaviour), "OnStateExit")]
    class LadderGoingUpBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ladder go down
    [HarmonyPatch(typeof(LadderGoingDownBehaviour), "OnStateEnter")]
    class LadderGoingDownBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderGoingDownBehaviour), "OnStateUpdate")]
    class LadderGoingDownBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderGoingDownBehaviour), "OnStateExit")]
    class LadderGoingDownBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ladder sliding
    [HarmonyPatch(typeof(LadderSlidingBehaviour), "OnStateEnter")]
    class LadderSlidingBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderSlidingBehaviour), "OnStateUpdate")]
    class LadderSlidingBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderSlidingBehaviour), "OnStateExit")]
    class LadderSlidingBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ladder release top
    [HarmonyPatch(typeof(ReleaseTopLadderBehaviour), "OnStateEnter")]
    class ReleaseTopLadderBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ReleaseTopLadderBehaviour), "OnStateExit")]
    class ReleaseTopLadderBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ladder release bottom
    [HarmonyPatch(typeof(ReleaseBottomLadderBehaviour), "OnStateEnter")]
    class ReleaseBottomLadderBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ReleaseBottomLadderBehaviour), "OnStateExit")]
    class ReleaseBottomLadderBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Crouch attack
    [HarmonyPatch(typeof(CrouchAttackBehaviour), "OnStateEnter")]
    class CrouchAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.Crouch);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(CrouchAttackBehaviour), "OnStateUpdate")]
    class CrouchAttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(CrouchAttackBehaviour), "OnStateExit")]
    class CrouchAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Crouch down
    [HarmonyPatch(typeof(CrouchDownBehaviour), "OnStateEnter")]
    class CrouchDownBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(CrouchDownBehaviour), "OnStateUpdate")]
    class CrouchDownBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(CrouchDownBehaviour), "OnStateExit")]
    class CrouchDownBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Crouch up
    [HarmonyPatch(typeof(CrouchUpBehaviour), "OnStateEnter")]
    class CrouchUpBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(CrouchUpBehaviour), "OnStateUpdate")]
    class CrouchUpBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Dashing
    [HarmonyPatch(typeof(DashBehaviour), "OnStateEnter")]
    class DashBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(DashBehaviour), "OnStateUpdate")]
    class DashBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(DashBehaviour), "OnStateExit")]
    class DashBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Air dash
    [HarmonyPatch(typeof(AirDashBehaviour), "OnStateEnter")]
    class AirDashBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(AirDashBehaviour), "OnStateExit")]
    class AirDashBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Run after dash
    [HarmonyPatch(typeof(RunAfterDashBehaviour), "OnStateEnter")]
    class RunAfterDashBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(RunAfterDashBehaviour), "OnStateUpdate")]
    class RunAfterDashBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(RunAfterDashBehaviour), "OnStateExit")]
    class RunAfterDashBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Death
    [HarmonyPatch(typeof(PlayerDeathAnimationBehaviour), "OnStateEnter")]
    class PlayerDeathAnimationBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(PlayerDeathAnimationBehaviour), "OnStateUpdate")]
    class PlayerDeathAnimationBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Death by fall
    [HarmonyPatch(typeof(PlayerDeathFallBehaviour), "OnStateEnter")]
    class PlayerDeathFallBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Death by spike
    [HarmonyPatch(typeof(PlayerDeathSpikeBehaviour), "OnStateEnter")]
    class PlayerDeathSpikeBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Healing
    [HarmonyPatch(typeof(HealingBehaviour), "OnStateEnter")]
    class HealingBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HealingBehaviour), "OnStateExit")]
    class HealingBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ground hurt
    [HarmonyPatch(typeof(GroundHurtBehaviour), "OnStateEnter")]
    class GroundHurtBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundHurtBehaviour), "OnStateUpdate")]
    class GroundHurtBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundHurtBehaviour), "OnStateExit")]
    class GroundHurtBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Air hurt
    [HarmonyPatch(typeof(AirHurtBehaviour), "OnStateEnter")]
    class AirHurtBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(AirHurtBehaviour), "OnStateUpdate")]
    class AirHurtBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Grounding over
    [HarmonyPatch(typeof(GroundingOverBehaviour), "OnStateEnter")]
    class GroundingOverBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundingOverBehaviour), "OnStateUpdate")]
    class GroundingOverBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundingOverBehaviour), "OnStateExit")]
    class GroundingOverBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Falling over
    [HarmonyPatch(typeof(FallingOverBehaviour), "OnStateEnter")]
    class FallingOverBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FallingOverBehaviour), "OnStateUpdate")]
    class FallingOverBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Jump
    [HarmonyPatch(typeof(JumpBehaviour), "OnStateEnter")]
    class JumpBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Jump forward
    [HarmonyPatch(typeof(JumpForwardBehaviour), "OnStateEnter")]
    class JumpForwardBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(JumpForwardBehaviour), "OnStateExit")]
    class JumpForwardBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Jump off
    [HarmonyPatch(typeof(JumpOffBehaviour), "OnStateEnter")]
    class JumpOffBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(JumpOffBehaviour), "OnStateUpdate")]
    class JumpOffBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(JumpOffBehaviour), "OnStateExit")]
    class JumpOffBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Falling
    [HarmonyPatch(typeof(FallingBehaviour), "OnStateEnter")]
    class FallingBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FallingBehaviour), "OnStateUpdate")]
    class FallingBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Falling forward
    [HarmonyPatch(typeof(FallingForwardBehaviour), "OnStateEnter")]
    class FallingForwardBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FallingForwardBehaviour), "OnStateUpdate")]
    class FallingForwardBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(FallingForwardBehaviour), "OnStateExit")]
    class FallingForwardBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Landing
    [HarmonyPatch(typeof(LandingBehaviour), "OnStateEnter")]
    class LandingBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LandingBehaviour), "OnStateUpdate")]
    class LandingBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LandingBehaviour), "OnStateExit")]
    class LandingBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Landing running
    [HarmonyPatch(typeof(LandingRunningBehaviour), "OnStateEnter")]
    class LandingRunningBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LandingRunningBehaviour), "OnStateExit")]
    class LandingRunningBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Hard landing
    [HarmonyPatch(typeof(HardLandingBehaviour), "OnStateEnter")]
    class HardLandingBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HardLandingBehaviour), "OnStateUpdate")]
    class HardLandingBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HardLandingBehaviour), "OnStateExit")]
    class HardLandingBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Wall contact
    [HarmonyPatch(typeof(WallJumpContactBehaviour), "OnStateEnter")]
    class WallJumpContactBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Aura transform
    [HarmonyPatch(typeof(AuraTransformBehaviour), "OnStateEnter")]
    class AuraTransformBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(AuraTransformBehaviour), "OnStateExit")]
    class AuraTransformBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // High wills respawn
    [HarmonyPatch(typeof(HighWillsRespawnBehaviour), "OnStateEnter")]
    class HighWillsRespawnBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HighWillsRespawnBehaviour), "OnStateExit")]
    class HighWillsRespawnBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Return to port
    [HarmonyPatch(typeof(PR202TeleportBehaviour), "OnStateEnter")]
    class PR202TeleportBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(PR202TeleportBehaviour), "OnStateExit")]
    class PR202TeleportBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Ground range attack
    [HarmonyPatch(typeof(GroundRangeAttackBehaviour), "OnStateEnter")]
    class GroundRangeAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.Ranged);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(GroundRangeAttackBehaviour), "OnStateUpdate")]
    class GroundRangeAttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundRangeAttackBehaviour), "OnStateExit")]
    class GroundRangeAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Mid air range attack
    [HarmonyPatch(typeof(MidAirRangeAttackBehaviour), "OnStateEnter")]
    class MidAirRangeAttackBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator)
        {
            if (animator.name == "Body")
            {
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.Ranged);
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(MidAirRangeAttackBehaviour), "OnStateUpdate")]
    class MidAirRangeAttackBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(MidAirRangeAttackBehaviour), "OnStateExit")]
    class MidAirRangeAttackBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Idle
    [HarmonyPatch(typeof(IdleAnimatonBehaviour), "OnStateEnter")]
    class IdleAnimatonBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(IdleAnimatonBehaviour), "OnStateExit")]
    class IdleAnimatonBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Move
    [HarmonyPatch(typeof(MoveAnimationBehaviour), "OnStateEnter")]
    class MoveAnimationBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Run
    [HarmonyPatch(typeof(RunStartBehaviour), "OnStateEnter")]
    class RunStartBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    // Sub states

    // Crouch
    [HarmonyPatch(typeof(CrouchSubStateBehaviour), "OnStateUpdate")]
    class CrouchSubStateBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ladder climbing
    [HarmonyPatch(typeof(LadderClimbingSubStateBehaviour), "OnStateEnter")]
    class LadderClimbingSubStateBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderClimbingSubStateBehaviour), "OnStateUpdate")]
    class LadderClimbingSubStateBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(LadderClimbingSubStateBehaviour), "OnStateExit")]
    class LadderClimbingSubStateBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Hurt
    [HarmonyPatch(typeof(HurtSubStateBehaviour), "OnStateEnter")]
    class HurtSubStateBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HurtSubStateBehaviour), "OnStateUpdate")]
    class HurtSubStateBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(HurtSubStateBehaviour), "OnStateExit")]
    class HurtSubStateBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Ground attack
    [HarmonyPatch(typeof(GroundAttackSubStateBehaviour), "OnStateEnter")]
    class GroundAttackSubStateBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundAttackSubStateBehaviour), "OnStateUpdate")]
    class GroundAttackSubStateBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(GroundAttackSubStateBehaviour), "OnStateExit")]
    class GroundAttackSubStateBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Air attack
    [HarmonyPatch(typeof(AirAttackSubStateBehaviour), "OnStateEnter")]
    class AirAttackSubStateBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(AirAttackSubStateBehaviour), "OnStateExit")]
    class AirAttackSubStateBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    // Charge attack
    [HarmonyPatch(typeof(ChargeAttackSubStateBehaviour), "OnStateEnter")]
    class ChargeAttackSubStateBehaviourEnter_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ChargeAttackSubStateBehaviour), "OnStateUpdate")]
    class ChargeAttackSubStateBehaviourUpdate_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }
    [HarmonyPatch(typeof(ChargeAttackSubStateBehaviour), "OnStateExit")]
    class ChargeAttackSubStateBehaviourExit_Patch
    {
        public static bool Prefix(Animator animator) => animator.name == "Body";
    }

    /// <summary>
    /// Counter-attack (PenitentParry). Also sent alongside the clang because the IL cannot say
    /// which of the two events on a successful parry is the actual clang; the player's ear
    /// decides.
    /// </summary>
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Audio.PenitentAudio), "PlayParryAttack")]
    class PenitentParryAttackSound_Patch
    {
        public static void Postfix()
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;
            Blasphemous.ModdingAPI.ModLog.Info($"[Sound TX] ParryAttack ({at.x:F1},{at.y:F1})");
            Main.Multiplayer.NetworkManager.SendEffect(EffectType.ParryAttack, at);
        }
    }
}