# Loot and weight — implementation handoff

**Status: implemented on `dan/loot-weight` (14 September 2026); Local rows in
`docs/HQ_PROTOTYPE_TEST_REPORT.md` ("Loot and weight branch"); Steam rows,
teammate review and the feel gate remain.** Two simplifications are marked
"Builder simplification" in sections 5 and 6; one setup finding (FishNet scene
id throttling) is recorded in the report.
Reviewed 14 September 2026 on `dan/loot-weight` at `f296d2c`, based on merged
inventory at `a8ffacd`. Before building, inspect newer main and local changes.
Companion: [mission](LOOT_WEIGHT_MISSION.md).

Read [DESIGN](DESIGN.md), [NETWORK_CONTRACT](NETWORK_CONTRACT.md),
[CONVENTIONS](CONVENTIONS.md), [WORKFLOW](WORKFLOW.md) and current inventory code.
The mission's gameplay requirements are retained. **Planner decisions** below
resolve omissions under the user's request to improve this plan. Numerical
defaults are initial tuning, not playtest findings. Human network review is
still required before merge; this document claims no approval.

## 1. Outcome and scope

Make hauling a visible tradeoff: small cargo barely affects mobility, heavy
cargo reduces travel speed and occupies the hand, and dropping cargo restores
the remaining load's speed after the server accepts release. Preserve aiming
and the ability to walk/sprint; do not invent a weight capacity.

- Four slots plus a separate hand remain. An equipped slot item occupies its
  existing slot: **count it once**, not again for the hand.
- All Held/Stowed items count in full; Free/Released do not. A throw stops
  weighing down the thrower before settling. A catch adds weight to the catcher.
- One-handed slot items use the right hold point. Two-handed cargo is always
  hands-only with a low central pose. Previously stored items remain stored and
  still contribute mass.
- Grey, unnumbered meter below slots; smooth slowdown toward 35%, no red zone,
  weight rejection or sprint cutoff. Look speed unchanged.
- Q drops, click Uses (throws these balls); E and 1–4 blocked during overflow.
  Preserve the E buffer, moving catches, 2 m reach, LOS and one request per gesture.
- Six-item HQ fixture: three basketballs and three differently sized/coloured
  heavy balls. No migration of the underwater scene.

Excluded: oxygen implementation, value/quota, persistence, corpse dragging,
two-player joint carrying, damage/friendly fire, noise/thuds, slower turning,
head bob, stamina, new movement anti-cheat, procedural spawning, hand animation/IK
and new art dependencies. The current player prefab has no animated arm rig:
two-handed means capacity and held pose, not delivered fingers/arm animation.
The six balls test hauling choices, not the complete horror/economy loop.

## 2. Audit corrections

| Previous gap | Resolution |
|---|---|
| W1 expected a heavy pickup with an equipped light and free slots, but current code refuses it | Explicit new auto-stow row in section 3 |
| Summing slots plus hand could count equipped items twice | Unique authoritative Held/Stowed items |
| State callback alone misses holder/lifetime changes and may run mid-transition | Dirty aggregation after completed transitions; lifecycle coverage |
| 38 kg example assumed three hands-only heavies at once | Remove impossible load; default maximum is 21.86 kg |
| Scale 25 barely approached the stated heavy-load experience | Initial scale 12; retain 35% lower limit |
| Exponential fill can round to exactly 1 | Numeric clamp and visible one-pixel end gap |
| Q/disconnect placement assumes a small basketball | Radius-aware, non-penetrating placement |
| Central point implied finished two-handed animation | Pose only, no rig/IK scope |
| Old setup renames every carryable as Basketball | One manifest-driven fixture reconciler |
| Three light items cannot exercise four occupied slots plus a fifth | Temporary extra light objects in regression tests |
| A later velocity sample was treated as launch speed | Record at assignment; separately measure first-contact distance |
| Heavy icons had no consumer | Keep basketball icon; defer unused heavy icons |
| Mandatory Claude attribution | Truthful attribution only; no fabricated contributor/approval |

## 3. Complete inventory rules

**Planner decision:** grabbing hands-only cargo automatically puts away an
equipped slot item even if there are unused slots. That equipped item already
has a slot; requiring a number-key press adds friction without a carrying
decision. This makes the brief's basketball-then-heavy example work.

