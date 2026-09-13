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
2. Open `Assets/_Project/Scenes/Prototype/HQPrototype.unity` and press Play.
3. Choose **Local / LAN** and **Host**. A second build can choose **Join** with
   `127.0.0.1` on the same computer, or the host's LAN IP on another computer.
4. Move with WASD, sprint with Shift, look with the mouse, press E to pick up or
   drop the basketball, and left-click while holding it to throw. Escape releases
   the cursor.

Create a runnable Windows build with **Sunk Cost > Prototype > Build Windows
Development**. Unity writes it to `Builds/HQPrototype/SunkCostHQ.exe` and creates
the ignored development `steam_appid.txt` beside it. For Steam, both players run
the same build with Steam open, select **Steam P2P**, and the joining player enters
the host's displayed SteamID64. Lobby browsing and friend invites are not in this
first slice.

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
