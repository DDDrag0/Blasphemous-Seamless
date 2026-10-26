# Headless server test over real TCP, with fan-out coverage.
#
# Opens real sockets, sends real packets in the framing [ushort len][byte type][payload],
# captures every byte the server replies with.
#
#   pwsh -NoProfile -File tools\run-headless.ps1 -ServerDir <server> -OutDir <results> `
#        [-Port 27999] [-Via dotnet|exe] [-ExpectFramesSha256 <sha|''>]
#
# Two sections:
#   A) 2 peers max, deterministic order: 42 frames checked against -ExpectFramesSha256.
#      This is the wire-level non-regression gate.
#   B) 3 peers at once: every fan-out has N>=2 targets, so an extraction that stops at the
#      first peer would fail. Section B artifacts are separate; each fan-out step is
#      asserted by named PASS/FAIL checks and a FAIL exits 1.
#
# Section A labels, payload hex and packet types are part of the golden artifact and must not
# change: frames.txt is compared to a fixed sha256. Section B labels are only used for the
# assertions.
#
# Section split: first occurrence of `Host disconnected, no other players connected.` (last
# effect of section A, step 17) closes section A. If missing, section A is the whole log.
#
# -Via exe uses blas1mpserver.exe (unsigned, Defender SmartScreen often blocks it).
# -Via dotnet (default) uses `dotnet blas1mpserver.dll` (signed host, no delay). Wire output
# is identical with both.
#
# Peers must use distinct source IPs (127.0.0.1/2/3): `e.ip` is `ip:port`, so distinct IPs
# give the server distinct identities. Ports are ephemeral, hence the :PORT normalization.
#
# Artifacts (none tracked; all under -OutDir):
#   frames.txt, bytes.txt, server-log-pulito.txt    section A
#   frames-fanout.txt, bytes-fanout.txt,
#   server-log-fanout-pulito.txt                    section B
#   server-log.txt                                  full server log, raw
#
# Sections:
#   A: 01 A Intro Alice, 02 EnterScene, 03 B Intro Bob, 04 B EnterScene, 05 A Position,
#      06 A Anim+Dir, 07 B EnemyDamageRequest (host only), 08 B EnemyOwnership (ignored,
#      peer), 09 A EnemyOwnership (relayed), 10 A Ping, 11 A closes (host reassigned),
#      12 A2 Intro Alice2, 13 A2 closes (host kept), 14 D same IP as B accepted,
#      15 D second Intro -> DuplicateIp, 16 B closes (host reassigned), 17 D closes
#   B: 18 P1 Uno, 19 P2 Due, 20 Tre enters (N=2 replay), 21-23 three peers enter SCENE-F
#      (fan-outs), 24 P1 Position reaches P2 AND Tre [N=2], 25 Anim+Dir, 26 Attack,
#      27 Effect, 28 EnemyOwnership, 29 Progress (TEAM filter) [N=2], 30 LeaveScene,
#      31 P2 Position only to in-scene peer [scene filter], 32 EnemyDeathGlobal even to
#      out-of-scene peer, 33 Skin, 34 P1 closes (host reassigned to BOTH survivors),
#      35 P2 closes (host reassigned to Tre), 36 Tre closes

