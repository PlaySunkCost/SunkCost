# Sunk Cost — roadmap, from here to a complete game

Written 19 September 2026 (Dan asked for the whole road, "from now to a
complete game"). This is a **plan**, not a spec: the rules live in
[DESIGN.md](DESIGN.md) and the networking in
[NETWORK_CONTRACT.md](NETWORK_CONTRACT.md); when this file and those disagree,
those win and this file is out of date. Every card here is mirrored on the
Notion **Tasks** board (the same titles, the phase and the epic), which is where
status lives. Change the road in a PR, with a reason.

Sizes are provisional (S = a day, M = two to four days, L = a week or more,
one person, ~20 h/week). Owners are **proposals**, not commitments: the team
is Dan and Idan (Dor is off the project since 18 September 2026; Dan models in
Blender). Nothing here has a calendar date on purpose — the phases are gates,
and each gate is a test with people, not a day on a calendar.

---

## 0. Where we stand (19 September 2026, `main` after PR #78)

**Playable today, over Steam, up to four players:** host or join a friends-only
lobby; walk, sprint, jump, crouch; carry with four slots and two hands, weight
that slows you; the HQ rig (the shops, the court, the plank) with its bridge
to the ship; the ship with the monitor that sails it, the storage room, the
deck TV; the glass tube and the car down to the one dive site and back; days,
the cycle of three, payday, the quota paid at the board, the shop (air tank,
large tank, bright headlamp); air, health, the air tank item; death (by a debug
key only), the body as cargo, dead spectating, End day revives; the plank when
a payday is missed and the run starts over; proximity voice, who-is-talking,
the winch, the bell and the footsteps; noise events on the server (nothing
listens yet); names, colours, the first-person body (unrigged); Unstuck; an
FPS counter; a Windows build on every push to `main`.

**Built since (20 September 2026, `dan/monsters`):** the seven monsters of
design §6 as placeholder shapes — the Elevator Ghost, the Long Walker, the
Weeping Angel, the Charger, the Lure, the Listener, the Impostor — on one
creature skeleton, three drawn at random per dive; damage and leaks, the
friend's patch (once a day) and the patch kit in the shop; the headlamp's
switch (F). **21 September 2026, `dan/dash`:** the dash (Alt) with its rings
and whoosh on every screen, and a thrown item's landing as noise. **Not built at all:** the site vote, dive
conditions, more than one site, the seafloor beyond a wreck and coins, the
run recap screen, the handheld device, the surface radio, cosmetics and
achievements, the quota curve (a flat $500), music and ambience, real art
(everything is code-built props and generated textures), an animation rig, a
main menu worth the name, settings beyond audio, a Steam store page.

**Known debt** (each is a card below): remote players jitter on moving floors
(the car, the sailing ship); the `camera`, `hands` and `inventory` matrices
still use the old hall's coordinates; the contract changes of PR #78 wait for
Idan's read on `main`; the world-loop Steam walk on two machines has not been
recorded since the HQ became the rig.

---

## 1. The road in one look

| Phase | Gate (how we know it is done) | What it is for |
|---|---|---|
| **02 Prototype — close-out** | Four people on the CI build over Steam say the four phrases unprompted ([DESIGN.md §10](DESIGN.md#the-only-test-that-matters)). | Find out whether this is fun before making it pretty. |
| **03 Vertical slice** | One site, three monsters, saving, the recap: a **complete run** end to end at shippable quality for its slice, played by strangers, with nobody explaining anything. | Prove the game, not just the loop. The slice is what the store page and the trailer are cut from. |
| **04 Content** | Four sites, five or six monsters, the loot list, the quota curve, cosmetics — the Early Access feature list, complete and stable in a closed beta. | Enough game to sell. |
| **05 Early Access** | On Steam, at $10–15, with a launch checklist crossed off and a patch cadence. | Ship, then listen. |
| **06 To 1.0** | The post-launch list burned down; the game leaves Early Access. | Finish. |

Within a phase the order below is the intended order; a card can move up if a
playtest says so. Every networked card needs Idan's read (the contract's
verification rule); every card that says *two clients* is not done until a
host and a separate machine have recorded it in `docs/test-runs/`.

---

## 2. Phase 02 — close the prototype (the four phrases)

Goal: the ugly version, played by four. The loop exists; what is missing is
the thing that makes you shout — something down there — and the two rules that
make a run a run (a save, a recap). Nothing gets art here.

### 2.1 Housekeeping first

| Card | Why | Size | Notes |
|---|---|---|---|
| Idan reads the PR #78 contract changes on `main` | The contract's rule: a teammate reads every networking change. #78 moved the way aboard, the housing doors, the well. | S | Not a code card. Done = a comment on #78 or a Decisions row. |
| Move the `camera`, `hands` and `inventory` matrices onto the rig | They assert positions from the old hall; they fail or are skipped today. | S | Editor only. |
| Remote players jitter on moving floors | Parked 18 September (Steam session): other players stutter while riding the car or the sailing ship. Direction: replicate riders in the moving frame's local space for the whole ride, not only through the locked stretch. | M | **Contract.** Two clients. |
| World loop on Steam, two machines, recorded | Nothing since the rig replaced the hall has been walked host + client over Steam end to end (HQ → sea → dive → up → End day ×3 → payday → HQ → plank). | S | A `docs/test-runs/` folder with roles, build revision, latency. |
| Four clients connected for ten minutes | The lobby holds four on paper; only three have ever stood together. | S | Two clients (four, really). |

### 2.2 Something down there — the monsters (built 20 September 2026, `dan/monsters`)

Dan decided the seven on 20 September 2026 (design §6 "The monsters") and
they were built in one pass on one creature skeleton. The Bell Eater's idea
lives on as the Listener. What remains here is the polish.

| Card | Why | Size | Notes |
|---|---|---|---|
| ~~Creature skeleton: server brain, replicated pose, a hearing sense on `INoiseListener`~~ | Done: `Creature` (server brain, `CharacterController` on the Monster layer, pose and target SyncVars, a server-authoritative `NetworkTransform`), `CreatureSenses` (sight, watched, light, hearing), `MonsterRoster` (three distinct kinds per dive). | — | Contract §3 rows "Monster AI and targeting", "Elevator Ghost", "Headlamp switch". |
| ~~The seven~~ | Done as placeholders: the Elevator Ghost (green car on a return trip, the doors slam), the Long Walker (sees you, walks forever at 60 % of walking, stops at the car), the Weeping Angel (frozen while any part of it is on anyone's screen, 2.5× sprint unwatched), the Charger (1.5 s shake, a 20 m rush at 3× sprint, 35 HP + a leak), the Lure (a laser at a lit lamp after a 0.4 s aim; lamps off and it forgets), the Listener (blind; a dark laser at what the noise bus says; crouch is silent), the Impostor (seen by its chosen diver only, wears a crewmate's colour and name, walks at walking speed, 30 HP + a leak, then runs). Tuned with Dan the same day. | — | `docs/test-runs/2026-09-20-monsters`. |
| ~~Damage and leaks~~ | Done: `ServerDamage`, the `leaking` SyncVar (3× drain), LEAK on the visor, the hiss. | — | |
| ~~Patch kit~~ and the friend's hands | Done: hold E on a leaking friend for 3 s (once a day per patient), the $60 kit in Gear & Supplies (one use). | — | |
| ~~The dash key~~ | Done 21 September 2026 (`dan/dash`): Alt, 4 m in 0.25 s in the steering direction, 3 s apart, 4 s of tank, a 20 m noise; client-simulated, judged by the server from the copy's speed; rings out of the boots and a whoosh on every screen; DASH (ALT) on the visor. The air dash is in for Dan to try. | — | Design §3; contract §3 "The dash". |
| ~~A landed throw makes noise~~ | Done the same day: the server judges a Released item's landing from the copy (the thrower simulates it, so `NoiseEmitter`'s collision path could never see it) — an `Impact` at 10 m, scaled by the fall. | — | Contract §3 "A landing's noise". |
| Lamps in the player's colour | The design's "how you identify a shape in the dark"; the colour exists, the headlamp is white. | S | Cosmetic, but the dark is the game. |
| Monster looks and signature sounds | Seven placeholder silhouettes with glowing eyes and generated calls today. Dan's models; a sound designer for the calls, the Ghost's hum, the slam. | L | Phase 03. |
| Tuning from play | Every number is in `MonsterSettings` (Resources) and provisional: three per dive, the Angel's 25 m and 2.5×, the Charger's 20 m and 35, the Walker's 60 %, the beams' 0.4 s aim. | S | Playtest-driven. |

### 2.3 A run is a run — save and recap

| Card | Why | Size | Notes |
|---|---|---|---|
| ~~Save at HQ, load from the menu~~ **built 19 September 2026** | Three named slots per player, picked when hosting; written whenever the crew is at HQ; the run, the box, everyone's upgrades and hands by identity (DESIGN §1). | L | Done (`dan/save`). The main menu card redraws its IMGUI. |
| The run recap screen | "The run recap screen is the ending, every time — give it real production value." First cut: days, cycles, money handed over, who died where, the best haul; on the plank card. | M | Data only now; the production value is Phase 03. |
| The starter site is survivable | Tune the Bell Eater and the coins so a careful crew clears $500 on day one without knowing the rules. "No tutorial." | S | Playtest-driven. |

### 2.4 The gate

**The four-player playtest** — the CI build, four machines, an evening,
nobody explaining. Listen for the four phrases; write the notes in Notion
(*Playtest notes*). If they come, Phase 03. If they do not, the answer is not
more content: it is a card on this list we have wrong, and the next card is
finding which.

---

## 3. Phase 03 — the vertical slice (one complete run, shippable)

Goal: a stranger sits down, plays a run of two or three cycles, dies to
something they understand, and wants to go again. One site at final quality,
three monsters, every system in its real form, the look Dan is building in
Blender replacing the code-built props. This slice is what the store page,
the screenshots and the trailer are made of.

### 3.1 The site

| Card | Why | Size | Notes |
|---|---|---|---|
| Site skeleton: rooms as hand-built prefabs, assembled by a seed each day | Design §6: "hand-built rooms assembled procedurally", "a site reloads fresh every day". Today the site is one wreck on a flat floor. The skeleton is the `DiveSiteBuilder` generalised: a tube landing, N rooms from a set, connectors, loot spawn points per room, the seed on the day state so every client builds the same site. | L | **Contract** (the seed is server state; no client builds a different floor). Idan's module. |
| Site 01 at slice quality — the drowned colony | Ten to fifteen rooms: housing, a wrecked ship's hold, a plant room; corridors; verticality that walking-only can handle. Blocked out first, dressed after the art pass. | L | Dan (Blender) with the skeleton from Idan. |
| Loot list, first cut: junk and artifacts | Design §2: recognisable human junk for volume, alien artifacts for the high-value scares. Eight to twelve items with mass, size (slot / one hand / two hands) and a value range; a rarity per room type. Coins stay as the small change. | M | Data (`LootCatalog`-style, like `ShopCatalog`). |
| Loot spawn points per room, rolled per day | Which item where, rolled on the server at day start, replicated. | S | Rides the skeleton. |
| Bounded content that feels unbounded | No walls: past the last room the floor simply holds nothing. A soft depth fog, the compass still pointing HOME. | S | Look, mostly. |
| Dive conditions: roll 0–2 per day, apply, reveal on the recap | Blackout (the lamps' reach halved), silt storm (fog in, footsteps silent to you too), infestation (a second monster), calm, rich vein (values up). Revealed on the results screen (`BLACKOUT · +30%`). | M | Server rolls, replicated with the day. |
| The site vote at the monitor | Design §6: "vote a site before each of the three dives". One site in the slice, so the vote is between *the same site* and *stay* — build the vote anyway, with the monitor's refusals. | M | **Contract** (a vote is server state). |

### 3.2 The monsters

| Card | Why | Size | Notes |
|---|---|---|---|
| ~~Monster 2 — a hunter that never lets go~~ | Done as the Long Walker (20 September 2026). | — | |
| ~~Monster 3 — a lurker that loses interest~~ | Done as the Lure and the Listener (they forget after 8–10 s of dark or silence). | — | |
| Monster look, sound and signature | Seven silhouettes in the R.E.P.O./PEAK direction, each with a call; the Listener's is the game's second-most-important sound after the elevator's scream. | L | Dan (Blender) + a sound designer for the signatures. |
| Lethality that reads | Design §3: damage depends on the monster, your health, your air. Tune so low air is fragile and a full tank survives one hit. | S | Playtest-driven. |

### 3.3 The player

| Card | Why | Size | Notes |
|---|---|---|---|
| The rig: neck, hips, knees; idle, walk, sprint, crouch, carry | Legs do not animate; the head is cut off with a mesh split. A rigged `GenericCharacter` (or Dan's own diver) makes the cut unnecessary and the friends real. | L | Dan (Blender). Owner-hides-head-bone replaces the cut. |
| The suit and the helmet, seen | The visor is drawn; the suit is a capsule. Dan's model with the tank on the back (the large tank visibly larger — the shop's "art pass"). | M | Dan. |
| Two-person carrying — protocol first, then code | Design §3 / Notion backlog: "write the protocol before any code". Two players on one heavy object, both slowed, the object between them. **Contract first**, then a grey-box card. | L | Contract, two clients. The most networking-risky card in the slice; can slip to Phase 04 without hurting the gate. |
| The handheld device | Design §5: raise it, see detail, lose a hand and half the view. A scan of what is near; the compass big. | M | |
| Why you can't swim — decided | Design §9: thrusters exist, loud, burn air. Decide it (a Decisions row), write it into the suit's description, build nothing. | S | Not a code card. |
| The surface radio — decided | In or out of the small equipment list. If in: a purchasable, the deck talks to one diver. | S/M | Decide first. |

### 3.4 The run

| Card | Why | Size | Notes |
|---|---|---|---|
| The quota curve | "Gentle for the first few cycles, then accelerating past what a careful crew can earn. Most runs end between cycle 6 and 12." A table on `WorldLoopSettings`, and partial scaling by crew size. | S | Playtest-tuned in Phase 04. |
| The recap screen with production value | The ending, every time: the run's arc as a scroll — every day's haul, every death with its monster, the cycle you fell at, the crew's total; music; a **screenshot** button. | M | Dan's UI pass. |
| Cosmetic unlocks through achievements | Design §8: "between runs, cosmetics only". Suit skins for milestones (cycle 6, a body brought up, a solo payday). Local unlock file now; Steam achievements in Phase 05. | M | |
| Personal debt — decided | Design §9: a per-player debt beside the crew quota "would make the title mechanically true". Decide it; if in, a number per player on the board and the visor, paid from the pot. | M | Decide first. |
| Joining between days: who may, with what | Design §7 still open: new versus returning player, gear restoration, quota scaling for a joiner. Decide and build: a returning player gets their saved upgrades; a new one the base kit; the quota scales at the next payday. | M | **Contract.** Two clients. |
| Lobby polish: invite codes | "Friends and invite codes at launch." Paste-a-code already exists; make it a proper code, a copy button, an error that reads. | S | |

### 3.5 The look and the sound

| Card | Why | Size | Notes |
|---|---|---|---|
| Art pass 1: HQ props from Blender | Dan replaces the code-built prop prefabs one by one (the prefabs are the seam: drop a mesh in). Booths, containers, the tower, the crane. | L | Dan. Rolling, no gate. |
| Art pass 2: the ship | Hull, tower, the tube's housing, the storage room, the TV. | L | Dan. Done 23 September 2026 (`dan/ship-look`): thirty generated parts on a 48 x 20 m deck. |
| The way aboard from the HQ | The ship's stair came off on 23 September 2026 (Dan: no stairs on the ship). At the rig the bridge still lands on the tower roof, 6 m over the deck, with no way down but a jump. Dan wants a fixed place on the HQ - a bridge the crew walks from the ship to the HQ - and maybe the HQ lower; the boarding point stays at `ShipStubBuilder.StairFoot` meanwhile. | M | Dan, 23 September 2026: "do it later". Blocks the cabin matrix's boarding rows until done. |
| How high the ship floats | Done 23 September 2026 (Dan: "make the ship move above the water"): the sea is 4.5 m under the deck at sea, one number in `ShipStubBuilder.SeaLevelY` mirrored by `DiveSiteSettings.seaLevelY`, the car and `PlayerSubmersion`'s fallback. The ride's dry stretch grew from 1 m to 4.5 m; the cabin matrix is the check. At the rig the water is 12 m down (`HQPlatformBuilder.WaterY`), unchanged. | M | |
| Art pass 3: the car and the tube | Glass that reads as glass, the ribs, the gate, the seafloor doorway. | M | Dan. |
| Textures: a stylised palette | Generated textures are placeholders. A flat-shaded palette with bold silhouettes; one trim sheet for metal, one for the colony. | M | Dan (Unity AI or procedural, per his choice). |
| Music on the ship, drones below | Design §2: "ambient drones underwater, real music on the ship". Two ship tracks (docked, at sea), one drone set, the HQ's morning. | M | A composer or a licensed pack; the `AudioLibrary` slots exist. |
| The signature sounds | The elevator's scream (the title's promise), the Bell Eater, the leak. Commissioned. | M | External. |
| Sound pass: every slot in `AudioLibrary` a real recording | Doors, the bell, the winch, footsteps by surface, the coin pickup, the buy, the plank's splash. | M | freesound/Sonniss per the design's licence note. |
| Lighting and fog per world | HQ's dawn, the sea's night, the site's dark: one `WorldLook` each, tuned; the flood towers and lamps baked where static. | S | |

### 3.6 The frame around the game

| Card | Why | Size | Notes |
|---|---|---|---|
| Main menu | Title, Host, Join (code), Continue (the save), Settings, Quit. The Steam name and the colour on it. | M | Replaces `PrototypeSessionUI`'s IMGUI. |
| Settings: video, audio, controls | Resolution/fullscreen/vsync/quality; the audio page exists; rebindable keys (Unity Input System is already the input path — confirm before this card). | M | |
| Performance budget and a pass | 60 FPS on a mid laptop at 1080p: static batching is in; next are shadow cascades, the lamp count, the TV's second render, GPU instancing on props. A `PerfReport` row per scene in every test-run. | M | |
| Crash and desync reporting | A log bundle button in the menu (Player.log, the F3 state, the build revision) — the playtesters' bug reports become useful. | S | |
| The full-run matrix covers the slice | Every rule above has a row in an editor matrix or a test-run; the `fullrun` job walks a whole run in one go. | M | Rolling. |

### 3.7 The gate

**The stranger test**: three groups who have never seen the game play the CI
build over Steam without a word from us; we watch (a recording, the log
bundle) and read the recap screens. They understand death, the quota, the
elevator; they want to go again. Then Phase 04.

---

## 4. Phase 04 — content (the Early Access feature list)

Goal: enough game to sell, stable. The numbers from the design: **four sites,
five or six monsters**, the loot list, the conditions, the quota curve that
ends runs between cycle 6 and 12.

| Card | Why | Size | Notes |
|---|---|---|---|
| Sites 02, 03, 04 — deeper, less human | Design §2: shallow is colony housing and wrecks; deeper is progressively less human "until nothing makes sense. Never explained." Each site: its room set, its depth (a longer ride, a longer scream), its loot table, its monster roster. | L ×3 | Dan builds, Idan wires. The skeleton makes each one a content job. |
| Monsters 4, 5, (6) | Behaviours that are not "hunt" or "lurk": a swarm that follows noise trails; a thing that takes the loot; a thing that waits in the car. | L ×3 | Idan. |
| The full loot list | Thirty to forty items across the four sites; the alien tier's values and weights; the two-handed monsters of loot (the artifact you cannot stow). | M | Data + Dan's models. |
| The quota curve, tuned | Runs end between cycle 6 and 12 for a careful crew; a solo run is harder, not impossible. | M | Playtest-driven across the beta. |
| Upgrade catalogue, final | Keep it small: the large tank, the headlamp, the patch kit, plus two or three decided here (a tool? thrusters if the swim answer lands?). Prices against the curve. | M | |
| Conditions, the full set | Every condition from §6 with its recap line; two per day at the deepest site. | M | |
| Cosmetics catalogue | Suit skins and lamp colours as achievement unlocks; a dozen. | M | Dan. |
| The four phrases, on purpose | Voice lines are the players'; but the game's own words (the monitor, the board, the panel, the visor) get a writing pass — one voice, the pirates', played straight. | S | |
| Localisation-ready | Every string through `HQSigns`-style tables already; the visor and the menus too; a second language as the proof (Hebrew — the team's). | M | |
| Accessibility | Subtitles for the who-is-talking and the game's sounds, colour-blind-safe player colours, a hold/toggle option for sprint and crouch, a field-of-view slider. | M | |
| Controller support | Optional for EA; decide by beta feedback. | M | |
| Host leaves — decided for EA | The contract says the run ends and host migration is out of scope. Keep it; make it graceful (a card, the save survives, "host again from the save"). | S | Contract. |
| Closed beta over Steam | A Steam beta branch, twenty testers, two weeks; a bug list burned down to zero crashers and zero desyncs. | L | The gate. |

**The gate:** the beta's last week has no new crashers, no desync a tester can
reproduce, and the median run lasts past cycle 4.

---

## 5. Phase 05 — Early Access

Goal: on Steam at $10–15 with everything a launch needs, and a cadence after.

| Card | Why | Size | Notes |
|---|---|---|---|
| Steamworks: the app, achievements, rich presence, cloud saves | The save is host-owned and local; cloud saves make it survive a machine. Rich presence: "At sea · day 2 of 3". | M | The Steam app id replaces the test id in `PrototypeBuildIdentity`. |
| Store page: capsule art, screenshots, the trailer, the description | Cut from the slice. The trailer is the elevator's scream and "NO NO NO NO NO—". | L | Dan; an artist for the capsule. |
| The launch checklist (Notion) | Age ratings, the EULA, the privacy note (voice goes over the host), a press kit, a Discord, the support mail, day-1 known issues. | M | Not code. |
| Release build hygiene | Development-only keys (K, L) compiled out; the F3 overlay behind a flag; the build revision on the menu; crash logs to a folder the support mail can ask for. | S | |
| A patch cadence | Weekly for the first month, then fortnightly; a public roadmap page on Steam mirroring this file's Phase 06. | S | |
| Launch | Push the button. | — | Dan. |

---

## 6. Phase 06 — to 1.0

What EA is for: listen, then finish. The candidates, in the order the design
suggests, to be re-cut by the players' feedback:

- **Sites 05 and 06**, the deepest, "where nothing makes sense".
- **Host migration** only if it turns out cheap (contract §6) — otherwise a
  save-and-rehost flow that feels like one.
- **The two-person carry** if it slipped from Phase 03.
- **Thrusters** as the swim answer, if decided: loud, air-burning, creature-drawing.
- **A daily seed / weekly challenge** — the same site for everyone, a leaderboard of cycles survived.
- **Mod-friendly sites**: the room set as a folder; a community site pack.
- **Console feasibility** — a study, not a commitment.
- **1.0**: the recap says "1.0", the price moves, the roadmap page closes.

---

## 7. How we work through it (unchanged, restated)

- One task, one branch, one PR; Dan merges (or says "merge it"). Networked
  cards carry *Touches the contract* and Idan's read; *Needs two-client test*
  cards carry a `docs/test-runs/` folder before they are called done.
- The editor matrices run before every merge of a gameplay card; the debt is
  recorded in the PR when Dan skips them, and paid before the phase gate.
- The design and the contract are updated **in the same PR** as the change
  they describe; this roadmap is updated when a phase's cards change.
- The Notion board is the status; this file is the reason. A card on one and
  not the other is a bug in the process — fix it the same day.

## 8. Risks, named

| Risk | What we do about it |
|---|---|
| Networking eats the schedule (the root page's warning: "it is almost always networking"). | Every creature and carry card is contract-first; the matrices grow with the rules; two-client runs are recorded, not remembered. |
| The monster is not scary. | The Bell Eater comes before any art; the four-phrase test is the gate; the elevator's scream is commissioned early. |
| One modeller (Dan) is also the producer and half the engineering. | Props are prefabs with a code fallback — the game is always playable ugly; the art passes are rolling, never a gate. |
| The save format changes under players. | A version field from the first save; migrations, never a wipe, from EA on. |
| Voice and Steam P2P on bad connections. | The host-relay at 22 m is built; the beta's first survey question is the connection. |
| Scope. | `scope-cop` on every card that is not on this list. |
