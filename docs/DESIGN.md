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

**The save (built 19 September 2026, Dan).** Every player has **three save
slots** on their own machine, named **Save 1, 2, 3** until renamed. Hosting
shows the three: pick one — an empty slot starts a new run under the name you
type; a used slot **continues** its run (its name, day, balance and last save
time on the row), can be **renamed**, or **deleted** (a second press confirms).
The host owns the run: the slot is written **automatically whenever the crew is
at HQ** — on docking, after paying the quota, after every buy, at the cast-off,
when someone leaves, when the run ends on the plank (the slot keeps its name,
the run in it starts over) and when the host leaves — never at sea or below,
so a cycle cannot be put down halfway. A slot keeps the crew's run (the day
count, payday, the cycle's hand-over, the balance, the run's days and clock,
the court's count), **everything in the storage room** where it lay, and for
**every person who was ever in the run**, by identity — the Steam account, or
the display name on a LAN — their bought upgrades and **whatever they carried**
in their hands and slots at the last save. Guests bring nothing of their own:
join the host's run and you get your gear back. Loose items anywhere else (a
tank dropped on the pier, a coin on the deck) are not kept. A run hosted
without a slot — the editor's checks — keeps nothing. The Continue-from-the-menu
screen is IMGUI for now; the main menu card (Phase 03) redraws it.

**Scene 2 — the ship at sea.** The same ship that was docked at HQ, now alone on
the ocean. Where you live during a cycle, between days. One big **monitor** on
board is the ship's only control: it lists every site and HQ, and choosing a
destination on it sails the ship — if everyone is aboard; otherwise it names who
is missing. No wheel, no lever. From HQ, choosing a site sails to sea; at sea,
choosing another site keeps the ship at sea with a new destination; choosing HQ
sails home. Surfaced players watch divers on the deck TV, its own screen since 17
September 2026 (the monitor doubled as it until then; the TV card in §4). A
storage room holds everything you've hauled up, as physical objects. A readout of
what you have against what you owe. A low sill across its doorway keeps a dropped
coin from rolling out (17 September 2026). **No shop.** The glass elevator cabin stands
on the deck: it is the only way down and the only way up.

**The ship** (rebuilt 23 September 2026 around Dan's generated models; the
sizes, the lounge and the fixes below decided by Dan the same day, being built
on `dan/ship-look` and not yet play-tested). A **48 × 20 m** deck. The **bridge
tower** stands at the stern, with the sailing console on its forward face (the
buttons **SITE 01 · HQ · END DAY**, left to right as the player reads them) and
the crew's screen beside it; the storage room is forward of it, to starboard. The
**glass elevator** stands amidships in its well, open to the sea, with a **low
visible rail** round the well, open only at the grate; the rail's collider is
1.2 m high so nobody jumps it (a jump is 0.65 m). The deck's side is a **1.2 m
bulwark** with an unseen guard above it. The crane and the winch stand by the
well, one container, and deck gear along the sides. The **lounge**
is at the bow: the **TV** in its big cabinet facing aft, two couches in front of
it, and a table between two benches that face each other across it, clear of
the couches. There is no ladder; the tower model has its own. **Sizes** (Dan):
the player stays as is (1.8 m, eyes at 1.6 m) and the props come to it —
furniture (couch, table, bench, toolbox) at 1×, the models being made at a
person's size; deck gear (barrel, crate, lamp, bollard, lifebuoy, pipes, coil,
signs) at 1.25×; the container a real 20-ft box (6 × 2.6 × 2.6 m); the crane
and the winch at 2×. The TV keeps its size but stands lower, its screen's centre
at about the eye height of someone sitting on the couch. **Sitting on a couch**
zooms the view onto the TV, and the TV's channel can be switched from **6 m**, so
from the couch (both in §4: "The couch" and the TV card). The elevator car's light is **warm white** in both worlds, on both
cars. The winch and the bell are mostly 3D: loud near the cabin, still faintly
heard across the deck. The name plates read **BLACK TIDE**, with SALVAGE under it.

**Sailing** (departure decided 15 September 2026; level boarding approved
for the generated HQ on 25 September 2026). HQ and ship decks are at the
same height, 4.5 m above the sea. The fixed, railed bridge belongs to HQ
and reaches an opening in the ship's port bulwark. No tower access or stairs
are required to board. A barrier at each end of the connection closes during
travel; the ship's opening stays closed at sea. Being on the fixed HQ bridge
does not count as aboard. When
the monitor accepts a destination everyone is held where they stand — you can
look around and talk, not walk or use items — the ship casts off (a short
still moment), the engine starts and the ship visibly pulls away for about
four seconds with everyone and everything on the deck riding along at their
own spots; then the screen fades to black, the scene changes, and you fade
back in on the stopped ship at the destination. At HQ a moment of making fast
passes before you can walk. No docking animation, no steering, no walking on
the moving deck in this version. Loose deck cargo is secured for the trip: a
ball thrown just before departure stops where it is and does not keep flying
at the far end. (Until 18 September the gangway was the ship's own and cargo
on it stopped the ship; there is no gangway now.)

**Scene 3 — the elevator and the site.** One scene per site, holding the shaft,
the moving cabin and the seafloor; reached only from the deck cabin and left only
into it. Walk, find salvage, carry it back, send it up. One tank of air each, with
capacity determined by tank size. During a dive, air can only be added by using
an air-restoring item; there are no refill stations or passive refills.

