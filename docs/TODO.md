# TODO — Blasphemous COOP

Ultimo aggiornamento: dopo il **test esteso** (boss fight, cutscene, falò, dialoghi).

> **Il grosso del progetto è concluso.** Movimento e posizione sincronizzati, morte sincronizzata e
> **animata anche per mano dell'altro giocatore**, **fantasmi risolti** e — novità del test esteso —
> **boss fight funzionante**: entrambi i giocatori ottengono **un singolo boss condiviso, con barra
> della vita**, e **i trigger di morte sono sincronizzati**.
>
> **Il problema rimasto è uno solo, e si è concentrato**: le **animazioni tagliate**. Riguarda i nemici,
> le morti **e anche il Penitente** — che la mod non tocca. Vedi la sezione 1.

### Esito del test esteso

| Cosa | Esito |
|---|---|
| Movimento e posizione dei nemici | ✅ funziona |
| Morte animata per mano dell'altro giocatore | ✅ funziona |
| **Fantasmi** | ✅ **risolti** — l'animazione di morte parte; a volte è **troncata**, non assente |
| **Boss fight** | ✅ **un boss condiviso con barra e morte sincronizzata** |
| Cutscene e dialoghi | ✅ nessun problema reale (era un tasto sbagliato: serve SPAZIO, non ESC) |
| Animazioni (nemici, morti, **Penitente**) | 🔴 **troncate a pochissimi frame** |
| Falò e respawn dei nemici | 🔴 solo locale: le due macchine si disallineano |

Punti di ritorno, in ordine di creazione:

| Tag | Cosa rappresenta |
|---|---|
| `enemy-sync-death-risolta` | morte sincronizzata in tutti i casi, prima delle features |
| `stato-pulito-pre-features` | tag stabile + igiene del repository |
| `stato-features-1-7` | le 7 features riapplicate dal ramo WIP |
| `stato-token` | token di proprietà con le quattro correzioni |
| `stato-placeenemy-fix` | scrittura della posizione nella fisica, ordine corretto |
| `stato-revisione-il` | revisione IL: fallback, pausa persistente, collider allineato |
| `stato-thaw-fix` | ThawPhysics non salta più il ripristino |
| `stato-arbitro-pari` | proprietà decisa dalle distanze, non dall'ordine di connessione |
| **`stato-sync-movimento-ok`** | **movimento e posizione sincronizzati** |
| `stato-morte-feedback` | morte robusta, feedback del colpo, boss esclusi |
| **`stato-morte-animata-ok`** | **morte animata anche per mano dell'altro giocatore — il grosso del progetto è concluso** |

---

## 1. Animazioni di movimento (Q4, il grosso)

**Stato: il problema principale rimasto. Non affrontato.**

> **Il test esteso ha aggiunto il dato decisivo**: le animazioni tagliate **non riguardano solo i
> nemici** — sono tagliate **anche quelle di combattimento del Penitente** e la sua camminata
> "risulta a pochissimi frame". **E il Penitente la mod non lo tocca.**
>
> **Questo apre una domanda nuova**: se soffre anche un attore che non tocchiamo, la causa potrebbe
> non essere nel codice dell'animazione dei nemici ma in **qualcosa che la mod fa girare** — carico
> della rete o diagnostica — oppure nel fatto che **i pacchetti in arrivo vengono elaborati sul thread
> principale**. Vedi 1d.

### 1a. Camminata "troncata"

`normalizedTime` non viene praticamente mai trasmesso con un valore utile:

- il mittente forza `animTime = 0f` **quando lo stato cambia** (`EnemySyncManager.cs`);
- i pacchetti partono solo se qualcosa è cambiato, o sul full-sweep da 1 s;
- `ApplyAnimation` **esce subito** se il ricevente è già in quello stato, quindi non lo riposiziona
  mai;
- e `Animator.Play` su uno stato **in loop** (la camminata) **riavvia il clip da zero**: quando il
  gioco locale cambia stato e il pacchetto successivo lo riporta indietro, la camminata riparte.

