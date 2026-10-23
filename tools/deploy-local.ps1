# Copia la build corrente nella cartella di gioco di QUESTA macchina.
# Da eseguire dopo ogni build: aggiorna mod e server in un colpo solo, cosi' non si
# sbaglia percorso e le due macchine restano allineate.
#
#   pwsh -NoProfile -File .\tools\deploy-local.ps1
#
# QUESTO SCRIPT E' L'UNICO MODO DI METTERE QUALCOSA NELLA CARTELLA DI GIOCO.
# Fino al 2026-10-16 non era vero: il target `Development` del .csproj del client copiava da solo in
# Modding\, quindi OGNI `dotnet build` — in qualunque worktree — riscriveva il gioco e poteva
# annullare in silenzio il deploy di HEAD (per giorni si e' creduto di testare una versione mentre il
# gioco ne eseguiva un'altra). Ora la build non tocca piu' il gioco (DeployToGame=false, vedi il
# .csproj) e quella copia e' stata portata qui, dove e' esplicita e voluta.

param(
    [string]$Game   = 'D:\SteamLibrary\steamapps\common\Blasphemous',
    # Radice del repo: NON si scrive, si TROVA. La radice e' la cartella che contiene la solution
    # (Blasphemous.Multiplayer.sln), e la si cerca sotto la radice del repo (<repo>\tools\deploy-local.ps1
    # sta un livello sotto, quindi Split-Path $PSScriptRoot -Parent). Le due forme che deve reggere:
    #   · annidata  : <repo>\Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main\*.sln
    #   · appiattita: <repo>\*.sln
    # Storia, perche' e' la ragione di questa forma: prima era 'D:\Codificando\Blasphemous COOP\...'
    # scritto a mano, e la rinomina della cartella avrebbe fatto fallire il deploy (o, peggio, l'avrebbe
    # fatto puntare a una copia vecchia); poi e' diventato relativo ma con la DOPPIA cartella, e
    # l'appiattimento l'avrebbe rotto lo stesso. Cercarlo e' l'unica forma che sopravvive a entrambe.
    # Costo: ~10 ms.
    [string]$Repo   = ''
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Game)) { throw "Cartella di gioco non trovata: $Game" }

# La radice del repo si ricava dalla solution, cercata SOTTO la radice (vedi il commento su -Repo).
if (-not $Repo) {
    $repoRootGuess = Split-Path -Parent $PSScriptRoot
    $sln = Get-ChildItem -LiteralPath $repoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -ErrorAction SilentlyContinue |
           Select-Object -First 1
    if (-not $sln) {
        $sln = Get-ChildItem -LiteralPath $repoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -Recurse -ErrorAction SilentlyContinue |
               Select-Object -First 1
    }
    if (-not $sln) {
        throw "Solution non trovata sotto '$repoRootGuess': cercavo 'Blasphemous.Multiplayer.sln'. Senza la solution la radice del repo non si ricava, quindi il deploy non parte (non si copia nel gioco una build di cui non si sa la provenienza)."
    }
    $Repo = $sln.DirectoryName
}
if (-not (Test-Path (Join-Path $Repo 'Blasphemous.Multiplayer.sln'))) {
    throw "Radice del repo non valida: '$Repo' non contiene Blasphemous.Multiplayer.sln (passata a mano con -Repo?)."
}

# IL GATE HARMONY NON E' UN ACCESSORIO, quindi si controlla PRIMA di copiare: se lo script non c'e'
# (rinominato, spostato, checkout parziale) il deploy si ferma qui, senza toccare la cartella di gioco.
# Prima era `if (Test-Path $verifier) { ... }` in fondo: il gate spariva in silenzio e il deploy
# proseguiva come se la verifica fosse stata fatta. Un gate opzionale non e' un gate.
$verifier = Join-Path $PSScriptRoot 'verify-patch-targets.ps1'
if (-not (Test-Path $verifier)) {
    throw "Gate Harmony non trovato: $verifier - la verifica NON e' stata eseguita, quindi il deploy non parte (non si copia una build che nessuno ha controllato)."
}

