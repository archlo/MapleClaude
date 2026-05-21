using MapleClaude.Wz;

namespace MapleClaude.Map;

/// <summary>
/// One entry in a map layer's <c>obj/N</c> list. Resolves to a sprite in
/// <c>Map.wz/Obj/&lt;Os&gt;.img/&lt;L0&gt;/&lt;L1&gt;/&lt;L2&gt;</c> at
/// position <c>(X, Y)</c> with z-order <see cref="Z"/>.
/// </summary>
public sealed class ObjInfo
{
    public string Os { get; init; } = string.Empty;
    public string L0 { get; init; } = string.Empty;
    public string L1 { get; init; } = string.Empty;
    public string L2 { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public bool Flip { get; init; }

    public static ObjInfo From(WzProperty entry)
    {
        return new ObjInfo
        {
            Os = entry.GetOrDefault<string>("oS") ?? string.Empty,
            L0 = entry.GetOrDefault<string>("l0") ?? string.Empty,
            L1 = entry.GetOrDefault<string>("l1") ?? string.Empty,
            L2 = entry.GetOrDefault<string>("l2") ?? string.Empty,
            X = ReadInt(entry, "x"),
            Y = ReadInt(entry, "y"),
            Z = ReadInt(entry, "z"),
            Flip = ReadInt(entry, "f") != 0,
        };
    }

    private static int ReadInt(WzProperty p, string key)
    {
        return p.Get(key) switch
        {
            int i => i,
            short s => s,
            long l => (int)l,
            _ => 0,
        };
    }
}
