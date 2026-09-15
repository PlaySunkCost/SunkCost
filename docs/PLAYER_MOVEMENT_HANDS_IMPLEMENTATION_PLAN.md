# Player jump, crouch and connected holding hands

**Status: implemented on `dan/player-movement-hands` (15 September 2026); Local
rows M1–M9 verified, see the test report.** Deviations from the plan below:
the player's visible body is Dor's 1.19 m placeholder model, not the capsule
the plan assumed — on friends' screens the arms meet the held item above its
head, and the owner's own body is hidden (the arms are not). The visible-body
card owns the proportions. The one-second release expiry runs on the owner
(an accepted release that never became applicable is cancelled), not on the
server. Setup twice, prefab reopened: no duplicates.

**Status: implementation handoff, documentation only.** Written 15 September
2026 on `codex/player-movement-hands-plan`, created from fetched `origin/main`
at `0bb99c2`. No movement code, meshes, prefabs or scenes changed in this task.
Check newer main before implementation. Read [DESIGN](DESIGN.md),
[NETWORK_CONTRACT](NETWORK_CONTRACT.md), [CONVENTIONS](CONVENTIONS.md) and
[WORKFLOW](WORKFLOW.md). User decisions below supersede walking-only wording for
this new feature; update the canonical documents during implementation.

## 1. Confirmed decisions and scope

Confirmed directly with the user:

- Add a modest normal jump, not a parkour jump.
- **Hold Ctrl** to crouch. Crouch reduces actual collision height and speed.
- Carried weight reduces jump height. **No jump with a two-handed item held.**
- Remove the rectangle in front of the avatar.
- Keep and improve the center aiming dot so players can see where grabs and
  throws are aimed (user addition, 15 September 2026).
- Simple gloved hands and connected forearms, visible to the owner and friends.
  **Every held item uses both hands and sits centrally in front of the body**
  (user revision, 15 September 2026). This supersedes the earlier one-hand pose
  for small items. Small items remain slot-able; existing heavy-item restrictions
  and mass-based movement/throw rules remain unchanged.
- Drop/throw starts in clear space in front, including when looking down. Never
  materialize a released item inside any player or directly below the releaser.
  Prevent released cargo from lifting/supporting players (section 6A).
- Hands visibly grip the item, inspired by the supplied image, but **no floating
  or detached hands**. Do not copy the reference game's art or textures.

Planner defaults below are deliberately tunable. No further architecture or
control decision is required before coding. Human visual/feel review remains
necessary before calling the result polished.

Excluded: mantle, vault, wall jump, double jump, slide, prone/crawl stance,
swimming, stamina, fall damage, footsteps/landing noise, stealth bonuses, melee,
full locomotion animation overhaul, VR interaction, physics hands, individual
networked fingers, purchasable art or a new character replacement. Crouching is
not automatically quieter to monsters: no hearing rule has been approved here.

## 2. Verified baseline and research

Current HQPlayerController owns CharacterController movement: walk 4 m/s,
sprint 6 m/s, gravity, camera look, interaction input. Capsule height 1.8 m,
radius 0.3 m, center y=0.9, stepOffset=0.25, skinWidth=0.03. ViewPivot height
is 1.6 m. There is no jump or crouch. Early returns for focus/menu currently
skip gravity too; jumping must not let an airborne player hang there indefinitely.

The rectangle is the prefab child **ForwardMarker**, created by
HQPrototypeBuilder. Remove that child and its builder creation, not Body,
HoldPoint, TwoHandHoldPoint or networking components.

CarryableItem already has Grip, TryGetHoldPose, and an owner-written rigid held
pose. Hands must follow this pose, not replace its physics/ownership system.
Update hold anchors/offsets to the new centered presentation without changing
the semantic CarryGrip classification used by inventory and heavy jump rules.
The current player prefab contains the prototype Body/marker, with no Animator
or mapped hand rig. GenericCharacter.blend exists but its importer has empty
human/skeleton mappings; file existence does not prove a usable humanoid rig.
This plan therefore specifies simple project-owned arm/glove geometry.

