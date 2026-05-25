using MapleClaude.Character;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Linq;

namespace MapleClaude.UI.Game;

/// <summary>
/// Item inventory window. Toggle with I.
/// Rebuilt against the authentic v95 <c>UIWindow2.img/Item</c> node: a layered
/// background (<c>backgrnd</c>/<c>backgrnd2</c>/<c>backgrnd3</c>), a 5-tab strip
/// (Equip | Use | Setup | Etc | Cash) whose buttons sit at the WZ-baked origins,
/// and a slot grid that shows 4×6 (24) slots in small mode and expands to 16×6
/// (96, four side-by-side pages) in full mode via the BtFull / BtSmall toggle.
/// Slots render real item icons resolved through <see cref="ItemIconLoader"/>.
///
/// WZ origin convention: a sub-sprite's origin already encodes its window-relative
/// position, so every background layer and Bt* button is drawn at the window's
/// top-left and the origin places it. Slot cells are baked into the background,
/// so only icons / highlights / counts are overlaid; the grid's first-cell origin
/// (<see cref="SlotBase"/>) is the one value WZ doesn't carry — tunable live.
/// </summary>
public sealed class ItemInventory : GamePanel
{
    // ── Public item record (kept stable for GameStage / Shop wiring) ─────────────
    public sealed class InvItem
    {
        public int    Id;
        public string Name     = string.Empty;
        public int    Quantity = 1;
        public int    Tab;        // 0=Equip 1=Use 2=Setup 3=Etc 4=Cash
        public int    Slot;       // 1-based inventory position
        public int    Grade;      // 0=none, 1..5 = potential rank (Quality dot)

        // Cached icon sprite (resolved lazily from the icon service).
        internal WzSprite? Icon;
        internal bool      IconResolved;
    }

    // ── Grid geometry (from CUIItem::GetItemSlotRect in the v95 client) ──────────
    // SetRect(36*col+10, 35*row+51, 36*col+42, 35*row+83): base (10,51), 32×32
    // cell box, column pitch 36, row pitch 35. Full mode wraps every 6 rows into
    // a new 4-column page (page = row/6).
    private const int CellW   = 36;   // column pitch
    private const int CellH   = 35;   // row pitch
    private const int IconBox = 32;   // visible slot box (icon + hit area)
    private const int SmallCols = 4;
    private const int Rows      = 6;  // visible rows in both modes
    private const int FullCols  = 16; // 4 horizontal pages × 4 cols
    private const int PageSlots = SmallCols * Rows;   // 24 slots per page

    // First-slot top-left relative to the window (CUIItem hardcodes (10,51)).
    // Exposed for the MAPLECLAUDE_DEBUG drag overlay (registered by GameStage).
    public Vector2 SlotBase = new(10, 51);

    private const int TabX0     = 9;    // Tab/enabled/0 origin → window+(9,26)
    private const int TabStride = 31;
    private const int TabY      = 26;
    private const int TabW      = 31;
    private const int TabH      = 22;
    private const int BottomY   = 267;  // BtCoin / action-button row
    private const int ScrollX   = 152;  // CCtrlScrollBar x (small mode)
    private const int MesoRight = 126;  // meso text right edge (CUIItem::Draw)
    private const int MesoY     = 268;  // meso text baseline y

    private static readonly string[] TabNames = ["Equip", "Use", "Setup", "Etc", "Cash"];

    // ── WZ assets ────────────────────────────────────────────────────────────────
    private readonly WzSprite?[] _bgSmall = new WzSprite?[3];
    private readonly WzSprite?[] _bgFull  = new WzSprite?[3];
    private readonly WzSprite?   _activeIcon;
    private readonly WzSprite?[] _tabEnabled  = new WzSprite?[5];
    private readonly WzSprite?[] _tabDisabled = new WzSprite?[5];
    private readonly WzSprite?[] _quality     = new WzSprite?[6];

