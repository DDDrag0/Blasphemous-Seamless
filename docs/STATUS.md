# Blasphemous Multiplayer — what you have in your hands

A co-op mod for **Blasphemous**: two players share **one Cvstodia** — the same world, the same enemies,
the same bosses, the same deaths. You play the game you already own; the mod is a layer on top of it,
and what it does is make the two games tell the same story.

**No connection, no mod.** With nothing to connect to, the game plays exactly like the original — alone,
you would not be able to tell the mod is installed.

---

## 1. What this mod does

It puts one friend in your Cvstodia, and it keeps the two of you in the same story: what dies on your
screen is dead on his, what moves on his screen moves on yours, and the fight you are in is the fight he
is in.

Nothing else changes. This is not a different game, a different save or a different difficulty: it is
the game you know, with a second Penitent in it.

## 2. What used to be impossible

Two games used to run **side by side**. Two worlds, two sets of enemies, two versions of every fight:
you could stand in the same room as your friend and, in fact, be playing a different game.

Now there is **one world**:

- the enemy you see is **where I see it**;
- if you kill a boss, **it is dead for me too**;
- if you die, **I see you die**;
- if the boss hits me, **I lose health**.

## 3. What syncs, and what it means

- **Enemies and their position** — you see them walk. They do not jump from place to place.
- **Death and respawn** — nobody disappears: the moment your teammate dies you know it, and you know
  when he is on his feet again, so you never have to guess whether he is next to you or on the ground.
- **The boss** — its health bar is on both screens, its attacks come at both of you, and when it dies
  it is dead for both.
- **The effects** — the areas, the trails, the projectiles: you see them **where they are**.
- **The damage** — the boss hurts you even when it is not your machine that runs it.
- **The pause** — it stops for **everyone**, and **anyone** can start the game again.
- **The bonfire** — you get up to a world with the same enemies your teammate has.
- **Who joins later** — he learns what already happened: the enemies that are already dead, the boss
  that is already dead.
- **Life guilt** — a penalty new to this mod: every death takes a bite out of your maximum health, and
  only a bonfire gives it back — and only to whoever sits.

## 4. The problems we had to solve

- The boss threw itself at the player of the machine that ran it, even when it was after the other one
  ⇒ now it goes where it is aiming, and the one it is aiming at is the one it hits.
- A blow of the boss reached one player only ⇒ now it reaches whoever was hit.
- The effects of a fight showed up on one side only ⇒ now on both.
- Death did not look real — an enemy went from standing to a corpse in a single frame ⇒ now it falls,
  and it stays down.
- One player's pause froze the world for the other one, without telling him ⇒ now the world stops for
  both, and either of the two can start it again.
- Coming in later, you had no idea what had already happened ⇒ now you learn it.
- When you died, the screen went black and the view stayed where you fell ⇒ now the veil is see-through
  and the view follows the one who is still alive — and in single player you get the game's own death
  screen, untouched.

## 5. What it is today

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

⚠️ **The real limits are not on this page**: they are in `BUG-NOTI.md`, one entry each.

## 6. Known limits

Some bosses — the DLC and the later additions — have no classic death flag, and the per-boss behaviour
has not been tuned for all of them. See `BUG-NOTI.md`.
