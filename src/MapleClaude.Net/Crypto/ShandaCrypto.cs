namespace MapleClaude.Net.Crypto;

/// <summary>
/// "Shanda" — a Nexon-custom byte-shuffle cipher with 3 forward + 3 backward
/// passes per encrypt and the mirror operation per decrypt. Stateless and
/// symmetric. Ported byte-for-byte from the upstream Kinoko
/// <c>ShandaCrypto.java</c>.
/// </summary>
public static class ShandaCrypto
{
    /// <summary>
    /// Encrypts <paramref name="data"/> in place. Safe to call with an empty span.
    /// </summary>
    public static void Encrypt(Span<byte> data)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var a = data.Length;
            byte b = 0;
            for (var j = 0; j < data.Length; j++)
            {
                unchecked
                {
                    b ^= (byte)(a + RotateLeft(data[j], 3));
                    data[j] = (byte)(0x47 - RotateRight(b, a));
                }
                a--;
            }
            a = data.Length;
            b = 0;
            for (var j = data.Length - 1; j >= 0; j--)
            {
                unchecked
                {
                    b ^= (byte)(a + RotateLeft(data[j], 4));
                    data[j] = RotateRight((byte)(b ^ 0x13), 3);
                }
                a--;
            }
        }
    }

    /// <summary>
    /// Decrypts <paramref name="data"/> in place. Safe to call with an empty span.
    /// </summary>
    public static void Decrypt(Span<byte> data)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var a = data.Length;
            byte b = 0;
            for (var j = data.Length - 1; j >= 0; j--)
            {
                byte c;
                unchecked
                {
                    c = (byte)(RotateLeft(data[j], 3) ^ 0x13);
                    data[j] = RotateRight((byte)((b ^ c) - a), 4);
                }
                b = c;
                a--;
            }
            a = data.Length;
            b = 0;
            for (var j = 0; j < data.Length; j++)
            {
                byte c;
                unchecked
                {
                    c = RotateLeft((byte)~(data[j] - 0x48), a);
                    data[j] = RotateRight((byte)((b ^ c) - a), 3);
                }
                b = c;
                a--;
            }
        }
    }

    private static byte RotateLeft(byte x, int y)
    {
        var tmp = (x & 0xFF) << (y % 8);
        return (byte)((tmp & 0xFF) | (tmp >> 8));
    }

    private static byte RotateRight(byte x, int y)
    {
        var tmp = ((x & 0xFF) << 8) >> (y % 8);
        return (byte)((tmp & 0xFF) | (tmp >> 8));
    }
}
