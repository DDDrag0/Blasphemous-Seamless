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
    /// ⚠️ LA SNAPSHOT DI PIXEL E' STATA SCARTATA, e il perche' e' concreto: `ReadPixels` legge solo
    /// **dentro il viewport della camera**, quindi un cadavere a bordo schermo verrebbe tagliato; servirebbe
    /// una camera temporanea + `RenderTexture` + `Texture2D` + `Sprite.Create`, quattro API in piu' e una
    /// texture da distruggere. Il clone non ha nessuno di quei problemi ed e' **esatto per costruzione**.
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
    /// I DIVIETI, riga per riga.
    ///   - ⛔ `Core.Input.SetBlocker`: **zero occorrenze**. `CINEMATIC` si legge e basta.
    ///   - ⛔ `Core.Input.ResetManager()`: zero.
    ///   - ⛔ `Time.timeScale` / `TimeScale`: zero. L'unico tempo usato e' `Time.unscaledTime`.
    ///   - ⛔ il `Penitent` dell'ALTRO: non esiste (e' un guscio, `OtherPlayerManager.cs:295-314`).
    ///     Si **legge** il transform del suo avatar, non si scrive mai.
    ///   - ⛔ la camera: **zero occorrenze** di `ProCamera2D`/`CameraManager` in questo file. E' il pregio
    ///     di questa via, ed e' la ragione per cui e' stata scelta.
    ///   - ⛔ `SetActive(false)`: zero. Si spegne **solo l'alpha** dello `SpriteRenderer` del Penitent.
    ///   - ⛔ `GetComponentsInChildren` nel ciclo per frame: **usato una volta per morte**, nella creazione
    ///     del clone (alloca, e per questo non e' nel tick).
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
        /// Per quanto tempo, dall'inizio della finestra, la copia **ricopia lo sprite** del Penitent.
        ///
        /// ⚠️ PERCHE' NON BASTA IL CLONE. Il morto diventa "morto" (`Status.Dead`) **all'inizio** della
        /// caduta: `Penitent.OnUpdate` alza `DeathEventLaunched` e chiama `OnDead` al primo frame in cui
        /// `Status.Dead` e' vero (`Penitent.cs:482-498`), mentre l'animazione di morte sta ancora girando.
        /// Il gioco **ferma quell'animazione solo alla fine** (`PlayerDeathAnimationBehaviour.cs:33-36`:
        /// `normalizedTime >= 0.95` -> `animator.enabled = false`).
        ///   ⇒ Un clone fatto **al primo frame** cristallizzerebbe **la prima posa della caduta**, e il
        ///     cadavere-copia mostrerebbe un corpo a meta' strada invece che a terra.
        ///   ⇒ E la correzione e' quella che costa meno: **non si riclona niente** (la posizione del
        ///     cadavere e' gia' quella giusta e **non deve muoversi**: e' il Penitent che si sposta, non la
        ///     copia). Si ricopia **solo `Sprite` e `color`** per una finestra breve, e il risultato e'
        ///     l'ultima posa - che e' esattamente quella che il gioco congela.
        /// Il valore copre la caduta con margine, e dopo di che si smette di leggere: nessun costo a
        /// regime, e **il cadavere si posa una volta sola**.
        /// </summary>
        private const float CORPSE_POSE_SECONDS = 2.5f;

        // ---- stato della finestra di morte. Nessuno di questi campi sopravvive a `Reset()`.
        private static bool _active;
        private static float _startedAt;
        private static Vector2 _deathPos;
        private static GameObject _copy;
        private static SpriteRenderer _copyRenderer;
        private static SpriteRenderer _body;
        private static float _poseUntil;
        private static float _originalAlpha = 1f;
        private static bool _rendererHidden;

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
                // resta intatto, cosi' il ripristino rimette un valore vero invece di indovinarlo.
                Color hidden = body.color;
                hidden.a = 0f;
                body.color = hidden;
                _rendererHidden = true;

                _active = true;
                _startedAt = Time.unscaledTime;
                _poseUntil = _startedAt + CORPSE_POSE_SECONDS;
                _moves = 0;
                _lastGap = 0f;
                _pausedByCinematic = false;
                _positionSendPaused = false;
                _repeatReported = false;
                _teleportFailed = false;

                ModLog.Info(MARK + " cadavere-copia creato: posMorte=" + V(_deathPos) +
                            " alphaPrima=" + F(_originalAlpha) + " copia=" + _copy.name +
                            " · il Penitent e' INVISIBILE e seguira' il vivo (la camera e' legata a lui)");
            }
            catch (Exception e)
            {
                Fail("Create", e);
            }
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
            // ---- 0. LA POSA DEL CADAVERE. Solo per i primi istanti: vedi `CORPSE_POSE_SECONDS`.
            // ⚠️ Prima di ogni altra cosa e **anche durante la cutscene**: la posa non sposta nessun
            // collider e non tocca il Penitent - e' solo lo sprite della copia - quindi non ha nessuna
            // delle ragioni per cui l'inseguimento si mette in pausa.
            if (now < _poseUntil)
                RefreshCorpseSprite();

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
        /// Tiene lo sprite della copia allineato a quello del Penitent **finche' l'animazione di morte sta
        /// girando**, cosi' il cadavere-copia si posa nella posa FINALE invece di restare a meta' caduta.
        ///
        /// ⚠️ NON SPOSTA NIENTE: copia **solo `Sprite` e `color`**. La posizione della copia e' fissata una
        /// volta sola alla creazione ed e' quella del punto di morte - e **non deve cambiare**, perche' e' il
        /// Penitent che si allontana. Il caso "il Penitent e' invisibile" e' innocuo: il suo `Sprite` resta
        /// quello dell'ultima posa raggiunta, quindi si ricopia sempre un valore valido.
        ///
        /// ⚠️ E IL CASO "NESSUNO SPRITE" E' DICHIARATO, non silenzioso: se a fine finestra lo sprite fosse
        /// ancora nullo, la copia disegnerebbe comunque qualcosa (il colore non lo si azzera mai), e la riga
        /// lo dice invece di lasciarlo scoprire a schermo.
        /// </summary>
        private static void RefreshCorpseSprite()
        {
            if (_copyRenderer == null || _body == null)
                return;

            Sprite sprite = _body.sprite;
            if (sprite != null && _copyRenderer.sprite != sprite)
                _copyRenderer.sprite = sprite;

            // ⚠️ E SI COPIA SOLO L'RGB, MAI L'ALPHA. Il Penitent e' gia' invisibile (`color.a = 0` da
            // `Create`), quindi copiare il colore **intero** renderebbe invisibile anche la copia - e per
            // 2,5 secondi non si vedrebbe NESSUN cadavere. L'alpha della copia resta quello ORIGINALE.
            Color theirs = _body.color;
            _copyRenderer.color = new Color(theirs.r, theirs.g, theirs.b, _originalAlpha);
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
        /// l'alpha **solo se l'abbiamo azzerato noi** (`_rendererHidden`). Se il gioco l'ha ricostruito
        /// (`SpawnManager.CreatePlayer` al load) il vecchio riferimento e' un oggetto distrutto, e
        /// scriverci sopra sarebbe l'unico modo di far danni.
        /// </summary>
        private static void Close(string why)
        {
            string report = why;
            try
            {
                Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
                if (penitent != null && _rendererHidden && penitent.SpriteRenderer != null)
                {
                    Color restored = penitent.SpriteRenderer.color;
                    restored.a = _originalAlpha;
                    penitent.SpriteRenderer.color = restored;
                    report += " · renderer ripristinato (alpha=" + F(_originalAlpha) + ")";
                }
                else if (_rendererHidden)
                {
                    report += " · renderer NON ripristinato (il Penitent e' stato ricostruito: niente da " +
                              "rimettere, ed e' il caso del respawn)";
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
            _body = null;
            _poseUntil = 0f;

            ModLog.Info(MARK + " finestra chiusa: " + report + " · durata=" + F(lasted) +
                        "s spostamenti=" + _moves + " ultimaDistanza=" + F(_lastGap) +
                        " invioPosizioneCongelato=" + (_positionSendPaused ? "si" : "no") +
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
                Fail("TryRealignNetwork", e);
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
