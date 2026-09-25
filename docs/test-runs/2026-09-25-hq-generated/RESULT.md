# Generated HQ verification — 25 September 2026

Branch: `codex/hq-generated-art`, based on `90e61b72b7b20e6874dcf37b4eb68c3cddbc17d8`.
Tests ran against the uncommitted implementation on this branch. The test build's
exact identity is preserved in [test-build.json](test-build.json); its `local-dev`
revision identifies the base commit, not a clean committed build of the changes.

## Environment and results

Unity 6000.6.0f1 on Windows. Editor host (client 0), separate Windows guest (client 1);
world-loop also launched a second late joiner (client 2). Local Tugboat over
127.0.0.1; no simulated latency or packet loss. RTT was not recorded. Steam
transport and four-player performance were not tested.

| Check | Result | Evidence |
|---|---|---|
| Unity compile / Windows development build | Succeeded | Build identity above; editor build log remains under `Logs/HQ-build-editor.log` |
| Authored layout | PASS | [layout-validation.txt](layout-validation.txt) |
| HQ movement and basketball | PASS | [hq-art-matrix.txt](hq-art-matrix.txt) |
| Shop host and guest | PASS | [shop-matrix.txt](shop-matrix.txt) |
| Plank host and guest | PASS | [plank-matrix.txt](plank-matrix.txt) |
| World loop, guests, departure and return | PASS | [world-loop-matrix.txt](world-loop-matrix.txt) |

The HQ movement test uses virtual W input on the real player controller, crossing
the bridge in both directions and the depot aisle. Both hoops use an actual
physics ball dropped through the generated rim and assert the score increases.
The loop explicitly refuses a passenger standing on the fixed bridge, leaves a
bridge ball behind, checks closed boarding gates on both peers during pull-away,
and checks gates reopen after the ship returns. Other rows cover held and loose
cargo, scene unload, late join, a passenger leaving during departure, and re-host.

Scanned shop, plank and both world-loop guest logs: no `Exception`,
`expected to exist`, `already found` or `Error:` matches. Unity cloud telemetry
DNS warnings were present; they did not prevent the local tests or build.

## Visual and asset checks

All 24 user-provided models were inspected and prepared. The kit is reduced from
6,554,628 source triangles to 92,590 across the 24 prepared models, with rebaked
colour and normal maps. The repeated scene, including the existing ship, has
about 2.08 million mesh triangles; this count is not a frame-rate benchmark.

The basketball hoop needed its rim made circular, its height corrected to 3.05 m,
and its orientation aligned with the existing score trigger. Existing ship props
and deck materials are reused. Collision is authored separately for doorways,
walking surfaces and shop areas; the hoop/chute use mesh collision where needed.

Final Unity captures are in `Temp/look/hq-generated-*.png`; the runtime arrival
capture is `Temp/look/hq-runtime-arrival.png`. These show the assembled Unity
scene, not an AI concept image.

After the matrices passed, the crew-colour sign support moved 20 cm behind the
sign and an arrival label's malformed dash was corrected. The scene was rebuilt,
static validation passed again, and captures were checked. No gameplay code or
travel geometry changed in that final cosmetic correction.

![Assembled HQ and ship](overview.png)

## Remaining external checks

- A teammate must review `ShipParts`, `DockBoardingGate` and the contract update
  before merge; this report is not human sign-off.
- Two computers using the same shared build must validate Steam connection and
  travel. Four-player performance and the look on target hardware need a playtest.
- The intake hopper and office are scenery; the quota console still sells ship
  storage and the four shop stands use the existing catalogue. No new economy or
  save-station interaction was introduced.
