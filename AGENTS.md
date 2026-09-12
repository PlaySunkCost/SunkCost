# Sunk Cost — shared assistant instructions

These instructions apply to the repository, whether you use Codex, Claude Code,
or another assistant. Work from the project root and read the relevant files
before editing:

- [docs/DESIGN.md](docs/DESIGN.md): current gameplay decisions and open questions.
- [docs/NETWORK_CONTRACT.md](docs/NETWORK_CONTRACT.md): multiplayer authority,
  ownership, replication and disconnect rules; required for networked changes.
- [docs/CONVENTIONS.md](docs/CONVENTIONS.md): code layout and team ownership.
- [docs/WORKFLOW.md](docs/WORKFLOW.md): setup, verification and review process.

The reference PDF is historical context, not a competing specification. Do not
turn proposals into approved decisions, invent team commitments, or silently
switch the networking stack. If documents conflict, surface the conflict and
resolve it against the user's confirmed decisions before dependent work. Explicit
user instructions take precedence; update affected documents when a change is
authorized. Make routine implementation choices within the agreed design without
asking for permission for every edit.

## Project and implementation

- Unity/C#, URP. Read `ProjectSettings/ProjectVersion.txt` for the exact editor
  version and `Packages/manifest.json` for installed packages.
- The selected networking stack is FishNet over Steam P2P, with FishySteamworks /
  Steamworks.NET as described in the README. Selection does not prove installation.
  Inspect actual packages and imported assets before using their APIs.
- Put game code in `Assets/_Project/`, using `SunkCost.<Area>` namespaces and
  small, single-purpose files. Preserve existing conventions when editing a file.
  Do not modify third-party assets; wrap them. Documentation and project settings
  belong in their normal repository locations outside `_Project/`.
- Follow the contract's server decisions and explicit client simulation exceptions.
  Do not introduce a second physics writer or client-written gameplay SyncVars.
- Use the existing `SunkCost.Noise` API. Gameplay hearing events are server-only;
  local sound effects are separate. Radius is measured in metres.
- Keep gameplay tuning editable through serialized settings or ScriptableObjects.
  Do not require a refactor merely because an existing setting is serialized on a
  component, or because a file crosses an arbitrary line count.
- Keep changes scoped. A dependency or architectural change needs a stated reason;
  do not import unrelated packages or rewrite working systems as incidental cleanup.
- Do not add new gameplay rules to an assistant file. Record them in the design,
  and networking implications in the contract, with their decision status.

## Verification and collaboration

- Inspect the diff and run checks relevant to the change. Documentation changes
  need link and consistency checks, not a multiplayer playtest.
- For code, use the matching Unity editor's compilation and relevant tests where
  available. Use a connected Unity bridge only if it actually exists; otherwise
  inspect available logs or report the exact test that still needs running.
- Multiplayer validation needs a host and a separate non-host client. Use builds
  on separate machines over Steam for transport validation; record roles, build
  revision, latency and results. Compilation alone does not verify replication.
- Networking and contract changes need a teammate's review under the conventions.
  An AI checklist helps but does not count as human sign-off. Never claim an
  approval or test happened without evidence.
- Coordinate scene/prefab editing with the owner. Preserve `.meta` files and GUIDs.
  Resolve serialization conflicts with Unity-aware tooling and editor verification;
  if uncertain, have the owners reapply the smaller change.
- Use a task branch by default (`codex/<task>` for Codex). Follow an explicit
  request to work in the current folder or branch. Preserve unrelated changes.
- Do not commit credentials, generated Unity caches, or assets without appropriate
  redistribution rights. State what changed, what was checked, and remaining gaps.

## Review checklists

The Markdown bodies in `.claude/agents/` are reusable by any assistant:
`netcode-reviewer`, `desync-hunter`, `plan-auditor`, and `scope-cop`.
Their YAML headers configure Claude Code only; Codex can read and apply the
checklists directly. They do not grant tools, require paid models, authorize
delegation, or override the current user request. Read the relevant checklist when
it helps the task; do not run every reviewer for every change.
