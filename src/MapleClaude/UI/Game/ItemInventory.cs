using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Linq;

namespace MapleClaude.UI.Game;

/// <summary>
/// Item inventory panel. Toggle with I.
/// 5 tabs: Equip | Use | Setup | Etc | Cash
/// Each tab: 4-column grid of item slots (4×6 visible, scrollable).
/// Items wired from server inventory packets — placeholder test items pre-seeded.
/// WZ: UIWindow.img/Item/
/// </summary>
public sealed class ItemInventory : GamePanel
{
    // ── Item data ──────────────────────────────────────────────────────────────
    public sealed class InvItem
    {
        public int    Id;
        public string Name     = string.Empty;
        public int    Quantity = 1;
        public int    Tab;        // 0=Equip 1=Use 2=Setup 3=Etc 4=Cash
    }

    // ── Layout ─────────────────────────────────────────────────────────────────
    private const int Cols    = 4;
    private const int Rows    = 6;
    private const int SlotW   = 36;
    private const int SlotH   = 36;
    private const int GapX    = 2;
    private const int GapY    = 2;
    private const int PanelW  = Cols * (SlotW + GapX) + 16;
    private const int PanelH  = Rows * (SlotH + GapY) + 74;
    private const int GridX   = 8;
    private const int GridY   = 46;

    private static readonly string[] TabNames = ["Equip", "Use", "Setup", "Etc", "Cash"];
    private static readonly Color[] TabColors =
    [
        new Color(90, 130, 90),
        new Color(90, 90, 160),
        new Color(130, 110, 70),
        new Color(100, 100, 100),
        new Color(150, 80, 150),
    ];

    // ── UI ─────────────────────────────────────────────────────────────────────
    private readonly WzSprite? _background;
    private readonly WzSprite? _slotBg;
    private readonly Button?   _btClose;
    private readonly Button?[] _tabBtns = new Button?[5];
    private readonly List<Button> _allButtons = new();

    // ── State ──────────────────────────────────────────────────────────────────
    private int _activeTab;
    private readonly int[] _scrollOffset = new int[5];
    private readonly List<InvItem> _items = new();
    private InvItem? _hoverItem;
    private bool _dragging;
    private Vector2 _dragOff;

    private readonly BuiltInFont? _font;

