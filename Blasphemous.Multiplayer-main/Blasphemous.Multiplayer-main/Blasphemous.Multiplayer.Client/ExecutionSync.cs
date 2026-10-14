using System;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;    // EffectType
using Gameplay.GameControllers.Combat;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using Tools.Level.Interactables;                    // Execution

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// A6 - LE ESECUZIONI. Il settimo prefisso del canale EnemyDeathGlobal.
    ///
    /// PERCHE' QUESTO CANALE E NON UN NetworkType NUOVO. EnemyDeathGlobal e' l'unico canale che il server
    /// inoltra a TUTTI tranne il mittente senza filtro di scena, e il suo payload e' una STRINGA UTF-8 che
    /// il server non legge e non valida (Server.cs:807-819). Un'eccezione, un id, una direzione: tutto
    /// entra. Un NetworkType nuovo costerebbe il bump di PROTOCOL_VERSION da 11 a 12 su DUE costanti
    /// (Server.cs:863 e NetworkManager.cs:665) piu' un redeploy simultaneo di entrambe le macchine e del
    /// server - che RIFIUTA la versione diversa a Server.cs:509 - e non trasporterebbe UN SOLO byte in
    /// piu' di quanto questa stringa non porti gia'.
    ///
    /// PERCHE' NON C'E' ECO. Il server esclude il mittente, quindi l'annuncio non torna mai indietro. La
    /// guardia su IsApplyingRemoteDamage chiude l'unica porta che restava: un colpo con
    /// DamageType.OptionalStunt (MiriamPortalPrayerAttack.cs:60 - e' l'UNICO produttore di quel tipo in
    /// tutto l'assembly) puo' arrivare a questa macchina GIA' relayato, e senza la guardia
    /// InstantiateExecution girerebbe di nuovo qui e il SUONO tornerebbe al mittente, che l'ha gia'
    /// sentito dal gioco. Il tipo Stunt, che e' quello che aprirebbe il gate in modo pulito, NON e'
    /// prodotto da nessun attaccante del gioco (verificato: DamageType.Stunt compare solo dentro
    /// Enemy.cs e LevelSleepTime.cs, mai come valore assegnato a un Hit).
    /// </summary>
    public static class ExecutionSync
    {
        /// <summary>
        /// Il settimo prefisso della serie: sp| (morti), brk| (lampade), falo| (falo'),
        /// pdead|/palive| (giocatori), nato| (nascita), ENEMYDEAD| (costante morta), e questo.
        /// </summary>
        public const string ExecPrefix = "exec|";

        /// <summary>True quando questo id e' un annuncio di esecuzione e non una morte.</summary>
        public static bool IsExecutionId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(ExecPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// L'id del nemico dentro un annuncio di esecuzione. Null per qualunque cosa malformata - e il
        /// null e' voluto: un id vuoto farebbe entrare la stringa nella memoria delle morti e
        /// MarkSpawnPointConsumed la scarterebbe in silenzio, lasciando il fix dipendente dal fatto che
        /// nessuno la legga mai.
        /// </summary>
        public static string IdOf(string id)
        {
            if (!IsExecutionId(id))
                return null;

            string enemyId = id.Substring(ExecPrefix.Length);
            return string.IsNullOrEmpty(enemyId) ? null : enemyId;
        }

        /// <summary>
        /// Annuncia che QUESTO gioco ha appena messo un nemico in stato eseguibile.
        ///
        /// PERCHE' InstantiateExecution E NON Behaviour.Execution(). Il gioco ha SEDICI override di
        /// Behaviour.Execution() - uno per tipo eseguibile - e TUTTI E SEDICI finiscono sullo stesso
        /// metodo di EntityExecution. Patchare il choke point invece dei sedici chiamanti e' la
        /// differenza fra una patch e sedici copie di una stessa riga, cioe' sedici posti in cui una
        /// puo' divergere.
        ///
        /// E InstantiateExecution() NON HA ARGOMENTI. Harmony lega i parametri delle patch PER NOME, e
        /// un nome sbagliato ha gia' impedito alla mod di avviarsi: con zero argomenti quel rischio non
        /// esiste.
        /// </summary>
        public static void Announce(EntityExecution execution)
        {
            try
            {
                if (execution == null)
                    return;

                if (!Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                // Un colpo relayato da questa macchina non si ri-annuncia: vedi il commento della classe.
                if (EnemySyncManager.IsApplyingRemoteDamage)
                    return;

                Enemy enemy = execution.EntityOwner as Enemy;

                string id;
                if (!EnemySyncManager.TryGetEnemyId(enemy, out id))
                {
                    // Osservazione, non un verdetto: un nemico che non e' in _ids non e' annunciabile su un
                    // canale che identifica per stringa. Senza questa riga l'assenza dell'annuncio e
                    // l'assenza del nemico sarebbero indistinguibili.
                    ModLog.Info("[Exec TX] esecuzione su un nemico senza id di rete: nessun annuncio");
                    return;
                }

                // PROVA LATO MITTENTE, ed e' obbligatoria per la stessa ragione dei marker di A5: la riga
                // [Exec RX] la scrive il RICEVENTE, e QueueMesssage esce in silenzio mentre e' disconnesso.
                // Senza questa riga "il Postfix non e' mai girato" e "il pacchetto non e' mai arrivato"
                // restano INDISTINGUIBILI in entrambi i log. L'id e' lo stesso che comparira' a
                // destinazione: e' quello che rende le due righe confrontabili.
                ModLog.Info($"[Exec TX] {id}");

                Main.Multiplayer.SendEnemyDeathGlobal(ExecPrefix + id);

                // A6 - IL FILO FRA INIZIO E FINE. `InstantiateExecution` (qui) e `InteractionEnd` (la patch in
                // fondo al file) sono DUE metodi di DUE classi diverse, e l'oggetto che finisce non e' questo:
                // e' la prop FIGLIA che `InstantiateExecution` ha appena istanziato.
                //   ⚠️ SI PRENDE DAL MONDO, NON DALLA PROP. `__instance` e' l'`EntityExecution` del NEMICO;
                //   l'`Execution` che ci interessa e' un componente della prop, e in questo istante la prop e'
                //   GIA' STATA ISTANZIATA da `InstantiateExecution` (e' un postfix: il corpo del gioco e' gia'
                //   girato per intero).
                //   `FindObjectOfType` invece di una ricerca dentro la prop perche' la prop non e' raggiungibile
                //   da `__instance` in modo pubblico. Ed e' legittimo proprio qui: in questo istante ce n'e' UNA
                //   SOLA, quella appena nata. E' l'unico uso di `FindObjectOfType` in questo file.
                PendingExecution = UnityEngine.Object.FindObjectOfType<Execution>();

                // ⚠️ LA GUARDIA, E NON E' PIGNOLERIA. Se la prop non si e' trovata, l'inizio partirebbe e
                // NESSUNO potrebbe fermarlo: `AnnounceEnd` uscirebbe alla prima riga per sempre, e l'unica via
                // di ripristino resterebbe il fusibile. Cioe': si sceglierebbe di far partire un'animazione
                // sapendo di non poterla spegnere. Con questa riga, invece, non parte.
                if (PendingExecution == null)
                {
                    ModLog.Warn("[ExecAnim TX] la prop dell'esecuzione non si e' trovata: " +
                                "l'animazione non parte (e non e' un problema di rete)");
                    return;
                }

                // PROVA LATO MITTENTE, accanto a `[Exec TX]`: la riga `[Effect] ExecutionStart` la scrive il
                // RICEVENTE. Senza questa, "il postfix non e' girato" e "il pacchetto non e' arrivato" restano
                // INDISTINGUIBILI in entrambi i log.
                ModLog.Info("[ExecAnim TX] START");
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.ExecutionStart);
            }
            catch (Exception e)
            {
                ModLog.Error("[Exec TX] annuncio fallito: " + e.GetType().Name + " - " + e.Message);
            }
        }

        /// <summary>
        /// A6 - LA FINE DELL'ESECUZIONE. Annuncia che l'animazione e' finita, cosi' il ricevente rimette
        /// l'avatar come stava.
        ///
        /// PERCHE' UN SECONDO CANALE, e non un ottavo prefisso su `EnemyDeathGlobal`. Il canale della morte NON
        /// PORTA IL MITTENTE: lo dice il commento di `ApplyRemoteExecution` («EnemyDeathGlobal non porta il
        /// mittente ... quindi un nome qui sarebbe inventato»). Per il SUONO e la contabilita' della morte non
        /// serve: sono effetti globali, uguali per tutti. Per l'ANIMAZIONE serve ECCOME: si suona SULL'AVATAR
        /// DI CHI HA ESEGUITO, e senza un nome non si sa quale avatar.
        ///   ⇒ Si usa `NetworkType.Effect`, che il server inoltra CON IL NOME DEL MITTENTE prefissato e che
        ///   `AttackManager.ReceiveEffect` risolve con `FindActivePlayer(playerName)` - la stessa rotta di
        ///   A3/A4/A5.
        ///
        /// E NON SERVE UN BUMP: `SendEffect(EffectType)` scrive UN byte, e 105/106 sono liberi.
        /// </summary>
        public static void AnnounceEnd()
        {
            try
            {
                Execution current = PendingExecution;
                PendingExecution = null;

                if (current == null)
                {
                    // Osservazione, non un verdetto. Se la fine arriva senza che l'inizio sia stato annunciato,
                    // le due cose possibili sono "l'inizio non e' mai partito" e "l'inizio e' partito e questo
                    // e' un secondo annuncio di fine": senza questa riga sarebbero indistinguibili, che e' la
                    // regola del progetto per ogni marker.
                    ModLog.Info("[ExecAnim TX] END senza inizio annunciato: nessun pacchetto");
                    return;
                }

                if (!Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                if (EnemySyncManager.IsApplyingRemoteDamage)
                    return;

                // PROVA LATO MITTENTE, ed e' obbligatoria per la stessa ragione di `[Exec TX]`: la riga
                // `[Effect] ExecutionEnd` la scrive il RICEVENTE e `QueueMesssage` esce in silenzio mentre e'
                // disconnesso. Senza questa riga "la patch non e' mai girata" e "il pacchetto non e' arrivato"
                // restano INDISTINGUIBILI in entrambi i log.
                ModLog.Info("[ExecAnim TX] END");

                Main.Multiplayer.NetworkManager.SendEffect(EffectType.ExecutionEnd);
            }
            catch (Exception e)
            {
                ModLog.Error("[ExecAnim TX] fine fallita: " + e.GetType().Name + " - " + e.Message);
            }
        }

        /// <summary>
        /// L'esecuzione in corso su QUESTA macchina, o null.
        ///
        /// ⚠️ PERCHE' UN CAMPO STATICO E NON UN PARAMETRO. L'inizio e la fine sono DUE metodi del gioco
        /// diversi, in DUE classi diverse (`EntityExecution.InstantiateExecution` e
        /// `Execution.InteractionEnd`), e fra i due non c'e' alcuna pila che li tenga insieme: l'`Execution`
        /// che finisce non e' lo stesso oggetto che ha chiamato `InstantiateExecution` - e' la prop FIGLIA che
        /// quello ha istanziato. Il filo che li lega e' questo campo.
        ///
        /// ⚠️ E' UN RIFERIMENTO A UN GameObject, QUINDI PUO' DIVENTARE UN OGGETTO MORTO. Un oggetto Unity
        /// distrutto NON e' `null` in C#: si confronta uguale a null, e toccarlo lancia. Per questo ogni uso e'
        /// guardato, e per questo `AnnounceEnd` lo azzera SEMPRE, anche quando non spedisce.
        ///
        /// UNO SOLO, e non una mappa. Il gioco non puo' avere due esecuzioni in corso sulla stessa macchina:
        /// e' una cutscene, e nasconde il penitent. Una mappa sarebbe struttura per un caso che non esiste.
        /// </summary>
        private static Execution PendingExecution;
    }

    /// <summary>
    /// IL BERSAGLIO. EntityExecution.InstantiateExecution() e' il punto in cui il nemico diventa
    /// eseguibile, ed e' UNO SOLO per tutti e sedici i tipi eseguibili del gioco.
    ///
    /// E' un POSTFIX, quindi il metodo del gioco e' gia' girato INTATTO: nessun valore di ritorno
    /// toccato, nessuno stato cambiato, nessun effetto collaterale alterato. La regola del progetto sui
    /// Prefix di AnimationFixes.cs - il cui VALORE DI RITORNO e' il punto, e la cui riscrittura ha gia'
    /// causato 12 NRE - non viene sfiorata, perche' qui non c'e' alcun Prefix.
    /// </summary>
    [HarmonyPatch(typeof(EntityExecution), "InstantiateExecution")]
    class EntityExecutionInstantiate_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(EntityExecution __instance)
        {
            ExecutionSync.Announce(__instance);
        }
    }

    /// <summary>
    /// A6 - LA FINE. `Tools.Level.Interactables.Execution.InteractionEnd()` e' il punto in cui il GIOCO sa che
    /// l'esecuzione e' finita, ed e' un punto di osservazione esatto, non una stima.
    ///
    /// ⚠️ COME LO SA IL GIOCO, e perche' e' lo stesso momento che serve a noi. `OnUse` entra nell'animazione
    /// del fantasma e poi ASPETTA:
    ///     interactorAnimator.SetTrigger("USED");
    ///     while (!base.Consumed) { yield return lapse; }
    /// e `Consumed` diventa vero in `InteractionEnd()`, chiamato da
    /// `Interactable.OnEventLaunched("INTERACTION_END")`, che a sua volta arriva da
    /// `Framework.Util.AnimatorEvent.LaunchEvent(string)` - un componente che la prop ha sul fantasma.
    ///   ⇒ "INTERACTION_END" e' il nome con cui il GIOCO dice "l'animazione e' finita". Noi non possiamo
    ///   ascoltarlo (e' un evento del fantasma, dentro la prop, che qui non esiste) - ma possiamo farcelo dire
    ///   dal mittente, e questo e' il punto in cui glielo si fa dire.
    ///
    /// ⚠️ PERCHE' UN POSTFIX E NON UN PREFIX. Due ragioni, e la seconda e' quella che conta.
    ///   1. E' la stessa disciplina della patch esistente: il metodo del gioco gira INTATTO, nessun valore di
    ///      ritorno toccato, nessuno stato alterato.
    ///   2. ⚠️ `InteractionEnd()` TOCCA IL PENITENT LOCALE:
    ///          Core.Logic.Penitent.IsOnExecution = false;
    ///          GhostTrailGenerator.AreGhostTrailsAllowed = true;
    ///      piu' `AddFervourBonus()`, che scrive `Penitent.Stats.Fervour.Current`. Sono le ricompense e i flag
    ///      di CHI HA ESEGUITO, su QUESTA macchina, e devono girare: e' il gioco. Un postfix le lascia girare
    ///      e si limita a osservare.
    ///
    /// ⚠️ E IL METODO E' `protected`. Non e' un ostacolo: Harmony risolve il bersaglio per nome e `PatchAll`
    /// non guarda la visibilita'. Ed `InteractionEnd()` NON HA ARGOMENTI: ⇒ il rischio "nome di parametro
    /// sbagliato" non esiste.
    /// </summary>
    [HarmonyPatch(typeof(Execution), "InteractionEnd")]
    class ExecutionInteractionEnd_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Execution __instance)
        {
            ExecutionSync.AnnounceEnd();
        }
    }
}
