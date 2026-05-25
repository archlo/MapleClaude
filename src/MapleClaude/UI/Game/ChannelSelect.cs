using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// In-game channel-change dialog — the authentic v95 <c>CUIChannelShift</c>, drawn from
/// <c>UIWindow2.img/Channel</c> (the MODERN art; NOT the login <c>CUIChannelSelect</c>).
///
/// Layout is WZ-origin baked like the other v95 windows: a 3-layer background
/// (<c>backgrnd</c>/<c>backgrnd2</c>/<c>backgrnd3</c>, 370×168) plus a 5-column channel grid of
/// <c>channel0</c> (normal) / <c>channel1</c> (selected) cell sprites with <c>ch/&lt;i&gt;</c> number
/// labels centred on each, an optional <c>world/&lt;id&gt;</c> name label, and the
/// <c>BtChange</c>/<c>BtCancel</c> buttons. Cell rects are a port of
/// <c>CUIChannelShift::GetRectFromIdx</c>: <c>left=70·(i%5)+11, top=20·(i/5)+55</c>, 68×20.
/// </summary>
public sealed class ChannelSelect : GamePanel
{
    private const int CellW = 68, CellH = 20, Cols = 5;
    private const int GridX = 11, GridY = 55, PitchX = 70, PitchY = 20;
    private const int MaxChannels = 20;

    private readonly WzSprite?[] _bg = new WzSprite?[3];
    private readonly WzSprite?   _cellNormal;
    private readonly WzSprite?   _cellSelected;
    private readonly WzSprite?[] _chNum = new WzSprite?[32];
    private readonly WzSprite?[] _world = new WzSprite?[24];
    private readonly Button?     _btChange;
    private readonly Button?     _btCancel;
    private readonly List<Button> _allButtons = new();
    private readonly BuiltInFont? _font;

    private int  _currentChannel  = 1;   // 1-based (highlighted as "you are here")
    private int  _selectedChannel = 1;   // 1-based (the cell the user clicked)
    private int  _channelCount    = MaxChannels;
    private int  _worldId         = -1;  // -1 = unknown → no world-name label
    private bool _dragging;
    private Vector2 _dragOff;

    /// <summary>Raised with the 1-based channel number when the user confirms a change.</summary>
    public Action<int>? OnChannelChange { get; set; }

    public ChannelSelect(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(300, 200);

        var ch = ui?.GetItem("UIWindow2.img/Channel") as WzProperty;
        _bg[0]        = Canvas(loader, ch, "backgrnd");
        _bg[1]        = Canvas(loader, ch, "backgrnd2");
        _bg[2]        = Canvas(loader, ch, "backgrnd3");
        _cellNormal   = Canvas(loader, ch, "channel0");
        _cellSelected = Canvas(loader, ch, "channel1");

        var chNum = ch?.Get("ch") as WzProperty;
        for (var i = 0; i < _chNum.Length; i++) _chNum[i] = Canvas(loader, chNum, i.ToString());
        var world = ch?.Get("world") as WzProperty;
        for (var i = 0; i < _world.Length; i++) _world[i] = Canvas(loader, world, i.ToString());

        _btChange = MakeBtn(loader, ch, "BtChange",
            () => { OnChannelChange?.Invoke(_selectedChannel); IsVisible = false; });
        _btCancel = MakeBtn(loader, ch, "BtCancel", () => IsVisible = false);
    }

    /// <summary>Populate the dialog before showing it (current channel highlighted, world label
    /// chosen). All optional — toggling <see cref="GamePanel.IsVisible"/> directly keeps the defaults.</summary>
    public void Show(int currentChannel, int channelCount, int worldId = -1)
    {
        _currentChannel  = Math.Max(1, currentChannel);
        _selectedChannel = _currentChannel;
        _channelCount    = Math.Clamp(channelCount, 1, MaxChannels);
        _worldId         = worldId;
        IsVisible        = true;
    }

    private int PanelW => _bg[0]?.Width ?? 370;
    private int PanelH => _bg[0]?.Height ?? 168;

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        LayoutButtons();
        var m = Mouse.GetState();
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = new Vector2(m.X, m.Y) - _dragOff;
            else _dragging = false;
        }
        foreach (var b in _allButtons) b.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_bg[0] != null)
        {
            for (var i = 0; i < 3; i++) _bg[i]?.Draw(sb, Position);
        }
        else
        {
            var r = new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH);
            sb.Draw(white, r, new Color(12, 14, 24, 245));
            DrawBorder(sb, white, r, new Color(60, 70, 110));
            _font?.Draw(sb, "Change Channel", Position + new Vector2(12, 8), new Color(220, 200, 150));
        }

        // World name label, centred near the top (only when the world id is known).
        if (_worldId >= 0 && _worldId < _world.Length && _world[_worldId] is { } wl)
            wl.Draw(sb, Position + new Vector2((PanelW - wl.Width) / 2f, 31));

        // Channel grid: a cell sprite (selected vs normal) + the channel-number label, per channel.
        for (var i = 0; i < _channelCount; i++)
        {
            var cell    = CellTopLeft(i);
            var oneBased = i + 1;
            var bgCell  = oneBased == _selectedChannel ? _cellSelected : _cellNormal;
            bgCell?.Draw(sb, cell);
            var num = i < _chNum.Length ? _chNum[i] : null;
            if (num != null)
                num.Draw(sb, cell + new Vector2((CellW - num.Width) / 2f, (CellH - num.Height) / 2f));
            else
                _font?.Draw(sb, oneBased.ToString(), cell + new Vector2(26, 4),
                    oneBased == _selectedChannel ? Color.White : new Color(60, 50, 35));
            // A small "current channel" tick so the player can tell where they already are.
            if (oneBased == _currentChannel && oneBased != _selectedChannel)
                sb.Draw(white, new Rectangle((int)cell.X + 2, (int)cell.Y + CellH - 3, CellW - 4, 2),
                    new Color(90, 160, 220));
        }

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private Vector2 CellTopLeft(int i) =>
        Position + new Vector2(PitchX * (i % Cols) + GridX, PitchY * (i / Cols) + GridY);

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons) if (b.HandleMouseButton(x, y, down)) return true;

        if (down)
        {
            for (var i = 0; i < _channelCount; i++)
            {
                var c = CellTopLeft(i);
                if (new Rectangle((int)c.X, (int)c.Y, CellW, CellH).Contains(x, y))
                {
                    _selectedChannel = i + 1;
                    return true;
                }
            }
            var title = new Rectangle((int)Position.X, (int)Position.Y, PanelW, 22);
            if (title.Contains(x, y))
            {
                _dragging = true;
                _dragOff  = new Vector2(x - Position.X, y - Position.Y);
                return true;
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

    private void LayoutButtons()
    {
        if (_btChange != null) _btChange.Position = Position;
        if (_btCancel != null) _btCancel.Position = Position;
    }

    private static WzSprite? Canvas(WzTextureLoader loader, WzProperty? parent, string name)
        => parent?.Get(name) is WzCanvas c ? loader.Load(c) : null;

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        if (root?.Get(name) is not WzProperty pr) return null;
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
