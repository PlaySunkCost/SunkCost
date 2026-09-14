# Sunk Cost: loot and weight implementation plan

**Status: plan, not yet implemented.** Written 14 September 2026 against `main`
at `a8ffacd` in `github.com/PlaySunkCost/SunkCost`. Owner: Dan. Notion card
"Loot and weight — mass, two-handed heavy balls, weight meter" (epic E3 Carry
and haul, phase 02 Prototype, branch `dan/loot-weight`, touches the contract:
**yes**, needs two-client test: **yes**). It folds in three cards: "Grab
handshake" (In progress), "Three loot objects with real mass" and "Weight — what
you carry changes how you move".

**Read first:** `AGENTS.md`, `docs/NETWORK_CONTRACT.md` (sections 2, 3, 5),
`docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md` (the inventory this builds on),
then `Assets/_Project/Scripts/Interaction/CarryableItem.cs`,
`PlayerInventory.cs`, `Player/HQPlayerController.cs`, `Player/PlayerHudUI.cs`.

---

## 1. Decisions (Dan, 14 September 2026)

1. **Every item has a weight.** Carrying weight makes you heavy and slower.
   Items in your slots count in full, as does the item in your hands.
2. **Weight meter** at the bottom of the screen: a grey bar that fills. Every
   item pushes you closer to the end but you never reach it (asymptote). No
   numbers. No hard cap: a pickup is never refused for weight; the four slots
   and the hands are the only limit.
3. **Slowdown is a smooth curve, never zero.** Empty = 100 %; near the end ≈ 35 %
   of walk speed. Sprint scales the same way.
4. **One-handed items** (mostly light) are held at the right hand as today.
5. **Two-handed heavy items** are held **low in the middle** with both hands.
   They cannot go in a slot. A very big one **blocks part of the view** — that
   is intended, it interrupts playing.
6. While holding a two-handed item: Q drops it, **left click is Use** (for a
   ball: throw), E and 1–4 are blocked (it is an overflow item).
7. **Throw scales with mass**: the basketball flies as today, a heavy ball lobs
   a metre or two. One rule for every ball.
8. **Fixture:** 3 basketballs (light, slot items) + 3 heavy balls in different
   colours and weights, two-handed, not stowable.

Not chosen (so not in): slower turning, head bob, drop thud/noise event, a
red zone, sprint cut-off. Air drain from weight is Idan's oxygen card.

## 2. Starting point (verified in the merged code)

- `CarryableItem` (409 lines) already has `displayName`, `fitsInSlot`, `icon`,
  `useAction`, `throwSpeed`, a Rigidbody (`mass` 0.62 on the basketball) and the
  rigid hold: on the writer, `LateUpdate` snaps the transform to
  `holder.HoldPoint`. `ServerRelease` → `TargetApplyRelease` applies
  `pendingReleaseDirection * throwSpeed` on the holder.
- `PlayerInventory` owns `SyncVar<InventorySlots> slots`, the four request RPCs,
  `ServerFindHeld()` (scan of items Held by this connection) and the disconnect
  dump. `InventoryRules.DecideGrab` already routes `fitsInSlot = false` items to
  the hands (`HoldOverflow` / `StowAndHoldOverflow`) and blocks E and 1–4 while
  an overflow item is held — exactly the two-handed rules in decision 6.
- `HQPlayerController.Move` uses `walkSpeed` 4 / `sprintSpeed` 6 with no
  modifier. `HoldPoint` is a child of `PlayerCamera` at (0.30, −0.22, 0.60).
- `PlayerHudUI` draws the prompt and the four slots (IMGUI); the slot row is
  64 px boxes centred at the bottom with a 24 px margin.
- Settings pattern: `Sites/DiveSiteSettings.cs` is a `ScriptableObject` under
  `Assets/_Project/Settings/Prototype/`. Weight tuning follows it.
