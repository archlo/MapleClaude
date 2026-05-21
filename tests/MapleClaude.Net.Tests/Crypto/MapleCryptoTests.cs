using FluentAssertions;
using MapleClaude.Net.Crypto;
using Xunit;

namespace MapleClaude.Net.Tests.Crypto;

public class MapleCryptoTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(100)]
    [InlineData(1455)]   // just below first chunk boundary
    [InlineData(1456)]   // exactly first chunk
    [InlineData(1457)]   // just over first chunk
    [InlineData(2916)]   // first chunk + one full subsequent chunk
    [InlineData(4096)]
    public void Crypt_is_symmetric_with_same_iv(int length)
    {
        byte[] iv = [0xAB, 0xCD, 0xEF, 0x01];
        var original = NewDeterministicBuffer(length, seed: length);
        var working = (byte[])original.Clone();

        MapleCrypto.Crypt(working, iv);
        MapleCrypto.Crypt(working, iv);

        working.Should().Equal(original,
            "Crypt is symmetric: applying it twice with the same IV restores the original data");
    }

    [Fact]
    public void Crypt_does_not_mutate_iv()
    {
        byte[] iv = [0xAB, 0xCD, 0xEF, 0x01];
        var snapshot = (byte[])iv.Clone();
        var data = NewDeterministicBuffer(100, seed: 1);

        MapleCrypto.Crypt(data, iv);

        iv.Should().Equal(snapshot,
            "Crypt only XORs data with a keystream; IV mutation belongs to IgCipher.InnoHash");
    }

    [Fact]
    public void Crypt_empty_buffer_is_a_noop()
    {
        byte[] iv = [0xAB, 0xCD, 0xEF, 0x01];
        var empty = Array.Empty<byte>();
        MapleCrypto.Crypt(empty, iv);
        empty.Should().BeEmpty();
    }

    [Fact]
    public void Crypt_rejects_wrong_iv_length()
    {
        var data = new byte[100];
        byte[] tooShort = [1, 2, 3];
        byte[] tooLong = [1, 2, 3, 4, 5];
        Assert.Throws<ArgumentException>(() => MapleCrypto.Crypt(data, tooShort));
        Assert.Throws<ArgumentException>(() => MapleCrypto.Crypt(data, tooLong));
    }

    [Fact]
    public void Crypt_different_ivs_produce_different_ciphertext()
    {
        byte[] iv1 = [0xAB, 0xCD, 0xEF, 0x01];
        byte[] iv2 = [0x12, 0x34, 0x56, 0x78];
        var data1 = NewDeterministicBuffer(100, seed: 1);
        var data2 = (byte[])data1.Clone();

        MapleCrypto.Crypt(data1, iv1);
        MapleCrypto.Crypt(data2, iv2);

        data1.Should().NotEqual(data2,
            "different IVs must produce different keystreams");
    }

    [Fact]
    public void Crypt_changes_buffer_for_nontrivial_payload()
    {
        byte[] iv = [0xAB, 0xCD, 0xEF, 0x01];
        var original = NewDeterministicBuffer(100, seed: 1);
        var working = (byte[])original.Clone();

        MapleCrypto.Crypt(working, iv);

        working.Should().NotEqual(original);
    }

    private static byte[] NewDeterministicBuffer(int length, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }
}
