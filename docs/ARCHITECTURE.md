# ARCHITECTURE — Architecture and development notes

> **What this file is for.** A developer opens this repository knowing nothing. After reading it:
> what the mod does, what works, what was **not** done and why, what has **not been verified yet**, and how
> to verify a build. It is the map; it is not the diary.
>
> **And who it is for.** Whoever builds on this code. The plain-language page — what the mod does, for
> somebody who just wants to know what he has in his hands — is `docs\STATUS.md`. This file is the former
> `docs\STATUS.md`, renamed and kept entire: nothing was thrown away, only the audience changed.
>
> **What it is not.** Not a chronicle: no "on such a day we did X". The day-by-day record (decisions,
> measurements, dead ends, the reasoning behind every number) is `docs\TODO.md`; the declared defects and
> accepted prices are `docs\BUG-NOTI.md`; the rollback points are `docs\RIPRISTINO.md`.
>
> **State.** Branch `fix-f1a`. Every `file:line` below was re-measured at `ebf1484`; after that commit only
> documentation changed (`a084166` and this page), so the code is still that one — do not trust numbers
> quoted inside older reports, some are 20+ lines behind. Code paths are relative to
> `Blasphemous.Multiplayer-main\Blasphemous.Multiplayer-main\`; document paths to the repository root.

---

## 1. What this mod is

**Co-op for Blasphemous** — Unity **2017.4.40f1**, BepInEx **5.4.23.4**, Blasphemous **Modding API 3.0.1**,
**HarmonyX** (the `0Harmony.dll` shipped with BepInEx). Two binaries: the client plugin `BlasClient.dll`
(loaded from `Modding\plugins`) and a standalone console server shipped in `SERVER-COOP`.
**272 Harmony patch targets** (see §2, last bullet).

**The principle: the co-op is a layer. With no server connection the game behaves exactly as vanilla.**
All new behaviour hangs off one predicate — the socket, `CoopGate.IsMultiplayerActive()`
(`Blasphemous.Multiplayer.Client\CoopGate.cs:28`) = `NetworkManager.IsConnected` — and when in doubt it
answers `false`, which is the inert state (`CoopGate.cs:22-31`). If you add a feature, gate it there.

---

## 2. Architecture in one page

- **The server is a relay with no memory.** Team progress lives in RAM only
  (`Blasphemous.Multiplayer.Server\Core.cs:11`) and is replayed to whoever connects — one packet per stored
  key (`Server.cs:385-413`); when the last player of a team leaves, that team's data is **deleted**
  (`Core.cs:68-91`, `teamGameDatas.Remove(i)` at `:88`, called from `Server.cs:143` and `:421`). There is
  no persistence anywhere in the server project: restarting it zeroes everything. **This is by design** — a
  server-side memory would mix saves from different sessions and different players, and a *wrong* flag
  cannot be rebuilt while a *lost* one can (`docs\TODO.md:7613-7621`).
- **The authority is the client's save.** Each client re-pushes its own progress: on entering a scene
  (`Multiplayer.cs:177`) and on connection (`:461`), via `SendAllProgress`
  (`ProgressSync\Helpers\FlagHelper.cs:22-25`) → `GetCurrentPersistentState("intro")` → the prefix that
  sends every flag that is set (`FlagHelper.cs:46-69`); the server stores it again (`Server.cs:591`). So a
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
- **Pause is shared, through the game's own channel.** Received "paused" ⇒ `Core.Logic.PauseGame()`
  (`PauseNotice.cs:465`) with the map tab opened on the passive; leaving goes through
  `UIController.HideInventory` / `HidePauseMenu` (`:587`, `:592`). `Time.timeScale` is **never written** —
  the effect on it is produced by the game (`PauseNotice.cs:21-26`).
- **Life guilt is a separate penalty on a separate channel.** Three fixed steps on `Life.MaxFactor`:
  0 deaths → 1.0, 1 → 0.8, 2 → 0.6, 3+ → 0.4 with the floor in code (`LifeGuilt.cs:84-90`). Reset **only**
  by sitting at a Prie-Dieu — never by the guilt fragment, never by the Confessor (`LifeGuilt.cs:781`);
  the counter survives closing the game because it is a file (`Modding\data\Multiplayer\life-guilt.txt`,
  `LifeGuilt.cs:105`, `:660`). The game's own guilt (`Fervour.MaxFactor`) is not touched anywhere.
- **272 Harmony targets.** 269 → 270 with the bonfire patch (`LifeGuilt.cs:45`), 270 → 272 with the two
  `InstantiateArea` postfixes, which are **done** (`a188060`, one `Postfix` per method, both calling the
  relay's own `EffectRelay.OnPoolSpawn`; `EffectRelayInstantiateArea.cs`). Read the number from the tool,
  never from a note: `tools\verify-patch-targets.ps1` prints `bersagli controllati : 272`, type missing 0,
  method missing 0, `ESITO : tutti validi` — re-run today against the deployed build of `a188060`.

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
  the magic-damage proof that the relayed copies are what hurt (`docs\TODO.md:6940-6960`). *The two
  `InstantiateArea` hooks are **not yet exercised in game**: that is an **observable**, listed in §8, not a
  limitation — the marker `[FxRelay] hook InstantiateArea: prefab=… da=BossJump|BossTeleport` says they fire,
  and `RISOLTO` on the peer **with** `istanzio` says the area travelled.*
- **Peer damage** — the enemy channel applies damage on the receiving machine with the game's own
  `Penitent.Damage(Hit)`; measured in a log (`[DannoPeer] APPLICATO`, `docs\TODO.md:6965`).
- **Shared pause** — measured, including the map opening on the passive machine
  (`docs\TODO.md:7475-7481`).
- **Life guilt** — the three steps, the persisted counter and the separation from the game's guilt
  fragment verified in game by the user (`docs\TODO.md:7520-7523`); the bonfire reset is local by design
  (whoever sits gets it, `LifeGuilt.cs:48-51`).
- **Warnings** — "wait for your teammates" on dying inside a bossfight, and "do not press" windowed on
  the **cutscene**, not on the boss's death (`BossDeathWarning.cs:34-38`; `BossFightDeathWarning.cs:145-179`).
  The window arithmetic is measured; the text on screen has not been re-observed since.
- **All of it off in single player** — and the reverse side, which is the point: the death screen is the
  game's own, opaque one (`DeathScreenTransparency.cs:120`), life is full (`LifeGuilt.cs:157`), and the
  team notice does not fire because there are no teammates (`BossFightDeathWarning.cs:168-169`).

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
- **Raw `Object.Instantiate` sites inside bosses remain outside the relay** (~18 of them, `docs\TODO.md:6829`):
  they never pass through `PoolManager`, so the hook cannot see them; the signal is silence, not an error.
  **The two `InstantiateArea` of `BossJumpAttack` / `BossTeleportAttack` are no longer among them**: they
  are hooked since `a188060` (270 → 272 targets, §2) and only their **run in game** is missing (§8).
- **Cosmetic effects are not relayed by design.** The sender filters on `Weapon`/`Projectile`
  (`EffectRelay.cs:1429`): a purely cosmetic prefab is dropped — the hook fires and the packet does not.
- **Audio is a separate channel**, outside the relay (`EffectRelay.cs:64`): sounds travel on the effect
  channel at player level only.
- **No latency compensation.** The peer is drawn where its last packet put it
  (`Players\OtherPlayerScript.cs:773-777`) while enemies are smoothed (`EnemySyncManager.cs:4547`); the
  relay is additionally rate-limited to 20 sends/s (`EffectRelay.cs:154`), and a braked effect is simply
  not seen.
- **The same attack lands ~14 units apart on the two machines** (`docs\TODO.md:6970-6973`, `:7491`): not
  reported as a problem by the user, but unexplained, and it is **upstream** of the dedup — no time window
  would merge them.
- **PE02 / Demake and the single-player death screen are implemented but unobserved**
  (`scratch\REFERTO-COLPA-PE02.md`, §6) — see §8.

The other declared items — the execution freeze that could not be reproduced, the tutorial rooms, the save
folder when Steam is not running, the crossed execution, the dead player leaving the bossroom, the 1.5 s
skip window, the late joiner — are BUG-NOTI §1-§6 and §10. §5 below explains the ones that are **decisions**.

---

## 5. Design decisions — and why

This is the part that gets re-litigated if it is not written down. Every line is a decision already taken:
re-opening one needs a new measurement, not a new opinion.

- **F2 was reverted** (`docs\TODO.md:6852-6859`). Binding the boss to a single owner made it single-target —
  worse for the other player than the flicker it removed. The ping-pong is back **by choice**, and it is the
  price the user judged acceptable.
- **The 1.5 s skip window is not fixed, and will not be fixed with a timer** (`docs\TODO.md:7010-7016`,
  BUG-NOTI §6). Pressing to skip the boss cutscene less than ~1.5 s before its end can kill the respawn: one
  `currentTween` in `FadeWidget`, and the second fade kills the first callback. The only proposed cure — an
  automatic `Respawn()` on a timer — **would skip cut-scenes**, the same damage the project spent weeks
  removing. There is already an exit, and it is the game's own: enter and leave a level.
- **The boss ceremony is not skipped, because it cannot be done from C#** (`docs\TODO.md:6627-6630`).
  `BossBlock` has **0 occurrences** in `Assembly-CSharp` — it is a string set by a Playmaker FSM inside the
  scene asset — and `onStartBossFight.Invoke()` is the **last line** of `StartBossFight`, after seven steps,
  with zero C# subscribers. Touching it risks the only failure worse than a desynced boss: a blocked room.
- **The server has no memory** (`docs\TODO.md:7594-7630`). Team slots are mutable: the same slot, on
  different days, holds different players with different saves, so a server-side archive would attribute to
  one save the content of another. A lost flag is rebuilt at the next connection; a wrong flag is not.
  Removing a team's data when it empties is **the rule**, not a defect.
- **`_AC43` is not a bug.** The game writes `D17Z01S11_BOSSDEAD_AC43` — the achievement counter — not
  `<room>_BOSSDEAD` (`docs\TODO.md:6501`, `:6841`); 11 of 12 rooms do have a `<room>_BOSSDEAD` flag, which
  is why the zone flag is the branch that actually fires here (`:7246`).
- **The guilt band on the health bar was attempted and reverted** (`docs\TODO.md:7528-7534`). With the
  penalty at 0.8 the bar keeps its width and fills to 80%: it does not shorten, because its width comes
  from `Final`, which ignores `MaxFactor` (`PlayerDeathSync.cs:293-295`), and the overlay was invisible in
  game. A text notification states the real percentage instead — the briars were a whim, not a requirement.
- **`tools\verify-deploy.ps1` was deleted** (`docs\TODO.md:7455-7469`). It printed "you can test" on a
  stale DLL and everyone used it precisely because it was reassuring. The gate is now
  `tools\marker-check.ps1`, which reads the **bytes** of the deployed DLL and can say NO.

---

## 6. Traps — read before you build

- 🔴 **`deploy-local.ps1` copies from `bin\Debug`** (`tools\deploy-local.ps1:42`, `:48`). A `Release`
  build followed by a deploy ships the **old Debug DLL** — and the deploy still prints a healthy target
  count, because that gate checks the *names* of the patch targets, not the content
  (`tools\verify-patch-targets.ps1:74-77`).
- 🔴 **Markers must be concatenated.** `$"{MARK} …"` compiles to `string.Format`, so the DLL contains
  `"{0} …"` and the byte check reports the marker as missing (`docs\TODO.md:7541-7545`; the rule in code:
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

- The deploy runs the Harmony-target gate itself (`tools\deploy-local.ps1:79-86`) and **stops** if a target
  name does not exist: a wrong name in `[HarmonyPatch]` compiles, then aborts `PatchAll` at runtime and the
  symptom looks like "the mod logs nothing at all".
- `marker-check` must exit **0** and print `ESITO: TUTTI i marker richiesti sono PRESENTI`. The `-Marker`
  array only works with the call operator `&`; with `-File` it takes a single value.

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
  (`docs\TODO.md:6970-6973`).
- **The two `InstantiateArea` postfixes** — **done since `a188060`**, never exercised in game *(an
  observable, not a limit)*: `[FxRelay] hook InstantiateArea: prefab=… da=BossJump|BossTeleport`:
  if `RISOLTO` on the peer comes **with** `istanzio` the area is relayed, if it comes **without** it the
  prefab is cosmetic and nothing was sent (`EffectRelayInstantiateArea.cs:48-53`).