**Soluzione proposta dall'Esploratore**, in ordine di qualità:
1. trasmettere `normalizedTime` **sempre** (togliere la riga `animTime = 0f`) — rischio basso;
2. meglio: per gli stati **in loop** non chiamare affatto `Animator.Play`, trasmettere uno stato
   semantico e lasciare che l'Animator del ricevente segua il proprio ciclo; forzare `Play` solo per
   gli stati one-shot. Serve `Animator.GetCurrentAnimatorClipInfo` per sapere se è in loop — rischio
   medio.

### 1b. Il nemico si gira a destra e sinistra velocissimo

**Causa trovata.** La mod scrive l'orientamento 20 volte al secondo, ma **il gioco lo riscrive ogni
frame** da un `OnUpdate` del componente `Enemy`. Due meccanismi senza isteresi:

- `Ghost::OnUpdate` — confronta la x dello sprite con quella del giocatore e chiama `SetOrientation`
  **ogni frame**, senza banda morta, senza soglia temporale, senza ricordare l'orientamento attuale.
  Se lo sprite oscilla attorno alla x del giocatore, inverte ogni frame.
- `TrinityMinionBehaviour::LookAtTarget` — è un **override che elimina la soglia temporale**,
  conservando solo la banda ±1. Il target è selezionabile a runtime con `SetTarget(Transform)`:
  **è il caso "più target"**. Se il target alterna fra due trasformazioni su lati opposti, inverte
  ogni frame.

E la protezione prevista dal gioco **non funziona**: in `EnemyBehaviour::LookAtTarget` il campo
`targetTime` è `private readonly float` e **il costruttore non lo assegna**, quindi vale `0f` e la
condizione `DeltaTargetTime < targetTime` è **sempre falsa**: resta solo la banda `|dx| <= 1`.

**Soluzioni proposte:**
- non scrivere l'orientamento sui nemici che hanno un writer per-frame (almeno `Ghost`,
  `TrinityMinion`); per gli altri lasciare `ApplyOrientation`;
- se si vuole l'orientamento corretto serve **Harmony**: un `Postfix` su
  `Entity::SetOrientation(EntityOrientation, bool, bool)` che per i nemici non posseduti riporti
  `Status.Orientation` al valore del proprietario — rischio medio.

### 1c. La morte parte e viene **skippata** (non è più un fantasma)

Il test esteso lo ha chiarito: **l'animazione di morte parte**, ma in alcuni casi **viene troncata
dopo pochi millisecondi**. **Non è un problema di morte: è lo stesso problema di animazione** — quindi
si risolve con 1a, non con altro codice sulla morte.

**Conseguenza importante**: **il capitolo "fantasmi" è chiuso.** Non serve più indagarlo.

### 1d. Riguarda anche il Penitente — da indagare

Il Penitente **non è toccato dalla mod**: nessuna scrittura sul suo Animator, nessun `Play` forzato.
Quindi se le sue animazioni si tagliano, la causa è **altrove**. Ipotesi da verificare, in ordine di
probabilità:

1. **Carico della diagnostica e della rete sul thread principale.** La mod gira `LogAnimationChanges`
   **ogni frame su tutti gli id** (`ResolveEnemy` + `AnimatorOf` + `GetCurrentAnimatorStateInfo` + due
   `TryGetValue` + una scrittura di dizionario **per nemico**), più `LogDiagnosticSnapshot` ogni 0,5 s
   (una riga formattata **per nemico**), più `RegisterEnemies` ogni 0,5 s (`FindObjectsOfType`, che
   alloca **due array**, più `GetComponentsInChildren` per ogni root).
2. **Rallentamento del frame rate** conseguente, che taglia le animazioni **di tutti**, Penitente
   compreso — e sarebbe **la spiegazione più elegante**, perché spiega con una causa sola tutti e tre i
   sintomi.
3. **Elaborazione dei pacchetti in arrivo sul thread principale** (`ProcessQueue` a inizio
   `OnLateUpdate`): con volumi alti, il frame può cedere.
4. **Il taglio delle animazioni dei nemici** (1a) come causa **separata**, che si somma.

