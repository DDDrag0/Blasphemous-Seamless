# tools\check.ps1 - one-command verification: build -> deploy -> marker -> summary.
# Stops at the first failing step with exit 1, so "green" means every step was actually run.
#
# Usage:
#   pwsh -NoProfile -File .\tools\check.ps1
#   pwsh -NoProfile -File .\tools\check.ps1 -Game 'D:\...\Blasphemous'
#   &     .\tools\check.ps1 -Marker '[Pausa]','[NonEsiste]'   # to test the gate
#         (a -Marker list is only accepted via the & call operator; with -File it collapses to one string)
#   exit 0 = green; exit 1 = stopped at the first failing step.

param(
    [string]$Game = 'D:\SteamLibrary\steamapps\common\Blasphemous',

    # Warning baseline. A new warning is not a failure, but a number without a base says nothing:
    # the summary flags a delta explicitly so it gets looked at.
    [int]$WarningBaseline = 469,

    # Project markers. Each entry is a literal in the code and is checked byte-by-byte (UTF-16LE)
    # in the deployed DLL. Passing a different list (via &, not -File) is the way to test the gate.
    [string[]]$Marker = @(
        '[Pausa]'
        '[Tempo]'
        '[EnemyInert]'
        '[Server] payload corto'
        '[Host]'
        '[Host] ascolto su='
        '[FxRelay]'
        '[ColpaVita]'
        '[BossLife]'
        '[BossCorpse]'
        '[DannoVita]'
        '[Impronta]'
        '[Net] skin:'
        'YOUR TEAMMATE IS PAUSED'
    )
)

$ErrorActionPreference = 'Stop'

# Disable native-command error-as-exception: the script reads $LASTEXITCODE itself, step by step,
# and must be able to report what failed.
$PSNativeCommandUseErrorActionPreference = $false

function Stop-Check {
    param([string]$Messaggio)
    ""
    "=== STOPPED ==="
    $Messaggio
    exit 1
}

function Read-BuildCount {
    # MSBuild summary is localized ("Warning(s)" / "Avvisi:"). Both forms are read: the run language
    # is forced (VSLANG), but a different locale must not silently make this read 0.
    param([string[]]$Righe, [string]$Inglese, [string]$Italiano)
    $valore = $null
    foreach ($r in $Righe) {
        if ($r -match ('^\s*(\d+)\s+' + $Inglese + '\s*$')) { $valore = [int]$Matches[1] }
        elseif ($r -match ('^\s*' + $Italiano + ':\s*(\d+)\s*$')) { $valore = [int]$Matches[1] }
    }
    return $valore
}

# --- REPO ROOT: found, not written (see header) --------------------------------------------------
$RepoRootGuess = Split-Path -Parent $PSScriptRoot
$sln = Get-ChildItem -LiteralPath $RepoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -ErrorAction SilentlyContinue |
       Select-Object -First 1
if (-not $sln) {
    $sln = Get-ChildItem -LiteralPath $RepoRootGuess -Filter 'Blasphemous.Multiplayer.sln' -File -Recurse -ErrorAction SilentlyContinue |
           Select-Object -First 1
}
if (-not $sln) {
    Stop-Check "Solution not found under '$RepoRootGuess': looked for 'Blasphemous.Multiplayer.sln'. Without the solution there is nothing to compile, and without the build there is nothing to verify."
}
$SlnPath = $sln.FullName
$Repo    = $sln.DirectoryName

$DeployScript = Join-Path $PSScriptRoot 'deploy-local.ps1'
$MarkerScript = Join-Path $PSScriptRoot 'marker-check.ps1'
foreach ($f in @($DeployScript, $MarkerScript)) {
    if (-not (Test-Path $f)) {
        Stop-Check "Missing '$f': one of the two .ps1 scripts was renamed or moved."
    }
}
if (-not (Test-Path $Game)) {
    Stop-Check "Game folder not found: '$Game'. Pass the real folder with -Game (this script does not guess it)."
}

# Array trap (verified): with `pwsh -NoProfile -File .\tools\check.ps1 -Marker 'a','b'` the list
# arrives as ONE string ("'a','b'"), which does not exist in any DLL, and the gate would fail for
# the wrong reason. Same applies to marker-check.ps1. So if a single "marker" contains a comma, we
# say so.
if ($Marker.Count -eq 1 -and $Marker[0] -ne '' -and $Marker[0].Contains(',')) {
    Stop-Check "The -Marker value arrived as ONE string: '$($Marker[0])'. With -File the array does not pass (this is how PowerShell builds the command line, not a bug of this script). To test the gate use the call operator:  & .\tools\check.ps1 -Marker '[Pausa]','[NonEsiste]'  - or a single marker at a time with -File."
}

