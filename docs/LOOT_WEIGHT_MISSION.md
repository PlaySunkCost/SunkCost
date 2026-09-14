# Mission brief: loot and weight — items have mass, weight slows you, two-handed heavy balls

**Status: brief for another assistant to plan and build.** Written 14 September
2026 against `main` at `a8ffacd` (`github.com/PlaySunkCost/SunkCost`). Branch:
`dan/loot-weight` (already created; this file and a suggested design are on it).
Owner: Dan. Epic: E3 Carry and haul. Phase: 02 Prototype. Touches the contract:
**yes** (one new server-owned number). Needs two-client test: **yes**.

A worked-out design already exists in `docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md`
(files, numbers, verification rows). The planner may follow it or replace it;
the decisions in section 2 below are the fixed part, the design is not.

Notion card: "Loot and weight — mass, two-handed heavy balls, weight meter".
It folds in three existing cards: "Grab handshake" (In progress), "Three loot
objects with real mass" and "Weight — what you carry changes how you move".

---

## 1. The goal in one paragraph

Right now the HQ room has five identical basketballs and carrying them costs
nothing. After this mission every item weighs something, carrying weight makes
you slower on a smooth curve that never reaches zero, a grey meter at the
bottom of the screen fills toward an end you can never reach, light items go in
your right hand or a slot as today, and heavy items need both hands: held low
in the middle, never stowable, big enough to block part of your view. The room
gets three basketballs and three heavy balls of different colours and weights.
This is the "help me carry this" feeling DESIGN.md says the whole game rests on.

## 2. Decisions (fixed — Dan, 14 September 2026)

1. **Every item has a weight.** Carried weight = what is in your hands + what is
   in your four slots, all counted in full.
2. **Slower on a smooth curve, never zero.** Empty = 100 %; near the end of the
   meter ≈ 35 % of walk speed. Sprint scales the same way. No sprint cut-off,
   no red zone, no pickup ever refused for weight.
3. **Weight meter:** a plain grey bar under the inventory slots that fills as
   you carry more. Asymptotic: every item pushes you closer to the end, you
   never get there. No numbers.
4. **One-handed items** (mostly light): held at the right hand, can go in a slot.
   Unchanged from today.
5. **Two-handed items** (heavy): held low in the middle with both hands; cannot
   go in a slot; a very big one covers part of the camera on purpose.
