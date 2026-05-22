# MapleClaude

> **Disclaimer.** MapleClaude is a made-from-scratch game client and a free, open-source project developed for educational purposes — specifically as a study of AI-assisted software development. **No human-written code is included.** Every source file in this repository is authored by an AI agent. This project is non-commercial, has no affiliation with Nexon or any MapleStory rights holder, and ships no proprietary game assets. Users must supply their own legally obtained client assets to run it.

A modernized, 64-bit MapleStory v95 game client built entirely with the help of Claude.

MapleClaude is a brand-new client written in C# 13 / .NET 10 with MonoGame for rendering. It connects to the [Kinoko](https://github.com/iw2d/kinoko) Java server (v95, locale 8, patch "1") and replicates the original v95 client flow from launch through in-game play. The codebase is greenfield — not a fork of any existing client — and is structured for long-term, multi-phase extension.

## Status

| Phase | Scope | Status |
| ----- | ----- | ------ |
| 1 | Launch → title → login → world/char select → char create → channel migrate handoff | shipped |
| 2 | Field load, player avatar render, no input | shipped |
| 3 | Movement, camera follow, `UserMove(44)` outgoing | shipped |
| 3.5 | Cosmetic in-game UI (StatusBar, Inventory, SkillBook, MiniMap, KeyConfig, etc.) — UI only, no server data yet | shipped |
| 4 | Mobs: `MobEnterField` / `MobMove` / `MobChangeController` decode, mob sprites + animation + HP bar, `UserMeleeAttack(47)` outgoing, damage numbers, kill + respawn | shipped |
| 5 | NPCs: `NpcEnterField` render, click-to-talk + Interact key (`UserSelectNpc`), `ScriptMessage` decode (say/menu/yes-no/text/number) + `UserScriptMessageAnswer` | shipped |
| 6 | Inventory wire-up: drive the StatusBar's `ItemInventory` / `EquipInventory` panels from real `InventoryOperation` packets, equip swap with stat updates | planned |
| 7 | Skills + buffs: drive the `SkillBook` panel from `ChangeSkillRecordResult`, send `UserSkillUseRequest`, decode `TemporaryStatSet`/`Reset` for the `BuffList` HUD | planned |
| 8 | Social: drive the existing `ChatBar` / `UserList` / `Messenger` UIs from real `UserChat` / `FriendResult` / `PartyResult` / `GuildResult` packets | planned |
| 9 | Drops, pickup, EXP / meso popups (already-built `StatusMessenger` UI hooks up to `DropEnterField` and stat-change packets) | planned |
| 10 | Polish: full keybind editor, settings persistence, channel transfer (`UserTransferChannelRequest`), Cash Shop migrate (existing `CashShopStage` shell hooks up to real packets), AOT publish | planned |

The cosmetic UI panels added in Phase 3.5 (StatusBar gauges, MiniMap, ItemInventory grid, SkillBook tabs, etc.) currently render with placeholder/demo data; Phases 4–10 progressively wire each one to live server packets without redrawing them.

See `docs/roadmap.md` for the detailed roadmap and `CLAUDE.md` for the contributor / Claude-Code guide.

## Keyboard

| Key | Action |
| --- | ------ |
| Tab | Toggle ID ↔ password field on the login screen |
| Enter | Submit login form / confirm character select |
| Backspace | Back one screen (e.g. ChannelGrid → WorldList → Login) |
| ESC | Quit |
| Arrow keys | Walk / turn in-game (configurable via the in-game `KeyConfig` panel) |
| Alt / Space / W | Jump in-game (configurable via `KeyConfig`) |
| Attack key (configurable via `KeyConfig`) | Melee swing — hits mobs in range, sends `UserMeleeAttack(47)` |
| Interact key (configurable via `KeyConfig`) / click an NPC | Talk to the nearest/clicked NPC (`UserSelectNpc`) |
| Ctrl+A / Ctrl+C / Ctrl+V / Ctrl+X | Select all / copy / paste / cut in text input fields (login ID, character name) |
| Right Alt | Toggle Korean IME Hangul ↔ English mode when Korean is the active Windows input language |

## Protocol notes

