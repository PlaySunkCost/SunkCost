# Spectating — dead players and the ship's TV

Status: **decided with Dan, 17 September 2026; technical plan ready; not built.**
Gameplay rules go into DESIGN.md and the network rules into NETWORK_CONTRACT.md
with the PRs that build them. Read [DESIGN](DESIGN.md), [NETWORK_CONTRACT](NETWORK_CONTRACT.md)
and [EDITOR_MATRIX_PLAYBOOK](EDITOR_MATRIX_PLAYBOOK.md) before building.

## 1. What Dan decided

**Two kinds of spectating.**

*A dead player* watches living players — from **their eyes, exactly their
screen**, visor and all, plus a `SPECTATING <name>` label and a small hint at
the bottom of the screen: `left click — next player`. **Left click** cycles the
living, **below or above water**. They hear what the target hears (every voice
the target can hear, at the target's distances) **plus every dead player
anywhere**; the dead talk only to the dead — the living never hear them. On
death you keep your own camera for about a second (you see yourself fall), then
a short fade, then the **nearest living player**. Spectating lasts **until End
day**: the dead are revived on the ship with the base kit; a body that was
brought up to the ship disappears and its owner stands next to where it lay. (A
crew can never sail home with a dead player: sailing is only possible at the
start of a day and nobody can die before going under.)

*The TV* — a separate **large screen on the deck** — broadcasts **one diver at a
time**: their screen (visor included), their voice, the voices they hear and
every game sound flagged for broadcast, all played **from the TV's position** so
whoever stands near it hears it. **E on the TV = next channel.** Only players
who are **underwater** are channels; nobody below → **NO SIGNAL**. Anyone near
the TV watches; E is only for the channel.

*The diver knows.* A living diver who is being watched — by the TV or by dead
spectators — sees a red **ON AIR · N watching** mark in the visor.

**Death, minimum for this card.** Nothing can kill yet. A debug key **K kills you,
underwater only** (the only place death can happen), development builds and the
editor only. The body drops where you stand; the four slots scatter (design
§4); the server marks you dead. Air and the monster will call the same death
later.

## 2. Contract changes (Idan reads, someone other than Dan merges)

- **Physical world vs loaded worlds.** A client loads the world its body is in
  *and, while it spectates or the TV shows a diver, the other world too*. The
  server adds the connection to the other world's scene as an observer
  (`WorldSceneFlow.ServerWatch`) and removes it when the reason ends; the
  player object never moves for watching. Eligibility for voice, items and the
  cabin keeps using the **physical** world (the player object's scene) — a
  loaded scene grants nothing by itself.
- **Death** on `CrewDayState`: `Dead` (client ids). `ServerPlayerDied(id)`
  removes the player from `Below`, so *living* finally means *alive and
  connected*: a dead player never blocks End day, the car's return or the
  site's unload. The dead's player objects are moved to the ship when the site
  unloads (the last living diver up) and revived in place at `ServerEndDay`.
- **Spectate state** on `CrewDayState`, server-written: `SpectateTarget` per
  dead player (who they watch) and the TV's `Channel` (a living diver below, or
  none). Watcher counts are derived on every peer from those two.
- **Voice** (replaces "a spectating camera must not open access to divers'
  voice"): routing is by *state*, never by camera position. The server relays a
  living speaker's frame to (a) the living listeners in its proximity set as
  today, (b) every dead listener whose `SpectateTarget` is the speaker or is in
  that proximity set, and (c) every ship-side client while the TV's `Channel` is
  the speaker or in its proximity set. A dead speaker is relayed only to dead
  listeners. Each frame carries a one-byte **route** (`Direct`, `Spectate`,
  `TV`) so the receiver mixes it at the right place: `Direct` at the listener's
  head, `Spectate` at the target's head, `TV` at the TV. Protocol **3**.
- **Player look pitch**: a server-written `SyncVar<sbyte>` on
  `HQPlayerController`, fed by the owner's `[ServerRpc]` at most 10×/s — the
  spectator camera needs the target's pitch; yaw is already on the transform.

## 3. Classes and data

### Death (card 1)
| Where | What |
|---|---|
| `CrewDayState` | `SyncList<int> dead`; `IsDead(id)`; `ServerPlayerDied(id)` (adds, removes from `Below`, clears riders/placements for it); `ServerRevive(id)`; `Living` = authenticated and not dead — used by `ServerRequestDive`'s all-aboard, the car's return and `ServerEndDayIfDone`. |
| `HQPlayerController` | `SyncVar<bool> dead` (server-written): every peer hides the body visual, disables the capsule and stops input; the owner keeps its camera ~1 s (`deathCameraSeconds`, serialized) then `SpectatorView` takes over. K → `RequestDebugDeath()` → `[ServerRpc] ServerRequestDebugDeath` (dev builds/editor only, refused unless the player is below and alive). |
| `PlayerBody` (new, `Scripts/Player`) | A `CarryableItem` variant: `CarryGrip.TwoHands`, mass 60 kg, value 0, `OwnerClientId` (SyncVar, server-written), display name `<name>'s body`, no slots. Spawned by the server where the player died; rides the car as cargo like any item (existing cabin-cargo code); unloads with the site if left below (design: lost). Prefab `Prefabs/Player/PlayerBody.prefab` built by `HQPrototypeLootSetup`. |
| `WorldSceneFlow.Death` (new partial) | `ServerKill(conn)`: `inventory.ServerDropEverything()`, spawn `PlayerBody`, `dayState.ServerPlayerDied`, `player.ServerSetDead(true)`. `ServerMoveDeadToShip()` called from the surface routine when the site is about to unload: the dead players' objects go into the move list to the ship, hidden. `ServerReviveAll()` at End day: for each dead, position next to their `PlayerBody` if one is on the ship (then despawn the body) else a free deck spawn point; `dayState.ServerRevive`, `ServerSetDead(false)`, base kit (slots empty). |
| Design/contract text | DESIGN §4 "Built: K, the body, revival at End day"; contract §3 rows above. |

### Dead spectating (card 2)
| Where | What |
|---|---|
| `CrewDayState` | `SyncList<SpectateEntry { Dead, Target }>`; `ServerSetSpectateTarget(dead, target)`; `WatchersOf(id)` (count of entries targeting `id`, plus 1 if `TvChannel == id`). |
| `WorldSceneFlow.Watch` (new partial) | `ServerWatch(conn, world)` / `ServerUnwatch(conn, world)`: `LoadConnectionScenes(conn, WatchDataFor(world))` — like `LoadDataFor` but **no `PreferredActiveScene`** and no moved objects — and `UnloadConnectionScenes(conn, UnloadDataFor(world, keepOnServer: true))`. The server decides from state: a dead player whose target is in the other world gets that world; a ship client gets the dive world while `TvChannel != none`; both are dropped when the reason ends. Idempotent, tracked in a `watching` map. |
| `SpectatorView` (new, `Scripts/Player`, local only) | On the owner's player object. `Begin(target)`: parents the local camera to the target's eye anchor, applies the target's replicated pitch every frame, disables own controls, keeps the `AudioListener` on the camera (world sounds = what the target hears). Left click → `RequestNextTarget()` → `[ServerRpc]` on `ShipControls`… no: on the player (`ServerRequestSpectate(next)`), server validates *alive, not me* and writes the entry. Nearest living player chosen by the server at death. If the target dies or leaves, the server re-targets to the nearest living; none → the spectator sees their own body from above (`NO SIGNAL` label). |
| `PlayerHudUI` | `ComputeReadout(HQPlayerController who, PlayerInventory inv, Camera cam)` extracted from `Update` so the visor can be computed for any player; in spectate mode it renders the **target's** readout (air/HP placeholders, ON ME from the target's replicated slots, depth from the target's head vs sea level, brackets from the spectator camera). Draws `SPECTATING <name>` (top centre) and the bottom hint `left click — next player`. The ON AIR mark: `ON AIR · N watching` top-right when `WatchersOf(me) > 0`. |
| `HQPlayerController` | `SyncVar<sbyte> lookPitch` (server-written) + `[ServerRpc] ServerSetLookPitch(sbyte)` from the owner at ≤ 10 Hz when changed by ≥ 2°. `EyeAnchor` transform for spectators (the camera's transform on remote copies). |
| `ProximityVoice` | `VoiceFrame.Route` byte; server relay per §2 (dead listeners by target, dead speakers to dead only); client `ClientFrame` accepts `Spectate` frames when local is dead; `Mix` uses the target's head for `Spectate` frames and full gain for dead-to-dead. Protocol 3 in `PrototypeBuildIdentity`. |
| F3 / peer snapshot | `dead=`, `spectating=`, `watchers=`, `tv=`. |

### The TV (card 3)
| Where | What |
|---|---|
| `ShipParts` | `TvScreenName = "TvScreen"` (a 2 m × 1.2 m quad on the deck wall facing the cabin, with a collider for E), `TvSpeakerName` (AudioSource position). Required children; `ShipStubBuilder` builds them; validator row. |
| `ShipTV` (new, `Scripts/World`) | On the ship root. Owns a `RenderTexture` (960×540) on the screen's material, a child `Camera` rendering only into it, enabled only while `TvChannel` names a diver whose object this client has; each frame it sits at the channel diver's eye anchor with their pitch. Draws the diver's visor into the texture (a second `PlayerHudUI` pass targeting the RT via a `CommandBuffer`/IMGUI-to-RT helper — v1: the world view + a `NO SIGNAL` / `LIVE · <name>` caption; the diver's full visor overlay is v2 if IMGUI-to-texture proves ugly). |
| `CrewDayState` | `SyncVar<int> tvChannel` (−1 none); `ServerTvNext()`: next living player in `Below` after the current, or −1; the server also advances/clears when the channel diver surfaces, dies or leaves. |
| `ShipControls` | `RequestTvNext()` → `[ServerRpc]` (presser aboard) → `ServerTvNext`. `HQPlayerController` targets `ShipTV`'s collider for E (`CurrentTv`), prompt `Press E — next channel`. |
| `ProximityVoice` | ship clients receive `TV` route frames for the channel's set; a `VoicePlayback` per speaker routed to the TV's `AudioSource` (3D, at `TvSpeaker`), gain by the channel diver's distance to the speaker (the ship client has the site loaded while a channel exists). |
| `BroadcastSound` (new, `Scripts/Audio`) | A tiny component on game `AudioSource`s we want on the TV: when it plays near the channel diver, the ship client re-emits the same clip at the TV at the diver's loudness. v1 flags: the car's winch, the deck cabin doors. |
| `WorldSceneFlow.Watch` | ship-side clients get the dive world while `tvChannel != -1` (and the dead the same rule as card 2). |

## 4. Protocols (server, in order)

**Death (K below).** Refuse unless dev build, alive, `IsBelow`. Drop everything
(scatter), spawn `PlayerBody` at the feet (rotation flat), `ServerPlayerDied`
(Below −, Dead +, riders −), `dead = true`. If the car is at the bottom with
nobody living below, the existing "car goes back / site unload" rules now fire
because Below is empty. The owner: 1 s own camera, fade 0.3 s, then the server
has already chosen the nearest living target (`ServerSetSpectateTarget`), and
the client's `SpectatorView` begins; if the target is in the other world the
server has already called `ServerWatch`.

**Site unload with dead inside.** In the surface routine, when `!othersBelow`
(no *living* below): move every dead player object into the ship's move list
(`ServerMoveDeadToShip`) before `UnloadConnectionScenes(Dive)`; they arrive
hidden at a spawn point (the dead never see it — they are watching someone).
Their `PlayerBody` left below unloads with the site (design: lost). A body
that rode up in the car is now ship cargo.

**End day.** `ServerEndDay` → `ServerReviveAll()`: next to the body on the ship
if any (despawn it), else a free spawn point; `dead = false`, `ServerRevive`,
`SpectatorView.End()` on the owner, `ServerUnwatch` for everything they watched.
Then the day advances as today.

**TV channel.** `ServerTvNext` picks the next living id in `Below` after the
current (wrapping) or −1. Whenever `Below`/`Dead` change (surface, death,
disconnect) the server re-validates the channel. On `tvChannel` becoming ≠ −1
the server `ServerWatch(ship clients, Dive)`; on −1 it unwatches once the
last ship client's TV shows nothing.

**Voice routing** per §2, computed on the server per frame from `Dead`,
`SpectateTarget`, `tvChannel` and the physical proximity set.

## 5. Cards, in order (each a PR touching the contract)

1. **Death-minimum** — K below, `PlayerBody`, scatter, `Dead` on the day state,
   living checks, dead moved to the ship at site unload, revival at End day
   (next to a brought-up body). Pitch SyncVar rides here (small, needed next).
2. **Dead spectating** — watch state and dual world, `SpectatorView`,
   left-click cycling, target's visor, labels and hint, the nearest-player
   default and the one-second fall, voice routes for the dead, ON AIR count.
3. **The TV** — the screen and speaker parts, channels, NO SIGNAL, render
   texture camera, E next, TV voice route, `BroadcastSound`, ship clients
   watching the dive world.

About 4–6 days. Protocol bump (3) lands with card 2.

## 6. Acceptance rows (new matrix job `spectate`: editor host + one guest build)

- K on deck: refused, nothing happens. K below: body where you stood
  (two-handed, `<name>'s body`), four slots scattered, `Dead` holds you, you
  are out of `Below`; the car and End day no longer wait for you; F3 `dead=`.
- Death view: ~1 s of your own camera, fade, then the nearest living player's
  eyes with their visor; `SPECTATING <name>`; the bottom hint; left click moves
  to the next living player below or above water; the target's pitch follows.
- Dual world: the dead diver watching a deck player has both scenes loaded;
  when they are revived the extra scene is unloaded.
- Dead voice: the dead hear the target's proximity set and every dead player;
  a living player never receives a dead player's frames; a `Spectate` frame
  mixes at the target's head.
- Site unload with a dead player inside: the dead player's object is on the
  ship, hidden; the seafloor body is gone.
- End day: the dead stand on the deck, alive, empty slots; a body brought up
  is gone and its owner stands where it lay.
- TV: first diver below on screen, `LIVE · <name>`; E cycles; a diver who
  surfaces or dies leaves the channels; nobody below → `NO SIGNAL`; the channel
  diver's voice plays at the TV; a ship client holds the dive world while a
  channel exists and drops it after.
- ON AIR: the channel diver sees `ON AIR · 1 watching`; with a dead spectator on
  them, `2 watching`.
- Guest build: the guest dies (peer command `die`), spectates the host,
  reads `spectating=<host>`; the host's visor shows `ON AIR · 1 watching`.

## 7. Open (not decided yet, not blocking)

- Which sounds get `BroadcastSound` first beyond the winch and the cabin doors
  (footsteps, breathing and the monster do not exist yet).
- IMGUI visor drawn into the TV texture (v2 if v1's caption-only view is not
  enough).
- Whether a spectator may look at a dead body (not a channel for the TV;
  for the dead, only when no living player is left).
