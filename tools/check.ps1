# tools\check.ps1 — UN comando solo per il giro di verifica: build -> deploy -> marker -> riepilogo.
#
# PERCHE' ESISTE. Il giro era quattro comandi (build, deploy-local, marker-check, e facoltativo headless)
# tenuti a memoria: un giro su due ne saltava uno, e il salto non si vedeva — nessuno dei quattro si
# lamentava di non essere stato chiamato, e "il giro e' verde" voleva dire "i comandi che mi sono
# ricordato sono andati bene". Qui l'ordine e' scritto una volta, ed e' vincolante: la verifica si FERMA
# al primo passo che non passa, con il nome di cosa e' mancato, e con exit 1.
#
# COME SI USA
#   pwsh -NoProfile -File .\tools\check.ps1
#   pwsh -NoProfile -File .\tools\check.ps1 -Game 'D:\...\Blasphemous'          # altra cartella di gioco
#   &     .\tools\check.ps1 -Marker '[Pausa]','[NonEsiste]'                     # per PROVARE il gate
#         (⚠️ l'elenco di -Marker passa solo col call operator &, come per marker-check.ps1:
#          con `pwsh -File` arriverebbe una sola stringa "'[Pausa]','[NonEsiste]'")
#   exit 0 = tutto verde; exit 1 = si e' fermato al primo passo rosso.
#
# COSA FA, IN ORDINE
#   1. build della solution (il comando di sempre: --nologo -v q -nodeReuse:false -clp:ErrorsOnly)
#        · errori > 0 ......................................... exit 1, senza deployare
#        · AVVISI: si contano e si confrontano con la baseline (469). Se salgono lo DICE ("470 (+1: guarda)")
#          e NON fallisce: un avviso nuovo non e' un guasto, e' una cosa da guardare.
#   2. deploy-local.ps1 (mod + Common + server + il gate Harmony dei 273 bersagli: e' dentro il deploy)
#        · exit != 0 .......................................... exit 1
#   3. marker-check.ps1 sulla DLL DEPLOYATA (…\Modding\plugins\BlasClient.dll), per TUTTI i marker del
#      progetto (elenco qui sotto, verificato a byte: ogni stringa compare davvero nella DLL)
#        · anche UN solo marker assente ....................... exit 1, con l'elenco
#   4. riepilogo breve: build · avvisi · deploy · N/N marker · e la COERENZA degli hash
#      (gioco == bin\Debug per mod, Common e server) => "la build deployata e' quella del repo: sha256 …"
#
# ⚠️ PERCORSI. Niente percorsi assoluti, tranne la cartella di gioco (che e' un default e si puo' passare).
# La radice del repo e' la cartella che CONTIENE tools\ ($PSScriptRoot\..), e la solution si CERCA sotto di
# essa: cosi' lo script funziona con la cartella annidata
#   <repo>\Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main\Blasphemous.Multiplayer.sln
# e con quella appiattita
#   <repo>\Blasphemous.Multiplayer.sln
# senza toccare una riga. Scrivere la doppia cartella funziona oggi e si rompe il giorno dell'appiattimento.
# ⚠️ E gli altri .ps1 si invocano con -NoProfile (come fa deploy-local.ps1 con il verifier): un profilo
# utente che stampa o cambia preferenze non deve poter alterare l'esito di un gate.