- Fixture today: 5 basketball instances in `HQPrototype.unity`
  (`HQPrototypeInventorySetup`, `SceneItemCount = 5`), validator expects 5.

## 3. Design

### 3.1 Weight = Rigidbody mass

One number per item: the Rigidbody's `mass` (kg). No second field to keep in
sync; the physics of a thrown heavy ball and its weight on the meter agree by
construction. `CarryableItem.MassKg => body.mass`.

### 3.2 `WeightSettings` ScriptableObject (`Interaction/WeightSettings.cs`, asset `Settings/Prototype/WeightSettings.asset`)

| Field | Default | Meaning |
|---|---|---|
| `meterScaleKg` | 25 | Asymptote scale. Meter fill = `1 − exp(−mass / meterScaleKg)`: 6 kg → 21 %, 20 kg → 55 %, 38 kg (three heavy + slots) → 78 %, never 100 %. |
| `minSpeedFactor` | 0.35 | Speed factor = `Lerp(1, minSpeedFactor, fill)`. Applied to walk and sprint. |
| `throwReferenceMassKg` | 1 | Throw launch speed = `throwSpeed × sqrt(reference / max(mass, reference))`: 0.62 kg → 8 m/s (clamped at 1×), 6 kg → 3.3, 12 kg → 2.3, 20 kg → 1.8. |
| `minThrowFactor` | 0.15 | Floor for the throw factor. |

Pure static helpers `WeightMath.Fill(mass, scale)`, `SpeedFactor(...)`,
`ThrowFactor(...)` in `Interaction/WeightMath.cs` so the editor checks can
cover the curves without an asset. The asset is referenced by
`PlayerInventory` and `CarryableItem` through a serialized field, set by the
setup script; `WeightSettings.Default` (a static fallback with the values
above) is used when the reference is missing so a scene never divides by zero.

### 3.3 Grip and hold points

- `CarryableItem` gains `enum CarryGrip { OneHand, TwoHands }` (serialized
  `grip`). Two-handed implies `fitsInSlot = false`; the validator errors on a
  prefab that says otherwise.
- The player prefab gains **`TwoHandHoldPoint`** under `PlayerCamera` at
  (0, −0.30, 0.70). `HQPlayerController.HoldPointFor(CarryGrip)` returns the
  right transform; `CarryableItem.LateUpdate` and `ServerGrab`'s teleport use
  it. Nothing else about the hold changes: still kinematic, no collider, snapped
  by the writer.
- A 0.7 m black ball at 0.70 m in front of a camera with a 0.3 near plane
  covers roughly the lower half of the view. That is decision 5; the per-item
  `holdDistanceOffset` (default 0) lets a specific item sit a little further
  out if it ever becomes unplayable.

### 3.4 Carried mass is server state

`PlayerInventory` gains `SyncVar<float> carriedMassKg` (server-written).
`ServerRecomputeCarriedMass()` sums `MassKg` over every spawned item whose
`HolderClientId == Owner.ClientId` and state is **Held or Stowed** (Released
items are in flight, not carried) and runs at the end of every server request
(`Grab`, `Equip`, `Drop`, `Use`), in `ServerDropEverything`, and when an item's
state changes on the server (`CarryableItem` calls
`PlayerInventory.ServerNotifyCarriedChanged(clientId)` from its server-side
`OnStateChanged`, which covers rest handoff and disconnect paths). Recompute is
a scan of six objects; no per-frame work.

Clients read `CarriedMassKg` and derive `MeterFill` and `SpeedFactor` from the
shared settings asset. That derivation is a **client simulation exception**
of the same kind as movement itself (contract section 3: "Player movement —
client, corrected by server"): the server owns the number that matters (the
mass), the client applies it to its own movement, and a server-side speed check
can be added later without changing any message. Contract text in section 6.

### 3.5 Movement

`HQPlayerController.Move`: `speed = (sprint ? sprintSpeed : walkSpeed) *
inventory.SpeedFactor`. That is the whole change. `SpeedFactor` is 1 when there
is no inventory or settings. No turn or look changes (not chosen).

