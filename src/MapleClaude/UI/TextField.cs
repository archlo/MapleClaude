using System.Text;
using MapleClaude.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI;

/// <summary>
/// A minimal text input field. Renders the supplied WZ background sprite,
/// captures typed characters via <see cref="OnTextInput"/>, and shows the
/// character count as a row of small dots (true text rendering arrives with
/// the font system on the next branch). When focused, a blinking caret
/// shows at the end.
/// </summary>
public sealed class TextField
{
    private readonly StringBuilder _text = new();
    private float _caretTimer;
    private bool _caretVisible = true;

    public Vector2 Position { get; set; }
    public int Width { get; set; } = 160;
    public int Height { get; set; } = 24;
    public bool IsFocused { get; set; }
    public bool IsPassword { get; set; }
    public int MaxLength { get; set; } = 24;
    public WzSprite? Background { get; set; }
    public BuiltInFont? Font { get; set; }

    public string Text => _text.ToString();

    public void Clear() => _text.Clear();

    public Rectangle Bounds => new(
        (int)Position.X, (int)Position.Y, Width, Height);

    public void OnTextInput(char ch)
    {
        if (!IsFocused)
        {
            return;
        }
        if (ch == '\b')
        {
            if (_text.Length > 0)
            {
                _text.Length--;
            }
            return;
        }
        if (ch is < ' ' or '\x7F')
        {
            return;
        }
        if (_text.Length >= MaxLength)
        {
            return;
        }
        _text.Append(ch);
    }

    /// <summary>Returns true if the click was inside the field (changing focus).</summary>
    public bool HandleMouseButton(int x, int y, bool down)
    {
        if (!down)
        {
            return false;
        }
        var inside = Bounds.Contains(x, y);
        IsFocused = inside;
        return inside;
    }

    public void Update(GameTime gameTime)
    {
        _caretTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_caretTimer >= 0.5f)
        {
            _caretTimer -= 0.5f;
            _caretVisible = !_caretVisible;
        }
    }

    public void Draw(SpriteBatch sb, Texture2D whitePixel)
    {
        // WZ background contains baked-in placeholder text ("Maple ID" /
        // "Password"). Hide it once the user has typed something so the
        // typed content isn't visually competing with the placeholder.
        var hasText = _text.Length > 0;
        if (Background is not null)
        {
            if (!hasText)
            {
                Background.Draw(sb, Position);
            }
        }
        else
        {
            sb.Draw(whitePixel, Bounds, new Color(230, 230, 230));
            sb.Draw(whitePixel, new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 1), Color.DarkGray);
            sb.Draw(whitePixel, new Rectangle(Bounds.X, Bounds.Y + Bounds.Height - 1, Bounds.Width, 1), Color.DarkGray);
        }

        // Text rendering: non-password fields with a font render the real
        // typed text. Password fields (or any field without a font available)
        // fall back to the dot placeholder so the ID doesn't look censored
        // and the PW doesn't expose its content.
        const int textPadX = 6;
        int caretX;
        if (!IsPassword && Font is not null)
        {
            var textY = (int)Position.Y + (Height - Font.LineHeight) / 2;
            Font.Draw(sb, _text.ToString(), new Vector2((int)Position.X + textPadX, textY), Color.Black);
            caretX = (int)Position.X + textPadX + (int)Font.Measure(_text.ToString()).X;
        }
        else
        {
            var dotColor = IsPassword ? Color.Black : new Color(40, 40, 60);
            const int dotSize = 4;
            const int dotSpacing = 7;
            var startX = (int)Position.X + textPadX;
            var dotY = (int)Position.Y + (Height - dotSize) / 2;
            for (var i = 0; i < _text.Length && startX + i * dotSpacing + dotSize < Position.X + Width - 8; i++)
            {
                var rect = new Rectangle(startX + i * dotSpacing, dotY, dotSize, dotSize);
                sb.Draw(whitePixel, rect, dotColor);
            }
            caretX = startX + Math.Min(_text.Length, MaxLength) * dotSpacing;
        }

        if (IsFocused && _caretVisible)
        {
            sb.Draw(whitePixel, new Rectangle(caretX, (int)Position.Y + 4, 1, Height - 8), Color.Black);
        }
    }
}
