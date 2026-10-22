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
# GLI ESITI (tre cose diverse, tre parole diverse - e nessuna si traveste da un'altra).
#     0  TUTTI-VALIDI ......... la sonda ha controllato: tutti i bersagli esistono
#     2  BERSAGLI-INVALIDI .... la sonda ha controllato e ha TROVATO bersagli mancanti o patch
#                               incomplete  -> "N bersagli NON esistono: <elenco>"
#     3  ZERO-BERSAGLI ........ la sonda non ha trovato NESSUN attributo [HarmonyPatch]
#                               -> "NON HO MISURATO NIENTE" (DLL sbagliata)
#     4  ERRORE ............... la sonda e' partita e si e' fermata a meta' (DLL illeggibile,
#                               eccezione dentro la sonda)  -> "NON HO POTUTO VERIFICARE"
#     altro, o marca assente .. la sonda e' crashata / e' stata uccisa  -> "NON HO POTUTO
#                               VERIFICARE" - mai "le patch sono sbagliate"
#
# ECCO IL DIFETTO CHE QUESTO BLOCCO CHIUDE (misurato il 2026-10-22, REFERTO-GATE-2.md). Fino a ieri
# qualsiasi exit != 0/3 finiva su `bersagli`, e si stampava "un nome in [HarmonyPatch] e' sbagliato"
# anche quando la sonda era morta PRIMA di aprire la DLL: con una BlasClient.dll corrotta, Cecil
# lancia BadImageFormatException dentro Main, il processo esce con -532462766 (0xE0434352) e lo
# script accusava le patch. La stessa diagnosi falsa che il commit precedente voleva togliere, viva
# un livello piu' in basso: mentiva esattamente quando la sonda non poteva leggere una DLL, cioe' nei
# casi in cui il gate e' l'unica cosa che guarda.
# Percio' l'exit code NON decide da solo: la sonda dichiara l'esito con una marca macchina
# `SONDA-ESITO: <esito>` (ultima riga del suo output) e lo script crede al numero SOLO se combacia
# con la marca. Un numero senza la sua marca e' un crash, non un esito.
#
# IL CODICE DEL GATE e' 0 solo se la sonda ha dichiarato TUTTI-VALIDI; ogni altro caso esce 1 (il
# deploy si ferma sempre: e' quello che conta). Il numero non distingue piu' i casi: li distingue il
# MESSAGGIO, e ognuno ha il suo. "un nome in [HarmonyPatch] e' sbagliato" si stampa SOLO sulla strada
# 'bersagli', cioe' solo quando la sonda ha dichiarato di aver misurato.
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

