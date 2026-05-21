using MapleClaude.Net;

namespace MapleClaude.Net.Senders;

/// <summary>
/// Builds and sends client→server packets for the login flow.
/// All structures match Kinoko LoginHandler.java field read order.
/// </summary>
public static class LoginSender
{
    // ── CheckPassword (InHeader = 1) ─────────────────────────────────────────
    // Kinoko reads: String username, String password, byte[16] machineId,
    //               int gameRoomClient, byte gameStartMode, byte worldId,
    //               byte channelId, byte[4] partnerCode
    public static OutPacket CheckPassword(string username, string password)
    {
        return new OutPacket((short)OutHeader.CheckPassword)
            .EncodeString(username)
            .EncodeString(password)
            .EncodeArray(MachineId())
            .EncodeInt(0)             // gameRoomClient
            .EncodeByte(0)            // gameStartMode
            .EncodeByte(0)            // worldId placeholder
            .EncodeByte(0)            // channelId placeholder
            .EncodeArray(new byte[4]);// partnerCode
    }

    // ── WorldInfoRequest (InHeader = 4) ──────────────────────────────────────
    public static OutPacket WorldInfoRequest()
        => new OutPacket((short)OutHeader.WorldInfoRequest);

    // ── SelectWorld (InHeader = 5) ───────────────────────────────────────────
    // Kinoko reads: byte gameStartMode, byte worldId, byte channelId, int unk
    public static OutPacket SelectWorld(int worldId, int channelId)
    {
        return new OutPacket((short)OutHeader.SelectWorld)
            .EncodeByte(0)                  // gameStartMode
            .EncodeByte((byte)worldId)
            .EncodeByte((byte)channelId)
            .EncodeInt(0);                  // unk
    }

    // ── SelectCharacter (InHeader = 19) ──────────────────────────────────────
    // Kinoko reads: int characterId, String macAddress, String macAddressWithHddSerial
    public static OutPacket SelectCharacter(int characterId)
    {
        return new OutPacket((short)OutHeader.SelectCharacter)
            .EncodeInt(characterId)
            .EncodeString(MacAddress())
            .EncodeString(MacAddress() + "_hdd");
    }

    // ── MigrateIn (InHeader = 20) ────────────────────────────────────────────
    // Kinoko reads: int characterId, byte[16] machineId, bool subGradeCode(skip),
    //               byte hardcoded(skip), byte[8] clientKey
    public static OutPacket MigrateIn(int characterId, byte[] clientKey)
    {
        return new OutPacket((short)OutHeader.MigrateIn)
            .EncodeInt(characterId)
            .EncodeArray(MachineId())
            .EncodeBool(false)          // subGradeCode
            .EncodeByte(0)              // hardcoded
            .EncodeArray(clientKey.Length >= 8 ? clientKey[..8] : new byte[8]);
    }

    // ── CheckDuplicatedID (InHeader = 21) ────────────────────────────────────
    public static OutPacket CheckDuplicatedId(string name)
        => new OutPacket((short)OutHeader.CheckDuplicatedID).EncodeString(name);

    // ── CreateNewCharacter (InHeader = 22) ───────────────────────────────────
    // Kinoko reads: String name, int selectedRace, short selectedSubJob,
    //               int[8] selectedAL {face,hair,hairColor,skin,coat,pants,shoes,weapon},
    //               byte gender
    public static OutPacket CreateNewCharacter(
        string name, int race, int face, int hair, int hairColor,
        int skin, int coat, int pants, int shoes, int weapon, bool male)
    {
        var pkt = new OutPacket((short)OutHeader.CreateNewCharacter)
            .EncodeString(name)
            .EncodeInt(race)
            .EncodeShort(0);  // selectedSubJob
        foreach (var v in new[] { face, hair, hairColor, skin, coat, pants, shoes, weapon })
            pkt.EncodeInt(v);
        pkt.EncodeByte(male ? (byte)0 : (byte)1);
        return pkt;
    }

    // ── DeleteCharacter (InHeader = 24) ──────────────────────────────────────
    public static OutPacket DeleteCharacter(string secondaryPassword, int characterId)
    {
        return new OutPacket((short)OutHeader.DeleteCharacter)
            .EncodeString(secondaryPassword)
            .EncodeInt(characterId);
    }

    // ── AliveAck (InHeader = 25) ─────────────────────────────────────────────
    public static OutPacket AliveAck()
        => new OutPacket((short)OutHeader.AliveAck);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] MachineId()
    {
        // Deterministic fake machine ID from MAC — stable across sessions
        var raw = System.Net.NetworkInformation.NetworkInterface
            .GetAllNetworkInterfaces()
            .FirstOrDefault()?.GetPhysicalAddress()?.GetAddressBytes() ?? new byte[6];
        var id = new byte[16];
        raw.CopyTo(id, 0);
        return id;
    }

    private static string MacAddress()
    {
        var iface = System.Net.NetworkInformation.NetworkInterface
            .GetAllNetworkInterfaces()
            .FirstOrDefault();
        var mac = iface?.GetPhysicalAddress()?.ToString() ?? "000000000000";
        return mac;
    }
}
