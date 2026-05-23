using FluentAssertions;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Phase 18 login-polish wire tests: the DeleteCharacter(24) encoder.
/// Field order mirrors upstream Kinoko handleDeleteCharacter:
/// <c>string secondaryPassword, int characterId</c>.
/// </summary>
public class LoginSenderTests
{
    [Fact]
    public void DeleteCharacter_HasCanonicalOpcode() =>
        ((int)InHeader.DeleteCharacter).Should().Be(24);

    [Fact]
    public void DeleteCharacter_Encodes_SpwThenCharId()
    {
        var p = new InPacket(LoginSender.DeleteCharacter(101, "hunter2").ToArray());
        p.ReadShort().Should().Be((short)InHeader.DeleteCharacter);
        p.ReadString().Should().Be("hunter2");   // secondary password first
        p.ReadInt().Should().Be(101);            // then character id
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void DeleteCharacter_EmptySpw_StillEncodesCharId()
    {
        var p = new InPacket(LoginSender.DeleteCharacter(7, "").ToArray());
        p.ReadShort().Should().Be((short)InHeader.DeleteCharacter);
        p.ReadString().Should().Be("");
        p.ReadInt().Should().Be(7);
        p.Remaining.Should().Be(0);
    }
}
