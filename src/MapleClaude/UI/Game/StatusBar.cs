using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Bottom status bar. Always visible at y=480 on 800x600.
///
/// Layout (fallback mode, no WZ):
///   [Lv.N Name] [HP bar] [MP bar]   [quickslot 0-7]   [CHR][CMM][EV][MENU][CS]
///   [======= EXP bar ========================================]
///
/// WZ: StatusBar3.img/mainBar/
///   status / status800   — background sprite
///   gauge/hp, gauge/mp   — filled gauge sprites
///   EXPBar               — exp bar sprite
///   quickSlot            — quickslot background
///   submenu              — submenu button set
/// </summary>
public sealed class StatusBar : GamePanel
{
    // ── WZ sprites ──────────────────────────────────────────────────────────
    private readonly WzSprite? _barBg;
    private readonly WzSprite? _hpFill;
    private readonly WzSprite? _mpFill;
    private readonly WzSprite? _expFill;
    private readonly WzSprite? _quickSlotBg;

    // ── Main-toolbar buttons ─────────────────────────────────────────────────
    private readonly Button? _btCharacter;   // opens Character submenu
    private readonly Button? _btCommunity;   // opens Community submenu
    private readonly Button? _btEvent;
    private readonly Button? _btMenu;        // opens Menu submenu
    private readonly Button? _btCashShop;
    private readonly List<Button> _mainButtons = new();

    // ── Submenu system ───────────────────────────────────────────────────────
    private enum SubMenu { None, Character, Setting, Community, Menu }
    private SubMenu _openSub = SubMenu.None;

    // Character sub: Info, Equip, Items, Skills, Stats
    private readonly Button?[] _charSubBtns = new Button?[5];
    private static readonly string[] CharSubNames = ["Info", "Equip", "Items", "Skills", "Stats"];

    // Setting sub: Channel, Options, Keys, Quit
    private readonly Button?[] _settingSubBtns = new Button?[4];
    private static readonly string[] SettingSubNames = ["Channel", "Options", "Keys", "Quit"];

    // ── Character stats (wired by GameStage) ────────────────────────────────
    public int Level { get; set; } = 1;
    public string CharName { get; set; } = "Unnamed";
    public int Hp { get; set; } = 50;  public int MaxHp { get; set; } = 50;
    public int Mp { get; set; } = 30;  public int MaxMp { get; set; } = 30;
    public long Exp { get; set; } = 0; public long NextExp { get; set; } = 100;

    // ── Quickslot ────────────────────────────────────────────────────────────
    private readonly string[] _slotLabels = ["1", "2", "3", "4", "5", "6", "7", "8"];
    private const int SlotW = 32;
    private const int SlotH = 32;

    // ── Callbacks ────────────────────────────────────────────────────────────
    public Action? OnInfo    { get; set; }
    public Action? OnEquip   { get; set; }
    public Action? OnItems   { get; set; }
    public Action? OnSkills  { get; set; }
    public Action? OnStats   { get; set; }
    public Action? OnOptions { get; set; }
    public Action? OnKeys    { get; set; }
    public Action? OnQuit    { get; set; }
    public Action? OnCashShop{ get; set; }

    private readonly BuiltInFont? _font;

    // Pre-computed gauge values
    private float _hpPct, _mpPct, _expPct;

    // Constants matching v95 800x600 layout
    private const int BarY     = 480;
    private const int GaugeX   = 130;
    private const int HpY      = 487;
    private const int MpY      = 502;
    private const int GaugeW   = 139;
    private const int GaugeH   = 10;
    private const int ExpY     = 477;
    private const int ExpH     = 4;
    private const int QsBaseX  = 270;
    private const int QsY      = 484;

