namespace MapleClaude.Wz;

/// <summary>WZ extended-property "type tag" string values.</summary>
internal enum WzNodeType
{
    Property,
    Canvas,
    Vector,
    Convex,
    PolyShape,
    Sound,
    Uol,
}

internal static class WzNodeTypes
{
    public static WzNodeType FromUol(string uol) => uol switch
    {
        "Property" => WzNodeType.Property,
        "Canvas" => WzNodeType.Canvas,
        "Shape2D#Vector2D" => WzNodeType.Vector,
        "Shape2D#Convex2D" => WzNodeType.Convex,
        "Shape2D#PolyShape2D" => WzNodeType.PolyShape,
        "Sound_DX8" => WzNodeType.Sound,
        "UOL" => WzNodeType.Uol,
        _ => throw new WzReaderException($"Unknown extended property UOL: {uol}"),
    };
}
