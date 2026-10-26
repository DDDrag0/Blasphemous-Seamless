# Harmony patch target verification.
#
# A [HarmonyPatch(typeof(X), "name")] carries the target method name as a STRING: if it does not
# exist, the build passes and the failure appears only at runtime as a HarmonyException inside
# PatchAll, which aborts every subsequent patch. Symptom is misleading: the mod stops logging and
# the per-frame update throws a NullReferenceException unrelated to the bad patch.
#
# Two valid forms:
#   normal: [HarmonyPatch(typeof(X), "name")]                  (one attribute, two args)
#   split : [HarmonyPatch(typeof(X))] + [HarmonyPatch("name")] (two attributes, must be adjacent)
#
# Split-form rule: the type and name pair only if nothing but [Harmony*] attributes lie between
# them. A split form left without its counterpart is an error and is reported.
#
# Exit codes (each paired with a machine marker on the probe's last line):
#   0 TUTTI-VALIDI        probe measured: all targets exist
#   2 BERSAGLI-INVALIDI   probe measured: broken or incomplete patches
#   3 ZERO-BERSAGLI       probe found no [HarmonyPatch] attribute: nothing measured
#   4 ERRORE              probe stopped halfway (unreadable DLL, internal exception)
#   any other / no marker = crash: cannot verify (never "patches are wrong")
#
# The probe lives at tools\verify-patch-targets\ (Program.cs + probe.csproj). It is copied to a
# per-process temp folder and built there, so the repo stays free of bin/obj and two parallel
# runs do not overwrite each other's Program.cs.
#
# Usage:
#   .\verify-patch-targets.ps1
#   .\verify-patch-targets.ps1 -ModDll <path> -GameDll <path>
#   .\verify-patch-targets.ps1 -ProbeSource <folder with Program.cs + probe.csproj>
#   .\verify-patch-targets.ps1 -ProbeDir <working folder for the probe>

[CmdletBinding()]
param(
    [string] $ModDll  = 'D:\SteamLibrary\steamapps\common\Blasphemous\Modding\plugins\BlasClient.dll',
    [string] $GameDll = 'D:\SteamLibrary\steamapps\common\Blasphemous\Blasphemous_Data\Managed\Assembly-CSharp.dll',
    [string] $ProbeSource = '',
    [string] $ProbeDir    = ''
)

$ErrorActionPreference = 'Stop'

if (-not $ProbeSource) { $ProbeSource = Join-Path $PSScriptRoot 'verify-patch-targets' }
# Per-process working folder ($PID): two parallel verifiers would otherwise write the same
# Program.cs and one would test the other's code.
if (-not $ProbeDir)    { $ProbeDir    = Join-Path ([System.IO.Path]::GetTempPath()) ("blasphemous-coop-verify-patch-targets\$PID") }

$probeCsproj  = Join-Path $ProbeSource 'probe.csproj'
$probeProgram = Join-Path $ProbeSource 'Program.cs'
$probeDll     = Join-Path $ProbeDir 'bin\Debug\net8.0\probe.dll'

if (-not (Test-Path $ModDll))  { Write-Host "  mod not found: $ModDll" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $GameDll)) { Write-Host "  game not found: $GameDll" -ForegroundColor Red; exit 1 }

# The probe is versioned in the repo; if it is missing, the gate cannot run.
foreach ($f in @($probeCsproj, $probeProgram)) {
    if (-not (Test-Path $f)) {
        Write-Host ""
        Write-Host "  COULD NOT VERIFY: probe not in repo, missing $f" -ForegroundColor Red
        Write-Host "  Probe folder: $ProbeSource" -ForegroundColor Red
        Write-Host "  No Harmony target was checked: this exit 1 does NOT mean" -ForegroundColor Yellow
        Write-Host "  'some targets do not exist', it means 'the gate could not run'." -ForegroundColor Yellow
        exit 1
    }
}

