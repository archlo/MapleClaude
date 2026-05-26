using MapleClaude.Domain;
using MapleClaude.Net.Packet;

namespace MapleClaude.Net.Senders;

/// <summary>
/// Encoders for the channel-server C→S packets the in-game stage sends. Field
/// orders mirror upstream Kinoko's per-opcode handler reads byte-for-byte.
/// Lives in <c>MapleClaude.Net</c> (not the exe) so the wire shapes are unit
/// testable; consumers reference it via the <c>MapleClaude.Net.Senders</c>
/// namespace exactly as before.
/// </summary>
public static class GameSender
{
    public static OutPacket AliveAck()
    {
        return OutPacket.Of(InHeader.AliveAck);
    }

    // UserCharacterInfoRequest(109): int update_time, int dwCharacterId, byte bPetInfo.
    // Sent when double-clicking another player; the server replies with CharacterInfo(61).
    public static OutPacket UserCharacterInfoRequest(int characterId)
    {
        var p = OutPacket.Of(InHeader.UserCharacterInfoRequest);
        p.WriteInt(0);            // update_time
        p.WriteInt(characterId);  // dwCharacterId
        p.WriteByte(0);           // bPetInfo (false)
        return p;
    }

    // ── Inventory ──────────────────────────────────────────────────────────────

    // UserChangeSlotPositionRequest(77): int update_time, byte invType,
    // short oldPos, short newPos, short count.
    // Equip: oldPos = source inventory slot, newPos = NEGATIVE body part.
    // Unequip: oldPos = NEGATIVE body part, newPos = free inventory slot.
    // Drop: newPos = 0.
    public static OutPacket ChangeSlotPosition(InventoryType invType, short oldPos, short newPos, short count)
    {
        var p = OutPacket.Of(InHeader.UserChangeSlotPositionRequest);
        p.WriteInt(0);                       // update_time
        p.WriteByte((byte)invType);
        p.WriteShort(oldPos);
        p.WriteShort(newPos);
        p.WriteShort(count);
        return p;
    }

    // UserStatChangeItemUseRequest(78): int update_time, short pos, int itemId.
    public static OutPacket UseItem(short pos, int itemId)
    {
        var p = OutPacket.Of(InHeader.UserStatChangeItemUseRequest);
        p.WriteInt(0);                       // update_time
        p.WriteShort(pos);
        p.WriteInt(itemId);
        return p;
    }

    // UserDropMoneyRequest(106): int update_time, int amount.
    public static OutPacket DropMoney(int amount)
    {
        var p = OutPacket.Of(InHeader.UserDropMoneyRequest);
        p.WriteInt(0);                       // update_time
        p.WriteInt(amount);
        return p;
    }

    // DropPickUpRequest(246): byte fieldKey, int update_time, short x, short y,
    // int dropId, int dwCliCrc. Per kinoko/handler/field/FieldHandler.handleDropPickUpRequest
    // (the server compares fieldKey to user.getFieldKey(); dwCliCrc is read but not validated).
    public static OutPacket PickUpDrop(byte fieldKey, short x, short y, int dropId)
    {
        var p = OutPacket.Of(InHeader.DropPickUpRequest);
        p.WriteByte(fieldKey);
        p.WriteInt(0);                       // update_time / tickCount
        p.WriteShort(x);
        p.WriteShort(y);
        p.WriteInt(dropId);
        p.WriteInt(0);                       // dwCliCrc
        return p;
    }

    // ── Skills ──────────────────────────────────────────────────────────────────

    // UserSkillUpRequest(102): int update_time, int skillId.
    public static OutPacket SkillUp(int skillId)
    {
        var p = OutPacket.Of(InHeader.UserSkillUpRequest);
        p.WriteInt(0);                       // update_time
        p.WriteInt(skillId);
        return p;
    }

    // UserSkillUseRequest(103) for a basic self-buff / self-target active skill:
    // int update_time, int skillId, byte slv, short delay. (Position/party/target
    // and attack skills add fields — not used for a simple self-cast.)
    public static OutPacket UseSkill(int skillId, byte slv)
    {
        var p = OutPacket.Of(InHeader.UserSkillUseRequest);
        p.WriteInt(0);                       // update_time
        p.WriteInt(skillId);
        p.WriteByte(slv);
        p.WriteShort(0);                     // tDelay
        return p;
    }

