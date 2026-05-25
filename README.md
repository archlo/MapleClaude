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
| 6 | Inventory: full `Item`/`ItemSlot` decode, initial inventory from `SetField` `CharacterData`, live `InventoryOperation(28)` updates, double-click to use/equip (`UserStatChangeItemUseRequest` / `UserChangeSlotPositionRequest`), drop money | shipped |
| 7 | Skills + buffs: `SkillBook` driven by `CharacterData` skill records + `ChangeSkillRecordResult`, SP-up + double-click cast (`UserSkillUpRequest` / `UserSkillUseRequest`), optimistic `BuffList` HUD + `TemporaryStatReset` clear | shipped |
| 8 | Social: map chat + party/buddy/guild chat (`GroupMessage`) + whisper in `ChatBar`; party (`PartyResult`/`PartyRequest`, invite→`/accept`) + friends (`FriendResult`/`FriendRequest`) in `UserList`; chat commands (`/w` `/p` `/invite` `/accept` `/leave`) | shipped |
| 9 | Loot: `Message(38)` decode (`IncEXP` / `IncMoney` / `DropPickUp`) → EXP / meso / item popups via `StatusMessenger`; `StatChanged(30)` HUD-total fix; `DropPickUpRequest(246)` pickup | shipped |
| 10 | Polish: settings + keybind persistence (`%APPDATA%/MapleClaude/settings.json`), map BGM + volume, portal travel (`UserTransferFieldRequest`), channel transfer (`UserTransferChannelRequest` + `MigrateCommand`), Cash Shop migrate (`UserMigrateToCashShopRequest`). AOT stays disabled (MonoGame reflection) — ships as single self-contained `.exe` | shipped |
| 11 | StringPool: bundled English language pack (`MapleClaude.Localization`), `Game.StringPool` lookup/format service, job names + loot warnings sourced from the pack | shipped |
| 12 | Display names (String.wz): item/skill/map/mob/npc names + descriptions via a cached `NameService`; wired into inventory, skill book, loot popups, name tags | planned |
| 13 | Map rendering completeness: tile layers 0–7, object layers 1–7 with cross-layer z-order, multi-frame back/obj animation, parallax (`rx`/`ry`, `HMove`/`VMove`); ladders/ropes, reactors, weather | planned |
| 14 | Character/avatar fidelity + login polish: CharSelect renders real avatars (walk1/walk2), consolidated char-create (per-race name screens + forbidden-name check), secondary-password (PIC) register/verify via the v95 soft keyboard, channel grey-out for inactive channels, logout-on-back | shipped |
| 15 | Skills & buffs depth: Skill.wz icons/max-level/active-passive/MP cost, cooldown timers + UI, per-skill cast animation/effect, full `TemporaryStatSet(31)` buff decode | planned |
| 16 | Keybinds & quickslots: bind skills/items to keys (`KeyAction` for skill/item/macro), working quickslot bar, drag-to-bind from SkillBook/Inventory, duplicate-binding warning | planned |
| 17 | In-game presentation: switch to the in-game resolution on map entry (restore on logout), mature the `StatusBar`/HUD to real v95 layout + assets, reflow all panels to the active resolution | planned |
| 18 | Login polish: real PIN stage (`CheckPinCode`/`UpdatePinCode`), wire the no-op buttons (Find ID/PW, Join, Homepage, BtStart/BtVAC), CharSelect delete flow | planned |
| 19 | NPC shops & storage: `OpenShopDlg`/`ShopResult`/`UserShopRequest` buy/sell, the `Shop` panel, and storage/trunk (`TrunkResult`/`TrunkRequest`) | planned |
| 20 | Quests: quest start/complete (`UserQuestRequest`), `QuestRecord` from `CharacterData` + live updates, the `QuestLog` driven by real data, quest NPC/markers | planned |
| 21 | Guild, messenger & combat depth: `GuildResult`/`GuildRequest` + guild tab, the messenger window; outbound `MobMove(227)` for controlled mobs and a server-accurate damage formula | planned |
| 22 | UI-origin tooling + login screens: `tools/wz-ui-dump` (WZ canvas size/origin dumper, `--png` export); world-select + character-select relaid out to authentic map-native v95 coordinates verified against the IDB | shipped |
| 23 | In-game HUD authenticity: `StatusBar` rebuilt 1:1 from `StatusBar2.img` (`CUIStatusBar`), `MiniMap` rewritten to the v95 `(world+center)>>mag` transform, `KeyConfig` rebuilt from `UIWindow2.img/KeyConfig` (`CalcKeyIconPosInfo` port) + `QuickSlotConfig` | in progress |
| 24 | Authentic in-game windows: repoint Item/Equip/Stat/UserInfo/Skill/Quest/options/channel-select/community/messenger/chat from the empty `UIWindow.img` to `UIWindow2.img`, origin-baked layout at IDB coordinates, shared `UIWindowFrame`; reuses existing data-binding (incl. the wired `UserTransferChannelRequest`) | planned |
| 25 | Family system: the `Family` (precept / junior / reputation-buff) and `FamilyTree` (senior-junior tree) windows, authentic from `UIWindow2.img`; server wiring where Kinoko supports it, else an authentic shell | planned |
| 26 | Notes, rankings & collections: `Memo` (note send/inbox), `Ranking`, `MonsterBook` (card collection), `BattleRecord`, `Title`/medal, and the paged `Book` reader | planned |
| 27 | Player trading & shops: the MiniRoom family — `TradingRoom` (player trade), `PersonalShop`, `EntrustedShop` (hired merchant) — authentic UI + the MiniRoom request/result protocol | planned |
| 28 | Maker, macros & item-utility dialogs: `Maker` (item maker), the skill-`Macro` wizard, `Reset` (AP/SP), `Delivery` (cash gift box), `Claim`, `EnchantSkill`, and the cash item dialogs (cube/hammer/scissors/protector/repair) | planned |
| 29 | Map `info` metadata: parse the full `...img/info` block into `MapInfo` (`version`, `cloud`, `town`, `mobRate`, `bgm`, `returnMap`, `mapDesc`, `hideMinimap`, `forcedReturn`, `moveLimit`, `mapMark`, `swim`, `fieldLimit`, `VR*`, `fly`, `noMapCmd`, `onFirstUserEnter`, `onUserEnter`) and wire the client behaviors (hide-minimap, map mark, swim/fly physics, cloud fall-return, field/move limits) | planned |
| 30 | Portal rendering: draw the animated in-game portals (`MapHelper.img/portal/game/{pv,ph,psh}`) at each parsed portal by type (`pt`), reusing the existing animation loader; warp logic (Phase 10) unchanged | planned |

