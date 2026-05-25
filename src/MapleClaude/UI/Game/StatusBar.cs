using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// In-game bottom status bar — authentic v95 <c>CUIStatusBar</c>, drawn from
/// <c>StatusBar2.img/mainBar</c>. The bar is a <b>1024-wide, bottom-anchored</b> frame (1024 is
/// hard-coded at every resolution); it is centred for windows wider than 1024 and otherwise sits
/// flush. Every static element and button is <b>origin-baked</b>, so they all draw at the bar
/// reference point <c>R = (viewW/2, viewH-1)</c> (the canvas origins place each piece, exactly like the
/// login UI). The HP/MP/EXP gauges (3-slice, fill left→right), their bitmap numbers, the level digits,
/// and the name/job text are positioned manually at the IDB bar-relative coordinates.
///
/// Layout extracted from the IDB (CUIStatusBar::OnCreate, CGauge::SetVal, CQuickSlot) cross-referenced
/// with the WZ canvas origins. Bar-relative = (512 − origin.x, 84 − origin.y) within the 1024×85 bar.
/// </summary>
public sealed class StatusBar : GamePanel
{
    private const int BarW = 1024, BarH = 85;

    // Gauge fill rects (bar-relative) + pixel lengths; fill is left→right (CGauge::SetVal).
    private static readonly Vector2 HpGauge = new(254, 53); private const int HpLen = 138;
    private static readonly Vector2 MpGauge = new(423, 53); private const int MpLen = 138;
    private static readonly Vector2 ExpGauge = new(254, 69); private const int ExpLen = 308;
    // Numeric value anchors (bar-relative, centre-aligned), into the gauge-text layer.
    private static readonly Vector2 HpNum = new(389, 55), MpNum = new(558, 55), ExpNum = new(558, 71);
    // Level/name plate (bar-relative).
    private static readonly Vector2 LvNum = new(45, 59);   // level digits, right-aligned to x=45
    private static readonly Vector2 JobPos = new(75, 52);
    private static readonly Vector2 NamePos = new(75, 64);

    // ── Static origin-baked sprites (all drawn at R) ────────────────────────────
    private readonly WzSprite? _backgrnd, _lvBacktrnd, _lvCover, _gaugeBackgrd, _gaugeCover, _notice;
    private readonly WzSprite? _chatSpace, _chatSpace2, _chatEnter, _chatCover, _quickSlotPanel;
    // ── Gauge 3-slices ──────────────────────────────────────────────────────────
    private readonly WzSprite?[] _hp = new WzSprite?[3];
    private readonly WzSprite?[] _mp = new WzSprite?[3];
    private readonly WzSprite?[] _exp = new WzSprite?[3];
    // ── Bitmap fonts ────────────────────────────────────────────────────────────
    private readonly Dictionary<char, WzSprite> _gaugeGlyphs = new();
    private readonly WzSprite?[] _lvDigits = new WzSprite?[10];

    // ── Buttons (origin-baked; all positioned at R) ─────────────────────────────
    private readonly List<Button> _buttons = new();
    private readonly BuiltInFont? _font;
    private readonly BuiltInFont? _smallFont;   // small native-scale font for the crisp name/job plate

    // ── Character stats (wired by GameStage) ────────────────────────────────────
    public int Level { get; set; } = 1;
    public string CharName { get; set; } = "Unnamed";
    public string JobName { get; set; } = "Beginner";
    public int Hp { get; set; } = 50;  public int MaxHp { get; set; } = 50;
    public int Mp { get; set; } = 30;  public int MaxMp { get; set; } = 30;
    public long Exp { get; set; } = 0; public long NextExp { get; set; } = 100;

    // ── Button callbacks (wired by GameStage) ───────────────────────────────────
    public Action? OnCharacter { get; set; } // BtCharacter → character info
    public Action? OnStats     { get; set; } // BtStat
    public Action? OnQuest     { get; set; } // BtQuest
    public Action? OnItems     { get; set; } // BtInven
    public Action? OnEquip     { get; set; } // BtEquip
    public Action? OnSkills    { get; set; } // BtSkill
    public Action? OnKeys      { get; set; } // BtKeysetting
    public Action? OnChannel   { get; set; } // BtChannel
    public Action? OnCashShop  { get; set; } // BtCashShop
    public Action? OnMenu      { get; set; } // BtMenu
    public Action? OnSystem    { get; set; } // BtSystem
    public Action? OnMTS       { get; set; } // BtMTS
    public Action? OnChat      { get; set; } // BtChat (chat expand/collapse — 23b)
    public Action? OnClaim     { get; set; } // BtClaim
    // Kept for compatibility with existing GameStage wiring.
    public Action? OnInfo    { get; set; }
    public Action? OnOptions { get; set; }
    public Action? OnQuit    { get; set; }

