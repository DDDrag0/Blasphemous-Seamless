using System;
using Blasphemous.ModdingAPI;
using Framework.Util;
using Gameplay.GameControllers.Bosses.EcclesiaBros.Perpetua;
using Gameplay.GameControllers.Bosses.Quirce.Attack;
using Gameplay.GameControllers.Enemies.WallEnemy.Attack;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Keeps the game's CONTACT damage away from the mod's aggro proxy.
    ///
    /// The proxy is a plain collider on the "Penitent" layer that exists only so the enemies' vision can
    /// find the other player. It has no Entity and no IDamageable on purpose, so nothing can be hurt
    /// through it. But a collider is a collider to the physics engine, and the game arms its contact
    /// attacks from OnTriggerEnter2D:
    ///
    ///     IDamageable c = other.GetComponentInParent&lt;IDamageable&gt;();
    ///     ...
    ///     EntityContactDamage(c);          ->  EnemyAttack.ContactAttack(c)  ->  c.Damage(ContactHit)
    ///
    /// Two things go wrong when that `other` is the proxy, and this prefix closes both:
    ///   - `c` is null (the proxy has no IDamageable) and the call is made anyway, throwing a
    ///     NullReferenceException out of a Unity trigger callback;
    ///   - even before that, IsTargetOverlapped has already been raised, and the attacks that read it
    ///     strike Core.Logic.Penitent without measuring any distance - so the LOCAL player, standing
    ///     twenty units away, took a hit from an enemy that only ever "felt" the proxy.
    ///
    /// Returning false skips the original method, so the proxy is simply not a damageable contact.
    /// </summary>
    [HarmonyPatch(typeof(ContactDamage), "OnTriggerEnter2D")]
    class ContactDamageProxyIgnore_Patch
    {
        public static bool Prefix(Collider2D other)
        {
            return !EnemySyncManager.IsRemoteTargetProxy(other == null ? null : other.gameObject);
        }
    }

    // =====================================================================================================
    // ⭐⭐ LO STESSO BUCO, PER LE AREE D'ATTACCO — «il colpo del boss che evapora quando mira al proxy».
    //
    // ⚠️ QUESTO E' IL PASSO ① DEL REFERTO (`scratch\DIAGNOSI-DANNO-BOSS-COOP.md` §6-①): si toglie
    // l'ECCEZIONE, NON si da' il danno all'altro giocatore. Il danno vero — il proxy che diventa
    // `IDamageable` e INOLTRA il colpo al peer — e' il passo ② e tocca il protocollo: NON e' qui.
    //
    // LA MISURA, dai log dell'host: `BossJumpAttack.OnPenitentEntersArea` fa
    //
    //     GameObject gameObject = e.Collider2DArg.gameObject;             // Assembly-CSharp.cs:75348
    //     _hit.OnGuardCallback = OnGuard;                                 //             :75349
    //     gameObject.GetComponentInParent<IDamageable>().Damage(_hit);    //             :75350  ← senza null-check
    //
    // e il proxy (`_coop_remote_target`, layer `Penitent`, `BoxCollider2D` trigger, `EnemySyncManager.cs`)
    // non ha `IDamageable` PER PROGETTO ⇒ la chiamata e' su `null` ⇒ `NullReferenceException` DENTRO la
    // callback del trigger ⇒ **il colpo si perde su ENTRAMBE le macchine** (3 volte in `G1:L2201, L2255,
    // L2308`). Il filtro che il proxy attraversa e' solo di layer: `AttackArea.OnTriggerEnter2D` fa
    // `if ((enemyLayerMask.value & (1 << col.gameObject.layer)) > 0)` (`:125922-125928`), e il proxy e'
    // sul layer `Penitent` — cioe' esattamente quello che il mask contiene.
    //
    // LA FORMA DEL FIX E' QUELLA GIA' USATA SOPRA PER IL CONTATTO: un `Prefix` che, quando il bersaglio
    // e' il proxy, fa `return false` e salta il colpo. Niente `Damage()`, niente `Kill()`, niente stato:
    // il colpo semplicemente non ha nessuno su cui atterrare, ed e' la verita' — l'altro giocatore, su
    // QUESTA macchina, non esiste.
    //
    // ⚠️ E NON SI TOCCA IL DANNO NORMALE: la guardia salta SOLO se il bersaglio e' il proxy, oppure se
    // non ha nessun `IDamageable` fra i parent — cioe' i soli casi in cui il gioco, senza di noi,
    // andrebbe in eccezione. Un `IDamageable` VERO (il `Penitent` locale) passa e prende il suo colpo
    // esattamente come prima.
    //
    // I QUATTRO SITI (tutti quelli raggiungibili dal proxy nella direzione nemico -> giocatore, con
    // `GetComponentInParent<IDamageable>().Damage()` e nessun null-check). Gli altri tre della stessa
    // forma (`CowardTrap.OnEnter:97649`, `JumperAttack:107898`, `ReekLeaderAttack:118785`) sono dietro
    // un `CompareTag("Penitent")` che il proxy NON soddisfa (nasce `Untagged`): non li tocca, e restano
    // fuori di proposito.
    //
    // ⭐ E C'E' UNA RIGA DI LOG, `[ProxyGuard]`, perche' senza quella il fix sarebbe INVISIBILE: si conta
    // quante volte la guardia e' scattata, e su quale attacco.
    //
    // ⚠️ COSTO: **quattro bersagli Harmony in piu'** (263 -> 267), e sono il prezzo di questa via: un
    // `Prefix` per metodo patchato e' l'unico modo di fermare una callback di trigger PRIMA che chiami
    // `Damage()` senza null-check. La via a zero patch (il proxy che diventa `IDamageable`) e' il passo
    // ② — tocca il protocollo e cambia il significato del proxy per TUTTE le armi del gioco.
    // =====================================================================================================

    /// <summary>
    /// La decisione, in un posto solo: «questo colpo non ha dove atterrare».
    ///
    /// Due vie, e la seconda e' la rete di sicurezza della prima:
    ///   1. **identita'** — `EnemySyncManager.IsRemoteTargetProxy` confronta il RIFERIMENTO con
    ///      `_coop_remote_target` (piu' `IsChildOf`): nessun nome, nessun tag, nessun layer da fidarsi;
    ///   2. **struttura** — `GetComponentInParent&lt;IDamageable&gt;()` nullo: e' il caso che il gioco non
    ///      gestisce. Copre il proxy anche se la via 1 non lo riconoscesse (proxy non ancora creato) e
    ///      qualunque altro oggetto nudo di un'altra mod finito sul layer `Penitent`.
    ///
    /// ⚠️ In caso di dubbio NON si salta: se la guardia stessa si rompe, si torna al comportamento di
    /// prima (`return false` = la patch non fa niente). Un colpo tolto per errore a un `IDamageable` vero
    /// sarebbe peggio dell'eccezione che stiamo chiudendo.
    /// </summary>
    internal static class ProxyDamageGuard
    {
        internal const string Marker = "[ProxyGuard]";

        internal static GameObject ObjectOf(Collider2DParam e)
        {
            if (e == null || e.Collider2DArg == null)
                return null;

            return e.Collider2DArg.gameObject;
        }

        /// <summary>True quando il colpo va SALTATO: il bersaglio e' il proxy, o non e' danneggiabile.</summary>
        internal static bool MustIgnore(GameObject target, string site)
        {
            try
            {
                if (target == null)
                {
                    Report(site, "il bersaglio e' nullo");
                    return true;
                }

                if (EnemySyncManager.IsRemoteTargetProxy(target))
                {
                    Report(site, "il bersaglio E' il proxy dell'altro giocatore (_coop_remote_target)");
                    return true;
                }

                if (target.GetComponentInParent<IDamageable>() == null)
                {
                    Report(site, "il bersaglio non ha IDamageable fra i parent");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                try
                {
                    ModLog.Error(Marker + " la guardia e' fallita (" + site + "): " +
                                 e.GetType().Name + " - " + e.Message);
                }
                catch
                {
                    // Se non si riesce nemmeno a scrivere nel log non c'e' altro da fare: si prosegue.
                }

                return false;
            }
        }

        private static void Report(string site, string why)
        {
            try
            {
                ModLog.Info(Marker + " colpo SALTATO (" + site + "): " + why +
                            " - nessun danno, nessuna eccezione");
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// `BossJumpAttack.OnPenitentEntersArea` — IL sito misurato (il salto dell'ElderBrother).
    /// Senza questo `Prefix`: `NullReferenceException` a `Assembly-CSharp.cs:75350`, colpo perso.
    /// </summary>
    [HarmonyPatch(typeof(BossJumpAttack), "OnPenitentEntersArea")]
    class BossJumpAttackProxyIgnore_Patch
    {
        public static bool Prefix(Collider2DParam e)
        {
            try
            {
                return !ProxyDamageGuard.MustIgnore(ProxyDamageGuard.ObjectOf(e),
                    "BossJumpAttack.OnPenitentEntersArea");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] BossJumpAttack: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// `BossDashAttack.OnPenitentEntersArea` — stessa identica forma (`Assembly-CSharp.cs:74501`, nessun
    /// null-check), stesso hook `AttackArea.OnEnter` filtrato per solo layer ⇒ il proxy lo attraversa.
    /// Il dash di Quirce/Perpetua ha lo stesso buco del salto.
    /// </summary>
    [HarmonyPatch(typeof(BossDashAttack), "OnPenitentEntersArea")]
    class BossDashAttackProxyIgnore_Patch
    {
        public static bool Prefix(Collider2DParam e)
        {
            try
            {
                return !ProxyDamageGuard.MustIgnore(ProxyDamageGuard.ObjectOf(e),
                    "BossDashAttack.OnPenitentEntersArea");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] BossDashAttack: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// `WallEnemyProjectile.OnEnterAttackArea` — stessa forma (`Assembly-CSharp.cs:124087`), e qui il
    /// filtro e' **solo** il layer dell'`AttackArea`: nessun `CompareTag`. Il proiettile del nemico da
    /// muro, se entra sul proxy, va in eccezione allo stesso modo.
    /// </summary>
    [HarmonyPatch(typeof(WallEnemyProjectile), "OnEnterAttackArea")]
    class WallEnemyProjectileProxyIgnore_Patch
    {
        public static bool Prefix(Collider2DParam e)
        {
            try
            {
                return !ProxyDamageGuard.MustIgnore(ProxyDamageGuard.ObjectOf(e),
                    "WallEnemyProjectile.OnEnterAttackArea");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] WallEnemyProjectile: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// `PerpetuaBehaviour.AttackOnStartDash` — la forma «proxy come BERSAGLIO», non come collisore.
    /// `Perpetua.Target` lo scrive l'aggro della mod (`EnemySyncManager.ApplyAggroTarget`: quando l'altro
    /// e' piu' vicino, `enemy.Target = _remoteTargetProxy`), e il metodo fa
    /// `Perpetua.Target.GetComponentInParent&lt;IDamageable&gt;().Damage(...)` SENZA null-check
    /// (`Assembly-CSharp.cs:55147`): col proxy come target, il dash di Perpetua va in eccezione.
    /// </summary>
    [HarmonyPatch(typeof(PerpetuaBehaviour), "AttackOnStartDash")]
    class PerpetuaDashProxyIgnore_Patch
    {
        public static bool Prefix(PerpetuaBehaviour __instance)
        {
            try
            {
                if (__instance == null || __instance.Perpetua == null)
                    return true;

                return !ProxyDamageGuard.MustIgnore(__instance.Perpetua.Target,
                    "PerpetuaBehaviour.AttackOnStartDash");
            }
            catch (Exception exception)
            {
                ModLog.Error("[ProxyGuard] PerpetuaBehaviour: " + exception.GetType().Name + " - " + exception.Message);
                return true;
            }
        }
    }
}