### 3.6 Throw

`CarryableItem.TryApplyPendingRelease`: `linearVelocity = direction *
throwSpeed * WeightMath.ThrowFactor(MassKg, settings)`. The direction still
comes from the server's `TargetApplyRelease`; only the magnitude changes, on the
writer, from numbers both sides know. A dropped item (Q) is unchanged.

### 3.7 HUD

`PlayerHudUI` draws the **weight meter** directly under the slot row: a grey
track (`totalWidth × 8 px`, 6 px below the slots) with a lighter grey fill of
`MeterFill × totalWidth`. No text. The prompt adds "(two hands)" after the name
of a two-handed item so the player knows before pressing E. The slot picture
and the "In hand: … (no slot)" label already handle overflow items.

### 3.8 Fixture: three basketballs, three heavy balls

`HQPrototypeLootSetup.Apply()` (idempotent, `SerializedObject` writes):

| Prefab | Colour | Diameter | Mass | Grip | Slot |
|---|---|---|---|---|---|
| `Basketball` (existing) | orange | 0.24 m | 0.62 kg | one hand | yes |
| `HeavyBallBlue` | blue | 0.40 m | 6 kg | two hands | no |
| `HeavyBallPurple` | purple | 0.55 m | 12 kg | two hands | no |
| `HeavyBallBlack` | black | 0.70 m | 20 kg | two hands | no |

- Creates the three heavy prefabs (sphere, URP material, Rigidbody with the
  mass and continuous collision, `NetworkObject`, client-authoritative
  `NetworkTransform` without scale sync, `CarryableItem` with `displayName`,
  `grip = TwoHands`, `fitsInSlot = false`, `useAction = Throw`, `throwSpeed` 8)
  under `Assets/_Project/Prefabs/Interaction/`. FishNet's prefab collection
  regenerates on import (it logged "found 2 prefabs" last time; expect 5).
- Scene: keeps `Basketball`, `Basketball (2)`, `Basketball (3)`; **removes**
  `Basketball (4)` and `Basketball (5)`; adds one instance of each heavy ball at
  (3, 1, 3), (−3, 1, 3), (0, 1, −3.5) with matching `resetPosition`.
  `SceneItemCount` moves to this class and becomes 6; the validator reads it.
- Assigns `WeightSettings.asset` to the player prefab's `PlayerInventory` and
  to each item prefab; creates the asset with the section 3.2 defaults if
  missing.
- Runs `ItemIconGenerator.GenerateAll()` (heavy balls get icons too; unused
  until something shows them, harmless).
- Player prefab: adds `TwoHandHoldPoint` under `PlayerCamera` and points
  `HQPlayerController.twoHandHoldPoint` at it. `HQPrototypeBuilder` generates
  the same for a fresh project.

## 4. Files

| File | Change |
|---|---|
| `Interaction/CarryGrip.cs` | new enum |
| `Interaction/WeightSettings.cs`, `Interaction/WeightMath.cs` | new |
| `Interaction/CarryableItem.cs` | `grip`, `holdDistanceOffset`, `weightSettings`; `MassKg`; hold point by grip; throw factor; server state-change notification |
| `Interaction/PlayerInventory.cs` | `carriedMassKg` SyncVar, `ServerRecomputeCarriedMass`, `ServerNotifyCarriedChanged`, `MeterFill`, `SpeedFactor`, `weightSettings` |
| `Player/HQPlayerController.cs` | `twoHandHoldPoint`, `HoldPointFor(grip)`, speed factor in `Move` |
| `Player/PlayerHudUI.cs` | meter, "(two hands)" in the prompt |
| `Editor/Prototype/HQPrototypeLootSetup.cs` | new: prefabs, scene fixture, settings asset, hold point |
| `Editor/Prototype/HQPrototypeWeightChecks.cs` | new: pure curve checks + grab table with two-handed items |
| `Editor/Prototype/HQPrototypeValidator.cs`, `HQPrototypeBuilder.cs`, `HQPrototypeTestHooks.cs`, `HQPrototypeInventorySetup.cs` | item count, prefab checks, `CarriedMassText()`, `SpeedFactor()` hooks, `SceneItemCount` moved |
| `Settings/Prototype/WeightSettings.asset`, three heavy ball prefabs + materials, three icons | assets, generated by the setup |
| `docs/NETWORK_CONTRACT.md`, `docs/DESIGN.md`, `README.md`, `docs/HQ_PROTOTYPE_TEST_REPORT.md` | section 6 |

