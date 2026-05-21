---
name: client-packet-author
description: Use when authoring an outgoing client→server packet in MapleClaude.Net. Triggers on phrases like "send a packet", "client request for X", "add a C→S packet", or any reference to an InHeader entry. Locates the upstream Kinoko InHeader entry and the matching server-side handler, then generates a C# encoder in the calling Stage that writes fields in the order the server reads them.
---

# client-packet-author

When you're writing a packet the client sends to the server, walk through these steps:

## 1. Identify the opcode

Look in upstream Kinoko `src/main/java/kinoko/server/header/InHeader.java`
(repo: <https://github.com/iw2d/kinoko>) for the named opcode. Record the
numeric value. Confirm it's in `src/MapleClaude.Net/Packet/OpCodes.cs` (regenerate
via `tools/gen-opcodes` if it isn't).

## 2. Find the server-side reader

Search Kinoko's `src/main/java/kinoko/handler/` tree for the matching
`@Handler(InHeader.<YourOpcode>)` annotation. The method body shows the read
order — that's the order you must write in.

| Java read | C# write |
| --------- | -------- |
| `inPacket.decodeByte()` | `WriteByte(x)` |
| `inPacket.decodeShort()` | `WriteShort(x)` |
| `inPacket.decodeInt()` | `WriteInt(x)` |
| `inPacket.decodeLong()` | `WriteLong(x)` |
| `inPacket.decodeString()` | `WriteString(s)` (length-prefixed `short`) |
| `inPacket.decodeArray(n)` | `WriteBytes(buf)` (length is contextual) |

## 3. Write the packet

Build the `OutPacket` in the calling Stage or in a static `Packets.Build*`
helper (preferred when the packet is reused):

```csharp
public static OutPacket BuildExample(int charId, ReadOnlySpan<byte> machineId)
{
    var p = OutPacket.Of(InOp.Example);
    p.WriteInt(charId);
    p.WriteBytes(machineId);
    p.WriteByte(0);
    return p;
}
```

Call `session.Send(BuildExample(...))`.

## 4. Verify before committing

- Run any unit tests in `MapleClaude.Net.Tests` that exercise the new packet.
- If feasible, smoke-test against a running Kinoko server and watch the server
  logs for a parse error on the new opcode.
- Confirm no local paths or private project names in the file.
- Propose the commit to the user; wait for approval.

## Hand-off

If the C→S packet is part of a multi-step flow (e.g. login → world list → char select),
launch the `kinoko-protocol-mirror` agent to coordinate the whole sequence.