"=== check: repo $Repo ==="
"=== check: game $Game ==="

# --- 1/3 BUILD ------------------------------------------------------------------------------------
""
"=== 1/3 solution build ==="
# --no-incremental: an incremental build of a compiled solution reports "0 Warning(s)" - the baseline
# comparison would be noise and a NEW warning would be invisible (the manual-run defect: "0 warnings"
# looked like an improvement). Cost: a few seconds. Benefit: the number is always the real one.
$env:VSLANG = '1033'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$buildArgs = @('build', $SlnPath, '--nologo', '-v', 'q', '-nodeReuse:false', '-clp:ErrorsOnly', '--no-incremental')
$eapPrima = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$buildOut = @(& dotnet @buildArgs 2>&1 | ForEach-Object { "$_" })
$buildCode = $LASTEXITCODE
$ErrorActionPreference = $eapPrima

foreach ($riga in $buildOut) { if ($riga.Trim() -ne '') { "    $riga" } }

$avvisi = Read-BuildCount -Righe $buildOut -Inglese 'Warning\(s\)' -Italiano 'Avvisi'
$errori = Read-BuildCount -Righe $buildOut -Inglese 'Error\(s\)'   -Italiano 'Errori'

if ($buildCode -ne 0 -or ($null -ne $errori -and $errori -gt 0)) {
    $quanti = if ($null -ne $errori) { "$errori errors" } else { "exit $buildCode" }
    Stop-Check "BUILD FAILED ($quanti). Errors are above (only errors were requested: -clp:ErrorsOnly). Deploy was NOT executed: a non-compiling build is never copied into the game."
}
if ($null -eq $errori) {
    "    (no 'Error(s)'/'Errori:' line in the MSBuild summary: relying on exit code, which is 0)"
}

if ($null -eq $avvisi) {
    $rigaAvvisi = "warnings: could not read the count from the MSBuild summary (see output above)"
} elseif ($avvisi -eq $WarningBaseline) {
    $rigaAvvisi = "warnings: $avvisi (baseline)"
} elseif ($avvisi -gt $WarningBaseline) {
    $delta = $avvisi - $WarningBaseline
    $rigaAvvisi = "warnings: $avvisi (+$delta : look)"
} else {
    $delta = $WarningBaseline - $avvisi
    $rigaAvvisi = "warnings: $avvisi (-$delta below baseline: a warning disappeared - if unintended, look)"
}
$rigaAvvisi

# --- 2/3 DEPLOY -----------------------------------------------------------------------------------
""
"=== 2/3 deploy to game folder ==="
# -NoProfile as deploy-local.ps1 does with its verifier. Deploy output is passed through (the
# Harmony gate runs visibly); the exit code is what matters here.
& pwsh -NoProfile -File $DeployScript -Game $Game
if ($LASTEXITCODE -ne 0) {
    Stop-Check "DEPLOY FAILED (exit $LASTEXITCODE): see the message above - that is what did not pass (Harmony gate, missing files, game folder). Marker check was NOT executed: the DLL in the game is not the one the gate was meant to check, so checking it would measure nothing."
}

# --- 3/3 MARKER ON DEPLOYED DLL -------------------------------------------------------------------
""
"=== 3/3 markers on DEPLOYED DLL ==="
$DllDeployata = Join-Path $Game 'Modding\plugins\BlasClient.dll'
if (-not (Test-Path $DllDeployata)) {
    Stop-Check "Deployed DLL not found: '$DllDeployata'. Deploy exited 0 but the file is missing: nothing to check byte-by-byte."
}

# marker-check accepts a LIST - only when invoked with & (an array passed with -File collapses into
# one string, and marker-check would search for that). A single call, in-process, with the whole list.
# Note (measured): an `exit N` in a script called via & does NOT exit the caller - its exit is in
# $LASTEXITCODE. The outcome is read from the "ESITO:" line that marker-check is required to print.
$markerOut = @()
try {
    $markerOut = @(& $MarkerScript -Dll $DllDeployata -Marker $Marker 2>&1 | ForEach-Object { "$_" })
    $markerCode = $LASTEXITCODE
} catch {
    Stop-Check "marker-check failed: $($_.Exception.Message). This is not 'a missing marker': the byte check was not executed."
}
$markerOut | Where-Object { $_ -match '^\s*\[(PRESENTE|ASSENTE)' }

