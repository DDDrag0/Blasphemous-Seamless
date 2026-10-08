# TODO — Blasphemous COOP

> **Documento vivo.** Va aggiornato a ogni passo: cosa è stato fatto, cosa ha misurato, cosa resta.
> L'ultima sezione (`Diario dei passi`) è il registro cronologico.

Ultimo aggiornamento: **dopo il passo 0** (strumentazione implementata, deployata, non ancora testata).

---

## STATO DEL PROGETTO

**Il grosso è concluso e verificato.** Il problema rimasto è **uno solo**: le **animazioni tagliate**,
che riguardano i nemici, le morti **e il Penitente**.

| Cosa | Esito |
|---|---|
| Movimento e posizione dei nemici | ✅ funziona |
| Morte animata per mano dell'altro giocatore | ✅ funziona |
| **Fantasmi** | ✅ **risolti** — l'animazione di morte parte; a volte è **troncata**, non assente |
| **Boss fight** | ✅ **un boss condiviso con barra e morte sincronizzata** (meccanismo chiarito, vedi §2) |
| Cutscene e dialoghi | ✅ nessun problema (era un tasto sbagliato: serve SPAZIO, non ESC) |
| Falò: il falò si accende su entrambi | ✅ |
| **Animazioni (nemici, morti, Penitente)** | 🔴 **troncate a pochissimi frame** |
| **Falò: respawn e refill** | 🔴 solo locali: le due macchine si disallineano |

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

**Prova nei log:** `[EnemyAnim]` = 274 righe su G1, 248 su G2; **236/274 (86%)** e **211/248 (85%)**
hanno `avvio=0.00`. `D01Z01S01@SPAWN` ha **100 transizioni (G1) / 87 (G2)** in 29 minuti, con
`765711723 -> 2081823275 avvio=0.00` ripetuto **una volta per frame per ~30 frame di fila**.

### 1b. Il nemico si gira a destra e sinistra velocissimo

**Causa trovata.** La mod scrive l'orientamento 20 volte al secondo, **il gioco lo riscrive ogni
frame**:

- `Ghost::OnUpdate` — confronta la x dello sprite con quella del giocatore e chiama `SetOrientation`
  **ogni frame**, senza banda morta, senza soglia temporale, senza ricordare l'orientamento attuale.
- `TrinityMinionBehaviour::LookAtTarget` — **override che elimina la soglia temporale**, conserva solo
  la banda ±1; il target è selezionabile con `SetTarget(Transform)`. **È il caso "più target".**

**E la protezione del gioco non funziona**: in `EnemyBehaviour::LookAtTarget` il campo `targetTime` è
`private readonly float` e **il costruttore non lo assegna** → vale `0f` → `DeltaTargetTime < targetTime`
è **sempre falso** → resta solo la banda `|dx| <= 1` → inversione ogni frame.

**Soluzioni**: non scrivere l'orientamento dove il gioco lo fa ogni frame; oppure **Harmony** (Postfix
su `Entity::SetOrientation`) — rischio medio.

### 1c. La morte parte e viene skippata

Chiarito dal test: **l'animazione di morte parte**, ma in alcuni casi **viene troncata dopo pochi
millisecondi**. **Non è un problema di morte: è 1a.** **Il capitolo "fantasmi" è chiuso.**

### 1d. 🔴 RIGUARDA ANCHE IL PENITENTE — prima ipotesi individuata, da chiudere

**Il Penitente NON è toccato dalla mod**, verificato in tre modi indipendenti:
- **IL**: `Gameplay.GameControllers.Penitent.Penitent` non ridefinisce `Animator`;
  `Entity::get_Animator` legge `<Animator>k__BackingField`.
- **codice mod**: le uniche interazioni sono **letture** (`MainPlayerManager.cs:83`,
  `Penitent.Animator.GetCurrentAnimatorStateInfo(0)` confrontato con 88 nomi).
- **log**: la stringa `Current animation doesn't exist!`, che comparirebbe se la mod forzasse stati
  arbitrari, ha **0 occorrenze** su G1 e G2.

**MA** — `AnimationFixes.cs` installa **~130** `[HarmonyPatch]` sui metodi
`OnStateEnter/OnStateUpdate/OnStateExit` delle classi
`Gameplay.GameControllers.AnimationBehaviours.Player.*`, **tutti** con:

```csharp
public static bool Prefix(Animator animator) => animator.name == "Body";
```

**Verificato in IL che quei metodi restituiscono `Void`**: con Harmony, un prefix `bool` che
restituisce `false` **salta il corpo originale**. E cosa verrebbe saltato:

