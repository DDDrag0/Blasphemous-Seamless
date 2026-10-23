using System;
using Blasphemous.ModdingAPI;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐⭐ L'ULTIMO PEZZO DELL'AGGIO: IL SALTO DEL BOSS SEGUE IL PROXY.
    ///
    /// IL DIFETTO, misurato (`scratch\DIAGNOSI-MEMORIA-NEMICI-E-AGGIO.md` §4.2, §(e)):
    ///
    ///     private Vector3 GetTargetPredictedPos()                       // Assembly-CSharp.cs:56000
    ///     {
    ///         Penitent penitent = Core.Logic.Penitent;                  // :56002  ⬅ IL PENITENT LOCALE
    ///         Vector2 vector = penitent.transform.position;
    ///         vector.x += penitent.GetVelocity().x;
    ///         return vector;
    ///     }
    ///
    /// e il salto e' l'UNICA locomozione di `ElderBrother`: `JumpAttackCoroutine` fa
    ///
    ///     Vector3 predictedPosition = GetTargetPredictedPos() + (Vector3)jumpOffset;   // :56023
    ///     jumpAttack.Use(base.transform, predictedPosition);                            // :56024
    ///
    /// mentre `GetTarget()` (`:103594-103602`) rispetta `Entity.Target`, cioe' il proxy d'aggio della mod
    /// scritto da `EnemySyncManager.ApplyAggroTarget`. Risultato sulla macchina che simula: il facing e
    /// l'attacco ad area guardano il peer (proxy), il salto torna sempre sul padrone del gioco ⇒
    /// «si gira per un attacco, poi torna a saltare sull'host»: la firma esatta del difetto.
    ///
    /// IL FIX, e non tocca nient'altro che il PUNTO DI MIRA:
    ///   - se `Entity.Target` NON e' il proxy, questo `Postfix` non scrive niente ⇒ il valore e' quello
    ///     che il gioco ha calcolato, bit per bit (stessa forma, stessa velocita', stesso z);
    ///   - se `Entity.Target` E' il proxy, si restituisce la posizione del proxy (x, y) tenendo lo z del
    ///     risultato originale (che il gioco ricava da un `Vector2`, quindi 0). La traiettoria NON cambia:
    ///     `BossJumpAttack.Use` (`:75212-75242`) proietta a terra il punto con un linecast 2D e costruisce
    ///     la bezier come prima; cambia solo DOVE atterra, che e' esattamente il difetto.
    ///
    /// ⚠️ LIMITE DICHIARATO (vedi `scratch\REFERTO-SALTO-PROXY.md`): il gioco anticipa il bersaglio di
    /// `penitent.GetVelocity().x`; il proxy non ha una velocita' propria, quindi sul proxy non c'e' alcun
    /// anticipo e la posizione e' quella che `ApplyAggroTarget` ha scritto all'ultimo giro di scansione
    /// (`RESCAN_INTERVAL = 0.5f`). Per un giocatore che corre il boss puo' atterrare qualche unita'
    /// indietro - meglio di un boss che salta sempre sull'host, che e' il difetto misurato.
    ///
    /// ⚠️ NIENTE `Kill()/Damage()/Death()/SetHealth()/Init()`, nessuno stato nuovo, nessuna scrittura:
    /// un `Postfix` che riscrive un `Vector3` di ritorno, e in caso di qualunque dubbio o eccezione si
    /// lascia il risultato originale (comportamento del gioco, identico a prima).
    ///
    /// ⭐ E DUE RIGHE DI MARKER, perche' senza quelle il fix e' INVISIBILE nel test: una quando il salto
    /// usa il PROXY, una quando usa l'ORIGINALE. Si leggono nei log delle due macchine e dicono, salto per
    /// salto, quale delle due strade e' stata presa e su quale punto e' atterrato.
    /// </summary>
    [HarmonyPatch(typeof(Gameplay.GameControllers.Bosses.ElderBrother.ElderBrotherBehaviour),
                  "GetTargetPredictedPos")]
    class ElderBrotherJumpAimProxy_Patch
    {
        internal const string Marker = "[SaltoProxy]";

        public static void Postfix(
            Gameplay.GameControllers.Bosses.ElderBrother.ElderBrotherBehaviour __instance,
            ref Vector3 __result)
        {
            try
            {
                Gameplay.GameControllers.Bosses.ElderBrother.ElderBrother boss =
                    __instance == null ? null : __instance.ElderBrother;

                GameObject target = boss == null ? null : boss.Target;

                if (target == null)
                {
                    // Nessun Target sul boss: il gioco non ha ancora deciso, e la mira originale e' gia'
                    // quella giusta. Non si tocca niente.
                    Log(__instance, false, "il boss non ha nessun Target", __result);
                    return;
                }

                if (!EnemySyncManager.IsRemoteTargetProxy(target))
                {
                    // Il caso NORMALE (single player, oppure il boss ha scelto il giocatore locale):
                    // `__result` e' quello del gioco, intatto.
                    Log(__instance, false, "il Target non e' il proxy", __result);
                    return;
                }

                // Il Target e' il proxy: si mira dove sta l'altro giocatore. Lo z resta quello del
                // risultato originale, cosi' l'unica differenza e' la posizione in x/y.
                Vector3 proxyPos = target.transform.position;
                __result = new Vector3(proxyPos.x, proxyPos.y, __result.z);

                Log(__instance, true, "il Target e' il proxy dell'altro giocatore", __result);
            }
            catch (Exception e)
            {
                // La mira resta quella del gioco: meglio il comportamento originale di un salto a caso.
                try
                {
                    ModLog.Error(Marker + " mira lasciata all'originale: " +
                                 e.GetType().Name + " - " + e.Message);
                }
                catch
                {
                }
            }
        }

        private static void Log(
            Gameplay.GameControllers.Bosses.ElderBrother.ElderBrotherBehaviour instance,
            bool usesProxy,
            string why,
            Vector3 aim)
        {
            try
            {
                int id = instance == null ? 0 : instance.GetInstanceID();
                string who = instance == null ? "ElderBrother n/d" : instance.name;

                // Il punto di mira e la posizione del GIOCATORE LOCALE nella stessa riga: cosi' il test
                // non deve incrociare altri log - se la riga dice PROXY, `punto` e' LONTANO dal
                // `penitenteLocale`, ed e' la prova che il salto non sta inseguendo il padrone.
                string where = "punto=(" + aim.x.ToString("F1") + ", " + aim.y.ToString("F1") + ")";

                Gameplay.GameControllers.Penitent.Penitent local = Framework.Managers.Core.Logic.Penitent;
                if (local != null && local.transform != null)
                {
                    Vector3 p = local.transform.position;
                    where += " penitenteLocale=(" + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ")" +
                             " distanza=" +
                             Vector2.Distance(new Vector2(aim.x, aim.y), new Vector2(p.x, p.y)).ToString("F1");
                }

                if (usesProxy)
                {
                    ModLog.Info(Marker + " il salto mira al PROXY dell'altro giocatore: " +
                                who + " istanza=" + id + " " + where);
                }
                else
                {
                    ModLog.Info(Marker + " il salto mira al PENITENT locale come il gioco (" +
                                why + "): " + who + " istanza=" + id + " " + where);
                }
            }
            catch
            {
                // Il marker non deve mai fermare un salto.
            }
        }
    }
}
