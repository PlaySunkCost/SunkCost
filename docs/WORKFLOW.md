# Working together with Claude and Codex

Git shares the project and written decisions. It does not share private chat
history, configure another developer's tools, or refresh an already-running AI
session. After pulling instruction changes, start a new session or explicitly ask
the assistant to reread them.

## Setup

1. Clone this existing project. Do not create another Unity project around it.
2. Install the exact Unity version in `ProjectSettings/ProjectVersion.txt` through
   Unity Hub. Install build modules needed for the team's chosen build target and
   scripting backend; do not assume every developer needs IL2CPP exclusively.
3. Install Git LFS, run `git lfs install`, then `git lfs pull`. Keep Unity's asset
   serialization on Force Text and version-control mode on Visible Meta Files.
   Check settings rather than assuming setup is complete.
4. Open the project root in your preferred assistant. Codex reads `AGENTS.md`;
   Claude Code uses `CLAUDE.md`, which imports the same instructions. Other tools
   should be told explicitly to read `AGENTS.md` and its referenced documents.
5. A Unity MCP bridge is optional. If you have one, configure it using that
   bridge's actual documentation and verify it can read this project's console.
   Do not assume a particular menu, integration, or connection is available.
   Without it, use editor logs and manual checks. Report unavailable tests clearly.

The selected stack is FishNet / Steam P2P, not Unity Netcode for GameObjects.
Inspect the manifest and imported assets before concluding it is installed.
Troubleshoot installation errors using their logs; one exception name does not
prove that reinstalling Unity is the right fix.

## Which file does what?

| File | Purpose |
|---|---|
| `AGENTS.md` | Shared instructions for coding assistants |
| `CLAUDE.md` | Imports those instructions for Claude Code |
| `docs/DESIGN.md` | Current game rules, scope and undecided ideas |
| `docs/NETWORK_CONTRACT.md` | Multiplayer authority and synchronization rules |
| `docs/CONVENTIONS.md` | Code organization, area ownership and review rules |
| `docs/reference/Sunk-Cost-Build.pdf` | Unchanged historical production plan |
| `.claude/agents/*.md` | Specialized review and debugging prompts |

Use any task board the team chooses for assignments and playtest notes. Approved
design and architecture changes belong in the repository, with a reason; proposals
must stay labelled as proposals. There is no approved new launch deadline, price,
team arrival date or binding cut list in this workflow. Consult the current design.

## Daily loop

1. Pick a bounded task and check the working tree. Coordinate shared files with
   teammates. Area ownership stays in `docs/CONVENTIONS.md`, not a duplicate table.
2. Work on a task branch, for example `codex/elevator-boarding` or a team-chosen
   branch name. Direct work on the current branch is possible when explicitly
   requested; do not discard or overwrite another person's changes.
3. Read the design and relevant contract before implementation. For new scope,
   use `scope-cop` as advice when useful; required infrastructure, accessibility,
   saving and testing do not need to be visible in a streamer clip to matter.
4. Make the smallest coherent change. Keep design amendments and their code in
   agreement, without quietly recording a new idea as settled.
5. Verify proportionally: Markdown links and diffs for docs; compilation and
   relevant tests for code; host/client scenarios for multiplayer behavior.
6. Use `netcode-reviewer` for networking, ownership, RPC, SyncVar/SyncList or noise
   changes. Supply the diff, relevant code and test evidence. A clean AI report
   does not replace the teammate review required for networking changes.
7. Commit with a reason and open a PR in the normal team workflow. Gameplay tweaks
   may be self-merged; contract, RPC and SyncVar changes require a second person
   under `docs/CONVENTIONS.md`. Never fabricate sign-off. A requested direct push
   should state any review or validation that remains outstanding.

## Using the four reviewers

In Claude Code, request the named custom agent; project agent definitions live in
`.claude/agents/`. Newly added definitions may need a new session or reloading via
`/agents`. In Codex or another assistant, ask it to read the matching Markdown
file and apply its checklist. These are prompts, not Unity scripts or automated
tests, and their tool permissions apply only in the tool that understands them.

- **netcode-reviewer:** finds concrete networking defects against the contract.
- **desync-hunter:** traces where host and client state diverge, then proposes
  one testable fix at a time.
- **plan-auditor:** checks evidence, contradictions, assumptions and arithmetic.
- **scope-cop:** recommends implement, defer or cut, with cost assumptions.

For a desync, record each machine's observed state, object identity, controller,
tick/time, request and ownership transitions. Test one hypothesis at a time. After
two failed fixes, revise the hypothesis and collect distinguishing evidence before
another change. Never call missing client logs a successful test.

