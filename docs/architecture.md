# MapleClaude — Architecture

This document maps the module dependency graph and the runtime data flow.

## Module dependency graph

```
                       ┌─────────────────────────┐
                       │      MapleClaude        │   <- WinExe (Program, MapleClaudeGame, Stages, UI)
                       └────────────┬────────────┘
                                    │
              ┌─────────────────────┼─────────────────────┐
              │                     │                     │
              ▼                     ▼                     ▼
   ┌──────────────────┐   ┌──────────────────┐   ┌──────────────────┐
   │ MapleClaude.Net  │   │ MapleClaude.Wz   │   │MapleClaude.Render│
   │ Crypto/Packet/   │   │ WZ reader (PKG1, │   │ MonoGame integ.  │
   │ Session/Handlers │   │ WzImage, Canvas, │   │ SpriteAtlas,     │
   │                  │   │ WzCrypto, UOL)   │   │ WzTextureLoader, │
   │                  │   │                  │   │ Camera2D         │
   └────────┬─────────┘   └────────┬─────────┘   └────────┬─────────┘
            │                      │                      │
            └──────────┬───────────┴──────────┬───────────┘
                       │                      │
                       ▼                      ▼
            ┌──────────────────────────────────────┐
            │       MapleClaude.Domain             │   <- POD types, no dependencies
            │ Account, ClientKey, WorldInfo,       │
            │ ChannelInfo, AvatarLook,             │
            │ CharacterEntry, ...                  │
            └──────────────────────────────────────┘
```

- **Domain** is the leaf. No code dependencies. Everyone references it.
- **Net** depends only on Domain. It does NOT depend on MonoGame or WZ — the
  cipher and packet code is testable in isolation.
- **Wz** depends only on Domain. The reader is testable without MonoGame.
- **Render** depends on Wz + Domain + MonoGame. Lifts `WzCanvas` BGRA buffers
  into `Texture2D` atlases.
- **MapleClaude (main app)** depends on everything. Owns the MonoGame `Game`
  subclass, the Stage tree, the UI widgets, the platform interop.

## Test projects

```
MapleClaude.Net.Tests → MapleClaude.Net
MapleClaude.Wz.Tests  → MapleClaude.Wz
```

Tests are deliberately at the package boundary. The cipher tests have no
MonoGame coupling and can run in CI without graphics.

## Runtime data flow

### Network read path

```
Socket (Winsock 2)
   │
   ▼
System.IO.Pipelines (PipeReader)
   │  exposes ReadOnlySequence<byte>
   ▼
ClientSession.ReadLoopAsync
   │  reads 4-byte header → ParseHeader(hdr, recvIv) → payloadLen
   │  reads payloadLen bytes → PacketCipher.Decrypt(buf, recvIv)
   │  rotates recvIv (IgCipher.InnoHash)
   ▼
new InPacket(decryptedBuf)
   │
   ▼
PacketRouter.Dispatch
   │  resolves IPacketHandler by opcode (first 2 bytes of body)
   ▼
LoginHandlers / MigrationHandlers / FieldHandlers / …
   │
   ▼
Stage.OnPacket / direct state mutation on ClientSession
   │
   ▼
StageDirector.Transition(...) / UI re-render
```

### Network write path

```
Stage (button click / keyboard event)
   │
   ▼
Packets.BuildXxx(args) → OutPacket
   │
   ▼
ClientSession.Send(OutPacket)
   │  PacketCipher.Encrypt(body, sendIv)
   │  BuildHeader(payloadLen, sendIv)
   │  PipeWriter.Write(header + body)
   │  rotates sendIv (IgCipher.InnoHash)
   ▼
Socket
```

### Render path

```
MapleClaudeGame.Update(GameTime)
   │  pumps Win32 input, delegates to active Stage
   ▼
Stage.Update
   │  reads ClientSession state, advances animations
   ▼
MapleClaudeGame.Draw(GameTime)
   │
   ▼
Stage.Draw(SpriteBatch)
   │  begin → draw background, world, UI, overlay → end
   ▼
SpriteAtlas-backed Texture2D blits
   │
   ▼
GraphicsDevice present (vsync)
```

## Threading

| Thread | Owner | What runs there |
| ------ | ----- | --------------- |
| Game / Main | MonoGame | `Update` + `Draw`. **Never block here.** |
| Pipe-read async | ASIO equivalent (PipeReader on ThreadPool) | Socket recv → cipher → router |
| Pipe-write async | PipeWriter on ThreadPool | Cipher encrypt → socket send |
| Logger sinks | Serilog | File / Console / Debug sink writers |

The handler dispatch is the seam: handlers run on the pipe-read thread, but
should not touch MonoGame `GraphicsDevice` directly. They mutate
`ClientSession` state; the next Stage `Update` picks the change up.

For UI state that requires immediate MonoGame-thread work (e.g. push a new
stage on `MigrateCommand`), handlers post to a thread-safe queue on
`ClientSession` that the Stage's `Update` drains.

## Phase 1 file map

```
src/
  MapleClaude/
    Program.cs                            Generic host + Serilog bootstrap
    MapleClaudeGame.cs                    MonoGame Game subclass
    App/Stage.cs                          abstract Stage
    App/StageDirector.cs                  stack-of-stages
    Platform/MachineId.cs                 16-byte machine fingerprint
    Platform/MacAddress.cs                MAC string for SelectCharacter
    Stages/{Title,Login,WorldSelect,ChannelSelect,CharSelect,CharCreate,Pin,PendingPhase2}Stage.cs
    UI/{Widget,Button,TextField,Label,ListBox,FontProvider}.cs
  MapleClaude.Net/
    Crypto/{AesUserKey,MapleCrypto,ShandaCrypto,IgCipher,PacketCipher}.cs
    Packet/{InPacket,OutPacket,OpCodes,AvatarData}.cs
    Session/{ClientSession,HandshakeReader,PacketRouter,MigrationCoordinator}.cs
    Handlers/{IPacketHandler,LoginHandlers,MigrationHandlers}.cs
  MapleClaude.Wz/
    {WzPackage,WzDirectory,WzImage,WzCanvas,WzReader,WzCrypto,WzUol,WzPath}.cs
  MapleClaude.Render/
    {SpriteAtlas,WzTextureLoader,Camera2D}.cs
  MapleClaude.Domain/
    {Account,ClientKey,WorldInfo,ChannelInfo,AvatarLook,CharacterEntry}.cs
```

## Coding conventions

See `.editorconfig` and `Directory.Build.props`. Highlights:

- File-scoped namespaces.
- Nullable enabled and treated as warnings-as-errors.
- `Span<byte>` and `stackalloc` on hot paths (cipher, packet framing).
- No `Console.WriteLine` in `src/`; logging through Serilog only.
- No `async void` (except event handlers); always `async Task` / `async ValueTask`.
- `[StructLayout(LayoutKind.Sequential, Pack = 1)]` only on documented wire structs.
