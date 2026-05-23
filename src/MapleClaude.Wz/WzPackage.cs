using System.IO.MemoryMappedFiles;
using System.Text;

namespace MapleClaude.Wz;

/// <summary>
/// A memory-mapped WZ file. Holds the cipher used to decrypt strings, the
/// version hash used by the offset descrambler, and a lazy root
/// <see cref="WzDirectory"/>. Dispose to release the mapping.
/// </summary>
public sealed class WzPackage : IDisposable
{
    private const int Pkg1Magic = 0x31474B50;

    private readonly MemoryMappedFile _mmap;
    private readonly MemoryMappedViewAccessor _view;
    private readonly WzCrypto _crypto;

    private WzDirectory? _root;

    public int Start { get; }
    public int VersionHash { get; }

    internal MemoryMappedViewAccessor View => _view;
    internal WzCrypto Crypto => _crypto;

    private WzPackage(MemoryMappedFile mmap, MemoryMappedViewAccessor view, WzCrypto crypto, int start, int versionHash)
    {
        _mmap = mmap;
        _view = view;
        _crypto = crypto;
        Start = start;
        VersionHash = versionHash;
    }

    public WzDirectory Root => _root ??= new WzDirectory(this, Start + 2);

    public object? GetItem(string path) => Root.GetItem(path);

    internal WzBuffer GetBuffer(long offset) => new(_view, offset);

    /// <summary>Opens a WZ file from disk and validates its header against the v95 GMS version hash.</summary>
    public static WzPackage Open(string path, int gameVersion = 95)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("WZ file not found", path);
        }

        var mmap = MemoryMappedFile.CreateFromFile(
            path: path,
            mode: FileMode.Open,
            mapName: null,
            capacity: 0,
            access: MemoryMappedFileAccess.Read);

        MemoryMappedViewAccessor? view = null;
        try
        {
            view = mmap.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

            var probe = new WzBuffer(view);
            var magic = probe.ReadInt();
            if (magic != Pkg1Magic)
            {
                throw new WzReaderException($"PKG1 header missing at start of {path} (got 0x{magic:X8})");
            }

            // long size, int start
            _ = probe.ReadLong();
            var start = probe.ReadInt();

            // Validate version hash
            probe.Position = start;
            var versionHeader = (ushort)probe.ReadShort();
            var versionHash = ComputeVersionHash(gameVersion);
            var computedHeader = 0xFF
                                 ^ ((versionHash >> 24) & 0xFF)
                                 ^ ((versionHash >> 16) & 0xFF)
                                 ^ ((versionHash >> 8) & 0xFF)
                                 ^ (versionHash & 0xFF);
            if (versionHeader != computedHeader)
            {
                throw new WzReaderException(
                    $"Version hash mismatch for {path}: header 0x{versionHeader:X4}, expected 0x{computedHeader:X4} for v{gameVersion}");
            }

            return new WzPackage(mmap, view, WzCrypto.CreateGms(), start, versionHash);
        }
        catch
        {
            view?.Dispose();
            mmap.Dispose();
            throw;
        }
    }

    private static int ComputeVersionHash(int version)
    {
        var hash = 0;
        foreach (var c in Encoding.ASCII.GetBytes(version.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            hash = (hash * 32) + c + 1;
        }
        return hash;
    }

    public void Dispose()
    {
        _crypto.Dispose();
        _view.Dispose();
        _mmap.Dispose();
    }
}
