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
     the mouth, the lamp. The server's judgement and the drawn beam both start
     here (`CreatureBolts.Origin` through `CreatureRig.BeamOriginPoint`, which
     falls back to the eye point when the anchor is missing or implausible).
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

## The generated route (Dan + an image AI + Meshy, 22 September 2026)

How the Listener was made, and how the rest are made:

1. **The concept picture:** an image AI, with the prompt blocks in
   `docs/reference/monster-prompts.md` (the GAME block + the monster's block +
   the OUTPUT block). Keep the sheet: front, side, attack.
2. **The mesh:** crop the front and the side to squares and feed them to
   **Meshy** (Image to 3D, multi-view; realistic; ~15 k polys; **auto-rig off**;
   texture on; symmetry on). Export **FBX** (GLB works too) into
   `Assets/_Project/Models/Monsters/<Kind>/Generated~/` (the `~` keeps Unity from importing the raw download).
3. **The rig:** one command turns that mesh into the model the prefab wears —
   framed to the kind's height with its feet at the origin, decimated to about
   20 k triangles, skinned to the kind's armature, `BeamOrigin` and `Voice` on
   the head, the kind's clips keyed, the maps written out by role:

   ```bash
   "/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b -P tools/blender/rig_generated_monster.py -- <Kind> "Assets/_Project/Models/Monsters/<Kind>/Generated~/<generated>.fbx" "Assets/_Project/Models/Monsters/<Kind>/<Kind>.fbx" 20000
   ```

4. **The prefab:** the menu below (or `MonsterModelSetup.Apply`), which also
   extracts the maps into `<Kind>/Textures`, builds `<Kind>.mat` (URP Lit, base
   colour + normal) and remaps the model onto it.
5. **Look at it:** the portraits menu, then judge the sheet before anything is
   merged.

The clips are keyed by bone name in `tools/blender/make_listener.py`, so a new
kind needs no new animation work: `rig_generated_monster.py`'s `KINDS` table
carries its height, its build, whether it has side bones (ears, a lantern) and
which clips it uses.

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

## Briefs per monster

### The Listener — from Dan's concept, 22 September 2026

Reference: `docs/reference/monsters/listener-concept.webp` — a thin grey
humanoid, a smooth eyeless skull with two huge red fan ears, a spine of spikes
down the back, long clawed hands, digitigrade feet; the attack is a crouch with
the ears fully spread and a violet beam from the open mouth.

- 2.0 m to the top of the skull (the ears above that). Feet at the origin,
  facing −Y in Blender.
- Rig (Generic): Root at the feet → hips → two spine bones → neck → head; one or
  two bones per ear from the skull outward; arms upper/lower/hand; legs
  thigh/shin/foot, the ankle high like the picture. The back spikes are mesh.
- Empties on the head bone: `BeamOrigin` inside the mouth, `Voice` in the skull.
- No `Eye*` meshes. Ear meshes named `EarL`/`EarR` if the ears should glow when
  it hears (a pose-driven glow to wire, Dan's call).
- Clips, in place: `Idle` (straight, ears half-folded, twitches, the head
  turning as if listening), `Drawn` (a careful walk toward a sound, ears fanning
  open, head cocked), `Hunting` (the crouched stance walking: low, hands
  forward, ears wide, quicker), `Shooting` (the attack picture: crouch, ears
  spread, head thrust, mouth open; ~0.5 s, plays once and holds).
- Two materials: the grey body, the red ears. Under ~15 k triangles.

## Not yet

- The Impostor keeps its placeholder: it must wear the diver's own model
  (`PlayerBodySetup`), a card for when the diver has a rig.
- Sounds per monster (the calls) are `AudioLibrary` slots, unchanged by this.
