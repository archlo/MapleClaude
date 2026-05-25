using MapleClaude.Character;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Equipment window — the v95 <c>CUIEquip</c>. Toggle with E.
///
/// A faithful rebuild of the real client: the authentic WZ frame from
/// <c>UI/UIWindow2.img/Equip/character</c> (backgrnd + slot-cell panel + the labelled
/// empty grid), the exact slot layout from the client, and real item icons drawn in
/// each worn slot. There is no character preview inside the window — v95 doesn't have one.
///
/// Slots are keyed by the positive body-part index (1..51), matching the inventory
/// wire data and <see cref="MapleClaude.Domain.AvatarLook"/>.HairEquip. Cell layout
/// follows the client's grid: <c>x = 33*col + 10</c>, <c>y = 33*row + 27</c>, each cell 32×32.
/// </summary>
public sealed class EquipInventory : GamePanel
{
    // Visible window bounds (the backgrnd canvas is 184×290; the window content box is 184×304).
    private const int PanelW   = 184;
    private const int PanelH   = 290;
    private const int TitleH   = 20;   // draggable titlebar height (baked into backgrnd art)
    private const int Cell     = 32;   // slot icon cell

    // Body-part -> cell top-left (content space, relative to the window top-left).
    // Verified against the v95 CUIEquip slot table (m_sEqSlotInfo).
    private static readonly Dictionary<int, Point> SlotPos = new()
    {
        [1]  = new Point(43,  27),   // Cap
        [2]  = new Point(43,  60),   // Face accessory
        [3]  = new Point(43,  93),   // Eye accessory
        [4]  = new Point(109, 93),   // Earring
        [5]  = new Point(43,  126),  // Clothes (top / overall)
        [6]  = new Point(43,  159),  // Pants
        [7]  = new Point(76,  192),  // Shoes
        [8]  = new Point(10,  159),  // Gloves
        [9]  = new Point(10,  126),  // Cape
        [10] = new Point(142, 126),  // Shield
        [11] = new Point(109, 126),  // Weapon
        [12] = new Point(109, 159),  // Ring 1
        [13] = new Point(142, 159),  // Ring 2
        [15] = new Point(109, 60),   // Ring 3
        [16] = new Point(142, 60),   // Ring 4
        [17] = new Point(76,  126),  // Pendant
        [18] = new Point(10,  225),  // Taming mob (mount)
        [19] = new Point(43,  225),  // Saddle
        [20] = new Point(76,  225),  // Mob equip
        [49] = new Point(10,  60),   // Medal
        [50] = new Point(76,  159),  // Belt
        [51] = new Point(142, 93),   // Shoulder
    };

    // ── Equipment data ───────────────────────────────────────────────────────
    // Key = positive body part, value = the worn item (id drives the icon, name the tooltip).
    private readonly Dictionary<int, (int ItemId, string Name)> _equipped = new();

    // ── WZ assets ────────────────────────────────────────────────────────────
    private readonly WzSprite? _background;   // outer frame + titlebar
    private readonly WzSprite? _background2;  // inner slot-cell panel
    private readonly WzSprite? _background3;  // labelled empty grid
    private readonly WzSprite? _background3Dual; // variant when an overall is worn
    private readonly Button?   _btClose;
    private readonly Button?   _btPet;
    private readonly Button?   _btSlot;
    private readonly List<Button> _allButtons = new();
    private readonly ItemIconLoader _icons;
    private readonly BuiltInFont? _font;
    private readonly ItemTooltip? _tooltip;
    private int _viewW = 800, _viewH = 600;

    // ── State ────────────────────────────────────────────────────────────────
    private int  _tooltipPart = -1;
    private bool _dragging;
    private Vector2 _dragOff;
    private int    _lastClickPart = -1;
    private double _lastClickTime;

    /// <summary>Raised on a double-click of a worn slot — the body part to unequip
    /// (CUIEquip::OnMouseButton → CDraggableItem::GetOffEquipItem).</summary>
    public Action<int>? OnUnequip { get; set; }

