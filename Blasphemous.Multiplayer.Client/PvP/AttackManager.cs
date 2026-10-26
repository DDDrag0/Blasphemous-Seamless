using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Player.Sparks;
using Gameplay.GameControllers.Entities;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.PvP
{
    public class AttackManager
    {
        private readonly Dictionary<AttackType, AttackData> allAttacks;

        /// <summary>
        /// Counts peer footsteps. Used only by the log filter in ReceiveEffect: footsteps are the
        /// most frequent effect on the channel and one line each would flood the log. Not game
        /// state; nothing else reads it.
        /// </summary>
        private int footStepArrivals;

        public AttackManager()
        {
            allAttacks = new Dictionary<AttackType, AttackData>();
            LoadAttacks();
        }

        public void ReceiveAttack(string attackerName, string receiverName, AttackType attack, byte damageAmount)
        {
            if (Core.Logic.Penitent == null || !allAttacks.ContainsKey(attack)) return;

            if (receiverName == Main.Multiplayer.PlayerName) // This is the player that got hit
            {
                // Find the attacker in the scene
                OtherPlayerScript attacker = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(attackerName);
                if (attacker == null)
                    return;

                // Ensure your input is not blocked
                if (Core.Input.HasBlocker("ANY"))
                    return;

                // Ensure pvp between you is allowed
                Config config = Main.Multiplayer.config;
                byte attackerTeam = Main.Multiplayer.OtherPlayerManager.FindConnectedPlayer(attackerName).Team;
                if (!config.enablePvP || (!config.enableFriendlyFire && Main.Multiplayer.PlayerTeam == attackerTeam))
                    return;

                // Perform the damage
                ModLog.Warn($"Receiving hit {attack} from {attackerName}");
                DamagePlayer(attack, damageAmount, attacker.gameObject);
            }
            else // It was a different player that got hit
            {
                // Simply display an effect
                ShowDamageEffects(receiverName);
            }
        }

        public void ReceiveEffect(string playerName, EffectType effect)
        {
            ReceiveEffect(playerName, effect, false, Vector2.zero);
        }

        /// <summary>
        /// Replays an effect the other player produced (A3 trails, A4 ground impact and slide dust).
        /// hasPosition is its own flag rather than a "zero means none" convention, because the
        /// origin is a place an effect can legitimately happen at; conflating the two would draw
        /// an effect at the wrong end of the map.
        /// </summary>
        public void ReceiveEffect(string playerName, EffectType effect, bool hasPosition, Vector2 position)
        {
            // This channel used to be silent on arrival, making the effect path unverifiable: "never
            // arrived" and "arrived and did nothing" looked identical. Footsteps are filtered to
            // first + every tenth to avoid flooding at ~4-5/s; other effects are one line each.
            if (effect == EffectType.FootStep)
            {
                footStepArrivals++;
                if (footStepArrivals == 1 || footStepArrivals % 10 == 0)
                {
                    ModLog.Info($"[Effect] {effect} n.{footStepArrivals} per {playerName}" +
                                (hasPosition ? $" ({position.x:F1},{position.y:F1})" : string.Empty));
                }
            }
            else
            {
                ModLog.Info($"[Effect] {effect} per {playerName}" +
                            (hasPosition ? $" ({position.x:F1},{position.y:F1})" : string.Empty));
            }

            OtherPlayerScript other = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(playerName);
            if (other == null)
            {
                ModLog.Warn($"[Effect] {effect} for {playerName}: no active player found");
                return;
            }

            other.OtherPlayerAttack.PlayEffectAnimation(effect, other.IsFacingRight, hasPosition, position);
        }

        private void DamagePlayer(AttackType attack, byte damageAmount, GameObject attacker)
        {
            // Calculate final damage amount based on resistances
            float finalDamage = Main.Multiplayer.DamageCalculator.CalculateDefense(attack, damageAmount);

            // Calculate hit data based on attack & parameters
            AttackData currentAttack = GetAttackData(attack);
            Hit hit = new()
            {
                AttackingEntity = attacker,
                DamageAmount = finalDamage,
                DamageElement = currentAttack.DamageElement,
                DamageType = currentAttack.DamageType,
                Force = currentAttack.Force,
                ThrowbackDirByOwnerPosition = true,
                Unparriable = true,
                Unblockable = true,
                Unnavoidable = true,
                HitSoundId = $"PVP{currentAttack.SoundId}"
            };

            // Actually damage player
            Core.Logic.Penitent.Damage(hit);
        }

        /// <summary>
        /// Damages the player with an attack without validation
        /// </summary>
        public void DamagePlayer_Internal(AttackType type, byte amount)
        {
            DamagePlayer(type, amount, Core.Logic.Penitent.gameObject);
        }

        public void ShowDamageEffects(string receiverName)
        {
            OtherPlayerScript receiver = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(receiverName);
            if (receiver == null) return;

            Vector3 effectPosition = receiver.transform.position + Vector3.up;
            Core.Logic.Penitent.GetComponentInChildren<SwordSparkSpawner>().GetSwordSpark(effectPosition);
            GameObject blood = Core.Logic.Penitent.GetComponentInChildren<BloodSpawner>().GetBloodFX(BloodSpawner.BLOOD_FX_TYPES.SMALL);
            blood.transform.position = effectPosition;
            Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy();
        }

        public AttackData GetAttackData(AttackType type)
        {
            if (allAttacks.TryGetValue(type, out AttackData attack))
                return attack;

            throw new System.Exception($"Attack type \"{type}\" doesn't exist!");
        }

        private void LoadAttacks()
        {
            if (!Main.Multiplayer.FileHandler.LoadDataAsJson("attacks.json", out AttackData[] attacks))
            {
                ModLog.Error("Failed to load attack data!");
                return;
            }
            
            foreach (AttackData attack in attacks)
            {
                allAttacks.Add(attack.Name, attack);
            }
            ModLog.Info("Successfully loaded all attack data!");
        }
    }
}