    // UserChat(54): int update_time, string text, byte onlyBalloon.
    // The server reads update_time first (UserHandler.handleUserChat); omitting
    // it shifts every following field and desyncs the cipher chain.
    public static OutPacket UserChat(string message, bool shout = false)
    {
        var p = OutPacket.Of(InHeader.UserChat);
        p.WriteInt(0);                       // update_time
        p.WriteString(message);
        p.WriteByte(shout);
        return p;
    }

    // UserEmotion(56): int nEmotion, int nDuration, byte bByItemOption.
    // No update_time prefix — UserHandler.handleUserEmotion reads int/int/bool directly.
    // duration = -1 (FuncKey path) means "play the WZ face's own per-frame total";
    // the server treats the value as opaque and echoes it on the broadcast.
    public static OutPacket UserEmotion(int emotion, int duration = -1, bool byItemOption = false)
    {
        var p = OutPacket.Of(InHeader.UserEmotion);
        p.WriteInt(emotion);
        p.WriteInt(duration);
        p.WriteByte(byItemOption);
        return p;
    }

    // UserAbilityUpRequest(98): int update_time, int dwFlag (the single Stat flag).
    // Mirrors UserHandler.handleUserAbilityUpRequest.
    public static OutPacket UserAbilityUp(int statType)
    {
        var p = OutPacket.Of(InHeader.UserAbilityUpRequest);
        p.WriteInt(0);   // update_time
        p.WriteInt(statType);
        return p;
    }

    // UserAbilityMassUpRequest(99): int update_time, int size, size×(int dwStatFlag, int nValue).
    // Used by the Stat window's auto-assign. Mirrors UserHandler.handleUserAbilityMassUpRequest.
    public static OutPacket UserAbilityMassUp(IReadOnlyList<(int statFlag, int value)> entries)
    {
        var p = OutPacket.Of(InHeader.UserAbilityMassUpRequest);
        p.WriteInt(0);                 // update_time
        p.WriteInt(entries.Count);     // size
        foreach (var (flag, value) in entries)
        {
            p.WriteInt(flag);
            p.WriteInt(value);
        }
        return p;
    }

    /// <summary>v95 <c>Stat</c> ability-up flags (the <c>dwFlag</c> sent by the +
    /// buttons). Values match the client's <c>CUIStat::OnButtonClicked</c>.</summary>
    public static class MapleStat
    {
        public const int Str   = 0x40;
        public const int Dex   = 0x80;
        public const int Int   = 0x100;
        public const int Luk   = 0x200;
        public const int MaxHp = 0x800;
        public const int MaxMp = 0x2000;
    }

    // ── Field travel / migration ─────────────────────────────────────────────────

    // UserTransferChannelRequest(42): byte channelId, int update_time.
    // (upstream MigrationHandler.handleUserTransferChannelRequest reads both.)
    public static OutPacket TransferChannel(int channelId)
    {
        var p = OutPacket.Of(InHeader.UserTransferChannelRequest);
        p.WriteByte((byte)channelId);
        p.WriteInt(0);                       // update_time
        return p;
    }

    // UserTransferFieldRequest(41): byte fieldKey, int targetMap, string portal,
    // [short x, short y if portal != ""], byte 0, byte premium, byte chase.
    // Mirrors MigrationHandler.handleUserTransferFieldRequest.
    public static OutPacket TransferField(byte fieldKey, int targetMap, string portal, short x, short y)
    {
        var p = OutPacket.Of(InHeader.UserTransferFieldRequest);
        p.WriteByte(fieldKey);
        p.WriteInt(targetMap);               // dwTargetField
        p.WriteString(portal);               // sPortal (source portal name on current field)
        if (!string.IsNullOrEmpty(portal))
        {
            p.WriteShort(x);                 // GetPos()->x
            p.WriteShort(y);                 // GetPos()->y
        }
        p.WriteByte(0);                      // (unused)
        p.WriteByte(0);                      // bPremium
        p.WriteByte(0);                      // bChase
        return p;
    }

