# KNOWN BUGS

**What is this file for? It lists bugs that cannot be reproduced on demand.**
Problems of this type depend on a combination of room, tutorial, entry order, and timing that cannot be recreated
during a test run but that a player might accidentally trigger.

## 1. An execution followed by an enemy’s death could cause the game to freeze — **It should be fixed, but we can’t reproduce it**

**What happened.** *"G1 kills an enemy, the execution prompt appears on G2’s screen, G2 performs the execution,
becomes **invisible** on their screen and **frozen** on G1’s; on G2’s client, it was impossible to do anything,
**all keys were completely unresponsive**"* — **forced to quit the game manually.**

**Why is it still listed here even though it’s been fixed?** The cause was identified and corrected, but the bug
**could never be reproduced on demand**: it occurred twice in separate sessions, at
uncontrolled moments.

**What it was.** `DestroyDeadEnemy` called `Object.Destroy` on the enemy’s GameObject **while the game
still held** `Execution.ExecutedEntity` and the execution prop’s state machine:
- `StuntBehaviour.OnStateUpdate` calls `GetComponent<EnemyBehaviour>()`, and with the object destroyed
  the exception occurs **inside the native iCall**;
- and the `Stunt` state **can no longer exit** *(the exit path is the last instruction in
  `OnStateUpdate`, which the exception skips)* ⇒ it is called **every frame**;
- **but the key lock**: the execution coroutine
  `SetBlocker(EXECUTION, true) → KillEntity() → ShowPlayer → SetBlocker(false)` **crashed** on
  `KillEntity()` ⇒ invisible penalty, `IsOnExecution` never reset, **blocker enabled forever.**

## 2. The tutorial rooms are unreliable — sprites and enemies do not respawn at the Prie-Dieu

**What you see.** In rooms with **sprite tutorials**, sitting at the Prie-Dieu **does not cause the
tutorial sprites to respawn**, while **in all other rooms, respawning works**. And in general, those
rooms behave abnormally: that’s where the highest number of hard-to-reproduce
bugs have been observed.

**What to do.** **Don’t worry about bugs in those rooms.** You’ll waste hours on issues that aren’t easily
resolved. A bug observed there involving enemies should be **verified elsewhere** before reporting it.

## 3. Save files move to a different folder if Steam isn’t running

**What you see.** The game appears **lost or corrupted** after launching without Steam.

**It’s not corruption.** The path is
`persistentDataPath + “/Savegames/” + (SteamManager.Initialized ? SteamID : ‘’) + “/”`, so
**without Steam, saves go to a different folder** — and with **Steam Cloud** enabled, the two
folders diverge.

**What to do.** **ALWAYS launch Steam before the game.** The mod writes `[Path]` at startup with the folder
in use: *that line is the answer to “where did the saves go?”*

## 4. ⚠️ “Crossed” execution — one kills while the other has the prompt *(RARE)*

**What you see.** An enemy is **killed by one player**, while **the other** has the execution prompt on the **same**
enemy. For the player who had the prompt, the enemy may become a **ghost**:
visible, untargetable, AI disabled.

**Why it’s rare and hard to reproduce.** It requires **two players acting on the same enemy
at the exact same moment, but in different roles** *(one kills it, the other executes it)*. The player in control **does not have
control of both machines at the same time**, so it cannot be controlled on command.

**Status.** The **general** case of the ghost has been **resolved**. **This “crossed” scenario is the rare exception.** The execution itself no longer freezes
the game *(verified: `EXECUTION` ON → OFF, no crash)*. **⇒ If it happens again, we need the full log.**

**What to capture**, from both machines:
```
ACT_...Execution · EXECUTION (enabled/disabled) · TRYING TO KILL ENTITY
[EnemyTick] with the FULL ID of the enemy (sp|scene|name|x|y), before and after execution
[EnemyKill] and [EnemySync] not in _ids, to see on which machine the death did not occur
```

## 5. ⚠️ A **DEAD** player can **LEAVE THE BOSSROOM** by pressing — **NOT a bug to be fixed**

**What happens.** During the boss fight, **a dead player presses any key** while still
on the ground: **the level reloads** and the player **exits the boss room**.

**This entry is different in nature from the four above, and should be mentioned first.** Entries 1–4 are
**fixed** bugs or **rare residual issues** that remain here *so they can be recognized if they happen again*. **Not this one**:
it is a **consequence of the game’s architecture** that we have **decided not to fix**, because **the only way to
fix it costs more than it’s worth.** ⇒ **It is an ACCEPTED COST**, not a task that has been postponed.

## 6. ⚠️ **Skipping** the boss cutscene **prevents the player from respawning** — **RARE · UNREPRODUCIBLE · RECOVERABLE** *(KNOWN ISSUE, not a development flaw)*

