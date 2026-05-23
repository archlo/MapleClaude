# MapleClaude — Phase Roadmap

This is the multi-phase build plan for MapleClaude. Each phase ships its own
`phase-N/*` branch family, an entry-criteria definition, an exit-criteria
definition, and a single PR to `master` at completion.

## Phase 1 — Pre-game flow (shipped)

**Scope.** Launch → title → login → world/server select → character list →
character create → PIN entry (dormant on Kinoko) → channel migrate handoff.

**Exit criteria.** With a running Kinoko server, the client connects, logs in,
shows the world list, lets the user create/select a character, reaches the
channel migrate, and logs:

```
MigrateIn ACK observed — Phase 2 boundary reached
```

## Phase 2 — Field load & avatar render (shipped)

**Scope.** Decode `SetField(141)` (the first in-game packet after migrate). Load
the player's starting map from `Map.wz/Map/Map<prefix>/<mapId>.img` (footholds,
portals, multi-layer backgrounds). Render the player's `AvatarLook`
(body + head + face + hair + equips from `Character.wz`) standing on the map.

**Exit criteria.** First-spawn map renders with the player's avatar visible at
the correct starting coordinates and standing on the correct foothold.

## Phase 3 — Movement & camera (shipped)

**Scope.** Arrow keys → walk; Alt → jump. v95 `UserMove(44)` outgoing
encoding (the move-path with NORMAL / JUMP / TELEPORT / STAT_CHANGE /
START_FALL_DOWN / FLYING_BLOCK sub-action categories). Gravity, foothold
ground-snap. `Camera2D` follow with deadzone + VR-bounds clamp.

**Exit criteria.** Player can walk, jump, and fall onto footholds; the server
accepts the outgoing `UserMove` packets without disconnecting.

## Phase 3.5 — In-game cosmetic UI (shipped)

**Scope.** All the in-game panels needed for a real client are laid out
visually with placeholder data — no server wiring yet. `StatusBar` (HP/MP/EXP
gauges + quickslot bar + submenu buttons), `ChatBar`, `MiniMap`,
`ItemInventory` (4-tab grid), `EquipInventory`, `SkillBook`, `StatsInfo`
(STR/DEX/INT/LUK + AP), `QuestLog`, `KeyConfig` (re-bindable keys),
`OptionMenu`, `CharInfo`, `Shop`, `NpcTalk`, `Notice`, `WorldMap`,
`UserList` (friends/party/guild tabs), `ChannelSelect`, `StatusMessenger`
(floating loot/exp/buff messages), `CashShopStage` (1024×768 overlay with
9 category tabs + paginated 7×2 item grid).

**Exit criteria.** Every panel renders with sensible placeholder data,
opens/closes via its StatusBar button or hotkey, and the user can interact
with the controls. Server data wiring is deferred to phases 4–10.

## Phase 4 — Mobs & combat (shipped)

**Scope.** `MobEnterField`/`MobLeaveField`/`MobMove`/`MobChangeController`/`MobDamaged`
decode → `MobLook` sprite render (Mob.wz animation states, HP bar, hit flash,
death). `DamageNumber` floating combat text, `DropSprite`, `OtherCharLook`.
`UserMeleeAttack(47)` outgoing via `MeleeAttackEncoder` (front-of-player hit
box, ≤6 targets, client-chosen damage that the v95 server trusts), a transient
`Stance.Swing` pose, and an attack cooldown.

**Exit criteria.** Player can swing at a low-level mob, the server echoes
`MobDamaged` → a red damage number appears + the mob's HP bar drops, enough
hits kill it, and the server respawns a fresh one. No disconnect (correct
`fieldKey`).

**Deferred to later phases:** server-accurate damage formula (needs weapon +
stat data — Phase 6/7); outbound `MobMove(227)` for controlled mobs (needs a
mob-side physics sim + HackedCode synthesis); `UserShootAttack(48)` /
`UserMagicAttack(49)` (need the skill system — Phase 7).

## Phase 5 — NPCs & dialog (shipped)

