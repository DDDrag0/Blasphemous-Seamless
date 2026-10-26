using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Framework.Managers;
using UnityEngine;


namespace Blasphemous.Multiplayer.Client.Players
{
    public class MainPlayerManager
    {
        private Vector2 lastPosition;
        private byte lastAnimation;
        private bool lastDirection;

        private readonly float positionCutoff = 0.03f;

        /// <summary>
        /// Resend interval for a stationary player. Receivers treat a position older than 0.3s as
        /// unknown (used by enemy aggro), so a still player would disappear to them without this
        /// heartbeat.
        /// </summary>
        private const float PositionHeartbeat = 0.2f;
        private float lastPositionSentAt;

        private readonly float totalTimeBeforeSendAnimation = 0.5f;
        private float currentTimeBeforeSendAnimation = 0;

        private bool _corpsePeerWasDead;
        private bool _corpsePeerDeathSent;
        private float _corpsePeerArmedAt;
        private bool _corpsePeerWarned;

        /// <summary>
        /// How long to wait for the Penitent's Animator to enter a death state before falling back
        /// to the default death animation. The transition is driven by the game, so the mod learns
        /// of the death slightly before the Animator does.
        /// </summary>
        private const float CorpsePeerFuseSeconds = 0.5f;

        /// <summary>
        /// Sends the death animation to the peer, once per death. Called by Multiplayer.OnLateUpdate
        /// after DeathCorpseFollow.Tick, when PositionSendPaused is already true - this is the last
        /// byte sent before the freeze. Retries while the Animator has not entered a death state
        /// (up to CorpsePeerFuseSeconds), then falls back to the default death animation.
        /// </summary>
        public void CorpsePeerTick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    // Death ends at respawn: DeathCorpseFollow.Close calls SendAllLocationData, which
                    // sends a non-death animation (0 = Idle) and lifts the avatar on the peer.
                    if (_corpsePeerWasDead)
                    {
                        ModLog.Info($"{CorpsePeerMark} peer avatar: alive and standing " +
                                    $"(death ended: at respawn the mod resends animation/direction " +
                                    $"and the peer lifts the avatar again) · deathAnimationSent=" +
                                    $"{(_corpsePeerDeathSent ? "yes" : "no")}");
                    }

                    _corpsePeerWasDead = false;
                    _corpsePeerDeathSent = false;
                    _corpsePeerArmedAt = 0f;
                    return;
                }

                if (!_corpsePeerWasDead)
                {
                    _corpsePeerWasDead = true;
                    _corpsePeerArmedAt = Time.unscaledTime;
                    _corpsePeerWarned = false;
                }

                if (_corpsePeerDeathSent)
                    return;

                AnimatorStateInfo state = Core.Logic.Penitent.Animator.GetCurrentAnimatorStateInfo(0);
                int deathIndex = PlayerStatus.DeathAnimationIndexOf(
                    "Death", "Death Spike", "Death Fall");

                bool inDeathState = deathIndex >= 0 && state.IsName(
                    Blasphemous.Multiplayer.Client.Data.AnimationStates.animations[deathIndex].name);

                float waited = Time.unscaledTime - _corpsePeerArmedAt;
                if (!inDeathState && waited < CorpsePeerFuseSeconds)
                    return;

                byte deathAnimation = inDeathState
                    ? (byte)deathIndex
                    : PlayerStatus.DefaultDeathAnimation;

                Main.Multiplayer.NetworkManager.SendAnimation(deathAnimation);
                _corpsePeerDeathSent = true;

