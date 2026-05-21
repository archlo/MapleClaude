using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Options / settings panel. Opened from the settings menu button.
/// WZ: <c>UIWindow.img/OptionMenu/</c>
/// </summary>
public sealed class OptionMenu : GamePanel
{
    private readonly WzSprite? _background;
    private readonly Button? _btClose;
    private readonly Button? _btOk;
    private readonly Button? _btCancel;
    private readonly BuiltInFont? _font;
    private readonly List<Button> _allButtons = new();

    // ── Settings state ────────────────────────────────────────────────────────
    private int  _bgmVolume   = 80;
    private int  _sfxVolume   = 100;
    private bool _showDamage  = true;
    private bool _showNames   = true;
    private bool _showHpBars  = true;
    private bool _showFps     = false;
    private bool _miniMapStart = true;
    private bool _snowEffect  = false;

    // Saved state for cancel
    private int  _savedBgm, _savedSfx;
    private bool _savedDmg, _savedNames, _savedHp, _savedFps, _savedMm, _savedSnow;

    private const int PanelW = 398;
    private const int PanelH = 388;

    public OptionMenu(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(200, 80);

        var opt = ui?.GetItem("UIWindow.img/OptionMenu") as WzProperty;
        _background = opt?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        _btClose  = MakeButton(loader, opt, "BtClose",  () => Cancel());
        _btOk     = MakeButton(loader, opt, "BtOK",     () => Accept());
        _btCancel = MakeButton(loader, opt, "BtCancel", () => Cancel());

        ApplyLayout();
    }

    private void Accept() => IsVisible = false;

    private void Cancel()
    {
        _bgmVolume   = _savedBgm;
        _sfxVolume   = _savedSfx;
        _showDamage  = _savedDmg;
        _showNames   = _savedNames;
        _showHpBars  = _savedHp;
        _showFps     = _savedFps;
        _miniMapStart = _savedMm;
        _snowEffect  = _savedSnow;
        IsVisible    = false;
    }

    private void SaveState()
    {
        _savedBgm   = _bgmVolume;
        _savedSfx   = _sfxVolume;
        _savedDmg   = _showDamage;
        _savedNames = _showNames;
        _savedHp    = _showHpBars;
        _savedFps   = _showFps;
        _savedMm    = _miniMapStart;
        _savedSnow  = _snowEffect;
    }

    public new bool IsVisible
    {
        get => base.IsVisible;
        set { if (value) SaveState(); base.IsVisible = value; }
    }

    private void ApplyLayout()
    {
        if (_btClose  != null) _btClose.Position  = Position + new Vector2(380, 6);
        if (_btOk     != null) _btOk.Position     = Position + new Vector2(268, 358);
        if (_btCancel != null) _btCancel.Position = Position + new Vector2(328, 358);
    }

    public override void Update(GameTime gameTime) => ApplyLayout();

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        ApplyLayout();

        var px = (int)Position.X;
        var py = (int)Position.Y;

