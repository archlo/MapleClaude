using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Player storage (trunk) panel. Shown when the server sends a
/// TrunkResult/OpenTrunkDlg after the player talks to a storage NPC.
/// Withdraw tab lists the trunk's contents; Deposit tab lists the player's
/// inventory. WZ: <c>UIWindow.img/Trunk/</c> (drawn fallback when absent).
/// </summary>
public sealed class Trunk : GamePanel
{
    /// <summary>A trunk row. For Withdraw, <c>InvType</c>/<c>Position</c> address the
    /// item within its trunk block; for Deposit, <c>Position</c> is the inventory slot
    /// (InvType 0).</summary>
    public sealed record TrunkItem(string Name, int ItemId, short Quantity, byte InvType, int Position);

    private readonly WzSprite? _background;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;
    private readonly List<Button> _allButtons = new();

    private int  _tab;          // 0=Withdraw (trunk), 1=Deposit (inventory)
    private int  _scroll;
    private int  _selected = -1;
    private int  _money;

    private readonly List<TrunkItem> _trunkItems = new();
    private readonly List<TrunkItem> _invItems = new();

    /// <summary>Withdraw: (invType, position) → GetItem.</summary>
    public Action<byte, byte>? OnWithdraw { get; set; }
    /// <summary>Deposit: (inventoryPos, itemId, count) → PutItem.</summary>
    public Action<short, int, short>? OnDeposit { get; set; }
    /// <summary>Sort the trunk → SortItem.</summary>
    public Action? OnSort { get; set; }
    /// <summary>Fired when the dialog closes (send the close request).</summary>
    public Action? OnClosed { get; set; }

    private const int PanelW  = 436;
    private const int PanelH  = 344;
    private const int ItemH   = 50;
    private const int ListTop = 58;
    private const int ListBot = 290;
    private const int VisRows = (ListBot - ListTop) / ItemH;  // 4

    public Trunk(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(180, 120);

        var trunk = ui?.GetItem("UIWindow.img/Trunk") as WzProperty;
        _background = trunk?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        _btClose = MakeButton(loader, trunk, "BtClose", Close);

        ApplyLayout();
    }

    /// <summary>Open with a fresh trunk listing + the player's inventory.</summary>
    public void Open(int money, IEnumerable<TrunkItem> trunkItems, IEnumerable<TrunkItem> invItems)
    {
        Refresh(money, trunkItems, invItems);
        _tab = 0; _scroll = 0; _selected = -1;
        IsVisible = true;
    }

    /// <summary>Replace the listings + money without resetting the panel state.</summary>
    public void Refresh(int money, IEnumerable<TrunkItem> trunkItems, IEnumerable<TrunkItem> invItems)
    {
        _money = money;
        _trunkItems.Clear(); _trunkItems.AddRange(trunkItems);
        _invItems.Clear();   _invItems.AddRange(invItems);
        if (_selected >= CurrentList.Count) _selected = -1;
    }

    public void SetMoney(int money) => _money = money;

    private IReadOnlyList<TrunkItem> CurrentList => _tab == 0 ? _trunkItems : _invItems;

    private void Close()
    {
        IsVisible = false;
        OnClosed?.Invoke();
    }

    private void ApplyLayout()
    {
        if (_btClose != null) _btClose.Position = Position + new Vector2(418, 4);
    }

