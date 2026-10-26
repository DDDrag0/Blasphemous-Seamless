# Copies the current build into this machine's game folder.
# Run after every build: updates mod and server in one step, so the paths can't drift and the two
# machines stay in sync.
#
#   pwsh -NoProfile -File .\tools\deploy-local.ps1
#
# This is the only way to write anything into the game folder: the build itself no longer touches
# the game (DeployToGame=false in the client .csproj), so the copy is explicit and intentional here.

param(
    [string]$Game   = 'D:\SteamLibrary\steamapps\common\Blasphemous',
    # Repo root: found, not written. It is the folder that contains the solution, searched under
    # the repo root ($PSScriptRoot\..\..). Cost: ~10 ms.
    [string]$Repo   = ''
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Game)) { throw "Game folder not found: $Game" }

# Repo root derived from the solution, searched under $PSScriptRoot's parent.
if (-not $Repo) {
    $repoRootGuess = Split-Path -Parent $PSScriptRoot
    $sln = Get-ChildItem -LiteralPath $repoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -ErrorAction SilentlyContinue |
           Select-Object -First 1
    if (-not $sln) {
        $sln = Get-ChildItem -LiteralPath $repoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -Recurse -ErrorAction SilentlyContinue |
               Select-Object -First 1
    }
    if (-not $sln) {
        throw "Solution not found under '$repoRootGuess': looking for 'Blasphemous.Multiplayer.sln'. Without the solution the repo root cannot be resolved, so the deploy does not start (a build of unknown origin is not copied into the game)."
    }
    $Repo = $sln.DirectoryName
}
if (-not (Test-Path (Join-Path $Repo 'Blasphemous.Multiplayer.sln'))) {
    throw "Invalid repo root: '$Repo' does not contain Blasphemous.Multiplayer.sln (passed manually via -Repo?)."
}

# The Harmony gate is checked BEFORE copying: if the script is missing (renamed, moved, partial
# checkout), the deploy stops here without touching the game folder. An optional gate is not a gate.
$verifier = Join-Path $PSScriptRoot 'verify-patch-targets.ps1'
if (-not (Test-Path $verifier)) {
    throw "Harmony gate not found: $verifier - verification was NOT performed, so the deploy does not start (a build that nobody checked is not copied)."
}

# 1. mod resources, in the same shape the build used to copy them (resources\** -> Modding\**).
#    Common.dll is resolved at runtime by ModdingAPI from Modding\data (Main.LoadMissingAssemblies)
#    and carries the protocol version and NetworkType. An old Common.dll next to a new BlasClient.dll
#    is the silent divergence this script exists to prevent.
$resSrc  = Join-Path $Repo 'resources'
$modRoot = Join-Path $Game 'Modding'
if (-not (Test-Path $resSrc)) { throw "Resources not found: $resSrc" }
Get-ChildItem $resSrc -Recurse -File | ForEach-Object {
    $rel  = $_.FullName.Substring($resSrc.Length + 1)
    $dest = Join-Path $modRoot $rel
    New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
    Copy-Item $_.FullName $dest -Force
}

# 2. mod (game loads it from Modding\plugins) and its Common.dll, taken from the SAME build:
#    bin\Debug, not resources\ (updated only on a successful build). These are the last writes on
#    purpose: if the two Common.dll copies diverge, the one paired with the DLL being deployed wins.
$modSrc = Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\BlasClient.dll'
$modDst = Join-Path $Game 'Modding\plugins\BlasClient.dll'
if (-not (Test-Path $modSrc)) { throw "Mod not built: $modSrc" }
New-Item -ItemType Directory -Path (Split-Path $modDst) -Force | Out-Null
Copy-Item $modSrc $modDst -Force

$commonSrc = Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\Blasphemous.Multiplayer.Common.dll'
$commonDst = Join-Path $Game 'Modding\data\Blasphemous.Multiplayer.Common.dll'
if (-not (Test-Path $commonSrc)) { throw "Common.dll not built: $commonSrc" }
New-Item -ItemType Directory -Path (Split-Path $commonDst) -Force | Out-Null
Copy-Item $commonSrc $commonDst -Force

# 3. server (dedicated folder inside the game).
#
# Taken from bin\Debug\net8.0, not from publish\BlasServer: bin\Debug\net8.0 is written by the
# compiler, so if the build succeeded it holds the server of THAT build. publish\ is produced by a
# post-build target that only runs when the server is rebuilt, so an incremental build (client
# touched, server not) would leave it stale and the deploy would silently copy an old server.
$srvSrc = Join-Path $Repo 'Blasphemous.Multiplayer.Server\bin\Debug\net8.0'
$srvDst = Join-Path $Game 'SERVER-COOP'
if (-not (Test-Path $srvSrc)) {
    throw "Server not built: '$srvSrc' does not exist. Run the solution build first (tools\check.ps1 does build and deploy together)."
}

# Files required for SERVER-COOP to start: executable, its dll, the two json files .NET reads at
# launch, the pdb (for stack traces), and the three dependencies the server does not bundle
# (Basalt.CommandParser, SimpleTCP, Common). If any is missing the server starts and dies; better
# to stop here with a clear name than to discover it in-game.
# If the server gains a new dependency, add it here: this is what the deploy guarantees to have
# put into SERVER-COOP.
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
    throw ("In '$srvSrc' $($srvMissing.Count) files used by SERVER-COOP are missing: " + ($srvMissing -join ', ') + ". Without any one of them the server does not start, so the deploy copies nothing.")
}

New-Item -ItemType Directory -Path $srvDst -Force | Out-Null
# Copy files only (no subfolders) and skip .log: SERVER-COOP's own log is a live trace and must
# not be overwritten by whatever happens to be in bin\Debug.
Get-ChildItem $srvSrc -File |
    Where-Object { $_.Extension -ne '.log' } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $srvDst $_.Name) -Force }

"=== deploy complete ==="
"mod    -> $modDst"
"  hash : " + (Get-FileHash $modDst -Algorithm SHA256).Hash.ToLower()
"common -> $commonDst"
"  hash : " + (Get-FileHash $commonDst -Algorithm SHA256).Hash.ToLower()
"server -> $srvDst"
"  hash : " + (Get-FileHash (Join-Path $srvDst 'blas1mpserver.dll') -Algorithm SHA256).Hash.ToLower()

# 4. Harmony patch targets.
#
# A [HarmonyPatch(typeof(X), "name")] carries its target as a STRING: if that method does not
# exist, the build passes and the failure only appears at runtime as a HarmonyException inside
# PatchAll, which ABORTS every subsequent patch. The symptom does not look like the cause: the
# mod stops writing any log lines and the per-frame update throws a NullReferenceException that
# has nothing to do with the bad patch. The deploy stops here if a target is missing - the last
# checkpoint before testing.
""
"=== Harmony patch targets ==="
& pwsh -NoProfile -File $verifier -ModDll $modDst
if ($LASTEXITCODE -ne 0) {
    # Two possible causes: broken targets, or the verifier failed to compile its probe. Both
    # mean the test would measure nothing.
    throw "Deploy was copied, but the Harmony gate FAILED (exit $LASTEXITCODE): see the message above. If it says 'could not compile the probe', the problem is NOT the patches: verification was not performed. Either way, a test now would measure nothing."
}