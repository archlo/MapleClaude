using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Mid-game channel-change popup.
/// Shows current channel, grid of available channels, and Change/Cancel buttons.
/// Channel buttons are enabled/disabled based on population data.
/// WZ: UIWindow2.img/Channel/ (or Login.img/WorldSelect/)
/// </summary>
public sealed class ChannelSelect : GamePanel
{
    private const int Cols    = 5;
    private const int BtnW    = 60;
    private const int BtnH    = 24;
    private const int BtnGapX = 4;
    private const int BtnGapY = 4;
    private const int PanelW  = Cols * (BtnW + BtnGapX) + 20;
    private const int PanelH  = 200;

    private readonly WzSprite? _background;
    private readonly WzSprite? _chSelected;
    private readonly WzSprite? _chNormal;
    private readonly WzSprite? _chDisabled;
    private readonly Button?   _btChange;
    private readonly Button?   _btCancel;
    private readonly List<Button> _allButtons = new();

    private int   _currentChannel  = 1;
    private int   _selectedChannel = 1;
    private int   _channelCount    = 20;
    private bool  _dragging;
    private Vector2 _dragOff;

    // Per-channel population (0-100, -1 = unavailable)
    private readonly int[] _population = new int[20];

    public Action<int>? OnChannelChange { get; set; }

    private readonly BuiltInFont? _font;

    public ChannelSelect(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(280, 200);

        var ch = ui?.GetItem("UIWindow2.img/Channel") as WzProperty
              ?? ui?.GetItem("Login.img/WorldSelect") as WzProperty;

        _background  = ch?.Get("backgrnd")   is WzCanvas bc ? loader.Load(bc) : null;
        _chSelected  = LoadFromProp(loader, ch, "channel/chSelect");
        _chNormal    = LoadFromProp(loader, ch, "channel/0/normal");
        _chDisabled  = LoadFromProp(loader, ch, "channel/0/disabled");

        _btChange = MakeBtn(loader, ch, "BtGoworld",
            () => { OnChannelChange?.Invoke(_selectedChannel); IsVisible = false; });
        _btCancel = MakeBtn(loader, ch, "BtBack",
            () => IsVisible = false);

        // Default population: channels 1-5 enabled, rest disabled for placeholder
        for (var i = 0; i < 20; i++)
            _population[i] = i < 5 ? (i * 17 + 20) : -1;

        LayoutButtons();
    }

    public void Show(int currentChannel, int channelCount, int[] population)
    {
        _currentChannel  = currentChannel;
        _selectedChannel = currentChannel;
        _channelCount    = Math.Min(channelCount, 20);
        population.AsSpan(0, Math.Min(population.Length, 20)).CopyTo(_population);
        IsVisible = true;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        LayoutButtons();
        var m = Mouse.GetState();
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed)
                Position = new Vector2(m.X, m.Y) - _dragOff;
            else _dragging = false;
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(PanelW / 2f, PanelH / 2f));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(12, 14, 24, 245));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH), new Color(60, 70, 110));
        }

        // Title
        sb.Draw(white, new Rectangle(px, py, PanelW, 28), new Color(15, 18, 38));
        _font?.Draw(sb, "Channel Select", new Vector2(px + 60, py + 8), new Color(220, 200, 150));
        _font?.Draw(sb, $"Current: CH {_currentChannel}", new Vector2(px + 4, py + 8), new Color(160, 200, 160));

        // Channel grid
        var rows = (_channelCount + Cols - 1) / Cols;
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            var ch  = r * Cols + c + 1;
            if (ch > _channelCount) break;
            var cx  = px + 8 + c * (BtnW + BtnGapX);
            var cy  = py + 36 + r * (BtnH + BtnGapY);
            var pop = _population[ch - 1];
            DrawChannelBtn(sb, white, cx, cy, ch, pop);
        }

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawChannelBtn(SpriteBatch sb, Texture2D white,
        int x, int y, int ch, int pop)
    {
        var isSel     = ch == _selectedChannel;
        var isCurrent = ch == _currentChannel;
        var disabled  = pop < 0;

        Color fill, border, text;
        if (disabled) { fill = new Color(20, 20, 30);   border = new Color(40, 40, 55);   text = new Color(70, 70, 80); }
        else if (isSel)  { fill = new Color(40, 70, 40); border = new Color(80, 160, 80);  text = Color.White; }
        else if (isCurrent){ fill = new Color(30, 45, 60); border = new Color(60, 120, 180); text = new Color(140, 200, 255); }
        else             { fill = new Color(24, 26, 44); border = new Color(50, 55, 85);   text = new Color(180, 185, 210); }

        var r = new Rectangle(x, y, BtnW, BtnH);
        sb.Draw(white, r, fill);
        DrawBorder(sb, white, r, border);
        _font?.Draw(sb, $"CH {ch}", new Vector2(x + 8, y + 5), text);

        // Population bar (bottom 3px of button)
        if (!disabled && pop >= 0)
        {
            var barW  = (int)(BtnW * pop / 100f);
            var popColor = pop < 40 ? new Color(80, 180, 80) : pop < 70 ? new Color(220, 180, 40) : new Color(220, 70, 70);
            sb.Draw(white, new Rectangle(x, y + BtnH - 3, barW, 3), popColor);
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        // Channel grid hit-test
        if (down)
        {
            var px = (int)Position.X;
            var py = (int)Position.Y;
            var rows = (_channelCount + Cols - 1) / Cols;
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < Cols; c++)
            {
                var ch  = r * Cols + c + 1;
                if (ch > _channelCount) continue;
                var cx  = px + 8 + c * (BtnW + BtnGapX);
                var cy  = py + 36 + r * (BtnH + BtnGapY);
                if (new Rectangle(cx, cy, BtnW, BtnH).Contains(x, y) && _population[ch - 1] >= 0)
                {
                    _selectedChannel = ch;
                    return true;
                }
            }

            var titleBar = new Rectangle(px, py, PanelW, 28);
            if (titleBar.Contains(x, y))
            {
                _dragging = true;
                _dragOff  = new Vector2(x - Position.X, y - Position.Y);
            }
        }

        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (key == Keys.Enter)  { OnChannelChange?.Invoke(_selectedChannel); IsVisible = false; return true; }
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void LayoutButtons()
    {
        if (_btChange != null) _btChange.Position = Position + new Vector2(PanelW - 100, PanelH - 26);
        if (_btCancel != null) _btCancel.Position = Position + new Vector2(PanelW - 50,  PanelH - 26);
    }

    private static WzSprite? LoadFromProp(WzTextureLoader loader, WzProperty? root, string path)
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

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
