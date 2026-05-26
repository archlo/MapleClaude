using System.IO.Compression;

namespace MapleClaude.Wz;

/// <summary>
/// A WZ canvas: a 2D bitmap with optional nested property tree (children
/// describe origin, animation timing, etc.). The raw pixel data is
/// zlib-compressed and stored in one of several pixel formats;
/// <see cref="DecodeBgra"/> returns a BGRA32 byte buffer that can be uploaded
/// directly to a <c>SurfaceFormat.Color</c> MonoGame Texture2D.
/// </summary>
public sealed class WzCanvas
{
    private readonly WzImage _parent;
    private readonly long _offset;

    private bool _headerRead;
    private WzProperty? _property;
    private byte[]? _decodedBgra;
    private int _width;
    private int _height;
    private int _format;
    private byte _formatScale;
    private long _dataStartOffset;
    private int _dataLength;

    public int Width { get { EnsureDecodedHeader(); return _width; } }
    public int Height { get { EnsureDecodedHeader(); return _height; } }
    public int Format { get { EnsureDecodedHeader(); return _format; } }
    public int FormatScale { get { EnsureDecodedHeader(); return _formatScale; } }

    /// <summary>
    /// Nested property tree of this canvas. Typically contains <c>origin</c>
    /// (a <see cref="WzVector"/>), animation metadata (<c>delay</c>, <c>a0</c>,
    /// <c>a1</c>), and sometimes nested image frames.
    /// </summary>
    public WzProperty Property { get { EnsureDecodedHeader(); return _property!; } }

    internal WzCanvas(WzImage parent, long offset)
    {
        _parent = parent;
        _offset = offset;
    }

    /// <summary>
    /// Returns the decoded pixel bytes in <b>BGRA32</b> order (the native layout
    /// of MonoGame <c>SurfaceFormat.Color</c> on x86/x64 LE). One uint per pixel.
    /// </summary>
    public ReadOnlySpan<byte> DecodeBgra()
    {
        EnsureDecodedHeader();
        if (_decodedBgra != null)
        {
            return _decodedBgra;
        }

        var buf = _parent.GetBuffer(_dataStartOffset);
        var compressed = buf.ReadBytes(_dataLength);
        var raw = InflateCanvas(compressed, _parent.Crypto);
        _decodedBgra = ConvertToBgra(raw, _width, _height, _format, _formatScale);
        return _decodedBgra;
    }

    private void EnsureDecodedHeader()
    {
        if (_headerRead)
        {
            return;
        }

        var buf = _parent.GetBuffer(_offset);
        var crypto = _parent.Crypto;

        // Canvas wrapper layout:
        //   string-block  "Canvas"     (variable bytes)
        //   byte          reserved
        //   byte          hasProperty  (0 or 1)
        //   if hasProperty == 1:
        //     byte byte   reserved (2 bytes that Kinoko's WzProperty hack accidentally consumes via "skip 2 after readStringBlock". For canvas's children we need to skip them explicitly.)
        //     compressedInt  children-count
        //     children (per WzProperty's child-loop format)
        //   pixel header (width, height, format, formatScale, ...)

        _ = WzReader.ReadStringBlock(_parent, buf, crypto);
        buf.Position++;                              // reserved
        var hasProperty = buf.ReadByte() == 1;

        var items = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (hasProperty)
        {
            buf.Position += 2;                       // canvas-specific 2-byte reserved
            ReadChildren(_parent, buf, crypto, items);
        }
        _property = new WzProperty(_parent, _offset, items);

        // Pixel header.
        _width = WzReader.ReadCompressedInt(buf);
        _height = WzReader.ReadCompressedInt(buf);
        _format = WzReader.ReadCompressedInt(buf);
        _formatScale = buf.ReadByte();
        _ = buf.ReadInt();             // reserved (often 0)
        _dataLength = buf.ReadInt() - 1;
        buf.Position++;                // leading 0 byte of payload
        _dataStartOffset = buf.Position;

        _headerRead = true;
    }