    // UserMigrateToCashShopRequest(43): int update_time. The cash shop runs on the
    // SAME connection — the server replies with SetCashShop + load packets.
    public static OutPacket MigrateToCashShop()
    {
        var p = OutPacket.Of(InHeader.UserMigrateToCashShopRequest);
        p.WriteInt(0);                       // update_time
        return p;
    }

    // Return from the cash shop: an EMPTY UserTransferFieldRequest body — the
    // server treats a zero-length packet as "re-enter the channel".
    public static OutPacket ReturnFromCashShop()
    {
        return OutPacket.Of(InHeader.UserTransferFieldRequest);
    }

    // ── Mob control ─────────────────────────────────────────────────────────────

    // MobMove(227): the controller (= nearest player) drives each mob's movement.
    //   int dwMobID; short mobCtrlSn; byte actionMask; byte actionAndDir = (action<<1)|left;
    //   int targetInfo; int multiTargetCount + (int,int)*n; int randTimeCount + int*n;
    //   byte bActive/cheat; int hackedCode; int ptTargetX; int ptTargetY; int hackedCodeCRC;
    //   <MovePath blob>; byte bChasing; byte hasTarget; byte pvcChasing; byte pvcChasingHack;
    //   int tChaseDuration.
    // Mirrors kinoko/handler/field/MobHandler.handleMobMove byte-for-byte. The server reads
    // targetInfo / hackedCode / CRC / ptTarget / tail but does not validate them, so zeros
    // are safe for basic move actions. The MovePath blob is identical to UserMove's; build
    // it with MovePathEncoder.Encode. The server replies with MobCtrlAck(288).
    public static OutPacket MobMove(int mobId, short mobCtrlSn, byte action, bool left,
                                    byte[] movePathBlob, bool chasing = false)
    {
        var p = OutPacket.Of(InHeader.MobMove);
        p.WriteInt(mobId);                                   // dwMobID
        p.WriteShort(mobCtrlSn);                             // nMobCtrlSN
        p.WriteByte(0);                                      // actionMask (no rush/toss)
        p.WriteByte((byte)((action << 1) | (left ? 1 : 0))); // (nAction << 1) | bLeft
        p.WriteInt(0);                                       // targetInfo (TARGETINFO union)
        p.WriteInt(0);                                       // aMultiTargetForBall count
        p.WriteInt(0);                                       // aRandTimeforAreaAttack count
        p.WriteByte(0);                                      // bActive | 16*!cheatRand
        p.WriteInt(0);                                       // HackedCode
        p.WriteInt(0);                                       // ptTarget.x
        p.WriteInt(0);                                       // ptTarget.y
        p.WriteInt(0);                                       // dwHackedCodeCRC
        p.WriteBytes(movePathBlob);                          // <MovePath>
        p.WriteByte(chasing ? (byte)1 : (byte)0);            // bChasing
        p.WriteByte(0);                                      // pTarget != 0
        p.WriteByte(0);                                      // pvcActive.bChasing
        p.WriteByte(0);                                      // pvcActive.bChasingHack
        p.WriteInt(0);                                       // pvcActive.tChaseDuration
        return p;
    }

    // ── Combat (mob -> player) ───────────────────────────────────────────────────