param(
    [string]$Game = 'D:\SteamLibrary\steamapps\common\Blasphemous',

    # Baseline degli avvisi di build, misurata oggi (2026-10-23). Serve a far PARLARE il numero: senza una
    # base, "469" e' un numero e "470" e' lo stesso numero. Se la baseline va aggiornata, si aggiorna qui,
    # consapevolmente: un +1 puo' essere un avviso legittimo nuovo, e la riga di riepilogo lo fa guardare.
    [int]$WarningBaseline = 469,

    # I MARKER DEL PROGETTO — la lista vera, non inventata: ognuno e' un letterale del codice, e ognuno e'
    # stato trovato a byte nella DLL deployata (UTF-16LE) prima di essere scritto qui. La fonte e' indicata
    # a fianco, cosi' chi cambia un marker sa che questo elenco va cambiato con lui.
    # Si puo' passare un elenco diverso (con il call operator &, non con -File) per PROVARE il gate: e' cosi'
    # che si verifica che il passo 3 sappia dire NO, senza toccare il repo.
    [string[]]$Marker = @(
        '[Pausa]'                    # PauseNotice.cs (MARK) — la pausa condivisa
        '[Tempo]'                    # TimeWhilePaused.cs (MARK) — "la pausa non ferma il mondo"
        '[EnemyInert]'               # EnemySyncManager.cs — le entita' di scena immobili per costruzione
        '[Server] payload corto'     # Server.cs (linkato nel client: e' l'host) — il pacchetto scartato e dichiarato
        '[Host]'                     # HostSession.cs — la famiglia del server locale/loopback
        '[Host] ascolto su='         # HostSession.cs (MarkerBind) — la riga di ascolto col trasporto
        '[FxRelay]'                  # EffectRelay.cs / EffectRelayInstantiateArea.cs — gli effetti tra peer
        '[ColpaVita]'                # LifeGuilt.cs (MARK) — la colpa
        '[BossLife]'                 # EnemySyncManager.cs — la vita del boss allineata ("stato + disegno")
        '[BossCorpse]'               # EnemySyncManager.cs — il cadavere del boss
        '[DannoVita]'                # PenitentLifeMarker.cs — l'importo del danno del penitente
        '[Impronta]'                 # SyncIdListMarker.cs — le due liste di id a confronto
        '[Net] skin:'                # SkinSendMarker.cs — la misura di una skin
        'YOUR TEAMMATE IS PAUSED'    # PauseNotice.cs (TEXT) — l'avviso a schermo: testo vero, non codice
    )
)

$ErrorActionPreference = 'Stop'

# Con $PSNativeCommandUseErrorActionPreference a $true (profilo utente, PS 7.3+) un exit != 0 di dotnet o
# pwsh diventerebbe un'ECCEZIONE prima che io possa leggere l'exit code e dire cosa e' mancato. Il codice di
# uscita lo leggo io, passo per passo: quindi la variabile si spegne.
$PSNativeCommandUseErrorActionPreference = $false

function Stop-Check {
    param([string]$Messaggio)
    ""
    "=== FERMATO ==="
    $Messaggio
    exit 1
}

function Read-BuildCount {
    # Il riepilogo di MSBuild e' localizzato ("Warning(s)" / "Avvisi:"). Si leggono entrambe le forme: la
    # lingua del giro la forzo (VSLANG), ma non voglio che un giorno un locale diverso faccia leggere 0.
    param([string[]]$Righe, [string]$Inglese, [string]$Italiano)
    $valore = $null
    foreach ($r in $Righe) {
        if ($r -match ('^\s*(\d+)\s+' + $Inglese + '\s*$')) { $valore = [int]$Matches[1] }
        elseif ($r -match ('^\s*' + $Italiano + ':\s*(\d+)\s*$')) { $valore = [int]$Matches[1] }
    }
    return $valore
}

# --- LA RADICE DEL REPO: si trova, non si scrive (vedi l'intestazione) ------------------------------
$RepoRootGuess = Split-Path -Parent $PSScriptRoot
$sln = Get-ChildItem -LiteralPath $RepoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -ErrorAction SilentlyContinue |
       Select-Object -First 1
if (-not $sln) {
    $sln = Get-ChildItem -LiteralPath $RepoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -Recurse -ErrorAction SilentlyContinue |
           Select-Object -First 1
}
if (-not $sln) {
    Stop-Check "Solution non trovata sotto '$RepoRootGuess': cercavo 'Blasphemous.Multiplayer.sln'. Senza la solution la verifica non parte (non so cosa compilare, e senza la build non c'e' niente da verificare)."
}
$SlnPath = $sln.FullName
$Repo    = $sln.DirectoryName

