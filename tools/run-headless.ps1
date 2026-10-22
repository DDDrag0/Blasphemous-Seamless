# ⭐ BANCO DI PROVA HEADLESS DEL SERVER, SUL FILO VERO — con la SEZIONE A 3 PEER.
#
# Non simula: apre socket TCP veri verso il server, manda pacchetti veri nel framing
# [ushort len][byte type][payload] e cattura OGNI byte che il server risponde.
#
#   pwsh -NoProfile -File tools\run-headless.ps1 -ServerDir <cartella del server> -OutDir <risultati> `
#        [-Port 27999] [-Via dotnet|exe] [-ExpectFramesSha256 <sha|''>]
#
# ---------------------------------------------------------------------------------------------
# DOVE STANNO GLI ARTEFATTI (nessuno di questi file e' tracciato: la prova e' lo SCRIPT, gli
# artefatti sono suoi, e vanno in -OutDir — per esempio sotto `scratch\`, che e' gitignored):
#
#   frames.txt                    ⭐ LA PROVA DI NON-REGRESSIONE: i 42 frame della SEZIONE A
#   bytes.txt                        (il capture grezzo della sezione A)
#   server-log-pulito.txt            (log del server della sola sezione A, normalizzato)
#   frames-fanout.txt             la SEZIONE B: i frame dei 3 peer insieme (il fan-out)
#   bytes-fanout.txt
#   server-log-fanout-pulito.txt
#   server-log.txt                il log del server INTERO, non normalizzato
#
#   Il log normalizzato si spezza in due sull'ULTIMA riga della sezione A,
#   `Host disconnected, no other players connected.` (regola dichiarata: la prima occorrenza di quella
#   riga chiude la sezione A). Cosi' `frames.txt` / `bytes.txt` / `server-log-pulito.txt` restano
#   BYTE PER BYTE quelli di sempre e un confronto con un artefatto vecchio ha senso.
# ---------------------------------------------------------------------------------------------
#
# ⭐ PERCHE' LE DUE SEZIONI (aggiunta del 2026-10-22, `scratch\RICERCA-FANOUT.md` punto (c)).
# Fino a ieri il banco apriva QUATTRO socket ma AL PIU' DUE INSIEME: ogni passo aveva un solo altro
# giocatore. In quella forma un fan-out (`foreach (PeerId other in connectedPlayers.Keys)`) ha sempre
# **N = 1 destinatario**, quindi un'estrazione che si ferma al PRIMO peer passerebbe IDENTICA - e la
# prova del refactor non esisterebbe. La sezione B ne aggiunge un TERZO (127.0.0.3): da li' in poi
# ogni fan-out ha N >= 2 destinatari, e ogni passo dice CHI deve ricevere.
#   ⚠️ La sezione A resta INVARIATA di proposito: i 42 frame (`sha256 9ecdfa42…`) sono la misura di
#   non-regressione del filo, e aggiungere un peer in mezzo li cambierebbe tutti. La sezione B e'
#   nuova e scrive in file suoi.
#
# ⚠️ IL SERVER VA AVVIATO CON UNA CONSOLE VERA: `Core.Main` finisce in `Console.ReadKey`, che lancia
#    `InvalidOperationException` se stdin/stdout sono rediretti (misurato). Percio' si lancia in un
#    processo suo e il suo resoconto si legge da `ServerOutput.log` (che e' la stessa riga del console:
#    `Logger.Print` scrive su entrambi).
#
# ⚠️ `-Via exe` e' il modo storico (`blas1mpserver.exe`) ed e' ANCHE il piu' fragile: l'exe e' NON
#    FIRMATO e Microsoft Defender SmartScreen lo blocca ("impossibile avviare un'app non riconosciuta"),
#    a volte trattenendolo per minuti; `Unblock-File` non serve (nessun flusso `Zone.Identifier`: il
#    blocco e' di REPUTAZIONE). `-Via dotnet` (DEFAULT) avvia `dotnet blas1mpserver.dll`: `dotnet.exe`
#    e' firmato e un `.dll` non e' un eseguibile per Windows, quindi parte subito. Il RESTO del banco e'
#    identico, e i 42 frame lo confermano (identici con entrambi, misurato in `REFERTO-C1A-IDENTITA.md`).
#
# ⚠️ IL PEER NON E' "L'IP": `e.ip` di SimpleTCP e' `IPEndPoint.ToString()`, cioe' `ip:porta`
#    (misurato: "Client connected at 127.0.0.1:54470"). La porta effimera cambia a ogni run, quindi
#    il confronto normalizza `:\d+` -> `:PORT`.
#
# ⚠️ TRE PEER = TRE INDIRIZZI DI ORIGINE (127.0.0.1 / .2 / .3): `Peer` lega il socket locale a quell'IP,
#    cosi' il server li vede come identita' distinte. Due peer con lo STESSO IP sono un caso a parte
#    (passo 14: la chiave e' `ip:porta`, quindi sono ammessi) e non serve a provare un fan-out.
#
# LE TAPPE DELLA SEZIONE A (golden: 42 frame, `sha256 9ecdfa42…`; ogni passo ha al massimo UN altro
# giocatore a cui rigiocare lo stato: ordine deterministico)
#   01 A (127.0.0.1) Intro "Alice"        -> HostAssign(1) + Intro(0)                 [Intro]
#   02 A EnterScene "SCENE-A"             -> niente
#   03 B (127.0.0.2) Intro "Bob"          -> B: HostAssign(0), Intro(0), replay di A; A: Connection(Bob)
#   04 B EnterScene "SCENE-A"             -> A: EnterScene/Pos/Anim/Dir di B; B: Pos/Anim/Dir di A
#   05 A Position(1.5, 2.5)               -> B: Position di A                    [Send(peer, ...) mirato]
#   06 A Animation(3), Direction(1)       -> B: Animation, Direction di A
#   07 B EnemyDamageRequest(dead)         -> SOLO A (host)
#   08 B EnemyOwnership(1)                -> NIENTE (solo l'host puo' emettere ownership)
#   09 A EnemyOwnership(2)                -> B: EnemyOwnership
#   10 A Ping(1.0, 42)                    -> A: Ping con la lista degli ALTRI      [chiave: player.Key == peer]
#   11 A si chiude (host)                 -> B: Connection(Alice,false) + HostAssign(1)
#   12 A2 (127.0.0.1) Intro "Alice2"      -> A2: HostAssign(0), Intro(0), replay di B; B: Connection(Alice2)
#   13 A2 si chiude                       -> B: Connection(Alice2,false) (host invariato: B)
#   14 D (127.0.0.2, STESSO IP di B) Intro "Dave" -> accettato (comportamento di oggi: la chiave e' ip:porta)
#   15 D manda un SECONDO Intro sulla stessa connessione -> Intro(12) = DuplicateIp   [DuplicateIp]
#   16 B si chiude (host)                 -> D: Connection(Bob,false) + HostAssign(1)
#   17 D si chiude                        -> "Host disconnected, no other players connected."
#
# LE TAPPE DELLA SEZIONE B (tre peer insieme, 127.0.0.1 + .2 + .3; N = 2 destinatari)
#   18 P1 Intro "Uno"                     -> HostAssign(1) + Intro(0)
#   19 P2 Intro "Due"                     -> P2: HostAssign+Intro+replay di Uno; P1: Connection(Due)
#   20 Tre (127.0.0.3) Intro "Tre"        -> Tre: replay di Uno E Due (2 destinatari gia' qui);
#                                            P1 e P2: Connection(Tre)                 [fan-out N=2]
#   21-23 i tre entrano in "SCENE-F"       -> ogni ingresso e' UNA chiamata che cicla su tutti:
#                                            l'ingresso di Tre manda EnterScene a P1 E P2, e Pos/Anim/Dir
#                                            di Uno E Due a Tre                          [fan-out N=2]
#   24 P1 Position(3.5, 4.5)               -> P2 **e** C: Position di Uno             ⭐ LA PROVA
#   25 P1 Animation(2), Direction(1)      -> P2 e C: Animation, Direction di Uno    [fan-out N=2]
#   26 P2 Attack(aa bb)                   -> P1 e C (tipo 10, mai spedito prima)     [fan-out N=2]
#   27 C  Effect(07 01 02)                -> P1 e P2 (tipo 11, mai spedito prima)     [fan-out N=2]
#   28 P1 EnemyOwnership(05) dall'host    -> P2 e C                                  [fan-out N=2]
#   29 P2 Progress(tipo 8, "CELLA")       -> P1 e C: e' l'unico fan-out col filtro SQUADRA [N=2]
#   30 C  LeaveScene (tipo 4)             -> P1 e P2 (nessun filtro di scena)         [fan-out N=2]
#   31 P2 Position(5.5, 6.5)              -> P1 SOLTANTO: Tre e' fuori scena           [filtro di scena]
#   32 P2 EnemyDeathGlobal (tipo 24)      -> P1 e C: ignora la scena                   [fan-out N=2]
#   33 P2 Skin (tipo 5)                   -> P1 e C (nessun filtro di scena)           [fan-out N=2]
#   34 P1 (host) si chiude                -> P2 e C: Connection(Uno,false) + HostAssign a TUTTI E DUE
#                                            (1 al promosso Due, 0 a Tre)               [fan-out N=2]
#   35 P2 (nuovo host) si chiude          -> C: Connection(Due,false) + HostAssign(1)
#   36 C si chiude                        -> "Host disconnected, no other players connected."
#
# ⚠️ LE PROVE DELLA SEZIONE B SONO ASSERZIONI, NON SOLO CATTURE: alla fine lo script dice CHI doveva
#    ricevere cosa e stampa PASS/FAIL per ognuna; con un FAIL l'exit code e' 1. Una prova che nessuno
#    legge e' un log, non una prova.

