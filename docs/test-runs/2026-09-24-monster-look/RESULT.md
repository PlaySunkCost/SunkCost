# Monster look — 24 September 2026

Branch `dan/monster-look`. Five of the seven monsters wear Dan's Meshy
generations:
- the Listener
- the Lure
- the Weeping Angel
- the Long Walker
- the four-legged Charger

Each generation is rigged by `tools/blender/rig_generated_monster.py`, then
imported by `MonsterModelSetup.ApplyAll`. The Impostor keeps its placeholder
until the diver has a rig; the Elevator Ghost is the car.

## What was checked (edit mode, no Play Mode)

- **Prefabs.** Every monster prefab has:
  - no missing scripts;
  - its NetworkObject, NetworkTransform, CharacterController, brain,
    CreatureLook, CreatureSounds and CreatureRig, with the animator, beam
    origin and voice all wired;
  - a URP Lit material with base colour and normal maps;
  - its clips.
- **Network list.** All six are still registered in `DefaultPrefabObjects`.
- **Look.** Close captures in neutral light, from front, three-quarter, side
  and back, plus every clip sampled mid-clip.
  - **Size:** each monster stands on the floor and faces forward, the way the
    brain moves it. The Listener is 2.3 m, the Lure 2.6 m, the Angel 2.35 m and
    the Walker 3.3 m; the Charger is 1.8 m high and 3.7 m long.
  - **Smooth shading:** the rig now shades smooth by angle (50°) instead of
    flat. Meshy's normal maps were baked for smooth normals, and flat facets
    showed on every limb. Vertices went from about 57k to about 17k each.
  - **The Charger's Hunting:** it has its own four-legged prowl. The shared
    upright Hunting pitched the spine 38° and drove its head 0.6 m under the
    floor; the head now stays at 0.82–0.94 m.
- **Raw downloads.** The raw Meshy downloads live in `<Kind>/Generated~`, which
  Unity doesn't import, and are no longer committed.

## Owed at first (Dan: no tests for now); paid by the polish pass below

| Check | Result |
|---|---|
| `monsters` matrix (host + guest, `MonsterRuntimeChecks`) with the new models | not run |
| Regressions with monsters: loop, cabin, spectate (the Impostor's visibility), fullrun | not run |
| In a dive: each monster's walk speed against its clip (foot sliding), the Charger's rush and turn, the Angel frozen on screen, the Listener's and the Lure's beams from the new `BeamOrigin` | not run |
| On a guest: the pose and clips replicate (CreaturePose) | not run |

## Monster polish (24 September 2026, Dan's brief: one agent per monster, the Impostor untouched)

All rows below are Play Mode matrices. **Host** means the editor alone. **Guest** means the editor hosting,
with a second build (`Builds/HQPrototypeLocal`, stamped with the commit named) as a separate process
on this machine over loopback. Nothing ran on two machines over Steam.

### Per monster

| Monster | Host matrix | Guest rows (build) |
|---|---|---|
| Charger (`polish-Charger`) | MATRIX_PASS, 126 rows | G1 PASS, 29 rows (3861be2) |
| Long Walker (`polish-LongWalker`) | MATRIX_PASS, 146 rows | R1–R2 PASS, 44 rows (869948b) |
| Weeping Angel (`polish-WeepingAngel`) | MATRIX_PASS, 870 rows | R1–R3 PASS, 105 rows (a08841e) |
| Listener (`polish-Listener`) | MATRIX_PASS, 175 rows | G1–G3 PASS, 45 rows (05c94d6) |
| Lure (`polish-Lure`) | MATRIX_PASS, 266 rows | covered by the Listener's G3 (the Lure's lights on a guest) |

### What the rows measured

- **The Walker's grab:**
  - it catches at 1.39 m, lifts, and kills at 2.01 s;
  - at the end of the hold the victim's eye is 0.11 m above the Walker's Face and 0.41 m in front of it;
  - the diver's camera matches the eye pose, 0.000 m apart;
  - a guest's own screen is 0.02 m from the host's copy, and a dead spectator's view is 0.02 m from it.
