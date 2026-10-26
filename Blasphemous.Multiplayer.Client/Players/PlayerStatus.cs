using Framework.Managers;
using System.Collections;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players
{
    /// <summary>
    /// State of another player as seen by this machine: name (used as unique id), current and
    /// last map scene, team, special animation, ping, skin, and the "down" flag. The <c>IsDead</c>
    /// state cannot be queried from the game (only one Penitent exists; remotes are not Penitents),
    /// so it is fed exclusively by the peer's announcements.
    /// </summary>
    public class PlayerStatus
    {
        // Name of this player.  Used as unique id
        public string Name { get; private set; }

        // Current scene to determine if in same scene
        private string _currentScene;
        public string CurrentScene
        {
            get => _currentScene;
            set
            {
                _currentScene = value;
                if (value.Length == 9)
                    _lastMapScene = value;
            }
        }

        // Last valid scene to show on map
        private string _lastMapScene;
        public string LastMapScene => _lastMapScene;

        // Current team of this player.  Used to determine map icons & pvp availability
        public byte Team { get; set; }

        // Animation id when this player is using a special animation
        public byte SpecialAnimation { get; set; }

        // Average ping in ms
        public ushort Ping { get; set; } = 0;

        // Skin sprite, is updated whenever receiving a skin update packet
        private Texture2D _skinTexture;
        public Texture2D SkinTexture => _skinTexture;

        // Determines whether to set player objects skin texture in an update cycle
        public SkinStatus SkinUpdateStatus { get; set; }

        /// <summary>
        /// True when this player is currently down. The game cannot answer this: there is one
        /// Penitent, and the other player is a bare GameObject, not a Penitent. The only source is
        /// the peer's pdead/palive announcements. Two consumers depend on it: the count of
        /// standing players (which ends the spectator state) and the boss handover (which must
        /// target a player who can still fight).
        /// </summary>
        public bool IsDead { get; private set; }

        /// <summary>Records that this player is down, or back up.</summary>
        public void SetDead(bool dead)
        {
            IsDead = dead;
        }

        public PlayerStatus(string name)
        {
            Name = name;
            Team = 1;
            SpecialAnimation = 0;
            _currentScene = string.Empty;
            _lastMapScene = string.Empty;

            SetSkinTexture("PENITENT_DEFAULT");
            SkinUpdateStatus = SkinStatus.Updated;
        }

        public void SetSkinTexture(string skinName)
        {
            _skinTexture = Core.ColorPaletteManager.GetColorPaletteById(skinName).texture;
        }

        public void SetSkinTexture(byte[] skinData)
        {
            Main.Instance.StartCoroutine(DelaySkinUpdate());

            IEnumerator DelaySkinUpdate()
            {
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                Texture2D tex = new Texture2D(256, 1, TextureFormat.RGB24, false);
                tex.LoadImage(skinData);
                tex.filterMode = FilterMode.Point;
                _skinTexture = tex;
            }
        }

        public static bool IsOriginalSkin(string skinName)
        {
            foreach (string ogskin in originalSkins)
            {
                if (skinName == ogskin)
                    return true;
            }
            return false;
        }

        private static readonly string[] originalSkins = new string[]
        {
            "PENITENT_DEFAULT",
            "PENITENT_ENDING_A",
            "PENITENT_ENDING_B",
            "PENITENT_OSSUARY",
            "PENITENT_BACKER",
            "PENITENT_DELUXE",
            "PENITENT_ALMS",
            "PENITENT_PE01",
            "PENITENT_PE02",
            "PENITENT_PE03",
            "PENITENT_BOSSRUSH",
            "PENITENT_DEMAKE",
            "PENITENT_ENDING_C",
            "PENITENT_SIERPES",
            "PENITENT_ISIDORA",
            "PENITENT_BOSSRUSH_S",
            "PENITENT_GAMEBOY",
            "PENITENT_KONAMI"
        };

        public enum SkinStatus
        {
            Updated = 0,
            YesUpdate = 1,
            NoUpdate = 2,
        }

        /// <summary>
        /// The three death states, by the same names the avatar already checks in
        /// <c>OtherPlayerScript.Update</c>. Lives here because two sides that never talk - the
        /// sender (MainPlayerManager) and the receiver (OtherPlayerScript) - must agree on the
        /// names; if the table lived on one side, the other would need the numbers memorised.
        /// Indices in AnimationStates.animations are 47, 48, 49: the array POSITION, not the file
        /// line number. "Grounding Over" is deliberately excluded: the avatar also treats it as a
        /// death pose, but the game itself uses it for the fall recovery too, so it does not prove
        /// a death. The final death pose comes from the game (PlayerDeathAnimationBehaviour
        /// disables the Animator at normalizedTime >= 0.95).
        /// </summary>
        private static readonly string[] DeathStates = { "Death", "Death Spike", "Death Fall" };

        /// <summary>
        /// The AnimationStates.animations index matching one of the passed state names, or -1 if
        /// none is a death. Takes the states rather than reading them: both sides already have
        /// their own AnimatorStateInfo, and reading here would pick one side - the exact
        /// misalignment this table exists to prevent.
        /// </summary>
        public static int DeathAnimationIndexOf(params string[] stateNames)
        {
            if (stateNames == null)
                return -1;

            for (int i = 0; i < Data.AnimationStates.animations.Length; i++)
            {
                string name = Data.AnimationStates.animations[i].name;
                for (int s = 0; s < stateNames.Length; s++)
                {
                    if (stateNames[s] == name)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// True when this byte is one of the three deaths, i.e. "the peer is on the ground".
        /// Compares the byte's state name against <see cref="DeathStates"/> by name, not through
        /// <c>DeathAnimationIndexOf</c> - the previous version asked that function whether a name
        /// taken from the table appeared IN the table, which was always true.
        /// </summary>
        public static bool IsDeathAnimation(byte animation)
        {
            Data.PlayerAnimState[] all = Data.AnimationStates.animations;
            if (animation >= all.Length)
                return false;

            string stateName = all[animation].name;
            for (int i = 0; i < DeathStates.Length; i++)
            {
                if (DeathStates[i] == stateName)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The death index used as fallback when the Penitent's Animator is not (yet) in a death
        /// state: "Death", the common case. See the fuse in <c>MainPlayerManager.CorpsePeerTick</c>.
        /// </summary>
        public static readonly byte DefaultDeathAnimation =
            (byte)DeathAnimationIndexOf(DeathStates);
    }
}