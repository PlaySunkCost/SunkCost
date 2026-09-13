# Network contract

**Read this before writing any networked code. Nobody writes a single `[ServerRpc]`
until they have read this file.**

Three people writing netcode with AI assistance will invent three incompatible
conventions in week one unless the rules are written down first. This document is
the rules. It is short on purpose. Change it by agreement, in a PR, never silently.

---

## 1. Topology

- The **host is the server.** One player's machine runs the authoritative simulation
  and also plays the game. There is no dedicated server, ever.
- Transport is **Steam P2P** (relay + NAT punchthrough). No ports, no hosting bill.
- **Gameplay outcomes are server-authoritative.** Clients send intent; the server
  validates it. Client movement and client simulation of granted physics objects
  are explicit exceptions described below, not permission to set health or loot.
- The selected stack is **FishNet**, with FishySteamworks / Steamworks.NET for
  Steam connectivity. Do not substitute Netcode for GameObjects APIs. Verify the
  installed package/import version before implementing any API examples.

## 2. Ownership

Every replicated physics object has **exactly one simulation writer** at a time.
In FishNet, an object may have a client owner or no assigned client owner. In this
document, "server-owned" means server-controlled with no assigned client owner;
the server is not itself a client owner. Ownership changes are server decisions.

- Default simulation writer is the **server**, with no assigned client owner.
- When a player grabs a physics object, **ownership transfers to that client.**
  That client simulates the object locally and broadcasts its transform.
- On release, ownership returns to the server after the object comes to rest.
- Until that handoff, the releasing client remains the simulation writer, including
  during a throw. The server tracks that it is released rather than still held.
  The free-object row below applies after handoff, not to this settling interval.
- Two clients can never own the same object. A grab request on an owned object is
  **refused by the server**, not queued.

Why: competing physics writers cause conflicting transforms. This project's
ownership model gives one peer responsibility for each replicated body; other
peers render its replicated state. **Do not attempt deterministic physics.**

### Grab and release flow

1. Client requests a grab through its owned player interaction object. Do not
   attach the target before approval. Cosmetic input feedback is allowed.
2. Server identifies the sender from the connection and validates the target,
   range, alive/dive state, carrying eligibility and current ownership.
3. Server accepts only one competing request, records the holder, and grants
   FishNet ownership to that connection. Denied requests get a response.
4. The granted client begins simulation only when ownership and authoritative
   holder state agree. Other peers stop simulating that body and interpolate.
   Ownership and a separate notification must not be assumed to arrive together.
5. On a validated release/throw, mark it released. The releasing client continues
   simulation until the server approves the rest/handoff condition, preserving
   the existing release policy. Replicate the final motion state at transfer.
6. After handoff, the server resumes simulation and clears client ownership.
   On disconnect, the server takes over immediately, even if it is still moving.

The HQ basketball prototype uses linear speed below **0.15 m/s**, angular speed
below **0.5 rad/s**, and **0.5 seconds** continuously below both thresholds as its
rest condition. It forces handoff after **4 seconds** so ownership cannot remain
stuck indefinitely. The current rule refuses grabs while a client still owns a
settling object; changing that needs an explicit amendment. Moving-elevator
handoff behavior remains undefined.
Two-person carrying has no defined protocol yet: do not assume a shared writer.

## 3. Who decides what

| State | Decided by | Notes |
|---|---|---|
| Player movement | Client, corrected by server | Clients move themselves; server validates position deltas loosely |
| Grabbed object transform | Owning client | Broadcast, not simulated elsewhere |
| Released object before rest/handoff | Releasing client | Server validates release and decides handoff |
| Free object transform | Server | Standard replication |
| Oxygen, health, damage | **Server only** | Clients display, never compute |
| Loot value, quota, funds | **Server only** | Never trust a client number |
| Monster AI and targeting | **Server only** | Clients receive positions and animation state |
| Noise events | **Server only** | See `NoiseSystem` — clients may play the sound, never emit the event |
| Elevator state | **Server only** | Clients send a request, server decides |
| Site seed and dive lifecycle | **Server only** | Replicate current state to supported entrants |

Rule of thumb: **if getting it wrong would let someone cheat or desync the run,
the server decides.**

## 4. Naming

FishNet attributes, consistent names, no exceptions:

```csharp
[ServerRpc(RequireOwnership = true)]
void ServerRequestGrab(NetworkObject target) { }   // client -> server, a REQUEST

[ObserversRpc]
void RpcPlayGrabEffect(Vector3 at) { }             // server -> everyone, cosmetic

[TargetRpc]
void TargetShowMessage(NetworkConnection conn, string text) { }  // server -> one client
```

- Client-to-server methods begin **`ServerRequest…`** or **`ServerSet…`**. They are
  requests, and the server may say no.
- Server-to-all methods begin **`Rpc…`**.
- Server-to-one methods begin **`Target…`**.
- Synced state uses `SyncVar` / `SyncList` and is **written only on the server**.
- Replicated state must initialize correctly without replaying a past event RPC.
  Use state change callbacks for presentation; RPCs are not durable storage.
- Validate every client request: sender authorization, target existence, range,
  current state and applicable numeric bounds. Do not trust client-computed rewards
  or arbitrary noise positions/radii. Reject stale or duplicate actions safely.
- Requests that can fail have a caller-visible failure path. Do not depend on a
  notification and an ownership update arriving in the same frame.

If a method name does not say who calls it and who runs it, rename it.

## 5. Hard rules

1. **Server decides gameplay outcomes.** Client movement and physics simulation
   follow only the explicit exceptions in sections 2 and 3.
2. **One writer per piece of state.** If two systems write the same SyncVar, one of
   them is wrong — fix the design, don't add a lock.
