using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Phase 10 travel / migration wire-format tests: portal field transfer, channel
/// transfer (with the trailing update_time the server reads), cash-shop migrate
/// request + empty-body return, and the inbound MigrateCommand(16) decode.
/// </summary>
public class TravelMigrationTests
{
    private static (FieldHandlers fh, PacketRouter router) NewHandlers()
    {
        var fh = new FieldHandlers(NullLogger<FieldHandlers>.Instance);
        var router = new PacketRouter(NullLogger<PacketRouter>.Instance);
        fh.Register(router);
        return (fh, router);
    }

    // ── Opcode values ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InHeader.UserTransferFieldRequest, 41)]
    [InlineData(InHeader.UserTransferChannelRequest, 42)]
    [InlineData(InHeader.UserMigrateToCashShopRequest, 43)]
    public void OutboundTravelOpcodes_HaveCanonicalValues(InHeader op, int expected) =>
        ((int)op).Should().Be(expected);

    [Fact]
    public void MigrateCommand_HasCanonicalValue() =>
        ((int)OutHeader.MigrateCommand).Should().Be(16);

    // ── TransferChannel (C→S) — must carry the trailing update_time ───────────────

    [Fact]
    public void TransferChannel_Encodes_ChannelAndUpdateTime()
    {
        var p = new InPacket(GameSender.TransferChannel(3).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserTransferChannelRequest);
        p.ReadByte().Should().Be(3);     // channelId
        p.ReadInt().Should().Be(0);      // update_time (was missing → desync)
        p.Remaining.Should().Be(0);
    }

    // ── TransferField (C→S) ──────────────────────────────────────────────────────

    [Fact]
    public void TransferField_WithPortal_EncodesPositionBlock()
    {
        var p = new InPacket(GameSender.TransferField(
            fieldKey: 2, targetMap: 100000000, portal: "west00", x: 250, y: -120).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserTransferFieldRequest);
        p.ReadByte().Should().Be(2);            // fieldKey
        p.ReadInt().Should().Be(100000000);     // targetMap
        p.ReadString().Should().Be("west00");   // source portal name on current field
        p.ReadShort().Should().Be(250);         // x
        p.ReadShort().Should().Be(-120);        // y
        p.ReadByte().Should().Be(0);            // unused
        p.ReadByte().Should().Be(0);            // premium
        p.ReadByte().Should().Be(0);            // chase
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void TransferField_EmptyPortal_OmitsPositionBlock()
    {
        var p = new InPacket(GameSender.TransferField(
            fieldKey: 1, targetMap: 200000000, portal: "", x: 5, y: 6).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserTransferFieldRequest);
        p.ReadByte().Should().Be(1);
        p.ReadInt().Should().Be(200000000);
        p.ReadString().Should().Be("");
        // No x/y when the portal name is empty.
        p.ReadByte().Should().Be(0);            // unused
        p.ReadByte().Should().Be(0);            // premium
        p.ReadByte().Should().Be(0);            // chase
        p.Remaining.Should().Be(0);
    }

    // ── Cash-shop migrate request + return (C→S) ─────────────────────────────────

    [Fact]
    public void MigrateToCashShop_Encodes_UpdateTimeOnly()
    {
        var p = new InPacket(GameSender.MigrateToCashShop().ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserMigrateToCashShopRequest);
        p.ReadInt().Should().Be(0);      // update_time
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void ReturnFromCashShop_Encodes_EmptyBody()
    {
        var p = new InPacket(GameSender.ReturnFromCashShop().ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserTransferFieldRequest);
        p.Remaining.Should().Be(0);      // empty body = "re-enter the channel"
    }

    // ── MigrateCommand (S→C) decode ──────────────────────────────────────────────

    [Fact]
    public void MigrateCommand_Decodes_HostAndPort()
    {
        var (fh, router) = NewHandlers();
        (byte[] host, ushort port)? captured = null;
        fh.OnMigrateCommand += (h, p) => captured = (h, p);

        var pkt = OutPacket.Of((short)OutHeader.MigrateCommand);
        pkt.WriteByte(1);                       // migrate flag
        pkt.WriteBytes(new byte[] { 127, 0, 0, 1 });
        pkt.WriteShort(8585);                   // channel port
        router.Dispatch(new InPacket(pkt.ToArray()), session: null!);

        captured.Should().NotBeNull();
        captured!.Value.host.Should().Equal(127, 0, 0, 1);
        captured.Value.port.Should().Be(8585);
    }
}
