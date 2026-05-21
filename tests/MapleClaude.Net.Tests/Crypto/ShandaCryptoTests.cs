using FluentAssertions;
using MapleClaude.Net.Crypto;
using Xunit;

namespace MapleClaude.Net.Tests.Crypto;

public class ShandaCryptoTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(100)]
    [InlineData(1500)]
    [InlineData(4096)]
    public void Encrypt_then_Decrypt_round_trips(int length)
    {
        var original = NewDeterministicBuffer(length, seed: length);
        var working = (byte[])original.Clone();

        ShandaCrypto.Encrypt(working);
        ShandaCrypto.Decrypt(working);

        working.Should().Equal(original,
            "Decrypt is the inverse of Encrypt — bytes must round-trip");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(100)]
    public void Encrypt_changes_input_for_nontrivial_payloads(int length)
    {
        var original = NewDeterministicBuffer(length, seed: 42);
        var working = (byte[])original.Clone();

        ShandaCrypto.Encrypt(working);

        working.Should().NotEqual(original,
            "Shanda is not the identity transform for any non-empty input");
    }

    [Fact]
    public void Empty_buffer_is_a_noop()
    {
        var empty = Array.Empty<byte>();
        ShandaCrypto.Encrypt(empty);
        ShandaCrypto.Decrypt(empty);
        empty.Should().BeEmpty();
    }

    private static byte[] NewDeterministicBuffer(int length, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }
}
