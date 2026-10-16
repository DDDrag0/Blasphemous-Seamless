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

        // ============================================================================================
        // ⭐⭐ [CorpsePeer] LA MORTE VIAGGIA UNA VOLTA - il cadavere a terra visto dal peer.
        // ============================================================================================
        //
        // IL DIFETTO, e la sua causa vera (misurata, non dedotta): dal REMOTO il giocatore morto si vede
        // **IN PIEDI E BLOCCATO**, una posa congelata. Non e' un problema di posa: e' un problema di
        // **CANALE**. Da morti `MainPlayerManager.Update` esce prima di spedire (`PositionSendPaused`,
        // righe sotto) e non manda PIU' NULLA - nemmeno l'animazione di morte. Quindi il peer resta
        // sull'ULTIMA posa ricevuta, che per definizione e' quella **di prima** della morte: in piedi.
        //
        // ⭐ E IL CONGELAMENTO NON E' UN ERRORE, e' voluto: `OtherPlayerScript.CurrentAnimation` usa
        // l'ARRIVO DI UN'ANIMAZIONE come PROVA DI VITA (`if (playerDead) SetDeathStatus(false)`, :778-782),
        // perche' gli bastava quello per rimettere in piedi il peer quando le animazioni tornavano a
        // scorrere. Il guaio e' che la stessa prova di vita scarta anche l'UNICA animazione che serviva:
        // quella di morte. ⇒ Da qui la forma del fix: **si manda la morte** (una volta, alla morte, prima
        // del congelamento) **e si esclude la morte dalla prova di vita**. Vedi `OtherPlayerScript`.
        //
        // ⚠️ E L'ANIMATOR DELL'AVATAR HA GIA' LA POSA: `OtherPlayerScript.Update` riconosce il proprio
        // cadavere con gli stessi stati (`"Death" | "Death Spike" | "Death Fall" | "Grounding Over"`) e il
        // gioco spegne l'Animator del Penitent sulla posa FINALE di "Death"
        // (`PlayerDeathAnimationBehaviour.cs:33-36`). Non si inventa nessuno sprite: si suona quello che
        // c'e'. E' la via chiesta dall'utente ("non abbiamo lo sprite del personaggio morto? ... utilizza
        // quello, cosi' evitiamo di farlo cancellare").
        //
        // ⚠️ NON SI RIAPRE IL CANALE: si manda UN byte di morte UNA volta. La posizione resta congelata e
        // il resto delle animazioni pure - quella decisione (e la sua ragione) non cambia.
        private bool _corpsePeerWasDead;
        private bool _corpsePeerDeathSent;
        private float _corpsePeerArmedAt;
        private bool _corpsePeerWarned;

        /// <summary>
        /// Quanto si aspetta che l'Animator del Penitent ENTRI nello stato di morte prima di usare il
        /// ripiego. E' una finestra corta di proposito: la morte e' un evento, e il gioco la mette in scena
        /// subito (`PenitentOnUpdateDeath_Patch` annuncia nello stesso frame).
        /// </summary>
        private const float CorpsePeerFuseSeconds = 0.5f;

        /// <summary>
        /// [CorpsePeer] LATO MANDANTE — manda l'animazione di morte al peer, UNA volta per morte.
        ///
        /// Chiamato da `Multiplayer.OnLateUpdate` DOPO `DeathCorpseFollow.Tick()`: in quel momento
        /// `PositionSendPaused` e' gia' vero, quindi questo byte e' **l'ultima cosa che parte** prima del
        /// congelamento - ed e' esattamente il punto in cui deve partire.
        ///
        /// ⭐ PERCHE' UNA FINESTRA E NON UNA RIGA SOLA. `CurrentAnimation` legge l'Animator del Penitent
        /// **ora**, e al primo frame di morte quell'Animator puo' non essere ancora entrato in "Death": la
        /// transizione la guida il gioco, e "il morto e' morto" e' noto alla mod un istante prima che lo sia
        /// all'Animator. Spedire alla cieca in quel frame manderebbe l'animazione SBAGLIATA, e il peer
        /// mostrerebbe un cadavere in una posa qualsiasi. Quindi: si ritenta per `CorpsePeerFuseSeconds`, e
        /// **se anche allora l'Animator non e' in uno stato di morte si usa "Death"**, che e' il ripiego
        /// dichiarato - meglio una posa di morte vera su un byte standard che un avatar in piedi.
        /// </summary>
        public void CorpsePeerTick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    // ⭐ IL RIPRISTINO E' LA MORTE CHE FINISCE, e non serve nuovo protocollo: al respawn
                    // `DeathCorpseFollow.Close()` chiama `SendAllLocationData()`, che rimanda posizione,
                    // animazione (0 = Idle) e direzione ⇒ sul peer arriva un'animazione NON di morte, la
                    // prova di vita non e' piu' esclusa, e l'avatar si rialza. Lo stato qui si azzera
                    // perche' la PROSSIMA morte deve poter spedire di nuovo.
                    if (_corpsePeerWasDead)
                    {
                        ModLog.Info($"{CorpsePeerMark} avatar del peer: vivo in piedi " +
                                    $"(la morte e' finita: al respawn la mod rimanda animazione/direzione " +
                                    $"e il peer rialza l'avatar) · animazioneDiMorteMandata=" +
                                    $"{(_corpsePeerDeathSent ? "si" : "no")}");
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

                // Si legge lo stato VERO dell'Animator, e si ricorda se e' una morte: la finestra e' li'
                // per aspettare QUESTO passaggio, non per aspettare a vuoto.
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

                ModLog.Info($"{CorpsePeerMark} animazione di morte mandata: " +
                            $"{(inDeathState ? "stato vero dell'Animator" : "RIPIEGO")}=" +
                            $"{Blasphemous.Multiplayer.Client.Data.AnimationStates.animations[deathAnimation].name}" +
                            $" (byte {deathAnimation}) · atteso={waited:F2}s" +
                            $"{(inDeathState ? "" : " ⚠️ l'Animator del Penitent non e' entrato in uno stato di morte entro " + CorpsePeerFuseSeconds.ToString("F2") + "s")}" +
                            $" · e' l'ULTIMO byte prima del congelamento: da qui in poi non parte piu' nulla" +
                            $" (nemmeno la posizione) finche' il morto non risorge");
            }
            catch (System.Exception e)
            {
                // ⚠️ UN GUASTO QUI NON DEVE FERMARE NIENTE: il peggio e' il difetto noto (l'avatar resta
                // in piedi sul peer), non uno nuovo. Stessa asimmetria dichiarata da `DeathCorpseFollow`.
                if (!_corpsePeerWarned)
                {
                    _corpsePeerWarned = true;
                    ModLog.Error($"{CorpsePeerMark} ERR nel mandante (nessun effetto sul gioco: il peer " +
                                 $"resta con l'ultima posa, il difetto noto): {e.GetType().Name} - {e.Message}");
                }
            }
        }

        /// <summary>Il marker obbligatorio di questa modifica, in un posto solo.</summary>
        internal const string CorpsePeerMark = "[CorpsePeer]";

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
