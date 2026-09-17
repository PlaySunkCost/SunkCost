# The "expected to exist" warning was a lost-players bug — 17 September 2026 — spectate MATRIX_PASS (210 rows)

Guest A of the spectate matrix logged FishNet's "Spawned NetworkObject was
expected to exist but does not for Id 8" around site close and revive. Not
harmless.

## Diagnosis
A `[Scenes]` traffic log was added (every load/unload the server sends with
the connections and the moved objects by id and name; every one a client
processes). One run gave the sequence on guest A (dead below, watching the
ship, the last diver surfacing — T3):

```
18:37:01  Received a spawn objectId of 8 which was already found in spawned   (host's player)
18:37:01  Received a spawn objectId of 26 which was already found in spawned  (guest B's player)
          [Scenes] client unloads ShipAtSea
          [Scenes] client loads ShipAtSea moving 17:PrototypePlayer(Clone)    (A's own object, to the ship)
          [Scenes] client unloads DiveSite01
18:37:10  Spawned NetworkObject was expected to exist but does not for Id 8
          Spawned NetworkObject was expected to exist but does not for Id 26
          [Scenes] client loads DiveSite01 moving ?, 17:PrototypePlayer(Clone), ?   (the next ride down)
```

`ServerMoveDeadToShip` unloaded the ship for A (`ServerUnwatch`, A was
watching it) and reloaded it with A's object in the same tick. FishNet's
observer rebuild crossed the queued unload: A received a second spawn of
every ship object, then the unload's despawn removed them for good — after
End day revived A on deck, the host and B did not exist on A's client. The
same shape sat in the ride down for a TV viewer (the site it watches is the
destination).

## Fix
`ServerDropWatchBefore(conn, destination)`: a watcher of the destination
world only forgets the watch; the load lands the object in the scene the
client already holds (FishNet moves `MovedNetworkObjects` into an
already-loaded scene and applies the preferred active scene). A watcher of
another world unloads it as before. Used at the three move sites (the dead
to the ship, ride down, ride up).

## Run
`spectate` — MATRIX_PASS, 210 rows ([spectate-matrix.log](spectate-matrix.log)).
New row T4: after the move up and the revive, A's client still has the host
and B. Both guest logs: 0 "expected to exist", 0 "already found"; the ride
down now reads `moving 8, 17, 26` ([guest-a-scenes-after.txt](guest-a-scenes-after.txt)).
