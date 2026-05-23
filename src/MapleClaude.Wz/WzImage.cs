namespace MapleClaude.Wz;

/// <summary>
/// A WZ <c>.img</c> blob: a lazy property tree. The root is a
/// <see cref="WzProperty"/> rooted at <see cref="Offset"/>.
/// </summary>
public sealed class WzImage
{
    private readonly WzPackage _parent;
    private WzProperty? _root;

    public long Offset { get; }

    internal WzImage(WzPackage parent, long offset)
    {
        _parent = parent;
        Offset = offset;
    }

    internal WzPackage Package => _parent;
    internal WzCrypto Crypto => _parent.Crypto;
    internal WzBuffer GetBuffer(long offset) => _parent.GetBuffer(offset);

    public WzProperty Root => _root ??= new WzProperty(this, Offset);

    public object? GetItem(string path) => Root.GetItem(path);
}