3. **Never send a value the server can compute.** A client saying "I picked up 1,400
   gold" is a bug waiting to be exploited.
4. **RPCs are not free.** Nothing per-frame. Movement goes through the transform
   syncer; everything else is event-driven.
5. **Nobody merges their own change to this contract.** Gameplay tweaks, fine.
   Anything here gets a second pair of eyes.

## 6. Disconnects

- A disconnected player's body **stays where it fell** and keeps its inventory.
  It can be dragged to the elevator like a corpse.
- Players rejoin **at HQ between cycles**, never mid-dive.
- Between-dive joining/rejoining on the boat is under consideration in the design,
  not implemented policy. It does not change the HQ-only save point.
- Recovering a disconnected or dead player's body preserves purchased gear under
  the design's recovery rule. Unrecovered purchases are lost; the free base kit
  remains available. Banked loot is unaffected. Held objects must not disappear
  with the connection; the server handles their ownership and inventory state.
- The **host owns the save.** If the host quits, the run ends. Host migration is
  explicitly out of scope for v1 — revisit only if it turns out to be cheap.
- The HQ basketball harness has no death, inventory or persistent run. In this
  harness only, disconnect despawns that player's temporary avatar and immediately
  returns any held or released basketball to server simulation.
- Harness leave semantics: a leaving client stops its own connection (a clean
  disconnect, so the host does not wait for a timeout); a leaving host stops the
  server, which disconnects every client. There is no host migration. The
  transport is bound once per process because FishNet's Server/ClientManagers
  subscribe to `TransportManager.Transport` at initialization; switching
  Local/Steam requires a restart.
- Ordering rule learned from the harness: a `TargetRpc` is written to the
  outgoing buffer immediately, while SyncVars flush at tick end, so a remote
  client can run the RPC before the same tick's SyncVar values arrive (the host
  never sees this because its values are set in-process). Client-side state that
  depends on both must be derived from the SyncVars' `OnChange` (or tolerate
  either order), never from the RPC alone.

## 7. Tick rate

Start at **30 Hz** server tick. Do not tune this until the prototype is playable;
it is the kind of number that eats a week and changes nothing about whether the
game is fun.

---

## 8. Noise

Use `Assets/_Project/Scripts/Noise/NoiseEvent.cs` and `NoiseSystem.Emit`:

```csharp
NoiseEvent(Vector3 position, float radius, NoiseKind kind, int sourceId = 0)
```

- Radius is in **metres**, not arbitrary loudness units. Route gameplay hearing
  through this bus and `INoiseListener`; do not create a parallel hearing system.
- Only the server emits gameplay noise and runs monster decisions. For client
  actions, validate intent on the server and derive the noise there. Emit once
  per accepted noise-producing action, not again on every receiving peer.
- Local sound effects for immediate feedback are allowed; they do not emit a
  gameplay event or change a monster's state.
- Server startup/shutdown must set/reset the bus's server guard and clean up
  scene listeners. The current scaffold is not proof of network integration.
- `SourceId` is intended as a network identity; the current emitter uses Unity
  instance IDs. Map it to a stable network identity when integrating networking,
  and do not compare instance IDs across machines.
- Hearing uses the bus. This does not forbid navigation, collision queries, or
  a separately approved creature sense; new senses belong in the design first.

## 9. Elevator, oxygen and spectating

- The server controls elevator requests, boarding/cargo state and motion. It
  carries **any number of the crew and any amount of cargo together**, including
  unattended loot or bodies. Do not impose a one-person-or-one-load limit.
- Travel is **15 seconds each direction**, returning without a surface delay:
  a diver below waits **30 seconds** after departure. Keep timing configurable.
- Each transition into motion emits authoritative elevator noise. Persistent
  elevator state, motion progress and occupants must be reconstructible from sync
  state. A surfaced diver cannot descend again during that dive.
- The server owns oxygen and item use. Each diver has one equipped tank of a
  selected size. During a dive, air is restored only by using an air-restoring
  item; there are no refill stations or passive refills. Validate and apply item
  consumption/restoration once. Exact item tuning remains open in the design.
- Spectating renders replicated state locally; do not stream another player's
  video. Dead players select divers independently. Surfaced players share one TV
  and one selected diver, with selection coordinated by the server.
- Voice membership is independent of camera/AudioListener position: dead players
  talk only to dead players, surfaced players to surfaced players, and divers use
  proximity voice. A spectating camera must not open access to divers' voice.
  A surface radio remains a proposed upgrade, not an approved exception yet.

## 10. Verification and changes

For networking changes, record a host and separate non-host reproduction, actual
results, and any missing runtime validation. Validate Steam transport using builds
on separate machines. Test contention, release/handoff, disconnect and applicable
gameplay rules under latency/packet loss where possible. Compilation is not proof
of synchronization. See [WORKFLOW.md](WORKFLOW.md) and the repository's
[review prompts](../.claude/agents/) for the process and reusable checklists.

Contract changes normally land through a PR with a teammate's review before
dependent implementation. An AI report does not replace that review. Do not mark
this document "signed off" unless that review actually occurred.

## Open

- Interest management (do distant players need updates?) — defer until a real map exists.
- Anti-cheat systems — out of scope for v1. Friends-only lobbies do not remove
  the need to validate requests for stale state, bugs and unexpected inputs.
- Moving-elevator physics handoff; see section 2.
- Two-person carrying protocol, if included; a single simulation writer still holds.
- Movement correction and transform interpolation settings for the selected
  FishNet version; start with the existing 30 Hz server baseline.

API reference: [FishNet ownership](https://fish-networking.gitbook.io/docs/guides/features/ownership).