**Scope.** `NpcEnterField`/`NpcLeaveField` render (`NpcLook`), click-to-talk +
the Interact key (`UserSelectNpc(63)` with player position), a wire-correct
`ScriptMessage(363)` decoder matching upstream `ScriptMessage.encode`
(SAY with prev/next, ASKYESNO, ASKMENU with `#L#`/`#l` item parsing, ASKTEXT,
ASKNUMBER), the `NpcTalk` dialog widget, and `UserScriptMessageAnswer(65)`
replies that echo the active dialog's `ScriptMessageType` (the canonical enum
in `src/MapleClaude.Net/Packet/ScriptMessageType.cs`).

**Exit criteria.** Click (or Interact-key) an NPC → its dialog renders the
real text → advance through a multi-line say, pick a menu item, answer a
yes/no, text, or number prompt → the server's script engine advances and
effects fire. No disconnect.

**Deferred:** `SAYIMAGE`, `ASKAVATAR`, `ASKSLIDEMENU` rich dialogs (rare);
`ASKQUIZ`/`ASKSPEEDQUIZ` (unsupported by the v95 server); NPC shops
(`OpenShopDlg`/`ShopResult`/`UserShopRequest`) → Phase 6.

## Phase 6 — Inventory & items (shipped)

**Scope.** Full `GW_ItemSlot*` decode (`ItemDecoder` — equip stat block,
bundle quantity, pet) mirroring upstream `Item.encode`. Initial inventory
parsed from `SetField`'s `CharacterData` (equipped + 5 tabs) and loaded into
the `ItemInventory` / `EquipInventory` panels. Live `InventoryOperation(28)`
updates (NewItem with full item / ItemNumber / Position / DelItem) applied to
the slot-indexed panels. Double-click to use a consumable
(`UserStatChangeItemUseRequest(78)`) or equip an equip item by computed body
part (`UserChangeSlotPositionRequest(77)`); `UserDropMoneyRequest(106)`.

**Exit criteria.** Pick up an item (live `InventoryOperation`); equip it
(double-click → the server recalculates and echoes `StatChanged`, the stat
panel updates); drop money. No disconnect.

**Deferred:** in-grid item drag/drop reordering + drag-to-drop (the move op is
applied when the server sends it; client-initiated drag is a later UI pass);
String.wz item-name lookup (panels show the wire title or a formatted id);
NPC shops (`OpenShopDlg`/`UserShopRequest`).

## Phase 7 — Skills, jobs, buffs (shipped)

**Scope.** `SkillRecord` decode from `SetField`'s `CharacterData` SKILLRECORD
section + live `ChangeSkillRecordResult(35)` drive the `SkillBook` panel.
SP-up (`UserSkillUpRequest(102)`) and double-click cast of a learned active
skill (`UserSkillUseRequest(103)`, self-buff body). The `BuffList` HUD shows a
buff optimistically on cast and expires it on a local timer; `TemporaryStatReset(32)`
clears it. Skill/buff opcodes added to `OpCodes.cs`.

**Exit criteria.** Skill book shows the character's learned skills; double-click
an active skill → it casts (no disconnect); for a buff skill the icon appears
and expires; SP-up adds a level via the server.

**Deferred:** full `TemporaryStatSet(31)` per-stat decode (needs the entire
`CharacterTemporaryStat` enum + LOCAL_ENCODE_ORDER — incoming parse errors are
isolated so this is safe to defer; the buff HUD is optimistic until then);
`UserEffectLocal/Remote` skill animations; skill-to-function-key binding;
String.wz skill names; cooldown UI; active/passive split (needs Skill.wz).

## Phase 8 — Social (shipped)

**Scope.** Map chat (`UserChat` — fixed the missing `update_time` field that
desynced sends), party/buddy/guild chat (`GroupMessage` 140/150) + whisper
(`Whisper` 141/151) in the `ChatBar`. Party (`PartyRequest(145)` create/invite/
leave/join + `PartyResult(62)` PARTYDATA member-list decode) and friends
(`FriendRequest(153)` load + `FriendResult(65)` decode) populate the `UserList`.
Chat commands `/w`, `/p`, `/invite`, `/accept`, `/create`, `/leave`. A party
invite arrives as a ChatBar prompt; `/accept` sends `JoinParty`.

**Exit criteria.** Two clients can party up (invite → `/accept`) and chat in
the party channel; both appear in the party tab. (Minimap party dots deferred.)

**Deferred:** guild (`GuildResult`/`GuildRequest`), messenger, party HP on the
party HUD, friend add/delete UI buttons (the senders exist), minimap party
positions, a dedicated yes/no invite popup (uses the `/accept` chat command).

