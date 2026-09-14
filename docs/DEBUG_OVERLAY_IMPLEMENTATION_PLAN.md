# Sunk Cost: network debug overlay implementation handoff

**Status: implemented and verified on 13 September 2026.** The implementation is
on branch `dan/debug-overlay`. It was originally prepared against `main` at
`7fd160f` in `github.com/PlaySunkCost/SunkCost`. Owner: Dan (Notion task
"Debug overlay — ownership, client ids, RTT, authoritative vs local", branch
`dan/debug-overlay`, epic E1, phase 02 Prototype).

This document records the implemented design and its verification guidance. Every
FishNet API named below was checked against the installed package
source (`Library/PackageCache/com.firstgeargames.fishnet@382980e7eed4`, tag 4.7.3).
Do not substitute API names from tutorials or other FishNet versions.

**Read first:** `AGENTS.md`, `docs/NETWORK_CONTRACT.md` (sections 2, 3, 4, 5),
`docs/CONVENTIONS.md`. Then `Assets/_Project/Scripts/Net/PrototypeSessionUI.cs`
and `Assets/_Project/Scripts/Interaction/Basketball.cs`, which are the two files
this overlay reads state from and sits beside.

---

## 1. Outcome

A developer-only on-screen panel, toggled with **F3**, that shows what the network
layer is doing on *this* machine, so a desync can be located by looking at the
screen instead of reading two `Player.log` files. Read-only: it never writes
network or gameplay state.

It shows:

- **Header:** role (`HOST` / `CLIENT` / `SERVER-ONLY` / `OFFLINE`), local client id,
  transport name, RTT in ms, tick rate and current tick, and on the server the
  number of connected clients.
- **One row per spawned `NetworkObject`:** object id, name, owner (`server`, `me`,
  or `client N`), whether this machine is the **simulation writer** for it
  (`SIM-HERE`) or only renders replicated state (`REPLICATED`), and an optional
  per-object detail string.

The "writer" column is the point of the tool: it is `NETWORK_CONTRACT.md` section 2
("exactly one simulation writer") made visible.

**Required:** the two files in section 4, the bootstrap that creates the overlay
without any scene edit, F3 toggle, F4 dump to console and clipboard, the
verification in section 7, a PR from `dan/debug-overlay` to `main`.

**Optional but recommended:** `Basketball` implementing the detail interface
(section 4.4). Five lines, read-only.

**Out of scope:** Canvas / UI Toolkit, bandwidth or packet-loss statistics,
interpolation/prediction internals, graphs, editing state from the overlay, any
scene or prefab change, any package addition, any change to the contract.

## 2. Constraints from the repository rules

- Put code in `Assets/_Project/Scripts/Net/` with namespace `SunkCost.Net`.
  Small single-purpose files. Match the style of `PrototypeSessionUI.cs`
  (immediate-mode `OnGUI`, `GUILayout`, no Canvas).
- **Do not edit** `Assets/_Project/Scenes/Prototype/HQPrototype.unity` or any
  prefab. The team's scene-lock rule means a scene diff needs coordination; this
  task is explicitly "all new files" so it cannot conflict with Idan's work.
- Do not edit anything under `Assets/ThirdParty/` or `Library/`.
- Do not add a `[ServerRpc]`, `[ObserversRpc]`, `[TargetRpc]`, `SyncVar` or
  `SyncList`. This tool reads; it never sends. That is what keeps it out of the
  "second pair of eyes" review rule in `CONVENTIONS.md`.
- Input: the project uses the **new Input System only**
  (`ProjectSettings.asset` has `activeInputHandler: 1`). Use
  `UnityEngine.InputSystem.Keyboard.current`; legacy `Input.GetKeyDown` throws.
- There are no assembly definitions under `Assets/_Project`; runtime code compiles
  into `Assembly-CSharp` and `Assets/_Project/Editor` into `Assembly-CSharp-Editor`.
  Both already reference FishNet.
- Branch: `dan/debug-overlay` from `main`. Do not work directly on `main`; the
  team's rule is pull requests only.

## 3. Verified FishNet API surface

All from `FishNet.InstanceFinder` (static) unless noted. `InstanceFinder.NetworkManager`
returns `null` when no `NetworkManager` exists in the loaded scenes; handle that.

