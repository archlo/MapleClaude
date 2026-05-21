using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// In-game chat bar. Always visible at bottom-left. Holds a scrollable chat log and text input.
/// WZ: <c>UIWindow2.img/Chat/</c>
/// </summary>
public sealed class ChatBar : GamePanel
{
    private const int MaxLines = 8;
    private const int LineHeight = 14;

    private readonly WzSprite? _background;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;
    private readonly TextField? _input;
    private readonly List<(string text, Color color)> _lines = new();

    public ChatBar(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = true;
        Position = new Vector2(0, 360);

        var chat = ui?.GetItem("UIWindow2.img/Chat") as WzProperty;
        _background = chat?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        var closeRoot = chat?.Get("BtClose") as WzProperty;
        if (closeRoot != null)
            _btClose = new Button(loader, closeRoot) { OnClick = () => IsVisible = false };

        if (font != null)
        {
            _input = new TextField
            {
                Position = Position + new Vector2(5, 102),
                Width = 230,
                Height = 16,
                Font = font,
                MaxLength = 70,
            };
        }

        AddLine("Welcome to MapleClaude!", new Color(220, 200, 100));
    }

    public void AddLine(string text, Color? color = null)
    {
        _lines.Add((text, color ?? Color.White));
        if (_lines.Count > 100) _lines.RemoveAt(0);
    }

    public override void Update(GameTime gameTime) => _input?.Update(gameTime);

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_background != null)
        {
            _background.Draw(sb, Position + new Vector2(122, 60));
        }
        else
        {
            var rect = new Rectangle((int)Position.X, (int)Position.Y, 244, 120);
            sb.Draw(white, rect, new Color(0, 0, 0, 160));
        }

        var visibleStart = Math.Max(0, _lines.Count - MaxLines);
        for (var i = visibleStart; i < _lines.Count; i++)
        {
            var y = Position.Y + 8 + (i - visibleStart) * LineHeight;
            _font?.Draw(sb, _lines[i].text, new Vector2(Position.X + 5, y), _lines[i].color);
        }

        _input?.Draw(sb, white);
        _btClose?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        _input?.HandleMouseButton(x, y, down);
        var chatRect = new Rectangle((int)Position.X, (int)Position.Y, 244, 120);
        return chatRect.Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (_input?.IsFocused == true)
        {
            if (key == Keys.Enter)
            {
                var text = _input.Text.Trim();
                if (text.Length > 0)
                {
                    AddLine(text, Color.White);
                    _input.Clear();
                }
                return true;
            }
            if (key == Keys.Back) { _input.OnTextInput('\b'); return true; }
        }
        return false;
    }

    public override void OnTextInput(char ch)
    {
        if (_input?.IsFocused == true) _input.OnTextInput(ch);
    }
}