## Phase 9 — Loot (shipped)

**Scope.** Drop spawn (`DropEnterField`) + render (`DropSprite`) and pickup
(`DropLeaveField`) already existed; this phase added the loot feedback loop:
`Message(38)` decode (`IncEXP` / `IncMoney` / `DropPickUp` item/meso/warning)
driving EXP + meso popups through the `StatusMessenger`. Item popups arrive via
`InventoryOperation`. The `DropPickUpRequest(246)` encode moved into
`GameSender.PickUpDrop` for testability.

Also fixed two `StatChanged(30)` decode bugs that mis-aligned the HUD stat
block: the mask is a 4-byte int (was read as a long) and `MONEY` is bit
`0x40000` (the code used `0x200000`, which is `TEMPEXP`).

**Exit criteria.** Killing mobs / picking up loot shows EXP, meso, and item
popups; the EXP bar and meso total update from the corrected `StatChanged`.
(Runtime gain/pickup is server-driven, verified against a live channel server.)

## Phase 10 — Polish (shipped)

**Scope.**
- **Settings persistence** — `SettingsStore` serialises keybinds + BGM/SFX
  volume to `%APPDATA%/MapleClaude/settings.json` (System.Text.Json source-gen);
  loaded on enter, saved when the user commits a rebind or option change. A
  server-sent keymap (`FuncKeyMappedInit`) still overrides the local layout.
- **Audio** — map BGM plays on `SetField` (resolved from `info/bgm` against
  Sound.wz) via the existing `WzAudioPlayer`; BGM/SFX volume are now settable and
  wired to the option menu.
- **Portals** — walk onto a warp portal + press Up sends
  `UserTransferFieldRequest(41)`; `SetField` now clears stale field entities so a
  transfer doesn't leak the previous map's mobs/npcs/drops.
- **Channel transfer** — `UserTransferChannelRequest(42)` (fixed to send the
  trailing `update_time`); the server's `MigrateCommand(16)` reply reconnects via
  the existing `MigrationCoordinator`, reusing the cached character id.
- **Cash-shop migrate** — `UserMigrateToCashShopRequest(43)` on open and an
  empty `UserTransferFieldRequest` on exit (cash shop is same-connection; full
  cash-shop content decode is a follow-up).

**AOT — known limitation (not done).** `PublishAot` and `PublishTrimmed` stay
disabled: MonoGame relies on reflection (content pipeline, type lookup) that
breaks under trimming/AOT. The shipping format remains a **single, self-contained
`MapleClaude.exe`** (~73 MB, runtime bundled) — it already runs without a
separate .NET install, satisfying the practical goal. Revisit AOT only if
MonoGame gains verified trim/AOT support upstream.

**Exit criteria.** Settings + keybinds persist across launches; map BGM plays
and respects volume; portals, channel transfer, and cash-shop migrate send
byte-correct requests (unit-tested) and reuse the migration template.
(Map-change / channel-reconnect / cash-shop-content runtime behaviour is
server-dependent, verified against a live server.)

## Phase 11 — StringPool language pack (shipped)

**Scope.** A new `MapleClaude.Localization` library bundles the v95 client's
internal string table (6883 entries) as an embedded English language pack
(`strings.en.csv`, extensible to other languages). `StringPool` loads/parses it
(`""` and `\r\n` escapes) and exposes `Get` / `GetOr` / `TryGet` / `Format`
(printf `%d`/`%s` substitution), surfaced as `Game.StringPool`. `StringId` names
the ids used today. `GameStage.JobName` and `LootWarningText` now read from the
pack (job names fall back to the built-in table for unmapped jobs).

**Exit criteria.** Job names and loot warnings render from the pack; the pack
ships embedded in the single-file exe; parser/format/mapping are unit-tested.

---

# Maturation roadmap (Phases 12–21)

