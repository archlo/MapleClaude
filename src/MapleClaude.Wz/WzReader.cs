using System.Text;

namespace MapleClaude.Wz;

/// <summary>
/// Low-level WZ primitive readers. All multi-byte values are little-endian.
/// Ported from upstream Kinoko <c>WzReader.java</c>.
/// </summary>
internal static class WzReader
{
    /// <summary>
    /// Variable-length signed int. One byte first; if it's <c>sbyte.MinValue</c>
    /// (i.e. 0x80), the next 4 bytes are a regular little-endian int32.
    /// Otherwise the byte itself is the value (signed).
    /// </summary>
    public static int ReadCompressedInt(WzBuffer buffer)
    {
        var first = buffer.ReadSByte();
        return first == sbyte.MinValue ? buffer.ReadInt() : first;
    }

    /// <summary>
    /// Variable-length string with a sign-prefixed length:
    /// <list type="bullet">
    /// <item>Negative length → ASCII, take <c>-length</c> bytes (or read int32 if length == sbyte.MinValue).</item>
    /// <item>Positive length → UTF-16LE, take <c>length*2</c> bytes (or read int32 if length == sbyte.MaxValue).</item>
    /// <item>Zero length → empty string.</item>
    /// </list>
    /// Both encodings pass through <see cref="WzCrypto"/> for XOR decryption.
    /// </summary>
    public static string ReadString(WzBuffer buffer, WzCrypto crypto)
    {
        var length = (int)buffer.ReadSByte();
        if (length < 0)
        {
            if (length == sbyte.MinValue)
            {
                length = buffer.ReadInt();
            }
            else
            {
                length = -length;
            }
            if (length > 0)
            {
                var data = buffer.ReadBytes(length);
                crypto.CryptAscii(data);
                return Encoding.ASCII.GetString(data);
            }
        }
        else if (length > 0)
        {
            if (length == sbyte.MaxValue)
            {
                length = buffer.ReadInt();
            }
            if (length > 0)
            {
                var byteLen = length * 2;
                var data = buffer.ReadBytes(byteLen);
                crypto.CryptUnicode(data);
                return Encoding.Unicode.GetString(data);
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// String reader that handles WZ's intra-image deduplication. A leading
    /// type byte selects between an inline string (<c>0x00</c>, <c>0x73</c>)
    /// and a reference to a string at an offset within the parent image
    /// (<c>0x01</c>, <c>0x1B</c>). The buffer position is restored after a
    /// referenced read so the parent stream continues sequentially.
    /// </summary>
    public static string ReadStringBlock(WzImage parent, WzBuffer buffer, WzCrypto crypto)
    {
        var stringType = buffer.ReadByte();
        switch (stringType)
        {
            case 0x00:
            case 0x73:
                return ReadString(buffer, crypto);

            case 0x01:
            case 0x1B:
                var stringOffset = buffer.ReadInt();
                var saved = buffer.Position;
                buffer.Position = parent.Offset + stringOffset;
                var result = ReadString(buffer, crypto);
                buffer.Position = saved;
                return result;

            default:
                throw new WzReaderException($"Unknown string block type: 0x{stringType:X2}");
        }
    }
}
