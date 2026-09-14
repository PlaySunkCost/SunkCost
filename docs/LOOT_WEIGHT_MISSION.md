# Mission: loot and weight

**Built on `dan/loot-weight`; Local verification done, Steam rows and teammate review open.** Reviewed
14 September 2026 on `dan/loot-weight` at `f296d2c` (base main `a8ffacd`).
Send [LOOT_WEIGHT_IMPLEMENTATION_PLAN.md](LOOT_WEIGHT_IMPLEMENTATION_PLAN.md)
with this repository to the builder. It contains the complete rules, calculations,
file map, asset migration, lifecycle, release safety, tests and build sequence.

Owner: Dan. Epic E3 Carry and haul, Phase 02 Prototype. Touches network/contract:
**yes**. Separate-client tests and teammate network review: **required**.
The original brief associates this with the Notion loot/weight, grab-handshake
and mass cards; their live status has not been checked or changed here.

## Gameplay requirements retained

1. Every carried item weighs something. Count all Held/Stowed items in full,
   but an equipped slot item counts **once**. Source: Rigidbody mass.
2. **Amended 14 September 2026 (Dan):** the bar is a hard capacity (25 kg).
   Walk/sprint slow in a straight line from 100 % at empty to 35 % just under
   full; at a full bar you **cannot move at all** (WASD, sprint and any future
   dash) until you drop something. Everything else — look, grab, drop, throw,
   equip, menu — keeps working. No pickup is ever refused for weight.
3. Plain grey unnumbered bar below the slots; it turns **red** when full and a
   line above the slots says "Too heavy to move — drop something".
4. One-handed slot items keep the right pose. Heavy two-handed items are
   hands-only, held low/centrally and obscure some lower view. This prototype
   implements grip/pose, not new animated hands.
5. Heavy held: Q drops, click Uses (throws these balls); E/1–4 blocked.
   Stored cargo remains stored and counts toward weight. This is not corpse drag.
6. Launch speed decreases with individual item mass; basketball keeps current
   launch speed, heavy balls make short lobs.
7. HQ has three basketballs plus blue, purple and charcoal heavy balls. Replace
   only the two identified extra basketballs; preserve other team assets.

No oxygen drain, prices/quota, noise/thuds, joint carrying, head bob, slower
turning or new movement enforcement. Future oxygen can read authoritative carried
mass; this task does not implement that integration.

## Planner decisions and corrections

The user requested a complete build plan and improvements. The handoff explicitly
resolves these choices instead of leaving them for the builder:

- Grabbing hands-only cargo with a slot item equipped auto-stows that item in
  its existing slot **even if slots are free**. Current code must change for this.
  No automatic dropping.
- Capacity 25 kg, speed floor 0.35 just under full (superseded the 12 kg
  asymptote). Blue is one-handed and slot-able (6 kg, two in the room) so the
  slots alone can reach 12 kg and a black ball in the hands overloads you:
  2 blue + purple = 24 kg (walk 1.5 m/s), 2 blue + black = 32 kg (stuck).
- Unique Held/Stowed objects determine server mass, recalculated after complete
  transitions, including holder change, despawn, reset and disconnect.
- Radius-aware drop/recovery prevents large spheres embedding in the floor or
  the player; the releasing writer places the item (no new network message).
- One fixture reconciler prevents old setup scripts renaming heavies or recreating
  removed balls; repeated setup preserves tuning and existing GUIDs.
- Temporary light test objects retain full-slot/fifth-pickup regression coverage:
  the three saved light balls cannot fill four slots by themselves.
- No unused heavy icons or invented assistant co-author/approval.

## Done means

Code/assets and the handoff's Local checks pass with actual separate peers,
screenshots and measured movement/launch behavior. Steam uses matching builds on
two computers, with roles and RTT recorded. A teammate reviews network/contract
changes before merge. Playtest feedback determines whether the initial tuning
feels good; this planning document cannot certify that.

This task changes these two Markdown files only. No scripts, prefabs, scenes,
tuning assets, gameplay sessions, builds or task-board state are changed.
