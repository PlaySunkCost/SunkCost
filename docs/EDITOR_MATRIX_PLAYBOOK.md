# Editor matrix playbook — how to write and run a Play Mode check so it passes the first time

What the cabin, world-loop, hands and full-run matrices taught on 16 September
2026 (the full run took six attempts; five were the script's own mistakes).
Read this before writing a new `*RuntimeChecks.cs` row or job.

## Running
- Jobs: `CameraClearanceMatrixDriver.Start("camera" | "cabin" | "hands" | "loop" | "fullrun" | "spectate" | "smooth")`
  (`spectate` = death and dead spectating, `SpectateRuntimeChecks`, log `Temp/spectate-matrix.log`,
  guest dirs `Temp/spectate-guest` and `Temp/spectate-guest-b` — the S rows run two guest builds;
  `Send`/`GuestEventually` take the directory as their last argument;
  `save` = the three save slots, `SaveRuntimeChecks`, log `Temp/save-matrix.log`: the slots live in
  `Temp/save-matrix-saves` for the run (`SaveSlots.DirectoryOverride`), the rows leave and re-host with
  `PrototypeSessionUI.StartLocalHost(slot)` — the driver's own host has no slot, so a stale save never
  leaks into any other job; one guest in `Temp/save-guest` (an upgrade back by name) — about 4 minutes;
  `air` = the tank and health, `AirRuntimeChecks`, log `Temp/air-matrix.log`, the host alone
  (drain at 1× and 1.5× sprinting on the virtual keyboard, L, suffocation to death, revive)
  then one guest in `Temp/air-guest` (replication, the ride up on an empty tank) — about 5 minutes;
  `cabin` rows **R1b** (the deck doors turn around for a leaver, "Nobody aboard"), **R1c** (a body in the doorway holds
  the deck doors open, the doorway passable, until it moves) and **R3b** (a body in the car's doorway holds its doors,
  then a crossing: seal again, climb without the leaver) cover the door rules of 18 September 2026; **Y3/Y8** read the
  day card (`DAY 2 OF 3`, `PAYDAY`); **U3/U4** unstuck on the deck and below (`plank` **U1/U2** at HQ and refused to the
  jumper); `plank` **P0–P2** also the gate and the world's items reset; `spectate` **D1/D2** the arms hidden with the body,
  **S3/T** the diver's head shown again after the TV's render;
  `plank` also **C1** (a ball through the west hoop counts once, the backboard shows it; P2 the count resets); the HQ is the
  platform since 18 September 2026 — **Create or Update HQ** regenerates the textures, the materials, the prop prefabs
  that do not exist yet and the scene (`Sunk Cost/Look/Rebuild look props` rewrites every prop from code and loses
  hand edits to them; `Regenerate textures and materials` alone for a palette change); rows that stood in the old hall
  by coordinates (camera clearance, hands, inventory) are matrix debt until they are re-placed;
  `plank` = the plank at HQ, `PlankRuntimeChecks`, log `Temp/plank-matrix.log`: a lost payday, the board and
  the prompt, sailing and the shop refused, the host placed and pushed, the card, the fresh run; then a guest in
  `Temp/plank-guest` (the host jumps, the guest is next and pushed, both start over) — about 3 minutes;
  `shop` = the shop at HQ, `ShopRuntimeChecks`, log `Temp/shop-matrix.log`: the room and stands, the
  prompt, refusals (no money, owned, too far), an air tank at the delivery spot, both upgrades, the
  large tank counting below, the visor marks, the upgrades lost with an unrescued body; then a guest in
  `Temp/shop-guest` (buys from the pot, replication, a body brought up keeps them) — about 6 minutes;
  `figure` = the first-person body, `FigureRuntimeChecks`, log `Temp/figure-matrix.log`: the head cut, what the
  owner sees, the crouch, screenshots `Temp/figure-own-body.png` / `figure-own-crouch.png`, then a guest in
  `Temp/figure-guest` (`figure-friend.png`) — about 2 minutes;
  `noise` = footsteps, the elevator's noise and sounds, `NoiseRuntimeChecks`, log `Temp/noise-matrix.log`,
  the host alone with a listener on the NoiseSystem (walk/sprint/crouch on the virtual keyboard, two rides)
  then one guest in `Temp/noise-guest` on the deck (the winch low, the bell) — about 4 minutes;
  `smooth` = how smoothly a remote player's head arrives, `RemoteSmoothnessRuntimeChecks`,
  log `Temp/smooth-matrix.log`, one guest in `Temp/smooth-guest` turning under the peer's
  `turn` command, then again with its outgoing packets through FishNet's latency simulator
  via the peer's `netsim` command — about a minute)
  from an editor command; it enters Play Mode, hosts, waits for the local player
  and hands over to the checks. Logs: `Temp/deck-cabin-matrix.log`,
  `Temp/world-loop-matrix.log`, `Temp/movement-hands-matrix.log`, `Temp/full-run.log`.
  The last line is `MATRIX_PASS` or `FAIL: <row>`; on a FAIL the editor **stays
  in Play Mode** with the failing state live — inspect it before stopping.
- **When the Unity MCP bridge cannot run commands** (every `RunCommand` answers
  "No logs available", 17 September 2026, through two editor restarts): write one
  line to `Temp/editor-command.txt` — `build-guest`, `matrix <job>`, `stop`,
  `refresh` or `run <Namespace.Type.Method>` (a public static parameterless method,
  e.g. `run SunkCost.Editor.Prototype.PlayerVitalsSetup.Apply`; its return value
  comes back in the reply) — and read `Temp/editor-command.reply.txt` (`EditorCommandFile`, polled
  twice a second on the editor's main thread). `refresh` after editing scripts
  (the editor only recompiles on focus), then wait for the assemblies.
- **Scene traffic is logged**: `[Scenes] server loads <scenes> for [ids] moving <id:name…> — <why>`
  on the host, `[Scenes] client loads/unloads …` in each guest's `player.log`. A moved
  object a guest cannot resolve prints as `?`, right after FishNet's "expected to exist
  but does not" — that pair found the watch/move bug of 17 September 2026 in one run.
  Grep the guest logs for `expected to exist|already found` after any run that moves players.
- **Stop with `CameraClearanceMatrixDriver.StopCleanly()`** (Leave first, then exit
  Play Mode). Since 18 September 2026 a bare Stop is safe too: `PlayModeSessionGuard`
  leaves the session as Play Mode exits, so the editor releases its UDP port; and a
  Local host whose port is taken hosts on the next free one and says so
  ("Hosting locally/LAN on UDP port 7771").
- Never start a job or a builder while someone is playing; never edit scripts
  during their Play Mode (Unity recompiles into the running session).
- Wait for `Library/ScriptAssemblies/Assembly-CSharp*.dll` to be newer than the
  edited source before starting; check `Unity_GetConsoleLogs` for compile errors.
- **The guest build must be from this tree.** `Builds/HQPrototypeLocal` carries
  `sunkcost-build.json`; its `revision` (`local-dev:<HEAD>`) and `protocol` must
  match the editor's, or the guest is refused at admission with no log line.
  Rebuild with `HQPrototypeBuild.BuildWindowsLocalDevelopment()` after any
  commit or checkout (HEAD changes even when the code does not).
- Runs take 8–12 minutes; a 1 s editor stall (browser, download) can fail a
  ride-timing row — rerun rather than loosen the tolerance.
- Batch edits before a run; every run is ten minutes.

## Rebuilding scenes and prefabs
- `HQPrototypeBuilder.CreateOrUpdate()` **regenerates `Ship.prefab` through the
  stub builder and drops the deck cabin's car glass.** Always run
  `DeckCabinRideSetup.Apply()` *after* it (and after `ShipStubBuilder.EnsurePrefab()`).
  If the regenerated prefab only reshuffled ids (`git diff --stat` ±1400 with the
  same `m_Name:` set), revert it before committing.
- Revert the settings churn before every commit: `Assets/Settings/*.asset`,
  `ProjectSettings/{GraphicsSettings,Physics2DSettings,UnityConnectSettings,ProjectSettings}.asset`,
  and the material `_Color` rewrites (`ColourSwatch.mat`, `QuotaBoard.mat`).
- A TextMesh reads correctly along its **+Z**: a label on a wall the player faces
  from +Z needs `Euler(0, 180, 0)` (the HQ board read mirrored).

## Writing rows
- **Find things by name *and* scene.** Site coins are re-spawned with the same
  names every dive; the ship's storage room may hold last dive's `Coin 6` while a
  fresh `Coin 6` lies on the seafloor. `H.Item(name)` returns the first match —
  it once picked the ship's coin from inside the dive (the ship sits 3 km away in
  the *same physics world*, so the teleport-and-aim even worked). Filter on
  `gameObject.scene == WorldScenes.Scene(WorldId.Dive)`.
- **Build the label after the wait.** `Check(cond, "…: " + Text())` evaluates
  the text *before* a `WaitUntil` — the log then shows the old value and hides
  what was actually seen. Wait first, then compose the label from the current
  value (`ExpectRefusal` in `FullRunRuntimeChecks` does this).
- **Hook output formats.** `H.PromptText()` returns `prompt='…'; target=…` —
  use `Contains`, not `StartsWith`. `H.ServerSail(...)` returns `sailing to X`
  or `refused: why` — call it once and keep the string (a second call is a
  second press). `H.QuotaBoardText()` / readout texts carry `\n`; replace with ` | `.
- **Grab semantics.** The first grab lands in the hands; with hands full the next
  goes straight into a slot (`Stowed`, not `Held`). Four slots; the held item is
  one of them; a fifth grab is refused. `ON ME` counts an equipped item once.
- **Drops need time.** Coins are cylinders and roll; let dropped items settle
  ~2.5 s, re-check positions, and note (not fail) a runaway you put back.
- **Presses come from where a player would stand.** Sail/End day from the deck
  (`Not aboard` otherwise), the cabin button from inside the cabin, pay from the
  HQ room. Teleport with `H.ClientMoveLocalPlayerTo` / `host.TeleportLocal`;
  `ClientMoveLocalPlayerToItem` puts the player at **y = 0** — never use it on
  the seafloor.
- **Aiming.** Stand ~0.9 m from the item, `H.ClientLookAtItem` every frame until
  `host.CurrentTarget == item`, re-teleport every 30 frames; try more than one
  standing spot before failing, and log `HasLineOfSight` when it does.
- The F3 snapshot names player rows by **display name** with a `Swatch`; find
  the local player's row by `IsOwner && Swatch != null`, not by GameObject name.
- **Refused joiners disconnect too.** A server hook on disconnect must check the
  leaver was actually part of something (`IsBelow`) before touching game state.
- Screenshots: `H.CaptureScreen` needs the HUD, and the hooks open the session
  menu — `SessionInputGate.Resume()`, two frames, capture, two frames,
  `OpenMenu()`. `H.CaptureFrom(pos, lookAt, path)` for a placed camera.
- Expected strings must match the code's **bytes**: the code uses a literal em
  dash `—` (U+2014) and middle dot `·` (U+00B7); a `—` C# escape is the same
  character, but a Python patch that is not a raw string turns `—` into the
  character while an `old` pattern with `\\u2014` matches nothing. Keep
  `python - <<'EOF'` heredocs away from backslashes: write patch scripts to a
  file and run them.

## Recording a run
- Copy the log and `Logs/<job>/` screenshots to `docs/test-runs/<date>-<job>/`
  with a `RESULT.md` (what happened in the run's own numbers, what was found,
  what is not covered). PNGs are LFS.
- Link the run from the script it played (`docs/FULL_RUN_PLAYTHROUGH.md` → Runs).
