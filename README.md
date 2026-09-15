# Sunk Cost — starter repo

A 1–4 player co-op salvage horror game. You walk the floor of an alien ocean in a
weighted suit, drag treasure to a glass elevator, and every time it rises it screams
your position across the water.

This repo is the scaffold: conventions, git setup, and the one system worth building
before anything else. It is not a Unity project on its own — unpack it into one.

---

## Day one

### 1. Everyone installs the same things

- **Unity Hub**, then the current **Unity 6 LTS** release. Check Unity's download
  page for the exact version and all three of you install the *same* one — mismatched
  editor versions cause phantom asset reimports and diffs nobody wrote.
- **Git LFS** (`git lfs install`) — before the first commit, on all three machines.
  Adding it later means rewriting history.
- **Rider or Visual Studio**, whichever you like.

### 2. One person creates the project

- New project, **Universal 3D (URP)** template. Not HDRP: your game is lit by one
  torch in near-total darkness, so URP's cheaper lighting costs you nothing visible
  and saves a lot of iteration time.
- Name it `SunkCost`.
- Copy this repo's contents into the project root — `.gitignore`, `.gitattributes`,
  `docs/`, and `Assets/_Project/`.
- `git init`, `git lfs install`, commit, push to a private repo. Invite the other two.

### 3. Everyone reads two files

- `docs/NETWORK_CONTRACT.md` — **before writing any networked code.**
- `docs/CONVENTIONS.md` — folders, scene ownership, the PR rule.

Twenty minutes now saves the rewrite in month five.

For working with Claude, Codex or another assistant, start with the shared
[AGENTS.md](AGENTS.md) and [team workflow](docs/WORKFLOW.md). Claude's entry point
imports those same rules; the four review prompts in `.claude/agents/` can also
be read as checklists by other assistants.

---

## Start the HQ basketball prototype

1. Open this repository with Unity `6000.6.0f1`.
2. Open `Assets/_Project/Scenes/Prototype/Session.unity` and press Play. That
   scene is the menu and the network root; hosting loads the base
   (`HQPrototype.unity`) next to it. Pressing Play inside a world scene starts
   nothing — there is no menu there.
3. Choose **Local / LAN** and **Host**. Up to three other players can choose
   **Join** with `127.0.0.1` on the same computer, or the host's LAN IP on another
   computer. Four players total, including the host.
4. Move with WASD, sprint with Shift, look with the mouse. **Space** jumps
   (lower the more you carry; not with a two-handed ball), **hold Ctrl** to
   crouch (half height, half speed; you stay down under a shelf until it is
   clear). **E** grabs; hold E to
   catch an approaching ball with forgiving aim within 2 m. **Q** puts the item
   down in front of you, **left click** throws at the point you are looking at,
   and **1–4** equip or put away inventory slots. Everything is held in both
   hands in front of you; you and your friends see the gloved arms. The centre
   dot turns gold when what is under it can be taken or pressed. Loose balls
   are walked through and never push or lift a player. Walls, corners and low
   ceilings never show what is behind them: the view stays inside the body
   (`Sunk Cost > Prototype > Apply camera clearance setup` after regenerating
   the player prefab). At sea, step into the glass cabin on the deck and press
   **E** on its panel: the doors close, the suit goes on, and you ride the same
   cabin down a clear glass tube to the seafloor — slowly through the surface,
   the water climbing the glass and over your head (F3 shows `underwater=True`),
   then fast down the flooded tube. Walk out through the tube's gate, look
   around, walk back in and press **E** on the car's panel to come up; the
   water drains as you pass the surface and you step out dry on the deck. Whoever
   stands in the cabin rides along; friends left below get the empty car sent
   back to them. The ship will not sail while anyone is down there.
   A held slot item stays assigned to its slot; new grabs fill free slots. When
   the target has no slot (four full slots, or a two-handed heavy ball), grabbing
   it automatically puts away the equipped slot item and holds the new item until
   dropped or thrown. Everything you carry weighs something: the grey bar under
   the slots fills and you walk and sprint slower; when it is full (25 kg) it
   turns red and you crawl until you drop something. Purple and black balls need
   both hands, sit in the middle of the view and only lob a short way; while you
   hold one, E on a smaller item stores it straight into a free slot. Blue balls
   are heavy but fit a slot. Escape opens the session menu (Resume recaptures the cursor); F3
   shows the network debug overlay with kg and grip per item.
   *Sunk Cost > Prototype > Apply loot setup* rebuilds the six-item fixture and
   the shared `WeightSettings` asset; tune weight there, not in code.

**Scenes and sailing.** The world is three scenes loaded next to `Session`
per player: `HQPrototype` (the base, its dock and the docked ship), `ShipAtSea`
(the same ship on open water — a grey stub until the real ship lands) and
`DiveSite01` (the elevator and the seafloor; not entered yet). Everyone aboard
the ship — inside the rails, on the deck — sails together; a sail is refused,
naming who is still ashore, otherwise. What you hold or stowed and anything
lying on the deck sails with you and is at the same spot on the new deck; the
base resets when the crew comes back. The monitor that chooses the destination
is the screen at the bow: look at the **Site 01** or **HQ** button and press
**E**. Everyone must be on the deck itself — the gangway does not count, and a
ball left on it stops the ship ("Clear the gangway"). Then everyone is held
where they stand (look around, no walking or items), the gangway lifts, the
ship pulls away for about four seconds, the screen fades, and you fade back in
on the stopped ship; at HQ the gangway comes down before you can move. The
screen says where you are ("Docked at HQ…", "At Site 01…"), and a
refusal ("Not aboard: Player 2") stays on it for three seconds on every
player's ship. The editor menu *Sunk Cost > Prototype > Debug > Sail to Sea /
Sail to HQ* (Play Mode, hosting) does the same without walking. Joining is
possible at the base and on the ship between days; a join while a dive is in
progress is refused ("Dive in progress — join between days"). F3 shows every
player's and item's scene. Menu: *Sunk Cost > Prototype > Create or Update
Session / HQ / ship stub* rebuild the generated scenes and the ship prefab,
*Validate Session / HQ / ShipAtSea* check them, *Check world loop (pure)* runs
the editor checks, and *Run world loop matrix (Local host, Play Mode)* runs the
Local host + headless guest rows from a hosting editor (needs the Local build
below); its log is `Temp/world-loop-matrix.log`.

