# Phase 25 — Social / Community UI (Family, Buddy, Party, Guild, Alliance, Blacklist)

Authentic rebuild of the v95 community system. Source of truth: live IDA DB (`CUIUserList`,
`CUIFamily`) + the WZ assets, against the Kinoko server's social opcodes.

## Server support (Kinoko upstream, verified)
- Friend `FriendRequest(153)`/`FriendResult(65)` — load/add/accept/delete.
- Party `PartyRequest(145)`/`PartyResult(62)` — create/invite/kick/leave + load.
- Guild `GuildRequest(149)`/`GuildResult(67)` — load/withdraw (+ board/grade ops, NPC-driven).
- Alliance `AllianceRequest(167)`/`AllianceResult(68)` — present; chat via group type 3.
- Family `FamilyInfoRequest(170)`/`FamilyInfoResult(99)` etc. — present.
- **No Blacklist opcode** — the block list is **client-local** in this build.
- Group chat `GroupChat` already supports Buddy(0)/Party(1)/Guild(2)/Alliance(3).

Existing senders (reused): `FriendLoad/Add/Accept/Delete`, `PartyCreate/Invite/Kick/Leave/Join`,
`GuildLoad/Leave`, `GroupChat`, `Whisper`. Existing handlers: `OnFriendList`, `OnPartyLoad`,
`OnGuildLoad`, `OnPartyInvite`, `OnGroupMessage`.

## 1. Community window — rebuild `UI/Game/UserList.cs` as `CUIUserList/Main`
WZ `UIWindow2.img/UserList/Main`: frame `backgrnd` (264×382) + `backgrnd2`, baked title
"MAPLE USER LIST & GUILD". Tab sprites `Main/Tab/enabled|disabled/0..5` at x={9,40,71,112,143,194},
y=25, widths {30,30,40,30,50,59}. **6 tabs: Friend / Party / Guild / Alliance(Union) / Expedition /
BlackList.**

Per-tab (real data + wired actions; a clean bottom action-button row + an inline name-entry
`TextField` where a name is needed):
- **Friend** — online count + rows (status dot, name, level·job, location). Actions: **Add** (name →
  `FriendAdd`), **Delete** (selected `FriendId` → `FriendDelete`), **Invite** (selected → `PartyInvite`
  by name), **Chat** (`/b`). Friend invites accepted via `FriendAccept` on the chat prompt.
- **Party** — member rows (level/name/job + HP bar). Actions: **Create**/`PartyCreate`,
  **Invite** (name → `PartyInvite`), **Kick** (selected `CharId` → `PartyKick`),
  **Leave**/`PartyLeave`, **Chat** (`/p`).
- **Guild** — guild name + member rows (rank, online dot). Actions: **Leave** (`GuildLeave(myId,name)`),
  **Chat** (`/g`). (Invite/grade/board = leader/NPC ops → deferred.)
- **Alliance(Union)** — alliance name + members if available; **Chat** (`/a`). (Member-list decode of
  `AllianceResult` = follow-up; chat works now.)
- **Expedition** — "Not available on this server." placeholder.
- **BlackList** — client-local blocked names; **Add** (name)/**Remove** (selected).

Selection: click a row to select (drives Delete/Kick/Invite). Scroll via wheel/PageUp-Down.

## 2. Family window — new `UI/Game/FamilyWindow.cs` as `CUIFamily`
WZ `UIWindow2.img/Family` frame (214×343), baked "FAMILY". Renders the family state; full pedigree
(`FamilyInfoResult` decode + junior/senior tree) is a follow-up — no blind wire decoder shipped
(would risk desync; unknown opcodes are safely logged+skipped by the router meanwhile).

## 3. GameStage wiring
- Open: status-bar Community button / the Friends key already toggles `_userList`; add a Family opener.
- Wire UserList callbacks → senders; route `OnTextInput` to the focused UserList name field.
- Add chat commands `/b <msg>` (buddy), `/g <msg>` (guild), `/a <msg>` (alliance) alongside `/p`.

## Deferred (need new wire decoders that can't be test-verified offline)
- Alliance member list (`AllianceResult`), Family pedigree (`FamilyInfoResult`), guild
  grade/board/invite ops, party search / expedition. UI present; data/ops are follow-ups.

Build per unit (`dotnet build -p:NoAutoPublish=true`), `dotnet test`, commit on
`phase-25/social-community`; PR + merge to master.
