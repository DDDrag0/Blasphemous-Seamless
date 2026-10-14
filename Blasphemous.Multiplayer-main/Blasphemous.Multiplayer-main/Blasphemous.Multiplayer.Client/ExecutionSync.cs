using System;
using Blasphemous.ModdingAPI;
using Gameplay.GameControllers.Combat;
using Gameplay.GameControllers.Entities;
using HarmonyLib;

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
            }
            catch (Exception e)
            {
                ModLog.Error("[Exec TX] annuncio fallito: " + e.GetType().Name + " - " + e.Message);
            }
        }
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
}
