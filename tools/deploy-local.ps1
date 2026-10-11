# Copia la build corrente nella cartella di gioco di QUESTA macchina.
# Da eseguire dopo ogni build: aggiorna mod e server in un colpo solo, cosi' non si
# sbaglia percorso e le due macchine restano allineate.
#
#   pwsh -File "D:\Codificando\Blasphemous COOP\tools\deploy-local.ps1"

param(
    [string]$Game   = 'D:\SteamLibrary\steamapps\common\Blasphemous',
    [string]$Repo   = 'D:\Codificando\Blasphemous COOP\Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Game)) { throw "Cartella di gioco non trovata: $Game" }

# 1. la mod (il gioco la carica da Modding\plugins)
$modSrc = Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\BlasClient.dll'
$modDst = Join-Path $Game 'Modding\plugins\BlasClient.dll'
if (-not (Test-Path $modSrc)) { throw "Mod non compilata: $modSrc" }
Copy-Item $modSrc $modDst -Force

# 2. il server (cartella dedicata dentro il gioco, cosi' si trova subito)
$srvSrc = Join-Path $Repo 'publish\BlasServer'
$srvDst = Join-Path $Game 'SERVER-COOP'
New-Item -ItemType Directory -Path $srvDst -Force | Out-Null
Get-ChildItem $srvSrc -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $srvDst $_.Name) -Force }

"=== deploy completato ==="
"mod    -> $modDst"
"  hash : " + (Get-FileHash $modDst -Algorithm SHA256).Hash.ToLower()
"server -> $srvDst"
"  hash : " + (Get-FileHash (Join-Path $srvDst 'blas1mpserver.dll') -Algorithm SHA256).Hash.ToLower()

# 3. I BERSAGLI DELLE PATCH HARMONY.
#
# Un [HarmonyPatch(typeof(X), "nome")] porta il bersaglio in una STRINGA: se quel metodo non esiste,
# il build passa e il guasto arriva solo a runtime, come HarmonyException dentro PatchAll - che
# INTERROMPE l'applicazione di tutte le patch successive. Il sintomo non assomiglia alla causa: la mod
# smette di scrivere qualsiasi riga di log e l'update per frame esplode con un NullReferenceException
# che non c'entra nulla con la patch sbagliata.
#
# E' successo l'11 ottobre 2026 (OnPlayerSpawn e' un evento, non un metodo) e ha bruciato una sessione.
# Da allora il deploy si ferma se un bersaglio non esiste: e' il momento giusto per scoprirlo, perche'
# e' l'ultimo prima del test.
$verifier = Join-Path $PSScriptRoot 'verify-patch-targets.ps1'
if (Test-Path $verifier) {
    ""
    "=== bersagli delle patch Harmony ==="
    & pwsh -NoProfile -File $verifier -ModDll $modDst
    if ($LASTEXITCODE -ne 0) {
        throw "Il deploy e' stato copiato, ma ci sono bersagli Harmony INESISTENTI: un test ora non misurerebbe niente. Vedi l'elenco sopra."
    }
}