    public ItemInventory(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(370, 50);

        var item = ui?.GetItem("UIWindow.img/Item") as WzProperty;
        _background = item?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;
        _slotBg     = item?.Get("slot")     is WzCanvas sc ? loader.Load(sc) : null;

        var closeRoot = item?.Get("BtClose") as WzProperty;
        if (closeRoot != null)
        {
            _btClose = new Button(loader, closeRoot) { OnClick = () => IsVisible = false };
            _allButtons.Add(_btClose);
        }

        var tabEnabled = (item?.Get("Tab") as WzProperty)?.Get("enabled") as WzProperty;
        for (var i = 0; i < 5; i++)
        {
            var idx = i;
            var tabRoot = tabEnabled?.Get($"{i}") as WzProperty;
            if (tabRoot != null)
            {
                _tabBtns[i] = new Button(loader, tabRoot)
                    { OnClick = () => { _activeTab = idx; } };
                _allButtons.Add(_tabBtns[i]!);
            }
        }

        SeedPlaceholder();
        LayoutButtons();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void AddItem(InvItem item) => _items.Add(item);
    public void RemoveItem(int id) => _items.RemoveAll(i => i.Id == id);
    public void Clear() => _items.Clear();

    // ── Update ─────────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        LayoutButtons();
        var m  = Mouse.GetState();
        var mp = new Vector2(m.X, m.Y);
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = mp - _dragOff;
            else _dragging = false;
        }
        _hoverItem = HitTestItem((int)mp.X, (int)mp.Y);
    }

    // ── Draw ───────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(PanelW / 2f, PanelH / 2f));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(12, 14, 24, 240));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH), new Color(60, 65, 100));
        }

        // Title strip
        sb.Draw(white, new Rectangle(px, py, PanelW, 22), new Color(15, 18, 36));
        _font?.Draw(sb, "Items - " + TabNames[_activeTab], new Vector2(px + 20, py + 5),
            new Color(220, 200, 150));

        // Tab strip
        var tabW = PanelW / 5;
        for (var i = 0; i < 5; i++)
        {
            var tx = px + i * tabW;
            var tr = new Rectangle(tx, py + 22, tabW, 20);
            var isActive = i == _activeTab;
            sb.Draw(white, tr, isActive ? TabColors[i] with { A = 200 } : new Color(20, 22, 38));
            DrawBorder(sb, white, tr, isActive ? TabColors[i] : new Color(40, 45, 65));
            _font?.Draw(sb, TabNames[i][..1], new Vector2(tx + tabW / 2 - 3, py + 26),
                isActive ? Color.White : new Color(130, 135, 160));
            _tabBtns[i]?.Draw(sb);
        }

        // Item grid
        var tabItems = _items.Where(it => it.Tab == _activeTab).ToList();
        var scroll = _scrollOffset[_activeTab];
        var maxSlots = Rows * Cols;

        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            var idx = scroll * Cols + r * Cols + c;
            var sr  = SlotRectAt(r, c);
            DrawSlot(sb, white, sr, idx < tabItems.Count ? tabItems[idx] : null);
        }

        // Scroll indicator
        var totalRows = (tabItems.Count + Cols - 1) / Cols;
        if (scroll > 0 || totalRows > Rows)
        {
            var scrollBarX = px + PanelW - 10;
            var barH = PanelH - GridY - 8;
            sb.Draw(white, new Rectangle(scrollBarX, py + GridY, 6, barH), new Color(20, 22, 38));
            var thumbH = Math.Max(20, barH * Rows / Math.Max(totalRows, 1));
            var thumbY = totalRows > 0 ? (int)(barH * scroll / totalRows) : 0;
            sb.Draw(white, new Rectangle(scrollBarX, py + GridY + thumbY, 6, thumbH), new Color(70, 75, 110));
        }

        // Hover tooltip
        if (_hoverItem != null && _font != null)
        {
            var m = Mouse.GetState();
            DrawItemTooltip(sb, white, _hoverItem, m.X, m.Y);
        }

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawSlot(SpriteBatch sb, Texture2D white, Rectangle r, InvItem? item)
    {
        if (_slotBg != null)
            _slotBg.Draw(sb, new Vector2(r.X + SlotW / 2f, r.Y + SlotH / 2f));
        else
        {
            sb.Draw(white, r, item != null ? new Color(24, 32, 22) : new Color(16, 18, 28));
            DrawBorder(sb, white, r, item != null ? new Color(55, 85, 55) : new Color(40, 44, 68));
        }

        if (item != null)
        {
            // Icon placeholder: first 2 letters
            _font?.Draw(sb, item.Name.Length >= 2 ? item.Name[..2] : item.Name,
                new Vector2(r.X + 6, r.Y + 9), new Color(200, 220, 200));

            // Quantity badge (if > 1)
            if (item.Quantity > 1 && _font != null)
            {
                var qty = item.Quantity > 9999 ? "999+" : item.Quantity.ToString();
                var sz  = _font.Measure(qty);
                var tx  = r.X + r.Width - (int)sz.X - 1;
                _font.Draw(sb, qty, new Vector2(tx, r.Y + r.Height - _font.LineHeight),
                    new Color(220, 220, 120));
            }
        }
    }

    private void DrawItemTooltip(SpriteBatch sb, Texture2D white, InvItem item, int mx, int my)
    {
        var lines = new[] { item.Name, $"ID: {item.Id}", $"Qty: {item.Quantity}" };
        var maxW  = lines.Max(l => (int)(_font!.Measure(l).X));
        var h     = lines.Length * (_font!.LineHeight + 2) + 8;
        var tr    = new Rectangle(mx + 14, my - h - 4, maxW + 12, h);
        sb.Draw(white, tr, new Color(0, 0, 0, 215));
        DrawBorder(sb, white, tr, new Color(90, 100, 150));
        for (var i = 0; i < lines.Length; i++)
            _font!.Draw(sb, lines[i], new Vector2(tr.X + 4, tr.Y + 4 + i * (_font.LineHeight + 2)),
                i == 0 ? Color.White : new Color(180, 185, 210));
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        var titleBar = new Rectangle((int)Position.X, (int)Position.Y, PanelW, 22);
        if (down && titleBar.Contains(x, y))
        {
            _dragging = true;
            _dragOff = new Vector2(x - Position.X, y - Position.Y);
            return true;
        }
        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        var tabItems = _items.Where(it => it.Tab == _activeTab).ToList();
        var maxScroll = Math.Max(0, (tabItems.Count + Cols - 1) / Cols - Rows);
        if (key == Keys.PageDown) { _scrollOffset[_activeTab] = Math.Min(_scrollOffset[_activeTab] + 1, maxScroll); return true; }
        if (key == Keys.PageUp)   { _scrollOffset[_activeTab] = Math.Max(0, _scrollOffset[_activeTab] - 1); return true; }
        return false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Rectangle SlotRectAt(int row, int col) =>
        new Rectangle(
            (int)Position.X + GridX + col * (SlotW + GapX),
            (int)Position.Y + GridY + row * (SlotH + GapY),
            SlotW, SlotH);

    private InvItem? HitTestItem(int x, int y)
    {
        var tabItems = _items.Where(it => it.Tab == _activeTab).ToList();
        var scroll   = _scrollOffset[_activeTab];
        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            var idx = scroll * Cols + r * Cols + c;
            if (idx >= tabItems.Count) return null;
            if (SlotRectAt(r, c).Contains(x, y)) return tabItems[idx];
        }
        return null;
    }

    private void LayoutButtons()
    {
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 4);
        var tabW = PanelW / 5;
        for (var i = 0; i < 5; i++)
            if (_tabBtns[i] != null)
                _tabBtns[i]!.Position = Position + new Vector2(i * tabW + tabW / 2f, 30);
    }

    private void SeedPlaceholder()
    {
        // Tab 0: Equip
        _items.Add(new InvItem { Id = 1302000, Name = "Sword",        Quantity = 1, Tab = 0 });
        _items.Add(new InvItem { Id = 1040002, Name = "Cloth Shirt",  Quantity = 1, Tab = 0 });
        _items.Add(new InvItem { Id = 1060002, Name = "Cloth Shorts", Quantity = 1, Tab = 0 });
        _items.Add(new InvItem { Id = 1072001, Name = "Leather Shoes",Quantity = 1, Tab = 0 });
        // Tab 1: Use
        _items.Add(new InvItem { Id = 2000000, Name = "Red Potion",   Quantity = 100, Tab = 1 });
        _items.Add(new InvItem { Id = 2000001, Name = "Orange Potion",Quantity = 50,  Tab = 1 });
        _items.Add(new InvItem { Id = 2000002, Name = "White Potion", Quantity = 20,  Tab = 1 });
        _items.Add(new InvItem { Id = 2001000, Name = "Mana Elixir",  Quantity = 30,  Tab = 1 });
        _items.Add(new InvItem { Id = 2000006, Name = "Power Elixir", Quantity = 5,   Tab = 1 });
        // Tab 3: Etc
        _items.Add(new InvItem { Id = 4000000, Name = "Blue Snail Shell", Quantity = 12, Tab = 3 });
        _items.Add(new InvItem { Id = 4000001, Name = "Snail Shell",     Quantity = 8,  Tab = 3 });
        _items.Add(new InvItem { Id = 4000002, Name = "Orange Mushroom Cap", Quantity = 3, Tab = 3 });
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