These phases mature the client toward a complete play experience. Each is an
independent contributor-ready unit — pick one, branch `phase-N/<slug>`, ship a PR
to `master` with the standard sections. Order is a suggestion, not a hard
dependency (12 unblocks the most downstream polish, so it's a good first pick).

## Phase 12 — Display names (String.wz)

**Scope.** Read `String.wz` (`Eqp.img`/`Ins.img`/`Etc.img`/`Cash.img`,
`Skill.img`, `Map.img`, `Mob.img`, `Npc.img`) for real names + descriptions
through a cached `NameService` keyed by id. Wire into `ItemInventory`/
`EquipInventory`, `SkillBook`, loot popups, `CharSelect` map name, and NPC/mob
name tags — replacing the `"Item {id}"` / `"Skill {id}"` placeholders.

**Exit criteria.** Items, skills, maps, and mobs show their real names from
String.wz. **Key files:** new `src/MapleClaude/.../NameService.cs`,
`GameStage.ItemDisplayName`/`ApplySkills`, the inventory/skill panels.

## Phase 13 — Map rendering completeness

**Scope.** Tile layers 0–7 (per-layer `tile` + `Tile.wz` tilesets), object layers
1–7 with correct cross-layer z-order, multi-frame animation for backgrounds +
objects (`ani`/`a0`/`a1`/delays), and parallax (`rx`/`ry` factors and
`HMove`/`VMove` scroll types). Follow-ups: ladders/ropes visuals, reactors,
weather, seats.

**Exit criteria.** A real town/field map renders with tiles, layered objects,
animation, and parallax matching the v95 client. **Key files:**
`src/MapleClaude/Map/{FieldScene,MapScene,TileInfo,BackInfo}.cs`.

## Phase 14 — Character / avatar fidelity

**Scope.** Replace the fixed equip draw order in `CharacterRenderer` with the real
Character.wz **zmap/vslot** z-ordering; a full animation state machine
(walk/jump/alert/attack/sit/prone/ladder/rope) for the player **and** other
players; render hair equips, weapon stickers, pets, and the cash overlay
(`UnseenEquip`); show a real avatar in **CharSelect**; **consolidate** the
duplicate `CharCreationStage` / `CharCreateStage` to the renderer-backed one.

**Exit criteria.** Avatars layer correctly and animate per stance everywhere.
**Key files:** `src/MapleClaude/Character/{CharacterRenderer,CharLook,OtherCharLook}.cs`,
`Stages/{CharSelectStage,CharCreateStage}.cs`.

## Phase 15 — Skills & buffs depth

**Scope.** `Skill.wz`: icons, max level, active/passive split, MP cost, cooldown
timers + UI, and per-skill cast animation/effect (`UserEffectLocal`/`Remote`).
Full `TemporaryStatSet(31)` per-stat decode (the `CharacterTemporaryStat` enum +
LOCAL_ENCODE_ORDER) driving a real `BuffList` instead of the optimistic
placeholder.

**Exit criteria.** The skill book shows real icons/levels; casting plays the
skill effect and respects cooldown/MP; buffs show real durations.
**Key files:** `UI/Game/{SkillBook,BuffList}.cs`, `Net/Handlers/FieldHandlers.cs`.

## Phase 16 — Keybinds & quickslots

**Scope.** `KeyAction` entries for skills/items/macros; bind a skill or item to a
key (extend `ApplyServerKeymap` for `type` 1/2/3/8); a working quickslot bar wired
to the bindings; drag-to-bind from `SkillBook`/`ItemInventory`; a
duplicate-binding warning.

**Exit criteria.** A skill/item dragged to a key fires on press; the quickslot bar
reflects bindings; layout persists (Phase 10 store). **Key files:**
`UI/Game/{KeyConfig,QuickSlots,SkillBook,ItemInventory}.cs`.

## Phase 17 — In-game presentation (resolution + HUD) (shipped)

**Scope.** `GameStage.OnEnter` enlarges the window from the 800×600 login canvas
to the in-game resolution (1024×768) and restores the login size in `OnExit`
(the same `Game.ResizeWindow` pattern `CashShopStage` uses; the previous size is
captured at enter time). `GameCamera`'s view dimensions are synced to the live
backbuffer each frame so the world→screen transform stays correct across the
resize and the cash-shop round-trip. A new `GamePanel.Relayout(viewW, viewH)`
hook re-anchors the HUD: `StatusBar` is now bottom-anchored and horizontally
centred with a full-width EXP gauge (every anchor reduces to the original 800×600
value at login size, so there is no regression there), and `ChatBar`,
`BuffList`, and `StatusMessenger` re-anchor to their screen edges. The
`QuitConfirmOverlay` dim layer now covers the whole window and recentres.

**Exit criteria.** Entering a map enlarges the view and the bottom HUD renders
correctly at 1024×768; returning to login restores 800×600. **Key files:**
`Stages/GameStage.cs`, `UI/Game/{GamePanel,StatusBar,ChatBar,BuffList,StatusMessenger}.cs`,
`UI/QuitConfirmOverlay.cs`.

**Deferred:** toggle windows (inventory, skills, stats, quest log, key config,
options, world map, user list) keep their authored positions — they remain fully
on-screen at the larger resolution; per-panel re-centring is a follow-up. The
v95 `StatusBar3.img` artwork is centred rather than stretched/tiled to the full
width (a later artwork pass).

## Phase 18 — Login polish (shipped)

**Scope.** The CharSelect **delete flow** is now live: `BtDelete` opens a new
`DeleteConfirmOverlay` that prompts for the account's secondary password (Kinoko
validates it server-side and returns `IncorrectSPW` otherwise), then sends
`LoginSender.DeleteCharacter(charId, spw)` (`string secondaryPassword, int
characterId`). The existing `DeleteCharacterResult(15)` handler drops the
character from the session; `CharSelectStage` now also refreshes its display list
and maps the failure codes (IncorrectSPW / DBFail / guild-master / engaged /
family) to readable messages. `LoginSender` moved from the exe into
`MapleClaude.Net` (it has no exe dependencies) so its wire shapes are unit
tested. The previously silent login buttons (Find ID/PW, Join, Homepage) now show
a notice explaining they aren't applicable to a Kinoko server (accounts
auto-register on first login).

