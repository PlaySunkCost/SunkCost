# Code check, items — 18 September 2026 — air, hands, loop, spectate all MATRIX_PASS

Four findings of the 18 September code check on the item side, each confirmed
in the source before the fix:

1. **A leaver's dropped items were lost mid-sail.** Between the move list and
   the load (gangway, pull-away, fade) a passenger who left had their items
   enrolled as cargo but not moved, and the old world unloaded them. The move
   list now closes at the load. Row **S14** (loop): the guest holds the deck
   ball, leaves during the pull-away; the ball lies aboard at sea.
2. **The dead could grab.** A grab in flight as the air ran out landed after
   the death. The four inventory RPCs return when the requester is dead. Row
   **D1** (spectate): a dead player's grab leaves the coin loose.
3. **A cancelled throw could make two items held at once** after a fast second
   grab. `ServerCancelRelease` drops the returning item where it lies when the
   hands already hold another. Row **M8c** (hands): thrown ball, heavy ball
   grabbed, cancel → the ball is Free, the heavy ball stays held.
4. **A breath at full air wasted the tank.** Refused (`AirFull`, "Your air is
   full — keep the tank"); the prompt says so before the click. Row **K3** (air).

Host: the editor, guest builds, Local transport. Logs alongside.
