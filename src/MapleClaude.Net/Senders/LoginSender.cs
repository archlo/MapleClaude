using MapleClaude.Net.Packet;

namespace MapleClaude.Net.Senders;

/// <summary>
/// Outgoing login-server requests. Builders mirror upstream Kinoko handler
/// readers (<c>kinoko/handler/stage/LoginHandler.java</c>) byte-for-byte. Stage
/// code constructs an <see cref="OutPacket"/> via these helpers and hands it to
/// <c>ClientSession.Send</c>. Lives in <c>MapleClaude.Net</c> (not the exe) so
/// the wire shapes are unit testable; consumers reference it via the
/// <c>MapleClaude.Net.Senders</c> namespace exactly as before.
/// </summary>
public static class LoginSender
{
    /// <summary>
    /// CheckDuplicatedID (<see cref="InHeader.CheckDuplicatedID"/>, opcode 21).
    /// Wire: <c>string name</c>.
    /// </summary>
    public static OutPacket CheckDuplicatedId(string name)
    {
        var p = OutPacket.Of(InHeader.CheckDuplicatedID);
        p.WriteString(name);
        return p;
    }

    /// <summary>
    /// CreateNewCharacter (<see cref="InHeader.CreateNewCharacter"/>, opcode 22).
    /// Wire: <c>string name, int race, short subJob, int face, int hair,
    /// int hairColor, int skin, int coat, int pants, int shoes, int weapon,
    /// byte gender</c>.
    /// </summary>
    public static OutPacket CreateNewCharacter(
        string name,
        int race,
        int face,
        int hair,
        int hairColor,
        int skin,
        int coat,
        int pants,
        int shoes,
        int weapon,
        bool male,
        short subJob = 0)
    {
        var p = OutPacket.Of(InHeader.CreateNewCharacter);
        p.WriteString(name);
        p.WriteInt(race);
        p.WriteShort(subJob);
        p.WriteInt(face);
        p.WriteInt(hair);
        p.WriteInt(hairColor);
        p.WriteInt(skin);
        p.WriteInt(coat);
        p.WriteInt(pants);
        p.WriteInt(shoes);
        p.WriteInt(weapon);
        p.WriteByte((byte)(male ? 0 : 1));
        return p;
    }

    /// <summary>
    /// DeleteCharacter (<see cref="InHeader.DeleteCharacter"/>, opcode 24).
    /// Wire: <c>string secondaryPassword, int characterId</c>. Kinoko validates
    /// the account's secondary password before deleting (it returns IncorrectSPW
    /// otherwise), so the caller prompts for it.
    /// </summary>
    public static OutPacket DeleteCharacter(int characterId, string secondaryPassword)
    {
        var p = OutPacket.Of(InHeader.DeleteCharacter);
        p.WriteString(secondaryPassword);
        p.WriteInt(characterId);
        return p;
    }

    /// <summary>AliveAck (<see cref="InHeader.AliveAck"/>, opcode 25). Empty body.</summary>
    public static OutPacket AliveAck() => OutPacket.Of(InHeader.AliveAck);
}
