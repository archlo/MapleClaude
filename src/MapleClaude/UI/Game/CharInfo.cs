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
/// <c>UIWindow2.img/UserInfo/character</c> (layered backgrounds 271×190). Opens for your own character
/// (status-bar Info button / CharInfo key / double-click yourself) and for another player when you
/// double-click them in the field — that path round-trips through the server
/// (<c>UserCharacterInfoRequest</c> → <c>CharacterInfo</c>).
///
/// The frame bakes the panel chrome; the content (name, the standing <b>avatar</b>, level/job, fame,
/// guild/alliance, active pets) is composed on top. The avatar is rendered through
/// <see cref="CharacterRenderer"/> — the same compositor the in-world player uses — and for another
/// player it reuses the look already decoded when they entered the field (the CharacterInfo response
/// carries no avatar). Text uses the basic font and the local coordinates the original client uses
/// (e.g. the fame line sits beside the give/defame buttons at local y≈106).
/// </summary>
public sealed class CharInfo : GamePanel
{
    private readonly WzSprite? _bg;
    private readonly WzSprite? _bg2;
    private readonly WzSprite? _bg3;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;
    private readonly ItemIconLoader? _icons;

    // ── Subject data (set via ShowProfile) ────────────────────────────────────
    public string CharName { get; set; } = string.Empty;
    public int    Level    { get; set; } = 1;
    public string Job      { get; set; } = "Beginner";
    public int    Fame     { get; set; }
    public string Guild    { get; set; } = string.Empty;
    public string Alliance { get; set; } = string.Empty;

    private CharacterRenderer? _renderer;
    private AvatarLook? _look;
    private IReadOnlyList<CharInfoPet>? _pets;
    private bool _dragging;
    private Vector2 _dragOff;

    private int PanelW => _bg?.Width ?? 271;
    private int PanelH => _bg?.Height ?? 190;

    // Local layout (window-relative). Tunable via the MAPLECLAUDE_DEBUG overlay; derived from the
    // authentic UIWindow2.img/UserInfo/character node geometry (content panel 6,23..265,160; avatar
    // box = left ~90px; fame buttons at 235/248,106).
    private const int   NameTopY  = 7;
    private static readonly Vector2 AvatarPen = new(58, 122);
    private const int   InfoX     = 116;
    private const int   RowLevel  = 34;
    private const int   RowJob    = 54;
    private const int   RowGuild  = 80;
    private const int   RowFame   = 104;
    private const int   RowAlly   = 128;
    private const int   PetRowY   = 166;   // icon baseline (pet icons anchor by their bottom-left origin)
    private const int   PetIconStep = 30;

    private static readonly Color NameColor  = new(255, 230, 120);
    private static readonly Color LabelColor = new(90, 80, 60);
    private static readonly Color ValueColor = new(40, 38, 34);

    public CharInfo(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, ItemIconLoader? icons = null)
    {
        _font = font;
        _icons = icons;
        IsVisible = false;
        Position = new Vector2(300, 180);

        var ci = ui?.GetItem("UIWindow2.img/UserInfo/character") as WzProperty;
        _bg  = ci?.Get("backgrnd")  is WzCanvas a ? loader.Load(a) : null;
        _bg2 = ci?.Get("backgrnd2") is WzCanvas b ? loader.Load(b) : null;
        _bg3 = ci?.Get("backgrnd3") is WzCanvas c ? loader.Load(c) : null;

        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
            _btClose = new Button(loader, close) { OnClick = () => IsVisible = false };
    }

    /// <summary>Populate the window with a subject (self or another player) and show it. The avatar
    /// <paramref name="look"/> is reused from the field for other players (the CharacterInfo response
    /// carries no look); pass <c>null</c> pets for your own profile.</summary>
    public void ShowProfile(string name, int level, string job, int fame, string guild, string alliance,
        CharacterRenderer? renderer, AvatarLook? look, IReadOnlyList<CharInfoPet>? pets)
    {
        CharName  = name;
        Level     = level;
        Job       = job;
        Fame      = fame;
        Guild     = guild;
        Alliance  = alliance;
        _renderer = renderer;
        _look     = look;
        _pets     = pets;
        IsVisible = true;
    }

    /// <summary>Swap just the avatar (e.g. when the local player's look changes on equip/unequip).</summary>
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
        }

        // The standing avatar in the left box (faces right, like the char-select/create previews).
        if (_renderer != null && _look != null)
            _renderer.Draw(sb, _look, null, Stance.Stand1, 0, Position + AvatarPen, facingLeft: false);

        if (_font != null)
        {
            // Name centred across the top.
            var name = string.IsNullOrEmpty(CharName) ? "-" : CharName;
            var nameW = _font.Measure(name).X;
            _font.Draw(sb, name, Position + new Vector2((PanelW - nameW) / 2f, NameTopY), NameColor);

            // Right column: the player's standing info.
            DrawField(sb, RowLevel, "Lv.",   Level.ToString());
            DrawField(sb, RowJob,   "Job",   Job);
            DrawField(sb, RowGuild, "Guild", string.IsNullOrEmpty(Guild) ? "-" : Guild);
            DrawField(sb, RowFame,  "Fame",  Fame.ToString());
            if (!string.IsNullOrEmpty(Alliance))
                DrawField(sb, RowAlly, "Union", Alliance);

            DrawPets(sb, white);
        }

        _btClose?.Draw(sb);
    }

    private void DrawField(SpriteBatch sb, int y, string label, string value)
    {
        var lx = Position.X + InfoX;
        _font!.Draw(sb, label, new Vector2(lx, Position.Y + y), LabelColor);
        var lw = _font.Measure(label).X;
        _font.Draw(sb, value, new Vector2(lx + lw + 5, Position.Y + y), ValueColor);
    }

    // Active pets (from the CharacterInfo response): up to three icons in a row under the info column.
    // Pet icons anchor by their bottom-left origin, so they "stand" on the PetRowY baseline.
    private void DrawPets(SpriteBatch sb, Texture2D white)
    {
        if (_pets is null || _pets.Count == 0) return;
        var n = Math.Min(_pets.Count, 3);
        for (var i = 0; i < n; i++)
        {
            var icon = _icons?.LoadPetIcon(_pets[i].TemplateId);
            var at = Position + new Vector2(InfoX + i * PetIconStep, PetRowY);
            if (icon != null) icon.Draw(sb, at);
            else sb.Draw(white, new Rectangle((int)at.X, (int)at.Y - 24, 24, 24), new Color(70, 60, 50, 180));
        }
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
