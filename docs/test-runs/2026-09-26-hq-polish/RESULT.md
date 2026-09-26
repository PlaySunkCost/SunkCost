# HQ corrections and expandable shop — 26 September 2026

Branch: `codex/hq-generated-art`. Tests use the working changes on base
`33b8186e1eeedc28bb788bb91ce969fcb343d2a7`, Unity **6000.6.0f1**.
The test executable's exact identity is in `test-build.json`.

## Scope

- Both imported hoops face inward; their visible rims align with scoring.
- Individual shop signs removed; names/prices appear when aimed at.
- Arrival moved north-east, quota south-west, colour selector ahead of spawns.
- Crane/winch collision follows the actual geometry; open floor is traversable.
- Complete lower exterior catwalk removed. The mistaken ship tower cover is undone.
- Hinged HQ bridge tip raises before pull-away and lowers on return, with both
  boarding barriers closed during travel and the ship barrier closed at sea.
  Overlapping coplanar ship threshold removed.
- Arrival wayfinding, seating light and merchandise details reuse existing art.
- Full-catalogue counter, search/filter/scroll and server-validated purchases.
  Featured displays remain optional; no stand required for new merchandise.

## Verified

- Authored HQ validation: clear spawns, colour panel ahead of each, continuous
  bridge floor, clear player capsule path, bridge excluded from aboard checks,
  two safety barriers, one hinged leaf, valid catalogue/court/plank.
- `hq-art.log`: **MATRIX_PASS**. Real controller input walks both directions
  over the bridge, the depot aisle, the former phantom crane blocker and the
  new arrival exit. All four featured names appear/disappear on aim/look-away.
  Both hoops score with balls dropped through their now-aligned physical rims.
- Catalogue test: E opens the browser; all rows appear. A temporary **fifth row**
  with an already registered consumable prefab appears and is purchased for its
  configured price without any display/scene addition. It uses the shared chute.
  Escape and leaving range close the browser. The temporary row is removed;
  the real catalogue asset has no changes. Screenshot: `catalogue.png`.
- `world-loop.log`: **MATRIX_PASS**, editor host + separate Windows client and
  a second late joiner. Both peers see raised gangway/closed gates before motion,
  lowered gangway/open gates at HQ, and the ship opening stays closed at sea.
  Boarding refusal, bridge cargo exclusion, held/loose cargo, return, disconnect
  during travel and re-host pass.
- `shop.log`: **MATRIX_PASS**, editor host + rendered Windows non-host client.
  Host purchases, delivery, insufficient funds, duplicate upgrade, range refusal,
  upgrade effects and loss on unrecovered death pass. The guest opens the full
  browser at the catalogue counter, receives server refusals, purchases an upgrade
  from shared funds and sees it replicated. Recovery of its body preserves it.
- `plank.log`: **MATRIX_PASS**, host + separate Windows client. Plank gates,
  voluntary jump, timeout push, lower water surface, fresh run, living players
  and reset of upgrades all pass after the HQ layout/spawn changes.

## Test environment and limits

Local Tugboat transport over loopback; no injected latency or packet loss.
This is separate-process validation, not a two-computer Steam test or a
four-player performance measurement. Network authority/contract changes still
need teammate review; no human approval is claimed.

The first catalogue guest check revealed that the verification tool opened the
pause menu on every command, closing the browser; the test tool now preserves
the shop modal. A later rendered client launch suffered a Unity native crash
before the first snapshot. Logs are retained as `client-crash-attempt.log` and
`shop-crash-attempt.log`; the subsequent fresh client opened the catalogue and
completed its purchase checks. No cause/fix for that native crash is claimed.
Both the complete fresh shopping run and subsequent plank client run passed.

The local Windows build succeeded with zero build errors on the final catalogue
test build. Existing obsolete-API, mesh/ray-tracing and large-seabed-triangle
warnings remain; this work does not claim a warning-free project.
