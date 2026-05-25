using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// In-game Key Configuration window — authentic v95 <c>CUIKeyConfig</c>, drawn from
/// <c>UIWindow.img/KeyConfig</c>. The <c>backgrnd</c> canvas is the whole labelled
/// keyboard plus the icon-palette grid below it; 32×32 action icons are laid onto
/// bound keys at the positions from <see cref="KeyConfigLayout"/> (a port of
/// <c>CalcKeyIconPosInfo</c>). Editing is drag-and-drop: pick an icon out of the
/// bottom palette and drop it on a key to bind, or drag a key's icon off to unbind.
///
/// The binding store is the server's model: a <see cref="FuncKeyMapped"/> array
/// indexed by DInput scancode (89 slots), so <c>FuncKeyMappedInit</c> wires straight
/// in and <c>FuncKeyMappedModified</c> saves straight out. Arrow-key movement is
/// client-only and lives outside this map.
/// </summary>
public sealed class KeyConfig : GamePanel
{
    // KeyAction is the dispatch vocabulary used by GameStage. The integer values
    // are the v95 MENU ids (0..29) and BASICACTION ids (50..54) carried by the
    // func-key map, so a Menu/BasicAction binding casts straight to a KeyAction.
    public enum KeyAction
    {
        None              = -1,
        Equipment         = 0,
        Items             = 1,
        Stats             = 2,
        Skills            = 3,
        Friends           = 4,
        WorldMap          = 5,
        MapleChat         = 6,
        MiniMap           = 7,
        QuestLog          = 8,
        KeyBindings       = 9,
        Say               = 10,
        Whisper           = 11,
        PartyChat         = 12,
        FriendsChat       = 13,
        Menu              = 14,
        QuickSlots        = 15,
        ToggleChat        = 16,
        Guild             = 17,
        GuildChat         = 18,
        Party             = 19,
        Notifier          = 20,
        MapleNews         = 21,
        CashShop          = 22,
        AllianceChat      = 23,
        BuddyChat         = 24,
        ManageLegion      = 25,
        Medals            = 26,
        BossParty         = 27,
        CharInfo          = 44,
        ChangeChannel     = 45,
        MainMenu          = 46,
        Screenshot        = 47,
        // In-game actions (BASICACTION ids 50..54)
        PickUp            = 50,
        Sit               = 51,
        Attack            = 52,
        Jump              = 53,
        Interact          = 54,
        // Client-only movement (not in the server map)
        MoveLeft          = 1002,
        MoveRight         = 1003,
    }

    public const int MapSize = 89;

    private readonly FuncKeyMapped[] _map = new FuncKeyMapped[MapSize];
    private readonly FuncKeyMapped[] _mapOnOpen = new FuncKeyMapped[MapSize];

    // ── WZ assets ───────────────────────────────────────────────────────────────
    private readonly WzTextureLoader _loader;
    private readonly WzProperty? _kc;            // UIWindow.img/KeyConfig (frame/buttons/key/layout)
    private readonly WzProperty? _iconRoot;      // UIWindow2.img/KeyConfig/icon (correct v95 action icons)

    // Menu id 22 is "Monster Book" — a feature v95 removed. UIWindow2 omits the icon; the palette
    // skips this slot so the dropped feature never surfaces (and we never fall back to UIWindow art).
    private const int MonsterBookMenuId = 22;
    // v95 KeyConfig window is layered in UIWindow2: dark outer frame + white inner panel + the
    // keyboard art (with modern orange labels on the fixed keys). Each layer's origin encodes its
    // inset from the window top-left. UIWindow's single backgrnd is the last-resort fallback.
    private readonly WzSprite? _bg;
    private readonly WzSprite? _bg2;
    private readonly WzSprite? _bg3;
    private readonly Dictionary<int, WzSprite?> _iconCache = new();
    private readonly BuiltInFont? _font;

    // ── Buttons ───────────────────────────────────────────────────────────────
    private readonly Button? _btClose, _btHelp, _btOk, _btCancel, _btDefault, _btDelete, _btQuickSlot;
    private readonly List<Button> _allButtons = new();

    // ── Drag state ──────────────────────────────────────────────────────────────
    // Sticky drag: a click picks an icon up; the next click drops it (no holding).
    private bool _dragActive;
    private FuncKeyMapped _dragIcon;
    private int _dragFromScancode = -1;     // -1 = came from palette / nowhere
    private Point _dragMouse;