**The way down.** Everyone walks into the deck cabin and someone presses the
button. The doors seal, the screen fades to "Putting on suit…" (about 3 s) — that
fade is the scene change — and you fade back in inside the cabin at the top of
the shaft, suit on, seeing whoever has already appeared. When the last of you is
there the cabin descends and the air starts counting (built 17 September 2026:
from the moment your object stands in the dive world, i.e. the landing at the top
of the shaft, until you are back on the deck). Then the cabin floods:
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
makes full winch noise, identical to any other move. **Settled 16 September
2026 (Dan, building it): the crew ends the day at the monitor's End day
button** — it unlocks on that same living-players-below check, once today's
dive has happened and everyone is up (or the last one below disconnected).
Until it is pressed the deck cabin refuses ("Dive done — end the day at the
monitor"): you go down once a day, no matter how long the day is left open.
Before any dive the button refuses ("Nobody has dived today"). Three days
make a cycle. Between days the ship stays at
sea: the crew can go straight back down to the same site, or change site on the
monitor first. **A site reloads fresh every day**: everything it had is there
again, and anything you dropped on the seafloor is gone. After the third day it
is **payday**: the monitor says so and offers only HQ, the deck cabin refuses
("Payday — sail home"); choosing HQ earlier is allowed (an early "go home").
**The quota is paid at a button (Dan, 16 September 2026):** the board on the
HQ wall — look, E — sells everything in the ship's storage room into the crew
balance. **Every dollar handed over is the crew's (Dan, 17 September 2026):**
nothing is charged out of the balance — the quota is the bar the cycle's
hand-over must clear, not a fee; $510 handed over for a $500 quota leaves the
crew with $510, spendable and shown ("PAID $500 — handed over $510 — every
dollar yours · balance $510"). Only what was handed over *this cycle* counts
toward the quota; money from earlier cycles stays yours and pays no later
quota. Paid: a new cycle, the next dive is day 1. Short **before payday**: not
a loss — the box is banked into the balance and counts toward the cycle, the
board says "SHORT BY $n — sail out and dive again", and the crew sails out on
the same day count to earn the rest (Dan: "not a loss instantly"). Short **at
payday**: **GAME LOST** — day 0, balance $0, a new run (nothing else happens
yet). On payday the docked ship will not sail out again until the quota is
paid ("Pay the quota first"); before payday the crew may pay early or sail
out again on the same day count — days are spent only by dives, never by
sailing. **Home only at the start of a day (Dan, 17 September 2026):** once
today's dive has happened the monitor refuses HQ ("Dive done — End day
first") until the crew ends the day; a day nobody has dived on yet — day 1
fresh from HQ included — may sail home. With no dive taken the board says
"Nothing to pay yet — dive first". The quota is a serialized number (`WorldLoopSettings`,
$500 for the prototype). **Built 16
September 2026 (Dan):** the day counter lives on the crew's day state — day 1
on arrival from HQ, the day in progress from the moment the riders stand in
the car below, *dive done* the moment nobody living is below, the next day
(or payday after the third) when the crew presses End day on the monitor,
day 0 again when the quota is paid. The deck button refuses with a reason in each
case: "Waiting for: <names>" when not everyone is in the cabin, "Dive in
progress — 2 below: <names>" mid-day, "Payday — sail home" after. The monitor
reads "Day 2 of 3 — Site 01" / "PAYDAY"; the visor's top-left corner says
"DAY 2/3"; F3 carries `day=`. No death system exists yet, so *living* means
*connected*: a disconnected player never blocks the day from ending. See
"Elevator rules" below for the full detail and the deliberate body-recovery
consequence.

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
- **The doors, both cabins (Dan, 18 September 2026).** Pressing a button
  starts the doors closing with everyone still free to move. Anyone who
  crosses the doorway or touches the closing doors turns them around: they
  swing fully open at door speed and close again from the start. Who rides is
  decided only once the doors are shut — whoever stands inside then. Stepping
  out fast no longer teleports you back in (the ship) or traps you in the tube
  (below). **Later that day:** moving doors are walked through (the doorway
  blocks only when shut; the leaves have no colliders, greybox) and they
  **never close on someone standing in the doorway** — they hold open until
  the body moves, however long. The 20-second cap counts only while the
  doorway is clear: a crew running in and out gets the doors closed on the
  next crossing after it.
- **The day card (Dan, 18 September 2026).** When the crew ends the day at the
  monitor every player on the ship sees a short black card — `DAY 2 OF 3`,
  `PAYDAY` after the last day — half a second out, a moment held, half a second
  in (`dayCardSeconds`, 1.5 s). Day 1 has no card: the deck panel says so.
- **At the top.** After the doors open, the cabin waits a tuned delay (a new
  serialized field on `ElevatorController`, default 3 s), then closes and
  descends — but only if at least one living player remains at the dive site.
  A disconnected player does not count as living. The empty automatic descent
  makes full winch noise, identical to any other move.
- **Day end.** The crew's End day button on the monitor (settled 16 September
  2026, Dan), which unlocks on the same server check that governs the
  automatic return above: today's dive taken and no living player below. One
  check, not two. Once up, nobody goes down again until it is pressed.
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
| HQ | **Generated-model layout, approved 25 September 2026:** a 60 x 36 m offshore platform at the ship's deck height (4.5 m over the sea). Six legs, lower south catwalk and warm work lights. A single connected **32 x 8 m equipment depot** along the north edge includes the office, upgrade and gear displays, and a clear interior aisle. The separate **pickup chute** discharges onto a marked deck-level pad; the intake console still settles the quota from ship storage. **No save-station prop:** saving remains automatic. The south-west **crew arrival pad** has four spawn positions and the colour panel. The 20 x 12 m basketball court has two scoring hoops and backstop fences; cargo gear and the crane are east, crew rest space south, and the gated punishment plank remains at the south-east edge. A level, HQ-owned bridge reaches a gated opening in the ship's port side. Art uses 24 prepared Meshy HQ models, existing ship props and materials, plus simple Unity floors/markings/collision. `HQGeneratedSetup` rebuilds it; [HQ build handoff](HQ_GENERATED_ART_HANDOFF.md) records assets, layout and validation. No new shop, save, quota or carry rules. |

---

## 3. The dive

| System | Decision |
|---|---|
| Camera | First person with a visible body. |
| Movement | Walking only, no swimming. In-world reason still open. |
| Suits | Identical for everyone, cosmetic skins purchasable. Each player has a distinct lamp colour — that's how you identify a shape in the dark. |
| Body | **Built 17 September 2026 (Dan: "I prefer to use Dor's thing"):** the player's body is Dor's `GenericCharacter` (one unrigged mesh, 1.21 m in its file), scaled ×1.49 to the 1.8 m capsule so a friend is diver-sized. **You see your own body from the shoulders down**: the editor setup cuts the model at the neck into two mesh assets (`Models/Generated`), the owner hides only the head (and the eyes) and a dark plug fills the neck so you never look down into a hollow torso; friends see it whole. Crouching squashes the model as before. **Legs do not animate** — that needs a rig (a neck, hips and knees, or an idle/walk cycle), a Blender job for Dor on the board; the cut becomes unnecessary the day the head is its own bone. The arms stay the gloved rig from the shoulders (`PlayerHands`). |
| Air | One equipped tank per diver, available in different sizes. Base capacity targets 12–15 minutes; larger tanks are purchasable and heavier. During a dive, air can only be added by activating a carried air-restoring item. No refill stations or passive refills. Item capacity, availability and activation time remain open. **Built 17 September 2026 (Dan):** the tank counts for as long as the suit is on — the whole time the player's object stands in the dive world, from the ride down's landing to the deck, the sealed car going up included ("even above water") — and is a new, full tank on the ship. **5 minutes** for the prototype site (a serialized number, `PlayerVitalsSettings`); the design target stays 12–15 for a real site. At **0 air, health goes at 8 a second** (about twelve seconds from empty to dead — between Minecraft's 10 s and a first thought of 20), and at 0 health the ordinary death (body, scatter, spectating). The visor's O2 bar is live: red and blinking with **AIR LOW** under 20 %, **NO AIR** on empty. **L** in a development build takes 5 % off the tank, below only, so the clock can be tested. |
| Air drain | Faster when sprinting (**1.5×**, built 17 September 2026) or damaged (leaks, later). **Carried weight does not cost air** (Dan, 17 September 2026 — reversed from the earlier "greed literally costs you air"). |
| The dash | **Built 21 September 2026 (Dan):** **Alt** is a burst of **6 m in a quarter second** (Dan, the same evening: "longer, like 150 %") in the direction you are steering (forward with none — a sideways dash is the point: it is the Charger's dodge) **with a little lift** — a kick upward of about a third of a metre that gravity takes back, "like a dash in water" (Dan) — **3 s** between two, only below, standing (a crouch stays the silent stance), not with both hands full; **in the air it works too** (Dan wants to try the air dash before deciding). It costs a flat **4 s of tank** (a leaking tank pays 3× on it like everything else) and it is **loud — 20 m**, more than a sprinting step (15): the Listener turns to it. A heavy diver dashes shorter, not longer (the weight factor scales the distance). **What you see:** a ring pops out of each boot, expanding and fading as it trails behind — as if the boots fired the diver forward — and a whoosh at the feet; seen by everyone near, through a spectator and on the TV. The visor says **DASH (ALT)** under the mode with a short bar that fills back over the cooldown. **A landed throw is loud too** (same day): a thrown item's landing carries **10 m** (a drop from the hands about half) — throw a coin and the Listener turns to where it fell. |
| Health | **Built 17 September 2026 (Dan):** 100 points, shown live on the visor's HP bar (red and blinking under 25 %). Lost health stays lost for the dive — no regeneration, healing is items only and none exists yet — and is full again at the next ride down; a revive at End day is a new life. **Inside the sealed car going up the tank still counts but health floors at 1** (the car saved you, barely): dying mid-ride, a body inside a moving car across a scene move, is not a path that exists. Decided for now. |
| Damage | Attacks can open a leak. Patch it with an item or a teammate. No regeneration — healing is items only. **Decided 20 September 2026 (Dan):** a **leak** makes the tank drain **3×** until it is patched; the visor hisses and bubbles and says LEAK. Two ways to patch: a **teammate holds E on you for 3 s** — free, but **once per day per diver** (a friend can fix your leak once a day; the next one needs a kit); or the **patch kit** from the shop's Gear & Supplies (about $60, one use, left click, works anywhere the suit is on). Kit after kit is fine; friend after friend is not. |
| Lethality | Depends on the monster, your current health, and your oxygen level. Low air makes you fragile, so a bad dive accelerates. **Decided 20 September 2026 (Dan):** three monsters kill on the spot (the Elevator Ghost, the Weeping Angel, the Long Walker — the ordinary death: body, scatter, spectating); the other four take health **and open a leak** (Charger 35, Lure 35, Listener 35, Impostor 30). **Nobody fights back**: no weapons, no shove — you avoid, distract and outlast. |
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

**Built 17 September 2026 (Dan, the first spectating card):** nothing kills yet
except a debug key — **K, underwater only**, development builds and the editor.
Dying drops the body where you stood (`<name>'s body`: two-handed, 60 kg, lost
with the site if left below, cargo like anything else if carried to the car),
scatters the four slots and the hands, hides you, and takes you out of every
living count: the dead never block the deck button, the car's return or End day.
When the last living diver is up the dead are carried to the ship unseen; **End
day revives them on the deck** with the base kit — next to their body if it came
up (the body disappears).

**Built 17 September 2026 (Dan, the second spectating card — dead spectating):**
you keep your own camera for about a second (you see yourself fall), a short fade,
then you are on the **nearest living player's eyes** — their screen exactly, visor
and all, with `SPECTATING <name>` at the top and `left click — next player` at the
bottom. **Left click** cycles the living players, below or above water; a target
who dies or leaves is replaced by the nearest living one; nobody living left is
**NO SIGNAL** over your body. You hear what your target hears — every voice at
their distances — **plus every dead player anywhere**; the dead talk only to the
dead, the living never hear them. A living player being watched sees a red
**ON AIR · N watching** mark. Spectating lasts until End day, which revives you as
above.

**Built 17 September 2026 (Dan, after the first three-player test — the car going
back for a diver left below):** when riders come up and someone is still below, the
car goes back for them — but **never with anyone inside the deck cabin**. It waits up
while the cabin is occupied (panel: `Step out — the car is needed below`), puts
whoever is still standing there out on the deck after a grace period
(`carReturnGraceSeconds`, 10 s), and reopens if someone steps in while the doors
close. If the diver below dies or leaves while the car is on its way down, the car
comes straight back and the site closes as at the end of a dive.

**Built 17 September 2026 (Dan, the third spectating card — the TV):** a large
screen on the deck (since 23 September 2026 at the bow, in its cabinet facing aft
over the lounge's couches, §1 "The ship") shows **one diver at a time** from their
eyes — whoever surfaced early watches the others. The channel is the first living
diver below; **E on the screen is the next channel**, wrapping (the screen answers E
from up to 6 m, so from the couch; every other control keeps 3.5 m: decided 23
September 2026, Dan); only divers below
are channels (a diver who surfaces or dies leaves them); nobody below → **NO
SIGNAL**. The caption over the screen reads `LIVE · <name>`. The channel diver's
voice — and the voices they can hear — play **from the TV**, as loud as the diver
hears them, falling off with your distance to it. The TV counts on the diver's
**ON AIR · N watching** mark. Game sounds flagged for broadcast (`BroadcastSound`)
replay at the TV; none are flagged yet (the winch and the doors have no clips).
The picture is a second render of the site, kept cheap (Dan, 17 September 2026:
"very laggy"): half resolution, every other frame, and only while someone stands
within 30 m of the screen; it renders under the site's own fog and ambient
(`WorldLook`, which every world scene carries), so the seafloor is as dark on the
TV as it is for the diver — the same swap serves a dead diver's eyes on the deck.

**The couch (decided 23 September 2026, Dan; built the same day, not yet
play-tested):** E on either couch in front of the TV sits you on the seat nearest
the dot (four seats). Sitting turns you to the screen and zooms until the picture
fills 95 % of the screen's width or height, with the TV's frame and what is behind
it round the edge. E, Space, Ctrl or a movement key stands you up in front of the
couch; left click while seated is the next channel (the implementer's choice of
keys, for Dan to confirm). Friends see you on the couch (the body is squashed to a
sitting height until the model has a rig), and a dead crewmate spectating you sees
your zoomed view. A sail keeps you seated; Unstuck, the plank and a revival stand
you up. **What can be used shows it** (built 23 September 2026 for the ship audit,
SHIP-054; not yet play-tested): what the dot rests on and E would act on gets a soft
warm rim, and the TV screen a thin gold frame. Only you see it, and decoration
never gets it.

---

## 5. Interface

| Element | Decision |
|---|---|
| Visor | Shows the basics as an AR overlay. **Text 1.7× (Dan, 18 September 2026: "can't see anything")** — every label on the glass, the compass heading, HOME and crew tags; the shop's marks on a row of their own under the bars. Loot values appear floating over objects. **Built 16 September 2026 (Dan):** on automatically in the dive (the suit), off on the deck, everyone has it, no item, no key. Air and health bars (both full until those systems exist), depth, a compass strip, HOME (arrow + distance to the tube doorway), the distance to each crew member in view under their name (the name itself floats over every player's head, below); every item within 12 m in view gets bracket corners, the one under the aiming dot turns gold with `Name · $value`. Seen through the suit's helmet visor (Dan's reference picture): one wide chamfered lens, a compass housing in the top edge, a dark rim with a cyan line, the readouts on the glass. Each player's **display name** (Dan, 16 September 2026) is the Steam name by default, changeable in the lobby before entering a room, saved on that machine for the next log in. **The name floats over every player's head (Dan, 20 September 2026, after his reference: "a name tag on him, it should look good")** — plain text in soft white with a dark drop shadow, no plate and **no colour** (the colour is kept for something else), a hand's breadth over the head, turned to face you, the same size on screen out to 14 m and faded out over the next 4 m, depth-tested so it never shows through a wall; seen at HQ, on the ship and below, by everyone but its owner; gone on the dead (the body says whose it is) and when your eyes are at that head (spectating through them). It is a child of the player prefab (`PlayerNamePlate`, built by `PlayerNamePlateSetup`). Every player also has a **colour** (Dan, the same day): one of sixteen fixed swatches, random the first time and saved, changed at the **colour panel on the HQ wall** (look, E, click a swatch on the wheel); the body wears it, the crew tag is written in it, the roster and F3 show a square of it; the headlamp will glow in it later. Not a zoom, not see-through-the-fog. |
| Who is talking | **Built 17 September 2026 (Dan):** top right, under the ON AIR mark, one line per voice you can hear right now with where it reaches you from — `Idan` straight from them, `Idan · TV` through the deck TV, `Idan · via Dan` through the watched player's ears when you are dead, `Idan · dead` from the dead to the dead. Listed only while they are actually saying something (the decoded audio is louder than a whisper), held 0.4 s so words do not flicker, gone when out of range. Yours, not the watched player's: a spectator sees their own list beside the target's visor. |
| Handheld device | Detailed readouts, scans, compass. Raising it narrows your view and occupies a hand — checking your air means not watching the dark. |
| Navigation | A compass in the suit. No map. |
| Leak feedback | Must be unmissable: a hiss in the helmet, bubbles past the visor, a visible jump in drain rate. **Built 20 September 2026:** LEAK blinks beside the HP bar in phase with AIR LOW, the hiss loops at the diver (yours under your ears, a friend's heard nearby), the O2 bar visibly runs. Looking at a leaking friend: `Hold E to patch Skipper` with the seconds counting down, or `Skipper was patched by a friend today — a kit now`; a patch kit in hand: `Left click to patch your leak`. The word back after a patch (`Patched Skipper`, `Patched by Dan`, or the refusal) is a notice, like a refusal. |
| The headlamp's switch | **Built 20 September 2026 (the Lure hunts light):** **F** turns your headlamp off and on, below; it is on again at every ride down. Everyone sees everyone's beam (a remote diver's lamp shows while they are below). The visor works in the dark and says **LAMP OFF (F)** under the mode while it is off. |

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
| Monsters | Five or six at launch. Behaviour varies — some hunt until the dive ends, some lose interest once they can't reach you. **The seven are decided (20 September 2026, Dan) — see "The monsters" below.** |
| Noise | **Wired 17 September 2026 (Dan):** every loud thing below is a server-side noise event with a radius in metres, the thing creatures will listen to. Footsteps: a **walking** step every 0.75 m carries **6 m**, a **sprinting** step every 1.1 m carries **15 m**, **crouch-walking makes no sound at all** (silent, not quieter — crouching is the stealth move). Nothing on the ship. The **elevator** carries **60 m** for as long as it moves ("riding up is safe for you and loud for everyone else"). Numbers are serialized (`NoiseSettings`); an editor gizmo (Sunk Cost → Prototype → Noise gizmo) draws every event's circle in the Scene view for tuning. **Added 21 September 2026:** a **dash** carries **20 m**, a thrown item's **landing 10 m** (a drop about half); the Listener (§6) hears all of it. |
| Sounds | **Built 17 September 2026 (Dan):** what players hear of the elevator — the **winch**, amended 18 September (Dan): heard **only for the 5 seconds the car is near you** — below: the last 5 going down (it arrives at you), the first 5 going up (it leaves you); on the deck: the first 5 going down, the last 5 going up — faded over half a second; **quiet but carrying across the whole site** (0.35 at the car, 250 m reach — "hear it from very far", not loud) for the divers below, **low through the deck** for the ship, and **low (0.06) the whole ride for the riders inside** ("the sound is mostly for those that are down"); and a **bell** ("ding", the movie elevator) when it arrives and the doors open, at the bottom for those below and at the top for the deck. **Feet (18 September 2026):** a footstep sound every stride, on every peer — the same strides as the noise, quicker and louder sprinting, **none crouched** — a thud on landing (a takeoff is silent, Dan); **faint** (Dan: "way lower" — a fifth of the first cut), your own quieter than a friend's. All sound effects live in one **`AudioLibrary`** asset (`Assets/_Project/Resources/AudioLibrary.asset`) with a named slot per sound; every slot starts as a generated placeholder, and replacing one is dropping a `.wav` into the slot in the Inspector — no code. Where the recordings come from: freesound.org (per-file licence — CC0 free, CC-BY needs a credit, never NC for a sold game), the Sonniss GDC bundles (free, commercial), Asset Store / itch.io packs, or a sound designer for the signature sounds (the elevator's scream, the monster). |

---

### The monsters (decided 20 September 2026, Dan)

Seven, on one rule: **you cannot kill anything.** No weapons, no shove — you
avoid, distract and outlast. Each has one sense it hunts by, one thing that
calls it, one way to shake it and one cost. **The deck is always safe**: nothing
comes above water, nothing enters the car or passes the tube's gate. **Three
monsters roam each dive**, drawn at random from the six walkers when the site
loads fresh for the day (a serialized count; the site's size and the dive's
conditions will change it later — for now three, whatever the day). The
Elevator Ghost is not one of the three: it is an event on the car. All seven
are placeholder shapes in the kit (a distinct dark silhouette each, glowing
eyes, a placeholder call in `AudioLibrary`) until Dan's models and the
signature sounds land; nothing about the rules reads the look.

| Monster | Hunts by | What calls it | How you shake it | What it costs |
|---|---|---|---|---|
| **Elevator Ghost** | — (the car) | The car coming **back down for divers still below** — never the ride down, never the first arrival of the day: on a return it rolls about **one in four**, at most once a day. The car arrives and opens as always, but the light inside is **green** for about **30 s**. Nobody is ever shown inside. | **Wait.** When the light turns white the car is clean. | Step in while it is green and the doors slam: **death**. |
| **Long Walker** | Sight | Standing in its line of sight within lamp range. From then it walks after you **until you leave the site**. | Outwalk it: it moves at **60 % of walking speed** (Dan, 20 September 2026: slower than a walk) — a heavy load or standing still lets it close. It stops at the car's doorway and never passes the tube's gate: inside the car you are safe. | Reaches you: **death**. |
| **Weeping Angel** | Being watched | Frozen while **any part of it is on any living diver's screen** within 25 m — edge to edge, not only under the crosshair (Dan, 20 September 2026) — with a clear line of sight (fog and walls hide it; the TV and spectators do not count). | Keep it on screen — which means someone stops working to watch and cannot leave. Unwatched it moves at **2.5× sprint speed** straight at the nearest diver: a glance away is fatal (Dan: "much faster"). | Reaches you: **death**. |
| **Charger** | Sight | Facing you it **shakes for 1.5 s** (the tell), then rushes in a straight line **20 m at three times sprint speed** (Dan, 20 September 2026: faster and longer). | **Step 2 m aside** and it passes; it needs about 3 s to turn and try again. **Alt — the dash** (§3, built 21 September 2026) is the intended dodge: 6 m sideways in a quarter second, once per rush. | A hit: **35 HP and a leak**. |
| **Lure** | Light | A **lit headlamp within 25 m**. It drifts toward the beam; when it sees a lamp it plants itself and **charges for 1 s** — a glow swells at its mouth and a faint thread points at you, the warning — then **burns a beam of light for 3 s** that turns after you at 160°/s, and hurts **once** per beam (Dan, 22 September 2026: "a second of loading, then 3 continuous seconds, one hit"; it should land about 95 % of the time). | **Lamps off (F)** — it loses you about 8 s after the beam goes dark; the visor still works dark. Once it is charging, only two things save you: **a wall between you** or **a dash across the beam** at the right moment. | A beam that lands: **35 HP and a leak**. |
| **Listener** | Sound | Blind. Hears exactly what the noise system says — a walking step 6 m, a sprinting step 15 m, a landed throw 10 m, a dash 20 m (both built 21 September 2026), and the **car's 60 m scream**. When it hears something it plants itself and **charges for 1 s** at the sound — the glow at its mouth, the faint thread — then **burns a dark beam for 3 s** that turns after whoever made the sound (or the nearest diver, when a coin made it) at 160°/s, and hurts **once** per beam (Dan, 22 September 2026). | **Crouch-walk** — silent. It loses interest about 10 s after silence and drifts back to where it stood. Once it charges: **a wall between you**, or **a dash across the beam** at the right moment. The car calls it, but it does not camp the door. | A beam that lands: **35 HP and a leak**. |
| **Impostor** | — (it picks you) | It **chooses one diver** — at random among those still underwater — and **only that diver sees it** (and the spectators and the TV watching them). It wears the **body colour and the name tag of a living crewmate** and walks at **walking speed the whole time** (Dan, 20 September 2026): it reaches you only if you stop, or walk to it as a friend. | Voice: a friend who does not answer is not a friend. Keep away from it for **30 s** and it runs off. | A touch: **30 HP and a leak**, then it **runs away** — and comes back for someone else. |

Underneath all seven: one **creature skeleton** on the server — idle, drawn (to
a heard or seen point), hunting (a diver), lost — with hearing on
`INoiseListener`, sight as a lamp-range line-of-sight check, a replicated pose
the clients smooth. Deaths are the ordinary death; a leak is §3's; the hit
numbers are serialized (`MonsterSettings`) and provisional until a playtest.
The dash is built (§3, 21 September 2026); the site vote's effect on the
roster and the conditions (infestation = a fourth monster) are their own cards.

### Monster polish (decided by Dan, 24 September 2026; built on `dan/monster-look`)

The Impostor is outside this pass and unchanged.

- **The Long Walker grabs.** It catches you when it reaches you, lifts you
  toward its face for about 2 s, then you die the ordinary death. When a wall
  stands between it and a diver it has seen, it keeps to one side and walks
  round the wall rather than pressing into it. This is local wall-following,
  not pathfinding.
- **The Weeping Angel embraces.** Its hands leave its face and take your head.
  A beat of stillness follows, then death at about 2.2 s. There is no lift; the
  style is its own.
- **The victim's view is the same for both.** The victim stays in first person,
  controls locked, the view turned to the monster's face with a small shake.
  Spectators and the TV show the same hold.
- **A dash never saves a held diver.** A dash is refused once the hold is on.
  One pressed in the instant before the hold reaches a guest is cut off, and
  the kill lands wherever the capsule went. A held diver rides no elevator car
  and cannot use Unstuck.
- **The Charger's charge.**
  - **Hit:** 35 HP and a leak, plus a knock-back of about 3 m along its path
    and a hard view jolt. The jolt snaps about 15°, swings back once and
    settles within 0.6 s.
  - **Aim:** the line is aimed during the 1.5 s tell and locked for its last
    0.2 s, then the rush runs straight and never homes. Stepping aside or
    dashing dodges it.
  - **Grab:** a held diver is never knocked back.
- **The lasers: bigger, and dash to dodge.**
  - **Size:** both beams are drawn 0.7 m wide (a half-width of 0.35 m), and a
    hit is exactly the drawn beam touching the body.
  - **Tracking:** the beam tracks its diver, so running alone never escapes
    it; a diver in the open is hit the instant the beam lights.
  - **The dodge:** a dash during the 1 s charge or the burn breaks the lock,
    and the beam holds its heading through the dash. For the rest of that
    beam it only trails at 2.5 m/s, so a diver who keeps moving escapes and
    one who stops is caught. A Charger's knock-back is not a dash.
  - One hit per beam.
- **The Lure's light beam lights the dark site.** Its charge glows, and the
  burn lights the Lure, the seabed along the path and the diver, with a splash
  where it meets a wall.
- **The Listener's dark beam eats light.** A dark haze runs along it and a dark
  patch shows on the wall it hits. Lights near it, headlamps included, are
  dimmed while it burns. All of this is looks only: no lamp switches off, and
  the Lure's sense of lamps is unaffected.
- **Parked (Dan):** the monsters make no movement or footstep sounds, and the
  Angel is silent while it moves. "Leave it like that for now."

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
| The shop | **Built 18 September 2026 (Dan):** things on display at HQ — look at one and press E to buy it from the crew pot; anyone in the crew buys; no menu. **On the platform (later that day):** two counters — **Upgrades** (the large tank, the bright headlamp: click and it is fitted) and **Gear & Supplies** (the air tank and whatever comes: click and it **lands upstairs**, dropping from the chute in the **Pickup** room on the tower's first floor, somewhere on the landing mark, so bought things do not stack). The prompt reads `Air tank · $40 — Press E to buy (pot $460)` and the refusal, if any (`Not enough money: $40 needed, $10 in the pot`, `You already have a large tank`, `Step up to the shelf`). Three things for now: the **air tank** ($40, a real tank that **falls to the floor at the delivery spot** in front of the shelves — anyone may take it), the **large tank** ($300, +50 % air: 7.5 minutes) and the **bright headlamp** ($150, a beam ×1.6 as long and ×1.5 as strong). Upgrades are **one per player**, no refunds, shown as small marks on the visor (`L-TANK  LAMP`); others see only the longer beam — a visible tank on the back comes with the shop's art pass. **Lost when you die and your body is not brought up** (§4); kept when it is. Greybox until Dan builds the real shop: the catalogue is data (`ShopCatalog` in Resources: ids, names, prices, prefabs), a stand is a `ShopDisplay` component on any object, the delivery spot a `ShopDeliveryPoint` marker, so the look, the place and the number of stands can change without touching the rules. Prices against the $500 quota: a good first day pays for one upgrade. |
| Storage | The boat's storage room is display only — a pile of physical objects growing across three dives. Sells at HQ. **Built 16 September 2026 (Dan):** a small walled room at the stern (doorway toward the centre line) with a readout over the door — `$<in the box> / $<quota>` and the balance — and the same line in the visor's corner with what is on you. **Both count the cycle, not the box (Dan, 18 September 2026: "I paid 400 already"):** `QUOTA $400/$500 · ON ME $45` on the visor, `quota $400 / $500 (handed over $400 + box $0) · balance $400` on the board — what was handed over at HQ this cycle plus what the box holds. The server sums the loose items inside the room four times a second (`CrewDayState.BoxValue`). |
| Banked loot | Anything sent up is safe permanently, even on a total wipe. |
| Quota curve | Gentle for the first few cycles, then accelerating past what a careful crew can earn. Most runs end between cycle 6 and 12. |
| Early return | You can go back to HQ before all three dives. **Amended 16 September 2026 (Dan):** sailing costs no day — days are spent only by dives — and docking judges nothing. At HQ the crew *may* pay early at the board; if it does not, it sails out again on the same day count. |
| Run length | Endless. The run recap screen is the ending, every time — give it real production value. |
| Failure | Miss the quota, walk the plank, run over. **Built 18 September 2026 (Dan):** the crew has sold everything, used its three days and is still short at payday — the board says THE RUN IS OVER and the crew **walks the plank** at HQ, a board off the pier over the water, **one by one in crew order**: the jumper is put at its base, free to walk out and back and jump when ready; after **10 seconds** they are pushed. **A gate** (a rail across the board's base, up on every peer while the crew walks the plank) keeps the jumper on the board — free along it, no way back to the pier — and the others on the pier. The last one in the water brings the card — **THE GAME IS OVER · N days · M minutes** (dive days begun this run, across cycles; wall time from the run's start) — for six seconds, then **everything from nothing**: day 0, $0, no cycle, empty hands, no upgrades, everyone alive on the pier at HQ, and **the world as it was found** — every loose item (bought tanks, bodies, whatever lies on the ship or the pier) gone, each loaded scene's fixture spawned again. Sailing, the shop and joining are refused while the plank is on. Nobody can hold the crew hostage on the board: the push, and a leaver simply counts as in the water. |
| Between runs | Cosmetics only, unlocked through achievements. No permanent power. |
| Difficulty | One difficulty for everyone. The site vote is how you choose your risk. |
| Teaching | No tutorial. The starter site is survivable enough that players learn noise matters by getting away with it once. |

---

## 9. Still open

| Question | Where it stands |
|---|---|
| Why you can't swim | Best option: thrusters exist, but they're loud, burn air and draw creatures. Build walking-only for now. |
| Which 5–6 monsters | **Decided 20 September 2026:** seven, in "The monsters" under §6. The Bell Eater's idea (drawn to the elevator's scream) lives on as the Listener; the first one built is the Elevator Ghost, which needs no walking AI. |
| Elevator timing | Settled: the same both ways, always, regardless of weight; 15 seconds each way was the target. **Amended 15 September 2026 (Dan, shaft tube card):** the time is derived from the shaft depth and a speed profile (3 m/s, 1 m/s while the cabin crosses the surface) — about 17.3 s each way on the 45 m prototype site — and the water level in the cabin is sea level minus the floor, not a timer. A rider-controlled return has no added wait; the unmanned automatic return additionally waits a tuned delay (default 3 s) at the top before descending, and only when a living player remains below (15 September 2026, Idan and Dan). The cabin fills in about 4 s after the suit fade and drains over the last few seconds of the ascent (decided 14 September 2026; numbers provisional). |
| Air-restoring items | **Built 17 September 2026 (Dan):** the **air tank** — a small safety-yellow capsule with a valve lying on the site (two per site among the coins, spawned fresh each day like them; 4 kg, one hand, a slot, worth nothing). Pick it up, **left click: half a tank back** — 50 → 100, 0 → 50, 70 → 100, never past full — and it is an **Empty air tank** from then on: grey (its inventory icon too), worth nothing, **left click does nothing, Q drops it** — no throw (Dan, 18 September 2026). **Full air tank** until then. **Breathed from underwater only** (head below the surface; the prompt says so on land and in the car above the waterline) and with the suit on. Activation is instant. Where tanks come from beyond the site (the shop, larger sizes) stays open. |
| Between-day joining | Decided: joining on the ship at sea between days is allowed. Still open: new-player versus returning-player eligibility, gear restoration and quota scaling for a joiner. |
| Upgrade catalogue | Keep it small. **Built 18 September 2026:** the large tank and the bright headlamp, plus the air tank as a consumable (§8, "The shop"). Others undecided. Full loss without body recovery remains the rule — and is built. |
| Personal debt per player | A visible per-player debt alongside the crew quota would make the title mechanically true. |
| Title | "Sunk Cost" vs "Black Tide". Search Steam and a trademark register first. |
| Loot breakage | Deferred. |
| Remote players jitter on moving floors | **Mission for later (Dan, 18 September 2026, Steam session with Idan):** other players "jump" while riding the elevator and while the ship is moving — the remote copy's replicated position fights the moving floor (the copy is placed from network snapshots in world space while the car/ship carries the frame; `ShipDepartureRider.PlaceRemoteCopy` pins riders only for a locked ride). Fix direction: replicate riders relative to the moving frame (car/ship local space) for the whole ride, or ease the copy in frame space. Everything else in that session worked. |

---

## 10. Production

- **Engine:** Unity 6 (`6000.6.0f1`), URP. Everyone on the same version.
- **Networking:** FishNet over Steam P2P, host-as-server. See `NETWORK_CONTRACT.md`.
- **Voice:** proximity speech over the existing authenticated FishNet connection
  (Steam P2P or Local/LAN), with miniaudio device capture/playback and Opus encoding.
  This supersedes Steam's capture API so the requested microphone and whole-game
  output dropdowns select the devices actually used. P and the Audio settings
  mute button toggle transmission; each new session starts muted. Default gain
  is full through 2 m, fading smoothly to silence at 20 m. Both device selectors
  default to Automatic; tests are local, with signal meters. Master/voice volume
  and per-player mute/volume are local. Enabled voice continues in the background.
  Walls, radios, underwater filters and monster hearing from speech remain future
  work. See [voice verification](PROXIMITY_VOICE_TEST_REPORT.md).
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
Escape opens the menu and Escape again closes it. The menu has an **Unstuck**
button (Dan, 18 September 2026): the server puts you back on a known spot of
the world you are in — a pier spawn point at HQ, the boarding point on the
ship, below the car's floor when the car is down or the landing outside its
doorway when it is not — and the menu closes. Refused to the dead, to a rider,
to the jumper on the plank, and within five seconds of the last one. A released item never
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
