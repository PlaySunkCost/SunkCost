# First-person camera wall clearance — implementation handoff

Status: implemented on `dan/camera-wall` (15 September 2026), based on the
jump/crouch/hands merge (`61214e7`). Evidence and the acceptance rows run are in
`docs/HQ_PROTOTYPE_TEST_REPORT.md` ("Camera wall clearance branch"). The plan was
written on `codex/camera-wall-plan` from `0bb99c29f79fcd229a4abf033b3d34cad56e1a9c`.

## 1. Requested outcome

When a player walks against a wall, rotates into a corner, looks up/down or
crouches under an obstacle, they cannot see the room through solid geometry.
Preserve normal first-person movement and camera control without visible jitter.
This task fixes camera visibility, not the whole character animation system.

Read [DESIGN](DESIGN.md), [NETWORK_CONTRACT](NETWORK_CONTRACT.md),
[CONVENTIONS](CONVENTIONS.md) and [WORKFLOW](WORKFLOW.md). The jump/crouch/hands
plan is on `codex/player-movement-hands-plan`; integrate with it if merged, but
this branch must be implementable against main without importing its feature.

## 2. Evidence and required reproduction

Inspected `Assets/_Project/Prefabs/Player/PrototypePlayer.prefab` has a camera
near clip plane of **0.3 m**, vertical FOV 75, far clip 1000. ViewPivot is at
1.6 m above the root; camera local position is zero. The CharacterController
radius is 0.3 m. `HQPrototypeBuilder` sets FOV but does not explicitly set near
clip, so regeneration can restore a bad default. `HQPlayerController` writes
pitch and exposes EyePosition from the camera; inventory validates LOS from it.

These are possible contributors, not a proven runtime diagnosis. Before edits,
reproduce at a flat wall and corner and record camera position, clip distance,
capsule/skin bounds, FOV/aspect and the affected renderer/collider. Distinguish:

- The camera center is inside the wall (bad transform/clearance).
- The camera is outside but its near clipping plane cuts through the wall.
- A wall has missing/misaligned collision or one-sided visual geometry.
- Only held hands/items render incorrectly; that is a different visual problem.

Do not solve missing wall collision by masking the whole screen permanently or
making every world material double-sided. Fix confirmed asset errors narrowly
with scene/prefab owner coordination and document them.

## 3. Camera dimensions and defaults

Explicitly set the player camera near clip to **0.05 m** in its settings and
builder. Keep current FOV/far clip initially and inspect depth artifacts at sea;
do not change every camera or globally alter URP to conceal the symptom.

Derive clearance from actual near-plane corners, not only a forward ray:
halfHeight = near * tan(verticalFOV/2); halfWidth = halfHeight * aspect.
A conservative sphere around the eye containing the near plane has radius
sqrt(near^2 + halfHeight^2 + halfWidth^2), plus 0.01 m margin. At 75 degrees,
16:9 and 0.05 m near, the radius before margin is approximately 0.093 m.
Use CalculateFrustumCorners or equivalent for a nonstandard projection.

Settings: 0.05 m near, 0.01 m margin, maximum corrective eye displacement 0.15 m,
return blend 0.10 s, bounded overlap buffer and maximum 4 resolution iterations.
Validate finite values, positive dimensions, and a feasible envelope within the
standing/crouched body. Recompute when FOV, aspect, stance or projection changes.
Extreme unsupported settings must warn/fail validation rather than quietly allow
clipping. These are tunable initial values, not guarantees for arbitrary cameras.

## 4. Clearance solver

1. Separate desired eye pose from corrected render pose. Sample the desired pose
   after movement, look and stance camera blending. Do not feed last frame's
   correction back into the desired pose and accumulate camera drift.
2. Use the stance-aware neutral eye inside the actual movement capsule as the
   initial reference. Test initial overlap as well as sweeping from reference
   to desired pose; a cast starting overlapped may miss the obstruction.
3. Check the near-plane envelope against solid world/door/elevator geometry.
   Ignore triggers, self, hand meshes and carryable layers. Do not accidentally
   remove all world collisions because a held item was ignored. Render geometry
   without appropriate collision is an asset-validation finding.
4. If the desired eye is clear, use it. Otherwise resolve a bounded displacement
   toward the last safe pose/reference using shape sweeps and penetration tests.
   Require the entire corrected envelope to be clear. Never push the view through
   the far side of a thin wall to find empty space.
5. Clamp movement correction inside the player's accepted stance envelope and
   the 0.15 m maximum. Do not move the player root, server transform or item to
   resolve a cosmetic camera collision. Inward safety correction is immediate;
   only return toward desired pose is smoothed and that path is checked too.
6. If no valid local pose exists (spawn embedded in geometry, closing door,
   query overflow), use a fully opaque local obstruction cover before rendering
   the unsafe world view; clear aim target and block local interaction until a
   safe view is restored. This is a last-resort fallback, not normal wall contact.
   Record diagnostics and let existing gameplay deal with the body obstruction.
7. On teleport/load, discard the previous world's last-safe point, solve from
   the new capsule and keep the existing travel fade until safe. Never blend
   across worlds or restore an old camera point behind a wall.

Use fixed/preallocated queries and fail closed on buffer overflow. Test slopes,
acute corners, thin walls and moving ceilings. No unbounded iterative depenetration.
If reducing near clip and maintaining the capsule-contained eye solves ordinary
contact, do not add unnecessary camera motion in those cases.

