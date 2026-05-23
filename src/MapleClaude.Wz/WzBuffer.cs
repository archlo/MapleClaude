using System.IO.MemoryMappedFiles;

namespace MapleClaude.Wz;

/// <summary>
/// Stateful cursor over a memory-mapped WZ file. Multiple buffers can read
/// from the same underlying view concurrently as long as their cursors are
/// independent. All multi-byte reads are little-endian (WZ files are LE).
/// </summary>
internal sealed class WzBuffer
{
    private readonly MemoryMappedViewAccessor _view;

    public long Position { get; set; }

    public long Length => _view.Capacity;

    public WzBuffer(MemoryMappedViewAccessor view, long initialPosition = 0)
    {
        _view = view;
        Position = initialPosition;
    }

    public byte ReadByte()
    {
        var v = _view.ReadByte(Position);
        Position++;
        return v;
    }

    public sbyte ReadSByte()
    {
        var v = _view.ReadSByte(Position);
        Position++;
        return v;
    }

    public short ReadShort()
    {
        var v = _view.ReadInt16(Position);
        Position += 2;
        return v;
    }

    public int ReadInt()
    {
        var v = _view.ReadInt32(Position);
        Position += 4;
        return v;
    }

    public long ReadLong()
    {
        var v = _view.ReadInt64(Position);
        Position += 8;
        return v;
    }

    public float ReadFloat()
    {
        var v = _view.ReadSingle(Position);
        Position += 4;
        return v;
    }

    public double ReadDouble()
    {
        var v = _view.ReadDouble(Position);
        Position += 8;
        return v;
    }

    public byte[] ReadBytes(int count)
    {
        var buf = new byte[count];
        if (count > 0)
        {
            var read = _view.ReadArray(Position, buf, 0, count);
            if (read != count)
            {
                throw new WzReaderException($"Short read: requested {count}, got {read}");
            }
            Position += count;
        }
        return buf;
    }
}
