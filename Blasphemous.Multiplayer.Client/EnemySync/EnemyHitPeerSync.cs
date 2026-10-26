using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Aggro proxy made damageable: it is the only object the game sees in place of the remote
    /// player, so it is the only interception point for an enemy hit on the simulating machine.
    /// It never touches the local <see cref="Penitent"/>: <see cref="Damage"/> only forwards the
    /// hit to the peer, which applies it via <c>Penitent.Damage(Hit)</c>. <see cref="SparkOnImpact"/>
    /// and <see cref="BleedOnImpact"/> return false because the game queries them before dealing
    /// damage and the proxy must not produce local effects.
    /// </summary>
    public class CoopRemoteTargetDamageable : MonoBehaviour, IDamageable
    {
        /// <summary>Hit landed on the proxy: forward to the peer. See <see cref="EnemyHitPeer.Forward"/>.</summary>
        public void Damage(Hit hit)
        {
            EnemyHitPeer.Forward(hit);
        }

        /// <summary>Proxy position, i.e. where this machine believes the other player stands.</summary>
        public Vector3 GetPosition()
        {
            return transform.position;
        }

        /// <summary>Never: the proxy must not request sparks for a hit that is not its own.</summary>
        public bool SparkOnImpact()
        {
            return false;
        }

        /// <summary>Never: the proxy does not bleed and must not spawn blood on the wrong machine.</summary>
        public bool BleedOnImpact()
        {
            return false;
        }
    }

    /// <summary>
    /// Enemy-to-player channel. Reuses <c>NetworkType.Attack</c> (a scene broadcast the server relays
    /// to every peer except the sender, with the sender name prefixed) and distinguishes this
    /// direction with <c>AttackType.EnemyHit</c>. The player-to-enemy direction cannot be reused:
    /// its payload is an enemy handle and it damages the enemy, the opposite of what is needed.
    /// Reusing the Attack channel adds no protocol or server code and degrades gracefully on
    /// mismatched peers (an old peer ignores type 200 and exits; an old server relays the bytes
    /// without interpreting them).
    ///
    /// Packet layout after the server-prefixed sender name (<c>startIdx</c> = first byte of the real
    /// payload):
    /// <code>
    ///   +0   byte    AttackType.EnemyHit (200)
    ///   +1   float   DamageAmount
    ///   +5   byte    DamageType
    ///   +6   byte    DamageElement
    ///   +7   ushort  flags (see FlagsFrom)
    ///   +9   float   Force
    ///   +13  uint    enemy handle (to resolve the local attacker copy)
    ///   +17  byte    recipient name length
    ///   +18  string  recipient name (UTF8)
    ///   ..   byte    sound id length
    ///   ..   string  impact sound id (UTF8)
    /// </code>
    /// The recipient name is required because the Attack channel is a scene broadcast. The sound
    /// travels as the exact bank path the enemy uses (<c>Hit.HitSoundId</c>, an <c>[EventRef]</c>
    /// field of the prefab), not as a reinterpreted key.
    /// </summary>
    internal static class EnemyHitPeer
    {
        /// <summary>
        /// Prefix of every log line of this direction. The "forwarded" / "applied" lines are the
        /// measure of the fix; the "not forwarded" / "not applied" lines make a dropped hit
        /// distinguishable from "nothing happened".
        /// </summary>
        internal const string Marker = "[DannoPeer]";

        /// <summary>
        /// Minimum interval between two forwards by the SAME enemy. It is a traffic gate, not a
        /// damage gate (the receiver applies its own invulnerability frames): the proxy is
        /// repositioned on the remote player every rescan, so it can re-enter the same attack area
        /// and produce a new <c>OnTriggerEnter2D</c> each time.
        /// </summary>
        private const float MIN_FORWARD_INTERVAL = 0.2f;

        /// <summary>Last forward time per enemy (key: enemy instance id).</summary>
        private static readonly Dictionary<int, float> _lastForward = new Dictionary<int, float>();

        /// <summary>
        /// Last time a "not forwarded" / "not applied" line was written. Throttled because
        /// <c>SkipForward</c> is also called for hits that do not concern us (traps on the proxy)
        /// and would otherwise spam the log. The two lines that matter are not throttled.
        /// </summary>
        private static float _lastSkipLog;

        private const float SKIP_LOG_INTERVAL = 1f;

        // ============================================================================================
        // SENDER SIDE: the machine simulating the enemy.
        // ============================================================================================

        /// <summary>
        /// Called from the proxy's <c>Damage</c>. Decides whether the hit can be forwarded and does
        /// it. Never throws: a failed forward must not stop the game attack, which has already been
        /// diverted here.
        /// </summary>
        internal static void Forward(Hit hit)
        {
            try
            {
                if (!Main.Multiplayer.NetworkManager.IsConnected)
                {
                    SkipForward("not connected");
                    return;
                }

                Enemy attacker = AttackerOf(hit);
                if (attacker == null)
                {
                    SkipForward("attacker is not an ENEMY (trap, level area or player): " +
                                "the peer takes it locally, forwarding would double-damage it");
                    return;
                }

                if (!IsDamageTypeThePenitentHandles(hit.DamageType))
                {
                    SkipForward("damage type " + hit.DamageType + " not handled by the Penitent");
                    return;
                }

                float amount = hit.DamageAmount;
                if (!(amount > 0f) || float.IsNaN(amount) || float.IsInfinity(amount))
                {
                    SkipForward("non-positive damage (" + amount.ToString("F2") + ")");
                    return;
                }

                string peerName;
                if (!EnemySyncManager.TryGetRemotePlayerHereAndAlive(out peerName))
                {
                    SkipForward("no other player HERE and ALIVE");
                    return;
                }

                int key = attacker.GetInstanceID();
                float last;
                if (_lastForward.TryGetValue(key, out last) && Time.time - last < MIN_FORWARD_INTERVAL)
                {
                    SkipForward("the same enemy already forwarded a hit " +
                                (Time.time - last).ToString("F2") + " s ago (proxy re-entered the area)");
                    return;
                }

                if (_lastForward.Count > 64)
                    _lastForward.Clear();

                _lastForward[key] = Time.time;

                uint handle;
                if (!EnemySyncManager.TryGetEnemyHandle(attacker, out handle))
                    handle = 0;

                string enemyId;
                if (!EnemySyncManager.TryGetEnemyId(attacker, out enemyId))
                    enemyId = null;

                byte[] payload = BuildPayload(peerName, handle, hit);

                Main.Multiplayer.NetworkManager.SendAttackPayload(payload);

                ModLog.Info($"{Marker} ENEMY hit forwarded to peer: " +
                            $"enemy={(enemyId ?? "handle=" + handle.ToString("X8"))} " +
                            $"danno={amount:F0} tipo={hit.DamageType} elem={hit.DamageElement} " +
                            $"bandiere={FlagsFrom(hit):X4} a={peerName} " +
                            $"nemicoPos={Pos(attacker.transform.position)} " +
                            $"proxyPos={ProxyPosition()}");
            }
            catch (Exception e)
            {
                ModLog.Error($"{Marker} forward failed ({e.GetType().Name}): {e.Message} - " +
                             "the hit was not applied here nor there");
            }
        }

        /// <summary>
        /// The only filter that makes this direction safe: the hit must come from an <c>Enemy</c>.
        /// <c>GetComponentInParent</c> is used because some enemy attacks build the <c>Hit</c> with
        /// the enemy root and others with a child component. Level hazards (traps,
        /// <c>SimpleDamageArea</c>, <c>DamageObject</c>) and the local <c>Penitent</c> have no
        /// <c>Enemy</c> among their parents and stop here, avoiding double damage.
        /// </summary>
        private static Enemy AttackerOf(Hit hit)
        {
            if (hit.AttackingEntity == null)
                return null;

            return hit.AttackingEntity.GetComponentInParent<Enemy>();
        }

        /// <summary>
        /// The four damage types <c>PenitentDamageArea.TakeDamage</c> handles. <c>Stunt</c> and
        /// <c>OptionalStunt</c> reach <c>throw new ArgumentOutOfRangeException()</c>; filtering
        /// them defensively prevents a malformed packet from crashing the receiver.
        /// </summary>
        private static bool IsDamageTypeThePenitentHandles(DamageArea.DamageType type)
        {
            return type == DamageArea.DamageType.Normal
                || type == DamageArea.DamageType.Heavy
                || type == DamageArea.DamageType.Simple
                || type == DamageArea.DamageType.Critical;
        }

        private static string ProxyPosition()
        {
            try
            {
                return EnemySyncManager.RemoteTargetProxyPosition();
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// Formats a position on one log line using the English decimal separator, so coordinate
        /// lists stay structurally readable when compared across machines.
        /// </summary>
        private static string Pos(Vector3 position)
        {
            return "(" + position.x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                         position.y.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        // ============================================================================================
        // THE PACKET
        // ============================================================================================

        /// <summary>Fixed head length: type(1) damage(4) damageType(1) element(1) flags(2) force(4) handle(4) nameLen(1).</summary>
        private const int HEAD_LENGTH = 18;

        /// <summary>
        /// All nine <c>Hit</c> booleans travel, not just the three of the enemy channel:
        /// <c>Penitent.Damage</c> branches on them (<c>forceGuardslide</c>, <c>DamageType</c>,
        /// <c>ThrowbackDirByOwnerPosition</c>), so a lost flag is a hit that reacts differently
        /// from what the game expects.
        /// </summary>
        private static ushort FlagsFrom(Hit hit)
        {
            ushort flags = 0;

            if (hit.Unnavoidable) flags |= 1;
            if (hit.ForceGuardSlideDirection) flags |= 2;
            if (hit.Unparriable) flags |= 4;
            if (hit.Unblockable) flags |= 8;
            if (hit.DestroysProjectiles) flags |= 16;
            if (hit.DontSpawnBlood) flags |= 32;
            if (hit.forceGuardslide) flags |= 64;
            if (hit.CheckOrientationsForGuardslide) flags |= 128;
            if (hit.ThrowbackDirByOwnerPosition) flags |= 256;

            return flags;
        }

        private static void ApplyFlags(Hit hit, ushort flags)
        {
            hit.Unnavoidable = (flags & 1) != 0;
            hit.ForceGuardSlideDirection = (flags & 2) != 0;
            hit.Unparriable = (flags & 4) != 0;
            hit.Unblockable = (flags & 8) != 0;
            hit.DestroysProjectiles = (flags & 16) != 0;
            hit.DontSpawnBlood = (flags & 32) != 0;
            hit.forceGuardslide = (flags & 64) != 0;
            hit.CheckOrientationsForGuardslide = (flags & 128) != 0;
            hit.ThrowbackDirByOwnerPosition = (flags & 256) != 0;
        }

        /// <summary>
        /// Returns the impact sound id only if it is a valid FMOD bank path. <c>Entity.Damage</c>
        /// calls the audio system before subtracting health, and a non-bank key throws, turning
        /// the hit into zero damage. Better no sound than no damage.
        /// </summary>
        private static string SoundFor(Hit hit)
        {
            string id = hit.HitSoundId;
            if (string.IsNullOrEmpty(id) || id.Length > 200)
                return string.Empty;

            return id.StartsWith("event:", StringComparison.Ordinal) ? id : string.Empty;
        }

        private static byte[] BuildPayload(string targetPlayer, uint handle, Hit hit)
        {
            string sound = SoundFor(hit);

            byte[] nameBytes = Encoding.UTF8.GetBytes(targetPlayer ?? string.Empty);
            if (nameBytes.Length > 255)
                Array.Resize(ref nameBytes, 255);

            byte[] soundBytes = Encoding.UTF8.GetBytes(sound);
            if (soundBytes.Length > 255)
                Array.Resize(ref soundBytes, 255);

            List<byte> bytes = new List<byte>(HEAD_LENGTH + nameBytes.Length + soundBytes.Length);

            bytes.Add((byte)AttackType.EnemyHit);
            bytes.AddRange(BitConverter.GetBytes(hit.DamageAmount));
            bytes.Add((byte)hit.DamageType);
            bytes.Add((byte)hit.DamageElement);
            bytes.AddRange(BitConverter.GetBytes(FlagsFrom(hit)));
            bytes.AddRange(BitConverter.GetBytes(hit.Force));
            bytes.AddRange(BitConverter.GetBytes(handle));

            bytes.Add((byte)nameBytes.Length);
            bytes.AddRange(nameBytes);

            bytes.Add((byte)soundBytes.Length);
            bytes.AddRange(soundBytes);

            return bytes.ToArray();
        }

        // ============================================================================================
        // RECEIVER SIDE: the peer, which applies the hit to its OWN Penitent.
        // ============================================================================================

        /// <summary>
        /// Applies a hit received on the Attack channel. <c>startIdx</c> is the index of the
        /// <c>AttackType.EnemyHit</c> byte inside <c>data</c> (right after the server-prefixed
        /// sender name).
        ///
        /// <c>Penitent.Damage(Hit)</c> is used instead of <c>DamageArea.TakeDamage</c>: it is the
        /// same door the game's hit uses and contains the real gates
        /// (<c>LungeAttack.Casting</c>, <c>IsOnExecution</c>, <c>IsPickingCollectibleItem</c>,
        /// <c>GuardSlide.Casting</c>, and PARRY: <c>Parry.IsOnParryChance</c> ->
        /// <c>Parry.CheckParry</c>). Going through the hurtbox would bypass all of this.
        /// </summary>
        internal static void ApplyFromWire(string senderName, byte[] data, int startIdx)
        {
            try
            {
                if (data == null || startIdx < 0 || data.Length < startIdx + HEAD_LENGTH)
                {
                    SkipApply("packet too short (" +
                              (data == null ? "null" : data.Length.ToString()) + " bytes)");
                    return;
                }

                float amount = BitConverter.ToSingle(data, startIdx + 1);
                byte damageType = data[startIdx + 5];
                byte damageElement = data[startIdx + 6];
                ushort flags = BitConverter.ToUInt16(data, startIdx + 7);
                float force = BitConverter.ToSingle(data, startIdx + 9);
                uint handle = BitConverter.ToUInt32(data, startIdx + 13);
                int index = startIdx + 17;

                int nameLength = data[index];
                index++;
                if (data.Length < index + nameLength + 1)
                {
                    SkipApply("recipient name truncated");
                    return;
                }

                string targetName = Encoding.UTF8.GetString(data, index, nameLength);
                index += nameLength;

                int soundLength = data[index];
                index++;
                if (data.Length < index + soundLength)
                {
                    SkipApply("sound id truncated");
                    return;
                }

                string soundId = Encoding.UTF8.GetString(data, index, soundLength);

                // 1. IS IT FOR ME? The Attack channel is a scene broadcast.
                string me = Main.Multiplayer.PlayerName;
                if (string.IsNullOrEmpty(me) || targetName != me)
                {
                    SkipApply("not for me (recipient=" +
                              (string.IsNullOrEmpty(targetName) ? "(empty)" : targetName) + ", me=" +
                              (string.IsNullOrEmpty(me) ? "(no name)" : me) + ")");
                    return;
                }

                // 2. IS THERE ANYONE TO HURT? A hit on the dead is not damage.
                Penitent penitent = Core.Logic.Penitent;
                if (penitent == null)
                {
                    SkipApply("no local Penitent (menu, level change)");
                    return;
                }

                if (PlayerDeathSync.IsLocalPlayerDead())
                {
                    SkipApply("my Penitent is already dead");
                    return;
                }

                // 3. IS THE PACKET REASONABLE?
                if (!(amount > 0f) || float.IsNaN(amount) || float.IsInfinity(amount))
                {
                    SkipApply("invalid damage (" + amount.ToString("F2") + ")");
                    return;
                }

                DamageArea.DamageType type = (DamageArea.DamageType)damageType;
                if (!IsDamageTypeThePenitentHandles(type))
                {
                    SkipApply("unhandled damage type (" + type + ")");
                    return;
                }

                // 4. THE ATTACKER must be a real GameObject: PenitentDamageArea.TakeDamage reads
                //    hit.AttackingEntity.transform.position to orient the pain animation and would
                //    NullReferenceException with a null attacker. Fall back to the local Penitent
                //    when the enemy copy is missing (e.g. enemy already despawned from this scene).
                Enemy attacker = EnemySyncManager.EnemyForHandle(handle);
                GameObject attackerObject = attacker != null ? attacker.gameObject : penitent.gameObject;

                Hit hit = new Hit
                {
                    AttackingEntity = attackerObject,
                    DamageAmount = amount,
                    DamageType = type,
                    DamageElement = (DamageArea.DamageElement)damageElement,
                    Force = force,
                    HitSoundId = soundId
                };

                ApplyFlags(hit, flags);

                float before = penitent.Stats.Life.Current;

                penitent.Damage(hit);

                float after = penitent.Stats.Life.Current;

                // The line that PROVES the application: states the measurable fact (health went
                // down), not the intention.
                ModLog.Info($"{Marker} APPLIED on local Penitent: da={senderName} " +
                            $"enemy={(attacker != null ? attacker.name : "handle=" + handle.ToString("X8"))} " +
                            $"danno={amount:F0} tipo={type} elem={(DamageArea.DamageElement)damageElement} " +
                            $"vitaPrima={before:F0} vitaDopo={after:F0} " +
                            $"perso={Math.Max(0f, before - after):F0}");
            }
            catch (Exception e)
            {
                ModLog.Error($"{Marker} apply failed ({e.GetType().Name}): {e.Message}");
            }
        }

        private static void SkipForward(string why)
        {
            LogSkip("not forwarded", why);
        }

        private static void SkipApply(string why)
        {
            LogSkip("not applied", why);
        }

        /// <summary>
        /// One "not forwarded" / "not applied" line, at most once per second: dropped hits can be
        /// frequent and are not the measure of the fix. The measure is the other two lines.
        /// </summary>
        private static void LogSkip(string verb, string why)
        {
            try
            {
                if (Time.time - _lastSkipLog < SKIP_LOG_INTERVAL)
                    return;

                _lastSkipLog = Time.time;
                ModLog.Info($"{Marker} {verb}: {why}");
            }
            catch
            {
            }
        }
    }
}