    // UserHit(52): the victim reports being hit by a mob. In real Maple mobs never send
    // attacks themselves; instead the controller client detects the collision and sends
    // this packet, and the server validates the damage against the mob's MobAttack
    // template and applies the HP loss / status effects. Mirrors
    // kinoko/handler/user/HitHandler.handleUserHit field-for-field for the
    // attackIndex >= 0 (mob-attack) branch:
    //   int update_time, byte attackIndex, byte magicElemAttr, int damage,
    //   int templateId, int mobId, byte dir, byte reflect, byte guard,
    //   byte knockback (1 = no knockback), [reflect block omitted], byte stance.
    // For a basic body-touch hit attackIndex = 0 (the body-attack index in MobAttack).
    public static OutPacket UserHit(byte attackIndex, byte magicElemAttr, int damage,
                                    int templateId, int mobId, byte dir, byte knockback = 1)
    {
        var p = OutPacket.Of(InHeader.UserHit);
        p.WriteInt(0);                       // get_update_time()
        p.WriteByte(attackIndex);            // nAttackIdx (body = 0; -1 Counter, -2 Obstacle, -3 Stat)
        p.WriteByte(magicElemAttr);          // nMagicElemAttr
        p.WriteInt(damage);                  // nDamage
        p.WriteInt(templateId);              // dwTemplateID
        p.WriteInt(mobId);                   // MobID
        p.WriteByte(dir);                    // nDir
        p.WriteByte(0);                      // nX = 0   (reflect)
        p.WriteByte(0);                      // bGuard
        p.WriteByte(knockback);              // (bKnockback != 0) + 1   ->   1 = none, 2 = knockback
        // reflect block skipped: only sent when knockback > 1 || reflect != 0. Sending the
        // reflect block when knockback==2 would be authentic to the real client, but
        // Kinoko's HitHandler enters that block iff `knockback > 1 || reflect != 0` — so
        // for our basic body-touch case we'd need ALL the reflect fields zeroed. Skipping
        // the block keeps the wire shorter; the server reads through the `else` path and
        // never references the reflect/power-guard fields. If knockback validation later
        // requires the block, expand here.
        p.WriteByte(0);                      // bStance | (nSkillID_Stance == 33101006 ? 2 : 0)
        return p;
    }

    // ── NPC shop (UserShopRequest 66) ─────────────────────────────────────────────
    // Mirrors kinoko ShopDialog.handlePacket (ShopRequestType: Buy=0 Sell=1 Recharge=2 Close=3).

    // Buy(0): byte type, short shopSlot, int itemId, short count, int price.
    public static OutPacket ShopBuy(short shopSlot, int itemId, short count, int price)
    {
        var p = OutPacket.Of(InHeader.UserShopRequest);
        p.WriteByte(0);
        p.WriteShort(shopSlot);
        p.WriteInt(itemId);
        p.WriteShort(count);
        p.WriteInt(price);
        return p;
    }

    // Sell(1): byte type, short pos, int itemId, short count.
    public static OutPacket ShopSell(short pos, int itemId, short count)
    {
        var p = OutPacket.Of(InHeader.UserShopRequest);
        p.WriteByte(1);
        p.WriteShort(pos);
        p.WriteInt(itemId);
        p.WriteShort(count);
        return p;
    }

    // Recharge(2): byte type, short pos.
    public static OutPacket ShopRecharge(short pos)
    {
        var p = OutPacket.Of(InHeader.UserShopRequest);
        p.WriteByte(2);
        p.WriteShort(pos);
        return p;
    }

    // Close(3): byte type, no body.
    public static OutPacket ShopClose()
    {
        var p = OutPacket.Of(InHeader.UserShopRequest);
        p.WriteByte(3);
        return p;
    }

    // ── Player storage / trunk (UserTrunkRequest 67) ──────────────────────────────
    // Mirrors kinoko TrunkDialog.handlePacket (TrunkRequestType: GetItem=4 PutItem=5
    // SortItem=6 Money=7 CloseDialog=8). Opening is server-initiated — the server
    // sends TrunkResult/OpenTrunkDlg after the player talks to a storage NPC.

    // GetItem(4): byte type, byte invType, byte position. Withdraw to inventory.
    // invType is the InventoryType value (1=Equip 2=Consume 3=Install 4=Etc 5=Cash);
    // position is the item's index within that type's trunk block.
    public static OutPacket TrunkWithdraw(byte invType, byte position)
    {
        var p = OutPacket.Of(InHeader.UserTrunkRequest);
        p.WriteByte(4);
        p.WriteByte(invType);
        p.WriteByte(position);
        return p;
    }

    // PutItem(5): byte type, short pos, int itemId, short quantity. Deposit from
    // the inventory slot to the trunk.
    public static OutPacket TrunkDeposit(short inventoryPos, int itemId, short quantity)
    {
        var p = OutPacket.Of(InHeader.UserTrunkRequest);
        p.WriteByte(5);
        p.WriteShort(inventoryPos);
        p.WriteInt(itemId);
        p.WriteShort(quantity);
        return p;
    }

    // SortItem(6): byte type, no body.
    public static OutPacket TrunkSort()
    {
        var p = OutPacket.Of(InHeader.UserTrunkRequest);
        p.WriteByte(6);
        return p;
    }

