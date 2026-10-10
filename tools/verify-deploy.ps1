<#
.SYNOPSIS
    Verifica che il mod installato su QUESTA macchina sia quello giusto, PRIMA di testare.

.DESCRIPTION
    Esiste per un motivo preciso: un test e' stato buttato via perche' le due macchine
    eseguivano binari diversi. G1 aveva ancora la build precedente, e le sue righe di log non
    contenevano i marcatori nuovi. Il test sembrava non misurare nulla, mentre in realta' stava
    misurando il binario vecchio.

    Il controllo e' a TRE livelli, perche' nessuno dei primi due basta da solo:
      1. HASH      - dice se il file e' quello atteso;
      2. MARCATORI - dice se il file CONTIENE davvero le funzionalita' attese;
      3. PROTOCOLLO - dice se mod e server parlano la stessa lingua.

.PARAMETER GameFolder
    Cartella di installazione del gioco. Se omesso, usa il percorso Steam noto.

.PARAMETER ExpectedHash
    Hash SHA256 atteso della mod. Se fornito, il confronto e' vincolante.

.PARAMETER SkipMarkers
    Salta il controllo dei marcatori (piu' veloce, ma meno sicuro).

.EXAMPLE
    .\verify-deploy.ps1
    Verifica completa dell'installazione locale.

.EXAMPLE
    .\verify-deploy.ps1 -ExpectedHash 9ef0950b...
    Verifica completa con hash vincolante.
#>
[CmdletBinding()]
param(
    [string] $GameFolder = 'D:\SteamLibrary\steamapps\common\Blasphemous',
    [string] $ExpectedHash,
    [switch] $SkipMarkers
)

$ErrorActionPreference = 'Stop'

$dll            = Join-Path $GameFolder 'Modding\plugins\BlasClient.dll'
$commonForMod   = Join-Path $GameFolder 'Modding\data\Blasphemous.Multiplayer.Common.dll'
$serverExe      = Join-Path $GameFolder 'SERVER-COOP\blas1mpserver.exe'
$serverDll      = Join-Path $GameFolder 'SERVER-COOP\blas1mpserver.dll'
$commonForSrv   = Join-Path $GameFolder 'SERVER-COOP\Blasphemous.Multiplayer.Common.dll'

# La sonda Mono.Cecil vive nell'area di lavoro: non serve installare nulla.
$probe = 'D:\Codificando\scratch\cecil'

<#
    I marcatori sono stringhe che esistono nel binario SOLO dalla versione che le ha introdotte.
    Ogni voce e' un momento preciso della storia del progetto, quindi la loro presenza data il
    binario in modo affidabile senza doverlo eseguire.

    Sono stringhe di FORMATO, non nomi di campo o di metodo: cercarle come nomi di simbolo non
    funziona, ed e' un errore gia' commesso una volta.
#>
$markers = [ordered]@{
    'orientSkip='         = "isteresi sull'orientamento attiva"
    'gameOrientBurst='    = 'contatore delle scritture del GIOCO'
    '[OrientByEnemy]'     = 'ripartizione per-nemico delle nostre inversioni'
    '[GameOrientByEnemy]' = 'ripartizione per-nemico delle inversioni del gioco'
    '[AnimByEnemy]'       = 'ripartizione per-nemico degli avvii di animazione'
    '[AnimName]'          = 'nomi veri degli stati di animazione (una volta per hash)'
    '[EnemyJoin]'         = 'fix del join: annuncio delle morti precedenti'
    # Rinominata due volte mentre si costruiva il falo''. Il testo vecchio era "Nemici rinati", ed era
    # protetto da un "if c'e'' qualcosa da dimenticare" che rendeva la riga INVISIBILE proprio quando il
    # falo'' non aveva nulla da riferire: e'' il motivo per cui la TODO diceva "il ramo del falo'' non
    # compare in nessun log". Ora la riga e'' incondizionata e porta la scena, e il marker la segue.
    "[Falo''] memoria nemici" = "fix del falo': _pendingDeaths azzerata al respawn"
    'IMPRONTA DIVERSA'    = 'rilevatore di divergenza delle liste (con cooldown)'
}

function Get-Sha256([string] $path)
{
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ProbePath
{
    Join-Path $probe 'Program.cs'
}

function Invoke-Probe([string] $path, [string[]] $extra)
{
    $program = @'
using Mono.Cecil;
using System.Linq;
class P {
  static void Main(string[] a) {
    var asm = AssemblyDefinition.ReadAssembly(a[0]);
    var strings = asm.MainModule.GetTypes().SelectMany(t => t.Methods)
      .Where(m => m.Body != null).SelectMany(m => m.Body.Instructions)
      .Where(i => i.OpCode.Name == "ldstr").Select(i => i.Operand as string)
      .Where(s => s != null).ToList();
    System.Console.WriteLine("VERSION " + asm.Name.Version);
    foreach (var wanted in a.Skip(1))
      System.Console.WriteLine((strings.Any(s => s.Contains(wanted)) ? "YES " : "NO  ") + wanted);
  }
}
'@

    Set-Content -LiteralPath (Get-ProbePath) -Value $program -Encoding utf8

    Push-Location $probe
    try { $raw = & dotnet run -- $path @extra 2>&1 } finally { Pop-Location }

    $out = @{ Strings = @{}; Version = $null }
    foreach ($line in $raw)
    {
        $text = [string]$line
        if ($text.StartsWith('VERSION ')) { $out.Version = $text.Substring(8).Trim() }
        elseif ($text.StartsWith('YES '))  { $out.Strings[$text.Substring(4).Trim()] = $true }
        elseif ($text.StartsWith('NO  '))  { $out.Strings[$text.Substring(4).Trim()] = $false }
    }

    return $out
}

function Get-Protocol([string] $path)
{
    $program = @'
using Mono.Cecil;
using System.Linq;
class P {
  static void Main(string[] a) {
    var asm = AssemblyDefinition.ReadAssembly(a[0]);
    var refusal = asm.MainModule.GetTypes().FirstOrDefault(t => t.Name == "RefusalType");
    var proto = refusal == null ? null : refusal.Fields.FirstOrDefault(f => f.Name == "Protocol");
    System.Console.WriteLine("PROTOCOL " + (proto != null && proto.HasConstant ? proto.Constant.ToString() : "?"));
    var nt = asm.MainModule.GetTypes().FirstOrDefault(t => t.Name == "NetworkType");
    if (nt != null) {
      var ownership = nt.Fields.FirstOrDefault(f => f.Name == "EnemyOwnership");
      System.Console.WriteLine("OWNERSHIP " + (ownership != null && ownership.HasConstant ? ownership.Constant.ToString() : "?"));
      System.Console.WriteLine("NTCOUNT " + nt.Fields.Count(f => f.HasConstant));
    }
  }
}
'@

    Set-Content -LiteralPath (Get-ProbePath) -Value $program -Encoding utf8

    Push-Location $probe
    try { $raw = & dotnet run -- $path 2>&1 } finally { Pop-Location }

    $out = @{ Protocol = $null; Ownership = $null; Count = $null }
    foreach ($line in $raw)
    {
        $text = [string]$line
        if ($text.StartsWith('PROTOCOL '))  { $out.Protocol  = $text.Substring(9).Trim() }
        if ($text.StartsWith('OWNERSHIP ')) { $out.Ownership = $text.Substring(10).Trim() }
        if ($text.StartsWith('NTCOUNT '))   { $out.Count     = $text.Substring(8).Trim() }
    }

    return $out
}

Write-Host ''
Write-Host '=== VERIFICA DEL DEPLOY ===' -ForegroundColor Cyan
Write-Host "  cartella gioco : $GameFolder"
Write-Host "  macchina       : $env:COMPUTERNAME   utente: $env:USERNAME"
Write-Host ''

$ok = $true
$haveProbe = Test-Path -LiteralPath (Get-ProbePath)

# --- 1. la mod --------------------------------------------------------------
if (-not (Test-Path -LiteralPath $dll))
{
    Write-Host "  MOD     ASSENTE: $dll" -ForegroundColor Red
    $ok = $false
}
else
{
    $hash = Get-Sha256 $dll
    Write-Host "  MOD     $dll"
    Write-Host "          sha256 : $hash"
    Write-Host "          scritto: $((Get-Item -LiteralPath $dll).LastWriteTime)"

    if ($ExpectedHash)
    {
        if ($hash -eq $ExpectedHash.ToLowerInvariant())
        {
            Write-Host '          hash   : CORRISPONDE' -ForegroundColor Green
        }
        else
        {
            Write-Host '          hash   : DIVERSO da quello atteso' -ForegroundColor Red
            Write-Host "                   atteso: $($ExpectedHash.ToLowerInvariant())"
            $ok = $false
        }
    }
}

# --- 2. i marcatori delle funzionalita' -------------------------------------
if (-not $SkipMarkers -and $haveProbe -and (Test-Path -LiteralPath $dll))
{
    Write-Host ''
    Write-Host "  MARCATORI delle funzionalita'"

    $found = Invoke-Probe $dll @($markers.Keys)

    foreach ($name in $markers.Keys)
    {
        $present = $found.Strings.ContainsKey($name) -and $found.Strings[$name]
        $label  = if ($present) { 'PRESENTE' } else { 'ASSENTE ' }
        $colour = if ($present) { 'Green' } else { 'Red' }
        Write-Host ("    {0}  {1,-22} {2}" -f $label, $name, $markers[$name]) -ForegroundColor $colour
        if (-not $present) { $ok = $false }
    }
}
elseif (-not $SkipMarkers)
{
    Write-Host ''
    Write-Host '  (sonda Mono.Cecil assente: salto il controllo dei marcatori)' -ForegroundColor DarkYellow
}

# --- 3. il protocollo -------------------------------------------------------
#
# Common.dll e' l'assembly CONDIVISO fra mod e server, e ne esistono due copie perche' i due
# progetti compilano per framework diversi:
#
#     Modding\data\...   net35   la carica il gioco
#     SERVER-COOP\...    net8.0  la carica il server
#
# Quindi gli HASH sono diversi per costruzione, e confrontarli sarebbe un falso allarme: e' un
# errore che ho commesso scrivendo la prima versione di questo script. Quello che deve coincidere
# e' il CONTENUTO che riguarda il dialogo: il numero di protocollo e i valori di NetworkType.
# Se divergono, mod e server si scambiano pacchetti che non capiscono.
Write-Host ''
Write-Host '  PROTOCOLLO (mod e server devono concordare)'

if (-not $haveProbe)
{
    Write-Host '    (sonda assente: salto)' -ForegroundColor DarkYellow
}
else
{
    $pMod = if (Test-Path -LiteralPath $commonForMod) { Get-Protocol $commonForMod } else { $null }
    $pSrv = if (Test-Path -LiteralPath $commonForSrv) { Get-Protocol $commonForSrv } else { $null }

    foreach ($pair in @(@('mod   ', $pMod), @('server', $pSrv)))
    {
        if ($pair[1] -ne $null -and $pair[1].Protocol)
        {
            Write-Host ("    {0} : protocollo={1}  EnemyOwnership={2}  valori NetworkType={3}" -f `
                $pair[0], $pair[1].Protocol, $pair[1].Ownership, $pair[1].Count)
        }
        else
        {
            Write-Host ("    {0} : Common.dll assente (normale se questa macchina non lo ospita)" -f $pair[0])
        }
    }

    if ($pMod -ne $null -and $pSrv -ne $null -and $pMod.Protocol -and $pSrv.Protocol)
    {
        if ($pMod.Protocol -eq $pSrv.Protocol)
        {
            Write-Host '    i due protocolli COINCIDONO' -ForegroundColor Green
        }
        else
        {
            Write-Host '    I DUE PROTOCOLLI DIVERGONO: mod e server non si capirebbero' -ForegroundColor Red
            $ok = $false
        }
    }
}

# --- il server (facoltativo su questa macchina) -----------------------------
if (Test-Path -LiteralPath $serverDll)
{
    Write-Host ''
    Write-Host "  SERVER  sha256 : $(Get-Sha256 $serverDll)"
}

Write-Host ''
if ($ok)
{
    Write-Host '  ESITO: questa macchina esegue il binario atteso. Puoi testare.' -ForegroundColor Green
}
else
{
    Write-Host '  ESITO: QUESTA MACCHINA NON ESEGUE IL BINARIO ATTESO.' -ForegroundColor Red
    Write-Host "         Un test qui non misurerebbe le funzionalita' nuove." -ForegroundColor Red
    Write-Host '         Copia Modding\ (e Common.dll dove serve) e riprova.' -ForegroundColor Red
}
Write-Host ''

if (-not $ok) { exit 1 }
