using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Combat;
using Gameplay.GameControllers.Enemies.Projectiles;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Entities.Audio;
using Gameplay.GameControllers.Penitent.Audio;
using System.Collections;
using UnityEngine;
using Framework.Util;
using Tools.Level;

namespace Blasphemous.Multiplayer.Client.Players
{
    public class OtherPlayerAttackScript : MonoBehaviour
    {
        private const float DEBLA_DURATION = 1.2f;
        private const float VERDIALES_DURATION = 2f;

        // DashDustGenerator's start/stop prefab fields are private [SerializeField] with no accessor,
        // so they are read reflectively once and cached. GetField is a string lookup and this runs on
        // the effect path; if the lookup fails the effect degrades to "no dust" instead of throwing
        // (this is inside the packet handler, where an exception would re-present the packet).
        private static readonly System.Reflection.FieldInfo StartDashDustField =
            typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator).GetField(
                "startDashDustPrefab",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static readonly System.Reflection.FieldInfo StopDashDustField =
            typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator).GetField(
                "stopDashDustPrefab",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private SpriteRenderer SwordRenderer { get; set; }
        private Animator SwordAnim { get; set; }
        private BoxCollider2D DamageArea { get; set; }

        public void SetupAttack()
        {
            // Add components
            transform.localPosition = Vector3.zero;
            SwordRenderer = gameObject.AddComponent<SpriteRenderer>();
            SwordAnim = gameObject.AddComponent<Animator>();
            SwordAnim.runtimeAnimatorController = UnityReferences.PlayerSwordAnimator;

            // Create damage area collider
            gameObject.layer = LayerMask.NameToLayer("Enemy");
            DamageArea = gameObject.AddComponent<BoxCollider2D>();
            DamageArea.offset = new Vector2(0, 0.92f);
            DamageArea.size = new Vector2(0.665f, 1.866f);
            DamageArea.isTrigger = true;
        }

        // Upon death, the hitbox should be disabled
        public void SetHitboxStatus(bool active)
        {
            DamageArea.enabled = active;
        }

        // When receiving an attack from another player, make their character play the sword/prayer animation
        public void PlayEffectAnimation(EffectType effect, bool facingRight)
        {
            PlayEffectAnimation(effect, facingRight, false, Vector2.zero);
        }

        /// <summary>
        /// Replays an effect on the avatar. <paramref name="hasPosition"/> says whether <paramref name="position"/>
        /// is meaningful; only effects that happen at a specific place (ground impact, slide dust) use it.
        /// </summary>
        public void PlayEffectAnimation(EffectType effect, bool facingRight, bool hasPosition, Vector2 position)
        {
            SwordRenderer.flipX = !facingRight;

            // Dash sound expiry: a lost STOP would leave a latched event on forever. Only checked while
            // the instance is valid, i.e. during a peer dash.
            if (dashSoundInstance.isValid() &&
                Time.unscaledTime - dashSoundStartedAt > DASH_SOUND_TIMEOUT)
            {
                ModLog.Warn("[Sound] the peer's slide did not get the STOP within " +
                            DASH_SOUND_TIMEOUT + "s: la spengo io (pacchetto perso?)");
                StopPeerDashSound();
            }

            switch (effect)
            {
                case EffectType.SidewaysGrounded:
                    SwordAnim.Play("Basic1_Lv1");
                    break;
                case EffectType.UpwardsGrounded:
                    SwordAnim.Play("BasicUpward_Lv1");
                    break;
                case EffectType.SidewaysAir:
                    SwordAnim.Play("Air1_Lv1");
                    break;
                case EffectType.UpwardsAir:
                    SwordAnim.Play("AirUpward_Lv1");
                    break;
                case EffectType.Crouch:
                    SwordAnim.Play("Crouch_Lv1");
                    break;
                case EffectType.Debla:
                    StartCoroutine(DisplayDebla());
                    break;
                // The ghost trail belongs to the avatar, not to the sword.
                case EffectType.TrailOn:
                case EffectType.TrailOff:
                {
                    OtherPlayerScript owner = GetComponentInParent<OtherPlayerScript>();
                    if (owner != null)
                        owner.SetGhostTrail(effect == EffectType.TrailOn);
                    break;
                }
                // Slide dust and its sound, at the place the sender slid. hasPosition is checked rather
                // than assumed: a payload without coordinates would put the effect at world origin.
                case EffectType.SlideDust:
                {
                    if (hasPosition)
                        PlaySlideDust(position, facingRight);
                    break;
                }
                // Dash dust (movement dash). Two prefabs in the game (start / stop), so two bytes.
                case EffectType.DashDustStart:
                case EffectType.DashDustStop:
                {
                    if (hasPosition)
                        PlayDashDust(effect == EffectType.DashDustStart, position, facingRight);
                    break;
                }
                // Ground impact: dust, beam and landing sound. Five shapes because the sender saw the
                // fall height and its own skill.
                case EffectType.LandingImpactBase:
                case EffectType.LandingImpact1:
                case EffectType.LandingImpact2:
                case EffectType.LandingImpact3:
                case EffectType.LandingImpact3Beam:
                {
                    if (hasPosition)
                        PlayLandingImpact(effect, position);
                    break;
                }
                // One-shot peer sounds: footsteps and attack swings. The avatar has no EntityAudio, and
                // PlayOneShotEvent reads the Owner (local Penitent) for panning - so this goes through
                // PlayOneShotFromCatalog (positional, catalog key). EventOneShotPanned takes a bank path
                // and would throw on a catalog key.
                case EffectType.FootStep:
                case EffectType.Attack1:
                case EffectType.Attack2:
                case EffectType.AirAttack1:
                case EffectType.AirAttack2:
                case EffectType.HeavyAttack:
                case EffectType.ChargedRelease:
                case EffectType.ComboHit:
                case EffectType.IdleSword:
                case EffectType.VerticalStart:
                case EffectType.VerticalFalling:
                case EffectType.ParryStart:
                    PlayPeerSound(effect, position);
                    break;
                // Dash sound: latched event, needs START/STOP.
                case EffectType.DashSoundStart:
                {
                    if (hasPosition)
                        PlayPeerDashSound(position);
                    break;
                }
                case EffectType.DashSoundStop:
                    // No hasPosition guard: STOP carries no position.
                    StopPeerDashSound();
                    break;
                // Parry clang: one-shot through EventOneShotPanned.
                case EffectType.ParryHit:
                {
                    if (hasPosition)
                        PlayPeerParryHitSound(position);
                    break;
                }
                case EffectType.ParryHitStop:
                    StopPeerParryHitSound();
                    break;
                // Successful parry flash sound: this case was missing, so the byte was dropped in
                // silence. It is a one-shot catalog key, resolved on the local catalog.
                case EffectType.ParryAttack:
                {
                    if (!hasPosition)
                    {
                        ModLog.Info("[Sound RX] peer parry succeeded, but the packet carries no " +
                                    "position: not playing it (not putting it at the origin)");
                        break;
                    }

                    ModLog.Info($"[Sound RX] parry riuscito del peer ({position.x:F1},{position.y:F1})");
                    PlayPeerSound(effect, position);
                    break;
                }
                // ParryStartCatalog is deliberately caseless: the sender emits it alongside ParryStart
                // (109) on every guard, and both resolve to the same event - playing both would double
                // the peer's guard sound.
                // Execution: start/stop the execution animation on the avatar. State lives on
                // OtherPlayerScript (per-avatar), not here (this script is on the Sword child).
                case EffectType.ExecutionStart:
                case EffectType.ExecutionEnd:
                {
                    OtherPlayerScript executionOwner = GetComponentInParent<OtherPlayerScript>();
                    if (executionOwner != null)
                        executionOwner.PlayExecutionAnimation(effect == EffectType.ExecutionStart);
                    break;
                }
            }
        }

        /// <summary>
        /// The catalog key for an effect. The value is not written here and does not travel on the
        /// wire: it comes from PenitentAudio's public const strings, so a game update is followed by
        /// reading the constant instead of freezing a transmitted string.
        ///
        /// AirAttack2 uses BasicAttack2EventKey because PenitentAttackAnimations.PlayAirAttack2 calls
        /// PlayBasicAttack2 - PenitentAudio.BasicAirAttack2EventKey exists but no method consumes it.
        /// </summary>
        private static string SoundKeyFor(EffectType effect)
        {
            switch (effect)
            {
                case EffectType.FootStep: return PenitentAudio.FootStepEventKey;
                case EffectType.Attack1: return PenitentAudio.BasicAttack1EventKey;
                case EffectType.Attack2: return PenitentAudio.BasicAttack2EventKey;
                case EffectType.AirAttack1: return PenitentAudio.BasicAirAttack1EventKey;
                case EffectType.AirAttack2: return PenitentAudio.BasicAttack2EventKey;
                case EffectType.HeavyAttack: return PenitentAudio.PenitentHeavyAttackEventKey;
                case EffectType.ChargedRelease: return PenitentAudio.ReleaseChargedAttackEventKey;
                case EffectType.ComboHit: return PenitentAudio.ComboHit;
                case EffectType.IdleSword: return PenitentAudio.IdleModeSword;
                case EffectType.VerticalStart: return PenitentAudio.VerticalAttackStart;
                case EffectType.VerticalFalling: return PenitentAudio.VerticalAttackFalling;
                case EffectType.ParryAttack: return PenitentAudio.ParryEventKey;
                case EffectType.ParryStartCatalog: return PenitentAudio.StartParryEventKey;
                default: return null;
            }
        }

        /// <summary>
        /// Probes whether the catalog key exists on this machine. PlayOneShotFromCatalog is silent
        /// when the key is missing, and "missing key" versus "missing packet" would then be
        /// indistinguishable. FMODAudioCatalog.GetSfx is public; _audioCatalogs is private and read
        /// via reflection. Results are cached, including nulls, so the probe runs once per key.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> SoundPathCache =
            new System.Collections.Generic.Dictionary<string, string>();

        private static readonly System.Reflection.FieldInfo AudioCatalogsField =
            typeof(Framework.Managers.FMODAudioManager).GetField(
                "_audioCatalogs",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// The bank path of a key if this machine has it, null otherwise (with a one-shot warning).
        /// Cached nulls are meaningful: they suppress the warn on subsequent calls.
        /// </summary>
        private static string ResolveCatalogPath(string key)
        {
            // A cached value can legitimately be null, so TryGetValue alone is not enough - returning
            // null here is "already decided: not present" and must not retry or re-log.
            string cached;
            if (SoundPathCache.TryGetValue(key, out cached))
                return cached;

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return null;

            // If a future build renames the private field, the probe degrades to "silent" and does
            // not block sound. The probe is a bonus, not a guard.
            Framework.Audio.FMODAudioCatalog[] catalogs = null;
            if (AudioCatalogsField != null)
                catalogs = AudioCatalogsField.GetValue(audio) as Framework.Audio.FMODAudioCatalog[];

            string path = null;
            if (catalogs != null)
            {
                for (int i = 0; i < catalogs.Length; i++)
                {
                    if (catalogs[i] == null)
                        continue;

                    Framework.Audio.FMODAudioCatalog.FMODIndexedClip clip = catalogs[i].GetSfx(key);
                    if (clip == null)
                        continue;

                    // Copy the value as-is: this is the path the game itself passes to
                    // RuntimeManager.CreateInstance, and normalizing it here would be an invented
                    // difference.
                    path = clip.FMODKey;
                    break;
                }
            }

            if (path == null)
            {
                ModLog.Warn($"[Sound] catalogue key \"{key}\" does not exist on this machine: " +
                            "the peer's sound will NOT play (and it is not a network problem)");
                SoundPathCache[key] = null;
                return null;
            }

            ModLog.Info($"[Sound] chiave \"{key}\" -> percorso \"{path}\"");
            SoundPathCache[key] = path;
            return path;
        }

        // --- Footstep audio: PlayOneShotEvent(Motion) sets 14 FMOD floor-material parameters, which
        // EventOneShotPanned does not. So the step reproduces the game's three-step chain: create,
        // set materials + panning, then start and release. Materials are read from the local penitent
        // via reflection (EntityAudio's fields are protected); failure degrades to default materials,
        // not silence.

        /// <summary>
        /// The 14 FMOD floor-material parameter names, matching EntityAudio.SetFloorMaterialParams.
        /// The exact spelling matters (getParameter is by name; "SecretFloor" is not "Secret").
        /// </summary>
        private static readonly string[] FloorMaterialParams =
        {
            EntityAudio.LabelParamDirt,
            EntityAudio.LabelParamWater,
            EntityAudio.LabelParamSnow,
            EntityAudio.LabelParamStone,
            EntityAudio.LabelParamWood,
            EntityAudio.LabelParamMarble,
            EntityAudio.LabelParamFlesh,
            EntityAudio.LabelParamMetal,
            EntityAudio.LabelParamMud,
            EntityAudio.LabelParamSecret,
            EntityAudio.LabelParamGrass,
            EntityAudio.LabelParamDemake,
            EntityAudio.LabelParamPalio,
            EntityAudio.LabelParamSnake,
        };

        /// <summary>
        /// EntityAudio's protected float fields holding the values, in the same order. Resolved once
        /// (14 GetField per sound would be wasteful); a miss degrades to default materials.
        /// </summary>
        private static readonly System.Reflection.FieldInfo[] FloorMaterialFields = ResolveFloorMaterialFields();

        private static System.Reflection.FieldInfo[] ResolveFloorMaterialFields()
        {
            string[] names =
            {
                "DirtValue", "WaterValue", "SnowValue", "StoneValue", "WoodValue", "MarbleValue",
                "FleshValue", "MetalValue", "MudValue", "SecretValue", "GrassValue", "DemakeValue",
                "PalioValue", "SnakeValue",
            };

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            System.Reflection.FieldInfo[] fields = new System.Reflection.FieldInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
                fields[i] = typeof(EntityAudio).GetField(names[i], flags);

            return fields;
        }

        private static bool floorMaterialReflectionChecked;

        /// <summary>
        /// Sets the 14 floor-material parameters on the instance from the LOCAL Penitent's audio. If
        /// reflection failed, nothing is set (event keeps its defaults, which is the pre-fix state).
        /// </summary>
        private static void ApplyLocalFloorMaterialParams(FMOD.Studio.EventInstance instance)
        {
            if (!floorMaterialReflectionChecked)
            {
                floorMaterialReflectionChecked = true;

                bool ok = FloorMaterialFields.Length > 0 && FloorMaterialFields[0] != null;
                ModLog.Info("[Sound] materiale del terreno per i passi del peer: " +
                            (ok
                                ? "letto dal penitent locale (14 parametri)"
                                : "NOT AVAILABLE (reflection does not find the EntityAudio fields): " +
                                  "il passo esce coi materiali di default del banco"));
            }

            Gameplay.GameControllers.Penitent.Penitent penitent = Core.Logic.Penitent;
            if (penitent == null || penitent.Audio == null)
                return;

            EntityAudio localAudio = penitent.Audio;

            for (int i = 0; i < FloorMaterialParams.Length; i++)
            {
                System.Reflection.FieldInfo field = FloorMaterialFields[i];
                if (field == null)
                    continue;

                FMOD.Studio.ParameterInstance parameter;
                instance.getParameter(FloorMaterialParams[i], out parameter);

                // getParameter returns an invalid instance when the event has no such parameter;
                // setValue on an invalid instance would throw inside FMOD.
                if (parameter.isValid())
                    parameter.setValue((float)field.GetValue(localAudio));
            }
        }

        /// <summary>
        /// Reproduces the footstep sound through the game's local chain, so the 14 floor-material
        /// parameters are applied. PlayOneShotFromCatalog would skip them and the step would be
        /// silent.
        /// </summary>
        private static void PlayPeerMotionSound(string key, Vector2 position)
        {
            string path = ResolveCatalogPath(key);
            if (string.IsNullOrEmpty(path))
                return;

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            UnityEngine.Vector3 world = EffectWorldPosition(position);

            // CreateCatalogEvent takes a KEY, not a path (it resolves via FindSfxInCatalogs). Passing
            // the resolved path here would look up nothing and produce an invalid instance with no
            // warning.
            FMOD.Studio.EventInstance instance = audio.CreateCatalogEvent(key, world);

            if (!instance.isValid())
                return;

            ApplyLocalFloorMaterialParams(instance);
            EntityAudio.SetPanning(instance, world);

            instance.start();
            instance.release();
        }

        /// <summary>Latched event instance for the peer's dash sound. One per avatar: START and STOP
        /// arrive in separate packets and this field is the only link between them.</summary>
        private FMOD.Studio.EventInstance dashSoundInstance;

        /// <summary>When the dash instance started playing, in Time.unscaledTime.</summary>
        private float dashSoundStartedAt;

        /// <summary>
        /// Fuse for the peer's dash sound. A latched event that loses its STOP stays on until the game
        /// restarts; 5s is well past any real dash and only says the STOP was lost.
        /// </summary>
        private const float DASH_SOUND_TIMEOUT = 5f;

        /// <summary>
        /// Starts the peer's dash sound at the peer's position. Same order as the game's PlayDashSound:
        /// stop whatever is playing, then start the new one.
        /// </summary>
        private void PlayPeerDashSound(Vector2 position)
        {
            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            string key = PenitentAudio.DashEventKey;

            // PlayEventWithCatalog takes the KEY, not the resolved path.
            if (string.IsNullOrEmpty(ResolveCatalogPath(key)))
                return;

            StopPeerDashSound();

            audio.PlayEventWithCatalog(
                ref dashSoundInstance,
                key,
                EffectWorldPosition(position));

            if (dashSoundInstance.isValid())
                dashSoundStartedAt = Time.unscaledTime;
        }

        /// <summary>
        /// Stops the peer's dash sound. Safe to call at any time: StopEvent starts with an isValid
        /// check. clearHandle inside StopEvent ensures the instance is not left "valid" after release.
        /// </summary>
        private void StopPeerDashSound()
        {
            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
            {
                dashSoundInstance = default(FMOD.Studio.EventInstance);
                return;
            }

            audio.StopEvent(ref dashSoundInstance);
            dashSoundInstance = default(FMOD.Studio.EventInstance);
        }

        /// <summary>
        /// Plays the parry clang via EventOneShotPanned. Previously this method had two call sites
        /// (CreateCatalogEvent + start, and EventOneShotPanned): the first was the residue of an
        /// experiment, doubled the start, and was not the missing sound. Only EventOneShotPanned
        /// remains. The clang fuse, its instance field and its timeout were removed: with a
        /// one-shot that releases itself, "event stuck on" is no longer reachable, and a fuse that
        /// can never fire is a lie.
        /// </summary>
        private void PlayPeerParryHitSound(Vector2 position)
        {
            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            string key = PenitentAudio.ParryHitEventKey;

            string path = ResolveCatalogPath(key);
            if (string.IsNullOrEmpty(path))
                return;

            StopPeerParryHitSound();

            Vector3 at = EffectWorldPosition(position);

            audio.EventOneShotPanned(path, at);
            ModLog.Info($"[Sound RX] clang acceso ({at.x:F1},{at.y:F1}) via EventOneShotPanned " +
                        $"(single start: no instance to stop, no fuse)");
        }

        /// <summary>
        /// Stops the peer's parry clang. Currently inert: the clang is a one-shot via
        /// EventOneShotPanned and holds no EventInstance. Kept as the STOP cleanup path (STOP still
        /// arrives from the sender) and as the place to re-add instance management if the clang
        /// ever becomes latched again.
        /// </summary>
        private void StopPeerParryHitSound()
        {
            // No-op by design.
        }

        /// <summary>
        /// Cleanup on disconnect / scene change. FMOD events are registered inside FMOD and outlive
        /// GameObject destruction, so without this the peer's dash sound would play forever.
        /// OnDestroy is a guaranteed cleanup path: OtherPlayerManager destroys the GameObject on
        /// every removal path.
        /// </summary>
        private void OnDestroy()
        {
            StopPeerDashSound();
            StopPeerParryHitSound();
        }

        /// <summary>
        /// Resolves the execution prop prefab (for OtherPlayerScript to spawn a real ghost).
        ///
        /// The prefab is cached because resolution depends on the enemy, and the enemy's death
        /// packet may arrive right after ExecutionStart and destroy it. The prefab is an asset and
        /// never changes, so caching it once closes that race.
        /// </summary>
        public static GameObject ResolveExecutionPrefab()
        {
            if (cachedExecutionPrefab != null)
                return cachedExecutionPrefab;

            Enemy enemy = EnemySyncManager.LastExecutedEnemy;
            if (enemy == null || enemy.gameObject == null)
                return null;

            // includeInactive: true because the prefab is almost certainly inactive until used.
            EntityExecution entityExecution = enemy.GetComponentInChildren<EntityExecution>(true);
            if (entityExecution == null || entityExecution.ExecutionPrefab == null)
                return null;

            cachedExecutionPrefab = entityExecution.ExecutionPrefab;
            return cachedExecutionPrefab;
        }

        /// <summary>
        /// Returns the ghost's RuntimeAnimatorController from the prefab, trying three sources.
        ///
        /// Path 1 uses Interactable.interactorAnimator (the field the game uses to play USED) via
        /// reflection. Path 2 finds the Animator carrying an AnimatorEvent (the predicate
        /// Interactable.CheckAnimationEvents uses). Path 3 falls back to HasState("USED"). Path 3 is
        /// a safety net only; path 1 is the correct answer because it is the field the game plays
        /// with.
        /// </summary>
        public static RuntimeAnimatorController ResolveExecutionController()
        {
            GameObject prefab = ResolveExecutionPrefab();
            if (prefab == null)
                return null;

            Interactable[] interactables = prefab.GetComponentsInChildren<Interactable>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                if (interactables[i] == null)
                    continue;

                Animator interactor = InteractorAnimatorField != null
                    ? InteractorAnimatorField.GetValue(interactables[i]) as Animator
                    : null;

                if (interactor == null || interactor.runtimeAnimatorController == null)
                    continue;

                ModLog.Info($"[ExecAnim RX] controller del fantasma dal campo `interactorAnimator` di " +
                            $"{interactables[i].GetType().Name} (prefab: {interactables.Length} Interactable)");
                return interactor.runtimeAnimatorController;
            }

            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] == null || animators[i].runtimeAnimatorController == null)
                    continue;
                if (animators[i].GetComponent<AnimatorEvent>() == null)
                    continue;

