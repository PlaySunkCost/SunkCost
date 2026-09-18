# Code check, the day flow — 18 September 2026 — `spectate` MATRIX_PASS (215 rows)

Three findings of the 18 September code check (netcode review of the ride and
death flows), each confirmed in the source before the fix:

1. **End day while the site is still closing** revived a dead player into the
   dive scene at deck coordinates: `ServerEndDay` now refuses "Bringing up the
   dead" while `siteClosing` or while any dead player's object still stands in
   the site. Row **D1**: refused right after the last diver died, accepted once
   the site closed.
2. **A player dead inside the car** was swept into the ride up and kicked
   "never arrived": the ride up lists the living only (as the ride down did).
   Row **S3**: dead A stands in the car with B; B rides, A is no rider, A is
   still connected afterwards and is carried to the ship with the dead.
3. **A ride down whose riders all drop** would have left the day in progress
   for nobody: `EndRide` now re-runs `ServerSiteMayClose`, and a ride down that
   nobody finished is cancelled instead of beginning a day. No row: a ride down
   needs every living player in the cabin and the host cannot drop, so it is
   unreachable in play today; the guard stays.

Host: the editor (`local-dev:0f5afb8` + this branch), two guest builds, Local
transport. Log: `spectate-matrix.log`.
