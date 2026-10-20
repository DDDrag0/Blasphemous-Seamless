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

## 2. How you start a game today

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

## 3. What used to be impossible

Two games used to run **side by side**. Two worlds, two sets of enemies, two versions of every fight:
you could stand in the same room as your friend and, in fact, be playing a different game.

Now there is **one world**:

- the enemy you see is **where I see it**;
- if you kill a boss, **it is dead for me too**;
- if you die, **I see you die**;
- if the boss hits me, **I lose health**.

## 4. What syncs, and what it means

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

## 5. The problems we had to solve

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

## 6. What it is today

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

## 7. Known limits

Some bosses — the DLC and the later additions — have no classic death flag, and the per-boss behaviour
has not been tuned for all of them. See `BUG-NOTI.md`.

## 8. What has not been seen in game yet

Seven features are implemented and verified in code, but nobody has observed them in a real match:
the per-boss aim and recited death · the projectiles of three enemies · the four bosses without a
classic death flag (including Viridiana, who may never die) · the two `InstantiateArea` hooks ·
the life guilt in PE02 / Demake · the 10-14 unit position divergence · the co-op gate edge
(connect and disconnect).

They are harmless to play with. Details, log strings and test steps are in the development notes.
