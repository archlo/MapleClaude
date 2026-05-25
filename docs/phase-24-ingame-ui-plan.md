# Phase 24 — In-game UI polish & authentic window rebuilds

Autonomous work batch (14 user-requested items). Source of truth for the authentic
rebuilds is the live v95 IDA database (idalib MCP) + the standard GMS v95 WZ assets,
cross-checked against the existing committed UI in `src/MapleClaude/UI/Game/`.

Legend: ✅ quick fix · 🏗️ authentic rebuild (IDB-driven) · class names are the v95
client's `CUI*` classes (research input — never commit raw IDB addresses/paths).

---

## 1. ✅ Item inventory — visible X (close) button
**File:** `UI/Game/ItemInventory.cs`
`CUIItem` has a real close button; our window only has an invisible `CloseRect`
hotspot ("X baked into art" comment is wrong — the v95 `UIWindow2.img/Item` frame's
X is a `BtClose`-style button). `EquipInventory` already loads `Basic.img/BtClose3`.
- Load a `BtClose` button: prefer `UIWindow2.img/Item/Bt{Close,X}` if present, else
  the shared `Basic.img/BtClose3`, anchored top-right (origin-baked or +(PanelW-…,6)).
- Keep the existing `CloseRect` as a fallback hit-area. Re-layout in both modes
  (small uses `_bgSmall[0].Width`, full uses `_bgFull[0].Width`).

## 2. 🏗️ Quest Log — authentic 3-category list (Available / In Progress / Complete)
**File:** `UI/Game/QuestLog.cs` (rebuild) — class `CUIQuestInfo` (+ `CUIQuestInfoDetail`).
Current is a 2-tab hand-drawn panel. v95 has 3 tab buttons and a category/scroll list.
- WZ: `UIWindow2.img/Quest` (or `UIWindow.img/QuestList`); dump to confirm node names
  (`backgrnd`, tab buttons `BtPerform`/`BtComplete`/`BtResign`?, list rows, scrollbar).
- Tabs: **Available** (not started, requirements met), **In Progress** (started),
  **Completed**. Data: started/completed come from the server quest records we already
  hold; "Available" derives from `Quest.wz/Check.img/<id>` start requirements filtered
  by level/job + not-started/not-completed (best-effort; render UI authentically even
  where the Available set is approximate).
- Keep `OnResign`. Wire the detail pane if cheap; otherwise authentic main list first.

## 3. ✅ Right Ctrl / Right Alt fire the same binding as the left modifier (held keys)
**Files:** `UI/Game/KeyConfig.cs`
`ForKey` (discrete press) already folds R→L. The **bug is in `AnyHeld`**: it only
checks the literal bound scancode's `Keys`, so a binding stored on `ScLCtrl` (29) is
never matched when **Right** Ctrl is physically held (used by held-attack repeat).
- In `AnyHeld`, when the matched scancode is a left modifier (`ScLCtrl/ScLShift/ScLAlt`),
  also test the right sibling (`Keys.RightControl/RightShift/RightAlt`). Generalizes to
  any binding on a modifier working from both physical keys, matching v95
  `GetShortCutIndexByPos` folding.

## 4. ✅ No walking while attacking (root the swing; kill the slide)
**Files:** `Stages/GameStage.cs`, `Character/PlayerController.cs`
v95 roots a grounded melee swing. Currently the controller comment explicitly allows
walking while swinging → visible slide.
- Add `public bool Grounded => _grounded;` and `public void StopWalking()` (zero
  `_velocity.X` when grounded) to `PlayerController`.
- In `GameStage.Update`, while `_attackCooldown > 0f && _physics.Grounded`, force
  `input.Left = input.Right = false` (still allow facing? no — leave as-is; jump stays
  allowed for jump-attack). Call `_physics.StopWalking()` once at attack start in
  `DoMeleeAttack` so there's no decel slide.

