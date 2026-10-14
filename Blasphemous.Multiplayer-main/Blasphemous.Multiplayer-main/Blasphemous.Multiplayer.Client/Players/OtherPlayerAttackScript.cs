using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client;              // UnityReferences (il ripiego di A6)
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Combat;             // EntityExecution (il ripiego di A6)
using Gameplay.GameControllers.Enemies.Projectiles;
using Gameplay.GameControllers.Entities;
// A5.1 - `EntityAudio`: le etichette dei 14 parametri FMOD del materiale del terreno, i campi che ne
// tengono i valori, e `SetPanning(EventInstance, Vector3)`.
using Gameplay.GameControllers.Entities.Audio;
// A5 - le `public const string` dei suoni del penitent (PenitentFootStep, PenitentAttack1, PenitentDash,
// ...). Il namespace e' `Gameplay.GameControllers.Penitent.Audio`: la classe e'
// `Gameplay.GameControllers.Penitent.Audio.PenitentAudio` e deriva da
// `Gameplay.GameControllers.Entities.Audio.EntityAudio`. NON e' lo stesso namespace di
// `Gameplay.GameControllers.Entities` che e' gia' importato sopra.
using Gameplay.GameControllers.Penitent.Audio;
using System.Collections;
using UnityEngine;
using Framework.Util;                                  // AnimatorEvent (il predicato del fantasma)
using Tools.Level;                                     // Interactable (il campo interactorAnimator)

namespace Blasphemous.Multiplayer.Client.Players
{
    public class OtherPlayerAttackScript : MonoBehaviour
    {
        private const float DEBLA_DURATION = 1.2f;
        private const float VERDIALES_DURATION = 2f;

