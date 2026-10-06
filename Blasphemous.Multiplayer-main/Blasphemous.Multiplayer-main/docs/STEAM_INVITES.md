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
SteamMatchmaking.GetLobbyMemberByIndex(CSteamID, int)
SteamMatchmaking.SendLobbyChatMsg(CSteamID, byte[], int)

SteamAPI.Init() / RunCallbacks() / Shutdown()
```

Callback payloads are also present:

```
GameLobbyJoinRequested_t { CSteamID m_steamIDLobby; CSteamID m_steamIDFriend; }
LobbyEnter_t
LobbyInvite_t
```

This is the same mechanism Spacewar and other Steam titles use: create a lobby, open the invite
overlay, and react to `GameLobbyJoinRequested_t` when a friend accepts.

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
3. **Invited player** accepts; Steam launches the game if needed and fires
   `GameLobbyJoinRequested_t` (or delivers it through `LobbyInvite_t` if the game is already running).
4. **Client** reads the lobby metadata and pre-fills the existing connection screen, so the join becomes
   one click instead of typing an IP.

Note the mod's transport today is a direct TCP connection to a `SimpleTCP` server; Steam lobbies would
be used for *discovery and invitation*, not as the transport. That keeps the change additive: the
existing netcode is untouched, and the lobby is purely a rendezvous mechanism.

## 5. What implementing it costs

- **A reference to `Assembly-CSharp-firstpass.dll` is required.** The mod's `.csproj` currently does not
  reference it, and the repo has no local pointer to the game assemblies. Two options:
  - *Type-safe reference*: add a `<Reference>` with a `HintPath` to the game's assembly. Simplest and
    best for local development, but it breaks the CI build (`.github/workflows/build.yml`), which has no
    game install.
  - *Runtime discovery via reflection*: resolve `Steamworks.SteamFriends` / `SteamMatchmaking` at runtime
    and invoke through reflection. No build dependency, CI keeps working, but the code is less pleasant
    and loses compile-time checking.
  A hybrid (reference with a build condition, reflection fallback) is also possible.
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
