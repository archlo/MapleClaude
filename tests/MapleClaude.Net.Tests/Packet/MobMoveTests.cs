using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Wire-format tests for the controlled-mob round trip: outbound <c>MobMove(227)</c>
/// (the controller's per-mob movement send) and inbound <c>MobCtrlAck(288)</c> (the
/// server's reply). The 227 body is read by Kinoko <c>MobHandler.handleMobMove</c>
/// field-for-field; a single misaligned field would desync the cipher chain so each
/// field is asserted in order here.
/// </summary>
public class MobMoveTests
{
    private static (FieldHandlers fh, PacketRouter router) NewHandlers()
    {
        var fh     = new FieldHandlers(NullLogger<FieldHandlers>.Instance);
        var router = new PacketRouter(NullLogger<PacketRouter>.Instance);
        fh.Register(router);
        return (fh, router);
    }

    // ── Opcode values ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InHeader.MobMove, 227)]
    [InlineData(InHeader.MobApplyCtrl, 228)]
    public void OutboundMobOpcodes_HaveCanonicalValues(InHeader op, int expected) =>
        ((int)op).Should().Be(expected);

    [Fact]
    public void MobCtrlAck_Has_CanonicalValue() =>
        ((int)OutHeader.MobCtrlAck).Should().Be(288);

    // ── MobMove(227) round trip ─────────────────────────────────────────────────

