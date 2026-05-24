using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

/// <summary>
/// The v95 secondary-password (PIC) soft keyboard — mirrors <c>CSoftKeyboardDlg</c>. A 140×280
/// modal centered on screen. WZ <c>Login.img/Common/SoftKey</c>: <c>backgrnd</c> + <c>backgrnd2/3</c>
/// composite the "SOFT KEYBOARD" title, lock icon, "ENTER YOUR PIC." prompt and the field border.
/// Three tabs (<b>123 / abc / ABC</b>) swap the key set — <c>BtNum</c> digits / <c>BtLowCase</c> /
/// <c>BtHighCase</c> (T9, 3 letters per key, cycled on repeat-press). The 10 key cells are shuffled
/// on open (one permutation shared by all three tabs). <c>BtDel</c> ← / <c>BtNext</c> → / OK / Cancel.
/// PIC length 6–16, masked. The real client is click-only; we additionally allow alphanumeric typing.
/// <para>Exact board-relative offsets from the IDB (CSoftKeyboardDlg::OnCreate / SetButton): edit
/// (12,47,118×14); tabs at (12/52/92, 68 selected / 70 normal); keys at x=39·col+12, y=35·row+94
/// (40×35); Del origin-placed at (51,199), Next at (90,199); OK (14,235), Cancel (72,235).</para>
/// </summary>
public sealed class SoftKeyOverlay : Overlay
{
    public const int MinLength = 6;
    public const int MaxLength = 16;
    private const int BoardW = 140, BoardH = 280;
    private const int TitleBarH = 44;          // top strip used as the drag handle
    private const float BackInitialDelay = 0.35f, BackRepeatInterval = 0.04f;

    private static readonly Vector2 EntryPos = new(12, 47);
    private static readonly Vector2 OkSlot = new(14, 235);
    private static readonly Vector2 CancelSlot = new(72, 235);
    private static Vector2 Cell(int idx) => new(39 * (idx % 3) + 12, 35 * (idx / 3) + 94);
    private static Vector2 TabPos(int t, bool selected) => new(12 + 40 * t, selected ? 68 : 70);

    // T9 letter groups (IDB GetNextSwitchingChar): key k = 'a'+3k … ; k0=abc … k8=yz, k9 empty.
    private static readonly string[] T9 = ["abc", "def", "ghi", "jkl", "mno", "pqr", "stu", "vwx", "yz", ""];

    private readonly BuiltInFont? _font;
    private readonly WzSprite? _bg, _bg2, _bg3;
    private readonly WzSprite?[] _tabNormal = new WzSprite?[3];
    private readonly WzSprite?[] _tabSelected = new WzSprite?[3];
    private readonly Button?[] _numKeys = new Button?[10];
    private readonly Button?[] _lowKeys = new Button?[10];
    private readonly Button?[] _highKeys = new Button?[10];
    private readonly Button? _btDel, _btNext, _btOk, _btCancel;
    private Vector2 _boardTL;
    private Vector2 _homeTL;          // centred resting position; the pad re-centres here on each Show
    private float _screenW, _screenH; // window extents, for clamping the draggable pad on-screen
    private bool _dragging;
    private Vector2 _dragGrab;        // cursor offset within the board when the drag began
    private bool _backDown;           // Backspace held-state, for key-repeat
    private float _backTimer;         // seconds until the next repeat delete

