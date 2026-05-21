---
name: opcode-sync
description: Use when upstream Kinoko's InHeader.java or OutHeader.java has new or renamed opcodes and src/MapleClaude.Net/Packet/OpCodes.cs needs to follow. Triggers on phrases like "sync opcodes", "regen opcodes", "OpCodes.cs is out of date", or after pulling Kinoko upstream.
---

# opcode-sync

`src/MapleClaude.Net/Packet/OpCodes.cs` is a generated mirror of upstream
Kinoko's `InHeader.java` and `OutHeader.java`. Whenever those change, regen
and audit the diff.

## How to regen

```powershell
# Replace the paths with your local Kinoko checkout (don't commit them).
dotnet run --project tools/gen-opcodes -- `
    --in  "$env:KINOKO_SRC\src\main\java\kinoko\server\header\InHeader.java" `
    --out "$env:KINOKO_SRC\src\main\java\kinoko\server\header\OutHeader.java" `
    --emit src/MapleClaude.Net/Packet/OpCodes.cs
```

## Audit the diff

```powershell
git diff src/MapleClaude.Net/Packet/OpCodes.cs
```

Look for:

- **Renames**: a `[Obsolete]` shim may be needed if existing handlers reference
  the old name.
- **Removed opcodes**: handler files in `src/MapleClaude.Net/Handlers/` may now
  dispatch on a dead value. Search and remove.
- **Inserted opcodes**: if the underlying numeric value shifted, downstream
  switches don't break (we use named enum members), but a sanity check that
  the value matches Kinoko's runtime is wise.

## Verify

```powershell
dotnet build
dotnet test
```

Confirm `MapleClaude.Net` and all handler files compile. Run any opcode
round-trip tests in `MapleClaude.Net.Tests`.

## Hand-off

If an opcode change forces a protocol flow rewrite, launch the
`kinoko-protocol-mirror` agent to plan the cascading updates.
