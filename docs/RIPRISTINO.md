# PUNTI DI RIPRISTINO — Blasphemous COOP

> **A cosa serve questo file.** I tag git sono il punto di ripristino vero, ma vivono nel repository e
> non dicono *cosa* contengono né *quali binari* corrispondono loro. Qui c'è tutto quello che serve per
> tornare indietro in un minuto, senza ricostruire la storia a memoria.

---

## Come si torna indietro

```powershell
git checkout checkpoint-stabile-morti-boss   # il piu' recente e il piu' solido
# oppure, per tornare piu' indietro:
# git checkout checkpoint-stabile-falo        # (alias ASCII) = checkpoint-stabile-falò
```

Poi **ricostruire e rideployare**, perché il checkout da solo non tocca i binari installati:

```powershell
cd 'Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main'
dotnet build .\Blasphemous.Multiplayer.sln
cd ..\..
.\tools\deploy-local.ps1
.\tools\verify-deploy.ps1
```

⚠️ **`verify-deploy.ps1` deve uscire con codice 0.** Se dice *"questa macchina non esegue il binario
atteso"*, il deploy non è quello che credi — e un test lì non misurerebbe le funzionalità nuove.

⚠️ **E per tornare al ramo di lavoro**: `git checkout fix-f1a`.

---

## I punti di ripristino

### `checkpoint-stabile-morti-boss` — 12 ottobre 2026

**Commit** `a4a7528e` · **Le morti e i boss chiusi; il blocco totale del gioco risolto.** È il punto
**più recente e il più solido**: 32 commit e 10 file di differenza da `checkpoint-stabile-falo`.

| | sha256 |
|---|---|
| **mod** *(BlasClient.dll)* | `d824f4aa4d786cdfff6af7ce84c14b002d6738f2c0f88d0da587ec3f309034b3` |
| **server** *(blas1mpserver.dll)* | `d70ed09777936b6cc20ceb9e5fcab2f4fd45198afff9bd3888777c92810a8cf3` |

**Cosa contiene**
- **La morte di un giocatore non respawna più il mondo per gli altri.** Prima respawnava i nemici
  **solo per chi moriva**, e le due macchine **divergevano** — la convergenza avveniva **per caso**,
  con un annuncio di falò. Ora: la soppressione del `Reset` **e** la memoria delle morti leggono **lo
  stesso flag**, quindi gioco e mod restano coerenti **per costruzione**. **Verificato in gioco**:
  dopo entrambe le morti le due macchine danno lo **stesso** numero, e `3/7/3` = lista conservata
  contro `5/7/5` = lista azzerata è il controllo interno che rende la lettura non ambigua.
- **Il confine "mondo sì / benefici no" è misurato.** `RestoreStored` è una chiamata **diversa** dentro
  `Respawn`: sopprimere il respawn **non tocca** vita e fiasche. *Le fiale erano a 0 su entrambi: solo
  il seduto è salito.*
- **La morte è comunicata al DECESSO**, non alla pressione del tasto (`pdead|` + `palive|` su
  `EnemyDeathGlobal`, **nessun bump di protocollo**), e ogni macchina **sa chi è morto**
  *(`PlayerStatus.IsDead`, `AreAllPlayersDead`)*. **`palive|` è accoppiato** al `pdead|`: senza, un
  permesso perso non si sarebbe mai sanato.
- **Un cadavere non tiene più gli enemy, e non ne prende.** *Era la causa del "boss che gira intorno a
  un morto"*: `ShouldOwnByProximity` non guardava la vita, e l'unico ramo che cedeva esigeva una
  distanza di 2,5 unità — quindi **morire addosso al boss** lasciava il boss al cadavere, e
  `<Boss>::Damage` **buttava via i colpi del vivo** *(18 classi di boss su 21)*. **Verificato nei
  log**: il proprietario è morto a `dist_io=0.0`, e **solo** il fix poteva rilasciarlo.
- **🔴 Il blocco totale del gioco è chiuso.** `DestroyDeadEnemy` faceva `Object.Destroy` sul nemico
  mentre il gioco teneva ancora `Execution.ExecutedEntity`: l'eccezione nasceva **dentro l'icall
  nativo** `GetComponentFastPath`, lo stato `Stunt` non riusciva più a uscire *(514 eccezioni, 78% del
  log)* e la coroutine di esecuzione **moriva** lasciando il blocker `EXECUTION` acceso per sempre —
  **penitente invisibile, tutti i tasti morti**. Ora **`SetActive(false)`**: il nemico sparisce come
  deve, i riferimenti del gioco restano validi.