$DeployScript = Join-Path $PSScriptRoot 'deploy-local.ps1'
$MarkerScript = Join-Path $PSScriptRoot 'marker-check.ps1'
foreach ($f in @($DeployScript, $MarkerScript)) {
    if (-not (Test-Path $f)) {
        Stop-Check "Manca '$f': il giro non si puo' fare (uno dei due .ps1 e' stato rinominato o spostato)."
    }
}
if (-not (Test-Path $Game)) {
    Stop-Check "Cartella di gioco non trovata: '$Game'. Passa la cartella vera con -Game (questo script non la indovina)."
}

# ⚠️ TRAPPOLA DELL'ARRAY, verificata: con `pwsh -NoProfile -File .\tools\check.ps1 -Marker 'a','b'` l'elenco
# NON arriva come elenco — la riga di comando lo fonde in UNA stringa ("'a','b'"), che non esiste in nessuna
# DLL, e il gate fallirebbe per la ragione sbagliata (sembrerebbe "marker assente" invece di "lo hai scritto
# male"). Vale lo stesso per marker-check.ps1. Quindi, se un unico "marker" contiene una virgola, si dice.
if ($Marker.Count -eq 1 -and $Marker[0] -ne '' -and $Marker[0].Contains(',')) {
    Stop-Check "Il valore di -Marker e' finito in UNA sola stringa: '$($Marker[0])'. Con -File l'elenco non passa (non e' un difetto di questo script: e' come PowerShell costruisce la riga di comando). Per provare il gate usa il call operator, che passa l'array:  & .\tools\check.ps1 -Marker '[Pausa]','[NonEsiste]'  — oppure un solo marker per volta con -File."
}

"=== check: repo $Repo ==="
"=== check: gioco $Game ==="

# --- 1/3 BUILD --------------------------------------------------------------------------------------
""
"=== 1/3 build della solution ==="
# La lingua delle righe di riepilogo la decide lo script, non la macchina: il conteggio degli avvisi si legge
# da "469 Warning(s)". In italiano MSBuild scrive "Avvisi: 469" (verificato su questa macchina) e il conteggio
# non si trova piu'. VSLANG sceglie la lingua delle risorse di MSBuild, DOTNET_CLI_UI_LANGUAGE quella della CLI.
$env:VSLANG = '1033'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

# --no-incremental: con la build INCREMENTALE una soluzione gia' compilata non ricompila niente e il
# riepilogo dice "0 Warning(s)" — il confronto con la baseline diventerebbe rumore, e un avviso NUOVO non si
# vedrebbe proprio (il difetto del giro a mano: "0 avvisi" sembrava un miglioramento). Il costo sono pochi
# secondi; in cambio il numero e' sempre quello vero, ogniqualvolta.
$buildArgs = @('build', $SlnPath, '--nologo', '-v', 'q', '-nodeReuse:false', '-clp:ErrorsOnly', '--no-incremental')
$eapPrima = $ErrorActionPreference
$ErrorActionPreference = 'Continue'   # l'output di dotnet su stderr non deve diventare un'eccezione qui
$buildOut = @(& dotnet @buildArgs 2>&1 | ForEach-Object { "$_" })
$buildCode = $LASTEXITCODE
$ErrorActionPreference = $eapPrima

# Solo il riepilogo (con -clp:ErrorsOnly le righe degli avvisi non ci sono, gli errori si'): si stampa tutto.
foreach ($riga in $buildOut) { if ($riga.Trim() -ne '') { "    $riga" } }

$avvisi = Read-BuildCount -Righe $buildOut -Inglese 'Warning\(s\)' -Italiano 'Avvisi'
$errori = Read-BuildCount -Righe $buildOut -Inglese 'Error\(s\)'   -Italiano 'Errori'

if ($buildCode -ne 0 -or ($null -ne $errori -and $errori -gt 0)) {
    $quanti = if ($null -ne $errori) { "$errori errori" } else { "exit $buildCode" }
    Stop-Check "BUILD FALLITA ($quanti). Gli errori sono qui sopra (a MSBuild ho chiesto solo gli errori: -clp:ErrorsOnly). Il deploy NON e' stato eseguito: non si copia nel gioco una build che non compila."
}
if ($null -eq $errori) {
    # Non e' un dettaglio da nascondere: se il riepilogo non si legge, l'exit code resta l'unica prova, e va detto.
    "    (il riepilogo di MSBuild non ha una riga 'Error(s)'/'Errori:': vale l'exit code, che e' 0)"
}

