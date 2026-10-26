using System;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;    // EffectType
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using Tools.Level.Interactables;                    // Execution

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Executions, seventh prefix of the EnemyDeathGlobal channel. That channel is forwarded to
    /// every peer except the sender regardless of scene, and its payload is an unread UTF-8 string,
    /// so no new NetworkType and no PROTOCOL_VERSION bump are needed. The start is announced from
    /// <c>Execution.OnUse</c> (the player pressed), not from <c>InstantiateExecution</c> (the enemy
    /// merely became executable). Echo is prevented by the server excluding the sender and by the
    /// <c>IsApplyingRemoteDamage</c> guard.
    /// </summary>
    public static class ExecutionSync
    {
        /// <summary>Execution prefix of the death channel: <c>exec|</c>.</summary>
        public const string ExecPrefix = "exec|";

        /// <summary>True when this id is an execution announcement rather than a death.</summary>
        public static bool IsExecutionId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(ExecPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The enemy id inside an execution announcement. Returns null on anything malformed: an
        /// empty id would enter the death memory and be silently discarded by
        /// <c>MarkSpawnPointConsumed</c>, leaving the fix dependent on nobody ever reading it.
        /// </summary>
        public static string IdOf(string id)
        {
            if (!IsExecutionId(id))
                return null;

            string enemyId = id.Substring(ExecPrefix.Length);
            return string.IsNullOrEmpty(enemyId) ? null : enemyId;
        }

        /// <summary>
        /// Announces that THIS game has just executed an enemy (the player pressed).
        ///
        /// The hook point is <c>Execution.OnUse</c>, not <c>InstantiateExecution()</c>. The latter
        /// means "the enemy BECAME executable" (the player may never press); announcing there
        /// caused a spurious peer ghost and a desynced live/dead enemy. <c>OnUse</c> is where the
        /// game actually starts the execution, and it is also the counterpart of
        /// <c>InteractionEnd</c>.
        ///
        /// OnUse is an IEnumerator, but the postfix runs at the call (iterator creation), which
        /// happens inside the same frame as the keypress, before <c>KillEntity()</c> - so the
        /// announcement and the death are in the same frame.
        /// </summary>
        public static void Announce(Execution prop)
        {
            try
            {
                if (prop == null)
                    return;

                if (!Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                // A damage relayed by this machine must not re-announce.
                if (EnemySyncManager.IsApplyingRemoteDamage)
                    return;

                // The executed enemy lives on the prop (Execution.ExecutedEntity), populated by
                // InstantiateExecution when the enemy became executable - so it is already set
                // when the player presses.
                Enemy enemy = prop.ExecutedEntity;

                string id;
                if (!EnemySyncManager.TryGetEnemyId(enemy, out id))
                {
                    // Observation, not a verdict: an enemy not in _ids cannot be announced on a
                    // channel that identifies by string. Without this line, "no announcement" and
                    // "no enemy" are indistinguishable.
                    ModLog.Info("[Exec TX] execution on an enemy without a network id: no announcement");
                    return;
                }

                // Sender-side proof. The [Exec RX] line is written by the receiver, and the send
                // is silent while disconnected. Without this line, "the postfix never ran" and
                // "the packet never arrived" are indistinguishable in both logs.
                ModLog.Info($"[Exec TX] {id}");

                Main.Multiplayer.SendEnemyDeathGlobal(ExecPrefix + id);

                // The link between start and end: prop is the same object that will call
                // InteractionEnd, so the field is written from the object in hand.
                PendingExecution = prop;

                // Sender-side proof, next to [Exec TX]: the [Effect] ExecutionStart line is
                // written by the receiver. Without this, the same ambiguity applies to the
                // animation channel.
                ModLog.Info("[ExecAnim TX] START");
                Main.Multiplayer.NetworkManager.SendEffect(EffectType.ExecutionStart);
            }
            catch (Exception e)
            {
                ModLog.Error("[Exec TX] announcement failed: " + e.GetType().Name + " - " + e.Message);
            }
        }

        /// <summary>
        /// Announces that the execution animation is over, so the receiver can restore its avatar.
        ///
        /// Uses <c>NetworkType.Effect</c> rather than an eighth prefix on the death channel: the
        /// death channel does not carry the sender, and the animation plays on the executing
        /// player's avatar, so a name is required. <c>EffectType</c> 105/106 are free, no bump.
        /// </summary>
        public static void AnnounceEnd()
        {
            try
            {
                Execution current = PendingExecution;
                PendingExecution = null;

                if (current == null)
                {
                    // Observation, not a verdict: "the start never ran" and "this is a second
                    // end" are indistinguishable without this line.
                    ModLog.Info("[ExecAnim TX] END without an announced start: no packet");
                    return;
                }

                if (!Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                if (EnemySyncManager.IsApplyingRemoteDamage)
                    return;

                // Sender-side proof, same reason as [Exec TX].
                ModLog.Info("[ExecAnim TX] END");

                Main.Multiplayer.NetworkManager.SendEffect(EffectType.ExecutionEnd);
            }
            catch (Exception e)
            {
                ModLog.Error("[ExecAnim TX] end failed: " + e.GetType().Name + " - " + e.Message);
            }
        }

        /// <summary>
        /// The execution in progress on this machine, or null.
        ///
        /// Start and end are two different game methods (<c>OnUse</c> and <c>InteractionEnd</c>),
        /// and OnUse is a coroutine: when InteractionEnd runs, OnUse's stack is long gone. This
        /// field is the only link between them. Reset unconditionally by
        /// <see cref="AnnounceEnd"/>; a destroyed Unity object compares equal to null, so every
        /// read is guarded.
        /// </summary>
        private static Execution PendingExecution;
    }

    /// <summary>
    /// Postfix on <c>Execution.OnUse</c>: the moment the player presses.
    ///
    /// OnUse is an IEnumerator, but the postfix runs at the call (iterator creation), which
    /// happens inside the same frame as the keypress and before <c>KillEntity()</c> - so the
    /// announcement and the death are in the same frame. Same shape already used for other
    /// <c>OnUse</c> coroutines. A postfix leaves the game method untouched: no return value, no
    /// state, no side effects altered.
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
    /// Postfix on <c>Execution.InteractionEnd</c>: the moment the game knows the execution is
    /// over. The method writes to the LOCAL Penitent (IsOnExecution, GhostTrailsAllowed,
    /// AddFervourBonus), so a postfix lets those run untouched and only observes.
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