## 5. 🏗️ Authentic item tooltip + "ghost" drag icon
**Files:** new `UI/Game/ItemTooltip.cs`, `UI/Game/ItemInventory.cs`,
`UI/Game/EquipInventory.cs`, `Character/ItemIconLoader.cs` (+ equip-info reader).
Class `CUIToolTip` (`ShowItemToolTip`, `DrawItemTitle`, `DrawItemReqJob`,
`DrawTextEquip_Req[_Level]`, `DrawReqSkill`, `AddOptionInfo`/`DrawOptionInfo`,
`GetItemName`, `GetFontByType`). The v95 tooltip is a composed multi-line card:
- Header frame (`UI.wz/UIToolTip.img/Item/Frame*` / `ItemIcon`), item name colored by
  potential grade, item icon, **required level/STR/DEX/INT/LUK/job** (red when unmet),
  category line, equip stat lines (`incPAD/incMAD/incSTR/...`), and description.
- Data source: `Item.wz` (`Item/Consume|Install|Etc|Cash/<cat>.img/<id>/info`) and
  `Character.wz` (equips: `0XXXXXXX.img/info`) — read `reqLevel/reqSTR/reqDEX/reqINT/
  reqLUK/reqJob/reqPOP`, `inc*`, `attackSpeed`, `tuc`, `cash`, `only`, `price`, plus
  `String.wz/...` name+desc (already via `Game.Names`/StringPool where possible).
- Add an `ItemData` cache/provider (extend `ItemIconLoader` or new `ItemInfoProvider`).
- **Ghost drag:** picking up a slot item attaches its icon to the cursor (semi-
  transparent), like `CDraggableItem`. Left-press on an occupied slot starts a drag;
  drawn under the cursor; drop on another slot / equip / world handled in a later pass
  (at minimum: visual ghost while the button is held, matching the client feel).
- Replace the basic `DrawTooltip` in both inventory windows with the new tooltip.