if ($null -eq $avvisi) {
    $rigaAvvisi = "avvisi: non ho potuto leggere il conteggio dal riepilogo di MSBuild (guarda l'output qui sopra)"
} elseif ($avvisi -eq $WarningBaseline) {
    $rigaAvvisi = "avvisi: $avvisi (baseline)"
} elseif ($avvisi -gt $WarningBaseline) {
    $delta = $avvisi - $WarningBaseline
    $rigaAvvisi = "avvisi: $avvisi (+$delta : guarda)"
} else {
    $delta = $WarningBaseline - $avvisi
    $rigaAvvisi = "avvisi: $avvisi (-$delta sotto la baseline: un avviso e' sparito — se non e' voluto, guarda)"
}
$rigaAvvisi

# --- 2/3 DEPLOY -------------------------------------------------------------------------------------
""
"=== 2/3 deploy nella cartella di gioco ==="
# -NoProfile come fa deploy-local.ps1 col verifier. L'output del deploy si lascia passare (si vede il gate
# Harmony lavorare), e quello che conta qui e' l'exit code.
& pwsh -NoProfile -File $DeployScript -Game $Game
if ($LASTEXITCODE -ne 0) {
    Stop-Check "DEPLOY FALLITO (exit $LASTEXITCODE): leggi il messaggio qui sopra — e' quello che dice cosa non e' passato (gate Harmony dei bersagli, file mancanti, cartella di gioco). Il marker check NON e' stato eseguito: la DLL nel gioco non e' quella che il gate doveva controllare, quindi controllarla non misurerebbe niente."
}

# --- 3/3 MARKER SULLA DLL DEPLOYATA -----------------------------------------------------------------
""
"=== 3/3 marker sulla DLL DEPLOYATA ==="
$DllDeployata = Join-Path $Game 'Modding\plugins\BlasClient.dll'
if (-not (Test-Path $DllDeployata)) {
    Stop-Check "DLL deployata non trovata: '$DllDeployata'. Il deploy e' uscito con 0 ma il file non c'e': non c'e' niente da verificare a byte."
}

# marker-check ACCETTA UNA LISTA — ma solo se lo si chiama col call operator, come qui: con `pwsh -File`
# l'array si fonde in una sola stringa e marker-check cercherebbe quella (verificato: "'[Pausa]','[X]'"),
# cioè un falso "ASSENTE". Quindi: una chiamata sola, in-process, con l'elenco intero.
# ⚠️ nota misurata: un `exit N` dentro uno script chiamato con `&` NON termina il chiamante (lo termina
# solo se lo script è quello d'ingresso del processo) — quindi il suo `exit 0/1` è innocuo qui, e arriva
# in $LASTEXITCODE. L'esito però lo leggo dalla riga "ESITO:" che marker-check è tenuto a stampare (le
# docs la citano come la prova), non da un exit code: se un giorno cambia una delle due, il check lo dice
# invece di fidarsi del proxy sbagliato.
$markerOut = @()
try {
    $markerOut = @(& $MarkerScript -Dll $DllDeployata -Marker $Marker 2>&1 | ForEach-Object { "$_" })
    $markerCode = $LASTEXITCODE
} catch {
    Stop-Check "marker-check e' fallito: $($_.Exception.Message). Non e' 'un marker mancante': la verifica a byte non e' stata eseguita."
}
# Il referto di marker-check si stampa com'e' (una riga per marker, con la codifica e l'offset).
$markerOut | Where-Object { $_ -match '^\s*\[(PRESENTE|ASSENTE)' }

$verdetto = @($markerOut | Where-Object { $_ -match '^\s*ESITO:' }) | Select-Object -First 1
$assenti = @($markerOut |
    Where-Object { $_ -match '^\s*\[ASSENTE' } |
    ForEach-Object { if ($_ -match '\[ASSENTE\s*\]\s*"([^"]*)"') { $Matches[1] } else { '(riga non interpretabile)' } })

