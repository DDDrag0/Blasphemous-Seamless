using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Projectiles;
using Gameplay.GameControllers.Entities;
using System.Collections;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players
{
    public class OtherPlayerAttackScript : MonoBehaviour
    {
        private const float DEBLA_DURATION = 1.2f;
        private const float VERDIALES_DURATION = 2f;

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
        /// Replays an effect on the avatar. The position is only read by the effects that happen SOMEWHERE -
        /// the ground impact and the slide's dust (A4) - and hasPosition says whether it means anything.
        /// </summary>
        public void PlayEffectAnimation(EffectType effect, bool facingRight, bool hasPosition, Vector2 position)
        {
            SwordRenderer.flipX = !facingRight;

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
                //case EffectType.Ranged:
                //    Main.Multiplayer.LogError("Spawning range attack effect!");
                //    break;
                case EffectType.Debla:
                    StartCoroutine(DisplayDebla());
                    break;
                // A3: the ghost trail belongs to the avatar, not to the sword, so this reaches up to it.
                // Both cases share the body because the only difference is the flag passed on.
                case EffectType.TrailOn:
                case EffectType.TrailOff:
                {
                    OtherPlayerScript owner = GetComponentInParent<OtherPlayerScript>();
                    if (owner != null)
                        owner.SetGhostTrail(effect == EffectType.TrailOn);
                    break;
                }
                // A4 - the slide's dust and its sound, at the place the sender slid.
                //
                // hasPosition is CHECKED rather than assumed. A build of this mod from before the position, or
                // a server that still relays a single byte, sends the effect with no place attached - and the
                // origin is a legitimate coordinate in this game, so drawing it there would put the dust at the
                // far end of the level instead of at the player.
                case EffectType.SlideDust:
                {
                    if (hasPosition)
                        PlaySlideDust(position);
                    break;
                }
                // A4 - the ground impact: the dust, the beam and the landing sound. The five members are the
                // five shapes VerticalAttack.InstantiateExplosion can take, and the sender is the machine that
                // decided which one it was (it is the one that saw the fall height and its own skill).
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
                    //case EffectType.Verdiales:
                    //    StartCoroutine(DisplayVerdiales());
                    //    break;
            }
        }

        /// <summary>
        /// The world place an effect that arrived over the network belongs at.
        ///
        /// The payload carries two floats (x, y), because that is all a spot in this game needs, and z is taken
        /// from the LOCAL penitent: ObjectInstance.Reuse applies the z of the Vector3 it is handed, and the
        /// game's own effects are placed at the penitent's position and inherit its z. Rebuilding one at a
        /// different depth than the player's own dust could sort it wrongly. Zero when there is no penitent,
        /// which is the same z the remote avatar itself is drawn at.
        /// </summary>
        private static Vector3 EffectWorldPosition(Vector2 position)
        {
            float z = Core.Logic.Penitent != null ? Core.Logic.Penitent.transform.position.z : 0f;
            return new Vector3(position.x, position.y, z);
        }

        /// <summary>
        /// The slide's impact: the dust puff and the sound (A4).
        ///
        /// Reproduced from the LOCAL GuardSlide, never from a transmitted string. GuardSlideImpactEffect and
        /// GuardSlideSoundFx are prefab/[EventRef] fields whose values live in the asset and are NOT readable
        /// from the code, so the only correct source is the component itself - the same component
        /// GuardSlide.OnCastStart reads on the machine that slid, and the same one that exists on this one.
        ///
        /// What we do NOT reproduce is the particle system: GuardSlide.EnableParticleSystem drives
        /// Core.Logic.Penitent.ParticleSystem, which is the LOCAL player's emitter. Turning that on while the
        /// PEER slides would raise dust on the wrong, motionless character. The avatar has no particle system
        /// of its own, so the slide's dust trail is deliberately not part of this.
        /// </summary>
        private static void PlaySlideDust(Vector2 position)
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

            // createPoolIfNeeded stays FALSE and poolSize stays 1, exactly as GuardSlide.OnCastStart calls it:
            // GuardSlide.OnStart already ran CreatePool(GuardSlideImpactEffect, 1) on this machine - both
            // machines have their own penitent - so the pool is there. Passing true would be harmless, but it
            // would be a difference from the game invented here rather than needed.
            //
            // ⚠️ The null check is NOT decorative: PoolManager.ReuseObject starts with prefab.GetInstanceID(),
            // so a null prefab throws inside the pool instead of quietly drawing nothing.
            if (slide.GuardSlideImpactEffect != null)
                pool.ReuseObject(slide.GuardSlideImpactEffect, world, Quaternion.identity, false, 1);

            if (!string.IsNullOrEmpty(slide.GuardSlideSoundFx))
                Core.Audio.EventOneShotPanned(slide.GuardSlideSoundFx, world);
        }

        /// <summary>
        /// The ground impact: the dust, the beam and the landing sound (A4).
        ///
        /// This reproduces what VerticalAttack.InstantiateExplosion does - the choice of prefab, the beam and
        /// the sound - WITHOUT calling it, because that method reads the local penitent's position and the
        /// local skill, and here both would be the wrong ones. The prefabs and the three landing sounds come
        /// from the LOCAL VerticalAttack's own serialized fields, for the same reason as the slide: their
        /// values live in the prefab.
        ///
        /// The landing sound is NOT read through VerticalAttack.GetLandingFxEventKey: that property selects on
        /// LastUnlockedSkillId, which is THIS machine's skill. The level travelled in the effect byte instead.
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

            // `isVertical3 && BeamRequiredHeightReached` is the pair InstantiateExplosion branches on, and the
            // sender is the only machine that knows it: it saw the fall height. Hence the two members.
            bool beam = effect == EffectType.LandingImpact3Beam;

            if (beam
                && vertical.VerticalAttackBeamEffectPrefab != null
                && Core.Logic.Penitent.DamageArea != null)
            {
                // The game puts the beam at the TOP of the penitent's damage area. The avatar is somewhere
                // else, so the offset is taken from the local penitent (its top minus its own feet) and added
                // to the place the impact was reported at.
                Vector3 offset =
                    Core.Logic.Penitent.DamageArea.TopCenter - Core.Logic.Penitent.transform.position;

                pool.ReuseObject(
                    vertical.VerticalAttackBeamEffectPrefab,
                    world + offset,
                    Quaternion.identity,
                    false,
                    1);
            }

            // InstantiateExplosion returns WITHOUT doing anything when HardLandingEffectPrefab is null, so the
            // same guard belongs here - and it is load-bearing: ReuseObject dereferences the prefab.
            GameObject prefab = beam ? vertical.HardLandingEffectUpgradedPrefab : vertical.HardLandingEffectPrefab;
            if (prefab != null)
                pool.ReuseObject(prefab, world, Quaternion.identity, false, 1);

            string sound = LandingSound(vertical, effect);
            if (!string.IsNullOrEmpty(sound))
                Core.Audio.EventOneShotPanned(sound, world);
        }

        /// <summary>
        /// The landing sound for the shape the sender reported, taken from the LOCAL VerticalAttack. Its
        /// VerticalLandingFxLevel* fields are exactly what get_GetLandingFxEventKey selects between, and they
        /// hold the same bank paths on this machine (same build, same prefab).
        ///
        /// LandingImpactBase has no sound on purpose: when the selected skill is not one of the VERTICAL_* the
        /// game's own get_GetLandingFxEventKey returns an empty string, so that landing is silent on the
        /// machine that produced it as well.
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
            Destroy(crawlerleft); // Will probably have to leave colliders active to detect walls, maybe just change layermask to exlude enemy layer ?
        }
    }
}