Validate sender, spawn/lifetime, Free/Released eligibility, reach and LOS before
changing either item.

| Current hand | Target / slots | Result |
|---|---|---|
| Empty | Slot item / free slot | Assign first free slot and equip target |
| Equipped slot item | Slot item / free slot | Silently stow target; keep current hand |
| Empty | Slot item / full slots | Hold target as overflow; slots unchanged |
| Equipped slot item | Slot item / full slots | Stow equipped item in its slot; hold target as overflow |
| Empty | Hands-only / any slots | Hold target as overflow; slots unchanged |
| Equipped slot item | Hands-only / any slots | Stow equipped item in its slot; hold target as overflow |
| Overflow held | Slot item / free slot | Silently stow target (second amendment: "Press E to store"); hands unchanged |
| Overflow held | Hands-only, or full slots | Refuse with existing Hands full feedback; change nothing |

Reuse `StowAndHoldOverflow` for the new row and the existing request RPC.
If grabbing the target fails after stowing, re-equip the old item, preserve
slots and report failure. Aggregate weight after the transaction resolves.
Never auto-drop or leave two items Held. Contention losers keep their old hand.

Grip enum defaults to `OneHand=0` for existing assets; `TwoHands=1`.
Effective `FitsInSlot => grip != CarryGrip.TwoHands && fitsInSlot`.
The validator rejects TwoHands with serialized fitsInSlot=true, but runtime
must enforce it too. Guard direct ServerStow/ServerStowFromWorld and equip
paths, not just UI/decision helpers. Unknown enum values are invalid content.
Grip is authored, not inferred from a mass threshold.

Overflow blocks E/1–4 until Q/Use frees it. Dropping it clears no unrelated
slots. No weight-based pickup refusal is added; reach, obstruction, item-state
and hand-capacity refusals still apply.

## 4. Mass, settings and numerical behavior

### Source and validity

`CarryableItem.MassKg` reads its cached root Rigidbody.mass, in kg. No second
serialized weight. Prefab mass must be finite and >0. Validate before shipping;
invalid runtime content logs once and uses 0.62 kg as fault containment rather
than propagating NaN into movement. Invalid content must still fail validation.

Mass, grip and tuning are static for a session. No live Inspector mass edits or
runtime resizing in acceptance tests; restart after tuning. Future variable-load
cargo needs an explicit server-authorized mass-change API and replication, out
of scope here. Mesh diameter does not automatically determine authored mass.

### Shared settings

Create `SunkCost.Interaction.WeightSettings : ScriptableObject`, asset
`Assets/_Project/Settings/Prototype/WeightSettings.asset`. Every inventory/item
references this same asset through a serialized field with read-only accessors.
No Resources lookup, per-player settings creation or mutable global singleton.

| Field | Initial value | Valid domain |
|---|---:|---|
| capacityKg | 25 kg | finite, >0 |
| minSpeedFactor | 0.35 | finite, >0 and <=1 |
| overloadSpeedFactor | 0.01 | finite, >0 and <=1 |
| throwReferenceMassKg | 1 kg | finite, >0 |
| minThrowFactor | 0.15 | finite, >0 and <=1 |

`WeightMath` is pure, with scalar inputs:

**Amended 14 September 2026 (Dan, after the first build):** the meter is a hard
capacity, not an asymptote. Full = red bar = a 1 % crawl (`overloadSpeedFactor`,
second amendment: not a full stop).

```text
MeterFill   = min(1, totalMass / capacityKg)
Overloaded  = totalMass >= capacityKg
SpeedFactor = Overloaded ? overloadSpeedFactor : 1 - (1 - minSpeedFactor) * MeterFill
ThrowFactor(itemMass) = clamp(sqrt(reference / max(itemMass, reference)),
                              minThrowFactor, 1)
launchSpeed = existing throwSpeed * ThrowFactor(itemMass)
```

`HQPlayerController.Overloaded` exposes the state for a future dash; movement
itself just multiplies by `SpeedFactor`. Looking, grabbing, dropping, throwing,
equipping and the menu are unaffected.