- **Gli id degli spawn point non divergono più.** `BuildSpawnPointId` usava il nome **grezzo** di
  Unity (`SPAWN (11)` / `SPAWN (15)`): **2 id su 5 diversi** fra le macchine, e quelle chiavi **non
  ricevevano mai** una rivendicazione. Ora `StripInstanceSuffix` gestisce **entrambi** i suffissi
  *(l'hash del gioco e il contatore di Unity)*.
- **Il log è leggibile**: la cadenza di scrittura dei nemici è **separata** da quella del rescan
  *(10 s invece di 0,5 s)*, perché alzare il rescan avrebbe **peggiorato la correttezza**. E le **35
  voci chiuse del TODO** sono compresse, col testo integrale in fondo.
- **🛠 Due strumenti nuovi**: `tools\verify-patch-targets.ps1` controlla **tutti i 235 bersagli**
  delle patch Harmony, ed è **eseguito dal deploy** — *perché un nome sbagliato in `[HarmonyPatch]`
  compila e poi **aborta `PatchAll`**, uccidendo la mod a runtime. È già successo.*

**Cosa NON contiene / limiti noti**
- **I bug difficili da riprodurre sono dichiarati in `docs\BUG-NOTI.md`.** Fra questi: il **desync
  quando l'host si siede al falò col non-host presente**, l'**AI in pausa dopo il parry**, e le
  **stanze del tutorial**, che vanno **evitate per i test**.
- **`IMPRONTA DIVERSA` non è a zero**: la lista **viva** dei nemici diverge ancora, in modo asimmetrico
  con le morti *(voce 46)*. **Non è più il suffisso fra parentesi** *(`SPAWN (` = 0 occorrenze, e i
  conteggi degli spawn point coincidono)*.
- **La voce 51 è aperta**: al passaggio di proprietà si comunica **chi** simula, **non dove si trova**
  il nemico — *e il nuovo proprietario impone la sua copia.*
- **La voce 11 è aperta e va RISCRITTA prima di agganciare qualcosa**: il testo indicava
  `EnablePhysics` fra i tipi coinvolti, e **nessuno di quei 16 la chiama** — gli 8 call-site reali sono
  altri. *Un fix costruito sul nome sbagliato è il tipo di errore già costato una sessione.*
- **Le voci 7, 9, 10, 15, 18, 35, 36, 40, 43, 45, 52, 53** sono aperte: ottimizzazioni, procedure e
  difetti minori. Le vedi tutte nella lista iniziale del TODO.

---

### `checkpoint-stabile-falo` *(alias ASCII)* = `checkpoint-stabile-falò` — 10 ottobre 2026

**Commit** `cc0522ff` · **Il falò completo e verificato in gioco.**

| | sha256 |
|---|---|
| **mod** *(BlasClient.dll)* | `551dfddd14fb72c36b9790e99343f582c3acbbeb3c198738a5d0bf06fafd6281` |
| **server** *(blas1mpserver.dll)* | `ce214638cfae051bfbfeb2512f8823ac47bdea7877d8e75caa3eca28ab55550b` |

**Cosa contiene**
- **Il falò si propaga.** Passo 1 (l'annuncio `falo|` su `EnemyDeathGlobal`), 2a (il ricevente che lo
  riconosce), 2b (la replica). **Verificato in gioco in ENTRAMBI i casi**: stessa stanza e stanze
  diverse. **E i benefici NON si propagano**: chi non si siede non ricarica fiasche né vita.
- **I distruttibili chiusi con la prova.** F4b.1 → F4b.4: la guardia di idempotenza è dimostrata dalla
  sua stessa riga di misura, non da un'impressione.
- **La voce 32 chiusa**, e la lezione che ne viene: *"verificato nel codice" non è "verificato nel
  comportamento"*. Il gate host-only non era un difetto, era il disegno.
- **Voci 41 e 42 risolte**: il `try/finally` che impedisce a **una sola eccezione** di uccidere la
  sincronizzazione per sempre, e la dedup di `AddInteractedObject`.
- **Il server scrive `ServerOutput.log` su disco** *(prima il suo resoconto moriva con la finestra)*.
- **La riga `[Path]` all'avvio**, che rende visibile in quale cartella il gioco sta salvando — e che
  avrebbe risparmiato l'intera indagine sui salvataggi.
- **`verify-deploy.ps1` allineato** *(cercava un marker rinominato e bloccava ogni test)*.

**Cosa NON contiene / limiti noti**
- **`pending` dei distruttibili non viene ripulito dal falò**: è il progetto della voce 33, ma nel caso
  "rotto in questa vita prima del falò" diventa un'eco. **Da affrontare solo se il sintomo si ripresenta.**
- **`ReapplyCurrentScene` gira due volte per caricamento di livello** *(voce 40)*: innocuo con la
  guardia, tenuto di proposito.
- **`if (!localIsHost)` in `DecideOwnership` è codice morto** *(voce 43)*: pulizia opzionale.
- **La penitenza PE02 non è mai stata esercitata**: la trappola è nota *(`RespawnDeadEnemies` chiamata a
  ogni cambio stanza mentre quella penitenza è attiva)* e il fix la evita per costruzione, ma **non è
  stata verificata sul campo.**

---

### `checkpoint-stabile` — 9 ottobre 2026

**Commit** `a2824d69` · **Il punto prima del falò e dei distruttibili.**

15 file di differenza e **30 commit** di distanza da `checkpoint-stabile-falo`. **È il punto a cui
tornare se il falò o i distruttibili si rivelassero problematici in una situazione non coperta dai
test**, accettando di perdere: i distruttibili, il falò propagato, il log del server, la riga `[Path]`,
il `try/finally` e la dedup.

---

## Come si crea un nuovo punto di ripristino

1. **Working tree pulito** — `git status --short` deve essere vuoto.
2. **Verifier verde** — `.\tools\verify-deploy.ps1` deve uscire 0.
3. **Costruire il tag** con un messaggio che dica *cosa contiene* e *quali sono gli hash dei binari*,
   **in caratteri semplici** *(l'accento può dare fastidio a script e shell non UTF-8)*:
   ```powershell
   git tag -a checkpoint-<nome> -m "cosa contiene · mod <hash> · server <hash>"
   ```
4. **Annotarlo qui**, con la stessa struttura delle voci sopra.

> **Perché in caratteri semplici, e perché due tag.** Il primo punto di ripristino del falò è stato
> creato con l'accento (`checkpoint-stabile-falò`). Funziona, ma un accento in un nome di tag è il tipo
> di dettaglio che morde al momento sbagliato: uno script che non legge UTF-8, un copia-incolla fra
> sistemi diversi, una shell con una codepage vecchia. **Il testo accentato resta**, perché non si
> cancella la storia; **la versione ASCII è quella da usare.**
