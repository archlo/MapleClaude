using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Character information card. Shows name/level/job/fame. Toggle via Character menu.
/// WZ: <c>UIWindow.img/CharInfo/</c>
/// </summary>
public sealed class CharInfo : GamePanel
{
    private readonly WzSprite? _background;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;

    public string CharName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public string Job { get; set; } = "Beginner";
    public int Fame { get; set; } = 0;
    public string Guild { get; set; } = string.Empty;

    public CharInfo(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(300, 180);

        var ci = ui?.GetItem("UIWindow.img/CharInfo") as WzProperty;
        _background = ci?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        var closeRoot = ci?.Get("BtClose") as WzProperty;
        if (closeRoot != null)
            _btClose = new Button(loader, closeRoot)
            {
                Position = Position + new Vector2(196, 6),
                OnClick = () => IsVisible = false,
            };
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(100, 135));
        else
        {
            var rect = new Rectangle((int)Position.X, (int)Position.Y, 204, 274);
            sb.Draw(white, rect, new Color(15, 15, 25, 230));
            DrawBorder(sb, white, rect);
            _font?.Draw(sb, "Character Info", Position + new Vector2(50, 5), new Color(220, 200, 150));
        }

        if (_font != null)
        {
            var x = Position.X + 8;
            var y = Position.Y + 24;
            _font.Draw(sb, CharName, new Vector2(x, y), Color.Yellow); y += 15;
            _font.Draw(sb, $"Lv.{Level} {Job}", new Vector2(x, y), Color.White); y += 15;
            _font.Draw(sb, $"Fame: {Fame}", new Vector2(x, y), Color.White); y += 15;
            if (Guild.Length > 0)
                _font.Draw(sb, $"Guild: {Guild}", new Vector2(x, y), new Color(180, 220, 180));
        }

        _btClose?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        return new Rectangle((int)Position.X, (int)Position.Y, 204, 274).Contains(x, y);
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