## 5. Frame order, hands and interaction

Current controller does Look -> Move -> UpdateTarget in Update. Arrange explicit
ordering: sample input; move/stance; determine desired eye; solve corrected eye;
then compute target ray and sample item action. Render must use that same final
pose. Do not leave targeting on yesterday's corrected pose while LateUpdate
draws a different view. Hand/item presentation follows the finalized view/hold
anchor afterward, retaining the existing single item writer.

Expose desired gameplay eye and corrected visual eye separately. Owner crosshair
ray starts at corrected eye. Server interaction range/LOS must remain bounded
by its authoritative stance-aware eye; never trust an arbitrary client camera
origin. For an optional submitted correction, validate it within the permitted
envelope and require clear server LOS from canonical eye to corrected eye and
target. A small false refusal is safer than granting through-wall interaction.
No per-frame camera-position SyncVar or camera authority transfer is needed.

If an interaction request changes, apply the human network-review rule. Avoid
changing RPC shape unless needed: retain conservative canonical server LOS where
it already prevents through-wall actions. Test both successful legitimate edge
targets and rejected targets on the other side of walls.

Future hands remain connected and use world depth; do not add an always-on-top
camera that renders an item through walls. The hands branch owns centered grip,
safe forward release and cargo contact rules. Camera clearance must not undo its
downward throw clamp or create a second item pose writer. Its crouch camera blend
must pass through this clearance solver, not independently write final position.

## 6. File map and setup

All paths below are under `Assets/_Project/`.

| File | Responsibility |
|---|---|
| Scripts/Player/PlayerCameraClearance.cs | Desired/final eye, bounded queries, lifecycle/reset |
| Scripts/Player/PlayerCameraSettings.cs | Clip/clearance tuning and validation |
| Scripts/Player/HQPlayerController.cs | Explicit frame ordering, final targeting and interaction block |
| Scripts/Player/PlayerHudUI.cs | Obstruction cover and aim suppression if no existing fade can safely serve |
| Settings/Prototype/PlayerCameraSettings.asset | Shared tunable defaults |
| Prefabs/Player/PrototypePlayer.prefab | Camera settings/component references |
| Editor/Prototype/HQPrototypeBuilder.cs | Explicit near plane; preserve settings after regeneration |
| Editor/Prototype/PlayerCameraClearanceSetup.cs | Targeted idempotent prefab patch and validator |
| Editor/Prototype/PlayerCameraClearanceChecks.cs | Envelope math and reproducible corner fixtures |
| Scripts/Interaction/PlayerInventory.cs | Only if current server eye/LOS requires integration |

Keep SunkCost.Player namespace and current assembly conventions. No external
package required. Coordinate prefab/scene edits with owners, preserve GUIDs and
do not rebuild unrelated HQ content. Use a temporary test scene for fixtures.
Audit any new camera/stance code on main before implementation to avoid duplicate
settings/components when the hands branch lands first. Only the owner runs view
correction; remote avatar pose and host movement remain unchanged.

## 7. Implementation order and acceptance

1. Reproduce and identify renderer/collider/camera cause; capture before evidence.
2. Explicit near-clip setting and regression-proof setup; inspect visual result.
3. Add clearance/ordering/fallback only to meet remaining geometric cases above.
4. Integrate eye/target checks and hands/crouch seams; test host plus separate client.
5. Validate prefab regeneration, document results and open implementation PR.

| Test | Expected result |
|---|---|
| Flat wall, head-on and sideways | No seeing through wall, no unnecessary camera motion |
| Acute/right-angle corner, thin wall, door frame | Near-plane edges remain safe; no far-side correction |
| Look pitch extremes and fast turn | No single-frame room exposure or view drift |
| Low ceiling / crouch enter and stand | Camera stays in safe accepted envelope throughout blend |
| Closing door / moving cabin geometry | Immediate safe correction or cover; no root movement from camera solver |
| 16:9, 21:9, resized window and supported FOV limits | Envelope recalculated; no edge peeking |
| Target across wall vs accessible near corner | No through-wall grab/button use; normal nearby targeting works |
| Every held item, throw/drop toward wall | Depth respected; release safety remains owned by hands feature |
| World transfer / reconnect / focus / menu | No old last-safe point, stuck cover, duplicate listener or stale target |
| Host plus non-host client | Local view correction does not move remote roots or change authority |
| 30/60/120 FPS and fast movement | No visible jitter or one-frame leak; bounded query cost |
| Setup twice / save / reopen | Correct near plane and one component, same GUIDs |
| Ship ocean distance | No unacceptable new depth flicker from the smaller near plane |

Record exact build, roles, clip/FOV/aspect, geometry, frame conditions and result.
Use Unity MCP if actually connected, plus Game-view captures/video of movement;
logs alone cannot prove a see-through rendering bug is fixed. Run compilation,
envelope math checks and relevant runtime tests; mark crouch integration pending
if that feature is not yet built. Do not call unrun multiplayer rows passed.

## 8. Delivery

Update README/test report with the fix and actual evidence. Add contract text
only if eye validation/network requests change; teammate review then required.
Planning is complete when this document is committed and pushed on its own branch.
Implementation is complete only after the applicable acceptance cases above pass.
