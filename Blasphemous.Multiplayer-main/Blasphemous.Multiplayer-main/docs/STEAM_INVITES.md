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
