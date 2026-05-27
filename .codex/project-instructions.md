# Codex Project Instructions

Follow these instructions for every MapleClaude phase task in this workspace.

## Required behavior

- Read the relevant phase text in `docs/roadmap.md` before implementing phase
  work.
- Treat phase criteria as absolute. Do not edit or reinterpret them unless the
  user explicitly requests a roadmap change.
- Use branch names in the format `phase-N/short-slug` for phase work.
- Before editing phase code, ensure the phase has a dedicated Git worktree.
- Do not work on multiple phases in the same Git worktree.
- If the current folder is already a dedicated worktree for the requested phase,
  continue there.
- If the current folder is not dedicated to the requested phase, create or ask to
  create a sibling worktree first.
- Preferred worktree folder format: `MapleClaudeClient.phase-N` or
  `MapleClaudeClient.phase-N-short-slug`.
- Base new phase branches on the latest `upstream/master` when safe.
- Do not overwrite, reset, discard, delete, or reuse another phase's worktree
  changes.
- Do not delete old worktrees or branches unless the user explicitly requests
  cleanup.
- Recommended model: one phase branch per worktree, one chat per worktree.
- Use IDA Pro MCP against the actual v95 IDB/PDB before making implementation
  claims about original-client behavior.
- Use Kinoko source as the authority for server/protocol behavior.
- Use shared WZ assets as the authority for asset paths and WZ node structure.
- Do not invent behavior when authoritative evidence is missing.
- Preserve existing protocol, cipher, transfer, and warp behavior unless the
  phase explicitly requires a change.
- Compile MapleClaude at the end of implementation work.

## Commit and PR requirements

- Sanitize all commit messages and PR text.
- Do not include local workspace paths, machine names, personal names, IP
  addresses, MAC addresses, phone numbers, credentials, or private environment
  details.
- Use this structure for detailed phase commits and PR bodies:

```md
phase-N(scope): concise phase change title

## Summary

- What changed.
- Which phase criteria it satisfies.
- Which IDA/Kinoko/WZ evidence was used when relevant.

## Test plan

- [x] Build or tests that passed.
- [x] IDA/Kinoko/WZ checks that were completed.
- [x] Live smoke checks completed by the user or agent.
- [ ] Any manual checks still pending.

## Risk assessment

- **Protocol:** yes/no - explain.
- **Cipher:** yes/no - explain.
- **Asset:** yes/no - explain.
- **Future-compat:** low/moderate/high - explain.
```

- Do not include a `## Recommendation` section unless the user explicitly asks
  for it.
- After every commit or amend, print the final commit hash and full commit
  message for immediate audit.

## New-chat startup prompt

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
