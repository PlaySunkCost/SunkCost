# The shop at HQ — 18 September 2026 — `shop` MATRIX_PASS; `air`, `loop`, `spectate` MATRIX_PASS

Dan's decisions (18 September 2026, picked from the lists): a display room at
HQ, look and press E, no menu; anyone buys from the crew pot; three items —
air tank $40 (a real tank that falls to the floor at the delivery spot,
anyone may take it), large tank $300 (+50 % air), bright headlamp $150
(beam ×1.6); upgrades one per player, no refunds; lost when you die and the
body is not brought up, kept when it is; visor marks now, the body look with
the art pass; the greybox room off the hall's west wall.

Built so the art pass replaces it without touching the rules: `ShopCatalog`
(Resources) is the data, `ShopDisplay` a component on any stand,
`ShopDeliveryPoint` a marker; `WorldSceneFlow.ServerBuy` decides everything.

## `shop` (new, 6 min): every row
S0 the room, three stands with name and price, the delivery spot · S1 the
prompt `Air tank · $40 — Press E to buy (pot $0)`, E with no money refused
and shown · S2 with $500: the tank lies 0.16 m from the delivery spot, loose
in the HQ scene, a Full air tank, $460 left · S3 the large tank owned, $160
left, the stand says owned, a second buy refused "You already have a large
tank", nothing charged · S4 the headlamp: beam 25 → 40 m, $10 left, a tank
refused on money · S5 a request from 12 m refused "Step up to the shelf" ·
S6 below: the tank holds 450 s (plain 300), the visor reads the fraction of
it and marks `L-TANK  LAMP` · S7 dead with the body left below, End day:
revived with nothing, the plain beam, the pot untouched · G1 a guest at HQ:
refused at $100 and told why, buys the large tank at $400, the host's copy
shows it, per player · G2 the guest dies below, the host carries the body
up, End day: the guest keeps the large tank.

## Regression
- `air` — the A5 row now waits for the site to close before End day (code
  check A made End day refuse "Bringing up the dead" while it closes; the row
  predated it) and G1 reads the day's fresh tanks as full, then a used one as
  empty (a fresh site each day; the old row leaned on yesterday's emptied
  tanks surviving, which they no longer do).
- `loop` — its guest launcher now passes the host's port like the other
  matrices (a session started while the last socket lingers hosts on 7771).
- `spectate` — unchanged, passes (the revive rule with bodies).

Host: the editor, guest builds, Local transport. Logs alongside.
