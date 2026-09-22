# Monster models — the Blender-to-prefab pipeline

Decided with Dan, 22 September 2026: Dan models the monsters in Blender from
reference pictures (I describe each one, he sends a picture that matches); the
prefabs are the seam. Drop an FBX in, run one menu, the monster wears it. The
brain, the collider, the layer and the networking never change with the look
(`docs/NETWORK_CONTRACT.md` §3 "Monster AI and targeting": the look reads the
replicated pose and decides nothing).

The first one is the Listener ("the guy with the dark laser"), then the Lure.
A rigged **test Listener** built by `tools/blender/make_test_listener.py` proves
the pipeline end to end; it is not the look.

## The checklist for a model

1. **Where:** `Assets/_Project/Models/Monsters/<Kind>/<Kind>.fbx` — `Kind` is the
   `MonsterKind` name: `LongWalker`, `WeepingAngel`, `Charger`, `Lure`,
   `Listener`, `Impostor`. Textures beside it in the same folder.
2. **Scale and axes:** 1 Blender unit = 1 metre (Scene units metric, scale 1).
   Feet on the floor at the origin, the model facing **−Y** in Blender. Export
   with the FBX exporter's defaults **Forward −Z, Up Y**, "Apply Scale: FBX All",
   so Unity gets it facing +Z at scale 1 — the setup refuses nothing, but a wrong
   axis shows at once in the portrait.
3. **Heights** the brain assumes (the collider is built by the monster setup, not
   the model): Long Walker **3.2 m**, Weeping Angel **2.1**, Charger **1.4** (and
   wide), Lure **2.2**, Listener **2.0**, Impostor **1.8** (a diver).
4. **A rig, Generic** (not Humanoid): any bone names. One armature, the mesh
   skinned to it. Keep the root bone at the feet.
5. **Anchors** — empties, parented to a bone, named exactly:
   - `BeamOrigin` (the Lure and the Listener): where the beam visibly leaves —
     the mouth, the lamp. The server's line still starts at the eye point; only
     the drawn line moves.
   - `Voice`: where the call comes from (the head).
   - `Eye` (optional): its height becomes the creature's eye height (where it
     looks from and where the Angel is looked at). Without it the setup keeps the
     placeholder's number.
6. **Glowing parts:** any mesh object whose name starts with `Eye` gets the
   pose-driven glow (`CreatureLook`: a slow pulse idle, bright on the hunt,
   steady frozen). The Listener has none — it is blind.
7. **Clips**, one action per pose it animates, named exactly as `CreaturePose`:
   `Idle`, `Drawn` (walking toward a sound or a last-seen spot), `Hunting`
   (walking after a diver), `Frozen` (the Angel, watched), `Windup` (the
   Charger's shake), `Rushing`, `Shooting` (a beam just left), `Fleeing` (the
   Impostor after a touch). Loops: every pose but `Shooting`, which plays once
   and holds. A pose with no clip falls back to the code bob — a half-finished
   model still reads. Walk cycles are **in place** (the server moves the body;
   root motion is off). The Animator also gets `Speed` (flat m/s, smoothed) and
   `Moving` if a blend tree ever wants them.
8. **Materials:** in the FBX (imported "via material description", kept in the
   prefab). One or two materials; textures 1k; a bold silhouette matters more
   than detail (R.E.P.O./PEAK). Polycount: 5–15 k triangles is plenty.
9. **Export:** Armature + Mesh + Empty; "Bake Animation", **all actions**, no NLA
   strips needed; leaf bones off.

## Applying it

- Unity menu **Sunk Cost → Look → Apply monster models (every kind with an
  FBX)**, or `MonsterModelSetup.Apply(kind)` from a check. It sets the importer
  (Generic, clips looping by pose), builds
  `Assets/_Project/Animation/Monsters/<Kind>.controller` (Any State → the pose's
  state on the `Pose` int, 0.15 s blend, Idle by default), nests the model under
  the prefab as `Model` in place of the code-built placeholder, wires
  `CreatureRig` (animator, `BeamOrigin`, `Voice`, which poses are animated),
  points `CreatureLook` at the model and its `Eye*` renderers, and reads the eye
  height off an `Eye` node. Re-run after every re-export.
- **Sunk Cost → Prototype → Apply monster setup…** with *force* rebuilds the
  placeholders and drops the model: run the model setup again after it.
- **See it:** **Sunk Cost → Look → Monster portraits (Temp/look)** — the plain
  portrait per kind, and for a modelled kind a **pose sheet**: one frame per clip
  (`Temp/look/monster-<Kind>-<Pose>.png`). Dan looks before anything goes in.
- **Play it:** the monsters matrix (`monsters` job) still passes with a model on;
  a model changes no rule.

## Not yet

- The Impostor keeps its placeholder: it must wear the diver's own model
  (`PlayerBodySetup`), a card for when the diver has a rig.
- Sounds per monster (the calls) are `AudioLibrary` slots, unchanged by this.
