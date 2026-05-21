using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Character stats panel with ability-point distribution.
/// Toggle with S key. + buttons active when AP > 0.
/// Auto-assign (▶) button spends all AP into the primary stat.
///
/// WZ: UIWindow.img/Stat/
///   backgrnd, BtClose, BtDetail, BtAP, BtAutoAssign
/// </summary>
public sealed class StatsInfo : GamePanel
{
    // ── WZ ────────────────────────────────────────────────────────────────────
    private readonly WzSprite? _background;
    private readonly Button?   _btClose;
    private readonly Button?   _btDetail;
    private readonly List<Button> _allButtons = new();

    // Per-stat + buttons (drawn when AP > 0)
    private readonly Button?[] _apBtns = new Button?[4]; // STR DEX INT LUK

    // ── Stats (set by GameStage / server packets) ────────────────────────────
    public int Level { get; set; } = 1;
    public string Job { get; set; } = "Beginner";
    public int AP { get; set; } = 0;
    public int Str { get; set; } = 4;
    public int Dex { get; set; } = 4;
    public int Int { get; set; } = 4;
    public int Luk { get; set; } = 4;
    public int Hp  { get; set; } = 50;  public int MaxHp  { get; set; } = 50;
    public int Mp  { get; set; } = 5;   public int MaxMp  { get; set; } = 5;
    public int Atk { get; set; } = 10;
    public int Def { get; set; } = 10;
    public int Speed { get; set; } = 100;
    public int Jump  { get; set; } = 100;

    // ── Callbacks (wired by GameStage) ───────────────────────────────────────
    public Action? OnStrUp { get; set; }
    public Action? OnDexUp { get; set; }
    public Action? OnIntUp { get; set; }
    public Action? OnLukUp { get; set; }

    // ── Detail expand ────────────────────────────────────────────────────────
    private bool _showDetail;

    private readonly BuiltInFont? _font;

    private const int PanelW = 172;
    private const int PanelH = 340;
    private const int ColLabel = 10;
    private const int ColValue = 95;
    private const int ColBtn   = 140;
    private const int RowH     = 17;

    public StatsInfo(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(10, 80);

        var stat = ui?.GetItem("UIWindow.img/Stat") as WzProperty;
        _background = stat?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        _btClose  = MakeBtn(loader, stat, "BtClose",  () => IsVisible = false);
        _btDetail = MakeBtn(loader, stat, "BtDetail", () => _showDetail = !_showDetail);

        // + buttons — drawn conditionally in DrawStatRow, NOT in _allButtons loop
        var apRoot = stat?.Get("BtAP") as WzProperty;
        for (var i = 0; i < 4; i++)
        {
            var idx = i;
            if (apRoot != null)
                _apBtns[i] = new Button(loader, apRoot) { OnClick = () => SpendAP(idx) };
        }

        LayoutButtons();
    }

    // ── AP distribution ───────────────────────────────────────────────────────
    private void SpendAP(int statIndex)
    {
        if (AP <= 0) return;
        AP--;
        switch (statIndex)
        {
            case 0: Str++; OnStrUp?.Invoke(); break;
            case 1: Dex++; OnDexUp?.Invoke(); break;
            case 2: Int++; OnIntUp?.Invoke(); break;
            case 3: Luk++; OnLukUp?.Invoke(); break;
        }
    }

    public void AutoAssignAP()
    {
        // Primary stat by job: STR=warrior, DEX=bowman, INT=mage, LUK=thief, 0=beginner→all STR
        while (AP > 0) SpendAP(0);
    }

    // ── Update ───────────────────────────────────────────────────────────────
    public override void Update(GameTime gameTime)
    {
        LayoutButtons();
        foreach (var b in _apBtns)
            if (b != null) b.Enabled = AP > 0;
    }

