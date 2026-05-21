using FluentAssertions;
using MapleClaude.Net.Crypto;
using Xunit;

namespace MapleClaude.Net.Tests.Crypto;

public class PacketCipherTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(100)]
    [InlineData(1456)]
    [InlineData(2000)]
    public void BuildHeader_then_ParseHeader_round_trips(int payloadLen)
    {
        byte[] iv = [0x11, 0x22, 0x33, 0x44];
        Span<byte> header = stackalloc byte[PacketCipher.HeaderSize];

        PacketCipher.BuildHeader(payloadLen, iv, header);
        var (valid, parsedLen) = PacketCipher.ParseHeader(header, iv);

        valid.Should().BeTrue();
        parsedLen.Should().Be(payloadLen);
    }

    [Fact]
    public void ParseHeader_rejects_mismatched_iv()
    {
        byte[] iv = [0x11, 0x22, 0x33, 0x44];
        byte[] otherIv = [0x99, 0xAA, 0xBB, 0xCC];
        Span<byte> header = stackalloc byte[PacketCipher.HeaderSize];

        PacketCipher.BuildHeader(100, iv, header);
        var (valid, _) = PacketCipher.ParseHeader(header, otherIv);

        valid.Should().BeFalse(
            "the parse decodes the version from header XOR iv; mismatched IV means wrong version");
    }

    [Fact]
    public void ParseHeader_rejects_short_input()
    {
        byte[] iv = [0x11, 0x22, 0x33, 0x44];
        byte[] shortHeader = [1, 2, 3];

        var (valid, _) = PacketCipher.ParseHeader(shortHeader, iv);
        valid.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(100)]
    [InlineData(1500)]
    [InlineData(4096)]
    public void Encrypt_then_Decrypt_round_trips_with_synced_ivs(int length)
    {
        byte[] sendIv = [0xAB, 0xCD, 0xEF, 0x01];
        byte[] recvIv = (byte[])sendIv.Clone();
        var original = NewDeterministicBuffer(length, seed: length);
        var wire = (byte[])original.Clone();

        PacketCipher.EncryptBody(wire, sendIv);
        PacketCipher.DecryptBody(wire, recvIv);

        wire.Should().Equal(original,
            "Encrypt+Decrypt with paired IV state must restore the original bytes");
    }

    [Fact]
    public void EncryptBody_advances_iv()
    {
        byte[] iv = [0xAB, 0xCD, 0xEF, 0x01];
        var ivBefore = (byte[])iv.Clone();
        var data = NewDeterministicBuffer(50, seed: 7);

        PacketCipher.EncryptBody(data, iv);

        iv.Should().NotEqual(ivBefore,
            "EncryptBody must call InnoHash to rotate the IV in place");
    }

    [Fact]
    public void DecryptBody_advances_iv()
    {
        byte[] iv = [0xAB, 0xCD, 0xEF, 0x01];
        var ivBefore = (byte[])iv.Clone();
        var data = NewDeterministicBuffer(50, seed: 7);

        PacketCipher.DecryptBody(data, iv);

        iv.Should().NotEqual(ivBefore);
    }

    [Fact]
    public void Two_sequential_packets_keep_ivs_in_sync()
    {
        byte[] sendIv = [0xAB, 0xCD, 0xEF, 0x01];
        byte[] recvIv = (byte[])sendIv.Clone();
        var packet1 = NewDeterministicBuffer(100, seed: 1);
        var packet2 = NewDeterministicBuffer(200, seed: 2);

        var wire1 = (byte[])packet1.Clone();
        var wire2 = (byte[])packet2.Clone();

        PacketCipher.EncryptBody(wire1, sendIv);
        PacketCipher.EncryptBody(wire2, sendIv);

        PacketCipher.DecryptBody(wire1, recvIv);
        PacketCipher.DecryptBody(wire2, recvIv);

        wire1.Should().Equal(packet1, "first packet survives the round-trip");
        wire2.Should().Equal(packet2, "second packet survives — proves IV rotation stays in sync");
        sendIv.Should().Equal(recvIv, "IVs converge to the same state after equal traffic");
    }

    private static byte[] NewDeterministicBuffer(int length, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }
}
