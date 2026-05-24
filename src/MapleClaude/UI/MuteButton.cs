using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

/// <summary>
/// Small self-contained BGM mute toggle drawn at the top-right corner of the login
/// <c>Common/frame</c>. The frame is 800×600 with a centred origin, so its top-right
/// corner is at <c>(w/2 + 400, h/2 - 300)</c>; the button insets itself just inside.
///
/// It services its own input by polling the mouse (the corner is otherwise empty, so
/// there's no contention with the stage's own buttons), which keeps stage integration
/// to a single <see cref="ServiceAndDraw"/> call per frame. Muted state is read/written
/// through the supplied delegates (backed by the persistent audio player), so the icon
/// stays in sync across stage transitions.
/// </summary>
public sealed class MuteButton
{
    private const int Width = 24;
    private const int Height = 20;
    private const int MarginRight = 12;
    private const int MarginTop = 12;

    private readonly Func<bool> _isMuted;
    private readonly Action _onToggle;
    private readonly BuiltInFont? _font;

    private Rectangle _bounds;
    private bool _hover;
    private bool _pressedInside;
    private bool _prevDown;

    public MuteButton(Func<bool> isMuted, Action onToggle, BuiltInFont? font)
    {
        _isMuted = isMuted;
        _onToggle = onToggle;
        _font = font;
    }

    /// <summary>Repositions for the current viewport, polls the mouse for a click, and draws.</summary>
    public void ServiceAndDraw(SpriteBatch sb, Texture2D white, int viewW, int viewH)
    {
        _bounds = new Rectangle(
            viewW / 2 + 400 - Width - MarginRight,
            viewH / 2 - 300 + MarginTop,
            Width, Height);

        var m = Mouse.GetState();
        _hover = _bounds.Contains(m.X, m.Y);
        var down = m.LeftButton == ButtonState.Pressed;
        if (down && !_prevDown)
        {
            _pressedInside = _hover;
        }
        else if (!down && _prevDown)
        {
            if (_pressedInside && _hover)
            {
                _onToggle();
            }
            _pressedInside = false;
        }
        _prevDown = down;

        Draw(sb, white);
    }

    private void Draw(SpriteBatch sb, Texture2D white)
    {
        var muted = _isMuted();

        // Background plate + border so the icon reads on any part of the frame art.
        var fill = _hover ? new Color(70, 60, 40, 220) : new Color(35, 30, 22, 190);
        sb.Draw(white, _bounds, fill);
        DrawBorder(sb, white, _bounds, new Color(120, 100, 60));

        // A musical-note glyph (Malgun Gothic renders ♪ as a monochrome glyph).
        if (_font is not null)
        {
            const string note = "♪"; // ♪
            var sz = _font.Measure(note);
            var pos = new Vector2(
                _bounds.X + (_bounds.Width - sz.X) / 2f,
                _bounds.Y + (_bounds.Height - _font.LineHeight) / 2f);
            _font.Draw(sb, note, pos, muted ? new Color(120, 120, 120) : Color.White);
        }

        // When muted, strike a red diagonal slash across the icon.
        if (muted)
        {
            var a = new Vector2(_bounds.Left + 4, _bounds.Bottom - 4);
            var b = new Vector2(_bounds.Right - 4, _bounds.Top + 4);
            DrawLine(sb, white, a, b, new Color(220, 60, 60), 2f);
        }
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    private static void DrawLine(SpriteBatch sb, Texture2D white, Vector2 a, Vector2 b, Color c, float thickness)
    {
        var delta = b - a;
        var len = delta.Length();
        var angle = (float)Math.Atan2(delta.Y, delta.X);
        sb.Draw(white, a, null, c, angle, new Vector2(0f, 0.5f),
            new Vector2(len, thickness), SpriteEffects.None, 0f);
    }
}
