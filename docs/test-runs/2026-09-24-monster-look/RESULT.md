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

## Owed (Dan: no tests for now)

| Check | Result |
|---|---|
| `monsters` matrix (host + guest, `MonsterRuntimeChecks`) with the new models | not run |
| Regressions with monsters: loop, cabin, spectate (the Impostor's visibility), fullrun | not run |
| In a dive: each monster's walk speed against its clip (foot sliding), the Charger's rush and turn, the Angel frozen on screen, the Listener's and the Lure's beams from the new `BeamOrigin` | not run |
| On a guest: the pose and clips replicate (CreaturePose) | not run |
