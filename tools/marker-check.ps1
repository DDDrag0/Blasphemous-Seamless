# Content-based verification of markers inside a .NET DLL (no hash check).
# .NET string literals live in the #US heap as UTF-16LE, identifiers in #Strings as UTF-8; both
# encodings are searched on the raw bytes, so "found" means the literal is really in the DLL.
#
# A relative -Dll is resolved, in order, against: current directory, repo root ($PSScriptRoot\..),
# game folder. An absolute -Dll is used as-is.
#
# Usage:
#   & '.\tools\marker-check.ps1' -Dll 'Modding\plugins\BlasClient.dll' -Marker '[Pausa]','[Map]'
#   & '.\tools\marker-check.ps1' -Dll '...\bin\Debug\BlasClient.dll'   -Marker '[Pausa]'
# With -File (new process) -Marker only takes one value; the array passes only via the & operator.
param(
  [Parameter(Mandatory=$true)][string]$Dll,
  [Parameter(Mandatory=$true)][string[]]$Marker
)

$ErrorActionPreference = 'Stop'

# tools\ is one level below the repo root; the game folder is the default used by deploy-local.ps1.
$RepoRoot = Split-Path -Parent $PSScriptRoot
$GameRoot = 'D:\SteamLibrary\steamapps\common\Blasphemous'
if (-not [System.IO.Path]::IsPathRooted($Dll)) {
  foreach ($base in @((Get-Location).ProviderPath, $RepoRoot, $GameRoot)) {
    $candidate = Join-Path $base $Dll
    if (Test-Path -LiteralPath $candidate) { $Dll = $candidate; break }
  }
}
if (-not (Test-Path -LiteralPath $Dll)) {
  throw "DLL not found: '$Dll' (also looked in '$RepoRoot' and '$GameRoot')"
}
$Dll = (Resolve-Path -LiteralPath $Dll).ProviderPath

$bytes = [System.IO.File]::ReadAllBytes($Dll)

function Find-Bytes {
  param([byte[]]$Hay, [byte[]]$Needle)
  $hits = @()
  if ($Needle.Length -eq 0 -or $Hay.Length -lt $Needle.Length) { return $hits }
  $last = $Hay.Length - $Needle.Length
  for ($i = 0; $i -le $last; $i++) {
    if ($Hay[$i] -ne $Needle[0]) { continue }
    $ok = $true
    for ($j = 1; $j -lt $Needle.Length; $j++) {
      if ($Hay[$i + $j] -ne $Needle[$j]) { $ok = $false; break }
    }
    if ($ok) { $hits += $i }
  }
  return $hits
}

"=== marker check: $Dll ==="
"    size   : $($bytes.Length) byte"
"    sha256 : " + (Get-FileHash $Dll -Algorithm SHA256).Hash.ToLower()
"    mtime  : " + (Get-Item $Dll).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
""

$allOk = $true
foreach ($m in $Marker) {
  $u16 = [System.Text.Encoding]::Unicode.GetBytes($m)
  $u8  = [System.Text.Encoding]::UTF8.GetBytes($m)
  $h16 = Find-Bytes -Hay $bytes -Needle $u16
  $h8  = Find-Bytes -Hay $bytes -Needle $u8
  $present = ($h16.Count -gt 0) -or ($h8.Count -gt 0)
  if (-not $present) { $allOk = $false }
  $where = @()
  if ($h16.Count -gt 0) { $where += "UTF-16LE @ $($h16 -join ',')" }
  if ($h8.Count  -gt 0) { $where += "UTF-8 @ $($h8 -join ',')" }
  $tag = if ($present) { 'PRESENTE' } else { 'ASSENTE ' }
  "  [$tag] `"$m`"  $(if($present){$where -join ' | '}else{'(nessuna occorrenza)'})"
}
""
if ($allOk) { "ESITO: TUTTI i marker richiesti sono PRESENTI"; exit 0 }
else        { "ESITO: ALMENO UN marker richiesto e' ASSENTE"; exit 1 }