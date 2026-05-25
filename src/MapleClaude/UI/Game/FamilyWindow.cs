using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Family window — the authentic v95 <c>CUIFamily</c>, drawn from <c>UIWindow2.img/Family</c>
/// (frame 214×343, baked "FAMILY"). Shows the player's family standing (reputation / juniors).
///
/// The Kinoko server exposes the family opcodes (<c>FamilyInfoRequest</c>/<c>FamilyInfoResult</c>,
/// chart, register junior, …) but the full pedigree decode + management actions aren't wired yet
/// (a blind wire decoder can't be verified offline; the router safely skips the unhandled result).
/// This window renders the authentic shell + the family data we hold; the tree/ops are a follow-up.
/// </summary>
public sealed class FamilyWindow : GamePanel
{
    private readonly WzSprite? _bg, _bg2;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;
    private bool _dragging;
    private Vector2 _dragOff;

    // Family standing (0 until family data flows from the server).
    public int Reputation { get; set; }
    public int TodayRep { get; set; }
    public int JuniorCount { get; set; }
    public bool InFamily { get; set; }

    private int PanelW => _bg?.Width ?? 214;
    private int PanelH => _bg?.Height ?? 343;

    public FamilyWindow(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(330, 120);

        var fam = ui?.GetItem("UIWindow2.img/Family") as WzProperty;
        _bg  = fam?.Get("backgrnd")  is WzCanvas a ? loader.Load(a) : null;
        _bg2 = fam?.Get("backgrnd2") is WzCanvas b ? loader.Load(b) : null;

        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
            _btClose = new Button(loader, close) { OnClick = () => IsVisible = false };
    }

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 4);
        var m = Mouse.GetState();
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = new Vector2(m.X, m.Y) - _dragOff;
            else _dragging = false;
        }
        _btClose?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_bg != null) { _bg.Draw(sb, Position); _bg2?.Draw(sb, Position); }
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(15, 15, 25, 235));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH), new Color(80, 70, 50));
            _font?.Draw(sb, "FAMILY", new Vector2(px + 84, py + 5), new Color(220, 200, 150));
        }

        if (_font != null)
        {
            var x = px + 18;
            var y = py + 44;
            if (!InFamily)
            {
                _font.Draw(sb, "You are not in a family.", new Vector2(px + 30, py + 150), new Color(150, 150, 160));
            }
            else
            {
                _font.Draw(sb, $"Reputation : {Reputation}", new Vector2(x, y), new Color(225, 225, 225)); y += 18;
                _font.Draw(sb, $"Today's Rep : {TodayRep}", new Vector2(x, y), new Color(225, 225, 225)); y += 18;
                _font.Draw(sb, $"Juniors : {JuniorCount}", new Vector2(x, y), new Color(225, 225, 225));
            }
        }

        _btClose?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (down && new Rectangle((int)Position.X, (int)Position.Y, PanelW, 22).Contains(x, y))
        { _dragging = true; _dragOff = new Vector2(x - Position.X, y - Position.Y); return true; }
        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (key == Keys.Escape && IsVisible) { IsVisible = false; return true; }
        return false;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