| Need | API | Notes |
|---|---|---|
| Manager | `InstanceFinder.NetworkManager` | May be null |
| Server / client started | `NetworkManager.IsServerStarted`, `.IsClientStarted` | Host = both true |
| Local client id | `NetworkManager.ClientManager.Connection.ClientId` | `-1` until connected |
| RTT (ms) | `NetworkManager.TimeManager.RoundTripTime` | `long`; on a host it reads about one tick (33 ms at 30 Hz), not 0, because the host client measures the loopback connection |
| Tick rate / tick | `NetworkManager.TimeManager.TickRate` (`ushort`), `.Tick` (`uint`) | |
| Transport name | `NetworkManager.TransportManager.Transport.GetType().Name` | `Tugboat` or `FishySteamworks`; `Transport` may be null before first session |
| Connected clients (server) | `NetworkManager.ServerManager.Clients.Count` | Includes the host's own client |
| Spawned objects | `NetworkManager.ServerManager.Objects.Spawned` when server started, else `NetworkManager.ClientManager.Objects.Spawned` | `IReadOnlyDictionary<int, NetworkObject>` (`ManagedObjects.Spawned`) |
| Per object | `NetworkObject.ObjectId`, `.OwnerId` (`-1` = no client owner), `.IsOwner`, `.IsSceneObject`, `.gameObject.name` | |

`NetworkTransform.ClientAuthoritative` is **not** publicly readable in this
version (`_clientAuthoritative` is a private serialized field). Do not use
reflection for it; the writer rule below does not need it.

## 4. Design

### 4.1 `INetworkDebugInfo.cs` — optional per-object detail

```csharp
namespace SunkCost.Net
{
    // Implement on a NetworkBehaviour to add a one-line status to the overlay row
    // (for example "Held by 1"). Optional; the overlay works without it.
    public interface INetworkDebugInfo
    {
        string DebugStatus { get; }
    }
}
```

### 4.2 `NetworkDebugSnapshot.cs` — data and text, no drawing

Pure C# apart from reading Unity components. Separated from the MonoBehaviour so
the verification in section 7 can assert its text from an editor command, and so
F4 can dump the same text to the log.

```csharp
using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace SunkCost.Net
{
    public sealed class NetworkDebugSnapshot
    {
        public struct ObjectRow
        {
            public int ObjectId;
            public string Name;
            public int OwnerId;        // -1 = no client owner (server-controlled)
            public bool IsOwner;       // this machine owns it
            public bool IsWriter;      // this machine is the simulation writer
            public string Detail;      // INetworkDebugInfo.DebugStatus or ""
        }

        public bool HasNetworkManager;
        public bool ServerStarted;
        public bool ClientStarted;
        public int LocalClientId = -1;
        public string Transport = "none";
        public long RoundTripTimeMs;
        public ushort TickRate;
        public uint Tick;
        public int ConnectedClients;   // server side only, else 0
        public readonly List<ObjectRow> Objects = new();

        public string Role =>
            !HasNetworkManager ? "OFFLINE" :
            ServerStarted && ClientStarted ? "HOST" :
            ServerStarted ? "SERVER-ONLY" :
            ClientStarted ? "CLIENT" : "OFFLINE";

        public static NetworkDebugSnapshot Capture() { /* section 4.2.1 */ }
        public string ToText() { /* section 4.2.2 */ }
    }
}
```

#### 4.2.1 `Capture()` rules

1. `NetworkManager nm = InstanceFinder.NetworkManager;` if null, return a snapshot
   with `HasNetworkManager = false` and no rows.
2. Fill the header fields from the table in section 3. Guard every nullable
   (`TransportManager.Transport`, `ClientManager.Connection`).
3. Choose the object source: `nm.ServerManager.Objects.Spawned` if
   `nm.IsServerStarted`, else `nm.ClientManager.Objects.Spawned`. Skip entries
   whose `NetworkObject` is null or not `IsSpawned`.
