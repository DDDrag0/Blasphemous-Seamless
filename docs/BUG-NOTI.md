# BUG NOTI — Blasphemous COOP

> **A cosa serve questo file.** Alcuni difetti **non si riescono a far succedere a comando**: dipendono
> da una combinazione di stanza, tutorial, ordine di ingresso e tempismo che non si riesce a ricreare
> pilotando un test. Sono reali — li hanno visti i giocatori — ma **un test controllato non li produce**.
>
> **Quindi si dichiarano.** Un difetto noto e scritto è un difetto su cui, quando un giocatore lo
> riporta, si sa già dove guardare. Un difetto noto e taciuto è una segnalazione che ricomincia da zero.
>
> **⚠️ E vale la regola del progetto**: *un difetto che non si riproduce **non** è un difetto da aggirare
> con un fix "a naso".* Meglio lasciarlo scritto e aspettare un log.

---

## 🛑 QUESTO FILE SI AGGIORNA **SOLO SU RICHIESTA ESPLICITA DELL'UTENTE**

**Non è un file che si aggiorna a ogni sessione.** Lo decide l'utente, caso per caso.

**Perché.** Il confine fra *"un difetto che non si riproduce"* e *"un difetto che stiamo ancora
indagando"* è **sottile, e sbagliarlo è facile**: la prima stesura di questo file ci è caduta dentro,
elencando tre difetti **ancora in lavorazione** *(il desync del falò col non-host presente, l'AI in
pausa dopo il parry, la divergenza dopo il falò)* che vivono nel `TODO.md` con la loro voce e la loro
priorità — e lì devono restare, **perché non sono abbandonati, sono aperti.**

**⇒ Se lo aggiornasse l'agente a ogni sessione, tornerebbe a riempirsi di lavoro in corso**, e la
sezione perderebbe l'unica cosa che la rende utile: **che tutto ciò che sta qui è roba su cui non
stiamo più lavorando, ma che va riconosciuta se ricapita.**

**Quindi**: *l'agente lo tocca solo quando l'utente dice di toccarlo.*

---

## ⚠️ Il criterio, e cosa NON va in questo file

**Ci va un difetto che non si riesce a far succedere quando si vuole.**
**NON ci va un difetto che stiamo ancora indagando**: *quelli vivono nel `TODO.md`, con la loro voce e la
loro priorità.* Se un difetto è **ancora aperto e in lavorazione**, metterlo qui lo farebbe sembrare
abbandonato — e non lo è.

**La forma giusta di una voce qui**: *cos'è, come si riconosce, cosa si sa della causa, e cosa serve per
capirlo meglio.* **Mai un sintomo senza contesto.**

---

## 1. 🔴 Un'esecuzione seguita dalla morte di un nemico poteva bloccare il gioco — **CHIUSO, ma non sappiamo riprodurlo**

**Cosa si vedeva.** *"G1 uccide un nemico, esce il prompt di esecuzione a G2, G2 esegue l'esecuzione,
diventa **invisibile** nel suo schermo e **fermo** a G1; nel client di G2 non era possibile fare nulla,
**completamente bloccati tutti i tasti**"* — **costretti a chiudere il gioco a forza.**

**Perché sta ancora qui pur essendo chiuso.** **La causa è stata trovata e corretta**, ma il difetto
**non si è mai riuscito a riprodurre a comando**: è capitato due volte in sessioni diverse, in momenti
non pilotati. ⇒ **Se ricapita, va riconosciuto in un minuto**, non re-indagato da zero.

**Cos'era.** `DestroyDeadEnemy` faceva `Object.Destroy` sul GameObject del nemico **mentre il gioco
teneva ancora** `Execution.ExecutedEntity` e la macchina a stati della prop di esecuzione:
- `StuntBehaviour.OnStateUpdate` chiama `GetComponent<EnemyBehaviour>()`, e con l'oggetto distrutto
  l'eccezione nasce **dentro l'icall nativo** ⇒ **514 eccezioni non gestite**, il **78% di quel log**;
