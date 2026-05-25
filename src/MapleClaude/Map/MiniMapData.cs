using MapleClaude.Render;
using Microsoft.Xna.Framework;

namespace MapleClaude.Map;

/// <summary>
/// Decoded <c>...img/miniMap</c> node — the per-map rendered minimap bitmap plus the
/// coordinate metadata the client uses to plot markers on it.
///
/// The authentic v95 transform downscales a world coordinate into a canvas pixel by a
/// right-shift of <see cref="Mag"/> (divisor <c>2^Mag</c>), after adding the map's
/// <see cref="CenterX"/>/<see cref="CenterY"/> offset. Verified against the WZ:
/// <c>width &gt;&gt; mag == canvas.Width</c> (e.g. Henesys <c>6063 &gt;&gt; 4 == 378</c>).
/// </summary>
public sealed class MiniMapData
{
    /// <summary>The pre-rendered minimap image (top-down map art). Null on maps without one.</summary>
    public WzSprite? Canvas { get; init; }

    /// <summary>The region emblem shown at the minimap's top-left — <c>info/mapMark</c> resolved to
    /// <c>Map.wz/MapHelper.img/mark/&lt;name&gt;</c> (38×38). Null when the map has no mark.</summary>
    public WzSprite? Mark { get; init; }

    /// <summary>World-space span of the minimap, in world units (the un-shifted dimensions).</summary>
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Offset added to world coords before the shift so the map's left/top map to pixel 0.</summary>
    public int CenterX { get; init; }
    public int CenterY { get; init; }

    /// <summary>Right-shift amount (divisor is <c>2^Mag</c>). v95 maps are typically 4.</summary>
    public int Mag { get; init; } = 4;

    /// <summary>Canvas bitmap width in pixels (<see cref="Width"/> downscaled).</summary>
    public int CanvasWidth  => Canvas?.Width  ?? (Width  >> Mag);

    /// <summary>Canvas bitmap height in pixels (<see cref="Height"/> downscaled).</summary>
    public int CanvasHeight => Canvas?.Height ?? (Height >> Mag);

    /// <summary>Maps a world coordinate to a pixel inside the minimap canvas bitmap.</summary>
    public Point WorldToCanvas(Vector2 world)
        => new(((int)world.X + CenterX) >> Mag, ((int)world.Y + CenterY) >> Mag);
}
