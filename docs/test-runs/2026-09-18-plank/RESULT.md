# The plank — 18 September 2026 — `plank` MATRIX_PASS; `fullrun`, `cabin`, `loop` MATRIX_PASS

Dan's decisions (18 September 2026): after everything is sold and the three
days are used, short at payday means the run is over; the crew walks the
plank one by one; each gets 10 seconds on the board and is pushed; the card
says the game is over with the days and the minutes; then everything from
nothing.

## `plank` (new, ~3 min)
P0 a lost payday: the plank phase, the host first on the board, the board
says THE RUN IS OVER, sailing and the shop refused · P1 placed at the base
(0.0 m), the prompt "WALK THE PLANK — jump when you are ready · 10 s", free
to walk out on the board, pushed after 10 s into the water, counted · P2
the card (0 days, 0 minutes — no dive this run), black screen, the fresh
run: day 0, $0, no cycle, upgrades gone, the ball dropped, back on a spawn
point, screen clear, alive with a full tank · G1 a guest: the host jumps by
itself, the guest is next, placed at the base, pushed, the fresh run on
both, the guest alive with no upgrades, both on land.

## Regression
- `cabin` Z5 (the day state's own pay API): short at payday is now the plank
  phase with the balance kept until the reset; `ServerResetRun` clears it.
- `fullrun`: the lost branch waits for the plank and the reset (this run took
  the PAID branch); `loop`: the regenerated HQ scene with the plank.

Host: the editor, guest builds, Local transport. Logs alongside.