**What happens.** During the **boss death cutscene**, the player is **on the ground** *(the death screen
waits for a button press)*. If **they press to skip the cutscene**, the cutscene **closes** but **the respawn does not
occur**: **they remain there**, with a frozen screen and **unresponsive controls**.

**The symptom, in measured terms.** *If **you skip the boss cutscene < 1.5 s before it ends**, the
**respawn does not occur**: `Penitent.Respawn()` **is not called**, the **`FADE` blocker remains active**,
the state remains **`PLAYERDEAD`** ⇒ **the player remains there.***

**The cause, as measured.** The fade of the **death screen** lasts **1.5 s** and **ends in `Respawn()`**
*(`DeadScreenWidget.cs:87-96`: `Fade(toBlack: true, 1.5f, 0f, … Respawn())`)*. When the cutscene is
**canceled**, it closes by calling **`FadeInOut`** *(`Cinematics.EndCutscene:154-190`)*. And **`FadeWidget` has
ONLY ONE `currentTween`, and whatever calls it next kills it** *(`FadeWidget.cs:48-72` and `:190-222`)*. ⇒ If
`Cinematics.EndCutscene` arrives **while that fade is still in progress**, its `FadeInOut` **kills the tween** ⇒
**`OnComplete` doesn’t run** ⇒ **`Respawn()` never runs**. And `CHERUB_RESPAWN` *(which **death itself triggers**)* is
**exactly** the condition that **prevents `FadeInOut` from releasing the `FADE` blocker**
*(`FadeWidget.cs:212-215`)* ⇒ **black screen and locked input even though the player could still press buttons.**

**⇒ The threshold, measured:** button pressed **≈1.3 s** before `VideoEnded` ⇒ **locks** · at **≈2.7 s** ⇒ **works**.

**If a player reports this issue:** *"I pressed to skip the boss cutscene while I was dead,
and it just stayed there"* **this is the bug**, and everything is already documented here: **we need the log.**

## 7. ⚠️ The **THREE GAPS** IN THE EFFECT RELAY — effects and projectiles that **didn’t reach the other machine** (This should be fixed, but unfortunately testing hasn’t checked them yet; it will be resolved over time)

**What you see.** The effects relay covers **only** what passes through the `PoolManager`. Three
categories **do not pass through** (or do not reach it), and the result for the player is **twofold**:
- **the effects** from those categories **do not appear** on the other player’s machine: the player sees the
  boss charging, and **the attack materializing halfway** or not materializing at all;
- **projectiles** are worse, and there are **two distinct symptoms**: **(a)** the projectile **is not visible**, and
  **(b)** *depending on who the attacker is in the `Hit`* **the damage doesn’t register at all**.

### Known Issues:

| # | issue | where | status / why it was left out |
|---|---|---|---|
| **1** | `BossJumpAttack.InstantiateArea` — the **two `Object.Instantiate`** calls for the jump objects | `BossJumpAttack.cs:269-277` | does not pass through the `PoolManager` ⇒ **no hook could see it** ⇒ **encapsulated in the code with a `Postfix` that calls the relay. For `ElderBrother`, **this is not enforceable** *(`instantiateOnStart`/`instantiateOnEnd` = `<null>`)*; **for the Amanecidas, it is** |
| **2** | `BossTeleportAttack.InstantiateArea` — the teleport objects | `BossTeleportAttack.cs:73-81` | same as above, a raw `Instantiate` ⇒ **closed**. **No measured boss uses it** |
| **3** | **Common enemy projectiles** — the family declared as a private `ProjectilePool`: **`BellGhostVariant` · `RangedBoomerang` · `MeltedLady`** | `ProjectilePool.cs:12-33` · the three attacks | **the in-game test** remains |

- ⚠️ **And the following are excluded, by explicit declaration** *(as already noted in the report)*: the **~18 `Object.Instantiate`
  **raw** instances in bosses *(Piety 3, Snake 2, Crisanta 2…)*, the **cosmetics** outside the filter *(`CorpseAnimationFx`,
  `LandingFx`, `CorpsesParticlesJump` — their pools **do exist**, 30 / 1 / 1 instances, but they don’t deal damage)*,
  **camera and audio**, and **the entire player channel** *(`EffectType`: only **3** members instantiate a prefab from the
  pool, and they’re all cosmetic)*.

### And what to do in the meantime:

**It’s known, and it’s documented.** If a player reports *“I can’t see the bullet”* or *“That shot didn’t do
anything to me”*, **it’s not a regression and shouldn’t be reinvestigated**: it’s one of the issues listed here *(`RESOLVED`/`UNRESOLVED`
specify **which**)*, and it’s all already written here. **No logs need to be collected to identify it**—the signatures below
are sufficient.

