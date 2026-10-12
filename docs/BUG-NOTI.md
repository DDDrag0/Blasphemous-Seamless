# BUG NOTI — Blasphemous COOP

> **A cosa serve questo file.** Alcuni difetti sono **troppo difficili da riprodurre in laboratorio** per
> poterli chiudere qui: dipendono da una combinazione di stanza, tutorial, ordine di ingresso e
> tempismo che non si riesce a ricreare a comando. Sono reali — li hanno visti i giocatori — ma non
> sono diagnosticabili con un test controllato.
>
> **Quindi si dichiarano.** Un bug noto e scritto è un bug su cui, quando un giocatore lo riporta, si
> sa già dove guardare. Un bug noto e taciuto è una segnalazione che si ricomincia da zero.
>
> **⚠️ E vale la regola del progetto**: *un difetto difficile da riprodurre **non** è un difetto da
> aggirare con un fix "a naso".* Meglio lasciarlo scritto e aspettare un log.

---

## 1. Le stanze del tutorial sono inaffidabili — sprite e nemici non respawnano al falò

**Cosa si vede.** Nelle stanze con i **tutorial a sprite** (quelli che spiegano una meccanica), sedersi
al falò **non fa respawnare gli sprite del tutorial**, mentre **in tutte le altre stanze il respawn
funziona**. E in generale quelle stanze si comportano in modo anomalo: è lì che si sono visti il
maggior numero di difetti difficili da riprodurre.

**Perché non lo chiudiamo qui.** Il respawn degli sprite del tutorial è **logica di scena del gioco**,
non nostra: la mod non ha un percorso per i tutorial (`ShowTutorial` = 0 in tutti i log misurati).
E la stanza **mischiata al tutorial** rende ogni altro sintomo ambiguo — *"è un bug del multi o è il
tutorial che si comporta così?"* non ha risposta in quella stanza.

**Cosa fare.** **Non testare le meccaniche in quelle stanze.** Si perdono ore su sintomi che non hanno
niente a che vedere col multi. **Se un giocatore le attraversa, va bene** — ma un difetto osservato lì
va riportato **e poi riverificato altrove** prima di indagarlo.

---

## 2. Desync quando l'HOST si siede al falò e il NON-HOST è nella stanza

**Cosa si vede.** *Le due macchine simulano due stanze differenti.* **Uscire e rientrare risolve.**

**La condizione isolata** *(dall'osservazione diretta, molte interazioni)*:
- **l'HOST si siede al falò**, **e**
- **il NON-HOST è presente nella stanza in quel momento**.

**Indifferente**: l'ordine di ingresso *(chi entra prima)*, l'ordine dell'aggro, e **se il trigger di
aggro iniziale è scattato o no**.
**Non si vede** se il non-host **non** è nella stanza.

**Perché è difficile.** Serve una combinazione precisa di presenza e tempismo, e la finestra è breve.
È il tipo di difetto che **emerge giocando**, non pilotando un test.

**⚠️ Nota**: la **voce 4** del TODO è *"il falò si propaga"* ed è **verificata** in entrambi i casi
*(stessa stanza e stanze diverse)*. Questo difetto è **un'altra cosa** e va tenuto separato: non
rimette in dubbio la propagazione, ma dice che **in una condizione specifica lo stato delle due
macchine diverge lo stesso.**

---

## 3. L'AI va in pausa dopo il parry, e l'aggro è difficile

**Cosa si vede**, sul **non-host**:
- l'AI di un nemico **sembra andare in pausa** quando il nemico fa **parry / contrattacco**;
- un nemico **fatica ad andare in aggro** se non **dopo aver colpito o essere stato colpito**.

**L'ipotesi, coerente col disegno.** La mod **mette in pausa l'albero di comportamento sui
non-proprietari** *(è il modo in cui un "puppet" segue la posizione invece di simulare)*. Un albero in
pausa **non decide**: quindi non completa una reazione e **non entra in aggro da solo**. Sarebbe
**conseguenza del disegno**, non un difetto nuovo — ma **non è verificato**, e va misurato prima di
toccare qualsiasi cosa.

**Cosa servirebbe.** Un log che dica, per il nemico vicino al non-host: **chi lo possiede**, **se
l'albero è in pausa**, e **cosa fa l'aggro** quando il giocatore si avvicina senza colpire.

---

## 4. I salvataggi cambiano cartella se Steam non è avviato

**Cosa si vede.** La partita sembra **persa o corrotta** dopo un avvio senza Steam.

**Non è corruzione.** Il percorso è
`persistentDataPath + "/Savegames/" + (SteamManager.Initialized ? SteamID : "") + "/"`, quindi
**senza Steam i salvataggi vanno in una cartella diversa** — e con **Steam Cloud** attivo le due
cartelle divergono.

**Cosa fare.** **Avvia SEMPRE Steam prima del gioco.** La mod scrive `[Path]` all'avvio con la cartella
in uso: *quella riga è la risposta a "dove sono finiti i salvataggi".*

**Stato.** Documentato *(voce 36)*. Non è un difetto della mod e non è fixabile da qui.

---

## 5. Fuori portata, con la causa nota: la divergenza dopo un falò

**Cosa si vede.** Dopo un falò i due mondi possono restare **`5` contro `4`** nemici nella stessa
stanza.

**Causa parziale, misurata.** La divergenza residua **non è più la lista degli spawn point** *(i
conteggi ora coincidono: 5/5 e 9/9)*: è la **lista VIVA** dei nemici, che **si accorcia in modo
asimmetrico con le morti** — gli hash dei due log sono **incrociati**, cioè le due liste vive sono
davvero diverse.

**Perché è qui.** Va indagato con un test dedicato *(un falò con nemici uccisi in stanze diverse)* e
**non** è la stessa cosa delle morti o della proprietà. **Voce 46.**

---

## Come si riporta un bug noto *(per chi gioca e per i tester)*

**Quello che serve**, in ordine di utilità:

1. **Cosa hai visto**, in una frase, **e dove** *(quale stanza, o che aspetto aveva)*.
2. **Cosa stavate facendo**: chi era **host**, chi era **nella stanza**, e **cosa è successo subito prima**
   *(un falò, una morte, un'esecuzione, un cambio di stanza)*.
3. **L'ordine degli eventi**, anche approssimativo. *"Prima X, poi Y"* vale più di un minuto di descrizione.
4. ⭐ **I due log**, uno per macchina:
   `%USERPROFILE%\AppData\LocalLow\TheGameKitchen\Blasphemous\output_log.txt`
   **E il log del server**, `ServerOutput.log`, nella cartella del server.
5. ⭐ **E la cosa più utile di tutte: la ricetta.** *"Succede quando faccio A, B e C, nell'ordine."*
   **Un difetto riproducibile a comando è un difetto che si chiude.**

**⚠️ E una cosa da NON fare**: *non descrivere il sintomo con la causa che immaginate.*
*"Il nemico si teletrasporta perché la sincronizzazione è rotta"* è **meno** utile di
*"il nemico era a sinistra e dopo un secondo era tornato al punto di partenza"*.
**Il sintomo si osserva, la causa si ipotizza — e le ipotesi sbagliate costano sessioni.**
