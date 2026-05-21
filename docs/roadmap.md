# MapleClaude — Phase Roadmap

This is the multi-phase build plan for MapleClaude. Each phase ships its own
`phase-N/*` branch family, an entry-criteria definition, an exit-criteria
definition, and a single PR to `master` at completion.

## Phase 1 — Pre-game flow (in progress)

**Scope.** Launch → title → login → world/server select → character list →
character create → PIN entry (dormant on Kinoko) → channel migrate handoff.

**Exit criteria.** With a running Kinoko server, the client connects, logs in,
shows the world list, lets the user create/select a character, reaches the
channel migrate, and logs:

```
MigrateIn ACK observed — Phase 2 boundary reached
```

**Branch family.**

- `phase-1/scaffolding` — solution, project layout, CLAUDE docs, `.claude/`, build green.
- `phase-1/cipher` — Crypto/ + cipher tests.
- `phase-1/packet-io` — Packet/, OpCodes.cs, gen-opcodes.
- `phase-1/wz-reader` — Wz/ + fixture tests.
- `phase-1/render-bootstrap` — MapleClaudeGame, MonoGame loop, sprite atlas.
- `phase-1/login-stage` — Title, Login, WorldSelect, ChannelSelect stages.
- `phase-1/char-select` — CharSelect, CharCreate stages + AvatarData.
- `phase-1/pin-stage` — PinStage (wired dormant).
- `phase-1/migrate-handoff` — MigrationCoordinator + final wire-up.

## Phase 2 — Field load & avatar render

**Scope.** Decode `SetField` (the first in-game packet after migrate). Load
the player's starting map from `Map.wz/Map/Map<prefix>/<mapId>.img` (footholds,
ladders, portals, layers, backgrounds). Render the player's `AvatarLook`
(body + head + face + hair + equips from `Character.wz`) standing on the map.
No input yet — just the standing pose.

**Exit criteria.** First-spawn map renders with the player's avatar visible at
the correct starting coordinates and standing on the correct foothold.

## Phase 3 — Movement & camera

**Scope.** Keyboard input → `UserMove` packet construction (the v95 move-path
encoding with 12 sub-actions). Gravity, jump, run, walk. Camera follows the
player with viewport clipping and parallax background scrolling.

**Exit criteria.** Player can walk, jump, fall through one-way platforms, and
move between adjacent portals on a single map.

## Phase 4 — Mobs & combat

**Scope.** `MobEnterField`, `MobMove`, `MobChangeController`. Mob sprites,
animations, health. `UserMeleeAttack` packet construction with attack info.
Damage display.

**Exit criteria.** Player can kill a low-level mob, see damage numbers, and
have the mob respawn.

## Phase 5 — NPCs & dialog

**Scope.** `NpcEnterField`, click-to-talk, `ScriptMessage` decoder for
all dialog kinds (say/ask/menu/quiz/get-text/get-number/yes-no/icon).
Dialog UI widget.

**Exit criteria.** Click an NPC; talk through a multi-message script with
choices; trigger a server-side script effect.

## Phase 6 — Inventory & items

**Scope.** Inventory tabs (equip/use/setup/etc/cash), drag/drop,
`InventoryOperation` packet decoding, equip swap with stat updates,
tooltip widget. Cash shop UI shell (no real transactions).

**Exit criteria.** Pick up an item; equip it; see stats change; drop it.

## Phase 7 — Skills, jobs, buffs

**Scope.** Skill tree UI, skill bar, key configuration, `UserSkillUseRequest`,
`TemporaryStatSet` / `TemporaryStatReset` decoders, buff icon HUD.

**Exit criteria.** Use a job's first-class skill, see the buff appear, watch
it expire.

## Phase 8 — Social

**Scope.** Chat (`UserChat`), whispers, party invite / leave / chat, friend
list, guild basics.

**Exit criteria.** Two clients can join a party, chat in party channel, and
see each other's positions on the minimap.

## Phase 9 — Loot

**Scope.** Drop spawn (`DropEnterField`), pickup (`DropLeaveField` reasons),
meso pickup, EXP gain popups, item pickup.

**Exit criteria.** Killing mobs reliably drops loot the player can pick up.

## Phase 10 — Polish

**Scope.** Map transitions (portals), channel/cash-shop migrate, XAudio2 BGM
and SFX via MonoGame, settings persistence, full keybind editor, error
recovery, AOT publish (single self-contained `.exe`).

**Exit criteria.** First-time install → AOT-published `.exe` runs without the
.NET runtime; user can play from level 1 to mid-game continuously.

## Conventions across phases

- **Branch per phase**, commit per logical unit (file compiles, test passes,
  opcode decodes, stage renders).
- **Never commit on master**; PR per phase at exit.
- **Ask the user before each commit** and before opening a PR.
- **PR body**: Summary, Test plan, Risk assessment, Recommendation.
- **Privacy guard**: `git grep` for local paths / private project names
  before every commit; abort if anything matches.