**Da fare**: **misurare** prima di toccare — `Stopwatch` attorno a `EnemySyncManager.Update()`,
`GC.GetTotalMemory` una volta al secondo, e **misurare il frame rate** con e senza connessione. Se il
frame rate è basso anche in single player con la mod attiva, la causa è la diagnostica e **la cura è
semplice**: mettere i log dietro un flag e spostare il rescan su un intervallo più lungo.

---

## 2. Vita dei boss

**Stato: esclusi dal sync della vita** (fix applicato, non ancora testato in gioco).

Un boss deriva da `Enemy` (24 tipi verificati) e quindi viene tracciato, posizionato e animato come
gli altri, **ma la sua morte non è un'animazione**: è una sequenza scriptata
(`Crisanta_StDeath`, `PontiffGiantBehaviour::DeathSequence`, …) che parte **solo** da
`<Boss>::Damage`. Scrivere la vita a zero lo lascerebbe **in piedi con la barra vuota** e la stanza
bloccata.

**Da fare quando si testeranno i boss:**
- verificare che l'esclusione tenga (la barra si muove in modo indipendente sulle due macchine: è
  voluto);
- se serve la morte sincronizzata del boss: usare `((IDamageable)boss).Damage(hit)`, **non**
  `EntityDamageArea.TakeDamage`, perché è l'entry point da cui parte `<Boss>Behaviour::Death()`;
- **mai** distruggere il GameObject di un boss a mano: si perderebbe `GenericEntityDead` e barra e
  arena resterebbero appese;
- testare **boss per boss**.

---

## 3. Falò e respawn dei nemici

**Stato: non affrontato.**

`EnemySpawner` tiene la lista dei nemici uccisi e **la azzera quando un giocatore riposa a un falò**
(`EnemySpawner.RespawnDeadEnemies`). Il reset è **locale**: la mod non lo trasmette, quindi

> **se g1 riposa al falò, i nemici tornano solo sulla macchina di g1** e le due macchine restano
> disallineate su quali nemici sono morti.

**Da decidere:** se il riposo al falò debba essere un evento condiviso (e come), o se ogni macchina
debba restare indipendente.

### 3a. Il falò è il problema principale di questa sezione 🔴

Test esteso: sedendosi al falò compare nel log del modloader base un messaggio circa il
**"Respawning dead enemies on level"**. Correlato al save che avviene sedendosi — ma l'utente non è
certo che sia **sempre** così: potrebbero esistere **altri salvataggi automatici**.

**Da indagare:**
1. **Cosa triggera esattamente** quel respawn (verificare `EnemySpawner.RespawnDeadEnemies` e **chi**
   la chiama: il falò, un caricamento, un evento di scena, `PersistentManager`).
2. **Come propagarlo agli altri giocatori.** Proposta dell'utente: quando un giocatore provoca il
   respawn, gli altri **ricaricano stanza e mondo** così i nemici tornano anche a loro e l'evento
   scatta su entrambe le macchine.
3. **Il refill dei parametri del Penitente** (vita, fiasche, ecc.): è **legato allo stesso evento**?
   Se il respawn dei nemici può avvenire **anche fuori dal falò**, allora chi **non** si è seduto
   **non deve** ottenere il refill. L'utente cita come modello la **seamless co-op di Elden Ring** —
   e nota che una soluzione del genere **farebbe risparmiare anche altro codice**.

**Nota correlata:** `MarkSpawnPointConsumed` esce subito se l'id non inizia per `sp|`. Quindi per un
nemico registrato come `en|` (boss, incontri a trigger) la morte **non viene marcata** e il nemico
**ritorna al reload**. Per i boss è probabilmente corretto; per gli altri va valutato.

---

## 3b. Aggressività dei boss con due giocatori

Test esteso: la boss fight **funziona** (un boss condiviso, barra, morte sincronizzata), ma con due
giocatori nella stanza **il boss "impazzisce" con i target** — cambia bersaglio continuamente.

**Proposta dell'utente**, da valutare:
- un'**esclusione dai target** analoga a quella già in uso per i boss;
- **un tempo minimo o un numero di attacchi** prima di cambiare bersaglio, "più simile a Dark
  Souls".

