# Sunk Cost — design

The living record of what this game is. **This file is the source of truth.** If a
decision here and something someone remembers from a chat disagree, this file wins.
Change it in a PR, with a reason.

Updated 13 September 2026 with the team's latest design decisions.
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

Two surface locations, one cycle of three dives between them.

**HQ — the pirates' base.** Between cycles only. Pay the quota, buy equipment, and
**save**. This is the only save point, so a cycle is a commitment you can't put down
halfway. Miss the quota here and everyone walks the plank — the run is over.

**The boat — the salvage vessel.** Where you live during a cycle, between dives. One
big screen listing every site, which doubles as the TV surfaced players watch divers
on. A storage room holding everything you've hauled up, as physical objects. A readout
of what you have against what you owe. **No shop.**

**The dive.** Vote a site, ride down as a crew in the glass elevator. One tank of air
each, with capacity determined by tank size. During a dive, air can only be added
by using an air-restoring item; there are no refill stations or passive refills. Walk, find salvage, carry it back, send it up.

**Leaving.** Anyone can ride up at any time. Once you're up, you can't go back down —
and the noise of your escape tells the ocean where everyone else still is.

### The elevator

It is a real elevator. It carries **any number of people and any amount of cargo
together** — one diver or the whole crew, with or without treasure.

**Anything can be sent up without a passenger.** Load treasure — or a body — pull the
lever, and keep diving. That is the banking mechanic: whatever goes up is safe
forever, and you don't have to leave to secure it.

**There is only one of it.** That is the entire constraint. When it goes up, it is
gone until it comes back — 15 seconds up and 15 seconds back down.
It returns without an added surface delay, so a diver waiting below waits 30 seconds total. Anyone who needs it during
that window simply waits.

> **Sending it up is dangerous twice.** The winch screams and tells the ocean where
> you are, *and* your only exit is gone for 30 seconds. The original concept called
> depositing "safety and danger at the same time" — with one shared elevator that is
> now true in two separate ways at once.

> **Why it works.** Your exit is not a thing you have, it is a thing that can be
> somewhere else. Sending treasure up means nobody can leave for 30 seconds. One person
> bailing early means everyone else is stranded — and they watch it happen through the
> glass. A diver at 6% air, standing in the dark, watching the lights rise away
> without them, is the game working exactly as designed.

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
| Air | One equipped tank per diver, available in different sizes. Base capacity targets 12–15 minutes; larger tanks are purchasable and heavier. During a dive, air can only be added by activating a carried air-restoring item. No refill stations or passive refills. Item capacity, availability and activation time remain open. |
| Air drain | Faster when sprinting, carrying weight, or damaged. Greed literally costs you air. |
| Damage | Attacks can open a leak. Patch it with an item or a teammate. No regeneration — healing is items only. |
| Lethality | Depends on the monster, your current health, and your oxygen level. Low air makes you fragile, so a bad dive accelerates. |
| Carrying | 4 inventory slots plus one hand. Large objects are hands-only. Weight slows you and burns air. |
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
| Visor | Shows the basics as an AR overlay. Loot values appear floating over objects. |
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
| Loot values | Randomised within a fixed range each run, always visible through the visor. |
| Breakage | Nothing breaks, for now. |
| Monsters | Five or six at launch. Behaviour varies — some hunt until the dive ends, some lose interest once they can't reach you. |

---

## 7. Crew and spectating

| System | Decision |
|---|---|
| Crew size | Up to 4. Solo uses the same rules and is simply harder. |
| Quota scaling | Partial — more players raises the target, but less than it raises earning power. |
| Lobbies | Friends and invite codes at launch. |
| Dead players | Watch any diver, click to switch. Talk only to other dead players. |
| Surfaced players | **One shared TV on the boat** — everyone up top watches the same diver and has to agree who. Talk only to each other. |
| Talking to divers | Proposed: a purchasable surface radio. Its inclusion in the small equipment list remains undecided. |
| Disconnects | Body stays, gear recoverable. Current baseline: rejoin at HQ between cycles; never mid-dive. Under consideration: allow joining/rejoining on the boat between dives within the three-dive cycle. This is not yet approved and does not change HQ-only saving. |

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
| Elevator timing | Settled: 15 seconds down, 15 seconds up. Sending it up leaves a diver below waiting 30 seconds for its return. |
| Air-restoring items | Amount restored, availability and activation time are undecided. One equipped tank; no other in-dive air restoration. |
| Between-dive joining/rejoining | Consider boat entry between dives. Decide new-player versus returning-player eligibility, gear restoration and quota scaling before implementation. HQ-only entry remains the baseline. |
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

### The only test that matters

At the end of the prototype, four people play the ugly version. If you hear these
unprompted, you have a game and everything after is content:

> "HELP ME CARRY THIS."
> "WHAT IS THAT?"
> "SEND IT UP!"
> "NO NO NO NO NO—"

If you don't hear them, the answer isn't more content.