Current weight design has changed since early drafts: linear slowdown, red
overload meter and 10% crawl, and storing small items into free slots while a
heavy is held. Preserve current behavior; do not restore the older exponential
weight curve or blanket E-block rule from historical plans.

Research checked 15 September 2026:

- Valve's official Half-Life 2 multiplayer source defines standing hull height
  72 and crouched height 36, with unchanged horizontal bounds: a 50% height.
  [Valve source](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/shared/hl2mp/hl2mp_gamerules.cpp).
- Minecraft Bedrock documents a 1.5-block sneaking hitbox and passage through
  1.5-block gaps. There is no universal percentage across games.
  [Official release notes](https://feedback.minecraft.net/hc/en-us/articles/17381681401357-Minecraft-1-20-10-Bedrock).
- Unity exposes capsule height and center separately; changing center does not
  move the transform pivot. Step offset must not exceed controller height.
  [Unity CharacterController reference](https://docs.unity3d.com/6000.0/Documentation/Manual/class-CharacterController.html).

Recommendation: **1.8 m standing -> 1.0 m crouched** (about 44% shorter, close to
the requested half height), keeping radius 0.3 m. This is collision height, not
uniformly shrinking the player or promising an exact damage hitbox. Existing
and future hurtboxes must read the same stance dimensions when integrated.

## 3. Control and tuning table

Store values in a shared PlayerMovementSettings asset under
`Assets/_Project/Settings/Prototype/`, referenced by the player prefab.
Preserve existing walk/sprint/weight settings as their current source of truth.

Validate finite positive heights/speeds, crouch height >= twice capsule radius,
eye heights inside the corresponding capsule, crouch height below standing,
step offsets below height, nonnegative blend/buffer times and jump factor in
(0,1]. Use one documented default set for missing settings and fail the asset
validator for missing/invalid shipping references; never let NaN reach movement.

| Setting | Starting value / behavior |
|---|---|
| Jump key | Space, one jump per press; holding does not auto-hop |
| Crouch key | Left Ctrl, held; expose key fields for later rebinding |
| Base jump height | 0.65 m above takeoff feet |
| Gravity | Existing Physics.gravity.y, negative; do not change project gravity |
| Coyote time | 0.10 s after walking off a supported edge |
| Jump buffer | 0.10 s before landing; cleared by locks/refused heavy jump |
| Air control | Existing planar steering; no accumulating acceleration or bunny-hop boost |
| Weight jump floor | 0.40 of base height; no two-handed jump |
| Crouch height | 1.0 m, radius unchanged |
| Crouch eye height | 0.85 m above feet |
| Crouch camera blend | 0.15 s, no FOV/head-bob changes |
| Crouch speed | 50% of walking speed, then existing weight factor |
| Sprint while crouched | Ignored; does not force standing |
| Crouch step offset | 0.15 m; standing retains 0.25 m |
| Jump while crouched | Refused; release Ctrl, safely stand, then press Space |
| Midair crouch | Latch desired stance, apply on landing; no capsule shrinking in flight |

No new overload-specific jump ban: if not holding a two-handed item, weight
reduces jump down to the configured floor, even while overloaded. This follows
the user's accepted rule; do not silently add a second ban.

`jumpHeight = baseJumpHeight * Lerp(1, minJumpHeightFactor,
 Clamp01(CarriedMassKg / Weight.CapacityKg))`.

Use the existing authoritative mass/capacity, not the discontinuous overload
speed factor. Defaults: 0 kg -> 0.65 m, 12.5 kg -> 0.455 m, >=25 kg -> 0.26 m.
Takeoff velocity `sqrt(2 * abs(gravityY) * jumpHeight)`; empty ~3.57 m/s at
9.81 m/s². Sample at takeoff; do not change velocity mid-flight if cargo changes.
These are theoretical targets; measure actual CharacterController trajectories.

Crouch speed = walkSpeed * 0.5 * existing SpeedFactor: empty 2 m/s, overloaded
0.2 m/s with the current 0.1 factor. This intentionally stacks the two costs;
record feel feedback before changing the current weight rule.

## 4. Movement and collision implementation

Keep CharacterController and the owner's NetworkTransform. Do not add a dynamic
Rigidbody, root-motion movement or a second transform writer.

### Jump

Refactor the controller's movement update into input sampling, stance evaluation,
ground/jump state, velocity integration and one CharacterController.Move call.
Input gates block commands, not gravity. A travel/cinematic lock is different:
the departure follower may own root placement and must suppress this motor.

- Grounded means supported below, not a wall hit; use CollisionFlags.Below and
  a small ground probe consistent with capsule radius/skin. Exclude self/held
  visual geometry and triggers. Steep unwalkable slopes are not jump support.
- Press Space sets a short buffer; consume once on grounded/coyote eligibility.
  Clear coyote eligibility after takeoff; no repeated midair jump or infinite
  jumps when holding Space. Require fresh press after refusal/transition.
- Reject when crouched, holding TwoHands, travel-locked or input-disabled.
  Resolve held grip from actual replicated item state, not slot selection alone.
- If upward CollisionFlags.Above occurs, zero upward velocity immediately.
  Landing resets downward velocity to the existing small grounding value.
- Set stepOffset=0 in flight to avoid stepping up tall obstacles midair; restore
  stance-specific offset on landing. Cap pathological frame delta/substep motor
  updates as appropriate; never perform an unbounded catch-up loop.
- Dropping/picking up items midair does not retroactively alter takeoff. No
  jump/landing gameplay noise or damage event in this feature.

### Crouch

Keep foot transform fixed. Standing center=(0,0.9,0), crouch center=(0,0.5,0);
height changes, radius does not. Do not scale the root: that would resize items,
network transforms and hand anchors. Ensure height >= 2*radius plus tolerance.

Entering crouch changes collision dimensions immediately; camera/body blend is
presentation only. For standing, test the **additional headroom volume** using
the prospective capsule, excluding the player itself, carried disabled colliders
and triggers but respecting walls/ceilings/other solid objects. Ground contact
must not falsely block standing. Include a small clearance tolerance and fail
closed if a nonalloc query buffer fills.

Releasing Ctrl under an obstruction keeps the player crouched; retry locally
while stand intent remains and space changes. Do not send a stand request every
frame. Never force standing on Resume, scene transfer or re-host inside geometry.
Differentiate DesiredCrouch from actual IsCrouched for this reason.

Interpolate eye/visible body only within the accepted capsule envelope so a
shrinking camera cannot look through a ceiling during the blend. Update the
server's stance-dependent interaction eye too; the remote disabled camera's
default standing height must not be used for range/LOS while crouched.
Recheck low-camera throw/drop placement, including heavy items, near the floor.

Current prototype body can adopt a fitted lower capsule silhouette; this is
temporary crouch presentation, not a humanoid animation. Preserve appearance
and feet contact. Future hurtbox/monster queries must consume stance bounds,
not a separate standing-height constant.

## 5. Multiplayer stance without a movement rewrite

Player displacement/jump remain the contract's owner-simulated movement
exception; no per-frame movement RPC and no claim of new speed anti-cheat.
Observers see jump through NetworkTransform; no jump event replay required.

Add `PlayerStance : NetworkBehaviour` with a server-written persistent posture
snapshot (IsCrouched plus acknowledged request serial). Owner sends bounded
stance-change intent through an ownership-required ServerRpc. Server validates
sender/player, current travel state, and stand clearance at server-observed
position. It never accepts client capsule dimensions or eye heights.

For responsiveness, predict **shrinking** locally on Ctrl. Standing waits for
server acceptance and still rechecks local headroom before expanding. A rejected
stand leaves crouch intact. Acknowledgement serial must advance on refusal too,
so an unchanged bool cannot leave a pending request forever. Retry when clearance
changes, with a bounded cooldown (0.2 s), not every frame. Ignore stale replies.
While waiting, use crouch speed and eye on the owner. If local space becomes
blocked after server accepted stand, keep local crouch and promptly request it
again; never expand into geometry merely to match the server snapshot.

On server/remote peers apply accepted posture to relevant collision shape and
visual body; do not enable a second movement simulation. On the owner do not
blindly overwrite safe predicted posture inside a ceiling. Initialize from
snapshot on spawn/late join. Use one dimensions helper for controller, server
clearance, eye-height and diagnostics. Match build/settings on all peers.

Document the brief positional/stance reconciliation window inherent in existing
client movement. This feature does not establish competitive hit registration
or new damage rules. All stance RPC/SyncVar changes require teammate review.

## 6. Hands that visibly hold the actual object

### Geometry and ownership

Create project-owned simple glove/arm geometry: recognizable palm, thumb and
curled fingers, cuff, connected forearm and upper-arm segment. Low-poly, plain
material; no detached palms and no floating cubes accepted as final visuals.
Create with an editor mesh builder or an authored project mesh, with stable
assets/materials and .meta files. No third-party purchase/import required.

Add torso/shoulder anchors to the player visual hierarchy and a `PlayerHands`
component. Both owner and remote views render arms connected back to the body;
arm geometry has no gameplay Collider/Rigidbody/NetworkObject. Preserve root
dimensions. Validate local near-plane clipping with the existing camera before
adding any special owner-only renderer. Do not create a duplicate held ball or
force hand meshes to draw through walls.

Remove only ForwardMarker from the player prefab and its builder. Search all
validators/builders/scene overrides so it cannot return on regeneration. Do not
remove a newer real body rig if main changes before implementation.

### Authored grip targets

Add a non-networked `ItemHandPose` on each carryable prefab, with right/left
grip target transforms and wrist orientation, plus a small named finger pose
(Relaxed, BallSmall, BallLarge initially). Targets are authored in **item-local
space**, scaled with the item's actual mesh. Every visual pose now uses both
hands. Existing CarryGrip remains the gameplay classification: blue balls and
basketballs are still slot-able; purple/black remain heavy TwoHands cargo.
Do not set every prefab's Grip to TwoHands: that would wrongly block small-item
storage and jumping. Visual hand count must be independent of inventory rules.

- All held items: centered item anchor, palms on left/right support surfaces,
  fingers wrap, thumbs readable. Small objects use closer wrist targets; large
  objects use wider/lower support poses. Never put palms inside the object.
- Empty: relaxed hands beside/in front of torso without blocking the crosshair.
- Stowed: no hand contact; use currently Held item only. Silent storage while
  holding another item must not redirect either hand.

Implement a simple two-segment arm solver (or equivalent authored poses) from
shoulder to wrist with elbow hint and fixed segment lengths. Wrist targets are
computed from the rendered item's grip transforms **after** its hold/replication
pose update. Clamp impossible reach and report bad assets; never stretch forever
or move the authoritative item to satisfy a hand. Tune item grip/hold offsets
within comfortable reach on all fixture sizes and +/-80-degree look pitch.

Hands follow items; they do not write item position, ownership or physics.
Owner uses actual local held item. Remote view resolves the unique Held item
whose HolderClientId matches that player; host-side HeldItem for remote players
is not populated as an owner view. React to replicated state/lifetime changes,
with safe rescan on spawn/late join, no per-frame world scan. Remote arms follow
received item transforms, not an unreplicated copy of camera pitch.

Use ~0.1 s visual blend on grab/equip and return-to-rest on release; first valid
pose on late join snaps directly. Release the hand target when state stops Held
so hands do not chase thrown balls. A fast re-catch cancels the previous blend;
item motionVersion/ownership logic remains unchanged. Do not delay gameplay
requests until an animation finishes. Fingers/IK are cosmetic and unnetworked.

### Visual acceptance

Inspect owner and remote screenshots/video for every item: no gap between palm
and grip surface, no fingers through ball center, connected cuffs/arms, plausible
elbow bend, no large obstruction to the center view, no duplicate arms/items,
stable crouch/jump/turn/swap/throw/catch. Glove color is tunable; no requirement
to copy the screenshot's yellow. Asset appearance needs human feedback.

### Center aiming dot

PlayerHudUI already draws a 4-pixel white square at screen center and colors it
yellow when CurrentTarget or CurrentButton exists. Improve this existing element;
do not add a second crosshair or a world-space marker in front of the avatar.
ForwardMarker removal must not remove this HUD dot.

- Small white center dot, approximately 4 px across at 1080p, with a 1 px dark
  outline for contrast against the ocean, bright walls and held objects. Diameter,
  outline and colors are serialized HUD settings; use consistent HUD scaling and
  keep it centered when resolution/aspect ratio changes. No bloom or animation.
- Soft gold when the current target is locally usable: a Free/Released item
  within existing targeting/LOS rules and CanStoreOrHold, or an available monitor
  button under the current phase rules. Full hands or unavailable controls do
  not get a success-looking highlight. The server still decides acceptance.
- Reuse CurrentTarget/CurrentButton and existing prompt eligibility; no new
  physics query, increased reach or alternate targeting system. Keep the prompt
  text as feedback; color alone must not communicate a refusal.
- Visible for the local player while normal gameplay input is active, including
  holding, crouching and jumping. Hide for menus, Steam overlay/focus loss,
  loading fades and travel/cinematic input locks; preserve the original menu state
  when restoring it. No reticle drawn for remote avatars.
- Hands should leave the center view readable. The dot indicates camera aim,
  not a guarantee that a thrown ball reaches that point: gravity, safe release
  offset, the downward pitch clamp and collisions still apply. Do not add
  trajectory prediction or target-seeking assistance.

Verify empty hands and every grip on bright/dark backgrounds at 1280x720 and
1920x1080, plus window resize. Confirm target highlight, unusable-target neutral
state, prompt consistency and all hide/restore cases. This is planned HUD polish;
the existing dot's presence does not count as completion of these checks.

## 6A. Safe forward release and no cargo-standing exploit

User revision, 15 September 2026. Q places an item down in front; left click
throws it forward. Looking down must not put it under the player, launch the
player upwards, or leave the player standing on their dropped basketball.
Interpret 'can't be thrown on a player' as no placement inside/on their body
and no physical pushing/support from carryables; normal throws toward friends
and their ability to catch a moving item must continue to work.

### Placement and aiming

- Share a `ReleasePlacement` helper between owner prediction and server checks.
  Resolve the actual controller capsule, stance and item collision envelope;
  never hardcode the 0.3 m player radius or assume every future item is a sphere.
  For the current balls use sphere casts; author a conservative shape/envelope
  for other prefabs and validate it against all enabled physical colliders.
- Horizontal forward is camera forward projected onto the ground plane, falling
  back to player yaw near vertical view. Both Q and throw spawn use this direction.
  Keep the complete item behind no part of the player's forward clearance plane:
  minimum horizontal center distance = capsule radius + item forward extent +
  0.10 m clearance. Also reject overlap with every other player's capsule.
- Q ignores view pitch: find reachable free space ahead, then sweep down to the
  nearest supporting surface. Place above it with 0.05 m skin and zero initial
  launch velocity. Do not require a ground surface: over an edge it drops from
  the clear forward placement point and falls naturally. Do not snap to a lower
  floor across an arbitrarily deep shaft. Limit ground search to 1 m initially.
- Throw direction retains horizontal aim and upward aim; clamp downward launch
  pitch to 0 degrees (horizontal) initially. Thus looking at your feet throws
  forward, then gravity pulls the item down. Store min/max pitch as tuning; keep
  existing upward limit and item mass-dependent launch speed. Do not add a target
  lock, character hit impulse or a new damage system.
- Throw starts around chest height, stance-aware, outside the player capsule;
  floor/ceiling clearance may adjust height within a bounded corridor. All
  candidate poses remain within 2 m forward of the player, never behind them.
  If a large item cannot fit within that range, reject rather than extend reach.
- Check BOTH endpoint overlap and swept path from the player's safe interaction
  origin to candidate (ignore self, carried item and triggers). A clear endpoint
  beyond a wall is not sufficient. Do not use an already wall-clipped held pose
  as the sole sweep origin. Item/geometry overlap queries fail closed on overflow.
- Search a bounded set of closer/higher forward candidates, then refuse with
  'Not enough room to drop/throw'. Keep Held state, slot mapping, mass, ownership
  and hands unchanged on refusal. No fallback to feet, behind the player, through
  a friend/wall, or to an unchecked current hold pose.

### Prevent pushing/standing on cargo

Use a dedicated collision relationship between player movement capsules and
carryable physics shapes: players do not physically collide with carryables in
Free/Released states. This prevents self-launch, standing on balls and pushing
other players with thrown cargo, including after a ball settles. Carryables still
collide with world geometry and each other. Catch/grab queries must explicitly
include their layer; disabling contact must not make objects unselectable.
Document that players can walk through loose cargo in this prototype. This is
a scoped solution to the requested interaction, not a blanket removal of all
player/object collisions: doors, walls and future dedicated hazards remain solid.
Future corpse/hazard systems need their own collision rules rather than blindly
inheriting the carryable layer. Do not add a timer that reenables collision while
a player overlaps a ball; permanent pair/layer policy is more predictable here.

Use scoped project layers or a centralized spawn-safe collision policy after
auditing existing layers. Apply to every peer and newly spawned/late-joined
player/carryable; do not edit unrelated layer pairs. Ground probes explicitly
exclude carryables so the new jumping motor cannot treat an ignored ball as floor.
Players still obstruct release placement despite not physically colliding with
loose cargo. Catch assistance and server LOS remain unchanged.

### Release transaction and network ordering

Existing ServerRelease changes state before the owner computes a drop position.
Replace this with a versioned release transaction: server validates sender,
Held state, current motion version and a bounded proposed pose/direction against
its observed player and geometry before accepting. No client writes SyncVars.
Use an owner confirmation phase if local geometry has changed: while awaiting
confirmation keep item Held and reserve that release serial; owner validates
the approved pose locally and confirms or cancels. Server rechecks before commit.
Expire pending requests after 1 s (tunable), refusing safely with item still Held.
Commit Held->Released, slot clearing and mass update together, only on acceptance.

Deliver approved pose/direction/version to the existing releasing writer and
wait for matching replicated state just as TryApplyPendingRelease already does.
Apply placement before enabling colliders/gravity and impulse, exactly once.
If geometry changes after commit, the current writer suppresses activation and
reports failure for that version; server restores Held/slot atomically while the
same request is still current. Never rollback a later catch/stow/disconnect.
Keep one in-flight release per player; reject equip/grab while reserved. Preserve
the existing Released rest/handoff and moving-catch motionVersion protections.
Human netcode review must inspect this rollback path and RPC/SyncVar ordering.

Do not apply deliberate-drop placement to disconnect recovery without review:
disconnect has no owner to confirm and must retain server ownership/drop recovery.
Add a server-only safe recovery search separately if its current points overlap
world geometry; no human/disconnected inventory may vanish on failed placement.

### Extra files and validation

Add `Scripts/Interaction/ReleasePlacement.cs`, release tuning in shared settings,
and `Scripts/Interaction/CarryableCollisionPolicy.cs` if layer setup alone is not
sufficient. Update CarryableItem, PlayerInventory, controller centered anchors,
item hold offsets, setup/validator and relevant ProjectSettings collision layers.
They are behavioral changes, not merely read-only detail additions.

Test Q/click looking level, straight down and up; standing/crouched/airborne;
every item size; wall/corner/low ceiling; sloped floor/stairs/ledge; friend directly
ahead; simultaneous release/catch; delayed acceptance and refusal; disconnect
in every pending phase. Repeat after Free handoff. Player height must not jump
or become supported by cargo, even when walking over a settled ball. Verify
catching a friend's moving throw still works. Check remote and host observers,
slot/overflow preservation, no duplicate impulses and bounded request retries.

## 7. Integration boundaries

- Preserve current weight/overload, fifth pickup, hands-only silent storage,
  reach/LOS, single-writer holding, catching, monitor controls and world travel.
  Centered pose, forward release and cargo/player contact are deliberate changes
  specified above, not regressions to preserve from the old prototype.
- The separate ship-departure plan is **not implemented on this main**. Define
  a small movement lock interface now so future travel can block Jump/Crouch/
  Move without blocking scripted following or permitted look; don't implement
  the departure here. Existing active travel must block new jump/stance intent.
- At world placement reset vertical velocity and buffered jump; preserve crouch
  until safe to stand. Never replay pre-load inputs. Do not let jumping from the
  deck bypass everyone-aboard checks or teleport an off-deck avatar aboard.
- Current DiveSiteDevPlayer is a separate local harness. Apply this feature to
  the networked PrototypePlayer wherever it is used. Do not duplicate netcode or
  silently claim the dev harness/elevator now has multiplayer movement support.
- Menu/focus disables command input; ordinary gravity continues unless a travel
  follower intentionally controls the root. Resume never causes a buffered jump.
- No new fall damage or monster stealth/noise outcomes; report map exploits found
  by the new jump separately from changing game rules.

## 8. File and asset map

Paths under `Assets/_Project/` unless stated otherwise.

| File | Planned responsibility |
|---|---|
| Scripts/Player/PlayerMovementSettings.cs | Jump/crouch tuning, validation, shared defaults |
| Scripts/Player/PlayerMovementMath.cs | Pure jump-height/velocity and capsule dimensions |
| Scripts/Player/PlayerStance.cs | Requested/accepted stance, clearance, persistent server posture |
| Scripts/Player/HQPlayerController.cs | Jump/gravity/input split, stance speed/eye, transition reset |
| Scripts/Player/PlayerHands.cs | Held-item binding, arm/finger presentation and lifecycle |
| Scripts/Player/PlayerHudUI.cs | Improve existing center dot, shared prompt/highlight eligibility and visibility gates |
| Scripts/Player/ArmPoseSolver.cs | Small two-segment solver; no item/physics writes |
| Scripts/Interaction/ItemHandPose.cs | Prefab-local grip targets and finger-pose settings |
| Scripts/Interaction/CarryableItem.cs / PlayerInventory.cs | Centered hold, validated release transaction and held-state notifications; preserve single-writer ownership |
| Editor/Prototype/PlayerMovementHandsSetup.cs | Targeted repeatable setup and project-owned glove/arm asset generation |
| Editor/Prototype/PlayerMovementHandsChecks.cs | Math/clearance/asset invariants |
| Editor/Prototype/PlayerMovementHandsRuntimeChecks.cs | Real-input and separate-client checks |
| Editor/Prototype/HQPrototypeBuilder.cs, HQPrototypeValidator.cs, HQPrototypeTestHooks.cs | No marker regeneration; new required refs and diagnostics |
| Scripts/Net/InventoryVerificationPeer.cs | Opt-in read-only stance/jump/grip evidence on standalone peer |
| Settings/Prototype/PlayerMovementSettings.asset | Shared tuning |
| Art/Prototype/Models/PlayerArms.asset, Materials/PlayerGloves.mat | Original reusable prototype geometry/material |
| Prefabs/Player/PrototypePlayer.prefab and current carryable prefabs | Components, shoulder/elbow/grip anchors, remove ForwardMarker |
| docs/DESIGN.md, NETWORK_CONTRACT.md, README.md, HQ_PROTOTYPE_TEST_REPORT.md | Accepted movement/stance rules, controls and actual evidence |

Read current file names before editing; preserve all .meta GUIDs and scene
overrides. No new package or character import is required. Setup must load and
patch existing prefabs, not rebuild the room/player from scratch. Apply twice,
save/reopen and verify no duplicate arm targets/components or regenerated marker.
Preserve other uncommitted documents and the unrelated SteamManager meta file.

## 9. Build order and test gates

1. Recheck current main, player prefab and design changes. Coordinate player and
   carryable asset edits with owners. Confirm this plan's user choices.
2. Add settings/math and controller jump; test grounded, ceiling and weight rules.
3. Add safe crouch dimensions/camera and stance networking; test low passages
   with a separate client before spending time polishing the hands.
4. Remove ForwardMarker via targeted setup; add arm/glove geometry and item
   targets. Verify centered two-hand poses against every current prefab without
   changing the small/heavy inventory classifications. Implement section 6A
   release placement and collision policy with separate-client checks.
5. Add lifecycle/travel/menu integration and regression tests. Update docs with
   actual results and pending Steam/visual review, not expected results as passes.
6. When implementation is requested, commit/push/PR with teammate network review.
   This planning task creates the document only, no gameplay or board changes.

| Test | Expected evidence |
|---|---|
| Empty jump, 30/60/120 FPS | Feet apex ~0.65 m (+/-0.05), one jump per press, no air jump |
| Weight samples / two-handed | ~0.455 m at half capacity, ~0.26 at full; TwoHands blocks jump, storing small items unchanged |
| Coyote/buffer/ceiling/slope/stairs | Short forgiveness works once; head hit cancels ascent; no tall-step exploit |
| Crouch collider | 1.8 -> 1.0 m, same radius/feet; can traverse 1.1 m test tunnel, not 0.9 m gap |
| Blocked standing | Release Ctrl under ceiling stays crouched; exits/stands when clear, on both peers |
| Crouch speed | Empty 2 m/s; sprint ignored; existing weight factor multiplies once |
| Jump/crouch interaction | No crouch jump or midair capsule shrink; landing applies intended stance |
| Menu/focus while airborne | Falls/lands safely, no input replay or stuck camera |
| Remote stance / late join | Correct capsule/body/eye and hands from current persistent state |
| Stance latency/refusal | No standing through ceiling; stale ack ignored, bounded retries, no frame RPC spam |
| All item grips, both views | Both hands on every centered held item, unchanged slot/heavy classification, connected arms, marker absent |
| Center aiming dot | One centered outlined dot; correct usable-target highlight, contrast, resolution scaling and hide/restore behavior |
| Equip/stow/throw/moving catch | Correct target switching, no hand chasing released item, sole item writer retained |
| Forward release / crouch with heavy near floor/wall | Clear forward placement or safe refusal, no underfoot release or standing/pushing on cargo; section 6A matrix passes |
| Repeated ship world transfers / disconnect / re-host | Slots/weight/stance recover, no stale pose or buffered jump; existing travel regressions pass |
| Four players / Steam two machines | Everyone sees stance and held hands; record revision/roles/RTT and actual results |
| Setup twice + prefab reopen | Same assets/GUIDs, no ForwardMarker or duplicate limbs |

Use Unity MCP only when connected; stop Play for script/asset edits. Use typed
editor helpers for FishNet operations. Move through **actual input** for apex,
distance and clearance tests; teleport hooks only set up scenarios. Measure
foot displacement, elapsed time, height/center, grounded flags, mass and grip.
Inspect Game-view captures rather than assuming collider logs prove good art.

Only local compilation and math can be checked without gameplay execution;
multiplayer claims require independent peer evidence. Network changes need a
human reviewer; hands/feel need visual feedback. This document records a build
plan, not completed tests or a guarantee of finished animation quality.
