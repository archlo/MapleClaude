using MapleClaude.Net.Packet;

namespace MapleClaude.Net.Senders;

public static class GameSender
{
    public static OutPacket AliveAck()
    {
        return OutPacket.Of(InHeader.AliveAck);
    }

    public static OutPacket UserChat(string message, bool shout = false)
    {
        var p = OutPacket.Of(InHeader.UserChat);
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

    public static OutPacket TransferChannel(int channelId)
    {
        var p = OutPacket.Of(InHeader.UserTransferChannelRequest);
        p.WriteByte((byte)channelId);
        return p;
    }

    public static OutPacket UserSelectNpc(int npcObjId)
    {
        var p = OutPacket.Of(InHeader.UserSelectNpc);
        p.WriteInt(npcObjId);
        p.WriteShort(0);   // padding
        return p;
    }

    // SAY (0) / any type: action 0 = proceed / next
    public static OutPacket ScriptAnswerNext(byte msgType)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte(msgType);
        p.WriteByte(0);
        return p;
    }

    // ASK_YESNO (4)
    public static OutPacket ScriptAnswerYesNo(bool yes)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte(4);
        p.WriteByte((byte)(yes ? 1 : 0));
        return p;
    }

    // ASK_MENU (2): action 1 + int choice index
    public static OutPacket ScriptAnswerMenu(int choice)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte(2);
        p.WriteByte(1);
        p.WriteInt(choice);
        return p;
    }

    // ASK_TEXT (5): action 1 + string
    public static OutPacket ScriptAnswerText(string text)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte(5);
        p.WriteByte(1);
        p.WriteString(text);
        return p;
    }

    public static OutPacket ScriptAnswerTextCancel()
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte(5);
        p.WriteByte(0);
        return p;
    }

    // ASK_NUMBER (6): action 1 + int
    public static OutPacket ScriptAnswerNumber(int number)
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte(6);
        p.WriteByte(1);
        p.WriteInt(number);
        return p;
    }

    public static OutPacket ScriptAnswerNumberCancel()
    {
        var p = OutPacket.Of(InHeader.UserScriptMessageAnswer);
        p.WriteByte(6);
        p.WriteByte(0);
        return p;
    }
}