param(
    [Parameter(Mandatory = $true)][string]$ServerDir,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [int]$Port = 27999,
    [ValidateSet('dotnet', 'exe')][string]$Via = 'dotnet',
    # ⭐ L'IMPRONTA DELLA SEZIONE A. Se la sezione A non la riproduce, il filo E' cambiato: lo script lo
    # dice e esce 1. Si puo' azzerare (`-ExpectFramesSha256 ''`) per misurare senza giudicare, ma un
    # banco che non confronta niente non e' una prova di non-regressione.
    [string]$ExpectFramesSha256 = '9ecdfa429aacd33031e0bedaed1af7755e4be2b8a6f40a13fd4beaea1d666dd2'
)

$ErrorActionPreference = 'Stop'

$ServerDir = (Resolve-Path -LiteralPath $ServerDir).ProviderPath
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
Get-ChildItem $OutDir -File -ErrorAction SilentlyContinue | Remove-Item -Force
$logPath = Join-Path $ServerDir 'ServerOutput.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }

$serverFile = if ($Via -eq 'exe') { 'blas1mpserver.exe' } else { 'blas1mpserver.dll' }
$serverPath = Join-Path $ServerDir $serverFile
if (-not (Test-Path $serverPath)) { throw "server non trovato: $serverPath" }

