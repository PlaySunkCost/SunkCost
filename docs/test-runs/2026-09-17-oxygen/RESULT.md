# Oxygen and health — 17 September 2026 — air MATRIX_PASS (55 rows), spectate MATRIX_PASS (210 rows)

Dan's pick for the next system ("I want to build something"), with his answers:
5-minute tank for the prototype, 1.5× drain while sprinting, weight costs no
air, at 0 air health goes at 8 a second (≈12 s to dead), health stays low for
the dive and is full again next dive, the tank counts as long as the suit is
on (the whole dive world, the car ride up included), the visor blinks AIR LOW
at 20 % and NO AIR at 0, the HP bar is live, and L takes 5 % off the tank to
test it.

## Built
- `PlayerVitals` (NetworkBehaviour on the player prefab, added by
  `PlayerVitalsSetup` with a `PlayerVitalsSettings` asset): server-written
  `air` (0..200 half-percents) and `health` (points). The server drains while
  the player's object is in the dive world and alive; sprinting is judged from
  the copy's own speed (client-authoritative movement, no flag from the owner);
  at 0 air health goes, at 0 health `WorldSceneFlow.ServerKill(conn, "suffocated")`
  — the ordinary death. Fills: the ride down's landing (both), the deck (tank),
  End day's revive (both). Inside the car going up health floors at 1.
- `PlayerHudUI`: live O2/HP bars, red and blinking when low, AIR LOW / NO AIR
  beside the O2 bar — through the one visor path (a spectator and the TV show
  the watched player's).
- L (`HQPlayerController`) → `[ServerRpc]` 5 % off the tank, below only,
  development builds and the editor; the peer's `air_down`; `air=`/`health=`
  in the peer snapshot.
- `EditorCommandFile` gained `run <Type.Method>` (how the prefab setup ran).

## Runs
- `air` — MATRIX_PASS ([air-matrix.log](air-matrix.log)): on deck the tank
  does not count and L does nothing; below **1.00 s/s** standing, **1.50 s/s**
  sprinting (12 m in 2 s on the virtual keyboard, the server judged it a
  sprint); two L presses took 10.5 %; empty → **8.0 health a second**, dead in
  ~12 s with a body where the host stood, the dive done, carried to the ship;
  End day revived with a full tank and health. Guest: its air replicates
  within 2 % both ways; a ride up on an empty tank: 100 → 65 in the car,
  reached the deck with **1 health**, a new tank on deck, health still 1.
- `spectate` — MATRIX_PASS, 210 rows, guest logs clean: the ride and death
  paths untouched by the vitals hooks.

Not exercised: dying of air with other divers below (the same `ServerKill`
path the K rows use); the look of the blinking bars on a real screen.