    private readonly Button? _btCoin;
    private readonly Button? _btGather;
    private readonly Button? _btSort;
    private readonly Button? _btFull;
    private readonly Button? _btSmall;
    private readonly Button? _btCashshop;
    private readonly Button? _btClose;

    private readonly WzTextureLoader _loader;
    private readonly ItemIconLoader? _icons;
    private readonly BuiltInFont? _font;

    // ── State ────────────────────────────────────────────────────────────────────
    private int  _activeTab;
    private bool _fullMode;
    private bool _showSort;   // arrange button state: false → gather icon, true → sort icon
    private long _meso;
    private readonly int[] _scrollRow = new int[5];   // top visible row, per tab
    private readonly SortedDictionary<short, InvItem>[] _tabs =
    [
        new(), new(), new(), new(), new(),
    ];

    private InvItem? _hoverItem;
    private int _hoverPos = -1;
    private bool _dragging;
    private Vector2 _dragOff;
    private int _prevWheel;
    private int _viewW = 800, _viewH = 600;

    // ── Item tooltip + ghost drag ────────────────────────────────────────────────
    private readonly ItemTooltip? _tooltip;
    private bool _dragActive;            // an item icon is "picked up" onto the cursor
    private InvItem? _dragItem;
    private int _dragFromSlot = -1;
    private int _dragFromTab;
    private int _mouseX, _mouseY;

    public ItemInventory(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, ItemIconLoader? icons = null)
    {
        _loader = loader;
        _font   = font;
        _icons  = icons;
        IsVisible = false;
        Position = new Vector2(370, 50);

        var item = ui?.GetItem("UIWindow2.img/Item") as WzProperty
                   ?? ui?.GetItem("UIWindow.img/Item") as WzProperty;

        _bgSmall[0] = LoadCanvas(item, "backgrnd");
        _bgSmall[1] = LoadCanvas(item, "backgrnd2");
        _bgSmall[2] = LoadCanvas(item, "backgrnd3");
        _bgFull[0]  = LoadCanvas(item, "FullBackgrnd");
        _bgFull[1]  = LoadCanvas(item, "FullBackgrnd2");
        _bgFull[2]  = LoadCanvas(item, "FullBackgrnd3");
        _activeIcon = LoadCanvas(item, "activeIcon");

        var tab = item?.Get("Tab") as WzProperty;
        var tabEnabled  = tab?.Get("enabled")  as WzProperty;
        var tabDisabled = tab?.Get("disabled") as WzProperty;
        for (var i = 0; i < 5; i++)
        {
            _tabEnabled[i]  = LoadCanvas(tabEnabled,  i.ToString());
            _tabDisabled[i] = LoadCanvas(tabDisabled, i.ToString());
        }

        var quality = item?.Get("Quality") as WzProperty;
        for (var g = 0; g < 6; g++)
            _quality[g] = LoadCanvas(quality?.Get(g.ToString()) as WzProperty, "0");

        // BtGather and BtSort are the two states of one "arrange" button
        // (CUIItem::SetArrangeButton swaps the asset by sort state), so they share
        // the same origin (129,267); clicking toggles which is shown. The actual
        // gather/sort request to the server is a future wire-up.
        _btCoin     = MakeButton(item, "BtCoin");
        _btGather   = MakeButton(item, "BtGather", () => _showSort = true);
        _btSort     = MakeButton(item, "BtSort",   () => _showSort = false);
        _btFull     = MakeButton(item, "BtFull",  () => SetFullMode(true));
        _btSmall    = MakeButton(item, "BtSmall", () => SetFullMode(false));
        _btCashshop = MakeButton(item, "BtCashshop");

        // CUIItem has no own BtClose node — the close X is added by the shared CUIWnd
        // frame (Basic.img/BtClose3), exactly like CUIEquip. Anchor it top-right.
        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty closeRoot)
            _btClose = new Button(_loader, closeRoot) { OnClick = () => IsVisible = false };

