namespace MapleClaude.Wz;

/// <summary>
/// Thrown when the WZ reader encounters malformed data (bad magic, unknown
/// node type, corrupt offset, etc.). Equivalent to the upstream Kinoko
/// <c>WzReaderError</c>.
/// </summary>
public sealed class WzReaderException : Exception
{
    public WzReaderException(string message) : base(message) { }
    public WzReaderException(string message, Exception inner) : base(message, inner) { }
}