    // ── Window drag ───────────────────────────────────────────────────────────
    private bool _windowDrag;
    private Vector2 _windowDragOff;

    // ── Confirm overlay (Default / Delete) ──────────────────────────────────────
    private enum Confirm { None, Default, Delete }
    private Confirm _confirm = Confirm.None;

    private readonly int _panelW;   // window size, from the outer-frame background
    private readonly int _panelH;

    /// <summary>Persist hook (disk). Fired on OK and on Default/Delete apply.</summary>
    public event Action? OnBindingsChanged;

    /// <summary>Save-to-server hook: the changed slots since the window opened
    /// (index + new mapping). Wired by the stage to send <c>FuncKeyMappedModified</c>.</summary>
    public event Action<IReadOnlyList<(int index, FuncKeyMapped fk)>>? OnSaveToServer;

    /// <summary>Open the QuickSlot key-config sub-panel (BtQuickSlot). Wired by the stage.</summary>
    public Action? OnOpenQuickSlot;

    public KeyConfig(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _loader = loader;
        _font = font;
        IsVisible = false;
        Position = new Vector2(200, 150);

        _kc = ui?.GetItem("UIWindow.img/KeyConfig") as WzProperty;
        var kc2 = ui?.GetItem("UIWindow2.img/KeyConfig") as WzProperty;
        // Action icons + the window background come from UIWindow2 (the modern v95 art). UIWindow's
        // backgrnd bakes the legacy pink key labels (MENU / MOVE MENU / SCREEN SHOT); UIWindow2 splits
        // the window into frame + panel + keyboard layers with the modern orange labels. We never fall
        // back to UIWindow icons — its only extra id is 22 (Monster Book), which v95 dropped.
        _iconRoot = kc2?.Get("icon") as WzProperty;
        _bg  = (kc2?.Get("backgrnd") ?? _kc?.Get("backgrnd")) is WzCanvas bc ? loader.Load(bc) : null;
        _bg2 = kc2?.Get("backgrnd2") is WzCanvas bc2 ? loader.Load(bc2) : null;
        _bg3 = kc2?.Get("backgrnd3") is WzCanvas bc3 ? loader.Load(bc3) : null;
        _panelW = _bg?.Width  ?? 622;
        _panelH = _bg?.Height ?? 374;

        _btClose     = MakeBtn("BtClose",     () => CloseCancel());
        _btHelp      = MakeBtn("BtHelp",      () => { });
        _btOk        = MakeBtn("BtOK",        CloseOk);
        _btCancel    = MakeBtn("BtCancel",    CloseCancel);
        _btDefault   = MakeBtn("BtDefault",   () => _confirm = Confirm.Default);
        _btDelete    = MakeBtn("BtDelete",    () => _confirm = Confirm.Delete);
        _btQuickSlot = MakeBtn("BtQuickSlot", () => OnOpenQuickSlot?.Invoke());

        LoadDefaultMap();
    }

    // ── Public binding API (consumed by GameStage / FieldStage) ──────────────────

    /// <summary>The binding for a pressed key (right modifiers fold to the left), or None.</summary>
    public FuncKeyMapped ForKey(Keys key)
    {
        var sc = KeysToScanCode(key);
        sc = sc switch
        {
            KeyConfigLayout.ScRShift => KeyConfigLayout.ScLShift,
            KeyConfigLayout.ScRCtrl  => KeyConfigLayout.ScLCtrl,
            KeyConfigLayout.ScRAlt   => KeyConfigLayout.ScLAlt,
            _ => sc,
        };
        return sc >= 0 && sc < MapSize ? _map[sc] : FuncKeyMapped.None;
    }

    /// <summary>True if any key for this action (or the client-only arrows) is held.</summary>
    public bool IsActionDown(KeyboardState kb, KeyAction action)
    {
        switch (action)
        {
            case KeyAction.MoveLeft:  return kb.IsKeyDown(Keys.Left);
            case KeyAction.MoveRight: return kb.IsKeyDown(Keys.Right);
            case KeyAction.Jump:
                // Up is reserved for ladders/ropes + portals; jump is Left/Right Alt (v95 default) or the bound key.
                return kb.IsKeyDown(Keys.LeftAlt) || kb.IsKeyDown(Keys.RightAlt)
                    || AnyHeld(kb, FuncKeyType.BasicAction, (int)KeyAction.Jump);
            default:
                if (!TryActionToFuncKey(action, out var fk)) return false;
                return AnyHeld(kb, fk.Type, fk.Id);
        }
    }