# ⭐ DUE SEZIONI, DUE CATTURE. `$global:sezione` sceglie dove finisce ogni `Note`: la A scrive nei file
# storici (quelli del golden), la B nei suoi. Le liste sono separate perche' i due artefatti si
# confrontano con cose diverse: la A con il baseline, la B con le asserzioni di fan-out.
$global:sezione = 'A'
$global:capA = New-Object 'System.Collections.Generic.List[string]'
$global:frA  = New-Object 'System.Collections.Generic.List[string]'
$global:capB = New-Object 'System.Collections.Generic.List[string]'
$global:frB  = New-Object 'System.Collections.Generic.List[string]'
# Per le asserzioni: i frame di ogni singolo passo, indicizzati dalla sua etichetta.
$global:passi = @{}

function Note([string]$label, [byte[]]$bytes) {
    $hex = ''
    if ($bytes -and $bytes.Length -gt 0) { $hex = ($bytes | ForEach-Object { $_.ToString('x2') }) -join '' }
    $len = 0; if ($bytes) { $len = $bytes.Length }
    # ⚠️ ASSEGNAZIONE SEMPLICE, NON `$x = if (...) { $lista }`: l'`if`-espressione scrive il valore
    # nella pipeline, e la pipeline ENUMERA le collezioni - una lista di stringhe diventerebbe un array
    # (e una lista VUOTA diventerebbe $null), quindi `.Add` esploderebbe al primo passo. Misurato
    # scrivendo questo script: erano zero frame, non un errore di rete.
    $capture = $global:capB
    $frames = $global:frB
    if ($global:sezione -eq 'A') {
        $capture = $global:capA
        $frames = $global:frA
    }
    $capture.Add(("{0,-42} len={1} hex={2}" -f $label, $len, $hex))

    if (-not $global:passi.ContainsKey($label)) {
        $global:passi[$label] = New-Object 'System.Collections.Generic.List[string]'
    }
    $passo = $global:passi[$label]

    $i = 0
    while ($bytes -and $i + 3 -le $bytes.Length) {
        $flen = [BitConverter]::ToUInt16($bytes, $i)
        if ($i + 3 + $flen -gt $bytes.Length) { break }
        $type = $bytes[$i + 2]
        $payloadHex = ''
        if ($flen -gt 0) { $payloadHex = (($bytes[($i + 3)..($i + 3 + $flen - 1)]) | ForEach-Object { $_.ToString('x2') }) -join '' }
        $riga = ("{0,-42} type={1} len={2} payload={3}" -f $label, $type, $flen, $payloadHex)
        $frames.Add($riga)
        $passo.Add(("type={0} len={1} payload={2}" -f $type, $flen, $payloadHex))
        $i += 3 + $flen
    }
    if ($bytes -and $i -ne $bytes.Length) {
        $frames.Add(("{0,-42} TRONCATO: {1} byte non interpretati" -f $label, ($bytes.Length - $i)))
    }
}

function Peer([string]$sourceIp) {
    $deadline = (Get-Date).AddSeconds(6)
    while ($true) {
        $client = $null
        try {
            if ($sourceIp) {
                $client = New-Object System.Net.Sockets.TcpClient((New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Parse($sourceIp), 0)))
            } else {
                $client = New-Object System.Net.Sockets.TcpClient
            }
            $client.NoDelay = $true
            $client.Connect('127.0.0.1', $Port)
            $client.GetStream().ReadTimeout = 700
            return $client
        } catch {
            if ($client) { $client.Close() }
            if ((Get-Date) -gt $deadline) { throw "connessione fallita su porta ${Port}: $($_.Exception.Message)" }
            Start-Sleep -Milliseconds 120
        }
    }
}

function Frame($client, [byte]$type, [byte[]]$payload) {
    $list = New-Object 'System.Collections.Generic.List[byte]'
    $list.AddRange([BitConverter]::GetBytes([ushort]$payload.Length))
    $list.Add($type)
    $list.AddRange($payload)
    $stream = $client.GetStream()
    $bytes = $list.ToArray()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
}

