using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Quest log panel. Toggle with Q key.
/// WZ: <c>UIWindow.img/QuestLog/</c>
/// </summary>
public sealed class QuestLog : GamePanel
{
    private readonly WzSprite? _background;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;

    public QuestLog(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(50, 50);

        var ql = ui?.GetItem("UIWindow.img/QuestLog") as WzProperty;
        _background = ql?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        var closeRoot = ql?.Get("BtClose") as WzProperty;
        if (closeRoot != null)
            _btClose = new Button(loader, closeRoot)
            {
                Position = Position + new Vector2(290, 6),
                OnClick = () => IsVisible = false,
            };
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(150, 195));
        else
        {
            sb.Draw(white, new Rectangle((int)Position.X, (int)Position.Y, 302, 396), new Color(15, 15, 25, 230));
            DrawBorder(sb, white, new Rectangle((int)Position.X, (int)Position.Y, 302, 396));
            _font?.Draw(sb, "Quest Log", Position + new Vector2(115, 5), new Color(220, 200, 150));
            _font?.Draw(sb, "(No quests)", Position + new Vector2(110, 100), new Color(140, 140, 140));
        }

        _btClose?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        return new Rectangle((int)Position.X, (int)Position.Y, 302, 396).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (key == Keys.Escape && IsVisible) { IsVisible = false; return true; }
        return false;
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