    // ── Draw ─────────────────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;
        var h  = _showDetail ? PanelH + 80 : PanelH;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(PanelW / 2f, PanelH / 2f));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, h), new Color(12, 12, 22, 235));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, h));
        }

        _font?.Draw(sb, "Character Stats", new Vector2(px + 34, py + 5), new Color(220, 200, 150));

        var y = py + 22;
        DrawRow(sb, "Level",    Level.ToString(), px, ref y, false);
        DrawRow(sb, "Job",      Job,              px, ref y, false);
        y += 4;

        // AP banner (shown when AP > 0)
        if (AP > 0)
        {
            sb.Draw(white, new Rectangle(px + 4, y - 2, PanelW - 8, RowH + 2), new Color(60, 50, 0, 180));
            DrawBorder(sb, white, new Rectangle(px + 4, y - 2, PanelW - 8, RowH + 2), new Color(200, 160, 0));
        }
        _font?.Draw(sb, "AP", new Vector2(px + ColLabel, y), new Color(200, 200, 200));
        _font?.Draw(sb, AP.ToString(), new Vector2(px + ColValue, y),
            AP > 0 ? new Color(255, 220, 60) : new Color(160, 160, 160));
        y += RowH + 4;

        DrawStatRow(sb, white, "STR", Str, px, ref y, 0);
        DrawStatRow(sb, white, "DEX", Dex, px, ref y, 1);
        DrawStatRow(sb, white, "INT", Int, px, ref y, 2);
        DrawStatRow(sb, white, "LUK", Luk, px, ref y, 3);
        y += 4;

        DrawRow(sb, "HP",    $"{Hp}/{MaxHp}", px, ref y, false);
        DrawRow(sb, "MP",    $"{Mp}/{MaxMp}", px, ref y, false);
        y += 4;

        if (_showDetail)
        {
            DrawRow(sb, "ATK",   Atk.ToString(),   px, ref y, false);
            DrawRow(sb, "DEF",   Def.ToString(),   px, ref y, false);
            DrawRow(sb, "SPD",   Speed.ToString(), px, ref y, false);
            DrawRow(sb, "JUMP",  Jump.ToString(),  px, ref y, false);
        }

        // Auto-assign button (when AP > 0)
        if (AP > 0 && _font != null)
        {
            var btnR = new Rectangle(px + PanelW - 80, y + 2, 74, 16);
            sb.Draw(white, btnR, new Color(40, 40, 10));
            DrawBorder(sb, white, btnR, new Color(180, 150, 30));
            _font.Draw(sb, "Auto Assign", new Vector2(btnR.X + 4, btnR.Y + 2), new Color(220, 200, 60));
        }

        foreach (var b in _allButtons) b?.Draw(sb);
        _btClose?.Draw(sb);
        _btDetail?.Draw(sb);
    }

    private void DrawStatRow(SpriteBatch sb, Texture2D white, string label, int value,
        int px, ref float y, int btnIdx)
    {
        _font?.Draw(sb, label, new Vector2(px + ColLabel, y), new Color(200, 200, 200));
        _font?.Draw(sb, value.ToString(), new Vector2(px + ColValue, y), Color.White);
        // + button (only when AP > 0)
        if (AP > 0 && _apBtns[btnIdx] != null)
        {
            _apBtns[btnIdx]!.Position = new Vector2(px + ColBtn, y - 2);
            _apBtns[btnIdx]!.Draw(sb);
        }
        else if (AP > 0 && _font != null)
        {
            // Fallback drawn + button
            var r = new Rectangle(px + ColBtn, (int)y - 2, 18, 14);
            sb.Draw(white, r, new Color(30, 60, 30));
            DrawBorder(sb, white, r, new Color(80, 160, 80));
            _font.Draw(sb, "+", new Vector2(r.X + 4, r.Y), new Color(100, 220, 100));
        }
        y += RowH;
    }

    private void DrawRow(SpriteBatch sb, string label, string value,
        int px, ref float y, bool highlight)
    {
        _font?.Draw(sb, label, new Vector2(px + ColLabel, y),
            highlight ? new Color(220, 200, 100) : new Color(200, 200, 200));
        _font?.Draw(sb, value, new Vector2(px + ColValue, y), Color.White);
        y += RowH;
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons)
            if (b?.HandleMouseButton(x, y, down) == true) return true;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (_btDetail?.HandleMouseButton(x, y, down) == true) return true;
        if (AP > 0)
            foreach (var b in _apBtns)
                if (b?.HandleMouseButton(x, y, down) == true) return true;

        // Auto-assign hit test
        if (down && AP > 0)
        {
            var py = (int)Position.Y + PanelH - 50;
            var btnR = new Rectangle((int)Position.X + PanelW - 80, py, 74, 16);
            if (btnR.Contains(x, y)) { AutoAssignAP(); return true; }
        }

        return new Rectangle((int)Position.X, (int)Position.Y, PanelW,
            _showDetail ? PanelH + 80 : PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (key == Keys.Escape && IsVisible) { IsVisible = false; return true; }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void LayoutButtons()
    {
        if (_btClose  != null) _btClose.Position  = Position + new Vector2(PanelW - 18, 4);
        if (_btDetail != null) _btDetail.Position = Position + new Vector2(PanelW - 40, PanelH - 20);
    }

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r)
    {
        var c = new Color(70, 70, 90);
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
