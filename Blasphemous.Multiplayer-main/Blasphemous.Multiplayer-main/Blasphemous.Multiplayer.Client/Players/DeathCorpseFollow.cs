using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using System;
using System.Globalization;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players
{
    /// <summary>
    /// LA CAMERA DEL MORTO SEGUE IL COMPAGNO VIVO — LA VIA (c), IL CADAVERE-COPIA.
    ///
    /// ⭐ PERCHE' QUESTA VIA E NON LA (b). La (b) toccherebbe la CAMERA: `RemoveAllCameraTargets()` +
    /// `AddCameraTarget()`, ed e' il primo contatto di questa mod con la camera (verificato: oggi non la
    /// tocca mai). Ma il problema non e' la camera: e' che la camera e' legata a
    /// `Core.Logic.Penitent.transform` (`CameraManager.cs:105`, scritto UNA VOLTA per livello da
    /// `LevelManager.cs:349`, e **mai piu' riscritto mentre si e' a terra**). Quindi **spostare il
    /// Penitent** ottiene lo stesso risultato **senza toccare la camera**: nessun target, nessun offset,
    /// nessun `OverallOffset` da salvare e rimettere, nessun `tag` da dare al guscio dell'altro
    /// (`OtherPlayerManager.cs:301` lo crea senza tag). E riusa due fatti gia' verificati in IL:
    ///   - il respawn **non legge il transform**: `Penitent.Respawn` -> `SpawnManager.Respawn` ->
    ///     `SpawnPlayerOnLevelLoad` prende la posizione dall'oggetto `PrieDieu` / `safePosition` (`:334-340`,
    ///     `:424-433`), **mai dal Penitent**;
    ///   - i nemici **ignorano** un Penitent morto (`EnemyAI.cs:258, 274, 282, 314`) ⇒ spostarlo **non
    ///     tira l'aggro** e non cambia nessuna decisione di combattimento.
    ///
    /// ⭐ COME SI OTTIENE LA "COPIA" (e perche' cosi'). Si clona **l'albero visivo** del Penitent - il
    /// GameObject che porta lo `SpriteRenderer`, cioe' il figlio `Body` (verificato nel log:
    /// `[Probe] PENITENT animator.name='Body'`) - sotto una radice nuova, **dopo aver spento su di essa
    /// OGNI `Behaviour`**: `Animator` compreso, cosi' la posa di morte **resta congelata** (e' esattamente
    /// cio' che fa il gioco da solo: `PlayerDeathAnimationBehaviour.OnStateUpdate` fa `animator.enabled =
    /// false` a fine animazione di morte, `:30-37`), e nessuno script del gioco puo' girare sulla copia.
    /// E' la stessa famiglia di `Object.Instantiate` che il progetto usa gia' per il fantasma
    /// dell'esecuzione (`OtherPlayerScript.cs:148`), con la differenza che li' si istanzia un prefab e qui
    /// una gerarchia gia' in scena.
    ///
    /// ⚠️ E IL CLONE NON BASTA, perche' **nasce al PRIMO frame di morte** (e' li' che `Status.Dead` diventa
    /// vero) mentre l'animazione di morte deve ancora girare: da solo cristallizzerebbe **la prima posa
    /// della caduta**. Non e' un'ipotesi, e' il difetto misurato dall'utente (*"e' rimasto IN PIEDI"*):
    /// la copia va **inseguendo** la sorgente - sprite, colore **e posa** - finche' la sorgente non si
    /// ferma. Vedi `UpdateCorpsePose`, `ClosePose` e `CORPSE_POSE_MAX_SECONDS`.
    ///
    /// ⚠️ LA SNAPSHOT DI PIXEL E' STATA SCARTATA, e il perche' e' concreto: `ReadPixels` legge solo
    /// **dentro il viewport della camera**, quindi un cadavere a bordo schermo verrebbe tagliato; servirebbe
    /// una camera temporanea + `RenderTexture` + `Texture2D` + `Sprite.Create`, quattro API in piu' e una
    /// texture da distruggere. Il clone non ha nessuno di quei problemi: gli si copia il nodo visivo.
    ///
    /// ⭐⭐ LA FONTE DELLA POSIZIONE DA INSEGUIRE: L'AVATAR DEL PEER, NON IL PACCHETTO.
    /// (integrazione al mandato, e viene dall'utente: *"il morto potrebbe anche utilizzare le posizioni
    /// comunicate gia' direttamente dal peer... e spostare il proprio corpo invisibile li'"*.)
    /// **SI', ed e' la strada presa.** E non serve **niente** di nuovo, perche' la mod **ha gia' trasformato
    /// quella posizione in un avatar su questa macchina**: `OtherPlayerManager.AddActivePlayer` costruisce
    /// l'`OtherPlayerScript`, e `ReceivePosition` -> `player.CurrentPosition = position` -> `transform.position
    /// = value` (`OtherPlayerScript.cs:766-772`) lo tiene **esattamente dove sta il peer**.
    ///   ⇒ **per seguire il peer basta leggere `transform.position` del SUO avatar, in LOCALE.**
    ///     Zero canale nuovo, zero parsing, zero richieste, **zero latenza aggiuntiva** - e soprattutto:
    ///     **e' la stessa fonte che il morto vede a schermo**, quindi "seguo cio' che vedo". Coerente.
    /// Come si risolve l'avatar, verificato in IL: `OtherPlayerManager.FindActivePlayer(name)` (`:266-293`)
    /// sulle liste che il progetto ha gia' (`AllConnectedPlayers`, `AllActivePlayers`), con la guardia del
    /// **"fake null" di Unity** (`:276-280` ripulisce la lista dagli avatar distrutti) - ed e' la stessa
    /// famiglia di `FindConnectedPlayerQuietly` che `PlayerDeathSync` usa gia' (`:129`).
    ///
    /// ⚠️ E IL LIMITE, DICHIARATO: **l'avatar del peer esiste SOLO nella stessa scena** -
    /// `LevelLoaded` -> `RemoveAllActivePlayers` (`OtherPlayerManager.cs:24-27, 331-338`) lo distrugge al
    /// cambio stanza, e `RemoveActivePlayer` fa lo stesso su `ReceiveLeaveScene`. ⇒ **fuori scena non c'e'
    /// nessun avatar da seguire**: `FindLivingPeer` restituisce null e il morto **resta sul cadavere**.
    /// E' il ripiego naturale ed e' quello giusto: se non c'e' nessuno da guardare, l'unica cosa sensata da
    /// inquadrare e' il proprio corpo.
    ///
    /// ⚠️⚠️ LA COSA PIU' DELICATA: LA POSIZIONE DEL MORTO CHE VIAGGIA. `MainPlayerManager` spedisce la
    /// posizione leggendo `Core.Logic.Penitent.transform.position` (`:94`) a 0,2 s, **senza nessuna guardia
    /// di morte**. Se non si facesse niente, il peer vedrebbe l'avatar del morto **addosso al suo** -
    /// peggio del difetto che stiamo correggendo. La scelta, DICHIARATA: **da morti non si spedisce piu'
    /// niente** (`MainPlayerManager.Update`, guardia `PlayerDeathSync.IsLocalPlayerDead()`).
    /// Conseguenza voluta: sul peer l'avatar resta **dov'e' caduto**, cioe' esattamente il cadavere che
    /// deve essere. Non e' una novita': la mod **gia' conta su questo comportamento** e lo scrive
    /// (`EnemySyncManager.cs:2366`: *"a dead player stops sending positions (the packet only goes out when
    /// the position changes), so their last known position goes stale"*). E la posizione **non e' un
    /// segnale di vita** (`PlayerDeathSync.cs:205-212`): la vita viaggia su `pdead|`/`palive|`, quindi
    /// congelare la posizione **non mente a nessuno** su chi e' vivo.
    /// Alla riapertura della finestra si rimanda lo stato completo con
    /// `MainPlayerManager.SendAllLocationData()` (metodo che esiste gia' e gira al load), cosi' il peer
    /// riallinea la posizione **subito** e non aspetta il battito di 0,2 s.
    ///
    /// LE VIE DI USCITA - TUTTE, e ognuna ha il suo marker.
    ///   1. **Il respawn** (il tasto): `IsLocalPlayerDead()` torna falso -> `Close("il giocatore e' tornato")`
    ///      -> copia distrutta, `SpriteRenderer` del Penitent ripristinato, invio posizione riacceso.
    ///   2. **La disconnessione / il cambio scena**: `OnDisconnect` di `Multiplayer` chiama `Reset()` ->
    ///      stessa chiusura, motivo `"disconnessione"`.
    ///   3. **IL FUSIBILE**, che LOGGA: se la finestra di morte supera `FUSE_SECONDS` la copia viene
    ///      distrutta e il Penitent riportato al punto di morte. E' anche la risposta al caso
    ///      "**ENTRAMBI MORTI**": senza un vivo da seguire il morto **non si muove** (resta sul cadavere),
    ///      esattamente dove sta oggi - vedi `Tick`.
    ///   4. **`OnDestroy` del Penitent** (ricostruito al load): il riferimento diventa null e il tick
    ///      successivo chiude da solo. E' la stessa garanzia su cui poggia `OtherPlayerScript.OnDestroy`.
    ///
    /// ⚠️ LA CUTSCENE DEL BOSS - LA DOMANDA CHE POTEVA UCCIDERE L'IDEA, e la risposta.
    /// Muovere il Penitent durante la cutscene del boss **non e' dimostrabile come sicuro**: la cutscene
    /// gira sul video ma **il mondo e' vivo** (misurato: il boss cammina per 24 s dietro la schermata di
    /// morte) e i trigger di scena leggono posizioni. La soluzione, come previsto dal mandato, e'
    /// **seguire il vivo SOLO FUORI dalla cutscene**: se `Core.Input.HasBlocker("CINEMATIC")` e' vero,
    /// l'inseguimento si **mette in pausa** (il Penitent resta dov'e', il cadavere-copia resta al suo
    /// posto) e riprende quando il blocker cade. `CINEMATIC` e' **letto, mai scritto** - e il blocker lo
    /// spegne il gioco (`Cinematics.cs:134/:156/:253`).
    ///
    /// ⚠️ E IL MOMENTO DELLA VITA IN CUI SI E' PIU' FRAGILI E' UN ALTRO, non la cutscene: **il primo
    /// fotogramma dopo la morte**, quando si posiziona il clone e si spegne il renderer. Per questo la
    /// creazione e' l'unica parte che puo' fallire in modo rumoroso, ed e' l'unica che ha un marker
    /// d'errore dedicato: se il clone non nasce, **il Penitent NON si nasconde e NON si muove** (meglio
    /// un cadavere al posto giusto che un cadavere invisibile).
    ///
    /// ⭐ E IL CLONE NASCE AL PRIMO FRAME DI MORTE, quindi nasce **a meta' caduta**: la posa va inseguita
    /// finche' la sorgente non si ferma. Non e' un dettaglio, e' il difetto che l'utente ha visto
    /// (*"ha fatto l'animazione di morte e poi e' rimasto IN PIEDI"*): vedi `CORPSE_POSE_MAX_SECONDS`,
    /// `UpdateCorpsePose` e `ClosePose`. Il segnale di fine e' **del gioco**
    /// (`Penitent.Animator.enabled == false`, scritto da `PlayerDeathAnimationBehaviour.OnStateUpdate:33-36`),
    /// non un timer nostro: cosi' la copia e' esatta per costruzione e la durata dell'animazione - che
    /// **non e' leggibile da nessun log** - smette di essere un numero da indovinare.
    ///
    /// I DIVIETI, riga per riga.
    ///   - ⛔ `Core.Input.SetBlocker`: **zero occorrenze**. `CINEMATIC` si legge e basta.
    ///   - ⛔ `Core.Input.ResetManager()`: zero.
    ///   - ⛔ `Time.timeScale` / `TimeScale`: zero. L'unico tempo usato e' `Time.unscaledTime`.
    ///   - ⛔ il `Penitent` dell'ALTRO: non esiste (e' un guscio, `OtherPlayerManager.cs:295-314`).
    ///     Si **legge** il transform del suo avatar, non si scrive mai.
    ///   - ⛔ la camera: **zero occorrenze** di `ProCamera2D`/`CameraManager` in questo file. E' il pregio
    ///     di questa via, ed e' la ragione per cui e' stata scelta.
    ///   - ⛔ `SetActive(false)`: zero. Si spegne **solo l'alpha** dello `SpriteRenderer` del Penitent, e lo
    ///     si riafferma a ogni frame finche' la finestra e' aperta (`KeepLocalCorpseHidden`).
    ///   - ⛔ `GetComponentsInChildren` nel ciclo per frame: **usato una volta per morte**, nella creazione
    ///     del clone (alloca, e per questo non e' nel tick). Il ciclo per frame della posa **non alloca**:
    ///     legge e scrive `Sprite`/`Color`/`localPosition`/`localRotation`/`localScale` di due oggetti.
    ///   - ✅ `try/catch` in ogni punto di ingresso: `Tick` e `Create` sono i due soli, e entrambi hanno
    ///     il loro - un'eccezione qui uscirebbe dentro `Multiplayer.OnLateUpdate`, che e' il guasto da
    ///     2934 errori.
    ///   - ✅ zero patch Harmony nuove: si aggancia al ciclo che gira gia' (`Multiplayer.OnLateUpdate`).
    /// </summary>
    internal static class DeathCorpseFollow
    {
        /// <summary>
        /// Marker di ESITO. Distinto da `[DeathScreen]` di proposito: la trasparenza e questa sono due
        /// interventi diversi, e un marker condiviso renderebbe impossibile dire quale dei due ha parlato.
        /// </summary>
        private const string MARK = "[CorpseFollow]";

        /// <summary>L'ultima riga di difesa, come in `CutsceneProbe`/`DeathScreenProbe`.</summary>
        private const string MARK_FAIL = "[CorpseFollow ERR]";

        /// <summary>
        /// Di quanto si aggiorna la posizione, in unita' di mondo.
        ///
        /// ⚠️ PERCHE' UNA SOGLIA E NON OGNI FRAME. Due ragioni, e la seconda e' quella che conta:
        ///   1. `Penitent.Teleport` azzera le velocita' e **spegne e riaccende i collider**
        ///      (`Penitent.cs:326-336`) - farlo 60 volte al secondo e' lavoro inutile;
        ///   2. e soprattutto: **spostare il transform ogni frame vuol dire spostare il `Collider2D` del
        ///      Penitent ogni frame**, quindi fargli attraversare tutta la stanza in una frazione di
        ///      secondo. Con una soglia il corpo **salta** di rado e non striscia attraverso i trigger.
        /// Una soglia generosa e' anche visivamente giusta: la camera ha lo smorzamento di `ProCamera2D`,
        /// quindi un saltino ogni tanto non si vede, mentre un inseguimento rigido si vedrebbe.
        /// </summary>
        private const float FOLLOW_EPSILON = 6f;

        /// <summary>
        /// Il fusibile, in secondi di tempo NON scalato. Se la finestra di morte lo supera, la copia muore
        /// e il Penitent torna al punto di morte.
        ///
        /// ⚠️ E' UN VALORE DA TARARE COI DATI, NON DA INDOVINARE. La finestra di morte misurata nell'ultima
        /// sessione e' di **~102 s** (`[DeathScreen] misura INIZIO t=184.21` -> `FINE ... campioni=52`,
        /// cioe' `t=286.67`): il morto ha tenuto il prompt **per tutta la bossfight e per tutta la
        /// cutscene**, che sono rispettivamente ~51 s e ~27 s. Un fusibile piu' corto del caso reale
        /// spegnerebbe l'inseguimento proprio durante la cutscene - cioe' il caso per cui la funzione
        /// esiste. Sei minuti stanno larghi su qualunque morte reale e restano un fusibile vero.
        /// </summary>
        private const float FUSE_SECONDS = 360f;

        /// <summary>
        /// IL TETTO DELLA "POSA DEL CADAVERE", in secondi NON scalati: oltre questo tempo la copia smette
        /// di inseguire la posa del Penitent **comunque**, e il marker lo dice.
        ///
        /// ⚠️ E' UN TETTO, NON UNA DURATA, ed e' la correzione del difetto "il morto resta IN PIEDI".
        ///
        /// IL DIFETTO, e perche' era qui. Il morto diventa "morto" (`Status.Dead`) **all'inizio** della
        /// caduta (`Penitent.OnUpdate` -> `OnDead`, `Penitent.cs:482-498`), mentre l'animazione di morte sta
        /// ancora girando: il gioco la **ferma solo alla fine**
        /// (`PlayerDeathAnimationBehaviour.OnStateUpdate:33-36`: `normalizedTime >= 0.95` ->
        /// `animator.enabled = false`). La copia nasce quindi **al primo frame di morte**, e la prima
        /// versione di questo file copiava lo sprite **solo per 2,5 s fissi**: se l'animazione di morte dura
        /// di piu' - e la sua durata **non e' leggibile da nessun log** - la copia si ferma **a meta'
        /// caduta**, cioe' **in piedi**. Ed e' esattamente cio' che l'utente ha visto: *"ha fatto
        /// l'animazione di morte e poi e' rimasto IN PIEDI"*.
        ///
        /// ⭐ LA CORREZIONE NON E' UN NUMERO PIU' GRANDE, E' UN ALTRO SEGNALE. Si smette di copiare quando
        /// **la sorgente smette di cambiare**, cioe' quando il gioco stesso congela la posa:
        /// `Penitent.Animator.enabled == false` (verificato in IL: e' `PlayerDeathAnimationBehaviour` a
        /// scriverlo, ed e' la stessa riga per cui il cadavere vero non si muove). Cosi' la copia e' esatta
        /// **per costruzione**, qualunque sia la durata dell'animazione:
        ///   - se il segnale arriva, la copia ha **l'ultima posa** - la stessa del cadavere che il gioco
        ///     tiene a terra;
        ///   - se non arriva mai (l'animazione non passa da quel `Behaviour`, il Penitent e' morto in aria,
        ///     o l'`Animator` e' gia' fermo), la copia si ferma **al tetto** e il `Warn` lo dice: si perde
        ///     qualita' visiva, **non la funzione** (il Penitent e' nascosto e l'inseguimento continua).
        /// ⚠️ E' una scelta ASIMMETRICA e voluta: aspettare **troppo** costa una copia che insegue ancora
        /// (innocuo), fermarsi **troppo presto** ricrea il difetto. Il segnale del gioco puo' solo arrivare
        /// tardi, mai prima del momento in cui la sorgente si ferma davvero.
        /// </summary>
        private const float CORPSE_POSE_MAX_SECONDS = 8f;

        // ---- stato della finestra di morte. Nessuno di questi campi sopravvive a `Reset()`.
        private static bool _active;
        private static float _startedAt;
        private static Vector2 _deathPos;
        private static GameObject _copy;
        private static SpriteRenderer _copyRenderer;
        /// <summary>
        /// IL NODO VISIVO DELLA COPIA (il clone di `Body`), per copiarne anche la POSA e non solo lo sprite.
        /// </summary>
        private static Transform _copyBody;
        private static SpriteRenderer _body;
        private static float _originalAlpha = 1f;
        private static bool _rendererHidden;

        /// <summary>
        /// Quanti frame la copia ha inseguito la posa del Penitent, e quante volte si e' dovuto
        /// **riazzerare l'alpha** dopo averlo fatto (vedi `KeepLocalCorpseHidden`). Il secondo numero e' la
        /// prova che qualcuno stava scrivendo sul colore del renderer: zero significa che nessuno l'ha
        /// toccato, e non e' un dettaglio - e' l'unica misura che distingue "l'alpha tiene" da "l'alpha
        /// viene riscritto e per un frame il corpo vero si rivede".
        /// </summary>
        private static int _poseFrames;
        private static int _hiddenReasserts;

        /// <summary>Se la posa del cadavere e' stata CHIUSA, e da cosa (il segnale del gioco o il tetto).</summary>
        private static bool _poseClosed;
        private static string _poseClosedWhy = "in corso";

        /// <summary>
        /// L'ultima riga di un guasto che si ripeterebbe identico a ogni frame. Il fusibile lo logga una
        /// volta sola: un log che si ripete e' spam, ed e' un divieto del mandato.
        /// </summary>
        private static bool _repeatReported;

        /// <summary>
        /// ⚠️ UN GUARDIANO SEPARATO PER IL FALLIMENTO DI `Teleport`, e non e' pignoleria: `_repeatReported`
        /// viene **riabbassato a ogni frame in cui il vivo si trova** (e' la guardia della riga "nessun
        /// compagno vivo"), quindi riusarlo qui rifarebbe uscire l'avviso a ogni spostamento - cioe' spam
        /// esattamente nel caso che deve restare leggibile.
        /// </summary>
        private static bool _teleportFailed;

        /// <summary>
        /// Quante volte si e' spostato il Penitent e quanto valeva l'ultima distanza al vivo. Servono alla
        /// riga di chiusura: senza, "non ha mai inseguito" e "ha inseguito benissimo" hanno lo stesso
        /// aspetto in un log.
        /// </summary>
        private static int _moves;
        private static float _lastGap;

        private static bool _pausedByCinematic;
        private static bool _positionSendPaused;

        /// <summary>
        /// Un giro per frame. Chiamata da `Multiplayer.OnLateUpdate` (vedi `Multiplayer.cs`).
        ///
        /// La macchina a stati, in ordine:
        ///   vivo    -> (niente)                        ... nessuno stato, nessun costo
        ///   morendo -> `Create()`                      ... copia + renderer + congelamento posizione
        ///   morto   -> insegui / resta / fusibile
        ///   risorto -> `Close()`                       ... copia distrutta, tutto rimesso
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_active)
                        Close("il giocatore e' tornato (respawn)");
                    return;
                }

                if (!_active)
                {
                    Create();
                    return;
                }

                float now = Time.unscaledTime;

                // ---- IL FUSIBILE. Prima di ogni altra cosa: e' l'unica via d'uscita che non dipende
                // ne' dal gioco ne' dalla rete.
                if (now - _startedAt > FUSE_SECONDS)
                {
                    float lasted = now - _startedAt;
                    Close("FUSIBILE scattato dopo " + F(lasted) + "s (la finestra di morte non si e' chiusa)");
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
        /// Chiude la finestra per disconnessione o per fine sessione.
        ///
        /// ⚠️ PERCHE' ESISTE, visto che `Tick` chiude gia' da solo quando `IsLocalPlayerDead()` torna
        /// falso: perche' **la disconnessione puo' arrivare mentre si e' ancora morti**, e in quel momento
        /// `IsLocalPlayerDead()` resta vero (il gioco non sa nulla della rete). Senza questa riga
        /// resterebbero in scena una copia e un renderer spento, con nessun vivo da seguire.
        /// </summary>
        internal static void Reset()
        {
            try
            {
                if (_active)
                    Close("disconnessione / fine sessione");
            }
            catch (Exception e)
            {
                Fail("Reset", e);
            }
        }

        // ============================================================================================
        // LA CREAZIONE - l'unica parte che puo' fallire in modo rumoroso
        // ============================================================================================

        /// <summary>
        /// Crea il cadavere-copia e nasconde il Penitent. **Se il clone non nasce, non si nasconde e non si
        /// muove NIENTE**: un cadavere al posto giusto e' preferibile a un cadavere invisibile.
        /// </summary>
        private static void Create()
        {
            try
            {
                Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
                if (penitent == null)
                {
                    ReportOnce("Penitent non disponibile: nessuna copia, nessun movimento");
                    return;
                }

                SpriteRenderer body = penitent.SpriteRenderer;
                if (body == null)
                {
                    // ⚠️ `Entity.SpriteRenderer` e' assegnato in `Awake` da `Animator.GetComponent<SpriteRenderer>()`
                    // (`Entity.cs:154-160`). Se e' null, il Penitent non ha un albero visivo conosciuto e
                    // **non si tenta niente**: senza sapere cosa disegnare non si nasconde nulla.
                    ReportOnce("Penitent.SpriteRenderer nullo: nessuna copia, nessun movimento");
                    return;
                }

                _deathPos = penitent.transform.position;
                _originalAlpha = body.color.a;

                _copy = CloneCorpse(body, penitent.transform);
                if (_copy == null)
                {
                    ReportOnce("clone del cadavere NON riuscito: il Penitent resta visibile e fermo al " +
                               "punto di morte (nessun movimento, nessun renderer spento)");
                    return;
                }

                // Da qui in avanti il Penitent puo' sparire: la copia e' in scena a reggere la posa.
                // ⚠️ SI SPEGNE **SOLO L'ALPHA**, non `SetActive` e non `SpriteRenderer.enabled`: l'RGB
                // resta intatto, cosi' il ripristino rimette un valore **vero** invece di indovinarlo - ed e'
                // anche la scelta piu' facile da disfare, perche' non tocca il flag che il GIOCO usa per
                // nascondere il Penitent (`Interactable.ShowPlayer`, `Tools.Level.Interactable.cs:494`:
                // `Core.Logic.Penitent.SpriteRenderer.enabled = show`, usato da `Execution`, `PrieDieu`,
                // `Altar`, `Lever` e da `SpawnManager.CreatePlayer`). Vedi `KeepLocalCorpseHidden` per la
                // riasserzione, e `HideLocalCorpse` per il marker obbligatorio.
                //
                // ⚠️ L'ORDINE CONTA, E NON E' PIGNOLERIA: **prima si apre la finestra, poi si nasconde.**
                // Se l'ordine fosse l'inverso e la scrittura dell'alpha esplodesse, la finestra non si
                // aprirebbe mai, `Close()` non verrebbe mai chiamato e il giocatore resterebbe **invisibile
                // per sempre** - cioe' il guasto peggiore che questo intervento possa produrre. Cosi', il
                // peggio che puo' succedere e' un cadavere visibile che si sposta (il difetto noto, non uno
                // nuovo), perche' la finestra si chiude comunque e la chiusura rimette l'alpha.
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

                HideLocalCorpse(body, "all'inizio della finestra (e' il cadavere-copia a restare a terra)");

                ModLog.Info(MARK + " cadavere-copia creato: posMorte=" + V(_deathPos) +
                            " alphaPrima=" + F(_originalAlpha) + " copia=" + _copy.name +
                            " · il Penitent e' " + (_rendererHidden ? "INVISIBILE" : "ANCORA VISIBILE " +
                            "(la scrittura dell'alpha NON e' riuscita: vedi la riga sopra)") +
                            " e seguira' il vivo (la camera e' legata a lui)");
            }
            catch (Exception e)
            {
                Fail("Create", e);
            }
        }

        /// <summary>
        /// ⭐ IL MARKER OBBLIGATORIO: **`cadavere locale nascosto (renderer=…)`**.
        ///
        /// ⚠️ PERCHE' SERVE, E NON E' PIGNOLERIA: senza questa riga *"non si e' nascosto"* e *"si e' nascosto
        /// e non e' piu' tornato"* hanno lo stesso aspetto in un log, ed e' l'unica cosa che distingue il
        /// difetto che stiamo correggendo da quello che questo fix potrebbe introdurre (un giocatore
        /// invisibile per sempre). Per questo la riga porta **il valore prima E dopo** e **lo stato di
        /// `enabled`**, non solo un verbo: se al prossimo test il corpo vero si vedesse lo stesso, la riga
        /// dice se l'alpha e' a zero (allora e' il materiale/sprite a ignorarlo, e la riga da cambiare e'
        /// `enabled`) oppure no.
        /// </summary>
        private static void HideLocalCorpse(SpriteRenderer body, string why)
        {
            // ⚠️ IL FLAG PRIMA DELLA SCRITTURA: `Close()` ripristina **solo se** `_rendererHidden` e' vero,
            // quindi alzarlo per primo e' cio' che rende il ripristino vero anche nel caso in cui la
            // scrittura sotto lanci a meta'. Al contrario (flag dopo) un'eccezione lascerebbe un alpha
            // azzerato e nessuno che lo rimetta: un giocatore invisibile per sempre.
            _rendererHidden = true;

            float before = body.color.a;
            Color hidden = body.color;
            hidden.a = 0f;
            body.color = hidden;

            ModLog.Info(MARK + " cadavere locale nascosto (renderer=SpriteRenderer di " +
                        (body.gameObject != null ? body.gameObject.name : "?") +
                        " · sprite=" + (body.sprite != null ? body.sprite.name : "nessuno") +
                        " · enabled=" + (body.enabled ? "True" : "False") +
                        " · alpha " + F(before) + " -> " + F(body.color.a) + ") · " + why);
        }

        /// <summary>
        /// ⚠️ L'ALPHA SI RIAFFERMA A OGNI FRAME mentre la finestra e' aperta, e costa due letture.
        ///
        /// PERCHE': l'alpha **non e' un nostro campo privato**, e' una proprieta' del renderer del Penitent
        /// che qualunque cosa puo' riscrivere - l'animazione stessa se la clip ha una traccia sul colore, un
        /// effetto di danno, un `DamageEffect`, o il gioco che accende/spegne la visibilita' del Penitent.
        /// Una sola scrittura, all'inizio, affida l'invisibilita' a un fatto che non controlliamo: **una
        /// riscrittura, e il corpo vero riappare in piedi mentre la copia e' a terra** - cioe' esattamente
        /// il difetto da cui siamo partiti, con un cadavere in piu'. Riaffermarla e' l'unico modo di
        /// renderlo impossibile invece che improbabile, e il contatore `_hiddenReasserts` dice se e' servito.
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
        /// Costruisce la copia: una radice nuova + **l'albero visivo clonato**, con **ogni `Behaviour`
        /// spento** (quindi `Animator` compreso: la posa di morte resta quella che il gioco ha congelato).
        ///
        /// Restituisce `null` se non c'e' niente da clonare - e il chiamante, in quel caso, **non tocca
        /// niente**.
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

                // `worldPositionStays: false` come nel fantasma dell'esecuzione si ottiene passando il
                // padre: la copia conserva la posizione LOCALE del figlio rispetto al Penitent, che e'
                // esattamente quella giusta.
                GameObject clone = UnityEngine.Object.Instantiate(visualRoot, copyRoot.transform, false);
                if (clone == null)
                {
                    UnityEngine.Object.Destroy(copyRoot);
                    return null;
                }

                Freeze(clone);

                // ⚠️ IL RIFERIMENTO AL RENDERER DELLA COPIA si prende ADESSO e non a ogni refresh: e'
                // `GetComponent`, quindi non alloca, ma il punto vero e' che se il clone non avesse un
                // `SpriteRenderer` **lo si scopre qui** e non due secondi dopo.
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
        /// Spegne **ogni `Behaviour`** dell'albero clonato.
        ///
        /// ⚠️ PERCHE' TUTTI E NON SOLO L'`Animator`. Tre ragioni, in ordine di gravita':
        ///   1. **`Animator`**: e' quello che rende la copia una FOTO. Il gioco stesso ferma l'animazione di
        ///      morte con `animator.enabled = false` (`PlayerDeathAnimationBehaviour.cs:35`) - qui si fa la
        ///      stessa cosa, un frame prima, sulla copia.
        ///   2. **`EntityAnimationEvents`**: il suo `Start()` fa `GetComponentInParent<Execution>()` **senza
        ///      guardia `null`** ed e' la NRE misurata del fantasma dell'esecuzione
        ///      (`OtherPlayerScript.cs:220-243`: *"la NRE e' garantita, non probabile"*). Spegnere il
        ///      componente prima che `Start()` giri e' l'unica neutralizzazione che non richiede di
        ///      indovinare cosa c'e' sull'albero.
        ///   3. **tutto il resto**: su una copia non deve girare **nessuno** script del gioco. La
        ///      neutralizzazione piu' sicura di un componente e' che non faccia niente, ed e' anche la
        ///      lezione del fantasma dell'esecuzione.
        ///
        /// `SpriteRenderer` **non e' un `Behaviour`** ed e' l'unica cosa che deve restare accesa: e' la
        /// copia.
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

            ModLog.Info(MARK + " copia congelata: behaviour spenti=" + off +
                        " (SpriteRenderer escluso: non e' un Behaviour)");
        }

        // ============================================================================================
        // L'INSEGUIMENTO
        // ============================================================================================

        /// <summary>
        /// Sposta il Penitent (e quindi la camera, che e' legata a lui) sul **compagno vivo**.
        ///
        /// LE TRE CONDIZIONI CHE FERMANO L'INSEGUIMENTO, e cosa fa il morto in ognuna:
        ///   - **cutscene in corso** (`CINEMATIC` acceso) -> **pausa**: il Penitent resta dov'e'. E' la
        ///     risposta al rischio dichiarato dal mandato ("muoverlo potrebbe far scattare un trigger").
        ///   - **nessun vivo da seguire** (l'altro e' morto, o non c'e' nessun altro) -> **resta sul
        ///     cadavere**. E' il caso "ENTRAMBI MORTI": non si muove niente, come oggi.
        ///   - **l'altro e' in un'altra scena** (il suo guscio non esiste qui) -> **resta**: non c'e'
        ///     nessuna posizione da inseguire, e il morto non deve inventarne una.
        /// </summary>
        private static void Follow(float now)
        {
            // ---- 0. LA POSA DEL CADAVERE. Fino a quando la SORGENTE smette di cambiare: vedi
            // `CORPSE_POSE_MAX_SECONDS` e `UpdateCorpsePose`.
            // ⚠️ Prima di ogni altra cosa e **anche durante la cutscene**: la posa non sposta nessun
            // collider e non tocca il Penitent - e' solo lo sprite/il nodo visivo della copia - quindi non
            // ha nessuna delle ragioni per cui l'inseguimento si mette in pausa.
            if (!_poseClosed)
                UpdateCorpsePose(now);

            // ---- 1. LA CUTSCENE. Il blocker si LEGGE, non si scrive.
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
                    ModLog.Info(MARK + " inseguimento IN PAUSA: cutscene in corso (blocker CINEMATIC acceso) " +
                                "· il Penitent resta fermo, nessun trigger di scena puo' scattare per colpa nostra");
                }
                return;
            }

            if (_pausedByCinematic)
            {
                _pausedByCinematic = false;
                ModLog.Info(MARK + " inseguimento RIPRESO: cutscene finita (blocker CINEMATIC spento)");
            }

            // ---- 2. IL VIVO. Si cerca un guscio ATTIVO il cui giocatore non risulti morto.
            OtherPlayerScript alive = FindLivingPeer(out string aliveName);
            if (alive == null)
            {
                if (!_repeatReported)
                {
                    _repeatReported = true;
                    ModLog.Info(MARK + " nessun compagno vivo da seguire: il Penitent RESTA sul cadavere " +
                                "(caso \"entrambi morti\", oppure l'altro e' in un'altra scena) · " +
                                "nessun movimento, nessuna copia distrutta");
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

            ModLog.Info(MARK + " Penitent spostato sul vivo " + aliveName + ": pos=" + V(target) +
                        " (era a " + F(gap) + " unita' dal cadavere in " + V(_deathPos) + ") " +
                        "spostamenti=" + _moves + " · la camera lo segue da sola");
        }

        /// <summary>
        /// UN FRAME DI INSEGUIMENTO DELLA POSA, e la decisione di chiuderla.
        ///
        /// ⭐ LA REGOLA, in una riga: **si copia finche' la sorgente cambia, e ci si ferma quando il GIOCO
        /// congela la sorgente.** Il segnale e' `Penitent.Animator.enabled == false`, cioe' la riga che
        /// `PlayerDeathAnimationBehaviour.OnStateUpdate` (`:33-36`) esegue a `normalizedTime >= 0.95`
        /// dell'animazione di morte - la stessa riga per cui il cadavere vero non si muove piu'.
        ///   ⇒ **La copia e' esatta PER COSTRUZIONE**: nell'istante in cui la sorgente si ferma, la copia
        ///     ha l'ultimo valore copiato, che e' quello che il gioco tiene a terra. Non c'e' nessun numero
        ///     da indovinare, ed e' la ragione per cui questo fix non dipende dalla durata dell'animazione
        ///     (che **non e' leggibile da nessun log**).
        ///   - **se il segnale arriva**: riga `Info`, con la durata MISURATA - cosi' la prossima sessione ha
        ///     il numero che oggi manca;
        ///   - **se non arriva mai** (l'animazione non passa da quel `Behaviour`, il Penitent e' morto in
        ///     aria, l'`Animator` e' gia' fermo o qualcosa l'ha riacceso): scatta `CORPSE_POSE_MAX_SECONDS`
        ///     e si smette lo stesso. Si perde qualita' visiva, **non la funzione**, e il `Warn` lo dichiara
        ///     invece di lasciarlo scoprire a schermo.
        /// </summary>
        private static void UpdateCorpsePose(float now)
        {
            RefreshCorpseSprite();
            KeepLocalCorpseHidden();
            _poseFrames++;

            Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
            UnityEngine.Animator animator = penitent != null ? penitent.Animator : null;

            // ---- IL SEGNALE DEL GIOCO: la posa e' quella finale perche' e' il gioco ad averla fermata.
            if (animator != null && !animator.enabled)
            {
                ClosePose("animazione di morte finita: il gioco ha spento l'Animator del Penitent e la posa " +
                          "e' quella finale (PlayerDeathAnimationBehaviour)");
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
                ModLog.Info(MARK + " posa del cadavere chiusa dopo " + F(Time.unscaledTime - _startedAt) +
                            "s e " + _poseFrames + " frame copiati · " + why);
                return;
            }

            _poseClosedWhy = "TETTO di " + F(CORPSE_POSE_MAX_SECONDS) + "s: l'Animator del Penitent non si " +
                             "e' mai fermato (l'animazione di morte non passa da `PlayerDeathAnimationBehaviour`, " +
                             "oppure e' piu' lunga del tetto, oppure e' morto in aria) · la copia si ferma " +
                             "sull'ultima posa copiata e l'inseguimento continua";
            ModLog.Warn(MARK + " posa del cadavere chiusa dal TETTO dopo " + F(Time.unscaledTime - _startedAt) +
                        "s e " + _poseFrames + " frame copiati · " + _poseClosedWhy);
        }

        /// <summary>
        /// Copia sulla copia **lo sprite, il colore e la POSA** del nodo visivo del Penitent.
        ///
        /// ⚠️ ANZITUTTO LA POSA, che la prima versione non copiava ed e' l'altra meta' del difetto "resta in
        /// piedi". Il clone nasce **al primo frame di morte** (vedi `CORPSE_POSE_MAX_SECONDS`), quindi porta
        /// la posa di QUEL frame: se l'animazione di morte sposta o ruota il nodo visivo e non solo lo
        /// sprite, un clone congelato la' resta **in piedi**. Copiare `localPosition`/`localRotation`/
        /// `localScale` e' l'unico modo di essere esatti per costruzione, ed **e' locale e non mondiale**:
        /// il nodo della copia sta sotto una radice ferma al punto di morte (il Penitent invece si
        /// allontana, quindi copiare il mondo trascinerebbe la copia dietro di lui).
        ///
        /// ⚠️ E SI COPIA SOLO L'RGB, MAI L'ALPHA. Il Penitent e' gia' invisibile (`color.a = 0` da
        /// `Create`), quindi copiare il colore **intero** renderebbe invisibile anche la copia - e non si
        /// vedrebbe NESSUN cadavere. L'alpha della copia resta quello ORIGINALE.
        ///
        /// ⚠️ E IL CASO "NESSUNO SPRITE" E' DICHIARATO, non silenzioso: se a fine finestra lo sprite fosse
        /// ancora nullo, la copia disegnerebbe comunque qualcosa (il colore non lo si azzera mai), e la riga
        /// di chiusura porta il numero di frame copiati proprio per dire se la copia e' mai stata toccata.
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

            // ---- LA POSA. Locale -> locale: la radice della copia e' il fotogramma del punto di morte.
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
        /// Lo spostamento vero e proprio, con la via di riserva.
        ///
        /// ⭐ `Penitent.Teleport(Vector2)` e' **public** (`Penitent.cs:326-336`) ed e' la via giusta:
        /// azzera velocita' e accelerazione, **spegne i collider**, sposta, e li riaccende - cioe' sposta il
        /// corpo **senza fargli attraversare la geometria**. Scrivere `transform.position` a mano
        /// lascerebbe il `PlatformCharacterController` con una velocita' residue e i collider accesi
        /// mentre il corpo salta.
        ///
        /// ⚠️ MA `Teleport` legge `Core.Logic.Penitent` e tocca
        /// `PlatformCharacterController.PlatformCharacterPhysics` / `Physics` **senza guardie null**: se una
        /// di quelle catene fosse null (un Penitent a meta' costruzione, un cambio scena nel mezzo) si
        /// lancerebbe. Per questo la via di riserva esiste ed e' la riga piu' semplice possibile.
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
                    ModLog.Warn(MARK + " Teleport() ha fallito (" + e.GetType().Name +
                                "): passo allo spostamento diretto del transform, che non tocca collider ne' " +
                                "velocita' - il cadavere-copia resta comunque al suo posto");
                }
            }

            try
            {
                penitent.transform.position = new Vector3(target.x, target.y, penitent.transform.position.z);
            }
            catch (Exception e)
            {
                Fail("MoveTo (transform diretto)", e);
            }
        }

        /// <summary>
        /// Il primo guscio ATTIVO il cui giocatore **non risulta morto**.
        ///
        /// ⚠️ PERCHE' NON BASTA `FindActivePlayer`/`AllActivePlayers`: l'avatar dell'altro **resta in scena
        /// anche quando e' morto** (`OtherPlayerManager.Update` non lo nasconde: aggiorna solo i nametag), e
        /// seguire un cadavere porterebbe la camera del morto **addosso a un altro cadavere**. L'unica fonte
        /// di "chi e' vivo" e' l'annuncio, ed e' esattamente quella che la mod usa gia'
        /// (`PlayerStatus.IsDead`, `PlayerDeathSync.cs:127-131`).
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
                        continue; // in un'altra scena: non c'e' nessuna posizione da inseguire

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

        // ============================================================================================
        // LA CHIUSURA - e non deve poter fallire in silenzio
        // ============================================================================================

        /// <summary>
        /// Riporta tutto com'era.
        ///
        /// ⚠️ L'ORDINE CONTA. Prima **si rimette il Penitent** (se e' ancora vivo e valido) e **si riaccende
        /// il suo renderer**, poi si distrugge la copia: se la copia morisse per prima, per un fotogramma il
        /// cadavere non esisterebbe **e** il Penitent sarebbe ancora invisibile - cioe' un lampo di vuoto.
        /// Al contrario il peggio e' un fotogramma con **due** cadaveri, che non si nota.
        ///
        /// ⚠️ E IL RIPRISTINO DEL RENDERER E' CONDIZIONATO A UN FATTO, non a una speranza: si rimette
        /// l'alpha **solo se l'abbiamo azzerato noi** (`_rendererHidden`).
        ///
        /// ⭐ E SI RIPRISTINA **IL RENDERER CHE ABBIAMO SPENTO**, non "quello del Penitent di adesso":
        /// `_body` e' l'oggetto esatto su cui abbiamo scritto, e in Unity un riferimento distrutto e' "falso
        /// null", quindi la guardia e' la stessa - ma il caso "il Penitent di adesso e' lo STESSO oggetto e
        /// il suo `SpriteRenderer` e' momentaneamente nullo" (misurato in questa sessione: al respawn la mod
        /// legge `renderer NON ripristinato` **e** `TryRealignNetwork` esplode su `CurrentDirection`, che
        /// legge `Penitent.SpriteRenderer`) non lascerebbe piu' l'alpha a zero per sempre. Il ripiego sul
        /// Penitent corrente resta, per il caso in cui `_body` sia davvero andato.
        /// </summary>
        private static void Close(string why)
        {
            string report = why;
            try
            {
                Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;

                // ⭐ IL MARKER OBBLIGATORIO, nella direzione opposta a quello di `HideLocalCorpse`, e come
                // riga PROPRIA: deve poter essere cercato per `[CorpseFollow] cadavere locale mostrato`, e
                // dentro la riga di chiusura quel prefisso non ci sarebbe.
                if (_rendererHidden && _body != null)
                {
                    float was = _body.color.a;
                    Color restored = _body.color;
                    restored.a = _originalAlpha;
                    _body.color = restored;
                    ModLog.Info(MARK + " cadavere locale mostrato (renderer=SpriteRenderer di " +
                                (_body.gameObject != null ? _body.gameObject.name : "?") +
                                " · enabled=" + (_body.enabled ? "True" : "False") +
                                " · alpha " + F(was) + " -> " + F(_body.color.a) +
                                ") · e' l'oggetto che avevamo spento");
                }
                else if (_rendererHidden && penitent != null && penitent.SpriteRenderer != null)
                {
                    float was = penitent.SpriteRenderer.color.a;
                    Color restored = penitent.SpriteRenderer.color;
                    restored.a = _originalAlpha;
                    penitent.SpriteRenderer.color = restored;
                    ModLog.Info(MARK + " cadavere locale mostrato (renderer=SpriteRenderer di " +
                                (penitent.SpriteRenderer.gameObject != null
                                    ? penitent.SpriteRenderer.gameObject.name : "?") +
                                " · enabled=" + (penitent.SpriteRenderer.enabled ? "True" : "False") +
                                " · alpha " + F(was) + " -> " + F(penitent.SpriteRenderer.color.a) +
                                ") · RIPIEGO: il renderer che avevamo spento non esiste piu'");
                }
                else if (_rendererHidden)
                {
                    ModLog.Info(MARK + " cadavere locale NON mostrato (renderer=distrutto e Penitent " +
                                "ricostruito: non c'e' niente da rimettere, ed e' il caso del respawn - il " +
                                "Penitent NUOVO e' un altro oggetto e nasce visibile)");
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
                Fail("Close/copia", e);
            }

            float lasted = _startedAt > 0f ? Time.unscaledTime - _startedAt : 0f;

            // ⚠️ SI LEGGE PRIMA DI AZZERARE, e questa riga e' un fix a se': nella sessione precedente la
            // riga di chiusura stampava `invioPosizioneCongelato=no` **sempre**, perche' leggeva
            // `_positionSendPaused` dopo averlo messo a `false` qui sotto. ⇒ quel `no` NON era la prova che
            // la guardia di `MainPlayerManager` non era scattata: era un marker che non poteva dire altro.
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
            // ⚠️ ANCHE I RIFERIMENTI SI AZZERANO, e non e' pignoleria: `_body` punta a un `SpriteRenderer`
            // che al respawn puo' essere gia' distrutto, e `RefreshCorpseSprite` lo legge. Azzerarli rende
            // impossibile che una finestra chiusa lasci uno stato letto dalla successiva.
            _copyRenderer = null;
            _copyBody = null;
            _body = null;
            _poseClosed = false;
            _poseFrames = 0;
            _hiddenReasserts = 0;
            _poseClosedWhy = "in corso";

            ModLog.Info(MARK + " finestra chiusa: " + report + " · durata=" + F(lasted) +
                        "s spostamenti=" + _moves + " ultimaDistanza=" + F(_lastGap) +
                        " invioPosizioneCongelato=" + (sendPaused ? "si" : "no") +
                        " · posa=" + (poseClosed ? "chiusa" : "ancora aperta") + "(" + poseWhy + ")" +
                        " framePosa=" + poseFrames + " riasserzioniAlpha=" + reasserts +
                        " · la copia e' distrutta");

            // ⭐ IL RIALLINEAMENTO DELLA RETE. Da morti non si e' spedito niente (vedi
            // `MainPlayerManager.Update`), quindi il peer ha ancora l'avatar **dov'e' caduto**: la
            // posizione vera va rimandata ORA, senza aspettare il battito di 0,2 s. E il metodo che manda
            // posizione + animazione + direzione insieme esiste gia' ed e' quello che gira al load.
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
                ModLog.Info(MARK + " posizione/animazione/direzione rimandate al peer " +
                            "(da morti l'invio era congelato: senza questa riga l'avatar resterebbe " +
                            "dov'e' caduto fino al battito successivo)");
            }
            catch (Exception e)
            {
                // ⚠️ IL TIPO SI STAMPA, e non e' cosmetico: nella sessione precedente questa riga e' uscita
                // con il messaggio VUOTO (`[CorpseFollow ERR] in TryRealignNetwork ... : `), quindi non
                // diceva nemmeno che tipo di guasto fosse. `SendAllLocationData` legge
                // `Core.Logic.Penitent.Animator` e `Core.Logic.Penitent.SpriteRenderer` (CurrentAnimation /
                // CurrentDirection): al respawn, con il Penitent appena ricostruito, uno dei due puo' essere
                // ancora nullo - ed e' la stessa cosa che ha fatto stampare `renderer NON ripristinato`.
                // Il tipo (quasi sempre `NullReferenceException`) e' l'unica informazione disponibile qui.
                ModLog.Error(MARK_FAIL + " in TryRealignNetwork (nessun effetto sul gioco): " +
                             e.GetType().Name + " - " + e.Message);
            }
        }

        // ============================================================================================
        // STATO CONDIVISO CON MainPlayerManager
        // ============================================================================================

        /// <summary>
        /// True mentre la finestra di morte e' aperta **e** l'inseguimento e' partito.
        ///
        /// ⚠️ E' LETTO DA `MainPlayerManager.Update`, ed e' li' che si decide di **non spedire la
        /// posizione**. La guardia e' questa e non `IsLocalPlayerDead()` per una ragione precisa: se il
        /// cadavere-copia **non e' riuscito a nascere**, il Penitent **resta al suo posto** e non c'e'
        /// nessun motivo di congelare la posizione - la posizione vera e' ancora quella giusta. Congelarla
        /// li' sarebbe una bugia gratuita.
        /// </summary>
        internal static bool PositionSendPaused => _active;

        /// <summary>
        /// Segnala a questo componente che l'invio della posizione e' stato effettivamente sospeso, cosi'
        /// la riga di chiusura puo' dire **cosa** e' stato rimandato. E' una dichiarazione, non una leva:
        /// se il flag non viene mai alzato, la chiusura lo dice.
        /// </summary>
        internal static void NotePositionSendPaused()
        {
            _positionSendPaused = true;
        }

        /// <summary>L'ultima riga di difesa: non deve poter lanciare a sua volta.</summary>
        internal static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " in " + where + " (nessun effetto sul gioco): " + e.Message);
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
            ModLog.Error(MARK + " NON attivato: " + reason);
        }

        /// <summary>Un vettore in forma compatta, coi numeri in formato INVARIANTE (come le altre probe).</summary>
        private static string V(Vector2 v)
        {
            return "(" + F(v.x) + "," + F(v.y) + ")";
        }

        /// <summary>
        /// Formato dei numeri FISSO (invariante): la cultura di questa macchina e' italiana e scriverebbe
        /// `0,5`. La chiave di lettura del registro e' l'ordine e i numeri, e un separatore che cambia li
        /// renderebbe ambigui da leggere e da cercare.
        /// </summary>
        private static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
