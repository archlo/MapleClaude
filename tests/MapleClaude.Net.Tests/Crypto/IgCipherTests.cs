using FluentAssertions;
using MapleClaude.Net.Crypto;
using Xunit;

namespace MapleClaude.Net.Tests.Crypto;

public class IgCipherTests
{
    [Fact]
    public void InnoHash_rejects_non_four_byte_input()
    {
        byte[] tooShort = [0, 0, 0];
        byte[] tooLong = [0, 0, 0, 0, 0];
        Assert.Throws<ArgumentException>(() => IgCipher.InnoHash(tooShort));
        Assert.Throws<ArgumentException>(() => IgCipher.InnoHash(tooLong));
    }

    [Fact]
    public void InnoHash_mutates_iv_in_place()
    {
        byte[] iv = [0xF2, 0x53, 0x50, 0xC6];
        var snapshot = (byte[])iv.Clone();
        IgCipher.InnoHash(iv);
        iv.Should().NotEqual(snapshot,
            "InnoHash should rotate the IV to a new 4-byte state");
        iv.Should().HaveCount(4);
    }

    [Fact]
    public void InnoHash_is_deterministic_for_a_given_seed()
    {
        byte[] iv1 = [0x12, 0x34, 0x56, 0x78];
        byte[] iv2 = (byte[])iv1.Clone();

        IgCipher.InnoHash(iv1);
        IgCipher.InnoHash(iv2);

        iv1.Should().Equal(iv2, "same input must always produce same output");
    }

    [Fact]
    public void InnoHash_distinct_seeds_diverge()
    {
        byte[] iv1 = [0x00, 0x00, 0x00, 0x00];
        byte[] iv2 = [0xFF, 0xFF, 0xFF, 0xFF];

        IgCipher.InnoHash(iv1);
        IgCipher.InnoHash(iv2);

        iv1.Should().NotEqual(iv2, "different seeds should produce different next-state");
    }

    [Fact]
    public void Shuffle_table_has_256_distinct_byte_entries()
    {
        var shuffle = IgCipher.Shuffle.ToArray();
        shuffle.Length.Should().Be(256);
        shuffle.Distinct().Count().Should().Be(256,
            "the shuffle table is a permutation of 0..255");
        shuffle.Min().Should().Be(0);
        shuffle.Max().Should().Be(255);
    }

    [Fact]
    public void InnoHash_can_iterate_thousand_times_without_throwing()
    {
        byte[] iv = [0xF2, 0x53, 0x50, 0xC6];
        for (var i = 0; i < 1000; i++)
        {
            IgCipher.InnoHash(iv);
        }
        iv.Should().HaveCount(4);
    }
}
