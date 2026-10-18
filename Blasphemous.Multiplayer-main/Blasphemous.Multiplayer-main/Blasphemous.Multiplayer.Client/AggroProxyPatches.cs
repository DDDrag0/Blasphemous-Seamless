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
    /// find the other player. Since step ② it DOES carry an IDamageable (CoopRemoteTargetDamageable, which
    /// forwards the blow to the peer) — but NOT for this path, and the exception is deliberate. A contact
    /// attack is armed by OnTriggerEnter2D:
    ///
    ///     IDamageable c = other.GetComponentInParent&lt;IDamageable&gt;();
    ///     ...
    ///     EntityContactDamage(c);          ->  EnemyAttack.ContactAttack(c)  ->  c.Damage(ContactHit)
    ///
    /// Two things go wrong when that `other` is the proxy, and this prefix closes both:
    ///   - `c` used to be null (the proxy had no IDamageable) and the call was made anyway, throwing a
    ///     NullReferenceException out of a Unity trigger callback;
    ///   - even before that, IsTargetOverlapped has already been raised, and the attacks that read it
    ///     strike Core.Logic.Penitent without measuring any distance - so the LOCAL player, standing
    ///     twenty units away, took a hit from an enemy that only ever "felt" the proxy.
    ///
    /// Returning false skips the original method, so the proxy is simply not a damageable contact.
    ///
    /// ⚠️ WHY THIS ONE IS *NOT* DIVERTED LIKE THE FOUR BELOW. Sending contact damage through the proxy
    /// would mean letting the original run, and the original is precisely the code that raises
    /// IsTargetOverlapped before damaging anything. Opening it again would re-open the measured defect
    /// that `c89c368` closed (the LOCAL player taking a hit from twenty units away). ⇒ enemies whose only
    /// way to hurt someone is `DamageByContact` still cannot hurt the peer. Declared, not forgotten.
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
    // ⭐⭐ IL COLPO DEL BOSS CHE MIRA AL PROXY NON SI PERDE PIU' — adesso lo PRENDE IL PROXY, e lo porta
    // al peer.
    //
    // STORIA DI QUESTO BLOCCO, in due passi, perche' il secondo ha cambiato il significato del primo:
    //   ① (`c89c368`) il colpo che mirava al proxy veniva SALTATO: tolta l'eccezione, zero danno. Il
    //     difetto era che `IDamageable` sul proxy non c'era, quindi il gioco o lanciava
    //     (`BossJumpAttack.OnPenitentEntersArea`, nessun null-check) o scartava il colpo in silenzio.
    //   ② (questo) il proxy E' danneggiabile (`CoopRemoteTargetDamageable`, EnemyHitPeerSync.cs) e
    //     INOLTRA il colpo al peer. ⇒ la guardia non deve piu' SALTARE il colpo: deve LASCIARLO PASSARE
    //     fino al proxy e fermarlo li'. Il colpo non va al `Penitent` locale — quello non e' mai stato
    //     in gioco: il proxy sta dove sta l'altro giocatore.
    //
    // LA MISURA, dai log dell'host: `BossJumpAttack.OnPenitentEntersArea` fa
    //
    //     GameObject gameObject = e.Collider2DArg.gameObject;             // Assembly-CSharp.cs:75348
    //     _hit.OnGuardCallback = OnGuard;                                 //             :75349
    //     gameObject.GetComponentInParent<IDamageable>().Damage(_hit);    //             :75350
    //
    // Il filtro che il proxy attraversa e' solo di layer: `AttackArea.OnTriggerEnter2D` fa
    // `if ((enemyLayerMask.value & (1 << col.gameObject.layer)) > 0)` (`:125922-125928`), e il proxy e'
    // sul layer `Penitent` — cioe' esattamente quello che il mask contiene.
    //
    // ⚠️ LA GUARDIA RESTA, E SERVE: la via 2 (`GetComponentInParent<IDamageable>() == null`) resta la
    // rete di sicurezza per qualunque ALTRO oggetto nudo sul layer `Penitent` (di un'altra mod, o di
    // un futuro senza inoltro), e la via 1 protegge il caso in cui il componente mancasse (proxy creato
    // da una build precedente, `AddComponent` fallito): in quel caso il colpo viene ancora SALTATO,
    // non fatto esplodere.
    //
    // ⚠️ E NON SI TOCCA IL DANNO NORMALE: un `IDamageable` VERO (il `Penitent` locale) non e' il proxy
    // ⇒ la patch non fa niente e il colpo va esattamente come prima.
    //
    // I QUATTRO SITI (tutti quelli raggiungibili dal proxy nella direzione nemico -> giocatore, con
    // `GetComponentInParent<IDamageable>().Damage()`). Gli altri tre della stessa forma
    // (`CowardTrap.OnEnter:97649`, `JumperAttack:107898`, `ReekLeaderAttack:118785`) sono dietro un
    // `CompareTag("Penitent")` che il proxy NON soddisfa (nasce `Untagged`): non lo toccano.
    //
    // ⚠️ COSTO: **zero bersagli Harmony nuovi** (restano 268: i quattro `Prefix` sono quelli di
    // `c89c368`, cambia solo cosa rispondono), e il proxy prende un componente in piu'.
    // =====================================================================================================

    /// <summary>
    /// La decisione, in un posto solo: «questo colpo lo prende il proxy (e va al peer), oppure non ha
    /// dove atterrare?»
    ///
    /// Tre vie:
    ///   1. **identita' + inoltro** — `EnemySyncManager.IsRemoteTargetProxy` confronta il RIFERIMENTO
    ///      con `_coop_remote_target` (piu' `IsChildOf`): nessun nome, nessun tag, nessun layer da
    ///      fidarsi. Se il proxy ha il suo `IDamageable` (l'inoltro), il colpo si **lascia passare**
    ///      (`false`): lo prendera' il proxy, che non tocca il `Penitent` locale e lo manda al peer.
    ///   2. **identita' senza inoltro** — e' il proxy ma il componente non c'e' (non ancora aggiunto,
    ///      o aggiunta fallita): si SALTA come prima, perche' il gioco andrebbe in eccezione sul null.
    ///   3. **struttura** — `GetComponentInParent&lt;IDamageable&gt;()` nullo: e' il caso che il gioco non
    ///      gestisce. Copre qualunque altro oggetto nudo finito sul layer `Penitent`.
    ///
    /// ⚠️ In caso di dubbio NON si rompe niente: se la guardia stessa si rompe, si torna al
    /// comportamento di prima (`false` = la patch non fa niente, il gioco prosegue).
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

        /// <summary>
        /// True quando il colpo va SALTATO: il bersaglio e' il proxy SENZA inoltro, o non e' danneggiabile.
        /// False quando il colpo ha dove atterrare (il `Penitent` locale, oppure il proxy che inoltra).
        /// </summary>
        internal static bool MustIgnore(GameObject target, string site)
        {
            try
            {
                if (target == null)
                {
                    Report(site, "il bersaglio e' nullo");
                    return true;
                }

                bool isProxy = EnemySyncManager.IsRemoteTargetProxy(target);

                if (isProxy)
                {
                    // ⭐ IL PUNTO DEL PASSO ② — e la differenza con `c89c368` sta tutta qui.
                    if (target.GetComponentInParent<IDamageable>() != null)
                    {
                        ReportDiverted(site);
                        return false; // il colpo DEVE arrivare al proxy: e' lui che lo porta al peer
                    }

                    Report(site, "il bersaglio E' il proxy ma non ha l'inoltro (IDamageable assente)");
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

        /// <summary>
        /// La riga del passo ②: il colpo NON e' saltato, e' DIROTTATO. Non e' una riga di errore, e il
        /// verbo e' cambiato apposta: "SALTATO" descriveva un colpo perso, "DIROTTATO" descrive un
        /// colpo che ha preso la strada del peer. Se nei log si legge ancora "SALTATO" su questo sito,
        /// l'inoltro non e' in esecuzione (componente assente / DLL vecchia).
        /// </summary>
        private static void ReportDiverted(string site)
        {
            try
            {
                ModLog.Info(Marker + " colpo DIROTTATO (" + site + "): il bersaglio e' il proxy, " +
                            "il colpo va al peer (il Penitent locale non viene toccato)");
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// `BossJumpAttack.OnPenitentEntersArea` — IL sito misurato (il salto dell'ElderBrother).
    /// Con il proxy danneggiabile (passo ②) il `Prefix` fa passare il colpo, che il proxy inoltra al
    /// peer; senza l'inoltro lo salterebbe, perche' `GetComponentInParent&lt;IDamageable&gt;()` sarebbe
    /// nullo e `Assembly-CSharp.cs:75350` andrebbe in `NullReferenceException`.
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
    /// Il dash di Quirce/Perpetua ha lo stesso buco del salto, e adesso lo stesso inoltro.
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