    // ── Pop-up submenu item callbacks (the Menu / System buttons open these) ──────
    public Action? OnCommunity    { get; set; } // Menu → BtCommunity (UserList)
    public Action? OnMessenger    { get; set; } // Menu → BtMSN (Messenger window)
    public Action? OnRanking      { get; set; } // Menu → BtRank
    public Action? OnGameOption   { get; set; } // System → BtGameOption
    public Action? OnSystemOption { get; set; } // System → BtSystemOption
    public Action? OnJoyPad       { get; set; } // System → BtJoyPad

    private int _viewW = 1024, _viewH = 768;
    private float _hpPct, _mpPct, _expPct;
    private int _mouseX, _mouseY;
    private string? _tooltip;

    // Menu/System pop-up submenus (authentic StatusBar2.img/mainBar/{Menu,System}).
    private Button? _btMenu, _btSystem;
    private SubMenu? _menuPopup, _systemPopup, _openPopup;

    public StatusBar(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, BuiltInFont? smallFont = null)
    {
        _font = font;
        _smallFont = smallFont ?? font;
        IsVisible = true;

        var bar = ui?.GetItem("StatusBar2.img/mainBar") as WzProperty;

        _backgrnd    = Canvas(loader, bar, "backgrnd");
        _lvBacktrnd  = Canvas(loader, bar, "lvBacktrnd");
        _lvCover     = Canvas(loader, bar, "lvCover");
        _gaugeBackgrd = Canvas(loader, bar, "gaugeBackgrd");
        _gaugeCover  = Canvas(loader, bar, "gaugeCover");
        _notice      = Canvas(loader, bar, "notice");
        _chatSpace   = Canvas(loader, bar, "chatSpace");
        _chatSpace2  = Canvas(loader, bar, "chatSpace2");
        _chatEnter   = Canvas(loader, bar, "chatEnter");
        _chatCover   = Canvas(loader, bar, "chatCover");

        // Gauges: 3-slice (0 = left cap, 1 = stretched centre, 2 = right cap).
        var gauge = bar?.Get("gauge") as WzProperty;
        for (var i = 0; i < 3; i++)
        {
            _hp[i]  = Canvas(loader, gauge?.Get("hp")  as WzProperty, i.ToString());
            _mp[i]  = Canvas(loader, gauge?.Get("mp")  as WzProperty, i.ToString());
            _exp[i] = Canvas(loader, gauge?.Get("exp") as WzProperty, i.ToString());
        }

        // Gauge number bitmap font (0-9 plus '[', ']', '\\', '%', '.').
        if (gauge?.Get("number") is WzProperty num)
        {
            foreach (var (k, v) in num.Items)
            {
                if (k.Length == 1 && v is WzCanvas c && loader.Load(c) is { } g) _gaugeGlyphs[k[0]] = g;
            }
        }
        // Level digit bitmap font.
        if (bar?.Get("lvNumber") is WzProperty lv)
        {
            for (var i = 0; i < 10; i++) _lvDigits[i] = Canvas(loader, lv, i.ToString());
        }

        _quickSlotPanel = (bar?.Get("quickSlot") as WzProperty)?.Get("quickSlot") is WzCanvas qc
            ? loader.Load(qc) : null;

        // Bottom button row + menu row + chat controls (each carries its baked origin).
        AddButton(loader, bar, "BtCharacter", () => OnCharacter?.Invoke());
        AddButton(loader, bar, "BtStat",      () => OnStats?.Invoke());
        AddButton(loader, bar, "BtQuest",     () => OnQuest?.Invoke());
        AddButton(loader, bar, "BtInven",     () => OnItems?.Invoke());
        AddButton(loader, bar, "BtEquip",     () => OnEquip?.Invoke());
        AddButton(loader, bar, "BtSkill",     () => OnSkills?.Invoke());
        AddButton(loader, bar, "BtKeysetting",() => OnKeys?.Invoke());
        AddButton(loader, bar, "BtChannel",   () => OnChannel?.Invoke());
        AddButton(loader, bar, "BtCashShop",  () => OnCashShop?.Invoke());
        // BtMenu / BtSystem open authentic vertical pop-up submenus (built below) rather than a
        // single callback. We keep direct references so the pop-ups can anchor above each button.
        _btMenu   = AddButtonRef(loader, bar, "BtMenu",   () => Toggle(_menuPopup));
        _btSystem = AddButtonRef(loader, bar, "BtSystem", () => Toggle(_systemPopup));
        AddButton(loader, bar, "BtMTS",       () => OnMTS?.Invoke());
        AddButton(loader, bar, "BtChat",      () => OnChat?.Invoke());
        AddButton(loader, bar, "BtClaim",     () => OnClaim?.Invoke());

        // Authentic pop-up submenus (StatusBar2.img/mainBar/{Menu,System}); labels are baked into
        // the 63×25 button art, so each item is just an image button. Selecting one closes the pop-up.
        if (_btMenu != null)
            _menuPopup = new SubMenu(loader, bar?.Get("Menu") as WzProperty, _btMenu, new (string, Action)[]
            {
                ("BtItem",      () => { _openPopup = null; OnItems?.Invoke(); }),
                ("BtEquip",     () => { _openPopup = null; OnEquip?.Invoke(); }),
                ("BtStat",      () => { _openPopup = null; OnStats?.Invoke(); }),
                ("BtSkill",     () => { _openPopup = null; OnSkills?.Invoke(); }),
                ("BtCommunity", () => { _openPopup = null; OnCommunity?.Invoke(); }),
                ("BtQuest",     () => { _openPopup = null; OnQuest?.Invoke(); }),
                ("BtMSN",       () => { _openPopup = null; OnMessenger?.Invoke(); }),
                ("BtRank",      () => { _openPopup = null; OnRanking?.Invoke(); }),
            });
        if (_btSystem != null)
            _systemPopup = new SubMenu(loader, bar?.Get("System") as WzProperty, _btSystem, new (string, Action)[]
            {
                ("BtChannel",      () => { _openPopup = null; OnChannel?.Invoke(); }),
                ("BtKeySetting",   () => { _openPopup = null; OnKeys?.Invoke(); }),
                ("BtGameOption",   () => { _openPopup = null; OnGameOption?.Invoke(); }),
                ("BtSystemOption", () => { _openPopup = null; OnSystemOption?.Invoke(); }),
                ("BtGameQuit",     () => { _openPopup = null; OnQuit?.Invoke(); }),
                ("BtJoyPad",       () => { _openPopup = null; OnJoyPad?.Invoke(); }),
            });
    }