4. For each `NetworkObject`, compute `IsWriter` with these rules, in order. Stop at
   the first rule that applies:
   - **Override (14 September inventory update):** prefer the nullable
     `INetworkDebugInfo.WriterOverride` when set. `CarryableItem` declares its
     writer explicitly: a Held item is kinematic but its holder writes its pose.
     `PlayerInventory` supplies slot detail and returns null for the writer override.
   - **Rigidbody:** if the object has a `Rigidbody`, `IsWriter = !body.isKinematic`.
     This fallback applies only when no explicit writer is supplied; it must not
     override a kinematic Held/Stowed carryable's state-aware decision.
   - **Owned by a client (the players):** `IsWriter = nob.IsOwner`. The player
     prefab's `NetworkTransform` is client-authoritative, so the owner moves it.
   - **No client owner:** `IsWriter = nm.IsServerStarted`. Unowned objects are
     server-simulated.
5. `Detail` = the `DebugStatus` of the first `INetworkDebugInfo` on the object,
   else `""`. Wrap the property read in try/catch and show `"<error>"` on failure;
   a broken status string must not take the overlay down.
6. Sort rows: objects that are owned by anyone first (players), then by
   `ObjectId` ascending, so the layout is stable between refreshes.

Future kinematic writers (moving platforms, elevators) should implement the same
nullable writer override; do not special-case their types in the overlay.

#### 4.2.2 `ToText()` format

Plain text, fixed columns, one row per line, so it pastes cleanly into chat and
`Player.log`. Example output on a remote client while it holds the ball:

```
[NetDebug] CLIENT  client=1  transport=FishySteamworks  rtt=47ms  tick=30Hz/12345
 id   name              owner     sim         detail
  1   PrototypePlayer   client 0  REPLICATED
  2   PrototypePlayer   me        SIM-HERE
  3   Basketball        me        SIM-HERE    Held by 1
```

The same on the host would read `HOST  client=0  transport=Tugboat  rtt=33ms ...
clients=2` and the ball row `server  SIM-HERE  Free` when nobody holds it.

Owner text: `server` when `OwnerId == -1`, `me` when `IsOwner`, else `client N`.

### 4.3 `NetworkDebugOverlay.cs` — MonoBehaviour: toggle, dump, draw, bootstrap

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Net
{
    public sealed class NetworkDebugOverlay : MonoBehaviour
    {
        public const string ObjectName = "Network Debug Overlay";
        private const float RefreshInterval = 0.25f;

        // Public so an editor command can drive it without pressing keys.
        public bool Visible { get; set; }
        public NetworkDebugSnapshot Current { get; private set; }

        private static NetworkDebugOverlay instance;
        private float nextRefresh;

        // Creates the overlay in the editor and in development builds without any
        // scene or prefab change. Debug.isDebugBuild is true in both; release
        // builds never get this object.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Debug.isDebugBuild || instance != null) return;
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NetworkDebugOverlay>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f3Key.wasPressedThisFrame) Visible = !Visible;
            if (keyboard.f4Key.wasPressedThisFrame) DumpSnapshot();
            if (Visible && Time.unscaledTime >= nextRefresh) Refresh();
        }

        public void Refresh()
        {
            Current = NetworkDebugSnapshot.Capture();
            nextRefresh = Time.unscaledTime + RefreshInterval;
        }

        // F4: the text goes to the console (and therefore Player.log) and to the
        // clipboard, so two machines' views can be pasted side by side in chat.
        public void DumpSnapshot()
        {
            Refresh();
            string text = Current.ToText();
            Debug.Log(text);
            GUIUtility.systemCopyBuffer = text;
        }

        private void OnGUI()
        {
            if (!Visible || Current == null) return;
            // Top-right; PrototypeSessionUI owns the top-left (18, 18, 410, 300).
            const float width = 560f;
            GUILayout.BeginArea(new Rect(Screen.width - width - 18f, 18f, width, Screen.height - 36f), GUI.skin.box);
            // header line, then one GUILayout.Label per row with rich text colours:
            // SIM-HERE green, REPLICATED grey, own player row highlighted.
            // Keep it a straight transcription of Current; no logic in OnGUI.
            GUILayout.EndArea();
        }
    }
}
```

Requirements for the drawing:

- Enable `richText` on the label style; colour `SIM-HERE` `#7CFC7C`, `REPLICATED`
  `#A0A0A0`, and the row where `IsOwner` and the object is a player in cyan.
- Header shows the same fields as `ToText()` plus the hint
  `F3 hide · F4 copy/log snapshot`.
- No allocation-heavy work per frame: `Capture()` runs at most every 0.25 s while
  visible and never while hidden. `OnGUI` only formats what `Current` holds.
