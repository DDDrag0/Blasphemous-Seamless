using Framework.Managers;
using System.Collections;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players
{
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

        // Whether this player is currently down.
        //
        // It is state the game does not have and cannot be asked for: there is exactly one Penitent, and
        // the other player is not one - OtherPlayerManager builds them as a bare GameObject with a script
        // and no Penitent at all, so nothing on this machine can inspect whether they are alive. The only
        // source is what they tell us, which is what the pdead/palive announcements are for.
        //
        // It matters because two things depend on it: the count of who is still standing, which decides
        // when a spectator state may end, and the boss, which has to hand over to someone who can fight it.
        //
        // Reset is deliberately silent about a player it does not know. Network messages can arrive before
        // the connection entry exists, and FindConnectedPlayer also LOGS A WARNING when it misses - so the
        // noisy lookup is the wrong tool here: a legitimate message for a not-yet-known player would fill
        // the log with warnings that mean nothing.
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

        // ============================================================================================
        // LA MORTE E' UN'ANIMAZIONE, E QUESTA E' LA SUA TABELLA.
        // ============================================================================================
        //
        // ⭐⭐ PERCHE' STA QUI, e non su una delle due sponde. Il byte di morte ha DUE lettori che non si
        // parlano mai: chi lo SPEDE (`MainPlayerManager`, la macchina del morto) e chi lo RICEVE
        // (`OtherPlayerScript`, la macchina che guarda). Se la tabella vivesse su una delle due, l'altra
        // dovrebbe conoscerne i numeri a memoria - ed e' esattamente il modo in cui "spedisco una cosa e
        // l'altro ne riconosce un'altra" diventa un guasto che nessuna delle due parti vede.
        //
        // ⭐ I NOMI SONO QUELLI VERI, letti in `Data/AnimationStates.cs:64-66` (indici 55, 56, 57) e non
        // inventati: "Death" (55), "Death Spike" (56), "Death Fall" (57).
        //
        // ⭐ E SONO GLI STESSI CHE L'AVATAR GIA' GUARDA PER CONTO SUO: `OtherPlayerScript.Update` riconosce
        // il proprio cadavere con `state.IsName("Death") | "Death Spike" | "Death Fall" | "Grounding Over"`.
        // Cioe': la mod non deve INVENTARE una posa di morte, l'Animator dell'avatar ce l'ha gia'.
        //
        // ⚠️ E "Grounding Over" E' NELLA LISTA DELL'AVATAR MA **NON** QUI, e la differenza e' voluta:
        // "Grounding Over" e' anche il rialzarsi da una caduta, quindi non e' una prova che qualcuno sia
        // morto - infatti nel gioco la morte lo PRECEDE (`PlayerDeathAnimationBehaviour.OnStateEnter` fa
        // `animator.Play("Death Fall")` per il penitent spiaccicato). Spedirlo come morte riporterebbe a
        // terra un avatar solo perche' e' caduto.
        //
        // ⚠️ LA FINE DELLA SEQUENZA E' GIA' SCRITTA NEL GIOCO, e sta in `PlayerDeathAnimationBehaviour.cs:33-36`:
        // `if (stateInfo.normalizedTime >= 0.95f && _penitent.Status.Dead) animator.enabled = false;`
        // ⇒ il gioco **spegne l'Animator del Penitent sulla posa FINALE di "Death"**, e quella e' la posa a
        // terra del cadavere. L'avatar del peer fa lo stesso per via sua (`SetDeathStatus(true)`), quindi la
        // posa che si vede di la' e' la STESSA che si vede di qua: nessuno sprite nuovo, nessuna posa
        // indovinata.
        private static readonly string[] DeathStates = { "Death", "Death Spike", "Death Fall" };

        /// <summary>
        /// L'indice di <see cref="AnimationStates.animations"/> che corrisponde a uno degli stati di morte,
        /// oppure -1 se nessuno degli stati passati e' una morte.
        ///
        /// ⚠️ PRENDE GLI STATI, NON LI LEGGE. Entrambe le sponde hanno gia' in mano il proprio
        /// `AnimatorStateInfo` (il mandante legge il Penitent, il ricevente legge l'avatar), e farlo leggere
        /// qui significherebbe scegliere una delle due - cioe' esattamente il disallineamento che questa
        /// tabella esiste per rendere impossibile.
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

        /// <summary>True quando questo byte e' una delle tre morti - cioe' "il peer e' a terra".</summary>
        public static bool IsDeathAnimation(byte animation)
        {
            Data.PlayerAnimState[] all = Data.AnimationStates.animations;
            if (animation >= all.Length)
                return false;

            // Il nome dello stato si passa come UNO stato: `params` lo impacchetta da solo, mentre passare
            // direttamente il `string` a un metodo `params string[]` non compila.
            return DeathAnimationIndexOf(all[animation].name) >= 0;
        }

        /// <summary>
        /// L'indice di morte usato come RIPIEGO quando l'Animator del Penitent non e' (ancora) in uno stato
        /// di morte: "Death", il caso normale. Vedi il fusibile in <c>MainPlayerManager.CorpsePeerTick</c>.
        /// </summary>
        public static readonly byte DefaultDeathAnimation =
            (byte)DeathAnimationIndexOf(DeathStates);
    }
}
