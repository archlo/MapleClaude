namespace MapleClaude.Net.Crypto;

/// <summary>
/// The 32-byte expanded AES "user key" used by the MapleStory v95 packet
/// cipher. Byte-identical to the upstream Kinoko reference. The effective
/// entropy is only 8 bytes — the remaining 24 are interleaved zeros — but
/// the 32-byte form is what the .NET <see cref="System.Security.Cryptography.Aes"/>
/// (and Java's <c>SecretKeySpec</c>) consume, producing AES-256 key
/// scheduling that the server expects.
/// </summary>
internal static class AesUserKey
{
    private static readonly byte[] s_bytes =
    [
        0x13, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, 0xB4, 0x00, 0x00, 0x00,
        0x1B, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x00, 0x00,
        0x33, 0x00, 0x00, 0x00, 0x52, 0x00, 0x00, 0x00,
    ];

    /// <summary>Read-only view of the key.</summary>
    public static ReadOnlySpan<byte> Span => s_bytes;

    /// <summary>
    /// Returns a fresh copy of the key, suitable for assignment to
    /// <c>System.Security.Cryptography.Aes.Key</c> (which requires a
    /// mutable <see cref="byte"/>[]).
    /// </summary>
    public static byte[] ToArray() => (byte[])s_bytes.Clone();
}
