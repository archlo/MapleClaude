using FluentAssertions;
using Xunit;

namespace MapleClaude.Wz.Tests;

public class WzCryptoTests
{
    [Fact]
    public void CryptAscii_round_trips_with_same_cipher_state()
    {
        using var crypto = WzCrypto.CreateGms();

        byte[] original = "MapleClaude"u8.ToArray();
        var working = (byte[])original.Clone();
        crypto.CryptAscii(working);
        working.Should().NotEqual(original, "encryption must change the bytes");

        // Same cipher reused — the mask state is identical, so XOR'ing again undoes it.
        crypto.CryptAscii(working);
        working.Should().Equal(original, "XOR with same mask twice is identity");
    }

    [Fact]
    public void CryptUnicode_round_trips_with_same_cipher_state()
    {
        using var crypto = WzCrypto.CreateGms();
        byte[] original = System.Text.Encoding.Unicode.GetBytes("Henesys");
        var working = (byte[])original.Clone();

        crypto.CryptUnicode(working);
        working.Should().NotEqual(original);
        crypto.CryptUnicode(working);
        working.Should().Equal(original);
    }

    [Fact]
    public void Empty_iv_cipher_is_identity()
    {
        using var crypto = WzCrypto.CreateEmpty();
        byte[] original = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var working = (byte[])original.Clone();
        crypto.CryptAscii(working);
        // The XOR mask is zero, but the rolling 0xAA counter still applies.
        // So output should differ from input.
        working.Should().NotEqual(original);
        // Round-trip still holds.
        crypto.CryptAscii(working);
        working.Should().Equal(original);
    }

    [Fact]
    public void Mask_grows_to_cover_larger_payloads()
    {
        using var crypto = WzCrypto.CreateGms();
        var huge = new byte[4096];
        crypto.CryptAscii(huge);
        // Then crypt-again undoes. Just verifying no out-of-range.
        crypto.CryptAscii(huge);
        huge.Should().AllSatisfy(b => b.Should().Be(0));
    }
}