| Opcode | Direction | Where |
| ------ | --------- | ----- |
| `CheckPassword(1)` / `CheckPasswordResult(0)` | C↔S | `src/MapleClaude/Stages/LoginStage.cs`, `src/MapleClaude.Net/Handlers/LoginHandlers.cs` |
| `WorldInfoRequest(4)` / `WorldInformation(10)` | C↔S | `src/MapleClaude/Stages/WorldSelectStage.cs` |
| `SelectWorld(5)` / `SelectWorldResult(11)` | C↔S | `src/MapleClaude/Stages/WorldSelectStage.cs` |
| `CheckDuplicatedID(21)` / `CheckDuplicatedIDResult(13)` | C↔S | `src/MapleClaude/Stages/CharCreateStage.cs` |
| `CreateNewCharacter(22)` / `CreateNewCharacterResult(14)` | C↔S | `src/MapleClaude/Stages/CharCreateStage.cs` |
| `SelectCharacter(19)` / `SelectCharacterResult(12)` | C↔S | `src/MapleClaude/Stages/CharSelectStage.cs` |
| `MigrateIn(20)` | C→S | `src/MapleClaude.Net/Session/MigrationCoordinator.cs` |
| `SetField(141)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` |
| `UserMove(44)` | C→S | `src/MapleClaude/Stages/FieldStage.cs` + `src/MapleClaude.Net/Packet/MovePathEncoder.cs` |
| `UserMeleeAttack(47)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude.Net/Packet/MeleeAttackEncoder.cs` |
| `MobEnterField(284)` / `MobLeaveField(285)` / `MobMove(287)` / `MobChangeController(286)` / `MobDamaged(294)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude/Character/MobLook.cs` |
| `UserSelectNpc(63)` / `UserScriptMessageAnswer(65)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude/Net/Senders/GameSender.cs` |
| `NpcEnterField(311)` / `NpcLeaveField(312)` / `ScriptMessage(363)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude/UI/Game/NpcTalk.cs` |
| `AliveReq(17)` / `AliveAck(25)` | S↔C | auto-replied in both `LoginHandlers` and `FieldHandlers` |

All Maple wire strings are length-prefixed LE-short + US-ASCII (not UTF-16LE). All multi-byte primitives are little-endian.

## Setup

### 1. Prerequisites

