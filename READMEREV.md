# Blasphemous Seamless — README (rev)

Co-op multiplayer mod for **Blasphemous** (Steam AppID 774361), built on **BepInEx 5.4.23.5** and the
**Blasphemous Modding API 3.0.1**.

## Index

1. [Status](#status) — what the mod does today, what synchronises, what is still open.
2. [Known bugs](#known-bugs) — the bugs that cannot be reproduced on demand, and how to report one.
3. [Installing the mod](#installing-the-mod) — the three components, their SHA256, the install script.
4. [Building and testing your changes](#building-and-testing-your-changes) — build, deploy, bench.

> This file merges `docs/STATUS.md`, `docs/KNOWN_BUGS.md`, `docs/MOD_LOADER_SETUP.md` and `README.md`
> at commit `2e057d5`. **The originals are untouched** and still where they were; the deep document
> remains `docs/ARCHITECTURE.md`.

---

## Status

A co-op mod for **Blasphemous**: two players share **one Cvstodia** — the same world, the same enemies,
the same bosses, the same deaths. You play the game you already own; the mod is a layer on top of it,
and what it does is make the two games tell the same story.

**No connection, no mod.** With nothing to connect to, the game plays exactly like the original — alone,
you would not be able to tell the mod is installed.

---

### 1. What this mod does

It puts one friend in your Cvstodia, and it keeps the two of you in the same story: what dies on your
screen is dead on his, what moves on his screen moves on yours, and the fight you are in is the fight he
is in.

Nothing else changes. This is not a different game, a different save or a different difficulty: it is
the game you know, with a second Penitent in it.

### 2. How you start a game today

There are two doors, and both are in the mod's multiplayer panel.

**"Host session"** — one of you presses it: his game opens the door and he is already inside, because the
host is also the first player. The other one types the host's address in the "Server IP" field and presses
"Connect". This is the way that does not need anything but the two of you.

**The Steam invite** — the host presses "Host session" and then "Steam: Host", and invites his friend from
the Steam overlay. The friend accepts and types **nothing**: no address, no port. The two games find each
other and you are in the same Cvstodia.

⚠️ **The condition:** invites and the player-to-player link belong to **Steam**. They need the Steam copy
of the game, Steam running, and both of you having launched the game from Steam. **On another launcher
(GOG and the like) they are simply not there** — and nothing is lost: you type the **IP**, which is the way
that always works (same network, or an address you can reach).

⭐ **The new thing:** with a Steam invite, two players on **different networks** find each other with **no
address to type and no port to open on the router**. That is the result: before, you either shared a
network, or you had to know an address and open a door.

**And its limits, in two lines:** the host **is** the session — when he closes the game, it ends for both;
the direct link needs Steam, the IP does not; on a minority of networks Steam has to pass the traffic
through its own relay instead of a direct line — it works the same, the first connection can just take
longer.

### 3. What used to be impossible

Two games used to run **side by side**. Two worlds, two sets of enemies, two versions of every fight:
you could stand in the same room as your friend and, in fact, be playing a different game.

Now there is **one world**:

- the enemy you see is **where I see it**;
- if you kill a boss, **it is dead for me too**;
- if you die, **I see you die**;
- if the boss hits me, **I lose health**.

### 4. What syncs, and what it means

- **Enemies and their position** — you see them walk. They do not jump from place to place.
- **Death and respawn** — nobody disappears: the moment your teammate dies you know it, and you know
  when he is on his feet again, so you never have to guess whether he is next to you or on the ground.
- **The boss** — its health bar is on both screens, its attacks come at both of you, and when it dies
  it is dead for both.
- **The effects** — the areas, the trails, the projectiles: you see them **where they are**.
- **The damage** — the boss hurts you even when it is not your machine that runs it.
- **The pause** — it stays **yours**: pausing never freezes your teammate's world. What crosses the
  wire is a **flag**, not a command — the local pause state, one byte on the `Attack` channel `203`
  (`PauseRelay`), repeated every 3 s — and the other machine only **shows** the line
  `YOUR TEAMMATE IS PAUSED` *(sticky, cleared when the beat says running again)*. It never pauses,
  resumes, opens or closes a menu, or writes `TimeScale`.
- **The bonfire** — you get up to a world with the same enemies your teammate has.
- **Who joins later** — he learns what already happened: the enemies that are already dead, the boss
  that is already dead.
- **Life guilt** — a penalty new to this mod: every death takes a bite out of your maximum health, and
  only a bonfire gives it back — and only to whoever sits.

### 5. The problems we had to solve

- The boss threw itself at the player of the machine that ran it, even when it was after the other one
  ⇒ now it goes where it is aiming, and the one it is aiming at is the one it hits.
- A blow of the boss reached one player only ⇒ now it reaches whoever was hit.
- The effects of a fight showed up on one side only ⇒ now on both.
- Death did not look real — an enemy went from standing to a corpse in a single frame ⇒ now it falls,
  and it stays down.
- One player's pause froze the world for the other one, without telling him ⇒ **and the fix is not the
  one it looks like**: sharing the pause was *structurally* wrong, because not every pause is a choice —
  tutorials, dialogues and cutscenes pause the game on their own, through system input blockers, and
  that is nobody deciding anything. Propagated as a command, it left the peer frozen with **no menu
  open**, and the resume could never get through the blocker that was still on: 123 attempts in 2 s,
  then a permanent `timeScale 0`. **(`0246f0d`)** ⇒ today **nothing is propagated as a command**: the
  pause travels as a **flag** in the heartbeat *(channel `203`, `PauseRelay`)*, and the receiver **only
  displays** `YOUR TEAMMATE IS PAUSED` — it never pauses, resumes, opens or closes a menu, or writes
  `TimeScale`. His world keeps running: you know he is paused, and nothing else happens.
- Coming in later, you had no idea what had already happened ⇒ now you learn it.
- When you died, the screen went black and the view stayed where you fell ⇒ now the veil is see-through
  and the view follows the one who is still alive — and in single player you get the game's own death
  screen, untouched.

### 6. What it is today

| What | Today |
|---|---|
| Movement and position of the enemies | ✅ works |
| Death by the other player's hand, animated | ✅ works |
| Ghosts — an enemy killed by the other player leaves no body you cannot hit | ✅ works |
| Boss fight — one boss, one fight, its bar on both screens | ✅ works |
| Cutscenes and dialogues | ✅ works |
| Who joins later — the deaths that already happened | ✅ works |
| The bonfire — the respawn stays aligned | ✅ works |
| Orientation — enemies no longer spin on the spot | ✅ works |
| Blood and hit flash | ✅ works |
| Diagnostics and performance | ✅ works |
| Spectator — what you see when you are the one on the ground | ✅ works |
| Animations | 🟡 acceptable — not perfectly smooth, but the game plays |
| Bosses | 🟡 partial — one boss has been exercised in depth |

⚠️ **The real limits are not on this page**: they are in **Known bugs**, one entry each.

### 7. Known limits

Some bosses — the DLC and the later additions — have no classic death flag, and the per-boss behaviour
has not been tuned for all of them. See **Known bugs**, entries 8 and 9.

### 8. What has not been seen in game yet

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

### 1. An execution followed by an enemy’s death could cause the game to freeze — **It should be fixed, but we can’t reproduce it**

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

### 2. The tutorial rooms are unreliable — sprites and enemies do not respawn at the Prie-Dieu

**What you see.** In rooms with **sprite tutorials**, sitting at the Prie-Dieu **does not cause the
tutorial sprites to respawn**, while **in all other rooms, respawning works**. And in general, those
rooms behave abnormally: that’s where the highest number of hard-to-reproduce
bugs have been observed.

**What to do.** **Don’t worry about bugs in those rooms.** You’ll waste hours on issues that aren’t easily
resolved. A bug observed there involving enemies should be **verified elsewhere** before reporting it.

### 3. Save files move to a different folder if Steam isn’t running

**What you see.** The game appears **lost or corrupted** after launching without Steam.

**It’s not corruption.** The path is
`persistentDataPath + “/Savegames/” + (SteamManager.Initialized ? SteamID : ‘’) + “/”`, so
**without Steam, saves go to a different folder** — and with **Steam Cloud** enabled, the two
folders diverge.

**What to do.** **ALWAYS launch Steam before the game.** The mod writes `[Path]` at startup with the folder
in use: *that line is the answer to “where did the saves go?”*

### 4. ⚠️ “Crossed” execution — one kills while the other has the prompt *(RARE)*

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

### 5. ⚠️ A **DEAD** player can **LEAVE THE BOSSROOM** by pressing — **NOT a bug to be fixed**

**What happens.** During the boss fight, **a dead player presses any key** while still
on the ground: **the level reloads** and the player **exits the boss room**.

**This entry is different in nature from the four above, and should be mentioned first.** Entries 1–4 are
**fixed** bugs or **rare residual issues** that remain here *so they can be recognized if they happen again*. **Not this one**:
it is a **consequence of the game’s architecture** that we have **decided not to fix**, because **the only way to
fix it costs more than it’s worth.** ⇒ **It is an ACCEPTED COST**, not a task that has been postponed.

### 6. ⚠️ **Skipping** the boss cutscene **prevents the player from respawning** — **RARE · UNREPRODUCIBLE · RECOVERABLE** *(KNOWN ISSUE, not a development flaw)*

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

### 7. ⚠️ The **THREE GAPS** IN THE EFFECT RELAY — effects and projectiles that **didn’t reach the other machine** (This should be fixed, but unfortunately testing hasn’t checked them yet; it will be resolved over time)

**What you see.** The effects relay covers **only** what passes through the `PoolManager`. Three
categories **do not pass through** (or do not reach it), and the result for the player is **twofold**:
- **the effects** from those categories **do not appear** on the other player’s machine: the player sees the
  boss charging, and **the attack materializing halfway** or not materializing at all;
- **projectiles** are worse, and there are **two distinct symptoms**: **(a)** the projectile **is not visible**, and
  **(b)** *depending on who the attacker is in the `Hit`* **the damage doesn’t register at all**.

#### Known Issues:

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

#### And what to do in the meantime:

**It’s known, and it’s documented.** If a player reports *“I can’t see the bullet”* or *“That shot didn’t do
anything to me”*, **it’s not a regression and shouldn’t be reinvestigated**: it’s one of the issues listed here *(`RESOLVED`/`UNRESOLVED`
specify **which**)*, and it’s all already written here. **No logs need to be collected to identify it**—the signatures below
are sufficient.

### 8. ⚠️ The **BOSSES**: the **aim** remains on the boss *(23)* and **12 out of 17** without the **simulated death** — *(DECLARED LIMIT, accepted as-is — NOT a bug currently being addressed)*

> These aren’t defects we’re tracking: they’re **two boss limitations** that **haven’t been extensively tested**.
> They’re in progress: if a player reports them, **this is it**.

**What we’re seeing.** Two different things, both **per boss**:
- **A)** The boss **seems to ignore** the other player: it continues to spin, jump, and charge **toward the
  player on the machine simulating it**, even when the other player is closer;
- **B)** In the peer’s machine, the boss **remains “standing”**: `Kill()` has been called, but **the
  game’s death sequence doesn’t trigger** ⇒ no animation—**death isn’t played out**—and the boss remains frozen in the last animation frame.

#### A) THE AGGRO STAY ON THE MASTER — **one boss out of twenty-four**

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

#### B) 12 OUT OF 17 BOSSES WITHOUT THE **STAGED DEATH**

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

### 9. ⚠️ The **FOUR BOSSES** without the “classic” death flag — **Amanecidas · Perpetua · PontiffHusk · Viridiana** *(⚠️ **TO BE TESTED** — this is not a confirmed bug: **data is missing**)*

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

### How to Report a Bug *(for Players and Testers)*

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

---

## Installing the mod

*To **play**, start here: the mod needs three runtime components in the game folder, and one script
installs all of them. To **compile** it, see the next section.*

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

### How to install

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

### After installing

1. Build the mod; its post-build step copies `BlasClient.dll` into `<game>\Modding\`.
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

This section covers **only** how to build the project and how to test your changes. The state of each
subsystem is above, in **Status**; the deep document is `docs/ARCHITECTURE.md`.

---

### 1. What you need to build

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

### 2. Build

```bash
dotnet restore Blasphemous.Multiplayer.sln
dotnet build   Blasphemous.Multiplayer.sln
```

Expected: **0 errors**, about **400 warnings** — **391** at this commit, essentially all `CS1591`
("missing XML comment"). The count moves as XML docs get added: a rise is a signal, and so is a drop
that is not explained by removed dead code.

> `tools/check.ps1` compares against a **baseline of 469** (`-WarningBaseline`), fixed before the
> comments and strings were translated: today's tree sits **78 warnings below it** and the script still
> ends `ALL GREEN`. Pass `-WarningBaseline 391` to have the number reported as the baseline again.

> The scripts in `tools/` locate the solution on their own, so the commands above are the only place
> where the layout is written down.

| project | target | what it produces |
|---|---|---|
| `Blasphemous.Multiplayer.Client` | `net35` | the mod — `BlasClient.dll`, loaded by BepInEx from `Modding/plugins/` |
| `Blasphemous.Multiplayer.Common` | `net35` **and** `net8.0` | protocol and shared types |
| `Blasphemous.Multiplayer.Server` | `net8.0` | the standalone relay, `blas1mpserver` |

The **server is cross-platform by design** (`RuntimeIdentifiers`: `win-x64;linux-x64;osx-x64`).
The **client can be compiled anywhere**, but it only *runs* inside the game, which is **Windows-only**.

### 3. What you need to test your changes

Running the mod requires **Windows** and a working game installation.

| requirement | why |
|---|---|
| **Blasphemous** (Steam, AppID 774361) | the game itself |
| **BepInEx 5.4.23.5** in the game folder | loads the mod; `tools/install-mod-loader.ps1` installs it |
| the game's `Assembly-CSharp.dll` | the patch-target gate below resolves every `[HarmonyPatch]` against it, so it fails at deploy time instead of at runtime |
| a second machine (or a second game instance) | co-op is client/server over TCP; host and peer each need a game running |

### 4. Deploy and verify

Run from the repository root. All the scripts are **relative to their own location** — they do not
hardcode the repository path.

**One command does the whole round** — build, warning count against the baseline, deploy, and a
byte-level check that the DLL now sitting in the game is the one you just built:

```powershell
pwsh -NoProfile -File '.\tools\check.ps1'
```

It stops at the first red and exits non-zero. Green means: `0 error(s)` · **391** warning(s)
*(reported as `-78 below baseline`, the baseline being `469`)* · `273` Harmony targets · `14/14`
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

### 6. Repository layout

```
Blasphemous.Multiplayer.sln   solution: the three projects below
Blasphemous.Multiplayer.Client/    the mod (net35)
Blasphemous.Multiplayer.Common/    protocol and shared types (net35 + net8.0)
Blasphemous.Multiplayer.Server/    the standalone relay (net8.0)
resources/                    shipped assets (SimpleTCP.dll, json data …)
tools/                        build, deploy and verification scripts
docs/                         architecture, status, known issues
```

Build outputs (`bin/`, `obj/`, `publish/`) are **never** tracked.