    public EquipInventory(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, ItemIconLoader icons)
    {
        _font  = font;
        _icons = icons;
        if (font != null) _tooltip = new ItemTooltip(font, icons);
        IsVisible = false;
        Position  = new Vector2(560, 60);

        var character = ui?.GetItem("UIWindow2.img/Equip/character") as WzProperty;
        _background      = character?.Get("backgrnd")       is WzCanvas a ? loader.Load(a) : null;
        _background2     = character?.Get("backgrnd2")      is WzCanvas b ? loader.Load(b) : null;
        _background3     = character?.Get("backgrnd3")      is WzCanvas c ? loader.Load(c) : null;
        _background3Dual = character?.Get("backgrnd3_dual") is WzCanvas d ? loader.Load(d) : null;

        // Bottom-row buttons. Their canvas origins bake the in-window position, so they
        // draw correctly at the window top-left. They are decorative here (no handler).
        _btPet  = MakeButton(loader, character?.Get("BtPet")  as WzProperty);
        _btSlot = MakeButton(loader, character?.Get("BtSlot") as WzProperty);

        // Standard CUIWnd close button.
        var closeRoot = ui?.GetItem("Basic.img/BtClose3") as WzProperty;
        if (closeRoot != null)
        {
            _btClose = new Button(loader, closeRoot) { OnClick = () => IsVisible = false };
            _allButtons.Add(_btClose);
        }

        LayoutButtons();
    }

    private Button? MakeButton(WzTextureLoader loader, WzProperty? root)
    {
        if (root is null) return null;
        var b = new Button(loader, root);
        _allButtons.Add(b);
        return b;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Show <paramref name="itemId"/> worn at the given body part. Cash slots
    /// (bodyPart + 100) and negative wire positions fold onto the base slot.</summary>
    public void SetEquipped(int bodyPart, int itemId, string name)
    {
        var part = Normalize(bodyPart);
        if (SlotPos.ContainsKey(part))
        {
            _equipped[part] = (itemId, name);
        }
    }

    public void RemoveEquipped(int bodyPart) => _equipped.Remove(Normalize(bodyPart));

    public bool TryGetEquipped(int bodyPart, out int itemId, out string name)
    {
        if (_equipped.TryGetValue(Normalize(bodyPart), out var e))
        {
            itemId = e.ItemId;
            name   = e.Name;
            return true;
        }
        itemId = 0;
        name   = string.Empty;
        return false;
    }

    public void ClearEquipped() => _equipped.Clear();

    // Wire positions arrive as positive body parts; cash worn is bodyPart+100 and some
    // ops carry the slot as a negative body part. Fold all onto the base 1..51 index.
    private static int Normalize(int bodyPart)
    {
        var p = Math.Abs(bodyPart);
        if (p > 100) p -= 100;
        return p;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        LayoutButtons();

        var m  = Mouse.GetState();
        var mp = new Vector2(m.X, m.Y);
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = mp - _dragOff;
            else _dragging = false;
        }

        foreach (var b in _allButtons) b.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);

