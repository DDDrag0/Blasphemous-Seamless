# Verifica dei bersagli delle patch Harmony.
#
# PERCHE' ESISTE. Un attributo [HarmonyPatch(typeof(X), "nome")] porta il nome del bersaglio in una
# STRINGA. Se quel metodo non esiste, il compilatore non dice nulla, il build passa, e il guasto
# compare solo a runtime - come HarmonyException dentro PatchAll, che INTERROMPE l'applicazione di
# TUTTE le patch successive dell'assembly. Il sintomo e' ingannevole: la mod non scrive piu' una riga
# di log, e l'aggiornamento per frame esplode con un NullReferenceException che non ha niente a che
# vedere con la patch sbagliata.
#
# E' successo l'11 ottobre 2026 con `[HarmonyPatch(typeof(SpawnManager), "OnPlayerSpawn")]`:
# `OnPlayerSpawn` e' un CAMPO evento, non un metodo. Ha bruciato una sessione di test.
#
# LE DUE FORME DELL'ATTRIBUTO. Harmony accetta la stessa patch in DUE modi, entrambi validi:
#
#     FORMA NORMALE - UN attributo, DUE argomenti:
#         [HarmonyPatch(typeof(X), "nome")]
#
#     FORMA DIVISA - DUE attributi, UN argomento ciascuno, che Harmony COMBINA:
#         [HarmonyPatch(typeof(X))]
#         [HarmonyPatch("nome")]
#
# Fino al 22 ottobre 2026 questo script contava SOLO la forma normale. La forma divisa non entrava nel
# conteggio (diceva 272 dove i bersagli veri erano 273 a `e77d48d`, il fix del tempo in pausa) e -
# peggio - il suo metodo NON veniva verificato affatto: un errore di battitura li' sarebbe passato in
# silenzio, con la patch viva sulla carta e morta a runtime. Ora le due forme si contano e si
# verificano allo stesso modo.
#
# LA REGOLA PRUDENTE PER LA FORMA DIVISA. Il `typeof(T)` e il nome del metodo si accoppiano SOLO se
# stanno vicini nella dichiarazione: righe consecutive, o fra loro solo altri attributi `[Harmony*]`.
# Se in mezzo c'e' qualunque altra cosa, non sono la stessa patch e NON si accoppiano. E un attributo
# diviso che resta senza il suo complemento e' un ERRORE dichiarato, non un silenzio: una patch
# incompleta e' peggio di una patch non contata, perche' `PatchAll` non sa applicarla e nessuno se ne
# accorge.
#
# Questo script controlla ogni bersaglio PRIMA del deploy. Se trova un metodo o un tipo che non
# esiste - o una forma divisa incompleta - esce con codice 1 e dice quale.
#
# GLI ESITI (dichiarati, e distinti dal testo - non dal solo numero).
#     0  la sonda e' stata compilata, eseguita, e TUTTI i bersagli esistono
#     1  la verifica NON e' stata eseguita (senza `dotnet`, build della sonda fallita, sonda
#        mancante/non compilata, oppure ZERO bersagli trovati) - oppure e' stata eseguita e ha
#        trovato bersagli rotti.
# Il numero 1 e' lo stesso nei due casi (il deploy si ferma in entrambi: e' quello che conta), ma il
# MESSAGGIO dice quale dei due e': "non ho potuto compilare la sonda" non e' "un nome in
# [HarmonyPatch] e' sbagliato". Fino al 2026-10-22 il gate usciva **0** quando `dotnet` non c'era
# ($code non inizializzato -> `exit $null` -> 0), cioe' dichiarava "tutto valido" senza aver misurato
# niente. Il messaggio "un nome in [HarmonyPatch] e' sbagliato" si stampa SOLO sulla strada in cui la
# sonda ha davvero controllato qualcosa.
#
# LA SONDA. Il programma C# e il suo `.csproj` sono TRACCIATI in `tools\verify-patch-targets\`
# (Program.cs + probe.csproj). Questo script ne fa una COPIA in $env:TEMP e compila li', cosi' la repo
# non si sporca di bin/obj. Prima il `.csproj` stava in `D:\Codificando\scratch\` (fuori dalla repo,
# gitignored): su un clone pulito la sonda non c'era e il gate usciva 1 = "bersagli inesistenti",
# cioe' la diagnosi piu' allarmante possibile per un problema di setup.
#
# USO
#     .\verify-patch-targets.ps1
#     .\verify-patch-targets.ps1 -ModDll <percorso> -GameDll <percorso>
#     .\verify-patch-targets.ps1 -ProbeSource <cartella con Program.cs + probe.csproj>
#     .\verify-patch-targets.ps1 -ProbeDir <cartella di lavoro dove copiare e compilare la sonda>

[CmdletBinding()]
param(
    [string] $ModDll  = 'D:\SteamLibrary\steamapps\common\Blasphemous\Modding\plugins\BlasClient.dll',
    [string] $GameDll = 'D:\SteamLibrary\steamapps\common\Blasphemous\Blasphemous_Data\Managed\Assembly-CSharp.dll',
    [string] $ProbeSource = '',
    [string] $ProbeDir    = ''
)

$ErrorActionPreference = 'Stop'

if (-not $ProbeSource) { $ProbeSource = Join-Path $PSScriptRoot 'verify-patch-targets' }
# La cartella di lavoro e' UNA PER PROCESSO ($PID): due verifier in parallelo - per esempio due
# worktree dello stesso repo, uno che deploya e uno che controlla un'altra branch - scriverebbero la
# stessa Program.cs e uno dei due verificherebbe il codice dell'altro. Con $PID non si incontrano; e
# la sonda viene comunque ricopiata a ogni esecuzione, quindi non si esegue mai un binario stantio.
if (-not $ProbeDir)    { $ProbeDir    = Join-Path ([System.IO.Path]::GetTempPath()) ("blasphemous-coop-verify-patch-targets\$PID") }