- **The Angel's embrace:**
  - it catches at 1.28–1.37 m;
  - the hands leave the face at 0.68 s and reach the head at 0.97 s, and the kill lands at 2.20 s;
  - on a guest, 0.2° off the Face on the host's copy and 1.9° on the guest's own screen;
  - the guest's look alone freezes the Angel.
- **Dash versus a hold (Dan):**
  - a dash on the catch frame starts, then the hold cuts it off (G5 3.28 m, and a guest in R1);
  - dashes in the hold are refused;
  - the kill lands even after a dash onto the tube's safe ground (G6);
  - a held diver is never taken as an elevator rider and cannot Unstuck.
- **The Charger:**
  - the tell lasts 1.50 s and the aim locks 0.2 s before launch; the rush reaches 18.0 m/s and holds its line (0.000 m);
  - one hit, 35 HP and a leak, then a 3.00 m knock-back along the path;
  - the jolt peaks at 15.3° and settles in 0.46 s;
  - a real dash at launch dodges it (6.8 m off the line);
  - the snout stops 0.01 m into a wall;
  - on a guest the knock-back serial goes up by 1, and the host's copy lands 3.00 m along the path.
- **The beams:**
  - **Line match:** the drawn line and the judged line match (0.000 m on the host, ≤ 0.013 m on a guest).
  - **Width:** half-width 0.35 m.
  - **Tracking:** a sprinter is hit.
  - **Dash dodges:** a dash in the charge breaks the lock (6.18 m, broken 0.02 s after the press) and it misses; a dash out of cover while firing misses; a diver who dashes and then stands is caught at 2.88 s.
  - **Knock-back:** a Charger knock-back is not a dash.
- **The beams' looks:**
  - **The dark shade:** the headlamp next to the beam is drawn at 3 of 15 and restored exactly, over 520 renders; LampOn is never written; the shade reads ≥ 0.97 on a guest.
  - **The light beam's brightness:** the Lure 0.235 → 0.284, the seabed along the path 0.308 → 0.367, the diver 0.341 → 0.401.
  - **The light pool:** lights return to the pool; a second Lure is not drawn to them; a guest sees 7 lights, then 0.
- **The Impostor:**
  - `Impostor.cs`, `ImpostorLook.cs`, its prefab and its material are unchanged since 1639c82;
  - the Walker's row I1 shows it still hits for 30 HP plus a leak and runs, with no hold.

### Regressions after the pass (HEAD a08841e)

| Job | Result |
|---|---|
| `monsters` (host + guest) | MATRIX_PASS, 175 rows |
| `spectate` | MATRIX_PASS, 239 rows |
| `loop` | MATRIX_PASS, 62 rows |

### Still owed

- A two-machine Steam test.
- The TV has no row: the guest snapshot has no TV camera field.
- The jolt as a spectator sees it on a remote copy.
- The `cabin` and `fullrun` jobs, which were not re-run.
- A teammate's review of the three NETWORK_CONTRACT rows marked PROPOSED.

### Open for Dan

- **Feel:**
  - the grab's feel (the shake, the 0.41 m gap, the 2 s hold);
  - the jolt;
  - the beam brightness and darkness;
  - the re-acquire speed (2.5 m/s; a crouch-walker at 2 m/s is caught).
- **Found and not changed:**
  - the Angel's "watched" test uses its renderer bounds, so a diver within the statue's outstretched arms (about 0.7 m) counts as watching whichever way they face;
  - the Listener once never reached a sound 12 m to its side (probably the level geometry);
  - `WaitForCar` throws a null reference when a session stops mid-ride (it dates from 15 September; seen only in test teardown).
- **Parked by Dan:** the monsters have no movement or footstep sounds.
