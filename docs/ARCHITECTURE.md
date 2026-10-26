# ARCHITECTURE — Architecture and development notes

> **What this file is for.** A developer opens this repository knowing nothing. After reading it:
> what the mod does, what works, what was **not** done and why, what has **not been verified yet**, and how
> to verify a build. It is the map; it is not the diary.
>
> **And who it is for.** Whoever builds on this code. The plain-language page — what the mod does, for
> somebody who just wants to know what he has in his hands — is `docs\STATUS.md`. This file is the former
> `docs\STATUS.md`, renamed and kept entire: nothing was thrown away, only the audience changed.
>
> **What it is not.** Not a chronicle: no "on such a day we did X". **The chronicle itself no longer exists**:
> the day-by-day record (decisions, measurements, dead ends, the reasoning behind every number) was
> `docs\TODO.md`, **deleted in `bdea334`** — what it said is in `git log docs/TODO.md`. **The "why" is here**:
> §5 is the decisions, `docs\BUG-NOTI.md` the declared limits and accepted prices, `docs\STATUS.md` §5 the
> problems that were solved. The rollback points are `docs\RIPRISTINO.md`.
>
> **State.** Branch `fix-f1a`, HEAD `9e75a0b` (tag `pre-refactor`). This page was written and measured
> against `e243e07` and its network layer — `a8b2bde` (the peer identity), `cd5f11f` (the server seam),
> `22e1a9e` (the P2P transport), `2a6ca17` (the invite chooses the P2P), `e2fe745` (the two P2P timeouts),
> `694f16d` (the reconnection) — and then **updated across `694f16d..9e75a0b`, 16 commits**. What those
> commits changed, and where it is written: the **pause is no longer shared** (§2, §3, §5 — this was the
> one line here that taught the opposite, and it was false); the **world keeps running while a menu is
> open** and the death closes the menus first (§2, §5); the P2P link **revives** itself and tolerates
> **15 s** of silence (§2); the `Close` P2P **no longer runs during the shutdown** (§2, §5); the server
> **validates its payloads, its `team` and its `progressType`** (§2); `PROTOCOL_VERSION` lives in
> **`Common`** (§2, §6); the Harmony gate **stops lying** and counts both shapes of the attribute (§6);
> the headless bench lives in **`tools\`** and proves the fan-out with **three peers** (§7); `build.yml`
> is gone (§5). Every `file:line` that this update cites — or re-cites — was re-measured at `9e75a0b`; the
> ones deliberately left on their old base are listed in `scratch\REFERTO-ARCHITECTURE-2.md`. **Do not
> trust numbers quoted inside older reports**, some are 20+ lines behind, and two of them are no longer
> true at all: *"the pause is shared"* (removed by `0246f0d`) and *"the heartbeat timeout is 5 s"* (now
> 15 s, `6c06039`). Every deploy from `8d5dbc8` on still checks **273** Harmony targets with **0**
> missing. Code paths are relative to
> `Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main\`; document paths to the repository root.

---

## 1. What this mod is

**Co-op for Blasphemous** — Unity **2017.4.40f1**, BepInEx **5.4.23.4**, Blasphemous **Modding API 3.0.1**,
**HarmonyX** (the `0Harmony.dll` shipped with BepInEx). Two binaries: the client plugin `BlasClient.dll`
(loaded from `Modding\plugins`) and a standalone console server shipped in `SERVER-COOP`.
**273 Harmony patch targets** (see §2, the bullet with the count).

**The principle: the co-op is a layer. With no server connection the game behaves exactly as vanilla.**
All new behaviour hangs off one predicate — the socket, `CoopGate.IsMultiplayerActive()`
(`Blasphemous.Multiplayer.Client\CoopGate.cs:28`) = `NetworkManager.IsConnected` — and when in doubt it
answers `false`, which is the inert state (`CoopGate.cs:22-31`). If you add a feature, gate it there.

---

## 2. Architecture in one page

- **The server is a relay with no memory.** Team progress lives in RAM only
  (`Blasphemous.Multiplayer.Server\Core.cs:13`) and is replayed to whoever connects — one packet per stored
  key (`Server.cs:872-888`); when the last player of a team leaves, that team's data is **deleted**
  (`Core.removeUnusedGameData`, `Core.cs:108-142`, `teamGameDatas.Remove(team)` at `:139`; called when a
  client disconnects `Server.cs:562`, when a `Team` packet arrives `:900`, and at `Server.Start` `:199`, so a
  session always starts from zero). There is no persistence anywhere in the server project: restarting it
  zeroes everything. **This is by design** — a server-side memory would mix saves from different sessions and
  different players, and a *wrong* flag cannot be rebuilt while a *lost* one can (`§5`, "The server has no
  memory").
- ⭐ **The server defends itself against the wire, and never throws on a malformed packet.** Three surfaces
  were open and are now closed, each **before** the read that used to go past the end: **the payload
  length** — a per-type minimum table (`MinimumPayloadLength`, `Server.cs:382-394`) checked in
  `processDataReceived` (`:335-340`), so a short `Position`/`Animation`/`Direction`/`Team`/`Progress`/`Ping`
  is **discarded and declared** (`[Server] payload corto: tipo=… attesi=… ho=…`, `:436`), never an exception
  inside the transport's read thread — the very failure the framing guard was written for, applied to the
  payload at last; **`team`** — a `byte` off the wire, **normalized** into 1..10 at **both** doors
  (`sanitizeTeam`, `:851-859`, called at `:897` and `:1047`) rather than refused, because a `team` outside
  1..10 used to create a dictionary entry that `Core.removeUnusedGameData` never removed and the cleanup now
  scans the **whole** `byte` domain (`Core.cs:119`); **`progressType`** — a cap derived from the array it
  indexes (`MAX_PROGRESS_TYPE = TeamInfo.NUMBER_OF_PROGRESS_TYPES - 1`, `:499`), so **0..13** passes (the
  domain of `ProgressType`) and everything else is discarded and declared (`[Server] progresso scartato: `,
  `:530`); plus a **256-byte** cap on the progress id (`:501`), which had no prefix and no limit and created
  one entry per new id. **No byte on the wire changes**: the 42-frame harness stays `sha256 9ecdfa42…`, and
  that is the proof. ⚠️ One declared regression: a non-standard client whose `team` was outside 1..10 now
  shares team 1's progress instead of getting a bucket of its own (`scratch\AUDIT-REFACTOR.md` §④).
- **The server has no dead limbs left, measured on both builds.** The members with zero callers were removed
  (`308ecaa`) — `Server.IsRunning`, `Server.DelayDisabled`, `TcpServerTransport.IsListening`,
  `CompositeServerTransport`'s `params` constructor / `Count` / `Remove` (while `Add` stays),
  `PeerId.IsEmpty`, the write-only `Core` field, an unreachable `??` and one `using` — checked on the
  **deployed binaries** (Cecil + reflection, `net35` with `COOP_HOST` and `net8.0` standalone), because the
  server has two lives and "no callers in the client" is not "dead". ⚠️ Three were **kept with their proof**:
  `Logger.Print` (five callers per build — it is the line that writes console *and* `ServerOutput.log`),
  `ServerLog.Initialize` (called by `Write`), and `PeerId.Ip`, which stays **by decision** — it is the only
  reading of the IP case, and it is `Common.dll`'s ABI.
- **The authority is the client's save.** Each client re-pushes its own progress: on entering a scene
  (`Multiplayer.cs:221`) and on connection (`:540`), via `SendAllProgress`
  (`ProgressSync\Helpers\FlagHelper.cs:22-25`) → `GetCurrentPersistentState("intro")` → the prefix that
  sends every flag that is set (`FlagHelper.cs:46-69`); the server stores it again (`AddTeamProgress`,
  `Server.cs:1114`). So a
  flag the server has forgotten comes back from the player's own save, and a boss that was killed in a
  previous session is hidden through the flag (`HideDefeatedBosses`, `EnemySyncManager.cs:732`; the only
  reader of `_BOSSDEAD` is `FirstBossDeadFlagOn`, `:966`; called at `:2732`).
- **Effects: one pool hook, a multi-source catalogue on the receiver, a receiver-side dedup.** The channel
  is a single `Postfix` on `PoolManager.ReuseObject` (`Blasphemous.Multiplayer.Client\EffectRelay.cs:86`) —
  the only place where the game creates an effect from a pool. The sender carries no semantics (`prefab`
  name + position + rotation); the **receiver** decides, locally. Name resolution tries four sources in
  order and stops at the first hit: boss fields → enemies in the scene → the local `Penitent` → a per-name
  cache (`EffectRelay.cs:788`, `:826`, `SearchEnemies :910`). Duplicates are suppressed locally on
  `prefab + rounded position` in a 0.5 s window (`DEDUP_SECONDS`, `:144`; check at `:675`). Two raw
  `Object.Instantiate` sites inside boss attacks never reach the pool and have their own two postfixes
  (`EffectRelayInstantiateArea.cs:69`, `:89`).
- **Enemy damage to the peer: a forwarding proxy + the peer's own damage pipeline.** The aggro proxy
  carries an `IDamageable` (`EnemyHitPeerSync.cs:85`); a hit that lands on it does not touch the local
  `Penitent` — it is forwarded, and the peer applies it through the game's own door,
  `Penitent.Damage(Hit)` (`EnemyHitPeerSync.cs:551`). Attack sites that used to throw on the bare proxy are
  guarded in `AggroProxyPatches.cs`.
- 🔴 **Pause is NOT shared — the mod shows a line, and does nothing else.** This replaces a statement that
  stood here and taught the opposite: until `0246f0d` the received "paused" called `Core.Logic.PauseGame()`
  on the passive machine, with the map tab opened to disguise it. That is **gone, removed and not commented
  out** (`PauseNotice.cs`, 1231 → 578 lines). The mod now **reads** `Core.Logic.IsPaused` (`:532`), sends it
  as a **flag** on the `Attack` channel — `[AttackType.PauseRelay = 203][1|0]`, two bytes (`:292-293`, the
  enum value at `PvP\Models\Enums.cs:152`) — and on the receiving side it only turns a sticky line on:
  `YOUR TEAMMATE IS PAUSED` (`:76`, `Arm` at `:389`). No `PauseGame`, no `ResumeGame`, no menu opened or
  closed, no `Time.timeScale` written — verified on the **deployed DLL** with Cecil
  (`scratch\cecil-pausa-il.ps1`: 16 methods with a body, 142 calls, **zero** towards
  `PauseGame`/`ResumeGame`/`set_TimeScale`/`HidePauseMenu`/`HideInventory`). The line clears itself when
  the flag comes back "no", or after **8 s** without a beat (`:88`, `CheckRemoteStillPaused` at `:478`);
  while the flag says "yes" the 3 s heartbeat re-sends it (`ObserveLocalFlag` at `:226`). ⚠️ **And the
  reason, measured:** the game pauses **by itself** for tutorials, dialogues and cutscenes (a system `input
  blocker`: `G2:1164` → `G2:1168` in `scratch\DIAGNOSI-PAUSA-BUG.md`), so a pause is **not a choice** of the
  player. Propagated as an order it froze the peer inside a tutorial with no menu open, and the resume was
  refused by an input blocker nobody ever cleared — 123 attempts in 2 s, then "ripresa FRENATA" and a game
  **frozen for good**. A pause that is not a decision cannot be an instruction.
- ⭐ **The time: with a server, a paused game does not stop the world — the menu opens and the world keeps
  running.** One `Postfix` on `LevelInitializer.Update()` (`TimeWhilePaused.cs:82-95`), i.e. on the method
  that copies `CurrentLevelConfig.TimeScale` into `Time.timeScale` **every frame** and is the only place in
  the decompiled game that writes it, re-writes `Time.timeScale = 1` **after** the copy when
  `CoopGate.IsMultiplayerActive()` is true and `UIController.Paused` (the real pause — it reads
  `TimeScaleReal`) says the game is paused (`:143-160`). `TimeScaleReal` is **never** written: the game must
  go on believing it is paused. ⭐ **And why, measured:** with `timeScale = 0` `FixedUpdate` stops, so the
  enemies **possessed by that machine** stop for the peer too, which sees puppets (0 `[EnemyTick]` in 30/30
  pause windows on the pausing side, while the probes that live on unscaled clocks keep going —
  `scratch\DIAGNOSI-CRASH-E-AI.md` §B). It covers **all seven** ways into the pause (ESC, map, inventory,
  tutorial, unlock, dialogues, console) without listing them, and in single player it does nothing: no
  write, no log. ⚠️ The price, declared: you can **die with a menu open**, which is why the death **closes
  the menus first** (`DeathMenuCloser.cs:70`, called from the prefix of `Penitent.OnUpdate`,
  `PlayerDeathSync.cs:399`, i.e. **before** `OnDead` reaches the game's death screen) — otherwise the
  `while (!Input.anyKey)` of `DeadScreenWidget` skips the death screen for whoever is holding a key.
- **Life guilt is a separate penalty on a separate channel.** Three fixed steps on `Life.MaxFactor`:
  0 deaths → 1.0, 1 → 0.8, 2 → 0.6, 3+ → 0.4 with the floor in code (`LifeGuilt.cs:84-90`). Reset **only**
  by sitting at a Prie-Dieu — never by the guilt fragment, never by the Confessor (`LifeGuilt.cs:781`);
  the counter survives closing the game because it is a file (`Modding\data\Multiplayer\life-guilt.txt`,
  `LifeGuilt.cs:105`, `:660`). The game's own guilt (`Fervour.MaxFactor`) is not touched anywhere.
- **273 Harmony targets.** 269 → 270 with the bonfire patch (`LifeGuilt.cs:781`), then the two
  `InstantiateArea` postfixes, which are **done** (`a188060`, one `Postfix` per method, both calling the
  relay's own `EffectRelay.OnPoolSpawn`; `EffectRelayInstantiateArea.cs`), then the time-while-paused
  patch: it declares its target in the **divided form** (`[HarmonyPatch(typeof(LevelInitializer))]` plus
  `[HarmonyPatch("Update")]`, `TimeWhilePaused.cs:82-83`) and is counted now that the gate sees both
  shapes — **272 in the normal form + 1 divided = 273**. Read the number from the tool, never from a note:
  `tools\verify-patch-targets.ps1` prints `bersagli controllati : 273`, type missing 0, method missing 0,
  `ESITO : tutti validi` — re-run today on the deployed build (`scratch\REFERTO-PEERID-E-BANCO.md` §②,
  `scratch\AUDIT-REFACTOR.md` §①). ⚠️ And the number is now **measured, not asserted**: before `a4ba597` the
  divided form was invisible to the gate, so `PatchAll` would have failed at runtime while the deploy said
  everything was fine.

### The wire, in three layers

Added by the network work (`a8b2bde`, `cd5f11f`, `22e1a9e`, `2a6ca17`, `e2fe745`): before it, the wire was
one socket in one file and a peer was a string. The reports are `scratch\REFERTO-C1A-IDENTITA.md`,
`REFERTO-C1B1-SEAM-SERVER.md`, `REFERTO-C1B2-P2P.md`, `REFERTO-FIX-INVITO-P2P.md`,
`REFERTO-TIMEOUT-E-UI.md`; the requirement list they come from is `docs\STEAM_INVITES.md` §9-§10 (in the
client project's `docs\` folder).

- ⭐ **The client: one interface, two wires.** `NetworkManager` speaks only to `ICoopTransport`
  (`Blasphemous.Multiplayer.Client\Network\ICoopTransport.cs`): `Connect` / `Disconnect` / `IsConnected` /
  `Send` plus three events. The contract is on **bytes**: `Send(byte[] framed, TransportClass cls)` gets the
  buffer already framed (`[ushort len][byte type][payload]`, protocol 11) and the transport must not
  interpret, split or re-frame it. Two implementations: **`TcpTransport`** (the IP path —
  `SimpleTcpClient`, and the `NoDelay` that used to pierce the abstraction now lives inside it) and
  **`SteamP2PTransport`** (the P2P path, whose Steamworks calls are all inside `SteamP2PTransportImpl`).
  `NetworkManager.CreateTransport` chooses on the **"Server IP" field**: the literal `p2p` ⇒ P2P, anything
  else ⇒ TCP — so the IP path stays the default and no flag has to be added. ⭐ And the Steam **invite
  writes that token** (`ConnectionDisplay.ApplySteamJoin`), so *accepting an invite is what selects the
  P2P*; before `2a6ca17` it wrote the host's **LAN address** there, and the invite died on a private IP.
- ⭐ **The server: one interface, N wires, one map.** `IServerTransport`
  (`Blasphemous.Multiplayer.Server\IServerTransport.cs`) = `Start` / `Stop` / `Send(PeerId, frame)` /
  listening addresses / `DelayDisabled` plus `PeerConnected`, `PeerDisconnected`, `FrameReceived`. Three
  implementations: **`TcpServerTransport`** (the listener — today's `SimpleTcpServer`, the same lines moved),
  **`P2PServerTransport`** (the host-side adapter: it has **no listener**, it polls
  `IsP2PPacketAvailable` on the three channels and accepts a session only for a **member of the lobby**;
  it also keeps the `[origin]` of a relayed packet, or every player would collapse into one stream), and
  **`CompositeServerTransport`**, which holds N transports and a
  `Dictionary<PeerId, IServerTransport>` — **peer → transport** — so `Server` sees one interface and does not
  know there can be two. A transport must announce a peer **before** delivering its first frame.
  ⭐ **And the host stays a TCP client on the loopback**: his own game connects to `127.0.0.1:27051` with
  `TcpTransport`, exactly as before. `SendP2PPacket(ownSteamID)` would be UDP loopback negotiating with
  itself, so the host's own traffic never goes through Steam — the P2P work does not touch that path.
- ⭐ **The identity: a value, not a string.** `PeerId` (`Blasphemous.Multiplayer.Common\PeerId.cs`, a
  `struct` in a **multi-target** project `net35;net8.0`): `PeerKind` (`Ip` | `Steam`) + the IP string + a
  bare `ulong` for the SteamID. The transport is the only one that knows how a peer is called on its wire
  and builds the value (`TcpServerTransport` → `PeerId.FromIp(e.ip)`); from there on the server speaks
  `PeerId`, and for the IP case `ToString()` is **the same string as before**, so the log lines and the
  socket lookup are byte-identical (the string's own hash is kept on purpose, to keep the dictionary's
  enumeration order). ⇒ **No `Steamworks` type outside the `*Impl`** (verified with Cecil on the deployed
  DLL: the types naming Steamworks are all `*Impl`, or the compiler-generated closures inside one, «FUORI»
  = 0). **And the reason is not hygiene**: on a
  non-Steam copy (GOG & co.) those types may not exist, and a missing type is not "a feature that is
  absent" but a **`TypeLoadException` raised when the JIT compiles the method that names it**, i.e. at the
  **call site**. That is why the façade (`SteamLobby`, `SteamP2PTransport`, `P2PServerTransport`) names no
  Steam type, asks **by reflection** whether the Steam type exists (`SteamLobby.Available`), and calls the
  implementation from a method of its own: with no Steam the implementation is never touched, so its JIT
  never runs. Without Steam the mod works as it did, over the IP.
- ⭐ **The three P2P channels** (`Network\P2PWire.cs`; the classification is `docs\STEAM_INVITES.md` §10):
  **0** = `k_EP2PSendUnreliableNoDelay` — positions, orientation, animation clip, enemy snapshots, effects,
  ping; **1** = `k_EP2PSendReliable` — every atomic event and durable fact (deaths, bosses, damage, scene,
  team, progress, intro, pause, the three latched STOPs); **2** = `k_EP2PSendReliable` but **isolated** —
  skins and heavy payloads, so a PNG cannot delay a death and a death cannot queue behind a PNG. Channel 0
  is the one that needs help: unreliable does not order, so each packet carries `[origin 8][seq 4]` and the
  receiver keeps the last accepted sequence **per sender** and drops older ones — otherwise the last
  *arrival* wins instead of the last *sample*, and a position goes out up to **60 Hz** (every frame in which
  the player moved more than 0.03, plus a 5 Hz heartbeat when standing still). ⭐ **Which channel is decided
  by whoever builds the payload**, not by the message type: `NetworkType.Attack` is a multiplexer (five
  messages, two classes), `Effect` has two (29 cosmetic vs the three latched STOPs), and `Animation` is one
  **reliable byte (death) inside an unreliable message** — so the class travels as an argument
  (`TransportClass`), and `TcpTransport` **ignores it on purpose**: one ordered reliable stream has no
  channel and not one byte changes. The 3-byte prefix and the 12-byte header are the **transport's** format,
  never the protocol's: the protocol stays 11 and the server never sees them.
- ⭐ **The heartbeat and the two thresholds** (`SteamP2PTransportImpl`): one explicit state —
  `Connecting` → `Connected` → `Disconnected` (`:174-184`) — and **one threshold per state**. `Connecting`:
  **25 s** measured from `Begin` without **ever** a packet received ⇒ `handshake scaduto (25 s)`
  (`HANDSHAKE_TIMEOUT_SECONDS`, `:119`). `Connected`: the phase changes on the **first valid packet
  received** (and says so: `primo pacchetto (canale N) dopo Xs`, then `connesso (handshake Xs)`) ⇒ from that
  instant **15 s** of silence is a lost link (`KEEPALIVE_TIMEOUT_SECONDS`, `:136`). Why 25 and not 15: the
  legacy first negotiation takes **3-7 s**, with spikes of **10-15 s** when the punch-through fails and the
  traffic has to go through the **relay**; and the hello is **not lost** — Steam holds it in a transition
  buffer (~15-20 s) and delivers it on the `Accept`, so aborting early destroyed precisely the mechanism
  that was about to work. ⭐ **Why the heartbeat tolerance is 15 s and no longer 5** (`6c06039`): the
  heartbeat is one packet every **3 s** (`KEEPALIVE_SECONDS`, `:139`), so 5 s meant that **one** lost beat
  declared a live link dead — and the measured cause of those drops was a **4-5 s stall of the game**
  (`fps=0.2`, `frameMs 4190` and `4813`), not a dead network. 15 s = **five** beats, so four consecutive
  losses are tolerated. The **same 15 s** on the host side (`P2PServerTransportImpl.TIMEOUT_SECONDS`, `:62`):
  **the two thresholds must stay equal**, or the shorter side closes first and the other reads it as "the
  host hung up". `P2PSessionConnectFail_t` is not a substitute — it arrives **10-15 s** late and only on the
  side that has packets queued — hence the application heartbeat.
- ⭐ **The reconnection: a session that is accepted revives the transport** (`694f16d`). A
  `P2PSessionRequest_t` from the host that we accept now calls `ReviveLink` (`:1187`) with a **targeted**
  reset — `_linkLost = false`, the **hello re-sent**, the phase back to `Connecting` (so the 25 s count from
  *that* instant, `:1203-1204`), and **never `End()`**, which would close the session Steam has just
  re-accepted and wipe `_host`/`_lobby`. And the phase checks run **before every return** of `Tick`
  (`CheckPhase`, `:497`, called at `:452`): a deadline can no longer be skipped by an early return, so every
  failure **says so** (marker + notification) instead of going silent. ⭐ **The two gates that stayed closed
  after a drop:** the peer's `_joinLobby` — left set by the timeout, it made `StartJoin` discard **every**
  invite (`[Steam] join: richiesta lobby=… ignorata: c'e' gia' un ingresso in corso`) — is now reset by
  `SteamLobbyImpl.ResetJoinAfterLinkLost` (`:1300`), which clears the **gate** and not the **lobby** (leaving
  the host's lobby would make this player a non-member and the host would refuse the return session; the
  memory `_lastJoinLobby` stays, so a manual `p2p` connect still works). The host's `IsConnectedNow()`
  (`:1047`) **stays, and was not a defect**: on the host it reads the **loopback** client towards its own
  server, so discarding an invite there is correct — the invite is the **peer's**, and the host's server,
  still listening with the adapter armed, accepts its P2P session as always
  (`P2PServerTransportImpl.cs:638-641`). What *was* a defect is the `Disconnect()` on that loopback, added by
  `6c06039` and removed by `074c4e0`: it threw the **host** out of its own server, and "Host session" then
  answered "porta occupata" — the peer came back, the host did not (`scratch\REFERTO-FIX-HOST-RIENTRO.md`).