- Never throw from `OnGUI` or `Capture()`. Catch, log once, keep drawing.

### 4.4 Optional: `Basketball` implements `INetworkDebugInfo`

In `Assets/_Project/Scripts/Interaction/Basketball.cs` (Dan's file), add
`INetworkDebugInfo` to the class declaration and:

```csharp
public string DebugStatus =>
    state.Value == Held ? $"Held by {holderClientId.Value}" :
    state.Value == Released ? $"Released by {holderClientId.Value}" : "Free";
```

`state`, `Held`, `Released` and `holderClientId` already exist there. Nothing else
changes; the property only reads. If this edit is skipped, the ball row shows
owner and writer without the detail column, which is still correct.

### 4.5 Editor verification hook

Add to `Assets/_Project/Editor/Prototype/HQPrototypeTestHooks.cs`:

```csharp
public static string DebugSnapshotText()
{
    NetworkDebugOverlay overlay = Object.FindFirstObjectByType<NetworkDebugOverlay>();
    if (overlay == null) return "No NetworkDebugOverlay in the scene (bootstrap did not run?).";
    overlay.Refresh();
    return overlay.Current.ToText();
}

public static string SetOverlayVisible(bool visible)
{
    NetworkDebugOverlay overlay = Object.FindFirstObjectByType<NetworkDebugOverlay>();
    if (overlay == null) return "No NetworkDebugOverlay.";
    overlay.Visible = visible;
    return $"Visible={overlay.Visible}";
}
```

That file already has `ConnectionDiagnostics`, `ClientMoveLocalPlayerToBall`,
`ClientRequestGrab`, `ClientRequestRelease(bool)`, `ClientHeldBallField`,
`SessionUiState` and `BallState`; reuse them in section 7.

## 5. Implementation order

1. Branch: `git checkout -b dan/debug-overlay main` (tree must be clean first).
2. Add `INetworkDebugInfo.cs`, `NetworkDebugSnapshot.cs`, `NetworkDebugOverlay.cs`.
   Let Unity create the `.meta` files; commit them with the scripts.
3. Confirm a clean compile (section 7, step 1) **before** touching anything else.
4. Add the two test hooks (4.5). Compile again.
5. Optional: the `Basketball` detail (4.4). Compile again.
6. Run the verification in section 7. Fix what it finds.
7. Build a Windows development build through `HQPrototypeBuild.BuildWindowsDevelopment()`
   and confirm the overlay object exists in a standalone run (its `Debug.Log` of the
   F4 dump, or the `[NetDebug]` line from the hook, in the build's log).
8. Commit, push, open a PR to `main`. Section 8.

## 6. Working with Unity MCP

The editor bridge is the primary test tool. Its `RunCommand` wrapper is:

```csharp
using UnityEditor;
using UnityEngine;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("...");
    }
}
```

Two hard limits learned on this project:

- The dynamic command assembly **cannot reference FishNet types directly**
  (`using FishNet...` or any generic over a `NetworkBehaviour` subclass fails to
  compile), and it cannot `using System.Reflection`. Put anything that needs those
  into `HQPrototypeTestHooks` (editor assembly) and call the hook from the command.
- **Never edit a `.cs` file while the editor is in Play Mode with a live
  session.** The recompile reloads the domain and silently resets FishNet's
  connection state while leaving stale scene objects behind; every diagnostic
  after that is wrong. Exit Play Mode, edit, wait for compilation, re-enter.

Two-process local setup (all from the repository root, Git Bash):

```bash
# standalone build as a HEADLESS host so its own player cannot receive stray input
cd Builds/HQPrototype && (./SunkCostHQ.exe -batchmode -nographics -hq-auto-host-local -logFile host.log &)
# standalone build as a headless client instead
cd Builds/HQPrototype && (./SunkCostHQ.exe -batchmode -nographics -hq-auto-join-local 127.0.0.1 -logFile client.log &)
# stop it
taskkill //F //IM SunkCostHQ.exe
```

In the editor, enter Play Mode with a command, then
`Object.FindFirstObjectByType<PrototypeSessionUI>().StartLocalHost()` or
`.JoinLocal("127.0.0.1")`. A killed process is only noticed by the other side
after the transport timeout (35–75 s observed); `LeaveSession()` disconnects
immediately. Always run the headless host **before** entering Play Mode in the
editor when the editor is the client. `Builds/` and `*.log` are git-ignored.

## 7. Verification checklist (record actual output, not intent)

1. **Compile.** `EditorApplication.isCompiling == false`, console has zero errors
   after the new files import. Existing `CS0618` obsolete-API warnings in
   `PrototypeSessionUI.cs`/`Basketball.cs` are pre-existing and not yours.
2. **Bootstrap.** Enter Play Mode in `HQPrototype` without hosting. The hook
   `DebugSnapshotText()` returns a snapshot with `OFFLINE` (a `NetworkManager`
   exists in the scene but nothing is started) and the overlay object exists.
3. **Host alone.** `StartLocalHost()`, wait for `SessionUiState()` to show
   `server=True, client=True, players=1`. Snapshot must read `HOST`, `client=0`,
   `transport=Tugboat`, `rtt` of about one tick (33 ms observed, not 0), `clients=1`; player row `me SIM-HERE`; ball row
   `server SIM-HERE` (detail `Free` if 4.4 was done).
4. **Remote client.** Exit Play Mode. Start the headless standalone host. Enter
   Play Mode, `JoinLocal("127.0.0.1")`, wait for `players=2`. Snapshot must read
   `CLIENT`, `client=1`, `rtt` greater than `0`; player 0 `client 0 REPLICATED`;
   player 1 `me SIM-HERE`; ball `server REPLICATED`.
5. **Ownership transfer.** `ClientMoveLocalPlayerToBall()` then
   `ClientRequestGrab()`; after a second the ball row must be `me SIM-HERE`
   (`Held by 1`). `ClientRequestRelease(true)`; after the ball settles (about five
   seconds) it must return to `server REPLICATED` (`Free`). This is the contract's
   grab → release → handoff sequence, observed through the overlay.
6. **Drawing path.** `SetOverlayVisible(true)`, wait a few frames, read the console:
   no exceptions from `OnGUI`. `Unity_Camera_Capture` does not include IMGUI, so
   do not rely on a capture to prove it draws; the absence of errors plus the
   text assertions above is the evidence. A human presses F3 in the next
   two-computer session.
7. **Dump.** Call `DumpSnapshot()` through a command; the `[NetDebug]` line must
   appear in the console with the same content as step 5.
8. **Standalone.** Development build succeeds; running it with
   `-hq-auto-host-local` and calling nothing, its `Player.log` must not contain
   exceptions from `SunkCost.Net.NetworkDebug*`.

Put the results (commands used, actual snapshot text, anything skipped) in a short
section appended to `docs/HQ_PROTOTYPE_TEST_REPORT.md` under a heading
"Network debug overlay". Do not claim a step passed without its output.

## 8. Commit and pull request

- One commit for the overlay files, one for the hooks, one for the optional
  `Basketball` detail, or a single commit if the assistant's workflow prefers it.
  Messages say what changed and why (`CONVENTIONS.md`).
- Push `dan/debug-overlay` and open a PR to `main` titled
  `Add F3 network debug overlay (ownership, client ids, RTT, writer)`. The body
  lists the verification results and states plainly that the tool is read-only
  and adds no RPC or SyncVar. It therefore does **not** require the second
  reviewer, but a teammate glance is still welcome; do not merge it from the
  assistant session unless Dan says so.
- Notion (only if the assistant has access; otherwise leave for Dan): set the task
  to `In review` with the PR link, and fill `Branch` with `dan/debug-overlay`.

## 9. Acceptance

Done when all of section 7 has recorded output, the PR is open, and a person on
the next two-computer Steam session can press F3 on both machines and read the
same object ids with opposite `SIM-HERE`/`REPLICATED` columns for the two players.

## 10. Risks and non-goals, stated once

- The `Rigidbody` fallback cannot identify a kinematic held item's writer.
  CarryableItem supplies `WriterOverride` through the interface; future custom
  simulation modes should supply their actual writer in the same way.
- `Debug.isDebugBuild` gating means the overlay is absent from a release build by
  construction; nobody has to remember to strip it.
- Nothing here changes replication, ownership, or timing. If verification shows a
  contract violation in existing code, report it in the test report and stop; do
  not fix gameplay code inside this task.
