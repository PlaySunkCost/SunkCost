# Sunk Cost: HQ multiplayer basketball implementation handoff

**Status: first playable slice implemented on `codex/hq-basketball-prototype`.**

Prepared against repository commit `b82629669ccb44b20857afa1fd04baa2a00da175`.
Updated 13 September 2026 after a working Unity MCP connection was verified.
The user subsequently authorized implementation on a new branch. This document
remains the detailed target and test checklist; the smaller first slice records
its completed checks and remaining two-computer work in
`HQ_PROTOTYPE_TEST_REPORT.md`.

This is a complete implementation specification for the first prototype, not a
claim that uninstalled dependencies or a Steam connection have already been
tested. Section 3 defines the dependency check; section 16 defines the real
two-computer acceptance test. Neither can be replaced by a written plan.

Unity MCP setup, the grey-box HQ scene, local/Steam transport selection, player
movement and the basketball ownership loop are implemented. The Steam lobby,
invites, four-player capacity and the pre-spawn admission handshake from section 9
were implemented on `dan/steam-lobby` per `STEAM_LOBBY_IMPLEMENTATION_PLAN.md`;
that document and `HQ_PROTOTYPE_TEST_REPORT.md` record which acceptance checks
have actually run.

**Quick navigation:** [scope](#1-exact-outcome-and-scope),
[Unity MCP workflow](#unity-mcp-connection-and-working-method),
[dependencies](#3-dependencies-compatibility-and-external-prerequisites),
[files](#4-folder-layout-and-file-responsibilities),
[scene/prefabs](#5-scene-prefabs-and-inspector-wiring),
[ownership protocol](#11-ball-state-and-request-protocol),
[implementation order](#14-ordered-implementation-stages-and-gates),
[acceptance tests](#16-acceptance-checklist-for-you-and-a-friend).

## 1. Exact outcome and scope

Two friends launch the same Windows build. One hosts; the other joins. They appear
in the same simple HQ room, see each other walking and turning, and take turns
picking up, carrying, dropping and throwing one basketball. Both see the same ball
and its motion. Simultaneous grabs do not duplicate it. If its holder disconnects,
the remaining player can recover it. The host can reset a stuck/lost ball.

**Required:** a grey-box room; first-person keyboard/mouse control; visible player
bodies; two-player networking over Steam; local connection mode for development;
one physical ball; connection/interaction UI; ownership diagnostics; reproducible
scene creation and Windows build; automated logic checks and manual network tests.

**Outside this milestone:** underwater scenes, elevator, oxygen, inventory slots,
weight penalties, monsters, gameplay noise integration, quota, saving, HQ shop,
voice chat, a basketball scoring system, dribbling, special catches, animation
rigging, public matchmaking, host migration, character customization and final art.
A decorative hoop is optional only after the required tests pass. Existing game
rules remain unchanged; postponing a system here does not remove it from the game.

The room is a test HQ, not a finished social hub. Use primitive meshes and solid
materials; a shared orange sphere is sufficient. No asset purchases or external
art generation are needed. Team members can use an existing voice app during tests.

## 2. Repository baseline and decision status

Read `AGENTS.md`, `docs/DESIGN.md`, `docs/NETWORK_CONTRACT.md`,
`docs/CONVENTIONS.md` and `docs/WORKFLOW.md` before implementation. The current
network contract governs ownership; this plan supplies proposed implementation
defaults where it leaves engineering details open. These defaults do not claim
prior teammate sign-off. Have a teammate review the network design before merging
dependent implementation, following the existing workflow.

### What was actually found

| Area | Current repository state |
|---|---|
| Unity | `6000.6.0f1`, revision `f7f8ed4d1e24` |
| Rendering | URP `17.6.0`; existing desktop pipeline assets under `Assets/Settings/` |
| Input | Input System `1.20.0`; new input backend enabled; template `Assets/InputSystem_Actions.inputactions` |
| Tests/UI | Unity Test Framework `1.8.0`, uGUI `2.6.0` |
| Editor automation | Installed `com.unity.ai.assistant` `2.19.0-pre.2`; Codex `unity_mcp` connection verified directly in this task |
| Scene/build list | `Assets/_Project/Scenes/Prototype/HQPrototype.unity` enabled for the prototype build |
| Physics | Fixed timestep `0.02` seconds, separate from the contract's 30 Hz network tick |
| Serialization | Force Text and Visible Meta Files already configured |
| Game scripts | `NoiseSystem`, `NoiseEvent`, `NoiseEmitter`, `INoiseListener`, example `BellEaterEars` |
| Networking | FishNet and Steamworks.NET pinned in UPM; unmodified FishySteamworks `4.1.1` source and license vendored under `Assets/ThirdParty/` because its repository package layout is not directly UPM-compilable |
| Prototype | HQ scene, player/ball prefabs, movement, session UI, ownership loop, validator and build helper implemented |

### Verified MCP evidence and its limits

- Seven tools were discovered and became callable directly in this Codex task
  after restarting Codex. Configuration alone was not considered a connection.
- `Unity_GetConsoleLogs` executed successfully with `logTypes: "error"` and
  returned no errors. That filtered response does not establish a warning-free
  project, successful tests, or compilation of future network packages.
- `Unity_RunCommand` compiled and executed commands that created, selected and
  framed a primitive cube in `SampleScene`, then removed that cube on request.
  The scene was marked dirty; those commands did not save it to disk.
- Prefab generation, scene save/reload validation, screenshots, automated tests,
  builds and network play have **not** been verified by that smoke test. The
  procedures below specify how to perform and verify them during implementation.

Unity also generated previously missing `_Project` `.meta` files when the project
opened. Inspect and preserve those files and the editor's current unsaved state;
do not discard them as incidental MCP output or regenerate their GUIDs. Check
current Git/editor state again before beginning work.

The recently fixed emitter now defaults `SourceId` to zero. The contract's sentence
saying it currently uses Unity instance IDs is stale. Correct that description
when implementation is authorized; do not reintroduce `GetInstanceID()` or assume
zero already represents a real FishNet object. Leave the noise scaffold otherwise
untouched in this milestone.

### Proposed defaults for this prototype

- Two total players, including the host. Keep capacity configurable to four, but
  do not report four-player support as tested until four players actually test it.
- One room loaded locally before connecting; all peers use the same scene/build.
- One server-spawned ball, with no client owner initially.
- Client-owned character movement, with loose server validation/correction.
- Ball ownership transfers to its holder; release keeps that client as simulation
  writer until rest/handoff, matching the existing contract.
- **No mid-flight catching yet.** A released ball still owned by the throwing
  client is unavailable for another grab until handoff. Show this clearly in UI.
- Normal release returns control after settling. A 10-second release timeout
  forces a coordinated handoff even if still moving, preventing a stuck owner.
  This timeout is an explicit proposed completion of the contract's open item.
- No corpses/death in this HQ-only harness. Disconnect despawns the player avatar
  and preserves the ball. This is not the final mid-dive death/disconnect behavior.

When coding is authorized, record this milestone and its limits in DESIGN.md,
and the settling/timeout protocol in NETWORK_CONTRACT.md. Do not rewrite the
full-game release scope or silently replace ownership with server-only ball physics.

## 3. Dependencies, compatibility and external prerequisites

### Unity MCP connection and working method

Use this project's existing Unity MCP bridge as the primary editor interface.
Use normal repository file tools for C#, documentation and diffs; use MCP to run
Unity APIs for object/asset creation, wiring, inspection and editor validation.
MCP is development tooling, not a game runtime or networking dependency. The
room, player and ball must run in a standalone build without an MCP client.

The installed Unity package labels this bundled MCP bridge **deprecated** and
recommends Unity CLI. It nevertheless passed the tests above. Keep using this
working installation for the milestone; reassess if an update breaks it rather
than silently adding another bridge or changing the project stack.
[Unity's package setup and deprecation notice](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.19/manual/integration/unity-mcp-get-started.html).

**Connection on this Windows machine:** Codex's user-level
`C:\Users\Owner\.codex\config.toml` contains this entry:

```toml
[mcp_servers.unity_mcp]
command = 'C:\Users\Owner\.unity\relay\relay_win.exe'
args = ["--mcp", "--project-path", 'C:\Dev\SunkCost']
```

This is a record of the local setup, not a portable file to copy into Git.
Another developer must use their own executable and project paths. A teammate
using Claude can configure the same relay as a stdio MCP server with those
arguments in Claude's client configuration. Git does not transfer MCP connections.
Do not install a duplicate server on this machine. When configuration changes,
restart/reload the client and confirm the tools are actually available in its
current task. No further restart is needed while the tools already work.
[Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp).

Keep the correct project open in Unity `6000.6.0f1`. The installed package's
settings page is **Edit > Project Settings > AI > Unity MCP Server**. If stopped,
start the bridge; if Unity presents a pending connection, use its normal Allow
flow. Do not bypass connection approval or globally enable automatic approvals.
The project-path argument prevents selecting an unrelated open project.

**Actual tools exposed by this installation:** names below omit the client's
optional server/namespace prefix. Discover their current schemas before calling;
do not assume names from another Unity MCP plugin or older tutorial.

| Tool | Use in this milestone |
|---|---|
| `Unity_RunCommand` | Compile/execute an editor C# command; inspect scenes, call project-owned builders/validators, start tests/builds and inspect runtime state |
| `Unity_GetConsoleLogs` | Read console output; arguments `maxEntries`, `includeStackTrace`, `logTypes` |
| `Unity_Camera_Capture` | Check the preview/player camera image; optional current `cameraInstanceID` |
| `Unity_SceneView_CaptureMultiAngleSceneView` | Check 3D room layout; optional `focusObjectIds` obtained from current objects |
| `Unity_SceneView_Capture2DScene` | Available, but not needed for this 3D HQ |
| `Unity_AssetGeneration_GetModels` | Available, but not needed for primitive meshes and solid materials |
| `Unity_AssetGeneration_GenerateAsset` | Available, but outside this milestone's asset needs; do not invoke for the grey-box prototype |

There is no dedicated scene-save, prefab, test-runner or build tool in this
observed list. Use `Unity_RunCommand` with Unity editor APIs and the reusable
helpers specified below. Do not invent `Unity_ManageScene`/`Unity_ReadConsole`
calls just because older package documentation mentions them.

**Start each work session by inspecting the current state.** Read Git status and
the current docs, discover tools, read console errors/warnings, then send the
following through `Unity_RunCommand` in its `Code` argument, with a short `Title`:

```csharp
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = SceneManager.GetActiveScene();
        result.Log("Unity={0}; assets={1}; scene={2}; dirty={3}; play={4}; compiling={5}; importing={6}",
            Application.unityVersion, Application.dataPath, scene.path,
            scene.isDirty, EditorApplication.isPlayingOrWillChangePlaymode,
            EditorApplication.isCompiling, EditorApplication.isUpdating);
    }
}
```

Verify `Application.dataPath` is the intended clone's `Assets` directory. Also
inspect all open scenes and any prefab editing stage before changing scene setup.
Exit Play Mode before persistent asset work. Preserve unrelated dirty scenes;
open/create HQ additively when necessary instead of discarding or saving someone
else's scene. Coordinate shared scene/prefab editing under CONVENTIONS.md.

`internal class CommandScript : IRunCommand` and `Execute(ExecutionResult result)`
are required by this tool. Its parameter keys are `Code` and `Title`. For direct
command mutations, call `result.RegisterObjectCreation(obj)` after creation,
`result.RegisterObjectModification(obj)` before modifications, and
`result.DestroyObject(obj)` for deletion. Check compilation/execution flags and
the execution logs: a command can log an application error and return without
throwing. Treat that as a failure even when the outer tool reports success.

Put persistent gameplay and editor automation in the repository files in section
4. Temporary MCP commands should inspect state or invoke those helpers, not be
the only place where room construction or gameplay logic exists. Reacquire
references after reloads; do not persist session instance IDs as asset identity
or substitute them for FishNet IDs. Use asset paths/GUIDs and verified scene
references for persistent editor work.

**Compilation and interruption recovery:** change source in coherent batches,
let Unity import it, and wait for compilation/import to finish before attaching
new component types. In this package `Unity_GetConsoleLogs` calls
`AssetDatabase.Refresh()` itself, so even a console read may trigger a reload.
The bridge can briefly disconnect during compilation. Reconnect, inspect state
and compiler output, then continue; do not reinstall packages for every timeout.
Do not busy-wait on the editor main thread. Return from commands that start async
work and query completion in later calls.

A timed-out mutation may already have run. Inspect named assets/objects and saved
status before retrying; never blindly run creation, a throw, a test run or a build
twice. Do not close Unity, clear its cache, reset Git or overwrite its settings as
a generic MCP repair. If compilation prevents MCP from recovering, inspect the
project's `Logs/Editor.log` (the current editor redirects there), fix the actual
code error through file tools, and retry. Use the normal editor/CLI fallback only
when needed and report the exact remaining automation gap.

### Versions to try first

Use the existing Unity/editor packages unchanged. Add each network dependency once,
through UPM Git URLs, then commit the manifest and lockfile when committing the
implementation is authorized. Do not install duplicate copies under Assets.
In the connected editor, add dependencies sequentially through Package Manager
or an editor helper using `UnityEditor.PackageManager.Client.Add`. Wait for each
request to finish and its import/compile result before adding the dependent
package. Order: FishNet, Steamworks.NET, then FishySteamworks. Do not run concurrent
UPM writes or interpret a returned request handle as a successful installation.

| Dependency | Initial candidate Git URL |
|---|---|
| FishNet | `https://github.com/FirstGearGames/FishNet.git?path=Assets/FishNet#4.7.3` |
| Steamworks.NET | `https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2025.164.1` |
| FishySteamworks | `https://github.com/FirstGearGames/FishySteamworks.git#4.1.1` |

This combination compiles in Unity `6000.6.0f1`. FishNet resolves to Git commit
`73f30cf2425dc808a4f463f0a233d386010810b3`; its `4.7.3` tag contains package
metadata reporting `4.7.2`. Steamworks.NET resolves to
`c21a8f0e31c56ae8707130967faf491f7dd7c0d8`. FishySteamworks is the unchanged
`4.1.1` source at `21e858249249e2c322365fe9fefbe865f290b0d9` with its MIT license.
Sources: [FishNet installation](https://fish-networking.gitbook.io/docs/tutorials/getting-started/installing-fish-networking),
[FishNet tagged manifest](https://raw.githubusercontent.com/FirstGearGames/FishNet/4.7.3/Assets/FishNet/package.json),
[Steamworks.NET release](https://github.com/rlabrecque/Steamworks.NET/releases/tag/2025.164.1),
[FishySteamworks tags](https://github.com/FirstGearGames/FishySteamworks/tags).

**Stage-zero compatibility check:** import, wait for compilation, verify codegen
and native Steam initialization, and build a minimal host/client scene before
writing the ball system. Inspect the installed source for the API mappings in
section 8. Lock the exact revisions that pass and record them in the implementation
report. Do not leave floating `main`/`master` dependencies.

If a candidate fails on Unity 6.6, capture the exact error and find a compatible
upstream revision within the selected stack. Do not silently downgrade Unity,
switch to NGO/Mirror/Facepunch, suppress real errors, or edit PackageCache. If only
a third-party source patch can fix compatibility, report it as a specific upstream
dependency issue for a decision; continue independent room/input work if useful.
No compatibility success is asserted by this plan.

### Steam and local mode

- Final test: two Windows computers, two different Steam accounts signed in, same
  build revision, same App ID, and network access. No dedicated server is required.
- Use a team-owned development App ID if available and accessible to both testers.
  Otherwise configure test App ID `480`, which the transport documents as a test
  default. Keep the lobby private/friends-only and identify this prototype with a
  project marker; do not assume the shared test App ID gives exclusive discovery.
- The actual App ID/account access is an external input, not a value to invent.
  Put App ID in the prototype connection config and create local
  `steam_appid.txt` as required for development; it is already gitignored.
- Do not promise Steam overlay auto-launch behavior for an unregistered local
  build. The dependable test flow is both players launching the build first,
  then joining using the copied lobby ID or an in-game invite.
- Use FishNet's included Tugboat transport at `127.0.0.1:7770` for two processes on
  one computer. Local mode must skip Steam initialization entirely. A local pass
  is useful evidence but does not verify Steam transport.
- Select exactly one active transport before starting networking. No runtime
  switching while connected; no Multipass requirement for this milestone.

FishySteamworks requires Steamworks.NET and uses the host's **SteamID64** as its
P2P address. Two local Steam builds under one account are not a valid remote test.
[Transport setup and testing](https://github.com/FirstGearGames/FishySteamworks#setting-up).

### One Steam lifecycle owner

Create a small `SteamBootstrap` that owns initialization, regular callback pumping,
and shutdown, using the installed Steamworks.NET sample as the API reference.
Do not also import/instantiate another SteamManager that does the same work.
Check initialization before Steam calls, retain callback/call-result handles,
dispose subscriptions on teardown, and process stale asynchronous results safely.
If initialization fails, show a useful status and retain access to local mode.
[Steamworks.NET lifecycle examples](https://steamworks.github.io/gettingstarted/).

## 4. Folder layout and file responsibilities

All names below are proposed files, not claims that they already exist. Preserve
existing `.meta` GUIDs. Let Unity create new assets and metadata; never guess scene
serialization fields or vendor assembly names from memory.

```text
Assets/_Project/
  Scenes/Prototype/HQPrototype.unity
  Prefabs/Net/PrototypeNetworkRoot.prefab
  Prefabs/Player/PrototypePlayer.prefab
  Prefabs/Interaction/Basketball.prefab
  Prefabs/UI/PrototypeSessionCanvas.prefab
  Settings/Prototype/
    PrototypeSessionConfig.asset
    PrototypePlayerConfig.asset
    PickupConfig.asset
    BasketballPhysics.physicMaterial
    HQPrototypeInput.inputactions
  Art/Prototype/Materials/
    HQFloor.mat, HQWall.mat, BallOrange.mat, BallSeam.mat
    PlayerBlue.mat, PlayerAmber.mat
  Scripts/
    Net/
      PrototypeSessionController.cs
      SteamBootstrap.cs
      SteamLobbyService.cs
      PrototypeAuthenticator.cs
      HQPlayerSpawner.cs
      PrototypeSessionConfig.cs
    Player/
      PrototypePlayerInput.cs
      PrototypePlayerMotor.cs
      PrototypePlayerPresentation.cs
      PrototypePlayerConfig.cs
    Interaction/
      PlayerInteraction.cs
      NetworkPickup.cs
      PickupPhysicsDriver.cs
      PickupConfig.cs
    Prototype/
      HQPrototypeSceneRefs.cs
      HQPrototypeSession.cs
      PrototypeDebugOverlay.cs
      Logic/
        SunkCost.Prototype.Logic.asmdef
        PickupPhase.cs
        InteractionFailure.cs
        PickupRules.cs
        SessionAdmissionRules.cs
    UI/
      PrototypeSessionUI.cs
    Noise/                         # existing; unchanged for this milestone
  Editor/Prototype/
    HQPrototypeBuilder.cs
    HQPrototypeValidator.cs
    HQPrototypeTestRunner.cs
    HQPrototypeBuild.cs
  Tests/EditMode/Prototype/
    SunkCost.Prototype.Logic.Tests.asmdef
    PickupRulesTests.cs
    SessionAdmissionRulesTests.cs
docs/
  HQ_BASKETBALL_IMPLEMENTATION_PLAN.md  # this file
  HQ_PROTOTYPE_TEST_REPORT.md          # created during implementation/testing
Builds/HQPrototype/                    # generated, already gitignored
```

| File/group | Responsibility and allowed state changes |
|---|---|
| `PrototypeSessionController` | Owns local connection state; starts/stops selected transport; binds UI; no ball logic |
| `SteamBootstrap` | Only Steam API lifecycle owner; no network spawning |
| `SteamLobbyService` | Create/join/leave lobby, metadata, invites; hands validated host address to session controller |
| `PrototypeAuthenticator` | Uses FishNet's authentication/broadcast mechanism before player spawn; rejects wrong protocol/session/full admission |
| `HQPlayerSpawner` | Server-only connection-to-player map, scene-ready spawn, spawn slot allocation, disconnect cleanup |
| `PrototypePlayerInput` | Local input instance and cursor/focus gating; emits intent, does not write shared state |
| `PrototypePlayerMotor` | Owner-only CharacterController movement/look and server correction path |
| `PrototypePlayerPresentation` | Local camera/listener enablement; body visibility; server-assigned player color |
| `PlayerInteraction` | Local target selection; owned-player request RPCs; pending UI and result correlation |
| `NetworkPickup` | Server-only pickup state, request validation, ownership transitions and timeouts |
| `PickupPhysicsDriver` | One writer's fixed-step hold/release physics; configures proxy Rigidbody state; no authority grants |
| `HQPrototypeSession` | Server-spawns one ball, host reset, tracks session generation; no economy/save systems |
| `HQPrototypeSceneRefs` | Serialized spawn points, bounds, ball reset pose and scene refs; no hidden global lookup |
| Config types/assets | Editable defaults from section 7, shared asset values used by matching builds |
| `PrototypeSessionUI` | Buttons, status/errors, interaction prompt; binds events and displays state |
| `PrototypeDebugOverlay` | Development diagnostics from existing systems; no network decisions |
| `Logic/` | Pure C# predicates for transitions, duplicates and admission; no Unity/FishNet types |
| `HQPrototypeBuilder` | Explicit editor menu/static entry point to create/wire assets through Unity APIs; callable through MCP |
| `HQPrototypeValidator` | Read-only asset/scene/config checks with specific failure paths; shared by the builder and build command |
| `HQPrototypeTestRunner` | Runs the actual Unity EditMode tests asynchronously and saves their results; no substitute gameplay simulation |
| `HQPrototypeBuild` | Validates setup and calls Unity BuildPipeline for the standalone prototype |

Use namespaces matching folders (`SunkCost.Net`, `SunkCost.Player`,
`SunkCost.Interaction`, `SunkCost.Prototype`, `SunkCost.UI`). Runtime MonoBehaviours
may stay in the existing predefined assembly initially. The small pure-logic
assembly is auto-referenced, has no Unity engine references, and is referenced by
an Editor-only test assembly with test-assembly references enabled. This avoids
trying to reference `Assembly-CSharp` from an asmdef test assembly. Do not move
existing noise scripts into a new assembly just to make these tests possible.
Keep the editor helpers in `SunkCost.Editor.Prototype` under the Editor folder.
Their public static entry points must also work from menus without an MCP client;
do not add `IRunCommand` or other MCP-specific types to runtime assemblies.

Use serialized references for scene/config dependencies and component references
cached on startup. Do not create duplicate NetworkManagers, broad service-locator
frameworks, a new global GameManager, or speculative interfaces for future games.

## 5. Scene, prefabs and inspector wiring

Use **one** `HQPrototype.unity` for this milestone. The room exists in every build
before connecting, while network players and the ball are spawned only by the
server. Keep SampleScene intact; enable only HQPrototype in the prototype build.
All participants are observers of this room; no distance interest management.
Use MCP to invoke the section 13 builder, inspect hierarchy/components and verify
serialized references in the editor. Save the intended HQ assets explicitly, then
reload and validate them; an object visible in the live Hierarchy alone is not a
delivered scene. The earlier cube test did not exercise saving.

### Room

- Unity units are metres. Interior floor spans X/Z `-6..6`; floor surface Y `0`;
  wall height `3.5`. Use floor/walls/ceiling with non-trigger BoxColliders.
- Four spawn markers at `(-3,0,-3)`, `(3,0,-3)`, `(-3,0,3)`, `(3,0,3)` facing
  inward. Player root is feet position; choose an unoccupied slot, never overlap.
- Ball reset/spawn center `(0,1,0)`. It should fall and visibly bounce onto the floor.
- One light and the existing desktop URP configuration; no post-processing work.
- Network root and Canvas are ordinary scene objects. One room preview camera
  covers the room while disconnected; disable it when the local player activates.
- Use the host-only reset button plus bounds recovery, not a scoring trigger.

### Network root prefab

`NetworkManager`, its required managers, one selected transport reference,
`PrototypeSessionController`, `SteamBootstrap`, `SteamLobbyService`,
`PrototypeAuthenticator`, and `HQPlayerSpawner`. Disable vendor demo auto-start
and demo player spawners. Register the player and ball in one explicit spawnable
prefab collection. Preserve identical collection ordering/IDs in both builds.
Wire SceneRefs and config in the scene instance; no scene references saved into
an asset prefab. All references must be checked by the builder's validation step.
Add one ordinary `HQSession` scene object with `HQPrototypeSession` and
`HQPrototypeSceneRefs`, subscribing to the network root's lifecycle; add the debug
overlay to that object. Its server-start handler alone starts ball/session setup.

### Player prefab

- Root scale `(1,1,1)`: `NetworkObject`, owner-authoritative `NetworkTransform`,
  `CharacterController`, the three Player components, and `PlayerInteraction`.
- Capsule controller: height `1.8`, radius `0.3`, center `(0,0.9,0)`;
  step offset `0.25`, slope limit `45`, skin width `0.03`.
- Visual body mesh under root, with a distinct head/forward marker and player
  color. Disable extra primitive colliders; the controller is the player collider.
- `ViewPivot` at `(0,1.6,0)` holds pitch; child Camera and AudioListener are
  enabled only for the local owner. Root rotates yaw, not pitch/roll.
- Hide the local head mesh to avoid clipping, retain the lower body when looking
  down. Remote players have no active camera, listener or input.
- A hold-target child marks `(0.35,-0.25,1.1)` relative to ViewPivot. It is a local
  aiming reference, not a network parent for the ball.

### Ball prefab

- Root scale `(1,1,1)`: `NetworkObject`, `NetworkTransform`, `Rigidbody`,
  `SphereCollider` radius `0.12`, `NetworkPickup`, `PickupPhysicsDriver`.
- Orange sphere child diameter `0.24`; simple seam meshes/material are optional.
- No `NetworkObject` on decorative children; no parent changes when held.
- `Rigidbody` is initially kinematic in the prefab until network initialization
  assigns its simulation role. Non-writers remain kinematic; one writer is dynamic.
- Disable despawning when the client owner disconnects using the installed
  FishNet ownership/despawn settings. Verify this behavior rather than relying on
  a default. Server cleanup must run before ownership-dependent automatic despawn.
- Do not attach the existing NoiseEmitter in this phase: owner-client collision
  callbacks would hit its server-only guard. Sound/monster integration is separate.

### NetworkTransform configuration

Player: client authoritative, position and rotation enabled, scale and parent
sync disabled. Ball: client authoritative when it has an owner and server-driven
when ownerless, position/rotation enabled, scale/parent sync disabled. Verify the
ownerless fallback in the installed implementation before relying on it.

Start with a 30 Hz network tick, send interval one tick, two ticks of interpolation
and no optional/pro-only extrapolation. Keep physics fixed timestep at the existing
50 Hz. Do not call `Physics.Simulate` from network ticks or run a second physics loop.

For both prefabs, disable NetworkTransform's automatic component configuration
and let exactly one project component configure controller/Rigidbody enablement.
This prevents the transform component and custom driver from fighting over
`isKinematic`. Only transform replication/smoothing is the NetworkTransform's job.
Use its verified ownership/teleport facilities for corrections; do not patch its
private fields or run a second custom per-frame transform-RPC system.
[NetworkTransform settings](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/network-transform),
[tagged implementation](https://raw.githubusercontent.com/FirstGearGames/FishNet/4.7.3/Assets/FishNet/Runtime/Generated/Component/NetworkTransform/NetworkTransform.cs).

## 6. Input and minimal UI

Create a project-owned input asset under Settings/Prototype, reusing the template's
UI map shape if helpful. Leave the template asset intact. Each local player owns
its input action instance; remote spawns must not enable it.

| Control | Action |
|---|---|
| WASD | Move, normalized diagonal input |
| Mouse delta | Look, configurable sensitivity; do not multiply mouse delta by deltaTime |
| Left Shift | Sprint |
| E | Pick up aimed ball; while holding, drop it |
| Left mouse button | Throw the held ball with fixed configured speed |
| Escape | Release cursor and open session menu; Resume recaptures it |
| F3 | Toggle development diagnostics |

No jump/crouch/dribble in this prototype. Disable movement/look/interactions while
typing in UI, menu open, focus lost or Steam overlay active. The network simulation
continues; never pause a multiplayer room with `Time.timeScale = 0`. Already-held
ball follows the last valid target while menu/focus gates input.

Use existing uGUI with `InputSystemUIInputModule` and a single EventSystem. Use
simple text/buttons and an available built-in font; no additional UI dependency.
Connection panel: Steam/Local selection while disconnected, Host, lobby ID/IP
field with appropriate label, Join, Copy Lobby ID, Invite Friend (Steam), Leave,
Resume, and Quit. In Local mode host/IP `127.0.0.1`, port from config.

Show connection progress and actionable errors. Connected overlay shows player
count, crosshair and one prompt: `E: pick up`, `E: drop / LMB: throw`,
`Waiting for grab`, `Held by another player`, or `Ball settling`.
Do not show debug IDs/transport internals in normal player prompts. Host reset is
a menu button; guests cannot invoke it successfully even by sending a request.

## 7. Initial tuning values

These are editable prototype defaults, not final balance or measured results.

| Config | Initial value |
|---|---|
| Capacity / network tick | 2 total players / 30 Hz |
| Connection/auth timeout | 20 s / 10 s |
| Walk / sprint speed | 4 / 6 m/s |
| Ground acceleration / deceleration | 20 / 25 m/s² |
| Gravity / grounded downward speed | -20 / -2 m/s² |
| Look sensitivity / pitch clamp / FOV | 0.1 degrees per mouse pixel / -80..80 degrees / 75 |
| Grab distance / server position slack | 2.5 m / 0.5 m |
| Maximum held ball distance from holder root | 3 m; recover on sustained violation |
| Ball mass / radius | 0.62 kg / 0.12 m |
| Ball dynamic/static friction / bounce | 0.4 / 0.4 / 0.65; Average friction, Maximum bounce combine |
| Ball free linear/angular damping | 0.05 / 0.1 |
| Hold spring / damping / acceleration cap | 80 s⁻² / 18 s⁻¹ / 80 m/s² |
| Throw speed / forward bias | 8 m/s / aim direction plus 0.15 upward, then normalize |
| Release position tolerance | At most 0.5 m from server-observed ball; use authoritative fallback on mismatch |
| Rest thresholds / duration | Speed <0.15 m/s and spin <0.5 rad/s, continuously for 0.5 s |
| Settling timeout / handoff acknowledgement timeout | 10 s from release / 2 s |
| Interaction request rate / pending warning | 10 requests/s per player / 2 s |
| Play bounds / hard reset Y | X/Z ±7 m, Y -2..5 m / below -2 m |
| Loose movement envelope | At most 8 m/s over a rolling 1 s window, plus 0.75 m positional slack |

Expose tuning through the listed config assets. Validate finite positive settings
at startup. Use Unity 6.6 APIs such as `Rigidbody.linearVelocity`; confirm any
renamed damping/physics material properties in the installed editor.
[Unity Rigidbody API](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Rigidbody-linearVelocity.html).

## 8. Networking API map and lifecycle

Project method names in this plan are proposed interfaces, not copied SDK methods.
Before using a vendor API, read the pinned package source/signature. Use FishNet
`NetworkBehaviour`, `NetworkObject`, `NetworkConnection`, `SyncVar<T>`,
`ServerRpc`, `ObserversRpc` and `TargetRpc`; never NGO `NetworkVariable`,
`ChangeOwnership(ulong)` or `ClientRpc` syntax.

| Need | FishNet/Steam integration point |
|---|---|
| Start host | `ServerManager.StartConnection()`, then local `ClientManager.StartConnection()` after server reports started |
| Remote endpoint | Active transport `SetClientAddress(...)`, then ClientManager start; Steam endpoint is host SteamID64 |
| Client request | Owned player's `[ServerRpc(RequireOwnership = true)]`, named `ServerRequest...` |
| Response to requester | `[TargetRpc]`, first argument target NetworkConnection, named `Target...` |
| Cosmetic notification | `[ObserversRpc]`, named `Rpc...`; no persistent state carried only by this |
| Shared state | Server-written `SyncVar<T>`; current value initialized for new observers plus callbacks for changes |
| Assign/revoke owner | Server-side `GiveOwnership(connection)` / `RemoveOwnership()` after checking installed overloads |
| Lifecycle | `OnStartServer`, `OnStartClient`, ownership callbacks, stop callbacks; configure role idempotently |
| Spawn | ServerManager spawn with owner for player; without client owner for ball |
| Ready to spawn player | FishNet scene-ready/start-scene-loaded connection event, not socket connection alone |
| Scene membership | Ensure each authenticated connection observes the already-loaded HQ scene before spawning its player |

Use reliable messages for requests, results and ownership handoff control. Let
NetworkTransform use its native replication channels; it is the only continuous
transform sender. FishNet provides an injected sender connection for ServerRpc
validation; a client-supplied client ID is not proof of identity.
[RPC semantics](https://fish-networking.gitbook.io/docs/guides/features/network-communication/remote-procedure-calls),
[connection startup](https://fish-networking.gitbook.io/docs/tutorials/simple/starting-fishnets-connections),
[ownership](https://fish-networking.gitbook.io/docs/guides/features/ownership).

Host runs both client and server callbacks in one process. A callback being called
on the server does not mean that process should simulate a remotely-owned ball.
All physics-role application goes through one idempotent method. Do not enable
two controllers, listeners, input maps, spawners or Rigidbody writers on the host.

### Connection state machine

`Disconnected -> StartingHost/Joining -> Authenticating -> InHQ -> Leaving -> Disconnected`.
Any stage may fail into cleanup followed by `Disconnected` with a retained error.
Steam initialization is a prerequisite for Steam operations, not for local mode.

Have one monotonically increasing local attempt number. Capture it in async
callbacks; ignore old results, leave lobbies created by canceled attempts, and
never let two callback paths start the same client. UI buttons cannot start a
second attempt while the first is active. A timeout/cancel must undo partial setup.

### Player and ball spawning

Server keeps a `connection -> player` map and one `ball` reference per session.
On authenticated scene readiness, allocate a free slot and spawn exactly one
owned player. Guard against repeated readiness callbacks. Release slots on
disconnect. Spawn the ball once when the host's HQ session is ready, never once
per player and never from each client's Start/Awake. Leave the room environment
non-networked and identical across builds.

On client disconnect, reclaim any owned ball first, then despawn the player and
clear map/pending requests. On host shutdown, stop the entire room; no migration.
The host may later start a new session with one new ball and clean player mappings.
Rejoining while this HQ harness is running is supported; this does not authorize
joining during a dive in the full game.

## 9. Steam lobby and admission flow

Steam lobby membership supplies discovery and session metadata; FishNet carries
the actual gameplay connection. Do not send movement or ball transforms through
lobby chat or lobby metadata.

**Host:** create the new session identifier locally, initialize Steam, and choose
FishySteamworks with P2P enabled; start server and local client; on successful HQ
setup create a friends-only lobby with member
limit two. Advertise the project marker `sunkcost-hq-prototype`, protocol integer,
build identifier, original host SteamID64, that session identifier and `ready=1`.
Publish ready only after all required values and the room are ready. Failed lobby
creation cleans up the partially started Steam session and reports the reason.

**Join:** user pastes a decimal lobby ID or accepts an invite while the build is
running. Join the Steam lobby; check result, metadata, project marker, matching
protocol/build, ready state and capacity. Use the advertised original host
SteamID64 as FishySteamworks address. Never pass the lobby ID as the host address.
Wait briefly for metadata updates if not yet available, within the connection
timeout. Reject invalid/missing metadata rather than connecting to a guess.

**Important:** creating a lobby also generates a lobby-enter event. The host's
enter handler must not start a second local client. Steam can automatically choose
a new lobby owner when the host leaves; that is not game host migration. If the
original game host has left, clean up and return guests to the connection screen.
[Valve lobby lifecycle](https://partner.steamgames.com/doc/api/ISteamMatchmaking).

Register `LobbyCreated_t`, `LobbyEnter_t`, `LobbyDataUpdate_t`,
`LobbyChatUpdate_t` and `GameLobbyJoinRequested_t` using the proper callback versus
CallResult mechanism for each operation in the installed binding. Keep handles
alive. The Invite button opens the Steam invite overlay for the current lobby;
copied lobby ID remains the manual fallback. If `+connect_lobby` is provided at
startup, queue it until initialization; don't depend on automatic startup working
under the shared test App ID. [Valve invite flow](https://partner.steamgames.com/doc/api/ISteamMatchmaking#InviteUserToLobby).

**Admission:** FishNet's authenticator validates a small reliable handshake before
player spawning: project marker, protocol version, build identifier and session ID.
Allow only two total game connections, including host; reserve slots atomically
and time out unauthenticated attempts. Lobby size alone is not the connection cap.
In Steam mode, verify lobby membership against the actual transport peer Steam
identity. In the inspected FishySteamworks `4.1.1` source,
`GetConnectionAddress(connectionId)` returns the stored peer Steam ID as text;
parse it as an unsigned 64-bit value and compare against current lobby members.
Reject an empty/invalid mapping; do not trust a caller-provided Steam ID.
Authenticate the server's own local client by its known loopback connection and
locally generated session ID, without waiting for lobby membership: lobby creation
happens afterward. Never apply that exception to a remote connection claiming to
be the host. [Transport peer mapping](https://raw.githubusercontent.com/FirstGearGames/FishySteamworks/4.1.1/FishNet/Plugins/FishySteamworks/Core/ServerSocket.cs).

FishySteamworks' remote socket count excludes its special local host connection;
the game admission layer must still enforce two total players. Do not identify
host as client ID zero. Verify these details again if changing the candidate tag.
Local mode explicitly skips Steam identity checks but still checks protocol and
capacity. No production anti-cheat or Internet lobby browser is in this milestone.

**Leave/failure:** cancel the attempt, stop the client (and server when hosting),
leave the lobby, unsubscribe session callbacks, clear the current IDs and spawned
references, restore the preview camera/cursor/UI. Dispose Steam only when leaving
Steam mode or exiting the app; do not repeatedly initialize it per player spawn.

## 10. Player movement and interaction selection

Owner reads input in Update, computes yaw/pitch, accelerates horizontal velocity
toward normalized wish direction, applies gravity and calls CharacterController
Move. Use deltaTime for movement; preserve downward velocity when grounded.
NetworkTransform replicates root position/yaw. Remote bodies need no animation or
networked mouse cursor. Owner-only view pitch suffices for this milestone; release
requests include a validated direction so the ball can be thrown up or down.

Server tracks a rolling history of accepted positions. Reject non-finite/out-of-
bounds positions and implausible deltas under the configured loose envelope.
Corrections go through one targeted path to the owner, temporarily disabling the
CharacterController for repositioning, clearing motor velocity and resetting
transform interpolation using supported APIs. Ignore pre-correction samples for
the acknowledged correction revision; don't repeatedly correct an old buffered
pose. This is basic sanity/correction, not rollback prediction or perfect anti-cheat.

Local selection raycasts from the camera through crosshair, up to grab range.
Use a mask that includes **world obstacles and pickable objects**, ignore triggers
and the local player's colliders, and accept only the first visible hit if it is
the ball. Never raycast only the ball layer and thereby grab through walls.

Server independently checks range from its player head/root and a clear path to
the ball, using a bounded positional slack for replication delay. Check request
player identity, target spawn status, phase, per-player held slot and room state.
There is one ball slot, not a general inventory system. The client never attaches
or applies a throw impulse before approval.

## 11. Ball state and request protocol

Store persistent server state as one `SyncVar<PickupSnapshot>` (a project struct),
so mode/holder/revision do not require guessing the order of separate callbacks.
Snapshot fields: phase, holder connection ID or explicit none, controlling client
ID or explicit none, monotonically increasing ownership epoch, state revision,
last accepted action token, transition pose/linear/angular velocity where needed.
Use explicit `hasHolder/hasController` flags or `-1` sentinels; connection ID zero
may be valid. Network object identity is a NetworkObject reference or FishNet ID,
never Unity instance ID and never a SteamID compressed into an int.

`PickupSnapshot` is part of `NetworkPickup.cs` initially; split it into its own
file if it becomes more than a compact data definition. Enums/pure predicates go
in the listed Logic assembly. Server maintains per-connection request counters
and one outstanding interaction slot; clients keep matching local pending tokens.

| Phase | Authoritative writer | Can a new grab succeed? |
|---|---|---|
| `FreeServer` | Server Rigidbody | Yes, after normal range/eligibility checks |
| `Granting` | Nobody advances physics while ownership handshake completes | No |
| `HeldClient` | Granted client's dynamic Rigidbody with hold drive | No |
| `ReleasedClient` | Releasing client's dynamic Rigidbody, gravity/bounce | No, including re-grab by same client |
| `Returning` | Old writer freezes; server starts after snapshot/revocation | No |

All listed RPCs live on the player's owned `PlayerInteraction` behaviour and
delegate to the target's server `NetworkPickup`. This permits a player to request
a free ball without owning that ball. Do not set `RequireOwnership=false` on all
ball methods as a shortcut. The server derives identity from the owned player and
injected connection, not a supplied holder ID.

| Proposed request | Payload/validation | Result |
|---|---|---|
| `ServerRequestGrab` | target, request ID, expected epoch; server eligibility and line-of-sight | grant/reservation or failure enum |
| `ServerRequestRelease` | target, request ID, epoch, Drop/Throw enum, finite normalized direction, bounded pose | one accepted release snapshot or denial |
| `ServerRequestRest` | target, epoch, finite motion snapshot; only current releasing writer | validated return, or keep simulating |
| `ServerRequestHandoffAck` | target, epoch, server handoff token, frozen snapshot | finish matching return only |
| `ServerRequestGrantAck` | target, epoch after owner+state applied | finish granting or ignore stale ack |
| `ServerRequestResetBall` | host player only, target/current epoch | coordinated reset; denied to guest |

Use `TargetInteractionResult` for correlated success/failure and optional
`TargetPrepareHandoff` for freeze requests. Request IDs are local monotonic
counters scoped to connection/session. Epoch identifies the current grant;
revision/token distinguishes multiple actions within that epoch. Duplicate requests
return the previous result or no-op safely, never replay physics. Stale messages
from an old connection/session or ownership epoch cannot affect the current ball.

Failures are enums: `InvalidTarget`, `OutOfRange`, `Blocked`, `AlreadyHeld`,
`Settling`, `Busy`, `StaleRequest`, `NotAllowed`, `RateLimited`, `InvalidMotion`.
Map to short UI text. A missing response after two seconds shows a warning but
does not let the client decide it owns or released the ball. Use a status refresh
or connection failure path; do not blindly repeat a throw with a new request ID.

### Grab sequence

1. Client shows pending feedback and sends one request; ball remains where it is.
2. Server validates and atomically reserves both player slot and ball. Increment
   epoch, enter `Granting`, freeze the server Rigidbody and capture its pose/motion.
3. Server records the designated controller and grants FishNet ownership. It sends
   the authoritative transition snapshot. Ownership and state may arrive in either
   order; local driver waits until both identify this client and this epoch.
4. Designated client applies the starting pose and role, acknowledges readiness.
   Server changes phase to `HeldClient`; the owner starts the hold drive when that
   phase is received. Other peers interpolate and remain kinematic throughout.
5. On grant failure, disconnect or acknowledgement timeout, use the return/revoke
   path to recover the ball and clear the player's slot. Never leave it reserved.

### Drop and throw sequence

1. Owner sends release intent. It keeps holding until accepted; no local predicted
   impulse. Ignore a second click while that request is pending.
2. Server validates holder/epoch and finite direction/pose. Use a bounded current
   ball pose; prevent release through walls. Reject non-finite/excessive values.
3. Server clears the held slot, enters `ReleasedClient`, and publishes a new action
   token plus accepted release pose and velocities. Owner remains unchanged.
4. Owner applies the release once after receiving matching phase/token. Drop uses
   zero added throw velocity; throw uses the configured direction/speed. Add bounded
   player movement velocity if configured, derived/validated from movement history.
   Remove hold forces, restore free gravity/damping, and keep simulating bounces.
5. No peer independently replays this impulse from a cosmetic RPC. Peers show the
   NetworkTransform stream. Incoming observers see current phase and transform;
   they must not replay an old throw transition just because it is in the snapshot.

### Rest, return and timeout

The releasing owner tracks continuous low velocity/spin and requests return after
0.5 s below thresholds. Server verifies sender/epoch, bounds, observed position
history and plausible submitted velocities. Server's proxy is kinematic, so its
Rigidbody sleep flag/velocity are not proof that the owner's body is at rest.

Return is a freeze-and-ack handshake: server enters `Returning` and sends a unique
handoff token; old owner disables hold/physics writing, freezes its body and sends
final pose/motion with that token. Server validates the snapshot, removes client
ownership, applies the snapshot and becomes the only dynamic writer, then publishes
`FreeServer`. Old owner's later transform traffic must be rejected by the active
owner check in the installed NetworkTransform; verify that path during testing.

At the 10-second release timeout, request the same handoff even if still moving;
preserve velocity/spin in the transfer instead of stopping the ball in mid-air.
At a 2-second handoff timeout with an unresponsive still-connected writer, stop
that connection before server takeover, reclaim from the latest validated pose,
and report why. Do not enable server simulation while knowingly leaving an old
connected writer active. On actual disconnect, server takes over immediately;
the disconnected process can no longer publish accepted state.

Use a recent bounded transform history to estimate momentum when no final
snapshot is available; zero spin is an acceptable disconnect fallback. A slight
visual correction is acceptable, duplicate balls and permanent ownership are not.
Host-local ownership must run these same logical transitions once without waiting
for a second physical machine inside the same process.

## 12. Physics, collision and recovery details

**One role switch:** `PickupPhysicsDriver` has a single method that derives its
role from current network ownership, phase and transition readiness. Call it from
start/stop, ownership and state callbacks; it must be safe to call repeatedly.
Do not use `IsOwner || IsServerStarted` as the writer test: that makes the server
simulate a remote owner's ball too. Server writes only in `FreeServer`; designated
client writes only in `HeldClient`/`ReleasedClient` with matching ownership/epoch.
`Granting`/`Returning` freeze motion until the handshake permits the next writer.

**Held motion:** keep the root unparented and the owner's Rigidbody non-kinematic.
Each FixedUpdate applies a damped spring toward the owner hold target, with bounded
acceleration: `a = clampMagnitude(k * positionError - d * velocity, maxAcceleration)`.
Use acceleration mode, not an unbounded impulse every render frame. Disable
gravity while held, restore it at release. Don't simultaneously set Transform
position and run Rigidbody forces. No joint to the CharacterController is needed.

Spherecast from the player chest toward the desired hold position against world
geometry, shorten the target to keep the ball outside walls, and add a small skin
margin. Hold tension can lag visually under a quick turn; wall penetration is not
acceptable. If server-observed distance exceeds the configured bound continuously
for 0.5 s, cancel the hold via coordinated return and surface a debug reason.

**Collision layers:** allocate unused named layers `HQWorld`, `HQPlayer`,
`HQPickable`; don't hardcode numeric layer indices. World collides with players
and ball. Disable player/player collision to prevent body-blocking in this harness.
Disable player/ball physical collision for this milestone so a CharacterController
can't shove a remote kinematic proxy differently on each machine. Ball still
collides/bounces with world. Picking uses queries, not physical player collisions;
configure query masks explicitly. This is a prototype simplification, not a change
to the final game's carrying design.

Active writer: dynamic Rigidbody, continuous collision detection appropriate to
a fast sphere, local Rigidbody interpolation. Other peers: kinematic, gravity off,
no local physics interpolation (NetworkTransform already smooths them). Restore
free damping/material values on release; don't leave held settings on the ball.
When freezing, snapshot velocities before setting kinematic. Never set velocity
on a kinematic proxy and expect it to move.

**Reset:** host menu requests reset through the server. If client-owned, obtain a
handoff first; then clear held slot, invalidate old action tokens, reset pose to
`(0,1,0)`, set velocities to zero, and resume `FreeServer`. Reuse the same spawned
ball and network identity. Notify peers via current state/teleport sync. If out of
bounds or below Y -2, server initiates the same reset automatically. Rate-limit
reset requests and ignore stale acknowledgements. An in-bounds ordinary bounce
must not trigger a reset.

**No gameplay noise hookup yet:** keep ball physics tests independent of monsters.
Do not change NoiseSystem.IsServer or emit noise locally just to silence its guard.
When sound AI is introduced later, route validated impact events through the
server and solve source identity separately; the zero-source scaffold is not a
reason to attach a client-emitting noise component now.

## 13. Deterministic project setup and build outputs

Implement an explicit editor menu such as `Sunk Cost/Prototype/Create HQ`.
`HQPrototypeBuilder` creates the named folders/configs/materials/prefabs/scene,
assigns references, registers network prefabs, configures layers and calls the
shared validator. Build the room through Unity editor APIs; do not hand-author
hundreds of YAML references. The command must preserve assets/GUIDs on rerun and
must not overwrite a teammate's modified scene silently. Validate-or-update known
generated objects; require a deliberate rebuild action for destructive resets.

### Editor helper interfaces and MCP sequence

Implement these public static entry points in `SunkCost.Editor.Prototype` and
expose equivalent editor menu actions. These are proposed project methods to
write, not existing Unity or MCP APIs:

| Entry point | Result to verify |
|---|---|
| `HQPrototypeBuilder.CreateOrUpdate()` | Expected assets/objects created or updated once; exact paths and changes reported |
| `HQPrototypeValidator.Validate()` | Specific check results and failure paths; throws/fails the caller when required setup is invalid |
| `HQPrototypeTestRunner.RunEditMode()` | One tracked test run started; completion comes from TestRunner callbacks and saved XML |
| `HQPrototypeBuild.BuildWindows()` | A checked BuildReport and complete artifact at the reported output path |

Support an explicit stage argument for validation and intermediate development
builds: stages 0/1 must not fail merely because the later ball system is absent.
Report which stage was checked and its missing future deliverables. The default
final validation/build checks the complete milestone; an intermediate build
cannot satisfy final acceptance.

After compiling the helpers, invoke them through the standard CommandScript
wrapper, for example `SunkCost.Editor.Prototype.HQPrototypeBuilder.CreateOrUpdate();`.
Inspect actual return/status output before advancing. Keep each action bounded;
do not combine package installation, script creation, prefab wiring, tests and
building into one giant MCP command that must survive several domain reloads.

1. **Inspect:** confirm Edit Mode, project path, compilation state, current assets,
   dirty scenes and prefab stage. Capture baseline console output. Decide which
   stage's assets are ready to construct; do not reference uncompiled scripts.
2. **Create/update assets:** use `AssetDatabase` for folders, settings/materials
   and saved asset references; `PrefabUtility` for prefab assets/instances;
   `EditorSceneManager` for the HQ scene. Use the installed Input System API for
   the input asset and verified FishNet APIs for spawnable collection wiring.
   Use serialized fields/`SerializedObject` where appropriate, not guessed YAML.
3. **Preserve edits:** find existing assets by stable paths and preserve GUIDs.
   Update only builder-owned fields/objects; retain user tuning and manual edits
   unless the active request asks to change them. Fail on an unexpected conflicting
   asset instead of overwriting it. Register scene edits with Undo and record
   prefab-instance changes; editor helpers must implement this themselves since
   an outer MCP wrapper cannot track all nested mutations automatically.
4. **Validate, save and reload:** run structural validation, save intended prefabs
   with `PrefabUtility.SaveAsPrefabAsset`, save changed project assets, and call
   `EditorSceneManager.SaveScene` for HQ at its explicit path. Check return values.
   Do not save all unrelated open scenes. Reload HQ without losing other unsaved
   work, reacquire references and rerun validation against the saved result.
5. **Inspect presentation:** use a targeted 3D SceneView capture to check room
   dimensions, spawn spacing and ball placement, then a camera capture for the
   player/preview view and UI. Obtain current object/camera IDs just before use.
   Capture tools are available but not yet tested here; report a failure and use
   editor inspection if necessary. Images verify presentation, not ownership.
6. **Check the diff:** confirm the expected `.unity`, `.prefab`, `.asset`, input,
   settings and `.meta` files exist on disk. Check missing-script/null-reference
   errors and rerun the builder once to prove it creates no duplicates, preserves
   GUIDs/tuning, and produces no unexpected serialized changes.

Do not pass an empty Play Mode scene or transient Hierarchy-only changes off as a
saved deliverable. Leave the editor showing the intended saved HQ scene when the
stage is ready for review. Preserve the user's existing SampleScene state.
For runtime tests, load only HQ: unrelated additive scenes can introduce extra
cameras, listeners or physics. If preserving a dirty unrelated scene prevents
that, resolve its save/close state with the user or test the standalone HQ build.

The validator checks: compilation/import completed, all expected components/references,
exactly one network root/EventSystem, owner-only camera defaults, no demo spawner,
both network prefabs registered, configured owner-disconnect persistence, masks,
config ranges, correct prototype build scene, no missing scripts, and supported
native Steam library import settings. It must fail with specific asset paths.
It checks required settings, not behavioral claims: actual server takeover on
disconnect and single-writer physics still require the section 16 runtime tests.

### Tests and builds from the connected editor

The installed Unity Test Framework exposes `TestRunnerApi.Execute`,
`RegisterCallbacks`, `ICallbacks.RunFinished` and `SaveResultToFile`. Verify its
current signatures before implementing `HQPrototypeTestRunner`. Select the
prototype's EditMode test assembly explicitly. Keep the runner/callback objects
alive until completion, reject overlapping runs, and save XML under
`Logs/HQPrototype/` with the run ID, expected test count, pass/fail/skip counts and
completion status. Zero discovered tests is a failure for the required suite.
An MCP command that merely starts a test run has not passed it.

Start the test helper through MCP, return control to Unity, and read its completion
status/results in later calls. Do not block Unity's update loop waiting for its
own callbacks. Keep any reload/recovery status in editor-only storage, not in a
runtime game service. Record unfinished or interrupted runs as such. Use the
normal Test Runner window or matching Unity command line if the bridge cannot
run the helper; retain actual results rather than replacing tests with mocks.

For a CLI test/build fallback, close the same project's editor after preserving
unsaved work, or use an independent checkout and Library directory. Do not launch
a second Unity editor on `C:\Dev\SunkCost` while it is already open. A standalone
player build can run beside the editor; it does not open a second project editor.

Build target: **Windows x86-64 Development Build**, Mono backend initially to keep
setup small. Use the installed editor's supported Windows module/backend. IL2CPP
can be validated later; do not label an untested backend supported. Enable logs
and Run In Background for two-process local testing. The build must explicitly
include HQPrototype and matching configs; no need to delete SampleScene.

`HQPrototypeBuild.BuildWindows` should validate, call BuildPipeline, check
BuildReport success, and write to `Builds/HQPrototype/`. Accept an optional output
argument for separate test builds, validating it stays inside the intended build
directory. Never delete arbitrary user paths during a build.
Invoke the helper from MCP once the editor is idle and the validation/tests pass.
BuildPipeline can occupy the editor long enough for a tool timeout. Preserve a
build invocation ID and completion result in `Logs/HQPrototype/`; after the editor
responds, inspect the BuildReport-derived status and output before retrying.
Do not claim success from an EXE left by a previous build. Match the artifact's
identifier to this invocation and verify its required files.

Distribute the **whole** build folder, not just the EXE: executable, Data directory,
UnityPlayer/native libraries, and the Steam native DLL selected by the package's
Windows x64 import settings. For developer testing include the agreed local App ID
file as needed; do not commit it or package it for a customer release. Both testers
must see the same build identifier and Git revision in the connection screen.
Generate a build identifier when producing the artifact and embed it in its
configuration; share that exact artifact with both testers. A Git hash alone is
insufficient when the working tree contains uncommitted code. Separate rebuilds
may deliberately receive different identifiers and be rejected; copying one build
to both machines avoids ambiguity. Local Editor/build tests use an explicit
shared development build identifier, recorded as such in the test report.
Keep generated builds,
logs and caches out of Git.
[Steamworks.NET installation/runtime files](https://steamworks.github.io/installation/).

Put setup/version/test evidence in `docs/HQ_PROTOTYPE_TEST_REPORT.md` when building
is authorized. Record resolved dependency commits, Unity version, backend, build
revision, exact launch commands or clicks, machine roles, results and failures.
Do not put credentials or authentication tickets in that report.
Include MCP connection/inspection results, saved-scene validation, meaningful
captures, test-result paths and build completion evidence. Keep generated logs,
XML and captures in ignored output directories; put concise findings and evidence
locations in the tracked report rather than committing editor caches.

## 14. Ordered implementation stages and gates

These are work units for the implementing AI, not a calendar promise. Do not
advance by marking a gate passed without its evidence. Continue independent work
when a missing external input blocks one stage, but retain the unmet gate.

### Stage 0 — Preflight and documented decisions

Check current Git state against this baseline; read shared docs; inspect installed
tools/editor. This plan is currently on the user-requested
`codex/hq-basketball-plan` branch in `C:\Dev\SunkCost`; preserve it. When implementing,
follow the active branch/folder instruction. If a fresh implementation branch is
requested, use a name such as `codex/hq-basketball-prototype`. Verify the MCP tools
in the current task, read console output and inspect the correct project and
unsaved state using section 3. MCP setup has already passed on this machine;
reconnect as needed instead of repeating installation. Record the proposed
defaults and their contract implications for teammate review. Inspect the dependency candidates,
install/pin once, and verify compilation/codegen in Unity 6000.6.0f1.

**Gate:** verified editor connection or a documented working fallback;
reproducible clean compile with exact dependency revisions recorded;
no duplicate Steam/FishNet assemblies and no unapproved stack/editor migration.

### Stage 1 — HQ room and local player experience

Create configs, input, room, UI, player prefab and repeatable editor builder.
Use MCP to run the builder and validator, save/reload HQ and its prefabs, and
inspect the room/camera as specified in section 13. Check the resulting Git diff.
Test walk/look/cursor/focus/collisions/body visibility. Use a **local host through
FishNet** for the player, even when alone, instead of creating a second standalone
controller that must be networked later. No ball interaction implementation yet.

**Gate:** one locally owned player in HQ, one camera/listener, working controls,
validated saved assets, a repeatable builder, inspected presentation and no
compile/runtime errors from project scripts.

### Stage 2 — Two network players

Implement session state, chosen transport selection, authentication/admission,
scene-ready spawning and disconnect cleanup. Use local mode first with two
processes. In local authentication, server sends a fresh session challenge so a
joining client can echo the current session ID; do not require it to guess an ID
only Steam lobby metadata would have provided. Then implement Steam lifecycle,
lobby flow and invitations/manual lobby-ID join.
Use the connected editor for one peer and a standalone build for the other, or
two builds. MCP only targets this editor, so also collect the separate player's
log and observations. Query current roles/counts via MCP for diagnostics; exercise
normal UI/request paths for gameplay instead of granting ownership from editor
commands to make a test appear successful.

**Gate:** host and separate client see each other's movement and colors; only
their own input/camera responds; wrong build/full session fails cleanly; repeated
join/leave doesn't spawn duplicates. Obtain an actual two-machine Steam connection
result when testers are available; local success is separately labelled.

### Stage 3 — Server ball and basic ownership transfer

Spawn one ball once. Implement pure pickup rules, request validation/pending UI,
server arbitration, grant acknowledgement and dynamic/proxy roles. Add held spring
motion and wall obstruction tests. Start with drop, then add the fixed throw.

**Gate:** both peers can take turns carrying/dropping/throwing; no attach before
grant, no duplicate ownership, no physics impulses on observer clients. Local
host follows the same contract as remote holder. Verify a rejected grab has UI
feedback and does not leave the player slot reserved.

### Stage 4 — Handoff and lifecycle failures

Implement released/rest state, freeze acknowledgement, timeout return, reset,
disconnect takeover, epoch/token rejection and late-observer initialization.
Do not treat a late join's current snapshot as a new throw command.

**Gate:** all lifecycle tests in section 16 pass locally; the same ball survives
owner disconnect; it always becomes grabbable again after handoff; no stale motion
packet reverses a reset or creates a second simulation writer.

### Stage 5 — Build, remote test and review

Build from the documented process, share the complete artifact with testers, run
the two-machine Steam checklist, and read both logs. Apply the repo's
`netcode-reviewer` checklist, fixing concrete defects without speculative rewrites.
Get teammate review for network/contract changes under the team workflow.
Run the section 15 tests using the editor test helper and keep their completion
results. MCP can build and inspect the local project; it does not operate the
friend's computer or prove that a Steam connection succeeded there.

**Gate:** the required manual tests have actual evidence; unresolved issues are
listed with reproduction steps. Report separately: implementation completed,
local validation passed, Steam validation passed/pending, and human review status.
Only commit/push/merge when the active implementation request authorizes it.

## 15. Automated verification worth writing

EditMode tests target real decision logic, not MonoBehaviour implementation details.
Use the Logic assembly for deterministic functions over plain values:

- Two requests against the same free-state snapshot: after applying one accepted
  transition the second is denied; only one player owns the held slot.
- Grab fails for wrong phase, excessive distance, blocked line of sight, invalid
  target/session, or already occupied held slot.
- Release from a non-holder or wrong epoch is denied; duplicate release cannot
  increment state/apply a second impulse; a stale release after reset is ignored.
- Rest requires both thresholds for the entire configured interval; a bounce
  resets the interval; 10-second timeout selects return even without rest.
- Old handoff/grant tokens cannot change a newer epoch/session.
- Capacity counts host and in-progress reservations; slot freed exactly once on
  cancellation/disconnect; mismatched protocol/build rejected.
- Repeated readiness events select an existing player rather than another spawn.

Test actual runtime wiring with the manual integration checks below. A mocked
Unity/FishNet stub harness does not establish real package compilation or networking.
Read Unity's compilation/test outputs and any IL postprocessor errors. Do not
claim the code compiles based solely on a standalone C# compiler without Unity.
Use the MCP/test-helper process in section 13 and record the actual test run ID
and result file. `Unity_RunCommand` compiling its own wrapper is not proof that
all game scripts/codegen or a standalone player build have compiled. Console
requests are filtered and capped at 200 entries in this package; inspect the
relevant Editor.log/test XML when output is truncated or a complete check is needed.

## 16. Acceptance checklist for you and a friend

Record test IDs and results in the report, using **Pass / Fail / Not run**.
Use the same revision on both peers. Obtain logs from both, not only the host.
MCP may inspect the editor peer's objects, diagnostics and view while these tests
run. It does not supply real keyboard/mouse focus tests or the other process's
observations automatically. Have the testers exercise the listed controls unless
a separate, documented integration harness actually drives those same paths;
record automated checks and human playtests separately. Do not use editor writes
to SyncVars, ownership or Rigidbody state as substitutes for the request protocol.

### Required local integration tests

| ID | Procedure | Pass condition |
|---|---|---|
| L01 | Start local host, then separate local client | Exactly two players, one ball, correct owner-only input/cameras |
| L02 | Move/turn each player, open menu, type, alt-tab | Other peer sees motion; UI doesn't cause movement/throw; session keeps running |
| L03 | A grabs/carries/drops, then B repeats | One ball; same held/free state; grant precedes motion; each can pick up after handoff |
| L04 | A throws against floor/wall, wait for rest, B grabs/throws | Same visible trajectory within replication delay; no penetration/duplicate impulse; eventual handoff |
| L05 | Both press grab at the same time, repeat 20 times | Exactly one success per round; loser gets a clear response; no stranded pending state |
| L06 | Grab through a wall or out of range | Server denies it even if a client sends the request |
| L07 | Disconnect current holder and repeat during a throw | Ball persists at plausible recent pose, server controls it; remaining player can recover it |
| L08 | Host grabs/throws while remote observes, then reverse roles | No host double-simulation; both directions behave consistently |
| L09 | Guest joins while host holds ball, then while ball is moving | Correct current phase/pose, no replayed throw, no second ball |
| L10 | Reset ball while free, held, moving, and out of bounds | Same network object reset once; old requests/packets cannot undo reset |
| L11 | Keep ball moving past settling timeout | Coordinated handoff occurs with momentum; ball not permanently locked |
| L12 | Leave and rejoin five times; stop and restart host | No leftover avatars, listeners, lobbies, slots or duplicate callbacks |
| L13 | Third client tries to enter a full two-player room | Clean rejection; existing two-player session unaffected |
| L14 | Wrong build/protocol, malformed lobby ID, no Steam running | Clear failure, cleanup and usable retry/local mode |
| L15 | Simulate 150 ms RTT and about 2% packet loss where supported | Repeat grabs, throws, disconnect and reset without persistent divergence |

For L15 record the tool and settings; don't confuse 150 ms RTT with 150 ms each
direction. If simulation tooling is unavailable, mark this test Not run and use
real remote observations without inventing a latency figure. Avoid arbitrary
"pixel-perfect" trajectory criteria: brief interpolation lag is expected, lasting
state disagreement, tunneling, duplicate objects or broken ownership are failures.

### Required Steam acceptance on two computers

1. Both sign into separate Steam accounts and launch the same Windows build with
   the same development App ID. Confirm matching revision in the connection UI.
2. A hosts. B joins using the copied lobby ID. Repeat once using the in-game invite
   flow if the Steam overlay is available; record any test-App-ID limitation.
3. Verify both bodies, movement and facing direction. Run L03, L04, L05 and L07
   across Steam. Reverse host/client roles and repeat holding/throwing.
4. Test guest leave/rejoin, then host quit. Guest returns to the connection screen
   with a host-left message; Steam lobby ownership must not resurrect the session.
5. Play for at least 10 minutes: alternating grabs/throws, wall bounces, menu use
   and resets. Both logs must be free of recurring project errors or invalid RPCs.
6. Save results with actual connection mode/latency if available. If Steam fails,
   record both logs and the failing stage: initialization, lobby entry, transport,
   authentication, scene observation or spawning. Do not reclassify local success
   as successful remote play.

**Definition of done:** the two friends can perform the promised actions over
Steam, required checks have recorded outcomes, and failures affecting that promise
are fixed. Code delivered without available remote testers is a completed local
implementation with **Steam acceptance pending**, not a verified finished feature.

## 17. Diagnostics and common traps

Development overlay: build/protocol, transport, local role/client ID, active player
count, RTT when available, ball network ID, assigned client owner or none, phase,
holder, epoch/revision, local simulation role, latest request/result, handoff timer.
Log transitions with session ID and monotonic time/tick; rate-limit repeated
warnings. Show unavailable values as unavailable, not zero. Do not log Steam auth
tickets, secrets or unnecessary personal data.

| Symptom | First checks |
|---|---|
| MCP is configured but tools are absent in the task | Reload/restart the AI client, discover tools and test a console read; configuration is not proof of connection |
| MCP stops responding after script changes | Import/compile/domain reload, current Editor.log and bridge status; inspect before retrying mutations |
| Objects were visible but missing after reopening | Scene/prefab was not saved, wrong asset path/scene, or changes existed only in Play Mode |
| Test/build tool timed out | Inspect the tracked invocation's completion status, XML/BuildReport and artifact before starting another run |
| Two local players or two balls | Demo spawner, duplicate scene-ready/lobby-enter callback, client-side Instantiate |
| Remote player moves with my keyboard | Input map and motor enabled on non-owner |
| Camera jumps/listener warning | Multiple active cameras/listeners; ownership init order |
| Ball jitters only when guest holds it | Server still dynamic, two components setting kinematic state, proxy interpolation doubled |
| Ball snaps back after release/reset | Stale epoch/token or old transform stream; missing handoff snapshot/reset |
| Ball disappears on disconnect | Ownership despawn setting/cleanup ordering |
| Forever waiting to pick it up | Owner never relinquishes, rest tested on kinematic server proxy, timeout missing |
| Connects but sees no player/ball | Auth/scene-ready ordering, observer scene membership, prefab registration mismatch |
| Local works but Steam doesn't | Different App IDs/revisions, Steam init, wrong endpoint ID, native DLL, lobby readiness or admission |
| Ball stays behind a wall or leaps through it | Missing hold spherecast, query mask ignores obstacles, forces too large, collision detection |

Use `.claude/agents/desync-hunter.md` as a checklist: establish both peers' state,
trace authority and data flow, then change one coherent cause and retest. Do not
expand into unrelated refactors when the protocol or a prefab setting is wrong.

## 18. Handoff instructions and completion report

The implementing AI should receive this file with repository access and an explicit
instruction such as: "Implement this HQ prototype plan in Sunk Cost. Start with
preflight, use the connected Unity MCP tools and the repeatable editor helpers,
and continue through the staged gates. Preserve the game's existing
stack and rules. Report what you actually verified and what needs our two-PC test."

Before editing, reread the current repo: this baseline may have changed. Preserve
the emitter fix and other teammates' work. Do not create a new Unity project.
Implement the proposed file responsibilities without turning every method into a
separate abstraction. If changing a suggested filename makes the code clearer,
keep the same responsibilities and update the handoff references.

The final implementation report must include:

- How to open the HQ scene, regenerate/validate setup, and build/run it.
- MCP tools actually used, saved scene/prefab checks, and any editor automation
  that needed a fallback; separate command success from verified saved results.
- Exact resolved package revisions and any candidate compatibility failures.
- Final file layout, controls, config locations and deliberate prototype limits.
- Tests executed with results, including separate local/Steam/human-review status.
- Build artifact location and the full folder testers need to copy.
- Remaining issues, precise reproduction/log evidence, and any external input
  still required. No invented App ID, teammate approval, elapsed estimate or test.

This revision changes only this Markdown plan. The separate MCP setup and cube
smoke test are recorded in section 2; the test cube was removed. The task branch
already exists. HQ/gameplay files, editor helpers, test reports and build outputs
listed here remain future implementation deliverables. No gameplay implementation,
package installation, commit or push is performed by this plan update.
