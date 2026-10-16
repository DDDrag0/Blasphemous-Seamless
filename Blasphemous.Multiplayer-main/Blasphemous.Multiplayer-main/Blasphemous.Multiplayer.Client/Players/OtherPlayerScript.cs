using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Effects.Player.GhostTrail;
using System.Linq;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players
{
    public class OtherPlayerScript : MonoBehaviour, IDamageable
    {
        private PlayerStatus playerStatus;
        private bool playerDead = false;

        /// <summary>
        /// Vero quando <see cref="SetDeathStatus(bool)"/> ha davvero messo l'avatar **a terra** (Animator
        /// spento sulla posa di morte). E' il testimone della prova di vita: e' la cosa che va disfatta, e
        /// non oscilla come <c>playerDead</c> (vedi <c>Update</c>, il ramo <c>else</c>).
        /// </summary>
        private bool deathStatusApplied = false;

        private SpriteRenderer CharacterRenderer { get; set; }
        private Animator CharacterAnim { get; set; }

        // ============================================================================================
        // A6 - L'ESECUZIONE. SI ISTANZIA IL PREFAB, SI TIENE IL FANTASMA, SI DISTRUGGE IL RESTO.
        // ============================================================================================
        //
        // ⭐⭐ PERCHE' NON SI SUONA PIU' UNO STATO SULL'AVATAR. E' la versione precedente, ed e' quella che
        // l'utente ha visto NON funzionare: "solo il flash della parte iniziale dell'animazione" e poi
        // l'avatar invisibile. La ragione e' scritta in `Execution.OnUse` (`Execution.cs:150-178`):
        //
        //     Core.Input.SetBlocker("EXECUTION", true);
        //     interactableAnimator.gameObject.SetActive(false);
        //     ShowPlayer(false);                       <-- IL PENITENT LOCALE VIENE NASCOSTO
        //     ExecutedEntity.KillEntity();
        //     interactorAnimator.SetTrigger("USED");
        //
        // ⭐ IL PENITENT NON HA UN'ANIMAZIONE DI ESECUZIONE: viene NASCOSTO. L'unica cosa che si vede in
        // un'esecuzione e' IL FANTASMA (`interactorAnimator`, un clone del penitent, figlio della prop), e
        // quella clip e' disegnata per STARE AL POSTO del penitent, non per essere su di lui.
        //   ⇒ Si istanzia il prefab, si STACCA il fantasma (`SetParent(null)`, cosi' sopravvive), si distrugge
        //   tutto il resto, e si preme su di lui lo STESSO trigger che preme il gioco.
        //
        // ⭐ E LA NEUTRALIZZAZIONE DELLA PROP E' PERFETTA, non "improbabile": `Execution.OnUse` NON ESISTE su
        // questa strada. Nessuno accende `SetBlocker`, nessuno tocca `TimeScale` o la camera, nessuno sposta
        // il penitent locale - perche' la neutralizzazione piu' sicura di un componente e' non crearlo.
        //   ⚠️ `Awake()` GIRA comunque sull'istanza (Unity lo chiama dentro `Instantiate`), quindi la prop
        //   registra i suoi sensori e si aggancia a `SpawnManager.OnPlayerSpawn`. Ma e' `OnUpdate` che legge
        //   l'input, e `OnUpdate` gira al PRIMO `Update` successivo: distruggendo il resto NELLO STESSO FRAME,
        //   quell'`Update` non arriva mai. Vedi `PlayExecutionAnimation`, dove l'ordine e' argomentato.
        //
        // ⚠️ E COSA NON SI FA, CHE LA PROP INVECE FA. Non si tocca la camera (`CenterCamera` centra sul
        // penitent LOCALE), non si tocca lo slow-motion (`DoSlowmotion` scrive `_currentLevel.TimeScale`
        // LOCALE: rallenterebbe TUTTO il gioco di chi guarda, mentre sta camminando), non si accende alcun
        // input blocker, non si sposta il penitent locale. Non sono scelte di gusto: sono divieti.

        /// <summary>
        /// Lo stato di animator dell'esecuzione. UNO, e con la grafia esatta letta nel corpo IL del gioco
        /// (`Execution.cs:166`: `interactorAnimator.SetTrigger("USED")`).
        ///
        /// ⚠️ UN HASH CALCOLATO UNA VOLTA: `Animator.StringToHash` e' una ricerca per stringa, e questo
        /// codice gira sul percorso degli effetti. `static readonly` perche' non dipende dall'avatar.
        /// </summary>
        private static readonly int ExecutionStateHash = Animator.StringToHash("USED");

        /// <summary>
        /// Da quando l'esecuzione sta andando su QUESTO avatar, in `Time.unscaledTime`. Zero = nessuna.
        ///
        /// ⚠️ UN CAMPO D'ISTANZA, E NON UNO STATICO. Il fusibile deve poter essere letto PER L'AVATAR
        /// GIUSTO: se lo stato fosse statico ci sarebbe UN solo tempo per tutta la mod, e due esecuzioni -
        /// una per giocatore, che in co-op e' il caso NORMALE - si sovrascriverebbero a vicenda. Il primo
        /// avatar a eseguire resterebbe in posa finche' il fusibile del secondo non scatta.
        /// </summary>
        private float ExecutionStartedAt;

        /// <summary>
        /// Quanto puo' durare l'animazione dell'esecuzione prima che il fusibile la chiuda da solo.
        ///
        /// ⚠️ PERCHE' UN FUSIBILE E NON SOLO IL BYTE DI FINE. Lo stesso argomento della scivolata, con
        /// un'aggravante: li' un evento sostenuto che non si spegne "resta acceso"; qui un avatar che non
        /// torna indietro RESTA IN POSA DI ESECUZIONE. Le due condizioni che lo producono sono note e non
        /// rare: un pacchetto perso, e una disconnessione nel mezzo.
        ///
        /// ⚠️ ED E' UN VALORE DA TARARE, NON DA INDOVINARE: la durata reale in tempo reale NON e' quella
        /// dell'editor, perche' il gioco manda l'esecuzione in slow-motion (`TimeScale` a 0.25 in 0.5 s) **e**
        /// porta `_animator.speed` a 3. Le due cose si compongono in un modo che non si puo' calcolare da qui.
        ///   ⇒ SI PARTE LARGHI (12 s) E SI STRINGE COI DATI: i marker `[ExecAnim TX] START`/`END` danno i due
        ///   istanti esatti, e la loro differenza E' la durata. E `PlayExecutionAnimation` logga
        ///   `GetCurrentAnimatorStateInfo(0).length`, cioe' la durata che il CONTROLLER dichiara.
        /// </summary>
        private const float ExecutionTimeoutSeconds = 12f;

        /// <summary>
        /// Accende o spegne l'esecuzione su QUESTO avatar.
        ///
        /// ⚠️ E' UN METODO D'ISTANZA. Chi lo chiama e' `OtherPlayerAttackScript.PlayEffectAnimation`, che
        /// risale all'avatar dal padre esattamente come fa gia' per la scia. Nessuna ricerca per nome, nessun
        /// caso "avatar non trovato", nessun pacchetto applicato all'avatar sbagliato.
        /// </summary>
        public void PlayExecutionAnimation(bool start)
        {
            if (!start)
            {
                // La FINE. `RestoreExecutionAvatar` uccide il fantasma e rimette l'avatar, e si arrende da
                // sola se non c'e' nulla da fare.
                RestoreExecutionAvatar("byte di fine");
                return;
            }

            // ⚠️ UNA SECONDA ESECUZIONE NON SI ACCAVALLA: si riparte. Se una e' gia' in corso (il byte di
            // fine precedente e' andato perso, o il peer ha eseguito due nemici di fila) il tempo si
            // riazzera invece di sommare, e il fantasma vecchio muore invece di restare a girare.
            if (ghost != null)
            {
                ModLog.Info("[ExecGhost RX] una seconda esecuzione arriva mentre la prima e' ancora in " +
                            "scena: il fantasma vecchio muore adesso");
                DestroyGhost("seconda esecuzione");
            }

            ExecutionStartedAt = Time.unscaledTime;

            GameObject prefab = OtherPlayerAttackScript.ResolveExecutionPrefab();
            if (prefab == null)
            {
                // Osservazione, non un verdetto: senza prefab non c'e' nulla da istanziare, e la riga dice
                // che il guasto NON e' di rete.
                ModLog.Warn("[ExecGhost RX] il prefab dell'esecuzione non si e' risolto (il nemico non e' " +
                            "raggiungibile o non ha un EntityExecution): nessun fantasma, e NON e' un " +
                            "problema di rete");
                return;
            }

            // ⚠️⚠️ PERCHE' SI ISTANZIA IL PREFAB, E NON SI INSTALLA UN CONTROLLER SULL'AVATAR.
            //
            // E' la strada che il referto dichiara giusta, e la ragione e' una sola: IL PENITENT NON HA
            // UN'ANIMAZIONE DI ESECUZIONE. `Execution.OnUse` (`Execution.cs:150-178`) lo fa sparire:
            //
            //     Core.Input.SetBlocker("EXECUTION", true);
            //     interactableAnimator.gameObject.SetActive(false);
            //     ShowPlayer(false);                       <-- IL PENITENT LOCALE VIENE NASCOSTO
            //     ExecutedEntity.KillEntity();
            //     interactorAnimator.SetTrigger("USED");   <-- "USED" E' UN TRIGGER, NON UNO STATO
            //     while (!base.Consumed) yield return lapse;
            //
            // e l'unica cosa che si vede in un'esecuzione e' IL FANTASMA: `interactorAnimator`, un clone
            // del penitent, figlio della prop. Installare il SUO controller sull'avatar produce esattamente
            // cio' che l'utente ha visto: "il flash della parte iniziale e poi l'invisibile" - perche'
            // quella clip e' disegnata per STARE AL POSTO del penitent, non per essere su di lui.
            //   ⇒ Si prende IL FANTASMA VERO.
            GameObject prop = UnityEngine.Object.Instantiate(prefab, transform.position, transform.rotation);
            if (prop == null)
            {
                ModLog.Warn("[ExecGhost RX] l'istanza del prefab e' fallita: nessun fantasma");
                return;
            }

            // ⭐ IL FANTASMA E' IL FIGLIO DELLA PROP CHE PORTA IL CONTROLLER DELL'ESECUZIONE, e la via per
            // prenderlo e' il campo che il GIOCO usa per recitare: `Interactable.interactorAnimator`
            // (`Interactable.cs:65`), quello su cui `Execution.OnUse` preme `SetTrigger("USED")`.
            //   ⚠️ `GetComponentInChildren<Execution>()` include i componenti del GameObject radice: se la
            //   prop porta l'`Execution` sulla radice (come fa `EntityExecution.InstantiateExecution`),
            //   questo e' un `GetComponent`. Se invece sta su un figlio, prende quella - che e' comunque
            //   l'unica prop di questa istanza.
            Execution execution = prop.GetComponentInChildren<Execution>(true);
            Animator ghostAnimator = execution == null || OtherPlayerAttackScript.InteractorAnimatorField == null
                ? null
                : OtherPlayerAttackScript.InteractorAnimatorField.GetValue(execution) as Animator;

            if (ghostAnimator == null)
            {
                ModLog.Warn("[ExecGhost RX] l'istanza non espone `interactorAnimator` " +
                            $"(`Execution` trovata={execution != null}): il fantasma non si isola, la prop " +
                            "viene distrutta e l'esecuzione non si vede");
                UnityEngine.Object.Destroy(prop);
                return;
            }

            // ⭐⭐ LO STACCO - ED E' UN `SetParent`, NON UNA COPIA. Un GameObject figlio segue il genitore
            // nella distruzione: senza questa riga `Destroy(prop)` porterebbe via anche il fantasma, e la
            // strada non esisterebbe. Con `worldPositionStays: true` la posizione nel mondo non cambia.
            GameObject ghostObject = ghostAnimator.gameObject;
            ghostObject.transform.SetParent(null, true);
            ghostObject.SetActive(true);

            // ⭐⭐ E QUI MUORE TUTTO IL RESTO, NELLO STESSO FRAME.
            //
            // `Execution.OnUse` NON ESISTE su questa strada: nessuno chiama `SetPlayer(false)`, nessuno
            // accende `SetBlocker("EXECUTION")`, nessuno tocca `TimeScale` o la camera. La voce 54 non e'
            // "improbabile": e' IMPOSSIBILE, perche' la neutralizzazione piu' sicura di un componente e'
            // non crearlo.
            //
            // ⚠️ MA `Awake()` GIRA SULL'ISTANZA, perche' Unity lo chiama dentro `Instantiate`, e
            // `Interactable.Awake` (`Interactable.cs:331-349`) registra i SENSORI, si aggancia a
            // `SpawnManager.OnPlayerSpawn` e chiama `CheckAnimationEvents`. Questo e' verificato in IL e
            // non si puo' evitare.
            //   ⇒ E' `OnUpdate` che legge l'input:
            //         Interactable.Update (`Interactable.cs:411-414`) -> `OnUpdate`
            //         Execution.OnUpdate (`Execution.cs:194-212`) ->
            //             `Penitent.PlatformCharacterInput.Attack` e `base.InteractionTriggered`
            //             -> `ReInput.players.GetPlayer(0).GetButtonDown(8)`
            //   ⇒ E `Update` gira al PRIMO fotogramma successivo all'istanza. `Object.Destroy` DIFFERISCE la
            //   distruzione alla FINE DEL FRAME CORRENTE (`DestroyImmediate` sarebbe l'unica alternativa, e
            //   farebbe girare `OnDestroy` in mezzo a questo metodo).
            //   ⇒ DISTRUGGENDO IL RESTO NELLO STESSO FRAME, `OnUpdate` NON GIRA MAI, e l'unica riga che
            //   potrebbe accendere il blocker dell'input non viene mai raggiunta.
            //
            // ⚠️ E L'ORDINE CON `AnimatorEvent` E' SICURO, verificato in IL. `AnimatorEvent` sta SUL
            // FANTASMA (`Interactable.CheckAnimationEvents`, righe 351-365, lo prende con
            // `interactorAnimator.GetComponent<AnimatorEvent>()`) e si e' iscritto a
            // `Interactable.OnEventLaunched`. Quando la prop muore, `Interactable.OnDestroy` (righe
            // 367-388) fa `AnimatorEvent.OnEventLaunched -= OnEventLaunched`. Da li' in avanti il fantasma
            // ha un delegate verso un oggetto MORTO, e non e' un guasto: il delegate non lancia, e
            // l'unica cosa che smette e' `"INTERACTION_END"` - cioe' la fine dell'esecuzione, che su questa
            // macchina NON DEVE arrivare al gioco (la prop del gioco locale non esiste).
            UnityEngine.Object.Destroy(prop);

            // ============================================================================================
            // ⭐⭐ ②a - SI DISTRUGGE `ExecutionAnimationEvents` SUL FANTASMA. E' L'UNICO COMPONENTE CHE
            // RILEGGE UN `Execution`, E QUEL `Execution` NON ESISTE PIU'.
            // ============================================================================================
            //
            // ⭐ LA NRE, MISURATA. In G1, una riga dopo "fantasma istanziato" (1865):
            //
            //     1866 Ups! There was an unhandled exception: NullReferenceException
            //     1867 Tools.Level.Interactables.ExecutionAnimationEvents.Start ()
            //
            // e in IL la ragione e' letterale, `ExecutionAnimationEvents.cs:20-29`:
            //
            //     private void Start() {
            //         _execution = GetComponentInParent<Execution>();      // <-- NULL, e non e' un caso
            //         _animator = GetComponent<Animator>();
            //         execution.OnNormalTime = (Core.SimpleEvent)Delegate.Combine(...)   // <-- QUI si lancia
            //         ...
            //         CreateExecutionSoundEvent();                        // <-- MAI RAGGIUNTA
            //     }
            //
            //     ⚠️ `GetComponentInParent<Execution>()` non trova nulla E NON PUO' TROVARE NULLA: la prop
            //     (che porta l'`Execution`) e' distrutta due righe sopra, il fantasma e' stato STACCATO
            //     (`SetParent(null, true)`, riga 180) e `GetComponentInParent` NON risale oltre la radice.
            //     Le due righe che seguono leggono `execution` SENZA guardia `null`: la NRE e' garantita,
            //     non probabile.
            //
            //     ⚠️ E `Start()` NON era ancora girato perche' `Instantiate` chiama solo `Awake`/`OnEnable`:
            //     la NRE arriva quindi DOPO la distruzione della prop, cioe' quando il danno e' gia' fatto.
            //
            // ⭐⭐ E ALLORA PERCHE' DISTRUGGERLO INVECE DI TENERLO - la domanda vera, perche' il suono e' in
            // gioco. LA RISPOSTA, verificata in IL: IL SUONO DELL'ESECUZIONE NON E' RAGGIUNGIBILE DA QUESTA
            // STRADA, e non lo diventerebbe tenendo il componente. I suoi tre metodi di suono sono:
            //
            //     public void PlayExecutionSound()               { if (_executionSound.isValid()) _executionSound.start();   }
            //     public void StopExecutionSound()               { if (_executionSound.isValid()) _executionSound.release(); }
            //     public void UpdateExecutionSoundEventParam(float p) { if (_hitParam.isValid()) _hitParam.setValue(p); }
            //
            // e l'unica riga che li rende vivi e' dentro `Start()`:
            //
            //     private void CreateExecutionSoundEvent() {
            //         _executionSound = Core.Audio.CreateEvent(_execution.ActivationSound);   // Execution.cs:99 => activationSound
            //         _executionSound.getParameter("Hits", out _hitParam);
            //     }
            //
            // cioe' L'EVENTO FMOD E' L'`ActivationSound` DELLA PROP, CHE E' STATA DISTRUTTA. Su questa strada
            // `_execution` e' null PRIMA di quella riga: `_executionSound` resta un default (non valido) e i
            // tre metodi sono no-op. Il suono dell'esecuzione non "si perde" distruggendo il componente: si
            // e' gia' perso con la prop, e tenerlo in vita non lo recupera - aggiunge solo una NRE per ogni
            // esecuzione remota.
            //
            // ⚠️ E NON SI FABBRICA UN `Execution` FITTIZIO, ne' un `SetParent` sulla prop per farglielo
            // trovare: sarebbero STATO DEL GIOCO INVENTATO. Un `Execution` finto vorrebbe dire `OnUse` (o
            // `Awake`/istanza) con `SetPlayer`, `SetBlocker("EXECUTION")`, TimeScale e camera dentro - e la
            // prop VA distrutta, e' la ragione per cui il fantasma e' stato staccato.
            //
            // ⭐ E IL SUONO DELL'ESECUZIONE C'E' LO STESSO, PER UN'ALTRA VIA, GIA' VERIFICATA NEI LOG: e' il
            // `Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy()` di `ApplyRemoteExecution`, che la macchina
            // esecutrice chiama per davvero in ogni `Behaviour.Execution()` del gioco. In G1 e' la riga 1855,
            // "[TRACE][AUDIO] Playing audio hit. event:/SFX/Penitent/Damage/PenitentSimpleEnemyHit", e cade
            // PRIMA del fantasma (1852-1853): il colpo dell'esecuzione si sente mentre l'esecuzione accade.
            //   ⇒ Il silenzio che si aggiunge qui e' il solo `activationSound` della prop, non il suono
            //   dell'esecuzione per chi guarda.
            //
            // ⚠️ E VALE LA REGOLA GENERALE, tanto piu' qui: OGNI COMPONENTE CHE CERCA UN `Execution` IN UN
            // GENITORE E' ROTTO PER COSTRUZIONE SU QUESTO FANTASMA. Non e' "un componente da sistemare": il
            // fantasma e' staccato (`SetParent(null)`) e la prop e' distrutta, quindi non esiste - e non puo'
            // esistere - un `Execution` sopra di lui. Distruggere il componente e' l'unica cosa che toglie la
            // NRE SENZA LASCIARE IN GIRO UN COMPONENTE CHE PUNTA A UN OGGETTO DISTRUTTO.
            //
            // ⚠️ SI USA `Destroy`, NON `DestroyImmediate`: `Destroy` differisce a fine frame, e un
            // `DestroyImmediate` farebbe girare `OnDestroy` in mezzo a questo metodo (stessa ragione della
            // riga 199-201). E la differenza NON riapre la NRE: `Start()` di un oggetto istanziato in questo
            // frame gira al PRIMO `Update` successivo, cioe' DOPO la fine di questo frame - quindi il
            // componente e' gia' distrutto quando `Start()` sarebbe stato chiamato.
            //   ⚠️ `GetComponentsInChildren<...>()` INCLUDE il GameObject radice (stessa semantica gia' usata
            //   per `Execution` alla riga 162), quindi si coprono sia il fantasma stesso sia un eventuale
            //   figlio che la porta.
            ExecutionAnimationEvents[] ghostEvents =
                ghostObject.GetComponentsInChildren<ExecutionAnimationEvents>(true);
            for (int i = 0; i < ghostEvents.Length; i++)
            {
                if (ghostEvents[i] == null)
                    continue;

                UnityEngine.Object.Destroy(ghostEvents[i]);
            }

            if (ghostEvents.Length > 0)
                ModLog.Info($"[ExecGhost RX] distrutti {ghostEvents.Length} ExecutionAnimationEvents sul " +
                            "fantasma: cercavano un `Execution` in un genitore che non esiste piu' (prop " +
                            "distrutta + fantasma staccato), e `Start()` lanciava NRE. Il suono " +
                            "dell'esecuzione per chi guarda e' PlaySimpleHitToEnemy, gia' suonato da " +
                            "ApplyRemoteExecution: qui si perdeva solo l'`activationSound` della prop.");

            ghost = ghostObject;
            ghostAnimator.SetTrigger(ExecutionStateHash);

            // ⭐⭐ E ORA L'AVATAR SPARISCE, CHE E' LA RICHIESTA DELL'UTENTE.
            //
            // ⚠️ E' L'ULTIMA COSA, E NON LA PRIMA, DI PROPOSITO: si nasconde l'avatar SOLO QUANDO IL FANTASMA
            // ESISTE DAVVERO. Tutte le uscite anticipate qui sopra (prefab non risolto, `interactorAnimator`
            // assente) NON nascondono nulla: senza fantasma non c'e' nessuna animazione da mostrare, e un
            // avatar nascosto per un'esecuzione che non si vede sarebbe il difetto peggiore - invisibile e
            // senza motivo. La richiesta e' "invisibile MENTRE l'animazione va", non "invisibile e basta".
            HideAvatarForExecution();

            if (ExecutionGhostWatcher.Attach(this, ghost, ghostAnimator))
                ModLog.Info($"[ExecGhost RX] fantasma istanziato su {name}: prop distrutta nello stesso " +
                            $"frame, trigger USED premuto, durata clip=" +
                            $"{ghostAnimator.GetCurrentAnimatorStateInfo(0).length:F2}s");
            else
                ModLog.Warn("[ExecGhost RX] fantasma istanziato ma il sorvegliante non si e' potuto " +
                            "agganciare: resta il solo fusibile a 12s");
        }

        /// <summary>
        /// Rimette QUESTO avatar come stava. SICURA DA CHIAMARE SEMPRE, anche a vuoto, e IDEMPOTENTE: il byte
        /// di fine, il fusibile e `OnDestroy` possono chiamarla tutti e tre.
        ///
        /// ⚠️ CHI LA USA E PERCHE' SONO TRE. Il ripristino ha tre vie di uscita e servono tutte:
        ///   1. il byte di fine (il caso normale, arriva subito);
        ///   2. il fusibile (il byte di fine PERSO - un pacchetto perso non si puo' escludere);
        ///   3. `OnDestroy` (la disconnessione / il cambio scena, dove i pacchetti non arrivano piu').
        /// E' lo stesso schema della scivolata, con un'aggravante: qui non si "sente male", qui si VEDE un
        /// personaggio bloccato in una posa.
        /// </summary>
        private void RestoreExecutionAvatar(string why)
        {
            // ⚠️ L'IDEMPOTENZA SI OTTIENE DALLO STATO, NON DA UN FLAG. `ExecutionStartedAt == 0` significa
            // "su questo avatar non sta andando nulla": il byte di fine dopo il fusibile, o `OnDestroy` dopo
            // il byte di fine, escono qui senza toccare l'Animator e senza scrivere una seconda riga di log
            // che farebbe sembrare normale un doppio ripristino.
            //
            // ⚠️ E LE TRE CONDIZIONI SONO `||` E NON UNA SOLA, perche' adesso ci sono TRE STATI DA
            // RIPRISTINARE: il tempo, il fantasma e il renderer dell'avatar. Il fusibile azzera il tempo e
            // uccide il fantasma nello stesso giro, quindi di solito i tre casi sono lo stesso caso - ma un
            // avatar che avesse perso uno solo dei tre (o che sia stato nascosto senza che il tempo sia mai
            // stato acceso) deve comunque essere rimesso a posto.
            //
            // ⚠️ `executionRendererHidden` E' NELLA GUARDIA, E NON E' PIGNOLERIA: e' l'UNICO dei tre stati che
            // puo' lasciare un avatar INVISIBILE PER SEMPRE. Un `ExecutionStartedAt` rimasto acceso si
            // spegne al primo `Update`; un fantasma rimasto in scena si distrugge; un renderer spento e mai
            // riacceso non lo riaccende NESSUNO - e questo metodo e' l'unico posto in cui viene riacceso.
            if (ExecutionStartedAt <= 0f && ghost == null && !executionRendererHidden)
                return;

            ExecutionStartedAt = 0f;
            DestroyGhost(why);

            // ⚠️⚠️ L'AVATAR NON HA MAI RECITATO NULLA, E NON SI TOCCA IL SUO ANIMATOR.
            //
            // Fino a ieri qui c'era `CharacterAnim.Play(0)`, perche' l'animazione la recitava l'AVATAR con
            // il controller del fantasma installato - ed e' la strada che produceva "un flash e poi
            // l'invisibile". Adesso la recita il FANTASMA, che ha il SUO Animator e il SUO controller: il
            // controller dell'avatar non e' stato toccato, e un `Play(0)` lo farebbe saltare allo stato 0
            // proprio mentre G1 e' a meta' della sua cutscene.
            //   ⇒ L'UNICA COSA DA RIMETTERE E' CIO' CHE SI ERA SPENTO: il renderer dell'avatar. Il
            //   fantasma e' la sua faccia per tutta la durata dell'esecuzione, e alla fine la faccia torna
            //   al suo posto.
            //
            // ⚠️⚠️ E IL RENDERER TORNA AL VALORE CHE AVEVA, NON A `true`.
            //
            // Questa riga era `CharacterRenderer.enabled = true` ed era un difetto suo, oltre a quello che
            // l'utente ha visto: l'esecuzione che finisce su un avatar che era GIA' SPENTO - dentro una porta
            // (`FinishSpecialAnimation`), o non ancora vestito di skin - lo faceva COMPARIRE. Il ripristino
            // deve rimettere lo stato salvato, e `executionRendererHidden` dice se c'e' davvero qualcosa da
            // rimettere: se nessuno ha spento niente, qui non si scrive e non si logga - un avatar mai toccato
            // non deve produrre una riga che fa sembrare normale un ripristino che non e' avvenuto.
            //
            // ⚠️ E NON SI USA `SetActive`, ne' sul GameObject ne' sull'`Animator`: spegnere l'`Update` di
            // questo stesso script fermerebbe il fusibile (che deve poter scattare proprio quando i
            // pacchetti non arrivano piu'), e spegnere l'`Animator` fermerebbe il rilevamento della morte,
            // che e' l'altra cosa che questo `Update` fa.
            if (CharacterRenderer != null && executionRendererHidden)
            {
                executionRendererHidden = false;
                CharacterRenderer.enabled = executionRendererWasEnabled;

                // ⚠️ DUE ESITI DIVERSI, E IL LOG LI DISTINGUE: "torna visibile" e "resta spento com'era"
                // sono la stessa riga di codice ma due fatti opposti, e solo il valore salvato li separa.
                string outcome = executionRendererWasEnabled
                    ? "era VISIBILE prima dell'esecuzione, quindi torna visibile"
                    : "era GIA' SPENTO prima dell'esecuzione (morte, porta o skin): resta spento com'era";

                ModLog.Info($"[ExecGhost RX] avatar {name} ripristinato: SpriteRenderer.enabled " +
                            $"False -> {CharacterRenderer.enabled} ({outcome})");
            }

            ModLog.Info($"[ExecGhost RX] fine ({why}): fantasma ucciso, avatar {name} ripristinato " +
                        "(l'Animator dell'avatar non e' stato toccato in tutta l'esecuzione)");
        }

        /// <summary>
        /// Uccide il fantasma dell'esecuzione di QUESTO avatar, se c'e'. E' L'UNICA RIGA CHE LO DISTRUGGE, ed
        /// e' voluto: tre strade diverse possono chiamarla - il byte di fine, il fusibile, la fine della clip
        /// - e se ognuna avesse la sua `Destroy` la prima che dimentica un pezzo lascerebbe in giro un
        /// secondo penitent invisibile.
        ///
        /// ⚠️ NON E' SOLO IGIENE. `Interactable.Awake` ha registrato i SENSORI della prop su questo
        /// GameObject, e `Interactable.OnDestroy` li deregistra - cosa che non e' mai girata, perche' la prop
        /// e' stata distrutta mentre il fantasma era gia' stato staccato. Un fantasma che sopravvive e' un
        /// oggetto che risponde ai trigger del livello con un `Interactable` morto dietro.
        /// </summary>
        private void DestroyGhost(string why)
        {
            if (ghost == null)
                return;

            GameObject dying = ghost;
            ghost = null;

            // ⚠️ IL SORVEGLIANTE MUORE CON IL FANTASMA, e va spento PRIMA: e' lui che tiene il riferimento a
            // questo avatar, e lasciarlo vivo significherebbe un `Update` che parla a un avatar che sta
            // sparendo. Il fantasma viene distrutto per intero, quindi il componente se ne va comunque -
            // ma l'ordine e' esplicito perche' non e' ovvio che sia quello giusto.
            ExecutionGhostWatcher watcher = dying.GetComponent<ExecutionGhostWatcher>();
            if (watcher != null)
                watcher.enabled = false;

            Animator dyingAnimator = dying.GetComponent<Animator>();
            if (dyingAnimator != null)
            {
                // Si ferma PRIMA di distruggere, cosi' la posa congelata non viene disegnata per il
                // fotogramma che resta fra la distruzione e la fine del frame.
                dyingAnimator.StopPlayback();
                dyingAnimator.enabled = false;
            }

            ModLog.Info($"[ExecGhost RX] fantasma ucciso ({why})");
            UnityEngine.Object.Destroy(dying);
        }

        /// <summary>
        /// IL FANTASMA DELL'ESECUZIONE IN CORSO SU QUESTO AVATAR, o null.
        ///
        /// ⚠️ E' UN CAMPO D'ISTANZA, come `ExecutionStartedAt`, e per la stessa ragione: in co-op DUE
        /// esecuzioni insieme sono il caso normale, e un fantasma statico farebbe sparire quello del primo
        /// avatar quando il secondo comincia.
        ///
        /// ⚠️ PUO' DIVENTARE UN OGGETTO MORTO, come `ExecutionSync.PendingExecution`: un GameObject Unity
        /// distrutto si confronta uguale a null ma toccarlo lancia, quindi ogni uso e' guardato e
        /// `DestroyGhost` lo azzera SEMPRE, prima di distruggere.
        /// </summary>
        private GameObject ghost;

        /// <summary>
        /// IL VALORE DI `CharacterRenderer.enabled` COME ERA **PRIMA** CHE L'ESECUZIONE LO SPEGNESSE.
        ///
        /// ⭐⭐ PERCHE' SI SALVA, E NON SI RIMETTE `true`. Il ripristino deve rimettere l'avatar **esattamente
        /// come era**, e "come era" NON e' una costante: questo renderer viene spento da due percorsi che non
        /// hanno niente a che vedere con l'esecuzione -
        ///     `SetupPlayer` (`CharacterRenderer.enabled = false`, l'avatar nasce spento)
        ///     `FinishSpecialAnimation` (porta 247/248/249: l'avatar resta spento dentro la porta)
        /// - e viene riacceso da `ApplySkinTexture` (una volta sola, subito dopo la nascita dell'avatar).
        ///   ⇒ Scrivere `true` nel ripristino e' un'indovinata: se il peer era DENTRO UNA PORTA o non aveva
        ///   ancora ricevuto la sua skin, l'esecuzione lo avrebbe fatto COMPARIRE dal nulla - un difetto nuovo
        ///   al posto di quello vecchio, e pure peggiore, perche' la posizione e' quella sbagliata.
        ///   ⇒ Si salva il valore vero, e si rimette QUELLO. Vedi `HideAvatarForExecution`.
        /// </summary>
        private bool executionRendererWasEnabled;

        /// <summary>
        /// VERO SE E' STATO **QUESTO** SCRIPT A SPEGNERE IL RENDERER, cioe' se c'e' qualcosa da ripristinare.
        ///
        /// ⚠️ NON E' RIDONDANTE CON `ExecutionStartedAt`: quello dice che un'esecuzione e' in corso, questo dice
        /// che il renderer e' stato toccato. Sono diversi proprio nei casi che contano - prefab non risolto
        /// (`ExecutionStartedAt` acceso, renderer mai toccato), `OnDestroy` dopo un ripristino gia' avvenuto -
        /// e senza questa distinzione il ripristino non saprebbe se il suo silenzio e' "non c'era niente da
        /// fare" oppure "ho dimenticato di rimettere l'avatar".
        ///
        /// ⚠️ E SERVE ANCHE ALLA SECONDA ESECUZIONE: se una seconda esecuzione arriva mentre la prima e' in
        /// scena, il valore da salvare e' quello di PRIMA DELLA PRIMA (il renderer e' gia' spento da noi:
        /// salvarlo adesso salverebbe `false`, e alla fine l'avatar resterebbe invisibile per sempre).
        /// </summary>
        private bool executionRendererHidden;

        /// <summary>
        /// SPEgne il renderer dell'avatar per la durata dell'esecuzione, SALVANDO PRIMA com'era.
        ///
        /// ⭐ E' LETTERALMENTE CIO' CHE FA IL GIOCO SUL PENITENT CHE ESEGUE, e non e' un'analogia: `Execution.OnUse`
        /// chiama `ShowPlayer(false)` (`Execution.cs:160`), e `ShowPlayer` e' questo (`Interactable.cs:492-496`):
        ///
        ///     Core.Logic.Penitent.SpriteRenderer.enabled = show;      <-- SI SPEGNE IL RENDERER, NON L'ANIMATOR
        ///     Core.Logic.Penitent.DamageArea.enabled = show;
        ///
        /// cioe': durante l'esecuzione IL PENITENT CHE ESEGUE NON SI DISEGNA, e l'unica cosa disegnata e' il
        /// fantasma (`interactorAnimator`, `Execution.cs:166`). Sulla macchina di chi guarda il penitent locale
        /// non esiste, quindi l'unico che deve sparire e' QUESTO avatar: stessa riga, stesso effetto, e il
        /// fantasma resta la sola faccia dell'esecuzione - che e' esattamente cio' che l'utente ha chiesto.
        ///
        /// ⚠️ E NON SI SPEGNE L'ANIMATOR DELL'AVATAR (`CharacterAnim`). E' la trappola dichiarata: `SetDeathStatus`
        /// (`CharacterAnim.enabled = !dead`) e' un'ALTRA cosa, e spegnere l'Animator qui congelerebbe il
        /// rilevamento della morte e lo stato dell'avatar invece di limitarsi a non disegnarlo. Il renderer e'
        /// l'unica cosa che il gioco stesso spegne, e l'unica che si spegne qui.
        /// </summary>
        private void HideAvatarForExecution()
        {
            // Il renderer nasce in `SetupPlayer`, che precede qualunque pacchetto: se manca, non c'e' avatar da
            // nascondere e non c'e' nulla da salvare.
            if (CharacterRenderer == null)
                return;

            // ⚠️ SI SALVA SOLO LA PRIMA VOLTA. Vedi `executionRendererHidden`: alla seconda esecuzione il
            // renderer e' gia' spento DA NOI, e risalvarlo archivierebbe `false` come se fosse lo stato
            // originale - l'avatar non tornerebbe piu' visibile.
            if (!executionRendererHidden)
            {
                executionRendererWasEnabled = CharacterRenderer.enabled;
                executionRendererHidden = true;
            }

            CharacterRenderer.enabled = false;

            // ⚠️ IL MARKER DICE TRE COSE, E SERVONO TUTTE: che e' stato spento, DA QUALE VALORE, e A QUALE
            // valore va rimesso. Senza il valore di partenza "non si e' nascosto" e "si e' nascosto e non e'
            // tornato" restano indistinguibili nel log - che e' il modo in cui questo difetto e' passato
            // inosservato finora: il ripristino scriveva "di nuovo visibile" senza che nessuno avesse mai
            // scritto il contrario.
            ModLog.Info($"[ExecGhost RX] avatar {name} nascosto per l'esecuzione: SpriteRenderer.enabled " +
                        $"{executionRendererWasEnabled} -> {CharacterRenderer.enabled} (era " +
                        $"{executionRendererWasEnabled} PRIMA dell'esecuzione: e' il valore che il " +
                        "ripristino rimettera')");
        }

        /// <summary>
        /// IL FUSIBILE DEL FANTASMA, ed e' un fusibile a DUE stadi perche' i modi di restare appesi sono due.
        ///
        /// 1. LA FINE DELLA CLIP. Si campiona `AnimatorStateInfo.normalizedTime` e si guarda se la clip e'
        ///    arrivata in fondo. E' l'unico segnale disponibile su una prop che non esiste: `"INTERACTION_END"`
        ///    e' un ANIMATION EVENT DENTRO LA CLIP, e su questa macchina nessuno lo ascolta piu' (il
        ///    delegate del fantasma punta a un `Interactable` distrutto). E la fine vera la manda il
        ///    mittente, che pero' puo' tacere.
        ///    ⚠️ LA GUARDIA SUL TEMPO (2 s) E' OBBLIGATORIA E NON E' PIGNOLERIA: `normalizedTime` di una
        ///    clip NON ANCORA INTRAPRESA vale 1 (e' il default di uno stato mai giocato), quindi senza la
        ///    guardia il fantasma morirebbe nel frame stesso in cui nasce e non si vedrebbe NULLA.
        /// 2. IL TEMPO MASSIMO, che e' il fusibile che c'era gia' (`ExecutionTimeoutSeconds`): se nemmeno la
        ///    clip finisce - una clip in loop, un animator fermo - il fantasma muore lo stesso.
        ///
        /// ⚠️ E LOGGA, sempre: uno scatto del fusibile NON significa "l'esecuzione era lunga", significa che
        /// il byte di fine si e' perso o che la clip non e' finita. Nasconderlo dietro una distruzione
        /// silenziosa renderebbe indistinguibili "il pacchetto non e' arrivato" e "l'animazione non e' mai
        /// partita".
        /// </summary>
        private sealed class ExecutionGhostWatcher : MonoBehaviour
        {
            /// <summary>Solo per la riga di prova: dice QUALE stato si sta guardando, invece di darlo per buono.</summary>
            private static readonly int GhostStateHash = Animator.StringToHash("USED");

            /// <summary>
            /// Da quanto la clip e' in scena, e la soglia oltre la quale `normalizedTime` e' credibile.
            /// Vedi il commento della classe: senza questa guardia il fusibile scatterebbe nel frame in cui
            /// il fantasma nasce.
            /// </summary>
            private const float TrustClipAfterSeconds = 2f;

            private OtherPlayerScript owner;
            private GameObject watched;
            private Animator animator;
            private float bornAt;
            private bool reportedStart;
            private bool finished;

            public static bool Attach(OtherPlayerScript avat, GameObject ghostObject, Animator ghostAnimator)
            {
                if (avat == null || ghostObject == null || ghostAnimator == null)
                    return false;

                ExecutionGhostWatcher watcher = ghostObject.AddComponent<ExecutionGhostWatcher>();
                if (watcher == null)
                    return false;

                watcher.owner = avat;
                watcher.watched = ghostObject;
                watcher.animator = ghostAnimator;
                watcher.bornAt = Time.unscaledTime;
                return true;
            }

            private void Update()
            {
                // `finished` perche' `Destroy` e' differito: senza di esso questo `Update` potrebbe girare una
                // seconda volta nello stesso frame su un fantasma gia' condannato.
                if (finished || watched == null || animator == null || owner == null)
                    return;

                if (!reportedStart)
                {
                    reportedStart = true;
                    AnimatorStateInfo first = animator.GetCurrentAnimatorStateInfo(0);
                    ModLog.Info($"[ExecGhost RX] sorvegliante agganciato: hash={GhostStateHash} " +
                                $"normalizedTime={first.normalizedTime:F3} loop={first.loop} " +
                                $"lunghezza={first.length:F2}s");
                }

                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

                // Strada 1 - la clip e' finita. ⚠️ SOLO DOPO `TrustClipAfterSeconds`, perche' una clip non
                // ancora intrapresa riporta `normalizedTime = 1`.
                if (Time.unscaledTime - bornAt > TrustClipAfterSeconds && state.normalizedTime >= 0.99f)
                {
                    // ⚠️ LA RIGA CHE AZZERA IL TEMPO E UCCIDE IL FANTASMA E' UNA SOLA, ed e' questa: se il
                    // tempo restasse acceso, il fusibile a 12 s scatterebbe PIU' TARDI su un avatar che nel
                    // frattempo ha gia' ripreso a camminare, e scriverebbe un falso "pacchetto perso".
                    ModLog.Info($"[ExecGhost RX] la clip e' finita (normalizedTime={state.normalizedTime:F3} " +
                                $"dopo {Time.unscaledTime - bornAt:F2}s, senza il byte di fine): " +
                                "uccido il fantasma io");
                    Finish("fine della clip");
                    return;
                }

                // Strada 2 - il tempo massimo, il fusibile che c'era gia'.
                if (Time.unscaledTime - bornAt <= ExecutionTimeoutSeconds)
                    return;

                ModLog.Warn($"[ExecGhost RX] ne' il byte di fine ne' la fine della clip entro " +
                            $"{ExecutionTimeoutSeconds}s (normalizedTime={state.normalizedTime:F3}): " +
                            "uccido il fantasma io (pacchetto perso?)");
                Finish("fusibile");
            }

            private void Finish(string why)
            {
                finished = true;
                if (owner != null)
                    owner.OnExecutionGhostClipEnded(why);
            }
        }

        /// <summary>
        /// LA VIA DI USCITA DEL SORVEGLIANTE. Non uccide il fantasma direttamente: azzera lo stato con
        /// `RestoreExecutionAvatar`, che e' l'unico posto in cui l'avatar torna visibile e il fantasma muore.
        /// </summary>
        private void OnExecutionGhostClipEnded(string why)
        {
            RestoreExecutionAvatar(why);
        }

        /// <summary>The avatar's own ghost trail generator (A3). See SetGhostTrail.</summary>
        private GhostTrailGenerator GhostTrail { get; set; }

        /// <summary>
        /// The surrogate Entity the ghost trail reads its direction from (voice 61).
        ///
        /// It exists only to be that owner. The generator applies the owner's facing to every ghost it spawns,
        /// so pointing it at the penitent made the ghosts wear the LOCAL player's direction - a slide to the
        /// left drew ghosts looking right. It has to be its own GameObject rather than the avatar itself,
        /// because an Entity registers among the level's living entities and this avatar is not one of them.
        /// </summary>
        private Entity ghostTrailOwner { get; set; }

        public OtherPlayerAttackScript OtherPlayerAttack { get; private set; }

        public bool IsFacingRight => !CharacterRenderer.flipX;

        // Adds necessary components & initializes them
        public void SetupPlayer(PlayerStatus status)
        {
            playerStatus = status;
            playerStatus.SkinUpdateStatus = PlayerStatus.SkinStatus.NoUpdate;

            // Rendering
            CharacterRenderer = gameObject.AddComponent<SpriteRenderer>();
            CharacterRenderer.material = UnityReferences.PlayerMaterial;
            CharacterRenderer.sortingLayerName = "Player";
            CharacterRenderer.enabled = false;

            // Animation
            CharacterAnim = gameObject.AddComponent<Animator>();
            CharacterAnim.runtimeAnimatorController = UnityReferences.PlayerAnimator;

            // Sword handler
            GameObject sword = new GameObject("Sword");
            sword.transform.SetParent(transform);
            OtherPlayerAttack = sword.AddComponent<OtherPlayerAttackScript>();
            OtherPlayerAttack.SetupAttack();

            // Ghost trail (A3). Built here, where the avatar is born, because it has to be OURS: the penitent's
            // own generator belongs to the local player and cannot be lent out.
            GhostTrail = gameObject.AddComponent<GhostTrailGenerator>();

            // ⚠️ WITHOUT AN EntityOwner THIS GENERATOR IS INERT, AND IT SAYS SO ITSELF.
            //
            // The game logs "GhostTrailGenerator has no Entity. Traits won't execute." the moment this avatar is
            // created - one line, in the audio log, right after the avatar appears - and that line is the whole
            // story. Trait.Awake fills EntityOwner from GetComponentInParent<Entity>(); this avatar is a bare
            // GameObject with no Entity on it, and BOTH halves of the generator are guarded on that field:
            // Trait.Start skips OnStart, so _currentLevel stays null, and Trait.Update skips OnUpdate, so the
            // InvokeRepeating that would generate the ghosts is never even scheduled. Setting EnableGhostTrail
            // therefore did nothing at all - nothing reads it until the generator is actually running.
            //
            // This must come right after AddComponent: the component's Awake runs immediately, but its Start is
            // deferred to the end of the frame, so the owner is in place in time for Start to take the good path.
            //
            // ⭐ AND IT IS NOT THE PENITENT. Handing the generator Core.Logic.Penitent made the trail work but
            // face the wrong way, because the ghost's direction is read straight off this owner
            // (`flipX = EntityOwner.Status.Orientation == Left`) and the penitent carries the LOCAL player's
            // facing, not the avatar's: sliding left drew ghosts looking right. Hence a surrogate Entity that
            // exists only to face the way the avatar faces - see ghostTrailOwner.
            GameObject trailOwner = new GameObject("_coop_trail_owner");
            trailOwner.transform.SetParent(transform);
            ghostTrailOwner = trailOwner.AddComponent<Entity>();
            GhostTrail.EntityOwner = ghostTrailOwner;

            // MANDATORY as well: with followOrientation the generator applies the owner's facing to every
            // ghost, which is exactly what the surrogate is for.
            GhostTrail.followOrientation = true;

            // The look is copied from the penitent's generator rather than written here, because the colour and
            // the material live in the prefab and are not constants anyone can read out of the code.
            GhostTrailGenerator local = Core.Logic.Penitent == null
                ? null
                : Core.Logic.Penitent.GetComponentInChildren<GhostTrailGenerator>();

            // ⚠️ TrailColor and TimeStep have NO initialiser of their own - they default to (0,0,0,0) and to 0 -
            // so copying nothing would leave a trail that cannot be seen and a repeat interval of zero. Hence
            // the fallback chain: the local generator's values when it exists, sane ones when it does not.
            GhostTrail.TrailColor = local != null ? local.TrailColor : Color.white;
            GhostTrail.TimeStep = local != null && local.TimeStep > 0f ? local.TimeStep : 0.05f;
            GhostTrail.AlphaStep = local != null ? local.AlphaStep : 0.1f;
            GhostTrail.InitialGhostTrailAlpha = local != null ? local.InitialGhostTrailAlpha : 0.6f;
            GhostTrail.customMaterial = local != null ? local.customMaterial : null;
            GhostTrail.followRotation = local != null && local.followRotation;
            GhostTrail.followScale = local != null && local.followScale;

            GhostTrail.EnableGhostTrail = false;
        }

        /// <summary>
        /// Shows or hides this avatar's ghost trail - the wisps behind a dash, a plunge or a lunge (A3).
        ///
        /// followOrientation MUST stay false, and that is not a preference: GhostTrailGenerator.GetGhostTrail
        /// ends with `spriteRenderer.flipX = EntityOwner.Status.Orientation == EntityOrientation.Left`, and
        /// Trait fills EntityOwner from GetComponentInParent&lt;Entity&gt;(). This avatar is a bare GameObject
        /// with no Entity on it, so EntityOwner stays NULL and the default (true) makes every single trail
        /// generation throw. This is how the whole feature fails if the flag is forgotten: with a
        /// NullReferenceException inside the effect path, not with a missing trail.
        /// </summary>
        public void SetGhostTrail(bool on)
        {
            if (GhostTrail == null)
                return;

            // The direction is decided HERE, when the trail starts. A dash, a plunge or a slide does not turn
            // around halfway through, so reading the avatar's facing once is enough - and it keeps us off the
            // per-frame path, which is where a generator like this one is easiest to get wrong.
            if (on && ghostTrailOwner != null)
                ghostTrailOwner.SetOrientation(IsFacingRight ? EntityOrientation.Right : EntityOrientation.Left);

            GhostTrail.EnableGhostTrail = on;
        }

        public Vector2 CurrentPosition
        {
            set
            {
                transform.position = value;
            }
        }

        public byte CurrentAnimation
        {
            set
            {
                // ========================================================================================
                // ⭐⭐ [CorpsePeer] LA MORTE **NON** E' UNA PROVA DI VITA.
                // ========================================================================================
                //
                // Qui sotto c'e' la prova di vita: l'arrivo di un'animazione qualsiasi, mentre l'avatar e'
                // dato per morto, lo RIMETTE IN PIEDI. E' voluto, ed e' giusto per ogni animazione tranne
                // una: **quella di morte**. Senza questa esclusione il byte che `MainPlayerManager` spedisce
                // alla morte (vedi `CorpsePeerTick`) verrebbe letto come "e' tornato vivo", e l'avatar si
                // rialzerebbe nello stesso frame in cui si e' messo a terra - cioe' la mod si disferebbe DA
                // SOLA, e il difetto resterebbe quello di prima ("IN PIEDI BLOCCATO").
                //
                // ⚠️ E NON SI TOCCA LA PROVA DI VITA PER LE ALTRE ANIMAZIONI: e' il meccanismo su cui poggia
                // il risveglio del peer (`DeathCorpseFollow.Close()` -> `SendAllLocationData()` ->
                // animazione 0 = Idle), quindi resta intatto. Qui si aggiunge **un solo caso**, la morte.
                bool isDeathAnimation = PlayerStatus.IsDeathAnimation(value);

                // If this player was previously assumed dead but now they are receiving another anim, turn
                // them alive again - unless the animation IS the death, which is what confirms the corpse.
                //
                // ⚠️ LA CONDIZIONE GUARDA `deathStatusApplied`, **NON** `playerDead`: `Update` riabbassa
                // `playerDead` a ogni fotogramma che non sia quello in cui la posa e' "appena finita"
                // (`:1003-1006`, il ramo `else`), quindi mentre l'avatar e' a terra quel flag OSCILLA
                // vero/falso - e la prova di vita dipenderebbe dal fotogramma in cui arriva il pacchetto.
                // `deathStatusApplied` e' invece **esattamente cio' che `SetDeathStatus(true)` ha fatto
                // all'avatar** (`:1036-1045`): la prova di vita e' la cosa che lo disfa, quindi e' quello che
                // deve guardare. Con `playerDead` questa protezione non era affidabile nemmeno col predicato
                // giusto (e nei log non e' mai scattata: `vivo in piedi` = 0).
                if (deathStatusApplied && !isDeathAnimation)
                {
                    SetDeathStatus(false);
                    SetAnimatorBoolIfPresent("IS_DEAD", false);

                    // ⚠️ IL NOME SI LEGGE SOLO SE IL BYTE E' NELLA TABELLA, e non e' pignoleria: questa prova
                    // di vita scatta anche per le animazioni SPECIALI (byte >= 240, es. 247 = "entra nella
                    // porta"), e li' `AnimationStates.animations[value]` non esiste - indicizzare era un
                    // `IndexOutOfRangeException` dentro il marker della protezione stessa. Era invisibile
                    // solo perche' il ramo era irraggiungibile; ora che scatta, va scritto bene.
                    string arrivedName = value < AnimationStates.animations.Length
                        ? AnimationStates.animations[value].name
                        : ("animazione speciale tipo " + value);

                    ModLog.Info("[CorpsePeer] avatar del peer: vivo in piedi " +
                                "(l'avatar era a terra e arriva un'animazione che NON e' una morte: byte " +
                                value + "=" + arrivedName + " · IsDeathAnimation=false)");
                }

                if (value < 240)
                {
                    // Regular animation
                    if (playerStatus.SpecialAnimation > 0)
                    {
                        // Change back to regular animations
                        CharacterAnim.runtimeAnimatorController = UnityReferences.PlayerAnimator;
                        playerStatus.SpecialAnimation = 0;
                    }
                    CharacterAnim.SetBool("IS_CROUCH", false);

                    // Logic for ladder climbing

                    // Set required parameters to keep player object in this animation
                    PlayerAnimState animState = AnimationStates.animations[value];
                    for (int i = 0; i < animState.parameterNames.Length; i++)
                    {
                        CharacterAnim.SetBool(animState.parameterNames[i], animState.parameterValues[i]);
                    }

                    if (isDeathAnimation)
                    {
                        // ⚠️ `IS_DEAD` NON BASTA QUELLO CHE C'E' IN TABELLA, E VA GARANTITO.
                        // `AnimationStates.animations[47]` ("Death") dichiara UN solo parametro
                        // (`IS_DEAD = true`) e le altre due morti (`animations[48]` "Death Spike",
                        // `animations[49]` "Death Fall") nessuno, quindi per 48/49 il ciclo qui
                        // sopra non scrive nulla: su un avatar il cui Animator arriva da un'altra sequenza il
                        // bool puo' essere rimasto falso, e con `IS_DEAD` falso il controller puo' NON restare
                        // sulla posa di morte - cioe' il cadavere si rialza, che e' il difetto da correggere.
                        // Si scrive DOPO il ciclo, cosi' vince su qualunque valore esso abbia impostato.
                        SetAnimatorBoolIfPresent("IS_DEAD", true);
                    }

                    // ⭐ IL MARKER DEL PREDICATO, E STA SU **OGNI** ANIMAZIONE REGOLARE, non solo sulla morte.
                    // E' l'unico modo perche' un log provi anche il caso NEGATIVO - "byte 6=Run NON e' una
                    // morte" - che e' precisamente cio' che `PlayerStatus.IsDeathAnimation` sbagliava: prima
                    // della correzione rispondeva `true` per ogni byte della tabella, e il caso negativo non
                    // poteva comparire in nessun log (`morto a terra (byte 81=Healing)`).
                    ModLog.Info("[CorpsePeer] animazione del peer ricevuta: byte " + value + "=" +
                                animState.name + " · IsDeathAnimation=" +
                                (isDeathAnimation ? "true" : "false"));

                    CharacterAnim.Play(animState.name);

                    if (isDeathAnimation)
                    {
                        // ⭐ IL MARKER DEL RECEIVER. Senza, "la morte non e' arrivata", "e' arrivata e non si
                        // vede" e "e' arrivata e si e' rialzato" hanno lo stesso aspetto in un log.
                        // ⚠️ E IL FERMARSI E' DI POCHI CENTESIMI DOPO, NON DI SECONDI: `Update` riconosce la
                        // posa a `normalizedTime >= 0.95f` e chiama `SetDeathStatus(true)`, che spegne
                        // l'Animator sulla posa FINALE - la stessa su cui il gioco lo spegne sul Penitent
                        // (`PlayerDeathAnimationBehaviour.cs:33-36`). Fino a quel momento l'avatar sta
                        // cadendo: e' una dissolvenza, non un'attesa.
                        ModLog.Info("[CorpsePeer] avatar del peer: morto a terra " +
                                    "(animazione di morte ricevuta: byte " + value + "=" + animState.name +
                                    " · IsDeathAnimation=true) · l'Animator si fermera' da solo sulla posa finale " +
                                    "(normalizedTime>=0.95, come il Penitent locale)");
                    }
                }
                else
                {
                    // Special animation
                    if (PlaySpecialAnimation(value))
                    {
                        playerStatus.SpecialAnimation = value;
                        ModLog.Info("Playing special animation for " + name);
                    }
                    else
                        ModLog.Warn("Failed to play special animation for " + name);
                }

                // ⚠️ L'ORDINE NON E' PIGNOLERIA: `playerDead` si scrive SOLO alla fine, dopo che lo stato e'
                // stato davvero impostato. Se la morte non e' arrivata, `isDeathAnimation` e' falso e questa
                // riga RIABBASSA `playerDead` - ma solo dopo che la prova di vita ha gia' fatto il suo
                // lavoro, quindi il comportamento di prima e' intatto per ogni animazione normale.
                // ⚠️ NOTA: dopo la correzione la prova di vita NON legge piu' `playerDead` (legge
                // `deathStatusApplied`, che scrive solo `SetDeathStatus`); questa riga resta perche' `Update`
                // usa ancora il flag (`:1015`) e toccarlo non serve a niente.
                if (!isDeathAnimation)
                    playerDead = false;
            }
        }

        public bool CurrentDirection
        {
            set
            {
                CharacterRenderer.flipX = value;
            }
        }

        public void ApplySkinTexture()
        {
            CharacterRenderer.enabled = true;
            CharacterRenderer.material.SetTexture("_PaletteTex", playerStatus.SkinTexture);
        }

        // Gets the animator controller of an interactable object in the scene & plays special animation
        private bool PlaySpecialAnimation(byte type)
        {
            if (type == 240 || type == 241 || type == 242)
            {
                // Prie Dieu
                PrieDieu priedieu = FindObjectOfType<PrieDieu>();
                if (priedieu == null)
                    return false;

                CharacterAnim.runtimeAnimatorController = priedieu.transform.GetChild(4).GetComponent<Animator>().runtimeAnimatorController;
                if (type == 240)
                {
                    CharacterAnim.SetTrigger("ACTIVATION");
                }
                else if (type == 241)
                {
                    CharacterAnim.SetTrigger("KNEE_START");
                }
                else
                {
                    CharacterAnim.Play("Stand Up");
                }
            }
            else if (type == 243 || type == 244)
            {
                // Collectible item
                CollectibleItem item = FindObjectOfType<CollectibleItem>();
                if (item == null)
                    return false;

                CharacterAnim.runtimeAnimatorController = item.transform.GetChild(1).GetComponent<Animator>().runtimeAnimatorController;
                CharacterAnim.Play(type == 244 ? "Floor Collection" : "Halfheight Collection");
            }
            else if (type == 245)
            {
                // Chest
                Chest chest = FindObjectOfType<Chest>();
                if (chest == null)
                    return false;

                CharacterAnim.runtimeAnimatorController = chest.transform.GetChild(2).GetComponent<Animator>().runtimeAnimatorController;
                CharacterAnim.SetTrigger("USED");
            }
            else if (type == 246)
            {
                // Lever
                Lever lever = FindObjectOfType<Lever>();
                if (lever == null)
                    return false;

                CharacterAnim.runtimeAnimatorController = lever.transform.GetChild(2).GetComponent<Animator>().runtimeAnimatorController;
                CharacterAnim.SetTrigger("DOWN");
            }
            else if (type == 247 || type == 248 || type == 249)
            {
                // Door
                Door door = FindObjectOfType<Door>();
                if (door == null)
                    return false;

                CharacterAnim.runtimeAnimatorController = door.transform.GetChild(3).GetComponent<Animator>().runtimeAnimatorController;
                if (type == 247)
                {
                    CharacterAnim.SetTrigger("OPEN_ENTER");
                }
                else if (type == 248)
                {
                    CharacterAnim.SetTrigger("CLOSED_ENTER");
                }
                else
                {
                    CharacterAnim.SetTrigger("KEY_ENTER");
                }
            }
            else if (type == 250 || type == 251)
            {
                // Fake penitent
                GameObject logic = GameObject.Find("LOGIC");
                if (logic == null)
                    return false;

                CharacterAnim.runtimeAnimatorController = logic.transform.GetChild(3).GetComponent<Animator>().runtimeAnimatorController;
                CharacterAnim.Play(type == 250 ? "FakePenitent laydown" : "FakePenitent gettingUp");
            }
            else
            {
                return false;
            }

            return true;
        }

        // Finishes playing a special animation and returns to idle
        public void FinishSpecialAnimation()
        {
            if (playerStatus.SpecialAnimation >= 247 && playerStatus.SpecialAnimation <= 249)
            {
                // If finished entering door, disable renderer
                CharacterRenderer.enabled = false;
            }

            CurrentAnimation = 0;
        }

        // Check if death animation is playing
        private void Update()
        {
            AnimatorStateInfo state = CharacterAnim.GetCurrentAnimatorStateInfo(0);
            if (!playerDead && state.normalizedTime >= 0.95f && (state.IsName("Death") || state.IsName("Death Spike") || state.IsName("Death Fall") || state.IsName("Grounding Over")))
            {
                SetDeathStatus(true);
                playerDead = true;
            }
            else
            {
                playerDead = false;
            }

            // A6 - IL FUSIBILE DELL'ESECUZIONE. Vedi `PlayExecutionAnimation` per il come e il perche'.
            //
            // ⚠️ PERCHE' QUI E NON DOVE STA IL RESTO DI A6. `OtherPlayerAttackScript` viene chiamato SOLO da
            // un effetto in ARRIVO: se i pacchetti si fermano - che e' precisamente il caso che il fusibile
            // deve coprire - quel codice non gira piu' e il fusibile non scatterebbe mai. Questo `Update` gira
            // ogni fotogramma sull'avatar, ed e' l'unico posto che puo' accorgersi che il tempo e' passato
            // senza che sia arrivato nulla.
            //
            // ⚠️ `Time.unscaledTime` E NON `Time.time`, come per la scivolata: il metro di paragone dev'essere
            // un orologio che non si ferma quando il gioco mette in pausa - e l'esecuzione e' la cutscene che
            // piu' facilmente porta il gioco in uno stato di tempo alterato (`_currentLevel.TimeScale`).
            //
            // ⚠️ E LO STATO E' D'ISTANZA, quindi il confronto e' su `this` e non su un avatar risolto per
            // nome: due giocatori che eseguono insieme sono il caso normale in co-op, e ognuno ha il suo
            // `ExecutionStartedAt`. Il fusibile del primo non tocca il secondo.
            if (ExecutionStartedAt > 0f &&
                Time.unscaledTime - ExecutionStartedAt > ExecutionTimeoutSeconds)
            {
                // ⚠️ SCATTA E LO DICE, ed e' la parte importante. Uno scatto NON significa "l'animazione era
                // lunga": significa che il byte di fine si e' perso, oppure che questo avatar e' rimasto
                // appeso. Nasconderlo dietro un ripristino silenzioso renderebbe di nuovo indistinguibili
                // "il pacchetto non e' arrivato" e "l'animazione non e' mai partita".
                ModLog.Warn($"[ExecAnim RX] il byte di fine non e' arrivato entro " +
                            $"{ExecutionTimeoutSeconds}s: ripristino io l'avatar (pacchetto perso?)");
                RestoreExecutionAvatar("fusibile");
            }
        }

        private void SetDeathStatus(bool dead)
        {
            CharacterAnim.enabled = !dead;
            OtherPlayerAttack.SetHitboxStatus(!dead);

            // ⭐ IL TESTIMONE DELLA PROVA DI VITA. Si scrive QUI e non altrove perche' e' l'unica riga che sa
            // se l'avatar e' rimasto a terra: `CurrentAnimation` la interroga per rialzarlo, e `Update` la
            // spegne quando la posa di morte e' finita. Vedi `deathStatusApplied`.
            deathStatusApplied = dead;
        }

        /// <summary>
        /// Scrive un bool sull'Animator dell'avatar **solo se quel parametro esiste davvero**.
        ///
        /// ⭐ PERCHE' SI GUARDA PRIMA DI SCRIVERE. `Animator.SetBool` con un nome che il controller non ha NON
        /// lancia: Unity scrive un warning e va avanti, quindi una riga sbagliata qui NON si vedrebbe come
        /// errore - si vedrebbe come un parametro che "non fa niente", che e' il guasto peggiore da leggere.
        /// Guardando prima, la mod puo' DIRLO.
        ///
        /// ⭐⭐ E LA VIA E' QUELLA DEL GIOCO, non una inventata qui: `NpcDamage.cs:57` risponde alla stessa
        /// domanda con
        ///     `OwnerEntity.Animator.parameters.Any(param => param.name.Equals(paramName))`
        /// ⇒ `Animator.parameters` e `AnimatorControllerParameter` **esistono nella build di Unity di questo
        /// progetto**, ed e' una prova, non una firma ricordata. (`RuntimeAnimatorController` NON li espone:
        /// provato, e il compilatore ha risposto `does not contain a definition for 'parameters'`.)
        /// </summary>
        private void SetAnimatorBoolIfPresent(string parameter, bool value)
        {
            try
            {
                if (CharacterAnim == null)
                    return;

                AnimatorControllerParameter[] parameters = CharacterAnim.parameters;
                if (parameters == null)
                    return;

                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name != parameter)
                        continue;

                    CharacterAnim.SetBool(parameter, value);
                    return;
                }

                ModLog.Warn($"[CorpsePeer] l'Animator dell'avatar non ha il parametro \"{parameter}\": " +
                            $"la posa di morte si regge sulla sola clip (il ciclo dei parametri di " +
                            $"`AnimationStates` la salta, come per ogni altro stato)");
            }
            catch (System.Exception e)
            {
                ModLog.Warn($"[CorpsePeer] lettura dei parametri dell'Animator fallita ({e.GetType().Name}): " +
                            $"si prosegue senza scrivere \"{parameter}\"");
            }
        }

        /// <summary>
        /// A6 - LA TERZA VIA DI USCITA DEL RIPRISTINO, ed e' quella GARANTITA.
        ///
        /// `OtherPlayerManager.RemoveAllActivePlayers`/`RemoveActivePlayer` distruggono questo GameObject in
        /// OGNI percorso - `Multiplayer.OnDisconnect`, il cambio scena (`LevelLoaded`) e il
        /// `ReceiveLeaveScene` - quindi `OnDestroy` li copre tutti. E' la stessa garanzia su cui poggia
        /// `StopPeerDashSound`.
        ///
        /// ⚠️ SENZA QUESTA RIGA NON SI ROMPE NULLA VISIBILMENTE, E E' PROPRIO IL PROBLEMA. L'avatar viene
        /// distrutto, quindi non c'e' piu' nessuno in posa da guardare: quello che resterebbe e' il
        /// `ExecutionStartedAt` di un oggetto morto - irraggiungibile, e quindi innocuo. La riga serve
        /// perche' `RestoreExecutionAvatar` e' l'UNICO posto in cui lo stato si azzera, e lasciare una
        /// seconda strada che lo azzera a mano e' il modo di farne divergere una. E perche' `OnDestroy` e'
        /// l'unico punto che gira anche se tutto il resto tace.
        /// </summary>
        private void OnDestroy()
        {
            // ⚠️ E ADESSO C'E' UNA SECONDA COSA DA PORTARE VIA: IL FANTASMA. Prima di A6-fantasma questa
            // riga non poteva lasciare nulla in scena, perche' l'unica cosa che l'esecuzione toccava era
            // l'Animator di questo stesso avatar - che sparisce con lui. Il fantasma invece e' un GameObject
            // STACCATO (`SetParent(null, true)`), quindi non e' figlio di nessuno: se questo avatar muore
            // mentre l'esecuzione e' in scena, senza questa riga resterebbe un secondo penitent a girare per
            // il livello, immortale e invisibile a tutti i controlli di questo script.
            RestoreExecutionAvatar("OnDestroy");
        }

        // Finish a special animation when the event is received
        public void LaunchEvent(string eventName)
        {
            if (eventName == "INTERACTION_END")
                FinishSpecialAnimation();
        }

        public bool BleedOnImpact() => false;
        public bool SparkOnImpact() => false;
        public Vector3 GetPosition() => transform.position;

        public void Damage(Hit hit)
        {
            Config config = Main.Multiplayer.config;
            if (!config.enablePvP || (!config.enableFriendlyFire && Main.Multiplayer.PlayerTeam == playerStatus.Team))
                return;

            AttackType attack = CalculateAttack(hit);
            if (attack == AttackType.NoDamage || _blockedAttacks.Contains(attack))
                return;

            // Calculate damage amount based on attack type, sword level, and equipment
            float damageAmount = Main.Multiplayer.DamageCalculator.CalculateOffense(attack);

            ModLog.Warn($"Sending hit {attack} to {playerStatus.Name} ({damageAmount} damage)");
            Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy();
            Main.Multiplayer.AttackManager.ShowDamageEffects(playerStatus.Name);
            Main.Multiplayer.NetworkManager.SendAttack(playerStatus.Name, attack, (byte)damageAmount);
        }

        private AttackType CalculateAttack(Hit hit)
        {
            string attacker = hit.AttackingEntity.name;
            string animation = AnimationStates.animations
                .Select(x => x.name)
                .FirstOrDefault(x => Core.Logic.Penitent.Animator.GetCurrentAnimatorStateInfo(0).IsName(x)) ?? "Unknown";
            DamageArea.DamageType type = hit.DamageType;
            DamageArea.DamageElement element = hit.DamageElement;
            float force = hit.Force;

            ModLog.Info($"{playerStatus.Name} received damage [{attacker},{animation},{type},{element},{force}]");
            //Main.Multiplayer.LogError("Hit sound id: " + hit.HitSoundId);

            if (attacker == "Penitent(Clone)")
            {
                if (animation == "Combo_4")
                {
                    return AttackType.ComboNormal;
                }
                if (animation == "ComboFinisherUp")
                {
                    return AttackType.ComboUp;
                }
                if (animation == "ComboFinisherDown")
                {
                    return AttackType.ComboDown;
                }
                if (animation == "Charged Attack")
                {
                    return hit.Force == 0 ? AttackType.ChargedProjectile : AttackType.Charged;
                }
                if (animation == "AuraTransform")
                {
                    return force == 0 ? AttackType.PrayerHit : AttackType.Lorquiana;
                }
                if (Core.Logic.Penitent.LungeAttack.IsUsingAbility)
                {
                    return AttackType.Lunge;
                }
                if (Core.Logic.Penitent.VerticalAttack.IsUsingAbility)
                {
                    return AttackType.Vertical;
                }

                return force == 0 ? AttackType.RangedExplosion : AttackType.Slash;
            }
            
            if (attacker == "RangeAttackProjectile(Clone)")
            {
                return AttackType.Ranged;
            }
            if (attacker == "PenitentCloisteredGemBullet(Clone)")
            {
                return AttackType.Gem;
            }
            if (attacker == "PR203ElmFireTrapLightning(Clone)" || attacker.StartsWith("ElmFireTrap")) // This might be triggered by real traps in MaH
            {
                return AttackType.Tirana;
            }
            if (attacker == "PenitentVerticalBeam(Clone)")
            {
                return AttackType.Debla;
            }
            if (attacker == "PenitentTarantoDivineLight(Clone)")
            {
                return AttackType.Taranto;
            }
            if (attacker == "CrawlerBullet_Base(Clone)")
            {
                return AttackType.Verdiales;
            }
            if (attacker == "PrayerPoisonAreaEffect(Clone)")
            {
                return AttackType.PoisonMist;
            }
            if (attacker.StartsWith("PenitentShield"))
            {
                return AttackType.Shield;
            }
            if (attacker == "MiriamPortalPrayer(Clone)")
            {
                return AttackType.Miriam;
            }
            if (attacker.StartsWith("MiriamSpike"))
            {
                return AttackType.MiriamSpike;
            }
            if (attacker == "GuardianPrayer(Clone)")
            {
                return AttackType.Aubade;
            }

            // Cherubs
            // Cante Jondo

            return AttackType.NoDamage;
        }

        private readonly AttackType[] _blockedAttacks =
        {
            AttackType.Ranged,
            AttackType.ChargedProjectile,
            AttackType.RangedExplosion,
            AttackType.Gem,
            AttackType.PrayerHit,
            AttackType.Taranto,
            AttackType.Verdiales,
            AttackType.Lorquiana,
            AttackType.Tirana,
            AttackType.PoisonMist,
            AttackType.Shield,
            AttackType.Miriam,
            AttackType.Aubade,
            AttackType.Cherubs,
            AttackType.CanteJondo,
        };
    }
}