- 🔴 **The exit crash: `CloseP2PSessionWithUser` never runs during the shutdown** (`2ac4f1a`). The measured
  crash was a **native access violation in `steam_api64`**, and the stack was `Core.OnDestroy` →
  `ModLoader.Dispose` → `Multiplayer.OnDispose` → `HostSession.Stop` → `Server.Stop` →
  `CompositeServerTransport.stopAll` → `P2PServerTransportImpl.Stop` → `CloseP2PSessionWithUser`: a native
  call on a module already in teardown. The host died on exit and the guest did not, because only one of the
  two `Close` paths was reached (`scratch\DIAGNOSI-CRASH-E-AI.md` §A). The rule is now a **gate**:
  `P2PShutdownGate.Enter()` is the **first** line of `Multiplayer.OnDispose` (`Multiplayer.cs:129`, i.e.
  **before** `HostSession.Stop()`), the flag is `volatile` (`Network\P2PShutdownGate.cs:47`, `Enter :53`,
  `Active :69`), and it is read where a `Close` can still fire in shutdown — `SteamP2PTransportImpl.End`
  (`:363`, and in IL that branch is a **jump**, not a convention). The `Close` in
  `P2PServerTransportImpl.Stop` (`:230`) was **not** put behind the gate: it was **removed** — `Stop` now only
  forgets the peers. In shutdown **the process closes the session**: Steam closes P2P sessions when the
  process dies, which is the limit already declared. ⛔ The **gameplay** `Close`s all stay: `Drop` (peer
  lost), `LinkLost`, `HandshakeExpired`, the "Disconnect" button.
