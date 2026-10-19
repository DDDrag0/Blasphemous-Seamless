# TODO — Blasphemous COOP

> **Documento vivo.** Va aggiornato a ogni passo: cosa è stato fatto, cosa ha misurato, cosa resta.
> L'ultima sezione (`Diario dei passi`) è il registro cronologico.

---

## ⚠️ REGOLE DI MANUTENZIONE DI QUESTO FILE — vincolanti

1. **Questo file NON si riscrive e NON si compatta.** La storia non si perde.
2. Gli **aggiornamenti approfonditi** (parametri scoperti, funzioni e metodi da usare, scoperte
   dell'Esploratore) si aggiungono **SOLO IN FONDO**, come nuove entry **con l'orario di aggiunta**.
   **aggiungono** entry e si **aggiornano stato e priorità** di quelle esistenti.
   aggiornano **stato e priorità** di quelle esistenti. Nient'altro.
4. **Nessuna sezione esistente si cancella o si accorcia** — *e dal 2026-10-11 vale anche per le voci*
   **CHIUSE**: possono essere **compresse nella lista**, ma **solo se il testo integrale viene copiato
   nell'archivio in fondo**, sotto `### Voce N — …`, con il rimando nella riga. **Le voci APERTE non
   si comprimono mai.**
   indicando dove sta l'informazione aggiornata.
5. **Motivo**: nelle revisioni precedenti alcune voci sono andate perse (fra cui *Sangue e flash nel
   feedback* e *Camminata troncata*). Da ora la lista iniziale è il **punto unico di verità sullo
   stato**, e il corpo del file è **l'archivio che cresce**.

---

## 📋 LISTA INIZIALE — tutte le voci, con priorità

**Legenda**: 🔴 urgente · 🟡 da fare · 🟢 bassa · ✅ fatto · ⏳ in corso · ❌ caduta · ⏸️ parcheggiata

**Nota sulla forma**: le voci **CHIUSE** sono state **compresse** — *numero, titolo, esito in una riga,*
*rimando all'archivio in fondo* — **perche'** le ultime arrivavano a 2000-3600 caratteri su una riga sola e in
un editor si confondono con un muro di testo. **Il testo integrale di ognuna e' in fondo al file**,
sotto `### Voce N — …`. **Le voci APERTE non sono state toccate.**

| # | Voce | Priorità | Stato |

**CONVENZIONE (dal 2026-10-12, e va rispettata)**: la cella *Stato* di una voce **CHIUSA** è
esattamente `**CHIUSA — integrale in fondo** *(cerca "Voce N —")*`. Così **una regola sola** basta a
distinguere le chiuse dalle aperte — *prima i marcatori erano incoerenti (`✅ RISOLTA`, `CHIUSA`,
`RISOLTO E VERIFICATO`, …) e un conteggio automatico dava risultati sbagliati.*
|---|---|---|---|
| **1** | **Animazioni tagliate** (nemici, morti, Penitente) — §1a | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 1 —")* |
| **2** | **Orientamento che si gira velocissimo** — §1b | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 2 —")* |
| **3** | **Chi si connette dopo non apprende le morti** — §1g | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 3 —")* |
| **4** | **Falò: RESPAWN e REFILL** — §3 | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 4 —")* |
| **5** | **Sangue e flash nel feedback del colpo** — §12.1 *(`§5c` NON esiste)* | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 5 —")* |
| **6** | **Oggetti distruttibili** — §4 | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 6 —")* |
| **7** | **`RegisterEnemies`** — 84.5 ms e allocazione ~0.4 MB/s — §1e(c) | ✅ CHIUSA — non si manifesta | CHIUSA — integrale in fondo *(cerca "Voce 7 —")* |
| **8** | **Boss impazziscono coi target** — §2b | ✅ CHIUSA — superata | CHIUSA — integrale in fondo *(cerca "Voce 8 —")* |
| **9** | **Modalità spettatore** alla morte — §5a | 🟢 | ⏳ **RIDIMENSIONATA**: **fermabile su `DeadScreenWidget.OnDead`** *(`Prefix` che ritorna `false`: niente UI, niente `SetBlocker`, niente `Respawn`)*, **body invisibile** *(`SpriteRenderer.enabled`, che il gioco già usa al falò)*, **camera sul compagno** — **ma il "segue i cambi stanza" è BLOCCATO DAL MOTORE**: le stanze sono **scene Unity**, `OnLevelLoaded` azzera lo stato per-scena, `OtherPlayerManager` distrugge gli oggetti fuori scena, e `ProCamera2D` ha i **bordi geometrici per stanza**. **E il respawn-dal-falò dell'altro dipende dalla voce 4** *(dipendenza dura)*. **Il giocatore remoto è un GUSCIO**: `new GameObject` + `OtherPlayerScript`, con solo `SpriteRenderer` (disabilitato) + `Animator` — **nessun `Penitent`, nessuna camera, nessun collider** ⇒ **camera sul compagno = banale**, *"secondo Penitente" = impossibile*. Vedi §12 | ⏳ **APERTA — ma NON è più quella che questa voce descrive**: il **LIVELLO DI STATO è stato COSTRUITO** *(`Players/PlayerStatus.cs`, `PlayerDeathSync.cs`: `pdead\|`/`palive\|` sul canale `EnemyDeathGlobal` senza bump, `SetPlayerDead`, `CountDeadPlayers`, `IsEveryoneDead`, `AllConnectedPlayers`)* ⇒ **"chi è morto" e "sono tutti giù?" oggi hanno una risposta, che è il requisito della voce 44**. **RESTANO NON FATTI**: la camera sul compagno e il body invisibile. ⚠️ **NESSUN commit e NESSUN referto lo verificano in gioco** *(9 commit citano la parola "spettatore", nessuno lo chiude)*. **Audit 2026-10-12** · ⚠️ **DESCRIZIONE CORRETTA il 2026-10-13** *(e le cose utili trovate dallo `STUDIO-BOSSONE-MORTE-CAM.md` per quando si fara')*: la voce resta **APERTA**, ma **questa cella diceva il vero sullo STATO e non su cio' che manca** ⇒ **vedi in fondo `## [2026-10-13 10:00]`, §4** | ⭐⭐ **CHIUSA il 2026-10-14 — in DUE PEZZI, e col riscontro dell'utente.** *(Il testo sopra **resta** — era la descrizione del 2026-10-13, e allora era giusta.)* **IL PEZZO PRINCIPALE — LA SCHERMATA DI MORTE TRASPARENTE** *(`b9f8360`, l'idea dell'utente: **non si muove la camera, si toglie il velo**)*: si azzerano **solo i `Graphic` figli di `NormalGroup`** *(`DeathScreenTransparency.cs`)* e **MAI `CanvasGroup.alpha`** *(`OnDeadAction` fa `NormalGroup.DOFade(1f, 2.15f)` a `DeadScreenWidget.cs:82` ⇒ il tween lo riscriverebbe)*; **il prompt resta** *(vive su `ContinueGroup`, e **non puo'** essere annidato nello sfondo: in Demake `:165-186` e' acceso mentre `NormalGroup` e' a 0)*. **✅ VERIFICATO IN GIOCO: mai il nero, il prompt resta, la bossfight e la cutscene si vedono** — e **l'utente ha confermato: "tutto giusto funzionante"**. ⭐ **E la scoperta che l'ha reso sufficiente**: **la camera del morto E' GIA' nella bossroom** — il legame camera↔Penitent si scrive **una volta per livello** *(`CameraManager.cs:105`, unico chiamante `LevelManager.cs:349`)* ed e' **misurato**: `cam` **costante**, `target` a **0,5** da `posMia`, su **52 campioni / ~102 s** *(`[DeathScreen] misura`, `91acd22`)*. **L'EXTRA — LA CAM SUL VIVO** *(`04a8fad`)*: **spostare il Penitent E' spostare la camera** ⇒ si crea **una copia del cadavere** e il vero corpo va **sull'avatar del peer**, letto **in LOCALE (zero canale nuovo)**; **da morti non si spedisce piu' posizione/animazione/direzione** *(`OtherPlayerScript.cs:778-782` usa l'arrivo di un'animazione come **PROVA DI VITA**)*; l'inseguimento **si mette in pausa con `HasBlocker("CINEMATIC")`**; **fusibile 360 s che LOGGA**; **fuori scena resta sul cadavere**. ⚠️ **CON LA RISERVA DEL PUNTO 2**: **dal REMOTO il peer morto risulta ancora in piedi** *(il fix locale c'e', il remoto no)* — e 🚨 **l'EXTRA ha introdotto un GUASTO APERTO**: **il peer morto non respawna piu', i tasti non funzionano** *(sospetto: il `Collider2D` mosso ⇒ trigger di scena ⇒ `CINEMATIC` acceso)*, **in corso di riparazione al momento di scrivere**. **Storia intera in fondo: `## [2026-10-14 02:00]`, §1-§2-§3.** |
| **10** | **`ProcessQueue` + `IdOf`** (prestazioni) — §8 | ✅ CHIUSA — non si manifesta | CHIUSA — integrale in fondo *(cerca "Voce 10 —")* |
| **11** | **Il gioco disfa la pausa sul non-proprietario** — ma **il testo della voce era SBAGLIATO a metà** | ✅ CHIUSA — il testo era sbagliato | CHIUSA — integrale in fondo *(cerca "Voce 11 —")* |
| **12** | **Camminata troncata** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 12 —")* |
| **13** | **La migrazione host non riarbitra** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 13 —")* |
| **14** | **`IMPRONTA DIVERSA`** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 14 —")* |
| **15** | **Deploy: `Common.dll` va copiato su ENTRAMBE, nella versione giusta per ciascuna** | ✅ CHIUSA — procedura coperta | CHIUSA — integrale in fondo *(cerca "Voce 15 —")* |
| **16** | ⚠️ **Il deploy va verificato su ENTRAMBE le macchine** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 16 —")* |
| **17** | **I nemici che restano fermi (`pos_distinte=1`)** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 17 —")* |
| **18** | **Boss: test boss per boss** | 🟡 | ⏳ l'unica cosa non prevedibile — **APERTA e VERIFICATA APERTA dall'audit del 2026-10-12**: nessun commit nomina "boss per boss" e l'unico boss esercitato nei referti e' **`ElderBrother`** *(`D17Z01S11`, vita 400→0 con gli **stessi valori** su G1 e G2)*. **Resta la voce più esposta**: la barra del boss **coincide per coincidenza, non per sincronizzazione** *(§2)* — ⚠️ **AGGIORNAMENTO DEL 2026-10-14** *(il testo sopra **resta**)*: **il test di M1 e' stato FATTO e confermato dall'utente** *(la morte del boss sulla macchina del morto: `afca6fe`, e **l'animazione di morte del boss funziona** dopo `18a4500`)* ⇒ **questa voce e' meno urgente di prima, ma NON chiusa**: il fix della morte scriptata **non e' mai stato eseguito su nessuno degli altri 16 boss** *(la firma `Death()` e' verificata **in IL per tutte**, l'esecuzione no)* e la barra del boss resta **non sincronizzata**. `## [2026-10-14 02:00]`, §2.2 e §5* |
| **19** | **`Entity.Dead` calcolato vs `EntityStatus.Dead`** — §7 | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 19 —")* |
| ~~20~~ | ~~Passo 5: le 130 patch sul Penitente~~ | — | ❌ **cade**: `Animator.name == "Body"` su entrambe |
| ~~21~~ | ~~Passo 1: test di controllo single-player~~ | — | ❌ **cade**: la baseline disconnessa c'è già |
| ~~22~~ | ~~Isteresi via `targetTime` su `EnemyBehaviour`~~ | — | ❌ **cade**: `targetTime` è `Private, InitOnly` e **mai assegnato** ⇒ `LookAtTarget` è **codice morto** |
| **23** | **Il desync grave della sessione di test** (eccezione nel ciclo di invio) | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 23 —")* |
| **24** | **Il gioco azzera `consumedSpawns` in DUE metodi**, non uno | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 24 —")* |
| **25** | **Le morti non vengono annunciate se chi le ha viste non è più nella stanza** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 25 —")* |
| **26** | **La pausa NON esisteva per 60 tipi di nemico su 77** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 26 —")* |
| **27** | **Il nemico che resta fermo con un giocatore addosso** — §1h | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 27 —")* |
| **28** | **`EnemySpawner.Reset`: il logging funziona** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 28 —")* |
| **29** | **Dopo un `EnemySpawner.Reset` le due liste locali DIVERGONO** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 29 —")* |
| **30** | ⚠️ **La memoria delle morti si azzera SOLO sulla macchina che si siede** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 30 —")* |
| **31** | **Il falò (`RespawnDeadEnemies`) non è mai stato esercitato nei test** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 31 —")* |
| **32** | **L'arbitraggio host-only è CIECO fuori dalla scena condivisa** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 32 —")* |
| **33** | **`BreakSync`: l'hook non dipinge la rottura** *(memoria dei vasi)* | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 33 —")* |
| **34** | **La dedup del server scarta i ri-annunci** *(di qualunque oggetto persistente)* | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 34 —")* |
| **35** | 🟡 **L'AI sul non-proprietario è IN PAUSA per disegno ⇒ niente aggro spontaneo e parate che non escono** | ✅ RISOLTA | CHIUSA — integrale in fondo *(cerca "Voce 35 —")* |
| **36** | ⚠️ **DOCUMENTAZIONE: i salvataggi cambiano cartella se Steam non è avviato** | ✅ CHIUSA — documentata | CHIUSA — integrale in fondo *(cerca "Voce 36 —")* |
| **37** | 🔴 **L'ECO INFINITA: un'annuncio applicato veniva ri-annunciato** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 37 —")* |
| **38** | ⚠️ **Il log del SERVER non esisteva su disco** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 38 —")* |
| **39** | ⚠️ **La doppia animazione dei vasi all'ingresso in stanza** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 39 —")* |
| **40** | 🟡 **`ReapplyCurrentScene` gira due volte per caricamento di livello** | ✅ CHIUSA — non osservabile | CHIUSA — integrale in fondo *(cerca "Voce 40 —")* |
| **41** | 🔴 **`ProgressManager.Update` è senza `try/catch` né `finally`: UNA eccezione UCCIDEVA la sincronizzazione PER SEMPRE** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 41 —")* |
| **42** | **`AddInteractedObject` senza dedup** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 42 —")* |
| **43** | 🟢 **`if (!localIsHost) return false;` in `DecideOwnership` è probabilmente codice MORTO** | ✅ CHIUSA — scheletro rimosso | CHIUSA — integrale in fondo *(cerca "Voce 43 —")* |
| **44** | **La MORTE di un giocatore respawnava i nemici SOLO per chi muore, e lasciava le due macchine DIVERGENTI** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 44 —")* |
| **45** | 🔴 **`_ids` è appeso dal rescan SOLO sull'host ⇒ il peer NON PUÒ RISCRESCERE, e le due macchine divergono** | ✅ RISOLTA (col fix 45) | CHIUSA — integrale in fondo *(cerca "Voce 45 —")* |
| **46** | 🔴 **Dopo un falò i due mondi possono restare `5` contro `4`** | ✅ RISOLTA (col fix 45) | CHIUSA — integrale in fondo *(cerca "Voce 46 —")* |
| **47** | **La riga dei nemici occupava METÀ del log** — `LogDiagnosticSnapshot` stampava una riga per nemico ogni 0,5 s | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 47 —")* |
| **48** | 🔴 **Un CADAVERE poteva TENERE il boss — e il boss diventava INVULNERABILE** | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 48 —")* |
| **49** | 🔴 **Il nemico resta FERMO PER SEMPRE se l'host entra nella stanza ed esce subito** — segnalato dall'utente, riproducibile | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 49 —")* |
| **50** | ✅ **Gli id degli SPAWN POINT divergevano fra le macchine** *(nome Unity col contatore)* — `SPAWN (11)` vs `SPAWN (15)` | CHIUSA | **CHIUSA — integrale in fondo** *(cerca "Voce 50 —")* |
| **51** | 🔴 **Il nuovo proprietario IMPONE la sua posizione, e nessuno lo corregge dopo il passaggio** — l'altro vede il nemico "slittare" allo spawn | ✅ RISOLTA | CHIUSA — integrale in fondo *(cerca "Voce 51 —")* |
| **52** | 🔴 **Il PROMPT DI ESECUZIONE non condivide l'animazione ai due giocatori** | 🟡 **IN LAVORAZIONE — PIANO D'ATTACCO PRONTO** | **⭐ IL PIANO** *(`scratch\piano-attacco-sync-G1-G2.md`)*: **A1** il suono del colpo relayato *(`Entity.Damage` esige il **PERCORSO** `event:/SFX/Penitent/Damage/PenitentSimpleEnemyHit`, non la chiave di catalogo)* · **A2** le 4 patch di `AnimationFixes` che non mandano nulla · **A3** le SCIE con 2 nuovi membri di `EffectType` · **A4** i VFX a terra + GuardSlide *(chiama i METODI locali, non trasmette stringhe)*. **⭐ NESSUNO richiede il bump**: `SendEffect` manda UN byte, `EffectType` arriva a 60 e il byte a 255; il bump serve SOLO per un `NetworkType` nuovo. **⚠️ TRAPPOLA VERIFICATA IN IL**: `GhostTrailGenerator` fa `EntityOwner.Status.Orientation` e l'avatar remoto è un GameObject nudo ⇒ `EntityOwner` null ⇒ **`followOrientation=false` OBBLIGATORIO**, altrimenti NRE a ogni scia. **⚠️ PREREQUISITO**: `AttackManager.ReceiveEffect` **non logga nulla** ⇒ serve una riga `[Effect] <nome> <tipo>` prima di poter verificare. **ESCLUSI**: l'esecuzione completa *(serve un `NetworkType` ⇒ bump a 12)*, il bump, la trasmissione delle stringhe `event:/`, camera/zoom/slowmotion, le scie via `AnimationStates`. **✅ CHIUSA — A1 `8286d11` · A2 `e6eb9c1`+`1699536` · A3 `b46c651`+`aee38aa` · A4 `b1e21f8`+`060480b` · il registro `AttackManager.ReceiveEffect`/`[Effect]` `f4fd871` · il fix del server che inoltrava senza nome `74f17da`; gli enum verificati nel codice (`EffectType.TrailOn/TrailOff/SlideDust/DashDustStart/DashDustStop` in `Enums.cs`) e i `case` nel ricevente (`OtherPlayerAttackScript.cs:148-165`). Audit 2026-10-12** |
| **59** | 🟡 **A5 — i SUONI locali che il peer non sente**: gli attacchi A VUOTO *(il fendente che non colpisce)* · il suono della SCIVOLATA *(⚠️ verificare se A4 lo copre)* · i PASSI A TERRA dei peer | ✅ **CHIUSA — A5 completa** (`078ba10` `22b7d9d` `a159fa7` `eda9f0a`) **· il CARICAMENTO 101/102 e' DECISO CONTRO** (`1d4fa78`) | **Richiesta dell'utente** durante il lavoro della 52. *"Aggiungi un A5 con i suoni degli attacchi a vuoto, suono della scivolata (non so se hai risolto con A4) ed i passi a terra dei peers."* **⭐ CAUSA COMUNE (dalla triangolazione)**: il clone del giocatore remoto è un **GUSCIO** *(SpriteRenderer+Animator, senza i MonoBehaviour che emettono i suoni)* e **64 behaviour audio-emittenti sono soppressi** di proposito dal `Prefix => animator.name=="Body"`. ⇒ muti: **passi** *(`FootStepEventKey`)*, run-stop, salto, atterraggi, dash, scala, morte, danno, parry, attacchi, carica, cura, **lancio preghiera**, **scivolata**. **⚠️ LA LEZIONE DI A1**: i suoni del Penitent sono per la maggior parte risolti via **catalogo** *(`PlayOneShotEvent(key)` → `CreateCatalogEvent`)* mentre `Entity.Damage`/`EventOneShotPanned` esigono il **PERCORSO DI BANCA** ⇒ **verificare la sorgente di OGNI suono prima di trasmetterlo** *(una chiave al posto di un percorso ha già causato danno-zero + mod congelata)*. **⚠️ E la strada sicura (dal referto)**: quando il suono sta in un campo `[EventRef]` di un prefab *(valore non leggibile dal codice)*, NON trasmettere la stringa: **chiamare il METODO LOCALE** *(esiste anche sull'altra macchina)*. **✅ CHIUSA — A5 completa: i passi e i fendenti a vuoto `078ba10`+`22b7d9d`, i 6 fendenti + quarto colpo + sguaino + attacco aereo `a159fa7`, la SCIVOLATA (unico evento SOSTENUTO) `eda9f0a`. Verificato nel codice: `DashSoundStart`/`DashSoundStop` = 95/96 e i `case` in `OtherPlayerAttackScript.cs:261-267`. Il CARICAMENTO (101/102) e' DECISO CONTRO, per scelta dell'utente (`1d4fa78`). Audit 2026-10-12** |
| **60** | 🟡 **A6 — le ESECUZIONI non sono condivise** *(la clip dell'esecuzione, es. sul WheelCarrier, si vede solo su chi la esegue)* | 🟡 **NUOVA — richiesta dall'utente** | **Richiesta dell'utente**: *"le esecuzioni non mi sembra le abbiamo fatte, aggiungiamolo come A6"*. ⭐ **LA CAUSA (già verificata in IL)**: l'esecuzione NON è un'animazione del nemico NÉ del Penitent — è un **TERZO OGGETTO**: `EntityExecution.InstantiateExecution()` istanzia un **`ExecutionPrefab`** a runtime; il nemico viene NASCOSTO (`SpriteRenderer.enabled=false`), il Penitent reale viene NASCOSTO (`ShowPlayer(false)`) e riceve solo il trigger `USED`. ⇒ la clip visibile è del PREFAB, ed è **locale per costruzione**. **⚠️ PERCHÉ IL GATE NON PASSA**: `Enemy.Execution(Hit)` mescola dati di rete e **STATO LOCALE del Penitent attaccante** (`IsTriggeredRiposte`, `LungeAttack.Casting`, `ReleaseChargedAttack`, `IsAttacking`) ⇒ sulla macchina ricevente sono **FALSI** ⇒ non parte. **⭐ COSTO**: un `NetworkType` nuovo ⇒ **BUMP a 12**, entrambe le macchine. **⚠️ RISCHIO**: replicare un'esecuzione è replicare una **cutscene** (2 nemici uccisi? uno stato appeso?), e il codice avverte (`EnemySyncManager.cs:4413-4429`) che un errore lì **CHIUDE la partita**. **⇒ L'ESITO viaggia già**: il beneficio è solo visivo. **L'unica parte a costo zero è il SUONO** (`PlaySimpleHitToEnemy`) — ma è la stessa chiave di A1 ⇒ verificare se A1 lo copre già. **✅ CHIUSA — e NON ha richiesto il bump che questa voce temeva**: le esecuzioni viaggiano sull'`Effect` esistente. `9b1c296` (le esecuzioni, senza bump) · `f397168` (la cutscene sull'avatar, la strada dell'utente) · `15173fc`+`4a86280` (lo stato `USED` non e' nel controller dell'avatar ⇒ ripiego sull'Animator che lo ha, scelto per probe) · `b3c6e70` (`USED` e' un trigger, non uno stato) · `fa6361c` (l'annuncio parte quando si preme, non quando il nemico e' afferrabile: il postfix passa a `Execution.OnUse`) · `00fa1e5` (il Penitent non ha un'animazione di esecuzione: si istanzia il prefab e si tiene il fantasma) · `60b5c5f` (A6-bis: il renderer dell'avatar non veniva MAI spento). **Residui chiusi nei 3 fix di oggi**: `5547ba6` (tolto il prompt a schermo), `ea8ccfb` (la NRE di `ExecutionAnimationEvents` sul fantasma + il clang acceso due volte). Audit 2026-10-12** |
| **61** | 🟡 **A3-residuo — la scia si disegna col verso SBAGLIATO** *(scivolata verso SINISTRA ⇒ i fantasmi guardano a DESTRA)* | 🟡 **NUOVA — dall'utente** | *"la scivolata verso sinistra fa l'animazione della trail verso destra, solo un piccolo bug grafico"*. ⭐ **CAUSA PROBABILE**: `GhostTrailGenerator.GetGhostTrail` fa `spriteRenderer.flipX = EntityOwner.Status.Orientation == EntityOrientation.Left`, e l'`EntityOwner` che gli abbiamo dato è **il Penitent LOCALE** *(orientamento del giocatore locale, non dell'avatar remoto)*. Con `followOrientation = false` il flip non viene applicato affatto ⇒ il fantasma resta col verso di default. **⇒ FIX DA VALUTARE**: o un **surrogato `Entity`** per l'avatar (con `Status.Orientation` pilotato dal facing dell'avatar) + `followOrientation = true`; oppure **mandare l'orientamento dell'avatar** sul canale esistente *(l'orientamento viaggia già per i nemici)*. **NON è la trappola NRE**: quella è risolta. **✅ CHIUSA — `aee38aa` (FIX voce 61: il verso della scia, scivolata a sinistra ⇒ fantasmi a destra), e il verso della POLVERE (dash e scivolata di guardia) in `0ce8194`. Audit 2026-10-12** |
| **62** | 🔴 **La POLVERE della scivolata di MOVIMENTO (`Dash`) non si vede sul peer** | 🟡 **NUOVA — l'utente ha chiarito che è QUESTA la scivolata che intende** | **L'utente, alla domanda "quale scivolata?"**: *"La scivolata di MOVIMENTO (col tasto)"*. ⭐ **LA SCOPERTA CHE L'HA CHIARITO** *(esploratore, in IL)*: in questo gioco ci sono **DUE** scivolate — *(1)* **`Dash`** = quella di MOVIMENTO (col tasto), la cui SCIA è già coperta da **A3** ma la cui **POLVERE (`DashDustGenerator`) NON è coperta da A4**; *(2)* **`GuardSlide.OnCastStart`** = quella di **GUARDIA** (il contraccolpo di chi PARA un colpo pesante), la cui polvere **È** coperta da A4. **Un solo ingresso in IL per la guardia**: `Penitent.Damage(Hit)` → `CastSlide(Hit)` → `Ability.Cast()` → `OnCastStart()`, sotto `Parry.IsOnParryChance && !hit.Unblockable && (DamageType == Heavy \|\| forceGuardslide)`. **⇒ A4 resta VALIDO (è la scivolata di guardia) ma è un'ALTRA cosa.** **⚠️ E la patch di A4 è sul metodo GIUSTO e NON va spostata** *(le tre righe dell'effetto stanno davvero in `OnCastStart`; spostarla su `CastSlide` manderebbe polvere anche in cooldown, dove il gioco non la disegna)*. **⏳ IN LAVORAZIONE**: piano esecutivo dell'agente, con le 5 lezioni di oggi dentro *(mai trasmettere stringhe di suono · i nomi dei parametri Harmony · il valore di ritorno dei `Prefix` · mai chiamare le abilità sul ricevente · il server inoltra intero E prefissato)* + la riga `[Effect TX]` obbligatoria per l'osservabilità. **✅ CHIUSA — `5b44d7f`** *(la polvere della scivolata di MOVIMENTO: i due generatori `GetStartDashDust`/`GetDashDustPosition` patchati come **EMITTENTI**, e i `case EffectType.DashDustStart`/`DashDustStop` nel ricevente — `Enums.cs`, `OtherPlayerAttackScript.cs`)* **+ `0ce8194`** *(il verso della polvere: dash e scivolata di guardia)*. **Audit 2026-10-12** |

| **53** | 🔴 **I nemici creati DOPO il caricamento (evento locale) non si condividono** — il peer non li vede né li può colpire | ✅ RISOLTA | CHIUSA — integrale in fondo *(cerca "Voce 53 —")* |
| **58** | 🔴 **Le CUTSCENE si perdono per chi non entra in quel momento** — *e lo stesso fa perdere le cutscene dei BOSS se uno muore e torna al falò prima* | 🟡 **NUOVA — e la CAUSA e' chiara** | **Riscontrato dall'utente**: *"quando un giocatore entra in una cutscene (es. la prima volta nella stanza del falò), la cutscene viene PERSA da chi non entra in quel momento — però l'evento viene dichiarato in console"*. **⭐ LA CAUSA (chiarita dall'utente)**: *"la mod di BASE non sincronizza la cutscene: **REGISTRA che è avvenuta**, così evita di farla avvenire di nuovo a chi non l'ha triggerata"*. **⇒ E' ottimo per il PROGRESSO** *(boss · leve · porte · inventario/miglioramenti)* **ma UCCIDE l'ESPERIENZA** *(le cutscene)*. **⚠️ IL PRINCIPIO DA TENERE**: *alcuni eventi sono **PROGRESSO** (condiviso, va registrato), altri sono **ESPERIENZA** (locale, va lasciata staccata). La registrazione è giusta per i primi, sbagliata per i secondi.* **⇒ FIX**: *togliere la CUTSCENE dagli eventi registrati* (non la sincronizzazione: la REGISTRAZIONE), lasciando il resto com'è. **Stesso meccanismo per le cutscene dei BOSS** *(uno muore, torna al falò, e la perde)*. | ✅ **CHIUSA — PER DECISIONE il 2026-10-13** *(non per fix: **la mod base non si tocca**). **MA** il perche' e' cambiato strada facendo: **il danno non e' generale, e' CIRCOSCRITTO alle cutscene dei BOSS per chi muore nella bossroom** — perche' il boss e' **INVULNERABILE sulla macchina di chi e' a terra** *(`ElderBrother.cs:74-79`: `if (Status.Dead \|\| Core.Logic.Penitent.Stats.Life.Current <= 0f) return;`, **replicata su 16 classi di boss**)* ⇒ **non muore li' ⇒ la sua FSM di morte non parte ⇒ la cutscene non parte**. **La storia intera, con le prove e con il perche' della decisione, e' in fondo: `## [2026-10-13 10:00]`, §1.** ⇒ **e la morte del boss sulla macchina del morto ora VIAGGIA lo stesso: e' M1 (`afca6fe`), §2** |
| **54** | 🔴🔴 **BLOCCO TOTALE DEL GIOCO dopo un'esecuzione** — 516 eccezioni non gestite | ✅ RISOLTA | CHIUSA — integrale in fondo *(cerca "Voce 54 —")* |
| **55** | 🔴 **Al falò l'annuncio parte PRIMA del reset locale** — il ricevente azzera il mondo prima del mittente | ✅ CHIUSA — non è un problema | CHIUSA — integrale in fondo *(cerca "Voce 55 —")* |
| **56** | ⚠️ **MANCANO 5 MARKER per poter confrontare per-nemico** — senza di essi ogni diagnosi futura resta aggregata | 🟡 **NUOVA — strumentazione** | **Trovati dal referto, e sono il motivo per cui il confronto per-nemico oggi NON è possibile**: *(1)* **`ShortId` butta via `parts[0]`** ⇒ *tutti i nemici stampano `D01Z01S03@SPAWN`, e **l'unica componente discriminante è persa** (1999/1903 tick, 3917/4431 tree, 54/52 kill)* ⇒ **stampare l'id completo o la posizione dello spawn point**; *(2)* **nessun timestamp nei log** *(`Time.time` basta)*; *(3)* **`IMPRONTA DIVERSA` porta i conteggi SOLO locali** ⇒ *servono quelli del **peer** e **il primo id presente su un lato solo***; *(4)* ⭐ **una riga quando `ResolveByHandle`/`FindIdOfHandle` falliscono** — *è l'istante in cui l'aggiornamento viene **scartato in silenzio***; *(5)* **per il parry**, una riga per-nemico con `enabled`+`isRunning`+non-proprietario **all'uscita della parata**. **⇒ Il (3) e il (4) servono a verificare il fix della voce 45.** **✅ CHIUSA — i 5 marker sono stati TUTTI implementati: (1) `FullId(id)` accanto a `ShortId` (`EnemySyncManager.cs:3144`; `[EnemyTick]`/`[EnemyTree]`/`[Nascita]` usano l'id completo) · (2) `t={Time.time:F1}` nei marker (`:687`, `:2908`) · (3) i conteggi del PEER · (4) la riga quando `FindIdOfHandle` fallisce (`:680`, `:3385`, `:3847`) · (5) i marker del parry. Prova: `dbcc9f4` ("MARKER 1, 2, 4, 5 — i quattro locali, con verifica nell'IL di ognuno") + `ff887a0`. **Audit 2026-10-12** |
| **57** | 🔴 **Il FANTASMA: la morte via SALUTE non disattivava il cadavere** — un nemico con prompt di esecuzione ucciso dall'altro diventava non colpibile | ✅ RISOLTA | CHIUSA — integrale in fondo *(cerca "Voce 57 —")* |

### 🧾 AUDIT DEL 2026-10-12 — RIALLINEAMENTO DEGLI STATI *(`scratch\AUDIT-TODO.md`)*

**Perche' esiste questo blocco.** Un audit ha confrontato **la tabella qui sopra** con **i commit** *(265, dal piu'
recente al piu' vecchio)*, **il codice** e **i referti di `scratch\`**. Ha trovato che **molte voci erano state chiuse
nei giorni scorsi senza aggiornare la tabella** — cioe' il caso peggiore per un documento che si dichiara *"punto
unico di verita' sullo stato"*. **Niente e' stato cancellato**: le righe qui sopra sono state **marcate**, non riscritte.

| Voce | Prima diceva | Ora dice | Prova |
|---|---|---|---|
| **52** | 🟡 IN LAVORAZIONE | ✅ **CHIUSA** | `8286d11` A1 · `e6eb9c1`+`1699536` A2 · `b46c651`+`aee38aa` A3 · `b1e21f8`+`060480b` A4 · `f4fd871` · `74f17da` |
| **56** | 🟡 NUOVA — strumentazione | ✅ **CHIUSA** | `dbcc9f4` (5 marker su 5, ognuno verificato in IL) + `ff887a0` |
| **59** | 🟡 NUOVA | ✅ **CHIUSA** (A5 completa) | `078ba10` `22b7d9d` `a159fa7` `eda9f0a`; il 101/102 e' **deciso contro** (`1d4fa78`) |
| **60** | 🟡 NUOVA | ✅ **CHIUSA** | `9b1c296` · `f397168` · `15173fc` · `4a86280` · `b3c6e70` · `fa6361c` · `00fa1e5` · `60b5c5f` + i 3 fix `5547ba6` `ea8ccfb` `ce2b33e` |
| **61** | 🟡 NUOVA | ✅ **CHIUSA** | `aee38aa` (+ `0ce8194`) |
| **62** | 🟡 NUOVA | ✅ **CHIUSA** | `5b44d7f` *(+ `Enums.cs`: `SlideDust` · `DashDustStart` · `DashDustStop`; `OtherPlayerAttackScript.cs` ricettore)* |
| **9** | 🟢 RIDIMENSIONATA | ⏳ **APERTA** — *stato motivato* | **il livello di stato e' costruito** *(`PlayerDeathSync.cs`, `PlayerStatus.cs`: `pdead\|\|`/`palive\|\|` senza bump)*; **camera e body no, e nessun referto lo verifica** — **⭐ CHIUSA il 2026-10-14** *(`b9f8360` la schermata trasparente, `04a8fad` la cam sul vivo: **verificate in gioco e confermate dall'utente**), **con la riserva del punto 2** *(dal remoto il peer morto risulta ancora in piedi)* e **col guasto aperto del respawn** — `## [2026-10-14 02:00]`* |
| **18** | 🟡 «l'unica cosa non prevedibile» | 🟡 **APERTA — verificato aperto** | nessun commit nomina "boss per boss"; l'unico boss esercitato e' **`ElderBrother`** |
| **63** | *(non esisteva)* | 🟡 **PROMOSSA a voce** | la COLPA *(spine sulla barra, `GuiltManager`)* era **solo una entry datata** (righe 3412-3489) ⇒ **non compariva in nessun inventario** |

⚠️ **Le voci `CHIUSA` di questo audit portano la prova DENTRO la cella** *(hash dei commit, file:riga)*, perche' la
convenzione del file chiede che la chiusura sia **distinguibile dalle altre** e **verificabile**: la cella *Stato* usa la
formula **`✅ CHIUSA — …`** seguita dalla prova, e la convenzione compatta `**CHIUSA — integrale in fondo** *(cerca "Voce N —")*`
resta quella delle voci **52, 59, 60, 61, 62**, il cui testo integrale **non e' stato compresso** *(sono voci APERTE al
momento della scrittura: la regola vieta di comprimerle)*. **⇒ Aprire una voce `CHIUSA` di questo audit NON richiede di
cercare in fondo: il testo e' ancora nella riga.**

⚠️ **E una voce risulta APERTA pur essendo verde in questa tabella: la 9.** La tabella la descrive come un lavoro di
**camera**; il codice mostra che il **livello di stato** e' stato costruito **per un'altra voce (la 44)** e che
**camera e body non esistono**. ⇒ **La descrizione della 9 non e' piu' quella del suo stato.**

**Poi**, in fondo al file: `## 12. ARCHIVIO DELLE AGGIUNTE` (entry datate) e
`## 13. STORICO DELLE REVISIONI` (le versioni precedenti della todo, conservate).

---

Ultimo aggiornamento: **2026-10-14** — ⭐⭐ **LA VOCE 9 (lo spettatore) e' CHIUSA**, in due pezzi e con la parola dell'utente:
**la SCHERMATA DI MORTE TRASPARENTE** *(`b9f8360`: si azzerano i `Graphic` figli di `NormalGroup`, **mai `CanvasGroup.alpha`** che
`DOFade` riscrive)* e **LA CAM SUL VIVO** *(`04a8fad`, il cadavere-copia: **spostare il Penitent E' spostare la camera**)*, piu' **i due
ritocchi** *(`68f3b0e` il morto "in piedi" — la causa era **la POSA della copia**, non la visibilita'; `18a4500` la morte scriptata del
boss)*. ✅ **Confermato dall'utente**: *"tutto giusto funzionante"* e *"l'animazione della morte del boss funziona"*. 🚨 **MA c'e' un
GUASTO APERTO, IN CORSO DI RIPARAZIONE**: **il peer morto non riesce piu' a respawnare, i tasti non funzionano** *(`DeathCorpseFollow`
muove il `Collider2D` ⇒ puo' far scattare un trigger ⇒ `CINEMATIC` acceso)* — **la sua riserva era stata DICHIARATA, non prevista**.
⇒ **e la regola che ne esce: una modifica che sposta un `Collider2D` in un mondo vivo va trattata come una modifica al GIOCO, non
cosmetica.** E **resta aperto il punto 2**: *dal REMOTO il peer morto risulta ancora in piedi*. Tutto in fondo: `## [2026-10-14 02:00]`.
Ultimo aggiornamento precedente *(2026-10-13)*: **M1, la morte del boss sulla macchina di chi e' a terra** *(implementato, **NON
ancora verificato in gioco**)*: **voce 58 CHIUSA PER DECISIONE**, **voce 9 con la descrizione CORRETTA**, **voce 63 con la
formulazione dell'utente**, **probe ridotte da 3 a 2**. Tutto in fondo: `## [2026-10-13 10:00]`.

⚠️ **AGGIORNAMENTO DEL 2026-10-14** *(le righe sopra **restano**, come storia — e questa e' la piu' recente)*: ⭐⭐ **LA VOCE 9
E' CHIUSA.** Delle quattro elencate nel 2026-10-13, **una non e' piu' aperta**: la **9** *(chiusa in due pezzi — la **schermata di
morte trasparente** e la **cam sul vivo** — `§1` della sezione in fondo)*, **e la sua "riserva" e' il punto 2 della lista** *(dal
remoto il peer morto risulta ancora in piedi)*. **E M1 ha cambiato stato**: *"implementato, non verificato"* **non e' piu' la sua
dicitura** — **la morte del boss sulla macchina del morto e' stata verificata in gioco e confermata dall'utente** *(l'animazione di
morte del boss "funziona": `18a4500`, §2.2)*, **e il fix dei due ritocchi e' arrivato** *(`68f3b0e` + `18a4500`)*. **⇒ Restano
davvero TRE voci + le cose piccole** *(18 · 63 · le piccole e le domande §9)* **e due cose che non sono voci**: 🚨 **il GUASTO APERTO
del respawn** *(§3: **aperto, in corso di riparazione** — la priorita' assoluta)* e ⚠️ **il morto "in piedi" VISTO DAL REMOTO** *(il
fix locale c'e', il remoto no)*. **La lista operativa e' la §5 in fondo a questa sezione.**

Ultimo aggiornamento precedente: **AUDIT DEL 2026-10-12** — la lista e' stata riallineata al vero. **CHIUSE in questo audit: 52, 56, 59, 60, 61, 62** *(prove nei commit)*, e **la COLPA e' stata promossa a voce 63**.
~~**Restano**: la voce 33 *(misura)*, il passo 2 dell'arbitraggio *(voce 32)*, il falò *(voce 4 + 29/30/31)*, lo spettatore *(9)*, i boss *(8/18)*.~~ **⇒ QUESTA RIGA ERA VECCHIA DI CINQUE GIORNI E CONTRADDICEVA LA PROPRIA TABELLA**: 33, 32, 4, 29, 30, 31 e 8 **erano già chiuse in tabella** *(rispettivamente `f736cc3`, `9941eb9`, `6154863`, idem, idem, idem, `416fada`)*. **Restano davvero**: lo **spettatore (9)**, i **boss (18)**, le **CUTSCENE (58)**, la **COLPA (63)** e i **5 marker (56 → chiusa in questo audit)**.

⚠️ **AGGIORNAMENTO DEL 2026-10-13** *(la riga sopra **resta**, come storia)*: delle cinque elencate li', **due NON sono piu' aperte** —
la **58** e' **CHIUSA PER DECISIONE** *(§1 in fondo: il danno e' circoscritto alle cutscene dei boss per chi e' a terra, e la
mod base non si tocca)* e la **56** e' **chiusa dall'audit del 2026-10-12** *(i 5 marker)*. **Restano davvero, e sono QUATTRO**:
**9** *(corretta, §4)* · **18** *(+ il test di **M1**)* · **63** *(con la formulazione dell'utente, §3)* · **M1 in attesa di
verifica in gioco** *(§2.9: **implementato**, non chiuso)*. **La lista operativa e' la §6 in fondo.**



---

## STATO DEL PROGETTO

**Il grosso è concluso e verificato.** La riga originale diceva: *"Restano lavori mirati: l'**orientamento** (in verifica), il **feedback del colpo**, gli **oggetti distruttibili**, la **diagnostica**, i **boss** e lo **spettatore**"* — **ed era vecchia**: orientamento *(voce 2, `9ecf8ae`)*, feedback del colpo *(voce 5, `110b495`)* e distruttibili *(voce 6, `cfe7d6b`)* sono **chiusi da giorni**. **Restano davvero**: i **boss** *(18)* e lo **spettatore** *(9)*, piu' le **cutscene** *(58)* e la **COLPA** *(63, promossa da questo audit)*.


| Cosa | Esito |
|---|---|
| Movimento e posizione dei nemici | ✅ funziona |
| Morte animata per mano dell'altro giocatore | ✅ funziona |
| **Fantasmi** | ✅ **risolti** |
| **Boss fight** | ✅ **un boss condiviso con barra e morte sincronizzata** (vedi §2) |
| Cutscene e dialoghi | ✅ nessun problema (serve SPAZIO, non ESC) |
| **Chi si connette dopo** | ✅ **apprende le morti precedenti** (§1g, verificato) |
| **Falò** | ✅ **respawn allineato** (§3, verificato) |
| **Animazioni (nemici, morti, Penitente)** | 🟡 **accettabili** — `animRepeat` a 0, ma non totalmente fluide |
| **Orientamento** | ✅ **CHIUSO e verificato** *(voce 2, `9ecf8ae`: `orientFlip` da **85** a **1** su G1 e **3** su G2) |
| **Sangue e flash** | ✅ **CHIUSO e verificato dall'utente** *(voce 5, `110b495`) — e la diagnostica iniziale era **INVERTITA** |
| **Diagnostica / prestazioni** | ✅ **CHIUSE** *(voci 7 e 10, `c55d80e`; il passo 4b e' stato risolto **separando la cadenza del log dal rescan**, `215c4f1`)* |
| **Spettatore** | ⏳ **APERTO** *(voce 9: il livello di stato e' costruito, camera e body no)* — ⭐ **SUPERATO il 2026-10-14: la voce 9 e' CHIUSA** *(la schermata di morte trasparente `b9f8360` + la cam sul vivo `04a8fad`: **verificate in gioco, "tutto giusto funzionante"**)*, **con due riserve che restano aperte**: ⚠️ **dal REMOTO il peer morto risulta ancora in piedi** e 🚨 **il peer morto non riesce piu' a respawnare** *(guasto aperto, in corso di riparazione)* — `## [2026-10-14 02:00]` |
| **Boss** | 🟡 **APERTO** *(voce 18: un solo boss esercitato)* |

### Punti di ritorno

| Tag | Cosa rappresenta |
|---|---|
| `enemy-sync-death-risolta` | morte sincronizzata, prima delle features |
| `stato-pulito-pre-features` | tag stabile + igiene del repository |
| `stato-features-1-7` | le 7 features riapplicate dal ramo WIP |
| `stato-token` | token di proprietà con le quattro correzioni |
| `stato-placeenemy-fix` | scrittura della posizione nella fisica, ordine corretto |
| `stato-revisione-il` | revisione IL: fallback, pausa persistente, collider allineato |
| `stato-thaw-fix` | ThawPhysics non salta più il ripristino |
| `stato-arbitro-pari` | proprietà decisa dalle distanze, non dall'ordine di connessione |
| **`stato-sync-movimento-ok`** | **movimento e posizione sincronizzati** |
| `stato-morte-feedback` | morte robusta, feedback del colpo, boss esclusi |
| **`stato-morte-animata-ok`** | **morte animata anche per mano dell'altro giocatore** |
| **`stato-passo0-strumentazione`** | **passo 0: solo misure, nessun cambio di comportamento** |
| **`stato-passo2-animtime`** | **passo 2: la posizione di riproduzione non viene più azzerata** |
| **`stato-inert-scenery`** | **le entità di scena non vengono più tracciate come nemici** |
| **`stato-join-fix`** | **primo tentativo del fix del join (SBAGLIATO: usava `ProgressType.Flag`)** |
| **`stato-join-fix-2`** | **join riscritto sul canale `EnemyDeathGlobal` + fix `_pendingDeaths` al falò** |
| **`stato-join-fix-3`** | **+ pulizia del flag spazzatura nel salvataggio** |
| **`stato-orient-probe`** | **Prefix solo-misura su `Entity::SetOrientation`** |
| **`stato-orient-isteresi`** | **isteresi in `ApplyOrientation` + contatori per-nemico** |


---

## 1. ANIMAZIONI TAGLIATE — il problema principale

### 1a. Il meccanismo lato nemici (verificato nel codice, non ipotesi)

**Il filo di rete è a posto.** `NetworkManager.SendEnemyUpdate` manda un `float` **completo**
(`BitConverter.GetBytes(animTime)`, offset 20) e il ricevente lo rilegge integro
(`BitConverter.ToSingle(data, 20)`). **È il mittente che lo azzera di proposito.**

**Il taglio, in due tempi:**

1. `EnemySyncManager.cs:1730-1731` → `if (!hasPrevious || previous.AnimState != animState) animTime = 0f;`
   **azzera `normalizedTime` esattamente quando lo stato cambia**, che è l'unico momento in cui il
   ricevente ne avrebbe bisogno.
2. `ApplyAnimation` (`:2086-2106`) esce subito se il ricevente è **già** in quello stato, quindi **non
   riposiziona mai** un clip già in corso → ogni transizione ricevuta ripete `Play(hash, 0, 0f)` →
   **riavvio del clip da zero**.

**Difetto aggiuntivo, nuovo e importante:**

> `state.shortNameHash` **non identifica lo stato in modo stabile dentro un Sub-State Machine** —
> restituisce il nome dello stato **interno**. Le due macchine, con timing e blend diversi, leggono
> hash diversi per lo **stesso** stato logico → il test di uguaglianza in `ApplyAnimation` **fallisce
> quasi sempre** → `Play` viene chiamato **a ogni aggiornamento**, non solo al cambio di stato.
> Questo spiega perché il taglio è molto peggiore di quanto il solo `animTime = 0` farebbe prevedere.

**Prova nei log — e una CORREZIONE importante.** Il conteggio "86%/85% di righe con `avvio=0.00`" è
una prova **DEBOLE**: `LogAnimationChanges` campiona **dopo** che il gioco ha già cambiato stato,
quindi **anche una transizione legittima** appare con `avvio≈0.00`. **Non usare quella statistica.**

**La prova buona è la RIPETIZIONE IDENTICA** — la stessa coppia di hash, per lo **stesso** nemico,
ripetuta:

```
G2 (client): (14) 765711723 -> 2081823275 avvio=0.00    × 33 volte
G2 (client): (12) 460696939 -> 2081823275 avvio=0.00    × 58 volte
```

**a ~6 riavvii al secondo, con `fps 59` e `frameMs_max 19-31 ms`** — cioè **in frame sanissimi**. Su
113 righe `[EnemyAnim]` del client, **101** con `avvio=0.00`, di cui **91 in quei due burst: ~80% dei
riavvii osservabili**. Una transizione **legittima avviene una volta**; 33-58 volte è **questo mod**
che riavvia il clip.

> ### 🟡 ACCETTABILE — priorità ABBASSATA (deciso dall'utente)
> **Le animazioni NON sono totalmente fluide, ma sono ACCETTABILI per il momento.** Il passo 2 le ha
> migliorate **in modo misurabile** (nimRepeat da 33-58 a **0**), e l'utente ha stabilito che la
> priorità scende **molto più in basso della modalità spettatore** (§5a). **Non è un capitolo chiuso:
> è un capitolo PARCHEGGIATO con una soglia di qualità accettata.**
> Resta come **irrobustimento** il passo 4 (ullPathHash + riposizionare davvero), da riprendere
> **solo se** la fluidità tornasse a dare fastidio.

> ### ✅ RISOLTO E VERIFICATO (passo 2)
> Rimossa la riga che azzerava `animTime`, i contatori sono passati **da 33-58 a ZERO** su entrambe le
> macchine: `animRestart=0`, `animRepeat=0`, `stalls100=0`, `fps≈60`, e **`syncMs` invariato**
> (0.02-0.18 ms) → **la cura non è costata nulla altrove**. L'utente riferisce animazioni **fluide**.
> **Il capitolo §1a è CHIUSO.** Restano aperti §1b (orientamento) e §1g (join).

**Riferimenti di riga corretti** (quelli vecchi si erano spostati): `animTime = 0f` **:1754**,
`ApplyAnimation` **:2109-2129**, `ReadAnimation` **:2095**, `IdOf` **:1032**, `LogAnimationChanges`
**:2017**, `RegisterEnemies` **:917**, `LogDiagnosticSnapshot` **:1210**.

### 1b. 🔴 Il nemico si gira a destra e a sinistra velocissimo — meccanismo CORRETTO

> **La versione precedente di questa sezione era SBAGLIATA su un punto decisivo.** Riscritta dopo
> verifica in IL. **Due correzioni sostanziali**: il writer per-frame è `Ghost::OnUpdate` (non
> `EnemyBehaviour::LookAtTarget`), e **la "protezione" del gioco è CODICE MORTO**.

**Il writer per-frame è `Ghost::OnUpdate`** — `Gameplay.GameControllers.Enemies.Ghost.Ghost`
(`: Enemy <- Entity <- MonoBehaviour`), `protected virtual void OnUpdate()` **[verificato in IL]**:

```
IL_007e  ldfld Ghost::ghostSpriteRenderer → get_transform → get_position   → V1
IL_0085  ldfld Ghost::_penitent           → get_transform → get_position   → V2
IL_009d  blt.un  → IL_00b0
IL_00a6  callvirt Entity::SetOrientation(Left,  true, false)
IL_00ea  callvirt Entity::SetOrientation(Right, true, false)
```

**Nessuna banda morta, nessuna soglia temporale, nessuna memoria dell'orientamento corrente, nessun
gate di distanza.** È un **confronto nudo di due float**. `_penitent` è il **Penitente LOCALE**
(assegnato una volta in `Ghost::OnStart` da `Core.Logic.Penitent`), **non** un target di rete.
`Ghost::EnablePhysics(bool)` → **`throw new NotImplementedException()`**: nessun altro writer di
orientamento per questo tipo. `Ghost` **non assegna `EnemyBehaviour`** (coerente con §7j).

**E la protezione del gioco NON esiste — in un senso più forte di quanto scritto prima:**

`EnemyBehaviour::LookAtTarget(Vector3)` **[verificato in IL]**:
```
IL_0016  DeltaTargetTime += Time.deltaTime
IL_0028  ldfld DeltaTargetTime ; IL_002f ldfld targetTime ; IL_0034 blt.un → IL_0110: ret
...      (banda ±1 + SetOrientation)   ← TUTTO QUESTO È IRRAGGIUNGIBILE
```
`EnemyBehaviour.targetTime` è **`Private, InitOnly` (readonly)** e **nessun `stfld …targetTime`
esiste in tutta `Assembly-CSharp.dll`** (verificate tutte le istruzioni che nominano
`targetTime`/`DeltaTargetTime`: gli unici `stfld` sono su
`IsidoraBehaviour/WaitUntilAnticipationPeriod::targetTime`, **altra classe**).
⇒ vale **`0f`** ⇒ `DeltaTargetTime < 0` è **sempre falso** ⇒ `blt.un` non salta mai ⇒
**tutto il corpo è codice morto**. **Le 71 sottoclassi di `EnemyBehaviour` (contate: 71) non girano
mai il nemico.**

**Chi gira davvero, tipo per tipo:**

| Writer | Esito |
|---|---|
| `EnemyBehaviour::LookAtTarget` | ❌ **CODICE MORTO** (gate `targetTime=0` non superabile, 0 assegnazioni in tutta l'assembly). **71/71 sottoclassi escluse** |
| `TrinityMinionBehaviour::LookAtTarget` | ✅ **override che elimina davvero il gate temporale** (nessun `ldfld targetTime`: `IL_0000` Dead → `IL_0027` banda ±1 → `IL_006c` `SetOrientation`). **Confermato, ma è UN SOLO tipo** |
| `EnemyAI::FixedUpdate` | ⚠️ chiama `reverseOrientation` (`@IL_02a7`, `@IL_02e3`) → `SetOrientation` con banda ±1 e nessun gate temporale, ma **`EnemyAI` non ha NESSUNA sottoclasse (0/0)** e nessun `Update`/`OnFixedUpdate` pubblico. **Probabile codice morto — NON VERIFICATO** (potrebbe esistere un'istanza in un prefab) |
| `Ghost::OnUpdate` | ✅ **il writer per-frame verificato** |

**Firma nei log — quantificata, per POSIZIONE.** Nemico libero `D01Z01S01@SPAWN` a
**(-715.1, 10.0)**, vita 12: **è il nemico #1** (all'inizio il più vicino a G1, `dist_io=14.0`
contro 32.0-48.9, ed è l'unico che G1 aggrada per primo).

| | righe | `dALT` | inversioni |
|---|---|---|---|
| **esplosione G1** | **G1:590-636** (~1,7 s) | 3.8 → 2.2 | **31**, di cui **24** = `765711723→460696939` (**≈18/s**) |
| **esplosione G2** | **G2:837-1020** (~10,4 s) | 3.6-4.1 | **106**, di cui **99** = `765711723→2081823275` (**≈10/s**) |
| **allontanamento** | G2:1044-1164 | 5.1 → **27.6** | **0** — *"si è stabilito"* |

- `animRestart`/`animRepeat` = **31-33 per finestra**, **tutte con `avvio=0.00`**.
- **Frame sanissimi**: `fps 58.4-59.3`, `frameMs_max 26.6-32.2`, `stalls100=0` ⇒ **non è prestazioni**.
- **Il nemico resta FERMO mentre si gira**: 0.8 unità in 1,7 s con **24 inversioni**.
- **|x_nemico − x_target| resta sotto 1 unità ma il segno si inverte.**

**Perché serve il caso "due giocatori vicini" — ed è il giocatore a jitterare, non il nemico.**
Il fantasma insegue il Penitente locale con un'oscillazione idle di ~±1 unità; **ma il target si
muove di rete**: `MainPlayerManager.Update` (`positionCutoff = 0.03f`) invia una posizione **a ogni
frame** (lo spostamento per frame supera 0.03), e il ricevente la scrive **GREZZA** in
`_remotePositions` (`OtherPlayerManager.cs:329`), mentre **solo il nemico** passa dal lerp
`FOLLOW_LERP = 0.25`. ⇒ **il target del fantasma è la posizione di rete non filtrata, che jittera di
2-6 unità**, e il confronto nudo la insegue.

**Aggravante non verificata**: `PauseEnemy` ferma il behavior tree NodeCanvas e la fisica, **non**
`MonoBehaviour.Update` ⇒ `Ghost::OnUpdate` gira **anche sul non-proprietario**, insieme ad
`ApplyOrientation`, che scrive a **20 Hz contro i 60 Hz del gioco**.

**Soluzione: una Prefix con isteresi su `Entity::SetOrientation`, più la stessa isteresi in
`ApplyOrientation`.**

`Gameplay.GameControllers.Entities.Entity::SetOrientation(EntityOrientation, Boolean, Boolean)` —
`attrs = Public, Virtual, HideBySig, VtableLayoutMask` **[verificato in IL]**. La Prefix **scarta**
una richiesta di inversione quando `|x_nemico − x_target| < ~1.5-2.0` unità.

| Strada | Verdetto |
|---|---|
| ✅ **Prefix con isteresi su `Entity::SetOrientation`** | **La più semplice e robusta.** **Una sola armonica copre TUTTI i writer per-frame** (nessuno si può dimenticare), **non tocca il game loop** (è un filtro su una setter), e **non cede né forza** l'orientamento ⇒ **non può produrre ping-pong fra le macchine**. `Entity::SetOrientation` è **virtuale** e **override-ata** da `Amanecidas`, `Isidora` e dal `Penitent` **[IL]** ⇒ **i boss e il giocatore NON passano di lì e restano intatti** |
| ⚠️ **Non scrivere l'orientamento dove il gioco lo fa ogni frame** | **Giusto come principio** e va fatto, **ma da solo NON risolve**: il gioco riscrive subito dopo (60 Hz contro 20 Hz) |
| ❌ **Postfix** che riporta l'orientamento del proprietario | **Peggiore**: se il proprietario **jittera**, la Postfix **replica il jitter** a 60 Hz. **Serve la Prefix.** *(la versione precedente di §1b diceva "Postfix": corretto qui)* |
| ❌ **Assegnare `targetTime`** | **Non si può** (`Private, InitOnly` **[IL]**) e **non servirebbe** (`LookAtTarget` è codice morto e `Ghost::OnUpdate` non lo legge mai). **⇒ il passo 9 non ha più un meccanismo su cui agire: è assorbito nel 7** |
| ❌ **"Non applicare l'orientamento quando entrambi sono vicini"** | Il caso peggiore è reale **ma non è la causa**: l'inversione avviene **anche con un solo giocatore vicino** (G1:590-636, il fantasma è a 5.7 unità dal target che insegue) |

**Rischio: medio. Costo: basso.** Da verificare per primo: che un'inversione **reale** (giocatore che
passa davanti a 5+ unità) avvenga **comunque una volta sola**.

> ### ⚠️ LO STRUMENTO `orientFlip` MISURA LA COSA SBAGLIATA (accertato)
> Il contatore è chiamato **solo** dentro `ApplyOrientation`, cioè **le scritture della MOD** (20 Hz).
> **Le inversioni che si vedono sono quelle del GIOCO, ogni frame** — quindi **`orientFlip=0` non dice
> nulla sul fenomeno**, dice solo che *noi* non abbiamo scritto l'orientamento.
> **Prova**: nella sessione di conferma `orientFlip` è **0 su 111 report e su entrambe le macchine**,
> **ma il fix NON è implementato** — quindi non può essere risolto. L'utente: *"non abbiamo ancora visto
> il fix"*.
>
> **Conseguenza sull'ordine**: per **misurare** le scritture del gioco serve **la stessa Prefix Harmony**
> che sarebbe il fix. Quindi la Prefix **va scritta in ogni caso**, e **conviene scriverla prima in
> versione "solo misura"** (conta, non blocca → rischio quasi nullo), **poi**, se il fenomeno si
> ripresenta, **attivare l'isteresi con una riga**.

**Gravità: 🔴 alta come disturbo visivo, non bloccante, auto-limitante.** Puramente **cosmetico**:
posizione, vita e morte non ne sono toccati.

### 1c. La morte parte e viene skippata

Chiarito dal test: **l'animazione di morte parte**, ma in alcuni casi **viene troncata dopo pochi
millisecondi**. **Non è un problema di morte: è 1a.** **Il capitolo "fantasmi" è chiuso.**

### 1d. ✅ CHIUSA — le 130 patch NON c'entrano (misurato col passo 0)

Era il sospetto peggiore: `AnimationFixes.cs` installa **~130** `[HarmonyPatch]` sui metodi
`OnStateEnter/OnStateUpdate/OnStateExit` delle classi
`Gameplay.GameControllers.AnimationBehaviours.Player.*`, **tutti** con
`public static bool Prefix(Animator animator) => animator.name == "Body";`. Poiché quei metodi
restituiscono `Void`, un prefix che torna `false` **salterebbe il corpo originale** — e fra quei
corpi ci sono `MoveAnimationBehaviour` (che imposta `Animator.speed = 1`) e
`GroundAttackSubStateBehaviour` (che imposta la velocità d'attacco): **esattamente** camminata e
combattimento.

**La misura del passo 0 lo ha chiuso:**

```
[Probe] PENITENT animator.name='Body' animatorGameObject='Body' root='Penitent(Clone)'
[Probe] Le 130 patch su animator.name == "Body" CORRISPONDONO
```

Su **entrambe** le macchine, **4 volte ciascuna** (una per cambio scena). Il prefix torna `true`,
il corpo originale **non viene saltato**.

**E in più**, verificato nell'intero sorgente della mod: quei prefix **non toccano l'Animator** —
fanno solo `Main.Multiplayer.NetworkManager.SendEffect(...)` (7 casi) e per il resto sono guardie
pure. **In tutta la mod non esiste alcuna scrittura sull'Animator del Penitente locale**
(`EnemySyncManager:2126` è un nemico, `TriggerReceiverHelper:48` un interruttore di livello,
`OtherPlayerScript` scrive sull'Animator del **ghost**). Nessun `Time.timeScale`, nessun
`Animator.speed`.

⇒ **Il passo 5 del piano CADE.**

**Resta aperta, come causa separata e NON identificata**, l'osservazione che anche il Penitente
risulti tagliato. **Candidato falsificabile, non verificato**: il Penitente che si guarda in co-op
potrebbe essere **il ghost**, la cui animazione è pilotata **per nome** (tabella di 88 voci) e
**sempre da frame 0** (`OtherPlayerScript` → `CharacterAnim.Play(name)`), **senza posizione di
riproduzione**.

### 1e. IPOTESI DI CARICO — misurate: il carico *medio* è nullo, il *picco* no

Numeri (102 report per macchina, 2 s ciascuno):

```
syncMs_avg  = 0.02 – 0.19 ms          ← il costo medio di EnemySyncManager.Update
lateMs_max  = 2 – 4 ms tipici          ← per TUTTO Multiplayer.OnLateUpdate
gc0         = 0 nella maggior parte delle finestre (43 al primo report = avvio)
managedMB   = sega 71→85 MB, una gen0 ogni ~20 s, 9-13 MB per ciclo
              ⇒ ~0.4-0.65 MB/s di allocazione continua (non zero, ma non causa stallo)
```

| # | Ipotesi | Esito |
|---|---|---|
| a | `IdOf` O(N²) | ❌ **smentita come causa**: nelle stanze dei picchi (0 nemici) la lista è vuota |
| b | `LogAnimationChanges` ogni frame | ❌ **smentita come stallo**: 604/1303 righe `[EnemyTick]` = I/O sincrono 2×/s **senza alcun picco corrispondente** (frame 19-45 ms) |
| c | **`RegisterEnemies`** ogni 0,5 s → `FindObjectsOfType` + `GetRootGameObjects()` + `GetComponentsInChildren<Enemy>(true)` per root | ✅ **RESTA VIVA**: è **l'unico codice dentro `UpdateCore` capace di un frame da 84.5 ms** misurato con `_ids` **vuoto**, ed è coerente con ~0.4 MB/s |
| d | `ProcessQueue` alloca per messaggio | ⚠️ **confermata come allocazione, NON come stallo** (nessun picco correlato) |
| e | `PvPPatches` su `Entity.Damage` | ❓ non misurabile con questa strumentazione |
| f | `RunInBackgroundFix` | ❓ non misurabile con questa strumentazione |

> **Il passo 4 (diagnostica dietro flag, `RESCAN_INTERVAL` più lungo) trova la sua giustificazione
> qui — negli 84.5 ms e nell'allocazione — NON negli FPS.**

### 1f. STALLI DI FRAME (150-512 ms) — esistono, non sono la causa dei tagli

Picchi ≥60 ms: **3/102** su G1, **11/102** su G2. ≥100 ms: **6 in totale**, in due macchine e ~7
minuti (`512.0` e `205.0` all'avvio, `177.3` e `150.8` alla connessione, `124.1` all'avvio,
`106.5` in una stanza vuota).

**Collocazione** (i log **non hanno timestamp**: solo ordine di riga, e solo "quali eventi cadono fra
due report da 2 s"):

| Quando | Picchi | Contesto |
|---|---|---|
| **avvio** | 205.0 / 512.0 / 124.1 | `2× LoadAndActivateLevel` + `LOADGAME` + `SAVEGAME` + 2 coppie `BLOCK_UNTIL_FPS_STABLE`; **disconnessi, 0 nemici, `gc0=43`** |
| **connessione** | 150.8 e 177.3 | con `lateMs_max` 142.3 / 167.1 ⇒ **dentro `Multiplayer.OnLateUpdate`**: la stretta di mano della mod, ~150-170 ms **una volta per sessione** |
| **una porta** | 82.4 | `load + SAVEGAME` nella stessa finestra |
| **cluster su G2** | 94.4 / 76.6 / 78.6 / **106.5** / 69.2 / 81.0 | stanza con **0 nemici e 0 id**, **nessun evento** nella finestra. Nel frame da 106.5 ms, **84.5 ms dentro `EnemySyncManager.Update`** → §1e(c) |

**Candidati accertati in IL:**

- **`UnityEngine.Resources.UnloadUnusedAssets()`** — **unico call site in tutta l'assembly**:
  `Framework.FrameworkCore.Level/<Activate>c__Iterator2::MoveNext @IL_0179`, **sincrono sul main
  thread a ogni attivazione di livello**. È lo stallo classico da centinaia di ms.
- `LevelManager/<LoadAndActivateLevel>c__Iterator3::MoveNext`: `WaitForPreloadCacheScene` +
  `level.Activate()` in loop + `InitializeLevel` + **5× `WaitForEndOfFrame`**; nel frame ci stanno
  `UIManager::RefreshReferences` (**5 `FindObjectOfType`**) e `SpawnManager::SpawnPlayerOnLevelLoad`
  (**5 `FindObjectsOfType`**).
- `PersistentManager::SaveGame_Internal`: serializza tutto il save in JSON + Base64 + **scrittura su
  disco sincrona**, **6 volte per macchina** (a ogni porta/`OnBeforeLevelLoad`).
- `RegisterEnemies` (§1e c).

**Perché NON spiegano i tagli:** la coincidenza picchi↔taglio è **0/3** su G1 e **1/11** su G2 (e
quell'uno è la porta); i due picchi più grandi avvengono con **0 nemici e 0 id**, quando **non c'è
nulla da tagliare**; i tagli avvengono a **59 fps con `frameMs_max` 19-45 ms**. Le due cose **possono
sommarsi**, ma nei log **non si presentano mai insieme**.

**Nota**: `BLOCK_UNTIL_FPS_STABLE` è la logica `if (1/smoothDeltaTime < 50 && timeWaiting < 3) attendi`
[IL]: è la **conseguenza** dello stallo, non la causa, e **non è una metrica**.

### 1g. 🔴 PRIORITÀ ALZATA — chi si connette DOPO non apprende le morti già avvenute

> **Era 🟡 e "da non affrontare ora". L'utente ha chiesto di alzarla: NON più rimandato.**

**Firma nei log — per POSIZIONE** (le etichette `SPAWN (NN)` **si permutano**, §1h/§7i).

**Chi è host**: `[EnemySync] Host role: True` compare **solo** su **G2** (riga 672);
`Host role: False` solo su G1 (riga 275). G1 si connette a `localhost`, G2 a `213.167.233.85`.

| # | Evento | Dove |
|---|---|---|
| 1 | Entrambi in `D01Z01S01`, **disconnessi**, ognuno simula per conto suo (`Post-spawn: 5/7/5`) | G1:250, G2:301 |
| 2 | **G2 uccide OFFLINE** il nemico a **(-698.9, 11.0)** (`vita 60→10→0`, anim `1080829965→646380074`) | **G2:494-501** (`mia=host`) |
| 3 | **G2 si connette** (`Host role: True`) | G2:670-672 |
| 4 | G1 (già connesso) riceve `Notification: G2 has joined` + **32 `Received new game progress:`** — **solo progress** | **G1:277-342** |
| 5 | **G1 vede il nemico VIVO**: `(15) … life=60 posNemico=(-697.1,11.0)` | **G1:738**, poi 742-884 |
| 6 | G1 lo uccide **per conto suo, a un'altra x**: `(15) … pos=(-696.7,11.0)` | **G1:900-901** |

**DUE FATTI NUOVI che il fix deve rispettare:**

1. **Le due macchine hanno ucciso lo STESSO nemico a posizioni DIVERSE** — G2 a **x=−698.9**, G1 a
   **x=−696.7** (**2.2 unità**), mentre lo **spawn point** è a **(−697.1, 11.0)**.
   ⇒ **va scambiata la posizione dello SPAWN POINT, non quella dell'uccisione.**
2. **Il progress arriva SOLO al joiner**: `Server.sendPlayerTeam` (`Server.cs:382-398`) spedisce il team
   **solo a chi entra**; **chi è già dentro non riceve nulla di nuovo**.
   ⇒ la riconciliazione va **RI-ANNUNCIATA DAL VIVO a ogni join**, non "mandata al joiner".

**Meccanismo (verificato nel codice, ed è strutturale):**

- La morte esiste **solo come evento live**: `SendEnemyKill` (`:1714`) + `SendEnemyDeathGlobal`
  (`:1717`) stanno dentro `UpdateCore`, che **esce subito se `!IsConnected`** → una morte avvenuta
  **da disconnessi non viene accodata né ritrasmessa**; subito dopo l'id viene droppato
  (`ForgetEnemy`) quindi **non torna più**.
- **Chi entra dopo non riceve lo stato dei consumati**: `_pendingDeaths` è scritto **solo** da
  `ReceiveEnemyDeathGlobal` (pacchetto **live**); `MarkSpawnPointConsumed` e `DumpConsumedSpawnPoints`
  sono **locali**. Alla connessione viaggia **solo il progress**.
- La via "morte come conseguenza della vita" è **gated `!IsHost`** (`:1965`).
- **`ApplyPersistentDeath` non esiste** (0 occorrenze).

**✅ E il ricevente È GIÀ SCRITTO.** `ReceiveEnemyDeathGlobal` fa già **tutto**:
`_pendingDeaths.Add` + `MarkSpawnPointConsumed` + `DestroyDeadEnemy` sulle copie vive. E `RegisterEnemy`
ripete mark+destroy se il nemico nasce **dopo** l'annuncio ⇒ **la race joiner/annuncio è già coperta**.
**Manca solo chi lo chiama al join.**

**La strada giusta è la POSIZIONE, non l'id** **[verificato in IL]** — `EnemySpawner`:
```
private readonly List<Vector3> consumedSpawns                    (InitOnly)
AddConsumedSpawner(sp)  → consumedSpawns.Contains(sp.transform.position) ? return : Add
IsSpawnerConsumed(sp)   → consumedSpawns.Contains(sp.transform.position)
RespawnDeadEnemies()    → consumedSpawns.Clear() ; CreateEnemy() su tutti
EnemySpawnPoint::CreateEnemy @IL_0018 → callvirt IsSpawnerConsumed(EnemySpawnPoint)
```
`List<Vector3>.Contains` = **uguaglianza esatta per componente** ⇒ **bastano le POSIZIONI.**

**✅ E IL SERVER NON VA TOCCATO.** `Server.receiveEnemyDeathGlobal` (`Server.cs:784-796`) **inoltra già
a TUTTI i giocatori** senza filtro di scena. **Nessuna modifica al protocollo, nessun `NetworkType`
nuovo** (aggiungerne uno obbligherebbe a ricompilare e ricopiare **anche server e `Common.dll`**, §7d).

**Fix proposto — A1, sul canale Progress che ESISTE GIÀ:**

| File | Metodo / simbolo | Cosa |
|---|---|---|
| `EnemySyncManager.cs` | nuovo `ConsumedPositions()` accanto a `DumpConsumedSpawnPoints()` | legge `EnemySpawner.consumedSpawns` (privato → `AccessTools.FieldRefAccess`) e restituisce le posizioni **arrotondate a 2 decimali** |
| `EnemySyncManager.cs` | `ReceiveEnemyDeathGlobal` — **riusare, non riscrivere** | è già tutto il lato ricevente |
| `EnemySyncManager.cs` | nuovo `AnnounceDeathStateToPeers()` | manda lo stato per ogni posizione consumata |
| `NetworkManager.cs` | `ReceiveConnection`, ramo `connected == true` (`:335`) | quando un giocatore **entra**, chiama `AnnounceDeathStateToPeers()` |
| `ProgressManager.cs` | `ReceiveProgress` | instrada gli id con prefisso `ENEMYDEAD\|` a `EnemySyncManager`, **prima** della queue (il joiner è in `MainMenu`: la queue si applicherebbe troppo tardi o mai) |
| `StatHelper.cs` | `ApplyProgress` / `GetProgressNotification` | **guardia sul prefisso**, altrimenti `Error: Unknown stat` + **notifica spam** |

**Formato dell'id**, sul canale `ProgressType.PlayerStat` che esiste già:
```
ENEMYDEAD|<scene>|<x:F2>|<y:F2>
```

**⚠️ Due trappole da evitare**: (a) **non** chiamare `SendProgress` dal **thread di rete** (mutazione
concorrente della `sendingQueue`) — dispatchare al thread principale; (b) la **guardia sul prefisso** è
**obbligatoria**.

*Alternativa A2* — nuova `NetworkType.EnemyDeathState`: concettualmente più pulita, **ma obbliga a
ricompilare e ridistribuire `Common.dll` e il server** (§7d). Da preferire solo per un pacchetto batch.
*Alternativa A3* — "riapplica al load del joiner": **non funziona**, il joiner non ha **nessuna**
informazione e nessuno gliele manda.

**DESIGN — unione o vince l'host?** Con A1 **vince l'UNIONE, e non è una scelta: è imposto
dall'architettura.** Il server **è già** l'unione per team (`TeamInfo.progressSets`) e ogni macchina ha
già spedito il proprio stato consumato. Non esiste una "storia dell'host" da cui escludere quella del peer.

- **Unione (A1)**: il joiner impara i consumati dell'altro → i nemici uccisi dall'altro vengono marcati
  e distrutti anche nella sua copia. Le due stanze diventano **identiche**, al costo di **morti che il
  giocatore non ha causato ma accetta come fatto di mondo** — **esattamente come già accade per gate,
  leve e porte**.
- **"Vince l'host"**: l'host imporrebbe la propria lista e **cancellerebbe** le uccisioni del peer ⇒ il
  nemico che G1 aveva ucciso **ricompare vivo davanti a G1**: **si sostituisce il difetto attuale con
  uno peggiore.**
- ⚠️ **Interazione con §3**: la lista consumata è **la stessa** che il falò azzera, e il reset è
  **locale**. L'unione è quindi **auto-limitante** (si riallinea al primo falò), ma fino a lì le due
  liste restano diverse: **è §3, non un difetto nuovo.**

> ### ✅ RISOLTO E VERIFICATO IN GIOCO (tag `stato-join-fix-3`)
> I marcatori, dai log della sessione di conferma:
> ```
> G2: [EnemyJoin] annunciate 2 morti precedenti al nuovo giocatore
> G1: [EnemySync] Nemici rinati: dimenticate 2 morti annunciate in precedenza
> G1: Received new game progress: ENEMYDEAD    → 0 occorrenze (il percorso vecchio è morto)
> G2: [Probe] ... animRestart=0 animRepeat=0 stalls100=0 fps=60
> ```
> **Il fix del join e quello del falò sono confermati.** L'utente: *"tutto funziona benissimo, il problema è stato risolto"*.
>
> **E il contesto al falò è esatto**: `[TRACE][SPAWN] Respawning dead enemies on level.` immediatamente
> seguito da `[EnemySync] Nemici rinati: dimenticate 2 morti annunciate in precedenza`. **L'aggancio
> scatta nell'istante giusto.**
>
> **Nota**: la pulizia dei flag spazzatura ha prodotto **0 righe**, quindi **il salvataggio era già
> pulito** — la pulizia resta come rete di sicurezza, non è servita.

**Gravità: 🔴 ALZATA DALL'UTENTE.** Difetto **GARANTITO** (dipende solo da *chi ha ucciso cosa prima
della connessione*), **frequente** all'ingresso in una stanza già giocata, **auto-limitante**,
**non bloccante**. **Costo ~60-80 righe, tutte nel client. Rischio: basso.**

**Come verificarlo**: G2 uccide i due nemici in `D01Z01S01` **da solo**, *poi* G1 si connette.
**Atteso**: su G1 `Post-spawn: N enemies / 7 spawn points` con **N ridotto di 2**, e il nemico **non
compare mai vivo** dove G2 l'ha ucciso. **Controprova anti-regressione**: G1 cammina fino ai punti —
nessun nemico; **poi entrambi al falò** → i nemici **devono tornare su entrambe**. **Test negativo
indispensabile**: uccidere un nemico **prima** che l'altro entri nella stanza ma **dopo** la
connessione (funziona già oggi via `EnemyDeathGlobal`) e **confrontare: nessuna differenza**.

### 1h. ⚠️ LA STANZA DI TEST NON È PULITA — e il sospetto "nemici fermi" va SCARTATO

`D01Z01S01` contiene un `Tools.Level.Interactables.Execution` (con campo pubblico `TutorialID`) che in
`OnStart` fa `Invoke("ShowTutorial", 0.6f)`. La catena, **verificata in IL**:

```
Execution::ShowTutorial  →  Penitent::ShowTutorial  →  TutorialManager::ShowTutorial
    Input.SetBlocker("TUTORIAL", true)
    LogicManager.PauseGame()                    ← METTE IN PAUSA IL GIOCO
    istanzia Tutorial.prefab sotto UIController.GetTutorialRoot()   ← è UI
    TutorialWidget.ShowInGame()  →  ResumeGame()
```

**Conseguenza misurata**: nel log single-player, **al blocker `TUTORIAL` il frame è
`frameMs_max=53.6 ms` contro 18-28 ms dei frame adiacenti**. Non è colpa della mod e non è uno stallo
di §1f: **è il gioco che si mette in pausa.**

**Da fare**: i test di §1a/§1e/§1f **non vanno più fatti in questa stanza**, altrimenti ogni misura di
frame time contiene almeno un picco da tutorial. **Marker nei log**:
`[TRACE][INPUT] The input blocker TUTORIAL has been enabled.`

> ### **Il sospetto "i nemici fermi non si sincronizzano" VA SCARTATO.**
> Le entità che restavano ferme in `D01Z01S01` **non eseguono codice** (§7i): `pos_distinte=1` è
> **garantito per costruzione** e **non misura nulla**. Il capitolo va riaperto **solo** su nemici che
> **animano e traslano**.

**Conseguenza sul metodo**: **non trarre conclusioni per-nemico confrontando run diversi agganciandosi
a `SPAWN (NN)`** — le etichette **si permutano**. **Agganciarsi alla POSIZIONE.** Alcuni confronti
fatti in precedenza con quel metodo **non sono affidabili** e vanno rifatti se servono.

#### Requisiti di una stanza "pulita" per i test — checklist VERIFICATA sui log

| # | Criterio | Come si legge |
|---|---|---|
| 1 | **Conteggio RISOLTO**: `[EnemySync] Post-spawn: N enemies / M spawn points` **dopo** `OnLevelLoaded`, con **`N = M > 0`** | ⚠️ **È QUESTO che rende significativa l'assenza di `[EnemyInert]`** — non l'assenza in sé. `N ≠ M` ⇒ assenza **inconclusiva**; `0/0` ⇒ assenza **priva di valore** (la stanza è **vuota**, non "pulita") |
| 2 | **Nessun blocker di famiglia tutorial** | nessuna riga `input blocker <color=green>TUTORIAL</color>`, né `tutorial_block`, né `POP_UP` nella finestra della stanza |
| 3 | **Nessuna cutscene** | nessuna riga `[TRACE][CUTSCENE]`, `CINEMATIC`, `pontiff_block` |
| 4 | **Frame sani** | tutte le `[Probe]` con `stalls100=0` e `frameMs_max < ~60 ms`. ⚠️ valori **>300 ms** sono **caricamento**, non sync |
| 5 | **Almeno 2 nemici che si muovono** | ≥2 label `[EnemyTick]` con **più di una** `posNemico` distinta **e** `anim != 0` |
| 6 | ⚠️ **`anim=0` va SEMPRE letto con `life`** | `anim=0` **con `life=0`** = **cadavere** ✅ ammesso. `anim=0` **con `life>0`** = **entità mai attivata** → scarta la stanza. **Nei dati `anim=0` significa CADAVERE, non "oggetto mai attivo": l'ipotesi iniziale era sbagliata** |
| 7 | **`connected=True`** nelle probe | **senza questo il test non verifica alcuna sincronizzazione** |

#### Le quattro stanze provate in single-player — classifica verificata

| # | Stanza | Verdetto |
|---|---|---|
| **1** | **`D01Z01S01`** ⭐ **consigliata** | **zero blocker tutorial, nessuna cutscene, `stalls100=0`**, picco 41-43 ms, **5/5 nemici mobili** (attività **23.9%**, la più alta) e **2 morti** utili per la sync delle uccisioni. ⚠️ Caveat: **5 nemici / 7 spawn point** ⇒ qui l'assenza di `[EnemyInert]` **non è dimostrabile** |
| **2** | **`D01Z01S02`** | **9/9 nemici mobili** e **conteggio RISOLTO 9/9** → **l'unica, con S03, dove "nessuna entità inerte" è DIMOSTRABILE**. Nessun `TUTORIAL`. ⚠️ Un `POP_UP` da **ritiro del collezionabile** congela il mondo per ~110 righe: **evitabile non raccogliendolo** |
| 3 | `D01Z01S03` | 23 nemici, ma **`TUTORIAL` intrinseco** + **cutscene `PontiffEvent1`** di ~250 righe col mondo congelato + **10 entità mai mobili**: **peggior rapporto segnale/rumore** |
| 4 | `D01Z01S07` | **0 nemici / 0 spawn point**: **inutilizzabile**. E contiene le **uniche 2 stall** della sessione (`12607 ms`!) |
| — | `D01Z02S01` | **0 nemici + `TUTORIAL`** → l'esclusione dell'utente è **confermata dai dati** ✅ |

**Nessun boss in nessuna delle quattro** (l'unica stringa "boss" è il caricamento di `BossRushData.config`).

**Dettaglio importante**: **in tutte le stanze i nemici restano dormienti finché il player non si avvicina**. In S01 la prima visita ha **0%** di attività (player fermo a x≈−726.8), la seconda **23.9%** (il movimento parte solo quando il player avanza verso x≈−670). **Bisogna avvicinarsi, altrimenti si misurano nemici fermi PER PROGETTO.**

#### ⚠️ Terzo caso scoperto: nemici veri DORMIENTI

In `D01Z01S03` ci sono **10 entità ferme** che **non** hanno `anim=0` e **non** hanno `Idle`:

```
anim=1809480452   life=10   una sola posizione   y = −3 e −1   (sotto il piano di gioco)
```

**Quello stesso `anim` è usato anche dai membri MOBILI della stessa famiglia** (che ciclano fra `−1427642350` / `1809480452` / `801321347`). Quindi **sono nemici veri, con behaviour attiva, dormienti e mai spostati** — **non** oggetti mai attivati.

> **La dicotomia `anim=0` vs `Idle` NON copre i dati**: esiste un **terzo caso**.
> **Regola corretta**: `anim=0` + `life=0` = **cadavere**; `anim=0` + `life>0` = **mai attivato**; `anim≠0` + fermo = **dormiente**.

---

## 2. BOSS — meccanismo chiarito (era frainteso)

**L'esclusione `IsBoss` agisce solo su `enemy.SetHealth(currentLife)` e su `KillEnemyLocally` dentro
`ReceiveEnemyUpdate` (`:1853-1879`)** — cioè impedisce che uno **snapshot di vita** uccida il boss.
**NON blocca il danno**: quello entra da `ApplyDamage` → `((IDamageable)enemy).Damage(hit)`.

**La barra NON è sincronizzata: è in polling sulla vita LOCALE.** Verificato in IL:
`Gameplay.UI.Others.UIGameLogic.BossHealth` ha `private Entity target`;
`get_BarTarget()` → `target.Stats.Life.Current / target.Stats.Life.Final`;
`CalculateHealthBar()` → `fillAmount = Mathf.Lerp(fillAmount, BarTarget, _damageTimeElapsed * ConsumptionSpeeed)`
**ogni frame**; `SetTarget(GameObject)` → `target = go.GetComponent<Entity>()`.
Mostrata da `BossFightManager.EnableLifeBar(bool)` → `UIController.ShowBossHealth(Entity)` e da
`BossScripting.OnTriggerEnter2D`; nascosta da `BossScripting.OnDead()`.

> **Conclusione**: i due boss perdono vita per lo **stesso danno replicato**, quindi le due barre
> **coincidono per coincidenza, non per sincronizzazione**.
> **Conseguenza**: se un danno al boss arrivasse per una via che **non** passa da
> `EnemyDamageArea.TakeDamage` (trappola, arena), la barra **divergerebbe**. L'esclusione è **giusta
> per ora, ma fragile**: va ricordato.

**Confermato nei log** (`D17Z01S11@ElderBrother`): vita **400 → … → 0** con gli **stessi valori** su
G1 e G2, transizioni di morte parallele, `[EnemyKill] … MORTO Dead=True vita=0.0` su **entrambe**, e la
flag **`D17Z01_BOSSDEAD`** che viaggia come **progress** (il server registra 4 duplicati da G2).

### 2b. Il "boss che impazzisce con i target" (🟡)

Il target è `Enemy.Target`; `EnemyBehaviour.GetTarget()` lo restituisce e **se è null lo imposta al
Penitent locale** — giusto così: il boss di ogni macchina insegue il Penitent **di quella macchina**.
L'isteresi **esiste già nel gioco ed è rotta**: `targetTime` vale `0f` (vedi §1b) → inversione ogni
frame.

**Fix proposto**: assegnare un `targetTime` minimo (es. **0.85 s**) → isteresi temporale "come Dark
Souls", **senza toccare la scelta del bersaglio**. ⚠️ **Da verificare in IL se il campo è davvero
`readonly`** prima di scrivere il patch (se lo è, serve `AccessTools`/reflection o un prefix che
riscriva `DeltaTargetTime`).

---

## 3. FALÒ E RESPAWN — causa accertata

`EnemySpawner.RespawnDeadEnemies()` ha **3 chiamanti in tutta l'assembly** (verificato in IL):

1. `Tools.Level.Interactables.PrieDieu/<ActivationLogic>c__Iterator1::MoveNext` @ `IL_0188` — falò, prima accensione
2. `PrieDieu/<ReActivationLogic>c__Iterator2::MoveNext` @ `IL_013E` — falò, già acceso
3. `Framework.Penitences.PenitencePE02::OnLevelLoaded` — una penitenza

**Non è il salvataggio a triggerarlo.** L'ordine dentro `ActivationLogic`:
`ShallowActivationLogic()` → `LaunchEvent(PRIEDIEU_ACTIVATED)` → `LoadAttrackScene()` → **`RespawnDeadEnemies()`**.
E `ShallowActivationLogic()` (IL) è: `SpawnManager.set_ActivePrieDieu` →
`Penitent.Stats.Life.SetToCurrentMax()` → `Penitent.Stats.Flask.SetToCurrentMax()` →
`AlmsManager.GetPrieDieuLevel()` → `Penitent.Stats.Fervour.SetToCurrentMax()` →
**`PersistentManager.SaveGame(bool)`**.

> **Il `SAVEGAME` che compare nel log nasce DENTRO il refill.** Il save è una **conseguenza** del
> sedersi, non la causa del respawn.
>
> **Refill e respawn sono la stessa azione, ma solo per chi si siede.** La preoccupazione "chi non si
> siede non deve avere il refill" **è già soddisfatta per costruzione**. Fa eccezione
> `PenitencePE02` (respawn a ogni `OnLevelLoaded`, senza refill).

**Lato mod oggi**: `PrieDieuHelper` rende condiviso **solo** l'oggetto persistente — sul ricevente
`GetCurrentPersistentState` fa `PrieDieu.Ligthed = true` e `return false` (il sorgente commenta
`// Maybe play activation animation`). Il falò si **accende** su entrambi, ma **respawn, refill e save
restano locali**. **Nei log**: il messaggio compare **3 volte su G1 e 5 su G2** — conteggi diversi,
prova ulteriore che è locale.

### 3a. Soluzione raccomandata (A1) 🟢 rischio basso

`Postfix` su `PrieDieu.OnUse` → nuovo pacchetto `PrieDieuRested(id)`. Sul ricevente:
- **stessa scena** → `Core.Logic.EnemySpawner.RespawnDeadEnemies()` **diretto**, **senza** toccare
  vita/fiasche/fervour (quel giocatore farà il refill quando si siederà lui);
- **altra scena** → tenere un set di "falò riposati" da applicare a `OnLevelLoaded`.

*Alternativa A2*: `Postfix` su `EnemySpawner.RespawnDeadEnemies` + pacchetto "respawn" — copre anche
`PenitencePE02`, ma **spara anche durante i caricamenti** (rischio medio).
*Alternativa A3* (l'idea di far ricaricare la stanza): tecnicamente possibile ma **sconsigliata**.
`LevelManager/<LoadAndActivateLevel>::MoveNext` usa `SetBlocker("BLOCK_UNTIL_FPS_STABLE", true)` e
attende su `Time.smoothDeltaTime` (**19 coppie enable/disable per macchina nei log**) → **congela
l'input di entrambi a ogni falò**.

### 3b. Nota correlata

`MarkSpawnPointConsumed` esce subito se l'id non inizia per `sp|`. Quindi per un nemico registrato
come `en|` (boss, incontri a trigger) la morte **non viene marcata** e il nemico **ritorna al reload**.

---

## 4. OGGETTI DISTRUTTIBILI (leva da spaccare) — 🟡

**Il tipo è stato identificato.** `Lever` **NON implementa `IDamageable`** (verificato in IL:
`Tools.Level.Interactables.Lever : Tools.Level.Interactable`, nessun metodo `Damage`). La "leva da
spaccare" è quindi uno di questi, tutti `IDamageable` con `Damage(Hit)` e tutti `PersistentObject`:

| Tipo | Patch della mod |
|---|---|
| `PersistentBreakableObject` | ❌ **nessuna** |
| `SlashReceiver` | ❌ **nessuna** |
| `BreakableObject` | ❌ **nessuna** |
| `TriggerBasedTrap` | ❌ **nessuna** |
| `BreakableWall` | ✅ `BreakableWallHelper.cs` (Postfix su `Damage`) |
| `Lever` | ✅ `LeverHelper.cs` |
| `Gate` | ✅ `GateHelper.cs` |

Il **ricevente è già generico** (`PersistentObjectHelper.ApplyProgress` cerca l'oggetto per GUID e
chiama `GetCurrentPersistentState("use", false)`) → **manca solo il mittente**, ed è per questo che il
sintomo è asimmetrico (*"colpisco io, sull'altra sessione non si rompe"*).

**Soluzione (🟢 rischio basso, meccanica ripetitiva)**: 4 file gemelli di `BreakableWallHelper.cs`, con
Postfix su `Damage` + prefix su `GetCurrentPersistentState`/`SetCurrentPersistentState`.
⚠️ `Damage(Hit)` **può essere chiamato più volte**: `UsePersistentObject` va reso **idempotente** o
filtrato (solo quando `Destroyed` diventa true).
⚠️ `ImpacteableObject` **non** è un `PersistentObject` (è `MonoBehaviour`) → va escluso.

**Prima va identificato in gioco**: nei log di questo test `Broke wall: 0`, `Used Lever: 0` —
**l'oggetto non è mai stato toccato**.

---

## 5. IDEE A BASSA PRIORITÀ

### 5a. Modalità spettatore alla morte di un giocatore 🟢

Quando g1 muore: **non respawna subito**, la sua camera si **blocca su g2** e **lo segue nei cambi
stanza**; quando **g2 si siede al falò** parte l'**animazione dei cherubini** e **g1 torna in vita**.

**Cosa esiste (verificato in IL):**
- `Penitent::Respawn()` — **public**; `Logic.get_EnemySpawner().Reset()` → `Persistence.RestoreStored()`
  → `SpawnManager.Respawn()`. **Unico chiamante**: `DeadScreenWidget/<OnDeadAction>c__Iterator0`.
- `Penitent::CherubRespawn()` — **public**; `PoolManager.ReuseObject(<cherubPrefab>, ...)` +
  `UIController.UpdateGuiltLevel(bool)`. **Unico chiamante**: `SpawnManager.SpawnPlayerOnLevelLoad(bool)`.
- Tipi utili: `CherubRespawn : PoolObject`,
  `CherubRespawnAnimationBehaviour : StateMachineBehaviour`, `CherubCaptorPersistentObject`.
- `CameraManager`/`CameraPlayerOffset.SetCameraTarget` già esiste (IL: `ldstr "Penitent"`).

**Attenzione**: `Penitent.Respawn()` **non è "tornare in piedi"** — è **"ricaricare dal save"**: va
verificato che non riavvii la scena. **Rischio medio**: tocca il ciclo di morte del giocatore, che oggi
non è toccato affatto.

### 5b. `HighWillsRespawnBehaviour` è già patchato dalla mod — ⚠️ dipendenza

`AnimationFixes.cs:732` e `:738` lo patchano, entrambi con `animator.name == "Body"` → **è soggetto
alla stessa incognita di §1d**.

---

## 6. VERIFICATO E NON È UN BUG

| Cosa | Esito |
|---|---|
| **Dialoghi / cutscene che sembravano bloccarsi** | ❌ non è un bug: serviva **SPAZIO**, non **ESC**. Confermato dall'utente |
| **Fantasmi dei nemici** | ✅ risolti: l'animazione di morte parte. Quando "spariscono" è il taglio di §1a |
| **Boss fight** | ✅ funziona (meccanismo in §2) |
| **Gate** | ✅ `Gate opened` = 1+1, allineato |

---

## 7. NOTE TECNICHE DA RICORDARE

| # | Cosa |
|---|---|
| 7a | **`Entity.Dead` è CALCOLATO** (`!(Life.Current > 0)`), **`EntityStatus.Dead` è l'interruttore vero** che `KillInstanteneously()` accende. È la chiave di tutto il capitolo morte: una guardia su `Entity.Dead` si "disarma" da sola appena la vita tocca zero 🔴 **AUDIT 2026-10-12: QUESTA NOTA E' SUPERATA** — il blocco `if (!localIsHost) return false;` **non e' piu' "proposto": e' stato RIMOSSO** *(voce 43, `c55d80e` — "scheletro rimosso")*. **⇒ Questa nota dava per aperto un lavoro chiuso: la diagnosi di §1b che la riguardava e' confermata, la PROPOSTA no.** |
| 7b | **Non esiste una classe `Tutorial` nemica**: *"Tutorial"* è solo la stringa `EnemySpawnPoint.EntityName` (o `gameObject.name`, riscritto da `SetEntityName` come `EntityName + GetHashCode()`). Verificato in IL: gli unici tipi col nome sono `Framework.FrameworkCore.Tutorial` (**ScriptableObject**), `TutorialManager`, `TutorialWidget`, `TutorialsCommand`, `TutorialsEnabled` (PlayMaker), `TutorialIdAttribute` — **nessuno deriva da `Enemy`**. Il popup del tutorial è **UI**: `TutorialManager.ShowTutorial` mette il blocker `TUTORIAL` + `PauseGame()`, istanzia `Tutorial.prefab` sotto `UIController.GetTutorialRoot()` e alla fine disattiva la **UI root** — **nessun `Enemy` coinvolto** |
| 7c | **`pos_distinte=1` ha DUE cause diverse, da separare**: (a) `anim=Idle` e fermo → coerente con "nessuna IA di pattugliamento"; (b) **`anim=0`** e fermo → l'oggetto **non ha mai eseguito codice** (§7i), e la posizione unica **non dice nulla**. Nella stanza di test l'unico id che si sposta davvero è quello **senza spawn point** (`D01Z01S01@SPAWN`, 15-16 posizioni distinte). **Regola: prima di dire "non ha IA di movimento", guardare `anim`** |
| 7d | **Il protocollo di rete è 11**: mod, server **e `Common.dll`** vanno copiati insieme |
| 7e | **Build = deploy**: il `.csproj` copia sempre in `D:\SteamLibrary\...\Modding\`, quindi anche buildare da una copia di prova sovrascrive la mod installata |
| 7f | **I log non hanno timestamp** (una sola riga datata): niente sequenze temporali fra le macchine e niente frame rate. La durata si ricava dal log del server (in questo test: **1740 s** + 55 s) |
| 7g | **Nessun indicatore di prestazione esiste nei log**: `PingManager` calcola la latenza ma **non la logga mai**. Il passo 0 lo risolve |
| 7h | `RespawnDeadEnemies` non è chiamato dal salvataggio, ma dal **falò** (3 chiamanti in tutto l'assembly) |
| 7i | **Le entità inerti della stanza di test.** In `D01Z01S01` due `Enemy` a **(−697.1, 12.8)** e **(−689.1, 11.8)** risultano `anim=0`, vita 60, `pos_distinte=1` in **tutte** le run. Nel dataset a 2 giocatori erano etichettate `SPAWN (11)`/`(13)`; nel log single-player dell'08/10 le stesse **posizioni** erano etichettate `SPAWN (15)`/`(16)` → **le etichette `SPAWN (NN)` SI PERMUTANO fra run: agganciarsi alla POSIZIONE, non al nome.** Causa in IL: `EnemySpawnPoint.CreateEnemy` fa `SetActive(false)` quando `SpawnEnabledEnemy=false`, e nient'altro le riattiva (`SetEnemyActive(true)` è raggiungibile solo da `SpawnEnemyOnArena`, chiamato solo da `PontiffHuskiesCombatTrigger.CombatSequence` e `Tools.Playmaker.Action.EnableStandByEnemies`). Oggetto mai attivo ⇒ `Awake` mai eseguito ⇒ `EnemyBehaviour` nullo, `Entity.Animator` mai assegnato, nessuna entry in `Entity.livingEntities`. **Non sono nemici da sincronizzare: sono oggetti di scena.** Dal commit `b026f2e` la mod li **esclude** (`IsInertScenery`: inattivo **e** senza `EnemyBehaviour`) e li conta in `[EnemyInert]` |
| 7j | **Chi assegna `EnemyBehaviour`**: solo `Enemy::set_EnemyBehaviour`, chiamato da **17 classi su 78** derivate da `Enemy` (Acolyte, AxeRoller, BellCarrier, Bishop, ExplodingEnemy, Flagellant, GhostKnight, HeadThrower, HomingTurret, Jumper, Menina, Roller, WallEnemy, BejeweledSaintHead, HomingBonfire, PietyMonster, TresAngustiasMaster). `EnemySpawnPoint.CreateEnemy` **non lo assegna mai** (scrive solo `EnableBehaviourOnLoad`). Quindi `behaviour=null` **non è di per sé un'anomalia**: `DummyEnemy`, `ContactAreaDummyEnemy`, `Tools.Gameplay.CombatDummy` e `Ghost` non lo assegnano **per progetto** |

---

## 8. PIANO D'AZIONE — 10 passi

Regola del progetto: **un intervento per volta**, test in gioco in mezzo, **rollback immediato** al tag
precedente se peggiora.

| # | Intervento | Perché in questo ordine | Stato |
|---|---|---|---|
| **0** | **Strumentazione**: `Animator.name` del Penitente, FPS, `Stopwatch` su `Update` e `OnLateUpdate`, GC. **Nessun cambio di comportamento** | Decide fra **§1d** e **§1e** *prima* di toccare il codice | ✅ **TESTATA e conclusiva**: nome=`Body` (→ §1d chiusa), FPS 43-61 con picchi 124-512 ms, costo mod **0.02-0.19 ms** medi, `gc0≈0` → `stato-passo0-strumentazione` |
| ~~1~~ | ~~Test di controllo single-player~~ | ⬇️ **DECLASSATO**: il probe ha già dato la baseline **disconnessa**, e da lì sappiamo che lo stallo esiste disconnesso e i tagli no ⇒ **il test di controllo non discrimina più nulla** | ❌ **cade** |
| **2** | **B-lite**: togliere `animTime = 0f;` (**`:1754`**) | ✅ **VERIFICATO IN GIOCO**. `animRepeat` è passato da **33-58 per nemico a ZERO** su entrambe le macchine, `animRestart=0`, `stalls100=0`, `fps≈60`, `syncMs` invariato (0.02-0.18 ms): **la cura non è costata nulla altrove**. L'utente riferisce animazioni **fluide** | ✅ **CHIUSO** — `stato-passo2-animtime` |
| **3** | **Desync al join (§1g)** — pacchetto "stato morte" sul canale Progress + **ri-annuncio a ogni join** | ⬆️ **PROMOSSO da richiesta esplicita dell'utente.** Indipendente da §1, **riusa `ReceiveEnemyDeathGlobal` che è GIÀ scritto e collaudato**, **nessuna modifica al server**, nessun bump di protocollo. È il difetto **funzionale** più visibile rimasto | ⏳ **PROSSIMO** 🔴 **AUDIT 2026-10-12: QUESTA RIGA E' LA PIU' SBAGLIATA DELLA TABELLA.** Il testo dice **"⏳ PROSSIMO"** ma **il lavoro e' stato consegnato E VERIFICATO IN GIOCO** *(tag `stato-join-fix-3`; marcatori `[EnemyJoin] annunciate 2 morti`, `ENEMYDEAD → 0 occorrenze`; commit `b5208d6`, `38bd79e`, `55e711b`)*. **⇒ CHIUSO.** *E la stessa riga e' citata in testa al file come "cio' che resta": due punti del file dicevano il falso nella stessa direzione.* |
| **4** | **Contatore `orientFlip` per nemico** | **Necessario per rendere MISURABILE il fix dell'orientamento (§1b)**, che altrimenti si giudica a occhio. Poche righe nel probe | ⏳ **insieme al 3** ✅ **AUDIT 2026-10-12: FATTO** *(`PerformanceProbe.cs:78-79`, `:281-282`)* — e **allargato**: oltre a `orientFlip`/`orientSkip` oggi conta **`gameOrient`**, cioe' **le scritture del GIOCO** *(60 Hz)*, che erano **proprio la cosa che mancava** *(vedi il riquadro "LO STRUMENTO orientFlip MISURA LA COSA SBAGLIATA", §1b: il contatore vecchio misurava solo le scritture della MOD, 20 Hz)*. |
| **4b** | **Diagnostica dietro flag** + `RESCAN_INTERVAL` a 2-3 s | ↕️ giustificato da §1e(c) (gli **84.5 ms** e l'allocazione ~0.4 MB/s), **NON dagli FPS** | ⏳ ⚠️ **AUDIT 2026-10-12: NON e' stato fatto COSI'** — il "dietro flag" non esiste, e **va lasciato scritto** invece di dichiararlo chiuso. **Ma il problema e' stato risolto per un'altra via, e meglio**: `[EnemyTree]`/`[EnemyTick]` sono passati a **`LogCadence` separata da `RESCAN_INTERVAL`** *(`EnemySyncManager.cs:51-53`: *"deliberately separate … it used to share by accident"*; commit `215c4f1`, voce 47)*. **⇒ Il sintomo che il flag doveva togliere (la riga dei nemici che occupava meta' del log) e' RISOLTO; il meccanismo descritto no.** |
| **4c** | **`fullPathHash`** + riposizionare davvero (vecchio passo 3) | ⬇️ **DACCATO SOTTO LO SPETTATORE**: le animazioni sono **accettabili** (decisione dell'utente). Da riprendere **solo se** la fluidità tornasse a dare fastidio | ⏳ **parcheggiato** ⏳ **AUDIT 2026-10-12: PARCHEGGIATO DI PROPOSITO** — **non e' una svista**: la decisione e' dell'utente *("animazioni accettabili, priorita' piu' bassa dello spettatore", `7c638e5`)* e regge. **Resta come irrobustimento**, da riprendere **solo se** la fluidita' tornasse a dare fastidio. |
| ~~5~~ | ~~Solo se `Animator.name != "Body"`~~ | ❌ **CADE**: il nome è `Body` su entrambe le macchine, 4+4 volte | ❌ **cade** |
| **6** | **Falò A1**: pacchetto `PrieDieuRested` → `RespawnDeadEnemies()` sul ricevente, **senza** refill | Indipendente da §1, rischio basso | ⏳ ✅ **AUDIT 2026-10-12: CHIUSO E VERIFICATO IN GIOCO** — *(`6154863`, tag `checkpoint-stabile-falo`/`checkpoint-stabile-falò`)*: il falo' **si propaga** e i **benefici NO**. |
| **7** | **Oggetti distruttibili**: 4 helper (`PersistentBreakableObject`, `SlashReceiver`, `BreakableObject`, `TriggerBasedTrap`) | Meccanico e isolato, ma **dopo** aver identificato l'oggetto in gioco (`Broke wall: 0`, `Used Lever: 0` anche nell'ultimo test) | ⏳ ✅ **AUDIT 2026-10-12: CHIUSO** — *(`cfe7d6b`, `edabc5e`, `75cf234`, `a44a9e5`; voce 6 chiusa)*. |
| **8** | **Prestazioni**: `ProcessQueue` senza allocazioni + `IdOf` con dizionario inverso | ⬇️ **DECLASSATO**: **allocazione sì, stallo no**. Da fare **dopo** 2-3-4 | ⏳ ✅ **AUDIT 2026-10-12: CHIUSO — voce 10, `c55d80e`** *(e la 7, `RegisterEnemies`, con essa)*. |
| **9** | **Orientamento (§1b, RISCRITTO)**: **Prefix con isteresi** su `Entity::SetOrientation` + stessa isteresi in `ApplyOrientation` | ⬆️ **HA ORA UN MECCANISMO VERIFICATO IN IL** (`Ghost::OnUpdate`, `TrinityMinionBehaviour`). **Una sola armonica copre TUTTI i writer per-frame**; non tocca il game loop; **gli override dei boss e del Penitente non passano di lì** (verificato in IL) | ⏳ dopo il falò ✅ **AUDIT 2026-10-12: CHIUSO E VERIFICATO** — voce 2, commit `133274c` *(l'isteresi spostata sulla Prefix giusta)* + `9ecf8ae`; **confermato dall'utente** *("35 totalmente scomparsa")*. |
| **10** | **Modalità spettatore** (§5a) | Idea a bassa priorità, tocca il ciclo di morte del giocatore | ⏳ ⏳ **AUDIT 2026-10-12: ANCORA APERTO** *(voce 9)* — ma **non allo stato che la voce descrive**: il **livello di stato e' costruito** *(`PlayerDeathSync.cs`: `pdead\|`/`palive\|`, `IsEveryoneDead`, `CountDeadPlayers`)*, **camera e body no, e nessun referto lo verifica in gioco**. |

**Motivo dell'ordine**: prima **misurare** (0-1), poi i fix a **rischio crescente e verificabilità
decrescente** (2-3), poi la **diagnostica** (4) perché il suo effetto si misura con lo strumento del
passo 0, poi il **Penitente** (5) solo con la prova, poi i **due difetti funzionali** (6, 7), poi le
**prestazioni** (8), e per ultimo ciò che **già funziona** (9, 10).

---

## 9. DOMANDE APERTE

| # | Domanda | Cosa serve | Stato |
|---|---|---|---|
| 1 | `Animator.name` del Penitente — chiude §1d | il log del passo 0 | ✅ **`Body` su entrambe, 4+4 volte** → §1d **chiusa**. **NUOVA DOMANDA**: perché il Penitente risulta tagliato, se la mod non scrive sul suo Animator? **Candidato non verificato: il ghost**, pilotato per nome e sempre da frame 0 |
| 2 | Frame rate reale | FPS dal passo 0 | ✅ **43-61 fps**, picchi **124-512 ms** all'avvio/caricamento e **150-177 ms** alla connessione |
| 3 | Costo per frame della mod | `Stopwatch` dal passo 0 | ✅ **0.02-0.19 ms** medi (`Update`), **2-4 ms** tipici per tutto `OnLateUpdate`, **picco 84.5 ms** in `RegisterEnemies` |
| 4 | Gli `shortNameHash` delle due macchine per lo stesso stato — chiude §1a | log di `shortNameHash` **e** `fullPathHash` per un nemico, 5 s per macchina | ⏳ **da aggiungere se il passo 2 non basta** ⚠️ **AUDIT 2026-10-12: NON SERVE PIU'** — era *"da aggiungere **se il passo 2 non basta**"*: **il passo 2 e' bastato** *(`animRepeat` da 33-58 a **ZERO**, voce 1 chiusa)*, e il residuo e' stato **dichiarato del GIOCO** *(il `TURN` al cambio bersaglio)*. **⇒ SUPERATA dalla chiusura di §1a, non "verificata": la domanda non ha piu' un oggetto.** |
| 5 | Identità dell'oggetto di §4 | il tipo reale in gioco | ⏳ `Broke wall: 0`, `Used Lever: 0` **anche nell'ultimo test** ✅ **AUDIT 2026-10-12: RISOLTA** — l'oggetto e' **`Tools.Level.Actionables.BreakableObject`** *(lanterne, scheletri appesi, simboli attorcigliati — **la scenografia che respawna**, NON i muri)*, con **GUID per-istanza** e non `scena~indice` *(voce 6, chiusa)*. |
| 6 | Se `targetTime` è davvero `readonly` e non assegnato | verifica in IL | ✅ **RISOLTA**: `EnemyBehaviour::targetTime` e `EnemyAI::targetTime` sono **`Private, InitOnly`** e **nessun `stfld` li assegna in tutta l'assembly** ⇒ valgono `0f` ⇒ **`LookAtTarget` è CODICE MORTO su 71/71 sottoclassi**. Il vecchio passo 9 **cade ed è assorbito** nel nuovo
| 7 | Perché `[EnemyTick]` è ~1,8/s e non 2/s | — | ✅ **chiarito**: `LogDiagnosticSnapshot` gira a `RESCAN_INTERVAL = 0.5 s`, **una riga per nemico tracciato** |
| 8 | `Received duplicated or inferior progress` e `Couldn't find connected player:` | test dedicato | ✅ **0 "duplicated"** in questo test; restano **32** `Couldn't find connected player: *` su G2, **tutte al join** (una per voce di progress): rumore di handshake |
| 9 | `BLOCK_UNTIL_FPS_STABLE` **non è** un indicatore di prestazione | non usarlo come metrica | ✅ chiarito: è `if (1/smoothDeltaTime < 50 && timeWaiting < 3) attendi`, cioè la **conseguenza** |
| 10 | **Causa del cluster 69-106 ms su G2** | attribuzione esatta: `RegisterEnemies` è il candidato, ma i log non hanno eventi nella finestra | ⏳ non verificata ✅ **AUDIT 2026-10-12: RISOLTA** — il candidato e' **confermato** ed e' **`RegisterEnemies`**, che e' *"l'unico codice dentro `UpdateCore` capace di un frame da 84.5 ms"* **misurato con `_ids` vuoto** *(`:390`)*; **e la voce e' stata chiusa** *(`c55d80e`, voce 7: "non si manifesta")*. |
| 11 | **Data/ora assoluta dei picchi** | — | ❌ **impossibile**: i log non hanno timestamp |
| 12 | **Animazione del Penitente e del ghost** | **nessuna riga di log le misura** | ⏳ da strumentare, se serve |
| 13 | **Numero di inversioni di orientamento per nemico** | **nessuna riga di log lo misura** | ⏳ da strumentare (**orientFlip**, passo 4) — **indispensabile per PROVARE il fix di §1b** ✅ **AUDIT 2026-10-12: STRUMENTATA E USATA** — `orientFlip`/`orientSkip` **piu' `gameOrient`** *(`PerformanceProbe.cs:78-79`, `:281-282`)*: da **85** a **1** su G1 e **3** su G2. **⇒ Ha PROVATO il fix di §1b.** *⚠️ Ma la nota "`orientFlip` misura la cosa sbagliata" resta vera per il contatore VECCHIO: e' il motivo per cui esiste `gameOrient`.* |
| 14 | **Che EnemyAI giri davvero** (FixedUpdate chiama 
everseOrientation) | 0 sottoclassi: potrebbe essere codice morto **o** un'istanza in un prefab | ⏳ **non verificata** |
| 15 | **Il fantasma è congelato dalla mod?** PauseEnemy ferma il behavior tree NodeCanvas e la fisica, **non** MonoBehaviour.Update ⇒ Ghost::OnUpdate gira anche sul **non-proprietario** | test: loggare l'orientamento mentre l'altra macchina possiede il nemico | ⏳ **non verificata** |

---

## 10. DIARIO DEI PASSI

| Data/ordine | Passo | Esito | Tag |
|---|---|---|---|
| — | Rollback al tag stabile dopo il WIP delle animazioni | le features sono state riapplicate una per una, verificate in IL | `stato-pulito-pre-features` |
| — | Features 1-7 dal ramo WIP | 8 patch, zero conflitti | `stato-features-1-7` |
| — | Token di proprietà + 4 correzioni | flag invertito era **bloccante** | `stato-token` |
| — | `PlaceEnemy` nella fisica | l'ordine era sbagliato: **no-op** | `stato-placeenemy-fix` |
| — | Revisione IL: fallback, pausa persistente, collider | 4 difetti corretti | `stato-revisione-il` |
| — | `ThawPhysics` | saltava il ripristino | `stato-thaw-fix` |
| — | Arbitrato sulle distanze | **movimento e posizione sincronizzati** | `stato-sync-movimento-ok` |
| — | Morte robusta + feedback + boss esclusi | **morte animata per mano dell'altro** | `stato-morte-animata-ok` |
| **1** | **Passo 0: strumentazione** | ✅ **testata e conclusiva**: `Animator.name='Body'` (chiude §1d), FPS 43-61 con picchi 124-512 ms, costo mod 0.02-0.19 ms medi / **84.5 ms** il peggiore, `gc0≈0`. Ha **chiuso 2 ipotesi su 3** e **declassato il passo 1** | **`stato-passo0-strumentazione`** |
| **2** | **Passo 2: `animTime` non più azzerato** + contatori `stalls100`/`animRestart`/`animRepeat` nel probe | ✅ implementato e deployato, **da testare**. Se il taglio era questa riga, `animRepeat` deve **crollare** mentre `stalls100` resta | **`stato-passo2-animtime`** |

---

## 11. COME LAVORIAMO

```
tu          → testi in gioco e riporti (anche i conteggi: quanti nemici aggrati, quanti si sono mossi)
ESPLORATORE → legge log + IL del gioco e produce diagnosi e piano dettagliato
io          → implemento un intervento per volta e verifico nel binario compilato
tu          → testi, e rollback immediato se peggiora
```

- **Prima di ogni modifica**: l'Esploratore legge il **codice del gioco** (IL con Mono.Cecil) e verifica
  che i metodi esistano, siano pubblici e facciano quello che crediamo. **Le tre regressioni più
  costose sono nate da assunzioni non verificate, non da errori di scrittura.**
- **I log vanno all'Esploratore, non a me**: 1,5 MB in un contesto usa-e-getta, che conosce il codice.
- **Verifica nel binario compilato** (leggendo le chiamate dentro la DLL), non solo nel sorgente: è
  così che si è scoperto che `PlaceEnemy` non muoveva nulla.
- **Rollback immediato** al tag precedente se un test peggiora, senza tentare di salvarlo.

---

# 12. ARCHIVIO DELLE AGGIUNTE

> **Da qui in poi il file cresce e non si riscrive.** Ogni entry porta l'**orario di aggiunta**, e le
> voci della LISTA INIZIALE puntano qui per il dettaglio. Le entry **non si modificano**: se una cosa
> cambia, si aggiunge una entry nuova che la supersede.

## [2026-10-08 23:12] Re-inglobate voci andate perse nelle revisioni precedenti

Durante la triangolazione delle versioni passate della todo sono emerse **voci che erano sparite**.
Sono ricostruite qui dalla memoria dell'utente, che è l'unica fonte rimasta per il testo originale.

### 12.1 — Sangue e flash nel feedback del colpo (🟢, metodi identificati)

**Cosa manca**: quando un colpo va a segno, **l'effetto di sangue e lo "spark"** della lama **non sono
sincronizzati** fra le due macchine: chi non è il proprietario del nemico **non vede** (o vede in modo
diverso) il feedback del colpo.

**Metodi già identificati (dalla revisione IL precedente)**:
- PenitentSword::GetSwordSparks — le scintille dell'impatto;
- il **doppio danno** sospetto va verificato con i contatori dmgSent / dmgApplied.

**Perché è 🟢**: è **cosmetico** — vita, morte e posizione **non ne sono toccati**.

**Come verificarlo**: colpire lo stesso nemico da entrambe le macchine e **confrontare i due schermi**
nel momento dell'impatto.

### 12.2 — Camminata troncata (⏸️ parcheggiata)

**Voce distinta dall'orientamento**: riguarda il fatto che la **camminata** possa essere **troncata**
non per un riavvio del clip ma per il **protocollo animazione**, che non distingue **animazioni in loop**
da **animazioni one-shot**.

**Stato**: **parcheggiata insieme alle animazioni** (decisione dell'utente: accettabili per il momento,
priorità **sotto la modalità spettatore**).

**Strada indicata in precedenza**: Animator.GetCurrentAnimatorClipInfo per **distinguere loop da
one-shot**, e passare da shortNameHash a **ullPathHash**.

**NB**: questa voce **era andata persa** ed è stata re-inglobata.

### 12.3 — Le altre voci re-inglobate (dalla lista "altre todo")

| Voce | Gravità | Nota |
|---|---|---|
> **AUDIT 2026-10-12 — §12.3 e' un INVENTARIO PARALLELO, e va detto quale voce copre ogni riga.** Tutte e cinque hanno oggi una **voce numerata** o una **voce di `BUG-NOTI.md`**: *(1)* il gioco disfa la pausa → **voce 11** *(`a60e1df`/`c55d80e`, "CHIUSA — il testo era sbagliato")*; *(2)* la migrazione host → **voce 13** *(chiusa)*; *(3)* il falso `IMPRONTA DIVERSA` → **voce 14** *(chiusa: era un falso allarme)*; *(4)* il protocollo/`Common.dll` → **voce 15** *(chiusa: `verify-deploy.ps1`)* e **voce 36**; *(5)* i nemici fermi → **voce 17** *(chiusa: non e' un bug, **e il sospetto e' stato scartato** da §1h dopo la scoperta delle entità di scena)*. **⇒ Nessuna riga di §12.3 e' rimasta senza una voce: la sezione resta come STORIA, non come lavoro aperto.**

| **Il gioco disfa la pausa sul non-proprietario** — <Enemy>::OnUpdate chiama EnablePhysics(true) e StartBehaviour() su **8 tipi** | 🟡 **reale** | il nemico del non-proprietario può **"scivolare"** o **animarsi da solo** |
| **Migrazione host**: SetHost non azzera lo stato del token, **non riarbitra** | 🟡 **reale** | **già avvenuta in un test** |
| **Il falso IMPRONTA DIVERSA** — controllo eseguito **prima** del popolamento | 🟢 cosmetico | **2 occorrenze**, fuorviante |
| **Il protocollo di rete**: Common.dll va copiato **insieme a mod e server** | 🟢 documentato | **verificare i binari su ENTRAMBE le macchine** |
| **I nemici che restano fermi** (pos_distinte=1) | 🟢 **non è un bug** | nemici **senza IA di movimento** |

---

# 13. STORICO DELLE REVISIONI

> **Perché esiste**: nelle revisioni precedenti la todo è stata **riscritta più volte**, e alcune voci
> sono andate perse. Queste sono le **strutture delle versioni passate**, conservate perché **la storia
> non si perde** e perché ognuna contiene voci che le successive potrebbero aver omesso.

## Revisione A — dopo il test esteso (8 sezioni)

| Sezione | Contenuto |
|---|---|
| 0. Esito del test esteso | la tabella nuova: cosa funziona, cosa no |
| 1. Animazioni 🔴 | 1a camminata troncata · 1b orientamento oscillante · 1c la morte parte e viene skippata → fantasmi CHIUSI · 1d riguarda anche il Penitente |
| 2. Vita dei boss | l'esclusione e le regole per testarli |
| 3. Falò 🔴 | 3a i tre punti da indagare |
| 3b. Aggressività dei boss | il cambio bersaglio continuo, la proposta dell'utente |
| 3c. Da chiarire | perché la boss fight funziona nonostante l'esclusione |
| 4. Irrobustimenti | 5 voci note |
| 5. Feedback del colpo | **sangue e flash** |
| 6. Note non urgenti | — |
| 7. Idee a bassa priorità | 7a spettatore alla morte · 7b oggetti distruttibili |
| 8. Verificato e NON è un bug | dialoghi, fantasmi, boss fight |

## Revisione B — dopo la revisione IL (12 sezioni)

| Sezione | Contenuto |
|---|---|
| Stato del progetto | tabella esito + tutti i 12 tag come punti di ritorno |
| 1. Animazioni | il meccanismo, il difetto nuovo di shortNameHash, il thrashing documentato, l'ipotesi delle 130 patch con la tabella di cosa salterebbero, e le 6 ipotesi di carico |
| 2. Boss | il meccanismo chiarito: barra in polling, coincidenza non sincronizzazione, "giusta ma fragile" |
| 3. Falò | causa accertata: i 3 chiamanti, e il SAVEGAME che nasce dentro il refill |
| 4. Oggetti distruttibili | Lever non è IDamageable → i 4 tipi scoperti, e manca solo il mittente |
| 5. Idee a bassa priorità | spettatore con i metodi esatti e i loro unici chiamanti |
| 6. Verificato e NON è un bug | dialogo, fantasmi, boss |
| 7. Note tecniche | 8 voci, fra cui Entity.Dead calcolato vs EntityStatus.Dead |
| 8. Piano in 10 passi | ognuno con il motivo dell'ordine e lo stato |
| 9. Domande aperte | 9 voci, con cosa serve per chiuderle |
| 10. Diario dei passi | registro cronologico con esito e tag |
| 11. Come lavoriamo | il flusso tu → esploratore → io → tu |

## Revisione C — la lista "cosa resta" (7 voci, con gravità)

| # | Cosa resta | Gravità |
|---|---|---|
| 1 | Il gioco disfa la pausa sul non-proprietario: <Enemy>::OnUpdate chiama EnablePhysics(true) e StartBehaviour() su 8 tipi di nemico | 🟡 reale — il nemico del non-proprietario può "scivolare" o animarsi da solo |
| 2 | I nemici che restano fermi (pos_distinte=1) | 🟢 non è un bug: sono nemici senza IA di movimento |
| 3 | Le animazioni non sono ancora confrontabili — [EnemyAnim] c'è, ma non le abbiamo mai misurate con il movimento funzionante | 🟡 |
| 4 | Il falso IMPRONTA DIVERSA (controllo prima del popolamento) | 🟢 cosmetico, 2 occorrenze, fuorviante |
| 5 | La migrazione host non riarbitra (SetHost non azzera lo stato del token) | 🟡 reale — già avvenuta in un test |
| 6 | Prestazioni: RegisterEnemies ogni 0,5 s alloca 2 array (FindObjectsOfType), LogAnimationChanges gira ogni frame | 🟡 |
| 7 | Il protocollo di rete: Common.dll va copiato insieme a mod e server | 🟢 documentato |

## Revisione D — la lista per rischio

| Voce | Rischio |
|---|---|
| Orientamento | 🟡 richiede Harmony (un Postfix su SetOrientation) o rinunciarci — medio, ma isolato |
| Camminata troncata | 🟡 richiede di ripensare il protocollo animazione (loop vs one-shot) — medio |
| Falò | 🟢 decisione di design, poi piccola — basso |
| Boss | 🟡 test boss per boss — l'unica cosa non prevedibile |
| Flash/sangue | 🟢 metodi già identificati — basso |

## Revisione E — il piano in 10 passi (versione originale, per rischio)

| # | Intervento | Rischio |
|---|---|---|
| 0 | 🔬 STRUMENTAZIONE — nessun cambio di comportamento | 🟢 |
| 1 | 🧪 Test di controllo: single-player disconnesso | — |
| 2 | Togliere nimTime = 0f | 🟢 |
| 3 | Da shortNameHash a ullPathHash | 🟡 |
| 4 | Diagnostica dietro flag + RESCAN_INTERVAL a 2-3 s | 🟢 |
| 5 | Solo se il passo 0 prova il nome sbagliato: sistemare le 130 patch | 🟡 |
| 6 | Falò (pacchetto + RespawnDeadEnemies) | 🟢 |
| 7 | Oggetti distruttibili (4 helper) | 🟢 |
| 8 | ProcessQueue senza allocazioni + IdOf O(N²) | 🟢 |
| 9 | Isteresi sul target dei boss (	argetTime) | 🟡 |
| 10 | Modalità spettatore | 🟡 |

---

> **Nota di metodo, da non perdere** (su richiesta dell'utente, perché è il punto di svolta del progetto):
> **le tre regressioni più costose sono nate da assunzioni non verificate, non da errori di scrittura.**
> Prima di ogni intervento, l'Esploratore verifica nel **codice del gioco** (IL, Mono.Cecil) che i
> metodi esistano, siano pubblici e facciano quello che crediamo — e le affermazioni vanno marcate
> **verificata in IL** / **verificata nei log** / **verificata nel codice della mod** / **non verificata**.

## [2026-10-08 23:15] Strumento di verifica del deploy: tools\verify-deploy.ps1

**Perche' esiste.** Un intero test e' stato buttato via perche' le due macchine eseguivano
**binari diversi**: G1 aveva ancora la build precedente e le sue righe di log non contenevano i
marcatori nuovi. Il test sembrava non misurare nulla, mentre stava misurando **il binario vecchio**.
Questa e' la voce 16 della lista iniziale, e da ora si esegue **prima di ogni sessione di test**,
**su entrambe le macchine**.

**Uso:**
```
pwsh -NoProfile -File .\tools\verify-deploy.ps1
pwsh -NoProfile -File .\tools\verify-deploy.ps1 -ExpectedHash 9ef0950b...
```
Esce con codice **1** se qualcosa non torna, quindi si puo' mettere in uno script.

**Controlla tre cose, e nessuna delle tre basta da sola:**

| Livello | Cosa dice | Perche' serve |
|---|---|---|
| **1. HASH** della mod | se il file e' quello atteso | scopre la DLL sbagliata copiata a mano |
| **2. MARCATORI** nel binario | se il file **contiene davvero** le funzionalita' | scopre un file ricostruito senza una parte |
| **3. PROTOCOLLO** di Common.dll | se mod e server **parlano la stessa lingua** | scopre Common.dll disallineata |

**Dettaglio scoperto scrivendo lo strumento — e un errore commesso e corretto.**

Common.dll **esiste in DUE copie, e NON sono identiche**:

```
Modding\data\Blasphemous.Multiplayer.Common.dll    net35     6144 byte   <- la carica il gioco
SERVER-COOP\Blasphemous.Multiplayer.Common.dll     net8.0    5632 byte   <- la carica il server
```

**La prima versione dello script confrontava i loro HASH e dichiarava "LE DUE COPIE DIVERGONO"**:
era un **falso allarme**, perche' **i due progetti compilano per framework diversi** e quindi
**gli hash sono diversi per costruzione**. **Il confronto giusto e' SEMANTICO**: deve coincidere il
**contenuto che riguarda il dialogo**, cioe' il **numero di protocollo** e i **valori di NetworkType**.

Verifica attuale sulla macchina locale:
```
mod    : protocollo=11  EnemyOwnership=25  valori NetworkType=19
server : protocollo=11  EnemyOwnership=25  valori NetworkType=19
i due protocolli COINCIDONO
```
*(RefusalType::Protocol = 11 e' dove vive il numero di protocollo.)*

**Marcatori riconosciuti dallo strumento** (stringhe di FORMATO, non nomi di simbolo — cercarli
come nomi di campo e' un errore gia' commesso una volta):

| Marcatore | Funzionalita' |
|---|---|
| `orientSkip=` | isteresi sull'orientamento attiva |
| `gameOrientBurst=` | contatore delle scritture del GIOCO |
| `[OrientByEnemy]` | ripartizione per-nemico delle nostre inversioni |
| `[GameOrientByEnemy]` | ripartizione per-nemico delle inversioni del gioco |
| `[EnemyJoin]` | fix del join: annuncio delle morti precedenti |
| `Nemici rinati` | fix del falo': _pendingDeaths azzerata al respawn |

**Nota operativa**: Common.dll **non e' copiata dal deploy automatico** (che copia la mod e il
server), quindi **va copiata a mano su entrambe le macchine, nelle destinazioni giuste**. Lo script
lo segnala.

## [2026-10-08 23:15] Orientamento: cosa e' stato implementato, e come si legge il test

**Stato**: implementato in stato-orient-isteresi e deployato. **Non ancora verificato in gioco.**

### I tre pezzi

| Pezzo | Dove | Cosa fa |
|---|---|---|
| **Prefix solo-misura** | OrientationProbePatch.cs | conta le scritture di orientamento del **GIOCO** su nemici **non posseduti** |
| **Isteresi** | EnemySyncManager.ApplyOrientation | **scarta** un'inversione quando |x_nemico − x_target| < 1.5 |
| **Contatori per-nemico** | PerformanceProbe | [OrientByEnemy] e [GameOrientByEnemy], con la **POSIZIONE** |

### La Prefix e' una PREFIX, non una Postfix — e il motivo conta

Una **Postfix** che "riporta l'orientamento del proprietario" **replicherebbe il jitter a 60 Hz**:
e' l'esatto contrario di quello che serve. La Prefix **decide prima**, e **non blocca nulla**
(restituisce oid): per ora **misura soltanto**, come previsto dal metodo di lavoro.

### Perche' il contatore vecchio era cieco (errore commesso e corretto)

orientFlip e' chiamato **solo** dentro ApplyOrientation, cioe' vede **le scritture della mod**
(20 Hz). **Le inversioni che si vedono sono quelle del GIOCO, ogni frame.** Nella sessione di
conferma orientFlip valeva **0 su 111 report e su entrambe le macchine** — **ma il fix non era
implementato**, quindi non poteva essere risolto. **Il contatore misurava se stesso, non il fenomeno.**
Da qui la Prefix, che vede **tutti** i writer.

### Il punto di aggancio, verificato in IL

`
Entity::SetOrientation   virtual=True  newSlot=True  Public, Virtual, HideBySig
OVERRIDE di SetOrientation: solo 3 → Amanecidas (boss), Isidora (boss), Penitent (giocatore)
chiamate: 181 callvirt (passano dalla base, la Prefix le vede)  ·  15 call (non virtuali)
`
**I tre override NON passano dalla base ⇒ boss e giocatore restano intatti**, che e' il voluto.

### EnemyBehaviour::GetTarget() — IL completo, e perche' e' il posto giusto

`
if (Entity == null) Entity = GetComponentInParent<Enemy>();
if (Entity.get_Target() != null) return Entity.get_Target().transform;
if (Core.Logic.get_Penitent() == null) Debug.LogError(...);
Entity.set_Target(Core.Logic.get_Penitent().gameObject);   <-- UNICO punto di riassegnazione
return Entity.get_Target().transform;
`
**Un solo punto di riassegnazione, ed e' GetTarget().** E **Core.Logic.get_Penitent() e' il
Penitente LOCALE**: **su ogni macchina il fallback punta al giocatore di QUELLA macchina** ⇒ i due PC
sceglierebbero automaticamente giocatori diversi. **E' il nodo della proposta "quanto di tempo"
sul cambio bersaglio** (voce 8).

**Campi di tempo gia' presenti in EnemyBehaviour:**
`
DeltaTargetTime   Single   (Family)              <- accessibile
targetTime        Single   (Private, InitOnly)   <- READONLY, non assegnabile
LookAtTarget      public virtual void (Vector3)  <- ma e' CODICE MORTO
`
⇒ il "quanto di tempo" **non puo' riusare 	argetTime**: va costruito.

### Correzione trovata verificando in IL, che avrebbe bocciato il fix in silenzio

La prima versione misurava la banda contro **Enemy.Target per tutti**. **Sbagliato**:
- Enemy.Target e' un **GameObject** assegnato da **molti** tipi (trappole, boss, FoolBehaviour,
  FirethrowerBehaviour, Piety*, AcolyteBehaviour, AshChargerBehaviour, WaxCrawlerBehaviour,
  Processioner, Familiar, ...);
- **Ghost NON lo usa**: il suo OnUpdate confronta lo sprite con **_penitent** (i campi di Ghost
  sono _penitent, ghostSpriteRenderer, ghostFlight, ghostPath, ...).

Misurando contro il target sbagliato — **che in co-op puo' essere l'ALTRO giocatore** — **la banda non
si sarebbe mai aperta**, il tremolio sarebbe continuato, e **lo strumento sarebbe sembrato funzionare**.
**Ora Ghost misura contro il Penitente locale**, tutto il resto usa Target con fallback al
Penitente locale, come fa GetTarget.

### Come si legge il test

| Nei log | Significato |
|---|---|
| gameOrient alto + orientSkip alto | il gioco combatteva **e l'isteresi l'ha assorbito** ✅ |
| gameOrient=0 | **il fenomeno non si e' presentato**: serve un altro test |
| orientFlip alto **nonostante** orientSkip | **la banda e' troppo stretta**, e **di quanto** lo dice il valore per-nemico |
| [OrientByEnemy] (x,y)=N | **quale** nemico trema, per **POSIZIONE** (le etichette SPAWN (NN) si permutano) |

### Rischio, e perche' e' basso

**Il gioco puo' solo rallentare o fermare questa isteresi, non peggiorarla**: se il target non e'
trovabile, TryGetEnemyTargetX restituisce alse e **l'inversione viene applicata come prima**.
**La mod puo' al massimo non girare il nemico per un istante.** Un'inversione **vera** (un giocatore
che attraversa da un lato all'altro) sposta il target di **molti metri** e **viene applicata come prima**.

**Da verificare per primo**: che un'inversione reale avvenga **comunque una volta sola**.

---

## [2026-10-08 23:18] ⚠️ L'isteresi era nel posto SBAGLIATO — diagnosi e correzione

**Questa entry corregge la precedente sull'orientamento.** L'isteresi era stata messa in
ApplyOrientation: **non ha mai funzionato**, e la misura lo dimostra senza ambiguità.

### Il test che l'utente credeva valido NON ha misurato l'isteresi

**Fatto accertato**: i due log del test *"molto molto meglio"* sono **privi di TUTTI i marcatori
nuovi** — orientSkip=, gameOrient=, gameOrientBurst=, [OrientByEnemy], [GameOrientByEnemy]:
**0 occorrenze su 111 probe**. Tutte col formato **vecchio**.

| Evento | Ora |
|---|---|
| **log catturati** | **22:31:11** |
| commit del probe (stato-orient-probe) | 22:35:49 |
| commit dell'isteresi (stato-orient-isteresi) | 22:41:53 |

⇒ **entrambe le macchine eseguivano la build PRECEDENTE all'isteresi.** **Non è il caso "G1 col
binario vecchio" di prima: era il binario vecchio su TUTTE E DUE.** **L'isteresi non è mai girata.**

**Numero di protocollo e marcatori vanno verificati PRIMA di ogni test, su ogni macchina**:
	ools\verify-deploy.ps1. Questa è la seconda volta che un test viene sprecato così.

### Il dato vero, dal log vivo post-fix

BepInEx\LogOutput.log (mtime 22:53:09), **46 probe col formato nuovo**, Host role: True:

| Metrica | Valore | Lettura |
|---|---|---|
| **orientSkip** | **0** — somma 0 su 46 report | **l'isteresi non ha scartato UNA SOLA inversione** |
| **orientFlip** | **85** totali, **MAX 34 in una finestra da 2 s** | **~35 inversioni/giro ancora in corso** |
| **gameOrient** | **5** totali, non-zero in **2/46** report (max 4) | il gioco scrive **pochissimo** |
| gameOrientBurst | max **4** | nessun burst |

**Il nemico che gira**: sp|D01Z01S01|SPAWN|-715.00|13.00 → posizione **(−715.1, 10.0)**.
**È esattamente il nemico di §1b**: l'@SPAWN senza spawn point.

**[GameOrientByEnemy] nomina SOLO oggetti di scena**:
`
(-697.1,12.8)=2   (-689.1,11.8)=1   (-715.1,10.8)=1     <- le entità inerti di §7i
`
I conteggi **non crescono mai**: sono scritture di **inizializzazione**. **Nessun nemico mobile ha
mai una voce in gameOrient.**

### La causa dell'insuccesso

L'isteresi stava in **ApplyOrientation**, cioè sulla **scrittura DELLA MOD** (**20 Hz**).
**Il tremolio lo produce il GIOCO a 60 Hz**, con Ghost::OnUpdate che chiama
Entity::SetOrientation **direttamente**:

`
Ghost::OnUpdate  IL_007e  sprite.x  vs  IL_0085 _penitent.x  ->  blt.un  ->  SetOrientation
`
**confronto nudo, nessuna banda.** **La guardia filtrava la mod, non il gioco: non ha mai visto il
nemico che girava.**

### La correzione

**La banda è stata SPOSTATA nella Prefix su Entity::SetOrientation** — **l'unico punto dove passa
Ghost::OnUpdate** e ogni altro writer. La Prefix ora **ritorna Boolean** e **restituisce alse**
quando la scrittura è un tremolio.

`
Prefix ritorna Boolean · 3 ritorni false (blocca) · 7 true (passa)
Blocca SOLO su nemici NON posseduti: l'orientamento del proprietario è quello autorevole
Il Penitente non è un Enemy -> cade fuori · i 2 boss con override non passano dalla base
ShouldSkipOrientationFlip e TryGetEnemyTargetX RIMOSSI da EnemySyncManager (non servono più)
`

**La misura confronta la x del PENITENTE LOCALE**, che è **quello che Ghost::OnUpdate legge
davvero** — **non Enemy.Target**. Solo una scrittura che girerebbe il nemico **contro** la
direzione in cui il target si trova, **e dentro la banda**, viene scartata.

**Tag**: stato-orient-prefix · **Rollback**: stato-orient-isteresi

---

## [2026-10-08 23:18] ⚠️ CORREZIONI a cose scritte prima in questo file

### Correzione 1 — EnemyBehaviour::LookAtTarget NON è codice morto

**Scritto prima**: *"tutto il corpo è irraggiungibile, 71/71 sottoclassi escluse"*. **Impreciso.**

**Verificato in IL**: LookAtTarget **è chiamato da ~130 siti** negli OnStateEnter/OnStateExit/
Update di decine di *AttackBehaviour / *IdleState / *TurnAroundBehaviour. **Il corpo è
raggiunto.** Ma con DeltaTargetTime < 0 sempre falso **esce subito dal 
et**:
**ciò che è morto è il GATE, non il metodo.** La conclusione pratica (*non gira il nemico*) regge,
**il ragionamento era sbagliato**.

### Correzione 2 — EnemyBehaviour::GetTarget NON riassegna il bersaglio per frame

**Verificato in IL**: riscrive Target **solo quando è NULLO**:
`
IL_0022 get_Target  →  IL_002c brfalse IL_0042   (solo se nullo)
IL_007a set_Target(Core.Logic.Penitent.gameObject)   <-- Penitente LOCALE
`
**I veri riassegnatori per-frame sono gli OnUpdate/OnStart di ~40 tipi Enemy** (Acolyte,
BellCarrier, Bishop, ChimeRinger, CrossCrawler, Flagellant, Fool, GhostKnight, HeadThrower,
Menina, Nun, PontiffHuskMelee/Ranged, Runner, ShieldMaiden, Swimmer, ViciousDasher, WaxCrawler,
WheelCarrier, ...). **Tutti assegnano il Penitente LOCALE.**

### Correzione 3 — le animazioni NON sono al livello dichiarato in §1a

**Nella run post-fix**: nimRestart=33 animRepeat=33 per finestra da 2 s, **per 18 finestre
consecutive (~36 s)** — **identico al vecchio**. **Il churn animazioni è allo stesso livello.**
§1a va riletta come **"accettabile"**, **non come "risolto in modo misurabile"**.

---

## [2026-10-08 23:18] La proposta "quanto di tempo" sul cambio bersaglio — verdetto

**Risposta secca**: **tecnicamente fattibile, ma NON risolve il tremolio** — è **un problema diverso**.

### È fattibile, con un vincolo preciso

**Strada più semplice e robusta**: **Prefix Harmony su Enemy::set_Target** — **un solo punto copre
~40 siti**; l'istanza la dà __instance; **nessun 	argetTime da forzare**.

| Strada | Verdetto |
|---|---|
| **Prefix su Enemy::set_Target** | ✅ **la più semplice**: un punto copre ~40 siti |
| Postfix su GetTarget | ❌ **non serve**: GetTarget scrive **solo se nullo**, non è la sorgente |
| Assegnare Target dalla mod e impedire le riassegnazioni | ❌ richiede di bloccare **tutti** i ~40 siti ⇒ è la Prefix fatta peggio |
| Scrivere 	argetTime | ❌ Private, InitOnly **e irrilevante**: Ghost::OnUpdate non lo legge mai |

**⚠️ IL VINCOLO CO-OP CHE DECIDE TUTTO**: EnemySyncManager replica dal proprietario **posizione +
animazione + vita** — **NON il bersaglio**.

> **Se ogni macchina scegliesse "il più vicino" localmente, le due sceglierebbero giocatori diversi**,
> **e il puppet verrebbe comunque corretto dalla posizione dell'arbitro** ⇒ **l'effetto è invisibile
> o è un tug-of-war.**

**⇒ La proposta è fattibile SENZA toccare il protocollo**, purché la Prefix sia **attiva solo sul
PROPRIETARIO** (EnemySyncManager.IsPuppet esiste già). **Se si volesse il bersaglio REPLICATO,
servirebbe un campo nuovo ⇒ bump di protocollo (mod + server + Common.dll, §7d).**

### ❌ Ma NON ferma il tremolio

| | Livello | Effetto sul tremolio |
|---|---|---|
| Isteresi (era nel posto sbagliato) | sintomo visivo | **nullo** — il gioco riscrive ogni frame |
| **"Quanto di tempo" sul bersaglio** | **scelta del bersaglio** | **nullo** — Ghost::OnUpdate **non legge Enemy.Target**, legge _penitent |

**Non sono la stessa cosa a due velocità: sono DUE problemi diversi, e la causa comune è
Ghost::OnUpdate che confronta due float senza banda.**

**Rischio: medio.** I behaviour leggono Target per decidere lo stato ⇒ un bersaglio "congelato"
per 1 s può far scegliere lo stato sbagliato (es. CanSeePenitent). **Costo: ~40-60 righe + il file
di patch.** **Serve un contatore 	argetChanges= nel probe**: **senza, non è misurabile.**

---

## [2026-10-08 23:21] ✅ `IMPRONTA DIVERSA` ERA UN FALSO ALLARME — causa accertata e corretta

**Voce 14 della lista iniziale.** Era classificata 🟢 cosmetico, poi 🔴 *"la premessa di tutto il
sync nemici"*. **La verità è nel mezzo, ed è la più insidiosa delle due: non era una divergenza,
era un rilevatore rotto.**

### Come è stato smascherato

**Dai log**, tre indizi che insieme non lasciavano alternative:

1. **L'errore compare su ENTRAMBE le macchine con i valori SCAMBIATI**:
   `
   G1:  host=-1418051589  locale=-997076802
   G2:  host=-997076802   locale=-1418051589
   `
2. **Il messaggio si contraddice da solo**: dichiara `id locali: 3` **mentre il valore locale non
   corrisponde a una lista di 3 elementi**.
3. **Nella stessa sessione `Post-spawn` riporta 3, poi 5, poi 3 nemici**: la lista viene
   **ricostruita e appesa più volte**.

### La causa

```
_listFingerprint calcolata SOLO in:
  OnEnemiesSpawned   -> _ids.Clear + RegisterEnemies + Sort + ComputeFingerprint
  LevelLoaded        -> _listFingerprint = 0

Ma _ids viene mutata ANCHE altrove:
  _ids.Add(id)      in RegisterEnemies   <- l'HOST la chiama OGNI 0.5 s
  _ids.Remove(id)   in ForgetEnemy
```
⇒ **la fingerprint pubblicata descriveva una lista che non esisteva più.** Il confronto metteva
quel valore **stantio** contro uno **onesto**, e segnalava nemici che non c'erano.

### E c'era un secondo difetto, peggiore del primo

La segnalazione scattava **UNA VOLTA per livello** (`_fingerprintMismatchReported == 0`):
**un falso allarme precoce non solo era sbagliato, ma ZITTIVA ogni divergenza reale per il resto
della scena.**

> **Un rilevatore che si spegne dopo il primo falso positivo è peggio di nessun rilevatore.**

### Il fix

| Pezzo | Cosa fa |
|---|---|
| `_idsDirty` | alzato **dove la lista cambia** (append e prune) |
| `RefreshFingerprintIfDirty()` | consuma il flag, ricalcola e **ri-arma la segnalazione**; chiamata da `UpdateCore` **una volta per passata** *(fuori dal blocco di rescan: un ricalcolo per scansione, non per nemico)* |
| **Cooldown 15 s** | la segnalazione **non è più una-tantum**: una divergenza **reale** resta visibile **senza inondare il log** |
| `_fingerprintMismatches` | contatore **nel messaggio**, per vedere a colpo d'occhio una divergenza ricorrente |
| `_fingerprintMismatchReported` | da `int` a **`float`**: ora contiene **un istante**, non un flag |

### Nota importante sul `prune`

**`_ids.Remove(id)` in `ForgetEnemy` è rischioso in sé**: togliere un elemento **sposta ogni
indice successivo**, e **l'indice È l'handle sul filo**. Ora **almeno la divergenza viene segnalata**
invece di restare nascosta.

**Tag**: `stato-fingerprint-fix` · **Rollback**: `stato-orient-prefix`

### Cosa aspettarsi dal prossimo test
```
IMPRONTA DIVERSA → deve SPARIRE
Se ricompare → è una divergenza VERA (non più mascherata), e il contatore dice quante volte
```

---

## [2026-10-08 23:22] ⚠️ §1a NON È CHIUSA — la firma è ancora presente, e ora è localizzata

**L'Esploratore aveva ragione**: nimRestart/animRepeat sono **ancora alti** nella run post-fix.
**Ma la firma è molto più precisa di prima**, e questo restringe il campo.

### I numeri

Dal log **post-fix** (LogOutput.log, 46 report col formato nuovo, Host role: True):

| Metrica | Valore |
|---|---|
| report con nimRepeat > 0 | **17 su 46** |
| nimRepeat | fra **11 e 36**, **MAX 36** in una finestra da 2 s |
| righe [EnemyAnim] | **499**, **TUTTE con mia=host** |
| vvio=0.00 | **484 su 499** |

**Le transizioni, contate senza la posizione** *(le posizioni variano, quindi contare per posizione
frammenta la stessa transizione)*:
`
386x  765711723 -> 2081823275     <- UNA transizione, 386 volte
 79x  765711723 -> 460696939
 10x  2081823275 -> 765711723
 10x  460696939 -> 2081823275
  8x  460696939 -> 765711723
`

**Tutte su un nemico vicino a x ≈ -716, y = 10.0** — **che è il nemico di §1b**
(-715.1, 10.0, l'@SPAWN senza spawn point, quello che si gira).

**Circa 390 inversioni in ~90 secondi ⇒ ~4,3 al secondo, su un nemico che il log chiama Idle.**

### ✅ Una buona notizia: il guardiano funziona, quindi NON è un ping-pong

**Tutte le 499 righe sono su UNA macchina** (mia=host). **Il motivo è nel codice**: LogAnimationChanges
salta la registrazione quando lo stato letto è quello che **noi** abbiamo applicato:
```
if (_appliedAnim.TryGetValue(id, out applied) && applied == stateHash)
{
    _lastAnim[id] = stateHash;
    continue;                                   // una nostra forzatura non è un cambio locale
}
```
⇒ **il churn è LOCALE all'host, non è la mod che rimbalza fra le due macchine.** **Questo esclude
una famiglia di cause e ne conferma un'altra.**

### Cosa è già escluso

| Ipotesi | Stato |
|---|---|
| La riga nimTime = 0f (passo 2) | ❌ **esclusa**: la riga **non c'è più** nel sorgente (resta solo il commento che la spiega) |
| Ping-pong fra le due macchine | ❌ **escluso**: il guardiano _appliedAnim sopprime le auto-indotte, e infatti tutte le righe sono su una macchina |
| La mod che scrive l'animazione localmente | ⏳ **da verificare**: ApplyAnimation è chiamata **solo** sul non-proprietario? **Se sì, il proprietario non riceve mai una ApplyAnimation, e la causa sarebbe del tutto nel gioco** |
| Stalli di frame | ❌ **esclusi** in precedenza (0/3 e 1/11 di coincidenza) |

### La domanda aperta

**765711723 e 2081823275 sono hash di stato di Animator** (Animator.StringToHash, FNV-1a a
32 bit sul nome). **Il controller è un asset, non sta nell'assembly**, quindi **vanno invertiti**.
**Sapere quale stato è un LOOP e quale un ONE-SHOT cambia tutto**: se 2081823275 è un attacco,
**386 ingressi = 386 attacchi**; se è un idle, **qualcosa ce lo rimette continuamente**.

**E vvio=0.00**: la mod legge NormalizedTime **dopo** la transizione, quindi 0.00 può essere
fisiologico — **ma la mod lo TRASMETTE, e il peer chiama Animator.Play(hash, 0, 0f) facendo
ripartire il clip da capo.** **Da chiarire se la mod peggiora la situazione del peer.**

**All'Esploratore è stato chiesto**: invertire i tre hash, identificare il tipo concreto del nemico
a x ≈ -716 in D01Z01S01 (vincolo: life massima **12**), trovare chi riporta lo stato a
765711723, e dire se la mod tocca il percorso del proprietario.

### Conseguenza sulla lista

**La voce 1 va riletta**: era ⏸️ *"accettabili, parcheggiata"*. **Il dato dice che il churn è allo
stesso livello di prima**, quindi *"accettabile"* resta una **scelta dell'utente**, non un risultato
tecnico. **La voce 12 (Camminata troncata) è la stessa cosa vista dall'altro lato.**

---

## [2026-10-08 23:23] ✅ ESCLUSA la mod come causa del churn: `ApplyAnimation` tocca solo il NON-proprietario

**Domanda**: la mod forza l'animazione anche sul **proprietario** (l'host, dove il churn è tutto
concentrato)? **Risposta, verificata nel codice**: **no.**

```
EnemySyncManager.cs:2269   if (ownedRemotely)
                           {
                               PauseEnemy(enemy);
:2277                          ApplyAnimation(enemy, animState, animTime);   <- dentro il ramo NON-proprietario
                               ApplyOrientation(enemy, orientation);
                           }
                           else { ... }
```

**ApplyAnimation ha UNA SOLA chiamata in tutto il progetto** *(verificato: grep su tutte le .cs
del client)*, **ed è dentro if (ownedRemotely)**. ⇒ **sul proprietario la mod non scrive mai
l'animazione.**

**E le 499 righe del churn sono TUTTE mia=host.** ⇒ **il churn è il gioco che anima il proprio
nemico.**

### Le ipotesi escluse, ora tutte con una prova

| Ipotesi | Verdetto | Prova |
|---|---|---|
| **La mod forza l'animazione sul proprietario** | ❌ **ESCLUSA** | ApplyAnimation è dentro if (ownedRemotely) |
| **Ping-pong fra le due macchine** | ❌ **ESCLUSA** | tutte le 499 righe sono mia=host |
| **La riga nimTime = 0f (passo 2)** | ❌ **ESCLUSA** | la riga non è più nel sorgente |
| **Stalli di frame** | ❌ **ESCLUSI** | coincidenza 0/3 e 1/11 |
| **Il carico della mod** | ❌ **ESCLUSO** | syncMs_avg 0,01-0,69 |

⇒ **Resta UNA sola domanda**, ed è quella girata all'Esploratore:
**765711723 → 2081823275 è un LOOP o un ONE-SHOT?**

- **se 2081823275 è un attacco** ⇒ **386 ingressi = 386 attacchi** ⇒ **non è un difetto**;
- **se è un idle** ⇒ **qualcosa ce lo rimette continuamente** ⇒ **è un difetto, e del gioco**.

**Nota di metodo**: questo è il tipo di esclusione che vale più di una diagnosi. **Non sappiamo
ancora qual è la causa, ma sappiamo con certezza dove NON è** — e **quattro famiglie di cause sono
state eliminate con una prova, non con un ragionamento.**

---

## [2026-10-08 23:25] Strumento: ripartizione per-nemico degli avvii di animazione (`[AnimByEnemy]`)

**Perché è servito.** Il churn non era **attribuibile**. Contando le transizioni per coppia di stati
**senza distinguere il nemico**:
```
386x  765711723 -> 2081823275
```
sembrava **UN nemico in loop**. **Ma lo stesso conteggio si ottiene con PIÙ nemici che la fanno
qualche decina di volte ciascuno.** **Non è un dettaglio**: cambia se la causa è **un nemico
impazzito** o **un comportamento generale della stanza**.

> **Errore di metodo già commesso una volta in questa sessione**: ho presentato "386 volte" come se
> fosse un nemico solo. **Non lo era, e non potevo saperlo** — **i due casi sono indistinguibili dal
> conteggio che avevo.**

### Cosa è stato aggiunto

| Pezzo | Cosa fa |
|---|---|
| `_animRestartsByEnemy` | ripartizione per nemico, **alimentata da `NoteAnimationRestart`** |
| `LabelOf(enemy)` in `LogAnimationChanges` | **l'etichetta porta la POSIZIONE** (le etichette `SPAWN (NN)` **si permutano fra run**); `LabelOf` è **cache per istanza**, quindi **non alloca sul percorso caldo** |
| riga `[AnimByEnemy] avvii=...` | **le 4 peggiori**, accanto alle due dell'orientamento ⇒ **i tre report sono confrontabili** |

**Perché `LabelOf` e non `ShortId`**: stessa etichetta usata dai tally dell'orientamento ⇒
**i report si leggono insieme**, e `ShortId` può guadagnare un suffisso quando due spawn point
condividono la posizione.

**Scelta deliberata: è un intervento di MISURA, non un fix.** **Non sappiamo ancora se il churn sia
un difetto** — l'Esploratore sta stabilendo se `2081823275` è **un attacco** (386 ingressi = 386
attacchi ⇒ **nessun difetto**) o **un idle** (⇒ **qualcosa ce lo rimette**). **Cambiare il
comportamento prima di saperlo sarebbe stato scrivere codice su un'ipotesi**, ed è la disciplina che
ci ha evitato tre regressioni.

**Tag**: `stato-anim-tally` · **Rollback**: `stato-fingerprint-fix`

### Cosa aspettarsi dal prossimo test
```
[AnimByEnemy] avvii=(-715.1,10.0)=N  (-688.8,10.0)=M  ...
   -> se il churn è concentrato su UNO, è un nemico impazzito
   -> se è distribuito su tutti, è un comportamento generale della stanza
```
**E `tools/verify-deploy.ps1` ora controlla 8 marcatori**, fra cui i due nuovi.

---

## [2026-10-08 23:28] `[AnimName]`: i nomi degli stati di animazione risolti IN GIOCO (l'inversione statica fallisce)

### Il tentativo statico, e perché fallisce — vale la pena saperlo

**I tre hash del churn sono `765711723`, `2081823275`, `460696939`.** In Unity
`AnimatorStateInfo.shortNameHash` è **FNV-1a a 32 bit** sul nome dello stato.

**L'algoritmo è corretto**, verificato contro i nomi di stato che l'assembly usa davvero:
```
Fnv("Idle")  = 1168775091        Fnv("Death") = -1851835411
Fnv("Parry") =  917381717        Fnv("Hurt")  =  -487962152
```
*(i nomi raccolti dalle chiamate `Animator::Play`/`CrossFade` nell'assembly)*

**Ma i tre valori del churn NON corrispondono a NESSUNA stringa di `Assembly-CSharp`**
*(verificato: hash di tutte le `ldstr` più tutti i nomi di tipi, campi, metodi e proprietà)*.

> **Sono nomi di stato di un `AnimatorController`, che è un asset Unity IMPACCHETTATO**
> (`data.unity3d` 173 MB, più `sharedassets*.resource`). **Leggerli richiede un estrattore Unity.**

**⇒ L'inversione statica è un vicolo cieco ragionevole, e va lasciata.**

### La soluzione: chiedere il nome al gioco

**La mod legge il clip che l'Animator sta suonando** (`GetCurrentAnimatorClipInfo`) **e lo scrive
UNA VOLTA PER HASH**:
```
[AnimName] stato=2081823275 clip='...' primo-avvistamento=(-716.1,10.0)
```
**Costo: una manciata di righe in tutta la sessione**, senza possibilità di inondare il log
*(un `HashSet<int>` di hash già risolti)*.

### Perché è LA domanda che chiude il capitolo

| Se lo stato in cui si entra centinaia di volte è… | Allora |
|---|---|
| **un ATTACCO** | **sono centinaia di attacchi reali** ⇒ **NON c'è alcun difetto** ✅ |
| **un IDLE** | **qualcosa ce lo rimette dentro** ⇒ **è un difetto** ⚠️ |

**È l'unica informazione che mancava per decidere se toccare qualcosa.** *Tutti i numeri raccolti
finora — 386, 33/2s, 484/499 con `avvio=0.00` — dicono **quanto** succede, non **cosa**.*

**Tag**: `stato-anim-nomi` · **Rollback**: `stato-anim-tally`

**`tools/verify-deploy.ps1` ora controlla 9 marcatori.** Verifica attuale sulla macchina locale:
**tutti presenti, protocollo 11 su mod e server.**

---

## [2026-10-08 23:28] Chiarimento sulla voce 4: lo stato del falò è GIÀ sincronizzato, il respawn NO

**Verificato**: `PrieDieuHelper.cs` **esiste ed è già attivo**
(`ProgressSync/Helpers/PersistentObjects/PrieDieuHelper.cs`), con **tre patch**:
```
[HarmonyPatch(typeof(PrieDieu), "OnUse")]                     Postfix -> UsePersistentObject(persistentId)
[HarmonyPatch(typeof(PrieDieu), "GetCurrentPersistentState")] Prefix  -> Ligthed = true
[HarmonyPatch(typeof(PrieDieu), "SetCurrentPersistentState")] Prefix  -> salta se già interagito
```
⇒ **lo stato ACCESO del falò è già sincronizzato fra le due macchine.** *(coerente con la tabella
dello stato del progetto, dove "il falò si accende su entrambi" era già ✅.)*

**La voce 4 parla di un'altra cosa**: **il RESPAWN e il REFILL** — che sedersi al falò **fa rinascere
i nemici** e **ricarica le fiale**, e **quell'azione resta locale**. **Sono due cose distinte che
nella lista sembravano una sola**, e questo chiarimento evita di re-indagare lo stato del falò, che
è già risolto.

### Il meccanismo, verificato in IL adesso

```
RespawnDeadEnemies  chiamato da:
   PenitencePE02::OnLevelLoaded               <- una penitenza che respawna a ogni caricamento
   <ActivationLogic>c__Iterator1::MoveNext     <- l'attivazione del falò
   <ReActivationLogic>c__Iterator2::MoveNext   <- la ri-attivazione

AddConsumedSpawner  chiamato da UN SOLO posto:
   EnemySpawnPoint::OnEntityDie
```
⇒ **RespawnDeadEnemies è il punto in cui il gioco azzera la propria lista dei consumati**, ed è
**lo stesso metodo** su cui ora c'è il postfix che azzera _pendingDeaths *(fix del falò, §3)*.
**Quindi i due lati del falò passano ora dallo stesso punto.**

### Stato della voce 4: 🟡 APERTA, ma con una precisazione

**Non c'è prova recente che il disallineamento si presenti ancora** — e va detto: il test dopo il fix
ha visto **i nemici tornare su ENTRAMBE** le macchine. **Quindi la voce va tenuta aperta ma NON come
difetto confermato: come comportamento da verificare in un test dedicato.**

**Cosa servirebbe per chiuderla**: un test in cui **UNA SOLA macchina** si siede al falò, e si guarda
se **l'altra** vede rinascere i nemici. **Se non li vede, la voce è confermata.**


## [2026-10-10 03:10] Sessione di test: il desync e' CHIUSO, e tre fix nuovi (voci 24, 25, 26)

### Il verdetto sul desync: CHIUSO, con una prova forte

La sessione precedente era andata in **desync grave**. La causa era **una regressione nostra**,
introdotta in `b2c9cdd` con la strumentazione `[EnemyTree]`: la riga leggeva
`behaviour.BehaviourTree.isRunning` con una **chiamata diretta**, ma quel getter e'' **protected a
runtime** (la mod compila contro `Blasphemous.GameLibs`, un reference **publicized**, dove risulta
pubblico). Il compilatore accettava, **Mono rifiutava**: **479 eccezioni in un log e 666
nell''altro**, tutte da `UpdateCore -> ResumeEnemy`, che e'' il **percorso caldo**. L''eccezione
**interrompeva il ciclo di invio a metà**, quindi `SendEnemyUpdate` **non partiva mai**: il canale
restava vivo per i giocatori e morto per i nemici, e il sintomo sembrava un bug di desync da
tutt''altra parte.

**Non era preesistente**: `git show f854576:...EnemySyncManager.cs` non contiene `LogTreeState`.

**Corretto in `9b1ea87`** (reflection + try/catch + `_treeRunningUnavailable`), poi irrobustito:
 - `15eaadd`: tre blindature, fra cui un **`try/catch` DENTRO il ciclo di invio** (un nemico non
   puo'' piu'' fermare l''intera passata) e **`exceptions=` nella riga `[Probe]`**;
 - `86fd1dc`: corretta una blindatura **sbagliata** scritta in `15eaadd` (la doppia diagnosi in
   `PauseEnemy` **uccideva la deduplica** -> decine di righe di log al secondo).

**Prova che e'' chiuso**: nei quattro log del test, **`exceptions=0` su 384 report**, **zero stack
trace**, zero `IMPRONTA DIVERSA`, zero `CACHE DIVERGE`, **zero `ModLog.Error`**. Il binario
installato coincide con il sorgente di HEAD (verificato per hash).

**Lezione di metodo, e va ricordata**: nell''IL avevo **visto** che quel getter non era pubblico, e
ho tirato dritto **perche'' il progetto compilava**. **Compilare non e'' funzionare**: in Mono
l''accessibilita'' a runtime e'' un controllo separato da quello del compilatore. La classe di
rischio e'' **armata** per ogni riga futura che tocchi un membro del gioco.

### Voce 26 — la pausa non esisteva per 60 tipi di nemico su 77 (fix da 1 riga)

`Enemy.EnemyBehaviour` **non e'' la risposta generale**: solo **17 delle 77** sottoclassi di `Enemy`
chiamano mai `Enemy::set_EnemyBehaviour`. Le altre **60** - fra cui **Fool e WheelCarrier**, cioe''
**tutti i nemici della stanza di test**, e quasi tutti i boss - tengono il componente in una
**proprieta'' propria** (`Fool.Behaviour`, `WheelCarrier.Behaviour`, pubbliche, assegnate in
`OnStart`/`OnAwake`).

Conseguenza: `PauseEnemy` leggeva la proprieta'' di base, la trovava null, e **usciva alla riga del
controllo**, PRIMA di `PauseBehaviour()` e `enabled = false`. Per il **78% dei tipi** la pausa
**non avveniva**: sul non-proprietario restavano attivi **l''albero NodeCanvas, l''Animator e
l''orientamento**, mentre il corpo stava fermo. Misurato su **un solo Fool non posseduto**: **437
scritture di orientamento** e **54 riavvii di animazione**, con la posizione che si sposta di **0.1**.
Cioe'' **il "cammina sul posto"**, ed e'' la spiegazione piu'' plausibile dei residui di animazioni
tagliate.

**Fix** (`ea9a9f6`, tag `stato-behaviour-fallback`): `FindBehaviour(enemy)` = la proprieta'' di base,
e se e'' null `GetComponent<EnemyBehaviour>()` poi `GetComponentInChildren<EnemyBehaviour>()`.
**E il difetto era in TRE punti, non due**: oltre a `PauseEnemy` e `ResumeEnemy`, anche il **ramo di
PROMOZIONE di `SetHost`** saltava quei nemici, quindi **promuovere a host non faceva ripartire
l''AI** per quegli stessi 60 tipi.
**NON toccato di proposito**: `IsInertScenery`, che usa `enemy.EnemyBehaviour == null` per
**classificare** uno stand-by. Li'' un fallback **cambierebbe la classificazione**, e non e''
verificato che sia un miglioramento: da rivedere a parte.

### Voce 25 — le morti non venivano annunciate fuori dalla stanza delle uccisioni

`AnnounceDeathStateToPeers` ricavava gli id da `ConsumedSpawnPointIds()`, che usa
`FindObjectsOfType<EnemySpawnPoint>()`: **solo la scena corrente dell''annunciatore**. Se al momento
della riconnessione l''annunciatore era altrove, la lista era **vuota e il metodo usciva IN
SILENZIO**, e **i nemici gia'' uccisi riapparivano VIVI** a chi entrava.

**La prova nei log e'' una riga**: `"G2 has joined the server!"` **senza** nessun
`[EnemyJoin] annunciate` prima, mentre l''annunciatore era in una stanza **senza spawn point**;
contro **quattro join precedenti** - con l''annunciatore nella stanza delle uccisioni - che
annunciavano **2, 3, 5 e 5**. **Il fattore decisivo e'' la stanza dell''ANNUNCIATORE, non di chi si
riconnette.**

**Fix** (`5693b86`, tag `stato-riconnessione`):
 1. `_announcedDeaths`: ogni morte annunciata viene **ricordata**. **NON si svuota in `LevelLoaded`,
    di proposito**: attraversare una stanza e'' esattamente il caso per cui esiste.
 2. L''annuncio invia **l''unione deduplicata** fra gli id della scena corrente e quelli ricordati.
    Un id di spawn point e'' costruito da **scena, nome e posizione** - dati di scena - quindi la
    stessa stringa esce su entrambe le macchine e l''unione **non raddoppia**.
 3. **Una riga di log quando l''annuncio e'' VUOTO.** Il caso era invisibile, ed e'' **l''unico
    motivo per cui il difetto e'' stato scoperto per caso** invece che leggendo un log.

### Voce 24 — il gioco azzera `consumedSpawns` in DUE metodi, e il mio piano ne copriva uno

**Correzione trovata dall''Esploratore, e vale piu'' del fix stesso.**
`EnemySpawner.Reset` - oltre a `RespawnDeadEnemies` - azzera la lista dei consumati. I suoi **tre
chiamanti** sono **morte del giocatore, caricamento di un salvataggio, reset dal menu**: **nessuno
e'' un caricamento di livello normale** (verificato in IL).

Svuotare la memoria **solo al falo''** lasciava quindi un ricordo **stantio**: il nuovo entrato
veniva mandato a **uccidere nemici che l''annunciatore vedeva in piedi**. Cioe'': **il fix della
voce 25 introduceva l''errore OPPOSTO.** Da qui il secondo hook, `EnemySpawnResetPatch` su
`EnemySpawner.Reset`, e `OnDeathStateReset()`.

**Piano originale vs realta''**: avevo scritto *"~10 righe, nessun cambio di protocollo"* e
*"si svuota dove si svuota `_pendingDeaths`"*. **Erano due assunzioni sbagliate**, entrambe
scoperte prima di scrivere il codice:
 - **`SendEnemyDeathGlobal` NON viene chiamato a ogni morte**: `UpdateCore` esce prima se non
   connesso. Quindi la memoria registra **solo le morti avvenute da connessi** - che e'' anche
   corretto, perche'' e'' l''unica cosa che il peer puo'' sapere;
 - **il gioco azzera in due posti, non uno** (sopra).

### Il blocco (voce 27) — e una correzione a una mia affermazione

**17 run** con `posNemico` ferma per >=3 campioni e un giocatore a <5 unita'' (ricalcolato dalle
**posizioni grezze**). Il peggiore: **`WheelCarrier` a `(-697.1,11)` fermo 134 campioni, ~67 s**.
**Due cause distinte**: *(a)* nel test "stanza diversa" **nessuno lo simula** (il padrone e'' in
un''altra stanza -> nessun token di proprieta''); *(b)* con entrambi in stanza, il nemico resta
`Idle` a **1-4,5 unita''**.

**La spiegazione dell''utente (una pendenza) e'' compatibile per 2 casi su 3** - i giocatori sono
0,9-1 unita'' **sopra** il nemico - ma **NON** per il Fool a `(-717.7,10)`: **fermo 37 campioni
(18,5 s) con il giocatore a 1,1 unita'' e `dy = +0,1`**. E in IL il gioco ha **tutta**
l''attrezzatura per un limite del genere: `VisionCone` (presente sia su `Fool` sia su
`WheelCarrier`), `EnemyBehaviour.VisualSensor`, `EnemyAI.maxRangeGroundDetection`, `GroundLayerMask`.

**⚠️ Quindi NON si tocca il codice**: prima serve il **test in singolo SENZA mod nello stesso
punto**. **Se resta immobile anche li'', non e'' un difetto della mod e la voce si chiude da sola.**

**E una mia affermazione va RITIRATA**: avevo scritto in piu'' occasioni che **`dist_io` e''
incoerente con le posizioni nella stessa riga**. **NON e'' riproducibile**: lo scarto massimo e''
**0.13**, cioe'' l''arrotondamento a 0.1 su oltre 2200 righe. **La trappola vera e'' un''altra**:
**`posAltro` e'' la PRIMA entry di `_remotePositions`** - puo'' essere **stantia, o di un''altra
stanza** - mentre **`dist_altro` e'' il minimo su TUTTE**. Chi legge i log lo sappia.

### Il fantasma: NON e'' comparso, e il test lo ha coperto

Confronto per id fra i due log principali: **nessun id resta vivo/immobile su una macchina mentre
e'' morto sull''altra**. Il percorso "uccisione mentre l''altro era altrove" **e'' stato
esercitato** e **ha tenuto**. Le tre righe di morte mancanti hanno una spiegazione **benigna**,
verificata nel codice: chi riceve la morte **distrugge la copia locale** prima che lo snapshot (2 Hz)
possa vederla morta.

**E una correzione a un mio allarme**: temevo che il `try/catch` del ciclo di invio potesse produrre
un **fantasma permanente** (l''id finisce in `dead`, `ForgetEnemy` lo toglie da `_ids`, e il peer
non riceve mai la morte). **Il meccanismo non e'' raggiungibile per quella via**: le send sono
**pura memoria** (`QueueMesssage` fa solo `sendingQueue.AddRange`, **nessun I/O**); la scrittura
sulla socket sta in `SendQueue()`, che ha gia'' il suo `catch (IOException)`.
**Il fantasma che PUO'' accadere** - se la scrittura fallisce o il relay perde il pacchetto - **il
riordino delle righe non lo coprirebbe comunque**. E **"per sempre" e'' falso**: sparisce a **cambio
scena, riconnessione, falo'', o un colpo del peer** (`SettleStuckDeaths` gira su entrambe).

### Stato dei tag

| Tag | Cosa | Rollback |
|---|---|---|
| `stato-misura-uscita` | prima del ripristino | — |
| `stato-proprieta-ripristino` | ripristino + `[EnemyTree]` | `stato-misura-uscita` |
| `stato-tree-noreflection` | fix della regressione (reflection) | `stato-proprieta-ripristino` |
| `stato-blindature` | tre blindature | `stato-tree-noreflection` |
| `stato-blindature-2` | correzione della blindatura sbagliata | `stato-blindature` |
| `stato-behaviour-fallback` | voce 26 (`FindBehaviour`) | `stato-blindature-2` |
| `stato-riconnessione` | voci 24 e 25 | `stato-behaviour-fallback` |

### Cosa NON e'' stato fatto, ed e'' voluto

 - **Non toccare `LevelLoaded`** per svuotare la memoria: **romperebbe** la voce 25.
 - **Non toccare** `ConsumedSpawnPointIds` / `ConsumedSpawnPositions`, `MarkSpawnPointConsumed`,
   `ReceiveEnemyDeathGlobal`, il protocollo.
 - **Non toccare l''ordine `ThawPhysics` / controllo `behaviour`** in `ResumeEnemy`: e'' corretto
   (congelare e scongelare sono due metodi opposti, non oscillano).
 - **Non aprire un cantiere sul blocco** (voce 27) prima del test in singolo senza mod.
 - **Non aggiungere altra diagnostica al ciclo di invio**: il dedup e il `try/catch` per-nemico
   funzionano (`exceptions=0` su 384 report).
 - **Residuo noto e non coperto**: le uccisioni fatte con la mod **NON connessa** (single-player puro)
   non passano da `UpdateCore` e **non entrano in memoria**. E'' coerente con cio'' che il peer puo''
   sapere, ma e'' un limite da conoscere.

### Verifiche ancora mancanti (per dichiarare chiuso con piena fiducia)

 1. una sessione con **`multiplayer sync` lanciato su ENTRAMBE** le macchine in momenti allineati,
    per confrontare `sent=` / `recv=` / `fingerprint` - **che la riga `[Probe]` NON porta**, e senza
    i quali "nessun pacchetto perso" e'' dedotto dall''effetto e non misurato;
 2. un caso con un **boss** (nemico stand-by che si attiva dopo) e un **falo''**
    (`RespawnDeadEnemies`) - **e ora anche `EnemySpawner.Reset`** (voce 28), che **non e'' mai stato
    eseguito in gioco**.


## [2026-10-10 05:40] CAPITOLO CHIUSO — blocco, test a due, riconnessione, reset

Verdetto dell''Esploratore sui **sette log** del test finale. Binario testato
`aea02dcf50f28b38abeb0ab743dbb45c09e813aee32705bd69117bc691feb426` (tag `stato-riconnessione`);
l''unica differenza con `20a6970` e'' **la chiave di deduplicazione di `LogTreeState`**, quindi i log
restano validi per tutto il resto (diff verificato con `git diff`).

### Il blocco (voce 27): NON e'' un difetto della mod. CHIUSA.

L''utente aveva chiesto il test **in singolo senza mod**; ha fatto di meglio: **in singolo SENZA
CONNESSIONE**, con la mod attiva. Cosi'' si ha **il logging** *(`[EnemyTick]`)* e **nessuna
sincronizzazione**: se il nemico e'' fermo, **e'' il gioco**. Il percorso della pausa remota e''
**inerte** quando `connected=False`.

**Il criterio e'' soddisfatto** — `posNemico` identica per **6 campioni (~12 s)** con distanza grezza
**3.07** *(giocatore fermo a `(-721.3,9)` per 65 s, nemico a `(-717.7,10)`)* — **ma la causa e'' la
pendenza**:

```
nel tratto fermo:  dy = +1.0  COSTANTE   (il giocatore e'' SOTTO il nemico)
salito a y=10.7 (stesso piano, dist 0.81):  il nemico PARTE SUBITO (anim=765711723 = camminata)
```

**Controprove indipendenti**: in **Fase A** *(single-player, nessun peer)* lo stesso nemico e'' fermo
**32 campioni**; e nei log **a due** lo stesso Fool e'' fermo anche con `dist_io` **0.5-1.4**.

⚠️ **E va detto che la voce 27 conteneva una MIA misura sbagliata**: avevo scritto
`dy = +0.1`. **Era il campione di CONTATTO, non il tratto fermo.** Col siero giusto la spiegazione
dell''utente *(la pendenza)* regge in pieno. **La voce e'' chiusa e il codice non si tocca.**

*Limite dichiarato*: il test e'' "senza **connessione** con la mod caricata", non "senza mod". Il
verdetto regge per la Fase A e per l''inerzia del percorso di pausa, non per un test a mod scaricata.

### Il test a due: nessun blocco reale, e il fix della voce 26 e'' CONFERMATO

| | prima | G1 DUE | G2 DUE |
|---|---|---|---|
| run con `posNemico` ferma >=3 campioni | **17** | **5** | **5** |
| peggiore | **134 campioni (~67 s)** | **10 (20 s)** | **9 (18 s)** |
| scritture orientamento (max per nemico) | **437** | **14** | **5** |
| riavvii animazione `[AnimByEnemy] avvii=` | **54** | **14** | **21** |

**Il run peggiore ora e'' un nemico fermo PER NATURA a 14 unita''** *(dmin 13.94)*: non e'' un blocco.
**La riduzione del fix `FindBehaviour` e'' di ~10-30x.** `orientSkip=0`, `orientFlip` **11 e 1** in
totale su tutta la sessione. **L'"e'' fluidissimo, per me e'' risolto" dell''utente e'' confermato
dai numeri.**

*Baseline non verificata*: la misura "17 run / 134 campioni" viene da una sessione i cui log non sono
fra questi sette. Il confronto e'' omogeneo solo perche'' il criterio e'' identico.

### La riconnessione (voci 3 e 25): COMPLETA, conteggi combacianti

```
G1: [EnemyKill] x5  →  Dead=True vita=0.0  →  5 SpawnPoint consumati
    posizioni tutte DISTINTE: -715.9,10 · -698.4,11 · -689.3,10 · -685.1,9 · -684.6,9
G1: [EnemyJoin] annuncio vuoto per la scena D01Z01S07 ... uso solo le 5 morti in memoria
G1: [EnemyJoin] annunciate 5 (0 dalla scena, 5 dalla memoria)      ← 5 = 5
G2: ENTITY_DEAD x5  →  Post-spawn: 0 enemies / 7 spawn points / 0 ids
```

**Zero doppioni** *(la dedup a monte e'' `_killLogged.Add(id)`, e `_announcedDeaths` e'' un
`HashSet`)*. **Nessuna morte inventata.** E **G2 che non annuncia nulla e'' CORRETTO**: non uccide
nulla, e l''annuncio e'' un''**unione deduplicata**.

### Il reset (voci 24 e 28): hook esatto — ma il FALO'' resta aperto

`EnemySpawner.Reset` **scatta** e il conteggio **combacia**: G1 uccide **2** nemici e il log dice
**`dimenticate 2 morti annunciate e 0 ricevute`**, nell''ordine giusto *(dopo la 2ª morte, prima
della rinascita)*. **Il gancio non e'' troppo aggressivo.**

🔴 **Ma il reset ha rivelato una divergenza vera (voce 29)**: **`IMPRONTA DIVERSA` ×4** nel log di
G2, con gli id locali che scendono **da 5 a 3** *(`Post-spawn: 2 enemies` su G2 dove G1 ne ha 3)*.
**Causa**: `EnemySpawner.Reset` **non trasmette nulla** — **e'' un evento LOCALE**. ⇒ **chi non lo
esegue resta con i suoi consumati.** **Il rilevatore ha funzionato: ha segnalato una divergenza
reale, non un falso allarme.**

**Ed e'' lo stesso meccanismo del falo''**, che quindi **resta aperto** (voce 4): in IL
`RespawnDeadEnemies` fa `consumedSpawns.Clear()` + `EnemySpawnPoint.CreateEnemy()` **in locale** +
`OnEnemiesRespawn.Invoke()`, e **i tre chiamanti sono tutti della macchina che si siede**.
`OnEnemiesRespawned`/`OnDeathStateReset` svuotano le statiche **solo in locale**: **la macchina che
non tocca il falo'' non riceve nulla e conserva la sua memoria.**

⚠️ **Quindi**: il fix della voce 24 ha chiuso la causa dell''annuncio **stantio**, **non
l''asimmetria**. **La strada "falo''" NON risolve** una macchina in un''altra stanza che non lo tocca.
**E'' il punto su cui l''utente ha intuito bene** *("trovare la soluzione del reset in caso di seduta
ai falò sarebbe molto utile")*: **e'' utile, ma da solo non basta** — serve **un annuncio di rete**
quando il respawn avviene, non solo uno svuotamento locale.
**Nel test non si e'' materializzato** *(G2 non ha mai annunciato N>0)*: **reale ma latente** (voce 30).

**E il ramo del falo'' vero non e'' mai stato eseguito** (voce 31): solo `EnemySpawner.Reset` lo e''.
**Serve un test con falo'' su UNA SOLA macchina.**

### Il difetto mio trovato NEI log, e gia'' corretto

**1140 righe di `[EnemyTree]` su 2137** in G1 DUE. La chiave di dedup era `enabled|running|esito` e
**due punti chiamano `PauseEnemy` con motivi diversi** *(il ciclo di invio passa nullo -> `?`, il
percorso pacchetti passa `remote owns`)*: il motivo **alternava**, ogni chiamata sembrava un
cambiamento, **la deduplica era morta**. Le combinazioni contate lo dicono senza ambiguita'':
`234 x (15) enabled=False isRunning=null esito=?` contro `230 x ... esito=remote owns`, **stato
IDENTICO**.

**E'' lo STESSO difetto che avevo gia'' fatto con `:prima`/`:dopo`.** Corretto in `20a6970`: **la
chiave e'' `enabled|running`**. **La lezione, perche'' non si ripeta una terza volta**: *la
deduplicazione deve rispondere a "questo nemico e'' in uno stato diverso da quando l''ho riportato",
e la risposta non puo'' dipendere da CHI ha fatto la domanda.* Il motivo e'' contesto, non stato.

⚠️ **E mentre applicavo quella correzione ho rotto il metodo TRE volte** modificando per numero di
riga *(cancellata la scrittura nel dizionario — che **compilava** e avrebbe reintrodotto lo stesso
difetto; poi la riga `Vector3 p`; poi la chiamata `ModLog.Info`)*. **L''ho riscritto per intero.** La
lezione: **compilare non e'' funzionare, e "sembra corretto" non e'' una verifica.** Su questo file,
**niente piu'' sostituzioni per numero di riga**.

### Cosa NON toccare

 - **La voce 27** *(il blocco)* e **il single-player**: chiusi, il codice e'' corretto.
 - **Il codice della riconnessione**: verificato, conteggi combacianti.
 - **La chiave di dedup di `LogTreeState`**: corretta in `20a6970`, verificata in IL.
 - **L''ordine `ThawPhysics` / controllo `behaviour`** in `ResumeEnemy`: e'' corretto.

### Cosa NON e'' stato possibile verificare

 1. **Il falo'' su ENTRAMBE le macchine**: il ramo `RespawnDeadEnemies`/`OnEnemiesRespawned`
    **non e'' mai stato eseguito** in nessuno dei sette log.
 2. **La baseline "17 run / 134 campioni"**: i log di quella sessione non sono fra questi.
 3. **Se durante il blocco il giocatore stesse ATTACCANDO il nemico**: il log non registra
    input ne'' danno.
 4. **Se il gioco crei l''enemy anche su chi ha lo spawn point consumato**: la riga `Post-spawn: N
    enemies` **non e'' letta dal codice della mod**, quindi "5 enemies" non prova che siano
    registrabili.
 5. **Metodo**: l''Esploratore campiona **l''ultimo `[EnemyTick]` di ogni probe** *(5 per probe)*,
    quindi se due nemici sono vicini nello stesso probe uno dei due puo'' sfuggire al conteggio.

### Stato dei tag

| Tag | Cosa | Rollback |
|---|---|---|
| `stato-behaviour-fallback` | voce 26 (`FindBehaviour`) | `stato-blindature-2` |
| `stato-riconnessione` | voci 24 e 25 | `stato-behaviour-fallback` |
| `stato-dedup-fix` | la chiave di `LogTreeState` | `stato-riconnessione` |


## [2026-10-10 09:30] I DISTRUTTIBILI (i vasi) e L'ARBITRAGGIO: due capitoli che cambiano forma

### PARTE 1 — I vasi: cosa sono, e perche'' il piano del TODO era sbagliato

**Il tipo e'' `Tools.Level.Actionables.BreakableObject`.** In Blasphemous i "vasi" non esistono come
tipo *(0 match per `Pot`/`Vase`/`Urn` su tutta Assembly-CSharp)*: la scenografia che si spacca sono i
prefab **`ACT_Lantern_breakable`**, **`ACT_TwistedSymbol_breakable`**, **`ACT_HangedEskleton_breakable`**,
e portano **tutti** il componente `BreakableObject`. Si rompono con **UN SOLO colpo**.

⚠️ **E il TODO §4 sbagliava la strada**: diceva *"4 file gemelli di `BreakableWallHelper.cs`"*.
`BreakableWall` e `BreakableObject` sono **FRATELLI, non parenti** *(entrambi derivano da
`PersistentObject`)*, e **hanno cicli di vita diversi**: `BreakableWall` **non ha ne'' `OnUsePrieDieu`
ne'' `Restore`** *(e'' permanentemente rotto)*, `BreakableObject` **si''** — il gioco lo ripristina
a ogni falò. **Sono due problemi diversi, non due istanze dello stesso.**

**E l''utente vuole proprio QUESTI**: *"a me interessa solo di questi, di base la mod sincronizza tutto
il resto... a me interessano solo i vasetti, scheletri, lanterne di scena e stupidagini che si rompono
e respawnano"*. **La sua idea originale — "come il sync dei nemici" — era quella giusta.**

### L''IDENTITA'': IL GUID, E NON `scena~indice`

Verificato **contro il salvataggio reale**, non per deduzione: i `BreakableObject` hanno **GUID veri,
per-istanza e authored di scena** *(7/7 canonici, 36 caratteri, 4 trattini, 0 duplicati)*.
`PersistentObject.GetPersistenID()` = `GetComponent<UniqueId>().uniqueId`, e in **tutta** l''assembly
`UniqueId.uniqueId` ha **UN SOLO SCRITTORE**: il costruttore, che scrive `Empty`.
⇒ **nessuna generazione a runtime** ⇒ **dato serializzato di scena** ⇒ **identico sulle due macchine**.

⚠️ **Ma i NOMI non sono unici**: due oggetti chiamati **esattamente** `ACT_Lantern_breakable`
hanno GUID diversi. **Il nome non e'' una chiave, il GUID si''.**

**Perche'' NON `scena~indice`**: tutti gli altri oggetti persistenti viaggiano cosi'', dove l''indice e''
una posizione nella tabella **cablata a mano** `PersistentStates` *(371 voci)*. Quella tabella contiene
**ZERO GUID di `BreakableObject`** *(misurato: 0 su 10)*, e `ProgressManager.UsePersistentObject` **esce
in silenzio** quando la ricerca fallisce ⇒ **con la tabella com''era, il mittente non avrebbe mai spedito
NULLA**.
**Completarla e'' stato scartato di proposito**: gli indici sono **POSIZIONALI** e **finiscono dentro il
salvataggio del giocatore** come `scene~index`, quindi inserire voci puo'' **spostare l''indice di tutto
cio'' che segue** e far puntare i vecchi salvataggi **all''oggetto sbagliato**.
⇒ **il GUID viaggia come `ProgressUpdate.Id`**, che e'' gia'' una stringa libera: **nessun cambio di
pacchetto, di server, di protocollo**.

### LE TRE PATCH + IL RAMO, e i due guai che evitano

1. **MITTENTE** — `Postfix` su `Damage`, con tre guard: `!___destroyed` *(Harmony legge un campo
   **privato**; ed e'' significativo solo in una Postfix)*, `CurrentlyUpdatingProgress` *(anti-eco:
   e'' `true` mentre `ProgressManager` svuota la coda, cioe'' esattamente quando una rottura arrivata
   dalla rete viene applicata qui)*, `IsConnected`.
2. **RICEVENTE live** — `Prefix` su `GetCurrentPersistentState("use")` → **`__instance.Use()`**.
   ⚠️ **E NON `SetCurrentPersistentState(data)`**, e non e'' stilistico: quello legge la rottura da
   `BreakableManager.ContainsBreakable`, e **`AddBreakable` e'' chiamato da UN SOLO posto in tutta
   l''assembly** *(`BreakableObject.Damage`)*. Con il manager vuoto concluderebbe che l''oggetto **NON
   e'' rotto** e chiamerebbe `Restore()`: **la rottura verrebbe CANCELLATA IN SILENZIO**, senza
   un''eccezione che avverta qualcuno. **Peggio di un crash.**
3. **RICEVENTE load** — `Prefix` su `SetCurrentPersistentState`, **OBBLIGATORIA**: l''originale fa
   `((BreakablePersistenceData)data).broken` **senza null-check**, e `ProgressManager.LevelLoaded`
   *(riga 140)* lo chiama con **`null`** per dire "animazione istantanea". Il gioco non passa mai null
   *(il suo loader controlla prima)*; **questa mod si''**.
4. **`PersistentObjectHelper.ApplyProgress`** — un ramo per il GUID: prima faceva `Split('~')` e
   `int.Parse(sections[1])`, quindi **un GUID nudo non sarebbe stato ignorato: avrebbe lanciato
   `IndexOutOfRange`**, e `ProgressManager.Update` **svuota la coda in un solo ciclo SENZA try/catch**
   ⇒ **avrebbe perso ogni aggiornamento accodato dietro di lui**.

**E il CREDITO al ricevente si accetta, con i numeri**: `SendBreakBreakableEvent<T>` esiste per
**`RosaryBead`, `Relic`, `Prayer`, `Sword`** e finisce in `ObjectEffect.OnBreakBreakable`, che agisce
solo se **`effectType == OnBreakBreakable`** e il cui payload **`breakableObject` e'' scritto e MAI
LETTO** in tutta l''assembly ⇒ **nessun contatore, nessuna collezione, nessuna percentuale**.
**Gravita'' BASSA.** Ed e'' **lo stesso modello del canale delle morti**, dove il ricevente esegue la
morte nativa e prende il proprio drop: e'' **voluto**, e il commento dell''autore lo dice
*("so peers always see the corpse and the drop")*.

### ⚠️ E l''argomento "il muro lo fa da anni" e'' FALSO

`BreakableWall.Damage` **non chiama `OnBreakBreakable`**: `OnBreakBreakable` ha **un solo call site**
in tutta l''assembly *(`BreakableObject.Damage`)*. Quindi `BreakableWallHelper` e'' sicuro **solo perche''
quella classe non tocca l''inventario** — **non e'' evidenza sulla sicurezza di `Use()`**, e'' un
accidente di gerarchia. **Ed e'' coerente col `// Needs to be changed` che quel file porta scritto.**

### IL TEST: passi 1-6 ✅, punto 7 ❌, e la diagnosi

**Passati a pieni voti**: stessa stanza *(il vaso sparisce sull'altro)*, inversione dei ruoli, falo''
*(i vasi tornano su chi si siede)*. ⚠️ **E l''osservazione dell''utente e'' precisa**: *"per chi non si
e'' seduto no, perche'' la logica di ripristino/respawn globale ancora non e'' stata implementata"* —
**esatto, ed e'' la voce 4.**

**Il punto 7 — "G2 entra dopo" — e'' FALLITO**, e le cause sono **due, nessuna delle quali il filtro di
scena che sospettavo**:

**(a) il ricevente.** `ApplyByGuid` cercava il `PersistentObject` fra quelli della **SCENA CORRENTE**:
se il ricevente era altrove non trovava nulla, usciva **in silenzio**, e la coda veniva svuotata.
Al rientro **nessuno ri-applicava**, perche'' `ProgressManager.LevelLoaded` ri-applica **solo** le voci
`scene~index` della tabella **e un GUID non lo e''**. ⇒ **mancava la memoria che i nemici hanno**
*(`_pendingDeaths`)*.

**(b) il server — PREESISTENTE, e piu'' insidioso.** `TeamInfo.AddTeamProgress` deduplica **per stringa
id su TUTTA la sessione del server** e accetta **solo se `progressValue > currentValue`**. Tutti gli
oggetti persistenti mandano **`value = 0`**, quindi `0 > 0` e'' **falso** ⇒ un id gia'' visto viene
**sempre rifiutato** e `Server.cs:591` **esce senza inoltrare**.
⇒ **Ogni oggetto poteva essere annunciato UNA SOLA VOLTA per sessione del server.**
**Non e'' un difetto dei vasi: vale per muri e leve compresi.**

**E la prova nei log, riga per riga**: `0d384626` rotta da G1 in `D17Z01S03` → il server **rimanda** la
entry a G2 → G2 la **riceve e la ricorda** *(`[BreakSync] rottura rimandata`)* → **entra in `D17Z01S03`
e la trova IN PIEDI** → **la rompe di nuovo lui**, annuncio che il server **scarta come duplicato**.

### F4b.1 — il fix, e il suo fallimento

Il mittente manda **`scena|guid`** *(la scena DEVE viaggiare: il ricevente puo'' essere altrove, e un
GUID non dice a quale stanza appartiene)*, piu'' una **memoria** `BreakSyncMemory`
*(`Remember`/`Apply`/`Forget`)* e **due hook**:
- **`PersistentManager.LoadSnapShot`** (postfix) → `ReapplyCurrentScene()`. E'' il primo istante in cui
  valgono due condizioni: gli oggetti sono nella scena *(il metodo lavora su `FindObjectsOfType`,
  verificato in IL)* **E** il gioco ha finito di scriverci sopra il suo snapshot.
  ⚠️ **`Multiplayer.OnLevelLoaded` gira PRIMA che la scena esista** — il progetto l''aveva gia'' imparato
  con i nemici — e un replay li'' verrebbe **anche sovrascritto dal save**.
- **`BreakableObject.Restore`** (postfix) → `Forget`. E'' il punto in cui il gioco riporta in vita
  l''oggetto: **al falo''** *(via `OnUsePrieDieu`)* e **quando lo snapshot lo dice integro**.
  **Per oggetto, non per scena**: e'' cosi'' che lo fa il gioco, e **non serve nessun annuncio globale
  di respawn**.

⚠️ **E il risultato: la MEMORIA ha funzionato, l''HOOK no.** La entry e'' arrivata ed e'' stata
ricordata; l''hook **non l''ha dipinta**, e `[BreakSync] rottura applicata` **non esiste in nessun log**.
**Dentro l''hook non c''era una riga di log**, quindi **il perche'' era ignoto** — errore mio:
**dovevo strumentarlo prima di fidarmi dell''IL, come ho fatto con F4a.**

**Strumentazione deployata** *(tag `strumentazione-breaksync`)*: una riga dentro l''hook
*(`scena=… oggetti=N daApplicare=M pending=P applied=A`)* che distingue le tre possibilita''
*("girato troppo presto" / "girato ma non trovato" / "applicato e poi ripristinato")*, **e una SECONDA
possibilita'' un frame dopo** *(`Multiplayer.OnLateUpdate`, armato da `OnLevelLoaded`)*, **additiva**:
il vecchio hook resta, e **il log dice quale dei due dipinge**.
⚠️ **E la prima versione di quella patch era sbagliata in due modi**, entrambi scoperti compilando:
usava `StartCoroutine` su un tipo **non `MonoBehaviour`**, e la firma di `OnLevelLoaded` *(due stringhe)*
non corrispondeva.

### PARTE 2 — L''ARBITRAGGIO: il token risolveva un problema che non esisteva

**L''utente ha chiesto**: *"il fatto del giocatore che deve essere host e ammaestrare tutto serve
effettivamente? nonostante la logica del token che abbiamo implementato abbastanza tempo fa?"*

**E la risposta, verificata nella storia git, e'' NO — non nella forma attuale.**

**La frase che ha motivato il token e'' FALSA.** `5ecbc27` dice:
> *"entrambe le macchine calcolavano la proprieta'' per conto proprio dalla distanza misurata localmente.
> **Le due misure non possono coincidere** - i due giocatori hanno posizioni leggermente diverse, e
> sulle slope peggio."*

**Ma due commit dopo, `bf2f568`**:
> *"**La regola di prossimita'' era giusta: il dato che le passavo era sbagliato.**"*
> `HOST: posMia=(-704.4,9.0) posAltro=(-704.4,9.0) <- IDENTICHE`

**`GetPosition()` sull''"altro giocatore" restituiva la posizione LOCALE**: la regola confrontava il
nemico con **lo stesso giocatore due volte**, quindi **ogni macchina si credeva la piu'' vicina**.
⇒ **"Le misure non possono coincidere" e'' stato generalizzato DA UN BUG a un limite
dell''architettura** — ed e'' **su quella generalizzazione** che e'' stato costruito il token.

**E con i dati corretti le due macchine CONCORDANO** — e non e'' teoria, e'' un log *(`e2a8ab4`)*:
```
HOST:   dist_io=26.2  dist_altro=22.3  owner=altro
CLIENT: dist_io=22.3  dist_altro=26.2  owner=io
```
**Distanze speculari, decisione identica.** ⇒ **il disaccordo nasceva dall''errore del dato, non dalla
regola.**

**E le altre due prove:**
- **La doppia simulazione era GIA'' risolta, PRIMA del token, con una riga che non richiede l''host**:
  `68a4054` *("Solo il proprietario trasmette")* — **e'' il gate che c''e'' ancora oggi**.
- **Il progetto HA GIA'' FATTO la decisione locale simmetrica, e l''utente l''aveva verificata in gioco**:
  il tag **`stato-sync-movimento-ok`** → commit `0c2e199`, con `DecideOwnership(enemy, previous, IsHost)`
  chiamata **su ENTRAMBE le macchine**. **E l''isteresi era GIA'' li''**: `OWNER_DISTANCE_MARGIN = 2.5`
  + `OWNER_HOLD_SECONDS = 0.5`.

### ⚠️ E "stanze diverse" e'' STRUTTURALE, piu'' di quanto sembrasse

**Non e'' solo che l''host non vede la stanza: il server NON GLI CONSEGNEREBBE il token comunque.**
```
Server.cs:816     if (playerIp != _hostIp) return;        // solo l'host puo' emettere
Server.cs:819     relayToScene(...)                        // e filtrato per SCENA
PlayerInfo.cs:30  return sceneName == player.sceneName;
```
**E anche la posizione del giocatore viaggia per scena**: in stanze diverse `_remotePositions`
**non si aggiorna affatto** — non e'' "stantia per poco", e'' **ferma dall''ultima volta che eravate
insieme**.

**La catena, riga per riga:**
```
_ids = la scena LOCALE (RegisterEnemies -> FindObjectsOfType<EnemySpawnPoint>)
  -> DecideOwnership ha UN SOLO chiamante (2557), dentro `if (IsHost)` (2555)
  -> il peer resta OwnedByLocal = false (il default di EnemyState, riga 80)
  -> 2584 PauseEnemy + FreezePhysics (controller.enabled = false)
  -> NESSUNA via d''uscita: ResumeEnemy sul peer e'' raggiungibile solo da
     ReceiveEnemyOwnership, dal ramo else di ReceiveEnemyUpdate, e da SetHost ONLINE
```
**⇒ "l''host combatte i suoi nemici; il peer guarda i propri nemici immobili."**
**Ed e'' GIA'' misurato**: `WheelCarrier` fermo **134 campioni ≈ 67 secondi**.

**E NON e'' un caso limite: e'' la normalita'' del co-op.** Nei test precedenti non era mai emerso
**perche'' i due giocatori entravano sempre INSIEME nella stanza** — e l''utente l''ha detto lui stesso:
*"non escludo che questo problema ci fosse anche prima, anche perche'' tutti i test che ho effettuato
tutti i player entravano nello stesso momento nella stanza"*.

### IL TRADE-OFF VERO, che non e'' "host si'' o no"

| | Token | Locale simmetrico |
|---|---|---|
| **Simulatori** | **uno SEMPRE** *(garantito per costruzione)* | **uno TRANNE una finestra** |
| **Fuori dalla scena condivisa** | **cieco, e il server glielo impone** | **mai cieco** |

⭐ **E l''argomento che decide**: **la finestra esiste GIA'' OGGI.** I log documentano **5 tick
`owner=nuovo` su entrambe le macchine prima del primo token**. ⇒ **il token non elimina la finestra:
la SPOSTA.** **Nella stessa stanza la scelta e'' sottile; fuori dalla stanza non c''e'' scelta: il token
non arriva, e il gioco si blocca.**

### PASSO 1 — FATTO E DEPLOYATO *(tag `passo1-peer-simula`)*

**Il peer simula quando `!IsHost && !previous.OwnershipKnown`**, cioe'' **quando l''host non ha MAI
detto niente su quel nemico**. Il flag **esiste gia''** ed e'' gia'' scritto nei posti giusti
*(`SetHost`, e sul token in arrivo)*; finora era letto **una volta sola**, per il gate di pubblicazione.
**Prende il posto che merita: distinguere "l''host ha deciso che non e'' mio" da "l''host non ha ancora
parlato".**
**Ed e'' sicuro** perche'' la proprieta'' e'' **esclusiva per costruzione**: appena l''host vede il nemico
pubblica il token, il blocco sopra lo aggancia, e da li'' in poi **il ruolo lo decide l''host** — con
`OWNER_HOLD_SECONDS` ad assorbire il passaggio.

### PASSO 2 — DA FARE *(voce 32)*

**Togliere `if (!localIsHost) return false;`** e tornare a chiamare `DecideOwnership` **su entrambe**:
**e'' `stato-sync-movimento-ok` + il fix del dato di `bf2f568` + il gate di freschezza di `d66cf70` —
tutti GIA'' a HEAD.**
⚠️ **Il rischio va DECISO, non scoperto**: **quando l''host ENTRA nella stanza del peer, il peer
possiede gia'' quei nemici e l''host puo'' riprenderseli** ⇒ `PauseEnemy` + trascinamento
dell''interpolazione → **scatto visibile**. Mitigato da `OWNER_HOLD_SECONDS` *(il possesso del peer
e'' fresco per costruzione)*, **ma serve un test a due macchine in stanze diverse.**

**E un regalo**: **`NearestRemoteDistance` esiste gia'' ed e'' codice morto** *(il suo chiamante e''
sparito)* — **ed e'' esattamente il predicato "sono solo nella stanza".**

### PARTE 3 — SCOPERTE COLLATERALI DI QUESTA SESSIONE

- **`PROTOCOL_VERSION = 11` NON sta in `Common.dll`**: e'' una costante **duplicata** in
  `Client\Network\NetworkManager.cs` e `Server\Server.cs`, confrontata **solo all''handshake**
  *(`Common.dll` contiene solo `Protocol.VERSION = 0`, mai confrontato, e l''enum `NetworkType`)*.
  **Una premessa sbagliata che ho ripetuto tre volte prima che tre Esploratori la correggessero.**
- **`output_log.txt` NON e'' affidabile con due sessioni sulla stessa macchina**: contiene **DUE processi
  intrecciati** *(`Initialize engine version` alle righe 1 e 19; le due notifiche di join a 6 righe di
  distanza)*. **I marker sono la somma esatta dei due log per-processo.** **Non usarlo per misure.**
- **`BreakableObject.Use()` non e'' idempotente al 100%**: invoca `OnDead` **FUORI** dal guard
  `destroyed` dentro `Damage`. Il consumatore e'' `DestructibleDead` *(un''azione PlayMaker che per
  giunta ha un bug: `OnExit` fa `Delegate.Combine` invece di `Remove`)*. **L''utente ha notato proprio
  questo**: *"vedo che le animazioni partono quando gli oggetti vengono distrutti"*.
- **`NearestRemoteDistance` (2071-2082) e'' codice morto**, e **`RemoteEverClaimed` e'' scritto e mai
  letto** *(lo conferma anche `d66cf70`)*.
- **I 4 tipi di `SendBreakBreakableEvent`**: **`RosaryBead`, `Relic`, `Prayer`, `Sword`** — e gli array
  passati sono quelli degli oggetti **EQUIPAGGIATI** del **Penitente locale**.
- **Le fiale**: per chi si siede, **refill e respawn sono LA STESSA AZIONE**
  *(`PrieDieu.OnUse` → `ShallowActivationLogic` → `Life` + `Flask` + `Fervour` `SetToCurrentMax` +
  `SaveGame(true)`)*. ⚠️ **MAI eseguire `ShallowActivationLogic`/`ShallowUse` sul ricevente**:
  setterebbe **`SpawnManager.ActivePrieDieu` al falo'' del MITTENTE** *(alla morte il peer finirebbe
  nella stanza di G1!)* **e forzerebbe un `SaveGame` sul peer.**


## [2026-10-10 11:15] IL MISTERO DEI SALVATAGGI: non erano corrotti, erano da un''altra parte

### 🔴 LA SCOPERTA, e vale piu'' di qualunque fix

L''indagine era partita da un sospetto mio, sbagliato: **"F4 puo'' aver rotto il salvataggio"**. L''ho
scritto e l''ho fatto indagare. **Era falso, e le prove sono cinque.** Ma l''indagine ha trovato la
causa vera, che **non e'' della mod**.

**Il percorso dei salvataggi del gioco** *[verificato in IL]*:
```
GetPathAppSettings =
   persistentDataPath + "/Savegames/" + (SteamManager.Initialized
                                          ? SteamUser.GetSteamID().ToString()
                                          : "")                              + "/"
```
⇒ **Se Steam NON e'' inizializzato, il segmento dello SteamID e'' VUOTO** e **gioco e mod scrivono in
`Savegames\`** invece che in `Savegames\<steamid>\`. I log lo mostrano col **doppio slash**:
`…/Savegames//savegame_0.save`.

**E con Steam Cloud ABILITATO le due cartelle divergono:**
- sessioni **con** Steam → il gioco scrive in `Savegames\<steamid>\`, **e il Cloud sincronizza QUELLA**;
- sessioni **senza** Steam → il gioco scrive in `Savegames\`, **che il Cloud non guarda**;
- ⇒ **al riavvio con Steam, il Cloud riporta la sua versione** e la partita **sembra persa o corrotta**.

**⇒ Spiega TUTTO quello che l''utente ha osservato:**
| Sintomo | Spiegazione |
|---|---|
| *"salvataggi corrotti"*, due volte | i file sono in **un percorso o nell''altro** |
| *"non riesco a replicarlo"* | **dipende da se Steam e'' avviato** |
| *"ho riavviato e funzionano ancora"* | Steam e'' partito, il percorso e'' tornato canonico |
| *"su macchine diverse"* | **su una Steam si inizializza, sull''altra no** |

**E l''utente ha confermato la condizione**: *"avevo avviato il gioco senza steam attivo... mi sono
accorto che il gioco si avviava senza steam inizializzato, e sospetto che sia successo anche l''altra
volta"*. **E Steam Cloud e'' attivo su entrambe le macchine.** **Le due cose insieme sono la causa.**

### ⚠️ E LA STRUTTURA "ANOMALA" ERA NORMALE

Avevo allarmato l''utente con *"questo puo'' aver rotto il salvataggio"* guardando **cartelle vuote**
*(`savegame_0\`, `savegame_0_backup\`, e il contenitore `76561199216304572\` svuotato)*.
**Sbagliato**: **le crea IL GIOCO, normalmente** *[verificato in IL]*:
```
PersistentManager.CreateAndGetSaveGameInternalDir(slot) = Directory.CreateDirectory(
      GetPathAppSettings("savegame_" + slot) + "/")
   chiamata da: SaveGame, ResetCurrent, OnLevelLoaded, OnBeforeLevelLoad, LoadGameWithOutRespawn
savegame_0_backup/  da BackupSlot -> FileTools.DirectoryCopy -> Directory.CreateDirectory
```
**E convivono coi file anche su un''installazione SANA** — *verificato sull''installazione viva: nella
stessa cartella ci sono `savegame_0\` (vuota), `savegame_0_backup\` (vuota), `76561199216304572\`
(vuota), `savegame_0.save` (78008 B), `savegame_0.save_backup` (77916 B), `savegame_0_modded.save`.*
**Quindi quelle cartelle non erano il sintomo di niente.**

### ✅ E F4 E'' ESCLUSO, con cinque prove

1. **F4 non tocca il percorso di scrittura**: `git diff` **vuoto** su `Multiplayer.cs`,
   `ProgressManager.cs`, `PersistentStates.cs`, `SlotSaveData.cs`, `GlobalSaveData.cs`, `FileHandler.cs`.
2. **La mod scrive UN SOLO file**, `savegame_{slot}_modded.save`, con `File.WriteAllText` *(ModdingAPI)*,
   **postfix di `PersistentManager.SaveGame_Internal`**.
3. **Il gioco NON LEGGE MAI quel file**: `GetSaveGameFile` = `savegame_{slot}.save`;
   `ExistSlot`/`ExistBackupSlot` = `File.Exists(.save/.save_backup)`; e **in tutto l''assembly NON esiste
   nessuna `Directory.GetFiles/GetDirectories`** sulla cartella dei salvataggi.
4. **`Use()` e `GetCurrentPersistentState("use")` NON toccano il disco** *[IL: base `ldnull; ret`;
   per il breakable solo `broken = destroyed`]*.
5. **Nei cinque log**: `IndexOutOfRange` **0** · `ArgumentOutOfRange` **0** · `Failed to save` **0** ·
   `SAVEERROR 1/2/3` **0** · `SaveGame error` **0** · `LoadGame parsing error` **0**. E la sonda riporta
   **`exceptions=0` in ogni riga** delle sessioni lunghe.

**E il server**: **ZERO I/O su disco** *(nessun match per `File.`, `Directory.`, `Path.Combine`,
`StreamWriter`, `savegame` in Server e Common)*. **Nessuna cartella `Savegames` di sua proprieta''.**

**E il gioco non puo'' corrompersi da solo in questo modo**: `SaveSnapShot` ha le guardie
`InsideLoadLevelProcess/InsideSaveProcess/InsideLoadProcess` → **`*** SAVEERROR 1/2/3` e return SENZA
scrivere**; le eccezioni sono **catturate per oggetto**; `TrySerialize` fallito ⇒ **nessuna scrittura**.
**Caso peggiore: salvataggio NON fatto, in silenzio.** L''unica fragilita'' e'' `FileTools.SaveSecure`,
che e'' una **coroutine senza file temporaneo** ⇒ un crash a meta'' scrittura puo'' troncare il `.save`.

**E i due log "Salvataggio corrotto"**: **S1** ha **905 NRE ma NON della mod** *(tempesta in
`CameraManager.get_PanningRequired` / `Tools.UI.InputIcon` / `ControlRemapManager`)* e **`LOADGAME` = 0**:
**la partita non e'' mai stata caricata**. **S2** e'' pulita: tre `LOADGAME` e per **tutti e tre** manca
**solo il file della mod** *("Could not find file `Savegames\savegame_N_modded.save`")* mentre **il
salvataggio nativo si carica senza errori** ⇒ **il file cercato era nell''ALTRO percorso.**

### ✅ E LA MISURA, ora, e'' PERMANENTE

Una riga all''avvio, **una volta per sessione**:
```
[Path] Steam inizializzato: <bool> · cartella salvataggi in uso: <percorso>
```
e, **se Steam manca**, un **avviso esplicito** che dice cosa sta succedendo e cosa fare.
**Costa un minuto ed e'' la cosa che avrebbe risparmiato questa intera indagine** — *e se il difetto
si ripresenta, lo dice in un secondo.*

### RACCOMANDAZIONE OPERATIVA — da ripetere SEMPRE

1. ⭐ **Avviare SEMPRE Steam PRIMA del gioco** *(e'' la radice: un solo percorso, niente da sincronizzare)*;
2. **Oppure disattivare Steam Cloud per Blasphemous**;
3. **E backup a mano della cartella `Savegames` prima delle sessioni di test** *(5 secondi)*.

---

## [2026-10-10 11:15] VOCE 33 RISOLTA, e il PASSO 1 confermato in gioco

### VOCE 33 — la causa era MIA, in `Forget`

**Il sintomo**: l''hook `LoadSnapShot` **girava**, il contatore diceva **che c''era qualcosa da
applicare**, e **non dipingeva nulla**.

**La causa, ricostruita in IL** *[verificato]*:
```
LoadSnapShot chiama SetCurrentPersistentState su ogni oggetto di cui ha dati
  -> per un BreakableObject IsBroken() e'' FALSO (BreakableManager NON viene ripopolato al load)
  -> il gioco chiama Restore()
  -> il MIO postfix su Restore -> BreakSyncMemory.Forget(guid)
  -> Forget rimuoveva il guid da `pending` E da `applied`
E l''hook e'' un POSTFIX di LoadSnapShot, quindi gira DOPO
  => quando ReapplyCurrentScene guardava, il pending era GIA'' STATO CANCELLATO
```

**⇒ Era il mio `Forget`, non l''hook.** **Fix**: un flag **`loadingSnapshot`**, alzato da un **Prefix**
su `LoadSnapShot` e abbassato dal postfix **dopo** la ri-applicazione. **`Forget` esce subito se il flag
e'' alzato** ⇒ l''entry resta, e **decide la ri-applicazione**. Se l''oggetto e'' davvero tornato integro
e **non e'' nella stanza che si sta caricando**, la ri-applicazione **non lo trova e lo lascia stare** —
che e'' il risultato giusto.

⚠️ **E il caso centrale NON era mai stato esercitato**: nei test le 3 entry rimandate restavano
**`pending=3` fino alla fine**, **perche'' la stanza non e'' piu'' stata rivisitata**.
⇒ **il test da fare e'': rimandare una rottura e POI RIVISITARE QUELLA STANZA.**

**E una conferma dal test lungo**: l''hook **gira e dipinge** *(T1 57 righe, T2 56, OUT 113;
`rottura applicata` 5 / 4 / 9)*. **Quindi il meccanismo funziona: si rompeva solo sul caso "pending"**,
che e'' esattamente quello che il fix ripara.

### ⚠️ E DUE DIFETTI TROVATI PER STRADA *(non ancora fixati)*

1. **`ProgressManager.Update` e'' senza `try/catch` E senza `finally`**: **una sola eccezione lascia
   `CurrentlyUpdatingProgress = true` PER SEMPRE** ⇒ **il guard del mittente F4 sopprimerebbe OGNI
   futura rottura**, e la coda non si svuota *(stessa entry rilancia ogni frame)*.
   Trigger plausibile: `ApplyProgress` fa `AddInteractedObject` **prima** di validare, e
   `getObjectPersistentId` controlla solo `Count > idx` ⇒ **un id `scena~-1` da un peer fa `lista[-1]`**
   → `ArgumentOutOfRangeException`. **0 occorrenze nei log.**
2. **`AddInteractedObject` = `List.Add` senza dedup** ⇒ il file della mod ha **doppioni**
   *(osservato: 8 voci, 4 distinte)*. Innocuo, ma e'' sporcizia.
3. E **`ReapplyCurrentScene` mette in `toApply` TUTTI gli `applied` senza filtro di scena**: il
   contatore `daApplicare` **mente** *(osservato: `scena=MainMenu ogg=0 daApp=1`)*.

### ✅ PASSO 1 CONFERMATO IN GIOCO, dall''utente

> *"Abbiamo risolto il fatto che se l''host non c''e'' i nemici funzionano per i non host, appena l''host
> si collega alla stanza i nemici vengono posizionati nei punti giusti, **ho verificato anche lo scambio
> di target e funziona, quindi questo punto e'' pulitissimo**"*

**⇒ il caso "stanze diverse", che era la NORMALITA'' del co-op e non era mai stato testato, e'' risolto.**
**E il rischio che avevo dichiarato per il passo 1** *(lo "scatto" quando l''host entra nella stanza)*
**non si e'' manifestato**: *"i nemici vengono posizionati nei punti giusti"*.

### 🟡 VOCE 35 — NUOVA, segnalata dall''utente

> *"Se uno muore nella bossroom il boss spegne l''IA. Se ci sono altri giocatori nella bossroom, il boss
> deve cambiare il target con uno dei giocatori presenti nella bossroom ancora vivo."*

**Comportamento voluto**: **il boss non deve spegnersi quando un giocatore muore**, ma **cambiare
bersaglio con un giocatore ANCORA VIVO nella stanza**.
**Tocca**: la **voce 9** *(`Respawn`/morte del giocatore in bossroom)* **e la voce 8** *(cambio bersaglio
dei boss)*. ⚠️ **E la morte di un giocatore NON e'' oggi comunicata ai peer**, quindi **probabilmente
serve un pacchetto "player death"** — e **`NetworkType` NON ha un tipo player-death** ⇒ **richiederebbe
un bump di protocollo.** **Da analizzare, non da ipotizzare.**


## [2026-10-10 17:30] F4b.2 — IL POST-FALO'': la dedup del server, e perche'' i nemici non ne soffrivano

### IL DIFETTO, come lo ha descritto l''utente

> *"Appena mi siedo su un falo'' con entrambi, tutti i breakable che sono stati rotti ed erano
> sincronizzati tra i due vengono ripristinati come previsto (**ottimo**), ma **non sono piu''
> sincronizzati tra i due**, quindi anche se G1 rompe una lanterna davanti a G2, essa non viene rotta."*
> *"Questo **NON appare per quanto riguarda i NEMICI**, ho anche testato in queste sessioni passate."*

⚠️ **E quella seconda frase e'' stata l''indizio decisivo**: senza di essa avrei cercato nel posto
sbagliato. **I due canali si comportano diversamente sul server, e la differenza E'' la dedup.**

### 🔴 LA CAUSA, confermata nei log e in IL

**I vasi viaggiavano sul canale `Progress`** *(`ProgressType.PersistentObject`)*.
**Il server li passa da `TeamInfo.AddTeamProgress`** *[IL: `ContainsKey`→`Add`; poi `get_Item` →
`IL_0030 cgt` **unsigned** → `set_Item`, altrimenti `return false`]*: **tiene un dizionario di id per
TUTTA la sessione e inoltra un id solo se il suo valore e'' MAGGIORE di quello che ha gia''.**
**Tutti gli oggetti persistenti mandano `value = 0`** *(`BreakableObjectHelper`, `new ProgressUpdate(id,
ProgressType.PersistentObject, 0)`; `ProgressUpdate.Value` e'' un **`byte``**)*.
⇒ **`0 > 0` e'' falso** ⇒ **`Server.cs:591` esce senza inoltrare.**

```
Il PRIMO annuncio passa. Il falo'' ripristina i vasi.
Il giocatore rirompe lo STESSO vaso, con lo STESSO id `scena|guid`...
   ... e il server lo scarta IN SILENZIO.
```

**LA PROVA NEI LOG** *[il log del server non era fra gli allegati: la prova sta nei log dei client,
con orologio condiviso — entrambe le macchine partono nello stesso secondo, e c''e'' un offset costante
di 20 tick calibrato in entrambi i versi]*:
```
G1 rompe 0d384626 a G1-tick 60   -> G2 la riceve a G2-tick 80      (id NUOVO per il server: accettato)
falo'' simultaneo, stesso PrieDieu 3c6d3c9c in D01Z01S07
G2 rirompe 0d384626 in D17Z01S03 a G2-tick 133  =  G1-tick 113
   -> G1 NON lo riceve: nessuna riga "Received new game progress" dopo G1:401
   -> G1 era CONNESSO: crea oggetti per G2 fino a tick 121, fine sessione tick 127
```
**⇒ il server lo conosceva gia'', e dopo il falo'' lo scarta.** **E il verso opposto lo conferma**:
G2 rompe `9edafe04` a tick 108 *(senza `rottura applicata` a seguire ⇒ rottura vera)* → **accettata**,
G1 la riceve. **Dopo il falo'' → scartata.**

**E 8 conferme in piu''**, tutte con G1 connesso: `Broke breakable` seguito **subito** da
`rottura applicata` per lo stesso guid = ri-applicazione di `ReapplyCurrentScene`
*(nel percorso della coda `CurrentlyUpdatingProgress=true` sopprimerebbe quella riga, quindi
quell''ordine puo'' venire **solo** da li'')* — **e quel percorso non aveva il guard, quindi ri-annunciava**.
**G2 6 volte, G1 2 volte: tutte scartate.**

### ✅ PERCHE'' I NEMICI NON NE SOFFRONO — la chiave

```
I NEMICI viaggiano su EnemyDeathGlobal (NetworkType 24)
receiveEnemyDeathGlobal (Server.cs:798-810) e'' SOLO:
      foreach (string ip in connectedPlayers.Keys)
          if (playerIp != ip) Send(ip, data, NetworkType.EnemyDeathGlobal);
   -> NESSUN dizionario, NESSUNA dedup, NESSUN filtro di scena
E AddTeamProgress ha UN SOLO CHIAMANTE in tutto il repo: Server.cs:591.
```
⚠️ **E non c''e'' prova da log di questo** *(in quella sessione `Post-spawn: 0 enemies`,
`EnemyDeathGlobal` = 0 occorrenze, e `receiveEnemyDeathGlobal` **non logga nulla**)*:
**e'' verificato in codice + IL, non riprodotto in gioco.**

### ✅ IL FIX, in tre parti *(F4b.2, tag `fix-f4b2-canale-nemici`)*

**1. IL MITTENTE cambia canale**: `SendEnemyDeathGlobal("brk|" + scena + "|" + guid)` invece di
`SendProgress`. **Solo client: nessun `NetworkType` nuovo, nessun bump di protocollo, nessuna
`Common.dll` da ricopiare** — il 24 esiste gia'' su entrambe.
**Ed e'' la convenzione che il progetto gia'' usa**: `sp|` per i punti di spawn dei nemici.

⚠️ **CORREZIONE a una mia affermazione**: avevo citato un precedente **`rest|`**.
**NON ESISTE in nessun file del repo** *(verificato con grep)*. **Il precedente vero e'' `sp|`**:
`EnemySyncManager.cs:2160-2171` — *"Announced as spawn-point ids and on the `EnemyDeathGlobal` channel,
NOT as progress entries"* — **con la motivazione MISURATA**: *un record di progresso con id custom
creo'' flag di gioco, fini'' nel salvataggio, e il server lo ri-eco'' a ogni joiner*.
**E `:3389`: `if (… || !enemyId.StartsWith("sp|")) return;`** ⇒ **convenzione di prefisso su stringa
libera, id non riconosciuti ignorati.** **Avevo citato un prefisso inventato, e la lezione e'' che il
precedente va letto, non ricordato.**

**2. IL RICEVENTE intercetta `brk|`** in `ReceiveEnemyDeathGlobal` **PRIMA di `_pendingDeaths.Add`**.
**Trattare un break come un id di nemico metterebbe un GUID nella memoria delle morti**, dove
`MarkSpawnPointConsumed` lo ignora e **il ciclo dei nemici lo cercherebbe per sempre**.
Il ramo chiama **`ApplyOrRemember`**: **applica se la stanza e'' caricata, ricorda se non lo e''**.

**3. 🔴 L''ECHO E'' SOPPRESSO — e senza questo il fix avrebbe introdotto un desync NUOVO.**
```
ReapplyCurrentScene chiama GetCurrentPersistentState("use") FUORI da ProgressManager.Update
   => CurrentlyUpdatingProgress e'' FALSE mentre lavora
   => il Postfix di Damage RISPE DISCE l'annuncio
Era invisibile finche'' il canale Progress scartava i ripetuti.
Sul canale che NON scarta niente, quell''echo diventa un relay VERO
   => potrebbe far RIVIVERE sul peer una rottura che il suo falo'' aveva gia'' disfatto.
```
**Aggiunto il flag `IsReapplying`** *(`try/finally`)* **e il guard nel mittente.**
**Piu'' un guard in testa a `ReapplyCurrentScene`**: un `LoadSnapShot` annidato farebbe azzerare il
flag dal `finally` interno **mentre il passo esterno sta ancora girando**.

**⚠️ E questo era il TERZO difetto della stessa famiglia, non documentato prima**: i due trovati per
strada erano *(`ProgressManager.Update` senza `try/catch`)* e *(contatore `daApplicare` non filtrato
per scena)*. **Questo e'' emerso solo perche'' il fix cambiava canale — lezione: cambiare un canale
obbliga a ricontrollare TUTTI i guard che proteggevano il canale vecchio.**

**VERIFICATO NELL''IL DEL BINARIO DEPLOYATO**: il mittente chiama `SendEnemyDeathGlobal` e **NON**
`SendProgress`; i due guard presenti; il ricevente chiama `TryParseBreakId` e `ApplyOrRemember`;
`ReapplyCurrentScene` legge il campo `reapplying` in testa *(`IL_0001 ldsfld` → `brfalse` al corpo →
`br` a `ret`)*, 2 store, handler di eccezione per il `try/finally`; la stringa `brk|` presente.

### 📌 LO STATO DEI DISTRUTTIBILI

**La voce 33 e'' CHIUSA** *(il cambio stanza funziona, confermato dall''utente: dopo il falo''
`applied=0 pending=0` su entrambe)*. **E la voce 34 e'' risolta per i vasi.**
**⇒ I distruttibili sono completi, in attesa della verifica del post-falo''.**
⚠️ **Ma la voce 34 resta STRUTTURALE per ogni altro oggetto sul canale Progress** *(muri, leve)*.

### ⚠️ E UNA NOTA DI METODO, per il futuro

**Tutti i tick di questa analisi sono il battito del probe** *(≈1 s, finestra di 60 frame)* **e le
conclusioni dipendono dall''offset costante di 20 tick fra le due macchine**, calibrato in entrambi i
versi. **Non sono secondi assoluti.** **E il log del SERVER non era fra gli allegati**: quando si
indaga una dedup, **il log del server e'' la prova primaria** e va chiesto subito.


## [2026-10-10 18:45] F4b.2 VERIFICATO, e l''eco infinita: la lezione piu'' importante di questa sessione

### ✅ LA PROVA CHE F4b.2 FUNZIONA

**G2 (ricevente), falo'' a L596**: il GUID `0d384626-0c8f-4749-9254-8ed0d2a80f32`, **gia'' applicato
PRIMA del falo''** *(`L341`-`L462`)*, **viene RIAPPLICATO DOPO** *(`L664`, poi 666, 668…)*.
**E la memoria era VUOTA in quel momento** *(`applied=0`, hook a `L655`)*.
⇒ **E'' arrivato PER VIA DI RETE, non dalla memoria.** **Prima del fix era impossibile**: il server
lo avrebbe scartato. **Su G1 lo stesso**: falo'' `L468` -> riapplicata da `L539`.

**Il percorso "rimandata" funziona**: G2 `L465` e `L493` *(appartiene a `D17Z01S03`)* **poi applicate
all''ingresso della stanza** *(`L521`-`523`, `L538`-`539`)*.

**Conteggi**: `[BreakSync]` G1 231 = 218 applicata + 13 hook · G2 241 = 222 applicata + 17 hook +
2 rimandate. `Broke breakable` 221/218.

⚠️ **E la lezione sui falo'': `applied` NON si svuota "al falo'' in se''** — si svuota **quando il gioco
`Restore`-a davvero l''oggetto** *(`Forget` -> `applied.Remove`)*. **Prova**: G1 `L454 applied=1` ->
(falo'' `L468`) -> `L519 applied=0`; **MA** falo'' `L925` -> `L987 applied=2` *(i vasi erano in
un''altra stanza)*. **E `applied=0 pending=0` appare sugli INGRESSI in stanza, non come effetto del
falo''.** ⇒ **il falo'' sincronizza i vasi solo per chi c''e'', ed e'' coerente con la voce 4.**

### 🔴 E LA REGRESSIONE CHE F4b.2 AVEVA INTRODOTTO — l''eco infinita

**Conteggi incrociati, e sono una prova geometrica**:
```
APPLY(G1) = SEND(G2)     e     APPLY(G2) = SEND(G1) - 2
   => ogni annuncio ricevuto veniva riapplicato E RE-ANNUNCIATO, un round-trip per iterazione
blocco 1: G1 SEND 56 / APPLY 54 · G2 SEND 54 / APPLY 54
blocco 2 (post-falo''): G1 SEND 165 / APPLY 164 · G2 SEND 164 / APPLY 163
   => 187 + 34 SEND su DUE soli vasi
```

**Il meccanismo** *[IL su `Assembly-CSharp.dll`]*:
```
BreakableObject::Damage
   IL_0012  ldfld destroyed
   IL_0017  brfalse IL_001D
   IL_001C  ret          <- esce subito se GIA'' rotto, LASCIANDO destroyed = true
E il postfix del mittente:  if (!___destroyed) return;
   => su un oggetto gia'' rotto ANNUNCIAVA, pur non essendo successo nulla
E il percorso nuovo (ReceiveEnemyDeathGlobal -> ApplyOrRemember -> Apply) gira FUORI da
ProgressManager.Update e NON alzava `reapplying`: i guard non lo coprivano.
```

### ⚠️ PERCHE'' NON SI VEDEVA PRIMA, e LA LEZIONE

> **Il canale `Progress` deduplicava, quindi l''eco veniva inghiottita dall''eco stessa.**
> **Cambiando canale il dedup non c''e'' piu'', e l''eco e'' diventata visibile come un relay vero.**

**LA LEZIONE, e vale per tutto il progetto:**
> **Cambiare canale obbliga a ricontrollare TUTTI i guard che proteggevano il canale vecchio.**
> **Un guard che sembra ridondante puo'' essere l''unico che teneva su la baracca.**
> **E il dedup non era solo un difetto: stava anche NASCONDENDO questo.**

**Ed e'' la seconda volta in questa sessione che un mio fix ha creato un problema nuovo** *(la prima
e'' stata l''hook che non dipingeva)*. **Il rimedio e'' quello che abbiamo usato: far verificare i log
da un agente indipendente PRIMA di dichiarare chiuso qualcosa.** **L''Esploratore aveva segnalato
esattamente questo rischio, e io l''avevo coperto solo a meta''.**

### ✅ IL FIX *(F4b.3, tag `fix-f4b3-anti-eco`)*

**1. ALLA FONTE**: una **Prefix** su `Damage` cattura **se l''oggetto era GIA'' rotto PRIMA della
chiamata**. **E'' l''unico momento in cui quella domanda ha ancora una risposta**: dopo, il campo e''
`true` in entrambi i casi *(rottura vera / chiamata su oggetto gia'' rotto)* **e i due sono
indistinguibili**. La Postfix esce subito se era gia'' rotta.
**La mappa e'' ripulita dalla Prefix di `LoadSnapShot`**: un livello nuovo sostituisce ogni oggetto
che potrebbe contenere.

**2. IL PERCORSO NUOVO alza `reapplying`**: `ApplyOrRemember` lo **salva e lo ripristina** *(non lo
azzera)*, perche'' un''applicazione **puo'' annidarsi** e azzerarlo in un frame interno aprirebbe una
finestra in cui una rottura viene annunciata indietro.

**Verificato in IL**: ordine delle guardie
`TryGetValue → CurrentlyUpdatingProgress → IsReapplying → IsConnected → SendEnemyDeathGlobal`.

⚠️ **E una nota**: `[EnemyTick]`/`[EnemyTree]` sono **0 in tutti e quattro i log** e
`Post-spawn: 0 enemies` sempre ⇒ **il percorso dei nemici NON e'' stato esercitato in questo test.**
**Il ramo `brk` ritorna PRIMA di `_pendingDeaths`/`MarkSpawnPointConsumed`**, quindi **nessuna
regressione osservata** — ma **va riverificato in una stanza con nemici.**

### 📁 E IL LOG DEL SERVER NON ESISTEVA SU DISCO *(voce 38)*

`Logger.Print` era **solo `Console.WriteLine`**, e nella cartella del server **non c''era nessun file
di log** ⇒ **il resoconto del server moriva con la finestra.**
**E'' costato due indagini di fila.** Ora il server scrive **`ServerOutput.log`** accanto al binario,
e **tutte le 33 chiamate passano dal `Logger`**, quindi lo specchio le prende tutte — incluse
`Server.cs:625` *"Received new pers. object from X: Y"* e `Server.cs:593` *"Received duplicated or
inferior progress from X: Y"*, **le due righe che avrebbero provato la dedup in un colpo solo.**

### 📋 LA RISPOSTA DEFINITIVA SUI LOG *(domanda dell''utente)*

```
G1:  LogOutput 987 righe  vs  output_log 1878 righe
     righe della mod: 616 = 616   ·   [BreakSync] 231 = 231   ·   [Probe] 79 = 79
G2:  LogOutput 1107       vs  output_log 2167
     righe della mod: 672 = 672   ·   [BreakSync] 241 = 241
```
**Motivo in configurazione**: `BepInEx.cfg` ha `[Logging.Console] Enabled = true` e `LogLevels = All`
⇒ **l''output_log è un SOVRAINSIEME** *(multiset identico delle righe della mod, anche i `Warning`,
piu'' le righe di gioco)*. **Il `LogOutput` non aggiunge nulla.**
⚠️ **Motivo per cui BepInEx cattura anche il gioco**: `WriteUnityLog = true` ⇒ **274/320 righe Unity
con prefisso `[Info : Unity Log]`**, tutte presenti nell''`output_log`.

| Situazione | Cosa chiedere |
|---|---|
| **Problemi della MOD** *(nemici, vasi, sync)* | **`output_log.txt` per macchina** — basta da solo |
| **Problemi del GIOCO** *(salvataggi, scene, crash)* | **`output_log.txt`** — **unico** con `Initialize engine version`, `Unloading`, `Total:`, `(Filename:)`, eccezioni motore |
| **Problemi di SERVER / dedup / inoltro** | ⭐ **`ServerOutput.log` dell''HOST** *(adesso esiste!)* **+ gli `output_log` dei client** |
| **ENTRAMBI** | per escludere cause di gioco su un sospetto errore mod; **e se un domani si testasse con due sessioni sulla STESSA macchina** *(li'' l''`output_log` conterrebbe due processi intrecciati)* |

⚠️ **E il controllo che serve per la dedup**: `duplicated or inferior` **è una riga del SERVER** ⇒
**nei log dei client vale "0 per costruzione", e non e'' una prova.** **Va letto in `ServerOutput.log`.**

### ✅ E NESSUN''ALTRA REGRESSIONE

`[Error` **0** in tutti e quattro i file · `NullReference` **0** · `IndexOutOfRange` **0** ·
`IMPRONTA DIVERSA` **0** · `exceptions=` **0** in tutte le righe `[Probe]`.
**Solo**: 2× `[Warning:Modding API] Failed to load missing assembly: Rewired_Windows_Lib.resources` e
32× `[Warning:Multiplayer] Couldn''t find connected player: *` **in G2 durante la connessione** *(prima
di ogni rottura: benigno)*. **E nessun intreccio di processi**: `Initialize engine version` = **1 per
file**, banner BepInEx = 1, e i nomi interni dei giocatori coincidono coi nomi dei file.


## [2026-10-10 19:40] F4b.4 — LA DOPPIA ANIMAZIONE, e la fine della strumentazione

### IL DIFETTO, parole dell''utente

> *"G1 e G2 vedono 2 vasi rotti in una stanza -> si siedono al falo'' -> G2 entra nella stanza dei
> vasi e ne rompe solo 1 -> **G1 entra e vede l''animazione di rottura di ENTRAMBI i vasi**, quindi
> sono certo che li abbia respawnati, ma **sia rimasto con qualche eco**."*
> *"Non e'' gamebreaking."*

### ✅ PRIMA: LA MIA IPOTESI ERA SBAGLIATA, e va scritto

**Avevo ipotizzato** che il fix della voce 33 *(il guard `loadingSnapshot` in `Forget`)* avesse reso
`applied` persistente attraverso il falo''. **FALSO, e la prova e'' netta** *[IL riprodotto]*:
```
IL_0010  ldsfld   applied
IL_0016  callvirt HashSet.Remove     <- applied.Remove(guid)   [riga 534]
IL_001C  ldsfld   loadingSnapshot    <- il guard si legge SOLO ORA [riga 546]
IL_0027  br IL_00F4                  <- return
```
**`applied.Remove` PRECEDE il guard**: **il guard salta SOLO la parte `pending`.**
**E il falo'' non passa da `LoadSnapShot`**: `BreakableObject::OnUsePrieDieu() { call Restore(); ret; }`
— **una sola istruzione** *[IL]*, quindi `loadingSnapshot` e'' **falso** al falo''.
**E nei log, dopo ogni falo'' il primo hook riporta `applied=0`.** **`Forget` svuota davvero.**

### 🔴 LA CAUSA VERA: due passate di ri-applicazione

**I log lo mostrano riga per riga** *(G1)*:
```
L1093  hook  scena=D17Z01S03  daApplicare=2  pending=2  applied=0
L1094  rottura applicata: 9edafe04-8dd2-4d37-bd7e-4694b922f9af
L1095  rottura applicata: 0d384626-0c8f-4749-9254-8ed0d2a80f32
L1098  [Modding API] Loaded level D17Z01S03
L1115  hook  scena=D17Z01S03  daApplicare=2  pending=0  applied=2
L1116  rottura applicata: 9edafe04-...
L1117  rottura applicata: 0d384626-...
```
⇒ **4 `Use()` per 2 vasi in UN SOLO ingresso: 2 animazioni per vaso. La seconda e'' quella di troppo.**

⚠️ **E la prima riga NON e'' la mia "seconda possibilita''"**: **le due righe hook compaiono anche per
`MainMenu` e per stanze senza nulla da applicare**, quindi sono **due invocazioni di
`LoadSnapShot`** — la prima **PRIMA** di `Loaded level`, la seconda **dopo**.

⚠️ **NOTA DI ONESTA''**: **la "seconda possibilita''" (`MultiplayerLateUpdate_Patch`) era MIA
strumentazione di ieri**, aggiunta per misurare *"quale dei due dipinge"*. **Il log ha detto che
dipingono ENTRAMBI.** **Non l''ho rimossa**: con la guardia qui sotto **e'' innocua**, e **rimuoverla
sarebbe stato un secondo azzardo nello stesso momento.** *(`CountPersistentObjects`, `CountFor` e la
riga `hook LoadSnapShot` sono invece state rimosse: avevano finito il loro lavoro.)*

### ✅ IL FIX: una guardia in un punto solo

`BreakableObjectReceive_Patch.Prefix` — **l''UNICO posto che chiama `Use()`** — ora riceve
**`bool ___destroyed`** *(Harmony inietta il campo privato: i campi del gioco sono privati e non c''e''
un accessor)* **e, se l''oggetto e'' GIA'' rotto, ritorna `false` senza applicare.**
- **Non tocca la rete**, `applied`, `pending` ne'' `Forget`.
- **Un oggetto che va davvero rotto ha `destroyed == false`** e passa **esattamente come prima**.
- **`___destroyed` e'' la stessa fonte che il mittente gia'' usa**, ed e'' collaudata nel progetto.
- **Quando l''oggetto e'' gia'' rotto, `Use()` costruisce un `Hit` e `Damage` esce subito**
  *(`ldfld destroyed / brfalse / ret`, verificato in IL)*: **era lavoro che poteva produrre solo una
  seconda animazione.**

### ⚠️ E UNA LIMITAZIONE NOTA, che resta *(voce 39)*

**`pending` NON viene ripulito dal falo''.** Il guard `loadingSnapshot` in `Forget` salta **solo** la
parte `pending`, **e il falo'' non passa da `LoadSnapShot`** ⇒ **`applied` si svuota e `pending` no.**
**E'' il PROGETTO della voce 33** *(`pending` e'' cio'' che fa comparire il vaso rotto a chi entra
dopo)*, **ma diventa un''eco** quando il vaso **era gia'' rotto in questa vita prima del falo''**.
**Toccarlo e'' rischioso**: e'' **lo stesso `Forget`** che ha causato *"l''hook non dipinge"*.
**Da valutare SOLO se il sintomo si ripresenta dopo F4b.4.**

### ✅ E L''ECO E'' MORTA — misurato

```
Broke breakable:  3 in G1, 3 in G2          (prima: ~90 PER VASO, 187 SEND su due vasi)
[BreakSync]:      G1=38 (5 applicata + 2 rimandate + 31 hook) · G2=48 (8 + 1 + 39)
ServerOutput.log: brk| = 0 righe            => i vasi NON passano piu'' dal canale Progress
```

### 📁 E IL PRIMO `ServerOutput.log` — ha ripagato subito

**Sessione 18:32:59 -> 18:38:31, 174 righe.** **`duplicated or inferior` = 126** *(76 da G1, 50 da G2)*
— ⚠️ **e NESSUNA riguarda un breakable**: sono id di **formato tabella** *(`D17Z01S05~0` 25,
`D17Z01S03~1` 25, `D17Z01S03~0` 25, `D01Z01S07~0` 23, **tutti Type 10`**)*, piu'' **mappa** *(Type 8, 23)*,
**flag** *(`D17Z01_BOSSDEAD`, `DEOSGRACIAS_CUTSCENE_PLAYED`)*, **item** *(`QI31`)*, **`TELEPORT_*`**,
**Tears**, e progressi 96-128.
✅ **`brk|` = 0 righe** ⇒ **confermato: i vasi non passano piu'' dal canale Progress, e non c''e''
nessuna eco lato server.**
✅ `Received new pers. object` = **4**, **tutte da G1** e **tutte formato tabella**: e'' il canale
Progress, **non i vasi.**
⇒ **I 126 sono RUMORE PREESISTENTE del canale Progress** *(celle mappa, flag, item, lacrime)*,
**NON una regressione dei vasi.** **E finalmente lo vediamo invece di dedurlo.**
✅ Connessioni: G1 18:34:04 *(host)* -> G2 18:34:11; G1 disconnesso 18:38:24 *(host -> G2)*; G2 fine
18:38:31. **Nessun errore del server.**

### ✅ E NESSUNA REGRESSIONE

`NullReference` **0** · `IndexOutOfRange` **0** · `exceptions=0` in tutte le righe `[Probe]` ·
`l'LoadSnapShot, error` **0** · `SAVEERROR` **0** · `Persisten Error` **0**.
**I nemici funzionano**: `[EnemyTick]` **478 (G1) / 564 (G2)**, `Post-spawn` 30/38 — **e l''utente lo
conferma**: *"il test funziona anche per i NEMICI, non è stato rotto"*.
⚠️ `IMPRONTA DIVERSA` **1 per log** *(G1 L1979, su `D01Z01S01`)*: **pre-esistente, riguarda la lista
nemici, non i vasi** *(voce 14, gia'' chiusa come falso allarme: la segnalazione e'' una-tantum)*.

### 📚 E LA LIBRERIA ESTERNA: valutata e SCARTATA

L''utente ha segnalato **`Blasphemous.NewbieEltonLibs`** *("Library of useful APIs for modding
Blasphemous 1")*. **Valutata leggendo il README via API GitHub.** **E'' una class library, non una mod**,
target **.NET Framework 3.5**, con **quattro dipendenze di compilazione** *(ModdingAPI 3.0.1 che
abbiamo, GameLibs 4.0.67, CheatConsole 1.1.0, Framework.Levels 0.1.4 che NON abbiamo)*.
**Cosa offre**: resource storage *(`SpriteStorage`, `AnimationStorage`, `ModAnimator`, `ItemCollection<T>`)*,
comandi CheatConsole, flag di mod, serializzazione, estensioni ModdingAPI/GameLibs, utility.
**⇒ Nessuna di quelle aree e'' cio'' che serve al progetto**, che e'' **sincronizzazione di stato di gioco
fra processi**. **Le uniche cose vicine sarebbero `EntityOrientationExtensions` e
`EnemyHealthBarExtensions`** — **ma l''orientamento e'' gia'' risolto da noi** *(voce 2, con la nostra
isteresi tarata sui nostri numeri)*, **e la barra dei boss arrivera'' con le voci 8/18**.
**DECISIONE**: **NON si integra** *(tre dipendenze nuove a progetto stabile, con benefici zero)*,
**e NON si aggiunge nemmeno come riferimento "esplorativo"** — *una dipendenza inattiva ha un modo di
diventare attiva quando qualcuno la trova comoda*. **Resta come SPUNTO di lettura**, e
**`EnemyHealthBarExtensions` e'' l''unica cosa da guardare quando arriveremo ai boss.**
**Nota dichiarata dal README stesso**: il percorso AssetBundle e'' **deferito** *(issue #60)* e
`ModAnimator` ha un *"known last-frame skip behavior"*.


## [2026-10-10 20:20] VERIFICA FINALE DI STATO — e due correzioni mie

### ⚠️ CORREZIONE 1: la voce 39 NON era "risolta", era "fixata ma non dimostrata"

**Avevo scritto `✅ RISOLTA (F4b.4)`. E'' troppo, e lo correggo.** **La guardia c''e''** *[IL del
binario testato, `BlasClient.dll` sha `901a3a0f…`: `ldarg.2` → `brfalse` → `return false`, un solo
`Use()`]*, **ma NON c''e'' prova che sia mai stata colpita.**

**E il log NON PUO'' provarlo**, per un motivo strutturale: **`[BreakSync] rottura applicata` e''
stampato DOPO la chiamata**, e **`ApplyToLoadedObject` ritorna `true` anche quando la guardia
sopprime** ⇒ **restano 2 righe per GUID per ingresso** *(G1 ingresso#1 in `D17Z01S03`: L738 + L758)*,
**e non si distingue "due applicazioni" da "una applicazione + una soppressa".**

**E il meccanismo che aggira la guardia ESISTE, ed e'' nel GIOCO** *[IL di `Assembly-CSharp.dll`]*:
```
BreakableObject::SetCurrentPersistentState scrive `destroyed` dallo SNAPSHOT
    (IL_0009 ldfld broken / IL_000E stfld destroyed)
poi IsBroken() (= BreakableManager.ContainsBreakable, NON ripopolato al load) e'' FALSO
    -> IL_0040 call Restore()
E Restore() rimette destroyed = false.
E LoadSnapShot chiama SetCurrentPersistentState DUE volte per caricamento.
```
⇒ **la premessa del fix** *("la seconda passata trova gli oggetti gia'' rotti")* **vale solo se fra
le due passate NESSUNO riporta `destroyed = false`.** **L''utente conferma che il sintomo e'' sparito**
*(“tutto funziona alla grande”)*, **ma serve una misura**: **una riga di log quando la guardia
sopprime.** **Fino ad allora la voce resta 🟡.**

### ⚠️ CORREZIONE 2: nel §12 precedente ho citato righe di un''ALTRA sessione

**La entry del `2026-10-10 19:40` cita, come se fossero di quella verifica**, le righe
`hook scena=D17Z01S03 daApplicare=2 pending=2 applied=0` a `G1 L1093/L1094/L1095` e
`L1115/L1116/L1117`, piu'' i conteggi `[BreakSync] 38/48`, `Broke breakable 3/3`, `duplicati 126`.
**Sono della sessione PRECEDENTE** *(quella con la strumentazione `daApplicare`/`hook`)*.
**I tre log dell''ultima verifica sono un''ALTRA sessione** *(`ServerOutput.log` 18:59:38 → 19:03:30,
178 righe, 130 duplicati)*, **e non contengono ne'' `daApplicare` ne'' `hook`** *(0 occorrenze)* —
**perche'' la strumentazione e'' stata rimossa in F4b.4.**
**⇒ il confronto prima/dopo su quelle righe NON e'' riproducibile con gli allegati attuali.**
**Confusione mia nel documentare.** *(Nota per il futuro: le righe citate vanno sempre legate al file
e alla sessione a cui appartengono.)*

### ✅ E CIO'' CHE INVECE E'' CONFERMATO

- ✅ **F4b.4 e'' DEPLOYATO**: la guardia c''e'' nell''IL del binario testato.
- ✅ **L''eco e'' MORTA**: `Broke breakable` **G1=1, G2=2**, **un GUID distinto ciascuno** = il numero
  reale di rotture a mano. **Prima erano ~90 per vaso.**
- ✅ **`hook LoadSnapShot` = 0** su entrambi: **la strumentazione e'' stata rimossa davvero.**
- ✅ **Server**: `duplicated or inferior` **130** ma **tutti id di formato tabella/mappa/flag/item**;
  **`brk|` = 0**; `Received new pers. object` **4** *(tutte tabelle)*; **0 errori.**
- ✅ **Marcatori**: `NullReference` 0 · `IndexOutOfRange` 0 · `IMPRONTA DIVERSA` 0 · `SAVEERROR` 0.
  **Le 44/48 righe "Exception" sono TUTTE il campo `exceptions=0` della sonda.**
- ✅ **`[Path]`**: **Steam inizializzato su ENTRAMBE** — G1 `…/76561198161597473/`,
  G2 `…/76561199216304572/`. **Percorso canonico. La voce 36 funziona.**
- ⚠️ **E i NEMICI non sono stati esercitati in QUESTI log**: `Post-spawn: 0 enemies / 0 spawn points /
  0 ids` in **tutte** le 24 occorrenze, `[EnemyTick]`/`[EnemyTree]` = 0. **Quindi da qui non si
  confermano** — *l''utente li aveva confermati nella sessione precedente.*

### 🔴 E IL DIFETTO PIU'' PERICOLOSO DEL PROGETTO *(nuova voce 41)*

**`ProgressManager.Update` non ha `try/catch` NE'' `finally`** *[verificato nel codice]*:
```
CurrentlyUpdatingProgress = true;
... i 14 helper di ApplyProgress ...
CurrentlyUpdatingProgress = false;
```
**Fuori c''e'' un `try/finally` in `Multiplayer.cs:154`, ma resetta `PerformanceProbe`, NON questo
flag.** ⇒ **una sola eccezione in un helper lascia il flag `true` PER SEMPRE**, e si porta dietro:
- **`BreakableObjectHelper` sopprime OGNI futura rottura**;
- **~29 guard negli altri helper sopprimono l''invio di TUTTA la progressione**
  *(`BeadHelper`, `PrayerHelper`, `RelicHelper`, `QuestItemHelper`, `CollectibleHelper`, `HeartHelper`…)*;
- **la coda non si svuota e rilancia ogni frame.**

**⇒ il gioco resta vivo ma il co-op smette di sincronizzare**, **senza un crash e senza un errore
evidente.** **Probabilita'': BASSA** *(0 occorrenze nei log)* **ma non nulla**, **e i percorsi che la
possono causare sono verificati**: `ApplyProgress` fa **`int.Parse(sections[1])`** e
**`getObjectPersistentId`**, **entrambi senza `try/catch`** — **e la difesa attuale e'' SOLO il
parsing** *(`IsTableId` / `char.IsDigit`)*, **non un catch.**
⚠️ **E il progetto lo sapeva**: il commento in `BreakSyncMemory.IsTableId` dice **esplicitamente** che
un `IndexOutOfRange` li'' dentro **sarebbe fatale** *"perche'' `ProgressManager.Update` svuota la coda
in un solo ciclo senza `try/catch`"* — **e quella consapevolezza non e'' mai diventata un fix.**

### 📊 I NUMERI DELLO STATO *(per la domanda strategica)*

```
248 attributi [HarmonyPatch] su 37 file      (150 in AnimationFixes.cs da solo)
19 NetworkType distinti                       (nessun tipo player-death)
~29 punti che leggono CurrentlyUpdatingProgress
PROTOCOL_VERSION = 11 duplicata              (NetworkManager.cs:631, Server.cs:854)
```

**⇒ NON e'' uno stato da lavoro grande.** **Dopo cinque fix di fila, di cui almeno due hanno
introdotto un problema nuovo** *(F4b.2 → eco infinita; F4b.1 → hook che non dipinge)*, **il progetto ha
248 patch e un percorso di stato la cui unica uscita e'' una riga senza `finally`.**
**Raccomandazione**: **una consolidazione breve** *(il `try/finally`, la dedup di
`AddInteractedObject`, e la misura della guardia F4b.4)* **PRIMA del falo''.**

### 📋 E IL FALO'', dimensionato sul codice

**3 pezzi, VERIFICATI NEL CODICE**:
1. **broadcast del respawn** — `EnemyRespawnPatch` → `OnEnemiesRespawned()` fa **SOLO**
   `_pendingDeaths.Clear()` + `_announcedDeaths.Clear()` + log: **nessuna riga di rete**. Idem
   `EnemySpawnResetPatch` → `OnDeathStateReset()`. **E `PrieDieuHelper` ha 3 patch e nessuna comunica
   il respawn.**
2. **azzeramento della memoria su chi NON si siede.**
3. **reset di `pending`** *(la limitazione nota della voce 39)*.

**File**: `EnemySyncManager.cs`, `EnemySpawnPatch.cs`, forse `BreakableObjectHelper.cs`.
⭐ **Bump di protocollo NON necessario**: `EnemyDeathGlobal` **non ha dizionario**
*(`Server.cs:808`: `Send(ip, data, EnemyDeathGlobal)` a tutti tranne il mittente)* ⇒ **un prefisso
nuovo (es. `falo|`) viaggia gratis.** **E `PROTOCOL_VERSION` e'' duplicata** *(due costanti, confrontate
solo all''handshake)*, mentre `Protocol.VERSION` di `Common.dll` e'' **0 e non usata**.
⚠️ **Il costo vero NON e'' il codice: e'' il TEST.** La voce 31 dice che **il ramo falo'' non e'' mai
stato esercitato in nessun log**, **e lo confermo: 0 occorrenze.**

⭐ **E una precisazione importante sul falo''**: **l''allineamento dei distruttibili per chi si siede
e'' LOCALE**, non una sincronizzazione — *`BreakableObject.Start` sottoscrive `Logic.OnUsePrieDieu`
(IL: `Delegate::Combine` + `stfld`), e `OnUsePrieDieu()` e'' `Restore(); ret`, che gira su OGNI
macchina che si siede.* **Funziona perche'' entrambi si siedono.** **Il falo'' vero deve PROPAGARE il
respawn.**

### ⚠️ E IL RESPIRO DEL PROGETTO: il boss richiede un bump

**`NetworkType` NON ha un tipo player-death** *(19 membri: Position 0 … HostAssign 13, 21-25)*.
**La morte del giocatore non e'' comunicata** *(nessun `PlayerDeath`/broadcast; solo
`PlayerDeathAnimationBehaviour` in `AnimationFixes.cs`, che e'' animazione locale)*.
⇒ **per la voce 35 serve un pacchetto nuovo**: **bump di `PROTOCOL_VERSION` OBBLIGATORIO** *(entrambe
le costanti + la riga di handshake)*, **piu'' ricopiare `Common.dll` su entrambe nella versione giusta
per ciascuna** *(voce 15: `Modding\data` e'' net35, `SERVER-COOP` e'' net8.0 ⇒ hash diversi, e
`verify-deploy.ps1` lo controlla)*. **E la voce 9 (spettatore) richiede 3 pezzi interamente da
scrivere**: **nessun hook `DeadScreenWidget` esiste oggi nel repo** *(0 occorrenze)*.


## [2026-10-10 21:00] CONSOLIDAZIONE — il flag, la dedup, e la misura della guardia

**Tre interventi a rischio basso o nullo, fatti ASSIEME prima di costruire il falo''.** *(Il quarto,
la voce 40, e'' rimasto di proposito: con la guardia e'' innocuo, e rimuoverlo sarebbe stato un
secondo azzardo nello stesso momento.)*

### 🔴 1. IL FLAG DEVE SCENDERE, QUALUNQUE COSA ACCADA *(voce 41)*

```csharp
// com''era
CurrentlyUpdatingProgress = true;
... i 14 helper di ApplyProgress ...
CurrentlyUpdatingProgress = false;
```
**Nessun `try`, nessun `finally`. E il `try/finally` che c''e'' fuori** *(`Multiplayer.cs:154`)*
**resetta `PerformanceProbe`, NON questo flag.**

**Una sola eccezione in un helper lasciava il flag `true` PER SEMPRE**, e il danno sarebbe stato
**silenzioso e totale**:
- `ApplyProgress` **non avrebbe piu'' preso nulla dalla coda**;
- **OGNI guard `if (!CurrentlyUpdatingProgress) Send…` avrebbe soppresso l''annuncio** *(~29 punti:
  `BeadHelper`, `PrayerHelper`, `RelicHelper`, `QuestItemHelper`, `CollectibleHelper`, `HeartHelper`…)*;
- **e le rotture dei vasi non sarebbero piu'' partite.**
⇒ **il gioco vivo, il co-op che smette di sincronizzare. Nessun crash, nessun errore visibile.**

⚠️ **IL PROGETTO LO SAPEVA E NON L''AVEVA MAI FIXATO**: il commento in `BreakSyncMemory.IsTableId`
dice **esplicitamente** che un `IndexOutOfRange` li'' dentro **sarebbe fatale** *"perche''
`ProgressManager.Update` svuota la coda in un solo ciclo senza `try/catch`"*. **Quella
consapevolezza e'' rimasta un commento.**

**E i percorsi che la possono causare sono reali, non teorici**: `ApplyProgress` fa **`int.Parse`**
sulla parte dopo `~` di un id **che ARRIVA DALLA RETE**, e una lookup che **puo'' uscire dalla
tabella**. **La difesa attuale era SOLO il parsing** *(`IsTableId` / `char.IsDigit`)*, **non un `catch`.**

**FIX** *[verificato in IL]*:
```
try { ... il ciclo ... }
finally {
    IL_0043  callvirt List`1::Clear()                            <- la coda va scartata COMUNQUE
    IL_004b  call set_CurrentlyUpdatingProgress(false)           <- il flag scende SEMPRE
}
```
**Il `Clear` sta nel `finally` di proposito**: una coda parziale va comunque scartata, **altrimenti
la stessa entry che lancia verrebbe ritentata a ogni frame**.

### ✅ 2. `ApplyProgress`: una entry guasta non deve portarsi via la coda

**Ora il flag scende comunque**, quindi questo **non e'' piu'' la differenza fra una sessione viva e
una morta** — **ma resta la differenza fra perdere UN aggiornamento e perdere TUTTI quelli accodati
dietro.** **`catch` per-elemento, CON LOG**: l''**id** e il **tipo** finiscono nel messaggio,
**perche'' e'' cio'' che rende l''errore trovabile dopo**. **Un `catch` silenzioso nasconderebbe
l''unica prova che spiega perche'' un aggiornamento e'' sparito.**
```
[Progress] apply failed for '<id>' (<tipo>): <Eccezione> - <messaggio>
```

### ✅ 3. `AddInteractedObject`: dedup *(voce 42)*

**`List.Add` senza controllo**, e **il file su disco lo mostrava: 8 voci dove 4 erano distinte.**
**Innocuo in effetto** *(quella lista finisce in `savegame_N_modded.save`, che il gioco non legge mai
— voce 36)*, **ma e'' il tipo di innocuo che smette di esserlo il giorno in cui qualcuno conta le
voci o confronta due liste per uguaglianza.**
**FIX**: `IsNullOrEmpty` → **`Contains`** → `Add` *[verificato in IL: nell''ordine]*.

### ✅ 4. E LA MISURA DELLA GUARDIA F4b.4 — per non chiudere una voce su una sensazione

**La voce 39 era stata marcata RISOLTA senza prova, e non poteva esserlo**: *"rottura applicata"*
e'' stampato **dal chiamante DOPO** che la guardia ha deciso, **e il chiamante riporta successo in
entrambi i casi**. **Quindi le due passate producevano due righe IDENTICHE sia applicando sia
rifiutando** — **ed e'' esattamente cosi'''' che un fix e'' stato chiamato "fatto" senza evidenza.**

**Ora la guardia logga quando RIFIUTA**:
```
[BreakSync] gia'' rotto, applicazione saltata: <GUID>
```
**E'' la prova, ed e'' a costo zero**: **scatta solo quando una rottura viene rifiutata**, cioe'' il
caso che vale la pena contare.
- **Se la guardia lavora**: un oggetto gia'' rotto **produce questa riga e nessuna animazione**.
- **Se non compare mai**: **la guardia non viene raggiunta**, e **la doppia animazione ha un''altra
  causa** — *e a quel punto lo sapremo invece di supporlo.*

### ✅ VERIFICATO NELL''IL DEL BINARIO DEPLOYATO

```
Update:           1 handler Finally (try@0009, handler@003c), e DENTRO il finally:
                     IL_0043 callvirt List`1::Clear()
                     IL_004b call set_CurrentlyUpdatingProgress(false)
ApplyProgress:    1 handler Catch, con ModLog.Error e l''id nel messaggio
AddInteractedObject: IsNullOrEmpty -> Contains -> Add
la guardia:       la stringa "[BreakSync] gia'' rotto, applicazione saltata: "
```
**Build: 0 errori.** **Tag: `consolidazione-flag-e-misura`.**

### ⚠️ E UNA NOTA SU UN MIO ERRORE DI VERIFICA, per onesta'' e per il futuro

**Nel primo check di queste tre modifiche ho scritto due "NO" che erano falsi**: cercavo `Clear` come
`stfld` e come stringa contenente `"Clear"`, **ma `List<T>.Clear()` e'' una `callvirt` il cui operando
e'' il metodo, non la parola**, **e il flag e'' un `stsfld`** *(campo statico)*, **non `stfld`**.
**Il codice era giusto; il controllo era sbagliato.** **⇒ quando un check dice "manca", il sospetto
deve cadere PRIMA sul check.** *E'' la quarta volta in questa sessione che un mio strumento di
verifica produce un falso negativo — le altre tre sono state `__instance.Destroyed`,
`Tools.Level.Layout` e `AllActivePlayers`.*


## [2026-10-10 22:00] LA VOCE 32 CHIUSA: "verificato nel codice" NON e'' "verificato nel comportamento"

### ✅ E L''UTENTE AVEVA RAGIONE — la prova documentale

**L''utente ha detto**: *"sono certo che lo abbiamo risolto... potresti verificare che non sto impazzendo?
Ho testato anche precedentemente qualche sessione fa"*. **E aveva ragione.** **Ecco la catena git reale**:
```
5ecbc27  7 ott 10:59   Token di proprieta'': decide solo l''host, il peer obbedisce
efcf8b7  7 ott 17:38   Token di proprieta'' + quattro correzioni (protocollo 11)   <- il gate qui
0c2e199  7 ott 18:30   ThawPhysics...
998c561  7 ott 18:40   "Arbitro e peer pari: la proprieta'' si decide sulle distanze,
                        non sull''ordine di connessione"    <- E'' IL TAG stato-sync-movimento-ok
```
⚠️ **E `998c561` NON e'' antenato di `5ecbc27`: viene DOPO.** **Quindi il progetto prima ha messo il
token, POI ha scritto "Arbitro e peer pari", e quella e'' la versione che l''utente ha verificato in
gioco.**

**E la verifica decisiva**:
```
DIFF fra 998c561 e HEAD, su DecideOwnership:
   --- NESSUNA DIFFERENZA ---
E il CHIAMANTE e'' identico:  if (IsHost) { DecideOwnership(enemy, previous, true); }
```
**⇒ NULLA E'' ANDATO PERSO.** **La "parita''" era ed e'' DENTRO la funzione**: la prima assegnazione
e'' simmetrica *(`state.OwnedByLocal = wants || (!peerCloser && localIsHost);`)*, **e il commento nel
codice lo dice esplicitamente**:
> *"It used to read `localIsHost || wants`, which handed the enemy to the arbiter whatever the
> distances were. **That is what made being the first player to connect feel like a privilege**: the
> arbiter kept every enemy it touched, and the peer could not take one even standing on top of it."*

### 🔴 E LA CORREZIONE CHE DEVO: la voce 32 NON era "un difetto da fixare"

**Avevo scritto che il "passo 2" era da fare** *(togliere `if (!localIsHost) return false;` e chiamare
`DecideOwnership` su entrambe)*. **E'' SBAGLIATO.** **L''agente l''aveva dedotto dal codice senza
guardare il comportamento completo, e io l''ho ripetuto senza verificarlo.** **Ecco perche'' e'' sbagliato**:
```
Il peer NON PUBBLICA un token: ReceiveEnemyOwnership ritorna subito `if (IsHost) return;`
Il peer APPLICA il token dell''host: state.OwnedByLocal = ownedByReceiver

=> se togliessi il gate e lasciassi decidere anche il peer:
   1. il peer deciderebbe correttamente (le distanze sono speculari)   OK
   2. ma la sua decisione verrebbe SOVRASCRITTA dal token dell''host
   3. e nel frattempo il peer deciderebbe inutilmente su nemici che l''host vede
=> il gate NON e'' il difetto: e'' cio'' che tiene il peer da parte mentre aspetta l''autorita''
   dell''host. E'' il disegno, ed e'' coerente.
```
**Il DIFETTO VERO era UNO SOLO**: **il peer si congelava quando il token non poteva arrivare** *(l''host
altrove ⇒ `_ids`, che e'' la scena locale dell''host, non contiene quei nemici ⇒ nessun token ⇒
`PauseEnemy` per sempre)*. **Ed e'' chiuso dal PASSO 1** *(`f09839d`)*, **verificato in gioco**.

### ⭐ LA LEZIONE, e vale piu'' della voce

> **"VERIFICATO NEL CODICE" NON E'' "VERIFICATO NEL COMPORTAMENTO".**
> **Una verifica strutturale** *("`DecideOwnership` ha un solo chiamante, dentro `if (IsHost)`")*
> **puo'' essere VERA e portare a una conclusione SBAGLIATA**, **se non si guarda chi PUBBLICA il
> risultato e chi lo APPLICA.** **La domanda giusta non era "chi chiama la funzione", ma "cosa
> succede alla decisione".**

⚠️ **E in questa sessione e'' la QUARTA volta che una verifica produce una conclusione sbagliata**:
1. **`__instance.Destroyed`** *(non esiste: e'' un campo privato)*
2. **`Tools.Level.Layout`** *(namespace sbagliato)*
3. **`AllActivePlayers`** *(usato in un controllo "sono da solo" dove era sbagliato)*
4. **"il passo 2 e'' da fare"** *(dedotto dalla struttura, mai verificato nel comportamento)*
**⇒ REGOLA**: **quando una diagnosi dice "manca X", va verificata la CONSEGUENZA di X, non solo la sua
presenza.** **E quando l''utente dice "sono certo che funzioni", l''utente ha visto qualcosa che il
codice non mostra.**

### 📌 E LA VOCE 43, che nasce da qui

**Il gate `if (!localIsHost)` e'' probabilmente CODICE MORTO**, **perche'' il peer decide ma la sua
decisione viene sovrascritta.** **Ma non e'' un difetto e non c''e'' fretta**: **rimuoverlo e'' una
SEMPLIFICAZIONE**, **e va fatta solo dopo aver verificato che la prima assegnazione simmetrica basti
da sola** *(il ramo dei cambi successivi serve oggi solo all''host, che lo esegue con `localIsHost = true`)*.
⚠️ **E IL TEST IN GIOCO NON PUO'' DECIDERLA**: *"il non-host si muove"* e'' **il comportamento del
PASSO 1** *(il peer simula `!OwnershipKnown`)*, **e succede sia col gate sia senza.** **Serve
un''analisi dei percorsi nel codice, non una sessione.** **Ed e'' esattamente la lezione di cui sopra,
applicata alla prossima decisione.**

### ✅ E I COMMIT CHE L''UTENTE AVEVA IN MENTE *(la sua ipotesi, verificata)*

**L''utente ha chiesto se la cosa fosse stata risolta in `0939b92` e `697133e`. Risposta: NO, ma sono
due cose DIVERSE e reali**:
```
697133e (F1A)    -> EnemySyncManager.cs  51 righe   ← la DEMOZIONE offline e il buco della promozione
0939b92 (FIX C)  -> Server.cs            16 righe   ← l''annuncio della promozione a TUTTI
f09839d (PASSO 1)-> EnemySyncManager.cs  31 righe   ← il peer simula cio'' che l''host non vede
```
**F1A e FIX C curano la MIGRAZIONE dell''host** *(il problema TEMPORANEO: l''host cambia e per un
momento nessuno arbitra)*. **La voce 32 era l''ASSENZA di proprieta'' quando l''host e'' altrove.**
**Due difetti diversi**, **e il secondo e'' chiuso dal passo 1.**

⭐ **E la sequenza temporale spiega il ricordo dell''utente**: *"si e'' presentato un problema, forse
quello dei salvataggi proprio quando lo abbiamo risolto"*. **`efcf8b7` e `5ecbc27` sono del 7 ottobre
(mattina), `998c561` del 7 ottobre 18:40, e la corruzione dei salvataggi e'' arrivata con
`mega-fix-1`/F4a nei giorni successivi.** **Non erano collegati** *(lo abbiamo dimostrato: erano le due
cartelle di Steam — voce 36)*, **ma sono capitati vicini, e in memoria si sono attaccati.** **Normale.**


## [2026-10-10 23:00] VOCE 39 DIMOSTRATA — e quattro correzioni ai miei brief

### ✅ LA GUARDIA DI F4b.4 LAVORA, ED E'' DIMOSTRATO

**La riga di misura esiste e compare 3 volte**:
```
G1 L1115   0d384626-0c8f-4749-9254-8ed0d2a80f32
G1 L1409   1ba697d8-bc73-45b9-9bdf-9bc71a08af48
G2 L1104   9edafe04-8dd2-4d37-bd7e-4694b922f9af
   => UNA per GUID per ingresso
```
**E il conteggio `[BreakSync] rottura applicata` per GUID per ingresso e'' sceso a 1.**
```
G1 0d384626:  L769 appl · L978 rimandata · L1095 appl · L1115 SALTATA · L1116 appl
G1 1ba697d8:  L1263 rimandata · L1393 appl · L1409 SALTATA · L1410 appl
G2 9edafe04:  L992 rimandata · L1084 appl · L1104 SALTATA · L1105 appl
```
**La prova piu'' pulita e'' G2, che non ha mai rotto quei vasi di sua mano**: `9edafe04` ha **2 sole
righe `applicata`**, in **due ingressi diversi** — non 2 per ingresso.
⚠️ **Nota di onesta''**: **il baseline "2 per vaso per ingresso" e'' storico** *(dichiarato nella voce 39
e nei vecchi log)*, **non rimisurabile in questi log**. **Cio'' che questi log provano e'' il valore
ATTUALE = 1.** **Ed e'' esattamente per questo che avevo aggiunto la riga di misura: `rottura applicata`
non poteva dimostrare niente, perche'' e'' stampata dal chiamante DOPO la decisione e riporta successo
in entrambi i casi.**

### ⚠️ CORREZIONE DI ORDINE: il passaggio rifiutato e'' il PRIMO, non il secondo

**Avevo detto** che il gioco rimette `destroyed = false` **"fra due applicazioni"**, e che **il secondo
passaggio** veniva rifiutato. **FALSO, e il log lo mostra**:
```
L1087  OnLevelLoaded
L1095  APPLICATA
L1098  [Modding API] Loaded level
L1115  SALTATA      <- la guardia rifiuta QUI
L1116  APPLICATA
```
⇒ **il gioco rimette `destroyed = false` fra l''applicazione del pass A e l''arrivo del pass B**,
**non fra due applicazioni.** **Il meccanismo in IL e'' quello giusto; l''ordine A/B era invertito.**
**E non e'' stato possibile determinare per IL quale dei due passaggi sia A e quale B** *(i due si
alternano nei tre casi, e l''ordine rispetto ai marcatori di frame non e'' decidibile senza
strumentare)*. **Dichiarato come non verificato.**

### ✅ E IL MECCANISMO, CONFERMATO IN IL *(con una precisazione)*

```
get_IsBroken() = Logic.BreakableManager.ContainsBreakable(GetHashId())   <- NON legge `destroyed`
=> destroyed = true  +  IsBroken() = false  =>  Restore()
   e Restore() e'' l''UNICA cosa che azzera destroyed
   (IL: ldfld destroyed / brtrue / stfld destroyed 0)
Restore() e'' chiamato da ESATTAMENTE DUE metodi in tutto l''assembly:
   BreakableObject.OnUsePrieDieu        (il falo'')
   BreakableObject.SetCurrentPersistentState
E GetCurrentPersistentState NON chiama Use(): solo CreatePersistentData() + stfld broken.
```

### ✅ E IL PASSO 1, CONFERMATO COI NUMERI

```
G2 (non host dalla L1678):  owner=io = 99 righe, TUTTE dopo L1678, ZERO prima
G1 (non host L1434-1528):   owner=io = 88 righe
D01Z01S01@SPAWN si muove:   11 posizioni distinte (G2), 4 (G1)   <- supera il criterio (>=3)
```
**Prima del fix era `owner=nuovo`/`owner=altro` per sempre.** ✅
**E il fix F4b.2 non ha rotto i nemici**: il ramo `TryParseBreakId` e'' **la PRIMA cosa dopo il
null-check e fa `return`**, quindi `_pendingDeaths.Add` e `MarkSpawnPointConsumed` **non vengono
toccati** e **il ciclo dei nemici non cerca mai un GUID**. ✅

### ✅ E LA VOCE 43: codice morto CONFERMATO

```
DecideOwnership ha UN SOLO chiamante: riga 2558, `DecideOwnership(enemy, previous, true)`,
   letterale `true`, dentro `if (IsHost)` (2556). Grep su tutto il repo: nessun altro riferimento.
`localIsHost` compare 4 volte in HEAD: la firma (2298), un commento (2309), la prima assegnazione
   (2314) e il gate (2319). NESSUN chiamante passa mai `false`.
ReceiveEnemyOwnership (2350): `if (IsHost) return;` immediato, e APPLICA il token
   (`state.OwnedByLocal = ownedByMe`).
=> il peer non raggiunge MAI DecideOwnership, il gate e'' irraggiungibile,
   e rimuoverlo NON cambia alcun percorso.
```
⚠️ **Unica via teorica per farlo tornare vivo**: togliere `if (IsHost)` dal chiamante e passare
`IsHost` come argomento. **Oggi no.** **Essendo pulizia pura, e col gate che documenta l''intenzione,
lasciarlo non fa danno** *(voce 43, 🟢 opzionale)*.

### ⚠️ E QUATTRO AFFERMAZIONI DEI MIEI BRIEF CHE ERANO SBAGLIATE

**L''Esploratore ha smentito quattro cose, e vanno registrate perche'' l''errore e'' mio:**
1. **HEAD non era `69dd6d6`**: era **`9941eb9`** *(`69dd6d6` → `93fc076` → `fc61b7b` → `9941eb9`)*.
   **Ero indietro di 3 commit.** ✅ **Ma il tag `consolidazione-flag-e-misura` punta a `93fc076`,
   antenato di HEAD ⇒ la misura era nel binario testato.**
2. **`esito=resume:StartBehaviour` NON esiste in questi log**: ci sono solo **`resume:earlyReturn`**
   e **`esito=?`**. **L''avevo indicato come atteso.**
3. 🔴 **"i nemici non sono stati esercitati: `0 enemies` in tutte le occorrenze" — FALSO.**
   **G1 L1356 = `3 enemies / 7 spawn points / 3 ids` · G1 L1420 = `4/7/4` · G2 L1687 = `5/7/5`.**
   **I nemici c''erano.** ⚠️ **Ed e'' la piu'' grave delle quattro**: **avevo preso un dato VERO della
   verifica precedente** *(dove i nemici erano davvero 0)* **e l''ho trasferito alla sessione nuova
   senza controllare.** **E'' la stessa malattia della voce 32: un dato vero in un contesto, ripetuto
   in un altro contesto dove non lo e''.**
4. **L''ordine dei passaggi** *(sopra)*.

**⇒ LA REGOLA, che si aggiunge a quella della voce 32**: **un dato misurato vale per LA SESSIONE in cui
e'' stato misurato.** **Ripeterlo in un''altra sessione e'' un''affermazione nuova, e come tale va
verificata.** **In questa sessione e'' la quinta volta che un''affermazione non verificata produce una
conclusione sbagliata.**

### ✅ E LA CONSOLIDAZIONE: nessuna regressione, ma una prova mancata

`exceptions=` **tutti 0** *(59/59 righe in G1, 47/47 in G2)* · `NullReference` 0 ·
`IndexOutOfRange` 0 · `IMPRONTA DIVERSA` 0 · `[Error` 0 · `SAVEERROR` 0.
⚠️ **Ma `[Progress] apply failed` = 0 e NON e'' una prova positiva**: **non esiste NESSUNA riga
`[Progress]` in nessuno dei due log** ⇒ **il `catch` nuovo NON ha lavorato in questa sessione.**
**Assenza di esercizio, non assenza di difetti.** **Dichiarato.**
⚠️ **`Couldn't find connected player` = 32, SOLO su G2, concentrate L663-725** — **nella finestra di
join/handoff**, cioe'' **esattamente dove FIX C doveva lavorare**. **La connessione riesce e la sessione
prosegue** *(c''e'' anche un `Failed to establish connection` seguito da successo)*, **quindi resta
benigno — ma 32 occorrenze in quella finestra meritano una nota, e non le avevo mai contate.**

### ✅ `[Path]` E SERVER

**`[Path]`**: **G1 = `lambe`** → `…/Savegames/76561198161597473/` · **G2 = `Drago`** →
`…/Savegames/76561199216304572/`. **Steam inizializzato su entrambe. Cartelle distinte e corrette.**
**La voce 36 funziona.** ✅
**Server**: `duplicated or inferior` = **134**, **NESSUNO con GUID di breakable** *(tutti id tabellari
`~0`/`~1`, Type 5/8/9/10/11/12)*. **`brk|` = 0** ✅. `Received new pers. object` = **4**.
**2 connessioni, 2 disconnessioni, 1 host reassign** *(G1 → G2)*, **0 errori**.


## [2026-10-11 00:30] LA MAPPATURA DEL FALO'' IN IL — e la trappola che avrebbe fatto esplodere il fix

### 🔴 LA SCOPERTA PIU'' IMPORTANTE: `RespawnDeadEnemies` ha TRE chiamanti, e uno NON e'' il falo''

```
1. PrieDieu/<ActivationLogic>c__Iterator1::MoveNext     @IL_0188   ✅ falo'', prima accensione
2. PrieDieu/<ReActivationLogic>c__Iterator2::MoveNext   @IL_013E   ✅ falo'', ri-seduta
3. Framework.Penitences.PenitencePE02::OnLevelLoaded    @IL_000A   ❌ NO!
```
**`PenitencePE02::Activate()` fa `LevelManager.add_OnLevelLoaded(OnLevelLoaded)`** *(e `Deactivate()`
la disiscrive)*. ⇒ **finche'' quella penitenza e'' attiva, OGNI CAMBIO STANZA chiama
`RespawnDeadEnemies`.**

🔴 **CONSEGUENZA OPERATIVA, e stavo per commetterla**: **il postfix della mod
(`EnemyRespawnPatch`, `EnemySpawnPatch.cs:54`) e'' su quel metodo** ⇒ **`OnEnemiesRespawned()` gira
GIA'' OGGI su tutti e 3 i chiamanti.** **Un broadcast messo nudo li'' dentro esploderebbe a ogni
cambio stanza in penitenza PE02.** **Percorso VIVO, non teorico.** **Questa ricerca ha appena
risparmiato un disastro.**

**`EnemySpawner::Reset` — 3 chiamanti, NESSUNO e'' il falo''**:
```
Penitent::Respawn()            @IL_000A   morte del giocatore
PersistentManager::LoadGame    @IL_001E   load di un salvataggio
LogicManager::ResetAllData()   @IL_0014   reset dal menu
```
**`Reset()` = 4 istruzioni** *(`ldfld consumedSpawns; callvirt Clear(); ret`)* e **non fa rinascere
nulla** ✅. ⚠️ **Due `Reset` omonimi**: **il falo'' chiama `BreakableManager::Reset`** *(tail di
`OnUse`)* **e MAI `EnemySpawner::Reset`** ⇒ **`EnemySpawnResetPatch` non scatta al falo''**, e non
dobbiamo temerlo.

### ✅ LA CATENA DELLA SEDUTA, IN IL

```
PrieDieu::OnUpdate  IL_0022/0059 -> Interactable::Use()
Interactable::Use   IL_0002      -> Interactable::UseCorroutine()
<UseCorroutine>c__Iterator1::MoveNext:
   IL_00E4  SendMessage("OnUsePre")
   IL_0106  callvirt Interactable::OnUse()     <- ritorna l''ITERATORE
   IL_010B  StartCoroutine(IEnumerator)        <- il corpo parte ORA
   IL_0129  SendMessage("OnUsePost")
PrieDieu/<OnUse>c__Iterator0::MoveNext:
   IL_002B  if (get_Ligthed())  -> ReActivationLogic() : ActivationLogic()
   IL_0084  LogicManager::UsePrieDieu()        <- EVENTO
   IL_0098  BreakableManager::Reset()
```
**`ReActivationLogic`** *(gia'' acceso)*: `ShallowActivationLogic()` @IL_002B → audio/sprite/flip →
`KNEE_START` → attesa → **`RespawnDeadEnemies()` @IL_013E** → *(`KneeledMenuCoroutine` e'' solo
`UIController.*`, **nessuna chiamata al mondo**)* → `KNEE_END`.
**`ActivationLogic`** *(prima accensione)*: metrica → audio → nasconde sprite/`DamageArea` → flip →
`ACTIVATION` → `WaitForSeconds` → `ligthed=true` → `LaunchEvent` → **`ShallowActivationLogic()`
@IL_015F** → `if (gotoAttrackWhenActivate) LoadAttrackScene()` **← CAMBIO LIVELLO** →
**`RespawnDeadEnemies()` @IL_0188**.

### ⭐ BENEFICI DEL GIOCATORE vs MONDO — la distinzione su cui si costruisce il fix

**SOLO GIOCATORE — `ShallowActivationLogic()`** *(privato, 23 istruzioni)*:
`SpawnManager.ActivePrieDieu = this` · `Life.SetToCurrentMax()` · **`Flask.SetToCurrentMax()`** *(le
FIALE sono QUI, quindi non propagarle e'' a costo zero)* · `if (GetPrieDieuLevel() > 1)
Fervour.SetToCurrentMax()` · **`SaveGame(true)`** *(scrive su disco)*. **ZERO effetti sul mondo.**

**MONDO — solo TRE cose**:
1. **`EnemySpawner::RespawnDeadEnemies()`** *(in 2 punti, uno per ramo)*
2. **`LogicManager::UsePrieDieu()`** = `{ if (OnUsePrieDieu != null) OnUsePrieDieu.Invoke(); ret }`
   → **unico sottoscrittore: `BreakableObject`** → `Restore()`
3. **`BreakableManager::Reset()`** = `breakablesId.Clear()`

**SOLO UI/ANIMAZIONE**: metrica, audio, `SpriteRenderer.enabled=false`, `DamageArea.enabled=false`,
`flipX`, `SetTrigger`, `LaunchEvent`, `KneeledMenuCoroutine`.

⭐ **Il punto minimo per replicare i SOLI respawn**:
```csharp
Core.Logic.EnemySpawner.RespawnDeadEnemies();   // nemici
Core.Logic.UsePrieDieu();                       // -> OnUsePrieDieu -> Restore() su tutta la scena
Core.Logic.BreakableManager.Reset();            // memoria distruttibili
```
**NO `ShallowActivationLogic()`** — **e'' privato, setta `ActivePrieDieu` al falo'' del MITTENTE, e
forza un `SaveGame(true)` sul ricevente.** **NO `ActivationLogic`/`ReActivationLogic`** *(audio,
animazioni, `WaitForSeconds`, `LaunchEvent`, menu, e `LoadAttrackScene()` = **cambio livello**)*.

### ✅ L''INVENTARIO DI COSA VIENE RESPAWNATO

| elemento | identita'' | chi lo riporta in vita | propagazione |
|---|---|---|---|
| **Nemici** | `EnemySpawnPoint`; **chiave = `transform.position`** | `RespawnDeadEnemies()` | **basta il metodo** |
| **`BreakableObject`** | GUID authored per istanza | **`Restore()`** *(`public`)* | **basta il metodo** — via `Logic.UsePrieDieu()`. ⚠️ **NON `Use()`**: `Use()`→`Damage()` **ROMPE** |
| **`breakablesId`** | — | `BreakableManager::Reset()` | locale, gratis |
| **`BreakableWall`** *(muri)* | `PersistentObject` | **il falo'' NON lo tocca** | serve stato/altro canale |
| **Porte / leve / altari / gate / item** | `PersistentObject` | **nessuno** | ✅ **niente da propagare** |
| **snapshot** | — | **il falo'' SCRIVE, non rilegge** | `LoadSnapShot` ha 3 chiamanti: **nessuno e'' il falo''** |

**Sottoscrittori di `OnUsePrieDieu`** *(ricerca sui RIFERIMENTI di campo, non sulle chiamate)*:
```
ldfld LogicManager::UsePrieDieu @0001, 000C
ldfld BreakableObject::Start    @0017     stfld BreakableObject::Start @0032  <- Delegate::Combine
ldfld BreakableObject::OnDestroy @0017    stfld BreakableObject::OnDestroy @0032
```
⇒ **`BreakableObject` e'' l''UNICO tipo che tocca quel campo** ✅.
`OnUsePrieDieu` = **`{ call Restore(); ret }`** ✅ letteralmente.
`Restore()`: **esce subito se `!destroyed`**; poi `destroyed=false`, riabilita
collider/animator/renderer, `Play("idle")`, **`BreakableManager.RemoveBreakable(GetHashId())`**.
E `AddBreakable` ← **solo** `Damage`; `RemoveBreakable` ← **solo** `Restore`; `ContainsBreakable` ←
**solo** `IsBroken`.

### ⭐ `consumedSpawns`: GLOBALE, e la chiave e'' la POSIZIONE

- E'' una **`List<Vector3>`**, con **uguaglianza ESATTA** *(`List.Contains`)*.
  `AddConsumedSpawner` *(unico chiamante: `EnemySpawnPoint::OnEntityDie`)* **appende solo se non
  presente**. ⇒ **NON esiste alcun "ordine di scena"** *(e la frase che avevo attribuito alla voce 29
  non e'' nel TODO: `grep` = 0 occorrenze — **errore mio di citazione**)*.
- ⭐ **E'' GLOBALE, non per-scena**: `EnemySpawner` nasce in `LogicManager::Initialize` e **non viene
  ricreato a ogni livello**. Il residuo e'' innocuo grazie a **`EnablePersistence`**.
- ⭐ **`CreateEnemy` rispetta GIA'' `consumedSpawns`**: `IL_000C IsSpawnerConsumed→set_Consumed`;
  `IL_0022 if(!EnablePersistence) Consumed=false`; **`IL_0034-006A` early-exit** se
  `HasEnemySpawned || selectedEnemy==null || spawnPoint==null || Consumed`. ⇒ **il falo'' "funziona"
  SOLO perche'' azzera la lista PRIMA di ricreare.**
- `OnEntityDie` @IL_0008: **`HasEnemySpawned=false`** + `AddConsumedSpawner` ⇒ il ciclo si chiude.
  `OnDestroyEntity` fa **solo** `UnregisterEnemiesEvents` *(non azzera `HasEnemySpawned`)*.

### ✅ COSA SERVE PROPAGARE DAVVERO — e i due casi si distinguono

**"Chi e'' FUORI SCENA"** — ✅ verificato: **`FindObjectsOfType<EnemySpawnPoint>()` restituisce solo
gli oggetti attivi della scena caricata** ⇒ **e'' un NO-OP sui nemici altrui.** **Serve solo azzerare
la MEMORIA** *(`consumedSpawns` e'' unica e globale ⇒ `Reset()` la svuota)*.
⚠️ **MA il ricevente fuori scena NON deve chiamare `RespawnDeadEnemies()`**: gli ricreerebbe **i morti
della stanza in cui STA**, senza che nessuno si sia seduto li''. ⇒ **DUE GESTI DISTINTI**.

**"Chi e'' NELLA STESSA stanza ma non si e'' seduto"** — ✅ **basta `RespawnDeadEnemies()`**:
*(1)* `HasEnemySpawned` ⇒ **nessun duplicato**; *(2)* **i nemici vivi NON vengono toccati** *(vita e
posizione intatte)*; *(3)* `OnEnemiesRespawn` invocato; *(4)* ⚠️ **fa scattare il postfix sul
ricevente** ⇒ svuota la memoria della mod *(desiderabile)* **ma e'' ECO INFINITA se il broadcast vive
li'' dentro** → **serve un flag con SALVA/RIPRISTINA** *(lezione della voce 37)*.

### 🔴 E UN DIFETTO LATENTE CHE ESISTE GIA'' OGGI

**`PrieDieuUse_Patch` e'' un postfix su `PrieDieu.OnUse`, ma quel metodo COSTRUISCE SOLO L''ITERATORE
E RITORNA** *(6 istruzioni: `newobj`, `stfld $this`, `ret`)*. **Il chiamante fa `callvirt
Interactable::OnUse()` @IL_0106 e `StartCoroutine` SOLO a IL_010B** ⇒ **il postfix della mod gira
PRIMA che il corpo esegua la prima istruzione**: prima di `ShallowActivationLogic`, prima di
`RespawnDeadEnemies`, prima di `Restore()`.
⇒ **E'' un buon SEGNALE DI INTENTO, non un "la seduta e'' avvenuta".** **Per il broadcast il punto
giusto e'' il postfix di `RespawnDeadEnemies`** *(che c''e'' gia'')*.

### ⚠️ E LE TRAPPOLE DEL BROADCAST — tutte verificate

1. 🔴 **Il postfix copre TUTTI e 3 i chiamanti** *(penitenza PE02 inclusa)* ⇒ **il broadcast NON puo''
   stare nudo in `OnEnemiesRespawned()`.**
2. 🔴 **ECO INFINITA** se il ricevente esegue `RespawnDeadEnemies()` ⇒ flag **salva/ripristina**.
3. ⚠️ **`EnemySpawner.Reset` vs `BreakableManager.Reset`**: **omonimi**, e il falo'' chiama il secondo.
4. ⚠️ **Fuori scena = SOLO azzeramento memoria**, mai `RespawnDeadEnemies()`.
5. ⚠️ **Le FIALE restano fuori** *(sono in `ShallowActivationLogic`, che non va eseguito)* — coerente
   con la decisione di progetto.
6. ⚠️ **Un id `falo|` non riconosciuto finirebbe in `_pendingDeaths` come se fosse un nemico.** **E
   `_pendingDeaths` e'' la memoria che fa UCCIDERE i nemici.** Verificato che oggi sarebbe innocuo
   *(`MarkSpawnPointConsumed` esce su `!StartsWith("sp|")`; il loop su `_ids` non matcha;
   `RegisterEnemy` confronta contro id `sp|` veri)*, **ma sporcherebbe i conteggi e farebbe dipendere
   il fix dal fatto che nessuno interpreti mai quella stringa.** ⇒ **il ramo va messo PRIMA della
   riga 3396, accanto al `brk|`** *(`:3374` null-check · `:3388-3394` `brk|` · `:3396`
   `_pendingDeaths.Add`)*.

### ✅ LA CHIAVE DEL BROADCAST, CONFERMATA

**`NetworkType.EnemyDeathGlobal = 24`**; **`Server.receiveEnemyDeathGlobal`** *(`Server.cs:798-810`)* =
`foreach(connectedPlayers) if (playerIp != ip) Send(...)` — **nessun dizionario, nessuna dedup,
nessun filtro di scena**; **`NetworkManager.SendEnemyDeathGlobal`** *(`:606`)* manda la stringa
**verbatim**. Prefissi in uso: **`sp|`** e **`brk|`**. ⇒ **un terzo prefisso `falo|` viaggia gratis,
senza bump di protocollo e senza ricopiare `Common.dll`.** ✅

### ✅ E LA MEMORIA DEI DISTRUTTIBILI SI SISTEMA DA SOLA

**`Logic.UsePrieDieu()` → `Restore()` → il nostro `BreakableObjectRestore_Patch` → `Forget(guid)`**
⇒ **`applied` e `pending` si svuotano per gli oggetti riportati in vita.**
⭐ **Ed e'' legato all''esecuzione LOCALE di `Restore()`** — **che era il rischio n.3 segnalato**:
*svuotare solo sul mittente farebbe divergere le macchine.* ✅ **Risolto per costruzione.**
⚠️ **Ma `applied` va trattato con lo stesso criterio di `pending`**: `ReapplyCurrentScene` ri-applica
**tutto `applied`** *(`:619-622`, **mai filtrato per scena**)* ⇒ **se il falo'' rimette in piedi i vasi
ma `applied` conserva il guid, alla prossima ri-applicazione il vaso viene RI-ROTTO** — **e la guardia
F4b.4 non basta, perche'' dopo il falo'' `destroyed` e'' `false`.**

### 📌 E LO STATO DELLE VOCI, dopo questa mappatura

**24** ✅ confermato in IL · **29** 🟡 *il testo reale e'' diverso da come l''avevo citato* · **30** 🟢
confermato *(nessuna rete)* · **31** 🟢 **ora il postfix logga SEMPRE** *(il prossimo test lo vedra'')*
· **39** ✅ · **4** 🟡 **aperta, ed e'' questa mappatura a renderla affrontabile**.


## [2026-10-11 02:00] LA LIBRERIA ESTERNA: verdetto DEFINITIVO, con le prove questa volta

**La domanda e'' tornata** perche'' il progetto ha ora **due bisogni concreti di GUI**: il **messaggio
d''ingresso alla bossfight** e le **spine della Colpa sulla barra della vita**. **La prima valutazione
era basata SOLO sul README**, quindi e'' stata rifatta **leggendo il codice e l''IL**.

### 🔴 VERDETTO: NON SERVE A NESSUNO DEI DUE BISOGNI

**Bisogno 1 — il messaggio a schermo: NON coperto.** `LocalizationHandlerExtensions.Localize` legge
`_textByLanguage` della mod via `TraverseUtils` e ritorna `"LOC_ERROR"`: e'' **traduzione, non
visualizzazione**. **Nessuno dei 44 tipi del binario disegna testo.**

**Bisogno 2 — le spine sulla vita: NON coperto, ed e'' il buco che si temeva.**
> **LA LIBRERIA NON CARICA SPRITE.** `SpriteStorage` e'' **solo un dizionario nome->`Sprite`**:
> nessun `AssetBundle`, nessuna `Texture2D`, nessun `LoadImage`, nessun `File.`.
> **E il percorso AssetBundle e'' DEFERITO** *(issue #60)*. **`AnimationImportInfo` porta un
> `filePath` che NESSUNO legge**, e la spec interna S3.4 dice che lo storage
> *"does not own FileHandler importing"*.
> ⇒ **Non trasforma un PNG in uno sprite, quindi il bisogno non e'' coperto ne'' oggi ne'' domani.**

### ⚠️ E IL COSTO DI INTEGRARLA E'' PIU'' ALTO DI QUANTO SEMBRASSE

```
1 pacchetto nuovo: Blasphemous.Framework.Levels 0.1.4
1 DLL nuova da deployare accanto a BlasClient.dll, SU DUE MACCHINE
   (e tools\deploy-local.ps1 copia SOLO BlasClient.dll: andrebbe modificato)
+ UPGRADE OBBLIGATO ModdingAPI 3.0.0 -> 3.0.1
     ⚠️ VERIFICATO con un restore di prova: FALLISCE con
        error NU1605: Detected package downgrade: Blasphemous.ModdingAPI from 3.0.1 to 3.0.0
+ un assembly TERZO che inietta patch Harmony proprie:
     HarmonyPatchInstaller fa `new Harmony(...).PatchAll(typeof(HarmonyPatchInstaller).Assembly)`
     -- in un progetto con 248 [HarmonyPatch] e la regola "un intervento alla volta"
```
**⇒ Beneficio per i due bisogni: ZERO.** **E `GameLibs` non c''entra**: e'' un pacchetto di **sola
compilazione** *(assembly del gioco publicizzate)* e **il progetto la referenzia GIA''**
*(`<PackageReference Include="Blasphemous.GameLibs" Version="4.0.67" />`, presente anche in `bin\Debug`)*.
Sull''install vivo: **zero file GameLibs, e va bene cosi''**. Licenza **MIT**: nessun ostacolo legale.

### ✅ E IL REFERTO HA TROVATO LE ALTERNATIVE VERE — migliori, e senza dipendenze

| Bisogno | Come si fa col codice che ABBIAMO GIA'' |
|---|---|
| **1 · Il messaggio** | ⭐ **Il progetto HA GIA'' un `NotificationManager`** *(`Notifications\NotificationManager.cs`: `DisplayNotification(string)`, `Update()` chiamato da `Multiplayer.cs:148`, `LevelLoaded()` che costruisce la UI **clonando l''oggetto testo di `PlayerPurgePoints`** sotto il Canvas **"Game UI"**, 4 righe, fade)*. **Oppure il gioco**: `UIController.instance` *(campo **statico pubblico**)* → **`ShowPopUp(string message, string eventSound, float timeToWait, bool blockPlayer)`**, che in IL fa **`messageText.text = message`** — **testo arbitrario**.<br>⚠️ **MA LA FRASE VERA DELLA BOSSROOM NON E'' RIUSABILE**: `ShowQuotePopup.OnEnter` e'' **l''unico chiamante** di `ShowQuoteWidget` in tutto `Assembly-CSharp`, e `QuoteWidget.Open(...)` fa **solo un fade di `CanvasGroup`**: **il testo sta nel prefab** e non si puo'' iniettare. `FullMensages` e'' un enum chiuso.<br>⭐ **Costo reale: UNA chiave in `Modding\localization\Multiplayer.txt` + una chiamata `DisplayNotification(LocalizationHandler.Localize("boss"))` al gate "tutti nella stanza".** |
| **2 · Le spine sulla vita** | ⭐ **L''arte ESISTE GIA'' a runtime**: `PlayerFervour.RefreshGuilt(bool)` legge `Fervour.MaxFactor` e fa `DOFade` su **`guiltStart`/`guiltBar`/`guiltEnd`** *(tre `Image`, campi **privati** => Traverse)*. **Si possono istanziare/re-parentare sulla barra vita.**<br>**Oppure un PNG nostro**, col percorso **che il progetto usa gia''**: `Map\MapManager.cs:117` → `cfg.Marks[...]`, cioe'' **`Sprite` da config di ModdingAPI** *(`ConfigHandler` + `SpriteImportOptions` → `FileHandler` → `Texture2D.LoadImage` + `Sprite.Create`)* — **precedente interno, funzionante e deployato**. |

### ⚠️ E DUE SCOPERTE CHE CAMBIANO IL DESIGN DELLA COLPA

```
1. IL GIOCO HA GIA'' UN SISTEMA DI COLPA: GuiltManager.
   E in TUTTO il gioco `set_MaxFactor` ha DUE SOLI chiamanti, ENTRAMBI GuiltManager
   (OnPenitentReady, UpdateGuilt), e SEMPRE su `Stats.Fervour` -- MAI su `Life`.

2. ⚠️ E ABBASSARE `Life.MaxFactor` NON MOSTREREBBE UN TRATTO BLOCCATO:
   CurrentMax = CurrentMaxWithoutFactor * MaxFactor, e `CalculateValue()` CLAMPA `Current` a `CurrentMax`
   => `PlayerHealth.BarTarget = Life.Current / Life.Final` si RISCALA: la barra risulta di nuovo PIENA.
```
⇒ **Il disegno "spine sulla vita" NON si ottiene abbassando un massimo: va DISEGNATO.** **E la buona
notizia e'' che l''arte delle spine esiste gia''** *(sono le stesse del Fervore)*, **quindi si riusa
invece di disegnarla.**
⭐ **E c''e'' gia'' un `GuiltManager`**: **se estendibile, la Colpa si aggancia a quello invece di
aggiungerne uno parallelo** — *e "Blocco del Fervore" e'' gia'' il suo mestiere.*

### 📌 E COSA RESTA DA GUARDARE IN FUTURO *(come RIFERIMENTO di lettura, mai come dipendenza)*

1. ⭐ **`EnemyHealthBarExtensions`** — `EnemyHealthBar.GetOwner()`, `BossHealth.GetTarget()`,
   `UIController.GetBossHealth()`: **3 getter via Traverse**. **Utilissima come promemoria dei nomi
   dei campi privati** per i boss *(voci 8/18)*, **e riscrivibile da noi in 6 righe.**
2. `TraverseUtils` — wrapper di `Harmony.Traverse` *(ma Harmony lo usiamo gia'')*.
3. `ModFlagsManager` — flag di mod con policy NG+ e ownership per-mod.
4. `CheatConsoleLogging` + `ModSubCommandAttribute`/`AutoModCommand` — la console la usiamo gia''.
5. `SpriteStorage`/`AnimationStorage`/`ModAnimator`/`ItemCollection<T>` — **inutili finche'' l''import
   di immagini resta deferito**; e l''import da PNG ce lo da'' gia'' ModdingAPI.
6. Marginali: `*HandlerExtensions`, `ValidationUtils`, `SystemExtensions`, `InventoryManagerExtensions`.

**⚠️ E LA LEZIONE**: **la prima valutazione era giusta, ma si basava solo sul README.** **Questa volta
si basa su codice e IL, e ha trovato la prova che mancava** *(`NU1605`, `AssemblyRef` hard, `PatchAll`
di terzi, assenza totale di import immagini)*. **⇒ Una decisione presa sulla documentazione va
riconfermata sul codice prima di considerarla definitiva — e ora lo e''.**


## [2026-10-11 03:00] LA MORTE DI UN GIOCATORE: respawna SOLO per chi muore, e le due macchine DIVERGONO

### 🔴 LA SCOPERTA, ed e'' piu'' grave di quanto pensassimo

**L''utente aveva notato**: *"quando un giocatore muore, non estende il respawn dei nemici anche agli
altri ma solo a se stesso"*. **CONFERMATO, e con le prove**:
```
G2:884  Event ENTITY_DEAD
G2:885  Game state has been set to PLAYERDEAD
G2:886  blocker DEAD_SCREEN enabled
G1:     PLAYERDEAD = 0 occorrenze            <- G1 NON SA NULLA

G2:1020 [Falo''] consumedSpawns azzerati … 2 annunciate e 0 ricevute (scena=D01Z01S01)   <- LA MORTE
G2:1021 RestoreStored -> 1023 Active priedieu S07 -> 1035 Load and activate level D01Z01S07
G2:1119 Post-spawn 5/7/5                     <- i 2 nemici RINASCONO su G2
G1:1995 Post-spawn 3/7/3                     <- su G1 restano MORTI
   e G1 tiene le 2 morti in _pendingDeaths FINO A 2014
   e l''id `D01Z01S01@SPAWN` ha 0 occorrenze su G1 fino a 2014, contro 76 righe su G2
```
**E NESSUN `annuncio inviato` alla morte**: *in codice, `Penitent::Respawn → EnemySpawner::Reset` non
ha percorso di rete, e l''hook `OnDeathStateReset` **logga soltanto**.*

### ⚠️ MA LA CONVERGENZA E'' AVVENUTA **PER CASO**

```
G1 ha ricevuto un annuncio di FALO'' (righe 2008/2013) -- e quel falo'' era per un''ALTRA stanza!
=> e solo ALLORA ha dimenticato le 2 morti e ha visto il nemico tornare vivo (2019)
```
🔴 **⇒ Il problema NON e'' "il respawn e'' ingiusto per gli altri": e'' un BUG DI DIVERGENZA.**
**Senza quel falo'', le due macchine resterebbero divergenti: G1 ri-ucciderebbe nemici che G2 vede
vivi.** **⇒ Il lavoro sulle morti e'' un fix di CORRETTEZZA, non una rifinitura.** E ora e'' chiaro
perche'' **il pacchetto "player death" serve davvero** *(anche per la voce 35: il boss deve cambiare
target)*.

### 📌 IL LAVORO, come richiesto dall''utente *(voce 44)*

```
SOPPRIMERE il respawn dei nemici alla morte, SE connesso a un server con altre persone.
   check: sono connesso E ci sono peer?  si'' -> sopprimo.  no -> lascio fare al gioco.
Motivazione di design (utente): in un Metroidvania, se A e'' a meta'' di una sezione platform
   punitiva e B muore all''inizio della mappa, far respawnare tutto in faccia ad A e'' ingiusto
   e spezza il ritmo. (Coerente con Elden Ring Seamless.)
```
**MECCANISMO PROPOSTO**: `consumedSpawns` e'' **GLOBALE** e la sequenza e''
`Reset()` *(svuota)* → level load → `SpawnEnemiesOnLoad` → `CreateEnemy` *(early-exit se `Consumed`)*
⇒ **per tenere i nemici morti, `CreateEnemy` deve trovarli CONSUMATI**: **intercettare il `Reset` e
RIPRISTINARE `consumedSpawns` con le posizioni note**, ricostruite dagli id
**`sp|<scena>|<x>|<y>`** che la mod **gia'' possiede** in `_announcedDeaths`/`_pendingDeaths`.

⚠️ **E la morte va COMUNICATA ai peer** *(serve alla voce 35)*: **da decidere se con un bump di
protocollo o con un quarto prefisso su `EnemyDeathGlobal`** *(`sp|`, `brk|`, `falo|` esistono gia'')*.

⚠️ **ATTENZIONE ALLA STRUMENTAZIONE**: la riga `[Falo''] consumedSpawns azzerati … N annunciate e 0
ricevute` **da sola NON distingue una morte da un reset benigno** *(load di salvataggio, reset dal
menu)* ⇒ **va agganciata a `PLAYERDEAD`/`DEAD_SCREEN`, non a `EnemySpawner.Reset`**.

### ✅ E TUTTI I 5 PUNTI CHE RESTAVANO APERTI — chiusi, con tre correzioni alle MIE premesse

**(a) Rinascita IN-PLACE: OSSERVATA** *(ramo "altra stanza")*: `G1:2008 annuncio RICEVUTO` →
`2009 [TRACE][SPAWN] Respawning dead enemies on level` → `2013 RespawnDeadEnemies` → **`2019` il
nemico appare con `vita=12` e `pos=(-715.1,10.0)`**, **senza alcun `Loaded level` in mezzo**
*(precedente 1981, successivo 2221)*. **La vita risale**: l''id era assente prima *(0 occorrenze su
700-2012)* e presente dopo *(33 righe su 2014-2800)*.
⚠️ **Il caso "STESSA stanza con morti nella stanza" resta NON PROVATO**: entrambe le sedute sono in
`D01Z01S07`, che in questa sessione ha **0 nemici**.

**(b) "Chi uccide = chi si siede": COPRIBILE, e COPERTO.** G1: 1 seduta + 2 kill · G2: 2 sedute +
3 kill. ⚠️ **La MIA premessa era FALSA**: *avevo scritto che G2 non si era mai seduto.*

**(c) I due `Post-spawn` per load: confermati, ma la MIA spiegazione era SBAGLIATA.**
**Non e'' `SpawnEnemiesOnLoad` che gira 2×** *(gira **1 volta per load**: 11 trace in G1 = 11 load)*:
- **la 1ª e'' il postfix Harmony** *(`EnemySpawnPatch.cs:28-32`)*, 4 righe dopo il trace del gioco e
  **prima** di `Loaded level`;
- **la 2ª e'' il RESCAN DELLA MOD** *(`EnemySyncManager.cs:2514-2524`,
  `if (!_spawnFinalized) OnEnemiesSpawned()`)*, 11-24 righe **dopo** `Loaded level`.
**Numero corretto della linea di base = la SECONDA riga**, per le stanze con nemici.
⚠️ **E per `D01Z01S07` NESSUNA delle due e'' utilizzabile**: la prima e'' `0/7/0` e la seconda
`0/0/0` *(0 spawn point trovati: artefatto)*.

**(d) Fiasche e vita: NON MISURABILI oggi.** **0 righe** di `Flask`/`Fiala`/`Heal`/`Fervour` come
misura *(`vita=`/`life=` e'' dei NEMICI; `Storing 60 fervour…` e'' un messaggio del gioco al load)*.
**Strumento proposto** *(API già nel repo, `PvP/DamageCalculator.cs:83-84` e
`ProgressSync/Helpers/StatHelper.cs:17-26`)*: loggare `Core.Logic.Penitent.Stats.Life.Current`,
`Stats.Flask.Current`, `Stats.Fervour.Current` **nel postfix di `PrieDieu.OnUse`** e **dentro
`ReplicateBonfire`** ⇒ `[Falo''] stats: vita=X/Y fiale=A/B fervour=Z/W`.
⚠️ **Il postfix di `OnUse` gira PRIMA del corpo** ⇒ la lettura "dopo i benefici" va fatta **~1 s dopo**
*(coroutine)* oppure **nel postfix di `RespawnDeadEnemies`**, che nel ramo prima-attivazione e''
**dopo** `ShallowActivationLogic` *(confermato dall''ordine `save` → `respawn`)*.
✅ **E il misurabile che c''e'': il SEDUTO SALVA, IL RICEVENTE NO.** G1 *(1 seduta)*: `SAVEGAME` a 2256
e `SaveGame` a 2265, **11 righe dopo** `Used PrieDieu` 2254. G2 *(2 sedute)*: 1656/1659 e 1713/1716.
**Tutti gli altri save** stanno a **9 righe da un `OnBeforeLevelLoad`**. ✅ **Coerente con
`ReplicateBonfire` che NON chiama `ShallowActivationLogic`.**
⚠️ **E questo smentisce il CONTEGGIO del test precedente**: G1 si e'' seduto **1 volta** *(non 2)* e
G2 ha **10** `SaveGame` *(non 13)*, di cui 2 da seduta, **1 doppio dalla morte**, 7 da transizione.

**(f) REGRESSIONI: nessuna.** `[Falo''] replica fallita` **0** · `NullReference` **0** ·
`IndexOutOfRange` **0** · `SAVEERROR` **0** · `[Progress] apply failed` **0** · **`exceptions=`
non-zero: 0** *(110 righe `[Probe]` su G1, 116 su G2)*. **Server: nessun errore**, e l''host reassign
ha funzionato *(14:10:52 G1 disconnesso → `Host reassigned to: G2`)*.

### ⭐ E UNA SCOPERTA STRUTTURALE CHE SPIEGA LE `IMPRONTA DIVERSA` *(voce 45)*

```
`_ids` e'' APPESO dal rescan periodico che gira SOLO SULL''HOST (EnemySyncManager.cs:2525-2533)
   => sull''HOST un nemico creato fuori dai pass di spawn compare nei probe entro ~0,5 s
      (ed e'' cosi'' che si vede la rinascita in-place)
   => sul PEER `_ids` cambia solo ai load => CIECO
```
**⇒ E'' l''asimmetria host/peer, NON il falo''**, a produrre le `IMPRONTA DIVERSA` *(5 su G1 e 6 su G2
in questo test: **tutte** le righe `[Error]` della sessione)*. **La voce 29 resta coerente.** ⭐ **E
spiega anche perche'' una rinascita in-place e'' osservabile sull''host e non sul peer.** **Da collegare
alla voce 11 e alle rifiniture.**


## [2026-10-11 04:00] LA MORTE DEL GIOCATORE — mappatura completa in IL, e TRE DATI che decidono il fix

### 🔴 DATO 1 — IL REFILL STA IN UN''ALTRA CHIAMATA, e questo rende il fix piccolo

```
Penitent::Respawn()  -- 8 istruzioni, e il suo UNICO chiamante e'' DeadScreenWidget (<>m__0 @IL_0020)
   IL_000A  EnemySpawner.Reset()                     <- svuota la lista delle posizioni consumate
   IL_0014  PersistentManager.RestoreStored()        <- QUI STA IL REFILL
   IL_001E  SpawnManager.Respawn() -> SpawnPlayer(..., forceLoad=true) -> ChangeLevel -> InitializeLevel
```
**`RestoreStored()`** *[36 istruzioni]*: `Life.SetToCurrentMax` @IL_0024 · `Flask.SetToCurrentMax`
@IL_003D · Fervour @IL_0066 *(se `GetPrieDieuLevel()>1`, altrimenti `Current=0`)* ·
`InventoryManager.ResetObjectsEffects()` @IL_0093 · **`SaveGame(true)` @IL_009A**.
⇒ **`Reset()` e il refill sono DUE CHIAMATE DIVISE dentro lo stesso metodo: sopprimere `Reset()`
lascia il refill INTATTO** — *e va lasciato, altrimenti il giocatore torna al falò con 0 HP.*
**E'' esattamente il confine richiesto: i nemici non respirano, la vita sì.**
⚠️ **Effetto collaterale nuovo**: **OGNI MORTE SCRIVE IL SALVATAGGIO** *(`SaveGame(true)`)*.
⭐ **E il falo'' rifà le stesse 3 cose in `PrieDieu::ShallowActivationLogic`** *(metodo diverso)* ⇒
**due implementazioni parallele del refill**, ed e'' per questo che il ricevente di un falo'' non
guarisce: **non passa da nessuna delle due.**

### 🔴 DATO 2 — I POSTFIX GIRANO ANCHE QUANDO UN PREFIX RITORNA `false`

**In Harmony** *("Postfix methods are always executed", documentazione v2)*: **`EnemySpawnResetPatch`
e'' un Postfix su `EnemySpawner.Reset`** ⇒ **sopprimere il `Reset` con un Prefix NON ferma il suo
postfix** ⇒ **`OnDeathStateReset()` girerebbe comunque** ⇒ **il gioco tiene i nemici morti ma la mod
dimentica le morti** = **l''errore della voce 24, AL CONTRARIO** *(una macchina con nemici morti che
non li annuncia piu'' a chi entra)*.
✅ **Quindi sopprimere il respawn SIGNIFICA anche NON svuotare la memoria, e il salto va SCRITTO.**
⭐ **E la soluzione e'' che le DUE cose leggano lo STESSO flag** ⇒ **la coerenza e'' per costruzione,
non per disciplina.**
*(Nota: `_pendingDeaths` svuotato sarebbe innocuo — `ReceiveEnemyDeathGlobal` chiama anche
`MarkSpawnPointConsumed`; `_announcedDeaths` NO.)*

### 🔴 DATO 3 — `Reset()` HA TRE CHIAMANTI, E SOLO UNO E'' LA MORTE

```
Penitent::Respawn()            @IL_000A   morte del giocatore
PersistentManager::LoadGame    @IL_001E   load di un salvataggio
LogicManager::ResetAllData()   @IL_0014   reset dal menu
```
⇒ **il fix richiede un FLAG attorno a `Penitent::Respawn`**: sopprimere gli altri due **romperebbe il
caricamento dei salvataggi** *(la lista `consumedSpawns` non e'' serializzata)*.
⚠️ **E il flag va abbassato in un `Finalizer`, NON in un Postfix** *(lezione della voce 41: se
`Respawn` lancia a metà, un Postfix non girerebbe e il flag resterebbe alzato **per sempre**).*

### ✅ E LE ALTRE RISPOSTE

**(b) Il respawn della morte e'' 100% LOCALE.** *Nessuna riga di rete in
`Respawn`/`Reset`/`RestoreStored`/`SpawnManager.Respawn`/`SpawnPlayer`/`ChangeLevel`/`InitializeLevel`/
`SpawnEnemiesOnLoad`/`CreateEnemy`; `consumedSpawns` e'' `ldfld` di **istanza**.* **I 4 soli siti di
invio di `EnemySyncManager` sono `AnnounceDeathStateToPeers` L2308 e `UpdateCore` L2627/2685/2782:
NESSUNO nel percorso di morte.** ✅ **E la mod NON ha alcun hook sulla morte del giocatore** *[grep
ricorsivo: nessun `HarmonyPatch` su `Penitent`/`DeadScreen`/`Respawn`/`EntityStatus`/`OnDead`]*.
**⇒ "Si sopprime per CHI MUORE; negli altri non c''e'' nulla da sopprimere."** **La prima frase
dell''utente descriveva il difetto, la seconda la conseguenza operativa.**

**(c) La strada (a) e'' quella giusta**, con il flag obbligato. ⚠️ **La strada "sopprimere
`SpawnEnemiesOnLoad`" e'' SCORRETTA**: `Reset` ha **gia''** svuotato la lista ⇒ il respawn sarebbe
solo **rimandato al primo cambio stanza successivo** = proprio il caso da evitare. ⚠️ **Limite noto,
non un difetto**: `CreateEnemy` fa `if (!EnablePersistence) Consumed = false` ⇒ **quegli spawn point
rinascono comunque.**
✅ **E NON collide col falo'' appena deployato**: `RespawnDeadEnemies()` ha il **`Clear()` dentro**
*(IL_0017-001C)* e **non passa mai da `Reset`**; `ReplicateBonfire` chiama quello ⇒ **sedersi al falò
respawna normalmente.**

**(d) Il check**: **`Main.Multiplayer.NetworkManager.IsConnected`** *(copre **host E peer**)* **+
`Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Count > 0`** *(popolata da
`NetworkManager.cs:336` in `ReceiveConnection`)*.
⚠️ **NON usare `EnemySyncManager.IsHost`**: e'' `_isHost || !IsConnected` ⇒ **`true` anche da soli.**

**(e) QUARTO PREFISSO, NON UN BUMP.** `NetworkType` *(49 righe, membri 0-13 e 21-25)* **non ha** un
player-death. Aggiungerlo costerebbe: **enum in `Common.dll` da ridistribuire su ENTRAMBE le
macchine** + **`PROTOCOL_VERSION` in DUE posti** *(`NetworkManager.cs:631`, `Server.cs:854`)* +
`verify-deploy.ps1` + 2 switch + plumbing + **invaliderebbe ogni deployment esistente**.
**`EnemyDeathGlobal = 24`** invece porta **stringa UTF-8 libera**, il server inoltra **a tutti tranne
il mittente senza dizionario ne'' filtro di scena**, e ha gia'' `sp|`, `brk|`, `falo|`.
⇒ **`pdead|`, ~4 righe, zero bump.**
⚠️ **Contro**: **sovraccarico semantico**; **il ramo deve ritornare SUBITO**, prima di
`_pendingDeaths.Add` *(`:3501`)* — metterlo li'' sarebbe **piu'' rischioso di `falo|`**, perche'' il
payload naturale e'' un **nome giocatore** e quella lista e'' **la memoria che fa UCCIDERE i nemici**;
**nessuna garanzia di consegna** *(se il ricevente e'' a meta'' level load, il boss puo'' non esistere
ancora)* ⇒ **progettarlo IDEMPOTENTE.**
✅ **E NON replicarlo in `_announcedDeaths`**: **la morte di un giocatore e'' TRANSIENTE**, chi entra
dopo **non deve** apprenderla. **Lo schema dei nemici esiste perche'' una morte di nemico e'' stato
PERSISTENTE.**

### 🔴 E LA VOCE 35 (BOSS) CAMBIA FORMA — in meglio

```
EnemyBehaviour::GetTarget() (54 istr.) NON guarda MAI se il target e'' morto:
   @IL_0022-0041 ritorna il target corrente
   @IL_0042-008F FALLBACK = Core.Logic.Penitent LOCALE (lo setta e lo ritorna)
=> "il boss spegne l''IA" NON si spiega con GetTarget.
E IL GIOCATORE REMOTO NON E'' UN Penitent:
   OtherPlayerManager.AddActivePlayer (:189-191) fa `new GameObject("_"+name)` + OtherPlayerScript
   => nessun Penitent, nessun collider
   E il nemico e'' simulato sulla macchina che lo possiede, dove GetTarget() puo'' solo
      restituire il Penitente LOCALE
=> ⭐ "far cambiare target al boss verso un vivo" NON e'' un retarget: E'' IMPOSSIBILE.
```
**Le due strade reali, entrambe piu'' piccole di un retarget:**
| | Strada | Meccanismo |
|---|---|---|
| **A** | ⭐ **Cambiare la PROPRIETA'' del boss** alla macchina col giocatore **vivo** | **esiste GIA''**: `EnemyOwnership=25`, `SendEnemyOwnership` :2685, `ShouldOwnByProximity` :1846 |
| **B** | **Mettere in PAUSA il boss** | `PauseEnemy` :965 **esiste GIA''** |

⇒ **Il lavoro sui boss e'' medio-piccolo, e la (A) e'' quella giusta** *(il boss passa a chi puo''
combatterlo)*. **⚠️ Ma entrambe richiedono di comunicare la morte ⇒ il `pdead|` e'' il prerequisito.**

**E un dettaglio che conferma il disegno**: **`Penitent.OnDead` ha 71 sottoscrittori** — nemici
normali con `TargetIsDead`/cambio stato *(`AshChargerBehaviour::TargetOnDead`,
`WalkingTomb`, `Runner`, `Jumper`, `NewFlagellant`, `ViciousDasher`)*, e **boss: SOLO
`AmanecidasBehaviour`** *(`AmanecidasAudio.StopAll`, solo audio)*, **`SnakeBehaviour::OnPenitentDead`**,
**`IsadoraAudio`** ⇒ **la maggior parte dei boss (Crisanta, Esdras, Perpetua, Pietat, HighWills,
PontiffHusk, Bejeweled, Lesmes/Quirce…) NON e'' iscritta**, **e il meccanismo esatto resta NON
VERIFICATO** *(candidato: `Logic.SetState(2)`/`PlayerCurrentLife=-1` in `Penitent::OnUpdate`
@IL_003E/005F, che produce il log `Game state -> PLAYERDEAD`)*.
⭐ **Ma con la strada (A) non ci serve saperlo**: **il boss passa alla macchina del vivo e combatte
normalmente.**


## [2026-10-11 05:00] PEZZO 1 E 2 DELLE MORTI — e un difetto latente trovato nello strumento

### ✅ PEZZO 1 CONFERMATO, e la linea di base per il Pezzo 2 e'' chiara

```
G2 L941  Game state has been set to PLAYERDEAD
G2 L1092 [Morte] il giocatore e'' morto: connesso=True altri giocatori=1
             => SOPPRIMO il respawn del mondo · vita=0/88 fiale=2/2 fervour=32/60
G2 L1093 [Falo''] consumedSpawns azzerati dal gioco: dimenticate 0 annunciate e 0 ricevute (S01)
G2 L1094 RestoreStored · L1095 SAVEGAME · poi il level load
G1:      [Morte] = 0 occorrenze ✅ (la morte e'' locale, e nessuno la comunica)
```
✅ **`altri giocatori=1` e `SOPPRIMO`: il rilevamento e la decisione sono corretti.**
⭐ **E `consumedSpawns azzerati` e'' girato IN GAMEPLAY alla morte** *(su G1 le 2 occorrenze sono
entrambe `MainMenu`)* ⇒ **il Pezzo 2 ha davvero qualcosa da sopprimere, e questa e'' la linea di base.**
⚠️ **E il `Finalizer` ha funzionato**: nessuna seconda morte in sessione, **ma la riga delle stats al
falo'' NON porta il suffisso `(stiamo replicando un falo'')`** ⇒ **il flag era a terra** ✅.
⭐ **E un dato per il futuro `pdead|`**: **`[Morte]` scatta alla PRESSIONE DEL TASTO, non al decesso**
*(`PLAYERDEAD` a L941, `[Morte]` a L1092: `Respawn` ha un solo chiamante, `DeadScreenWidget`, dopo il
tasto)*. **⇒ per il boss, "quando comunicare la morte" e'' una decisione da prendere.**

### 🔴 IL DIFETTO LATENTE NELLO STRUMENTO — trovato per tempo

**Le barre del gioco** *[IL]*: `PlayerHealth.get_BarTarget = Life.Current / Life.**Final**` ·
`PlayerFlask` usa `Flask.**Final**` · `PlayerFervour` usa `Fervour.**CurrentMax**` per i segmenti.
**Il mio `StatsLine()` stampava `CurrentMax` per TUTTI e tre** ⇒ **giusto per il fervore, sbagliato per
vita e fiale.**
⚠️ **Oggi coincidono** *(`MaxFactor` non e'' mai toccato su Life/Flask: `set_MaxFactor` ha **2 soli
chiamanti in tutto l''assembly, entrambi `GuiltManager`, entrambi su `Fervour`**)* ⇒ **le misure gia''
fatte sono vere.** **MA il giorno in cui abbasseremo `Life.MaxFactor`** — **cioe'' esattamente la Colpa
con le spine che stiamo per costruire** — **lo strumento avrebbe misurato il massimo PENALIZZATO
mentre la barra continuava a dividere per `Final`.**
✅ **CORRETTO**: vita e fiale su `Final`, fervore su `CurrentMax`, **e in piu'' stampa i `MaxFactor`**
*("fattori: vita=… fervour=…")*, **cosi'' quando un massimo si muove si sa se la causa e'' nostra o
della Colpa nativa.**
⭐ **La lezione**: **uno strumento che misura una cosa mentre la barra ne mostra un''altra e'' peggio di
nessuno strumento, perche'' viene creduto** — *e la divergenza sarebbe cominciata proprio il giorno in
cui la funzione nuova entra in funzione.*

### ✅ E LE DUE ANOMALIE SPIEGATE — nessuna e'' nostra

**(A) Il massimo del fervour cambia (60 → 50): e'' la COLPA NATIVA.** `CurrentMax` e'' **calcolata**:
`Mathf.Min(Attribute.CalculateValue(), MaxValue) * MaxFactor` *[IL]*. **Il fattore =
`guiltConfig.fervourMaxFactor.Evaluate(guiltDrops.Count)`**, una curva sul **numero di colpe cadute**
*(l''asset e'' `Resources/GuiltConfig`, quindi il 5/6 non e'' in IL)*. **E la cronologia si spiega da
sola**: al `[Morte]` *(prefix di `Respawn`, **prima** del corpo)* il fattore e'' ancora 1 ⇒ `32/60`;
**il fattore viene riapplicato al prossimo spawn del Penitente** *(durante `Respawn`)* ⇒ dopo e'' `50`.
✅ **E `0/50`**: **`RestoreStored` E `ShallowActivationLogic` ricaricano il fervore SOLO se
`GetPrieDieuLevel() > 1`, altrimenti `Current = 0`** ⇒ **con `0` osservato, il livello e'' ≤ 1.**
*Quindi `0` = "non ricaricato", `50` = "massimo sceso per la Colpa".*

**(B) La fiala di G1: NON e'' un bug.** **"Ha perso una fiala" e'' ESCLUSO in IL** *(un clamp 2→1 e''
escluso perche'' il massimo letto e'' 2)*. **Il caso realistico e'': aveva 1/2** *(un giocatore vivo che
ha bevuto)*. ⚠️ **Ma la baseline non e'' nei log** *(`fiale=` compare una volta sola)* ⇒ **il confine
"mondo si'' / benefici no" NON e'' dimostrato dal test: lo dimostra l''IL** —
**`LogicManager.UsePrieDieu()` e'' SOLO `OnUsePrieDieu.Invoke()` (23 byte)** *[IL]*, e
`Life/Flask/Fervour.SetToCurrentMax` + `SaveGame(true)` vivono **solo** in
`PrieDieu.ShallowActivationLogic`, che la mod **non chiama mai**.

### ⚠️ E DUE CORREZIONI ALLA MAPPATURA

**(1) `ShallowActivationLogic` NON e'' solo del ramo di prima attivazione**: **anche
`ReActivationLogic::MoveNext` lo chiama come PRIMISSIMA istruzione** *(`IL_002b`)*, **poi
`RespawnDeadEnemies`** *(`IL_013e`)*. **⇒ il postfix delle stats legge DOPO i benefici in ENTRAMBI i
rami** *(meglio di quanto dichiarava il commit)*, **e il `SAVEGAME` visto su G2 non prova "prima
attivazione".**

**(2) ⚠️ E il test del confine era debole**: **su G2 la riga delle stats NON isola il beneficio del
falo''**, perche'' era **appena morto** e `RestoreStored` aveva **gia''** messo vita e fiale al massimo
⇒ **`88/88 2/2` non dimostra nulla sul falo''.** **Serve un falo'' su una macchina NON appena morta.**

### ⚠️ E IL PEZZO 2 NON E'' ANCORA VERIFICATO IN GIOCO

**Il difetto della voce 44 NON e'' riprodotto in questa sessione, e NON per merito**: **G2 muore in
`D01Z01S01`, azzera i consumati li'', rispunta al falo'' di `D01Z01S07` e poi esce al menu: NON rientra
mai in S01.** **Nemmeno G1.** ⇒ **la stanza del reset non e'' stata riosservata da nessuno.**
⭐ **Per il Pezzo 2 serve UNA sessione che TORNI nella stanza della morte: e'' l''unica misura
"prima/dopo" possibile.** **E i conteggi dove sono osservabili coincidono** *(`S01 3/7/3 → 5/7/5` su
entrambe; `S07 0/7/0`)*.

### 📌 E NESSUNA REGRESSIONE

`NullReference` 0 · `IndexOutOfRange` 0 · **`exceptions=` ≠ 0: 0** *(67 righe `[Probe]` su G1, 62 su G2)*
· `SAVEERROR` 0 · `[Progress] apply failed` 0 · `[Falo''] replica fallita` 0 ·
**`IMPRONTA DIVERSA` 1 su G1, 0 su G2** · `[Error` = la stessa riga.
**Server**: 2 connessioni, 2 disconnessioni, **0 riassegnazioni di host**, **0 errori**.


## [2026-10-11 06:00] IL PEZZO 2 VERIFICATO IN GIOCO — e il controllo interno che rende la lettura non ambigua

### ✅ LA SOPPRESSIONE FUNZIONA, coi numeri

**Stanza della morte = `D01Z01S01`.** `Post-spawn`, **seconda riga di ogni coppia** *(la prima e'' il
postfix Harmony, la seconda il rescan della mod)*:
```
dopo la 1a morte:  G2 2/7/2 -> 953 = 3/7/3     G1 2/7/2 -> 1227 = 3/7/3
dopo la 2a morte:  G2 2/7/2 -> 1658 = 3/7/3    G1 2/7/2 -> 1457 = 3/7/3
```
**⇒ G1 e G2 coincidono, E coincidono con lo stato PRE-MORTE** *(G2 prima di morire: 3 vivi, indici
12/14/16; al rientro: gli stessi 3)*.

⭐ **IL CONTROLLO INTERNO, ed e'' la cosa piu'' elegante di questo test**: **lo stesso `5/7/5`
ricompare su G2 SOLO a 1817, cioe'' DOPO IL FALO'' VERO** ⇒ **la lettura non e'' ambigua**:
```
3/7/3  = la lista dei consumati e'' stata CONSERVATA   (morte soppressa)
5/7/5  = la lista dei consumati e'' stata AZZERATA    (falo' vero)
```

**E la memoria**: `NON dimenticata (2 annunciate, 0 ricevute)` a 861 e 1564 ⇒ **al rientro G2 ha
TROVATO I NEMICI GIA'' MORTI, non li ha ri-uccisi** *(`[EnemyKill]` = 2 in tutto il log, entrambe
PRIMA della morte: 382, 632)*. ✅
**E la prova negativa**: `consumedSpawns azzerati dal gioco` in **gameplay** = **ZERO** *(le 2 righe su
G2 e le 2 su G1 sono tutte `MainMenu`)*. ✅
**E il `Finalizer`**: le righe di soppressione di `EnemySpawner.Reset` su G2 sono **2 = il numero
delle morti** ⇒ **il flag non e'' rimasto alto.** ✅
**⚠️ E il `5/7/5` vs `3/7/3` "di base" ESISTE ANCORA in questa sessione, ma all''INGRESSO INIZIALE e
PRIMA di ogni morte**: *e'' l''asimmetria dei due salvataggi* *(G2 aveva 5, G1 ne vede 3)*, **non la
morte.** ✅ **E dopo la morte di G2 i due si sono allineati.**

### ✅ E IL CONFINE MONDO/BENEFICI E'' MISURATO

```
G1 (riceve il falo'', NON si siede):  fiale=0/2    <- NON salite  ✅
G2 (si siede):                        fiale=2/2    <- salite      ✅
```
*Le fiale erano a **0** su entrambi* *(l''utente le aveva usate tutte)*, **quindi il valore non si
confonde con lo stato precedente.** ✅
⚠️ **Riserve dichiarate**: **su G1 manca una lettura "prima"** *(`fiale=` compare una volta sola)* ⇒
**il confine e'' misurato solo in USCITA**; **e su G2 il `2/2` non isola il falo''**, perche'' era
appena morto e `RestoreStored` aveva gia'' riempito tutto. **Entrambe le riserve sono oneste e non
intaccano la conclusione**, che regge anche in IL *(`LogicManager.UsePrieDieu()` e'' **solo**
`OnUsePrieDieu.Invoke()`, 23 byte)*.

### ⚠️ E DUE CORREZIONI ALLA MIA ULTIMA ENTRY

**(1) `fervour=37/6` NON ESISTE in questi log.** Estrazione esaustiva: **4 righe** — `37/60`, `30/60`
*(fattore 1.00)*, `0/50` *(fattore 0.83)*, `60/60`. **Sempre `Current <= CurrentMax`.** ⇒ **`/6` era
un `/60` TRONCATO dal mio `Select-String`** *(stessi 37 e 30, massimo diverso)*. **E il "dilemma del
fervour" che avevo sollevato era quindi in parte un artefatto della mia lettura.** ✅ *(Nota: il
`Current > CurrentMax` resta possibile in teoria — `CalculateValue()` **fa** il clamp, `IL` — solo
nella finestra fra il cambio del fattore di colpa e il clamp successivo, cioe'' **l''istante esatto del
prefix su `Respawn`**. **Non e'' un danno: la mod non scrive MAI `Stats.Fervour`.**)*

**(2) 🔴 E un difetto VERO dello strumento, corretto**: **il suffisso `(stiamo replicando un falo'')`
leggeva `PlayerDeathSync.SuppressingWorldReset`** *— il flag della **MORTE*** — **mentre una replica e''
marcata da `EnemySyncManager.IsReplicatingBonfire`.** ⇒ **il suffisso non e'' mai apparso: 0
occorrenze in tutti i log, compresa la riga di G1 che ERA una replica.** ✅ **Corretto, e ora distingue
i tre casi**: *replica · morte soppressa · falo'' locale.*
⭐ **Uno strumento che etichetta sempre allo stesso modo e'' peggio di uno che non etichetta: si legge
come una prova.** *(E'' la stessa famiglia delle guardie che tacciono e dei postfix che girano comunque.)*

### 🔴 E UNA DIVERGENZA RESIDUA, NON NOSTRA DEL PEZZO 2 *(voce 46)*

**Dopo il falo'' di G2, G2 ha `5/7/5` e G1 `4/7`** — e i log lo denunciano da soli
*(`IMPRONTA DIVERSA` su entrambi: G1 id locali 4, G2 id locali 5)*. ⚠️ **Un solo nemico dei due non
riappare su G1**: quello a **`-697.1`** non compare vivo in nessun punto del log di G1, mentre l''altro
*(`-715.1`)* **e'' stato ricreato** *(1492 `owner=nuovo`, vivo per 22 rilevazioni)*.
**⇒ 1 su 2, non zero su 2.** **Candidato** *(non verificato)*: **`ReplicateBonfire` esegue
`RespawnDeadEnemies()` nella scena CORRENTE del ricevente, e `CreateEnemy` scansiona solo la scena
caricata** ⇒ *forse i due spawn point sono in stanze diverse, o uno dei due e'' a spawn on-date.*
⚠️ **NON e'' il Pezzo 2**: compare **dopo il falo''**, e **prima del falo'' i conteggi coincidevano.**
**Da indagare con un test dedicato.** Vedi voce 46.

### 📌 E NESSUNA REGRESSIONE

`NullReference` 0/0 · `IndexOutOfRange` 0/0 · **`exceptions=` ≠ 0: 0** *(118 righe `[Probe]` su G1,
125 su G2)* · `SAVEERROR` 0 · `[Progress] apply failed` 0 · `[Falo''] replica fallita` 0 ·
`IMPRONTA DIVERSA` **2 su G1 e 1 su G2** *(di cui una e'' la divergenza residua)*.
**Server**: 2 connessioni, 2 disconnessioni, **1 riassegnazione di host**, **0 errori**.

### ⚠️ E UNA NOTA SUI BINARI, che spiega una confusione

**Il binario testato era compilato alle 15:06:46/49, PRIMA di `fca4611` (15:07:20)** ⇒ **non conteneva
i due commit dello strumento.** **Ecco perche'' le letture erano `/60` e `/50` con la `StatsLine`
vecchia.** ✅ **Coerente, e senza conseguenze**: *la soppressione — cioe'' la cosa che il test doveva
verificare — c''era.*


## [2026-10-11 07:00] IL BOSS, VERIFICATO — il fix della cessione NON era una precauzione: era una correzione, e nel caso peggiore

### ✅ (a) LA GUARDIA `<Boss>::Damage` E'' VERA, e il verso NON e'' scambiato

**Verificato in IL**: `PietyMonster::Damage(Hit)` **e** `ElderBrother::Damage(Hit)` hanno la guardia
agli **stessi offset**:
```
get_Dead @0006 · brtrue -> ret
get_Logic @0010 -> get_Penitent @0015 -> Entity::Stats @001A -> get_Life @001F
   -> get_Current @0024 -> ldc.r4 0 @0029 -> bgt.un @002E -> altrimenti ret @0033
```
✅ **E'' il verso RICEVENTE, non quello con cui il boss infligge danno**: `IDamageable` dichiara
`Void Damage(Hit)` **e** `Vector3 GetPosition()`, e `PietyMonster` dichiara esattamente quei due.
**Percorso del colpo del giocatore**: `Weapon::AttackDamageableEntities` → `callvirt
IDamageable::Damage(Hit)` → `<Boss>::Damage(Hit)` ⇒ **IL COLPO VIENE SCARTATO.** ✅
*(L''altro `Damage`, quello **senza parametri** di `XxxBehaviour::Damage()`, ha corpo `ret` ed e''
chiamato **DAL** ricevente: il nome ambiguo **non** ha ingannato la mappatura.)*
⚠️ **CORREZIONE AL CONTEGGIO**: **16 classi su 22** sotto `Bosses.*` hanno la guardia, **non "18 su 21"**.
*La lista e le eccezioni erano giuste: `HighWills`, `PontiffHuskBoss`, `Lesmes` + le parti-accessorio
`WickerWurmHeart`, `HomingBonfire`, `SingleAnguish`.*

### ✅ (b) IL BOSS ERA DEL MORTO, ed e'' passato all''altro DOPO la morte

**Nei log**: `G2` = host *(`IsHost: True`, tutte le righe `mia=host`)* **ed e'' G2 il giocatore morto.**
```
G2:777  owner=io  dist_io=0.0  dist_altro=2.7  life=220     <- ULTIMO tick prima della morte
G2:786  [Morte] annuncio inviato: G2 e'' morto
G2:787  PLAYERDEAD
G2:789  [EnemyTree] ElderBrother enabled=False isRunning=False  vita=190  mia=host   <- PAUSA
G2:801  owner=altro
G1:1934 [Morte] stato aggiornato: G2 morto
        [EnemyTree] ElderBrother enabled=True isRunning=True esito=resume:StartBehaviour  vita=190
```

### ⭐ (c) IL NOSTRO FIX E'' STATO ESERCITATO — e il ramo VECCHIO non poteva bastare

**Al rilascio**: `dist_io=7.9`, `dist_altro=6.2` *(G2:801)*.
**`PeerIsCloser` esige `nearestRemote + OWNER_DISTANCE_MARGIN(2.5) < localDistance`** ⇒
*`6.2 + 2.5 = 8.7 < 7.9` = **FALSO***. **E `wants` e'' falso** *(il locale e'' morto)*.
⇒ **NESSUNO dei due rami vecchi scatta** ⇒ **solo `DecideOwnership` con `IsLocalPlayerDead()`** *(e
**senza** `OWNER_HOLD_SECONDS`)* **puo'' aver ceduto.** ✅
**Firma osservabile**: *pausa sul morto + `resume:StartBehaviour` sul vivo, **nello stesso istante della
morte**, a `vita=190`.*
⭐ **E `dist_io=0.0` significa che G2 e'' morto ADDOSSO al boss** — **il caso peggiore, quello che
`peerCloser` non copriva, e il buco morde SOLO li''** *(a meno di 2,5 unita'')*. **Ecco perche'' "in
passato funzionava": se il proprietario moriva LONTANO, il ramo vecchio bastava.**
*(Nota: la stringa `PauseEnemy` non compare nei log perche'' `PauseEnemy` logga come `[EnemyTree]`.)*

### 🔴 (d) E IL BUCO ERA REALE, e si VEDE nei log — e'' esattamente il sintomo segnalato

```
Dopo la morte:
   la copia dell''HOST resta FERMA a 190      <- ⭐ la riga "la barra resta ferma mentre l''altro colpisce"
   il CLIENT (nuovo proprietario, vivo) la porta a 190 -> 0
```
**⇒ Con l''host ancora PADRONE sarebbe stato BOSS INVULNERABILE.** ✅
⚠️ **E la spiegazione della "vita condivisa" dell''utente e'' ora precisa**: *scende **identica** su
entrambe finche'' entrambe applicano *(400→190 identico nei due log)*, **poi solo sul client** e
**gelata sull''host**.* ⇒ **e'' la fragilita'' gia'' annotata in §2** *("giusta per ora, ma fragile:
se un danno arrivasse per una via che non passa da `EnemyDamageArea.TakeDamage`, la barra
divergerebbe")* — **e il caso "proprietario morto addosso al boss" e'' proprio una di quelle vie.**
**⇒ Materializzata, non piu'' solo prevista.** **Da decidere** *(non implementare a sorpresa)*.

### ⚠️ (e) `EnemyDamagePatch`: il suo COMMENTO e'' FALSO

**E'' un Postfix su `EnemyDamageArea.TakeDamage`** *(nome di METODO reale: `Void TakeDamage(Hit,
Boolean)` — non un evento)*. ⚠️ **Ma la frase «a PEER never sees its own hit applied» e'' falsa**: un
Postfix gira **dopo** `EnemyDamageArea::TakeDamage` → `TakeDamageAmount` *(la sottrazione vita)*, **e per
i boss la vita NON viene riscritta dalla rete** *(`if (!isBoss) enemy.SetHealth(currentLife)`;
`IsBoss` = namespace sotto `Gameplay.GameControllers.Bosses`)*.
**Prova nei log**: *la copia del client **perde vita mentre il padrone e'' l''host*** *(G1 `owner=altro`
con `life=400` → `owner=altro` con `life=370`)*.
**Il doppio conteggio NON avviene** perche'' ogni macchina applica ogni colpo **una volta sola** *(il
proprio in locale + gli altrui via relay)*. ⇒ **funziona, ma il commento va corretto: descrive un
modello che non e'' quello vero.**

### ⚠️ CORREZIONE IMPORTANTE: il boss NON era `PietyMonster`

**Era `ElderBrother`** *(= Warden of the Silent Sorrow, il PRIMO boss, scena `D17Z01S11`)*.
**Prove**: id `en|D17Z01S11|ElderBrother|-806|22` · clip `ElderBrother_Death` ·
`ElderBrotherIntroDummy` · **G2:914 "G1 has defeated the Warden"** · progresso `D17Z01_BOSSDEAD`.
**`PietyMonster` = 0 occorrenze nei log.**
⭐ **E questo spiega la terza riga del test** *("il boss si ferma del tutto" → "MAI avvenuto")*:
**`PietyMonsterBehaviour::OnUpdate` e'' l''UNICO behaviour di boss che legge `LogicManager::get_CurrentState`
e ferma l''albero sul `PlayerDead`** *(verificato in IL su tutti i chiamanti)*; **`ElderBrotherBehaviour`
non ha quel ramo** ⇒ **non poteva fermarsi.** ✅ **"Mai avvenuto" era corretto E previsto.**

### ✅ (f) LA MORTE DEL BOSS: una sola, pulita, sulla macchina proprietaria

`[EnemyKill] D17Z01S11@ElderBrother MORTO Dead=True vita=0.0 attivo=True` — **1× su G1, 0× su G2**;
clip `ElderBrother_Death`. **`[EnemyDeath]` = 0 in entrambi** ⇒ **`SettleStuckDeaths` MAI SCATTATA**:
**il rischio dichiarato NON si e'' materializzato.** L''host lo seppe dal canale progresso
*(G2:913 `D17Z01_BOSSDEAD`, G2:914 "G1 has defeated the Warden")* dopo aver lasciato la stanza.
**Al rientro (G2:977) il boss NON e'' risorto** ✓.
⚠️ **La struttura del rischio resta comunque confermata dal codice**: *il blocco che annuncia la morte
(`[EnemyKill]`+`SendEnemyKill`+`SendEnemyDeathGlobal`) **non e'' protetto da controllo di proprieta''***,
e le copie non-proprietarie **possono** davvero arrivare a zero *(la vita dei boss non e'' sincronizzata)*.
**Porta aperta, non attraversata.**

### ✅ (g) NESSUNA REGRESSIONE

**`exceptions` max=0 e somma=0 in ENTRAMBI** *(attenzione: il valore, non il nome del campo — le righe
che **contengono** la parola sono 173/174)*. Zero `NullReference` · `IndexOutOfRange` ·
`Object reference` · `IMPRONTA DIVERSA` · `SAVEERROR` · `[Progress] apply failed` · `replica fallita`.
**Unica `[Error` per log = file di salvataggio assente, benigna.** `animRestart` 89/59 · `animRepeat`
45/33 · `orientFlip` 14/16 · `orientSkip` 0/0 · un solo `stalls100=1` per log.
⭐ **E `[EnemyTick]` e'' scesa a <1% del log** *(22 righe su G1, 18 su G2, intervallo 10 s)*, contro
**`[EnemyTree]` = 1140 righe** misurate in una sessione precedente prima del fix del dedup.
**Server**: nessun log fornito ⇒ non verificabile.

### 📌 E UNA NOTA DI METODO DELL''UTENTE, che vale come regola

> *"talvolta codice che funziona non va toccato; al massimo se e'' grave e va pulito."*
**Applicata qui**: il meccanismo dei boss **funziona**, ed e'' **giusto per ora** *(parole della todo
§2)*. **⇒ La divergenza delle barre a proprietario morto si REGISTRA, non si tocca a sorpresa.**
**La decisione si prende con i dati davanti.**


## [2026-10-11 08:00] IL NEMICO FERMO PER SEMPRE — diagnosi verificata, e la voce 11 era scritta male a metà

### 🔴 IL BUG DELL''UTENTE *(voce 49)*, e la diagnosi REGGE

**Parole sue**: *"se un giocatore non-host sta in una stanza la simulazione dei nemici funziona bene, ma
appena l''host entra ed esce subito dopo, quelli **non ancora triggerati** smettono di funzionare per il
non-host."*

**Diagnosi, verificata punto per punto nel codice:**
```
1. il client e'' solo nella stanza: `OwnershipKnown = false`
   => il self-heal gli da'' il nemico e lui SIMULA ✅
2. l''HOST ENTRA: i nemici entrano nella SUA lista `_ids` (lista della SUA scena)
   => `DecideOwnership` gira (solo sull''host) e se si ritiene piu'' vicino SE LI PRENDE
   => manda il token => sul client: `OwnedByLocal = false` + PauseEnemy + **`OwnershipKnown = true`**
      <- IL LATCH SCATTA
3. l''HOST ESCE: quei nemici non sono piu'' in `_ids`
   => l''host non puo'' piu'' arbitrarli, e non manda la restituzione
   (e il token viaggia con `relayToScene`, filtrato per scena)
4. => il client resta con `OwnershipKnown = true`, e il self-heal
   `else if (!IsHost && !previous.OwnershipKnown)` NON SCATTA
   => PUPPET PER SEMPRE. Nessuna via di recupero: `ReceiveEnemyUpdate` → `ResumeEnemy`
      richiederebbe un pacchetto che non arrivera'' mai
```

**⭐ E il punto debole che avevo dichiarato io NON morde — il latch sopravvive:**
```
`_lastSent.Clear()` a :1292 sta in `LevelLoaded(scene)` => solo se QUESTA macchina cambia scena
`_lastSent.Clear()` a :1401 sta in `OnEnemiesSpawned()` => il client non rispawna
l''ID non cambia (`RegisterEnemy` esce se l''istanza e'' gia'' in `_enemyIds`; e il peer nemmeno
   riscansiona: `:2624 else if (IsHost)`)
`HandleOf` e'' FNV-1a della STRINGA id, non un indice
`EnemyState` e'' una CLASSE => le mutazioni PERSISTONO
```

### ⭐ LA CAUSA STRUTTURALE, che risponde alla domanda dell''utente

> *"questa soluzione potrebbe stravolgere il progetto, o lo renderebbe piu'' leggero?"*

```
`EnemySyncManager.LevelLoaded(scene)` — gira su OGNI macchina che cambia scena:
   _ids.Clear();  _lastSent.Clear();  _puppetEnemies.Clear();  ... tutto azzerato
   col commento: "ids do not survive a scene change"

⇒ LA PROPRIETA'' E'' MEMORIA DI SCENA PER COSTRUZIONE
⇒ chi si MUOVE dimentica, chi RESTA no  => IL RESET E'' ASIMMETRICO
```
✅ **Quindi il fix NON stravolge niente e NON duplica niente**: *va in un punto che **gia'' esiste** e
riguarda **un caso che nessuno sta decidendo**.* **E non sovrappone il self-heal**: *quello copre "l''host
non ha **mai visto** il nemico"; questo copre "l''host **l''ha visto e poi e'' uscito**"* ⇒ **caso nuovo.**
✅ **E non tocca nessuna decisione esistente** *(l''host al ritorno ha comunque la proprieta'' da
ridecidere, perche'' `LevelLoaded` gli ha azzerato tutto)*.

### ✅ IL FIX SCELTO — (B): l''host restituisce mentre esce

**Dove**: `Multiplayer.OnLevelUnloaded` *(:125)*, **PRIMA di `SendLeaveScene()` (:131)**.
⚠️ **L''ORDINE E'' TUTTO**, e la prova e'' nel server: **il filtro di scena e'' quello del MITTENTE**
*(`Server.cs:768 current = getCurrentPlayer(playerIp)` → `:771 isInSameScene`)*, e
`receivePlayerLeaveScene` *(:350)* fa **`current.sceneName = ""`** ⇒
```
token PRIMA di SendLeaveScene  = ARRIVA ✅
token DOPO                     = "" = MAI
token dopo SendEnterScene      = scena nuova = MAI
```
*E `QueueMesssage` e'' un''unica coda, quindi vale l''ordine di accodamento.*
✅ **E il momento e'' garantito**: `LevelManager` **non ha** `OnLevelUnloaded` *(confermato in IL:
dichiara `OnMenuLoaded/OnGenericsElementsLoaded/OnBeforeLevelLoad/OnLevelPreLoaded/OnLevelLoaded`)*;
`ModLoader.cs:86` mappa l''uno su `OnBeforeLevelLoad`, invocato in
`<ChangeLevelCorrutine>c__Iterator0::MoveNext()` **prima del load** ⇒ **nemici ancora validi e `_ids`
non ancora azzerato** *(`EnemySyncManager.LevelLoaded` arriva dopo, da `Multiplayer.cs:103`)*.
*E il server accetta token **solo dall''host** (`Server.cs:816`)* ⇒ **(B) cura esattamente il caso.**

### ⚠️ E LA (A) COME TIMER E'' SCARTATA — e in DUE modi

```
1. IL TOKEN E'' PUBBLICATO PRIMA DEL GATE:
      :2764-2786  pubblica il token
      :2839-2840  `if (!enemy.gameObject.activeInHierarchy) continue;`   <- il gate
      :2842       il fullSweep sta DOPO
   => un nemico INATTIVO riceve il TOKEN e ZERO aggiornamenti di stato
   => un timer "non sento piu'' l''host" da'' un FALSO POSITIVO
   ⚠️ E IL FALSO POSITIVO CADE PROPRIO SULLA POPOLAZIONE DEL BUG:
      "non ancora triggerati" = tipicamente `activeInHierarchy == false`

2. E un falso positivo che NON avevamo previsto:
      `SendPosition` e'' CHANGE-GATED (`MainPlayerManager.cs:25`, cutoff 0.03)
      => UN HOST FERMO SEMBRA ANDATO VIA IN 0,3 s  => si ruberebbe il nemico a un host fermo
```
⭐ **E la logica giusta esiste GIA''', ma e'' consultata SOLO dall''host**: `ShouldOwnByProximity` e
`PeerIsCloser` trattano **`nearestRemote == float.MaxValue`** come *"l''altro e'' in un''altra scena"* —
**ma girano dentro `DecideOwnership`, cioe'' dietro `if (IsHost)`** ⇒ **e'' letteralmente il pezzo mancante.**
**⇒ Se mai si volesse la (A), il discriminante deve essere la SCENA del giocatore, non un timer.**
⚠️ **Riserva sulla variante "il peer agisce su `ReceiveLeaveScene`"** *(piu'' semplice: esiste, arriva
**senza filtro di scena**, e su un peer `OwnedByLocal == false` implica "e'' dell''host")*: **a 3+
giocatori non dice CHI e'' uscito** *(il pacchetto non porta il nome)*. **⇒ (B) classica = precisa.**

### 🔴 E LA VOCE 11 ERA SCRITTA MALE A META'' — corretto, verificato in IL

```
GLI 8 TIPI SONO CORRETTI **PER `StartBehaviour()`**:
   CrossCrawler.Update, ExplodingEnemy.OnUpdate, Firethrower.Update, Fool.OnUpdate,
   Lesmes.Update, Nun.OnUpdate, RangedBoomerang.Update, ShieldMaiden.Update  = esattamente 8 ✅
MA NESSUNO DI QUEI 16 TIPI CHIAMA `EnablePhysics`  (ZERO su tutte le 16 classi)

GLI 8 CALL-SITE REALI di `Entities.Enemy::EnablePhysics` sono ALTRI:
   PietyMonster::EnablePiety · Acolyte::OnUpdate (x2) · BellCarrier::OnUpdate ·
   Bishop::OnUpdate · Flagellant::OnUpdate (x2) · Menina::OnUpdate
   => tipi veri: Acolyte, BellCarrier, Bishop, Flagellant, Menina, PietyMonster
```
⚠️ **E QUESTO IMPORTA**: **il fix che la voce proponeva** *(agganciare `<Enemy>::OnUpdate` /
`EnablePhysics`)* **sarebbe sul METODO SBAGLIATO** *— e `EnablePhysics` e'' definito da **72** classi.*
⭐ **NAMESPACE**: dei 7 nemici + `Lesmes`, **solo `Lesmes` e'' nel namespace dei boss**
*(`Gameplay.GameControllers.Bosses.Lesmes`)*. **Coroutine di intro dei boss: NON verificate.**
⭐ **E NEI LOG LA 11 NON SI VEDE**: `orientFlip=0`, `orientSkip=0`, `gameOrient=0`, `animRestart=0`,
`animRepeat=0` in ogni `[Probe]`, e `IMPRONTA DIVERSA` = 0 ⇒ **"da misurare, e da riscrivere prima di
agganciare qualsiasi cosa".**
⚠️ **E VA TENUTA DISTINTA DALLA 49**: *sono **speculari e indipendenti*** — **11 = il gioco sveglia il
puppet**, **49 = noi dimentichiamo il puppet** ⇒ **un fix NON copre l''altro.** *(L''utente aveva chiesto
se si potevano chiudere assieme: la risposta e'' no.)*

### 📌 E ZERO REGRESSIONI

`IMPRONTA DIVERSA` 0/0 · `Object reference` 0/0 · `NullReference` 0/0 · `IndexOutOfRange` 0/0 ·
`SAVEERROR` 0/0 · `[Progress] apply failed` 0/0 · `replica fallita` 0/0 ·
`[Error` **1/1** *(entrambe `savegame_1_modded.save` mancante = voce 36, innocue)*.
⚠️ **`exceptions=` NON e'' un conteggio**: e'' il contatore cumulativo di `PerformanceProbe` — *173/174
righe, ma **max=0 e somma=0*** ⇒ **zero eccezioni di sincronizzazione** *(voce 23 chiusa)*.
**E il latch e'' visibile nei log**: su G1 (client) con l''host **gia'' uscito**, il client mostra
**`owner=altro` a :894, :916, :925, :942** — *quattro campioni consecutivi, col Cherub **in pausa
dall'':888***. *Poiche'' su un non-host il self-heal mette `OwnedByLocal=true` quando
`OwnershipKnown==false`, un `owner=altro` ripetuto **dimostra il latch**.* ✅

## [2026-10-11 09:00] ARCHIVIO DELLE VOCI CHIUSE — testo integrale, spostato dalla lista iniziale

**Perche':** la lista iniziale era diventata illeggibile — le voci 44-49 arrivavano a 2000-3600
caratteri su UNA riga sola, e in un editor si confondono con un muro di testo. **Le voci CHIUSE
sono state compresse** nella lista *(numero, titolo, esito in una riga, rimando qui)* e il loro
testo integrale e' qui sotto. **NIENTE E' ANDATO PERSO**: questa sezione e' la copia verbatim di
cio' che c'era nella cella "Stato" della lista. **Le voci APERTE non sono state toccate.**

### Voce 1 — **Animazioni tagliate** (nemici, morti, Penitente) — §1a

**Priorita':** ✅ **stabile, il residuo è del gioco**

**CHIUSA su decisione dell'utente.** Il residuo (**il `TURN` al cambio bersaglio**) è del **GIOCO**:
`TurnAround` → `SetTrigger("TURN")`, ri-triggerato a ogni attraversamento dell'asse x — **`ApplyAnimation` è
escluso perché esce quando è già in quello stato e usa `SetBool("WALK")` per la locomozione, che preserva la
fase del clip**. La **troncatura NON esiste**: 29 uscite misurate a **0,96-1,00**. E il fix `FindBehaviour`
(voce 26) ha ridotto churn e orientamento di **~10-30×**. ⚠️ I numeri vecchi di questa riga *(`animRepeat`
11-36, `avvio=0.00` in 484/499)* **non si vedono più nei test recenti**. Vedi §12

### Voce 2 — **Orientamento che si gira velocissimo** — §1b

**Priorita':** ✅ **RISOLTO E VERIFICATO**

`orientFlip` **crollato da 85 (MAX 34 in 2 s) a 1 (G1) e 3 (G2), MAX 1**; `orientSkip=0` su entrambe, ora normale perché nessuno scrive più l'orientamento da combattere. **Causa della prima versione**: l'isteresi era in `ApplyOrientation` (scrittura della mod, 20 Hz) invece che sulla Prefix su `Entity::SetOrientation` (dove passa il gioco, 60 Hz). **Confermato stabile dall'utente.**

### Voce 3 — **Chi si connette dopo non apprende le morti** — §1g

**Priorita':** ✅

**RISOLTO E VERIFICATO**: `[EnemyJoin] annunciate 2`, nessun `ENEMYDEAD` nel progress

### Voce 4 — **Falò: RESPAWN e REFILL** — §3

**Priorita':** ✅ **RISOLTA E VERIFICATA — il falò si propaga, i benefici NO**

⚠️ **lo stato ACCESO del falò è GIÀ sincronizzato** (`PrieDieuHelper.cs`, 3 patch). **Il respawn invece era LOCALE e non propagato** *(misurato: dopo il falò di uno, le due macchine avevano `5/7/5` e `3/7/3` nella stessa stanza, e la seconda era entrata DOPO il falò e vedeva ancora lo stato pre-falò)*. **✅ ORA SI PROPAGA**, in tre passi e **senza bump di protocollo**: *(1)* l'annuncio `falo\

### Voce 5 — **Sangue e flash nel feedback del colpo** — §12.1 *(`§5c` NON esiste)*

**Priorita':** ✅ **RISOLTO — e la diagnosi iniziale era INVERTITA**

**F3 fatto e verificato dall'utente in gioco.** L'ipotesi di partenza era **SBAGLIATA** (verificato in IL):
**sparks e sangue nascono dal LATO ATTACCANTE** *(`PenitentSword.Attack` → `GetDamageableEntities` →
`GetSwordSparks`, e **solo dopo** applica il danno)*, mentre **suono, flash, dolore e morte nascono dal LATO
DIFENSORE**, dentro `<EnemyType>::Damage(Hit)`, che la mod **già** rieseguiva. **Il confine vero è CHI MENA vs
CHI GUARDA**, non proprietario/non-proprietario. **E "alcuni nemici non fanno sparks" è CORRETTO**:
`sparksOnImpact`/`bleedOnImpact` sono **campi di prefab**. Fix: **replay degli effetti sul percorso di relay**
+ **3 byte nuovi** nel payload di `EnemyDamageRequest` *(`DamageType`, `DamageElement`, flag)* che correggono
anche il **suono** *(prima sempre "simple" su chi guarda)*. **Nessun bump di protocollo**: il server rilancia
il payload verbatim. Vedi §12

### Voce 6 — **Oggetti distruttibili** — §4

**Priorita':** ✅ **QUASI CHIUSO — manca l'ultima verifica**

**Il tipo è `Tools.Level.Actionables.BreakableObject`** *(lanterne, scheletri appesi, simboli attorcigliati:
**la scenografia che respawna**)* — **NON i muri**, che sono **fratelli** e non parenti, hanno vita diversa e
sono già coperti. **GUID veri, per-istanza, authored di scena** *(7/7 canonici, 0 duplicati)*:
`GetPersistenID()` = `GetComponent<UniqueId>().uniqueId`, e `UniqueId.uniqueId` ha **UN SOLO SCRITTORE** *(il
costruttore che scrive `Empty`)* ⇒ **nessuna generazione a runtime** ⇒ **identico sulle due macchine**.
**L'identità è il GUID, NON `scena~indice`**: la tabella `PersistentStates` *(371 voci, curata a mano)*
**contiene ZERO GUID di BreakableObject**, e completarla è **pericoloso** *(gli indici sono posizionali e
**finiscono nei salvataggi** come `scene~index`)*. **Tre patch + un ramo**, e il **crash latente** evitato:
`BreakableObject.SetCurrentPersistentState` fa `((BreakablePersistenceData)data).broken` **senza null-check**,
e `ProgressManager.LevelLoaded` lo chiama con **`null`**. ⚠️ **E il ricevente DEVE usare `Use()`, non
`SetCurrentPersistentState(data)`**: quest'ultimo legge la rottura da `BreakableManager.ContainsBreakable`, e
`AddBreakable` è chiamato **da un solo posto** *(`Damage`)* ⇒ **con il manager vuoto concluderebbe "non è
rotto" e chiamerebbe `Restore()`, CANCELLANDO la rottura in silenzio**. **Test**: passi 1-6 ✅ *(stessa stanza,
inversione ruoli, falò)* · **punto 7 ❌** *(chi entra dopo vede il vaso in piedi)* → **F4b.1** ha aggiunto la
memoria per scena + il formato `scena\

### Voce 12 — **Camminata troncata**

**Priorita':** ✅ **stabile, il residuo è del gioco**

**CHIUSA su decisione dell'utente.** Richiede di ripensare il protocollo animazione *(**loop vs one-shot**)*, e **succede solo in casi estremi**: con le interazioni risolte **è stato fatto il massimo possibile, e il resto è bloccato dalla logica del gioco**. **NB: voce re-inglobata**

### Voce 13 — **La migrazione host non riarbitra**

**Priorita':** ✅ **RISOLTO E VERIFICATO**

**Due fix, e il TODO indicava il ramo sbagliato.** Il difetto **misurato** nasceva dalla **DEMOZIONE**, non dalla promozione: quando l'host perde la connessione, `SetHost(false)` mette in pausa **tutti** i nemici, ma `IsHost = _isHost \

### Voce 14 — **`IMPRONTA DIVERSA`**

**Priorita':** ✅ **RISOLTO**

**ERA UN FALSO ALLARME**: `_listFingerprint` era calcolata solo in `OnEnemiesSpawned`, ma `_ids` viene **appesa** ogni 0.5 s e **rimossa** in `ForgetEnemy` ⇒ la fingerprint descriveva una lista che non esisteva più. **Peggio**: la segnalazione era **una-tantum**, quindi un falso allarme **zittiva ogni divergenza reale**. Ora `_idsDirty` + `RefreshFingerprintIfDirty()` + cooldown 15 s. Vedi §12

### Voce 16 — ⚠️ **Il deploy va verificato su ENTRAMBE le macchine**

**Priorita':** ✅ **RISOLTO**

**Errore di copia nel testing.** ✅ **RISOLTO con uno strumento**: `tools\verify-deploy.ps1` — hash + marcatori + protocollo, esce con codice 1. Vedi §12

### Voce 17 — **I nemici che restano fermi (`pos_distinte=1`)**

**Priorita':** ✅ **non è un bug**

nemici **senza IA di movimento** *(1 sola `posNemico` per tutta la loro presenza nel log, `anim=Idle` sempre)*. **Attenzione a leggerli come "sospesi": non lo sono.**

### Voce 19 — **`Entity.Dead` calcolato vs `EntityStatus.Dead`** — §7

**Priorita':** ✅ **nota tecnica**

documentato

### Voce 23 — **Il desync grave della sessione di test** (eccezione nel ciclo di invio)

**Priorita':** ✅ **RISOLTO**

**era una REGRESSIONE NOSTRA** introdotta in `b2c9cdd`: la diagnostica `[EnemyTree]` leggeva `BehaviourTree`
con **chiamata diretta**, ma il getter è **protected a runtime** (publicized a compile-time) ⇒ **479 e 666
eccezioni** da `UpdateCore → ResumeEnemy`, che **interrompevano il ciclo di invio a metà** ⇒ `SendEnemyUpdate`
non partiva mai. Corretto in `9b1ea87` (reflection + try/catch), **verificato con `exceptions=0` su 384 report
in 4 sessioni**. Vedi §12

### Voce 24 — **Il gioco azzera `consumedSpawns` in DUE metodi**, non uno

**Priorita':** ✅ **RISOLTO**

**`EnemySpawner.Reset`** (morte del giocatore, load di un salvataggio, reset dal menu — **nessuno è un caricamento di livello**) oltre a `RespawnDeadEnemies`. **Scoperto durante il fix della voce 25**: svuotare solo al falò lasciava un annuncio **stantio** ⇒ il nuovo entrato uccideva nemici che l'annunciatore vedeva **vivi**. **Errore speculare, introdotto dal fix stesso.** Vedi §12

### Voce 25 — **Le morti non vengono annunciate se chi le ha viste non è più nella stanza**

**Priorita':** ✅ **RISOLTO**

`AnnounceDeathStateToPeers` ricavava gli id da `FindObjectsOfType<EnemySpawnPoint>()`, cioè **solo la scena corrente dell'annunciatore**: altrove la lista era **vuota e usciva in silenzio** ⇒ **i nemici riapparivano VIVI**. Fix: memoria lato mod degli id annunciati + unione deduplicata + **una riga di log quando l'annuncio è vuoto** (il caso era invisibile). Vedi §12

### Voce 26 — **La pausa NON esisteva per 60 tipi di nemico su 77**

**Priorita':** ✅ **RISOLTO E VERIFICATO**

`Enemy.EnemyBehaviour` è **null** per 60 sottotipi (fra cui **Fool e WheelCarrier**, tutti i nemici della
stanza di test, e quasi tutti i boss): salvano il behaviour in una **proprietà propria**. `PauseEnemy`
**usciva lì**, prima di `PauseBehaviour`/`enabled=false` ⇒ sul non-proprietario **AI, Animator e orientamento
continuavano** = **"cammina sul posto"**. Fix: `FindBehaviour` con fallback su `GetComponent`. **E lo stesso
difetto era nel ramo di promozione di `SetHost`.** **MISURA DOPO IL FIX** (baseline pre-fix ritrovata in modo
indipendente in `a7a726ba73…/LogOutput G1.log`, **stessa sessione** del log G2 DUE ⇒ condizioni omogenee):
scritture di orientamento **437 → 14 (G1) e 5 (G2)**; riavvii di animazione **54 → 14 (G1) e 21 (G2)**;
`orientSkip=0`; `orientFlip` **11 e 1 in totale**. **Riduzione ~10-30×.** ⚠️ Confronto fra sessioni diverse:
non è un A/B sullo stesso run. Vedi §12

### Voce 27 — **Il nemico che resta fermo con un giocatore addosso** — §1h

**Priorita':** ✅ **CHIUSA — NON è un difetto della mod**

**PROVATO col controllo in singolo, disconnesso** *(la mod attiva ma senza peer ⇒ **il percorso della pausa
remota è inerte**)*: il `Fool` a `(-717.7,10)` resta fermo **6 campioni (~12 s)** con distanza grezza **3.07**
*(giocatore a `(-721.3,9)`)*, e **in Fase A** *(single-player, nessun peer)* resta fermo **32 campioni**.
**`dy = +1.0` COSTANTE per tutto il tratto fermo** → salendo a `y=10.7` *(stesso piano, dist 0.81)* **il
nemico parte subito** (`anim=765711723` = camminata). ⇒ **la spiegazione "pendenza" dell'utente è
CONFERMATA.** ⚠️ **La mia misura precedente diceva `dy=+0.1`: era il campione di CONTATTO, non il tratto fermo
— errore mio, corretto qui.** Nei log a due lo stesso Fool è fermo anche con `dist_io` 0.5-1.4. **NON si
tocca.** Vedi §12

### Voce 28 — **`EnemySpawner.Reset`: il logging funziona**

**Priorita':** ✅ **CONFERMATO IN GIOCO**

Il hook **scatta** e il conteggio **combacia esattamente**: G1 uccide **2** nemici *(pos `-717.6,10` e
`-697.5,11`)* e il log dice **`consumedSpawns azzerato dal gioco: dimenticate 2 morti annunciate e 0
ricevute`**, **dopo la 2ª morte e prima della rinascita**. **Il gancio NON è troppo aggressivo.** ⚠️ **Resta
NON testato il ramo `RespawnDeadEnemies`/`OnEnemiesRespawned`** *(il falò vero)*: **mai eseguito** nei log →
vedi voce 31. Vedi §12

### Voce 29 — **Dopo un `EnemySpawner.Reset` le due liste locali DIVERGONO**

**Priorita':** ✅ **RISOLTA di fatto — e il testo citava una cosa che NON esiste**

**Il testo parlava di un "ordine di scena": verificato, NON esiste.** `consumedSpawns` è una **`List<Vector3>`
con uguaglianza ESATTA della posizione**, e `AddConsumedSpawner` appende **solo se non presente**. **E
`EnemySpawner.Reset` è chiamato solo da morte del giocatore, load di un salvataggio e reset dal menu — NESSUNO
dei tre è il falò** *(il falò chiama `BreakableManager::Reset`, omonimo ma diverso)*. L'`IMPRONTA DIVERSA` che
l'aveva generata resta **1 per macchina**, **pre-esistente e non correlata**. ✅ **E la divergenza non può più
nascere dal falò**, perché la replica fa eseguire a entrambe le macchine la stessa azione. Vedi §12

### Voce 30 — ⚠️ **La memoria delle morti si azzera SOLO sulla macchina che si siede**

**Priorita':** ✅ **RISOLTA dalla voce 4**

**Era il difetto latente descritto**: `OnEnemiesRespawned` azzerava la memoria **solo in locale**. **Ora il
ricevente chiama `RespawnDeadEnemies()`, e il NOSTRO postfix su quel metodo azzera anche la sua memoria** ⇒
**entrambe le macchine dimenticano le stesse morti, nello stesso momento.** Verificato nei log: su G2 `memoria
nemici: dimenticate 0 ricevute e 2 annunciate` **dopo la ricezione**, **senza essersi seduto**. Vedi §12

### Voce 31 — **Il falò (`RespawnDeadEnemies`) non è mai stato esercitato nei test**

**Priorita':** ✅ **RISOLTA — e la causa della "assenza" era un LOG MUTO**

**Il ramo del falò non compariva in nessun log, e la spiegazione non era solo "non è mai stato esercitato"**: `OnEnemiesRespawned` e `OnDeathStateReset` loggavano **dentro `if (pending > 0 \

### Voce 32 — **L'arbitraggio host-only è CIECO fuori dalla scena condivisa**

**Priorita':** ✅ **CHIUSA — e la diagnosi "è un difetto da fixare col passo 2" era SBAGLIATA**

**Il DIFETTO VERO era uno solo: il peer si congelava quando il token non poteva arrivare** *(l'host altrove ⇒
`_ids` non contiene quei nemici ⇒ nessun token ⇒ `PauseEnemy` per sempre, misurato: `WheelCarrier` fermo 134
campioni ≈ 67 s)*. **Ed è chiuso dal PASSO 1** *(`f09839d`: il peer simula `!IsHost && !OwnershipKnown`)*,
**verificato in gioco dall'utente**. ⚠️ **Ma la parte "PASSO 2 da fare: togliere `if (!localIsHost)`" era
SBAGLIATA, e l'ho ripetuta senza verificarne la conseguenza pratica**: **il peer NON PUBBLICA un token**
*(`ReceiveEnemyOwnership` ritorna subito `if (IsHost) return;`)* **e APPLICA quello dell'host**
*(`OwnedByLocal = ownedByReceiver`)* ⇒ **se il peer decidesse, la sua decisione verrebbe SOVRASCRITTA** ⇒ **il
gate non è il difetto: è ciò che tiene il peer da parte mentre aspetta l'autorità dell'host.** **E' il
disegno, ed è coerente.** ✅ **PROVA DOCUMENTALE**: **`DecideOwnership` e il suo chiamante sono IDENTICI fra il
tag `stato-sync-movimento-ok` (`998c561`, 7 ott 18:40) e HEAD** — *la "parità" era ed è DENTRO la funzione: la
prima assegnazione è simmetrica, `wants \

### Voce 33 — **`BreakSync`: l'hook non dipinge la rottura** *(memoria dei vasi)*

**Priorita':** ✅ **RISOLTA — la causa era MIA, in `Forget`**

**Il test F4b.1 era fallito al suo caso centrale**: `0d384626` rotta da G1 in `D17Z01S03`, il server
**rimanda** la entry a G2, G2 la **riceve e la ricorda**, entra nella stanza e **la trova in piedi**. **La
causa, ricostruita in IL**: `LoadSnapShot` chiama `SetCurrentPersistentState` su ogni oggetto di cui ha dati →
per un `BreakableObject` **`IsBroken()` è FALSO** *(il `BreakableManager` non viene ripopolato al load)* →
**il gioco chiama `Restore()`** → il mio postfix su `Restore` → **`Forget` cancellava il guid da `pending` E
da `applied`**. **E l'hook è un POSTFIX di `LoadSnapShot`, quindi gira DOPO** ⇒ **quando guardava, il pending
era già stato cancellato.** **Fix**: un flag `loadingSnapshot` *(Prefix su `LoadSnapShot`, abbassato dopo la
ri-applicazione)*: **`Forget` esce subito se il flag è alzato**, e decide la ri-applicazione. **Verificato in
IL.** ⚠️ **E il caso "G2 entra dopo" NON è mai stato esercitato nei test**: le 3 entry rimandate restavano
`pending=3` fino alla fine **perché la stanza non è più stata rivisitata** ⇒ **serve un test che la
rivisiti.** Vedi §12

### Voce 34 — **La dedup del server scarta i ri-annunci** *(di qualunque oggetto persistente)*

**Priorita':** ✅ **RISOLTA per i vasi (F4b.2) — e il difetto è STRUTTURALE per tutto il resto**

`TeamInfo.AddTeamProgress` deduplica **per stringa id su TUTTA la sessione del server** e accetta **solo se
`progressValue > currentValue`** *(in IL: `cgt` **unsigned**)*: tutti gli oggetti persistenti mandano **`value
= 0`**, quindi `0 > 0` è **falso** e un id già visto è **sempre rifiutato** *(`Server.cs:591` esce senza
inoltrare)*. **Conseguenza vera: ogni oggetto può essere annunciato UNA VOLTA per sessione del server.** ⚠️
**CORREZIONE a una riga precedente di questa voce**: *"il formato `scena\

### Voce 37 — 🔴 **L'ECO INFINITA: un'annuncio applicato veniva ri-annunciato**

**Priorita':** ✅ **RISOLTA (F4b.3) — ma la LEZIONE va ricordata**

**F4b.2 funzionava ma aveva introdotto qualcosa di peggio del difetto che curava.** **Conteggi incrociati nei
log**: `APPLY(G1)=SEND(G2)` e `APPLY(G2)=SEND(G1)-2` ⇒ **ogni annuncio ricevuto veniva riapplicato E
re-annunciato, un round-trip per iterazione** ⇒ **187 + 34 SEND su DUE soli vasi**. **Meccanismo, in IL**:
`BreakableObject::Damage` fa `ldfld destroyed / brfalse / ret` ⇒ **se l'oggetto è GIÀ rotto esce subito
LASCIANDO `destroyed = true`**, e il postfix del mittente faceva `if (!___destroyed) return;` ⇒ **annunciava
pur non essendo successo nulla**. **E il percorso nuovo non alzava `reapplying`**. ⚠️ **PERCHÉ NON SI VEDEVA
PRIMA: il canale Progress deduplicava, quindi l'eco veniva inghiottita dall'eco stessa.** **LA LEZIONE, e vale
per tutto il progetto**: *cambiare canale obbliga a ricontrollare TUTTI i guard che proteggevano il canale
vecchio — un guard che sembra ridondante può essere l'unico che teneva su la baracca.* **Fix**: una **Prefix**
su `Damage` cattura **se l'oggetto era già rotto PRIMA** *(l'unico momento in cui la domanda ha risposta:
dopo, il campo è `true` in entrambi i casi)*, e la Postfix esce subito se lo era. **Più** `ApplyOrRemember`
che alza `reapplying` con **salva/ripristina** *(non azzera: un'applicazione può annidarsi)*. **Verificato in
IL**: ordine `TryGetValue → CurrentlyUpdatingProgress → IsReapplying → IsConnected → Send`. Vedi §12

### Voce 38 — ⚠️ **Il log del SERVER non esisteva su disco**

**Priorita':** ✅ **RISOLTA — `ServerOutput.log`**

**`Logger.Print` era solo `Console.WriteLine`** *(`Logger.cs:39`)* **e nella cartella del server NON c'era
nessun file di log** ⇒ **il resoconto del server moriva con la finestra**. **È costato DUE indagini di fila**:
la dedup della proprietà e la lanterna post-falò sono state diagnosticate dai log dei **client**, perché il
log del server non era mai stato conservato — *e le due righe che le avrebbero provate in un colpo solo sono
`Server.cs:625` "Received new pers. object from X: Y" e `Server.cs:593` "Received duplicated or inferior
progress from X: Y"*. ✅ **Ora il server scrive `ServerOutput.log`** accanto al binario
*(`AppContext.BaseDirectory`)*, con intestazione datata e **flush a ogni riga**. **`Print` formatta la riga
UNA volta e la manda a entrambe le destinazioni**, così ciò che si è visto e ciò che si è conservato non
possono divergere. **Tutte le 33 chiamate del server passano dal `Logger`**, quindi lo specchio le prende
tutte. ⚠️ **Due scelte deliberate**: *scrittura sincrona sotto lock* *(un log che perde le ultime righe quando
il processo viene fermato — e questo server si ferma con un tasto — è peggio di nessun log, perché viene
creduto)*, e **nessun errore può far cadere il server**. **Verificato AVVIANDOLO**, non solo compilato. **Da
chiedere per nome: `ServerOutput.log` dell'host.** Vedi §12

### Voce 39 — ⚠️ **La doppia animazione dei vasi all'ingresso in stanza**

**Priorita':** ✅ **RISOLTA E DIMOSTRATA (F4b.4)**

**Segnalata dall'utente**: *"G1 e G2 vedono 2 vasi rotti → si siedono al falò → G2 entra e ne rompe 1 → G1
entra e vede l'animazione di ENTRAMBI"*. **Causa: `ReapplyCurrentScene` gira più di una volta per ogni
caricamento di livello** *(`LoadSnapShot` viene invocato due volte per load)*, **e ogni passata che trova un
oggetto ancora in piedi lo rompe e ne riproduce l'animazione** ⇒ **4 `Use()` per 2 vasi in un ingresso, 2
animazioni per vaso**. **Fix (F4b.4)**: **una guardia in `BreakableObjectReceive_Patch.Prefix`** *(l'unico
posto che chiama `Use()`)* — se `___destroyed` è già `true`, **ritorna `false` senza applicare**. **Non tocca
rete, `applied`, `pending` né `Forget`.** ⚠️ **MA LA GUARDIA NON È DIMOSTRATA, e va detto**: **il log NON può
provarla**, perché `[BreakSync] rottura applicata` è stampato **DOPO** la chiamata e **`ApplyToLoadedObject`
ritorna `true` anche quando la guardia sopprime** ⇒ **restano 2 righe per GUID per ingresso, e non si
distingue "due applicazioni" da "una applicazione + una soppressa"**. **E il meccanismo che la aggira esiste,
nel GIOCO** *(IL)*: `SetCurrentPersistentState` scrive `destroyed` dallo snapshot, poi `IsBroken()` è falso
*(il `BreakableManager` non viene ripopolato al load)* ⇒ **IL_0040 `call Restore()`** ⇒ **`Restore()` rimette
`destroyed = false`**. **Quindi la premessa del fix** *("la seconda passata trova gli oggetti già rotti")*
**vale solo se nessuno li riporta in piedi fra le due passate.** **L'utente conferma che il sintomo è
sparito** *("tutto funziona alla grande")*, **ma serve una misura**: una riga di log **quando la guardia
sopprime**. ⚠️ **LIMITAZIONE NOTA**: **`pending` NON viene ripulito dal falò** *(il guard `loadingSnapshot` in
`Forget` salta solo la parte `pending`; il falò non passa da `LoadSnapShot`)*. **È il PROGETTO della voce 33,
ma diventa un'eco** quando il vaso era già rotto in questa vita prima del falò. **Toccarlo è rischioso**: è lo
stesso `Forget` che ha causato "l'hook non dipinge". **Da affrontare col falò (voce 4).** ⭐ **E ORA LA
DIMOSTRAZIONE, con la misura aggiunta apposta**: la guardia **logga quando rifiuta** *(una riga che avevo
messo proprio perché `rottura applicata` NON poteva provarla: è stampata dal chiamante DOPO, e riporta
successo in entrambi i casi)*. **Nei log compare 3 volte** — *G1 `0d384626` L1115 · G1 `1ba697d8` L1409 · G2
`9edafe04` L1104*, **una per GUID per ingresso** — **e il conteggio `rottura applicata` per GUID per ingresso
è sceso a 1**. **La prova più pulita è G2, che non ha mai rotto quei vasi di sua mano.** ⚠️ **CORREZIONE DI
ORDINE, dall'analisi in IL**: **il passaggio rifiutato è il PRIMO, non il secondo** *(`L1087 OnLevelLoaded ·
L1095 APPLICATA · L1098 Loaded level · L1115 SALTATA · L1116 APPLICATA`)* ⇒ **il gioco rimette `destroyed =
false` fra l'applicazione del pass A e l'arrivo del pass B.** **E il meccanismo è confermato**: `IsBroken()`
legge `BreakableManager.ContainsBreakable` *(non `destroyed`)* ed è falso al load ⇒ **`Restore()`**, **che è
l'unica cosa che azzera `destroyed`** ed è chiamato da **soli due metodi** *(`OnUsePrieDieu`,
`SetCurrentPersistentState`)*. Vedi §12

### Voce 40 — 🟡 **`ReapplyCurrentScene` gira due volte per caricamento di livello**

**Priorita':** 🟢 **nota tecnica — innocua con la guardia**

**Due invocazioni**: *(1)* il postfix di `PersistentManager.LoadSnapShot` *(il fix vero)*; *(2)* **la "seconda
possibilità"** `MultiplayerLateUpdate_Patch`, armata da `Multiplayer.OnLevelLoaded` *(⚠️ **era strumentazione
temporanea mia, aggiunta per misurare quale delle due dipingesse** — il log ha detto che dipingono
**entrambe**)*. **Con la guardia della voce 39 è innocua** *(la seconda passata trova gli oggetti già rotti e
non fa nulla)*, **quindi è stata TENUTA di proposito**: rimuoverla sarebbe stato un secondo azzardo nello
stesso momento, **e le due non scattano nello stesso istante** *(la prima può girare mentre gli oggetti si
stanno ancora costruendo)*. **Se un giorno si vuole semplificare, è un candidato**, con la raccomandazione di
farlo **dopo** aver verificato che la prima passata basti da sola. Vedi §12

### Voce 41 — 🔴 **`ProgressManager.Update` è senza `try/catch` né `finally`: UNA eccezione UCCIDEVA la sincronizzazione PER SEMPRE**

**Priorita':** ✅ **RISOLTA — con l'eccezione isolata E loggata**

**Era il difetto più pericoloso del progetto.** **Verificato nel codice** *(`ProgressManager.cs:58-69`)*:
`CurrentlyUpdatingProgress = true;` → **i 14 helper di `ApplyProgress`** → **`= false`**, **senza `try` né
`finally`**. **Fuori c'è un `try/finally` in `Multiplayer.cs:154` che però resetta `PerformanceProbe`, NON
questo flag.** ⇒ **una sola eccezione in un helper lasciava il flag `true` PER SEMPRE**, e si portava dietro:
**(a)** `BreakableObjectHelper` **sopprimeva OGNI futura rottura**, **(b)** **~29 guard** negli altri helper
*(`BeadHelper`, `PrayerHelper`, `RelicHelper`, `QuestItemHelper`, `CollectibleHelper`, `HeartHelper`…)*
**sopprimevano l'invio di TUTTA la progressione**, **(c)** **la coda non si svuotava e rilanciava ogni
frame**. **⇒ il gioco restava vivo ma il co-op smetteva di sincronizzare**, senza un crash e senza un errore
evidente. **Probabilità: BASSA** *(0 occorrenze nei log)* **ma i percorsi sono verificati**: `ApplyProgress`
fa **`int.Parse`** su un id **che arriva dalla rete** e una lookup che può uscire dalla tabella — **e la
difesa era SOLO il parsing** *(`IsTableId` / `char.IsDigit`)*, **non un catch**. ⚠️ **Il progetto lo sapeva**:
il commento in `BreakSyncMemory.IsTableId` lo dice **esplicitamente**, e **non era mai diventato un fix**. **✅
FIX**: **`try/finally` con `Clear()` E il flag abbassato DENTRO il `finally`** *(il `Clear` deve stare lì: una
coda parziale va comunque scartata, o la stessa entry che lancia verrebbe ritentata ogni frame)* **+ `catch`
per-elemento in `ApplyProgress`, CON LOG dell'id** *(un `catch` silenzioso nasconderebbe l'unica prova che
spiega perché un aggiornamento è sparito)*. **Nessun bump.** Vedi §12

### Voce 42 — **`AddInteractedObject` senza dedup**

**Priorita':** ✅ **RISOLTA — `if (!Contains)`**

**`List.Add` senza controllo**, e **il file su disco lo mostrava: 8 voci dove 4 erano distinte**. **Innocuo in
effetto** *(quella lista finisce in `savegame_N_modded.save`, che il gioco non legge mai — voce 36)*, **ma è
il tipo di innocuo che smette di esserlo il giorno in cui qualcuno conta le voci o confronta due liste per
uguaglianza.** ✅ **Fix**: `IsNullOrEmpty` → **`Contains`** → `Add`. Vedi §12

### Voce 44 — **La MORTE di un giocatore respawnava i nemici SOLO per chi muore, e lasciava le due macchine DIVERGENTI**

**Priorita':** ✅ **RISOLTA E VERIFICATA IN GIOCO**

**Misurato prima del fix**: `PLAYERDEAD` + `DEAD_SCREEN` su una macchina e **zero occorrenze sull'altra**; il
respawn avveniva **solo sulla macchina morta** *(G2 rientrava con `5/7/5`, G1 restava a `3/7/3`)*, e **la
convergenza era avvenuta PER CASO** — *per un annuncio di falò che era per un'ALTRA stanza*. ⇒ **non era
"ingiusto per gli altri": era un BUG DI DIVERGENZA.** **✅ FIX IN DUE PEZZI**: *(1)* **il rilevamento**
*(`Prefix` su `Penitent.Respawn` che alza un flag, `Finalizer` che lo abbassa — **non** un Postfix: se
`Respawn` lancia, un Postfix non girerebbe e il flag resterebbe alto per sempre)*; *(2)* **la soppressione**
*(un `Prefix` su `EnemySpawner.Reset` che ritorna `false`, **e una guardia in `OnDeathStateReset`**)*. **⭐ E
le due metà leggono lo STESSO flag**: *il gioco non svuota la lista E la mod non dimentica le morti* — **la
coerenza è per costruzione, non per disciplina.** ⚠️ **E il Postfix del `Reset` gira COMUNQUE** *(in Harmony i
postfix vengono sempre eseguiti, anche quando un prefix ritorna `false`)*: **senza la guardia, il gioco
avrebbe tenuto i nemici morti e la mod li avrebbe dimenticati** — *l'errore della voce 24 al contrario.* **✅
VERIFICATO IN GIOCO, con un controllo interno elegante**: *dopo entrambe le morti, G2 e G1 danno **`3/7/3` e
`3/7/3`** — lo stesso numero, e uguale allo stato **pre-morte**; e `5/7/5` ricompare su G2 **solo dopo il falò
vero** ⇒ `3/7/3` = lista conservata, `5/7/5` = lista azzerata.* **E `consumedSpawns azzerati` in gameplay =
ZERO** *(le 2 superstiti sono `MainMenu`)*. ✅ **E il confine "mondo sì / benefici no" è misurato**: *le fiale
erano a **0** su entrambi; dopo la seduta **solo il seduto** è salito a `2/2`, il ricevente è rimasto `0/2`.*
**⚠️ Il refill (vita/fiasche/fervour) NON è toccato, ed è voluto**: sta in `PersistentManager.RestoreStored`,
una chiamata **diversa** dentro `Respawn` *(`Reset` @IL_000A, `RestoreStored` @IL_0014)*. **Vedi voce 46 per
la divergenza residua.** Vedi §12

### Voce 47 — **La riga dei nemici occupava METÀ del log** — `LogDiagnosticSnapshot` stampava una riga per nemico ogni 0,5 s

**Priorita':** ✅ **RISOLTA**

**Misurato**: su G2 *(`194 KB`) *`[EnemyTick]` = **640 righe (46,1%)*** e con `[EnemyAnim]` 68 e `[EnemyTree]`
12 **le tre insieme erano il 52% delle righe e ~70% dei byte**; su G1 **zero righe di nemici** *(era in una
stanza senza nemici)* ⇒ *tutto il rumore da una macchina sola*. **E la distanza media fra due `[EnemyTick]`
era 1,5 righe** ⇒ *per nemico, in continuazione*. **LA CAUSA, una riga**: `LogDiagnosticSnapshot()` era
chiamata **dentro il blocco che scatta ogni `RESCAN_INTERVAL` (0,5 s)** ⇒ *3 nemici × 2 volte al secondo ≈ 6
righe/s ⇒ 640 in ~107 s, i conti tornano esatti.* ⚠️ **E LA CORREZIONE OVVIA SAREBBE STATA SBAGLIATA**:
*alzare `RESCAN_INTERVAL`* — **perché il rescan NON serve al logging**: *serve a **scoprire gli spawn point
creati DOPO il level load**, ed è esattamente ciò che rende visibile un nemico rinato sulla macchina che lo
possiede* ⇒ **rallentarlo avrebbe comprato ordine pagandolo con la correttezza**, *e quel comportamento
l'avevamo appena misurato.* ✅ **LA CORREZIONE GIUSTA**: **separare le due cadenze** — il rescan resta **0,5
s**, **la scrittura** passa a **`DIAGNOSTIC_LOG_INTERVAL = 10 s`**, con la guardia **in testa a
`LogDiagnosticSnapshot`** *(non al sito di chiamata: **così la cadenza è una proprietà della funzione che
scrive**, non una cosa da ricordare in ogni futuro chiamante)* **e il timer azzerato sul cambio scena**,
*altrimenti il primo snapshot di una stanza nuova arriverebbe dopo 10 s.* **`[EnemyAnim]` e `[EnemyTree]` NON
toccati**: *scrivono solo quando qualcosa cambia davvero.* **Effetto atteso: da ~640 righe a ~32.** Vedi §12

### Voce 48 — 🔴 **Un CADAVERE poteva TENERE il boss — e il boss diventava INVULNERABILE**

**Priorita':** ✅ **RISOLTA** *(2 fix)*

**La causa del "boss che gira intorno a un morto" NON era "il boss che spegne l'IA"**:
*`EnemyBehaviour.GetTarget()` non guarda mai se il target è vivo e fa fallback sul **Penitente LOCALE**, e il
giocatore remoto **non è un `Penitent`*** ⇒ *il boss insegue il cadavere che sta sulla macchina che lo
possiede.* ⚠️ **E la regola della proprietà lo manteneva**: *`ShouldOwnByProximity` confrontava solo
distanze*, **e un morto smette di mandare la posizione** *(il pacchetto parte solo se la posizione cambia)* ⇒
*la sua ultima posizione diventa stale in 0,3 s e il ramo "nessuna posizione remota" **TENEVA** l'enemy.* ✅
**FIX 1**: *`ShouldOwnByProximity` ritorna `false` se il giocatore locale è morto* *(verificato in IL:
`IsLocalPlayerDead` @9 prima di `Vector2::Distance` @30)*. 🔴 **MA RESTAVA IL BUCO GRAVE, trovato
dall'agente**: **«rifiutare non è rinunciare»** — *l'unico ramo che **cede** è `peerCloser`, che esige
`nearestRemote + 2,5 < localDistance`* ⇒ **morendo ADDOSSO al boss, dove il vivo accanto al cadavere non è 2,5
unità più vicino, l'host teneva il boss**; **e siccome `<Boss>::Damage` ESCE SUBITO se
`Core.Logic.Penitent.Stats.Life.Current <= 0`** *(**18 classi di boss su 21**; eccezioni HighWills,
PontiffHuskBoss, Lesmes)*, **ogni colpo del vivo veniva buttato via** *(i colpi arrivano **solo all'host**)* ⇒
**boss INVULNERABILE**, *e il vivo non poteva accorgersene perché **la barra è locale e solo il proprietario
ne vede una veritiera**.* ✅ **FIX 2**: *`DecideOwnership` cede **subito** se il locale è morto e lo stato è
`OwnedByLocal`* — **senza `OWNER_HOLD_SECONDS`, di proposito**: *quella attesa esiste per impedire a due
**vivi** di scambiarsi l'enemy molte volte al secondo; un cadavere non ha niente da stabilizzare.* **✅ E il
cambio di proprietà era GIÀ automatico per prossimità** *— host-arbitrato, ricalcolato ogni 0,05 s* ⇒ *il
lavoro era solo renderlo consapevole della morte.* ⚠️ **E una scoperta collaterale**: **`BossScripting`
esiste** *(`Gameplay.GameControllers.Bosses.BossScripting`, con un campo `PietyMonster Boss`)* **e la
mappatura non l'aveva** — *ma la sua `OnDead()` si aggancia a `Entity::add_OnDeath` (la morte **del boss**),
non a `Penitent.OnDead`* ⇒ *conferma che i boss non sanno nulla della morte del giocatore.* **Da testare**:
*`SettleStuckDeaths` non è esclusa per i boss, e `EnemyDamagePatch` è un **Postfix** ⇒ la copia locale del
boss sul non-proprietario potrebbe morire in 1,5 s e propagare la morte senza controlli di proprietà*
*(premessa inferita, non verificata)*. Vedi §13


### Voce 49 — 🔴 **Il nemico resta FERMO PER SEMPRE se l'host entra nella stanza ed esce subito** — segnalato dall'utente, riproducibile

**Priorita':** 🔴 **NUOVA — diagnosi VERIFICATA, fix ancora da fare**

**Il sintomo (parole dell'utente)**: *"se un giocatore non-host sta in una stanza la simulazione dei nemici
funziona bene, ma appena l'host entra ed esce subito dopo, quelli **non ancora triggerati** smettono di
funzionare per il non-host."* ⭐ **LA DIAGNOSI, verificata punto per punto nel codice**: **1)** il client è
solo nella stanza, `OwnershipKnown = false` ⇒ **il self-heal gli dà il nemico e lui simula** ✅ · **2)**
**l'host ENTRA**: i nemici entrano nella sua lista `_ids` *(`_ids` = la scena **dell'host**)* ⇒
`DecideOwnership` gira *(solo sull'host)* e se si ritiene più vicino **se li prende** ⇒ manda il token ⇒ sul
client **`OwnedByLocal = false` + `PauseEnemy` + `OwnershipKnown = true`** ← **IL LATCH SCATTA** · **3)**
**l'host ESCE**: quei nemici non sono più in `_ids`, quindi **l'host non può più arbitrarli e non manda la
restituzione** *— e il token viaggia con `relayToScene`, filtrato per scena* · **4)** ⇒ **il client resta con
`OwnershipKnown = true`, e il self-heal `else if (!IsHost && !previous.OwnershipKnown)` NON SCATTA** ⇒
**puppet per sempre, senza via di recupero** *(`ReceiveEnemyUpdate` → `ResumeEnemy` richiederebbe un pacchetto
che non arriverà mai)*. ⭐ **E il LATCH SOPRAVVIVE**, verificato: `_lastSent.Clear()` a `:1292` sta in
`LevelLoaded(scene)` *(solo se **questa** macchina cambia scena)* e a `:1401` in `OnEnemiesSpawned()` *(il
client non rispawna)*; **l'ID non cambia** *(il peer nemmeno riscansiona: `else if (IsHost)`)*; `HandleOf` è
**FNV-1a della stringa**; `EnemyState` è una **classe** ⇒ le mutazioni persistono. ⭐ **E LA CAUSA
STRUTTURALE**: **la proprietà è MEMORIA DI SCENA per costruzione** *(`LevelLoaded` azzera
`_lastSent`/`_ids`/`_puppetEnemies`/…, col commento "ids do not survive a scene change")* ⇒ **chi si muove
dimentica, chi resta NO** ⇒ **il reset è ASIMMETRICO.** ✅ **FIX SCELTO — (B): l'host restituisce ciò che
possiede mentre esce**, in `Multiplayer.OnLevelUnloaded` *(:125)* **PRIMA di `SendLeaveScene()` (:131)** ⚠️
**L'ORDINE È TUTTO**: *il filtro di scena è quello del **MITTENTE** (`Server.cs:768`), e
`receivePlayerLeaveScene` fa `current.sceneName = ""`* ⇒ **token prima = ARRIVA, dopo = MAI** *(e
`QueueMesssage` è un'unica coda, quindi vale l'ordine di accodamento)*. ✅ **E il momento è garantito**:
`LevelManager` **non ha** `OnLevelUnloaded` *(confermato in IL)*; `ModLoader.cs:86` mappa l'uno su
`OnBeforeLevelLoad`, invocato in `<ChangeLevelCorrutine>…::MoveNext()` **prima del load** ⇒ **nemici ancora
validi e `_ids` non ancora azzerato.** ⚠️ **SCARTATA LA (A) COME TIMER, e in DUE modi**: *il **token è
pubblicato PRIMA** del gate `if (!enemy.gameObject.activeInHierarchy) continue;` (`:2839-2840`)* ⇒ **un nemico
inattivo riceve il token e ZERO aggiornamenti di stato** ⇒ *un timer "non sento più l'host" darebbe un **FALSO
POSITIVO** — e **"non ancora triggerati" È la popolazione del bug***; **e** `SendPosition` è **change-gated**
*(`MainPlayerManager.cs:25`, cutoff 0.03)* ⇒ **anche un host FERMO sembra andato via in 0,3 s** ⇒ *si
ruberebbe il nemico a un host fermo.* ⭐ **E la logica giusta esiste già, ma è consultata SOLO dall'host**:
`ShouldOwnByProximity` e `PeerIsCloser` trattano `nearestRemote == float.MaxValue` come *"l'altro è in
un'altra scena"* — **ma girano dentro `DecideOwnership`, cioè dietro `if (IsHost)`** ⇒ **è letteralmente il
pezzo mancante.** **Da NON confondere con la voce 13** *(migrazione host)*, che è un altro caso. Vedi §14

**ESITO:** RISOLTA nel commit `0cbfe7b` — *"l'host restituisce i nemici mentre esce dalla scena,
PRIMA di annunciare che e' uscito"*. Il fix e' `EnemySyncManager.ReleaseOwnedEnemiesBeforeLeaving()`,
chiamato da `Multiplayer.OnLevelUnloaded` **prima** di `SendLeaveScene()` — *l'ordine e' tutto, perche'
il server filtra la proprieta' per la scena del MITTENTE.*


### Voce 50 — gli id degli spawn point divergevano (nome Unity col contatore di istanza)

**Priorita':** ✅ RISOLTA

**Sintomo**: 9 `[Error]` su G1 e 10 su G2, **tutte `IMPRONTA DIVERSA`**, con
`host=-1418051589` vs `locale=-1030017804` e *"spawn point locali: 7, id locali: 5"*.
**Causa, verificata in codice**: `BuildSpawnPointId` usava `point.gameObject.name` **grezzo**,
mentre `BuildFreeEnemyId` toglieva il suffisso di istanza. Unity nomina le istanze `SPAWN (11)`,
`SPAWN (15)`, ... e quel contatore **cambia fra processi** ⇒ le due macchine costruivano id
**diversi** per lo **stesso** spawn point: **2 id su 5 divergenti**.
**Effetto misurato**: quelle chiavi non ricevono **mai** una rivendicazione risolvibile
*(`remoto = ?`/`False`, 6/6 e 9/9 campioni)*, mentre le altre si'' *(`remoto=True`)*.
In una stanza piena sarebbero **2 nemici su N rotti del tutto**: posizione **e** token persi,
perche'' `FindIdOfHandle` fallisce e il percorso esce.
**Fix**: `StripInstanceHash` → `StripInstanceSuffix`, che gestisce **entrambi** i pattern —
l''hash del gioco *(cifre finali)* e il contatore di Unity *(` (N)`)* — con le parentesi per prime.
Usato da **entrambi** i builder.
**Verificato su 9 casi reali**, tutti corretti, inclusi i due che rompono un''implementazione
ingenua: `Fool(Clone)` *(nessuno spazio)* e `123` *(tutte cifre)* restano intatti.

### Voce 51 — il nuovo proprietario impone la sua posizione, e nessuno lo corregge

**Priorita':** 🔴 NUOVA — causa verificata, fix da progettare

**Il sintomo (utente)**: *"i nemici vengono slittati nelle posizioni iniziali, solo di posizione,
l''AI e i target funzionano, vengono semplicemente spostati"* — *"qualche volta funziona il caso che
voglio [...], qualche volta no"*.

**LA CAUSA, verificata in codice**
```
Il cedente NON trasmette la sua posizione quando cede:
   `SendEnemyOwnership` porta SOLO handle (4 byte) + 1 bool  => NESSUNA POSIZIONE
   e il gate di trasmissione e''  `if (!previous.OwnedByLocal) continue;`
=> chi perde la proprieta'' SMETTE di trasmettere
=> chi la riceve NON ricevera'' MAI piu'' un pacchetto correttivo => la sua copia non e'' mai corretta
E `PlaceEnemy` NON viene MAI chiamato per un nemico appena diventato nostro
   (il ciclo salta i posseduti: righe 2223-2224)
   e in tutto il file non esiste una riga che riporti la copia locale alla posizione nota
   quando si prende la proprieta''
```
**E la macchina che RICEVE la proprieta'' scrive la posizione SBAGLIATA** — *non da un
`_remoteTargets` stantio, ma dalla **propria istanza appena ricreata dal gioco allo spawn**, che
impone **trasmettendola**.*

**⚠️ E IL NOSTRO FIX DELLA VOCE 49 HA TOLTO UNA PROTEZIONE ACCIDENTALE**
```
PRIMA: il peer restava `OwnedByLocal=false` con la FISICA SPENTA
       => NON poteva muoversi ne'' trasmettere => NON contraddiceva mai l''host
          (ma restava fermo per sempre: era la voce 49)
ORA:   riprende l''AI sul posto e RITRASMETTE dalla propria copia
       => che, se ha appena caricato la scena, e'' ALLO SPAWN
```
**Il bilancio resta positivo** *(un nemico fermo per sempre e'' peggio di uno spostato)*, **ma va
detto: il fix ha esposto questo difetto invece di crearlo.**

**⚠️ MA IL TELETRASPORTO NON E'' RIPRODOTTO NEI LOG**
```
escursione massima del nemico mobile: 4.0 unita'' su 281 campioni
FOLLOW_SNAP_DISTANCE = 6  =>  NON SCATTA MAI
=> solo Lerp(..., 0.25) = SCIVOLAMENTO GRADUALE, nessun teletrasporto
=> e in TUTTI i travasi presenti la posizione resta invariata a -715.1
```
**Il valore di spawn e'' pero'' reale e dimostrato**: `x = -715.1`, identico sulle due macchine,
**14 volte su 14** ricariche di `D01Z01S01`. **E ogni ritorno a `-715.1` avviene sulla macchina che
ha appena ricaricato la scena** ⇒ *e'' il respawn del **gioco**, non uno snap di rete.*

**⚠️ PRIMA DI FIXARE SERVE UNO STRUMENTO** — *il referto dichiara il limite*: *"l''unico anello non
osservabile e'' il contenuto di `_remoteTargets` al momento della rivendicazione: per chiudere oltre
ogni dubbio servirebbe una riga di log a 3101-3105 / 2875-2879 con `id` + `transform.position`
locale + `_remoteTargets[id]`"*.
**⚠️ E in questa stessa indagine ho sbagliato l''ipotesi DUE volte** *(prima "il valore e'' stantio",
poi la sorgente)*: **meglio uno strumento prima del fix.**

**E LA VOCE 11 NON C''ENTRA**: *`StartBehaviour`/`EnablePhysics` non scrivono posizioni; nei log la 11
si vede come **churn di proprieta''** (`owner=io` E `remoto=True` insieme), quindi e'' **conseguenza**,
non causa.*

### Voce 54 — BLOCCO TOTALE DEL GIOCO dopo un'esecuzione (516 eccezioni non gestite)

**Priorita':** ✅ RISOLTA *(commit `22d9fec`)*

**Sintomo (utente)**: *"G1 uccide un nemico, esce il prompt di esecuzione a G2, G2 esegue
l'esecuzione, diventa invisibile nel suo schermo e fermo a G1; nel client di G2 non era possibile
fare nulla, completamente bloccati tutti i tasti"* — costretto a chiudere il gioco a forza.

**Causa TROVATA e verificata in IL: era NOSTRA.** *`DestroyDeadEnemy` faceva
`Object.Destroy(enemy.gameObject)` mentre il gioco teneva ancora `Execution.ExecutedEntity` e la macchina a
stati della prop di esecuzione* ⇒ *`StuntBehaviour.OnStateUpdate` chiama `GetComponent<EnemyBehaviour>()` e
l'eccezione nasce DENTRO l'icall nativo (`GetComponentFastPath`) ⇒ l'oggetto era DISTRUTTO.* **E diventa un
diluvio perché** *la via d'uscita dallo stato `Stunt` è l'ULTIMA istruzione di `OnStateUpdate`* ⇒ *l'eccezione
la salta ⇒ lo stato non esce mai ⇒ 514 eventi, 78% di quel log.* ⚠️ **E il blocco dei tasti NON era il
diluvio** *(fps 59.5 dentro)*: *la coroutine di esecuzione `SetBlocker(EXECUTION,true) → KillEntity() →
ShowPlayer → SetBlocker(false)` — `KillEntity()` lancia sull'entità fantasma ⇒ la coroutine MUORE ⇒ penitente
invisibile, `IsOnExecution` mai resettato, **blocker ON per sempre**.* **Prova**: `EXECUTION enabled` e
**nessun** `disabled` in tutto il log *(su G1, gruppo di controllo: 1514 acceso, 1564 spento)*. **FIX**:
`SetActive(false)` invece di `Destroy`. Vedi "Voce 54" in fondo


### Voce 57 — il FANTASMA: la morte via SALUTE non disattivava il cadavere

**Priorita':** ✅ RISOLTA *(commit `0769fd6`)*

**Riprodotto dall'utente**: *"se uno ha il prompt di esecuzione su un nemico e l'altro lo uccide, il nemico diventa un fantasma non colpibile con AI spenta"*. **Causa verificata con l'id completo** *(`sp\|D01Z01S03\|SPAWN\|-545.00\|14.00`)*: **la morte arriva su DUE canali, e vince il SALUTE** — *`ReceiveEnemyUpdate` (currentLife=0) → `SetHealth(0)` + `KillEnemyLocally` → `ForgetEnemy` **senza** `SetActive(false)`; l'annuncio (`DestroyDeadEnemy` → `SetActive(false)`, **l'unico che disattiva**) arriva **dopo** che l'id è sparito ⇒ non scatta ⇒ cadavere `attivo=True`.* **L'id COMBACIA su G1 e G2** ⇒ **NON è la voce 45** *(le liste coincidevano: zero `non in _ids`, zero IMPRONTA)*, **è un buco residuo della voce 54.** **FIX**: `KillEnemyLocally` disattiva il GameObject **dopo** `ForgetEnemy` — *sul peer l'AI è in pausa, la clip di morte non parte mai da sola, e disattivare subito è la fine corretta; l'host continua a giocare la propria morte.* **E ora sappiamo anche il nome della parata**: **`WheelCarrier_parryReaction`** *(stato 504284770, confermato su entrambe)* — *serve alla voce 35.* Vedi "Voce 57" in fondo


### Voce 45 — chiusa dal fix 45

**Priorita':** ✅ RISOLTA dal commit `e4269fd` *(il rescan su ENTRAMBE)*

⭐ **PROVA 10 su 10**: *ogni aumento del conteggio dell'host **dentro la visita** è preceduto da `annuncio RICEVUTO` + `RespawnDeadEnemies`; **aumenti dentro la visita: HOST = 10, CLIENT = 0*** *(il client è monotono non crescente).* **Stessa stanza, stesso istante: host 22 vs client 18.** **CAUSA IN CODICE**: `UpdateCore` fa `else if (IsHost) { RegisterEnemies(_scene); RefreshFingerprintIfDirty(); }` ⇒ *il peer cambia `_ids` **solo ai load o per potatura**, quindi **non può ricrescere***. **E il falò fa rinascere i nemici**: *l'host li ri-registra entro 0,5 s, il client no.* ✅ **E "uscita e rientro fixa" è CONFERMATO**: *`LevelLoaded` azzera e `OnEnemiesSpawned` ri-registra **e ordina** su entrambe* ⇒ *G1 chiude a 18 e riparte a 22, G2 pure.* **⇒ FIX LOCALE** *(il peer deve poter appendere come l'host)*, **ma va verificato con due marker che oggi mancano**: *una riga quando `ResolveByHandle`/`FindIdOfHandle` falliscono* *(è l'istante in cui l'aggiornamento viene scartato **in silenzio**)*, **e `IMPRONTA DIVERSA` che stampi anche i conteggi del PEER e il primo id presente su un lato solo.** ⚠️ **Effetto collaterale misurato**: `IMPRONTA DIVERSA` **47 su G1 / 62 su G2**, **tutte in `D01Z01S03` e ZERO in `D01Z02S01`** ⇒ *non è un falso allarme: tace quando le liste coincidono.*


### Voce 46 — chiusa dal fix 45

**Priorita':** ✅ RISOLTA dal commit `e4269fd` *(il rescan su ENTRAMBE)*

**Causa**: *la **lista VIVA** `_ids` si accorcia in modo asimmetrico con le morti **e non può ricrescere sul peer*** *(voce 45: il rescan che appende gira solo sull'host)*. ⇒ *non è una divergenza di spawn point (i conteggi coincidono: 5/5 e 9/9) ma di **lista viva**.* **⇒ Si chiude insieme alla voce 45**, non separatamente.


### Voce 53 — chiusa dal fix 45

**Priorita':** ✅ RISOLTA dal commit `e4269fd` *(il rescan su ENTRAMBE)*

**Parole dell'utente**: *"i nemici della seconda stanza quando spuntano ad un giocatore non vengono condivisi all'altro; sono nemici che immagino spawnino sul momento quando si triggerano ad un evento locale; G2 non poteva né vederli né colpirli; l'evento è stato andare in una patch di fango."* ⭐ **La voce 45 descrive esattamente questo**: *`_ids` è appeso dal **rescan periodico**, che gira **SOLO sull'host*** ⇒ *sull'host un nemico creato fuori dai pass di spawn compare **entro ~0,5 s**, sul peer `_ids` cambia **solo ai load*** ⇒ **il peer è CIECO.** ✅ **È la spiegazione più probabile, ed è già mappata.** **⚠️ E potrebbe spiegare parte degli `IMPRONTA DIVERSA` residui** *(l'host ha un id che il peer non ha)*. **Da verificare**: *l'id di questi nemici è `en\|...` o `sp\|...`, e ha un nome col suffisso?*


### Voce 51 — la posizione al passaggio di proprieta'

**Priorita':** ✅ RISOLTA *(commit `3f5ce69`)*

⭐ **Verificato in codice**: `SendEnemyOwnership` porta **solo handle + 1 bool — NESSUNA POSIZIONE**, e il gate di trasmissione è `if (!previous.OwnedByLocal) continue;` ⇒ **chi cede smette di trasmettere, e chi riceve non riceverà mai più un pacchetto correttivo** ⇒ *la sua copia non viene mai corretta.* E `PlaceEnemy` **non viene mai chiamato per un nemico appena diventato nostro** *(il ciclo salta i posseduti)*: *non esiste una riga che riporti la copia locale alla posizione nota quando si prende la proprietà.* ⚠️ **E il nostro fix di oggi ha TOLTO UNA PROTEZIONE ACCIDENTALE**: *prima il peer restava `OwnedByLocal=false` con la **fisica spenta** ⇒ non poteva muoversi né trasmettere; ora riprende l'AI sul posto e **ritrasmette dalla propria copia**, che se ha appena caricato la scena è **allo spawn**.* ⚠️ **MA il teletrasporto NON è riprodotto nei log**: *escursione massima 4.0 unità su 281 campioni, e `FOLLOW_SNAP_DISTANCE=6` non scatta mai* ⇒ *solo `Lerp(0.25)`, **scivolamento, nessun teletrasporto**; e in tutti i travasi presenti la posizione resta invariata.* ⇒ **⚠️ PRIMA DI FIXARE SERVE UNO STRUMENTO**: *il referto dichiara che l'anello decisivo **non è osservabile** — «serve una riga di log con id + `transform.position` locale + `_remoteTargets[id]` al momento della rivendicazione»*. Vedi "Voce 51" in fondo

**ESITO:** `AdoptLastKnownPosition` posiziona il nemico sull'ultima posizione nota al momento
della presa, nei tre punti (host-prende, peer-prende, self-heal). Marker `[Proprieta'] adotta
la posizione`.


### Voce 55 — l'annuncio parte prima del reset locale

**Priorita':** ✅ CHIUSA — **non è un problema** *(confermato dall'utente)*

**Perché è chiusa.** Il referto diceva: *"la divergenza è causata dalla 45, non dall'ordine"*.
Con la 45 il peer ricresce su `_ids` al rescan, quindi la finestra "annuncio prima del reset" si
auto-risana. E l'utente ha confermato col test: si è seduto più volte al falò senza problemi — il
gap è di POCHI MILLISECONDI, e l'animazione di seduta al falò lo copre. **Toccare il flusso del
falò (già verificato) per un ordine cosmetico sarebbe un rischio senza beneficio.**


### Voce 35 — l'aggro sul non-proprietario (il PROXY)

**Priorita':** ✅ RISOLTA *(commit `a307a4c`)* — **confermato dall'utente**: *"35 totalmente scomparsa, il nemico adesso non perde più l'aggro"*.

**IL PROBLEMA**: il gioco ha UN SOLO bersaglio di aggro (`Enemy.Target`), e TUTTI i siti che lo scrivono nominano il Penitent LOCALE. Il giocatore remoto e' un GameObject nudo senza collider e senza Entity: la vista del gioco (`VisionCone.CanSeeTarget`, che pretende un collider sul layer "Penitent") non poteva vederlo. Quindi il nemico aggroava il PROPRIETARIO (spesso lontano) e ignorava il giocatore vicino.

**IL FIX**: un PROXY — un GameObject sul layer "Penitent" con un BoxCollider2D trigger e NIENTE Entity/IDamageable, posizionato sulla posizione del remoto; `ApplyAggroTarget` punta `Enemy.Target` al giocatore piu' vicino (proxy o Penitent locale). **Il DANNO NON e' toccato**: ogni macchina ferisce il proprio Penitent col pipeline locale del gioco (verificato in IL).

**E LA 35a (ParrySync) E' STATA RIMOSSA** *(commit `82b2bc0`)*: congelava i nemici del proprietario, perche' `GotParry` si resetta SOLO con `WheelCarrier.Damage(Hit)` e la parola data da remoto lasciava l'IA spenta. La reazione di parata e' gia' sincronizzata owner->puppet, e col proxy nasce da un ingaggio VERO.


### Voce 51 — chiusa: la posizione al passaggio di proprieta'

**Priorita':** ✅ RISOLTA — **confermato dall'utente**: *"abbiamo risolto entrambe la 35 e lo spawn"*.

**La storia (5 tentativi, e ognuno ha insegnato qualcosa)**:
1. **adopt al passaggio** *(3f5ce69)* — corretto ma insufficiente, e mancava nel ramo prima-volta.
2. **il defer** *(b3c6f8c)* — causo' una REGRESSIONE CRITICA: i nemici SPENTI quando entrava solo l'host (`owner=nuovo` 52%, `enabled=False` x22).
3. **il defer a tre lati** *(689ad64)* — posizione nota / nessun peer raggiungibile / attesa oltre 1,5 s. Risolve la regressione.
4. **il check della STANZA** *(4433d70, idea dell'utente)* — `OtherPlayerInMyScene` usa `AllConnectedPlayers` + `PlayerStatus.CurrentScene`: "c'e' qualcuno nella mia stanza? allora aspetto le sue posizioni."
5. ⭐ **IL GATE DI ATTIVITA'** *(df45f72f, la causa vera)* — l'invio della posizione era gated su `activeInHierarchy`: un nemico FUORI DALLA VISUALE del proprietario non veniva MAI trasmesso, quindi l'altra macchina non aveva nulla da adottare e ripiegava su SPAWN. Due commenti nel codice si contraddicevano, e il gate era costruito su quello sbagliato.


### Voce 53 — i nemici RIVELATI da un evento locale

**Priorita':** ✅ RISOLTA — **confermato dall'utente**: *"funziona ad entrambi"*.

**Il problema**: un nemico rivelato da un evento LOCALE (il fango) esisteva e agiva SOLO sulla macchina che l'aveva triggerato. Non un desync — un nemico che esiste da una parte sola. E per il MudCrawler, che morde, era un BLOCCO DI PROGRESSIONE (la stanza non si libera).

**La scoperta**: il MudCrawler e' la famiglia **WaxCrawler** — l'oggetto esiste GIA' su entrambe le macchine, attivo ma INVISIBILE e col collider pilotato dall'Animator. Quindi niente `SetActive`: il segnale giusto e' il **COLLIDER del danno** (`DamageAreaCollider.enabled` false→true), e la replica usa il punto d'ingresso del gioco, **`WaxCrawlerBehaviour.Rise()`**.

**Il fix**: `EnemyState.WasReal` + `DetectReveals()` (ogni frame, prima del gate di connessione) → `nato|<id>` sul canale esistente (6° prefisso), gated su `OwnedByLocal`; ramo `nato|` in `ReceiveEnemyDeathGlobal` PRIMA di `_pendingDeaths.Add`; `ReceiveBirth()`; `_announcedBirths` per chi entra dopo; `sprite=`/`collider=` nel log `[EnemyTree]`.


### Voce 8 — i boss coi target (superata; il lavoro vero e' la voce 18, test boss per boss)

**Verdetto:** ✅ CHIUSA — superata


### Voce 40 — ReapplyCurrentScene gira due volte

**Verdetto:** ✅ CHIUSA — non osservabile


### Voce 7 — RegisterEnemies 84,5 ms

**Verdetto:** ✅ CHIUSA — non si manifesta


### Voce 10 — ProcessQueue + IdOf

**Verdetto:** ✅ CHIUSA — non si manifesta


### Voce 43 — codice morto in DecideOwnership

**Verdetto:** ✅ CHIUSA — scheletro rimosso


### Voce 11 — la pausa che il gioco disfa

**Verdetto:** ✅ CHIUSA — il testo era sbagliato


### Voce 36 — i salvataggi senza Steam (vedi BUG-NOTI)

**Verdetto:** ✅ CHIUSA — documentata


### Voce 15 — Common.dll su entrambe (deploy + verify-deploy)

**Verdetto:** ✅ CHIUSA — procedura coperta


### A3 (le SCIE) — API VERIFICATE, pronte per l'implementazione

**⚠️ LA TRAPPOLA (verificata in IL, e va rispettata)**: `GhostTrailGenerator.GetGhostTrail()` finisce con `spriteRenderer.flipX = base.EntityOwner.Status.Orientation == EntityOrientation.Left`, e `Trait` valorizza `EntityOwner = GetComponentInParent<Entity>()`. **L'avatar remoto e' un `new GameObject` SENZA `Entity`** ⇒ `EntityOwner` resta **null** ⇒ **con `followOrientation` al default (true) OGNI generazione di scia lancia `NullReferenceException`**. **⇒ `followOrientation = false` NON E' NEGOZIABILE.** *(E' il modo in cui questa strada fallisce: con un'eccezione, non con un'assenza.)*

**API VERIFICATE sul binario del gioco** *(`Gameplay.GameControllers.Effects.Player.GhostTrail.GhostTrailGenerator`)*:

```
campi PUBBLICI: customMaterial (Material) · AlphaStep (float) · InitialGhostTrailAlpha (float)
                TimeStep (float) · TrailColor (Color) · followRotation (bool)
                followScale (bool) · followOrientation (bool) · EnableGhostTrail (bool)
                AreGhostTrailsAllowed (bool)
metodi:         DoEnableTrail() · DoDisableTrail() · DrainGhostTrailPool()
base:           Trait -> MonoBehaviour   (quindi AddComponent<GhostTrailGenerator>())
```

**Il piano (dal referto)**: emittenti = patch su `Dash.OnCastStart`/`OnCastEnd`, `VerticalAttack.ShowGhostTrail(show)`, `LungeAttackBehaviour.OnStateEnter/Exit`, `IncreaseSpeedSwordHeartEffect.ApplyGhostingEffect`; ricettore = `OtherPlayerScript` (dove nasce l'avatar) con un `SetGhostTrail(bool)` che **copia i parametri dal generatore LOCALE** di `Core.Logic.Penitent` e li imposta su quello nuovo. **2 membri nuovi in `EffectType`** *(es. `TrailOn=70`, `TrailOff=71`; i valori in uso sono 0,1,10,11,20,30,40,60 e il byte arriva a 255)* ⇒ **nessun bump**.


### A4 — STATO (i VFX a terra e la scivolata)

- ✅ **Passo 1 FATTO** *(commit 9eba600)*: il payload di `Effect` porta una **posizione OPZIONALE** (`<tipo:1><x:4><y:4>`). `SendEffect(EffectType, Vector2)` + `ReceiveEffect(..., bool hasPosition, Vector2)` + `PlayEffectAnimation(..., bool hasPosition, Vector2)`. ⭐ **NESSUN BUMP**: `ReceiveEffect` legge UN byte e si ferma, quindi il payload puo' crescere senza rompere nulla, e i pacchetti vecchi restano validi (`hasPosition` e' un flag, NON un "zero = assente": l'origine e' un posto legittimo).
- ✅ **Campi della scivolata VERIFICATI in IL**: `GuardSlide.GuardSlideImpactEffect` *(public GameObject)* e `GuardSlide.GuardSlideSoundFx` *(public string)* — quindi si LEGGONO dal `GuardSlide` LOCALE e si riproducono alla posizione mandata.
- ⚠️ **DA NON FARE**: chiamare `GuardSlide.OnCastStart()` sul ricevente — eseguirebbe l'ABILITA'. Idem per `VerticalAttack.InstantiateExplosion` senza guardie.
- 📋 **RESTA**: *(a)* il membro `EffectType.SlideDust = 80`; *(b)* il patch su `GuardSlide.OnCastStart` che manda `SendEffect(SlideDust, posizione)`; *(c)* i `case` nel ricevente che fanno `PoolManager.ReuseObject(GuardSlideImpactEffect, posizione)` + il suono `GuardSlideSoundFx`; *(d)* la stessa cosa per l'impatto A TERRA (`HardLandingEffect`, `VerticalAttackBeam`, `VerticalLandingFxLevel*` — campi `[EventRef]` del prefab, **valori non leggibili dal codice** ⇒ si usano i campi del componente LOCALE, come per la scivolata).
- ⚠️ **SIGNATURE DA PROBARE PRIMA**: `PoolManager.ReuseObject(...)` e `Core.Audio.EventOneShotPanned(...)` — non ancora verificate in IL.


### Voce 59 (A5 — i SUONI) — STATO: quasi completa, e il metodo delle PROBE ha pagato

- ✅ **I PASSI** *(A5.1)*: **era un guasto di PARAMETRI FMOD, non di rete**. Il gioco suona il passo con `PlayOneShotEvent(..., FxSoundCategory.Motion)`, e QUELLA categoria imposta **14 parametri del materiale del terreno** (Dirt, Water, Stone...). I **fendenti** passano da `PlaySfxOnCatalog`, che **non imposta nulla** — ed e' **per questo che si sentivano**. La mod usava `EventOneShotPanned` (solo `Spanish` e `Panning`). Fix = **riprodurre la catena locale a 3 passi**: `CreateCatalogEvent(CHIAVE, pos)` + i 14 materiali + `SetPanning` + `start/release`. ⚠️ **Due trappole intercettate**: `CreateCatalogEvent` vuole la **CHIAVE e non il percorso** *(passargli il percorso fallirebbe IN SILENZIO)*, e i 14 valori sono campi `Family` ⇒ letti per **riflessione** dal penitent LOCALE (degrada, non blocca).
- ✅ **I 6 FENDENTI** *(A5)* + ✅ **il quarto colpo di combo** (`ComboHit`), **lo sguaino** (`IdleSword`, categoria Motion ⇒ stessi 14 materiali) e **l'attacco aereo-a-terra** (`VerticalAttackStart`/`Falling`; il tonfo era gia' coperto da A4).
- ⭐ **IL METODO DELLE PROBE**: tre domande **non erano decidibili** ne' in IL ne' dai log *(i nomi degli AnimationEvent stanno nei CLIP, negli asset)* ⇒ **5 patch di SOLO LOG** invece di scommettere su 3 fix. Hanno risposto: `ComboHit` **si'**, `ComboHitUp/Down` **mai**, `IdleSword` **raro** (copribile), `LoadingCharged` **si'**. Rimosse dopo il test.
- 📋 **RESTA**: *(1)* **la SCIVOLATA** (`DashSoundStart/Stop` 95/96) — evento CATALOG **SOSTENUTO**, l'unico di A5 con STATO *(3 vie di sicurezza: STOP, `OnDestroy`, scadenza a 5 s che LOGGA)*; *(2)* **il CARICAMENTO** (`LoadingCharged` 101/102) — lo stesso pattern del dash, con **4 punti di spegnimento**; *(3)* `ComboHitUp/Down` (98/99) restano liberi e **non servono** (mai comparsi).


### Il CARICAMENTO della carica (`LoadingCharged` 101/102) — NON SI FA, ed e' una SCELTA

**Verdetto: ❌ NON SI FA — decisione dell'utente, non un buco.**

> *"Saltiamola, perche' i suoni di charging mi piace che li abbiano solo i remoti, perche' e' un charging mentale del penitente alla fine."* — l'utente

**Cosa sarebbe stato**: `PenitentLoadingChargedAttack` e' un evento CATALOG **SOSTENUTO**, con **QUATTRO punti di spegnimento** nel gioco (`AttackBehaviour`, `ChargedAttackEffectBehaviour`, `IdleAnimatonBehaviour`, `HurtSubStateBehaviour`) ⇒ lo stesso pattern della scivolata, ma con quattro uscite invece di una. I byte 101/102 restano liberi e documentati.
**⭐ E la motivazione e' di DESIGN, non tecnica**: il suono del caricamento e' l'unico che l'utente vuole **SOLO sul lato di chi carica** — *"e' un charging mentale del penitente"*. ⇒ **non coprirlo e' la scelta giusta**, non una rinuncia.

## [2026-10-12 12:00] AUDIT DELLA TODO — la tabella in testa era indietro di cinque giorni, e la COLPA non era una voce

**Cosa e' stato fatto.** Un audit ha messo **la tabella in testa** contro **i 265 commit** *(dal piu' recente al piu'
vecchio)*, **il codice della mod** e **i referti di `scratch\`**. Il referto completo e' in `scratch\AUDIT-TODO.md`.
**Niente e' stato cancellato**: le voci sono state **marcate**, e il testo che c'era e' ancora li'.

### ✅ LE VOCI CHIUSE DA QUESTO AUDIT, e la prova di ognuna

| Voce | Cos'era | Prova |
|---|---|---|
| **52** | 🔴 il prompt di esecuzione non condivideva l'animazione | **A1** `8286d11` · **A2** `e6eb9c1`+`1699536` · **A3** `b46c651`+`aee38aa` · **A4** `b1e21f8`+`060480b` · il registro `[Effect]` `f4fd871` · il server che inoltrava senza nome `74f17da` |
| **56** | ⚠️ mancavano 5 marker per il confronto per-nemico | `dbcc9f4` *("MARKER 1, 2, 4, 5 — i quattro locali, con verifica nell'IL di ognuno")* + `ff887a0`. **Nel codice**: `FullId` `:3144`, `t={Time.time:F1}` `:687`/`:2908`, la riga su `FindIdOfHandle` fallito `:680`/`:3385`/`:3847` |
| **59** | 🟡 A5 — i suoni locali che il peer non sente | i **passi** `078ba10`+`22b7d9d` *(guasto di **parametri FMOD**, non di rete)* · i **6 fendenti a vuoto** `078ba10` · **quarto colpo + sguaino + aereo-a-terra** `a159fa7` · la **SCIVOLATA** `eda9f0a`. Nel codice: `DashSoundStart/Stop = 95/96`, `case` in `OtherPlayerAttackScript.cs:261-267` |
| **60** | 🟡 A6 — le esecuzioni non erano condivise | `9b1c296` · `f397168` · `15173fc` · `4a86280` · `b3c6e70` · `fa6361c` · `00fa1e5` · `60b5c5f`; **residui** `5547ba6`, `ea8ccfb`, `ce2b33e`. ⭐ **E NON servi' il bump a 12 che la voce temeva** |
| **61** | 🟡 la scia si disegnava col verso sbagliato | `aee38aa` *(e `0ce8194` per la polvere)* |
| **62** | 🔴 la polvere della scivolata di MOVIMENTO non si vedeva | `5b44d7f`; nel codice `EffectType.SlideDust`/`DashDustStart`/`DashDustStop` e i `case` in `OtherPlayerAttackScript.cs` |

### ⭐ E LA COSA CHE MANCAVA DEL TUTTO: LA COLPA — ora **voce 63**

**La COLPA** *(la penalita' di morte, le **spine sulla barra della vita**)* aveva **una sezione di design completa** —
la entry `[2026-10-11 02:00]` *(righe 3412-3489)*, con `GuiltManager`, `PlayerFervour.RefreshGuilt`,
`guiltStart`/`guiltBar`/`guiltEnd` e la scoperta che **le spine vanno DISEGNATE perche' la barra si riscala** —
**ma NON era una voce numerata** ⇒ **non compariva in nessun inventario**. **Promossa a voce 63**, con dentro tutto
quello che la sezione aveva gia' trovato, i riferimenti di lettura e **un rimando alla sezione** *(che resta il punto
unico di verita': la voce ne e' l'indice numerato, non una copia con cui puo' divergere)*.

### ⛔ LE COSE MARCATE COME SUPERATE / DECISE CONTRO *(nessuna cancellata)*

- **La riga "Ultimo aggiornamento" diceva che restavano 33, 32, 4, 29, 30, 31 e 8 — tutte CHIUSE in tabella.**
  E' la contraddizione piu' grave trovata: **due punti dello stesso file dicevano il falso nella stessa direzione**.
- **§8, passo 3**: diceva *"⏳ PROSSIMO"* mentre il fix del join era **consegnato e verificato in gioco**.
- **§8, passo 4b** *(diagnostica dietro flag)*: **NON e' stato fatto cosi'** — **va detto**, non chiuso: il sintomo
  e' stato risolto **per un'altra via** *(la cadenza del log separata dal rescan, `215c4f1`)*.
- **§8, passo 4c** *(fullPathHash)*: **parcheggiato di proposito** *(decisione dell'utente, `7c638e5`)* — non e' una svista.
- **§9, domanda 4** *(`shortNameHash`)*: **superata** — era *"da aggiungere **se il passo 2 non basta**"*, e **il passo 2
  e' bastato** ⇒ **la domanda non ha piu' un oggetto**.
- **§9, domande 5, 10, 13**: **risolte** *(l'oggetto di §4 e' `BreakableObject`; il cluster 69-106 ms e' `RegisterEnemies`;
  `orientFlip` e' strumentato e **ha provato** il fix di §1b)*.
- **§12.3**: tutte e cinque le righe hanno oggi una **voce numerata** o una voce di `BUG-NOTI.md` (11, 13, 14, 15/36, 17)
  ⇒ **la sezione resta come STORIA, non come lavoro aperto**.
- **Il CARICAMENTO della carica (`LoadingCharged` 101/102)**: **deciso di NON farlo** *(`1d4fa78`)* — **e la tabella in
  testa NON lo elencava come "da fare"**, quindi qui **non c'era niente da correggere**: la verifica e' andata a buon fine.

### ⚠️ E CIO' CHE NON E' STATO CHIUSO, perche' la prova non c'e'

- **La voce 9 (spettatore)**: **il livello di stato e' stato COSTRUITO**, ma **per un'altra voce (la 44)** e **con un'altra
  forma**: `PlayerDeathSync.cs` implementa `pdead|`/`palive|` sul canale `EnemyDeathGlobal` *(nessun bump)*,
  `SetPlayerDead`, `CountDeadPlayers`, `IsEveryoneDead`. **La camera sul compagno e il body invisibile NON esistono**, e
  **nessun referto lo verifica in gioco**. ⇒ **RESTA APERTA**, e la sua descrizione in tabella **non e' piu' quella del suo stato**.
- **La voce 18 (boss per boss)**: **verificato aperto** — nessun commit nomina *"boss per boss"* e l'unico boss esercitato
  nei referti e' **`ElderBrother`**. **Resta la voce piu' esposta**, perche' la barra del boss *"coincide per coincidenza,
  non per sincronizzazione"* *(§2)*.
- **La voce 58 (le cutscene)**: **la causa e il principio sono chiari** *(la mod di base **REGISTRA** la cutscene: giusto per
  il progresso, sbagliato per l'esperienza)*, **ma il fix — togliere la cutscene dagli eventi registrati — non e' stato fatto**:
  in `Data\FlagStates.cs` le cutscene **sono ancora nella lista dei flag sincronizzati** *(righe 163, 166, 175-183)*, e nessun
  commit lo chiude. ⇒ **APERTA, con la strada gia' scritta.**
- **§9, domanda 14** *(`EnemyAI` gira davvero?)*: **non verificabile da qui** — nel codice della mod **0 occorrenze**, e
  verificarlo richiede di guardare i **prefab**, non il codice. **Resta aperta.**
- **§9, domanda 15** *(il fantasma e' congelato dalla mod?)*: **non verificata** — servirebbe un test che logghi
  l'orientamento **mentre l'altra macchina possiede il nemico**. **Resta aperta.**

### 📊 I NUMERI CORRETTI *(`RIPRISTINO.md` + questa TODO)*

| Dove | Diceva | Vero |
|---|---|---|
| `docs\RIPRISTINO.md:78` | **235** bersagli Harmony | **263** *(`REFERTO-SPAM-LOG.md:12`: "263 bersagli controllati … baseline 263 confermata")*. ⚠️ **E il campo e' cambiato due volte in pochi giorni**: `5021f23` dichiara **255**, poi il fix di A5 lo riporta a **263** |
| `docs\TODO.md:844` *(domanda 8)* | **32** `Couldn't find connected player` | **309** *(`REFERTO-SPAM-LOG.md:99`, in alternanza **1:1** con 309 `Received new game progress`)*. **E non era "una finestra di handshake": e' il sentinella `"*"`**, cioe' una domanda senza risposta possibile |
| `docs\TODO.md`, riga dell'ultimo aggiornamento | *"Restano: 33, 32, 4, 29/30/31, 9, 8/18"* | **33, 32, 4, 29, 30, 31 e 8 erano tutte CHIUSE in tabella** |
| **7a** *("§7 note tecniche")* | `if (!localIsHost) return false;` **proposto** | **RIMOSSO** *(voce 43, `c55d80e`, "scheletro rimosso")* |
| `docs\RIPRISTINO.md:94-95` | *"Le voci 7, 9, 10, 15, 18, 35, 36, 40, 43, 45, 52, 53 sono aperte"* | **APERTE DAVVERO SOLO LA 9 E LA 18** — 7, 10, 15, 35, 36, 40, 43, 45, 52, 53 sono **chiuse** *(e la 9 e' aperta **per una ragione diversa** da quella scritta)* |

### 📌 LA LISTA DELLE COSE ANCORA DA FARE, in ordine di importanza

**1. La voce 18 — il test boss per boss.** E' l'unica *"non prevedibile"* e la piu' esposta: un solo boss e' stato
esercitato. **2. La voce 58 — le cutscene**: causa e strada sono scritte, il fix *(togliere la cutscene dai flag
registrati)* non e' fatto e tocca **entrambe le macchine**. **3. La voce 63 — LA COLPA**: il design e' completo,
l'arte esiste, **l'implementazione non e' iniziata**. **4. La voce 9 — lo spettatore**: il livello di stato c'e',
**camera e body no**; il *"segue i cambi stanza"* resta **bloccato dal motore**. **5. §9 domanda 15 — il fantasma
e' congelato dalla mod?** *(test mai fatto)*. **6. §9 domanda 14 — `EnemyAI` gira davvero?** *(si decide sui prefab)*.
**7. §8 passo 4c — `fullPathHash`**: parcheggiato di proposito, **da riprendere solo se** la fluidita' tornasse a dare
fastidio. **8. I residui dichiarati in `BUG-NOTI.md`**: la seconda `Object.Destroy` in `SettleStuckDeaths` *(`[EnemyDeath]`
= 0 in ogni sessione: ha un commento che dice cosa fare la prima volta che appare in un log)* e l'esecuzione incrociata.

### Voce 63 — **LA COLPA** *(promossa a voce da questo audit: prima era solo una entry datata)*

**Priorita':** 🟡 **APERTA** — *design completo, implementazione NON iniziata.*

**Perche' e' una voce nuova.** La Colpa **non era una voce numerata**: viveva solo dentro la entry
`[2026-10-11 02:00] LA LIBRERIA ESTERNA: verdetto DEFINITIVO` *(righe 3412-3489)*. Quindi **non compariva in nessun
inventario**, e un difetto con un design completo risultava invisibile a chi guardava la lista. **Il testo integrale
e' nel suo posto originale** *(la sezione e' il punto unico di verita'); **qui c'e' l'indice numerato e l'esito
della verifica.**

**Il problema.** La **penalita' di morte** del gioco — *la Colpa* — **non e' condivisa**: e' un effetto di GUI e di
stato che oggi vive **solo sulla macchina di chi muore**. Il secondo bisogno della stessa entry e' il **messaggio
d'ingresso alla bossfight**, che ha una soluzione a costo quasi nullo *(vedi il punto 6 della voce 63 nella lista in testa)*.

**Cosa e' stato ACCERTATO** *(dalla entry, verificato in IL)*:

1. **Le spine NON si ottengono abbassando un massimo.** `CurrentMax = CurrentMaxWithoutFactor * MaxFactor` e
   `CalculateValue()` **CLAMPA** `Current` a `CurrentMax` ⇒ `PlayerHealth.BarTarget = Life.Current / Life.Final`
   **si RISCALA** e la barra risulta **di nuovo PIENA** ⇒ **le spine vanno DISEGNATE**.
2. **Il gioco ha gia' un `GuiltManager`**, e in **tutta** l'assembly `set_MaxFactor` ha **DUE SOLI chiamanti**,
   **entrambi `GuiltManager`** *(`OnPenitentReady`, `UpdateGuilt`)* e **sempre su `Stats.Fervour`, MAI su `Life`**.
   ⇒ **Se e' estendibile, la Colpa si aggancia a quello** invece di aggiungerne uno parallelo: *"Blocco del Fervore"*
   e' gia' il suo mestiere.
3. **L'arte esiste gia' a runtime**: `PlayerFervour.RefreshGuilt(bool)` legge `Fervour.MaxFactor` e fa `DOFade` su
   **`guiltStart`/`guiltBar`/`guiltEnd`** *(tre `Image`, campi **privati** ⇒ `Traverse`)* ⇒ **si riusano le stesse spine**,
   o in alternativa un PNG nostro col percorso che il progetto **usa gia'** *(`Map\MapManager.cs:117` → `cfg.Marks[...]`)*.
4. ⛔ **La libreria esterna NON serve a questo bisogno** *(ed era la domanda da cui la entry nasce)*: `SpriteStorage`
   e' **solo un dizionario nome→`Sprite`** e non carica immagini; il costo di integrarla e' alto *(`NU1605`).

**Stato della verifica** *(audit 2026-10-12)*: `git log --grep` su **colpa / guilt / spine** non trova **nessun commit**,
e **nessun file del client implementa le spine**. ⇒ **voce APERTA, non fatta, e mai tracciata come voce.**

**⭐ LA FORMULAZIONE DELL'UTENTE** *(2026-10-13 — e questa e' la definizione da usare, perche' dice **quando** la Colpa deve
applicarsi e **quando no**)*:

> *"la **grief sulla vita** in caso di **morte** e **non** seduta al falò"* — l'utente

⇒ **Due condizioni, entrambe necessarie**: *(1)* si applica **morendo** *(non al falò: sedersi al falò NON deve aggiungere
Colpa)*, e *(2)* colpisce **la VITA** *(`Stats.Life`)*, che e' il pezzo che il gioco **non** fa gia': `GuiltManager` chiama
`set_MaxFactor` **sempre e solo su `Stats.Fervour`** *(punti 2 e 3 qui sopra)* ⇒ **estendere `GuiltManager` a `Life` E'
il lavoro**, non riusarlo com'e'. **Le spine vanno DISEGNATE** *(punto 1)* perche' la barra si riscala. **Verificato al
2026-10-13**: la voce esiste, e' leggibile, e il design qui sopra e' completo — **nessuna implementazione, nessun commit,
e i due punti (la vita, non il Fervour · la morte, non il falò) sono il contenuto della voce, non un'aggiunta.**

---

## [2026-10-13 10:00] M1 — LA MORTE DEL BOSS ARRIVA SULLA MACCHINA DI CHI E' A TERRA, e la voce 58 si chiude PER DECISIONE

**Catena dei commit di questa sessione** *(verificata con `git log --oneline -6`)*:
`31f53ad` *(l'audit)* → `b86bd3f` *(le tre probe sulle cutscene, bersaglio #264)* → **`b6e1838`** *(via la sola probe #1:
264 → **263**)* → **`afca6fe`** *(**M1**, l'unico intervento di gioco: +237 righe, **1 file**, **nessuna patch nuova**)*.
**Albero pulito.** I referti sono in `scratch\`: `REFERTO-M1-MORTE-BOSS.md` *(il referto di M1)*,
`STUDIO-BOSSONE-MORTE-CAM.md` *(l'analisi: le tre run, la guardia, la cam)*, `STUDIO-CUTSCENE-CONDIVISA.md` *(la voce 58)*,
`PIANO-58-9.md`, `REFERTO-PROBE-CUTSCENE.md`, `REFERTO-SPAM-LOG.md`, `REFERTO-PEER-INVISIBILE.md`,
`REFERTO-TRE-FIX.md`, `REFERTO-FANTASMA-ESECUZIONE.md`, `REFERTO-ANNUNCIO-E-FLASH.md`.

**Perche' questa sezione esiste.** Per due giorni il lavoro e' andato in una direzione **che il documento non raccontava**:
la **voce 58** (le cutscene) e' stata **chiusa per decisione** — e la decisione e' cambiata **perche' la diagnosi e'
cambiata**, non perche' qualcuno abbia rinunciato; e la **voce 9** (lo spettatore) e' stata **corretta**, perche' la sua
cella raccontava uno stato che non e' quello che c'e'. In mezzo c'e' **M1**, che e' il lavoro vero di questa sessione.
**Niente e' stato cancellato**: le celle delle voci 58 e 9 sono state **marcate**, la voce 63 **estesa**, e la storia sta
qui sotto.

---

### 1. ⭐ LA VOCE 58 — **CHIUSA PER DECISIONE** *(non per fix)*

**La storia, per intero — e' una decisione, quindi va scritta.**

**Cos'era la voce 58.** *"Le cutscene si perdono per chi non entra in quel momento"* — riscontrata dall'utente e
diagnosticata da lui: **la mod di base non sincronizza la cutscene: REGISTRA il flag che la scena e' avvenuta**, cosi'
evita di farla riaccadere a chi non l'ha triggerata. Il peer riceve il flag, lo applica con `Core.Events.SetFlag` e — poiche'
i flag **vivono su disco** *(`ID_EVENT_MANAGER.flags` nel `.save`, `EventManager.GetCurrentPersistentState` / `SetCurrentPersistentState`)*
— **la scena e' persa per sempre in quel salvataggio**: non si torna indietro dal menu e non si torna indietro
riconnettendosi. L'unica via era `flag clear <ID>` dalla console *(`FlagCommand`, il cheat di test)*.

**Cosa ha accertato lo STUDIO** *(`STUDIO-CUTSCENE-CONDIVISA.md`, `PIANO-58-9.md`)*:

- il **"check"** non e' un `if` su una cutscene: e' **una tabella di id ammessi**, `Data\FlagStates.cs` —
  il blocco letterale `// Cutscenes` *(9 id, righe 174-183: `DEOSGRACIAS_CUTSCENE_PLAYED`, `PONTIFF_ALBERO_EVENT`,
  `PONTIFF_BRIDGE_EVENT`, `PONTIFF_ARCHDEACON1/2_EVENT`, `BROTHERS_EVENT1/2_COMPLETED`, `BROTHERS_GRAVEYARD_EVENT`,
  `BROTHERS_WASTELAND_EVENT`)* **piu' 3 fuori blocco** nel blocco `// World states` — `DAGGER_CUTSCENE_FINISHED` *(163)*,
  `MEACULPA_CUTSCENE_PLAYED` *(166)* e `BAPTISMAL_0*` *(165)*. **Chi tocca solo il blocco `// Cutscenes` lascia il bug a meta'.**
- il **check in uscita** e' in `ProgressSync\Helpers\FlagHelper.cs`: il `Postfix` su `EventManager.SetFlag` *(`:31`)* fa
  `if (FlagStates.getFlagState(formatted) != null)` *(`:37`)* → `SendProgress` *(`:40`)*; il **check in entrata**
  *(`EventManagerIntro_Patch`, `:49`)* usa **lo stesso** `getFlagState` per il send-all al join.
- **il fix minimo sarebbe stato TOGLIERE 11 ID** *(non 12: `BAPTISMAL_0*` e' un **wildcard** che copre `BAPTISMAL_01..06_USED`,
  e per nome e' **una meccanica, non una scena** — toglierlo sarebbe stato il modo piu' rapido di perdere progresso in silenzio)*.
  **Nessuna patch nuova: 263 bersagli restavano 263.** La forma consigliata era un **blocco di esclusione esplicito**
  in `getFlagState` *(leggibile e annullabile in un punto solo)*, **piu' una riga `[FlagSync] … locale (escluso)` obbligatoria** —
  senza, *"il flag e' arrivato"* e *"il flag non e' mai partito"* hanno lo **stesso aspetto** nel log.
- ⛔ **E lo studio ha anche chiuso il rischio opposto, che era il rischio VERO**: `StartCutscene` **non guarda nessun flag**
  *(`Cinematics.cs:113-152`: gli unici controlli sono `if (cinematicRunning) return;` `:115` e `SearchReferences()` `:122`)*
  ⇒ **il RISCHIO 4 del piano non esiste**. Ma ne esiste uno **peggiore, ed e' un bug del GIOCO**: il blocker
  `CINEMATIC` viene acceso a `:134` **prima** che `cinematicRunning` diventi `true`, e `EndCutscene` esce subito se
  `!cinematicRunning` *(`:156`)* ⇒ se il `VideoPlayer` non prepara mai il video *(clip mancante, NRE catturata a `:343-347`,
  che fa `Log.Error("…Forcing ending")` e **non chiude niente**)*, **il blocker resta acceso e la tastiera non risponde piu'
  fino al riavvio**. La rete di sicurezza esiste ed e' verificata *(`SetBlocker`/`HasBlocker` sono `public`)*, ma e'
  **una rete**, non una strada.
- ✅ **E la misura che ha ridimensionato il danno** *(`STUDIO-CUTSCENE-CONDIVISA.md` §4.2)*: il salvataggio ha **698 flag**,
  e la **maggior parte dei flag "a forma di cutscene" NON e' in `FlagStates`** *(`SANTOS_INGAME_CUTSCENE`,
  `SANTOS_LAUDES_CUTSCENE_PLAYED`, `SANTOS_CTS102/103_PLAYED`, `CANDELARIA_1014_PLAYED`, `MIRIAM_INGAME_CUTSCENE`,
  `PONTIFF_CUTSCENE_PLAYED`, `BROTHERHOOD_CUTSCENE1/2_DONE`, `PENITENCE_EVENT_FINISHED`, `ZONE_NAME_OF_D01Z02_DISPLAYED` — tutte ❌)*
  ⇒ **il gioco ha molte piu' cutscene di quelle in tabella, e quelle fuori tabella NON si perdono**: restano `false` sul peer,
  che le vedra' quando ci arriva. **Il danno era limitato ai 12 id registrati** — non un danno generale.

**⭐ E POI E' EMERSO IL FATTO CHE HA CAMBIATO TUTTO** *(`STUDIO-BOSSONE-MORTE-CAM.md`)*.

Dall'analisi delle **tre run** sulla bossroom di `D17Z01S11` (`ElderBrother`), con i log di entrambe le macchine
allineati sul `ServerOutput.log`:

| Run | Chi uccide il boss | Bossroom | Cutscene `CTS02-Bloody Baptism` |
|---|---|---|---|
| **1** | G1 (G2 contribuisce) | **tutti e due vivi** | ✅ **parte su ENTRAMBE** *(G1:2721, G2:2603)* |
| **2** | G2 | G1 morto, poi **torna al falo'** | ❌ **persa da G1** *(0 `CTS02`)*, ✅ G2:2917 |
| **3** | G2 | G1 morto **e ancora nella stanza** | ❌ **persa da G1** *(0 `CTS02`)*, ✅ R3-G2:2814 |

Il **conteggio per macchina** *(`STUDIO-BOSSONE-MORTE-CAM.md` §1.4-bis)* si legge in una riga:
*la macchina su cui il boss non emette `[EnemyKill]` / `vita=0` e' **esattamente** la macchina su cui non compare
`cutscene_block` e non compare `CTS02`.*

| Run / macchina | `[EnemyKill] …ElderBrother…MORTO` | `ElderBrother_Death` | `vita=0` | `cutscene_block` | `Boss fight Nº 1 was successful` | `CTS02` |
|---|---|---|---|---|---|---|
| **R1-G1** *(vivo, uccide)* | 1 | 1 | 1 | **2** | ✅ | ✅ 2 |
| **R1-G2** *(vivo, aiuta)* | 0 | 0 | 0 | **2** | ✅ | ✅ 2 |
| **R2-G1** *(morto)* | **0** | **0** | **0** | **0** | ❌ | ❌ 0 |
| **R2-G2** *(uccide)* | 1 | 0 | 0 | 2 | ✅ | ✅ 2 |
| **R3-G1** *(morto, in stanza)* | **0** | **0** | **0** | **0** | ❌ | ❌ 0 |
| **R3-G2** *(uccide)* | 1 | 0 | 0 | 2 | ✅ | ✅ 2 |

**⇒ IL FATTO: il danno e' CIRCOSCRITTO alle cutscene dei BOSS, per chi muore nella bossroom.** E la causa, in una riga di IL:

```csharp
// Gameplay.GameControllers.Bosses.ElderBrother\ElderBrother.cs:74-79   ← la GUARDIA
public void Damage(Hit hit)
{
    if (Status.Dead || Core.Logic.Penitent.Stats.Life.Current <= 0f)
        return;                       // ← con il giocatore LOCALE a terra non entra NIENTE
```

⇒ **il boss e' INVULNERABILE sulla macchina di chi e' a terra** ⇒ **non muore li'** ⇒ **la sua FSM di morte non parte**
⇒ **`Boss.OnDeath` non scatta** ⇒ **nessun `BossFightMetrics.EndBossFight`, nessun `onFinishBossFight`** ⇒
**la cutscene non parte**. **Misurato in R3-G1**: per **~24 s** il log stampa `[EnemyAnim] D17Z01S11@ElderBrother … vita=330`
*(**330 = vita piena**, sempre)*, e il colpo letale dell'altro **arriva e viene scartato** *(riga `2734`
`[Danno] relay  amount=331 … vitaPrima=330`, con **DUE SPAZI** dopo `relay`: quel formato e' `$"[Danno] relay {IdOf(enemy)} amount=…"`,
quindi **l'id e' VUOTO** — `ReceiveEnemyKill` l'ha gia' tolto da `_enemies` prima di `ApplyDamage`, e un boss e' un **oggetto di scena**,
senza spawn point)*. E la riga `[TRACE][METRICS] Boss fight Nº 1 was successful.` — che esce **solo** da
`BossFightMetrics.EndBossFight()`, il cui **unico chiamante** e' `BossFightManager.OnDeathBoss` — **sul morto e' ASSENTE**.
**La stanza non c'entra**: in run 3 il morto **era** in `D17Z01S11` quando e' arrivato il flag `D17Z01_BOSSDEAD`
*(righe 2731/2735)*, ed e' uscito **dopo**, pigiando un tasto *(2767 FADE → 2778 `RestoreStored` → 2820 load)*.
E il morto **non c'entra con la cutscene in generale**: in **R1-G2**, che **non** aveva ucciso lei il boss, la scena e'
partita **completa** — **basta che il boss muoia in locale, in un modo qualunque.**

**La guardia, verificata su 16 classi di boss** *(cercata per stringa in `scratch\_il\…\Bosses`)*: `Amanecidas`,
`BejeweledSaintHead`, `WickerWurm`, `BurntFace`, `Crisanta`, `Esdras`, `Perpetua`, `ElderBrother`, `Isidora`,
`PietyMonster`, `PontiffGiant`, `PontiffOldman`, `PontiffSword`, `Quirce`, `Snake`, `TresAngustiasMaster`.
**Verificata per stringa in tutte e 16, ma valutata in esecuzione solo per `ElderBrother`.** E la mod **lo sapeva gia'** e
lo scriveva: `EnemySyncManager.cs:3208` — *"because the boss's own `Damage()` discards everything while the LOCAL player's
life is zero, every hit the survivor landed was thrown away"*.

**✅ LA DECISIONE DELL'UTENTE: NON TOCCARE LA MOD BASE.** Le cutscene normali **funzionano** *(la misura §4.2 lo prova:
le scene fuori tabella non si perdono)*, il danno e' **circoscritto** alle cutscene dei boss per chi e' a terra ⇒
**la voce 58 si chiude come "documentata e accettata"**.

**⚠️ E va scritto PERCHE' non si fa — non e' pigrizia, e non e' "non si puo' fare".**
1. **Il fix minimo toccava una tabella COLLAUDATA** *(`Data\FlagStates.cs`, 181 id)* **per risolvere un caso che si risolve
   dall'altra parte**: la registrazione del flag e' **giusta per il progresso** *(boss, leve, porte, inventario)*, e
   `BAPTISMAL_0*` e' la prova che quella tabella **mischiata** non si lima a occhio senza rischiare progresso perso in silenzio.
2. **Il caso che resta — la cutscene del boss persa dal morto — e' la morte del boss, non la cutscene.** ⇒ **si risolve
   portando la MORTE sulla macchina del morto** *(M1, §2)*, e allora **la cutscene parte da sola per la strada nativa**,
   **senza toccare ne' la tabella ne' `StartCutscene`**.
3. **E la strada "far partire la scena all'altro" era, misurata, la piu' rischiosa delle tre** *(via (a) dello studio)*:
   somma il rischio del **blocker che resta acceso per un guasto del video** *(bug del gioco, §sopra)* alla traduzione a
   due salti `id → nome → CutsceneData` *(che funziona **solo nella scena caricata**)*. **E non serviva**: la via (b)
   — non registrare — non perde progresso, e M1 copre il caso dei boss.

**📌 E per la cronaca: la tabella id→scena che serviva a decidere la via (a) NON e' stata costruita, e non serve piu'.**
Vedi §5.

---

### 2. ⭐⭐ M1 — LA MORTE DEL BOSS *(il lavoro vero)*

**Commit `afca6fe`** — *"M1 - LA MORTE DEL BOSS ARRIVA ANCHE SULLA MACCHINA DI CHI E' A TERRA: annuncio bdead| +
boss.Kill() sul morto"*. **+237 righe, 1 file** *(`EnemySyncManager.cs`)*, **nessuna patch Harmony nuova**.
Referto: `scratch\REFERTO-M1-MORTE-BOSS.md`.

**Il principio, in una riga**: **non si sincronizza la CUTSCENE — si sincronizza la MORTE DEL BOSS.** La cutscene e' gia'
nativa, gia' locale, gia' corretta: parte da sola da `Boss.OnDeath` → `BossFightManager.OnDeathBoss` → `onFinishBossFight`
→ FSM `CutscenePlay`. **Se il boss muore anche sulla macchina del morto, la scena del morto arriva per la strada di
sempre.**

#### 2.1 Il percorso della morte, dal vivo *(tutto in `EnemySyncManager.cs`)*

| Passo | Dove | Cosa fa |
|---|---|---|
| 1. il gioco uccide il nemico | — | `Status.Dead = true`, vita 0 |
| 2. il ciclo di invio lo vede | `:3768-3793` | **la morte e' controllata PRIMA dell'attivita'** *(`enemy.Dead \|\| enemy.CurrentLife <= 0f`)* ⇒ vale anche per un nemico spento fuori camera |
| 3. annuncia | ivi | `SendEnemyKill(handle)` *(`:3783`)* · `SendEnemyDeathGlobal(id)` *(`:3785`)* · **`bdead\|`** *(`:3800`, M1)* |
| 4. sul ricevente | `NetworkManager.cs:616` / `:640-648` | `EnemyDeath` → `ReceiveEnemyKill` · `EnemyDeathGlobal` → `ReceiveEnemyDeathGlobal` |
| 5. `ReceiveEnemyKill` | `:5010` | `ResolveByHandle` → **toglie l'id da `_enemies`** *(`:5025`)* → `ApplyDamage(enemy, vita+1, …)` *(`:5036`)* |
| 6. `ReceiveEnemyDeathGlobal` | `:4676` | catena di prefissi; **M1 si inserisce a `:4777`**, prima del fall-through |
| 7. `ApplyDamage` | `:5067-5151` | `((IDamageable)enemy).Damage(hit)` *(`:5146-5148`)* ← **qui c'e' la GUARDIA dei boss** |

**Il boss passa da tutti e sette i passi**, e **non c'e' nessun filtro `IsBoss` sull'annuncio**: `IsBoss` era usato **solo**
per non scrivere la vita del boss *(`:4140-4163`)*, non per la morte. **Quello che non viaggiava era la parola "boss".**

#### 2.2 Il fix — il mittente `[BossDeath TX]`

Nel ramo di morte del ciclo di invio, **subito dopo** l'annuncio generico *(`:3798-3804`)*:

```csharp
if (IsBoss(enemy, id))
{
    Main.Multiplayer.SendEnemyDeathGlobal(BossDeathPrefix + id);   // "bdead|" + id
    ModLog.Info($"[BossDeath TX] {FullId(id)} boss={enemy.GetType().Name} " +
                $"vita={enemy.CurrentLife:F1} StatusDead={enemy.Status.Dead} " +
                $"localeMorto={PlayerDeathSync.IsLocalPlayerDead()}");
}
```

- **`BossDeathPrefix = "bdead|"`** *(`:315`)*: **l'ottavo prefisso** sul canale `EnemyDeathGlobal`, dopo `sp|` (nemici),
  `brk|` (lampade), `falo|` (falo'), `pdead|`/`palive|` (giocatori), `nato|` (nascite), `exec|` (esecuzioni).
  **Il server rilancia il payload verbatim: NESSUN bump di protocollo.**
- ⚠️ **E' un annuncio IN PIU', non uno di meno**: `SendEnemyKill` e `SendEnemyDeathGlobal(id)` **restano dove sono** ⇒
  **niente di cio' che esisteva cambia comportamento**, e chi non conosce il prefisso *(versione mista)* se lo trova in
  `_pendingDeaths`, dove e' **inerte** — la stessa proprieta' gia' verificata in IL per `falo|` e `pdead|`.
- **`IsBoss(enemy, id)`** *(`:273`)*: decide **per namespace** `Gameplay.GameControllers.Bosses`, con cache `_bossIds`.
  ⚠️ **E NON si usa `BossFightManager.IsFightStarted`**, di proposito: risponde a una domanda **diversa** *("il combattimento
  e' in corso ADESSO")*, richiede `FindObjectsOfType<BossFightManager>(true)` — una scansione di scena — ed e' **falso proprio
  nel momento che ci interessa** *(`OnDeathBoss` lo mette a `false`)*.

#### 2.3 Il fix — il ricevente `[BossDeath RX]`

In `ReceiveEnemyDeathGlobal`, **dopo `exec|` e PRIMA del fall-through a `_pendingDeaths.Add`** *(`:4777-4783`)*,
con lo stesso schema degli altri sei prefissi:

```csharp
if (IsBossDeathId(enemyId))
{
    string bossId = BossDeathIdOf(enemyId);
    if (!string.IsNullOrEmpty(bossId))
        ApplyBossDeath(bossId);
    return;                       // ← NIENTE _pendingDeaths, NIENTE MarkSpawnPointConsumed
}
```

**Perche' esce senza la contabilita' generica** *(e non e' pigrizia)*: `_pendingDeaths` non serve, perche' l'id nudo
arriva per conto suo e fa la sua parte come ha sempre fatto; e **`MarkSpawnPointConsumed` su un boss sarebbe un DANNO NUOVO** —
segnare consumato lo spawn di un boss puo' impedirgli di tornare in scena per un altro tentativo. ⇒ **il ramo fa una cosa
sola: `Kill()` sul boss locale, e solo se serve.**

#### 2.4 ⭐ Il punto del fix, una riga — `ApplyBossDeath` *(`:371`)*

```csharp
bool isBoss = IsBoss(boss, id);
string type = boss.GetType().Name;
float lifeBefore = boss.CurrentLife;

boss.Kill();                       // ← L'UNICA SCRITTURA SUL GIOCO DI TUTTO M1
```

**Perche' `Kill()` e non un `Damage(HP+1)`** *(la ragione per cui il fix e' cosi' piccolo)*: `Entity.Kill()`
*(`Entity.cs:312`, **`public virtual`**, e **nessun boss fra i 16 lo sovrascrive** — l'unico override in tutto l'assembly e'
`SingleAnguish.Kill()`, che non e' fra i 16)* fa `Damage(Stats.Life.Current + Stats.Defense.Final, string.Empty)` →
`Stats.Life.Current <= 0` → **`KillInstanteneously()`** *(`:317`)* → `Status.Dead = true`, vita 0 →
**`Entity.Death` · `OnDeath` · `OnEntityDeath`** · `Core.Events.LaunchEvent("ENTITY_DEAD")`.
**⭐ La guardia del morto NON e' sulla strada**: e' su **`Damage(Hit)`**, l'overload del **colpo**; `Kill()` usa
**`Damage(float, string)`**. **E' l'unica ragione per cui M1 e' una riga.**

**Le guardie, nell'ordine in cui sono valutate** *(`:379-408`)*:

| # | Guardia | `motivo=` | Perche' |
|---|---|---|---|
| 1 | `boss == null` | `boss-non-in-scena` | fuori scena / livello ricaricato: **dichiara il limite** invece di fingere che il pacchetto sia perso |
| 2 | `!PlayerDeathSync.IsLocalPlayerDead()` | `giocatore-locale-vivo` | ⭐ **M1.3**: su una macchina **viva** la morte arriva gia' per la strada di sempre *(R1-G2)* ⇒ **si interviene SOLO sul caso rotto** |
| 3 | `boss.Status.Dead` | `boss-gia-morto` | `Status.Dead` = *"la sequenza di morte e' partita"*, la stessa domanda di `KillEnemyLocally` |
| 4 | `!_bossKillsApplied.Add(id)` | `gia-applicata` | **una volta sola per id** |
| 5 | tutto dentro `try/catch` | *(scrive un `ModLog.Error`)* | un'eccezione qui uscirebbe da `ProcessQueue`, che gira in try/**finally** senza catch: **la coda non si svuota e lo stesso pacchetto si riprocessa per sempre** — e' il guasto da **2934 errori** |

**`_bossKillsApplied`** *(`:337`)* e' azzerato **con il resto della memoria di scena**: `:1727` in `LevelLoaded` e `:1989`
in `OnDeathStateReset` ⇒ un boss della scena dopo e' di nuovo uccidibile, e al ritorno del mondo *(dopo una morte,
`SuppressingWorldReset`)* anche.

#### 2.5 ⭐ IL SECONDO GRADINO — `_enemyIds`, e perche' senza sarebbe stato un NO-OP SILENZIOSO

`ResolveEnemyForBossDeath` *(`:460`)* prova **`ResolveEnemy(id)`** e, se tace, scende a **`_enemyIds`** *(istanza → id,
`:182`)* — **l'unica mappa che `ReceiveEnemyKill` NON tocca** e che `ForgetEnemy` lascia intatta **di proposito**
*(`:3996-4021`, "the instance keeps its id on purpose")*. Il marker stampa **`via=cache|istanza|nessuna`**.

**Non e' prudenza: e' la misura di `amount=331`.** L'annuncio arriva **dopo** `ReceiveEnemyKill` *(pacchetto precedente
dello stesso ciclo di invio)*, che toglie l'id da `_enemies` **prima** di applicare il danno; e **il boss non ha spawn
point** *(il suo id e' `en|D17Z01S11|ElderBrother|-806|22`, non `sp|…`)*, mentre `ResolveEnemy` *(`:5040`)* interroga
**prima lo spawn point** e **poi** la cache. ⇒ **senza questo gradino M1 sarebbe stato un no-op silenzioso, e il test non
avrebbe misurato niente** *(esattamente la riga con **DUE SPAZI**: `[Danno] relay  amount=331`, dove l'id e' vuoto perche'
`IdOf`, passando da `ResolveEnemy`, non trova piu' niente)*.
✅ **E che gli id delle due macchine coincidano e' PROVATO, non sperato**: `HandleOf` e' un **FNV-1a della STRINGA**
*(`:824`)*, e in R3-G1 `ReceiveEnemyKill(handle)` **ha risolto** il boss *(`amount=331` e' stato stampato)* ⇒ l'id del peer
ha trovato la **stessa stringa** in `_ids` sul morto.

#### 2.6 Il conteggio dei target: **263 → 263, e nessuna patch nuova**

| | commit | build | deploy |
|---|---|---|---|
| **1. Pulizia probe #1** | `b6e1838` | `Build succeeded`, **0 errori** | bersagli **263** *(era **264**)*, tipo assente **0**, METODO assente **0** |
| **2. M1 — morte del boss** | `afca6fe` | `Build succeeded`, **0 errori** | bersagli **263 → 263**, tipo assente **0**, METODO assente **0** |

⚠️ **M1 NON alza il conteggio.** Non c'e' nessun `Prefix`/`Postfix` nuovo: e' codice dentro **un ciclo che girava gia'**
*(il ciclo di invio)* e dentro **un metodo che girava gia'** *(`ReceiveEnemyDeathGlobal`)*.
**E' la risposta onesta alla domanda "di quanto sale": di ZERO.** *(E il conteggio che doveva tornare a 263 e' tornato a
263 per la **rimozione** della probe #1, non per M1.)* **Deploy verificato**: `bin\Debug\BlasClient.dll` e
`Modding\plugins\BlasClient.dll` hanno lo **stesso hash** SHA256
`2de61ee7973fcedfb2caeb0b7e2479cdfe071bee3c4acf07daddd0b37944fd36`.

#### 2.7 ⚠️ I DIVIETI, riga per riga — come sono rispettati

| Divieto | Come e' rispettato |
|---|---|
| ❌ **NON chiamare `StartCutscene`** | **Zero occorrenze** nel diff: la scena parte **da sola** da `Boss.OnDeath` → `BossFightManager.OnDeathBoss` → `onFinishBossFight` |
| ❌ **NON toccare `SetFreeCamera`** | non citato ⇒ **MAI `FREECAMERA` acceso** |
| ❌ **NON toccare `Core.Input.SetBlocker`** | **zero `SetBlocker`** nel diff, e **zero `ResetManager()`** *(`ResetManager` azzera TUTTI i blocker: `LevelManager.cs:355`)* |
| ❌ **NON toccare `TimeScale`** | non citato *(e `DeadScreenWidget` usa `WaitForSecondsRealtime` apposta)* |
| ❌ **NON toccare il `Penitent` dell'altro** | M1 tocca **il boss locale** e nient'altro: nessun `Penitent`, nessuna camera, nessun guscio |
| ⚠️ **NON toccare i `Prefix` di `AnimationFixes.cs`** | file **non aperto**: nessun `Prefix` nuovo, e nessun `Postfix` aggiunto dove c'e' gia' un `Prefix` |
| ⚠️ **NON toccare `FlagStates`, `FlagHelper`, i suoni della parata, l'esecuzione, il fantasma, l'orfano, lo spam** | **tutti fuori dal diff**. I marker nuovi sono **2 righe per morte di boss** *(TX + RX)*, una tantum per evento |

*(Le stesse righe sono scritte nel commento del codice, `EnemySyncManager.cs:355-363`, cosi' chi legge il codice domani
non deve fidarsi di questo documento.)*

#### 2.8 ⚠️ LE SEI COSE DICHIARATE NON RISOLTE *(e due di queste vanno lette prima del test)*

**1. 🔴 "ENTRAMBI MORTI" = M1 NON FA NIENTE.** E' il **buco noto**: se il sopravvissuto **non esiste**, nessuno uccide il
boss, nessuno manda `[BossDeath TX]`, `ApplyBossDeath` **non viene mai chiamato**, e i due log vedono **due giocatori a
terra davanti a un boss vivo**. **Non e' una svista: nessuna delle tre run copre il caso** *(`PlayerDeathSync.IsEveryoneDead()`
esiste, `:213`, ma **nessun log lo esercita in bossfight**)*. **M1 non lo risolve e non lo simula**, e la via d'uscita
prevista resta **il timeout di M4** — ed e' l'unica. ⚠️ **Da non dimenticare quando si scrivera' M2/M4**: un ritardo
**senza timeout**, con `DEAD_SCREEN` acceso, e' **tastiera bloccata fino al cambio livello** *(solo `ResetManager`,
`LevelManager.cs:355`, la scioglie)*.

**2. ⚠️ La sequenza scriptata di morte del boss NON viene eseguita da `Kill()` ⇒ il boss puo' restare IN PIEDI.**
`ElderBrotherBehaviour.Death()` *(`:319-327`: `StopAllCoroutines` + `UnsubscribeFromEverything` + `StopMovement` +
`jumpAttack.StopJump` + `BehaviourTree.StopBehaviour` + `AnimatorInyector.Death()`)* e' chiamata **solo** da
**`ElderBrother.Damage(Hit)`** *(`:94`)*, e `Kill()` **non passa da li'**. ⇒ **il boss puo' restare in piedi, con
l'animazione di morte non comandata e il behaviour tree ancora vivo**, finche' qualcosa non lo spegne.
**E' il rischio visivo piu' concreto di M1, e NON e' verificabile dai log: va GUARDATO A SCHERMO nel test.**
*E' la stessa riserva che la mod annota a `:254-262` — "the boss's death is not a death animation: it is a scripted
sequence" — e le **16 classi hanno strutture diverse** *(`Crisanta_StDeath`, `PontiffGiantBehaviour.DeathSequence`, …)*:
la posizione della guardia e' verificata per stringa in tutte e 16, ma **valutata solo per `ElderBrother`**.*

**3. 🔴 La lacuna dello STUDIO: chi lancia `CTS02` — non risolta, da provare.** Non si e' potuto stabilire se a lanciare
la cutscene sia **`BossFightManager.onFinishBossFight`** *(`:188`, la strada che `Kill()` percorre)* **o una FSM separata**
che reagisce alla morte del boss. Nei log ci sono **22-51 righe** fra `Metrics.EndBossFight` e `StartCutscene`
*(R1-G1 2690→2721, R3-G2 2785→2814)* **e quelle righe non nominano nessun evento**. **Il fix e' progettato per funzionare
in entrambi i casi** *— non chiama `StartCutscene`, lascia partire la catena nativa e produce **sia** `onFinishBossFight`
**sia** `ENTITY_DEAD` —* **ma la prova e' il test, non il referto.**

**4. ⚠️ Il fallback e' scritto e NON implementato** *(per mandato, non per dimenticanza)*. Se il test dovesse fallire
**con `[BossDeath RX] applicato … Kill=ok` e senza `CTS02`**, la seconda strada e': **riaprire la guardia** *(patch mirata,
solo per il boss e solo quando il giocatore locale e' a terra)* **e mandare il colpo letale**, cosi' che parta anche
`ElderBrotherBehaviour.Death()` *(che `Kill()` salta, punto 2)*. **Non va scritta prima della misura**: questo progetto
ha una storia di fix che hanno rotto cose che funzionavano.

**5. ⚠️ La doppia morte del boss** *(il rischio R3 dello studio)*. `Kill()` **non** e' l'unica strada che puo' portare alla
morte del boss sulla stessa macchina: la catena locale *(colpo letale inoltrato / `ReceiveEnemyKill`)* puo' arrivare
**anche lei**, e in R1-G2 il flag `D17Z01_BOSSDEAD` e' stato settato **due volte** *(2547 e 2581)* **senza danno**.
**Mitigazione di M1**: applica **una volta sola per id** *(`_bossKillsApplied`)* e **solo se `Status.Dead` e' falso**.
**Rischio residuo dichiarato**: se **entrambe** scattassero, il secondo giro sarebbe bloccato da `Status.Dead` ⇒ al massimo
**una** `Metrics.EndBossFight`, **un** `ENTITY_DEAD`. **Da confermare nel log**: `Metrics … successful` deve comparire
**una volta**.

**6. ⚠️ Protocollo misto** *(una macchina con la build vecchia)*. Una macchina vecchia tratterebbe `bdead|` come id di
nemico: finirebbe in `_pendingDeaths`, **inerte**. ⇒ **nessun guasto, ma nessuna protezione**: la macchina vecchia
continuerebbe a perdere la cutscene. **Il test va fatto con ENTRAMBE le macchine aggiornate.**

**⚠️ E gli effetti collaterali LOCALI di `Kill()`, che sono reali e vanno guardati** *(non sono nuovi: sono gli stessi di
una morte uccisa dal giocatore, su QUESTA macchina)*: `onFinishBossFight` + `Metrics.EndBossFight()` *(**l'obiettivo**)* e
`IsFightStarted = false` · **`ENTITY_DEAD`** *sia via `Core.Events.LaunchEvent` sia via l'evento Playmaker —
**ed e' il segnale che una FSM di livello puo' aspettare*** · **Purge al Penitent LOCALE** *(`GetPurge`)*, che al respawn
`Core.Persistence.RestoreStored()` **dovrebbe** ripristinare — ⚠️ **non verificato** · `InventoryManager.OnEnemyKilled`
→ `KillEnemy(e)` su **perle, reliquie, preghiere e spade equipaggiate** del giocatore morto: **un secondo giro** di quegli
effetti per lo stesso boss · `AchievementsManager.AddProgressToAC39/AC41`: progresso **locale**, **puo' contare un boss in
piu'** su una macchina *(. Da guardare, non da correggere adesso.)* · **e NON fa**: nessun `Destroy`, nessuna
disattivazione, nessun blocker, nessun `TimeScale`, nessuna camera, nessuna scrittura di flag, **nessuna vita di nessuno**.
**E non tocca il boss dell'altra macchina**: `bdead|` e' un **annuncio**, non un comando.

#### 2.9 ✅ IL TEST — **e' ancora DA FARE**: M1 e' **IMPLEMENTATO, IN ATTESA DI VERIFICA IN GIOCO**

> **Stato dichiarato, senza abbellimenti: M1 e' implementato, compila, e' deployato, e NON e' mai stato eseguito in gioco.**
> **Nessun referto lo verifica. Va segnato come "IN ATTESA DI TEST", non come chiuso.** *(Nel codice i marker ci sono
> apposta: senza `Kill=ok|nessun-effetto` nel log, "l'annuncio non e' arrivato" e "e' arrivato e non ha fatto nulla"
> restano **indistinguibili** — ed e' esattamente la differenza che il test deve misurare.)*

**Lo scenario, UN BOSS SOLO**: il boss di `D17Z01S11` *(`ElderBrother`)*, log aperti su **entrambe** le macchine, i due
giocatori nella bossroom. **G1 (host) muore e RESTA nel prompt di morte**; **G2 resta vivo e uccide il boss.** *E' la run 3,
cioe' il caso peggiore gia' misurato.*

**L'ordine atteso SUL MORTO (G1), da confrontare con la run 3**:

| # | Riga attesa | Run 3 *(prima di M1)* |
|---|---|---|
| 1 | `[Morte] annuncio inviato: G1 e' morto` · `PLAYERDEAD` · `DEAD_SCREEN enabled` | c'era |
| 2 | il boss **continua a combattere** *(`[EnemyAnim] … vita=330`)* | c'era |
| 3 | **`[BossDeath TX] …`** — ⚠️ **sul VIVO (G2)**, non sul morto | **non esisteva** |
| 4 | **`[BossDeath RX] … applicato (ero morto: si) via=… Kill=ok vitaDopo=0 StatusDead=True`** — ⭐ **la riga nuova sul morto** | **non esisteva** |
| 5 | `[TRACE][METRICS] Boss fight Nº 1 was successful.` **sul morto** | ❌ assente |
| 6 | `cutscene_block enabled` / `[Cutscene BLK] … CINEMATIC=True` **sul morto** | ❌ assente |
| 7 | `ENTITY_DEAD` **sul morto** | ❌ assente |
| 8 | **`[Cutscene EV] … VideoStarted nome="CTS02-Bloody Baptism"`** *(o `CTS02` nel log del gioco)* **sul morto** | ❌ assente |

**Come si legge l'esito — decisione, non impressione**:

| Cosa si vede sul morto | Verdetto |
|---|---|
| `[BossDeath RX] … applicato … Kill=ok` **+ `Metrics … successful`** + `cutscene_block` + **`CTS02`** | ✅ **M1 FUNZIONA**: la morte viaggia e la catena nativa porta la cutscene. **Il buco della voce 58 e' chiuso senza `StartCutscene`, senza camera, senza blocker** |
| `[BossDeath RX] … applicato … Kill=ok` **+ `Metrics`** ma **NIENTE `cutscene_block`/`CTS02`** | ⚠️ **la morte e' arrivata, la cutscene no**: e' la **lacuna §2.8 punto 3**. ⇒ **tocca il fallback (§2.8 punto 4), non un fix affrettato** |
| `… NON applicato … motivo=giocatore-locale-vivo` | ❌ il morto **non era** considerato morto da `PlayerDeathSync.IsLocalPlayerDead()`: guardia troppo stretta ⇒ **si allenta con la misura in mano, non a occhio** |
| `… motivo=boss-gia-morto` | la morte era gia' arrivata per un'altra strada *(doppia catena)*: **verificare chi l'ha uccisa** prima di concludere |
| `… motivo=boss-non-in-scena via=nessuna` | l'id non e' risolvibile sul morto: **o** il morto era uscito dalla stanza **o** gli id delle due macchine divergono ⇒ guardare in quest'ordine, **e i due casi sono distinguibili grazie a `via=`** |
| **`[BossDeath TX]` assente sul vivo** | il boss **non** e' passato dal ramo di morte del ciclo di invio ⇒ **NON e' un problema di RX** |
| `Kill=nessun-effetto` *(+ `ModLog.Error`)* | `Kill()` non ha ucciso il boss: **la seconda strada del piano diventa obbligatoria**, e questo log e' la prova da portare |

**⚠️ Da NON interpretare come guasti** *(dichiarato)*: **la riga `[Cutscene] … StartCutscene …` NON esiste piu'** — e' la
**probe #1**, tolta in `b6e1838` *(§5)*; il testimone equivalente e' `[Cutscene EV] … VideoStarted` *(probe #2)* o il `CTS02`
del log del gioco. **E il boss PUO' restare in piedi e continuare a muoversi per un istante dopo `Kill()`** *(§2.8 punto 2)*.

**⚠️ E una cosa da guardare che c'e' GIA' OGGI e che M1 non introduce**: il mittente manda **anche** `SendEnemyKill(handle)`,
e sul ricevente `ReceiveEnemyKill` fa `_enemies.Remove(id)` **prima** di `ApplyDamage`. Se il pacchetto **nudo**
`SendEnemyDeathGlobal(id)` arrivasse **PRIMA**, il ramo generico troverebbe il boss in `_ids`, chiamerebbe
`DestroyDeadEnemy` → `KillEnemyLocally` *(guardia: non passa, nessun danno)* → **`SetActive(false)` sul boss**: **il boss
SPARIREBBE senza morire**, e la cutscene non avrebbe piu' su cosa girare. **Oggi non e' osservato, ma non e' garantito**:
⇒ **se il boss sparisce dalla scena del morto SENZA `[BossDeath RX] applicato`, e' questo**, e la correzione *— una riga:
non spedire il pacchetto nudo per un boss —* va fatta **con la prova in mano**.

**⚠️ E il morto NON deve uscire dalla stanza mentre la scena parte**: la scena deve essere **ancora caricata**, o
`ResolveEnemyForBossDeath` risponde `motivo=boss-non-in-scena`. **Non serve nessuna modifica: e' il "resta nel prompt"
che l'utente gia' fa.**

---

### 3. LA VOCE 63 (LA COLPA) — verificata, e le mancava **la formulazione dell'utente**

**Verifica del 2026-10-13, come richiesto.** La voce **63 esiste, ed e' leggibile**: sta **in testa alla tabella**
*(promossa dall'audit del 2026-10-12)* e ha la sua **sezione di archivio** in fondo, `### Voce 63 — LA COLPA`
*(che a sua volta **rimanda** alla entry `[2026-10-11 02:00] LA LIBRERIA ESTERNA: verdetto DEFINITIVO`, righe ~3446-3520,
che **resta il punto unico di verita'**)*. **Presenti e corretti**: `GuiltManager` e i **due soli chiamanti** di
`set_MaxFactor` *(entrambi `GuiltManager`, **sempre su `Fervour`, mai su `Life`**)* · `PlayerForever.RefreshGuilt`
*(il nome nel TODO e' **`PlayerFervour.RefreshGuilt`**: la voce in fondo lo scrive correttamente)* · i tre `Image` privati
`guiltStart`/`guiltBar`/`guiltEnd` *(⇒ `Traverse`)* · **la scoperta che le spine vanno DISEGNATE perche' la barra si
riscala** *(`CalculateValue()` clampa `Current` a `CurrentMax`, e `PlayerHealth.BarTarget = Life.Current / Life.Final`
**si riscala** ⇒ la barra risulta **di nuovo piena**)* · il verdetto sulla libreria esterna · il messaggio d'ingresso alla
bossfight · i 6 riferimenti di lettura. **Design completo, implementazione NON iniziata**: `git log --grep` su
*colpa / guilt / spine* → **nessun commit**, e **nessun file del client la implementa**.

**⚠️ COSA MANCAVA, ed e' stato aggiunto** *(alla sezione in fondo, sotto lo "Stato della verifica")*: **LA FORMULAZIONE
DELL'UTENTE**, che non era scritta da nessuna parte ed e' la **definizione operativa** della voce:

> *"la **grief sulla vita** in caso di **morte** e **non** seduta al falò"* — l'utente

⇒ **Due condizioni, e servono entrambe**: **la VITA** *(`Stats.Life`)* e **la MORTE** *(non il falo')*. **Il primo punto e'
il lavoro vero** — `GuiltManager` oggi agisce **solo su `Fervour`**, quindi **estenderlo a `Life` e' il fix**, non riusarlo
com'e'; **il secondo e' un vincolo di design** *(sedersi al falo' NON deve aggiungere Colpa)*, e va scritto perche' e'
esattamente il tipo di cosa che si perde e poi si implementa al contrario.

**⚠️ E la voce NON e' stata toccata oltre a questo**: la sua sezione di design in fondo **resta com'era**.

---

### 4. LA VOCE 9 (LO SPETTATORE) — **la descrizione era sbagliata, ed e' stata corretta**

**Cos'ha accertato l'audit, e cosa dice il codice.** La cella della voce 9 raccontava un lavoro di **camera** con i pezzi
gia' identificati *("fermabile su `DeadScreenWidget.OnDead`", "body invisibile", "camera sul compagno = banale")* e un solo
ostacolo di motore. **La realta' e' un'altra, in due meta':**

**✅ QUELLO CHE C'E' DAVVERO** *(ed e' codice reale e di qualita')*: il **livello di STATO** e' costruito —
`Players\PlayerStatus.cs` e `Players\PlayerDeathSync.cs` implementano **`pdead|` / `palive|`** sul canale
`EnemyDeathGlobal` *(senza bump)*, `SetPlayerDead`, `CountDeadPlayers`, `IsEveryoneDead`, `AllConnectedPlayers`,
e usano **`DeathEventLaunched` del gioco** come guardia del "una volta per morte" **invece di una flag propria**.
⚠️ **Ma quel codice e' stato scritto per UN'ALTRA VOCE — la 44** *(sopprimere il respawn del mondo alla morte)*,
**non per la 9**, e con un'altra forma.

**❌ QUELLO CHE NON C'E'**: **la camera sul compagno NON esiste**, **il body invisibile NON esiste**, e il
**"segue i cambi stanza" resta BLOCCATO DAL MOTORE** *(le stanze sono **scene Unity**, `OnLevelLoaded` azzera lo stato
per-scena, `OtherPlayerManager` distrugge gli oggetti fuori scena, e `ProCamera2D` ha i **bordi geometrici per stanza**)*.
**Nessun referto lo verifica in gioco**, e **nessun commit chiude la voce**: **9 commit citano la parola "spettatore",
e nessuno la chiude** — `PlayerDeathSync.cs:293` e `:339` parlano di *"spectator camera"* in forma **condizionale**
*(«the spectator camera would have nothing to follow»)*: e' una **premessa di design**, non una funzione.

**⇒ Da "quasi fatta" a "lo stato c'e', il visibile no".** *(La cella in testa e' stata **marcata**, non riscritta: il testo
originale resta, com'e' giusto per un documento storico.)*

#### 4.1 ⭐ COSA LO `STUDIO-BOSSONE-MORTE-CAM.md` HA TROVATO DI UTILE PER IL FUTURO

Tutto **verificato in IL**, e sono i pezzi che rendono la voce 9 **fattibile nella stessa stanza** — e **solo** li'.

1. **⭐ Il morto ha ANCORA IL SUO Penitent.** `Core.Logic.Penitent` e' **valido** mentre si e' a terra:
   `PlayerDeathSync.IsLocalPlayerDead()` *(`:108`)* legge `Core.Logic.Penitent.Status.Dead` **a ogni frame**, e
   `PenitentOnUpdateDeath_Patch` ci gira sopra *(`:318-331`)*. **Il Penitent non viene distrutto alla morte: viene
   ricostruito al load** *(`SpawnManager.CreatePlayer`; il marker `[Probe] PENITENT …` compare **dopo** il respawn)*.
   ⇒ **spostare la SUA camera e' legittimo e non tocca quella dell'altro** *(l'altro **non ha** una camera: e' un guscio)*.
2. **⚠️ `SetFreeCamera` NON e' riusabile.** Il *perche'* e' doppio, e il secondo non era noto:
   *(a)* accende **`SetBlocker("FREECAMERA", …)`** *(`Cinematics.cs:219`)* — blocker **non refcounted**, divieto n.1;
   *(b)* **il ripristino NON e' idempotente**: `if (!lastCameraValid)` *(`:196`)* ⇒ se il gioco ha **gia'**
   `lastCameraValid == true` *(una cutscene in corso, o una `SetFreeCamera(true)` mai richiusa)* **la nostra posizione non
   viene salvata**, e alla chiusura `MoveCameraInstantlyToPosition(lastCamera)` **ci teletrasporta altrove**.
   **⇒ Va IMITATO (salva → stacca → ripristina), non chiamato.**
3. **⭐ LA RICETTA GIUSTA, e senza blocker**: salvare `ProCamera2D` *(`FollowHorizontal`, `FollowVertical`, le due
   smoothness, `OverallOffset`)* **e `CameraPlayerOffset.PlayerTarget`**, poi **`RemoveAllCameraTargets()` +
   `AddCameraTarget(<guscio dell'altro>.transform, …)`**, e **al `OnPenitentReady` rimettere tutto**.
   **In alternativa, zero interazione con i target**: `Cinematics.SetFreeCameraPosition` *(`:222`, **pubblica**, senza
   blocker)* ogni frame — costo: si perde lo smoothing.
   ⚠️ **E il legame non si riscrive da solo**: `CameraManager.UpdateNewCameraParams` **non gira ogni frame** — il suo
   **unico chiamante** e' `LevelManager.cs:349`, dentro la coroutine di caricamento livello ⇒ **si esegue UNA VOLTA PER
   LIVELLO**. **Conseguenza a favore**: chi sposta la camera **resta spostato** fino al load successivo *(la cam **non**
   sopravvive a un cambio scena — e nella nostra versione non serve: siamo nella stessa stanza)*.
4. **⚠️ L'ATTrito del `tag == "Penitent"`.** `CameraPlayerOffset.SetCameraTarget` *(`:139-157`)* accetta **solo** un
   `CameraTarget` il cui `TargetTransform.tag == "Penitent"`; **il guscio nasce `new GameObject("_" + name)` SENZA tag**
   e senza `Penitent` *(`OtherPlayerManager.cs:295-345`)* ⇒ **o si da' il tag al guscio**, o si muove la cam a mano con
   `MoveCameraInstantlyToPosition` *(che **e' gia' pubblica**)*. **Decide una riga di codice, non un'architettura.**
5. **🔴 IL TIMEOUT E' OBBLIGATORIO — e c'e' un secondo spegnitore di `DEAD_SCREEN`.** `OnPenitentReady` *(`:161`)* e'
   l'unico spegnitore **nel gioco normale** *(`SetBlocker("DEAD_SCREEN", false)` compare due volte in tutto l'assembly:
   `:101` dentro `OnDeadActionBossRush`, e `:161` dentro `OnPenitentReady`)*. **Ma** `LevelManager.cs:355`
   **`Core.Input.ResetManager()` azzera TUTTI i blocker a ogni load** ⇒ *un ritardo **senza uscita** con `DEAD_SCREEN`
   acceso = **tastiera bloccata fino al cambio livello**, e la scioglierebbe solo un cambio scena.* ⇒ **un timeout di
   sicurezza che esegue il `Respawn()` normale e' parte del fix, non un extra.**
6. **⭐ Il caso del morto e' il migliore possibile**: la sua camera e' **gia' puntata sul suo cadavere**, e il cadavere e'
   **nella stessa stanza** del vivo. **Nessun cambio-scena, nessuna scena non caricata: il vincolo "l'avatar esiste solo
   nella tua scena" qui NON morde.** E il mondo **continua a girare**: `DeadScreenWidget.OnDeadAction` *(`:67-89`)* e' una
   coroutine con `WaitForSecondsRealtime` e un ciclo `while (!Input.anyKey)`, e **niente in essa ferma il mondo** *(il
   `Respawn()` avviene **solo dopo** il tasto)* ⇒ **il posto dove agganciarsi c'e' gia'.**
7. **⚠️ E il predicato "in bossfight", se servira'**: due vie **verificate** e **per-oggetto** —
   `BossFightManager.IsFightStarted` *(`:47`, richiede `FindObjectsOfType<…>(true)`)* per *"il combattimento e' in corso
   ADESSO"*, e **`EnemySyncManager.IsBoss`** *(`:273`, **gia' nostro**)* per *"questo nemico e' un boss"*.
   **Scartati, e va detto**: `LevelHasGeometryBoundaries` *(**vero ovunque**: tutte le stanze hanno collider)*,
   `BossAwarenessArea` *(un `[Serializable]`, non un componente, nessuna logica)* e la riga `[TRACE][METRICS] Boss fight`
   *(e' `PlayerPrefs` + `Log.Trace`: **leggerla da codice per pilotare una decisione significa leggere `PlayerPrefs`, non
   lo stato del gioco**)*.

**⚠️ E il piano dell'utente, valutato dallo studio**: *"non far uscire dalla bossroom"* e' **utile ma NON risolutivo**
*(la run 3 lo falsifica: il morto era in `D17Z01S11` e la scena non e' partita lo stesso)*, *"aspettare i trigger, poi il
prompt"* e' **utile ma non e' il collo di bottiglia** *(il prompt non era la causa; e il trigger della cutscene e quello del
boss sono **lo stesso evento**: aspettare "il trigger della cutscene" e' **circolare**)*, *"la cam su un giocatore vivo"* e'
**fattibile, economico e sicuro**, e *"i suoni partono fino al tasto"* e' **VERO e verificato in IL**.
**⇒ IL BUCO, in una riga**: *"non far uscire le persone dalla bossroom"* non serve a niente finche' **il boss non puo'
morire sulla macchina di chi e' a terra** — **ed e' M1, §2**.

---

### 5. LE COSE PICCOLE *(seguite in questi giorni, e il loro stato reale)*

**(a) Lo spam del log: due fix fatti, e UNO NO.** *(`REFERTO-SPAM-LOG.md`)*

| Spam | Commit | Misura |
|---|---|---|
| `[Proprieta']` — 1700 righe su G1 | `d3f54c9` | **1700 → ~40** *(-97,6%)*: la riga esce **solo quando la decisione cambia** *(`Dictionary<string,string> _deadReleaseLogged`, **chiave = id, valore = la DECISIONE**, mai il motivo testuale — la trappola che il progetto ha gia' fatto due volte)* |
| `Couldn't find connected player: *` — 309 righe su G2 | `ff5dec7` | **309 → 1 per nome**: `HashSet<string> _missingPlayersReported` |

**E il NUMERO SBAGLIATO nel TODO e' stato corretto dall'audit**: le righe **844** e **3212** dicevano **32** *"nella
finestra di handshake"*; il vero e' **309**, **e non e' un handshake**: e' il **SENTINELLA `"*"`** che il server usa per il
sync iniziale di squadra *(`Server.cs`, `getProgressPacket("*", …)`)* → **nessuno si chiama `"*"`** ⇒ **una domanda senza
risposta possibile**. *(E **nessun pacchetto veniva scartato**: `ProcessRecievedStat` fa `if (player == null) return;`, e il
progresso vero e' gia' stato applicato **una riga sopra** da `ProgressManager.ReceiveProgress`.)*

**⚠️ E IL SECONDO INTERVENTO — LA CAUSA A MONTE — NON E' STATO FATTO.** *(`REFERTO-SPAM-LOG.md:127-133`)*
Il fix di `ff5dec7` toglie **la ripetizione**, non **la domanda**: `ReceiveProgress` **continua a interrogare il registro
anche per il sentinella `"*"`**. La correzione sarebbe in **un punto solo**
*(`if (playerName != "*") { ProcessRecievedStat(...); DisplayProgressNotification(...); }`)* ed e' **equivalente**
*(per `"*"` la ricerca ritorna sempre `null`)*, risparmiando **309 scansioni per sessione**.
**⛔ NON e' stato implementato, ed e' dichiarato: e' LOGICA, non logging**, e la consegna di quei due commit toccava
**solo il quando si logga**. ⇒ **resta un lavoro reale e non e' una voce numerata.**

**(b) LA PROBE RIDOTTA — cosa c'e' in giro, adesso, e perche' nessuno la creda abbandonata.** *(`b6e1838`)*

La probe sulle cutscene *(`Blasphemous.Multiplayer.Client\CutsceneProbe.cs`, nata in `b86bd3f`)* era **tre** probe di
solo log. **Ne restano DUE, entrambe a costo ZERO, e la terza e' stata TOLTA:**

| # | Dove | Costo | Cosa scrive |
|---|---|---|---|
| **#0** | `CutsceneProbe.Tick()`, da `Multiplayer.OnLateUpdate` *(ciclo che **girava gia'**)* | **nessuna patch** | **`[Cutscene BLK] t=… CINEMATIC=<bool> InputBlocked=<bool>`** — **una riga SOLO quando il valore cambia** |
| ~~#1~~ | ~~`Prefix` su `Cinematics.StartCutscene`~~ | ~~**1 patch**~~ | **⛔ TOLTA in `b6e1838`** *(264 → **263**)*: la riga `[Cutscene] … StartCutscene nome="…"` **non esiste piu'**, e cercarla in un log nuovo **non e' un guasto: e' voluto** |
| **#2** | `Subscribe()` in `Multiplayer.OnInitialize`, `Unsubscribe()` in `OnDispose` | **nessuna patch** | **`[Cutscene EV] t=… CinematicStarted / CinematicEnded cancelled=… / VideoStarted nome="…" / VideoEnded`** |

**Perche' la #1 e' stata tolta** *(tre ragioni, in ordine)*: **(1)** era **l'unica che costava una patch Harmony**
*(264 → 263, verificato da `tools\verify-patch-targets.ps1`)*; **(2)** la sua domanda e' **chiusa** — serviva a costruire la
tabella `flag → nome scena`, e **l'utente ha deciso di NON fare il fix delle cutscene condivise** *(§1)*, quindi la tabella
non serve piu' a nessuno; **(3)** misurava **la chiamata**, non l'esito *(nome, tipo, fade, `giaInCorso`)*, e l'esito lo
dice gia' la **#2**. **⚠️ NON RIMETTERE un `Prefix` su `StartCutscene` "per completezza"**: e' esattamente il bersaglio
tolto, e ogni patch in piu' e' un bersaglio in piu' che puo' fallire a runtime.

**E perche' #0 e #2 RESTANO — serviranno a M3/M4, e non e' una speranza:**
- **#0 (`[Cutscene BLK]`) e' LA MISURA CHE CONTA ADESSO**, per una ragione precisa: `CINEMATIC` ha **DUE proprietari** e
  **NON e' refcounted** *(`Cinematics` `:134`/`:253` e `TresAngustiasMasterBehaviour` `:614`/`:639`/`:691`)*, e
  **l'accensione del blocker e' IL PRIMO SEGNO DELLA FSM DI MORTE DEL BOSS** *(`cutscene_block has been enabled` e' la
  stessa accensione vista dal gioco)*. ⇒ **dice se la catena di morte e' partita anche quando nessuna cutscene e'
  ancora nominata nel log** — ed e' **il rischio dichiarato di M3/M4** *(`STUDIO-BOSSONE-MORTE-CAM.md` §1.4-bis: quella
  riga esiste **solo sulla macchina che ha ucciso il boss** su tutte e tre le run)*. **E' anche il candidato naturale per
  l'aggancio della cam di M3** *(sapere quando una scena e' in corso, **senza** toccare `Cinematics`)*.
- **#2 (`[Cutscene EV]`) e' l'UNICA COSA CHE DICE SE UNA SCENA E' IN CORSO E SE IL CONTENUTO E' PARTITO DAVVERO**, ora che
  la #1 non c'e' piu'. E il testimone vero e' **`VideoStarted`**, non `CinematicStarted`: quest'ultimo viene invocato
  *(`:148-151`)* **anche** quando `PlayVideo` e' morto dentro il `catch (NullReferenceException)` di `:343-347` — e in quel
  caso `cinematicRunning` resta `false`, `EndCutscene` e' un **no-op** *(`:156`)* e **il blocker `CINEMATIC` non viene
  piu' spento**. **E' il caso peggiore, e senza `VideoStarted` sarebbe indistinguibile da una partenza riuscita.**
- **E la #0 misura anche il caso "blocker rimasto acceso"**: con il **timeout obbligatorio** di M4 *(§4.1 punto 5)*, la
  riga `CINEMATIC=False` a fine test e' **la prova** che la rete di sicurezza ha tenuto.

**(c) LA VOCE 56 (i 5 MARKER) E' CHIUSA DALL'AUDIT — e va trovata chiusa.** *(se qualcuno la cerca)*
La cella lo dice gia': **`✅ CHIUSA — i 5 marker sono stati TUTTI implementati`** con la prova dentro
*(`dbcc9f4` "MARKER 1, 2, 4, 5 — i quattro locali, con verifica nell'IL di ognuno" + `ff887a0`)*, e con il riscontro nel
codice: `FullId(id)` accanto a `ShortId` *(`EnemySyncManager.cs:3144`)* · `t={Time.time:F1}` *(`:687`, `:2908`)* ·
i conteggi del **peer** · la riga quando `FindIdOfHandle` fallisce *(`:680`, `:3385`, `:3847`)* · i marker del parry.
**⚠️ La riga in testa diceva ancora "MANCANO 5 MARKER": e' superata** — l'audit l'ha marcata, e **il testo originale
resta** per storia *(la chiusura e' visibile nella stessa cella)*.

**📌 E la tabella `flag → nome scena` che la probe #1 doveva costruire NON e' stata costruita, e NON serve piu'** *(§1)*:
era lo strumento per decidere la **via (a)** *("far partire la scena all'altro")*, e quella via e' stata **respinta perché
non serviva**. **La #1 e' stata tolta proprio per questo: non c'e' nessun lavoro interrotto a meta'.**

---

### 6. 📌 LA LISTA DELLE COSE DA FARE *(corta, in ordine di importanza — verificata contro il file)*

**1. ⭐ LA CAM in caso di morte nella bossroom — voce 9.** *"Vedere l'altro quando sono morto"*. **La parte camera non
esiste** *(il livello di stato si', ed e' della voce 44)*. La ricetta e' in **§4.1 punto 3**; l'attrito e' al **punto 4**;
il **timeout e' obbligatorio** *(punto 5)*; e **il "segue i cambi stanza" NON esiste** *(non pianificarlo, non stimarlo,
non prometterlo)*. **Dipende da M1** *(§2)*: prima la morte del boss, poi la cam.

**2. ⭐ LA COLPA — la grief sulla vita morendo non seduti al falo' — voce 63.** *Formulazione dell'utente, §3:*
**`Stats.Life`, non `Fervour`** · **morendo, non al falo'**. Design completo, `GuiltManager` presente, arte esistente
*(`guiltStart`/`guiltBar`/`guiltEnd`)*, **implementazione non iniziata**. ⚠️ **Il messaggio d'ingresso alla bossfight costa
una chiave di localizzazione + una chiamata a `DisplayNotification`** *(`NotificationManager` esiste gia')*: e' il pezzo
a costo quasi nullo della stessa voce.

**3. I TEST DEI BOSS — voce 18.** **Resta la voce piu' esposta**: nei referti e' esercitato **un solo boss**
*(`ElderBrother`, `D17Z01S11`)*, e la barra del boss *"coincide per coincidenza, non per sincronizzazione"* *(§2 del
documento)*. **E da qui passa anche il test di M1** *(§2.9)*: **UN boss solo, con i log aperti su entrambe le macchine**,
e **da guardare a schermo** *(il boss puo' restare in piedi: §2.8 punto 2)*.

**4. LE COSE PICCOLE** *(§5)*: **(a)** lo **spam a monte del `"*"`** — il taglio di `ProcessRecievedStat` per il
sentinella, **NON implementato**, *"va deciso a parte"* *(309 scansioni per sessione, ed e' **logica, non logging**)* ·
**(b)** i **due residui di `BUG-NOTI.md`**: la **seconda `Object.Destroy` in `SettleStuckDeaths`** *(`[EnemyDeath]` = 0 in
ogni sessione misurata: ha un commento nel codice che dice cosa fare la prima volta che appare in un log → diventare
`SetActive(false)`)* e l'**esecuzione "incrociata"** *(il caso **generale** del fantasma e' chiuso — voce 57; questo residuo
raro **non e' riproducibile a comando** e chiede il log completo di **entrambe** le macchine, non un fix a naso)* ·
**(c)** il **`fullPathHash`** *(`§8 passo 4c`)*: **parcheggiato di proposito** *(`7c638e5`)* — **da riprendere solo se** la
fluidita' tornasse a dare fastidio, **non alzarlo di priorita' senza un sintomo nuovo**.

**5. LE DOMANDE APERTE DEL §9.** Due, e sono le sole: **§9 domanda 15** *(il fantasma e' congelato dalla mod?)*:
`PauseEnemy` ferma il behavior tree e la fisica, **non `MonoBehaviour.Update`** ⇒ `Ghost::OnUpdate` gira **anche sul
non-proprietario**: **test mai fatto** *(loggare l'orientamento **mentre l'altra macchina possiede il nemico**)*.
**§9 domanda 14** *(`EnemyAI` gira davvero?)*: **0 occorrenze** nel codice della mod, **0 sottoclassi**: si decide
**guardando i prefab**, ed e' l'unico modo di sapere se `LookAtTarget` e' davvero codice morto su **71/71 sottoclassi**.

**⭐ E UNA COSA NON E' IN LISTA, MA VA DETTA: M1 e' IMPLEMENTATO E NON VERIFICATO IN GIOCO.** *(`afca6fe`)*
**Non e' chiuso, e non e' aperto come gli altri**: e' **"implementato, in attesa di verifica"**. Finche' il test di §2.9
non e' stato fatto, **la cutscene del boss per chi e' a terra potrebbe ancora perdersi** — e il **fallback e' scritto e
NON implementato** *(§2.8 punto 4, per mandato: prima la misura)*. **Va segnato cosi', non come chiuso.**

**✅ E DUE VOCI CHE NON SONO IN LISTA PERCHE' SONO CHIUSE:** la **58** *(chiusa **per decisione**, non per fix: §1)* e la
**56** *(i 5 marker, chiusa dall'audit: §5(c))*. **Se qualcuno le cerca, deve trovarle chiuse — non "in sospeso".**

---

## [2026-10-14 02:00] LA VOCE 9 — **CHIUSA** *(in due pezzi)*, E UN GUASTO APERTO CHE HA CAMBIATO LA REGOLA

**Catena dei commit di questa sessione** *(verificata con `git log --oneline -12`)*:
`91acd22` *(Passo 0 — la MISURA della schermata di morte)* → **`b9f8360`** *(Passo 1 — la schermata di morte **TRASPARENTE**)* →
**`04a8fad`** *(la cam del morto segue il vivo, via (c) il cadavere-copia)* → **`68f3b0e`** *(FIX 1 — il cadavere del morto non
resta piu' in piedi)* → **`18a4500`** *(FIX 2 — il boss recita la sua morte scriptata anche su questa macchina)*.
**HEAD verificato all'inizio della redazione era `18a4500`** *(e non e' avanzato: `git status --porcelain` vuoto, nessun
commit piu' recente)*. **Un altro agente stava lavorando al GUASTO URGENTE del §3 mentre questa sezione veniva scritta: se
HEAD e' avanzato oltre `18a4500`, la riparazione e' arrivata DOPO questa sezione — e il §3 va riletto come "com'era".**
I referti sono in `scratch\`: `STUDIO-DEATH-SCREEN-TRASPARENTE.md` *(lo studio)*, `REFERTO-DEATH-SCREEN-TRASPARENTE.md` *(la
misura + la trasparenza)*, `REFERTO-CAM-SEGUE-VIVO.md` *(l'inseguimento del vivo)*, `REFERTO-MORTE-IN-PIEDI-E-BOSS.md` *(i due
ritocchi)*.

**Perche' questa sezione esiste.** La **voce 9** — *"modalita' spettatore alla morte"*, la voce su cui si e' lavorato di piu' in
tutto il progetto — **e' chiusa**, e non da un fix solo: da **due pezzi** che sono **due idee dell'utente**, e il secondo dei due
ha **introdotto un guasto che blocca il gioco**. Le due cose vanno lette insieme, perche' la seconda e' la lezione della prima.

---

### 1. ⭐⭐ LA VOCE 9 — **CHIUSA** *(e va raccontata per intero: e' la voce su cui si e' lavorato di piu')*

**Cos'era.** *"Modalita' spettatore alla morte"* — **vedere l'altro quando sei morto**. La cella in testa la raccontava come un
lavoro con i pezzi gia' identificati *("fermabile su `DeadScreenWidget.OnDead`", "body invisibile", "camera sul compagno")* e un
solo ostacolo di motore; l'audit del 2026-10-12 ha accertato che **il livello di stato era stato costruito per un'altra voce (la
44)** e che **camera e body non esistevano** *(§4 della sezione `[2026-10-13 10:00]`)*. **Oggi e' risolta — in DUE PEZZI.**

#### 1.1 ⭐ IL PEZZO PRINCIPALE — **LA SCHERMATA DI MORTE TRASPARENTE** *(l'idea dell'utente)*

> **Non si muove la camera: si toglie il velo.**

**Com'e' fatta la schermata, e perche' la soluzione e' quella.** La schermata di morte **non e' una `Image`**: e' lo stato
visibile di **quattro `CanvasGroup` pubblici** di `DeadScreenWidget` — `NormalGroup` *(lo sfondo/disegno di morte, `:30`)*,
`BossRushGroup`, `DemakeGroup` e **`ContinueGroup` *(il prompt, `:39`)*** — e **l'unica cosa che accende e spegne la schermata sono
i loro `alpha`** *(`HideAll()` `:208-214`; le due `DOFade` `:82` e `:85`)*. **Prova che il prompt NON e' sullo stesso oggetto dello
sfondo**: `ContinueGroup` e' condiviso col percorso **Demake** — `OnDeadDemake` *(`:165-186`)* accende `DemakeGroup` **e**
`ContinueGroup` **senza mai toccare `NormalGroup`** *(che resta a 0 da `HideAll`)* ⇒ **se il prompt fosse figlio dello sfondo, in
Demake sarebbe moltiplicato per 0 e non apparirebbe mai.** *Prova per contraddizione, non per induzione* `(REFERTO-DEATH-SCREEN-TRASPARENTE.md:137-140)`.

**La modifica, in due righe** *(`DeathScreenTransparency.cs`, commit `b9f8360`)*:
```csharp
Graphic[] graphics = widget.NormalGroup.GetComponentsInChildren<Graphic>(true);   // Image/RawImage/Text
graphic.color = new Color(color.r, color.g, color.b, 0f);                          // SOLO l'alpha
```
- ⭐ **Si azzerano SOLO i `Graphic` FIGLI di `NormalGroup`, NON `CanvasGroup.alpha`.** Il motivo e' verificato:
  `OnDeadAction` fa `NormalGroup.DOFade(1f, 2.15f)` *(`DeadScreenWidget.cs:82`)* ⇒ **il tween riscrive `CanvasGroup.alpha`**, e una
  scrittura una-tantum verrebbe **annullata in 2,15 s**. L'alpha del `Graphic` **non e' animato da `DOFade`** (il `CanvasGroup` lo
  **moltiplica**): `color.a = 0` × qualunque cosa = **0** ⇒ **una scrittura per morte, zero DOTween da referenziare, nessuna lotta
  di frame** `(REFERTO-DEATH-SCREEN-TRASPARENTE.md:129-133)`.
- **`NormalGroup` e' `public`** *(`DeadScreenWidget.cs:30`)* ⇒ **nessun `Traverse`**.
- **Si azzera SOLO `a`**: l'RGB resta intatto, cosi' un eventuale secondo intervento *("solo in bossfight")* puo' rimettere
  l'alpha originale **senza indovinarla**.
- **Zero patch Harmony**: la `Tick()` gira in `Multiplayer.cs:184`, dentro `OnLateUpdate` — **il ciclo per frame che girava gia'**.
  **Bersagli `263 → 263`**, `tipo assente 0`, `METODO assente 0`, un solo deploy, e **il DLL buildato ha lo stesso hash di quello
  deployato** *(SHA256 `4da77a45…`)* `(REFERTO-DEATH-SCREEN-TRASPARENTE.md:11-31)`.
- **Perche' non si tocca altro**: il respawn e' `Core.Logic.Penitent.Respawn()` **dentro il callback del fade** *(`:91-96`)* e **non
  legge nessun alpha**; l'uscita e' `while (!Input.anyKey)` *(`:87-90`)*; e il blocker **`DEAD_SCREEN` non si tocca** — lo spegne il
  gioco *(`OnPenitentReady` `:101`)*, e l'unico altro spegnitore e' `LevelManager.cs:355 ResetManager()` **a ogni load**.

**✅ VERIFICATO IN GIOCO** *— e questa e' la prima volta in questa voce che la distinzione conta:*
- ✅ **mai il nero** durante la morte,
- ✅ **il prompt resta** *(e' `ContinueGroup`: non e' stato toccato)*,
- ✅ ⭐ **la bossfight e la cutscene si vedono**,
- ✅ **e l'utente ha confermato: "tutto giusto funzionante".**

#### 1.2 ⭐ E LA SCOPERTA CHE L'HA RESO SUFFICIENTE: **la camera del morto E' GIA' NELLA BOSSROOM**

**Non era una deduzione, era una misura** *(`91acd22`, `[DeathScreen] misura`)*. Il legame camera↔Penitent lo scrive
`CameraManager.cs:105` *(`AddCameraTarget(Core.Logic.Penitent.transform, …)`)*, e le due chiamate
`RemoveAllCameraTargets()`/`AddCameraTarget()` esistono in **un solo punto di tutto l'assembly**, con **un solo chiamante**
(`LevelManager.cs:349`, dentro la coroutine di load) ⇒ **si scrive UNA VOLTA PER LIVELLO e non si riscrive mai mentre si e' a terra.**
**Misurato**: `cam` **costante**, `target` a **0,5 unita'** da `posMia`, e `posMia` **ferma** — **52 campioni su ~102 s**
*(`[DeathScreen] misura INIZIO t=184.21` → `FINE … campioni=52`)* `(REFERTO-CAM-SEGUE-VIVO.md:30-33)`. *(E in R3-G1, la run dello
studio: `posMia=(-806.2,10.0)` **identica in 6 campioni** da `DEAD_SCREEN enabled` al `Respawn`, ~24 s, mentre il boss camminava e
arrivava a `dist_io=0.9` — `STUDIO-DEATH-SCREEN-TRASPARENTE.md:52-55`.)*
⇒ **Il cadavere e' nella bossroom, la camera lo inquadra, e il mondo dietro la schermata gira**: togliere il velo **basta** — e
infatti **la cutscene del boss si vede** *(`CTS02-Bloody Baptism`: `VideoStarted` ×5, `CinematicEnded cancelled=False`, `t=235.80`
→ `t=262.25`, con la finestra di morte aperta da `t=184.21` a `t≈286.67`)* `(REFERTO-CAM-SEGUE-VIVO.md:233-237)`.

#### 1.3 ⭐ L'EXTRA — **LA CAM DEL VIVO** *(l'idea dell'utente, il "cadavere-copia")* — commit `04a8fad`

> **L'idea dell'utente**: *«il morto potrebbe anche utilizzare le posizioni comunicate gia' direttamente dal peer… e spostare il
> proprio corpo invisibile li'.»* — **ed e' esattamente la strada presa** `(REFERTO-CAM-SEGUE-VIVO.md:63-67)`.

**Il fatto che decide tutto**: `CameraManager.cs:105` lega la camera a `Core.Logic.Penitent.transform`, quel legame si scrive una
volta per livello e non si riscrive mai da morti ⇒ **spostare il Penitent E' spostare la camera.** *Nessun `RemoveAllCameraTargets`,
nessun `tag` da dare al guscio dell'altro, nessun primo contatto della mod con `ProCamera2D`* **(`zero occorrenze` nel file nuovo)**
`(REFERTO-CAM-SEGUE-VIVO.md:22-28, 401-406)`.

**Come e' fatto**: si crea una **copia del cadavere** dove si e' caduti *(`Instantiate` dell'albero visivo sotto `_coop_death_corpse`,
poi `Freeze`: **ogni `Behaviour` spento**, `SpriteRenderer` escluso — non e' un `Behaviour`)*, si nasconde il **vero** corpo **solo
nell'alpha** *(`body.color.a = 0`, non `SetActive(false)`, non `SpriteRenderer.enabled`)*, e il corpo vero **si sposta verso l'avatar
del peer** `(REFERTO-CAM-SEGUE-VIVO.md:163-181)`.

**⭐ E la posizione si legge in LOCALE: zero canale nuovo.** Non serve niente di nuovo, perche' **la mod ha gia' trasformato quella
posizione in un avatar su questa macchina**: `ReceivePosition` → `OtherPlayerManager.ReceivePosition` → `transform.position`
*(`NetworkManager.cs:187-194` → `OtherPlayerManager.cs:434-448` → `OtherPlayerScript.cs:766-772`)* ⇒ **si legge
`transform.position` del SUO avatar** — la stessa fonte che il morto vede a schermo *("seguo cio' che vedo": coerente per
costruzione)* `(REFERTO-CAM-SEGUE-VIVO.md:69-82)`.

**⚠️ E le scelte da registrare — ognuna con la sua ragione:**

| Scelta | La ragione, con la prova |
|---|---|
| **Da morti NON si spedisce piu' posizione/animazione/direzione** | la posizione e' spedita a **0,2 s** leggendo `Core.Logic.Penitent.transform.position` *(`MainPlayerManager.cs:94`)* **senza guardia di morte** ⇒ se il Penitent si sposta e l'invio resta acceso, **il peer vede l'avatar del morto addosso al suo** — *peggio del difetto che si sta correggendo*. **E si ferma TUTTO il blocco, non solo la posizione**, per una trappola trovata in IL: **`OtherPlayerScript.cs:778-782` usa l'arrivo di un'animazione come PROVA DI VITA** — `if (playerDead) SetDeathStatus(false);` ⇒ **una sola animazione spedita da morti RIMETTEREBBE IN PIEDI l'avatar del morto sull'altra macchina**. *(Non era nel mandato: e' stata trovata leggendo `OtherPlayerScript`.)* E **non e' una bugia**: la posizione non e' un segnale di vita — la vita viaggia su `pdead\|`/`palive\|` *(`PlayerDeathSync.cs:205-212`)* `(REFERTO-CAM-SEGUE-VIVO.md:104-143)` |
| **L'inseguimento si mette in pausa con `HasBlocker("CINEMATIC")`** | ⚠️ **era un rischio DICHIARATO**, non una scoperta: *"se un trigger della scena dipende dalla posizione del Penitent, muoverlo potrebbe far scattare qualcosa"*. `CINEMATIC` si **legge e basta** *(`InputManager.cs:210`, nessun side-effect: **zero `SetBlocker` in tutto il file nuovo**)*, e **il gate non toglie niente**: durante la cutscene `Cinematics.cs:272` spegne `GameplayUI` ⇒ non c'e' nessuna camera da guidare `(REFERTO-CAM-SEGUE-VIVO.md:213-241)` |
| **Fusibile 360 s che LOGGA** | la finestra misurata e' di **~102 s** *(`t=184.21` → `t≈286.67`: il morto ha tenuto il prompt per tutta la bossfight ~51 s **e** per tutta la cutscene ~27 s)*: un fusibile piu' corto del caso reale **spegnerebbe l'inseguimento proprio durante la cutscene**, cioe' il caso per cui la funzione esiste. **6 minuti stanno larghi e restano un fusibile vero** — e la chiusura **logga** *(`finestra chiusa: FUSIBILE scattato dopo N s`)* `(REFERTO-CAM-SEGUE-VIVO.md:270-274)` |
| **Fuori scena resta sul cadavere** | l'avatar del peer **esiste solo nella stessa scena** *(`LevelLoaded` → `RemoveAllActivePlayers`, `OtherPlayerManager.cs:24-27, 331-338`)* ⇒ `FindLivingPeer` → null e **il morto resta sul cadavere**. E' il ripiego naturale: se non c'e' nessuno da guardare, l'unica cosa sensata e' il proprio corpo `(REFERTO-CAM-SEGUE-VIVO.md:92-98)` |
| **Il caso "entrambi morti" e' DICHIARATO, non risolto** | non c'e' nessun vivo da seguire ⇒ **il Penitent NON si muove** e la camera resta sul corpo: **e' il comportamento di oggi, quindi nessuna regressione**, e la riga lo dice una volta per finestra `(REFERTO-CAM-SEGUE-VIVO.md:245-257)` |

**Le quattro vie di uscita, ognuna col suo marker**: il **respawn** *(il tasto)* · la **disconnessione/fine sessione**
*(`Multiplayer.OnDisconnect`/`OnDispose` → `Reset()`)* · **il fusibile** · **`Penitent` ricostruito o distrutto**. **E l'ordine
della chiusura conta**: prima si rimette il Penitent e **si riaccende il renderer**, **poi** si distrugge la copia — al contrario,
per un fotogramma, **il cadavere non esisterebbe E il Penitent sarebbe ancora invisibile** *(un lampo di vuoto)*
`(REFERTO-CAM-SEGUE-VIVO.md:261-288)`.

**✅ E SCRIVI COSA E' STATO CONFERMATO DALL'UTENTE: *"tutto giusto funzionante"*** ✅ — **verificato in gioco.**

#### 1.4 ⚠️ COSA RESTA DICHIARATO NON VERIFICATO, sulla 9 *(perche' "chiusa" non vuol dire "senza riserve")*

1. 🔴 **Se spostare il `Collider2D` del Penitent faccia scattare un trigger di scena**: la pausa su `CINEMATIC` copre **la
   cutscene**, ma **non e' una prova** che nessun altro trigger della bossroom reagisca al corpo del morto. La mitigazione
   strutturale c'e' *(salti radi con soglia a 6 unita', non uno strisciamento continuo)*, ma **e' una mitigazione, non una
   dimostrazione** `(REFERTO-CAM-SEGUE-VIVO.md:375-380)`. ⇒ **⚠️ QUESTA RISERVA E' DIVENTATA IL §3 DI QUESTA SEZIONE.**
2. **Se il clone si veda ESATTAMENTE come il cadavere originale** *(ordine di rendering, e il `BlobShadow` **non** e' clonato:
   sotto la copia non c'e' ombra)* `(REFERTO-CAM-SEGUE-VIVO.md:370-374)`.
3. **Se `ProCamera2D` "voglia" un bersaglio MOBILE anche da morto**: il legame e' provato e `LogicStates.PlayerDead` non e' un gate
   dell'inseguimento *(`CameraManager.cs:130` governa **solo** il camera pan)*, ma **52 campioni FERMI non provano che il follower
   segua un bersaglio in movimento** — `ProCamera2D` e' un assembly di terze parti e non e' in `_il`
   `(REFERTO-CAM-SEGUE-VIVO.md:381-385)`.
4. **Il caso "entrambi morti"**: trattato e dichiarato, **mai esercitato in bossfight** `(REFERTO-CAM-SEGUE-VIVO.md:391-392)`.
5. **La gerarchia reale di `NormalGroup`** *(quanti figli, se il fondale nero sia fra loro)*: **non e' nell'IL e non e' estraibile
   da questa macchina** *(payload compresso: `UnityFS … 2017.4.40f1`)* ⇒ la ricetta e' gerarchia-agnostica, e sono i marker
   `figli=`/`azzerati=` a dirlo `(REFERTO-DEATH-SCREEN-TRASPARENTE.md:296-301)`.

---

### 2. ⭐ I DUE RITOCCHI *(e il loro esito reale)* — commit `68f3b0e` (FIX 1) e `18a4500` (FIX 2)

#### 2.1 ⚠️ IL MORTO "IN PIEDI" — la causa NON era la visibilita', era **la POSA della copia** *(`68f3b0e`)*

**Il sintomo, parola dell'utente**: *"quando G1 e' morto ha fatto l'animazione di morte e poi e' rimasto IN PIEDI appena e' arrivata
la cam"* `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:52)`.

**La catena, e perche' non era la visibilita':**

| # | Fatto | Prova |
|---|---|---|
| 1 | **La copia nasce al PRIMO frame di morte**, quando l'animazione non e' finita | il log G1: `copia congelata`/`cadavere-copia creato` vengono **prima** di `[Morte] annuncio inviato` e `Game state has been set to PLAYERDEAD`. In IL: `Entity.KillInstanteneously` *(`Entity.cs:319`)* alza `Status.Dead` **subito** `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:59)` |
| 2 | **Il cadavere VERO e' congelato nella posa FINALE dal GIOCO**, non da noi | `PlayerDeathAnimationBehaviour.OnStateUpdate` *(`:33-36`)*: `normalizedTime >= 0.95 && Status.Dead` → **`animator.enabled = false`**. **E non e' l'unico**: `GroundingOverBehaviour.OnStateUpdate` *(`:47-54`)* fa la stessa cosa con la stessa guardia *(`:40-55`)* ⇒ **due** behaviour indipendenti, stesso esito `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:60, 100)` |
| 3 | ⇒ **Quello che l'utente vede e' la COPIA**, perche' il corpo vero e' invisibile dal frame 0 *(`body.color.a = 0`, `alphaPrima=1.00`)* — e la prova indiretta che chiude il cerchio e' che **l'utente ha visto l'animazione di morte**: l'unica cosa disegnata era la copia `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:61)` |
| 4 | **La copia riceveva solo `Sprite` + `color`, e solo per 2,5 s FISSI** *(`CORPSE_POSE_SECONDS`, `if (now < _poseUntil) RefreshCorpseSprite();`)* — **e nemmeno la POSA veniva copiata** | il codice precedente: **un timer CIECO**, che non c'entra niente con la durata dell'animazione `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:62)` |
| 5 | ⇒ se l'animazione dura **piu' di 2,5 s**, la copia si ferma **a meta' caduta** = **in piedi**. **E la durata NON e' leggibile da nessun log** *(nessun timestamp per riga, nessun marker sull'animazione, nessun clip nell'IL)* ⇒ la diagnosi **non poteva** essere *"alza il numero"* `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:63)` |

**Il fix, e perche' questa forma**: **non un numero piu' grande, ma un altro segnale** — si copia **`sprite + colore(RGB) + POSA`
*(`localPosition`/`localRotation`/`localScale`)* finche' la sorgente cambia, e ci si ferma quando IL GIOCO la ferma:

```
finche' !_poseClosed:  copia sprite + colore(RGB) + localPosition/localRotation/localScale
                       riafferma l'alpha del corpo vero
                       se penitent.Animator.enabled == false  ->  CHIUDI (e' il gioco che ha fermato la posa)
                       se sono passati 8 s                    ->  CHIUDI comunque (TETTO, con Warn)
```
- **Il segnale e' del gioco, non nostro**: `Penitent.Animator.enabled == false` e' **la riga di `PlayerDeathAnimationBehaviour`**
  *(`:33-36`)* per cui il cadavere vero non si muove piu'. `Entity.Animator` e' `public` *(`Entity.cs:67`)* ed e' quello del `Body`
  *(`Entity.Awake:156`)* — **lo stesso** che quei behaviour spengono `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:85, 98)`.
- **La copia e' esatta PER COSTRUZIONE**: nell'istante in cui la sorgente si ferma, la copia ha **l'ultimo valore copiato**
  `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:86)`.
- **`CORPSE_POSE_MAX_SECONDS = 8 s` e' un TETTO, non una durata**: se il segnale non arriva mai *(morte in aria, `Animator` gia'
  fermo)* si smette lo stesso con un `Warn` — **si perde qualita' visiva, non la funzione**. **E il tetto e' asimmetrico e voluto**:
  aspettare troppo e' innocuo, **fermarsi presto ricrea il difetto** `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:87)`.
- **`KeepLocalCorpseHidden`: l'alpha si RIAFFERMA a ogni frame**, perche' l'alpha **non e' un nostro campo privato** *(l'animazione,
  un effetto di danno o il gioco possono riscriverlo)*: il contatore **`riasserzioniAlpha=`** dice se e' servito `(…:89)`.
- **Si nasconde con l'ALPHA, non con `SpriteRenderer.enabled`** — e la risposta al *"chi lo riaccende"* e' che **`enabled` e' il flag
  che il GIOCO usa per nascondere il Penitent**: `Interactable.ShowPlayer` *(`Tools.Level\Interactable.cs:492-496`)* e' chiamato da
  `Execution`, `PrieDieu`, `Altar`, `DemakeAltar`, `Lever`, e `SpawnManager.CreatePlayer` *(`:497`)* lo spegne **sul Penitent
  nuovo**: usare il flag del gioco per un nostro stato, e doverlo restituire, **e' un rischio che l'alpha non ha** `(…:90)`.

**I marker**, e la durata che finalmente si misura: `cadavere locale nascosto (renderer=… alpha 1.00 -> 0.00)` ·
**`posa del cadavere chiusa dopo X.XXs e N frame copiati`** *(⭐ **`X.XX` e' la MISURA che oggi manca**: la durata dell'animazione
di morte)* · `posa del cadavere chiusa dal TETTO dopo 8.00s` *(**Warn**, e **non e' un guasto**: e' il limite dichiarato)* ·
`cadavere locale mostrato (… alpha 0.00 -> 1.00)` · e la riga di chiusura con `posa=chiusa|ancora aperta(<perche'>) framePosa=N
riasserzioniAlpha=K` `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:111-136)`.

**⚠️ E VA REGISTRATO CHE RESTA UN DIFETTO — QUESTO PEZZO NON E' RISOLTO:** **l'utente dice *"dal REMOTO il peer morto risulta
ancora in piedi"*.** *(⚠️ Onesta' sulla prova: **questo esito non e' scritto in nessun referto di `scratch\`** — il
`REFERTO-MORTE-IN-PIEDI-E-BOSS.md` §4 punto 7 dichiara che **di quella consegna non esiste un log** e che **niente e' stato provato
in gioco**; l'esito qui registrato viene **dalla parola dell'utente**, come per la voce 63 il 2026-10-13. Va segnato cosi': **non
verificato da un referto, riferito dall'utente** — ed e' il punto 2 della lista in fondo.)* ⇒ **il fix locale c'e' *(la copia su
QUESTA macchina)*, il REMOTO no** *(l'avatar del morto che l'altro vede)*.

#### 2.2 ✅ LA MORTE SCRIPTATA DEL BOSS — `Kill()` **non la esegue**, e ora la si invoca noi *(`18a4500`)*

**La causa, con la catena completa**: la morte di un boss sono **due strade diverse** —
- **la strada CONTABILE**: `Kill()` = `Damage(Stats.Life.Current + Stats.Defense.Final, string.Empty)` *(`Entity.cs:312-315`)* →
  `Damage(float,string)` *(`:270-295`)* → **`KillInstanteneously()`** *(`:317-335`)*: `Status.Dead = true`, `Entity.Death` /
  `OnDeath` / `OnEntityDeath`, `ENTITY_DEAD` → e `BossFightManager.OnDeathBoss` *(`BossFightManager.cs:64, :179-189`)*:
  `Metrics.EndBossFight()` + `IsFightStarted=false` + `onFinishBossFight.Invoke()`;
- **la strada SCRIPTATA**: `Behaviour.Death()` → `StopBehaviour` + `AnimatorInyector.Death()` → **`SetTrigger("DEATH")` = l'animazione
  di morte** — e sta **SOLO in `<Boss>.Damage(Hit)`**, **l'overload del COLPO**, che `Kill()` **non percorre** *(`ElderBrother.cs:85-94`)*.

⇒ **`Kill()` e' la meta' "contabile"; manca la meta' "scriptata"** — ed e' esattamente il sintomo dell'utente: **il boss resta in
piedi**. *(Era il rischio che M1 aveva dichiarato: `REFERTO-M1-MORTE-BOSS.md` §7.3 — **qui e' chiuso**.)*
`(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:156-193)`

**Il fix**: **`Kill()` E POI `Death()` — non l'uno al posto dell'altro** *(si perderebbe la morte, e quindi `ENTITY_DEAD`,
`OnDeathBoss` e **la cutscene nativa** che M1 ha appena portato sul morto)*. **Ed e' l'ordine del gioco**: `Damage(Hit)` fa
`DamageArea.TakeDamage(hit)` e **poi**, `if (Status.Dead)`, `Behaviour.Death()` `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:195-208)`.
- ⭐ **E' UN FIX UNICO, non sedici.** `Death()` **non esiste su una base comune** *(`EnemyBehaviour` dichiara `abstract`
  `Idle`/`Wander`/`Chase`/`Attack`/`Damage`/`StopMovement` e **non ha `Death()`**)* ⇒ si cerca **per tipo**, con
  `GetComponents<EnemyBehaviour>()` *(SOLO il GameObject del boss)* e `GetMethod("Death", Public|Instance, null, `**`Type.EmptyTypes`**`, null)`,
  **con la cache per tipo** *(valore `null` compreso: "cercato e assente" ≠ "mai cercato", e **non va azzerata con la memoria di
  scena**, perche' le chiavi sono **tipi dell'assembly**)* `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:210-235)`.
- ⚠️ **`GetComponentsInChildren` sarebbe stato un guaio vero**: i behaviour di **altre entita'** stanno nella stessa gerarchia —
  `PontiffGiantBehaviour.Death()` chiama `sword.PontiffSword.Kill()`, e la spada ha il **suo** `PontiffSwordBehaviour.Death():314`
  ⇒ si sarebbe rischiato di chiamare **la morte della spada** al posto di quella del gigante.
- **E' AUTO-LIMITANTE**: dove `Death()` non c'e' **non si inventa niente** e il marker lo dichiara — **e non e' un caso di scuola**:
  `PietyMonsterBehaviour` fa la stessa cosa in `PietyMonsterOnEntityDie()`, sottoscritto a `_pietyMonster.OnDeath` *(`:57`,
  corpo `:207-212`)*, cioe' su una strada che **`Kill()` percorre**; idem `BejeweledSaintBehaviour` *(`Head.OnDeath += BossOnDeath`)*
  ⇒ **la chiamata in piu' arriva solo dove la catena e' rotta**.
- **Senza doppioni**: `ApplyBossDeath` ha **gia'** le guardie di M1 *(`Status.Dead` → `motivo=boss-gia-morto`; `_bossKillsApplied`
  → `motivo=gia-applicata`)* ⇒ `Death()` **non viene mai chiamato due volte** — **e non e' pignoleria**: `AmanecidasBehaviour.Death()`
  fa `PlayMakerFSM.BroadcastEvent("BOSS DEAD")` e `TresAngustiasMasterBehaviour.Death()` fa `ClearAll()` +
  `StartAttackAction()` + `StartDividedDeathSequence()` ⇒ **due invocazioni sarebbero due eventi Playmaker e due sequenze**
  `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:250-260)`.

**I marker**: `morteScriptata=` **dentro la riga che esisteva gia'** *(un rifiuto e un'applicazione restano distinguibili come prima,
con una informazione in piu')* · `morte scriptata: <Tipo>.Death() trovata (public, zero argomenti)` · `morte scriptata INVOCATA: …`
· e `morte scriptata NON invocata (<motivo>)` *(**Warn**)*. **L'esito vero non e' un marker nostro: e' l'animazione di morte del boss
a schermo** `(REFERTO-MORTE-IN-PIEDI-E-BOSS.md:262-281)`.

**✅ CONFERMATO DALL'UTENTE: "l'animazione della morte del boss funziona"** ✅ *(verificato in gioco **dopo** la stesura del referto,
che al §4 punto 7 dichiara di non avere log di quella consegna — e il **5.1** di `REFERTO-M1-MORTE-BOSS.md`, che diceva "in attesa di
verifica", **e' superato da questa conferma**)*.

---

### 3. 🚨🚨 IL GUASTO APERTO — *e questo e' il punto piu' importante della sezione*

**Il GUASTO, con la parola dell'utente:** *"il peer morto NON RIESCE PIU' A RESPWNARE: i tasti non funzionano, ne' per tornare al
falo' ne' per il ritorno al menu."*

**⚠️ STATO: GUASTO APERTO, IN CORSO DI RIPARAZIONE — NON E' RISOLTO.** **Al momento di scrivere questa sezione un ALTRO AGENTE lo
sta riparando.** **HEAD era `18a4500` e non era avanzato**: quindi **la riparazione, se e' arrivata, e' arrivata dopo**. ⇒ *questa
sezione lo registra **com'era**: aperto, con il sospetto principale e con la lezione. **Chi legge un TODO aggiornato dopo il
`18a4500` deve cercare in fondo un'altra sezione, non questa.***

**⚠️ IL SOSPETTO PRINCIPALE — ed era un rischio DICHIARATO, non una sorpresa.** L'utente ha perso **i tasti**: ne' il tasto per
tornare al falo', ne' quello per il ritorno al menu. Il sospetto e' **`DeathCorpseFollow`**, cioe' l'EXTRA del §1.3 — **quello che
muove il `Penitent` (e con lui il suo `Collider2D`)**:

```
DeathCorpseFollow.MoveTo() → Penitent.Teleport(target)      (DeathCorpseFollow.cs:759-785; Penitent.cs:326-336)
   ⇒ si sposta il TRANSFORM, e con esso il Collider2D, in un mondo VIVO
   ⇒ puo' far scattare un TRIGGER di scena (entrate, CameraBoundariesTrigger, aree di boss)
   ⇒ blocker CINEMATIC acceso (il gioco lo accende lui: Cinematics.cs:134)
   ⇒ NESSUN TASTO RISPONDE
```
**E la cosa che rende il sospetto forte e' che l'autore l'aveva DICHIARATO, con le stesse parole**:
*"**Se spostare il `Collider2D` del Penitent faccia scattare un trigger di scena.** La pausa su `CINEMATIC` copre la **cutscene**,
ma **non e' una prova** che nessun altro trigger della bossroom (entrate, `CameraBoundariesTrigger`, aree di boss) reagisca al corpo
del morto. La mitigazione strutturale c'e' (**salti radi, con soglia a 6 unita'**, invece di uno strisciamento continuo), ma **e' una
mitigazione, non una dimostrazione**. ⇒ **il primo test da fare**, ed e' quello che ho messo come principale."*
*(`REFERTO-CAM-SEGUE-VIVO.md:375-380`, §10 punto 2)* — **e il primo test ha dato l'esito peggiore.**

**E il §3.4 del referto spiega perche' il gate su `CINEMATIC` NON poteva bastare da solo**: il blocker si **legge** *(`:579-599`)* e
si mette in pausa l'inseguimento, ma **il trigger e' cio' che ACCENDE il blocker**: se il trigger scatta, la pausa arriva **dopo**
che il danno e' fatto. **Il gate e' una reazione, non una prevenzione.**

**⚠️ NON chiuderlo: al momento di scrivere NON e' risolto.** ✅ *(La ragione per cui va lasciato aperto **anche se** il fix arrivasse
subito: **un guasto riferito dall'utente e riparato senza una run di verifica non e' chiuso — e' "implementato, non verificato",
che e' la distinzione che l'audit ha insegnato a questo progetto.)***

#### 3.1 ⭐⭐ LA LEZIONE — e la REGOLA che ne esce *(il perche' questa sezione e' la piu' importante)*

> **Un ritocco COSMETICO ha introdotto un guasto che BLOCCA IL GIOCO.**

Il FIX 1 *(§2.1)* e' nato per correggere **una posa** — un difetto **visivo**. La modifica che lo ha reso possibile *(FIX 1 tocca il
modo in cui la copia insegue la posa; ma **l'EXTRA del §1.3 — l'inseguimento del vivo — e' quello che sposta il corpo**)* agisce su
**una cosa che il gioco usa davvero**: il `Collider2D` del giocatore in una **stanza viva**, con i suoi trigger. **Nessuno dei due
referti lo poteva escludere** — ed entrambi lo dicevano.

> ⭐ **LA REGOLA CHE NE ESCE — e vale da adesso per tutto il progetto:**
> **UNA MODIFICA CHE SPOSTA UN `Collider2D` IN UN MONDO VIVO VA TRATTATA COME UNA MODIFICA AL GIOCO, NON COME UNA MODIFICA
> COSMETICA.**
> Cioe': **stesso peso di un fix di gameplay** — *prova in IL prima, test in gioco dopo, e nessuna dichiarazione di "fatto" senza
> una run che lo confermi.* Perche' **il confine non e' "quanto codice ho scritto" ma "cosa tocca il mondo"**: spostare uno sprite
> e' cosmesi; **spostare un `Collider2D` in una stanza con i trigger accesi e' gameplay**, anche se la ragione per cui lo fai e'
> **far vedere meglio** una bossfight a chi e' morto.

**E la conseguenza pratica, in una riga**: la prossima volta che un ritocco *"solo visivo"* tocca un `Collider2D`, un `Rigidbody2D`,
un `Animator.enabled`, un `SetActive` o una posizione che il gioco legge — **le tre domande da farsi prima** sono:
**(1) chi altro legge questo oggetto? (2) cosa e' acceso in questa stanza? (3) come si torna indietro, e cosa lo dice nel log?**

---

### 4. LE COSE PICCOLE *(seguite in questi giorni, e il loro stato reale)*

**(a) ⛔ Lo SPAM A MONTE DEL `"*"` — NON implementato, e la ragione resta quella.** Il fix `ff5dec7` toglie **la ripetizione**, non
**la domanda**: `ReceiveProgress` **continua a interrogare il registro anche per il sentinella `"*"`** *(il server lo usa per il sync
iniziale di squadra: `Server.cs`, `getProgressPacket("*", …)` — e **nessuno si chiama `"*"`**, quindi e' **una domanda senza risposta
possibile**)*. La correzione sarebbe in **un punto solo** *(`if (playerName != "*") { … }`)* ed e' **equivalente**, risparmiando
**309 scansioni per sessione** *(`REFERTO-SPAM-LOG.md:127-133`)*. **⛔ NON e' stato implementato, ed e' dichiarato: e' LOGICA, non
logging** — e la consegna di quei due commit toccava **solo il quando si logga**. **Resta un lavoro reale e non e' una voce numerata.**

**(b) ✅ IL MARKER FALSO — trovato nei log e CORRETTO *(`REFERTO-MORTE-IN-PIEDI-E-BOSS.md:299-310`)*.** Nella riga di chiusura
della sessione precedente *(G1:3654)*:
```
[CorpseFollow] finestra chiusa: il giocatore e' tornato (respawn) · … invioPosizioneCongelato=no · la copia e' distrutta
```
**`invioPosizioneCongelato` veniva letto DOPO essere azzerato** *(i campi si azzerano prima della `ModLog.Info`)* ⇒ **stampava `no`
SEMPRE.** ⇒ **quel `no` NON e' la prova che la guardia di `MainPlayerManager` non e' scattata**: era **un marker che non poteva dire
altro**. Ora il valore si legge **prima** di azzerare *(e la stessa trappola e' evitata per `posa=`)*. **Conseguenza per la prossima
sessione**: e' **la riga da guardare** per sapere se l'invio e' stato davvero congelato — **atteso: `si`**.
> ⭐ **E va tenuta la massima che ne esce, perche' e' la seconda volta in questo progetto: UN MARKER CHE MENTE E' PEGGIO DI UN MARKER
> ASSENTE.** Un marker assente ti fa andare a cercare; **un marker falso ti fa chiudere una diagnosi sbagliata.**

**(c) E DUE COSE MINORI, dichiarate e non toccate** *(stessa sezione del referto)*: **`[CorpseFollow ERR] in TryRealignNetwork … :`**
con **messaggio VUOTO** *(senza nemmeno il tipo dell'eccezione)* ⇒ ora stampa `e.GetType().Name`; **diagnosi, e resta un sospetto**:
`SendAllLocationData` legge `Penitent.SpriteRenderer` (`CurrentDirection`) e in quella riga il renderer **era nullo** ⇒ quasi
certamente una `NullReferenceException`, **effetto pratico nessuno** *(il peer teneva l'avatar dov'e' caduto, che e' il
comportamento voluto)*. E la soglia **`FOLLOW_EPSILON = 6`** e' risultata **proprio sull'ampiezza dell'oscillazione** del compagno in
quella stanza: **40 spostamenti in 161 s**, quasi tutti con `era a 6.0x unita'` — **non e' un guasto**, ma **se quei salti si
vedessero a schermo, il numero da guardare e' quello**.

---

### 5. 📌 LA LISTA CORTA DELLE COSE DA FARE *(in ordine di importanza — verificata contro il file)*

**1. 🚨 IL RESPAWN BLOCCATO** *(§3)* — **aperto, IN CORSO DI RIPARAZIONE da un altro agente**. **E' la priorita' assoluta**: e' un
**guasto che blocca il gioco** *(i tasti non rispondono: ne' falò ne' menu)*, non un difetto visivo. Il sospetto e' il
`Collider2D` mosso da `DeathCorpseFollow` che fa scattare un trigger di scena ⇒ blocker `CINEMATIC` acceso. **E non si chiude senza
una run di verifica**: un guasto riparato e non provato e' **"implementato, non verificato"**.

**2. ⚠️ IL MORTO "IN PIEDI" VISTO DAL REMOTO** *(§2.1)* — **il fix locale c'e' *(`68f3b0e`, confermato dall'utente su QUESTA
macchina)*, il remoto no**: l'utente dice che **sull'altra macchina il peer morto risulta ancora in piedi**. Non e' la stessa cosa
della posa locale: e' **l'avatar del morto come lo vede l'altro**, e il fix non lo copre.

**3. LA COLPA** — la **grief sulla vita morendo non seduti al falò** — **voce 63**. *Formulazione dell'utente:* **`Stats.Life`, non
`Fervour`** · **morendo, non al falò**. Design completo, `GuiltManager` presente, arte esistente *(`guiltStart`/`guiltBar`/`guiltEnd`)*,
**implementazione non iniziata**. *(Il messaggio d'ingresso alla bossfight: **una chiave di localizzazione + una chiamata a
`DisplayNotification`**.)*

**4. I TEST DEI BOSS** — **voce 18**. **Resta la voce piu' esposta**: nei referti e' esercitato **un solo boss** *(`ElderBrother`,
`D17Z01S11`)*, e la barra del boss *"coincide per coincidenza, non per sincronizzazione"*. ⚠️ **E ora ha una parte meno urgente di
prima**: il **test di M1** *(la morte del boss sulla macchina del morto)* **e' stato fatto e confermato dall'utente** *(§2.2)*, e il
**rischio dichiarato di M1 — il boss che resta in piedi — e' stato chiuso da `18a4500`**, ma il fix **non e' mai stato eseguito su
nessuno degli altri 16 boss** *(firma verificata in IL per tutte, esecuzione no: `REFERTO-MORTE-IN-PIEDI-E-BOSS.md:349-350`)*.

**5. LE COSE PICCOLE + LE DOMANDE §9** *(§4)* — **(a)** lo **spam a monte del `"*"`** *(il taglio per il sentinella: **logica, non
logging**, 309 scansioni per sessione)* · **(b)** i **due residui di `BUG-NOTI.md`** *(la seconda `Object.Destroy` in
`SettleStuckDeaths`, e l'esecuzione "incrociata")* · **(c)** il **`fullPathHash`** *(**parcheggiato di proposito**: non alzarlo di
priorita' senza un sintomo nuovo)* · **e le domande del §9**: **domanda 15** *(il fantasma e' congelato dalla mod? — test mai fatto)*
e **domanda 14** *(`EnemyAI` gira davvero? — si decide **sui prefab**)*. **Sono le sole due rimaste.**

**✅ E LE VOCI CHIUSE DA REGISTRARE — e chi le cerca le deve trovare chiuse:**
- **voce 58** — **CHIUSA per DECISIONE** *(non per fix: la mod base non si tocca)* · `[2026-10-13 10:00]` §1.
- ⭐ **voce 9** — **CHIUSA** *(con la **riserva del punto 2**: il fix locale c'e', **il remoto no**)* · **questa sezione, §1**.
- **voce 56** — **CHIUSA dall'audit del 2026-10-12** *(i 5 marker, tutti implementati)* · `[2026-10-13 10:00]` §5(c).

**⚠️ E UNA COSA VA DETTA, perche' la lista non la contiene e non e' una voce:** **il guasto del §3 e' l'unica cosa di questa sezione
che puo' rendere il gioco INGIOCABILE.** Tutto il resto e' visivo, di design o di test. ⇒ **finche' non e' chiuso con una run che lo
confermi, nessun'altra voce di questa lista va promossa sopra di lui.**


---

## [2026-10-16 20:00] IL PEER MORTO **CHIUSO** *(si suona la morte, non si nasconde)* — E IL RESPAWN: **la rete di riserva NON e' difettosa**

Referto: `scratch/REFERTO-PEER-MORTO-E-CAUSA.md`. Commit **`c5c7299`**. Build `succeeded`, 0 errori, **263 -> 263**,
deploy **263 -> 263**, DLL gioco == repo *(SHA256 `C43097FC…`, marker riletti nella DLL byte-per-byte)*.

### 1. ✅ LA VOCE 2 (il morto "in piedi" visto dal remoto) — **CHIUSA** *(implementata, non ancora verificata in gioco)*

**La causa non era la posa, era il CANALE.** Da morti `MainPlayerManager.Update` esce prima di spedire
(`PositionSendPaused`) e **non manda piu' nulla, nemmeno l'animazione di morte** ⇒ il peer restava sull'ultima posa
ricevuta, che per definizione e' quella **di prima** della morte: **in piedi**. E il congelamento e' voluto *(serve a non
mandare la posizione del cadavere-copia addosso al vivo)*.

⭐ **E c'era un secondo ostacolo, che e' la parte delicata:** `OtherPlayerScript.CurrentAnimation` usa **l'arrivo di
un'animazione come PROVA DI VITA** (`if (playerDead) SetDeathStatus(false)`) ⇒ la stessa prova scartava **l'unica
animazione che serviva**, quella di morte. Spedire solo il byte avrebbe **rialzato l'avatar nello stesso frame**: la mod
si sarebbe disfatta da sola.

**Il fix, in due meta':** si manda **la morte UNA VOLTA** prima del congelamento (`MainPlayerManager.CorpsePeerTick`), e
**la morte e' esclusa dalla prova di vita** (`OtherPlayerScript`), **e solo la morte**.

**⛔ E NON si nasconde l'avatar**, che era la prima strada *(e la richiesta iniziale dell'utente)*: l'alternativa costa
**la stessa classe di lavoro ma aggiunge uno stato che puo' restare bloccato** *(avatar invisibile per sempre — il
difetto peggiore dei due)* **e mostra meno**. Lo sprite di morte **esiste gia'**: l'Animator dell'avatar ha la posa, e il
gioco spegne l'Animator del Penitent **sulla posa finale** — `PlayerDeathAnimationBehaviour.cs:33-36`,
`normalizedTime >= 0.95`. *(Indici 55/56/57 = `Death`/`Death Spike`/`Death Fall`, da `Data/AnimationStates.cs:64-66`.
⚠️ `"Grounding Over"` sta nella lista dell'**avatar** ma **non** qui: e' anche il rialzarsi da una caduta.)*

**Marker:** `[CorpsePeer] animazione di morte mandata: …` · `[CorpsePeer] avatar del peer: morto a terra (…)` ·
`[CorpsePeer] avatar del peer: vivo in piedi (…)`. ⇒ *「non e' partita」, 「e' partita e non si vede」 e 「e' partita e si e'
rialzato」 ora hanno tre righe diverse.*

**Ripristino: nessun protocollo nuovo.** Al respawn `DeathCorpseFollow.Close()` chiama `SendAllLocationData()` ⇒
animazione **0 = Idle**, che **non e' una morte**, quindi la prova di vita torna attiva e l'avatar si rialza.

### 2. 🚨 IL RESPAWN — ⛔ **la causa NON e' chiusa, MA LA RETE DI RISERVA NON E' DIFETTOSA** *(e questo cambia il §5 qui sotto)*

**La premessa va corretta, ed e' una misura, non un'opinione.** I due log di questa sessione **non sono quelli del
guasto**: G1 ha **3153** righe, **nessuna cutscene dopo la morte** *(il boss resta a `vita=370`, `G1:2495`)*, **tutti** i
fade completati *(`Called fade function` 7 `True`/7 `False` in G1, 6/6 in G2)*, e `DEAD_SCREEN` acceso a `2018` e
**spento** a `2565`.

**I tre candidati:** ① la gerarchia `GameplayUI` — **NON PROVATO**, e **indebolito**: `gameplayUIActive=True` e
`schermataMorte=[attivo=True enabled=True]` per **tutti e 45** i campioni, zero `Cam cinematic mode` dopo la morte; e
`OnDeadAction` gira su **`Core`**, non sul widget (`DeadScreenWidget.cs:145`) ⇒ **lo spegnimento del widget non ferma la
sua coroutine**. ② `StopAllCoroutines` — **SMENTITO** *(zero occorrenze)*. ③ DOTween fermo — **SMENTITO**
*(`alphaNorm` 0.00 → 0.71 → 1.00 **durante** la finestra)*.

⭐ **Il fusibile e' scattato perche' nessuno ha premuto un tasto:** `unityAnyKey=False` in **tutti e 45** i campioni;
l'unico `True` di G1 e' il campione del **frame della morte**. **E G2 lo prova al contrario:** tasto `Space` ⇒
`RESPAWN CHIESTO: tasto premuto dal morto (Space)` **immediato**, `fusibile=non usato`.
⇒ **RETE 1 non e' rotta: e' una rete d'INGRESSO, e in G1 non e' entrato niente.** *("Il fusibile e' scattato ⇒ RETE 1 non
ha funzionato" NON regge.)*

**⇒ La configurazione che ruppe la sessione precedente — morte + cutscene in modalita' cinema, `FADE` rimasto appeso —
NON e' stata riprodotta.** Non ho toccato `GameplayUI`, le cutscene, il fade o i blocker. **E la riga che chiude il
candidato ① e' UNA: `gameplayUIActive`.** Serve una run che la mostri `False` mentre si e' morti.

### 3. 📌 LISTA CORTA — **aggiornata**

- **1. ⚠️ IL RESPAWN** *(§2)* — **aperto, e la priorita' resta assoluta** — *ma il watchdog non e' il sospetto*: ha
  respawnato G1 correttamente, e **in G2 non e' nemmeno intervenuto** *(non ruba il respawn al gioco)*. **Non si chiude
  senza una run** che riproduca **morte + cutscene in modalita' cinema**: la riga da leggere e' **`gameplayUIActive`**.
- **2. ✅ IL MORTO "IN PIEDI" VISTO DAL REMOTO** — **CHIUSA** *(§1)*: **implementata, non ancora verificata in gioco**.
  ⚠️ **Se il cadavere non si vedesse, la riga da cercare e' `avatar del peer: morto a terra`**: se **c'e'**, la morte e'
  arrivata *(problema di posa)*; se **non c'e'**, non e' arrivata *(problema di canale)*.
- **3. LA COLPA** *(voce 63)* · **4. I TEST DEI BOSS** *(voce 18)* · **5. LE COSE PICCOLE + LE DOMANDE §9** — invariate.
- ⚠️ **Il TODO non e' stato riscritto in tutte le sue parti**: questa voce **si sovrappone** al §3 e al §5 della voce
  `[2026-10-14 02:00]`, che **parlano ancora del guasto del respawn come se il fusibile fosse la prova di un guasto** —
  e ora **non lo e'**. **Chi legge quella voce legga anche questa.**


---

## [2026-10-16 22:00] ⭐⭐ **IL TASTO ARRIVA A `UnityEngine.Input` ANCHE CON L'INPUT DEL GIOCO BLOCCATO** — il RESPAWN raccontato per intero *(la causa, le due proposte scartate, il fix del cambio stanza)*, e **LA DECISIONE CHE ASPETTA IL TEST**

**Referti**: `scratch/REFERTO-RITORNO-IN-VITA-E-NUMERI.md` *(il principale: HEAD di partenza verificato `59f34d7`, arrivo `d0facd4`)* · `scratch/REFERTO-FUSIBILE-3.md` · `scratch/REFERTO-FUSIBILE-2.md` · `scratch/REFERTO-FUSIBILE-RESPAWN.md` · `scratch/REFERTO-RESPAWN-BLOCCATO.md` · `scratch/REFERTO-PEER-MORTO-E-CAUSA.md` · `scratch/REFERTO-CAM-SEGUE-VIVO.md`.
**Commit**: **`16a49ee`** *(il cadavere-seguace si FERMA sul cambio stanza + lo stato incoerente ha una rete che lo DICHIARA)* e **`d0facd4`** *(i due numeri del fusibile)* — e la **catena del fusibile**, tutta del `2026-10-16`: `4c15659` *(nasce `RETE 1` + il fusibile)* → `ea9d1ea` → `0393fd2` → `59f34d7`.
**Build/deploy dei due commit**: `Build succeeded`, **0 errori** *(441 warning)*, **263 → 263**, deploy **263 → 263**, 0 tipo assente, 0 METODO assente. **DLL gioco == repo**: SHA256 `8052A8B3A0AFC601266A105473F8EC14A2B7C07C4B594918AA758F4C89294920` per (A) e `DA98723F240A56AC47B0A71619955454C4EF5FAF905B5BA80FA97C7568D8EE77` per (B) — e per (B) i due numeri sono stati **riletti come COSTANTI dentro la DLL deployata** con **Mono.Cecil**, non con un `Contains`.
⚠️ **E prima di leggere il §1: questa sezione NON e' una voce chiusa** — e' lo stato di una diagnosi. **La run che la conferma non e' stata fatta** *(§5)*.

**🔎 COME SI LEGGE QUESTA SEZIONE — tre etichette, e vanno cercate una per una:**
- ✅ **VERIFICATO DALL'UTENTE** *(in gioco, con le sue parole)* · 📊 **PROVATO NEI LOG** *(una riga di log, un conteggio)* · 🔧 **IMPLEMENTATO, NON VERIFICATO** *(build + deploy + referto, nessuna run)*.

---

### 1. ⭐⭐ LA SCOPERTA CHE CHIUDE TRE SESSIONI DI DUBBI *(il pezzo piu' importante)*

#### ⭐ **IL TASTO ARRIVA A `UnityEngine.Input` ANCHE CON L'INPUT DEL GIOCO BLOCCATO** ✅ *(📊 provato nei log)*

- **PROVA NEI LOG**: `unityAnyKey=True` **con `blockers=(DEAD_SCREEN,FADE)` ACCESI** — `G1:2894 · 2898 · 2905 · 2906 · 2916` *(campioni 56, 57, 59, 60, 64)*, ognuno con `schermataMorte=[trovato=1 attivo=True enabled=True alphaNorm=1.00]` *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:251-256`)*.
- **E il meccanismo, verificato in IL**: **`SetBlocker` NON blocca `UnityEngine.Input`** — `DeadScreenWidget` legge `UnityEngine.Input` *(la sua uscita e' `while (!Input.anyKey)`, `:87`)*, **non** `Core.Input.InputBlocked`: **47 riferimenti a `UnityEngine.Input`** contro **20 a `Core.Input.InputBlocked`** nella DLL del gioco ⇒ **due piani diversi** *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:262-265` e `:436-441`)*.
- **E la conferma indipendente, dello stesso ordine**: a `t≈308.5` il morto preme **`Escape`** e la mod scrive `TASTO IGNORATO: Escape e' stato premuto mentre la sequenza di morte del boss era in corso` *(riga **2844**)*, e **nello stesso torno di tempo il `FADE` della schermata si ACCENDE** *(riga **2831**: `FADE has been enabled` + `[TRACE][FADE] Called fade function. Active: True`)* ⇒ **il tasto e' arrivato al GIOCO con i due blocker accesi.**
- ⇒ ⭐ **CONSEGUENZA OPERATIVA**: **RETE 1** — *il watchdog che legge i tasti IN PROPRIO (`DeathRespawnWatchdog` → `UnityEngine.Input.GetKeyDown`, verificato in IL) e chiama `Penitent.Respawn()`* — **funziona in OGNI caso, anche quando il gioco sembra bloccato** ✅
- ⇒ e **il fusibile diventa la rete di CHI NON PREME**, non la rete di un guasto ✅ *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:267-270`; ed e' la ragione per cui il numero del §2 puo' essere corto: `:391-393`)*.

**⚠️ PRECISAZIONE PER CHI CERCA NEI LOG — due sessioni diverse si chiamano tutte e due `G1`, e vanno tenute separate:**
- la **sessione del GUASTO** *(il verbatim dell'utente: "i tasti non funzionano, ne' per tornare al falo' ne' per il ritorno al menu")* e' quella di `REFERTO-RESPAWN-BLOCCATO.md` *(3347 righe)*: `DEAD_SCREEN` acceso a `G1:2510` e **mai spento**, `FADE` acceso a `G1:3159` e **mai spento** *(l'11º `Called fade function. Active: True` senza il suo `False`, `:85-94`)*, `Respawn()` **mai invocato** *(`:75-81`)*;
- la **sessione di `REFERTO-RITORNO-IN-VITA-E-NUMERI.md` §1.1/§2.4** *(dove `unityAnyKey=True` con `blockers=(DEAD_SCREEN,FADE)`)* e' quella del cambio stanza: morte a `t=253.43`, `kill()` del boss a `t=278.9`, cutscene a `t=290.8`, **porta aperta dal peer a `t≈325.6`**, load a `t≈326.4`;
- e una **terza**, quella di `REFERTO-FUSIBILE-2.md` §1.1 / `REFERTO-FUSIBILE-3.md`, in cui e' la **schermata di morte del gioco** a respawnare (`[Morte] il giocatore e' morto` a riga **3196**, ~`t=287.14`).
⇒ ⚠️ **Le tre non si mescolano**: chi legge un numero trovi prima il referto.

---

### 2. ⭐ IL RESPAWN: LA STORIA COMPLETA *(e' stata una delle parti piu' intricate — va raccontata)*

**· LA CAUSA VERA DEL GUASTO "i tasti non funzionano" — e' la schermata di morte DEL GIOCO.** *(📊 provato nei log)*
Il suo callback e' una coroutine: `DeadScreenWidget.OnDeadAction:87-96` = `while (!Input.anyKey) { yield return null; }` → `Core.UI.Fade.Fade(toBlack, 1.5f, 0f, …)` → **nel callback** `Core.Logic.Penitent.Respawn()` *(`REFERTO-FUSIBILE-3.md:38-39` e `:130-134`, dove il conto e' `1.5 s` fra il `FADE` a ~285.6 e il `Respawn()` a ~287.1)*.
⚠️ **E la variabile che decide tutto**: **`inputBlockers` + `InputBlocked`**, che si aggiorna **SOLO dentro `SetBlocker`** *(verificato in IL: `inputBlockers` e' `private List<string>`, `InputBlocked` e' `Count > 0`, il setter e' non-public, `HasBlocker` e' `public` e legge soltanto — `REFERTO-RITORNO-IN-VITA-E-NUMERI.md:288-290` e `:419-423`)*.
E i due blocker **non li spegne nessuno**: `DEAD_SCREEN` acceso da `OnDeadAction:78` (`G1:2510`) e `FADE` acceso da `FadeWidget.Fade` (`G1:3159`), **mai spenti**, e `CINEMATIC` **era spento** *(`G1:3183`: `[Cutscene BLK] CINEMATIC=False InputBlocked=True`)*
⇒ ✅ **con `DEAD_SCREEN`+`FADE` accesi e mai spenti il gioco non reagiva piu' ai tasti** *(`REFERTO-RESPAWN-BLOCCATO.md:66-69, 85-94, 109`)*.
⚠️ **E il prezzo e' grande, e va detto**: `PlatformCharacterInput.get_Blocked` = `Core.Input.InputBlocked` ⇒ **20 letture di `InputBlocked`** nelle classi di gioco *(attacco, parata, preghiera, dash, scale, idle-mode: `Parry.OnUpdate`, `GrabLadder.OnUpdate`, `PlayerIdleMode.OnUpdate`, `PrayerUse.OnUpdate`, `RangeAttack.OnUpdate`, …)* ⇒ **un giocatore a cui manca meta' dell'input** *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:351`)*.

**· E LA CAUSA CHE NON ERA NOSTRA.** *(📊 provato nei log)*
Nella **sessione del guasto** `[Respawn] RESPAWN CHIESTO` = **0 occorrenze** *(la mod non ha chiesto nulla: quella riga esce **sempre** prima che la mod chiami `Penitent.Respawn()`)* e `[Morte] il giocatore e' morto` = **0 occorrenze** *(ed e' il `Prefix` di Harmony di `Penitent.Respawn()`, `PlayerDeathSync.cs:378-400` — **se `Respawn()` girasse, quella riga uscirebbe sempre**)*
⇒ ✅ **il respawn non e' stato rifiutato: non e' mai stato chiesto, e non l'ha chiesto la mod** *(`REFERTO-RESPAWN-BLOCCATO.md:75-81`)*.
📊 **E nella sessione del cambio stanza la stessa assenza vale per entrambe le righe**: in **tutti e due** i log nuovi `[Morte] il giocatore e' morto` = **0** e `RESPAWN CHIESTO` = **0** ⇒ **il giocatore e' tornato vivo lo stesso, e a riportarlo su e' stato il LOAD** *(`LevelManager.InitializeLevel` → `SpawnManager.SpawnPlayerOnLevelLoad():422` → `CreatePlayer` → `Instantiate`, `SpawnManager.cs:489`)* *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:39-45, 139-167`)*.
⚠️ **Una precisazione, perche' e' un numero che si legge male**: nella **terza** sessione *(`REFERTO-FUSIBILE-2.md` §1.1 / `REFERTO-FUSIBILE-3.md`)* `[Morte] il giocatore e' morto` **c'e'** *(riga 3196)* e `RESPAWN CHIESTO` **non c'e'**: li' **ha respawnato la schermata di morte del gioco**, `1.5 s` dopo il suo `FADE` *(`REFERTO-FUSIBILE-2.md:93-100`)*. ⇒ **in tutte e tre le sessioni la mod non ha mai chiesto il respawn**: cambia **chi** l'ha chiesto *(il load, oppure il gioco)*, non **se** e' stata la mod. ✅

**· ⛔ E DUE PROPOSTE VALUTATE E SCARTATE — con la prova, perche' non si rifacciano.** *(📊 in IL + nei log)*
**① bloccare l'input in bossfight (`SetBlocker`): ⛔ NON funziona.** La schermata di morte **legge `UnityEngine.Input`**, quindi **reagisce lo stesso** anche col blocker acceso *(`unityAnyKey=True` con `blockers=(DEAD_SCREEN,FADE)`: §1)*, e il prezzo sarebbe **il controllo del personaggio spento** *(`PlatformCharacterInput.get_Blocked`)* **+ la sparizione del prompt che si voleva proteggere** *(`InputNotifier.DelayerShowNotification` esce subito se `InputBlocked` e `!showWhenInputBlocked`, `:0039-0053`)* ⇒ **toglie il controllo e non ottiene cio' che promette** *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:412-456, 478-496`)*. ✅ E la parte di fattibilita' **era vera**: `SetBlocker`/`HasBlocker` sono `public`, e un nome nostro **non disturba il gioco** *(`inputBlockers` e' una lista di stringhe e il gioco non conosce nessun nome)* — **e' il MECCANISMO che non funziona, non il permesso**.
**② "sfruttare" il ritorno in vita per il cambio stanza: ⛔ e' una TRAPPOLA.** E' un **load di livello dipendente da un trigger che non controlliamo** *(`Door.OnUpdate:267-284` legge `base.PlayerInRange` del **Penitent locale**, che quando si e' morti e' il cadavere)* **e perde la registrazione della morte** *(`[Morte] il giocatore e' morto` assente ⇒ nessuna animazione di respawn, nessuna morte registrata: `:54-57`)* ⇒ **il fix prioritario non e' "ripararlo", e' NON PRODURLO** *(`:356-359`)*.

**· IL FUSIBILE, DOPO TRE ITERAZIONI — la prima versione era un TIMER CIECO.** *(🔧 implementato, non verificato in gioco)*
- **`4c15659` — nasce** *(con `REFERTO-RESPAWN-BLOCCATO.md`)*: **`RETE 1`** *(si legge `UnityEngine.Input` in proprio e al primo tasto vero si chiama `Penitent.Respawn()`, che e' la via del gioco: un **load**, e il load e' l'unico punto in cui il gioco rimette a posto `ResetManager()`, il `FADE` e `DEAD_SCREEN` **da solo**)* **piu' un fusibile a 45 s dal momento della morte**: **un timer cieco**, che non sapeva niente del boss.
- **`ea9d1ea`** *(con `REFERTO-FUSIBILE-RESPAWN.md`)*: il fusibile **aspetta il `kill()` del boss** *(`EnemySyncManager` conta i `kill()` nei due punti che il progetto gia' conosce: `ApplyBossDeath` e il ramo `[BossDeath TX]`)*, il conteggio si **azzera** mentre e' acceso un blocker di scena *(anche `FADE`, `cutscene_block`, …)* e resta un **orologio assoluto che ignora le sequenze**.
- **`0393fd2`** *(con `REFERTO-FUSIBILE-2.md`)*: il fusibile si arma **alla FINE della sequenza di morte del boss** *(eventi pubblici `Cinematics.CinematicStarted`/`CinematicEnded`, nessuna patch Harmony)*, col **ripiego** `BOSS_DEATH_GRACE_SECONDS` per un boss **senza** cutscene.
- **`59f34d7`** *(con `REFERTO-FUSIBILE-3.md`)*: **`RETE 1` diventa MUTA** per tutta la sequenza *(`BossSequenceInProgress` = `_cinematicOn || _waitingSequence`)* **piu' l'avviso a schermo** *"NON PREMERE: il boss sta morendo - la cutscene sta per partire"* (`WARN_PLAYER_ON_BOSS_DEATH`) — perche' **la mod puo' impedire a SE STESSA di respawnare, ma non puo' impedire al GIOCO di farlo** *(`Input.anyKey`)*.
- ⭐ **OGGI, dopo `d0facd4`** *(e i valori sono in cima al file: `DeathRespawnWatchdog.cs:344`, `:354`, `:376`)*:
  **`BOSS_DEATH_GRACE_SECONDS = 30 s`** *(la grazia **PRIMA**: quanto si aspetta, dopo il `kill()`, che parta una cutscene — **7-10× la misura di 3.0-3.9 s**)* **+ `ESCAPE_SECONDS = 15 s`** *(il conteggio **DOPO**, e parte **solo a sequenza conclusa**)* **+ `MAX_ESCAPE_SECONDS = 300 s`** *(l'orologio che ignora le sequenze: la spiaggia di una cutscene appesa)* *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:365-396`)*.
  ⚠️ **E il fusibile ha GIA' causato due volte il difetto che gli si rimprovera** — ed e' la ragione per cui la voce 2 della lista corta parla di **toglierlo**, non di tararlo: **①** `REFERTO-FUSIBILE-RESPAWN.md:30-35` *(scatta a `45.01 s`, `RESPAWN CHIESTO: FUSIBILE scattato` → `Load and activate level D17Z01S05`: **ha respawnato uno spettatore e ha distrutto la cutscene** che stava guardando — il verbatim dell'utente)*; **②** `REFERTO-FUSIBILE-2.md:37-42` e `:89-92` *(armato **per tutta l'animazione di morte** del boss — 5 campioni `fusibile=armato-in-pausa(scena)` — e sarebbe scattato `45 s` dopo il `kill()`)*.

**· E IL FIX DEL CAMBIO STANZA — `DeathCorpseFollow` SI CONGELA su due FATTI DEL GIOCO, non su sensori a raggio.** *(🔧 implementato, non verificato in gioco — commit `16a49ee`)*
`DeathCorpseFollow.SceneChangeFrozen()`, chiamato in `Follow()` **dopo** la pausa sulla cutscene, si ferma su:
- **`Core.LevelManager.InsideChangeLevel`** *(campo `public bool` con setter privato, `LevelManager.cs:69`, vero da `:162` a `:195`)* — il gioco **sta gia'** cambiando livello;
- **`Door.OnDoorEnter` / `Door.OnDoorExit`** *(eventi `Core.SimpleEvent` **pubblici**, `Door.cs:127-129`, invocati in `EnterDoor:212-215` / `ExitDoorSafe:154-157`)* — e servono perche' coprono la finestra in cui `InsideChangeLevel` e' **ancora falso**: **~0.8 s** misurati in `G1` fra la porta (`325.6`) e il load (`~326.4`) *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:194-228`; nel codice: `DeathCorpseFollow.cs:262-266`, `:634`, `:698-745`, `:776-780`, `:796-826`)*.
⛔ **E il sensore a raggio (`Physics2D.OverlapCircleAll`) e' stato scartato DI PROPOSITO**: dipende dal **layer** dei trigger di porta e `Physics2D.DefaultRaycastLayers` **esclude** i layer piu' alti ⇒ **puo' non vedere niente e non dirlo** — *un guardiano che fallisce in silenzio e' peggio di nessun guardiano* *(`:208-212`)*.
✅ **E non e' una patch**: `InsideChangeLevel` si **legge**, i due eventi si **sottoscrivono** *(una volta sola, da `Multiplayer.OnLateUpdate`)*: **zero patch Harmony, 263 → 263**, zero `SetBlocker`/`ResetManager`/`TimeScale`/`SetActive`/camera *(`:205-207` e `:543-557`)*.
📌 **E c'e' un pezzo in piu', che va detto perche' e' nuovo**: da `16a49ee` lo **stato incoerente** — `!Penitent.Status.Dead` **e** `Core.Input.HasBlocker("DEAD_SCREEN")` vero — ha finalmente una **rete che lo DICHIARA** *(una riga `[Respawn] STATO INCOERENTE …` + **una sola** richiesta di `Respawn()` dopo `INCOHERENT_GRACE_SECONDS = 5 s`, `DeathRespawnWatchdog.cs:413`)*, perche' **il watchdog era STRUTTURALMENTE CIECO** *(`Tick`: `if (!dead) return` — la sua finestra esiste solo da morto)* e **nessuna via lecita congeda la schermata** *(`Respawn()` = il load che l'ha creata; `Status.Dead = true` = un'altra attesa; `HideAll()` e' **privata**)* *(`:274-345, 561-605`)*. ⚠️ **Questo tentativo NON e' stato esercitato** *(`:585-588`)*.

---

### 3. ⭐⭐ LA DECISIONE DA PRENDERE DOPO IL TEST *(⚠️ e va scritta come CANDIDATO, non come piano)*

> ⚠️ **SE IL TEST CONFERMA CHE `RETE 1` BASTA** *(e la prova nei log e' gia' forte: §1)*
> ⇒ ⭐ **SI POSSONO TOGLIERE TUTTI I CONTROLLI E I TIMER DEL RESPAWN AUTOMATICO**: il fusibile *(`15 s`)* e l'orologio *(`300 s`)* diventano **RESIDUI** — sono la rete di **CHI NON PREME**, non la rete di un guasto ✅
> ⇒ **meno stato, meno timer, e meno cose che possono scattare a sproposito** ✅ — *(ed e' ESATTAMENTE il difetto che il fusibile ha gia' causato **DUE VOLTE**: §2, ① e ②)*
> ⚠️ **MA E' UNA DECISIONE DOPO IL TEST, NON PRIMA**: e' un **CANDIDATO ALLA RIMOZIONE**, e la **CONDIZIONE e' esplicita** — *il test del §4 punto 1 deve mostrare `RETE 1` che respawna al primo tasto vero, e nessun `[Respawn] STATO INCOERENTE` che compaia per conto suo.* **Finche' quel test non c'e', il fusibile RESTA**: un fusibile inerte non costa niente, un fusibile tolto prima della prova costa una sessione.
> ⚠️ **E VA TENUTO IL CASO CHE `RETE 1` NON COPRE** — e va detto perche' e' l'unico: **un blocco a livello UNITY** *(nessun tasto arriva affatto)*. `RETE 1` legge `UnityEngine.Input` in proprio: se nemmeno quello risponde *— finestra non a fuoco, dispositivo scollegato, motore fermo —* la mod e' cieca come il gioco ⇒ **in quel caso il fusibile e' l'unica uscita automatica che esiste** *(`REFERTO-FUSIBILE-RESPAWN.md:229-231`: "se il tasto non arriva a Unity, la mod non puo'…")*.
> ⇒ **la forma della decisione, in una riga: si tolgono i TIMER, si tiene la `RETE 1`, e il fusibile si toglie solo dopo che il test ha mostrato che un tasto vero arriva sempre.**

---

### 4. 📌 LA LISTA CORTA — *verificata contro il file, e aggiornata*

**1. ⭐ IL TEST** *(§2 — ed e' l'unica cosa che sblocca tutto il resto)*: i due casi, e ognuno ha la sua riga da leggere:
   - **(a)** morte in una **stanza con una porta** + il vivo la apre ⇒ deve comparire **`[CorpseFollow] inseguimento CONGELATO`** *(una per finestra: `_sceneChangeReported`)* — e' **la prova che il fix del cambio stanza di `16a49ee` funziona** *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:531`)*. ⚠️ **In `G1` questo NON e' successo**: a usare la porta e' stato **il peer**, e il `[CorpseFollow]` non registra nessun movimento dopo `t=325.10` *(`:567-572`)*;
   - **(b)** morte **col boss vivo** + il compagno lo uccide ⇒ **la cutscene si vede** *(la riga da leggere e' `la CUTSCENE e' PARTITA … CinematicStarted`, e **non** deve comparire `fusibile=armato(X.XX/15.00s)` prima della fine della scena)* *(`REFERTO-FUSIBILE-2.md:289-296` — ⚠️ **quella tabella scrive `45.00s` perche' e' scritta PRIMA di `d0facd4`: oggi la soglia e' `ESCAPE_SECONDS = 15 s`, `DeathRespawnWatchdog.cs:344`)*.
   - ⚠️ **E la riga da leggere per prima se qualcosa non torna**: `[Respawn] STATO INCOERENTE` **non deve comparire**; `[Morte] il giocatore e' morto` **deve** comparire ogni volta che `Respawn()` gira davvero.

**2. ⚠️ POI: LA VALUTAZIONE SE TOGLIERE IL FUSIBILE** *(§3)* — **condizionata al test**, e va scritta come **candidato alla rimozione** con la condizione esplicita. **Non prima.**

**3. LA COLPA** *(voce 63)* — la grief sulla vita morendo non seduti al falo': **`Stats.Life`, non `Fervour`** · **morendo, non al falo'** · design completo, `GuiltManager` presente, arte esistente *(`guiltStart`/`guiltBar`/`guiltEnd`)*, **implementazione non iniziata**. *(Il messaggio d'ingresso alla bossfight: una chiave di localizzazione + una chiamata a `DisplayNotification`.)* **Invariata.**
   - ⚠️ **E una cosa va detta, perche' un conteggio automatico non la vede**: la voce 63 **NON e' una riga della tabella principale** — vive nel **blocco di provenienza** (`docs\TODO.md:123`, `**PROMOSSA a voce**`) ⇒ *verificato il 2026-10-16*: nella tabella principale *(righe 41-108)* ci sono **59 righe-voce**, **58** con un marcatore di chiusura e **una sola aperta: la 18**; la **63** sta fuori, e **per questo va cercata a parte**.

**4. I TEST DEI BOSS** *(voce 18)* — **resta la voce piu' esposta**: nei referti e' esercitato **un solo boss** *(`ElderBrother`, `D17Z01S11`)* e la barra del boss *"coincide per coincidenza, non per sincronizzazione"*; il fix della morte scriptata **non e' mai stato eseguito su nessuno degli altri 16 boss** *(la firma `Death()` e' verificata in IL per tutte, l'esecuzione no)*. **Invariata.**

**5. LE COSE PICCOLE + LE DOMANDE §9** *(§4 della voce `[2026-10-14 02:00]`)* — **(a)** lo **spam a monte del `"*"`** *(logica, non logging: 309 scansioni per sessione)* · **(b)** i **due residui di `BUG-NOTI.md`** · **(c)** il **`fullPathHash`** *(parcheggiato di proposito)* · **e le domande §9: la 15** *(il fantasma e' congelato dalla mod? — test mai fatto)* **e la 14** *(`EnemyAI` gira davvero? — si decide sui prefab)*. **Invariate.**

**✅ E LE VOCI CHIUSE DA REGISTRARE — chi le cerca le deve trovare chiuse:**
- **voce 58** — **CHIUSA per DECISIONE** *(non per fix: la mod base non si tocca)* · `## [2026-10-13 10:00]`, §1 · in tabella: `docs\TODO.md:100`.
- ⭐ **voce 9** — **CHIUSA il 2026-10-14, in due pezzi e col riscontro dell'utente** *(`b9f8360` la schermata trasparente, `04a8fad` la cam sul vivo)* **con la riserva del punto 2** *(dal remoto il peer morto risultava ancora in piedi)* — e **quella riserva e' stata CHIUSA dalla voce `[2026-10-16 20:00]`, §1** *(`c5c7299`)* · in tabella: `docs\TODO.md:50`.
- **voce 56** — **CHIUSA dall'audit del 2026-10-12** *(i 5 marker, tutti implementati)* · `## [2026-10-13 10:00]`, §5(c) · in tabella: `docs\TODO.md:103`.
- 📐 **E la convenzione dei marcatori va letta com'e', perche' un conteggio automatico sbaglia di 4**: la cella *Stato* di una voce chiusa **compresa nell'archivio** e' esattamente `**CHIUSA — integrale in fondo** *(cerca "Voce N —")*`; le **sei** voci chiuse dall'audit del 2026-10-12 — **e fra queste la 56 e la 58** — usano `✅ CHIUSA — <prova>`, **perche' il loro testo non e' stato compresso** ⇒ **una regola rigida conta 55 chiuse invece di 59** *(`scratch/AUDIT-TODO.md`, §5 "Un'irregolarita' di convenzione, dichiarata e non nascosta")*.

---

### 5. ⚠️ COSA **NON** E' VERIFICATO *(e va detto senza sconti, perche' questa sezione contiene due fix)*

1. 🔧 **NIENTE DI `16a49ee` E `d0facd4` E' STATO PROVATO IN GIOCO.** Il referto lo dichiara al §8.1: *"questa consegna ha prodotto codice, build, deploy e due commit, non una misura runtime"*. ⇒ **il fix del cambio stanza e i due numeri del fusibile sono "implementati, non verificati"** *(`REFERTO-RITORNO-IN-VITA-E-NUMERI.md:561-566`)*.
2. 📊 **E in `G1` il corpo del morto NON e' entrato in un trigger di cambio stanza**: e' stato **il peer** a usare la porta ⇒ **la meccanica e' strutturalmente possibile ma non e' quello che e' successo**, e il fix **non e' provato da un log** *(`:567-572`)*.
3. ⚠️ **Lo "stato incoerente vero" non ha nessuna occorrenza in quei log**: il predicato e i marker ci sono, la riproduzione no *(`:573-579`)*; e del pari **il tentativo automatico di `Respawn()` (`INCOHERENT_GRACE_SECONDS`) non e' stato esercitato** *(`:585-588`)*.
4. ⚠️ **`ESCAPE_SECONDS = 15 s` e' una SCELTA, non una misura**: il numero "quanto ci mette il gioco a finire dopo la `CinematicEnded`" **non esiste in nessun log**, ed e' la ragione per cui il marker dice **da dove** conta *(`:592-596`)*.
5. 📊 **E l'assenza di tasti nella sessione del cambio stanza e' un'ASSENZA, non una misura** *(`:580-584`)*: `[Morte] il giocatore e' morto` = 0 e `RESPAWN CHIESTO` = 0 **dicono che nessuno ha chiesto il respawn**, non **cosa** ha fatto partire il load *(lo dice la riga `Door opened`, non una misura nostra)*.

> ⚠️ **E una cosa va detta, perche' e' il filo di questa sezione**: **`RETE 1` e' provata nei log, il fusibile e' provato nei log come DIFETTO, e i due fix di oggi non sono provati affatto.** ⇒ **la prossima sessione non deve "vedere se funziona": deve fare i DUE casi del §4 punto 1**, e solo dopo si decide se togliere il fusibile *(§3)*.

---



---

## [2026-10-17 01:07] ⛔⚠️ **IL FUSIBILE E `RETE 1` NON ESISTONO PIU'** *(e la sezione del `2026-10-16 22:00` li descrive ancora come PRESENTI)* — piu' il **ROLLBACK**, l'**AUTO-DEPLOY SPENTO**, le **DUE RUN** e **cosa e' stato verificato dall'utente**

**Referti**: `scratch/REFERTO-ROLLBACK.md` *(il rollback)* · `scratch/REFERTO-AUTODEPLOY.md` e `scratch/REFERTO-DEPLOY-HEAD-2.md` *(l'auto-deploy e il deploy di HEAD)* · `scratch/DIAGNOSI-MORTE-STUCKATA.md` *(la morte stuckata)* · `scratch/DIAGNOSI-DUE-RUN.md` *(le due run)* · `scratch/PIANO-SPRITE-MORTO-E-M1.md` *(sprite del morto e M1)* · `scratch/STUDIO-TUTTI-NELLA-BOSSROOM.md` *(il punto 0)*.
**Commit**: **`13107c9`** *(ROLLBACK — revert di 7 commit)* · **`2e8cf19`** *(l'auto-deploy spento — e' l'HEAD)*.
⚠️ **E QUESTA SEZIONE NON CANCELLA NULLA**: le righe `docs\TODO.md:6135-6246` **restano dove sono e come sono**, perche' **la storia non si perde** *(e' la regola in testa al file)*. Ma **da qui in poi vanno lette come STORIA, non come stato**: la descrizione del fusibile che sta li' **non e' piu' vera**.

**🔎 COME SI LEGGE QUESTA SEZIONE — le tre etichette del `2026-10-16 22:00`, e vanno cercate una per una:**
- ✅ **VERIFICATO DALL'UTENTE** *(in gioco, con le sue parole)* · 📊 **PROVATO NEI LOG** *(una riga di log, un conteggio)* · 🔧 **IMPLEMENTATO, NON VERIFICATO** *(build + deploy + referto, nessuna run)*.

---

### 1. ⛔⚠️ **LA COSA DA CUI PARTE TUTTO: QUESTO FILE CONTRADDICE L'ALBERO** *(e ha gia' fatto sbagliare)*

**📊 Misurato** *(`scratch/DIAGNOSI-MORTE-STUCKATA.md:40` e `:246`, §4.4)*:

- le righe **`docs\TODO.md:6185-6210`** *(`## [2026-10-16 22:00]`, §2 e §3)* descrivono **il fusibile del respawn come PRESENTE**, con i suoi numeri *(§2: `BOSS_DEATH_GRACE_SECONDS = 30 s`, `ESCAPE_SECONDS = 15 s`, `MAX_ESCAPE_SECONDS = 300 s`)* e con la prescrizione di §3: *"**Finche' quel test non c'e', il fusibile RESTA**"*;
- **e l'albero NON lo ha piu'**: il rollback **`13107c9`** *(commit message: "ROLLBACK dei respawn automatici: revert di 7 commit")* ha **cancellato `DeathRespawnWatchdog.cs`** — `git show HEAD --stat` → `DeathRespawnWatchdog.cs | 1664 --------------------`, **file cancellato**; e **0 occorrenze** in `*.cs` di `DeathRespawnWatchdog` *(verificato oggi: `git grep -c "DeathRespawnWatchdog" -- "*.cs"` → **vuoto**, e il file **non e' tracciato**)*.
- ⇒ ⛔ **la doc contraddice il codice.** E **non e' un dettaglio formale**: e' **la trappola che ci ha gia' fatto sbagliare** — un agente ha letto questa TODO **e ha creduto che il fusibile ci fosse** *(`DIAGNOSI-MORTE-STUCKATA.md:40`: *"il registro del progetto oggi contraddice l'albero"*)*.
- ⇒ ⭐ **e il rollback non ha toccato la doc**: `docs/TODO.md` e' **byte-identica a `0bca7fa`** prima e dopo *(`REFERTO-ROLLBACK.md` V1: `git diff 0bca7fa HEAD -- docs/TODO.md` → **VUOTO**, hash `2DF34A07…C6D93` su disco dopo il rollback)*. **Ecco perche' questa sezione esiste.**

**⛔ E la `RETE 1` non c'e' nemmeno lei.** Erano **la stessa cosa**: `RETE 1` *(leggere `UnityEngine.Input` in proprio e chiamare `Penitent.Respawn()` al primo tasto vero)* e il fusibile sono nati **insieme**, in **`4c15659`**, e **insieme** sono spariti. A HEAD: **0 occorrenze** di `RESPAWN CHIESTO`, `STATO INCOERENTE`, `unityAnyKey`, `fusibile` *(e nei due log nuovi, **0** anche li': `DIAGNOSI-MORTE-STUCKATA.md` §4.3)*. ⇒ ⛔ **oggi NESSUNA riga di codice della mod chiede il respawn**: l'unica via di ritorno e' un **load di livello**.

---

### 2. ⭐⚠️ **E IL PROGETTO AVEVA DECISO IL CONTRARIO** — il rollback ha tolto **ENTRAMBI**

**📊 E' scritto, prima del rollback** — `docs\TODO.md:6210` *(`## [2026-10-16 22:00]`, §3, **ultima riga**)*:

> *"la forma della decisione, in una riga: **si tolgono i TIMER, si tiene la `RETE 1`**, e il fusibile si toglie solo dopo che il test ha mostrato che un tasto vero arriva sempre."*

E la condizione era **esplicita** *(`docs\TODO.md:6208`)*: ⚠️ **"E' UNA DECISIONE DOPO IL TEST, NON PRIMA"** — *"un fusibile inerte non costa niente, un fusibile tolto prima della prova **costa una sessione**"*.
⇒ ⛔ **il rollback ha tolto i TIMER *e* `RETE 1` INSIEME**, cioe' **piu' di quanto la decisione scritta autorizzasse**, e **senza il test** che la condizionava *(`DIAGNOSI-MORTE-STUCKATA.md` §5.3)*.

---

### 3. ⚠️ **LA CONSEGUENZA MISURATA: LA "MORTE STUCKATA"** *(due log, `DIAGNOSI-MORTE-STUCKATA.md` §1.1 e §2)*

**📊 misurato nei log** *(sessione `G1`, 3371 righe, a HEAD `13107c9`)*:

| riga | cosa | `t` |
|---|---|---|
| `G1:2494` | `[Morte] annuncio inviato: G1 e' morto` — la morte | ~303,6 |
| `G1:2499` | `The input blocker DEAD_SCREEN has been enabled.` — **mai piu' spento** | — |
| `G1:2548` | `[DeathScreen] misura t=307.68 … normalAlpha=1.00` — ⛔ **QUI SI FERMA** | 307,68 |
| *assente* | ⛔ **`Penitent.Respawn()` non viene MAI chiamato**: la riga che il suo `Prefix` scrive **sempre** — `[Morte] il giocatore e' morto: …` *(`PlayerDeathSync.cs:397`)* — ha **0 occorrenze** | — |
| *assente* | ⛔ **nessun load**: `Load and activate level` ha l'**ultima** occorrenza a `G1:1712`, **prima** della morte *(controllo positivo: il marker esiste, e dopo `:2494` e' 0)* | — |
| `G1:3358` | `[DeathScreen] misura t=404.13 campione=51 … normalAlpha=1.00` — **la schermata era ancora li'** | 404,13 |
| `G1:3371` | `All mods disposed!` — **il gioco si chiude** | — |

⇒ ⭐ **`Game state` inchiodato a `PLAYERDEAD`**: `G1:2495` (`Game state has been set to PLAYERDEAD`) ed e' **l'ultima** riga `Game state` di tutta la sessione *(totale 10, ultima `:2495`)*.
⇒ ⭐ **la schermata ferma ~100 s** *(da `t=307.68` a `t=404.13`)*, fino alla chiusura del gioco, **con `DEAD_SCREEN` acceso e mai spento**.
⇒ ⚠️ **e il morto non ha nemmeno un soccorso automatico**: senza `RETE 1` e senza fusibile, **la mod non lo aiuta piu'** *(e' il prezzo dichiarato del rollback: `REFERTO-ROLLBACK.md` §5 punto 1)*.

⚠️ **E DUE COSE NON VANNO CONFUSE, perche' la gerarchia conta** *(`DIAGNOSI-MORTE-STUCKATA.md` §3.3 e §6)*:
1. **la "morte stuckata" E' il respawn che non arriva** — e **M1 non ne e' la causa**: il `Kill` del boss arriva a `t≈325,6` *(`G1:2851`)*, **~22 s dopo** la morte, su una schermata **gia' opaca** da `t=307.68`, e **nessuna richiesta di respawn era stata fatta ne' prima ne' dopo**;
2. **M1 pero' e' un difetto a se'** — e **misurato**: fa girare **la sequenza di morte del boss su una macchina in `PLAYERDEAD`**, e quella sequenza **le lascia il `FADE` acceso per sempre**. Differenziale netto: **`G1:2910` acceso → mai spento** *(e `G1:2914` `Active: True` senza il suo `False`)* contro **`G2:2608` acceso → `G2:2632` spento**, **la stessa cutscene su una macchina viva**. ⚠️ **osservazione NUOVA di quella sessione**: il registro non ne aveva un precedente *(§5.4)*.

📌 **E la riga del progetto che descrive esattamente cio' che e' successo** e' la sua stessa condizione, `docs\TODO.md:6208`: *"**un fusibile tolto prima della prova costa una sessione**"*. Il fusibile **e `RETE 1`** sono stati tolti **prima** di quella prova.

---

### 4. ⭐ **LA RACCOMANDAZIONE: RIMETTERE LA SOLA `RETE 1`, SENZA TIMER** *(🔧 proposta — NON implementata, NON verificata)*

**📊 e la prova che la sostiene e' nei log, ed e' quella giusta**: **`RETE 1` e' gia' stata vista funzionare con `DEAD_SCREEN` e `FADE` accesi INSIEME** — cioe' **esattamente la configurazione in cui `G1` e' rimasto bloccato 100 s** *(`docs\TODO.md:6151`: `unityAnyKey=True` con `blockers=(DEAD_SCREEN,FADE)` accesi; il meccanismo e' che **`SetBlocker` NON blocca `UnityEngine.Input`**, `docs\TODO.md:6152`)*.

⇒ **la forma minima, in una riga**: *reintrodurre **la sola `RETE 1`** — lettura in proprio di `UnityEngine.Input` + `Penitent.Respawn()` al primo tasto vero — **senza timer, senza grazia, senza orologio***. E' **la forma che il progetto aveva gia' deciso** *(`docs\TODO.md:6210`)*, ed e' **l'unica che non dipende da un load ne' dalla buona volonta' del peer**.
⛔ **E il fusibile NON si rimette**: il registro lo ha provato **due volte come DIFETTO** *(ha respawnato uno spettatore **e ha distrutto la cutscene**: `docs\TODO.md:6191`)* ⇒ *"un fusibile inerte non costa niente"* **non e' vero**: **costa una cutscene**, e la misura e' nel registro.

⚠️ **E il caso che `RETE 1` NON copre va detto** *(`docs\TODO.md:6209`)*: **un blocco a livello UNITY** — nessun tasto che arrivi affatto *(finestra non a fuoco, dispositivo scollegato, motore fermo)*. In quel caso **la mod e' cieca come il gioco**, e non c'e' uscita automatica. ⇒ **e' il residuo vero, e va dichiarato, non nascosto.**

📌 **E perche' la prossima sessione possa DECIDERE** *(oggi non puo': `DIAGNOSI-MORTE-STUCKATA.md` §7 punto 1)*: **nei due log nuovi NON c'e' nessuna informazione sui tasti** — l'unico componente che leggeva la tastiera in proprio era **il watchdog, cancellato** *(nel codice `anyKey` sopravvive **solo in un commento**: `DeathScreenTransparency.cs:65`)*. ⇒ **il prossimo test deve portarsi la riga `unityAnyKey`** *(o una sonda equivalente)*, perche' **e' l'unica misura che distingue** *"la schermata di morte non accetta i tasti"* da *"nessun tasto e' arrivato"*.

---

### 5. ✅ **IL ROLLBACK — `13107c9`** *(`REFERTO-ROLLBACK.md`)*

- **7 commit revertati**, in **UN SOLO commit**: `4c15659` *(nasce `RETE 1` + il fusibile, e RETE 2)* · `ea9d1ea` · `0393fd2` · `59f34d7` · `d0facd4` · `16a49ee` *(⚠️ **solo la meta' "rete"**: la meta' "congelamento" e' tenuta)* · `18a4500` *(la morte **scriptata** del boss)*. **Revert, non reset**: nessun force-push.
- **Rete di sicurezza `pre-rollback`** → `0bca7fa`, **e branch `backup-prima-del-rollback`**; la prima stesura con un errore resta raggiungibile col tag **`rollback-pre-correzione`** → `4ab2212` *(l'errore e' dichiarato per intero in `REFERTO-ROLLBACK.md` §2.5: `CorpsePeerTick()` non era piu' invocato — **la vista del peer sarebbe rimasta codice dormiente**)*.
- ⚠️ **quello che il rollback TOGLIE, e va saputo**: (1) **nessuna rete di riserva per il giocatore bloccato** sulla schermata di morte; (2) **il boss ucciso dall'altra macchina non recita piu' la sua morte** *(il boss **resta in piedi**: `EnemySyncManager.cs:422-425`, marker reale `Kill=ok` **senza** `morteScriptata=`)*; (3) **il prompt "premi un tasto" resta del gioco** e la mod non lo tocca *(`REFERTO-ROLLBACK.md` §5)*.
- ⚠️ **E il messaggio del commit dichiara un obiettivo che NON ha raggiunto**: dice *"l'obiettivo e' NON usare `kill()`"* — **ma `kill()` c'e' ed e' eseguito** *(`DIAGNOSI-MORTE-STUCKATA.md` §4.2: `EnemySyncManager.cs:416`, e in `G1:2851` `Kill=ok`)*.
- ✅ **E cosa ha tenuto, di proposito**: **`CorpsePeerTick()`** *(`Multiplayer.cs:215`, da `c5c7299`)* e **`AttachOnce()`** *(`Multiplayer.cs:204`, il congelamento sul cambio stanza)* — entrambe **invocate ogni frame**, verificate con un `git grep` sulla **chiamata** e non sull'esistenza dei metodi *(`REFERTO-ROLLBACK.md` §2.5 e V2)*.

---

### 6. ✅ **L'AUTO-DEPLOY E' SPENTO — `2e8cf19`** *(`REFERTO-AUTODEPLOY.md` — ed e' l'HEAD)*

- ⚠️ **il problema**: il target `Development` del `.csproj` del client copiava **la DLL in gioco A OGNI BUILD** *(righe 59-60, con `DevFolder` **assoluto**: `D:\SteamLibrary\steamapps\common\Blasphemous\Modding\`)*. ⇒ ⭐ **era la causa dei fraintendimenti**: *"si credeva di testare una versione e il gioco ne eseguiva un'altra"*.
- ✅ **il fix**: un interruttore, **`DeployToGame` = `false` per default** ⇒ `dotnet build` **non tocca piu' la cartella di gioco**. E **l'unico modo di deployare e' `tools\deploy-local.ps1`**.
- ✅ **e `deploy-local.ps1` ora copia anche `Modding\data\*`** *(prima copiava **solo** `BlasClient.dll`)*: senza il target, **`Common.dll` non l'avrebbe piu' aggiornata nessuno** — ed e' **la stessa divergenza silenziosa** *(mod nuova con `Common.dll` vecchia)* che si e' poi **misurata davvero** prima del deploy di HEAD: `REFERTO-DEPLOY-HEAD-2.md` §1, coppia mista `5fa9acf2` (build di `wt-04a8fad`) + `d165d391` (build di `wt-HEAD`).
- **📊 provato con TRE build** *(due nel repo principale, una in un worktree)*: la cartella di gioco **identica** ogni volta — **18 file, stessi sha256 E stessi timestamp**. E il controllo **positivo** dell'interruttore: con `-p:DeployToGame=true` la copia **avviene** ⇒ l'interruttore **non e' vacuo**.

---

### 7. ⛔⚠️ **`tools\verify-deploy.ps1` NON VERIFICA** — e il sostituto e' `scratch\marker-check.ps1`

- ⛔ **cosa fa**: stampa *"ESITO: questa macchina esegue il binario atteso. **Puoi testare.**"* con **exit 0 anche su una DLL sbagliata** *(e **non e' stato toccato**: `REFERTO-ROLLBACK.md` §V6 e `REFERTO-DEPLOY-HEAD-2.md` §3, che lo dichiara **non usato come prova**)*.
- ✅ **il sostituto**: **`scratch\marker-check.ps1`** — verifica il **CONTENUTO** della DLL **deployata nel gioco**, con **due metodi indipendenti** (*byte-level sull'heap `#US` in UTF-16LE, **e** a livello IL con **Mono.Cecil**, `Ldstr` su 2459 letterali)*, e **sa dire NO**.
- ⭐ **e la prova che il check DISCRIMINA** *(non e' un timbro)*: sulla mod **vecchia** che stava in gioco (`5fa9acf2…`) lo **stesso** check da' il risultato **opposto** — `[ASSENTE]` sulle stringhe di HEAD e `[PRESENTE]` su quelle esclusive di `04a8fad` *(`REFERTO-DEPLOY-HEAD-2.md` §3D)*.
- 📊 **e il deploy di HEAD e' stato verificato cosi'**: `Modding\plugins\BlasClient.dll` → sha256 **`6d880bec…b447`**, `Modding\data\Blasphemous.Multiplayer.Common.dll` → **`fab798e2…07d0`**, `SERVER-COOP\blas1mpserver.dll` → **`82e21ba1…6b95`**, **protocollo allineato** *(letto con Mono.Cecil dai due `Common.dll`: `Protocol.VERSION = 0` su **entrambi**)*, e **263 bersagli Harmony, 0 tipo assente, 0 METODO assente** *(`REFERTO-DEPLOY-HEAD-2.md` §2-§4)*.
- ⭐ **e questo e' il punto che conta**: **l'auto-deploy spento `+` l'HEAD deployato e verificato NEL CONTENUTO sono le due condizioni che hanno reso affidabile il test che l'utente ha poi confermato** *(§9)*.

---

### 8. ✅ **LE DUE RUN** *(`scratch/DIAGNOSI-DUE-RUN.md` — 4 log, 95/95 assert)*

- ⚠️ **la premessa "la A respawna e la B no" NON e' confermata**: **respawnano ENTRAMBE** *(`A-G2:2554` e `B-G1:2733` dicono la stessa cosa: "e' di nuovo vivo")*. ✅ *(e' la stessa correzione che il referto fa all'utente)*.
- ⭐ **la differenza vera e' IL SALTO DELLA CUTSCENE** *(`[TRACE][CUTSCENE] Video End` presente **solo** in B; `CinematicEnded cancelled=True` in A contro `cancelled=False` in B; ~2,4/5,4 s contro ~23,5 s)*:
  - **se salti** *(run A)*: **`cutscene_block` resta ACCESO sulla macchina del morto** *(acceso `A-G2:2359`, **mai spento**)*, **`POP_UP` non compare mai**, e **la chiusura del gioco non gira**;
  - **se guardi** *(run B)*: `POP_UP` **si accende e si spegne**, `cutscene_block` **viene spento**, e la finestra di morte si chiude a **68,39 s** *(~23 s dopo la fine della cutscene)*.
  - ⇒ ⚠️ **ed e' una differenza DI FORMA, NON DI SOSTANZA**: **il load lo azzera comunque** — in A il morto torna **~31 s prima** *(finestra 36,80 s)*.
- ✅ **il `FADE` si chiude in ENTRAMBE** *(`B-G1:2652`→`:2707`; `A-G2:2463`→`:2524`)*, e su tutte e 4 le macchine gli `enabled` del blocker `FADE` sono **esattamente pari** ai `disabled` *(9/9, 9/9, 7/7, 7/7)* ⇒ ⭐ **il difetto "il `FADE` resta acceso" misurato in `G1` NON si e' riprodotto in queste due run**: o e' un'altra condizione, o e' specifico della sessione di `G1`. **Da non dare per chiuso.**
- ✅ **il cadavere locale e' a posto in entrambe** *(copia `_coop_death_corpse` a terra al punto di morte, `alphaPrima=1.00`)*, ⚠️ **ma il binario di quelle run era `04a8fad`** *(provato da una stringa che esiste **solo** li': `DIAGNOSI-DUE-RUN.md` §0.2)*, quindi **quelle run NON dicono nulla su `c5c7299`**: a `04a8fad` `[CorpsePeer]` **non esisteva** *(0 occorrenze in tutti e 4 i log)*.

---

### 9. ✅✅ **VERIFICATO DALL'UTENTE OGGI — `c5c7299` E LA CUTSCENE AD ENTRAMBI**

> **⭐ VERIFICATO DALL'UTENTE** *(in gioco, con le sue parole)*: *"Il test è positivo, **il cadavere c'è** — e **la cutscene funziona anche ad entrambi**."*

- ✅ **il cadavere del peer C'E'** — il morto si vede **a terra**, **non piu' in piedi** *(`c5c7299`: si manda l'animazione di morte **una volta**, prima del congelamento)*;
- ✅ **la cutscene del boss funziona su ENTRAMBE le macchine** — **compresa quella del morto**;
- ⇒ ⭐ **e questo CHIUDE il difetto storico**: *"dal REMOTO il peer morto risulta ancora in piedi"* — che era **la voce 9** di questo file *(`docs\TODO.md:50`) e `[2026-10-16 20:00]` §1*, chiusa fino ad oggi con la **riserva**: *"il fix locale c'e', **il remoto no**"* *(`docs\TODO.md:6232`)*. ⭐ **LA RISERVA E' CHIUSA.**

⚠️ **E va detto anche il metro di questa verifica**, perche' e' quello che la rende affidabile: **l'auto-deploy era spento** *(§6)* **e l'HEAD era deployato e verificato NEL CONTENUTO** *(§7: `marker-check.ps1`, con il controllo negativo sulla DLL vecchia)*. ⇒ **il gioco eseguiva davvero il binario di `2e8cf19`**: senza quelle due condizioni, *"verificato"* avrebbe voluto dire *"verificato su una versione che non si sa quale fosse"*.

**⭐ E LO STATO DELLE FEATURE DI MORTE, COSI' COM'E' ORA:**

| feature | stato |
|---|---|
| ✅ **cadavere locale** *(la posa chiusa dal segnale del gioco: `Animator.enabled == false`)* | **VERIFICATO** ✅ *(`DIAGNOSI-MORTE-STUCKATA.md` §1.1: `posa del cadavere chiusa dopo 1.46s`, **senza** il `Warn` del tetto)* |
| ✅ **vista del peer** *(`c5c7299`)* | ⭐ **VERIFICATO DALL'UTENTE OGGI** ✅ |
| ✅ **congelamento sul cambio stanza** *(`AttachOnce()`, `Multiplayer.cs:204`)* | **vivo e funzionante nei log** ✅ *(`G1:2887` inseguimento in pausa sulla cutscene, `:2939` ripresa, `:3369` finestra chiusa; ⚠️ **ma la porta l'ha usata il peer**, non il corpo del morto: `DIAGNOSI-MORTE-STUCKATA.md` §4.4)* |
| ✅ **M1** *(la morte del boss portata sulla macchina di chi e' a terra)* | ⭐ **VERIFICATO DALL'UTENTE OGGI** ✅ *(e 📊 nei log: `G1:2851` `Kill=ok` sul morto, `G2:2557` la trasmissione dal vivo)* |
| ✅ **cutscene del boss su ENTRAMBE le macchine** | ⭐ **VERIFICATO DALL'UTENTE OGGI** ✅ |
| ⚠️ **M1: la conseguenza da affrontare** | **APERTA** — sulla macchina del morto la sequenza **lascia il `FADE` acceso** *(§3, misurato in `G1`)*. ⇒ o si tiene **garantendo che la macchina esca da `PLAYERDEAD`**, o si toglie *(ed e' cio' che il messaggio del rollback **dichiara** di voler fare e **non** ha fatto)*. **Da decidere esplicitamente.** |
| ⛔ **`RETE 1` e il fusibile del respawn** | **NON ESISTONO** *(§1-§2)* — **da rimettere: la sola `RETE 1`, senza timer** *(§4)* |

---

### 10. ⚠️ **M1 RESTA — `afca6fe`** *("M1 - LA MORTE DEL BOSS ARRIVA ANCHE SULLA MACCHINA DI CHI E' A TERRA: annuncio `bdead|` + `boss.Kill()` sul morto")*

- ⭐ **E' il fix che ha fatto funzionare le CUTSCENE INSIEME** *(l'utente lo conferma: §9)* ⇒ ⛔ **NON va tolto.**
- ⚠️ **E il difetto a se' che si porta dietro** resta quello del §3: **il `FADE` acceso e mai spento sulla macchina del morto.** ⇒ ⇒ **si tiene, e si affronta la conseguenza**: una macchina in `PLAYERDEAD` che **non puo' respawnare** **non puo' assorbire in sicurezza una sequenza di morte del boss** *(`DIAGNOSI-MORTE-STUCKATA.md` §6 — e' la stessa ragione per cui il fusibile, quando respawnava durante la cutscene, **la distruggeva**: solo al contrario)*.
- ⚠️ **e una cosa che il piano aveva dimostrato e che va ricordata prima di toccarlo**: **se togli M1, il morto NON vede piu' la cutscene del boss** — *"«togliere M1» e «avere la cutscene sul morto» sono, su questa base di codice, la stessa scelta presa due volte al contrario"* *(`scratch\PIANO-SPRITE-MORTO-E-M1.md`, §"la cosa da dire per prima")*.

---

### 11. ⚠️ **LA DECISIONE DI OGGI: IL "GATE" SUL MORTO E' MESSO DA PARTE**

- ⛔ **il "gate"** — trattenere il morto in bossfight sopprimendo `Penitent.Respawn()` *(la via minima studiata in `scratch\STUDIO-TUTTI-NELLA-BOSSROOM.md` §6.2, dentro `PenitentRespawn_Patch` che **esiste gia'**: `PlayerDeathSync.cs:377`)* — **E' MESSO DA PARTE**, e per due ragioni **misurate**:
  1. ✅ **il risultato si ottiene GIA' NON PREMENDO**: e' la **run B** del §8 — **cutscene intera, prompt funzionante, nessuno bloccato**;
  2. ⚠️ **e il rischio e' un giocatore BLOCCATO**: il rilascio dipenderebbe da **condizioni che non controlliamo** *(crash, macchina congelata, peer che non torna)*, e **un `Prefix` che non rilascia blocca la stanza** — **peggio del difetto**.
- ⭐ **E le due conseguenze operative, che vanno scritte perche' altrimenti si riapre due volte**:
  - ⛔ **NON si estende `PenitentRespawn_Patch` con un `Prefix` che ritorna `false`**: **oggi MISURA e non sopprime** *(alza solo `SuppressingWorldReset`, `PlayerDeathSync.cs:394-395`)*, e **deve restare cosi'**;
  - ✅ **e vale la mitigazione che c'e' gia'**: **avviso a schermo / consegna "NON PREMERE"** e **le due righe da cercare nel log** *(`[Morte] il giocatore e' morto: …` = il tasto e' arrivato; `Load and activate level` con una cutscene in corso = **la bossroom e' andata persa**)*. ⇒ **la nota e' scritta anche in `docs\BUG-NOTI.md` voce 5, come PREZZO ACCETTATO** *(e' il file giusto: **un difetto noto e non in lavorazione**)*.

---

### 12. 📌 **LA PROSSIMA COSA DA FARE: IL PUNTO 0** *(la sequenza d'ingresso del boss per chi arriva dopo)*

- ⭐ **il punto 0** = *"tutti i giocatori nella bossroom prima che parta la sequenza d'ingresso"*.
- ⚠️ **E IL PUNTO 0, COME E' SCRITTO, NON E' FATTIBILE** *(verificato: `scratch\STUDIO-TUTTI-NELLA-BOSSROOM.md` §(b), §(c), §(f))*: **non esiste nessuna funzione di attesa da ritardare** — la sequenza d'ingresso del boss e' **un evento PER MACCHINA**, agganciato **a un trigger di scena che ogni macchina attraversa per conto suo**, mentre **il boss e' GIA' NELLA STANZA dal caricamento del livello**. Il predicato *"metti in attesa la partenza"* **non esiste e non e' raggiungibile dal codice**: l'unico chiamante di `StartBossFight()` in tutto l'assembly e' **un pulsante di test dell'ispettore**.
- ✅ **ma il risultato che l'utente vuole si ottiene lo stesso**, e la via e' **praticabile**: **un gate all'INGRESSO della bossroom** *(trattenere il giocatore, non la bossfight)*, con **predicati che la mod ha gia'** *(`OtherPlayerInMyScene()`, `EnemySyncManager.cs:247…`)*.
- ⇒ **l'ordine dichiarato dalle due vie** *(`STUDIO-TUTTI-NELLA-BOSSROOM.md` §(f))*: **(1)** il gate del respawn *(piccolissimo — **ma oggi messo da parte, §11**)*; **(2)** il **gate all'ingresso della bossroom** *(medio)*. ⇒ **con la decisione di oggi, la prossima cosa da fare e' la (2).**
- ⚠️ **e la trappola da non riaprire**: tenere un `Prefix` che **non rilascia** ⇒ *"ingresso mai piu' comandato = stanza bloccata"* *(e' la stessa ragione del §11 punto 2)*.

## [2026-10-17 23:40] ⭐ **LA MORTE DEL BOSS RECITATA ANCHE SULLA MACCHINA DI CHI E' A TERRA** — e quattro riparazioni verificate *(l'`IsDeathAnimation`, l'avviso "DO NOT PRESS", i due marker, il boss esente da `_pendingDeaths`)*, con **la PAUSA toccata e NON risolta**, il **RITORNO IN VITA che a volte non arriva** e **le due barre**

**Referti** *(la fonte, tutti in `scratch\`)*: `REFERTO-ISDEATHANIMATION.md` *(l'`IsDeathAnimation`)* · `REFERTO-AVVISO-NON-PREMERE.md` *(l'avviso)* · `REFERTO-MARKER-PUNTO-0.md` *(i due marker)* · `REFERTO-FIX-AFK.md` *(il boss esente)* · `REFERTO-A-C-MORTE-BOSS.md` *(A e C)* · `REFERTO-PROPRIETA-BOSS.md` *(la proprietà e il latch)* · `DIAGNOSI-A-C-E-PUNTO-0.md` *(A e C misurati, e `scena=`)* · `DIAGNOSI-PAUSA-E-DUE-BARRE.md` *(la pausa e le due barre)* · `STUDIO-VIA-BOSS-E-TRIGGER.md` *(la "via boss" e la leva sul trigger)* · `DIAGNOSI-REGRESSIONE-RIENTRO.md` *(il ritorno in vita e la finestra dell'avviso)*.
**Commit** *(dal più vecchio, e sono tutti su `fix-f1a`)*: **`9be05d0`** *(l'`IsDeathAnimation`)* · **`982baf7`** *(l'avviso, in inglese)* · **`47bf4ee`** *(una riga di commento)* · **`c10bcc6`** *(i due marker)* · **`56db44e`** *(il boss esente da `_pendingDeaths`)* · **`731f4f8`** *(A: la morte recitata · C: il cadavere nascosto)* · **`3239407`** *(① la proprietà · ② il latch `BOSSFIGHT|scena` — **ed è l'HEAD verificato oggi**, `git rev-parse HEAD`)*.
**Base**: `2b06950` *(la DOC del `[2026-10-17 01:07]`)*. **Albero**: pulito (`git status --porcelain` vuoto) prima di questa sezione.

**🔎 COME SI LEGGE QUESTA SEZIONE — le quattro etichette, e vanno cercate una per una:**
- ✅ **VERIFICATO DALL'UTENTE** *(in gioco, con le sue parole)* · 📊 **PROVATO NEI LOG** *(una riga di log, un conteggio, un'aritmetica)* · 🔧 **IMPLEMENTATO, NON VERIFICATO** *(build + deploy + referto, nessuna run)* · 💭 **IPOTESI** *(un meccanismo plausibile, dichiarato come tale)*.

⚠️ **E QUESTA SEZIONE NON CANCELLA NULLA**: le righe **1-6418** restano dove sono e come sono *(è la regola in testa al file)*. Ma **tre cose dette prima vanno lette come STORIA**, e il punto esatto è qui: **(a)** il `[2026-10-17 01:07]` §4 raccomandava *"rimettere la sola `RETE 1`, senza timer"* ⇒ **oggi quella proposta è MESSA DA PARTE** *(§3.4)*; **(b)** lo stesso §12 diceva che *"la prossima cosa da fare è il gate all'ingresso della bossroom"* ⇒ **la lista corta di oggi la mette al 4° posto, dopo tre cose più piccole e più misurate** *(§5)*; **(c)** il `[2026-10-16 22:00]` §3 diceva che il fusibile *"RESTA"* ⇒ **il rollback `13107c9` l'ha tolto, e il §1 del `[2026-10-17 01:07]` lo dice già**. Questa sezione **non ripete** quelle storie: le dà per lette e ne registra l'esito.

---

### 1. ✅ LE COSE FATTE E VERIFICATE *(o in prova)*

**📌 In una tabella, per non seppellire il quadro d'insieme:**

| commit | cosa | grado di prova |
|---|---|---|
| `9be05d0` | ⭐ **`IsDeathAnimation`**: era vera per **86 byte**, ora per **3** *(47/48/49)*; e la prova di vita usa `deathStatusApplied` | 📊 **tavola di verità rifatta oggi senza il gioco** + la tavola vera estratta dai sorgenti (§1.1) |
| `982baf7` | ⭐ **l'avviso "DO NOT PRESS"**, in inglese, solo sulla macchina del morto | 📊 `[BossWarn]` su **una** macchina, e la riga negativa sull'altra (§1.2) |
| `c10bcc6` | ⭐ **i due marker** `[BossView]` / `[BossJoin]` — **solo log, zero comportamento** *(+263 / −0, zero patch)* | 📊 **48 righe `[BossView]` + 10 `[BossJoin]`** in due log (§1.3) |
| `56db44e` | ⭐ **② i BOSS esentati da `_pendingDeaths`** — niente più "danno nuovo" al rientro | 🔧 **implementato, MAI esercitato**: 0 righe `[BossPending]`, e la causa è misurata (§2.6) |
| `731f4f8` | ⭐⭐ **A) la morte recitata** *(`Death()` del boss dopo `Kill()`, whitelist 5 su 17)* · ⭐ **C) il cadavere nascosto** *(`SetActive(false)` all'ingresso, mai `Destroy`)* | ✅ **A: VERIFICATO DALL'UTENTE** *(il boss è animato alla morte)* · 📊 **C: la guardia ha rifiutato un boss vivo** (§1.5, §1.6) |
| `3239407` | ⭐ **① tre proprietà + ④ la rinuncia all'adozione** · ⭐ **② il latch `BOSSFIGHT\|scena = OFF\|IN_CORSO\|FINITA`** | 🔴 **④ provato, ② e ③ no**: la pausa **non è risolta** *(§2.1)* |

---

#### 1.1 ⭐ **`IsDeathAnimation` CORRETTO** — era vera per **86 byte**, ora per **3** *(47/48/49)*

- **Il difetto**: `PlayerStatus.IsDeathAnimation` *(a `2b06950`: `PlayerStatus.cs:197-206`)* chiamava `DeathAnimationIndexOf(all[animation].name)` — cioè cercava **il nome letto dalla tabella dentro la stessa tabella** ⇒ il nome **c'è sempre** ⇒ `true` per **ogni byte 0..85**, e `false` solo fuori tabella *(86..239 e le speciali ≥240)*. `DeathStates` *(la lista giusta: `"Death"`, `"Death Spike"`, `"Death Fall"`)* **non era mai consultata** da quel predicato: viveva solo per `DefaultDeathAnimation` *(`REFERTO-ISDEATHANIMATION.md` §1.1 e §2)*.
- ✅ **Il fix `9be05d0`** *(`PlayerStatus.cs:199-219`, e la lista a `:169`)*: il confronto ora è **con `DeathStates`** — `if (DeathStates[i] == stateName) return true;` a **`:215`** — e non con la tabella. Nessuna riscrittura della classe.
- ✅ **E la PROVA DI VITA ora usa `deathStatusApplied`** *(nuovo campo `OtherPlayerScript.cs:24`, condizione a **`:812`**, scritto **solo** da `SetDeathStatus` a **`:1083`**)*, non `playerDead` — che **oscillava a ogni frame** *(riabbassato a `:1005` dal ramo `else` di `Update`)*. Con il solo predicato corretto sarebbe rimasto **un tiraggio a sorte**: la protezione dipendeva dal fotogramma in cui arrivava il pacchetto *(`REFERTO-ISDEATHANIMATION.md` §2.1 punto 2)*.
- ⭐ **VERIFICATO SENZA IL GIOCO — e rifatto OGGI, alle 23:40, con la tavola vera estratta dai sorgenti** *(`scratch\predicate-check.ps1`, che compila `PlayerStatus.cs` + `Data\AnimationStates.cs` con `csc`)*:

```
byte   0 = Idle                   -> IsDeathAnimation=false
byte   6 = Run                    -> IsDeathAnimation=false
byte  25 = Grounding Over         -> IsDeathAnimation=false
byte  45 = Hurt                   -> IsDeathAnimation=false
byte  47 = Death                  -> IsDeathAnimation=true
byte  48 = Death Spike            -> IsDeathAnimation=true
byte  49 = Death Fall             -> IsDeathAnimation=true
byte  50 = Attack                 -> IsDeathAnimation=false
byte  81 = Healing                -> IsDeathAnimation=false
byte  85 = RegresoAPuerto         -> IsDeathAnimation=false
byte  86 = <fuori tabella>        -> IsDeathAnimation=false
byte 239 = <fuori tabella>        -> IsDeathAnimation=false
voci della tabella = 86 | byte con IsDeathAnimation=true = 3
```

⇒ ⭐ **vero per 3 byte su 86, e sono esattamente 47/48/49** *(prima: 86 su 86)*. **Questa è una prova che non dipende dal gioco, né da una build, né da un hash**: si rifà dai sorgenti in qualunque momento.
- 📊 **E la prova nei log del difetto riparato**: nella sessione del `DIAGNOSI-A-C-E-PUNTO-0` `IsDeathAnimation=true` compare **solo** con `byte 47=Death` *(2 righe, `G1:2211`,`G1:2212`)*, `byte 45=Hurt` dà **`false`**, e **i byte 48 e 49 non compaiono affatto** ⇒ **nessun falso positivo** *(`DIAGNOSI-A-C-E-PUNTO-0.md` §5)*. Prima della correzione le righe "morto a terra" erano **173** con **19 nomi distinti**, di cui **18 non erano morti** *(es. `byte 6=Run`, `byte 81=Healing`: `G1:2434`, `G2:707`)*.
- ⚠️ **Un residuo dichiarato, che non è di questa correzione**: la **prova di vita** ora *può* scattare, ma **scatta solo se l'oggetto dell'avatar sopravvive alla morte** — nei due log vecchi veniva **ricostruito** prima dell'animazione di risveglio *(`G1:2923` prima di `G1:2937`)* ⇒ `vivo in piedi` = **0** e **non è un fallimento** *(`REFERTO-ISDEATHANIMATION.md` §6 punto 3)*.

#### 1.2 ⭐ **L'AVVISO "DO NOT PRESS"** rimesso, **in inglese** — e **da spostare** *(vedi sotto, e §5 voce 1)*

```
DO NOT PRESS: the boss is dying - the cutscene is about to start
```

- **Il testo esatto** è la costante `TEXT` a **`BossDeathWarning.cs:83`** *(tutti caratteri ASCII: nessun accento che una notifica o un log possano storpiare)*, **traduzione** — non riscrittura — del testo italiano del commit originale *(`REFERTO-AVVISO-NON-PREMERE.md` §4)*. **Nessuna chiave di localizzazione**: `DisplayNotification` vuole una **stringa** *(`NotificationManager.cs:23`)*, e con una chiave l'avviso sarebbe arrivato **nella lingua del gioco** — l'opposto della richiesta *(§4)*.
- **Non è un prompt modale e non blocca niente**: è una riga dell'elenco di `NotificationManager`, che **appare e sfuma da sola**; **interruttore** `WARN_PLAYER_ON_BOSS_DEATH` a **`BossDeathWarning.cs:78`** *(`static readonly` e **non** `const`: con un `const true` la riga `if (!X) return;` farebbe emettere `CS0162`)*.
- **Dove è agganciato** *(e i due punti sono quelli ORIGINALI, senza il corriere morto)*: `EnemySyncManager.cs:566` *(`rx:ApplyBossDeath`, subito dopo `boss.Kill()`: è **l'istante in cui il boss muore** su questa macchina)* e `EnemySyncManager.cs:4904` *(`tx:`: questa macchina **ha visto lei** il boss morto)*, più il `Tick` per frame a **`Multiplayer.cs:225`**. **Zero patch Harmony, zero `MonoBehaviour` nuovi, zero scritture sul gioco** *(`REFERTO-AVVISO-NON-PREMERE.md` §3)*.
- ✅ **VERIFICATO NEI LOG: compare SOLO sulla macchina del morto.** Sulla macchina del morto `G2:2748` `Notification: DO NOT PRESS: the boss is dying - the cutscene is about to start` e `G2:2749` `[BossWarn] AVVISO A SCHERMO n.1 … predicato=IsLocalPlayerDead()=True · durata=4.0s`; sulla macchina del vivo la riga **negativa** `G1:2598 avviso NON mostrato: il giocatore locale e' VIVO` *(`DIAGNOSI-A-C-E-PUNTO-0.md` §5)*. ✅ **E l'utente lo ha visto a schermo** *("l'avviso ovviamente deve essere in INGLESE" — ed è in inglese)*.
- 📊 **E la distanza dalla cutscene, che è il punto della lista corta (voce 1)**: in quella sessione l'avviso esce a `G2:2749` e la cutscene parte a `G2:2776`/`2788` `CinematicStarted t=254.54` — **dentro l'intervallo di un `[Probe]`** *(2,00 s)* ⇒ **l'avviso c'era quando serviva**. ⚠️ **Ma in un'altra sessione la stessa distanza è ≈9 s** *(sotto)*: **è per questo che la finestra va spostata**, non perché l'avviso sia sbagliato.
- ⚠️ **E OGGI LA FINESTRA È SBAGLIATA — con i numeri** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §4.2)*: la durata **non si passa**, la decide la config *( `durata=4.0s` = `config.notificationDisplaySeconds`, letto da `BossDeathWarning.DurationSeconds()` a **`BossDeathWarning.cs:224`**; default `4f` in `Config.cs:18`)* ⇒ **l'avviso scade 4 s dopo il `Kill()`**. Le quattro ancore misurate in quella sessione:
  - **avviso** `t≈400,1` *(`G2:3006`/`3007`; il probe `G2:3020` dice `t=400.31`)* ⇒ **scade `t≈404,1`**;
  - **la cutscene comincia** `t≈409,5` *(`G2:3035` `Starting cinematic video`, `G2:3047` `CinematicStarted`, fra `t=408.34` di `G2:3032` e `t=410.35` di `G2:3063`)* ⇒ **≈9 s dopo il `Kill()`**;
  - **il tasto fatale arriva** `t≈425` *(`G2:3085` `FADE` acceso, `G2:3118` `CinematicEnded cancelled=True`, fra `t=424.43` di `G2:3084` e `t=426.44` di `G2:3100`)* ⇒ **≈25 s dopo il `Kill()`**.
  - ⇒ ⛔ **l'avviso era già sparito dallo schermo quando la cutscene è cominciata *(≈5 s prima)* e quando è arrivato il tasto *(≈21 s prima)***: **non era a schermo nel momento in cui è stato premuto**. ⚠️ **E una correzione al referto, perché un numero sbagliato fa sbagliare la decisione**: il referto scrive *"≈15 s dopo che l'avviso era sparito"* — **l'aritmetica giusta è ≈21 s** *(425 − 404,1)*. **La sostanza non cambia, la misura sì.**
  - ⇒ ⭐ **DA SPOSTARE**: la finestra va **dalla morte alla CUTSCENE** *(§3.7 A — zero comportamento, zero patch, zero blocker)*, ed è **la voce 1 della lista corta** *(§5)*. ⚠️ **Non rende il guasto impossibile**: toglie la causa **nello stesso modo in cui il progetto l'ha già scelto**.
- ⚠️ **E UNA COSA DA SAPERE, perché altrimenti si crede a una regressione nuova: l'avviso era stato RIMOSSO PER SBAGLIO col rollback.** Stava in **`59f34d7`** *(il metodo `WarnPlayerNotToPress` a `:905`, il testo italiano a `:918`, la chiamata a `:766` dentro `UpdateFuseGate`)*, e il rollback **`13107c9`** ha **revertato quel commit cancellando `DeathRespawnWatchdog.cs`** *(conflitto `modify/delete` risolto cancellando il file)* ⇒ **l'avviso è sparito con il file**, benché **non fosse la rete** che il rollback voleva togliere *(la `RETE 1` e i timer)*. ⇒ *(`REFERTO-AVVISO-NON-PREMERE.md` §1-§2)* — **non era una decisione, era un effetto collaterale.**

#### 1.3 ⭐ **I MARKER `[BossView]` / `[BossJoin]`** — diagnostici, **zero comportamento** — e **due scoperte**

- **Cosa sono**: due righe di log, **+263 / −0** in **2 file** *(`c10bcc6`)*, **zero patch Harmony nuove** *(263 → 263)*, **nessuna scrittura** su `_pendingDeaths` *(letta e basta)* né su `RegisterEnemy`/`DestroyDeadEnemy` *(`REFERTO-MARKER-PUNTO-0.md` §5 e §7)*. Stanno **prima** e **dopo** `RegisterEnemies`, **nello stesso frame**, così la coppia dice **se a spegnere il boss è stata la registrazione, cioè la mod** *(`EnemySyncManager.cs:2204-2238`)*. `[BossJoin]` è un frame dopo il load *(`Multiplayer.cs:188`)*.
- 📊 **E i marker funzionano**: **48 righe `[BossView]` + 10 `[BossJoin]`** nei due log *(`DIAGNOSI-A-C-E-PUNTO-0.md` §5.2)*.
- ⚠️ **SCOPERTA (i) — `scena=` è la scena PRECEDENTE nella PRIMA passata di spawn.** Ogni caricamento produce **due passate**, e la prima è **etichettata con la scena che si è appena lasciata**: `G2:3162` dice `scena=D17Z01S05` mentre parla del boss di **S11** *(la posizione `(-805.9,22.1)` è la stessa di `G2:1846`)*, e la passata 2 dice `scena=D17Z01S11` *(`G2:3216`)*. **7 volte su 7 in `G2`, 6 su 6 in `G1`.** La causa, verificata: `_scene = scene;` si assegna **solo** in `LevelLoaded` *(`EnemySyncManager.cs:2067`)*, ma `OnEnemiesSpawned()` è un **postfix di `EnemySpawner.SpawnEnemiesOnLoad`** *(`EnemySpawnPatch.cs:25-32`)* che gira **prima** di `OnLevelLoaded`; la passata 2 non è un secondo spawn del gioco ma il ripiego `if (!_spawnFinalized) OnEnemiesSpawned();` in `UpdateCore` *(`EnemySyncManager.cs:4282-4287`)* *(`DIAGNOSI-A-C-E-PUNTO-0.md` §1-bis)*.
  - ⇒ ⭐ **la conseguenza che conta: lo stesso oggetto riceve DUE id diversi nello stesso caricamento** — `en|D17Z01S05|ElderBrother|-806|22` *(`G2:3163`)* e `en|D17Z01S11|ElderBrother|-806|22` *(`G2:3217`)* — **ed è la causa del `[BossPending]` assente** (§2.6): la memoria delle morti tiene `…|S11|…`, la passata 1 cerca `…|S05|…` ⇒ **`Contains` = false**.
  - ⚠️ **e il conto lo paga**: l'id del boss **resta in `_pendingDeaths` per sempre** e `pendingMorti` non scende più *(`G2:3162`, `3216`, `3218`, `3331`, `3332`)* — il commento del codice *(`EnemySyncManager.cs:3015-3019`)* dice che è indesiderabile.
- ⚠️ **SCOPERTA (ii) — il flag "a stanza" NON esiste nella forma che il codice cerca.** Il gioco crea **`D17Z01S11_BOSSDEAD_AC43`** *(`G1:2544`,`G1:2548`; `G2:2701`,`G2:2705`)*, **non** `D17Z01S11_BOSSDEAD`: il nome che `FirstBossDeadFlagOn` *(**`EnemySyncManager.cs:790`**)* prova **per primo** **non esiste per questa stanza** ⇒ **qui funziona solo il ramo di zona** *(`D17Z01_BOSSDEAD`)*, e il ramo "a stanza" di C **non è mai stato esercitato** *(`DIAGNOSI-A-C-E-PUNTO-0.md` §1-bis punto 5 e §9.5)*.
  - ⚠️ **e questo indebolisce C in generale**: per una stanza con **solo** flag di stanza, il predicato **guarderebbe la stanza sbagliata** nella passata 1 *(§1.6)*.

#### 1.4 ⭐ **② I BOSS ESENTATI DA `_pendingDeaths`** *(niente più "danno nuovo" al rientro)* — `56db44e`

- **Prima**: nel ramo `_pendingDeaths` di `RegisterEnemy`, **anche un boss** finiva in `MarkSpawnPointConsumed` + `DestroyDeadEnemy` ⇒ `KillEnemyLocally` + **`SetActive(false)` su un boss che la scena aveva appena rimesso in scena**, **su una macchina sola**, alle spalle della FSM della stanza. **Non era "il boss è morto anche qui": era un danno nuovo** *(`REFERTO-FIX-AFK.md` §2, e il commento gemello nel ramo `bdead|`)*.
- ✅ **Ora** *(`EnemySyncManager.cs:3024-3036`)*: se `IsBoss(enemy, id)` ⇒ **`_pendingDeaths.Remove(id)`** *(l'id **esce** dalla memoria: se restasse, la decisione verrebbe ripresa a ogni registrazione)* + una riga `[BossPending]` *(**`:3028`**)*; **altrimenti** il blocco `else` è **identico a prima, riga per riga**.
- ⚠️ **Il caso normale non cambia**: `IsBoss(...) = false` ⇒ `else` ⇒ spawn point risegnato consumato + istanza abbattuta *(`REFERTO-FIX-AFK.md` §2.1)*.
- ✅ **E il verdetto sul "①" (riaccendere la tree prima di `Kill()`)**: **FERMATO DI PROPOSITO**, con **due prove** — *(a)* la tree **non è l'attuatore della morte**, è una cosa che **la morte spegne** *(il trigger `DEATH` lo tira `Behaviour.Death()`, che `Kill()` non chiama)*; *(b)* riaccenderla **arma l'AI su un cadavere** *(il collider di danno resta acceso: `DamageAreaCollider.enabled = false` sta solo in `Damage(Hit)`)* *(`REFERTO-FIX-AFK.md` §3)*. ⇒ ⭐ **"meglio un AFK noto che un boss che combatte da morto".**
- 📊 **e la misura che lo rende leggibile in una riga**: il campo `treePrima=` nel `[BossDeath RX]` dice **se la tree era ferma quando `Kill()` è passato** — e nei log dice **`treePrima=off/isRunning=False`** *(`G2:2747`)* ⇒ **la diagnosi è confermata sul campo** *(§3.1 del referto)*.

#### 1.5 ⭐⭐ **A) LA MORTE RECITATA** — `Death()` del boss invocata **dopo** `Kill()` — `731f4f8`

- **La forma**: dopo `boss.Kill()` *(e **solo se** `Kill()` ha davvero ucciso: `morte=non-invocata:kill-fallito` altrimenti)*, `ApplyBossDeathSequence` *(`EnemySyncManager.cs:596`)* cerca l'`EnemyBehaviour` del boss e invoca, **per riflessione**, il suo metodo pubblico `Death()` senza parametri — **l'ordine è quello NATIVO**: nel gioco `<Boss>.Damage(Hit)` rende `Status.Dead` e **poi** chiama `Behaviour.Death()` *(`ElderBrother.cs:74-101`)*. **Una volta sola per id** (`_bossDeathsApplied`).
- ⭐ **La whitelist: 5 dentro, 12 fuori** — `BossDeathSequenceWhitelist` a **`EnemySyncManager.cs:378-386`** *(verificata oggi)*: **`ElderBrotherBehaviour`, `LesmesBehaviour`, `QuirceBehaviour`, `PerpetuaBehaviour`, `EsdrasBehaviour`**. **Fuori i 12 che toccano il `Penitent`, l'input o la camera** *(Amanecidas, BurntFace, Crisanta, Isidora, PontiffGiant, PontiffHusk, PontiffOldman, PontiffSword, Snake, SingleAnguish, TresAngustias, WickerWurm — tabella tipo per tipo con la riga di `Death()` in `REFERTO-A-C-MORTE-BOSS.md` §2)*. Per i 12 **non si invoca niente**, e il log lo dice: `morte=escluso:<tipo>-non-in-whitelist`.
- ✅ **VERIFICATO DALL'UTENTE**: *"il boss è **animato alla morte**"* — e la chiamata è **provata nei log**: `G2:2747 [BossDeath RX] … vitaPrima=260 vitaDopo=0 StatusDead=True Kill=ok **morte=recitata:ElderBrotherBehaviour** treePrima=off/isRunning=False treeToccata=no` — **una sola occorrenza**, `morte=escluso` = **0**, `morte=eccezione` = **0**, e **sulla macchina del giocatore MORTO** *(chi uccide è l'host: `G1:2596` `[EnemyKill]` → `G1:2597` `[BossDeath TX]`; G2 riceve e applica)* *(`DIAGNOSI-A-C-E-PUNTO-0.md` §2.1)*.
- ⚠️ **E va detto senza sconti: il log NON nomina la clip su quella macchina.** `ElderBrother_Death` compare **solo** sul **vivo** *(`G1:2595` `[AnimName] … clip='ElderBrother_Death'`, dove il boss muore nativamente)*, perché alla ricezione l'id esce da `_enemies` **prima** del danno e **il campionatore smette di guardare quel boss** *(`EnemySyncManager.cs:815-826`)*. Restano **due tracce indirette** *( `animRestart=1` a `G2:2758`; `[AnimByEnemy] avvii` da **41** a **42** a `G2:2681`→`2759` )* ⇒ **la conferma che il boss si vede animato è l'occhio dell'utente**, non il log *(§2.2 e §9.1 del referto)*.
- ⭐ **E ha curato ANCHE lo stuck a fine cutscene** — **misurato**, e sulla macchina del morto *(che è il caso che prima restava appeso)*: `cutscene_block` acceso `G2:2709` → **spento `G2:2879`**; **`POP_UP` acceso `G2:2871` → spento `G2:2885`**; **`CinematicEnded cancelled=False`** *(`G2:2870`)*; la clip `CTS02` dura **23,46 s interi** *(`G2:2799`→`2846`, con `[CUTSCENE] Video End`)*. Confronto con la baseline: **run A** aveva `cutscene_block` **mai spento** e `POP_UP` **mai** (`cancelled=True`); **questa run ha il profilo della B** *(`DIAGNOSI-A-C-E-PUNTO-0.md` §4.1-§4.2)*.

#### 1.6 ⭐ **C) IL CADAVERE NASCOSTO** — `SetActive(false)` all'ingresso se il boss è morto — `731f4f8`

- **La forma**: `HideDefeatedBosses(scene, "ingresso in scena")` *(`EnemySyncManager.cs:692-745`, chiamata a **`:2234`**, fra i due marker)* spegne un `Enemy` **boss** *(anche inattivo, via `FindAllEnemiesIncludingInactive`)* se **`Status.Dead`** **oppure** il flag `…_BOSSDEAD` è acceso — cercato in **due forme** *(`<scena>_BOSSDEAD` e `<zona>_BOSSDEAD`)*. **Copertura verificata: 12 flag di boss su 12** nella tabella di `Data\FlagStates.cs`, **11 come stanza intera** e **`D17Z01_BOSSDEAD` come zona** *(perché `D17Z01S11` non ha un flag proprio)*.
- ⛔ **MAI `Destroy`** *(solo `enemy.gameObject.SetActive(false)` — la lezione di `DestroyDeadEnemy`)*; **idempotente** *(`_hiddenBossCorpses`, `:662`)*; **su ENTRAMBE le macchine** *(nessuna condizione `IsHost`)*.
- ✅ **VERIFICATO: non tocca mai un boss vivo — e la prova è che la guardia ha SCRITTO di aver rifiutato.** `G2:3163 [BossCorpse] … NON nascosto (ingresso in scena): il boss sembra VIVO (statusDead=False flag=D17Z01_BOSSDEAD **vita=400**) e un boss vivo non si tocca` — la guardia `BossLooksAlive` *(`EnemySyncManager.cs:757`, condizione a **`:724`**)*: acceso in gerarchia **e** non `Status.Dead` **e** vita > 0 **e** `EnemyBehaviour` **acceso**.
- ✅ **E la controprova negativa è pulita**: al **primo ingresso** nella bossroom *(boss vivo, flag spento)* le righe `[BossCorpse]` sono **ZERO su entrambe le macchine** *(`G1:1908`/`1910` e `G2:1843`/`1845`)*.
- ⚠️ **Ma due cose vanno dette, e sono misure:**
  1. **C non ha rimosso NIENTE in quella run**: la riga `nascosto` dice **`attivoPrima=no: era gia' spento`** *(`G2:3217`)* ⇒ **il `SetActive(false)` è stato un no-op**: **a spegnere il boss è stato il GIOCO**, fra la passata 1 *(`attivo=1`, `G2:3162`)* e la passata 2 *(`attivo=0`, `G2:3216`)*. ⇒ **la rimozione di un cadavere visibile NON è provata** *(servirebbe un log con `attivoPrima=si`)*.
  2. **Il pezzo che rende C innocuo** *(ed era necessario)*: un oggetto spento **non basta** a tenere il boss fuori dalla sincronizzazione — `IsInertScenery` *(`:4233`)* scarta solo "inattivo **e** senza `EnemyBehaviour`", e un boss il suo `EnemyBehaviour` ce l'ha anche da spento ⇒ senza una riga in più il cadavere sarebbe entrato in `_enemies` e sarebbe diventato **un boss invisibile che combatte**. ⇒ `RegisterEnemy` **salta** gli istanze in `_hiddenBossCorpses` *(`:2957`)*.

#### 1.7 ⭐ **`3239407` — LA PROPRIETÀ (tre proprietà, non una) e il latch `BOSSFIGHT|scena`**

- **① le tre proprietà, tutte in `DecideOwnership`** *(che gira **solo sull'host**)*: **②** il boss non entra nel ripiego del primo ramo *(forzatura `:4363-4368`, attesa `:4377`)* · **③** il boss non entra nei due rami di cessione, **con la via d'uscita del recupero** *(`bossRecovery` `:4516`, `takeNow` `:4518`, cessione `:4540`)* · **④** il boss **non viene mai riagganciato a una posizione remota** *(guardia in testa a `AdoptLastKnownPosition`, **`:4008`**, che copre **tutti e 5** i punti di chiamata)*.
  - ⚠️ **e la quarta riga non è un dettaglio: è la ragione per cui il ④ esiste**. Il ramo che faceva il danno era **il PRIMO**, non un passaggio di mano: con `dist_io=22.9` e `dist_altro=21.5` il margine rende `wants=false` **e** `peerCloser=false` ⇒ scattava il ripiego `!peerCloser && localIsHost`, che **scavalca `ShouldWaitForPosition`** e **adotta** la posizione *(`adotta (-805.9,22.1)` = **lo SPAWN**, mentre il boss era a `(-823.0,9.0)`)* *(`REFERTO-PROPRIETA-BOSS.md` §1 e §2; `STUDIO-VIA-BOSS-E-TRIGGER.md` §4.6)*.
  - ⚠️ **E il fix non poteva essere "rendere definitiva la regola com'era"**: quello avrebbe **congelato** il salto allo spawn invece di toglierlo.
- ✅ **IL TEARDOWN DEL TELETRASPORTO FUNZIONA — ed è l'unica delle tre che è PROVATA**: la riga `[Proprieta'] … boss: posizione remota NON adottata (resta a (x,y), …)` è **presente** *(`G1:1789`, `G1:2103`, `G2:1880`, `G2:2062`)* e **`adotta la posizione (-805.9,22.1)` (lo spawn) è ASSENTE per il boss** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §5.2)*. ⭐ **⇒ ④ è provato.**
- ⭐ **② il latch dello stato** *(`:2426-2624`; enum `:2457`, `UpdateBossFightState` `:2575`, aggancio `:4808`, riga `:2613`)*: `BOSSFIGHT|<scena> = OFF | IN_CORSO | FINITA`, **latch** *(non misura: cambia solo su `bossVivi>0` + `peerInScena`/boss toccato, e su `bossVivi==0` da `IN_CORSO`)*, **per scena** e **non svuotato da `LevelLoaded`** *(al rientro il valore deve esserci ancora)*.
  - ⛔ **Cosa NON fa, ed è la parte che conta**: **nessun comportamento** — non accende niente, non spegne niente, non sopprime la cerimonia, non manda un pacchetto. **È un `Dictionary` e una riga di log** *(`REFERTO-PROPRIETA-BOSS.md` §3)*.
  - ⚠️ **e il prezzo è dichiarato**: con un altro giocatore in stanza e un boss **dormiente** il latch dice `IN_CORSO` senza che nessuno combatta — per questo la riga stampa **sempre** anche `bossToccato` e `peerInScena`.
- 🔴 **MA LA PAUSA NON È RISOLTA** — vedi §2.1: **il criterio del referto stesso è fallito.**

---

### 2. 🔴 I PROBLEMI APERTI — con la misura

#### 2.1 🔴 **LA PAUSA DEL BOSS NON È RISOLTA** — **5 passaggi di mano** contro un criterio di **1-2**

- **📊 Il criterio, scritto dal referto stesso** *(`REFERTO-PROPRIETA-BOSS.md` §5.1)*: *"`[BossProp]` deve comparire **1-2 volte** per fight, **non 4+**. Se compare 4+ volte, la proprietà non è stabile e il fix non ha funzionato."*
- **📊 La misura** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §4.3 e §5.2, sulla sessione di HEAD `3239407`)*: **`[BossProp]` compare 6 volte sull'host** ⇒ **5 passaggi di mano**. E le pause **del boss**, non dell'input: **2 sull'host** *(`G2:1877 enabled=False esito=remote owns` → `G2:1882 resume`; `G2:2019` → `G2:2068`)* e **3 sul peer** *(la tree toccata 7 volte, con pause a `G1:1794`, `G1:2105 esito=remote owns`, `G1:2143`)*. Nella pausa lunga *(`G2:2019`→`2068`)* il boss **non si anima** *(**0** `[EnemyAnim]` del boss)* e **non si sposta** *(stessa `pos`)*, mentre la vita scende **solo per i relay** *(`[Danno] relay … vitaPrima=370`, `G2:2033`)* e l'owner è `altro` con `dist_altro=1.4` *(`G2:2028`)*.
- ⚠️ **E il referto `3239407` va corretto in due punti, perché dichiarava due cose che il codice non fa:**
  1. **La "ripresa" (`wants`) NON è chiusa per un boss.** Il referto (§2, tabella ③) dice *"il boss non entra nei due rami di cessione (**`wants`-take** e `peerCloser`-give)"* — **nel codice solo la cessione è chiusa**:

     ```csharp
     // EnemySyncManager.cs:4516-4520  (HEAD 3239407)
     bool bossRecovery = boss && !state.OwnedByLocal && !OtherPlayerHereAndAlive();
     bool takeNow = bossRecovery ||
                    (wants && !state.OwnedByLocal && Time.time - state.OwnerSince >= OWNER_HOLD_SECONDS);  // ← nessun `!boss`
     …
     else if (!boss && peerCloser && state.OwnedByLocal && …)                                            // :4540 — solo QUESTA è chiusa
     ```
     **Il log lo mostra in chiaro**: `G2:2063 [BossProp] … padrone=io motivo=fisso wants=True … dist_io=7.6 dist_altro=1.1` — **l'host riprende il boss mentre l'altro è più vicino e lo sta simulando**; e `G2:1878`→`G2:1881` sono **due passaggi di mano in tre righe**.
  2. **Il secondo ramo di `②` è forato dall'attesa della posizione.** `EnemySyncManager.cs:4377-4378`: `if (!forced && wants && ShouldWaitForPosition(enemy, state)) take = false;` — con l'altro **vivo in scena** *(`forced=false`)* **l'attesa si applica anche al boss**. La prova sta in due righe dello stesso istante: `G2:1878 [BossProp] … padrone=altro … pos=(-805.9,22.1)` *(**lo spawn**)* mentre `G2:1877 [EnemyTree]` dà `pos=(-810.1,10.0)` ⇒ **è esattamente il "lasciarlo al peer" che doveva essere evitato**, e produce la pausa a `G2:1877` e la ripresa tre righe dopo *(`G2:1882`)*.
  - ⇒ ⛔ **conclusione da registrare**: **la pausa non è chiusa**, e **non è un difetto di misura** — sono **due condizioni nel codice** *(la ripresa aperta per i boss e l'attesa che fora il ramo ②)*. **Va corretto** *(§5, punto 2)*.
- ⭐ **E l'utente ha isolato il caso, ed è la parte più utile di tutta la giornata**: *la pausa succede **al rientro dopo il RESPAWN** e **NON** col boss appena spawnato* ⇒ **è legata al respawn del Penitent**. ✅ *(isolamento dell'utente, una run contro l'altra)*
  - 💭 **e il meccanismo che lo spiega è coerente con il codice**: durante il respawn il giocatore locale **non è né vivo né fuori scena** — `OtherPlayerInMyScene()` **è vero** *(la scena annunciata è la stessa)* mentre `OtherPlayerHereAndAlive()` **è falso** *(`IsDead`)*. Nel **primo ramo** questo è coperto *(`take = otherCanSimulate ? wants : true`, `:4368`)*; **nella ripresa no**, perché `bossRecovery` usa **lo stesso predicato** ma `takeNow` **non lo usa**: con `wants=true` e `bossRecovery=false` *(l'altro è tornato vivo)* la ripresa si riapre **esattamente nella finestra del respawn**. 💭 **Ipotesi**, non ancora misurata con un marker dedicato.
  - 📌 **e la cosa da fare per chiuderla**: una riga `[BossProp]` **per cambio di decisione** esiste già; manca **il motivo del "perché"** — cioè `wants`, `peerCloser`, `hold`, `ShouldWaitForPosition` e **le due distanze** *contestualmente* *(è il marker `0c` proposto in `STUDIO-VIA-BOSS-E-TRIGGER.md` §7.2)*.

#### 2.2 🔴 **LO SKIP DELLA CUTSCENE A VOLTE BLOCCA IL RITORNO IN VITA**

- **📊 Il fatto, misurato** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §2.1, host `G2`)*: il giocatore locale muore a `t=374.20` e **non risorge più** — **`Penitent.Respawn()` MAI chiamato** *(la riga del `Prefix` della mod, `[Morte] il giocatore e' morto: …`, ha **0 occorrenze**)* · stato **`PLAYERDEAD`** *(`G2:2577`)* e **nessun `PLAYING` dopo** · **`FADE` acceso `G2:3085` e mai spento** *(nessun `FADE has been disabled` dopo)* · **`Load and activate level` = 0 dopo la morte** *(⇒ nessun respawn al falò)* · finestra di morte **131,81 s** fino alla chiusura della sessione *(`G2:4067`)*.
- ⚠️ **La causa è nel GIOCO, e sono due cose che si contendono lo stesso oggetto** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §2.2, dal decompilato in `scratch\ildump\`)*:
  - la **schermata di morte** aspetta **`Input.anyKey`** *(tastiera/mouse)* e poi arma un fade che chiama il respawn: `while (!Input.anyKey) { yield return null; }` + `Core.UI.Fade.Fade(toBlack: true, 1.5f, 0f, delegate { … Penitent.Respawn(); })` *(`DeadScreenWidget.cs:87-96`)*;
  - la **cutscene viene ANNULLATA** da un **bottone Rewired** *(`Cinematics.Update:100-111`, bottone **51**)* e la sua chiusura chiama **`FadeInOut`** *(`Cinematics.EndCutscene:154-190`)*;
  - e **`FadeWidget` ha UNA SOLA `currentTween`**, che **chi chiama dopo la uccide** *(`FadeWidget.cs:48-72` e `:190-222`)* ⇒ **il callback del respawn muore con il suo tween** *(misura: `HideAll()` non è mai girato — il probe legge `normalAlpha=1.00` a **tutte** le 66 misure, da `t=376` a `t=504`)*;
  - **+ `CHERUB_RESPAWN` acceso** *(`G2:2562`)* è **esattamente** la condizione che **impedisce il rilascio del blocker `FADE`** *(`FadeWidget.cs:212-215`)*.
- ⚠️ **E NON è colpa dei nostri commit — verificato**: nella finestra del guasto *(`G2:3007`→`G2:3118`)* **non c'è una sola riga della mod** che riguardi il respawn o la schermata di morte *(`§4.1`)*; **C è no-op** *(`attivoPrima=no`)* e agisce **all'ingresso in scena**, non alla morte.
- ✅ **E in un test successivo lo skip ha FUNZIONATO**: nella sessione di `DIAGNOSI-A-C-E-PUNTO-0` la stessa cutscene `CTS02` dura **23,46 s interi**, `cancelled=False`, `cutscene_block` **spento**, `POP_UP` **gira** *(`§4.1-§4.2`)* ⇒ ⭐ **il guasto è RARO, o legato a una combinazione** *(la periferica? il momento? un fade a durata ≈0 della FSM di stanza?)* ⇒ **triangolazione in corso** *(§5 punto 1)*.
- ⚠️ **E l'ambiguità che il log non può sciogliere** *(💭 ipotesi dichiarata, `§2.3`)*: se il tasto fatale è stato un **bottone del pad**, la cutscene si annulla **e** la schermata di morte **non avanza** *(profilo esatto del log)*; se invece è stato un **tasto di tastiera**, il respawn sarebbe **ancora armato** e basterebbe un altro tasto. **Nessun log dice con quale periferica si è giocato.**
- 📌 **il residuo da dichiarare, non da nascondere**: la mod **ha già** un `Prefix` su `Penitent.Respawn` *(`PlayerDeathSync.cs:377-413`)* ⇒ **potrebbe** chiamarlo per recupero, con le condizioni **tutte già misurate** (`PLAYERDEAD` + `FADE` acceso + `NormalGroup.alpha==1` + `CinematicEnded` passato da N secondi) — **ma è comportamento**: **solo con consenso esplicito** *(`§7.2 C`)*.

#### 2.3 ⚠️ **LE DUE BARRE DELLA VITA** — la vita del boss **non viaggia**

- **📊 La misura** *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md` §4.2-§4.4)*: **110 HP di scarto su 400 = 27,5% della barra**, **costante** dall'ingresso fino alla morte. All'ingresso l'host legge **`life=400`** *(`G1:2421`)* e il client **`life=290`** *(`G2:2164`)*, **nello stesso istante**. La contabilità chiude all'HP: G1 `400 − 260 (propri) − 30 (relay) = 110` *(e a `G1:2944` legge `vita=110`)*; G2 `400 − 110 − 30 − 260 (relay) = 0` *(e a `G2:2789` è a `vitaPrima=10`)*.
- **La catena**: il relay del danno è **filtrato per scena** e in **una direzione sola** *(`Server.cs:835-843`, e i colpi di un peer vanno **sempre** all'host `:850`)*, **ma l'host li scarta se non conosce il nemico** *(`ReportResolveFailure`, deduplicato per handle, `EnemySyncManager.cs:1239`)*: **`G1:2281`** *(`t=299.7 … non in _ids`)* e **`G1:2417`** *(`t=312.6`)* — **lo stesso handle `F3D16595` (il boss)** ⇒ **2 pacchetti persi in chiaro, tutti gli altri in silenzio per via del dedup**. E **la vita del boss non viene mai applicata**: `bool isBoss = IsBoss(...); if (!isBoss) { enemy.SetHealth(currentLife); }` *(`EnemySyncManager.cs:5241-5252`, con il commento *"A BOSS keeps its own health"*)*.
- ⛔ **IL DIVIETO, che resta e va rispettato in UN punto solo: MAI `SetHealth(0)`.** `SetHealth` chiama **`KillInstanteneously()`** quando atterra a ≤ 0 *(`:5247-5252`)*, e **un boss scritto a zero resta in piedi con la barra vuota e la stanza bloccata** — è **il motivo scritto del divieto** *(`:5239-5240`)*. ⇒ **pavimento a 1 HP**, e **il colpo letale continua ad arrivare dal kill/`bdead|`**, che funziona già *(`G1:2956` → `G1:3007`; `G2:2863`-`2864`)*.
- ⚠️ **e la barra non è strumentata**: `BossHealth`/`HideBossHealth`/`EnableLifeBar`/`LifeBar` = **0 occorrenze nei log** ⇒ **dai log si legge la vita delle COPIE, non il disegno della barra**. Che la barra mostri la copia locale è **prova di codice** *(`BossHealth.cs:31-40` legge `target.Stats.Life` della copia che le è stata passata)*, **non misura** *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md` §4.1 e §8.1)*.

#### 2.4 ⚠️ **LO SPRITE FERMO DEL BOSS AL RIENTRO** — e il perché non è un residuo di sessione

- **La forma del sintomo**: sulla macchina del morto il boss **resta in piedi, immobile, con lo sprite acceso**, invece di **recitare la morte** — era **l'AFK** del `REFERTO-FIX-AFK.md` *(§3.2: `Kill()` non passa da `Behaviour.Death()`, quindi il trigger `DEATH` non parte)*. **Su 12 boss su 17 il sintomo resta**, ed è **una scelta** *(sono fuori whitelist: meglio un AFK noto che un effetto sul giocatore)*.
- ⚠️ **E al rientro la scena lo RIPRESENTA VIVO, per un frame**: `G2:3162 [BossView] … attivo=1 · statusDead=0 · bossVivi=1` + `G2:3163 [BossCorpse] … sembra VIVO (… vita=400)` + `G2:3165 Post-spawn: 1 enemies`, e **solo dopo `OnLevelLoaded` è spento** *(`G2:3216 attivo=0`)*. ⇒ **a spegnerlo è il GIOCO**, e **C non lo copre per progetto** *(la guardia `BossLooksAlive`)* *(`DIAGNOSI-A-C-E-PUNTO-0.md` §6.4)*.
- ⚠️ **e attraversa i salvataggi**: il flag che governa la soppressione è un **progresso di gioco**, non uno stato di sessione *( `Received new game progress: D17Z01_BOSSDEAD`, `G2:2713`; `G1:2946` )* ⇒ **lo stesso sprite fermo torna a ogni caricamento**. 💭 **deduzione dalla persistenza del flag**, **non** una misura su un secondo salvataggio *(quel test non c'è: §6)*.
- 📌 **e il pericolo vero è l'inverso di quello che sembra**: se la soppressione del gioco **non** arrivasse — **e non è garantita: dipende da una FSM dentro l'asset di scena** *(`EnemySyncManager.cs:2202-2206`)* — quel boss **vivo a 400 hp** resterebbe **in piedi**, e **C non lo toglierebbe perché *sembra vivo*** ⇒ **una fight che ricomincia da sola al rientro** *(`§6.4` punto 3)*. ⚠️ **Non misurato.**

#### 2.5 ⚠️ **IL PUNTO 0** — la cerimonia per chi arriva dopo *(≈8-10 s di `input block`)* ⇒ **via ②**

- **Il punto 0** = *"tutti i giocatori nella bossroom prima che parta la sequenza d'ingresso"*.
- ⚠️ **E il punto 0, come è scritto, NON è fattibile** *(`scratch\STUDIO-TUTTI-NELLA-BOSSROOM.md` §(b),(c),(f))*: **non esiste nessuna funzione di attesa da ritardare** — la sequenza d'ingresso è **un evento PER MACCHINA**, agganciato a un trigger che ogni macchina attraversa per conto suo, mentre **il boss è GIÀ NELLA STANZA dal caricamento del livello**.
- 📊 **E la "pausa" che l'occhio attribuisce alla cerimonia è quasi tutta PROPRIETÀ** *(`STUDIO-VIA-BOSS-E-TRIGGER.md` §(a) e §0.2)*: **boss fermo (AI + fisica) = ≈30,5 s su 58 s (53%) su G1 e ≈28 s su 62 (45%) su G2 — 100% proprietà**; **giocatore fermo (input bloccato `BossBlock`) = ≈8-10 s per macchina**, **una volta sola** *(`G1:2521`→`2584`; `G2:1931`→`1948`)*. **Sovrapposizione: ≈4 s su G1, 0 s su G2** ⇒ **due difetti, due fix, e nessuno dei due è il trigger.**
- ✅ **e la cerimonia NON si riarma su chi combatte**: `BossBlock` = **2 righe per log** *(= enabled + disabled = **1 cerimonia**)*, `Boss fight Nº0 started.` = **1**, su **entrambe** le macchine *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md` §3.1)* ⇒ **niente secondo `BossBlock`** ⇒ **per la regola del progetto: via ②.**
- ⚠️ **ma il residuo resta, e va scritto**: la cerimonia **del nuovo arrivato** esiste *(è **lui** a calpestare il trigger)*, e **nessuna via ① sulla macchina che combatte la spegnerebbe** — servirebbe un gate **sulla macchina che entra**, che dovrebbe sapere *"in questa stanza c'è già una fight in corso"*: **informazione che prima di `3239407` non esisteva** *(ora c'è il latch `BOSSFIGHT|scena`, **solo log**)*.
- ⭐ **e il dato nuovo di oggi, che è il pericolo della via ①**: al rientro il boss è **vivo e attivo a 400 hp per un frame** *(§2.4)* ⇒ **la soppressione dipende dal gioco, non da noi**.

#### 2.6 ⚠️ **LE ANOMALIE** *(tutte contate, nessuna nascosta)*

| anomalia | misura | lettura |
|---|---|---|
| ⚠️ **`IMPRONTA DIVERSA`** | **1 volta**, transitoria: `G1:1782` *(`DIAGNOSI-REGRESSIONE-RIENTRO.md`)* e `G1:2416` *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md`)*, sempre al **primo frame d'ingresso in S11** | **vero positivo di sostanza**: `host=-1416540193 locale=0` — il pacchetto arriva quando la lista locale è **vuota**. **Due righe dopo** il boss è registrato *(`Post-spawn: 1 enemies`)*. ⇒ **non è la causa di niente**, ma **è la prova che la condizione «liste divergenti» accade** *(il rischio che il referto `3239407` dichiarava: caso (a))* |
| ⚠️ **1 eccezione del GIOCO** | `G2:2496` `NullReferenceException` in `Gameplay.GameControllers.Bosses.Quirce.Attack.BossJumpAttack.OnPenitentEntersArea` | **del gioco, non della mod** *(il contatore della mod dice `exceptions=0`)*; **durante la fight**, ~2 s prima della morte locale; **non tocca `Respawn` né `Fade`** *(il gioco continua, `stalls100=0`)*. ⚠️ **non posso dire se è nuova** |
| ⚠️ **63 eccezioni in `D17Z01S01`** | `NullReferenceException`, stack `Map.MapManager.RefreshMap` ← `Multiplayer.OnLateUpdate`, **tutte fra `L907` e `L1405`** *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md` §6)* | **preesistenti e FUORI dalla bossroom**. ⚠️ **e nella sessione di stasera sono 0** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §6)* ⇒ **non si sono ripresentate** |
| ⚠️ **`[BossPending]` = ZERO** | **0 occorrenze in entrambi i log** *(`DIAGNOSI-A-C-E-PUNTO-0.md` §5)* | ⛔ **non è un guasto dell'esenzione**: è **la conseguenza dell'etichetta di scena sbagliata** (§1.3, scoperta (i)) ⇒ l'id non combacia e il ramo **non entra**. **Ma il conto lo paga**: l'id del boss **resta in `_pendingDeaths` per sempre** *(§1.3)*. ⇒ **il fix `56db44e` è implementato e MAI esercitato** |
| ⚠️ **1 crash a fine sessione** | `G2:3131 Crash!!!`, **dopo** `All mods disposed!`, nel ritorno al `MainMenu`, con `Unloading` a **409 ms** *(contro 48 ms di G1)*; macchina **VM/VMware SVGA** | **fuori dal fight**, **nessuno stack utile** prima del `SymInit` ⇒ **non attribuito al fight** *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md` §6)*. **Da guardare.** |

---

### 3. 💭 LE PROPOSTE VALUTATE — e quelle scartate, con la ragione

#### 3.1 ⛔ **LA "TABELLA DEI MORTI"** *(il morto che rientra muore subito)* ⇒ **SCARTATA DALL'UTENTE**

- **La proposta**: chi rientra nella bossroom **muore subito** *(una tabella dei morti che riporta il morto allo stato di morte all'ingresso)* — chiude il problema del morto che resta vivo in stanza **senza** dipendere dai tasti.
- ⛔ **SCARTATA, e per due ragioni che vanno scritte per non riaprirla**:
  1. ✅ **il risultato si ottiene GIÀ NON PREMENDO**: è la **run B** *(`docs\TODO.md:6362`: cutscene intera, prompt funzionante, **nessuno bloccato**)* ⇒ **la tabella aggiungerebbe un meccanismo dove basta una scelta**;
  2. ⚠️ **e il rischio è un GIOCATORE BLOCCATO**: il rilascio dipenderebbe da **condizioni che non controlliamo** *(crash, macchina congelata, peer che non torna)*, e **un meccanismo che non rilascia blocca la stanza** — **peggio del difetto** *(è la stessa ragione del "gate" in §3.3)*.
- 📌 **e resta come idea da riproporre a progetto stabile** *(dichiarato: **non è sbagliata in sé**, è **prematura**: prima si chiudono le tre cose della lista corta)*.

#### 3.2 ⛔ **INTERCETTARE IL TRIGGER DELLA BOSSROOM** ⇒ **ESCLUSA**, con **tre prove**

1. **il trigger È GIÀ SCATTATO** su ogni macchina che entra: G1 entra in S11 e **≈18 s dopo** ha il suo `BossBlock` *(`L2399` → `L2521`)*; G2 entra e **≈150 s dopo** ha il suo *(`L1805` → `L1931`)* ⇒ *"attivarlo all'ingresso" non aggiunge niente: **l'ingresso lo fa già***;
2. **è una FSM DENTRO L'ASSET**: `BossBlock` **non esiste in tutto `Assembly-CSharp`** *(0 occorrenze)* mentre **esiste come stringa** nei log — lo accende `Tools.Playmaker.Action\InputBlock.cs:16` da un FSM. **L'oggetto-trigger NON è decidibile dal repo** *(la scena è un asset `data.unity3d`, LZ4HC, non decodificato)*;
3. **`onStartBossFight` è L'ULTIMA RIGA e NON FA LA CERIMONIA**: è `BossFightManager.cs:121`, **dopo sette passi** *(barra `:111`, fase `:112`, musica `:115-116`, metrica `:118`, `IsFightStarted=true` `:119`, fiale `:120`)* — e **invocarlo non imposta `IsFightStarted`**. **Zero sottoscrittori in C#** in tutta la soluzione: i listener sono **solo quelli serializzati in scena, ignoti**. E **nemmeno `StartBossFight()` fa la cerimonia**: `BossBlock`, accensione del boss, intro, barriera, fade e camera **no** *(`STUDIO-VIA-BOSS-E-TRIGGER.md` §1.1-§2.3)*.
- ⛔ **E LA CERIMONIA NON VA TOCCATA**: *"provare a farla partire rischia l'unica cosa peggiore di un boss desincronizzato: **una stanza bloccata**"* — `BossBlock` acceso + barriera su + boss spento. ⇒ **è l'unica cosa che può bloccare la stanza**, e per questo **non si tocca** *(`§2.3`, e §7.2 `⛔`)*.
- ⛔ **scartate anche, con la prova**: **`SetBlocker("BossBlock", false)`** come scorciatoia *(finché non si sa chi la accende: è l'unica cosa che oggi rende la scena "spenta", e non sappiamo se il suo spento sia anche altro)* · **sollevare l'evento della FSM** *(`ListenToAll` fa scattare **ogni** FSM in ascolto, e **lo stato in cui entra non è conoscibile**: la cerimonia è **una sequenza**, non un interruttore)* · **teleportare il giocatore nel collider** *(**muove il giocatore**, e il trigger **non è all'ingresso**: si entra dalla porta e si cammina)*.

#### 3.3 ⏸️ **IL GATE SUL MORTO** *(trattenerlo in bossfight sopprimendo `Penitent.Respawn()`)* ⇒ **messo da parte**

- ⛔ **messo da parte**, e per le **due ragioni misurate** già registrate nel `[2026-10-17 01:07]` §11 *(che **restano valide** e non si riscrivono)*: **(1)** il risultato **si ottiene già non premendo**; **(2)** **il rischio è un giocatore bloccato**, e **un `Prefix` che non rilascia blocca la stanza** — peggio del difetto.
- ✅ **e le due conseguenze operative restano**: **NON** si estende `PenitentRespawn_Patch` con un `Prefix` che ritorna `false` *(oggi **misura** e non sopprime, `PlayerDeathSync.cs:394-395`)*; e vale la mitigazione che c'è già — **avviso a schermo** *(§1.2)* e le **due righe da cercare nel log**.

#### 3.4 ⏸️ **`RETE 1` SENZA TIMER** ⇒ **messo da parte** *(⚠️ e cambia la raccomandazione del `[2026-10-17 01:07]` §4)*

- ⏸️ **La proposta era** *(e resta **la forma minima** che quel registro indicava)*: *reintrodurre **la sola `RETE 1`** — lettura in proprio di `UnityEngine.Input` + `Penitent.Respawn()` al primo tasto vero — **senza timer, senza grazia, senza orologio*** *(ogni forma con timer è stata provata **due volte come difetto**: ha respawnato uno spettatore e ha distrutto la cutscene)*.
- ⏸️ **Oggi è messa da parte**, e la ragione **non è un difetto della proposta**: il caso che doveva coprire *(§2.2)* **in un test successivo NON si è presentato** — lo skip ha funzionato, `cancelled=False`, la sequenza di chiusura ha girato *(§2.2)* ⇒ **prima di aggiungere comportamento si triangola** *(§2.2 e §6 punto 9)*.
- ⚠️ **ma il prezzo va scritto, perché non è zero**: **a HEAD nessuna riga della mod chiede il respawn** *(0 occorrenze di `RESPAWN CHIESTO` / `fusibile` / `STATO INCOERENTE` / `unityAnyKey`, `docs\TODO.md:6274`)* ⇒ se il guasto del §2.2 si ripresenta, **la macchina bloccata non ha nessuna rete**, e l'unica via d'uscita è **un load di livello**.

#### 3.5 ⏸️ **IL REPO SEPARATO PER I DOCS** ⇒ **non necessario**

- **La proposta**: spostare `docs\` in un repository a parte *(per non far muovere `TODO.md` a ogni commit di codice)*.
- ⏸️ **Non necessario**, e la ragione pratica è **in questo file**: la doc **non è mai stata toccata dai commit di codice** *(il rollback `13107c9` ha lasciato `docs/TODO.md` **byte-identica**: `git diff 0bca7fa HEAD -- docs/TODO.md` **vuoto**, `REFERTO-ROLLBACK.md` V1)* ⇒ **il problema che il repo separato risolverebbe non si è mai presentato**. *(Decisione, non misura: se un giorno la doc tornasse a essere toccata dai fix, la proposta si riapre.)*

#### 3.6 ⭐ **LA "VIA BOSS"** ⇒ **in due tempi** — e **il primo tempo è fatto, ma da correggere**

- **La forma, e la ragione per cui sono due lavori e non uno** *(`STUDIO-VIA-BOSS-E-TRIGGER.md` §"la cosa da dire per prima" e §6)*:

| pezzo della via boss | rischio | verdetto |
|---|---|---|
| **escludere il boss dal ping-pong di proprietà** | **BASSO** — decisione locale dell'host, **zero pacchetti**, **tre precedenti già in codice** (`SetHealth` saltato, esenzione `_pendingDeaths`, `HideDefeatedBosses`) | ✅ **fatto in `3239407` — ma con due condizioni ancora aperte (§2.1)**: **va corretto** |
| **far viaggiare la vita del boss** | **MEDIO** — è l'unica scrittura sul *gioco* che la mod si era vietata, e la ragione del divieto è scritta | ⚠️ **solo una-tantum**, **mai per pacchetto**, **mai a 0** ⇒ **da fare, non fatto** |
| **"attivare il trigger" all'ingresso** | **ALTO e inutile** | ⛔ **non si fa** *(§3.2)* |

- ⚠️ **e i due tempi vanno decisi INSIEME, non uno alla volta** *(`STUDIO-VIA-BOSS-E-TRIGGER.md` §(g) punto 3, e §7.1 punto 7)*: **fatto (1) senza (2)** il boss resta **congelato a vita intera** sulla macchina che non lo simula *(il difetto **peggiora** agli occhi di chi guarda)*; e **fatto (1) senza la protezione del primo tick** *(che c'è)* il salto allo spawn **diventerebbe permanente**.
- ⚠️ **MAI scrivere 0** *(§2.3)*, e **pavimento a 1 HP**.
- ⭐ **E LA SCOPERTA CHE RENDE (2) MOLTO MENO RISCHIOSO DI QUANTO SEMBRA: le fasi si riallineano DA SOLE, perché derivano dalla vita.** `CheckNextPhase()` legge `CurrentLife / Stats.Life.Base` e avanza **monotonicamente per soglie** *(Crisanta 0,6/0,3, BurntFace 0,75/0,5, …)*, e si ricalcola **al colpo successivo** *(`CrisantaBehaviour.cs:561-596` e `Damage()` a `:1753-1755`)*; `SetHealth` **non** chiama `Damage()` e **non** fa avanzare la fase. ⇒ **scrivere la vita non "desincronizza" la fase: la *riallinea*** — ed è **ciò che oggi manca** *(una macchina che ha perso 110 HP di pacchetti ha **una fase diversa**, quindi **un pool d'attacchi diverso**: due boss diversi nella stessa stanza)*. **Per `ElderBrother` non c'è nemmeno niente da rompere**: legge la vita **una volta sola** *(`ElderBrother.cs:76`, la guardia del giocatore morto)* e il suo pool d'attacchi è **costante** *(`ElderBrotherBehaviour.cs:87-93`)*.
  - ⚠️ **il residuo è estetico e una-tantum**: un salto **in avanti** può **saltare la presentazione** di una transizione *(es. `PushToActionQueue(RELEASE_SECOND_HAND)` di BurntFace)*. **Non rompe lo stato, salta un effetto** *(`§5.3`)*.
- 📌 **la forma minima, se si fa** *(`§5.4`)*: **all'ingresso in scena** e **al cambio di proprietà**, **se** `!Status.Dead` **e** `vitaRicevuta > 0` **e** `vitaRicevuta < CurrentLife` ⇒ `SetHealth(vitaRicevuta)` **una volta**. **Mai per pacchetto, mai in salita, mai a ≤ 0.**
- ⚠️ **e una cosa che il fix NON deve promettere**: l'**aggro NON è un costo** *(la mod ha già il proxy del giocatore remoto, `ApplyAggroTarget`, che fa inseguire il più vicino anche se remoto)* — **ma resta la falla NOTA del Warden**: `GetTargetPredictedPos()` calcola il punto d'atterraggio del **salto** sul `Penitent` **locale** ⇒ **a parità di padrone, il salto cade addosso al padrone**. **Non è causato dalla via boss** e va messo in conto come cosa nota *(`§4.7`)*.

#### 3.7 ⭐ **E LE TRE PROPOSTE SULL'AVVISO, che sono la parte da decidere per prima** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §7.2)*

- ⭐ **A. Il minimo, zero comportamento — l'avviso nell'intervallo giusto** *(consigliato)*: spostare la finestra **dalla morte alla CUTSCENE** *(aprirla a `CinematicStarted`, tenerla fino a `CinematicEnded`, o riaprirla se la schermata di morte è ancora in attesa)*, con un testo che dica la verità: *un tasto adesso respawna **e** annulla la cutscene; le due cose insieme lasciano il respawn perso*. **Costo: una condizione in più, zero patch, zero blocker, zero `SetActive`.** ⚠️ **Non rende il guasto impossibile**, toglie la causa **nello stesso modo in cui il progetto l'ha già scelto**.
- ⚠️ **B. La sola che lo rende impossibile con le leve della mod** *(e va DECISA, non improvvisata)*: **non applicare la morte del boss** *(`ApplyBossDeath`)* **mentre la schermata di morte locale è armata e in attesa**, e riapplicarla al risveglio. È **il fusibile già scritto e poi rollbackato** *(`0393fd2`, `59f34d7` → revert `13107c9`)*: **costo noto** *(il boss resta vivo su questa macchina per la finestra)* ⇒ **scelta dell'utente, non fix gratuito**. **Patchare `DeadScreenWidget` è zona vietata.**
- ⚠️ **C. La rete di soccorso** *(solo con consenso esplicito: è comportamento)*: la mod **ha già** un `Prefix` su `Penitent.Respawn`, quindi **può** chiamarlo, con le condizioni **tutte già misurate** *(§2.2)*.
- ⛔ **il fix che NON serve**: toccare C *(`HideDefeatedBosses`)* o `RegisterEnemy`. **Al rientro il gioco spegne il boss da solo e la stanza aveva già finito tutto** *(`§7.2`, ultima riga)*.

---

### 4. 📌 E IL PROCESSO *(le cose che evitano di ripetere gli errori)*

> ⚠️ **Quattro voci su cinque sono GIÀ registrate** nel `[2026-10-17 01:07]` §6-§7: **qui non si riscrivono, si CONFERMANO** con la prova rifatta oggi, perché **sono le condizioni che rendono affidabile ogni "verificato" di questa sezione**.

| # | cosa | prova di OGGI |
|---|---|---|
| ✅ **1** | **L'AUTO-DEPLOY DELLA BUILD È DISATTIVATO** — il `.csproj` copiava la DLL in gioco **a ogni build** ⇒ ⭐ **era la causa dei fraintendimenti**: *si credeva di testare una versione e il gioco ne eseguiva un'altra* | `Blasphemous.Multiplayer.Client.csproj:23` `<DeployToGame …>false</DeployToGame>` *(verificato)*; la vecchia cartella assoluta è ancora li' come **costante** per il solo caso `-p:DeployToGame=true` *(`:62`)*, e le due `Copy` hanno `Condition="'$(DeployToGame)' == 'true'"` *(`:69-70`)* ⇒ **`dotnet build` non tocca il gioco** |
| ✅ **2** | **`deploy-local.ps1` copia anche `Modding\data\*`** — *mod + protocollo + server dallo stesso build*: senza, `Common.dll` non l'avrebbe più aggiornata nessuno | `tools\deploy-local.ps1:24-35` (copia di `Modding\data`) · `:43-46` `BlasClient.dll` · `:49-52` `Common.dll` · `:56-58` `SERVER-COOP` *(verificato)* |
| ⚠️ **3** | **`verify-deploy.ps1` NON VERIFICA**: stampa *"puoi testare"* **anche su una DLL sbagliata** *(exit 0)* ⇒ ⭐ **il sostituto è `scratch\marker-check.ps1`**, che verifica il **CONTENUTO** (*heap `#US` UTF-16LE + `#Strings` UTF-8, con **due** metodi indipendenti*) e **sa dire NO** | 📊 **rifatto OGGI sulla DLL IN GIOCO**: `sha256 5e6c98a027622edbe9949d548af28b7a07ead33bb8c99758bf502a84a232d7b6`, **375296 byte**, `mtime 2026-10-17 22:41:41` ⇒ **è la build di `3239407`** *(lo stesso sha che il referto della proprietà dichiara)*, e **tutti** i marker sono PRESENTI: `[BossProp]` · `[BossFight]` · `BOSSFIGHT\|` · `morte=` · `[BossCorpse]` · `[BossView]` · `[BossJoin]` · `[BossPending]` · `IsDeathAnimation=` · **`DO NOT PRESS: the boss is dying - the cutscene is about to start`**. ⇒ ⭐ **il binario che l'utente ha in gioco è quello che questa sezione descrive**, verificato **nel contenuto**, non sull'hash di un file di deploy |
| ⚠️ **4** | **`G1` (`lambe`) va deployata A MANO a ogni cambio** | `tools\deploy-local.ps1:15` ha **UNA** cartella di gioco come default `-Game 'D:\SteamLibrary\…\Blasphemous'`, e **nessun giro su due macchine** ⇒ la seconda macchina *(l'utente `lambe`, `DIAGNOSI-PAUSA-E-DUE-BARRE.md:43`)* va servita **a mano** o con un `-Game` esplicito. ⚠️ **e il costo è noto**: un test su `G1` con la DLL vecchia *sembra* una regressione del codice |
| ⚠️ **5** | **due agenti sulla stessa copia ⇒ l'attribuzione si perde**: **ognuno dichiari quali commit sono nella build che deploya** | **è successo davvero, tre volte in due giorni**, e ogni referto lo dichiara: `REFERTO-ISDEATHANIMATION.md` §7 (*"due agenti hanno lavorato sulla STESSA copia … l'attribuzione di una build si perde se non la si dichiara"*, tabella commit per commit) · `REFERTO-AVVISO-NON-PREMERE.md` §8 nota di contorno · `REFERTO-MARKER-PUNTO-0.md` §6 · `REFERTO-FIX-AFK.md` §0. ⇒ ⭐ **la regola che ne esce, e vale per tutti**: **un referto senza la tabella dei commit nella build non è un referto** |

---

### 5. 📌 LA LISTA CORTA, in fondo

> **Cinque voci, in ordine di importanza — e ognuna con la ragione per cui sta dove sta.** ⚠️ **Nessuna è "implementare"**: la 1 è **zero comportamento**, la 2 **corregge un fix appena fatto**, la 3 **tocca il gioco** *(e va decisa insieme alla 2)*, la 4 **aspetta un test**, la 5 **sono due righe di log che rendono leggibile il resto**.

| # | cosa | perché qui | prova | chiude |
|---|---|---|---|---|
| **1** | ⭐ **LA FINESTRA DELL'AVVISO** *(dalla morte alla cutscene)* | **zero comportamento**, costo minimo, **e il difetto è misurato in secondi**: l'avviso scade **≈5 s prima** che la cutscene cominci e **≈21 s prima** del tasto fatale | §1.2 e `DIAGNOSI-REGRESSIONE-RIENTRO.md` §4.2: avviso `t≈400,1` *(durata 4,0 s)* · scade `t≈404,1` · cutscene `t≈409,5` · tasto fatale `t≈425` | ⚠️ **non rende il guasto impossibile** *(§3.7 A)*, ma **toglie la causa** |
| **2** | 🔴 **LA CORREZIONE DELLA PROPRIETÀ** *(la pausa dopo il respawn)* | **è un fix appena fatto che non passa il suo criterio**: `[BossProp]` **6** volte invece di 1-2, e **due condizioni nel codice** lo spiegano *(§2.1)* | `EnemySyncManager.cs:4518` *(la ripresa senza `!boss`)* e `:4377-4378` *(l'attesa che fora il ramo ②)*; log: `G2:1877`/`1878`/`1882`, `G2:2019`→`2068`, `G1:1794`, `G1:2105`, `G1:2143` | la pausa, i freeze a raffica, il "combatte solo con lui" |
| **3** | ⭐ **LA VITA DEL BOSS** *(le due barre)* | **110 HP di scarto = 27,5%**, costanti, e **il buco delle fasi** *(che però si riallinea da sola)*; **va deciso INSIEME alla 2** | §2.3: `G1:2421` `life=400` contro `G2:2164` `life=290`; `EnemySyncManager.cs:5241-5252`; ⛔ **mai 0** | le due barre, e il boss che è "due boss" |
| **4** | ⭐ **IL PUNTO 0** *(via ②)* | **aspetta un test che non c'è**: il caso *"rientro con fight in corso"* **non è in nessun log** *(nei due ingressi si entra insieme, e al rientro il boss era già morto)* | `DIAGNOSI-A-C-E-PUNTO-0.md` §7 e §9.3 *(il test minimo, in 5 passi)* · `DIAGNOSI-PAUSA-E-DUE-BARRE.md` §3 *(nessun secondo `BossBlock`)* | la cerimonia **del nuovo arrivato** *(≈8-10 s di input block)*, **sulla macchina che entra** |
| **5** | 📌 **i due bug piccoli** *(il `scena=` in 1ª passata · il flag "a stanza")* | **sono due righe di log e un predicato**, ma **senza** la prima **ogni rientro va letto due volte** *(e la passata 1 va ignorata)*, e **senza** la seconda **C si regge sul solo flag di zona** | §1.3, scoperte (i) e (ii): `G2:3162` contro `G2:3216`; `D17Z01S11_BOSSDEAD_AC43` contro `D17Z01S11_BOSSDEAD` *(e `FirstBossDeadFlagOn`, `EnemySyncManager.cs:790`)* | la leggibilità del punto 0, e **`[BossPending]`** *(che oggi è disinnescato)* |

⚠️ **E la cosa che la lista NON contiene, di proposito**: **il trigger/la cerimonia** *(§3.2)*, **la `RETE 1`** *(§3.4)*, **il gate sul morto** *(§3.3)*, **la tabella dei morti** *(§3.1)*. **Non perché siano sbagliati, ma perché sono stati scartati o messi da parte con una ragione scritta.**

---

### 6. ⚠️ COSA **NON** È VERIFICATO *(dichiarato, non omesso)*

1. ⚠️ **La clip di morte su `G2`** *(la macchina del morto)*: `ElderBrother_Death` è nominata **solo** sul vivo *(`G1:2595`)*, perché **il campionatore smette di guardare il boss alla ricezione della morte**. Restano **due tracce indirette** *(§1.5)*. ⇒ **"il boss è animato" è la parola dell'utente, non una misura.**
2. ⚠️ **La rimozione di un cadavere da parte di C**: in entrambi i rientri misurati `attivoPrima=no` ⇒ **C è stata un no-op**. **Servirebbe un log con `attivoPrima=si`.**
3. ⚠️ **Il secondo rientro** *(il cadavere "sparito per previsione del gioco")*: `D17Z01S11` è caricata **due volte in tutto**, e al **primo** rientro il cadavere era già spento ⇒ **né confermato né smentito**.
4. ⚠️ **Il punto 0** *(rientro con la fight IN CORSO)*: **non misurato** in nessuna delle sessioni di oggi. ⚠️ **E il `peerInScena` al rientro non è affidabile**: `G2:3211` dice `1/1` mentre l'altro aveva **già chiuso** *(legge la **scena annunciata in rete**, non la presenza fisica)*.
5. ⚠️ **Le durate delle pause #2-#5**: solo la prima è ancorata a due `t=` veri *(**6,8 s**)*; le altre sono **stimate** *(i log non hanno un timestamp per riga)*. La pausa lunga `G2:2019`→`2068` è **≈4-5 s a ~11 righe/s**: **stima, non misura**.
6. ⚠️ **Se il gioco sopprima il boss in un'ALTRA stanza al rientro**: dipende da una **FSM dentro l'asset** *(`EnemySyncManager.cs:2202-2206`)*, **che il repo non contiene**.
7. ⚠️ **Il crash di `G2`** *(`L3131`)*: **nessuno stack utile**, macchina VM ⇒ **non attribuito al fight**, **non spiegato**.
8. ⚠️ **Se l'eccezione `BossJumpAttack.OnPenitentEntersArea`** *(`G2:2496`)* **sia nuova**: manca una run di confronto con la stessa build.
9. ⚠️ **Lo skip della cutscene**: **due esiti opposti in due sessioni**, e **il log non dice con quale periferica si è giocato** ⇒ **la triangolazione è in corso** *(§2.2)*.
10. ⚠️ **La barra**: **non strumentata** *(0 righe `BossHealth`/`LifeBar`)* ⇒ **la vita delle copie è misurata, il disegno della barra è dedotto dal codice** *(§2.3)*.
11. ⚠️ **E una nota di metodo, che vale per tutto quanto sopra**: **i due log di stasera sono la STESSA sessione sullo STESSO binario** *(`3239407`)* — il mandato li trattava come **prima/dopo**, e **non lo sono**: dentro quella sessione il rientro **funziona** su `G1` e il respawn **è rotto** su `G2`. ⇒ **non esiste, in mano, una coppia "prima/dopo" del rientro** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §1 e §8.1)*.

## [2026-10-18 04:20] ⭐⭐ **IL BOSS NON SI TRASPORTA PIÙ: LA STRADA DEL TRIGGER È TORNATA LIBERA** — e il salto segue il proxy, **il boss ferisce il peer**, **gli effetti viaggiano**; restano aperti **la finestra dei 1,5 s**, **le due barre** e **la finestra dell'avviso**, con **i 3 buchi del relay** e **i 23 boss**

**Referti** *(la fonte, tutti in `scratch\`)*: `STUDIO-PUNTO-0-RESET.md` *(il punto 0, il trapianto e la pistola fumante)* · `REFERTO-NESSUN-TRAPIANTO.md` *(il fix `3e52fc9`)* · `DIAGNOSI-MEMORIA-NEMICI-E-AGGIO.md` *(i log del fix e le 5 righe `[Insegue]`)* · `DIAGNOSI-BLOCCO-RESISTE.md` *(il blocco e il boss solido)* · `DIAGNOSI-ENTITA-SOPRAVVISSUTA.md` *(l'entità che torna nuova)* · `REFERTO-SALTO-PROXY.md` *(il salto e i 233 siti)* · `DIAGNOSI-DANNO-BOSS-COOP.md` *(perché il peer non prendeva danno)* · `REFERTO-DANNO-AL-PEER.md` *(il ②)* · `DIAGNOSI-TEST-2.md` *(la run del ②, con i numeri)* · `STUDIO-CANALE-EFFETTI.md` · `REFERTO-SONDA-EFFETTI.md` · `DIAGNOSI-VOCABOLARIO-EFFETTI.md` *(la sonda e il vocabolario)* · `REFERTO-RELAY-EFFETTI.md` *(il relay)* · `TRIANGOLAZIONE-SKIP.md` *(la finestra dei 1,5 s)* · `REFERTO-REVERT-F2.md` · `REFERTO-F2-GUARDIE.md` *(F2 e il revert)* · `REFERTO-A-C-MORTE-BOSS.md` *(i 17 tipi e i 5 in whitelist)* · e i due del `[2026-10-17 23:40]` per le voci che **restano** aperte (`DIAGNOSI-PAUSA-E-DUE-BARRE.md`, `DIAGNOSI-REGRESSIONE-RIENTRO.md`). ⚠️ **`CONFERMA-RELAY.md` NON C'È**: per il relay **non esiste un referto di run**, e l'unica conferma è **quella dell'utente** (§1.4, §6 punto 1).
**Commit** *(dal più vecchio, tutti su `fix-f1a`)*: **`d73e9dd`** *(il revert di F2 — §3.1)* · **`3e52fc9`** *(il punto 0: un boss non insegue una posizione non di questa visita)* · **`f3084ea`** *(il salto segue il proxy d'aggio)* · **`1d9e4fa`** *(il boss ferisce il peer)* · **`6605119`** *(la sonda degli effetti — **rimossa** nel commit dopo)* · **`958a07f`** *(il relay degli effetti)*. **HEAD verificato: `958a07f46ad2797267272a93f6d2934b72b3b5c4`** *(`git rev-parse HEAD`)*, **albero pulito** prima di questa sezione.
**Base**: `cb9fc69` *(la DOC del `[2026-10-17 23:40]`)*.

**🔎 COME SI LEGGE QUESTA SEZIONE — le quattro etichette, e vanno cercate una per una:**
- ✅ **VERIFICATO DALL'UTENTE** *(in gioco, con le sue parole)* · 📊 **PROVATO NEI LOG** *(una riga di log, un conteggio, un'aritmetica)* · 🔧 **IMPLEMENTATO, NON VERIFICATO** *(build + deploy + referto, nessuna run)* · 💭 **IPOTESI** *(un meccanismo plausibile, dichiarato come tale)*.

⚠️ **E QUESTA SEZIONE NON CANCELLA NULLA**: le righe **1-6717** restano dove sono e come sono *(è la regola in testa al file)*. Ma **tre cose dette prima vanno lette come STORIA**, e il punto esatto è qui: **(a)** il `[2026-10-17 23:40]` §3.4 metteva da parte la `RETE 1` **in attesa della triangolazione** ⇒ **oggi la triangolazione c'è e ha una soglia** *(§2.1)*, quindi la **decisione torna sul tavolo** — non presa, riportata; **(b)** lo stesso §2.5 diceva che **il punto 0 "come è scritto non è fattibile"** ⇒ **è ancora vero come è scritto** *(non esiste una funzione di attesa da ritardare)*, **ma il risultato si è ottenuto per l'altra via**: **non spostare il boss** *(§1.1)*, quindi non serve ritardare nessuna cerimonia; **(c)** la lista corta di ieri aveva **5 voci**, quella di oggi ne ha **8** *(§5)*: **non è una riscrittura** — il punto 0 è **fatto** *(e quindi esce)*, la correzione della proprietà **non è stata toccata in questa tornata** *(misurato: `git diff cb9fc69 HEAD` non cambia una riga di `bossRecovery` `:4969`, `takeNow` `:4971`, l'attesa `:4830`)* ⇒ **resta aperta dov'era** e semplicemente non è più in testa alla lista.

---

### 1. ✅ LE COSE FATTE E VERIFICATE *(o in prova)*

**📌 In una tabella, per non seppellire il quadro d'insieme:**

| commit | cosa | grado di prova |
|---|---|---|
| `3e52fc9` | ⭐⭐ **IL PUNTO 0**: *un boss non insegue una posizione **non di questa visita*** — il corpo del boss smette di chiudere il corridoio | ✅ **VERIFICATO DALL'UTENTE** *(il boss non viene più trapiantato)* + 📊 **5 righe `[Insegue]`, tutte su `ElderBrother`**, con `boss=(-805.9,22.1)` *(§1.1)* |
| `f3084ea` | ⭐ **IL SALTO SEGUE IL PROXY**: `GetTargetPredictedPos()` non legge più `Core.Logic.Penitent` **diretto** | 📊 **2 su 7 `[SaltoProxy]` dicono `PROXY`** sul ferro, **0 su 7** sul peer *(§1.2)* — ⚠️ **vale solo per `ElderBrother`** |
| `1d9e4fa` | ⭐⭐ **IL ② — IL BOSS FERISCE IL PEER**: il proxy ha un `IDamageable` che **inoltra**, il peer applica con `Penitent.Damage(Hit)` | 📊 **1 inoltro → 1 applicazione, `-10 PV`** *(`vitaPrima=78 → vitaDopo=68`)* *(§1.3)* |
| `958a07f` | ⭐⭐ **IL RELAY DEGLI EFFETTI**: un `Postfix` su `PoolManager.ReuseObject`, **dedup sul ricevente** | ✅ **VERIFICATO DALL'UTENTE** *(gli effetti si vedono da entrambi)* + 🔧 **il danno dalla copia del ricevente** è provato **nel codice**, non in gioco *(§1.4)* |
| `6605119` | ⚠️ **LA SONDA** — aveva misurato il **vocabolario reale** *(poi **rimossa** in `958a07f`)* | 📊 **53 pool per scena**, **70 chiamate su G1 contro 64 su G2** nella stessa fight, **2 nomi** davvero usati *(§1.5)* |

---

#### 1.1 ⭐⭐ **IL PUNTO 0 — IL BOSS NON VIENE PIÙ TRAPIANTATO NEL CORRIDOIO** — `3e52fc9`

- **Il difetto, in una riga**: la mod **inseguiva** il boss con `InterpolateRemoteEnemies` **a ogni frame** *(`EnemySyncManager.cs:4246`, chiamata da `UpdateCore` a **`:5241`**)*, e oltre **6 unità** di scarto *(`FOLLOW_SNAP_DISTANCE = 6f`, **`:1117`**; il ramo a **`:4268`**)* faceva **SNAP secco** su una posizione che veniva **da una visita precedente della scena** ⇒ `PlaceEnemy` lo scriveva nel corridoio.
- 📊 **La catena, coi numeri** *(`STUDIO-PUNTO-0-RESET.md` §A-§B; `REFERTO-NESSUN-TRAPIANTO.md` §2)*, due righe consecutive dello stesso log:

  ```
  G1:L2826  posNemico=(-805.9,22.1)  statusDead=0  flag=nessuno  vita=400   ← il GIOCO ricrea il boss NUOVO, al suo spawn
  G1:L2827  pos=(-822.0,9.0)                                                ← UNA riga dopo: la mod lo scrive 16,3 unità più avanti
  ```

  e poi: **il padrone si ferma a `-822.6`** *(`G1:L2844`, `G1:L2956`)* contro un trigger che parte a **`x ≈ -816.3 ÷ -814.5`** *(misurato: `G1:L1800` `FootStep n.40 (-816.3,9.5)` **una riga prima** del `BossBlock`; `G2:L3999` `DashDustStop (-814.5,9.8)`)* ⇒ ⭐ **6,3 unità PRIMA del trigger** *(822,6 − 816,3)*. E il boss, **sul padrone**, è **solido e con l'AI accesa** *(`G1:L2836` `enabled=True isRunning=True`, `DIAGNOSI-BLOCCO-RESISTE.md` §(e))* ⇒ **il corpo chiude il corridoio** ⇒ **la cerimonia non parte** ⇒ nessuno chiama `IntroJump` ⇒ lo stato resta `WAITING` ⇒ **il boss resta `Idle` e solido** ✅
- ✅ **Il fix** *(la riga che decide è **una**, `EnemySyncManager.cs:4264`)*: `if (SkipBossFollow(entry.Key, enemy, flat, target, distance)) continue;`. `SkipBossFollow` sta a **`:4305`** e risponde **`false` come prima istruzione** per tutto ciò che non è un boss *(`DIAGNOSI-MEMORIA-NEMICI-E-AGGIO.md` §(b))*; per un boss guarda **la freschezza della voce**: `_remoteTargetsVisit[id]` *(`:927`, scritta **`:5716`**)* contro `_sceneVisit++` *(`:2393`)*, mentre `_remoteTargets` **non viene svuotata a `LevelLoaded`, di proposito** *(`:2370`)*. ⇒ ⭐ **la regola, in una frase: un boss non insegue una posizione che non è di questa visita.**
- ⚠️ **Perché NON "nessun boss, mai"** *(una precisazione che evita di rompere quello che funziona)*: quando è **l'altra macchina** a simulare, l'inseguimento è **l'unico motivo** per cui il pupazzo vede il boss muoversi — misurato: `G2:L4584` `dist_altro=0.0 posAltro=(-820.3,9.0)`, cioè il client **seguiva** il boss combattuto dall'host *(`REFERTO-NESSUN-TRAPIANTO.md` §3)*. La forma letterale (un `return true` in `SkipBossFollow`) è **una riga**, ed è **rifiutata con la ragione**.
- ⭐ **E LA SCOPERTA, che vale più del fix**: **il boss tornava NUOVO a ogni respawn** — la scena lo ricrea a **vita piena 400**, `statusDead=0`, **`flag=nessuno`**, **nella sua posizione d'autore `(-805.9,22.1)`**, identica alla 1ª visita *(`G1:L1752`; `G1:L2823`, `G2:L4907`)* ⇒ **"il reset mancante" non esisteva**: il gioco fa tutto bene, **eravamo NOI a spostarlo** *(`STUDIO-PUNTO-0-RESET.md` §B: «TORNA NUOVO. È nuovo.»)* ✅
- 📊 **E i log del fix, che sono la prova che la guardia agisce**: **5 righe `[Insegue]` in quattro log**, **tutte** su `en|D17Z01S11|ElderBrother|-806|22`, **zero** su un nemico normale, con `boss=(-805.9,22.1)`, `target=(-806.3,14.8)`, `dist=7.2` e **`visita 3 / siamo alla 5`** *(`DIAGNOSI-MEMORIA-NEMICI-E-AGGIO.md` §1.5)*. Il marker è `:4322` (`[Insegue] … inseguimento SALTATO …`), deduplicato per boss e per visita *(senza, sarebbero 60 righe/s)*.
- 🔧 **Build/deploy**: build `Errori: 0`; deploy **267 bersagli** *(zero patch Harmony nuove: era una guardia dentro codice già patchato)*; contenuto verificato con `scratch\marker-check.ps1` **sulla DLL in gioco** — `sha256 4310411a1f770bb3f3a4345872f79bf83f479be10dd2dbc0752037b3147a8ea5`, **382.464 byte**, `mtime 2026-10-18 01:38:33` *(`REFERTO-NESSUN-TRAPIANTO.md` §4)*.
- ⚠️ **Il caso che può peggiorare, dichiarato**: **rientro a metà fight** ⇒ per **≤ 1 s** il boss della macchina che rientra può restare **allo spawn** finché non arriva un pacchetto **di questa visita** *(il padrone rispedisce tutto ogni **1 s**: `FULL_RESEND_INTERVAL = 1f`, `:58`)*. Se in gioco si vedesse troppo, **la leva è una riga** *(freschezza che scade a `OnEnemiesSpawned` + 1 s per i boss già ingaggiati)* — **non fatta, e dichiarata** *(§6 dello stesso referto)*.

#### 1.2 ⭐ **IL SALTO DEL BOSS SEGUE IL PROXY** — `f3084ea`

- **Il difetto, in una riga**: `GetTargetPredictedPos()` *(`Assembly-CSharp.cs:56000-56006`)* leggeva **`Core.Logic.Penitent`** **diretto** ⇒ **il salto mirava sempre al padrone**, qualunque cosa dicesse l'aggio scritto da `ApplyAggroTarget` *(`EnemySyncManager.cs:3998`)*. E non è un dettaglio: per `ElderBrother` **il salto è l'unica locomozione** — `Chase()`, `Wander()`, `Attack()` sono `throw new NotImplementedException()`, `StopMovement()`/`Idle()` sono vuoti *(`REFERTO-SALTO-PROXY.md` §2)*.
- **Il fix**: **una sola patch**, un `Postfix` sul punto di mira *(`BossJumpAimPatch.cs:53`; `Postfix` a **`:59`**; il riconoscimento del proxy a **`:78`**; **una sola scrittura**, a **`:89`**, che cambia solo la **x/y** e lascia **z** del gioco)*. Se il `Target` **non** è il proxy, **`__result` non si tocca**: valore del gioco, **bit per bit** *(`§3` del referto)*. **Bersagli Harmony: 267 → 268.**
- 📊 **La misura, dalla run del ②** *(`DIAGNOSI-TEST-2.md` §(e))*: `[SaltoProxy]` **7 righe su ciascuna macchina**; sul ferro **2 su 7** dicono **`PROXY`** *(`distanza=16.5` e `12.4`)*, sul peer **0 su 7** *(lì il boss non ha mai mirato al proxy di G1)* ⇒ ⭐ **la mira si sposta quando c'è un proxy, e solo allora**.
- ⚠️ **Il limite, dichiarato e non nascosto**: il proxy **non ha velocità**, quindi **non c'è anticipo**; e la sua posizione è fresca entro **0,3 s** *(`RESCAN_INTERVAL = 0.5f` `:47`; `REMOTE_POSITION_MAX_AGE = 0.3f`)* ⇒ contro chi corre il boss può atterrare **qualche unità indietro** *(`REFERTO-SALTO-PROXY.md` §3)*.
- ⚠️ **E l'effetto collaterale atteso, che va letto nel test**: i `[ProxyGuard]` del salto diventano **`DIROTTATO`** invece di `SALTATO`, e **il giocatore locale non prende più il colpo destinato al peer** *(`§7 punto 1`)* — che è **il motivo per cui serviva il ②** (§1.3).

#### 1.3 ⭐⭐ **IL ② — IL BOSS FERISCE IL PEER** — `1d9e4fa`

- **La forma**: il proxy d'aggio `_coop_remote_target` ha da sempre **un collider sul layer `Penitent` e NESSUN `IDamageable`** *(prima di `1d9e4fa`)*, di proposito *(`DIAGNOSI-DANNO-BOSS-COOP.md` §(b))* ⇒ il colpo del boss **lo trovava e lo buttava via**, o **andava in eccezione** sul salto che non ha null-check. **Ora il proxy ha un `IDamageable` che INOLTRA**, e il peer lo applica con **`Penitent.Damage(Hit)`** *(la porta d'ingresso del gioco, `EnemyHitPeerSync.cs:551`)*.
- ⭐ **Generico per costruzione** *(ed è la risposta alla domanda «vale per ogni boss?»)*: **nessun `IsBoss`, nessun `ElderBrother`, nessun elenco** — l'inoltro sta a livello di **collisione** *(`REFERTO-DANNO-AL-PEER.md` §1)*. La patch non nomina mai il boss; l'unica `Damage()` che chiama è **`Penitent.Damage`** **sul proprio** giocatore.
- **Il canale, e perché non tocca il protocollo**: `NetworkType.Attack` *(già esistente: il server inoltra **ai peer della stessa scena escludendo il mittente**, col nome del mittente premesso, `Server.cs:676-699`)* + **un byte nuovo**, `AttackType.EnemyHit = 200` *(`PvP\Models\Enums.cs:75`)*. ⇒ **`PROTOCOL_VERSION` resta 11**, **zero righe di server**, **zero ridistribuzioni di `Common.dll`**.
- **Il filtro che lo rende sicuro**: si inoltra **solo se `hit.AttackingEntity` si risolve a un `Enemy`** *(`AttackerOf`, `EnemyHitPeerSync.cs:288-293`)* ⇒ **trappole, spine, `AreaEffect` e il giocatore locale NON viaggiano** *(esistono su entrambe le macchine: inoltrarli sarebbe **danno doppio**)*. Il contatto resta **saltato** per scelta di `c89c368` *(`ContactDamage` alza `IsTargetOverlapped` **prima** di danneggiare: riaprirlo sarebbe stato uno scambio in perdita)*.
- 📊 **PROVATO NEI LOG — e sono i tre numeri che contano** *(`DIAGNOSI-TEST-2.md` §(a)-(d), build allineate su entrambe le macchine, §0)*: **1 inoltro → 1 applicazione** *(`G1:L1211` `[DannoPeer] … inoltrato … danno=10 … proxyPos=(-803.0,9.3)` contro `nemicoPos=(-803.1,10.0)` — **≈0,7 unità**: il salto è atterrato **addosso al pupazzo**; `G2:L1141` `APPLICATO … vitaPrima=78 vitaDopo=68 perso=10`)* · **2 inoltri BLOCCATI** dal gate «nessun altro giocatore QUI e VIVO» *(il peer era morto)* · **8 colpi d'area MAI inoltrati** *(il filtro: l'attaccante di un'area non è un `Enemy`)* · **zero danno doppio, zero applicazioni fallite**.
- ⚠️ **E LA MIOPIA CHE HA PORTATO AL RELAY**: il ② è **corretto ma cieco sulle aree** *(`DIAGNOSI-TEST-2.md` §(f): «non è rotto, è miope su una forma di attacco»)*. Il ② **non è stato toccato** per rimediare: **la copia dell'area arriva al peer per un'altra via** *(§1.4)*, e ogni macchina ferisce **il proprio** `Penitent` con la **sua** copia.

#### 1.4 ⭐⭐ **IL RELAY DEGLI EFFETTI** — `958a07f` *(e la sonda `6605119`, **rimossa**)*

- **Un hook solo, per tutti i boss e tutti i nemici**: un `Postfix` su **`PoolManager.ReuseObject(GameObject prefab, Vector3 position, Quaternion rotation, bool, int32)`** *(`EffectRelay.cs:72`)* — è **l'unico punto** in cui il gioco crea un effetto da un pool. `Postfix` e non `Prefix`: un `Prefix` che ritorna `false` **sostituirebbe il gioco** *(un interruttore sugli effetti)*; il `Postfix` guarda **a cose fatte**, e se il relay esplode **l'effetto il gioco l'ha già mostrato**.
- **Il payload**: `prefab` + **posizione vera** + **rotZ** *(l'unica rotazione che il gioco usa: misurato, **un solo valore distinto** in tutta la fight)*, **≈35 byte**, sul tipo nuovo **`AttackType.CoopEffectRelay = 201`** *(`Enums.cs:99`)* — accanto al 200, **stessa famiglia**, **zero protocollo (resta 11), zero server** *(`REFERTO-RELAY-EFFETTI.md` §1.2-§1.3)*.
- ⭐ **Il filtro è sul RICEVENTE, ed è questo che rende il fix una proprietà e non una speranza**: il ricevente chiede *«il nome è un effetto del **mio** boss?»* *(catalogo: **5 nomi su 5** risolti dai campi serializzati della copia locale — `CorpsesShockwave`, `MaceImpact`, `CorpseAnimationFx`, `LandingFx`, `CorpsesParticlesJump`)* e *«ce l'ho **già**?»* *(chiave `prefab + F0(x)|F0(y)`, finestra **0,5 s**, `DEDUP_SECONDS` `:114`)*. ⭐ **E la propria copia entra nella tabella PRIMA di tutto** *(`NoteLocalSpawn` `:349`)* ⇒ **il doppione non nasce per costruzione**, anche nelle finestre in cui **entrambe** le macchine simulano.
- ⛔ **Perché NON la strada di `Resources`**: `Resources.FindObjectsOfTypeAll` risponde sì su **53 nomi su 53** — e su `LandingFx` restituisce **`asset=no`**, cioè **l'istanza di scena dentro il pool**, non il prefab ⇒ «un catalogo che **sembra** completo e ha **un buco silenzioso** proprio su un prefab del boss» *(`REFERTO-RELAY-EFFETTI.md` §3.1)*. Scartata con la prova.
- ⚠️ **Il prezzo, dichiarato**: massimo **20 invii al secondo** *(`MAX_SENDS_PER_SECOND` `:124`)*, e quando scatta **lo dice** *(`[FxRelay] freno`)*; l'effetto scartato è **un effetto che il peer non vede**. E **la copia non ha padrone** *(`SetOwner` non viene chiamato)*: il **danno** non ne soffre *(provato nel codice: `CreateHit` **non legge** `WeaponOwner`)*, l'**attribuzione** del colpo sì.
- ✅ **VERIFICATO DALL'UTENTE**: **gli effetti si vedono da entrambi** *(la sua parola)*. 🔧 **NON verificato in gioco**: che la **copia relayata** ferisca il `Penitent` del ricevente — è provato **nel codice** *(`REFERTO-RELAY-EFFETTI.md` §4: la copia è un `BossSpawnedAreaAttack` vero, `CreateHit` costruisce l'`Hit` dal prefab, `AttackArea` colpisce i `IDamageable` **di questa macchina**)*, **non da una run** *(§6 punto 1)*.
- 📊 **E la verifica del contenuto, sulla DLL in gioco**: `sha256 7cff5ab147192ff5a343f458e97ea9c77d504f5cd1f9783953126f1de9db70f8`, **404.480 byte**, `mtime 2026-10-18 03:57:34`; `[FxRelay]` **presente** (7 occorrenze), `[FxPool]`/`[FxRisolvi]`/`[FxRiepilogo]` **assenti** ⇒ **la sonda è andata davvero** *(`REFERTO-RELAY-EFFETTI.md` §7.2-§7.3)*.

#### 1.5 ⚠️ **E LA SONDA CHE AVEVA MISURATO IL VOCABOLARIO REALE** — `6605119`, **rimossa** in `958a07f`

- **Cosa era**: `EffectPoolProbe.cs`, **1.645 righe**, marker `[FxPool]`, `[FxCensimento]`, `[FxBoss]`, `[FxRisolvi]`, `[FxRiepilogo]`. **Non è codice di produzione**: era la misura che serviva a **decidere** se il relay era fattibile, e **ha risposto**.
- 📊 **Cosa ha misurato** *(e che il relay usa)*: **53 pool per scena** *(`[FxCensimento]`: `arma=10 · proiettile=2 · vfx=40 · -`=1)*, **70 chiamate su G1 contro 64 su G2** nella **stessa** fight, **2 nomi** davvero usati, **5 su 5** risolti dal catalogo del boss *(`DIAGNOSI-VOCABOLARIO-EFFETTI.md` §(b) e §(c); `REFERTO-SONDA-EFFETTI.md` §3.3)*. ⚠️ **E la premessa che ha smentito**: l'area di `ElderBrother` è un **`BossSpawnedAreaAttack`**, **non** una `ShockwaveArea` — **4 fonti indipendenti per macchina**, su **entrambi** i prefab e **entrambe** le macchine *(`DIAGNOSI-VOCABOLARIO-EFFETTI.md` §(b))*.
- **La rimozione è contata**: `EffectPoolProbe.cs` **cancellato**, i richiami in `Multiplayer.cs` passano a `EffectRelay.LevelLoaded/Tick`, `git grep EffectPoolProbe` **zero riferimenti**, e i **bersagli Harmony tornano 269** *(`269 − 1 sonda + 1 relay`, `REFERTO-RELAY-EFFETTI.md` §5 e §7.1)*.

---

### 2. 🔴 I PROBLEMI APERTI — con la misura

#### 2.1 ⚠️ **LA FINESTRA DEI 1,5 s** — lo skip della cutscene **uccide il fade della morte**, ma solo **alla fine**

- **📊 La misura, e ha una soglia** *(`TRIANGOLAZIONE-SKIP.md:403`)*: *«il caso blocca se la pressione cade **< 1,5 s prima** di `Cinematics.EndCutscene`. Misurato: **≈1,3 s** (blocca) contro **≈2,7 s** (funziona)»*. La finestra mortale è **≈1,5 s** su una cutscene di **16,5 s** *(O_G2, `mod t` 384,38→400,86)* o di **9,8 s** *(N_G2, 811,14→821,13)* ⇒ **9-15% del video**, e sta **in fondo** *(`:37`)*.
- **Il meccanismo** *(e sono due cose che si contendono **lo stesso** oggetto)*: la schermata di morte aspetta **`Input.anyKey`** e poi arma un fade che chiama il respawn *(`DeadScreenWidget.cs:87-96`)*; la cutscene annullata chiude con **`FadeInOut`** *(`Cinematics.EndCutscene:154-190`)*; e **`FadeWidget` ha UNA SOLA `currentTween`: chi chiama dopo la uccide** *(`FadeWidget.cs:48-72`, `:190-222`)* ⇒ **il callback del respawn muore col suo tween**. In più `CHERUB_RESPAWN` acceso è **esattamente** la condizione che **impedisce il rilascio del blocker `FADE`** *(`:212-215`)*.
- 📊 **E il guasto nei log** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §2.1)*: **`Penitent.Respawn()` MAI chiamato** *(0 righe del `Prefix` della mod)*; **`PLAYERDEAD` e nessun `PLAYING` dopo**; **`FADE` acceso `G2:3085` e mai spento**; il probe legge **`normalAlpha=1.00` a tutte le 66 misure** *(da `t=376` a `t=504`)*; finestra di morte **131,81 s**.
- ⚠️ **DECISIONE NECESSARIA** *(ed è una decisione, non un fix: è **comportamento**)*: **la mod chiama `Respawn()` se `FADE` acceso e `PLAYERDEAD` da > N s**. ⚠️ **Ma va decisa sapendo due cose**: **(1)** il `[2026-10-17 23:40]` §3.4 aveva messo da parte la `RETE 1` **perché mancava la triangolazione** — **ora c'è** *(sopra)*, e dice che il guasto **non dipende dalla periferica** ma **dal tempo** *(`:403`: «il confine non è il tasto, non è il pad»; `:427`: nessuna riga dice con quale periferica si è giocato)*; **(2)** **il rischio non è cambiato**: un meccanismo che **non rilascia** blocca la stanza, e **un `Prefix` che non rilascia** è peggio del difetto *(è la ragione per cui il gate fu messo da parte, §3.4 di ieri e §3.3 di oggi)* ⇒ **serve la via d'uscita**, non solo la soglia.

#### 2.2 ⚠️ **LE DUE BARRE** — la vita del boss **non viaggia**, e le fasi **non si aggiornano quando la vita viene scritta**

- **📊 La misura** *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md` §4.2-§4.4, già registrata nel `[2026-10-17 23:40]` §2.3 e **non superata** da nessun log nuovo)*: **110 HP di scarto su 400 = 27,5%**, **costante** dall'ingresso alla morte. All'ingresso l'host legge **`life=400`** *(`G1:2421`)* e il client **`life=290`** *(`G2:2164`)*, **nello stesso istante**; la contabilità chiude all'HP *(G1 `400 − 260 − 30 = 110`; G2 `400 − 110 − 30 − 260 = 0`)*.
- **La catena, e la riga da cambiare**: il relay del danno è **filtrato per scena** e in **una direzione sola** *(`Server.cs:835-843`)*, **ma l'host scarta i pacchetti se non conosce il nemico**; e **la vita del boss non viene mai applicata**, per scelta scritta: `// A BOSS keeps its own health` a **`EnemySyncManager.cs:5692-5693`** e `enemy.SetHealth(currentLife);` **solo se `!isBoss`** a **`:5696-5705`**.
- ⚠️ **E LE FASI, che è la parte da non sbagliare**: derivano dalla vita *(`CheckNextPhase()` legge `CurrentLife / Stats.Life.Base` e avanza **monotonicamente per soglie** — `STUDIO-VIA-BOSS-E-TRIGGER.md` §5.3, già registrato nel `[2026-10-17 23:40]` §3.6)*, **ma `SetHealth` non passa da `Damage()`** ⇒ **la fase si aggiorna al primo colpo dopo**, non quando si scrive la vita ⇒ ⭐ **va fatto "stato + disegno insieme"**: scrivere la vita **e** riallineare/stampare la fase **nello stesso gesto**, altrimenti si ottiene **una barra giusta con una fase vecchia** — cioè **due boss diversi nella stessa stanza** lo stesso. *(La barra legge `Stats.Life.Current` a ogni frame: `BossHealth.cs:31-40`, `48-55`; quindi se la scrittura è nello stesso punto, il disegno segue.)* ⚠️ **Per `ElderBrother` non c'è niente da rompere** *(legge la vita **una volta sola**, `ElderBrother.cs:76`, e il suo pool d'attacchi è costante)*: **il problema è dei boss con le fasi**.
- ⛔ **MAI `SetHealth(≤ 0)`** — e **la ragione è scritta nel codice due volte**: il commento a **`:5700-5702`** dice che `SetHealth` chiama **`KillInstanteneously()`** quando atterra a ≤ 0, e a **`:5692-5693`** dice che **un boss scritto a zero resta in piedi con la barra vuota e la stanza bloccata** *(parole sue)*. ⇒ **pavimento a 1 HP**, e **il colpo letale continua ad arrivare dal kill/`bdead|`**, che funziona già.
- ⚠️ **E la barra non è strumentata**: `BossHealth`/`HideBossHealth`/`EnableLifeBar`/`LifeBar` = **0 occorrenze nei log** ⇒ **dai log si legge la vita delle COPIE, non il disegno della barra** *(che mostri la copia locale è **prova di codice**, non misura)*.

#### 2.3 ⚠️ **LA FINESTRA DELL'AVVISO** — `DO NOT PRESS` parte dal `Kill()`, la cutscene arriva **≈9-21 s dopo**

- **📊 Le quattro ancore, misurate** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §4.2, rifatte nel `[2026-10-17 23:40]` §1.2)*: avviso `t≈400,1` *(il probe dice `t=400.31`)* ⇒ **scade `t≈404,1`**, perché la durata **non si passa**: la decide la config *(`config.notificationDisplaySeconds` = **4,0 s**, `BossDeathWarning.cs:218` e `Config.cs:18`)* · la cutscene comincia `t≈409,5` *(`G2:3035` `Starting cinematic video`, `G2:3047` `CinematicStarted`)* · il tasto fatale arriva `t≈425` *(`G2:3085` `FADE` acceso)*. ⇒ ⛔ **l'avviso era già sparito ≈5 s prima della cutscene e ≈21 s prima del tasto** *(e la correzione del referto — che diceva «≈15 s» — è **≈21 s**: la sostanza non cambia, **la misura sì**)*.
- ⭐ **Il fix è zero comportamento**: **spostare la finestra dalla morte alla CUTSCENE** *(aprirla a `CinematicStarted`, tenerla fino a `CinematicEnded`, o riaprirla se la schermata di morte è ancora in attesa)*, col testo che dice **la verità** *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §7.2 A)*. ⚠️ **Non rende il guasto impossibile** *(§2.1)*: **toglie la causa nello stesso modo in cui il progetto l'ha già scelto**. ⇒ **è la voce 3 della lista corta, non la prima**, perché **la prima è la finestra dei 1,5 s**, che è la sola con una **soglia misurata**.

#### 2.4 📌 **I 3 BUCHI DEL RELAY** — non passano dal `PoolManager`, e sono **dichiarati**

| # | buco | dove | perché resta fuori |
|---|---|---|---|
| **1** | `BossJumpAttack.InstantiateArea` — i **2 `Object.Instantiate`** degli oggetti del salto | `BossJumpAttack.cs:269-277` | non passa dal `PoolManager` ⇒ **nessun hook lo vede**. Per `ElderBrother` **non è esercitabile** *(`instantiateOnStart`/`instantiateOnEnd` = `<null>`)*; per le Amanecidas **sì** |
| **2** | `BossTeleportAttack.InstantiateArea` — gli oggetti del teletrasporto | `BossTeleportAttack.cs:73-81` | idem. **Nessun boss misurato lo usa** |
| **3** | **`ProjectilePool` privato** | `ProjectilePool.cs:12-33` | è **fuori dal `PoolManager`**: un pool suo, con un suo `Get`. Copre **`BellGhostVariant`, `RangedBoomerang`, `MeltedLady`** — **0 occorrenze nei log** ⇒ **la copertura mancante è un dato STATICO, non una misura** |

- ⚠️ **E restano fuori, per dichiarazione esplicita**: i **~18 `Object.Instantiate` grezzi** nei boss *(Piety 3, Snake 2, Crisanta 2…)*, i **cosmetici** fuori dal filtro *(`CorpseAnimationFx`, `LandingFx`, `CorpsesParticlesJump`)*, **camera e audio**, e **tutto il canale del giocatore** *(`EffectType`: solo **3** membri istanziano un prefab dal pool, e sono tutti cosmetici — `REFERTO-RELAY-EFFETTI.md` §2.2)*.

#### 2.5 📌 **I 23 BOSS** — la mira resta sul padrone *(233 siti)*, e **12 su 17** senza la morte recitata

- **La mira**: `GetTargetPredictedPos` esiste **una volta sola** in tutta `Assembly-CSharp` *(`:56000`)* ed è dell'`ElderBrother` ⇒ ⭐ **gli altri 23 boss «inseguono solo il padrone» ancora oggi**. La famiglia è **233 letture dirette** di `Core.Logic.Penitent` nei namespace `Bosses.*` *(`REFERTO-SALTO-PROXY.md` §5)*: `AmanecidasBehaviour` **50** · `SnakeBehaviour` **25** · `IsidoraBehaviour` **17** · `CrisantaBehaviour` **17** · `PontiffHuskBossBehaviour` **17** · `PontiffOldmanBehaviour` **11** · `WickerWurmBehaviour` **11** · `PontiffGiantBehaviour` **10** · altre 40 classi **75**.
  - ⚠️ **E due conteggi diversi vanno tenuti distinti, perché confonderli fa sbagliare i lavori**: **17** sono le **classi di boss censite per la morte recitata** *(`REFERTO-A-C-MORTE-BOSS.md` §2: **5 dentro**, **12 fuori**)*; **23** sono gli **«altri boss» della famiglia della mira** *(`ElderBrother` + 23)*. Non sono lo stesso insieme e **non si sommano**.
- **La morte recitata**: la whitelist è **5 nomi** *(`EnemySyncManager.cs:378-386`: `ElderBrotherBehaviour`, `LesmesBehaviour`, `QuirceBehaviour`, `PerpetuaBehaviour`, `EsdrasBehaviour`)* ⇒ **12 tipi su 17** restano **senza `Death()` invocata** *(Amanecidas, BurntFace, Crisanta, Isidora, PontiffGiant, PontiffHusk, PontiffOldman, PontiffSword, Snake, SingleAnguish, TresAngustias, WickerWurm)*. **È una scelta scritta, non una dimenticanza**: quei 12 toccano il `Penitent`, l'input o la camera ⇒ *«meglio un AFK noto che un effetto sul giocatore»*.
- **E l'aggio, che è già a posto**: `ApplyAggroTarget` *(`:3998`)* scrive `enemy.Target = _remoteTargetProxy` **per ogni nemico, boss compresi**, quando l'altro è più vicino. Dopo il ② quel bersaglio **è anche feribile** ⇒ *il boss che mira al peer **lo colpisce***; **ma il salto dei 23 boss non legge `Target`** — legge `Core.Logic.Penitent` ⇒ **la mira resta sul padrone** *(§1.2)*.

#### 2.6 📌 **I BUG PICCOLI** — due, e **senza** questi la lettura del rientro va fatta due volte

1. ⚠️ **`scena=` è la scena PRECEDENTE nella 1ª passata di spawn** *(`DIAGNOSI-A-C-E-PUNTO-0.md` §1-bis)*: **7 volte su 7 in `G2`, 6 su 6 in `G1`**. La causa: `_scene = scene;` si assegna **solo** in `LevelLoaded` *(`EnemySyncManager.cs:2358`)*, ma `OnEnemiesSpawned()` è un **postfix** di `EnemySpawner.SpawnEnemiesOnLoad` che gira **prima** di `OnLevelLoaded`; la passata 2 è il **ripiego** in `UpdateCore`. ⇒ **lo stesso oggetto riceve DUE id nello stesso caricamento** *(`en|D17Z01S05|ElderBrother|-806|22` e `en|D17Z01S11|ElderBrother|-806|22`)* ⇒ **la memoria delle morti non combacia** ⇒ **`[BossPending]` = 0** *(e l'id del boss **resta in `_pendingDeaths` per sempre**)*.
2. ⚠️ **Il flag "a stanza" NON esiste nella forma che il codice cerca**: il gioco crea **`D17Z01S11_BOSSDEAD_AC43`**, **non** `D17Z01S11_BOSSDEAD` *(`G1:2544`,`G1:2548`; `G2:2701`,`G2:2705`)*; il nome che `FirstBossDeadFlagOn` *(`EnemySyncManager.cs:816`)* prova **per primo** **non esiste per questa stanza** ⇒ **qui funziona solo il ramo di zona** *(`D17Z01_BOSSDEAD`)*, e **il ramo "a stanza" di C non è mai stato esercitato**.

#### 2.7 ⚠️ **E RESTA ANCHE, FUORI DALLA LISTA CORTA: LA PAUSA DELLA PROPRIETÀ**

- ⛔ **Non è stata toccata in questa tornata**, e va detto perché non è in lista: `git diff cb9fc69 HEAD` **non cambia una riga** di `bossRecovery` *(`EnemySyncManager.cs:4969`)*, `takeNow` *(`:4971`)*, dell'attesa che fora il ramo ② *(`:4830`)* e della cessione *(`:4993`)* ⇒ **la pausa del `[2026-10-17 23:40]` §2.1 resta aperta dov'era** *(6 `[BossProp]` invece di 1-2; due condizioni nel codice)*. **Non è chiusa: semplicemente non è la prima cosa di oggi** — la finestra dei 1,5 s **blocca il respawn**, la pausa **rallenta una fight**.
- ⚠️ **E il revert di F2 (§3.1) la tocca di lato**: F2 **riduceva lo sfarfallio** e **toglieva il ping-pong**, ma rendeva il boss **mono-bersaglio**; il revert **riporta il ping-pong** *(`REFERTO-F2-GUARDIE.md` §1: **82** righe `morto, lo RILascio` contro **1**)*. ⇒ **la pausa e il ping-pong sono lo stesso problema visto da due lati**, e il fix vero è **uno solo**: quello della proprietà.

---

### 3. 💭 LE PROPOSTE VALUTATE — e quelle scartate, con la ragione

#### 3.1 ⛔ **F2 — REVERTATO** *(`d73e9dd`)*

- **Che cosa era**: il **rilascio** del boss da parte del padrone morto usava il **predicato della ripresa** *(`OtherPlayerHereAndAlive()`)*, invece di «c'è qualcuno qui» *(`OtherPlayerInMyScene()`)* — così i due rami **non potevano più contraddirsi** *(`REFERTO-F2-GUARDIE.md` §1-§2)*.
- ⛔ **Perché è stato revertato, con le parole della decisione**: *«riduceva lo sfarfallio ma rendeva il boss **mono-bersaglio** (poco interattivo per l'altro giocatore) ⇒ **scelta di design**: meglio lo sfarfallio»* ✅ *(decisione dell'utente)*.
- **La forma del revert, verificabile**: `git revert --no-commit fcbaf0d` ⇒ **1 file, +1/−42**, l'**inverso esatto** di F2 *(+42/−1)*; nessun conflitto; il file di HEAD è `fcbaf0d^` + `934820d` + nulla *(`git diff fcbaf0d^` dà **255/7**, la statistica **identica** di `934820d`)* ⇒ **niente di `934820d` perso** *(`REFERTO-REVERT-F2.md` §1)*.
- **Cosa torna** *(dichiarato)*: il **ping-pong** quando **entrambi** sono a terra — **le `morto, lo RILascio` multiple** e le accensioni/spegnimenti della tree *(misura di riferimento della run `N_G2`: **82** righe contro **1** nella run vecchia, tree **121/119**)*. **È il numero che l'utente giudica accettabile**, e il prezzo è scritto, non nascosto.
- **Cosa NON cambia**: il **blocco del trigger non torna** *(era `934820d`: collisori del pupazzo spenti in `FreezeCollision`, marker `[BossSolido]`, e `veto SCAVALCATO` quando il flag è acceso — **verificato riga per riga**)*; e **il danno single-target non è colpa di F2** *(`REFERTO-REVERT-F2.md` §5)*.
- 📊 **Build/deploy del revert**: build `Errori: 0`; **267 bersagli** *(F2 non aveva patch Harmony)*; DLL in gioco `sha256 773851178c5297c93eabf230b07d7523199bbe84d474178f15f4887dd4b4f46e`, **381.440 byte**, `mtime 2026-10-18 01:14:45`, e **`receiverCanSimulate` = 0 occorrenze nella DLL** ⇒ **F2 non è nella build** *(§2b-§2c)*.

#### 3.2 ⏸️ **F1 — SCARTATO**

- **La forma**: *«il boss fermo per qualche secondo è **l'attesa del trigger**: quando il giocatore ci arriva, funziona»* ⇒ **funziona, non si tocca** ✅ *(decisione dell'utente)*.
- ⚠️ **E la misura che la sostiene** *(perché questa non è pigrizia)*: la cerimonia è **un evento per macchina**, parte quando **il giocatore** arriva al volume *(misurato: `G1:L1800` a `-816.3`; `G2:L3999` a `-814.5`)*, e **una volta sola**: `BossBlock` = **2 righe per log** *(enabled + disabled = **1** cerimonia)*, su **entrambe** le macchine *(`DIAGNOSI-PAUSA-E-DUE-BARRE.md` §3.1)* ⇒ **il boss che aspetta non è un guasto: è il trigger che deve ancora arrivare.**

#### 3.3 ❌ **IL CENSIMENTO DEI PREFAB DEL PENITENTE — CANCELLATO** *(l'utente è contento così)*

- **Che cos'era**: il censimento che sarebbe servito ad **allargare il relay al giocatore** — l'unico pezzo rimasto fuori *(`REFERTO-RELAY-EFFETTI.md` §2.2: «allargare il catalogo al giocatore è un lavoro **separato**, con la sua misura»)*.
- ❌ **Cancellato**: **l'utente è contento così** ✅ *(decisione dell'utente)*. ⚠️ **E il fatto tecnico che la rende innocua**: il canale del giocatore **esiste già** *(`EffectType`)* e dei suoi membri **solo tre** istanziano un prefab dal pool, **tutti cosmetici** *(SlideDust, DashDust, LandingImpact)* ⇒ **il relay non ne ha bisogno** per fare il suo lavoro *(boss e nemici)*, e il censimento avrebbe comprato **solo cosmetica**.

#### 3.4 ⏸️ **IL GATE SUL MORTO · `RETE 1` · LA TABELLA DEI MORTI · IL TRIGGER INTERCETTATO · IL REPO SEPARATO DEI DOCS** ⇒ *tutte con la loro ragione, **già registrate***

- ⚠️ **Non si riscrivono**: stanno nel `[2026-10-17 23:40]` **§3.1** *(la tabella dei morti)*, **§3.2** *(il trigger intercettato, con **tre prove**)*, **§3.3** *(il gate sul morto)*, **§3.4** *(la `RETE 1` senza timer)*, **§3.5** *(il repo separato dei docs)*.
- ⭐ **L'unica cosa che cambia oggi, e va registrata**: la `RETE 1` era messa da parte **in attesa della triangolazione**. **La triangolazione è arrivata** *(`TRIANGOLAZIONE-SKIP.md`)* e **non ha riabilitato la proposta di per sé**: ha dato **la soglia** *(< 1,5 s)* e **la causa** *(due `Fade` sullo stesso `currentTween`)* ⇒ **la voce non è più "in attesa di un test", è "in attesa di una decisione"** *(§2.1)*.

---

### 4. 📌 E IL PROCESSO *(le cose che evitano di ripetere gli errori)*

> ⚠️ **Le voci di casa sono GIÀ registrate** nel `[2026-10-17 23:40]` §4: **qui non si riscrivono, si CONFERMANO** con la prova di oggi; le altre **sono nuove di questa tornata**.

**⚠️ CINQUE DIAGNOSI HANNO SMENTITO UN'IPOTESI — e la lezione è una sola: *«una catena plausibile non è la catena vera»* ⇒ misurare, non dedurre** ✅

| # | l'ipotesi che sembrava ovvia | cosa ha detto la misura | dove |
|---|---|---|---|
| **1** | **«la cerimonia dipende dalla posizione del boss»**, cioè da una **FSM che guarda dove sta** | **NON è una FSM**: è **il CORPO del boss** che sbarra la strada — il padrone si ferma a `-822.6` contro il boss messo a `-822.0`, e il trigger è a `-816.3` | `STUDIO-PUNTO-0-RESET.md` §A |
| **2** | **«manca il reset del boss al rientro»** | **il reset C'È, e lo fa il gioco**: scena ricaricata, boss **nuovo**, vita **400**, posizione d'autore, `flag=nessuno` ⇒ **eravamo noi a spostarlo** | `STUDIO-PUNTO-0-RESET.md` §B |
| **3** | **«basta una query che cerchi un `Entity`»** per risolvere il padrone di un effetto | **smentita due volte**: sul pool `GetComponentInParent<Entity>()` è **`null`** *(`STUDIO-CANALE-EFFETTI.md:284`)*, e **il relay non legge affatto `WeaponOwner`** ⇒ la query **non serve** | `STUDIO-CANALE-EFFETTI.md` §3.3 · `REFERTO-RELAY-EFFETTI.md` §4 |
| **4** | **«l'area di `ElderBrother` è una `ShockwaveArea`»** | **è un `BossSpawnedAreaAttack`** — **4 fonti indipendenti per macchina**, su **entrambi** i prefab e **entrambe** le macchine | `DIAGNOSI-VOCABOLARIO-EFFETTI.md` §(b) |
| **5** | **«serve una coda di 1 frame»** per leggere il padrone dopo `SetOwner` | **non serve**: il relay risolve il **nome** dal catalogo dei campi del boss, e `ownerSubito` **non si legge** *(la sonda diceva `ownerDopo=boss` 20/20, ma la strada scelta non passa di lì)* | `REFERTO-RELAY-EFFETTI.md` §4 · `REFERTO-SONDA-EFFETTI.md` §5 |

- ⭐ **E la lezione, che vale più delle cinque voci**: **ognuna di quelle ipotesi era plausibile, e tre erano già scritte in un referto**. La cura non è "essere più prudenti": è **avere un marker che parla** *(e ogni volta che il marker non c'era, la diagnosi è costata una run in più)*.
- ⚠️ **E UNA NOSTRA CIFRA CHE ERA FALSA — corretta qui, perché un numero sbagliato fa sbagliare le decisioni**: il referto di `3e52fc9` diceva *«+107 righe, di cui **1 eseguibile**»*. **Il conto vero**: **62 righe di commento, 9 vuote, 10 di sole graffe, 26 di codice**, e **tre** punti in cui il codice nuovo tocca il vecchio *(`:2393` in `LevelLoaded`, `:4264` in `InterpolateRemoteEnemies`, `:5716` nel ricevitore)* — **la sola che decide è `:4264`** *(`DIAGNOSI-MEMORIA-NEMICI-E-AGGIO.md` §(d))*. ⇒ **"1 riga di comportamento" era giusto come effetto, sbagliato come numero**.
- ⚠️ **`verify-deploy.ps1` NON verifica** *(`[2026-10-17 23:40]` §4 voce 3)* ⇒ ⭐ **il sostituto è `scratch\marker-check.ps1`**, e **questa tornata l'ha usato quattro volte di fila**: `3e52fc9` `4310411a…`/382.464 B · `f3084ea` `6db8b575…`/384.512 B · `1d9e4fa` `6df5b556…`/391.680 B · `958a07f` `7cff5ab1…`/404.480 B — **ogni volta sulla DLL IN GIOCO e sul CONTENUTO** *(letterali in UTF-16LE e UTF-8)*, mai sull'hash di un file di deploy.
- ⚠️ **`G1` (`lambe`) va deployata A MANO** *(`tools\deploy-local.ps1:15` ha **una** cartella di gioco come default)*. ⚠️ **E questa tornata l'allineamento è stato PROVATO, non supposto**: nella run del ② **entrambe** le macchine hanno i marker nuovi *(`[DannoPeer]` 9 righe su G1 e 3 su G2; `APPLICATO` 1 su G2; `DIROTTATO` 3 su G1)* ⇒ *«build disallineata»* è **escluso dai dati** *(`DIAGNOSI-TEST-2.md` §0)*. **La regola resta**: chi deploya **dichiari i commit nella build**.
- ⚠️ **E i commenti falsi da correggere** *(sono due, e sono prosa, non comportamento)*: **`ApplyAggroTarget`** dice *«the other player takes damage through the game's local pipeline»* *(`EnemySyncManager.cs:3992`)* — **falso fino a `1d9e4fa`** *(il proxy non era feribile e non esisteva il canale)*, **vero da `1d9e4fa`**; e la giustificazione di `ShouldWaitForPosition` dice *«`_remoteTargets` is emptied by LevelLoaded»* *(`:4781` a HEAD; **`:4676-4681`** at `3e52fc9`)* — **falso dal FIX 51**, perché `_remoteTargets` **non** viene svuotata *(`:2370`)*. ⚠️ **Nessuno dei due è un difetto di comportamento** *(la funzione chiede `ContainsKey`, non si fida del commento)*: sono **la traccia di quanto sia facile, in questo file, credere a una cosa che il codice non fa**.
- ✅ **E il conto dei bersagli, per la terza volta**: **269 a HEAD** *(269 = 269 − 1 sonda + 1 relay)* — un numero che **da solo non prova niente**, ed è per questo che ogni referto porta **il contenuto** della DLL e **i commit** che ci sono dentro.

---

### 5. 📌 LA LISTA CORTA, in fondo

> **Otto voci, in ordine di importanza — ognuna con la ragione per cui sta dove sta.** ⚠️ **La 1 è una DECISIONE** *(e ha una soglia misurata)*, la **2** è **l'unica scrittura sul gioco** della lista, la **3** è **zero comportamento**, la **4-6** sono **lavori dichiarati e confinati**, la **7-8** sono **due righe e la "casa"**.

| # | cosa | perché qui | prova | chiude |
|---|---|---|---|---|
| **1** | ⚠️ **LA FINESTRA DEI 1,5 s** *(decisione: `Respawn()` se `FADE` acceso e `PLAYERDEAD` da > N s)* | **è l'unico guasto che BLOCCA un giocatore**, e ha **una soglia misurata**: `< 1,5 s` prima di `EndCutscene`; **1,3 s blocca, 2,7 s funziona** | `TRIANGOLAZIONE-SKIP.md:403` e `:37` *(finestra ≈1,5 s = **9-15%** del video, **in fondo**)*; `FadeWidget.cs:48-72`, `:190-222`, `:212-215`; `DeadScreenWidget.cs:87-96`; `DIAGNOSI-REGRESSIONE-RIENTRO.md` §2.1 *(`FADE` acceso e mai spento, **131,81 s**)* | **il ritorno in vita perso** *(e con lui la macchina bloccata)* |
| **2** | ⚠️ **LE DUE BARRE** *(la vita del boss + le fasi, **stato + disegno insieme**)* | **110 HP = 27,5%** di scarto **costanti** ⇒ **due boss diversi nella stessa stanza**; e le fasi **non si aggiornano** quando si scrive la vita *(`SetHealth` non passa da `Damage()`)* | `DIAGNOSI-PAUSA-E-DUE-BARRE.md` §4.2-§4.4 *(`G1:2421` `400` contro `G2:2164` `290`)*; `EnemySyncManager.cs:5692-5705`; ⛔ **mai ≤ 0** *(`:5702`, `:5692-5693`)*; la barra legge `Life.Current` ogni frame *(`BossHealth.cs:31-40`)* | le due barre, la fase giusta, e il "boss che è due boss" |
| **3** | ⭐ **LA FINESTRA DELL'AVVISO** *(dalla morte alla cutscene)* | **zero comportamento**, costo minimo, e il difetto è **misurato in secondi**: l'avviso scade **≈5 s prima** della cutscene e **≈21 s prima** del tasto fatale | `DIAGNOSI-REGRESSIONE-RIENTRO.md` §4.2 *(`t≈400,1` → scade `404,1`; cutscene `409,5`; tasto `425`)*; `BossDeathWarning.cs:218` *(4,0 s)* | ⚠️ **non rende il guasto impossibile**: **toglie la causa** *(§2.3)* |
| **4** | 📌 **I 3 BUCHI DEL RELAY** *(2 `InstantiateArea` + il `ProjectilePool` privato)* | **sono confinati e dichiarati**: non passano dal `PoolManager`, quindi **nessun hook li vede** — ma **il buco 1 non è esercitabile su `ElderBrother`** e **le 3 famiglie di proiettili non sono mai comparse nei log** | `REFERTO-RELAY-EFFETTI.md` §6 *(`BossJumpAttack.cs:269-277`, `BossTeleportAttack.cs:73-81`, `ProjectilePool.cs:12-33`)* | gli effetti di salto, teletrasporto e **3 famiglie di proiettili** |
| **5** | 📌 **I 23 BOSS** *(la mira sul padrone: **233 siti**)* — e **12 su 17** senza la morte recitata | **un lavoro per boss**, con la **stessa forma** già provata su `ElderBrother`; e i **12** sono una **scelta scritta**, non una falla | `REFERTO-SALTO-PROXY.md` §5 *(tabella **233**)*; `EnemySyncManager.cs:378-386` *(whitelist **5**)*; `REFERTO-A-C-MORTE-BOSS.md` §2 *(**17** tipi, **12** fuori)* | il boss che **salta sempre sull'host**; e l'AFK dichiarato dei 12 |
| **6** | 📌 **I BUG PICCOLI** *(il `scena=` in 1ª passata · il flag "a stanza")* | **sono due righe di log e un predicato**, ma **senza** la prima ogni rientro va letto **due volte**, e **senza** la seconda **C si regge sul solo flag di zona** | §2.6 *(`7/7` e `6/6`; `en\|D17Z01S05\|…` contro `en\|D17Z01S11\|…`; `D17Z01S11_BOSSDEAD_AC43` contro `…_BOSSDEAD`; `FirstBossDeadFlagOn` `:816`)* | la leggibilità del punto 0 — e **`[BossPending]`**, che oggi è **disinnescato** |
| **7** | 📌 **LE CORREZIONI AI COMMENTI** *(`ApplyAggroTarget` · il commento di `_remoteTargets`)* | **due righe di prosa**, ma sono **esattamente** il punto in cui il prossimo lettore crede a una cosa falsa *(`:3992` e `:4781`)* | §4 ultima voce *(`:4676-4681` at `3e52fc9`)* | il prossimo "perché il peer non prende danno?" che riparte da zero |
| **8** | 📌 **CASA** *(`G1` a mano · `verify-deploy` → `marker-check`)* | **non è codice, è l'attrezzatura**: senza, **una run misura la build sbagliata** — ed è già successo **tre volte in due giorni** | `tools\deploy-local.ps1:15` *(una sola cartella di gioco)*; `scratch\marker-check.ps1` e i **4 sha256** di questa tornata *(§4)*; `DIAGNOSI-TEST-2.md` §0 *(l'allineamento **provato**)* | il fraintendimento "sembra una regressione" |

⚠️ **E la cosa che la lista NON contiene, di proposito**: **il trigger/la cerimonia** *(§3.2 di ieri, con tre prove)*, **la `RETE 1` senza timer** *(§3.4 di ieri: oggi è **una decisione**, ed è la voce 1 nella sua forma minima)*, **il gate sul morto** *(§3.3)*, **la tabella dei morti** *(§3.1)*, **il censimento dei prefab del Penitente** *(§3.3: cancellato)*, **F2** *(§3.1: revertato)*. **Non perché siano sbagliati, ma perché sono stati scartati o messi da parte con una ragione scritta.**

---

### 6. ⚠️ COSA **NON** È VERIFICATO *(dichiarato, non omesso)*

1. ⚠️ **Il relay non ha un log di run.** `CONFERMA-RELAY.md` **non esiste**, e `958a07f` è l'ultimo commit: gli effetti visti **da entrambi** sono **la parola dell'utente** *(✅, e vale come verifica)*, ma **non c'è un referto con `mando`/`istanzio`/`scartatiPerDedup`** da confrontare. ⇒ **il dedup, il catalogo e il freno sono provati nella DLL e nel codice, non in una sessione.**
2. ⚠️ **Che la copia relayata FERISCA il `Penitent` del ricevente**: provato **nel codice** *(`REFERTO-RELAY-EFFETTI.md` §4)*, **non misurato**: manca una riga di log che dica `vitaPrima`/`vitaDopo` di un'area relayata *(e il ② **non** la scriverà, perché scarta le aree per progetto)*.
3. ⚠️ **I 3 buchi**: la copertura mancante è **statica** *(codice)*; **0 occorrenze** di `BellGhostVariant`/`RangedBoomerang`/`MeltedLady` nei log ⇒ **non sappiamo quanto pesino**.
4. ⚠️ **Gli altri 23 boss**: la mira sul padrone è **provata per `ElderBrother`**; per gli altri è **il conteggio dei 233 siti**, non una run.
5. ⚠️ **I 12 tipi fuori whitelist**: la **tabella** è verificata tipo per tipo sul decompilato *(`REFERTO-A-C-MORTE-BOSS.md` §2)*, ma **l'esecuzione** è stata provata **solo su `ElderBrother`**.
6. ⚠️ **La finestra dei 1,5 s**: misurata su **due sessioni diverse** *(O_G2 e N_G2)*, con **timestamp stimati** *(i log del client non hanno un timestamp per riga)*, e **la periferica non è nei log** ⇒ **la soglia è solida, i margini no**.
7. ⚠️ **Il punto 0**: la prova nei log è **l'assenza del trapianto** *(`[Insegue]` 5 righe)*, non **l'ingresso del boss**: nei quattro log `[Insegue]` **non porta un hash** ⇒ il binario è **dedotto dai marker** e dall'aritmetica di `_sceneVisit` *(«visita 3 / siamo alla 5», identico in tutti e quattro)*, **non da un'impronta per-log**.
8. ⚠️ **La memoria che cresce**: `_remoteTargets` e `_remoteTargetsVisit` **non vengono mai svuotate** *(`:2370`)*, si aggiungono una voce per id visto in sessione, e **la dimensione reale dopo una sessione lunga non è misurata**.
9. ⚠️ **E una nota di metodo, che vale per tutto quanto sopra**: **`[Insegue]` deduplica per boss e per visita**, quindi **un secondo trapianto rifiutato nella stessa visita non scrive niente** ⇒ *contare le righe non conta i rifiuti*. Il numero giusto è **uno per boss per visita**, e va letto così.

---

## [2026-10-18 05:40] ⭐⭐ **LA CONFERMA DEL RELAY È ARRIVATA** — **88 su 88**, appaiamento perfetto, e **la prova che il danno viene dalle copie relayate**; restano **l'importo del danno** *(senza marker)*, **il doppione a ~14 unità** e **i proiettili** *(assenti: assenza, non risposta)*

**Referto** *(la fonte)*: `scratch\CONFERMA-RELAY.md` — **367 righe**, dai due log `output_log G1.txt` *(2355 righe / 229255 byte)* e `output_log G2.txt` *(2015 righe / 200622 byte)*. **HEAD di questa tornata: `f06fc61`** *(`git rev-parse HEAD`)*, branch `fix-f1a`, albero pulito.

⚠️ **E QUESTA SEZIONE NON CORREGGE QUELLA QUI SOPRA** *(la regola del file: **non si riscrive e non si compatta**)*: la frase *«`CONFERMA-RELAY.md` non esiste»* del §6 punto 1 **era vera quando è stata scritta** — il referto **è arrivato dopo**. È l'arte di un TODO cronologico: **le sezioni vecchie si leggono come storia, non come stato**.

### 1. ⭐ I NUMERI: **88 · 88 · 0 · 0 · 0**

| | G1 (host) | G2 (client) | totale |
|---|---|---|---|
| **mandati** | 72 | 16 | **88** |
| **istanziati** | 16 | 72 | **88** |
| **scartati per dedup** | 0 | 0 | **0** |
| **non risolti** | 0 | 0 | **0** |
| **frenati** | 0 | 0 | **0** |
| catalogo | 8 | 8 | — |

⭐ **L'appaiamento è perfetto**: `Compare-Object` fra i **72** `mando` di G1 e i **72** `istanzio … da=G1` di G2 dà **0 differenze**, e i **16** di G2 contro i **16** di G1 danno **0 differenze** *(chiave `prefab@x,y` esatta, non arrotondata)*. ⇒ **nessun effetto perso, nessuno duplicato, nessuno inventato.**

### 2. ⭐⭐⭐ LA PROVA DEL DANNO DEGLI EFFETTI

**Il gioco emette `event:/SFX/Penitent/Damage/PenitentMagicDamage` quando un Penitente SUBISCE danno magico** *(le altre due famiglie sono `SimpleEnemyDamage` e `HeavyEnemyDamage`; `…Hit` è invece il colpo **portato dal giocatore**)*.

- ⭐ **G2 non ha generato NESSUN effetto proprio prima della riga 1546** *(1546 è il **primo** `mando` di G2: fino lì nessuna chiamata al suo pool)*
- ⭐ e **tutte e 5** le sue `MagicDamage` *(righe 997 · 1093 · 1161 · 1275 · 1370)* stanno **0-4 righe dopo un `istanzio`**
- ⇒ ⭐ **in quei cinque istanti le uniche aree del boss nel mondo di G2 erano le copie relayate** ⇒ **il danno viene dal relay**: il canale porta l'effetto **e** l'effetto ferisce ✅
- ✅ **e su G1, specularmente**: le sue **3 su 3** `MagicDamage` stanno **1-4 righe dopo un `mando`** *(le copie locali)*
- ⚠️ **il limite, dichiarato e non nascosto**: l'evento audio prova che **un** Penitente su quella macchina ha preso danno magico — **non dice quale** (vedi §3)

### 3. ⚠️ LA LACUNA CHE RESTA: **l'importo del danno NON è documentato**

- **176 PV persi in due** *(88 + 88: entrambi i giocatori morti a `vita=0/88`)*, di cui **solo 10** hanno una riga: `G2:1135 [DannoPeer] APPLICATO … vitaPrima=54 vitaDopo=44 perso=10` — e quei 10 vengono dal **canale NEMICO**, non dal relay
- **Per la strada dell'effetto non esiste alcun marker di vita**: `[DannoPeer]` si ferma a `non inoltrato`, e `[Danno]` riguarda la vita del **boss** *(non del Penitente)*
- ⇒ ⭐ **serve un marker**: *la riga `vitaPrima=`/`vitaDopo=` anche per un attaccante **effetto*** *(la stessa riga che `[DannoPeer] APPLICATO` già stampa per il canale nemico)*
- ⚠️ **e serve anche perché**: il proxy **`Penitent(Clone)`** sta a **1,1 unità** dal locale *(`G2:1006` → `posMia=(-812,3 · 10,0)`, `posAltro=(-813,4 · 10,0)`)* ⇒ **la geometria non discrimina** quale dei due ha perso vita

### 4. ⚠️ E IL DOPPIONE VISIVO *(trovato)*

- In **2 finestre** entrambe le macchine generano la **stessa raffica**, ma a **posizioni specchiate e ~14 unità distanti** *(y = 13,1 e y = 15,3; centri x ≈ -801 su G1 e x ≈ -815 su G2)* ⇒ **chiavi diverse** ⇒ **8 → 16 effetti** per finestra: il relay **somma** la copia invece di sostituirla
- 🎯 **e la domanda che ne nasce, da mettere come voce**: **perché le due macchine posizionano lo stesso attacco a ~14 unità di distanza?** ⭐ *(è a **monte** del dedup: nessuna finestra di tempo lo salverebbe)*

### 5. ✅ E IL DEDUP **NON È ROTTO: È NON ESERCITATO**

`scartatiPerDedup=0` **mentre** `istanziati == mandati` è la combinazione che il referto del relay indica come sospetta. **Il sospetto qui non regge**, e il conto lo dice: la coppia di arrivi della stessa chiave **più stretta** dista **≈4 s** *(misurata col metronomo interno di `[Probe]`, che batte ogni 2,01 s — calibrazione verificata su `[DeathScreen] misura t=`)* contro una **finestra di 0,5 s** ⇒ **8 volte la finestra**. ⇒ **`0` è il risultato corretto**, ma **la strada di rigetto non è mai stata percorsa**: questa run **non dimostra che il dedup scarti**.

### 6. ⛔ I PROIETTILI: **ASSENZA, NON RISPOSTA**

`Bullet` · `Projectile` · `Crawler` · `Cloistered` · `Boomerang` · `MeltedLady` · `BellGhost` · `ProjectilePool` ⇒ **0 su entrambe le macchine**. I due prefab relayati sono **solo** `CorpsesShockwave` *(66)* e `MaceImpact` *(22)*, aree del boss, e il catalogo della scena **(8 nomi)** non contiene proiettili: **`ElderBrother` non spara**. ⇒ **sui proiettili non si conclude niente**: serve una run con un boss che spara.

### 7. ✅ E IL RELAY **NON HA ROTTO NIENTE**

| | G1 | G2 |
|---|---|---|
| `exceptions=` | **0** su **39/39** campioni | **0** su **42/42** campioni |
| `fps` medio | **60,1** | **59,0** |
| `lateMs_max` **dentro la finestra del relay** | **15,7 ms** | **8,4 ms** |

⭐ **I due picchi globali di latenza stanno PRIMA del primo evento del relay** *(`G1:631` e `G2:692`, contro `G1:980` e `G2:994`)*. ⇒ il canale **accoda, non blocca**; e **nessun errore dei log è del relay** *(l'`IMPRONTA DIVERSA` e il `NullReferenceException` di `CorpseFollow` sono due difetti preesistenti e dichiarati)*.

### 8. 📌 LE DUE VOCI CHE NE NASCONO *(proposte qui, non aggiunte alla lista corta del §5 qui sopra: quella sezione non si tocca)*

1. ⚠️ **IL MARKER DELLA VITA PER GLI ATTACCANTI NON-NEMICI** — la riga `vitaPrima=`/`vitaDopo=` quando il danno arriva da un **effetto**: è **l'unico modo** per avere l'**importo**, e senza di esso la catena resta provata **fino al danno**, non **fino ai PV**.
2. 🎯 **LA GEOMETRIA DELLO STESSO ATTACCO SU DUE MACCHINE** — perché la stessa raffica nasce a **~14 unità** di distanza *(mentre le due pedine stanno a **1,1 unità**)*: è **a monte** del dedup, e finché non si risponde **il doppione resta invisibile al relay**.

⚠️ **Una nota di metodo**: i log **non hanno un orologio** *(zero righe `HH:MM:SS`)*, quindi gli istanti del §2 sono **adiacenze di riga** *(fortissime: 1-4 righe)*, **non misure in secondi**; e il tempo del §5 è **interpolato** fra due campioni a 2,01 s.

---

## [2026-10-18 06:00] ⭐⭐ **LA DECISIONE È PRESA: SULLA FINESTRA DEI 1,5 s NON SI FA NIENTE** — il timer è **scartato** *(farebbe saltare una cutscene: **lo stesso danno**)*, **la via d'uscita c'è già** *(entrare e uscire)*, e il difetto **vive in `BUG-NOTI` voce 6**

**Referto** *(la fonte)*: `docs\BUG-NOTI.md` **voce 6** — *la misura intera, le firme del log e il marker che manca*; a monte `scratch\TRIANGOLAZIONE-SKIP.md` *(la soglia)* e `scratch\DIAGNOSI-REGRESSIONE-RIENTRO.md` *(il meccanismo)*. **HEAD di questa tornata: `a98ad2b`**, branch `fix-f1a`.

⚠️ **E QUESTA SEZIONE NON CORREGGE QUELLE QUI SOPRA** *(la regola del file: **non si riscrive e non si compatta**)*: `:6801` *(`§2.1`)*, `:6907` *(la lista corta, **voce 1**)* e `:6874` *(«non è più "in attesa di un test", è "in attesa di una decisione"»)* **erano vere quando sono state scritte** — **la decisione non c'era.** Restano **come storia**; le sezioni nuove valgono **come stato**.

### 1. ⭐🛑 LA DECISIONE, in una riga

**Sulla finestra dei 1,5 s non si fa niente.** Non «in attesa di una decisione», non rimandata a una tornata: **scartata per decisione**, e il difetto **resta noto e accettato**.

### 2. 🔴 PERCHÉ NON SI FA — e non è prudenza: è **lo stesso danno**

- **La sola cura proposta** era **una finestra con `Respawn()` automatico** *(quando `FADE` è acceso e `PLAYERDEAD` dura da più di *N* secondi)* — ed è **esattamente l'opzione 1 della voce 1** della lista corta (`:6907`).
- ⚠️ **E il `Respawn()` che scatta mentre la scena del boss è in corso la cancella**: può **far saltare una cutscene** ⇒ sarebbe **lo stesso danno** che il progetto evita da giorni *(il fatto è già misurato nella voce 5, `docs\TODO.md:6191`)*.
- ⇒ ⭐ **Sarebbe curare un danno con lo stesso danno.** **SCARTATO per decisione** — e **non** «da valutare».

### 3. ✅ PERCHÉ SI PUÒ — la via d'uscita **esiste già**

- ⭐ **Entrare e uscire**: un **cambio di livello** — *il load è l'unico punto in cui il gioco rimette a posto da solo `ResetManager()`, il `FADE` e `DEAD_SCREEN`, come nella voce 5* — **resetta il fade** ⇒ il giocatore **torna comandabile**.
- 📌 **Il comportamento è riportato dall'utente; il meccanismo è quello che il gioco usa già** *(non una mitigazione della mod)*. ⇒ **il costo di questa voce è basso: c'è una via d'uscita e non richiede un fix.**

### 4. ⚠️ PERCHÉ È ACCETTABILE — **raro, e non a comando**

- ⚠️ **Non riproducibile a comando**: l'utente **ci ha provato più volte** e **non ci riesce**.
- ⭐ **E non è una combinazione di tasti: è un confine al decimo di secondo** — **≈1,3 s ⇒ blocca**, **≈2,7 s ⇒ funziona**, **stesso binario** *(la finestra è ≈1,5 s, il **9-15%** del video, **in fondo**: proprio dove si preme per saltare)*.
- ⇒ **Un difetto così non si insegue con un timer a caso**: si **documenta** *(voce 6)* e si lascia **dov'è**.

### 5. 📌 DOVE VIVE ORA

- **`docs\BUG-NOTI.md` voce 6** — *«Lo skip della cutscene del boss uccide il ritorno in vita»*, titolo **RARO · NON RIPRODUCIBILE · RECUPERABILE** *(PREZZO NOTO, non un difetto in lavorazione)*: sintomo, causa *(`DeadScreenWidget.cs:87-96` → `Fade(1.5 s, → Respawn())` contro `Cinematics.EndCutscene:154-190` → `FadeInOut`; **un solo** `currentTween`, `FadeWidget.cs:48-72`, `:190-222`; `CHERUB_RESPAWN` che blocca il rilascio del `FADE`, `:212-215`)*, la tabella delle due run, le **firme del log** e il contatore `Active: True` **6** / `False` **5** *(16/16 nella run sana)*.
- ✅ **Ed è il posto giusto**: `BUG-NOTI` si aggiorna **solo su richiesta esplicita**, e la richiesta è arrivata. ⛔ **Il codice non è stato toccato**: qui **non c'è un fix da fare**, c'è **uno stato da scrivere**.

### 6. 📌 E IL MARKER CHE SERVIREBBE, se un giorno ricapita

**Una riga che dica quando il fade è in volo e quando arriva `EndCutscene`.** ⚠️ **Oggi non esiste**: `Called fade function … Active: True` è un marker **d'avvio**, **non** di tween ucciso *(nella finestra n.4 sta **1 frame** dopo il `FADE` acceso, mentre il `Respawn()` arriva **1,4 s** dopo)* ⇒ senza quella riga **un log non distingue** «il tween è stato ucciso» da «il tasto non è arrivato». **Con quella riga**, lo stesso caso si diagnostica da un log invece che da una ricostruzione.

### 7. ⚠️ NOTA PER IL LETTORE

- **`:6801` · `:6907` · `:6874` restano come storia** *(erano vere quando sono state scritte: la decisione non c'era)* ⇒ ⭐ **le nuove sezioni valgono come stato**.
- **Che cosa cambia, in pratica**: **niente nel codice, tutto nelle priorità** — la finestra dei 1,5 s **non è più la voce 1 di una lista aperta**: è **una voce chiusa in `BUG-NOTI`**, con la sua via d'uscita e il suo marker da aggiungere **se ricapita**.
- ⚠️ **La data di questa sezione** segue la serie del file *(`:6719` = `04:20` → `:6934` = `05:40`)*: è **la successiva**, non l'orologio della macchina *(che segnava le `04:28` quando è stata scritta)*. **Il nome della sezione non è una misura**: è un'etichetta.

---

## [2026-10-18 07:00] ⭐⭐ **LA "STRADA DI RISOLUZIONE" DEL RELAY — una strada più larga, non tre hook** — perché il ricevente non sa cos'è un proiettile, **la sonda da fare PRIMA** del fix, e **i 2 marker che restano alla voce 56** *(da chiudere insieme, ma **non oggi**)*

**Fonti**: `docs\BUG-NOTI.md` **voce 7** *(i tre buchi, e la correzione alla fonte)* · `scratch\REFERTO-RELAY-EFFETTI.md` *(il canale, la strada 1 misurata, la strada 3 scartata)* · `scratch\DIAGNOSI-VOCABOLARIO-EFFETTI.md` *(le tre strade a confronto, 53 nomi su 53)* · `scratch\BUGNOTI-3BUCHI-E-23BOSS.md` *(⭐ la correzione importante: **il relay VEDE anche i proiettili dei 3 nemici**)*. **HEAD verificato: `8978107`** *(`git rev-parse HEAD`)* — il mandato indicava `00b9c94`, che è **il commit precedente** *(`2026-10-18 15:02` contro `15:00`)*; branch `fix-f1a`, albero pulito.

⚠️ **E QUESTA SEZIONE NON CORREGGE QUELLE QUI SOPRA** *(`:6719` · `:6934` · `:7002`)* **e non tocca `docs\BUG-NOTI.md`**: la regola del file è **non si riscrive e non si compatta**, quindi qui si **aggiunge**, in fondo, e si **rimanda**. **Nessuna voce è dichiarata chiusa qui sotto** *(in particolare **non la 56**: vedi §4)*.

### 1. ⭐⭐ LA "STRADA DI RISOLUZIONE" — **il problema**, in una riga e poi per esteso

**In una riga**: 🔴 **il relay manda il *nome* del prefab + posizione + rotazione, e il ricevente deve *trovare il prefab* per istanziare la sua copia — ma oggi lo cerca SOLO nei campi del boss.**

- ✅ **Il mittente non decide niente di semantico**: manda *`prefab (nome) + posizione + rotazione`* per **ogni** istanziazione che passa dal `PoolManager` *(un solo `Postfix`, `EffectRelay.cs:54-67`; il perché è scritto in testa al file, `:33-41`)*.
- ⚠️ **Il ricevente invece deve risolvere** *nome → riferimento*, perché **i prefab non sono in `Resources`**: sono **referenze nei campi serializzati del codice** *(è la ragione per cui la strada 1 esiste, `:596-604`)*.
- ⛔ **E oggi c'è UNA sola domanda**: *«il nome è un effetto del **mio** boss?»* — `Resolve(name)` *(`:605-623`)* consulta `_catalog` *(`:160-161`)*, che è costruito **solo** dal boss: `BuildCatalog` *(`:625-655`)* → `FindBosses` *(`:685-728`)* filtra gli `Enemy` del namespace `Gameplay.GameControllers.Bosses` *(`:730-735`)* → `HarvestBoss` *(`:743-851`)* riflette sui componenti del boss e segue i riferimenti `EnemyAttack` per **due passi**.
- ✅ **Funziona per gli effetti del boss**, ed è **misurato**: **5 nomi su 5** del vocabolario di `ElderBrother` risolti dai campi della copia locale, su entrambe le macchine *(`[FxRisolvi] boss=si(campo=…)`)*; nella run di conferma il catalogo era **8 nomi** e il relay ha chiuso **88 mandati / 88 istanziati / 0 non risolti** *(`:6940-6951`)*.
- ⛔ **NON funziona per altre due famiglie**:
  - **i proiettili dei nemici comuni** — `BellGhostVariant`, `MeltedLady`, `RangedBoomerang`: il prefab sta nei campi **del nemico**, non del boss;
  - **gli effetti del Penitente** *(`RangeAttackProjectile`, `ChargedAttackProjectile`, `PenitentFlamePillar`…)*: il prefab sta **nel suo** codice, e il Penitente **non è un `Enemy`** ⇒ non entra in `FindBosses`.
- 📋 **`[FxRelay] non risolto`** *(`:472-488`)* = *«l'ho ricevuto, ma non so cos'è»*: una riga per nome per scena, il nome, il mittente e il numero di nomi del catalogo. Non è un errore: è **la copia che non nasce**.

⭐ **E la correzione che cambia il fix** *(da `BUG-NOTI` voce 7, §"Una correzione alla fonte")*: **il canale VEDE anche i proiettili dei 3 nemici.** `BellGhostVariantAttack.cs:39`, `MeltedLadyAttack.cs:40`, `RangedBoomerangAttack.cs:40` chiamano `PoolManager.Instance.ReuseObject(projectilePrefab, …)`; il `ProjectilePool` privato **non è usato da nessuno** *(0 chiamate a `Spawn` in tutta `Assembly-CSharp`)* ⇒ il mittente **manda** quei proiettili, e a mancare è **solo la risoluzione** sul ricevente. ⇒ **il buco non è nel canale: è nel catalogo.**

### 2. ⭐⭐ IL FIX — **una strada più larga, non tre hook**

**La forma**: ⭐ **una sola "strada di risoluzione"** che cerca in **più sorgenti, in ordine** — e **si ferma alla prima che risponde**:

| # | sorgente | cosa copre | stato |
|---|---|---|---|
| ① | **i campi del boss** | gli effetti del boss *(`CorpsesShockwave`, `MaceImpact`, …)* | ✅ **già c'è**, ed è misurato 5/5 |
| ② | **i campi dei nemici** | **i proiettili** *(`BellGhostVariant`, `MeltedLady`, `RangedBoomerang`)* | ⛔ da aggiungere |
| ③ | **i campi del Penitente** | **gli effetti del giocatore** | ⛔ da aggiungere |
| ④ | ⚠️ **una cache per nome** | il costo delle tre precedenti | ⛔ da aggiungere *(la forma c'è già: `_catalog` è un `Dictionary<string, GameObject>`, `:160-161`)* |

- ⭐ **Un fix → copre i 3 buchi E il Penitente.** Le sorgenti ② e ③ **non sono hook nuovi**: `HarvestBoss(Enemy boss)` *(`:743`)* non usa **nient'altro che `boss.gameObject`** *(`:751`)* e i suoi componenti ⇒ **lo stesso camminatore**, applicato a **più radici**. *(Le altre due famiglie di `BUG-NOTI` voce 7 — i due `InstantiateArea` — restano lavoro meccanico a parte: **quelli non passano dal pool**, quindi nessun catalogo li può vedere.)*
- ⛔ **E non tre hook**, per la ragione già scritta in `BUG-NOTI` voce 7: un hook sul `ProjectilePool` sarebbe **un hook su codice che il gioco non esegue**.
- ✅ **La forma del pacchetto non cambia**: il mittente continua a mandare `nome + posizione + rotazione`, il ricevente risolve più largamente, il dedup *(`:490-510`, chiave `prefab@x,y` arrotondata + finestra 0,5 s)* resta **com'è**.

### 3. ⚠️ IL RISCHIO, E IL METODO — **prima la sonda, poi il fix**

- ⚠️ **"Trovare il prefab da un nome" è l'unica cosa che il progetto non è mai riuscito a provare in generale.** La strada 3 — `Resources.FindObjectsOfTypeAll` — risponde `resources=si` su **53 nomi su 53** e sarebbe **la trappola perfetta**: per `LandingFx` restituisce `asset=no`, cioè ha trovato **l'istanza di scena (la copia dentro il pool), non il prefab** *(su 2 nomi su 53; `DIAGNOSI-VOCABOLARIO-EFFETTI.md` §4.1, §4.3)* ⇒ **un catalogo che sembra completo e ha un buco silenzioso**. Per questo `Resolve` **non ripiega su `Resources`** *(`:602-604`)*.
- ⚠️ **La ricerca larga è lenta**: il camminatore ha un tetto di **96 componenti per radice** *(`MAX_BOSS_COMPONENTS`, `:148`)* e segue i riferimenti `EnemyAttack` per due passi; su **un** boss costa, su **tutti i nemici di una stanza + il Penitente** costa molto di più — e `Resolve` è chiamata **a ogni pacchetto ricevuto**. ⇒ ⭐ **serve la cache** *(e c'è già il posto: `_catalog`, `:160`)*; il ritmo attuale è **2 s** a catalogo vuoto e **10 s** a catalogo pieno *(`:134-137`, `:260`)*, **da rimisurare** con la strada larga.
- ⚠️ **E va provata**, per un motivo che non è eleganza: se un nome non si risolve, **l'effetto si perde — silenziosamente**. Oggi il silenzio è dichiarato *(`non risolto`)*; con una strada larga il rischio è **peggiore**, perché un catalogo più grande *sembra* aver coperto tutto *(è esattamente l'errore che `LandingFx` insegna)*.
- 📌 **QUINDI, PRIMA DI TUTTO: una SONDA** che dica **quali nomi si risolvono, e da dove**. ⭐ **Solo dopo, il fix.** È lo stesso metodo che ha funzionato per il canale: **misurare il vocabolario, non indovinarlo** *(`REFERTO-SONDA-EFFETTI.md` → `DIAGNOSI-VOCABOLARIO-EFFETTI.md`: si è partiti da «quale strada?» e si è arrivati a «la 1, 5/5»)*.

**La sonda, nella forma minima** *(osservazione pura: nessuna decisione, nessun istanziato in più)*:

```
[FxSonda] nome="RangeAttackProjectile" boss=no nemici=si(campo=EnemyAttack.areaPrefab) penitente=si(campo=...) cache=no ms=1,8
[FxSonda] riepilogo scena=… nomi=K boss=A nemici=B penitente=C nessuno=D msMax=… msTot=…
```

- **Le righe che contano sono tre**: *(a)* un nome che **oggi** è `non risolto` e **domani** si risolve da ② o ③ → **il fix ha un caso reale**; *(b)* un nome che **non si risolve da nessuna sorgente** → **quel nome va dichiarato perso**, non nascosto; *(c)* il **costo** (`ms`) per nome e per scena → è il numero che decide **se la cache basta**.
- **Da fare nella stessa scena dei buchi** *(un boss che spara, o i tre nemici)*: la sonda **non inventa** i nomi, li prende dai `[FxRelay] non risolto` e dai `mando` dei log già raccolti.

### 4. ⭐ I **DUE MARKER DELLA VOCE 56** — *da chiudere insieme*, e il pacchetto è di **tre** marker

⚠️ **La voce 56 ha ancora aperto** *«2 marker mancanti (i conteggi del peer)»*: la cella in tabella *(`docs\TODO.md:103`)* la dà **CHIUSA dall'audit del 2026-10-12**, ma **la prova elencata copre i marker 1, 2, 4 e 5** *(il commit stesso si chiama «MARKER 1, 2, 4, 5 — i quattro locali»)*, e il **(3) — *i conteggi del PEER* — è elencato senza prova**. Il resto del file lo dice ancora aperto: `:4795` parla di *«due marker che oggi mancano»* — e, verificato sul codice, **uno dei due c'è** *(`ReportResolveFailure`, `EnemySyncManager.cs:1365-1379`, chiamata a `:5845`)*, mentre **l'altro è il (3)**. **E il codice di oggi conferma**: `IMPRONTA DIVERSA` *(`EnemySyncManager.cs:5835-5839`)* stampa `host=<fingerprint> locale=<fingerprint>` e i conteggi **solo locali** *(`spawn point locali`, `id locali`)*, perché **sul filo viaggia solo l'`int` dell'impronta** *(`NetworkManager.cs:582-591`, `:605-609`)*. ⇒ ⭐ **i "2 marker mancanti" sono i due pezzi del (3)**: *i conteggi del peer* e *il primo id presente su un lato solo*.

**E siamo nel punto giusto per farli**, perché è **la stessa zona del marker dell'importo del danno** *(che serve perché: **176 PV persi, solo 10 documentati** — `:6940-6968` — e il proxy sta a **1,1 unità** dal locale ⇒ **la geometria non dice chi ha perso vita**)*.

**📌 La lista — tre marker, zero comportamento** *(nessuno dei tre cambia una decisione: due sono righe di log, uno ha bisogno di pochi byte di osservazione sul filo; niente li legge per decidere)*:

| # | marker | cosa misura | perché manca oggi | dove |
|---|---|---|---|---|
| **A** | **l'importo del danno** — `vitaPrima=` / `vitaDopo=` / `perso=` | **quanti PV** ha perso il `Penitent` **quando l'attaccante NON è un nemico** *(l'area relayata, la trappola, il proiettile)* | `[DannoPeer] APPLICATO` **esiste ma solo per il canale nemico** *(`EnemyHitPeerSync.cs:555-560`)*; per gli altri si ferma a `non inoltrato` *(`:571`, throttled `:580-592`)*, e `[Danno]` misura la vita del **boss** | dove il danno si applica al `Penitent` *(la stessa `PenitentDamageArea.TakeDamage` che il codice cita a `EnemySyncManager.cs:1337`)* |
| **B** | **i conteggi del peer** *(voce 56, marker 3a)* | quante `spawn point` e quanti `id` ha **l'altro**, accanto ai propri | **non viaggiano**: il pacchetto porta solo `fingerprint` *(`NetworkManager.cs:591`)* | la riga `IMPRONTA DIVERSA` *(`:5835`)* |
| **C** | **il primo id presente su un lato solo** *(voce 56, marker 3b)* | **quale** id diverge, non solo **che** diverge | come B: senza la lista del peer non si può dire **quale** | idem *(o l'uscita del `multiplayer sync dump` già esistente, `:1207`)* |

- ⚠️ **Onestà sul costo**: **A** è **una riga di log** e basta. **B** e **C** hanno bisogno che **qualche numero del peer** arrivi *(due `int` in più sul pacchetto che esiste già, oppure una richiesta/risposta sul `dump`)*: è **ancora osservazione** — niente decide niente da quei numeri — ma **non è "solo una riga"**, e va detto **prima** di stimarlo. **Se C non si può fare senza la lista del peer, si dice così**: non si finge un marker che indovina.
- ✅ **E a quel punto — e solo a quel punto — la voce 56 si può chiudere.** ⛔ **NON è chiusa da questa sezione**, e la cella *(`:103`)* **non si tocca**: si chiuderà **quando i tre marker saranno nei log** *(A è della stessa zona ma non è uno dei cinque della 56; B e C sono i due che mancano)*, con la prova nel commit, **come è stato fatto per 1, 2, 4 e 5**.

### 5. 📌 LO STATO, E COSA NON SI FA

- **Stato**: la **forma del fix** è nota *(una strada larga a quattro sorgenti, §2)*; **il primo passo è la sonda** *(§3)*; **i tre marker** *(§4)* sono **indipendenti** dal fix e si possono fare subito. ⛔ **Nessuno dei tre è "in lavorazione" qui**: questa sezione **scrive lo stato**, non apre cantieri.
- ⛔ **Codice non toccato** *(questa tornata è un'aggiunta al TODO)* · ⛔ **`docs\BUG-NOTI.md` non toccato** · ⛔ **nessuna sezione esistente riscritta o accorciata** — e la voce 56 **solo rimandata**, mai modificata.
- ⚠️ **La data di questa sezione** segue la serie del file *(`:6719` = `04:20` → `:6934` = `05:40` → `:7002` = `06:00`)*: è **la successiva**, non l'orologio della macchina *(che segnava le `15:0x`)*. Il nome della sezione **non è una misura**: è un'etichetta.

---

## [2026-10-18 08:00] ⭐⭐ **IL PUNTO COMPLETO: quattro voci della lista corta sono già chiuse da tre commit — e due voci nuove che non sta guardando nessuno**: il **nome di scena della 1ª passata** *(che può nascondere un boss VIVO)* e le **63 `NullReferenceException` di `MapManager.RefreshMap`** *(codice nostro)*; più **la casa** *(`verify-deploy` con i marker fermi all'8-12/10)*, **la Colpa** *(voce 63: mai cominciata)* e **la cerimonia** *(studiata e scartata con 3 prove)* — chiusa da **LA LISTA CORTA, RIFATTA**

> **Che cos'è questa sezione**: la fotografia di **tutte** le voci ancora aperte, presa a **HEAD `13f3d52`** *(albero pulito)*, con l'elenco di **quelle che i commit recenti hanno già chiuso e che il TODO non registra**.
> **Da dove viene**: `scratch\PUNTO-COMPLETO.md` *(297 righe — la tabella delle 32 voci aperte, la casa, la Colpa, la cerimonia)*, `scratch\STUDIO-FLAG-AC43.md` *(§4.1-§4.4: il nome di scena)*, `scratch\REFERTO-MARKER-PACCHETTO.md` *(§2.4: la condizione di chiusura della voce 56)*.
> ⛔ **Cosa questa sezione NON fa**: non riscrive, non compatta, non corregge **nessuna** sezione precedente *(si leggono come storia)*, **non tocca `docs\BUG-NOTI.md`**, **non tocca una riga di codice**, e **non dichiara chiusa la voce 56** *(serve un log, non un build)*. È **un'aggiunta in fondo, con i rimandi**.

---

### ① ✅ **LE VOCI CHE I COMMIT HANNO CHIUSO** — *il TODO non lo registra*

**Il fatto, in una riga**: la **lista corta `[2026-10-18 04:20]` §5** *(`:6901-6914`)* ha **otto voci**, e a HEAD **quattro non sono più lavoro** — chiuse **da tre commit** che il TODO **non nomina mai** in quel ruolo.

| # | la voce della lista del `04:20` | chi la chiude | prova *(verificata a HEAD)* |
|---|---|---|---|
| **2** | ⚠️ **LE DUE BARRE** *(`:6908`)* — *«110 HP = 27,5 % di scarto costanti»* | ⭐ **`9baeb15`** *(P1 — la vita del boss viaggia)* | `EnemySyncManager.cs:5254` *(`[BossLife] — LA VITA DEL BOSS VIAGGIA: "STATO + DISEGNO INSIEME"`)*, `:5313` `AlignBossLife`, `:5322` `state.BossLifeAligned`, `:5361` `causa = padrone-cambiato \| ingresso-in-scena` |
| **3** | ⭐ **LA FINESTRA DELL'AVVISO** *(`:6909`)* — *«`DO NOT PRESS` parte dal `Kill()`, la cutscene arriva ≈9-21 s dopo … DA SPOSTARE»* | ⭐ **`8978107` ②** — *la finestra non parte più dal `Kill()` ma dalla CUTSCENE*, **zero patch Harmony** | `BossDeathWarning.cs:47-57` *(armamento + tetti `CINEMATIC_ARM_TIMEOUT=60`, `CINEMATIC_WINDOW_MAX=45`)*, `:127-145`; `BossFightDeathWarning.cs:179` `if (BossDeathWarning.CutsceneWindowOpen)` |
| **7** | 📌 **LE CORREZIONI AI COMMENTI** *(`:6913`)* — i **due commenti falsi**: `ApplyAggroTarget` e la giustificazione di `ShouldWaitForPosition` | ✅ **`8978107` ③** | `EnemySyncManager.cs:4102` → *«⚠️ CORREZIONE (18/10) — QUI C'ERA SCRITTO “the other player takes damage through the game's …»*; `:4905` → *«⚠️ CORREZIONE (18/10) — QUI C'ERA SCRITTO “_remoteTargets is emptied by LevelLoaded …»* — **i due commenti ora dicono il vero** |
| **6** | 📌 **I BUG PICCOLI** *(`:6912`)* — **la metà `scena=`** *(corretta)* **e la metà “flag a stanza”** *(decaduta)* | ⭐ **`13f3d52` ③** per la prima; **premessa falsa** per la seconda | `SceneForBossView()` *(`EnemySyncManager.cs:2773`)* legge la scena **dal gioco** *(`Core.LevelManager.currentLevel.LevelName`)* e la riga stampa `scena=<nuova> · mod=<quella della mod>` *(`:2645`)*; ⚠️ **`SceneForMarker()` non è stato toccato di proposito** *(`scratch\REFERTO-MARKER-PACCHETTO.md:119`)*. E il *«flag a stanza»*: `…_BOSSDEAD` senza suffisso **esiste 11 volte su 12** ⇒ lo studio dice **«non toccare»** *(`scratch\STUDIO-FLAG-AC43.md:55-78`, `:358-385`)* |

**E la quinta, che però non è un commit**: la **voce 1** della lista *(la finestra dei 1,5 s)* è chiusa **PER DECISIONE**, non per fix — *«sulla finestra dei 1,5 s non si fa niente»* *(`[2026-10-18 06:00]`, `:7002-7041`)*, e vive in **`BUG-NOTI` voce 6** come prezzo noto.

**Il conto torna e va detto**: **8 voci** = **4 chiuse da un commit** *(2, 3, 6, 7)* + **1 chiusa per decisione** *(1)* + **3 ancora aperte** *(4 = i 3 buchi del relay, 5 = i 23 boss, 8 = la casa)*.

⚠️ **E c'è un dato che vale la pena scrivere, perché è la misura esatta di quanto il TODO sia indietro**:

```
grep 9baeb15 docs\TODO.md   → 0   ← il TODO non nomina MAI il commit che chiude la sua voce n.2
grep 13f3d52 docs\TODO.md   → 0   ← né il commit di HEAD, che chiude la metà della voce n.6 e i 3 marker
grep 8978107 docs\TODO.md   → 1   ← una sola volta, e non nella lista corta
```

⇒ ⭐ **Dichiarato chiaro, come va dichiarato**: **queste 4 voci sono chiuse dai commit `9baeb15`, `8978107` e `13f3d52`** — **la lista corta del `04:20` resta come storia**, non si tocca e non si riscrive. È **uno stato superato**, non un errore: la sezione fu scritta **prima** di quei tre commit.

**E la voce 56 — che NON è una voce della lista — va detta con la stessa precisione**:

- ✅ **i 3 marker dell'ultimo pacchetto sono implementati e nella DLL**: `[DannoVita]` *(`PenitentLifeMarker.cs:72`)* e `[Impronta]` *(`SyncIdListMarker.cs:71`, + `AttackType.SyncIdList = 202`)* — verificati **nel file**, alla riga citata;
- ⛔ **ma “implementata” non è “provata”**: **la voce 56 NON è chiusa**, perché manca **un log**;
- ⭐ **la condizione di chiusura è nera su bianco** in `scratch\REFERTO-MARKER-PACCHETTO.md:99-105` *(§2.4)*: *una run in cui **almeno una** `[Impronta] confronto #n` compaia **con i due `primoIdSolo…` valorizzati**, oppure con **entrambi** `(nessuno)` se i conteggi coincidono ma le impronte no*;
- ⛔ **e la cella `:103` non si tocca**: oggi dice **“✅ CHIUSA”** per i marker 1, 2, 4 e 5, e i due che mancano sono **il (3)**.

---

### ② 🔴 **LE DUE VOCI NUOVE** — *nessuno le sta guardando*

#### A) 🔴 **IL NOME DI SCENA NELLA 1ª PASSATA** — *è la priorità n.1 dello studio AC43, e non è un problema di log*

- ⚠️ **Il difetto**: `HideDefeatedBosses` *(e `FirstBossDeadFlagOn`)* leggono **`_scene`**, e `_scene` è assegnata **solo** in `LevelLoaded` — ma la prima passata di spawn gira **prima** di `OnLevelLoaded`. ⇒ **nella prima passata leggono la stanza PRECEDENTE**. **Misurato: 7 su 7 in `G2`, 6 su 6 in `G1`** *(`scratch\STUDIO-FLAG-AC43.md:313-323`; `docs\TODO.md:6498-6499`)*.
- 🔴 **E da FIX ② un flag acceso SCAVALCA il veto “sembra VIVO”** — la condizione è nel codice, ed è scritta così *(`:328-333`)*:

  ```csharp
  bool vivoScavalcato = BossLooksAlive(enemy);
  if (vivoScavalcato && flag == null)   // ← il veto vale SOLO con il flag spento
  ```

  ⇒ ⭐ **con un confronto largo, un flag sbagliato acceso + un boss vivo nella stanza nuova = boss NASCOSTO** — e **lo spegnimento è irreversibile nella sessione**: `_hiddenBossCorpses` *(`EnemySyncManager.cs:682`, azzerato solo in `LevelLoaded`)* e `RegisterEnemy` **salta** le istanze nascoste *(`:3362`)* ⇒ **la fight diventa invisibile e non riparte fino a un ricaricamento**. **È il peggior danno possibile.**
- ⭐ **Il fix, ed è piccolo**: leggere la scena **dal gioco** — `Core.LevelManager.currentLevel.LevelName`, **la stessa sorgente con cui il gioco scrive `_AC43`** e **la stessa che usa già `SceneForBossView`** *(`EnemySyncManager.cs:2773`)* — invece di `_scene`. **≈2 righe** *(`scratch\STUDIO-FLAG-AC43.md:348-354`)*.
- ⛔ **Ma non è “solo un marker”**: **tocca un predicato che SPEGNE i boss** ⇒ **merita un mandato suo e un test**. E ⚠️ **`13f3d52` non lo copre**: quel commit ha corretto **solo l'etichetta di `[BossView]`**, e **lo dichiara fuori scope** *(`REFERTO-MARKER-PACCHETTO.md:119`)*.
- 📌 **Da chiudere PRIMA dei 23 boss** *(voce 18 / la voce 5 della lista)*: è **l'unico rischio latente reale** che lo studio ha trovato, e **non si vede in nessun log** finché non morde.

#### B) 🔴 **LE 63 `NullReferenceException` in `Map.MapManager.RefreshMap` ← `Multiplayer.OnLateUpdate`**

- ⚠️ **Il fatto**: **63 eccezioni** in **una** sessione, stanza **`D17Z01S01`**, stack `Map.MapManager.RefreshMap` ← **`Multiplayer.OnLateUpdate`**, tutte fra **`L907` e `L1405`** *(`docs\TODO.md:6609`, `DIAGNOSI-PAUSA-E-DUE-BARRE.md` §6)*.
- 🔴 **È CODICE NOSTRO**: la mod chiama il `Map` **fuori dalla bossroom**, nel ciclo di rete. **63 volte in una sessione**, **e nessun proprietario**: la voce sta **solo** in una riga di una sezione datata, e **in nessuna lista**.
- ⭐ **E la sua unica difesa è un'assenza**: *«nella sessione di stasera sono 0»* *(`DIAGNOSI-REGRESSIONE-RIENTRO.md` §6)*. **Zero in una sessione è un'assenza, non una chiusura** — è esattamente la forma di prova che questo progetto ha già pagato altrove.
- 📌 **E c'è una nota che nessuno ha scritto**: **63 `NullReferenceException` in `OnLateUpdate` non sono solo un log sporco** — un'eccezione per frame **è una perdita di prestazioni**, e `OnLateUpdate` è **il punto in cui la mod scrive ogni giro**. Va messa come **voce di prestazioni**, non come igiene.

---

### ③ 🟡 **LA CASA** — *più seria di come la chiamavamo*

**Il quadro**: **l'unico pezzo di attrezzatura che verifica davvero** è uno script **in `scratch\`**, e **l'unico strumento in `tools\`** che dovrebbe verificare **non discrimina**.

- ⚠️ **`verify-deploy.ps1`**: *non “non controlla”* — **i suoi 9 marker sono fermi al 2026-10-08 → 2026-10-12** *(`tools\verify-deploy.ps1:59-73`)*: `orientSkip=`, `gameOrientBurst=`, `[OrientByEnemy]`, `[GameOrientByEnemy]`, `[AnimByEnemy]`, `[AnimName]`, `[EnemyJoin]`, `[Falo''] memoria nemici`, `IMPRONTA DIVERSA`. **Ogni build degli ultimi ~250 commit li contiene tutti** ⇒ **il livello dei marker non può fallire**;
- ⚠️ **l'hash è vincolante SOLO con `-ExpectedHash`** *(`:181-194`)*: senza il parametro lo **stampa e basta**;
- ⚠️ **e senza la sonda in `scratch\cecil`** *(`:49`)* **i marker si saltano IN SILENZIO** *(`:216`: «sonda Mono.Cecil assente: salto il controllo dei marcatori»)*;
- ⇒ ⭐ **la conseguenza, ed è la frase che conta**: **qualsiasi build recente passa e stampa “Puoi testare”** *(`:280`)* ed **esce 0** — anche una DLL di due settimane fa. *(La frase del TODO «stampa “puoi testare” anche su una DLL sbagliata» è **vera nel risultato** e **imprecisa nel meccanismo**: lo script **sa dire NO**, è il suo **elenco** a essere un reperto archeologico.)*
- ✅ **Il sostituto esiste e funziona**: `scratch\marker-check.ps1` *(51 righe, `-Dll` + `-Marker[]` **obbligatori**, cerca il **contenuto** sui byte grezzi in UTF-16LE **e** UTF-8, e **sa dire NO**)* — ⚠️ **ma sta in `scratch\`, non in `tools\`**, e **non è versionato insieme al codice**. Finché sta lì, **non è un gate**: è una cosa che **qualcuno si ricorda di lanciare**.
- ⚠️ **lo zip per `G1` NON ESISTE**: l'unico in `tools\` è `BlasphemousCOOP-enemysync.zip` — **1.112.105 byte, `mtime 2026-10-06 22:18`**, **50 voci**, e le prime sono **`BepInEx\…`**, cioè **il loader**, **non una build**. *(`ModdingTools-win64.zip`, l'altro, è un attrezzo, non la mod.)* ⇒ **non è «lo zip da aggiornare»: è «lo zip che non c'è»**. Serve un artefatto **prodotto dal deploy** — cartella `Modding\` + `SERVER-COOP\`, **con `sha256` e i commit scritti dentro** — non un pacchetto preparato a mano una volta;
- ✅ **e quello che già funziona, perché va detto anche questo**: il **gate dei bersagli Harmony** sta **dentro** `deploy-local.ps1` ed è **bloccante** *(`:68-85`, `throw` a `:85`)* · **`Modding\data\*` viene copiato** *(quindi `Common.dll` non resta indietro)* · **`DeployToGame=false`** *(la build **non tocca più il gioco**)*.

---

### ④ ⚠️ **LA COLPA (voce 63)** — *mai implementata, e sparita dai radar*

- ⭐ **Che cos'era**, con **la formula dell'utente** *(`docs\TODO.md:5070`)*:

  > *«la **grief sulla vita** in caso di **morte** e **non** seduta al falò»*

- ⇒ ⭐ **È la COLPA** *(la penalità di morte del gioco, le **spine** sulla barra)*, **non il griefing fra i due giocatori**, e la definizione dice **due cose**: **`Stats.Life`, non `Fervour`** · **morendo, non al falò** *(sedersi NON deve aggiungere Colpa)*.
- ⚠️ **Mai implementata**: **0 commit di codice**. Verificato in due modi indipendenti *(`scratch\PUNTO-COMPLETO.md` §3.2)*: `git log --grep` su *colpa* / *guilt* / *spine* restituisce **solo documentazione**; e `git grep -i guilt` nel client dà **7 occorrenze, tutte in commenti e in una riga di log** *(`PlayerDeathSync.cs:248-283`)* ⇒ **la mod LEGGE i fattori di colpa e li stampa** *(`vita={MaxFactor}` / `fervour={MaxFactor}`)*, **non li scrive e non li sincronizza**. Il commento del codice è esplicito: *«the game's native guilt system … is the only thing that ever moves it»* *(`:261-263`)*.
- 🔴 **Ed è SPARITA**: ultima menzione il **2026-10-16** — *«Invariata»* *(`:6127`, `:6223`)* — e **assente** dalle liste corte del **17** *(`[2026-10-17 23:40]`)* e del **18** *(`[2026-10-18 04:20]`)*. *(Verificato: `grep "voce 63"` → **16 righe**, **nessuna** nelle due liste.)*
- ⇒ 📌 **Va dichiarata come è, e questa è la formula**: **«funzionalità mai cominciata — né aperta in lista né chiusa»**. È **lo stato peggiore** per un documento che si dichiara punto unico di verità: se **si fa**, va **rimessa in lista**; se **non si fa**, va **deciso e scritto** — come è stato fatto per la voce 58.

---

### ⑤ ⛔ **LA CERIMONIA DEL BOSS** — *la memoria era giusta a metà*

- ✅ **È stata studiata**, e in **tre** documenti diversi, con **tre oggetti diversi**:
  1. **`scratch\STUDIO-TUTTI-NELLA-BOSSROOM.md`** — il «punto 0»: **infattibile COME SCRITTO** *(`:45`)*. La catena è **`Collider2D` → FSM → `UnityEvent`** e **tre quarti stanno negli asset di scena**, che **non sono nel repo**;
  2. **`scratch\STUDIO-VIA-BOSS-E-TRIGGER.md`** — il trigger: **«non si fa»**, rischio **ALTO**, **tre prove**: *il trigger **è già scattato** su ogni macchina che entra* · **`BossBlock` = 0 occorrenze in tutta `Assembly-CSharp`** *(`:174-179`; esiste solo come stringa nei log, accesa da una FSM)* · **`onStartBossFight.Invoke()` è l'ULTIMA riga** del metodo *(`:121`)* e **non imposta `IsFightStarted`** *(`:119`)*;
  3. **`scratch\STUDIO-PUNTO-0-RESET.md`** — la soluzione vera, ed è **un'altra**: **non trapiantare il boss** *(`3e52fc9`)*. *«Torna nuovo. È nuovo.»* ⇒ **togliendo il corpo dal corridoio, il trigger torna raggiungibile e la cerimonia parte da sé**.
- ⛔ **Perché no**, in tre righe: **non è decidibile dal repo** *(la scena è `data.unity3d`, LZ4HC, non decodificata)* + **il rischio è una STANZA BLOCCATA** *(`BossBlock` acceso + barriera su + boss spento: non si annulla in un commit)* + ⭐ **il problema che doveva risolvere è stato risolto PER L'ALTRA VIA, e verificato in gioco**.
- ⚠️ **MA NON È IMPOSSIBILE, e va detto anche questo**: manca **il test** *(5 passi, **mai fatto**: `STUDIO-VIA-BOSS-E-TRIGGER.md` §7.3 — «rientro con la fight in corso»)* e manca **il marker `[Zona]`**. La strada scritta **non è «skippare la cerimonia»: è un gate sul giocatore che ENTRA**. Finché quel test non c'è, **la decisione del progetto è non toccare la cerimonia**, ed è una decisione, non una svista.
- ⭐ **E il fallback dichiarato è `IntroJump()`** *(`ElderBrotherBehaviour.cs:191-194`; **1 definizione, 1 chiamante**: `ElderBrotherIntroDummy.cs:55`)* — ⚠️ **NON implementato** *(dichiarato: «resta il fallback, fuori da questo commit»)*, **per-boss**, **non idempotente** *(due chiamate = due coroutine + due `jumpAttack.Use`)*, **passa dalle clip** ⇒ **può accendere la barriera**, **non fa la cerimonia** *(si perdono ≈4,7 s di messa in scena)*, e **non è mai stato provato in gioco** *(`scratch\STUDIO-PUNTO-0-RESET.md:282-331`)*.
- ⚠️ **E va distinto dall'ALTRO fallback**, quello della **voce 58 / M1** *(`:5391-5395`: «riaprire la guardia e mandare il colpo letale»)*: sono **due cose diverse in due catene diverse** — *ingresso del boss → `IntroJump`* contro *morte del boss → colpo letale*. **Chi cerca «il fallback» senza dire quale, sbaglia riga.**

---

### ⑥ 📋 **E LA LISTA CORTA, RIFATTA** — *con lo stato vero*

> **Sette voci**. Le prime due **non sono nella lista del `04:20`** e sono **le più urgenti**; la 3-6 sono **lavori dichiarati**; la 7 è **una decisione presa**. *(Le voci 2, 3, 6 e 7 della vecchia lista **non ci sono più**: le hanno chiuse `9baeb15`, `8978107` e `13f3d52` — §①. La voce 1 è chiusa **per decisione**.)*

| # | cosa | perché qui | rimando |
|---|---|---|---|
| **1** | 🔴 **Il nome di scena in 1ª passata** *(può nascondere un boss VIVO)* | **l'unico rischio latente che spegne un boss e non torna indietro senza ricaricare**, e **va chiuso PRIMA dei 23 boss** | §②-A; `STUDIO-FLAG-AC43.md:313-354`; `:6498-6499` |
| **2** | 🔴 **Le 63 NRE di `RefreshMap`** *(codice nostro)* | **63 in una sessione**, **nessun proprietario**, **in nessuna lista**, e **costa prestazioni** oltre al log | §②-B; `:6609`; `DIAGNOSI-PAUSA-E-DUE-BARRE.md` §6 |
| **3** | 🟡 **La casa** *(`verify-deploy` · `marker-check` in `tools\` · lo zip per `G1`)* | **l'unica voce che rende false tutte le altre**: con i marker dell'8-12/10, **ogni «verificato» è a rischio** | §③; `tools\verify-deploy.ps1:59-73`, `:181-194`, `:216`; `tools\deploy-local.ps1:68-85` |
| **4** | 🟡 **La Colpa** *(voce 63 — mai cominciata)* | **la cosa che l'utente ha chiesto**, e **dal 16/10 non è in nessuna lista** | §④; `:5035-5077`, `:6223`; `PlayerDeathSync.cs:248-283` |
| **5** | 🟡 **Gli eventi del boss** | **le animazioni sono provate**; **gli effetti viaggiano** *(relay, 88/88)*; il residuale è **la strada di risoluzione**, e **prima va la SONDA** | `[2026-10-18 07:00]` `:7046-7120`; `docs\BUG-NOTI.md:317-430`; `:6821-6829` |
| **6** | 🟡 **I 23 boss** *(la mira)* **e 12 su 17** *(la morte recitata)* — **dopo la 1** | **il lavoro più largo e il meno rischioso**: **un boss per volta, con la forma già provata** su `ElderBrother` | `:6831-6836`; `docs\BUG-NOTI.md:434-553`; voce 18 |
| **7** | ⛔ **La cerimonia** | **studiata e scartata con 3 prove**; **non impossibile, ma non conveniente** — e **il problema è stato risolto per l'altra via** | §⑤; `STUDIO-VIA-BOSS-E-TRIGGER.md`; `STUDIO-PUNTO-0-RESET.md`; `3e52fc9` |

⚠️ **E due cose che restano fuori di proposito, come prima**: **la finestra dei 1,5 s** *(chiusa per decisione, `BUG-NOTI` voce 6)* e **il flag “a stanza”** *(premessa falsa: 11 su 12)*.

---

### ⑦ 📌 **LO STATO DI QUESTA SEZIONE**

- ✅ **Aggiunta, non riscrittura**: le sezioni precedenti *(compresa la lista corta del `04:20`, che qui è dichiarata **superata**)* **restano intatte e leggibili come storia**. Nessuna riga cancellata, nessuna sezione accorciata.
- ⛔ **`docs\BUG-NOTI.md` non è stato toccato** *(voci 1-8 già aggiornate)* · ⛔ **nessuna riga di codice toccata** · ⛔ **la voce 56 NON è dichiarata chiusa** *(manca il log, non il build: `REFERTO-MARKER-PACCHETTO.md:99-105`)*.
- ⚠️ **La data di questa sezione** segue **la serie del file** *(`:7002` = `06:00` → `:7046` = `07:00`)*: è **la successiva**, non l'orologio della macchina. **Il nome della sezione non è una misura**: è un'etichetta.
- ⚠️ **E i numeri di riga del codice vanno letti come indizi, non come coordinate**: il TODO ne cita già quattro **sbagliati a HEAD** *(`EnemySyncManager.cs:4516`, `:4377`, `:5241`, `:790`)*. **Citare il metodo.**

---

## [2026-10-18 09:00] ⭐⭐ **LA PAUSA CONDIVISA È CHIUSA — e con lei la NRE che la teneva muta**: la pausa diventa **una regola** *(`50c8ca9`, testata dall'utente)* e `RefreshMap` **smette di lanciare** *(`bc834f3`)*; più **la lista corta rifatta** *(5 voci, la casa in testa)*, **i 4 flag** che nessuna lista nominava, e **le righe da non dimenticare**

**Fonti**: `scratch\REFERTO-PAUSA-CONDIVISA.md` *(la V2: i sei pezzi, il costo, i test, i residui)* · `scratch\STUDIO-PAUSA-CONDIVISA-V2.md` *(lo studio che l'ha preceduta)* · `scratch\REFERTO-NRE-E-PAUSA.md` *(la NRE e la notifica B)* · `scratch\STUDIO-PAUSA-CONDIVISA.md` *(`§5.1`: perché i timer si fermano)* · `scratch\DIAGNOSI-63-NRE.md` *(la catena della NRE)*. **HEAD verificato: `50c8ca9`** *(`git rev-parse --short HEAD`)*, branch `fix-f1a`, albero pulito.

⚠️ **E QUESTA SEZIONE NON CORREGGE QUELLE QUI SOPRA**: `:7124` *(il punto completo del `08:00`)* e **la sua lista corta a 7 voci** *(`:7236-7244`)* **erano vere quando sono state scritte** — `9ee3ca6`, `bc834f3` e `50c8ca9` sono **arrivati dopo**. Restano **come storia**; qui si **aggiunge** e si **rimanda**. ⛔ **Nessuna sezione riscritta o accorciata** · ⛔ **`docs\BUG-NOTI.md` non toccato** · ⛔ **codice non toccato da questa tornata** *(il codice è quello di `50c8ca9`: qui si registra, non si modifica)*.

---

### ① ✅ **LA PAUSA CONDIVISA — CHIUSA E VERIFICATA DALL'UTENTE**

**Il fatto, in una riga**: **`50c8ca9`** — *la pausa è **una regola***: chi mette in pausa **ferma anche il mondo dell'altro**, e **l'altro viene tirato fuori** quando il primo chiude — da qualunque scheda, in qualunque ordine.

#### 1.1 Il problema, come si vedeva

- il pausante **fermava il mondo per chi era nella stessa stanza**;
- ⇒ **il passivo vedeva un mondo fermo che non era il suo** — un nemico **congelato** che lo inseguiva, e nessun modo di capire perché;
- ⇒ ⭐ **e `B` (la notifica `YOUR TEAMMATE IS PAUSED`) lo **descriveva** senza risolverlo**: una riga a schermo, il mondo comunque fermo. **Descrivere non è curare.**

#### 1.2 La causa VERA — trovata strada facendo, e non è un `if`

🔴 **`Time.timeScale = 0`**, cioè **il meccanismo** con cui il gioco mette in pausa:

- `Core.Logic.PauseGame()` scrive `CurrentLevelConfig.TimeScale = 0f` *(`LogicManager.cs:250`)* → il setter scrive `TimeScaleReal` *(`LevelInitializer.cs:130-136`)* → `LevelInitializer.Update()` fa **`Time.timeScale = TimeScaleReal;` ogni frame** *(`:234-237`)*;
- ⇒ **i timer di invio della mod si fermano per **aritmetica**, non per un `if`**: a `timeScale = 0` **`Time.deltaTime` vale 0** e **`Time.time` non avanza** *(fatto engine, non di questo repo)* — e la mod usa **esattamente quei due valori** come orologi di invio *(`scratch\STUDIO-PAUSA-CONDIVISA.md` §5.1)*;
- ⚠️ **e la frase «il progetto ha escluso il `timeScale`» va **rovesciata****: la regola vieta **alla mod** di scriverlo *(`:5356` «❌ NON toccare `TimeScale`»; `:6198` «zero `SetBlocker`/`ResetManager`/`TimeScale`»)* — e `PauseGame()` è **la funzione del gioco** che lo fa. **Non esiste una pausa in questa engine che non sia `timeScale = 0`.**

⇒ ⭐ **La conseguenza che ha deciso la forma del fix**: la pausa **non si può «annunciare» e basta**, e **non si possono riscrivere i timer** *(sarebbe rifare il gioco)*. O si **condivide la pausa con il canale del gioco** — cioè `PauseGame()` / `HidePauseMenu()`, gli **stessi** che preme il giocatore — o il mondo fermo resta.

#### 1.3 I sei pezzi — `PauseNotice.cs`, ognuno con la sua prova

| # | riga | cosa fa |
|---|---|---|
| **R1** | `ApplyRemotePause` `:394` | ricevuto «in pausa» ⇒ **`Core.Logic.PauseGame()`** + **`_pausedByRemote = true`** *(`:467`)*. **Non** tocca `LogicStates` né i blocker ⇒ lo stato «senza menù» è quello che il gioco produce |
| **R2** | `CloseAndResume` `:575` (+ retry `RetryClose` `:268`) | ricevuto «riprendi» ⇒ **dispatch a 3 rami**: `IsShowingInventory` → `HideInventory()` · `IsShowingMenu` → `HidePauseMenu()` · altrimenti `ResumeGame()` **solo se `IsPaused`**. Il retry copre i **0,4 s** del fade *(`FadeHide` esce se `IsFading`)*, con tetto **`CLOSE_CAP_SECONDS = 2f`** *(`:110`)*: al tetto si **dichiara** la frenata e la pausa resta — **mai** `ResumeGame()` con un menù aperto |
| **R3** | `TrySendFront` `:669` — **la riga obbligatoria** | **`\|\| _pausedByRemote`** nella guardia *(`:673`)*: senza, il **passivo** non potrebbe **mai** mandare la sua ripresa *(non ha mai annunciato una pausa)* e **il pausante resterebbe in menù per sempre** |
| **R4** | `CheckRemoteStillPaused` `:844` + `Disarm` `:902` | la pausa **cade** se: disconnesso · nessun peer connesso · peer in un'altra stanza **o a terra** · ritorno al menù principale. ⛔ **Sono predicati istantanei, NON un timeout**: l'**8 s** che resta è **solo** per la build diversa, che sulla rete è **indistinguibile** *(protocollo `11` uguale per tutte)* |
| **R5** | `StepRemotePauseGuards` `:214` | niente pausa se: fuori livello / `CurrentLevelConfig` null · `InsideChangeLevel` · `sceneName` vuoto · **io a terra** · già in pausa mia · slow-motion · fade a schermo. **+ il caso «passivo morto»** *(§1.5)* |
| **R0** | `TryOpenMapOntoPassive` `:507` | `PauseWidget` → `InitialWidget = MAP` · `InitialMapMode = SHOW` → **`FadeShow(checkInput: true)`**, la chiamata **identica** a `UIController.cs:440-442` ⇒ al passivo **si apre la mappa in faccia**: la pausa ha una faccia che si può leggere |

**L'ordine dei quattro passi di `Tick()` `:168` non è cosmetico** *(e chi lo cambia rompe R3 senza accorgersene)*:

```
① StepRemotePauseGuards()   ← può far cadere la pausa in QUESTO frame
② ObserveLocalFront()       ← la ripresa del ① parte NELLO STESSO frame (caso «passivo morto ⇒ manda RESUME»)
③ SettlePausedByRemote()    ← il flag si congeda DOPO il ②, altrimenti il ② non avrebbe più il permesso di R3
④ CheckRemoteStillPaused()  ← le uscite di R4
```

#### 1.4 ⭐ Il costo — dichiarato e verificato

**0 patch Harmony** *(269 bersagli, invariati)* · **0 righe di protocollo** *(resta `11`: il canale è `AttackType.PauseRelay = 203`, `PvP\Models\Enums.cs:150`, già introdotto da `bc834f3`)* · **0 righe di server** *(il filtro per stanza è gratis: `Attack` è già filtrato per scena lato server)* · **0 file nuovi** · **0 reflection** · **0 input simulato** · **0 scritture di `Time.timeScale`** *(le scrive il gioco, dentro `PauseGame`)*.

#### 1.5 ✅ Il test — fatto dall'utente, ed è la prima volta in questa voce

- ✅ **l'uscita al menù principale**: la pausa **cade** e **non resta appesa**;
- ✅ **il crash di G1** ⇒ *la pausa cade **istantaneamente*** — non a 8 s: è la differenza fra il timeout e le uscite di R4;
- ✅ **«tutto perfetto»** — la formula dell'utente, **non** una deduzione mia.
- 📌 **E il caso «passivo morto» è la ragione per cui la morte toglie la pausa a tutti**: a `timeScale = 0` la **morte** regge *(usa `Input` e `WaitForSecondsRealtime`)*, ma **il respawn no** *(passa da un tween: `Fade(1.5 s, …)`)* ⇒ senza R5-morte il morto resterebbe **congelato in una schermata di morte**.

#### 1.6 ⚠️ I residui dichiarati — non sono chiusi, e vanno letti

| # | residuo | peso |
|---|---|---|
| **1** | **`R0` non è misurata in gioco**: che `FindObjectOfType<PauseWidget>()` restituisca l'istanza giusta è **dedotto** *(`Deactivate()` non disattiva il GameObject)* | 🟡 **e `R0` è l'unico pezzo rimovibile**: basta non chiamare `TryOpenMapOntoPassive` — **una riga** — e tutto il resto resta identico |
| **2** | **`mappaAschermoDopo=` resta `true`**: dopo la chiusura il GameObject della mappa **resta attivo** *(spostato di 1000 unità)* ⇒ qualche `RefreshMap` in più su un widget invisibile | 🟢 **non è una NRE** *(`RootRenderer_0` esiste comunque)*, e succede **già oggi** chiudendo la mappa a mano |
| **3** | **le due macchine devono avere lo stesso `50c8ca9`**: il protocollo **non distingue le build** *(resta 11)* ⇒ una macchina vecchia **ignora il 203** e non fa niente | 🟠 **silenzioso**: le difese sono i marker *(`TX` dice che è partito, `RX` che è arrivato)* e il timeout di **8 s** |
| **4** | **`d07z01s03` (Isidora)**: il gioco **vieta** il menù di pausa lì *(`UIController.cs:1053`)* e la mod **non replica il divieto** *(R1 non ha guardie di livello)* | 🟡 **da misurare** se si gioca Isidora in due |
| **5** | **tutorial e popup di sblocco mettono in pausa senza che il giocatore lo decida** *(`TutorialManager.cs:110`, `UIController.cs:651`)* ⇒ con la V2 **congelerebbero l'altro per un popup** | 🟡 **non visto succedere**, filtro **non** aggiunto *(sarebbe la forma fragile a cinque stringhe)*: se dà fastidio il discriminante è `Core.Input.HasBlocker("TUTORIAL")` |
| **6** | **la finestra di 0,4 s del fade**: `IsPaused` è già `false` e il widget è ancora attivo | 🟡 **ereditato dal gioco** *(è la finestra che il gioco ha per sé)*, **non** creato dalla mod |

---

### ② ✅ **E LA NRE — CHIUSA** *(era il grimaldello)*

**Il fatto**: **`bc834f3`** — e la prima riga è quella che cambia l'attribuzione:

- ✅ **`RefreshMap` non esiste nel gioco**: **0 occorrenze** in tutto il decompilato *(**2079** file `.cs`, contati oggi)* ⇒ il `Map.MapManager` nello stack **è la nostra classe**, non una del gioco. **Verificato, non ereditato** *(`scratch\DIAGNOSI-63-NRE.md`)*;
- **la catena**: `Multiplayer.OnLateUpdate` → `MapManager.Update` `:76` → `RefreshMap` `:180` → la riga `playerMarks.childCount` ⇒ **NRE**, e **una per frame** per tutta la pausa *(l'eccezione usciva **prima** dell'azzeramento del flag)*;
- **la causa VERA**: ⭐ **la mappa è una scheda del menù di pausa**, e il suo canvas **è disattivato quando non la mostri** *(`PauseWidget.SetChildActive` → `SetActive(false)`)* ⇒ `FindObjectOfType` **non guarda gli oggetti disattivati** ⇒ `CreateMapUI` **usciva muta** *(due `return` senza dire niente)* ⇒ `playerMarks` restava `null` ⇒ **la riga dopo lanciava**;
- ⚠️ **e la scoperta dell'utente, che il codice conferma**: *si mette in pausa **solo** da **inventario** e **mappa*** ⇒ la finestra del guasto è **«prima della prima apertura della scheda mappa»** in quella sessione. Chi apre la mappa per primo **non vede mai** la NRE;
- **il fix, in tre pezzi**:
  - **`CreateMapUI` dice se ce l'ha fatta** *(`private bool`, `:257`)* e `RefreshMap` **esce PRIMA** della riga che lanciava: la guardia era messa **prima** del tentativo, ora è **dopo** — che è l'unica posizione che dice la verità;
  - **il flag servito, non buttato**: se la mappa non è a schermo la richiesta **resta in coda** e si riprova; quando è a schermo il flag si azzera **prima** della chiamata ⇒ un fallimento residuo costa **una riga**, non una raffica;
  - **il predicato diventa «la scheda mappa è a schermo»** *(`IsMapOnScreen()` `:135`, `activeInHierarchy`)*, **non `IsPaused`**: `IsPaused` lo alzano **cinque** UI, e **la mappa è una sola**;
- ⭐ **era anche «la metà che si ammutoliva in pausa»**: **ogni frame che lanciava saltava `SendQueue()`** *(il `try/finally` di `OnLateUpdate` lasciava fuori le **7** chiamate dopo la `:292`)* ⇒ **la notifica `B` non era solo inutile: era **muta** proprio mentre serviva**, e il battito della pausa condivisa non sarebbe mai partito. ⇒ **La NRE è venuta prima della pausa condivisa per questa ragione, non per ordine alfabetico**;
- 📌 **marker**: `[Map] richiesta di ridisegno IN CODA e chiamata SALTATA … motivo=` *(una per richiesta, non per frame; `motivo` ∈ `inventario-a-schermo` · `tutorial-a-schermo` · `sblocco-a-schermo` · `menu-di-pausa-su-un-altra-scheda` · `mappa-in-game-senza-scheda` · `pausa-senza-mappa` · `gioco-non-in-pausa`)* e `[Map] CreateMapUI non ha creato niente`;
- ⚠️ **e la correzione al commento che vale più del fix**: *«**0 connessi non è una difesa**»* — `playerMarks.childCount` viene **prima** del ciclo sui giocatori ⇒ **una lista vuota non protegge niente**. La difesa vera è che `mapUpdateQueued` **non si accende senza un peer** *(i suoi tre soli scrittori sono tutti eventi del peer)*.

---

### ③ 📋 **E LA LISTA CORTA, RIFATTA** — *con lo stato vero*

> **Cinque voci**. La **1** è la più utile *(senza la casa, ogni «verificato» è a rischio)*; la **2** è **una sonda prima di un fix**; la **3** non è mai cominciata; la **4** è il lavoro più largo e il meno rischioso; la **5** è un residuo che **nessuna lista nominava**. *(Le voci della lista del `08:00` non sono sparite: il conto è in §3.2.)*

| # | cosa | perché è qui | rimando |
|---|---|---|---|
| **1** | 🟡 **LA CASA** *(la più utile: come si mette in produzione)* | **l'unica voce che rende false tutte le altre** | §3.1; `:7192-7202` |
| **2** | 🟡 **LA STRADA DI RISOLUZIONE** *(i 3 buchi **e** gli effetti del Penitente)* | ⚠️ **sonda prima**, poi il fix | `:7046-7120`; `docs\BUG-NOTI.md` voce 7 |
| **3** | 🟡 **LA COLPA (voce 63)** | **mai cominciata**, e dal 16/10 **in nessuna lista** | `:7206-7215` |
| **4** | 🟡 **IL LAVORO PER BOSS** *(la mira **23** · la morte recitata **12 su 17**)* | il più largo e il meno rischioso: **un boss per volta, con la forma già provata** su `ElderBrother` | `:7243`; `docs\BUG-NOTI.md:434-553`; voce 18 |
| **5** | 🟡 **I 4 FLAG** *(Amanecidas · Perpetua · Pontiff Husk · Viridiana)* | ⭐ **nuova di questa lista**: il loro flag ha **un altro nome** *(`SANTOS_AMANECIDA_*_DEFEATED`, `VIRIDIANA_DEAD_BOSS01…05`)* e **non si ricava dalla scena** ⇒ per loro resta **il solo `Status.Dead`** | `scratch\STUDIO-FLAG-AC43.md:73`, `:228`, `:254`; `scratch\REFERTO-NOME-SCENA.md:137`; `EnemySyncManager.cs:378-386` *(la whitelist: 5 nomi)* |

#### 3.1 🟡 LA CASA — quattro pezzi, e uno è **nuovo**

- ⚠️ **`verify-deploy.ps1` mente**: i suoi **9 marker sono fermi all'2026-10-08 → 2026-10-12** *(`tools\verify-deploy.ps1:59-73`)* ⇒ **il livello dei marker non può fallire**; **l'hash è vincolante solo con `-ExpectedHash`** *(`:181-194`)*; e **senza la sonda in `scratch\cecil` i marker si saltano in silenzio** *(`:216`: «sonda Mono.Cecil assente: salto il controllo dei marcatori»)* ⇒ **qualsiasi build recente stampa «Puoi testare»** *(`:280`)* **ed esce 0**;
- 🔴 **NUOVO — `deploy-local.ps1` copia da `bin\Debug`**: il percorso è **scritto a mano** *(`:42` `…\bin\Debug\BlasClient.dll`; `:48` `…\bin\Debug\Blasphemous.Multiplayer.Common.dll`)* ⇒ ⚠️ **una build `Release` + deploy = la DLL `Debug` **vecchia** in gioco**, e il deploy **dice comunque «269, tutti validi»** — perché **il gate è vero, ma il soggetto del gate è la DLL sbagliata**;
- 📌 **`marker-check.ps1` sta in `scratch\`, non in `tools\`** *(51 righe; `-Dll` + `-Marker[]` **obbligatori**; cerca il **contenuto** sui byte grezzi in UTF-16LE **e** UTF-8; **sa dire NO**)*: finché sta lì **non è un gate**, è una cosa che **qualcuno si ricorda di lanciare**;
- ⚠️ **lo zip per `G1` NON ESISTE**: l'unico in `tools\` è `BlasphemousCOOP-enemysync.zip` — **1.112.105 byte, `mtime 2026-10-06 22:18`**, **50 voci**, e le prime sono `BepInEx\…` *(il loader, **non una build**)*. ⇒ **non è «lo zip da aggiornare»: è «lo zip che non c'è»**.

#### 3.2 📌 Che cosa esce dalla lista del `08:00`, e perché — *nessuna voce è sparita in silenzio*

| la voce del `08:00` | dove è finita |
|---|---|
| **1** il nome di scena in 1ª passata *(poteva nascondere un boss VIVO)* | ✅ **chiusa dopo quella sezione** da **`9ee3ca6`**: `SceneForBossFlags` *(`EnemySyncManager.cs:889`)* legge **dal gioco** *(`Core.LevelManager.currentLevel.LevelName`)* con **ripiego dichiarato** su `_scene`, e il marker `[BossCorpse] scena-flag=` *(`:936`)* dice a ogni passata se il nome viene dal gioco. ⚠️ **Il test in gioco non risulta**: il rischio era *un flag sbagliato che spegne un boss vivo* |
| **2** le 63 NRE di `RefreshMap` | ✅ **chiusa da `bc834f3`** ⇒ §② |
| **3** la casa | ➡️ **1** di questa lista, con **un pezzo nuovo** *(§3.1)* |
| **4** la Colpa | ➡️ **3** — **invariata** |
| **5** gli eventi del boss *(il relay)* | ➡️ **2** — **invariata**: la **sonda non è stata fatta** |
| **6** i 23 boss · 12 su 17 | ➡️ **4** — **invariata**, più il residuo dei **4 flag** *(**5**)* |
| **7** la cerimonia | ⛔ **fuori di proposito**: era ed è **una decisione presa** *(studiata e scartata con 3 prove, `:7219-7228`)*, non lavoro — come **la finestra dei 1,5 s** *(`BUG-NOTI` voce 6)* |

---

### ④ ⚠️ **E LE COSE DA NON DIMENTICARE** — *quattro righe per chi legge*

- ⚠️ **la regola «chi esce per primo porta fuori tutti»** ⇒ **chiude anche un menù non suo**: se il passivo preme una volta, **il menù del pausante si chiude da solo** — *anche se stava su un'altra scheda, anche se era il suo*. È **voluto** *(è la richiesta dell'utente alla lettera)* e il costo è **una pressione di tasto**. L'alternativa *(chiudere solo la pausa remota)* è **un `if` in `Disarm`**.
- ⚠️ **la morte toglie la pausa a tutti**: due giocatori, uno è a terra, **la partita va avanti** — e il respawn del morto **non reggerebbe** a `timeScale = 0`. Il pausante lo legge nel log come `pausa caduta, motivo=…`.
- ⚠️ **e `R0` (la mappa sul passivo) non è misurata** ⇒ **ma è rimovibile in una riga**: basta non chiamare `TryOpenMapOntoPassive` e **il resto resta identico**. È **l'unico pezzo di tutta la V2 che si toglie senza toccare nient'altro**.
- ⚠️ **«0 in un'altra sessione» NON è una chiusura: è **assenza**** — e qui la forma di prova ha un nome preciso: per la NRE la finestra era **«prima della prima apertura della scheda mappa»** ⇒ **una sessione in cui la mappa era già stata aperta non poteva produrre la NRE, nemmeno con il difetto al suo posto.** È la ragione per cui `bc834f3` è un **fix** e «0 di stasera» no.

---

### ⑤ 📌 **LO STATO DI QUESTA SEZIONE**

- ✅ **Aggiunta, non riscrittura**: le sezioni precedenti — comprese **la lista corta del `04:20`** *(`:6901-6914`)* e **quella del `08:00`** *(`:7236-7244`)*, qui dichiarate **superate** — **restano intatte e leggibili come storia**. **Nessuna riga cancellata, nessuna sezione accorciata.**
- ⛔ **`docs\BUG-NOTI.md` non è stato toccato** · ⛔ **nessuna riga di codice toccata in questa tornata** · ⛔ **nessun build e nessun deploy fatti da me**: build, deploy, sha e marker sono **dei due commit**, e il test in gioco è **dell'utente** *(§1.5)*.
- ⚠️ **I numeri di riga del codice vanno letti come indizi**: quelli di questa sezione sono **riverificati a HEAD** *(`PauseNotice.cs` per R1-R5 e R0; `Map\MapManager.cs`: `Update:76`, `IsMapOnScreen:135`, `RefreshMap:180`, `CreateMapUI:257`)*. **Citare il metodo** resta la regola.
- ⚠️ **La data di questa sezione** segue **la serie del file** *(`:7002` = `06:00` → `:7046` = `07:00` → `:7124` = `08:00`)*: è **la successiva**, non l'orologio della macchina *(il commit `50c8ca9` è datato `2026-10-18 23:47`)*. **Il nome della sezione non è una misura**: è un'etichetta.

## [2026-10-19 00:15] 📌 **LO STRUMENTO DI VERIFICA ORA È VERSIONATO** — `marker-check.ps1` passa da `scratch\` *(gitignored: non versionato)* a **`tools\marker-check.ps1`**, con **i percorsi corretti per la nuova base** e **la logica del check intatta**; e **`tools\verify-deploy.ps1` viene CANCELLATO** *(stessa tornata, decisione dell'utente: uno strumento che mente viene usato)*

> **Questa entry è piccola di proposito.** Non chiude nessuna voce della lista corta: **chiude il buco per cui una voce della lista corta — «la casa» — poteva perdere il suo unico strumento che sa dire NO.** Il check era **il gate vero** e viveva in una cartella che **git non guarda**: se il disco fosse morto, sarebbe morto con lui.

### ① ✅ Cosa si è spostato — **un solo posto**, e il resto è un rimando

- ⭐ **`scratch\marker-check.ps1` → `tools\marker-check.ps1`**, **versionato** insieme a `deploy-local.ps1` / `verify-patch-targets.ps1` *(e **non più** con `verify-deploy.ps1`: vedi §⑥)*. Il file era di **51 righe**; ora ne ha **82**, e le righe in più sono **solo commento e la correzione dei percorsi** *(la logica del check è la stessa riga per riga)*.
- ⛔ **Nessuna doppia copia**: in `scratch\` resta **un rimando di 22 righe** *(nessuna logica: controlla che il file versionato ci sia e gli inoltra `-Dll` e `-Marker`)* ⇒ le invocazioni vecchie **continuano a funzionare** e **il posto della logica resta uno**.

### ② ✅ I percorsi corretti — perché cambiando cartella cambia la base

- ⚠️ **Il fatto**: `scratch\` sta **un livello SOPRA** la radice del repo *(`D:\Codificando\scratch` vs `D:\Codificando\Blasphemous COOP`)*, `tools\` sta **un livello SOTTO** *(`…\Blasphemous COOP\tools`)*. ⇒ **la stessa stringa relativa** puntava a due file diversi. *(Nel file non c'erano percorsi interni: il check riceve sempre `-Dll` da fuori — la base sbagliata era quella del **chiamante**, ed è la ragione per cui un `-Dll` relativo funzionava «per caso» o non funzionava.)*
- ⭐ **La correzione, minima e dichiarata**: un `-Dll` **relativo** viene risolto, **in quest'ordine**, su **cartella corrente → radice del repo (`$PSScriptRoot\..`) → cartella di gioco** (`D:\SteamLibrary\steamapps\common\Blasphemous`, **la stessa scritta a mano da `deploy-local.ps1:15`**). Un `-Dll` **assoluto** resta com'è ⇒ **le invocazioni di prima non cambiano**.
- ✅ **I parametri sono gli stessi**: `-Dll` e `-Marker[]`, **entrambi `Mandatory`**. ⇒ `& '.\tools\marker-check.ps1' -Dll 'Modding\plugins\BlasClient.dll' -Marker '[Pausa]','[Map]'` *(relativo, da qualunque cartella)* e `& '…\tools\marker-check.ps1' -Dll 'D:\…\Modding\plugins\BlasClient.dll' -Marker …` *(assoluto, come si è sempre lanciato)* **funzionano entrambe**.

### ③ ✅ La verifica — **l'uscita vera**, non un riassunto

**Sulla DLL DEPLOYATA** *(`Modding\plugins\BlasClient.dll`, lanciato **da `tools\`** con percorso **relativo**)* — `451584` byte, `sha256 f0dc643b864c3d4c931fd98487972df21ee0ff297b45a143e3fe16feb690bc3c`, `mtime 2026-10-18 23:48:02`:

```
[PRESENTE] "[Pausa]"  UTF-16LE @ 301171,301457,… (24 occorrenze)
[PRESENTE] "YOUR TEAMMATE IS PAUSED"  UTF-16LE @ 310196,310347,400523
[PRESENTE] "ho messo in pausa il passivo"  UTF-16LE @ 304525
[PRESENTE] "ho chiuso e ripreso"  UTF-16LE @ 307448
[PRESENTE] "sciolta dalla MORTE locale"  UTF-16LE @ 301473
[PRESENTE] "[Map] richiesta di ridisegno IN CODA e chiamata SALTATA"  UTF-16LE @ 345016
[PRESENTE] "[Map] CreateMapUI non ha creato niente"  UTF-16LE @ 346419
[PRESENTE] "mappaAschermoDopo="  UTF-16LE @ 307796
[PRESENTE] "MAP/SHOW"  UTF-16LE @ 305575
[PRESENTE] "[BossCorpse]"  UTF-16LE @ 282068,282813,283150
[PRESENTE] "BOSSFIGHT|"  UTF-16LE @ 288982
ESITO: TUTTI i marker richiesti sono PRESENTI
```
⇒ **`exit 0`**. **11 marker su 11 PRESENTI** *(anche dalla radice del repo, con lo stesso percorso relativo: la base non dipende più dalla cartella corrente)*.

**La DLL deployata È quella di `bin\Debug`** — **confronto fatto, non supposto**: `Blasphemous.Multiplayer.Client\bin\Debug\BlasClient.dll` ha **lo stesso `sha256 f0dc643b…`**, **gli stessi `451584` byte** e **lo stesso `mtime 2026-10-18 23:48:02`** ⇒ **il gioco esegue la build che `deploy-local.ps1` copia** *(è la build di `50c8ca9`, cfr. `_commitmsg.txt` in `scratch\`)*.

**Il controllo negativo — perché un check che non sa dire NO non è un check**: con un marker inesistente *(`marker-che-NON-esiste`)* **la stessa DLL** dà
```
[ASSENTE ] "marker-che-NON-esiste"  (nessuna occorrenza)
ESITO: ALMENO UN marker richiesto e' ASSENTE
```
⇒ **`exit 1`**. ⭐ **Il file funziona da `tools\`: cercare e non trovare è la prova che discrimina**, e il risultato **dipende dalla DLL**, non dal percorso da cui è lanciato.

### ④ ⚠️ L'attribuzione — chi ha fatto cosa

- **`HEAD` è `c061249`** *(`s <s@l>`, `2026-10-18 23:59:23`)*, branch **`fix-f1a`**, working tree **pulito** prima di questa tornata.
- ⛔ **Quello che NON ho fatto**: nessun build, nessun deploy, **nessuna riga di codice toccata**, `docs\BUG-NOTI.md` **non toccato**, `deploy-local.ps1` **non toccato** *("nessun controllo nuovo")*, `verify-patch-targets.ps1` **non toccato** *(è il gate dei bersagli Harmony: blocca il deploy se un tipo manca, **funziona e serve**)*. La DLL in gioco **non è mia**: è la build di `50c8ca9`, deployata alle `23:48:02` da una tornata precedente.
- ✅ **Quello che ho fatto**: **lo spostamento** del file (`tools\marker-check.ps1`), **i percorsi** (nuova base), **il rimando** in `scratch\`, ⭐ **la cancellazione di `tools\verify-deploy.ps1`** *(§⑥: aggiunta dell'utente nella stessa tornata)*, **questa entry**. ⚠️ **La logica del check è identica**: stesse due codifiche *(UTF-16LE e UTF-8)*, stessi `exit 0`/`exit 1`, **nessun controllo aggiunto**, **nessun marker atteso cambiato**.

### ⑤ 📌 Lo stato — e i due limiti che restano dichiarati

- ✅ **Lo strumento ora è versionato**: `git add tools/marker-check.ps1` **non serve più ricordarlo**, e la voce «la casa» della lista corta **perde due pezzi di rischio** *(lo strumento non versionato, e lo strumento che mentiva — §⑥)*. ⚠️ **Resta aperto il resto della casa**: lo **zip per `G1` che non esiste**, e `deploy-local.ps1:42`/`:48` che copiano da `bin\Debug` **a mano** *(una build `Release` + deploy mette in gioco la `Debug` vecchia, §3.1 della sezione del `09:00`)*.
- ⚠️ **Limite 1 — nessun elenco di marker**: i marker si passano a mano *(invariato: **non** è stato aggiunto un elenco)*. Un gate automatico è un'altra cosa, e **non è questa tornata**.
- ⚠️ **Limite 2 — `-File` non passa un array**: `pwsh -NoProfile -File tools\marker-check.ps1 -Marker 'a','b'` prende **un solo valore** ⇒ l'array di marker passa **solo** col call operator `&`. È scritto nell'intestazione del file.
- ✅ **Aggiunta, non riscrittura**: **solo righe in fondo**, **0 righe cancellate**, **nessuna sezione precedente toccata**.

### ⑥ 🗑️ **E `tools\verify-deploy.ps1` È STATO CANCELLATO** — *la ragione non è «era vecchio», è **che mentiva***

> **Deciso dall'utente nella stessa tornata**: *«sì, fallo assieme, così togliamo tutto di mezzo in un commit»*. ⇒ **una sola tornata, un solo commit.**

- **Cosa è stato cancellato**: `tools\verify-deploy.ps1` — **290 righe**, **10.977 byte**, `sha256 63dccd11e08c3967792feecd7bd62b4e5a113ea5567b5c7682c6ef6a7d2f4530` *(recuperabile da `git show c061249:tools/verify-deploy.ps1`, e da qualunque checkout precedente)*.
- ⭐ **Il difetto che ha deciso**: **stampa *"Puoi testare"* anche su una DLL sbagliata** *(exit 0)* ⇒ ⚠️ **e noi l'abbiamo usato per giorni**: uno strumento che mente **non viene ignorato, viene usato** — ed è esattamente quello che è successo *(`[2026-10-17 23:40]` §4 voce 3)*. E **non è un caso isolato**, sono tra loro indipendenti:
  - **i 9 marker sono fermi all'`2026-10-08` → `2026-10-12`** *(`:59-73`)* ⇒ **il livello dei marker non può fallire**: ogni build degli ultimi ~250 commit li contiene tutti *(§3.1 del `09:00`, che li elenca)*;
  - **l'hash è vincolante solo con `-ExpectedHash`** *(`:181-194`)* ⇒ **senza quello switch il check dell'hash non c'è**;
  - **senza la sonda `scratch\cecil` i marker si saltano in silenzio** *(`:216`)* ⇒ qualsiasi build recente stampa *"Puoi testare"* *(`:280`)* **ed esce 0**.
- ⛔ **Perché non «aggiustarlo»**: *aggiustarlo = aggiungere controlli*, ed è **la cosa che l'utente non vuole**. ✅ **Il sostituto esiste già ed è quello che si è sempre usato quando contava**: `tools\marker-check.ps1` — verifica il **CONTENUTO della DLL deployata** *(UTF-16LE **e** UTF-8 sui byte grezzi)* e **sa dire NO** *(§③: `exit 1` sul controllo negativo)*.
- 📌 **Chi lo citava, e cosa è stato aggiornato** *(`git grep verify-deploy` su tutti i file tracciati, **132**)*:
  - `docs\RIPRISTINO.md` — **l'unico file che lo INVOCAVA** ⇒ aggiornato in **4 punti**: la procedura di ripristino *(`:24` → `marker-check.ps1`)*, l'avviso *«deve uscire 0»* *(`:36`)*, la **storia** del checkpoint `checkpoint-stabile-falo` *(`:160`, con una nota che dice che lo strumento non esiste più)* e la procedura del **nuovo punto di ripristino** *(`:188`)*;
  - `docs\TODO.md` — **solo citazioni storiche** *(le sezioni del `12.`, le entry del `2026-10-17` e del `09:00`)* ⇒ ⛔ **NON toccate**: restano come storia, e **questa §⑥ è la riga che dice come stanno le cose adesso**;
  - **nessun altro**: `deploy-local.ps1` **non lo chiamava** — chiama `verify-patch-targets.ps1` *(`:79-86`)*, che ⛔ **resta e non è stato toccato**: è **il gate dei bersagli Harmony** *(blocca il deploy se un tipo o un metodo mancano, perché un errore lì uccide `PatchAll` a runtime)*.
- ⭐ **Da ora la verifica è `tools\marker-check.ps1`**, e per l'**hash** dei binari si usa lo strumento di sistema: `Get-FileHash <file> -Algorithm SHA256` *(è quello che stampa anche `marker-check.ps1`, con `size` e `mtime`, in testa a ogni run)*. ⇒ **`tools\` resta con i tre script giusti**: `deploy-local.ps1` *(l'unico modo di mettere qualcosa in gioco)*, `verify-patch-targets.ps1` *(il gate dei bersagli)*, `marker-check.ps1` *(il contenuto)* — più `install-mod-loader.ps1`.

## [2026-10-19 00:30] 📌 **`R0` È MISURATA — E IL TESTING CHE MANCA ORA HA UNA LISTA** — la mappa si apre davvero sulla macchina che riceve la pausa *(in test, dall'utente)* ⇒ il residuo «`R0` **non misurata**» della sezione del **`09:00`** è **superato** *(e quella sezione resta com'è)*; più **la lista degli osservabili** del testing che si farà **in produzione**, e **una voce che resta da fare su un altro file** *(`BUG-NOTI` voce 7, firma vecchia del marker)*

> **Questa entry è piccola di proposito, e fa due cose più una che NON fa.** Dichiara **misurato** ciò che il `09:00` dichiarava **dedotto**; scrive **la lista del testing post-produzione** *(per non doverla ricordare a memoria)*; e **registra senza eseguirla** la sola modifica che serve in `docs\BUG-NOTI.md`.

### ① ✅ **`R0` È MISURATA — e lo stato si supera QUI, non là**

- ⭐ **La richiesta dell'utente, alla lettera**: *in test, **la mappa si apre davvero** sulla macchina che riceve la pausa.* ⇒ **non è più una deduzione**: è **un comportamento visto**.
- ⇒ ⭐ **quindi**: il residuo «**`R0` non misurata**» — quello dichiarato nella sezione del **`09:00`** *(`:7386`, §④ «le cose da non dimenticare», e §6 #1 del referto della pausa) — **è superato**.
- ⚠️ **E NON si corregge quella sezione**: il `09:00` **resta com'è, parola per parola** *(è **storia**: la forma è la stessa già usata al `00:15` §⑥ per le citazioni storiche di questo file)*. ⇒ **è questa la riga che dice come stanno le cose adesso.**
- ⭐ **Detto chiaro, nella forma in cui va letta**: «**R0 = APERTURA DELLA MAPPA SUL PASSIVO: MISURATA, FUNZIONA**».
- ⚠️ **E resta vero che è rimovibile** *(una sola chiamata: smettere di chiamare `TryOpenMapOntoPassive`)* — **ma ora non c'è motivo di toglierla**: la ragione del «si toglie in una riga» era **il rischio che non funzionasse**, e quel rischio **è caduto**. La rimovibilità resta **un'informazione**, non un'opzione in tavola.

### ② 🟡 **IL TESTING POST-PROD — la lista** *(gli osservabili: cose **implementate e non ancora viste** in gioco)*

> ⚠️ **La decisione dell'utente**: **si va in produzione**, e **il testing che manca si fa lì**. ⇒ **la lista sta qui, perché a memoria si perde.**

- 🟡 **I 3 NEMICI** — `BellGhostVariant` · `RangedBoomerang` · `MeltedLady` ⇒ ⚠️ **APERTO, da testare in gioco**.
  - ⭐ **La nota che rende il test corto**: **il canale dei proiettili funziona** — **provato**, con **un altro nemico** *(un proiettile di `G2` ha colpito `G1` e l'ha ucciso)*. ⇒ **non si sta mettendo in dubbio il canale**: si sta guardando **una cosa sola**, la risoluzione del nome.
  - ⭐ **Cosa si guarda, esattamente**: questi **3** hanno i prefab **nei propri campi** *(`public GameObject projectilePrefab`, e li passano a `ReuseObject`)* e la **strada ②** *(la sorgente dei nemici)* li cerca **lì** ⇒ **va visto** se il ricevente li trova. Il marker risponde da solo: `[FxRelay] RISOLTO "<nome>" da=nemici in=…ms` *(procedura e cosa leggere: `REFERTO-STRADA-RISOLUZIONE.md` §5.2; se invece esce `NON RISOLTO … provate: boss,nemici,penitente`, **quel** prefab non sta in nessuno dei tre posti — ed è un'informazione, non un silenzio)*.
- 🟡 **TUTTI I BOSS** ⇒ due cose diverse, entrambe **per boss**: la **mira** *(23 ancora sul padrone)* e la **morte recitata** *(12 su 17)*. *(Sono la **voce 8** di `BUG-NOTI`: **limiti dichiarati**, non difetti in lavorazione — e il test in massa è **proprio quello che manca**.)*
- 🟡 **IL DOPPIONE A ~14 UNITÀ** — le due macchine posizionano **lo stesso attacco in punti diversi** ⇒ ⚠️ **non è un problema per l'utente** *("non ho notato incoerenze")*, **ma resta un osservabile**: va guardato, non dato per chiuso per cortesia.
- ✅ **`R0`** ⇒ **fatto**: misurata *(§①)*. ⇒ **esce dalla lista**, e ci resta solo come riga chiusa.
- ⭐ **E la riga che tiene insieme tutto**: questi sono gli **osservabili** — cose **implementate e non ancora viste** in gioco. ⚠️ **Non sono difetti aperti**: sono **misure mancanti**, e il posto dove si prendono è **la produzione**.

### ③ ⚠️ **E UNA COSA DA FARE — ma in un ALTRO commit**: allineare `docs\BUG-NOTI.md` **voce 7**

- ⚠️ **Il fatto**: la **voce 7** di `docs\BUG-NOTI.md` *(`:418`)* cita ancora **la vecchia firma** del marker:
  `[FxRelay] non risolto prefab="<idem>" da=<peer>: non e' nel catalogo del boss locale (N nomi)`.
- ⇒ ⭐ **quella firma NON esiste più**: `36a8878` l'ha **sostituita**, e adesso le righe sono due:
  `[FxRelay] RISOLTO "<nome>" da=<boss|nemici|penitente|cache> in=<ms>ms …` e
  `[FxRelay] NON RISOLTO "<nome>" provate: boss,nemici,penitente …`
  *(`NON RISOLTO` non è più filtrata dal tetto di una riga al secondo: **una riga per nome per scena**, e nessuna si perde.)* ⇒ **e il vecchio testo non è solo vecchio: sarebbe falso** — il catalogo non è più «solo quello del boss».
- 📌 **Quindi va aggiornata**: **una riga**, e **la mappa vecchio→nuovo è già scritta** — `REFERTO-STRADA-RISOLUZIONE.md` **§4.2** *(la sostituzione dichiarata)* e **§4.1** *(i campi nuovi: `da=`, `in=`, `mittente=`, `provate:`)*.
- ⛔ **NON in questo commit**: `docs\BUG-NOTI.md` **non è stato toccato** *(divieto esplicito di questa tornata — è un altro file, e va chiesto)*. ⇒ **questa voce è il segnaposto, non il lavoro.**
- ⚠️ **Perché conta**: la voce 7 è **la pagina che si legge quando un log non torna**. Se la firma è quella vecchia, chi cerca quella riga **non trova niente** e conclude che il canale è morto — cioè **il contrario** di quello che il log sta dicendo.

### ④ 📌 **LO STATO DI QUESTA SEZIONE**

- ✅ **Aggiunta, non riscrittura**: **solo righe in fondo**, **0 righe cancellate**, **nessuna sezione accorciata**. ⇒ ⚠️ **in particolare il «non misurata» del `09:00` (`:7386`) e il suo §④ restano INTATTI**: si superano **qui**, non là.
- ⛔ **`docs\BUG-NOTI.md` non toccato** · ⛔ **nessuna riga di codice toccata** · ⛔ **nessun build e nessun deploy**: **la mappa che si apre è la prova dell'utente**, presa in gioco.
- 📌 **Le fonti di questa entry**, e nessuna delle due è nel repo *(`scratch\` sta **un livello sopra** la radice)*: `scratch\REFERTO-PAUSA-CONDIVISA.md` **§6 #1** *(dove `R0` era «non misurata» — 🟡, con la nota che è **l'unico pezzo che si toglie**)* e `scratch\REFERTO-STRADA-RISOLUZIONE.md` **§4.1-§4.2** *(la firma nuova e la mappa vecchio→nuovo che serve alla voce 7)*.
- ⚠️ **La data** segue **la serie del file** *(`09:00` → `00:15` → questa)*: è **l'etichetta della entry successiva**, non l'orologio della macchina.

## [2026-10-19 17:20] ⭐⭐ **LA COLPA SULLA VITA È CHIUSA E PROVATA IN GIOCO** — tre scalini fissi, il salvataggio che regge la chiusura del gioco, e **la separazione dal frammento vista dall'utente**; più 🔴 **la banda sulla barra: tentata e RIMOSSA** *(«nada, non funziona»)*, **il checkpoint** che la toglie senza portare via la Colpa, **la regola dei marker** *(il `string.Format`)*, **la lista rifatta** con lo stato vero e ⭐ **la ragione vera dei 4 flag** *(che cambia il fix)*

> **Questa entry chiude due cose e ne sposta una.** Chiude **la Colpa** *(la voce 3 della lista corta del `09:00`: non era mai cominciata, e adesso è **provata in gioco dall'utente**)*; **registra la banda come tentativo fallito e rimosso** *(se non si scrive, si riprova senza sapere perché)*; e **cambia la ragione dei 4 flag** — che era scritta come «un altro nome di flag» ed **era sbagliata**: il fix non è aggiungere un flag, è **propagare `Status.Dead`**.

### ① ⭐⭐ **LA COLPA SULLA VITA — CHIUSA E PROVATA IN GIOCO** *(l'utente l'ha testata)*

- ✅ **Tre scalini fissi, niente curva**: **1 morte → −20%** *(`Life.MaxFactor = 0.8`)* · **2 → −40%** *(`0.6`)* · **3+ → −60%** *(`0.4`, col **tetto fermato nel codice**: il pavimento sta dentro `LifeGuilt.Factor()`)*.
- ⭐ **La curva NATIVA misurata, per riferimento** *(e non è la nostra)*: **`−17,0 % · −9,7 % · −3,3 % · −5,0 %`** del massimo pieno *(`scratch\DIAGNOSI-COLPA-CURVA.md:35`, cumulato `−17,0 · −26,7 · −30,0 · −35,0`)* ⇒ ⭐ **non è estrapolabile**: il **4° scalino è più grande del 3°** *(3,0 punti contro 2,0)*, quindi nessuna formula la ricava. ⇒ ⚠️ **la nostra è VOLUTAMENTE diversa**: tre scalini tondi e un tetto, perché è **una regola di gioco** e non **un'imitazione del decompilato**.
- ✅ **Il salvataggio**: **testo puro** in `Modding\data\Multiplayer\life-guilt.txt` *(tre righe di commento e `deaths=N`)* ⇒ **persiste alla chiusura del gioco — provato dall'utente**, non dedotto. Chiudere il gioco **non è una via d'uscita** dalla colpa; l'unica è **il falò**.
- ⭐⭐ **La separazione dal frammento, PROVATA IN GIOCO**: *l'utente ha preso la goccia e **le due colpe sono rimaste separate*** ⇒ ⭐ **il rischio che lo studio temeva NON c'è**: `Life` ha **0 occorrenze** in `GuiltManager.cs` e in `Altar.cs` *(misurato nel referto: `scratch\REFERTO-COLPA-VITA.md:190`)*, e `ResetGuilt` tocca **solo** `Fervour.MaxFactor`. La separazione **non è una guardia**: è **nel tipo** *(due attributi distinti, due `MaxFactor`)*.
- ✅ **La notifica**, **una volta per scalino** *(inglese e ASCII come le altre)*, e **il falò**: il reset è **locale** — **chi si siede lo ha, chi no no** *(`PrieDieu.ShallowActivationLogic`, prefix: la replica non lo chiama mai)*.
- 📌 **E i due residui dichiarati, che restano dichiarati**: **PE02 / Demake** mostrerebbero **meno stock** *(le due varianti ricavano gli stock da `Life.CurrentMax`)* · **la colpa nativa si disattiva in `BOSS_RUSH`/`DEMAKE`** *(`GuiltManager.cs:93`)* **e la nostra no** *(se si volesse imitarla, è **una riga** in `ApplyOnSpawn`)*.
- 📌 **Il costo, com'è stato costruito**: **due righe in due patch che c'erano già** *(morte, spawn)* **+ una patch nuova** *(il falò)* ⇒ **269 → 270 bersagli**, e **zero scritture sulla colpa nativa**.

### ② 🔴 **LA BANDA SULLA BARRA — TENTATA E RIMOSSA** *(va registrata, o si riproverà senza sapere perché)*

- **Cosa era**: un'`Image` **figlia del padre della barra** *(ultima sorella, quindi sopra `health` e `loss`)*, larga **`(1 − fattore) × larghezza`**, colore **`rgba(0.42, 0.05, 0.07, 0.62)`** — sangue scuro semitrasparente. L'idea: con la colpa a `0,8` la barra resta **larga uguale** e si riempie all'80%, quindi **il buco si vede e la penalità no**; la banda doveva disegnare quel buco.
- ⚠️ **L'aggancio, senza reflection**: `PlayerHealth.transform` è l'**unico** handle pubblico *(i due `Image` sono privati)* ⇒ il criterio dato dalla sonda: **`Image.type == Filled`** + **larghezza ≈ `Life.Final`** *(su `rect.width` e, in seconda battuta, su `sizeDelta.x`)*.
- 🔴 **L'esito**: **«nada, non funziona»** *(l'utente, in gioco)* ⇒ ⭐ **rimossa** *(commit di revert `1db74ab`, che toglie **solo la banda** e **lascia la Colpa**)*. È uno di quei casi in cui **il codice gira, il marker c'è, e la cosa non si vede**: nessun `try/catch`, nessuna eccezione — semplicemente **il criterio della sonda non vale su quel prefab**, ed era l'esito dichiarato nella clausola di stop *(«meglio nessuna banda che una banda nel posto sbagliato»)*.
- 📌 **La lezione, e va scritta perché è la parte utile**: *la rappresentazione **testuale** funziona ed è **sufficiente*** *(la notifica dice la percentuale vera)*; **i rovi e la banda sono uno sfizio, non un requisito**. ⚠️ **Non riprovarci senza una ragione nuova**: la prossima volta il criterio d'aggancio va **misurato prima** *(una sonda che stampa le `Image` sotto `PlayerHealth`, come si è fatto per il relay)*, non dopo.
- ✅ **E il codice**: `LifeGuiltBand.cs` **rimosso** *(−656 righe)*, gli altri tre file **tornati a `3b43094`** *(−5 `LifeGuilt.cs`, −15 `Multiplayer.cs`, −11 `PlayerDeathSync.cs`)* ⇒ **0 inserzioni, 687 rimozioni**: l'**inverso esatto** di `536f94a`, che era **687/0**.

### ③ ✅ **IL CHECKPOINT `pre-rovina-vita`** — su **`3b43094`**

- **3 file, 635 inserzioni, ZERO rimozioni**, e **solo codice nostro** *(nessuna libreria, nessun referto)*. È **la Colpa senza la banda**, cioè **lo stato che l'utente ha testato**: se mai servisse tornare, **quello** è il punto.
- ⭐ **E il revert lo conferma**: i tre file dopo `1db74ab` sono **identici byte per byte** a `3b43094` *(`git diff --numstat 3b43094 -- <i tre>` non stampa niente)* ⇒ **non è «una Colpa simile»: è la stessa**.

### ④ ⭐ **LA REGOLA DEI MARKER** — *la lezione del `string.Format`*

- ⚠️ **`$"{MARK} …"`** ⇒ ⭐ **il compilatore usa `string.Format`** ⇒ **nella DLL finisce `"{0} …"`**, non il testo ⇒ ⭐ **il marker è nel SORGENTE e ASSENTE al byte-check**. Non è un errore di `marker-check`: è **il compilatore che ha ragione**.
- ✅ **La regola**: *i marker si scrivono **per concatenazione*** — **`MARK + " …"`**, che è **una costante a sua volta** e finisce nella DLL **come UN letterale** *(è il motivo per cui `LifeGuilt` e `PauseNotice` sono scritti così)*.
- ⚠️ **E il corollario, che fa risparmiare un pomeriggio**: *se un marker **«sembra presente»** nel sorgente ma `marker-check` dice **ASSENTE**, **è questa la causa***. Prima di cercare un deploy sbagliato o una DLL vecchia, **si guarda com'è scritta la stringa**.

### ⑤ ⚠️ **`scratch` NON È GITIGNORED** — *è **fuori dal repo**, e basta*

- **Il fatto**: `scratch\` non sta nella radice del repo, sta **un livello sopra** *(`D:\Codificando\scratch`)* ⇒ **non ha bisogno di essere ignorato**, e **infatti non lo è** *(non c'è nessuna riga per lui in `.gitignore`)*.
- ⭐ **E va bene così, è la forma voluta**: **l'utente vuole nel repo solo il codice della mod** — **niente librerie, niente referti**. ⇒ 📌 **conseguenza pratica**: i referti **non sono versionati**, quindi **il TODO è l'unico posto in cui una conclusione sopravvive a un `git clone`** — ed è **la ragione per cui questa entry è lunga**.

### ⑥ 📋 **LA LISTA, RIFATTA** — *con lo stato vero*

| # | cosa | stato | perché è qui |
|---|---|---|---|
| **1** | 🟡 **I 2 `InstantiateArea`** *(`BossJump` · `BossTeleport`)* | **270 → 272** *(+2 hook)* | ⭐ **è la parte residua della «strada di risoluzione»**: il resto *(multi-sorgente boss → nemici → penitente → cache)* è **fatto** in `36a8878` |
| **2** | 🟡 **I 4 FLAG** *(`Amanecidas` · `Perpetua` · `Pontiff Husk` · `Viridiana`)* | aperto | ⚠️ **la ragione è cambiata**: non è «un altro nome di flag» ⇒ **vedi ⑦**, che è **la parte che cambia il fix** |
| **3** | 👀 **Gli osservabili in produzione** | da guardare | *i **3 nemici** · **i boss** (la mira e la morte recitata) · **il doppione a ~14 unità*** — cose **implementate e non ancora viste** |
| **4** | ✅ **La Colpa sulla vita** | **CHIUSA** | provata in gioco dall'utente ⇒ **esce dalla lista** *(`§①`)* |
| **5** | ✅ **La banda sulla barra** | **RIMOSSA** | tentata e non visibile ⇒ **esce dalla lista** *(`§②`)* |
| **6** | ⛔ **Il lavoro per boss** | **esce dalle cose da fare** | ⭐ **è un OSSERVABILE, non un lavoro**: la forma è provata su `ElderBrother` e il resto è **misura in produzione** ⇒ va nella riga **3** |

- ⚠️ **E il conteggio dei bersagli, che è la bussola**: **270 adesso** *(269 + la patch del falò)*; i **2 `InstantiateArea`** porteranno a **272**. ⭐ **La banda non ha mai aggiunto un bersaglio** *(`536f94a` era 270 → 270)*, quindi **il revert non muove il numero** — ed è **la prova** che non si è portato via nessun gancio Harmony.

### ⑦ ⭐⭐ **I 4 FLAG — LA RAGIONE VERA** *(va scritta: cambia il fix)*

- ⛔ **Non è un bug di C#**, e non è nemmeno «il flag ha un altro nome» *(che era la lettura della lista del `09:00`: `SANTOS_AMANECIDA_*_DEFEATED`, `VIRIDIANA_DEAD_BOSS01…05`)*. La verità è più semplice e più scomoda: ⭐ **quei 4 NON HANNO un flag di morte «classico»**.
- ⭐ **Perché**: sono **DLC o eventi aggiuntivi**, **iniettati quando l'architettura dei boss era già sigillata**. ⇒ ⚠️ **per non toccare i salvataggi** *(un flag nuovo in un salvataggio vecchio è un campo che non esiste)*, gli sviluppatori usano lo **stato locale** *(`Status.Dead`)* e **sotto-sistemi separati**: **il contatore di Jibrael** per le Amanecidas, **il percorso arcade** per Husk, **lo script di Viridiana**, **l'incontro irripetibile di Perpetua**.
- ⭐⭐ **E la conseguenza per il co-op, che è il punto**: **`Status.Dead` è ATOMICO** — non è un flag che si può «leggere dal salvataggio» né ricavare dalla scena. ⇒ **se NON viene propagato**, l'entità sull'altro client **può restare in un loop di idle/transizione** che **nessun flag riuscirebbe a sbloccare**, perché **non c'è nessun flag da leggere**.
- ⇒ 📌 **Il fix non è «aggiungere un flag»: è PROPAGARE `Status.Dead`** per quei 4. ⭐ **E il progetto ha già il canale**: **la morte si sincronizza** *(provata in gioco)* — quindi non serve un canale nuovo, serve **lo stesso canale applicato a quattro entità che oggi non lo usano**.
- ⚠️ **La riga che evita il lavoro sbagliato**: chi apre questa voce e legge «4 flag» **proverà ad aggiungere 4 flag**, e **non funzionerà** *(non c'è dove leggerli)*. La forma giusta è **la propagazione**, e si misura con **lo stesso marker della morte** — non con un `[BossCorpse]` nuovo.

### ⑧ 📌 **LO STATO DI QUESTA SEZIONE**

- ✅ **Aggiunta, non riscrittura**: **solo righe in fondo**, **0 righe cancellate**, **nessuna sezione precedente toccata** — comprese **la lista corta del `09:00`** *(`:7349-7359`, dove la Colpa è la voce **3** «mai cominciata» e i 4 flag sono la voce **5**)* e **la lista degli osservabili del `00:30`** *(`:7471-7493`)*, che **restano intatte e leggibili come storia**. ⇒ ⭐ **si superano QUI, non là**.
- ⛔ **`docs\BUG-NOTI.md` NON è stato toccato** — e la voce 7 *(firma vecchia del marker)* **resta il segnaposto del `00:30` §③**, non è stata fatta in questa tornata.
- 📌 **I due commit di questa tornata, dichiarati**: **`1db74ab`** — il **revert** *(4 file, **687 rimozioni**, 0 inserzioni)*; e **il commit del TODO** — **questo file, in fondo**. Sono **due** e non uno perché il revert è **una cosa che si può voler annullare da sola** *(un `git revert 1db74ab` la rimette)*, mentre il TODO è **la registrazione di com'è andata**.
- ✅ **Build, deploy e marker sono stati fatti DAVVERO in questa tornata** *(a differenza del `00:30`, dove la prova era l'utente)*: build **Debug della SOLUTION** ⇒ `Compilazione completata.`, **Errori 0**, **Avvisi 438** *(la baseline)* · deploy ⇒ **270 bersagli, tutti validi** · `tools\marker-check.ps1` sulla **DLL deployata** *(sha256 `8a3d618f…`, **465408 byte** — la **stessa dimensione** della build di `3b43094`)* ⇒ **9 marker della Colpa PRESENTI su 9** *(`[ColpaVita] morte=`, `applicata a ogni spawn`, `reset al falo'`, `salvato`, `caricato`, `notifica`, più `[Colpa]`, `[Morte]`, `[Falo'']`)* e **7 marker della banda ASSENTI su 7** *(exit 1, l'esito atteso)*.
- 📌 **Le fonti di questa entry**, e **nessuna è nel repo** *(`scratch\` sta un livello sopra la radice, §⑤)*: `scratch\REFERTO-COLPA-VITA.md` *(la Colpa: i tre ganci, il file, la verifica sul frammento, i residui `:421-431`)*, `scratch\REFERTO-BANDA-VITA.md` *(la banda: l'aggancio, la geometria, il colore)*, `scratch\DIAGNOSI-COLPA-CURVA.md` *(la curva nativa `:35`, `:170`)* e `scratch\REFERTO-REVERT-BANDA.md` *(cosa è stato rimosso e come si è verificato che la Colpa è intatta)*.
- ⚠️ **La data** segue **la serie del file** *(`00:15` → `00:30` → questa)*: è **l'etichetta della entry successiva**. ⚠️ **Ma qui c'è un salto**: i due commit di questa tornata sono datati **`15:48`** *(la Colpa)*, **`16:59`** *(la banda)* e **`17:17`** *(il revert)* — quindi **l'ordine del file e l'orologio della macchina, per una volta, coincidono**.

---

## [2026-10-19 18:00] ⭐⭐ **LA DECISIONE È PRESA: IL SERVER NON HA MEMORIA, E DEVE RESTARE COSÌ** — è un **relay**, non un magazzino: il team che si svuota è **la regola**, non un difetto, e **la voce 11 del `BUG-NOTI` è stata RITIRATA** *(`05a3209`)*

> **Questa entry registra una DECISIONE, e insieme registra un file che torna indietro.** L'utente ha deciso che il caso
> *«l'ultimo che esce porta via l'archivio del team»* **non è un bug e non è nemmeno una cosa da testare**: è **il
> comportamento voluto**, con una ragione che va scritta perché è la parte che conta. ⇒ **una regola architetturale va
> qui, non nel `BUG-NOTI`** — quel file dichiara il suo criterio in apertura *(ci vanno i difetti che non si riproducono
> e i prezzi accettati)*, e una regola messa lì **sembrerebbe un difetto aperto o un lavoro rimandato**, che non è. E
> **la voce 11**, aggiunta come *«meccanismo verificato · conseguenza da testare»*, è stata **ritirata**: ⇒
> **`docs\BUG-NOTI.md` è identico byte per byte a `19773ed`**, cioè allo stato *prima* che quella voce esistesse.

### ① ⭐ **LA REGOLA** — *il server è un relay: archivia in RAM e ripete, non conserva*

- ⭐ **Archivia in RAM**: `teamGameDatas` è un `private static readonly Dictionary<byte, TeamInfo>` **in memoria**
  *(`Core.cs:11`)*, e `TeamInfo` **è** il progresso — 14 dizionari *(`TeamInfo.cs:7`)*, flag compresi — che riempie
  `AddTeamProgress` *(`:22-43`, chiamata a `Server.cs:591`)*.
- ⭐ **E ripete a chi si connette**: due pacchetti dalla **stessa** memoria — il dump al joiner
  *(`sendPlayerTeam`, `Server.cs:396-412`, col nome `"*"`)* e il relay ai presenti *(`:662` → `sendPlayerProgress`, col
  nome del mittente)*.
- ⛔ **E non conserva NIENTE quando il team si svuota**: `removeUnusedGameData` *(`Core.cs:68-91`, `teamGameDatas.Remove(i)`
  a `:88`, con la riga `Removing game data for team N` a `:87`)* cancella l'archivio **quando nessun giocatore connesso ha
  quel team**; è chiamata a `Server.cs:143` *(subito dopo `connectedPlayers.Remove` a `:142`)* e a `:421` *(dopo
  `sendPlayerTeam`)*.
- ⛔ **E non esiste NESSUNA persistenza, in nessun punto**: in tutto il progetto server **zero** `ReadAllText`,
  `StreamReader`, deserializzatori; gli unici `File.*` sono in `ServerLog.cs` *(`:54`, `:83`, `:97`)* e scrivono **solo il
  log**, che nessuno rilegge. ⇒ **riavviare il server azzera tutto** e **l'ultimo che esce azzera il team**: sono lo
  **stesso fatto**, ed è **il disegno**.
- 📌 **Quindi `removeUnusedGameData` è CORRETTA, e va lasciata così**: non è una riga da correggere, è **la riga che
  realizza la regola**.

### ② ⭐ **LA RAGIONE** — *una memoria del server mescolerebbe i salvataggi di sessioni e giocatori diversi*

- **Il team è un contenitore mutevole**: gli stessi slot, in giorni diversi, sono **giocatori diversi con salvataggi
  diversi**. Il numero del team **non dice di chi** sono i dati.
- ⇒ **una memoria del server mescolerebbe** i salvataggi di **sessioni differenti** e di **giocatori differenti** ⇒
  ⚠️ **incoerenze che nessuno può ricostruire**: un boss che risulta morto a chi non l'ha ucciso, o un progresso che salta
  in una sessione che non l'ha mai fatto.
- ⭐ **E il punto che rende la scelta asimmetrica**: *un flag perso si ricostruisce dal save* *(§③)*, **un flag sbagliato
  no**. ⇒ il rischio peggiore non è «non avere il flag»: è **attribuire a un salvataggio il contenuto di un altro**.

### ③ ✅ **LA CONSEGUENZA, dichiarata — ed è CORRETTA**: *la verità è il salvataggio del giocatore*

- **Il fatto** *(descrizione, non difetto)*: un joiner che entra dopo che **tutti** sono usciti **non trova il flag** —
  `getTeamData` ricrea un `TeamInfo` vuoto *(`Core.cs:57-66`, riga `Creating new game data for team N`)* e
  `sendPlayerTeam` non ha **nessuna chiave** da mandare.
- ⇒ ⭐ **vede il boss come glielo dice il SUO save**: se il suo save dice morto il boss non c'è *(`HideDefeatedBosses` legge
  `FirstBossDeadFlagOn`, `EnemySyncManager.cs:966`)*; se dice vivo, è vivo. ⇒ ⭐ **ed è giusto così: la SUA verità è il
  SUO salvataggio.** Il server non è un'autorità sul mondo — è **un corriere**, e non deve correggere il save di nessuno.
- ⭐ **E il posto della verità sono i SAVE DEI CLIENT**: ogni client **rispinge i propri flag** — all'ingresso in scena
  *(`Multiplayer.cs:177`)* e alla connessione *(`:452`)*, via `SendAllProgress` *(`FlagHelper.cs:22-25`)* →
  `GetCurrentPersistentState("intro")` → il `Prefix` che li manda a **valore 0** *(`:48-69`)* — e il server **li
  riarchivia** *(`Server.cs:591`)*. ⭐ È esattamente ciò che dicono i log della voce 10: `D17Z01_BOSSDEAD` non è arrivato
  da una kill, è stato **spinto dal save di G2** alla connessione *(`ServerOutput.log:8` e `:27`)*.
- ⇒ 📌 **quindi lo stato SI RICOSTRUISCE DAL GIOCATORE, non dal server**: il server riparte vuoto e **nel giro di una
  connessione torna pieno** — di quello che i giocatori **sono**, non di quello che **sono stati**.

### ④ ⭐ **IL CONFINE** — *dentro la sessione funziona, quindi la memoria non serve*

- ✅ **La sincronizzazione DENTRO UNA SESSIONE funziona, ed è provata**: il flag viaggia **a sessione aperta** — misurato:
  G2 accende `D17Z01_BOSSDEAD` **a metà partita** *(riga 3713)* e G1 lo riceve **mentre combatte** *(riga 4000, **3191
  righe dopo** la connessione)* *(`scratch\DIAGNOSI-FLAG-SESSIONE-APERTA.md` §(a))*.
- ✅ **E il flag ricevuto entra nel save di chi lo riceve**: `ApplyProgress` chiama `Core.Events.SetFlag(id, value == 0,
  false)` *(`FlagHelper.cs:13`)*, e il terzo parametro è **`forcePreserve`** *(new game+)*, **non** «non salvare» ⇒ il flag
  finisce nei `flags` del client, che il gioco **persiste nel save**. ⇒ **chi era connesso alla kill ce l'ha in casa.**
- ⇒ ⭐ **l'unico caso senza flag è «team vuoto»**, cioè **nessuno che possa ricordare** — ed è **voluto**. ⇒ **la memoria
  del server non serve**, nemmeno per *«un joiner che entra dopo una kill della stessa sessione»*: chi era dentro ce l'ha
  nel proprio save, e chi entra dopo **rispinge i propri** *(§③)*.

### ⑤ 📌 **IL «FIX»: NESSUNO** — *non c'è niente da fare, perché è una scelta*

- ⛔ **Nessun fix e nessun test**: non c'è niente di sospeso. `removeUnusedGameData` è **corretta** *(§①)* e **resta com'è**;
  una run non deciderebbe niente, perché **la decisione è già presa** e non è una misura.
- ⚠️ **E va detto il resto**: **se un giorno** si volesse una memoria, sarebbe **un CAMBIO DI DESIGN, non un fix** — e
  avrebbe due forme, **nessuna gratis**: una **grazia** *(il team sopravvive N minuti)* o un archivio dei **soli flag**.
  Entrambe rendono il server **stateful fra sessioni** e chiedono **una regola di appartenenza** dei dati — *per team? per
  coppia di giocatori? per save?* — **che oggi non esiste**. ⇒ 📌 **finché quella regola non c'è, la risposta giusta è
  quella di adesso: nessuna memoria.**

### ⑥ ⚠️ **LA VOCE 11 È STATA RITIRATA** — *e il `BUG-NOTI` è tornato a `19773ed`*

- 🔴 **Cos'era**: `53ddbef` aveva aggiunto **130 righe** — la **voce 11** *(«il team che si svuota») dichiarata **non un
  difetto*** *ma anche* ***«conseguenza da testare»*** — e **una riga di rimando dentro la voce 10**. ⇒ ⚠️ **il problema era la
  formulazione**: *«meccanismo verificato · conseguenza da testare»* **si legge come un difetto aperto**, e «da testare»
  chiede una run che non serve.
- ✅ **Cosa è stato fatto**: **`git revert 53ddbef`** ⇒ commit **`05a3209`**, *1 file, **130 rimozioni, 0 inserzioni***.
- ✅ **La verifica richiesta, e il suo esito**: `git diff 19773ed HEAD -- docs\BUG-NOTI.md` ⇒ **vuoto** ⇒ il file è
  **identico byte per byte** a com'era prima della voce 11. ⇒ **voci 1-9 intatte**, **voce 10 identica a `19773ed`**
  *(senza il rimando)*, **voce 11 assente**.
- 📌 **E perché non tenerla, riscritta bene**: perché una **regola** lì **non ci va** *(`BUG-NOTI` accoglie i difetti che non
  si riproducono e i prezzi accettati)* — e riscritta come *«è voluto»* sarebbe stata comunque **una regola nel posto
  sbagliato**, con l'aria di un prezzo pagato. **La sua casa è questa entry.**

### ⑦ 📌 **LO STATO DI QUESTA TORNATA**

- **Due cose, due commit** *(entrambi solo `docs\`)*: **`05a3209`** — il **revert** del `BUG-NOTI` *(130 rimozioni)*; e **il
  commit di questa entry** — **questo file, in fondo**. Sono due e non uno perché il revert **si può voler annullare da
  solo** *(un `git revert 05a3209` la rimette)*, mentre il TODO è **la registrazione di com'è andata**.
- ✅ **Solo aggiunte in coda al TODO**: **0 righe cancellate**, nessuna sezione precedente toccata.
- ⛔ **Codice non toccato** *(nessun file sotto `Blasphemous.Multiplayer-main\` in `git status`)* e ⛔ **nessun test da fare**
  *(§⑤)*.
- 📌 **Le fonti, e nessuna è nel repo**: `scratch\DIAGNOSI-FLAG-SESSIONE-APERTA.md` *(le quattro risposte: (a) il flag a
  sessione aperta arriva — misurato; (b) viene archiviato nella stessa `TeamInfo`; (c) il dump legge l'istanza viva; (d)
  la kill in co-op accende il flag)* e `scratch\BUGNOTI-VOCE11.md` *(il referto della voce ritirata: **le righe misurate
  restano valide**, è la **cornice** che cambia — da «da testare» a «deciso»)*.
- ⚠️ **La data** segue la serie del file *(`17:20` → questa)*, e questa volta **l'orologio della macchina le va dietro**: il
  revert `05a3209` è datato **`17:58`**.

## [2026-10-19 19:00] ⭐ **I DUE `InstantiateArea` SONO CHIUSI — 2 `Postfix`, 270 → 272**, e ⚠️ **DUE CORREZIONI AI REFERTI** *(il «2» è **1**; e «nessun catalogo li può vedere» è **falso**)*

- ✅ **Il fix** *(branch `fix-f1a`, da `07a1fd4`)*: **due `Postfix`** — `BossJumpAttack.InstantiateArea` e `BossTeleportAttack.InstantiateArea` — in **`Blasphemous.Multiplayer.Client\EffectRelayInstantiateArea.cs`** *(file nuovo: **nel relay non è stata toccata una riga**)*. Il corpo è **una riga**: `EffectRelay.OnPoolSpawn(prefab, __instance.transform.position, Quaternion.identity)` ⇒ **filtro, freno, dedup, payload e invio restano dove sono** *(nessuna copia)*. Tutto in `try/catch`: un hook non deve mai rompere un attacco.
- ✅ **La posizione, che non è nella firma** *(`private void InstantiateArea(GameObject)`)*: `__instance.transform.position`, che **coincide in 13 siti su 14**; ⚠️ **il 14º è `JarThrower`** *(`Assembly-CSharp.cs:107392` passa `JarThrower.transform`, non il padre)* ⇒ per quel chiamante la posizione trasmessa è **quella del lanciatore**: **limite dichiarato**, non corretto qui.
- ✅ **Il costo, misurato**: **270 → 272** bersagli *(=`verify-patch-targets.ps1` sulla DLL deployata: **272**, tipo assente 0, metodo assente 0, «tutti validi»)*. Deploy fatto; il marker `[FxRelay] hook InstantiateArea: prefab=… da=<BossJump|BossTeleport>` è **nella DLL deployata** *(byte-check, non hash; scritto **per concatenazione**, §④ del `17:20`)*.
- 🔴 **Il rischio, dichiarato**: il filtro del **mittente** è `Weapon|Projectile` ⇒ **se il prefab trascinato in quei campi è cosmetico, il pacchetto non parte**: l'hook scatta, scrive la sua riga, e non manda niente *(innocuo, ma muto)*. Il test lo distingue: `RISOLTO` **con** `istanzio` sul peer = funziona; `RISOLTO` **senza** `istanzio` = cosmetico. **Fonti** *(non versionate)*: `scratch\REFERTO-2-HOOK.md`, `scratch\ARCHIVIO-2-INSTANTIATEAREA.md`.

### ⚠️ Le due correzioni — *i referti dicevano altro*

| # | dov'era scritto | diceva | è |
|---|---|---|---|
| **1** | `:6825` («i **2 `Object.Instantiate`** degli oggetti del salto») · `:7556` · e la stessa frase in **5 referti** di `scratch\` | **2** `Object.Instantiate` in `BossJumpAttack` | ⭐ **1**: `instantiateOnStart` è un **campo MORTO** — nessun `ldfld` nel dump IL ⇒ **1 `Instantiate`, 1 chiamante vivo** *(`OnLanded` → `instantiateOnEnd`)*. Il teletrasporto invece ha davvero **2 campi vivi** *(`instantiateOnTeleportIn`/`Out`)*: il conto giusto per i due metodi è **1 + 2 = 3 siti d'istanziazione**, non 2. |
| **2** | `:7078` *(+ `:6910`)* | «i due `InstantiateArea`… **non passano dal pool**, quindi **nessun catalogo li può vedere**» | ⛔ **falso, la seconda metà**: il catalogo del **ricevente** li vede **per costruzione** — i prefab sono **campi `public GameObject` sul componente del boss** *(verificati sulla DLL del gioco: `instantiateOnEnd`, `instantiateOnTeleportIn`, `instantiateOnTeleportOut`)* e `EffectRelay.Harvest` scandisce **tutti** i campi `GameObject`, pubblici e privati. È il **filtro del MITTENTE** *(`Weapon|Projectile`)* che li scarta **se cosmetici**. ⇒ **non era un buco di catalogo: era un buco di mittente** — ed è quello che questi 2 hook chiudono. |

- 📌 **Solo aggiunte in coda**: nessuna riga delle sezioni precedenti toccata, `docs\BUG-NOTI.md` **non toccato** *(le sue voci 7 restano come sono: la chiusura dei buchi si registra qui)*.
- ⚠️ **Il «quasi» che resta, dichiarato**: il ricevente istanzia **dal pool** *(`ReuseObject(…, true, 4)`)*; un prefab **senza `PoolObject`** viene comunque riusato *(verificato)*, ma senza `OnObjectReuse` un effetto che si autodistrugge lascia il pool «bucato» ⇒ **da guardare nel test**, non un difetto noto.
