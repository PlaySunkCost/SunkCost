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
- A **client is never authoritative about anything.** Clients send intent and render
  what they are told. The only thing a client decides on its own is how to draw.

## 2. Ownership

Every networked object has **exactly one owner** at any moment.

- Default owner is the **server**.
- When a player grabs a physics object, **ownership transfers to that client.**
  That client simulates the object locally and broadcasts its transform.
- On release, ownership returns to the server after the object comes to rest.
- Two clients can never own the same object. A grab request on an owned object is
  **refused by the server**, not queued.

Why: networked rigidbodies are unsolvable if two machines simulate the same body.
Ownership transfer is how every game in this genre does it, and the visible jank is
acceptable — it is where the clips come from. **Do not attempt deterministic physics.**

## 3. Who decides what

| State | Decided by | Notes |
|---|---|---|
| Player movement | Client, corrected by server | Clients move themselves; server validates position deltas loosely |
| Grabbed object transform | Owning client | Broadcast, not simulated elsewhere |
| Free object transform | Server | Standard replication |
| Oxygen, health, damage | **Server only** | Clients display, never compute |
| Loot value, quota, funds | **Server only** | Never trust a client number |
| Monster AI and targeting | **Server only** | Clients receive positions and animation state |
| Noise events | **Server only** | See `NoiseSystem` — clients may play the sound, never emit the event |
| Elevator state | **Server only** | Clients send a request, server decides |

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

If a method name does not say who calls it and who runs it, rename it.

## 5. Hard rules

1. **No gameplay decision runs on a client.** Prediction is visual only.
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
- The **host owns the save.** If the host quits, the run ends. Host migration is
  explicitly out of scope for v1 — revisit only if it turns out to be cheap.

## 7. Tick rate

Start at **30 Hz** server tick. Do not tune this until the prototype is playable;
it is the kind of number that eats a week and changes nothing about whether the
game is fun.

---

## Open

- Interest management (do distant players need updates?) — defer until a real map exists.
- Anti-cheat — out of scope. Friends-only lobbies at launch make this a non-problem.
