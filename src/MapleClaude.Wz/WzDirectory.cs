namespace MapleClaude.Wz;

/// <summary>
/// A WZ directory: a lazy-loaded ordered map of names to either child
/// <see cref="WzDirectory"/> or <see cref="WzImage"/> entries. Offsets to
/// children are scrambled by the WZ offset descrambler that mixes in the
/// package's version hash and a magic constant.
/// </summary>
public sealed class WzDirectory
{
    private readonly WzPackage _parent;
    private readonly long _offset;
    private Dictionary<string, object>? _items;

    internal WzDirectory(WzPackage parent, long offset)
    {
        _parent = parent;
        _offset = offset;
    }

    public IReadOnlyDictionary<string, object> Items => _items ??= ReadDirectory();

    /// <summary>
    /// Walks a slash-separated path through this directory and any child
    /// directories or images. Returns <c>null</c> if the path doesn't resolve.
    /// </summary>
    public object? GetItem(string path)
    {
        var slash = path.IndexOf('/', StringComparison.Ordinal);
        var head = slash < 0 ? path : path[..slash];
        if (!Items.TryGetValue(head, out var child))
        {
            return null;
        }
        if (slash < 0)
        {
            return child;
        }
        var tail = path[(slash + 1)..];
        return child switch
        {
            WzDirectory dir => dir.GetItem(tail),
            WzImage img => img.GetItem(tail),
            _ => null,
        };
    }

    private Dictionary<string, object> ReadDirectory()
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var buf = _parent.GetBuffer(_offset);
        var crypto = _parent.Crypto;
        var size = WzReader.ReadCompressedInt(buf);

        for (var i = 0; i < size; i++)
        {
            string itemName;
            var itemType = buf.ReadByte();

            switch (itemType)
            {
                case 1:
                    // Unknown/skip entry: 01 XX 00 00 00 00 00 OFFSET
                    _ = buf.ReadInt();
                    _ = buf.ReadShort();
                    _ = ReadDescrambledOffset(buf);
                    continue;

                case 2:
                    // Name is stored at a string-table offset elsewhere in the file.
                    var stringOffset = buf.ReadInt();
                    var saved = buf.Position;
                    buf.Position = _parent.Start + stringOffset;
                    itemType = buf.ReadByte();
                    itemName = WzReader.ReadString(buf, crypto);
                    buf.Position = saved;
                    break;

                case 3:
                case 4:
                    itemName = WzReader.ReadString(buf, crypto);
                    break;

                default:
                    throw new WzReaderException($"Unknown directory item type: {itemType}");
            }

            _ = WzReader.ReadCompressedInt(buf);          // item size — unused for now
            _ = WzReader.ReadCompressedInt(buf);          // item checksum — unused for now
            var itemOffset = ReadDescrambledOffset(buf);

            if (itemType == 3)
            {
                result[itemName] = new WzDirectory(_parent, itemOffset);
            }
            else if (itemType == 4)
            {
                result[itemName] = new WzImage(_parent, itemOffset);
            }
        }

        return result;
    }

    /// <summary>
    /// The WZ offset descrambler. Mixes the current buffer position, the package
    /// start, the version hash, and the magic constant to produce the actual
    /// target offset. Lifted byte-for-byte from upstream Kinoko.
    /// </summary>
    private long ReadDescrambledOffset(WzBuffer buf)
    {
        var start = (uint)_parent.Start;
        var hash = (uint)_parent.VersionHash;
        unchecked
        {
            var result = (uint)buf.Position;
            result = ~(result - start);
            result *= hash;
            result -= WzConstants.WzOffsetConstant;
            var rotateBy = (int)(result & 0x1F);
            result = (result << rotateBy) | (result >> (32 - rotateBy));
            result ^= (uint)buf.ReadInt();
            result += start * 2;
            return result;
        }
    }
}
