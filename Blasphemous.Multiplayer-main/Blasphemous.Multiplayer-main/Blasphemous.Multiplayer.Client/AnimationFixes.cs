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
// A5 - i due punti di aggancio dei SUONI: `PenitentMoveAnimations.PlayFootStep()` (i passi) e i sei
// `PenitentAttackAnimations.Play*` (i fendenti). Namespace letto dall'assembly, non indovinato.
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
                // A2: this state used to return true and tell the other machine NOTHING, so the charged attack
                // existed only on the machine that performed it - the remote avatar simply stood there. Every
                // other attack already announces itself on this channel; these four were the ones left out.
                //
                // The line below is what makes it PROVABLE. The effect type is one the ground attacks already
                // used, so the effect arriving tells us nothing about whether THIS patch sent it - and that is
                // a gap in the verification, not in the code. One line, and the attribution is unambiguous.
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
                // A2: the charged attack's own effect state, which is where the swing is actually visible.
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
                // A2: the lunge, which is the attack that carries the penitent forward - the one whose absence
                // is most obvious on the other screen, because the avatar stays put while the player lunges.
                Blasphemous.ModdingAPI.ModLog.Info("[Invia] Lunge");
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.SidewaysGrounded);

                // A3: and the trail, which on this attack comes from the same state. Two sends rather than one
                // because the packet carries a single byte - doubling a byte on the wire is cheaper than
                // widening a payload for the sake of tidiness.
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

            // ⚠️ NOT the body animator: return FALSE, which is what the original one-liner did and what every
            // other patch in this file does. Returning true unconditionally - which is what a careless rewrite
            // of `=> animator.name == "Body"` produces - lets the game's real OnStateExit run for the SWORD
            // animator too, and it throws a NullReferenceException there. That is a patch that reads correctly
            // and behaves differently from the line it replaced: the whole point of the one-liner was the
            // return VALUE, not the guard.
            return false;
        }
    }

    // A3 - the other three places that turn the penitent's ghost trail on and off.
    //
    // Each is patched where the GAME itself decides, rather than in one place of our own, so the receiving
    // machine follows the same decisions instead of a guess about them. They fire only for the LOCAL penitent:
    // the remote avatar is a bare GameObject and carries none of these components, so nothing echoes back.
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
        // The game calls this with the state it wants, so the flag is passed straight through: no need to
        // infer when the plunge's trail starts or ends.
        public static void Postfix(bool show)
        {
            Main.Multiplayer.NetworkManager.SendEffect(show ? EffectType.TrailOn : EffectType.TrailOff);
        }
    }
    [HarmonyPatch(typeof(Framework.Inventory.IncreaseSpeedSwordHeartEffect), "ApplyGhostingEffect")]
    class IncreaseSpeedSwordHeartEffectGhosting_Patch
    {
        // ⚠️ THE PARAMETER NAME MUST MATCH THE GAME'S, CHARACTER FOR CHARACTER.
        //
        // Harmony binds a Postfix's arguments BY NAME, and the game's parameter here is called `apply`. This
        // was first written as `enable` - a name that reads better and exists nowhere in the method - and
        // Harmony threw "Parameter enable not found" while patching. That throw happens during patch
        // APPLICATION, so it took the whole mod down: initialisation never finished, and everything after it
        // reported a NullReferenceException. One parameter name, no mod at all.
        //
        // The deploy's target check cannot catch this: it verifies that the TYPE and the METHOD exist, not
        // that the patch's own arguments line up. So the name is read off the assembly, not chosen.
        public static void Postfix(bool apply)
        {
            Main.Multiplayer.NetworkManager.SendEffect(apply ? EffectType.TrailOn : EffectType.TrailOff);
        }
    }

    /// <summary>
    /// Il mittente condiviso dei SEI fendenti di A5 (base 1 e 2, aria 1 e 2, pesante, rilascio della
    /// carica). Uno solo perche' i sei corpi sarebbero stati identici a meno di due parole, e sei copie
    /// di una stessa riga sono sei posti in cui una puo' divergere.
    ///
    /// ⚠️ PERCHE' DUE DEGLI OTTO SUONI DI A5 NON PASSANO DA QUI. I passi hanno il loro blocco perche'
    /// hanno un FUSIBILE DI FREQUENZA che gli altri non hanno; la scivolata ha il suo perche' e' un
    /// evento SOSTENUTO con uno START e uno STOP separati. Gli altri sei sono one-shot puri: accendono,
    /// suonano e si rilasciano da soli (`EventOneShotPanned` chiama `release()` subito dopo `start()`).
    ///
    /// ⚠️ E NON C'E' NESSUN FUSIBILE QUI, ed e' voluto: un fendente e' un'azione DELIBERATA del
    /// giocatore, non un evento di animazione che si ripete da solo. Il massimo che un umano produce e'
    /// la raffica di una combo, e ogni fendente della combo ha un suono DIVERSO: sopprimerne uno
    /// significherebbe far sentire al peer una combo che non e' quella che sta guardando.
    /// </summary>
    static class LocalSoundSender
    {
        private static int sentCount;

        /// <summary>
        /// Spedisce il suono del fendente appena suonato DAL GIOCO su questa macchina, con la posizione
        /// del penitent (che e' dove il gioco prende il panning: `EntityAudio.SetPanning` legge
        /// `Owner.transform.position`).
        ///
        /// `what` serve SOLO al log: il payload e' un byte, e il nome del suono lo risolve il ricevente
        /// dalla propria copia dei `const`. Non e' un dato trasmesso.
        /// </summary>
        public static void SendLocalSound(EffectType effect, string what)
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            // PROVA LATO MITTENTE, con lo stesso filtro dei passi e per la stessa ragione: la riga
            // `[Effect]` la scrive il RICEVENTE, e `QueueMesssage` esce in silenzio da disconnesso.
            // Il primo e poi uno ogni dieci: un fendente e' raro, ma una combo lunga no.
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
    // ============================================================================================
    // A5 - I PASSI A TERRA DEI PEER.
    // ============================================================================================
    //
    // PERCHE' QUESTO METODO, E NON UN `Behaviour`. La catena del passo, verificata con una ricerca dei
    // chiamanti su TUTTO l'assembly, e' questa - e ha UN SOLO punto in cui il gioco suona:
    //
    //     [evento di animazione dentro il clip]      <- non ha chiamanti nel IL: vive nei dati del clip
    //       -> PenitentMoveAnimations.PlayFootStep()  <- QUESTO metodo, public, ZERO argomenti
    //           -> _penitentAudio.PlayFootStep()
    //               -> EntityAudio.PlayOneShotEvent("PenitentFootStep", FxSoundCategory.Motion)
    //                   -> FMODAudioManager.CreateCatalogEvent("PenitentFootStep")
    //
    // Nessun Behaviour lo chiama e nessun input lo innesca: e' l'evento di animazione, e basta. Patchare
    // QUI da' un mittente 1:1 con il suono che il gioco suona, senza inventare una condizione di
    // movimento che il gioco non usa.
    //
    // ⭐ ED E' ANCHE LA FIRMA PIU' SICURA CHE ESISTA: `PlayFootStep()` non ha argomenti. Harmony lega i
    // parametri dei patch PER NOME, e un nome sbagliato ha gia' IMPEDITO ALLA MOD DI AVVIARSI: con zero
    // argomenti quel rischio non esiste.
    //
    // PERCHE' UN FUSIBILE. I passi sono l'effetto piu' frequente di tutto il canale Effect, e la loro
    // cadenza non e' leggibile da qui: sta negli eventi dentro i clip di animazione, cioe' negli asset.
    // La costante sotto e' un TETTO, non una limatura: a 0.08 s sono 12.5 al secondo, mentre la falcata
    // del gioco emette un passo circa ogni 0.2 s (5/s), quindi in condizioni normali NON si scarta nulla.
    // Se un giorno la cadenza crescesse, il fusibile tiene il traffico a ~250 byte/s invece di lasciarlo
    // crescere in silenzio. Non si mette una taratura fine su una cosa che non si puo' misurare.
    //
    // ⚠️ SI SPEDISCE SOLO SE IL PENITENT E' A TERRA. Il clip della scala e quello dell'idle possono
    // contenere lo stesso evento, e un passo su una scala non e' un passo a terra. La proprieta' e' letta
    // dove vive davvero: `Penitent.Status` e' un CAMPO PUBLIC di `Entity` (base di `Penitent`) di tipo
    // `Framework.FrameworkCore.EntityStatus`, e `IsGrounded` e' una sua PROPERTY public.
    //
    // ⚠️ E IL VALORE DI RITORNO NON SI TOCCA. Questa classe non ha `Prefix`: il suono del gioco resta
    // esattamente quello di prima. I `Prefix` di questo file - il cui VALORE DI RITORNO e' il punto, e la
    // cui riscrittura ha gia' causato 12 NRE - non vengono sfiorati.
    [HarmonyPatch(typeof(PenitentMoveAnimations), "PlayFootStep")]
    class PenitentFootStep_Patch
    {
        /// <summary>
        /// Tetto di frequenza verso il peer. Vedi il commento sopra: e' un fusibile, non una taratura.
        /// </summary>
        private const float MIN_INTERVAL = 0.08f;

        private static float lastSent = float.NegativeInfinity;
        private static int sentCount;

        // Postfix, e senza argomenti: il metodo del gioco gira per primo e resta INTATTO.
        public static void Postfix()
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            // Solo a terra: il clip della scala e quello dell'idle possono contenere lo stesso evento, e un
            // passo su una scala non e' un passo a terra.
            if (Framework.Managers.Core.Logic.Penitent.Status == null ||
                !Framework.Managers.Core.Logic.Penitent.Status.IsGrounded)
                return;

            // `Time.unscaledTime` e non `Time.time`: i suoni di FMOD non si fermano quando il gioco mette
            // in pausa, quindi il metro di paragone dev'essere lo stesso orologio.
            if (UnityEngine.Time.unscaledTime - lastSent < MIN_INTERVAL)
                return;

            lastSent = UnityEngine.Time.unscaledTime;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            // PROVA LATO MITTENTE, e ragione per cui esiste: la riga `[Effect]` e' scritta SOLO dal
            // RICEVENTE (AttackManager.ReceiveEffect) e `NetworkManager.QueueMesssage` esce in silenzio
            // mentre e' disconnesso, quindi senza questa riga "il Postfix non e' mai girato" e "il
            // pacchetto non e' mai arrivato" restano INDISTINGUIBILI in entrambi i log.
            //
            // ⚠️ E QUI C'E' UN SECONDO FILTRO, che gli altri effetti non hanno: i passi arrivano a
            // raffica, e una riga ciascuno affogherebbe il log. Si stampa il PRIMO (che e' quello che
            // dice "il canale e' acceso") e poi uno ogni dieci (che e' quello che dice "sta ancora
            // andando"). Se il contatore si ferma su un numero, si sa che si e' fermato DOPO quel passo.
            sentCount++;
            if (sentCount == 1 || sentCount % 10 == 0)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[Sound TX] FootStep n.{sentCount} ({at.x:F1},{at.y:F1})");
            }

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.FootStep, at);
        }
    }

    // ============================================================================================
    // A5 - I SUONI DEGLI ATTACCHI A VUOTO (il fendente).
    // ============================================================================================
    //
    // COS'E' UN "ATTACCO A VUOTO". E' un fendente che non incontra nulla. E il suono che esce e' LO STESSO
    // di un fendente che colpisce, perche' NON dipende dal colpo: e' un EVENTO DI ANIMAZIONE dentro il
    // clip. Verificato: il corpo di `PenitentAttackAnimations.PlayBasicAttack1()` e' una sola chiamata a
    // `_penitent.Audio.PlayBasicAttack1()`, e non legge ne' l'area d'attacco ne' `IsEnemyHit` (quelle le
    // legge `ComboCameraShake`, che e' un evento diverso e ha un'altra guardia).
    //   ⇒ Non serve sapere se il colpo e' andato a segno. Non serve un flag nel payload.
    //
    // CHI SUONA, PER OGNI UNO DEI SEI (ognuno con UN SOLO chiamante nel gioco, oltre all'evento di clip):
    //     PenitentAttackAnimations.PlayBasicAttack1()      -> PenitentAudio.PlayBasicAttack1()
    //     PenitentAttackAnimations.PlayBasicAttack2()      -> PenitentAudio.PlayBasicAttack2()
    //     PenitentAttackAnimations.PlayAirAttack1()        -> PenitentAudio.PlayAirAttack1()
    //     PenitentAttackAnimations.PlayAirAttack2()        -> PenitentAudio.PlayBasicAttack2()   <-- !
    //     PenitentAttackAnimations.PlayHeavyAttack()       -> PenitentAudio.PlayHeavyAttack()
    //     PenitentAttackAnimations.PlayReleaseChargeAttack()-> PenitentAudio.PlayReleaseChargedAttack()
    //   ⚠️ `PlayAirAttack2` NON esiste in PenitentAudio: l'evento in aria 2 riusa `PlayBasicAttack2`, e
    //   la costante `BasicAirAttack2EventKey` ("PenitentAirAttack2") non e' letta da NESSUN metodo
    //   dell'assembly. ⇒ Il mittente e' comunque quello giusto (l'evento di clip esiste ed e' distinto),
    //   e il byte trasmesso e' distinto (92); e' solo il RICEVENTE che deve risolvere sulla chiave che
    //   suona davvero.
    //
    // LA NATURA DEL SUONO, ed e' la cosa che decide il METODO del ricevente: sono TUTTI E SEI chiavi di
    // catalogo, con il valore in una `public const string` di `PenitentAudio`. NON sono campi `[EventRef]`
    // di un prefab e NON sono percorsi di banca. ⇒ NON si trasmette alcuna stringa e NON si legge alcun
    // campo serializzato: viaggia il solo byte, e il ricevente risolve la chiave nel PROPRIO catalogo.
    //
    // PERCHE' POSTFIX E SENZA ARGOMENTI. I sei metodi non hanno parametri. Col Postfix il suono del gioco
    // parte comunque e resta intatto; senza argomenti non c'e' alcun nome di parametro da sbagliare. E
    // sono tutti `public` su `PenitentAttackAnimations`, che e' un MonoBehaviour del penitent: l'avatar
    // remoto non lo porta.
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

    // A5.2 - I SUONI CONFERMATI DALLE PROBE (zero-argomenti, one-shot, tutti `PlaySfxOnCatalog` tranne
    // `IdleSword` che e' categoria Motion -> sul ricevente passa dai 14 materiali del terreno).
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

    // ============================================================================================
    // A5 - LE PROBE SONO STATE RIMOSSE. Avevano un solo scopo: dire QUALE suono il gioco suona
    // davvero per il quarto colpo, per lo sguaino e per l'attacco aereo-a-terra, invece di
    // scommettere su tre fix. Hanno risposto (`ComboHit` si', `ComboHitUp/Down` mai, `IdleSword`
    // raro, `LoadingCharged` si'), i fix sono al loro posto, e il test ha confermato.
    //
    // Se servisse di nuovo una di quelle risposte, sono cinque righe:
    //     [HarmonyPatch(typeof(PenitentAttackAnimations), "PlayComboHit")]
    //     class X { public static void Postfix() { ModLog.Info("[Probe] ComboHit"); } }

    // A4 - the slide's dust and its sound.
    //
    // Sent from the place the game performs the slide, and as a POSTFIX so the ability itself is untouched. The
    // position is the penitent's own, which is where the game takes it from too.
    //
    // ⚠️ The receiving machine must NOT call OnCastStart: that would CAST THE ABILITY there. This end only says
    // WHERE it happened; the other end reads GuardSlide's two public fields and reproduces the effect itself.
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.GuardSlide), "OnCastStart")]
    class GuardSlideEffect_Patch
    {
        public static void Postfix()
        {
            if (Framework.Managers.Core.Logic.Penitent == null)
                return;

            UnityEngine.Vector3 at = Framework.Managers.Core.Logic.Penitent.transform.position;

            // SENDER-SIDE PROOF, and the reason this line exists at all: the [Effect] line is written ONLY on
            // ARRIVAL (AttackManager.ReceiveEffect) and NetworkManager.QueueMesssage returns silently while
            // disconnected, so today "this Postfix never ran" and "the packet never arrived" are
            // INDISTINGUISHABLE in both logs. This is the only line that tells them apart.
            // ModLog is written out in full because this file has no `using Blasphemous.ModdingAPI;` - the
            // other ModLog calls in it do the same.
            Blasphemous.ModdingAPI.ModLog.Info($"[Effect TX] SlideDust ({at.x:F1},{at.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.SlideDust, at);
        }
    }

    // A4 - the ground impact: the dust, the beam and the landing sound.
    //
    // WHY THIS METHOD. VerticalAttack.TriggerVerticalAttack(bool) is where a plunge finishes, and it is reached
    // exactly ONCE per plunge: OnUpdate latches its private _instantiateExplosion to true immediately before
    // calling it with true, and it only does so while the animator sits in VerticalAttackLanding. Its only two
    // callers are VerticalAttack.OnUpdate (true) and PenitentAttackAnimations.PrayerUseAttack (FALSE) - and
    // that false is the whole reason the argument is checked below. Without the check this patch would also
    // fire every time a prayer is used, and the peer would watch a ground impact that never happened.
    //
    // ⚠️ THE RECEIVING MACHINE MUST NOT CALL THIS METHOD. TriggerVerticalAttack also runs
    // AttackDamageableEntities on the hard-landing Hit and disables the attack collider: calling it on the
    // other end would DEAL THE LANDING DAMAGE A SECOND TIME. This end only says WHERE it happened and WHICH
    // of the five shapes it was, so the other end can draw it without running any of it.
    //
    // ⚠️ And it is a POSTFIX, so the ability runs untouched - the same rule as the slide above.
    [HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Abilities.VerticalAttack), "TriggerVerticalAttack")]
    class VerticalAttackLandingEffect_Patch
    {
        // The skill is read through the PUBLIC LastUnlockedSkillId, which Ability fills from
        // GetLastUnlockedSkill().id in OnUpdate just before the plunge begins, so at landing it is the same
        // value the landing itself uses. These three strings are the ones the game compares against in
        // get_GetLandingFxEventKey - not invented.
        private const string Vertical1 = "VERTICAL_1";
        private const string Vertical2 = "VERTICAL_2";
        private const string Vertical3 = "VERTICAL_3";

        /// <summary>
        /// VerticalAttack.BeamRequiredHeightReached is PRIVATE (verified in IL: `P Boolean
        /// BeamRequiredHeightReached` with `M private Boolean get_BeamRequiredHeightReached()`), and it is the
        /// only thing that separates the upgraded impact - the beam included - from the plain one on a
        /// VERTICAL_3 plunge. It is looked up once, here, and cached. If the name ever stops existing the
        /// lookup yields null and the impact degrades to the plain one instead of throwing.
        /// </summary>
        private static readonly System.Reflection.PropertyInfo BeamReached =
            typeof(Gameplay.GameControllers.Penitent.Abilities.VerticalAttack).GetProperty(
                "BeamRequiredHeightReached",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public static void Postfix(
            Gameplay.GameControllers.Penitent.Abilities.VerticalAttack __instance,
            bool instantiateExplosion)
        {
            // PrayerUseAttack calls this with false. Sending here would invent an impact.
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

            // The position is the penitent's own, which is where the game takes it from too: both
            // InstantiateExplosion and the landing audio read the penitent's transform.
            //
            // [Effect TX] for the same reason as the slide above: the arrival line is written on the OTHER
            // machine, so without this one "the Postfix never ran" and "the packet never arrived" cannot be
            // told apart - and the plunge has two gates of its own that can stop it before this point.
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
    // A5 - la polvere della SCIVOLATA DI MOVIMENTO (Dash), sull'ALTRA macchina.
    //
    // PERCHE' QUI E NON SU Dash.OnCastStart / Dash.OnCastEnd. Quei due sono GIA' patchati da A3 per il
    // ghost trail, e questa funzione non deve dipendere da quella patch ne' disturbarla. I due metodi
    // patchati sotto sono i punti in cui il GIOCO crea la polvere del dash, e sono raggiunti da OGNI
    // percorso che la crea (verificato nel IL di Assembly-CSharp.dll):
    //
    //   GetStartDashDust()    - UN SOLO chiamante in tutto l'assembly: Dash.OnCastStart.
    //   GetDashDustPosition() - UN SOLO chiamante in tutto l'assembly: GetStopDashDust(), che a sua
    //                           volta e' raggiunto da PenitentMoveAnimations.RaiseStopDust (un EVENTO DI
    //                           ANIMAZIONE, che quindi non ha alcun chiamante nel IL) e dai QUATTRO punti
    //                           con GetStopDashDust(float) di DashBehaviour (tre in OnStateUpdate, uno in
    //                           Crouch) attraverso DelayStopDash.
    //                           Una sola patch copre quindi TUTTE E CINQUE le emissioni di arresto.
    //
    // ⚠️ GetStopDashDust NON si patcha: e' SOVRACCARICO (esistono GetStopDashDust() e
    // GetStopDashDust(System.Single)). Un [HarmonyPatch(typeof(X), "GetStopDashDust")] senza lista di
    // argomenti e' una ricerca PER SOLO NOME che deve scegliere fra due metodi - e il verificatore dei
    // bersagli non lo vedrebbe, perche' indicizza i metodi per nome. GetDashDustPosition non e'
    // sovraccarico, ha un solo chiamante, e il suo VALORE DI RITORNO e' la posizione esatta che il gioco
    // passa a PoolManager.ReuseObject: cosi' non va riscritto un solo carattere dell'aritmetica del
    // gioco, compresa la mutazione `StopDustSpawnOffsetPos *= +-1` che quel metodo compie.
    //
    // ⚠️ Sono POSTFIX: il metodo del gioco gira per primo e resta INTATTO. Nessun valore di ritorno
    // toccato, nessun effetto collaterale cambiato: i Prefix di questo file - il cui VALORE DI RITORNO
    // e' il punto, e la cui riscrittura ha gia' causato 12 NRE - non vengono sfiorati.
    //
    // ⚠️ Scattano SOLO per il penitent LOCALE: l'avatar remoto e' un GameObject nudo costruito da
    // OtherPlayerManager.AddActivePlayer e non porta alcun DashDustGenerator, quindi nulla torna indietro.
    [HarmonyPatch(typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator), "GetStartDashDust")]
    class DashDustStart_Patch
    {
        // La posizione NON e' inventata: GetStartDashDust restituisce l'istanza appena riusata dal pool,
        // e ObjectInstance.Reuse(position, rotation) le ha appena assegnato ESATTAMENTE
        // `_penitent.transform.position`, che e' il valore che il gioco legge dentro quel metodo.
        public static void Postfix(UnityEngine.GameObject __result)
        {
            if (__result == null)
                return;

            UnityEngine.Vector3 at = __result.transform.position;

            // PROVA LATO MITTENTE, e ragione per cui questa riga esiste: la riga [Effect] e' scritta SOLO
            // dal RICEVENTE (AttackManager.ReceiveEffect) e NetworkManager.QueueMesssage esce in silenzio
            // mentre e' disconnesso, quindi oggi "questo Postfix non e' mai girato" e "il pacchetto non e'
            // mai arrivato" sono INDISTINGUIBILI in entrambi i log. Questa e' l'unica riga che li separa.
            // ModLog e' scritto per esteso perche' questo file non ha `using Blasphemous.ModdingAPI;`:
            // le altre chiamate a ModLog che contiene fanno lo stesso.
            Blasphemous.ModdingAPI.ModLog.Info($"[Effect TX] DashDustStart ({at.x:F1},{at.y:F1})");

            Main.Multiplayer.NetworkManager.SendEffect(EffectType.DashDustStart, at);
        }
    }

    [HarmonyPatch(typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator), "GetDashDustPosition")]
    class DashDustStop_Patch
    {
        // __result E' la posizione: GetStopDashDust() passa questo valore di ritorno diritto a
        // PoolManager.ReuseObject, quindi e' il punto esatto in cui la polvere di arresto del gioco nasce.
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
}