**Da verificare**: chi decide il target di un boss (`Enemy.Target`, `GetTarget()`,
`*BossBehaviour::UpdateTarget`), e se la mod possa imporre un bersaglio stabile **senza** combattere
con la logica del gioco (è lo stesso tipo di conflitto di fisica, collider e orientamento: **due
scrittori ogni frame**).

---

## 3c. I boss sono davvero sincronizzati? Da chiarire

La mod **esclude i boss dal sync della vita** (`IsBoss`, namespace `Gameplay.GameControllers.Bosses`),
**eppure la boss fight mostra un boss condiviso con barra della vita e morte sincronizzata**.
Le due cose vanno riconciliate: capire **perché funziona** (la barra è in polling sulla vita locale e i
due boss ricevono lo stesso danno? i boss hanno un `EnemySpawnPoint` e finiscono quindi in un percorso
diverso? la morte si sincronizza per un'altra via?) — altrimenti non sappiamo **quale** parte
dell'esclusione stia davvero operando, e una modifica futura potrebbe romperla senza che ce ne
accorgiamo.

---

## 4. Irrobustimenti noti

| # | Cosa | Dove |
|---|---|---|
| 4a | **La migrazione host non riarbitra**: `SetHost` non azzera `OwnershipKnown`/`LastPublishedOwnerIsMe`, quindi il nuovo arbitro non ripubblica e l'ex host continua a trasmettere stato congelato | `EnemySyncManager.SetHost` |
| 4b | **Falso `IMPRONTA DIVERSA`**: il controllo gira **prima** che la lista locale sia popolata (`locale=0`), poi i fingerprint coincidono. 2 occorrenze nei log, fuorviante | `ReceiveEnemyUpdate` |
| 4c | **Prestazioni**: `RegisterEnemies` ogni 0,5 s chiama `FindObjectsOfType` (che alloca **due array**) più `GetComponentsInChildren` per ogni root; `LogAnimationChanges` gira **ogni frame** su tutti gli id | `EnemySyncManager` |
| 4d | **`_remoteTargets` non viene resettato al cambio di proprietà** → possibile snap a posizione stantia | `EnemySyncManager` |
| 4e | **`Enemy` non proprietario pinzato dal gioco**: `<Enemy>::OnUpdate` chiama `EnablePhysics(true)` e `StartBehaviour()` su 8 tipi, disfando la pausa imposta dalla mod | da valutare se serve |

---

## 5. Feedback del colpo — estensione

**Stato: il SUONO si sente in modo netto** (riscontro dell'utente), **il sangue non c'è** (concordato).

**Da fare:**

1. **Sangue e scintille** su chi non vibra il colpo, con il metodo del gioco:

```csharp
PenitentSword s = Core.Logic.Penitent.PenitentAttack.CurrentPenitentWeapon as PenitentSword;
s.GetSwordSparks(new List<IDamageable>{ enemy }, hit);
```

`GetSwordSparks` è l'**unico** chiamante in tutta l'assembly di `SparkOnImpact`/`BleedOnImpact`, e
esce subito se `_swordSparkSpawner == null`. Non ha effetti collaterali di gameplay.

2. **Il flash di ferita**: l'utente riferisce che **forse non si nota, o non è contemplato per tutti i
   tipi**. Va capito se dipende dal tipo di nemico (il flash è dentro `<TipoNemico>::Damage`, quindi
   non tutti lo fanno) o se il componente manca.

   Il metodo esiste ed è pubblico: `DamageEffect.DamageEffectScript::Blink(float, float)` — lo stesso
   componente che `MeninaBehaviour::OnDeath` disabilita. Fallback a costo zero:

```csharp
enemy.SpriteRenderer.GetComponent<DamageEffect.DamageEffectScript>().Blink(0f, 0.1f);
```

3. **Da verificare**: il presunto **doppio danno** di `EnemyDamagePatch` (è solo un postfix, senza
   prefix che blocchi). La verifica sul server suggerisce che **non colpisce il proprietario del
   nemico**, quindi è stato lasciato: da confermare con i contatori `dmgSent`/`dmgApplied` prima di
   toccare qualcosa che funziona.

---

## 6. Cose note ma non urgenti

| # | Cosa |
|---|---|
| 6a | I nemici `Tutorial` hanno `behaviour=null` e `anim=0` sempre: non si muovono e non animano, **nel gioco stesso** (verificato in single-player). Non è un difetto della mod |
| 6b | Molti nemici hanno `pos_distinte=1` perché **non hanno IA di movimento**: non è un sintomo |
| 6c | Il filo `enemy.CurrentLife = 0` rende `Entity.Dead` true, ma `EntityStatus.Dead` è il vero "sequenza avviata": **distinzione da ricordare** in ogni futura modifica sulla morte |
| 6d | Il protocollo di rete è **11**: mod, server **e `Common.dll`** vanno copiati insieme, altrimenti la connessione viene rifiutata |

---

## 7. Idee a bassa priorità (non vitali per la mod)

### 7a. Modalità spettatore alla morte di un giocatore

Idea dell'utente, dichiarata "malsana" ma **coerente con il genere**. Quando un giocatore muore
(esempio: g1):

1. **g1 non respawna subito**: la sua camera si **blocca su g2** e **lo segue anche nei cambi di
   stanza**;
2. quando **g2 si siede al falò**, parte l'**animazione dei cherubini** e **g1 torna in vita**.

L'utente dice di vedere nei log qualcosa su **respawn e cherubini**: da verificare l'esistenza di
`*Cherub*`, `Penitent.Respawn`, `*RespawnBehaviour`, e quale di questi sia il gancio giusto.

**Nota**: si aggancia naturalmente al punto 3 (falò), perché il ritorno in vita avviene **al falò
dell'altro giocatore** — quindi **conviene affrontarlo dopo** aver chiarito come si propaga l'evento
falò.

### 7b. Oggetti distruttibili non si sincronizzano

Test eseguito dall'utente su **una leva da spaccare** (di quelle che aprono cancelli di ferro): colpirla
**non** la rompe sull'altra sessione.

**Da identificare**: il tipo concreto (cercare `*Lever*`, `BreakableObject`, `*Breakable*`,
`IDamageable` su oggetti di scena) e **chi** applica il danno agli oggetti di scena — se passa dallo
stesso percorso dei nemici (`Weapon → IDamageable.Damage`) allora **la sincronizzazione è già a metà
strada** e il lavoro è limitato; se ha un percorso suo, va valutato se ne vale la pena.

**Gravità**: 🟡 il cancello resta chiuso per uno dei due, quindi **può bloccare l'avanzamento**. Da
verificare se blocca davvero o se basta che uno dei due la rompa.

---

## 8. Verificato e NON è un bug

| Cosa | Esito |
|---|---|
| **Dialoghi / cutscene che sembravano bloccarsi** | ❌ **non è un bug**: l'utente premeva **ESC** invece di **SPAZIO**. Riconnettendosi e premendo il tasto giusto, funziona. Verificato dall'utente stesso |
| **Fantasmi dei nemici** | ✅ **risolti**: l'animazione di morte parte. Quando "spariscono" è perché l'animazione **viene troncata**, che è il problema 1 |
| **Boss fight** | ✅ **funziona**: un boss condiviso, barra della vita, morte sincronizzata (vedi però 3c) |

---

## Come lavoriamo

- **Prima di ogni modifica**: l'Esploratore legge il **codice del gioco** (IL con Mono.Cecil) e
  verifica che i metodi esistano, siano pubblici e facciano quello che crediamo. Le tre regressioni
  più costose sono nate da assunzioni non verificate, non da errori di scrittura.
- **Un intervento per volta**, con **test in gioco** in mezzo.
- **Rollback immediato** al tag precedente se peggiora, senza tentare di salvarlo.
- **Verifica nel binario compilato** (leggendo le chiamate dentro la DLL), non solo nel sorgente:
  è così che si è scoperto che `PlaceEnemy` non muoveva nulla.
- **Build = deploy**: il `.csproj` copia sempre in `D:\SteamLibrary\...\Modding\`, quindi anche
  buildare da una copia di prova sovrascrive la mod installata.
