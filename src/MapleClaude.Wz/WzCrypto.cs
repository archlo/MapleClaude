using System.Security.Cryptography;

namespace MapleClaude.Wz;

/// <summary>
/// WZ string XOR cipher. Generates a growable mask buffer by running
/// AES-256-CBC over zero-filled blocks with a per-region IV (GMS by default),
/// then XORs that mask plus a rolling per-byte counter against the WZ string
/// bytes. Port of upstream Kinoko <c>WzCrypto.java</c>.
/// </summary>
internal sealed class WzCrypto : IDisposable
{
    private const int BatchSize = 1024;

    private readonly Aes? _aes;
    private readonly object _lock = new();
    private byte[] _mask = [];

    /// <summary>Constructs the GMS cipher (the v95 default).</summary>
    public static WzCrypto CreateGms() => new(WzConstants.WzGmsIv);

    /// <summary>Constructs a no-op cipher (used for WZ files without string encryption).</summary>
    public static WzCrypto CreateEmpty() => new(WzConstants.WzEmptyIv);

    private WzCrypto(byte[] iv)
    {
        if (iv.Length != 4)
        {
            throw new ArgumentException("WZ IV must be exactly 4 bytes", nameof(iv));
        }

        if (iv.SequenceEqual(WzConstants.WzEmptyIv))
        {
            _aes = null;
            return;
        }

        // Compress the 128-byte expanded AES_USER_KEY into a 32-byte key for AES-256:
        // take the byte at indices 0, 16, 32, 48, 64, 80, 96, 112; leave the
        // remaining 24 bytes of the key as zero. Matches WzCrypto.java exactly.
        var trimmedKey = new byte[32];
        for (var i = 0; i < 128; i += 16)
        {
            trimmedKey[i / 4] = WzConstants.AesUserKey[i];
        }

        // Expand the 4-byte IV by repeating it 4× to fill 16 bytes.
        var expandedIv = new byte[16];
        for (var i = 0; i < expandedIv.Length; i += iv.Length)
        {
            Buffer.BlockCopy(iv, 0, expandedIv, i, iv.Length);
        }

        _aes = Aes.Create();
        _aes.Mode = CipherMode.CBC;
        _aes.Padding = PaddingMode.None;
        _aes.KeySize = 256;
        _aes.BlockSize = 128;
        _aes.Key = trimmedKey;
        _aes.IV = expandedIv;
    }

    /// <summary>
    /// XOR-decrypts an ASCII (1-byte-per-char) WZ string in place. Each byte
    /// is XORed with the cipher-mask byte at the same index and a rolling
    /// counter starting at <c>0xAA</c>.
    /// </summary>
    public void CryptAscii(Span<byte> data)
    {
        EnsureMaskSize(data.Length);
        byte mask = 0xAA;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(data[i] ^ _mask[i] ^ mask);
            unchecked { mask++; }
        }
    }

    /// <summary>
    /// XOR-decrypts a UTF-16LE WZ string in place. Each 2-byte char is XORed
    /// against the mask bytes plus a rolling 16-bit counter starting at
    /// <c>0xAAAA</c>.
    /// </summary>
    public void CryptUnicode(Span<byte> data)
    {
        EnsureMaskSize(data.Length);
        ushort mask = 0xAAAA;
        for (var i = 0; i < data.Length; i += 2)
        {
            data[i]     = (byte)(data[i]     ^ _mask[i]     ^ (mask & 0xFF));
            data[i + 1] = (byte)(data[i + 1] ^ _mask[i + 1] ^ (mask >> 8));
            unchecked { mask++; }
        }
    }

    /// <summary>
    /// XOR a canvas data block in place with the raw WZ keystream (the AES mask only —
    /// no rolling 0xAA counter, index reset to 0). Used by the chunked/encrypted canvas
    /// pixel format. A no-op when this is the empty cipher (mask stays all-zero).
    /// </summary>
    public void XorKeyStream(Span<byte> data)
    {
        EnsureMaskSize(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            data[i] ^= _mask[i];
        }
    }

    private void EnsureMaskSize(int size)
    {
        lock (_lock)
        {
            if (_mask.Length >= size)
            {
                return;
            }

            var newSize = ((size / BatchSize) + 1) * BatchSize;
            var newMask = new byte[newSize];

            if (_aes != null)
            {
                // CBC over zero input gives a deterministic keystream that
                // depends only on the (key, iv) pair. We use the encrypted
                // zeros as the XOR mask.
                using var encryptor = _aes.CreateEncryptor();
                encryptor.TransformBlock(newMask, 0, newSize, newMask, 0);
            }
            // If _aes is null (empty IV), the mask stays all-zero — XOR is a no-op.

            _mask = newMask;
        }
    }

    public void Dispose() => _aes?.Dispose();
}