- ⭐ **The protocol number lives in one place — `Common` — and it stays 11.** `Protocol.PROTOCOL_VERSION`
  (`Blasphemous.Multiplayer.Common\Protocol.cs:24`, a **`byte`**, the type of the byte `Intro` writes on the
  wire) replaces the two `const byte PROTOCOL_VERSION = 11` that sat in `Server.cs` and `NetworkManager.cs`
  and had to match on two different machines; `Protocol.VERSION` — a `const int 0` with **zero** occurrences
  in the whole repository — is gone, and the place that was meant for it now holds the living number (uses:
  `Server.cs:1000`, `NetworkManager.cs:630`). The value did **not** move to 12: this round changed
  validation, not the wire — no new `NetworkType`, no new field, no different byte, and the 42 frames are the
  proof. ⚠️ **And the `const` is inlined**: the 11 stays inside the IL of *both* DLLs, and `PROTOCOL_VERSION`
  is absent from their metadata (present in `Common.dll` only) — so the single definition removes the
  divergence **in the source**, not between binaries, and **the three DLLs must still be deployed together**
  (`scratch\AUDIT-REFACTOR.md` §①; `tools\deploy-local.ps1:33-36`, `:57-61`).
- ⭐⭐ **The golden rule: the TCP works always, everything else is additive.** The IP path is the base and
  the regression test: the framing harness over the TCP wire must stay **byte-identical** (42 frames,
  `sha256 9ecdfa42…`) every time the wire is touched, and it has, in every deploy of this round that touched
  the client or the server. Steam is a set of **additions** — lobby, invites, the P2P transport, the probe —
  each behind its own gate, each silent when Steam, the lobby or the library is absent. If you add a wire, add
  a transport; do not put a branch in the protocol.

