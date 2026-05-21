using MapleClaude.Wz;

namespace MapleClaude.Map;

/// <summary>
/// One entry in a map's <c>back/N</c> list. Resolves to a sprite in
/// <c>Map.wz/Back/&lt;Bs&gt;.img/{back|ani}/&lt;No&gt;</c> at position
/// <c>(X, Y)</c>, with parallax factors <see cref="Rx"/>/<see cref="Ry"/>,
/// tile periods <see cref="Cx"/>/<see cref="Cy"/>, and a tile/scroll
/// <see cref="Type"/>.
/// </summary>
public sealed class BackInfo
{
    public string Bs { get; init; } = string.Empty;
    public int No { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Rx { get; init; }
    public int Ry { get; init; }
    public int Cx { get; init; }
    public int Cy { get; init; }
    public BackType Type { get; init; }
    public bool Front { get; init; }
    public bool Animated { get; init; }
    public byte Alpha { get; init; }
    public bool Flip { get; init; }

    public static BackInfo From(WzProperty entry)
    {
        return new BackInfo
        {
            Bs = entry.GetOrDefault<string>("bS") ?? string.Empty,
            No = ReadInt(entry, "no"),
            X = ReadInt(entry, "x"),
            Y = ReadInt(entry, "y"),
            Rx = ReadInt(entry, "rx"),
            Ry = ReadInt(entry, "ry"),
            Cx = ReadInt(entry, "cx"),
            Cy = ReadInt(entry, "cy"),
            Type = (BackType)ReadInt(entry, "type"),
            Front = ReadInt(entry, "front") != 0,
            Animated = ReadInt(entry, "ani") != 0,
            Alpha = (byte)Math.Clamp(ReadInt(entry, "a", 255), 0, 255),
            Flip = ReadInt(entry, "f") != 0,
        };
    }

    private static int ReadInt(WzProperty p, string key, int fallback = 0)
    {
        return p.Get(key) switch
        {
            int i => i,
            short s => s,
            long l => (int)l,
            null => fallback,
            _ => fallback,
        };
    }
}