if (-not $verdetto) {
    Stop-Check "marker-check non ha stampato la riga ESITO (exit $markerCode): la verifica a byte non e' arrivata a un verdetto, quindi non c'e' niente da credere."
}
if ($verdetto -notmatch 'TUTTI i marker richiesti sono PRESENTI') {
    Stop-Check ("MARKER ASSENTI nella DLL deployata: " + $(if ($assenti.Count -gt 0) { $assenti -join ', ' } else { 'vedi il verdetto qui sopra' }) + " — $($assenti.Count) di $($Marker.Count). La DLL che il gioco esegue non contiene quel codice: o e' una build vecchia, o il marker nel codice e' stato scritto diversamente. Se l'elenco e' sbagliato (marker rinominato nel codice), va aggiornato QUI e in tools\marker-check.ps1: non e' un falso allarme da ignorare.")
}
"  ESITO: $($Marker.Count)/$($Marker.Count) marker presenti"

# --- 4/4 RIEPILOGO E COERENZA DEGLI HASH -------------------------------------------------------------
# I tre artefatti che il gioco usa (mod, Common, server) devono essere quelli di bin\Debug: se il gioco ha
# qualcosa di diverso, il deploy ha copiato altro o qualcuno ha riscritto il gioco dopo — e allora tutto
# quello che ho scritto sopra descrive una build che il gioco NON esegue.
$coppie = @(
    [pscustomobject]@{ Nome = 'mod';    Repo = (Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\BlasClient.dll');                            Gioco = $DllDeployata }
    [pscustomobject]@{ Nome = 'common'; Repo = (Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\Blasphemous.Multiplayer.Common.dll');       Gioco = (Join-Path $Game 'Modding\data\Blasphemous.Multiplayer.Common.dll') }
    [pscustomobject]@{ Nome = 'server'; Repo = (Join-Path $Repo 'Blasphemous.Multiplayer.Server\bin\Debug\net8.0\blas1mpserver.dll');                 Gioco = (Join-Path $Game 'SERVER-COOP\blas1mpserver.dll') }
)

$diverse = @()
$hashMod = $null
$righeCoerenza = @()
foreach ($c in $coppie) {
    if (-not (Test-Path $c.Repo))  { $diverse += "$($c.Nome): manca nella build ($($c.Repo))";    continue }
    if (-not (Test-Path $c.Gioco)) { $diverse += "$($c.Nome): manca nel gioco ($($c.Gioco))";    continue }
    $hRepo  = (Get-FileHash $c.Repo  -Algorithm SHA256).Hash.ToLower()
    $hGioco = (Get-FileHash $c.Gioco -Algorithm SHA256).Hash.ToLower()
    $uguale = ($hRepo -eq $hGioco)
    if (-not $uguale) { $diverse += "$($c.Nome): build=$hRepo gioco=$hGioco" }
    if ($c.Nome -eq 'mod') { $hashMod = $hGioco }
    $righeCoerenza += ("  {0} {1,-7} build={2}… gioco={3}… {4}" -f '[hash]', $c.Nome, $hRepo.Substring(0, 12), $hGioco.Substring(0, 12), $(if ($uguale) { 'UGUALE' } else { 'DIVERSO' }))
}

""
"=== RIEPILOGO ==="
"  build  : OK, 0 errori · " + ($rigaAvvisi -replace '^avvisi: ', '')
"  deploy : OK (include il gate Harmony dei bersagli delle patch)"
"  marker : $($Marker.Count)/$($Marker.Count) presenti sulla DLL deployata"
$righeCoerenza
if ($diverse.Count -gt 0) {
    Stop-Check ("COERENZA: il gioco NON contiene la build del repo. " + ($diverse -join ' | ') + ". Il giro qui sopra descrive una build che il gioco non esegue: o il deploy ha copiato altro, o qualcosa ha riscritto la cartella dopo. Rifai il deploy (e se torna diverso, guarda COSA lo riscrive).")
}
"  la build deployata e' quella del repo: sha256 $hashMod"
""
"=== TUTTO VERDE ==="
exit 0
