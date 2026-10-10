# PUNTI DI RIPRISTINO — Blasphemous COOP

> **A cosa serve questo file.** I tag git sono il punto di ripristino vero, ma vivono nel repository e
> non dicono *cosa* contengono né *quali binari* corrispondono loro. Qui c'è tutto quello che serve per
> tornare indietro in un minuto, senza ricostruire la storia a memoria.

---

## Come si torna indietro

```powershell
git checkout checkpoint-stabile-falo        # oppure checkpoint-stabile-falò: stesso commit
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
