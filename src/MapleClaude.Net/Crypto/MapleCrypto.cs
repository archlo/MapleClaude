using System.Security.Cryptography;

namespace MapleClaude.Net.Crypto;

/// <summary>
/// "MapleCrypto" — AES-256 ECB (no padding) used as a keystream generator
/// against an expanded IV. The keystream block is the AES-encryption of a
/// 16-byte buffer formed by repeating the 4-byte IV four times. The
/// keystream is re-generated every 16 bytes of plaintext and XOR'd byte by
/// byte. The cipher is naturally symmetric — calling <see cref="Crypt"/>
/// twice with the same IV restores the original data.
///
/// Chunk sizes mirror upstream exactly: 0x5B0 (1456) bytes for the first
/// chunk, 0x5B4 (1460) bytes for every chunk after. The IV is NOT mutated
/// by this method; use <see cref="IgCipher.InnoHash"/> for that.
/// </summary>
public static class MapleCrypto
{
    /// <summary>AES block size for this cipher: 128 bits (16 bytes).</summary>
    public const int BlockSize = 16;

    private const int FirstChunkSize = 0x5B0;
    private const int SubsequentChunkSize = 0x5B4;

    private static readonly Aes s_aes = CreateAes();
    private static readonly Lock s_aesLock = new();

    private static Aes CreateAes()
    {
        var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.BlockSize = 128;
        aes.KeySize = 256;
        aes.Key = AesUserKey.ToArray();
        return aes;
    }

    /// <summary>
    /// Encrypts (or decrypts — the operation is symmetric) <paramref name="data"/>
    /// in place using <paramref name="iv"/> as the IV seed. <paramref name="iv"/>
    /// is not modified.
    /// </summary>
    /// <param name="data">The buffer to crypt; mutated in place.</param>
    /// <param name="iv">A 4-byte IV. Read-only here.</param>
    /// <exception cref="ArgumentException"><paramref name="iv"/> must be exactly 4 bytes.</exception>
    public static void Crypt(Span<byte> data, ReadOnlySpan<byte> iv)
    {
        if (iv.Length != 4)
        {
            throw new ArgumentException("iv must be exactly 4 bytes", nameof(iv));
        }
        if (data.Length == 0)
        {
            return;
        }

        Span<byte> block = stackalloc byte[BlockSize];
        var remaining = data.Length;
        var chunkLimit = FirstChunkSize;
        var offset = 0;

        while (remaining > 0)
        {
            FillBlock(block, iv);
            var chunkLen = remaining < chunkLimit ? remaining : chunkLimit;
            for (var i = offset; i < offset + chunkLen; i++)
            {
                var blockIndex = (i - offset) % BlockSize;
                if (blockIndex == 0)
                {
                    EncryptBlockInPlace(block);
                }
                data[i] ^= block[blockIndex];
            }
            offset += chunkLen;
            remaining -= chunkLen;
            chunkLimit = SubsequentChunkSize;
        }
    }

    private static void FillBlock(Span<byte> block, ReadOnlySpan<byte> iv)
    {
        // Repeat the 4-byte IV four times to fill a 16-byte block.
        for (var i = 0; i < block.Length; i += iv.Length)
        {
            iv.CopyTo(block[i..]);
        }
    }

    private static void EncryptBlockInPlace(Span<byte> block)
    {
        lock (s_aesLock)
        {
            s_aes.EncryptEcb(block, block, PaddingMode.None);
        }
    }
}