function Drain($client, [string]$label, [int]$MinFrames = -1) {
    # ⚠️ Se `$MinFrames` > 0 e non e' arrivato NIENTE, si riprova: e' la differenza fra "il server non
    # ha risposto" e "la risposta non era ancora arrivata". Misurato: con un'attesa fissa i byte di un
    # passo finivano sotto l'etichetta del passo successivo (stesso filo, artefatto del test).
    # Il default si deduce dall'etichetta: i passi marcati "(niente)" NON devono aspettare niente.
    if ($MinFrames -lt 0) { $MinFrames = 1; if ($label -match 'niente') { $MinFrames = 0 } }
    $deadline = (Get-Date).AddMilliseconds(4000)
    while ($true) {
        $stream = $client.GetStream()
        $stream.ReadTimeout = 700
        $buf = New-Object 'System.Collections.Generic.List[byte]'
        $tmp = New-Object 'byte[]' 8192
        try {
            while ($true) {
                $n = $stream.Read($tmp, 0, 8192)
                if ($n -le 0) { break }
                for ($k = 0; $k -lt $n; $k++) { $buf.Add($tmp[$k]) }
            }
        } catch { }
        if ($MinFrames -le 0 -or $buf.Count -gt 0 -or (Get-Date) -gt $deadline) { break }
        Start-Sleep -Milliseconds 100
    }
    $arr = $buf.ToArray()
    Note $label $arr
}

function Payload-Intro([string]$room, [string]$name, [string]$password, [byte]$team) {
    $l = New-Object 'System.Collections.Generic.List[byte]'
    $l.Add(11)
    $rb = [Text.Encoding]::UTF8.GetBytes($room); $l.Add([byte]$rb.Length); $l.AddRange($rb)
    $nb = [Text.Encoding]::UTF8.GetBytes($name); $l.Add([byte]$nb.Length); $l.AddRange($nb)
    $pb = [Text.Encoding]::UTF8.GetBytes($password); $l.Add([byte]$pb.Length); $l.AddRange($pb)
    $l.Add($team)
    return $l.ToArray()
}

function Payload-Position([single]$x, [single]$y) {
    $l = New-Object 'System.Collections.Generic.List[byte]'
    $l.AddRange([BitConverter]::GetBytes($x))
    $l.AddRange([BitConverter]::GetBytes($y))
    return $l.ToArray()
}

function Wait([int]$ms) { Start-Sleep -Milliseconds $ms }

# Scrittura dei file d'artefatto. Non e' `| Set-Content`: una lista VUOTA in pipeline non crea il file
# (misurato), e un artefatto che manca invece di essere vuoto si confonde con "il run non e' arrivato".
# Le righe e i fine-riga sono gli stessi di `Set-Content -Encoding utf8` (UTF-8 senza BOM, CRLF).
function Scrivi([string]$path, $righe) {
    $arr = @($righe | ForEach-Object { [string]$_ })
    if ($arr.Count -eq 0) { [IO.File]::WriteAllText($path, '') ; return }
    [IO.File]::WriteAllLines($path, [string[]]$arr, (New-Object System.Text.UTF8Encoding($false)))
}

function ShaDi([string]$path) {
    if (-not (Test-Path $path)) { return 'ASSENTE' }
    return (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
}

# --- lettura delle catture, per le asserzioni della sezione B ---------------------------------
function Passo([string]$label) {
    if ($global:passi.ContainsKey($label)) { return @($global:passi[$label]) }
    return @()
}

function Conteggio([string]$label, [int]$type) {
    $n = 0
    foreach ($f in (Passo $label)) { if ($f -match ("^type={0} " -f $type)) { $n++ } }
    return $n
}

function PayloadDi([string]$label, [int]$type) {
    $out = @()
    foreach ($f in (Passo $label)) {
        if ($f -match ("^type={0} len=\d+ payload=(.*)$" -f $type)) { $out += $Matches[1] }
    }
    return $out
}

$global:prove  = New-Object 'System.Collections.Generic.List[string]'
$global:proveKO = 0
function Check([string]$nome, [bool]$ok, [string]$dettaglio) {
    if ($ok) { $global:prove.Add("PASS  $nome") }
    else { $global:prove.Add("FAIL  $nome   ($dettaglio)"); $global:proveKO++ }
}

# --- avvio in un processo SUO: la console e' vera, cosi' `Console.ReadKey` non lancia ---
# Prima si ripuliscono eventuali server rimasti appesi da un run precedente: due ascoltatori sulla
# stessa porta falserebbero il test (il secondo uscirebbe con "Port was already occupied").
# ⚠️ CON `-Via dotnet` SI CERCA PER RIGA DI COMANDO: il processo e' `dotnet.exe` (condiviso con la
# build e col compilatore), quindi il nome del processo non basta piu' a distinguerlo.
Get-Process blas1mpserver -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -match 'blas1mpserver\.dll' } |
    ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch { } }
Start-Sleep -Milliseconds 300