    private readonly int[] _cellForKey = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]; // key k shown at this cell
    private readonly Random _rng = new();
    private int _activeTab;          // 0 digits, 1 lowercase, 2 uppercase
    private string _entered = string.Empty;
    private int _switchKey = -1;     // T9: letter key currently mid-cycle
    private int _switchIdx;          // T9: index within that key's group
    private string _error = string.Empty;
    private string _caption = string.Empty; // re-enter tooltip ("Please re-enter your PIC"); blank otherwise

    public Action<string>? OnSubmit { get; set; }
    public Action? OnCancel { get; set; }

    public SoftKeyOverlay(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, Vector2 screenCenter)
    {
        _font = font;
        _homeTL = screenCenter - new Vector2(BoardW / 2f, BoardH / 2f);
        _boardTL = _homeTL;
        _screenW = screenCenter.X * 2f;
        _screenH = screenCenter.Y * 2f;

        var root = ui?.GetItem("Login.img/Common/SoftKey") as WzProperty;
        _bg = LoadCanvas(loader, root, "backgrnd");
        _bg2 = LoadCanvas(loader, root, "backgrnd2");
        _bg3 = LoadCanvas(loader, root, "backgrnd3");

        var tabNorm = (root?.Get("Tab") as WzProperty)?.Get("normal") as WzProperty;
        var tabSel = (root?.Get("Tab") as WzProperty)?.Get("selected") as WzProperty;
        for (var t = 0; t < 3; t++)
        {
            _tabNormal[t] = LoadCanvas(loader, tabNorm, t.ToString());
            _tabSelected[t] = LoadCanvas(loader, tabSel, t.ToString());
        }
        for (var k = 0; k < 10; k++)
        {
            var key = k; // capture
            _numKeys[k] = MakeKey(loader, root, "BtNum", k, () => TypeDigit(key));
            _lowKeys[k] = MakeKey(loader, root, "BtLowCase", k, () => TypeLetter(key));
            _highKeys[k] = MakeKey(loader, root, "BtHighCase", k, () => TypeLetter(key));
        }
        _btDel = MakeButton(loader, root, "BtDel", DeleteLast);
        _btNext = MakeButton(loader, root, "BtNext", CommitSwitch);
        _btOk = MakeButton(loader, root, "BtOK", Confirm);
        _btCancel = MakeButton(loader, root, "BtCancel", Cancel);
    }

    /// <summary>Opens the pad fresh (digits tab, empty entry, re-shuffled cells).</summary>
    public void Show(string title, Action<string> onSubmit, Action? onCancel = null)
    {
        _caption = title; // optional caption (the re-enter tooltip); the baked "ENTER YOUR PIC." stays put
        _boardTL = _homeTL; // re-centre the pad each time it opens
        _dragging = false;
        _backDown = false;
        _entered = string.Empty;
        _error = string.Empty;
        _activeTab = 0;
        _switchKey = -1;
        OnSubmit = onSubmit;
        OnCancel = onCancel;
        Shuffle();
        Reposition();
        IsVisible = true;
    }

    private Button?[] ActiveKeys() => _activeTab == 1 ? _lowKeys : _activeTab == 2 ? _highKeys : _numKeys;

    // Draw-without-replacement permutation of the 10 cells, shared by all three tabs.
    private void Shuffle()
    {
        var cells = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        for (var key = 0; key < 10; key++)
        {
            var j = _rng.Next(cells.Count);
            _cellForKey[key] = cells[j];
            cells.RemoveAt(j);
        }
    }

    private void Reposition()
    {
        var keys = ActiveKeys();
        for (var k = 0; k < 10; k++)
        {
            if (keys[k] is { } b) b.Position = _boardTL + Cell(_cellForKey[k]);
        }
        // Del/Next carry baked origins (-51,-199)/(-90,-199); placed at the board origin they land
        // at (51,199)/(90,199). OK/Cancel have origin (0,0).
        if (_btDel != null) _btDel.Position = _boardTL;
        if (_btNext != null) _btNext.Position = _boardTL;
        if (_btOk != null) _btOk.Position = _boardTL + OkSlot;
        if (_btCancel != null) _btCancel.Position = _boardTL + CancelSlot;
    }

    private void TypeDigit(int d)
    {
        _switchKey = -1;
        _error = string.Empty;
        if (_entered.Length < MaxLength) _entered += (char)('0' + d);
    }

    // T9: repeat-pressing the same key cycles its group (a→b→c→a, replacing the last char); a
    // different key (or any other input) commits the current char and starts a new one.
    private void TypeLetter(int key)
    {
        var group = T9[key];
        if (group.Length == 0) return; // k9 is the empty no-op key
        _error = string.Empty;
        var upper = _activeTab == 2;
        if (_switchKey == key && _entered.Length > 0)
        {
            _switchIdx = (_switchIdx + 1) % group.Length;
            var ch = group[_switchIdx];
            _entered = _entered[..^1] + (upper ? char.ToUpperInvariant(ch) : ch);
        }
        else
        {
            _switchKey = -1;
            if (_entered.Length >= MaxLength) return;
            _switchKey = key;
            _switchIdx = 0;
            var ch = group[0];
            _entered += upper ? char.ToUpperInvariant(ch) : ch;
        }
    }

    private void CommitSwitch() => _switchKey = -1;   // Next: lock in the current T9 char

    private void DeleteLast()
    {
        _switchKey = -1;
        _error = string.Empty;
        if (_entered.Length > 0) _entered = _entered[..^1];
    }

    private void Confirm()
    {
        if (_entered.Length < MinLength)
        {
            _error = $"Min {MinLength} characters.";
            return;
        }
        IsVisible = false;
        OnSubmit?.Invoke(_entered);
    }

    private void Cancel()
    {
        IsVisible = false;
        OnCancel?.Invoke();
    }

    public override void Update(GameTime gameTime)
    {
        if (!IsVisible) return;
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Hold-to-repeat Backspace. Stage input is edge-triggered (OnKeyPress fires once), so we poll
        // the device here: delete on the press edge, then repeat after an initial delay.
        var back = Keyboard.GetState().IsKeyDown(Keys.Back);
        if (back)
        {
            if (!_backDown) { DeleteLast(); _backTimer = BackInitialDelay; }
            else if ((_backTimer -= dt) <= 0f) { DeleteLast(); _backTimer = BackRepeatInterval; }
        }
        _backDown = back;

        // Drag the pad by its title bar. No mouse-move events reach overlays, so poll the cursor here
        // and follow it until the button releases, keeping the board clamped on-screen.
        if (_dragging)
        {
            var ms = Mouse.GetState();
            if (ms.LeftButton == ButtonState.Pressed)
            {
                _boardTL = new Vector2(ms.X, ms.Y) - _dragGrab;
                _boardTL.X = MathHelper.Clamp(_boardTL.X, 0, _screenW - BoardW);
                _boardTL.Y = MathHelper.Clamp(_boardTL.Y, 0, _screenH - BoardH);
                Reposition();
            }
            else
            {
                _dragging = false;
            }
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        sb.Draw(white, sb.GraphicsDevice.PresentationParameters.Bounds, new Color(0, 0, 0, 150));

        if (_bg != null || _bg2 != null || _bg3 != null)
        {
            _bg?.Draw(sb, _boardTL);    // title baked here
            _bg2?.Draw(sb, _boardTL);
            _bg3?.Draw(sb, _boardTL);   // lock + "ENTER YOUR PIC." + field border baked here
        }
        else
        {
            sb.Draw(white, new Rectangle((int)_boardTL.X, (int)_boardTL.Y, BoardW, BoardH), new Color(40, 40, 48));
        }

        for (var t = 0; t < 3; t++)
        {
            // The tab canvases carry baked origins (tab t ≈ (-(12+40t), -68/-70)); drawn at the
            // board origin they land at (12/52/92, 68 selected / 70 normal) — no offset to add.
            var spr = t == _activeTab ? _tabSelected[t] : _tabNormal[t];
            spr?.Draw(sb, _boardTL);
        }

        if (_font != null)
        {
            var masked = new string('*', _entered.Length);
            _font.Draw(sb, masked, _boardTL + EntryPos + new Vector2(4, 1), new Color(85, 85, 85));
            if (!string.IsNullOrEmpty(_error))
            {
                var ew = _font.Measure(_error).X;
                _font.Draw(sb, _error, _boardTL + new Vector2((BoardW - ew) / 2f, 33), new Color(210, 60, 60));
            }
            if (!string.IsNullOrEmpty(_caption))
            {
                // Re-enter prompt (mirrors CSoftKeyboardDlg::SetTooltip), drawn in the gap between the
                // field (ends y≈61) and the keypad (starts y≈94) so it never collides with the baked text.
                var cw = _font.Measure(_caption).X;
                _font.Draw(sb, _caption, _boardTL + new Vector2((BoardW - cw) / 2f, 64), new Color(70, 70, 70));
            }
        }

        foreach (var b in ActiveKeys()) b?.Draw(sb);
        _btDel?.Draw(sb);
        _btNext?.Draw(sb);
        _btOk?.Draw(sb);
        _btCancel?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (!down && _dragging) { _dragging = false; return true; }
        if (down)
        {
            // The title-bar strip (above the field at y≈47 and tabs at y≈68) is the drag handle.
            if (new Rectangle((int)_boardTL.X, (int)_boardTL.Y, BoardW, TitleBarH).Contains(x, y))
            {
                _dragging = true;
                _dragGrab = new Vector2(x, y) - _boardTL;
                return true;
            }
            for (var t = 0; t < 3; t++)
            {
                var tp = _boardTL + TabPos(t, true);
                if (new Rectangle((int)tp.X, (int)tp.Y, 39, 19).Contains(x, y))
                {
                    if (_activeTab != t) { _activeTab = t; _switchKey = -1; Reposition(); }
                    return true;
                }
            }
        }
        foreach (var b in ActiveKeys())
        {
            if (b?.HandleMouseButton(x, y, down) == true) return true;
        }
        if (_btDel?.HandleMouseButton(x, y, down) == true) return true;
        if (_btNext?.HandleMouseButton(x, y, down) == true) return true;
        if (_btOk?.HandleMouseButton(x, y, down) == true) return true;
        if (_btCancel?.HandleMouseButton(x, y, down) == true) return true;
        return true; // swallow clicks while the modal pad is up
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        switch (key)
        {
            case Keys.Enter: Confirm(); return true;
            case Keys.Escape: Cancel(); return true;
            default: return true;
        }
    }

    public override void OnTextInput(char character)
    {
        if (!IsVisible) return;
        if (character is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
        {
            _switchKey = -1; // typing commits any in-progress T9 char
            _error = string.Empty;
            if (_entered.Length < MaxLength) _entered += character;
        }
    }

    private static WzSprite? LoadCanvas(WzTextureLoader loader, WzProperty? parent, string name)
    {
        var node = parent?.Get(name);
        if (node is WzUol uol) node = uol.Resolve();
        return node is WzCanvas c ? loader.Load(c) : null;
    }

    private static Button? MakeKey(WzTextureLoader loader, WzProperty? root, string set, int k, Action onClick)
        => (root?.Get(set) as WzProperty)?.Get(k.ToString()) is WzProperty pr
            ? new Button(loader, pr) { OnClick = onClick } : null;

    private static Button? MakeButton(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
        => root?.Get(name) is WzProperty pr ? new Button(loader, pr) { OnClick = onClick } : null;
}
