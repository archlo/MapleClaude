using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Phase 9 loot wire-format tests: the <c>Message(38)</c> popup subtypes
/// (IncEXP / IncMoney / DropPickUp) and the corrected <c>StatChanged(30)</c>
/// decode (4-byte mask, MONEY = 0x40000). Decoders run through
/// <see cref="PacketRouter"/> exactly as the live pipeline does.
/// </summary>
public class LootMessageTests
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

    // ── Opcode values ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OutHeader.Message, 38)]
    [InlineData(OutHeader.StatChanged, 30)]
    public void OutboundLootOpcodes_HaveCanonicalValues(OutHeader op, int expected) =>
        ((int)op).Should().Be(expected);

    [Fact]
    public void DropPickUpRequest_HasCanonicalValue() =>
        ((int)InHeader.DropPickUpRequest).Should().Be(246);

    // ── Message(38) — IncEXP / IncMoney ──────────────────────────────────────────

    [Fact]
    public void Message_IncExp_FiresOnIncExp()
    {
        var (fh, router) = NewHandlers();
        int? captured = null;
        fh.OnIncExp += e => captured = e;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(3);        // MessageType.IncEXP
        p.WriteByte(1);        // white
        p.WriteInt(1234);      // exp
        p.WriteByte(0);        // bOnQuest (decoder stops after exp, but realistic tail)
        Dispatch(router, p);

        captured.Should().Be(1234);
    }

    [Fact]
    public void Message_IncMoney_FiresOnIncMoney()
    {
        var (fh, router) = NewHandlers();
        int? captured = null;
        fh.OnIncMoney += m => captured = m;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(6);        // MessageType.IncMoney
        p.WriteInt(500);       // money
        Dispatch(router, p);

        captured.Should().Be(500);
    }

    // ── Message(38) — DropPickUp subtypes ────────────────────────────────────────

    [Fact]
    public void Message_DropPickUp_ItemBundle_FiresLootItem()
    {
        var (fh, router) = NewHandlers();
        LootMessageArgs? captured = null;
        fh.OnLootMessage += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(0);        // MessageType.DropPickUp
        p.WriteSByte(0);       // ITEM_BUNDLE
        p.WriteInt(2000000);   // itemId
        p.WriteInt(3);         // quantity
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.IsMoney.Should().BeFalse();
        captured.ItemId.Should().Be(2000000);
        captured.Quantity.Should().Be(3);
        captured.Warning.Should().Be(0);
    }

    [Fact]
    public void Message_DropPickUp_Money_FiresLootMoney()
    {
        var (fh, router) = NewHandlers();
        LootMessageArgs? captured = null;
        fh.OnLootMessage += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(0);        // MessageType.DropPickUp
        p.WriteSByte(1);       // MONEY
        p.WriteByte(0);        // portionNotFound
        p.WriteInt(750);       // money
        p.WriteShort(0);       // internet-cafe bonus
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.IsMoney.Should().BeTrue();
        captured.Money.Should().Be(750);
    }

    [Fact]
    public void Message_DropPickUp_Warning_FiresWithNegativeWarning()
    {
        var (fh, router) = NewHandlers();
        LootMessageArgs? captured = null;
        fh.OnLootMessage += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(0);        // MessageType.DropPickUp
        p.WriteSByte(-1);      // CANNOT_GET_ANYMORE_ITEMS — no body
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.Warning.Should().Be(-1);
        captured.IsMoney.Should().BeFalse();
        captured.ItemId.Should().Be(0);
    }

    // ── StatChanged(30) — regression for the long-mask / wrong-MONEY-bit bugs ─────

    [Fact]
    public void StatChanged_ExpAndMoney_DecodeCorrectly()
    {
        var (fh, router) = NewHandlers();
        StatChangedArgs? captured = null;
        fh.OnStatChanged += a => captured = a;

        const int mask = 0x10000 /*EXP*/ | 0x40000 /*MONEY*/;
        var p = OutPacket.Of((short)OutHeader.StatChanged);
        p.WriteByte(0);        // bExclRequestSent
        p.WriteInt(mask);      // 4-byte mask (was wrongly read as long before the fix)
        p.WriteInt(98765);     // EXP (ENCODE_ORDER: EXP before MONEY)
        p.WriteInt(54321);     // MONEY
        p.WriteByte(0);        // trailing
        p.WriteByte(0);        // trailing
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.Exp.Should().Be(98765);
        captured.Meso.Should().Be(54321);   // 0x40000, not 0x200000 (TEMPEXP)
    }

    [Fact]
    public void StatChanged_LevelAndExp_StayAligned()
    {
        var (fh, router) = NewHandlers();
        StatChangedArgs? captured = null;
        fh.OnStatChanged += a => captured = a;

        const int mask = 0x10 /*LEVEL*/ | 0x10000 /*EXP*/;
        var p = OutPacket.Of((short)OutHeader.StatChanged);
        p.WriteByte(0);        // bExclRequestSent
        p.WriteInt(mask);
        p.WriteByte(13);       // LEVEL (byte, ENCODE_ORDER before EXP)
        p.WriteInt(4242);      // EXP
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.Level.Should().Be(13);
        captured.Exp.Should().Be(4242);
    }

    // ── PickUpDrop encoder (C→S) ─────────────────────────────────────────────────

    [Fact]
    public void PickUpDrop_Encodes_FieldKeyPosDropId()
    {
        var p = new InPacket(GameSender.PickUpDrop(fieldKey: 7, x: 120, y: -45, dropId: 9001).ToArray());
        p.ReadShort().Should().Be((short)InHeader.DropPickUpRequest);
        p.ReadByte().Should().Be(7);     // fieldKey
        p.ReadInt().Should().Be(0);      // update_time
        p.ReadShort().Should().Be(120);  // x
        p.ReadShort().Should().Be(-45);  // y
        p.ReadInt().Should().Be(9001);   // dropId
        p.ReadInt().Should().Be(0);      // dwCliCrc
        p.Remaining.Should().Be(0);
    }
}
