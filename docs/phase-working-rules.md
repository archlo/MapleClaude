# Phase Working Rules

These rules are the project-level operating contract for phase work. They are
intended to be reused across chats so each new thread starts from the same
expectations.

## Phase authority

- Treat `docs/roadmap.md` phase criteria as absolute.
- Do not change, weaken, reinterpret, or rewrite phase entry criteria, exit
  criteria, scope, deferred items, or acceptance language unless the user
  explicitly asks for a roadmap edit.
- Derive implementation requirements from the phase text first, then verify
  against authoritative sources.

## Branch naming

- Use phase-readable branch names for phase work.
- Preferred branch format: `phase-N/short-slug`.
- Example: `phase-30/portal-rendering`.
- Avoid opaque generated branch names that do not identify the phase.

## Worktree isolation

- Do not work on multiple phases in the same Git worktree.
- Before editing phase code, ensure the phase has a dedicated Git worktree.
- If the current folder is already a dedicated worktree for the requested phase,
  continue there.
- If the current folder is not dedicated to the requested phase, create or ask to
  create a sibling worktree before editing.
- Preferred worktree folder format: `MapleClaudeClient.phase-N` or
  `MapleClaudeClient.phase-N-short-slug`.
- Use branch format `phase-N/short-slug` inside that worktree.
- Base new phase branches on the latest `upstream/master` when safe.
- Do not overwrite, reset, discard, delete, or reuse another phase's worktree
  changes.
- Do not delete old worktrees or branches unless the user explicitly requests
  cleanup.
- Recommended model: one phase branch per worktree, one chat per worktree.

## Verification sources

- Use IDA Pro MCP against the actual v95 IDB/PDB before making implementation
  claims about original-client behavior.
- Do not invent v95 behavior. If the IDA evidence is missing or inconclusive,
  say so and keep the claim scoped.
- Use Kinoko server source for server/protocol behavior.
- Use the shared WZ asset files for asset paths, image families, frame data, and
  WZ node verification.
- When behavior depends on IDA, Kinoko, and WZ, report which source proved which
  part.

## Implementation rules

- Preserve existing protocol and warp logic unless the phase explicitly requires
  changing it.
- Keep phase work focused on the phase criteria.
- Do not hide unrelated behavior changes inside a phase commit.
- If a live-test-only item remains, mark it explicitly instead of implying it was
  verified.
- Always compile MapleClaude at the end of implementation work so the user can
  run a live test.

## Commit and PR hygiene

- Commit messages and PR descriptions must be sanitized.
- Do not include local workspace paths, machine names, personal names, IP
  addresses, MAC addresses, phone numbers, private credentials, or private
  environment details.
- Use the project PR structure:
  - `## Summary`
  - `## Test plan`
  - `## Risk assessment`
- Omit `## Recommendation` unless the user explicitly asks for it.
- Phase commit titles should identify the phase and scope.
- Example title: `phase-30(map): render portal animations by portal type`.
- Always show the final commit message after committing or amending so the user
  can audit it immediately.

## Test plan format

- Mark completed checks with `[x]`.
- Mark checks not yet performed with `[ ]`.
- Include build commands that passed.
- Include IDA, Kinoko, WZ, and live smoke checks when they are relevant.
- If the user performs a live smoke test and reports success, record it as a
  completed live smoke check.

## Risk assessment format

- `**Protocol:** yes/no - explain packet or sender impact.`
- `**Cipher:** yes/no - explain crypto impact.`
- `**Asset:** yes/no - explain WZ or asset-reading impact.`
- `**Future-compat:** low/moderate/high - explain deferred or follow-up risk.`

## Default prompt for a new chat

Use this prompt when starting a new chat in this project:

```text
Follow docs/phase-working-rules.md and .codex/project-instructions.md.
Work on Phase <N>: <phase title or short task>.

Before editing, ensure this phase has its own Git worktree. Do not work on
multiple phases in the same workspace. If this folder is not already a dedicated
worktree for Phase <N>, create or ask to create a sibling worktree named
MapleClaudeClient.phase-<N> or MapleClaudeClient.phase-<N>-<short-slug>, using
branch phase-<N>/<short-slug> based on the latest upstream/master when safe.

Do not change phase criteria. Use IDA Pro MCP for v95 behavior, Kinoko source
for server/protocol behavior, and WZ assets for asset verification. Compile
MapleClaude at the end of implementation work. Use phase-N/short-slug branch
names and sanitized commit/PR messages.
```
