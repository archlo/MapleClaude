using System.Text;

namespace MapleClaude.Net;

/// <summary>
/// Reads fields from an incoming server→client packet payload (after header stripping and decryption).
/// All multi-byte values are little-endian, matching the v95 protocol.
/// </summary>
public sealed class InPacket
{
    private readonly byte[] _data;
    private int _pos;

    public InPacket(byte[] data)
    {
        _data = data;
        _pos  = 0;
    }

    public InPacket(byte[] data, int startOffset)
    {
        _data = data;
        _pos  = startOffset;
    }

    public int Remaining => _data.Length - _pos;
    public bool HasMore   => _pos < _data.Length;

    public short Opcode => BitConverter.ToInt16(_data, 0);

    public byte DecodeByte()
    {
        AssertRemaining(1);
        return _data[_pos++];
    }

    public bool DecodeBool() => DecodeByte() != 0;

    public short DecodeShort()
    {
        AssertRemaining(2);
        var v = BitConverter.ToInt16(_data, _pos);
        _pos += 2;
        return v;
    }

    public int DecodeInt()
    {
        AssertRemaining(4);
        var v = BitConverter.ToInt32(_data, _pos);
        _pos += 4;
        return v;
    }

    public long DecodeLong()
    {
        AssertRemaining(8);
        var v = BitConverter.ToInt64(_data, _pos);
        _pos += 8;
        return v;
    }

    public string DecodeString()
    {
        var len = DecodeShort();
        AssertRemaining(len);
        var s = Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        return s;
    }

    public byte[] DecodeArray(int length)
    {
        AssertRemaining(length);
        var result = new byte[length];
        Array.Copy(_data, _pos, result, 0, length);
        _pos += length;
        return result;
    }

    public void Skip(int count)
    {
        AssertRemaining(count);
        _pos += count;
    }

    private void AssertRemaining(int needed)
    {
        if (_pos + needed > _data.Length)
            throw new InvalidOperationException(
                $"InPacket underflow: need {needed} bytes at pos {_pos}, have {_data.Length - _pos}");
    }
}
