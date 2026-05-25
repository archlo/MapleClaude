using MapleClaude.Character;
using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Character info / profile window — the authentic v95 <c>CUIUserInfo</c> "character" tab, drawn from
/// <c>UIWindow2.img/UserInfo/character</c> (layered backgrounds 271×190). The old window loaded a
/// non-existent <c>UIWindow.img/CharInfo</c> node and fell back to hand-drawn chrome.
///
/// The frame bakes the "CHARACTER INFO" title; the content (name, the player's standing **avatar**,
/// level/job, fame, guild) is composed on top. The avatar is rendered through
/// <see cref="CharacterRenderer"/> — the same compositor the in-world player uses — so the profile
/// shows the real character beneath the name, matching the client's layout.
/// </summary>
public sealed class CharInfo : GamePanel
{
    private readonly WzSprite? _bg;
    private readonly WzSprite? _bg2;
    private readonly WzSprite? _bg3;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;

    // ── Stats (set by GameStage from CharStats) ───────────────────────────────
    public string CharName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public string Job { get; set; } = "Beginner";
    public int Fame { get; set; }
    public string Guild { get; set; } = string.Empty;

    // ── Avatar (set by GameStage) ─────────────────────────────────────────────
    private CharacterRenderer? _renderer;
    private AvatarLook? _look;
    private bool _dragging;
    private Vector2 _dragOff;

    private int PanelW => _bg?.Width ?? 271;
    private int PanelH => _bg?.Height ?? 190;

    private static readonly Color NameColor  = new(255, 230, 120);
    private static readonly Color LabelColor = new(90, 80, 60);
    private static readonly Color ValueColor = new(40, 38, 34);

    public CharInfo(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(300, 180);

        var ci = ui?.GetItem("UIWindow2.img/UserInfo/character") as WzProperty;
        _bg  = ci?.Get("backgrnd")  is WzCanvas a ? loader.Load(a) : null;
        _bg2 = ci?.Get("backgrnd2") is WzCanvas b ? loader.Load(b) : null;
        _bg3 = ci?.Get("backgrnd3") is WzCanvas c ? loader.Load(c) : null;

        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
            _btClose = new Button(loader, close) { OnClick = () => IsVisible = false };
    }

    /// <summary>Supply the avatar compositor + the player's look so the profile renders the real
    /// character. Called on SetField / look change.</summary>
    public void SetAvatar(CharacterRenderer renderer, AvatarLook look)
    {
        _renderer = renderer;
        _look = look;
    }

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 6);
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

        if (_bg != null)
        {
            _bg.Draw(sb, Position);
            _bg2?.Draw(sb, Position);
            _bg3?.Draw(sb, Position);
        }
        else
        {
            var r = new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH);
            sb.Draw(white, r, new Color(15, 15, 25, 230));
            DrawBorder(sb, white, r, new Color(80, 70, 50));
            _font?.Draw(sb, "CHARACTER INFO", Position + new Vector2(70, 6), NameColor);
        }

        // Name across the top of the content area, then the avatar beneath it (left), with the
        // level/job/fame/guild lines to the right — the authentic UserInfo composition.
        if (_font != null)
        {
            var name = string.IsNullOrEmpty(CharName) ? "-" : CharName;
            _font.Draw(sb, name, Position + new Vector2(16, 28), NameColor);

            var rx = Position.X + 150;
            DrawRow(sb, rx, 52, "Level", Level.ToString());
            DrawRow(sb, rx, 70, "Job",   Job);
            DrawRow(sb, rx, 88, "Fame",  Fame.ToString());
            DrawRow(sb, rx, 106, "Guild", string.IsNullOrEmpty(Guild) ? "-" : Guild);
        }

        // The player's standing avatar, beneath the name on the left. The pen is the body origin
        // (~navel); place it so head/feet sit inside the content box.
        if (_renderer != null && _look != null)
            _renderer.Draw(sb, _look, null, Stance.Stand1, 0,
                Position + new Vector2(68, 120), facingLeft: false);

        _btClose?.Draw(sb);
    }

    private void DrawRow(SpriteBatch sb, float x, int y, string label, string value)
    {
        _font!.Draw(sb, label, new Vector2(x, Position.Y + y), LabelColor);
        _font.Draw(sb, value, new Vector2(x, Position.Y + y + 9), ValueColor);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (down)
        {
            var title = new Rectangle((int)Position.X, (int)Position.Y, PanelW, 22);
            if (title.Contains(x, y)) { _dragging = true; _dragOff = new Vector2(x - Position.X, y - Position.Y); return true; }
        }
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
