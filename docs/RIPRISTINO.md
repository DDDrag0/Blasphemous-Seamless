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
# e poi LA VERIFICA CHE CONTA: il CONTENUTO della DLL deployata, con i marker della
# versione che stai ripristinando (l'array passa col call operator &, non con -File)
& '.\tools\marker-check.ps1' -Dll 'Modding\plugins\BlasClient.dll' -Marker '<i marker della versione>'
```

> ⚠️ **Dal 2026-10-19 `tools\verify-deploy.ps1` NON ESISTE PIÙ** *(cancellato, vedi `docs\TODO.md`
> `[2026-10-19 00:15]`)*: stampava *"Puoi testare"* **anche su una DLL sbagliata** *(marker fermi
> all'8-12/10, hash vincolante solo con `-ExpectedHash`, marker saltati in silenzio senza
> `scratch\cecil`)* ⇒ **è stato usato per giorni proprio perché mentiva**. Il suo sostituto è
> **`tools\marker-check.ps1`**, che legge i byte della DLL e **sa dire NO**.

> ⚠️ **Dal 2026-10-16 la build NON tocca più la cartella di gioco.** Il `.csproj` del **client** la
> copiava a ogni `dotnet build` (target `Development`, `DevFolder` con percorso **assoluto**): bastava
> una build in un **worktree di prova** per annullare in silenzio il deploy di HEAD — *ed è successo,
> e per giorni si è creduto di testare una versione mentre il gioco ne eseguiva un'altra.* Ora quella
> copia è **spenta** (`DeployToGame=false`) e **`deploy-local.ps1` è l'unico modo di mettere qualcosa
> in gioco**: copia la mod, la sua `Common.dll` *(che il gioco carica da `Modding\data`)* e il server.
> Se un giorno servisse la vecchia copia fatta dalla build:
> `dotnet build .\Blasphemous.Multiplayer.sln -p:DeployToGame=true`.

⚠️ **`marker-check.ps1` deve uscire con codice 0** e stampare *"ESITO: TUTTI i marker richiesti sono
PRESENTI"*. Se dice **`ASSENTE`**, il deploy non è quello che credi — e un test lì non misurerebbe le
funzionalità nuove. *(È l'unica verifica del contenuto che abbiamo: `verify-deploy.ps1` è stato
cancellato il 2026-10-19 perché rispondeva 0 anche su una DLL vecchia.)*

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
  > ⚠️ **CORREZIONE DEL 2026-10-12 (audit della TODO) — il numero «235» era vecchio di giorni.** Al momento di
  > questa nota il verifier riporta **263 bersagli** *(`scratch\REFERTO-SPAM-LOG.md:12`: «263 bersagli controllati,
  > tipo assente 0, METODO assente 0 … baseline 263 confermata», e `scratch\REFERTO-PEER-INVISIBILE.md` lo
  > riconferma sulla DLL deployata)*. **Il campo si è mosso due volte in pochi giorni**: `5021f23` dichiara
  > **255** *(«A5: rimosse le 5 probe … 255 target»)* e il completamento di A5/A6/A7 lo ha riportato a **263**.
  > **La lezione**: *questo è un numero che va letto dal verifier, non copiato* — **il valore buono è quello che
  > `verify-patch-targets.ps1` stampa nell'ultimo deploy**, e nessuna nota storica lo sostituisce.

**Cosa NON contiene / limiti noti**
- **I bug difficili da riprodurre sono dichiarati in `docs\BUG-NOTI.md`.** Fra questi: il **desync
  quando l'host si siede al falò col non-host presente**, l'**AI in pausa dopo il parry**, e le
  **stanze del tutorial**, che vanno **evitate per i test**.
- **`IMPRONTA DIVERSA` non è a zero**: la lista **viva** dei nemici diverge ancora, in modo asimmetrico
  con le morti *(voce 46)*. **Non è più il suffisso fra parentesi** *(`SPAWN (` = 0 occorrenze, e i
  conteggi degli spawn point coincidono)*.
- **La voce 51 è aperta**: al passaggio di proprietà si comunica **chi** simula, **non dove si trova**
  il nemico — *e il nuovo proprietario impone la sua copia.*
  > ⚠️ **CORREZIONE DEL 2026-10-12 (audit della TODO) — la voce 51 è CHIUSA e verificata dall'utente.** La cura
  > è arrivata in cinque tentativi, e **la causa vera era un gate di attività**: l'invio della posizione era
  > condizionato ad `activeInHierarchy`, quindi un nemico **fuori dalla visuale del proprietario non veniva mai
  > trasmesso** e l'altra macchina ripiegava sullo SPAWN *(`df45f72f`, più `3f5ce69`, `a41e7b6`, `4433d70`)*.
  > L'utente: *"abbiamo risolto entrambe la 35 e lo spawn"*. **Vedi `docs\TODO.md`, `### Voce 51`.**
- **La voce 11 è aperta e va RISCRITTA prima di agganciare qualcosa**: il testo indicava
  `EnablePhysics` fra i tipi coinvolti, e **nessuno di quei 16 la chiama** — gli 8 call-site reali sono
  altri. *Un fix costruito sul nome sbagliato è il tipo di errore già costato una sessione.*
  > ⚠️ **CORREZIONE DEL 2026-10-12 (audit della TODO) — la voce 11 è CHIUSA, ed è chiusa PROPRIO perché il testo
  > era sbagliato a metà** *(`a60e1df` / `c55d80e`: «il testo era sbagliato»; il TODO ha al riguardo la sezione
  > «E LA VOCE 11 ERA SCRITTA MALE A METÀ — corretto, verificato in IL», riga 4120)*. **⇒ Non c'è niente da
  > riscrivere prima di agganciare qualcosa: la riscrittura è stata fatta, e la diagnosi corretta è quella.**
- **Le voci 7, 9, 10, 15, 18, 35, 36, 40, 43, 45, 52, 53** sono aperte: ottimizzazioni, procedure e
  difetti minori. Le vedi tutte nella lista iniziale del TODO.
  > ⚠️ **CORREZIONE DEL 2026-10-12 (audit della TODO) — di quelle dodici, ne restano aperte DUE.** Erano
  > **già chiuse** 7 e 10 *(prestazioni: non si manifestano, `c55d80e`)*, 15 *(deploy: procedura coperta)*,
  > 35 *(l'aggro sul non-proprietario: il PROXY, `a307a4c`, confermato dall'utente)*, 36 *(i salvataggi senza
  > Steam: documentata)*, 40 *(non osservabile)*, 43 *(codice morto: scheletro rimosso)*, 45 *(il rescan che
  > appende a `_ids`: `e4269fd`)*, 52 *(A1–A4: `8286d11`, `e6eb9c1`, `b46c651`, `b1e21f8`, `060480b`)* e
  > 53 *(i nemici rivelati da un evento locale: `139a1b5`)*.
  > **RESTANO APERTE SOLO LA 9 E LA 18** — e **la 9 per una ragione diversa da quella che questa riga lasciava
  > intendere**: il **livello di stato** dello spettatore è stato costruito *(`PlayerDeathSync.cs`:
  > `pdead|`/`palive|`, `IsEveryoneDead`, `CountDeadPlayers`)*, mentre **camera sul compagno e body invisibile
  > NON esistono** e nessun referto li verifica in gioco. **La 18** *(boss per boss)* è **verificata aperta**:
  > l'unico boss esercitato nei referti è **`ElderBrother`**. **E c'è una voce che questo elenco non nomina: la
  > 58** *(le cutscene perse da chi entra dopo)*, aperta con la strada già scritta ma il fix non fatto.
  > **La lezione**: questo elenco è invecchiato **mentre le voci venivano chiuse** — *un elenco di "cose aperte"
  > è la cosa che marcisce più in fretta in un documento vivo.*

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
  > ⚠️ **Nota del 2026-10-19**: quello strumento **non esiste più** *(cancellato: rispondeva 0 anche su
  > una DLL vecchia)*. Al suo posto **`tools\marker-check.ps1`**. La riga qui sopra resta come storia.

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
2. **Verifica verde** — `& '.\tools\marker-check.ps1' -Dll 'Modding\plugins\BlasClient.dll' -Marker <…>`
   deve uscire **0** con *"TUTTI i marker richiesti sono PRESENTI"*. *(Prima qui c'era
   `.\tools\verify-deploy.ps1`: cancellato il 2026-10-19 perché usciva 0 anche su una DLL sbagliata.)*
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
