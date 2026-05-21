---
name: server-packet-mirror
description: Use when implementing a server→client opcode handler in MapleClaude.Net. Triggers on phrases like "implement opcode X", "handle a new server packet", "add a S→C handler", or any reference to an OutHeader entry. Locates the upstream Kinoko OutHeader entry and packet builder, then generates a matching C# decoder skeleton in src/MapleClaude.Net/Handlers/ that reads fields in the same order the server writes them.
---

# server-packet-mirror

When you're adding a handler for a packet the server sends to the client, walk through these steps:

## 1. Identify the opcode

Look in the upstream Kinoko `src/main/java/kinoko/server/header/OutHeader.java`
(repo: <https://github.com/iw2d/kinoko>) for the named opcode. Record the numeric value
and the symbol. Add it to `src/MapleClaude.Net/Packet/OpCodes.cs` if missing.

## 2. Find the builder

Search Kinoko's `src/main/java/kinoko/packet/` tree for a method that calls
`OutPacket.of(OutHeader.<YourOpcode>)`. The body of that method is the wire encoding,
in field order, with the exact write calls:

| Java write | C# read |
| ---------- | ------- |
| `outPacket.encodeByte(x)` | `ReadByte()` (signed/unsigned per usage) |
| `outPacket.encodeShort(x)` | `ReadShort()` |
| `outPacket.encodeInt(x)` | `ReadInt()` |
| `outPacket.encodeLong(x)` | `ReadLong()` |
| `outPacket.encodeString(s)` | `ReadString()` (length-prefixed `short`) |
| `outPacket.encodeArray(buf)` | `ReadBytes(len)` (length is contextual; check the builder) |
| `outPacket.encodeFT(time)` | `ReadFileTime()` |

## 3. Generate the handler

Create or extend a handler in `src/MapleClaude.Net/Handlers/` named after the
flow (e.g. `LoginHandlers`, `MigrationHandlers`, `FieldHandlers`). Match the
field order **exactly**.

```csharp
public sealed class ExampleHandler : IPacketHandler
{
    public OutOp Opcode => OutOp.Example;

    public void Handle(ClientSession session, InPacket p)
    {
        var someByte = p.ReadByte();
        var someInt  = p.ReadInt();
        var someStr  = p.ReadString();
        // dispatch to stage / domain
        session.Stage.OnExample(someByte, someInt, someStr);
    }
}
```

## 4. Register it

Add to `PacketRouter` startup wiring. Confirm via a unit test that captures a
real packet against the Kinoko Java server (use `tools/pcap-replay` once it
lands).

## 5. Verify before committing

- `git grep` for `// TODO` and remove or convert to issues.
- Run `dotnet test` on `MapleClaude.Net.Tests`.
- Confirm no local paths or private project names slipped into the file.
- Propose the commit to the user; wait for approval (see `CLAUDE.md` git workflow).

## Hand-off

For deeper work (a flow that spans many packets), launch the
`kinoko-protocol-mirror` agent instead — it loads the full trace through
`kinoko/world/**` and the matching handler chain.
