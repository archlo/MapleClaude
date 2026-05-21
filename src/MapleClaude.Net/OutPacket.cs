using System.Text;

namespace MapleClaude.Net;

/// <summary>
/// Builds a client→server packet payload.
/// All multi-byte values are little-endian, matching the v95 protocol.
/// After construction, call <see cref="GetBytes"/> to get the raw payload
/// (without the 4-byte framing header — <see cref="Crypto.PacketCipher"/> adds that).
/// </summary>
public sealed class OutPacket
{
    private readonly List<byte> _buf = new();

    public OutPacket(short opcode)
    {
        EncodeShort(opcode);
    }

    public OutPacket EncodeByte(byte v)  { _buf.Add(v); return this; }
    public OutPacket EncodeBool(bool v)  { _buf.Add(v ? (byte)1 : (byte)0); return this; }

    public OutPacket EncodeShort(short v)
    {
        _buf.Add((byte)(v & 0xFF));
        _buf.Add((byte)((v >> 8) & 0xFF));
        return this;
    }

    public OutPacket EncodeInt(int v)
    {
        _buf.Add((byte)(v & 0xFF));
        _buf.Add((byte)((v >> 8) & 0xFF));
        _buf.Add((byte)((v >> 16) & 0xFF));
        _buf.Add((byte)((v >> 24) & 0xFF));
        return this;
    }

    public OutPacket EncodeLong(long v)
    {
        for (var i = 0; i < 8; i++)
            _buf.Add((byte)((v >> (i * 8)) & 0xFF));
        return this;
    }

    public OutPacket EncodeString(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        EncodeShort((short)bytes.Length);
        _buf.AddRange(bytes);
        return this;
    }

    public OutPacket EncodeArray(ReadOnlySpan<byte> data)
    {
        _buf.AddRange(data.ToArray());
        return this;
    }

    public OutPacket Zero(int count)
    {
        for (var i = 0; i < count; i++) _buf.Add(0);
        return this;
    }

    public byte[] GetBytes() => _buf.ToArray();
    public int Length => _buf.Count;
}
