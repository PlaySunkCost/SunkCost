# The first-person body — 17 September 2026 — figure MATRIX_PASS (22 rows), spectate MATRIX_PASS (210 rows)

Dan: "I want to build something" → the visible first-person body; then, on
finding the prefab already carries Dor's `GenericCharacter`, "I prefer to use
Dor's thing". A primitive figure with animated legs was built and thrown away.

## Built, on Dor's model
- `CharacterModelScale` 1.0 → **1.49**: the 1.21 m chibi stands the 1.8 m of
  the capsule; a friend is diver-sized, the owner's eyes are in its head.
- `PlayerHeadSplitSetup` (editor, run by the movement and hands setup): the
  model's mesh is cut at the neck (0.685 m in its own frame, the narrowest
  ring per `CharacterModelProbe`) into two mesh assets under
  `Models/Generated` — body 9536 triangles, head 1728 — the model shows the
  body and a `Head` child shows the head with the same materials; a dark
  flattened sphere plugs the neck so the owner never looks down into a hollow
  torso. The .blend is untouched. Mesh y is not height (the importer keeps
  the .blend's Z-up axes in mesh space), so the cut measures up the player's
  root — the first cut had 0 head triangles for that reason.
- `PlayerHeadSplit` (on the prefab): knows the head renderers (the cut head,
  Eye.L, Eye.R). `HQPlayerController.SetLocalPresentation` now shows the
  owner its body and hides only the head; dead/travel hide it whole as
  before; the crouch squash is unchanged.

## Runs
- `figure` — MATRIX_PASS ([figure-matrix.log](figure-matrix.log)): the cut,
  the owner without its head and with its body, capsule-tall, in view when
  looking down ([figure-own-body.png](figure-own-body.png)), the crouch
  ([figure-own-crouch.png](figure-own-crouch.png)); a guest's head, body and
  colour as the host sees them ([figure-friend.png](figure-friend.png)).
- `spectate` — MATRIX_PASS, 210 rows, guest logs clean: dead and travelling
  players still hide whole, revives show them again.

## Left for Dor (Notion card "Rig GenericCharacter")
Legs that swing, a head that turns with the look pitch for friends, a real
neck: an armature with neck, hips and knees and an idle/walk cycle. The cut
goes away the day the head is its own bone.