    /// <summary>Bar reference point: the 1024-wide bar is centred horizontally and bottom-anchored.
    /// Origin-baked sprites/buttons drawn here land at their authentic positions.</summary>
    // ≤1024: left-anchored (level plate flush left; the right end overhangs at 800, exactly like the
    // v95 client). Wider than 1024: centred. The bar is always 1024 wide.
    private float BarCenterX => Math.Max(512f, _viewW / 2f);
    private Vector2 BarRef => new(BarCenterX, _viewH - 1);
    /// <summary>Top-left of the 1024×85 bar in screen space (for manual bar-relative placement).</summary>
    private Vector2 BarTopLeft => new(BarCenterX - 512, _viewH - BarH);

    /// <summary>Screen rect of the chat input box (<c>chatEnter</c>) so the chat bar can place its input
    /// line on it. Derived from the origin-baked WZ sprite; falls back to the bar's bottom-left.</summary>
    public Rectangle ChatInputRect => _chatEnter is { } s
        ? new Rectangle((int)(BarRef.X - s.Origin.X), (int)(BarRef.Y - s.Origin.Y), s.Width, s.Height)
        : new Rectangle((int)BarTopLeft.X + 8, _viewH - 24, 480, 18);

    public override void Relayout(int viewWidth, int viewHeight)
    {
        _viewW = viewWidth;
        _viewH = viewHeight;
        var r = BarRef;
        foreach (var b in _buttons) b.Position = r;
        Position = BarTopLeft;
    }