Use double intermediate aggregation/exponential/division and finite float
outputs. One centralized immutable default set handles missing/invalid settings
with a once-per-component warning. Negative/nonfinite pure math mass inputs
sanitize to zero; item content uses the positive fallback above. Missing asset
must not mean speed 1 on one component and default slowdown on another.
Validator fails missing/mismatched shared references. Never tune only one peer
during a session.

| Load | kg | Fill % | Speed factor | Walk / sprint m/s |
|---|---:|---:|---:|---:|
| Empty | 0 | 0 | 1 | 4 / 6 |
| Basketball | 0.62 | 2.5 | 0.9839 | 3.94 / 5.90 |
| Three basketballs | 1.86 | 7.4 | 0.9516 | 3.81 / 5.71 |
| Blue | 6 | 24 | 0.844 | 3.38 / 5.06 |
| Basketball + blue | 6.62 | 26.5 | 0.8279 | 3.31 / 4.97 |
| Two blues (slots) | 12 | 48 | 0.688 | 2.75 / 4.13 |
| Purple | 12 | 48 | 0.688 | 2.75 / 4.13 |
| Black | 20 | 80 | 0.48 | 1.92 / 2.88 |
| Two blues + purple | 24 | 96 | 0.376 | 1.50 / 2.26 |
| Two blues + black | 32 | 100 (red) | 0.01 | 0.04 / 0.06 (crawl) |

Calculated defaults, not proof of fun. Only diagnostics show numbers.

## 5. Server mass and lifecycle

Add **one** `SyncVar<float> carriedMassKg` to PlayerInventory and read APIs
`CarriedMassKg`, `SpeedFactor`, `MeterFill`. Only the server writes it; no RPC
accepts client mass, factor or total.

Sum unique spawned CarryableItems with matching HolderClientId and Held/Stowed
state. Do not sum UI slots plus HeldItem. The host's owner-side HeldItem is null
for remote players; it is not the authoritative source. Stowed items have no
client owner, so OwnerId is also the wrong source.

