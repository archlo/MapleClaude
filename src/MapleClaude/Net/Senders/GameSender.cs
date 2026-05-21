using MapleClaude.Net;

namespace MapleClaude.Net.Senders;

/// <summary>
/// Builds and sends in-game client→server packets.
/// Structures match Kinoko handler read order.
/// </summary>
public static class GameSender
{
    // ── AliveAck ─────────────────────────────────────────────────────────────
    public static OutPacket AliveAck()
        => new OutPacket((short)OutHeader.AliveAck);

    // ── UserMove (InHeader = 44) ──────────────────────────────────────────────
    // Simplified: position + portal + foothold info
    public static OutPacket UserMove(float x, float y, float vx, float vy, int foothold)
    {
        return new OutPacket((short)OutHeader.UserMove)
            .EncodeInt(0)               // fieldKey (server validates)
            .EncodeInt(0)               // tickCount
            .EncodeInt((int)x)
            .EncodeInt((int)y)
            .EncodeInt(foothold)
            .EncodeByte(0)              // move action count
            .EncodeByte(0);             // unknown
    }

    // ── UserChat (InHeader = 54) ──────────────────────────────────────────────
    // Kinoko: String message, bool shout
    public static OutPacket UserChat(string message, bool shout = false)
    {
        return new OutPacket((short)OutHeader.UserChat)
            .EncodeString(message)
            .EncodeBool(shout);
    }

    // ── UserMeleeAttack (InHeader = 47) ──────────────────────────────────────
    public static OutPacket UserMeleeAttack(int skillId, int mobCount)
    {
        return new OutPacket((short)OutHeader.UserMeleeAttack)
            .EncodeByte((byte)(mobCount & 0x0F))  // mobCount packed in nibble
            .EncodeInt(skillId)
            .EncodeByte(0)   // skillLevel
            .EncodeByte(0);  // speed
    }

    // ── UserSkillUseRequest (InHeader = 103) ─────────────────────────────────
    public static OutPacket UserSkillUse(int skillId, byte skillLevel)
    {
        return new OutPacket((short)OutHeader.UserSkillUseRequest)
            .EncodeInt(0)               // tickCount
            .EncodeInt(skillId)
            .EncodeByte(skillLevel)
            .EncodeInt(0)               // x
            .EncodeInt(0);              // y
    }

    // ── UserSkillUpRequest (InHeader = 102) ──────────────────────────────────
    public static OutPacket UserSkillUp(int skillId)
    {
        return new OutPacket((short)OutHeader.UserSkillUpRequest)
            .EncodeInt(0)               // tickCount
            .EncodeInt(skillId);
    }

    // ── UserAbilityUpRequest (InHeader = 98) ─────────────────────────────────
    // Kinoko: int tickCount, int statType (MapleStat enum)
    public static OutPacket UserAbilityUp(int statType)
    {
        return new OutPacket((short)OutHeader.UserAbilityUpRequest)
            .EncodeInt(0)               // tickCount
            .EncodeInt(statType);
    }

    // MapleStat enum values (from Kinoko MapleStat.java)
    public static class MapleStat
    {
        public const int Str = 0x40;
        public const int Dex = 0x80;
        public const int Int = 0x200;
        public const int Luk = 0x400;
    }

    // ── UserSitRequest (InHeader = 45) ───────────────────────────────────────
    public static OutPacket UserSitRequest(short fieldSeatId = -1)
    {
        return new OutPacket((short)OutHeader.UserSitRequest)
            .EncodeShort(fieldSeatId);
    }

    // ── UserSelectNpc (InHeader = 63) ────────────────────────────────────────
    public static OutPacket UserSelectNpc(int npcObjId)
    {
        return new OutPacket((short)OutHeader.UserSelectNpc)
            .EncodeInt(npcObjId)
            .EncodeShort(0); // unk
    }

    // ── UserScriptMessageAnswer (InHeader = 65) ───────────────────────────────
    // action: 0=OK/Next, 1=Yes, 255=No/Cancel
    public static OutPacket UserScriptMessageAnswer(int messageType, byte action, int answer = 0)
    {
        return new OutPacket((short)OutHeader.UserScriptMessageAnswer)
            .EncodeByte((byte)messageType)
            .EncodeByte(action)
            .EncodeInt(answer);
    }

    // ── FuncKeyMappedModified (InHeader = 159) ────────────────────────────────
    // Sends the full 90-slot keymap to server
    public static OutPacket FuncKeyMappedModified(
        IEnumerable<(int keyIndex, int type, int actionId)> entries)
    {
        var pkt = new OutPacket((short)OutHeader.FuncKeyMappedModified);
        foreach (var (key, type, action) in entries)
        {
            pkt.EncodeInt(key);
            pkt.EncodeByte((byte)type);
            pkt.EncodeInt(action);
        }
        return pkt;
    }

    // ── UserTransferChannelRequest (InHeader = 42) ────────────────────────────
    public static OutPacket TransferChannel(int channelId)
    {
        return new OutPacket((short)OutHeader.UserTransferChannelRequest)
            .EncodeByte((byte)channelId);
    }

    // ── MobMove (InHeader = 227) ─────────────────────────────────────────────
    public static OutPacket MobMove(int mobObjId, int x, int y)
    {
        return new OutPacket((short)OutHeader.MobMove)
            .EncodeInt(mobObjId)
            .EncodeByte(0)    // action
            .EncodeInt(0)     // unk
            .EncodeInt(x)
            .EncodeInt(y)
            .EncodeShort(0)   // foothold
            .EncodeByte(0);   // moveActionCount
    }

    // ── DropPickUpRequest (InHeader = 246) ───────────────────────────────────
    public static OutPacket DropPickUp(int dropObjId, int x, int y)
    {
        return new OutPacket((short)OutHeader.DropPickUpRequest)
            .EncodeByte(0)    // fieldKey
            .EncodeInt(0)     // tickCount
            .EncodeInt(x)
            .EncodeInt(y)
            .EncodeInt(dropObjId);
    }
}
