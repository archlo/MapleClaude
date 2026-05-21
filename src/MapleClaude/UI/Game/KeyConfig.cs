using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Key-binding configuration panel.
/// Displays a visual keyboard grid; clicking a slot enters rebind-mode for that slot;
/// pressing any key assigns it. The Default button restores v95 defaults.
/// Changes take effect immediately (no separate confirm step).
///
/// WZ: UIWindow.img/KeyConfig/
/// </summary>
public sealed class KeyConfig : GamePanel
{
    // ── Key action model ─────────────────────────────────────────────────────
    public enum KeyAction
    {
        None = 0,
        Attack, Jump, PickUp, Interact, BasicAttack,
        Equip, Items, Skills, Stats, Quest, Map,
        Party, Friends, Guild, Chat,
        Skill1, Skill2, Skill3, Skill4, Skill5, Skill6,
        HP_Potion, MP_Potion,
        Macro, Screenshot, CashShop, Menu, Quit,
    }

    private static readonly string[] ActionLabels = [
        "", "ATK", "JUMP", "PICK", "NPC", "ATTK2",
        "EQP", "ITEM", "SKIL", "STAT", "QUST", "MAP",
        "PTY", "FRND", "GILD", "CHAT",
        "SK1", "SK2", "SK3", "SK4", "SK5", "SK6",
        "HPP", "MPP",
        "MCR", "SS", "CS", "MENU", "QUIT",
    ];

    // ── Binding store ────────────────────────────────────────────────────────
    // Key: virtual key code  Value: action assigned
    private readonly Dictionary<Keys, KeyAction> _bindings = new();

    // ── Keyboard grid layout ─────────────────────────────────────────────────
    // Each row is a list of (Keys enum, display label) pairs
    private static readonly (Keys key, string label)[][] KeyRows =
    [
        // Row 0: function keys
        [ (Keys.F1,"F1"),(Keys.F2,"F2"),(Keys.F3,"F3"),(Keys.F4,"F4"),
          (Keys.F5,"F5"),(Keys.F6,"F6"),(Keys.F7,"F7"),(Keys.F8,"F8"),
          (Keys.F9,"F9"),(Keys.F10,"F10"),(Keys.F11,"F11"),(Keys.F12,"F12") ],
        // Row 1: numbers
        [ (Keys.OemTilde,"`"),(Keys.D1,"1"),(Keys.D2,"2"),(Keys.D3,"3"),
          (Keys.D4,"4"),(Keys.D5,"5"),(Keys.D6,"6"),(Keys.D7,"7"),
          (Keys.D8,"8"),(Keys.D9,"9"),(Keys.D0,"0") ],
        // Row 2: QWERTY
        [ (Keys.Tab,"TAB"),(Keys.Q,"Q"),(Keys.W,"W"),(Keys.E,"E"),
          (Keys.R,"R"),(Keys.T,"T"),(Keys.Y,"Y"),(Keys.U,"U"),
          (Keys.I,"I"),(Keys.O,"O"),(Keys.P,"P") ],
        // Row 3: ASDF
        [ (Keys.CapsLock,"CAPS"),(Keys.A,"A"),(Keys.S,"S"),(Keys.D,"D"),
          (Keys.F,"F"),(Keys.G,"G"),(Keys.H,"H"),(Keys.J,"J"),
          (Keys.K,"K"),(Keys.L,"L"),(Keys.Enter,"ENT") ],
        // Row 4: ZXCV + modifiers
        [ (Keys.LeftShift,"SHF"),(Keys.Z,"Z"),(Keys.X,"X"),(Keys.C,"C"),
          (Keys.V,"V"),(Keys.B,"B"),(Keys.N,"N"),(Keys.M,"M"),
          (Keys.LeftControl,"CTR"),(Keys.LeftAlt,"ALT"),(Keys.Space,"SPACE") ],
    ];

    // ── UI ───────────────────────────────────────────────────────────────────
    private readonly WzSprite? _background;
    private readonly Button?   _btClose;
    private readonly Button?   _btDefault;
    private readonly Button?   _btOk;
    private readonly List<Button> _allButtons = new();

    // ── State ────────────────────────────────────────────────────────────────
    // When waiting for a key press to rebind, this is the target
    private Keys? _rebindTarget;

    private const int PanelW = 508;
    private const int PanelH = 360;
    private const int GridX  = 8;
    private const int GridY  = 30;
    private const int SlotW  = 36;
    private const int SlotH  = 28;
    private const int SlotGap = 2;

    private readonly BuiltInFont? _font;

    public KeyConfig(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(146, 110);

        var kc = ui?.GetItem("UIWindow.img/KeyConfig") as WzProperty;
        _background = kc?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        _btClose   = MakeBtn(loader, kc, "BtClose",   () => { IsVisible = false; _rebindTarget = null; });
        _btDefault = MakeBtn(loader, kc, "BtDefault",  ResetToDefault);
        _btOk      = MakeBtn(loader, kc, "BtOK",      () => { IsVisible = false; _rebindTarget = null; });

        ResetToDefault();
        LayoutButtons();
    }

