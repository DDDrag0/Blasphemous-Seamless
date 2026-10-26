using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using System;
using System.Globalization;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players
{
    /// <summary>
    /// Corpse-copy for the dead player. Hides the local Penitent by zeroing only the alpha of its
    /// sprite renderer, clones its visual tree under a frozen root (every Behaviour disabled, so
    /// the death pose stays put), and moves the Penitent - and therefore the camera bound to it -
    /// onto the live peer's avatar. Follow pauses on cinematics (CINEMATIC blocker) and on scene
    /// changes, and position sending is suppressed while dead so the peer's avatar stays where it
    /// fell. Reset in OnDisconnect. No Harmony patches; hooks into Multiplayer.OnLateUpdate.
    /// </summary>
    internal static class DeathCorpseFollow
    {
        /// <summary>Outcome marker, distinct from [DeathScreen].</summary>
        private const string MARK = "[CorpseFollow]";

        /// <summary>Last line of defense, same as other probes.</summary>
        private const string MARK_FAIL = "[CorpseFollow ERR]";

        /// <summary>
        /// Follow step in world units. A threshold rather than per-frame: Penitent.Teleport resets
        /// velocity and toggles colliders, and moving a Collider2D every frame would sweep it
        /// through triggers. The camera has ProCamera2D smoothing, so occasional jumps are
        /// invisible while rigid follow would not be.
        /// </summary>
        private const float FOLLOW_EPSILON = 6f;

        /// <summary>
        /// Fuse in unscaled seconds. Real death windows are ~102s (last session), so anything
        /// shorter than the boss cutscene would disable follow exactly when it is needed.
        /// </summary>
        private const float FUSE_SECONDS = 360f;

        /// <summary>
        /// Cap for pose refresh, unscaled. The signal that stops refresh is the game's own
        /// (Animator.enabled == false, set by PlayerDeathAnimationBehaviour), but if that never
        /// arrives (custom death, mid-air death, Animator already off), the cap stops refresh and
        /// the warn says so. Better a copy that keeps refreshing a hair too long than a corpse
        /// frozen mid-fall.
        /// </summary>
        private const float CORPSE_POSE_MAX_SECONDS = 8f;

        // Death window state. None of this survives Reset().
        private static bool _active;
        private static float _startedAt;
        private static Vector2 _deathPos;
        private static GameObject _copy;
        private static SpriteRenderer _copyRenderer;
        /// <summary>Visual node of the copy (clone of Body), for pose copy beyond the sprite.</summary>
        private static Transform _copyBody;
        private static SpriteRenderer _body;
        private static float _originalAlpha = 1f;
        private static bool _rendererHidden;

        /// <summary>Pose-refresh frames and alpha-reassert count (proves external writes to the color).</summary>
        private static int _poseFrames;
        private static int _hiddenReasserts;

        /// <summary>Whether the pose was closed, and by what (the game signal or the cap).</summary>
        private static bool _poseClosed;
        private static string _poseClosedWhy = "in corso";

        /// <summary>One-shot flag for a repeated failure line.</summary>
        private static bool _repeatReported;

        /// <summary>
        /// Separate flag for the Teleport failure: _repeatReported is reset whenever a live peer
        /// is found, so reusing it here would spam the warning on every move.
        /// </summary>
        private static bool _teleportFailed;

        /// <summary>Move count and last gap, so the closing line distinguishes "never followed" from "followed well".</summary>
        private static int _moves;
        private static float _lastGap;

        private static bool _pausedByCinematic;
        private static bool _positionSendPaused;

        // Scene-change freeze state (kept across the window's lifetime; see SceneChangeFrozen).
        private static bool _doorTransit;
        private static bool _sceneChangeReported;

        // Public Door event subscription.
        private static bool _doorSubscribed;

        /// <summary>
        /// One frame. State machine: alive -> no-op; dying -> Create; dead -> follow/fuse;
        /// respawned -> Close.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_active)
                        Close("the player returned (respawn)");
                    return;
                }

                if (!_active)
                {
                    Create();
                    return;
                }

                float now = Time.unscaledTime;

                // Fuse before anything else: the only exit that does not depend on the game or
                // on the network.
                if (now - _startedAt > FUSE_SECONDS)
                {
                    float lasted = now - _startedAt;
                    Close("FUSE tripped after " + F(lasted) + "s (the death window did not close)");
                    return;
                }

                Follow(now);
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Close window on disconnect or end of session. Needed because Tick already closes when
        /// IsLocalPlayerDead becomes false, but a disconnect while dead leaves IsLocalPlayerDead
        /// true and would leave a copy and a hidden renderer in the scene.
        /// </summary>
        internal static void Reset()
        {
            try
            {
                if (_active)
                    Close("disconnection / end of session");
            }
            catch (Exception e)
            {
                Fail("Reset", e);
            }
        }

        // --- Creation: the only part that may fail loudly. If the clone does not come up,
        // nothing is hidden and nothing moves.

        /// <summary>
        /// Create the corpse-copy and hide the Penitent. If the clone fails, nothing is hidden and
        /// nothing moves: better a corpse in the right place than an invisible one.
        /// </summary>
        private static void Create()
        {
            try
            {
                Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
                if (penitent == null)
                {
                    ReportOnce("Penitent not available: no copy, no movement");
                    return;
                }

                SpriteRenderer body = penitent.SpriteRenderer;
                if (body == null)
                {
                    // Entity.SpriteRenderer is assigned in Awake; if it is null there is no visual
                    // tree to clone, so nothing is attempted.
                    ReportOnce("Penitent.SpriteRenderer null: no copy, no movement");
                    return;
                }

                _deathPos = penitent.transform.position;
                _originalAlpha = body.color.a;

                _copy = CloneCorpse(body, penitent.transform);
                if (_copy == null)
                {
                    ReportOnce("corpse clone FAILED: the Penitent stays visible and still at the " +
                               "death point (no movement, no renderer off)");
                    return;
                }

                // Only the alpha is zeroed, not SetActive and not SpriteRenderer.enabled: RGB stays
                // intact for a truthful restore, and the game's own sprite toggle (Interactable.
                // ShowPlayer) is left alone. See KeepLocalCorpseHidden for the reassertion.
                //
                // Order matters: open the window BEFORE hiding. If the alpha write threw first,
                // the window would never open, Close() would never run, and the player would stay
                // invisible forever - the worst outcome. This way the worst case is a visible
                // corpse that moves (the known defect, not a new one).
                _active = true;
                _startedAt = Time.unscaledTime;
                _poseClosed = false;
                _poseClosedWhy = "in corso";
                _poseFrames = 0;
                _hiddenReasserts = 0;
                _moves = 0;
                _lastGap = 0f;
                _pausedByCinematic = false;
                _positionSendPaused = false;
                _repeatReported = false;
                _teleportFailed = false;

                // Reset the scene-change freeze here, not in Close(): a door that never fires
                // OnDoorExit (death mid-transit, load rebuilding, scene change while closed) would
                // keep the follow frozen forever. Each death window starts clean.
                _doorTransit = false;
                _sceneChangeReported = false;

                HideLocalCorpse(body, "at the start of the window (the corpse-copy stays on the ground)");

                ModLog.Info(MARK + " corpse-copy created: posMorte=" + V(_deathPos) +
                            " alphaPrima=" + F(_originalAlpha) + " copia=" + _copy.name +
                            " · the Penitent is " + (_rendererHidden ? "INVISIBLE" : "STILL VISIBLE " +
                            "(the alpha write failed: see the line above)") +
                            " and will follow the live peer (the camera is bound to it)");
            }
            catch (Exception e)
            {
                Fail("Create", e);
            }
        }

        /// <summary>
        /// Mandatory marker: "local corpse hidden (renderer=…)". Carries the value before and after
        /// and the enabled state, because without it "did not hide" and "hid and did not come back"
        /// look identical in the log.
        /// </summary>
        private static void HideLocalCorpse(SpriteRenderer body, string why)
        {
            // Flag before the write: Close() restores only when _rendererHidden is true, so raising
            // it first makes the restore truthful even if the write below throws. The opposite
            // (flag after) would leave the alpha zeroed with nothing to restore it.
            _rendererHidden = true;

            float before = body.color.a;
            Color hidden = body.color;
            hidden.a = 0f;
            body.color = hidden;

            ModLog.Info(MARK + " local corpse hidden (renderer=SpriteRenderer of " +
                        (body.gameObject != null ? body.gameObject.name : "?") +
                        " · sprite=" + (body.sprite != null ? body.sprite.name : "nessuno") +
                        " · enabled=" + (body.enabled ? "True" : "False") +
                        " · alpha " + F(before) + " -> " + F(body.color.a) + ") · " + why);
        }

        /// <summary>
        /// Reassert the alpha every frame while the window is open. The alpha is a renderer
        /// property, not a private field: any animator trace, damage effect, or the game's own
        /// visibility toggle can rewrite it, and a single write would let the real body reappear
        /// standing while the copy is on the ground. The reassert count proves whether anyone
        /// wrote it.
        /// </summary>
        private static void KeepLocalCorpseHidden()
        {
            if (!_rendererHidden || _body == null)
                return;

            Color current = _body.color;
            if (current.a == 0f)
                return;

            current.a = 0f;
            _body.color = current;
            _hiddenReasserts++;
        }

        /// <summary>
        /// Build the copy: a new root plus the cloned visual tree, with every Behaviour disabled
        /// (so the Animator is off and the death pose stays put). Returns null when there is
        /// nothing to clone; the caller then does not touch anything.
        /// </summary>
        private static GameObject CloneCorpse(SpriteRenderer body, Transform root)
        {
            try
            {
                GameObject visualRoot = body.gameObject;
                if (visualRoot == null)
                    return null;

                GameObject copyRoot = new GameObject("_coop_death_corpse");
                copyRoot.transform.position = root.position;
                copyRoot.transform.rotation = root.rotation;
                copyRoot.transform.localScale = Vector3.one;

                GameObject clone = UnityEngine.Object.Instantiate(visualRoot, copyRoot.transform, false);
                if (clone == null)
                {
                    UnityEngine.Object.Destroy(copyRoot);
                    return null;
                }

                Freeze(clone);

                _copyRenderer = clone.GetComponent<SpriteRenderer>();
                _copyBody = clone.transform;
                _body = body;
                return copyRoot;
            }
            catch (Exception e)
            {
                Fail("CloneCorpse", e);
                return null;
            }
        }

        /// <summary>
        /// Disable every Behaviour in the cloned tree, Animator included: the copy is a
        /// photograph. EntityAnimationEvents is the strongest reason - its Start() reads
        /// GetComponentInParent&lt;Execution&gt;() with no null check, which is the measured NRE on
        /// the execution ghost - but disabling everything is also the neutralization that does
        /// not require guessing what is on the tree. SpriteRenderer is not a Behaviour and stays
        /// on: it is the copy.
        /// </summary>
        private static void Freeze(GameObject clone)
        {
            Behaviour[] behaviours = clone.GetComponentsInChildren<Behaviour>(true);
            int off = 0;
            if (behaviours != null)
            {
                for (int i = 0; i < behaviours.Length; i++)
                {
                    Behaviour behaviour = behaviours[i];
                    if (behaviour == null)
                        continue;

                    behaviour.enabled = false;
                    off++;
                }
            }

            ModLog.Info(MARK + " copy frozen: behaviour disabled=" + off +
                        " (SpriteRenderer excluded: not a Behaviour)");
        }

        // --- Follow

        /// <summary>
        /// Move the Penitent (and therefore the camera, bound to it) onto the live peer. Three
        /// stops: cinematic blocker on (pause), no live peer to follow (stay - "both dead" case),
        /// peer in another scene (no shell to follow, stay). Scene-change freeze is separate:
        /// moving a body into a door trigger is a level load that respawns the player.
        /// </summary>
        private static void Follow(float now)
        {
            // Pose refresh first, and also during cinematics: it only touches the copy's visual
            // node, not the Penitent, so no scene trigger is reachable from it.
            if (!_poseClosed)
                UpdateCorpsePose(now);

            // Cinematic blocker is read, never written.
            bool cinematic = false;
            try
            {
                InputManager input = Core.Input;
                cinematic = input != null && input.HasBlocker("CINEMATIC");
            }
            catch (Exception)
            {
            }

            if (cinematic)
            {
                if (!_pausedByCinematic)
                {
                    _pausedByCinematic = true;
                    ModLog.Info(MARK + " follow PAUSED: cinematic in progress (CINEMATIC blocker on) " +
                                "· the Penitent stays put, no scene trigger can fire because of us");
                }
                return;
            }

            if (_pausedByCinematic)
            {
                _pausedByCinematic = false;
                ModLog.Info(MARK + " follow RESUMED: cinematic finished (CINEMATIC blocker off)");
            }

            // Scene change: moving the corpse into a door trigger is the same as the fuse, a
            // level load that respawns the player with nobody having asked. Stop and say why.
            if (SceneChangeFrozen())
                return;

            OtherPlayerScript alive = FindLivingPeer(out string aliveName);
            if (alive == null)
            {
                if (!_repeatReported)
                {
                    _repeatReported = true;
                    ModLog.Info(MARK + " no live peer to follow: the Penitent STAYS on the corpse " +
                                "(case \"both dead\", or the other is in another scene) · " +
                                "no movement, no copy destroyed");
                }
                return;
            }

            _repeatReported = false;

            Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
            if (penitent == null)
                return;

            Vector2 target = alive.transform.position;
            float gap = Vector2.Distance(penitent.transform.position, target);
            _lastGap = gap;

            if (gap < FOLLOW_EPSILON)
                return;

            MoveTo(penitent, target);
            _moves++;

            ModLog.Info(MARK + " Penitent moved onto live " + aliveName + ": pos=" + V(target) +
                        " (was " + F(gap) + " units from the corpse at " + V(_deathPos) + ") " +
                        "spostamenti=" + _moves + " · the camera follows it by itself");
        }

        /// <summary>
        /// True while a scene is changing, and the corpse-follower must stay put. Two facts from
        /// the game, not sensors: LevelManager.InsideChangeLevel (public field) and Door.OnDoorEnter
        /// (public event, subscribed with no Harmony patch). The door condition covers the ~0.8s
        /// between door-open and load-start, when InsideChangeLevel is still false and the corpse
        /// would still be free to enter the trigger. Both are read-only; the door events are the
        /// same ones the game uses for fade and camera.
        /// </summary>
        private static bool SceneChangeFrozen()
        {
            bool changing = false;
            try
            {
                LevelManager levels = Core.LevelManager;
                changing = levels != null && levels.InsideChangeLevel;
            }
            catch (Exception)
            {
            }

            if (!changing && !_doorTransit)
                return false;

            if (!_sceneChangeReported)
            {
                _sceneChangeReported = true;
                ModLog.Info(MARK + " follow FROZEN: scene change in progress (" +
                            (changing ? "`LevelManager.InsideChangeLevel`=True" : "door crossed") +
                            (_doorTransit ? " · `Door.OnDoorEnter` seen" : string.Empty) +
                            ") · the Penitent does NOT move: moving its `Collider2D` into a scene-change " +
                            "trigger is the same as the fuse, i.e. a level load that respawns the " +
                            "player with nobody having asked. Resumes when the scene is stable");
            }

            return true;
        }

        /// <summary>
        /// Subscribe once to Door.OnDoorEnter/OnDoorExit (public events). Same events the game
        /// uses for the door's fade and camera; this only listens. No OverlapCircleAll: it
        /// depends on the door trigger's layer, which DefaultRaycastLayers excludes, and could
        /// fail silently - worse than no guard.
        /// </summary>
        internal static void AttachOnce()
        {
            try
            {
                if (_doorSubscribed)
                    return;

                Door.OnDoorEnter += OnDoorEnter;
                Door.OnDoorExit += OnDoorExit;
                _doorSubscribed = true;

                ModLog.Info(MARK + " scene change: subscribed to `Door.OnDoorEnter`/`OnDoorExit` " +
                            "(PUBLIC events, no Harmony patches) · the corpse-follower STOPS until " +
                            "the scene is stable, so it cannot enter a scene-change trigger nor " +
                            "follow the peer ACROSS a load");
            }
            catch (Exception e)
            {
                Fail("AttachOnce", e);
            }
        }

        /// <summary>Door crossed by this player (Door.EnterDoor fires the event before fade).</summary>
        private static void OnDoorEnter()
        {
            try
            {
                _doorTransit = true;
                _sceneChangeReported = false;
            }
            catch (Exception e)
            {
                Fail("OnDoorEnter", e);
            }
        }

        /// <summary>Door traversed (Door.ExitDoorSafe fires OnDoorExit): follow can resume.</summary>
        private static void OnDoorExit()
        {
            try
            {
                _doorTransit = false;
                _sceneChangeReported = false;
            }
            catch (Exception e)
            {
                Fail("OnDoorExit", e);
            }
        }

        /// <summary>
        /// One pose-refresh frame plus the close decision: copy until the source stops, and stop
        /// when the game freezes the source (Penitent.Animator.enabled == false, set by
        /// PlayerDeathAnimationBehaviour at normalizedTime >= 0.95 - the same line that makes the
        /// real corpse stop). Falling back to CORPSE_POSE_MAX_SECONDS keeps the function alive
        /// even when the signal never comes; the warn says so.
        /// </summary>
        private static void UpdateCorpsePose(float now)
        {
            RefreshCorpseSprite();
            KeepLocalCorpseHidden();
            _poseFrames++;

            Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
            UnityEngine.Animator animator = penitent != null ? penitent.Animator : null;

            // Game signal: the pose is final because the game froze it.
            if (animator != null && !animator.enabled)
            {
                ClosePose("death animation finished: the game disabled the Penitent's Animator and " +
                          "the pose is the final one (PlayerDeathAnimationBehaviour)");
                return;
            }

            if (now - _startedAt >= CORPSE_POSE_MAX_SECONDS)
            {
                ClosePose(null);
            }
        }

        private static void ClosePose(string why)
        {
            _poseClosed = true;

            if (why != null)
            {
                _poseClosedWhy = why;
                ModLog.Info(MARK + " corpse pose closed after " + F(Time.unscaledTime - _startedAt) +
                            "s and " + _poseFrames + " frames copied · " + why);
                return;
            }

            _poseClosedWhy = "CAP of " + F(CORPSE_POSE_MAX_SECONDS) + "s: the Penitent's Animator " +
                             "never stopped (the death animation does not go through " +
                             "`PlayerDeathAnimationBehaviour`, or it is longer than the cap, or the " +
                             "death was in mid-air) · the copy stops on the last copied pose and the " +
                             "follow continues";
            ModLog.Warn(MARK + " corpse pose closed by the CAP after " + F(Time.unscaledTime - _startedAt) +
                        "s and " + _poseFrames + " frames copied · " + _poseClosedWhy);
        }

        /// <summary>
        /// Copy sprite, color and pose from the Penitent's visual node. Pose is local-to-local:
        /// the copy's root is fixed at the death point, so copying world would drag the copy
        /// along with the moving Penitent. Only RGB is copied, never alpha: the Penitent is
        /// already invisible, and copying alpha would hide the copy as well.
        /// </summary>
        private static void RefreshCorpseSprite()
        {
            if (_copyRenderer == null || _body == null)
                return;

            Sprite sprite = _body.sprite;
            if (sprite != null && _copyRenderer.sprite != sprite)
                _copyRenderer.sprite = sprite;

            Color theirs = _body.color;
            _copyRenderer.color = new Color(theirs.r, theirs.g, theirs.b, _originalAlpha);

            Transform clone = _copyBody;
            Transform source = _body.transform;
            if (clone != null && source != null && clone != source)
            {
                clone.localPosition = source.localPosition;
                clone.localRotation = source.localRotation;
                clone.localScale = source.localScale;
            }
        }

        /// <summary>
        /// The move itself: Penitent.Teleport is the right path (resets velocity, toggles
        /// colliders, no sweeping). It reads Core.Logic.Penitent and touches physics with no
        /// null guard, so a fallback to direct transform.position exists for the cases where a
        /// chain is half-built - it does not touch colliders or velocity, which is exactly why
        /// it is only the fallback.
        /// </summary>
        private static void MoveTo(Penitent penitent, Vector2 target)
        {
            try
            {
                penitent.Teleport(target);
                return;
            }
            catch (Exception e)
            {
                if (!_teleportFailed)
                {
                    _teleportFailed = true;
                    ModLog.Warn(MARK + " Teleport() failed (" + e.GetType().Name +
                                "): falling back to direct transform move, which does not touch " +
                                "colliders or velocity - the corpse-copy stays put either way");
                }
            }

            try
            {
                penitent.transform.position = new Vector3(target.x, target.y, penitent.transform.position.z);
            }
            catch (Exception e)
            {
                Fail("MoveTo (direct transform)", e);
            }
        }

        /// <summary>
        /// First active shell whose player is not dead. The other's avatar stays in scene even
        /// when dead, so following it blindly would put the camera on another corpse.
        /// </summary>
        private static OtherPlayerScript FindLivingPeer(out string name)
        {
            name = null;

            try
            {
                OtherPlayerManager manager = Main.Multiplayer.OtherPlayerManager;
                if (manager == null)
                    return null;

                var connected = manager.AllConnectedPlayers;
                if (connected == null)
                    return null;

                for (int i = 0; i < connected.Count; i++)
                {
                    PlayerStatus status = connected[i];
                    if (status == null || status.IsDead)
                        continue;

                    OtherPlayerScript avatar = manager.FindActivePlayer(status.Name);
                    if (avatar == null || avatar.gameObject == null)
                        continue; // in another scene: no position to follow

                    name = status.Name;
                    return avatar;
                }
            }
            catch (Exception e)
            {
                Fail("FindLivingPeer", e);
            }

            return null;
        }

        // --- Close, which must not be able to fail silently.

        /// <summary>
        /// Restore everything. Order matters: restore the renderer first, then destroy the copy -
        /// otherwise a frame exists in which neither the copy nor the Penitent is visible. The
        /// renderer restore is conditional on _rendererHidden (only undoes what we did). _body is
        /// preferred over the current Penitent.SpriteRenderer because at respawn the current
        /// renderer may be momentarily null and would leave the alpha at zero.
        /// </summary>
        private static void Close(string why)
        {
            string report = why;
            try
            {
                Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;

                if (_rendererHidden && _body != null)
                {
                    float was = _body.color.a;
                    Color restored = _body.color;
                    restored.a = _originalAlpha;
                    _body.color = restored;
                    ModLog.Info(MARK + " local corpse shown (renderer=SpriteRenderer of " +
                                (_body.gameObject != null ? _body.gameObject.name : "?") +
                                " · enabled=" + (_body.enabled ? "True" : "False") +
                                " · alpha " + F(was) + " -> " + F(_body.color.a) +
                                ") · it is the object we hid");
                }
                else if (_rendererHidden && penitent != null && penitent.SpriteRenderer != null)
                {
                    float was = penitent.SpriteRenderer.color.a;
                    Color restored = penitent.SpriteRenderer.color;
                    restored.a = _originalAlpha;
                    penitent.SpriteRenderer.color = restored;
                    ModLog.Info(MARK + " local corpse shown (renderer=SpriteRenderer of " +
                                (penitent.SpriteRenderer.gameObject != null
                                    ? penitent.SpriteRenderer.gameObject.name : "?") +
                                " · enabled=" + (penitent.SpriteRenderer.enabled ? "True" : "False") +
                                " · alpha " + F(was) + " -> " + F(penitent.SpriteRenderer.color.a) +
                                ") · FALLBACK: the renderer we hid no longer exists");
                }
                else if (_rendererHidden)
                {
                    ModLog.Info(MARK + " local corpse NOT shown (renderer=destroyed and Penitent " +
                                "rebuilt: nothing to restore, this is the respawn case - the new " +
                                "Penitent is a different object and starts visible)");
                }
            }
            catch (Exception e)
            {
                Fail("Close/renderer", e);
            }

            try
            {
                if (_copy != null)
                {
                    UnityEngine.Object.Destroy(_copy);
                    _copy = null;
                }
            }
            catch (Exception e)
            {
                Fail("Close/copy", e);
            }

            float lasted = _startedAt > 0f ? Time.unscaledTime - _startedAt : 0f;

            // Read before the reset: reading after the zeroing would print "no" regardless of the
            // pause state, which is exactly what made the previous session's marker lie.
            bool sendPaused = _positionSendPaused;
            int reasserts = _hiddenReasserts;
            int poseFrames = _poseFrames;
            bool poseClosed = _poseClosed;
            string poseWhy = _poseClosedWhy;

            _active = false;
            _rendererHidden = false;
            _startedAt = 0f;
            _pausedByCinematic = false;
            _repeatReported = false;
            _positionSendPaused = false;
            _teleportFailed = false;
            _copyRenderer = null;
            _copyBody = null;
            _body = null;
            _poseClosed = false;
            _poseFrames = 0;
            _hiddenReasserts = 0;
            _poseClosedWhy = "in corso";

            ModLog.Info(MARK + " window closed: " + report + " · durata=" + F(lasted) +
                        "s spostamenti=" + _moves + " ultimaDistanza=" + F(_lastGap) +
                        " invioPosizioneCongelato=" + (sendPaused ? "si" : "no") +
                        " · posa=" + (poseClosed ? "chiusa" : "ancora aperta") + "(" + poseWhy + ")" +
                        " framePosa=" + poseFrames + " riasserzioniAlpha=" + reasserts +
                        " · the copy is destroyed");

            TryRealignNetwork();
        }

        private static void TryRealignNetwork()
        {
            try
            {
                if (Main.Multiplayer == null || Main.Multiplayer.NetworkManager == null ||
                    !Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                Main.Multiplayer.MainPlayerManager.SendAllLocationData();
                ModLog.Info(MARK + " position/animation/direction sent to peer " +
                            "(while dead the send was frozen: without this line the avatar would stay " +
                            "where it fell until the next heartbeat)");
            }
            catch (Exception e)
            {
                // Type is printed: SendAllLocationData reads Penitent.Animator and Penitent.SpriteRenderer
                // (CurrentAnimation/CurrentDirection), and at respawn one of them may still be null.
                ModLog.Error(MARK_FAIL + " in TryRealignNetwork (no effect on the game): " +
                             e.GetType().Name + " - " + e.Message);
            }
        }

        // --- Shared with MainPlayerManager

        /// <summary>
        /// True while the death window is open and the follow has started. Read by
        /// MainPlayerManager.Update to suppress position sends. Keyed on _active (not on
        /// IsLocalPlayerDead) so that a failed clone - which leaves the Penitent where it was -
        /// does not freeze sending needlessly.
        /// </summary>
        internal static bool PositionSendPaused => _active;

        /// <summary>Records that position sending was actually suppressed, so the closing line can report it.</summary>
        internal static void NotePositionSendPaused()
        {
            _positionSendPaused = true;
        }

        /// <summary>Last line of defense: must not throw itself.</summary>
        internal static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " in " + where + " (no effect on the game): " + e.Message);
            }
            catch
            {
            }
        }

        private static void ReportOnce(string reason)
        {
            if (_repeatReported)
                return;

            _repeatReported = true;
            ModLog.Error(MARK + " NOT activated: " + reason);
        }

        /// <summary>Compact vector with invariant formatting (like the other probes).</summary>
        private static string V(Vector2 v)
        {
            return "(" + F(v.x) + "," + F(v.y) + ")";
        }

        /// <summary>
        /// Fixed numeric format (invariant). The log's key is order and numbers; a locale-dependent
        /// separator would make them ambiguous.
        /// </summary>
        private static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}