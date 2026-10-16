using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using Gameplay.UI.Others.Buttons;
using System;
using System.Globalization;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// LA VIA D'USCITA GARANTITA PER IL GIOCATORE MORTO — la lezione del guasto "il peer morto non
    /// respawna piu': i tasti non funzionano".
    ///
    /// ============================================================================================
    /// IL GUASTO, e la riga di log che lo prova (sessione G1, `output_log G1.txt`).
    ///
    ///   riga 2510  `[TRACE][INPUT] The input blocker <color=green>DEAD_SCREEN</color> has been enabled.`
    ///   ...        e NON esiste, in tutto il log, la riga corrispondente `... DEAD_SCREEN ... disabled`.
    ///
    /// Lo `stack` dei blocker ricostruito riga per riga su tutto il log dice, alla fine della sessione:
    ///
    ///   `FADE`        -> +@3159  (mai spento)
    ///   `DEAD_SCREEN` -> +@2510  (mai spento)
    ///
    /// e il giocatore e' rimasto morto **fino alla chiusura del gioco** (`[CorpseFollow] finestra chiusa:
    /// disconnessione / fine sessione` alla riga 3345, con la finestra di morte ancora aperta).
    /// ⭐ LA PROVA CHE IL RESPAWN NON E' MAI PARTITO, e non e' una deduzione: `Penitent.Respawn()` ha un
    /// `Prefix` di Harmony che logga **sempre** la sua prima riga
    /// (`PlayerDeathSync.cs`, `PenitentRespawn_Patch`: `[Morte] il giocatore e' morto: connesso=...`).
    /// **Quella riga non compare: `Respawn()` non e' stato invocato nemmeno una volta.**
    /// E non compare nemmeno `[TRACE][FADE] Called fade function. Active: False`, cioe' la riga che
    /// `FadeWidget.FadeAfterDelay` scrive SEMPRE alla fine di un fade (FadeWidget.cs:133): l'undicesima e
    /// ultima occorrenza e' `Active: True` (@3163, il fade della cutscene di morte del boss), e il suo
    /// completamento non e' mai arrivato.
    ///
    /// ⇒ **Il respawn NON e' stato rifiutato: non e' mai stato chiesto.** `DeadScreenWidget.OnDeadAction`
    /// (:87-96) esce da `while (!Input.anyKey)` e SOLO ALLORA fa `Core.UI.Fade.Fade(...)` e SOLO nel suo
    /// callback chiama `Core.Logic.Penitent.Respawn()`. Nessun fade, nessun `Respawn`: la lettura dei tasti
    /// di quella schermata non e' arrivata a destinazione.
    ///
    /// ============================================================================================
    /// PERCHE' QUESTA RETE E NON UNA TOCCATA AI BLOCKER.
    ///
    /// ⛔ `Core.Input.ResetManager()` azzera **TUTTI** i blocker (`InputManager.cs:181-185` →
    ///    `RemoveBlockers()`), quindi usarlo come riflesso riaprirebbe l'input **durante un'altra sequenza**
    ///    (una cutscene, un dialogo, un'esecuzione). E' il divieto esplicito del mandato.
    /// ⛔ `DEAD_SCREEN` e' **del gioco** e lo spegne `OnPenitentReady` (`DeadScreenWidget.cs:101`):
    ///    non lo si tocca.
    /// ✅ E non serve toccarli, perche' **l'uscita buona esiste gia' ed e' quella del gioco**:
    ///    `Core.Logic.Penitent.Respawn()` (`Penitent.cs:310-315`) → `SpawnManager.Respawn()` →
    ///    `SpawnPlayer(..., forceLoad: true)`, cioe' **un vero load di livello**, e il load e' l'unico
    ///    punto in cui il gioco rimette a posto tutto da solo:
    ///      - `LevelManager` : `ResetManager()` (:355) ⇒ blocker azzerati dal GIOCO;
    ///      - `FadeWidget.instance.StartEasyFade(..., toBlack: false)` (:354) ⇒ il fade riaperto dal GIOCO;
    ///      - `CreatePlayer` → `SpawnManager.OnPlayerSpawn` (:505-508) ⇒ `DeadScreenWidget.OnPenitentReady`
    ///        ⇒ `SetBlocker("DEAD_SCREEN", false)` e `HideAll()`.
    ///    **Il watchdog non ripara i blocker: chiede al gioco di fare la cosa che li ripara.**
    ///
    /// LE DUE RETI, e sono indipendenti di proposito.
    ///
    ///   RETE 1 — "il tasto premuto dal morto". Il ciclo per frame della mod gira **anche quando il gioco
    ///   non legge piu' i tasti** (e' il ciclo che ha continuato a misurare `[DeathScreen] misura` fino
    ///   all'ultima riga del log). Qui si guarda `UnityEngine.Input` **in proprio** e, alla prima pressione
    ///   vera, si chiama `Respawn()`. E' la stessa cosa che la schermata di morte farebbe: se lei ci
    ///   arriva, questa rete e' un **no-op** (il giocatore e' gia' risorto quando il tasto arriva); se non
    ///   ci arriva, questa e' l'unica strada.
    ///
    ///   RETE 2 — "il fusibile". Se il giocatore resta morto oltre `ESCAPE_SECONDS` **dopo la fine della
    ///   sequenza di morte del boss** con la schermata di morte aperta e **nessuna** sequenza in corso
    ///   (niente `CINEMATIC`, niente `DIALOG`, niente cutscene di `Cinematics`, niente dialogo), il respawn
    ///   viene invocato **anche senza tasti**. Un giocatore bloccato non puo' piu' giocare: e' il guasto
    ///   peggiore possibile, e a quel punto il respawn e' **esattamente cio' che il gioco avrebbe fatto al
    ///   primo tasto**.
    ///   ⭐⭐ **I DUE NUMERI (19/10)**: `BOSS_DEATH_GRACE_SECONDS = 30 s` **prima** (la grazia perche' parta
    ///   una cutscene: e' la richiesta dell'utente, ed e' 7–10× la finestra misurata di ~3.0–3.9 s) e
    ///   `ESCAPE_SECONDS = 15 s` **dopo** (la fine della scena: da li' il gioco non sta piu' lavorando, e
    ///   il tasto lo copre gia' `RETE 1`, che legge il dispositivo anche con l'input bloccato). E l'ultima
    ///   spiaggia resta `MAX_ESCAPE_SECONDS = 300 s`, l'orologio che **ignora** le sequenze.
    ///
    /// ============================================================================================
    /// ⭐⭐ LA REGRESSIONE DEL 16/10, E LE DUE GUARDIE NUOVE. *Questa* e' la parte da leggere.
    ///
    /// **IL FATTO.** La sessione G1 di questa consegna (`output_log G1.txt`, filtrato) mostra il fusibile
    /// scattare **mentre nessuno era bloccato**:
    ///
    ///   riga 1386  `[Respawn] stato t=107.27 daMorte=0.00 campione=1 unityAnyKey=True … blockers=(nessuno)`
    ///              ⇒ il giocatore muore. **Il boss e' VIVO** (`[EnemyAnim] …ElderBrother … vita=220` alle
    ///              righe 2052-2060, cioe' **a 44 s dalla morte**).
    ///   riga 1417  `[Respawn] ascolto dei tasti ARMATO (nessuna sequenza in corso)`
    ///   righe 1412→2043  44 campioni con **`unityAnyKey=False`** ⇒ **nessuno ha premuto niente**.
    ///   riga 2061  `[CorpseFollow] finestra chiusa: disconnessione / fine sessione` (a 45.01 s)
    ///   riga 2064  `[Respawn] RESPAWN CHIESTO: FUSIBILE scattato: 45.01s da morto … via=blockers=(DEAD_SCREEN)`
    ///   riga 2110  `---> LevelManager:  Load and activate level D17Z01S05`  ⇒ **IL LIVELLO E' STATO
    ///              RICARICATO**: il giocatore e' tornato allo spawn, il boss (`vita=220`) e' sparito con la
    ///              scena, e **la cutscene della sua morte non e' mai arrivata**.
    ///
    /// ⇒ **Il fusibile ha respawnato uno spettatore, e ha distrutto la cosa che stava guardando.** E' il
    /// verbatim dell'utente: *"il timer non si deve attivare fino a quando non viene effettuato il `kill()`,
    /// altrimenti mi riporti il giocatore allo spawn … e se vedi perde la cutscene"*. E la perdita non e'
    /// solo estetica: **impedisce di testare i tasti bloccati**, perche' il load chiude la finestra prima.
    ///
    /// **GUARDIA 1 — IL FUSIBILE NON SI ARMA PRIMA DEL `kill()` DEL BOSS.** Il segnale esiste gia' nel
    /// progetto e **non se ne inventa uno nuovo**: `EnemySyncManager` lo conta in **entrambi** i punti in cui
    /// il `kill()` di un boss avviene, e sono gli stessi due che il progetto gia' conosce —
    /// (a) `ApplyBossDeath` (`boss.Kill()`, l'annuncio `bdead|` dell'altra macchina) e (b) il ramo
    /// `IsBoss(enemy, id)` del ciclo di invio (`[BossDeath TX]`), cioe' il boss ucciso **da questa**
    /// macchina. Si legge `EnemySyncManager.BossKillSignal`.
    ///
    ///   ⚠️ **E NON BASTAVA UN BOOLEANO**, che e' la trappola di questa consegna: nel caso del guasto il
    ///   `kill()` avviene **mentre il giocatore e' GIA' morto**. Un `bool` acceso prima della morte direbbe
    ///   "gia' armato" e il fusibile **non scatterebbe mai** nella finestra in cui serve. Quindi si registra
    ///   **il valore all'apertura della finestra** (`_killSignalAtOpen`) e ci si arma solo quando il valore
    ///   **cresce**: un `kill()` avvenuto prima della morte e' **invisibile** a questa finestra, e uno
    ///   avvenuto durante la morte la arma **in quel momento** — esattamente cio' che il mandato chiede.
    ///
    /// **GUARDIA 2 — FINCHE' UNA SEQUENZA E' IN CORSO, IL FUSIBILE NON CONTA.** E' la guardia che risponde
    /// al punto piu' importante del mandato: *"il fusibile non deve far perdere la cutscene"*. Il conteggio
    /// del fusibile viene **azzerato** (`_killSignalAt`, non `_deadSince`, cosi' non si tocca la misura
    /// `daMorte=`) mentre e' acceso **uno qualunque** dei blocker di scena — `CINEMATIC`, `FADE`,
    /// `BossBlock`, `cutscene_block`, `POP_UP`, `popup_block`, `DIALOG`, `UIBLOCKING`, `CUTSCENE`,
    /// `END_BOSS_DEFEATED` — e riparte da zero quando la scena e' finita.
    ///
    ///   ⚠️ **PERCHE' NON BASTAVANO `CINEMATIC`/`DIALOG`:** la cutscene della morte del boss **non e' ancora
    ///   partita** a 45 s, quindi i due blocker che c'erano non coprivano la finestra. E la coppia che il
    ///   referto del guasto misura accesa durante quella cutscene e' **`FADE` + `cutscene_block`**, non
    ///   `CINEMATIC`. ⇒ la tabella e' piu' larga di prima, e ogni nome in piu' e' nel file perche' e' stato
    ///   **letto** in un log o in `_il`, non indovinato.
    ///
    ///   ⚠️ **E NON CI SONO `DEAD_SCREEN` NE' `PLAYER_LOGIC` NE' `BLOCK_UNTIL_FPS_STABLE` in quella
    ///   tabella, ed e' deliberato**: `DEAD_SCREEN` e' acceso **per tutta** la finestra di morte (e' la
    ///   schermata di morte), quindi metterlo li' **spegherebbe il fusibile per sempre**; gli altri due sono
    ///   transitori e non significano "una sequenza sta girando" (accendere `PLAYER_LOGIC` per una posa di
    ///   animazione non deve poter disarmare la rete).
    ///
    /// **E LA RETE RESTA UNA RETE.** Se una cutscene fosse **davvero appesa** (e' il caso della sessione del
    /// guasto: `Called fade function. Active: True` senza il suo `False`), rinviare all'infinito
    /// significherebbe **non uscire piu'**: quindi c'e' `MAX_ESCAPE_SECONDS = 300 s` di **orologio**, che
    /// **non guarda le sequenze** e respawna comunque. E' l'unico numero nuovo, ed e' quello che tiene
    /// insieme le due richieste dell'utente: *"il fusibile deve restare come ultima spiaggia"* e *"non deve
    /// far perdere la cutscene"*. Trecento secondi stanno **tre volte** sopra la finestra di morte misurata
    /// (~102 s, bossfight + cutscene comprese) e sopra la finestra del guasto (~90 s), quindi non
    /// interferiscono con nessun caso reale, e restano un fusibile vero.
    ///
    /// ============================================================================================
    /// ⭐⭐ LA REGRESSIONE DEL 17/10 — *"respawnato appena il boss ha finito l'animazione di morte, ed
    /// ha perso la cutscene"* — E IL FUSIBILE CHE ASPETTA **LA FINE DELLA SEQUENZA**. *Questa* e' la
    /// parte nuova, e la guardia 1/2 qui sopra **non bastavano**.
    ///
    /// **IL FATTO, dai due log di questa sessione** (`output_log G1/G2`, filtrati):
    ///
    ///   | | G1 (spettatore, boss ucciso dal compagno) | G2 (vivo, uccide lui) |
    ///   |---|---|---|
    ///   | `kill()` del boss (il segnale) | `t=282.6` | `t≈271.9` |
    ///   | **animazione di morte** (nessuna scena) | `282.6 → 285.6` = **~3.0 s** | `271.9 → 276.98` = **~5.1 s** |
    ///   | `cutscene_block` acceso | `t=282.7` | `t=275.51` |
    ///   | `FADE` della scena | `t=285.58` | subito prima del `CINEMATIC` |
    ///   | `CinematicStarted` / cutscene | **mai arrivata** (respawn a `t=287.14`) | `t=276.98` |
    ///   | `VideoStarted` | — | `t=277.13` (`CTS02-Bloody Baptism`) |
    ///   | `VideoEnded` / `CinematicEnded` | — | `t=282.85` / `t=285.85` |
    ///   | finestra di morte | `80.10 s` (da `207.30`) | — |
    ///
    /// ⇒ **`sequenza=` era `si` per tutta l'animazione di morte** (`cutscene_block`, `[Respawn] stato` a
    /// `t=282.97…287.00`), quindi il predicato riconosceva la sequenza: **il difetto non e' li'**. Il
    /// difetto e' che il fusibile si considera *"un fusibile"* **appena il boss muore**, cioe' con
    /// **~3 s di anticipo sulla cutscene**: in quei 3 s il gioco sta **lavorando normalmente** (e' la
    /// sequenza scriptata di morte del boss) e **non c'e' niente da sbloccare**.
    ///
    /// **E LA DOMANDA DI FONDO E' PROPRIO QUESTA**: *quando ha senso che il fusibile esista?* Solo da
    /// **fine sequenza in poi**: da li' il gioco non sta piu' facendo niente per il giocatore morto, e se
    /// i tasti non arrivano **quello** e' il guasto. ⇒ **il fusibile si arma DOPO la sequenza**, non al
    /// `kill()`.
    ///
    /// **IL CRITERIO SCELTO — il segnale DENTRO il gioco, con un ripiego, e niente predicati inventati.**
    ///   1. **la via buona**: `Cinematics.CinematicStarted`/`CinematicEnded`, **gli eventi pubblici** che
    ///      `CutsceneProbe` gia' sottoscrive (`Cinematics.cs:85-91`). Il fusibile si arma **alla FINE
    ///      della cutscene** (`CinematicEnded`), e **non conta** finche' la scena gira (`_cinematicOn`).
    ///      ⇒ fra il `kill()` e la fine della scena **il fusibile non esiste**, che e' esattamente cio'
    ///      che il mandato chiede;
    ///   2. **il ripiego per un boss SENZA cutscene**: se dopo il `kill()` **nessuna cutscene e' partita**
    ///      entro `BOSS_DEATH_GRACE_SECONDS = 30 s` (misurato: la scena parte **3.0–3.9 s** dopo il
    ///      `kill()`, quindi 30 s stanno **7–10 volte** sopra la misura), la **sequenza di morte** e'
    ///      finita e il fusibile si arma **da li' in poi**. ⚠️ **E non si arma mai prima**: e' la scelta
    ///      conservativa che il mandato indica, e l'attesa ha il tetto dell'orologio a 300 s;
    ///   3. ⚠️ **la via scartata**: *azzerare il conteggio finche' "una sequenza e' in corso"* **non
    ///      basta**, ed e' il punto 2 del mandato: il fusibile **esiste** in quella finestra, e basta che
    ///      un blocker esca per un frame (o che la scena non sia ancora partita) perche' parta. Il
    ///      fusibile **non deve esistere** li': si arma quando la sequenza e' **finita**, non quando
    ///      "sembra in corso".
    ///
    /// ⛔ **E NON SI TOGLIE IL FUSIBILE** (divieto del mandato): resta — **15 s dalla fine della sequenza**
    /// (`ESCAPE_SECONDS`, 19/10: prima erano 45 s contati da un punto che includeva l'animazione di morte)
    /// — e resta l'orologio
    /// assoluto a **300 s** che ignora le sequenze, perche' una cutscene **appesa** non deve poter
    /// bloccare il giocatore per sempre.
    ///
    /// **E RETE 1 SMETTE DI ESSERE MUTA NELLA FINESTRA CHE CONTA.** Il suo guardiano era `!_sceneOn`, e
    /// `FADE`/`cutscene_block` sono accesi **prima** che la cutscene parta: misurato in `G1` (`FADE`
    /// acceso a `t=285.58`, `_sceneOn=si`, e la mod **non** ha chiesto il respawn → **`[Respawn]
    /// RESPAWN CHIESTO` NON compare** nel log, mentre il livello e' stato ricaricato lo stesso a
    /// `t=287.14` con `Penitent.Respawn()` invocato — `[Morte] il giocatore e' morto` a riga 3196, che e'
    /// **il `Prefix` di `Respawn`**). ⇒ il guardiano ora e' **`_cinematicOn`**, cioe' "la cutscene e'
    /// **davvero** in corso" (`CinematicStarted` acceso): **fuori** dalla cutscene i tasti tornano a
    /// contare, dentro la cutscene no. Cosi' il caso normale (l'ascolto dei tasti) **non e' piu'
    /// disarmato** nel momento in cui il gioco si sta preparando a mostrare la scena.
    ///
    /// ⚠️ **COSA QUESTO FIX NON PUO' IMPEDIRE** *(dichiarato)*: la schermata di morte **del gioco**
    /// (`DeadScreenWidget.OnDeadAction:87-96`) respawna **al primo tasto** con la sua stessa catena
    /// (`FADE` 1.5 s → `Core.Logic.Penitent.Respawn()`) e **non passa da questa mod**: se il tasto arriva
    /// **prima** che la cutscene parta, la scena si perde **comunque**, ed e' la ricostruzione di `G1`.
    /// Nessuna via di questa consegna puo' cambiarlo (servirebbe una patch su `DeadScreenWidget`, che e'
    /// **la zona vietata**). ⇒ nel test: **non premere niente** durante l'animazione di morte del boss.
    ///
    /// ============================================================================================
    /// ⭐⭐ LA TERZA REGRESSIONE, DEL 18/10 — **LA PORTA DEI TASTI**, E L'AVVISO A SCHERMO.
    /// *Questa* e' la parte nuova, e la guardia di `RETE 1` scritta qui sopra l'aveva aperta.
    ///
    /// **IL FATTO, e non e' un'ipotesi.** Il fix del 17/10 ha cambiato il guardiano di `RETE 1` da
    /// `!_sceneOn` a **`!_cinematicOn`**, e ha fatto bene: `FADE`/`cutscene_block` si accendono **prima**
    /// che la scena parta, quindi con `!_sceneOn` la rete era **muta** nella finestra che conta. ⚠️ **Ma
    /// `_cinematicOn` e' vero solo da `CinematicStarted`**, cioe' **dopo** l'animazione di morte del boss:
    /// ⇒ in tutta la finestra `kill()` → `CinematicStarted` (**~3.0 s** in `G1`, **~3.9 s** in `G2`) i
    /// tasti **tornavano a contare**, e un tasto premuto li' faceva respawnare **la mod** — lo stesso
    /// guasto di `G1`, **da un'altra porta**.
    ///
    /// **LA CHIUSURA — il predicato "sequenza in corso" RIUSATO, non reinventato.** `RETE 1` ora usa
    /// **lo stesso guardiano del fusibile** (`_waitingSequence`, cioe' "il `kill()` e' arrivato e la
    /// sequenza non e' conclusa", piu' `_cinematicOn` per la scena davvero in corso):
    ///
    ///   | finestra | predicato | `RETE 1` |
    ///   |---|---|---|
    ///   | morte normale, boss vivo | **falso** | **legge i tasti** (come sempre) ⭐ |
    ///   | `kill()` → `CinematicStarted` (**l'animazione di morte**) | **vero** | **muta** ⭐ *(era accesa: e' la porta chiusa qui)* |
    ///   | cutscene in corso | **vero** | **muta** (la scena non si interrompe) |
    ///   | dopo la fine della sequenza | **falso** | **legge i tasti** (e il fusibile conta) ⭐ |
    ///
    /// ⛔ **E NON E' UNA MUTILAZIONE DELLA RETE**: `RETE 1` e' il ripiego per una schermata di morte
    /// **rotta**, e nel caso normale i tasti li legge **il gioco**; fuori dalla sequenza la rete e'
    /// **identica a prima**, e la riga di stato lo dice in ogni campione col campo `tasti=`
    /// (`ignorati(sequenza-boss)` · `ignorati(cutscene)` · `attesa(armamento)` · `attivi` · `respawN`).
    ///
    /// ⚠️ **E LA SECONDA DOMANDA — si puo' impedire alla schermata di morte DEL GIOCO di respawnare?**
    /// **NO, e va detto chiaro**: la catena e' `while (!Input.anyKey)` → `FADE(1.5 s)` → `Respawn()`
    /// (`DeadScreenWidget.cs:87-96`). La mod **non puo' bloccare l'input** (divieto), **non puo'
    /// "consumare" il tasto** (`Input.anyKey` e' una lettura diretta del dispositivo, e non esiste nessuna
    /// API per svuotarla) e **non puo' patchare la schermata di morte** (zona vietata). ⇒ *premendo un
    /// tasto durante l'animazione di morte del boss, la schermata del GIOCO respawna e la cutscene si
    /// perde, e la mod non puo' impedirlo.*
    ///
    /// ⭐ **IL COMPROMESSO MIGLIORE CHE RISPETTA I DIVIETI, ed e' implementato**: un **avviso a schermo**
    /// al giocatore morto nel momento esatto del `kill()` — *"NON PREMERE: il boss sta morendo - la
    /// cutscene sta per partire"* — via `NotificationManager`, che e' **della mod** (nessuna UI del gioco
    /// toccata, nessuna patch). E' l'unica leva che resta: non impedisce il respawn del gioco, ma
    /// **toglie la causa** (il tasto non voluto). L'avviso e' **uno per finestra**, dura
    /// `config.notificationDisplaySeconds` (4 s di default: si spegne prima che la scena parta), e si
    /// spegne con `WARN_PLAYER_ON_BOSS_DEATH = false` se da' fastidio.
    ///
    /// ⚠️ **LE DUE CONDIZIONI CHE ARMANO LA RETE 1**, e non sono pignoleria.
    ///   - `KEY_ARM_SECONDS = 2.5 s`: il tasto che ha **ucciso** il giocatore (o quello premuto mentre
    ///     moriva) non deve essere riletto come "voglio respawnare". La schermata di morte del gioco non ha
    ///     questo problema perche' guarda `Input.anyKey` — che e' vero **mentre** il tasto e' tenuto giu',
    ///     mentre qui si guarda la **pressione** (`GetKeyDown`), che e' un fronte.
    ///   - `CINEMATIC` spento e nessun dialogo: muoversi mentre una cutscene gira e' il secondo guasto, non
    ///     una riparazione.
    ///
    /// ⚠️ LA SCELTA DEL MOMENTO DI "SPEGNIMENTO" DEL CADAVERE-SEGUACE. Si chiude la finestra di
    /// `DeathCorpseFollow` **prima** di invocare `Respawn()`, e non per estetica: quell'inseguimento ha
    /// spostato il Penitent addosso al vivo (e' invisibile), quindi lo si riporta **al punto di morte**
    /// (`Teleport`) mentre la scena e' ancora quella giusta, invece di lasciare che sia il load a scoprirlo.
    /// Se `Respawn()` lanciasse, il cadavere e' comunque gia' ricomposto e il giocatore si ritrova morto
    /// **dov'e' caduto** — il difetto noto, non uno nuovo.
    ///
    /// ⚠️ LA PROBE, e perche' sta qui. Il mandato lo chiede: *"quali blocker sono accesi al momento in cui i
    /// tasti non rispondono?"*. La probe #0 (`[Cutscene BLK]`) logga solo `CINEMATIC` + `InputBlocked`, e
    /// nella sessione del guasto **non e' bastata**: `DEAD_SCREEN` e `FADE` erano accesi e nessuna riga lo
    /// diceva. Qui, **una riga al secondo mentre si e' morti**, si legge TUTTO quello che serve a chiudere
    /// la diagnosi alla prossima sessione:
    ///   - `unityAnyKey` — `UnityEngine.Input.anyKey` letto **in proprio** (quello che legge la schermata
    ///     di morte): distingue "il gioco non legge i tasti" da "i tasti non arrivano a Unity";
    ///   - `blockers=` — l'elenco VERO, ricostruito per differenza sulle transizioni di `SetBlocker`
    ///     (l'API pubblica non espone la lista: `HasBlocker` e' pubblico, `inputBlockers` no). Serve la
    ///     **transizione**, non lo stato, perche' `SetBlocker` logga solo al cambio;
    ///   - `gameplayUIActive` / `fadeUIActive` — ⭐ la gerarchia: se una di queste due e' `False`, i
    ///     **coroutine** della schermata di morte e del fade sono in pausa (Unity li ferma con il
    ///     `MonoBehaviour`), e quella e' la spiegazione meccanica di "i tasti non fanno niente";
    ///   - `deadScreenAttivo` — ⭐ `activeInHierarchy` del widget, che `DeathScreenProbe` **non** leggeva:
    ///     `FindWidget` usa `Resources.FindObjectsOfTypeAll`, che restituisce **anche gli inattivi**, quindi
    ///     "widget=1" nel log precedente **non** voleva dire "la schermata di morte e' viva";
    ///   - `respawnVisti` — quante volte il `Prefix` di `Respawn()` ha visto una richiesta in questa
    ///     finestra (letta da `PlayerDeathSync`), cosi' "il gioco ha provato e non e' riuscito" e "nessuno
    ///     ha mai provato" restano separabili.
    ///
    /// I DIVIETI, riga per riga.
    ///   - ⛔ `Core.Input.SetBlocker`: **zero occorrenze**. `CINEMATIC` si legge e basta (come in
    ///     `DeathCorpseFollow`).
    ///   - ⛔ `Core.Input.ResetManager()`: **zero occorrenze**.
    ///   - ⛔ `DEAD_SCREEN`: **zero occorrenze**, nemmeno in lettura come scrittura. E' del gioco.
    ///   - ⛔ `Time.timeScale` / `TimeScale`: zero. Si usa `Time.unscaledTime`, perche' un respawn deve
    ///     poter scattare anche a tempo fermo.
    ///   - ⛔ `SetActive`: zero.
    ///   - ⛔ nessuna patch Harmony nuova: si aggancia al ciclo che gira gia' (`Multiplayer.OnLateUpdate`).
    ///   - ✅ `try/catch` in ogni punto di ingresso: `Tick()` e' l'unico, e ha il suo.
    /// </summary>
    internal static class DeathRespawnWatchdog
    {
        /// <summary>Marker di ESITO. Chi legge deve poter dire chi ha parlato senza indovinare.</summary>
        private const string MARK = "[Respawn]";

        /// <summary>L'ultima riga di difesa, come in `CutsceneProbe`/`DeathCorpseFollow`.</summary>
        private const string MARK_FAIL = "[Respawn ERR]";

        /// <summary>
        /// Da quando la schermata di morte e' stabile si ascoltano i tasti. Vedi il commento di classe:
        /// e' la distanza dal tasto che ha ucciso.
        /// </summary>
        private const float KEY_ARM_SECONDS = 2.5f;

        /// <summary>
        /// ⭐⭐ **IL PRIMO DEI DUE NUMERI (19/10): IL CONTEGGIO DOPO LA FINE DELLA SEQUENZA — 45 s → 15 s.**
        ///
        /// ⚠️ **E' UN CONTO CHE PARTE SOLO A SEQUENZA CONCLUSA** (fix `0393fd2`, non toccato): fra il
        /// `kill()` e la fine della cutscene il fusibile **non esiste** (`disarmato(attesa-fine-sequenza)`),
        /// quindi questo numero **non ha niente a che vedere con l'animazione di morte del boss** — non la
        /// puo' interrompere per costruzione.
        ///
        /// **PERCHE' 15 E NON 45** — *"a scena finita il gioco non sta piu' lavorando"* (il mandato):
        ///   - ⭐ **il gioco ha SMESSO**: `CinematicEnded` e' arrivato, i blocker di scena sono caduti, il
        ///     boss e' a terra, il mondo e' fermo. Da li' in poi **l'unica cosa che deve succedere e' che il
        ///     giocatore torni**;
        ///   - ⭐ **e il tasto lo copre gia' `RETE 1`**: e' provato su questo log che
        ///     `UnityEngine.Input.GetKeyDown` **legge comunque** con l'input bloccato (in `G1` le righe
        ///     2894/2898/2905/2906/2916 hanno `unityAnyKey=True` **con `blockers=(DEAD_SCREEN,FADE)`**);
        ///     quindi il fusibile non e' la rete principale, e' **la rete di chi NON preme**;
        ///   - ⚠️ **45 s erano tarati su una finestra piu' larga di quella vera**: il conteggio partiva al
        ///     `kill()` e includeva l'animazione di morte. Adesso che parte **dalla fine della scena**, 15 s
        ///     misurano il tempo in cui il gioco **non sta piu' facendo niente**;
        ///   - **e restano un fusibile**: nessun caso reale misurato ha mai avuto bisogno di piu' di pochi
        ///     secondi dopo la scena, e chi e' bloccato davvero aspetta **30 + 15 = 45 s** dall'inizio della
        ///     sequenza (prima erano 30 + 45 = 75 s);
        ///   - **e l'ultima spiaggia non cambia**: `MAX_ESCAPE_SECONDS = 300 s` (l'orologio che ignora le
        ///     sequenze) e' **intatto**, quindi una cutscene appesa non puo' bloccare il giocatore per sempre.
        /// </summary>
        private const float ESCAPE_SECONDS = 15f;

        /// <summary>
        /// ⭐ L'ULTIMA SPIAGGIA VERA, e **l'unico numero nuovo di questa consegna**. E' un orologio che
        /// **non guarda le sequenze**: se la finestra dura davvero tanto (una cutscene **appesa**, che e'
        /// il caso misurato nella sessione del guasto: `Called fade function. Active: True` e il suo `False`
        /// mai arrivato), rinviare all'infinito vorrebbe dire **non uscire piu'**. Trecento secondi stanno
        /// **tre volte** sopra la finestra di morte misurata (~102 s) e sopra quella del guasto (~90 s),
        /// quindi non toccano nessun caso reale e restano un fusibile.
        /// </summary>
        private const float MAX_ESCAPE_SECONDS = 300f;

        /// <summary>
        /// ⭐⭐ **IL SECONDO DEI DUE NUMERI (19/10): LA GRAZIA PRIMA — 30 s, ed e' la richiesta esplicita
        /// dell'utente** (*"magari aggiungendo un 30 secondi di attesa che parta una cutscene"*).
        ///
        /// ⚠️ **E' GIA' QUELLO CHE C'ERA** (`0393fd2`), quindi questa consegna **non lo cambia: lo conferma**
        /// — ed e' il punto: era stato scelto come **ripiego per un boss SENZA cutscene**, e la misura che lo
        /// giustifica e' la stessa che l'utente cita a occhio. Se dopo il `kill()` **nessuna cutscene e'
        /// partita** entro questo tempo, la *sequenza di morte* di quel boss e' conclusa e il fusibile si
        /// arma da li' in poi.
        ///
        /// ⚠️ **E' UNA MISURA, NON UN INDOVINELLO** — la finestra misurata `kill()` → `CinematicStarted` e'
        /// **~3.0 s** (`G1`) e **~3.9 s** (`G2`): **trenta secondi stanno 7–10× sopra la misura**, che e'
        /// esattamente il margine che il mandato chiede, e un boss con una scena lenta non viene mai
        /// scambiato per un boss senza scena.
        ///
        /// ⛔ **E NON SI ACCORCIA**: e' il numero che protegge la cutscene da un `kill()` senza scena, e
        /// **accorciarlo e' l'unico modo di far ripartire la regressione del 17/10** (il fusibile armato
        /// mentre il boss finisce l'animazione di morte). **Il numero da accorciare era l'ALTRO**
        /// (`ESCAPE_SECONDS`, vedi li').
        /// </summary>
        private const float BOSS_DEATH_GRACE_SECONDS = 30f;

        /// <summary>
        /// ⭐ L'AVVISO A SCHERMO AL GIOCATORE MORTO, e **l'unico rimedio lecito** alla porta che la mod
        /// **non puo' chiudere**: la schermata di morte **del gioco** respawna al primo tasto con
        /// `while (!Input.anyKey)` → `FADE(1.5 s)` → `Penitent.Respawn()` (`DeadScreenWidget.cs:87-96`), e
        /// **non passa da questa mod**. La mod non puo' bloccare l'input (divieto), non puo' "consumare" il
        /// tasto (`Input.anyKey` e' la lettura diretta del dispositivo) e non puo' patchare quella schermata
        /// (zona vietata). ⇒ **l'unica cosa che resta e' togliere la causa**: dire al giocatore, nel momento
        /// in cui il boss muore, che **non deve premere niente**.
        ///
        /// E' l'avviso che il mandato chiede di valutare, ed e' **acceso**: usa `NotificationManager`
        /// (**della mod**: nessuna UI del gioco, nessuna patch), esce **una volta per finestra**, e con
        /// `false` qui si spegne tutto senza toccare nient'altro.
        /// </summary>
        private const bool WARN_PLAYER_ON_BOSS_DEATH = true;

        /// <summary>
        /// La distanza fra due tentativi del fusibile. Non e' pignoleria: `Respawn()` fa un **load di
        /// livello**, che non e' istantaneo, e ritentarlo a ogni frame sarebbe una richiesta di load per
        /// frame. Dieci secondi stanno larghi su un load vero e restano un fusibile vero.
        /// </summary>
        private const float ESCAPE_RETRY_SECONDS = 10f;

        /// <summary>
        /// ⭐⭐ LA GRAZIA DELLO STATO INCOERENTE, in secondi NON scalati (19/10). E' il tempo che si lascia
        /// passare **fra il riconoscimento di "vivo ma con `DEAD_SCREEN` acceso" e il tentativo lecito**
        /// (`Penitent.Respawn()`), e non e' pignoleria: quello stato nasce **durante un load di livello**
        /// (`LevelManager` → `SpawnPlayerOnLevelLoad` → `CreatePlayer` → il Penitent e' un altro oggetto),
        /// e chiedere un secondo load mentre il primo non e' finito significa chiamare `ChangeLevel` dentro
        /// `ChangeLevel` — che il gioco rifiuta con
        /// `"Calling a ChangeLevel when a change level is in progress"` (`LevelManager.cs:135`).
        /// **Cinque secondi stanno sopra qualunque load misurato** (il load di `G1` da
        /// `Load and activate level` a `OnLevelLoaded` e' ~1 s) **e restano corti per un giocatore fermo**.
        /// ⚠️ **E IL TENTATIVO E' UNO SOLO PER EPISODIO** (`_incoherentRespawned`): ripetere i load sarebbe
        /// **peggio** dello stato, perche' **un load e' esattamente cio' che lo produce**.
        /// </summary>
        private const float INCOHERENT_GRACE_SECONDS = 5f;

        /// <summary>
        /// ⭐ I BLOCKER CHE SIGNIFICANO "UNA SEQUENZA STA GIRANDO SU QUESTA MACCHINA". Finche' uno di questi
        /// e' acceso, il fusibile **non conta** (guardia 2): e' la guardia che impedisce al respawn di
        /// ricaricare il livello mentre una cutscene scorre o sta per partire, che e' la seconda richiesta
        /// dell'utente.
        ///
        /// ⚠️ **OGNI NOME E' QUI PERCHE' E' STATO LETTO**, non indovinato: `CINEMATIC`/`FADE`/`DIALOG`/
        /// `POP_UP`/`BossBlock`/`cutscene_block`/`CUTSCENE`/`UIBLOCKING` compaiono nei log di sessione
        /// (G1 di questa consegna: `BossBlock` a :885-920, `UIBLOCKING` a :2374-2470, `FADE`/`INTERACTABLE`/
        /// `DOOR` a :734-792; la sessione del guasto: `cutscene_block` acceso a :3063 e spento a :3200), e
        /// gli altri vengono dagli stessi nomi di `SetBlocker` raccolti in `KnownBlockers`.
        ///
        /// ⛔ **E NON C'E' `DEAD_SCREEN`**: e' acceso **per tutta** la finestra di morte (e' la schermata),
        /// quindi metterlo qui **disarmerebbe il fusibile per sempre** — l'opposto dello scopo.
        /// ⛔ Ne' `PLAYER_LOGIC` (una posa di animazione non deve poter disarmare la rete) ne'
        /// `BLOCK_UNTIL_FPS_STABLE` (transitorio, non e' una sequenza) ne' `MULTIPLAYER`/`TUTORIAL`/
        /// `INTERACTABLE`/`DOOR`/`EXECUTION` (non sono la cutscene che si perde).
        /// </summary>
        private static readonly string[] SceneBlockers = new string[]
        {
            "CINEMATIC", "FADE", "BossBlock", "cutscene_block", "CUTSCENE",
            "POP_UP", "popup_block", "DIALOG", "UIBLOCKING", "END_BOSS_DEFEATED"
        };

        /// <summary>Cadenza della riga di stato. Un secondo, non un frame: e' una misura, non uno spam.</summary>
        private const float SAMPLE_INTERVAL = 1f;

        /// <summary>
        /// I tasti che interessano, in una tabella fissa: `Enum.GetValues` allocherebbe un array a ogni
        /// chiamata, e questo codice gira per frame.
        /// </summary>
        private static readonly KeyCode[] Keys = new KeyCode[]
        {
            KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape,
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.E, KeyCode.Q, KeyCode.R, KeyCode.F,
            KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.Tab, KeyCode.Backspace,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4
        };

        // ---- stato della finestra. Nessuno di questi campi sopravvive a una finestra chiusa.
        private static bool _dead;
        private static float _deadSince;
        private static float _nextSample;
        private static int _samples;
        private static bool _keyArmed;
        private static bool _fuseUsed;
        private static int _attempts;
        private static float _nextAttempt;

        // ---- ⭐⭐ LO STATO INCOERENTE (19/10). Vedi `IncoherentState()` e `TickAlive()`: sono la memoria
        //      dell'episodio "vivo ma con `DEAD_SCREEN` acceso", che il watchdog **non poteva vedere**
        //      perche' la sua finestra esiste solo da morto.
        private static bool _incoherent;
        private static float _incoherentSince;
        private static bool _incoherentReported;
        private static bool _incoherentRespawned;
        private static float _incoherentNextAttempt;

        // ---- ⭐ GUARDIA 1: il `kill()` del boss. `_killSignalAtOpen` e' il valore del contatore
        //      ALL'APERTURA della finestra: un `kill()` avvenuto PRIMA della morte non conta (e sarebbe il
        //      modo sbagliato di leggere un booleano), uno avvenuto DURANTE la morte arma il fusibile in
        //      quel momento. E' il criterio dell'utente: *"il timer non si deve attivare fino a quando non
        //      viene effettuato il kill()"*.
        private static int _killSignalAtOpen;
        private static int _killSignal;

        /// <summary>
        /// ⭐ GUARDIA 2: l'istante da cui si conta il fusibile. Parte dall'apertura della finestra e viene
        /// **riportato a `now` a ogni frame in cui una sequenza e' in corso**: cosi' il conteggio riparte da
        /// zero quando la scena finisce, e **una cutscene non viene mai interrotta dal respawn**.
        /// ⚠️ E' un campo **separato** da `_deadSince` di proposito: `_deadSince` e' la misura (`daMorte=`)
        /// e non va toccata, o il log mentirebbe su da quanto si e' morti.
        /// </summary>
        private static float _fuseSince;
        private static bool _armed;
        private static bool _sceneOn;
        private static bool _disarmedReported;

        // ---- ⭐⭐ LA SEQUENZA DEL BOSS, MISURATA DAGLI EVENTI DEL GIOCO (non da un predicato inventato).
        //      `_cinematicOn` e' "la cutscene e' DAVVERO in corso" (`Cinematics.CinematicStarted` acceso e
        //      non ancora chiuso da `CinematicEnded`): e' la verita' di "una scena sta girando", ed e' il
        //      guardiano che sostituisce `!_sceneOn` su RETE 1 (vedi il commento di classe: `FADE` e
        //      `cutscene_block` si accendono PRIMA che la scena parta).
        private static bool _cinematicOn;

        // ---- ⭐ IL FUSIBILE NON ESISTE FRA IL `kill()` E LA FINE DELLA SEQUENZA: `_killSeen` e' "il
        //      `kill()` del boss e' arrivato DOPO questa morte", `_waitingSequence` e' "il fusibile e'
        //      ancora da armare perche' la sequenza non e' conclusa", `_sequenceEnded` e' "la cutscene e'
        //      finita", `_bossDeathAt`/`_bossDeathFuseAt` sono i due istanti da cui si decide (la morte del
        //      boss e la fine della scena), e `_armReason` e' **perche'** si e' armato — cosi' il marker lo
        //      dice invece di lasciarlo indovinare.
        private static bool _killSeen;
        private static bool _waitingSequence;
        private static bool _sequenceEnded;
        private static float _bossDeathAt;
        private static float _bossDeathFuseAt;
        private static string _armReason = string.Empty;

        // ---- ⭐⭐ LA PORTA DEI TASTI (18/10). `_keysRespawns` conta le volte in cui RETE 1 ha chiesto il
        //      respawn (e' il valore che la riga di stato stampa in `tasti=respawN`), `_keysMuted` ricorda
        //      se l'ultimo giro l'ha vista **muta** (serve alla riga di RIARMO, una per sequenza) e
        //      `_warnedThisWindow` tiene l'avviso a schermo a **uno per finestra di morte**.
        private static int _keysRespawns;
        private static bool _keysMuted;
        private static bool _keyIgnoredReported;
        private static bool _warnedThisWindow;

        // ---- la sottoscrizione agli eventi pubblici di `Cinematics` (zero patch Harmony).
        private static bool _subscribed;
        private static bool _subscribePending;

        // ---- lo stack dei blocker, ricostruito per differenza dalle transizioni di `SetBlocker`.
        private static readonly System.Collections.Generic.List<string> Blockers =
            new System.Collections.Generic.List<string>();

        // ---- una riga sola per finestra quando il Penitent non e' disponibile.
        private static bool _goneReported;

        // ============================================================================================
        // ⭐⭐ LO STATO INCOERENTE (19/10) — "VIVO, MA CON LA SCHERMATA DI MORTE ANCORA ADDOSSO".
        // ============================================================================================

        /// <summary>
        /// ⭐ **IL PREDICATO DELLO STATO INCOERENTE, e sono DUE FONTI DEL GIOCO CHE SI CONTRADDICONO.**
        ///
        ///   `!PlayerDeathSync.IsLocalPlayerDead()`   ⇒ il gioco dice **VIVO** (`Penitent.Status.Dead` falso)
        ///   `Core.Input.HasBlocker("DEAD_SCREEN")`   ⇒ il gioco dice **MORTO** (la schermata di morte e'
        ///                                               accesa e l'input e' bloccato)
        ///
        /// ⚠️ **E' ESATTAMENTE LO STATO CHE L'UTENTE HA VISTO** (*"quando torna in vita in questa maniera
        /// non fa l'animazione di respawn, quindi ha ancora il prompt di tasto da premere buggato"*), ed e'
        /// **misurabile**: `DEAD_SCREEN` e' acceso da `DeadScreenWidget.OnDeadAction:78` e lo spegne
        /// **solo** il gioco, in `OnPenitentReady:101` — cioe' **solo se quella schermata riceve
        /// `SpawnManager.OnPlayerSpawn`** (`DeadScreenWidget.cs:58`). Se il livello viene ricaricato mentre
        /// il PENITENT e' un altro oggetto (o il widget della scena vecchia non riceve piu' l'evento),
        /// **il giocatore e' vivo e il blocker resta**.
        ///
        /// ⛔ **E NON SI TOCCA**: `HasBlocker` e' solo **LETTO** (e' `public`, `InputManager.cs:210-213`,
        /// verificato in IL: `inputBlockers.Contains(name)` — nessun side-effect). Questo metodo **non
        /// chiama `SetBlocker`, non chiama `ResetManager`, non tocca la schermata**: riporta e basta.
        /// </summary>
        private static bool IncoherentState()
        {
            if (PlayerDeathSync.IsLocalPlayerDead())
                return false;

            try
            {
                InputManager input = Core.Input;
                return input != null && input.HasBlocker("DEAD_SCREEN");
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// ⭐⭐ UN GIRO PER FRAME **DA VIVI**, e serve a una cosa sola: **dire che lo stato incoerente
        /// esiste, e che questa rete e' strutturalmente cieca**.
        ///
        /// ⚠️ **IL WATCHDOG NON PUO' REPARARLO, ed e' la risposta alla domanda del mandato**: tutta la sua
        /// logica vive dentro `if (!dead) { ... return; }` e `dead` e'
        /// `Core.Logic.Penitent.Status.Dead` → **falso** appena il load ricostruisce il Penitent. Quindi
        /// *"il watchdog non e' scattato"* **non e' un guasto del watchdog**: e' che **per il gioco il
        /// giocatore e' vivo**, e il watchdog e' una rete **per il morto**.
        ///
        /// ⇒ **E LE VIE D'USCITA LECITE SONO TRE, TUTTE VERIFICATE IN IL, E NESSUNA FA CONGEDARE LA
        /// SCHERMATA** *(quindi non si implementa nessuna delle tre: sarebbe un fix sporco in zona morte)*:
        ///
        ///   | via | firma verificata in `_il` | cosa fa | perche' NON basta |
        ///   |---|---|---|---|
        ///   | `Penitent.Respawn()` | `public void` (`Penitent.cs:310-315`) | load di livello ⇒ `LevelManager:355` `ResetManager()` ⇒ **il blocker cade dal GIOCO** | **non congeda la schermata**: la fa sparire solo perche' **ricarica il livello**, cioe' **la stessa cosa che ha creato lo stato** (e' il loop) |
        ///   | `Penitent.Status.Dead = true` | campo **pubblico** (`Entity.Status` → `EntityStatus.Dead`) | rientra in `Penitent.OnUpdate:482-498` e rilancia `OnDead` | ⇒ `DeadScreenWidget.OnDead` parta **di nuovo** la coroutine della schermata su un widget che sta gia' mostrandola: **nessun congedo**, solo un'altra attesa di tasto (e `HideAll()` non e' mai chiamata) |
        ///   | forzare il congedo della schermata | `HideAll()` e' **privata** (`DeadScreenWidget.cs:208`) | — | servirebbe una patch su `DeadScreenWidget` (**zona vietata**) o `Core.Input.ResetManager()` (**vietato**: azzera **tutti** i blocker) |
        ///
        /// ✅ **E L'UNICA VIA CHE ESISTE DAVVERO E' GIA' QUELLA CHE C'E'**: `RETE 1` legge
        /// `UnityEngine.Input.GetKeyDown` **in proprio**, e il ciclo per frame della mod gira **anche**
        /// quando il gioco non legge piu' i tasti ⇒ **al primo tasto vero la mod chiama `Respawn()`**
        /// (`DeathRespawnWatchdog.Respawn`), che fa il load e il load rimette a posto **dal lato gioco**
        /// (`ResetManager`, il fade e `DEAD_SCREEN`). ⚠️ **Ed e' provato su questo log, non dedotto**: in
        /// `G1` le righe 2894/2898/2905/2906/2916 hanno `unityAnyKey=True` **con
        /// `blockers=(DEAD_SCREEN,FADE)` accesi** → **il tasto ARRIVA a Unity anche con l'input bloccato**.
        /// ⇒ **il caso "bloccato vero" e' quello che `RETE 1` copre, e per questo il fusibile e' solo la
        /// rete di chi NON preme.**
        private static void TickAlive()
        {
            bool incoherent = IncoherentState();

            if (!incoherent)
            {
                if (_incoherent)
                {
                    _incoherent = false;
                    ModLog.Info(MARK + " STATO INCOERENTE FINITO dopo " +
                                F(Time.unscaledTime - _incoherentSince) + "s: il blocker `DEAD_SCREEN` non " +
                                "e' piu' acceso e il giocatore e' vivo: le due fonti del gioco tornano " +
                                "d'accordo" + (_incoherentRespawned
                                    ? " · ed e' finito con un RESPAWN CHIESTO da questa rete"
                                    : " · non e' finito per un'azione di questa rete"));
                }
                return;
            }

            float now = Time.unscaledTime;

            if (!_incoherent)
            {
                _incoherent = true;
                _incoherentSince = now;
                _incoherentReported = false;
                _incoherentRespawned = false;
                _incoherentNextAttempt = now + INCOHERENT_GRACE_SECONDS;
            }

            if (!_incoherentReported)
            {
                _incoherentReported = true;
                ModLog.Warn(MARK + " STATO INCOERENTE: il giocatore e' VIVO e il blocker `DEAD_SCREEN` e' " +
                            "ANCORA ACCESO (due fonti del gioco che si contraddicono: " +
                            "`Penitent.Status.Dead`=False, `Core.Input.HasBlocker(\"DEAD_SCREEN\")`=True) · " +
                            "⭐ e' lo stato che nasce quando un load ricostruisce il Penitent mentre la " +
                            "schermata di morte era aperta: il giocatore riappare **senza l'animazione di " +
                            "respawn** (`Penitent.Respawn()` non e' mai stato invocato: nel log G1 " +
                            "`[Morte] il giocatore e' morto` = 0 occorrenze e il gioco lo dichiara vivo " +
                            "lo stesso, e il prompt del tasto resta a schermo) · ⚠️ IL WATCHDOG E' " +
                            "STRUTTURALMENTE CIECO a questo stato: `IsLocalPlayerDead()` risponde False, " +
                            "quindi non entra mai nella sua finestra · questa rete lo vede e lo dichiara, e " +
                            "chiedera' `Respawn()` fra " + F(INCOHERENT_GRACE_SECONDS) + "s (una volta " +
                            "sola, perche' ripetere dei load e' peggio dello stato)");
            }

            if (_incoherentRespawned || now < _incoherentNextAttempt)
                return;

            _incoherentRespawned = true;
            Respawn("STATO INCOERENTE (il giocatore e' vivo ma `DEAD_SCREEN` e' ancora acceso da " +
                    F(now - _incoherentSince) + "s): si chiede `Penitent.Respawn()` — **la via del gioco**, " +
                    "la stessa di un tasto — perche' e' l'unico modo lecito di rimettere d'accordo le due " +
                    "fonti (`Respawn()` → load → `LevelManager:355` `ResetManager()` azzera i blocker " +
                    "DAL GIOCO). ⚠️ NON congeda la schermata: la fa sparire **ricaricando il livello**, ed " +
                    "e' dichiarato nel referto. UNA volta sola per episodio: ripetere i load sarebbe " +
                    "peggio dello stato (ed e' esattamente cio' che l'ha prodotto)");
        }

        /// <summary>
        /// ⭐⭐ IL PREDICATO UNICO — **"LA SEQUENZA DI MORTE DEL BOSS E' IN CORSO"** — e lo usano **tutte e
        /// due** le reti, di proposito: il mandato chiede che `RETE 1` usi **lo stesso guardiano del
        /// fusibile**, cosi' "la mod non ha respawnato" non e' piu' una coincidenza ma una conseguenza.
        ///
        ///   - `_waitingSequence` ⇒ il `kill()` del boss e' arrivato e la sequenza **non e' conclusa**
        ///     (`kill()` → animazione di morte → cutscene → fine). ⚠️ **E' la finestra che il fix del 17/10
        ///     aveva lasciato scoperta su `RETE 1`**: `_cinematicOn` e' falso per tutta l'animazione di
        ///     morte (**~3.0 s** in `G1`, **~3.9 s** in `G2`), quindi con il solo `_cinematicOn` un tasto
        ///     premuto li' faceva respawnare **la mod** e la cutscene si perdeva;
        ///   - `_cinematicOn` ⇒ la cutscene e' **davvero** in corso (`CinematicStarted` acceso e non ancora
        ///     chiuso): vale anche per una scena **senza** `kill()` di mezzo (si muore durante una cutscene).
        ///
        /// Fuori da questi due casi il predicato e' **falso** e `RETE 1` e' **identica a prima**: legge i
        /// tasti e respawna subito, che e' la configurazione normale (morte senza boss, morte con il boss
        /// vivo) e la regressione da non rompere.
        /// </summary>
        private static bool BossSequenceInProgress
        {
            get { return _cinematicOn || _waitingSequence; }
        }

        /// <summary>
        /// Un giro per frame, **solo da morti**. Chiamata da `Multiplayer.OnLateUpdate`, subito dopo
        /// `DeathCorpseFollow.Tick()` (che e' il pezzo che ha spostato il Penitent e va chiuso per primo).
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_dead)
                        CloseWindow("il giocatore e' tornato (respawn)");

                    // ⭐⭐ E IL GIRO "DA VIVI" ESISTE PER UNA COSA SOLA: vedere lo STATO INCOERENTE
                    // (vivo + `DEAD_SCREEN` acceso), che altrimenti **non avrebbe nessuna rete**. La
                    // finestra di questa classe esiste solo da morto, e in quello stato `Status.Dead` e'
                    // falso: senza queste due righe il watchdog e' **strutturalmente cieco**. Vedi
                    // `TickAlive()`.
                    TickAlive();
                    return;
                }

                float now = Time.unscaledTime;

                if (!_dead)
                {
                    _dead = true;
                    _deadSince = now;
                    _nextSample = now;
                    _samples = 0;
                    _keyArmed = false;
                    _fuseUsed = false;
                    _attempts = 0;
                    _nextAttempt = 0f;
                    _goneReported = false;

                    // ⭐ GUARDIA 1, e sono le due righe che chiudono la regressione del 16/10. Il valore del
                    // contatore si registra ADESSO, all'apertura: il fusibile si armera' solo quando il
                    // contatore CRESCE, cioe' quando il `kill()` di un boss avviene DOPO questa morte. Un
                    // `kill()` avvenuto prima (boss gia' morto quando si cade) lascia il fusibile disarmato,
                    // ed e' giusto cosi': li' non c'e' nessuna cutscene da perdere.
                    _killSignalAtOpen = EnemySyncManager.BossKillSignal;
                    _killSignal = _killSignalAtOpen;
                    _armed = false;
                    _sceneOn = false;
                    _fuseSince = now;
                    _disarmedReported = false;

                    // ⭐⭐ LA SEQUENZA DEL BOSS: si azzera la memoria della finestra. `_cinematicOn` **non**
                    // si azzera: non e' un fatto di questa morte, e' lo stato vero del gioco (se una
                    // cutscene sta girando, sta girando).
                    _killSeen = false;
                    _waitingSequence = false;
                    _sequenceEnded = false;
                    _bossDeathAt = 0f;
                    _bossDeathFuseAt = 0f;
                    _armReason = string.Empty;

                    // ⭐ LA PORTA DEI TASTI: si riparte da "nessun respawn chiesto dai tasti", da "RETE 1
                    // non ancora muta" e da "avviso non ancora mostrato" (uno per finestra).
                    _keysRespawns = 0;
                    _keysMuted = false;
                    _keyIgnoredReported = false;
                    _warnedThisWindow = false;

                    // ⭐ IL PRIMO GIRO NON SI FIDA DELLA DIFFERENZA: le sottoscrizioni possono essere
                    // arrivate DOPO un blocker gia' acceso (la mod si aggancia al primo ciclo per frame, e il
                    // gioco accende `InitialBlocker` nel suo `Start`). Quindi all'apertura della finestra si
                    // fa una **fotografia** con `HasBlocker` su tutti i nomi noti: cosi' `DEAD_SCREEN` —
                    // che e' acceso **prima** che la finestra si apra, perche' la schermata di morte lo
                    // accende nel suo `OnDeadAction` — compare nella riga di stato invece di restare
                    // invisibile. E' esattamente il blocker che nel log del guasto non si vedeva.
                    SyncBlockers();
                }

                // ---- ⭐ LA PROBE CINEMATIC: se `Core.Cinematics` non era pronto all'aggancio, si riprova
                //      qui (una volta riuscita non fa piu' niente). E' lo stesso schema di `CutsceneProbe`.
                TickCinematicSubscription();

                // ---- ⭐ GUARDIA 1 (la lettura del segnale) e GUARDIA 2 (la pausa sulle sequenze).
                //      Stanno PRIMA della probe, cosi' la riga di stato del secondo in cui il fusibile si
                //      arma lo dice gia': "non si e' armato" e "si e' armato e non e' scattato" devono
                //      avere due aspetti diversi **nella stessa riga**.
                UpdateFuseGate(now);

                // ---- LA PROBE: si legge lo stato VERO una volta al secondo. E' il pezzo che il mandato
                // chiede ("quali blocker sono accesi QUANDO i tasti non rispondono?") e che nella sessione
                // del guasto non c'era: `DEAD_SCREEN` e `FADE` erano accesi e nessuna riga lo diceva.
                if (now >= _nextSample)
                {
                    _nextSample = now + SAMPLE_INTERVAL;
                    _samples++;
                    ModLog.Info(State(now));
                }

                // ---- RETE 1 + RETE 2. Le sequenze in corso si LEGGONO, mai si scrivono.
                if (Core.Input == null || Core.Logic == null)
                    return;

                if (now - _deadSince < KEY_ARM_SECONDS)
                    return;

                // ---- ⭐ RETE 1: IL TASTO. Non e' un doppione del fusibile: e' la rete che risponde
                // **subito** quando il tasto arriva al gioco (provato su `G2` con `Space`,
                // `fusibile=non usato`).
                //
                // ⚠️ E IL GUARDIANO E' CAMBIATO DUE VOLTE, e questa e' la seconda.
                //   - `!_sceneOn` (fino al 17/10) ⇒ **muta** nella finestra che conta: `FADE` e
                //     `cutscene_block` si accendono **prima** che la scena parta (misurato in `G1`: `FADE`
                //     come **inizio** del fade a `t=285.58`, la scena mai arrivata), e in `G1` il livello e'
                //     stato ricaricato lo stesso, dalla schermata di morte del gioco, senza che la mod
                //     chiedesse niente;
                //   - `!_cinematicOn` (dal 17/10) ⇒ **accanto** alla scena ma **troppo largo**: la cutscene
                //     inizia solo a `CinematicStarted`, quindi per tutta **l'animazione di morte** del boss
                //     (`kill()` → `CinematicStarted`: **~3.0 s** in `G1`, **~3.9 s** in `G2`) i tasti
                //     contavano di nuovo, e un tasto premuto li' faceva respawnare **la mod** ⇒ **la stessa
                //     perdita di cutscene, da un'altra porta** (la regressione del 18/10);
                //   - ⭐ **`BossSequenceInProgress`** (adesso) ⇒ **lo stesso predicato del fusibile**: muta
                //     dal `kill()` alla fine della sequenza, viva **fuori**. Cosi' "la mod non ha
                //     respawnato mentre il boss moriva" non e' una coincidenza, ed e' la richiesta del
                //     mandato (*"RETE 1 deve usare lo stesso guardiano del fusibile"*).
                bool keysMuted = BossSequenceInProgress;

                if (keysMuted)
                {
                    _keysMuted = true;

                    // ⭐⭐ E QUESTA E' LA DIAGNOSTICA CHE PRIMA NON ESISTEVA, e non e' cosmetica: se il tasto
                    // arriva **mentre la sequenza gira**, la mod **non** respawna (e' la porta chiusa), ma la
                    // schermata di morte **DEL GIOCO** si' (`while (!Input.anyKey)` → `FADE` 1.5 s →
                    // `Respawn()`). ⇒ **questa riga e' l'unica cosa che, nella prossima sessione, puo'
                    // distinguere "la cutscene si e' persa per un tasto" da "si e' persa per la mod"** —
                    // in `G1` la pressione e' caduta **fra due campioni** (`unityAnyKey=False` in tutti gli
                    // 80 campioni) e non si e' potuta vedere. Una volta per finestra, e non consuma niente:
                    // `GetKeyDown` e' una lettura.
                    if (!_keyIgnoredReported)
                    {
                        KeyCode ignored = FirstKeyPressed();
                        if (ignored != KeyCode.None)
                        {
                            _keyIgnoredReported = true;
                            ModLog.Warn(MARK + " TASTO IGNORATO: " + ignored + " e' stato premuto mentre la " +
                                        "sequenza di morte del boss era in corso (`tasti=ignorati(" +
                                        (_waitingSequence ? "sequenza-boss" : "cutscene") + ")`): la mod " +
                                        "NON respawna, e la cutscene non si perde per colpa di questa rete · " +
                                        "⚠️ MA LA SCHERMATA DI MORTE DEL GIOCO respawna al primo tasto " +
                                        "(`DeadScreenWidget.OnDeadAction:87-96`: `while (!Input.anyKey)` → " +
                                        "`FADE(1.5s)` → `Penitent.Respawn()`), quindi se da qui in poi il " +
                                        "livello si ricarica e la cutscene NON arriva, **la causa e' quella " +
                                        "e non questa mod**: e' la porta che il referto dichiara di non " +
                                        "poter chiudere (l'avviso a schermo esiste per questo)");
                        }
                    }
                }
                else if (_keysMuted)
                {
                    _keysMuted = false;
                    if (_keyArmed)
                    {
                        ModLog.Info(MARK + " RETE 1 di nuovo ATTIVA (la sequenza di morte del boss e' " +
                                    "conclusa): da qui i tasti tornano a chiedere il respawn, e il campo " +
                                    "`tasti=` della riga di stato vale `attivi`");
                    }
                }

                if (!_keyArmed && !keysMuted)
                {
                    _keyArmed = true;
                    ModLog.Info(MARK + " ascolto dei tasti ARMATO (nessuna sequenza di morte del boss in " +
                                "corso): se il gioco non legge piu' i tasti della schermata di morte, " +
                                "questo e' il punto in cui il respawn viene chiesto lo stesso");
                }

                if (_keyArmed && !keysMuted)
                {
                    KeyCode pressed = FirstKeyPressed();
                    if (pressed != KeyCode.None)
                    {
                        _keysRespawns++;
                        Respawn("tasto premuto dal morto (" + pressed + "): nessuna sequenza di morte del " +
                                "boss in corso (quindi la scena non si interrompe) e la schermata di morte " +
                                "non ha reagito, percio' il respawn viene chiesto qui");
                        return;
                    }
                }

                // ---- ⭐ RETE 2: IL FUSIBILE, e i DUE NUMERI di questa consegna (19/10). Il conteggio
                // (`now - _fuseSince`) NON parte prima del `kill()` del boss e NON avanza durante una
                // sequenza; il tentativo parte quando il conteggio supera `ESCAPE_SECONDS` — che ora sono
                // **15 s e partono dalla FINE della sequenza** (a scena finita il gioco non sta piu'
                // lavorando) — oppure quando l'orologio assoluto (`MAX_ESCAPE_SECONDS = 300 s`) dice che la
                // finestra e' troppo lunga per essere una scena normale: ed e' l'unica via che ignora di
                // proposito le sequenze, perche' una cutscene APPESA non deve poter bloccare il giocatore per
                // sempre. La grazia di `BOSS_DEATH_GRACE_SECONDS = 30 s` sta nell'armamento (vedi
                // `UpdateFuseGate`), non qui.
                bool hardTimeout = now - _deadSince >= MAX_ESCAPE_SECONDS;
                if (!_armed && !hardTimeout)
                    return;

                float fuseElapsed = hardTimeout ? now - _deadSince : now - _fuseSince;

                if (fuseElapsed >= ESCAPE_SECONDS && (!_fuseUsed || now >= _nextAttempt))
                {
                    bool primo = !_fuseUsed;
                    _fuseUsed = true;
                    _nextAttempt = now + ESCAPE_RETRY_SECONDS;
                    Respawn((primo
                                ? "FUSIBILE scattato: " + F(fuseElapsed) + "s da morto" +
                                  (hardTimeout
                                      ? " e oltre " + F(MAX_ESCAPE_SECONDS) + "s di orologio: la finestra " +
                                        "e' troppo lunga per essere una scena normale (cutscene appesa?), " +
                                        "e un giocatore bloccato non puo' piu' giocare"
                                      : ", contati DALLA FINE DELLA SEQUENZA (`armato-per=" +
                                        (_armReason.Length > 0 ? _armReason : "?") + "`, soglia " +
                                        F(ESCAPE_SECONDS) + "s) e senza che il gioco abbia risposto ai " +
                                        "tasti: a scena finita il gioco non sta piu' lavorando, quindi il " +
                                        "fusibile puo' essere corto")
                                : "FUSIBILE, nuovo tentativo a " + F(fuseElapsed) + "s da morto") +
                            " · tentativi=" + (_attempts + 1) + " di questa rete (e' esattamente cio' " +
                            "che il gioco avrebbe fatto al primo tasto)");
                }
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        // ============================================================================================
        // ⭐ LE GUARDIE DEL FUSIBILE, E ORA SONO TRE
        // ============================================================================================

        /// <summary>
        /// Un giro per frame, **solo da morti**: legge il segnale del `kill()` del boss (guardia 1), misura
        /// se una **scena** e' in corso e fa ripartire il conteggio del fusibile (guardia 2), e **arma il
        /// fusibile solo quando la sequenza di morte del boss e' CONCLUSA** (guardia 3, quella di questa
        /// consegna). **Non chiama niente del gioco**: legge `InputManager.HasBlocker` e un `int`, e scrive
        /// righe di log.
        /// </summary>
        private static void UpdateFuseGate(float now)
        {
            // `Core.Input` puo' essere null per un istante durante un cambio scena: senza questa guardia
            // `HasBlocker` lancerebbe a ogni frame e il log si riempirebbe di `[Respawn ERR]` inutili.
            if (Core.Input == null)
                return;

            _killSignal = EnemySyncManager.BossKillSignal;

            // ---- ⭐ GUARDIA 3, IL PUNTO DI QUESTA CONSEGNA. Il `kill()` NON arma piu' il fusibile: lo
            //      mette in **attesa della fine della sequenza**. Il fusibile esiste solo da li' in poi,
            //      perche' e' solo da li' in poi che "il giocatore non respawna" e' un guasto invece che
            //      **il gioco che sta facendo la sua animazione di morte e la sua cutscene**.
            //      ⚠️ Un `kill()` avvenuto **prima** di questa morte resta invisibile alla finestra
            //      (`_killSignalAtOpen`), ed e' giusto: li' non c'e' nessuna scena da attendere.
            while (_killSignal > _killSignalAtOpen)
            {
                _killSignalAtOpen++;
                _killSeen = true;
                _waitingSequence = true;
                _sequenceEnded = false;
                _bossDeathAt = now;
                _fuseSince = now;
                _disarmedReported = false;
                ModLog.Info(MARK + " segnale del `kill()` del boss arrivato " + F(now - _deadSince) +
                            "s dopo la morte (`BossKillSignal`=" + _killSignal + "): ⭐ da qui la sequenza " +
                            "di morte del boss puo' partire, quindi il fusibile **ASPETTA la fine della " +
                            "sequenza** e NON si arma adesso: fra il `kill()` e la fine della cutscene il " +
                            "gioco sta lavorando normalmente e non c'e' niente da sbloccare · ⭐ e da qui " +
                            "e' **MUTA anche RETE 1** (l'ascolto dei tasti): un tasto premuto durante " +
                            "l'animazione di morte del boss faceva respawnare la mod e la cutscene si " +
                            "perdeva — e' la porta chiusa il 18/10, e si legge in `tasti=` " +
                            "(`ignorati(sequenza-boss)` fino alla fine della sequenza)");

                // ⭐⭐ E L'UNICA COSA CHE RESTA CONTRO LA PORTA CHE NON SI CHIUDE: l'avviso a schermo.
                // La schermata di morte DEL GIOCO respawna al primo tasto e non passa da qui (vedi il
                // commento di `WARN_PLAYER_ON_BOSS_DEATH`): l'unico rimedio lecito e' **togliere la
                // causa**, cioe' dire al giocatore di non premere. Una volta per finestra.
                WarnPlayerNotToPress();
            }

            // ---- GUARDIA 2. La sequenza in corso NON fa scattare il fusibile, e **azzera il conteggio**.
            // Si guarda PIU' DI `CINEMATIC`/`DIALOG`: a 45 s la cutscene del boss non era ancora partita, e
            // quella che il referto del guasto misura accesa durante la cutscene e' `FADE` +
            // `cutscene_block`. Un `false` che era `true` significa "la scena e' finita", ed e' li' che il
            // conteggio riparte da zero.
            bool scene = false;
            for (int i = 0; i < SceneBlockers.Length; i++)
            {
                if (Core.Input.HasBlocker(SceneBlockers[i]))
                {
                    scene = true;
                    break;
                }
            }

            _sceneOn = scene;
            _bossDeathFuseAt = _cinematicOn ? now : _bossDeathFuseAt;

            if (scene)
            {
                // ⚠️ NON si tocca `_deadSince`: quello e' `daMorte=` nella riga di stato, e deve restare la
                // verita' su da quanto si e' morti. Si sposta solo il punto di partenza del fusibile.
                _fuseSince = now;
                return;
            }

            // ---- ⭐ L'ARMATURA, E HA DUE VIE (vedi il commento di classe).
            if (!_armed && _waitingSequence)
            {
                // (a) LA VIA BUONA: la cutscene e' partita ed e' **finita** (`CinematicEnded`).
                bool byCinematic = _sequenceEnded;

                // (b) IL RIPIEGO: nessuna cutscene e' partita entro `BOSS_DEATH_GRACE_SECONDS` ⇒ questo
                //     boss non ha una scena, la sua **sequenza di morte** e' conclusa, e il fusibile puo'
                //     esistere. ⚠️ **E non si arma mai prima**: e' la scelta conservativa del mandato.
                bool byFallback = !_cinematicOn && (now - _bossDeathAt) >= BOSS_DEATH_GRACE_SECONDS;

                if (byCinematic || byFallback)
                {
                    // ⭐ Il conteggio parte da QUANDO LA SEQUENZA E' FINITA, non da quando il boss e'
                    //    morto: e' esattamente la differenza che questa consegna doveva colmare.
                    float from = byCinematic && _bossDeathFuseAt > _bossDeathAt
                        ? _bossDeathFuseAt
                        : now;

                    _armed = true;
                    _waitingSequence = false;
                    _fuseSince = from;
                    _armReason = byCinematic ? "fine-cutscene" : "nessuna-cutscene";
                    ModLog.Info(MARK + " fusibile ARMATO (perche=" +
                                (byCinematic
                                    ? "la CUTSCENE del boss e' FINITA (`CinematicEnded`): la sequenza di " +
                                      "morte e' conclusa e solo da qui il fusibile ha senso"
                                    : "sono passati " + F(now - _bossDeathAt) + "s dal `kill()` e nessuna " +
                                      "cutscene e' partita (soglia " + F(BOSS_DEATH_GRACE_SECONDS) +
                                      "s): questo boss non ha una scena, la sua sequenza di morte e' " +
                                      "conclusa") +
                                ") · il conteggio parte da QUI (non dal `kill()`): " + F(now - _fuseSince) +
                                "s di " + F(ESCAPE_SECONDS) + "s · era `BossKillSignal`=" + _killSignal);
                }
                else
                {
                    if (!_disarmedReported)
                    {
                        _disarmedReported = true;
                        ModLog.Info(MARK + " fusibile NON armato (perche=manca ancora la fine della " +
                                    "sequenza di morte del boss: o e' in corso (`sequenza=`/`cinematic=`) " +
                                    "o non e' ancora passata la soglia di " +
                                    F(BOSS_DEATH_GRACE_SECONDS) + "s dal `kill()`, che e' il ripiego per " +
                                    "un boss senza cutscene) · `BossKillSignal`=" + _killSignal + ", " +
                                    "atteso>" + _killSignalAtOpen + " · i tasti restano la rete " +
                                    "principale e funzionano lo stesso fuori dalla cutscene");
                    }

                    // ⚠️ IL CONTEGGIO NON PARTE: finche' la sequenza non e' conclusa il fusibile **non
                    //    esiste**. E' il punto della regressione del 17/10: prima si armava qui, e
                    //    scattava mentre il boss finiva l'animazione di morte.
                    _fuseSince = now;
                    return;
                }
            }

            if (!_armed && !_fuseUsed)
            {
                // Lo dice una volta sola e solo sul caso che il mandato chiede di poter distinguere:
                // "il fusibile NON si e' armato, ed e' per questo". Senza questa riga, "non si e' armato"
                // e "si e' armato e non e' scattato" hanno lo stesso aspetto nel log.
                if (!_disarmedReported)
                {
                    _disarmedReported = true;
                    ModLog.Info(MARK + " fusibile NON armato (perche=il `kill()` di un boss non e' ancora " +
                                "avvenuto: `BossKillSignal`=" + _killSignal + ", atteso>" +
                                _killSignalAtOpen + ") · i tasti restano la rete principale e funzionano lo " +
                                "stesso: questa e' la configurazione che il 16/10 respawnava uno spettatore " +
                                "e gli faceva perdere la cutscene");
                }

                return;
            }

            if (!_fuseUsed)
            {
                ModLog.Info(MARK + " fusibile armato E in conteggio: " + F(now - _fuseSince) + "s (di " +
                            F(ESCAPE_SECONDS) + "s) · `kill()` del boss avvenuto e nessuna sequenza in " +
                            "corso: se il giocatore e' bloccato, il respawn arriva da solo");
            }
        }

        /// <summary>
        /// ⭐⭐ L'AVVISO A SCHERMO — **il compromesso dichiarato**, e l'unica leva lecita contro la porta
        /// che la mod **non puo' chiudere**.
        ///
        /// **LA PORTA, e la prova che non si chiude**: `DeadScreenWidget.OnDeadAction` fa
        /// `while (!Input.anyKey)` → `Core.UI.Fade.Fade(toBlack: true, 1.5f, 0f, callback)` → nel callback
        /// `Core.Logic.Penitent.Respawn()` (`DeadScreenWidget.cs:87-96`). Tre divieti si frappongono:
        /// `Core.Input.SetBlocker` (**vietato**), una patch su `DeadScreenWidget` (**zona vietata**) e
        /// `ResetManager` (**vietato**). E "consumare" il tasto non e' una cosa che esista: `Input.anyKey` e'
        /// la lettura diretta del dispositivo, la **stessa** che farebbe questa rete. ⇒ **premendo un tasto
        /// durante l'animazione di morte del boss, la schermata del GIOCO respawna e la cutscene si perde,
        /// e la mod non puo' impedirlo.**
        ///
        /// **L'UNICA COSA CHE RESTA e' togliere la causa**: dire al giocatore di **non premere**. Non e' un
        /// rimedio al guasto, e' un modo per non farlo succedere — e nel test che l'utente vuole fare
        /// (*"la cutscene si deve vedere"*) e' l'unica leva che esista.
        ///
        /// ⚠️ **Perche' e' lecito**: `NotificationManager` e' **della mod** (`Multiplayer.NotificationManager`,
        /// gia' usato per "connesso"/"disconnesso"/errori) — non e' la UI del gioco, non la si patcha e non la
        /// si tocca: si aggiunge una riga al suo elenco, e il suo `Update` la disegna. Nessun `SetActive`,
        /// nessun blocker, nessuna patch: **i 263 bersagli Harmony non cambiano**.
        ///
        /// ⚠️ **E perche' e' anche poco invasivo**: **uno per finestra di morte** (`_warnedThisWindow`),
        /// mostrato **solo se il giocatore locale e' morto** (questa classe gira solo da morti), e dura
        /// `config.notificationDisplaySeconds` — **4 s di default**, cioe' si spegne intorno al momento in cui
        /// la scena parte (la scena parte **3.0–3.9 s** dopo il `kill()`), quindi **non copre la cutscene**.
        /// Se desse fastidio, si spegne con `WARN_PLAYER_ON_BOSS_DEATH = false`.
        /// </summary>
        private static void WarnPlayerNotToPress()
        {
            if (!WARN_PLAYER_ON_BOSS_DEATH || _warnedThisWindow)
                return;

            // Una volta sola, anche se l'avviso non riesce: ritentarlo a ogni frame sarebbe spam.
            _warnedThisWindow = true;

            try
            {
                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                    return;

                string text = "NON PREMERE: il boss sta morendo - la cutscene sta per partire";

                Main.Multiplayer.NotificationManager.DisplayNotification(text);

                ModLog.Info(MARK + " avviso a schermo al giocatore morto: \"" + text + "\" · e' il " +
                            "compromesso dichiarato per la porta che la mod NON puo' chiudere (la " +
                            "schermata di morte DEL GIOCO respawna al primo tasto: " +
                            "`DeadScreenWidget.cs:87-96`) · l'avviso non impedisce il respawn del gioco, " +
                            "toglie la causa (il tasto non voluto)");
            }
            catch (Exception e)
            {
                Fail("WarnPlayerNotToPress", e);
            }
        }

        /// <summary>
        /// Chiude la finestra e **riporta lo stato della sessione a zero**. Esiste anche per la
        /// disconnessione (`Reset`), perche' la disconnessione puo' arrivare **mentre si e' ancora morti** e
        /// in quel momento `IsLocalPlayerDead()` resta vero (il gioco non sa nulla della rete).
        /// </summary>
        internal static void Reset()
        {
            try
            {
                if (_dead)
                    CloseWindow("disconnessione / fine sessione");
            }
            catch (Exception e)
            {
                Fail("Reset", e);
            }
        }

        private static void CloseWindow(string why)
        {
            ModLog.Info(MARK + " finestra chiusa: " + why + " · campioni=" + _samples +
                        " fusibile=" + (_fuseUsed ? "usato" : "non usato") +
                        " fusibileArmato=" + (_armed ? "si" : "no") +
                        (_armed && _armReason.Length > 0 ? "(perche=" + _armReason + ")" : string.Empty) +
                        " killBoss=(visti " + (_killSignal - _killSignalAtOpen) + " durante la finestra, " +
                        "totale scena " + _killSignal + ")" +
                        " · " + BlockersLine());
            _dead = false;
            _deadSince = 0f;
            _samples = 0;
            _keyArmed = false;
            _fuseUsed = false;
            _attempts = 0;
            _nextAttempt = 0f;
            _goneReported = false;
            _armed = false;
            _sceneOn = false;
            _fuseSince = 0f;
            _killSignalAtOpen = 0;
            _killSignal = 0;
            _disarmedReported = false;
            _killSeen = false;
            _waitingSequence = false;
            _sequenceEnded = false;
            _bossDeathAt = 0f;
            _bossDeathFuseAt = 0f;
            _armReason = string.Empty;
            _keysRespawns = 0;
            _keysMuted = false;
            _keyIgnoredReported = false;
            _warnedThisWindow = false;
        }

        // ============================================================================================
        // L'USCITA
        // ============================================================================================

        /// <summary>
        /// **LA VIA D'USCITA E' QUELLA DEL GIOCO**: `Penitent.Respawn()` — l'unico chiamante in tutto
        /// l'assembly e' `DeadScreenWidget` (`PlayerDeathSync.cs` lo documenta), quindi chiamarla qui e'
        /// fare la stessa cosa che il gioco farebbe al tasto, non un rimedio nuovo.
        ///
        /// E L'ORDINE CONTA: prima si ricompone il cadavere (il `Penitent` torna **al punto di morte** e il
        /// renderer torna visibile), poi si respawna. Se il respawn lanciasse, il giocatore si ritrova morto
        /// **dov'e' caduto**, cioe' il difetto noto, non un giocatore invisibile a meta' stanza.
        /// </summary>
        private static void Respawn(string why)
        {
            Penitent penitent = Core.Logic != null ? Core.Logic.Penitent : null;
            if (penitent == null)
            {
                // Il Penitent puo' essere assente per un istante durante un cambio scena: non e' un guasto,
                // ma va DETTO una volta (senza, "non ho fatto niente" e "ho provato e non c'era" si
                // assomigliano).
                if (!_goneReported)
                {
                    _goneReported = true;
                    ModLog.Warn(MARK + " respawn NON chiesto (" + why + "): Core.Logic.Penitent non e' " +
                                "disponibile in questo momento (cambio scena in corso?) · si riprova al " +
                                "prossimo frame");
                }
                return;
            }

            try
            {
                DeathCorpseFollow.Reset();
            }
            catch (Exception e)
            {
                // Mai fatale: la copia e' cosmetica, il respawn no. Si prosegue comunque.
                Fail("Respawn/DeathCorpseFollow.Reset", e);
            }

            ModLog.Info(MARK + " RESPAWN CHIESTO: " + why + " · via=" + BlockersLine() +
                        " · tentativi=" + (_attempts + 1) + " (le stesse cose che farebbe la schermata di " +
                        "morte: il load del livello rimette a posto blocker, fade e renderer DAL GIOCO)");

            try
            {
                _attempts++;
                // ⚠️ `SpawnManager.AutomaticRespawn`: se il gioco e' in una modalita' che respawna da solo
                // (`DeadScreenWidget.OnDead` lo legge per primo, :126), il `Prefix` di `PenitentRespawn` lo
                // rispetta. Qui si vuole **la stessa cosa che farebbe la schermata di morte**, quindi si
                // lascia la decisione al gioco.
                penitent.Respawn();
            }
            catch (Exception e)
            {
                Fail("Respawn/penitent.Respawn", e);
            }
        }

        // ============================================================================================
        // LE LETTURE
        // ============================================================================================

        /// <summary>
        /// Il primo tasto premuto **in questo frame**, `None` se nessuno. `GetKeyDown` e' un fronte: il
        /// tasto tenuto giu' dalla morte non viene riletto.
        /// </summary>
        private static KeyCode FirstKeyPressed()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                if (Input.GetKeyDown(Keys[i]))
                    return Keys[i];
            }

            return KeyCode.None;
        }

        /// <summary>
        /// LA RIGA DELLA PROBE. Ogni campo e' isolato: se una lettura fallisce il campo lo dice e **la riga
        /// esce lo stesso**, perche' un campo mancante non deve poter cancellare la misura degli altri.
        /// </summary>
        private static string State(float now)
        {
            bool unityAnyKey = false;
            try
            {
                unityAnyKey = Input.anyKey;
            }
            catch (Exception)
            {
            }

            string deadScreen = "widget-non-trovato";
            try
            {
                int found;
                DeadScreenWidget widget = DeathScreenProbe.FindWidget(out found);
                if (widget != null)
                {
                    deadScreen = "trovato=" + found +
                                 " attivo=" + (widget.gameObject != null && widget.gameObject.activeInHierarchy
                                     ? "True" : "False") +
                                 " enabled=" + (widget.enabled ? "True" : "False") +
                                 " alphaNorm=" + (widget.NormalGroup != null
                                     ? DeathScreenProbe.F(widget.NormalGroup.alpha) : "n/d");
                }
            }
            catch (Exception)
            {
                deadScreen = "errore";
            }

            string gameplayUI = "n/d";
            string fadeUI = "n/d";
            try
            {
                if (Core.UI != null)
                {
                    if (Core.UI.GameplayUI != null)
                        gameplayUI = Core.UI.GameplayUI.gameObject.activeInHierarchy ? "True" : "False";

                    if (Core.UI.Fade != null)
                        fadeUI = Core.UI.Fade.gameObject.activeInHierarchy ? "True" : "False";
                }
            }
            catch (Exception)
            {
                gameplayUI = "errore";
            }

            return MARK + " stato t=" + F(now) + " daMorte=" + F(now - _deadSince) +
                   " campione=" + _samples +
                   " unityAnyKey=" + (unityAnyKey ? "True" : "False") +
                   " tasti=" + KeysState() +
                   " tentativiMiei=" + _attempts +
                   " sequenza=" + (_sceneOn ? "si" : "no") +
                   " cinematic=" + (_cinematicOn ? "si" : "no") +
                   " fusibile=" + FusibileState(now) +
                   " killBoss=" + _killSignal + "(atteso>" + _killSignalAtOpen + ")" +
                   " sequenzaBoss=" + (_armed ? "conclusa(armato-per=" +
                                             (_armReason.Length > 0 ? _armReason : "?") + ")"
                                       : _waitingSequence
                                           ? "in-corso(attesa-da-" + F(now - _bossDeathAt) + "s/" +
                                             F(BOSS_DEATH_GRACE_SECONDS) + "s)"
                                           : "non-ancora") +
                   " gameplayUIActive=" + gameplayUI + " fadeUIActive=" + fadeUI +
                   " schermataMorte=[" + deadScreen + "] · " + BlockersLine();
        }

        /// <summary>
        /// ⭐⭐ IL CAMPO CHE DICE **PERCHE' RETE 1 NON HA AGITO**, ed e' il campo obbligatorio di questa
        /// consegna: senza, *"non ha agito perche' non ho premuto"* e *"non ha agito perche' era muta"*
        /// hanno **lo stesso aspetto** nel log — ed e' esattamente la confusione che ha reso illeggibile la
        /// sessione di `G1`.
        ///
        ///   | valore | cosa significa | cosa fare |
        ///   |---|---|---|
        ///   | `respawN` | RETE 1 ha chiesto il respawn **N** volte in questa finestra | il giocatore e' tornato |
        ///   | `ignorati(sequenza-boss)` | ⭐ **la sequenza di morte del boss e' in corso**: la rete e' **muta** di proposito (e' la porta chiusa il 18/10) | **normale**: il gioco sta facendo l'animazione di morte / la cutscene |
        ///   | `ignorati(cutscene)` | una cutscene e' in corso **senza** un `kill()` di mezzo | **normale**: la scena non si interrompe |
        ///   | `attesa(armamento)` | primi `2.5 s` dalla morte (`KEY_ARM_SECONDS`) | **normale**: e' il tasto che ha ucciso |
        ///   | `attivi` | la rete **sta leggendo i tasti** | se il giocatore preme, il respawn parte |
        ///
        /// ⚠️ **E l'ordine dei controlli e' l'informazione**: `respawN` viene **prima** di tutto (se ha
        /// agito, il motivo non conta piu'), e `ignorati(sequenza-boss)` **prima** di `attesa(armamento)`,
        /// perche' "muta per la sequenza" e' la risposta che si sta cercando in questo test.
        /// </summary>
        private static string KeysState()
        {
            if (_keysRespawns > 0)
                return "respaw" + _keysRespawns;

            if (_waitingSequence)
                return "ignorati(sequenza-boss)";

            if (_cinematicOn)
                return "ignorati(cutscene)";

            if (!_keyArmed)
                return "attesa(armamento)";

            return "attivi";
        }

        /// <summary>
        /// ⭐ IL CAMPO CHE RENDE LEGGIBILE LA REGRESSIONE IN UNA RIGA SOLA: dice in **quale** degli stati e'
        /// il fusibile — `disarmato(pre-kill)`, `disarmato(attesa-fine-sequenza)`, `armato`, `scattato` — e
        /// **da quanto** conta. Senza, "non si e' armato", "si e' armato e non e' scattato" e "e' scattato"
        /// hanno lo stesso aspetto nel log.
        ///
        /// ⚠️ E IL FORMATO E' QUELLO CHE IL MANDATO CHIEDE, riga per riga:
        /// `fusibile=disarmato(pre-kill)|armato(X/45.00s)|armato-in-pausa(perche=…)|scattato`. Lo stato
        /// nuovo e' `disarmato(attesa-fine-sequenza)`: e' **il fusibile che NON esiste ancora** perche' il
        /// boss sta finendo la sua animazione di morte o la sua cutscene — ed e' la regressione del 17/10.
        /// </summary>
        private static string FusibileState(float now)
        {
            if (_fuseUsed)
                return "scattato";

            if (!_armed)
            {
                if (_waitingSequence)
                    return "disarmato(attesa-fine-sequenza:da-kill=" + F(now - _bossDeathAt) + "s/" +
                           F(BOSS_DEATH_GRACE_SECONDS) + "s)";

                return "disarmato(pre-kill)";
            }

            if (_cinematicOn)
                return "armato-in-pausa(perche=cutscene)";

            if (_sceneOn)
                return "armato-in-pausa(perche=scena)";

            return "armato(" + F(now - _fuseSince) + "/" + F(ESCAPE_SECONDS) + "s)";
        }

        /// <summary>
        /// L'elenco dei blocker accesi, **tenuto per differenza**: `InputManager` logga `SetBlocker` a ogni
        /// transizione ma non espone la lista (`inputBlockers` e' privato; `HasBlocker` e' pubblico ma
        /// chiede un nome). Quindi si ascolta `OnInputLocked`/`OnInputUnlocked` — due eventi **pubblici** —
        /// e si ricostruisce lo stack dai log di `SetBlocker`. E' l'unico modo di rispondere alla domanda
        /// operativa del mandato senza toccare niente.
        /// </summary>
        private static string BlockersLine()
        {
            if (Blockers.Count == 0)
                return "blockers=(nessuno)";

            return "blockers=(" + string.Join(",", Blockers.ToArray()) + ")";
        }

        /// <summary>
        /// Le due sottoscrizioni pubbliche, fatte **una volta sola** e con la guardia: `Core.Input` puo'
        /// esistere prima di `Awake`. Gli eventi non si toccano mai in scrittura.
        /// </summary>
        internal static void AttachOnce()
        {
            try
            {
                if (_attached || Core.Input == null)
                    return;

                Core.Input.OnInputLocked += OnLocked;
                Core.Input.OnInputUnlocked += OnUnlocked;
                _attached = true;
            }
            catch (Exception e)
            {
                Fail("AttachOnce", e);
            }

            // ⭐ E LA SOTTOSCRIZIONE AGLI EVENTI DELLA CUTSCENE, che e' il segnale della guardia 3.
            SubscribeCinematics();
        }

        // ============================================================================================
        // ⭐⭐ LA SEQUENZA DEL BOSS: "LA CUTSCENE E' IN CORSO" E "LA CUTSCENE E' FINITA"
        // ============================================================================================

        /// <summary>
        /// ⭐ IL SEGNALE CHE IL MANDATO CHIEDE, E **ESISTEVA GIA'**: `Cinematics.CinematicStarted` /
        /// `CinematicEnded` sono `event` **pubblici** di `Framework.Managers.Cinematics` (`:85-91`), gli
        /// stessi che `CutsceneProbe` sottoscrive gia' senza nessuna patch Harmony. ⇒ **una sottoscrizione,
        /// zero patch**: il conteggio dei 263 bersagli non cambia.
        ///
        /// ⚠️ E NON SI Sottoscrive `VideoStarted`: la **cutscene** e' quella che cambia il mondo (`CINEMATIC`
        /// e' il blocker che spegne i tasti), e `CinematicStarted` e' invocato in fondo a `StartCutscene`
        /// (`Cinematics.cs:148-151`), cioe' **dopo** il blocker: e' la verita' di "la scena e' partita".
        /// </summary>
        private static void SubscribeCinematics()
        {
            try
            {
                Cinematics cinematics = Core.Cinematics;
                if (cinematics == null)
                {
                    // Come in `CutsceneProbe`: `Core.Cinematics` puo' non essere pronto al primo giro, e il
                    // ritentativo sta in `TickCinematicSubscription`.
                    _subscribePending = true;
                    return;
                }

                if (_subscribed)
                    return;

                cinematics.CinematicStarted += OnCinematicStarted;
                cinematics.CinematicEnded += OnCinematicEnded;

                _subscribed = true;
                _subscribePending = false;

                ModLog.Info(MARK + " sequenza del boss: sottoscritti `CinematicStarted`/`CinematicEnded` " +
                            "(eventi PUBBLICI, nessuna patch Harmony) · il fusibile del respawn si armera' " +
                            "**dopo la fine della cutscene**, non al `kill()`: fra i due il gioco sta " +
                            "lavorando normalmente");
            }
            catch (Exception e)
            {
                Fail("SubscribeCinematics", e);
            }
        }

        /// <summary>
        /// Il ritentativo della sottoscrizione, **una volta per frame e solo finche' non e' riuscita**:
        /// quando `Core.Cinematics` arriva, si aggancia e non fa piu' niente (nessuno spam).
        /// </summary>
        private static void TickCinematicSubscription()
        {
            if (_subscribed || !_subscribePending)
                return;

            SubscribeCinematics();
        }

        /// <summary>
        /// LA CUTSCENE E' PARTITA. Da questo istante il fusibile **non conta** e **RETE 1 tace**: il respawn
        /// non deve poter ricaricare il livello mentre una scena scorre (e' la richiesta dell'utente, e la
        /// guardia che non fa perdere la cutscene).
        /// </summary>
        private static void OnCinematicStarted()
        {
            try
            {
                if (!_cinematicOn)
                {
                    _cinematicOn = true;

                    // Il fusibile e' ancora in attesa (o armato): in entrambi i casi, da qui si conta da
                    // zero. Se era armato, la pausa e' quella di sempre (`armato-in-pausa(perche=cutscene)`).
                    _fuseSince = Time.unscaledTime;

                    // ⚠️ La scena che conta e' quella che parte **dopo** il `kill()`: una cutscene che era
                    // gia' in corso quando il boss e' morto non deve poter dire "la sequenza e' finita".
                    if (_killSeen)
                        _sequenceEnded = false;

                    if (_dead)
                        ModLog.Info(MARK + " la CUTSCENE e' PARTITA (`CinematicStarted`) · da qui il " +
                                    "fusibile NON conta e RETE 1 non legge i tasti: la scena non si " +
                                    "interrompe" + (_waitingSequence
                                        ? " · il fusibile aspettava proprio questa, e si armera' alla FINE"
                                        : string.Empty));
                }
            }
            catch (Exception e)
            {
                Fail("OnCinematicStarted", e);
            }
        }

        /// <summary>
        /// ⭐⭐ LA CUTSCENE E' FINITA. **E' QUI CHE IL FUSIBILE PUO' ESISTERE**: da questo istante il gioco
        /// non sta piu' facendo niente per il giocatore morto, e se i tasti non arrivano quello **e'** il
        /// guasto. Il conteggio parte da qui (o dall'istante in cui anche l'ultimo blocker di scena cade,
        /// vedi `UpdateFuseGate`).
        /// </summary>
        private static void OnCinematicEnded(bool cancelled)
        {
            try
            {
                if (_cinematicOn)
                {
                    _cinematicOn = false;
                    _sequenceEnded = true;
                    _bossDeathFuseAt = Time.unscaledTime;

                    if (_dead)
                        ModLog.Info(MARK + " la CUTSCENE e' FINITA (`CinematicEnded cancelled=" + cancelled +
                                    "`) · la sequenza di morte del boss e' CONCLUSA: da qui il fusibile e' " +
                                    "un fusibile" + (_waitingSequence
                                        ? " e si arma adesso (il conteggio parte da QUI, non dal `kill()`)"
                                        : string.Empty));
                }
                else
                {
                    // Nessuna cutscene aperta: non si inventa niente, ma il fatto si registra (una
                    // `CinematicEnded` senza la sua `Started` e' un dato, non un errore).
                    _sequenceEnded = true;
                    _bossDeathFuseAt = Time.unscaledTime;
                }
            }
            catch (Exception e)
            {
                Fail("OnCinematicEnded", e);
            }
        }

        private static bool _attached;

        /// <summary>
        /// Un blocker e' stato ACCESO. Il nome esatto non arriva dall'evento (e' `Core.SimpleEvent`, senza
        /// argomenti), quindi la lista si ricostruisce **leggendo `HasBlocker` sui nomi noti del gioco**:
        /// sono 20 e sono tutti in `_il` (l'elenco completo dei `SetBlocker` del gioco). Un nome che non
        /// fosse in questa tabella non verrebbe mostrato: il campo `blockers=` lo dichiara con un `+?`.
        /// </summary>
        private static void OnLocked()
        {
            try
            {
                SyncBlockers();
            }
            catch (Exception e)
            {
                Fail("OnLocked", e);
            }
        }

        /// <summary>
        /// Riallinea l'elenco con la verita' di `HasBlocker` (pubblico). Si usa sia sulle transizioni sia
        /// all'apertura della finestra: vedi il commento li'.
        /// </summary>
        private static void SyncBlockers()
        {
            for (int i = 0; i < KnownBlockers.Length; i++)
            {
                string name = KnownBlockers[i];
                bool on = Core.Input.HasBlocker(name);
                bool known = Blockers.Contains(name);

                if (on && !known)
                    Blockers.Add(name);
                else if (!on && known)
                    Blockers.Remove(name);
            }
        }

        private static void OnUnlocked()
        {
            try
            {
                SyncBlockers();
            }
            catch (Exception e)
            {
                Fail("OnUnlocked", e);
            }
        }

        /// <summary>
        /// ⭐ I NOMI DEI BLOCKER DEL GIOCO, presi da `_il` (tutti i `Core.Input.SetBlocker` dell'assembly):
        /// `InitialBlocker` (InputManager:142), `CINEMATIC`/`FREECAMERA` (Cinematics:134,219,253),
        /// `DIALOG` (DialogManager:209,471), `BLOCK_UNTIL_FPS_STABLE` (LevelManager:329,336),
        /// `TUTORIAL` (TutorialManager:109,156), `PLAYER_LOGIC` (12 behaviour di animazione),
        /// `HW_DIALOG_TIME`/`END_BOSS_DEFEATED`/`HW_FINAL_PLATFORM_RISING` (PontiffHuskBossBehaviour),
        /// `cutscene_block`/`BossBlock`/`popup_block`/`POP_UP`/`CUTSCENE`/`MULTIPLAYER`/`FADE`/`DEAD_SCREEN`/
        /// `INTERACTABLE`/`DOOR`/`EXECUTION` (dai nomi misurati nei log di sessione).
        /// </summary>
        private static readonly string[] KnownBlockers = new string[]
        {
            "InitialBlocker", "CINEMATIC", "FREECAMERA", "DIALOG", "BLOCK_UNTIL_FPS_STABLE", "TUTORIAL",
            "PLAYER_LOGIC", "HW_DIALOG_TIME", "END_BOSS_DEFEATED", "HW_FINAL_PLATFORM_RISING",
            "BossBlock", "cutscene_block", "popup_block", "POP_UP", "CUTSCENE", "MULTIPLAYER", "FADE",
            "DEAD_SCREEN", "INTERACTABLE", "DOOR", "EXECUTION", "IntroBlock"
        };

        /// <summary>Formato FISSO dei numeri (invariante): la cultura di questa macchina puo' essere italiana.</summary>
        private static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// L'ultima riga di difesa: se fallisse anche `ModLog.Error`, l'eccezione uscirebbe dal punto di
        /// ingresso e arriverebbe dentro `OnLateUpdate` del gioco (il guasto da 2934 errori).
        /// </summary>
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
    }
}