# 1. le RISORSE della mod, nella stessa forma in cui le copiava la build (resources\** -> Modding\**).
#    Non e' cosmesi: da qui arriva Common.dll, che il ModdingAPI risolve a runtime da Modding\data
#    (Main.LoadMissingAssemblies), e che contiene il numero di protocollo e NetworkType. Una
#    Common.dll vecchia con una BlasClient.dll nuova e' la divergenza silenziosa che questo script
#    esiste per evitare (docs\TODO.md 7d: "mod, server e Common.dll vanno copiati insieme").
$resSrc  = Join-Path $Repo 'resources'
$modRoot = Join-Path $Game 'Modding'
if (-not (Test-Path $resSrc)) { throw "Risorse non trovate: $resSrc" }
Get-ChildItem $resSrc -Recurse -File | ForEach-Object {
    $rel  = $_.FullName.Substring($resSrc.Length + 1)
    $dest = Join-Path $modRoot $rel
    New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
    Copy-Item $_.FullName $dest -Force
}

# 2. la mod (il gioco la carica da Modding\plugins) e la sua Common.dll, prese DALLO STESSO BUILD:
#    bin\Debug, non resources\ (che la build aggiorna solo se la build e' andata a buon fine).
#    Sono le ultime scritture di proposito: se le due copie di Common.dll divergessero, vince quella
#    accoppiata alla DLL che stiamo deployando.
$modSrc = Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\BlasClient.dll'
$modDst = Join-Path $Game 'Modding\plugins\BlasClient.dll'
if (-not (Test-Path $modSrc)) { throw "Mod non compilata: $modSrc" }
New-Item -ItemType Directory -Path (Split-Path $modDst) -Force | Out-Null
Copy-Item $modSrc $modDst -Force

$commonSrc = Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\Blasphemous.Multiplayer.Common.dll'
$commonDst = Join-Path $Game 'Modding\data\Blasphemous.Multiplayer.Common.dll'
if (-not (Test-Path $commonSrc)) { throw "Common.dll non compilata: $commonSrc" }
New-Item -ItemType Directory -Path (Split-Path $commonDst) -Force | Out-Null
Copy-Item $commonSrc $commonDst -Force

# 3. il server (cartella dedicata dentro il gioco, cosi' si trova subito).
#
# ⚠️ SI PRENDE DA bin\Debug\net8.0, NON DA publish\BlasServer.
# PERCHE': publish\BlasServer non e' un prodotto della compilazione, la crea il target `PrepareBuild` del
# .csproj del server (AfterTargets="PostBuildEvent"), e quel target riparte SOLO quando il server viene
# ricompilato. Con una build INCREMENTALE (client toccato, server no) la cartella resta ferma all'ultima
# ricompilazione del server: il deploy copierebbe un server VECCHIO senza dirlo, e la mod e il server nel
# gioco sarebbero di due build diverse — proprio la divergenza silenziosa che questo script esiste per
# evitare (docs\TODO.md 7d: "mod, server e Common.dll vanno copiati insieme"). In piu' quel target fa
# RemoveDir della cartella a ogni giro, e la cancella chi fa pulizia: con publish\ assente il deploy si
# FERMAVA, pur avendo in mano un server perfettamente valido. bin\Debug\net8.0 la scrive il compilatore:
# se la build e' andata a buon fine, li' c'e' il server di QUELLA build.
$srvSrc = Join-Path $Repo 'Blasphemous.Multiplayer.Server\bin\Debug\net8.0'
$srvDst = Join-Path $Game 'SERVER-COOP'
if (-not (Test-Path $srvSrc)) {
    throw "Server non compilato: '$srvSrc' non esiste. Esegui prima la build della solution (tools\check.ps1 fa build e deploy in un colpo solo)."
}

