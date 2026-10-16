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
