# Blasphemous Seamless

Co-op multiplayer mod for **Blasphemous** (Steam AppID 774361), built on **BepInEx 5.4.23.4** and the
**Blasphemous Modding API 3.0.1**.

This file covers **only** how to build the project and how to test your changes.
Architecture, known limitations and the state of each subsystem live in `docs/`.

---

## 1. What you need to build

**Any OS works** (Windows, Linux, macOS): the mod targets `net35`, but its reference assemblies come
from a NuGet package, not from the operating system.

| requirement | why |
|---|---|
| **.NET SDK 8.0** (or newer) | builds all three projects |
| **internet access on the first `restore`** | `GameLibs`, `Blasphemous.ModdingAPI`, `HarmonyX`, `Mono.Cecil`, `Microsoft.NETFramework.ReferenceAssemblies` … are NuGet packages |
| **PowerShell 7 (`pwsh`)** | only for the helper scripts in `tools/` |
| **nothing else** | ⚠️ you do **not** need the .NET Framework Developer Pack, and you do **not** need to enable the ".NET Framework 3.5" Windows feature: `Microsoft.NETFramework.ReferenceAssemblies` (referenced by `Client.csproj` and `Common.csproj`) already provides the `net35` reference assemblies |

> If your IDE reports `MSB3644` ("reference assemblies for v3.5 were not found") it almost always means
> **that project has not been restored yet** — run `dotnet restore`. The command-line build below works
> without installing anything else.

## 2. Build

```bash
dotnet restore Blasphemous.Multiplayer-main/Blasphemous.Multiplayer-main/Blasphemous.Multiplayer.sln
dotnet build   Blasphemous.Multiplayer-main/Blasphemous.Multiplayer-main/Blasphemous.Multiplayer.sln
```

Expected: **0 errors**, about **469 warnings** — essentially all `CS1591` ("missing XML comment"),
which is the baseline. A different count is a signal: more means something new, fewer usually means dead code was
removed.

| project | target | what it produces |
|---|---|---|
| `Blasphemous.Multiplayer.Client` | `net35` | the mod — `BlasClient.dll`, loaded by BepInEx from `Modding/plugins/` |
| `Blasphemous.Multiplayer.Common` | `net35` **and** `net8.0` | protocol and shared types |
| `Blasphemous.Multiplayer.Server` | `net8.0` | the standalone relay, `blas1mpserver` |

The **server is cross-platform by design** (`RuntimeIdentifiers`: `win-x64;linux-x64;osx-x64`).
The **client can be compiled anywhere**, but it only *runs* inside the game, which is **Windows-only**.

## 3. What you need to test your changes

Running the mod requires **Windows** and a working game installation.

| requirement | why |
|---|---|
| **Blasphemous** (Steam, AppID 774361) | the game itself |
| **BepInEx 5.4.23.4** in the game folder | loads the mod; `tools/install-mod-loader.ps1` can install it |
| the game's `Assembly-CSharp.dll` | the patch-target gate below resolves every `[HarmonyPatch]` against it, so it fails at deploy time instead of at runtime |
| a second machine (or a second game instance) | co-op is client/server over TCP; host and peer each need a game running |

## 4. Deploy and verify

Run from the repository root. All the scripts are **relative to their own location** — they do not
hardcode the repository path.

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

## 5. Headless wire bench (no game required)

The server protocol can be tested without the game, on any OS, by driving the real TCP sockets:

```powershell
pwsh -NoProfile -File '.\tools\run-headless.ps1' `
  -ServerDir '.\Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main\publish\BlasServer' `
  -OutDir    '.\scratch\bench'

# compare against a previous run: only clock and ephemeral ports are normalised
pwsh -NoProfile -File '.\tools\compare-headless.ps1' -Before '.\scratch\old-run' -After '.\scratch\bench'
```

`frames.txt` is the non-regression measure: its `sha256` must stay identical across changes that are
not supposed to touch the wire. The bench also runs a three-peer section with explicit assertions
about who must receive what, so a fan-out (`foreach (peer in connectedPlayers)`) is actually covered.

> The `-ServerDir` above is the folder produced by the build. After a repository flattening the
> solution moves to the repository root; the helper scripts find it on their own, the paths in this
> section are the only ones that change.

## 6. Repository layout

```
Blasphemous.Multiplayer-main/Blasphemous.Multiplayer-main/   solution and the three projects
  resources/                                                shipped assets (SimpleTCP.dll, json data …)
tools/                                                       build, deploy and verification scripts
docs/                                                        architecture, status, known issues
```

Build outputs (`bin/`, `obj/`, `publish/`) are **never** tracked.