    private bool AnyHeld(KeyboardState kb, FuncKeyType type, int id)
    {
        for (var sc = 0; sc < MapSize; sc++)
        {
            if (_map[sc].Type != type || _map[sc].Id != id) continue;
            if (ScanCodeToKeys(sc) is { } key && kb.IsKeyDown(key)) return true;
            // The map only ever stores the LEFT modifier scancode (right mods fold to
            // left at bind/lookup, per CUIKeyConfig::GetShortCutIndexByPos). So a binding
            // on L-Ctrl/Shift/Alt must also fire when the matching RIGHT key is held —
            // e.g. Attack on L-Ctrl works from R-Ctrl too.
            var sibling = sc switch
            {
                KeyConfigLayout.ScLCtrl  => (Keys?)Keys.RightControl,
                KeyConfigLayout.ScLShift => Keys.RightShift,
                KeyConfigLayout.ScLAlt   => Keys.RightAlt,
                _ => null,
            };
            if (sibling is { } rk && kb.IsKeyDown(rk)) return true;
        }
        return false;
    }

    /// <summary>Snapshot the 89-slot map (for disk persistence).</summary>
    public FuncKeyMapped[] ExportMap() => (FuncKeyMapped[])_map.Clone();

    /// <summary>Replace the live map (from disk). A null/short array keeps defaults.</summary>
    public void ImportMap(IReadOnlyList<FuncKeyMapped>? map)
    {
        if (map is null || map.Count == 0) return;
        for (var i = 0; i < MapSize; i++)
            _map[i] = i < map.Count ? map[i] : FuncKeyMapped.None;
    }

    /// <summary>
    /// Apply the full server keymap (<c>FuncKeyMappedInit</c>). Entries are
    /// (scancode, type, id); the array is rebuilt from scratch.
    /// </summary>
    public void ApplyServerKeymap(IEnumerable<(int keyIndex, int type, int actionId)> entries)
    {
        Array.Clear(_map);
        foreach (var (keyIndex, type, actionId) in entries)
        {
            if (keyIndex < 0 || keyIndex >= MapSize) continue;
            if (!Enum.IsDefined(typeof(FuncKeyType), (byte)type)) continue;
            _map[keyIndex] = new FuncKeyMapped((FuncKeyType)type, actionId);
        }
        SnapshotOpen();
    }

    // ── Default map (Kinoko GameConstants.defaultFuncKeyMap, GMS v95) ─────────────
    private static readonly int[] DefIndex =
        { 2, 3, 4, 5, 6, 7, 8, 16, 17, 18, 19, 20, 23, 24, 25, 26, 27, 29, 31, 33, 34, 35, 37, 38, 39, 40, 41, 43, 44, 45, 46, 50, 56, 57, 59, 60, 61, 62, 63, 64, 65 };
    private static readonly int[] DefType =
        { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 4, 4, 5, 5, 6, 6, 6, 6, 6, 6, 6 };
    private static readonly int[] DefId =
        { 10, 12, 13, 18, 24, 21, 29, 8, 5, 0, 4, 28, 1, 25, 19, 14, 15, 52, 2, 26, 17, 11, 3, 20, 27, 16, 23, 9, 50, 51, 6, 7, 53, 54, 100, 101, 102, 103, 104, 105, 106 };

    private void LoadDefaultMap()
    {
        Array.Clear(_map);
        for (var i = 0; i < DefIndex.Length; i++)
            _map[DefIndex[i]] = new FuncKeyMapped((FuncKeyType)DefType[i], DefId[i]);
        SnapshotOpen();
    }

    // ── Window lifecycle ────────────────────────────────────────────────────────

    public void Open()
    {
        IsVisible = true;
        SnapshotOpen();
        CancelDrag();
        _confirm = Confirm.None;
    }

    private void SnapshotOpen() => Array.Copy(_map, _mapOnOpen, MapSize);

