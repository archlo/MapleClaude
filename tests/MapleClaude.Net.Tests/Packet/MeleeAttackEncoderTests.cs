using FluentAssertions;
using MapleClaude.Net.Packet;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

public class MeleeAttackEncoderTests
{
    [Fact]
    public void Encode_TwoMobs_OneHit_MatchesUpstreamReadOrder()
    {
        var targets = new[]
        {
            new MeleeTarget { MobId = 0x1001, HitX = 100, HitY = 200, Delay = 0, Damage = new[] { 47 } },
            new MeleeTarget { MobId = 0x1002, HitX = 150, HitY = 200, Delay = 0, Damage = new[] { 63 } },
        };
        var blob = MeleeAttackEncoder.Encode(
            fieldKey: 7, actionAndDir: unchecked((short)0x8000), attackSpeed: 6,
            userX: 90, userY: 200, targets, damagePerMob: 1);

        // Decode through the exact upstream AttackHandler.handlerUserMeleeAttack order.
        var r = new InPacket(blob);
        r.ReadShort().Should().Be((short)InHeader.UserMeleeAttack); // opcode prefix
        r.ReadByte().Should().Be(7);                    // fieldKey
        r.ReadInt().Should().Be(0);                     // dr0
        r.ReadInt().Should().Be(0);                     // dr1
        var mask = r.ReadByte();                        // damagePerMob | mobCount<<4
        (mask & 0xF).Should().Be(1);                    // damagePerMob
        ((mask >> 4) & 0xF).Should().Be(2);             // mobCount
        r.ReadInt().Should().Be(0);                     // dr2
        r.ReadInt().Should().Be(0);                     // dr3
        r.ReadInt().Should().Be(0);                     // skillId (basic)
        r.ReadByte().Should().Be(0);                    // combatOrders
        r.ReadInt().Should().Be(0);                     // dwKey
        r.ReadInt().Should().Be(0);                     // crc32
        r.ReadInt().Should().Be(0);                     // skill crc
        r.ReadInt().Should().Be(0);                     // skill crc
        r.ReadByte().Should().Be(0);                    // flag
        r.ReadShort().Should().Be(unchecked((short)0x8000)); // actionAndDir (bLeft set)
        r.ReadInt().Should().Be(0);                     // GETCRC32Svr
        r.ReadByte().Should().Be(0);                    // attackActionType
        r.ReadByte().Should().Be(6);                    // attackSpeed
        r.ReadInt().Should().Be(0);                     // tAttackTime
        r.ReadInt().Should().Be(0);                     // dwID

        // mob 0
        r.ReadInt().Should().Be(0x1001);
        r.ReadByte().Should().Be(0);                    // hitAction
        r.ReadByte().Should().Be(0);                    // foreAction|left
        r.ReadByte().Should().Be(0);                    // frameIdx
        r.ReadByte().Should().Be(0);                    // calcDamageStatIndex
        r.ReadShort().Should().Be(100);                 // hitX
        r.ReadShort().Should().Be(200);                 // hitY
        r.ReadShort().Should().Be(0);                   // pad
        r.ReadShort().Should().Be(0);                   // pad
        r.ReadShort().Should().Be(0);                   // delay
        r.ReadInt().Should().Be(47);                    // damage[0]
        r.ReadInt().Should().Be(0);                     // mob crc

        // mob 1
        r.ReadInt().Should().Be(0x1002);
        r.Skip(4);                                       // hitAction/foreAction/frameIdx/calcDmgIdx
        r.ReadShort().Should().Be(150);                 // hitX
        r.ReadShort().Should().Be(200);                 // hitY
        r.Skip(4);                                       // 2 pads
        r.ReadShort().Should().Be(0);                   // delay
        r.ReadInt().Should().Be(63);                    // damage[0]
        r.ReadInt().Should().Be(0);                     // mob crc

        r.ReadShort().Should().Be(90);                  // userX
        r.ReadShort().Should().Be(200);                 // userY
        r.Remaining.Should().Be(0);
    }

    [Fact]
    public void Encode_NoTargets_IsValidWhiff()
    {
        var blob = MeleeAttackEncoder.Encode(
            fieldKey: 3, actionAndDir: 0, attackSpeed: 6, userX: 10, userY: 20,
            targets: Array.Empty<MeleeTarget>());
        var r = new InPacket(blob);
        r.ReadShort();                                  // opcode
        r.ReadByte().Should().Be(3);                    // fieldKey
        r.Skip(8);                                       // dr0, dr1
        var mask = r.ReadByte();
        ((mask >> 4) & 0xF).Should().Be(0, "no mobs were hit");
    }

    [Fact]
    public void Encode_MultiHit_WritesAllDamageValues()
    {
        var targets = new[]
        {
            new MeleeTarget { MobId = 5, HitX = 0, HitY = 0, Delay = 0, Damage = new[] { 11, 22, 33 } },
        };
        var blob = MeleeAttackEncoder.Encode(0, 0, 6, 0, 0, targets, damagePerMob: 3);
        var r = new InPacket(blob);
        r.ReadShort();                                  // opcode
        r.ReadByte();                                   // fieldKey
        r.Skip(8);                                       // dr0/dr1
        (r.ReadByte() & 0xF).Should().Be(3, "damagePerMob = 3");
        // skip to first mob's damage: dr2,dr3(8) skillId(4) combat(1) dwKey(4) crc32(4) crc(4) crc(4) flag(1) actionDir(2) crc32svr(4) atkType(1) atkSpeed(1) tAtkTime(4) dwID(4)
        r.Skip(8 + 4 + 1 + 4 + 4 + 4 + 4 + 1 + 2 + 4 + 1 + 1 + 4 + 4);
        r.ReadInt().Should().Be(5);                     // mobId
        r.Skip(4 + 2 + 2 + 2 + 2 + 2);                   // hitAction..pads..delay
        r.ReadInt().Should().Be(11);
        r.ReadInt().Should().Be(22);
        r.ReadInt().Should().Be(33);
    }

    [Fact]
    public void Encode_DamageLengthMismatch_Throws()
    {
        var targets = new[]
        {
            new MeleeTarget { MobId = 5, Damage = new[] { 11, 22 } }, // 2 values
        };
        var act = () => MeleeAttackEncoder.Encode(0, 0, 6, 0, 0, targets, damagePerMob: 1);
        act.Should().Throw<ArgumentException>();
    }
}