        if (font != null && icons != null) _tooltip = new ItemTooltip(font, icons);

        LayoutButtons();
    }

    // ── Public API (preserved for GameStage / Shop) ──────────────────────────────

    public int ActiveTab => _activeTab;
    public long Meso { set => _meso = value; }

    public void AddItem(InvItem item)
    {
        if ((uint)item.Tab < 5) _tabs[item.Tab][(short)item.Slot] = item;
    }

    public void RemoveItem(int id)
    {
        foreach (var t in _tabs)
            foreach (var key in t.Where(kv => kv.Value.Id == id).Select(kv => kv.Key).ToList())
                t.Remove(key);
    }

    public void Clear()    { foreach (var t in _tabs) t.Clear(); }
    public void ClearAll() => Clear();

    /// <summary>Insert or replace the item occupying (tab, slot).</summary>
    public void SetSlot(int tab, int slot, InvItem item)
    {
        if ((uint)tab >= 5) return;
        item.Tab = tab;
        item.Slot = slot;
        _tabs[tab][(short)slot] = item;
    }

    public void RemoveSlot(int tab, int slot)
    {
        if ((uint)tab < 5) _tabs[tab].Remove((short)slot);
    }

    public void SetSlotQuantity(int tab, int slot, int qty)
    {
        if ((uint)tab < 5 && _tabs[tab].TryGetValue((short)slot, out var it)) it.Quantity = qty;
    }

    /// <summary>Move the item at (tab, fromSlot) to toSlot (0 = removed/dropped).</summary>
    public void MoveSlot(int tab, int fromSlot, int toSlot)
    {
        if ((uint)tab >= 5 || !_tabs[tab].TryGetValue((short)fromSlot, out var it)) return;
        _tabs[tab].Remove((short)fromSlot);
        if (toSlot == 0) return;          // dropped out of this tab
        it.Slot = toSlot;
        _tabs[tab][(short)toSlot] = it;
    }

    public InvItem? ItemAt(int tab, int slot) =>
        (uint)tab < 5 && _tabs[tab].TryGetValue((short)slot, out var it) ? it : null;

    /// <summary>Lowest 1-based slot in a tab not currently occupied (1..max), or -1 if full.
    /// Used to auto-place an item unequipped back into the Equip tab.</summary>
    public int FirstFreeSlot(int tab, int max = 96)
    {
        if ((uint)tab >= 5) return -1;
        var t = _tabs[tab];
        for (var s = 1; s <= max; s++)
            if (!t.ContainsKey((short)s)) return s;
        return -1;
    }

    /// <summary>All inventory items (for the shop sell list).</summary>
    public IReadOnlyList<InvItem> AllItems => _tabs.SelectMany(t => t.Values).ToList();

    /// <summary>Inventory position of a consumable in the Use tab, or -1.</summary>
    public int FindUseSlot(int itemId)
    {
        foreach (var kv in _tabs[1])
            if (kv.Value.Id == itemId) return kv.Key;
        return -1;
    }

    /// <summary>Raised on a double-click of an occupied slot: (tab, slot, itemId).</summary>
    public Action<int, int, int>? OnItemActivate { get; set; }

    /// <summary>Raised when an item is dragged to a different slot in the same tab: (tab, from, to).</summary>
    public Action<int, int, int>? OnMoveItem { get; set; }

    // ── Layout ───────────────────────────────────────────────────────────────────

    private int PanelW => _fullMode ? (_bgFull[0]?.Width ?? 594) : (_bgSmall[0]?.Width ?? 172);
    private int PanelH => _bgSmall[0]?.Height ?? 293;
    private int Cols   => _fullMode ? FullCols : SmallCols;

    public override void Relayout(int viewWidth, int viewHeight)
    {
        _viewW = viewWidth;
        _viewH = viewHeight;
        ClampOnScreen();
    }

    private void SetFullMode(bool full)
    {
        _fullMode = full;
        ClampOnScreen();
        LayoutButtons();
    }

    private void ClampOnScreen()
    {
        var x = MathHelper.Clamp(Position.X, 0, Math.Max(0, _viewW - PanelW));
        var y = MathHelper.Clamp(Position.Y, 0, Math.Max(0, _viewH - PanelH));
        Position = new Vector2(x, y);
    }

    private void LayoutButtons()
    {
        // Every Bt* origin encodes its window-relative spot (CLayoutMan places them
        // at (0,0) offset → the canvas origin), so drawing at the window top-left
        // reproduces the authentic layout. The arrange button's two states
        // (gather/sort) intentionally share the same origin.
        if (_btCoin     != null) _btCoin.Position     = Position;
        if (_btGather   != null) _btGather.Position   = Position;
        if (_btSort     != null) _btSort.Position     = Position;
        if (_btFull     != null) _btFull.Position     = Position;
        if (_btSmall    != null) _btSmall.Position    = Position;
        if (_btCashshop != null) _btCashshop.Position = Position;

        if (_btFull  != null) _btFull.Enabled  = !_fullMode;
        if (_btSmall != null) _btSmall.Enabled = _fullMode;

        // Close X: top-right of the active background (small 172w / full 594w).
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 6);
    }

    private IEnumerable<Button> VisibleButtons()
    {
        if (_btClose != null) yield return _btClose;
        if (_btCoin != null) yield return _btCoin;
        // Single arrange button (gather/sort are its two states).
        var arrange = _showSort ? _btSort : _btGather;
        if (arrange != null) yield return arrange;
        if (_fullMode)
        {
            if (_btSmall    != null) yield return _btSmall;
            if (_btCashshop != null) yield return _btCashshop;
        }
        else if (_btFull != null) yield return _btFull;
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        LayoutButtons();

        var m  = Mouse.GetState();
        var mp = new Vector2(m.X, m.Y);
        _mouseX = m.X; _mouseY = m.Y;

        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = mp - _dragOff;
            else { _dragging = false; ClampOnScreen(); }
        }

        // Mouse-wheel scroll (small mode only).
        if (!_fullMode && WindowRect.Contains(m.X, m.Y))
        {
            var delta = m.ScrollWheelValue - _prevWheel;
            if (delta != 0) ScrollBy(-Math.Sign(delta));
        }
        _prevWheel = m.ScrollWheelValue;

        var down = m.LeftButton == ButtonState.Pressed;
        foreach (var b in VisibleButtons()) b.Update(m.X, m.Y, down);

        // Finalize a picked-up item on mouse-up: drop onto the slot under the cursor (same tab).
        if (_dragActive && !down)
        {
            var (_, toPos) = HitTest(m.X, m.Y);
            if (toPos > 0 && toPos != _dragFromSlot)
                OnMoveItem?.Invoke(_dragFromTab, _dragFromSlot, toPos);
            _dragActive = false;
            _dragItem = null;
            _dragFromSlot = -1;
        }

        (_hoverItem, _hoverPos) = HitTest(m.X, m.Y);
    }

    private void ScrollBy(int rows)
    {
        var max = MaxScroll();
        _scrollRow[_activeTab] = Math.Clamp(_scrollRow[_activeTab] + rows, 0, max);
    }

    private int MaxScroll()
    {
        if (_fullMode || _tabs[_activeTab].Count == 0) return 0;
        var highest = _tabs[_activeTab].Keys.Max();    // sorted keys → last is the max position
        var totalRows = Math.Max(Rows, (highest + SmallCols - 1) / SmallCols);
        return Math.Max(0, totalRows - Rows);
    }

    // ── Draw ───────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var bg = _fullMode ? _bgFull : _bgSmall;
        if (bg[0] != null)
        {
            for (var i = 0; i < 3; i++) bg[i]?.Draw(sb, Position);
        }
        else
        {
            // Fallback chrome when the WZ background is unavailable.
            var r = WindowRect;
            sb.Draw(white, r, new Color(12, 14, 24, 240));
            DrawBorder(sb, white, r, new Color(60, 65, 100));
        }

        // Tabs: enabled sprite for the active one, disabled for the rest.
        for (var i = 0; i < 5; i++)
        {
            var spr = i == _activeTab ? _tabEnabled[i] : _tabDisabled[i];
            if (spr != null) spr.Draw(sb, Position);
            else
            {
                var tr = TabRect(i);
                sb.Draw(white, tr, i == _activeTab ? new Color(70, 90, 140) : new Color(28, 30, 46));
                _font?.Draw(sb, TabNames[i][..1], new Vector2(tr.X + 11, tr.Y + 5),
                    i == _activeTab ? Color.White : new Color(140, 145, 170));
            }
        }

        // Hover highlight (drawn under the icon). activeIcon origin (2,2) inset,
        // so drawing at the cell top-left frames the 32-box with a 2px margin.
        if (_hoverPos > 0 && CellForPosition(_hoverPos, out var hcell))
            _activeIcon?.Draw(sb, hcell);

        // Occupied slots.
        foreach (var (pos, it) in _tabs[_activeTab])
        {
            if (!CellForPosition(pos, out var cell)) continue;
            // The picked-up item is drawn on the cursor, not in its home slot.
            if (_dragActive && _dragFromTab == _activeTab && pos == _dragFromSlot) continue;
            DrawItemIcon(sb, white, it, cell);
            if (it.Grade is > 0 and < 6 && _quality[it.Grade] != null)
                DrawAt(sb, _quality[it.Grade], cell + new Vector2(1, 1));
            if (it.Quantity > 1) DrawQuantity(sb, it.Quantity, cell);
        }

        // Small-mode scrollbar thumb.
        if (!_fullMode)
        {
            var max = MaxScroll();
            if (max > 0)
            {
                var barX = (int)Position.X + ScrollX;
                var barY = (int)(Position.Y + SlotBase.Y);
                var barH = Rows * CellH;
                sb.Draw(white, new Rectangle(barX, barY, 5, barH), new Color(18, 20, 32, 180));
                var totalRows = max + Rows;
                var thumbH = Math.Max(16, barH * Rows / totalRows);
                var thumbY = barY + (barH - thumbH) * _scrollRow[_activeTab] / max;
                sb.Draw(white, new Rectangle(barX, thumbY, 5, thumbH), new Color(90, 95, 130));
            }
        }

        foreach (var b in VisibleButtons()) b.Draw(sb);

        // Meso: right-aligned ending at x=126, y=268 (CUIItem::Draw → DrawTextA),
        // small black font.
        if (_font != null)
        {
            var meso = _meso.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            var w = _font.Measure(meso).X;
            _font.Draw(sb, meso, new Vector2(Position.X + MesoRight - w, Position.Y + MesoY),
                new Color(40, 40, 40));
        }

        // Ghost: the picked-up item's icon follows the cursor (so it reads as "held").
        if (_dragActive && _dragItem != null)
        {
            var icon = ResolveIcon(_dragItem);
            if (icon != null)
                icon.Draw(sb, new Vector2(_mouseX - icon.Width / 2f, _mouseY - icon.Height / 2f) + icon.Origin,
                    Microsoft.Xna.Framework.Graphics.SpriteEffects.None, new Color(255, 255, 255, 185));
            else
                sb.Draw(white, new Rectangle(_mouseX - 14, _mouseY - 14, 28, 28), new Color(80, 90, 120, 170));
        }
        else if (_hoverItem != null)
        {
            if (_tooltip != null)
                _tooltip.Draw(sb, white, _hoverItem.Id, _hoverItem.Name, _hoverItem.Grade,
                    _hoverItem.Quantity, _mouseX, _mouseY, _viewW, _viewH);
            else if (_font != null)
                DrawTooltip(sb, white, _hoverItem, _mouseX, _mouseY);
        }
    }

    private void DrawItemIcon(SpriteBatch sb, Texture2D white, InvItem it, Vector2 cell)
    {
        var icon = ResolveIcon(it);
        if (icon != null)
        {
            // Centre the icon in the 32×32 slot box, honouring its (bottom-anchored) origin.
            var pos = cell + new Vector2(
                (IconBox - icon.Width) / 2f + icon.Origin.X,
                (IconBox - icon.Height) / 2f + icon.Origin.Y);
            icon.Draw(sb, pos);
        }
        else
        {
            // Placeholder when the icon can't be resolved.
            var r = new Rectangle((int)cell.X + 1, (int)cell.Y + 1, IconBox - 2, IconBox - 2);
            sb.Draw(white, r, new Color(40, 46, 66, 220));
            _font?.Draw(sb, it.Name.Length >= 2 ? it.Name[..2] : it.Name,
                new Vector2(r.X + 4, r.Y + 8), new Color(200, 210, 225));
        }
    }

    private WzSprite? ResolveIcon(InvItem it)
    {
        if (it.IconResolved) return it.Icon;
        it.Icon = _icons?.LoadIcon(it.Id);
        it.IconResolved = true;
        return it.Icon;
    }

    private void DrawQuantity(SpriteBatch sb, int qty, Vector2 cell)
    {
        if (_font == null) return;
        var s  = qty > 99999 ? "99999+" : qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sz = _font.Measure(s);
        _font.Draw(sb, s, new Vector2(cell.X + IconBox - sz.X - 1, cell.Y + IconBox - _font.LineHeight),
            new Color(245, 240, 200));
    }

    private void DrawTooltip(SpriteBatch sb, Texture2D white, InvItem item, int mx, int my)
    {
        var lines = new[] { item.Name, $"ID: {item.Id}", $"Qty: {item.Quantity}" };
        var maxW  = lines.Max(l => (int)_font!.Measure(l).X);
        var h     = lines.Length * (_font!.LineHeight + 2) + 8;
        var tr    = new Rectangle(mx + 14, my - h - 4, maxW + 12, h);
        sb.Draw(white, tr, new Color(0, 0, 0, 215));
        DrawBorder(sb, white, tr, new Color(90, 100, 150));
        for (var i = 0; i < lines.Length; i++)
            _font!.Draw(sb, lines[i], new Vector2(tr.X + 4, tr.Y + 4 + i * (_font.LineHeight + 2)),
                i == 0 ? Color.White : new Color(180, 185, 210));
    }

    private static void DrawAt(SpriteBatch sb, WzSprite? s, Vector2 topLeft)
    {
        if (s != null) s.Draw(sb, topLeft + s.Origin);
    }

    // ── Input ────────────────────────────────────────────────────────────────────

    private double _lastClickTime;
    private InvItem? _lastClickItem;

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        foreach (var b in VisibleButtons())
            if (b.HandleMouseButton(x, y, down)) return true;

        if (down)
        {
            // Close hotspot (top-right; the X is baked into the background art).
            if (CloseRect.Contains(x, y)) { IsVisible = false; return true; }

            // Tab switch.
            for (var i = 0; i < 5; i++)
                if (TabRect(i).Contains(x, y)) { _activeTab = i; return true; }

            // Slot click: a double-click activates (equip/use); a single click picks the item up
            // onto the cursor (ghost) so it can be dragged to another slot (dropped in Update).
            var (hit, hitPos) = HitTest(x, y);
            if (hit != null)
            {
                var now = Environment.TickCount64 / 1000.0;
                if (ReferenceEquals(hit, _lastClickItem) && now - _lastClickTime < 0.4)
                {
                    OnItemActivate?.Invoke(hit.Tab, hit.Slot, hit.Id);
                    _lastClickItem = null;
                    _dragActive = false;
                }
                else
                {
                    _lastClickItem = hit;
                    _lastClickTime = now;
                    _dragActive = true;
                    _dragItem = hit;
                    _dragFromSlot = hitPos;
                    _dragFromTab = _activeTab;
                }
                return true;
            }

            // Title strip → drag.
            if (TitleRect.Contains(x, y))
            {
                _dragging = true;
                _dragOff = new Vector2(x - Position.X, y - Position.Y);
                return true;
            }
        }

        return WindowRect.Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        switch (key)
        {
            case Keys.Escape:   IsVisible = false; return true;
            case Keys.PageDown: ScrollBy(1);  return true;
            case Keys.PageUp:   ScrollBy(-1); return true;
            case Keys.Tab:      _activeTab = (_activeTab + 1) % 5; return true;
            default: return false;
        }
    }

    // ── Slot ↔ position mapping ───────────────────────────────────────────────────

    /// <summary>Top-left of the cell for a 1-based inventory position, if visible.</summary>
    private bool CellForPosition(int pos, out Vector2 cell)
    {
        cell = default;
        if (pos < 1) return false;
        var abs = pos - 1;
        int col, row;
        if (_fullMode)
        {
            if (abs >= FullCols * Rows) return false;     // beyond the 96-slot page set
            var page = abs / PageSlots;
            var idx  = abs % PageSlots;
            col = page * SmallCols + idx % SmallCols;
            row = idx / SmallCols;
        }
        else
        {
            var r = abs / SmallCols;
            if (r < _scrollRow[_activeTab] || r >= _scrollRow[_activeTab] + Rows) return false;
            col = abs % SmallCols;
            row = r - _scrollRow[_activeTab];
        }
        cell = new Vector2(Position.X + SlotBase.X + col * CellW, Position.Y + SlotBase.Y + row * CellH);
        return true;
    }

    /// <summary>The item (and its position) under a screen point, or (null,-1).</summary>
    private (InvItem? item, int pos) HitTest(int x, int y)
    {
        var cols = Cols;
        for (var vr = 0; vr < Rows; vr++)
        for (var vc = 0; vc < cols; vc++)
        {
            // Hit area is the 32×32 slot box (gaps between cells register as empty,
            // matching CUIItem::GetSlotPositionFromPoint).
            var cx = (int)(Position.X + SlotBase.X + vc * CellW);
            var cy = (int)(Position.Y + SlotBase.Y + vr * CellH);
            if (x < cx || x >= cx + IconBox || y < cy || y >= cy + IconBox) continue;

            int abs;
            if (_fullMode)
            {
                var page = vc / SmallCols;
                var cInPage = vc % SmallCols;
                abs = page * PageSlots + vr * SmallCols + cInPage;
            }
            else
            {
                abs = (_scrollRow[_activeTab] + vr) * SmallCols + vc;
            }
            var pos = abs + 1;
            return (ItemAt(_activeTab, pos), pos);
        }
        return (null, -1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Rectangle WindowRect => new((int)Position.X, (int)Position.Y, PanelW, PanelH);
    private Rectangle TitleRect  => new((int)Position.X, (int)Position.Y, PanelW, 22);
    private Rectangle CloseRect  => new((int)Position.X + PanelW - 20, (int)Position.Y + 5, 14, 14);
    private Rectangle TabRect(int i) =>
        new((int)Position.X + TabX0 + i * TabStride, (int)Position.Y + TabY, TabW, TabH);

    private WzSprite? LoadCanvas(WzProperty? root, string name) =>
        root?.Get(name) is WzCanvas c ? _loader.Load(c) : null;

    private Button? MakeButton(WzProperty? item, string name, Action? onClick = null)
    {
        if (item?.Get(name) is not WzProperty root) return null;
        var b = new Button(_loader, root);
        if (onClick != null) b.OnClick = onClick;
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