# The working folder is wiped before use: it must not overlap the probe source, or the script
# would delete what it is about to compile.
$dirFull = [System.IO.Path]::GetFullPath($ProbeDir).TrimEnd('\', '/')
$srcFull = [System.IO.Path]::GetFullPath($ProbeSource).TrimEnd('\', '/')
if ($srcFull -eq $dirFull -or $srcFull.StartsWith($dirFull + '\') -or $dirFull.StartsWith($srcFull + '\')) {
    Write-Host ""
    Write-Host "  COULD NOT VERIFY: -ProbeDir and -ProbeSource overlap." -ForegroundColor Red
    Write-Host "  ProbeSource : $srcFull" -ForegroundColor Red
    Write-Host "  ProbeDir    : $dirFull" -ForegroundColor Red
    Write-Host "  The working folder is wiped: pick a separate path." -ForegroundColor Yellow
    exit 1
}

# $code starts at 1: any path that does not reach a measurement leaves the gate red.
$code     = 1
$esito    = 'nonmisurato'
$output   = @()
$testo    = @()
$visibile = @()
$buildOut = @()
$dettaglio = ''
$marca    = ''
$conta    = ''
$c        = @{}

# Outcome protocol: marker <-> code. The probe prints its marker as the last line; the code is
# accepted only when it matches. Anything else is a crash, not an outcome.
$codiciEsito = @{
    'TUTTI-VALIDI'      = 0
    'BERSAGLI-INVALIDI' = 2
    'ZERO-BERSAGLI'     = 3
    'ERRORE'            = 4
}
$esitiPerMarca = @{
    'TUTTI-VALIDI'      = 'ok'
    'BERSAGLI-INVALIDI' = 'bersagli'
    'ZERO-BERSAGLI'     = 'zerobersagli'
    'ERRORE'            = 'sondaerrore'
}

# Working folder wiped before copying: Copy-Item preserves file times and incremental dotnet
# build would skip recompiling if the leftover probe.dll is newer than the just-copied source.
# Without this, a failed run left a stale probe.dll that produced a green "273/273 valid".
if (Test-Path $ProbeDir) { Remove-Item $ProbeDir -Recurse -Force }
New-Item -ItemType Directory -Path $ProbeDir -Force | Out-Null
Copy-Item $probeCsproj  $ProbeDir -Force
Copy-Item $probeProgram $ProbeDir -Force

Push-Location $ProbeDir
try {
    $buildOut = & dotnet build 'probe.csproj' --nologo -v q -nodeReuse:false 2>&1
    $buildCode = $LASTEXITCODE
    if ($null -eq $buildCode) { $buildCode = 1 }

    if ($buildCode -ne 0) {
        $esito = 'buildko'
    }
    elseif (-not (Test-Path $probeDll)) {
        $esito = 'noprobe'
    }
    else {
        # Probe output is always shown: on broken targets the TIPO ASSENTE / METODO ASSENTE
        # lines are the diagnosis.
        $output = & dotnet $probeDll $ModDll $GameDll 2>&1
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 1 }

        # Read the protocol: `SONDA-CONTA: k=v ...` (counts) and `SONDA-ESITO: <outcome>` (the
        # declaration). If the declaration is missing or does not match the code, treat as crash.
        $testo = @($output | ForEach-Object { [string]$_ })
        foreach ($riga in $testo) {
            if     ($riga -match '^\s*SONDA-ESITO:\s*(\S+)\s*$') { $marca = $Matches[1] }
            elseif ($riga -match '^\s*SONDA-CONTA:\s*(.+?)\s*$') { $conta = $Matches[1] }
        }
        if ($conta) {
            foreach ($pezzo in ($conta -split '\s+')) {
                if ($pezzo -match '^(\w+)=(-?\d+)$') { $c[$Matches[1]] = [int]$Matches[2] }
            }
        }

        if ($marca -and $codiciEsito.ContainsKey($marca) -and $codiciEsito[$marca] -eq $code) {
            $esito = $esitiPerMarca[$marca]
            if ($esito -eq 'sondaerrore') {
                # The detail is the line the probe wrote before stopping: which exception, on
                # which file.
                $rigaErrore = $testo | Where-Object { $_ -match 'SONDA FALLITA:' } | Select-Object -First 1
                $dettaglio = if ($rigaErrore) { ($rigaErrore -replace '^\s*SONDA FALLITA:\s*', '').Trim() }
                             else { "probe declared an internal error (exit $code) without saying which" }
            }
        }
        else {
            $esito = 'sondacrash'
            if ($marca) {
                $dettaglio = "exit $code contradicts marker '$marca', which does not map to that code"
            } else {
                $dettaglio = "exit $code, and the 'SONDA-ESITO: ...' line is missing"
            }
        }

        # Machine lines are not diagnosis: drop them from what is displayed.
        $visibile = @($testo | Where-Object { $_ -notmatch '^\s*SONDA-(ESITO|CONTA):' })
    }
}
catch {
    # dotnet missing or failed to start.
    $esito = 'nondotnet'
    $dettaglio = $_.Exception.Message
    $code = 1
}
finally {
    Pop-Location
}

if ($esito -eq 'ok') {
    $visibile | ForEach-Object { "  $_" }
    # Success: wipe the working folder so $env:TEMP does not accumulate builds. On failure the
    # folder is kept - it holds what is needed to understand the failure.
    Remove-Item $ProbeDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 0
}

if ($esito -eq 'nondotnet') {
    Write-Host ""
    Write-Host "  COULD NOT COMPILE THE PROBE: 'dotnet' is unavailable or failed to start." -ForegroundColor Red
    Write-Host "  Detail: $dettaglio" -ForegroundColor Red
    Write-Host "  No Harmony target was checked. This exit 1 does NOT mean" -ForegroundColor Yellow
    Write-Host "  'some targets do not exist': it means 'the gate could not run'." -ForegroundColor Yellow
    exit 1
}

if ($esito -eq 'buildko') {
    Write-Host ""
    Write-Host "  COULD NOT COMPILE THE PROBE: probe BUILD failed (exit $buildCode)." -ForegroundColor Red
    Write-Host "  This is NOT a Harmony patch problem: verification was not performed." -ForegroundColor Red
    Write-Host "  Build output:" -ForegroundColor Red
    $buildOut | ForEach-Object { "  $_" }
    exit 1
}

if ($esito -eq 'noprobe') {
    Write-Host ""
    Write-Host "  COULD NOT VERIFY: build succeeded but $probeDll is missing." -ForegroundColor Red
    Write-Host "  If you renamed AssemblyName in probe.csproj, this script still looks there." -ForegroundColor Yellow
    exit 1
}

if ($esito -eq 'zerobersagli') {
    $visibile | ForEach-Object { "  $_" }
    Write-Host ""
    Write-Host "  NOTHING MEASURED: the probe found no [HarmonyPatch] attribute at all." -ForegroundColor Red
    Write-Host "  The mod has 273: a zero count means the DLL read is the wrong one ($ModDll)." -ForegroundColor Red
    Write-Host "  This exit 1 does NOT mean 'some targets do not exist': nothing was measured." -ForegroundColor Yellow
    exit 1
}

# The probe failed or crashed: cannot verify.
if ($esito -eq 'sondaerrore' -or $esito -eq 'sondacrash') {
    Write-Host ""
    if ($esito -eq 'sondaerrore') {
        Write-Host "  COULD NOT VERIFY: probe failed ($dettaglio)." -ForegroundColor Red
    } else {
        Write-Host "  COULD NOT VERIFY: probe did not declare an outcome ($dettaglio)." -ForegroundColor Red
        Write-Host "  A crash (unhandled exception, unreadable DLL, killed process) exits with a code" -ForegroundColor Red
        Write-Host "  that is NOT an outcome - sometimes 1, the same the probe uses for 'broken targets'." -ForegroundColor Red
        Write-Host "  The code alone does not decide: the marker is what counts." -ForegroundColor Red
    }
    # Whatever the probe printed before stopping is shown, but clarified as not a list of targets:
    # the measurement did not complete.
    $altre = @($visibile | Where-Object { $_ -notmatch 'SONDA FALLITA:' })
    if ($altre.Count -gt 0) {
        Write-Host ""
        Write-Host "  Probe output (crash trace, NOT a target list: verification did not complete):" -ForegroundColor Yellow
        $altre | ForEach-Object { "  $_" }
    }
    Write-Host ""
    Write-Host "  This exit 1 does NOT mean 'a name in [HarmonyPatch] is wrong': it means the gate" -ForegroundColor Yellow
    Write-Host "  could not measure. The fault is in the probe (unreadable DLL, exception), not in" -ForegroundColor Yellow
    Write-Host "  the patches - which remain unverified." -ForegroundColor Yellow
    exit 1
}

# The probe measured and found broken targets - the only path where "a name in [HarmonyPatch] is
# wrong" is true.
$nRotti = $null; $nInc = 0; $nTot = 0
if ($c.ContainsKey('tipoassente') -and $c.ContainsKey('metodoassente')) {
    $nRotti = $c['tipoassente'] + $c['metodoassente']
}
if ($c.ContainsKey('incompleta')) { $nInc = $c['incompleta'] }
if ($c.ContainsKey('totale'))     { $nTot = $c['totale'] }

Write-Host ""
if ($null -ne $nRotti -and $nRotti -eq 1) {
    Write-Host "  1 target does NOT exist, out of $nTot checked:" -ForegroundColor Red
}
elseif ($null -ne $nRotti -and $nRotti -gt 1) {
    Write-Host "  $nRotti targets do NOT exist, out of $nTot checked:" -ForegroundColor Red
}
if ($nInc -eq 1) {
    Write-Host "  1 INCOMPLETE patch (a broken split form is not applied at runtime):" -ForegroundColor Red
}
elseif ($nInc -gt 1) {
    Write-Host "  $nInc INCOMPLETE patches (a broken split form is not applied at runtime):" -ForegroundColor Red
}
if ($null -eq $nRotti) {
    Write-Host "  Probe declared BERSAGLI-INVALIDI but did not declare the counts:" -ForegroundColor Red
}
Write-Host ""
$visibile | ForEach-Object { "  $_" }
Write-Host ""
Write-Host "  A wrong name in [HarmonyPatch] compiles and fails at runtime; the aborting PatchAll" -ForegroundColor Yellow
Write-Host "  prevents every subsequent patch from being applied." -ForegroundColor Yellow
exit 1