**Builder simplification (14 September 2026, Dan's session):** recompute
directly, no dirty flag or tick hook. `PlayerInventory.ServerRecomputeCarriedMass()`
scans the spawned items (six objects) and writes the SyncVar only when the sum
changes. It is called at the end of every server request (after the last
transition of that request, so a stow+grab pair is summed once), in
`ServerDropEverything`, in `OnStartServer` (zero), and from
`CarryableItem`'s server-side state/holder callbacks through a static
`PlayerInventory.ServerRecomputeAll()` (rest handoff, disconnect drop, reset,
despawn). An intermediate sum written inside a two-step transition is harmless:
FishNet sends a SyncVar's last value per tick, and the final call in the same
request restores the correct sum before the tick flushes. Exclude items that
are not spawned. `OnStopServer` needs no teardown; re-host starts at zero.

A guest may see the accepted change at the next replication flush. Record that
delay; do not write speculative client mass to conceal it. Checking a dirty
boolean each tick is fine; scanning every frame or per-frame RPCs is not.

| Transition | Mass behavior |
|---|---|
| Grab/catch | Add target once to accepted catcher |
| Held <-> Stowed / swap | Total unchanged |
| Fifth/auto-stow pickup | Add new target only |
| Drop/throw | Subtract on accepted Held -> Released, not at rest |
| Released re-caught | Thrower already subtracted; catcher adds |
| Rejected/stale/duplicate action | No lasting change |
| Held/Stowed despawn or server reset | Remove from previous carrier without client request |
| Late join | Initial SyncVar totals correct without event replay |
| Guest clean/abrupt loss | Existing harness recovery drops items; other totals remain correct |
| Host Leave/re-host | Fresh mass zero; six fixtures reset |

Add kg/factor to inventory DebugStatus and kg/grip to item diagnostics, preserving
WriterOverride. Future oxygen can read server CarriedMassKg; do not add tank mass
or oxygen modifiers without that equipment integration.

## 6. Movement, grip and release safety

### Movement

In HQPlayerController.Move multiply selected walk/sprint speed by
inventory.SpeedFactor. Null inventory alone means factor 1. Preserve gravity,
diagonal normalization, look, input gate and NetworkTransform. Remote observers
do not multiply received movement again.

Existing client movement authority is unchanged. A server-derived speed factor
does **not** prove server speed enforcement; no anti-cheat claim. Shared build
identity keeps ordinary Steam peers on matching settings. Do not weaken admission
or add a movement reconciliation project here.

### Grip pose

Add TwoHandHoldPoint under existing PlayerCamera at `(0,-0.35,0.80)`; retain
right HoldPoint `(0.30,-0.22,0.60)`. Player root remains scale 1. Add
HQPlayerController.HoldPointFor(CarryGrip) and per-item serialized
holdDistanceOffset (default 0, along selected pose's forward).

One shared pose calculation serves writer LateUpdate, server equip teleport and
test hooks. Missing central reference falls back to right point with a diagnostic
and fails validator. Remote observers render NetworkTransform; never re-snap to
their unreplicated copy of camera pitch. Preserve motionVersion/pending gates.

Largest radius=0.35 m, near surface at z=0.45 with center z=0.80, beyond current
near clip=0.30. Current camera FOV=75 degrees. Intended low occlusion must leave
a usable center sightline. This geometry is a starting point, not a screenshot
result. Test level/+/-80-degree pitch, sprint, walls and remote side view.
Adjust serialized pose/offset, not camera/collider/look behavior.

### Throw and placement

Keep base throwSpeed 8. Initial speeds at 0.62/6/12/20 kg are
8.00/3.27/2.31/1.79 m/s. Use **item** mass, not carried total.
Rigidbody.mass alone does not reduce a directly assigned velocity.

Multiply once in TryApplyPendingRelease, where linearVelocity is written; do not
also scale direction or add another impulse. Validate finite direction components
and magnitude, normalize once, retain version/owner/state agreement. Q uses zero
launch velocity. Keep exactly one writer.

The current Q offset (forward 0.5, up 0.3) and the disconnect scatter can embed
a 0.70 m ball in the floor or the player capsule. **Builder simplification:**
placement stays with whoever already writes the item's transform; no new RPC
payload and no server-chosen positions.

- `CarryableItem.Radius` = SphereCollider radius × largest lossy scale axis,
  valid while the collider is disabled.
- Q (writer side, in `TryApplyPendingRelease`): start from the player's feet,
  forward `playerRadius + Radius + 0.1`, up `Radius + 0.05`. If a sphere check
  at that point (excluding this item and the player) hits geometry, use the
  current held pose instead (it is already clear of the capsule for every
  fixture size: 0.80 m forward against a 0.30 m capsule and a 0.35 m radius).
  Zero velocity, `Teleport()`. A held item that pokes through a wall drops on
  the player's side because the feet-based point is inside the room.
- Throws start at the held pose, as today.
- Disconnect recovery (server side): scatter ring radius `0.4 + Radius`, height
  `Radius + 0.05`, index-based angle so several items never share a point.
- Spheres only; the validator rejects a `CarryableItem` without a SphereCollider
  until another shape's placement is defined.

Check throws/drops cannot launch inside the character capsule. No damage/noise.
“One or two metres” means a short lob, not a range guarantee at every pitch,
height, slope or bounce. Record distance to **first floor contact** at specified
release height/aim; report later rolling separately. Preserve basketball behavior
unless a safety correction requires a documented adjustment. Do not tune gravity.

## 7. HUD and assets

### HUD

Owner-only, same menu/input visibility gate. Track y=slotTop+SlotSize+6,
height=8, width=slot-row width. Existing bottom margin 24 leaves 10 px underneath.
Dark-grey track, lighter-grey fill; no gradients or flashing. At capacity the
fill is full and **red**, and one line above the slot row reads "Too heavy to
move — drop something" (amended 14 September 2026).

No animation/smoothing initially. Append `(two hands)` to both ground-grab and
moving-catch prompts. Preserve refusal priority, overflow label and slot visuals.
Numbers stay in F3/F4/hooks. Verify 1280x720, 1920x1080 and resized windows.
Keep basketball icon; heavy icons have no current consumer and are deferred.

### Fixture manifest

Duplicate the known working basketball prefab through Unity for three new heavy
prefabs, then alter intended fields. Preserve tested NetworkObject/Transform/
observer settings; never guess client-authority serialization or copy object IDs.
New prefabs get new GUIDs; existing player/basketball .meta GUIDs stay intact.
Keep shared physics material initially; launch factor is the throw adjustment.

| Scene name / prefab | Diameter m | kg | Colour | Grip / slot | Reset center |
|---|---:|---:|---|---|---|
| Basketball / Basketball | 0.24 | 0.62 | Existing orange | OneHand / yes | (0,1,0) |
| Basketball (2) / Basketball | 0.24 | 0.62 | Existing orange | OneHand / yes | (1.5,1,1.5) |
| Basketball (3) / Basketball | 0.24 | 0.62 | Existing orange | OneHand / yes | (-1.5,1,1.5) |
| HeavyBallBlue | 0.40 | 6 | Blue | OneHand / yes (amended: slot-able, so slots can be loaded) | (3,1,0) |
| HeavyBallBlue (2) / HeavyBallBlue | 0.40 | 6 | Blue | OneHand / yes | (3,1,2.5) |
| HeavyBallPurple | 0.55 | 12 | Purple | TwoHands / no | (-3,1,0) |
| HeavyBallBlack | 0.70 | 20 | Readable charcoal | TwoHands / no | (0,1,-3.5) |

Avoid corner player spawns; verify actual scene clearance. Balls settle from y=1
on hosting as today. Names identify local fixtures; network assertions use actual
NetworkObject IDs, not assumed numeric spawn order.

### One idempotent upgrade owner

`HQPrototypeLootSetup.Apply()` owns a single manifest (names, prefab paths,
reset positions). Validator/hooks use it. Only run outside Play in the HQ scene.

1. Preflight actual scene/refs/dirty state; coordinate shared asset editing.
   Do not save unrelated dirty scenes or rebuild the HQ room.
2. Create settings/materials/prefabs only when absent; reconcile explicitly owned
   fields. Repeat Apply preserves tuned values; reset-tuning is a separate action.
3. Patch player narrowly: point/ref and settings; preserve appearance, forward
   marker, cameras, layers and network components.
4. Keep the three specified basketballs. Remove only Basketball (4)/(5), after
   verifying source prefab. Unexpected name/source -> diagnostic, no deletion.
5. Add/reconcile three heavies. SerializedObject/PrefabUtility name and property
   overrides must survive scene save/close/reopen; no global index-based renaming.
6. Refactor HQPrototypeInventorySetup to retain component setup but delegate
   fixture work to this reconciler. Calling either menu after upgrade preserves
   six identities; no extras reappear, no heavy renamed, no recursive Apply.
7. Update fresh-build HQPrototypeBuilder path to share manifest/setup; never run
   its full regeneration on the team's current player/scene.
8. Verify prefab registration by references, not global prefab count (other
   scenes can add network prefabs). Keep scale authored; no scale sync needed.
9. Apply twice and reopen: second run has no semantic diff or duplicate GUIDs.
   Do not regenerate all icons every time.

## 8. File map

Relative to `Assets/_Project/` unless otherwise stated.

| File | Work |
|---|---|
| Scripts/Interaction/CarryGrip.cs | New enum |
| Scripts/Interaction/WeightMath.cs | Pure curves, sanitization, centralized defaults |
| Scripts/Interaction/WeightSettings.cs | Serialized asset, readonly access, validation |
| Scripts/Interaction/CarryableItem.cs | Mass/grip/settings, effective slot rule, pose, radius-aware drop, scaled launch, server recompute triggers |
| Scripts/Interaction/PlayerInventory.cs | Server mass SyncVar + direct recompute, read APIs, radius-aware recovery |
| Scripts/Interaction/InventoryRules.cs | New hands-only auto-stow row |
| Scripts/Player/HQPlayerController.cs | Grip point selection, movement multiplier |
| Scripts/Player/PlayerHudUI.cs | Grey bar, two-hands prompt |
| Scripts/Net/InventoryVerificationPeer.cs | Read-only kg/grip/factor/launch diagnostics, preserve opt-in Local guard |
| Editor/Prototype/HQPrototypeLootSetup.cs | Manifest, targeted migration |
| Editor/Prototype/HQPrototypeWeightChecks.cs | Pure/asset checks |
| Editor/Prototype/HQPrototypeInventorySetup.cs | Remove independent five-ball fixture ownership |
| Editor/Prototype/HQPrototypeBuilder.cs, HQPrototypeValidator.cs | Shared manifest and new invariants |
| Editor/Prototype/HQPrototypeTestHooks.cs | CarriedMassText, grip-aware HoldOffset, launch speed diagnostic, timed walk |
| Editor/Prototype/HQPrototypeInventoryChecks.cs, HQInventoryRuntimeChecks.cs, HQCatchRuntimeChecks.cs | Updated rules/fixtures, retain regression coverage |
| Settings/Prototype/WeightSettings.asset | Shared tuning |
| Prefabs/Interaction/HeavyBallBlue.prefab, HeavyBallPurple.prefab, HeavyBallBlack.prefab | New registered prefabs |
| Art/Prototype/Materials/HeavyBallBlue.mat, HeavyBallPurple.mat, HeavyBallBlack.mat | New URP materials |
| Prefabs/Interaction/Basketball.prefab, Prefabs/Player/PrototypePlayer.prefab, Scenes/Prototype/HQPrototype.unity | Narrow migration |
| docs/DESIGN.md, docs/NETWORK_CONTRACT.md, README.md, docs/HQ_PROTOTYPE_TEST_REPORT.md | Decisions/authority/controls/evidence during implementation |

Include Unity-generated .meta files. No package/third-party, DiveSite01,
DiveSiteSettings, oxygen or CI changes as incidental cleanup. The new SyncVar
requires matching updated builds; preserve admission checks.

## 9. Verification

### Pure / asset

- Section 4 values within 0.001; speed non-increasing, positive and bounded;
  meter monotone non-decreasing, <1 at 1000 kg and extreme finite inputs.
  Do not require strict float growth after saturation.
- Invalid/missing settings use documented fallback; shipping validator rejects
  bad content. No NaN/infinity output.
- Throw factors 1/0.40825/0.28868/0.22361; other carried cargo never changes them.
- Full section 3 matrix, malformed TwoHands+fitsInSlot, rollback and fifth
  pickup; no duplicate mass or slot corruption.
- Six fixture identities, positive mass, sizes/uniform scale, shared refs,
  grip points, no missing scripts, registered prefabs and preserved network setup.
- Both setup menu entries, Apply twice, reopen scene, compare semantic diff.

### Runtime: actual host and separate non-host

Use MCP for editor state and typed project helpers returning text; its dynamic
assembly cannot reliably resolve FishNet types. Stop Play before script/asset
edits; refresh/compile, then test. Separate RPC requests and assertions by ticks.
Bounded condition polling (normal Local timeout 5 s) beats arbitrary immediate
reads; abrupt loss uses actual transport timeout, not 5 s.

Record build revision, roles/client/object IDs, settings, mass on **both** peers,
holder/state/writer, expected/actual and timing. Host-only reads do not certify
guest replication. Use existing opt-in command directory to read the standalone.
Missing replies are failures/pending, never passes. Clean up injected input and
temporary items.

| ID | Scenario | Acceptance |
|---|---|---|
| W1 | Guest grabs light, stows, equips, swaps with second light | 0.62 then 1.24; equips do not count twice |
| W2 | Equipped light, free slots, grab blue | Light stowed, blue overflow; both peers 6.62 kg, ~0.7244 factor |
| W3 | Three light slots + black | 21.86 kg/~0.4551; Q -> 1.86/~0.9067; slots unchanged |
| W4 | Heavy grip/controls and old fifth-pickup regression | Heavy never in slot; E/1–4 blocked; Q/Use work; light fifth rule retained |
| W5 | Local/remote hold while moving/pitching | Correct pose/offset, one writer, usable view, no near-plane clipping |
| W6 | Throw each ball empty vs other stored cargo | Initial speeds ~8/3.27/2.31/1.79 within 5%; first-contact distance separately recorded |
| W7 | Guest walks/sprints for 2 s empty vs black | Flat clear lane, input-driven distance ratio within 10% of 0.4728 |
| W8 | Throw and moving catch between peers both directions | Thrower subtracts at release; catcher adds once; no stale impulse/rest or shared writer |
| W9 | Two guests contend for heavy with slot items equipped | Exactly one succeeds; loser keeps old hand/slots/mass. Needs host plus two guests; pending until a three-machine session |
| W10 | Far/blocked/Held/Stowed target, duplicate/stale request | No inventory/mass mutation on refusal |
| W11 | Q/throw near floor/wall/corner/ball/player | Dropped heavy ball rests on the floor outside the capsule; facing a wall it lands on the player's side; throws start at the held pose |
| W12 | Late join into loaded room | Initial totals and pose correct without event replay |
| W13 | Server despawns/resets Held/Stowed | Old carrier loses mass by next dirty flush; no ghost mass/slot after cleanup |
| W14 | Clean leave and killed guest with slots+heavy | Items recovered safely, remaining totals correct, transport detection delay recorded |
| W15 | Host Leave/re-host, exit/re-enter Play | Zero totals, six reset fixtures, no retained callbacks |
| W16 | HUD at both resolutions | Grey, unnumbered, end gap, clear empty/light/heavy difference, menu gate |

The saved scene has only three slot-capable objects. W4's four-slot/fifth
regression spawns **two additional light instances in the test session only**
from registered prefabs on the server, with finally cleanup. Update old H tests
to create needed fixtures rather than search for removed Basketball (4)/(5).

Record initial velocity at assignment in a read-only dev diagnostic; later
FixedUpdate includes gravity/collisions. Measure actual movement via input and
the current controller, focused window/gate, same lane and elapsed time.
ClientMoveLocalPlayerTo is setup, not evidence of slowdown.

Steam: same clean build on two computers/accounts; both roles exercise
W2/W3/W5/W6/W8/W14 with RTT/revision/results. Test latency/loss using available
tooling and record actual profile; otherwise mark pending. Never conflate Local
pass, four-player support or a successful Steam join with these checks.

### Feel gate

A person carries each load over the same lane: light nearly unchanged, heavy
clearly costly but tolerable, dropping responsive, route visible, short throws
useful. Record feedback; math does not prove fun. Tune shared settings/pose only,
rebuild both peers and rerun affected rows. No invented playtest approvals.

## 10. Build sequence and delivery

1. Inspect current branch/diff/main, preserve unrelated work, coordinate HQ/player
   asset edits. Confirm merged hold/catch/fifth-pickup behavior.
2. Prepare implementation PR's design/contract changes listed below. Human network
   review required before merge; never invent approval.
3. Add enum/math/settings/pure checks, compile in Unity 6000.6.0f1. Default enum
   keeps existing one-hand content loadable during migration.
4. Add inventory rules/mass recompute, movement/HUD/grip and radius-aware
   drop/recovery. Preserve the versioned handoff. Keep the old fixture until migration.
5. Add manifest/setup/validator together; migrate through Unity once, Apply twice,
   reopen, preserve existing GUIDs.
6. Update old tests and new hooks. Run pure/asset, Local matrix, input/launch
   measurements and Game screenshots. Inspect source and serialized diffs.
7. Update report with actual results/pending Steam/feel/review. When implementation
   is requested, commit/push/PR using truthful attribution, not hard-coded names.
   No Notion completion during planning or from incomplete evidence.
8. Build shared binaries through existing clean-checkout identity workflow. Never
   discard unrelated settings to force a clean build. Supply the whole matching
   build folder to testers, run Steam rows, get teammate review.

Documentation edits in the implementation PR:

- DESIGN: full stored mass, smooth slowdown, grey meter, heavy hands-only pose and
  auto-stow; six-item harness and provisional tuning. Corpse rules stay separate.
- NETWORK_CONTRACT: heavy cannot stow, new auto-stow transition, carried mass
  server-only from unique Held/Stowed objects. Existing local movement exception
  applies the factor; the releasing writer places a dropped item (radius-aware).
- README: setup/menu, controls/meter, build matching; no claim of animated arms,
  oxygen or two-player carrying.
- Test report: W1–W16 evidence, roles/revisions/RTT and remaining rows/sign-off.
  Loot/UI and oxygen owners review MassKg, Grip, effective FitsInSlot, server
  CarriedMassKg and PlayerHudUI integration.

## 11. Final acceptance

This handoff resolves the technical approach and inventory behavior so the
builder does not need to invent either. All implementation and runtime work is
**unperformed in this planning pass**. Call code implemented after assets/code
and Local checks pass; certify Steam only with specified two-machine evidence.
Finished balance, human review and board completion are separate evidence gates.