---

## 3. What is implemented and verified

"Verified" here means: exercised on two machines, or measured in a log. Where the evidence is only code or
the deployed DLL it says so; anything implemented but **not observed in game** is in §8, not here.

- **Shared death** — death is announced when it happens (`pdead|`, `PlayerDeathSync.cs:66`, with `palive|`
  coupled at `:72`), so every machine knows who is dead and who came back.
- **Corpse** — the dead player's body is a frozen clone moved to follow the living peer's avatar
  (`Players\DeathCorpseFollow.cs:12-21`, `:49-56`); the corpse window closes with the respawn
  (`docs\BUG-NOTI.md:191-196`).
- **Camera** — never written. The camera is bound once per level to `Core.Logic.Penitent.transform`
  (`DeathCorpseFollow.cs:16-21`), so moving the body is enough; no camera target is added or removed.
- **Boss — aim and jump** — `ElderBrother`'s `GetTargetPredictedPos` returns the proxy position when the
  boss's `Target` *is* the proxy, and the original value bit-for-bit otherwise
  (`BossJumpAimPatch.cs:53-54`, markers `[SaltoProxy]`).
- **Boss — recited death** — only for 5 boss types (`EnemySyncManager.cs:398-406`), exercised on
  `ElderBrother`.
- **Effects** — the relay, the four resolution sources, and the two `InstantiateArea` postfixes, implemented
  and inside the deployed DLL (`a188060`). In game: 88 effects sent, 88 instantiated, perfect pairing; and
  the magic-damage proof that the relayed copies are what hurt (`scratch\CONFERMA-RELAY.md` §2). *The two
  `InstantiateArea` hooks are **not yet exercised in game**: that is an **observable**, listed in §8, not a
  limitation — the marker `[FxRelay] hook InstantiateArea: prefab=… da=BossJump|BossTeleport` says they fire,
  and `RISOLTO` on the peer **with** `istanzio` says the area travelled.*
