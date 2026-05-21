using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Equipment inventory panel. Toggle with E.
/// Shows the paperdoll slot layout: Hat, Face, Eye, Ear, Top, Bottom,
/// Shoes, Gloves, Cape, Ring, Pendant, Belt, Weapon, Shield/Secondary, Medal.
/// Each slot is a labelled drop-zone; icons appear when wired to inventory data.
/// WZ: UIWindow.img/Equip/
/// </summary>
public sealed class EquipInventory : GamePanel
{
    // ── Slot definition ────────────────────────────────────────────────────────
    private sealed class EquipSlot
    {
        public string Label;
        public Point  Offset;   // pixels from panel top-left (content area)
        public string Key;      // inventory key (server-side)
        public EquipSlot(string label, Point offset, string key)
            { Label = label; Offset = offset; Key = key; }
    }

    // Paperdoll layout for 800×600; panel starts at Position (550, 50)
    // Each slot is 32×32; layout based on v95 reference equip window
    private static readonly EquipSlot[] Slots =
    [
        // Head column
        new("Hat",      new Point(69,  22),  "Hat"),
        new("Face",     new Point(35,  60),  "FaceAcc"),
        new("Eye",      new Point(69,  60),  "EyeAcc"),
        new("Ear",      new Point(103, 60),  "Earring"),
        // Torso column
        new("Top",      new Point(69,  98),  "Top"),
        new("Overall",  new Point(69,  136), "Overall"),
        new("Bottom",   new Point(69,  174), "Bottom"),
        // Extremities
        new("Shoes",    new Point(69,  212), "Shoes"),
        new("Gloves",   new Point(35,  174), "Gloves"),
        new("Cape",     new Point(103, 98),  "Cape"),
        // Accessories
        new("Ring 1",   new Point(35,  98),  "Ring1"),
        new("Ring 2",   new Point(35,  136), "Ring2"),
        new("Pendant",  new Point(103, 136), "Pendant"),
        new("Belt",     new Point(69,  250), "Belt"),
        // Weapons
        new("Weapon",   new Point(35,  212), "Weapon"),
        new("Shield",   new Point(103, 212), "Shield"),
        new("Medal",    new Point(103, 250), "Medal"),
    ];

    // ── Equipment data ────────────────────────────────────────────────────────
    // Key = slot key (from EquipSlot.Key), Value = item name shown in tooltip
    private readonly Dictionary<string, string> _equipped = new();

    // ── WZ assets ─────────────────────────────────────────────────────────────
    private readonly WzSprite? _background;
    private readonly WzSprite? _slotBg;
    private readonly Button?   _btClose;
    private readonly List<Button> _allButtons = new();

    // ── State ──────────────────────────────────────────────────────────────────
    private string? _tooltipSlot;
    private bool  _dragging;
    private Vector2 _dragOff;

    private const int PanelW  = 172;
    private const int PanelH  = 320;
    private const int SlotSize = 32;

    private readonly BuiltInFont? _font;

    public EquipInventory(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(550, 50);

        var equip = ui?.GetItem("UIWindow.img/Equip") as WzProperty;
        _background = equip?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;
        _slotBg     = equip?.Get("slot")     is WzCanvas sc ? loader.Load(sc) : null;

        var closeRoot = equip?.Get("BtClose") as WzProperty;
        if (closeRoot != null)
        {
            _btClose = new Button(loader, closeRoot)
            {
                OnClick = () => IsVisible = false,
            };
            _allButtons.Add(_btClose);
        }

        LayoutButtons();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void Equip(string slotKey, string itemName) => _equipped[slotKey] = itemName;
    public void Unequip(string slotKey) => _equipped.Remove(slotKey);

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

        // Tooltip: find hovered slot
        _tooltipSlot = null;
        foreach (var slot in Slots)
        {
            var sr = SlotRect(slot);
            if (sr.Contains((int)mp.X, (int)mp.Y))
            {
                _tooltipSlot = slot.Key;
                break;
            }
        }
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

        // Title
        sb.Draw(white, new Rectangle(px, py, PanelW, 22), new Color(15, 18, 36));
        _font?.Draw(sb, "Equipment", new Vector2(px + 54, py + 5), new Color(220, 200, 150));

        // Slots
        foreach (var slot in Slots) DrawSlot(sb, white, slot);

        // Tooltip
        if (_tooltipSlot != null && _font != null)
        {
            var hasItem = _equipped.TryGetValue(_tooltipSlot, out var itemName);
            var tip = hasItem ? itemName! : $"[{_tooltipSlot}]";
            var m   = Mouse.GetState();
            var sz  = _font.Measure(tip);
            var tr  = new Rectangle(m.X + 12, m.Y - 18, (int)sz.X + 8, _font.LineHeight + 4);
            sb.Draw(white, tr, new Color(0, 0, 0, 210));
            DrawBorder(sb, white, tr, new Color(100, 100, 160));
            _font.Draw(sb, tip, new Vector2(tr.X + 4, tr.Y + 2), Color.White);
        }

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawSlot(SpriteBatch sb, Texture2D white, EquipSlot slot)
    {
        var r = SlotRect(slot);
        var hasItem = _equipped.ContainsKey(slot.Key);

        if (_slotBg != null)
            _slotBg.Draw(sb, new Vector2(r.X + SlotSize / 2f, r.Y + SlotSize / 2f));
        else
        {
            sb.Draw(white, r, hasItem ? new Color(30, 45, 35) : new Color(18, 20, 32));
            DrawBorder(sb, white, r, hasItem ? new Color(60, 110, 70) : new Color(45, 50, 75));
        }

        if (hasItem && _equipped.TryGetValue(slot.Key, out var name))
        {
            // Item icon placeholder (first 2 chars of name)
            _font?.Draw(sb, name.Length >= 2 ? name[..2] : name,
                new Vector2(r.X + 7, r.Y + 9), new Color(200, 220, 200));
        }
        else
        {
            // Slot label (tiny, bottom of slot)
            if (_font != null)
            {
                var lbl = slot.Label.Length > 4 ? slot.Label[..4] : slot.Label;
                _font.Draw(sb, lbl, new Vector2(r.X + 1, r.Y + 20), new Color(70, 75, 100));
            }
        }
    }

    private Rectangle SlotRect(EquipSlot slot) =>
        new Rectangle(
            (int)Position.X + slot.Offset.X,
            (int)Position.Y + slot.Offset.Y,
            SlotSize, SlotSize);

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

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void LayoutButtons()
    {
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 4);
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
