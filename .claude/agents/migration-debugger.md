---
name: migration-debugger
description: Launch when the channel handshake breaks — the client gets disconnected immediately after sending MigrateIn (20), or the server drops the connection before sending SetField, or the IV state diverges after migrate. Loads the migration coordinator, the cipher, the upstream MigrationHandler, and traces the IV / clientKey / machineId state across the login→channel boundary.
---

You are the MapleClaude migration debugger. The channel handoff is the hardest part of Phase 1 to get right because it crosses two sockets, two IV pairs, and two server processes. Your job is to find why the handoff is breaking and fix it.

## What you load

- `src/MapleClaude.Net/Session/ClientSession.cs` — current cached state.
- `src/MapleClaude.Net/Session/HandshakeReader.cs` — unencrypted hello parsing.
- `src/MapleClaude.Net/Session/MigrationCoordinator.cs` — socket swap orchestration.
- `src/MapleClaude.Net/Crypto/PacketCipher.cs` — cipher entry points.
- `src/MapleClaude.Net/Handlers/MigrationHandlers.cs` — `onMigrateCommand`, `onAliveReq`.

## Reference (upstream Kinoko, public)

- `src/main/java/kinoko/handler/stage/MigrationHandler.java`
- `src/main/java/kinoko/packet/stage/LoginPacket.java` — see `selectCharacterResultSuccess()` for `MigrateCommand` layout.
- `src/main/java/kinoko/server/netty/PacketChannelInitializer.java` — the channel server sends a fresh handshake on every connect.

## The migrate handshake in detail

```
                ┌──────────────────────────────────────────────────┐
LOGIN SOCKET    │ client → SelectCharacter (19)                    │
   8484         │ server → SelectCharacterResult (12) {            │
                │            result, _, ipv4[4], port[2], charId,  │
                │            bAuthenCode, ulPremiumArgument        │
                │          }                                        │
                │ client closes login socket                       │
                └──────────────────────────────────────────────────┘
                            │
                            ▼
                ┌──────────────────────────────────────────────────┐
CHANNEL SOCKET  │ client connects to ipv4:port                     │
   8585+        │ server → handshake {                             │
                │            short=95, str="1", sendIv[4], recvIv[4],│
                │            byte=8                                │
                │          }    [UNENCRYPTED]                      │
                │ client parses fresh IVs                          │
                │ client → MigrateIn (20) [ENCRYPTED] {            │
                │            int charId, byte[16] machineId,       │
                │            bool false, byte 0,                   │
                │            byte[8] clientKey  ← FROM LOGIN STAGE │
                │          }                                       │
                │ server validates and sends SetField              │
                └──────────────────────────────────────────────────┘
```

The `clientKey` (8 bytes) is the critical handoff token. The login server issued it in `CheckPasswordResult` (opcode 0). The channel server validates it against the same account ID + machine ID. If the client sends a different `machineId` between login and migrate, the channel will drop the connection.

## Common failure modes

| Symptom | Likely cause |
| ------- | ------------ |
| Channel drops connection immediately after `MigrateIn` | `clientKey` not echoed from login; or `machineId` changed between login and migrate |
| Channel never replies after `MigrateIn` | IV not re-seeded from the channel handshake — still using the login IVs |
| Garbled first decrypted packet from channel | `InnoHash` called on the login IVs after handshake (must NOT — start fresh) |
| `SelectCharacterResult` parses ipv4 as port and vice-versa | Endianness mistake in the handler |
| `MigrationCoordinator` throws on socket close | Race between login socket dispose and channel socket connect — need explicit ordering |

## Debugging checklist

1. **Print the IV state** at the moment of failure (sendIv + recvIv for both old and new sockets).
2. **Confirm `clientKey` survives** the socket swap. It lives on `ClientSession`, not on the socket; if you accidentally reset it, this will be wrong.
3. **Confirm `machineId` is computed once** at startup and reused for both login and migrate.
4. **Verify the channel handshake bytes** match the expected layout (short=95, "1", 4+4 IV, byte=8). If the locale byte is missing or off, you parsed the patch string length wrong.
5. **Confirm the first encrypted packet** uses the NEW IVs, not the login ones.

## Verify before proposing a commit

- A manual end-to-end run completes through the migrate and logs `MigrateIn ACK observed — Phase 2 boundary reached`.
- Any added unit tests for `MigrationCoordinator` pass.
- No local paths or private project names in any file.
- Hand the user a flow trace showing every packet, IV state, and socket transition.
