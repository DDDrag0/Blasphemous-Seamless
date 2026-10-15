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
        /// How often the position is re-sent even when the player has NOT moved, in seconds.
        ///
        /// The send below is gated on the position CHANGING, which is right for bandwidth and wrong for
        /// everyone who needs to LOCATE this player. The receivers treat a position older than
        /// REMOTE_POSITION_MAX_AGE (0.3 s) as unknown - EnemySyncManager.IsRemotePositionFresh - so a player
        /// standing still simply stopped existing for the enemy aggro: enemies went idle a couple of units
        /// away and only noticed again once the player took a step and a packet happened to go out. This
        /// heartbeat sits comfortably inside that window, so "standing still" is a state the other machine
        /// can always see, not a state in which the player disappears.
        /// </summary>
        private const float PositionHeartbeat = 0.2f;
        private float lastPositionSentAt;

        private readonly float totalTimeBeforeSendAnimation = 0.5f;
        private float currentTimeBeforeSendAnimation = 0;

        public void Update()
        {
            if (Main.Multiplayer.NetworkManager.IsConnected && Core.Logic.Penitent != null)
            {
                // ========================================================================================
                // ⭐⭐ LA GUARDIA DI MORTE — la posizione del MORTO NON VIAGGIA PIU'.
                //
                // PERCHE' SERVE, e perche' e' la parte piu' delicata della via (c) ("il cadavere-copia").
                // `DeathCorpseFollow` sposta il Penitent del morto **addosso al compagno vivo**, cosi' la
                // camera (che e' legata a `Core.Logic.Penitent.transform`, `CameraManager.cs:105`) lo segue
                // da sola. Ma qui sotto la posizione si legge **dallo stesso transform** (`CurrentPosition`,
                // `:94`) e si spedisce a 0,2 s **senza nessuna guardia di morte**: senza questa riga il peer
                // vedrebbe l'avatar del morto **addosso al suo**, che e' PEGGIO del difetto che stiamo
                // correggendo (il morto che guarda il vuoto).
                //
                // LA SCELTA, DICHIARATA: da morti NON SI SPEDISCE NIENTE. Sul peer l'avatar resta
                // **dov'e' caduto**, cioe' esattamente il cadavere che deve essere - e la copia che il morto
                // vede in scena e' alla stessa posizione, quindi le due macchine **raccontano la stessa
                // storia**. L'alternativa (spedire la posizione del vivo) e' quella che produce l'avatar
                // sovrapposto; l'alternativa "spedire la posizione di morte" e' **identica a questa** ma con
                // piu' codice, perche' il peer ce l'ha gia' - e' l'ultima che ha ricevuto.
                //
                // NON E' UNA NOVITA' PER LA MOD, ed e' il punto: `EnemySyncManager.cs:2366` conta GIA' su
                // questo comportamento e lo scrive - *"a dead player stops sending positions (the packet
                // only goes out when the position changes), so their last known position goes stale"*.
                // E **la posizione non e' un segnale di vita** (`PlayerDeathSync.cs:205-212`): la vita
                // viaggia su `pdead|`/`palive|`. Congelare la posizione quindi **non mente a nessuno**.
                //
                // ⚠️ E SI FERMA TUTTO IL BLOCCO, non solo la posizione: anche animazione e direzione.
                // Una direzione che arriva da un cadavere e' rumore, e l'animazione e' peggio che rumore -
                // `OtherPlayerScript.CurrentAnimation` usa l'arrivo di un'animazione come prova di vita
                // (`if (playerDead) SetDeathStatus(false)`, `:778-782`) ⇒ una sola animazione spedita da
                // morti **rimetterebbe in piedi l'avatar del morto** sull'altra macchina.
                //
                // ⚠️ LA CONDIZIONE E' `PositionSendPaused`, NON `IsLocalPlayerDead()`, e la differenza
                // conta: se il cadavere-copia **non e' riuscito a nascere**, `DeathCorpseFollow` non sposta
                // nessuno e il Penitent resta al punto di morte - quindi la posizione vera e' ancora quella
                // giusta e **non c'e' nessun motivo di tacerla**. Congelarla li' sarebbe una bugia gratuita.
                //
                // ⚠️ E IL RITORNO E' COPERTO: alla chiusura della finestra `DeathCorpseFollow.Close()`
                // chiama `SendAllLocationData()` (il metodo che esiste gia' e gira al load), quindi il peer
                // riallinea posizione, animazione e direzione **subito**, senza aspettare il battito.
                //
                // ⚠️ E SI AGGIORNA LA MEMORIA DEL "GIA' SPEDITO" SENZA SPEDIRE, che non e' un dettaglio:
                // se `lastPosition`/`lastAnimation`/`lastDirection` restassero quelli di prima della morte,
                // alla riapertura il confronto direbbe "e' cambiato tutto" e partirebbero **tre pacchetti
                // stale** proprio mentre `Close()` ne manda uno completo. Allineando qui la memoria, la
                // riapertura riparte pulita e l'unica cosa che parla e' `SendAllLocationData()`.
                // ========================================================================================
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