    private void CloseOk()
    {
        // Save the diff to the server, persist the full map to disk, then close.
        var changed = new List<(int index, FuncKeyMapped fk)>();
        for (var i = 0; i < MapSize; i++)
            if (_map[i] != _mapOnOpen[i])
                changed.Add((i, _map[i]));
        if (changed.Count > 0) OnSaveToServer?.Invoke(changed);
        OnBindingsChanged?.Invoke();
        SnapshotOpen();
        CloseInternal();
    }

    private void CloseCancel()
    {
        Array.Copy(_mapOnOpen, _map, MapSize);  // revert
        CloseInternal();
    }

    private void CloseInternal()
    {
        IsVisible = false;
        CancelDrag();
        _confirm = Confirm.None;
    }

    private void CancelDrag()
    {
        _dragActive = false;
        _dragFromScancode = -1;
        _windowDrag = false;
    }

    // ── Update (drag tracking) ───────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        LayoutButtons();
        if (!IsVisible) return;

        var m = Mouse.GetState();
        _dragMouse = new Point(m.X, m.Y);

        // Window move is HOLD-to-drag: follow the cursor while the button is held, drop on release.
        if (_windowDrag)
        {
            if (m.LeftButton == ButtonState.Pressed)
                Position = new Vector2(m.X, m.Y) - _windowDragOff;
            else
                _windowDrag = false;
        }
    }

    private void FinishDrag(int sx, int sy)
    {
        var lx = sx - (int)Position.X;
        var ly = sy - (int)Position.Y;
        var sc = KeyConfigLayout.HitTestKey(lx, ly);
        if (sc >= 0)
        {
            // A skill/item/macro lives on exactly one key — clear any prior holder.
            if (_dragIcon.Type is FuncKeyType.Skill or FuncKeyType.Item or FuncKeyType.Effect or FuncKeyType.MacroSkill)
                for (var i = 0; i < MapSize; i++)
                    if (_map[i] == _dragIcon) _map[i] = FuncKeyMapped.None;
            _map[sc] = _dragIcon;
        }
        // Dropped on the palette or outside → stays unbound (already cleared on pickup).
        CancelDrag();
    }

    // ── Draw ──────────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        _white = white;
        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_bg != null)
        {
            // Base frame: top-left at Position (cancel its own origin). The inner panel + keyboard
            // layers position themselves via their negative origins (insets) relative to Position.
            _bg.Draw(sb, Position + _bg.Origin);
            _bg2?.Draw(sb, Position);
            _bg3?.Draw(sb, Position);
        }
        else
        {
            sb.Draw(white, new Rectangle(px, py, _panelW, _panelH), new Color(12, 12, 22, 240));
            _font?.Draw(sb, "Key Configuration", new Vector2(px + 230, py + 6), new Color(220, 200, 150));
        }

        DrawKeyIcons(sb);
        DrawPalette(sb, white);
        foreach (var b in _allButtons) b.Draw(sb);

        // Drag ghost
        if (_dragActive)
        {
            var icon = IconFor(_dragIcon);
            if (icon != null)
                icon.Draw(sb, new Vector2(_dragMouse.X, _dragMouse.Y) + icon.Origin - new Vector2(16, 16));
            else
                DrawPlaceholder(sb, white, _dragMouse.X - 16, _dragMouse.Y - 16, _dragIcon);
        }

        if (_confirm != Confirm.None) DrawConfirm(sb, white, px, py);
    }

    private void DrawKeyIcons(SpriteBatch sb)
    {
        foreach (var sc in KeyConfigLayout.BindableScancodes)
        {
            // Right modifiers mirror their left counterpart's binding.
            var bindSc = sc switch
            {
                KeyConfigLayout.ScRShift => KeyConfigLayout.ScLShift,
                KeyConfigLayout.ScRCtrl  => KeyConfigLayout.ScLCtrl,
                KeyConfigLayout.ScRAlt   => KeyConfigLayout.ScLAlt,
                _ => sc,
            };
            if (bindSc < 0 || bindSc >= MapSize) continue;
            var fk = _map[bindSc];
            if (!fk.IsBound) continue;
            if (!KeyConfigLayout.TryGetCell(sc, out var cell)) continue;
            DrawIconAt(sb, cell, fk);
        }
    }

    private void DrawPalette(SpriteBatch sb, Texture2D white)
    {
        for (var slot = 0; slot < KeyConfigLayout.PaletteCount; slot++)
        {
            var fk = KeyConfigLayout.PaletteBinding(slot);
            // Skip the Monster Book slot — v95 dropped the feature (no UIWindow2 icon for it).
            if (fk.Type == FuncKeyType.Menu && fk.Id == MonsterBookMenuId) continue;
            // Hide a palette icon once it's been placed on a key (matches bMapped).
            if (IsPlaced(fk)) continue;
            // While carrying an icon, hide it from its palette home slot so grabbing one off a key
            // doesn't make it flash into the unused area — it lives only on the cursor until dropped.
            if (_dragActive && fk == _dragIcon) continue;
            var cell = KeyConfigLayout.PaletteCell(slot);
            DrawIconAt(sb, cell, fk);
        }
    }

    private bool IsPlaced(FuncKeyMapped fk)
    {
        for (var i = 0; i < MapSize; i++)
            if (_map[i] == fk) return true;
        return false;
    }

    private void DrawIconAt(SpriteBatch sb, Point cell, FuncKeyMapped fk)
    {
        // Uniform anchor: top-left at Position+cell, regardless of icon origin
        // (CalcKeyIconPosInfo: top-left = pos - origin + (0,32)).
        var anchor = Position + new Vector2(cell.X, cell.Y + 32);
        var icon = IconFor(fk);
        if (icon != null) icon.Draw(sb, anchor);
        else DrawPlaceholder(sb, _white, cell.X, cell.Y, fk);
    }

    // Skill/item/macro icons need their own WZ providers (not wired here yet); show
    // a small typed placeholder so server-sent bindings are still visible/draggable.
    private Texture2D _white = null!;
    private void DrawPlaceholder(SpriteBatch sb, Texture2D white, int x, int y, FuncKeyMapped fk)
    {
        if (white == null) return;
        var pos = new Rectangle((int)Position.X + x, (int)Position.Y + y, 32, 32);
        var c = fk.Type switch
        {
            FuncKeyType.Skill => new Color(60, 90, 150),
            FuncKeyType.Item or FuncKeyType.Effect => new Color(110, 90, 50),
            FuncKeyType.MacroSkill => new Color(90, 60, 110),
            _ => new Color(70, 70, 80),
        };
        sb.Draw(white, pos, c);
        _font?.Draw(sb, fk.Type switch
        {
            FuncKeyType.Skill => "SK", FuncKeyType.Item or FuncKeyType.Effect => "IT",
            FuncKeyType.MacroSkill => "MA", _ => "?",
        }, new Vector2(pos.X + 8, pos.Y + 10), Color.White);
    }

    private WzSprite? IconFor(FuncKeyMapped fk)
    {
        if (fk.Type is FuncKeyType.Menu or FuncKeyType.BasicAction or FuncKeyType.BasicMotion or FuncKeyType.Emotion)
            return LoadIcon(fk.Id);
        return null; // skill/item/macro icons: provider not wired here yet
    }

    private WzSprite? LoadIcon(int id)
    {
        if (_iconCache.TryGetValue(id, out var s)) return s;
        WzSprite? sprite = _iconRoot?.Get(id.ToString()) is WzCanvas canvas ? _loader.Load(canvas) : null;
        _iconCache[id] = sprite;
        return sprite;
    }

    // ── Mouse input ─────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        var inside = new Rectangle((int)Position.X, (int)Position.Y, _panelW, _panelH).Contains(x, y);

        // Confirm overlay is modal while shown.
        if (_confirm != Confirm.None)
        {
            if (down) HandleConfirmClick(x, y);
            return true;
        }

        if (!down)
        {
            // Releases only complete a button click; sticky drags drop on the next press.
            foreach (var b in _allButtons) if (b.HandleMouseButton(x, y, false)) return true;
            return inside;
        }

        // Sticky icon drag: a press while carrying an icon drops it (on a key, the palette, or
        // outside → unbind). Takes precedence over everything else. (Window move is separate —
        // it's hold-to-drag, started below and released on mouse-up in Update.)
        if (_dragActive) { FinishDrag(x, y); return true; }

        // Down: buttons first.
        foreach (var b in _allButtons) if (b.HandleMouseButton(x, y, true)) return true;
        if (!inside) return false;

        var lx = x - (int)Position.X;
        var ly = y - (int)Position.Y;

        // Pick an icon off a key.
        var sc = KeyConfigLayout.HitTestKey(lx, ly);
        if (sc >= 0 && _map[sc].IsBound)
        {
            _dragIcon = _map[sc];
            _dragFromScancode = sc;
            _map[sc] = FuncKeyMapped.None;
            _dragActive = true;
            return true;
        }

        // Pick an icon out of the palette.
        var slot = KeyConfigLayout.HitTestPalette(lx, ly);
        if (slot >= 0)
        {
            var fk = KeyConfigLayout.PaletteBinding(slot);
            // Only grab when the slot's icon is actually shown: skip the (empty) Monster Book slot
            // and any action already placed on a key (its palette cell is visually empty — grabbing
            // it would fabricate a duplicate). Mirrors DrawPalette's skip logic.
            if ((fk.Type == FuncKeyType.Menu && fk.Id == MonsterBookMenuId) || IsPlaced(fk))
                return true;
            _dragIcon = fk;
            _dragFromScancode = -1;
            _dragActive = true;
            return true;
        }

        // Otherwise: drag the window from empty chrome (top strip).
        if (ly < 24)
        {
            _windowDrag = true;
            _windowDragOff = new Vector2(x - Position.X, y - Position.Y);
        }
        return true;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (_confirm != Confirm.None)
        {
            if (key == Keys.Escape) { _confirm = Confirm.None; return true; }
            if (key == Keys.Enter) { ApplyConfirm(); return true; }
            return true;
        }
        if (key == Keys.Escape) { CloseCancel(); return true; }
        return false;  // let bound keys still dispatch while the window is open
    }

    // ── Confirm overlay ──────────────────────────────────────────────────────────

    private void HandleConfirmClick(int x, int y)
    {
        var (yes, no) = ConfirmButtons();
        if (yes.Contains(x, y)) ApplyConfirm();
        else if (no.Contains(x, y)) _confirm = Confirm.None;
    }

    private void ApplyConfirm()
    {
        if (_confirm == Confirm.Default) LoadDefaultMap();
        else if (_confirm == Confirm.Delete) Array.Clear(_map);
        OnBindingsChanged?.Invoke();
        _confirm = Confirm.None;
    }

    private (Rectangle yes, Rectangle no) ConfirmButtons()
    {
        var cx = (int)Position.X + _panelW / 2;
        var cy = (int)Position.Y + _panelH / 2;
        return (new Rectangle(cx - 90, cy + 12, 70, 24), new Rectangle(cx + 20, cy + 12, 70, 24));
    }

    private void DrawConfirm(SpriteBatch sb, Texture2D white, int px, int py)
    {
        _white = white;
        var box = new Rectangle(px + _panelW / 2 - 140, py + _panelH / 2 - 40, 280, 90);
        sb.Draw(white, new Rectangle(px, py, _panelW, _panelH), new Color(0, 0, 0, 120));
        sb.Draw(white, box, new Color(20, 22, 34, 250));
        DrawBorder(sb, white, box, new Color(90, 100, 140));
        var msg = _confirm == Confirm.Default
            ? "Restore the default key layout?"
            : "Clear all key bindings?";
        if (_font != null)
        {
            var sz = _font.Measure(msg);
            _font.Draw(sb, msg, new Vector2(px + (_panelW - (int)sz.X) / 2, box.Y + 16), new Color(230, 220, 200));
        }
        var (yes, no) = ConfirmButtons();
        DrawTextButton(sb, white, yes, "OK");
        DrawTextButton(sb, white, no, "Cancel");
    }

    private void DrawTextButton(SpriteBatch sb, Texture2D white, Rectangle r, string label)
    {
        var hover = r.Contains(_dragMouse);
        sb.Draw(white, r, hover ? new Color(90, 110, 160) : new Color(60, 70, 110));
        DrawBorder(sb, white, r, new Color(120, 130, 170));
        if (_font != null)
        {
            var sz = _font.Measure(label);
            _font.Draw(sb, label, new Vector2(r.X + (r.Width - sz.X) / 2, r.Y + (r.Height - _font.LineHeight) / 2), Color.White);
        }
    }

    // ── Button layout ────────────────────────────────────────────────────────────
    // Bottom band of the lower panel (between the keyboard and the 3-row palette).
    // NOTE: button x/y are best-effort pending an exact CUIKeyConfig read; the
    // window-frame Close/Help sit top-right.
    private void LayoutButtons()
    {
        Place(_btClose, _panelW - 18, 6);
        Place(_btHelp, _panelW - 34, 6);
        Place(_btQuickSlot, 14, 240);
        Place(_btDefault, 120, 240);
        Place(_btDelete, 186, 240);
        Place(_btOk, _panelW - 112, 240);
        Place(_btCancel, _panelW - 60, 240);
    }

    private void Place(Button? b, int x, int y)
    {
        if (b != null) b.Position = Position + new Vector2(x, y);
    }

    private Button? MakeBtn(string name, Action onClick)
    {
        if (_kc?.Get(name) is not WzProperty pr) return null;
        var b = new Button(_loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    // ── Scancode ⇄ Keys ──────────────────────────────────────────────────────────

    private static readonly Dictionary<Keys, int> KeysToSc = BuildKeysToSc();

    private static int KeysToScanCode(Keys k) => KeysToSc.TryGetValue(k, out var sc) ? sc : -1;

    private static Keys? ScanCodeToKeys(int sc) => sc switch
    {
        2 => Keys.D1, 3 => Keys.D2, 4 => Keys.D3, 5 => Keys.D4, 6 => Keys.D5,
        7 => Keys.D6, 8 => Keys.D7, 9 => Keys.D8, 10 => Keys.D9, 11 => Keys.D0,
        12 => Keys.OemMinus, 13 => Keys.OemPlus, 14 => Keys.Back,
        16 => Keys.Q, 17 => Keys.W, 18 => Keys.E, 19 => Keys.R, 20 => Keys.T,
        21 => Keys.Y, 22 => Keys.U, 23 => Keys.I, 24 => Keys.O, 25 => Keys.P,
        26 => Keys.OemOpenBrackets, 27 => Keys.OemCloseBrackets,
        28 => Keys.Enter, 29 => Keys.LeftControl,
        30 => Keys.A, 31 => Keys.S, 32 => Keys.D, 33 => Keys.F, 34 => Keys.G,
        35 => Keys.H, 36 => Keys.J, 37 => Keys.K, 38 => Keys.L,
        39 => Keys.OemSemicolon, 40 => Keys.OemQuotes, 41 => Keys.OemTilde,
        42 => Keys.LeftShift, 43 => Keys.OemPipe,
        44 => Keys.Z, 45 => Keys.X, 46 => Keys.C, 47 => Keys.V, 48 => Keys.B,
        49 => Keys.N, 50 => Keys.M, 51 => Keys.OemComma, 52 => Keys.OemPeriod,
        53 => Keys.OemQuestion, 54 => Keys.RightShift,
        56 => Keys.LeftAlt, 57 => Keys.Space, 58 => Keys.CapsLock,
        59 => Keys.F1, 60 => Keys.F2, 61 => Keys.F3, 62 => Keys.F4, 63 => Keys.F5,
        64 => Keys.F6, 65 => Keys.F7, 66 => Keys.F8, 67 => Keys.F9, 68 => Keys.F10,
        71 => Keys.Home, 73 => Keys.PageUp, 79 => Keys.End, 81 => Keys.PageDown,
        82 => Keys.Insert, 83 => Keys.Delete,
        87 => Keys.F11, 88 => Keys.F12,
        _ => null,
    };

    private static Dictionary<Keys, int> BuildKeysToSc()
    {
        var d = new Dictionary<Keys, int>();
        for (var sc = 0; sc < 145; sc++)
            if (ScanCodeToKeys(sc) is { } k) d[k] = sc;
        d[Keys.RightControl] = KeyConfigLayout.ScRCtrl;
        d[Keys.RightAlt] = KeyConfigLayout.ScRAlt;
        return d;
    }

    // ── KeyAction ⇄ FuncKeyMapped (dispatch glue) ───────────────────────────────

    private static bool TryActionToFuncKey(KeyAction action, out FuncKeyMapped fk)
    {
        var id = (int)action;
        if (id is >= 0 and < 30) { fk = new FuncKeyMapped(FuncKeyType.Menu, id); return true; }
        if (id is >= 50 and <= 54) { fk = new FuncKeyMapped(FuncKeyType.BasicAction, id); return true; }
        fk = FuncKeyMapped.None;
        return false;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