    public override void Update(GameTime gt)
    {
        if (MaxHp  > 0)  _hpPct  = Math.Clamp((float)Hp  / MaxHp,  0f, 1f);
        if (MaxMp  > 0)  _mpPct  = Math.Clamp((float)Mp  / MaxMp,  0f, 1f);
        if (NextExp > 0) _expPct = Math.Clamp((float)Exp / NextExp, 0f, 1f);

        var ms = Mouse.GetState();
        _mouseX = ms.X; _mouseY = ms.Y;
        _tooltip = IsVisible ? HoverTooltip(_mouseX, _mouseY) : null;

        if (IsVisible) _openPopup?.UpdateHover(_mouseX, _mouseY);
        else _openPopup = null;
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        var r = BarRef;
        var tl = BarTopLeft;

        if (_backgrnd == null)
        {
            // Fallback: a plain band if StatusBar2.img is unavailable.
            sb.Draw(white, new Rectangle((int)tl.X, (int)tl.Y, BarW, BarH), new Color(18, 18, 28));
        }
        else
        {
            _backgrnd.Draw(sb, r);
        }

        // Chat area backgrounds (the chat log/input/tabs land here in 23b).
        _chatSpace?.Draw(sb, r);
        _chatSpace2?.Draw(sb, r);
        _chatEnter?.Draw(sb, r);
        _chatCover?.Draw(sb, r);

        // Level/name plate.
        _lvBacktrnd?.Draw(sb, r);
        _lvCover?.Draw(sb, r);

        // Gauges: backdrop → fills → cover.
        _gaugeBackgrd?.Draw(sb, r);
        DrawGauge(sb, white, _hp, tl + HpGauge, HpLen, _hpPct, new Color(220, 50, 50));
        DrawGauge(sb, white, _mp, tl + MpGauge, MpLen, _mpPct, new Color(60, 90, 220));
        DrawGauge(sb, white, _exp, tl + ExpGauge, ExpLen, _expPct, new Color(220, 180, 40));
        _gaugeCover?.Draw(sb, r);

        // Gauge numbers (bitmap font; HP/MP "[cur\max]", EXP "exp[pct%]").
        DrawGlyphs(sb, $"[{Hp}\\{MaxHp}]", tl + HpNum, GlyphAlign.Right);
        DrawGlyphs(sb, $"[{Mp}\\{MaxMp}]", tl + MpNum, GlyphAlign.Right);
        var pct = NextExp > 0 ? _expPct * 100f : 0f;
        DrawGlyphs(sb, $"{Exp}[{pct:0.00}%]", tl + ExpNum, GlyphAlign.Right);

        // Level digits (right-aligned to LvNum.x) + name/job text.
        DrawLevel(sb, tl + LvNum);
        DrawName(sb, tl);

        // Quickslot panel (origin-baked) + notice icon.
        // 800×600 quickslot only — above that, the bar's own (lower-right) quickslot is used instead.
        if (_viewW <= 800) _quickSlotPanel?.Draw(sb, r);
        _notice?.Draw(sb, r);

        foreach (var b in _buttons) b.Draw(sb);

        // Open pop-up submenu (drawn on top of the bar).
        _openPopup?.Draw(sb, white);

        DrawTooltip(sb, white);
    }

    private void DrawGauge(SpriteBatch sb, Texture2D white, WzSprite?[] slc, Vector2 pos, int length, float pct, Color fallback)
    {
        var fill = (int)(length * Math.Clamp(pct, 0f, 1f));
        if (fill <= 0) return;
        if (slc[0] == null || slc[1] == null || slc[2] == null)
        {
            sb.Draw(white, new Rectangle((int)pos.X, (int)pos.Y, fill, 10), fallback);
            return;
        }
        var lcap = slc[0]!; var mid = slc[1]!; var rcap = slc[2]!;
        lcap.Draw(sb, pos);
        var centreW = Math.Max(0, fill - lcap.Width - rcap.Width);
        if (centreW > 0)
            sb.Draw(mid.Texture, new Rectangle((int)pos.X + lcap.Width, (int)pos.Y, centreW, mid.Height), Color.White);
        rcap.Draw(sb, new Vector2(pos.X + lcap.Width + centreW, pos.Y));
    }

