namespace MapleClaude.Wz;

/// <summary>
/// A WZ sound node. Owns the audio payload as a byte array; consumers
/// (e.g. <c>WzAudioPlayer</c>) decide how to decode / play. v95 BGM is
/// typically MP3-encoded with a small WAVEFORMATEX-style header prefix.
///
/// Format (decoded by <see cref="WzCanvas.ReadChildren"/> when it sees the
/// "Sound_DX8" UOL tag):
/// <list type="bullet">
///   <item>The string-block tag is already consumed before this node loads.</item>
///   <item>byte (reserved, usually 0)</item>
///   <item>CompressedInt sound-data length (in bytes)</item>
///   <item>CompressedInt duration in milliseconds</item>
///   <item>byte[82] header (GUID + WAVEFORMATEX + extra bytes — implementation-defined)</item>
///   <item>byte[data-length] audio payload (MP3 for v95 GMS BGM)</item>
/// </list>
/// </summary>
public sealed class WzSound
{
    private readonly WzImage _parent;
    private readonly long _offset;
    private byte[]? _audioBytes;
    private int _durationMs;

    internal WzSound(WzImage parent, long offset)
    {
        _parent = parent;
        _offset = offset;
    }

    /// <summary>Length of the audio in milliseconds.</summary>
    public int DurationMs { get { EnsureLoaded(); return _durationMs; } }

    /// <summary>Raw audio payload bytes (typically MP3 for v95 GMS BGM).</summary>
    public ReadOnlySpan<byte> AudioBytes { get { EnsureLoaded(); return _audioBytes!; } }

    private void EnsureLoaded()
    {
        if (_audioBytes != null)
        {
            return;
        }

        var buf = _parent.GetBuffer(_offset);
        var crypto = _parent.Crypto;

        // Sound type-tag string ("Sound_DX8") was already read by the parent
        // property reader. We start at _offset with one reserved byte + lengths.
        _ = WzReader.ReadStringBlock(_parent, buf, crypto);
        buf.Position++;                              // reserved
        var dataLength = WzReader.ReadCompressedInt(buf);
        _durationMs = WzReader.ReadCompressedInt(buf);
        // Skip the WAVEFORMATEX-style header (82 bytes is the typical fixed prefix
        // observed in v95 GMS sounds before the MP3 payload).
        buf.Position += 82;
        _audioBytes = buf.ReadBytes(dataLength);
    }
}