- Windows 10/11 (64-bit).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` pins the exact version).
- Visual Studio 2026 (any edition) **or** just the `dotnet` CLI.
- Java 21 + Maven (only for running the Kinoko server).
- Docker (optional; the server's recommended run mode).

### 2. Get the v95 WZ assets

MapleClaude reads standard GMS v95 `.wz` files. Install the original v95 client to extract them:

- Download: <https://ia600809.us.archive.org/19/items/GMSSetup93-133/GMS0095/GMSSetupv95.exe>
- Install to any directory (default is fine).
- After install, the WZ files live next to the client `.exe`: `UI.wz`, `Map.wz`, `Sound.wz`, `Character.wz`, `Item.wz`, `Skill.wz`, `Mob.wz`, `Npc.wz`, `Quest.wz`, etc.
- Note the install directory; you'll point `MAPLECLAUDE_WZ_DIR` at it in step 5.

### 3. Run the Kinoko server

The Kinoko v95 server is the protocol counterpart MapleClaude talks to.

- Repo: <https://github.com/iw2d/kinoko>
- Follow Kinoko's own README to clone, configure (`.env` for DB + WZ directory), and start the server. Easiest path is Docker Compose:
  ```powershell
  git clone https://github.com/iw2d/kinoko.git
  cd kinoko
  copy .env.example .env   # then edit DB_TYPE and WZ_DIRECTORY
  docker compose up -d postgres server
  ```
- Verify the login server is listening on `127.0.0.1:8484` (channel server defaults to `8585`).

### 4. (Optional) Verify your server with the reference v95 client

If you want to confirm Kinoko is reachable before running MapleClaude itself, an existing v95 client preconfigured for localhost is available here:

- <https://mega.nz/file/dWIgyR4I#6cDN_ycLLiFtad07Eby3UfjdY3TqGI65g6X-xEqlmds>

This is purely a smoke-test aid; MapleClaude does not require or depend on it.

### 5. Configure MapleClaude

Set the environment variables MapleClaude reads at runtime:

```powershell
$env:MAPLECLAUDE_WZ_DIR    = "C:\path\to\v95\wz_files"     # directory containing UI.wz, Map.wz, ...
$env:MAPLECLAUDE_LOGIN_HOST = "127.0.0.1"                  # default
$env:MAPLECLAUDE_LOGIN_PORT = "8484"                       # default
```

You can also record these locally (untracked) in `CLAUDE.local.md` for later sessions.

### 6. Build and run

```powershell
git clone https://github.com/MujyKun/MapleClaude.git
cd MapleClaude
dotnet restore
dotnet build -c Debug
dotnet test
dotnet run --project src/MapleClaude
```

Open `MapleClaude.slnx` in Visual Studio 2026 for the IDE experience (Debug → Start with F5).

If the title screen loads, your WZ assets are wired correctly. If you can log in and reach the world / character select, the cipher pipeline is working. The Phase 1 finish line is logging `MigrateIn ACK observed — Phase 2 boundary reached` after picking a character — that means the channel handoff worked.

### 7. Single `.exe` is the default build output

**Every `dotnet build` automatically produces a single self-contained `MapleClaude.exe`** at:

```
artifacts\<Configuration>\MapleClaude.exe
```

The .NET runtime, MonoGame, and all native deps are bundled — no separate install needed on the target machine. Drop the `.exe` next to your `UI.wz`, `Map.wz`, etc. and run it.

Approximate size: **~73 MB** (uncompressed). Compression and `IncludeAllContentForSelfExtract` are intentionally disabled — both crash the .NET 10 single-file launcher with `STATUS_STACK_BUFFER_OVERRUN (0xc0000409)` when combined with MonoGame's native deps. Reliability matters more than 36 MB.

> **First-launch delay:** Windows Defender scans the freshly built binary on first run. Expect a 30–90 second wait the first time you double-click `MapleClaude.exe`. Subsequent launches are sub-second. To skip the scan, add the deploy folder to Windows Security → Virus & threat protection → Exclusions.

If you want maximum iteration speed and don't care about the single-file output for a given build:

```powershell
dotnet build -c Debug -p:NoAutoPublish=true   # ~3 s; skips publish
```

### 8. Auto-deploy on every build

Make every `dotnet build` (or **Rebuild Solution** in VS2026) drop `MapleClaude.exe` straight into your MapleStory folder. Configure the deploy directory once with either:

**Option A — environment variable** (visible to any tool, persists across projects; **requires VS restart** to be picked up):

```powershell
[Environment]::SetEnvironmentVariable('MAPLECLAUDE_DEPLOY_DIR', 'X:\path\to\maplestory', 'User')
```

**Option B — `.deploy.local` file at repo root** (no shell or VS restart needed, simplest setup; gitignored):

```powershell
"X:\path\to\maplestory" | Set-Content .deploy.local -Encoding ascii
```

After either, every build will print:

```
==> Deploying MapleClaude.exe -> X:\path\to\maplestory
==> Deploy OK
```

To disable for one build: `dotnet build -p:NoAutoDeploy=true`. To disable auto-publish too (fastest dev iteration): `dotnet build -p:NoAutoPublish=true`.

`publish.ps1` is still around for explicit Release + deploy from the CLI, but it's no longer required since `dotnet build` does the same work automatically.

### 9. Optional: live debug window (`MAPLECLAUDE_DEBUG`)

Set the `MAPLECLAUDE_DEBUG` environment variable to any non-empty value and `MapleClaude.exe` will open a second window — a 700×700 WinForms overlay — alongside the game. It has two panes:

- **Top:** a grid listing every UI position the active stage has registered as tunable (e.g. `LoginCamera / Offset`, `LoginSignboard / Center`). The X and Y cells are directly editable — press Enter and the value is applied to the running game on the next frame. No rebuild, no restart. Great for nailing down UI placements against the real v95 reference without an edit/build/relaunch loop.
- **Bottom:** a live stream of the application's Serilog output (info, warnings, errors).

**One-shot launch (just this shell):**

```powershell
$env:MAPLECLAUDE_DEBUG = '1'
& 'C:\path\to\MapleClaude.exe'
```

**Persist for your user account** (every future launch — including Explorer double-clicks — opens the debug window):

```powershell
[Environment]::SetEnvironmentVariable('MAPLECLAUDE_DEBUG', '1', 'User')
```

**Disable later:**

```powershell
[Environment]::SetEnvironmentVariable('MAPLECLAUDE_DEBUG', $null, 'User')
```

The debug window is purely a dev aid; when the variable is unset, no extra window opens and there is zero runtime cost.

## Layout

```
src/
  MapleClaude/         main game executable (MonoGame entry, stages, UI)
  MapleClaude.Net/     network, crypto, packet framing, handlers
  MapleClaude.Wz/      GMS v95 WZ reader (PKG1)
  MapleClaude.Render/  MonoGame integration (sprite atlas, texture loader)
  MapleClaude.Domain/  POD state types (no dependencies)
tests/
  MapleClaude.Net.Tests/
  MapleClaude.Wz.Tests/
tools/
  gen-opcodes/         generates OpCodes.cs from Kinoko In/OutHeader.java
  pcap-replay/         offline packet replay
docs/                  protocol, WZ format, architecture, roadmap
.claude/
  skills/              auto-trigger skills
  agents/              named agents
```

## License

See `LICENSE`.