    private enum GlyphAlign { Left, Center, Right }

    private void DrawGlyphs(SpriteBatch sb, string text, Vector2 pos, GlyphAlign align)
    {
        var w = 0;
        foreach (var ch in text) if (_gaugeGlyphs.TryGetValue(ch, out var g)) w += g.Width;
        var x = align switch
        {
            GlyphAlign.Center => pos.X - w / 2f,
            GlyphAlign.Right => pos.X - w,
            _ => pos.X,
        };
        foreach (var ch in text)
        {
            if (!_gaugeGlyphs.TryGetValue(ch, out var g)) continue;
            g.Draw(sb, new Vector2(x, pos.Y));   // glyph origin is (0,0)
            x += g.Width;
        }
    }

    private void DrawLevel(SpriteBatch sb, Vector2 rightAnchor)
    {
        var s = Level.ToString();
        var w = 0;
        foreach (var ch in s) { var d = _lvDigits[ch - '0']; if (d != null) w += d.Width; }
        var x = rightAnchor.X - w;     // right-aligned
        foreach (var ch in s)
        {
            var d = _lvDigits[ch - '0'];
            if (d == null) continue;
            d.Draw(sb, new Vector2(x, rightAnchor.Y));
            x += d.Width;
        }
    }

    private void DrawName(SpriteBatch sb, Vector2 tl)
    {
        var f = _smallFont ?? _font;
        if (f == null) return;
        // Native scale (no bilinear downscale) keeps the small name/job text crisp + fully formed.
        f.Draw(sb, JobName, tl + JobPos, new Color(255, 230, 140));
        var namePos = tl + NamePos;
        // 1px black outline + white centre.
        foreach (var o in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
            f.Draw(sb, CharName, namePos + o, Color.Black);
        f.Draw(sb, CharName, namePos, Color.White);
    }

    // Hover tooltips over the gauges (mirrors the v95 CUIToolTip gauge popups): current/total values.
    private string? HoverTooltip(int x, int y)
    {
        var tl = BarTopLeft;
        const int gh = 13;
        if (InRect(x, y, tl + HpGauge, HpLen, gh)) return $"HP: {Hp} / {MaxHp}";
        if (InRect(x, y, tl + MpGauge, MpLen, gh)) return $"MP: {Mp} / {MaxMp}";
        if (InRect(x, y, tl + ExpGauge, ExpLen, gh))
        {
            var pct = NextExp > 0 ? (double)Exp / NextExp * 100.0 : 0.0;
            return $"EXP: {Exp} / {NextExp} ({pct:0.00}%)";
        }
        return null;
    }

    private static bool InRect(int x, int y, Vector2 tl, int w, int h)
        => x >= tl.X && x < tl.X + w && y >= tl.Y && y < tl.Y + h;

    private void DrawTooltip(SpriteBatch sb, Texture2D white)
    {
        if (_tooltip is null || _font is null) return;
        var sz = _font.Measure(_tooltip);
        const int pad = 4;
        var w = (int)sz.X + pad * 2;
        var h = (int)sz.Y + pad;
        var x = _mouseX + 12;
        var y = _mouseY - h - 4;
        if (x + w > _viewW) x = _viewW - w;
        if (y < 0) y = _mouseY + 18;
        var rect = new Rectangle(x, y, w, h);
        sb.Draw(white, rect, new Color(255, 255, 220, 245));   // v95 beige tooltip
        DrawBorder(sb, white, rect, new Color(90, 75, 45));
        _font.Draw(sb, _tooltip, new Vector2(x + pad, y + pad / 2f), Color.Black);
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        // An open pop-up submenu gets first crack; an item click closes it via its wired action.
        var open = _openPopup;
        if (open != null && open.HandleMouseButton(x, y, down)) return true;

        // Bar buttons — incl. BtMenu / BtSystem, which toggle the pop-ups.
        foreach (var b in _buttons)
            if (b.HandleMouseButton(x, y, down)) return true;

        // A press outside an open pop-up closes it.
        if (open != null && down && !open.Bounds.Contains(x, y)) _openPopup = null;

        // Swallow clicks on the bar band (or anywhere while a pop-up is open) so they don't reach the world.
        var tl = BarTopLeft;
        return open != null || new Rectangle((int)tl.X, (int)tl.Y, BarW, BarH).Contains(x, y);
    }

    public override void OnTextInput(char character) { }

    private void AddButton(WzTextureLoader loader, WzProperty? bar, string name, Action onClick)
        => AddButtonRef(loader, bar, name, onClick);

    private Button? AddButtonRef(WzTextureLoader loader, WzProperty? bar, string name, Action onClick)
    {
        if (bar?.Get(name) is not WzProperty root) return null;
        var b = new Button(loader, root) { OnClick = onClick, Position = BarRef };
        _buttons.Add(b);
        return b;
    }

    private void Toggle(SubMenu? popup) => _openPopup = ReferenceEquals(_openPopup, popup) ? null : popup;

    private static WzSprite? Canvas(WzTextureLoader loader, WzProperty? parent, string name)
        => parent?.Get(name) is WzCanvas c ? loader.Load(c) : null;

    /// <summary>
    /// A vertical pop-up submenu (opened by the bar's <c>Menu</c> / <c>System</c> buttons). Built from
    /// the authentic <c>StatusBar2.img/mainBar/{Menu|System}</c> 3-slice background + baked-label image
    /// buttons, anchored just above its owning bar button. Draw and hit-test share the same item
    /// positions, so the clickable area always matches the art.
    /// </summary>
    private sealed class SubMenu
    {
        // Tunable via the MAPLECLAUDE_DEBUG overlay, then baked. 63-wide buttons centred in the 79-wide bg.
        private const int ItemH = 25, LeftInset = 8, TopInset = 7, BotInset = 7;

        private readonly WzSprite? _bgTop, _bgMid, _bgBot;
        private readonly int _bgW;
        private readonly Button _anchor;
        private readonly List<Button> _buttons = new();

        public SubMenu(WzTextureLoader loader, WzProperty? root, Button anchor, (string name, Action onClick)[] items)
        {
            _anchor = anchor;
            if (root?.Get("backgrnd") is WzProperty bg)
            {
                _bgTop = Canvas(loader, bg, "0");
                _bgMid = Canvas(loader, bg, "1");
                _bgBot = Canvas(loader, bg, "2");
            }
            _bgW = _bgTop?.Width ?? 79;
            foreach (var (name, onClick) in items)
                if (root?.Get(name) is WzProperty br)
                    _buttons.Add(new Button(loader, br) { OnClick = onClick });
        }

        public int Height => TopInset + _buttons.Count * ItemH + BotInset;

        // Anchor the pop-up centred above its owning bar button (Bounds is origin-accounted).
        private Vector2 Anchor
        {
            get { var bb = _anchor.Bounds; return new Vector2(bb.Center.X - _bgW / 2f, bb.Top - Height); }
        }

        public Rectangle Bounds { get { var a = Anchor; return new Rectangle((int)a.X, (int)a.Y, _bgW, Height); } }

        private void Layout()
        {
            var a = Anchor;
            for (var i = 0; i < _buttons.Count; i++)
                _buttons[i].Position = new Vector2(a.X + LeftInset, a.Y + TopInset + i * ItemH);
        }

        public void UpdateHover(int x, int y)
        {
            Layout();
            foreach (var b in _buttons) b.Update(x, y, false);
        }

        public bool HandleMouseButton(int x, int y, bool down)
        {
            Layout();
            foreach (var b in _buttons)
                if (b.HandleMouseButton(x, y, down)) return true;
            return false;
        }

        public void Draw(SpriteBatch sb, Texture2D white)
        {
            Layout();
            var a = Anchor;
            var h = Height;
            if (_bgTop != null && _bgMid != null && _bgBot != null)
            {
                _bgTop.Draw(sb, a);
                var midH = Math.Max(0, h - _bgTop.Height - _bgBot.Height);
                if (midH > 0)
                    sb.Draw(_bgMid.Texture, new Rectangle((int)a.X, (int)a.Y + _bgTop.Height, _bgTop.Width, midH), Color.White);
                _bgBot.Draw(sb, new Vector2(a.X, a.Y + h - _bgBot.Height));
            }
            else
            {
                sb.Draw(white, new Rectangle((int)a.X, (int)a.Y, _bgW, h), new Color(30, 30, 42, 245));
            }
            foreach (var b in _buttons) b.Draw(sb);
        }
    }
}
