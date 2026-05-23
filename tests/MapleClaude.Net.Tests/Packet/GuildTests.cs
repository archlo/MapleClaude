using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Phase 21 guild wire-format tests: GuildResult(67) LoadGuild_Done(28) roster
/// decode and the GuildRequest(149) load/leave encoders.
/// </summary>
public class GuildTests
{
    private static (FieldHandlers fh, PacketRouter router) NewHandlers()
    {
        var fh = new FieldHandlers(NullLogger<FieldHandlers>.Instance);
        var router = new PacketRouter(NullLogger<PacketRouter>.Instance);
        fh.Register(router);
        return (fh, router);
    }

    private static void Dispatch(PacketRouter router, OutPacket p) =>
        router.Dispatch(new InPacket(p.ToArray()), session: null!);

    [Theory]
    [InlineData(OutHeader.GuildResult, 67)]
    public void GuildResult_HasCanonicalValue(OutHeader op, int expected) =>
        ((int)op).Should().Be(expected);

    [Fact]
    public void GuildRequest_HasCanonicalValue() =>
        ((int)InHeader.GuildRequest).Should().Be(149);

    // ── LoadGuild_Done roster decode ─────────────────────────────────────────────

    [Fact]
    public void GuildResult_LoadGuildDone_DecodesRoster()
    {
        var (fh, router) = NewHandlers();
        GuildLoadArgs? captured = null;
        var fired = false;
        fh.OnGuildLoad += a => { captured = a; fired = true; };

        var p = OutPacket.Of((short)OutHeader.GuildResult);
        p.WriteByte(28);          // LoadGuild_Done
        p.WriteByte(1);           // has guild
        p.WriteInt(101);          // guild id
        p.WriteString("Knights");
        for (var i = 0; i < 5; i++) p.WriteString($"Rank{i}");
        p.WriteByte(2);           // member count
        p.WriteInt(1001);         // ids (column-major)
        p.WriteInt(1002);
        WriteMember(p, "Leader", job: 112, level: 75, rank: 1, online: 1);
        WriteMember(p, "Member", job: 212, level: 60, rank: 3, online: 0);
        p.WriteInt(20);           // memberMax
        p.WriteShort(0);          // markBg
        p.WriteByte(0);           // markBgColor
        p.WriteShort(0);          // mark
        p.WriteByte(0);           // markColor
        p.WriteString("Welcome"); // notice
        p.WriteInt(500);          // points
        p.WriteInt(0);            // allianceId
        p.WriteByte(3);           // level
        p.WriteShort(0);          // skill count
        Dispatch(router, p);

        fired.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.GuildId.Should().Be(101);
        captured.Name.Should().Be("Knights");
        captured.Members.Should().HaveCount(2);
        captured.Members[0].CharacterId.Should().Be(1001);
        captured.Members[0].Name.Should().Be("Leader");
        captured.Members[0].Rank.Should().Be(1);
        captured.Members[0].Online.Should().BeTrue();
        captured.Members[1].CharacterId.Should().Be(1002);
        captured.Members[1].Name.Should().Be("Member");
        captured.Members[1].Online.Should().BeFalse();
    }

    [Fact]
    public void GuildResult_LoadGuildDone_NoGuild_FiresNull()
    {
        var (fh, router) = NewHandlers();
        var fired = false;
        GuildLoadArgs? captured = new();
        fh.OnGuildLoad += a => { fired = true; captured = a; };

        var p = OutPacket.Of((short)OutHeader.GuildResult);
        p.WriteByte(28);          // LoadGuild_Done
        p.WriteByte(0);           // no guild
        Dispatch(router, p);

        fired.Should().BeTrue();
        captured.Should().BeNull();
    }

    private static void WriteMember(OutPacket p, string name, int job, int level, int rank, int online)
    {
        p.WriteString(name, 13);
        p.WriteInt(job);
        p.WriteInt(level);
        p.WriteInt(rank);
        p.WriteInt(online);
        p.WriteInt(0);            // commitment
        p.WriteInt(0);            // alliance rank
    }

    // ── GuildRequest encoders ────────────────────────────────────────────────────

    [Fact]
    public void GuildLoad_Encodes_TypeOnly()
    {
        var p = new InPacket(GameSender.GuildLoad().ToArray());
        p.ReadShort().Should().Be((short)InHeader.GuildRequest);
        p.ReadByte().Should().Be(0);   // LoadGuild
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void GuildLeave_Encodes_CharIdAndName()
    {
        var p = new InPacket(GameSender.GuildLeave(1001, "Leader").ToArray());
        p.ReadShort().Should().Be((short)InHeader.GuildRequest);
        p.ReadByte().Should().Be(7);   // WithdrawGuild
        p.ReadInt().Should().Be(1001);
        p.ReadString().Should().Be("Leader");
        p.Remaining.Should().Be(0);
    }
}
