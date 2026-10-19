using System;
using Blasphemous.ModdingAPI;
using Gameplay.GameControllers.Bosses.Quirce.Attack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    // =====================================================================================================
    // I DUE BUCHI DEL RELAY CHE **NON** PASSANO DAL `PoolManager` — e i due `Postfix` che li chiudono.
    //
    // IL FATTO. `EffectRelayPoolHook` (`EffectRelay.cs:86-94`) vede OGNI effetto che il gioco prende da un
    // pool, perche' `PoolManager.ReuseObject` e' l'unico varco di quel canale. Ma due metodi dei boss
    // **non passano di li'**: chiamano `Object.Instantiate` grezzo, e per il relay sono invisibili:
    //   · `BossJumpAttack.InstantiateArea(GameObject)`   — `BossJumpAttack.cs:269-277`
    //     *(1 solo `Instantiate`, 1 solo chiamante vivo: `OnLanded` -> `instantiateOnEnd`.
    //      `instantiateOnStart` e' dichiarato ma **mai letto**: e' un campo MORTO. Vedi REFERTO)*
    //   · `BossTeleportAttack.InstantiateArea(GameObject)` — `BossTeleportAttack.cs:73-81`
    //     *(`OnTeleportOut` -> `instantiateOnTeleportOut`, `OnTeleportIn` -> `instantiateOnTeleportIn`)*
    // Corpo identico, 6 righe: `Object.Instantiate(prefab, _parentToMove.position, Quaternion.identity)` +
    // `GetComponent<BossSpawnedAreaAttack>()` e, se il cast riesce, `SetOwner(base.EntityOwner)`.
    // Sono i **buchi 1 e 2** della voce 7 di `BUG-NOTI`.
    //
    // ⛔ E NON SI DUPLICA NIENTE. Il mittente del relay e' **una funzione** che ha gia' tutto dentro —
    // filtro (`Weapon|Projectile`), freno (20/s), dedup (`prefab+posizione`, 0,5 s), payload, invio:
    // `EffectRelay.OnPoolSpawn(prefab, position, rotation)` (`EffectRelay.cs:423-479`). Questi due hook
    // la **chiamano**, e basta. Il ricevente non si tocca: la copia nasce dal suo pool, con
    // `ApplyFromWire` -> `Resolve` -> `ReuseObject`, e il `_applyingFromWire` del relay impedisce il
    // rimbalzo all'infinito.
    //
    // ⚠️ LA POSIZIONE NON E' NELLA FIRMA. La firma e' `private void InstantiateArea(GameObject)`: niente
    // ritorno, niente posizione. La posizione vera sta in `_parentToMove`, un campo **privato**
    // (`Transform`, scritto in `Use`). La si prende dall'istanza del componente:
    // `__instance.transform.position`. ⭐ **Coincide in 13 siti su 14**, perche' i chiamanti passano
    // `base.transform` del behaviour che sta sulla radice del boss. ⚠️ **IL 14º SITO E' `JarThrower`**:
    // `Assembly-CSharp.cs:107392` chiama `JumpAttack.Use(JarThrower.transform, ...)`, cioe' passa il
    // transform **del lanciatore**, non quello del padre a cui il salto appartiene. ⇒ per QUEL
    // chiamante la posizione trasmessa e' quella del `JarThrower`: e' un limite dichiarato, non un
    // errore da correggere qui (il `JarThrower` non e' un boss e non e' fra i 4 che si testano).
    // La `rotation` invece e' quella scritta nel codice del gioco: `Quaternion.identity`.
    //
    // ⚠️ IL PREFAB E' UNA COSA CHE IL RICEVENTE SA GIA' RISOLVERE, e non per fortuna: e' un campo
    // **pubblico `GameObject` del componente del boss** (`instantiateOnEnd`, `instantiateOnTeleportIn/Out`)
    // e `EffectRelay.Harvest` scandisce per costruzione tutti i campi `GameObject` di tutti i componenti
    // del boss, pubblici e privati. ⇒ il catalogo del peer quel nome ce l'ha **da solo**; qui mancava
    // solo il mittente.
    //
    // 🔴 IL RISCHIO, DICHIARATO: il filtro del mittente e' `Weapon ∨ Projectile` (`IsInteresting`,
    // `EffectRelay.cs:1429`). **Se il prefab trascinato in uno di quei quattro campi e' cosmetico
    // (nessuna `Weapon`, nessun `Projectile`), `OnPoolSpawn` non manda niente**: l'hook scatta, scrive
    // la sua riga, e il pacchetto non parte. ⇒ l'hook e' **morto ma innocuo** — nessun danno al gioco,
    // nessun traffico in piu'. E lo dira' il marker: `RISOLTO` sul peer **senza** `istanzio` = cosmetico.
    // (Per l'unico boss misurato, `ElderBrother`, i campi sono `<null>`: non esercitato. Vedi REFERTO.)
    //
    // IL COSTO: **270 → 272 bersagli Harmony** (2 metodi privati nuovi). Nient'altro: nessuna riga di
    // protocollo, nessun tipo nuovo nel wire, nessuna patch tolta.
    //
    // ⛔ COSA QUESTI HOOK NON FANNO E NON DEVONO FARE: nessun `Kill/Damage/Death/SetHealth/Init`, nessun
    // `TimeScale`, nessun `Object.Instantiate` in piu', nessuna scrittura sul prefab, nessun tocco al
    // `_parentToMove` ne' al transform. E tutto in `try/catch`: **un hook non deve mai rompere un
    // attacco** — se questo file esplode, il salto e il teletrasporto del boss devono restare identici.
    // =====================================================================================================

    /// <summary>
    /// `BossJumpAttack.InstantiateArea(GameObject)` — l'area di fine salto (ElderBrother, Amanecidas,
    /// JarThrower). Il `Postfix` gira **dopo** che il gioco ha istanziato: la sua area esiste gia', qui
    /// si avvisa solo il relay.
    /// </summary>
    [HarmonyPatch(typeof(BossJumpAttack), "InstantiateArea")]
    internal static class EffectRelayJumpAreaHook
    {
        /// <summary>
        /// ⚠️ IL NOME DEL PARAMETRO E' CONTRATTUALE: Harmony lo inietta **per nome** contro la firma
        /// reale del gioco (`InstantiateArea(GameObject toInstantiate)`). Verificato sul decompilato e
        /// sulla DLL del gioco con Cecil: un parametro, chiamato `toInstantiate`, nessun overload.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(BossJumpAttack __instance, GameObject toInstantiate)
        {
            EffectRelayInstantiateArea.TryRelay(EffectRelayInstantiateArea.SiteJump, __instance, toInstantiate);
        }
    }

    /// <summary>
    /// `BossTeleportAttack.InstantiateArea(GameObject)` — l'area del teletrasporto (Lesmes, Quirce).
    /// Due chiamanti: `OnTeleportOut` e `OnTeleportIn`; il metodo e' uno solo, e il `Postfix` scatta una
    /// volta per chiamata, quindi non serve distinguerli.
    /// </summary>
    [HarmonyPatch(typeof(BossTeleportAttack), "InstantiateArea")]
    internal static class EffectRelayTeleportAreaHook
    {
        [HarmonyPostfix]
        private static void Postfix(BossTeleportAttack __instance, GameObject toInstantiate)
        {
            EffectRelayInstantiateArea.TryRelay(EffectRelayInstantiateArea.SiteTeleport, __instance, toInstantiate);
        }
    }

    /// <summary>
    /// Il corpo in comune ai due `Postfix`: **due righe** — il marker e la chiamata a
    /// <see cref="EffectRelay.OnPoolSpawn"/>. Non c'e' nessuna logica del relay qui dentro (ne' filtro,
    /// ne' freno, ne' dedup, ne' payload): tutta quella roba ha **una sola casa**, e non e' questo file.
    /// </summary>
    internal static class EffectRelayInstantiateArea
    {
        /// <summary>I due nomi che finiscono nel marker (`da=…`): sono **contrattuali** per il test.</summary>
        internal const string SiteJump = "BossJump";

        internal const string SiteTeleport = "BossTeleport";

        /// <summary>
        /// ⚠️ IL MARKER SI SCRIVE **PER CONCATENAZIONE**, non con `$"{...}"`: l'interpolazione diventa
        /// una `string.Format` e nella DLL finirebbe `"{0} …"`, cioe' un testo che `marker-check.ps1`
        /// (che cerca i byte) **non trova**. E' la regola dei marker di `docs\TODO.md` (§④ del `17:20`):
        /// `MARK + " …"` e' una costante a sua volta e finisce nella DLL **come un letterale solo**.
        /// </summary>
        internal const string Marker = "[FxRelay] hook InstantiateArea: prefab=";

        /// <summary>
        /// Chiamata dai due `Postfix`. **Non lancia mai**: un hook che rompe un attacco e' un difetto
        /// peggiore dell'effetto che non arriva.
        ///
        /// ⚠️ La riga si scrive **prima** della chiamata, di proposito: cosi' il log dice **che l'hook e'
        /// scattato anche quando il filtro del mittente scarta il prefab**. La differenza fra
        /// «`hook … prefab=X`» seguita da «`mando … X`» e la stessa riga **senza** il `mando` e' proprio
        /// la misura che serve: la prima e' un effetto trasmesso, la seconda un **cosmetico**.
        /// </summary>
        internal static void TryRelay(string site, Component owner, GameObject prefab)
        {
            try
            {
                if (prefab == null || owner == null)
                    return;

                // La posizione: non e' nella firma del metodo (vedi il commento in testa al file).
                Vector3 position = owner.transform.position;

                // Il marker, in un `try/catch` suo: se il log esplodesse non deve portarsi via l'invio.
                try
                {
                    ModLog.Info(Marker + "\"" + prefab.name + "\" da=" + site);
                }
                catch (Exception)
                {
                }

                // ⭐ LA FUNZIONE CHE IL RELAY HA GIA': filtro + freno + dedup + payload + invio.
                EffectRelay.OnPoolSpawn(prefab, position, Quaternion.identity);
            }
            catch (Exception exception)
            {
                try
                {
                    ModLog.Error("[FxRelay] hook InstantiateArea/" + site + ": " +
                                 exception.GetType().Name + " - " + exception.Message);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
