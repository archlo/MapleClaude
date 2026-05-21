using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// NPC shop panel. Shown when a shop packet arrives.
/// WZ: <c>UIWindow.img/Shop/</c>
/// </summary>
public sealed class Shop : GamePanel
{
    private sealed record ShopItem(string Name, int ItemId, int Price, bool Buyable = true);

    private readonly WzSprite? _background;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;
    private readonly List<Button> _allButtons = new();

    private int  _tab;         // 0=Buy, 1=Sell
    private int  _scroll;
    private int  _selected = -1;

    private static readonly ShopItem[] _buyItems =
    [
        new("Red Potion",            2000000,     50),
        new("Orange Potion",         2000001,    150),
        new("White Potion",          2000002,    500),
        new("Blue Potion",           2000003,    150),
        new("Mana Elixir",           2000005,   3000),
        new("Power Elixir",          2000006,   5000),
        new("Antidote",              2050001,    100),
        new("Eye Drops",             2050000,    100),
        new("Return Scroll to Town", 2030000,   2000),
        new("Magic Rock",            4006000,   5000),
        new("Summoning Sack (Blue)", 2270000,   1500),
        new("Arrow for Bow",         2060000,     10),
    ];

    private static readonly ShopItem[] _sellItems =
    [
        new("Blue Snail Shell",   4000000,  10,  false),
        new("Snail Shell",        4000001,   6,  false),
        new("Red Snail Shell",    4000002,  25,  false),
        new("Mushroom",           4000010,  14,  false),
        new("Firewood",           4020007,  30,  false),
    ];

    private const int PanelW   = 436;
    private const int PanelH   = 344;
    private const int ItemH    = 50;
    private const int ListTop  = 58;
    private const int ListBot  = 290;
    private const int VisRows  = (ListBot - ListTop) / ItemH;  // 4

    public Shop(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(180, 120);

        var shop = ui?.GetItem("UIWindow.img/Shop") as WzProperty;
        _background = shop?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        _btClose = MakeButton(loader, shop, "BtClose", () => IsVisible = false);

        ApplyLayout();
    }

    private ShopItem[] CurrentList => _tab == 0 ? _buyItems : _sellItems;

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

