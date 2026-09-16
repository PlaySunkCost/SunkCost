# Sunk Cost — design

The living record of what this game is. **This file is the source of truth.** If a
decision here and something someone remembers from a chat disagree, this file wins.
Change it in a PR, with a reason.

Updated 14 September 2026 with the world loop decisions (section 1: three
scenes and how you move between them, the monitor, the flooding cabin, days).
The [original production plan](reference/Sunk-Cost-Build.pdf) is an unchanged
Draft 3 snapshot dated 12 September 2026. It provides production context;
where gameplay rules differ, this file takes precedence. The PDF's open questions
and implementation suggestions are not additional approved requirements.

> A 1–4 player co-op salvage horror game. Around the year 2300, on an ocean world, a
> pirate crew lowers you to the seafloor to work off a debt you will never clear. You
> walk in the dark, drag treasure to a glass elevator, and every time it rises it
> screams your position across the water.

---

## 1. The loop

Three scenes, one cycle of three days between visits to HQ. Decided 14 September
2026 (Dan), including the transitions and the flooding cabin; the earlier "two
surface locations" wording is superseded.

**Scene 1 — HQ, the pirates' base, with the ship docked beside it.** The base and
the ship at the pier in one scene, joined by a bridge you walk across, no loading.
Between cycles only. Pay the quota, buy equipment, and **save**. This is the only
save point, so a cycle is a commitment you can't put down halfway. Miss the quota
here and everyone walks the plank — the run is over. The cabin on the docked ship
is dead: there is no diving from HQ.

**Scene 2 — the ship at sea.** The same ship that was docked at HQ, now alone on
the ocean. Where you live during a cycle, between days. One big **monitor** on
board is the ship's only control: it lists every site and HQ, and choosing a
destination on it sails the ship — if everyone is aboard; otherwise it names who
is missing. No wheel, no lever. From HQ, choosing a site sails to sea; at sea,
choosing another site keeps the ship at sea with a new destination; choosing HQ
sails home. The monitor doubles as the TV surfaced players watch divers on. A
storage room holds everything you've hauled up, as physical objects. A readout of
what you have against what you owe. **No shop.** The glass elevator cabin stands
on the deck: it is the only way down and the only way up.

