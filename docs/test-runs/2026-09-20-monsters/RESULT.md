# The monsters — 20 September 2026 — monsters MATRIX_PASS (166 rows); regressions below

Dan's card, decided in one sitting (design §6 "The monsters"): seven monsters,
nobody fights back, the deck is always safe, three drawn per dive. Built on one
creature skeleton (`Creature`, server brain, a server-authoritative
`NetworkTransform`, the Monster layer) with leaks and patches, the patch kit,
and F as the headlamp's switch.

Host: the editor; guest: the local build of the same revision.

| Job | Result | Log |
|---|---|---|
| monsters (new, `MonsterRuntimeChecks`) | MATRIX_PASS, 166 rows — the roster, the lamp, the Long Walker, the Weeping Angel, the Charger, a friend's patch and the once-a-day refusal, the patch kit, the hold on E, the Lure, the Listener, the Impostor, the Elevator Ghost (a return trip waited out, then walked into), End day, day 2 with an empty draw and the two killers | `monsters-matrix.log` |
| shop | MATRIX_PASS, 90 rows (the catalogue now four items; the kit's stand) | `shop-matrix.log` |
| air | MATRIX_PASS, 86 rows | `air-matrix.log` |
| spectate | MATRIX_PASS, 224 rows | `spectate-matrix.log` |
| cabin | MATRIX_PASS, 288 rows on the second attempt; the first failed E1 (the coin had settled 8 cm into the moving car's floor and hid from the dot — the known moving-floor flake, `deck-cabin-matrix-attempt1.log`) | `deck-cabin-matrix.log` |
| plank | MATRIX_PASS, 60 rows | `plank-matrix.log` |
| save | MATRIX_PASS, 57 rows | `save-matrix.log` |
| loop | MATRIX_PASS, 62 rows | `world-loop-matrix.log` |
| fullrun | MATRIX_PASS, 231 rows | `full-run.log` |
| photo | MATRIX_PASS, 8 rows | `photo.log` |

Every regression job ran with an empty draw and the Ghost's chance at 0 (the driver sets both for every job but `monsters`).

Found on the way (all fixed before the pass):

- Creatures walk from their spawn spots at once — an Angel stood at the car's
  edge within seconds — so the roster row judges the spawn spots
  (`MonsterRoster.LastSpawns`), not where the creatures are.
- A Lure walked into the host's capsule (the Monster layer ignores players) and
  its collider blocked the camera clearance, which suppresses the keys: F did
  nothing. Creatures now keep a standoff (a shooter 3 m, a hunter just inside
  its reach) and the Monster layer is out of the world masks the clearance,
  the placement and the crosshair use.
- The site's unload does not destroy a spawned object on the host (FishNet
  moves it out of the scene): an Angel outlived the site. The roster despawns
  the creatures when the dive scene is gone.
- FishNet reorders its default prefab collection after new prefabs and the
  editor syncs a fresh material's `_Color` on first use: both dirtied the tree
  under a running matrix, and a dirty tree is a `local-dev:` identity the
  guest is refused on. Both committed.
- A review pass (four lenses) added: strikes and bolts need a clear line to the
  diver (no kills through the wreck's walls), the Charger's rush is judged on
  the path its body travelled (a wall ends it), a 0.25 s grace on the Angel's
  freeze for a remote diver's lagging eyes, the Listener drifts back to its
  spawn spot after silence and the car's scream does not walk it up to the
  doorway, a joiner never replays a creature's old pose as an event, and the
  roster waits for the car to land.