    // Money(7): byte type, int money. Positive withdraws from the trunk to the
    // inventory; negative deposits from the inventory to the trunk.
    public static OutPacket TrunkWithdrawMoney(int amount)
    {
        var p = OutPacket.Of(InHeader.UserTrunkRequest);
        p.WriteByte(7);
        p.WriteInt(amount);
        return p;
    }

    public static OutPacket TrunkDepositMoney(int amount)
    {
        var p = OutPacket.Of(InHeader.UserTrunkRequest);
        p.WriteByte(7);
        p.WriteInt(-amount);
        return p;
    }

    // CloseDialog(8): byte type, no body.
    public static OutPacket TrunkClose()
    {
        var p = OutPacket.Of(InHeader.UserTrunkRequest);
        p.WriteByte(8);
        return p;
    }

    // ── Maple Messenger (Messenger 143) ───────────────────────────────────────────
    // Mirrors kinoko UserHandler.handleMessenger (MessengerProtocol: Enter=0 Leave=2
    // Invite=3 Chat=6). The window is opened client-side by sending Enter with a
    // messengerId of 0 (create a new room) or an invite's dwSN (join that room).

    // Enter(0): byte action, int messengerId (dwJoinSN; 0 to create a new room).
    public static OutPacket MessengerEnter(int messengerId)
    {
        var p = OutPacket.Of(InHeader.Messenger);
        p.WriteByte(0);
        p.WriteInt(messengerId);
        return p;
    }

    // Leave(2): byte action, no body.
    public static OutPacket MessengerLeave()
    {
        var p = OutPacket.Of(InHeader.Messenger);
        p.WriteByte(2);
        return p;
    }

    // Invite(3): byte action, string targetName.
    public static OutPacket MessengerInvite(string targetName)
    {
        var p = OutPacket.Of(InHeader.Messenger);
        p.WriteByte(3);
        p.WriteString(targetName);
        return p;
    }

    // Chat(6): byte action, string message.
    public static OutPacket MessengerChat(string text)
    {
        var p = OutPacket.Of(InHeader.Messenger);
        p.WriteByte(6);
        p.WriteString(text);
        return p;
    }

    // ── Quests (UserQuestRequest 119) ─────────────────────────────────────────────
    // Mirrors kinoko QuestHandler (QuestRequestType: Accept=1 Complete=2 Resign=3).

    // Accept(1): byte type, short questId, int npcTemplateId, int itemPos, short x, short y.
    public static OutPacket QuestAccept(short questId, int npcId, short x, short y)
    {
        var p = OutPacket.Of(InHeader.UserQuestRequest);
        p.WriteByte(1);
        p.WriteShort(questId);
        p.WriteInt(npcId);
        p.WriteInt(0);          // itemPos
        p.WriteShort(x);
        p.WriteShort(y);
        return p;
    }

    // Complete(2): byte type, short questId, int npcTemplateId, int itemPos, short x, short y, int rewardIndex.
    public static OutPacket QuestComplete(short questId, int npcId, short x, short y, int rewardIndex = 0)
    {
        var p = OutPacket.Of(InHeader.UserQuestRequest);
        p.WriteByte(2);
        p.WriteShort(questId);
        p.WriteInt(npcId);
        p.WriteInt(0);          // itemPos
        p.WriteShort(x);
        p.WriteShort(y);
        p.WriteInt(rewardIndex);
        return p;
    }

    // Resign(3): byte type, short questId.
    public static OutPacket QuestResign(short questId)
    {
        var p = OutPacket.Of(InHeader.UserQuestRequest);
        p.WriteByte(3);
        p.WriteShort(questId);
        return p;
    }

    // OpeningScript(4): byte type, short questId, int npcTemplateId, short x, short y.
    // Runs the quest's q{id}s start script (the server validates canStartQuest first, then drives the
    // conversation via ScriptMessage). This is how a *quest* NPC starts a quest — UserSelectNpc only
    // fires for NPCs that carry a general info/script.
    public static OutPacket QuestStartScript(short questId, int npcTemplateId, short x, short y)
    {
        var p = OutPacket.Of(InHeader.UserQuestRequest);
        p.WriteByte(4);
        p.WriteShort(questId);
        p.WriteInt(npcTemplateId);
        p.WriteShort(x);
        p.WriteShort(y);
        return p;
    }

