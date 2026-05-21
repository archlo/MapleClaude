using MapleClaude.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI;

/// <summary>
/// A toggle backed by two WZ canvases (typically <c>Login.img/Title/check/0</c>
/// for unchecked and <c>/1</c> for checked).
/// </summary>
public sealed class Checkbox
{
    public Vector2 Position { get; set; }
    public bool IsChecked { get; set; }
    public WzSprite? Unchecked { get; set; }
    public WzSprite? Checked { get; set; }
    public int HitSize { get; set; } = 14;
    public Action<bool>? OnChange { get; set; }

    public Rectangle Bounds => new(
        (int)Position.X, (int)Position.Y, HitSize, HitSize);

    public bool HandleMouseButton(int x, int y, bool down)
    {
        if (!down)
        {
            return false;
        }
        if (!Bounds.Contains(x, y))
        {
            return false;
        }
        IsChecked = !IsChecked;
        OnChange?.Invoke(IsChecked);
        return true;
    }

    public void Draw(SpriteBatch sb)
    {
        var sprite = IsChecked ? Checked : Unchecked;
        sprite?.Draw(sb, Position);
    }
}