                ModLog.Info($"{CorpsePeerMark} death animation sent: " +
                            $"{(inDeathState ? "stato vero dell'Animator" : "RIPIEGO")}=" +
                            $"{Blasphemous.Multiplayer.Client.Data.AnimationStates.animations[deathAnimation].name}" +
                            $" (byte {deathAnimation}) · waited={waited:F2}s" +
                            $"{(inDeathState ? "" : " ⚠️ the Penitent's Animator did not enter a death state within " + CorpsePeerFuseSeconds.ToString("F2") + "s")}" +
                            $" · it is the LAST byte before the freeze: nothing is sent from here on" +
                            $" (not even the position) until the dead player revives");
            }
            catch (System.Exception e)
            {
                // A failure here leaves the known defect (avatar standing on the peer); it must
                // not cause a new one.
                if (!_corpsePeerWarned)
                {
                    _corpsePeerWarned = true;
                    ModLog.Error($"{CorpsePeerMark} ERR on the sender (no effect on the game: the peer " +
                                 $"keeps the last pose, the known defect): {e.GetType().Name} - {e.Message}");
                }
            }
        }

        /// <summary>Marker for the corpse-animation fix.</summary>
        internal const string CorpsePeerMark = "[CorpsePeer]";

        public void Update()
        {
            if (Main.Multiplayer.NetworkManager.IsConnected && Core.Logic.Penitent != null)
            {
                // While dead, nothing is sent (position, animation, direction). DeathCorpseFollow
                // moves the Penitent onto the live peer so the camera follows - without this
                // guard the peer would place the dead avatar on top of its own. Freezing also
                // matches what EnemySyncManager already assumes (a dead player stops sending
                // positions).
                //
                // Uses PositionSendPaused (not IsLocalPlayerDead) so a failed corpse-clone - which
                // leaves the Penitent at the death point - does not freeze the send.
                //
                // Memory is aligned here so reopening does not send three stale packets while
                // Close() sends one complete batch.
                if (DeathCorpseFollow.PositionSendPaused)
                {
                    DeathCorpseFollow.NotePositionSendPaused();
                    lastPosition = CurrentPosition;
                    lastAnimation = CurrentAnimation;
                    lastDirection = CurrentDirection;
                    return;
                }

                // Check & send updated position - on a move, or on the heartbeat so a standing player stays
                // locatable.
                Vector2 newPosition = CurrentPosition;
                bool moved = Mathf.Abs(newPosition.x - lastPosition.x) > positionCutoff ||
                             Mathf.Abs(newPosition.y - lastPosition.y) > positionCutoff;
                if (moved || Time.time - lastPositionSentAt >= PositionHeartbeat)
                {
                    Main.Multiplayer.NetworkManager.SendPosition(newPosition);
                    lastPosition = newPosition;
                    lastPositionSentAt = Time.time;
                }

                // Check & send updated animation clip
                byte newAnimation = CurrentAnimation;
                if (newAnimation != lastAnimation)
                {
                    // Don't send new animations right after a special animation
                    if (currentTimeBeforeSendAnimation <= 0 && newAnimation != 255)
                    {
                        Main.Multiplayer.NetworkManager.SendAnimation(newAnimation);
                    }
                    lastAnimation = newAnimation;
                }

                // Check & send updated facing direction
                bool newDirection = CurrentDirection;
                if (newDirection != lastDirection)
                {
                    Main.Multiplayer.NetworkManager.SendDirection(newDirection);
                    lastDirection = newDirection;
                }
            }

            // Decrease frame counter for special animation delay
            if (currentTimeBeforeSendAnimation > 0)
                currentTimeBeforeSendAnimation -= Time.deltaTime;
        }

        // Sends the current position/animation/direction when first entering a scene or joining server
        public void SendAllLocationData()
        {
            Main.Multiplayer.NetworkManager.SendPosition(lastPosition = CurrentPosition);
            Main.Multiplayer.NetworkManager.SendAnimation(lastAnimation = 0);
            Main.Multiplayer.NetworkManager.SendDirection(lastDirection = CurrentDirection);
        }

        public void UseSpecialAnimation(byte animation)
        {
            currentTimeBeforeSendAnimation = totalTimeBeforeSendAnimation;
            Main.Multiplayer.NetworkManager.SendAnimation(animation);
        }

        private Vector2 CurrentPosition
        {
            get
            {
                return Core.Logic.Penitent.transform.position;
            }
        }

        private byte CurrentAnimation
        {
            get
            {
                AnimatorStateInfo state = Core.Logic.Penitent.Animator.GetCurrentAnimatorStateInfo(0);
                for (byte i = 0; i < AnimationStates.animations.Length; i++)
                {
                    if (state.IsName(AnimationStates.animations[i].name))
                    {
                        return i;
                    }
                }

                // This animation could not be found
                ModLog.Error("Current animation doesn't exist!");
                return 255;
            }
        }

        private bool CurrentDirection
        {
            get
            {
                return Core.Logic.Penitent.SpriteRenderer.flipX;
            }
        }
    }
}