# Cosa DEVE esserci perche' SERVER-COOP parta: l'eseguibile, la sua dll e i due json che .NET legge al
# lancio, il pdb (per le tracce) e le tre dipendenze che il server non si porta dentro (Basalt.CommandParser
# per la riga di comando, SimpleTCP per il trasporto, Common per protocollo/NetworkType). Se ne manca una il
# server si avvia e muore: meglio fermarsi qui, con il nome in chiaro, che scoprirlo in partita.
# ⚠️ Se il server acquisisce una dipendenza NUOVA, va aggiunta qui: e' l'elenco di cio' che il deploy
#    garantisce di aver messo in SERVER-COOP.
$srvNeeded = @(
    'blas1mpserver.exe'
    'blas1mpserver.dll'
    'blas1mpserver.deps.json'
    'blas1mpserver.runtimeconfig.json'
    'blas1mpserver.pdb'
    'Basalt.CommandParser.dll'
    'SimpleTCP.dll'
    'Blasphemous.Multiplayer.Common.dll'
    'Blasphemous.Multiplayer.Common.pdb'
)
$srvFiles   = @(Get-ChildItem $srvSrc -File | Select-Object -ExpandProperty Name)
$srvMissing = @($srvNeeded | Where-Object { $srvFiles -notcontains $_ })
if ($srvMissing.Count -gt 0) {
    throw ("In '$srvSrc' mancano $($srvMissing.Count) file che SERVER-COOP usa: " + ($srvMissing -join ', ') + ". Non e' un dettaglio: senza uno di questi il server non parte, quindi il deploy non copia niente.")
}

New-Item -ItemType Directory -Path $srvDst -Force | Out-Null
# Si copiano i FILE della cartella (non le sottocartelle: il filtro resta quello di sempre) ma NON i log.
# Ragione: se qualcuno ha lanciato il server da bin\Debug\net8.0, li' e' rimasto un ServerOutput.log; copiarlo
# sovrascriverebbe il log VIVO di SERVER-COOP, che e' suo e non si tocca. Il log non e' un artefatto da
# deployare, e' la traccia di quello che e' successo la' dentro.
Get-ChildItem $srvSrc -File |
    Where-Object { $_.Extension -ne '.log' } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $srvDst $_.Name) -Force }

"=== deploy completato ==="
"mod    -> $modDst"
"  hash : " + (Get-FileHash $modDst -Algorithm SHA256).Hash.ToLower()
"common -> $commonDst"
"  hash : " + (Get-FileHash $commonDst -Algorithm SHA256).Hash.ToLower()
"server -> $srvDst"
"  hash : " + (Get-FileHash (Join-Path $srvDst 'blas1mpserver.dll') -Algorithm SHA256).Hash.ToLower()

# 4. I BERSAGLI DELLE PATCH HARMONY.
#
# Un [HarmonyPatch(typeof(X), "nome")] porta il bersaglio in una STRINGA: se quel metodo non esiste,
# il build passa e il guasto arriva solo a runtime, come HarmonyException dentro PatchAll - che
# INTERROMPE l'applicazione di tutte le patch successive. Il sintomo non assomiglia alla causa: la mod
# smette di scrivere qualsiasi riga di log e l'update per frame esplode con un NullReferenceException
# che non c'entra nulla con la patch sbagliata.
#
# E' successo l'11 ottobre 2026 (OnPlayerSpawn e' un evento, non un metodo) e ha bruciato una sessione.
# Da allora il deploy si ferma se un bersaglio non esiste: e' il momento giusto per scoprirlo, perche'
# e' l'ultimo prima del test. Il verifier e' stato controllato all'inizio: qui si esegue sempre.
""
"=== bersagli delle patch Harmony ==="
& pwsh -NoProfile -File $verifier -ModDll $modDst
if ($LASTEXITCODE -ne 0) {
    # Due cause possibili, e la vecchia riga ne dichiarava una sola ("bersagli INESISTENTI"): puo'
    # anche essere che il verifier non sia riuscito a compilare la sua sonda. Ripetere qui una
    # diagnosi che il verifier ha gia' smentito e' il difetto che questo script non deve avere.
    throw "Il deploy e' stato copiato, ma il gate Harmony NON e' passato (exit $LASTEXITCODE): leggi il messaggio qui sopra. Se dice 'non ho potuto compilare la sonda', il problema NON sono le patch: la verifica non e' stata eseguita. In entrambi i casi un test ora non misurerebbe niente."
}