# La cartella di lavoro viene AZZERATA (vedi sotto): non deve poter essere la cartella della sonda,
# ne' contenerla, ne' starci dentro. Con -ProbeDir sbagliato si perderebbe il sorgente della sonda -
# che e' tracciato, ma il gate deve fermarsi, non distruggere. Meglio un exit 1 dichiarato che un
# `-ProbeDir tools\` che cancella il tool che sta girando.
$dirFull = [System.IO.Path]::GetFullPath($ProbeDir).TrimEnd('\', '/')
$srcFull = [System.IO.Path]::GetFullPath($ProbeSource).TrimEnd('\', '/')
if ($srcFull -eq $dirFull -or $srcFull.StartsWith($dirFull + '\') -or $dirFull.StartsWith($srcFull + '\')) {
    Write-Host ""
    Write-Host "  NON HO POTUTO VERIFICARE: -ProbeDir e -ProbeSource si sovrappongono." -ForegroundColor Red
    Write-Host "  ProbeSource : $srcFull" -ForegroundColor Red
    Write-Host "  ProbeDir    : $dirFull" -ForegroundColor Red
    Write-Host "  Lo script azzera la cartella di lavoro: se contiene il sorgente della sonda, il gate" -ForegroundColor Yellow
    Write-Host "  distruggerebbe cio' che deve compilare. Scegli una cartella separata." -ForegroundColor Yellow
    exit 1
}

# `$code` parte da 1 e NON da $null: qualunque strada che non arrivi a misurare (eccezione, sonda
# mancante, build fallita) deve lasciare il gate ROSSO. Un gate che dichiara "tutto valido" quando non
# ha potuto girare e' peggio di un gate assente.
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

# IL PROTOCOLLO D'ESITO. Tabella unica: marca <-> codice. La sonda scrive la marca come ultima riga
# del suo output, e lo script accetta il codice SOLO se combacia (vedi il commento in testa).
# Un numero che non e' in questa tabella, o che non ha la sua marca, e' un crash: non un esito.
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

# Copia della sonda in una cartella temporanea: la repo resta pulita (niente bin/obj dentro tools\),
# e la copia e' sempre quella TRACCIATA - non una versione lasciata li' da una sessione precedente.
#
# LA CARTELLA SI AZZERA PRIMA DI COPIARE, e non e' igiene: e' l'unico modo di essere sicuri di
# eseguire la sonda TRACCIATA. `Copy-Item` conserva la data del file, e `dotnet build` incrementale
# NON ricompila quando la `probe.dll` che trova e' piu' NUOVA della `Program.cs` appena copiata: se la
# cartella di lavoro resta da una corsa precedente (su un fallimento resta, di proposito), il gate
# eseguirebbe la sonda VECCHIA. Misurato il 2026-10-22: una sonda che crasha, eseguita in una cartella
# lasciata da una corsa fallita, ha stampato "273 / tutti validi" e il gate e' uscito 0 - un verde
# prodotto da un binario che non era quello nella repo (REFERTO-GATE-2.md §4). Succede davvero, non
# in teoria: il nome della cartella usa il PID, e Windows riusa i PID.
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
        # L'output della sonda si mostra SEMPRE: quando trova un bersaglio rotto, le righe
        # "TIPO ASSENTE"/"METODO ASSENTE" SONO la diagnosi.
        $output = & dotnet $probeDll $ModDll $GameDll 2>&1
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 1 }

        # Lettura del protocollo: `SONDA-CONTA: k=v ...` (i numeri) e `SONDA-ESITO: <esito>` (la
        # dichiarazione). Se la dichiarazione combacia col codice, l'esito e' quello; se non c'e',
        # o non combacia, e' un crash - qualunque cosa dica il numero.
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
                # Il dettaglio e' la riga che la sonda ha scritto prima di fermarsi: quale eccezione,
                # su quale file. Non e' una diagnosi sulle patch, ed e' l'unica cosa che si sa.
                $rigaErrore = $testo | Where-Object { $_ -match 'SONDA FALLITA:' } | Select-Object -First 1
                $dettaglio = if ($rigaErrore) { ($rigaErrore -replace '^\s*SONDA FALLITA:\s*', '').Trim() }
                             else { "la sonda ha dichiarato un errore interno (exit $code) senza dire quale" }
            }
        }
        else {
            $esito = 'sondacrash'
            if ($marca) {
                $dettaglio = "exit $code contro la marca '$marca', che a quel codice non corrisponde"
            } else {
                $dettaglio = "exit $code, e la riga 'SONDA-ESITO: ...' non c'e'"
            }
        }

        # Le righe macchina non sono diagnosi: si tolgono da quello che si mostra.
        $visibile = @($testo | Where-Object { $_ -notmatch '^\s*SONDA-(ESITO|CONTA):' })
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
    $visibile | ForEach-Object { "  $_" }
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
    $visibile | ForEach-Object { "  $_" }
    Write-Host ""
    Write-Host "  NON HO MISURATO NIENTE: la sonda non ha trovato NESSUN attributo [HarmonyPatch]." -ForegroundColor Red
    Write-Host "  La mod ne ha 273: se il conteggio e' zero, la DLL letta e' quella sbagliata ($ModDll)." -ForegroundColor Red
    Write-Host "  Questo exit 1 NON significa 'ci sono bersagli inesistenti': la verifica non ha misurato." -ForegroundColor Yellow
    exit 1
}