## 5. Verification

Pure (`HQPrototypeWeightChecks.RunOrThrow()`): fill is 0 at 0 kg, strictly
increasing, < 1 at 1000 kg; speed factor is 1 at 0 kg and ≥ `minSpeedFactor`
always; throw factor is 1 for the basketball and ≈ 0.41 / 0.29 / 0.22 for
6 / 12 / 20 kg; `DecideGrab(fitsInSlot:false, …)` never yields a slot outcome.

Runtime, Local (editor host + standalone guest, hooks):

| Id | Case | Expected |
|---|---|---|
| W1 | Grab basketball, then 6 kg ball | `carriedMassKg` 0.62 → basketball stowed (fifth-pickup rule) + 6.62; meter fill ≈ 23 %; `SpeedFactor` ≈ 0.85 |
| W2 | Stack: 3 basketballs in slots + 20 kg in hands | mass 21.9; fill ≈ 58 %; speed ≈ 0.62; drop the black ball → 1.86, speed ≈ 0.95 |
| W3 | Two-handed hold point | `HoldOffset()` ≈ 0 against `TwoHandHoldPoint`, not `HoldPoint`; guest sees the ball centred low on the host |
| W4 | Heavy ball never in a slot | E on a heavy ball with a free slot → hands only, `HeldSlot = −1`, "In hand: Blue ball (no slot)"; 1–4 blocked |
| W5 | Throw scaling | `Use` on the basketball → ~8 m/s; on the 20 kg ball → ~1.8 m/s (read `linearVelocity` the frame after) |
| W6 | Speed actually changes | move the guest with held input for 2 s empty vs. carrying 20 kg; distance ratio ≈ speed factor |
| W7 | Disconnect / re-host | mass back to 0 on the dump; six items at their reset positions after re-host |
| W8 | Meter | Game view capture: grey bar under the slots, partially filled |

Steam, two machines: W1, W3, W5 with a shared build; record RTT.

## 6. Contract and design edits (same PR)

- `NETWORK_CONTRACT.md` section 3 table: *Carried mass — Server only — summed
  from the items the server says a player holds or stows; clients derive the
  speed factor from the shared `WeightSettings`.* Section 2, one sentence after
  the Stowed paragraph: *A two-handed item is always an overflow item; the
  server never assigns it a slot.* Section 5 rule 1 already covers the client
  applying its own speed; add "(weight factor included)" to the movement row.
- `DESIGN.md` section 3 "Carrying" row: add "Two-handed objects are held in
  front with both hands and cannot be stowed; weight fills a meter that never
  reaches the end." Harness paragraph: six items, meter.
- `README.md` controls: weight meter, two-handed balls.
- Idan: the loot prefab interface for his spawn-points card is `CarryableItem`
  + `Rigidbody.mass` + `grip`; tell him when this merges.

## 7. Sequence

1. `CarryGrip`, `WeightSettings`, `WeightMath`, pure checks. Compile.
2. Item + inventory + controller + HUD changes. Compile, validator still passes
   with 5 items (count constant not yet moved).
3. `HQPrototypeLootSetup.Apply()`, prefab/scene/asset generation, validator to 6.
4. Runtime W1–W8 with hooks and a capture; test report.
5. Docs, PR, Notion.