        // Background
        if (_background != null)
            _background.Draw(sb, Position + new Vector2(216, 170));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(15, 15, 25, 230));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH));
        }

        _font?.Draw(sb, "Shop", new Vector2(px + 198, py + 5), new Color(220, 200, 150));

        // Tabs
        DrawTab(sb, white, px + 8,  py + 24, 120, "Buy",  _tab == 0);
        DrawTab(sb, white, px + 136, py + 24, 120, "Sell", _tab == 1);

        sb.Draw(white, new Rectangle(px, py + 46, PanelW, 1), new Color(80, 70, 50));

        // Item list
        var list  = CurrentList;
        var maxSc = Math.Max(0, list.Length - VisRows);
        _scroll   = Math.Clamp(_scroll, 0, maxSc);

        for (var i = 0; i < VisRows; i++)
        {
            var idx = i + _scroll;
            if (idx >= list.Length) break;
            var isSelected = idx == _selected;
            DrawItem(sb, white, list[idx], px + 6, py + ListTop + i * ItemH, isSelected);
        }

        // Scroll bar (right edge)
        if (list.Length > VisRows)
        {
            var trackH   = ListBot - ListTop;
            var thumbH   = Math.Max(20, trackH * VisRows / list.Length);
            var thumbY   = list.Length > 1 ? _scroll * (trackH - thumbH) / (list.Length - VisRows) : 0;
            sb.Draw(white, new Rectangle(px + PanelW - 14, py + ListTop, 10, trackH), new Color(25, 25, 50));
            sb.Draw(white, new Rectangle(px + PanelW - 14, py + ListTop + thumbY, 10, thumbH), new Color(80, 70, 120));
        }

        // Bottom action bar
        sb.Draw(white, new Rectangle(px, py + ListBot + 4, PanelW, 1), new Color(80, 70, 50));
        DrawActionBar(sb, white, px, py + ListBot + 8, list);

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawTab(SpriteBatch sb, Texture2D white, int x, int y, int w, string label, bool active)
    {
        var bg = active ? new Color(40, 40, 70, 220) : new Color(20, 20, 40, 160);
        sb.Draw(white, new Rectangle(x, y, w, 20), bg);
        DrawBorder(sb, white, new Rectangle(x, y, w, 20));
        var c = active ? new Color(255, 220, 100) : new Color(180, 180, 180);
        _font?.Draw(sb, label, new Vector2(x + 44, y + 3), c);
    }

    private void DrawItem(SpriteBatch sb, Texture2D white, ShopItem item, int x, int y, bool selected)
    {
        var bg = selected ? new Color(60, 55, 100, 220) : new Color(25, 25, 45, 200);
        sb.Draw(white, new Rectangle(x, y + 1, PanelW - 20, ItemH - 2), bg);
        DrawBorder(sb, white, new Rectangle(x, y + 1, PanelW - 20, ItemH - 2));

        // Icon placeholder (colored by item category)
        var iconColor = item.ItemId / 1000000 switch
        {
            2 => new Color(220, 80, 80),   // consumable = red
            4 => new Color(120, 180, 220), // etc = blue
            _ => new Color(160, 120, 60),  // other = brown
        };
        sb.Draw(white, new Rectangle(x + 4, y + 5, 38, 38), iconColor);
        DrawBorder(sb, white, new Rectangle(x + 4, y + 5, 38, 38));

        // Name + ID
        _font?.Draw(sb, item.Name, new Vector2(x + 48, y + 6), Color.White);
        _font?.Draw(sb, $"ID: {item.ItemId}", new Vector2(x + 48, y + 21), new Color(140, 140, 160));

        // Price
        var priceStr = $"{item.Price:N0} meso";
        _font?.Draw(sb, priceStr, new Vector2(x + 48, y + 33), new Color(255, 220, 80));
    }

    private void DrawActionBar(SpriteBatch sb, Texture2D white, int px, int py, ShopItem[] list)
    {
        if (_selected >= 0 && _selected < list.Length)
        {
            var item    = list[_selected];
            var btnColor = new Color(50, 100, 160);
            var btnLabel = _tab == 0 ? "Buy" : "Sell";
            sb.Draw(white, new Rectangle(px + PanelW - 80, py, 68, 22), btnColor);
            DrawBorder(sb, white, new Rectangle(px + PanelW - 80, py, 68, 22));
            _font?.Draw(sb, $"{btnLabel}: {item.Name}", new Vector2(px + 8, py + 4), new Color(220, 220, 220));
            _font?.Draw(sb, btnLabel, new Vector2(px + PanelW - 66, py + 4), Color.White);
        }
        else
        {
            _font?.Draw(sb, "Click an item to select", new Vector2(px + 8, py + 4), new Color(140, 140, 140));
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
            // Tabs
            if (new Rectangle(px + 8,   py + 24, 120, 20).Contains(x, y)) { _tab = 0; _scroll = 0; _selected = -1; return true; }
            if (new Rectangle(px + 136, py + 24, 120, 20).Contains(x, y)) { _tab = 1; _scroll = 0; _selected = -1; return true; }

            // Item rows
            var list = CurrentList;
            for (var i = 0; i < VisRows; i++)
            {
                var idx = i + _scroll;
                if (idx >= list.Length) break;
                var itemRect = new Rectangle(px + 6, py + ListTop + i * ItemH + 1, PanelW - 20, ItemH - 2);
                if (itemRect.Contains(x, y)) { _selected = idx; return true; }
            }

            // Scroll track (right edge)
            if (new Rectangle(px + PanelW - 14, py + ListTop, 10, ListBot - ListTop).Contains(x, y))
            {
                var rel    = y - (py + ListTop);
                var trackH = ListBot - ListTop;
                _scroll = Math.Clamp(rel * list.Length / trackH, 0, Math.Max(0, list.Length - VisRows));
                return true;
            }

            // Action button
            var actBtn = new Rectangle(px + PanelW - 80, py + ListBot + 8, 68, 22);
            if (actBtn.Contains(x, y) && _selected >= 0)
            {
                // Buy/Sell action — placeholder (no packet yet)
                _selected = -1;
                return true;
            }
        }

        return new Rectangle(px, py, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        var list = CurrentList;
        if (key == Keys.Up   && _selected > 0)             { _selected--; ClampScrollToSelected(list.Length); return true; }
        if (key == Keys.Down && _selected < list.Length-1) { _selected++; ClampScrollToSelected(list.Length); return true; }
        return true;
    }

    private void ClampScrollToSelected(int count)
    {
        if (_selected >= 0)
        {
            _scroll = Math.Clamp(_scroll, Math.Max(0, _selected - VisRows + 1), _selected);
        }
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