        // Tooltip: the worn slot under the cursor.
        _tooltipPart = -1;
        foreach (var (part, _) in _equipped)
        {
            if (CellRect(part).Contains(m.X, m.Y)) { _tooltipPart = part; break; }
        }
    }

    // ── Draw ─────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_background != null)
        {
            // All four canvases carry origins that place them in window-content space,
            // so each draws correctly at the window top-left.
            _background.Draw(sb, Position);
            _background2?.Draw(sb, Position);
            (WearingOverall() ? _background3Dual ?? _background3 : _background3)?.Draw(sb, Position);
        }
        else
        {
            DrawFallbackFrame(sb, white);
        }

        // Worn item icons, centred in their 32×32 cells.
        foreach (var (part, e) in _equipped)
        {
            if (!SlotPos.ContainsKey(part)) continue;
            var cell = CellRect(part);
            var icon = _icons.LoadIcon(e.ItemId);
            if (icon != null)
            {
                var drawPos = new Vector2(
                    cell.X + (Cell - icon.Width) / 2f,
                    cell.Y + (Cell - icon.Height) / 2f) + icon.Origin;
                icon.Draw(sb, drawPos);
            }
            else if (_background == null)
            {
                // Fallback only matters when the frame art is missing.
                _font?.Draw(sb, e.Name.Length >= 2 ? e.Name[..2] : e.Name,
                    new Vector2(cell.X + 7, cell.Y + 9), new Color(200, 220, 200));
            }
        }

        foreach (var b in _allButtons) b.Draw(sb);

        if (_tooltipPart >= 0 && _equipped.TryGetValue(_tooltipPart, out var hov))
        {
            var m = Mouse.GetState();
            if (_tooltip != null)
                _tooltip.Draw(sb, white, hov.ItemId, hov.Name, 0, 1, m.X, m.Y, _viewW, _viewH);
            else if (_font != null)
                DrawTooltip(sb, white, hov.Name);
        }
    }

    public override void Relayout(int viewWidth, int viewHeight)
    {
        _viewW = viewWidth;
        _viewH = viewHeight;
    }

    private void DrawTooltip(SpriteBatch sb, Texture2D white, string text)
    {
        var m  = Mouse.GetState();
        var sz = _font!.Measure(text);
        var tr = new Rectangle(m.X + 14, m.Y - 18, (int)sz.X + 10, _font.LineHeight + 6);
        sb.Draw(white, tr, new Color(0, 0, 0, 215));
        DrawBorder(sb, white, tr, new Color(120, 110, 70));
        _font.Draw(sb, text, new Vector2(tr.X + 5, tr.Y + 3), Color.White);
    }

    private void DrawFallbackFrame(SpriteBatch sb, Texture2D white)
    {
        var r = new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH);
        sb.Draw(white, r, new Color(12, 14, 24, 240));
        DrawBorder(sb, white, r, new Color(60, 65, 100));
        sb.Draw(white, new Rectangle(r.X, r.Y, PanelW, TitleH), new Color(15, 18, 36));
        _font?.Draw(sb, "EQUIPMENT", new Vector2(r.X + 12, r.Y + 4), new Color(220, 200, 150));
        foreach (var (_, p) in SlotPos)
        {
            var cell = new Rectangle((int)Position.X + p.X, (int)Position.Y + p.Y, Cell, Cell);
            sb.Draw(white, cell, new Color(18, 20, 32));
            DrawBorder(sb, white, cell, new Color(45, 50, 75));
        }
    }

    // An overall (longcoat, category 105) occupies the Clothes slot and merges top+bottom.
    private bool WearingOverall() =>
        _equipped.TryGetValue(5, out var top) && top.ItemId / 10000 == 105;

    private Rectangle CellRect(int part)
    {
        var p = SlotPos[part];
        return new Rectangle((int)Position.X + p.X, (int)Position.Y + p.Y, Cell, Cell);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        // Double-click a worn slot → unequip it (the item moves to the first free Equip-inventory slot).
        if (down)
        {
            foreach (var (part, _) in _equipped)
            {
                if (!CellRect(part).Contains(x, y)) continue;
                var now = Environment.TickCount64 / 1000.0;
                if (part == _lastClickPart && now - _lastClickTime < 0.4)
                {
                    OnUnequip?.Invoke(part);
                    _lastClickPart = -1;
                }
                else { _lastClickPart = part; _lastClickTime = now; }
                return true;
            }
        }

        var titleBar = new Rectangle((int)Position.X, (int)Position.Y, PanelW, TitleH);
        if (down && titleBar.Contains(x, y))
        {
            _dragging = true;
            _dragOff  = new Vector2(x - Position.X, y - Position.Y);
            return true;
        }
        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (key == Keys.Escape && IsVisible) { IsVisible = false; return true; }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void LayoutButtons()
    {
        // BtPet / BtSlot origins bake their content position, so anchor them to the window top-left.
        if (_btPet  != null) _btPet.Position  = Position;
        if (_btSlot != null) _btSlot.Position = Position;
        if (_btClose != null) _btClose.Position = Position + new Vector2(162, 6);
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