    /// <summary>
    /// Reads the children entries of a WZ property list (the same per-entry
    /// loop format used by both top-level images and nested canvases).
    /// </summary>
    internal static void ReadChildren(WzImage parent, WzBuffer buf, WzCrypto crypto, Dictionary<string, object?> items)
    {
        var size = WzReader.ReadCompressedInt(buf);
        for (var i = 0; i < size; i++)
        {
            var itemName = WzReader.ReadStringBlock(parent, buf, crypto);
            var itemType = buf.ReadByte();
            items[itemName] = ReadChildValue(parent, buf, crypto, itemType);
        }
    }

    private static object? ReadChildValue(WzImage parent, WzBuffer buf, WzCrypto crypto, byte itemType)
    {
        switch (itemType)
        {
            case 0:
                return null;
            case 2:
            case 18:
                return buf.ReadShort();
            case 3:
            case 19:
                return WzReader.ReadCompressedInt(buf);
            case 20:
            {
                var first = buf.ReadSByte();
                return first == sbyte.MinValue ? buf.ReadLong() : (long)first;
            }
            case 4:
            {
                var floatType = buf.ReadByte();
                return floatType switch
                {
                    0x00 => 0f,
                    0x80 => buf.ReadFloat(),
                    _ => throw new WzReaderException($"Unknown float type: 0x{floatType:X2}"),
                };
            }
            case 5:
                return buf.ReadDouble();
            case 8:
                return WzReader.ReadStringBlock(parent, buf, crypto);
            case 9:
            {
                var subSize = buf.ReadInt();
                var subOffset = buf.Position;
                var value = ReadExtendedProperty(parent, buf, crypto, subOffset);
                buf.Position = subOffset + subSize;
                return value;
            }
            default:
                throw new WzReaderException($"Unknown property item type: {itemType}");
        }
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
                return new WzProperty(parent, offset, new Dictionary<string, object?>(StringComparer.Ordinal));
            default:
                throw new WzReaderException($"Unhandled extended-property UOL: {uol}");
        }
    }

    // GMS v95 canvas pixel data is either a plain zlib stream (starts with a 0x78
    // header — what most UI canvases use) or the chunked/encrypted form: a sequence of
    // [int blockLen][blockLen bytes XOR'd with the WZ key] concatenated into the zlib
    // stream. Mob (and various other) canvases use the chunked form; decoding only the
    // plain form is what threw "unsupported compression method".
    private static byte[] InflateCanvas(byte[] compressed, WzCrypto crypto)
    {
        if (HasZlibHeader(compressed))
        {
            return ZlibInflate(compressed);
        }
        var dechunked = Dechunk(compressed, crypto);
        try
        {
            return ZlibInflate(dechunked);
        }
        catch (InvalidDataException)
        {
            return RawInflate(dechunked); // dechunked payload was raw DEFLATE (no zlib header)
        }
    }

    private static bool HasZlibHeader(byte[] d) =>
        d.Length >= 2 && d[0] == 0x78 && d[1] is 0x01 or 0x5E or 0x9C or 0xDA;

    private static byte[] Dechunk(byte[] data, WzCrypto crypto)
    {
        using var dst = new MemoryStream(data.Length);
        var pos = 0;
        while (pos + 4 <= data.Length)
        {
            var blockLen = BitConverter.ToInt32(data, pos);
            pos += 4;
            if (blockLen <= 0 || pos + blockLen > data.Length)
            {
                break;
            }
            var block = data.AsSpan(pos, blockLen).ToArray();
            crypto.XorKeyStream(block);
            dst.Write(block, 0, blockLen);
            pos += blockLen;
        }
        return dst.ToArray();
    }

    private static byte[] ZlibInflate(byte[] compressed)
    {
        using var src = new MemoryStream(compressed);
        using var inflater = new ZLibStream(src, CompressionMode.Decompress);
        using var dst = new MemoryStream();
        inflater.CopyTo(dst);
        return dst.ToArray();
    }

    private static byte[] RawInflate(byte[] compressed)
    {
        using var src = new MemoryStream(compressed);
        using var inflater = new DeflateStream(src, CompressionMode.Decompress);
        using var dst = new MemoryStream();
        inflater.CopyTo(dst);
        return dst.ToArray();
    }

    private static byte[] ConvertToBgra(byte[] raw, int width, int height, int format, byte formatScale)
    {
        var scale = formatScale == 0 ? 1 : 1 << formatScale;
        var sw = Math.Max(1, width / scale);
        var sh = Math.Max(1, height / scale);

        var dst = new byte[width * height * 4];

        // Output byte order matches MonoGame SurfaceFormat.Color
        // (packed uint 0xAABBGGRR → byte 0 = R, byte 1 = G, byte 2 = B, byte 3 = A).
        // WZ stores pixels in BGRA byte order on disk; this method converts.
        switch (format)
        {
            case 1:
                // A4R4G4B4 — packed ushort 0xARGB.
                ExpandFromScaled(width, height, sw, sh, scale, dst, (sx, sy, dstOffset) =>
                {
                    var src = (sy * sw + sx) * 2;
                    if (src + 1 >= raw.Length)
                    {
                        return;
                    }
                    var low = raw[src];
                    var high = raw[src + 1];
                    var b = (byte)((low & 0x0F) * 0x11);
                    var g = (byte)((low >> 4) * 0x11);
                    var r = (byte)((high & 0x0F) * 0x11);
                    var a = (byte)((high >> 4) * 0x11);
                    dst[dstOffset + 0] = r;
                    dst[dstOffset + 1] = g;
                    dst[dstOffset + 2] = b;
                    dst[dstOffset + 3] = a;
                });
                return dst;

            case 2:
                // A8R8G8B8 (DirectX D3DFMT) — on disk byte order is BGRA;
                // swap R and B to land in MonoGame Color order.
                ExpandFromScaled(width, height, sw, sh, scale, dst, (sx, sy, dstOffset) =>
                {
                    var src = (sy * sw + sx) * 4;
                    if (src + 3 >= raw.Length)
                    {
                        return;
                    }
                    dst[dstOffset + 0] = raw[src + 2]; // R
                    dst[dstOffset + 1] = raw[src + 1]; // G
                    dst[dstOffset + 2] = raw[src + 0]; // B
                    dst[dstOffset + 3] = raw[src + 3]; // A
                });
                return dst;

            case 513:
                // R5G6B5 — packed ushort with high 5 bits R, mid 6 bits G, low 5 bits B; opaque.
                ExpandFromScaled(width, height, sw, sh, scale, dst, (sx, sy, dstOffset) =>
                {
                    var src = (sy * sw + sx) * 2;
                    if (src + 1 >= raw.Length)
                    {
                        return;
                    }
                    var lo = raw[src];
                    var hi = raw[src + 1];
                    var pixel = (ushort)(lo | (hi << 8));
                    var r = (byte)(((pixel >> 11) & 0x1F) * 255 / 31);
                    var g = (byte)(((pixel >> 5) & 0x3F) * 255 / 63);
                    var b = (byte)((pixel & 0x1F) * 255 / 31);
                    dst[dstOffset + 0] = r;
                    dst[dstOffset + 1] = g;
                    dst[dstOffset + 2] = b;
                    dst[dstOffset + 3] = 0xFF;
                });
                return dst;

            default:
                throw new NotSupportedException(
                    $"WzCanvas format {format} (scale {formatScale}) is not implemented yet");
        }
    }

    private static void ExpandFromScaled(
        int width, int height, int sw, int sh, int scale,
        byte[] dst, Action<int, int, int> writePixel)
    {
        for (var y = 0; y < height; y++)
        {
            var sy = Math.Min(y / scale, sh - 1);
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Min(x / scale, sw - 1);
                var dstOffset = (y * width + x) * 4;
                writePixel(sx, sy, dstOffset);
            }
        }
    }

    // Diagnostic helpers — only used by Wz.Tests to introspect raw bytes.
    internal byte DebugReadByte(long offset)
    {
        var buf = _parent.GetBuffer(offset);
        return buf.ReadByte();
    }

    internal long DebugOffset => _offset;
}
