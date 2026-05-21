# MapleClaude — WZ File Format Notes (GMS v95)

MapleClaude reads standard GMS v95 WZ files (`UI.wz`, `Map.wz`, `Sound.wz`,
`Character.wz`, `Item.wz`, etc.). The reader is in `src/MapleClaude.Wz/`. The
authoritative reference implementation is upstream Kinoko's
`src/main/java/kinoko/provider/wz/` (<https://github.com/iw2d/kinoko>).

## File layout

```
┌─────────────────────────┐
│ PKG1 header             │
│   magic   "PKG1" (4)    │
│   dataSize  long LE (8) │
│   headerSize  int LE (4)│
│   copyright   string    │  (headerSize - 16 bytes, ASCII)
│   version_hash  short   │
│   ... encryption seed   │
├─────────────────────────┤
│ root WzDirectory        │
│   ... images, dirs, ... │
├─────────────────────────┤
│ packed data segments    │
│   strings, canvases,    │
│   sounds, properties    │
└─────────────────────────┘
```

The file is memory-mapped; node references are byte offsets resolved on demand.

## Version hash

The 16-bit version hash validates a WZ file was packed by the expected client
version. For GMS v95, derived from the ASCII string `"95"` via:

```
hash = 0
foreach char c in versionString:
    hash = (hash * 32) + (c + 1)
versionHash = (0xFF ^ ((hash >> 24) & 0xFF) ^ ((hash >> 16) & 0xFF) ^ ((hash >> 8) & 0xFF) ^ (hash & 0xFF)) & 0xFFFF
```

Cross-check against the server's `WzPackage.from()` implementation if the
detection ever fails.

## Compressed int

Variable-length signed integer:

- Read one byte. If it equals `0x80` (sbyte.MinValue), read the next 4 bytes
  as a little-endian int32.
- Otherwise, the byte (as a signed int8) is the value.

Same pattern for long (`0x80` → next 8 bytes as int64).

## Strings

WZ strings are length-prefixed via a compressed int. Sign of the prefix
distinguishes encoding:

- **Negative prefix**: ASCII (one byte per char), XOR-decrypted with an
  incrementing mask byte starting at `0xAA`.
- **Positive prefix**: UTF-16LE (two bytes per char), XOR-decrypted with an
  incrementing mask short starting at `0xAAAA`.

The XOR mask is then further xor'd with the WZ-key stream produced by
`WzCrypto` (AES-CFB on a known IV). This second layer is what makes WZ
strings "encrypted" rather than simply masked.

## Property tree

A WzImage parses into a property tree. Property tags:

| Tag | Type |
| --- | ---- |
| `0x00` | Property (empty / placeholder) |
| `0x02` | Float32 |
| `0x03` | Compressed int |
| `0x04` | Long |
| `0x05` | Double |
| `0x08` | String (UOL string table entry) |
| `0x09` | Extended property (nested with type name string) |

Extended property type names include `Property`, `Canvas`, `Shape2D#Convex2D`,
`Shape2D#Vector2D`, `Sound_DX8`, `UOL`.

## Canvas decoding

```
header: width(int), height(int), format(int), formatScale(byte), unknown(int)
payload: compressed pixel data
```

Format determines the bytes-per-pixel:

| Format | Description |
| ------ | ----------- |
| 1 | A4R4G4B4 — 2 BPP |
| 2 | A8R8G8B8 — 4 BPP |
| 513 | R5G6B5 — 2 BPP |
| 1026 | DXT3 — compressed |
| 2050 | DXT5 — compressed |

`formatScale` is an additional downscale factor.

Pixel-data stream is either LZ4 or Deflate (detect from the first 2 bytes;
zlib header is `0x78`). After decompression, the bytes pass through AES-128
ECB with the WZ key. The output is then converted to BGRA32 for upload to
the GPU.

## UOL nodes

A UOL ("User Object Link") is a symlink-in-WZ — a node whose payload is a
string path that resolves to another node, possibly in another image. Resolve
lazily on traversal. Cache the resolution per `WzPath` to avoid re-walking.

## Sounds

Sound nodes wrap raw audio data (PCM or MP3 depending on the WAV format
header embedded in the payload). Out of scope until Phase 10.

## Things that bite

- **Endianness**: every multibyte field is little-endian. There is no big-endian
  data anywhere in WZ.
- **Offsets are relative to the file start**, not to the current node.
- **String dedup**: the same string may appear referenced from many properties
  via a "use string table entry" tag. Cache the decoded value.
- **Image lazy-load**: a `WzImage` payload only parses when you walk into it.
  Don't eagerly decode every image at startup — the UI.wz alone has thousands.

## Reference

- <https://github.com/iw2d/kinoko/tree/main/src/main/java/kinoko/provider/wz>
- `WzPackage.java`, `WzReader.java`, `WzCrypto.java`, `WzImage.java`,
  `WzCanvas.java`, `WzDirectory.java`, `WzUol.java`.