    // CompleteScript(5): same shape; runs q{id}e (server validates canCompleteQuest first).
    public static OutPacket QuestCompleteScript(short questId, int npcTemplateId, short x, short y)
    {
        var p = OutPacket.Of(InHeader.UserQuestRequest);
        p.WriteByte(5);
        p.WriteShort(questId);
        p.WriteInt(npcTemplateId);
        p.WriteShort(x);
        p.WriteShort(y);
        return p;
    }

    // ── Guild (GuildRequest 149) ──────────────────────────────────────────────────
    // Mirrors kinoko GuildRequestType (LoadGuild=0, WithdrawGuild=7).

    // LoadGuild(0): byte type, no body. The server replies with GuildResult
    // LoadGuild_Done (guild = null if the player has no guild).
    public static OutPacket GuildLoad()
    {
        var p = OutPacket.Of(InHeader.GuildRequest);
        p.WriteByte(0);
        return p;
    }

    // WithdrawGuild(7): byte type, int characterId, string characterName.
    public static OutPacket GuildLeave(int characterId, string characterName)
    {
        var p = OutPacket.Of(InHeader.GuildRequest);
        p.WriteByte(7);
        p.WriteInt(characterId);
        p.WriteString(characterName);
        return p;
    }

    // UserSelectNpc(63): int objectId, short userX, short userY
    // (upstream UserHandler.handleUserSelectNpc reads the player's position).
    public static OutPacket UserSelectNpc(int npcObjId, short userX, short userY)
    {
        var p = OutPacket.Of(InHeader.UserSelectNpc);
        p.WriteInt(npcObjId);
        p.WriteShort(userX);
        p.WriteShort(userY);
        return p;
    }