    public StatusBar(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = true;
        Position = new Vector2(0, BarY);

        // Background
        var mainBar = ui?.GetItem("StatusBar3.img/mainBar") as WzProperty;
        var statusNode = mainBar?.Get("status") ?? mainBar?.Get("status800");
        _barBg = statusNode is WzCanvas sc ? loader.Load(sc) : null;

        // Gauge fills
        var gaugeNode = (mainBar?.Get("status") as WzProperty)?.Get("gauge") as WzProperty;
        _hpFill = LoadCanvas(loader, gaugeNode, "hp");
        _mpFill = LoadCanvas(loader, gaugeNode, "mp");
        _expFill = LoadCanvas(loader, mainBar, "EXPBar/0");
        _quickSlotBg = LoadCanvas(loader, mainBar, "quickSlot/0");

        // Main-toolbar buttons
        var sub = mainBar?.Get("submenu") as WzProperty;
        _btCharacter = MakeMainBtn(loader, sub, "character/0/normal/0",
            () => ToggleSub(SubMenu.Character));
        _btCommunity = MakeMainBtn(loader, sub, "community/0/normal/0",
            () => ToggleSub(SubMenu.Community));
        _btEvent     = MakeMainBtn(loader, sub, "event/0/normal/0", () => { });
        _btMenu      = MakeMainBtn(loader, sub, "menu/0/normal/0",
            () => ToggleSub(SubMenu.Menu));
        _btCashShop  = MakeMainBtn(loader, mainBar, "BT_CASHSHOP",
            () => OnCashShop?.Invoke());

        // Character sub-buttons (drawn if _openSub == Character)
        var charSub = (sub?.Get("character") as WzProperty);
        for (var i = 0; i < _charSubBtns.Length; i++)
        {
            var idx = i;
            var root = (charSub?.Get($"{i}") as WzProperty);
            // These are plain text buttons with fallback drawn rendering
            _charSubBtns[i] = root != null ? new Button(loader, root) { OnClick = () => CharSubClick(idx) } : null;
        }

        var settSub = sub?.Get("setting") as WzProperty;
        for (var i = 0; i < _settingSubBtns.Length; i++)
        {
            var idx = i;
            var root = settSub?.Get($"{i}") as WzProperty;
            _settingSubBtns[i] = root != null ? new Button(loader, root) { OnClick = () => SettingSubClick(idx) } : null;
        }

        LayoutMainButtons();
    }

    private void ToggleSub(SubMenu m) =>
        _openSub = _openSub == m ? SubMenu.None : m;

    private void CharSubClick(int i)
    {
        _openSub = SubMenu.None;
        switch (i)
        {
            case 0: OnInfo?.Invoke(); break;
            case 1: OnEquip?.Invoke(); break;
            case 2: OnItems?.Invoke(); break;
            case 3: OnSkills?.Invoke(); break;
            case 4: OnStats?.Invoke(); break;
        }
    }

    private void SettingSubClick(int i)
    {
        _openSub = SubMenu.None;
        switch (i)
        {
            case 2: OnKeys?.Invoke(); break;
            case 3: OnQuit?.Invoke(); break;
            default: OnOptions?.Invoke(); break;
        }
    }

    private void LayoutMainButtons()
    {
        // Right-side cluster, 24px spacing, bottom row
        var btns = new[] { _btCharacter, _btCommunity, _btEvent, _btMenu, _btCashShop };
        for (var i = 0; i < btns.Length; i++)
            if (btns[i] != null) btns[i]!.Position = new Vector2(632 + i * 26, BarY + 11);
    }

    public override void Update(GameTime gt)
    {
        if (MaxHp  > 0) _hpPct  = Math.Clamp((float)Hp  / MaxHp,  0f, 1f);
        if (MaxMp  > 0) _mpPct  = Math.Clamp((float)Mp  / MaxMp,  0f, 1f);
        if (NextExp > 0) _expPct = Math.Clamp((float)Exp / NextExp, 0f, 1f);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        // ── Background ──────────────────────────────────────────────────────
        if (_barBg != null)
            _barBg.Draw(sb, new Vector2(400, BarY + 10));
        else
        {
            sb.Draw(white, new Rectangle(0, BarY, 800, 40), new Color(18, 18, 28));
            sb.Draw(white, new Rectangle(0, BarY - 1, 800, 1), new Color(60, 60, 80));
        }

        // ── EXP bar ─────────────────────────────────────────────────────────
        DrawGauge(sb, white, new Rectangle(0, ExpY, 800, ExpH), _expPct,
            new Color(220, 180, 40), new Color(80, 60, 0, 160));

        // ── HP gauge ────────────────────────────────────────────────────────
        DrawGaugeLabeled(sb, white,
            new Rectangle(GaugeX, HpY, GaugeW, GaugeH),
            _hpPct, new Color(220, 50, 50), new Color(0, 0, 0, 140),
            $"{Hp}/{MaxHp}", new Color(255, 200, 200));

        // ── MP gauge ────────────────────────────────────────────────────────
        DrawGaugeLabeled(sb, white,
            new Rectangle(GaugeX, MpY, GaugeW, GaugeH),
            _mpPct, new Color(60, 90, 220), new Color(0, 0, 0, 140),
            $"{Mp}/{MaxMp}", new Color(180, 200, 255));

        // ── Level + name ────────────────────────────────────────────────────
        _font?.Draw(sb, $"Lv.{Level}", new Vector2(6, BarY + 5), new Color(240, 220, 100));
        _font?.Draw(sb, CharName,      new Vector2(6, BarY + 18), Color.White);

        // ── HP/MP labels ────────────────────────────────────────────────────
        _font?.Draw(sb, "HP", new Vector2(GaugeX - 22, HpY), new Color(255, 120, 120));
        _font?.Draw(sb, "MP", new Vector2(GaugeX - 22, MpY), new Color(120, 160, 255));

        // ── Quickslot ───────────────────────────────────────────────────────
        DrawQuickSlot(sb, white);

        // ── Main buttons ────────────────────────────────────────────────────
        foreach (var b in _mainButtons) b.Draw(sb);

        // ── Submenu overlay ──────────────────────────────────────────────────
        if (_openSub != SubMenu.None)
            DrawSubMenu(sb, white);
    }