$probeCsproj  = Join-Path $ProbeSource 'probe.csproj'
$probeProgram = Join-Path $ProbeSource 'Program.cs'
$probeDll     = Join-Path $ProbeDir 'bin\Debug\net8.0\probe.dll'

if (-not (Test-Path $ModDll))  { Write-Host "  mod non trovata: $ModDll" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $GameDll)) { Write-Host "  gioco non trovato: $GameDll" -ForegroundColor Red; exit 1 }

# La sonda e' versionata: se manca, manca NELLA REPO, e il gate non puo' misurare niente. Dirlo cosi'
# e' l'unico modo di non mandare a cercare una patch rotta al posto di un file mancante.
foreach ($f in @($probeCsproj, $probeProgram)) {
    if (-not (Test-Path $f)) {
        Write-Host ""
        Write-Host "  NON HO POTUTO VERIFICARE: la sonda non e' nella repo, manca $f" -ForegroundColor Red
        Write-Host "  La cartella della sonda e': $ProbeSource" -ForegroundColor Red
        Write-Host "  Nessun bersaglio Harmony e' stato controllato: questo exit 1 NON significa" -ForegroundColor Yellow
        Write-Host "  'ci sono bersagli inesistenti', significa 'il gate non ha potuto girare'." -ForegroundColor Yellow
        exit 1
    }
}

# `$code` parte da 1 e NON da $null: qualunque strada che non arrivi a misurare (eccezione, sonda
# mancante, build fallita) deve lasciare il gate ROSSO. Un gate che dichiara "tutto valido" quando non
# ha potuto girare e' peggio di un gate assente.
$code     = 1
$esito    = 'nonmisurato'
$output   = @()
$buildOut = @()
$dettaglio = ''

# Copia della sonda in una cartella temporanea: la repo resta pulita (niente bin/obj dentro tools\),
# e la copia e' sempre quella TRACCIATA - non una versione lasciata li' da una sessione precedente.
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
        # L'output della sonda si mostra SEMPRE: quando trova un bersaglio rotto, le righe
        # "TIPO ASSENTE"/"METODO ASSENTE" SONO la diagnosi.
        $output = & dotnet $probeDll $ModDll $GameDll 2>&1
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 1 }
        $esito = if ($code -eq 0) { 'ok' } elseif ($code -eq 3) { 'zerobersagli' } else { 'bersagli' }
    }
}
catch {
    # `dotnet` non esiste, o non parte: era ESATTAMENTE il caso in cui il vecchio script usciva 0.
    $esito = 'nondotnet'
    $dettaglio = $_.Exception.Message
    $code = 1
}
finally {
    Pop-Location
}

if ($esito -eq 'ok') {
    $output | ForEach-Object { "  $_" }
    # Successo: la cartella di lavoro si butta, cosi' $env:TEMP non si riempie di compilazioni
    # (ricompilare costa ~3 s). Se invece il gate FALLISCE la cartella resta: li' dentro c'e' quello
    # che serve a capire.
    Remove-Item $ProbeDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 0
}

if ($esito -eq 'nondotnet') {
    Write-Host ""
    Write-Host "  NON HO POTUTO COMPILARE LA SONDA: 'dotnet' non e' disponibile o non parte." -ForegroundColor Red
    Write-Host "  Dettaglio: $dettaglio" -ForegroundColor Red
    Write-Host "  Nessun bersaglio Harmony e' stato controllato. Questo exit 1 NON significa" -ForegroundColor Yellow
    Write-Host "  'ci sono bersagli inesistenti': significa 'il gate non ha potuto girare'." -ForegroundColor Yellow
    exit 1
}

if ($esito -eq 'buildko') {
    Write-Host ""
    Write-Host "  NON HO POTUTO COMPILARE LA SONDA: la BUILD della sonda e' fallita (exit $buildCode)." -ForegroundColor Red
    Write-Host "  Questo NON e' un problema delle patch Harmony: la verifica non e' stata eseguita." -ForegroundColor Red
    Write-Host "  Output della build:" -ForegroundColor Red
    $buildOut | ForEach-Object { "  $_" }
    exit 1
}

if ($esito -eq 'noprobe') {
    Write-Host ""
    Write-Host "  NON HO POTUTO VERIFICARE: la build e' passata ma $probeDll non esiste." -ForegroundColor Red
    Write-Host "  Se hai rinominato l'AssemblyName in probe.csproj, questo script lo cerca ancora li'." -ForegroundColor Yellow
    exit 1
}

if ($esito -eq 'zerobersagli') {
    $output | ForEach-Object { "  $_" }
    Write-Host ""
    Write-Host "  NON HO MISURATO NIENTE: la sonda non ha trovato NESSUN attributo [HarmonyPatch]." -ForegroundColor Red
    Write-Host "  La mod ne ha 273: se il conteggio e' zero, la DLL letta e' quella sbagliata ($ModDll)." -ForegroundColor Red
    Write-Host "  Questo exit 1 NON significa 'ci sono bersagli inesistenti': la verifica non ha misurato." -ForegroundColor Yellow
    exit 1
}

# esito 'bersagli': la sonda HA girato, e ha trovato qualcosa. Questa e' l'unica strada in cui la
# diagnosi "un nome in [HarmonyPatch] e' sbagliato" e' VERA.
$output | ForEach-Object { "  $_" }
Write-Host ""
Write-Host "  Un nome sbagliato in [HarmonyPatch] compila e fallisce a runtime, e la PatchAll" -ForegroundColor Yellow
Write-Host "  che abortisce impedisce l'applicazione di TUTTE le patch successive." -ForegroundColor Yellow
exit $code