## Multiplayer and Unity checks

At minimum, use a host and a separate non-host client. An editor plus one build is
useful during development; validate Steam transport with builds on two machines.
For networking changes, exercise the applicable scenarios: simultaneous grabs,
drop/throw and re-grab, owner disconnect, loaded elevator departure, oxygen item
use, corpse recovery, and dead/surfaced voice isolation. Test latency and packet
loss where tooling permits; record actual conditions rather than invented results.
Test four players before accepting four-player behavior as verified.

One person edits a given scene or prefab at a time. Unity YAML can sometimes be
merged using UnityYAMLMerge, but a clean text merge does not prove the asset works.
Have the owners inspect it in Unity; if it cannot be verified, reapply one change.
Never regenerate `.meta` files merely to clear a conflict.

Playtest the ugly core loop regularly: descend, haul salvage, bank it through the
shared elevator, rescue or abandon bodies, surface and meet the quota. Record what
players actually do before expanding content. This is a practice, not an invented
calendar commitment or a change to the launch scope in `docs/DESIGN.md`.

## Editor matrices

The Play Mode checks (`Assets/_Project/Editor/Prototype/*RuntimeChecks.cs`,
started through `CameraClearanceMatrixDriver.Start(job)`) and how to write and
run one without losing an afternoon: [EDITOR_MATRIX_PLAYBOOK.md](EDITOR_MATRIX_PLAYBOOK.md).
The scripted full playthrough and its recorded runs: [FULL_RUN_PLAYTHROUGH.md](FULL_RUN_PLAYTHROUGH.md).

## Continuous integration

**Every push to `main` produces a Windows build you can download from the
Releases page: https://github.com/PlaySunkCost/SunkCost/releases/latest.**
Download the `SunkCost-Windows-build<N>-<commit>.zip`, extract it anywhere and
run `SunkCostHQ.exe` with Steam running. Each release is named after the pull
request that was merged and says which commit it was built from; the ten newest
are kept. The same zip is also attached to the workflow run itself (Actions →
the run → **Artifacts** at the bottom; needs a GitHub login, kept 90 days), so
every build stays downloadable after its release has been pruned. A build can
also be started by hand from the Actions tab (**Windows build → Run workflow**).

`.github/workflows/build.yml` does this on a GitHub-hosted `ubuntu-latest`
runner through `game-ci/unity-builder`, which cross-builds Windows from a
Linux Unity Editor image. It calls `HQPrototypeBuild.BuildWindowsCI`
(`Assets/_Project/Editor/Prototype/HQPrototypeBuild.cs`), the CI twin of
**Sunk Cost/Prototype/Build Windows Development**: the same identity-stamped
shared Steam build, with the pushed commit as its revision, so everyone on the
same release can join each other. Unity rewrites `Packages/manifest.json` and
`packages-lock.json` when it imports on the runner; `BuildWindowsCI` tolerates
exactly that and refuses any other difference from the pushed commit. The
Library cache is saved only after a successful build. Documentation-only pushes
do not build.

This needs a one-time Unity license secret, since headless Unity requires
activation. GameCI's `unity-request-activation-file` action is retired; get the
license locally instead (https://game.ci/docs/github/activation):

1. On a machine with Unity Hub, sign in with the Unity account that will hold
   this project's seat, then go to **Preferences → Licenses → Get a free
   personal license** (Personal is fine for a project this size; already done
   if that account activated Unity Hub normally).
2. Find the resulting `Unity_lic.ulf` file: `C:\ProgramData\Unity\Unity_lic.ulf`
   (Windows), `/Library/Application Support/Unity/Unity_lic.ulf` (Mac), or
   `~/.local/share/unity3d/Unity/Unity_lic.ulf` (Linux).
3. Add three repository secrets (Settings → Secrets and variables → Actions):
   `UNITY_LICENSE` (the full contents of that `.ulf` file), `UNITY_EMAIL`, and
   `UNITY_PASSWORD` (the same Unity account's login — GameCI's activation step
   needs these alongside the license file; it does not store them).

A Unity seat and its credentials are the account holder's decision, not
something an assistant should provision. Re-run step 1 if the license ever
needs re-activating (e.g. after Unity revokes it for concurrent use).

## Tool documentation

- [Codex repository instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
- [Claude Code memory and imports](https://code.claude.com/docs/en/memory)
- [Claude Code custom subagents](https://code.claude.com/docs/en/sub-agents)
