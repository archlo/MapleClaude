using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// In-game notice / alert dialog (server broadcasts, system messages, etc.).
/// WZ: <c>UIWindow.img/Notice/</c>
/// </summary>
public sealed class Notice : GamePanel
{
    private readonly WzSprite? _background;
    private readonly Button? _btOk;
    private readonly BuiltInFont? _font;
    private string _message = string.Empty;

    public Action? OnClose { get; set; }

    public Notice(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(250, 220);

        var notice = ui?.GetItem("UIWindow.img/Notice") as WzProperty;
        _background = notice?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        var okRoot = notice?.Get("BtOK") as WzProperty;
        if (okRoot != null)
            _btOk = new Button(loader, okRoot)
            {
                Position = Position + new Vector2(145, 90),
                OnClick = () => { IsVisible = false; OnClose?.Invoke(); },
            };
    }

    public void Show(string message)
    {
        _message = message;
        IsVisible = true;
        if (_btOk != null) _btOk.Position = Position + new Vector2(145, 90);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        // Dim
        sb.Draw(white, new Rectangle(0, 0, 800, 600), new Color(0, 0, 0, 80));

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(150, 55));
        else
        {
            var rect = new Rectangle((int)Position.X, (int)Position.Y, 302, 115);
            sb.Draw(white, rect, new Color(15, 15, 25, 240));
            DrawBorder(sb, white, rect);
        }

        _font?.Draw(sb, _message, Position + new Vector2(8, 18), Color.White);
        _btOk?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        _btOk?.HandleMouseButton(x, y, down);
        return true;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Enter || key == Keys.Escape)
        {
            IsVisible = false;
            OnClose?.Invoke();
            return true;
        }
        return true;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r)
    {
        var c = new Color(80, 70, 50);
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