**Exit criteria.** A character can be deleted end-to-end (confirm → packet →
result → list refresh); the info buttons give feedback. **Key files:**
`Stages/{LoginStage,CharSelectStage}.cs`, `UI/DeleteConfirmOverlay.cs`,
`MapleClaude.Net/Senders/LoginSender.cs`.

**Deferred:** the **PIN stage** stays dormant — upstream Kinoko always sends
`bSkipPinCode = true` in `CheckPasswordResult` and implements no
`CheckPinCode`/`UpdatePinCode` handler, so a real PIN flow can't be exercised
against it. `WorldSelectStage`'s `BtStart`/`BtVAC`/`BtViewChoice` are
v95-client-only view toggles with no server packet and remain no-ops.

## Phase 19 — NPC shops & storage

**Scope.** NPC shops (`OpenShopDlg`/`ShopResult` decode, `UserShopRequest`
buy/sell/recharge) driving the `Shop` panel; player storage / trunk
(`TrunkResult`/`TrunkRequest`).

**Exit criteria.** Buy/sell at an NPC shop updates inventory + meso; storage
deposits/withdraws. **Key files:** `UI/Game/Shop.cs`, `Net/Handlers/FieldHandlers.cs`,
`Net/Senders/GameSender.cs`.

## Phase 20 — Quests

**Scope.** `QuestRecord` from `CharacterData` + live `Message` quest-record
updates; quest start/complete (`UserQuestRequest`); the `QuestLog` driven by real
data; quest availability markers on NPCs.

**Exit criteria.** Accept a quest, see it in the log, complete it, and receive the
reward. **Key files:** `UI/Game/QuestLog.cs`, `Net/Handlers/FieldHandlers.cs`.

## Phase 21 — Guild, messenger & combat depth

**Scope.** Guild (`GuildResult`/`GuildRequest`) + the guild tab in `UserList`; the
messenger window. Combat depth: outbound `MobMove(227)` for client-controlled
mobs (a mob-side physics sim) and a server-accurate damage formula (now that
weapon + stat data exist).

**Exit criteria.** A guild loads + displays; controlled mobs move for other
players; melee damage matches the server. **Key files:**
`UI/Game/UserList.cs`, `Character/MobLook.cs`, `Net/{Handlers,Senders}/*`.

## Conventions across phases

- **Branch per phase**, commit per logical unit (file compiles, test passes,
  opcode decodes, stage renders).
- **Never commit on master**; PR per phase at exit.
- **Ask the user before each commit** and before opening a PR.
- **PR body**: Summary, Test plan, Risk assessment, Recommendation.
- **Privacy guard**: `git grep` for local paths / private project names
  before every commit; abort if anything matches.