    [Fact]
    public void MobMove_Encodes_KinokoHandleMobMove_FieldOrder()
    {
        // A representative MovePath: one NORMAL element walking right at 60 px/s on
        // foothold 7. Matches what MobController emits for a wandering walker.
        var elements = new[]
        {
            new MoveElement
            {
                Attr = 0, X = 100, Y = 50, Vx = 60, Vy = 0, Fh = 7,
                MoveAction = (byte)0 /* MOVE */, Elapse = 100,
            },
        };
        var blob = MovePathEncoder.Encode(originX: 90, originY: 50, originVx: 60, originVy: 0, elements);

        var packet = GameSender.MobMove(
            mobId:        0x1234,
            mobCtrlSn:    5,
            action:       0 /* MOVE */,
            left:         false,
            movePathBlob: blob,
            chasing:      false);

        var p = new InPacket(packet.ToArray());
        p.ReadShort().Should().Be((short)InHeader.MobMove);   // opcode (227)
        // Header (mirrors MobHandler.handleMobMove decode order)
        p.ReadInt().Should().Be(0x1234);              // dwMobID
        p.ReadShort().Should().Be((short)5);          // nMobCtrlSN
        p.ReadByte().Should().Be((byte)0);            // actionMask
        p.ReadByte().Should().Be((byte)0);            // (action << 1) | left  (MOVE=0, right=0)
        p.ReadInt().Should().Be(0);                   // targetInfo
        p.ReadInt().Should().Be(0);                   // multiTargetForBall count
        p.ReadInt().Should().Be(0);                   // randTimeForAreaAttack count
        p.ReadByte().Should().Be((byte)0);            // bActive | 16*!cheatRand
        p.ReadInt().Should().Be(0);                   // HackedCode
        p.ReadInt().Should().Be(0);                   // ptTarget.x
        p.ReadInt().Should().Be(0);                   // ptTarget.y
        p.ReadInt().Should().Be(0);                   // dwHackedCodeCRC

        // MovePath (shared with UserMove)
        p.ReadShort().Should().Be((short)90);         // origin X
        p.ReadShort().Should().Be((short)50);         // origin Y
        p.ReadShort().Should().Be((short)60);         // origin VX
        p.ReadShort().Should().Be((short)0);          // origin VY
        p.ReadByte().Should().Be((byte)1);            // element count
        p.ReadByte().Should().Be((byte)0);            // attr (NORMAL)
        p.ReadShort().Should().Be((short)100);        // x
        p.ReadShort().Should().Be((short)50);         // y
        p.ReadShort().Should().Be((short)60);         // vx
        p.ReadShort().Should().Be((short)0);          // vy
        p.ReadShort().Should().Be((short)7);          // fh
        p.ReadShort().Should().Be((short)0);          // xOffset
        p.ReadShort().Should().Be((short)0);          // yOffset
        p.ReadByte().Should().Be((byte)0);            // bMoveAction
        p.ReadShort().Should().Be((short)100);        // tElapse

        // Tail (chase block — kinoko reads but doesn't validate)
        p.ReadByte().Should().Be((byte)0);            // bChasing
        p.ReadByte().Should().Be((byte)0);            // hasTarget
        p.ReadByte().Should().Be((byte)0);            // pvcActive.bChasing
        p.ReadByte().Should().Be((byte)0);            // pvcActive.bChasingHack
        p.ReadInt().Should().Be(0);                   // pvcActive.tChaseDuration

        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void MobMove_ActionAndDir_Packs_LeftBit_AndChasingFlag()
    {
        // Empty MovePath blob (no elements) - lets us focus on header bits.
        var blob = MovePathEncoder.Encode(0, 0, 0, 0, System.Array.Empty<MoveElement>());

        var p = new InPacket(GameSender.MobMove(
            mobId: 1, mobCtrlSn: 1,
            action: 39 /* CHASE */,
            left:   true,
            movePathBlob: blob,
            chasing: true).ToArray());

        p.ReadShort();                                              // opcode
        p.ReadInt();                                                // mobId
        p.ReadShort();                                              // sn
        p.ReadByte();                                               // actionMask
        p.ReadByte().Should().Be((byte)((39 << 1) | 1));            // (CHASE<<1) | left

        // Skip the unvalidated middle (targetInfo, multi/rand counts, bActive, hackedCode,
        // ptTarget x/y, hackedCodeCRC) = 4+4+4+1+4+4+4+4 = 29 bytes.
        for (var i = 0; i < 29; i++) p.ReadByte();

        // MovePath origin (4 shorts) + count (1 byte) - all zero.
        p.ReadShort().Should().Be((short)0);
        p.ReadShort().Should().Be((short)0);
        p.ReadShort().Should().Be((short)0);
        p.ReadShort().Should().Be((short)0);
        p.ReadByte().Should().Be((byte)0);                          // element count

        // Tail starts here.
        p.ReadByte().Should().Be((byte)1);                          // bChasing = true
        p.ReadByte().Should().Be((byte)0);                          // hasTarget
        p.ReadByte().Should().Be((byte)0);                          // pvcChasing
        p.ReadByte().Should().Be((byte)0);                          // pvcChasingHack
        p.ReadInt().Should().Be(0);                                 // tChaseDuration
        p.Remaining.Should().Be(0);
    }

    // ── MobCtrlAck(288) decode ──────────────────────────────────────────────────

    [Fact]
    public void MobCtrlAck_Decode_RoundTrips()
    {
        var (fh, router) = NewHandlers();
        MobCtrlAckArgs? captured = null;
        fh.OnMobCtrlAck += a => captured = a;

        var pkt = OutPacket.Of((short)OutHeader.MobCtrlAck);
        pkt.WriteInt(0xABCD);    // mobId
        pkt.WriteShort(42);      // mobCtrlSn
        pkt.WriteByte(1);        // nextAttackPossible
        pkt.WriteShort(500);     // mp
        pkt.WriteByte(7);        // nSkillCommand
        pkt.WriteByte(3);        // nSLV
        router.Dispatch(new InPacket(pkt.ToArray()), session: null!);

        captured.Should().NotBeNull();
        captured!.MobId.Should().Be(0xABCD);
        captured.MobCtrlSn.Should().Be((short)42);
        captured.NextAttackPossible.Should().BeTrue();
        captured.Mp.Should().Be((short)500);
        captured.NextSkillId.Should().Be((byte)7);
        captured.NextSkillLevel.Should().Be((byte)3);
    }

    // ── UserHit(52) round trip — "mob attacks back" ────────────────────────────

    [Fact]
    public void UserHit_Has_CanonicalValue() =>
        ((int)InHeader.UserHit).Should().Be(52);

    [Fact]
    public void UserHit_BodyAttack_Encodes_HitHandlerFieldOrder()
    {
        // The body-touch path (attackIndex = 0): mirrors HitHandler.handleUserHit's
        // attackIndex > -2 branch, with the no-knockback / no-reflect tail.
        var packet = GameSender.UserHit(
            attackIndex:    0,    // body attack (index 0 in MobAttack map)
            magicElemAttr:  0,
            damage:         15,
            templateId:     100000,   // green snail
            mobId:          0x12345678,
            dir:            1);

        var p = new InPacket(packet.ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserHit);   // 52
        p.ReadInt().Should().Be(0);                  // get_update_time()
        p.ReadByte().Should().Be((byte)0);           // nAttackIdx
        p.ReadByte().Should().Be((byte)0);           // nMagicElemAttr
        p.ReadInt().Should().Be(15);                 // nDamage
        p.ReadInt().Should().Be(100000);             // dwTemplateID
        p.ReadInt().Should().Be(0x12345678);  // MobID
        p.ReadByte().Should().Be((byte)1);           // nDir
        p.ReadByte().Should().Be((byte)0);           // nX = 0 (reflect)
        p.ReadByte().Should().Be((byte)0);           // bGuard
        p.ReadByte().Should().Be((byte)1);           // knockback (1 = no knockback)
        // Reflect block is conditional (knockback > 1 || reflect != 0) — not present here.
        p.ReadByte().Should().Be((byte)0);           // bStance
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void UserHit_Knockback_Encodes_TheKnockbackByte()
    {
        // knockback = 2 means "I took a knockback" — the server (HitHandler.handleHit)
        // broadcasts it via UserRemote.hit so other clients play our flinch animation.
        // We deliberately skip the reflect block in the basic-body-touch path; if the
        // server ever starts requiring it when knockback > 1, expand the encoder.
        var packet = GameSender.UserHit(
            attackIndex: 0, magicElemAttr: 0, damage: 10,
            templateId: 100000, mobId: 99, dir: 0, knockback: 2);
        var p = new InPacket(packet.ToArray());
        p.ReadShort();    // opcode
        p.ReadInt();      // update_time
        p.ReadByte();     // attackIndex
        p.ReadByte();     // magicElemAttr
        p.ReadInt();      // damage
        p.ReadInt();      // templateId
        p.ReadInt();      // mobId
        p.ReadByte();     // dir
        p.ReadByte();     // reflect
        p.ReadByte();     // guard
        p.ReadByte().Should().Be((byte)2);   // knockback = 2 (took a knockback)
        p.ReadByte();     // stance
        p.Remaining.Should().Be(0);
    }

    // ── MobHPIndicator(298) decode ──────────────────────────────────────────────

    [Fact]
    public void MobHPIndicator_Has_CanonicalValue() =>
        ((int)OutHeader.MobHPIndicator).Should().Be(298);

    [Fact]
    public void MobHPIndicator_Decode_RoundTrips()
    {
        var (fh, router) = NewHandlers();
        (int mobId, byte pct)? captured = null;
        fh.OnMobHpIndicator += (id, p) => captured = (id, p);

        // MobPacket.mobHpIndicator: int mobId, byte percentage.
        var pkt = OutPacket.Of((short)OutHeader.MobHPIndicator);
        pkt.WriteInt(0xCAFE);
        pkt.WriteByte(73);
        router.Dispatch(new InPacket(pkt.ToArray()), session: null!);

        captured.Should().NotBeNull();
        captured!.Value.mobId.Should().Be(0xCAFE);
        captured.Value.pct.Should().Be((byte)73);
    }
}