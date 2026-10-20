# Steam invites – feasibility assessment

Status: **feasibility confirmed, not implemented.**

This document records what was verified directly against the game binaries installed on disk, and what
the remaining implementation work and constraints are. Nothing here changes the build.

## 1. What is already on disk

| Item | Value | How it was verified |
| ---- | ----- | ------------------- |
| Steam AppID | `774361` | `appmanifest_774361.acf` (`name = Blasphemous`) |
| Native Steam API | `Blasphemous_Data/Plugins/steam_api64.dll`, 244 KB | file listing |
| Steamworks SDK | 1.42, `Steamworks.NET Version: 12.0.0` | `Blasphemous_Data/Plugins/Steamworks.NET.txt` |
| Managed Steam API | `Assembly-CSharp-firstpass.dll` – **367 types in the `Steamworks` namespace** | Mono.Cecil over the shipped assembly |
| Steam bootstrap | `SteamManager : MonoBehaviour` in the firstpass assembly | Mono.Cecil |

`SteamManager` is already running the Steam API:

```
SteamAPI.Init()          called from  SteamManager.Awake
SteamAPI.RunCallbacks()  called from  SteamManager.Update
```

So the Steam API is initialised and its callback pump runs every frame. A mod does **not** need to
initialise Steam itself — it only needs to call the specific lobby/overlay functions.

## 2. The APIs needed for invites exist

All of the following are confirmed present in `Assembly-CSharp-firstpass.dll`:

```
SteamFriends.ActivateGameOverlayInviteDialog(CSteamID)   // the native Steam invite overlay
SteamFriends.ActivateGameOverlay(string)
SteamFriends.InviteUserToGame(CSteamID, string)

SteamMatchmaking.CreateLobby(ELobbyType, int)
SteamMatchmaking.JoinLobby(CSteamID)
SteamMatchmaking.LeaveLobby(CSteamID)
SteamMatchmaking.SetLobbyData(CSteamID, string, string)
SteamMatchmaking.GetLobbyData(CSteamID, string)
SteamMatchmaking.RequestLobbyData(CSteamID)
SteamMatchmaking.GetLobbyMemberByIndex(CSteamID, int)
SteamMatchmaking.SendLobbyChatMsg(CSteamID, byte[], int)

SteamAPI.Init() / RunCallbacks() / Shutdown()
```

Callback payloads are also present:

```
GameLobbyJoinRequested_t { CSteamID m_steamIDLobby; CSteamID m_steamIDFriend; }
LobbyCreated_t { EResult m_eResult; CSteamID m_ulSteamIDLobby; }
LobbyDataUpdate_t { CSteamID m_ulSteamIDLobby; CSteamID m_ulSteamIDMember; bool m_bSuccess; }
LobbyEnter_t { CSteamID m_ulSteamIDLobby; ... uint m_EChatRoomEnterResponse; }
LobbyInvite_t
```

This is the same mechanism Spacewar and other Steam titles use: create a lobby, open the invite
overlay, and react to `GameLobbyJoinRequested_t` when a friend accepts.

### 2.1 Join sequence — every step waits for a callback

The lobby API is asynchronous end to end: the answer to a call is not its return value, it is a
callback, and nothing that depends on a previous step is guaranteed to be readable in the instruction
right after it. The order is fixed:

```
CreateLobby(ELobbyType, maxMembers)              // host
  -> LobbyCreated_t { m_eResult, m_ulSteamIDLobby }
SetLobbyData(lobby, key, value)                  // host, one call per connection detail
  -> guest: RequestLobbyData(lobby)
  -> LobbyDataUpdate_t { m_ulSteamIDLobby, m_ulSteamIDMember, m_bSuccess }
GetLobbyData(lobby, key)                         // only now the value is there
JoinLobby(lobby)                                 // guest
  -> LobbyEnter_t { m_ulSteamIDLobby, m_EChatRoomEnterResponse }
```

Two consequences, and both of them are rules:

