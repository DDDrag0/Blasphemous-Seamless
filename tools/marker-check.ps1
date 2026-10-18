# Verifica di CONTENUTO (non di hash) dei marker dentro una DLL .NET.
# I letterali stringa .NET vivono nell'heap #US in UTF-16LE; i nomi in #Strings in UTF-8.
# Cerco quindi ENTRAMBE le codifiche sui byte grezzi: se il marker compare, il letterale
# e' nella DLL per davvero. Nessuna fiducia negli hash, nessuna fiducia in verify-deploy.ps1.
#
# ⚠️ PERCORSI — questo file ora vive in `tools\`, VERSIONATO, un livello SOTTO la radice del
# repo. Prima stava in `scratch\` (gitignored), che e' un livello SOPRA: la base dei percorsi
# relativi era un'altra, e il check non era un gate — era una cosa che qualcuno si ricordava
# di lanciare. Un `-Dll` RELATIVO viene risolto, in quest'ordine, su: cartella corrente,
# radice del repo, cartella di gioco. Un `-Dll` assoluto resta com'e'.
#
# Come si usa — le DUE DLL che contano (i parametri sono gli stessi di prima: -Dll e -Marker):
#   # 1) la DLL DEPLOYATA nel gioco (quella che il gioco esegue davvero)
#   & '.\tools\marker-check.ps1' -Dll 'Modding\plugins\BlasClient.dll' -Marker '[Pausa]','[Map]'
#   # 2) la DLL di bin\Debug (quella che deploy-local.ps1 copia), per confrontare gli sha256
#   & '.\tools\marker-check.ps1' -Dll 'Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main\Blasphemous.Multiplayer.Client\bin\Debug\BlasClient.dll' -Marker '[Pausa]'
# ⚠️ Con `-File` (processo nuovo) `-Marker` prende UN SOLO valore per volta: l'array di marker
#    passa solo col call operator `&`, come qui sopra e come lo si e' sempre lanciato.
param(
  [Parameter(Mandatory=$true)][string]$Dll,
  [Parameter(Mandatory=$true)][string[]]$Marker
)

$ErrorActionPreference = 'Stop'

# --- CORREZIONE DEI PERCORSI (l'unica differenza rispetto alla copia in scratch\) --------
# `tools\` sta un livello sotto la radice del repo: `$PSScriptRoot\..` e' la radice. La
# cartella di gioco e' quella che deploy-local.ps1 usa di default (una sola, scritta a mano).
$RepoRoot = Split-Path -Parent $PSScriptRoot
$GameRoot = 'D:\SteamLibrary\steamapps\common\Blasphemous'
if (-not [System.IO.Path]::IsPathRooted($Dll)) {
  foreach ($base in @((Get-Location).ProviderPath, $RepoRoot, $GameRoot)) {
    $candidate = Join-Path $base $Dll
    if (Test-Path -LiteralPath $candidate) { $Dll = $candidate; break }
  }
}
if (-not (Test-Path -LiteralPath $Dll)) {
  throw "DLL non trovata: '$Dll' (cercata anche in '$RepoRoot' e in '$GameRoot')"
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