    public override void Update(GameTime gameTime) => ApplyLayout();

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        ApplyLayout();

        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(216, 170));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(15, 15, 25, 230));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH));
        }

        _font?.Draw(sb, "Storage", new Vector2(px + 190, py + 5), new Color(220, 200, 150));

        // Tabs
        DrawTab(sb, white, px + 8,   py + 24, 120, "Withdraw", _tab == 0);
        DrawTab(sb, white, px + 136, py + 24, 120, "Deposit",  _tab == 1);

        sb.Draw(white, new Rectangle(px, py + 46, PanelW, 1), new Color(80, 70, 50));

        // Item list
        var list  = CurrentList;
        var maxSc = Math.Max(0, list.Count - VisRows);
        _scroll   = Math.Clamp(_scroll, 0, maxSc);

        for (var i = 0; i < VisRows; i++)
        {
            var idx = i + _scroll;
            if (idx >= list.Count) break;
            DrawItem(sb, white, list[idx], px + 6, py + ListTop + i * ItemH, idx == _selected);
        }

        if (list.Count > VisRows)
        {
            var trackH = ListBot - ListTop;
            var thumbH = Math.Max(20, trackH * VisRows / list.Count);
            var thumbY = list.Count > VisRows ? _scroll * (trackH - thumbH) / (list.Count - VisRows) : 0;
            sb.Draw(white, new Rectangle(px + PanelW - 14, py + ListTop, 10, trackH), new Color(25, 25, 50));
            sb.Draw(white, new Rectangle(px + PanelW - 14, py + ListTop + thumbY, 10, thumbH), new Color(80, 70, 120));
        }

        // Bottom bar: meso total, Sort + action buttons
        sb.Draw(white, new Rectangle(px, py + ListBot + 4, PanelW, 1), new Color(80, 70, 50));
        _font?.Draw(sb, $"Storage mesos: {_money:N0}", new Vector2(px + 8, py + ListBot + 10), new Color(255, 220, 80));
        DrawActionBar(sb, white, px, py + ListBot + 8, list);

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawTab(SpriteBatch sb, Texture2D white, int x, int y, int w, string label, bool active)
    {
        sb.Draw(white, new Rectangle(x, y, w, 20), active ? new Color(40, 40, 70, 220) : new Color(20, 20, 40, 160));
        DrawBorder(sb, white, new Rectangle(x, y, w, 20));
        _font?.Draw(sb, label, new Vector2(x + 38, y + 3), active ? new Color(255, 220, 100) : new Color(180, 180, 180));
    }

    private void DrawItem(SpriteBatch sb, Texture2D white, TrunkItem item, int x, int y, bool selected)
    {
        sb.Draw(white, new Rectangle(x, y + 1, PanelW - 20, ItemH - 2),
            selected ? new Color(60, 55, 100, 220) : new Color(25, 25, 45, 200));
        DrawBorder(sb, white, new Rectangle(x, y + 1, PanelW - 20, ItemH - 2));

        var iconColor = (item.ItemId / 1000000) switch
        {
            2 => new Color(220, 80, 80),
            4 => new Color(120, 180, 220),
            _ => new Color(160, 120, 60),
        };
        sb.Draw(white, new Rectangle(x + 4, y + 5, 38, 38), iconColor);
        DrawBorder(sb, white, new Rectangle(x + 4, y + 5, 38, 38));

        _font?.Draw(sb, item.Name, new Vector2(x + 48, y + 6), Color.White);
        _font?.Draw(sb, $"ID: {item.ItemId}", new Vector2(x + 48, y + 21), new Color(140, 140, 160));
        if (item.Quantity > 1)
            _font?.Draw(sb, $"x{item.Quantity}", new Vector2(x + 48, y + 33), new Color(200, 200, 200));
    }

    private void DrawActionBar(SpriteBatch sb, Texture2D white, int px, int py, IReadOnlyList<TrunkItem> list)
    {
        // Sort button (left of the action button)
        sb.Draw(white, new Rectangle(px + PanelW - 156, py, 64, 22), new Color(70, 90, 60));
        DrawBorder(sb, white, new Rectangle(px + PanelW - 156, py, 64, 22));
        _font?.Draw(sb, "Sort", new Vector2(px + PanelW - 142, py + 4), Color.White);

        if (_selected >= 0 && _selected < list.Count)
        {
            var label = _tab == 0 ? "Take" : "Store";
            sb.Draw(white, new Rectangle(px + PanelW - 80, py, 68, 22), new Color(50, 100, 160));
            DrawBorder(sb, white, new Rectangle(px + PanelW - 80, py, 68, 22));
            _font?.Draw(sb, label, new Vector2(px + PanelW - 62, py + 4), Color.White);
        }
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
            if (new Rectangle(px + 8,   py + 24, 120, 20).Contains(x, y)) { _tab = 0; _scroll = 0; _selected = -1; return true; }
            if (new Rectangle(px + 136, py + 24, 120, 20).Contains(x, y)) { _tab = 1; _scroll = 0; _selected = -1; return true; }

            var list = CurrentList;
            for (var i = 0; i < VisRows; i++)
            {
                var idx = i + _scroll;
                if (idx >= list.Count) break;
                if (new Rectangle(px + 6, py + ListTop + i * ItemH + 1, PanelW - 20, ItemH - 2).Contains(x, y))
                {
                    _selected = idx;
                    return true;
                }
            }

            if (new Rectangle(px + PanelW - 14, py + ListTop, 10, ListBot - ListTop).Contains(x, y))
            {
                var rel = y - (py + ListTop);
                _scroll = Math.Clamp(rel * list.Count / (ListBot - ListTop), 0, Math.Max(0, list.Count - VisRows));
                return true;
            }

            // Sort button
            if (new Rectangle(px + PanelW - 156, py + ListBot + 8, 64, 22).Contains(x, y))
            {
                OnSort?.Invoke();
                return true;
            }

            // Take / Store the selected row.
            if (new Rectangle(px + PanelW - 80, py + ListBot + 8, 68, 22).Contains(x, y)
                && _selected >= 0 && _selected < list.Count)
            {
                var item = list[_selected];
                if (_tab == 0) OnWithdraw?.Invoke(item.InvType, (byte)item.Position);
                else           OnDeposit?.Invoke((short)item.Position, item.ItemId, item.Quantity > 0 ? item.Quantity : (short)1);
                _selected = -1;
                return true;
            }
        }

        return new Rectangle(px, py, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { Close(); return true; }
        var list = CurrentList;
        if (key == Keys.Up   && _selected > 0)              { _selected--; ClampScrollToSelected(); return true; }
        if (key == Keys.Down && _selected < list.Count - 1) { _selected++; ClampScrollToSelected(); return true; }
        return true;
    }

    private void ClampScrollToSelected()
    {
        if (_selected >= 0)
            _scroll = Math.Clamp(_scroll, Math.Max(0, _selected - VisRows + 1), _selected);
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
