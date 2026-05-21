---
name: crypto-validator
description: Use when editing any file under src/MapleClaude.Net/Crypto/ or when investigating a packet desync. Triggers on references to AES, Shanda, IV, IGCipher, InnoHash, packet cipher, or header XOR. Ensures the C# pipeline produces byte-identical output to the upstream Kinoko Java reference using the known-vector tests in MapleClaude.Net.Tests.
---

# crypto-validator

The cipher pipeline is the most fragile part of the protocol layer. A
single-byte divergence anywhere makes the entire session unrecoverable. Every
change to `src/MapleClaude.Net/Crypto/` must pass byte-exact round-trips
against the Java reference.

## Pipeline (must match upstream)

```
encrypt(body, iv):
    body = ShandaCrypto.Encrypt(body)            // 3-pass byte shuffle
    body = MapleCrypto.Crypt(body, iv)           // AES-128 ECB, expanded-IV, 1456-byte blocks
    iv   = IgCipher.InnoHash(iv)                 // 4-byte IV rotation

decrypt(body, iv):
    body = MapleCrypto.Crypt(body, iv)           // same call (XOR / ECB symmetric)
    body = ShandaCrypto.Decrypt(body)
    iv   = IgCipher.InnoHash(iv)
```

Header (4 bytes, LE):

```
rawSeq  = (iv[2] | (iv[3] << 8)) ^ (0xFFFF - GAME_VERSION)   // GAME_VERSION = 95
dataLen = payloadLen ^ rawSeq
```

## Reference

- IgCipher: <https://github.com/iw2d/kinoko/blob/main/src/main/java/kinoko/util/crypto/IGCipher.java>
- MapleCrypto: <https://github.com/iw2d/kinoko/blob/main/src/main/java/kinoko/util/crypto/MapleCrypto.java>
- ShandaCrypto: <https://github.com/iw2d/kinoko/blob/main/src/main/java/kinoko/util/crypto/ShandaCrypto.java>
- Framing (header XOR): <https://github.com/iw2d/kinoko/blob/main/src/main/java/kinoko/server/netty/PacketEncoder.java> and `PacketDecoder.java`

## Validation checklist

For every change in `Crypto/`:

1. `dotnet test --filter "FullyQualifiedName~Crypto"` is green.
2. Each cipher has at least one **known-vector test** with a fixed input and
   expected output (capture the expected from a quick Java run against the
   same input).
3. `MapleCrypto.Crypt(MapleCrypto.Crypt(buf, iv), iv)` returns `buf` unchanged
   (symmetry test).
4. `ShandaCrypto.Decrypt(ShandaCrypto.Encrypt(buf))` returns `buf` unchanged.
5. `IgCipher.InnoHash` over a known 4-byte seed produces the expected 4-byte
   output for 256 successive iterations.
6. The 4-byte header round-trips: build with `BuildHeader(payload.Length, iv)`,
   parse with `ParseHeader(header, iv)`, and the parsed length matches.

## Common failure modes

- **Wrong endianness on the header** — always little-endian.
- **IV not rotated** between packets — every encrypt AND every decrypt rotates.
- **Block-boundary off-by-one in `MapleCrypto`** — the 1456-byte rule means
  the IV is "expanded" (4 bytes × 4 = 16) and re-keyed each block.
- **Wrong AES key** — must match Kinoko's `MapleCrypto.AES_USER_KEY`.

## Verify before committing

- All cipher tests pass.
- No console writes left in `Crypto/`.
- No local paths or private project names in the file.
- Propose the commit to the user; wait for approval.

## Hand-off

For desync bugs at runtime, launch the `maple-cipher-expert` agent — it loads
the full pipeline + the suspect handlers and traces IV state divergence.
