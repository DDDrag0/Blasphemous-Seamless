# Copia la build corrente nella cartella di gioco di QUESTA macchina.
# Da eseguire dopo ogni build: aggiorna mod e server in un colpo solo, cosi' non si
# sbaglia percorso e le due macchine restano allineate.
#
#   pwsh -File "D:\Codificando\Blasphemous COOP\tools\deploy-local.ps1"
#
# QUESTO SCRIPT E' L'UNICO MODO DI METTERE QUALCOSA NELLA CARTELLA DI GIOCO.
# Fino al 2026-10-16 non era vero: il target `Development` del .csproj del client copiava da solo in
# Modding\, quindi OGNI `dotnet build` — in qualunque worktree — riscriveva il gioco e poteva
# annullare in silenzio il deploy di HEAD (per giorni si e' creduto di testare una versione mentre il
# gioco ne eseguiva un'altra). Ora la build non tocca piu' il gioco (DeployToGame=false, vedi il
# .csproj) e quella copia e' stata portata qui, dove e' esplicita e voluta.

param(
    [string]$Game   = 'D:\SteamLibrary\steamapps\common\Blasphemous',
    [string]$Repo   = 'D:\Codificando\Blasphemous COOP\Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Game)) { throw "Cartella di gioco non trovata: $Game" }

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

# 3. il server (cartella dedicata dentro il gioco, cosi' si trova subito)
$srvSrc = Join-Path $Repo 'publish\BlasServer'
$srvDst = Join-Path $Game 'SERVER-COOP'
New-Item -ItemType Directory -Path $srvDst -Force | Out-Null
Get-ChildItem $srvSrc -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $srvDst $_.Name) -Force }

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
