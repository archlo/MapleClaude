using FluentAssertions;
using MapleClaude.Domain;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Phase 19 storage/trunk wire tests: the UserTrunkRequest(67) encoders and the
/// TrunkResult(368) decode. Field orders mirror upstream Kinoko TrunkDialog /
/// TrunkPacket / Trunk.encode.
/// </summary>
public class TrunkTests
{
    private static (FieldHandlers fh, PacketRouter router) NewHandlers()
    {
        var fh = new FieldHandlers(NullLogger<FieldHandlers>.Instance);
        var router = new PacketRouter(NullLogger<PacketRouter>.Instance);
        fh.Register(router);
        return (fh, router);
    }

    // ── Opcodes ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TrunkOpcodes_HaveCanonicalValues()
    {
        ((int)InHeader.UserTrunkRequest).Should().Be(67);
        ((int)OutHeader.TrunkResult).Should().Be(368);
    }

    // ── Encoders ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrunkWithdraw_Encodes_TypeInvTypePosition()
    {
        var p = new InPacket(GameSender.TrunkWithdraw(invType: 2, position: 3).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserTrunkRequest);
        p.ReadByte().Should().Be(4);   // GetItem
        p.ReadByte().Should().Be(2);   // invType
        p.ReadByte().Should().Be(3);   // position
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void TrunkDeposit_Encodes_PosItemIdQuantity()
    {
        var p = new InPacket(GameSender.TrunkDeposit(inventoryPos: 5, itemId: 2000000, quantity: 10).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserTrunkRequest);
        p.ReadByte().Should().Be(5);   // PutItem
        p.ReadShort().Should().Be(5);  // inventory position
        p.ReadInt().Should().Be(2000000);
        p.ReadShort().Should().Be(10);
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void TrunkSort_And_Close_Encode_TypeOnly()
    {
        var sort = new InPacket(GameSender.TrunkSort().ToArray());
        sort.ReadShort().Should().Be((short)InHeader.UserTrunkRequest);
        sort.ReadByte().Should().Be(6);
        sort.Remaining.Should().Be(0);

        var close = new InPacket(GameSender.TrunkClose().ToArray());
        close.ReadShort().Should().Be((short)InHeader.UserTrunkRequest);
        close.ReadByte().Should().Be(8);
        close.Remaining.Should().Be(0);
    }

    [Fact]
    public void TrunkMoney_Withdraw_IsPositive_Deposit_IsNegative()
    {
        var w = new InPacket(GameSender.TrunkWithdrawMoney(1000).ToArray());
        w.ReadShort().Should().Be((short)InHeader.UserTrunkRequest);
        w.ReadByte().Should().Be(7);
        w.ReadInt().Should().Be(1000);     // > 0 → trunk → inventory

        var d = new InPacket(GameSender.TrunkDepositMoney(1000).ToArray());
        d.ReadShort().Should().Be((short)InHeader.UserTrunkRequest);
        d.ReadByte().Should().Be(7);
        d.ReadInt().Should().Be(-1000);    // < 0 → inventory → trunk
    }

    // ── Decode ───────────────────────────────────────────────────────────────────

    private static void WriteBundle(OutPacket p, int itemId, short qty)
    {
        p.WriteByte((byte)InvItemType.Bundle);  // nType = 2
        p.WriteInt(itemId);
        p.WriteByte(0);                          // cash = false
        p.WriteLong(0);                          // dateExpire
        p.WriteShort(qty);                       // nNumber
        p.WriteString("");                       // sTitle
        p.WriteShort(0);                         // nAttribute
    }

    [Fact]
    public void TrunkResult_OpenTrunkDlg_DecodesMoneyAndItems()
    {
        var (fh, router) = NewHandlers();
        TrunkResultArgs? captured = null;
        fh.OnTrunkResult += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.TrunkResult);
        p.WriteByte(22);                 // OpenTrunkDlg
        p.WriteInt(9030000);             // dwNpcTemplateID
        p.WriteByte(20);                 // nSlotCount
        p.WriteLong(-1);                 // dbcharFlag = ALL
        p.WriteInt(12345);               // nMoney (ALL has MONEY)
        p.WriteByte(0);                  // EQUIP count
        p.WriteByte(2);                  // CONSUME count
        WriteBundle(p, 2000000, 100);
        WriteBundle(p, 2000001, 50);
        p.WriteByte(0);                  // INSTALL count
        p.WriteByte(1);                  // ETC count
        WriteBundle(p, 4000000, 3);
        p.WriteByte(0);                  // CASH count
        router.Dispatch(new InPacket(p.ToArray()), session: null!);

        captured.Should().NotBeNull();
        captured!.ResultType.Should().Be(22);
        captured.TemplateId.Should().Be(9030000);
        captured.SlotCount.Should().Be(20);
        captured.Money.Should().Be(12345);
        captured.Items.Should().HaveCount(3);

        captured.Items[0].InvType.Should().Be(2);          // CONSUME
        captured.Items[0].PositionInType.Should().Be(0);
        captured.Items[0].ItemId.Should().Be(2000000);
        captured.Items[0].Quantity.Should().Be(100);
        captured.Items[1].PositionInType.Should().Be(1);   // second consume
        captured.Items[1].ItemId.Should().Be(2000001);
        captured.Items[2].InvType.Should().Be(4);          // ETC
        captured.Items[2].PositionInType.Should().Be(0);
        captured.Items[2].ItemId.Should().Be(4000000);
    }

    [Fact]
    public void TrunkResult_MoneySuccess_DecodesMoneyOnly()
    {
        var (fh, router) = NewHandlers();
        TrunkResultArgs? captured = null;
        fh.OnTrunkResult += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.TrunkResult);
        p.WriteByte(19);                 // MoneySuccess
        p.WriteByte(20);                 // nSlotCount
        p.WriteLong(0x2);                // dbcharFlag = MONEY only
        p.WriteInt(777);                 // nMoney
        router.Dispatch(new InPacket(p.ToArray()), session: null!);

        captured.Should().NotBeNull();
        captured!.Money.Should().Be(777);
        captured.Items.Should().BeEmpty();
    }

    [Fact]
    public void TrunkResult_ServerMsg_DecodesMessage()
    {
        var (fh, router) = NewHandlers();
        TrunkResultArgs? captured = null;
        fh.OnTrunkResult += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.TrunkResult);
        p.WriteByte(24);                 // ServerMsg
        p.WriteByte(1);                  // hasMsg
        p.WriteString("Your storage is full.");
        router.Dispatch(new InPacket(p.ToArray()), session: null!);

        captured.Should().NotBeNull();
        captured!.Message.Should().Be("Your storage is full.");
    }
}