**Builds.** Two menu items write to ignored folders and put a `sunkcost-build.json`
manifest beside the executable:

- **Sunk Cost > Prototype > Build Windows Development** — the shareable Steam
  build. It refuses to run on a dirty checkout, so every tester's copy carries
  the same Git revision. Output: `Builds/HQPrototype/`.
- **Sunk Cost > Prototype > Build Windows Local Development** — for two-process
  testing of uncommitted work. Output: `Builds/HQPrototypeLocal/`; Steam mode
  refuses this build.

**Steam.** Every player runs the *same* shared build with Steam open and selects
**Steam P2P** first (that is what registers the invite listener). The host clicks
**Host**: a friends-only Steam lobby is created and its **lobby ID** is shown with
Copy and Invite buttons. Guests either accept the Steam overlay invite or paste
the lobby ID and click **Join**. The lobby is friends-only, so joining by ID only
works for people on the host's Steam friends list (invites are friends anyway).
Joining checks the lobby's game marker, protocol
and build revision, then the server checks Steam lobby membership before a player
spawns; a fifth player, a different build, or a closed room gets a message. The
first session locks the transport (Local or Steam) until the game restarts.
Under the shared test App ID `480`, start the build before accepting an invite;
Steam cannot launch it for you.

---

## Week one, in parallel

### Dev A — networking, alone, nothing else
The prototype now pins **FishNet** and **Steamworks.NET**, and carries the unchanged
FishySteamworks source under `Assets/ThirdParty/`. The networking goal remains:

> Two separate builds, on two machines, connected over Steam, walking around a grey box.

Do not add gameplay to this. Do not let anyone else add gameplay to this. If this
takes the full two weeks, that is fine and expected — everything else is blocked on
it working, and nothing else matters until it does.

### Dev B — the noise system and a grey level
`Assets/_Project/Scripts/Noise/` is already written and working. Wire it up:
- A grey-box site: a floor, some walls, a wreck to walk through.
- A cube that subscribes via `INoiseListener` (see `Examples/BellEaterEars.cs`) and
  moves toward whatever it heard. That cube is your first monster and it is enough.
- Turn on `NoiseSystem.DebugDraw` and make noise visible in the scene view.

### Dev C — the player, locally
Single-player, no networking, none of Dev A's code:
- First-person controller with a visible body. Walk, sprint, crouch, mantle.
- Pick up, carry, drop. Weight slows you.
- An oxygen countdown that drains faster when sprinting or carrying.
- The handheld device: raise it, see your air, lower it.

### Week two
Merge C's player into A's networking. That merge is the moment the project becomes
real, and it will be uglier than you expect. Budget for it.

---

## What "done" looks like for the prototype

Four people in a grey room, one elevator, three objects, one cube that hunts noise.
If you hear these without prompting, you have a game:

> "HELP ME CARRY THIS."
> "WHAT IS THAT?"
> "SEND IT UP!"
> "NO NO NO NO NO—"

If you don't hear them, the answer is not more content.

---

## Voice chat — build it, don't buy it

Proximity voice is not polish, it is the product. But you are already on Steam P2P,
so Steam gives you the hard part for free:

- `SteamUser.StartVoiceRecording()` / `StopVoiceRecording()` on the push-to-talk key
  (which is diegetic here — the radio button on the suit).
- `GetAvailableVoice()` + `GetVoice()` hand you compressed bytes each frame.
- Ship those over the connection you already have — unreliable channel, dropped
  packets are fine and ordering does not matter.
- `DecompressVoice()` on the receiving side, feed the PCM into an `AudioSource`
  parented to the speaking player. Unity's 3D falloff is your proximity.
- Radio degradation: an `AudioLowPassFilter` plus a static loop, both driven by
  distance. You would be writing this part yourself with any asset anyway.

Roughly 300 lines, two or three days.

Paid assets (Dissonance and similar) buy echo cancellation, voice activity detection
and jitter buffering. Two of those three don't apply: your players will be in
headphones, and push-to-talk removes the need for voice activation. That leaves
jitter buffering, which is solvable. Revisit only if voice becomes a real time sink.

## Spending money

Don't, for a while. Start on free kits (Kenney's are free; Synty is on deep sale
constantly) and only buy an environment pack once the art direction is locked — or
you will buy twice.

The one thing genuinely worth paying for eventually is **rigged characters and
creatures**, around month four. They need clean topology and readable silhouettes,
and it is where AI-generated meshes cost more time than they save.

Loot objects are the opposite case — static, physics-driven, seen in near-darkness,
and you want a hundred varied ones. Generate those.

---

## Full design

The living gameplay design is [docs/DESIGN.md](docs/DESIGN.md). Keep current
decisions and open questions there. The [original production plan](docs/reference/Sunk-Cost-Build.pdf)
is the unchanged Draft 3 snapshot from 12 September 2026, retained for its roadmap
and production context. Where rules differ, DESIGN.md takes precedence.
