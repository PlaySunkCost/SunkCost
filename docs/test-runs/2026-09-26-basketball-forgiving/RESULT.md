# Larger basketball and forgiving rims — 26 September 2026

Branch `codex/basketball-physics-scoring`, implementation commit `70de1b9`.
Unity 6000.6.0f1; editor host and separate Windows client, Tugboat loopback,
no injected network latency/loss. Build identity saved in `test-build.json`.

`matrix.log`: **MATRIX_PASS**. Ball diameter is 0.26 m; rims/brackets are 40%
wider with matching derived render/collision mesh and scoring radius. Original
post/backboard placement is unchanged. Visually inspected `hoop.png` and ball art.

- Clean centre baskets and drops offset **-0.16 m and +0.16 m** pass through
  the actual collision opening and score at **both** hoops.
- Side misses and upward passes do not score; each basket counts once.
- Real host and non-host inventory throws score while Released. The server's
  remote copy stays kinematic; no second physics writer is introduced.
- Late joiner receives existing count; guest sees the final count and both
  backboards update (`guest-score.txt`).
- The larger ball bounces and loses height on successive bounces. Measured
  first rebound underside: **1.189 m** from a 1.800 m underside drop. This is
  higher than the original 24 cm ball's 1.057 m result; the tuning reference
  is not a claim that this game prop meets regulation-ball certification.
- No replication exception signatures in the passing guest log.

An initial run was intentionally stopped after discovering the local executable
predated the implementation commit; the client was rebuilt to the current revision
before this complete passing run. Original prepared-model assets were preserved.

Steam across two computers, artificial latency/loss and four-player play were
not verified. Human review remains pending before merging the scoring change.