param(
    [Parameter(Mandatory = $true)][string]$ServerDir,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [int]$Port = 27999,
    [ValidateSet('dotnet', 'exe')][string]$Via = 'dotnet',
    # Section A golden hash: if the wire changed, section A will not reproduce it and the
    # test exits 1. Pass '' to measure without judging.
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
if (-not (Test-Path $serverPath)) { throw "server not found: $serverPath" }

# Two sections, two captures. $global:sezione routes each Note to the right list: A goes into
# the golden artifacts, B into separate ones. Kept separate because A is compared to the
# baseline while B is judged by the fan-out assertions.
$global:sezione = 'A'
$global:capA = New-Object 'System.Collections.Generic.List[string]'
$global:frA  = New-Object 'System.Collections.Generic.List[string]'
$global:capB = New-Object 'System.Collections.Generic.List[string]'
$global:frB  = New-Object 'System.Collections.Generic.List[string]'
# Per-step frames, keyed by label, for the section B assertions.
$global:passi = @{}

function Note([string]$label, [byte[]]$bytes) {
    $hex = ''
    if ($bytes -and $bytes.Length -gt 0) { $hex = ($bytes | ForEach-Object { $_.ToString('x2') }) -join '' }
    $len = 0; if ($bytes) { $len = $bytes.Length }
    # Simple assignment, not `$x = if (...) { $list }`: the if-expression writes its result into
    # the pipeline, and the pipeline ENUMERATES collections - a list would become an array (and
    # an empty list would become $null), so .Add would explode on the first step.
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
        $frames.Add(("{0,-42} TRUNCATED: {1} bytes not interpreted" -f $label, ($bytes.Length - $i)))
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
            if ((Get-Date) -gt $deadline) { throw "connection failed on port ${Port}: $($_.Exception.Message)" }
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
    # If MinFrames > 0 and nothing arrived yet, retry: distinguishes "server did not answer"
    # from "answer not there yet". A fixed wait caused step bytes to leak under the next label.
    # Default: labels containing "niente" expect 0 frames, others expect >= 1.
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

# Artifact writer. Not `| Set-Content`: an empty list in the pipeline creates no file (measured),
# and a missing artifact looks like a run that never started. Lines and line endings match
# Set-Content -Encoding utf8 (UTF-8 no BOM, CRLF).
function Scrivi([string]$path, $righe) {
    $arr = @($righe | ForEach-Object { [string]$_ })
    if ($arr.Count -eq 0) { [IO.File]::WriteAllText($path, '') ; return }
    [IO.File]::WriteAllLines($path, [string[]]$arr, (New-Object System.Text.UTF8Encoding($false)))
}

function ShaDi([string]$path) {
    if (-not (Test-Path $path)) { return 'ASSENTE' }
    return (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
}

# --- capture lookups for section B assertions ---
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

# --- launch in its own process: a real console, so Console.ReadKey does not throw ---
# Clean up any leftover server first: two listeners on the same port would falsify the test.
# With -Via dotnet, match by command line: the process is dotnet.exe (shared with the build
# and compiler), so the process name alone is not enough.
Get-Process blas1mpserver -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Get-Process -Name dotnet -ErrorAction SilentlyContinue |
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
if ($mine.Count -eq 0) { throw "server did not start (no process)" }
Wait 500

$A = $null; $B = $null; $A2 = $null; $D = $null
$P1 = $null; $P2 = $null; $C = $null
$errore = $null
try {
    # ===== SECTION A — golden: 42 frames, at most TWO peers at a time =====
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

    # ===== SECTION B — THREE peers at once: every fan-out has N >= 2 targets =====
    # From here frames do NOT go into frames.txt: section A gold stays untouched.
    $global:sezione = 'B'

    $P1 = Peer '127.0.0.1'
    Frame $P1 8 (Payload-Intro 'TEST' 'Uno' '' 1); Wait 350; Drain $P1 '18 P1 <- HostAssign(1)+Intro(0)'

    $P2 = Peer '127.0.0.2'
    Frame $P2 8 (Payload-Intro 'TEST' 'Due' '' 1); Wait 450
    Drain $P2 '19 P2 <- HostAssign+Intro+replay di Uno'
    Drain $P1 '19 P1 <- Connection(Due,true)'

    $C = Peer '127.0.0.3'   # the third peer: from here every fan-out has TWO targets
    Frame $C 8 (Payload-Intro 'TEST' 'Tre' '' 1); Wait 450
    Drain $C  '20 C <- HostAssign+Intro+replay di Uno e Due'
    Drain $P1 '20 P1 <- Connection(Tre,true)'
    Drain $P2 '20 P2 <- Connection(Tre,true)'

    # 21-23: three peers enter the same scene. Each entry is one call that loops over all
    # peers: Tre's entry sends EnterScene to P1 AND P2, and Uno's + Due's Pos/Anim/Dir to Tre.
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

    # 24: THE PROOF. One packet from Uno, TWO targets (Due and Tre).
    Frame $P1 0 (Payload-Position 3.5 4.5); Wait 350
    Drain $P2 '24 P2 <- Position di Uno [FAN-OUT]'
    Drain $C  '24 C <- Position di Uno [FAN-OUT]'

    Frame $P1 1 ([byte[]]@(2)); Wait 300
    Drain $P2 '25 P2 <- Animation di Uno'
    Drain $C  '25 C <- Animation di Uno'

    Frame $P1 2 ([byte[]]@(1)); Wait 300
    Drain $P2 '25 P2 <- Direction di Uno'
    Drain $C  '25 C <- Direction di Uno'

    # 26-27: two packet types the bench had never sent (Attack=10, Effect=11), scene filter.
    Frame $P2 10 ([byte[]]@(0xaa, 0xbb)); Wait 350
    Drain $P1 '26 P1 <- Attack di Due'
    Drain $C  '26 C <- Attack di Due'

    Frame $C 11 ([byte[]]@(0x07, 0x01, 0x02)); Wait 350
    Drain $P1 '27 P1 <- Effect di Tre'
    Drain $P2 '27 P2 <- Effect di Tre'

    # 28: EnemyOwnership from host -> relayToScene, i.e. everyone in the scene.
    Frame $P1 25 ([byte[]]@(0x05)); Wait 350
    Drain $P2 '28 P2 <- EnemyOwnership(05) dall host'
    Drain $C  '28 C <- EnemyOwnership(05) dall host'

    # 29: Progress (type 9). The only fan-out with a DIFFERENT filter: same TEAM, not scene.
    # Team 1 for all three, so N=2. Payload: [type=8][value=1]["CELLA"].
    Frame $P2 9 ([byte[]]@(8, 1, 0x43, 0x45, 0x4c, 0x4c, 0x41)); Wait 350
    Drain $P1 '29 P1 <- Progress di Due [SQUADRA]'
    Drain $C  '29 C <- Progress di Due [SQUADRA]'

    # 30: LeaveScene (type 4, never sent) -> to all except me, WITHOUT scene filter.
    Frame $C 4 ([byte[]]@(0x00)); Wait 350
    Drain $P1 '30 P1 <- LeaveScene(Tre)'
    Drain $P2 '30 P2 <- LeaveScene(Tre)'

    # 31: Tre is now OUT of scene: the same Position from Due reaches ONE peer only. The
    # scene filter works, so the fan-out of step 24 is not "send to everyone by mistake".
    Frame $P2 0 (Payload-Position 5.5 6.5); Wait 350
    Drain $P1 '31 P1 <- Position di Due (Tre e fuori scena)'
    Drain $C  '31 C <- (niente: fuori scena)'

    # 32: EnemyDeathGlobal (type 24) ignores the scene: reaches Tre too.
    Frame $P2 24 ([byte[]]@(0x2f, 0x61)); Wait 350
    Drain $P1 '32 P1 <- EnemyDeathGlobal'
    Drain $C  '32 C <- EnemyDeathGlobal (Tre e fuori scena)'

    # 33: Skin (type 5, never sent) -> to all except me.
    Frame $P2 5 ([byte[]]@(0x09, 0x08, 0x07)); Wait 350
    Drain $P1 '33 P1 <- Skin di Due'
    Drain $C  '33 C <- Skin di Due'

    # 34: HOST LEAVES, with TWO peers still inside. The first fan-out (clientDisconnected):
    # Connection(Uno,false) goes to both, and HostAssign goes to BOTH - not one-shot to the
    # promoted peer only. New host is the first remaining in dictionary order (Due).
    $P1.Close(); $P1 = $null; Wait 700
    Drain $P2 '34 P2 <- Connection(Uno,false)+HostAssign(1)'
    Drain $C  '34 C <- Connection(Uno,false)+HostAssign(0)'

    # 35: the new host also leaves, one peer remains: Connection(Due,false) + HostAssign(1).
    $P2.Close(); $P2 = $null; Wait 700
    Drain $C  '35 C <- Connection(Due,false)+HostAssign(1)'

    $C.Close(); $C = $null; Wait 500
}
catch {
    # Run stopped halfway: keep the message and the position.
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

    # --- log: raw copy, then the declared normalization (strip timestamp, ports -> :PORT) ---
    $pulito = @()
    if (Test-Path $logPath) {
        Copy-Item $logPath (Join-Path $OutDir 'server-log.txt') -Force
        $pulito = @((Get-Content $logPath) | ForEach-Object {
            ($_ -replace '^\[\d\d:\d\d:\d\d\] ', '') -replace '(?<=\d):\d{4,5}\b', ':PORT'
        })
    } else {
        $pulito = @('NESSUN ServerOutput.log: il server non ha scritto niente')
    }

    # Section divider is a server line: FIRST occurrence of
    # `Host disconnected, no other players connected.` closes section A (the effect of the last
    # step of A, step 17: host leaves, nobody left). Declared rule, not guessed: if the line is
    # missing, section A is the whole log.
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

# ------------------------------------------------------------- SECTION B ASSERTIONS ----
# Expected Position payload of Uno (3.5, 4.5): [len=3]"Uno" + 8 bytes little-endian floats.
$posUno = '03556e6f' + '00006040' + '00009040'

$p2 = @(PayloadDi '24 P2 <- Position di Uno [FAN-OUT]' 0)
$c2 = @(PayloadDi '24 C <- Position di Uno [FAN-OUT]' 0)
Check 'fan-out: Position of Uno reaches TWO peers' ($p2.Count -eq 1 -and $c2.Count -eq 1) "P2=$($p2.Count) frames, C=$($c2.Count) frames"
Check 'fan-out: payload is Uno (name+3.5+4.5), identical to both' `
      ($p2.Count -eq 1 -and $p2[0] -eq $posUno -and $c2.Count -eq 1 -and $c2[0] -eq $posUno) `
      "P2=$($p2 -join ',') C=$($c2 -join ',') expected=$posUno"

Check 'fan-out: EnterScene of Tre reaches P1 AND P2 (one call, N=2)' `
      ((Conteggio '23 P1 <- EnterScene+Pos/Anim/Dir di Tre' 3) -eq 1 -and (Conteggio '23 P2 <- EnterScene+Pos/Anim/Dir di Tre' 3) -eq 1) `
      "P1=$((Conteggio '23 P1 <- EnterScene+Pos/Anim/Dir di Tre' 3)) P2=$((Conteggio '23 P2 <- EnterScene+Pos/Anim/Dir di Tre' 3))"
Check 'fan-out: Tre receives Position of Uno AND Due (N=2 in one loop)' `
      ((Conteggio '23 C <- Pos/Anim/Dir di Uno e di Due' 0) -eq 2) `
      "found $((Conteggio '23 C <- Pos/Anim/Dir di Uno e di Due' 0))"
Check 'fan-out: EnemyDeathGlobal reaches even out-of-scene peer' `
      ((Conteggio '32 C <- EnemyDeathGlobal (Tre e fuori scena)' 24) -eq 1 -and (Conteggio '32 P1 <- EnemyDeathGlobal' 24) -eq 1) `
      "C=$((Conteggio '32 C <- EnemyDeathGlobal (Tre e fuori scena)' 24)) P1=$((Conteggio '32 P1 <- EnemyDeathGlobal' 24))"
Check 'scene filter: out-of-scene Tre does NOT receive Position of Due' `
      ((Passo '31 C <- (niente: fuori scena)').Count -eq 0) `
      "received $((Passo '31 C <- (niente: fuori scena)').Count) frames"
Check 'fan-out: Progress (TEAM filter) reaches P1 AND C' `
      ((Conteggio '29 P1 <- Progress di Due [SQUADRA]' 9) -eq 1 -and (Conteggio '29 C <- Progress di Due [SQUADRA]' 9) -eq 1) `
      "P1=$((Conteggio '29 P1 <- Progress di Due [SQUADRA]' 9)) C=$((Conteggio '29 C <- Progress di Due [SQUADRA]' 9))"

# Host departure: two recipients, each gets a different "you are host" value.
$haP2 = @(PayloadDi '34 P2 <- Connection(Uno,false)+HostAssign(1)' 13)
$haC  = @(PayloadDi '34 C <- Connection(Uno,false)+HostAssign(0)' 13)
Check 'fan-out: host departure -> Connection to TWO peers' `
      ((Conteggio '34 P2 <- Connection(Uno,false)+HostAssign(1)' 7) -eq 1 -and (Conteggio '34 C <- Connection(Uno,false)+HostAssign(0)' 7) -eq 1) `
      "P2=$((Conteggio '34 P2 <- Connection(Uno,false)+HostAssign(1)' 7)) C=$((Conteggio '34 C <- Connection(Uno,false)+HostAssign(0)' 7))"
Check 'fan-out: HostAssign to BOTH, 1 for the promoted peer and 0 for the other' `
      ($haP2.Count -eq 1 -and $haP2[0] -eq '01' -and $haC.Count -eq 1 -and $haC[0] -eq '00') `
      "P2=$($haP2 -join ',') C=$($haC -join ',')"
Check 'host election: after host leaves, first remaining (Due) becomes host' `
      ((Conteggio '35 C <- Connection(Due,false)+HostAssign(1)' 13) -eq 1 -and @(PayloadDi '35 C <- Connection(Due,false)+HostAssign(1)' 13)[0] -eq '01') `
      "HostAssign to Tre: $(@(PayloadDi '35 C <- Connection(Due,false)+HostAssign(1)' 13) -join ',')"

$golden = 'non misurato'
if ($ExpectFramesSha256) {
    $golden = if ($shaA -eq $ExpectFramesSha256) { 'OK' } else { 'DIVERSO' }
    Check "section A: the 42-frame golden ($($ExpectFramesSha256.Substring(0,8))...)" ($shaA -eq $ExpectFramesSha256) "measured sha256 $shaA"
} else {
    $golden = 'non atteso (-ExpectFramesSha256 empty)'
}
Check 'section A: frames count is 42' ($global:frA.Count -eq 42) "measured $($global:frA.Count)"

'=== result ==='
"server           : $Via $serverFile  ($ServerDir)"
"ServerOutput.log : $logSize"
''
'--- SECTION A - golden (at most 2 peers at a time) ---'
"frames           : $($global:frA.Count)"
"sha256 frames    : $shaA"
"expected         : $ExpectFramesSha256"
"golden           : $golden"
''
'--- SECTION B - three peers at once (fan-outs) ---'
"frames           : $($global:frB.Count)"
"sha256 frames    : $shaB"
''
'--- fan-out assertions ---'
$global:prove | ForEach-Object { $_ }
''
"Artifacts in: $OutDir"
'  section A : frames.txt, bytes.txt, server-log-pulito.txt'
'  section B : frames-fanout.txt, bytes-fanout.txt, server-log-fanout-pulito.txt'
'  full      : server-log.txt'
''

if ($errore) {
    "ESITO: ERROR DURING RUN - $errore"
    'The run did NOT reach the end: assertions above are only valid for the steps executed.'
    exit 1
}
if ($global:proveKO -gt 0) {
    "ESITO: $($global:proveKO) FAILED ASSERTIONS out of $($global:prove.Count)"
    exit 1
}
"ESITO: $($global:prove.Count)/$($global:prove.Count) PASS - section A golden $golden"
exit 0