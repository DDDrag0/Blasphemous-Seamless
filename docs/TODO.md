# TODO — Blasphemous COOP

> **Documento vivo.** Va aggiornato a ogni passo: cosa è stato fatto, cosa ha misurato, cosa resta.
> L'ultima sezione (`Diario dei passi`) è il registro cronologico.

---

## ⚠️ REGOLE DI MANUTENZIONE DI QUESTO FILE — vincolanti

1. **Questo file NON si riscrive e NON si compatta.** La storia non si perde.
2. Gli **aggiornamenti approfonditi** (parametri scoperti, funzioni e metodi da usare, scoperte
   dell'Esploratore) si aggiungono **SOLO IN FONDO**, come nuove entry **con l'orario di aggiunta**.
3. **L'unica parte che si modifica è la LISTA INIZIALE** qui sotto: si **aggiungono** entry e si
   aggiornano **stato e priorità** di quelle esistenti. Nient'altro.
4. **Nessuna sezione esistente si cancella o si accorcia.** Al massimo si annota che è superata,
   indicando dove sta l'informazione aggiornata.
5. **Motivo**: nelle revisioni precedenti alcune voci sono andate perse (fra cui *Sangue e flash nel
   feedback* e *Camminata troncata*). Da ora la lista iniziale è il **punto unico di verità sullo
   stato**, e il corpo del file è **l'archivio che cresce**.

---

## 📋 LISTA INIZIALE — tutte le voci aperte, con priorità

**Legenda**: 🔴 urgente · 🟡 da fare · 🟢 bassa · ✅ fatto · ⏳ in corso · ❌ caduta · ⏸️ parcheggiata

| # | Voce | Priorità | Stato |
|---|---|---|---|
| **1** | **Animazioni tagliate** (nemici, morti, Penitente) — §1a | ✅ **stabile, il residuo è del gioco** | **CHIUSA su decisione dell'utente.** Il residuo (**il `TURN` al cambio bersaglio**) è del **GIOCO**: `TurnAround` → `SetTrigger("TURN")`, ri-triggerato a ogni attraversamento dell'asse x — **`ApplyAnimation` è escluso perché esce quando è già in quello stato e usa `SetBool("WALK")` per la locomozione, che preserva la fase del clip**. La **troncatura NON esiste**: 29 uscite misurate a **0,96-1,00**. E il fix `FindBehaviour` (voce 26) ha ridotto churn e orientamento di **~10-30×**. ⚠️ I numeri vecchi di questa riga *(`animRepeat` 11-36, `avvio=0.00` in 484/499)* **non si vedono più nei test recenti**. Vedi §12 |
| **2** | **Orientamento che si gira velocissimo** — §1b | ✅ **RISOLTO E VERIFICATO** | `orientFlip` **crollato da 85 (MAX 34 in 2 s) a 1 (G1) e 3 (G2), MAX 1**; `orientSkip=0` su entrambe, ora normale perché nessuno scrive più l'orientamento da combattere. **Causa della prima versione**: l'isteresi era in `ApplyOrientation` (scrittura della mod, 20 Hz) invece che sulla Prefix su `Entity::SetOrientation` (dove passa il gioco, 60 Hz). **Confermato stabile dall'utente.** |
| **3** | **Chi si connette dopo non apprende le morti** — §1g | ✅ | **RISOLTO E VERIFICATO**: `[EnemyJoin] annunciate 2`, nessun `ENEMYDEAD` nel progress |
| **4** | **Falò: RESPAWN e REFILL solo locali** — §3 | 🟡 **da confermare — È IL LAVORO PIÙ GRANDE RIMASTO** | ⚠️ **lo stato ACCESO del falò è GIÀ sincronizzato** (`PrieDieuHelper.cs`, 3 patch). Questa voce riguarda **il respawn dei nemici e il refill delle fiale**, che restano **locali**. **Meccanismo accertato in IL**: `EnemySpawner.Reset()` fa **SOLO** `consumedSpawns.Clear()` *(4 istruzioni: **NON fa rinascere nulla**)*; è **`RespawnDeadEnemies()`** che fa `Clear()` @IL_001c → `CreateEnemy()` @IL_002e → `OnEnemiesRespawn`, **e l'ordine giusto è già dentro il metodo**. **Il reload della stanza è da ABOLIRE**: è superfluo *(`SpawnEnemiesOnLoad` rispetta già `consumedSpawns`)* ed è **distruttivo** *(fade, `BLOCK_UNTIL_FPS_STABLE`, teletrasporto del giocatore)*. **La memoria va azzerata su chi si siede:** chi non si siede ricostruisce la stanza dal **proprio save**. **FIALE: NON si propagano** *(decisione dell'utente: toglierebbe al peer la gestione delle sue fiale, è sfruttabile campeggiando, e da solo crea asimmetria)*. **Vedi voci 29/30/31, che sono la STESSA cosa** *(da fondere)*. Vedi §12 |
| **5** | **Sangue e flash nel feedback del colpo** — §12.1 *(`§5c` NON esiste)* | ✅ **RISOLTO — e la diagnosi iniziale era INVERTITA** | **F3 fatto e verificato dall'utente in gioco.** L'ipotesi di partenza era **SBAGLIATA** (verificato in IL): **sparks e sangue nascono dal LATO ATTACCANTE** *(`PenitentSword.Attack` → `GetDamageableEntities` → `GetSwordSparks`, e **solo dopo** applica il danno)*, mentre **suono, flash, dolore e morte nascono dal LATO DIFENSORE**, dentro `<EnemyType>::Damage(Hit)`, che la mod **già** rieseguiva. **Il confine vero è CHI MENA vs CHI GUARDA**, non proprietario/non-proprietario. **E "alcuni nemici non fanno sparks" è CORRETTO**: `sparksOnImpact`/`bleedOnImpact` sono **campi di prefab**. Fix: **replay degli effetti sul percorso di relay** + **3 byte nuovi** nel payload di `EnemyDamageRequest` *(`DamageType`, `DamageElement`, flag)* che correggono anche il **suono** *(prima sempre "simple" su chi guarda)*. **Nessun bump di protocollo**: il server rilancia il payload verbatim. Vedi §12 |
| **6** | **Oggetti distruttibili** — §4 | ✅ **QUASI CHIUSO — manca l'ultima verifica** | **Il tipo è `Tools.Level.Actionables.BreakableObject`** *(lanterne, scheletri appesi, simboli attorcigliati: **la scenografia che respawna**)* — **NON i muri**, che sono **fratelli** e non parenti, hanno vita diversa e sono già coperti. **GUID veri, per-istanza, authored di scena** *(7/7 canonici, 0 duplicati)*: `GetPersistenID()` = `GetComponent<UniqueId>().uniqueId`, e `UniqueId.uniqueId` ha **UN SOLO SCRITTORE** *(il costruttore che scrive `Empty`)* ⇒ **nessuna generazione a runtime** ⇒ **identico sulle due macchine**. **L'identità è il GUID, NON `scena~indice`**: la tabella `PersistentStates` *(371 voci, curata a mano)* **contiene ZERO GUID di BreakableObject**, e completarla è **pericoloso** *(gli indici sono posizionali e **finiscono nei salvataggi** come `scene~index`)*. **Tre patch + un ramo**, e il **crash latente** evitato: `BreakableObject.SetCurrentPersistentState` fa `((BreakablePersistenceData)data).broken` **senza null-check**, e `ProgressManager.LevelLoaded` lo chiama con **`null`**. ⚠️ **E il ricevente DEVE usare `Use()`, non `SetCurrentPersistentState(data)`**: quest'ultimo legge la rottura da `BreakableManager.ContainsBreakable`, e `AddBreakable` è chiamato **da un solo posto** *(`Damage`)* ⇒ **con il manager vuoto concluderebbe "non è rotto" e chiamerebbe `Restore()`, CANCELLANDO la rottura in silenzio**. **Test**: passi 1-6 ✅ *(stessa stanza, inversione ruoli, falò)* · **punto 7 ❌** *(chi entra dopo vede il vaso in piedi)* → **F4b.1** ha aggiunto la memoria per scena + il formato `scena\|guid` + gli hook, **ma l'hook non ha dipinto la rottura** e la **strumentazione è deployata per capire perché**. Vedi §12 |
| **7** | **`RegisterEnemies`** — 84.5 ms e allocazione ~0.4 MB/s — §1e(c) | 🟢 **ottimizzazione, non un difetto misurato di recente** | ⏳ **il picco da 84,5 ms è reale come misura, ma `stalls100=0` e `fps 55-59` nei test recenti dicono che oggi non si manifesta.** ⚠️ **Attenzione**: `stalls100` conta solo i frame **>100 ms**; il probe **non registra i 50-99 ms**, quindi *"84,5 ms"* **può ancora accadere senza comparire**. **Se si vuole sapere se esiste ancora, si legge `frameMs_max` da un log recente** *(è in ogni riga `[Probe]`)*. **Il lavoro è piccolo**: `FindObjectsOfType` ogni 0,5 s → `RESCAN_INTERVAL` a 2-3 s lo riduce di 4-6×, rischio quasi nullo |
| **8** | **Boss impazziscono coi target** — §2b | 🟡 | ⏳ **ultimo dei lavori "di gioco"**. **Proposta utente: quanto di tempo sul cambio bersaglio** |
| **9** | **Modalità spettatore** alla morte — §5a | 🟢 | ⏳ **RIDIMENSIONATA**: **fermabile su `DeadScreenWidget.OnDead`** *(`Prefix` che ritorna `false`: niente UI, niente `SetBlocker`, niente `Respawn`)*, **body invisibile** *(`SpriteRenderer.enabled`, che il gioco già usa al falò)*, **camera sul compagno** — **ma il "segue i cambi stanza" è BLOCCATO DAL MOTORE**: le stanze sono **scene Unity**, `OnLevelLoaded` azzera lo stato per-scena, `OtherPlayerManager` distrugge gli oggetti fuori scena, e `ProCamera2D` ha i **bordi geometrici per stanza**. **E il respawn-dal-falò dell'altro dipende dalla voce 4** *(dipendenza dura)*. **Il giocatore remoto è un GUSCIO**: `new GameObject` + `OtherPlayerScript`, con solo `SpriteRenderer` (disabilitato) + `Animator` — **nessun `Penitent`, nessuna camera, nessun collider** ⇒ **camera sul compagno = banale**, *"secondo Penitente" = impossibile*. Vedi §12 |
| **10** | **`ProcessQueue` + `IdOf`** (prestazioni) — §8 | 🟢 | ⏳ **declassato**: allocazione sì, stallo no. **Cosa sono**: `ProcessQueue` = il ciclo che svuota la coda dei messaggi in arrivo *(alloca una `List<byte>` per messaggio)*; `IdOf` = costruisce l'id di un nemico *(stringa da scena+nome+posizione, alloca ogni volta)*. **Sono due pezzi VIVI e centrali, non residui**: l'ottimizzazione è di secondo ordine |
| **11** | **Il gioco disfa la pausa sul non-proprietario**: `<Enemy>::OnUpdate` chiama `EnablePhysics(true)` e `StartBehaviour()` su **8 tipi** | 🟢 **da misurare** | ⚠️ **NON è la stessa cosa della voce 26**, e il fix 26 l'ha resa **più rilevante, non meno**: **26 = "non abbiamo mai messo in pausa"**, **11 = "il gioco ci toglie dalla pausa"**. Ora la pausa **viene applicata davvero**, quindi **c'è qualcosa che il gioco può disfare**. **MA l'effetto sembra svanito**: lo scivolamento è **quasi azzerato** e l'utente non lo vede. **Gli 8 tipi sono specifici**: `ShieldMaiden`, `RangedBoomerang`, `Firethrower`, `CrossCrawler`, `Nun`, `Fool`, `ExplodingEnemy`, `Lesmes` + coroutine di boss. **Conclusione corretta: MISURARLA, non chiuderla a scatola chiusa.** **Fix possibile non ancora valutato**: bloccare il gioco dal disfarla *(Prefix su `<Enemy>::OnUpdate` / `EnablePhysics`, che non agisce sui puppet)* — **ma `OnUpdate` è caldo: va progettata bene** |
| **12** | **Camminata troncata** | ✅ **stabile, il residuo è del gioco** | **CHIUSA su decisione dell'utente.** Richiede di ripensare il protocollo animazione *(**loop vs one-shot**)*, e **succede solo in casi estremi**: con le interazioni risolte **è stato fatto il massimo possibile, e il resto è bloccato dalla logica del gioco**. **NB: voce re-inglobata** |
| **13** | **La migrazione host non riarbitra** | ✅ **RISOLTO E VERIFICATO** | **Due fix, e il TODO indicava il ramo sbagliato.** Il difetto **misurato** nasceva dalla **DEMOZIONE**, non dalla promozione: quando l'host perde la connessione, `SetHost(false)` mette in pausa **tutti** i nemici, ma `IsHost = _isHost \|\| !IsConnected` **resta `true`**, e `DecideOwnership` è dietro `if (!IsConnected) return;` ⇒ **5/5 nemici sospesi, 70 campioni identici = 35 s**, camminata attiva e spostamento nullo. **1A**: nel ramo di demozione, **se non si è connessi**, si reclama la proprietà e si chiama `ResumeEnemy` **invece di metterli in pausa**. **1B**: nella promozione la proprietà viene **reclamata subito** *(`OwnedByLocal=true`)*, non solo il timer ⇒ **sparisce il buco di 0,5-1,0 s** in cui nessuno simulava. **E il fix C, nel SERVER**: la promozione era annunciata **solo al promosso** e **mai ritentata** ⇒ con 3 giocatori il terzo **non riceveva né `Host role: True` né il leave del precedente** *(`owner=altro` su un giocatore **uscito**, AI ferma 128 campioni)*. Ora è un **broadcast a tutti i rimasti**. **Verificato**: primo handover *(`io=230/230`)* e secondo *(G3 riceve `Host role: True`)*, **zero `pause:`** sui log. **Variante non coperta**: *G2 esce per primo*. Vedi §12 |
| **14** | **`IMPRONTA DIVERSA`** | ✅ **RISOLTO** | **ERA UN FALSO ALLARME**: `_listFingerprint` era calcolata solo in `OnEnemiesSpawned`, ma `_ids` viene **appesa** ogni 0.5 s e **rimossa** in `ForgetEnemy` ⇒ la fingerprint descriveva una lista che non esisteva più. **Peggio**: la segnalazione era **una-tantum**, quindi un falso allarme **zittiva ogni divergenza reale**. Ora `_idsDirty` + `RefreshFingerprintIfDirty()` + cooldown 15 s. Vedi §12 |
| **15** | **Deploy: `Common.dll` va copiato su ENTRAMBE, nella versione giusta per ciascuna** | 🟢 **procedura** | ⏳ **`Modding\data` è net35 (gioco), `SERVER-COOP` è net8.0 (server): gli hash DIFFERISCONO per costruzione, quindi vanno confrontati SEMANTICAMENTE, non per hash.** Se non si copia quello giusto, **il join viene rifiutato con `RefusalType::Protocol`** *(è già successo)*. ✅ **verificato automaticamente da `tools\verify-deploy.ps1`.** ⚠️ **`PROTOCOL_VERSION = 11` NON sta in `Common.dll`**: è una costante **duplicata** in `Client\Network\NetworkManager.cs` e `Server\Server.cs`, confrontata **solo all'handshake** *(`Common.dll` contiene solo l'enum `NetworkType`)* |
| **16** | ⚠️ **Il deploy va verificato su ENTRAMBE le macchine** | ✅ **RISOLTO** | **Errore di copia nel testing.** ✅ **RISOLTO con uno strumento**: `tools\verify-deploy.ps1` — hash + marcatori + protocollo, esce con codice 1. Vedi §12 |
| **17** | **I nemici che restano fermi (`pos_distinte=1`)** | ✅ **non è un bug** | nemici **senza IA di movimento** *(1 sola `posNemico` per tutta la loro presenza nel log, `anim=Idle` sempre)*. **Attenzione a leggerli come "sospesi": non lo sono.** |
| **18** | **Boss: test boss per boss** | 🟡 | ⏳ l'unica cosa non prevedibile |
| **19** | **`Entity.Dead` calcolato vs `EntityStatus.Dead`** — §7 | ✅ **nota tecnica** | documentato |
| ~~20~~ | ~~Passo 5: le 130 patch sul Penitente~~ | — | ❌ **cade**: `Animator.name == "Body"` su entrambe |
| ~~21~~ | ~~Passo 1: test di controllo single-player~~ | — | ❌ **cade**: la baseline disconnessa c'è già |
| ~~22~~ | ~~Isteresi via `targetTime` su `EnemyBehaviour`~~ | — | ❌ **cade**: `targetTime` è `Private, InitOnly` e **mai assegnato** ⇒ `LookAtTarget` è **codice morto** |
| **23** | **Il desync grave della sessione di test** (eccezione nel ciclo di invio) | ✅ **RISOLTO** | **era una REGRESSIONE NOSTRA** introdotta in `b2c9cdd`: la diagnostica `[EnemyTree]` leggeva `BehaviourTree` con **chiamata diretta**, ma il getter è **protected a runtime** (publicized a compile-time) ⇒ **479 e 666 eccezioni** da `UpdateCore → ResumeEnemy`, che **interrompevano il ciclo di invio a metà** ⇒ `SendEnemyUpdate` non partiva mai. Corretto in `9b1ea87` (reflection + try/catch), **verificato con `exceptions=0` su 384 report in 4 sessioni**. Vedi §12 |
| **24** | **Il gioco azzera `consumedSpawns` in DUE metodi**, non uno | ✅ **RISOLTO** | **`EnemySpawner.Reset`** (morte del giocatore, load di un salvataggio, reset dal menu — **nessuno è un caricamento di livello**) oltre a `RespawnDeadEnemies`. **Scoperto durante il fix della voce 25**: svuotare solo al falò lasciava un annuncio **stantio** ⇒ il nuovo entrato uccideva nemici che l'annunciatore vedeva **vivi**. **Errore speculare, introdotto dal fix stesso.** Vedi §12 |
| **25** | **Le morti non vengono annunciate se chi le ha viste non è più nella stanza** | ✅ **RISOLTO** | `AnnounceDeathStateToPeers` ricavava gli id da `FindObjectsOfType<EnemySpawnPoint>()`, cioè **solo la scena corrente dell'annunciatore**: altrove la lista era **vuota e usciva in silenzio** ⇒ **i nemici riapparivano VIVI**. Fix: memoria lato mod degli id annunciati + unione deduplicata + **una riga di log quando l'annuncio è vuoto** (il caso era invisibile). Vedi §12 |
| **26** | **La pausa NON esisteva per 60 tipi di nemico su 77** | ✅ **RISOLTO E VERIFICATO** | `Enemy.EnemyBehaviour` è **null** per 60 sottotipi (fra cui **Fool e WheelCarrier**, tutti i nemici della stanza di test, e quasi tutti i boss): salvano il behaviour in una **proprietà propria**. `PauseEnemy` **usciva lì**, prima di `PauseBehaviour`/`enabled=false` ⇒ sul non-proprietario **AI, Animator e orientamento continuavano** = **"cammina sul posto"**. Fix: `FindBehaviour` con fallback su `GetComponent`. **E lo stesso difetto era nel ramo di promozione di `SetHost`.** **MISURA DOPO IL FIX** (baseline pre-fix ritrovata in modo indipendente in `a7a726ba73…/LogOutput G1.log`, **stessa sessione** del log G2 DUE ⇒ condizioni omogenee): scritture di orientamento **437 → 14 (G1) e 5 (G2)**; riavvii di animazione **54 → 14 (G1) e 21 (G2)**; `orientSkip=0`; `orientFlip` **11 e 1 in totale**. **Riduzione ~10-30×.** ⚠️ Confronto fra sessioni diverse: non è un A/B sullo stesso run. Vedi §12 |
| **27** | **Il nemico che resta fermo con un giocatore addosso** — §1h | ✅ **CHIUSA — NON è un difetto della mod** | **PROVATO col controllo in singolo, disconnesso** *(la mod attiva ma senza peer ⇒ **il percorso della pausa remota è inerte**)*: il `Fool` a `(-717.7,10)` resta fermo **6 campioni (~12 s)** con distanza grezza **3.07** *(giocatore a `(-721.3,9)`)*, e **in Fase A** *(single-player, nessun peer)* resta fermo **32 campioni**. **`dy = +1.0` COSTANTE per tutto il tratto fermo** → salendo a `y=10.7` *(stesso piano, dist 0.81)* **il nemico parte subito** (`anim=765711723` = camminata). ⇒ **la spiegazione "pendenza" dell'utente è CONFERMATA.** ⚠️ **La mia misura precedente diceva `dy=+0.1`: era il campione di CONTATTO, non il tratto fermo — errore mio, corretto qui.** Nei log a due lo stesso Fool è fermo anche con `dist_io` 0.5-1.4. **NON si tocca.** Vedi §12 |
| **28** | **`EnemySpawner.Reset`: il logging funziona** | ✅ **CONFERMATO IN GIOCO** | Il hook **scatta** e il conteggio **combacia esattamente**: G1 uccide **2** nemici *(pos `-717.6,10` e `-697.5,11`)* e il log dice **`consumedSpawns azzerato dal gioco: dimenticate 2 morti annunciate e 0 ricevute`**, **dopo la 2ª morte e prima della rinascita**. **Il gancio NON è troppo aggressivo.** ⚠️ **Resta NON testato il ramo `RespawnDeadEnemies`/`OnEnemiesRespawned`** *(il falò vero)*: **mai eseguito** nei log → vedi voce 31. Vedi §12 |
| **29** | 🔴 **Dopo un `EnemySpawner.Reset` le due liste locali DIVERGONO** | 🟡 **priorità media** | **NUOVA, misurata nel test della voce 28.** Nel log G2 RESET **`IMPRONTA DIVERSA` ×4** *(righe 558, 1043, 1271, 1499)*: tornando in `D01Z01S01` la macchina registra **3 id invece di 5** *(e il suo `Post-spawn` dice `2 enemies` dove l'host ne ha 3)*. **Causa accertata**: `EnemySpawner.Reset` **non trasmette nulla** — rigenera i nemici **solo in locale**, quindi **chi non esegue il reset resta con i suoi consumati**. **È lo stesso meccanismo del falò**: per questo la voce 4 resta gialla. **Il rilevatore ha funzionato: ha segnalato una divergenza VERA.** Vedi §12 |
| **30** | ⚠️ **La memoria delle morti si azzera SOLO sulla macchina che esegue il reset** | 🟢 **latente** | In IL `RespawnDeadEnemies` **non trasmette nulla**: chiama `EnemySpawnPoint.CreateEnemy()` **in locale** + `OnEnemiesRespawn`; i **tre chiamanti** sono tutti della macchina che si siede. Quindi `_announcedDeaths`/`_pendingDeaths`/`_killLogged` si svuotano **solo dove si siede** *(o dove il giocatore muore)*. **Se G1 si riconnette dopo, G2 potrebbe annunciargli morti che non esistono più.** **NON si è materializzato nei log** *(G2 non ha mai annunciato N>0)*: **reale ma latente.** Vedi §12 |
| **31** | **Il falò (`RespawnDeadEnemies`) non è mai stato esercitato nei test** | 🟢 **verifica** | Il hook su `EnemySpawner.Reset` è confermato, ma **il ramo del falò non compare in nessuno dei sette log**. Serve **un test dedicato con falò su UNA SOLA macchina**: *(1)* l'altra vede rinascere i nemici? *(2)* la memoria si svuota su chi si siede? *(3)* **su chi NON si siede NON si svuota** ⇒ un `[EnemyJoin]` successivo annuncerebbe `N>0` → **conferma della voce 4**. **È lo stesso test già indicato per la voce 4.** Vedi §12 |
| **32** | **L'arbitraggio host-only è CIECO fuori dalla scena condivisa** | ✅ **CHIUSA — e la diagnosi "è un difetto da fixare col passo 2" era SBAGLIATA** | **Il DIFETTO VERO era uno solo: il peer si congelava quando il token non poteva arrivare** *(l'host altrove ⇒ `_ids` non contiene quei nemici ⇒ nessun token ⇒ `PauseEnemy` per sempre, misurato: `WheelCarrier` fermo 134 campioni ≈ 67 s)*. **Ed è chiuso dal PASSO 1** *(`f09839d`: il peer simula `!IsHost && !OwnershipKnown`)*, **verificato in gioco dall'utente**. ⚠️ **Ma la parte "PASSO 2 da fare: togliere `if (!localIsHost)`" era SBAGLIATA, e l'ho ripetuta senza verificarne la conseguenza pratica**: **il peer NON PUBBLICA un token** *(`ReceiveEnemyOwnership` ritorna subito `if (IsHost) return;`)* **e APPLICA quello dell'host** *(`OwnedByLocal = ownedByReceiver`)* ⇒ **se il peer decidesse, la sua decisione verrebbe SOVRASCRITTA** ⇒ **il gate non è il difetto: è ciò che tiene il peer da parte mentre aspetta l'autorità dell'host.** **E' il disegno, ed è coerente.** ✅ **PROVA DOCUMENTALE**: **`DecideOwnership` e il suo chiamante sono IDENTICI fra il tag `stato-sync-movimento-ok` (`998c561`, 7 ott 18:40) e HEAD** — *la "parità" era ed è DENTRO la funzione: la prima assegnazione è simmetrica, `wants \|\| (!peerCloser && localIsHost)`, e il commento spiega che prima leggeva `localIsHost \|\| wants` "e questo è ciò che faceva sembrare essere il primo a connettersi un privilegio"*. **⇒ NULLA È ANDATO PERSO.** **L'utente ricordava bene.** ⚠️ **REGISTRATA ANCHE LA LEZIONE**: **"verificato nel codice" NON è "verificato nel comportamento"** — *una verifica strutturale ("`DecideOwnership` ha un solo chiamante") può essere vera e portare a una conclusione sbagliata se non si guarda chi pubblica e chi applica il risultato.* Vedi §12 e voce 43 |
| **33** | **`BreakSync`: l'hook non dipinge la rottura** *(memoria dei vasi)* | ✅ **RISOLTA — la causa era MIA, in `Forget`** | **Il test F4b.1 era fallito al suo caso centrale**: `0d384626` rotta da G1 in `D17Z01S03`, il server **rimanda** la entry a G2, G2 la **riceve e la ricorda**, entra nella stanza e **la trova in piedi**. **La causa, ricostruita in IL**: `LoadSnapShot` chiama `SetCurrentPersistentState` su ogni oggetto di cui ha dati → per un `BreakableObject` **`IsBroken()` è FALSO** *(il `BreakableManager` non viene ripopolato al load)* → **il gioco chiama `Restore()`** → il mio postfix su `Restore` → **`Forget` cancellava il guid da `pending` E da `applied`**. **E l'hook è un POSTFIX di `LoadSnapShot`, quindi gira DOPO** ⇒ **quando guardava, il pending era già stato cancellato.** **Fix**: un flag `loadingSnapshot` *(Prefix su `LoadSnapShot`, abbassato dopo la ri-applicazione)*: **`Forget` esce subito se il flag è alzato**, e decide la ri-applicazione. **Verificato in IL.** ⚠️ **E il caso "G2 entra dopo" NON è mai stato esercitato nei test**: le 3 entry rimandate restavano `pending=3` fino alla fine **perché la stanza non è più stata rivisitata** ⇒ **serve un test che la rivisiti.** Vedi §12 |
| **34** | **La dedup del server scarta i ri-annunci** *(di qualunque oggetto persistente)* | ✅ **RISOLTA per i vasi (F4b.2) — e il difetto è STRUTTURALE per tutto il resto** | `TeamInfo.AddTeamProgress` deduplica **per stringa id su TUTTA la sessione del server** e accetta **solo se `progressValue > currentValue`** *(in IL: `cgt` **unsigned**)*: tutti gli oggetti persistenti mandano **`value = 0`**, quindi `0 > 0` è **falso** e un id già visto è **sempre rifiutato** *(`Server.cs:591` esce senza inoltrare)*. **Conseguenza vera: ogni oggetto può essere annunciato UNA VOLTA per sessione del server.** ⚠️ **CORREZIONE a una riga precedente di questa voce**: *"il formato `scena\|guid` aggira il caso cambio stanza"* **era IMPRECISO** — **il GUID è authored e unico per ISTANZA**, quindi **l'id è per-oggetto, non per-visita**: rompere un'ALTRA lanterna altrove dà un id nuovo *(passa)*, ma **rirompere la STESSA lanterna, anche uscendo e rientrando, dà lo STESSO id** *(scartato)*. **Quindi lasciare e rientrare NON rinfresca la chiave.** **✅ RISOLTA per i vasi da F4b.2**: l'annuncio **non usa più il canale Progress**, ma **`EnemyDeathGlobal`** con id `brk\|scena\|guid` — **il canale che il server inoltra senza alcun dizionario** *(e che i nemici già usavano, ed è il motivo per cui i nemici non hanno mai avuto questo problema)*. **Solo client: nessun `NetworkType` nuovo, nessun bump di protocollo, nessuna `Common.dll` da ricopiare.** ⚠️ **RESTA STRUTTURALE per muri, leve e ogni altro oggetto che usa il canale Progress**: se in futuro servisse ri-annunciarli, **il problema è ancora lì**. Vedi §12 |
| **35** | 🟡 **Se un giocatore muore nella bossroom, il boss spegne l'IA** | 🟡 **nuova, segnalata dall'utente** | **Segnalato in gioco**: *"se uno muore nella bossroom il boss spegne l'IA"*. **Comportamento voluto**: **se ci sono altri giocatori nella bossroom, il boss deve cambiare target con uno di quelli ANCORA VIVI nella stanza** *(e non spegnersi)*. **Questo tocca la voce 9** *(`Respawn`/morte del giocatore in bossroom)* **e la voce 8** *(il cambio bersaglio dei boss)*: la morte di un giocatore **non è oggi comunicata ai peer**, quindi **probabilmente serve un pacchetto "player death"** *(⚠️ `NetworkType` **non ha** un tipo player-death ⇒ richiederebbe un bump di protocollo)*. **Da analizzare, non da ipotizzare.** Vedi §12 |
| **37** | 🔴 **L'ECO INFINITA: un'annuncio applicato veniva ri-annunciato** | ✅ **RISOLTA (F4b.3) — ma la LEZIONE va ricordata** | **F4b.2 funzionava ma aveva introdotto qualcosa di peggio del difetto che curava.** **Conteggi incrociati nei log**: `APPLY(G1)=SEND(G2)` e `APPLY(G2)=SEND(G1)-2` ⇒ **ogni annuncio ricevuto veniva riapplicato E re-annunciato, un round-trip per iterazione** ⇒ **187 + 34 SEND su DUE soli vasi**. **Meccanismo, in IL**: `BreakableObject::Damage` fa `ldfld destroyed / brfalse / ret` ⇒ **se l'oggetto è GIÀ rotto esce subito LASCIANDO `destroyed = true`**, e il postfix del mittente faceva `if (!___destroyed) return;` ⇒ **annunciava pur non essendo successo nulla**. **E il percorso nuovo non alzava `reapplying`**. ⚠️ **PERCHÉ NON SI VEDEVA PRIMA: il canale Progress deduplicava, quindi l'eco veniva inghiottita dall'eco stessa.** **LA LEZIONE, e vale per tutto il progetto**: *cambiare canale obbliga a ricontrollare TUTTI i guard che proteggevano il canale vecchio — un guard che sembra ridondante può essere l'unico che teneva su la baracca.* **Fix**: una **Prefix** su `Damage` cattura **se l'oggetto era già rotto PRIMA** *(l'unico momento in cui la domanda ha risposta: dopo, il campo è `true` in entrambi i casi)*, e la Postfix esce subito se lo era. **Più** `ApplyOrRemember` che alza `reapplying` con **salva/ripristina** *(non azzera: un'applicazione può annidarsi)*. **Verificato in IL**: ordine `TryGetValue → CurrentlyUpdatingProgress → IsReapplying → IsConnected → Send`. Vedi §12 |
| **36** | ⚠️ **DOCUMENTAZIONE: i salvataggi cambiano cartella se Steam non è avviato** | 🟢 **operativa — da avvertire SEMPRE** | **Il percorso dei salvataggi è costruito così** *(IL del gioco)*: `persistentDataPath + "/Savegames/" + (SteamManager.Initialized ? SteamUser.GetSteamID().ToString() : "") + "/"`. ⇒ **avviare il gioco SENZA Steam sposta silenziosamente ogni salvataggio in `Savegames\` invece che in `Savegames\<steamid>\`.** **E con Steam Cloud abilitato le due cartelle DIVERGONO**: una sessione iniziata senza Steam **sembra** aver corrotto o perso i salvataggi — **non erano corrotti: erano da un'altra parte.** **Intermittente e dipendente dalla macchina**, quindi **facile da scambiare per un bug della mod**. ⚠️ **E la struttura "anomala" che aveva allarmato** *(cartelle vuote `savegame_0\`, `savegame_0_backup\`, contenitore `76561199216304572\` vuoto)* **è NORMALE: le crea il GIOCO** *(`PersistentManager.CreateAndGetSaveGameInternalDir` + `BackupSlot`→`FileTools.DirectoryCopy`)* **e convivono coi file anche su un'installazione sana.** **La mod NON c'entra**: scrive **un solo file** *(`savegame_N_modded.save`)* e **il gioco non lo legge mai** *(solo `.save`/`.save_backup`; nessuna `Directory.GetFiles` sulla cartella)*. **RACCOMANDAZIONE OPERATIVA, da ripetere SEMPRE**: *(1)* **avviare SEMPRE Steam prima del gioco**, oppure *(2)* **disattivare Steam Cloud per Blasphemous**, e *(3)* **backup a mano di `Savegames` prima delle sessioni di test.** ✅ **Ora reso VISIBILE**: la mod logga all'avvio `[Path] Steam inizializzato: … · cartella salvataggi in uso: …` e **avverte se Steam manca**. Vedi §12 |
| **38** | ⚠️ **Il log del SERVER non esisteva su disco** | ✅ **RISOLTA — `ServerOutput.log`** | **`Logger.Print` era solo `Console.WriteLine`** *(`Logger.cs:39`)* **e nella cartella del server NON c'era nessun file di log** ⇒ **il resoconto del server moriva con la finestra**. **È costato DUE indagini di fila**: la dedup della proprietà e la lanterna post-falò sono state diagnosticate dai log dei **client**, perché il log del server non era mai stato conservato — *e le due righe che le avrebbero provate in un colpo solo sono `Server.cs:625` "Received new pers. object from X: Y" e `Server.cs:593` "Received duplicated or inferior progress from X: Y"*. ✅ **Ora il server scrive `ServerOutput.log`** accanto al binario *(`AppContext.BaseDirectory`)*, con intestazione datata e **flush a ogni riga**. **`Print` formatta la riga UNA volta e la manda a entrambe le destinazioni**, così ciò che si è visto e ciò che si è conservato non possono divergere. **Tutte le 33 chiamate del server passano dal `Logger`**, quindi lo specchio le prende tutte. ⚠️ **Due scelte deliberate**: *scrittura sincrona sotto lock* *(un log che perde le ultime righe quando il processo viene fermato — e questo server si ferma con un tasto — è peggio di nessun log, perché viene creduto)*, e **nessun errore può far cadere il server**. **Verificato AVVIANDOLO**, non solo compilato. **Da chiedere per nome: `ServerOutput.log` dell'host.** Vedi §12 |
| **39** | ⚠️ **La doppia animazione dei vasi all'ingresso in stanza** | 🟡 **FIXATO (F4b.4) MA NON DIMOSTRATO — l'utente non la vede più, i log non lo provano** | **Segnalata dall'utente**: *"G1 e G2 vedono 2 vasi rotti → si siedono al falò → G2 entra e ne rompe 1 → G1 entra e vede l'animazione di ENTRAMBI"*. **Causa: `ReapplyCurrentScene` gira più di una volta per ogni caricamento di livello** *(`LoadSnapShot` viene invocato due volte per load)*, **e ogni passata che trova un oggetto ancora in piedi lo rompe e ne riproduce l'animazione** ⇒ **4 `Use()` per 2 vasi in un ingresso, 2 animazioni per vaso**. **Fix (F4b.4)**: **una guardia in `BreakableObjectReceive_Patch.Prefix`** *(l'unico posto che chiama `Use()`)* — se `___destroyed` è già `true`, **ritorna `false` senza applicare**. **Non tocca rete, `applied`, `pending` né `Forget`.** ⚠️ **MA LA GUARDIA NON È DIMOSTRATA, e va detto**: **il log NON può provarla**, perché `[BreakSync] rottura applicata` è stampato **DOPO** la chiamata e **`ApplyToLoadedObject` ritorna `true` anche quando la guardia sopprime** ⇒ **restano 2 righe per GUID per ingresso, e non si distingue "due applicazioni" da "una applicazione + una soppressa"**. **E il meccanismo che la aggira esiste, nel GIOCO** *(IL)*: `SetCurrentPersistentState` scrive `destroyed` dallo snapshot, poi `IsBroken()` è falso *(il `BreakableManager` non viene ripopolato al load)* ⇒ **IL_0040 `call Restore()`** ⇒ **`Restore()` rimette `destroyed = false`**. **Quindi la premessa del fix** *("la seconda passata trova gli oggetti già rotti")* **vale solo se nessuno li riporta in piedi fra le due passate.** **L'utente conferma che il sintomo è sparito** *("tutto funziona alla grande")*, **ma serve una misura**: una riga di log **quando la guardia sopprime**. ⚠️ **LIMITAZIONE NOTA**: **`pending` NON viene ripulito dal falò** *(il guard `loadingSnapshot` in `Forget` salta solo la parte `pending`; il falò non passa da `LoadSnapShot`)*. **È il PROGETTO della voce 33, ma diventa un'eco** quando il vaso era già rotto in questa vita prima del falò. **Toccarlo è rischioso**: è lo stesso `Forget` che ha causato "l'hook non dipinge". Vedi §12 |
| **40** | 🟡 **`ReapplyCurrentScene` gira due volte per caricamento di livello** | 🟢 **nota tecnica — innocua con la guardia** | **Due invocazioni**: *(1)* il postfix di `PersistentManager.LoadSnapShot` *(il fix vero)*; *(2)* **la "seconda possibilità"** `MultiplayerLateUpdate_Patch`, armata da `Multiplayer.OnLevelLoaded` *(⚠️ **era strumentazione temporanea mia, aggiunta per misurare quale delle due dipingesse** — il log ha detto che dipingono **entrambe**)*. **Con la guardia della voce 39 è innocua** *(la seconda passata trova gli oggetti già rotti e non fa nulla)*, **quindi è stata TENUTA di proposito**: rimuoverla sarebbe stato un secondo azzardo nello stesso momento, **e le due non scattano nello stesso istante** *(la prima può girare mentre gli oggetti si stanno ancora costruendo)*. **Se un giorno si vuole semplificare, è un candidato**, con la raccomandazione di farlo **dopo** aver verificato che la prima passata basti da sola. Vedi §12 |
| **41** | 🔴 **`ProgressManager.Update` è senza `try/catch` né `finally`: UNA eccezione UCCIDEVA la sincronizzazione PER SEMPRE** | ✅ **RISOLTA — con l'eccezione isolata E loggata** | **Era il difetto più pericoloso del progetto.** **Verificato nel codice** *(`ProgressManager.cs:58-69`)*: `CurrentlyUpdatingProgress = true;` → **i 14 helper di `ApplyProgress`** → **`= false`**, **senza `try` né `finally`**. **Fuori c'è un `try/finally` in `Multiplayer.cs:154` che però resetta `PerformanceProbe`, NON questo flag.** ⇒ **una sola eccezione in un helper lasciava il flag `true` PER SEMPRE**, e si portava dietro: **(a)** `BreakableObjectHelper` **sopprimeva OGNI futura rottura**, **(b)** **~29 guard** negli altri helper *(`BeadHelper`, `PrayerHelper`, `RelicHelper`, `QuestItemHelper`, `CollectibleHelper`, `HeartHelper`…)* **sopprimevano l'invio di TUTTA la progressione**, **(c)** **la coda non si svuotava e rilanciava ogni frame**. **⇒ il gioco restava vivo ma il co-op smetteva di sincronizzare**, senza un crash e senza un errore evidente. **Probabilità: BASSA** *(0 occorrenze nei log)* **ma i percorsi sono verificati**: `ApplyProgress` fa **`int.Parse`** su un id **che arriva dalla rete** e una lookup che può uscire dalla tabella — **e la difesa era SOLO il parsing** *(`IsTableId` / `char.IsDigit`)*, **non un catch**. ⚠️ **Il progetto lo sapeva**: il commento in `BreakSyncMemory.IsTableId` lo dice **esplicitamente**, e **non era mai diventato un fix**. **✅ FIX**: **`try/finally` con `Clear()` E il flag abbassato DENTRO il `finally`** *(il `Clear` deve stare lì: una coda parziale va comunque scartata, o la stessa entry che lancia verrebbe ritentata ogni frame)* **+ `catch` per-elemento in `ApplyProgress`, CON LOG dell'id** *(un `catch` silenzioso nasconderebbe l'unica prova che spiega perché un aggiornamento è sparito)*. **Nessun bump.** Vedi §12 |
| **42** | **`AddInteractedObject` senza dedup** | ✅ **RISOLTA — `if (!Contains)`** | **`List.Add` senza controllo**, e **il file su disco lo mostrava: 8 voci dove 4 erano distinte**. **Innocuo in effetto** *(quella lista finisce in `savegame_N_modded.save`, che il gioco non legge mai — voce 36)*, **ma è il tipo di innocuo che smette di esserlo il giorno in cui qualcuno conta le voci o confronta due liste per uguaglianza.** ✅ **Fix**: `IsNullOrEmpty` → **`Contains`** → `Add`. Vedi §12 |
| **43** | 🟢 **`if (!localIsHost) return false;` in `DecideOwnership` è probabilmente codice MORTO** | 🟢 **opzionale — PULIZIA, non un fix** | **Il gate esiste per un motivo storico**: *"only the arbiter decides; this method is never the peer's path"*. **E con il disegno attuale è coerente**: **il peer NON PUBBLICA un token** *(`ReceiveEnemyOwnership` ritorna subito `if (IsHost) return;`)* **e APPLICA quello dell'host** ⇒ **qualsiasi decisione del peer verrebbe sovrascritta.** **Quindi il gate non cambia nulla di osservabile.** ⚠️ **Non è un difetto e non c'è fretta**: **rimuoverlo sarebbe una semplificazione, e va fatta SOLO dopo aver verificato che la PRIMA ASSEGNAZIONE SIMMETRICA** *(`state.OwnedByLocal = wants \|\| (!peerCloser && localIsHost);`)* **basti da sola** — *il ramo dei cambi successivi serve oggi solo all'host, che lo esegue con `localIsHost = true`*. ⚠️ **E il test in gioco NON può deciderla**: *"il non-host si muove"* è **il comportamento del PASSO 1** *(il peer simula `!OwnershipKnown`)*, **e succede sia col gate sia senza**. **Serve un'analisi dei percorsi nel codice, non una sessione.** Vedi §12 |

**Poi**, in fondo al file: `## 12. ARCHIVIO DELLE AGGIUNTE` (entry datate) e
`## 13. STORICO DELLE REVISIONI` (le versioni precedenti della todo, conservate).

---

Ultimo aggiornamento: **i distruttibili (vasi) quasi chiusi** — tipo, identità e meccanica accertati; F4b.1 ha aggiunto la memoria per scena, ma **l'hook non dipinge ancora** e la **strumentazione è deployata**. **E il passo 1 dell'arbitraggio** *(il peer simula ciò che l'host non vede)* è **fatto e deployato**. **Restano**: la voce 33 *(misura)*, il passo 2 dell'arbitraggio *(voce 32)*, il falò *(voce 4 + 29/30/31)*, lo spettatore *(9)*, i boss *(8/18)*.


---

## STATO DEL PROGETTO

**Il grosso è concluso e verificato.** Restano lavori mirati: l'**orientamento** (in verifica), il
**feedback del colpo**, gli **oggetti distruttibili**, la **diagnostica**, i **boss** e lo **spettatore**.

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
| **Orientamento** | 🔬 **isteresi implementata, misura in corso** |
| **Sangue e flash** | ⏳ metodi identificati |

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
| 7a | **`Entity.Dead` è CALCOLATO** (`!(Life.Current > 0)`), **`EntityStatus.Dead` è l'interruttore vero** che `KillInstanteneously()` accende. È la chiave di tutto il capitolo morte: una guardia su `Entity.Dead` si "disarma" da sola appena la vita tocca zero |
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
| **3** | **Desync al join (§1g)** — pacchetto "stato morte" sul canale Progress + **ri-annuncio a ogni join** | ⬆️ **PROMOSSO da richiesta esplicita dell'utente.** Indipendente da §1, **riusa `ReceiveEnemyDeathGlobal` che è GIÀ scritto e collaudato**, **nessuna modifica al server**, nessun bump di protocollo. È il difetto **funzionale** più visibile rimasto | ⏳ **PROSSIMO** |
| **4** | **Contatore `orientFlip` per nemico** | **Necessario per rendere MISURABILE il fix dell'orientamento (§1b)**, che altrimenti si giudica a occhio. Poche righe nel probe | ⏳ **insieme al 3** |
| **4b** | **Diagnostica dietro flag** + `RESCAN_INTERVAL` a 2-3 s | ↕️ giustificato da §1e(c) (gli **84.5 ms** e l'allocazione ~0.4 MB/s), **NON dagli FPS** | ⏳ |
| **4c** | **`fullPathHash`** + riposizionare davvero (vecchio passo 3) | ⬇️ **DACCATO SOTTO LO SPETTATORE**: le animazioni sono **accettabili** (decisione dell'utente). Da riprendere **solo se** la fluidità tornasse a dare fastidio | ⏳ **parcheggiato** |
| ~~5~~ | ~~Solo se `Animator.name != "Body"`~~ | ❌ **CADE**: il nome è `Body` su entrambe le macchine, 4+4 volte | ❌ **cade** |
| **6** | **Falò A1**: pacchetto `PrieDieuRested` → `RespawnDeadEnemies()` sul ricevente, **senza** refill | Indipendente da §1, rischio basso | ⏳ |
| **7** | **Oggetti distruttibili**: 4 helper (`PersistentBreakableObject`, `SlashReceiver`, `BreakableObject`, `TriggerBasedTrap`) | Meccanico e isolato, ma **dopo** aver identificato l'oggetto in gioco (`Broke wall: 0`, `Used Lever: 0` anche nell'ultimo test) | ⏳ |
| **8** | **Prestazioni**: `ProcessQueue` senza allocazioni + `IdOf` con dizionario inverso | ⬇️ **DECLASSATO**: **allocazione sì, stallo no**. Da fare **dopo** 2-3-4 | ⏳ |
| **9** | **Orientamento (§1b, RISCRITTO)**: **Prefix con isteresi** su `Entity::SetOrientation` + stessa isteresi in `ApplyOrientation` | ⬆️ **HA ORA UN MECCANISMO VERIFICATO IN IL** (`Ghost::OnUpdate`, `TrinityMinionBehaviour`). **Una sola armonica copre TUTTI i writer per-frame**; non tocca il game loop; **gli override dei boss e del Penitente non passano di lì** (verificato in IL) | ⏳ dopo il falò |
| **10** | **Modalità spettatore** (§5a) | Idea a bassa priorità, tocca il ciclo di morte del giocatore | ⏳ |

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
| 4 | Gli `shortNameHash` delle due macchine per lo stesso stato — chiude §1a | log di `shortNameHash` **e** `fullPathHash` per un nemico, 5 s per macchina | ⏳ **da aggiungere se il passo 2 non basta** |
| 5 | Identità dell'oggetto di §4 | il tipo reale in gioco | ⏳ `Broke wall: 0`, `Used Lever: 0` **anche nell'ultimo test** |
| 6 | Se `targetTime` è davvero `readonly` e non assegnato | verifica in IL | ✅ **RISOLTA**: `EnemyBehaviour::targetTime` e `EnemyAI::targetTime` sono **`Private, InitOnly`** e **nessun `stfld` li assegna in tutta l'assembly** ⇒ valgono `0f` ⇒ **`LookAtTarget` è CODICE MORTO su 71/71 sottoclassi**. Il vecchio passo 9 **cade ed è assorbito** nel nuovo
| 7 | Perché `[EnemyTick]` è ~1,8/s e non 2/s | — | ✅ **chiarito**: `LogDiagnosticSnapshot` gira a `RESCAN_INTERVAL = 0.5 s`, **una riga per nemico tracciato** |
| 8 | `Received duplicated or inferior progress` e `Couldn't find connected player:` | test dedicato | ✅ **0 "duplicated"** in questo test; restano **32** `Couldn't find connected player: *` su G2, **tutte al join** (una per voce di progress): rumore di handshake |
| 9 | `BLOCK_UNTIL_FPS_STABLE` **non è** un indicatore di prestazione | non usarlo come metrica | ✅ chiarito: è `if (1/smoothDeltaTime < 50 && timeWaiting < 3) attendi`, cioè la **conseguenza** |
| 10 | **Causa del cluster 69-106 ms su G2** | attribuzione esatta: `RegisterEnemies` è il candidato, ma i log non hanno eventi nella finestra | ⏳ non verificata |
| 11 | **Data/ora assoluta dei picchi** | — | ❌ **impossibile**: i log non hanno timestamp |
| 12 | **Animazione del Penitente e del ghost** | **nessuna riga di log le misura** | ⏳ da strumentare, se serve |
| 13 | **Numero di inversioni di orientamento per nemico** | **nessuna riga di log lo misura** | ⏳ da strumentare (**orientFlip**, passo 4) — **indispensabile per PROVARE il fix di §1b** |
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
