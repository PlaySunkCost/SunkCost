# HQ experience polish — 27 September 2026

Branch `dan/hq-polish` (from `main` 00bd9bb). Unity **6000.6.0f1**. Editor host, separate Windows guest
build from the same tree (`test-build.json`: revision e514c17, protocol 3). Local Tugboat over loopback,
no injected latency; not a two-computer Steam test. No audio was opened, reviewed or changed.

## What changed (presentation only; no gameplay, networking or economy rules)

- **Sky:** the dawn glow moved to the south-east so the arrivals (who face south) see it; horizon band,
  glow width/height, stars, sun and fog colour tuned (`HQPlatformBuilder.Sky`, `DawnSky.mat`).
- **Shop prompt:** the HUD draws the aim prompt for shop displays and the counter
  ("Large tank · $300 — Press E to buy · crew pot $0"); the aiming dot goes gold on a usable stand, the
  counter, the colour panel and the quota board (`PlayerHudUI`). Other prompts keep the 19 September rule.
- **Browser:** `ShopBrowserUI` restyled in place (IMGUI, `ScreenStyle` palette): pot chip, tabs, cards with a
  description line (`ShopItem.Description`, or a line composed from the catalogue's numbers), price,
  covered/short-by/fitted line, BUY / ghost BUY / OWNED, styled scrollbar.
- **Signs:** every platform plate reads an `HQSigns` key (`HQGeneratedLayout.Label(key)`): fascia
  `BLACK TIDE SALVAGE CO. / EQUIPMENT DEPOT`; counter `EQUIPMENT CATALOGUE / BROWSE & BUY · PRESS E` on a
  taller mast with a floor frame and lamp; east end wall `EQUIPMENT DEPOT / SHOP · UPGRADES · SUPPLIES`;
  PICKUP raised over the chute's lip with `BOUGHT GEAR LANDS HERE`; `INTAKE / SELL` over
  `PAY THE QUOTA HERE`; arrival board `EQUIPMENT DEPOT > / SHIP > / SELL / QUOTA >`.
- Quota board text fitted to its plate; stock on the depot's upper shelves.
- Test row: the world loop's S3 waits for the dropped deck ball to rest before D7 reads its spot
  (`world-loop-matrix-attempt1-fail.txt` shows the pre-existing failure: the ball rides, but rolls 0.24 m
  after landing with its bouncier material from PR #86).

## Results

| Check | Result | Evidence |
|---|---|---|
| `HQGeneratedSetup.Validate()` after each of three rebuilds | PASS | editor log |
| `matrix hq` (host) | **MATRIX_PASS**, 29 rows | `hq-art-matrix.txt` |
| `matrix shop` (host + guest) | **MATRIX_PASS**, 92 rows | `shop-matrix.txt` |
| `matrix plank` (host + guest) | **MATRIX_PASS**, 60 rows | `plank-matrix.txt` |
| `matrix loop` (host + guest + late joiner) | **MATRIX_PASS**, 67 rows (attempt 3; attempts 1–2 failed D7 as above) | `world-loop-matrix.txt` |
| Host purchase through the restyled browser (pot 500 → 350, OWNED) | seen | `after-shop-ui.png` |
| Stand aim prompt drawn | seen | `after-stand-prompt.png` |
| Quota board text inside its plate | seen | `before-/after-quota-board.png` |
| First view from the spawn | seen | `before-/after-spawn.png` |

Before/after captures: `before-depot.png` / `after-depot.png`, `after-depot-end.png`.

## Limits

Hover states of the browser were not exercised (no mouse in the harness). The Ship prefab regenerated
by each rebuild was reverted (identical name set, ids only). No human approval is claimed.
