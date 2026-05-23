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

    public static OutPacket UserAbilityUp(int statType)
    {
        var p = OutPacket.Of(InHeader.UserAbilityUpRequest);
        p.WriteInt(0);   // tickCount
        p.WriteInt(statType);
        return p;
    }

    public static class MapleStat
    {
        public const int Str = 0x40;
        public const int Dex = 0x80;
        public const int Int = 0x200;
        public const int Luk = 0x400;
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
        p.WriteString(portal);               // sPortal (destination portal name)
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