        // A5 - i due prefab della polvere del dash (DashDustGenerator.startDashDustPrefab e
        // stopDashDustPrefab). Sono `private [SerializeField]` e NON esiste alcun accessore pubblico:
        // l'elenco COMPLETO dei membri pubblici di DashDustGenerator e' StopDustSpawnOffsetPos,
        // GetStartDashDust, i due GetStopDashDust e GetDashDustPosition (verificato nella metadata).
        // Quindi si leggono per RIFLESSIONE, una volta sola e tenuti da parte: GetField e' una ricerca per
        // stringa e questo codice gira sul percorso degli effetti. E' la stessa tecnica che
        // EnemySyncManager usa gia' per i campi privati del gioco.
        //
        // Se la ricerca restituisce null l'effetto degrada a "nessuna polvere" e lo dice, invece di
        // lanciare: questo codice gira DENTRO il gestore dei pacchetti, dove un'eccezione fa ri-presentare
        // lo stesso pacchetto a ogni frame e ferma l'aggiornamento della mod.
        private static readonly System.Reflection.FieldInfo StartDashDustField =
            typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator).GetField(
                "startDashDustPrefab",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static readonly System.Reflection.FieldInfo StopDashDustField =
            typeof(Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator).GetField(
                "stopDashDustPrefab",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private SpriteRenderer SwordRenderer { get; set; }
        private Animator SwordAnim { get; set; }
        private BoxCollider2D DamageArea { get; set; }

        public void SetupAttack()
        {
            // Add components
            transform.localPosition = Vector3.zero;
            SwordRenderer = gameObject.AddComponent<SpriteRenderer>();
            SwordAnim = gameObject.AddComponent<Animator>();
            SwordAnim.runtimeAnimatorController = UnityReferences.PlayerSwordAnimator;

            // Create damage area collider
            gameObject.layer = LayerMask.NameToLayer("Enemy");
            DamageArea = gameObject.AddComponent<BoxCollider2D>();
            DamageArea.offset = new Vector2(0, 0.92f);
            DamageArea.size = new Vector2(0.665f, 1.866f);
            DamageArea.isTrigger = true;
        }

        // Upon death, the hitbox should be disabled
        public void SetHitboxStatus(bool active)
        {
            DamageArea.enabled = active;
        }

        // When receiving an attack from another player, make their character play the sword/prayer animation
        public void PlayEffectAnimation(EffectType effect, bool facingRight)
        {
            PlayEffectAnimation(effect, facingRight, false, Vector2.zero);
        }

        /// <summary>
        /// Replays an effect on the avatar. The position is only read by the effects that happen SOMEWHERE -
        /// the ground impact and the slide's dust (A4) - and hasPosition says whether it means anything.
        /// </summary>
        public void PlayEffectAnimation(EffectType effect, bool facingRight, bool hasPosition, Vector2 position)
        {
            SwordRenderer.flipX = !facingRight;

            // A5 - LA SCADENZA DELLA SCIVOLATA, ed e' l'unico pezzo di A5 che gira su OGNI effetto.
            //
            // PERCHE' QUI. Questo metodo e' gia' chiamato per ogni effetto che arriva, quindi la scadenza
            // non aggiunge ne' un `Update` ne' un `MonoBehaviour`: e' un confronto di float che costa nulla
            // e viene valutato solo mentre `dashSoundInstance` e' valida (cioe' solo durante un dash del
            // peer). Il caso "nessuna scivolata accesa" esce alla prima riga.
            //
            // Se scatta, lo DICE: uno STOP perso e' una cosa che si vuole sapere, non una da nascondere
            // dietro una scadenza silenziosa.
            if (dashSoundInstance.isValid() &&
                Time.unscaledTime - dashSoundStartedAt > DASH_SOUND_TIMEOUT)
            {
                ModLog.Warn("[Sound] la scivolata del peer non ha ricevuto lo STOP entro " +
                            DASH_SOUND_TIMEOUT + "s: la spengo io (pacchetto perso?)");
                StopPeerDashSound();
            }

            // A7 - LO STESSO FUSIBILE PER IL CLANG DELLA PARATA. Un evento SOSTENUTO che non viene mai
            // fermato non "si sente male": RESTA ACCESO PER SEMPRE. Le due cause sono note e non rare - un
            // pacchetto perso e una disconnessione nel mezzo; la seconda la copre OnDestroy, ma solo se
            // l'oggetto viene distrutto.
            //
            // 4 secondi: la finestra di parata arriva al massimo a 1 s, e la parata finisce quando finisce
            // la sua animazione. E' un FUSIBILE, non una durata: se scatta, si e' perso uno STOP.
            if (parryHitInstance.isValid() &&
                Time.unscaledTime - parryHitStartedAt > PARRY_HIT_TIMEOUT)
            {
                ModLog.Warn("[Sound] il clang della parata del peer non ha ricevuto lo STOP entro " +
                            PARRY_HIT_TIMEOUT + "s: lo spengo io (pacchetto perso?)");
                StopPeerParryHitSound();
            }

            switch (effect)
            {
                case EffectType.SidewaysGrounded:
                    SwordAnim.Play("Basic1_Lv1");
                    break;
                case EffectType.UpwardsGrounded:
                    SwordAnim.Play("BasicUpward_Lv1");
                    break;
                case EffectType.SidewaysAir:
                    SwordAnim.Play("Air1_Lv1");
                    break;
                case EffectType.UpwardsAir:
                    SwordAnim.Play("AirUpward_Lv1");
                    break;
                case EffectType.Crouch:
                    SwordAnim.Play("Crouch_Lv1");
                    break;
                //case EffectType.Ranged:
                //    Main.Multiplayer.LogError("Spawning range attack effect!");
                //    break;
                case EffectType.Debla:
                    StartCoroutine(DisplayDebla());
                    break;
                // A3: the ghost trail belongs to the avatar, not to the sword, so this reaches up to it.
                // Both cases share the body because the only difference is the flag passed on.
                case EffectType.TrailOn:
                case EffectType.TrailOff:
                {
                    OtherPlayerScript owner = GetComponentInParent<OtherPlayerScript>();
                    if (owner != null)
                        owner.SetGhostTrail(effect == EffectType.TrailOn);
                    break;
                }
                // A4 - the slide's dust and its sound, at the place the sender slid.
                //
                // hasPosition is CHECKED rather than assumed. A build of this mod from before the position, or
                // a server that still relays a single byte, sends the effect with no place attached - and the
                // origin is a legitimate coordinate in this game, so drawing it there would put the dust at the
                // far end of the level instead of at the player.
                case EffectType.SlideDust:
                {
                    if (hasPosition)
                        PlaySlideDust(position, facingRight);
                    break;
                }
                // A5 - la polvere della SCIVOLATA DI MOVIMENTO (Dash), nel punto in cui il mittente e'
                // scivolato.
                //
                // DUE case e non uno perche' il gioco ha DUE prefab: quello di PARTENZA
                // (startDashDustPrefab, sulla trasform del penitent) e quello di ARRESTO
                // (stopDashDustPrefab, a terra davanti o dietro di loro). Il ricevente non puo' dedurre
                // quale dei due serve, quindi lo dice il byte.
                //
                // hasPosition e' CONTROLLATO e non dato per scontato, come per SlideDust: un byte senza
                // posizione disegnerebbe la polvere nell'origine, che in questo gioco e' una coordinata
                // legittima - cioe' dall'altra parte del livello.
                case EffectType.DashDustStart:
                case EffectType.DashDustStop:
                {
                    if (hasPosition)
                        PlayDashDust(effect == EffectType.DashDustStart, position, facingRight);
                    break;
                }
                // A4 - the ground impact: the dust, the beam and the landing sound. The five members are the
                // five shapes VerticalAttack.InstantiateExplosion can take, and the sender is the machine that
                // decided which one it was (it is the one that saw the fall height and its own skill).
                case EffectType.LandingImpactBase:
                case EffectType.LandingImpact1:
                case EffectType.LandingImpact2:
                case EffectType.LandingImpact3:
                case EffectType.LandingImpact3Beam:
                {
                    if (hasPosition)
                        PlayLandingImpact(effect, position);
                    break;
                }
                // A5 - I SUONI che il peer non sente: i PASSI A TERRA e i FENDENTI A VUOTO.
                //
                // PERCHE' QUESTI SEI STANNO IN UN CASE SOLO E LA SCIVOLATA NO. Questi sei sono one-shot
                // PURI: il suono parte e finisce da solo. La scivolata e' un evento di catalogo SOSTENUTO
                // (LATCHED) e ha bisogno di ricordarsi un'istanza fra un pacchetto e l'altro: sta in un
                // case suo, con lo stato in un campo suo, perche' e' l'unico pezzo di A5 che puo'
                // "restare acceso".
                //
                // PERCHE' IL SUONO NON SI PUO' SUONARE CHIAMANDO IL METODO DEL GIOCO. La strada "sicura"
                // di A4 - chiamare il metodo locale - QUI NON ESISTE: `PenitentAudio.PlayBasicAttack1()`
                // e `PlayFootStep()` passano da `EntityAudio.PlayOneShotEvent`, che prende il PANNELLO
                // dall'`Owner` del componente, cioe' dal PENITENT LOCALE. Chiamarle qui farebbe uscire il
                // fendente del peer DALLA TUA POSIZIONE, che e' esattamente il difetto che A4 ha dovuto
                // correggere per la polvere. E l'avatar non ha comunque un `EntityAudio` su cui
                // chiamarle: `OtherPlayerScript.SetupPlayer` aggiunge solo SpriteRenderer, Animator,
                // OtherPlayerAttackScript e GhostTrailGenerator. E' un GUSCIO.
                //
                // ⭐ E ALLORA COSA SI CHIAMA. `FMODAudioManager.PlayOneShotFromCatalog(string eventKey,
                // Vector3 position)`: l'unico one-shot POSIZIONALE che risolve una CHIAVE DI CATALOGO.
                // Verificato nel suo corpo IL: fa `FindSfxInCatalogs(eventKey)`, prende il `FMODKey` della
                // clip e chiama `EventOneShotPanned(clip.FMODKey, position)` - cioe' passa a FMOD il
                // PERCORSO e calcola il panning con `GetPanningValueByPosition(position)`, che legge SOLO
                // `Camera.main`: non tocca il penitent.
                //
                // ⚠️ `EventOneShotPanned(chiave, pos)` NON si usa: pretende un PERCORSO DI BANCA e con una
                // chiave di catalogo fa LANCiARE FMOD. E' l'errore che ha gia' causato danno-zero + mod
                // congelata, e queste sette sono tutte chiavi di catalogo.
                case EffectType.FootStep:
                case EffectType.Attack1:
                case EffectType.Attack2:
                case EffectType.AirAttack1:
                case EffectType.AirAttack2:
                case EffectType.HeavyAttack:
                case EffectType.ChargedRelease:
                // A5.2 - i suoni confermati dalle probe: il quarto colpo di una combo, lo sguaino, e i due
                // suoni dell'attacco aereo-a-terra (l'inizio e la caduta: il TONFO era gia' coperto da A4).
                case EffectType.ComboHit:
                case EffectType.IdleSword:
                case EffectType.VerticalStart:
                case EffectType.VerticalFalling:
                // A7 - la guardia del peer (suono #2), che `SoundKeyFor` risolve in "PenitentStartParry".
                case EffectType.ParryStart:
                    PlayPeerSound(effect, position);
                    break;
                // A5 - IL SUONO DELLA SCIVOLATA, l'UNICO evento SOSTENUTO (LATCHED) di A5, e l'unico che ha
                // bisogno di DUE casi.
                //
                // PERCHE' DUE CASI E NON UNO. `PenitentDash` non e' un one-shot: resta ACCESO finche'
                // qualcuno non lo spegne - il gioco usa `PlayEvent(ref _dashEventInstance, "PenitentDash")`
                // e poi `StopDashSound()`. Un byte solo non puo' dire "accendi" e "spegni", quindi START e
                // STOP sono due membri dell'enum (95 e 96).
                //
                // ⭐ PERCHE' `PlayEventWithCatalog(ref ...)` E NON `PlayOneShotFromCatalog`. Quest'ultimo
                // chiama `release()` subito dopo `start()`: l'istanza e' liberata mentre l'evento sta ancora
                // suonando, e un evento SOSTENUTO trattato cosi' non si puo' piu' fermare. Firme verificate
                // in IL: `PlayEventWithCatalog(ref EventInstance, string key, Vector3)` e
                // `StopEvent(ref EventInstance)`.
                //
                // ⚠️ E NON SI PASSA DAL `PenitentAudio` LOCALE. `PlayDashSound()` scriverebbe
                // `_dashEventInstance` del PENITENT LOCALE: il dash del peer uscirebbe dalla TUA posizione e -
                // peggio - il TUO dash successivo troverebbe l'istanza occupata e la fermerebbe al posto tuo.
                // L'istanza dev'essere NOSTRA, una per avatar.
                case EffectType.DashSoundStart:
                {
                    if (hasPosition)
                        PlayPeerDashSound(position);
                    break;
                }
                case EffectType.DashSoundStop:
                    // ⚠️ NIENTE guardia su `hasPosition` QUI: lo STOP non porta posizione, e non deve
                    // farne una condizione. Fermare qualcosa che non c'e' non fa nulla.
                    StopPeerDashSound();
                    break;
                // A7 - IL CLANG DELLA PARATA DEL PEER. Due casi, come la scivolata, e per la stessa
                // ragione: `PenitentParryHit` e' un evento SOSTENUTO. Il gioco lo spegne alla fine della
                // parata (`Parry.StopParry` -> `Audio.StopParryFx`); senza il secondo byte il clang
                // resterebbe acceso per sempre.
                //
                // ⚠️ hasPosition e' CONTROLLATO e non dato per scontato: il clang deve uscire DA DOVE E'
                // SUCCESSO. Un byte senza coordinate lo metterebbe nell'origine, che in questo gioco e' una
                // coordinata legittima - cioe' dall'altra parte del livello.
                case EffectType.ParryHit:
                {
                    if (hasPosition)
                        PlayPeerParryHitSound(position);
                    break;
                }
                case EffectType.ParryHitStop:
                    // ⚠️ NIENTE guardia su `hasPosition`: lo STOP non porta posizione e non deve farne una
                    // condizione. Fermare qualcosa che non c'e' non fa nulla.
                    StopPeerParryHitSound();
                    break;
                // A6 - L'ESECUZIONE. Il primo dei due byte ACCENDE l'animazione sull'avatar, il secondo la
                // SPEGNE. Nessuna posizione: l'animazione si suona dove l'avatar e', e dove l'avatar e' lo
                // decide gia' il canale Position, che gira da sempre.
                //
                // ⚠️ E NON SI PASSA DA `hasPosition`: come per lo STOP della scivolata, non e' una
                // condizione - questi due byte non portano coordinate, e pretenderle li scarterebbe tutti.
                //
                // ⚠️ L'AVATAR SI RISALE DAL PADRE, e non si riceve: `PlayEffectAnimation` e' un metodo di
                // questo script, che vive sul GameObject "Sword", FIGLIO dell'avatar. E' la stessa tecnica
                // gia' usata in questo stesso metodo per la scia. Ed e' per questo che lo STATO
                // dell'esecuzione sta su `OtherPlayerScript` e non qui: un avatar per istanza, uno stato per
                // avatar. Uno stato statico condiviso farebbe sovrascrivere a vicenda due esecuzioni - che in
                // co-op sono il caso NORMALE.
                case EffectType.ExecutionStart:
                case EffectType.ExecutionEnd:
                {
                    OtherPlayerScript executionOwner = GetComponentInParent<OtherPlayerScript>();
                    if (executionOwner != null)
                        executionOwner.PlayExecutionAnimation(effect == EffectType.ExecutionStart);
                    break;
                }
                    //case EffectType.Verdiales:
                    //    StartCoroutine(DisplayVerdiales());
                    //    break;
            }
        }

        /// <summary>
        /// La CHIAVE DI CATALOGO del suono che questo `EffectType` rappresenta.
        ///
        /// ⚠️ IL VALORE NON E' SCRITTO QUI E NON VIAGGIA SUL FILO: e' letto dalle `public const string`
        /// di `PenitentAudio`. Non e' pignoleria - e' la ragione per cui il payload e' UN BYTE. Se il
        /// gioco cambiasse una chiave in una build futura, la mod la seguirebbe leggendo la costante,
        /// mentre una stringa trasmessa la congelerebbe a quella vecchia.
        ///
        /// I valori attesi, verificati in IL sui campi `Literal`:
        ///     FootStep       -> "PenitentFootStep"
        ///     Attack1        -> "PenitentAttack1"
        ///     Attack2        -> "PenitentAttack2"
        ///     AirAttack1     -> "PenitentAirAttack1"
        ///     AirAttack2     -> "PenitentAttack2"       <-- vedi sotto, NON e' un refuso
        ///     HeavyAttack    -> "PenitentHeavyAttack"
        ///     ChargedRelease -> "PenitentReleaseChargedAttack"
        ///
        /// ⚠️ `AirAttack2` USA `BasicAttack2EventKey`, E NON `BasicAirAttack2EventKey`. La costante
        /// `PenitentAudio.BasicAirAttack2EventKey` vale "PenitentAirAttack2" e NON E' MAI USATA da nessun
        /// metodo dell'assembly: in `PenitentAudio` non esiste alcun `PlayAirAttack2`, e il metodo del gioco
        /// `PenitentAttackAnimations.PlayAirAttack2()` chiama `PlayBasicAttack2()` - cioe' suona
        /// "PenitentAttack2" (verificato in IL: l'unico `call` nel suo corpo e' `PlayBasicAttack2`).
        ///
        ///   ⇒ La domanda giusta non e' "quale costante descrive meglio il colpo", ma "quale chiave suona
        ///   DAVVERO quando il penitent fa quel colpo". Se qui si mettesse "PenitentAirAttack2", il
        ///   `ResolveCatalogPath` non la troverebbe nel catalogo e il fendente in aria 2 del peer NON
        ///   uscirebbe - con una riga di WARN che accusa il catalogo per una scelta sbagliata fatta qui.
        ///
        /// ⚠️ Questo NON cambia il byte trasmesso: `AirAttack2` (92) resta un membro distinto da `Attack2`
        /// (90) e il mittente resta agganciato a `PenitentAttackAnimations.PlayAirAttack2`, che e' l'evento
        /// giusto. Cambia solo QUALE suono il ricevente riproduce - ed e' lo stesso che ha suonato il
        /// mittente. Se una build futura separasse davvero i due suoni, questa e' la riga da cambiare.
        /// </summary>
        private static string SoundKeyFor(EffectType effect)
        {
            switch (effect)
            {
                case EffectType.FootStep: return PenitentAudio.FootStepEventKey;
                case EffectType.Attack1: return PenitentAudio.BasicAttack1EventKey;
                case EffectType.Attack2: return PenitentAudio.BasicAttack2EventKey;
                case EffectType.AirAttack1: return PenitentAudio.BasicAirAttack1EventKey;
                case EffectType.AirAttack2: return PenitentAudio.BasicAttack2EventKey;
                case EffectType.HeavyAttack: return PenitentAudio.PenitentHeavyAttackEventKey;
                case EffectType.ChargedRelease: return PenitentAudio.ReleaseChargedAttackEventKey;
                case EffectType.ComboHit: return PenitentAudio.ComboHit;                  // "PenitentComboHit"
                case EffectType.IdleSword: return PenitentAudio.IdleModeSword;            // "PenitentIdleSword"
                case EffectType.VerticalStart: return PenitentAudio.VerticalAttackStart;  // "PenitentVerticalAttackStart"
                case EffectType.VerticalFalling: return PenitentAudio.VerticalAttackFalling;
                // A7 - LA GUARDIA: chiave letta dalla `public const string` di `PenitentAudio`.
                // ⚠️ La categoria NON e' Motion: `PlayStartParry` fa `PlaySfxOnCatalog`, che non imposta un
                default: return null;
            }
        }

        /// <summary>
        /// La spia: la chiave esiste in uno dei cataloghi che QUESTA macchina ha caricato?
        ///
        /// PERCHE' ESISTE, e perche' non e' un lusso. `PlayOneShotFromCatalog` tace quando non trova la
        /// chiave: non lancia, non logga, non fa NULLA. Un suono che non si sente perche' la chiave non
        /// c'e' e un suono che non si sente perche' il pacchetto non e' arrivato sarebbero di nuovo
        /// INDISTINGUIBILI - e distinguerli e' meta' del lavoro fatto finora.
        ///
        /// `FMODAudioCatalog.GetSfx(string id)` e' PUBLIC e fa `_sfxDictionary.TryGetValue`. `_audioCatalogs`
        /// e' PRIVATO, quindi la lettura passa dalla riflessione: la stessa tecnica che questo file usa gia'
        /// per i due prefab di `DashDustGenerator`, che sono `private [SerializeField]` senza accessore.
        ///
        /// ⚠️ Si legge UNA VOLTA per chiave e si tiene da parte: e' una ricerca per stringa, e questo codice
        /// gira sul percorso degli effetti. `null` in cache significa "non ancora deciso", la lista vuota
        /// "deciso: non c'e'".
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> SoundPathCache =
            new System.Collections.Generic.Dictionary<string, string>();

        private static readonly System.Reflection.FieldInfo AudioCatalogsField =
            typeof(Framework.Managers.FMODAudioManager).GetField(
                "_audioCatalogs",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// Il PERCORSO DI BANCA della chiave, se questa macchina ce l'ha; `null` se la chiave non esiste
        /// (e in quel caso lo DICE, una volta per chiave). Vedi `PlayPeerSound` per l'uso.
        /// </summary>
        private static string ResolveCatalogPath(string key)
        {
            // ⚠️ IL VALORE IN CACHE PUO' ESSERE LEGITTIMAMENTE `null`, quindi non basta `TryGetValue`:
            // quando `null` significa "gia' deciso: non c'e'", restituirlo e basta. Senza la seconda riga
            // il fallimento verrebbe riprovato - e RI-LOGGATO - a ogni singolo passo del peer, che e' il
            // modo piu' rapido di rendere inutile la riga che serve a diagnosticare il guasto.
            string cached;
            if (SoundPathCache.TryGetValue(key, out cached))
                return cached;

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return null;

            // ⚠️ `AudioCatalogsField` puo' essere null se una build futura rinomina il campo: in quel caso
            // si degrada a "nessuna spia" e NON si blocca il suono. La spia e' un di piu', non una guardia.
            Framework.Audio.FMODAudioCatalog[] catalogs = null;
            if (AudioCatalogsField != null)
                catalogs = AudioCatalogsField.GetValue(audio) as Framework.Audio.FMODAudioCatalog[];

            string path = null;
            if (catalogs != null)
            {
                for (int i = 0; i < catalogs.Length; i++)
                {
                    if (catalogs[i] == null)
                        continue;

                    Framework.Audio.FMODAudioCatalog.FMODIndexedClip clip = catalogs[i].GetSfx(key);
                    if (clip == null)
                        continue;

                    // ⚠️ SI COPIA IL VALORE COSI' COM'E', senza "normalizzarlo". E' il PERCORSO che il
                    // gioco passa a `RuntimeManager.CreateInstance` quando suona lo stesso suono: la
                    // costante `[EventRef]` scritta a mano che a volte porta uno slash davanti e' un
                    // dettaglio dell'inspector, e nulla nel percorso del gioco lo toglie - quindi toglierlo
                    // qui sarebbe una differenza inventata, cioe' esattamente il modo in cui un suono
                    // "quasi giusto" smette di suonare senza che nessuno possa dire perche'.
                    path = clip.FMODKey;
                    break;
                }
            }

            if (path == null)
            {
                // ⚠️ Questa riga NON e' rumore: senza di essa la chiave assente e il pacchetto mancato
                // restano identici nei log. Il fallimento viene memorizzato (`null` in cache), quindi la
                // riga esce UNA VOLTA per chiave e non a ogni passo.
                ModLog.Warn($"[Sound] la chiave di catalogo \"{key}\" non esiste su questa macchina: " +
                            "il suono del peer NON uscira' (e non e' un problema di rete)");
                SoundPathCache[key] = null;
                return null;
            }

            ModLog.Info($"[Sound] chiave \"{key}\" -> percorso \"{path}\"");
            SoundPathCache[key] = path;
            return path;
        }

        // ============================================================================================
        // A5.1 - IL PASSO: PERCHE' NON BASTAVA `EventOneShotPanned`, E COSA SI FA INVECE.
        // ============================================================================================
        //
        // IL DIFETTO, IN UNA RIGA DI IL. Il gioco suona il passo con
        //     EntityAudio.PlayOneShotEvent("PenitentFootStep", FxSoundCategory.Motion)
        // e quella categoria e' L'UNICA che chiama `SetFloorMaterialParams`, che imposta QUATTORDICI
        // parametri FMOD (Dirt, Water, Snow, Stone, Wood, Marble, Flesh, Metal, Mud, SecretFloor, Grass,
        // Demake, Palio, Snake). Verificato nel corpo IL di `PenitentAudio.PlayFootStep`: @IL_0007 e' un
        // `call EntityAudio::PlayOneShotEvent`, mentre `PlayBasicAttack1/2`, `PlayHeavyAttack`,
        // `PlayAirAttack1/2` e `PlayReleaseChargedAttack` fanno @IL_0022 `callvirt PlaySfxOnCatalog(String)`,
        // che NON imposta un solo parametro. => I FENDENTI SI SENTONO PERCHE' NON DIPENDONO DA NULLA;
        // IL PASSO E' MUTO PERCHE' DIPENDE DAI MATERIALI, E NOI NON NE IMPOSTAVAMO NESSUNO.
        //
        // ⚠️ NON BASTAVA `PlayOneShotFromCatalog`, ed e' un punto importante perche' era la scorciatoia
        // "ovvia": quella chiama `EventOneShotPanned`, che nel suo corpo IL imposta SOLO `Spanish` e
        // `Panning`. E' la stessa strada che avevamo, con un nome piu' corto.
        //
        // LA STRADA GIUSTA E' RIPRODURRE LA CATENA LOCALE, che ha TRE passi e non uno:
        //     1. `CreateCatalogEvent(chiave, pos)`  -> risolve la chiave, applica set3DAttributes e Spanish;
        //     2. i MATERIALI + il PANNING, PRIMA di start();
        //     3. `start()` e `release()`.
        // Il punto 2 e' l'unico che mancava. `release()` subito dopo `start()` e' esattamente cio' che fa
        // anche il gioco in `PlayOneShotEvent`: non e' una differenza introdotta qui.
        //
        // ⭐ E IL PANNING SI FA CON `EntityAudio.SetPanning(EventInstance, Vector3)`, che e' PUBLIC STATIC.
        // Non esisteva nel piano precedente, e risparmia di scrivere a mano il getParameter("Panning"): fa
        // esattamente la stessa cosa con la stessa formula.
        //
        // ⚠️ PERCHE' NON SI CHIAMA `EntityAudio.SetFloorMaterialParams`, CHE ESISTE E FA PROPRIO QUESTO.
        // Perche' e' PRIVATO. L'alternativa era la riflessione su un metodo privato: si e' preferito
        // scrivere i 14 `getParameter` per esteso, che sono piu' righe ma non dipendono da un nome privato
        // che una build futura puo' rinominare.
        //
        // ⚠️ E I 14 VALORI DA DOVE VENGONO. Nel gioco vengono dai campi `DirtValue`, `SnowValue`, ... di
        // `EntityAudio`, aggiornati da `PenitentAudio.FloorSensorEmitterOnEnter`/`OnStay` leggendo il TAG
        // del collider su cui il penitent sta camminando (`Material:Dirt`, `Material:Stone`, ...).
        // Quei campi sono `Family` (= protected), quindi NON si leggono da fuori: si leggono per
        // RIFLESSIONE dal penitent LOCALE.
        //   E' la scelta giusta e non un ripiego: in co-op i due giocatori stanno quasi sempre sullo stesso
        // terreno, quindi il materiale locale E' il materiale giusto. E quando non lo fosse, il passo esce
        // col materiale SBAGLIATO invece che MUTO - che e' il difetto che stiamo togliendo.
        //   Se la riflessione fallisce (build futura che rinomina i campi) si usano i DEFAULT del gioco e
        // il passo esce come oggi: DEGRADA, NON BLOCCA. La spia lo dice una volta sola.

        /// <summary>
        /// I nomi dei parametri FMOD del materiale del terreno, NELL'ORDINE E NELLA GRAFIA del corpo IL di
        /// `EntityAudio.SetFloorMaterialParams`. La grafia conta: `getParameter` cerca per NOME, e
        /// `"SecretFloor"` NON e' `"Secret"` (il campo si chiama `SecretValue` e il label e' diverso).
        /// </summary>
        private static readonly string[] FloorMaterialParams =
        {
            EntityAudio.LabelParamDirt,        // "Dirt"
            EntityAudio.LabelParamWater,       // "Water"
            EntityAudio.LabelParamSnow,        // "Snow"
            EntityAudio.LabelParamStone,       // "Stone"
            EntityAudio.LabelParamWood,        // "Wood"
            EntityAudio.LabelParamMarble,      // "Marble"
            EntityAudio.LabelParamFlesh,       // "Flesh"
            EntityAudio.LabelParamMetal,       // "Metal"
            EntityAudio.LabelParamMud,         // "Mud"
            EntityAudio.LabelParamSecret,      // "SecretFloor"
            EntityAudio.LabelParamGrass,       // "Grass"
            EntityAudio.LabelParamDemake,      // "Demake"
            EntityAudio.LabelParamPalio,       // "Palio"
            EntityAudio.LabelParamSnake,       // "Snake"
        };

        /// <summary>
        /// I campi `protected float` di `EntityAudio` che tengono i valori, NELLO STESSO ORDINE di
        /// `FloorMaterialParams`. Sono `Family`, quindi si leggono per riflessione. Si risolvono UNA VOLTA:
        /// `FieldInfo` risolti a ogni passo sarebbero 14 `GetField` per suono.
        /// </summary>
        private static readonly System.Reflection.FieldInfo[] FloorMaterialFields = ResolveFloorMaterialFields();

        private static System.Reflection.FieldInfo[] ResolveFloorMaterialFields()
        {
            string[] names =
            {
                "DirtValue", "WaterValue", "SnowValue", "StoneValue", "WoodValue", "MarbleValue",
                "FleshValue", "MetalValue", "MudValue", "SecretValue", "GrassValue", "DemakeValue",
                "PalioValue", "SnakeValue",
            };

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            System.Reflection.FieldInfo[] fields = new System.Reflection.FieldInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
                fields[i] = typeof(EntityAudio).GetField(names[i], flags);

            return fields;
        }

        /// <summary>
        /// Una volta sola, la PRIMA volta che si prova a suonare un passo: la riflessione ha trovato i 14
        /// campi, o no? Si logga in ogni caso, e in caso di fallimento si dice anche cosa succede (i
        /// materiali restano ai default del gioco). Una riga per sessione, non una per passo.
        /// </summary>
        private static bool floorMaterialReflectionChecked;

        /// <summary>
        /// Imposta i 14 parametri del materiale del terreno sull'istanza, copiandoli dal penitent LOCALE.
        /// Se la riflessione non e' disponibile NON imposta nulla e NON e' un errore: l'istanza resta coi
        /// default del banco, che e' esattamente lo stato in cui si trova oggi.
        /// </summary>
        private static void ApplyLocalFloorMaterialParams(FMOD.Studio.EventInstance instance)
        {
            if (!floorMaterialReflectionChecked)
            {
                floorMaterialReflectionChecked = true;

                bool ok = FloorMaterialFields.Length > 0 && FloorMaterialFields[0] != null;
                ModLog.Info("[Sound] materiale del terreno per i passi del peer: " +
                            (ok
                                ? "letto dal penitent locale (14 parametri)"
                                : "NON DISPONIBILE (la riflessione non trova i campi di EntityAudio): " +
                                  "il passo esce coi materiali di default del banco"));
            }

            // ⚠️ `Core.Logic.Penitent` (o il suo Audio) puo' mancare in cambio scena: in quel caso si esce
            // senza toccare nulla. Non si mette una guardia sul `null` del penitent PIU' SOPRA perche' il
            // passo deve poter arrivare anche quando il penitent locale non c'e' piu'.
            Gameplay.GameControllers.Penitent.Penitent penitent = Core.Logic.Penitent;
            if (penitent == null || penitent.Audio == null)
                return;

            EntityAudio localAudio = penitent.Audio;

            for (int i = 0; i < FloorMaterialParams.Length; i++)
            {
                System.Reflection.FieldInfo field = FloorMaterialFields[i];
                if (field == null)
                    continue;

                FMOD.Studio.ParameterInstance parameter;
                instance.getParameter(FloorMaterialParams[i], out parameter);

                // ⚠️ `isValid()` NON e' una formalita': `getParameter` restituisce un'istanza NON valida
                // quando l'evento non HA quel parametro, e chiamare `setValue` su un'istanza non valida e'
                // il modo di far lanciare FMOD dentro il percorso degli effetti. Si imposta solo se c'e'.
                if (parameter.isValid())
                    parameter.setValue((float)field.GetValue(localAudio));
            }
        }

        /// <summary>
        /// Il PASSO del peer, riprodotto dalla CATENA LOCALE e non da `EventOneShotPanned`.
        ///
        /// PERCHE' UN METODO SUO E NON UN `case` IN PIU' IN `PlayPeerSound`: perche' e' l'unico dei sette
        /// suoni che ha bisogno dei parametri di materiale. `PlayPeerSound` resta per i sei fendenti, che
        /// sono chiavi pure e non hanno un solo parametro da impostare - e' la stessa distinzione che il
        /// piano fa fra i passi (con fusibile) e i fendenti (senza), per la stessa ragione: chi ha una
        /// proprieta' in piu' ha un posto suo, altrimenti quella proprieta' si perde nel codice condiviso.
        /// </summary>
        private static void PlayPeerMotionSound(string key, Vector2 position)
        {

            // La spia (una volta per chiave): dice se la chiave non esiste su QUESTA macchina, che e' una
            // cosa diversa da "il pacchetto non e' arrivato". Si usa SOLO come guardia diagnostica.
            //
            // ⚠️⚠️ E IL SUO VALORE DI RITORNO NON SI PASSA A `CreateCatalogEvent`. Quest'ultima NON prende
            // un PERCORSO: prende una CHIAVE DI CATALOGO, e dentro fa `FindSfxInCatalogs(key)` e poi
            // `RuntimeManager.CreateInstance(clip.FMODKey)`. Verificato nel corpo IL.
            //   ⇒ passarle `event:/SFX/Penitent/Motion/PenitentFootStep` la farebbe cercare NEL CATALOGO
            //   come se fosse un Id: `FindSfxInCatalogs` non troverebbe nulla, `FMODKey` sarebbe null,
            //   `RuntimeManager.CreateInstance(null)` lancerebbe `ArgumentNullException` (o tornerebbe
            //   un'istanza non valida), e il passo resterebbe MUTO **con una riga di log che dice che va
            //   tutto bene**. E' l'errore piu' facile da commettere in questo fix: la variabile si chiama
            //   `path` e sembra la cosa da passare.
            string path = ResolveCatalogPath(key);
            if (string.IsNullOrEmpty(path))
                return;

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            UnityEngine.Vector3 world = EffectWorldPosition(position);

            // 1. come il gioco: `CreateCatalogEvent(CHIAVE, pos)` risolve la chiave, applica set3DAttributes
            //    e `Spanish` (UpdateEventInstanceParam). ⚠️ `key`, NON `path`.
            FMOD.Studio.EventInstance instance = audio.CreateCatalogEvent(key, world);

            // ⚠️ `CreateCatalogEvent` NON dice niente quando la chiave non e' nel catalogo: restituisce
            // un'istanza NON valida. `ResolveCatalogPath` l'ha gia' detto (una volta per chiave), quindi
            // qui basta uscire senza chiamare `start` su un'istanza morta.
            if (!instance.isValid())
                return;

            // 2. i MATERIALI DEL TERRENO, e il PANNING dalla posizione del PEER (non del penitent locale:
            //    `SetPanning(EventInstance, Vector3)` e' la variante che prende la posizione, ed e'
            //    public static - l'altra, `SetPanning(EventInstance)`, leggerebbe `Owner.transform.position`,
            //    cioe' la posizione del SUONATORE LOCALE, e il passo uscirebbe dal lato sbagliato).
            ApplyLocalFloorMaterialParams(instance);
            EntityAudio.SetPanning(instance, world);

            // 3. come il gioco, e nello stesso ordine: `start()` e poi `release()`. Il `release()` subito
            //    dopo `start()` e' quello che fa anche `EntityAudio.PlayOneShotEvent`, riga per riga.
            instance.start();
            instance.release();
        }

        /// <summary>
        /// Suona uno dei sei SUONI non sostenuti di A5 nel punto in cui e' successo.
        ///
        /// ⚠️ PERCHE' NON `PlayOneShotFromCatalog`, che sarebbe la riga piu' corta.
        /// Perche' quel metodo NON dice niente quando la chiave non c'e'. Con `ResolveCatalogPath` +
        /// `EventOneShotPanned` si ottiene la stessa identica cosa - percorso risolto nel catalogo, panning
        /// dalla posizione - con in piu' la riga che distingue "chiave assente" da "pacchetto mancato".
        /// `EventOneShotPanned` prende il PERCORSO, che e' esattamente quello che `ResolveCatalogPath`
        /// restituisce: nessun rischio di passargli una chiave.
        /// </summary>
        /// <summary>
        /// L'istanza dell'evento SOSTENUTO della scivolata, una per avatar.
        ///
        /// ⚠️ PERCHE' UN CAMPO E NON UNA VARIABILE LOCALE. L'ACCENSIONE e lo SPEGNIMENTO arrivano in due
        /// pacchetti DIVERSI, a distanza di un dash: fra i due non c'e' alcuna pila che li tenga insieme.
        /// L'istanza e' l'unico filo che li lega, e per questo vive qui.
        ///
        /// ⚠️ È IL PRIMO PEZZO DI A5 CON UNO STATO, e va guardato come tale: se qualcosa di A5 puo'
        /// "restare acceso", e' questo. Le tre vie di uscita sono: lo STOP (il caso normale), OnDestroy
        /// (la disconnessione), e la SCADENZA (il caso in cui lo STOP non arriva mai).
        /// </summary>
        private FMOD.Studio.EventInstance dashSoundInstance;

        /// <summary>
        /// Da quando l'istanza della scivolata sta suonando, in `Time.unscaledTime`. Serve alla scadenza.
        /// </summary>
        private float dashSoundStartedAt;

        /// <summary>
        /// Oltre questo tempo la scivolata si spegne da sola, e lo DICE.
        ///
        /// PERCHE' UNA SCADENZA E NON SOLO LO STOP. Lo STOP arriva da `Dash.OnCastEnd` sull'altra macchina
        /// e in condizioni normali c'e' sempre. Ma un evento sostenuto che non viene mai fermato non si
        /// "sente male": RESTA ACCESO PER SEMPRE, e l'unico modo di farlo smettere e' riavviare il gioco.
        /// Le due condizioni che lo producono sono note e non rare: un pacchetto perso, e una
        /// disconnessione nel mezzo di un dash. La prima non si puo' escludere, la seconda la copre
        /// OnDestroy - ma solo se l'oggetto viene distrutto, e una disconnessione con la scena che si
        /// ricarica non e' l'unico percorso.
        ///
        /// 5 secondi perche' il dash piu' lungo del gioco non arriva a un terzo di questo: e' un fusibile,
        /// non una durata. Se scatta, il log lo dice - e vuol dire che si e' perso uno STOP, non che il
        /// dash e' durato tanto.
        /// </summary>
        private const float DASH_SOUND_TIMEOUT = 5f;

        /// <summary>
        /// A7 - L'ISTANZA DEL CLANG DELLA PARATA, una per avatar.
        ///
        /// ⚠️ PERCHE' UN CAMPO E NON UNA VARIABILE LOCALE, esattamente come per la scivolata: lo START e lo
        /// STOP arrivano in DUE pacchetti diversi, a distanza di una parata, e fra i due non c'e' alcuna
        /// pila che li tenga insieme. L'istanza e' l'unico filo che li lega.
        /// </summary>
        private FMOD.Studio.EventInstance parryHitInstance;

        /// <summary>Da quando il clang sta suonando, in `Time.unscaledTime`. Serve al fusibile.</summary>
        private float parryHitStartedAt;

        /// <summary>
        /// Oltre questo tempo il clang si spegne da solo, e lo DICE. Stesso fusibile della scivolata, stessa
        /// ragione: un evento sostenuto senza STOP resta acceso per sempre.
        /// </summary>
        private const float PARRY_HIT_TIMEOUT = 4f;

        /// <summary>
        /// Accende il suono della scivolata del peer nel punto in cui e' scivolato.
        ///
        /// La sequenza e' quella del gioco, non una inventata: `PenitentAudio.PlayDashSound()` fa
        /// `StopDashSound(); PlayEvent(ref _dashEventInstance, "PenitentDash");` - PRIMA spegne, POI
        /// accende. Serve per il dash concatenato (due dash di fila: il secondo accende mentre il primo e'
        /// ancora acceso), e replicarla e' l'unico modo di non trovarsi due istanze vive.
        /// </summary>
        private void PlayPeerDashSound(Vector2 position)
        {
            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            string key = PenitentAudio.DashEventKey;   // "PenitentDash", const verificata in IL

            // ⚠️⚠️ LA SPIA SI CONSULTA, MA IL SUO VALORE DI RITORNO NON SI PASSA A `PlayEventWithCatalog`.
            // Quest'ultima prende una CHIAVE DI CATALOGO: dentro fa `CreateCatalogEvent(eventKey, position)`,
            // che a sua volta fa `FindSfxInCatalogs(key)` e `RuntimeManager.CreateInstance(clip.FMODKey)`.
            // Passandole il PERCORSO la ricerca fallirebbe, `FMODKey` sarebbe null, e la scivola resterebbe
            // MUTA **con un log che dice che va tutto bene**.
            //   ⚠️ E' la stessa trappola dei passi, ed e' per questo che la riga e' scritta cosi': il metodo
            //   si chiama "WithCatalog" e sembra volere il percorso risolto, mentre vuole l'Id.
            if (string.IsNullOrEmpty(ResolveCatalogPath(key)))
                return;

            // Come il gioco: prima si spegne quello che c'e'.
            StopPeerDashSound();

            audio.PlayEventWithCatalog(
                ref dashSoundInstance,
                key,
                EffectWorldPosition(position));

            // ⚠️ Il controllo NON e' decorativo: se la chiave non e' nel catalogo, `PlayEventWithCatalog`
            // lascia l'istanza NON VALIDA e non lo dice. Registrare l'ora su un'istanza invalida farebbe
            // scattare la scadenza su un suono che non c'e' mai stato, con una riga di avviso che accusa
            // la rete per un problema di catalogo.
            if (dashSoundInstance.isValid())
                dashSoundStartedAt = Time.unscaledTime;
        }

        /// <summary>
        /// Spegne il suono della scivolata. SICURA DA CHIAMARE SEMPRE, anche a vuoto: `StopEvent` del gioco
        /// comincia con `if (eventInstance.isValid())`, e un'istanza mai accesa non e' valida.
        ///
        /// ⚠️ `clearHandle()` e' dentro `StopEvent` (verificato), e senza di essa l'istanza resterebbe
        /// "valida" dopo `release()`: la scadenza la troverebbe valida e la fermerebbe una seconda volta.
        /// Un `EventInstance` e' uno STRUCT, quindi azzerarlo e' comunque l'unico stato sicuro in cui
        /// lasciarlo.
        /// </summary>
        private void StopPeerDashSound()
        {
            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
            {
                dashSoundInstance = default(FMOD.Studio.EventInstance);
                return;
            }

            audio.StopEvent(ref dashSoundInstance);
            dashSoundInstance = default(FMOD.Studio.EventInstance);
        }

        /// <summary>
        /// A7 - IL CLANG DELLA PARATA DEL PEER, alla catena del gioco.
        ///
        /// PERCHE' UNA CATENA SUA E NON UN `case` IN PIU' IN `PlayPeerSound`: perche' questo evento e'
        /// SOSTENUTO. `PlayPeerSound` finisce in `EventOneShotPanned`, che fa `start()` e subito `release()`:
        /// su un evento che non si ferma da solo, il `release()` non lo spegne - lo lascia PARTITO e senza
        /// piu' un manico per fermarlo.
        ///
        /// ⚠️⚠️ E LA CHIAVE, NON IL PERCORSO. `PlayEventWithCatalog(ref eI, key, pos)` chiama
        /// `CreateCatalogEvent(key, pos)`, che fa `FindSfxInCatalogs(key)` e poi
        /// `RuntimeManager.CreateInstance(clip.FMODKey)`. Passandole il PERCORSO risolto la ricerca
        /// fallirebbe, `FMODKey` sarebbe null, e il clang resterebbe MUTO **con un log che dice che va tutto
        /// bene**. La variabile qui sotto si chiama `key` proprio per non ripetere l'errore.
        /// </summary>
        private void PlayPeerParryHitSound(Vector2 position)
        {
            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            string key = PenitentAudio.ParryHitEventKey;   // "PenitentParryHit"

            // La spia di A5: dice se la chiave non esiste su QUESTA macchina. Si consulta - e il suo valore
            // di ritorno NON si passa a `PlayEventWithCatalog`, che vuole l'Id e non il percorso.
            if (string.IsNullOrEmpty(ResolveCatalogPath(key)))
                return;

            // Come il gioco: PRIMA si spegne quello che c'e', POI si accende.
            StopPeerParryHitSound();

            // ⚠️ LA POSIZIONE E' UNA SCELTA DICHIARATA, NON UNA COPIA DEL GIOCO. `EntityAudio.PlayEvent`
            // chiama `CreateCatalogEvent(eventKey)` SENZA posizione, quindi il gioco crea questo evento
            // nell'origine. Passare la posizione del PEER lo spazializza dove la parata e' avvenuta, che e'
            // lo scopo del fix. Se all'orecchio suonasse sbagliato, per essere fedeli al gioco basta passare
            // `Vector3.zero`: e' l'unica riga da cambiare.
            // ⚠️⚠️ PERCHE' NON `PlayEventWithCatalog`. Il suo IL e' 18 istruzioni e comincia cosi':
            //     if (eventInstance.isValid()) return;    // <-- esce SENZA creare, SENZA avviare, SENZA loggare
            // E' un LATCH, non uno `start()`: il suono parte solo la PRIMA volta che il campo e' invalido.
            // E il campo non si invalida da solo, perche' ne' `PlayEventWithCatalog` ne' `release()` toccano
            // `handle` (`release()` fa solo `ldfld handle; call Release; ret`): l'unico che lo azzera e'
            // `clearHandle()`, che sta dentro `FMODAudioManager.StopEvent`.
            //   `StopPeerParryHitSound()` lo chiama gia' - ma la guardia va tolta lo stesso, perche' questa
            //   riga deve creare SEMPRE un'istanza nuova, come fa il gioco:
            //       EntityAudio.PlayEvent -> CreateCatalogEvent(eventKey) -> start()
            Vector3 at = EffectWorldPosition(position);

            // ⚠️ UNA SOLA `CreateCatalogEvent`, E IL CAMPO CI FINISCE DENTRO SUBITO. `StopPeerParryHitSound()`
            // ha gia' fermato e azzerato l'istanza precedente: sovrascrivere il campo con un'istanza NUOVA e'
            // l'unico modo di non lasciare acceso un evento senza piu' un manico per fermarlo. Crearne due -
            // una per "testare" e una per "accendere" - lascerebbe la prima accesa per sempre.
            parryHitInstance = audio.CreateCatalogEvent(key, at);

            if (parryHitInstance.isValid())
            {
                parryHitInstance.start();
                parryHitStartedAt = Time.unscaledTime;
                ModLog.Info($"[Sound RX] clang acceso ({at.x:F1},{at.y:F1})");
            }
            else
            {
                // ⚠️ OSSERVABILITA': senza questa riga "il clang e' partito e non si sente" e "il clang e' stato
                // SCARTATO dalla guardia" restano INDISTINGUIBILI, perche' `PlayEventWithCatalog` tace.
                ModLog.Warn("[Sound RX] il clang NON e' partito: `CreateCatalogEvent(\"" + key +
                            "\")` ha restituito un'istanza NON valida (chiave/catalogo, non rete)");
            }

            if (parryHitInstance.isValid())
                parryHitStartedAt = Time.unscaledTime;
        }

        /// <summary>
        /// Spegne il clang. SICURA DA CHIAMARE SEMPRE, anche a vuoto: `StopEvent` comincia con
        /// `if (eventInstance.isValid())`, e un'istanza mai accesa non e' valida. E `clearHandle()` e' dentro
        /// `StopEvent`, quindi l'istanza non resta "valida" dopo il `release()`.
        /// </summary>
        private void StopPeerParryHitSound()
        {
            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
            {
                parryHitInstance = default(FMOD.Studio.EventInstance);
                return;
            }

            audio.StopEvent(ref parryHitInstance);
            parryHitInstance = default(FMOD.Studio.EventInstance);
        }

        /// <summary>
        /// LA PULIZIA SU DISCONNESSIONE, ed e' la ragione per cui questa riga esiste.
        ///
        /// ⚠️ COSA SUCCEDE SE IL PEER SI DISCONNETTE MENTRE LA SCIVOLATA E' ACCESA. L'istanza dell'evento
        /// NON appartiene all'avatar: e' registrata dentro FMOD, e distruggere un GameObject non ferma
        /// nulla di cio' che FMOD sta suonando. Senza questa riga il `PenitentDash` del peer resterebbe
        /// acceso per sempre, e nessun pacchetto successivo potrebbe piu' spegnerlo - perche' il suo
        /// mittente non c'e' piu'.
        ///
        /// ⚠️ E' UN PUNTO DI PULIZIA GARANTITO, e non per caso: `OtherPlayerManager.RemoveAllActivePlayers`
        /// e `RemoveActivePlayer` distruggono il GameObject in OGNI percorso - `Multiplayer.OnDisconnect`,
        /// il cambio scena (`LevelLoaded`), e il `ReceiveLeaveScene`. `OnDestroy` li copre tutti.
        ///
        /// La scadenza in `PlayEffectAnimation` copre il caso che `OnDestroy` NON copre: un pacchetto di
        /// STOP perso mentre la connessione e' ancora viva.
        /// </summary>
        private void OnDestroy()
        {
            StopPeerDashSound();
            // A7 - E LA STESSA PULIZIA PER IL CLANG. La ragione e' quella della scivolata, parola per parola:
            // l'istanza NON appartiene all'avatar, e' registrata dentro FMOD, e distruggere un GameObject non
            // ferma nulla di cio' che FMOD sta suonando.
            StopPeerParryHitSound();
        }

        /// ⚠️ IL DIFETTO DELLA VERSIONE PRECEDENTE NON ERA "IL PRIMO E' SBAGLIATO": era che il controller che
        /// recita l'esecuzione non e' raggiungibile chiedendo `HasState(0,"USED")` agli `<Animator>` del
        /// prefab. Il log lo ha dimostrato su TUTTI (non solo sul primo): nessuno risponde `true`.
        ///
        /// ⭐ LA SCELTA GIUSTA NON E' UNA SCELTA: e' il campo che il GIOCO usa per recitare.
        ///    `Tools.Level.Interactables.Interactable` dichiara:
        ///        protected Animator interactableAnimator;   // la prop
        ///        protected Animator interactorAnimator;     // il fantasma (il penitent clonato)
        ///    ed e' su `interactorAnimator` che il gioco esegue
        ///        Execution.cs:166   interactorAnimator.SetTrigger("USED");
        ///    Quindi non si indovina fra N Animator: si prende quello che il gioco usa.
        ///    ⚠️ Il valore e' nell'asset, ma la SCELTA no: questo e' l'unico punto che non dipende dall'asset.
        ///
        /// ⚠️ NON SI CERCA `FindObjectOfType<Execution>()`: la prop NON ESISTE finche' qualcuno non esegue
        /// qualcosa, e quando esiste e' a meta' cutscene. Il prefab invece e' sempre raggiungibile dal nemico,
        /// che il ricevente HA GIA' risolto.
        /// </summary>
        public static RuntimeAnimatorController ResolveExecutionController()
        {
            // ⚠️⚠️ LA CACHE NON E' UN'OTTIMIZZAZIONE: E' LA CURA DI UNA CORSA VERA.
            //
            // Questa risoluzione dipende dal NEMICO: `LastExecutedEnemy` e il suo `EntityExecution`. Ma la
            // morte del nemico viaggia PER CONTO SUO, e il ricevente la applica entro ~50 ms - con
            // `SetActive(false)` e, nel caso peggiore, con un `Destroy`.
            //   ⇒ Fra l'arrivo di `ExecutionStart` e l'arrivo della MORTE c'e' una corsa, e se la morte
            //   vince il nemico e' gia' un oggetto morto: `enemy == null` e' VERO per un oggetto Unity
            //   distrutto, e la risoluzione fallirebbe su un'esecuzione che sta andando benissimo.
            //   ⭐ E il controller del fantasma NON cambia mai durante la sessione: e' un asset. Una volta
            //   risolto, resta valido per sempre - quindi la corsa si chiude CHIEDENDOLO UNA VOLTA SOLA.
            if (cachedExecutionController != null)
                return cachedExecutionController;

            Enemy enemy = EnemySyncManager.LastExecutedEnemy;
            if (enemy == null || enemy.gameObject == null)
                return null;

            // `includeInactive: true` perche' il prefab e' quasi certamente inattivo finche' la prop non e'
            // in uso: senza il flag la ricerca non lo troverebbe e il ripiego fallirebbe in silenzio.
            EntityExecution entityExecution = enemy.GetComponentInChildren<EntityExecution>(true);
            if (entityExecution == null || entityExecution.ExecutionPrefab == null)
                return null;

            GameObject prefab = entityExecution.ExecutionPrefab;

            // VIA 1 - il campo del gioco, su TUTTE le Interactable del prefab.
            Interactable[] interactables = prefab.GetComponentsInChildren<Interactable>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                if (interactables[i] == null)
                    continue;

                Animator interactor = InteractorAnimatorField != null
                    ? InteractorAnimatorField.GetValue(interactables[i]) as Animator
                    : null;

                if (interactor == null || interactor.runtimeAnimatorController == null)
                    continue;

                ModLog.Info($"[ExecAnim RX] controller del fantasma dal campo `interactorAnimator` di " +
                            $"{interactables[i].GetType().Name} (prefab: {interactables.Length} Interactable)");
                cachedExecutionController = interactor.runtimeAnimatorController;
                return cachedExecutionController;
            }

            // VIA 2 (ripiego) - l'Animator che porta un `AnimatorEvent`: e' il predicato che
            // `Interactable.CheckAnimationEvents` usa per riconoscere il fantasma. Non e' una scommessa
            // sull'ordine: e' la stessa domanda che si fa il gioco.
            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] == null || animators[i].runtimeAnimatorController == null)
                    continue;
                if (animators[i].GetComponent<AnimatorEvent>() == null)
                    continue;

                ModLog.Info($"[ExecAnim RX] controller del fantasma dall'Animator con AnimatorEvent " +
                            $"(animator {i + 1} di {animators.Length})");
                cachedExecutionController = animators[i].runtimeAnimatorController;
                return cachedExecutionController;
            }