if ($Via -eq 'exe') {
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', 'start', '""', '/D', $ServerDir, 'blas1mpserver.exe', '-p', "$Port", '-mp', '10' -WindowStyle Hidden
    $launcher = $null
} else {
    $launcher = Start-Process -FilePath 'dotnet' -ArgumentList @('blas1mpserver.dll', '-p', "$Port", '-mp', '10') `
                              -WorkingDirectory $ServerDir -WindowStyle Hidden -PassThru
}
$deadline = (Get-Date).AddSeconds(10)
$mine = @()
while ((Get-Date) -lt $deadline) {
    if ($Via -eq 'exe') {
        $mine = @(Get-Process blas1mpserver -ErrorAction SilentlyContinue)
        if ($mine.Count -gt 0 -and (Test-Path $logPath)) { break }
    } else {
        $mine = @(Get-Process -Id $launcher.Id -ErrorAction SilentlyContinue)
        if ($mine.Count -gt 0 -and (Test-Path $logPath)) { break }
        if ($launcher.HasExited) { break }
    }
    Start-Sleep -Milliseconds 150
}
if ($mine.Count -eq 0) { throw "il server non e' partito (nessun processo)" }
Wait 500

$A = $null; $B = $null; $A2 = $null; $D = $null
$P1 = $null; $P2 = $null; $C = $null
$errore = $null
try {
    # ══════════ SEZIONE A — il golden: 42 frame, al piu' DUE peer insieme ══════════
    $A = Peer '127.0.0.1'
    Frame $A 8 (Payload-Intro 'TEST' 'Alice' '' 1); Wait 350; Drain $A '01 A <- HostAssign(1)+Intro(0)'

    Frame $A 3 ([Text.Encoding]::UTF8.GetBytes('SCENE-A')); Wait 250; Drain $A '02 A <- (niente)'

    $B = Peer '127.0.0.2'
    Frame $B 8 (Payload-Intro 'TEST' 'Bob' '' 1); Wait 400
    Drain $B '03 B <- HostAssign+Intro+replay di Alice'
    Drain $A '03 A <- Connection(Bob,true)'

    Frame $B 3 ([Text.Encoding]::UTF8.GetBytes('SCENE-A')); Wait 400
    Drain $A '04 A <- EnterScene/Pos/Anim/Dir di Bob'
    Drain $B '04 B <- Pos/Anim/Dir di Alice'

    Frame $A 0 (Payload-Position 1.5 2.5); Wait 300; Drain $B '05 B <- Position di Alice (mirato)'

    Frame $A 1 ([byte[]]@(3)); Wait 250; Drain $B '06 B <- Animation di Alice'
    Frame $A 2 ([byte[]]@(1)); Wait 250; Drain $B '06 B <- Direction di Alice'

    Frame $B 22 ([byte[]]@(0xde, 0xad)); Wait 300
    Drain $A '07 A(host) <- EnemyDamageRequest dal peer'
    Drain $B '07 B <- (niente)'

    Frame $B 25 ([byte[]]@(0x01)); Wait 300
    Drain $A '08 A <- (niente: ownership dal peer)'
    Drain $B '08 B <- (niente)'

    Frame $A 25 ([byte[]]@(0x02)); Wait 300; Drain $B '09 B <- EnemyOwnership dall host'

    $ping = New-Object 'System.Collections.Generic.List[byte]'
    $ping.AddRange([BitConverter]::GetBytes([single]1.0))
    $ping.AddRange([BitConverter]::GetBytes([ushort]42))
    Frame $A 12 ($ping.ToArray()); Wait 300; Drain $A '10 A <- Ping (chiave del Dictionary)'

    $A.Close(); $A = $null; Wait 600; Drain $B '11 B <- Connection(Alice,false)+HostAssign(1)'

    $A2 = Peer '127.0.0.1'
    Frame $A2 8 (Payload-Intro 'TEST' 'Alice2' '' 1); Wait 400
    Drain $A2 '12 A2 <- HostAssign(0)+Intro(0)+replay di Bob'
    Drain $B '12 B <- Connection(Alice2,true)'

    $A2.Close(); $A2 = $null; Wait 500; Drain $B '13 B <- Connection(Alice2,false)'

    $D = Peer '127.0.0.2'
    Frame $D 8 (Payload-Intro 'TEST' 'Dave' '' 1); Wait 400
    Drain $D '14 D(stesso IP di B) <- HostAssign(0)+Intro(0)+replay di Bob'
    Drain $B '14 B <- Connection(Dave,true)'

    Frame $D 8 (Payload-Intro 'TEST' 'Dave2' '' 1); Wait 400
    Drain $D '15 D <- Intro(12) DuplicateIp'
    Drain $B '15 B <- (niente)'

    $B.Close(); $B = $null; Wait 600; Drain $D '16 D <- Connection(Bob,false)+HostAssign(1)'

    $D.Close(); $D = $null; Wait 600

    # ══════════ SEZIONE B — TRE peer insieme: i fan-out hanno N >= 2 destinatari ══════════
    # ⭐ Da qui i frame NON vanno in frames.txt: il golden della sezione A resta quello di sempre.
    $global:sezione = 'B'

    $P1 = Peer '127.0.0.1'
    Frame $P1 8 (Payload-Intro 'TEST' 'Uno' '' 1); Wait 350; Drain $P1 '18 P1 <- HostAssign(1)+Intro(0)'

    $P2 = Peer '127.0.0.2'
    Frame $P2 8 (Payload-Intro 'TEST' 'Due' '' 1); Wait 450
    Drain $P2 '19 P2 <- HostAssign+Intro+replay di Uno'
    Drain $P1 '19 P1 <- Connection(Due,true)'

    $C = Peer '127.0.0.3'   # ⭐ IL TERZO PEER: da qui ogni fan-out ha DUE destinatari
    Frame $C 8 (Payload-Intro 'TEST' 'Tre' '' 1); Wait 450
    Drain $C  '20 C <- HostAssign+Intro+replay di Uno e Due'
    Drain $P1 '20 P1 <- Connection(Tre,true)'
    Drain $P2 '20 P2 <- Connection(Tre,true)'

    # 21-23: i tre nella STESSA scena. Ogni ingresso e' UNA chiamata che cicla su tutti i peer:
    # l'ingresso di Tre manda EnterScene a P1 E P2, e Pos/Anim/Dir di Uno E Due a Tre.
    Frame $P1 3 ([Text.Encoding]::UTF8.GetBytes('SCENE-F')); Wait 400
    Drain $P1 '21 P1 <- (niente)'
    Drain $P2 '21 P2 <- EnterScene(Uno) (Due non ha una scena)'
    Drain $C  '21 C <- EnterScene(Uno) (Tre non ha una scena)'

    Frame $P2 3 ([Text.Encoding]::UTF8.GetBytes('SCENE-F')); Wait 450
    Drain $P1 '22 P1 <- EnterScene+Pos/Anim/Dir di Due'
    Drain $P2 '22 P2 <- Pos/Anim/Dir di Uno (stessa scena)'
    Drain $C  '22 C <- EnterScene(Due) (Tre non ha una scena)'

    Frame $C 3 ([Text.Encoding]::UTF8.GetBytes('SCENE-F')); Wait 500
    Drain $P1 '23 P1 <- EnterScene+Pos/Anim/Dir di Tre'
    Drain $P2 '23 P2 <- EnterScene+Pos/Anim/Dir di Tre'
    Drain $C  '23 C <- Pos/Anim/Dir di Uno e di Due'

    # ⭐⭐ 24: LA PROVA. UN pacchetto di Uno, DUE destinatari (Due e Tre).
    Frame $P1 0 (Payload-Position 3.5 4.5); Wait 350
    Drain $P2 '24 P2 <- Position di Uno [FAN-OUT]'
    Drain $C  '24 C <- Position di Uno [FAN-OUT]'

    Frame $P1 1 ([byte[]]@(2)); Wait 300
    Drain $P2 '25 P2 <- Animation di Uno'
    Drain $C  '25 C <- Animation di Uno'

    Frame $P1 2 ([byte[]]@(1)); Wait 300
    Drain $P2 '25 P2 <- Direction di Uno'
    Drain $C  '25 C <- Direction di Uno'

    # 26-27: due tipi che il banco non aveva MAI spedito (Attack=10, Effect=11), filtro di scena.
    Frame $P2 10 ([byte[]]@(0xaa, 0xbb)); Wait 350
    Drain $P1 '26 P1 <- Attack di Due'
    Drain $C  '26 C <- Attack di Due'

    Frame $C 11 ([byte[]]@(0x07, 0x01, 0x02)); Wait 350
    Drain $P1 '27 P1 <- Effect di Tre'
    Drain $P2 '27 P2 <- Effect di Tre'

    # 28: EnemyOwnership dall'host -> relayToScene, cioe' a tutti quelli della scena.
    Frame $P1 25 ([byte[]]@(0x05)); Wait 350
    Drain $P2 '28 P2 <- EnemyOwnership(05) dall host'
    Drain $C  '28 C <- EnemyOwnership(05) dall host'

    # ⭐ 29: Progress (tipo 9). E' l'UNICO dei 15 fan-out con un filtro DIVERSO: stessa SQUADRA, non
    # stessa scena. Squadra 1 per tutti e tre, quindi N=2. Payload: [tipo=8][valore=1]["CELLA"].
    Frame $P2 9 ([byte[]]@(8, 1, 0x43, 0x45, 0x4c, 0x4c, 0x41)); Wait 350
    Drain $P1 '29 P1 <- Progress di Due [SQUADRA]'
    Drain $C  '29 C <- Progress di Due [SQUADRA]'

    # 30: LeaveScene (tipo 4, mai spedito) -> a tutti tranne me, SENZA filtro di scena.
    Frame $C 4 ([byte[]]@(0x00)); Wait 350
    Drain $P1 '30 P1 <- LeaveScene(Tre)'
    Drain $P2 '30 P2 <- LeaveScene(Tre)'

    # ⭐ 31: Tre ora e' FUORI scena: la stessa Position di Due arriva a UNO SOLO. Il filtro di scena
    # funziona, quindi il fan-out del passo 24 non e' un "manda a tutti per sbaglio".
    Frame $P2 0 (Payload-Position 5.5 6.5); Wait 350
    Drain $P1 '31 P1 <- Position di Due (Tre e fuori scena)'
    Drain $C  '31 C <- (niente: fuori scena)'

    # ⭐ 32: EnemyDeathGlobal (tipo 24) ignora la scena: arriva ANCHE a Tre.
    Frame $P2 24 ([byte[]]@(0x2f, 0x61)); Wait 350
    Drain $P1 '32 P1 <- EnemyDeathGlobal'
    Drain $C  '32 C <- EnemyDeathGlobal (Tre e fuori scena)'

    # 33: Skin (tipo 5, mai spedito) -> a tutti tranne me.
    Frame $P2 5 ([byte[]]@(0x09, 0x08, 0x07)); Wait 350
    Drain $P1 '33 P1 <- Skin di Due'
    Drain $C  '33 C <- Skin di Due'

    # ⭐ 34: ESCE L'HOST, con DUE peer ancora dentro. E' il fan-out n.1 (`clientDisconnected:598`):
    # la Connection(Uno,false) va a tutti e due, e l'HostAssign va a TUTTI E DUE - prima era one-shot
    # al solo promosso, e il terzo non sapeva niente. Il nuovo host e' il primo rimasto in ordine di
    # enumerazione del dizionario (Due, inserito prima di Tre).
    $P1.Close(); $P1 = $null; Wait 700
    Drain $P2 '34 P2 <- Connection(Uno,false)+HostAssign(1)'
    Drain $C  '34 C <- Connection(Uno,false)+HostAssign(0)'

    # 35: esce anche il nuovo host, e resta un peer solo: Connection(Due,false) + HostAssign(1).
    $P2.Close(); $P2 = $null; Wait 700
    Drain $C  '35 C <- Connection(Due,false)+HostAssign(1)'

    $C.Close(); $C = $null; Wait 500
}
catch {
    # Il run si e' fermato a meta': si tiene il messaggio E il punto, perche' "un passo e' fallito"
    # senza dire dove non serve a nessuno.
    $errore = $_.Exception.Message + '   [dove: ' + $_.InvocationInfo.PositionMessage.Trim() + ']'
}
finally {
    foreach ($client in @($A, $B, $A2, $D, $P1, $P2, $C)) {
        if ($client) { try { $client.Close() } catch { } }
    }
    Wait 400
    $mine | ForEach-Object { try { $_.Kill() } catch { } }
    Get-Process blas1mpserver -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
    Wait 600

    # --- log: copia grezza, poi la normalizzazione dichiarata (via il timestamp, porta -> :PORT) ---
    $pulito = @()
    if (Test-Path $logPath) {
        Copy-Item $logPath (Join-Path $OutDir 'server-log.txt') -Force
        $pulito = @((Get-Content $logPath) | ForEach-Object {
            ($_ -replace '^\[\d\d:\d\d:\d\d\] ', '') -replace '(?<=\d):\d{4,5}\b', ':PORT'
        })
    } else {
        $pulito = @('NESSUN ServerOutput.log: il server non ha scritto niente')
    }

    # IL CONFINE FRA LE DUE SEZIONI E' UNA RIGA DEL SERVER: la PRIMA occorrenza di
    # `Host disconnected, no other players connected.` chiude la sezione A (e' l'effetto dell'ultimo
    # passo della A, il 17: l'host se ne va e non resta nessuno). Regola dichiarata, non indovinata: se
    # la riga non c'e', la sezione A e' tutto il log e lo si vedra' dal conteggio dei frame.
    $confine = -1
    for ($i = 0; $i -lt $pulito.Count; $i++) {
        if ($pulito[$i].Trim() -eq 'Host disconnected, no other players connected.') { $confine = $i; break }
    }
    $pulitoA = if ($confine -ge 0) { @($pulito[0..$confine]) } else { @($pulito) }
    $pulitoB = if ($confine -ge 0 -and ($confine + 1) -lt $pulito.Count) { @($pulito[($confine + 1)..($pulito.Count - 1)]) } else { @() }
    $pulitoA | Set-Content (Join-Path $OutDir 'server-log-pulito.txt') -Encoding utf8
    if ($pulitoB.Count -gt 0) { $pulitoB | Set-Content (Join-Path $OutDir 'server-log-fanout-pulito.txt') -Encoding utf8 }

    Scrivi (Join-Path $OutDir 'bytes.txt')          $global:capA
    Scrivi (Join-Path $OutDir 'frames.txt')         $global:frA
    Scrivi (Join-Path $OutDir 'bytes-fanout.txt')   $global:capB
    Scrivi (Join-Path $OutDir 'frames-fanout.txt')  $global:frB
}

$shaA = ShaDi (Join-Path $OutDir 'frames.txt')
$shaB = ShaDi (Join-Path $OutDir 'frames-fanout.txt')
$logSize = 'ASSENTE'
if (Test-Path $logPath) { $logSize = (Get-Item $logPath).Length.ToString() + ' byte' }

# ---------------------------------------------------------------- LE PROVE DELLA SEZIONE B ----
# Il payload atteso della Position di Uno(3.5, 4.5): [len=3]"Uno" + 8 byte di float little-endian.
$posUno = '03556e6f' + '00006040' + '00009040'

$p2 = @(PayloadDi '24 P2 <- Position di Uno [FAN-OUT]' 0)
$c2 = @(PayloadDi '24 C <- Position di Uno [FAN-OUT]' 0)
Check 'fan-out: la Position di Uno arriva a DUE' ($p2.Count -eq 1 -and $c2.Count -eq 1) "P2=$($p2.Count) frame, C=$($c2.Count) frame"
Check 'fan-out: il pacchetto e'' quello di Uno (nome+3.5+4.5), identico ai due' `
      ($p2.Count -eq 1 -and $p2[0] -eq $posUno -and $c2.Count -eq 1 -and $c2[0] -eq $posUno) `
      "P2=$($p2 -join ',') C=$($c2 -join ',') atteso=$posUno"

Check 'fan-out: EnterScene di Tre arriva a P1 E P2 (una sola chiamata, N=2)' `
      ((Conteggio '23 P1 <- EnterScene+Pos/Anim/Dir di Tre' 3) -eq 1 -and (Conteggio '23 P2 <- EnterScene+Pos/Anim/Dir di Tre' 3) -eq 1) `
      "P1=$((Conteggio '23 P1 <- EnterScene+Pos/Anim/Dir di Tre' 3)) P2=$((Conteggio '23 P2 <- EnterScene+Pos/Anim/Dir di Tre' 3))"
Check 'fan-out: Tre riceve la Position di Uno E di Due (N=2 destinatari dentro un ciclo)' `
      ((Conteggio '23 C <- Pos/Anim/Dir di Uno e di Due' 0) -eq 2) `
      "trovati $((Conteggio '23 C <- Pos/Anim/Dir di Uno e di Due' 0))"
Check 'fan-out: EnemyDeathGlobal arriva anche a chi e'' fuori scena' `
      ((Conteggio '32 C <- EnemyDeathGlobal (Tre e fuori scena)' 24) -eq 1 -and (Conteggio '32 P1 <- EnemyDeathGlobal' 24) -eq 1) `
      "C=$((Conteggio '32 C <- EnemyDeathGlobal (Tre e fuori scena)' 24)) P1=$((Conteggio '32 P1 <- EnemyDeathGlobal' 24))"
Check 'filtro di scena: a Tre (fuori scena) NON arriva la Position di Due' `
      ((Passo '31 C <- (niente: fuori scena)').Count -eq 0) `
      "ricevuti $((Passo '31 C <- (niente: fuori scena)').Count) frame"
Check 'fan-out: Progress (filtro SQUADRA) arriva a P1 E C' `
      ((Conteggio '29 P1 <- Progress di Due [SQUADRA]' 9) -eq 1 -and (Conteggio '29 C <- Progress di Due [SQUADRA]' 9) -eq 1) `
      "P1=$((Conteggio '29 P1 <- Progress di Due [SQUADRA]' 9)) C=$((Conteggio '29 C <- Progress di Due [SQUADRA]' 9))"

# ⭐ L'USCITA DELL'HOST: due destinatari, e il "chi" e' diverso per ognuno (uno e' il nuovo host).
$haP2 = @(PayloadDi '34 P2 <- Connection(Uno,false)+HostAssign(1)' 13)
$haC  = @(PayloadDi '34 C <- Connection(Uno,false)+HostAssign(0)' 13)
Check 'fan-out: uscita dell''host -> Connection a DUE destinatari' `
      ((Conteggio '34 P2 <- Connection(Uno,false)+HostAssign(1)' 7) -eq 1 -and (Conteggio '34 C <- Connection(Uno,false)+HostAssign(0)' 7) -eq 1) `
      "P2=$((Conteggio '34 P2 <- Connection(Uno,false)+HostAssign(1)' 7)) C=$((Conteggio '34 C <- Connection(Uno,false)+HostAssign(0)' 7))"
Check 'fan-out: HostAssign a TUTTI E DUE, con 1 al promosso e 0 all''altro' `
      ($haP2.Count -eq 1 -and $haP2[0] -eq '01' -and $haC.Count -eq 1 -and $haC[0] -eq '00') `
      "P2=$($haP2 -join ',') C=$($haC -join ',')"
Check 'elezione host: dopo l''uscita dell''host il primo rimasto (Due) diventa host' `
      ((Conteggio '35 C <- Connection(Due,false)+HostAssign(1)' 13) -eq 1 -and @(PayloadDi '35 C <- Connection(Due,false)+HostAssign(1)' 13)[0] -eq '01') `
      "HostAssign a Tre: $(@(PayloadDi '35 C <- Connection(Due,false)+HostAssign(1)' 13) -join ',')"

$golden = 'non misurato'
if ($ExpectFramesSha256) {
    $golden = if ($shaA -eq $ExpectFramesSha256) { 'OK' } else { 'DIVERSO' }
    Check "sezione A: i 42 frame del golden ($($ExpectFramesSha256.Substring(0,8))…)" ($shaA -eq $ExpectFramesSha256) "sha256 misurato $shaA"
} else {
    $golden = 'non atteso (-ExpectFramesSha256 vuoto)'
}
Check 'sezione A: i frame sono 42' ($global:frA.Count -eq 42) "misurati $($global:frA.Count)"

'=== esito ==='
"server           : $Via $serverFile  ($ServerDir)"
"ServerOutput.log : $logSize"
''
'--- SEZIONE A · il golden (al piu'' 2 peer insieme) ---'
"frame            : $($global:frA.Count)"
"sha256 frames    : $shaA"
"atteso           : $ExpectFramesSha256"
"golden           : $golden"
''
'--- SEZIONE B · tre peer insieme (i fan-out) ---'
"frame            : $($global:frB.Count)"
"sha256 frames    : $shaB"
''
'--- prove del fan-out ---'
$global:prove | ForEach-Object { $_ }
''
"Artefatti in: $OutDir"
'  sezione A : frames.txt, bytes.txt, server-log-pulito.txt'
'  sezione B : frames-fanout.txt, bytes-fanout.txt, server-log-fanout-pulito.txt'
'  intero    : server-log.txt'
''

if ($errore) {
    "ESITO: ERRORE DURANTE IL RUN — $errore"
    'Il run NON e'' arrivato in fondo: le prove sopra valgono solo per i passi eseguiti.'
    exit 1
}
if ($global:proveKO -gt 0) {
    "ESITO: $($global:proveKO) PROVE FALLITE su $($global:prove.Count)"
    exit 1
}
"ESITO: $($global:prove.Count)/$($global:prove.Count) PASS — sezione A golden $golden"
exit 0