- **After `SetLobbyData`, read only after `LobbyDataUpdate_t` — never straight away.** The value is
  replicated by the Steam back end, so it is not on the other side when `SetLobbyData` returns: a
  `GetLobbyData` issued right after the set, or right after `JoinLobby`, can come back as the **empty
  string** and look exactly like a lobby with no data. The guest calls `RequestLobbyData` and does its
  work in `LobbyDataUpdate_t` (when `m_bSuccess` is true), not in `LobbyEnter_t`.
- **`LobbyEnter_t.m_EChatRoomEnterResponse` answers "did I actually get in".**
  `k_EChatRoomEnterResponseSuccess` is the only value that means the lobby is ours; after a failed enter
  there is no data to read and no chat to send, so the failure has to surface instead of turning into a
  silent empty lobby.

## 3. Verdict

**Feasible, and only via the legitimate route.**

`steam_api64.dll` is the official Steam client API and it is already loaded by the game. The invite
overlay, the lobby API and the callback pump are all present. There is no need for emulation, DRM
workarounds or cracked-build tooling — in fact the legitimate Steam path is the only one that *can*
work, because:

- lobby and overlay calls are serviced by the **running Steam client**, and the app must be launched
  through Steam with a valid AppID for that client to recognise it;
- under a Steam emulator or a non-Steam copy those calls are not backed by anything, so the invite
  overlay and the lobby API are unavailable.

There is therefore no "less legal alternative" to weigh: the official API is both the legal option and
the only functional one.

## 4. Sketched integration

1. **Host** creates a lobby (`CreateLobby`) — or reuses the one the game may already hold — and stores
   the connection details the mod already uses (server address, port, room, optional password) as
   lobby metadata via `SetLobbyData`.
2. **Host** opens the invite UI with `ActivateGameOverlayInviteDialog(<own steam id>)`.
3. **Invited player** — the path splits in two, and Steam picks which one runs:
   - **game not running**: Steam starts the game *itself* and puts **`+connect_lobby <lobby id>`** on the
     command line. No callback can reach a process that does not exist yet, so this argument is the only
     thing that arrives; the mod has to read the command line it was launched with and join that lobby.
     Nothing in the game handles it — the string `connect_lobby` does not appear at all in
     `Assembly-CSharp-firstpass.dll` — so the parsing is ours. This is the case the first version of
     this document missed.
   - **game already running**: the overlay delivers `GameLobbyJoinRequested_t { m_steamIDLobby,
     m_steamIDFriend }` to the running process; the mod calls `JoinLobby(m_steamIDLobby)` and waits for
     `LobbyEnter_t` (§2.1).
4. **Client** reads the lobby metadata — after `LobbyDataUpdate_t`, see §2.1 — and pre-fills the existing
   connection screen, so the join becomes one click instead of typing an IP.

**`LobbyInvite_t` is not a join.** It reports that an invite was *received*: it does not mean the invitee
accepted, and it is not a `LobbyEnter_t`. It is the right hook for a notification or a join button, but
the join itself stays `JoinLobby` -> `LobbyEnter_t`.

Note the mod's transport today is a direct TCP connection to a `SimpleTCP` server; Steam lobbies would
be used for *discovery and invitation*, not as the transport. That keeps the change additive: the
existing netcode is untouched, and the lobby is purely a rendezvous mechanism. What a *P2P* transport
would be worth on this SDK is the subject of §8.

## 5. What implementing it costs

- **Nothing has to be added to the build.** `Assembly-CSharp-firstpass.dll` — the assembly that carries
  the `Steamworks` types — is **already** a compile *and* runtime asset of the client project:
  `Blasphemous.Multiplayer.Client.csproj` references `Blasphemous.GameLibs` `4.0.67`, and the restore
  output (`obj/project.assets.json`, entry `Blasphemous.GameLibs/4.0.67`) lists
  `lib/net35/Assembly-CSharp-firstpass.dll` under both `"compile"` and `"runtime"`. So the lobby, overlay
  and networking calls are **typed at compile time**: no `HintPath`, no local pointer to the game
  install, no reflection. The CI build restores the same package from NuGet exactly as it already does
  for `Blasphemous.ModdingAPI`, which is why **the "reflection fallback so the CI keeps working" risk
  this section used to describe does not exist**.
  `PrivateAssets="All"` becomes necessary only in the one case where a `Steamworks.NET` package is
  *added on top*, next to the assembly the game already ships: it keeps that package's assets from
  propagating to whatever consumes the project (the same role it already plays for
  `Microsoft.NETFramework.ReferenceAssemblies` in this `.csproj`). With `Blasphemous.GameLibs` alone
  there is nothing to add and nothing to mark private.