## 6. ✅ Double-click equipped item → unequip
**Files:** `UI/Game/EquipInventory.cs`, `Stages/GameStage.cs`
`CUIEquip::OnMouseButton` → `CDraggableItem::GetOffEquipItem` moves a worn item to the
first free Equip-inventory slot. We have no double-click handler on the equip window.
- Add double-click detection (mirror `ItemInventory`'s `_lastClick*`). On dbl-click of a
  worn slot, raise `OnUnequip(bodyPart)`.
- `GameStage`: send `ChangeSlotPosition(Equipped, (short)-bodyPart, freeEquipSlot, 1)`
  where `freeEquipSlot` is the first empty slot in the Equip tab (`ItemInventory`).

## 7. ✅ Double-click Equip-tab item → equip
**Files:** `Stages/GameStage.cs` (verify/repair existing wiring).
`ItemInventory` already double-clicks → `OnItemActivate` → `OnInventoryItemActivate`
case 0 → `ChangeSlotPosition(Equip, slot, -bodyPart, 1)`. Verify `EquipBodyPart`
covers v95 categories incl. two-handed weapons (`13x`–`17x`) and overall (105). Improve
to read the worn slot from item data when available; keep the category fallback.

## 8. 🏗️ In-game channel selection — authentic `CUIChannelShift`
**File:** `UI/Game/ChannelSelect.cs` (rebuild) — class `CUIChannelShift`.
NOT the login `CUIChannelSelect`. Grid from `GetRectFromIdx`:
`left=70*(i%5)+11, top=20*(i/5)+55, right=+79, bottom=+75` → **68×20 cells, 5 cols,
pitch (70,20), base (11,55)**. Triggered by `CWvsContext::ChannelShift`.
- WZ: dump to find the frame (`UIWindow2.img/ChannelShift`? or reuse
  `Login.img/WorldSelect/chBackgrn`+`channel/<i>/{normal,disabled}`+`chSelect`).
  Render real channel button canvases per cell (enabled/disabled/selected), current
  channel marker, GoWorld/Back buttons.
- Wire to `GameSender.TransferChannel(ch-1)` (already done) + populate channel count
  from world data if available (else 20).

## 9. ✅/🏗️ Bottom-bar "System" pop-up uses the modern StatusBar2 art
**Files:** `UI/Game/StatusBar.cs`, `Stages/GameStage.cs`
ESC→`CUIGameMenu` correctly uses `UIWindow.img/GameMenu` (verified). The user's "old UI"
is the **bottom-bar System button**: it currently routes to `CUIGameMenu`. v95's
bottom bar has its own `StatusBar2.img/mainBar/System` pop-up (modern art) with
**Game Option / System Option / JoyPad**, parallel to the existing `Menu` pop-up.
- Build a second `SubMenu` from `StatusBar2.img/mainBar/System` (`BtGameOption`,
  `BtSystemOption`, `BtJoyPad`), toggled by `BtSystem`; route Game/System Option to
  the rebuilt Options dialog (task 14). Keep ESC → `CUIGameMenu` unchanged.

## 10. ✅ Stat window — name shows first, fix vertical text position
**Files:** `UI/Game/StatsInfo.cs`, `Stages/GameStage.cs`
Row Y's already match `CUIStat::Draw` exactly. Two real issues:
- **Name empty:** `_stats.Name` is only set on `StatChanged`; `OnSetField` never pushes
  the SetField `CharacterStat`. → In `OnSetField`, populate `_charStats` from `args.Stat`
  and call `PushCharStats` so name/job/level/stats are correct immediately.
- **Text too low/large:** give the stat values a dedicated small font (native-scale
  11/12px like the dialog font) and/or a small Y nudge so glyph tops align with the
  WZ label rows (the original `IWzFont` sits higher than our default `Game.Font`).

## 11. 🏗️ Show the player's character (avatar) under the name
**Files:** `UI/Game/CharInfo.cs` (rebuild, task 13), `Stages/GameStage.cs`
`CUIStat` has no avatar (verified) — the character avatar belongs in the **character
info** window (`CUIUserInfo`), where name sits above the avatar. Implemented as part of
task 13: pass the player's `AvatarLook` + `CharacterRenderer` to the char-info window and
render the standing avatar below the name plate.

## 12. 🏗️ Skill window — authentic `CUISkill`
**File:** `UI/Game/SkillBook.cs` (rebuild) — class `CUISkill`.
WZ: `UIWindow2.img/Skill` (`backgrnd`, `tab/enabled|disabled/<i>`, `BtSpUp`, scrollbar,
`sp`/`sp_backgrnd` SP counter, skill slot grid). v95 shows a **grid of skill icons**
per job-tab (not text rows) with level under each, an SP-up control, and a hover tooltip
(reuse task 5's `DrawReqSkill`/skill tooltip). Use `GetSkillIndexFromPoint`,
`SetTabItems`, `SetButtons`, `OnSkillLevelUpButton` for geometry/behavior. Keep
`OnSkillUp`/`OnSkillCast` wiring; tabs come from the learned skills' job roots.

## 13. 🏗️ Character profile — authentic `CUIUserInfo`
**File:** `UI/Game/CharInfo.cs` (rebuild) — class `CUIUserInfo` (+ `CUIUserInfoDetail`).
WZ: `UIWindow2.img/UserInfo` (frame, name plate, avatar area, stat lines, guild/fame,
detail toggle). Render: name, the player's **avatar** (task 11), level, job, fame,
guild, and the personality/extended lines where data exists. Self-open from the
status-bar Character button / CharInfo key; (other players via `CharacterInfoRequest`
is a later wire-up). Falls back gracefully if `UIWindow2.img/UserInfo` is absent.

## 14. 🏗️ System / Game Options dialog — authentic art + controls
**File:** `UI/Game/OptionMenu.cs` (rebuild).
Current dialog loads `UIWindow.img/OptionMenu/backgrnd` but hand-draws every control.
Rebuild against the authentic WZ (dump `UIWindow.img/OptionMenu` **and** any
`UIWindow2.img` option node) using the baked tabs/checkbox/slider button canvases
(`BtClose/BtOK/BtCancel` + real radio/check `Bt*` nodes) so it reads as v95, not a
hand-drawn panel. Preserve current behavior (BGM/SFX volume, resolution, display
toggles, OK/Cancel persistence) but bind clicks to the real control rects.

---

## Execution order
1. Quick wins (low risk, high value): **3, 4, 1, 7, 6, 10**.
2. Tooltip/ghost (**5**) — unlocks skill/charinfo tooltips.
3. Authentic rebuilds: **8 (channel), 9 (system popup), 14 (options), 12 (skill),
   13+11 (char info + avatar), 2 (quest log)**.

Build with `dotnet build -c Debug -p:NoAutoPublish=true` after each unit; commit at
logical checkpoints on the current `phase-23/ingame-statusbar` branch (re-check
`git status`/branch first — the user may be doing their own git). No auto-push.
