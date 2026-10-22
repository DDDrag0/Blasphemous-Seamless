# ⭐ CONFRONTO PRIMA/DOPO DEI DUE RUN DEL BANCO, normalizzato in modo DICHIARATO.
#
#   pwsh -NoProfile -File tools\compare-headless.ps1 -Before <cartella> -After <cartella> [-Kind server|harness]
#
# Era `scratch\c1a-test\compare.ps1`: sta qui perche' una prova che vive fuori dalla repo non esiste
# per chi clona. L'ha accompagnata `tools\run-headless.ps1` (stesso giro).
#
# Cosa viene confrontato ( `-Kind server`, il caso normale ) — i file della SEZIONE A del banco, cioe'
# il golden dei 42 frame: `frames.txt`, `bytes.txt`, `server-log-pulito.txt`. Le catture della sezione
# B (`frames-fanout.txt`, …) NON si confrontano con un baseline: si GIUDICANO con le asserzioni del
# banco, che sanno chi doveva ricevere cosa.
#
# Cosa viene normalizzato, e perche' e' lecito:
#   · `[HH:mm:ss]` e `# ... started <data>`  -> orologio, non contenuto.
#   · `:\d{4,5}` (porta EFFIMERA del peer)  -> la porta la sceglie il sistema operativo a ogni
#     connessione; con `-Kind harness` si normalizza anche la porta scelta dal test.
#   · `port \d+` (la porta del server)      -> scelta dal test.
# ⚠️ NIENT'ALTRO viene toccato: ogni altra differenza e' una differenza VERA e va guardata.
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
        "  SALTATO  $name (manca in $Before o in $After)"
        continue
    }
    $nb = Normalize $b
    $na = Normalize $a
    $diff = Compare-Object $nb $na
    if ($null -eq $diff) {
        "  IDENTICO {0,-24} ({1} righe)" -f $name, $nb.Count
    } else {
        $ok = $false
        "  DIVERSE  {0,-24} ({1} righe di differenza)" -f $name, $diff.Count
        $diff | Select-Object -First 20 | ForEach-Object { "           {0} {1}" -f $_.SideIndicator, $_.InputObject }
    }
}
if ($ok) { "ESITO: PRIMA e DOPO sono IDENTICI (a meno di orologio e porte, dichiarati)" ; exit 0 }
else { "ESITO: CI SONO DIFFERENZE VERE" ; exit 1 }