- **Lobby lifetime**: the lobby must be kept alive and left on disconnect, and lobby creation must handle
  failure (Steam not running, offline mode).
- This work is **independent of the enemy-sync work** and can be done at any point; it is deliberately
  not started yet.

## 6. Constraints to be aware of

- Steam must be running and the app launched through Steam. This cannot be tested with a non-Steam copy,
  which is another reason the official route is the one to build.
- `ActivateGameOverlayInviteDialog` needs an active lobby to invite into; calling it without one does not
  show a usable dialog.
- The overlay is a Steam client feature; it behaves differently in offline mode.

## 7. Alternatives considered and rejected

| Alternative | Why rejected |
| ----------- | ------------ |
| Steam emulator / "cracked-style" invites | Not legal, and technically weaker: without the real Steam client the overlay and lobby APIs have no backing service, so they do not work at all. |
| Steam Remote Play Together | Legitimate and already available; it streams the host's screen and inputs, so it needs no netcode. It is a genuine option for playing the existing local co-op mod on two PCs, but it gives the guest the host's camera rather than a second viewpoint, so it is a different product decision rather than an invite feature. |
| Direct IP only (today's behaviour) | Works, but requires the guest to be told an address; this is what the invite flow would replace. |

## 8. What this SDK cannot do

The Steamworks build inside the game is SDK **1.42** / `Steamworks.NET 12.0.0`, and its peer-to-peer
surface is the *legacy* one. Three consequences, and one open question.

- **No modern P2P.** `SteamNetworkingSockets` does **not exist** in this build — zero occurrences in
  `Assembly-CSharp-firstpass.dll`, and `SteamNetworkingMessages` / `SteamNetworkingUtils` are absent too.
  There are no `ISteamNetworkingSockets` channels, no messages API, no SDR back end with its own
  encryption and relay guarantees. What *is* there is `SteamNetworking`, the legacy one:
  `SendP2PPacket` / `ReadP2PPacket` / `AcceptP2PSessionWithUser` / `GetP2PSessionState`, with the
  `P2PSessionRequest_t` and `P2PSessionConnectFail_t` callbacks.
- **Getting the modern API means replacing the game's Steam integration.** It would require shipping a
  newer `steam_api64.dll` than the game loads today, i.e. taking over the Steam initialisation that the
  game's `SteamManager` owns — a different project, not a feature of this mod. For the invite feature it
  is not needed anyway: lobbies and the overlay already exist (§2).
- **So the only P2P route is the legacy one**, with its limits: datagrams unreliable and unordered by
  default (a reliable channel has to be built on top by hand), and NAT traversal that falls back to
  Steam's relay when it fails.
- **Open question: does the relay work under an AppID that is not ours?** The relay is the fallback that
  makes P2P work outside a LAN, and whether AppID `774361` (the game's, not the mod's) is allowed to
  relay arbitrary traffic is **to be verified, not assumed**. It is the question that decides whether the
  mod works over the internet at all: **one test with two machines on two different networks closes it**
  — one host, one guest, no port forwarding, and see whether the session comes up and what
  `GetP2PSessionState` reports.

Until that test runs, the direct TCP transport remains the only transport known to work.

## 9. What a P2P transport would need

Written down before any code, from the legacy API of §8: what `SteamNetworking` gives for free, what it
does not, and the two holes it leaves in `ICoopTransport`. This is the bridge between the invite work
and a P2P transport; it is a requirement list, not an implementation.

**What the legacy P2P does give**

- **Delivery and order, on the reliable channel.** `k_EP2PSendReliable` is the reliable, ordered send
  option — the guarantee the TCP transport has today. ⚠️ This is documentation plus a plausible reading
  of the API, **not something measured on this build**: it is to be verified when the transport is built,
  not taken as a fact now.
- **Packets larger than ~1200 bytes.** Steam fragments and reassembles them itself, so `ReadP2PPacket`
  returns the whole packet. **Our `ushort` framing is therefore not needed on top** (and is already out
  of the seam's plan).

**What it does not give — and the trap**

- **The send buffer.** Send too fast, the buffer fills and **`SendP2PPacket` returns `false`** — silently,
  with no exception and no callback. **The return value has to be checked, always**: a discarded `false`
  is a position update that nobody ever reports.
- **The handshake is mandatory.** The receiver must catch **`P2PSessionRequest_t`** and call
  **`AcceptP2PSessionWithUser`**, or **every packet from that peer is discarded in silence**. With it,
  the security rule: **accept only SteamIDs that are members of the current lobby**.
- **Disconnection is not an event.** There is no immediate callback; `P2PSessionConnectFail_t` arrives
  after **~10-20 s**, which is far too long for a co-op that has to say "connection lost" the way it does
  now. So: an **application heartbeat — 1 byte every 2-3 s — with a ~5 s timeout**, and
  **`CloseP2PSessionWithUser` on the way out** to release the session.
- **Channels 0-255 are isolated**, so traffic on one does not queue behind another: **0** gameplay
  (positions, input), **1** heavy state (map change), **2** skins — a skin transfer then cannot make
  movement stutter.

**The consequence for the seam**

`ICoopTransport` today has `Connect` / `Disconnect` / `IsConnected` / `Send` / events. Steam P2P asks for
**two things more: a keepalive/timeout and a channel.** So the contract will have to be **extended in the
P2P step of the work; the TCP step does not implement either**, because TCP already has the timeout at
the socket level and has no channels.

## 10. Which messages go unreliable, and which go reliable

§9 says what a P2P transport needs. This section answers the one question that has to be answered
**before** any of it is written: of the **20 `Send*` verbs** in `NetworkManager.cs`, which ones may be
dropped by the wire and which ones may not. It is a classification, not an implementation.

⚠️ **The TCP transport does not change: this classification is for the P2P transport only.** Over TCP
there is one ordered reliable stream and none of it applies.

Why it cannot be skipped: on the legacy P2P, sending high-frequency data **reliable** fills the send
buffer (~1 MB) until **`SendP2PPacket` returns `false`, silently** (§9). At 60 Hz that is **under a
second**, with one visible hitch, and a `SendPosition` put on the reliable class **breaks the session**.

**The criterion, in two lines**

> A message is **reliable** when losing it leaves the state **wrong for good** — nothing later re-states
> it.
> A message is **unreliable** when the **next** message of the same kind **overwrites** it, so a loss
> costs one sample of truth, not the truth.

Frequency and size decide nothing, and the two intuitions fail in opposite directions right here:

- **rare but critical** — a death, a boss, a persistent object: **reliable**, however rarely it happens,
  because nothing will say it again (`EnemyDeathGlobal`, `Progress`, `EnemyKill`);
- **frequent but overwritable** — a position: **unreliable**, however many times per second it goes out,
  because every copy supersedes the last one (`Position`, `EnemyUpdate`).

Frequency decides the **channel**, not the class. That is the next heading, and it is not a style
choice.

### 10.1 Channels are assigned by class, not by topic

§9 proposed three channels by topic (0 gameplay, 1 heavy state, 2 skins). The number and the isolation
stay; the rule for **what goes where** does not, because of a property of the API:

- ⚠️ **order is not guaranteed between classes on the same channel** — Steam keeps separate queues per
  send class even inside one channel, so an **unreliable packet may overtake a reliable one**. Positions
  and damage events sharing a channel would mean *the position of the next frame arriving before the
  damage of the previous instant*;
- ⚠️ **the receiver cannot tell the class**: `IsP2PPacketAvailable` / `ReadP2PPacket` do not return the
  `EP2PSend` flag a packet was sent with. There is no way to demultiplex at the far end.

⇒ the class has to be encoded in **the channel, at the source**. One channel per class:

| Channel | Send option | Carries | Sequence number |
| ------- | ----------- | ------- | --------------- |
| **0** | `k_EP2PSendUnreliableNoDelay` | **every unreliable message**: positions, orientation, animation clip, enemy snapshots, effects, ping | **yes** (§10.4) |
| **1** | `k_EP2PSendReliable` | **every atomic event and every durable fact**: deaths, bosses, damage, scene changes, team, progress, intro, pause | no — Steam orders it |
| **2** | `k_EP2PSendReliable` | **skin and any other heavy payload** | no |

Two notes on the options themselves:

- ⭐ **`UnreliableNoDelay`, not `Unreliable`**: the latter batches (a Nagle of its own), which at 60 Hz
  shows up as an **elastic micro-jitter**, and this is an action game.
- ⚠️ channel 2 is **reliable but isolated** — so the layout is *class + isolation*, not class alone. That
  is deliberate: a PNG must not delay a death, and a death must not wait behind a PNG.
- ⚠️ The three rows are **documentation plus a plausible reading of the API, not measured on this build**
  — same status as §9's claims about delivery. Verify them when the transport is built.

### 10.2 The table

`file:line` references are to `Blasphemous.Multiplayer.Client/` and are from HEAD `a8b2bde`. "Payload" is
what the client hands to `QueueMesssage`, **before** the `[ushort len][byte type]` framing and before the
name the server puts in front.

| Message | Frequency — who calls it, how often | Payload | If lost | Class | Ch |
| ------- | ------------------------------------ | ------- | ------- | ----- | -- |
| **Position** `NetworkManager.cs:224` | `MainPlayerManager.Update`, every frame (`Multiplayer.cs:373`), sent when moved >0.03 **⇒ up to 60 Hz**; standing still, a heartbeat every 0.2 s **⇒ 5 Hz** (`MainPlayerManager.cs:28,229-236`) | 8 B | the peer keeps the previous point for ≤0.2 s; the next sample replaces it. The ownership rule stops trusting a sample older than 0.3 s (`EnemySyncManager.cs:4539`) and falls back to "keep it here" — conservative, not wrong | 🔵 | 0 |
| **Animation** `:243` | on every clip change, with a 0.5 s suppression after a special one (`MainPlayerManager.cs:31,243-247`); forced by `UseSpecialAnimation` (`:272-275`) — event-driven, several per second in a fight | 1 B | the avatar holds the previous clip until the next change: **the next change overwrites it** | 🔵 | 0 |
| ↳ **Animation — the death byte** `MainPlayerManager.cs:141` | **once per death**, before the corpse freeze | 1 B | the peer's corpse **stays standing for the whole death** — the "in piedi bloccato" defect — and the reliable death record does **not** repair it: `PlayerDeathSync.ApplyRemoteState` only sets a flag (`PlayerDeathSync.cs:196-203`), not the pose | 🟢 | 1 |
| **Direction** `:258` | only on a flip (`MainPlayerManager.cs:252-255`), plus the three-message burst of `SendAllLocationData` on scene enter / connect (`:264-269`) — a flip can be minutes apart | 1 B | the peer's avatar faces the wrong way until the next flip. The next flip overwrites: wrong **for a while**, not for good — the weakest call in this table, because this edge has no heartbeat (§10.4 folds it into the snapshot instead) | 🔵 | 0 |
| **EnterScene** `:273` | once per level load (`Multiplayer.cs:198`), and on connect (`:498`) — a handful per minute of play | 9-12 B | my scene stays the old one on the far side ⇒ **every scene-filtered stream** (positions, enemy updates, effects, attacks) is silently misrouted until the next room | 🟢 | 1 |
| **LeaveScene** `:288` | once per level unload (`Multiplayer.cs:228`) | 1 B | the peer keeps an avatar in the room I left, and the branch that removes it is never taken; nothing re-states it | 🟢 | 1 |
| **Skin** `:302` | on connect (`Multiplayer.cs:491`) and on every palette change (`GamePatches.cs:14-16`) — rare, plus **one heavy payload** | 1+~18 B, or **1+PNG (KBs)** via `EncodeToPNG` (`:326`) | the peer wears the default skin for the rest of the session; it is never re-sent. Also the only payload that can exceed one unreliable datagram (~1200 B) | 🟢 | **2** |
| **Team** `:363` | **once**, on connect (`Multiplayer.cs:492`) | 1 B | the peer's nameplate and map colour are wrong for the session (`RefreshPlayerColors` is local, `Multiplayer.cs:463`) | 🟢 | 1 |
| **Intro** `:411` | **once per connection**, before anything else (`NetworkManager.cs:96`) | 10-60 B | no handshake, no session, and nothing retries | 🟢 | 1 |
| **Progress** `:454` | event-driven from ~20 helpers (beads, relics, prayers, Mea Culpa, flags, map cells): human-paced, seconds apart; **plus a burst on connect** — one message per tracked entry (`ProgressManager.cs:151-161`, called from `Multiplayer.cs:199,500`) | 2+id ≈10-40 B | the peer's world never learns the fact and there is no re-state: `_sentAllProgress` is one-shot (`ProgressManager.cs:153-155`) | 🟢 | 1 |
| **Attack** (PvP hit) `:485` | per sword swing that lands on a player (`OtherPlayerScript.cs:1185`) — human-paced | ≈10-25 B | that damage never happens on the other machine; health is not a replicated value, so nothing re-states it | 🟢 | 1 |
| **AttackPayload** `:507` | **five different messages on one channel** — see §10.3 | 2-40 B | depends on the sub-message | **mixed** | **0 or 1** |
| **Effect** `:578,590` | **29 call sites** across `AnimationFixes.cs`, `ExecutionSync.cs` and `PvPPatches.cs` — one send per game event. The only one with a rate fuse is the footstep, min 0.08 s **⇒ ≤12.5 Hz** (`AnimationFixes.cs:463,482`); the rest are swings, dashes, parries, executions | 1 B, or 9 B with a position | one sound or particle is missing on the peer. Cosmetic, no durable state | 🔵 | 0 |
| ↳ **Effect — the three STOP edges** (`TrailOff` `AnimationFixes.cs:277,309,319,337`, `DashSoundStop` `:700`, `ParryHitStop` `:777`) | one per ended trail / dash / parry | 1 B | **the latched thing stays ON for ever**: a ghost trail or a looping dash sound — and the receiver has **no fuse**: the `PARRY_HIT_TIMEOUT` that used to expire a lost STOP was removed (`OtherPlayerAttackScript.cs:909-927`) | 🟢 | 1 |
| **Ping** `:627` | **1 Hz**, from `PingManager.OnUpdate` (`PingManager.cs:42-46`, `SEND_INTERVAL = 1` at `:50`) | 6 B | one RTT sample missing; the next second replaces it | 🔵 | 0 |
| **EnemyUpdate** `:662` | 20 Hz per pass (`SEND_INTERVAL = 0.05f`, `EnemySyncManager.cs:47`, gate `:5773-5776`), **one message per enemy this machine owns ⇒ 20 × (owned enemies) per second**; a full sweep resends every enemy once per second (`:5780`) | 30 B | the next sample is ≤50 ms away and the full sweep bounds the error to 1 s. This is the message the freeze in §9 is made of: 30 B × 20 Hz × N, all of it through the ~1 MB buffer | 🔵 | 0 |
| **EnemyDamageRequest** `:709` | per sword hit on an enemy this machine owns (`EnemyDamagePatch.cs:68,73`); the peer→host variant of the same event is capped at 5 Hz per enemy (`EnemyHitPeerSync.cs:173`) | 11 B | the host never applies the hit: **the swing does not count, for ever**, while the sender's screen already showed it. Peer→host only (`Server.cs:1116-1123`) | 🟢 | 1 |
| **EnemyKill** `:735` | once per death (`EnemySyncManager.cs:5829-5830`) | 4 B | the peer's copy stays alive — the "ghost" — until something re-states it. In practice it is re-stated twice by `EnemyDeathGlobal` right after (`:5833`) | 🟢 | 1 |
| **EnemyOwnership** `:750` | **host only**, on a change of owner, latched — sent once per change, never repeated (`EnemySyncManager.cs:5901-5918`); a handful per room transition | 5 B | the peer keeps the wrong owner: a frozen enemy for the rest of the session (measured: 134 samples ≈ 67 s of a WheelCarrier standing still, `:5932-5938`) or two owners fighting over one enemy | 🟢 | 1 |
| **EnemyDeathGlobal** `:766` | once per death, broadcast to **everyone** regardless of scene (`Server.cs:1078-1090`); replayed in bulk to a joiner (`EnemySyncManager.cs:4901-4920`) | 20-60 B (id string) | the durable record is the whole point of this message (`:764-765`): a player who walks in later finds the enemy alive | 🟢 | 1 |

**Not in the table, on purpose**

- **How 20 verbs become 18 rows**: `SendEffect` has two overloads (one row), `SendAttackPayload` is a
  multiplexer (its own table, §10.3), `SendQueue` is the flush and not a message, and `SendConnection`
  sends nothing (next bullet).
- **`SendConnection`** (`:378-381`) **sends nothing** — it is server→client only, so it has no class.
- **`HostAssign`** and **`Connection`** are received but never sent by a client. In P2P they need the same
  treatment on the sending side, and both belong to the reliable class: a lost `HostAssign` leaves the
  client believing it is not the host, which is why the server already had to broadcast it — a single
  lost one was permanent (`Server.cs:417-430`).
- The **fan-out** the server does (name in front, scene filter, one copy per peer) is not classified
  here: over P2P it disappears, and its cost becomes one send per peer.

### 10.3 The Attack channel carries five messages, not one

`NetworkType.Attack` is a multiplexer: the first payload byte (`AttackType`) picks the branch
(`NetworkManager.cs:528-568,570-573`). One `NetworkType`, five messages, and **two different classes** —
so the class cannot be keyed on the packet type. It has to be chosen **where the payload is built**,
which is exactly what §10.1 says the receiver cannot do.

| Sub-message | Where | If lost | Class | Ch |
| ----------- | ----- | ------- | ----- | -- |
| **PvP hit** (`SendAttack`) | `OtherPlayerScript.cs:1185` | the damage never lands, nothing re-states it | 🟢 | 1 |
| **`AttackType.EnemyHit`** — an enemy's blow on the other player, `EnemyHitPeerSync.cs:259-261` | ≤5 Hz per enemy (`:173`) | the peer takes no damage: their health diverges and the boss fight with it | 🟢 | 1 |
| **`AttackType.PauseRelay`** — the pause front, `PauseNotice.cs:689-690` | edge-triggered **once** per pause / resume (`TrySendFront`) | the peer never learns the pause and keeps playing; there is no repeat, the front only comes again when the state flips twice | 🟢 | 1 |
| **`AttackType.CoopEffectRelay`** — an effect born on another machine, `EffectRelay.cs:467-468` | one per weapon/projectile spawn, filtered by `Braked` | a particle is missing on the peer: cosmetic | 🔵 | 0 |
| **`AttackType.SyncIdList`** — the id-list diagnostic, `SyncIdListMarker.cs:136,269` | at most one every 15 s (`:80`) | no comparison report this round; the next divergence re-asks | 🔵 | 0 |

### 10.4 What channel 0 needs in the payload: a counter, at 30 Hz

**The counter is not there.** The `Position` payload is **two floats and nothing else**
(`NetworkManager.cs:226-229`), and the receiver writes it straight into the transform
(`OtherPlayerManager.cs:434-448` → `OtherPlayerScript.cs:773-779`). `EnemyUpdate` is the same: the
snapshot is recorded as a target with no age and no order (`EnemySyncManager.cs:6252`). Over TCP this is
free — the stream is ordered. Over an **unordered** class it is not: the last **arrival** wins, and on a
jittery link the last arrival is not the last **sample**, so an overtaken packet drags the peer
backwards.

⇒ **channel 0 needs a monotonic counter in its snapshot payloads**, and the receiver must **discard any
packet whose counter is older than the last one it accepted** for that sender. Which messages:

- **yes**: `Position`, `EnemyUpdate` — last-wins state, and `EnemyUpdate` carries `currentLife` and
  `animState` too, so a stale copy does not just move an enemy, it can heal it for a frame;
- **yes, if they stay separate edges**: `Animation`, `Direction` — they are also last-wins state, so a
  counter is what makes an out-of-order arrival harmless. ⚠️ A better fix costs no field at all: fold
  them into the position snapshot, which already goes out on the heartbeat, and the edge stops being a
  one-shot;
- **no**: `Effect` and the `CoopEffectRelay` payloads are **edges, not snapshots** — every copy must be
  delivered, so a counter there would throw away the ones that arrive late and lose an event. And `Ping`
  measures a round trip: a counter would only make the sample discardable.

⚠️ **And where the counter can go is not free.** The server **re-encodes** `Position`, `Animation` and
`Direction` from its own `PlayerInfo` (`Server.cs:471-486,503-516,533-552`), so an extra byte added on
the client is **dropped in the middle** unless the server carries it too — and both ends are on
`PROTOCOL_VERSION = 11` (`NetworkManager.cs:791`, `Server.cs:1134`). The messages the server relays
**verbatim** are the `relayToScene` ones (`Server.cs:1046-1056`): `EnemyUpdate`, `EnemyDeath`,
`EnemyDeathGlobal`, `EnemyOwnership`, `EnemyDamageRequest`, plus `Attack` and `Effect`, which are
prefixed but whole (`:965-970`, `:1001-1006`). ⇒ for `EnemyUpdate` the counter is a client-only change;
for `Position` it is a wire **and** server change — unless the P2P path drops the server, in which case
the re-encode disappears with it.

**30 Hz, and the interpolation that goes with it**

Channel 0 has to be **coalesced to 30 Hz** (one send every two frames), not 60: it halves the traffic and
above all halves the number of `NoDelay` packets, which are the expensive kind. The mechanism already
exists on the sending side — everything queued in a frame leaves in one `SendQueue` flush
(`Multiplayer.cs:379`) — so 30 Hz means *not queueing on alternate flushes*. The 5 Hz heartbeat stays as
it is: it is what proves a standing player has not moved, and it is already below the cap.

⚠️ **Coalescing only works if the receiver interpolates, and today only half of it exists:**

- **enemies**: yes — `InterpolateRemoteEnemies` (`EnemySyncManager.cs:4547`) eases every non-owned enemy
  towards its last snapshot, every frame (`:5742`), `FOLLOW_LERP = 0.25f` (`:1268`), snapping beyond 6
  units (`:1271`);
- **the other player: no.** The position goes straight into `transform.position`
  (`OtherPlayerScript.cs:773-779`), and the code says so in as many words: *"the lerp in
  InterpolateRemoteEnemies smooths enemies, never the other player"* (`EnemySyncManager.cs:4527-4528`).
  `animRepeat` is a **counter in the performance probe** (`PerformanceProbe.cs:281`), not a smoothing
  mechanism.

⇒ at 30 Hz the player avatar needs an interpolation that **does not exist today**: it has to be
**declared** as part of the P2P step. The enemy path can reuse what is there.

**The consequence for TCP, in one line: nothing changes.** Everything above is a property of the P2P
send classes — one ordered reliable stream has no class to choose, no channel to isolate and no
out-of-order arrival to reject.
