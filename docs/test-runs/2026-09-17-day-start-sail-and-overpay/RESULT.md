# Sail home only at the start of a day; every dollar handed over is the crew's — 17 September 2026 — cabin MATRIX_PASS, full run MATRIX_PASS

Two Notion missions from Dan, decided today: "yes" — a day nobody has dived
on yet (day 1 fresh from HQ included) may sail home; "both" — the overpaid
money is spendable balance *and* shown.

## Sail home only at the start of a day
`CrewDayState.ServerCanSail` refuses HQ while `DiveDone`: "Dive done — End day
first". The monitor already said "dive done — E on END DAY". After End day
(a day with no dive yet) HQ is offered as before.

## Every dollar handed over is the crew's
`ServerPay` no longer charges the quota out of the balance: the sale joins it
in full. The quota is a bar the cycle's hand-over must clear — a new
server-written `CycleSales` (`PayReport.Had`), reset when the quota is paid
or the run is lost. A short sale before payday is banked *and* counts toward
the cycle. Money from earlier cycles stays the crew's and pays no later quota
(otherwise one good cycle would end diving for good). The board: "PAID $500 —
handed over $510 — every dollar yours · balance $510"; "SHORT BY $n — sold $s,
handed over $h · balance $b"; between pays the money line shows "($h handed
over)".

## Runs
- `cabin` — MATRIX_PASS ([deck-cabin-matrix.log](deck-cabin-matrix.log)):
  Y8 sailing home with the dive done is refused until End day; Z3 paid with a
  $88 quota and a $88 coin → balance $88, nothing charged; Z5 short banks $40
  (balance $128, handed $40), short at payday is GAME LOST and $0. Two stale
  rows fixed on the way (the guest named by display name in a refusal; E1's
  known intermittency showed once and passed on the rerun).
- `fullrun` — MATRIX_PASS ([full-run.log](full-run.log)): sail home after
  the first dive refused until End day; cycle 1 short $464 banked → +$556 =
  $1020 handed over → PAID, balance $1020; cycle 2 judged on its own $426
  (SHORT by $74), balance $1446.
