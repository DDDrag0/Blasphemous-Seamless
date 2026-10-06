# Installing the mod loader (why the mod has never run)

The multiplayer mod cannot load without three runtime components, and **none of them are on this
machine**. That is the reason the enemy-sync feature has never been executed: not a code problem, a
missing runtime. This document explains what is missing, what the verified versions are, and how to
install them.

## What was wrong

`Blasphemous.Multiplayer.Client/Main.cs` declares:

```csharp
[BepInDependency("Blasphemous.ModdingAPI", "3.0.0")]
[BepInDependency("Blasphemous.CheatConsole", "1.1.0")]
```

Both are **BepInEx plugins**, so a BepInEx loader must be present in the game folder. On this machine:

| Location | State |
| -------- | ----- |
| `D:\SteamLibrary\steamapps\common\Blasphemous` | the real game (169 files, 859 MB) - **no `BepInEx`, no `winhttp.dll`, no `doorstop_config.ini`** |
| `C:\Program Files (x86)\Steam\steamapps\common\Blasphemous` | only a leftover `Modding\` folder, no game |

On top of that, the project's post-build step was copying the plugin into the **C:** path, which contains
no game. So the plugin was being deployed into a folder nothing loads, while the actual game had no mod
loader at all. Both problems are now addressed: the build target points at the real install (commit
`6c3c33c`), and `tools/install-mod-loader.ps1` installs the loader.

## Verified components

Every download is checked against the SHA256 published in the GitHub release manifest, so the bytes are
provably the released ones.

| Component | Version | SHA256 (from the release manifest) |
| --------- | ------- | ---------------------------------- |
| [BepInEx](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) | 5.4.23.5 (win x64) | `82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4` |
| [Blasphemous Modding API](https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/tag/3.0.1) | 3.0.1 | `b74fe3900b563483d090fd7f01939fe7d60ba5711e83d7996396a274e7156ff7` |
| [Blasphemous Cheat Console](https://github.com/BrandenEK/Blasphemous.CheatConsole/releases/tag/1.1.0) | 1.1.0 | `2280e5b4fd82a88395aae5f841a4b64c47cf24a22c0603f02efb220662067db8` |

Why these versions and not others:

- **BepInEx 5, not 6.** The Modding API assembly references `BepInEx 5.4.20.0` (checked with Mono.Cecil
  over the downloaded `ModdingAPI.dll`), and it also references `0Harmony 2.7.0.0`, which BepInEx 5
  bundles. BepInEx 6 does not load BepInEx 5 plugins. 5.4.23.5 is the current LTS release and satisfies
  the `>= 5.4.20` requirement.
- **Modding API 3.0.1**, which satisfies the mod's `3.0.0` dependency.
- **Cheat Console 1.1.0**, exactly the declared dependency. It provides the console used by the
  `multiplayer sync` diagnostic commands.

The upstream projects are the same author as the multiplayer mod itself
([BrandenEK](https://github.com/BrandenEK)), and this is the install path the mod's own README points at
via the [Blasphemous Mod Installer](https://github.com/BrandenEK/Blasphemous.Modding.Installer). Nothing
here is a third-party repackage.

## How to install

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

Two details worth knowing, both found by testing the script against a throwaway folder rather than the
real game:

- the plugin zips contain a bare `plugins/` folder that belongs under `BepInEx/`, **not** at the game
  root - installing it as-is would put the plugins where the loader never looks;
- because several components contribute to `BepInEx/plugins`, directories are **merged** and individual
  files are backed up, rather than moving the whole folder aside (which would have deleted the other
  plugin).

## Undo

```
Remove-Item '<game>\BepInEx' -Recurse -Force
Remove-Item '<game>\winhttp.dll' -Force
Remove-Item '<game>\doorstop_config.ini' -Force
Remove-Item '<game>\.doorstop_version' -Force
```

Anything that was replaced is in the timestamped backup folder the script printed; copy it back if
needed. Note this removes BepInEx for **every** mod in that install, not just this one.

## After installing

1. Build the mod; its post-build step now copies `BlasClient.dll` into `<game>\Modding\`.
2. Launch Blasphemous through Steam.
3. Check `<game>\BepInEx\LogOutput.log` for the plugin list. If `BlasClient` is not mentioned there, the
   mod is still not loading and the log is the place to find out why.
4. Then work through `ENEMY_SYNC_TESTPLAN.md`.