- **Peer damage** — the enemy channel applies damage on the receiving machine with the game's own
  `Penitent.Damage(Hit)`; measured in a log (`[DannoPeer] APPLICATO`, `scratch\REFERTO-DANNO-AL-PEER.md` §5).
- **The pause notice** — the flag on channel 203, the sticky line, and the **absence** of any pause action:
  verified by **construction** on the deployed DLL (Cecil: zero calls from `PauseNotice` to
  `PauseGame`/`ResumeGame`/`set_TimeScale`/`HidePauseMenu`/`HideInventory` — `scratch\cecil-pausa-il.ps1`)
  and by **content** (the markers of the removed code are **absent** from the DLL: `RX fronte=`,
  `ho chiuso e ripreso`, `ripresa FRENATA`, `ho aperto la MAPPA`). The line **on screen** has not been
  re-observed since: §8. ⚠️ The old "shared pause — measured, with the map opening on the passive"
  (`scratch\REFERTO-PAUSA-CONDIVISA.md`) described code that **no longer exists**: that document is history,
  not the state.
- **The time** — the `LevelInitializer.Update` postfix is in the deployed DLL (marker `[Tempo]`), the target
  was resolved independently with Cecil (`scratch\cecil-tempo-il.ps1` finds
  `Tools.Level.Layout.LevelInitializer::Update` and the postfix declared on it), the **divided form** of the
  attribute is exercised on **HarmonyX 2.7.0** (`scratch\armonia-merge\`: `Update chiamato: 2`, `postfix
  eseguiti: 2`), and the IL contains **one** write of `Time.timeScale` (`ldc.r4 1`) gated on `CoopGate` +
  `UIController.Paused`, with **zero** writes of `TimeScaleReal`. The world running in pause, and the death
  screen arriving after the menus closed, are **not** observed in game: §8.
- **The fan-out of the server** — proven on the headless bench with **three peers**, i.e. with **N ≥ 2**
  destinations: **12/12** assertions pass, including the two that distinguish **who** and **how many**
  (a `Position` reaching **two** peers with the same payload, and the scene filter keeping the third out) —
  `tools\run-headless.ps1`, `scratch\REFERTO-PEERID-E-BANCO.md` §③. Before the third peer, every fan-out was
  exercised with **N = 1**, so an extraction that stopped at the first recipient would have passed identically
  (`scratch\RICERCA-FANOUT.md`).
- **Life guilt** — the three steps, the persisted counter and the separation from the game's guilt
  fragment verified in game by the user (`scratch\REFERTO-COLPA-VITA.md`); the bonfire reset is local by design
  (whoever sits gets it, `LifeGuilt.cs:48-51`).
- **Warnings** — "wait for your teammates" on dying inside a bossfight, and "do not press" windowed on
  the **cutscene**, not on the boss's death (`BossDeathWarning.cs:34-38`; `BossFightDeathWarning.cs:145-179`).
  The window arithmetic is measured; the text on screen has not been re-observed since.
- **All of it off in single player** — and the reverse side, which is the point: the death screen is the
  game's own, opaque one (`DeathScreenTransparency.cs:120`), life is full (`LifeGuilt.cs:157`), and the
  team notice does not fire because there are no teammates (`BossFightDeathWarning.cs:168-169`).
- **The Steam invite and the P2P wire** — the client seam, the server seam and the new identity (§2, "The
  wire, in three layers") are implemented and deployed, and verified where a wire can be verified without
  two players: the 42-frame TCP harness stays **byte-identical** (`sha256 9ecdfa42…`), the deploy still
  reports **273** targets with 0 missing, and Cecil on the deployed DLL finds **zero** `Steamworks` outside
  the `*Impl`. **In game**: one two-machine session ran over the P2P — `[Net] p2p: connesso (handshake
  5.4s)`, about two minutes of link, positions, animations and damage travelling on `[Net] p2p: rx`,
  no drop, no re-handshake, no exception; no boss was alive during it (`scratch\CONFERMA-P2P.md`). And the
  P2P crosses NAT where the TCP cannot: on a phone hotspot, **15** probe measurements all `relay=0
  via=diretto err=0`, bytes in **both** directions, while the TCP dial to the host's private address was
  refused (`scratch\ESITO-SONDA-P2P.md`). The end-to-end attempt that failed before the timeout fix — hello
  accepted, then 5 s of silence on both sides and the screen stuck on *Connecting…* — is
  `scratch\DIAGNOSI-P2P-FALLITO.md`; that is what `e2fe745` closed.
  *Not measured*: whether those sessions went **direct** or through the **relay** (the probe was not armed
  in the game logs), and the relay under the game's AppID — the open question of `docs\STEAM_INVITES.md` §8.

---

## 4. Known limitations

Declared, not open investigations. The full text — symptoms, log signatures, what to collect — is
`docs\BUG-NOTI.md`, one entry per item; the numbers below are its entry numbers. **None of these is a
regression**: if a player reports one, it is already written down.

- **Four bosses have no classic death flag** — `Amanecidas`, `Perpetua`, `PontiffHusk`, `Viridiana`
  (BUG-NOTI §9). They are DLC/extra content: their state is local or lives in separate sub-systems. The
  mod propagates a **kill** (life reaching 0), not `Status.Dead`, so the channel may already cover them —
  unknown, and one run decides it (§8).
- **23 boss behaviours out of 24 still aim at the local player** (BUG-NOTI §8A): their aiming reads
  `Core.Logic.Penitent` directly — 233 sites in `Gameplay.GameControllers.Bosses.*` — so on the machine that
  simulates the boss it ignores the peer standing next to it. Only `ElderBrother` goes through the proxy.
  Per-boss work, not one fix.
- **12 of 17 boss types never recite their death on the dead player's machine** (BUG-NOTI §8B): their
  `Death()` touches the `Penitent`, the input, the camera or the room geometry, so the whitelist excludes
  them on purpose (the reasons are in `EnemySyncManager.cs:377-397`, the list at `:398-406`).
- **Raw `Object.Instantiate` sites inside bosses remain outside the relay** (~18 of them, `BUG-NOTI.md` voce 7):
  they never pass through `PoolManager`, so the hook cannot see them; the signal is silence, not an error.
  **The two `InstantiateArea` of `BossJumpAttack` / `BossTeleportAttack` are no longer among them**: they
  are hooked since `a188060` (two of the 273 targets, §2) and only their **run in game** is missing (§8).
- **Cosmetic effects are not relayed by design.** The sender filters on `Weapon`/`Projectile`
  (`EffectRelay.cs:1429`): a purely cosmetic prefab is dropped — the hook fires and the packet does not.
- **Audio is a separate channel**, outside the relay (`EffectRelay.cs:64`): sounds travel on the effect
  channel at player level only.
- **No latency compensation.** The peer is drawn where its last packet put it
  (`Players\OtherPlayerScript.cs:773-777`) while enemies are smoothed (`EnemySyncManager.cs:4547`); the
  relay is additionally rate-limited to 20 sends/s (`EffectRelay.cs:154`), and a braked effect is simply
  not seen.
- **The same attack lands ~14 units apart on the two machines** (`scratch\CONFERMA-RELAY.md` §5.1): not
  reported as a problem by the user, but unexplained, and it is **upstream** of the dedup — no time window
  would merge them.
- **PE02 / Demake and the single-player death screen are implemented but unobserved**
  (`scratch\REFERTO-COLPA-PE02.md`, §6) — see §8.
- **There is no automatic reconnection** (`scratch\REFERTO-FIX-RICONNESSIONE-2.md` §5). After a drop the
  link has to be invited again (or "Host session" / Connect pressed): the fix guarantees that the gate no
  longer blocks the invite and that a refusal is **visible**, not that the session reopens by itself. And a
  **silence longer than 15 s** — a dead tunnel, or a stall of the game longer than five beats — still kills
  the link: the tolerance was widened to what a hitch needs, not to infinity.
- **The P2P sessions at shutdown are left to the process** (`scratch\REFERTO-FIX-CRASH.md` §6). `Stop` does
  not close them any more, so the sessions of a host who **leaves the session without closing the game** stay
  open until the peer times out or the process dies. The `BYE` still goes out on exit (it is a `Send`, not a
  `Close`), and `SteamLobby.LeaveLobby()` still runs after `HostSession.Stop()` — if a crash ever showed up
  inside one of those, the same rule has to be extended to it.
- **What the time patch does not fix, and the risks it declares** (`scratch\REFERTO-FIX-TEMPO.md` §8):
  `IsSlowMode()` becomes false while `Paused` is true, so one of the three "am I paused" guards
  (`UIController.cs:386-393`) falls and a second widget could open on top of the menu — **not measured**;
  gravity is **not** disabled while paused, so a player who pauses in mid-air could fall — **not measured**;
  the audio of a world that now runs is heard (`LogicManager.PauseGame` only pauses the SFX bus, `:251` in the
  decompiled game). And the death can also happen with a menu open, which is why the menus are closed at the
  instant of death — a key **held** when the `while (!Input.anyKey)` is reached can still skip the screen
  (~6.3 s after the death).

The other declared items — the execution freeze that could not be reproduced, the tutorial rooms, the save
folder when Steam is not running, the crossed execution, the dead player leaving the bossroom, the 1.5 s
skip window, the late joiner — are BUG-NOTI §1-§6 and §10. §5 below explains the ones that are **decisions**.

---

## 5. Design decisions — and why

This is the part that gets re-litigated if it is not written down. Every line is a decision already taken:
re-opening one needs a new measurement, not a new opinion.

- **F2 was reverted** (`scratch\REFERTO-REVERT-F2.md`). Binding the boss to a single owner made it single-target —
  worse for the other player than the flicker it removed. The ping-pong is back **by choice**, and it is the
  price the user judged acceptable.
- **The 1.5 s skip window is not fixed, and will not be fixed with a timer** (BUG-NOTI §6). Pressing to
  skip the boss cutscene less than ~1.5 s before its end can kill the respawn: one `currentTween` in
  `FadeWidget`, and the second fade kills the first callback. The only proposed cure — an
  automatic `Respawn()` on a timer — **would skip cut-scenes**, the same damage the project spent weeks
  removing. There is already an exit, and it is the game's own: enter and leave a level.
- **The boss ceremony is not skipped, because it cannot be done from C#** (`scratch\STUDIO-VIA-BOSS-E-TRIGGER.md` §2).
  `BossBlock` has **0 occurrences** in `Assembly-CSharp` — it is a string set by a Playmaker FSM inside the
  scene asset — and `onStartBossFight.Invoke()` is the **last line** of `StartBossFight`, after seven steps,
  with zero C# subscribers. Touching it risks the only failure worse than a desynced boss: a blocked room.
- **The server has no memory**. Team slots are mutable: the same slot, on
  different days, holds different players with different saves, so a server-side archive would attribute to
  one save the content of another. A lost flag is rebuilt at the next connection; a wrong flag is not.
  Removing a team's data when it empties is **the rule**, not a defect.
- **`_AC43` is not a bug.** The game writes `D17Z01S11_BOSSDEAD_AC43` — the achievement counter — not
  `<room>_BOSSDEAD` (`scratch\STUDIO-FLAG-AC43.md`); 11 of 12 rooms do have a `<room>_BOSSDEAD` flag, which
  is why the zone flag is the branch that actually fires here (`scratch\STUDIO-FLAG-AC43.md` §1.4).
- **The guilt band on the health bar was attempted and reverted** (`scratch\REFERTO-REVERT-BANDA.md`). With the
  penalty at 0.8 the bar keeps its width and fills to 80%: it does not shorten, because its width comes
  from `Final`, which ignores `MaxFactor` (`PlayerDeathSync.cs:293-295`), and the overlay was invisible in
  game. A text notification states the real percentage instead — the briars were a whim, not a requirement.
- **`tools\verify-deploy.ps1` was deleted** (`RIPRISTINO.md`, "Come si torna indietro"). It printed "you can test" on a
  stale DLL and everyone used it precisely because it was reassuring. The gate is now
  `tools\marker-check.ps1`, which reads the **bytes** of the deployed DLL and can say NO.
- 🔴 **The pause is not shared, and will not be shared again** (`0246f0d`, `scratch\REFERTO-FIX-PAUSA.md`).
  The reason is not taste: the game puts itself in pause for tutorials, dialogues and cutscenes, so a pause
  is **not a decision of the player** — and a thing that is not a decision cannot travel as an order. The
  measured price of doing it anyway was a peer frozen inside a tutorial with no menu open, a resume refused
  by a system input blocker, 123 retries in 2 s and a game frozen for good. What the mod does instead is
  **inform**: a flag on channel 203, and a sticky line on the other machine. Re-opening this needs a new
  measurement, not an opinion — and it would bring back the whole class of failures the removal closed.
- **The time is patched on `LevelInitializer.Update()`, not on `PauseGame()`** (`e77d48d`,
  `scratch\REFERTO-FIX-TEMPO.md` §2). `LevelInitializer.Update` is the only place in the decompiled game that
  writes `Time.timeScale`, it runs every frame and it is the **last** to write; a postfix on `PauseGame()`
  would be overwritten on the next frame. One patch, on the copy itself, covers **all seven** ways into the
  pause without listing them. And `TimeScaleReal` is never written: the game must keep **believing** it is
  paused, or the guards that read the pause open and a second widget lands on top of the menu.
- **`team` out of range is normalized, not refused** (`e3b2049`, `scratch\REFERTO-DUE-DIFESE.md` §②). Refusing
  would have meant a new `RefusalType` — i.e. a protocol change this round does not make — or reusing one
  that speaks of something else (`Protocol`, `DuplicateName`), and a log that says a false thing is worse
  than a log that says what happened. Normalizing keeps the player in the game and the server from learning
  a key it would never clean.
- 🔴 **The `Close` P2P never runs during the shutdown** (`2ac4f1a`, `scratch\REFERTO-FIX-CRASH.md` §2). In
  shutdown the session is the **process's** business: Steam closes it when the process dies. The gate exists
  because the peer side still has a closing path (`End()`), and closing a session is not an operation that
  can be done "half way"; the server side did not get a gate — its `Close` was **removed**. Distinguishing
  the two `Stop` cases with a gate would have left the **same native call** in the shutdown path, i.e. the
  risk with one `if` more.
- **`build.yml` was removed** (`9e75a0b`). It sat in the **nested** folder, it was **never executed**, and
  there is **no remote** (`git remote -v` is empty): a dead configuration that would only have come alive
  with the flattening of the repository, i.e. at the worst possible moment. Re-adding CI is a decision to be
  taken when there is something to run it on, not a file to keep warm.

---

## 6. Traps — read before you build

- 🔴 **`deploy-local.ps1` copies from `bin\Debug`** (`tools\deploy-local.ps1:51`, `:57`). A `Release`
  build followed by a deploy ships the **old Debug DLL** — and the deploy still prints a healthy target
  count, because that gate checks the **names** of the patch targets, not their content
  (`tools\verify-patch-targets.ps1`). The gate is **mandatory** now and is checked **before** copying
  (`tools\deploy-local.ps1:27-30`), but it is still a **name** gate: content is `marker-check`'s job.
- 🔴 **The Harmony gate no longer lies — and each way it used to lie has its own sentence.**
  (1) **A crash is not a wrong target.** The probe declares its outcome on its **last line**
  (`SONDA-ESITO: <esito>`) and the script believes the exit code **only** when the mark matches: an
  unhandled exception exits 4 + `ERRORE`, a code without its mark is a **crash** — before `c1381a8` any exit
  other than 0/3 fell into "un nome in `[HarmonyPatch]` e' sbagliato", i.e. it accused the patches exactly
  when the probe could not read the DLL (a corrupted DLL → `BadImageFormatException` → exit -532462766)
  (`tools\verify-patch-targets.ps1:38-63`, `tools\verify-patch-targets\Program.cs:72-90`).
  (2) **A stale probe is not green.** The working folder is **per `$PID`** and is **wiped** before copying,
  because `Copy-Item` keeps timestamps and `dotnet build` does not recompile a `probe.dll` newer than the
  `Program.cs` it finds: measured, a probe that crashes, left in the folder of a failed run, printed
  `273 / tutti validi` and the gate exited **0** (`scratch\REFERTO-GATE-2.md` §4).
  (3) **The probe is in the repo** — `tools\verify-patch-targets\` (`Program.cs` + `probe.csproj`, tracked):
  before they sat in `D:\Codificando\scratch\`, and on a clean clone the gate could not run and exited 1 =
  "targets do not exist", the most alarming diagnosis possible for a setup problem.
  (4) **Both shapes of the attribute are counted** (§2, the bullet with the count): the divided form is
  invisible to a regex over `[HarmonyPatch(typeof(X), "name")]`, and an invisible target is a `PatchAll` that
  fails at runtime while the deploy says everything is fine.
- ⚠️ **The three DLLs are deployed together, or not at all**: `BlasClient.dll`, `Blasphemous.Multiplayer.Common.dll`
  and the server. `deploy-local.ps1` does all three from the same `bin\Debug` / `publish`
  (`tools\deploy-local.ps1:40-67`), and the reason is written in the script: `Common.dll` carries the
  protocol number and the `NetworkType` enum, and **`PROTOCOL_VERSION` is a `const`, so it is inlined** — a
  new client with an old `Common.dll` is the silent divergence the single definition cannot prevent (§2).
- ⚠️ **The bench runs the *deployed* server**, in a process of its own (a real console: `Core.Main` ends in
  `Console.ReadKey` and throws if the streams are redirected), and writes its artifacts to `-OutDir`. Nothing
  in the bench is tracked except the two scripts: the proof is the script, not the capture.
- 🔴 **Markers must be concatenated.** `$"{MARK} …"` compiles to `string.Format`, so the DLL contains
  `"{0} …"` and the byte check reports the marker as missing (`scratch\REFERTO-2-HOOK.md`; the rule in code:
  `EffectRelayInstantiateArea.cs:111-117`). If a marker "looks present" in the source and the check says
  ASSENTE, look at how the string is built before suspecting the deploy.
- ✅ **Verify with `tools\marker-check.ps1`**, which reads the **content** of the deployed DLL (UTF-16LE
  and UTF-8 on raw bytes) and exits 1 when a marker is absent (`tools\marker-check.ps1:81-82`). Never trust
  the deploy output, and never trust a hash alone.
- ⚠️ **G1 — the second machine — is deployed by hand**: `deploy-local.ps1` has one game folder as its
  default (`tools\deploy-local.ps1:15`), so the other machine gets the DLL copied manually. Declare which
  commits are inside the build you are testing.

---

## 7. How to build, deploy and verify

Three commands, in this order:

```powershell
cd 'Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main'
dotnet build .\Blasphemous.Multiplayer.sln        # the SOLUTION, not just the client

cd ..\..
.\tools\deploy-local.ps1                          # mod + Common.dll + server into the game folder

& '.\tools\marker-check.ps1' -Dll 'Modding\plugins\BlasClient.dll' -Marker '[FxRelay]','[ColpaVita]'
```

- The deploy runs the Harmony-target gate itself (`tools\deploy-local.ps1:88-96`) and **stops** if a target
  name does not exist: a wrong name in `[HarmonyPatch]` compiles, then aborts `PatchAll` at runtime and the
  symptom looks like "the mod logs nothing at all". Since `128e68e` the gate is required **before** anything
  is copied (`:27-30`) and its message no longer re-states a diagnosis the verifier has already denied.
- `marker-check` must exit **0** and print `ESITO: TUTTI i marker richiesti sono PRESENTI`. The `-Marker`
  array only works with the call operator `&`; with `-File` it takes a single value.

### The headless bench — the only proof that a fan-out reaches **two** peers

Two scripts, both tracked, both in `tools\`; the server must already be deployed (`SERVER-COOP`).

```powershell
pwsh -NoProfile -File .\tools\run-headless.ps1 `
     -ServerDir '<...>\SERVER-COOP' -OutDir '<scratch>\out-3peer' -Via dotnet

pwsh -NoProfile -File .\tools\compare-headless.ps1 `
     -Before '<scratch>\out-after-...' -After '<scratch>\out-3peer'
```

- It opens **real TCP sockets** to the **deployed** server and sends **real packets** in the
  `[ushort len][byte type][payload]` framing, capturing every byte the server answers.
- **Section A** is the golden wire: **42 frames**, `sha256 9ecdfa42…`, byte-identical to the baseline. It is
  the non-regression measure of the wire and it must not change when the wire is touched — it has not, in any
  commit of the round.
- **Section B** is the **three-peer** run (`127.0.0.1`, `.2`, `.3`): from there every fan-out has **N ≥ 2**
  destinations, and the last line is a verdict — `ESITO: 12/12 PASS`, exit 0, or exit 1 with the failed
  assertion named. An extraction that stops at the first recipient **does not pass any more**; before the
  third peer it would have passed identically (`scratch\RICERCA-FANOUT.md`, `scratch\REFERTO-PEERID-E-BANCO.md` §③).
- `-Via exe|dotnet` (default `dotnet`): the exe is unsigned and SmartScreen may hold it for minutes.

---

## 8. Implemented but not yet observed in game

Observables, not open defects: implemented and never yet seen happen. This is the list of measurements still
missing.

- **The 3 projectile enemies** — `BellGhostVariant`, `RangedBoomerang`, `MeltedLady`. Look for
  `[FxRelay] RISOLTO "<name>" da=nemici in=…ms` on the receiver, and the projectile alive on the peer; in
  the summary, `nemici:K` with K > 0. A `NON RISOLTO "<name>" provate: boss,nemici,penitente` means the
  prefab is in none of the three places — an answer, not a silence.
- **All bosses — the aim (23) and the recited death (12/17)**. Look for `[SaltoProxy] …` (it exists only for
  `ElderBrother`, by construction) and for `[BossDeath RX] … morte=recitata:<Type>` versus
  `morte=escluso:<Type>-non-in-whitelist`. `[BossDeath RX]` only appears on the **dead** player's machine.
- **The 4 DLC bosses** — kill one and look for `[EnemyKill] … MORTO … vita=0`; and check `Viridiana` both
  alive and dead (`docs\BUG-NOTI.md:613-624`). That log line is the measurement which closes BUG-NOTI §9.
- **PE02 and Demake** — the life guilt must **not** apply: `[ColpaVita] non applicata: modalita' PE02` /
  `modalita' Demake`, and the health orbs must stay the usual count (`scratch\REFERTO-COLPA-PE02.md` §7).
- **The ~14-unit double** — the same attack at mirrored positions on the two machines; look at both logs
  (`scratch\CONFERMA-RELAY.md` §5.1).
- **The two `InstantiateArea` postfixes** — **done since `a188060`**, never exercised in game *(an
  observable, not a limit)*: `[FxRelay] hook InstantiateArea: prefab=… da=BossJump|BossTeleport`:
  if `RISOLTO` on the peer comes **with** `istanzio` the area is relayed, if it comes **without** it the
  prefab is cosmetic and nothing was sent (`EffectRelayInstantiateArea.cs:48-53`).
- **The pause line, on screen** — `[Pausa] cancello APERTO` → `[Pausa] TX flag=pausa locale: si'` →
  on the other machine `[Pausa] RX flag=… avviso A SCHERMO "YOUR TEAMMATE IS PAUSED"` → `[Pausa] NOTIFICA
  TOLTA · motivo=…` (or `TIMEOUT di 8s` if the peer closed the game). What is **not** observed: that the line
  really appears and disappears at the right moments, and that **no** `PauseGame` line ever comes back. The
  absence of the call is proven on the deployed DLL (§3), not by the eye (`scratch\REFERTO-FIX-PAUSA.md` §6).
- **The world running in pause** — start a tutorial / dialogue / cutscene on one machine and look at the
  log `[Tempo] la pausa non ferma il mondo · in pausa il MONDO CORRE · TimeScaleReal(letto)=… · finestre=N`.
  The other player must keep playing; nobody freezes. In single player the line is `cancello CHIUSO`.
- **The death screen after the menus are closed** — die with a menu open: `[Morte] menu' chiusi PRIMA della
  schermata di morte · modo=HideInventory|HidePauseMenu`. The screen must be seen, not skipped
  (`scratch\REFERTO-FIX-TEMPO.md` §3-§4).
- **The reconnection, in game** — after a drop the chain the user must see is `[Net] p2p: link RITROVATO
  (…)` → `hello inviato a …` → `primo pacchetto (canale N) dopo Xs` → `connesso (handshake Xs)`, and on the
  host `[Net] p2p host: nessun peer connesso (…): … connesso=true` (the host never left its own server).
  The 15 s and the 25 s are proven by construction (the constant, the branch that reads it, the string in the
  DLL), not by a stopwatch (`scratch\REFERTO-FIX-RICONNESSIONE-2.md` §5).
- **The exit without crash** — close the game **as host**: the last lines of the log must be, in this order,
  `[Net] chiusura in corso: da qui in poi nessuna CloseP2PSessionWithUser` and then
  `[Net] p2p: adapter fermo peer=N - nessuna Close P2P da qui (…)`. Proven in IL (the branch is a jump), **not**
  on the field: if the first line is there and the game died anyway, the crash is no longer that `Close`
  (`scratch\REFERTO-FIX-CRASH.md` §6).
- **`[EnemyInert]`** — five lines that had never been written to **any** log, in **any** build, because they
  sat after a `return` (`CS0162`); reachable since `46e35fb` (`EnemySyncManager.cs:1355-1378`), counts
  **distinct ids** and prints once per growth, and `LevelLoaded` resets it. The first run that shows it is the
  measurement, and it answers a specific symptom: "an enemy that stands still and cannot be pushed" is
  **by construction** inert scenery (`IsInertScenery`) or it is another fault (`scratch\REFERTO-ENEMY-INERT.md`).
- **The server's two discards, in a live log** — `[Server] payload corto: tipo=… attesi=… ho=…` and
  `[Server] progresso scartato: …` only ever appear when something malformed actually arrives. The negative
  proofs exist on the bench (`scratch\REFERTO-DUE-DIFESE.md` §3.3-3.4, `scratch\REFERTO-TRE-PASSI.md` §1);
  in a normal session they are **silence**, and that is the correct outcome.