$verdetto = @($markerOut | Where-Object { $_ -match '^\s*ESITO:' }) | Select-Object -First 1
$assenti = @($markerOut |
    Where-Object { $_ -match '^\s*\[ASSENTE' } |
    ForEach-Object { if ($_ -match '\[ASSENTE\s*\]\s*"([^"]*)"') { $Matches[1] } else { '(unparsable line)' } })

if (-not $verdetto) {
    Stop-Check "marker-check did not print an ESITO line (exit $markerCode): the byte check did not reach a verdict, so there is nothing to trust."
}
if ($verdetto -notmatch 'TUTTI i marker richiesti sono PRESENTI') {
    Stop-Check ("MARKERS MISSING in deployed DLL: " + $(if ($assenti.Count -gt 0) { $assenti -join ', ' } else { 'see verdict above' }) + " - $($assenti.Count) of $($Marker.Count). The DLL the game executes does not contain that code: either it is an old build or the marker is written differently in code. If the list is wrong (marker renamed in code), update it HERE and in tools\marker-check.ps1: this is not a false alarm to ignore.")
}
"  RESULT: $($Marker.Count)/$($Marker.Count) markers present"

# --- 4/4 SUMMARY AND HASH COHERENCE ---------------------------------------------------------------
# The three artifacts the game uses (mod, Common, server) must match bin\Debug: otherwise the deploy
# copied something else, or something rewrote the game folder - and everything above describes a
# build the game does not execute.
$coppie = @(
    [pscustomobject]@{ Nome = 'mod';    Repo = (Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\BlasClient.dll');                            Gioco = $DllDeployata }
    [pscustomobject]@{ Nome = 'common'; Repo = (Join-Path $Repo 'Blasphemous.Multiplayer.Client\bin\Debug\Blasphemous.Multiplayer.Common.dll');       Gioco = (Join-Path $Game 'Modding\data\Blasphemous.Multiplayer.Common.dll') }
    [pscustomobject]@{ Nome = 'server'; Repo = (Join-Path $Repo 'Blasphemous.Multiplayer.Server\bin\Debug\net8.0\blas1mpserver.dll');                 Gioco = (Join-Path $Game 'SERVER-COOP\blas1mpserver.dll') }
)

$diverse = @()
$hashMod = $null
$righeCoerenza = @()
foreach ($c in $coppie) {
    if (-not (Test-Path $c.Repo))  { $diverse += "$($c.Nome): missing in build ($($c.Repo))";    continue }
    if (-not (Test-Path $c.Gioco)) { $diverse += "$($c.Nome): missing in game ($($c.Gioco))";    continue }
    $hRepo  = (Get-FileHash $c.Repo  -Algorithm SHA256).Hash.ToLower()
    $hGioco = (Get-FileHash $c.Gioco -Algorithm SHA256).Hash.ToLower()
    $uguale = ($hRepo -eq $hGioco)
    if (-not $uguale) { $diverse += "$($c.Nome): build=$hRepo game=$hGioco" }
    if ($c.Nome -eq 'mod') { $hashMod = $hGioco }
    $righeCoerenza += ("  {0} {1,-7} build={2}... game={3}... {4}" -f '[hash]', $c.Nome, $hRepo.Substring(0, 12), $hGioco.Substring(0, 12), $(if ($uguale) { 'MATCH' } else { 'DIFFERENT' }))
}

""
"=== SUMMARY ==="
"  build  : OK, 0 errors · " + ($rigaAvvisi -replace '^warnings: ', '')
"  deploy : OK (includes the Harmony gate on patch targets)"
"  marker : $($Marker.Count)/$($Marker.Count) present on deployed DLL"
$righeCoerenza
if ($diverse.Count -gt 0) {
    Stop-Check ("COHERENCE: the game does NOT contain the repo build. " + ($diverse -join ' | ') + ". Everything above describes a build the game does not execute: either the deploy copied something else, or something rewrote the folder afterwards. Redo the deploy (and if it turns different again, see WHAT is rewriting it).")
}
"  deployed build is the repo one: sha256 $hashMod"
""
"=== ALL GREEN ==="
exit 0