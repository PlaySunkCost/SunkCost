# Basketball verification — 26 September 2026

Branch: `codex/basketball-physics-scoring`; working changes on base `1464477`.
Unity 6000.6.0f1. Editor host + separate rendered Windows client over local
Tugboat loopback, no added latency/loss. Test build identity: `test-build.json`.

`matrix.log`: **MATRIX_PASS**. Verified on the actual generated HQ:

- Leather material present; prefab/slot icon and court close-up visually inspected.
- 1.800 m underside drop -> 1.057 m first rebound; next rebound loses energy.
- Both hoops count a downward basket once; backboards show the shared count.
- Side misses and upward passes do not score.
- Host normal inventory throw scores while the ball is `Released`.
- Guest joins after three baskets and receives that score.
- Guest grabs the exact ball by network ID, aims with its real camera and uses
  its normal inventory request. Its thrown ball scores with the server's copy
  kinematic, proving the scorer does not require server Rigidbody velocity.
- Guest sees the new replicated score and backboard text; no duplicate count.

The test-only pitch search predicts a suitable camera angle; it does not move
the released ball or change gameplay aim assistance. Initial harness attempts
were corrected because the old throw argument does not control camera aim, and
client clone names do not identify a specific ball. The final run uses the real
release proposal and network ID. No runtime replication exceptions found in
the passing guest log. Screenshot: `basketball.png`.

Code review against the netcode checklist: server remains sole score writer;
owner remains sole Released physics writer; no new RPC/SyncVar; stale samples
are cleared for held/stowed/transit items, despawn, scene/role or motion changes.
This AI review does not substitute for teammate approval before merge.

Not verified here: Steam between two computers, artificial latency/packet loss,
four-player play. These local results do not claim those checks passed.
