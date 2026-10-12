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
