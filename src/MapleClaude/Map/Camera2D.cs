using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Map;

/// <summary>
/// World-space camera with a deadzone follow target and VR-bounds clamp.
/// Position is the world coordinate that should appear at the screen centre.
/// </summary>
public sealed class Camera2D
{
    public Vector2 Position { get; set; }
    public Vector2 DeadZone { get; set; } = new(32, 16);

    public void Follow(Vector2 target, FieldScene field, PresentationParameters pp)
    {
        var dx = target.X - Position.X;
        var dy = target.Y - Position.Y;
        if (Math.Abs(dx) > DeadZone.X)
        {
            Position = new Vector2(Position.X + dx - Math.Sign(dx) * DeadZone.X, Position.Y);
        }
        if (Math.Abs(dy) > DeadZone.Y)
        {
            Position = new Vector2(Position.X, Position.Y + dy - Math.Sign(dy) * DeadZone.Y);
        }

        // VR-bounds clamp.
        if (field.Info.HasVR)
        {
            var halfW = pp.BackBufferWidth / 2f;
            var halfH = pp.BackBufferHeight / 2f;
            var minX = field.Info.VRLeft + halfW;
            var maxX = field.Info.VRRight - halfW;
            var minY = field.Info.VRTop + halfH;
            var maxY = field.Info.VRBottom - halfH;
            // If the VR is smaller than the viewport on an axis, min > max — clamp would throw; centre instead.
            Position = new Vector2(
                minX <= maxX ? Math.Clamp(Position.X, minX, maxX) : (field.Info.VRLeft + field.Info.VRRight) / 2f,
                minY <= maxY ? Math.Clamp(Position.Y, minY, maxY) : (field.Info.VRTop + field.Info.VRBottom) / 2f);
        }
    }
}