            // VIA 3 - l'ultima spiaggia, tenuta SOLO come rete di sicurezza e DICHIARATA come tale.
            int executionState = Animator.StringToHash("USED");
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] == null || animators[i].runtimeAnimatorController == null)
                    continue;

                if (animators[i].HasState(0, executionState))
                {
                    ModLog.Info($"[ExecAnim RX] controller del fantasma da HasState(\"USED\") " +
                                $"(animator {i + 1} di {animators.Length})");
                    cachedExecutionController = animators[i].runtimeAnimatorController;
                return cachedExecutionController;
                }
            }

            ModLog.Warn($"[ExecAnim RX] nessuna delle {interactables.Length} Interactable e dei " +
                        $"{animators.Length} Animator del prefab espone il fantasma: l'animazione non esiste");
            return null;
        }

        /// ⚠️ `interactorAnimator` e' `protected`: si legge per RIFLESSIONE, come questo file fa gia' per
        /// `_audioCatalogs`, `DashDustGenerator` e `GuardSlide`.
        /// <summary>
        /// Il controller del fantasma, risolto UNA VOLTA e tenuto. Vedi la cache in `ResolveExecutionController`:
        /// e' la cura della corsa con la morte del nemico, non un'ottimizzazione.
        /// </summary>
        private static RuntimeAnimatorController cachedExecutionController;

        private static readonly System.Reflection.FieldInfo InteractorAnimatorField =
            typeof(Tools.Level.Interactable).GetField(
                "interactorAnimator",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// A7 - LA GUARDIA DEL PEER. Il percorso lo legge dal PENITENT LOCALE, perche' il valore vive
        /// nell'asset e non viaggia sul filo: un byte (109) dice "guardia", e ogni macchina risolve il
        /// PROPRIO percorso.
        ///
        /// ⚠️ E SI USA `EventOneShotPanned`, non la catena del motion: questo suono NON passa da
        /// `PlayOneShotEvent`, quindi non ha una `FxSoundCategory` e non imposta i 14 parametri del terreno.
        /// Pretendere quelli (come per i passi) non servirebbe a nulla.
        /// </summary>
        private static void PlayPeerGuardSound(Vector2 position)
        {
            Gameplay.GameControllers.Penitent.Abilities.Parry parry =
                Core.Logic.Penitent == null
                    ? null
                    : Core.Logic.Penitent.GetComponentInChildren<Gameplay.GameControllers.Penitent.Abilities.Parry>(true);

            if (parry == null)
                return;

            string path = parry.ParryGuardFx;
            if (string.IsNullOrEmpty(path))
            {
                ModLog.Warn("[Sound] `Parry.ParryGuardFx` e' vuoto su questa macchina: " +
                            "la guardia del peer non esce (e NON e' un problema di rete)");
                return;
            }

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            // La spia dice una volta sola PERCORSO e origine, cosi' "il pacchetto non e' arrivato" e
            // "il percorso non c'e'" restano distinguibili.
            ModLog.Info($"[Sound] guardia del peer -> percorso \"{path}\"");
            audio.EventOneShotPanned(path, EffectWorldPosition(position));
        }

        private static void PlayPeerSound(EffectType effect, Vector2 position)
        {
            // A5.1 - IL PASSO HA UNA STRADA SUA. E' l'unico dei sette suoni che il gioco suona con
            // `EntityAudio.PlayOneShotEvent(..., FxSoundCategory.Motion)`, e quella categoria imposta i 14
            // parametri del materiale del terreno. `EventOneShotPanned` non ne imposta nessuno, ed e' per
            // questo che il passo arrivava, si risolveva nel catalogo, e NON si sentiva.
            if (effect == EffectType.FootStep)
            {
                PlayPeerMotionSound(PenitentAudio.FootStepEventKey, position);
                return;
            }

            // ⚠️ Anche `IdleSword` e' categoria MOTION: il gioco lo suona con
            // `PlayOneShotEvent("PenitentIdleSword", Motion)`, quindi soffre ESATTAMENTE del guasto dei
            // passi e ha bisogno degli stessi 14 parametri del terreno. Cambia solo la chiave.
            if (effect == EffectType.IdleSword)
            {
                PlayPeerMotionSound(PenitentAudio.IdleModeSword, position);
                return;
            }

            // A7 - LA GUARDIA HA UNA STRADA SUA, e non passa da `SoundKeyFor`.
            //
            // ⚠️ PERCHE'. La chiave che il gioco usa per questo suono NON e' una costante leggibile dal
            // codice: e' `Parry.ParryGuardFx`, un campo `[EventRef]` il cui valore vive nell'ASSET. La
            // costante `PenitentAudio.StartParryEventKey` esiste, ma il metodo che la consuma
            // (`PlayStartParry`) non ha alcun chiamante C# in tutto l'assembly: sarebbe la chiave di un
            // suono che il gioco non suona mai.
            //   ⇒ Si legge il campo dal componente LOCALE (entrambe le macchine hanno il penitent, quindi
            //   la stessa stringa), esattamente come A4 fa per `GuardSlide.GuardSlideSoundFx`.
            if (effect == EffectType.ParryStart)
            {
                PlayPeerGuardSound(position);
                return;
            }

            string key = SoundKeyFor(effect);
            if (string.IsNullOrEmpty(key))
                return;

            string path = ResolveCatalogPath(key);
            if (string.IsNullOrEmpty(path))
                return;

            Framework.Managers.FMODAudioManager audio = Framework.Managers.Core.Audio;
            if (audio == null)
                return;

            // La z arriva dal penitent LOCALE, come per la polvere di A4: vedere EffectWorldPosition.
            audio.EventOneShotPanned(path, EffectWorldPosition(position));
        }

        /// <summary>
        /// The world place an effect that arrived over the network belongs at.
        ///
        /// The payload carries two floats (x, y), because that is all a spot in this game needs, and z is taken
        /// from the LOCAL penitent: ObjectInstance.Reuse applies the z of the Vector3 it is handed, and the
        /// game's own effects are placed at the penitent's position and inherit its z. Rebuilding one at a
        /// different depth than the player's own dust could sort it wrongly. Zero when there is no penitent,
        /// which is the same z the remote avatar itself is drawn at.
        /// </summary>
        private static Vector3 EffectWorldPosition(Vector2 position)
        {
            float z = Core.Logic.Penitent != null ? Core.Logic.Penitent.transform.position.z : 0f;
            return new Vector3(position.x, position.y, z);
        }

        /// <summary>
        /// The slide's impact: the dust puff and the sound (A4).
        ///
        /// Reproduced from the LOCAL GuardSlide, never from a transmitted string. GuardSlideImpactEffect and
        /// GuardSlideSoundFx are prefab/[EventRef] fields whose values live in the asset and are NOT readable
        /// from the code, so the only correct source is the component itself - the same component
        /// GuardSlide.OnCastStart reads on the machine that slid, and the same one that exists on this one.
        ///
        /// What we do NOT reproduce is the particle system: GuardSlide.EnableParticleSystem drives
        /// Core.Logic.Penitent.ParticleSystem, which is the LOCAL player's emitter. Turning that on while the
        /// PEER slides would raise dust on the wrong, motionless character. The avatar has no particle system
        /// of its own, so the slide's dust trail is deliberately not part of this.
        /// </summary>
        private static void PlaySlideDust(Vector2 position, bool facingRight)
        {
            if (Core.Logic.Penitent == null)
                return;

            Gameplay.GameControllers.Penitent.Abilities.GuardSlide slide = Core.Logic.Penitent.GuardSlide;
            if (slide == null)
                return;

            PoolManager pool = PoolManager.Instance;
            if (pool == null)
                return;

            Vector3 world = EffectWorldPosition(position);

            // createPoolIfNeeded stays FALSE and poolSize stays 1, exactly as GuardSlide.OnCastStart calls it:
            // GuardSlide.OnStart already ran CreatePool(GuardSlideImpactEffect, 1) on this machine - both
            // machines have their own penitent - so the pool is there. Passing true would be harmless, but it
            // would be a difference from the game invented here rather than needed.
            //
            // ⚠️ The null check is NOT decorative: PoolManager.ReuseObject starts with prefab.GetInstanceID(),
            // so a null prefab throws inside the pool instead of quietly drawing nothing.
            // ⚠️ Anche qui il prefab si specchia da solo leggendo il penitent LOCALE (GuardSlideImpact
            // ::OnObjectReuse) ⇒ la scivolata di guardia del peer aveva LO STESSO VERSO SBAGLIATO.
            // Il ritorno si cattura per riapplicare la faccia del peer e per non perdere in silenzio il
            // caso "pool assente".
            if (slide.GuardSlideImpactEffect != null)
            {
                PoolManager.ObjectInstance instance =
                    pool.ReuseObject(slide.GuardSlideImpactEffect, world, Quaternion.identity, false, 1);

                if (instance != null)
                    OrientEffect(instance, facingRight, "SlideDust", slide.GuardSlideImpactEffect);
            }

            if (!string.IsNullOrEmpty(slide.GuardSlideSoundFx))
                Core.Audio.EventOneShotPanned(slide.GuardSlideSoundFx, world);
        }

        /// <summary>
        /// La polvere della SCIVOLATA DI MOVIMENTO (Dash), presa dal DashDustGenerator LOCALE.
        ///
        /// ⚠️ NON e' la scivolata qui sopra. GuardSlide e' la scivolata che esce dalla guardia; questa e'
        /// il dash. Sono due componenti diversi con due prefab diversi.
        ///
        /// ⚠️ I DUE PREFAB SONO CAMPI PRIVATI, al contrario del GuardSlideImpactEffect pubblico di
        /// GuardSlide, e non c'e' modo di raggiungerli se non per riflessione: l'elenco COMPLETO dei membri
        /// pubblici di DashDustGenerator e' StopDustSpawnOffsetPos, GetStartDashDust, i due
        /// GetStopDashDust e GetDashDustPosition.
        ///
        /// ⚠️ GetStartDashDust/GetStopDashDust del gioco NON vengono chiamate, per due motivi distinti.
        /// Primo: mettono la polvere sul penitent LOCALE - GetStartDashDust legge _penitent.transform.position
        /// e GetStopDashDust legge GetDashDustPosition(), che legge _penitent.DamageArea e
        /// _penitent.Status - quindi alzerebbero polvere sul TUO personaggio invece che su quello del peer.
        /// Secondo: sono i due metodi che il MITTENTE patcha, quindi chiamarli qui ri-trasmetterebbe.
        /// </summary>
        private static void PlayDashDust(bool isStart, Vector2 position, bool facingRight)
        {
            if (Core.Logic.Penitent == null)
                return;

            Gameplay.GameControllers.Effects.Player.Dash.DashDustGenerator generator =
                Core.Logic.Penitent.DashDustGenerator;
            if (generator == null)
            {
                // Penitent.DashDustGenerator e' riempito da GetComponentInChildren: puo' essere null.
                ModLog.Warn("[DashDust] il penitent locale non ha un DashDustGenerator");
                return;
            }

            PoolManager pool = PoolManager.Instance;
            if (pool == null)
                return;

            System.Reflection.FieldInfo field = isStart ? StartDashDustField : StopDashDustField;
            if (field == null)
            {
                // Il nome vive nei dati serializzati dell'asset: se una build futura rinomina il campo la
                // ricerca restituisce null. Dirlo una volta, invece di lanciare o di non disegnare nulla
                // in silenzio.
                ModLog.Warn("[DashDust] campo prefab non trovato in DashDustGenerator");
                return;
            }

            GameObject prefab = field.GetValue(generator) as GameObject;
            if (prefab == null)
            {
                // ⚠️ Il controllo NON e' decorativo: PoolManager.ReuseObject inizia con
                // prefab.GetInstanceID(), quindi un prefab null lancia DENTRO il pool invece di non
                // disegnare nulla. E il caso e' previsto dal gioco stesso: DashDustGenerator.OnStart
                // guarda entrambi i prefab con `if (prefab)` prima di creare il pool.
                ModLog.Warn("[DashDust] prefab della polvere del dash non assegnato su questa build");
                return;
            }

            // La z arriva dal penitent LOCALE, come in PlaySlideDust: ObjectInstance.Reuse applica la z
            // del Vector3 che riceve, e gli effetti del gioco nascono sulla posizione del penitent e ne
            // ereditano la z. Ricostruirne una diversa potrebbe ordinarli male rispetto alla polvere
            // del giocatore locale.
            Vector3 world = EffectWorldPosition(position);

            // createPoolIfNeeded resta FALSE e poolSize resta 1, come in PlaySlideDust e come nelle
            // chiamate del gioco: l'OnStart del DashDustGenerator di QUESTA macchina ha gia' eseguito
            // CreatePool(prefab, 5) per entrambi i prefab.
            //
            // ⚠️ Il ritorno si CATTURA ora, per due motivi: orientare l'effetto (vedi OrientEffect) e
            // smettere di perdere in silenzio il caso "pool assente", in cui ReuseObject restituisce null
            // senza dire nulla.
            PoolManager.ObjectInstance instance =
                pool.ReuseObject(prefab, world, Quaternion.identity, false, 1);

            if (instance == null)
            {
                ModLog.Warn("[DashDust] ReuseObject non ha restituito l'istanza (pool assente?)");
                return;
            }

            OrientEffect(instance, facingRight, "DashDust", prefab);
        }

        /// <summary>
        /// Re-applies the peer's facing to an effect we just spawned on their behalf.
        ///
        /// ⚠️ THE GAME FLIPS THE PREFAB ITSELF, AND IT FLIPS IT BY LOOKING AT THE WRONG PLAYER.
        ///
        /// The spawn methods pass Quaternion.identity and never touch scale or flip, and PoolObject.OnObjectReuse
        /// - which ObjectInstance.Reuse calls right after setting the position - is where the direction is
        /// actually decided. The only two implementations that read an orientation both go through
        /// Core.Logic.Penitent, the LOCAL player. On the machine that performed the action that is correct,
        /// because the local penitent IS the one who slid; on the machine that merely watches, the peer's dust
        /// mirrors according to YOUR face. That is the whole bug, and it is the same one the ghost trail had.
        ///
        /// The fix costs nothing: AttackManager already hands us the peer's facing. It is re-applied through the
        /// prefab's OWN component so that ITS flags choose the mechanism - SimpleVFX flips with localScale when
        /// useScaleToFlip is set and with flipX otherwise, so doing flipX ourselves on such a prefab would ADD
        /// to the scale flip instead of replacing it.
        /// </summary>
        private static void OrientEffect(
            PoolManager.ObjectInstance instance, bool facingRight, string what, GameObject prefab)
        {
            if (instance == null || instance.GameObject == null)
                return;

            Framework.FrameworkCore.EntityOrientation peer = facingRight
                ? Framework.FrameworkCore.EntityOrientation.Right
                : Framework.FrameworkCore.EntityOrientation.Left;

            Gameplay.GameControllers.Entities.SimpleVFX vfx =
                instance.GameObject.GetComponent<Gameplay.GameControllers.Entities.SimpleVFX>();

            if (vfx != null && vfx.FlipByPlayerOrientation)
            {
                vfx.SetOrientation(peer, vfx.useReversedPlayerOrientation);
                return;
            }

            Gameplay.GameControllers.Effects.Player.GuardSlide.GuardSlideImpact impact =
                instance.GameObject.GetComponent<Gameplay.GameControllers.Effects.Player.GuardSlide.GuardSlideImpact>();

            if (impact != null && impact.SpriteRenderer != null)
            {
                impact.SpriteRenderer.flipX = peer == Framework.FrameworkCore.EntityOrientation.Left;
                return;
            }

            // Neither component: the effect is drawn, but its facing is unknown. Said rather than guessed - a
            // wrong flip is more visible than none, and this line names the prefab so the next session knows
            // which asset to look at.
            ModLog.Warn($"[{what}] prefab senza componente di orientamento: " +
                        (prefab == null ? "n/d" : prefab.name));
        }

        /// <summary>
        /// The ground impact: the dust, the beam and the landing sound (A4).
        ///
        /// This reproduces what VerticalAttack.InstantiateExplosion does - the choice of prefab, the beam and
        /// the sound - WITHOUT calling it, because that method reads the local penitent's position and the
        /// local skill, and here both would be the wrong ones. The prefabs and the three landing sounds come
        /// from the LOCAL VerticalAttack's own serialized fields, for the same reason as the slide: their
        /// values live in the prefab.
        ///
        /// The landing sound is NOT read through VerticalAttack.GetLandingFxEventKey: that property selects on
        /// LastUnlockedSkillId, which is THIS machine's skill. The level travelled in the effect byte instead.
        /// </summary>
        private static void PlayLandingImpact(EffectType effect, Vector2 position)
        {
            if (Core.Logic.Penitent == null)
                return;

            Gameplay.GameControllers.Penitent.Abilities.VerticalAttack vertical = Core.Logic.Penitent.VerticalAttack;
            if (vertical == null)
                return;

            PoolManager pool = PoolManager.Instance;
            if (pool == null)
                return;

            Vector3 world = EffectWorldPosition(position);

            // `isVertical3 && BeamRequiredHeightReached` is the pair InstantiateExplosion branches on, and the
            // sender is the only machine that knows it: it saw the fall height. Hence the two members.
            bool beam = effect == EffectType.LandingImpact3Beam;

            if (beam
                && vertical.VerticalAttackBeamEffectPrefab != null
                && Core.Logic.Penitent.DamageArea != null)
            {
                // The game puts the beam at the TOP of the penitent's damage area. The avatar is somewhere
                // else, so the offset is taken from the local penitent (its top minus its own feet) and added
                // to the place the impact was reported at.
                Vector3 offset =
                    Core.Logic.Penitent.DamageArea.TopCenter - Core.Logic.Penitent.transform.position;

                pool.ReuseObject(
                    vertical.VerticalAttackBeamEffectPrefab,
                    world + offset,
                    Quaternion.identity,
                    false,
                    1);
            }

            // InstantiateExplosion returns WITHOUT doing anything when HardLandingEffectPrefab is null, so the
            // same guard belongs here - and it is load-bearing: ReuseObject dereferences the prefab.
            GameObject prefab = beam ? vertical.HardLandingEffectUpgradedPrefab : vertical.HardLandingEffectPrefab;
            if (prefab != null)
                pool.ReuseObject(prefab, world, Quaternion.identity, false, 1);

            string sound = LandingSound(vertical, effect);
            if (!string.IsNullOrEmpty(sound))
                Core.Audio.EventOneShotPanned(sound, world);
        }

        /// <summary>
        /// The landing sound for the shape the sender reported, taken from the LOCAL VerticalAttack. Its
        /// VerticalLandingFxLevel* fields are exactly what get_GetLandingFxEventKey selects between, and they
        /// hold the same bank paths on this machine (same build, same prefab).
        ///
        /// LandingImpactBase has no sound on purpose: when the selected skill is not one of the VERTICAL_* the
        /// game's own get_GetLandingFxEventKey returns an empty string, so that landing is silent on the
        /// machine that produced it as well.
        /// </summary>
        private static string LandingSound(
            Gameplay.GameControllers.Penitent.Abilities.VerticalAttack vertical,
            EffectType effect)
        {
            switch (effect)
            {
                case EffectType.LandingImpact1:
                    return vertical.VerticalLandingFxLevel1;
                case EffectType.LandingImpact2:
                    return vertical.VerticalLandingFxLevel2;
                case EffectType.LandingImpact3:
                case EffectType.LandingImpact3Beam:
                    return vertical.VerticalLandingFxLevel3;
                default:
                    return null;
            }
        }

        private IEnumerator DisplayDebla()
        {
            ModLog.Info("Spawning debla object for " + transform.parent.name);
            if (Core.Logic.Penitent == null)
                yield break;

            GameObject debla = Instantiate(Core.Logic.Penitent.PrayerCast.lightBeamPrayer.areaPrefab, transform);
            debla.name = "Debla";
            debla.transform.localPosition = Vector3.zero;
            Destroy(debla.GetComponent<Gameplay.GameControllers.Bosses.Quirce.Attack.BossSpawnedAreaAttack>());
            Destroy(debla.transform.GetChild(0).GetChild(0).gameObject);
            debla.GetComponent<Animator>().Play("AttackLoop");
            yield return new WaitForSecondsRealtime(DEBLA_DURATION);
            Destroy(debla);
        }

        private IEnumerator DisplayVerdiales()
        {
            ModLog.Info("Spawning verdiales object for " + transform.parent.name);
            if (Core.Logic.Penitent == null)
                yield break;

            GameObject crawlerright = Instantiate(Core.Logic.Penitent.PrayerCast.crawlerBallsPrayer.projectilePrefab, transform);
            crawlerright.name = "Crawler right";
            crawlerright.transform.localPosition = Vector3.zero;
            crawlerright.GetComponentInChildren<AttackArea>().enabled = false;
            crawlerright.GetComponentInChildren<PolygonCollider2D>().enabled = false;
            crawlerright.GetComponentInChildren<StraightProjectile>().Init(crawlerright.transform.position, crawlerright.transform.position + Vector3.right, 16);
            crawlerright.transform.position += Vector3.right * 0.01f;

            GameObject crawlerleft = Instantiate(Core.Logic.Penitent.PrayerCast.crawlerBallsPrayer.projectilePrefab, transform);
            crawlerleft.name = "Crawler left";
            crawlerleft.transform.localPosition = Vector3.zero;
            crawlerleft.GetComponentInChildren<AttackArea>().enabled = false;
            crawlerleft.GetComponentInChildren<PolygonCollider2D>().enabled = false;
            crawlerleft.GetComponentInChildren<StraightProjectile>().Init(crawlerleft.transform.position, crawlerleft.transform.position + Vector3.left, 16);
            crawlerleft.transform.position += Vector3.left * 0.01f;

            yield return new WaitForSecondsRealtime(VERDIALES_DURATION);
            Destroy(crawlerright);
            Destroy(crawlerleft); // Will probably have to leave colliders active to detect walls, maybe just change layermask to exlude enemy layer ?
        }
    }
}