**Sailing** (decided 15 September 2026). The gangway belongs to the ship: down
on the pier while docked, lifted before the ship moves, stowed at sea. Someone
standing on it is not aboard, and cargo lying on it stops the ship ("Clear the
gangway"). When the monitor accepts a destination everyone is held where they
stand — you can look around and talk, not walk or use items — the gangway
lifts, the engine starts and the ship visibly pulls away for about four
seconds with everyone and everything on the deck riding along at their own
spots; then the screen fades to black, the scene changes, and you fade back in
on the stopped ship at the destination. At HQ the gangway lowers before you
can walk. No docking animation, no steering, no walking on the moving deck in
this version. Loose deck cargo is secured for the trip: a ball thrown just
before departure stops where it is and does not keep flying at the far end.

**Scene 3 — the elevator and the site.** One scene per site, holding the shaft,
the moving cabin and the seafloor; reached only from the deck cabin and left only
into it. Walk, find salvage, carry it back, send it up. One tank of air each, with
capacity determined by tank size. During a dive, air can only be added by using
an air-restoring item; there are no refill stations or passive refills.

**The way down.** Everyone walks into the deck cabin and someone presses the
button. The doors seal, the screen fades to "Putting on suit…" (about 3 s) — that
fade is the scene change — and you fade back in inside the cabin at the top of
the shaft, suit on, seeing whoever has already appeared. When the last of you is
there the cabin descends and the air starts counting. Then the cabin floods:
within about four seconds the water is over your head, and the rest of the 15 s
ride is spent submerged, watching the shaft go by. The bottom door opens only
when the cabin is full. You can walk around inside the cabin the whole way.

**The way up.** The cabin at the bottom is always flooded. Walk in, press the
button, the doors seal, 15 s up. Over the last few seconds the water drains out;
once the cabin is dry, just before the top, you are moved to the deck cabin and
its doors open. The deck cabin never moves and is never wet: every metre of travel
and every drop of water lives in the dive scene. There is no way from HQ to the
seafloor or back; the ship is always in between.

**What is built (15 September 2026, `dan/deck-cabin` and `dan/shaft-tube`,
Dan: "start without day state, just going up and down as we wish"; "the
elevator inside a tube, transparent as well").** The ride above works both
ways with the seafloor car driven by the server; riders are locked only through
the swap and the fades and walk freely inside the moving car. The shaft is a
snug clear glass tube (0.5 m around the car, thin ring ribs, a gated doorway at
the seafloor) that runs from the seafloor up through the surface platform; it
holds water only below sea level (1 m under the top), so the cabin floods and
drains **by geometry** as it passes the surface — no fill timers. The car runs
at 3 m/s and slows to 1 m/s while its floor-to-roof span crosses the surface;
the travel time follows from the site's depth and that profile (about 17.3 s
for the 45 m prototype site), the same both ways. When your eyes go under, F3
says `underwater=True depth=…`; nothing else happens yet. Not built yet, on
purpose: the all-aboard rule (the deck button takes whoever stands in the
cabin), once-per-day, the air, the ship shell over the tube's top. If someone
stays below, the car goes back down for them, empty; the monitor will not sail
while anyone is below.

**A day is one dive.** The day starts when **every living player is in the deck
cabin** and it departs — the button does nothing with anyone missing, nothing
overrides this, and the cabin panel names who. Anyone can ride up whenever they
want; **once you are up you cannot go down again until the next day** — mid-day
the deck button is dead. **Amended 15 September 2026 (Idan and Dan):** the
cabin no longer returns down empty automatically as soon as it unloads. At the
top, after the doors open, it waits a tuned delay (default 3 s), then closes
and descends — but only if at least one living player remains at the dive site;
a disconnected player does not count as living. The empty automatic descent
makes full winch noise, identical to any other move. The day likewise no
longer ends automatically when everyone is up or dead: the crew ends it with a
deck/monitor action, which unlocks on that same living-players-below check —
one check, not two. Three days make a cycle. Between days the ship stays at
sea: the crew can go straight back down to the same site, or change site on the
monitor first. **A site reloads fresh every day**: everything it had is there
again, and anything you dropped on the seafloor is gone. After the third day the
monitor offers only HQ; choosing HQ earlier is allowed (an early "go home").
Either way the ship sails back to HQ and the quota is judged. See "Elevator
rules" below for the full detail and the deliberate body-recovery consequence.

**What travels.** Whatever you carry (hands and slots) comes with you through the
elevator and across the sea. Whatever you put down stays where you put it: on the
seafloor it is lost when the day ends; on the ship it stays on the ship. Cargo
sent up unattended rides the same cabin and arrives, dry, in the deck cabin; it
is carried into the storage room by hand.

**Leaving.** Riding up is safe for you and loud for everyone else — the noise of
your escape tells the ocean where everyone still below is.

### The elevator

It is a real elevator. It carries **any number of people and any amount of cargo
together** — one diver or the whole crew, with or without treasure.

**Anything can be sent up without a passenger.** Load treasure — or a body — pull the
lever outside the cabin (the button inside is for riding with it), and keep
diving. That is the banking mechanic: whatever goes up is safe forever, and you
don't have to leave to secure it.

**There is only one of it.** That is the entire constraint. When it goes up, it is
gone until it comes back — 15 seconds up and 15 seconds back down, 30 seconds
round trip, always, regardless of weight. A rider-controlled return has no
added wait; the unmanned automatic return waits an extra tuned delay at the top
first — see "Elevator rules" below. Anyone who needs it during that window
simply waits.

> **Sending it up is dangerous twice.** The winch screams and tells the ocean where
> you are, *and* your only exit is gone for 30 seconds. The original concept called
> depositing "safety and danger at the same time" — with one shared elevator that is
> now true in two separate ways at once.

> **Why it works.** Your exit is not a thing you have, it is a thing that can be
> somewhere else. Sending treasure up means nobody can leave for 30 seconds. One person
> bailing early means everyone else is stranded — and they watch it happen through the
> glass. A diver at 6% air, standing in the dark, watching the lights rise away
> without them, is the game working exactly as designed.

#### Elevator rules (approved 15 September 2026 — Idan and Dan)

Settled on a call between Idan and Dan. These are approved rules, not a
proposal.

- **Down.** Every living player must be inside the deck cabin before the down
  button does anything; nothing overrides this. The panel names who is
  missing.
- **At the bottom.** The cabin parks open and waits.
- **Up.** Any rider presses the button. During the 1.5 s door seal, any player
  entering the cabin reopens the doors. Once the cabin is moving, the ascent
  cannot be aborted.
- **At the top.** After the doors open, the cabin waits a tuned delay (a new
  serialized field on `ElevatorController`, default 3 s), then closes and
  descends — but only if at least one living player remains at the dive site.
  A disconnected player does not count as living. The empty automatic descent
  makes full winch noise, identical to any other move.
- **Day end.** The day no longer ends automatically. The crew ends it with a
  deck/monitor action. The monitor unlocks on exactly the same server check
  that governs the automatic return above: no living players below. One
  check, not two.
- **Carrying.** Four inventory slots hold everything — air, tools and loot
  compete for the same four. Cabin floor cargo is unlimited and rides up on
  the outside lever.
- **Death.** A dead player's four slots scatter on the seafloor.
- **Disconnect mid-ride.** The player despawns; their four slots drop in the
  cabin and finish the trip.
- **The shaft.** Gated shut at the bottom when the cabin is away; a player
  cannot walk in.
- **Weight.** Has no effect on the cabin. 15 seconds each way, 30 seconds
  round trip, always.

**Consequence, recorded deliberately:** body recovery now only exists during
a dive. If a player dies after the rest of the crew has surfaced, no living
player remains below, the cabin stays at the top, the deck button is dead
(mid-day), and the dead player's loot is on the seafloor, which the site wipes
at the next day's fresh reload. That body is lost. This is intentional.

---

## 2. Setting

| Element | Decision |
|---|---|
| Era | Roughly 2300. Futuristic pirates — raiders and scavengers, not a corporation. |
| Place | An ocean world, not Earth. |
| What's down there | A failed human colony built over much older alien ruins. Shallow sites are colony housing and wrecked ships; deeper sites get progressively less human until nothing makes sense. Never explained. |
| Loot | Mixed — recognisable human junk for comedy and volume, alien artifacts for the high-value scares. |
| Tone | Played completely straight. The game is never funny on purpose; every laugh comes from physics, panic and your friends. |
| Art direction | Stylised and chunky, in the R.E.P.O. / PEAK direction. Low-poly geometry, flat shading, bold silhouettes. Not photorealistic. |
| Audio | Ambient drones underwater, real music on the ship. |
| Ship name | **Black Tide Salvage** |

---

## 3. The dive

| System | Decision |
|---|---|
| Camera | First person with a visible body. |
| Movement | Walking only, no swimming. In-world reason still open. |
| Suits | Identical for everyone, cosmetic skins purchasable. Each player has a distinct lamp colour — that's how you identify a shape in the dark. |
| Air | One equipped tank per diver, available in different sizes. Base capacity targets 12–15 minutes; larger tanks are purchasable and heavier. Air starts counting when the suit fade ends in the descending cabin (section 1). During a dive, air can only be added by activating a carried air-restoring item. No refill stations or passive refills. Item capacity, availability and activation time remain open. |
| Air drain | Faster when sprinting, carrying weight, or damaged. Greed literally costs you air. |
| Damage | Attacks can open a leak. Patch it with an item or a teammate. No regeneration — healing is items only. |
| Lethality | Depends on the monster, your current health, and your oxygen level. Low air makes you fragile, so a bad dive accelerates. |
| Carrying | 4 inventory slots plus one hand. Large objects are two-handed: held low in front with both hands, never stowed, big enough to block part of the view. Every item weighs something; carried weight (hands and slots) fills a grey meter and slows you in a straight line down to 35 %; a full meter turns red and you crawl at 10 % until you drop something — everything else still works. Holding a two-handed object you can still store small items into free slots. Nothing is ever refused for weight. Weight also burns air (not yet implemented). |
| Sharing | Everything can be dropped, thrown and picked up. No trade menu. |
| Friendly fire | None. |
| Safe rooms | None on the seafloor. Everything you need, you carry. |

---

## 4. Death, bodies and equipment

This is the rule set that makes people play carefully, and the reason dragging a
corpse is a decision rather than a gesture.

**Dying costs you everything you bought.** All purchased upgrades are gone if your body is not recovered. Not damaged, not dropped: gone. You come back for the next
dive with nothing but the base kit.

**Unless your body comes up.** If the crew gets your corpse to the surface, you keep
everything. A body is cargo worth recovering, and recovering it is a favour with a
number attached.

**What this creates.** The body competes with the treasure for your **hands** and your
**air** — not for space in the elevator, which has plenty. Carrying a friend back
means carrying less gold and moving slowly while you do it, and you have to say out
loud which one you chose. That is the game's whole question pointed at a person
instead of an object.

**Bodies are heavy.** Two hands, badly slowed, no carrying anything else. Dragging
someone is a commitment you feel.

### Rules that must hold

- **The base kit is always free and always full.** Death takes your purchases, never
  your ability to dive again. Without this you get a death spiral: the player who died
  is now the poorest, so they die more, so they stay poorest.
- **Banked loot is unaffected.** Anything already sent up stays up. Death costs
  equipment, never the crew's earnings.
- **A disconnected player's body behaves exactly like a dead one** — stays where it
  fell, carries their gear, recoverable.

### Recovery incentive and upgrade scope

**Keep full equipment loss when a body is not recovered.** This is an intentional
reason to carry a friend's body to the elevator. Recovering the body preserves
their purchased equipment; the free base kit always lets them dive again.

Expect a small upgrade list. A larger oxygen tank is confirmed; other upgrades
are still undecided. Lights, tools and a surface radio are possible equipment
ideas, not a committed upgrade catalogue. Tune prices and recovery effort around
this small scope during playtests.

**Settled:** a body can be loaded into the elevator and sent up alone, like any other
cargo. Recovering a friend's gear costs you the carry and the elevator's absence, not
your own exit.

---

## 5. Interface

| Element | Decision |
|---|---|
| Visor | Shows the basics as an AR overlay. Loot values appear floating over objects. **Built 16 September 2026 (Dan):** on automatically in the dive (the suit), off on the deck, everyone has it, no item, no key. Air and health bars (both full until those systems exist), depth, a compass strip, HOME (arrow + distance to the tube doorway), crew names + distance in view; every item within 12 m in view gets bracket corners, the one under the aiming dot turns gold with `Name · $value`. Seen through the suit's diving mask: one wide lens with a nose bridge, a thin dark rim with a faint cyan edge, the readouts on the glass. Not a zoom, not see-through-the-fog. |
| Handheld device | Detailed readouts, scans, compass. Raising it narrows your view and occupies a hand — checking your air means not watching the dark. |
| Navigation | A compass in the suit. No map. |
| Leak feedback | Must be unmissable: a hiss in the helmet, bubbles past the visor, a visible jump in drain rate. |

---

## 6. Sites and conditions

| System | Decision |
|---|---|
| Count | Four at launch. |
| Choosing | Vote a site before each of the three dives. |
| Construction | Hand-built rooms assembled procedurally. |
| Size | Bounded content that feels unbounded. No walls or warnings — the built area simply stops containing anything. Oxygen is the real limit. |
| Conditions | Zero to two rolled per dive. Mostly bad (blackout, infestation, silt storm), occasionally good (calm, rich vein). |
| Condition info | Not shown beforehand. A harder dive silently rolls item values toward the top of their range. **Reveal them on the results screen** (`BLACKOUT · +30%`) or players never learn the system exists. |
| Loot values | Randomised within a fixed range each run, always visible through the visor. **Built 16 September 2026:** each loot type carries a range; the server rolls once at spawn and replicates. The first loot is coins in three sizes on the seafloor between the tube doorway and the wreck (small $10–30, medium $40–90, large $150–300); the HQ balls are not loot and show no value. |
| Breakage | Nothing breaks, for now. |
| Monsters | Five or six at launch. Behaviour varies — some hunt until the dive ends, some lose interest once they can't reach you. |

---

## 7. Crew and spectating

| System | Decision |
|---|---|
| Crew size | Up to 4. Solo uses the same rules and is simply harder. |
| Quota scaling | Partial — more players raises the target, but less than it raises earning power. |
| Lobbies | Friends and invite codes at launch. |
| Dead players | Watch any diver, click to switch, and hear what that diver hears (camera switching first; hearing arrives with proximity voice). Talk only to other dead players. Body and loot drop where you died; a body brought up in the elevator keeps its upgrades (section 4). |
| Surfaced players | **One shared TV on the boat** — everyone up top watches the same diver and has to agree who. Talk only to each other. |
| Talking to divers | Proposed: a purchasable surface radio. Its inclusion in the small equipment list remains undecided. |
| Disconnects and joining | Body stays, gear recoverable. Players join or rejoin at HQ, and **on the ship at sea between days** (decided 14 September 2026; they spawn on deck). Never mid-day: while a dive is in progress the lobby refuses with "dive in progress". This does not change HQ-only saving. |

---

## 8. Economy and progression

| System | Decision |
|---|---|
| Funds | Shared crew pot. One shop, at HQ only. |
| Storage | The boat's storage room is display only — a pile of physical objects growing across three dives. Sells at HQ. |
| Banked loot | Anything sent up is safe permanently, even on a total wipe. |
| Quota curve | Gentle for the first few cycles, then accelerating past what a careful crew can earn. Most runs end between cycle 6 and 12. |
| Early return | You can go back to HQ before all three dives. The cycle ends and the quota is judged immediately. |
| Run length | Endless. The run recap screen is the ending, every time — give it real production value. |
| Failure | Miss the quota, walk the plank, run over. |
| Between runs | Cosmetics only, unlocked through achievements. No permanent power. |
| Difficulty | One difficulty for everyone. The site vote is how you choose your risk. |
| Teaching | No tutorial. The starter site is survivable enough that players learn noise matters by getting away with it once. |

---

## 9. Still open

| Question | Where it stands |
|---|---|
| Why you can't swim | Best option: thrusters exist, but they're loud, burn air and draw creatures. Build walking-only for now. |
| Which 5–6 monsters | Build the Bell Eater first — it's drawn to the elevator, so it tests the core loop directly. |
| Elevator timing | Settled: the same both ways, always, regardless of weight; 15 seconds each way was the target. **Amended 15 September 2026 (Dan, shaft tube card):** the time is derived from the shaft depth and a speed profile (3 m/s, 1 m/s while the cabin crosses the surface) — about 17.3 s each way on the 45 m prototype site — and the water level in the cabin is sea level minus the floor, not a timer. A rider-controlled return has no added wait; the unmanned automatic return additionally waits a tuned delay (default 3 s) at the top before descending, and only when a living player remains below (15 September 2026, Idan and Dan). The cabin fills in about 4 s after the suit fade and drains over the last few seconds of the ascent (decided 14 September 2026; numbers provisional). |
| Air-restoring items | Amount restored, availability and activation time are undecided. One equipped tank; no other in-dive air restoration. |
| Between-day joining | Decided: joining on the ship at sea between days is allowed. Still open: new-player versus returning-player eligibility, gear restoration and quota scaling for a joiner. |
| Upgrade catalogue | Keep it small. Larger oxygen tanks confirmed; other upgrades undecided. Full loss without body recovery remains the rule. |
| Personal debt per player | A visible per-player debt alongside the crew quota would make the title mechanically true. |
| Title | "Sunk Cost" vs "Black Tide". Search Steam and a trademark register first. |
| Loot breakage | Deferred. |

---

## 10. Production

- **Engine:** Unity 6 (`6000.6.0f1`), URP. Everyone on the same version.
- **Networking:** FishNet over Steam P2P, host-as-server. See `NETWORK_CONTRACT.md`.
- **Voice:** Steam's built-in voice API. Free. Radio degradation written by hand.
- **Release:** Early Access, $10–15.
- **Team:** 3 developers, ~20 hours each per week. See `CONVENTIONS.md` for ownership.

### First playable HQ harness

The first implemented slice is a grey-box HQ where players can walk,
look, carry basketballs, drop them and throw them. It exists to verify the basic
FishNet ownership loop and Steam P2P transport before underwater gameplay begins.
It has four inventory slots plus hands, but no scoring, economy, saving, combat or
finished art. E grabs, Q drops at the feet, left click uses/throws, and 1–4 equip
or put away slot items. Held items stay rigidly at the camera's hold point;
stowed items are hidden. Every item weighs its Rigidbody mass; the carried total
is server state and drives a grey meter under the slots and a linear slowdown
(empty 100 %, 35 % just under full); a full meter (25 kg) turns red and leaves a
10 % crawl until something is dropped. Holding a two-handed ball, E on a
slot-able item stores it straight into a free slot. Two-handed items are held centred and low,
never stowed, and throw shorter the heavier they are. The fixture is three
basketballs (0.62 kg, one hand), two blue balls (6 kg, one hand, slot-able) and
two two-handed heavy balls (purple 12 kg, black 20 kg); the tuning numbers
(decided 14 September 2026) live in
`Assets/_Project/Settings/Prototype/WeightSettings.asset` and are provisional
until a playtest says otherwise. It holds
**four players** (host plus three). Local/LAN mode supports development on one
computer. Steam mode creates a friends-only Steam lobby when hosting; guests join
through a Steam overlay invite or by pasting the lobby ID, and the server admits a
player only after checking the game marker, protocol, build revision and Steam
lobby membership. No public lobby browser or matchmaking.

Catching (user decision, 14 September 2026): a ball may be caught while flying,
before its release/rest handoff. The prototype's eyes-to-surface reach is
**3.5 m** (Dan, 16 September 2026: "pick up items from farther away, ×1.5–2";
it was 2 m — the shooter norm, Half-Life 2 / Subnautica about 2 m; Deep Rock
Galactic about 3; Minecraft 4.5; Lethal Company about 5, built around grabbing
scrap fast — 3.5 m sits between the shooters and the loot games, and the suit's
bulk reads as the reason), with a tunable 0.35 m aim allowance around the
crosshair and 0.3 s early-press buffer. Hold E to catch an approaching ball; one gesture grabs at most one item.
The server checks reach and line of sight, so assistance does not grab through
walls or steal something held/stowed by another player. Grabbing with a slot item
already in hand stores the new item silently if a slot is free. With four occupied
slots, the next item is overflow: if a slot item is equipped, automatically stow
it in its existing slot and hold the new item (user decision, 14 September 2026).
The four slot assignments remain unchanged. E and number keys are blocked
until it is dropped or used/thrown. Disconnect drops the harness inventory near
the departing player's last position.

Jumping, crouching and hands (decided 15 September 2026,
`docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md`). **Space** is a modest
jump (0.65 m empty), one per press with a short buffer and coyote time; the
carried weight lowers it linearly to 40 % at full capacity, and a two-handed
item forbids it. **Hold Ctrl** to crouch: the collision capsule drops from 1.8 m
to 1.0 m at the same radius, the eyes to 0.85 m, walking to half speed (sprint
ignored), no jumping; you stay crouched under a shelf until there is headroom.
Every held item is held **with both hands, centred in front of the body**;
gloved arms connected to the shoulders are visible to you and to your friends
(small items stay slot-able; the heavy rules are unchanged). The old white
rectangle in front of the avatar is gone; the centre aiming dot is outlined
and turns gold only when what is under it can actually be taken or pressed.
**Q** puts an item down in clear space in front of you whatever the view
pitch; a throw lands **on the point the crosshair is on** (Dan, 15 September
2026): the ball leaves the hands, below and ahead of the eyes, on the low arc
that reaches that point; beyond its range it flies straight at it and falls
short; looking straight down it still goes forward, landing right in front
of the feet, never under you. Crouching and standing blend the camera the same way (0.2 s), no snap.
Escape opens the menu and Escape again closes it. A released item never
starts inside or under a player, and never behind a wall — with no room it is
refused ("Not enough room to drop/throw") and stays in the hands. Players and
loose items do not collide: you walk through balls, and a dropped or thrown
ball can never lift you or push a friend. Excluded for now: mantle, slide,
prone, fall damage, footstep noise, a stealth bonus for crouching.

Camera wall clearance (decided 15 September 2026,
`docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md`): you never see through a
wall, a corner or a ceiling from the first-person view. The near clip plane is
**0.05 m** (it was Unity's 0.3 m default, whose corners reached 0.5 m out of the
head and showed the sea through a room corner), so with the eyes inside the
movement capsule ordinary wall contact needs no camera motion at all. When the
near plane would still cut geometry — a low overhang while the crouch camera is
still blending down, a cabin wall or door moving onto the head — the **view**
is pushed back toward the body along a checked sweep, immediately, and glides
back (0.1 s) once clear; the body, the server's idea of the player and any held
item never move for it. If no clear view exists at all (spawned inside a wall,
a door closed on the head) the screen is covered and nothing can be targeted
until it does. The crosshair, aiming dot and grab reach start from the eye that
actually renders. Owner only; friends' avatars are untouched.

Leaving (decided 13 September 2026): **Leave** returns the player to the host/join
menu rather than quitting. If the host leaves, the room closes and every client is
returned to their own menu; if a client leaves, only that client goes back and the
host keeps playing. A closed room starts fresh when hosted again (the basketball
returns to its spawn). The transport chosen for the first session of a run stays
fixed until the game is restarted.

### The only test that matters

At the end of the prototype, four people play the ugly version. If you hear these
unprompted, you have a game and everything after is content:

> "HELP ME CARRY THIS."
> "WHAT IS THAT?"
> "SEND IT UP!"
> "NO NO NO NO NO—"

If you don't hear them, the answer isn't more content.
