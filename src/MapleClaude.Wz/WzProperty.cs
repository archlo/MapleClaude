namespace MapleClaude.Wz;

/// <summary>
/// A WZ property node: an ordered map from string keys to one of:
/// <see cref="short"/>, <see cref="int"/>, <see cref="long"/>, <see cref="float"/>,
/// <see cref="double"/>, <see cref="string"/>, nested <see cref="WzProperty"/>,
/// <see cref="WzCanvas"/>, <see cref="WzVector"/>, <see cref="WzUol"/>, or <c>null</c>.
/// </summary>
public sealed class WzProperty
{
    private readonly WzImage _parent;
    private readonly long _offset;
    private Dictionary<string, object?>? _items;
    private long _endPosition;
    private WzProperty? _parentProperty;

    /// <summary>The property that contains this one (null for the image-root property).
    /// Used to resolve <c>..</c> segments in <see cref="WzUol"/> targets.</summary>
    internal WzProperty? ParentProperty => _parentProperty;
    internal void SetParentProperty(WzProperty? parent) => _parentProperty = parent;

    internal WzProperty(WzImage parent, long offset)
    {
        _parent = parent;
        _offset = offset;
    }

    internal WzProperty(WzImage parent, long offset, Dictionary<string, object?> items)
    {
        _parent = parent;
        _offset = offset;
        _items = items;
        _endPosition = offset;
    }

    public IReadOnlyDictionary<string, object?> Items => _items ??= ReadItems();

    /// <summary>
    /// Buffer position one byte past the end of this property tree, after the
    /// children loop completes. Used by <see cref="WzCanvas"/> to find where
    /// the pixel header starts. Only meaningful after <see cref="Items"/> has
    /// been read.
    /// </summary>
    internal long EndPosition
    {
        get
        {
            _ = Items; // force read so _endPosition is populated
            return _endPosition;
        }
    }

    public object? Get(string key) => Items.TryGetValue(key, out var v) ? v : null;

    public T? GetOrDefault<T>(string key, T? defaultValue = default)
    {
        var v = Get(key);
        return v is T t ? t : defaultValue;
    }

    /// <summary>
    /// Walks a slash-separated path through nested properties.
    /// Returns <c>null</c> if any segment is missing.
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
            WzProperty p => p.GetItem(tail),
            WzCanvas c => c.Property.GetItem(tail),
            WzUol u => u.Resolve() switch
            {
                WzProperty p => p.GetItem(tail),
                WzCanvas c => c.Property.GetItem(tail),
                _ => null,
            },
            _ => null,
        };
    }

    private Dictionary<string, object?> ReadItems()
    {
        var buf = _parent.GetBuffer(_offset);
        var crypto = _parent.Crypto;

        // Skip the "Property" type marker + 2 reserved bytes.
        _ = WzReader.ReadStringBlock(_parent, buf, crypto);
        buf.Position += 2;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var size = WzReader.ReadCompressedInt(buf);

        for (var i = 0; i < size; i++)
        {
            var itemName = WzReader.ReadStringBlock(_parent, buf, crypto);
            var itemType = buf.ReadByte();

            switch (itemType)
            {
                case 0:
                    result[itemName] = null;
                    break;

                case 2:
                case 18:
                    result[itemName] = buf.ReadShort();
                    break;

                case 3:
                case 19:
                    result[itemName] = WzReader.ReadCompressedInt(buf);
                    break;

                case 20:
                {
                    var first = buf.ReadSByte();
                    result[itemName] = first == sbyte.MinValue ? buf.ReadLong() : (long)first;
                    break;
                }

                case 4:
                {
                    var floatType = buf.ReadByte();
                    result[itemName] = floatType switch
                    {
                        0x00 => 0f,
                        0x80 => buf.ReadFloat(),
                        _ => throw new WzReaderException($"Unknown float type: 0x{floatType:X2}"),
                    };
                    break;
                }

                case 5:
                    result[itemName] = buf.ReadDouble();
                    break;

                case 8:
                    result[itemName] = WzReader.ReadStringBlock(_parent, buf, crypto);
                    break;

                case 9:
                {
                    var subSize = buf.ReadInt();
                    var subOffset = buf.Position;
                    var child = ReadExtendedProperty(_parent, buf, crypto, subOffset);
                    // Record the parent so UOL targets can walk ".." up the tree.
                    switch (child)
                    {
                        case WzProperty p: p.SetParentProperty(this); break;
                        case WzUol u: u.ParentProperty = this; break;
                    }
                    result[itemName] = child;
                    buf.Position = subOffset + subSize;
                    break;
                }

                default:
                    throw new WzReaderException($"Unknown property item type: {itemType}");
            }
        }

        _endPosition = buf.Position;
        return result;
    }

    private static object ReadExtendedProperty(WzImage parent, WzBuffer buf, WzCrypto crypto, long offset)
    {
        var uol = WzReader.ReadStringBlock(parent, buf, crypto);
        switch (WzNodeTypes.FromUol(uol))
        {
            case WzNodeType.Property:
                return new WzProperty(parent, offset);

            case WzNodeType.Canvas:
                return new WzCanvas(parent, offset);

            case WzNodeType.Vector:
                var x = WzReader.ReadCompressedInt(buf);
                var y = WzReader.ReadCompressedInt(buf);
                return new WzVector(x, y);

            case WzNodeType.Uol:
                buf.Position++;
                var target = WzReader.ReadStringBlock(parent, buf, crypto);
                return new WzUol(parent, target);

            case WzNodeType.Sound:
                return new WzSound(parent, offset);

            case WzNodeType.Convex:
            case WzNodeType.PolyShape:
                // Phase 1 doesn't render shapes. Stash an empty placeholder so the
                // node parse doesn't throw; rendering lands on a later branch.
                return new WzProperty(parent, offset, new Dictionary<string, object?>(StringComparer.Ordinal));

            default:
                throw new WzReaderException($"Unhandled extended-property UOL: {uol}");
        }
    }
}