    // UserScriptMessageAnswer(65). The echoed msgType MUST equal the type the
    // server last sent (UserHandler.handleUserScriptMessageAnswer looks it up).
    //
    // SAY / SAYIMAGE / ASKYESNO / ASKACCEPT: just byte action.
    //   action -1 = prev, 0 = no/end/dismiss, 1 = yes/next.
    public static OutPacket ScriptAnswerSay(ScriptMessageType type, sbyte action)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte((byte)type);
        p.WriteByte((byte)action);
        return p;
    }

    // ASKMENU / ASKNUMBER / ASKSLIDEMENU: byte action, then (if action==1) int answer.
    //   For ASKMENU the int is the selection index; for ASKNUMBER it's the value.
    public static OutPacket ScriptAnswerNumber(ScriptMessageType type, int answer)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte((byte)type);
        p.WriteByte(1);
        p.WriteInt(answer);
        return p;
    }

    // ASKTEXT / ASKBOXTEXT: byte action, then (if action==1) string answer.
    public static OutPacket ScriptAnswerText(ScriptMessageType type, string answer)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte((byte)type);
        p.WriteByte(1);
        p.WriteString(answer);
        return p;
    }

    // Cancel / dismiss a prompt (action 0) for any ASK* type.
    public static OutPacket ScriptAnswerCancel(ScriptMessageType type)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte((byte)type);
        p.WriteByte(0);
        return p;
    }

    // ── Social: chat ─────────────────────────────────────────────────────────────

    /// <summary>Group-chat target (nChatTarget). Mirrors upstream
    /// <c>kinoko/packet/field/ChatGroupType</c>.</summary>
    public enum ChatGroupType : byte
    {
        Friend = 0,   // buddy chat
        Party  = 1,
        Guild  = 2,
        Alliance = 3,
        Expedition = 6,   // values 4/5 are couple chat (kinoko ChatGroupType)
    }

    // GroupMessage(140): int update_time, byte type, byte count, int[count] memberIds, string text.
    // Server reads the member-id list verbatim and broadcasts to those ids
    // (kinoko UserHandler.handleGroupMessage).
    public static OutPacket GroupChat(ChatGroupType type, IReadOnlyList<int> memberIds, string text)
    {
        var p = OutPacket.Of(InHeader.GroupMessage);
        p.WriteInt(0);                       // update_time
        p.WriteByte((byte)type);
        p.WriteByte((byte)memberIds.Count);  // nMemberCnt
        foreach (var id in memberIds)
        {
            p.WriteInt(id);
        }
        p.WriteString(text);
        return p;
    }

    // Whisper(141) send: byte flag, int update_time, string targetName, string text.
    // flag 0x6 = WhisperRequest (Whisper 0x2 | Request 0x4).
    private const byte WhisperRequestFlag = 0x6;

    public static OutPacket Whisper(string targetName, string text)
    {
        var p = OutPacket.Of(InHeader.Whisper);
        p.WriteByte(WhisperRequestFlag);
        p.WriteInt(0);                       // update_time
        p.WriteString(targetName);
        p.WriteString(text);
        return p;
    }

    // ── Social: party ────────────────────────────────────────────────────────────
    // PartyRequest(145): byte requestType then per-type fields. Request type
    // values mirror upstream kinoko/server/party/PartyRequestType.
    private const byte PartyReqCreateNewParty = 1;
    private const byte PartyReqWithdrawParty  = 2;
    private const byte PartyReqJoinParty      = 3;
    private const byte PartyReqInviteParty    = 4;
    private const byte PartyReqKickParty      = 5;

    public static OutPacket PartyCreate()
    {
        var p = OutPacket.Of(InHeader.PartyRequest);
        p.WriteByte(PartyReqCreateNewParty);
        return p;
    }

    public static OutPacket PartyLeave()
    {
        var p = OutPacket.Of(InHeader.PartyRequest);
        p.WriteByte(PartyReqWithdrawParty);
        p.WriteByte(0);                      // hardcoded 0 the server reads
        return p;
    }

    // Accept an invite: JoinParty echoes the inviter's character id. The server
    // reads `int inviterId` then a trailing byte (PartyHandler.handlePartyRequest).
    public static OutPacket PartyJoin(int inviterId)
    {
        var p = OutPacket.Of(InHeader.PartyRequest);
        p.WriteByte(PartyReqJoinParty);
        p.WriteInt(inviterId);
        p.WriteByte(0);                      // unknown trailing byte from InviteParty
        return p;
    }

    public static OutPacket PartyInvite(string targetName)
    {
        var p = OutPacket.Of(InHeader.PartyRequest);
        p.WriteByte(PartyReqInviteParty);
        p.WriteString(targetName);
        return p;
    }

    public static OutPacket PartyKick(int characterId)
    {
        var p = OutPacket.Of(InHeader.PartyRequest);
        p.WriteByte(PartyReqKickParty);
        p.WriteInt(characterId);
        return p;
    }

    // ── Social: friends ──────────────────────────────────────────────────────────
    // FriendRequest(153): byte requestType then per-type fields. Request type
    // values mirror upstream kinoko/world/user/friend/FriendRequestType.
    private const byte FriendReqLoadFriend   = 0;
    private const byte FriendReqSetFriend    = 1;
    private const byte FriendReqAcceptFriend = 2;
    private const byte FriendReqDeleteFriend = 3;

    public static OutPacket FriendLoad()
    {
        var p = OutPacket.Of(InHeader.FriendRequest);
        p.WriteByte(FriendReqLoadFriend);
        return p;
    }

    // SetFriend: add (or re-group) a friend by name. The server reads
    // `string targetName, string group` (FriendHandler.handleFriendRequest).
    public static OutPacket FriendAdd(string targetName, string group = "Default Group")
    {
        var p = OutPacket.Of(InHeader.FriendRequest);
        p.WriteByte(FriendReqSetFriend);
        p.WriteString(targetName);
        p.WriteString(group);
        return p;
    }

    public static OutPacket FriendAccept(int friendId)
    {
        var p = OutPacket.Of(InHeader.FriendRequest);
        p.WriteByte(FriendReqAcceptFriend);
        p.WriteInt(friendId);
        return p;
    }

    public static OutPacket FriendDelete(int friendId)
    {
        var p = OutPacket.Of(InHeader.FriendRequest);
        p.WriteByte(FriendReqDeleteFriend);
        p.WriteInt(friendId);
        return p;
    }
}
