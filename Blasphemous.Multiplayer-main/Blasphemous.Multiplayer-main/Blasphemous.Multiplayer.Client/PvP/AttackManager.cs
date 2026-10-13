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
        /// Quanti passi del peer sono arrivati. Serve SOLO al filtro del log (vedi ReceiveEffect): i passi
        /// sono l'effetto piu' frequente del canale e una riga ciascuno renderebbe il log illeggibile.
        /// Non e' uno stato di gioco: non decide nulla, non si azzera a un evento, e nessun altro lo legge.
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
        ///
        /// hasPosition is its own flag rather than a "zero means none" convention, because the origin is a
        /// place an effect can legitimately happen at - the level's coordinates start there - and a convention
        /// that quietly means two things is how an effect ends up drawn at the wrong end of the map.
        /// </summary>
        public void ReceiveEffect(string playerName, EffectType effect, bool hasPosition, Vector2 position)
        {
            // Logged because this channel used to be completely SILENT on arrival, and that silence made the
            // whole effect path unverifiable: an effect that never arrived and an effect that arrived and did
            // nothing looked EXACTLY the same in the logs. One line per effect tells them apart, and every
            // effect fix (trails, ground hits, slide dust) has to be verified through it.
            // A5 - IL FILTRO SUI SUONI FREQUENTI, ed e' obbligatorio, non un abbellimento.
            //
            // Questa riga e' l'unica prova che un effetto e' ARRIVATO, e per A3/A4 si legge tutta: gli
            // effetti di A4 sono rari e uno per uno contano. I PASSI no: alla cadenza della falcata sono
            // 4..5 al secondo, e in una sessione di dieci minuti sono ~2700 righe che non dicono nulla di
            // nuovo e nascondono quelle che dicono qualcosa. Un log che si puo' leggere e' lo strumento;
            // un log che non si puo' leggere e' la ragione per cui un guasto resta invisibile per giorni.
            //
            // ⚠️ SI CONTA E SI STAMPA IL PRIMO E POI UNO OGNI DIECI. Il primo e' quello che dice "il
            // canale e' acceso", il decimo quello che dice "sta ancora andando". Il contatore e' PER
            // EFFETTO e non globale: se i passi si fermano mentre i fendenti continuano, si vede subito.
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
                ModLog.Warn($"[Effect] {effect} per {playerName}: giocatore attivo non trovato");
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
