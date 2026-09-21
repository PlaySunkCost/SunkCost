# The dash and a landed throw's noise — 21 September 2026

Branch `dan/dash`, editor matrices (host = the editor, guest = a local build of
the same revision, Tugboat on 127.0.0.1). Not a Steam run.

| Job | Rows | Result | Log |
|---|---|---|---|
| `dash` (`DashRuntimeChecks`, host + guest below) | 90 | **MATRIX_PASS** | `dash-matrix.log` |
| `noise` (regression: footsteps, the car) | see below | | `noise-matrix.log` |
| `cabin` (regression: thrown items in the moving car) | see below | | `deck-cabin-matrix.log` |

## What the dash rows say

- **D0** — on the deck Alt is refused (`Only below`), the diver does not move.
- **D1** — on the seabed one Alt covers **4.1 m** forward; the server judged one
  dash from the copy's speed; the cue's serial is 1; the tank lost **4.6 s**
  (the dash's 4 plus half a second of breathing); one `Dash` noise at **20 m**
  from the host's id; two rings and a whoosh on the host; the visor's DASH bar
  at 0.16 right after; **the guest's copy of the host played the two rings from
  the cue** and reads the bar running down (a spectator's and the TV's visor
  read the same state).
- **D2** — a second Alt within the cooldown: `Dash in 2.1 s`, no move; after the
  cooldown the next dash goes (4.0 m).
- **D3** — A held with Alt: 5.0 m to the left (dot right −1.00); the cue carries
  the direction.
- **D4** — Space then Alt in the air: **4.1 m** (the air dash is in for Dan to try).
- **D5** — crouched: `Not crouched`; a heavy ball in both hands: `Not with both
  hands full`.
- **D6** — the guest's dash: the host's copy moved 3.9 m, the server judged it,
  the guest's tank lost 4.8 s, one `Dash` noise from the guest's id, **the host
  sees the guest's two rings**; the guest played its own at the press and reads
  its own cue.
- **D7** — a Listener 14 m to the side heard the dash (`last=Dash`) and turned to
  shoot at where it was. It also heard two sprinting steps before the server's
  judgement fired (the first ~2 m of a burst read as a sprint): harmless, same
  place, the dash's 20 m dominates.
- **D8** — the host's thrown coin: one `Impact` at **6.7 m** (10 m scaled by a
  6 m/s fall) where the coin lies, one landing on the server's count, the
  Listener heard it (`last=Impact`); the guest's throw — simulated by the guest,
  never by the server — landed loud on the server too, near where it lies.

## Process notes

- The first two runs failed row D0 on a test bug (the visor is dark on the deck,
  so its readout is zero there). The fix compiled, but the editor's live domain
  kept the old editor assembly through a `refresh` (no domain reload on Play
  Mode in this project); `EditorUtility.RequestScriptReload()` and a rerun
  passed. Lesson for the playbook: after editing an Editor script while a
  failed matrix left the editor in Play Mode, stop, then force a script reload.