    private void DrawQuickSlot(SpriteBatch sb, Texture2D white)
    {
        for (var i = 0; i < 8; i++)
        {
            var rx = QsBaseX + i * (SlotW + 2);
            var slot = new Rectangle(rx, QsY, SlotW, SlotH);
            sb.Draw(white, slot, new Color(30, 30, 45));
            DrawBorder(sb, white, slot, new Color(70, 70, 100));
            if (_font != null && i < _slotLabels.Length)
                _font.Draw(sb, _slotLabels[i], new Vector2(rx + 2, QsY + 2), new Color(160, 160, 200));
        }
    }

    private void DrawSubMenu(SpriteBatch sb, Texture2D white)
    {
        string[] items;
        int baseX;
        switch (_openSub)
        {
            case SubMenu.Character:
                items  = CharSubNames;
                baseX  = (int)(_btCharacter?.Position.X ?? 632);
                break;
            case SubMenu.Setting:
                items  = SettingSubNames;
                baseX  = (int)(_btMenu?.Position.X ?? 710);
                break;
            case SubMenu.Menu:
                items  = SettingSubNames;
                baseX  = (int)(_btMenu?.Position.X ?? 710);
                break;
            default: return;
        }

        var menuW = 90;
        var menuH = items.Length * 20 + 4;
        var menuY = BarY - menuH - 2;
        var rect  = new Rectangle(baseX - 4, menuY, menuW, menuH);

        sb.Draw(white, rect, new Color(15, 15, 25, 235));
        DrawBorder(sb, white, rect, new Color(90, 80, 60));

        for (var i = 0; i < items.Length; i++)
        {
            var itemY = menuY + 4 + i * 20;
            var btn = _openSub == SubMenu.Character ? _charSubBtns[i] : _settingSubBtns[Math.Min(i, _settingSubBtns.Length - 1)];
            if (btn != null)
                btn.Position = new Vector2(baseX, itemY + 8);
            _font?.Draw(sb, items[i], new Vector2(baseX + 4, itemY + 2), Color.White);
        }

        foreach (var b in (_openSub == SubMenu.Character ? (IEnumerable<Button?>)_charSubBtns : _settingSubBtns))
            b?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        // Submenu intercepts first
        if (_openSub != SubMenu.None)
        {
            var btns = _openSub == SubMenu.Character
                ? (IEnumerable<Button?>)_charSubBtns
                : _settingSubBtns;
            foreach (var b in btns)
                if (b?.HandleMouseButton(x, y, down) == true) return true;
            // Click outside submenu → close it
            if (down) _openSub = SubMenu.None;
        }

        foreach (var b in _mainButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        // Claim any click on bar area so it doesn't fall through to game
        return new Rectangle(0, BarY - 4, 800, 44).Contains(x, y);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void DrawGauge(SpriteBatch sb, Texture2D white,
        Rectangle r, float pct, Color fill, Color bg)
    {
        sb.Draw(white, r, bg);
        if (pct > 0f)
        {
            var fw = new Rectangle(r.X, r.Y, (int)(r.Width * pct), r.Height);
            sb.Draw(white, fw, fill);
        }
    }

    private void DrawGaugeLabeled(SpriteBatch sb, Texture2D white,
        Rectangle r, float pct, Color fill, Color bg, string label, Color textColor)
    {
        DrawGauge(sb, white, r, pct, fill, bg);
        DrawBorder(sb, white, r, new Color(60, 60, 80));
        if (_font != null)
        {
            var sz = _font.Measure(label);
            var tx = r.X + (r.Width - (int)sz.X) / 2;
            _font.Draw(sb, label, new Vector2(tx, r.Y), textColor);
        }
    }

    private Button? MakeMainBtn(WzTextureLoader loader, WzProperty? root, string path, Action onClick)
    {
        try
        {
            // Navigate nested path inside root
            var node = root;
            foreach (var part in path.Split('/'))
                node = node?.Get(part) as WzProperty;
            if (node is null) return null;
            var b = new Button(loader, node) { OnClick = onClick };
            _mainButtons.Add(b);
            return b;
        }
        catch { return null; }
    }

    private static WzSprite? LoadCanvas(WzTextureLoader loader, WzProperty? root, string path)
    {
        if (root is null) return null;
        var parts = path.Split('/');
        var cur = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            cur = cur?.Get(parts[i]) as WzProperty;
            if (cur is null) return null;
        }
        return cur?.Get(parts[^1]) is WzCanvas c ? loader.Load(c) : null;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
