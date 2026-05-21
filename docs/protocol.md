# MapleClaude — Wire Protocol

This document describes the protocol MapleClaude speaks with the upstream
Kinoko server (<https://github.com/iw2d/kinoko>). It mirrors what the server
sends and expects. Java reference paths below are relative to that repo.

## Versioning

- `GAME_VERSION = 95`
- `LOCALE = 8`
- `PATCH = "1"`

## Connection lifecycle

```
1. TCP connect to LOGIN_HOST:8484
2. Recv unencrypted handshake from server
3. Encrypted bidirectional packet traffic until …
4. Server sends MigrateCommand (in SelectCharacterResult) with channelIp:port
5. Client closes login socket
6. TCP connect to channelIp:channelPort
7. Recv fresh unencrypted handshake (new IVs)
8. Client sends encrypted MigrateIn (20)
9. Server sends SetField → Phase 2 boundary
```

## Handshake (unencrypted)

Sent from the server immediately on TCP accept. Built by
`LoginPacket.connect(sendIv, recvIv)` (Java) and framed by a 2-byte
little-endian length prefix.

Layout after the length prefix:

| Bytes | Type | Value | Notes |
| ----- | ---- | ----- | ----- |
| 2 | short LE | 95 | `GAME_VERSION` |
| 2+n | string | `"1"` | length-prefixed (short LE) ASCII bytes |
| 4 | bytes | sendIv | seeds the client's *send* IV |
| 4 | bytes | recvIv | seeds the client's *recv* IV |
| 1 | byte | 8 | `LOCALE` |

The server's `sendIv` becomes the client's `recvIv` and vice versa.

## Packet framing (encrypted)

Every packet after the handshake:

```
[ header(4 bytes) | encrypted_body(N bytes) ]
```

### Header

```
rawSeq  = (iv[2] | (iv[3] << 8)) ^ (0xFFFF - GAME_VERSION)    // both LE short
dataLen = payloadLen              ^ rawSeq
```

Both fields are little-endian unsigned 16-bit. The header is built/parsed
against the same IV that will encrypt/decrypt the body.

### Body cipher

```
encrypt(body, iv):
    body = ShandaCrypto.Encrypt(body)
    body = MapleCrypto.Crypt(body, iv)     // AES-128 ECB with expanded IV per 1456-byte block
    iv   = IgCipher.InnoHash(iv)           // rotate

decrypt(body, iv):
    body = MapleCrypto.Crypt(body, iv)
    body = ShandaCrypto.Decrypt(body)
    iv   = IgCipher.InnoHash(iv)
```

Both directions keep two IVs (one for sending, one for receiving). The
appropriate IV rotates **after** every packet.

References:

- `src/main/java/kinoko/server/netty/PacketEncoder.java`
- `src/main/java/kinoko/server/netty/PacketDecoder.java`
- `src/main/java/kinoko/util/crypto/{MapleCrypto,ShandaCrypto,IGCipher}.java`

## Opcodes used in Phase 1

### Client → Server (`InHeader`)

| Name | Op | Notes |
| ---- | -- | ----- |
| `CheckPassword` | 1 | id, pwd, machineId[16], gameRoomClient(int), gameStartMode(byte), worldId(byte), channelId(byte), partnerCode(int) |
| `WorldInfoRequest` | 4 | empty payload |
| `SelectWorld` | 5 | gameStartMode(byte), worldId(byte), channelId(byte), unk(int) |
| `CheckPinCode` | 9 | dormant in Kinoko |
| `UpdatePinCode` | 10 | dormant in Kinoko |
| `WorldRequest` | 11 | alternative WorldInfo request |
| `SelectCharacter` | 19 | charId(int), mac(string), macWithHddSerial(string) |
| `MigrateIn` | 20 | charId(int), machineId[16], bool false, byte 0, clientKey[8] |
| `CheckDuplicatedID` | 21 | name(string) |
| `CreateNewCharacter` | 22 | name(string), race(int), subJob(short), appearance[8 int], gender(byte) |
| `DeleteCharacter` | 24 | secondaryPassword(string), charId(int) |
| `AliveAck` | 25 | empty payload — reply to server's AliveReq |

### Server → Client (`OutHeader`)

| Name | Op | Notes |
| ---- | -- | ----- |
| `CheckPasswordResult` | 0 | result, account fields..., bSkipPinCode(bool), bLoginOpt(byte), clientKey[8] |
| `CheckPinCodeResult` | 6 | dormant in Kinoko |
| `UpdatePinCodeResult` | 7 | dormant in Kinoko |
| `WorldInformation` | 10 | repeated, terminated by sentinel record with worldId=-1 |
| `SelectWorldResult` | 11 | result, char count, repeating: AvatarData blobs |
| `SelectCharacterResult` | 12 | result, skip byte, channelIp[4], channelPort(short), charId(int), bAuthenCode(byte), ulPremiumArgument(int) |
| `CheckDuplicatedIDResult` | 13 | name(string), result(byte) |
| `CreateNewCharacterResult` | 14 | result, AvatarData on success |
| `DeleteCharacterResult` | 15 | charId(int), result(byte) |
| `MigrateCommand` | 16 | embedded in `SelectCharacterResult` flow |
| `AliveReq` | 17 | empty payload — client replies AliveAck |

The full machine-generated list lives in `src/MapleClaude.Net/Packet/OpCodes.cs`.

## AvatarData encoding

The most complex sub-encoding in Phase 1. Mirrored byte-for-byte from
upstream `src/main/java/kinoko/world/user/AvatarData.java`. A captured fixture
lives in `tests/MapleClaude.Net.Tests/Fixtures/avatar_data.bin` (added on the
`phase-1/char-select` branch) and a round-trip test in `AvatarDataTests.cs`.

## Keepalive

The server occasionally sends `AliveReq` (17) with an empty payload. The
client replies with `AliveAck` (25), also empty. No fixed interval; the
exchange is reactive.

## Migration handshake summary

```
[login socket]
  client → SelectCharacter (19) {charId, mac, mac+hdd}
  server → SelectCharacterResult (12) {result, _, ipv4, port, charId, bAuthen, ulPremium}
  client closes login socket

[channel socket]
  client connects to (ipv4, port)
  server → handshake (unencrypted): version, patch, sendIv, recvIv, locale
  client parses fresh IVs (does NOT carry login IVs forward)
  client → MigrateIn (20) {charId, machineId, false, 0, clientKey[8 from login]}
  server → SetField (Phase 2 boundary; client logs and shows PendingPhase2Stage)
```

Reference: `src/main/java/kinoko/handler/stage/MigrationHandler.java` and
`LoginPacket.selectCharacterResultSuccess()`.

## Things that are NOT yet wired

- `LoginOpt` (secondary password): the field is parsed from
  `CheckPasswordResult` but the dedicated stages are out of scope for Phase 1.
- HWID-based ban checks rely on the `machineId` we compute in
  `src/MapleClaude/Platform/MachineId.cs`; Kinoko reads but does not validate
  in its current open-source state.
- MAC address strings (`mac`, `mac+hdd`) are sent but Kinoko discards them
  today. We still compute real values for realism / future compatibility.
