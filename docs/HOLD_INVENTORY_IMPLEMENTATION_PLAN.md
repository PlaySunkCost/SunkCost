# Sunk Cost: holding and inventory implementation plan

**Status: merged to `main` on 14 September 2026 (PR #11, `780de9e`). Local
inventory, moving-catch, disconnect and re-host checks passed; the Steam
two-machine rows and the teammate review remain — see section 17 and
`HQ_PROTOTYPE_TEST_REPORT.md`.** Written 14 September 2026 against `main` at
`a4ddc45` in `github.com/PlaySunkCost/SunkCost`. Owner: Dan. Notion card
"Holding and inventory — rigid hold, E grab, Q drop, 4 slots + hand" (epic E3
Carry and haul, phase 02 Prototype, branch `dan/hold-feel`, touches the contract:
**yes**, needs two-client test: **yes**). It folds in the Phase-03 card "Four
inventory slots plus one hand".

Every FishNet API named below was checked against the installed package source
(`Library/PackageCache/com.firstgeargames.fishnet@382980e7eed4`, tag 4.7.3), and
the Unity editor APIs (`PreviewRenderUtility.Render(bool allowScriptableRenderPipeline, bool updatefov)`,
`BeginStaticPreview`, `EndStaticPreview`, `AddSingleGO`, `AssetPreview.GetAssetPreview`,
`IsLoadingAssetPreview(int)`, `Keyboard.digit1Key`, `NetworkTransform.Teleport()`)
were reflected in the running 6000.6.0f1 editor on 14/09. Do not substitute names
from tutorials or other versions.

**Read first:** `AGENTS.md`, `docs/NETWORK_CONTRACT.md` (sections 2, 3, 4, 5, 6),
`docs/CONVENTIONS.md`. Then `Assets/_Project/Scripts/Interaction/Basketball.cs`
and `Assets/_Project/Scripts/Player/HQPlayerController.cs`, which this plan
rewrites, and `docs/DEBUG_OVERLAY_IMPLEMENTATION_PLAN.md` section 4.2 for the
overlay's writer rule, which this plan has to keep honest.

### User amendment, 14 September 2026: automatic fifth-item pickup

With four occupied slots and one slot item equipped, E on an eligible fifth item
automatically stows the equipped item in its existing slot and holds the new item
as overflow. All four slot ids remain unchanged. This supersedes the
full-slots/occupied-hand refusal in the original table below. Holding an overflow
item still blocks further grabs and number keys until dropping or throwing it.
H5 must cover this transition and verify that all four original slot ids remain.

### User amendment, 14 September 2026: easier moving catches

The user requested catching thrown/moving balls and more forgiving targeting.
This supersedes the original Free-only/raycast-only rules below: **Free and
Released** items may be grabbed after server range, line-of-sight and inventory
checks. Held/Stowed items remain protected. Reach is still 2 m; aim allowance is
0.35 m around the crosshair, and an early E press is buffered for 0.3 s. Holding E
arms one grab as an item enters reach; one press/hold gesture issues at most one
request. A server-written motion version guards release impulses and rest RPCs
against messages from an earlier throw/catch. Silent storage may also catch a
Released item into a free slot. The contract change is in this branch for human
review, not claimed approved by a teammate.

---

## 1. Outcome and scope

What Dan asked for, decided on 14 September 2026 (the Notion card has the
question-by-question record):

1. **Rigid hold.** A held item is locked to a hold point at the lower-right of the
   camera (a right hand). It turns with the look direction, never lags, never
   collides while held. It moves only when thrown or released.
2. **Reach and prompt.** E grabs when the crosshair is on an item and the item's
   surface is within **2 m** of the eyes (both serialized). While that is true the
   HUD shows *"Press E to grab Basketball"*. With an overflow item in the hands it
   shows *"Hands full"*.
3. **Four slots plus hands.** Four slots drawn at the bottom of the screen with a
   number and a picture of the item. Keys 1–4 equip a slot's item into the hands
   (the item **stays in its slot** and the slot is highlighted); pressing the held
   slot's number puts it away. A stowed item disappears from the world for
   everyone.
4. **E rules.** E puts the item into the first free slot. If the hands are empty it
   also goes into the hands; if a slot item is already in the hands, the new item
   is stored silently and the hands do not change. When no slot is free (or the
   item is hands-only) the item goes into the hands as an **overflow item**; while
   an overflow item is held, E and the number keys do nothing until Q.
5. **Q drops** at the feet, no push. **Left click is Use**; for the basketball Use
   is throw (8 m/s along the look direction). Both empty the item's slot.
6. **Disconnect / leave:** everything the player carried, held or stowed, drops at
   the player's last position.

**Required:** the files in section 4, the scripted prefab changes in section 10,
the icon generator in section 11, the verification in section 14, the document
updates in section 15, a PR from `dan/hold-feel` to `main` with Idan or Dor
having read `PlayerInventory.cs` and `CarryableItem.cs`.

**Out of scope:** weight, a visible hand or arm, item icons drawn by hand,
hands-only loot beyond the flag (the "Three loot objects" card), a backpack
visual for stowed items, two-person carrying, any change to the elevator or
noise systems, Canvas / UI Toolkit.

## 2. Verified starting point

- `Basketball.cs` (259 lines) is the only carryable. State is two server-written
  SyncVars: `state` (int: Free 0 / Held 1 / Released 2) and `holderClientId`.
  Grab gives FishNet ownership to the holder; the holder is the physics writer
  while Held and Released; rest (0.15 m/s, 0.5 s, 4 s forced) hands back to the
  server. The held ball is a **live non-kinematic body pulled toward
  `HQPlayerController.HoldPoint` by a spring** every `FixedUpdate` (accel 80,
  damping 18). That spring is the drift Dan dislikes.
- The local "am I holding it" flag is derived from the SyncVars in
  `SyncLocalHeldState()` → `HQPlayerController.SetHeldBall`, never from an RPC,
  because a `TargetRpc` can reach a remote client before the same tick's SyncVar
  flush (`NETWORK_CONTRACT.md` section 6). The release impulse waits for both
  (`hasPendingRelease` + `TryApplyPendingRelease`). **Keep both mechanisms.**
- `OnOwnershipServer` resets the ball to Free whenever ownership is removed; the
  disconnect handler relies on that (`ServerOnRemoteConnectionState` →
  `RemoveOwnership()`). Stowing also removes ownership, so this reset must become
  state-aware (section 5).
- `HQPlayerController.cs` (139 lines): E toggles grab/drop, left click throws,
  `grabDistance` is a **2.5 m raycast** from the camera; the server re-checks
  distance from the player root with +0.75 m tolerance. `ServerRequestGrab` /
  `ServerRequestRelease` live here.
- `PrototypePlayer.prefab`: root (CharacterController 1.8 m, `NetworkObject`,
  client-authoritative `NetworkTransform`, `HQPlayerController`) → `ViewPivot`
  (y 1.6) → `PlayerCamera` and `HoldPoint` **as siblings**. Pitch is applied to
  `PlayerCamera.localRotation` only, so today the hold point yaws with the body
  but does not pitch with the view. Pitch is not replicated. Dor replaced the
  visual model on 14/09 (PR #5); the prefab is otherwise as `HQPrototypeBuilder`
  generated it.
- `Basketball.prefab`: sphere scaled 0.24 (a 24 cm ball, collider radius 0.12 m
  world), Rigidbody mass 0.62, `NetworkTransform` client-authoritative,
  `_sendToOwner: 1`, `_enableTeleport: 0`, `_preventDespawnOnDisconnect: 1`.
  It is a scene object in `HQPrototype.unity`, one instance.
- `NetworkDebugSnapshot` decides the SIM-HERE column from **`!Rigidbody.isKinematic`**
  (rule 1 in its comment). A kinematic held item would read REPLICATED on its
  holder. The snapshot's own comment says to extend `INetworkDebugInfo` with an
  explicit writer flag rather than special-case it; section 9 does that.
- Editor tooling that names `Basketball`: `HQPrototypeBuilder.CreateBallPrefab`,
  `HQPrototypeTestHooks` (grab/release/held hooks via reflection on
  `HQPlayerController`), `HQPrototypeValidator` (`CheckCount<Basketball>(scene, 1)`).
- `CONVENTIONS.md` ownership table puts "loot, UI" under Dev C and "player
  controller, grab/carry/drag" under Dev A. This card sits on both; **tell Dor
  before starting** the HUD and inventory files. No scene other than
  `HQPrototype.unity` (Dan's) is touched.

## 3. Contract impact, stated up front

`NETWORK_CONTRACT.md` changes (section 15 has the exact edits). None of them
weakens a rule; they add a state and a table row:

- **Section 3 table:** new row *Inventory contents and slot assignment — Server
  only — clients send requests, the server decides what fits and where.*
- **Section 2 / grab flow:** a fourth item state, **Stowed**: server-owned, hidden,
  kinematic, carried in a player's inventory. Equip from Stowed is a grab from
  the server's point of view (validate, grant ownership); stow is a release that
  skips the settle phase because the item is not moving.
- **Section 6 harness bullet:** the harness now has an inventory; disconnect
  returns held, released **and stowed** items to server simulation, dropped at
  the player's last position. (The real game's "body keeps its inventory" rule is
  unchanged; the harness has no bodies.)
- **Writer rule (section 2, "exactly one simulation writer"):** unchanged. A held
  item is written by its holder even though its Rigidbody is kinematic; the
  holder writes the transform from the hold point instead of from physics. The
  overlay's writer flag is made explicit so the F3 column keeps telling the truth.

Because SyncVars and RPCs change, `PlayerInventory.cs` and `CarryableItem.cs`
need a human reader (Idan or Dor) before merge. `CONVENTIONS.md`, "The AI rule".

## 4. Architecture, files and responsibilities

All new code under `Assets/_Project/Scripts/`, namespaces `SunkCost.Interaction`
and `SunkCost.Player`. One class per file.

| File | Role |
|---|---|
| `Interaction/CarryableItem.cs` (rename of `Basketball.cs`, **keep the `.meta` GUID**) | The item's network state machine: Free / Held / Released / Stowed, ownership, presentation (kinematic, gravity, collider, renderer), rigid snap to the hold point on the holder, rest handoff, `INetworkDebugInfo`. Serialized: `displayName`, `fitsInSlot`, `icon` (Texture2D), `useAction`, `throwSpeed`, `releaseHandoffTimeout`, `resetPosition`. |
| `Interaction/ItemState.cs` | `enum ItemState : byte { Free, Held, Released, Stowed }` and `enum ItemUseAction : byte { None, Throw }`. |
| `Interaction/InventorySlots.cs` | Pure `struct InventorySlots` (four `int` object ids, `-1` empty) with `IndexOf(id)`, `FirstFree()`, `With(index, id)`, `Count`. No Unity types. Unit-checkable in the editor like `HQPrototypeLobbyChecks`. |
| `Interaction/PlayerInventory.cs` | `NetworkBehaviour` on the player prefab. Owns `SyncVar<InventorySlots> slots` (server-written) and the four `ServerRequest…` RPCs (grab, equip, drop, use), the `TargetRefuse` failure path, the server-side decision table (section 6), and the disconnect dump. Exposes read-only `Slots`, `HeldItem`, `HeldSlot` (−1 for overflow), `Refusal` (text + expiry) for the HUD. |
| `Player/HQPlayerController.cs` | Keeps look/move/escape. Loses the grab/release RPCs and `heldBall`. Gains `interactReach`, per-frame **interaction targeting** (raycast → `CurrentTarget`, `TargetInReach`) and the new bindings: E → `inventory.RequestGrab(target)`, Q → `RequestDrop()`, left click → `RequestUse(camera.forward)`, 1–4 → `RequestEquip(i)`. |
| `Player/PlayerHudUI.cs` | Plain `MonoBehaviour` on the player prefab, `OnGUI`, owner-only. Draws the prompt and the four slots from `HQPlayerController.CurrentTarget` and `PlayerInventory`. IMGUI like `PrototypeSessionUI`; no Canvas. Hidden while `SessionInputGate.MenuOpen`. |
| `Net/INetworkDebugInfo.cs` + `Net/NetworkDebugSnapshot.cs` | Add an explicit writer flag (section 9). |
| `Editor/Prototype/HQPrototypeInventorySetup.cs` | Idempotent, `SerializedObject`-based prefab and scene edits (section 10). |
| `Editor/Prototype/ItemIconGenerator.cs` | Renders each `CarryableItem` prefab to a 128×128 PNG and assigns it to `icon` (section 11). |
| `Editor/Prototype/HQPrototypeInventoryChecks.cs` | Pure checks for `InventorySlots` and the decision table (section 12). |
| `Editor/Prototype/HQPrototypeTestHooks.cs`, `HQPrototypeValidator.cs`, `HQPrototypeBuilder.cs` | Rename references; new hooks for equip/drop/use/prompt/inventory text. |

**Why the inventory owns the RPCs, not the controller.** The contract says a
client requests grabs "through its owned player interaction object". Today that
is the controller; after this change every request touches inventory state, so
one file makes every server decision and the controller is input only. The test
hooks that reflect on `HQPlayerController.ServerRequestGrab` move with it.

**Why the item keeps its own state instead of the inventory holding everything.**
The item's `state` + `holderClientId` SyncVars already implement the ordering-safe
"am I holding it" derivation and the release handoff, verified on two machines.
The inventory adds one SyncVar (the slots) and reads the items for the rest.
There is exactly one writer for each piece of state: the server writes both.

## 5. Item state machine (`CarryableItem`)

SyncVars (server-written only): `SyncVar<ItemState> state`, `SyncVar<int>
holderClientId` (the holder while Held/Released, the **carrier** while Stowed, −1
while Free). FishNet 4.7.3 generates the serializer for a byte enum; if the
codegen complains, fall back to `SyncVar<byte>` with casts.

| State | Ownership | Writer | Rigidbody on writer | Gravity | Collider | Renderer |
|---|---|---|---|---|---|---|
| Free | none (server) | server | non-kinematic | on | on | on |
| Held | holder | holder (snap, not physics) | **kinematic** | off | **off** | on |
| Released | holder until rest | holder | non-kinematic | on | on | on |
| Stowed | none (server) | server (nothing moves it) | kinematic | off | off | **off** |

Non-writers are always kinematic with zero velocity, as today. Every peer derives
the whole row from `(state, IsOwner, IsServerStarted, Owner.IsValid)` in one
method, `ApplyRole()` (today's `RefreshRole` plus collider and renderer), called
from `OnStartNetwork`, `OnStartClient`, both `OnOwnership…` callbacks and both
SyncVar `OnChange`s. Never from an RPC.

Transitions, all `[Server]` methods on the item, called by `PlayerInventory`:

- `ServerGrab(conn, player)` — from **Free or Stowed**. Sets `holderClientId`,
  `state = Held`, then `GiveOwnership(conn)`. From Stowed it first moves the item
  to the carrier's hold point and calls `NetworkTransform.Teleport()` (verified:
  `Teleport()` marks the next send as a teleport when called by the controller,
  which the server is while the item has no owner) so spectators do not
  interpolate it from wherever it was parked.
- `ServerStow(conn)` — from **Held** by `conn`. Sets `state = Stowed` **before**
  `RemoveOwnership()`, so the server's `OnOwnershipServer` sees Stowed and does
  not reset to Free. Position is left where the hand was; the item is hidden.
- `ServerRelease(conn, direction, throwIt)` — from Held, unchanged: `Released`,
  `TargetApplyRelease` to the holder, rest → `ServerRequestRest` → Free.
- `ServerDropAt(position)` — from any state, server decides: `Free`,
  `holderClientId = -1`, `RemoveOwnership()` if owned, move to `position`,
  `Teleport()`, zero velocity. Used by the disconnect dump and by `ServerReset`
  (which becomes `ServerDropAt(resetPosition)`).

`OnOwnershipServer` rule after this change: *if ownership was removed and the
state is Held or Released, reset to Free; if Stowed, leave it.* The disconnect
safety net on the item (`ServerOnRemoteConnectionState` → `RemoveOwnership`)
stays for Held/Released; the inventory's dump handles Stowed (section 8).

**The rigid hold** (replaces the spring): on the holder only, in `LateUpdate`
(after `HQPlayerController.Update` has moved the camera), while
`state == Held && IsOwner && !hasPendingRelease`:
`transform.SetPositionAndRotation(holder.HoldPoint.position, holder.HoldPoint.rotation)`.
The body is kinematic and `Rigidbody.interpolation` is None on the prefab, so a
direct transform write is correct; the client-authoritative `NetworkTransform`
sends whatever the transform is at the next tick. Non-holders keep rendering
replicated state exactly as they do now, so nothing about who sends what changes.
`FixedUpdate` keeps only the Released rest check and the fall-out-of-world reset.

Ordering cases the derivation must survive (both were the cause of the earlier
"non-host cannot throw" bug, so test them deliberately):

- **Stow, old holder:** ownership removal and `state = Stowed` may arrive in
  either order. Ownership first → Held + !IsOwner → kinematic hidden? No: Held
  non-owner keeps the renderer **on** (that is the normal spectator view), then
  Stowed arrives and hides it. Stowed first → hidden while still owner; then
  ownership removal changes nothing visible. Both end in the Stowed row.
- **Equip, new holder:** `GiveOwnership` and `state = Held` in either order. Owner
  first → Stowed + IsOwner → still hidden and kinematic (Stowed row ignores
  ownership). Held first → Held + !IsOwner → visible, kinematic, replicated.
  Then the other arrives → Held row on the owner, snap begins. The one-frame
  pop at the parked position on spectators is accepted (it is why `Teleport()`
  is called).
- **Release:** unchanged (`hasPendingRelease`).

## 6. Inventory rules as a server decision table (`PlayerInventory`)

Server-side inputs: `slots` (SyncVar), `heldItem` (server-only field, the item
whose `state == Held && holderClientId == Owner.ClientId`, maintained by the
inventory because it drives every transition), the target item.

| Request | Preconditions checked on the server | Effect |
|---|---|---|
| `ServerRequestGrab(NetworkObject target)` | sender owns this player (`RequireOwnership`); target has a `CarryableItem`; `state == Free` (a settling Released item is refused, contract section 2); reach: distance from the player's eye (the `ViewPivot` height, server has no pitch) to `Collider.ClosestPoint(eye)` ≤ `interactReach + 0.75`; hands do not hold an **overflow** item | If `fitsInSlot` and `slots.FirstFree() >= 0`: write the slot; if `heldItem == null` → `ServerGrab` (into the hands), else → `ServerStow` immediately (stored silently, hands unchanged). Else (no slot or hands-only): if `heldItem == null` → `ServerGrab` as overflow (no slot written), else refuse `HandsFull`. |
| `ServerRequestEquip(int slot)` | 0 ≤ slot < 4; `slots[slot] != -1`; item still spawned and `holderClientId == me` or Stowed by me; hands do not hold an overflow item | If `heldItem` is that item → `ServerStow` (put away). Else: if `heldItem != null` (another slot item) → `ServerStow` it; then `ServerGrab` the requested item from Stowed. |
| `ServerRequestDrop()` | `heldItem != null` | `ServerRelease(conn, Vector3.zero, false)`; clear its slot if it has one. |
| `ServerRequestUse(Vector3 aim)` | `heldItem != null` | `useAction == Throw` → `ServerRelease(conn, aim.normalized, true)`; clear its slot. `None` → nothing (future items). The direction is client-supplied, as today; the server normalises it and ignores zero. |
| (disconnect) | owner's connection stopped | section 8. |

Refusals go back through `[TargetRpc] TargetRefuse(NetworkConnection conn, byte
reason)` with reasons `HandsFull`, `TooFar`, `NotFree`, `NoSuchItem`; the HUD shows
the text for 1.5 s. That is the caller-visible failure path the contract asks
for. Successful requests need no reply: the SyncVars are the reply.

Client-side pre-checks mirror the table so the common refusals never leave the
machine (E with an overflow item held, a number key with an overflow item held,
Q with empty hands). The server checks anyway.

`slots` is a `SyncVar<InventorySlots>`, one struct with four public `int`s, so a
change is one message and one `OnChange`; FishNet generates the struct
serializer. Object ids are FishNet `NetworkObject.ObjectId`; clients resolve them
with `ClientManager.Objects.Spawned.TryGetValue(id, out NetworkObject)`
(`IReadOnlyDictionary<int, NetworkObject>`; on the server `ServerManager.Objects.Spawned`).
A stowed item is still spawned, only hidden, so the lookup works everywhere,
including for a late joiner who receives the SyncVar's full value at spawn.

## 7. Input and HUD

`HQPlayerController.Update`, owner only, after `Look()` and `Move()` and only
while `SessionInputGate.CanPlay` and the cursor is locked (existing gate):

- **Targeting every frame** (cheap, one raycast): `Physics.Raycast(camera.position,
  camera.forward, out hit, interactReach, ~0, QueryTriggerInteraction.Ignore)`;
  `CurrentTarget = hit.collider.GetComponentInParent<CarryableItem>()`. Held and
  stowed items have their collider off, so they cannot be targeted; a Released
  (settling) item can, and the prompt says nothing for it. `hit.distance` is the
  eyes-to-surface distance, so "within 2 m of the surface and the crosshair on it"
  is exactly "the raycast of length `interactReach` hit it".
- **E** → `inventory.RequestGrab(CurrentTarget)` when it is Free.
- **Q** → `RequestDrop()`. **Left click** → `RequestUse(camera.forward)`, still
  gated by `SessionInputGate.ClickSuppressedThisFrame` (the Resume click).
- **1–4** (`Keyboard.current.digit1Key…digit4Key.wasPressedThisFrame`) →
  `RequestEquip(i)`.
- E no longer drops. `grabDistance` becomes `interactReach = 2f`; the old 2.5 m
  raycast and the server's +0.75 tolerance are the two numbers that change.

`PlayerHudUI.OnGUI` (owner only, hidden while the session menu is open):

- **Prompt**, centred under the crosshair: *"Press E to grab {displayName}"* when
  `CurrentTarget` is Free; *"Hands full"* when a target is in reach but the hands
  hold an overflow item; the last refusal text while it is fresh.
- **Slots**, bottom centre: four 64 px boxes with 8 px gaps, number top-left, icon
  drawn with `GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit)` (a grey square
  placeholder when the item has no icon), a highlighted border on the slot whose
  item is in the hands. An overflow item has no slot; a one-line label above the
  boxes says *"In hand: Basketball (no slot)"* so the state is visible.
- Names and icons come from the item resolved by object id (section 6).

## 8. Disconnect and leave

`PlayerInventory.OnStartServer` subscribes `ServerManager.OnRemoteConnectionState`
(the pattern already in `Basketball`); on `RemoteConnectionState.Stopped` for
this player's `Owner`:

1. Read `transform.position` (the callback fires before FishNet despawns the
   player's objects, verified in the earlier disconnect fix).
2. For every non-empty slot and for `heldItem`: `item.ServerDropAt(position +
   scatter)` where scatter is a small ring (0.4 m radius, 0.3 m up) so several
   items do not spawn inside each other. Clear `slots`.

The item's own handler also runs for a Held item and removes ownership; both end
in Free at the dropped position, order irrelevant. Host leave stops the server;
`CarryableItem.OnStartServer` → `ServerDropAt(resetPosition)` and
`PlayerInventory.OnStartServer` → empty slots make a re-hosted room start fresh,
as the design's leave decision requires.

## 9. Overlay writer flag

`INetworkDebugInfo` gains `bool? WriterOverride { get; }` (null = use the
Rigidbody rule). `NetworkDebugSnapshot` checks it **before** rule 1.
`CarryableItem` returns `true` when this machine is the writer per the section 5
table (Free → server with no owner; Held/Released → `IsOwner`; Stowed → server),
so the F3 column shows SIM-HERE on the holder of a kinematic held item and
REPLICATED everywhere else. `DebugStatus` adds the Stowed case:
*"Stowed by N"*. The `HQPrototypeTestHooks.DebugSnapshotText()` hook already
prints this, which is how the checkpoints below read it.

## 10. Prefab and scene changes, scripted

No hand edits. `HQPrototypeInventorySetup.Apply()` (menu *Sunk Cost / Prototype /
Apply inventory setup*), idempotent, `SerializedObject` for every write, throws on
unexpected state, returns a summary. It:

1. **Player prefab:** re-parents `HoldPoint` from `ViewPivot` to `PlayerCamera`
   and sets its local position to `(0.30, -0.22, 0.60)` (lower-right, 60 cm out;
   the ball is 24 cm and the camera near plane 0.3). Adds `PlayerInventory` and
   `PlayerHudUI` if missing. Leaves Dor's model, the CharacterController and the
   NetworkTransform untouched. `HQPrototypeBuilder.CreatePlayerPrefab` is updated
   to generate the same layout for a fresh project.
2. **Ball prefab:** sets `displayName = "Basketball"`, `fitsInSlot = true`,
   `useAction = Throw`. The `icon` is assigned by the generator (section 11).
3. **Scene `HQPrototype.unity`:** adds four more basketball instances (five total)
   at distinct `resetPosition`s around the spawn area, so *slots full* and
   *overflow* can be exercised with the one prefab that exists. Names
   `Basketball (2)`… `Basketball (5)`. `HQPrototypeValidator` changes from
   `CheckCount<Basketball>(scene, 1)` to `>= 1` items and checks the player
   prefab layout (HoldPoint under the camera, both components present).

Scene lock: Dan owns `HQPrototype.unity`; say so in chat before running step 3.
Idan's `DiveSite01.unity` is not touched.

## 11. Item icons (editor tool)

`ItemIconGenerator` (menu *Sunk Cost / Prototype / Generate item icons*):

- For every prefab under `Assets/_Project/Prefabs/Interaction/` with a
  `CarryableItem`: `PreviewRenderUtility` (`UnityEditor`; `camera`, `lights`,
  `BeginStaticPreview(Rect)`, `AddSingleGO(instance)`, `Render(true)` so URP is
  used, `EndStaticPreview()` → `Texture2D`, `Cleanup()`), camera framed on the
  renderer bounds from a 30° elevated three-quarter angle, 128×128, transparent
  background.
- `EncodeToPNG` → `Assets/_Project/Art/Prototype/ItemIcons/{prefabName}.png`;
  `TextureImporter`: Default, `alphaIsTransparency`, no mipmaps, point-free
  bilinear, 128 max size. Then assign to the prefab's `icon` through
  `SerializedObject` and `SaveAssets`.
- Fallback if the preview utility renders magenta under URP: `AssetPreview.GetAssetPreview(prefab)`
  polled until `AssetPreview.IsLoadingAssetPreview` is false. Try the utility
  first; the fallback is lower quality but always works.
- Regenerating overwrites; icons are committed assets (small PNGs, LFS by the
  existing `.gitattributes` rule for `*.png`).

## 12. Editor checks and hooks

- `HQPrototypeInventoryChecks.RunOrThrow()`: pure `InventorySlots` behaviour
  (`FirstFree` on empty/full, `IndexOf`, `With` immutability) and the decision
  table for grab/equip on a fake state (`hands empty + free slot`, `holding slot
  item + free slot`, `holding overflow`, `slots full + hands empty`, `hands-only
  item`), asserting which transition each case selects. The table logic lives in
  a static, Unity-free `InventoryRules.Decide(...)` inside `PlayerInventory.cs`'s
  sibling `InventoryRules.cs` so it can be checked without a network.
- `HQPrototypeTestHooks`: `Basketball` → `CarryableItem` (find by name for the
  multi-ball scene: `Item(string name)`); `ClientRequestGrab(name)`,
  `ClientRequestEquip(int)`, `ClientRequestDrop()`, `ClientRequestUse()` reflect on
  `PlayerInventory`; `InventoryText()` prints slots, held item, overflow flag;
  `PromptText()` prints the HUD prompt; `ItemState(name)` prints state, holder,
  owner, kinematic, collider and renderer enabled, position. Unity MCP's dynamic
  assembly cannot reference FishNet types, so everything goes through these hooks.

## 13. Implementation sequence and checkpoints

Each step compiles on its own and is checked before the next. Exit Play Mode
before every script edit (a live FishNet/Steam session does not survive a domain
reload).

1. **Rename** `Basketball` → `CarryableItem` (file + `.meta` together so the
   prefab keeps its script GUID), add `ItemState` / `ItemUseAction`, serialized
   `displayName`, `fitsInSlot`, `icon`, `useAction`. No behaviour change. Update
   the editor references. **Check:** validator passes; editor host + headless
   client (`-hq-auto-join-local 127.0.0.1`) grab/throw/drop as before.
2. **Rigid hold + collider off + hold point under the camera** (`Apply()` step 1).
   **Check:** in the editor, hold while walking, strafing and turning: `ItemState`
   hook shows kinematic, collider off, position == hold point each frame; walk
   into a wall, ball pokes through, nothing pushes it. Headless client holds
   while the editor watches: no drift on the spectator beyond normal
   interpolation. F3 shows SIM-HERE on the holder (section 9 lands here).
3. **Reach + prompt** (`interactReach`, targeting, `PlayerHudUI` prompt only).
   **Check:** `PromptText()` is empty at 3 m, *"Press E to grab Basketball"* at
   1.5 m with the crosshair on it, empty when looking past it. Server refuses a
   grab requested from 4 m via the hook (`TargetRefuse TooFar` visible in the
   prompt).
4. **Inventory core:** `InventorySlots`, `InventoryRules`, `PlayerInventory` with
   the RPCs, Stowed state on the item, key bindings E/Q/click/1–4, `Apply()`
   steps 2–3 (components, more balls). **Check:** `HQPrototypeInventoryChecks`
   pass. In the editor as host: E → slot 1 + hand; 1 → put away, ball hidden,
   `DebugSnapshotText` shows *Stowed by 0* with SIM-HERE on the host (the server
   is the Stowed writer); 1 → back in hand; Q → on the floor, slot empty;
   click → thrown, slot empty. With five balls: E ×4 fills the
   slots, E on the fifth → overflow, then 1 and E do nothing and the prompt says
   *"Hands full"*; Q → number keys work again. Headless client repeats the same
   through hooks while the editor watches renderer/collider flags.
5. **Slots HUD + icons.** **Check:** `Unity_Camera_Capture` screenshot shows four
   boxes, the ball icon in slot 1, highlight on the held slot, the overflow
   label when relevant.
6. **Disconnect dump.** **Check:** headless client holds one ball and stows two,
   then is killed; after FishNet's timeout (35–75 s) all three are Free on the
   floor near its last position, F3 shows server SIM-HERE on each. Repeat with
   the client's `LeaveSession()` (immediate).
7. **Docs, report, PR.** Section 15, then the PR with the review note.

## 14. Verification matrix and acceptance

Local (editor host + headless standalone clients; record in
`docs/HQ_PROTOTYPE_TEST_REPORT.md`):

| Id | Case | Expected |
|---|---|---|
| H1 | Hold while moving/turning | no drift, kinematic, collider off, spectator sees it at the hand |
| H2 | Reach | prompt only within 2 m and on target; 4 m grab refused with message |
| H3 | Slots | E fills first free slot; 1–4 equip/put away; held slot highlighted; stowed item hidden on every peer |
| H4 | Silent store | holding slot item, E on another → stored, hands unchanged |
| H5 | Overflow | five balls: fifth is hands-only; E and numbers blocked; Q frees them |
| H6 | Drop / Use | Q drops at feet, click throws; both clear the slot; rest handoff unchanged |
| H7 | Late joiner | client joining while items are stowed sees them hidden and the slots correct on the carrier's F3 detail |
| H8 | Disconnect | held + stowed items dropped at last position, server-owned |
| H9 | Host leave / re-host | items back at reset positions, slots empty |

Steam, two machines, shared build (the card's "Done means"): H1, H3, H5, H6, H8
with real people; record build revision, roles, Steam accounts, RTT.

Acceptance: all Local rows pass with hook output pasted into the report; the
Steam rows are run by two people; Idan or Dor has read `PlayerInventory.cs` and
`CarryableItem.cs` and says so on the PR; the documents in section 15 are updated
in the same PR.

## 15. Documents to update in the same PR

- `docs/NETWORK_CONTRACT.md`: section 3 table row (inventory, server only);
  section 2 note adding the Stowed state to the grab/release flow; section 6
  harness bullet (stowed items drop at the last position). Second pair of eyes
  required by section 5, rule 5.
- `docs/DESIGN.md`: "First playable HQ harness" paragraph — the harness now has
  four slots plus hands, E/Q/click bindings, a 2 m reach and a prompt; keep the
  section 3 "Carrying" row as the game rule it already states.
- `README.md`: controls line (E grab, Q drop, click use/throw, 1–4 slots).
- `docs/HQ_PROTOTYPE_TEST_REPORT.md`: the matrix above with actual output.
- `docs/DEBUG_OVERLAY_IMPLEMENTATION_PLAN.md` section 4.1: note the writer
  override.
- Notion: this card to Done; the Phase-03 inventory card closed as folded.

## 16. Risks and non-goals, stated once

- **A kinematic held item shows REPLICATED on the holder** unless section 9 lands
  with section 5's step 2. Do them together.
- **`OnOwnershipServer` reset** is the one place a Stowed item could silently
  become Free. The state-aware rule in section 5 and check H3 cover it.
- **Ordering** between ownership and SyncVar messages is real on remote clients
  and invisible on the host; every checkpoint that involves a client runs on the
  headless standalone, never only on the host.
- **Hold point under the camera** means an item can be pushed into geometry when
  the player faces a wall; when released there it may be inside a collider for a
  frame. Continuous collision on the ball's Rigidbody handles the usual case;
  accepted for the prototype.
- **Five identical balls** are a test fixture, not content. The "Three loot
  objects" card replaces them and adds the hands-only item that exercises
  `fitsInSlot = false` for real.
- **Not doing:** weight, a visible hand, a backpack visual, swap-on-equip for
  overflow items (blocked by decision), throw charge, hands-only enforcement
  beyond the flag, changes to the elevator, noise, oxygen or dive site.

## 17. Progress record

**Merged to `main` on 14 September 2026 as PR #11 (`780de9e`, commit `b3dd1cf`).**
Everything in sections 1–15 landed, plus two things the plan did not have:
moving catches (a Released item can be caught before rest; the contract's grab
flow and a motion version guard were amended for it) and an automatic fifth
pickup (four slots full and one of them equipped → the equipped item is stowed
into its own slot and the fifth is held as overflow, one server request).
`HQInventoryRuntimeChecks`, `HQCatchRuntimeChecks` and the Local-only
`InventoryVerificationPeer` (`-hq-inventory-test-dir`) are the repeatable
runtime checks.

Verified on Local/Tugboat (editor + standalone peers, see
`docs/HQ_PROTOTYPE_TEST_REPORT.md`, "Hold/inventory branch"): rows H1–H9 of
section 14, the HUD capture, moving catches both directions, and the fifth
pickup rule.

### Still open

1. **Steam, two machines:** rows H1, H3, H5, H6, H8 with a shared build from
   `main` at `780de9e` or later (*Sunk Cost / Prototype / Build Windows
   Development*), recorded in the test report with revision, roles, accounts
   and RTT.
2. **Teammate review** (CONVENTIONS "The AI rule"): Idan or Dor reads
   `PlayerInventory.cs`, `CarryableItem.cs` and the `NETWORK_CONTRACT.md`
   changes; Dor looks at `PlayerHudUI.cs` and the icon generator (UI/loot are
   Dev C's column). The PR merged before that read; the review is tracked as a
   Notion card.
3. Notion: the card stays *In progress* until 1 and 2 are done; the Phase-03
   "Four inventory slots plus one hand" card is folded into it.
