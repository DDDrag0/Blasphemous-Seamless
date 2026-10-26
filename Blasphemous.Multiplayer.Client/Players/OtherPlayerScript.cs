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
        /// True once <see cref="SetDeathStatus(bool)"/> actually put the avatar on the ground (Animator
        /// disabled on the death pose). Used by the life-proof check instead of <c>playerDead</c>, which
        /// oscillates every frame while the corpse is on the ground.
        /// </summary>
        private bool deathStatusApplied = false;

        private SpriteRenderer CharacterRenderer { get; set; }
        private Animator CharacterAnim { get; set; }

        // Execution ghost: instantiate the prop prefab, keep the ghost child, destroy the rest. On
        // this path Execution.OnUse never runs, so the execution input blocker, TimeScale, and local
        // Penitent are never touched - only the ghost plays the animation.

        /// <summary>Trigger name read verbatim from the game's Execution.OnUse ("USED").</summary>
        private static readonly int ExecutionStateHash = Animator.StringToHash("USED");

        /// <summary>Start time of the execution on this avatar, unscaled. Zero when idle.</summary>
        private float ExecutionStartedAt;

        /// <summary>
        /// Max clip duration before the fuse kills the ghost. Wide on purpose: real duration depends on
        /// the execution's slow-motion and animator speed composition, which cannot be computed from
        /// here. Markers [ExecAnim TX] START/END give the exact duration in the logs; shrink when
        /// measured.
        /// </summary>
        private const float ExecutionTimeoutSeconds = 12f;

        /// <summary>
        /// Starts or ends the execution animation on THIS avatar. Called from
        /// <c>OtherPlayerAttackScript.PlayEffectAnimation</c> via the parent avatar.
        /// </summary>
        public void PlayExecutionAnimation(bool start)
        {
            if (!start)
            {
                RestoreExecutionAvatar("byte di fine");
                return;
            }

            // A second execution does not stack: reset and kill any prior ghost.
            if (ghost != null)
            {
                ModLog.Info("[ExecGhost RX] a second execution arrives while the first is still in " +
                            "scena: il fantasma vecchio muore adesso");
                DestroyGhost("seconda esecuzione");
            }

            ExecutionStartedAt = Time.unscaledTime;

            GameObject prefab = OtherPlayerAttackScript.ResolveExecutionPrefab();
            if (prefab == null)
            {
                ModLog.Warn("[ExecGhost RX] the execution prefab did not resolve (the enemy is not " +
                            "reachable or has no EntityExecution): no ghost, and it is NOT a " +
                            "problema di rete");
                return;
            }

            // Instantiate the prop prefab. The penitent has no execution animation - the game hides
            // it via ShowPlayer(false) inside Execution.OnUse - so only the ghost is drawn; installing
            // the ghost's controller on the avatar plays the first frame and leaves it invisible.
            GameObject prop = UnityEngine.Object.Instantiate(prefab, transform.position, transform.rotation);
            if (prop == null)
            {
                ModLog.Warn("[ExecGhost RX] prefab instantiation failed: no ghost");
                return;
            }

            // The ghost is the child carrying the Interactable.interactorAnimator (the field the
            // game's Execution.OnUse presses). GetComponentInChildren<Execution> includes the root.
            Execution execution = prop.GetComponentInChildren<Execution>(true);
            Animator ghostAnimator = execution == null || OtherPlayerAttackScript.InteractorAnimatorField == null
                ? null
                : OtherPlayerAttackScript.InteractorAnimatorField.GetValue(execution) as Animator;

            if (ghostAnimator == null)
            {
                ModLog.Warn("[ExecGhost RX] the instance does not expose `interactorAnimator` " +
                            $"(`Execution` found={execution != null}): the ghost does not isolate itself, the prop " +
                            "gets destroyed and the execution is not seen");
                UnityEngine.Object.Destroy(prop);
                return;
            }

            // Detach the ghost so destroying the prop does not take it too.
            GameObject ghostObject = ghostAnimator.gameObject;
            ghostObject.transform.SetParent(null, true);
            ghostObject.SetActive(true);

            // Destroy the prop in this same frame. Awake runs on Instantiate, but OnUpdate (which
            // reads input and could enable the EXECUTION blocker) runs on the next frame - deferred
            // Destroy guarantees it never fires. ExecutionAnimationEvents on the ghost reads its
            // parent Execution, which no longer exists (the prop is destroyed and the ghost is
            // detached), so its Start throws an NRE; its sound event was the prop's activationSound,
            // already lost with the prop. ApplyRemoteExecution plays the execution hit sound
            // separately.
            UnityEngine.Object.Destroy(prop);

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
                            "ghost: they looked for an `Execution` in a parent that no longer exists (prop " +
                            "distrutta + fantasma staccato), e `Start()` lanciava NRE. Il suono " +
                            "of the execution for the viewer is PlaySimpleHitToEnemy, already played by " +
                            "ApplyRemoteExecution: here only the prop's `activationSound` was lost.");

            ghost = ghostObject;
            ghostAnimator.SetTrigger(ExecutionStateHash);

            // Hide the avatar only now that the ghost exists: every early return above leaves the
            // avatar visible, so an execution that will not be seen does not hide anyone.
            HideAvatarForExecution();

            if (ExecutionGhostWatcher.Attach(this, ghost, ghostAnimator))
                ModLog.Info($"[ExecGhost RX] fantasma istanziato su {name}: prop distrutta nello stesso " +
                            $"frame, trigger USED premuto, durata clip=" +
                            $"{ghostAnimator.GetCurrentAnimatorStateInfo(0).length:F2}s");
            else
                ModLog.Warn("[ExecGhost RX] ghost instantiated but the watchdog could not " +
                            "agganciare: resta il solo fusibile a 12s");
        }

        /// <summary>
        /// Restores this avatar to its previous state. Safe to call at any time and idempotent: the
        /// end byte, the fuse, and OnDestroy may all call it.
        /// </summary>
        private void RestoreExecutionAvatar(string why)
        {
            // The three states to restore are time, ghost, and avatar renderer. The renderer is the
            // only one that could leave the avatar invisible forever, so its flag is part of the
            // guard.
            if (ExecutionStartedAt <= 0f && ghost == null && !executionRendererHidden)
                return;

            ExecutionStartedAt = 0f;
            DestroyGhost(why);

            // The avatar's own Animator was never touched: the ghost has its own Animator and
            // controller. Only the renderer was hidden, and it is restored to the value it had
            // before the execution (not to true), so an avatar hidden by a door or a missing skin
            // stays hidden.
            if (CharacterRenderer != null && executionRendererHidden)
            {
                executionRendererHidden = false;
                CharacterRenderer.enabled = executionRendererWasEnabled;

                string outcome = executionRendererWasEnabled
                    ? "was VISIBLE before the execution, so it becomes visible again"
                    : "was ALREADY OFF before the execution (death, door or skin): it stays off as it was";

                ModLog.Info($"[ExecGhost RX] avatar {name} ripristinato: SpriteRenderer.enabled " +
                            $"False -> {CharacterRenderer.enabled} ({outcome})");
            }

            ModLog.Info($"[ExecGhost RX] fine ({why}): fantasma ucciso, avatar {name} ripristinato " +
                        "(the avatar's Animator was not touched during the whole execution)");
        }

        /// <summary>
        /// Kills this avatar's execution ghost, if any. Single place that destroys it: end byte,
        /// fuse, and clip end all reach here.
        /// </summary>
        private void DestroyGhost(string why)
        {
            if (ghost == null)
                return;

            GameObject dying = ghost;
            ghost = null;

            ExecutionGhostWatcher watcher = dying.GetComponent<ExecutionGhostWatcher>();
            if (watcher != null)
                watcher.enabled = false;

            Animator dyingAnimator = dying.GetComponent<Animator>();
            if (dyingAnimator != null)
            {
                dyingAnimator.StopPlayback();
                dyingAnimator.enabled = false;
            }

            ModLog.Info($"[ExecGhost RX] fantasma ucciso ({why})");
            UnityEngine.Object.Destroy(dying);
        }

        /// <summary>Active execution ghost for this avatar, or null. Instance field so two co-op executions do not collide.</summary>
        private GameObject ghost;

        /// <summary>
        /// CharacterRenderer.enabled value before the execution hid it. Required because the
        /// renderer is toggled by several paths (SetupPlayer, FinishSpecialAnimation, ApplySkinTexture)
        /// and restoring to true would reveal the peer where it was meant to be hidden.
        /// </summary>
        private bool executionRendererWasEnabled;

        /// <summary>
        /// True when THIS script hid the renderer (so there is something to restore). Distinct from
        /// ExecutionStartedAt because a second execution while the first is on scene must not save
        /// the already-hidden state.
        /// </summary>
        private bool executionRendererHidden;

        /// <summary>
        /// Hides this avatar's renderer for the execution, saving the prior value. Mirrors what the
        /// game does to the local Penitent via ShowPlayer(false). Animator is left on so the death
        /// detection and fuse keep running.
        /// </summary>
        private void HideAvatarForExecution()
        {
            if (CharacterRenderer == null)
                return;

            if (!executionRendererHidden)
            {
                executionRendererWasEnabled = CharacterRenderer.enabled;
                executionRendererHidden = true;
            }

            CharacterRenderer.enabled = false;

            ModLog.Info($"[ExecGhost RX] avatar {name} nascosto per l'esecuzione: SpriteRenderer.enabled " +
                        $"{executionRendererWasEnabled} -> {CharacterRenderer.enabled} (era " +
                        $"{executionRendererWasEnabled} BEFORE the execution: it is the value the " +
                        "ripristino rimettera')");
        }

        /// <summary>
        /// Ghost fuse with two paths: clip end (normalizedTime >= 0.99 after a 2s trust window,
        /// because an unentered clip reports normalizedTime = 1) and max duration
        /// (ExecutionTimeoutSeconds). Logs the trigger reason so a lost end packet is not
        /// indistinguishable from a normal short execution.
        /// </summary>
        private sealed class ExecutionGhostWatcher : MonoBehaviour
        {
            private static readonly int GhostStateHash = Animator.StringToHash("USED");

            /// <summary>Trust window: before this, normalizedTime = 1 means "clip not started".</summary>
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

                if (Time.unscaledTime - bornAt > TrustClipAfterSeconds && state.normalizedTime >= 0.99f)
                {
                    ModLog.Info($"[ExecGhost RX] la clip e' finita (normalizedTime={state.normalizedTime:F3} " +
                                $"after {Time.unscaledTime - bornAt:F2}s, with no end byte): " +
                                "uccido il fantasma io");
                    Finish("end of the clip");
                    return;
                }

                if (Time.unscaledTime - bornAt <= ExecutionTimeoutSeconds)
                    return;

                ModLog.Warn($"[ExecGhost RX] neither the end byte nor the end of the clip within " +
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

        /// <summary>Clip-end exit path from the watcher; restores through the single restore method.</summary>
        private void OnExecutionGhostClipEnded(string why)
        {
            RestoreExecutionAvatar(why);
        }

        /// <summary>Avatar's ghost trail generator (A3).</summary>
        private GhostTrailGenerator GhostTrail { get; set; }

        /// <summary>
        /// Surrogate Entity the ghost trail reads its direction from. It exists only to be that owner:
        /// the generator applies the owner's facing to every ghost, so pointing it at the Penitent
        /// would use the LOCAL player's direction. Separate GameObject because Entity registers among
        /// the level's living entities and this avatar is not one of them.
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

            // Ghost trail (A3). Built here where the avatar is born: it must be its own, not the
            // Penitent's.
            GhostTrail = gameObject.AddComponent<GhostTrailGenerator>();

            // The generator is inert without an EntityOwner (its Trait.Awake fills it via
            // GetComponentInParent<Entity>(), and this avatar has no Entity). Setting the owner right
            // after AddComponent is required because Start is deferred to the end of the frame. The
            // owner is NOT the Penitent: that would use the local player's facing for every ghost.
            GameObject trailOwner = new GameObject("_coop_trail_owner");
            trailOwner.transform.SetParent(transform);
            ghostTrailOwner = trailOwner.AddComponent<Entity>();
            GhostTrail.EntityOwner = ghostTrailOwner;

            GhostTrail.followOrientation = true;

            // Copy look from the local generator's prefab; fall back to sane values when the local
            // generator is missing (its fields have no initializer of their own).
            GhostTrailGenerator local = Core.Logic.Penitent == null
                ? null
                : Core.Logic.Penitent.GetComponentInChildren<GhostTrailGenerator>();

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
        /// Toggles this avatar's ghost trail. followOrientation stays false because the generator
        /// applies the owner's facing to every ghost it spawns, and the owner must be our surrogate
        /// Entity (not the Penitent), otherwise the ghosts use the local player's direction.
        /// </summary>
        public void SetGhostTrail(bool on)
        {
            if (GhostTrail == null)
                return;

            // Direction is decided at trail start; the avatar does not turn mid-action, so reading it
            // once here keeps this off the per-frame path.
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
                // A death animation is NOT proof of life: without this exclusion the death byte sent
                // by MainPlayerManager.CorpsePeerTick would flip the peer back up on the same frame,
                // undoing the fix. Other animations keep the life-proof behavior, which is what wakes
                // the avatar after respawn (SendAllLocationData sends animation 0 = Idle).
                bool isDeathAnimation = PlayerStatus.IsDeathAnimation(value);

                // The life-proof check uses deathStatusApplied, not playerDead: playerDead oscillates
                // every frame while the avatar is on the ground, so the check would depend on the
                // packet's arrival frame.
                if (deathStatusApplied && !isDeathAnimation)
                {
                    SetDeathStatus(false);
                    SetAnimatorBoolIfPresent("IS_DEAD", false);

                    // Read the name only if the byte is in the table: this also fires for special
                    // animations (>= 240, e.g. 247 = enter door), and animations[value] does not
                    // exist for those.
                    string arrivedName = value < AnimationStates.animations.Length
                        ? AnimationStates.animations[value].name
                        : ("animazione speciale tipo " + value);

                    ModLog.Info("[CorpsePeer] avatar del peer: vivo in piedi " +
                                "(the avatar was down and an animation arrives that is NOT a death: byte " +
                                value + "=" + arrivedName + " · IsDeathAnimation=false)");
                }

                if (value < 240)
                {
                    // Regular animation
                    if (playerStatus.SpecialAnimation > 0)
                    {
                        CharacterAnim.runtimeAnimatorController = UnityReferences.PlayerAnimator;
                        playerStatus.SpecialAnimation = 0;
                    }
                    CharacterAnim.SetBool("IS_CROUCH", false);

                    // Set required parameters to keep player object in this animation
                    PlayerAnimState animState = AnimationStates.animations[value];
                    for (int i = 0; i < animState.parameterNames.Length; i++)
                    {
                        CharacterAnim.SetBool(animState.parameterNames[i], animState.parameterValues[i]);
                    }

                    // Death animations 48/49 have no parameters in the table; force IS_DEAD=true so
                    // the controller cannot lift the corpse off the death pose.
                    if (isDeathAnimation)
                    {
                        SetAnimatorBoolIfPresent("IS_DEAD", true);
                    }

                    // Marker on every regular animation, so the negative case (byte not a death) is
                    // visible in the log too.
                    ModLog.Info("[CorpsePeer] animazione del peer ricevuta: byte " + value + "=" +
                                animState.name + " · IsDeathAnimation=" +
                                (isDeathAnimation ? "true" : "false"));

                    CharacterAnim.Play(animState.name);

                    if (isDeathAnimation)
                    {
                        ModLog.Info("[CorpsePeer] avatar del peer: morto a terra " +
                                    "(death animation received: byte " + value + "=" + animState.name +
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

                // playerDead is written only after state is applied; used by Update, not by the
                // life-proof check anymore.
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

            // Execution fuse. Lives here rather than in OtherPlayerAttackScript because that script
            // only runs on incoming effects - which is exactly the case the fuse must cover when
            // packets stop. Per-avatar state so two co-op executions do not interfere. Uses unscaled
            // time because executions run during slow-motion.
            if (ExecutionStartedAt > 0f &&
                Time.unscaledTime - ExecutionStartedAt > ExecutionTimeoutSeconds)
            {
                ModLog.Warn($"[ExecAnim RX] the end byte did not arrive within " +
                            $"{ExecutionTimeoutSeconds}s: ripristino io l'avatar (pacchetto perso?)");
                RestoreExecutionAvatar("fusibile");
            }
        }

        private void SetDeathStatus(bool dead)
        {
            CharacterAnim.enabled = !dead;
            OtherPlayerAttack.SetHitboxStatus(!dead);

            deathStatusApplied = dead;
        }

        /// <summary>
        /// Sets a bool on the avatar's Animator only if that parameter exists. SetBool on a missing
        /// parameter only logs a Unity warning; checking first lets this class report the actual
        /// state. Uses the same accessor pattern the game uses in NpcDamage.cs (parameters array).
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

                ModLog.Warn($"[CorpsePeer] the avatar's Animator has no parameter \"{parameter}\": " +
                            $"the death pose relies on the clip alone (the parameter loop of " +
                            $"`AnimationStates` skips it, as for every other state)");
            }
            catch (System.Exception e)
            {
                ModLog.Warn($"[CorpsePeer] lettura dei parametri dell'Animator fallita ({e.GetType().Name}): " +
                            $"continuing without writing \"{parameter}\"");
            }
        }

        /// <summary>
        /// Third exit path: guaranteed cleanup on destroy. Without it, the detached ghost would
        /// survive as an orphan while this avatar is destroyed. The state reset itself is already
        /// covered by RestoreExecutionAvatar.
        /// </summary>
        private void OnDestroy()
        {
            // The ghost is detached (SetParent(null)), so it is not destroyed with this avatar
            // automatically; without this call it would remain in the level.
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