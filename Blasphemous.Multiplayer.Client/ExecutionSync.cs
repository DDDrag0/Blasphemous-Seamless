using System;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;    // EffectType
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
    /// tutto l'assembly) puo' arrivare a questa macchina GIA' relayato, e senza la guardia l'annuncio
    /// girerebbe di nuovo qui e il SUONO tornerebbe al mittente, che l'ha gia' sentito dal gioco. Il tipo
    /// Stunt, che e' quello che aprirebbe il gate in modo pulito, NON e' prodotto da nessun attaccante del
    /// gioco (verificato: DamageType.Stunt compare solo dentro Enemy.cs e LevelSleepTime.cs, mai come
    /// valore assegnato a un Hit).
    ///
    /// ⭐ E IL PUNTO DI PARTENZA E' `Execution.OnUse`, NON PIU' `EntityExecution.InstantiateExecution`.
    /// La ragione intera sta nel commento di `Announce` e in quello della sua patch: il primo metodo
    /// significa "il nemico e' DIVENTATO eseguibile" (e il giocatore puo' non premere mai), il secondo
    /// significa "il giocatore HA PREMUTO". Solo il secondo e' un'esecuzione.
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
        /// Annuncia che QUESTO gioco ha appena ESEGUITO un nemico - cioe' che il giocatore ha premuto.
        ///
        /// ⚠️⚠️ PERCHE' NON PIU' `EntityExecution.InstantiateExecution()`, e questa e' la correzione.
        /// `InstantiateExecution()` NON significa "il giocatore esegue": e' chiamato da
        /// `Enemy.Execution(Hit)` -> `Behaviour.Execution()` nel momento in cui il nemico DIVENTA
        /// ESEGUIBILE (per prossimita', vita o stun). Da li' in poi il giocatore PUO' premere - e puo'
        /// anche non premere MAI.
        ///   ⇒ Annunciare li' significava dire al peer "si sta eseguendo un nemico" nel momento sbagliato,
        ///   e i due sintomi misurati sono esattamente quelli:
        ///     1. il peer fa partire il FANTASMA (l'animazione dell'esecuzione, con un penitent IN PIU' e
        ///        con lo sprite del remoto) anche se nessuno ha premuto;
        ///     2. su una macchina il nemico resta VIVO e sull'altra muore, perche' la morte arriva solo
        ///        con `Execution.OnUse` -> `ExecutedEntity.KillEntity()`.
        ///
        /// ⭐ IL PUNTO GIUSTO E' `Execution.OnUse`, che e' il metodo in cui il gioco fa davvero
        /// l'esecuzione:
        ///     Core.Input.SetBlocker("EXECUTION", true);   // <-- da qui in poi si sta eseguendo
        ///     interactableAnimator.gameObject.SetActive(false);
        ///     ShowPlayer(false);
        ///     ExecutedEntity.KillEntity();                // <-- la morte parte QUI
        ///     interactorAnimator.SetTrigger("USED");
        ///     while (!base.Consumed) yield return lapse;
        ///
        /// E NON E' UN METODO NUOVO DA SCEGLIERE: `Execution.OnUse` e' l'unico posto in cui l'esecuzione
        /// comincia, ed e' anche il gemello esatto di `InteractionEnd` (la patch in fondo al file), che
        /// l'esecuzione la chiude. Inizio e fine tornano a essere DUE METODI DELLA STESSA CLASSE, sullo
        /// STESSO oggetto.
        ///
        /// ⚠️ E L'ITERATORE NON E' UN OSTACOLO: `OnUse` e' `IEnumerator`, quindi il suo corpo parte al
        /// primo `MoveNext()` e non alla chiamata - ma il POSTFIX gira alla CHIAMATA, cioe' alla
        /// creazione dell'iteratore. E quella chiamata avviene DENTRO il frame della pressione:
        ///     Execution.OnUpdate -> Use() -> StartCoroutine(UseCorroutine())
        ///       -> [primo MoveNext, immediato] ... yield return StartCoroutine(OnUse())
        ///         -> OnUse()  <-- QUI GIRA IL POSTFIX, e il corpo di OnUse non e' ancora partito
        /// La prova nel log di una sessione reale e' l'ordine delle righe: `Starting using: ...`
        /// (scritta da `UseCorroutine` PRIMA di chiamare `OnUse`) precede `EXECUTION` blocker enabled
        /// (scritta da `OnUse`). ⇒ L'annuncio ora cade FRA quelle due, e non prima di entrambe.
        /// (E' la stessa forma gia' usata da `PrieDieuHelper`: un Postfix su un `OnUse` che e' una
        /// coroutine.)
        ///
        /// ⚠️ ED E' IL MOMENTO CHE SERVE AL RICEVENTE. Il fantasma deve partire quando si preme, e il
        /// nemico deve morire quando si preme: `KillEntity()` sta DUE righe sotto il punto in cui questo
        /// postfix gira, quindi l'annuncio e la morte sono nello STESSO frame, e il peer non vede piu'
        /// un'esecuzione su un nemico che cammina.
        /// </summary>
        public static void Announce(Execution prop)
        {
            try
            {
                if (prop == null)
                    return;

                if (!Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                // Un colpo relayato da questa macchina non si ri-annuncia: vedi il commento della classe.
                if (EnemySyncManager.IsApplyingRemoteDamage)
                    return;

                // ⚠️ L'ENTITA' ESEGUITA, E IL CAMPO E' UN ALTRO RISPETTO A PRIMA. Fino a ieri la patch
                // riceveva l'`EntityExecution` del NEMICO e leggeva `EntityOwner`; adesso riceve la PROP
                // (`Execution`), e il nemico sta in `Execution.ExecutedEntity`:
                //     public Enemy ExecutedEntity { get; set; }        (Execution.cs:101)
                //     execution.GetComponentInChildren<Execution>().ExecutedEntity = base.EntityOwner as Enemy;
                //                                         (EntityExecution.InstantiateExecution, riga 40)
                //   ⇒ E' LO STESSO NEMICO: quel campo viene scritto con `base.EntityOwner as Enemy`, cioe'
                //   ESATTAMENTE il valore che prima si leggeva dall'altra parte della catena. Non cambia
                //   nulla nell'id, nella memoria delle morti, in `MarkSpawnPointConsumed` o nel ciclo che
                //   abbatte il nemico presente: `TryGetEnemyId` vede lo stesso `Enemy`.
                //   ⚠️ E `ExecutedEntity` e' GIA' VALORIZZATO quando si preme: lo scrive
                //   `InstantiateExecution`, cioe' quando il nemico diventa eseguibile - prima della
                //   pressione. Se fosse nullo, `TryGetEnemyId` uscirebbe con la sua riga di osservazione.
                Enemy enemy = prop.ExecutedEntity;

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

                // A6 - IL FILO FRA INIZIO E FINE, E ADESSO E' L'OGGETTO STESSO.
                //
                // ⚠️⚠️ QUI C'ERA `FindObjectOfType<Execution>()`, ED E' SPARITO PERCHE' NON SERVE PIU'.
                // Prima si arrivava da `EntityExecution.InstantiateExecution` - un metodo del NEMICO - e la
                // prop dell'esecuzione andava CERCATA nel mondo (con la nota che in quell'istante ce n'era
                // una sola, quella appena nata, e che `FindObjectOfType` era l'unico modo di raggiungerla).
                // Adesso `__instance` E' la prop: l'annuncio parte da `Execution.OnUse`, quindi il campo si
                // scrive con l'oggetto che si ha in mano.
                //   ⇒ Il filo fra inizio e fine non e' piu' "una prop trovata per somiglianza": e' la prop
                //   che sta eseguendo, e sara' la STESSA che chiamera' `InteractionEnd` (la patch in fondo
                //   al file, sullo stesso oggetto).
                PendingExecution = prop;

                // ⚠️ E LA GUARDIA CHE C'ERA QUI NON HA PIU' SENSO D'ESSERE, il che e' un guadagno e non una
                // perdita. Prima serviva a non far partire l'inizio quando la prop non si trovava: senza di
                // essa un inizio senza fine avrebbe lasciato l'animazione appesa al solo fusibile. Ma quella
                // guardia esisteva SOLO perche' si stava guardando un altro oggetto: `prop` qui non puo'
                // essere null (la prima riga di questo metodo esce se lo e'), e se e' vivo adesso lo e' anche
                // quando `InteractionEnd` girera'. La via di uscita e' garantita dal fatto, non da un
                // controllo.

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
        /// diversi (`Execution.OnUse` e `Execution.InteractionEnd`), e fra i due non c'e' alcuna pila che li
        /// tenga insieme: `OnUse` e' una COROUTINE, e quando `InteractionEnd` gira la pila di `OnUse` e' gia'
        /// stata smontata da decine di `yield`. Il filo che li lega e' questo campo.
        ///   ⚠️ E adesso i due METODI sono della STESSA CLASSE e l'oggetto e' LO STESSO: il campo si scrive
        ///   da `OnUse` (`PendingExecution = prop`) e si legge da `InteractionEnd`. Prima erano due classi
        ///   diverse - il nemico e la sua prop - e questo campo era l'unica cosa che le tenesse insieme.
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
    /// ⭐ IL BERSAGLIO, E IL PERCHE' E' CAMBIATO.
    ///
    /// QUESTO ERA `EntityExecution.InstantiateExecution()`, E NON POTEVA ESSERE GIUSTO:
    /// `InstantiateExecution()` e' chiamato da `Enemy.Execution(Hit)` -> `Behaviour.Execution()` quando il
    /// nemico DIVENTA ESEGUIBILE - per prossimita', vita o stun - e da quel momento il giocatore PUO'
    /// premere. Puo' anche NON premere mai. Annunciare li' il "si sta eseguendo" e' annunciare una
    /// intenzione che nessuno ha ancora avuto, e i due sintomi dell'utente sono esattamente le sue
    /// conseguenze: un fantasma in piu' (con lo sprite del remoto) che recita da solo, e un nemico vivo su
    /// una macchina e morto sull'altra.
    ///
    /// ⭐ ADESSO E' `Execution.OnUse()`, cioe' il metodo che il gioco chiama QUANDO SI PREME:
    ///     Execution.OnUpdate  ->  if (... base.InteractionTriggered ...) { ...; Use(); }
    ///     Interactable.Use()  ->  StartCoroutine(UseCorroutine())
    ///     UseCorroutine()     ->  ... yield return StartCoroutine(OnUse())   <-- la chiamata a OnUse
    /// ed e' dentro `OnUse` che il gioco fa `SetBlocker("EXECUTION")`, `ShowPlayer(false)` e
    /// `KillEntity()`.
    ///
    /// ⚠️⚠️ E' UNA COROUTINE, QUINDI VA DETTO CON PRECISIONE DOVE GIRA IL POSTFIX. `OnUse()` e' un
    /// ITERATORE: il suo corpo non gira alla chiamata, gira al primo `MoveNext()`. Ma Harmony avvolge il
    /// METODO, non l'iteratore, quindi il Postfix gira ALLA CHIAMATA - cioe' alla CREAZIONE
    /// dell'iteratore, e il corpo di `OnUse` non e' ancora partito.
    ///   Dove cade quella chiamata, nel tempo: `StartCoroutine` esegue il primo segmento IMMEDIATAMENTE,
    /// quindi `UseCorroutine` arriva a `yield return StartCoroutine(OnUse())` nello STESSO frame di
    /// `Use()`, che e' lo STESSO frame in cui `Execution.OnUpdate` ha letto la pressione del tasto.
    ///   ⇒ Il postfix gira DENTRO il frame dell'azionamento, poche istruzioni PRIMA di
    ///   `ExecutedEntity.KillEntity()`. E la prova e' nel log, nell'ordine delle righe:
    ///       1. `Starting using: ACT_...Execution(Clone)`      <- scritta da `UseCorroutine` PRIMA di OnUse
    ///       2. `[Exec TX] ...` / `[ExecAnim TX] START`        <- QUESTO postfix
    ///       3. `The input blocker <EXECUTION> has been enabled` <- scritta da `OnUse`, riga 2 del suo corpo
    ///   Se l'annuncio ricompare PRIMA della riga 1, il postfix non sta girando dove crediamo.
    ///   (La stessa forma e' gia' usata in `PrieDieuHelper`: `[HarmonyPatch(typeof(PrieDieu), "OnUse")]`,
    ///   con la nota che OnUse "only builds the coroutine and returns - so a postfix on it runs BEFORE the
    ///   body does anything".)
    ///
    /// ⚠️ E `OnUse` e' `protected override`. Non e' un ostacolo: Harmony risolve il bersaglio per nome e
    /// `PatchAll` non guarda la visibilita' - gia' verificato per `InteractionEnd()`, che e' protected
    /// anche lui. L'override vive su `Execution` (non su `Interactable`), che e' il tipo dichiarato qui, e
    /// `AccessTools.Method` cerca prima i metodi dichiarati nel tipo. Ed e' il TIPO che si patcha: e' anche
    /// l'unico modo di non colpire gli altri `Interactable` del gioco (forziere, leva, PrieDieu...), che
    /// hanno il loro `OnUse`.
    ///
    /// E' un POSTFIX, quindi il metodo del gioco e' gia' girato INTATTO: nessun valore di ritorno
    /// toccato, nessuno stato cambiato, nessun effetto collaterale alterato. La regola del progetto sui
    /// Prefix di AnimationFixes.cs - il cui VALORE DI RITORNO e' il punto, e la cui riscrittura ha gia'
    /// causato 12 NRE - non viene sfiorata, perche' qui non c'e' alcun Prefix.
    ///
    /// ⚠️ E NON CE N'E' UN ALTRO SU QUESTO METODO: nessuna classe del progetto patcha
    /// `Tools.Level.Interactables.Execution.OnUse`. `InteractionEnd` resta l'unica altra patch su questa
    /// classe, ed e' quella della FINE. I due metodi sono UNO L'INIZIO e UNO LA FINE dello stesso oggetto.
    /// </summary>
    [HarmonyPatch(typeof(Execution), "OnUse")]
    class ExecutionOnUse_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Execution __instance)
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
