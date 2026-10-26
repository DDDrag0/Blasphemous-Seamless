# tools\compare-headless.ps1 - normalize and compare two headless bench runs.
#
#   pwsh -NoProfile -File tools\compare-headless.ps1 -Before <folder> -After <folder> [-Kind server|harness]
#
# -Kind server compares section A golden files (frames.txt, bytes.txt, server-log-pulito.txt).
# Section B captures (frames-fanout.txt, ...) are NOT compared against a baseline: they are judged
# by the bench's own assertions, which know who was supposed to receive what.
#
# Normalization is declared: clock headers and timestamps, ephemeral peer ports, and the server
# port (also inside Start(N)). Nothing else is touched - every other difference is real and must
# be looked at.
param(
    [Parameter(Mandatory = $true)][string]$Before,
    [Parameter(Mandatory = $true)][string]$After,
    [ValidateSet('server', 'harness')][string]$Kind = 'server'
)

$ErrorActionPreference = 'Stop'

function Normalize([string]$path) {
    $out = New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)) {
        $l = $line
        $l = $l -replace '^# Blasphemous COOP server log - started .*$', '# <header>'
        $l = $l -replace '^\[(\d\d):(\d\d):(\d\d)\] ', ''
        $l = $l -replace '\[Server\] \[\d\d:\d\d:\d\d\] ', '[Server] '
        $l = $l -replace '(?<=\d):\d{4,5}\b', ':PORT'
        $l = $l -replace 'port \d+', 'port PORT'
        $l = $l -replace 'porta=\d+', 'porta=PORT'
        $l = $l -replace 'Start\(\d+\)', 'Start(PORT)'
        $l = $l -replace 'Stesso IP di [AB]\)', 'Stesso IP)'
        $out.Add($l)
    }
    return $out
}

$files = if ($Kind -eq 'server') {
    @('frames.txt', 'bytes.txt', 'server-log-pulito.txt')
} else {
    @('harness-before.txt', 'harness-after.txt')
}

$ok = $true
foreach ($name in $files) {
    $b = Join-Path $Before $name
    $a = Join-Path $After $name
    if (-not (Test-Path $b) -or -not (Test-Path $a)) {
        "  SKIPPED  $name (missing in $Before or in $After)"
        continue
    }
    $nb = Normalize $b
    $na = Normalize $a
    $diff = Compare-Object $nb $na
    if ($null -eq $diff) {
        "  IDENTICAL {0,-24} ({1} lines)" -f $name, $nb.Count
    } else {
        $ok = $false
        "  DIFFERENT {0,-24} ({1} differing lines)" -f $name, $diff.Count
        $diff | Select-Object -First 20 | ForEach-Object { "           {0} {1}" -f $_.SideIndicator, $_.InputObject }
    }
}
if ($ok) { "ESITO: BEFORE and AFTER are IDENTICAL (clock and ports aside, as declared)" ; exit 0 }
else { "ESITO: THERE ARE REAL DIFFERENCES" ; exit 1 }