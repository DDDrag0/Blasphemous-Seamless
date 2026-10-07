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