                ModLog.Info($"[ExecAnim RX] controller del fantasma dall'Animator con AnimatorEvent " +
                            $"(animator {i + 1} di {animators.Length})");
                return animators[i].runtimeAnimatorController;
            }

            int executionState = Animator.StringToHash("USED");
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] == null || animators[i].runtimeAnimatorController == null)
                    continue;

                if (animators[i].HasState(0, executionState))
                {
                    ModLog.Info($"[ExecAnim RX] controller del fantasma da HasState(\"USED\") " +
                                $"(animator {i + 1} di {animators.Length})");
                    return animators[i].runtimeAnimatorController;
                }
            }

            ModLog.Warn($"[ExecAnim RX] none of the {interactables.Length} Interactable nor of the " +
                        $"{animators.Length} Animator of the prefab exposes the ghost: the animation does not exist");
            return null;
        }

        /// <summary>
        /// Interactable.interactorAnimator via reflection: it is protected, and reflection is the same
        /// technique used for _audioCatalogs, DashDustGenerator and GuardSlide. Public because
        /// OtherPlayerScript uses it to detach the ghost from the prop.
        /// </summary>
        public static readonly System.Reflection.FieldInfo InteractorAnimatorField =
            typeof(Tools.Level.Interactable).GetField(
                "interactorAnimator",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>Execution prop prefab, resolved once. See ResolveExecutionPrefab.</summary>
        private static GameObject cachedExecutionPrefab;

        /// <summary>
        /// Peer's guard sound. Reads the path from the LOCAL Parry component (the value lives in the
        /// asset and does not travel). PlayOneShotEvent is not used: this sound has no FxSoundCategory,
        /// so it does not set the 14 floor parameters.
        /// </summary>
        private static void PlayPeerGuardSound(Vector2 position)
        {
            Gameplay.GameControllers.Penitent.Abilities.Parry parry =
                Core.Logic.Penitent == null
                    ? null
                    : Core.Logic.Penitent.GetComponentInChildren<Gameplay.GameControllers.Penitent.Abilities.Parry>(true);

            if (parry == null)
                return;

            string path = parry.ParryGuardFx;
            if (string.IsNullOrEmpty(path))
            {
                ModLog.Warn("[Sound] `Parry.ParryGuardFx` is empty on this machine: " +
                            "the peer's guard does not play (and it is NOT a network problem)");
                return;
            }

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            ModLog.Info($"[Sound] guardia del peer -> percorso \"{path}\"");
            audio.EventOneShotPanned(path, EffectWorldPosition(position));
        }

        private static void PlayPeerSound(EffectType effect, Vector2 position)
        {
            // Footstep goes through PlayPeerMotionSound because of the floor-material parameters.
            if (effect == EffectType.FootStep)
            {
                PlayPeerMotionSound(PenitentAudio.FootStepEventKey, position);
                return;
            }

            // IdleSword is also a Motion category sound: same material-parameter requirement.
            if (effect == EffectType.IdleSword)
            {
                PlayPeerMotionSound(PenitentAudio.IdleModeSword, position);
                return;
            }

            // Guard sound has a distinct path: its key lives in the asset (Parry.ParryGuardFx), not
            // in a readable constant. StartParryEventKey exists but its consumer (PlayStartParry) has
            // no C# caller in the assembly.
            if (effect == EffectType.ParryStart)
            {
                PlayPeerGuardSound(position);
                return;
            }

            string key = SoundKeyFor(effect);
            if (string.IsNullOrEmpty(key))
                return;

            string path = ResolveCatalogPath(key);
            if (string.IsNullOrEmpty(path))
                return;

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            // z comes from the local penitent, matching how the game's own effects are placed.
            audio.EventOneShotPanned(path, EffectWorldPosition(position));
        }

        /// <summary>
        /// The world place an effect from the network belongs at. The payload carries (x, y); z
        /// comes from the local penitent because ObjectInstance.Reuse applies the z of the Vector3
        /// it is handed, and the game's effects inherit the penitent's z.
        /// </summary>
        private static Vector3 EffectWorldPosition(Vector2 position)
        {
            float z = Core.Logic.Penitent != null ? Core.Logic.Penitent.transform.position.z : 0f;
            return new Vector3(position.x, position.y, z);
        }

        /// <summary>
        /// Reproduces the slide's impact dust and sound from the LOCAL GuardSlide. GuardSlideImpactEffect
        /// and GuardSlideSoundFx are prefab/[EventRef] values that live in the asset. The particle
        /// system (Core.Logic.Penitent.ParticleSystem) is deliberately not touched: it is the local
        /// player's emitter.
        /// </summary>
        private static void PlaySlideDust(Vector2 position, bool facingRight)
        {
            if (Core.Logic.Penitent == null)
                return;

            Gameplay.GameControllers.Penitent.Abilities.GuardSlide slide = Core.Logic.Penitent.GuardSlide;
            if (slide == null)
                return;

            PoolManager pool = PoolManager.Instance;
            if (pool == null)
                return;

            Vector3 world = EffectWorldPosition(position);

            if (slide.GuardSlideImpactEffect != null)
            {
                // Pool is already created on this machine; the anchor is registered for dedup with
                // any future relay path that might also spawn it.
                EffectRelay.NoteLocalSpawn(slide.GuardSlideImpactEffect, world);

                PoolManager.ObjectInstance instance =
                    pool.ReuseObject(slide.GuardSlideImpactEffect, world, Quaternion.identity, false, 1);

                if (instance != null)
                    OrientEffect(instance, facingRight, "SlideDust", slide.GuardSlideImpactEffect);
            }

            if (!string.IsNullOrEmpty(slide.GuardSlideSoundFx))
                Core.Audio.EventOneShotPanned(slide.GuardSlideSoundFx, world);
        }

        /// <summary>
        /// Reproduces the dash dust from the LOCAL DashDustGenerator. Its prefab fields are private
        /// with no public accessor, so reflection is the only way. GetStartDashDust / GetStopDashDust
        /// are not used: they place the dust on the LOCAL penitent and re-transmit on the sender.
        /// </summary>
        private static void PlayDashDust(bool isStart, Vector2 position, bool facingRight)
        {
            if (Core.Logic.Penitent == null)
                return;

            Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator generator =
                Core.Logic.Penitent.DashDustGenerator;
            if (generator == null)
            {
                ModLog.Warn("[DashDust] the local penitent has no DashDustGenerator");
                return;
            }

            PoolManager pool = PoolManager.Instance;
            if (pool == null)
                return;

            System.Reflection.FieldInfo field = isStart ? StartDashDustField : StopDashDustField;
            if (field == null)
            {
                ModLog.Warn("[DashDust] prefab field not found in DashDustGenerator");
                return;
            }

            GameObject prefab = field.GetValue(generator) as GameObject;
            if (prefab == null)
            {
                // ReuseObject starts with prefab.GetInstanceID(); a null prefab would throw inside the
                // pool. The game itself guards the same way in DashDustGenerator.OnStart.
                ModLog.Warn("[DashDust] dash dust prefab not assigned in this build");
                return;
            }

            Vector3 world = EffectWorldPosition(position);

            // Pool already exists on this machine (DashDustGenerator.OnStart ran CreatePool).
            EffectRelay.NoteLocalSpawn(prefab, world);

            PoolManager.ObjectInstance instance =
                pool.ReuseObject(prefab, world, Quaternion.identity, false, 1);

            if (instance == null)
            {
                ModLog.Warn("[DashDust] ReuseObject returned no instance (missing pool?)");
                return;
            }

            OrientEffect(instance, facingRight, "DashDust", prefab);
        }

        /// <summary>
        /// Re-applies the peer's facing to an effect we spawned for them. The game's prefab flips
        /// itself by reading Core.Logic.Penitent - the LOCAL player - which is correct on the sending
        /// machine and mirrored on the receiving one. Re-applying through the prefab's own component
        /// preserves its flip mechanism (SimpleVFX uses localScale when useScaleToFlip is set, else
        /// flipX).
        /// </summary>
        private static void OrientEffect(
            PoolManager.ObjectInstance instance, bool facingRight, string what, GameObject prefab)
        {
            if (instance == null || instance.GameObject == null)
                return;

            Framework.FrameworkCore.EntityOrientation peer = facingRight
                ? Framework.FrameworkCore.EntityOrientation.Right
                : Framework.FrameworkCore.EntityOrientation.Left;

            Gameplay.GameControllers.Entities.SimpleVFX vfx =
                instance.GameObject.GetComponent<Gameplay.GameControllers.Entities.SimpleVFX>();

            if (vfx != null && vfx.FlipByPlayerOrientation)
            {
                vfx.SetOrientation(peer, vfx.useReversedPlayerOrientation);
                return;
            }

            Gameplay.GameControllers.Effects.Player.GuardSlide.GuardSlideImpact impact =
                instance.GameObject.GetComponent<Gameplay.GameControllers.Effects.Player.GuardSlide.GuardSlideImpact>();

            if (impact != null && impact.SpriteRenderer != null)
            {
                impact.SpriteRenderer.flipX = peer == Framework.FrameworkCore.EntityOrientation.Left;
                return;
            }

            ModLog.Warn($"[{what}] prefab with no orientation component: " +
                        (prefab == null ? "n/d" : prefab.name));
        }

        /// <summary>
        /// Reproduces the ground impact - dust, beam and landing sound - from the LOCAL VerticalAttack's
        /// serialized fields, without calling InstantiateExplosion (which would read the local
        /// penitent's position and skill). The landing sound is looked up here rather than via
        /// GetLandingFxEventKey, which selects on the local skill.
        /// </summary>
        private static void PlayLandingImpact(EffectType effect, Vector2 position)
        {
            if (Core.Logic.Penitent == null)
                return;

            Gameplay.GameControllers.Penitent.Abilities.VerticalAttack vertical = Core.Logic.Penitent.VerticalAttack;
            if (vertical == null)
                return;

            PoolManager pool = PoolManager.Instance;
            if (pool == null)
                return;

            Vector3 world = EffectWorldPosition(position);

            // Whether the beam should appear is decided by the sender (it saw the fall height); that
            // is why the effect byte already encodes it.
            bool beam = effect == EffectType.LandingImpact3Beam;

            if (beam
                && vertical.VerticalAttackBeamEffectPrefab != null
                && Core.Logic.Penitent.DamageArea != null)
            {
                // The game places the beam at the TOP of the penitent's damage area. The avatar is
                // elsewhere, so the offset is the local penitent's top minus its own feet.
                Vector3 offset =
                    Core.Logic.Penitent.DamageArea.TopCenter - Core.Logic.Penitent.transform.position;

                EffectRelay.NoteLocalSpawn(vertical.VerticalAttackBeamEffectPrefab, world + offset);

                pool.ReuseObject(
                    vertical.VerticalAttackBeamEffectPrefab,
                    world + offset,
                    Quaternion.identity,
                    false,
                    1);
            }

            // InstantiateExplosion returns early when HardLandingEffectPrefab is null; the same
            // guard belongs here because ReuseObject dereferences the prefab.
            GameObject prefab = beam ? vertical.HardLandingEffectUpgradedPrefab : vertical.HardLandingEffectPrefab;
            if (prefab != null)
            {
                EffectRelay.NoteLocalSpawn(prefab, world);
                pool.ReuseObject(prefab, world, Quaternion.identity, false, 1);
            }

            string sound = LandingSound(vertical, effect);
            if (!string.IsNullOrEmpty(sound))
                Core.Audio.EventOneShotPanned(sound, world);
        }

        /// <summary>
        /// Landing sound for the shape the sender reported, from the LOCAL VerticalAttack's
        /// VerticalLandingFxLevel* fields. LandingImpactBase is silent on purpose: when the selected
        /// skill is not one of the VERTICAL_* levels, the game's own get_GetLandingFxEventKey returns
        /// empty.
        /// </summary>
        private static string LandingSound(
            Gameplay.GameControllers.Penitent.Abilities.VerticalAttack vertical,
            EffectType effect)
        {
            switch (effect)
            {
                case EffectType.LandingImpact1:
                    return vertical.VerticalLandingFxLevel1;
                case EffectType.LandingImpact2:
                    return vertical.VerticalLandingFxLevel2;
                case EffectType.LandingImpact3:
                case EffectType.LandingImpact3Beam:
                    return vertical.VerticalLandingFxLevel3;
                default:
                    return null;
            }
        }

        private IEnumerator DisplayDebla()
        {
            ModLog.Info("Spawning debla object for " + transform.parent.name);
            if (Core.Logic.Penitent == null)
                yield break;

            GameObject debla = Instantiate(Core.Logic.Penitent.PrayerCast.lightBeamPrayer.areaPrefab, transform);
            debla.name = "Debla";
            debla.transform.localPosition = Vector3.zero;
            Destroy(debla.GetComponent<Gameplay.GameControllers.Bosses.Quirce.Attack.BossSpawnedAreaAttack>());
            Destroy(debla.transform.GetChild(0).GetChild(0).gameObject);
            debla.GetComponent<Animator>().Play("AttackLoop");
            yield return new WaitForSecondsRealtime(DEBLA_DURATION);
            Destroy(debla);
        }

        private IEnumerator DisplayVerdiales()
        {
            ModLog.Info("Spawning verdiales object for " + transform.parent.name);
            if (Core.Logic.Penitent == null)
                yield break;

            GameObject crawlerright = Instantiate(Core.Logic.Penitent.PrayerCast.crawlerBallsPrayer.projectilePrefab, transform);
            crawlerright.name = "Crawler right";
            crawlerright.transform.localPosition = Vector3.zero;
            crawlerright.GetComponentInChildren<AttackArea>().enabled = false;
            crawlerright.GetComponentInChildren<PolygonCollider2D>().enabled = false;
            crawlerright.GetComponentInChildren<StraightProjectile>().Init(crawlerright.transform.position, crawlerright.transform.position + Vector3.right, 16);
            crawlerright.transform.position += Vector3.right * 0.01f;

            GameObject crawlerleft = Instantiate(Core.Logic.Penitent.PrayerCast.crawlerBallsPrayer.projectilePrefab, transform);
            crawlerleft.name = "Crawler left";
            crawlerleft.transform.localPosition = Vector3.zero;
            crawlerleft.GetComponentInChildren<AttackArea>().enabled = false;
            crawlerleft.GetComponentInChildren<PolygonCollider2D>().enabled = false;
            crawlerleft.GetComponentInChildren<StraightProjectile>().Init(crawlerleft.transform.position, crawlerleft.transform.position + Vector3.left, 16);
            crawlerleft.transform.position += Vector3.left * 0.01f;

            yield return new WaitForSecondsRealtime(VERDIALES_DURATION);
            Destroy(crawlerright);
            Destroy(crawlerleft);
        }
    }
}