## 8. ⚠️ The **BOSSES**: the **aim** remains on the boss *(23)* and **12 out of 17** without the **simulated death** — *(DECLARED LIMIT, accepted as-is — NOT a bug currently being addressed)*

> These aren’t defects we’re tracking: they’re **two boss limitations** that **haven’t been extensively tested**.
> They’re in progress: if a player reports them, **this is it**.

**What we’re seeing.** Two different things, both **per boss**:
- **A)** The boss **seems to ignore** the other player: it continues to spin, jump, and charge **toward the
  player on the machine simulating it**, even when the other player is closer;
- **B)** In the peer’s machine, the boss **remains “standing”**: `Kill()` has been called, but **the
  game’s death sequence doesn’t trigger** ⇒ no animation—**death isn’t played out**—and the boss remains frozen in the last animation frame.

### A) THE AGGRO STAY ON THE MASTER — **one boss out of twenty-four**

**Effect on the player.** ⇒ **The boss always chases the player controlled by the emulator**: as for the other player, the
boss **acts as if it doesn’t see them**. *(Aggro, however, works fine: `ApplyAggroTarget` sets `enemy.Target = proxy` for
every enemy, including bosses, and after step ② that target **is also vulnerable** ⇒ **the boss targeting the peer
hits it**. Only **the targeting** of the other 23 does not read `Target`.)*

**The fix, for each boss.** **The same pattern as `f3084ea`** — a `Postfix` on the aim point that sets the
position of the **proxy** when the target *is* the proxy, and **nothing** when it isn’t — **but the right spot
must be found for each one**: `GetTargetPredictedPos` **exists only once** in the entire `Assembly-CSharp` *(`:56000`)*, and
in the decompiled code there is no other member with `Predict` in its name ⇒ **it is not copied**: you look at **which method**
that class uses for aiming, and patch that one. The **233 reads are a map of where to look**, not 233
changes.

**And for the test, this is a boss-specific issue, not a run**: the test is *“the boss charges at the player it is
targeting”*, and it must be observed **on the simulation machine**.

### B) 12 OUT OF 17 BOSSES WITHOUT THE **STAGED DEATH**

**What.** The `Death()` whitelist covers **5 types** *(`EnemySyncManager.cs:398-406`)*: **`ElderBrotherBehaviour`
· `LesmesBehaviour` · `QuirceBehaviour` · `PerpetuaBehaviour` · `EsdrasBehaviour`*. **The other 12 are intentionally excluded**
— their `Death()` affects the **`Penitent`**, the **input**, or the **camera**, and **would have caused more damage
than the AFK player leaving**:

