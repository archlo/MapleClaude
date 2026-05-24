using Microsoft.Xna.Framework;

namespace MapleClaude.Map;

/// <summary>
/// Smooth-follow camera for the in-game map view.
/// Tracks a target world-space position, clamps to map bounds, and lerps
/// each frame so motion feels fluid.  MapToScreen converts world coordinates
/// to screen pixels for rendering.
/// </summary>
public sealed class GameCamera
{
    /// <summary>Current camera centre in world space (pixels).</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Target the camera is moving toward.</summary>
    public Vector2 Target { get; set; }

    /// <summary>Map boundary in world space. Camera clamps so the viewport never shows outside.</summary>
    public Rectangle MapBounds { get; set; } = new Rectangle(-10000, -10000, 20000, 20000);

    public int ViewWidth { get; set; } = 800;
    public int ViewHeight { get; set; } = 600;

    /// <summary>
    /// Follow speed (0-1). 1 = instant snap. 0.1 = slow drift.
    /// Applied per-second via exponential decay so frame-rate independence holds.
    /// </summary>
    public float FollowSpeed { get; set; } = 6f;

    public GameCamera(Vector2 startPosition)
    {
        Position = startPosition;
        Target = startPosition;
    }

    public void Update(float deltaTime)
    {
        var t = 1f - MathF.Pow(1f - Math.Clamp(FollowSpeed * deltaTime, 0f, 1f), 1f);
        Position = Vector2.Lerp(Position, Target, t);
        Clamp();
    }

    private void Clamp()
    {
        var hw = ViewWidth / 2f;
        var hh = ViewHeight / 2f;
        var minX = MapBounds.Left + hw;
        var maxX = MapBounds.Right - hw;
        var minY = MapBounds.Top + hh;
        var maxY = MapBounds.Bottom - hh;
        // When the map is smaller than the viewport on an axis, min > max — Math.Clamp would throw.
        // Centre the camera on the map midpoint there so the whole (small) map is shown, centred.
        Position = new Vector2(
            minX <= maxX ? Math.Clamp(Position.X, minX, maxX) : (MapBounds.Left + MapBounds.Right) / 2f,
            minY <= maxY ? Math.Clamp(Position.Y, minY, maxY) : (MapBounds.Top + MapBounds.Bottom) / 2f);
    }

    /// <summary>
    /// Converts a world-space position to a screen pixel position.
    /// Screen (0,0) is top-left; camera centre maps to screen centre.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var screenCenter = new Vector2(ViewWidth / 2f, ViewHeight / 2f);
        return worldPos - Position + screenCenter;
    }

    /// <summary>
    /// Converts screen pixel position back to world space.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var screenCenter = new Vector2(ViewWidth / 2f, ViewHeight / 2f);
        return screenPos - screenCenter + Position;
    }

    /// <summary>True if a world-space rectangle is potentially visible on screen (with margin).</summary>
    public bool IsVisible(Rectangle worldRect, int margin = 64)
    {
        var screen = WorldToScreen(new Vector2(worldRect.X, worldRect.Y));
        var screenR = new Rectangle((int)screen.X - margin, (int)screen.Y - margin,
            worldRect.Width + margin * 2, worldRect.Height + margin * 2);
        return screenR.Intersects(new Rectangle(0, 0, ViewWidth, ViewHeight));
    }
}
