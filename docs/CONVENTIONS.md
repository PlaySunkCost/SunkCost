# Working conventions

Three people, AI-assisted, one repo. These rules exist to stop the two failure modes
that actually kill projects like this: architectural drift, and scene merge conflicts.

## Scenes and prefabs

**One person edits a given scene at a time.** Say it in chat before you open it, say
it when you're done. There is no tooling that saves you here — Unity's YAML merges
badly and a conflicted scene usually means someone loses an hour of work.

Configure Unity's merge tool once per machine so the occasional collision is
survivable: `git config merge.unityyamlmerge.driver` pointing at Unity's
`UnityYAMLMerge` binary (it ships inside the editor install, under `Editor/Data/Tools`).

Prefer prefabs over scene objects. A prefab is owned by one person and edited in
isolation; a scene is shared ground. If you find yourself hand-placing things in a
scene, ask whether it should be a prefab instead.

## Files

**Small, single-purpose files.** One class, one job, one file. This is not style
pedantry — it is what makes AI-assisted edits reliable. A 900-line file is where an
assistant confidently rewrites something it didn't need to touch.

If a file is getting long, that is the signal to split it, not to add a region.

## Folders

Everything we write lives under `Assets/_Project/`. The leading underscore keeps it
pinned at the top of the project window, above the asset store imports.

```
Assets/
  _Project/          <- ours
    Scripts/
    Prefabs/
    Scenes/
    Art/
    Audio/
    Settings/
  Plugins/           <- third party, never edited
  ThirdParty/
```

Never edit anything outside `_Project/`. If an asset pack needs a change, wrap it.

## Namespaces

`SunkCost.<Area>` — `SunkCost.Noise`, `SunkCost.Diving`, `SunkCost.Economy`. Matches
the folder. Keeps AI-generated code landing in the right place.

## Ownership

| Area | Owner |
|---|---|
| Networking, player controller, grab/carry/drag | **Dev A** (has shipped before) |
| Monster AI, noise system, level tooling, sites | **Dev B** |
| Oxygen, loot, weight, economy, quota, UI, audio | **Dev C** |

Work inside your area freely. Crossing into someone else's means a conversation first
— not a permission slip, just a heads-up so two people don't solve the same thing
differently on the same afternoon.

## One path for anything shown or used twice (Dan, 17 September 2026)

Most of what the game looks like will change — the visor, its bars, the panels,
the sounds. So anything that can appear in more than one place is **built once and
fed data**, never copied:

- The visor is drawn by one method (`PlayerHudUI.DrawVisor`) from one state object
  (`PlayerHudUI.VisorFrame`, filled by `PlayerHudUI.Compute` for *any* player from
  *any* camera). The owner's own screen, a dead player's spectator view and the deck
  TV's picture all go through it. Change the visor's look there and it changes in
  all three; do not draw a "TV version" or a "spectator version" of anything.
- The same for readouts and panels: the storage readout, the HQ board, the cabin
  panel each have one writer that formats the text; another surface that needs the
  same words calls that writer.
- Game sounds a second place must play (the deck TV) are flagged where they are
  (`BroadcastSound`), not re-triggered by hand elsewhere.
- Rule of thumb before adding a screen, a panel or an effect: *whose data is this
  and where else could it show?* If the answer is "somewhere else too", write the
  data-in, pixels-out function first and call it from both places. A copy that
  drifts is a bug we will only find in a playtest.

## Pull requests

- Gameplay tweaks: merge your own, no ceremony.
- **Anything touching `docs/NETWORK_CONTRACT.md`, or any RPC or SyncVar: second pair
  of eyes, always.** This is the one rule worth being annoying about.
- Commit messages say what changed and why, not "fixes".

## The AI rule

Assistants write confident, plausible, wrong netcode. Everything that crosses the
network gets read by a human before it merges — read, not skimmed. Everything else
you can trust and move fast on.