| Excluded type | What its `Death()` does *(and why it's prohibited)* |
|---|---|
| `Amanecidas` | `Penitent.Status.Invulnerable = true` |
| `BurntFace` | `Invulnerable` **+** `ProCamera2DShake` |
| `Crisanta` | `Penitent.DrivePlayer.MoveToPosition` *(redux and non-redux branches)* |
| `Isidora` | `Invulnerable` **+** `MasterShaderEffects` **on `Penitent`** |
| `PontiffGiant` | `Invulnerable` |
| `PontiffHusk` | `Invulnerable` **+** `SetBlocker(“END_BOSS_DEFEATED”/“HW_…”)` **+** end-of-battle scroll/camera |
| `PontiffOldman` | `Invulnerable` |
| `PontiffSword` | `Invoke(“ActivatePlatform”)`: touches the **room geometry** |
| `Snake` | `Invulnerable` **+** `SetBlocker(“TPOReposition”)` |
| `TresAngustias` | `Invulnerable` **+** `ProCamera2DShake` **+** `SetBlocker(“CINEMATIC”)` |
| `WickerWurm` | `Invulnerable` **+** `Unattacable` *(via `StartDeathSequence`)* |
| `SingleAnguish` | nothing is prohibited, **but it is not a story boss**: its death is controlled by `TresAngustiasMasterBehaviour` *(outside the whitelist)*, and triggering it on its own would leave **the trio in an intermediate state** |

**Effect on the player.** For those 12, **the boss does not trigger the death animation on the dead character**: `Kill()` sets
`Status.Dead` to true and triggers the cutscene, but **it does not go through `Behaviour.Death()`** *(only the lethal hit
from `Damage(Hit)` calls it, which cannot reach that point: the guard `Penitent.Life <= 0` prevents it)*. ⇒ **The boss remains
“standing”**, motionless, with the sprite active and the tree frozen. **The C fix partially addresses this** *(the corpse does not
remain visible upon returning, `[BossCorpse]`)* — *partially*: **the death animation is a different matter**, and that is what
is missing.

**The fix.** **One type at a time, looking at what its `Death()` does**: the whitelist expands **only** if that
method doesn’t touch `Penitent`, the input, the camera, or the geometry. For the 12 above, **this isn’t an oversight**: it’s the
code that says no. ⇒ **The task is to check 12 methods**, not to expand a list.

## 9. ⚠️ The **FOUR BOSSES** without the “classic” death flag — **Amanecidas · Perpetua · PontiffHusk · Viridiana** *(⚠️ **TO BE TESTED** — this is not a confirmed bug: **data is missing**)*

**The four.** `Amanecidas` · `Perpetua` · `PontiffHusk` · `Viridiana`.

**Why they’re different—and it’s not just a taxonomic detail.** They are **DLC or additional events**, injected into a
game whose **boss architecture was already finalized**. To **avoid altering** existing save files *(a new flag
in an old save file is a field that doesn’t exist)*, their status is **local** and completion is tracked in
**separate subsystems** *(**Jibrael**’s counter for the Amanecidas, the **arcade** route for Husk, the
**script** for Viridiana, the **one-time-only** encounter with Perpetua)*. ⇒ **They don’t have a “classic” death flag** to
read, and this isn’t an oversight on the part of the other three: it’s **how they’re designed**.

**What we KNOW** *(verified)*:
- the **`<scene>_BOSSDEAD`** flag does not exist for them: none of the four can be derived from the scene name;
- the mod **propagates the KILL** — *an entity whose health has reached zero* — **not** `Status.Dead`; and on the
  receiving end, it **reconstructs** `Status.Dead` from the predicate **`Entity.Dead || CurrentLife <= 0`** *(`:5816` on the
  sending side; `Kill()` / `KillInstanteneously()` / `Damage(Hit)` on the receiving side)*;
- ⇒ **so the channel might already cover them**, without a new flag and without a new line of code;
- and `Status.Dead` **is not** the detection predicate: at **every** point where the mod reads it, it is an
  **idempotence guard** — *“the death sequence has already started”* — (`:465`, `:6302`, `:6362`, `:6714`,
  `:7100`, `:7136`) and **never** the question *“is he dead?”*.

**What we DON’T know—and that is the whole question—** is whether their death actually depends on **`CurrentLife <= 0`
on the machine that kills them**. This isn’t an idle question: the subsystem-by-subsystem analysis shows that they shut down
**outside the life channel**—that is, **exactly** the case where the predicate `:5816` might not detect anything.
⇒ **One question:** *Does the death of these four bring life to zero **on the machine that kills them**, or does it
shut them down elsewhere?*

**Status.** **TO BE TESTED — not a confirmed bug.** **Don’t touch anything until you see the log of a
kill**, and **it’s not in progress**. The test requires **one run with the DLCs and a log search**.

**If a player reports** *“I killed him, but he’s still standing on the other side”* **about one of these four**, that log **is**
the missing piece of evidence: it must be obtained **before** modifying the predicate. THEREFORE: **Kill one of the four** and search the log for the line
recording the kill:

```
[EnemyKill] …|<Name>|… DEAD Dead=… health=0 … active=… pos=(…)
   <- The actual search is “[EnemyKill]” + ‘DEAD’ + “health=0” (returns a decimal: 0.0 or 0.0)
```

| result | what it means | what to do |
|---|---|---|
| **the line is there** | the channel **already covers them**: the predicate detects them and the death event propagates | **nothing to do** — the entry closes **with the measure**, not with a fix |
| **the line is NOT there** | the predicate **doesn’t see them**, and death fades away another way | **the fix is there**: extend detection to `Status.Dead` — the only path the work item anticipated |

---

## How to Report a Bug *(for Players and Testers)*

**What You Need**, in order of importance:

1. **What you saw**, in one sentence, **and where** *(which room, or what it looked like)*.
2. **What you were doing**: who was the **host**, who was **in the room**, and **what happened right
   before** *(a bonfire, a death, an execution, a room change)*.
3. **The order of events**, even if approximate. *“First X, then Y”* is worth more than a minute of
   description.
4. **The two logs**, one per machine:
   `%USERPROFILE%\AppData\LocalLow\TheGameKitchen\Blasphemous\output_log.txt`
   **And the server log**, `ServerOutput.log`, in the server folder.
5. **And the most useful thing of all: the recipe.** *“It happens when I do A, B, and C, in that order.”*
   **A reproducible bug is a bug that gets fixed.**

**And one thing NOT to do**: *don’t describe the symptom with the cause you imagine.*
*“The enemy teleports because the synchronization is broken”* is **less** useful than *"the enemy was on the
left, and a second later he was back at the starting point"*.
**You observe the symptom; you hypothesize the cause—and wrong hypotheses cost sessions.**