        // Background
        if (_background != null)
            _background.Draw(sb, Position + new Vector2(195, 190));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(15, 15, 25, 230));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH));
        }

        _font?.Draw(sb, "Options", new Vector2(px + 165, py + 5), new Color(220, 200, 150));

        var y = py + 28;

        // ── Sound ─────────────────────────────────────────────────────────────
        DrawSectionHeader(sb, white, px + 8, y, PanelW - 16, "Sound");
        y += 22;

        DrawVolumeControl(sb, white, px + 12, y, "BGM Volume",  ref _bgmVolume);
        y += 26;
        DrawVolumeControl(sb, white, px + 12, y, "SFX Volume",  ref _sfxVolume);
        y += 32;

        // ── Display ───────────────────────────────────────────────────────────
        DrawSectionHeader(sb, white, px + 8, y, PanelW - 16, "Display");
        y += 22;

        DrawCheckRow(sb, white, px + 12, y, "Show Damage Numbers", ref _showDamage);  y += 24;
        DrawCheckRow(sb, white, px + 12, y, "Show Player Names",   ref _showNames);   y += 24;
        DrawCheckRow(sb, white, px + 12, y, "Show HP/MP Bars",     ref _showHpBars);  y += 24;
        DrawCheckRow(sb, white, px + 12, y, "Snow Effect",         ref _snowEffect);  y += 32;

        // ── Interface ─────────────────────────────────────────────────────────
        DrawSectionHeader(sb, white, px + 8, y, PanelW - 16, "Interface");
        y += 22;

        DrawCheckRow(sb, white, px + 12, y, "Show FPS Counter",       ref _showFps);      y += 24;
        DrawCheckRow(sb, white, px + 12, y, "Show Mini-Map on Start",  ref _miniMapStart); y += 24;

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawSectionHeader(SpriteBatch sb, Texture2D white, int x, int y, int w, string label)
    {
        sb.Draw(white, new Rectangle(x, y + 8, w, 1), new Color(80, 70, 50));
        _font?.Draw(sb, label, new Vector2(x + 4, y), new Color(200, 180, 120));
    }

    private void DrawVolumeControl(SpriteBatch sb, Texture2D white, int x, int y, string label, ref int volume)
    {
        _font?.Draw(sb, label, new Vector2(x, y), new Color(200, 200, 200));

        // 10-step bar
        var barX = x + 130;
        for (var i = 0; i < 10; i++)
        {
            var segRect = new Rectangle(barX + i * 16, y + 1, 14, 12);
            var filled  = i < volume / 10;
            sb.Draw(white, segRect, filled ? new Color(80, 160, 220) : new Color(30, 30, 60));
            DrawBorder(sb, white, segRect);
        }

        // Volume label
        _font?.Draw(sb, $"{volume}%", new Vector2(barX + 164, y), new Color(160, 200, 255));
    }

    private void DrawCheckRow(SpriteBatch sb, Texture2D white, int x, int y, string label, ref bool value)
    {
        // Checkbox box
        var box = new Rectangle(x, y + 1, 12, 12);
        sb.Draw(white, box, new Color(25, 25, 50));
        DrawBorder(sb, white, box);
        if (value)
        {
            sb.Draw(white, new Rectangle(x + 2, y + 3, 8, 6), new Color(100, 220, 100));
        }

        _font?.Draw(sb, label, new Vector2(x + 18, y), new Color(200, 200, 200));
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (down)
        {
            // Volume bar click — BGM at row 50, SFX at row 76
            TryVolumeClick(x, y, px + 12 + 130, py + 50, ref _bgmVolume);
            TryVolumeClick(x, y, px + 12 + 130, py + 76, ref _sfxVolume);

            // Checkboxes — positions match DrawCheckRow calls above
            var cy = py + 28 + 22 + 26 + 26 + 32 + 22;   // start of Display section checkboxes
            TryCheckboxClick(x, y, px + 12, cy,      ref _showDamage);
            TryCheckboxClick(x, y, px + 12, cy + 24, ref _showNames);
            TryCheckboxClick(x, y, px + 12, cy + 48, ref _showHpBars);
            TryCheckboxClick(x, y, px + 12, cy + 72, ref _snowEffect);

            var iy = cy + 72 + 32 + 22;  // Interface section
            TryCheckboxClick(x, y, px + 12, iy,      ref _showFps);
            TryCheckboxClick(x, y, px + 12, iy + 24, ref _miniMapStart);
        }

        return new Rectangle(px, py, PanelW, PanelH).Contains(x, y);
    }

    private static void TryVolumeClick(int mx, int my, int barX, int barY, ref int volume)
    {
        for (var i = 0; i < 10; i++)
        {
            if (new Rectangle(barX + i * 16, barY + 1, 14, 12).Contains(mx, my))
            {
                volume = (i + 1) * 10;
                break;
            }
        }
    }

    private static void TryCheckboxClick(int mx, int my, int cx, int cy, ref bool value)
    {
        if (new Rectangle(cx, cy + 1, 12, 12).Contains(mx, my))
            value = !value;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { Cancel(); return true; }
        if (key == Keys.Enter)  { Accept(); return true; }
        return true;
    }

    private Button? MakeButton(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
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