Phases 22–28 form the **authentic-UI rebuild track**: each in-game screen is relaid out 1:1 from the standard `UIWindow2.img` canvases (origins) cross-referenced with the decompiled v95 client, replacing the hand-authored placeholder layouts. Earlier cosmetic panels (Phase 3.5) and their Phase 4–10 server wiring are reused unchanged; these phases only redraw them authentically. Windows for features upstream Kinoko does not implement (alliance, expedition, family, player shops, makers, cash dialogs) ship as fully-laid-out **shells** until their protocol lands.

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
| Double-click an item (inventory panel) | Use a consumable, or equip an equip item |
| Double-click a skill (skill book) | Cast a learned active skill (`UserSkillUseRequest`) |
| Chat commands | `/w <name> <msg>` whisper · `/p <msg>` party chat · `/invite <name>` · `/accept` · `/create` · `/leave` |
| Ctrl+A / Ctrl+C / Ctrl+V / Ctrl+X | Select all / copy / paste / cut in text input fields (login ID, character name) |
| Right Alt | Toggle Korean IME Hangul ↔ English mode when Korean is the active Windows input language |

## Protocol notes

| Opcode | Direction | Where |
| ------ | --------- | ----- |
| `CheckPassword(1)` / `CheckPasswordResult(0)` | C↔S | `src/MapleClaude/Stages/LoginStage.cs`, `src/MapleClaude.Net/Handlers/LoginHandlers.cs` |
| `WorldInfoRequest(4)` / `WorldInformation(10)` | C↔S | `src/MapleClaude/Stages/WorldSelectStage.cs` |
| `SelectWorld(5)` / `SelectWorldResult(11)` | C↔S | `src/MapleClaude/Stages/WorldSelectStage.cs` |
| `CheckDuplicatedID(21)` / `CheckDuplicatedIDResult(13)` | C↔S | `src/MapleClaude/Stages/CharCreationStage.cs` |
| `CreateNewCharacter(22)` / `CreateNewCharacterResult(14)` | C↔S | `src/MapleClaude/Stages/CharCreationStage.cs` |
| `SelectCharacter(19)` / `SelectCharacterResult(12)` | C↔S | `src/MapleClaude/Stages/CharSelectStage.cs` |
| `EnableSPWRequest(28)` / `CheckSPWRequest(29)` / `CheckSPWResult(27)` (secondary password / PIC) | C↔S | `src/MapleClaude/Stages/CharSelectStage.cs` + `src/MapleClaude.Net/Handlers/LoginHandlers.cs` |
| `LogoutWorld(12)` | C→S | `src/MapleClaude/Stages/CharSelectStage.cs` + `src/MapleClaude.Net/Senders/LoginSender.cs` |
| `MigrateIn(20)` | C→S | `src/MapleClaude.Net/Session/MigrationCoordinator.cs` |
| `SetField(141)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` |
| `UserMove(44)` | C→S | `src/MapleClaude/Stages/FieldStage.cs` + `src/MapleClaude.Net/Packet/MovePathEncoder.cs` |
| `UserMeleeAttack(47)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude.Net/Packet/MeleeAttackEncoder.cs` |
| `MobEnterField(284)` / `MobLeaveField(285)` / `MobMove(287)` / `MobChangeController(286)` / `MobDamaged(294)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude/Character/MobLook.cs` |
| `UserSelectNpc(63)` / `UserScriptMessageAnswer(65)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude/Net/Senders/GameSender.cs` |
| `NpcEnterField(311)` / `NpcLeaveField(312)` / `ScriptMessage(363)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude/UI/Game/NpcTalk.cs` |
| `InventoryOperation(28)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` + `src/MapleClaude.Net/Packet/ItemDecoder.cs` → `src/MapleClaude/UI/Game/ItemInventory.cs` |
| `UserChangeSlotPositionRequest(77)` / `UserStatChangeItemUseRequest(78)` / `UserDropMoneyRequest(106)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude/Net/Senders/GameSender.cs` |
| `UserSkillUpRequest(102)` / `UserSkillUseRequest(103)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude/Net/Senders/GameSender.cs` |
| `ChangeSkillRecordResult(35)` / `TemporaryStatSet(31)` / `TemporaryStatReset(32)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude/UI/Game/{SkillBook,BuffList}.cs` |
| `UserChat(54)` / `GroupMessage(140)` / `Whisper(141)` / `PartyRequest(145)` / `FriendRequest(153)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude.Net/Senders/GameSender.cs` |
| `GroupMessage(150)` / `Whisper(151)` / `PartyResult(62)` / `FriendResult(65)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude/UI/Game/{ChatBar,UserList}.cs` |
| `DropPickUpRequest(246)` | C→S | `src/MapleClaude/Stages/GameStage.cs` + `src/MapleClaude.Net/Senders/GameSender.cs` |
| `Message(38)` (`IncEXP` / `IncMoney` / `DropPickUp`) | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude/UI/Game/StatusMessenger.cs` |
| `UserTransferFieldRequest(41)` / `UserTransferChannelRequest(42)` / `UserMigrateToCashShopRequest(43)` | C→S | `src/MapleClaude/Stages/{GameStage,CashShopStage}.cs` + `src/MapleClaude.Net/Senders/GameSender.cs` |
| `MigrateCommand(16)` | S→C | `src/MapleClaude.Net/Handlers/FieldHandlers.cs` → `src/MapleClaude.Net/Session/MigrationCoordinator.cs` |
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

### 6b. Hot-reload dev loop

```powershell
.\watch.ps1
```

Runs the client under `dotnet watch run`. Save a C# edit and **method-body changes** (Draw / Update / layout logic) apply to the running game via .NET Hot Reload; **structural changes** (new fields, signatures, types) auto-rebuild and relaunch. No manual close → build → deploy → reopen. The script reads the WZ folder from `MAPLECLAUDE_WZ_DIR` or `.deploy.local` and sets `MAPLECLAUDE_DEBUG=1` so the live layout overlay (§9) is available for dragging positions with zero rebuild.

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
