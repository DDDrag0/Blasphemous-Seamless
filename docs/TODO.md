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
| **1** | **Animazioni tagliate** (nemici, morti, Penitente) — §1a | 🟡 **riaperta di fatto** | ⚠️ **il churn NON è migliorato**: `animRepeat` fra 11 e 36 in **17 report su 46**, `avvio=0.00` in **484/499**. **È LOCALE all'host** (tutte le 499 righe `mia=host`), concentrato su **un solo nemico** a `x≈-716` con **386 ripetizioni della stessa transizione** `765711723 → 2081823275`. **"Accettabile" resta una scelta dell'utente, non un risultato tecnico.** Vedi §12 |
| **2** | **Orientamento che si gira velocissimo** — §1b | 🔴 **in verifica** | ⚠️ **la prima isteresi NON funzionava** (`orientSkip=0` su 46 report, `orientFlip` 34 in 2 s): era in `ApplyOrientation`, cioè sulla scrittura **della mod** a 20 Hz, mentre il tremolio è **del gioco** a 60 Hz. **SPOSTATA nella Prefix su `Entity::SetOrientation`** (`stato-orient-prefix`). **Da testare** |
| **3** | **Chi si connette dopo non apprende le morti** — §1g | ✅ | **RISOLTO E VERIFICATO**: `[EnemyJoin] annunciate 2`, nessun `ENEMYDEAD` nel progress |
| **4** | **Falò: RESPAWN e REFILL solo locali** — §3 | 🟡 **da confermare** | ⚠️ **lo stato ACCESO del falò è GIÀ sincronizzato** (`PrieDieuHelper.cs`, 3 patch). Questa voce riguarda **solo** il respawn dei nemici e il refill delle fiale, che restano **locali**. **Nessuna prova recente del disallineamento**: il test dopo il fix ha visto i nemici tornare su **entrambe**. **Da verificare con un test dedicato.** Vedi §12 |
| **5** | **Sangue e flash nel feedback del colpo** — §5c | 🟢 | ⏳ **metodi identificati** (`PenitentSword::GetSwordSparks`). **NB: voce re-inglobata, era andata persa** |
| **6** | **Oggetti distruttibili** non sincronizzati — §4 | 🟡 | ⏳ **prima va identificato l'oggetto in gioco** (`Broke wall: 0`, `Used Lever: 0`) |
| **7** | **`RegisterEnemies`** — 84.5 ms e allocazione ~0.4 MB/s — §1e(c) | 🟡 | ⏳ diagnostica dietro flag, `RESCAN_INTERVAL` a 2-3 s |
| **8** | **Boss impazziscono coi target** — §2b | 🟡 | ⏳ **ultimo dei lavori "di gioco"**. **Proposta utente: quanto di tempo sul cambio bersaglio** |
| **9** | **Modalità spettatore** alla morte — §5a | 🟢 | ⏳ idea a bassa priorità, metodi già identificati |
| **10** | **`ProcessQueue` + `IdOf`** (prestazioni) — §8 | 🟢 | ⏳ **declassato**: allocazione sì, stallo no |
| **11** | **Il gioco disfa la pausa sul non-proprietario**: `<Enemy>::OnUpdate` chiama `EnablePhysics(true)` e `StartBehaviour()` su **8 tipi** | 🟡 **reale** | ⏳ il nemico del non-proprietario può "scivolare" o animarsi da solo |
| **12** | **Camminata troncata** | ⏸️ | ⏳ richiede di ripensare il protocollo animazione (**loop vs one-shot**). **NB: voce re-inglobata** |
| **13** | **La migrazione host non riarbitra** (`SetHost` non azzera lo stato del token) | 🟡 **reale** | ⏳ **già avvenuta in un test** |
| **14** | **`IMPRONTA DIVERSA`** | ✅ **RISOLTO** | **ERA UN FALSO ALLARME**: `_listFingerprint` era calcolata solo in `OnEnemiesSpawned`, ma `_ids` viene **appesa** ogni 0.5 s e **rimossa** in `ForgetEnemy` ⇒ la fingerprint descriveva una lista che non esisteva più. **Peggio**: la segnalazione era **una-tantum**, quindi un falso allarme **zittiva ogni divergenza reale**. Ora `_idsDirty` + `RefreshFingerprintIfDirty()` + cooldown 15 s. Vedi §12 |
| **15** | **Il protocollo di rete**: `Common.dll` va copiato insieme a mod e server | 🟢 documentato | ⏳ |
| **16** | ⚠️ **Il deploy va verificato su ENTRAMBE le macchine** | 🔴 **procedura** | ✅ **RISOLTO con uno strumento**: `tools\verify-deploy.ps1` — hash + marcatori + protocollo, esce con codice 1. Vedi §12 |
| **17** | **I nemici che restano fermi (`pos_distinte=1`)** | 🟢 **non è un bug** | ✅ nemici **senza IA di movimento** |
| **18** | **Boss: test boss per boss** | 🟡 | ⏳ l'unica cosa non prevedibile |
| **19** | **`Entity.Dead` calcolato vs `EntityStatus.Dead`** — §7 | 🟢 nota tecnica | ✅ documentato |
| ~~20~~ | ~~Passo 5: le 130 patch sul Penitente~~ | — | ❌ **cade**: `Animator.name == "Body"` su entrambe |
| ~~21~~ | ~~Passo 1: test di controllo single-player~~ | — | ❌ **cade**: la baseline disconnessa c'è già |
| ~~22~~ | ~~Isteresi via `targetTime` su `EnemyBehaviour`~~ | — | ❌ **cade**: `targetTime` è `Private, InitOnly` e **mai assegnato** ⇒ `LookAtTarget` è **codice morto** |

**Poi**, in fondo al file: `## 12. ARCHIVIO DELLE AGGIUNTE` (entry datate) e
`## 13. STORICO DELLE REVISIONI` (le versioni precedenti della todo, conservate).

---

Ultimo aggiornamento: dopo aver **corretto l'isteresi dell'orientamento** (era in `ApplyOrientation`, cioè nel posto sbagliato: ora è nella Prefix su `Entity::SetOrientation`), aver risolto **`IMPRONTA DIVERSA`** (era un falso allarme) e aver aggiunto tre strumenti di misura (`[AnimByEnemy]`, `[AnimName]`, `tools\verify-deploy.ps1`).


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
