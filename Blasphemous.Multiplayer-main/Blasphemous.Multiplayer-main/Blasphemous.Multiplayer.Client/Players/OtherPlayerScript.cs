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

        private SpriteRenderer CharacterRenderer { get; set; }
        private Animator CharacterAnim { get; set; }

        // ============================================================================================
        // A6 - L'ESECUZIONE SULL'AVATAR. Suonare uno stato, non istanziare una prop.
        // ============================================================================================
        //
        // ⭐ LA SCOPERTA CHE RENDE QUESTO POSSIBILE, in una riga: l'animazione dell'esecuzione NON e' un
        // asset da caricare, e' uno STATO DI ANIMATOR chiamato "USED", e vive in un fantasma che il gioco
        // suona con `interactorAnimator.SetTrigger("USED")`. Quel fantasma e' un clone del penitent guidato
        // dal controller DEL PENITENT - e la prova sta nel fatto che lo STESSO campo `interactorAnimator`
        // suona le animazioni in ginocchio di PrieDieu (`KNEE_START`), che sono animazioni del penitent.
        //
        // ⭐⭐ E QUEL CONTROLLER E' GIA' ADDOSSO A QUESTO AVATAR: `SetupPlayer` gli assegna
        // `UnityReferences.PlayerAnimator`, che e' il `runtimeAnimatorController` del penitent LOCALE.
        //   ⇒ Non serve ottenerlo, non serve cambiarlo, non serve `Resources.Load`, non serve riflessione:
        //   serve il `Play` dell'hash di "USED".
        //
        // ⭐ E LA MOD LO FA GIA'. `PlaySpecialAnimation` installa sull'avatar il controller del fantasma di
        // un interactable e per il PETTO suona LETTERALMENTE `SetTrigger("USED")`. Questa non e' una tecnica
        // nuova: e' la stessa, sullo stesso stato, senza il cambio di controller (che per PrieDieu e petto
        // serve solo perche' li' lo stato NON e' nel controller del penitent).
        //
        // ⚠️ PERCHE' NON SI ISTANZIA LA PROP. Perche' la prop porta un `MonoBehaviour` il cui `Update()`
        // legge l'input LOCALE:
        //     Interactable.Update -> Execution.OnUpdate -> ReInput.players.GetPlayer(0).GetButtonDown(8)
        // Cioe': se la prop esiste su questa macchina, QUESTA macchina puo' premerci sopra e far partire
        // `Execution.OnUse` - che accende `Core.Input.SetBlocker("EXECUTION", true)` e lo spegne solo
        // all'ultima riga. Quella e' la voce 54. E neutralizzarla non e' gratis: `enabled = false` fermerebbe
        // anche `Start()`, lasciando `Penitent`/`_cameraZoom`/`_currentLevel` a null e trasformando
        // `Execution.Damage` in un NRE.
        //   ⇒ LA NEUTRALIZZAZIONE PIU' SICURA DI UN COMPONENTE E' NON CREARLO. Qui non si crea nulla, e il
        //   blocker non e' "improbabile": e' IMPOSSIBILE, perche' non c'e' alcun `Update` che possa
        //   chiamarlo.
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
        /// Accende o spegne l'animazione dell'esecuzione su QUESTO avatar.
        ///
        /// ⚠️ E' UN METODO D'ISTANZA. Chi lo chiama e' `OtherPlayerAttackScript.PlayEffectAnimation`, che
        /// risale all'avatar dal padre esattamente come fa gia' per la scia. Nessuna ricerca per nome, nessun
        /// caso "avatar non trovato", nessun pacchetto applicato all'avatar sbagliato.
        /// </summary>
        public void PlayExecutionAnimation(bool start)
        {
            if (!start)
            {
                // La FINE. `RestoreExecutionAvatar` si arrende da sola se non c'e' nulla da fare.
                RestoreExecutionAvatar("byte di fine");
                return;
            }

            // ⚠️ UNA SECONDA ESECUZIONE NON SI ACCAVALLA: si riparte. Se una e' gia' in corso (il byte di
            // fine precedente e' andato perso, o il peer ha eseguito due nemici di fila) il tempo si
            // riazzera invece di sommare. Senza questo, la seconda animazione erediterebbe il fusibile della
            // prima e potrebbe essere tagliata a meta'.
            ExecutionStartedAt = Time.unscaledTime;

            // ⚠️⚠️ "USED" NON E' UNO STATO: E' UN TRIGGER. Ed e' la scoperta che chiude la questione.
            //
            // Il gioco fa `interactorAnimator.SetTrigger("USED")` (Execution.cs:166). "USED" e' un
            // PARAMETRO dell'animator, e il trigger fa partire una TRANSIZIONE verso uno stato il cui nome
            // puo' essere qualsiasi cosa.
            //   ⇒ `Animator.Play(Animator.StringToHash("USED"))` non trova NULLA, e `HasState(0,"USED")`
            //   risponde `false` ANCHE sul controller del fantasma che l'esecuzione la recita davvero - che
            //   e' esattamente quello che dicevano i log.
            //   ⇒ NON si suona uno stato: si preme lo STESSO trigger che preme il gioco.
            //
            // ⚠️ E IL CONTROLLER VA INSTALLATO, perche' il trigger vive nel controller del FANTASMA della
            // prop (un clone del penitent), non in quello dell'avatar. E il fantasma e' raggiungibile SENZA
            // ISTANZIARE NULLA: `Interactable.interactorAnimator` e' un campo del PREFAB, e leggere un prefab
            // non esegue `Awake`, non crea GameObject, non fa partire alcun `Update` ⇒ NON puo' accendere
            // l'input blocker, che e' la voce 54.
            RuntimeAnimatorController controller = OtherPlayerAttackScript.ResolveExecutionController();
            if (controller == null)
            {
                ModLog.Warn("[ExecAnim RX] il controller del fantasma non si e' risolto: " +
                            "l'animazione non esce (e NON e' un problema di rete)");
                return;
            }

            CharacterAnim.runtimeAnimatorController = controller;

            // LA PROBE GIUSTA. Prima si chiedeva `HasState(0, "USED")`: la domanda sbagliata, che risponde
            // `false` anche su un controller che FUNZIONA. Adesso si cerca il PARAMETRO trigger "USED" fra i
            // parametri del controller, che e' la cosa che il gioco preme davvero.
            //
            // ⚠️ E SI PREME COMUNQUE, anche se il controllo non lo trova: `SetTrigger` con un trigger
            // assente non lancia (logga un warning dell'engine), e un controllo che BLOCCA l'azione e' un
            // modo di non far mai partire nulla per un falso negativo. La riga qui sotto e' la prova, non
            // una condizione.
            bool hasTrigger = HasTrigger(CharacterAnim, "USED");

            CharacterAnim.SetTrigger(ExecutionStateHash);

            // La durata REALE, dal controller invece che da una costante: una riga che tara il fusibile con
            // un dato invece che con una scommessa.
            ModLog.Info($"[ExecAnim RX] trigger USED su {name} parametri={CharacterAnim.parameterCount} " +
                        $"trigger_trovato={hasTrigger} " +
                        $"(durata={CharacterAnim.GetCurrentAnimatorStateInfo(0).length:F2}s)");
        }

        /// <summary>
        /// Vero se il controller dell'animator dichiara un PARAMETRO di tipo trigger con questo nome.
        ///
        /// ⚠️ E' LA DOMANDA CHE PRIMA SI FACEVA MALE. `HasState(0, hash("USED"))` chiede se esiste uno STATO
        /// chiamato "USED", e la risposta e' `false` anche sul controller del gioco, perche' "USED" e' un
        /// TRIGGER che porta a uno stato dal nome diverso. Chiedere la cosa giusta e' la differenza fra una
        /// probe che accusa il gioco e una che dice la verita'.
        /// </summary>
        private static bool HasTrigger(Animator animator, string triggerName)
        {
            if (animator == null)
                return false;

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type == AnimatorControllerParameterType.Trigger &&
                    parameters[i].name == triggerName)
                    return true;
            }

            return false;
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
            if (ExecutionStartedAt <= 0f)
                return;

            ExecutionStartedAt = 0f;

            // ⚠️ PERCHE' `Play(0)` E NON `SetTrigger`/`ResetTrigger`. Lo stato "USED" e' entrato con un `Play`
            // esplicito, quindi si esce allo stesso modo: si torna allo stato 0 del layer 0, che e' lo stato
            // di riposo del controller del penitent. Non si usa `Rebind()` ne' si spegne l'Animator:
            // spegnerlo fermerebbe anche l'`Update` di questo stesso script, che ricava da
            // `GetCurrentAnimatorStateInfo(0)` lo stato di morte - cioe' un avatar che non muore piu'
            // visivamente.
            CharacterAnim.Play(0);

            // ⚠️ IL RIPRISTINO DEL CONTROLLER, E DOPO IL RIPIEGO E' OBBLIGATORIO E NON UNA COMODITA'.
            // Il ripiego INSTALLA il controller del fantasma della prop: senza questa riga l'avatar
            // resterebbe con quel controller, cioe' rotto del tutto (animazioni del penitent assenti), e
            // `Play(0)` presupporrebbe uno stato di riposo che non e' il suo.
            // Quando invece lo stato "USED" era gia' nel controller dell'avatar, questa assegnazione e'
            // idempotente: rimette lo stesso riferimento che c'e' gia'.
            CharacterAnim.runtimeAnimatorController = UnityReferences.PlayerAnimator;

            // ⚠️ IL RIPRISTINO DEL CONTROLLER STA QUI, E SOLO SE SERVE. Su questa strada il controller non e'
            // MAI stato cambiato: e' gia' quello del penitent, ed e' quello che `Play(0)` presuppone.
            // Se lo stato "USED" non fosse nel controller, servirebbe la riga
            //     CharacterAnim.runtimeAnimatorController = UnityReferences.PlayerAnimator;
            // che e' esattamente quella che la mod usa gia' per tornare dall'animazione speciale.

            ModLog.Info($"[ExecAnim RX] fine ({why}): avatar {name} ripristinato");
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
                // If this player was previously assumed dead but now they are receiving another anim, turn them alive again
                if (playerDead)
                {
                    SetDeathStatus(false);
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
                    CharacterAnim.Play(animState.name);
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
