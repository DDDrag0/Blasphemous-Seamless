# Enemy sync – in-game verification plan

Status: **not yet executed.** No build of this feature has ever been run in game, by anyone. Everything
below exists to turn "it compiles" into evidence.

Two files carry the whole feature:

- `Blasphemous.Multiplayer.Client/EnemySyncManager.cs` – identity, authority, state broadcast.
- `Blasphemous.Multiplayer.Client/EnemyDamagePatch.cs` – routes player damage through the host.

## 0. Build and deploy – read this first, the mod is going to the wrong place

```
dotnet build Blasphemous.Multiplayer.sln
```

The `Development` target in `Blasphemous.Multiplayer.Client.csproj` copies the plugin to a hardcoded

```
C:\Program Files (x86)\Steam\steamapps\common\Blasphemous\Modding\
```

That folder contains **only** a leftover `Modding` directory (7 files, 0.3 MB). The actual game is
installed elsewhere:

| Path | Contents |
| ---- | -------- |
| `C:\Program Files (x86)\Steam\steamapps\common\Blasphemous` | `Modding\` only – no game |
| `D:\SteamLibrary\steamapps\common\Blasphemous` | **the real install** – 169 files, 859 MB |

So every build so far has been writing the plugin into a directory with no game in it, and the real
installation has no `Modding` folder, no `BepInEx`, no `winhttp.dll` and no `doorstop_config.ini`. That
is consistent with the feature never having been run: it was never in a position to load.

Before testing:

1. Install the Blasphemous mod loader (the Modding API that provides `Blasphemous.ModdingAPI`, which this
   mod depends on) into `D:\SteamLibrary\steamapps\common\Blasphemous`.
2. Either update the `DevFolder` property in the `.csproj`, or copy manually from
   `publish\BlasClient\`:
   - `plugins\BlasClient.dll` -> `<game>\Modding\plugins\`
   - `data\` and `localization\` -> `<game>\Modding\`
3. Launch the game through Steam and confirm the mod appears in the BepInEx log before testing anything
   below.


## 1. Single machine, offline – the cheap checks

Do this first: it needs no server and no second player.

1. Load a save and walk into a room with enemies.
2. Open the console and run `multiplayer sync`.

Expected:

- `role=solo` (offline is treated as host, so single-player behaves exactly like vanilla).
- `spawnPoints` and `tracked` both non-zero; `tracked` grows as arena/standby enemies appear.
- `sent=0 recv=0` – nothing is transmitted while offline.
- No `ATTENZIONE: ... identita' duplicata` warning.

3. Run `multiplayer sync dump` and check that:

- every entry has a distinct id;
- id format is `<scene>|<spawn point name>|<x>|<y>`;
- enemies you can see are listed as `alive=True`, and enemies already killed in this save show as
  `alive=False` or `dead=True`.

4. Kill an enemy. It must die normally, drop its reward, and disappear from the tracked list after a
   moment. This confirms offline play is untouched.

**If this step fails, stop** – the feature is broken in a way that has nothing to do with networking.

## 2. Identity agreement – the core test

This is the test the whole design rests on, and it needs two machines (or two instances).

1. Start the server, connect both clients, and have both players enter the **same room**.
2. On **both** machines run `multiplayer sync dump`.
3. Compare the two lists.

The `[index]` column and the id strings **must be byte-identical on both machines.** The ids are derived
from authored scene data, so any difference means the two machines disagree about which enemy is which,
and every position update would be applied to the wrong enemy.

- The index is the wire handle and is sorted, so it does not depend on Unity's enumeration order.
- `life` and `pos` will differ (one machine is the host, the other interpolates nothing yet), that is
  expected. Only the ids and indices must match.

Run `multiplayer sync` on both as well: `spawnPoints` must be equal.

## 3. Authority – who is driving

On the host, `multiplayer sync` must report `role=HOST`. On the other machine, `role=peer`.

- `sent` on the host must climb; `sent` on the peer must stay at `0`.
- `recv` on the peer must climb; `recv` on the host stays at `0`.

If both report `peer`, the server never assigned the host and nothing is being synchronised. If both
report `HOST`, the server assigned both.

## 4. Movement

Watch a patrolling enemy from both screens.

- On the peer, the enemy should follow the host's position and should **not** wander off on its own.
- The peer's enemies keep their idle/walk animation (the Animator is not paused by `PauseBehaviour`),
  and attack animations should now appear because they are driven from the host's animator state.

Known limitation: positions are applied by teleport, without interpolation, so the peer's movement will
look steppy at 20 Hz. That is expected for now and is a separate improvement.

## 5. Damage

1. Have the **peer** hit an enemy.
2. Expected: the hit registers on the peer after a round trip (it is host-authoritative), the host's
   health drops, and the health bar agrees on both screens.
3. On the peer, `dmgSent` increments; on the host, `dmgApplied` increments.
4. Kill an enemy entirely from the peer and confirm both machines remove it and both players receive the
   drop/tears.

Watch specifically for **double damage**: if the host's enemy loses twice the expected health per swing,
the re-entrancy guard is not working.

## 6. Host migration

1. With both connected in the same room, disconnect the host.
2. The server reassigns the host and sends `HostAssign` to the remaining player.
3. On that machine, `multiplayer sync` must now report `role=HOST`, `sent` must begin climbing, and its
   enemies must **start moving again** (the promotion path calls `StartBehaviour`).

If the remaining player keeps reporting `peer`, migration failed.

## 7. What to capture if something is wrong

Copy the output of `multiplayer sync` and `multiplayer sync dump` from both machines, plus the BepInEx
log (`BepInEx/LogOutput.log`) around the failure. The important lines are prefixed `[EnemySync]`:

- `[EnemySync] Host role: True/False` – host assignment arrived.
- `[EnemySync] Scene <name>: tracking N enemies / M spawn points` – the registry was built.
- `[EnemySync] Indice nemico N fuori intervallo ...` – the two machines disagree about the id list. This
  is the single most useful error to see, because it means the identity scheme failed.

## 8. Still unhandled (do not expect these to work)

| Gap | Effect |
| --- | --- |
| Enemies damaging players | Peers pause enemy AI, so an enemy that never runs its attack on the peer may never damage that player. Never implemented. |
| Boss phases, RNG, invulnerability windows | Not addressed. Bosses are the hardest case. |
| Dynamically triggered enemies | Registered by rescan, but their spawn *timing* is not synchronised across machines. |
| Position interpolation | Teleport at 20 Hz. |
| Enemy despawn/revive per `EnemySpawnPoint.EnablePersistence` | Not synchronised. |
| Steam invites | Feasibility only; see `STEAM_INVITES.md`. |
