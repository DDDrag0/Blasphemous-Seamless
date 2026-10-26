# Blasphemous Seamless

Seamless Co-op mod for **Blasphemous**, built on the original mod [Blasphemous.multiplayer](https://github.com/BrandenEK/Blasphemous.Multiplayer) by [BrandenEK](https://github.com/BrandenEK) and inspired by [Yui's](https://github.com/yuiamoroll) Seamless mods.

## Index

1. [Status](#status) — what the mod does today, what synchronises, what is still open.
2. [Known bugs](#known-bugs) — the bugs that we know and still working on fixing them, and how to report one.
3. [Installing the mod](#installing-the-mod) — the three components, their SHA256, the install script.
4. [Building and testing your changes](#building-and-testing-your-changes) — build, deploy, bench.
5. [Tools](#the-tools-one-by-one) — what each tool actually does.

---

## Status

### 1. What this mod does

This mod is a clone (unfortunately not a fork) of the original mod by BrandenEX; it expands upon the original author's work, aiming to achieve the best possible synchronization of all gameplay elements within the game itself.


The primary focus of the mod is the synchronization of enemies, bosses, bonfires, and various other features while also introducing additions such as Steam P2P to simplify connections between players.

### 2. How you start a game today

There are two ways, and both are in the mod's multiplayer panel.

**"Host session"** — It’s the same as the original mod; I’ve tried to keep the connection mechanism as it was. I don’t want to break the game for those who don’t own the Steam version.

**The Steam invite: Steam: Host** — This is an addition from the Seamless mod: the ability to use the Steam files included in the original game to facilitate connections between players, completely bypassing the need for port forwarding by leveraging the platform's built-in P2P capabilities.

**Compatibility:** Invites and direct connections between players are a **Steam** feature. You must own the Steam version of the game, have the Steam client running, and both players must have launched the game via Steam. **These options are not available on other platforms (such as GOG or similar)**; however, this is not an issue, as you can always use an **IP address and port forwarding** to host, for players without Steam can easily join a session hosted this way.

**Server Simplification:** The host now **represents** the session; if they close the game, the session ends for both players. Direct connections require Steam, whereas using an IP address does not. In certain specific cases, Steam may route traffic through one of its own relay servers instead of establishing a direct connection; the end result remains the same, though the initial connection may take longer. You can always use the old method for the server; as mentioned earlier, the mod does not overwrite what is already working.

### 3. What used to be impossible

Two games used to run **side by side**. Two worlds, two sets of enemies, two versions of every fight:
you could stand in the same room as your friend and, in fact, be playing a different game.

### 4. What syncs for now

- **Enemies and their position**
- **Death and respawn**
- **The boss fight**: Obviously, it still needs to be tested more thoroughly; the mod is still in an early, unpolished state.
- **Cutscenes post boss death**
- **The effects**: some penitent sounds, hits, the trails, the projectiles.
- **The damage**: the boss hurts you even when it is not your machine that runs it.
- **The pause**: it stays **yours**: pausing never freezes your teammate's world.
- **The Prie-Dieu**: If you die now, it works in a way similar to the seamless [co-op] in Elden Ring: the game doesn't reset until you rest at a Prie-Dieu.
- **Life guilt**: a penalty new to this mod: every death takes a bite out of your maximum health, and only resting at a Prie-Dieu gives it back.
- **Spectator mode**: If you die while the mod is active, the screen no longer fades out; instead, you can see what the other players in the room are doing. It’s not perfectly implemented yet (you can’t switch between players) but for now, it gets the job done.

### 5. Known limits

Some bosses — the DLC and the later additions — have no classic death flag, and the per-boss behaviour
has not been tuned for all of them. See **Known bugs**, entries 8 and 9.

### 6. What has not been seen in game yet

Seven features are implemented and verified in code, but nobody has observed them in a real match:
the per-boss aim and recited death · the projectiles of three enemies · the four bosses without a
classic death flag (including Viridiana, who may never die) · the two `InstantiateArea` hooks ·
the life guilt in PE02 / Demake · the 10-14 unit position divergence · the co-op gate edge
(connect and disconnect).

They are harmless to play with. Details, log strings and test steps are in the development notes.

---

## Known bugs

**What is this file for? It lists bugs that cannot be reproduced on demand.**
Problems of this type depend on a combination of room, tutorial, entry order, and timing that cannot be recreated
during a test run but that a player might accidentally trigger.

⚠️ **The real limits are not on this page**: they are in **docs\KNOWN_BUGS.md**, one entry each.

1. An execution followed by an enemy’s death could cause the game to freeze — **It should be fixed, but we can’t reproduce it**

2. The tutorial rooms are unreliable — sprites and enemies do not respawn at the Prie-Dieu

3. Save files move to a different folder if Steam isn’t running: **What you see.** The game appears **lost or corrupted** after launching without Steam. **It’s not corruption, without Steam, saves go to a different folder** — and with **Steam Cloud** enabled, the two
folders diverge. **What to do.** **ALWAYS launch Steam before the game.**

4. “Crossed” execution — one kills while the other has the prompt *(RARE)*: **What you see.** An enemy is **killed by one player**, while **the other** has the execution prompt on the **same** enemy. For the player who had the prompt, the enemy may become a **ghost**:
visible, untargetable, AI disabled.

5. A **DEAD** player can **LEAVE THE BOSSROOM** by pressing any key

6. **Skipping** the boss cutscene **can prevent the player from respawning** *(RARE)*

7. Some effects and projectiles might be out of sync. This should be fixed, but unfortunately testing hasn’t checked them yet; it will be resolved over time

8. The **BOSSES ARE STILL IN TESTING** two main problems: 
    1. The boss **seems to ignore** the other player: it continues to spin, jump, and charge **toward the player on the machine simulating it**, even when the other player is closer; 
    2. In the peer’s machine, the boss **remains “standing”** when it should die, this is because **the game’s death sequence doesn’t trigger** ⇒ no animation and **death isn’t played out** so the boss remains frozen in the last animation frame.

9. **FOUR BOSSES** without the “classic” death flag — **Amanecidas · Perpetua · PontiffHusk · Viridiana**: *(**ARE TO BE TESTED** — this is not a confirmed bug: **data is missing**)*


### How to Report a Bug *(for Players and Testers)*

**What You Need**, in order of importance:

1. **What you saw**, in one sentence, **and where** *(which room, or what it looked like)*.
2. **What you were doing**: who was the **host**, who was **in the room**, and **what happened right
   before** *(a bonfire, a death, an execution, a room change)*.
3. **The order of events**, even if approximate. *“First X, then Y”* is worth more than a minute of
   description.
4. **The two logs**, one per machine:
  
    | Platform | output_log location |
    | --------- | ------- |
    | Windows |`%USERPROFILE%\AppData\LocalLow\TheGameKitchen\Blasphemous\output_log.txt`|
    | macOS |`~/Library/Logs/TheGameKitchen/Blasphemous/Player.log`|
    | Linux / Steam Deck |`~/.config/unity3d/TheGameKitchen/Blasphemous/Player.log`|

   **And the server log**, `ServerOutput.log`, in the server folder.
5. **And the most useful thing of all: the recipe.** *“It happens when I do A, B, and C, in that order.”*
   **A reproducible bug is a bug that gets fixed.**

---

## Installing the mod

*To **play**, start here: the mod needs three runtime components in the game folder, and one script installs all of them (Windows only). To **compile** it, see the next section.*

`Blasphemous.Multiplayer.Client/Main.cs` declares two **BepInEx plugins** it depends on:

```csharp
[BepInDependency("Blasphemous.ModdingAPI", "3.0.0")]
[BepInDependency("Blasphemous.CheatConsole", "1.1.0")]
```

Both are loaded by BepInEx, so the game folder must contain a BepInEx loader. `tools/install-mod-loader.ps1`
installs it together with the two plugins.

### Verified components

Every download is checked against the SHA256 published in the GitHub release manifest, so the bytes are
provably the released ones.

| Component | Version | SHA256 (from the release manifest) |
| --------- | ------- | ---------------------------------- |
| [BepInEx](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) | 5.4.23.5 (win x64) | `82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4` |
| [Blasphemous Modding API](https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/tag/3.0.1) | 3.0.1 | `b74fe3900b563483d090fd7f01939fe7d60ba5711e83d7996396a274e7156ff7` |
| [Blasphemous Cheat Console](https://github.com/BrandenEK/Blasphemous.CheatConsole/releases/tag/1.1.0) | 1.1.0 | `2280e5b4fd82a88395aae5f841a4b64c47cf24a22c0603f02efb220662067db8` |

The upstream projects are the same author as the multiplayer mod itself
([BrandenEK](https://github.com/BrandenEK)), and this is the install path the mod's own README points at
via the [Blasphemous Mod Installer](https://github.com/BrandenEK/Blasphemous.Modding.Installer). Nothing
here is a third-party repackage.

### How to install all in one go (Command is Windows Only)

```
pwsh -NoProfile -File ".\tools\install-mod-loader.ps1"
```

What it does:

1. checks the target really is a Blasphemous install (refuses otherwise unless `-Force`);
2. downloads each component into `tools/cache/` and **verifies its SHA256**, aborting on any mismatch;
3. extracts them;
4. copies them into the game, **backing up every file it replaces** into a timestamped
   `tools/cache/backup-<timestamp>/`;
5. verifies the installed layout and exits non-zero if anything is missing;
6. prints the exact removal commands at the end.

Useful flags:

- `-WhatIf` - print the plan without changing anything.
- `-GamePath <path>` - install somewhere else.
- `-CachePath <path>` - keep the zips elsewhere.

### After installing

1. Build the mod; Copy the build generated inside the `.\publish` folder into the game folder.
2. Launch Blasphemous through Steam.
3. Check `<game>\BepInEx\LogOutput.log` for the plugin list. If `BlasClient` is not mentioned there, the
   mod is still not loading and the log is the place to find out why.

### Undo

```
Remove-Item '<game>\BepInEx' -Recurse -Force
Remove-Item '<game>\winhttp.dll' -Force
Remove-Item '<game>\doorstop_config.ini' -Force
Remove-Item '<game>\.doorstop_version' -Force
```

Anything that was replaced is in the timestamped backup folder the script printed; copy it back if
needed. Note this removes BepInEx for **every** mod in that install, not just this one.

---

## Building and testing your changes

### 1. What you need to build

**Any OS should work** (I ONLY TESTED ON WINDOWS): the mod targets `net35`, but its reference assemblies come
from a NuGet package, not from the operating system.

| requirement | why |
|---|---|
| **.NET SDK 8.0** (or newer) | builds all three projects |
| **internet access on the first `restore`** | `GameLibs`, `Blasphemous.ModdingAPI`, `HarmonyX`, `Mono.Cecil`, `Microsoft.NETFramework.ReferenceAssemblies` … are NuGet packages |
| **PowerShell 7 (`pwsh`)** | only for the helper scripts in `tools/` |

> If your IDE reports `MSB3644` ("reference assemblies for v3.5 were not found") it almost always means
> **that project has not been restored yet** — run `dotnet restore`. The command-line build below works
> without installing anything else.

### 2. Build

```bash
dotnet restore Blasphemous.Multiplayer.sln
dotnet build   Blasphemous.Multiplayer.sln
```

| project | target | what it produces |
|---|---|---|
| `Blasphemous.Multiplayer.Client` | `net35` | the mod — `BlasClient.dll`, loaded by BepInEx from `Modding/plugins/` |
| `Blasphemous.Multiplayer.Common` | `net35` **and** `net8.0` | protocol and shared types |
| `Blasphemous.Multiplayer.Server` | `net8.0` | the standalone relay, `blas1mpserver` |

### 3. What you need to test your changes

Running the mod tool requires **Windows** and a working game installation.

| requirement | why |
|---|---|
| **Blasphemous** (Steam, AppID 774361) | the game itself |
| **BepInEx 5.4.23.5** in the game folder | loads the mod; `tools/install-mod-loader.ps1` installs it |
| the game's `Assembly-CSharp.dll` | the patch-target gate below resolves every `[HarmonyPatch]` against it, so it fails at deploy time instead of at runtime |
| a second machine or a VM goes well (or a second game instance in the same machine if you want to go crazy with the log looping) | co-op is client/server over TCP; host and peer each need a game running |

### 4. Deploy and verify

Run from the repository root. All the scripts are **relative to their own location** — they do not
hardcode the repository path.

**One command does the whole round** — build, warning count against the baseline, deploy, and a
byte-level check that the DLL now sitting in the game is the one you just built:

```powershell
pwsh -NoProfile -File '.\tools\check.ps1'
```

It stops at the first red and exits non-zero. Green means: `0 error(s)` · **391** warning(s) · `273` Harmony targets · `14/14`
markers present · `game == bin/Debug` for the mod, Common and the server.

The individual steps, if you want to run them yourself:

```powershell
# copy the build into the game: mod, Common.dll and the server, then run the Harmony gate
pwsh -NoProfile -File '.\tools\deploy-local.ps1'

# the gate on its own (no deploy): every [HarmonyPatch] target must exist
pwsh -NoProfile -File '.\tools\verify-patch-targets.ps1'

# proof that the DLL actually sitting in the game contains the code you just built
pwsh -NoProfile -File '.\tools\marker-check.ps1' `
  -Dll 'D:\SteamLibrary\steamapps\common\Blasphemous\Modding\plugins\BlasClient.dll' -Marker '[Tempo]'
```

⚠️ **A green build is not enough.** A `[HarmonyPatch(typeof(X), "NotAMethod")]` compiles with **0
errors** and only fails at runtime, where Harmony aborts the remaining patches and the symptom looks
nothing like the cause. That is why the gate exists: it checks every target against the game assembly.

⚠️ **Deploy the three DLLs together.** `PROTOCOL_VERSION` is a `const`, so it is inlined into both the
client and the server: mixing a new client with an old server (or the reverse) gives a version refusal
that looks like a bug.

### 5. Headless wire bench (no game required)

The server protocol can be tested without the game, on any OS, by driving the real TCP sockets:

```powershell
pwsh -NoProfile -File '.\tools\run-headless.ps1' `
  -ServerDir '.\Blasphemous.Multiplayer.Server\bin\Debug\net8.0' `
  -OutDir    '.\scratch\bench'

# compare against a previous run: only clock and ephemeral ports are normalised
pwsh -NoProfile -File '.\tools\compare-headless.ps1' -Before '.\scratch\old-run' -After '.\scratch\bench'
```

`frames.txt` is the non-regression measure: its `sha256` must stay identical across changes that are
not supposed to touch the wire. The bench also runs a three-peer section with explicit assertions
about who must receive what, so a fan-out (`foreach (peer in connectedPlayers)`) is actually covered.

> `-ServerDir` is the folder produced by the build (`blas1mpserver.dll` plus its dependencies). Do
> **not** point it at `publish/BlasServer`: that folder is written by an MSBuild target that only runs
> when the server project is actually recompiled, so it can hold a stale server.

## The tools, one by one

### 1 `check.ps1`

**What it does:**
1. Build (`--no-incremental`, language forced to English to read the number of warnings) →
   > Why `--no-incremental`? Without this flag, an incremental build might report "0 warnings," making any comparison against the baseline meaningless (just noise).
   
   > Why `VSLANG=1033`: in Italian, MSBuild writes `Avvisi:` (Warnings:), making the number unreadable.
2. Deploy 
   1. 14 markers placed on the deployed DLL 
   2. hash consistency check (game vs. `bin\Debug`)

**Purpose:** A single command to wrap up a cycle · returns exit code 0 only if everything is "green" (all checks pass).

### 2 `deploy-local.ps1`
**What it does:** Copy the mod files from the project folder (/publish) to the game folder.

### 3 `verify-patch-targets.ps1`
**What it does:** Resolves all `[HarmonyPatch]` attributes (currently 273) against the game's `Assembly-CSharp.dll` using Cecil, and verifies that both the type and the method exist.

**Purpose:** An incorrect target will compile with 0 errors, but will then cause `PatchAll` to fail, which halts all subsequent patches: the mod stops writing logs, and the symptom does not resemble the actual cause.
   -  this is the only check that catches it.

### 4 `tools\verify-patch-targets\`

**What it does:** The Cecil-based program that the gate compiles and executes (copied to `%TEMP%` so that the repository doesn't get cluttered with `bin/obj` files).

### 5 `marker-check.ps1`
**What it does:** Searches for literal strings (UTF-16LE) within a DLL, at specific offsets.

**Purpose:** Proves that the DLL in the game is the one you compiled: *this is the only way to detect a stale deployment*.
   -  It has happened before: we thought we were testing one version, but the game was running another.

### 6 `run-headless.ps1`
**What it does:** Opens actual TCP sockets, sends real packets using `[len][type][payload]` framing, and captures every single byte of the response.
   -  Section A: The 42 frames ⇒ checked against the golden `sha256 9ecdfa42…` hash *(non-regression)*.
   -  Section B: 3 peers with 12 assertions regarding who should receive what *(fan-out)*.

**Purpose:** The only protocol test that DOES NOT require the game.
   -  *this is what runs in the CI pipeline; using `-ExpectFramesSha256`, it will reject the release if even a single byte has changed.*

### 7 `compare-headless.ps1`
**What it does:** Compares two ''Runs'' from the test bench, normalizing only the clock and ephemeral ports *(and explicitly states what is being normalized: nothing else is touched)*

**Purpose:** Provides a local answer to: "Did this change affect the protocol?"

### 8 `install-mod-loader.ps1`
**What it does:** 

Downloads:
1. BepInEx 5.4.23.5,
2. ModdingAPI 3.0.1,
3. CheatConsole 1.1.0,

Into `tools\cache\`, verifies their SHA256 hashes, extracts them, copies them into the game folder with timestamped backups, validates the file layout, and outputs the commands for removal

**Purpose:** The only reproducible way to prepare a machine *(the 3 dependencies are mandatory: see `[BepInDependency]` in `Main.cs`)*.

**What it DOES NOT cover**: It is Windows-only *(requires PowerShell + `win_x64` packages)* and *it does not attempt to run the game*.