    // ── Defaults ─────────────────────────────────────────────────────────────
    private void ResetToDefault()
    {
        _bindings.Clear();
        _bindings[Keys.LeftControl] = KeyAction.Attack;
        _bindings[Keys.LeftAlt]     = KeyAction.Jump;
        _bindings[Keys.Z]           = KeyAction.PickUp;
        _bindings[Keys.Space]       = KeyAction.Jump;
        _bindings[Keys.A]           = KeyAction.None;    // move left (built-in)
        _bindings[Keys.D]           = KeyAction.None;    // move right
        _bindings[Keys.E]           = KeyAction.Equip;
        _bindings[Keys.I]           = KeyAction.Items;
        _bindings[Keys.K]           = KeyAction.Skills;
        _bindings[Keys.S]           = KeyAction.Stats;
        _bindings[Keys.Q]           = KeyAction.Quest;
        _bindings[Keys.M]           = KeyAction.Map;
        _bindings[Keys.F1]          = KeyAction.HP_Potion;
        _bindings[Keys.F2]          = KeyAction.MP_Potion;
        _bindings[Keys.F3]          = KeyAction.Skill1;
        _bindings[Keys.F4]          = KeyAction.Skill2;
        _bindings[Keys.F5]          = KeyAction.Skill3;
        _bindings[Keys.F6]          = KeyAction.Skill4;
        _bindings[Keys.F7]          = KeyAction.Skill5;
        _bindings[Keys.F8]          = KeyAction.Skill6;
        _bindings[Keys.OemTilde]    = KeyAction.Chat;
    }

    // ── Accessors ─────────────────────────────────────────────────────────────
    public KeyAction GetAction(Keys key) =>
        _bindings.TryGetValue(key, out var a) ? a : KeyAction.None;

    public void SetBinding(Keys key, KeyAction action) => _bindings[key] = action;

    // ── Update ───────────────────────────────────────────────────────────────
    public override void Update(GameTime gameTime) => LayoutButtons();

    // ── Draw ─────────────────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(PanelW / 2f, PanelH / 2f));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(12, 12, 22, 235));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH));
        }

        _font?.Draw(sb, "Key Configuration", new Vector2(px + 180, py + 6), new Color(220, 200, 150));

        if (_rebindTarget.HasValue)
        {
            var msg = $"Press a key for: {SlotLabel(_rebindTarget.Value)}  (ESC = cancel)";
            var sz  = _font?.Measure(msg) ?? Vector2.Zero;
            _font?.Draw(sb, msg, new Vector2(px + (PanelW - (int)sz.X) / 2, py + PanelH - 22), new Color(255, 220, 80));
        }

        DrawKeyGrid(sb, white, px, py);
        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawKeyGrid(SpriteBatch sb, Texture2D white, int px, int py)
    {
        var rowY = py + GridY;
        foreach (var row in KeyRows)
        {
            var slotX = px + GridX;
            foreach (var (key, label) in row)
            {
                var w = label.Length > 2 ? SlotW + 10 : SlotW;
                DrawKeySlot(sb, white, slotX, rowY, w, key, label);
                slotX += w + SlotGap;
            }
            rowY += SlotH + SlotGap;
        }
    }

    private void DrawKeySlot(SpriteBatch sb, Texture2D white,
        int x, int y, int w, Keys key, string keyLabel)
    {
        var isTarget  = _rebindTarget == key;
        var hasBind   = _bindings.TryGetValue(key, out var action) && action != KeyAction.None;
        var slotColor = isTarget   ? new Color(80, 60, 20)
                      : hasBind    ? new Color(30, 40, 55)
                      : new Color(22, 22, 32);
        var borderCol = isTarget   ? new Color(240, 180, 40)
                      : hasBind    ? new Color(60, 90, 130)
                      : new Color(55, 55, 75);

        sb.Draw(white, new Rectangle(x, y, w, SlotH), slotColor);
        DrawBorder(sb, white, new Rectangle(x, y, w, SlotH), borderCol);

        // Key name (top-left, small)
        _font?.Draw(sb, keyLabel, new Vector2(x + 2, y + 2), new Color(140, 140, 180));

        // Action label (bottom, larger)
        if (hasBind)
        {
            var al = ActionLabels[(int)action];
            _font?.Draw(sb, al, new Vector2(x + 2, y + 14), new Color(200, 220, 200));
        }
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        if (!down) return false;

        // Hit-test key slots
        var px = (int)Position.X;
        var py = (int)Position.Y;
        var rowY = py + GridY;
        foreach (var row in KeyRows)
        {
            var slotX = px + GridX;
            foreach (var (key, label) in row)
            {
                var w   = label.Length > 2 ? SlotW + 10 : SlotW;
                var r   = new Rectangle(slotX, rowY, w, SlotH);
                if (r.Contains(x, y))
                {
                    _rebindTarget = key;
                    return true;
                }
                slotX += w + SlotGap;
            }
            rowY += SlotH + SlotGap;
        }

        return new Rectangle(px, py, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;

        if (_rebindTarget.HasValue)
        {
            if (key == Keys.Escape)
            {
                _rebindTarget = null;
            }
            else
            {
                // Assign the pressed key to the currently targeted slot's action
                var oldAction = _bindings.TryGetValue(_rebindTarget.Value, out var a) ? a : KeyAction.None;
                if (_bindings.ContainsKey(key)) _bindings.Remove(key);
                _bindings[key] = oldAction;
                _bindings.Remove(_rebindTarget.Value);
                _rebindTarget = key; // show new assignment
            }
            return true;
        }

        if (key == Keys.Escape) { IsVisible = false; return true; }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string SlotLabel(Keys k)
    {
        foreach (var row in KeyRows)
            foreach (var (key, label) in row)
                if (key == k) return label;
        return k.ToString();
    }

    private void LayoutButtons()
    {
        if (_btClose   != null) _btClose.Position   = Position + new Vector2(PanelW - 20, 4);
        if (_btDefault != null) _btDefault.Position = Position + new Vector2(PanelW - 140, PanelH - 24);
        if (_btOk      != null) _btOk.Position      = Position + new Vector2(PanelW - 60,  PanelH - 24);
    }

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r)
    {
        var c = new Color(60, 60, 80);
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