- e lo stato `Stunt` **non riesce più a uscire** *(la via d'uscita è l'ultima istruzione di
  `OnStateUpdate`, che l'eccezione salta)* ⇒ viene richiamato **ogni frame**;
- **ma il blocco dei tasti non era il diluvio**: la coroutine di esecuzione
  `SetBlocker(EXECUTION,true) → KillEntity() → ShowPlayer → SetBlocker(false)` **moriva** su
  `KillEntity()` ⇒ penitente invisibile, `IsOnExecution` mai resettato, **blocker acceso per sempre.**

**Fix** *(commit `22d9fec`, tag `niente-destroy-sul-nemico-morto`)*: **`SetActive(false)` invece di
`Destroy`** — il nemico sparisce come deve, **i riferimenti del gioco restano validi.**

### 🔎 Se ricapita, queste sono le firme da cercare nel log

```
[Error: Unity Log] NullReferenceException                       <- in DILUVIO = questo bug
[Error: Unity Log] Ups! There was an unhandled exception: ...   <- e questo
   stack: UnityEngine.Component.GetComponent[EnemyBehaviour]()
          ...StuntBehaviour.OnStateUpdate
   stack: Tools.Level.Interactables.Execution.TriggerAwareness()   <- il prompt
[TRACE][INPUT] blocker EXECUTION ... enabled                    <- e NESSUN "disabled" dopo
```
**⇒ Se vedi `EXECUTION enabled` senza il `disabled` corrispondente, è questo**: *il penitente è
invisibile e i tasti sono morti.*

**⚠️ E c'è un secondo punto, mai osservato**: in `SettleStuckDeaths` è rimasta **la stessa
`Object.Destroy`**, lasciata perché non è **mai** scattata *(`[EnemyDeath]` = 0 in ogni sessione
misurata)*. **Ha un commento nel codice che dice cosa fare la prima volta che appare in un log:**
diventare `SetActive(false)` come l'altra.

---

## 2. Le stanze del tutorial sono inaffidabili — sprite e nemici non respawnano al falò

**Cosa si vede.** Nelle stanze con i **tutorial a sprite**, sedersi al falò **non fa respawnare gli
sprite del tutorial**, mentre **in tutte le altre stanze il respawn funziona**. E in generale quelle
stanze si comportano in modo anomalo: è lì che si è visto il maggior numero di difetti difficili da
riprodurre.

**Perché non lo chiudiamo qui.** Il respawn degli sprite del tutorial è **logica di scena del gioco**,
non nostra: la mod non ha un percorso per i tutorial *(`ShowTutorial` = 0 in tutti i log misurati)*. E
la stanza **mischiata al tutorial** rende ogni altro sintomo ambiguo: *"è un bug del multi o è il
tutorial che si comporta così?"* non ha risposta lì dentro.

**Cosa fare.** **Non testare le meccaniche in quelle stanze.** Si perdono ore su sintomi che non hanno
niente a che vedere col multi. **Se un giocatore le attraversa va bene** — ma un difetto osservato lì va
riportato **e poi riverificato altrove** prima di indagarlo.

---

## 3. I salvataggi cambiano cartella se Steam non è avviato

**Cosa si vede.** La partita sembra **persa o corrotta** dopo un avvio senza Steam.

**Non è corruzione.** Il percorso è
`persistentDataPath + "/Savegames/" + (SteamManager.Initialized ? SteamID : "") + "/"`, quindi
**senza Steam i salvataggi vanno in una cartella diversa** — e con **Steam Cloud** attivo le due
cartelle divergono.

**Cosa fare.** **Avvia SEMPRE Steam prima del gioco.** La mod scrive `[Path]` all'avvio con la cartella
in uso: *quella riga è la risposta a "dove sono finiti i salvataggi".*

**Stato.** Documentato *(voce 36)*. **Non è un difetto della mod e non è fixabile da qui.**

---

## 4. 🔴 L'esecuzione "incrociata" — uno uccide mentre l'altro ha il prompt *(RARO)*

**Cosa si vede.** Un nemico viene **ucciso da un giocatore**, mentre **l'altro** ha sullo **stesso**
nemico il prompt di esecuzione. Per chi aveva il prompt, il nemico può diventare un **fantasma**:
visibile, non colpibile, AI spenta.

**Perché è raro e difficile da riprodurre.** Servono **due giocatori che agiscono sullo stesso nemico
nello stesso istante, ma con ruoli diversi** *(uno lo uccide, l'altro lo esegue)*. Chi guida **non ha il
controllo di entrambe le macchine nello stesso momento**, quindi non lo si può pilotare a comando.

**Stato.** Il caso **generale** del fantasma è **chiuso** *(voce 57: la morte via salute ora disattiva
il cadavere)*. **Questo scenario "incrociato" è il residuo raro.** L'esecuzione in sé non blocca più
il gioco *(verificato: `EXECUTION` ON → OFF, nessun crash)*. **⇒ Se ricapita, serve il log completo, non
un fix a naso.**

**Cosa catturare**, da entrambe le macchine:
```
ACT_...Execution · EXECUTION (enabled/disabled) · TRYING TO KILL ENTITY
[EnemyTick] con l'id COMPLETO del nemico (sp|scena|nome|x|y), prima e dopo l'esecuzione
[EnemyKill] e [EnemySync] non in _ids, per vedere su quale macchina la morte non è arrivata
```

---

## 5. ⚠️ Il giocatore **MORTO** può **USCIRE DALLA BOSSROOM** premendo — **PREZZO ACCETTATO, NON un difetto da riparare**

> **Questa voce è di natura diversa dalle quattro sopra, e va detto per primo.** Le voci 1-4 sono difetti
> **chiusi** o **residui rari** che restano qui *per essere riconosciuti se ricapitano*. **Questa no**:
> è una **conseguenza dell'architettura** che abbiamo **deciso di non chiudere**, perché **l'unico modo di
> chiuderla costa più di quanto rende.** ⇒ **è un PREZZO ACCETTATO**, non un lavoro rimandato.
> **Se ricapita, NON è una regressione e NON va re-indagata**: è il comportamento noto qui sotto.

**Cosa si vede.** Durante la bossfight, **un giocatore morto preme un tasto qualsiasi** mentre è ancora
a terra: **il livello si ricarica** e lui **esce dalla bossroom**. *(Il sintomo è dell'utente; il
meccanismo è verificato nel codice.)*

**Perché succede** *(verificato: `scratch\DIAGNOSI-MORTE-STUCKATA.md`, §2 e §5.2)*. La **schermata di
morte del gioco** ha come via d'uscita `while (!Input.anyKey)` e, quando il tasto arriva, chiama nel
callback del fade `Core.Logic.Penitent.Respawn()` — che **ricarica il livello** *(il load è l'unico punto
in cui il gioco rimette a posto da solo `ResetManager()`, il `FADE` e `DEAD_SCREEN`)*.

**⚠️ E la conseguenza, che è la parte che conta.** Quel load **distrugge la bossroom, il boss e la
cutscene in corso** — sulla macchina del morto. È **lo stesso danno** che il progetto ha già misurato due
volte dal lato opposto: un respawn che scatta mentre la scena del boss è in corso **la cancella**
(`docs\TODO.md:6191`: *"ha respawnato uno spettatore e ha distrutto la cutscene"*).

**Perché NON si blocca l'input** *(verificato: `scratch\DIAGNOSI-MORTE-STUCKATA.md` §4.3 e §5.2)*.
**La schermata di morte legge `UnityEngine.Input` direttamente.** ⇒ **bloccarle l'input non la ferma**:
`SetBlocker` agisce su `Core.Input.InputBlocked`, che è **un altro piano**, e la schermata **non lo
legge**. *(Il file della mod che si occupa della schermata non chiama `SetBlocker` nemmeno una volta:
`DeathScreenTransparency.cs:57`, e la riga che descrive l'uscita è la `:65`.)*

**Perché NON si chiude: la via esiste, ma il rilascio è il problema.** L'aggancio c'è già:
`PlayerDeathSync.cs:377-400` è **già** una patch Harmony su `Penitent.Respawn` *(oggi **misura** e non
sopprime)*, e un `Prefix` che ritorna `false` **impedirebbe** il load. ⚠️ **Ma il rilascio del blocco
dipenderebbe da condizioni che non controlliamo** — un crash, una macchina congelata, un peer che non
torna: **il blocco non verrebbe mai rilasciato** ⇒ **un giocatore bloccato a tempo indeterminato**.
**Un giocatore bloccato è peggio del difetto.** *(È la stessa ragione per cui il progetto ha già scartato
la via gemella: `scratch\STUDIO-TUTTI-NELLA-BOSSROOM.md:191` — "un `Prefix` che non rilascia = ingresso
mai più comandato = stanza bloccata".)*

**✅ E il morto ha già la sua via d'uscita, che è quella del gioco: se NON preme, l'uscita dalla
bossroom è il RESPAWN quando la sequenza si chiude** — ed è **misurata come funzionante**, con la
cutscene intera. ⇒ **il costo di questa voce è zero per chi non preme.**

**⭐ E la mitigazione, che è anche la prova che lo stato è gestibile.** **Se il morto non preme, lo stato
è pulito.** Misurato nella **run B** di `scratch\DIAGNOSI-DUE-RUN.md` *(§(b), §(c))*, dove il morto ha
guardato **tutta** la cutscene *(~23,5 s, `Video End` presente, `CinematicEnded cancelled=False`)*:

| cosa | esito in run B |
|---|---|
| la cutscene del boss | **intera** ✅ |
| la chiusura del gioco (`POP_UP`, `cutscene_block`) | **gira** ✅ |
| la finestra del cadavere-seguace | si chiude **con il respawn** ✅ |
| il giocatore | **torna vivo** e **nessuno resta bloccato** ✅ |

**🔎 Se lo si vuole riconoscere in un log**, le firme sono queste *(canale della morte; i due orologi
`t=` non si mescolano)*:

```
[TRACE][INPUT] The input blocker DEAD_SCREEN has been enabled.      <- la morte (mai spento finche' non si respawna)
core.Logic.Penitent.Respawn  ->  [Morte] il giocatore e' morto: …   <- il tasto E' arrivato (PlayerDeathSync.cs:397)
---> LevelManager: Load and activate level …                        <- IL LOAD: la bossroom e' sparita
   e, subito prima, una cutscene in corso:  CINEMATIC / cutscene_block / FADE accesi
```

**⚠️ E l'altra firma, che è quella che dice "sto per perdere la scena"**: `[TRACE][INPUT] The input
blocker cutscene_block has been enabled.` **seguito da un load invece che** dal `disabled` della
chiusura.

**📌 E le due cose che rendono questa voce verificabile la prossima volta** *(senza di esse un log non
distingue "la schermata non accetta i tasti" da "nessun tasto è arrivato")*:

1. **la riga `[Morte] il giocatore e' morto: …`** *(il `Prefix` di `PlayerDeathSync.cs:397`, che esce
   **sempre** quando `Respawn()` gira)*: se c'è, **il tasto è arrivato al gioco**;
2. **e il load** (`Load and activate level`) **a valle di quella riga**, con una cutscene in corso:
   **allora la bossroom è andata persa** — ed è questa la cosa da non fare.

**Stato.** **PREZZO ACCETTATO.** ⛔ **Non è un difetto da riparare**, e **non è in lavorazione**. Se un
giocatore lo riporta: *"ha premuto mentre era morto, durante la morte del boss"* — **è questo**, ed è già
tutto scritto qui. **Nessun log da raccogliere, nessun fix da cercare.**

---

## 6. ⚠️ Lo **skip** della cutscene del boss **uccide il ritorno in vita** — **RARO · NON RIPRODUCIBILE · RECUPERABILE** *(PREZZO NOTO, non un difetto in lavorazione)*

> **Va detto per primo, come per la voce 5.** Le voci 1-4 sono difetti **chiusi** o **residui rari** che restano
> qui *per essere riconosciuti se ricapitano*; la 5 è un **prezzo accettato**. **Questa è un prezzo noto della
> stessa famiglia:** il difetto **non si riproduce a comando**, e **l'unica cura proposta** — *una finestra con
> `Respawn()` automatico* — **è stata scartata per decisione**, perché **causerebbe lo stesso danno che stiamo
> evitando da giorni** *(una cutscene che salta)*. ⇒ **Non è un lavoro rimandato e non è in lavorazione.**
> **Se ricapita, NON è una regressione**: è il comportamento noto qui sotto, e **la via d'uscita c'è.**

**Cosa si vede.** Durante la **cutscene di morte del boss** il giocatore è **a terra** *(la schermata di morte
aspetta un tasto)*. Se **preme per saltare la cutscene**, la cutscene **si chiude** ma **il ritorno in vita non
arriva**: **resta lì**, schermo fermo e **tasti bloccati**. Nella sessione in cui è stato misurato è rimasto così
**131,81 s**, fino alla chiusura del gioco *(`O_G2:2577` → `O_G2:4067`)*.

**⭐ Il sintomo, nei termini misurati.** *Se **salti la cutscene del boss < 1,5 s prima della sua fine**, il
**ritorno in vita non arriva**: `Penitent.Respawn()` **non viene chiamato**, il blocker **`FADE` resta acceso**,
lo stato resta **`PLAYERDEAD`** ⇒ ⚠️ **il giocatore resta lì.***

**⭐ La causa, misurata.** Il fade della **schermata di morte** dura **1,5 s** e **finisce in `Respawn()`**
*(`DeadScreenWidget.cs:87-96`: `Fade(toBlack: true, 1.5f, 0f, … Respawn())`)*. La cutscene, quando viene
**annullata**, si chiude passando da **`FadeInOut`** *(`Cinematics.EndCutscene:154-190`)*. E **`FadeWidget` ha
UNA SOLA `currentTween`, e chi chiama dopo la uccide** *(`FadeWidget.cs:48-72` e `:190-222`)*. ⇒ Se
`Cinematics.EndCutscene` arriva **mentre quel fade è ancora in volo**, il suo `FadeInOut` **uccide il tween** ⇒
**`OnComplete` non parte** ⇒ **`Respawn()` mai**. E `CHERUB_RESPAWN` *(che **la morte stessa accende**)* è
**esattamente** la condizione che **impedisce a `FadeInOut` di rilasciare il blocker `FADE`**
*(`FadeWidget.cs:212-215`)* ⇒ **schermo nero e input bloccato anche se il giocatore potesse ancora premere.**

**📋 La misura — due sessioni, due esiti opposti dalla stessa causa** *(la fonte: `scratch\TRIANGOLAZIONE-SKIP.md`)*:

| run | tasto → `EndCutscene` | esito |
|---|---|---|
| **vecchia** *(`O_G2`)* | **≈1,3 s** *(il fade da 1,5 s era al ~87%)* | tween **ucciso** ⇒ `Respawn()` **mai**, `FADE` **mai** rilasciato, `PLAYERDEAD` per **131,81 s** |
| **nuova** *(`N_G2`, finestra n.4)* | **≈2,7 s** *(il fade finisce **1,2 s prima**)* | `Respawn()` **chiamato** *(`8475`)*, `FADE` **rilasciato** *(`8535`)*, `PLAYING` *(`8568`)* |

**⇒ La soglia, misurata:** tasto a **≈1,3 s** prima di `VideoEnded` ⇒ **blocca** · a **≈2,7 s** ⇒ **funziona**.
**⭐ E la finestra è ≈9-15% del video** *(1,5 s su 16,5 s di video nella vecchia, su 9,8 s nella nuova)*, **alla
fine** — cioè **proprio dove si preme per saltare**.

**⭐ Quanto è raro.** **Non riproducibile a comando**: l'utente ci ha provato **più volte** e **non ci riesce**.
**⭐ E non è una combinazione di tasti: è un confine al decimo di secondo** — la stessa pressione «a fine video»
cade **di qua o di là** della soglia, e da che parte cade **non si vede**. *(E le due run sono la **stessa build**:
provato in `scratch\TRIANGOLAZIONE-SKIP.md` §1 — il codice è lo stesso, cambia il decimo di secondo. Nella
sessione nuova convivono **4 finestre di morte** e **2 cutscene**: il caso che funziona e quello che blocca hanno
la stessa causa.)*

**🔴 E perché NON si cura con un timer.** La proposta naturale — **una finestra con `Respawn()` automatico**
quando `FADE` è acceso e `PLAYERDEAD` dura da più di *N* secondi — **è stata scartata per decisione**: quel
`Respawn()` **può far saltare una cutscene**, cioè **lo stesso danno** che il progetto evita da giorni *(un
respawn che scatta mentre la scena del boss è in corso **la cancella**: è lo stesso fatto già misurato nella
voce 5, `docs\TODO.md:6191`)*. ⇒ **Sarebbe curare un danno con lo stesso danno.** **SCARTATO per decisione,
non rimandato.**

**✅ E la via d'uscita che esiste già.** ⭐ **Entrare e uscire**: un **cambio di livello** — *e il load è l'unico
punto in cui il gioco rimette a posto da solo `ResetManager()`, il `FADE` e `DEAD_SCREEN`, come è scritto nella
voce 5* — **resetta il fade** ⇒ il giocatore torna comandabile. *(Il comportamento è **riportato dall'utente**;
il meccanismo è quello che il gioco usa già, **non** una mitigazione della mod.)* ⇒ **il costo di questa voce è
basso: c'è una via d'uscita, e non richiede un fix.**

**🔎 Se ricapita, queste sono le firme nel log** *(canale della morte; righe misurate nella run bloccata)*:

```
[TRACE][INPUT] … FADE … has been enabled.            <- IL TASTO: parte Fade(1.5 s, → Respawn)
[TRACE][FADE] Called fade function. Active: True     <- ⚠️ marker d'AVVIO (1 frame dopo), NON di tween ucciso
[TRACE][CUTSCENE] Ending cinematic video.            <- EndCutscene: qui FadeInOut uccide il tween (≈1,3 s dopo)
[Cutscene EV] … VideoEnded · CinematicEnded cancelled=True
   e poi NIENTE: nessun "[Morte] il giocatore e' morto", nessun "FADE … has been disabled", nessun PLAYING
```

**⚠️ E il contatore che lo riassume:** `Called fade function. Active: True` **6** contro `… False` **5** nella run
bloccata ⇒ **un fade acceso non è mai tornato** *(quello del tasto)*; nella run che funziona sono **16 e 16**,
bilanciati. **Un fade che non torna = un respawn che non c'è.**

**📌 E il marker che servirebbe** *(oggi **non esiste**)*. Una riga che dica **quando** il fade è **in volo** e
**quando** arriva `EndCutscene`: `Called fade function` **è un marker d'avvio, non di tween ucciso** *(misurato:
nella finestra n.4 sta **1 frame dopo** il `FADE` acceso, mentre il `Respawn()` arriva **1,4 s dopo** ⇒ si stampa
all'inizio, non alla fine)*. **È l'unica riga che renderebbe questo caso diagnosticabile da un log** invece che da
una ricostruzione: senza, un log **non distingue** «il tween è stato ucciso» da «il tasto non è arrivato».

**Stato.** **RARO · NON RIPRODUCIBILE · RECUPERABILE.** ⛔ **Non è un difetto in lavorazione**: la sola cura
proposta *(il `Respawn()` a timer)* è **scartata per decisione**, perché **farebbe saltare le cutscene**. ✅ **E
non lascia bloccati**: **entrare e uscire** resetta il fade. 📌 **Se un giocatore lo riporta** — *"ho premuto per
saltare la cutscene del boss mentre ero morto, ed è rimasto lì"* — **è questo**, ed è già tutto scritto qui:
**serve il log, non un fix a naso.**

**⚠️ E il legame con `docs\TODO.md`, dichiarato invece che taciuto.** In `docs\TODO.md` *(§2.1, `:6801`, e la
lista corta a `:6907`)* la finestra dei 1,5 s compare **ancora come "in attesa di una decisione"**, e la sua
opzione 1 è **proprio** il `Respawn()` automatico. **Questa voce registra la decisione presa**: il timer **non si
fa**, e il difetto **resta noto e accettato**. *(La voce nel `TODO.md` resta dov'è: **non è stata toccata**.)*

---

## 7. ⚠️ I **TRE BUCHI** DEL RELAY DEGLI EFFETTI — effetti e proiettili che **non arrivano all'altra macchina** *(⚠️ **uno dei tre è chiuso nel codice**: la risoluzione dei prefab dei nemici — vedi "Stato"; **due restano aperti** — LIMITE DICHIARATO, prezzo accettato, NON un difetto in lavorazione)*

> **Va detto per primo, come per la voce 5 e la 6.** Le voci 1-4 sono difetti **chiusi** o **residui rari**, da
> riconoscere se ricapitano; la 5 e la 6 sono **prezzi accettati**. **Questa è della stessa famiglia, in una forma
> più semplice**: non c'è nessuna decisione da prendere, perché **la forma del lavoro è già nota** *(due hook
> mirati — la strada di risoluzione è fatta, §"Il fix" qui sotto)*. È nei bug noti **per decisione dell'utente**, con
> queste parole: *«penso che non lo testeremo per adesso: da testare assieme a tutti i boss»*. ⇒ **è un LIMITE
> DICHIARATO**, non un lavoro rimandato, e **se ricapita non è una regressione**: è il comportamento noto qui sotto.

**Cosa si vede.** Il relay degli effetti *(`958a07f`)* copre **solo** ciò che passa dal `PoolManager`. Tre
categorie **non ci passano** (o non ci arrivano), e il risultato sul giocatore è **doppio**:
- **gli effetti** di quelle categorie **non compaiono** sulla macchina dell'altro giocatore: il giocatore vede il
  boss caricare, e **il colpo materializzarsi a metà** — o non materializzarsi affatto;
- **i proiettili** sono peggio, e sono **due sintomi distinti**: **(a)** il proiettile **non si vede**, e
  **(b)** *a seconda di chi è l'attaccante dell'`Hit`* **il danno non arriva affatto** — vedi la correzione qui
  sotto, che è la parte da leggere prima di metterci mano.

### ⚠️ Le tre categorie, una per una

| # | buco | dove | perché resta fuori |
|---|---|---|---|
| **1** | `BossJumpAttack.InstantiateArea` — i **due `Object.Instantiate`** degli oggetti del salto | `BossJumpAttack.cs:269-277` | non passa dal `PoolManager` ⇒ **nessun hook lo vede**. Per `ElderBrother` **non è esercitabile** *(`instantiateOnStart`/`instantiateOnEnd` = `<null>`)*; **per le Amanecidas sì** |
| **2** | `BossTeleportAttack.InstantiateArea` — gli oggetti del teletrasporto | `BossTeleportAttack.cs:73-81` | idem, un `Instantiate` grezzo. **Nessun boss misurato lo usa** |
| **3** | i **proiettili dei nemici comuni** — la famiglia dichiarata come «`ProjectilePool` privato»: **`BellGhostVariant` · `RangedBoomerang` · `MeltedLady`** | `ProjectilePool.cs:12-33` · i tre attacchi (sotto) | **il buco era vero, la causa scritta nei referti no** — e **sul ricevente è chiuso nel codice** *(`36a8878`: la sorgente ② li cerca nei **loro** campi)*: resta **il test in gioco** *(vedi "Stato")* |

- ⚠️ **E restano fuori, per dichiarazione esplicita** *(come già scritto nel referto)*: i **~18 `Object.Instantiate`
  grezzi** nei boss *(Piety 3, Snake 2, Crisanta 2…)*, i **cosmetici** fuori dal filtro *(`CorpseAnimationFx`,
  `LandingFx`, `CorpsesParticlesJump` — i loro pool **esistono**, 30 / 1 / 1 istanze, ma non feriscono)*,
  **camera e audio**, e **tutto il canale del giocatore** *(`EffectType`: solo **3** membri istanziano un prefab dal
  pool, e sono tutti cosmetici)*.

### ⚠️ Una correzione alla fonte, perché un hook che non serve si paga due volte

**Il referto dice** *(`scratch\REFERTO-RELAY-EFFETTI.md` §6, ripetuto in `DIAGNOSI-VOCABOLARIO-EFFETTI.md` §a)*:
*«`ProjectilePool` **privato**, usato da **3 nemici** ⇒ i loro proiettili non sono sincronizzati»*. **Sul
decompilato questo non regge, e va corretto qui** *(è la stessa lezione di `docs\TODO.md` §4: «una catena
plausibile non è la catena vera»)*:

- **`ProjectilePool.Spawn` non è chiamato da nessuna parte** nel gioco: **0 chiamate** su tutta `Assembly-CSharp`
  *(`ProjectilePool.cs:15-32`)*, e `Initialize`/`StoreProjectile` sono chiamati **solo dentro la classe stessa**
  *(`:34-50`)* ⇒ **quel pool, nella build che giochiamo, non si usa.**
- **I tre nemici passano dal `PoolManager`**, non dal pool privato: `BellGhostVariantAttack.cs:39`,
  `MeltedLadyAttack.cs:40`, `RangedBoomerangAttack.cs:40` chiamano tutti
  `PoolManager.Instance.ReuseObject(projectilePrefab, …)`, e creano il loro pool con
  `PoolManager.Instance.CreatePool(projectilePrefab, 1|2)` *(`:31`, `:25`, `:25`)*.
- Il campo `ProjectilePool` che dichiarano **serve solo come ancoraggio di posizione**:
  `PoolManager.Instance.ReuseObject(projectilePrefab, pool.transform.position, …)` — **è un `Transform`, non un
  pool**.
- ⇒ ⭐ **il canale del relay VEDE quelli spawn** *(`IsInteresting`: `Projectile`/`Weapon` nei figli del prefab,
  `EffectRelay.cs:1429-1457`)*. **E allora perché non arrivavano?**

**Perché il ricevente non lo sapeva ricostruire — e cosa è cambiato.** Il catalogo del relay era costruito **solo
dai prefab del boss locale** *(`FindBosses`, namespace `Gameplay.GameControllers.Bosses`;
`EffectRelay.cs:1076-1119`)*, e la risoluzione era **una sola domanda**: *«il nome è un effetto del **mio** boss?»*.
Un proiettile di un **nemico comune** non sta in nessun campo di nessun boss ⇒ ***`[FxRelay] NON RISOLTO "<nome>"
provate: boss,nemici,penitente`*** ⇒ **la copia non nasceva, e sull'altra macchina il proiettile non esisteva.**
**✅ E questo è il pezzo che è stato chiuso** *(`36a8878`)*: la risoluzione ha ora **quattro sorgenti in ordine, e si
ferma alla prima che riesce** *(`Resolve`/`ResolveCore`, `EffectRelay.cs:788-901`)* — **① i campi del boss** *(il
catalogo, come prima)* → **② i campi dei nemici in scena**, anche spenti *(`SearchEnemies`, `:910-971`)* → **③ i campi
del `Penitent` locale** *(`:979-1013`)* → **④ la cache per nome**. ⚠️ **Il canale non c'entrava**: i `mando` c'erano
già *(riga sopra)*; era **il catalogo del ricevente** a non trovare un nome che il mittente aveva mandato.

**⚠️ E la conseguenza, che ha scelto il fix**: la cura **non è un hook sul `ProjectilePool`** *(sarebbe un hook su
codice che il gioco non esegue)*: è **allargare il catalogo del ricevente ai nemici comuni**, cioè dargli una
seconda strada di risoluzione — **ed è quello che è stato fatto** *("Il fix", punto ②)*. ⚠️ **Resta vero, però, che
`ProjectilePool` è fuori dal `PoolManager`**: **se un giorno qualcosa ci passasse davvero, nessun hook lo
vedrebbe** — è un'affermazione sul **canale**, e la risoluzione non la copre.

### ⚠️ Perché è un buco di **gameplay**, non solo di immagine

- **Per le aree relayate** il danno **arriva dalla copia del ricevente** *(non da un pacchetto:
  `REFERTO-RELAY-EFFETTI.md` §4)*. ⇒ **se la copia non nasce, non c'è nessuno che ferisca** il `Penitent` del
  peer: **manca l'effetto = manca il colpo**. Non è un problema di disegno.
- **Per i proiettili** il conto si fa in due, e va detto con precisione perché è la differenza fra un buco visivo
  e un buco di danno:
  - **`MeltedLady` e `RangedBoomerang`**: l'`Hit` è costruito con **`AttackingEntity = il proiettile`**
    *(`MeltedLadyAttack.cs:42`, `RangedBoomerangAttack.cs:42`)*. Il filtro del ② vuole un **`Enemy`** fra i parent
    dell'attaccante *(`EnemyHitPeerSync.cs:288-294`)*, e il proiettile nasce **sotto il pool**, non sotto il
    nemico ⇒ il `[DannoPeer]` lo **scarta** *(`non inoltrato: l'attaccante non e' un NEMICO`)*. ⇒ **effetto
    assente + danno assente: buco di gameplay pieno.**
  - **`BellGhostVariant`**: `SetOwner(owner.gameObject)` mette **il nemico** come `AttackingEntity`
    *(`ProjectileWeapon.cs:415-418`, `BellGhostVariantAttack.cs:42`)* ⇒ il ② **può** inoltrarne il danno. ⚠️ **Il
    danno può quindi arrivare anche se il proiettile non si vede**: è **una delle cose da misurare** nella run dei
    boss, non da dare per scontata in nessuna delle due direzioni.

### ⭐ Il fix: **una parte è fatta, e restano due hook**

1. **`BossJumpAttack.InstantiateArea`** e **`BossTeleportAttack.InstantiateArea`**: **un `Postfix` per metodo**,
   che manda `prefab + posizione` sul **canale che esiste già** *(tipo `CoopEffectRelay`, 201)*. È **lavoro
   meccanico**: non è ricerca, la forma è quella del relay. ⛔ **I due `InstantiateArea` restano da fare** *(2 hook
   in più)*: **nessun hook li vede**, ed è il buco che **nessun log può smentire** — *il segnale è il silenzio*.
2. **I proiettili dei nemici comuni**: **una strada di risoluzione in più sul ricevente** *(il catalogo non è più
   «solo il boss»)*. ⚠️ **Non un hook sul pool privato**, per la ragione qui sopra. ✅ **FATTO** *(`36a8878`)*: le
   **quattro sorgenti ①②③④** *(§"Perché il ricevente non lo sapeva ricostruire")*. ⇒ **il meccanismo è provato**
   *(il **canale**: in gioco, con un altro nemico; la **sorgente ②**: misurata offline sui tipi veri)*; quello che
   resta su `BellGhostVariant` · `RangedBoomerang` · `MeltedLady` è **un test in gioco**, non del codice *(vedi
   "Stato")*.

⇒ **Due punti aperti, tutti confinati**, e **nessuno di essi tocca la vita, la morte o il flag**: sono istanziazioni.

### 📌 E cosa fare nel frattempo: **niente da nascondere**

**Si sa, e si dichiara.** Se un giocatore riporta *«il proiettile non si vede»* o *«quel colpo non mi ha fatto
niente»*, **non è una regressione e non va re-indagato**: è uno dei buchi di questa voce *(`RISOLTO`/`NON RISOLTO`
dicono **quale**)*, ed è già tutto scritto qui. **Nessun log da raccogliere per riconoscerlo** — le firme qui sotto
bastano.

### 🔎 Se ricapita, queste sono le firme nel log

```
[FxRelay] mando n=… prefab="<il nome del proiettile>" …     <- c'e' sul mandante: il canale lo vede
   e sull'altra macchina, per quel nome, una delle due:
[FxRelay] RISOLTO "<idem>" da=<boss|nemici|penitente|cache> in=<ms>ms mittente=<peer> (…)  <- ⭐ la sorgente che ha vinto
[FxRelay] istanzio prefab="<idem>" pos=(…) rotZ=… da=<peer>   <- la copia nasce dal SUO pool
   oppure, se nessuna delle sorgenti conosce il nome:
[FxRelay] NON RISOLTO "<idem>" provate: boss,nemici,penitente mittente=<peer> (in=<ms>ms, catalogo boss=N nomi, …)
[FxRelay] riepilogo scena=… mandati=N istanziati=0 scartatiPerDedup=0 nonRisolti=M catalogo=K
```

**⚠️ E la riga `NON RISOLTO` non è più filtrata dal tetto di una riga al secondo**: **una riga per nome per scena**,
e nessuna si perde — prima un nome nuovo poteva essere **ingoiato dal throttle**, e una firma che perde righe non è
una misura.

**⚠️ E per i due boomerang, la riga che dice che il danno non c'è**: nessun `[DannoPeer] colpo del NEMICO
inoltrato`, e al suo posto *(throttled)* `[DannoPeer] … non inoltrato: l'attaccante non e' un NEMICO`. **Per gli
`InstantiateArea` invece il segnale è il silenzio**: nessuna riga `mando` per quei prefab, perché **l'hook non
esiste** — *è il buco che nessun log può vedere*.

**Stato.** **UNA PARTE CHIUSA, DUE BUCHI APERTI — limite dichiarato, prezzo accettato.** ⭐ **La risoluzione
multi-sorgente** *(strade ①②③④, `36a8878`)* **chiude il buco dei prefab dei 3 nemici e del `Penitent`**: il
**canale** li vedeva già *(`mando`)*, era **il catalogo del ricevente** a non trovarli — e adesso li cerca anche nei
**loro** campi. ⇒ ⚠️ **"i 3 nemici" *(BellGhost · RangedBoomerang · MeltedLady)* non sono quindi più un buco di
codice**: sono **osservabili da testare in gioco** *(il canale è provato in gioco con un altro nemico, la sorgente ②
è misurata offline sui tipi veri: manca **la scena viva**; da leggere `RISOLTO "…" da=nemici in=…ms`, e nel riepilogo
`nomi=[… nemici:K …]` con K > 0)*.
⛔ **Restano aperti, e sono ancora solo codice, i 2 `InstantiateArea`** *(BossJump · BossTeleport ⇒ **+2 hook**)*:
**nessun hook li vede**, e per quelli **il segnale è il silenzio**. ⛔ **Non è un difetto in lavorazione**, per
decisione dell'utente. 📌 **La voce di lavoro resta in `docs\TODO.md` §2.4**, che **non è stata toccata**: qui c'è
**cosa dichiarare**, là c'è **cosa fare**.

---

## 8. ⚠️ I **BOSS**: la **mira** resta sul padrone *(23)* e **12 su 17** senza la **morte recitata** — *(LIMITE DICHIARATO, prezzo accettato — NON un difetto in lavorazione)*

> **Stessa premessa della voce 7, e vale per entrambi i punti.** Non sono difetti che stiamo inseguendo: sono
> **due limiti per boss**, **non testati in massa**, e l'utente ha deciso di dichiararli invece di lavorarci adesso:
> *«penso che non lo testeremo per adesso: da testare assieme a tutti i boss»*. ⇒ **limiti dichiarati, non difetti
> in lavorazione**: se un giocatore li riporta, **è questo**, e **non è una regressione**.

**Cosa si vede.** Due cose diverse, entrambe **per boss**:
- **A)** il boss **sembra ignorare** l'altro giocatore: continua a girare, saltare e caricare **addosso al
  giocatore della macchina che lo simula**, anche quando l'altro gli sta più vicino;
- **B)** nella macchina del giocatore **morto**, il boss **resta "in piedi"**: `Kill()` è passato, ma **la
  sequenza di morte del gioco non parte** ⇒ niente animazione, sprite acceso. Il cadavere del boss **non resta
  visibile al rientro** *(`[BossCorpse]`, il fix C)*, ma **la morte non è recitata**.

### A) ⚠️ LA MIRA RESTA SUL PADRONE — **un boss su ventiquattro**

**Cosa.** Solo `ElderBrother` ha la mira che segue il **proxy d'aggio**: `f3084ea` *(il `Postfix` su
`GetTargetPredictedPos`, `BossJumpAimPatch.cs:53-60`)*. **Gli altri 23 no**: la loro mira legge
**`Core.Logic.Penitent` direttamente** — **233 letture dirette** nei namespace `Gameplay.GameControllers.Bosses.*`
*(conteggio sul decompilato, `scratch\REFERTO-SALTO-PROXY.md` §5)*:

| classe | letture dirette di `Core.Logic.Penitent` |
|---|---|
| `Bosses.Amanecidas.AmanecidasBehaviour` | 50 |
| `Bosses.Snake.SnakeBehaviour` | 25 |
| `Bosses.Isidora.IsidoraBehaviour` | 17 |
| `Bosses.Crisanta.CrisantaBehaviour` | 17 |
| `Bosses.PontiffHusk.PontiffHuskBossBehaviour` | 17 |
| `Bosses.PontiffOldman.PontiffOldmanBehaviour` | 11 |
| `Bosses.BlindBaby.WickerWurmBehaviour` | 11 |
| `Bosses.PontiffGiant.PontiffGiantBehaviour` | 10 |
| *altre 40 classi* *(BurntFace, PontiffSword, HighWills, Perpetua, Quirce, Esdras, Lesmes, TresAngustias, BejeweledSaint, PietyMonster…)* | 75 |
| **totale** | **233** |

**Effetto sul giocatore.** ⇒ **il boss insegue sempre il giocatore della macchina che simula**: per l'altro, il
boss **è come se non lo vedesse**. *(L'aggio però è a posto: `ApplyAggroTarget` scrive `enemy.Target = proxy` per
ogni nemico, boss compresi, e dopo il ② quel bersaglio **è anche feribile** ⇒ **il boss che mira al peer lo
colpisce**. Solo **la mira** dei 23 non legge `Target`.)*

**⭐ Il fix, per ogni boss.** **La stessa forma di `f3084ea`** — un `Postfix` sul punto di mira che scrive la
posizione del **proxy** quando il target *è* il proxy, e **niente** quando non lo è — **ma va trovato il punto
giusto per ognuno**: `GetTargetPredictedPos` **esiste una volta sola** in tutta `Assembly-CSharp` *(`:56000`)*, e
nel decompilato non c'è nessun altro membro con `Predict` nel nome ⇒ **non si copia**: si guarda **quale metodo**
quella classe usa per mirare, e si patcha quello. Le **233 letture sono la mappa di dove guardare**, non 233
modifiche.

**⚠️ E per il test è una cosa per boss, non una run**: la prova è *«il boss carica addosso al giocatore che sta
bersagliando»*, e va guardata **sulla macchina che simula**.

### B) ⚠️ 12 BOSS SU 17 SENZA LA **MORTE RECITATA**

**Cosa.** La lista bianca di `Death()` copre **5 tipi** *(`EnemySyncManager.cs:398-406`)*: **`ElderBrotherBehaviour`
· `LesmesBehaviour` · `QuirceBehaviour` · `PerpetuaBehaviour` · `EsdrasBehaviour`*. **Gli altri 12 sono esclusi di
proposito** — il loro `Death()` tocca il **`Penitent`**, l'**input** o la **camera**, e **avrebbe fatto più danni
che l'AFK che lascia**:

| tipo escluso | cosa fa il suo `Death()` *(e perché è vietato)* |
|---|---|
| `Amanecidas` | `Penitent.Status.Invulnerable = true` |
| `BurntFace` | `Invulnerable` **+** `ProCamera2DShake` |
| `Crisanta` | `Penitent.DrivePlayer.MoveToPosition` *(ramo redux e non redux)* |
| `Isidora` | `Invulnerable` **+** `MasterShaderEffects` **sul `Penitent`** |
| `PontiffGiant` | `Invulnerable` |
| `PontiffHusk` | `Invulnerable` **+** `SetBlocker("END_BOSS_DEFEATED"/"HW_…")` **+** scroll/camera di fine scontro |
| `PontiffOldman` | `Invulnerable` |
| `PontiffSword` | `Invoke("ActivatePlatform")`: tocca la **geometria della stanza** |
| `Snake` | `Invulnerable` **+** `SetBlocker("TPOReposition")` |
| `TresAngustias` | `Invulnerable` **+** `ProCamera2DShake` **+** `SetBlocker("CINEMATIC")` |
| `WickerWurm` | `Invulnerable` **+** `Unattacable` *(via `StartDeathSequence`)* |
| `SingleAnguish` | niente di vietato, **ma non è un boss di scena**: la sua morte la guida `TresAngustiasMasterBehaviour` *(fuori whitelist)*, e invocarla da sola lascerebbe **il trio in uno stato intermedio** |

*(Tabella verificata tipo per tipo sul decompilato: `scratch\REFERTO-A-C-MORTE-BOSS.md` §2; il motivo per esteso è
nel commento del codice, `EnemySyncManager.cs:378-397`.)*

**Effetto sul giocatore.** Per quei 12, **sulla macchina del morto il boss non recita la morte**: `Kill()` porta
`Status.Dead` a vero e fa partire la cutscene, ma **non passa da `Behaviour.Death()`** *(la chiama solo il colpo
letale di `Damage(Hit)`, che lì non può arrivare: la guardia `Penitent.Life <= 0` lo scarta)*. ⇒ **il boss resta
"in piedi"**, immobile, con lo sprite acceso e la tree ferma. **Il fix C lo copre in parte** *(il cadavere non
resta visibile al rientro, `[BossCorpse]`)* — *in parte*: **la morte recitata è un'altra cosa**, ed è quella che
manca.

**⭐ Il fix.** **Un tipo alla volta, guardando cosa fa il suo `Death()`**: la whitelist si allarga **solo** se quel
metodo non tocca il `Penitent`, l'input, la camera o la geometria. Per i 12 qui sopra **non è una svista**: è la
lettura che dice di no. ⇒ **il lavoro è leggere 12 metodi**, non allargare un elenco.

### ⚠️ I due conteggi non si sommano, e confonderli fa sbagliare i lavori

**17** sono le **classi di boss censite per la morte recitata** *(5 dentro, 12 fuori)*; **23** sono gli **altri boss
della famiglia della mira** *(`ElderBrother` + 23)*. **Non sono lo stesso insieme** e **non si sommano**: un boss
può stare nel primo elenco e non nel secondo, e viceversa.

### 🔎 Se ricapita, le firme nel log

```
[SaltoProxy] il salto mira al PENITENT locale come il gioco: ElderBrother … distanza=0.1
   <- questa riga esiste SOLO per ElderBrother: per gli altri 23 il marker non c'e', perche' la patch non c'e'
```

```
[BossDeath RX] en|…|D17Z01S11|… morte=recitata:EsdrasBehaviour        <- la morte E' stata recitata
[BossDeath RX] en|…|<Scena>|…      morte=escluso:SnakeBehaviour-non-in-whitelist   <- ⚠️ il boss resta in piedi
```

**⚠️ E la riga `[BossDeath RX]` esce solo sulla macchina del giocatore MORTO** *(`ApplyBossDeath` esce prima con
`motivo=giocatore-locale-vivo`)*: cercarla sulla macchina viva **non trova niente, e non è un guasto**.

### 📌 E perché sono nei BUG NOTI

**Perché l'utente l'ha detto, e la ragione è sua:** *«penso che non lo testeremo per adesso: **da testare assieme a
tutti i boss**»*. ⇒ ⭐ **sono LIMITI DICHIARATI, non difetti in lavorazione**: due lavori **per boss**, con la forma
già provata su `ElderBrother` e con la lista dei 12 già letta. **Se un giocatore riporta** *«il boss mi ignora»* o
*«il boss è rimasto in piedi da morto»*, **è questo** — **non serve un log nuovo per riconoscerlo**.

**Stato.** **LIMITE DICHIARATO — prezzo accettato.** ⛔ **Non è un difetto in lavorazione**: la mira è **provata su
un boss** *(`f3084ea`)* e per gli altri è **il conteggio dei 233 siti**, non una run; la morte recitata è **provata
su un tipo**, e per gli altri è **la tabella**, non una misura *(sta già scritto in `docs\TODO.md` §6, punti 4 e
5)*. 📌 **Le voci di lavoro restano in `docs\TODO.md` §2.5**, che **non è stata toccata**: qui c'è **cosa
dichiarare**, là c'è **cosa fare**.

---

## 9. ⚠️ I **QUATTRO BOSS** senza flag di morte "classico" — **Amanecidas · Perpetua · PontiffHusk · Viridiana** *(⚠️ **DA TESTARE** — non è un difetto accertato: **mancano i dati**)*

> **La distinzione con la voce 10, che le sta accanto, è il punto di tutta questa coppia.** La **10 è verificata
> nel codice**: si legge, e la catena si chiude. **Questa no.** Qui **non c'è un difetto accertato**: c'è **una
> domanda a cui manca la misura**. ⇒ ⭐ **è una cosa DA TESTARE, non una cosa da fixare** — e il test **non è un
> esperimento da inventare, è un'osservazione da fare**: una run, e una riga di log da cercare. Finché quella riga
> non esiste, mettersi a "sistemare" qui sarebbe **il fix a naso** che il progetto ha già dichiarato di non volere.

**I quattro.** `Amanecidas` · `Perpetua` · `PontiffHusk` · `Viridiana`.

**⭐ Perché sono diversi, e non è un dettaglio di tassonomia.** Sono **DLC o eventi aggiuntivi**, iniettati in un
gioco la cui **architettura dei boss era già sigillata**. Per **non toccare i salvataggi** esistenti *(un flag
nuovo in un salvataggio vecchio è un campo che non esiste)*, il loro stato è **locale** e il completamento va su
**sotto-sistemi separati** *(il contatore di **Jibrael** per le Amanecidas, il percorso **arcade** per Husk, lo
**script** di Viridiana, l'incontro **irripetibile** di Perpetua)*. ⇒ **non hanno un flag di morte "classico"** da
leggere, e non è una svista degli altri tre: è **la loro forma**.

**✅ Quello che SAPPIAMO** *(verificato)*:
- il flag **`<scena>_BOSSDEAD` non esiste** per loro: nessuno dei quattro è ricavabile dal nome della scena;
- ⭐ la mod **propaga l'UCCISIONE** — *un'entità la cui vita è arrivata a zero* — **non** `Status.Dead`; e sul
  ricevente **ricostruisce** `Status.Dead` dal predicato **`Entity.Dead || CurrentLife <= 0`** *(`:5816` lato
  invio; `Kill()` / `KillInstanteneously()` / `Damage(Hit)` lato ricezione)*;
- ⇒ ⭐ **quindi il canale potrebbe già coprirli**, senza un flag nuovo e senza una riga di codice nuova;
- ⚠️ e `Status.Dead` **non è** il predicato di rilevamento: in **ogni** punto in cui la mod lo legge è una
  **guardia di idempotenza** — *"la sequenza di morte è già partita"* — (`:465`, `:6302`, `:6362`, `:6714`,
  `:7100`, `:7136`) e **mai** la domanda *"è morto?"*.

**⚠️ Quello che NON sappiamo, ed è la domanda intera.** Se la loro morte passi davvero da **`CurrentLife <= 0`
sulla macchina che li uccide**. Non è una domanda oziosa: la lettura per sotto-sistemi dice che si spengono
**fuori dal canale della vita** — cioè **esattamente** il caso in cui il predicato `:5816` può non vedere niente.
⇒ ⭐ **una domanda sola:** *la morte di questi quattro porta la vita a zero **sulla macchina che li uccide**, o la
spegne da un'altra parte?*

**📋 IL TEST — una riga, e va scritta come osservazione.** **Uccidere uno dei quattro** e cercare nel log la riga
dell'uccisione:

```
[EnemyKill] …|<Nome>|… MORTO Dead=… vita=0 … attivo=… pos=(…)
   <- la ricerca vera e' "[EnemyKill]" + "MORTO" + "vita=0" (esce con un decimale: 0,0 oppure 0.0)
```

| esito | cosa vuol dire | cosa fare |
|---|---|---|
| **la riga c'è** | ⭐ il canale **li copre già**: il predicato li vede e la morte viaggia | **niente da fare** — la voce si chiude **con la misura**, non con un fix |
| **la riga NON c'è** | il predicato **non li vede**, e la morte si spegne per un'altra strada | ⚠️ **il fix è lì**: estendere il rilevamento a `Status.Dead` — la sola riga che il `docs\TODO.md:7567-7571` prevede |

**⚠️ E i limiti del test, da dire prima di chiederlo a qualcuno.** (1) ⚠️ **Servono i DLC**: chi non li ha **non
può** fare questa prova, e non c'è modo di aggirarla. (2) ⛔ Nei log che abbiamo **i quattro hanno 0 righe** —
`Amanecida|Perpetua|Viridiana|PontiffHusk|Husk|Jibrael|arcade` non compaiono **mai**: **nessuno dei quattro è mai
stato ucciso in co-op**, quindi oggi la domanda è **senza risposta per assenza di dati**, non per difficoltà.

**⚠️ E una parte è dichiarata non verificata, e va tenuta così.** La lettura per **sotto-sistemi** — il contatore
di **Jibrael** *(Amanecidas)*, il percorso **arcade** di Husk, lo **script** di Viridiana — riposa su **una sola
riga** di `docs\TODO.md` (`:7568`), **mai verificata**: **nessun referto** la nomina. ⇒ **non è un fatto, è una
dichiarazione**: serve a dire *dove guardare*, non *cosa succede*.

**📌 NOTA SULLE RIGHE — i riferimenti dei referti sono in ritardo.** Molti referti e il `TODO` citano la **whitelist**
a `EnemySyncManager.cs:378-386`: **a HEAD `599b796` è a `:398-406`** *(misurato: **20 righe di scarto**)*. E
**`FirstBossDeadFlagOn`** — il **solo** lettore di `_BOSSDEAD`, cioè il fix C — è citato `:836-856` **ma è a
`:966`**. ⇒ **valgono i numeri misurati a HEAD**, non quelli dei referti.

**Stato.** **DA TESTARE — non un difetto accertato.** ⛔ **Non si tocca niente finché non si vede il log di una
kill**, e **non è in lavorazione**. ✅ Il test costa **una run con i DLC e una ricerca nel log**. 📌 **Se un
giocatore riporta** *"l'ho ucciso io e dall'altro è ancora in piedi"* **su uno di questi quattro**, quel log **è**
la misura che manca: va preso **prima** di toccare il predicato.

---

## 10. ⚠️ IL **LATE JOINER** E IL BOSS MORTO — il prefisso `bdead|<id>` non si ripete, **ma il flag sì**: la conseguenza è **smentita**, il residuo vero sono **i 4 della voce 9** *(✅ **VERIFICATO NEL CODICE** — il fatto **resta**, l'effetto **no**)*

> **Questa voce è stata RISCRITTA, e va letta come la coda della 9.** Diceva *«il late joiner non sa che un boss è
> morto»* e *«per lui il boss può essere ancora vivo»*. **La seconda frase è falsa**, e non per un'opinione: i log
> mostrano il joiner che **riceve il flag** `D17Z01_BOSSDEAD` **dal server** e **nasconde il boss**. ⇒ **il fatto di
> codice non si nega, si DECLASSA**: il prefisso `bdead|` manca davvero nell'annuncio, **ma il caso che doveva
> coprire è coperto da un'altra via — il flag, che passa dal server** — e **resta scoperto solo per i 4** della voce 9.
> ⚠️ La **9** non cambia natura: è **da testare**. **Questa**, che era dichiarata «verificata e da fixare per tutti i
> boss», è ora **un fatto di codice verificato con una portata misurata**: *dodici coperti, quattro no*.

**✅ LA CONSEGUENZA SMENTITA — dodici boss su sedici, con prova incrociata.** *(fonte: `scratch\DIAGNOSI-SERVER-BOSS-MORTE.md`, che cita le righe dei log reali)*
- **il flag viene rimandato dal SERVER a chi si connette dopo**: `sendPlayerTeam` (`Server.cs:385-413`) alla
  connessione cicla su **tutti e 14** i tipi di progresso — il ciclo è a `:396-412` — e manda **un pacchetto per ogni
  chiave archiviata**;
- ⭐ **la prova che viene dal server e non dal peer**: G1 riceve **26 progressi** in **ordine di tipo crescente** —
  `QI31` (5) → celle mappa (8) → **`D17Z01_BOSSDEAD`** (9) → oggetti personali (10) → `TELEPORT_D02` (11) →
  `Tears` (12) — cioè **esattamente l'ordine del `for` del server**;
- ✅ **e il client lo applica**: `G1:666` il flag è ricevuto → `G1:674` `Flag D17Z01_BOSSDEAD has been set to TRUE`;
- ✅ **e il boss non c'è**: `G1:864` `[BossCorpse] … nascosto (ingresso in scena) … flag=D17Z01_BOSSDEAD … veto SCAVALCATO: il flag e' acceso, il boss sembrava vivo (vita=400)` — cioè `HideDefeatedBosses` (`:732`, chiamata a `:2732`) col flag letto da `FirstBossDeadFlagOn` (`:966`);
- ⭐ **i 12 con un flag in `FlagStates`**: `Data\FlagStates.cs:37-48` — dal Warden a Isidora. **Per loro il joiner lo
  sa, e il cadavere è già spento.** ⇒ *«per lui il boss può essere ancora vivo»* **va cancellata**: non è «poco
  probabile», è **smentita**;
- ⚠️ **e il merito onesto**: i log **client** non hanno orologio e quello del **server** non dice **quale IP** ha
  ricevuto quale flag ⇒ l'abbinamento è **dedotto** da `sendPlayerTeam` **più** la ricezione a `G1:666`, non da una
  riga del server. **Ma la deduzione ha una firma che non si imita**: l'**ordine del `for`**.

**⚠️ IL FATTO DI CODICE CHE RESTA — declassato, non negato.** `bdead|<id>` — il prefisso che dice *«questa è la morte
di un BOSS: applicala con `Kill()`»* — **non viene mai ritrasmesso** a chi si connette **dopo** la kill:
- `AnnounceDeathStateToPeers` (`:4871`) ripete le morti al joiner mandando **solo id nudi** (`:4913-4914`);
- la memoria `_announcedDeaths` contiene **l'id nudo** (`:5864`);
- il prefisso compare **solo** dove nasce e dove parte **dal vivo** — la costante `:335`, lo scorporo `:349`, l'invio
  `:5848` — **nessuno dei tre è nel percorso del joiner**;
- ⇒ al joiner arriva la strada **generica**: `_pendingDeaths.Add` + `MarkSpawnPointConsumed` (`:6889`) →
  `DestroyDeadEnemy` (`:7089`) → `KillEnemyLocally` (`:6348`), che su un boss finisce nella **`Damage(Hit)` nativa**;
  **non** `ApplyBossDeath` (`:440`) → `Kill()` (`:531`) → **morte recitata** della whitelist;
- ⭐ **ma è un fatto senza effetto sui 12**: il boss **non ha bisogno di essere ucciso** dal joiner, perché **non
  compare affatto** — il flag lo spegne all'ingresso in scena. **Due vie disgiunte, e ne basta una.**

**🔴 IL RESIDUO VERO SONO I 4 DELLA VOCE 9, e per loro il buco è reale.** `Amanecidas` · `Perpetua` · `PontiffHusk` ·
`Viridiana` **non hanno un flag di morte ricavabile**: `FirstBossDeadFlagOn` cerca `<scena>_BOSSDEAD` e per loro
**non lo trova** *(per `HUSK` e `VIRIDIANA` **zero** righe in `FlagStates`; per `AMANECIDA` e `PERPETUA` il flag
esiste **con nome-DLC** — `SANTOS_AMANECIDA_*_DEFEATED`, `BROTHERS_PERPETUA_DEFEATED` — e `FirstBossDeadFlagOn` **non
lo legge**, `:963-964`)*. ⇒ **per un joiner, dei quattro non c'è né flag né `bdead|`**: **lì** non ha **nessuna delle
due vie**, ed **è lì** che il difetto è vero. **Che siano coperti o no è la misura della voce 9.**

**📌 E vedi la voce 11: il team che si svuota è UN'ALTRA VIA per cui un joiner non vede il boss morto** — ⚠️ **da
testare, non verificata**, e **non passa dai quattro**: passa da **tutti** i boss, perché lì non manca il flag, manca
**l'archivio del team**.

**⚠️ DUE CORREZIONI AL «CHI FA COSA», perché la via del flag le rende facili da sbagliare.**
1. **Il server è un ARCHIVIO CIECO, non un controllore.** Riceve (`receivePlayerProgress` `Server.cs:583-596` →
   `AddTeamProgress` `TeamInfo.cs:22-43`, che tiene il **massimo**), **archivia** e **ripete** (`:396-412`). **Non
   guarda il merito di niente**: non sa che `D17Z01_BOSSDEAD` è un boss, non sa che un boss è morto, non decide.
   **Il controllo è nel CLIENT**: `FirstBossDeadFlagOn` (`EnemySyncManager.cs:966`) — l'**unico** lettore di
   `_BOSSDEAD` — chiamato da `HideDefeatedBosses` (`:732`). ⇒ *«è il server che fa il controllo»* sbaglia **di un
   salto**: il server è **il corriere con la memoria**.
2. **La direzione: il codice NON implementa «solo host→peer».** Flag e `bdead|` sono **simmetrici**: nessun gate di
   ruolo, né nel patch di `SetFlag` *(`FlagHelper.cs:28-43`)* né nell'invio della morte *(`EnemySyncManager.cs:5846-5848`)* —
   li manda **chi vede** la cosa. **Host e peer contano solo per danno e ownership**, nel server (`Server.cs:821-853`:
   ownership **solo host** `:821-829`; danno host→tutti, peer→**solo host** `:830-853`). ⇒ che nei log si veda **solo
   host→peer** dipende da un fatto del **save**, non dal codice: **solo G2 aveva la kill**.

**⚠️ E UN FATTO NUOVO, da leggere prima di ogni test: la sessione dei log NON conteneva una kill in co-op.**
`[EnemyKill]` = **0** e `[BossDeath TX]` = **0** in **entrambi** i client; `bdead` = **0** in **tutti e tre** i log
*(G1, G2, server)*. G2 **non** ha sconfitto il Warden in co-op: **ce l'aveva già nel save**, e il flag è stato
**spinto dal suo save** alla connessione — `ServerOutput.log:8` `Creating new game data for team 1`, `:27`
`Received new flag from G2: D17Z01_BOSSDEAD` *(patch di `SetFlag`: `FlagHelper.cs:28-43`; l'invio dei flag accesi
all'ingresso: `FlagHelper.cs:22` + `:49-69`)*. ⇒ **la catena «kill in co-op → joiner» NON è stata misurata**: è provata
la catena **«archivio del server → joiner»**, non **«kill → archivio → joiner»**. **Non è un dettaglio**: è la ragione
per cui il test qui sotto viene **prima** di qualunque fix.

**📋 LA LISTA DEL TESTING — due prove, e la prima è quella che manca.**

| # | prova | cosa si guarda | cosa decide |
|---|---|---|---|
| **1** ⭐ | **kill in co-op, poi un joiner** *(un boss con flag, es. il Warden)* | `[EnemyKill]` e `[BossDeath TX]` sulla macchina che lo uccide; sul joiner il flag `D…_BOSSDEAD` ricevuto, `[BossCorpse] … veto SCAVALCATO`, e `bdead` | che il flag copra **una kill di questa sessione**, e **quale ramo** copre un joiner **vivo** *(guardia `:456`, qui sotto)* |
| **2** | **i 4 della voce 9** *(servono i DLC)* | `[EnemyKill] … MORTO … vita=0`, come in voce 9 | se il canale della vita li vede: **è la misura che chiude la 9**; se non li vede, il fix è **lì** |

**⭐ IL FIX, ancora valido ma per i soli 4 — e con l'avviso.** **Mandare anche `bdead|<id>` nell'annuncio** — cioè
aggiungere il prefisso agli id che `AnnounceDeathStateToPeers` ripete. **È nel canale che esiste già** *(il ricevente
sa già cosa fare con quel prefisso: `:6881`)*, **non** un flag nuovo e **non** un pacchetto nuovo. ⚠️ **E il pezzo che
il fix deve tenere presente, altrimenti resta mezza riga**: `ApplyBossDeath` interviene **solo se il giocatore locale è
a terra** *(`:456`, `if (!localDead) { … motivo=giocatore-locale-vivo; return; }`)* ⇒ rimandare `bdead|<id>` a un
joiner **vivo** lo farebbe **uscire subito dal ramo**, senza applicare niente. ⚠️ **E per i 12 il fix non aggiunge
niente che serva**: lì il boss **non c'è**. ⇒ **il fix si valuta dopo il test 1, e si scrive per i quattro.**

**📌 I RIFERIMENTI, misurati a HEAD `7c5895d`.** `sendPlayerTeam` **`:385-413`** *(ciclo dei 14 tipi: `:396-412`)* ·
`receiveEnemyDeathGlobal` **`:807-819`** *(rilancia e **non archivia**: la morte **non** ha memoria sul server)* ·
danno/ownership **`:821-853`** · `receivePlayerProgress` **`:583-596`** · `AddTeamProgress` **`TeamInfo.cs:22-43`** · i
12 flag **`FlagStates.cs:37-48`** · `FlagHelper` **`:22`, `:28-43`, `:49-69`** · `FirstBossDeadFlagOn` **`:966`** ·
`HideDefeatedBosses` **`:732`** *(chiamata `:2732`)* · `AnnounceDeathStateToPeers` **`:4871`** · id nudi **`:4913-4914`** ·
memoria **`:5864`** · prefisso **`:335` / `:349` / `:5848`** · `ApplyBossDeath` **`:440`** *(guardie `:448`, `:456`,
`:465`, `:472`; `Kill()` `:531`)* · strada generica **`:6881-6889`**, `:7089`, `:6348`.

**📌 NOTA SULLE RIGHE — i riferimenti dei referti sono in ritardo.** La **whitelist** è citata `:378-386` e a HEAD è
**`:398-406`** *(misurato: **20 righe di scarto**)*; `FirstBossDeadFlagOn` è citato `:836-856` **ma è a `:966`**. ⇒
**valgono i numeri misurati a HEAD.**

**Stato.** **FATTO DI CODICE VERIFICATO — CONSEGUENZA SMENTITA.** ✅ Il prefisso manca *(verificato)*; ⛔ **ma «per lui
il boss può essere ancora vivo» è falso per 12 boss su 16** *(log reali: flag dal server, boss nascosto)*. ⇒ **la voce
non è più «da fixare per tutti i boss»**: è **la coda della 9** — *i 12 sono coperti dal flag, i 4 no* — e **il
residuo è quello della 9**, che **resta da testare**. ⛔ **Nessun fix prima del test 1** *(kill in co-op → joiner)* e
**prima della misura sui 4**. 📌 **La 9 e la 10 non si confondono**: la 9 chiede **una misura** sul rilevamento; la 10
ha **una portata** — e **nessuna delle due** si chiude con un flag nuovo.

---

## 11. ⚠️ IL **TEAM CHE SI SVUOTA** — l'ultimo che esce porta via l'archivio del team, e il flag **non torna da solo** *(⚠️ **MECCANISMO VERIFICATO NEL CODICE** · ⛔ **CONSEGUENZA DA TESTARE** — non un difetto accertato)*

> **Va detto per primo, come nelle voci 9 e 10: qui non c'è un difetto accertato.** Il **meccanismo** si legge e si
> chiude — e le righe qui sotto sono tutte misurate a HEAD `19773ed`: il server **non ha nessuna persistenza**, e
> quando esce **l'ultimo** giocatore di un team `removeUnusedGameData` **cancella l'archivio di quel team**. ⛔ **La
> conseguenza invece non si legge**: che chi entra dopo veda il boss **vivo** dipende da un fatto **fuori dal
> server** — *se il save di chi entra ha già quel flag* — e **il codice, da solo, non lo dice**. ⇒ **è una cosa DA
> TESTARE**, non una cosa da fixare: il test costa **una run**, il fix è una **decisione di design** che nessuno ha
> preso.

**✅ COSA È VERIFICATO — il meccanismo, riga per riga.**
- **`removeUnusedGameData` cancella l'ARCHIVIO DEL TEAM, non «dati di sessione»**: `TeamInfo` **è** il progresso — 14
  dizionari (`TeamInfo.cs:7`), gli stessi che riempie `AddTeamProgress` (`:22-43`) e che legge il dump (`:46-54`). E
  `teamGameDatas.Remove(i)` (`Core.cs:88`, con la riga `Removing game data for team N` a `:87`) toglie **quello**:
  i flag `_BOSSDEAD` compresi.
- **e lo toglie quando il team è DAVVERO vuoto**: la funzione (`Core.cs:68-91`) scorre i team 1-10 e cancella solo se
  **nessun giocatore connesso** ha quel `team` (`:76-84`). Non è «l'ultimo che si disconnette» in astratto: è
  «**nessuno più dentro**». ✅ **Su questo la domanda secca ha una risposta netta: è vero, si cancella.**
- **le due chiamate**: `clientDisconnected` toglie il giocatore (`Server.cs:142`) e **subito dopo** ripulisce
  (`:143`); e `receivePlayerTeam` ripulisce **anche al cambio di team** (`:418` scrive il team, `:421` pulisce).
- **⛔ e non c'è NESSUNA persistenza lato server** — è la prova che serviva: `teamGameDatas` è un `private static
  readonly Dictionary<byte, TeamInfo>` **in RAM** (`Core.cs:11`), e in tutto il progetto server **non esiste una riga
  che legga da disco**: nessun `ReadAllText`, nessun `StreamReader`, nessun deserializzatore. Gli unici `File.*` sono
  in `ServerLog.cs` (`:54`, `:83`, `:97`) e scrivono **solo il log**, che nessuno rilegge. ⇒ **riavviare il server
  azzera tutto** e **l'ultimo che esce azzera il team**: sono lo stesso fatto, e non c'è un salvataggio che li salvi.

**❓ E LA FRASE DELL'UTENTE È VERA — ma è l'altra metà della catena, e va scritta per intero.**
*«Lo stato a chi si connette viene salvato»* è **vera**, e non smentisce il punto sopra: **lo stato lo conservano i
CLIENT, non il server.** Ogni client, **alla connessione** (`Multiplayer.cs:177`) e **al cambio scena/team** (`:452`),
**spinge al server tutti i flag che ha già accesi nel proprio save** — `SendAllProgress` → `GetCurrentPersistentState("intro")`
→ il `Prefix` che li manda a **valore 0** (`FlagHelper.cs:22-25` e `:48-69`) — e il server li **riarchivia**
(`Server.cs:591`, la riga `Received new flag from …` a `:617-621`).
- ⭐ **è esattamente ciò che dicono i log della voce 10**: il `D17Z01_BOSSDEAD` non è arrivato da una kill, è stato
  **spinto dal save di G2** alla connessione (`ServerOutput.log:8` e `:27`);
- ⭐ **e il flag ricevuto entra davvero nel gioco di chi lo riceve**: `ApplyProgress` chiama `Core.Events.SetFlag(id,
  value == 0, false)` (`FlagHelper.cs:13`), e il terzo parametro di `SetFlag` è **`forcePreserve`** *(new game+)*,
  **non** «non salvare» ⇒ il flag finisce nei `flags` del client, che il gioco **persiste nel save**
  (`GetCurrentPersistentState` serializza **tutto** il dizionario in `FlagPersistenceData.flags`);
- ⇒ 🔴 **e questo RESTRINGE il buco, di molto**: chi era connesso **quando la kill è avvenuta** ha ricevuto il flag
  per relay (`Server.cs:662`) e **ce l'ha nel proprio save**. Per lui il team vuoto **non cambia niente**: il flag lo
  ha in casa. ⚠️ **Il buco resta solo per chi NON ce l'ha nel proprio save.**

**⭐ LA SEQUENZA CHE LO PROVOCA — con il vincolo che la rende rara.**
① **P1 uccide il boss** ⇒ il gioco accende il flag, la patch lo manda (`FlagHelper.cs:28-43`), il server lo archivia
   (`Server.cs:591`).
② **P1 esce** — e se era **l'ultimo** giocatore del team, `removeUnusedGameData` **cancella il team**
   (`Server.cs:142-143` ⇒ `Removing game data for team N`).
③ **P2 entra** ⇒ `getTeamData` **ricrea un `TeamInfo` vuoto** (`Core.cs:57-66`, `Creating new game data for team N`)
   e `sendPlayerTeam` (`Server.cs:396-412`) non ha **nessuna chiave** da mandare.
④ ⭐ **e allora il boss resta vivo** — ma **solo se** P2 **non aveva già il flag nel proprio save**, cioè solo se
   **non era connesso** quando P1 l'ha ucciso. Se c'era, i passi ③ e ④ non fanno danno: il flag è suo.
- ⚠️ **perché «nessuno gliel'ha detto»**: col team vuoto non c'è nemmeno il pacchetto del dump *(e anche quando c'è,
  il dump parla con `"*"` e il client **non notifica** per `"*"`, `NetworkManager.cs:424-425`)*; e il client, senza
  flag, **non nasconde niente**: `HideDefeatedBosses` (`EnemySyncManager.cs:732`, chiamata **all'ingresso in scena** a
  `:2732`) con `statusDead = false` e `flag = null` **esce senza toccare il boss** (`:793`).
- ⚠️ **e non è il `bdead|`**: quello è coperto **dal flag**, che passa dal server (voce 10). Qui il problema non è un
  prefisso mancante: è **l'archivio che non c'è più**.
- ⚠️ **e non riguarda i 4 della voce 9**: la cancellazione porta via **tutti** i progressi del team — i 12 flag
  `_BOSSDEAD` (`FlagStates.cs:37-48`), gli oggetti, le celle di mappa, i punti di teletrasporto — mentre per i
  **quattro** di voce 9 il flag **non esiste in partenza**. Quella resta **un'altra domanda**, e non cambia qui.

**⚠️ E UN SECONDO FATTO, NELLA STESSA VOCE — l'inversione e il massimo.**
- i flag viaggiano **invertiti**: acceso = **0**, spento = **1** (`FlagHelper.cs:39`; e `ApplyProgress` li rilegge al
  contrario, `:13`);
- e il server **tiene il massimo** (`TeamInfo.cs:33-39`): se il valore nuovo è **maggiore** lo sovrascrive e lo
  rilancia (`:37-38`), altrimenti lo **scarta** come *inferiore* (`Server.cs:592-594`, la riga `Received duplicated or
  inferior progress`); e **una chiave, una volta entrata, non si cancella mai**;
- ⇒ ⭐ **per questi valori la regola del massimo sceglie `1`, cioè lo SPENTO** — e il `0` che arrivasse dopo è
  «inferiore» e **non torna**: *uno spento è **definitivo**, per la vita del server*. **Ciò che il server non sa fare
  è TORNARE INDIETRO**, non «sapere spento».
- ⚠️ **CORREZIONE ALLA DIAGNOSI, dichiarata invece che taciuta**: la sua riga 29 dice *«un flag spento (valore 1) non
  viene mai archiviato: il server non sa "spento"»* e *«un flag acceso resta acceso»*. **A HEAD non regge**: `1 > 0`,
  quindi lo spento **entra** e **vince**. Il **resto** della frase regge tutto *(il massimo, il non-ritorno)* ed è
  quello che resta scritto qui sopra.
- 📌 **e va dichiarato per quello che è**: la **direzione dello spareggio** — *chi vince fra acceso e spento* — **non
  l'ha decisa nessuno**. È **una scelta implicita**, non un bug. ⛔ **Nessuno ha mai visto in un log un flag spento
  partire** per uno dei 12 `_BOSSDEAD`: se il gioco non li spegne mai, il massimo in pratica non fa danno; se li
  spegne, **cancella la kill per tutti** — ed è **l'altro** danno, non questo.

**📋 IL TEST — in casa, senza DLC.** ⚠️ **con un vincolo che decide l'esito**: il test vale **solo se chi rientra non
ha quel boss già ucciso nel proprio save**. Un test in cui P2 era connesso alla kill **non prova niente**: il flag è
già suo.

| # | passo | cosa si guarda |
|---|---|---|
| **1** | **P1 da solo** in co-op uccide **un** boss *(il Warden va bene: `D17Z01_BOSSDEAD`)*, con **P2 non connesso** | su P1 `Flag D17Z01_BOSSDEAD has been set to TRUE`; lato server `Received new flag from P1` |
| **2** | **P1 esce** *(era l'ultimo)* | lato server `Client disconnected at …` **e poi** `Removing game data for team 1` ← ⭐ **la firma da vedere** |
| **3** | **P2 entra** col suo save in cui quel boss è **vivo** | lato server `Creating new game data for team 1` + `Sending all server data to …` **senza** nessun `D17Z01_BOSSDEAD` |
| **4** | P2 va nella stanza del boss | ⭐ **il boss è morto o vivo?** |

| esito | cosa vuol dire | cosa fare |
|---|---|---|
| **il boss è MORTO** | il flag è arrivato da un'altra via *(il save di P2, o una kill che non abbiamo visto)* ⇒ **il buco, così, non c'è** | la voce si chiude **con la misura**, senza toccare il codice |
| **il boss è VIVO** | ⚠️ **il buco è confermato**: la via del flag **non** copre il team vuoto | il fix diventa **una decisione di design** *(sotto)*, **non** una riga da aggiungere |

**🔎 Se ricapita, le firme nel log** — la sequenza si riconosce **dal lato server**, in quattro righe:
```
Client disconnected at <ip>              <- esce l'ultimo del team
Removing game data for team 1            <- ⭐ L'ARCHIVIO E' ANDATO
Creating new game data for team 1        <- al rientro il team nasce VUOTO
Sending all server data to <ip>          <- ...e non ha nessun flag da mandare
```
e **sul client che entra**, una delle due:
```
[BossCorpse] en|…|…|… NON nascosto (ingresso in scena): il boss sembra VIVO (statusDead=False flag=nessuno vita=…)  <- ⭐ il buco
   oppure nessuna riga [BossCorpse] per quel boss, se il boss non "sembra vivo" (:793 esce in silenzio)
```

**📌 E IL FIX, IPOTIZZABILE MA NON DECISO.** Due forme, e **nessuna delle due è decisa**:
- **il team non si svuota subito** — una **grazia** *(il team sopravvive N minuti, o fino al riavvio del server)*: un
  server «che ricorda» è comodo, ma **di chi sono quei dati se il team è vuoto?**
- **oppure i flag sopravvivono al team vuoto** — un archivio dei **soli flag**, separato dai progressi.
⚠️ **E il prezzo di entrambe, che va detto prima di sceglierle**: il server diventa **stateful fra sessioni** — e
serve una **regola di appartenenza** dei dati *(per team? per coppia di giocatori? per save?)* che oggi **non esiste**.

**Stato.** **MECCANISMO VERIFICATO NEL CODICE · CONSEGUENZA DA TESTARE.** ✅ `removeUnusedGameData` cancella davvero
l'archivio del team quando non c'è più nessuno, e il server **non ha nessuna persistenza** *(verificato)*; ✅ **ma lo
stato lo conservano i save dei client**, che lo rispingono a ogni connessione *(verificato)* — quindi il caso **comune**
è coperto, e resta scoperto **solo il joiner che quel flag non l'ha mai avuto**. ⛔ **Non è un difetto accertato e non è
in lavorazione**: **una run** lo decide. 📌 **I due lati non vanno confusi**: *«il server conserva lo stato»* è
**falso** *(non ha nemmeno un file)*; *«lo stato si ricostruisce da chi si connette»* è **vero** — e per la maggior
parte dei casi basta. **Nessun fix prima del test**, e **il codice non è stato toccato**: qui c'è **cosa dichiarare**,
là *(nel `docs\TODO.md`)* ci sarebbe **cosa fare** — ma **quel file non è stato toccato**.

---

## Come si riporta un difetto *(per chi gioca e per i tester)*

**Quello che serve**, in ordine di utilità:

1. **Cosa hai visto**, in una frase, **e dove** *(quale stanza, o che aspetto aveva)*.
2. **Cosa stavate facendo**: chi era **host**, chi era **nella stanza**, e **cosa è successo subito
   prima** *(un falò, una morte, un'esecuzione, un cambio di stanza)*.
3. **L'ordine degli eventi**, anche approssimativo. *"Prima X, poi Y"* vale più di un minuto di
   descrizione.
4. ⭐ **I due log**, uno per macchina:
   `%USERPROFILE%\AppData\LocalLow\TheGameKitchen\Blasphemous\output_log.txt`
   **E il log del server**, `ServerOutput.log`, nella cartella del server.
5. ⭐ **E la cosa più utile di tutte: la ricetta.** *"Succede quando faccio A, B e C, nell'ordine."*
   **Un difetto riproducibile a comando è un difetto che si chiude.**

**⚠️ E una cosa da NON fare**: *non descrivere il sintomo con la causa che immaginate.*
*"Il nemico si teletrasporta perché la sincronizzazione è rotta"* è **meno** utile di *"il nemico era a
sinistra e dopo un secondo era tornato al punto di partenza"*.
**Il sintomo si osserva, la causa si ipotizza — e le ipotesi sbagliate costano sessioni.**