| Metodo | Cosa fa (IL) | Cosa si perde |
|---|---|---|
| `MoveAnimationBehaviour::OnStateEnter` | `Animator.set_speed(1f)`, `SetBool("IS_CLIMBING_LADDER", false)`, `Penitent.set_IsGrabbingLadder(false)`, `StepDustSpawner.set_CurrentStepDustSpawn(...)` | **la camminata** |
| `GroundAttackSubStateBehaviour::OnStateUpdate` | `set_speed(PenitentAttack.AttackSpeed)` | **il combattimento** |
| `GroundAttackSubStateBehaviour::OnStateExit` | `set_speed(1)` + `PenitentAttackAnimations.CloseAttackWindow()` + `PenitentAttack.ClearHitEntityList()` | la finestra d'attacco |
| `HurtSubStateBehaviour::OnStateEnter` | `set_IsHurt(true)`, `Physics.EnablePhysics/EnableColliders/Enable2DCollision(true)`, `GrabLadder.EnableClimbLadderAbility(false)` | la reazione al colpo |

> **Se `Animator.name` del Penitente non è esattamente `"Body"`, queste patch uccidono camminata e
> combattimento di un attore che la sync non tocca.** E il `set_speed` saltato è la spiegazione
> **esatta** di "la camminata risulta a pochissimi frame".

**La stringa `"Body"` NON esiste in `Assembly-CSharp.dll`** (verificato: `ldstr "Body"`/`"Slash"` =
**0 occorrenze**): è un nome di GameObject/prefab di scena → **si legge solo a runtime**.

**Il passo 0 lo misura.** Se il nome è `"Body"` → ipotesi chiusa, restano il carico e la diagnostica.
Se è diverso → **è questa la causa**, ed è 🔴.

### 1e. IPOTESI DI CARICO, da misurare (passo 0)