# ── ② LA SONDA E' FALLITA (o e' crashata): NON HO POTUTO VERIFICARE ──────────────────────────────
# Questa strada esiste perche' prima non esisteva: qui non si sa NULLA delle patch. La sonda non ha
# finito, quindi nessun nome in [HarmonyPatch] puo' essere dichiarato sbagliato - e la frase che lo
# dice non si stampa qui. Un crash esce con un codice qualunque: non lo si interpreta.
if ($esito -eq 'sondaerrore' -or $esito -eq 'sondacrash') {
    Write-Host ""
    if ($esito -eq 'sondaerrore') {
        Write-Host "  NON HO POTUTO VERIFICARE: la sonda e' fallita ($dettaglio)." -ForegroundColor Red
    } else {
        Write-Host "  NON HO POTUTO VERIFICARE: la sonda NON ha dichiarato un esito ($dettaglio)." -ForegroundColor Red
        Write-Host "  Un crash della sonda - eccezione non gestita, DLL illeggibile, processo ucciso - esce" -ForegroundColor Red
        Write-Host "  con un codice che NON e' un esito: a volte 1, lo stesso che la sonda usa per 'bersagli" -ForegroundColor Red
        Write-Host "  rotti'. Per questo il codice, da solo, non decide niente: serve la marca." -ForegroundColor Red
    }
    # Quello che la sonda ha stampato prima di fermarsi si mostra (serve a capire), dicendo pero'
    # che NON e' un elenco di bersagli: la misura non e' arrivata in fondo.
    $altre = @($visibile | Where-Object { $_ -notmatch 'SONDA FALLITA:' })
    if ($altre.Count -gt 0) {
        Write-Host ""
        Write-Host "  Output della sonda (traccia del guasto, NON un elenco di bersagli: la verifica non" -ForegroundColor Yellow
        Write-Host "  e' arrivata in fondo):" -ForegroundColor Yellow
        $altre | ForEach-Object { "  $_" }
    }
    Write-Host ""
    Write-Host "  Questo exit 1 NON significa 'un nome in [HarmonyPatch] e' sbagliato': significa che il" -ForegroundColor Yellow
    Write-Host "  gate non ha potuto misurare. La colpa e' della sonda (DLL illeggibile, eccezione), non" -ForegroundColor Yellow
    Write-Host "  delle patch - che restano non verificate." -ForegroundColor Yellow
    exit 1
}

# ── ① LA SONDA HA CONTROLLATO E HA TROVATO BERSAGLI ROTTI ──────────────────────────────────────
# Questa e' l'UNICA strada in cui la diagnosi "un nome in [HarmonyPatch] e' sbagliato" e' VERA: la
# sonda ha dichiarato BERSAGLI-INVALIDI, e il codice 2 combacia.
$nRotti = $null; $nInc = 0; $nTot = 0
if ($c.ContainsKey('tipoassente') -and $c.ContainsKey('metodoassente')) {
    $nRotti = $c['tipoassente'] + $c['metodoassente']
}
if ($c.ContainsKey('incompleta')) { $nInc = $c['incompleta'] }
if ($c.ContainsKey('totale'))     { $nTot = $c['totale'] }

Write-Host ""
if ($null -ne $nRotti -and $nRotti -eq 1) {
    Write-Host "  1 bersaglio NON esiste, su $nTot controllati:" -ForegroundColor Red
}
elseif ($null -ne $nRotti -and $nRotti -gt 1) {
    Write-Host "  $nRotti bersagli NON esistono, su $nTot controllati:" -ForegroundColor Red
}
if ($nInc -eq 1) {
    Write-Host "  1 patch INCOMPLETA (una forma divisa spezzata non si applica a runtime):" -ForegroundColor Red
}
elseif ($nInc -gt 1) {
    Write-Host "  $nInc patch INCOMPLETE (una forma divisa spezzata non si applica a runtime):" -ForegroundColor Red
}
if ($null -eq $nRotti) {
    Write-Host "  La sonda ha dichiarato BERSAGLI-INVALIDI ma non ha dichiarato i conteggi:" -ForegroundColor Red
}
Write-Host ""
$visibile | ForEach-Object { "  $_" }
Write-Host ""
Write-Host "  Un nome sbagliato in [HarmonyPatch] compila e fallisce a runtime, e la PatchAll" -ForegroundColor Yellow
Write-Host "  che abortisce impedisce l'applicazione di TUTTE le patch successive." -ForegroundColor Yellow
exit 1
