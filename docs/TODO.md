# TODO — Blasphemous COOP

Ultimo aggiornamento: dopo il tag `stato-morte-animata-ok`.

> **Il grosso del progetto è concluso.** Movimento e posizione sincronizzati, morte sincronizzata e
> **animata anche per mano dell'altro giocatore** (era il caso rotto da sempre). Tutte le fasi di
> test passate, tranne quella sui boss che non è testabile al momento.
>
> **Da qui in avanti: bug fix minori e le animazioni mancanti.**

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

**Stato: non affrontato.** Riguarda due sintomi distinti.

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
debba restare indipendente. Da verificare prima **cosa si vede in gioco** quando uno dei due riposa.

**Nota correlata:** `MarkSpawnPointConsumed` esce subito se l'id non inizia per `sp|`. Quindi per un
nemico registrato come `en|` (boss, incontri a trigger) la morte **non viene marcata** e il nemico
**ritorna al reload**. Per i boss è probabilmente corretto; per gli altri va valutato.

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