| # | Cosa | Dove |
|---|---|---|
| a | `ApplyAnimation` chiama `IdOf(enemy)`, che fa **scansione lineare di `_ids` con `ResolveEnemy` per elemento** → **O(N²) per pacchetto** | `EnemySyncManager.IdOf` (1028) / `ApplyAnimation` (2100) |
| b | `LogAnimationChanges` gira **ogni frame su tutti gli id**: `ResolveEnemy` + `AnimatorOf` + `GetCurrentAnimatorStateInfo(0)` + 2 `TryGetValue` + 1 scrittura dizionario **per nemico**, con `string` interpolata e I/O sincrono su disco in caso di cambio | `:1994` |
| c | `RegisterEnemies` ogni 0,5 s → `FindObjectsOfType` (**alloca 2 array** via `Resources.ConvertObjects`) + `GetRootGameObjects()` + `GetComponentsInChildren<Enemy>(true)` **per ogni root** | `:872` |
| d | `NetworkManager.ProcessQueue` alloca **per ogni messaggio**: `GetRange(startIdx,2).ToArray()` + `new byte[length]` + copia byte-per-byte → **GC spike** | `NetworkManager.cs:123-167` |
| e | `PvPPatches.cs:54` patcha `Entity.Damage(float, string)` — IL: `public`, **non virtual**, è il percorso di danno di **ogni entità** → il prefix gira **a ogni colpo**, con `string.Replace` che alloca | `PvPPatches.cs:54` |
| f | `RunInBackgroundFix.Apply()` è agganciato **solo** a `NewMainMenu.Awake`, `Landing.Awake`, `LogicManager.SetCurrentLevelConfig`. Il gioco logga `Run in background was true! Correcting.`: **se lo riabbassa durante il gioco nessuno lo rialza** e l'host che perde il focus crolla di FPS | `RunInBackgroundPatch.cs` |

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
| 7b | I nemici `Tutorial` hanno `behaviour=null` e `anim=0` sempre: **non si muovono e non animano nel gioco stesso**. Non è un difetto della mod |
| 7c | Molti nemici hanno `pos_distinte=1` perché **non hanno IA di movimento**: non è un sintomo |
| 7d | **Il protocollo di rete è 11**: mod, server **e `Common.dll`** vanno copiati insieme |
| 7e | **Build = deploy**: il `.csproj` copia sempre in `D:\SteamLibrary\...\Modding\`, quindi anche buildare da una copia di prova sovrascrive la mod installata |
| 7f | **I log non hanno timestamp** (una sola riga datata): niente sequenze temporali fra le macchine e niente frame rate. La durata si ricava dal log del server (in questo test: **1740 s** + 55 s) |
| 7g | **Nessun indicatore di prestazione esiste nei log**: `PingManager` calcola la latenza ma **non la logga mai**. Il passo 0 lo risolve |
| 7h | `RespawnDeadEnemies` non è chiamato dal salvataggio, ma dal **falò** (3 chiamanti in tutto l'assembly) |

---

## 8. PIANO D'AZIONE — 10 passi

Regola del progetto: **un intervento per volta**, test in gioco in mezzo, **rollback immediato** al tag
precedente se peggiora.

| # | Intervento | Perché in questo ordine | Stato |
|---|---|---|---|
| **0** | **Strumentazione**: `Animator.name` del Penitente, FPS, `Stopwatch` su `Update` e `OnLateUpdate`, GC. **Nessun cambio di comportamento** | Decide fra **§1d (le 130 patch)** e **§1e (carico/diagnostica)** *prima* di toccare il codice. Il caso del nome è **binario**. Costa un test, ne risparmia tre | ✅ **implementato e deployato** — `stato-passo0-strumentazione` · ⏳ **da testare** |
| **1** | **Test di controllo**: stesso salvataggio in **single-player con la mod attiva ma disconnessa**, cronometrando la camminata a occhio | Separa "rete" da "non rete" in un colpo. Nessuna modifica al codice | ⏳ da fare insieme al passo 0 |
| **2** | **B-lite**: togliere `animTime = 0f;` (`:1730-1731`) | Massimo beneficio / rischio minimo. Da solo riduce molto il taglio dei one-shot (attacchi, morti) | ⏳ |
| **3** | **B-hash**: da `shortNameHash` a `fullPathHash` in `ReadAnimation`/`ApplyAnimation`, e riposizionare davvero | Chiude il riavvio da confronto d'hash sbagliato. Richiede il passo 2 già verde per isolare l'effetto | ⏳ |
| **4** | **Diagnostica dietro flag**: `LogAnimationChanges`/`LogDiagnosticSnapshot` off di default, `RESCAN_INTERVAL` a 2-3 s | Se il passo 0 mostra FPS bassi, è il rimedio più semplice. **Da rimisurare** con lo stesso strumento | ⏳ |
| **5** | **Solo se il passo 0 mostra `Animator.name != "Body"`**: rimuovere i prefix sulle classi Player e ri-identificare il ghost **per componente** | È l'unico intervento che tocca il Penitente: va fatto **solo con la prova in mano** | ⏳ condizionale |
| **6** | **Falò A1**: pacchetto `PrieDieuRested` → `RespawnDeadEnemies()` sul ricevente, **senza** refill | Indipendente da §1, rischio basso. Dopo la strumentazione perché ora si sa quanto costa | ⏳ |
| **7** | **Oggetti distruttibili**: 4 helper (`PersistentBreakableObject`, `SlashReceiver`, `BreakableObject`, `TriggerBasedTrap`) | Meccanico e isolato, ma **dopo** aver identificato l'oggetto in gioco | ⏳ |
| **8** | **Prestazioni**: `ProcessQueue` senza allocazioni + `IdOf` con dizionario inverso | Pura prestazione, da fare **solo se** i passi 0/4 non hanno già risolto | ⏳ |
| **9** | **Isteresi sul target dei boss**: `targetTime` minimo su `EnemyBehaviour` | La boss fight funziona: qualunque regressione qui costa caro. Ultimo dei lavori "di gioco" | ⏳ |
| **10** | **Modalità spettatore** (§5a) | Idea a bassa priorità, tocca il ciclo di morte del giocatore | ⏳ |

**Motivo dell'ordine**: prima **misurare** (0-1), poi i fix a **rischio crescente e verificabilità
decrescente** (2-3), poi la **diagnostica** (4) perché il suo effetto si misura con lo strumento del
passo 0, poi il **Penitente** (5) solo con la prova, poi i **due difetti funzionali** (6, 7), poi le
**prestazioni** (8), e per ultimo ciò che **già funziona** (9, 10).

---

## 9. DOMANDE APERTE

| # | Domanda | Cosa serve | Stato |
|---|---|---|---|
| 1 | **`Animator.name` del Penitente** — chiude §1d | il log del passo 0 | ⏳ **il passo 0 lo risponde** |
| 2 | **Frame rate reale durante il gioco** — assente dai log | FPS + `deltaTime` massimo dal passo 0 | ⏳ **il passo 0 lo risponde** |
| 3 | **Costo per frame della mod** — e la latenza reale (ignota: `PingManager` non logga) | `Stopwatch` dal passo 0 | ⏳ **il passo 0 lo risponde** |
| 4 | **Gli `shortNameHash` delle due macchine per lo stesso stato** — chiude §1a | log di `shortNameHash` **e** `fullPathHash` per un nemico, 5 s per macchina, poi diff | ⏳ da aggiungere |
| 5 | **Identità dell'oggetto di §4** | il tipo reale in gioco: `multiplayer sync dump` prima del colpo, o log del componente colpito | ⏳ |
| 6 | **Se `targetTime` è davvero `readonly` e non assegnato** (per il passo 9) | verifica in IL dell'assegnazione nel costruttore | ⏳ |
| 7 | **Perché `[EnemyTick]` è ~1,8/s e non 2/s** | misura diretta | ⏳ |
| 8 | **`Received duplicated or inferior progress`** (fino a 22 volte per id) e **83 `Couldn't find connected player:`** su G2: esistono, **non è verificato che siano innocui** | test dedicato | ⏳ |
| 9 | **`BLOCK_UNTIL_FPS_STABLE`** (19 coppie per macchina) **non è** un indicatore di prestazione: è il blocco d'input di `LevelManager` | non usarlo come metrica | ✅ chiarito |

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
| **1** | **Passo 0: strumentazione** | implementata e deployata, **da testare**: `Animator.name`, FPS, `Stopwatch`, GC | **`stato-passo0-strumentazione`** |

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