6. While holding a two-handed item: **Q** drops it, **left click is Use** (a
   ball's Use is throw), **E and 1–4 do nothing** — same as the existing
   overflow rule.
7. **Throw scales with mass**: basketball flies as today, a heavy ball only lobs
   a metre or two. One rule for all balls.
8. **Fixture in the HQ room:** 3 basketballs + 3 heavy balls (different colours
   and weights, two-handed, not stowable). Remove the two extra basketballs.

Explicitly not in this mission: slower turning, head bob, drop thud / noise
event, air drain from weight (Idan's oxygen card), loot value / quota.

## 3. What exists today (verified)

- `Assets/_Project/Scripts/Interaction/CarryableItem.cs`: the item state
  machine (Free / Held / Released / Stowed), server-written SyncVars, rigid hold
  (the writer snaps the item to `HQPlayerController.HoldPoint` every
  `LateUpdate`), throw impulse `direction × throwSpeed`, `displayName`,
  `fitsInSlot`, `icon`, `useAction`. Its Rigidbody `mass` is 0.62 on the ball.
- `Interaction/PlayerInventory.cs`: four slots (`SyncVar<InventorySlots>`), the
  `ServerRequestGrab/Equip/Drop/Use` RPCs, `InventoryRules` decision table
  (already sends `fitsInSlot = false` items to the hands as "overflow" and
  blocks E and 1–4 while overflow is held), disconnect dump.
- `Player/HQPlayerController.cs`: walk 4 / sprint 6 m/s, no modifier; `HoldPoint`
  is a child of `PlayerCamera` at (0.30, −0.22, 0.60).
- `Player/PlayerHudUI.cs`: IMGUI prompt + four slot boxes at the bottom.
- `Sites/DiveSiteSettings.cs` shows the ScriptableObject pattern for tuning
  numbers (`Assets/_Project/Settings/Prototype/`).
- Editor: `HQPrototypeInventorySetup` (scripted prefab/scene edits, idempotent),
  `ItemIconGenerator`, `HQPrototypeValidator` (expects 5 items),
  `HQPrototypeTestHooks` (MCP hooks; Unity MCP's dynamic assembly cannot see
  FishNet types, so every check goes through these hooks), `HQPrototypeInventoryChecks`.
- Contract: `NETWORK_CONTRACT.md` section 3 says player movement is
  client-simulated, loosely corrected by the server; inventory and stowed items
  are server-only.

## 4. What has to be done

- **Mass on items** — one number per item; the suggested design uses the
  Rigidbody mass so physics and the meter agree.
- **Carried mass as server state** — the server owns the inventory, so it sums
  the mass and replicates it; clients derive speed factor and meter fill from a
  shared tuning asset. Recompute on every inventory change and on disconnect.
- **Movement** reads the factor. Nothing else about movement changes.
- **Grip**: one-hand vs two-hands per item; a second hold point (centre, low)
  on the player prefab; the hold snap uses the point for the item's grip.
- **Throw factor** from mass, applied where the throw impulse is applied.
- **HUD**: the meter; the prompt says "(two hands)" for two-handed items.
- **Fixture**: three heavy ball prefabs (colour, size, mass), scene instances,
  removal of two basketballs, validator count 6, icons regenerated. All through
  a scripted, idempotent setup like `HQPrototypeInventorySetup` — no hand edits
  of prefabs or the scene.
- **Checks**: pure checks for the curves and the two-handed grab rule; runtime
  rows with hooks (mass sums, speed factor, hold point, throw speed, slot
  refusal, disconnect, re-host); a Game view capture of the meter.
- **Docs**: contract section 3 row for carried mass (server only) and the
  two-handed/overflow sentence in section 2; DESIGN.md section 3 "Carrying" and
  the harness paragraph; README controls; test report.

## 5. Done means

- Pick up a basketball: meter moves a little, you barely slow down. Pick up the
  black ball: it sits in the middle of the screen, covers the lower part of the
  view, you crawl, the meter is past half. Drop it: back to normal.
- Three basketballs in slots + a heavy ball in hand: meter high, still moving.
  Nothing ever refuses a pickup for weight.
- A heavy ball never appears in a slot; 1–4 and E do nothing while you hold it;
  Q drops it; click lobs it a short way; the basketball still flies.
- A second machine sees the heavy ball centred and low on the carrying player.
- Leave / disconnect drops everything and the carried mass goes to zero;
  re-hosting puts all six items back.
- Recorded in `docs/HQ_PROTOTYPE_TEST_REPORT.md` with build revision and roles;
  Local matrix by hooks, Steam rows with two people.

## 6. Rules that apply

- Server decides gameplay outcomes; the carried mass is a server-written
  SyncVar. Client-computed speed from a shared asset is a movement-simulation
  exception, like movement itself — say so in the contract.
- Anything crossing the network (the new SyncVar, any new RPC) is read by Idan
  or Dor before merge (`CONVENTIONS.md`, "The AI rule"). Loot/UI are Dev C's
  column: tell Dor.
- Exit Play Mode before editing scripts; never `AssetDatabase.Refresh` while a
  session is live. Don't commit Unity's auto-migrated settings churn.
- Commits end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`;
  PR bodies end with the Claude Code line; `main` is protected, PRs only.

## 7. Open for the planner

1. Exact numbers (meter scale, minimum speed factor, throw curve, heavy ball
   masses/sizes/colours) — the suggested design has a set; tune in the editor.
2. Whether heavy balls need icons at all (nothing shows them today).
3. Whether the server should already validate movement speed against the
   carried mass, or leave that for later as the contract allows.
